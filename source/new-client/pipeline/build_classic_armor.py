"""BotRealm: worn armour, cloaks and helms on the CLASSIC
figures (build_classic_races.py), the original client's way. Spec: TRUE 1.65 MODE CLASSIC FIGURES.md.

    python build_classic_armor.py              # every objects.csv row with a classic skin (2,000+ models)
    python build_classic_armor.py --upscale    # also Real-ESRGAN the textures

objects.csv per item (the same in 1.64 and SI):
  skins   cols 4 body (male) / 5 body (female) / 6 arms / 7 gloves / 8 lbody / 9 legs / 10 boots / 11 cloak / 12 helm
          - pskins.csv rows, each taken from the archive the row names (items/pskins/pskinNNN.mpk: other archives keep
          older same-named copies), keyed "<texture>__a<archive>"
  meshes  cols 13 Head# / 14 Body# / 15 Lbody# / 16 Cloak# / 32 Arms# / 33 Gloves# / 34 Legs# / 35 Boots#, and the
          female overrides 37 Body# / 38 Lbody# / 39 Arms# / 40 Gloves# / 41 Legs# / 42 Boots#
  helm    col 2 (items.csv: the NIF in app/items) and the alternates the race's monsters.csv 'Alt Helm' picks:
          1 -> col 24, 2 -> col 28, 3 -> col 29, 4 -> col 30, 5 -> col 31 (0 or an empty alternate: col 2); the helm
          skin (col 12) on it
Writes godot/content/characters/classic_armor.json {"models": {model: {"skins": {...}, "mesh": {...}, "fem": {...},
"helm": [file col 2, col 24, col 28, col 29]}}}, textures in characters/classic_textures/original and the helm meshes
in godot/content/helms_classic/<nif>__<texture>/model.gltf.
"""
import csv, io, json, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from assets_pipeline import CLIENT, ROOT
from daoc_mpk import read_mpk
from build_helms import archive_image, nif_textures
from build_zone import upscale_dir, save_if_changed
import nif_static

OUT = ROOT / 'godot/content/characters/classic_armor.json'
TEX = ROOT / 'godot/content/characters/classic_textures'
HELMS = ROOT / 'godot/content/helms_classic'
SKIN_COLS = {'body_m': 4, 'body_f': 5, 'arms': 6, 'gloves': 7, 'lbody': 8, 'legs': 9, 'boots': 10, 'cloak': 11, 'helm': 12}
MESH_COLS = {'head': 13, 'body': 14, 'lbody': 15, 'cloak': 16, 'arms': 32, 'gloves': 33, 'legs': 34, 'boots': 35}
FEM_COLS = {'body': 37, 'lbody': 38, 'arms': 39, 'gloves': 40, 'legs': 41, 'boots': 42}
HELM_COLS = (2, 24, 28, 29, 30, 31)                   # 'Alt Helm' 0 (and none), 1, 2, 3, 4, 5


def num(v):
    v = str(v).strip()
    return int(v) if v.lstrip('-').isdigit() else 0


def use_x4plus():
    """The helm meshes on the upscaled textures where they exist (as the Catacombs helms: content/helms x4plus)."""
    up = HELMS / 'textures/x4plus'
    if not up.exists():
        return
    have = {p.name.lower() for p in up.glob('*.png')}
    for g in HELMS.glob('*/model.gltf'):
        doc = json.loads(g.read_text(encoding='utf-8'))
        changed = False
        for im in doc.get('images', []):
            name = im['uri'].rsplit('/', 1)[-1]
            if im['uri'].startswith('../textures/original/') and name.lower() in have:
                im['uri'] = '../textures/x4plus/' + name
                changed = True
        if changed:
            g.write_text(json.dumps(doc), encoding='utf-8')


def main():
    g = read_mpk(str(CLIENT / 'gamedata.mpk'))
    rows = lambda n: list(csv.reader(io.StringIO(g[n].decode('latin1'))))
    objects = [r for r in rows('objects.csv') if r and r[0].strip().isdigit()]
    pskins = {r[0].strip(): r for r in rows('pskins.csv') if r and r[0].strip().isdigit()}
    items = {r[0].strip(): r for r in rows('items.csv') if r and r[0].strip().isdigit()}
    files = {p.name.lower(): p for p in (CLIENT / 'items').iterdir()}
    (TEX / 'original').mkdir(parents=True, exist_ok=True)
    HELMS.mkdir(parents=True, exist_ok=True)
    (HELMS / 'textures/original').mkdir(parents=True, exist_ok=True)
    out, missing, textures, helm_built = {}, [], {}, {}

    def skin_key(sid):
        r = pskins.get(str(sid).strip())
        if r is None or not r[2].strip():
            return '', None
        arc = num(r[4])
        return r[2].strip().rsplit('.', 1)[0].lower() + (f'__a{arc}' if arc > 0 else ''), r

    def export(key, row, dst):
        if key in textures:
            return textures[key]
        img = archive_image(row)
        if img is None:
            from texture_index import TextureIndex
            img = TextureIndex().image(key.split('__a')[0])
        textures[key] = img is not None
        if img is None:
            missing.append(('texture', key))
        else:
            save_if_changed(img, dst / f'{key}.png')
        return textures[key]

    def helm_mesh(item_id, tex_key, tex_row):
        it = items.get(str(item_id))
        if it is None or not it[2].strip() or it[2].strip().lower() == 'loot':
            return ''
        nif = it[2].strip()
        path = files.get(nif.lower() + '.nif')
        if path is None:
            missing.append(('helm nif', nif))
            return ''
        folder = nif.lower() + (f'__{tex_key}' if tex_key else '')
        if folder not in helm_built:
            d = HELMS / folder
            d.mkdir(parents=True, exist_ok=True)
            info = nif_static.convert(path.read_bytes(), str(d / 'model.gltf'), '../textures/original')
            used = set(info['textures'])
            # the item's helm skin on the mesh where it is the mesh's own texture (the archive the row names), or
            # on the whole helm when the mesh has one texture: one helm NIF serves several items, each with its own
            # Helm skin (owner 2026-10-30: Osric's Albion Guard Plate Helmet 95, g1_helm2 on helm2, drew the plain
            # near-white b_plt1_helm2 of the Alb plate helm). A mesh with several textures keeps its others.
            base = tex_key.split('__a')[0] if tex_key else ''
            own = nif_textures(path)
            if tex_key and (base in own or len(own) == 1):
                swap = base if base in own else next(iter(own))
                doc = json.loads((d / 'model.gltf').read_text(encoding='utf-8'))
                for im in doc.get('images', []):
                    if im['uri'].rsplit('/', 1)[-1].rsplit('.', 1)[0].lower() == swap:
                        im['uri'] = f'../textures/original/{tex_key}.png'
                (d / 'model.gltf').write_text(json.dumps(doc), encoding='utf-8')
                used = (used - {swap}) | {tex_key}
            for t in sorted(used):
                if t == tex_key:
                    export(t, tex_row, HELMS / 'textures/original')
                else:
                    from texture_index import TextureIndex
                    img = TextureIndex().image(t)
                    if img is None:
                        missing.append(('helm texture', t))
                    else:
                        save_if_changed(img, HELMS / 'textures/original' / f'{t}.png')
            helm_built[folder] = True
        return f'{folder}/model.gltf'

    for r in objects:
        if len(r) < 43:
            r = r + [''] * (43 - len(r))
        skins = {}
        for slot, col in SKIN_COLS.items():
            if num(r[col]) > 0:
                key, row = skin_key(r[col])
                if key and slot != 'helm' and export(key, row, TEX / 'original'):
                    skins[slot] = key
                elif key and slot == 'helm':
                    skins[slot] = key
                    skins['_helm_row'] = row
        if not skins:
            continue
        entry = {'name': r[1].strip(), 'mesh': {k: num(r[c]) for k, c in MESH_COLS.items() if num(r[c])}}
        fem = {k: num(r[c]) for k, c in FEM_COLS.items() if num(r[c])}
        if fem:
            entry['fem'] = fem
        helm_row = skins.pop('_helm_row', None)
        if 'helm' in skins:
            files_ = [helm_mesh(r[c].strip(), skins['helm'], helm_row) if num(r[c]) > 0 else '' for c in HELM_COLS]
            if any(files_):
                entry['helm'] = files_
        entry['skins'] = skins
        out[r[0].strip()] = entry
    if '--upscale' in sys.argv:
        upscale_dir(TEX / 'original', TEX / 'x4plus', factor_after=4)
        upscale_dir(HELMS / 'textures/original', HELMS / 'textures/x4plus', factor_after=4)
    use_x4plus()
    OUT.write_text(json.dumps({'models': out, 'missing': missing}, indent=1), encoding='utf-8')
    print(len(out), 'models,', len(helm_built), 'helm meshes,', sum(1 for v in textures.values() if v), 'textures,',
          len(missing), 'missing:', missing[:12])


if __name__ == '__main__':
    main()
