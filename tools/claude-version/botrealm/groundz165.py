"""Ground height at a world spot from the server's navmeshes (the 1.65 overrides in runtime\\server\\navmesh-165 first).
Used by the builders that move spawns sideways (build_innclear165.py): a monster moved to a new X/Y keeps its old Z
otherwise and floats over a dip or sinks into a rise (owner 2026-10-30: a blodfelag oathbreaker flying near
Huginfell)."""
import math, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
sys.path.insert(0, os.path.join(ROOT, 'development-source', 'server', 'tools', 'navmesh_splice'))
from navcomp import NavAreas, _inside   # noqa: E402
from regionaudit import load_zones      # noqa: E402

ZONES_DAT = os.path.join(ROOT, 'development-source', 'research', 'period-data', 'client-1.64', 'zones', 'zones.dat')
NAV_DIRS = [os.path.join(ROOT, 'runtime', 'server', 'navmesh-165'), os.path.join(ROOT, 'runtime', 'server', 'navmesh')]
_zones = None
_meshes = {}


def _mesh(zone):
    if zone not in _meshes:
        _meshes[zone] = None
        for d in NAV_DIRS:
            p = os.path.join(d, f'zone{zone:03d}.nav')
            if os.path.exists(p):
                _meshes[zone] = NavAreas(p)
                break
    return _meshes[zone]


import functools


@functools.lru_cache(maxsize=4_000_000)
def floors(region, x, y):
    """Every walkable floor height at (x, y) (a bridge deck and the river bed under it are two), sorted; cached, as the
    route builders ask about the same spots many times (2026-11-01: the horse-route deck pass ran for hours)."""
    global _zones
    if _zones is None:
        _zones = load_zones(ZONES_DAT)
    hs = set()
    for zid, z in _zones.items():
        if z['region'] != region or not (z['x'] <= x < z['x'] + z['w'] and z['y'] <= y < z['y'] + z['h']):
            continue
        m = _mesh(zid)
        if m is None:
            continue
        for i in m._grid().get((int(x // m.CELL), int(y // m.CELL)), ()):
            _, pts, bb, _, om = m.polys[i]
            if om or not (bb[0] <= x <= bb[2] and bb[1] <= y <= bb[3]) or not _inside(pts, x, y):
                continue
            hs.add(int(round(m.height(i, x, y))))
    return tuple(sorted(hs))


def ground_z(region, x, y, near_z):
    """The walkable floor at (x, y) closest to near_z (outdoors that is the ground, not a roof or bridge above it);
    None when no navmesh covers the spot."""
    hs = floors(region, int(round(x)), int(round(y)))
    return min(hs, key=lambda h: abs(h - near_z)) if hs else None


def nearest_ground(region, x, y, near_z, search=400, step=50):
    """(x, y, z) on the navmesh at or nearest (x, y) within search: a moved spawn landing in a hole in the mesh
    (a rock, a tree trunk, water) is nudged to the closest walkable spot."""
    z = ground_z(region, x, y, near_z)
    if z is not None:
        return x, y, z
    for r in range(step, search + 1, step):
        for k in range(16):
            a = k * math.pi / 8
            px, py = int(x + math.cos(a) * r), int(y + math.sin(a) * r)
            z = ground_z(region, px, py, near_z)
            if z is not None:
                return px, py, z
    return None
