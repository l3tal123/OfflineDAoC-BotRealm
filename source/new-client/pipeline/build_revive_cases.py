"""Test data for godot/tests/companion_revive_probe.gd: every True 1.65 class and race it allows, both genders, each
with gear that class wears (the world database's item templates: per slot the most common model the class may use at
levels 40-51 - weapons, shield or instrument, armour, cloak, helm).

    python build_revive_cases.py      -> godot/tests/data/companion_revive_cases.json
"""
import collections, json, sqlite3
import paths as _paths
from assets_pipeline import ROOT

CLASSES = ROOT / 'godot/content/rules/server_classes.json'
OUT = ROOT / 'godot/tests/data/companion_revive_cases.json'
# the client's race id -> its classic look row (male, female): Server.RACES
LOOKS = {1: (32, 35), 2: (61, 65), 3: (39, 43), 4: (48, 52), 5: (503, 507), 6: (137, 145), 7: (185, 193), 8: (169, 177),
         9: (302, 310), 10: (286, 294), 11: (334, 342), 12: (318, 326), 13: (716, 724), 14: (773, 781), 15: (700, 708)}
REALM_ID = {'alb': 1, 'mid': 2, 'hib': 3}
ARMOUR_SLOTS = (21, 22, 23, 25, 26, 27, 28)                   # helm, hands, feet, torso, cloak, legs, arms
WEAPON_SLOTS = (10, 11, 12, 13)                               # right hand, left hand (shield / off hand), two hand, ranged


def main():
    db = sqlite3.connect('file:%s?mode=ro' % _paths.WORLD_DB, uri=True)
    rows = db.execute('select Model, Item_Type, Object_Type, AllowedClasses, Realm, Extension, Color from ItemTemplate '
                      'where Level between 40 and 51 and Model > 0').fetchall()
    by = collections.defaultdict(collections.Counter)
    for model, slot, otype, allowed, realm, ext, color in rows:
        ids = [int(x) for x in str(allowed or '').replace(';', ',').split(',') if x.strip().isdigit()]
        for cid in ids:
            by[(cid, int(slot))][(int(model), int(ext or 0), int(color or 0))] += 1
    classes = json.loads(CLASSES.read_text(encoding='utf-8'))['classes']
    cases = []
    for cid, c in sorted(classes.items(), key=lambda kv: int(kv[0])):
        gear = []
        for slot in ARMOUR_SLOTS + WEAPON_SLOTS:
            top = by.get((int(cid), slot))
            if top:
                (model, ext, color), _n = top.most_common(1)[0]
                gear.append({'slot': slot, 'model': model, 'extension': ext, 'color': color})
        for race in c['races']:
            for gender in (0, 1):
                if race not in LOOKS:
                    continue
                cases.append({'class_id': int(cid), 'class': c['name'], 'race': race, 'gender': gender,
                              'model': LOOKS[race][gender], 'realm': REALM_ID[c['realm']], 'gear': gear})
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps({'source': 'world database item templates + rules/server_classes.json', 'cases': cases},
                              indent=1), encoding='utf-8')
    print(len(cases), 'cases ->', OUT, '; classes without gear:',
          [classes[k]['name'] for k in classes if not any(by.get((int(k), s)) for s in ARMOUR_SLOTS + WEAPON_SLOTS)])


if __name__ == '__main__':
    main()
