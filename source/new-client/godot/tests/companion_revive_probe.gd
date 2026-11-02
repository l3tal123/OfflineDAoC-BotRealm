extends Node
## Offline: a hired companion revived at a healer (a player report: the client crashed), for every True 1.65 class and
## race it allows, both genders, in class gear (tests/data/companion_revive_cases.json - pipeline/build_revive_cases.py).
## The server's own packets for it, in its order (HiredCompanions165.Revive: health to full, MoveTo beside you =
## RemoveObject + NPCCreate + equipment, OnResurrectedBy: group invite, roster numbers - the companion's hair colour -,
## a spoken line), fed to a ServerWorld as the network client hands them over. Three ways the body can be when it is
## revived: A still lying in view (the health update first), B already faded (a dead body's RemoveObject type 0),
## C in view but the RemoveObject before the health update. Before dying each one casts (hand glows) and is targeted.
##   Godot --path godot -- --skip-login --zone=200 --world-mode=1.65 --probe=companion_revive_probe [--cases=N]
var viewer: Node
var sw: ServerWorld
var zid := 0
var zx := 0.0
var zy := 0.0
var spell := 0                                 # a client effect with hand glows and a target effect
var problems: Array = []


func _ready() -> void:
	await get_tree().create_timer(4.0).timeout
	viewer = get_tree().current_scene
	zid = int(viewer.zone.info.id)
	var zi := Server.zone_info(zid)
	zx = float(zi.get("x", 0)) * 8192.0
	zy = float(zi.get("y", 0)) * 8192.0
	Server.me_oid = 1
	sw = ServerWorld.new()
	sw.name = "ProbeServerWorld"
	viewer.add_child(sw)
	sw.region = viewer.get("region")
	sw.setup_server(viewer, viewer.zone, viewer.player)
	var fx: Dictionary = SpellFx.data().get("effects", {})
	for k in fx:
		var e: Dictionary = fx[k]
		if not (e.get("gather", []) as Array).is_empty() and int(e.get("target", 0)) > 0:
			spell = int(k)
			break
	var cases: Array = JSON.parse_string(FileAccess.get_file_as_string("res://tests/data/companion_revive_cases.json")).cases
	var limit := int(_arg("cases", str(cases.size())))
	cases = cases.slice(0, limit)
	print("[revive] %d cases x 3 ways, spell effect %d, zone %d" % [cases.size(), spell, zid])
	var t0 := Time.get_ticks_msec()
	for way in ["A", "B", "C"]:
		if not way in _arg("ways", "ABC"):
			continue
		for b in range(0, cases.size(), 16):
			var batch := cases.slice(b, b + 16)
			var runs := []
			for i in batch.size():
				runs.append(_run(batch[i], way, 2000 + b + i + {"A": 0, "B": 1000, "C": 2000}[way], i))
			for r in runs:
				if not r.finished:                         # (one may end before its turn to be waited on)
					await r.done
			print("[revive] way %s: cases %d-%d done (%d s)" % [way, b, b + batch.size() - 1, (Time.get_ticks_msec() - t0) / 1000])
	print("[revive] problems: %d" % problems.size())
	for p in problems.slice(0, 40):
		print("[revive]   ", p)
	print("[revive] DONE in %d s" % ((Time.get_ticks_msec() - t0) / 1000))
	get_tree().quit()


func _arg(k: String, def: String) -> String:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--%s=" % k):
			return a.split("=", true, 1)[1]
	return def


class Run:
	signal done
	var finished := false


func _run(c: Dictionary, way: String, oid: int, slot: int) -> Run:
	var r := Run.new()
	_sequence.call_deferred(c, way, oid, slot, r)
	return r


func _where(slot: int, far := false) -> Vector3:
	var p: Vector3 = viewer.player.global_position
	var a := float(slot) / 16.0 * TAU
	var d := 30.0 if far else 6.0 + float(slot % 4)
	return Vector3(p.x + cos(a) * d, p.y, p.z + sin(a) * d)


func _npc(c: Dictionary, oid: int, at: Vector3) -> Dictionary:
	var g := Server.to_region(zid, at)
	return {"kind": "npc", "oid": oid, "speed": 0, "heading": 0, "z": int(g.z), "x": int(g.x), "y": int(g.y),
			"speed_z": 0, "model": int(c.model), "size": 50, "level": 50, "flags": int(c.realm) << 6, "flags2": 0,
			"flags3": 0, "name": "%s%d" % [String(c["class"]).left(6), oid], "guild": ""}


func _move(oid: int, at: Vector3, health: int) -> Dictionary:
	var g := Server.to_region(zid, at)
	return {"kind": "move", "speed": 0, "heading": 0, "x": int(g.x - zx), "tx": 0, "y": int(g.y - zy), "ty": 0,
			"z": int(g.z), "tz": 0, "oid": oid, "target": 0, "health": health, "flags": 0, "zone": zid, "tzone": 0}


func _gear(c: Dictionary, oid: int) -> Dictionary:
	return {"kind": "equipment", "oid": oid, "weapon_slots": 0x10, "speed": 0, "hidden": 0, "hood": 0, "items": c.gear}


func _send(d: Dictionary) -> void:
	if _arg("trace", "") != "":
		print("[revive] send %s oid %s" % [d.kind, d.get("oid", d.get("caster", ""))])
	sw._on_world(String(d.kind), d)


func _view_within(oid: int, ms: int) -> bool:
	var until := Time.get_ticks_msec() + ms
	while Time.get_ticks_msec() < until:
		var v = sw._view_of(oid)
		if v and is_instance_valid(v) and (not (v is Npc) or (v as Npc).model != null):
			return true
		await get_tree().process_frame
	return false


func _frames(n: int) -> void:
	for i in n:
		await get_tree().process_frame


func _sequence(c: Dictionary, way: String, oid: int, slot: int, r: Run) -> void:
	var tag := "%s %s %s r%d g%d oid %d" % [way, c["class"], c.model, int(c.race), int(c.gender), oid]
	var at := _where(slot, way == "B")
	_send(_npc(c, oid, at))
	_send(_gear(c, oid))
	_send(_move(oid, at, 100))
	if not await _view_within(oid, int(_arg("wait", "4000"))):
		var st0: NpcState = sw.by_oid.get(oid)
		problems.append(tag + ": no view in time after its create (info %s)" % (str(st0.info.keys()) if st0 else "no state"))
	var v = sw._view_of(oid)
	if v is Npc and is_instance_valid(v):
		sw.select(v)                                         # (your target when it dies)
	_send({"kind": "cast", "caster": oid, "spell": spell, "time": 3000})   # dies mid-cast: its hand glows
	await _frames(6)
	_send(_move(oid, at, 0))                                   # dead
	await _frames(30)
	if way == "B":
		_send({"kind": "remove", "oid": oid, "type": 0})        # the body fades (HiredCompanions165.DeathTick)
		await _frames(10)
	var beside := _where(slot)
	beside.x += 1.0
	# HiredCompanions165.Revive: health to full, MoveTo beside you (RemoveObject + NPCCreate + gear), then the update
	if way == "A":
		_send(_move(oid, at, 100))
		await _frames(1)
	if way != "B":
		_send({"kind": "remove", "oid": oid, "type": 1})
	_send(_npc(c, oid, beside))
	_send(_gear(c, oid))
	_send({"kind": "spell_effect", "caster": oid, "spell": spell, "target": oid, "bolt": 0, "no_sound": 0, "success": 1})
	await _frames(1)
	var st: NpcState = sw.by_oid.get(oid)
	if st:                                                     # the roster numbers: its hair colour (companion_hair)
		st.spawn["hair_index"] = 3
		var nv = sw._view_of(oid)
		if nv and is_instance_valid(nv) and nv.has_method("set_hair_index"):
			nv.set_hair_index(3)
	_send(_move(oid, beside, 100))
	_send({"kind": "message", "type": 1, "text": "Back among the living.", "popup": false, "chat": false})
	await _view_within(oid, int(_arg("wait", "4000")))
	await _frames(10)
	var nv2 = sw._view_of(oid)
	if nv2 == null or not is_instance_valid(nv2):
		problems.append(tag + ": no view after the revive")
	else:
		var n := nv2 as Npc
		if n and n.state and not n.state.alive:
			problems.append(tag + ": still dead after the revive")
		if n and n.corpse:
			problems.append(tag + ": the revived view is a sinking corpse")
		if n and n.model == null:
			problems.append(tag + ": the revived view has no figure")
		if n and n.position.distance_to(beside) > 3.0:
			problems.append(tag + ": the revived view is %.1f m from where the server put it" % n.position.distance_to(beside))
	_send({"kind": "remove", "oid": oid, "type": 1})           # (cleared for the next batch)
	await _frames(2)
	r.finished = true
	r.done.emit()
