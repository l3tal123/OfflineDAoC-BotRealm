extends Node
## Offline check of the Mouse Look Toggle (True 1.65 Mode): off by default (Options > CONTROLS switch); then a right click on open ground switches it on, the mouse
## then turns the character with no button held, the next right click switches it off; a right drag still only steers.
##   Godot --path godot -- --skip-login --zone=<id> --world-mode=1.65 --probe=mouse_look_probe
var viewer: Node
var p: Player


func _ready() -> void:
	await get_tree().create_timer(4.0).timeout
	viewer = get_tree().current_scene
	p = viewer.player
	var sky := Vector2(get_viewport().get_visible_rect().size.x * 0.5, 12.0)
	print("[mouse_look] bound to: ", Keybinds.bindings.get("mouse_look", []), "  option on: ", p.mouse_look_allowed)
	await _click(sky, Vector2.ZERO)
	print("[mouse_look] option OFF (the default), a right click on open ground: on=%s captured=%s (expected off)" % [p.mouse_look, Input.mouse_mode == Input.MOUSE_MODE_CAPTURED])
	p.mouse_look_allowed = true                          # Options > CONTROLS "MOUSE LOOK TOGGLE" switched on
	await _click(sky, Vector2.ZERO)
	print("[mouse_look] after a right click on open ground: on=%s captured=%s" % [p.mouse_look, Input.mouse_mode == Input.MOUSE_MODE_CAPTURED])
	var yaw0 := p.char_yaw
	for i in 10:
		var m := InputEventMouseMotion.new()
		m.relative = Vector2(20, 0)
		Input.parse_input_event(m)
		await get_tree().process_frame
	print("[mouse_look] moving the mouse with no button held turned the character %.0f deg" % rad_to_deg(p.char_yaw - yaw0))
	await _click(sky, Vector2.ZERO)
	print("[mouse_look] after the second right click: on=%s captured=%s" % [p.mouse_look, Input.mouse_mode == Input.MOUSE_MODE_CAPTURED])
	await _click(sky, Vector2(40, 0))
	print("[mouse_look] after a right DRAG: on=%s captured=%s (expected off, cursor back)" % [p.mouse_look, Input.mouse_mode == Input.MOUSE_MODE_CAPTURED])
	await _click(sky, Vector2.ZERO)
	viewer.ui.options.visible = true                     # the Esc menu (zone_viewer turns the player's input off)
	for i in 6:
		await get_tree().physics_frame
	print("[mouse_look] on, then the Esc menu opened: on=%s captured=%s (expected off)" % [p.mouse_look, Input.mouse_mode == Input.MOUSE_MODE_CAPTURED])
	viewer.ui.options.visible = false
	get_tree().quit()


func _click(at: Vector2, drag: Vector2) -> void:
	var down := InputEventMouseButton.new()
	down.button_index = MOUSE_BUTTON_RIGHT
	down.pressed = true
	down.position = at
	down.global_position = at
	Input.parse_input_event(down)
	await get_tree().process_frame
	if drag != Vector2.ZERO:
		for i in 4:
			var m := InputEventMouseMotion.new()
			m.relative = drag / 4.0
			m.button_mask = MOUSE_BUTTON_MASK_RIGHT
			Input.parse_input_event(m)
			await get_tree().process_frame
	var up := InputEventMouseButton.new()
	up.button_index = MOUSE_BUTTON_RIGHT
	up.pressed = false
	up.position = at
	up.global_position = at
	Input.parse_input_event(up)
	await get_tree().process_frame
	await get_tree().process_frame
