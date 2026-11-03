"""BotRealm: the roadside inns' scenery, copied 1:1 from the original-client
layouts (<TRUE165_WORK>/layouts/inn_*.json, pipeline/paths.py - the fixture rows it writes into the original
client's zone archives). The server's inn NPCs, props and horn click target stand on these spots.

Writes content/zones/zoneNNN/inns_165.json ({models, fixtures} in zone.json's own format, model indexes local to the
file) plus the models (models/inn165_<name>.gltf) and their textures (textures/original). ZoneLoader adds them only
when the game runs in True 1.65 Mode (Server.world_mode); normal mode never sees them. Positions: the layout's local
x / y; Z the terrain under the fixture when its ground flag is set (the client's ground follow - placed by ZoneLoader
from its own heights, as build_zone does), else the row's own Z (the horn: z_absolute, lying on its platform top).
Re-run whenever the layouts change:
    python build_inns_165.py
"""
import json, sys
import paths as _paths
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from assets_pipeline import CLIENT, ROOT
import nif_static
from texture_index import TextureIndex
from build_zone import zone_local_npks, read_mpk, read_csv, ambient_only, fixture_radius, save_if_changed

def _lightdef():
    path = next((p for p in (CLIENT / 'zones/textures').iterdir() if p.name.lower() == 'lightdef.csv'), None)
    out = {}
    for r in read_csv(path.read_bytes()) if path else []:
        if len(r) > 6 and r[0].strip().isdigit():
            out[int(r[0])] = {'name': r[1].strip(), 'color': [float(r[k]) / 255.0 for k in (2, 3, 4)],
                              'radius_m': float(r[6]) * nif_static.UNITS_TO_METRES,
                              'enabled': r[7].strip() != '0' if len(r) > 7 else True}
    return out


LIGHTDEF = _lightdef()
LAYOUTS = _paths.TRUE165_WORK / 'layouts'
M = nif_static.UNITS_TO_METRES


def source_bytes(nif_file, zone_id, src_zone):
    """The NIF's bytes: app/items for an item model, else the zone's own nifs folder, the source zone's, zones/Nifs."""
    base = nif_file.rsplit('.', 1)[0].lower()
    if src_zone in ('items', 'housing'):                  # (housing: the player-housing wall torch, phousing/nifs)
        d = CLIENT / ('items' if src_zone == 'items' else 'phousing/nifs')
        p = next((p for p in d.iterdir() if p.name.lower() == base + '.nif'), None)
        return p.read_bytes() if p else None
    npks = {p.name.lower(): p for p in (CLIENT / 'zones/Nifs').iterdir()}
    if isinstance(src_zone, int):
        npks.update(zone_local_npks(src_zone))
    npks.update(zone_local_npks(zone_id))
    npk = npks.get(base + '.npk')
    if npk is None:
        return None
    return next((v for k, v in read_mpk(str(npk)).items() if k.endswith('.nif')), None)


def roads(zone_id, out, tex_names):
    """The inn roads the inn layouts painted into overlay copies of the zone's ter / lod archives
    (layouts/inn_roads_<realm>.json): the ground-blend patches they change, as weight maps of their own
    (splat/inn165_patch_XX_YY_wN.png, the zone.json ground_splat format), and the baked distance ground (lod.png /
    x4plus) with the changed tiles - ZoneLoader swaps both in, in True 1.65 Mode only."""
    import io
    import numpy as np
    from PIL import Image
    import terrain_splat
    ov = LAYOUTS.parent / 'overlay/zones' / f'zone{zone_id:03d}'
    patches, sets = {}, {}
    ter = ov / f'ter{zone_id:03d}.mpk'
    if ter.exists():
        base = terrain_splat.decode(zone_id)
        new = terrain_splat.decode(zone_id, ter)
        (out / 'splat').mkdir(exist_ok=True)
        for (px, py), layers in sorted(new.items()):
            old = base.get((px, py), [])
            same = len(old) == len(layers) and all(
                a['tex'] == b['tex'] and np.abs(a['weight'] - b['weight']).max() < 1.5 / 255 for a, b in zip(old, layers))
            if same:
                continue
            over = layers[1:9]
            files = []
            for k in range(0, 8, 4):
                chans = [over[i]['weight'] if i < len(over) else np.zeros((128, 128), np.float32) for i in range(k, k + 4)]
                img = Image.fromarray((np.clip(np.stack(chans, axis=2), 0, 1) * 255).astype(np.uint8), 'RGBA')
                name = f'inn165_patch_{px:02d}_{py:02d}_w{k // 4}.png'
                save_if_changed(img, out / 'splat' / name)
                files.append(f'splat/{name}')
            patches[f'{px}_{py}'] = {'layers': [{'tex': l['tex'], 'tiles': l['tiles'], 'tileable': l['tileable']}
                                                for l in layers[:9]], 'weights': files}
            tex_names |= {l['tex'] for l in layers[:9]}
    lod = ov / f'lod{zone_id:03d}.mpk'
    if lod.exists():
        theirs = read_mpk(str(lod))
        ours = read_mpk(str(CLIENT / f'zones/zone{zone_id:03d}/lod{zone_id:03d}.mpk'))
        changed = [k for k in theirs if k.startswith('lod') and theirs[k] != ours.get(k)]
        for res, scale in (('original', 1), ('x4plus', 2)):
            src = out / f'terrain/{res}/lod.png'
            if not src.exists() or not changed:
                continue
            mosaic = Image.open(src)
            tw = mosaic.width // 8
            for k in changed:                                  # lodXX-YY.dds: column XX east, row YY south
                x, y = int(k[3:5]), int(k[6:8])
                tile = Image.open(io.BytesIO(theirs[k])).convert(mosaic.mode).resize((tw, tw), Image.BICUBIC)
                mosaic.paste(tile, (x * tw, y * tw))
            save_if_changed(mosaic, out / f'terrain/{res}/lod165.png')
            sets['lod' if res == 'original' else 'lod_x4plus'] = f'terrain/{res}/lod165.png'
        print('  lod tiles changed', changed)
    return patches, sets


def build(layout_path):
    lay = json.loads(layout_path.read_text())
    zone_id = int(lay['zone'])
    out = ROOT / f'godot/content/zones/zone{zone_id:03d}'
    mdir = out / 'models'
    tex_names, models, index, fixtures = set(), [], {}, []
    for fx in lay['fixtures']:
        key = fx['nif_file'].lower()
        if key not in index:
            raw = source_bytes(fx['nif_file'], zone_id, fx.get('source', {}).get('zone'))
            if raw is None:
                print('  missing model', fx['nif_file'])
                continue
            base = 'inn165_' + key.rsplit('.', 1)[0]
            info = nif_static.convert(raw, str(mdir / f'{base}.gltf'), '../textures/original')
            tex_names |= info['textures']
            index[key] = len(models)
            models.append({'nif': fx['nif_file'], 'name': fx['name'], 'file': f'models/{base}.gltf',
                           'triangles': info['triangles'], 'collision_triangles': info['collision_triangles'],
                           'ambient_only': ambient_only(fx.get('nifs_row', [])),
                           'warnings': [f'{base}: {w}' for w in info['warnings']]})
        row = fx['fixtures_row']
        on_ground = row[11].strip() != '0'
        z = float(fx.get('z_absolute', row[5] or 0))
        fixtures.append({'m': index[key], 'name': row[2].strip(), 'pos': [fx['local']['x'] * M, z * M, fx['local']['y'] * M],
                         'yaw': -float(row[6] or 0), 'scale': float(row[7] or 100) / 100.0,
                         'collide': row[8].strip() not in ('', '0'), 'radius': fixture_radius(row),
                         'ground_follow': on_ground, 'id': int(row[0]), 'source': 'inn_165'})
    splat_patches, terrain_sets = roads(zone_id, out, tex_names)
    idx = TextureIndex()
    idx.first = [f'zones/zone{zone_id:03d}/nifs', 'phousing/nifs']
    odir = out / 'textures/original'
    missing = []
    for n in sorted(tex_names):
        img = idx.image(n) or nif_static.EMBEDDED.get(n)
        if img is None:
            missing.append(n)
            continue
        save_if_changed(img, odir / f'{n}.png')
    # the night lights the layout writes into the zone's lights.csv (x, y, z absolute, lightdef), as build_zone reads them
    lights = []
    for lt in lay.get('lights', []):
        x, y, z, kind = (float(v) for v in lt['row'][:4])
        d = LIGHTDEF.get(int(kind))
        if d is None or not d['enabled']:
            print('  light type not enabled', lt['row'])
            continue
        lights.append({'pos': [x * M, z * M, y * M], 'type': int(kind), 'name': d['name'], 'color': d['color'],
                       'range_m': d['radius_m'], 'source': 'inn_165'})
    # zone fixtures the layout takes out of the zone's tables (trees under the new buildings) - by fixtures.csv ID
    removed = [int(r['fixture_id']) for r in lay.get('removed_zone_fixtures', [])]
    (out / 'inns_165.json').write_text(json.dumps({'layout': layout_path.name, 'town': lay.get('town'),
                                                   'models': models, 'fixtures': fixtures, 'lights': lights,
                                                   'removed_fixtures': removed, 'splat_patches': splat_patches,
                                                   'terrain_sets': terrain_sets,
                                                   # the map's marker (owner 2026-10-31): at the innkeeper, zone units
                                                   'map': {'units': [lay['npc_spots']['innkeeper']['local']['x'],
                                                                     lay['npc_spots']['innkeeper']['local']['y']],
                                                           'label': "TRAVELER'S INN",
                                                           # (Huginfell's own map name sits just above the inn)
                                                           **({'side': 'below'} if zone_id == 102 else {})}}, indent=1))
    print(f"zone {zone_id} ({lay.get('zone_name')}): {len(models)} models, {len(fixtures)} fixtures, "
          f"{len(tex_names)} textures, {len(lights)} lights, removed {removed}, road patches {sorted(splat_patches)}" + (f", missing {missing}" if missing else ''))
    for m in models:
        for w in m['warnings']:
            print('  warning', w)


if __name__ == '__main__':
    for p in [LAYOUTS / f'inn_{r}.json' for r in ('alb', 'mid', 'hib')]:          # (not inn_roads_*)
        build(p)
