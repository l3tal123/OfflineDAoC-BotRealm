"""Moves True 1.65 spawns that stand off the walkable navmesh, or on a small walkable island cut off from the main
ground, to the nearest spot on the main walkable area (owner 2026-10-31, from the camp audit: 94 of 15,603 restored and
period spawns; bots picked them as camps and could never reach them).

Reads the spawn rows from add-restoredspawns165.json and add-periodspawns165.json and the navmeshes (navmesh-165 first,
then navmesh); zones from the world DB's Zones table. A spot counts as main ground when its navmesh area is at least
2,000,000 square units (the same test the audit used). Searches outward to 1,500 units; a spawn with no main ground
that close is left alone and listed.
Usage: python -I fix_offmesh_spawns165.py <world db> [--write]   (--write updates the json in runtime\\true165 and here)
"""
import json, math, os, sqlite3, sys

HERE = os.path.dirname(os.path.abspath(__file__))
TRUE165 = os.path.normpath(os.path.join(HERE, '..'))
ROOT = os.path.normpath(os.path.join(TRUE165, '..', '..', '..'))
sys.path.insert(0, os.path.join(ROOT, 'development-source', 'server', 'tools', 'navmesh_splice'))
from navcomp import NavAreas   # noqa: E402

MAIN_AREA = 2e6
FILES = ['add-restoredspawns165.json', 'add-periodspawns165.json']
NAV = [os.path.join(ROOT, 'runtime', 'server', 'navmesh-165'), os.path.join(ROOT, 'runtime', 'server', 'navmesh')]

db = sqlite3.connect(f'file:{sys.argv[1]}?mode=ro', uri=True)
write = '--write' in sys.argv
zones = [dict(id=r[0], reg=r[1], x=r[2] * 8192, y=r[3] * 8192, w=r[4] * 8192, h=r[5] * 8192)
         for r in db.execute('select ZoneID,RegionID,OffsetX,OffsetY,Width,Height from Zones')]
meshes = {}


def mesh(zone_id):
    if zone_id not in meshes:
        meshes[zone_id] = None
        for d in NAV:
            p = os.path.join(d, f'zone{zone_id:03d}.nav')
            if os.path.exists(p):
                meshes[zone_id] = NavAreas(p)
                break
    return meshes[zone_id]


def zone_of(reg, x, y):
    for z in zones:
        if z['reg'] == reg and z['x'] <= x < z['x'] + z['w'] and z['y'] <= y < z['y'] + z['h']:
            return z
    return None


def on_main(reg, x, y, near_z, tol=256):
    """(poly z) when (x, y) near near_z is on the main walkable area, else None."""
    z = zone_of(reg, x, y)
    m = mesh(z['id']) if z else None
    if m is None:
        return None
    i = m.locate(x, y, near_z, tolerance=tol)
    if i is None or m.area[m.root[i]] < MAIN_AREA:
        return None
    return m.height(i, x, y) if hasattr(m, 'height') else near_z


def find_spot(reg, x, y, z):
    if on_main(reg, x, y, z) is not None:
        return None   # already fine
    for r in range(25, 1501, 25):
        n = max(16, int(2 * math.pi * r / 50))
        for k in range(n):
            a = 2 * math.pi * k / n
            px, py = int(x + math.cos(a) * r), int(y + math.sin(a) * r)
            for dz in (0, -200, 200, -400, 400):
                hz = on_main(reg, px, py, z + dz)
                if hz is not None:
                    return px, py, int(round(hz))
    return False


changed, left = {}, []
for name in FILES:
    data = json.load(open(os.path.join(TRUE165, name), encoding='utf-8'))
    for row in data['rows']:
        if row.get('Region') not in (1, 100, 200):
            continue
        spot = find_spot(row['Region'], row['X'], row['Y'], row['Z'])
        if spot is None:
            continue
        if spot is False:
            left.append((row.get('Name'), row['Region'], row['X'], row['Y'], row['Z']))
            continue
        changed[row['Mob_ID']] = (row.get('Name'), (row['X'], row['Y'], row['Z']), spot)
        row['X'], row['Y'], row['Z'] = spot
    if write:
        for folder in (TRUE165, os.path.join(ROOT, 'runtime', 'true165')):
            path = os.path.join(folder, name)
            raw = open(path, 'rb').read()
            nl = b'\r\n' if b'\r\n' in raw else b'\n'
            open(path, 'wb').write(json.dumps(data, indent=1, ensure_ascii=False).encode('utf-8').replace(b'\n', nl) +
                                   (nl if raw.endswith(nl) else b''))

for mob, (n, a, b) in changed.items():
    print(f'moved {n} {mob}: {a} -> {b} ({int(math.dist(a[:2], b[:2]))} units)')
print('moved', len(changed), 'left (no main ground within 1500)', len(left))
for l in left:
    print('  left', l)
print('written' if write else '(dry run)')
