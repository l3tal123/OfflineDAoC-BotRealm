extends Node
## Offline: where NPCs stored at the server's places (region units) stand - the floor the client picks for them
## (NpcManager.floor_at: the first floor casting down from 64 units over the server's Z, as the 1.64 client), against
## the old 1.2 m pick - for GitHub issue #4 (Gothwaite Harbor's dye merchants, stored 61 units under their floor, stood
## sunk to the neck). Markers in the shots: red = the server's Z, green = the picked floor. --npcs="name:x:y:z;..."
##   --skip-login --zone=51 --pos=x,y,z --world-mode=1.65 --probe=npc_floor_probe --npcs="..." --out=<dir>
var v: Node


func _arg(k: String, def: String) -> String:
	return String(v.args.get(k, def))


func _local(zid: int, gx: float, gy: float, gz: float) -> Vector3:
	var idx = JSON.parse_string(FileAccess.get_file_as_string("res://content/zones/zones_index.json"))
	var zi: Dictionary = idx.get(str(zid), {})
	return Vector3((gx - float(zi.get("x", 0)) * 8192.0) * 0.025, gz * 0.025, (gy - float(zi.get("y", 0)) * 8192.0) * 0.025)


func _pick(at: Vector3, up: float) -> float:
	var q := PhysicsRayQueryParameters3D.create(at + Vector3(0, up, 0), at - Vector3(0, 6, 0), ZoneLoader.LAYER_FLOOR)
	var hit: Dictionary = v.zone.get_world_3d().direct_space_state.intersect_ray(q)
	return float(hit.position.y) if not hit.is_empty() else NAN


func _marker(at: Vector3, c: Color) -> void:
	var m := MeshInstance3D.new()
	var cyl := CylinderMesh.new()
	cyl.top_radius = 0.18
	cyl.bottom_radius = 0.18
	cyl.height = 1.8
	m.mesh = cyl
	var mat := StandardMaterial3D.new()
	mat.albedo_color = c
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	m.material_override = mat
	v.add_child(m)
	m.global_position = at + Vector3(0, 0.9, 0)


func _ready() -> void:
	v = get_tree().current_scene
	await get_tree().create_timer(8.0).timeout
	var zid := int(v.zone.info.id)
	var nm := NpcManager.new()                              # (floor_at as the game uses it)
	nm.zone = v.zone
	v.add_child(nm)
	var mid := Vector3.ZERO
	var n := 0
	for one in _arg("npcs", "").split(";", false):
		var f := one.split(":")
		var at := _local(zid, float(f[1]), float(f[2]), float(f[3]))
		var now := nm.floor_at(at)
		var old := _pick(at, 1.2)
		print("[floor] %-16s server z %.2f m | old pick (1.2 m up) %s | now (1.6 m up) %.2f m (%+.2f)" % [f[0], at.y,
				"no floor - stood at the server's Z" if is_nan(old) else "%.2f" % old, now, now - at.y])
		_marker(at, Color(0.9, 0.15, 0.15))
		_marker(Vector3(at.x + 0.5, now, at.z), Color(0.2, 0.9, 0.3))
		mid += at
		n += 1
	if n == 0:
		get_tree().quit()
		return
	mid /= n
	v.player.visible = false
	var cam := Camera3D.new()
	v.add_child(cam)
	cam.fov = 75.0
	for i in 2:
		var yaw := PI * 0.75 + PI * i
		cam.global_position = mid + Vector3(sin(yaw) * 5.0, 2.6, cos(yaw) * 5.0)
		cam.look_at(mid + Vector3(0, 0.8, 0))
		cam.current = true
		await get_tree().create_timer(1.5).timeout
		await RenderingServer.frame_post_draw
		get_viewport().get_texture().get_image().save_png("%s/%s_%d.png" % [_arg("out", "user://"), _arg("tag", "floor"), i])
	get_tree().quit()
