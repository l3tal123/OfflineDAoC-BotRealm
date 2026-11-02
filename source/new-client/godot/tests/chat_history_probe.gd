extends Node
## Offline: the chat box's Shift+Up / Shift+Down through your sent lines - three lines sent, then the keys pressed in
## the typing box as you would, the box's text after each.
##   --skip-login --zone=200 --probe=chat_history_probe
var v: Node


func _key(k: Key) -> void:
	var e := InputEventKey.new()
	e.keycode = k
	e.physical_keycode = k
	e.shift_pressed = true
	e.pressed = true
	Input.parse_input_event(e)
	await get_tree().process_frame
	var u := e.duplicate() as InputEventKey
	u.pressed = false
	Input.parse_input_event(u)
	await get_tree().process_frame


func _ready() -> void:
	v = get_tree().current_scene
	await get_tree().create_timer(5.0).timeout
	var chat = null
	for c in v.ui.find_children("*", "ChatWindow", true, false):
		if c.get("_input") != null:
			chat = c
	if chat == null:
		print("[chat] FAIL: no chat box")
		get_tree().quit()
		return
	for t in ["/loc", "first line", "second line"]:
		chat.start_typing(t)
		await get_tree().process_frame
		chat._send(t)
		await get_tree().process_frame
	chat.start_typing("half typed")
	await get_tree().process_frame
	var seen := []
	for k in [KEY_UP, KEY_UP, KEY_UP, KEY_UP, KEY_DOWN, KEY_DOWN, KEY_DOWN, KEY_DOWN]:
		await _key(k)
		seen.append("%s:%s" % ["up" if k == KEY_UP else "down", chat._input.text])
	print("[chat] ", seen)
	# the 1.64 client's order: its index already points past the line shown (the first Down shows it again), stops
	# at the oldest; past the newest the line you were typing (owner: a convenience)
	var want := ["up:second line", "up:first line", "up:/loc", "up:/loc", "down:/loc", "down:first line",
			"down:second line", "down:half typed"]
	print("[chat] ", "ok" if seen == want else "FAILED, wanted %s" % str(want))
	chat._send("/appeal stuck")                            # (not kept, as the 1.64 client)
	await get_tree().process_frame
	await _key(KEY_UP)                                     # the chat line closed: opens with the newest line
	print("[chat] Shift+Up closed -> open %s, line '%s' (want true, 'second line')" % [chat._input.visible, chat._input.text])
	chat.stop_typing()
	# /qbar (True 1.65: the 1.64 client's "/qbar N")
	var said := []
	PlayerData.message.connect(func(t: String, _k: String): said.append(t))
	var qb = v.ui.quickbars[0]
	for line in ["/qbar 10", "/qbar 11", "/qbar 2 1"]:
		chat._send(line)
		await get_tree().process_frame
		print("[chat] %s -> bank %d, said %s" % [line, int(PlayerData.quickbar_bank[0]) + 1, str(said)])
		said.clear()
	get_tree().quit()
