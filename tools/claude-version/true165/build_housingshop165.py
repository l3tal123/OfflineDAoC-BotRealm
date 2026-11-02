"""True 1.65 housing prices and decorator stock from period sources (housing research 2026-11-02).

- Rent: 2% of the deed price a week (Herald's revised Foundations price list, 2003-06-23, gamebanshee.com/fdah; the
  in-game help text "Paying Rent"): cottage 20g, house 200g, villa 800g, mansion 2p. The world had 20g / 35g / 60g / 1p.
- Deeds: cottage 1p, house 10p, villa 40p, mansion 100p (same list); the world had house 5p, villa 10p, mansion 25p.
- Hibernian paintings: slots 2 and 6 cost 10g and the others 25g in every realm (wayback-allakhazam item pages,
  Aug-Oct 2003); four Hibernian prices had them swapped.
- Siegecraft supplies (hookpoint): 1p until mid-2004 (the Herald list; a 2004 note quotes its "old price of 1
  platinum"); the world had 100g.
- The Albion and Midgard decorators' lists name 24 / 25 items the world has no template for; they get templates with
  exactly those ids, as the Hibernian ones (copied from housing_indoor_shop55): models from the 1.64 client's
  objects.csv (paintings Alb 1382-1391, Mid 1402-1411, Mid wall map 1381), prices from the period item pages where
  captured, else the slot pattern. Tapestry and arch models are the remaining 1.64 ones not used by Hibernia (marked
  as guesses: to be checked in the client).
Usage: python -I build_housingshop165.py <world db> [--write]"""
import json, os, sqlite3, sys

HERE = os.path.dirname(os.path.abspath(__file__))
db = sqlite3.connect(f'file:{sys.argv[1]}?mode=ro', uri=True)
write = '--write' in sys.argv
TAG = 'True 1.65 housing prices:'

SETTINGS = {'housing_rent_cottage': '200000', 'housing_rent_house': '2000000', 'housing_rent_villa': '8000000',
            'housing_rent_mansion': '20000000'}
DEEDS = {'house': 100000000, 'villa': 400000000, 'mansion': 1000000000}
PRICE_FIX = {'housing_indoor_shop55': 250000, 'housing_indoor_shop56': 100000, 'housing_indoor_shop58': 250000,
             'housing_indoor_shop60': 100000, 'housing_siegecraft_supplies': 10000000}
ALB = [('Icons_of_Caer_Sidi_', 'Icons of Caer Sidi', 1382, 250000), ('Gothwaite_Harbor_at_Night', 'Gothwaite Harbor at Night', 1383, 100000),
       ('Sunset_Over_Avalon_Isle', 'Sunset Over Avalon Isle', 1384, 250000), ('Agony_of_Defeat', 'Agony of Defeat', 1385, 250000),
       ('Caer_Gothwaite', 'Caer Gothwaite', 1386, 250000), ('Stonehenge', 'Stonehenge', 1387, 100000),
       ('Sunset_at_Gothwaite_Harbor', 'Sunset at Gothwaite Harbor', 1388, 250000), ('Bridge_to_Clifton', 'Bridge to Clifton', 1389, 250000),
       ('Anniogel_at_Night', 'Anniogel at Night', 1390, 250000), ('Safety_in_the_Distance', 'Safety in the Distance', 1391, 250000)]
MID = [('Glacial_Resting_Place', 'Glacial Resting Place', 1402, 250000), ('Frozen_Horizon', 'Frozen Horizon', 1403, 100000),
       ('Aegirhamn_Docks', 'Aegirhamn Docks', 1404, 250000), ('Darkness_Falls', 'Darkness Falls', 1405, 250000),
       ('Skyline_of_Jordheim', 'Skyline of Jordheim', 1406, 250000), ('Queen_Kulas_Splendor', "Queen Kula's Splendor", 1407, 100000),
       ('Hazards_of_Iarnvidiurs_Lair', "Hazards of Iarnvidiur's Lair", 1408, 250000), ('Wintry_Forest_Sunrise', 'Wintery Forest Sunrise', 1409, 250000),
       ('Sanctuary', 'Sanctuary', 1410, 250000), ('Fog_Over_Hagall', 'Fog Over Hagall', 1411, 250000)]
OTHER = [  # (id, name, model, price, object type, realm, guess)
    ('A_Hero_of_Albion_wall_tapestry', 'A Hero of Albion wall tapestry', 1572, 780000, 50, 1, True),
    ('An_Albion_Adept_wall_tapestry', 'An Albion Adept wall tapestry', 1592, 750000, 50, 1, True),
    ('granite_arch', 'granite arch', 1453, 500000, 49, 1, True),
    ('walled_granite_arch', 'walled granite arch', 1463, 1000000, 49, 1, True),
    ('Midgard_wall_map', 'Midgard wall map', 1381, 50000, 50, 2, False),
    ('A_Hero_of_Midgard_wall_tapestry', 'A Hero of Midgard wall tapestry', 1576, 780000, 50, 2, True),
    ('Three_Defenders_of_Midgard_wall_tapestry', 'Three Defenders of Midgard wall tapestry', 1593, 750000, 50, 2, True),
    ('crossed_beam_arch', 'crossed beam arch', 1466, 500000, 49, 2, True),
    ('walled_crossed-beam_arch', 'walled crossed-beam arch', 1467, 1000000, 49, 2, True),
]

cols = [r[1] for r in db.execute('PRAGMA table_info(ItemTemplate)')]
base = dict(zip(cols, db.execute("SELECT * FROM ItemTemplate WHERE Id_nb = 'housing_indoor_shop55'").fetchone()))
items = []


def item(idn, name, model, price, otype, realm, note):
    if db.execute("SELECT 1 FROM ItemTemplate WHERE lower(Id_nb)=lower(?)", (idn,)).fetchone():
        return
    r = dict(base)
    r.update(Id_nb=idn, Name=name, Model=model, Price=price, Object_Type=otype, Realm=realm,
             ItemTemplate_ID=f'p165-housing-decor-{idn}', LastTimeRowUpdated='2026-11-02 12:00:00',
             PackageID='True165Housing' + ('-modelguess' if note else ''))
    items.append(r)


for realm, rows in ((1, ALB), (2, MID)):
    for idn, name, model, price in rows:
        item(idn, name, model, price, 50, realm, False)
        # the list's id for "Icons_of_Caer_Sidi_" is Icons_of_Caer_Sidi_grandiose_painting (one underscore)
        item(idn.rstrip('_') + '_grandiose_painting', name + ' grandiose painting', model, 400000, 50, realm, False)
for idn, name, model, price, otype, realm, guess in OTHER:
    item(idn, name, model, price, otype, realm, guess)

updates = []
for kind, price in DEEDS.items():
    updates.append({'sql': f"UPDATE ItemTemplate SET Price = {price} WHERE Id_nb LIKE '%\\_{kind}\\_deed' ESCAPE '\\'",
                    'reason': f'{TAG} {kind} deed {price // 10000000}p (Herald revised Foundations price list, 2003-06-23)'})
for idn, price in PRICE_FIX.items():
    updates.append({'sql': f"UPDATE ItemTemplate SET Price = {price} WHERE Id_nb = '{idn}'",
                    'reason': f'{TAG} {idn} at its period price (period item pages / Herald list)'})
print(f'decorator templates {len(items)} ({sum(1 for i in items if "modelguess" in i["PackageID"])} model guesses), '
      f'deed and price updates {len(updates)}, rent settings {SETTINGS}')
if write:
    for folder in (HERE, os.path.join(HERE, '..', '..', '..', 'runtime', 'true165')):
        json.dump({'table': 'ItemTemplate', 'key': 'Id_nb', 'reason': f'{TAG} the Albion and Midgard decorators\' period '
                   'paintings, tapestries and arches (build_housingshop165.py)', 'rows': items},
                  open(os.path.join(folder, 'add-housingdecor165.json'), 'w', encoding='utf-8', newline='\n'), indent=1)
        rp = os.path.join(folder, 'true165-rules.json')
        raw = open(rp, 'rb').read()
        rules = json.loads(raw.decode('utf-8'))
        rules['settings'].update(SETTINGS)
        rules['updates'] = [u for u in rules['updates'] if not u.get('reason', '').startswith(TAG)] + updates
        nl = b'\r\n' if b'\r\n' in raw else b'\n'
        open(rp, 'wb').write(json.dumps(rules, indent=1, ensure_ascii=False).encode('utf-8').replace(b'\n', nl) + (nl if raw.endswith(nl) else b''))
    print('written')
