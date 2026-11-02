"""Horse route points that both clients draw on the bridge, and fewer of them (owner 2026-11-01: "still dipping in
bridges", "rubberbanding in a way the old client didn't do").

Runs after build_routefloor165.py, on its output (add-routefloor165.json), per route:
1. Client rule. The original client (game.dll) and the new client draw a walking NPC on the first floor found casting
   down from the server's Z + 64, and a stable horse gets one position update per route leg, so for a whole leg that Z
   is the leg's START. Where a leg climbs a bridge ramp more than 64 over its start, the cast starts under the deck and
   the horse is drawn on the river bed / bank below: the "dips" on Prydwen Bridge, though every route point sat on the
   deck. On a spot with more than one floor (a bridge), a leg may climb at most CLIENT_RISE over its start point;
   longer climbs get extra points on the deck. (A single floor is plain ground: the clients keep a rider on their own
   terrain there whatever the server Z.)
2. Fewer points. The floor pass left a point every 20-60 units on long stretches (836 on Campacorentin Station ->
   Camelot East); each point is a new straight leg and a position packet. A point is dropped when the straight line
   past it keeps within XY_TOL sideways, never runs under the floor, floats at most FLOAT over it and obeys the client
   rule, with the same speed and no wait.

Points are rewritten as p165floor-<path>-<n> rows (same ids scheme, renumbered). Read-only on the database (regions).
Usage: python -I refine_routefloor165.py <world db> [route-id-part ...] [--write]"""
import json, math, os, sqlite3, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import groundz165 as g   # noqa: E402

CLIENT_PICK = 64     # the clients' floor pick starts this far over the server Z
CLIENT_RISE = 40     # a leg on a multi-floor spot climbs at most this far over its start (24 under the pick, margin)
LIFT = 8             # route points sit this far over the floor (as build_routefloor165.py)
FLOAT = 40           # the straight line may float this far over the floor
XY_TOL = 24          # a dropped point may lie this far beside the new line
SAMPLE = 20          # floor samples along a leg
MAX_SPAN = 120       # never merge more than this many points into one leg

db_path = sys.argv[1]
write = '--write' in sys.argv
only = [a.lower() for a in sys.argv[2:] if not a.startswith('--')]
if only and write:
    sys.exit('a filtered run must not --write')
db = sqlite3.connect(f'file:{db_path}?mode=ro', uri=True)
SRC = os.path.join(HERE, 'add-routefloor165.json')


def region_of(p):
    row = db.execute("SELECT Region FROM Mob WHERE abs(X-?)<800 AND abs(Y-?)<800 AND abs(Z-?)<600 "
                     "ORDER BY (X-?)*(X-?)+(Y-?)*(Y-?) LIMIT 1", (p['X'], p['Y'], p['Z'], p['X'], p['X'], p['Y'], p['Y'])).fetchone()
    return row[0] if row else None


def riding_floor(region, x, y, z):
    """(floor the line rides on, how many floors there are) at a spot; None when no navmesh covers it."""
    fl = g.floors(region, int(round(x)), int(round(y)))
    if not fl:
        return None, 0
    return min(fl, key=lambda h: abs(h - (z - LIFT))), len(fl)


def samples(a, b):
    L = math.hypot(b['X'] - a['X'], b['Y'] - a['Y'])
    n = max(1, int(L // SAMPLE))
    for k in range(1, n):
        t = k / n
        yield t, a['X'] + (b['X'] - a['X']) * t, a['Y'] + (b['Y'] - a['Y']) * t, a['Z'] + (b['Z'] - a['Z']) * t


def client_ok(region, a, b):
    """The first sample along a->b where a client would draw the horse under the deck, or None."""
    for t, x, y, z in samples(a, b):
        f, n = riding_floor(region, x, y, z)
        if f is not None and n > 1 and abs(f + LIFT - z) <= 120 and f - a['Z'] > CLIENT_RISE:
            return (t, x, y, f)
    return None


def line_ok(region, a, b, dropped):
    if any(p['WaitTime'] or p['MaxSpeed'] != b['MaxSpeed'] for p in dropped):
        return False
    dx, dy = b['X'] - a['X'], b['Y'] - a['Y']
    L2 = dx * dx + dy * dy
    if L2 == 0:
        return False
    for p in dropped:
        t = max(0.0, min(1.0, ((p['X'] - a['X']) * dx + (p['Y'] - a['Y']) * dy) / L2))
        if math.hypot(a['X'] + dx * t - p['X'], a['Y'] + dy * t - p['Y']) > XY_TOL:
            return False
        lz = a['Z'] + (b['Z'] - a['Z']) * t
        if p['Z'] - lz > 2 or lz - p['Z'] > FLOAT:   # the dropped point sits at floor + LIFT
            return False
    for t, x, y, z in samples(a, b):
        f, n = riding_floor(region, x, y, z)
        if f is None or abs(f + LIFT - z) > 120:
            continue                                  # navmesh hole / another floor far off: the points decide
        if f + LIFT - z > 2 or z - (f + LIFT) > FLOAT:
            return False
    return client_ok(region, a, b) is None


def refine(region, pts):
    # 1. thin: from each kept point, the farthest point the straight line can reach
    keep, i = [0], 0
    while i < len(pts) - 1:
        best = i + 1
        for j in range(i + 2, min(len(pts), i + MAX_SPAN + 1)):
            if not line_ok(region, pts[i], pts[j], pts[i + 1:j]):
                break
            best = j
        keep.append(best)
        i = best
    out = [pts[k] for k in keep]
    # 2. client rule on the legs left (an original leg can climb a ramp by itself): a point on the deck before the cast
    #    would start under it
    res, added = [out[0]], 0
    for b in out[1:]:
        for _ in range(64):
            a = res[-1]
            bad = client_ok(region, a, b)
            if bad is None:
                break
            t, x, y, f = bad
            # back off to the last sample still in reach, so the new point is on the deck the horse is climbing
            back = max(SAMPLE, math.hypot(b['X'] - a['X'], b['Y'] - a['Y']) * t - SAMPLE)
            L = math.hypot(b['X'] - a['X'], b['Y'] - a['Y'])
            u = back / L
            px, py = a['X'] + (b['X'] - a['X']) * u, a['Y'] + (b['Y'] - a['Y']) * u
            pf, _ = riding_floor(region, px, py, a['Z'] + (b['Z'] - a['Z']) * u)
            pz = (pf + LIFT) if pf is not None else int(round(a['Z'] + (b['Z'] - a['Z']) * u))
            if pz <= a['Z'] and u * L <= SAMPLE:      # no progress possible: leave it
                break
            res.append({'X': int(round(px)), 'Y': int(round(py)), 'Z': int(round(pz)), 'MaxSpeed': b['MaxSpeed'], 'WaitTime': 0})
            added += 1
        res.append(b)
    return res, added


def audit(region, pts):
    """client-rule violations and legs under 50 units"""
    v = sum(1 for a, b in zip(pts, pts[1:]) if client_ok(region, a, b))
    short = sum(1 for a, b in zip(pts, pts[1:]) if math.hypot(b['X'] - a['X'], b['Y'] - a['Y']) < 50)
    return v, short


src = json.load(open(SRC, encoding='utf-8'))
routes = {}
for r in src['rows']:
    routes.setdefault(r['PathID'], []).append(r)
tot = dict(routes=0, before=0, after=0, viol_before=0, viol_after=0, short_before=0, short_after=0, added=0)
new_rows, now = [], src['rows'][0]['LastTimeRowUpdated'] if src['rows'] else '2026-10-31 12:00:00'
for pid, rows in routes.items():
    rows.sort(key=lambda r: r['Step'])
    region = region_of(rows[0])
    if only and not any(o in pid.lower() for o in only):
        continue
    if region is None:
        out, added = rows, 0
    else:
        vb, sb = audit(region, rows)
        out, added = refine(region, rows)
        va, sa = audit(region, out)
        tot['viol_before'] += vb; tot['viol_after'] += va; tot['short_before'] += sb; tot['short_after'] += sa
        if only:
            print(f'{pid}: points {len(rows)} -> {len(out)}, client dips {vb} -> {va}, legs<50 {sb} -> {sa}, deck points added {added}')
    tot['routes'] += 1; tot['before'] += len(rows); tot['after'] += len(out); tot['added'] += added
    for i, p in enumerate(out, 1):
        new_rows.append({'PathPoints_ID': f'p165floor-{pid}-{i}', 'PathID': pid, 'Step': i, 'X': int(p['X']), 'Y': int(p['Y']),
                         'Z': int(p['Z']), 'MaxSpeed': p['MaxSpeed'] or 0, 'WaitTime': p['WaitTime'] or 0, 'LastTimeRowUpdated': now})
print(tot)
if write:
    for folder in (HERE, os.path.join(HERE, '..', '..', '..', 'runtime', 'true165')):
        json.dump({'table': 'PathPoints', 'key': 'PathPoints_ID',
                   'reason': src['reason'] if 'refine_routefloor165' in src['reason'] else
                             src['reason'] + '; refined for the clients\' floor pick, fewer points (refine_routefloor165.py)',
                   'rows': new_rows}, open(os.path.join(folder, 'add-routefloor165.json'), 'w', encoding='utf-8', newline='\n'), indent=1)
        # A world that already has the longer route lists (v0.2's first build, a dev world) keeps its rows past each
        # route's new end (the add step only writes the rows in the file): those go, or the horse would ride on into them.
        counts = {}
        for r in new_rows:
            counts[r['PathID']] = max(counts.get(r['PathID'], 0), r['Step'])
        tag = 'True 1.65 horse routes: route-floor points past a refined route\'s end'
        rp = os.path.join(folder, 'true165-rules.json')
        raw = open(rp, 'rb').read()
        rules = json.loads(raw.decode('utf-8'))
        rules['remove']['where'] = [w for w in rules['remove']['where'] if not w['reason'].startswith(tag)]
        keep = ','.join("'" + pid.replace("'", "''") + "'" for pid in sorted(counts))
        cond = ' OR '.join(f"(PathID = '{pid.replace(chr(39), chr(39) * 2)}' AND Step > {n})" for pid, n in sorted(counts.items()))
        rules['remove']['where'].append({'table': 'PathPoints', 'key': 'PathPoints_ID',
            # ... and route-floor points of routes no longer in the file (v0.1's first pass also re-pointed 24 routes no
            # stable master sells; a fresh world has none of their rows)
            'where': f"PathPoints_ID LIKE 'p165floor-%' AND (({cond}) OR PathID NOT IN ({keep}))",
            'reason': tag + ' (refine_routefloor165.py: the refined routes have fewer points)'})
        nl = b'\r\n' if b'\r\n' in raw else b'\n'
        open(rp, 'wb').write(json.dumps(rules, indent=1, ensure_ascii=False).encode('utf-8').replace(b'\n', nl) + (nl if raw.endswith(nl) else b''))
    print('written', len(new_rows), 'rows')
