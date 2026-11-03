"""Horse routes that follow the floor (owner 2026-10-31: gamebot horses clipped through the bridge near Cotswold).
A route is a list of points the horse moves between in straight lines. Where a long segment crosses an arched bridge
(Prydwen Bridge: the line runs flat at 2072 while the deck rises to ~2373 over a river bed at ~1901) the horse goes
through the deck. This adds points along every such stretch at the walkable floor's height, following the floor the
route is already on (the deck, not the river bed below it), from the server's navmeshes (groundz165).

Output: add-routefloor165.json (table PathPoints, key PathPoints_ID): every point of each changed route, re-stepped,
with ids p165floor-<path>-<n>; true165-rules.json gets one "where" rule that archives the original points of exactly
those routes (ids not starting with p165floor-), so a refresh never doubles them. Read-only on the database it reads.
Usage: python -I build_routefloor165.py <world db> [route-id-part ...] [--write] [--dump]
  route-id-parts only test those routes (never with --write); --dump writes routefloor165_dump.json for the dip check"""
import json, math, os, sqlite3, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import groundz165 as g   # noqa: E402

STEP = 40             # sample spacing along a segment
DEVIATION = 45        # a sample this far BELOW the line (the horse floating over a dip) is a real height change
ABOVE = 2             # a floor this far ABOVE the line is the horse sinking into it: never allowed (owner 2026-10-31:
                      # "dipped below a bridge over and over"; the first pass let up to 45 through on bridge decks)
MAX_CLIMB = 70        # max difference between the tracked floor and a route point at a segment's end
STEP_UP = 90          # max floor change between two samples: Prydwen Bridge's ramp climbs ~75 per 40 (2026-11-01: at
                      # 60 the deck was out of reach and the track followed the bank into the river); a wall top is more
MIN_SEGMENT = 80      # shorter segments are left alone
LIFT = 8              # added points sit this far above the floor
DECK_REACH = 500      # a deck up to this far above the line is lifted onto (Prydwen Bridge: deck 2373 over a line at
                      # 2072, 301 above; the first pass stopped at 250)

db_path = sys.argv[1]
write = '--write' in sys.argv
db = sqlite3.connect(f'file:{db_path}?mode=ro', uri=True)


def route_ids():
    """Every route a stable master sells a HORSE ticket for (the ticket's Id_nb is the route; its Color is the mount's
    NPC template, 0 = the stable's own horse). 2026-11-01 (owner: horses still sank under Prydwen Bridge and the bridge
    into Camelot): the first pass took only HS_ and ticket_to routes, so the 28 HR_ horse routes (Campacorentin
    Station -> Camelot over Prydwen Bridge, Snowdonia Station -> Camelot...) were never fixed. Flying mounts (dragonfly,
    gryphon, wyvern) and boats are never touched: they don't walk on the floor."""
    templates = {r[0]: (r[1] or '').lower() for r in db.execute("SELECT TemplateId, Name FROM NpcTemplate")}
    sold = {}
    for iid, color in db.execute(
            "SELECT t.Id_nb, t.Color FROM MerchantItem mi JOIN Mob m ON m.ItemsListTemplateID = mi.ItemListID "
            "JOIN ItemTemplate t ON lower(t.Id_nb) = lower(mi.ItemTemplateID) WHERE m.ClassType LIKE '%GameStableMaster%'"):
        mount = templates.get(color, 'horse') if color else 'horse'
        if 'horse' in mount:
            sold[iid.lower()] = True
    return [pid for (pid,) in db.execute("SELECT PathID FROM Path WHERE PathType = 1") if pid.lower() in sold]


def region_of(points):
    x, y, z = points[0][1:4]
    row = db.execute("SELECT Region FROM Mob WHERE abs(X-?)<800 AND abs(Y-?)<800 AND abs(Z-?)<600 "
                     "ORDER BY (X-?)*(X-?)+(Y-?)*(Y-?) LIMIT 1", (x, y, z, x, x, y, y)).fetchone()
    return row[0] if row else None


def track(region, a, b):
    """Floor heights along a->b, following the surface the route starts on; None when unknown."""
    length = math.hypot(b[0] - a[0], b[1] - a[1])
    n = max(1, int(length // STEP))
    zs, prev, gap = [], a[2], 0
    for i in range(1, n):
        t = i / n
        x, y = a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t
        cands = [z for z in {g.ground_z(region, x, y, prev - 600), g.ground_z(region, x, y, prev),
                             g.ground_z(region, x, y, prev + 600)} if z is not None]
        if not cands:
            # a short hole in the navmesh (a rock, a post) is stepped over at the last floor height; only a longer one
            # loses the surface (2026-11-01: one empty 40-unit sample made the whole 2,312-unit stretch over the rise
            # into Camelot keep its straight line, 70 under the road)
            gap += 1
            if gap > 5:
                return None
            continue
        gap = 0
        # Of the surfaces a horse can step onto from here, the highest: a road goes over a bridge, not under it into
        # the river (Prydwen Bridge's deck climbs ~75 per sample while the bank beside it drops into the river). Wall
        # tops and arches are far higher than one step, so they are never taken.
        layers_here = [h for h in g.floors(region, int(round(x)), int(round(y))) if prev - 600 <= h <= prev + 600]
        if len(layers_here) == 1 and abs(layers_here[0] - prev) <= 300:
            # only one floor here (open ground, no deck over anything): follow it however steep, as the original client
            # never draws an NPC below the terrain (2026-11-01: a Huginfel route cut 250 under a hill whose slope
            # climbed 120 in the first 40 units, past STEP_UP)
            z = layers_here[0]
            zs.append((x, y, z))
            prev = z
            continue
        reach = [c for c in cands if abs(c - prev) <= STEP_UP]
        if not reach:
            return None            # lost the surface (a cliff, a gap in the mesh): leave this segment as it is
        z = max(reach)
        zs.append((x, y, z))
        prev = z
    if abs(prev - b[2]) > MAX_CLIMB * 2:
        return None
    return zs


def simplify(profile, eps=25):
    """Douglas-Peucker on (distance along, height): only the points that shape the floor's profile. One-sided: the
    simplified line may float up to eps over the floor but never runs ABOVE+0 under it."""
    if len(profile) < 3:
        return profile
    (s0, z0), (s1, z1) = profile[0], profile[-1]
    worst, wi = 0, 0
    for i in range(1, len(profile) - 1):
        s, z = profile[i]
        line = z0 + (z1 - z0) * (s - s0) / (s1 - s0) if s1 != s0 else z0
        err = (z - line) * (eps / ABOVE) if z > line else line - z   # under the floor counts eps/ABOVE times more
        if err > worst:
            worst, wi = err, i
    if worst <= eps:
        return [profile[0], profile[-1]]
    return simplify(profile[:wi + 1], eps)[:-1] + simplify(profile[wi:], eps)


def layers(region, x, y, z):
    return [h for h in g.floors(region, int(round(x)), int(round(y))) if z - 900 <= h <= z + 600]


def deck_pass(region, out):
    """Last check over the finished route: wherever the line still runs under a deck that has a floor below it (a
    bridge), a point goes in on the deck. Repeated until nothing is under one (owner 2026-10-31: no dipping at all)."""
    added = 0
    for _ in range(4):
        res, more = [out[0]], 0
        for a, b in zip(out, out[1:]):
            n = max(1, int(math.hypot(b[1] - a[1], b[2] - a[2]) // 20))
            for k in range(1, n):
                t = k / n
                x, y, z = a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t, a[3] + (b[3] - a[3]) * t
                ls = layers(region, x, y, z)
                above = [h for h in ls if z + 2 < h + LIFT <= z + DECK_REACH]
                # a deck with a floor below it (a bridge), or the only floor here up to 100 over the line (a stretch the
                # tracker could not follow, e.g. leaving Svasud Faste's plateau for an unmeshed ramp: 39 under the floor)
                if above and (any(h < z - 60 for h in ls) or len(ls) == 1 and ls[0] + LIFT - z <= 100):
                    res.append((None, int(round(x)), int(round(y)), int(round(max(above) + LIFT)), a[4], 0))
                    more += 1
            res.append(b)
        out = res
        added += more
        if not more:
            break
    return out, added


changed, stats = {}, {'routes': 0, 'segments_fixed': 0, 'points_added': 0, 'no_region': 0, 'deck_points': 0}
only = [a.lower() for a in sys.argv[2:] if not a.startswith('--')]
if only and write:
    sys.exit('a filtered run must not --write (it would drop every other route)')
for pid in route_ids():
    if only and not any(o in pid.lower() for o in only):
        continue
    pts = db.execute("SELECT Step,X,Y,Z,MaxSpeed,WaitTime FROM PathPoints WHERE PathID=? ORDER BY Step", (pid,)).fetchall()
    if len(pts) < 2:
        continue
    region = region_of(pts)
    if region is None:
        stats['no_region'] += 1
        continue
    stats['routes'] += 1
    # A route point under the floor at its own spot (no floor near its height, one above it) is lifted onto that floor
    # first, so each segment ends on the floor (2026-11-01: the last point into Ludlow sat 157 under the road, so the
    # end check refused the whole approach and it kept running underground).
    lifted = []
    for p in pts:
        here = g.floors(region, p[1], p[2])
        if here and not any(p[3] - 60 <= h <= p[3] + LIFT for h in here):
            up = [h for h in here if p[3] + LIFT < h <= p[3] + 400]
            if up:
                p = (p[0], p[1], p[2], min(up) + LIFT, p[4], p[5])
                stats['points_lifted'] = stats.get('points_lifted', 0) + 1
        lifted.append(p)
    if lifted != list(pts):
        fixed_points = True
    else:
        fixed_points = False
    pts = lifted
    out, fixed = [pts[0]], fixed_points
    for a, b in zip(pts, pts[1:]):
        A, B = (a[1], a[2], a[3]), (b[1], b[2], b[3])
        if math.hypot(B[0] - A[0], B[1] - A[1]) >= MIN_SEGMENT:
            zs = track(region, A, B)
            if zs:
                seg = math.hypot(B[0] - A[0], B[1] - A[1])
                # the line's height at each sample's own position (a stepped-over navmesh hole leaves no sample)
                line = [A[2] + (B[2] - A[2]) * math.hypot(x - A[0], y - A[1]) / seg for x, y, _ in zs]
                sink = max(z + LIFT - l for (_, _, z), l in zip(zs, line))
                float_ = max(l - z for (_, _, z), l in zip(zs, line))
                if sink > ABOVE or float_ > DEVIATION:
                    full = [A] + [(x, y, z + LIFT) for x, y, z in zs] + [B]
                    prof = [(math.hypot(p[0] - A[0], p[1] - A[1]), p[2]) for p in full]
                    keep = {round(s_, 3) for s_, _ in simplify(prof)}
                    zs = [p for p, (s_, _) in zip(full[1:-1], prof[1:-1]) if round(s_, 3) in keep]
                    for x, y, z in zs:
                        out.append((None, int(round(x)), int(round(y)), int(round(z)), a[4], 0))
                    stats['segments_fixed'] += 1
                    stats['points_added'] += len(zs)
                    fixed = True
        out.append(b)
    out, deck = deck_pass(region, out)
    if deck:
        stats['deck_points'] += deck
        fixed = True
    # A route re-pointed by an earlier pass stays in the output (its original points are archived by the rule below).
    if fixed or db.execute("SELECT 1 FROM PathPoints WHERE PathID=? AND PathPoints_ID LIKE 'p165floor-%' LIMIT 1", (pid,)).fetchone():
        changed[pid] = out

print(stats, 'routes changed', len(changed))
if '--dump' in sys.argv:
    json.dump({pid: [[p[1], p[2], p[3]] for p in out] for pid, out in changed.items()}, open(os.path.join(HERE, 'routefloor165_dump.json'), 'w'))
if write:
    now = '2026-10-31 12:00:00'
    rows = []
    for pid, out in changed.items():
        for i, p in enumerate(out, 1):
            rows.append({'PathPoints_ID': f'p165floor-{pid}-{i}', 'PathID': pid, 'Step': i, 'X': p[1], 'Y': p[2], 'Z': p[3],
                         'MaxSpeed': p[4] or 0, 'WaitTime': p[5] or 0, 'LastTimeRowUpdated': now})
    for folder in (HERE, os.path.join(HERE, '..', '..', '..', 'runtime', 'botrealm')):
        json.dump({'table': 'PathPoints', 'key': 'PathPoints_ID',
                   'reason': 'True 1.65 horse routes follow the floor over bridges and dips (build_routefloor165.py)',
                   'rows': rows}, open(os.path.join(folder, 'add-routefloor165.json'), 'w', encoding='utf-8', newline='\n'), indent=1)
        rp = os.path.join(folder, 'true165-rules.json')
        raw = open(rp, 'rb').read()
        rules = json.loads(raw.decode('utf-8'))
        tag = 'True 1.65 horse routes follow the floor'
        rules['remove']['where'] = [w for w in rules['remove']['where'] if not w['reason'].startswith(tag)]
        ids = ','.join("'" + p.replace("'", "''") + "'" for p in sorted(changed))
        rules['remove']['where'].append({'table': 'PathPoints', 'key': 'PathPoints_ID',
            'where': f"PathID IN ({ids}) AND PathPoints_ID NOT LIKE 'p165floor-%'",
            'reason': tag + ': the original points of the re-pointed routes (add-routefloor165.json has them, with the floor added)'})
        nl = b'\r\n' if b'\r\n' in raw else b'\n'
        open(rp, 'wb').write(json.dumps(rules, indent=1, ensure_ascii=False).encode('utf-8').replace(b'\n', nl) + (nl if raw.endswith(nl) else b''))
    print('written', len(rows), 'rows')
else:
    for pid in list(changed)[:8]:
        print(pid, len(changed[pid]))
