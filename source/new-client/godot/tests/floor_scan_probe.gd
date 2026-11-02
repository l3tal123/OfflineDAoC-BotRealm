extends Node
## Offline: for every candidate NPC of this zone (pipeline/floor_candidates.py: stored off the terrain), where the game
## draws it with the floor pick cast from 1.2 m over its stored height (before) and from 64 units = 1.6 m (now, as the
## 1.64 client) - ServerWorld._ground's rule: the picked floor when within 4 m of the stored height, else the stored
## height. Those that move more than 0.3 m go to --out as JSON lines (GitHub issue #4 follow-up).
##   --skip-login --zone=N --pos=x,y,z --world-mode=1.65 --probe=floor_scan_probe --file=<candidates.json> --out=<file>
var v: Node


func _pick(at: Vector3, up: float) -> float:
	var g: float = v.zone.height_at(at.x, at.z)
	var q := PhysicsRayQueryParameters3D.create(at + Vector3(0, up, 0), at - Vector3(0, 6, 0), ZoneLoader.LAYER_FLOOR)
	var hit: Dictionary = v.zone.get_world_3d().direct_space_state.intersect_ray(q)
	var f: float = maxf(g, float(hit.position.y)) if not hit.is_empty() else g
	return f if f > -INF and absf(f - at.y) < 4.0 else at.y


func _ready() -> void:
	v = get_tree().current_scene
	await get_tree().create_timer(2.0).timeout
	for i in 10:
		await get_tree().physics_frame
	var zid := str(int(v.zone.info.id))
	var all = JSON.parse_string(FileAccess.get_file_as_string(String(v.args.get("file", ""))))
	var list: Array = all.get(zid, []) if all is Dictionary else []
	var out := FileAccess.open(String(v.args.get("out", "user://floor_scan.jsonl")), FileAccess.WRITE)
	var moved := 0
	for c in list:
		var at := Vector3(float(c[1]), float(c[2]), float(c[3]))
		var before := _pick(at, 1.2)
		var now := _pick(at, 64 * 0.025)
		if absf(now - before) > 0.3:
			moved += 1
			out.store_line(JSON.stringify({"zone": int(zid), "name": c[0], "at": [at.x, at.y, at.z],
					"before": snappedf(before, 0.01), "now": snappedf(now, 0.01)}))
	out.close()
	print("[scan] zone %s: %d candidates, %d drawn at another height now" % [zid, list.size(), moved])
	get_tree().quit()
