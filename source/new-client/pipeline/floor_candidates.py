"""NPCs that may stand on a building floor rather than the ground - the ones whose drawn height depends on the floor
pick (GitHub issue #4: NPCs stored a little under their floor stood sunk until the pick started 64 units up, as the
1.64 client). From the world database: every Mob whose stored Z is more than --slack metres off the zone's terrain
under it (inside / on top of buildings, docks, platforms). Written per zone for tests/floor_scan_probe.gd.

    python floor_candidates.py [--slack 0.5] [--npcs]   -> pipeline/_work/floor_candidates.json {zone: [[name, x, y, z m], ...]}
"""
import json, sqlite3, sys
import numpy as np
import paths as _paths
from assets_pipeline import ROOT

ZONES = ROOT / 'godot/content/zones'
UNIT = 0.025


def main():
    slack = float(sys.argv[sys.argv.index('--slack') + 1]) if '--slack' in sys.argv else 0.5
    idx = json.loads((ZONES / 'zones_index.json').read_text(encoding='utf-8'))
    by_region = {}
    for zid, z in idx.items():
        if z.get('built'):
            by_region.setdefault(int(z['region']), []).append((int(zid), z))
    db = sqlite3.connect('file:%s?mode=ro' % _paths.need(_paths.WORLD_DB, 'WORLD_DB'), uri=True)
    realm_only = '--npcs' in sys.argv                       # NPCs only (a realm: guards, merchants, trainers), no monsters
    rows = db.execute('select Name, X, Y, Z, Region from Mob' + (' where Realm in (1, 2, 3)' if realm_only else '')).fetchall()
    heights, out, total, on_ground, no_zone = {}, {}, 0, 0, 0
    for name, x, y, z, region in rows:
        total += 1
        hit = None
        for zid, zi in by_region.get(int(region or 0), []):
            if zi['x'] * 8192 <= x < (zi['x'] + zi['w']) * 8192 and zi['y'] * 8192 <= y < (zi['y'] + zi['h']) * 8192:
                hit = (zid, zi)
                break
        if hit is None:
            no_zone += 1
            continue
        zid, zi = hit
        lx, ly, lz = (x - zi['x'] * 8192) * UNIT, (y - zi['y'] * 8192) * UNIT, z * UNIT
        if zid not in heights:
            zj = json.loads((ZONES / f'zone{zid:03d}/zone.json').read_text(encoding='utf-8'))
            hf = ZONES / f'zone{zid:03d}/heights.f32'
            if zj.get('city') or not hf.exists():
                heights[zid] = (None, zj)                    # a city / dungeon: no terrain - every NPC is a candidate
            else:
                n = int(zj['heights_size'])
                heights[zid] = (np.fromfile(hf, dtype=np.float32).reshape(n, n), zj)
        grid, zj = heights[zid]
        if grid is not None:
            cell = float(zj['cell_m'])
            gi = min(int(round(ly / cell)), grid.shape[0] - 1)
            gj = min(int(round(lx / cell)), grid.shape[1] - 1)
            ground = float(grid[gi, gj])
            if abs(lz - ground) <= slack:
                on_ground += 1
                continue
        out.setdefault(str(zid), []).append([name, round(lx, 2), round(lz, 2), round(ly, 2)])
    (ROOT / 'pipeline/_work').mkdir(exist_ok=True)
    (ROOT / 'pipeline/_work/floor_candidates.json').write_text(json.dumps(out), encoding='utf-8')
    print(f'{total} NPCs: {on_ground} on the ground, {no_zone} in no built zone, '
          f'{sum(len(v) for v in out.values())} candidates in {len(out)} zones')
    for zid, v in sorted(out.items(), key=lambda kv: -len(kv[1]))[:15]:
        print(f'  zone {zid} {idx[zid]["name"]}: {len(v)}')


if __name__ == '__main__':
    main()
