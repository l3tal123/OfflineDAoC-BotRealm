extends Node3D
## Offline, True 1.65 Mode classic figures of the Shrouded Isles races (Half Ogre, Frostalf, Shar - built 2026-11-02 for
## their ~155 world NPCs): the six figures standing, mid-walk from the side, from behind, in plate + cloak + helm with a
## sword, and two looks of each (head A / head B, other faces) close up.
##   Godot --path godot --screen 1 res://tests/si_races_probe.tscn -- --out=<png prefix>

const LOOKS := [["hal_m", 1008, 1011], ["hal_f", 1020, 1027], ["fro_m", 1051, 1058], ["fro_f", 1063, 1070],
		["sha_m", 1075, 1082], ["sha_f", 1087, 1094]]
const PLATE := {25: 86, 27: 87, 28: 88, 22: 89, 23: 90, 21: 93, 26: 57, 10: 3}
var figs: Array = []
var cam: Camera3D
var out := "user://si_races"


func _ready() -> void:
	Server.world_mode = "1.65"
	Fig3Figure._classic = -1
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--out="):
			out = a.get_slice("=", 1)
	var env := WorldEnvironment.new()
	env.environment = Environment.new()
	env.environment.background_mode = Environment.BG_COLOR
	env.environment.background_color = Color(0.35, 0.4, 0.45)
	env.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.environment.ambient_light_color = Color(0.6, 0.6, 0.6)
	add_child(env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-35, 25, 0)
	add_child(sun)
	cam = Camera3D.new()
	cam.fov = 45
	add_child(cam)
	_run()


func _make(prefix: String, model: int, x: float) -> Fig3Figure:
	var f := Fig3Figure.make()
	add_child(f)
	if not f.load_figure(prefix):
		print("[si] %s did not load" % prefix)
		return null
	f.apply({"model": model})
	f.position = Vector3(x, 0, 0)
	var shown := []
	for part in f._meshes:
		for mi in f._meshes[part]:
			if (mi as MeshInstance3D).visible:
				shown.append(part)
				break
	print("[si] %s %s model %d: scale %.2f, shown %s, row %s, clips %d" % [prefix, f.get_class(), model, f.scale.y,
			str(shown), str(ClassicFigure.look_row(f.meta, {"model": model})), f.anim.get_animation_list().size() if f.anim else 0])
	return f


func _clear() -> void:
	for f in figs:
		if is_instance_valid(f):
			f.queue_free()
	figs.clear()
	await get_tree().process_frame


func _shot(tag: String) -> void:
	for i in 20:
		await get_tree().process_frame
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png("%s_%s.png" % [out, tag])
	print("[si] shot ", tag)


func _run() -> void:
	# the six standing in their first look, then walking (side view), then from behind
	for i in LOOKS.size():
		var f := _make(LOOKS[i][0], LOOKS[i][1], (i - 2.5) * 1.3)
		if f:
			figs.append(f)
	cam.position = Vector3(0, 1.3, 7.0)
	await get_tree().create_timer(1.0).timeout
	await _shot("front")
	for f in figs:
		f.play("walk")
		f.rotation_degrees.y = 90
	await get_tree().create_timer(0.45).timeout
	await _shot("walk")
	for f in figs:
		f.play("idle")
		f.rotation_degrees.y = 180
	await _shot("back")
	# in plate, full helm, cloak and a sword (Albion plate set, the classic_dress_probe's): mid-swing
	for f in figs:
		f.rotation_degrees.y = 0
		var item_at := func(slot: int) -> Dictionary:
			return {"model": int(PLATE[slot]), "color": 0} if PLATE.has(slot) else {}
		Player.dress(f, item_at, "standard", false, true, {})
	await _shot("plate")
	for f in figs:
		f.play("attack_med")
	await get_tree().create_timer(0.3).timeout
	await _shot("plate_swing")
	for f in figs:
		f.play("cast_mid")
	await get_tree().create_timer(0.6).timeout
	await _shot("plate_cast")
	await _clear()
	# both looks of each, close up (head A / B, two faces)
	for half in 2:
		for i in 6:
			var e: Array = LOOKS[i]
			var f := _make(e[0], e[1 + half], (i - 2.5) * 0.75)
			if f:
				f.play("idle")
				figs.append(f)
		cam.position = Vector3(0, 1.75, 3.3)
		cam.look_at(Vector3(0, 1.6, 0))
		await get_tree().create_timer(0.6).timeout
		await _shot("heads_%d" % (half + 1))
		await _clear()
	print("[si] done")
	get_tree().quit()
