"""BotRealm: clear the traveler's inn sites and the walk to them (owner 2026-10-30: bogmen wandered into the Albion
inn yard and the guards kept fighting them; "no aggressive mobs on the route or nearby ... a low level player is
expected to get there"). As the old Rebuilt project did (inns.json "moved"), the same monsters, levels and numbers
stay; only where they stand changes:
- every wild monster spawning within CLEAR of an inn site moves out to MOVE_TO along its own direction from the site;
- every AGGRESSIVE monster (its row or its NPC template has aggro) within ROUTE_CLEAR of the walking route to the inn
  (layout paths "<town> -> yard") moves sideways out to ROUTE_MOVE_TO from the route.
Each moved monster is set on the ground at its new spot (navmesh height; owner 2026-10-30: moved with its old height,
a blodfelag oathbreaker floated 200 units up near Huginfell). A monster an earlier run moved that no longer needs moving (the site moved) is put back where it spawned originally.
Positions come from the normal database (the original spawn spots). Writes the moves as "updates" into
true165-rules.json (tools copy and runtime\\botrealm), replacing earlier inn-clearing entries, so the launcher's content
refresh applies them to the existing 1.65 world.

    python -I build_innclear165.py
"""
import json, math, os, re, sqlite3
from groundz165 import ground_z, nearest_ground

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
LAYOUTS = os.path.normpath(os.path.join(ROOT, '..', '1.65 MODE CLIENT (local only)', 'layouts'))
DB = os.path.join(ROOT, 'runtime', 'data', 'opendaoc.sqlite3.db')   # the original spawn spots (the 1.65 DB has them moved)
CLEAR, MOVE_TO = 3000, 3300
# the horn platform and the realm exchange get their own cleared circle (owner 2026-10-30: at Midgard they stand
# 2700 and 3500 units from the inn, past its circle, and the monsters moved out of it landed around the horn)
FEATURE_CLEAR = 2000
MARGIN = 300
ROUTE_CLEAR, ROUTE_MOVE_TO = 700, 900
MAX_CLIMB = 300   # a moved spawn stays within this of its own height (not up on a cliff)
TAG = '1.65 inn clearing:'
RULES = [os.path.join(HERE, 'true165-rules.json'), os.path.join(ROOT, 'runtime', 'botrealm', 'true165-rules.json')]


def nearest_on_segment(px, py, ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    t = 0.0 if dx == dy == 0 else max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
    return ax + t * dx, ay + t * dy


def main():
    con = sqlite3.connect(f'file:{DB}?mode=ro', uri=True)
    aggressive_templates = {r[0] for r in con.execute("SELECT TemplateId FROM NpcTemplate WHERE AggroLevel > 0")}
    moves = {}   # Mob_ID -> (region, x, y, spawn z, reason)
    keep_out = {}   # region -> the cleared circles
    for realm in ('alb', 'mid', 'hib'):
        layout = json.load(open(os.path.join(LAYOUTS, f'inn_{realm}.json'), encoding='utf-8'))
        spots = layout['npc_spots']
        site = spots['trader_roam']['world']
        cx, cy, region = site['x'], site['y'], layout['region']
        circles = [(site['x'], site['y'], CLEAR, 'inn'),
                   (spots['horn_handler']['world']['x'], spots['horn_handler']['world']['y'], FEATURE_CLEAR, 'horn'),
                   (spots['exchange_brokers'][0]['world']['x'], spots['exchange_brokers'][0]['world']['y'], FEATURE_CLEAR, 'exchange')]

        keep_out[region] = circles

        def outside_all(px, py):
            return all(math.dist((px, py), (ox, oy)) >= r + MARGIN for ox, oy, r, _ in circles)

        # the grounds: every wild spawn inside any circle moves out along its own direction from that circle's centre
        # until it stands clear of every circle
        xs = [c[0] for c in circles]; ys = [c[1] for c in circles]; R = max(c[2] for c in circles)
        for mob_id, name, x, y, z in con.execute(
                "SELECT Mob_ID, Name, X, Y, Z FROM Mob WHERE Region=? AND Realm=0 AND X BETWEEN ? AND ? AND Y BETWEEN ? AND ?",
                (region, min(xs) - R, max(xs) + R, min(ys) - R, max(ys) + R)):
            inside = [(math.dist((x, y), (ox, oy)), ox, oy, r, what) for ox, oy, r, what in circles if math.dist((x, y), (ox, oy)) < r]
            if not inside:
                continue
            d, ox, oy, r, what = min(inside)
            ux, uy = ((x - ox) / d, (y - oy) / d) if d > 1 else (1.0, 0.0)
            step = r + MARGIN
            while not outside_all(ox + ux * step, oy + uy * step):
                step += 100
            moves[mob_id] = (region, int(ox + ux * step), int(oy + uy * step), z,
                             f"{TAG} {name} moved from {int(d)} to {int(step)} units off the {realm} {what}")
        # the walk there: aggressive spawns near the route
        routes = [p['points'] for p in layout.get('paths', [])
                  if re.match(r'^(?!.*door)(?!yard).* -> yard$', p['name'])]
        for points in routes:
            pts = [(p['world']['x'], p['world']['y']) for p in points]
            xs, ys = [p[0] for p in pts], [p[1] for p in pts]
            for mob_id, name, x, y, z, aggro, template in con.execute(
                    "SELECT Mob_ID, Name, X, Y, Z, AggroLevel, NPCTemplateID FROM Mob WHERE Region=? AND Realm=0 AND "
                    "X BETWEEN ? AND ? AND Y BETWEEN ? AND ?",
                    (region, min(xs) - ROUTE_CLEAR, max(xs) + ROUTE_CLEAR, min(ys) - ROUTE_CLEAR, max(ys) + ROUTE_CLEAR)):
                if mob_id in moves or not ((aggro or 0) > 0 or template in aggressive_templates):
                    continue
                best = min((nearest_on_segment(x, y, *a, *b) for a, b in zip(pts, pts[1:])),
                           key=lambda q: math.dist(q, (x, y)), default=None)
                if best is None or math.dist(best, (x, y)) >= ROUTE_CLEAR:
                    continue
                d = math.dist(best, (x, y))
                ux, uy = ((x - best[0]) / d, (y - best[1]) / d) if d > 1 else (1.0, 0.0)
                moves[mob_id] = (region, int(best[0] + ux * ROUTE_MOVE_TO), int(best[1] + uy * ROUTE_MOVE_TO), z,
                                 f"{TAG} {name} (aggressive) moved {int(d)} -> {ROUTE_MOVE_TO} units off the {realm} inn route")
    updates = []
    for mob_id, (region, x, y, z, reason) in moves.items():
        spot = nearest_ground(region, x, y, z)
        if spot is not None and abs(spot[2] - z) > MAX_CLIMB:
            # a cliff or hilltop at the new spot: take the nearest spot at about its own level, still clear of the circles
            better = [(r, px, py, gz) for r in range(100, 3001, 100) for k in range(24)
                      for px, py in [(int(x + math.cos(k * math.pi / 12) * r), int(y + math.sin(k * math.pi / 12) * r))]
                      if all(math.dist((px, py), (ox, oy)) >= cr + MARGIN for ox, oy, cr, _ in keep_out[region])
                      for gz in [ground_z(region, px, py, z)] if gz is not None and abs(gz - z) <= MAX_CLIMB]
            if better:
                spot = min(better)[1:]
        if spot is None:
            print('  no navmesh near', reason)
            spot = (x, y, z)
        x, y, gz = spot
        updates.append({'sql': f"UPDATE Mob SET X = {x}, Y = {y}, Z = {gz} WHERE Mob_ID = '{mob_id}'",
                        'reason': f"{reason} (ground {gz}, spawned at {z})"})
    # put back what an earlier run moved and this one doesn't (the site or route moved)
    previous = set()
    for path in RULES:
        if os.path.exists(path):
            for u in json.load(open(path, encoding='utf-8')).get('updates', []):
                if u['reason'].startswith(TAG):
                    m = re.search(r"Mob_ID = '([^']+)'", u['sql'])
                    if m:
                        previous.add(m.group(1))
    for mob_id in sorted(previous - set(moves)):
        row = con.execute("SELECT X, Y, Name, Z FROM Mob WHERE Mob_ID=?", (mob_id,)).fetchone()
        if row:
            updates.append({'sql': f"UPDATE Mob SET X = {row[0]}, Y = {row[1]}, Z = {row[3]} WHERE Mob_ID = '{mob_id}'",
                            'reason': f"{TAG} restore {row[2]} to its own spawn spot (no longer near an inn or its route)"})
    for path in RULES:
        if not os.path.exists(path):
            continue
        rules = json.load(open(path, encoding='utf-8'))
        kept = [u for u in rules.get('updates', []) if not u['reason'].startswith(TAG)]
        rules['updates'] = kept + updates
        json.dump(rules, open(path, 'w', encoding='utf-8'), indent=1)
    print(f'{len(updates)} inn clearing moves')
    for u in updates:
        print(' ', u['reason'])


if __name__ == '__main__':
    main()
