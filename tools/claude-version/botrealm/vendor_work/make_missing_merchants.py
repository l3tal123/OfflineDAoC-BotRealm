"""The period merchants our world was missing (owner 2026-10-31: "deep dive them"), as BotRealm content.
Spawns: positions from CapnBry (period radar data) via missing_merchants.json; Melanaha's town from the period pages, her
spot beside the town's merchants (derived). Each spawn copies the nearest existing merchant of its region for the columns
no source gives (model is the agent's derived pick), and sells only its period stock. Not placed: Somyr and Caryan
(free gear for /level characters: not merchants), Rhodyn and Tempeste (no period location).
Also: Dyemaster Mara takes her period name, Marna."""
import json
import math
import os
import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", ".."))  # found from this script
TARGETS = [os.path.join(CV, 'tools', 'claude-version', 'botrealm'), os.path.join(CV, 'runtime', 'botrealm')]
TAG = '1.65 period merchants (missing merchants)'
SKIP = {'Somyr', 'Caryan', 'Rhodyn', 'Tempeste'}
CLASS = {'Royd': 'DOL.GS.GameBountyMerchant'}   # bounty totems (our world's bounty merchants use this class)
# titles in our world's own words: cloth-dye sellers are Dye Merchants, hat sellers Armor Merchants
TITLE = {'Madarl': 'Dye Merchant', 'Nealcail': 'Dye Merchant', 'Melanaha': 'Armor Merchant'}

data = json.load(open(os.path.join(HERE, 'missing_merchants.json'), encoding='utf-8'))
db = sqlite3.connect(f"file:{os.path.join(HERE, 'world165_snapshot.db')}?mode=ro", uri=True)
db.row_factory = sqlite3.Row
cols = [r[1] for r in db.execute('pragma table_info(Mob)')]
merchants = [dict(r) for r in db.execute("select * from Mob where ClassType like '%Merchant%'")]
items = {r[0].lower() for r in db.execute('select Id_nb from ItemTemplate')}
for f in ('add-vendoritems165.json', 'add-deaditems165.json'):
    p = os.path.join(HERE, f)
    if os.path.exists(p):
        items |= {r['Id_nb'].lower() for r in json.load(open(p, encoding='utf-8'))['rows']}

spawns, stock, report = [], [], []
for m in data['missing']:
    name = m['name']
    pl = m.get('placement') if isinstance(m.get('placement'), dict) else None
    if name in SKIP or not pl or 'Region' not in pl:
        report.append(f'{name}: not placed ({(pl or {}).get("kind", m.get("placement"))})')
        continue
    near = min((r for r in merchants if r['Region'] == pl['Region']),
               key=lambda r: math.hypot(r['X'] - pl['X'], r['Y'] - pl['Y']))
    row = {k: near[k] for k in cols}
    lid = (m.get('stock_list') or {}).get('new_list_id') or f'p165list_{name.replace(" ", "_")}'
    row.update({'Mob_ID': f'p165mob_{name.replace(" ", "_")}', 'Name': name, 'Guild': TITLE.get(name) or pl.get('Guild') or near['Guild'],
                'X': pl['X'], 'Y': pl['Y'], 'Z': pl['Z'], 'Heading': pl.get('Heading', near['Heading']),
                'Region': pl['Region'], 'Realm': pl.get('Realm', near['Realm']), 'Level': pl.get('Level', near['Level']),
                'Model': pl.get('Model', near['Model']), 'Size': pl.get('Size', near['Size']),
                'ClassType': CLASS.get(name, pl.get('ClassType') or 'DOL.GS.GameMerchant'),
                'ItemsListTemplateID': lid, 'NPCTemplateID': 0, 'PathID': '', 'PackageID': 'True165Vendor',
                'LastTimeRowUpdated': '2026-10-31 00:00:00'})
    good = [r for r in (m.get('stock_list') or {}).get('rows', [])
            if r.get('confidence', 'high') in ('high', 'medium') and r['template_id'].lower() in items]
    if not good:
        report.append(f'{name}: no stock with an existing item, not placed')
        continue
    spawns.append(row)
    for r in good:
        stock.append({'MerchantItem_ID': f"p165list-{lid}-{r['page']}-{r['slot']}", 'ItemListID': lid,
                      'ItemTemplateID': r['template_id'], 'PageNumber': r['page'], 'SlotPosition': r['slot'],
                      'LastTimeRowUpdated': '2026-10-31 00:00:00'})
    report.append(f"{name}: placed region {pl['Region']} {pl['X']},{pl['Y']},{pl['Z']} ({pl.get('kind', '')}), "
                  f"copied from {near['Name']}, {len(good)} items, {row['ClassType'].split('.')[-1]} '{row['Guild']}'")

updates = [{'sql': "UPDATE Mob SET Name='Dyemaster Marna' WHERE Name='Dyemaster Mara' AND Region=10",
            'reason': f"{TAG}: Dyemaster Mara is the period Dyemaster Marna (CapnBry spot 76 units away; all 17 of her period enamels are on this list)"}]
for folder in TARGETS:
    for fname, table, key, reason, rows in (
            ('add-missingmerchants165.json', 'Mob', 'Mob_ID',
             'True 1.65 period merchants our world was missing (positions from CapnBry period data; vendor pass 2)', spawns),
            ('add-missingmerchantstock165.json', 'MerchantItem', 'MerchantItem_ID',
             'True 1.65 period stock of the restored missing merchants (period Allakhazam pages; vendor pass 2)', stock)):
        with open(os.path.join(folder, fname), 'w', encoding='utf-8', newline='\n') as f:
            json.dump({'table': table, 'key': key, 'reason': reason, 'rows': rows}, f, ensure_ascii=False, indent=1)
    rp = os.path.join(folder, 'true165-rules.json')
    raw = open(rp, 'rb').read()
    rules = json.loads(raw.decode('utf-8'))
    rules['updates'] = [u for u in rules['updates'] if not u['reason'].startswith(TAG)] + updates
    nl = b'\r\n' if b'\r\n' in raw else b'\n'
    out = json.dumps(rules, indent=1, ensure_ascii=False).encode('utf-8').replace(b'\n', nl)
    open(rp, 'wb').write(out + (nl if raw.endswith(nl) else b''))
print('\n'.join(report))
print(f'spawns {len(spawns)}, stock rows {len(stock)}')
