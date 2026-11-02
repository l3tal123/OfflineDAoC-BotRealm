extends Node
## Offline check of GitHub issue #6 (quickbar swap): A on slot 1, B on slot 2; C put onto slot 1 (A to the pointer),
## A clicked onto slot 2 - B must end on the pointer, not vanish. Real mouse events; the bar is restored afterwards.
##   --skip-login --zone=N --world-mode=1.65 --probe=quickbar_swap_probe


var v
var bar: Quickbar


func _click(at: Vector2) -> void:
	for pressed in [true, false]:
		var e := InputEventMouseButton.new()
		e.button_index = MOUSE_BUTTON_LEFT
		e.pressed = pressed
		e.position = at
		e.global_position = at
		Input.parse_input_event(e)
		await get_tree().process_frame
		await get_tree().process_frame


func _move(at: Vector2, held := false) -> void:
	var m := InputEventMouseMotion.new()
	if held:
		m.button_mask = MOUSE_BUTTON_MASK_LEFT
		m.relative = Vector2(4, 0)
	m.position = at
	m.global_position = at
	Input.parse_input_event(m)
	await get_tree().process_frame


func _state(tag: String) -> void:
	var b: Array = PlayerData.quickbars[0][PlayerData.quickbar_bank[0]]
	var dd = get_viewport().gui_get_drag_data() if get_viewport().gui_is_dragging() else null
	print("[qb] %s: slot1=%s slot2=%s dragging=%s holding=%s" % [tag, b[0].get("name", "-"), b[1].get("name", "-"),
			get_viewport().gui_is_dragging(), (dd.get("entry", dd).get("name", "?") if dd is Dictionary else "-")])


func _ready() -> void:
	v = get_tree().current_scene
	await get_tree().create_timer(6.0).timeout
	bar = v.ui.quickbars[0]
	var bank: Array = PlayerData.quickbars[0][PlayerData.quickbar_bank[0]]
	var keep := [bank[0], bank[1], bank[2]]
	bank[0] = {"kind": "spell", "id": "A", "icon": 1, "name": "A", "border": -1}
	bank[1] = {"kind": "spell", "id": "B", "icon": 2, "name": "B", "border": -1}
	for s in bar._slots:
		s.refresh()
	var s1: Control = bar._slots[0]
	var s2: Control = bar._slots[1]
	var p1 := s1.get_global_rect().get_center()
	var p2 := s2.get_global_rect().get_center()
	_state("start")
	# C from somewhere else (a spell sheet row): on the pointer, then a click on slot 1
	await _move(p1 + Vector2(0, -80))
	var src := Control.new()
	v.ui.root.add_child(src)
	src.force_drag({"kind": "spell", "id": "C", "icon": 3, "name": "C", "border": -1}, TextureRect.new())
	await get_tree().process_frame
	await _move(p1)
	await _click(p1)
	await get_tree().create_timer(0.2).timeout
	_state("after C onto slot 1")
	await _move(p2)
	await _click(p2)
	await get_tree().create_timer(0.2).timeout
	_state("after A onto slot 2")
	var b: Array = PlayerData.quickbars[0][PlayerData.quickbar_bank[0]]
	var dd = get_viewport().gui_get_drag_data() if get_viewport().gui_is_dragging() else null
	var held: String = dd.get("entry", {}).get("name", "") if dd is Dictionary else ""
	var ok: bool = b[0].get("name", "") == "C" and b[1].get("name", "") == "A" and held == "B"
	# put B down on slot 3, then a normal drag (press, move, release) from slot 1 (C) onto slot 2 (A)
	var p3 := (bar._slots[2] as Control).get_global_rect().get_center()
	await _move(p3)
	await _click(p3)
	await get_tree().create_timer(0.2).timeout
	_state("after B onto slot 3")
	var down := InputEventMouseButton.new()
	down.button_index = MOUSE_BUTTON_LEFT
	down.pressed = true
	down.position = p1
	down.global_position = p1
	await _move(p1)
	Input.parse_input_event(down)
	await get_tree().process_frame
	for k in 12:
		await _move(p1.lerp(p2, (k + 1) / 12.0), true)
	var up := down.duplicate() as InputEventMouseButton
	up.pressed = false
	up.position = p2
	up.global_position = p2
	Input.parse_input_event(up)
	await get_tree().create_timer(0.3).timeout
	_state("after dragging slot 1 onto slot 2")
	b = PlayerData.quickbars[0][PlayerData.quickbar_bank[0]]
	dd = get_viewport().gui_get_drag_data() if get_viewport().gui_is_dragging() else null
	held = dd.get("entry", {}).get("name", "") if dd is Dictionary else ""
	ok = ok and b[0].is_empty() and b[1].get("name", "") == "C" and b[2].get("name", "") == "B" and held == "A"
	print("[qb] RESULT ", "PASS" if ok else "FAIL")
	bank[2] = keep[2]
	# put the bar back as it was
	bank[0] = keep[0]
	bank[1] = keep[1]
	PlayerData.save_character()
	get_tree().quit()
