"""True 1.65 housing: only the 1.64 client's housing areas and models (owner 2026-11-02: "i only want the real 1.65
housing areas and models").

The world DB (OpenDAoC) has 200 lots in each of 27 housing zones; the 1.64 client's zones.dat has 12 of them, 4 per
realm (Alb 13 Caerwent, 16 Old Sarum, 17 Rilan, 18 Dalton; Mid 114 Erikstaad, 117 Arothi, 118 Carlingford, 119 Wyndham;
Hib 213 Meath, 215 Dunshire, 217 Torrylin, 218 Kilcullen), each with exactly 200 lots. This archives (true165-rules.json
"remove.where", both copies):
- the lots (DBHouse) outside every 1.64 zone, so no one can buy a lot the period client never had;
- the NPCs (Mob) in those later housing zones;
- merchant rows selling housing furniture (object types 50/51) whose model the 1.64 client's house items (objects.csv
  ids 1315-1599, house_objects_164.tsv from the new client chat) don't have.
Read-only on the database it reads. Usage: python -I build_housing165.py <world db> [--write]"""
import csv, json, os, sqlite3, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import groundz165 as g          # noqa: E402  (its zones.dat: the 1.64 client's)
from regionaudit import load_zones   # noqa: E402

db = sqlite3.connect(f'file:{sys.argv[1]}?mode=ro', uri=True)
write = '--write' in sys.argv
zones = load_zones(g.ZONES_DAT)
HOUSING_REGIONS = (2, 102, 202)
ITEMS_164 = os.path.join(HERE, 'house_objects_164.tsv')


def in_164_zone(region, x, y):
    return any(z['region'] == region and z['x'] <= x < z['x'] + z['w'] and z['y'] <= y < z['y'] + z['h']
               for z in zones.values())


def ids(rows):
    return ','.join("'" + str(r).replace("'", "''") + "'" for r in rows)


lots = [hn for hn, reg, x, y in db.execute("SELECT HouseNumber, RegionID, X, Y FROM DBHouse")
        if not in_164_zone(reg, x, y)]
owned = db.execute(f"SELECT count(*) FROM DBHouse WHERE HouseNumber IN ({','.join(map(str, lots)) or '0'}) "
                   "AND COALESCE(OwnerID,'') <> ''").fetchone()[0]
mobs = [mid for mid, reg, x, y in db.execute(f"SELECT Mob_ID, Region, X, Y FROM Mob WHERE Region IN {HOUSING_REGIONS}")
        if not in_164_zone(reg, x, y)]
models_164 = {int(r['object_id']) for r in csv.DictReader(open(ITEMS_164, encoding='utf-8'), delimiter='\t')}
late_items = [i for i, m in db.execute("SELECT Id_nb, Model FROM ItemTemplate WHERE Object_Type IN (50, 51)") if m not in models_164]
sold = db.execute(f"SELECT count(*) FROM MerchantItem WHERE lower(ItemTemplateID) IN ({ids(i.lower() for i in late_items)})").fetchone()[0]
print(f'lots outside the 1.64 zones: {len(lots)} (owned {owned}); NPCs there: {len(mobs)}; post-1.64 furniture templates: '
      f'{len(late_items)}, merchant rows selling them: {sold}')
tokens = [t.lower() for (t,) in db.execute("SELECT Id_nb FROM ItemTemplate WHERE PackageID = 'HousingMarketToken'")]
print('market return tokens:', len(tokens))
if owned:
    sys.exit('owned lots in a later zone: not removing them automatically')
if not lots or not mobs or not late_items or not tokens:
    sys.exit('nothing (left) to remove: a world refreshed with these rules already lacks them; run on one that has them')

tag = 'True 1.65 housing:'
new = [
    {'table': 'DBHouse', 'key': 'HouseNumber', 'where': f"HouseNumber IN ({','.join(map(str, lots))})",
     'reason': f'{tag} lots in the housing zones the 1.64 client does not have (it has 12, 4 per realm, 200 lots each)'},
    {'table': 'Mob', 'key': 'Mob_ID', 'where': f"Mob_ID IN ({ids(mobs)})",
     'reason': f'{tag} NPCs in the housing zones the 1.64 client does not have'},
    {'table': 'MerchantItem', 'key': 'MerchantItem_ID', 'where': f"lower(ItemTemplateID) IN ({ids(i.lower() for i in late_items)})",
     'reason': f'{tag} housing furniture whose model is not among the 1.64 client\'s house items (objects.csv 1315-1599)'},
    # "market return tokens": no period source has them (the Foundations Guide, the 1.64 /help text and the 1.63 notes
    # only know rent tokens); OpenDAoC's consignment merchant code took them (reachability audit 2026-11-02)
    {'table': 'MerchantItem', 'key': 'MerchantItem_ID', 'where': f"lower(ItemTemplateID) IN ({ids(tokens)})",
     'reason': f'{tag} market return tokens (not in 1.64/1.65: later emulator travel tokens)'},
]
if write:
    for folder in (HERE, os.path.join(HERE, '..', '..', '..', 'runtime', 'true165')):
        rp = os.path.join(folder, 'true165-rules.json')
        raw = open(rp, 'rb').read()
        rules = json.loads(raw.decode('utf-8'))
        rules['remove']['where'] = [w for w in rules['remove']['where'] if not w['reason'].startswith(tag)] + new
        nl = b'\r\n' if b'\r\n' in raw else b'\n'
        open(rp, 'wb').write(json.dumps(rules, indent=1, ensure_ascii=False).encode('utf-8').replace(b'\n', nl) + (nl if raw.endswith(nl) else b''))
        print('written', rp)
