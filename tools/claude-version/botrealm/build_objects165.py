"""BotRealm: the inns' server-placed objects (owner 2026-10-30).

Reads the inn layout files (DAOC HIATUS/1.65 MODE CLIENT (local only)/layouts/inn_*.json,
"server_world_objects": the Horn of Rallying on its platform, the realm banners, props) and writes
add-objects165.json for the 1.65 database (WorldObject rows; applied by content165.apply_additions and the
launcher's TrueMode.AddRows / Refresh). The horn gets ClassType DOL.GS.HornOfRallyingObject165, so right-clicking
it opens the horn window. Local only; 1.65 Mode only.

    python -I build_objects165.py
"""
import json
import os
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..', '..'))                     # new class test - CLAUDE VERSION
LAYOUTS = os.path.normpath(os.path.join(ROOT, '..', '1.65 MODE CLIENT (local only)', 'layouts'))
REALMS = {'alb': 1, 'mid': 2, 'hib': 3}
NOW = '2026-10-30 00:00:00'
GUILD_BANNERS = {679, 680, 681}                 # GameKeepBanner guild models (show a guild emblem)
REALM_BANNERS = {1: 464, 2: 465, 3: 466}        # GameKeepBanner realm models (the realm's crest)


def main():
    rows = []
    for key, realm in REALMS.items():
        path = os.path.join(LAYOUTS, f'inn_{key}.json')
        layout = json.load(open(path, encoding='utf-8'))
        region = int(layout['region'])
        for i, o in enumerate(layout.get('server_world_objects', [])):
            # the horn's right-click target (the horn itself is client scenery, a scaled Norse Relic Horn; 2026-10-30)
            horn = 'horn of rallying' in o['name'].lower()
            model = int(o['model'])
            if model in GUILD_BANNERS:
                model = REALM_BANNERS[realm]   # a guild banner is blank without a guild: the realm's crest banner
            rows.append({
                'WorldObject_ID': f'true165-inn-{key}-{i}',
                'ClassType': 'DOL.GS.HornOfRallyingObject165' if horn else '',
                'TranslationId': None,
                'Name': 'Horn of Rallying' if horn else o['name'],
                'ExamineArticle': None,
                'X': int(o['world']['x']),
                'Y': int(o['world']['y']),
                'Z': int(o.get('z', o.get('z_terrain', 0))),
                'Heading': int(o.get('heading', 0)) % 4096,
                'Region': region,
                'Model': model,
                'Emblem': 0,
                'Realm': realm,
                'RespawnInterval': 0,
                'LastTimeRowUpdated': NOW,
            })
    out = {
        'table': 'WorldObject',
        'key': 'WorldObject_ID',
        'reason': "True 1.65 Mode traveler's inn objects: the Horn of Rallying, banners, props (inn layouts of the original-client chat)",
        'rows': rows,
    }
    target = os.path.join(HERE, 'add-objects165.json')
    json.dump(out, open(target, 'w', encoding='utf-8'), indent=1)
    runtime = os.path.join(ROOT, 'runtime', 'botrealm')
    if os.path.isdir(runtime):
        shutil.copyfile(target, os.path.join(runtime, 'add-objects165.json'))
    horns = sum(1 for r in rows if r['ClassType'])
    print(f'{len(rows)} objects ({horns} horns) -> {target}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
