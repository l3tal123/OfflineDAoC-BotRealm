"""Converted models whose NIF holds junk texture coordinates (owner 2026-11-02: the room above Camelot's necromancer
and reaver trainers drew its floor, beams, pillars and furniture in one flat purple). A few vertices in some original
NIFs carry +-1.7e38 or NaN as a UV. Godot stores a mesh's UVs packed into that mesh's own UV range, so one such value
flattens every triangle of the surface to a single texel - not only the few bad ones. The converter now cleans them
(nif_static.clean_uvs); this repairs the models converted before that, in place, with the same rule.

    python fix_junk_uvs.py [--dry-run] [content dir]
Afterwards repack the changed models' scenes (only the .scn files older than their glTF are rebuilt):
    Godot --headless --path godot -s res://tools/pack_scenes.gd -- res://content
"""
import base64, json, sys
from pathlib import Path
import numpy as np
from nif_static import clean_uvs

ROOT = Path(__file__).resolve().parents[1]


def fix(path: Path, dry: bool) -> int:
    g = json.loads(path.read_text(encoding='utf-8'))
    bufs = {}

    def buf(i):
        if i not in bufs:
            uri = g['buffers'][i]['uri']
            raw = base64.b64decode(uri.split(',', 1)[1]) if uri.startswith('data:') else (path.parent / uri).read_bytes()
            bufs[i] = bytearray(raw)
        return bufs[i]

    def view(ai, comps, dt):
        a = g['accessors'][ai]
        bv = g['bufferViews'][a['bufferView']]
        if bv.get('byteStride') not in (None, comps * np.dtype(dt).itemsize):
            return None, None
        off = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
        arr = np.frombuffer(buf(bv['buffer']), dt, count=a['count'] * comps, offset=off)
        return (arr.reshape(-1, comps) if comps > 1 else arr), bv['buffer']

    fixed, dirty, done = 0, set(), set()
    for m in g.get('meshes', []):
        for pr in m['primitives']:
            tri = None
            if 'indices' in pr:
                a = g['accessors'][pr['indices']]
                dt = {5125: np.uint32, 5123: np.uint16, 5121: np.uint8}[a['componentType']]
                idx, _ = view(pr['indices'], 1, dt)
                tri = idx.reshape(-1, 3).astype(np.int64) if idx is not None else None
            for k, ai in pr['attributes'].items():
                if not k.startswith('TEXCOORD') or ai in done or g['accessors'][ai].get('componentType') != 5126:
                    continue
                done.add(ai)
                uv, bi = view(ai, 2, np.float32)
                if uv is None:
                    continue
                good, n = clean_uvs(uv.astype(np.float64), tri)
                if n:
                    fixed += n
                    dirty.add(bi)
                    uv[:] = good.astype(np.float32)               # (a view into the bytearray: writes through)
                    a = g['accessors'][ai]
                    if 'min' in a or 'max' in a:
                        a['min'] = good.min(axis=0).tolist()
                        a['max'] = good.max(axis=0).tolist()
    if fixed and not dry:
        for bi in dirty:
            uri = g['buffers'][bi]['uri']
            if uri.startswith('data:'):
                g['buffers'][bi]['uri'] = 'data:application/octet-stream;base64,' + base64.b64encode(bytes(bufs[bi])).decode()
            else:
                (path.parent / uri).write_bytes(bytes(bufs[bi]))
        path.write_text(json.dumps(g))
    return fixed


def main():
    dry = '--dry-run' in sys.argv
    dirs = [Path(a) for a in sys.argv[1:] if not a.startswith('--')] or [ROOT / 'godot/content']
    total = []
    for d in dirs:
        for p in sorted(d.rglob('*.gltf')):
            n = fix(p, dry)
            if n:
                total.append(p)
                print('%6d junk UVs  %s' % (n, p.relative_to(ROOT).as_posix()))
    print(('would fix' if dry else 'fixed'), len(total), 'models')


if __name__ == '__main__':
    main()
