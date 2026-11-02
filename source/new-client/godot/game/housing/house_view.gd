class_name HouseView
extends Node3D
## Player housing drawn from the server's packets (Housing), as the 1.64 client drew it - its own models only (owner
## 2026-11-02: "i only want the real 1.65 housing areas and models"): content/housing (pipeline/build_housing.py) has
## the 12 houses' exteriors and interiors with their NIF node names kept, the PH_* material textures and the 1.64
## house items (housing.json). game.dll (research 2026-11-02, scratchpad housing/assets.md 2.4-2.6):
## - a house stands at the lot's X / Y / Z, turned by its heading (degrees) like a zone fixture;
## - the six material bytes go on named parts: roof -> "roof" (ph_houses Roof Start + n), wall -> "material" (Wall
##   Start), door -> "door" (Door Start), truss -> "supports", porch -> "porchsupports", window -> "shutters" (Wood
##   Start) - ph_textures row -> its texture on the part's meshes;
## - flags: the porch ("porch", its collision "porchcollision"), the outdoor guild banner / shield ("banners",
##   "shields"); the garden: an item per GPoint<n> (its Position byte);
## - the interior is its own model at the house's X / Y and Z 25000, never turned; the furniture sits at interior
##   X / Y (units from the house), turned by its Rotation (degrees), scaled by its Size %.

const ROOT := "res://content/housing"
const UNIT := 0.025
const INTERIOR_Z := 25000.0
## which ph_textures range a part takes, and the word its default texture carries (a part's subtree can hold other
## textures - the roof its wooden beams): only those are swapped; a part with none of them takes it on every mesh
const SLOTS := {"roof": ["roof_start", "roof"], "material": ["wall_start", "wall"], "door": ["door_start", "door"],
		"supports": ["wood_start", "wood"], "porchsupports": ["wood_start", "wood"], "shutters": ["wood_start", "wood"]}
const PACKET_FIELD := {"roof": "roof", "material": "wall", "door": "door", "supports": "truss",
		"porchsupports": "porch_material", "shutters": "window"}

var world: Node                               # ServerWorld (scene_pos, picks)
var housing: Node                             # Housing
var data := {}                                # housing.json (shared: _housing_data)
var exteriors := {}                           # house number -> {node, model, garden: Node3D}
var interior: Node3D                          # the house you are in
var interior_number := 0
static var _tex_cache := {}


func setup(w: Node, h: Node) -> void:
	world = w
	housing = h
	data = _housing_data()
	if data.is_empty():
		push_warning("housing: no %s/housing.json - houses are not drawn" % ROOT)
	h.house_changed.connect(_house_changed)
	h.house_removed.connect(_house_removed)
	h.garden_changed.connect(_garden_changed)
	h.entered.connect(_entered)
	h.left.connect(_left)
	h.furniture_changed.connect(_furniture_changed)
	h.points_toggled.connect(func(n): _hookpoints(n))


# ---------------------------------------------------------------- outside

func _house_changed(n: int) -> void:
	var h: Dictionary = housing.house(n)
	if data.is_empty() or not h.has("model"):
		return
	var model := int(h.model)
	var have: Dictionary = exteriors.get(n, {})
	if model <= 0:                                    # an owned lot without a house: the lot marker shows instead
		_house_removed(n)
		return
	if have.is_empty() or int(have.model) != model:
		_house_removed(n)
		var node := _load(_house_file(model, "exterior"))
		if node == null:
			return
		var holder := Node3D.new()
		holder.name = "House%04d" % n
		add_child(holder)
		holder.add_child(node)
		have = {"node": holder, "model": model, "scene": node}
		exteriors[n] = have
		_collide(node, holder, n)
	var holder: Node3D = have.node
	holder.position = world.scene_pos(float(h.x), float(h.y), float(h.z))
	holder.rotation = Vector3(0, deg_to_rad(-float(h.heading)), 0)
	_apply_materials(have.scene, model, h)
	_show(have.scene, "porch", bool(h.get("porch", false)))
	_show(have.scene, "banners", bool(h.get("banner", false)))
	_show(have.scene, "shields", bool(h.get("shield", false)))
	_set_porch_collision(holder, bool(h.get("porch", false)))
	_garden_changed(n)


func _house_removed(n: int) -> void:
	var have: Dictionary = exteriors.get(n, {})
	if not have.is_empty() and is_instance_valid(have.node):
		(have.node as Node).queue_free()
	exteriors.erase(n)


func _garden_changed(n: int) -> void:
	var have: Dictionary = exteriors.get(n, {})
	if have.is_empty():
		return
	var holder: Node3D = have.node
	var old := holder.get_node_or_null("Garden")
	if old:
		old.free()
	var garden := Node3D.new()
	garden.name = "Garden"
	holder.add_child(garden)
	var h: Dictionary = housing.house(n)
	for key in h.get("garden", {}):
		var e: Dictionary = h.garden[key]
		var spot := _helper(have.scene, "gpoint%d" % int(e.position))
		if spot == null:
			continue
		var item := _item(int(e.model))
		if item == null:
			continue
		item.transform = holder.global_transform.affine_inverse() * spot.global_transform
		# the rotation byte: quarter turns (the 1.64 garden window turns an item a quarter at a time - to verify)
		item.rotate_y(deg_to_rad(-90.0 * float(int(e.rotation) % 4)))
		garden.add_child(item)
		_pick_body(item, {"house": n, "position": int(e.position)}, "house_garden")


# ---------------------------------------------------------------- inside

func _entered(n: int) -> void:
	_left(interior_number)
	var h: Dictionary = housing.house(n)
	var model := int(housing.interior.get("model", h.get("model", 0)))
	if data.is_empty() or model <= 0:
		return
	var node := _load(_house_file(model, "interior"))
	if node == null:
		return
	interior = Node3D.new()
	interior.name = "Interior%04d" % n
	add_child(interior)
	interior.add_child(node)
	interior_number = n
	var x := float(housing.interior.get("x", h.get("x", 0)))
	var y := float(housing.interior.get("y", h.get("y", 0)))
	interior.position = world.scene_pos(x, y, INTERIOR_Z)     # never turned (the server's interior is axis-aligned)
	interior.set_meta("scene", node)
	_collide(node, interior, n)
	_apply_slot(node, "door", int(_row(model).get("door_start", 1)) + int(housing.interior.get("door", 0)))
	_furniture_changed(n)
	_hookpoints(n)


func _left(n: int) -> void:
	if interior and is_instance_valid(interior):
		interior.queue_free()
	interior = null
	interior_number = 0


func _furniture_changed(n: int) -> void:
	if interior == null or n != interior_number:
		return
	var old := interior.get_node_or_null("Furniture")
	if old:
		old.free()
	var box := Node3D.new()
	box.name = "Furniture"
	interior.add_child(box)
	for idx in housing.furniture:
		var e: Dictionary = housing.furniture[idx]
		var item := _item(int(e.model))
		if item == null:
			continue
		# its X / Y are in the placement surface's own frame (1.64 game.dll 0x44d060: the hit point in the picked
		# Wallplane<n> / Floorplane<n> node's space; its Position byte = n): on that node, turned by its Rotation
		var mode := int(e.get("mode", 1))
		var plane := _plane(mode, int(e.get("position", 0)))
		box.add_child(item)
		if plane:
			item.global_position = plane.global_transform * nif_to_local(Vector3(float(e.x), float(e.y), 0.0))
			if mode == 0:                                  # on a wall: the wall's own facing
				item.global_basis = plane.global_basis.orthonormalized()
		else:                                              # (no such surface: house units from its X / Y)
			item.position = Vector3(float(e.x) * UNIT, 0.0, float(e.y) * UNIT)
		if mode == 1:
			item.rotation.y = deg_to_rad(-float(e.rotation))
		item.scale = Vector3.ONE * clampf(float(e.get("size", 100)) / 100.0, 0.1, 4.0)
		item.set_meta("furniture_index", int(idx))
		if mode == 1:
			_drop_to_floor(item)
		_pick_body(item, {"house": n, "index": int(idx), "mode": int(e.get("mode", 1))}, "house_furniture")


func _drop_to_floor(item: Node3D) -> void:
	var from := item.global_position + Vector3(0, 6, 0)
	var q := PhysicsRayQueryParameters3D.create(from, from - Vector3(0, 30, 0), ZoneLoader.LAYER_FLOOR)
	var hit := get_world_3d().direct_space_state.intersect_ray(q)
	if not hit.is_empty():
		item.global_position.y = float(hit.position.y)


# ---------------------------------------------------------------- models

func _row(model: int) -> Dictionary:
	return (data.get("houses", {}) as Dictionary).get(str(model), {})


func _house_file(model: int, which: String) -> String:
	var name := String(_row(model).get(which, ""))
	return "" if name == "" else ROOT.path_join("houses/%s/model.gltf" % name)


func _item(model: int) -> Node3D:
	return item_model(model)


static var _data_cache = null


static func _housing_data() -> Dictionary:
	if _data_cache == null:
		var raw = JSON.parse_string(FileAccess.get_file_as_string(ROOT.path_join("housing.json"))) 				if FileAccess.file_exists(ROOT.path_join("housing.json")) else null
		_data_cache = raw if raw is Dictionary else {}
	return _data_cache


## A 1.64 house item's model by its objects.csv id (furniture, garden objects, and the server's world objects of
## housing: lot markers 1306-1308, vaults / bindstones / tools 1488-1496), or null.
static func item_model(model: int) -> Node3D:
	var it: Dictionary = (_housing_data().get("items", {}) as Dictionary).get(str(model), {})
	if it.is_empty():
		return null
	return load_model(ROOT.path_join("items/%s/model.gltf" % String(it.model)))


func _load(path: String) -> Node3D:
	return load_model(path)


static var _mats: NifMaterials
static var _specs := {}                        # glTF path -> its nif_materials


## A housing model (pipeline/build_housing.py): its packed .scn - the NIF's node tree, names kept - with the glTF's
## converted materials on its drawn surfaces, from the library's one texture folder (content/housing/textures, x4plus
## copies beside); collision_* (collision) and helper_* (the interiors' placement planes, footprints) hidden.
static func load_model(gltf_path: String) -> Node3D:
	var scn := gltf_path.get_base_dir().path_join("model.scn")
	if not FileAccess.file_exists(gltf_path) or not FileAccess.file_exists(scn):
		push_warning("housing: missing model " + gltf_path)
		return null
	if _mats == null:
		_mats = NifMaterials.new({"original": ROOT.path_join("textures"), "x4plus": ROOT.path_join("textures/x4plus")},
				"x4plus")
		_mats.use_material_light = true                  # (ambient-lit like the client, as NifScene)
	if not _specs.has(gltf_path):
		var j = JSON.parse_string(FileAccess.get_file_as_string(gltf_path))
		_specs[gltf_path] = (j as Dictionary).get("extras", {}).get("nif_materials", []) if j is Dictionary else []
	var specs: Array = _specs[gltf_path]
	var root := (load(scn) as PackedScene).instantiate() as Node3D
	for node in root.find_children("*", "MeshInstance3D", true, false):
		var m := node as MeshInstance3D
		var n := String(m.name)
		if n.begins_with("collision_") or n.begins_with("helper_") or n.begins_with("climb_"):
			m.visible = false
			continue
		for s in m.mesh.get_surface_count():
			var src := m.mesh.surface_get_material(s)
			var nm := src.resource_name if src else ""
			if nm.begins_with("nifmat:") and int(nm.substr(7)) < specs.size():
				var spec: Dictionary = specs[int(nm.substr(7))]
				m.set_surface_override_material(s, _mats.material(spec))
				m.set_meta("spec_%d" % s, spec)
		m.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	return root


## A named part (the NIF's node, lowercase) or helper node under a house.
func _helper(scene: Node, node_name: String) -> Node3D:
	var n := scene.find_child(node_name, true, false)
	if n == null:
		n = scene.find_child(node_name.capitalize().replace(" ", ""), true, false)
	return n as Node3D


func _show(scene: Node, part: String, on: bool) -> void:
	var n := _helper(scene, part)
	if n:
		n.visible = on


func _apply_materials(scene: Node, model: int, h: Dictionary) -> void:
	var row := _row(model)
	for part in SLOTS:
		var start := int(row.get(String(SLOTS[part][0]), 0))
		_apply_slot(scene, part, start + int(h.get(String(PACKET_FIELD[part]), 0)))


## ph_textures row `id` on a part's meshes - only on the meshes wearing that part's own kind of texture (roof, wall,
## door, wood); a part with none of them takes it everywhere.
func _apply_slot(scene: Node, part: String, id: int) -> void:
	var node := _helper(scene, part)
	var tex_name := String((data.get("textures", {}) as Dictionary).get(str(id), ""))
	if node == null or tex_name == "":
		return
	var tex := _texture(tex_name)
	if tex == null:
		return
	var word: String = SLOTS[part][1]
	var meshes := node.find_children("*", "MeshInstance3D", true, false)
	if node is MeshInstance3D:
		meshes.append(node)
	var any_own := false
	for mi in meshes:
		if _wears(mi, word):
			any_own = true
			break
	for mi in meshes:
		if any_own and not _wears(mi, word):
			continue
		var m := mi as MeshInstance3D
		for s in m.mesh.get_surface_count():
			var mat := m.get_active_material(s)
			if mat is ShaderMaterial:
				var copy := (mat as ShaderMaterial).duplicate() as ShaderMaterial
				copy.set_shader_parameter("base_tex", tex)
				m.set_surface_override_material(s, copy)


func _wears(mi: MeshInstance3D, word: String) -> bool:
	for s in mi.mesh.get_surface_count():
		var spec: Dictionary = mi.get_meta("spec_%d" % s, {})
		var tex := String((spec.get("slots", {}) as Dictionary).get("base", {}).get("tex", "")).to_lower()
		if tex.begins_with("ph_") and tex.contains(word):
			return true
	return false


static func _texture(tex_name: String) -> Texture2D:
	if _tex_cache.has(tex_name):
		return _tex_cache[tex_name]
	var tex: Texture2D = null
	for sub in ["textures/x4plus", "textures"]:      # (the library's one folder; PH roofs never upscaled)
		var p := ROOT.path_join("%s/%s.png" % [sub, tex_name.to_lower()])
		if FileAccess.file_exists(p):
			var img := Image.load_from_file(p)
			if img:
				img.generate_mipmaps()
				tex = ImageTexture.create_from_image(img)
				break
	_tex_cache[tex_name] = tex
	return tex


# ---------------------------------------------------------------- collision and clicks

## The house's collision as the zones' (ZoneLoader): its collision meshes (under "collidee") walls, its drawn
## faces floors (the porch, the steps, an interior's floors); the porch's own collision kept apart (shown with the
## porch). Its door ("action001") a click target: a click on it goes in / out (HouseEnterLeave).
func _collide(scene: Node, holder: Node3D, number: int) -> void:
	var walls := StaticBody3D.new()
	walls.name = "Walls"
	walls.collision_layer = ZoneLoader.LAYER_WALL
	walls.collision_mask = 0
	var porch := StaticBody3D.new()
	porch.name = "PorchWalls"
	porch.collision_layer = ZoneLoader.LAYER_WALL
	porch.collision_mask = 0
	var floors := StaticBody3D.new()
	floors.name = "Floors"
	floors.collision_layer = ZoneLoader.LAYER_FLOOR
	floors.collision_mask = 0
	for b in [walls, porch, floors]:
		b.set_meta("house_number", number)
		b.set_meta("house_inside", holder == interior)
		holder.add_child(b)
	for node in scene.find_children("*", "MeshInstance3D", true, false):
		var mi := node as MeshInstance3D
		if String(mi.name).begins_with("helper_"):
			continue                                      # (placement planes / footprints: not walked on)
		var is_col := String(mi.name).begins_with("collision_")
		var faces := mi.mesh.get_faces()
		if faces.is_empty():
			continue
		var shape := ConcavePolygonShape3D.new()
		shape.backface_collision = true
		shape.set_faces(faces)
		var cs := CollisionShape3D.new()
		cs.shape = shape
		cs.transform = holder.global_transform.affine_inverse() * mi.global_transform
		if is_col:
			mi.visible = false
			var in_porch := false
			var p := mi.get_parent()
			while p and p != scene:
				if String(p.name).to_lower() == "porchcollision":
					in_porch = true
				p = p.get_parent()
			(porch if in_porch else walls).add_child(cs)
		elif mi.is_visible_in_tree():
			floors.add_child(cs)
	var door := _helper(scene, "action001")
	if door:
		var pick := StaticBody3D.new()
		pick.name = "DoorPick"
		pick.collision_layer = world.OBJECT_PICK if world else 0
		pick.collision_mask = 0
		var shape := CollisionShape3D.new()
		var box := BoxShape3D.new()
		box.size = Vector3(1.6, 2.6, 0.8)
		shape.shape = box
		shape.position.y = 1.3
		pick.add_child(shape)
		pick.set_meta("house_door", number)
		holder.add_child(pick)
		pick.global_transform = door.global_transform


# ---------------------------------------------------------------- hookpoints

const HOOK_MENUS := {"bpoint": 7, "vpoint": 5, "tpoint": 6, "mpoint": 4}   # 1.127 menus: bindstone / vault / tools / NPC
static var _hooks = null


## The hookpoint markers ("two stacked white circles on the ground", 1.64 help), shown while /housepoints is on: at
## house X / Y + the hookpoint's offset, Z 25000 + its Z (DB househookpointoffset, content/housing/hookpoints.json).
## Each one's kind (its right-click store) from the interior model's nearest Bpoint / Vpoint / Tpoint / Mpoint.
func _hookpoints(n: int) -> void:
	if interior == null or n != interior_number:
		return
	var old := interior.get_node_or_null("Hookpoints")
	if old:
		old.free()
	if not bool(housing.points_shown):
		return
	if _hooks == null:
		var raw = JSON.parse_string(FileAccess.get_file_as_string(ROOT.path_join("hookpoints.json"))) 				if FileAccess.file_exists(ROOT.path_join("hookpoints.json")) else null
		_hooks = raw if raw is Dictionary else {}
	var h: Dictionary = housing.house(n)
	var model := int(housing.interior.get("model", h.get("model", 0)))
	var hx := float(housing.interior.get("x", h.get("x", 0)))
	var hy := float(housing.interior.get("y", h.get("y", 0)))
	var box := Node3D.new()
	box.name = "Hookpoints"
	interior.add_child(box)
	var scene: Node = interior.get_meta("scene")
	var helpers := []
	for node in scene.find_children("*", "Node3D", true, false):
		var nm := String(node.name).to_lower()
		for k in HOOK_MENUS:
			if nm.begins_with(k):
				helpers.append([node, k])
	for hp in (_hooks as Dictionary).get(str(model), []):
		var at: Vector3 = world.scene_pos(hx + float(hp[1]), hy + float(hp[2]), INTERIOR_Z + float(hp[3]))
		var kind := "mpoint"
		var best := 1e9
		for e in helpers:
			var d := (e[0] as Node3D).global_position.distance_to(at)
			if d < best:
				best = d
				kind = e[1]
		var marker := _marker(kind)
		box.add_child(marker)
		marker.global_position = at
		var body := StaticBody3D.new()
		body.collision_layer = world.OBJECT_PICK if world else 0
		body.collision_mask = 0
		var cs := CollisionShape3D.new()
		var cyl := CylinderShape3D.new()
		cyl.radius = 0.5
		cyl.height = 0.4
		cs.shape = cyl
		body.add_child(cs)
		body.set_meta("house_hookpoint", {"house": n, "id": int(hp[0]), "menu": int(HOOK_MENUS[kind])})
		marker.add_child(body)


const MARKER_KINDS := {"bpoint": "bindstone", "vpoint": "vault", "mpoint": "merchant", "tpoint": "trade"}


## The 1.64 hookpoint marker of that kind (phousing BindstoneMarker / VaultMarker / MerchantMarker / TradeMarker,
## housing.json "markers"); two white rings if it's missing.
static func _marker(kind: String) -> Node3D:
	var name := String((_housing_data().get("markers", {}) as Dictionary).get(String(MARKER_KINDS.get(kind, "")), ""))
	if name != "":
		var made := load_model(ROOT.path_join("items/%s/model.gltf" % name))
		if made:
			return made
	var m := Node3D.new()
	var mat := StandardMaterial3D.new()
	mat.albedo_color = Color(1, 1, 1)
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	for i in 2:
		var ring := MeshInstance3D.new()
		var t := TorusMesh.new()
		t.inner_radius = 0.34 - 0.08 * i
		t.outer_radius = 0.4 - 0.08 * i
		ring.mesh = t
		ring.material_override = mat
		ring.position.y = 0.03 + 0.06 * i
		m.add_child(ring)
	return m


## NIF model axes (Z up) <-> the converted scene's (pipeline/nif_static DAOC_TO_GODOT: (x, y, z) -> (x, z, -y), metres).
static func nif_to_local(v: Vector3) -> Vector3:
	return Vector3(v.x, v.z, -v.y) * UNIT


static func local_to_nif(g: Vector3) -> Vector3:
	return Vector3(g.x, -g.z, g.y) / UNIT


## The interior's placement surface: Wallplane<n> (mode 0) / Floorplane<n> (mode 1).
func _plane(mode: int, n: int) -> Node3D:
	if interior == null or not interior.has_meta("scene"):
		return null
	return _helper(interior.get_meta("scene"), ("wallplane%d" if mode == 0 else "floorplane%d") % n)


## An item from the backpack let go on a wall or floor inside your house (1.64: a left click with it held, game.dll
## 0x42950a): the surface under the point - the nearest Wallplane / Floorplane by its bounds - and the point in its
## own frame; the server's HousePlaceItem (method 2 wall / 3 floor). True when it was placed (or tried).
func place_inside(slot: int, point: Vector3, normal: Vector3) -> bool:
	if interior == null or not interior.has_meta("scene"):
		return false
	var scene: Node = interior.get_meta("scene")
	var floor := normal.y > 0.7
	var best: Node3D = null
	var best_d := 1.0
	var best_n := -1
	for node in scene.find_children("*", "Node3D", true, false):
		var nm := String(node.name).to_lower()
		var prefix := "floorplane" if floor else "wallplane"
		if not nm.begins_with(prefix) or not nm.substr(prefix.length()).is_valid_int():
			continue
		for mi in (node as Node).find_children("*", "MeshInstance3D", true, false) + ([node] if node is MeshInstance3D else []):
			var m := mi as MeshInstance3D
			var box: AABB = m.global_transform * m.get_aabb()
			var d := box.grow(0.05).has_point(point)
			var dist := 0.0 if d else box.get_center().distance_to(point)
			if dist < best_d:
				best_d = dist
				best = node as Node3D
				best_n = int(nm.substr(prefix.length()))
	if best == null:
		PlayerData.message.emit("You may not place that here.", "error")
		return true
	var local := local_to_nif(best.global_transform.affine_inverse() * point)
	Server.client.call("HousePlace", slot, interior_number, best_n, 3 if floor else 2, 0, int(local.x), int(local.y))
	return true


## A click target around a placed item (its meshes' bounds), carrying what it is.
func _pick_body(item: Node3D, what: Dictionary, meta: String) -> void:
	var box := AABB()
	var first := true
	for node in item.find_children("*", "MeshInstance3D", true, false):
		var mi := node as MeshInstance3D
		var b := (item.global_transform.affine_inverse() * mi.global_transform) * mi.get_aabb()
		box = b if first else box.merge(b)
		first = false
	if first:
		box = AABB(Vector3(-0.3, 0, -0.3), Vector3(0.6, 0.6, 0.6))
	var body := StaticBody3D.new()
	body.collision_layer = world.OBJECT_PICK if world else 0
	body.collision_mask = 0
	var cs := CollisionShape3D.new()
	var shape := BoxShape3D.new()
	shape.size = box.size.max(Vector3(0.2, 0.2, 0.2))
	cs.shape = shape
	cs.position = box.get_center()
	body.add_child(cs)
	body.set_meta(meta, what)
	item.add_child(body)


var _last_click := {}                          # the last left click on a placed item: {key, at}


## A left click on a placed decoration / garden item: a double click picks it up (game.dll: A5, the server's 0x0D -
## a wall / floor decoration by its index, a garden item by its spot).
func item_clicked(meta: String, what: Dictionary) -> void:
	var key := "%s/%s" % [meta, str(what)]
	var now := Time.get_ticks_msec()
	if String(_last_click.get("key", "")) == key and now - int(_last_click.get("at", 0)) < 450:
		_last_click = {}
		if meta == "house_furniture":
			Server.client.call("HousePickup", int(what.house), int(what.index), 2 if int(what.mode) == 0 else 3)
		else:
			Server.client.call("HousePickup", int(what.house), int(what.position), 1)
		return
	_last_click = {"key": key, "at": now}


func _set_porch_collision(holder: Node3D, on: bool) -> void:
	var porch := holder.get_node_or_null("PorchWalls") as StaticBody3D
	if porch:
		porch.collision_layer = ZoneLoader.LAYER_WALL if on else 0


## A click on a house door (ServerWorld's object pick found its "house_door"): in from outside, out from inside.
func door_clicked(number: int) -> void:
	if Server.client:
		Server.client.call("HouseEnterLeave", number, housing.inside != number)


## The External Options window's live preview (game.dll 0x453e5f -> 0x404c06: nothing sent): the house drawn with
## these material bytes in place of its own ({} = its real look again).
func preview(number: int, over: Dictionary) -> void:
	var have: Dictionary = exteriors.get(number, {})
	if have.is_empty():
		return
	var h: Dictionary = (housing.house(number) as Dictionary).duplicate()
	h.merge(over, true)
	_apply_materials(have.scene, int(have.model), h)


## Whether the lot's garden has spot `pos` (its exterior model's GPoint<pos> node: the garden window's plantable mask,
## game.dll 0x403a5e).
func has_garden_point(number: int, pos: int) -> bool:
	var have: Dictionary = exteriors.get(number, {})
	return not have.is_empty() and _helper(have.scene, "gpoint%d" % pos) != null


## Seamless zones moved everything by -d (ServerWorld.rebase): the houses too.
func rebase(d: Vector3) -> void:
	for n in exteriors:
		var have: Dictionary = exteriors[n]
		if is_instance_valid(have.node):
			(have.node as Node3D).position -= d
	if interior and is_instance_valid(interior):
		interior.position -= d
