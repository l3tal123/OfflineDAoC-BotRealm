extends Node
## Offline check of the housing client (2026-11-02) before a live run: the server's packets as DaocClient decodes them
## fed to Housing / HouseView / HousingWindows in any zone - a house built in front of you (materials, porch), its
## garden, going in (interior, door, carpets), furniture on its floor and walls, the hookpoint markers, a material
## preview - and every housing window with sample data. Shots of each step; [hflow] lines (node checks).
##   --skip-login --zone=N --world-mode=1.65 --ui-test --probe=housing_flow_probe --out=<dir> [--model=1]

const HousingScript := preload("res://game/housing/housing.gd")
const HouseViewScript := preload("res://game/housing/house_view.gd")
const WindowsScript := preload("res://game/housing/housing_windows.gd")

var v: Node
var out := ""
var housing: Node
var view: Node3D
var wins: Node
var base := Vector3.ZERO                       # where the house stands (scene)
const HX := 500000.0                           # the fake lot's region place
const HY := 500000.0
const HZ := 3000.0


## The fake world HouseView needs: region units -> scene around `base`, and the pick layer.
class FakeWorld extends Node3D:
	const OBJECT_PICK := 1 << 11
	var origin := Vector3.ZERO

	func scene_pos(gx: float, gy: float, gz: float) -> Vector3:
		return origin + Vector3((gx - HX) * 0.025, (gz - HZ) * 0.025, (gy - HY) * 0.025)


func _shot(tag: String, ui_on := false) -> void:
	v.ui.root.visible = ui_on                     # (world shots without the interface in the way)
	await get_tree().create_timer(1.2).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png("%s/%s.png" % [out, tag])


func _look(cam: Camera3D, from: Vector3, at: Vector3) -> void:
	cam.global_position = from
	cam.look_at(at)


func _ready() -> void:
	v = get_tree().current_scene
	out = String(v.args.get("out", "user://"))
	await get_tree().create_timer(6.0).timeout
	var model := int(v.args.get("model", "1"))
	if v.args.has("at"):                         # (an open spot: --at=x,z scene metres)
		var a := String(v.args.at).split(",")
		v.player.global_position = Vector3(float(a[0]), v.zone.height_at(float(a[0]), float(a[1])) + 1.0, float(a[1]))
		await get_tree().create_timer(4.0).timeout
	var p: Vector3 = v.player.global_position
	var fwd: Vector3 = -(v.player.global_transform.basis.z * Vector3(1, 0, 1)).normalized()
	base = p + fwd * 30.0
	if v.args.has("at"):                         # (the house on the open spot itself)
		base = Vector3(p.x, 0, p.z)
	base.y = v.zone.height_at(base.x, base.z)
	var world := FakeWorld.new()
	world.origin = base
	v.add_child(world)
	housing = HousingScript.new()
	v.add_child(housing)
	view = HouseViewScript.new()
	v.add_child(view)
	view.setup(world, housing)
	wins = WindowsScript.new()
	v.add_child(wins)
	wins.setup(v.ui, housing, view)
	v.player.visible = false
	var cam := Camera3D.new()
	cam.far = 2000.0
	v.add_child(cam)
	cam.current = true
	var n := 7
	housing.on_packet("house", {"number": n, "x": HX, "y": HY, "z": HZ, "heading": 30, "model": model, "roof": 0,
			"wall": 0, "door": 0, "truss": 0, "porch_material": 0, "window": 0, "porch": false, "banner": false,
			"shield": false, "emblem": 0, "porch_roof_color": 0, "name": "Housetest"})
	var have: Dictionary = view.exteriors.get(n, {})
	print("[hflow] exterior built: %s" % (not have.is_empty()))
	if have.is_empty():
		get_tree().quit()
		return
	var sc: Node = have.scene
	for nm in ["roof", "material", "door", "supports", "shutters", "porchsupports", "porch", "banners", "shields",
			"action001", "gpoint0", "smokepoint01"]:
		print("[hflow]   part %-14s %s" % [nm, "found" if view._helper(sc, nm) else "MISSING"])
	var walls: StaticBody3D = (have.node as Node3D).get_node("Walls")
	var floors: StaticBody3D = (have.node as Node3D).get_node("Floors")
	print("[hflow]   collision: walls %d shapes, porch %d, floors %d; door pick %s" % [walls.get_child_count(),
			(have.node as Node3D).get_node("PorchWalls").get_child_count(), floors.get_child_count(),
			(have.node as Node3D).get_node_or_null("DoorPick") != null])
	_look(cam, base + Vector3(-14, 9, 16), base + Vector3(0, 3, 0))
	await _shot("1_house_plain")
	housing.on_packet("house", {"number": n, "x": HX, "y": HY, "z": HZ, "heading": 30, "model": model, "roof": 2,
			"wall": 1, "door": 6, "truss": 8, "porch_material": 3, "window": 5, "porch": true, "banner": false,
			"shield": false, "emblem": 0, "porch_roof_color": 0, "name": "Housetest"})
	await _shot("2_house_materials_porch")
	_look(cam, base + Vector3(16, 9, -14), base + Vector3(0, 3, 0))
	await _shot("3_house_other_side")
	# the garden: a few 1.64 garden objects on spots the model has
	var items := []
	var garden_ids := []
	for id in (view.data.get("items", {}) as Dictionary):
		var it: Dictionary = view.data.items[id]
		if String(it.get("model", "")).begins_with("ext_") and garden_ids.size() < 4:
			garden_ids.append(int(id))
	var spots := []
	for k in 256:
		if view.has_garden_point(n, k):
			spots.append(k)
	for i in garden_ids.size():
		items.append({"key": i, "model": garden_ids[i], "position": spots[(i * 37 + 11) % spots.size()], "rotation": i % 4})
	housing.on_packet("garden", {"number": n, "full": true, "items": items})
	var gnode: Node = (have.node as Node3D).get_node_or_null("Garden")
	print("[hflow] garden: %d spots on the lot, items %s, drawn %d" % [spots.size(), str(garden_ids),
			gnode.get_child_count() if gnode else -1])
	if gnode and gnode.get_child_count() > 0:
		var g0 := gnode.get_child(0) as Node3D
		_look(cam, g0.global_position + Vector3(4, 3, 4), g0.global_position)
		await _shot("4b_garden_item")
	_look(cam, base + Vector3(0, 28, 1), base)
	await _shot("4_garden_top")
	# the windows: External Options with sample menus (the server's housing_outdoor_menu rows as ItemDb runtime items)
	var pages := {0: []}
	var sample := [[59, "Commoner Roof", 0], [59, "Burgess Roof", 1], [59, "Noble Roof", 2], [60, "Commoner Wall", 0],
			[60, "Noble Wall", 2], [61, "Oak Door", 0], [61, "Iron Door", 4], [63, "Sand Wood", 0], [63, "Onyx Wood", 9],
			[64, "Sand Shutters", 0], [62, "Sand Porch", 0], [56, "Red Awning", 1], [57, "Guild Banner", 1], [58, "Guild Shield", 1]]
	for i in sample.size():
		var id := "srvm:0:%d" % i
		ItemDb.add_runtime(id, {"id": id, "name": sample[i][1], "object_type": sample[i][0], "dps_af": sample[i][2],
				"price": 50000 * (i + 1), "level": 1})
		pages[0].append(id)
	wins._options_house = n
	wins.merchant(4, pages)
	wins._step(0, 1)
	wins._step(1, 1)
	_look(cam, base + Vector3(-14, 9, 16), base + Vector3(0, 3, 0))
	await _shot("5_ext_options_preview", true)
	wins._close_options()
	wins._open_garden()
	await _shot("6_garden_window", true)
	wins.windows.garden.hide_window()
	# inside
	housing.on_packet("house_enter", {"number": n, "x": HX, "y": HY, "heading": 0, "model": model, "door": 2,
			"rugs": [1, 0, 0, 0], "banner": false, "shield": false, "emblem": 0})
	print("[hflow] interior built: %s" % (view.interior != null))
	var isc: Node = view.interior.get_meta("scene") if view.interior else null
	var planes := {"wall": 0, "floor": 0}
	if isc:
		for node in isc.find_children("*", "Node3D", true, false):
			var nm := String(node.name)
			if nm.begins_with("wallplane"):
				planes.wall += 1
			elif nm.begins_with("floorplane"):
				planes.floor += 1
		print("[hflow]   planes %s, entrypoint %s" % [str(planes), view._helper(isc, "entrypoint") != null])
	var ep: Node3D = view._helper(isc, "entrypoint") if isc else null
	var inside_at: Vector3 = ep.global_position if ep else view.interior.global_position
	# furniture: two floor items and a wall item on the first planes
	var floor_ids := []
	var wall_ids := []
	for id in (view.data.get("items", {}) as Dictionary):
		var m := String(view.data.items[id].get("model", ""))
		if m.begins_with("int_floor") and floor_ids.size() < 2:
			floor_ids.append(int(id))
		elif m.begins_with("int_wall") and wall_ids.size() < 1:
			wall_ids.append(int(id))
	var fp: Node3D = view._plane(1, 1)
	var wp: Node3D = view._plane(0, 1)
	var fl := []
	if fp:
		var c: Vector3 = HouseViewScript.local_to_nif(fp.global_transform.affine_inverse() * inside_at)
		fl.append({"index": 0, "model": floor_ids[0], "x": int(c.x), "y": int(c.y) + 60, "rotation": 45, "size": 100, "position": 1, "mode": 1})
		fl.append({"index": 1, "model": floor_ids[1], "x": int(c.x) + 60, "y": int(c.y) + 60, "rotation": 0, "size": 100, "position": 1, "mode": 1})
	if wp and not wall_ids.is_empty():
		var mid: Vector3 = Vector3.ZERO
		for mi in wp.find_children("*", "MeshInstance3D", true, false):
			mid = (mi as MeshInstance3D).global_transform * (mi as MeshInstance3D).get_aabb().get_center()
		var wl: Vector3 = HouseViewScript.local_to_nif(wp.global_transform.affine_inverse() * mid)
		fl.append({"index": 2, "model": wall_ids[0], "x": int(wl.x), "y": int(wl.y), "rotation": 0, "size": 100, "position": 1, "mode": 0})
	housing.on_packet("furniture", {"number": n, "full": true, "items": fl})
	print("[hflow]   furniture: %d placed (floor plane %s, wall plane %s)" % [fl.size(), fp != null, wp != null])
	housing.on_packet("house_points", {"number": n})
	_look(cam, inside_at + Vector3(0, 2.2, -0.5), inside_at + Vector3(0, 1.0, 4.0))
	await _shot("7_inside_entry")
	_look(cam, inside_at + Vector3(0, 2.2, 4.0), inside_at + Vector3(0, 0.8, 0))
	await _shot("8_inside_back")
	_look(cam, view.interior.global_position + Vector3(0, 25, 0.1), view.interior.global_position)
	await _shot("9_inside_top")
	# the inside windows
	wins._open_permissions(n, [{"level": 1, "enter": 1, "vault1": 4, "vault2": 0, "vault3": 0, "vault4": 0,
			"appearance": 0, "interior": 1, "garden": 0, "banish": 0, "merchants": 1, "tools": 0, "bind": 1}])
	await _shot("10_permissions", true)
	wins.windows.perms.hide_window()
	wins._open_users(n, [{"slot": 0, "type": 1, "level": 1, "name": "Friendlyguy"}, {"slot": 1, "type": 2, "level": 5, "name": "The Guild"}])
	await _shot("11_permissions_list", true)
	wins.windows.users.hide_window()
	wins._open_rotate(n, 0)
	await _shot("12_rotate", true)
	wins.windows.rotate.hide_window()
	wins._open_money("gold")
	await _shot("13_money_selector", true)
	print("[hflow] shots in ", out)
	get_tree().quit()
