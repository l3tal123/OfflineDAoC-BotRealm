"""True 1.65 Mode: the PERIOD player-housing model library, from the real 1.64 client only (owner 2026-11-02: "i only
want the real 1.65 housing areas and models" - the 1.64 client's phousing data: its non-HD houses, its ph_houses.csv /
ph_textures.csv and its 271 house items; no later-client HD / Tier 5 / Yule models, no post-1.64 furniture).

Sources, all read from DAoC 1.64.zip (DAOC_164_ZIP, pipeline/paths.py; never modified):
  phousing/phousing.mpk     ph_houses.csv (house model 1-12 -> exterior / interior NIF, Roof / Wall / Door / Wood Start,
                            Smoke Nif = spnifs.csv row, Banner / Shield Detail) and ph_textures.csv (material id -> texture)
  phousing/nifs/            the 12 exteriors + 12 interiors, the house items, lot markers, hookpoint markers
  phousing/textures/        the 57 material-swap textures (PH_Door / Wood / <realm>Roof / <realm>Wall, tree_ocean_02)
  phousing/effects/         chimsmoke (spnifs.csv row 8, "Housing Only")
  gamedata.mpk              objects.csv: col 36 "HouseItem Nif" -> items.csv row, whose col 2 is the NIF name in
                            phousing/nifs (col 16 "Housing Only"); col 43 House Collide, 44 Garden House Height,
                            45 / 46 garden Length / Width (GPoint cells), 47 / 48 X / Y offset. Lot markers 1306-1308 use
                            col 2 (items.csv 566-568 MLot / Hlot / Alot).
Textures a NIF names come from the 1.64 zip's own folders (phousing/nifs first; PH_* from phousing/textures first,
the folder game.dll swaps materials from), then other 1.64 folders (zones/Nifs, zones/Dnifs, insignia ...).

Writes godot/content/housing/ (the client's HouseView reads it):
  houses/<nif lowercased>/model.gltf  the 24 house NIFs with their NODE TREE KEPT (nif_static keep_paths: NIF node
                            names lowercased, empty helper nodes - GPoint000..255, SmokePoint01, Mpoint*, EntryPoint,
                            Bpoint* / Vpoint* / Tpoint*, Light009 ... - kept at their transforms; collision as
                            'collision_<n>' child meshes under the collidee nodes; see nif_static.KeepPaths)
  items/<nif lowercased>/model.gltf   house items, lot markers, hookpoint markers, 4sale_sign, chimsmoke (flat, as the
                            zone fixtures; the four untextured hookpoint markers drawn in their material colours -
                            nif_static draw_untextured)
  textures/<name>.png       every texture those models use, the client's own pixels; textures/x4plus/<name>.png the
                            Real-ESRGAN copies (2x, as zone textures) of those >= 64 px on their short side, except
                            every PH_*Roof* (the 64 px Albion / Hibernia ones are tiny - kept original, and all roof
                            variants stay alike) and the lettered trade / for-sale sign textures (ESRGAN invents
                            letters: WORLD IMPORT LESSONS). NifMaterials falls back to the original for the rest.
  housing.json              {"houses": {model: {exterior, interior, roof_start, wall_start, door_start, wood_start, smoke,
                            banner_detail, shield_detail, name}}, "textures": {ph_textures id: texture name},
                            "items": {objects.csv id: {model, garden: [length, width, xoff, yoff], collide, height,
                            name, nif}}, "markers": {name: item folder}, "effects": {name: item folder}}
Coordinates as every converted model: metres (0.025 per DAoC unit), x east, y up, z = DAoC +Y (south), model Y
flipped on the way in (nif_static DAOC_TO_GODOT); a house is placed like a fixture (yaw = -Heading degrees).
Then pack the .scn copies:  Godot --headless --path godot -s res://tools/pack_scenes.gd -- res://content/housing

    python build_housing.py [--no-upscale] [--upscale-only]
"""
import csv, io, json, os, shutil, sys, tempfile, zipfile
from pathlib import Path
import paths as _paths
from assets_pipeline import ROOT
from build_zone import upscale_dir, save_if_changed
from daoc_mpk import read_mpk
import nif_static
import numpy as np
from PIL import Image

OUT = ROOT / 'godot/content/housing'
TEX = OUT / 'textures'
X4 = TEX / 'x4plus'
TEX_URI = '../../textures'                                   # from houses/<name>/ and items/<name>/
# where a NIF's texture is looked for in the 1.64 zip, by folder (lowercase prefixes), first match wins
TEX_FOLDERS = ['phousing/nifs/', 'phousing/textures/', 'phousing/effects/', 'phousing/', 'zones/nifs/', 'zones/dnifs/',
               'items/', 'effects/', 'insignia/', 'figures/', 'nifs/', 'zones/']
EXTRA_MARKERS = {'bindstone': 'BindstoneMarker', 'vault': 'VaultMarker', 'merchant': 'MerchantMarker',
                 'trade': 'TradeMarker', 'for_sale': '4sale_sign'}       # the /housepoints markers, the lot sale sign


def rows(raw):
    return [r for r in csv.reader(io.StringIO(raw.decode('latin1')))]


def col(r, i):
    return r[i].strip() if r is not None and len(r) > i else ''


def num(r, i):
    try:
        return int(float(col(r, i))) if col(r, i) else 0
    except ValueError:
        return 0


class Zip164:
    def __init__(self):
        self.z = zipfile.ZipFile(_paths.need(_paths.ZIP_164, 'ZIP_164'))
        self.names = {n.lower(): n for n in self.z.namelist() if not n.endswith('/')}
        self.by_base = {}                                      # texture basename -> [members], folder order
        for low, n in self.names.items():
            if low.rsplit('.', 1)[-1] in ('dds', 'tga', 'bmp'):
                self.by_base.setdefault(low.split('/')[-1].rsplit('.', 1)[0], []).append(low)

    def read(self, low):
        return self.z.read(self.names[low.lower()])

    def mpk(self, low):
        with tempfile.TemporaryDirectory() as t:              # (daoc_mpk reads files)
            p = Path(t) / 'a.mpk'
            p.write_bytes(self.read(low))
            return read_mpk(str(p))

    def nif(self, folder, name):
        """A NIF's bytes from <folder>/<name>.nif or .npk (any case), or None."""
        for ext in ('.nif', '.npk'):
            low = f'{folder}/{name}{ext}'.lower()
            if low in self.names:
                if ext == '.npk':
                    return next(v for k, v in self.mpk(low).items() if k.endswith('.nif'))
                return self.read(low)
        return None

    def texture(self, base):
        """(PIL RGBA image, zip member) of a texture basename, or (None, None). PH_* from phousing/textures first."""
        cands = self.by_base.get(base.lower(), [])
        order = (['phousing/textures/'] if base.lower().startswith('ph_') else []) + TEX_FOLDERS
        cands = sorted(cands, key=lambda c: next((i for i, f in enumerate(order) if c.startswith(f)), len(order)))
        for c in cands:
            try:
                img = Image.open(io.BytesIO(self.read(c)))
                img.load()
                return img.convert('RGBA'), c
            except Exception as e:                            # (a format PIL can't read: try the next copy)
                print('  unreadable', c, e)
        return None, None


def main():
    no_up = '--no-upscale' in sys.argv
    if '--upscale-only' in sys.argv:
        upscale(json.loads((OUT / 'build_report.json').read_text(encoding='utf-8'))['no_upscale'])
        return
    z = Zip164()
    g = z.mpk('gamedata.mpk')
    ph = z.mpk('phousing/phousing.mpk')
    spnifs = {col(r, 0): r for r in rows(g['spnifs.csv']) if col(r, 0).isdigit()}
    houses, need = {}, {}                                      # need: folder name -> (kind, zip folder, nif name)
    for r in rows(ph['ph_houses.csv']):
        if not col(r, 0).isdigit() or not col(r, 2):           # (the first header cell holds the row count, 12)
            continue
        ext, inn = col(r, 2), col(r, 3)
        smoke = col(spnifs.get(col(r, 8)), 2).lower()
        houses[col(r, 0)] = {'exterior': ext.lower(), 'interior': inn.lower(), 'roof_start': num(r, 4),
                             'wall_start': num(r, 5), 'door_start': num(r, 6), 'wood_start': num(r, 7),
                             'smoke': smoke, 'banner_detail': num(r, 9), 'shield_detail': num(r, 10), 'name': col(r, 1)}
        need[ext.lower()] = ('houses', 'phousing/nifs', ext)
        need[inn.lower()] = ('houses', 'phousing/nifs', inn)
        if smoke:
            need[smoke] = ('items', 'phousing/effects', col(spnifs.get(col(r, 8)), 2))
    textures = {col(r, 0): col(r, 2).lower() for r in rows(ph['ph_textures.csv']) if col(r, 0).isdigit() and col(r, 2)}
    items_csv = {col(r, 0): r for r in rows(g['items.csv']) if col(r, 0).isdigit()}
    items = {}
    for r in rows(g['objects.csv']):
        oid = col(r, 0)
        if not oid.isdigit():
            continue
        row = col(r, 36) if col(r, 36) not in ('', '0') else (col(r, 2) if oid in ('1306', '1307', '1308') else '')
        if not row:
            continue
        nif = col(items_csv.get(row), 2)
        if not nif:
            raise SystemExit(f'objects.csv {oid}: items.csv row {row} has no NIF')
        items[oid] = {'model': nif.lower(), 'garden': [num(r, 45), num(r, 46), num(r, 47), num(r, 48)],
                      'collide': num(r, 43), 'height': num(r, 44), 'name': col(r, 1), 'nif': nif}
        need[nif.lower()] = ('items', 'phousing/nifs', nif)
    markers = {k: v.lower() for k, v in EXTRA_MARKERS.items()}
    for v in EXTRA_MARKERS.values():
        need[v.lower()] = ('items', 'phousing/nifs', v)
    # convert
    tex_names, report, sign_tex = set(), {'models': {}}, set()
    for folder, (kind, zdir, nif) in sorted(need.items()):
        raw = z.nif(zdir, nif)
        if raw is None:
            raise SystemExit(f'{nif}: not in the 1.64 zip\'s {zdir}')
        d = OUT / kind / folder
        d.mkdir(parents=True, exist_ok=True)
        info = nif_static.convert(raw, str(d / 'model.gltf'), TEX_URI, keep_paths=kind == 'houses',
                                  draw_untextured=folder in markers.values() and folder.endswith('marker'))
        tex_names |= info['textures']
        sign_tex |= {t for t in info['textures'] if t.startswith(('tradesign', '4sale'))}   # the lettered ones
        report['models'][f'{kind}/{folder}'] = {'triangles': info['triangles'], 'collision': info['collision_triangles'],
                                                'particles': info['particles'], 'warnings': info['warnings']}
    for t in textures.values():                                # every swap texture, used by a default NIF or not
        tex_names.add(t)
    for low in z.names:                                        # and the whole phousing/textures folder (57 files)
        if low.startswith('phousing/textures/') and low.rsplit('.', 1)[-1] in ('dds', 'tga', 'bmp'):
            tex_names.add(low.split('/')[-1].rsplit('.', 1)[0])
    # textures
    TEX.mkdir(parents=True, exist_ok=True)
    missing, sources = [], {}
    for n in sorted(tex_names):
        doubled = n.endswith(('__d2', '__x2'))                 # __x2: the raw detail map (nif_static / detail_x2.py)
        base = n[:-4] if doubled else n
        img, src = z.texture(base)
        if img is None and base in nif_static.EMBEDDED:
            img, src = nif_static.EMBEDDED[base].convert('RGBA'), 'embedded in its NIF'
        if img is None:
            missing.append(n)
            continue
        if n.endswith('__d2'):
            rgb = np.asarray(img.convert('RGB'), dtype=np.float32) * 2.0
            img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8))
        save_if_changed(img, TEX / f'{n}.png')
        sources[n] = src
    no_up = sorted(n for n in sources if n.startswith('ph_') and 'roof' in n) + sorted(sign_tex)
    report.update(textures=len(sources), missing_textures=missing, texture_sources=sources, no_upscale=sorted(set(no_up)))
    data = {'source': 'DAoC 1.64.zip phousing/ + gamedata.mpk objects.csv / items.csv / spnifs.csv (build_housing.py)',
            'houses': houses, 'textures': textures, 'items': items, 'markers': markers,
            'effects': {h['smoke']: h['smoke'] for h in houses.values() if h['smoke']}}
    (OUT / 'housing.json').write_text(json.dumps(data, indent=1), encoding='utf-8')
    (OUT / 'build_report.json').write_text(json.dumps(report, indent=1), encoding='utf-8')
    print(f'houses {sum(1 for k in need.values() if k[0] == "houses")}, item models '
          f'{sum(1 for k in need.values() if k[0] == "items")}, objects.csv items {len(items)}, textures {len(sources)}, '
          f'missing {missing}')
    if not no_up:
        upscale(report['no_upscale'])


def upscale(skip):
    """Real-ESRGAN (2x, as the zones') for the textures >= 64 px on the short side, not the roofs / signs."""
    with tempfile.TemporaryDirectory() as t:
        stage = Path(t)
        for p in sorted(TEX.glob('*.png')):
            if p.stem in skip or min(Image.open(p).size) < 64:
                continue
            shutil.copy2(p, stage / p.name)                    # (copy2: upscale_dir compares modification times)
        upscale_dir(stage, X4)
    print('x4plus:', len(list(X4.glob('*.png'))), 'textures')


if __name__ == '__main__':
    main()
