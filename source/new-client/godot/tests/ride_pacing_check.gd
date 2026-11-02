extends SceneTree
## Offline check of a ridden steed's pacing (server chat 2026-11-01, rubber-banding on horse routes): the server's
## route horse moves at ~60% of the speed its packets say and stalls ~1.5 s now and then. Each server update keeps
## the gap (ServerWorld._move: mv.err = server place - drawn place) and ServerWorld._move_ridden paces it off. Compared
## with the old rule (eased 25% toward the server's place at every update): how often and how far the drawn horse
## stepped BACK along its way, and how far it ends up from the server's place.
##   godot --headless --path godot -s res://tests/ride_pacing_check.gd

const DT := 1.0 / 60.0
const SPEED := 1500.0                          # packet speed (units/s) = 37.5 m/s


func _run(w, steed, mv: Dictionary, old_rule: bool) -> Dictionary:
	steed.pos = Vector3.ZERO
	steed.yaw = PI * 0.5                                        # facing +x, the way it goes
	mv["err"] = Vector3.ZERO
	mv["dest"] = null
	var srv := Vector3.ZERO
	var t := 0.0
	var next_upd := 0.1
	var back := 0
	var worst_back := 0.0
	var worst_gap := 0.0
	while t < 30.0:
		var stalled := t > 10.0 and t < 11.5 or t > 20.0 and t < 21.2
		if not stalled:
			srv.x += SPEED * 0.025 * 0.6 * DT                       # the server's horse: 60% of its packet speed
		if t >= next_upd and not stalled:                          # an update (none while the server stalls)
			next_upd = t + 0.1
			mv["dest"] = srv + Vector3(80, 0, 0)
			mv["sy"] = 0.0
			var before_x: float = steed.pos.x
			if old_rule:
				steed.pos = steed.pos.lerp(srv, 0.25)
			else:
				var e: Vector3 = srv - steed.pos
				e.y = 0.0
				mv["err"] = e
			if steed.pos.x < before_x - 0.001:
				back += 1
				worst_back = maxf(worst_back, before_x - steed.pos.x)
		var x0: float = steed.pos.x
		w._move_ridden(steed, mv, SPEED * 0.025 * DT, DT)
		if steed.pos.x < x0 - 0.001:
			back += 1
			worst_back = maxf(worst_back, x0 - steed.pos.x)
		worst_gap = maxf(worst_gap, absf(steed.pos.x - srv.x))
		t += DT
	return {"back": back, "worst_back": worst_back, "worst_gap": worst_gap, "end_gap": absf(steed.pos.x - srv.x)}


func _initialize() -> void:
	var w = load("res://game/net/server_world.gd").new()
	var ns = load("res://game/npcs/npc_state.gd")
	var steed = ns.new(1, {"name": "Horse", "pos": [0, 0, 0], "heading": 0}, {"figure": "gryphon"})   # (a flier: no ground)
	steed.info = {"figure": "gryphon"}
	var mv := {"speed": SPEED, "sy": 0.0}
	w._mover[steed] = mv
	var old := _run(w, steed, mv, true)
	var now := _run(w, steed, mv, false)
	print("[pacing] old rule: %d steps back (worst %.2f m), gap worst %.1f m, at the end %.1f m" % [old.back, old.worst_back, old.worst_gap, old.end_gap])
	print("[pacing] new rule: %d steps back (worst %.2f m), gap worst %.1f m, at the end %.1f m" % [now.back, now.worst_back, now.worst_gap, now.end_gap])
	# (at 60% server speed the paced horse settles 0.4 x 37.5 m/s / 1 per s = 15 m ahead: the server chat's fix runs the
	# taxi at its real speed, which takes most of that away)
	var ok: bool = now.back == 0 and now.end_gap < 20.0
	print("[pacing] ", "OK" if ok else "FAIL")
	w.free()
	quit(0 if ok else 1)
