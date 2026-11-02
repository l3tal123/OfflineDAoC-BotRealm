extends Node
## Offline: shots of the death windows - the release window and the server's "Release Timer" timer window, as on a
## live server (owner 2026-11-01: buttons, timer and bar hung out of their boxes).
##   --skip-login --zone=N --world-mode=1.65 --probe=death_windows_probe --out=<dir> [--tag=x]


func _ready() -> void:
	var v := get_tree().current_scene
	await get_tree().create_timer(6.0).timeout
	if v.ui.timer_bar == null:                              # (built only on a server: built here as GameUI does)
		var tb := TimerBarWindow.new()
		tb.resizable = true
		tb.patch = [6, 19, 13, 33]
		tb.size = Vector2(256, 96)
		tb.min_size = tb.size
		tb.max_size = tb.size
		tb.setup("timer", "interact", Rect2(), v.ui.client_pos(272, 330))
		v.ui.root.add_child(tb)
		tb.visible = false
		tb.build()
		v.ui.timer_bar = tb
	v.ui.show_release(true)
	PlayerData.release_timer.emit(-1.0)
	Server.craft_timer.emit(int(v.args.get("secs", "900")), "Release Timer")
	await get_tree().create_timer(3.0).timeout
	await RenderingServer.frame_post_draw
	var out := String(v.args.get("out", "user://"))
	get_viewport().get_texture().get_image().save_png("%s/%s.png" % [out, String(v.args.get("tag", "death"))])
	PlayerData.release_timer.emit(600.0)                    # offline: the release window's own countdown line + bar
	Server.craft_timer.emit(-1, "")
	await get_tree().create_timer(1.0).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png("%s/%s_offline.png" % [out, String(v.args.get("tag", "death"))])
	print("[death] shots in ", out)
	get_tree().quit()
