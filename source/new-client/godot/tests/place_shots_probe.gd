extends Node
## Offline: shots of one place from several sides - before / after checks of a zone model fix (GitHub issue #2:
## Camelot's white walls). --target=x,y,z (engine metres), --dist=m (default 25), --n=angles (default 4), --up=m
## (camera height over the target, default 3), --tag=name.
##   --skip-login --zone=N --probe=place_shots_probe --target=x,y,z --out=<dir> [--dist=25] [--n=4] [--up=3] [--tag=x]
var v: Node


func _arg(k: String, def: String) -> String:
	return String(v.args.get(k, def))


func _ready() -> void:
	v = get_tree().current_scene
	var t := _arg("target", "0,0,0").split(",")
	var at := Vector3(float(t[0]), float(t[1]), float(t[2]))
	if _arg("ground", "") != "":                              # (--ground=1: the target at the ground there)
		at.y = v.zone.height_at(at.x, at.z) + 2.0
	v.player.global_position = at + Vector3(0, 1, 0)            # (its surroundings load around you)
	await get_tree().create_timer(10.0).timeout
	var cam := Camera3D.new()
	v.add_child(cam)
	cam.far = 3000.0
	cam.fov = 70.0
	var n := int(_arg("n", "4"))
	var dist := float(_arg("dist", "25"))
	var up := float(_arg("up", "3"))
	var out := _arg("out", "user://")
	var tag := _arg("tag", "look")
	for i in n:
		var yaw := TAU * i / n
		cam.global_position = at + Vector3(sin(yaw) * dist, up, cos(yaw) * dist)
		cam.look_at(at)
		cam.current = true
		await get_tree().create_timer(2.0).timeout
		await RenderingServer.frame_post_draw
		get_viewport().get_texture().get_image().save_png("%s/%s_%d.png" % [out, tag, i])
	print("[look] %d shots of %s" % [n, str(at)])
	var r := float(_arg("list", "0"))
	if r > 0.0:                                            # what is drawn there: each mesh, its model, its textures
		for mi in v.find_children("*", "MeshInstance3D", true, false):
			var m := mi as MeshInstance3D
			if m.mesh == null or not m.is_visible_in_tree():
				continue
			var box: AABB = m.global_transform * m.get_aabb()
			if box.grow(r).has_point(at):
				var model := m.get_parent()
				var path := ""
				while model and path == "":
					path = String(model.scene_file_path)
					model = model.get_parent()
				var tex := []
				for s in m.mesh.get_surface_count():
					var mat := m.get_active_material(s)
					var tx: Texture2D = null
					if mat is StandardMaterial3D:
						tx = (mat as StandardMaterial3D).albedo_texture
					elif mat is ShaderMaterial:
						for pn in ["albedo", "tex", "base", "texture_albedo", "base_tex", "tex1"]:
							var x = (mat as ShaderMaterial).get_shader_parameter(pn)
							if x is Texture2D:
								tx = x
								break
					tex.append(tx.resource_path.get_file() if tx else "NONE")
				print("[look]   %-28s %-40s box %s..%s  tex %s" % [m.name, path.get_file(), str(box.position.snapped(Vector3.ONE * 0.1)),
						str(box.end.snapped(Vector3.ONE * 0.1)), str(tex)])
	get_tree().quit()
