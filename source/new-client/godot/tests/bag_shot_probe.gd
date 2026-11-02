extends Node
## Offline: the dropped-loot bag (model 488) on the ground in front of you, from two sides - True 1.65 Mode draws the
## 1.64 client's bag (pipeline/build_loot_164.py), normal mode the later client's.
##   --skip-login --zone=N --world-mode=1.65 --probe=bag_shot_probe --out=<dir> [--tag=x] [--model=488]
var v: Node


func _ready() -> void:
	v = get_tree().current_scene
	await get_tree().create_timer(6.0).timeout
	var model := int(v.args.get("model", "488"))
	var p: Vector3 = v.player.global_position
	var fwd := Vector3(-sin(v.player.char_yaw), 0, -cos(v.player.char_yaw))
	var at := p + fwd * 2.0
	at.y = v.zone.height_at(at.x, at.z)
	var bag := WeaponModels.make_world(model)
	print("[bag] model %d -> %s  (world mode %s)" % [model, WeaponModels.path_of(model), Server.world_mode])
	if bag == null:
		print("[bag] FAIL: no model")
		get_tree().quit()
		return
	v.add_child(bag)
	bag.global_position = at
	v.player.visible = false
	var cam := Camera3D.new()
	v.add_child(cam)
	cam.fov = 40.0
	var tag := String(v.args.get("tag", "bag"))
	for i in 2:
		var side := fwd.rotated(Vector3.UP, PI * 0.5 * i)
		cam.global_position = at - side * 1.1 + Vector3(0, 0.7, 0)
		cam.look_at(at + Vector3(0, 0.15, 0))
		cam.current = true
		await get_tree().create_timer(1.0).timeout
		await RenderingServer.frame_post_draw
		get_viewport().get_texture().get_image().save_png("%s/%s_%d.png" % [String(v.args.get("out", "user://")), tag, i])
	get_tree().quit()
