extends Node
## Offline check (owner 2026-11-02, player report): %t / %T in a chat line takes your target's name (or "<nothing>"),
## as the 1.64 client; and the status panel's red attack-mode border (rightbotcomb) on and off - shots of both.
##   --skip-login --zone=N --world-mode=1.65 --probe=target_fill_border_probe --out=<dir>


func _ready() -> void:
	var v := get_tree().current_scene
	await get_tree().create_timer(6.0).timeout
	var out := String(v.args.get("out", "user://"))
	PlayerData.set_target({})
	var ok := true
	var cases := [["/s Healing %t now", "/s Healing <nothing> now"]]
	for c in cases:
		var got := ChatWindow.target_fill(c[0])
		print("[fill] '%s' -> '%s'" % [c[0], got])
		ok = ok and got == c[1]
	PlayerData.set_target({"name": "Rory", "level": 5})
	cases = [["/s Healing %t now", "/s Healing Rory now"], ["/g %T at 50%", "/g Rory at 50*"], ["%t and %t", "Rory and *t"],
			["no target words", "no target words"]]
	for c in cases:
		var got := ChatWindow.target_fill(c[0])
		print("[fill] '%s' -> '%s'" % [c[0], got])
		ok = ok and got == c[1]
	var ui = v.ui
	var st: Control = ui._status
	var r := Rect2(st.global_position, st.size * st.get_global_transform().get_scale())
	ui.set_combat_border(true)
	await get_tree().create_timer(0.3).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().get_region(Rect2i(r)).save_png(out + "/status_combat.png")
	var red_on: Color = (st.frame as TextureRect).texture.get_image().get_pixel((st.frame as TextureRect).texture.get_width() / 2, 2)
	ui.set_combat_border(false)
	await get_tree().create_timer(0.3).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().get_region(Rect2i(r)).save_png(out + "/status_normal.png")
	var red_off: Color = (st.frame as TextureRect).texture.get_image().get_pixel((st.frame as TextureRect).texture.get_width() / 2, 2)
	# (corner pixel of the frame art: rightbotcomb's red edge vs rightbot's grey)
	print("[fill] border corner pixel: on %s off %s" % [red_on, red_off])
	ok = ok and red_on.r - red_on.g > 0.2 and absf(red_off.r - red_off.g) < 0.1
	print("[fill] RESULT ", "PASS" if ok else "FAIL")
	get_tree().quit()
