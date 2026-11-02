extends Node
## Offline check of the 1.64 housing model library (pipeline/build_housing.py -> content/housing): loads houses of each
## realm (exteriors and interiors, from the packed .scn with the glTF's converted materials) and a grid of house items
## into a world of their own (no zone fog), prints the node names the client looks up (find_child), the helper nodes,
## the collision rule (every collision_ mesh under a collidee node, no material) and any texture that would draw white,
## then screenshots each one. A material swap by node (roof / material / door) and the porch off is shot too.
##   --skip-login --zone=1 --world-mode=1.65 --probe=housing_models_probe --out=<dir>
const DIR := "res://content/housing"
const EXT_NAMES := ["collisionswitch", "action001", "door", "collidee", "porchcollision", "options", "garden", "porch",
		"porchsupports", "tent", "banners", "shields", "emblem", "visible", "material", "shutters", "roof", "supports",
		"windows", "smokepoint01", "gpoint0", "mpoint*"]
const INT_NAMES := ["action001", "door", "anim001", "collidee", "lights", "options", "banners", "carpets",
		"collectionbox", "floors", "walls", "shields", "emblem", "entrypoint", "bpoint*", "vpoint*", "tpoint*",
		"mpoint*", "visible", "floorplane*", "wallplane*"]
const HOUSES := ["a_phouseext1", "m_phouseext2", "h_phouseext3", "a_phouseext4", "a_phouseint1", "m_phouseint2",
		"h_phouseint4", "m_phouseext4", "h_phouseint1"]
const ITEMS := ["ext_bench_01", "ext_fountain_01", "ext_garden_03", "ext_tree_01", "ext_arch_01", "ext_hedge_01",
		"int_floor_chair_01", "int_floor_bed_01", "int_floor_bookshelf_01", "int_wall_paint_alb_01", "int_wall_tapestry_01",
		"int_wall_torch_01", "aphvault", "hphbindstone", "tool_forge", "tradesign_clothwork_use", "alot", "mlot", "hlot",
		"vaultmarker", "bindstonemarker", "merchantmarker", "4sale_sign", "int_floor_trophy_skeleton_01"]

var mats: NifMaterials
var vp: SubViewport
var world: Node3D
var cam: Camera3D
var out := "user://"
var problems := 0


func _ready() -> void:
	var v := get_tree().current_scene
	out = String(v.args.get("out", "user://"))
	mats = NifMaterials.new({"original": DIR + "/textures", "x4plus": DIR + "/textures/x4plus"}, "x4plus")
	vp = SubViewport.new()
	vp.size = Vector2i(1280, 720)
	vp.own_world_3d = true
	vp.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	add_child(vp)
	world = Node3D.new()
	vp.add_child(world)
	var env := WorldEnvironment.new()
	env.environment = Environment.new()
	env.environment.background_mode = Environment.BG_COLOR
	env.environment.background_color = Color(0.32, 0.38, 0.46)
	env.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.environment.ambient_light_color = Color(0.6, 0.6, 0.6)
	vp.add_child(env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-50, 35, 0)
	vp.add_child(sun)
	cam = Camera3D.new()
	cam.far = 2000.0
	cam.fov = 55.0
	vp.add_child(cam)
	cam.current = true
	await get_tree().create_timer(2.0).timeout
	var data: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(DIR + "/housing.json"))
	print("[housing] housing.json: %d houses, %d textures, %d items, markers %s, effects %s" % [data.houses.size(),
			data.textures.size(), data.items.size(), str(data.markers), str(data.effects)])
	for h in HOUSES:
		var node := _load("houses/" + h)
		if node == null:
			continue
		world.add_child(node)
		_check_house(h, node)
		await _shot(node, h, h.contains("int"))
		node.queue_free()
	# a material swap by node, as HouseView will do it: roof 102, wall 152, door 5, shutters / supports wood 57, no porch
	var hs := _load("houses/a_phouseext1")
	var tx: Dictionary = data.textures
	_swap(hs, "roof", "ph_albionroof", String(tx["102"]))
	_swap(hs, "material", "ph_albionwall", String(tx["152"]))
	_swap(hs, "door", "ph_door", String(tx["5"]))
	_swap(hs, "shutters", "ph_wood", String(tx["57"]))
	_swap(hs, "supports", "ph_wood", String(tx["57"]))
	hs.find_child("porch", true, false).visible = false
	world.add_child(hs)
	await _shot(hs, "a_phouseext1_swapped", false)
	hs.queue_free()
	# the items, a grid, each fitted to a 2.4 m cell
	var grid := Node3D.new()
	world.add_child(grid)
	var cols := 6
	for i in ITEMS.size():
		var it := _load("items/" + ITEMS[i])
		if it == null:
			continue
		_check_item(ITEMS[i], it)
		var box := _box(it)
		var s := 2.0 / maxf(maxf(box.size.x, box.size.y), maxf(box.size.z, 0.01))
		var holder := Node3D.new()
		holder.position = Vector3((i % cols) * 3.0, -(i / cols) * 3.2, 0)
		holder.scale = Vector3.ONE * s
		holder.add_child(it)
		it.position = -box.get_center() + Vector3(0, 0, 0)
		grid.add_child(holder)
		var lbl := Label3D.new()
		lbl.text = ITEMS[i]
		lbl.font_size = 22
		lbl.pixel_size = 0.004
		lbl.position = holder.position + Vector3(0, -1.35, 0.6)
		grid.add_child(lbl)
	await _shot(grid, "items_grid", false, Vector3(0, 0.1, 1))
	print("[housing] done, problems %d, shots in %s" % [problems, ProjectSettings.globalize_path(out)])
	get_tree().quit()


## The packed .scn (node tree as the client will instance it) with the glTF's converted materials on its surfaces.
func _load(rel: String) -> Node3D:
	var gltf := DIR + "/" + rel + "/model.gltf"
	var scn := DIR + "/" + rel + "/model.scn"
	var j = JSON.parse_string(FileAccess.get_file_as_string(gltf))
	if j == null:
		print("[housing] FAIL cannot read ", gltf)
		problems += 1
		return null
	var specs: Array = (j as Dictionary).get("extras", {}).get("nif_materials", [])
	var root: Node3D
	if FileAccess.file_exists(scn):
		root = (load(scn) as PackedScene).instantiate() as Node3D
	else:
		print("[housing] no .scn for ", rel, " - the glTF")
		var doc := GLTFDocument.new()
		var st := GLTFState.new()
		doc.append_from_file(gltf, st)
		root = doc.generate_scene(st) as Node3D
	var white := 0
	for mi in root.find_children("*", "MeshInstance3D", true, false):
		var m := mi as MeshInstance3D
		var n := String(m.name)
		if n.begins_with("collision_") or n.begins_with("helper_") or n.begins_with("climb_"):
			m.visible = false
			continue
		for s in m.mesh.get_surface_count():
			var src := m.mesh.surface_get_material(s)
			var nm := src.resource_name if src else ""
			if not nm.begins_with("nifmat:") or int(nm.substr(7)) >= specs.size():
				print("[housing] FAIL %s/%s surface %d: no converted material (%s)" % [rel, n, s, nm])
				problems += 1
				continue
			var spec: Dictionary = specs[int(nm.substr(7))]
			for slot in (spec.get("slots", {}) as Dictionary).values():
				if not FileAccess.file_exists(DIR + "/textures/" + String(slot.tex) + ".png"):
					print("[housing] FAIL %s/%s: texture %s missing" % [rel, n, slot.tex])
					problems += 1
			if not (spec.get("slots", {}) as Dictionary).has("base"):
				white += 1
			m.set_surface_override_material(s, mats.material(spec))
			m.set_meta("spec_%d" % s, spec)
	if white:
		print("[housing] note %s: %d untextured drawn surfaces (material colour only)" % [rel, white])
	return root


func _check_house(h: String, root: Node3D) -> void:
	var names: Array = INT_NAMES if h.contains("int") else EXT_NAMES
	var found := []
	var missing := []
	for nm in names:
		var c := root.find_child(nm, true, false)
		if c:
			found.append("%s@%s" % [c.name, str((c as Node3D).global_position.snapped(Vector3.ONE * 0.01))] if nm in ["smokepoint01", "entrypoint", "gpoint0", "mpoint*"] else String(c.name))
		else:
			missing.append(nm)
	var all := root.find_children("*", "", true, false)
	var gp := all.filter(func(n): return String(n.name).begins_with("gpoint")).size()
	var cols := 0
	var bad := 0
	for mi in root.find_children("collision_*", "MeshInstance3D", true, false):
		cols += 1
		var p: Node = mi.get_parent()
		var under := false
		while p != null and p != root:
			if String(p.name).begins_with("collidee"):
				under = true
			p = p.get_parent()
		var mat := (mi as MeshInstance3D).mesh.surface_get_material(0)
		if not under or (mat != null and mat.resource_name.begins_with("nifmat:")):
			if bad == 0:
				print("[housing]   bad collision %s: under collidee %s, material %s" % [mi.get_path(), under, mat])
			bad += 1
	var helpers := root.find_children("helper_*", "MeshInstance3D", true, false).size()
	var drawn := root.find_children("*", "MeshInstance3D", true, false).size() - cols - helpers
	var pc := root.find_child("porchcollision", true, false)
	print("[housing] %s: %d nodes, %d drawn meshes, %d collision meshes (%d not under a collidee / with a material), %d helper meshes, %d gpoints, porchcollision collision meshes %d" % [
			h, all.size(), drawn, cols, bad, helpers, gp, pc.find_children("collision_*", "MeshInstance3D", true, false).size() if pc else -1])
	print("[housing]   found ", ", ".join(found))
	if not missing.is_empty():
		print("[housing]   MISSING ", missing)
	if bad:
		problems += 1


func _check_item(n: String, root: Node3D) -> void:
	var meshes := root.find_children("*", "MeshInstance3D", true, false)
	var drawn := meshes.filter(func(m): return m.visible).size()
	if drawn == 0:
		print("[housing] FAIL item %s draws nothing" % n)
		problems += 1


## Every drawn surface under `node` whose base texture starts with `prefix` gets `tex` instead (ph_textures row).
func _swap(root: Node3D, node: String, prefix: String, tex: String) -> void:
	var at := root.find_child(node, true, false)
	var n := 0
	for mi in at.find_children("*", "MeshInstance3D", true, false):
		var m := mi as MeshInstance3D
		for s in m.mesh.get_surface_count():
			if not m.has_meta("spec_%d" % s):
				continue
			var spec: Dictionary = (m.get_meta("spec_%d" % s) as Dictionary).duplicate(true)
			var base: Dictionary = spec.get("slots", {}).get("base", {})
			if String(base.get("tex", "")).begins_with(prefix):
				base["tex"] = tex
				m.set_surface_override_material(s, mats.material(spec))
				n += 1
	print("[housing] swap %s -> %s on %d surfaces" % [node, tex, n])


func _box(root: Node3D) -> AABB:
	var box := AABB()
	var first := true
	for mi in root.find_children("*", "MeshInstance3D", true, false):
		var m := mi as MeshInstance3D
		if not m.visible:
			continue
		var t := Transform3D.IDENTITY
		var p: Node = m
		while p != root and p is Node3D:
			t = (p as Node3D).transform * t
			p = p.get_parent()
		var b: AABB = t * m.get_aabb()
		box = b if first else box.merge(b)
		first = false
	return box


func _shot(node: Node3D, tag: String, interior: bool, dir := Vector3(0.75, 0.55, 1.0)) -> void:
	await get_tree().process_frame
	var box: AABB = node.global_transform * _box(node)
	var c := box.get_center()
	var r := box.size.length() * 0.5
	var d := dir.normalized()
	if interior:
		d = Vector3(0.35, 1.0, 0.6).normalized()        # from above: the walls' backs are not drawn
	cam.global_position = c + d * r * (1.25 if interior else 1.9)
	cam.look_at(c)
	for i in 4:
		await RenderingServer.frame_post_draw
	vp.get_texture().get_image().save_png("%s/%s.png" % [out, tag])
	print("[housing] shot ", tag)
