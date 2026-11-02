extends Node
## Live test (a player report: "Revived my companion at Epona in Mag Mell and cause godot client to crash"): in the
## world on the running True 1.65 server with Claude's test character, as a player does it - the companion dies, you
## right-click Epona (the healer's window opens, the server's healer page), and press REVIVE on its row. Three rounds.
## GM steps only to set it up: level 5 + the free first hire when the roster is empty, /summon, /jump to Epona, /harm
## to kill the companion. [epona] lines; shots per round.
##   Godot --path godot res://tests/server_session.tscn -- --server=127.0.0.1 --account=<a> --password=<p>
##       --char=<test character> --world-mode=1.65 --probe=epona_revive_probe --out=<prefix> [--rounds=3]

func _ready() -> void:
	var root := get_tree().root
	if root.has_node("EponaReviveProbe"):
		queue_free()
		return
	var d := Driver.new()
	d.name = "EponaReviveProbe"
	root.add_child.call_deferred(d)
	queue_free()


class Driver extends Node:
	var chat: Array = []
	var net: CompanionNet
	var ui
	var viewer

	func _ready() -> void:
		process_mode = Node.PROCESS_MODE_ALWAYS
		PlayerData.message.connect(func(t: String, k: String): chat.append("%s: %s" % [k, t]))
		_run()

	func _arg(k: String, def: String) -> String:
		for a in OS.get_cmdline_user_args():
			if a.begins_with("--%s=" % k):
				return a.split("=", true, 1)[1]
		return def

	func _shot(tag: String) -> void:
		for i in 10:
			await get_tree().process_frame
		await RenderingServer.frame_post_draw
		var out := _arg("out", "")
		if out != "":
			get_viewport().get_texture().get_image().save_png("%s_%s.png" % [out, tag])

	func _wait(s: float) -> void:
		await get_tree().create_timer(s).timeout

	func _target(oid: int) -> void:
		if Server.client:
			Server.client.call("Target", oid, 0x8000 | 0x6000)
		await _wait(1.0)

	func _npc_named(n: String) -> Npc:
		var sw = viewer.npcs
		for st in sw.states:
			if String((st as NpcState).spawn.get("name", "")).to_lower() == n.to_lower() and st.view is Npc:
				return st.view as Npc
		return null

	func _companion_oid() -> int:
		for rec in net.roster.values():
			if int(rec.get("oid", 0)) != 0:
				return int(rec.oid)
		return 0

	func _alive(oid: int) -> String:
		var st: NpcState = viewer.npcs.by_oid.get(oid)
		if st == null:
			return "not in view"
		return "alive" if st.alive else "dead"

	func _revive_button() -> Button:
		for b in ui.companion_hire.find_children("*", "Button", true, false):
			var bt := b as Button
			if bt.is_visible_in_tree() and bt.text.to_lower().contains("revive"):
				return bt
		return null

	func _run() -> void:
		await _wait(20.0)
		viewer = get_tree().current_scene
		ui = viewer.get("ui")
		if ui == null or ui.companion_net == null:
			print("[epona] no companion windows (world mode %s)" % Server.world_mode)
			_quit()
			return
		net = ui.companion_net
		Server.command("/companionui test")
		await _wait(4.0)
		if net.roster.is_empty():
			await _target(Server.me_oid)
			Server.command("/player level 5")
			await _wait(3.0)
			Server.command("/companionui test")
			await _wait(4.0)
			net.command("hire")
			await _wait(6.0)
		print("[epona] roster ", net.roster)
		if net.roster.is_empty():
			print("[epona] FAIL: no companion")
			_quit()
			return
		var r: int = net.roster.keys()[0]
		if int(net.roster[r].state) == 2:
			net.command("revive %d" % r)                        # (the test window: a companion left dead)
			await _wait(5.0)
		if int(net.roster[r].get("state", 0)) == 0:
			net.command("summon %d" % r)
			await _wait(6.0)
		ui.companion_hire.hide_window()
		await _wait(1.0)
		Server.command("/jump to Epona")
		await _wait(10.0)
		var rounds := int(_arg("rounds", "3"))
		for round in rounds:
			var oid := _companion_oid()
			print("[epona] round %d: companion oid %d %s, roster state %s" % [round + 1, oid, _alive(oid), str(net.roster[r].get("state"))])
			if oid == 0:
				print("[epona] FAIL: the roster names no companion object")
				break
			await _target(oid)
			Server.command("/harm 100000")
			await _wait(4.0)
			print("[epona]   after /harm: %s, roster state %s" % [_alive(oid), str(net.roster[r].get("state"))])
			await _shot("r%d_dead" % (round + 1))
			var epona := _npc_named("Epona")
			if epona == null:
				print("[epona] FAIL: Epona not in view")
				break
			# a right click on Epona (zone_viewer's right click: target and interact)
			viewer.npcs.select(epona)
			viewer.npcs.interact(epona)
			await _wait(4.0)
			print("[epona]   Epona's window: visible %s, healer mode %s" % [ui.companion_hire.visible, net.mode == 1])
			await _shot("r%d_healer" % (round + 1))
			var b := _revive_button()
			if b == null or b.disabled:
				print("[epona] FAIL: no REVIVE button to press (%s)" % ("disabled" if b else "none"))
				break
			chat.clear()
			b.pressed.emit()                                     # the player's click on REVIVE
			await _wait(6.0)
			var now_oid := _companion_oid()
			var v = viewer.npcs._view_of(now_oid)
			var dist := (v as Node3D).global_position.distance_to(viewer.player.global_position) if v else -1.0
			print("[epona]   after REVIVE: oid %d %s, view %s, %.1f m from you, roster state %s, chat %s" % [now_oid,
					_alive(now_oid), "yes" if v else "NO", dist, str(net.roster[r].get("state")), str(chat)])
			await _shot("r%d_revived" % (round + 1))
			ui.companion_hire.hide_window()
			await _wait(3.0)
		print("[epona] done")
		_quit()

	func _quit() -> void:
		Server.command("/quit")
		await _wait(8.0)
		get_tree().quit()
