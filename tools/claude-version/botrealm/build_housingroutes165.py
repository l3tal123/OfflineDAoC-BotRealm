"""Stable routes inside the BotRealm housing zones (owner 2026-11-02: "make sure the stable routes inside the housing
markets work too"; chose the partial network).

The world DB has no housing horse routes at all: the 22 stable masters in the 12 period housing zones sell nothing. The
period route network (darkageofcamelot.com zone pages: Caerwent's four masters all linked, Caerwent's Alis to Dalton's
Alamdar and Rilan's Hwithloew, and pairs inside the other zones) also names ~28 masters the DB doesn't have, with no
source for where they stood: only routes between masters that exist are built.

Each route is planned on a 96-unit grid over the 1.64 client's own terrain (its zones' terrain.pcx/offset.pcx: the
server has no navmesh for the housing zones), keeping clear of the house lots (a lot is a house to be), avoiding
slopes, then straightened and given a point wherever the ground leaves the straight line by more than EPS. Output, both
copies (tools and runtime\\botrealm):
  add-housingtickets165.json  ItemTemplate: one ticket per route (Item_Type 40, the realm's horse)
  add-housingpaths165.json    Path
  add-housingroutes165.json   PathPoints
  add-housingstables165.json  MerchantItem: each master's ticket list
  true165-rules.json          "updates": each master's ItemsListTemplateID
Usage: python -I build_housingroutes165.py <world db> [--write]"""
import heapq, json, math, os, re, sqlite3, struct, sys, zipfile, zlib

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import groundz165 as g          # noqa: E402
from regionaudit import load_zones   # noqa: E402

CLIENT_ZIP = os.path.normpath(os.path.join(HERE, '..', '..', '..', '..', '1.64 real client', 'DAoC 1.64.zip'))
CELL = 96            # planning grid
LOT_BLOCK = 520      # no route within this of a lot's centre (a house stands there once bought)
LOT_NEAR = 800       # ... and it prefers to stay this far away
STEEP = 80           # a grid step climbing more than this is not taken
EPS = 24             # a point wherever the ground leaves the straight line by more than this
LIFT = 8             # route points sit this far over the ground
TAG = 'True 1.65 housing stable routes:'
REALM_HORSE = {2: 311, 102: 312, 202: 313}
PAIRS = [  # (from, to): both directions are built
    ('Alis', 'Gyrom'), ('Alis', 'Cora'), ('Alis', 'Itis'), ('Gyrom', 'Cora'), ('Gyrom', 'Itis'), ('Cora', 'Itis'),   # Caerwent
    ('Alis', 'Alamdar'), ('Alis', 'Hwithloew'),          # Caerwent -> Dalton, Rilan
    ('Tomaz', 'Maela'), ('Corliss', 'Hwithloew'),         # Old Sarum, Rilan
    ('Buri', 'Haile'), ('Hallbera', 'Erling'),            # Arothi, Carlingford
    ('Feldun', 'Maka'), ('Makenna', 'Braddan'),           # Dunshire, Torrylin
]

db = sqlite3.connect(f'file:{sys.argv[1]}?mode=ro', uri=True)
write = '--write' in sys.argv
zones = load_zones(g.ZONES_DAT)


# ---- the 1.64 client's terrain (zones/zoneNNN/datNNN.mpk: sector.dat, terrain.pcx, offset.pcx)
def _mpk(raw):
    header = bytes(b ^ i for i, b in enumerate(raw[5:21]))
    _crc, dsz, nsz, cnt = struct.unpack('<4I', header)
    cur = 21 + nsz
    d = zlib.decompress(raw[cur:cur + dsz])
    ds, out = cur + dsz, {}
    for i in range(cnt):
        rec = d[i * 0x11C:(i + 1) * 0x11C]
        n = rec[:256].split(b'\0', 1)[0].decode('latin1').lower()
        f = struct.unpack('<7I', rec[256:284])
        s = ds + f[4]
        out[n] = zlib.decompress(raw[s:s + f[5]])
    return out


def _pcx(b):
    w = int.from_bytes(b[8:10], 'little') - int.from_bytes(b[4:6], 'little') + 1
    h = int.from_bytes(b[10:12], 'little') - int.from_bytes(b[6:8], 'little') + 1
    bpl, out, i = int.from_bytes(b[66:68], 'little'), bytearray(), 128
    while len(out) < bpl * h and i < len(b):
        c = b[i]; i += 1
        if c >= 0xC0:
            out += bytes([b[i]]) * (c & 0x3F); i += 1
        else:
            out.append(c)
    return w, h, bpl, out


_zip = zipfile.ZipFile(CLIENT_ZIP)
_names = {n.lower(): n for n in _zip.namelist()}
_terrain = {}


def terrain(zid):
    if zid not in _terrain:
        cand = [n for n in _names if re.search(r'zones/zone%03d/dat%03d\.mpk$' % (zid, zid), n)]
        fs = _mpk(_zip.read(_names[cand[0]]))
        m = re.search(r'\[terrain\][^\[]*scalefactor=(\d+)[^\[]*offsetfactor=(\d+)', fs['sector.dat'].decode('latin1'))
        _terrain[zid] = (int(m.group(1)), int(m.group(2)), _pcx(fs['terrain.pcx']), _pcx(fs['offset.pcx']))
    return _terrain[zid]


def zone_at(region, x, y):
    for zid, z in zones.items():
        if z['region'] == region and z['x'] <= x < z['x'] + z['w'] and z['y'] <= y < z['y'] + z['h']:
            return zid
    return None


def height(region, x, y):
    zid = zone_at(region, x, y)
    if zid is None:
        return None
    sf, of, (w, h, bpl, td), (_, _, obpl, od) = terrain(zid)
    lx, ly = x - zones[zid]['x'], y - zones[zid]['y']
    px, py = min(w - 1, int(lx * w / 65536)), min(h - 1, int(ly * h / 65536))
    return td[py * bpl + px] * sf + od[py * obpl + px] * of


# ---- planning
def plan(region, a, b, lots):
    zs = {zone_at(region, *a[:2]), zone_at(region, *b[:2])}
    x0 = min(zones[z]['x'] for z in zs); y0 = min(zones[z]['y'] for z in zs)
    x1 = max(zones[z]['x'] + zones[z]['w'] for z in zs); y1 = max(zones[z]['y'] + zones[z]['h'] for z in zs)
    nx, ny = (x1 - x0) // CELL, (y1 - y0) // CELL
    near = {}
    for lx, ly in lots:
        cx, cy = (lx - x0) // CELL, (ly - y0) // CELL
        r = LOT_NEAR // CELL + 1
        for i in range(cx - r, cx + r + 1):
            for j in range(cy - r, cy + r + 1):
                d = math.hypot(x0 + i * CELL + CELL / 2 - lx, y0 + j * CELL + CELL / 2 - ly)
                if d < LOT_NEAR:
                    near[(i, j)] = min(near.get((i, j), 1e9), d)
    hcache = {}

    def hz(i, j):
        if (i, j) not in hcache:
            hcache[(i, j)] = height(region, x0 + i * CELL + CELL // 2, y0 + j * CELL + CELL // 2)
        return hcache[(i, j)]

    def cell(p):
        return (int((p[0] - x0) // CELL), int((p[1] - y0) // CELL))

    s, t = cell(a), cell(b)
    free = lambda c: c in (s, t) or near.get(c, 1e9) >= LOT_BLOCK
    openq, best, came = [(0, s)], {s: 0}, {}
    while openq:
        _, c = heapq.heappop(openq)
        if c == t:
            break
        for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
            n = (c[0] + di, c[1] + dj)
            if not (0 <= n[0] < nx and 0 <= n[1] < ny) or not free(n):
                continue
            h0, h1 = hz(*c), hz(*n)
            if h0 is None or h1 is None or abs(h1 - h0) > STEEP * math.hypot(di, dj):
                continue
            cost = best[c] + CELL * math.hypot(di, dj) + abs(h1 - h0) * 2 + \
                (LOT_NEAR - near[n]) * 0.5 if n in near else best[c] + CELL * math.hypot(di, dj) + abs(h1 - h0) * 2
            if cost < best.get(n, 1e18):
                best[n], came[n] = cost, c
                heapq.heappush(openq, (cost + math.hypot(t[0] - n[0], t[1] - n[1]) * CELL, n))
    if t not in came:
        return None
    path, c = [t], t
    while c != s:
        c = came[c]
        path.append(c)
    path.reverse()
    pts = [(x0 + i * CELL + CELL // 2, y0 + j * CELL + CELL // 2) for i, j in path]
    pts[0], pts[-1] = a[:2], b[:2]

    # straighten: the farthest point still reachable in a line over free, not-steep cells
    def clear(p, q):
        n = max(1, int(math.hypot(q[0] - p[0], q[1] - p[1]) // (CELL / 2)))
        prev = height(region, *p)
        for k in range(1, n + 1):
            x, y = p[0] + (q[0] - p[0]) * k / n, p[1] + (q[1] - p[1]) * k / n
            c = cell((x, y))
            if not free(c):
                return False
            hh = height(region, int(x), int(y))
            if hh is None or prev is None or abs(hh - prev) > STEEP:
                return False
            prev = hh
        return True
    keep, i = [pts[0]], 0
    while i < len(pts) - 1:
        j = len(pts) - 1
        while j > i + 1 and not clear(pts[i], pts[j]):
            j -= 1
        keep.append(pts[j])
        i = j
    # the ground along each straight leg: a point wherever it leaves the line by more than EPS
    out = []
    for p, q in zip(keep, keep[1:]):
        L = math.hypot(q[0] - p[0], q[1] - p[1])
        n = max(1, int(L // 32))
        prof = [(L * k / n, int(p[0] + (q[0] - p[0]) * k / n), int(p[1] + (q[1] - p[1]) * k / n)) for k in range(n + 1)]
        prof = [(s_, x, y, (height(region, x, y) or 0) + LIFT) for s_, x, y in prof]

        def dp(seg):
            (s0, _, _, z0), (s1, _, _, z1) = seg[0], seg[-1]
            worst, wi = 0, 0
            for k in range(1, len(seg) - 1):
                line = z0 + (z1 - z0) * (seg[k][0] - s0) / (s1 - s0)
                if abs(seg[k][3] - line) > worst:
                    worst, wi = abs(seg[k][3] - line), k
            return [seg[0], seg[-1]] if worst <= EPS else dp(seg[:wi + 1])[:-1] + dp(seg[wi:])
        out += [(x, y, z) for _, x, y, z in dp(prof)][:-1] if out or True else []
    last = keep[-1]
    out.append((last[0], last[1], (height(region, *last) or b[2]) + LIFT))
    out[0] = (a[0], a[1], a[2])
    return out


masters = {}
for name, reg, x, y, z, mid in db.execute("SELECT Name, Region, X, Y, Z, Mob_ID FROM Mob WHERE Region IN (2,102,202) "
                                          "AND ClassType LIKE '%StableMaster%'"):
    if zone_at(reg, x, y) is not None:
        masters[name] = (reg, x, y, z, mid)
tickets, paths, points, lists, updates = [], [], [], {}, []
now = '2026-11-02 12:00:00'
for a_name, b_name in PAIRS:
    for src, dst in ((a_name, b_name), (b_name, a_name)):
        if src not in masters or dst not in masters:
            print('missing master', src, dst)
            continue
        reg = masters[src][0]
        a, b = masters[src][1:4], masters[dst][1:4]
        # the horse starts beside its stable master and stops short of the other one
        d = math.hypot(b[0] - a[0], b[1] - a[1])
        ax, ay = a[0] + (b[0] - a[0]) * 120 / d, a[1] + (b[1] - a[1]) * 120 / d
        bx, by = b[0] - (b[0] - a[0]) * 200 / d, b[1] - (b[1] - a[1]) * 200 / d
        lots = [(lx, ly) for lx, ly in db.execute("SELECT X, Y FROM DBHouse WHERE RegionID=?", (reg,))
                if zone_at(reg, lx, ly) is not None]
        route = plan(reg, (int(ax), int(ay), a[2]), (int(bx), int(by), b[2]), lots)
        if not route:
            print('NO ROUTE', src, dst)
            continue
        pid = f'HH_{src}_{dst}'
        zname = zones[zone_at(reg, b[0], b[1])]['name']
        length = sum(math.hypot(q[0] - p[0], q[1] - p[1]) for p, q in zip(route, route[1:]))
        print(f'{pid}: {len(route)} points, {length:.0f} units ({zname})')
        tickets.append({'ItemTemplate_ID': f'p165-housing-ticket-{pid}', 'Id_nb': pid, 'Name': f'Ticket to {zname} ({dst})',
                        'Level': 0, 'Durability': 1, 'MaxDurability': 1, 'Condition': 1, 'MaxCondition': 1, 'Quality': 1,
                        'Item_Type': 40, 'Object_Type': 0, 'Color': REALM_HORSE[reg], 'Model': 499, 'Price': 0,
                        'IsPickable': 1, 'IsDropable': 1, 'CanDropAsLoot': 1, 'IsTradable': 1, 'MaxCount': 1, 'PackSize': 1,
                        'AllowedClasses': '0', 'Realm': 0, 'LastTimeRowUpdated': now})
        paths.append({'Path_ID': f'p165-housing-path-{pid}', 'PathID': pid, 'PathType': 1, 'RegionID': reg, 'LastTimeRowUpdated': now})
        for i, (x, y, z) in enumerate(route, 1):
            points.append({'PathPoints_ID': f'p165housing-{pid}-{i}', 'PathID': pid, 'Step': i, 'X': int(x), 'Y': int(y),
                           'Z': int(z), 'MaxSpeed': 1000, 'WaitTime': 0, 'LastTimeRowUpdated': now})   # speed as the realm horse routes
        lists.setdefault(src, []).append(pid)
rows_mi = []
for src, pids in lists.items():
    lid = f'p165_housing_stable_{src}'
    for slot, pid in enumerate(pids):
        rows_mi.append({'MerchantItem_ID': f'p165-housing-mi-{pid}', 'ItemListID': lid, 'ItemTemplateID': pid,
                        'PageNumber': 0, 'SlotPosition': slot, 'LastTimeRowUpdated': now})
    updates.append({'sql': f"UPDATE Mob SET ItemsListTemplateID = '{lid}' WHERE Mob_ID = '{masters[src][4]}'",
                    'reason': f'{TAG} {src} sells the tickets of its routes (build_housingroutes165.py)'})
print(f'routes {len(paths)}, points {len(points)}, masters with tickets {len(lists)}')
if write:
    out = {'add-housingtickets165.json': ('ItemTemplate', 'Id_nb', tickets),
           'add-housingpaths165.json': ('Path', 'PathID', paths),
           'add-housingroutes165.json': ('PathPoints', 'PathPoints_ID', points),
           'add-housingstables165.json': ('MerchantItem', 'MerchantItem_ID', rows_mi)}
    for folder in (HERE, os.path.join(HERE, '..', '..', '..', 'runtime', 'botrealm')):
        for fn, (table, key, rows) in out.items():
            json.dump({'table': table, 'key': key, 'reason': f'{TAG} the stable routes between the period housing zones\' '
                       'stable masters (build_housingroutes165.py)', 'rows': rows},
                      open(os.path.join(folder, fn), 'w', encoding='utf-8', newline='\n'), indent=1)
        rp = os.path.join(folder, 'true165-rules.json')
        raw = open(rp, 'rb').read()
        rules = json.loads(raw.decode('utf-8'))
        rules['updates'] = [u for u in rules['updates'] if not u.get('reason', '').startswith(TAG)] + updates
        nl = b'\r\n' if b'\r\n' in raw else b'\n'
        open(rp, 'wb').write(json.dumps(rules, indent=1, ensure_ascii=False).encode('utf-8').replace(b'\n', nl) + (nl if raw.endswith(nl) else b''))
    print('written')
