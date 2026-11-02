extends Node
## Live test of a stable horse ride (owner 2026-11-02, through the server chat: "make sure it gets proper screenshots /
## data and doesn't hallucinate route issues because it sees in screenshots"): Claude's test character jumps to the
## stable master, buys the ticket as a player does (his merchant window, the ticket's row) and rides. Every 0.1 s a
## JSON line: the horse's server place (its last update), where it is drawn, the height the client picked, and the
## deck under it (the highest floor more than 1 m over the terrain at that spot - a bridge). A DIP is only the drawn
## horse more than 0.25 m under such a deck; RUBBER-BANDING only a drawn step backwards along the way it goes.
## Shots: on each deck start / end, once mid-deck, every 25 s.
##   Godot --path godot res://tests/server_session.tscn -- --server=127.0.0.1 --account=<a> --password=<p>
##       --char=<test character> --world-mode=1.65 --probe=live_ride_probe --out=<prefix>
##       [--master=Idian] [--ticket=Ticket to East Camelot Gates] [--max=420]

func _ready() -> void:
	var root := get_tree().root
	if root.has_node("LiveRideProbe"):
		queue_free()
		return
	var d := Driver.new()
	d.name = "LiveRideProbe"
	root.add_child.call_deferred(d)
	queue_free()


class Driver extends Node:
	var viewer
	var ui
	var log_file: FileAccess
	var chat: Array = []

	func _ready() -> void:
		process_mode = Node.PROCESS_MODE_ALWAYS
		PlayerData.message.connect(func(t: String, k: String): chat.append("%s: %s" % [k, t]))
		_run()

	func _arg(k: String, def: String) -> String:
		for a in OS.get_cmdline_user_args():
			if a.begins_with("--%s=" % k):
				return a.split("=", true, 1)[1]
		return def

	func _wait(s: float) -> void:
		await get_tree().create_timer(s).timeout

	func _shot(tag: String) -> void:
		await RenderingServer.frame_post_draw
		var out := _arg("out", "")
		if out != "":
			get_viewport().get_texture().get_image().save_png("%s_%s.png" % [out, tag])

	func _npc_named(n: String) -> Npc:
		for st in viewer.npcs.states:
			if String((st as NpcState).spawn.get("name", "")).to_lower() == n.to_lower() and st.view is Npc:
				return st.view as Npc
		return null

	func _steed() -> NpcState:
		var oid := int(viewer.get("_server_steed"))
		return viewer.npcs.by_oid.get(oid) if oid != 0 else null

	## The deck at (x, z): the highest floor-layer surface more than 1 m above the terrain, else NAN.
	func _deck(x: float, z: float) -> float:
		var zone = viewer.zone
		var g: float = zone.height_at(x, z)
		var space = viewer.get_world_3d().direct_space_state
		var q := PhysicsRayQueryParameters3D.create(Vector3(x, g + 60.0, z), Vector3(x, g - 2.0, z), ZoneLoader.LAYER_FLOOR)
		var hit: Dictionary = space.intersect_ray(q)
		if hit.is_empty() or float(hit.position.y) < g + 1.0:
			return NAN
		return float(hit.position.y)

	func _run() -> void:
		await _wait(20.0)
		viewer = get_tree().current_scene
		ui = viewer.get("ui")
		var master := _arg("master", "Idian")
		var ticket := _arg("ticket", "Ticket to East Camelot Gates")
		log_file = FileAccess.open(_arg("out", "user://ride") + ".jsonl", FileAccess.WRITE)
		if _arg("realm", "") != "":                     # (a GM step: the test character in the route's realm for the ride)
			Server.client.call("Target", Server.me_oid, 0x8000 | 0x6000)
			await _wait(1.0)
			Server.command("/player realm %s" % _arg("realm", ""))
			await _wait(3.0)
			Server.command("/player money gold 20")                 # (a ticket's price, if it has none)
			await _wait(2.0)
		Server.command("/jump to %s" % master)
		await _wait(14.0)
		viewer = get_tree().current_scene                  # (a region change replaces the scene)
		ui = viewer.get("ui")
		var m := _npc_named(master)
		if m == null:
			print("[ride] FAIL: %s not in view" % master)
			_quit()
			return
		print("[ride] money before: ", PlayerData.money)
		viewer.npcs.select(m)
		viewer.npcs.interact(m)                                # (a right click: his merchant window)
		await _wait(4.0)
		var slot := -1
		var window := 0
		var pages: Dictionary = viewer.npcs.get("_merchant_pages")
		for pg in pages:
			for iid in pages[pg]:
				if String(ItemDb.item(String(iid)).get("name", "")).to_lower() == ticket.to_lower():
					slot = int(pg) * 30 + int(String(iid).get_slice(":", 2))
		window = int(ui.merchant.get("server_window")) if ui.merchant else 0
		if slot < 0:
			var names := []
			for pg in pages:
				for iid in pages[pg]:
					names.append(String(ItemDb.item(String(iid)).get("name", "")))
			print("[ride] FAIL: no '%s' in %s's window; it lists %s; merchant window visible %s; chat %s" % [ticket,
					master, str(names), str(ui.merchant.visible) if ui.merchant else "none", str(chat.slice(maxi(0, chat.size() - 8)))])
			_quit()
			return
		var r: Vector3 = Server.to_region(viewer.npcs._current_zone(), viewer.player.global_position)
		Server.client.call("BuyItem", int(r.x), int(r.y), slot, 1, maxi(window, 0))
		print("[ride] bought '%s' (slot %d, window %d)" % [ticket, slot, window])
		await _shot("0_bought")
		var t0 := Time.get_ticks_msec()
		var started := -1
		var prev := Vector3.INF
		var on_deck := false
		var deck_since := 0
		var mid_shot := false
		var next_periodic := 25000
		var n := 0
		var back := 0
		var worst_back := 0.0
		var dips := 0
		var worst_dip := 0.0
		var deck_samples := 0
		var max_t := float(_arg("max", "420")) * 1000.0
		while Time.get_ticks_msec() - t0 < max_t:
			await _wait(0.1)
			var st := _steed()
			var now := Time.get_ticks_msec() - t0
			if st == null:
				if started >= 0:
					print("[ride] the ride ended at %.1f s (you dismounted / arrived)" % (now / 1000.0))
					break
				continue
			if started < 0:
				started = now
				print("[ride] mounted at %.1f s" % (now / 1000.0))
				await _shot("1_mounted")
			var mv: Dictionary = viewer.npcs._mover.get(st, {})
			var srv: Vector3 = mv.get("srv_pos", Vector3.INF)
			var dest = mv.get("dest")
			var drawn: Vector3 = st.pos
			var deck := _deck(drawn.x, drawn.z)
			var below := 0.0
			if not is_nan(deck):
				deck_samples += 1
				below = deck - drawn.y
				if below > 0.25:
					dips += 1
					worst_dip = maxf(worst_dip, below)
			var step_back := 0.0
			if prev != Vector3.INF:
				var dir := Vector3.ZERO
				if dest is Vector3:
					dir = Vector3((dest as Vector3).x - prev.x, 0, (dest as Vector3).z - prev.z)
				if dir.length() < 0.5:
					dir = Vector3(sin(st.yaw), 0, cos(st.yaw))
				var along := (drawn - prev).dot(dir.normalized())
				if along < -0.05:
					step_back = -along
					back += 1
					worst_back = maxf(worst_back, step_back)
			prev = drawn
			n += 1
			var rec := {"t": snappedf(now / 1000.0, 0.01), "srv": [snappedf(srv.x, 0.01), snappedf(srv.y, 0.01), snappedf(srv.z, 0.01)],
					"drawn": [snappedf(drawn.x, 0.01), snappedf(drawn.y, 0.01), snappedf(drawn.z, 0.01)],
					"srv_z": snappedf(float(mv.get("sy", NAN)), 0.01), "speed": int(mv.get("speed", 0)),
					"gap": snappedf(Vector2(srv.x - drawn.x, srv.z - drawn.z).length(), 0.01) if srv != Vector3.INF else -1.0,
					"deck": null if is_nan(deck) else snappedf(deck, 0.01), "below_deck": snappedf(below, 0.01),
					"back": snappedf(step_back, 0.01)}
			log_file.store_line(JSON.stringify(rec))
			if (not is_nan(deck)) != on_deck:
				on_deck = not on_deck
				deck_since = now
				mid_shot = false
				await _shot("deck_%s_%03d" % ["start" if on_deck else "end", int(now / 1000.0)])
			elif on_deck and not mid_shot and now - deck_since > 3000:
				mid_shot = true
				await _shot("deck_mid_%03d" % int(now / 1000.0))
			if now >= next_periodic:
				next_periodic += 25000
				await _shot("t%03d" % int(now / 1000.0))
		log_file.close()
		print("[ride] %d samples, %d on a deck; drawn steps backwards: %d (worst %.2f m); under the deck by > 0.25 m: %d (worst %.2f m)"
				% [n, deck_samples, back, worst_back, dips, worst_dip])
		print("[ride] chat: ", chat.slice(maxi(0, chat.size() - 12)))
		print("[ride] done")
		_quit()

	func _quit() -> void:
		if _arg("restore_realm", "") != "":              # (the test character back in its own realm)
			Server.client.call("Target", Server.me_oid, 0x8000 | 0x6000)
			await _wait(1.0)
			Server.command("/player realm %s" % _arg("restore_realm", ""))
			await _wait(3.0)
		Server.command("/quit")
		await _wait(8.0)
		get_tree().quit()
