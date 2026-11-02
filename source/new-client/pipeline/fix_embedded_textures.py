"""Zone and dungeon models whose NIF keeps its textures inside itself (NiSourceTexture use_external 0 -> NiPixelData),
converted before nif_static could read those (2026-10-14, the paletted fix for Tir na Nog's tir24): their surfaces drew
white (GitHub issue #2: Camelot's round table area - cam36; Darkness Falls' demontunnel2 the same). Each such model
is converted again with today's converter and replaced only when that leaves fewer untextured triangles; its own
textures are saved as the zone build saves them, and the zone's x4plus set gets the missing ones (upscale_dir only
makes what is missing - also the new 1.65 inns' textures that had no x4plus copy). Nothing else is rewritten, so a
patch carries only what really changed.

    python fix_embedded_textures.py [--dry-run] [zone ids ...]
Afterwards repack the replaced models' scenes:
    Godot --headless --path godot -s res://tools/pack_scenes.gd -- --force res://content/zones/zoneNNN/models/<name>.gltf
"""
import json, shutil, sys, tempfile
from pathlib import Path
from assets_pipeline import CLIENT, ROOT
from daoc_mpk import read_mpk
from netimmerse import read_nif
import nif_static
from build_zone import zone_local_npks, upscale_dir, save_if_changed

ZONES = ROOT / 'godot/content/zones'


def untextured_triangles(gltf: Path) -> int:
    d = json.loads(gltf.read_text(encoding='utf-8'))
    acc = d.get('accessors', [])
    nm = (d.get('extras') or {}).get('nif_materials') or []
    bad = {i for i in range(len(d.get('materials', []))) if i < len(nm) and not nm[i].get('slots')}
    return sum(acc[p['indices']]['count'] // 3 for m in d.get('meshes', []) for p in m['primitives']
               if p.get('material') in bad and 'indices' in p)


def sources(zone_id):
    """{model name: [npk paths]} where the zone's models can come from, in the order tried: its own nifs, the dungeon
    pieces (Dnifs), the shared Nifs (a shared name can be an older outdoor model - Darkness Falls' demontunnel2)."""
    out = {}
    for n, p in zone_local_npks(zone_id).items():
        out.setdefault(Path(n).stem, []).append(p)
    for sub in ('Dnifs', 'Nifs', 'nifs'):
        d = CLIENT / 'zones' / sub
        if d.is_dir():
            for p in d.iterdir():
                if p.suffix.lower() == '.npk' and p not in out.get(p.stem.lower(), []):
                    out.setdefault(p.stem.lower(), []).append(p)
    return out


def has_embedded(raw) -> bool:
    try:
        nif = read_nif(raw)
    except Exception:                                      # (a NIF the reader can't open was never converted either)
        return False
    return any(b.type == 'NiSourceTexture' and not b.get('use_external', 1) for b in nif.blocks)


def main():
    dry = '--dry-run' in sys.argv
    want = {int(a) for a in sys.argv[1:] if a.isdigit()}
    replaced = []
    for zd in sorted(ZONES.glob('zone[0-9][0-9][0-9]')):
        zid = int(zd.name[4:])
        if want and zid not in want:
            continue
        mdir = zd / 'models'
        if not mdir.is_dir():
            continue
        src = None
        new_tex = set()
        for g in sorted(mdir.glob('*.gltf')):
            before = untextured_triangles(g)
            if before == 0:
                continue
            if src is None:
                src = sources(zid)
            raw = None
            for npk in src.get(g.stem.lower(), []):
                r = next((v for k, v in read_mpk(str(npk)).items() if k.lower().endswith('.nif')), None)
                if r is not None and has_embedded(r):
                    raw = r
                    break
            if raw is None:
                continue
            with tempfile.TemporaryDirectory() as tmp:
                t = Path(tmp) / 'models' / g.name
                t.parent.mkdir(parents=True)
                info = nif_static.convert(raw, str(t), '../textures/original')
                after = untextured_triangles(t)
                print(f'zone {zid} {g.stem}: untextured triangles {before} -> {after}')
                if after >= before or dry:
                    continue
                for f in t.parent.iterdir():                  # the gltf and its buffers
                    shutil.copy2(f, mdir / f.name)
            odir = zd / 'textures/original'
            for n in info['textures']:
                if n in nif_static.EMBEDDED and not (odir / f'{n}.png').exists():
                    save_if_changed(nif_static.EMBEDDED[n], odir / f'{n}.png')
                    new_tex.add(n)
            replaced.append(f'res://content/zones/{zd.name}/models/{g.name}')
        x4 = zd / 'textures/x4plus'
        odir = zd / 'textures/original'
        if x4.is_dir() and odir.is_dir() and not dry:
            missing = [p.name for p in odir.glob('*.png') if not (x4 / p.name).exists()]
            if missing:
                print(f'zone {zid}: x4plus for {len(missing)} textures: {missing[:8]}')
                upscale_dir(odir, x4)
    print('replaced models:', replaced)
    (ROOT / 'pipeline/_work').mkdir(exist_ok=True)
    (ROOT / 'pipeline/_work/fix_embedded_replaced.txt').write_text('\n'.join(replaced), encoding='utf-8')


if __name__ == '__main__':
    main()
