"""BotRealm: spell visuals and sounds from the REAL 1.64 client's own tables (owner 2026-10-31:
"Switch True 1.65 to the 1.64 spell table" - the later client's spells.csv gave e.g. the necromancer shade's Regenerate
Flesh (9461) hand glows the period client never had; 673 of the 4,046 shared rows differ in glows, sounds, cast
animations or hit / bolt / pulse effects).

Sources (DAoC 1.64.zip at DAOC_164_ZIP, its gamedata.mpk extracted under TRUE165_WORK/work/164 - pipeline/paths.py):
   gamedata spells.csv / speffects.csv / spnifs.csv - the same columns as build_spell_fx.py reads (spells.csv 0 ID, 1 Name,
   3-5 Anim1-3, 6-7 hand glows, 8-9 cast effects, 10 Projectile, 11 Target, 12 Buildup, 13 Pulse, 14 Sound Set, 15 Radius,
   16 Resist; 1.64 has no Projectile Lag column). anims.csv: the casting columns carry the same names in both clients
   (only two emote columns differ), so the 1.64 anim numbers map onto the clips the classic figures already have.
   effects/*.nif: 351 of the NIFs 1.64 names are byte-identical to the later client's (the converted model is shared);
   the ones that differ are converted from the 1.64 bytes into content/monsters/effects164/<nif>/, with the 1.64
   effects folder's own textures first; a NIF the 1.64 zip lacks keeps the later conversion.
   sounds/splsnds.csv: 28 spell sound sets, the same ids / names / wav files as the later cc_spells.csv's first 28
   (checked here - the build stops if one differs), so a 1.64 Sound Set plays through the existing sound index.

Writes godot/content/spells/spell_fx_165.json: {"effects": {client effect: {...}}, "fx": {fx id + 100000: {...}}} -
1.64 effect ids are offset by FX_BASE so they never collide with the later table's ids; SpellFx merges this over the
later table in True 1.65 Mode (a client effect the 1.64 table lacks - a later-only server spell - keeps the later row).
    python build_spell_fx_164.py"""
import csv, io, json, sys, zipfile
import paths as _paths
from pathlib import Path
from assets_pipeline import CLIENT, ROOT
from daoc_mpk import read_mpk
import nif_character as nc
from build_races import cast_clip
from texture_index import TextureIndex
import build_monsters

ZIP = _paths.ZIP_164
GAMEDATA = _paths.TRUE165_WORK / 'work' / '164' / 'gamedata.mpk'
OUT = ROOT / 'godot/content/spells/spell_fx_165.json'
EFX164 = build_monsters.OUT / 'effects164'
FX_BASE = 100000


def table(text):
    return {r[0].strip(): r for r in csv.reader(io.StringIO(text)) if r and r[0].strip().isdigit()}


def num(r, i):
    try:
        return int(float(r[i])) if i < len(r) and r[i].strip() else 0
    except ValueError:
        return 0


class Textures164:
    """build_effect's texture lookup: the 1.64 effects folder's own textures first, then the later client's index."""
    def __init__(self, z, later):
        self.z, self.later = z, later
        self.files = {}
        for n in z.namelist():
            low = n.lower()
            if low.startswith('effects/') and low.rsplit('.', 1)[-1] in ('tga', 'dds', 'bmp'):
                self.files[low.split('/')[-1].rsplit('.', 1)[0]] = n

    def image(self, key):
        from PIL import Image
        n = self.files.get(key.lower())
        if n:
            try:
                img = Image.open(io.BytesIO(self.z.read(n)))
                img.load()
                return img.convert('RGBA')
            except Exception as e:                       # (a format PIL can't read: the later client's copy)
                print('1.64 texture unreadable, using the later one:', n, e)
        return self.later.image(key)


def check_sounds(z):
    later = json.loads((ROOT / 'godot/content/sounds/sounds.json').read_text(encoding='utf-8')).get('spells', {})
    rows = table(z.read('sounds/splsnds.csv').decode('latin1'))
    files = table(z.read('sounds/sndfiles.csv').decode('latin1'))
    bad = []
    for sid, r in rows.items():
        l = later.get(sid, {})
        for ev, col in (('gather', 2), ('cast', 4), ('hit', 6)):
            f = files.get(r[col].strip(), ['', ''])[1].strip().lower().rsplit('.', 1)[0] if r[col].strip() != '0' else ''
            lf = str(l.get(ev, '')).lower().removeprefix('spell_')
            if f != lf:
                bad.append((sid, r[1], ev, f, l.get(ev)))
    if bad:
        raise SystemExit(f'1.64 sound sets differ from the later index: {bad}')
    print(len(rows), '1.64 sound sets = the later index\'s ids 1 -', max(int(k) for k in rows))
    return {int(k) for k in rows}


def main():
    z = zipfile.ZipFile(ZIP)
    g = read_mpk(str(GAMEDATA))
    spells, speffects, spnifs = (table(g[n].decode('latin1')) for n in ('spells.csv', 'speffects.csv', 'spnifs.csv'))
    header, _f, _x = nc.animation_tables()
    old_header = list(csv.reader(io.StringIO(g['anims.csv'].decode('latin1'))))[1]
    sound_ids = check_sounds(z)

    def anim_clip(n):
        col = n + 2
        if not (0 <= col < len(old_header)):
            return ''
        name = old_header[col]
        assert col >= len(header) or header[col].strip() == name.strip() or name.startswith('EM '), (col, name)
        ok = 'Start' in name or 'Mid' in name or 'End' in name or any(w in name.lower() for w in ('lute', 'drum', 'flute'))
        return cast_clip(name) if ok else ''

    effects, fx_used = {}, set()
    for ce, r in spells.items():
        e = {'name': r[1].strip(), 'anims': [anim_clip(num(r, i)) for i in (3, 4, 5)],
             'gather': [x for x in (num(r, 6), num(r, 7)) if x], 'cast_fx': [x for x in (num(r, 8), num(r, 9)) if x],
             'projectile': num(r, 10), 'target': num(r, 11), 'buildup': num(r, 12), 'pulse': num(r, 13),
             'sound_set': num(r, 14), 'radius': num(r, 15), 'resist': num(r, 16), 'lag_ms': 0}
        if e['sound_set'] and e['sound_set'] not in sound_ids:
            print('spell', ce, e['name'], 'names a sound set 1.64 lacks:', e['sound_set'], '- silent')
            e['sound_set'] = 0
        fx_used.update(e['gather'] + e['cast_fx'] + [e[k] for k in ('projectile', 'target', 'buildup', 'pulse', 'radius', 'resist') if e[k]])
        for k in ('gather', 'cast_fx'):
            e[k] = [x + FX_BASE for x in e[k]]
        for k in ('projectile', 'target', 'buildup', 'pulse', 'radius', 'resist'):
            if e[k]:
                e[k] += FX_BASE
        effects[ce] = e

    zfx = {n.lower().split('/')[-1]: n for n in z.namelist() if n.lower().startswith('effects/') and n.lower().endswith('.nif')}
    later_files = {p.name.lower(): p for p in (CLIENT / 'effects').iterdir()}
    later_idx = TextureIndex()
    tex = Textures164(z, later_idx)
    fx, missing, own, shared, later_only = {}, [], [], [], []
    for fid in sorted(fx_used):
        r = speffects.get(str(fid))
        if r is None:
            missing.append(f'speffect {fid}')
            continue
        nr = spnifs.get(str(num(r, 2)))
        base = nr[2].strip().lower() if nr else ''
        if not base or base == 'empty':
            continue
        src = zfx.get(base + '.nif')
        lf = later_files.get(base + '.nif')
        if src is not None and (lf is None or z.read(src) != lf.read_bytes()):
            # the 1.64 NIF differs: its own conversion
            d = EFX164 / base
            if not (d / 'model.gltf').exists() or '--reconvert-effects' in sys.argv:
                tmp = ROOT / 'pipeline/_work/nif164' / (base + '.nif')     # (a work copy, outside the game's content)
                tmp.parent.mkdir(parents=True, exist_ok=True)
                tmp.write_bytes(z.read(src))
                saved = build_monsters.EFX
                build_monsters.EFX = EFX164
                try:
                    ok = build_monsters.build_effect(base, tex, {base + '.nif': tmp})
                finally:
                    build_monsters.EFX = saved
                if not ok:
                    missing.append(base)
                    continue
            nif = f'res://content/monsters/effects164/{base}/model.gltf'
            own.append(base)
        else:
            if not build_monsters.build_effect(base, later_idx, later_files):
                missing.append(base)
                continue
            nif = f'res://content/monsters/effects/{base}/model.gltf'
            (shared if src is not None else later_only).append(base)
        fx[str(fid + FX_BASE)] = {'name': r[1].strip(), 'nif': nif, 'node': r[3].strip(),
                                  'duration_s': num(r, 4) / 1000.0, 'rate': (num(r, 5) or 100) / 100.0,
                                  'track': num(r, 6), 'autoscale': num(r, 7) == 1, 'spin': num(r, 8)}
    for f in fx.values():                                     # the cast effects' release moment (build_spell_fx.py)
        gl = json.loads((ROOT / 'godot' / f['nif'].replace('res://', '')).read_text(encoding='utf-8'))
        rel = [next((k[0] for k in a.get('vis', []) if k[1]), None) for a in gl.get('extras', {}).get('nif_node_anims', [])
               if not a.get('visible', True)]
        rel = [x for x in rel if x is not None]
        if rel:
            f['release_s'] = round(min(rel), 3)
    for e in effects.values():
        rel = [fx[str(i)].get('release_s', 0.0) for i in e['cast_fx'] if str(i) in fx]
        if rel and max(rel) > 0 and not e['projectile']:
            e['release_s'] = max(rel)
    if '--no-upscale' not in sys.argv:
        from build_zone import upscale_dir
        for base in sorted(set(own)):
            d = EFX164 / base
            upscale_dir(d / 'textures/original', d / 'textures/x4plus', factor_after=4)
    OUT.write_text(json.dumps({'effects': effects, 'fx': fx, 'fx_base': FX_BASE,
                               'source': '1.64 real client: gamedata spells.csv / speffects.csv / spnifs.csv, effects/*.nif, '
                                         'sounds/splsnds.csv'}, indent=1), encoding='utf-8')
    print(len(effects), 'client effects,', len(fx), 'effects:', len(set(own)), '1.64-only NIFs,', len(set(shared)),
          'shared,', len(set(later_only)), 'later-only; missing', missing[:20])


if __name__ == '__main__':
    main()
