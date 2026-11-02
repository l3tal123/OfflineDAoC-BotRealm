extends Node
## Live: GM-jump to an NPC by name on the running server and take shots there - 8 all round, then each pair of NPCs
## named --pair (default "Guardsman") standing within 6 m of each other (a guarded doorway) from 12 m in front of them.
## For texture reports (GitHub issue #2: Camelot's round table area drew white walls, a doorway between two Guardsmen).
##   server_session.tscn -- ... --world-mode=1.65 --probe=live_jump_shots_probe --jump=Arthur --out=<prefix>

func _ready() -> void:
	var root := get_tree().root
	if root.has_node("LiveJumpShots"):
		queue_free()
		return
	var d := Driver.new()
	d.name = "LiveJumpShots"
	root.add_child.call_deferred(d)
	queue_free()


class Driver extends Node:
	var viewer

	func _arg(k: String, def: String) -> String:
		for a in OS.get_cmdline_user_args():
			if a.begins_with("--%s=" % k):
				return a.split("=", true, 1)[1]
		return def

	func _ready() -> void:
		process_mode = Node.PROCESS_MODE_ALWAYS
		_run()

	func _wait(s: float) -> void:
		await get_tree().create_timer(s).timeout

	func _shot(cam: Camera3D, from: Vector3, to: Vector3, tag: String) -> void:
		cam.global_position = from
		cam.look_at(to)
		cam.current = true
		await _wait(1.5)
		await RenderingServer.frame_post_draw
		var out := _arg("out", "")
		if out != "":
			get_viewport().get_texture().get_image().save_png("%s_%s.png" % [out, tag])

	func _run() -> void:
		await _wait(20.0)
		viewer = get_tree().current_scene
		var who := _arg("jump", "")
		if who != "":
			Server.command("/jump to %s" % who)
			await _wait(15.0)
		viewer = get_tree().current_scene                  # (a jump to another region loads a new world scene)
		var p: Vector3 = viewer.player.global_position
		print("[shots] at %s: %s (zone %s)" % [who, str(p), str(viewer.zone.info.id)])
		var look := _arg("look", "")
		if look != "":                                    # an exact place (engine metres in --lookzone): jump there
			var lv := look.split(",")
			var lp := Vector3(float(lv[0]), float(lv[1]), float(lv[2]))
			var zid := int(_arg("lookzone", str(viewer.zone.info.id)))       # (--lookzone: the place's zone, e.g. 26 Camelot)
			var g := Server.to_region(zid, lp)
			var region := int(Server.zone_info(zid).get("region", 0))
			Server.command("/jump to %d %d %d %d" % [int(g.x), int(g.y), int(g.z) + 20, region])
			await _wait(30.0)
			viewer = get_tree().current_scene
			if int(viewer.zone.info.id) != zid:
				print("[shots] FAIL: after the jump the zone is %s, not %d" % [str(viewer.zone.info.id), zid])
			p = viewer.player.global_position
			print("[shots] at %s: %s" % [look, str(p)])
			for nm in _arg("names", "").split(";", false):        # NPCs to measure: server height vs where drawn
				for st in viewer.npcs.states:
					if String((st as NpcState).spawn.get("name", "")).to_lower() == nm.to_lower():
						var mv: Dictionary = viewer.npcs._mover.get(st, {})
						var sy := float(mv.get("sy", NAN))
						var v3 = viewer.npcs._view_of(int(st.spawn.get("oid", 0)))
						var dy: float = (v3 as Node3D).global_position.y if v3 else NAN
						print("[shots] %-18s server height %.2f m, drawn at %.2f m (%+.2f); you stand at %.2f m" % [nm, sy, dy,
								dy - sy, p.y])
			var lcam := Camera3D.new()
			viewer.add_child(lcam)
			lcam.far = 2000.0
			lcam.fov = 70.0
			for i in 8:
				var yaw := TAU * i / 8.0
				var ld := float(_arg("ldist", "9"))
				var c := lp + Vector3(sin(yaw) * ld, float(_arg("lup", "1.5")), cos(yaw) * ld)
				await _shot(lcam, c, lp, "look_%d" % i)
		var cam := Camera3D.new()
		viewer.add_child(cam)
		cam.far = 2000.0
		cam.fov = 70.0
		var eye := p + Vector3(0, 1.7, 0)
		for i in 8:
			var yaw := TAU * i / 8.0
			await _shot(cam, eye, eye + Vector3(sin(yaw), -0.05, cos(yaw)) * 10.0, "around_%d" % i)
		var pair := _arg("pair", "Guardsman").to_lower()
		var guards: Array = []
		for st in viewer.npcs.states:
			var n := String((st as NpcState).spawn.get("name", "")).to_lower()
			if n == pair and st.pos.distance_to(p) < 150.0:
				guards.append(st)
		print("[shots] %d '%s' within 150 m" % [guards.size(), pair])
		var k := 0
		for i in guards.size():
			for j in range(i + 1, guards.size()):
				var a: NpcState = guards[i]
				var b: NpcState = guards[j]
				if a.pos.distance_to(b.pos) > 6.0:
					continue
				var mid := (a.pos + b.pos) * 0.5
				var side := (b.pos - a.pos)
				side.y = 0.0
				var front := Vector3(-side.z, 0.0, side.x).normalized()
				for s in [1.0, -1.0]:                             # both sides of the doorway
					var from: Vector3 = mid + front * 12.0 * s + Vector3(0, 2.0, 0)
					await _shot(cam, from, mid + Vector3(0, 4.0, 0), "pair%d_%s" % [k, "a" if s > 0 else "b"])
				print("[shots] pair %d at %s (%.1f m apart)" % [k, str(mid), a.pos.distance_to(b.pos)])
				k += 1
		print("[shots] done")
		Server.command("/quit")
		await _wait(8.0)
		get_tree().quit()
