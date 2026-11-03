"""BotRealm: every player race and gender as the CLASSIC figure
the original client draws when its figure version is Classic (user.dat [character_version] 0 - the 1.65 profile). Spec: TRUE 1.65 MODE CLASSIC FIGURES.md.

    python build_classic_races.py                 # all figures -> godot/content/characters/classic/<prefix>/
    python build_classic_races.py cel_m tro_f     # only these
    python build_classic_races.py --upscale       # also Real-ESRGAN the textures (classic_textures/x4plus)

Per figure (keyed by the same prefix as the Catacombs one, cel_m = HCeltM): figure.gltf - the race NIF's skeleton,
EVERY part variant as its own node (Body1..5 / Body1b..5b, Arms1..6, Gloves1..3, LBody1..4, Legs1..4, Boots1..5,
HeadA1..5 / HeadB1..5 - hair base texture on UV set 0, the face decal on set 1 -, Cloak1..3 with all 3 UV sets) and
the classic anim set's clips under the same names as the Catacombs figures (build_races CLIPS / emotes / combat /
styles), so every caller plays them unchanged - and figure.json:
  monnifs     id, file, anim set (col 3), strides (cols 4-7)
  monsters.csv player rows of the race (Head = hair skin col 4, Face skin col 18, Hair set col 19, Head Geom col 20):
              'looks' {model: {face, hair, geom, hair_set}}; the defaults per slot (cols 3, 5-10); flags kilt (16),
              sarac (17), fem (22), obj scale (23), norse (24), helm scale (25), alt helm (26), cloak body (28),
              size (12), sound set (15), shield scale (46)
  hair.csv    the race's hair sets (8 colours each)
  fig3tofigmain / fig3tofighaircolor: a Catacombs look (face, hair style, hair colour) -> the classic model row and
              old hair colour, as the client maps a Catacombs-made character onto its classic figure
"""
import csv, io, json, sys, collections
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from daoc_mpk import read_mpk
from assets_pipeline import CLIENT, ROOT
import nif_character as nc
import build_races as br
from build_monsters import find_figure, skinned_parts

OUT = ROOT / 'godot/content/characters/classic'
TEX = ROOT / 'godot/content/characters/classic_textures'
# prefix -> (eRace id, gender 0/1, monnifs id, first and last player row of monsters.csv)
FIGURES = {
    'bri_m': (1, 0, 206, 32, 34), 'bri_f': (1, 1, 209, 35, 38), 'ava_m': (2, 0, 217, 61, 64), 'ava_f': (2, 1, 218, 65, 68),
    'hig_m': (3, 0, 210, 39, 42), 'hig_f': (3, 1, 211, 43, 46), 'sar_m': (4, 0, 213, 48, 51), 'sar_f': (4, 1, 214, 52, 55),
    'nor_m': (5, 0, 239, 503, 506), 'nor_f': (5, 1, 240, 507, 510), 'tro_m': (6, 0, 237, 137, 144),
    'tro_f': (6, 1, 238, 145, 152), 'dwa_m': (7, 0, 243, 185, 192), 'dwa_f': (7, 1, 244, 193, 200),
    'kob_m': (8, 0, 241, 169, 176), 'kob_f': (8, 1, 242, 177, 184), 'cel_m': (9, 0, 250, 302, 309),
    'cel_f': (9, 1, 251, 310, 317), 'fir_m': (10, 0, 254, 286, 293), 'fir_f': (10, 1, 255, 294, 301),
    'elf_m': (11, 0, 252, 334, 341), 'elf_f': (11, 1, 253, 342, 349), 'lur_m': (12, 0, 256, 318, 325),
    'lur_f': (12, 1, 257, 326, 333), 'inc_m': (13, 0, 348, 716, 723), 'inc_f': (13, 1, 349, 724, 731),
    'val_m': (14, 0, 351, 773, 780), 'val_f': (14, 1, 352, 781, 788), 'syl_m': (15, 0, 346, 700, 707),
    'syl_f': (15, 1, 347, 708, 715),
    # the Shrouded Isles races: no player race in True 1.65 Mode, but the world's NPCs of them (build_monsters
    # RACE_FIGURES bhalfogrem -> hal_m, nsvartalfm -> fro_m, hsharm -> sha_m ...) are drawn with these prefixes, and
    # without a classic figure ~155 of them drew broken (2026-11-02). Each has its own classic NIF and 12 rows per sex
    'hal_m': (16, 0, 449, 1008, 1019), 'hal_f': (16, 1, 450, 1020, 1031), 'fro_m': (17, 0, 464, 1051, 1062),
    'fro_f': (17, 1, 465, 1063, 1074), 'sha_m': (18, 0, 490, 1075, 1086), 'sha_f': (18, 1, 491, 1087, 1098)}
SLOT_COLS = {'body': 3, 'arms': 5, 'gloves': 6, 'lbody': 7, 'legs': 8, 'boots': 9, 'cloak': 10}
PART_SLOT = (('Body', 'body'), ('Arms', 'arms'), ('Gloves', 'gloves'), ('LBody', 'lbody'), ('Legs', 'legs'),
             ('Boots', 'boots'), ('Cloak', 'cloak'), ('Head', 'head'))


class Tables:
    def __init__(self):
        g = read_mpk(str(CLIENT / 'gamedata.mpk'))
        self.rows = lambda n: list(csv.reader(io.StringIO(g[n].decode('latin1'))))
        self.monsters = {r[0].strip(): r for r in self.rows('monsters.csv') if r and r[0].strip().isdigit()}
        self.monnifs = {r[0].strip(): r for r in self.rows('monnifs.csv') if r and r[0].strip().isdigit()}
        self.skins = {r[0].strip(): r for r in self.rows('skins.csv') if r and r[0].strip().isdigit()}
        self.hair = {int(r[0]): [x.strip() for x in r[2:10]] for r in self.rows('hair.csv') if r and r[0].strip().isdigit()}
        self.f2c = [r for r in self.rows('fig3tofigmain.csv')[2:] if len(r) > 5 and r[1].strip().isdigit()]
        self.f2c_hair = [r for r in self.rows('fig3tofighaircolor.csv')[2:] if len(r) > 3 and r[0].strip().isdigit()]

    def skin_tex(self, sid):
        r = self.skins.get(str(sid).strip())
        return r[2].strip().rsplit('.', 1)[0] if r and r[2].strip() else ''


def num(v, default=0.0):
    try:
        return float(str(v).strip())
    except ValueError:
        return default


# figtofig3main.csv's row names per figure: "<RACE> <MALE|FEMALE> n"
F2F_RACE = {'bri': 'BRITON', 'ava': 'AVALONIAN', 'hig': 'HIGHLANDER', 'sar': 'SARACEN', 'nor': 'NORSE', 'tro': 'TROLL',
            'dwa': 'DWARF', 'kob': 'KOBOLD', 'cel': 'CELT', 'fir': 'FIRBOLG', 'elf': 'ELF', 'lur': 'LURIKEEN',
            'inc': 'INCONNU', 'val': 'VALKYN', 'syl': 'SYLVAN', 'hal': 'HALF_OGRE', 'fro': 'FROSTALF', 'sha': 'SHAR'}


def player_looks_of(t, prefix):
    """The period player looks of a race + sex, in the client's order (figtofig3main.csv rows "<RACE> <MALE|FEMALE> n",
    n = 1..12, model = column 2, each model once) - the creation page's Face - / + (owner 2026-10-31: period pregame;
    the server's LOOK list is the same rule, ClassicFigures165 "Looks")."""
    race, sex = F2F_RACE[prefix[:3]], ('FEMALE' if prefix.endswith('_f') else 'MALE')
    found = {}
    for r in t.rows('figtofig3main.csv')[2:]:
        if len(r) < 2:
            continue
        parts = r[0].strip().upper().split()
        if len(parts) == 3 and parts[1] == sex and parts[2].isdigit() and parts[0].startswith(race):
            n = int(parts[2])
            if 1 <= n <= 12 and n not in found and r[1].strip().isdigit():
                found[n] = int(r[1])
    out = []
    for n in sorted(found):
        if found[n] not in out:
            out.append(found[n])
    return out


def build_figure(t, prefix, textures):
    race, gender, mn_id, first, last = FIGURES[prefix]
    mn = t.monnifs[str(mn_id)]
    nif = find_figure(mn[2].strip())
    if nif is None:
        print(prefix, 'MISSING figure', mn[2])
        return None
    rows = [t.monsters[str(i)] for i in range(first, last + 1) if str(i) in t.monsters]
    pick = lambda col: collections.Counter(r[col].strip() for r in rows).most_common(1)[0][0]
    defaults = {slot: t.skin_tex(pick(col)) for slot, col in SLOT_COLS.items()}
    looks = {}
    # every monsters.csv row drawn with this figure (col 2 = its monnifs id), not only the player rows: the client's
    # face / hair picker (fig3tofigmain) and the server's NPCs use them all (owner 2026-10-30: the hire window's face
    # and hair buttons never changed the preview - Briton male models 471 / 473 / 14 / 257 ... weren't here)
    same_fig = [r for r in t.monsters.values() if len(r) > 20 and r[2].strip() == str(mn_id)]
    for r in rows + [r for r in same_fig if r not in rows]:
        looks[r[0].strip()] = {'face': t.skin_tex(r[18]), 'hair': t.skin_tex(r[4]), 'geom': int(num(r[20], 1)) or 1,
                               'hair_set': int(num(r[19]))}
    hair_sets = {}
    for lk in looks.values():
        hs = lk['hair_set']
        if hs in t.hair:
            hair_sets[str(hs)] = [t.skin_tex(x) for x in t.hair[hs]]
    # the Catacombs look -> classic row and hair colour (fig3tofigmain race index = eRace - 1)
    cata = {}
    for r in t.f2c:
        if int(r[1]) == race - 1 and int(r[2]) == gender:
            cata[f'{int(r[3])},{int(r[4])}'] = int(r[5])
    cata_hair = {}
    for r in t.f2c_hair:
        if int(r[0]) == race - 1:
            cata_hair[str(int(r[1]))] = int(num(r[3], 1))
    # every part node, each with its slot's default texture (the game sets the worn ones)
    nodes = [n for n in skinned_parts(nif) if not n.lower().startswith('lod')]
    parts = {}
    first_look = looks[str(first)] if str(first) in looks else next(iter(looks.values()))
    for n in nodes:
        slot = next((s for p, s in PART_SLOT if n.lower().startswith(p.lower())), None)
        if slot is None:
            continue
        # (a slot without a default skin - the Celts' cloak - still gets its meshes: the game paints every part)
        tex = first_look['hair'] if slot == 'head' else (defaults.get(slot, '') or first_look['hair'])
        if tex:
            parts[n] = tex
    clips = {br.CLIPS[k]: v for k, v in nc.clip_files(int(mn[3]), tuple(br.CLIPS)).items() if k in br.CLIPS}
    em = br.emote_slots()
    have = {p.name.lower() for p in (CLIENT / 'anims').iterdir()}
    clips.update({em[k]: v for k, v in nc.clip_files(int(mn[3]), tuple(em)).items() if v[0].lower() in have})
    every = {**br.COMBAT_CLIPS, **br.style_clips()[0]}
    combat, _ = nc.combat_clip_files(int(mn[3]), tuple(every))
    clips.update({every[k]: v for k, v in combat.items() if v[0].lower() in have})
    out = OUT / prefix
    out.mkdir(parents=True, exist_ok=True)
    player_looks = player_looks_of(t, prefix)
    if '--looks-only' in sys.argv:                     # only the look table (+ its textures): the figure stays as built
        meta = json.loads((out / 'figure.json').read_text(encoding='utf-8'))
        meta.update({'looks': looks, 'hair_sets': hair_sets, 'cata_to_classic': cata, 'cata_hair_to_classic': cata_hair,
                     'player_looks': player_looks})
        (out / 'figure.json').write_text(json.dumps(meta, indent=1), encoding='utf-8')
        for x in [*(lk['face'] for lk in looks.values()), *(lk['hair'] for lk in looks.values()),
                  *(c for hs in hair_sets.values() for c in hs)]:
            if x:
                textures.add(x)
        missing = sorted({str(v) for v in cata.values() if str(v) not in looks})
        print(f'{prefix}: {len(looks)} looks' + (f', picker models without a look: {missing}' if missing else ''))
        return meta
    report = nc.build(nif, parts, clips, str(out / 'figure.gltf'), '../../classic_textures/original')
    r0 = rows[0]
    meta = {
        'classic': True, 'prefix': prefix, 'race': race, 'gender': 'female' if gender else 'male', 'nif': nif,
        'monnifs': mn_id, 'anim_set': int(mn[3]),
        'strides_units': {'run': num(mn[4]), 'walk': num(mn[5]), 'back': num(mn[6]), 'strafe': num(mn[7])},
        'clip_seconds': {k: v['seconds'] for k, v in report.items() if 'seconds' in v},
        'failed_clips': {k: v['failed'] for k, v in report.items() if 'failed' in v},
        'scale': num(pick(12), 100) / 100.0, 'sound_set': int(num(pick(15))),
        'flags': {'kilt': pick(16) == '1', 'sarac': pick(17) == '1', 'norse': pick(24) == '1',
                  'cloak_body': pick(28) == '1', 'fem': pick(22) == '1', 'alt_helm': int(num(pick(26)))},
        'obj_scale': num(pick(23), 100) / 100.0, 'helm_scale': num(pick(25), 100) / 100.0,
        'shield_scale': num(pick(46), 100) / 100.0,
        'defaults': defaults, 'nodes': sorted(parts), 'looks': looks, 'first_look': str(first), 'hair_sets': hair_sets,
        'cata_to_classic': cata, 'cata_hair_to_classic': cata_hair, 'player_looks': player_looks,
    }
    (out / 'figure.json').write_text(json.dumps(meta, indent=1), encoding='utf-8')
    for x in [*defaults.values(), *(lk['face'] for lk in looks.values()), *(lk['hair'] for lk in looks.values()),
              *(c for hs in hair_sets.values() for c in hs)]:
        if x:
            textures.add(x)
    print(f'{prefix}: {nif} set {mn[3]}, {len(parts)} parts, {len(looks)} looks, {len(clips)} clips '
          f'({len(meta["failed_clips"])} failed), flags {meta["flags"]}')
    return meta


def export_textures(names, dst_dir):
    """Every classic skin as PNG, from the archive the client loads it from (figures/skins, Mskins, items/pskins)."""
    from texture_index import TextureIndex
    idx = TextureIndex()
    dst_dir.mkdir(parents=True, exist_ok=True)
    missing = []
    for name in sorted(set(n.lower() for n in names)):
        dst = dst_dir / f'{name}.png'
        if dst.exists():
            continue
        img = idx.image(name)
        if img is None:
            missing.append(name)
            continue
        img.convert('RGBA').save(dst)
    return missing


def main():
    want = [a for a in sys.argv[1:] if not a.startswith('--')]
    t = Tables()
    textures = set()
    index = {}
    for prefix in FIGURES:
        if want and prefix not in want:
            continue
        m = build_figure(t, prefix, textures)
        if m:
            index[prefix] = {'race': m['race'], 'gender': m['gender'], 'file': f'classic/{prefix}/figure.gltf'}
    missing = export_textures(textures, TEX / 'original')
    print(len(textures), 'textures, missing:', missing)
    path = OUT / 'classic.json'
    old = json.loads(path.read_text()) if path.exists() else {}
    old.update(index)
    path.write_text(json.dumps(old, indent=1), encoding='utf-8')
    if '--upscale' in sys.argv:
        from build_zone import upscale_dir
        upscale_dir(TEX / 'original', TEX / 'x4plus', factor_after=4)


if __name__ == '__main__':
    main()
