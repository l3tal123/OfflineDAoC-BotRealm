extends Node
## Offline check (owner 2026-11-02: Stick / Face / Follow / Camera Reset keys did nothing): presses each action's own
## bound key as a real input event and reads the result - track mode on, the same key again off, the camera back
## behind the character.
##   --skip-login --zone=N --world-mode=1.65 --probe=track_keys_probe


func _key_for(action: String) -> InputEvent:
	var b: String = Keybinds.bindings.get(action, [""])[0]
	var parts := b.split("+")
	var e := InputEventKey.new()
	e.pressed = true
	for p in parts.slice(0, parts.size() - 1):
		match p:
			"Shift": e.shift_pressed = true
			"Ctrl": e.ctrl_pressed = true
			"Alt": e.alt_pressed = true
	var k: String = parts[parts.size() - 1]
	e.keycode = Keybinds.NAMES.get(k, OS.find_keycode_from_string(k))
	e.physical_keycode = e.keycode
	return e


func _press(action: String) -> void:
	var e := _key_for(action)
	Input.parse_input_event(e)
	await get_tree().process_frame
	var up := e.duplicate() as InputEventKey
	up.pressed = false
	Input.parse_input_event(up)
	await get_tree().create_timer(0.3).timeout


func _ready() -> void:
	var v := get_tree().current_scene
	await get_tree().create_timer(8.0).timeout
	var p = v.player
	var n = v.npcs.nearest_enemy(400.0) if v.npcs else null
	if n == null and v.npcs:
		for k in v.npcs.active:
			n = v.npcs.active[k]
			break
	if n == null:
		print("[track] FAIL no npc")
		get_tree().quit()
		return
	p.global_position = n.global_position + Vector3(4, 0.5, 0)
	v.npcs.select(n)
	await get_tree().create_timer(0.5).timeout
	var ok := true
	for mode in ["face", "follow", "stick"]:
		print("[track] ", mode, " key ", Keybinds.bindings.get(mode))
		await _press(mode)
		var on: String = p.track_mode
		await _press(mode)
		var off: String = p.track_mode
		print("[track] ", mode, ": after 1st press '", on, "', after 2nd '", off, "'")
		ok = ok and on == mode and off == ""
	p.cam_yaw = p.char_yaw + 1.7
	p.cam_pitch = 0.4
	print("[track] camera_reset key ", Keybinds.bindings.get("camera_reset"))
	await _press("camera_reset")
	var dy := absf(angle_difference(p.cam_yaw, p.char_yaw))
	print("[track] camera reset: yaw off by %.3f, pitch %.2f" % [dy, p.cam_pitch])
	ok = ok and dy < 0.05 and absf(p.cam_pitch + 0.22) < 0.01
	# Look Up / Look Down held half a second
	for a in [["look_up", 1.0], ["look_down", -1.0]]:
		p.cam_pitch = 0.0
		var e := _key_for(a[0])
		Input.parse_input_event(e)
		await get_tree().create_timer(0.5).timeout
		var up := e.duplicate() as InputEventKey
		up.pressed = false
		Input.parse_input_event(up)
		await get_tree().process_frame
		print("[track] %s %s: pitch 0 -> %.2f" % [a[0], Keybinds.bindings.get(a[0]), p.cam_pitch])
		ok = ok and signf(p.cam_pitch) == a[1]
	var ui = v.ui
	await _press("toggle_names")
	var hidden := NamePlates.hidden
	await _press("toggle_names")
	print("[track] toggle_names: hidden %s then %s" % [hidden, NamePlates.hidden])
	ok = ok and hidden and not NamePlates.hidden
	await _press("perf_meter")
	var perf_on: bool = ui._perf != null and ui._perf.visible
	await get_tree().create_timer(0.7).timeout
	print("[track] perf_meter: shown %s text '%s'" % [perf_on, ui._perf.text.replace(char(10), " | ") if ui._perf else ""])
	await _press("perf_meter")
	ok = ok and perf_on and not ui._perf.visible
	print("[track] chat_log default ", Keybinds.bindings.get("chat_log"), " fires on L: ", Keybinds.pressed(_key_for("show_combat"), "chat_log"),
			"   Tab is target_friend: ", Keybinds.pressed(_key_for("target_enemy"), "target_friend"),
			" target_enemy: ", Keybinds.pressed(_key_for("target_enemy"), "target_enemy"))
	Keybinds.bindings["chat_log"] = ["Ctrl+Shift+L"]
	Keybinds._apply()
	await _press("chat_log")
	var logging: bool = ui._chat_log != null
	PlayerData.message.emit("chat log probe line", "system")
	await _press("chat_log")
	var txt := FileAccess.get_file_as_string(GameUI.chat_log_path())
	print("[track] chat_log: on %s, off %s, file tail: %s" % [logging, ui._chat_log == null, txt.right(160).replace(char(10), " / ")])
	ok = ok and logging and ui._chat_log == null and txt.contains("chat log probe line")
	var wm_before: bool = ui.warmap.visible
	await _press("show_war_map")
	print("[track] war map visible ", wm_before, " -> ", ui.warmap.visible)
	ok = ok and ui.warmap.visible != wm_before
	await _press("show_commands")
	print("[track] command window: typing %s text '%s'" % [ui.chat.typing(), ui.chat._input.text])
	ok = ok and ui.chat.typing() and ui.chat._input.text == "/"
	print("[track] RESULT ", "PASS" if ok else "FAIL")
	get_tree().quit()
