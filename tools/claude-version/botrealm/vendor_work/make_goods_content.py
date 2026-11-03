"""Merchant pages pass (owner 2026-10-31): the period Allakhazam merchant pages' "Goods for Sale" lists as stock.
Input: merchant_goods_additions.json (same record format as merchant_stock_additions.json). Output: add-merchantgoods165.json
in both botrealm folders, and its dead-row repoints as tagged updates in true165-rules.json. Run AFTER make_vendor_content.py
and make_missing_merchants.py: free slots are checked against the snapshot plus every installed add-*.json MerchantItem row."""
import json
import os
import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", ".."))  # found from this script
RUNTIME = os.path.join(CV, 'runtime', 'botrealm')
TARGETS = [os.path.join(CV, 'tools', 'claude-version', 'botrealm'), RUNTIME]
TAG = '1.65 period merchant stock (merchant pages pass)'
OUTFILE = 'add-merchantgoods165.json'

adds = json.load(open(os.path.join(HERE, 'merchant_goods_additions.json'), encoding='utf-8'))
if isinstance(adds, dict):
    adds = adds.get('additions') or adds.get('rows') or next(v for v in adds.values() if isinstance(v, list))
# the goods items built from their item pages (build_goodsitems165.py): install them and stock them too
built_path = os.path.join(HERE, 'add-goodsitems165.json')
if os.path.exists(built_path):
    for folder in TARGETS:
        with open(os.path.join(folder, 'add-goodsitems165.json'), 'w', encoding='utf-8', newline=chr(10)) as f:
            json.dump(json.load(open(built_path, encoding='utf-8')), f, ensure_ascii=False, indent=1)
    adds = adds + json.load(open(os.path.join(HERE, 'goods_built_additions.json'), encoding='utf-8'))
db = sqlite3.connect(f"file:{os.path.join(HERE, 'world165_snapshot.db')}?mode=ro", uri=True)
items = {r[0].lower() for r in db.execute('select Id_nb from ItemTemplate')}
taken = {(l.lower(), p, s) for l, p, s in db.execute('select ItemListID, PageNumber, SlotPosition from MerchantItem')}
on_list = {(l.lower(), t.lower()) for l, t in db.execute('select ItemListID, ItemTemplateID from MerchantItem')}
for f in os.listdir(RUNTIME):
    if not (f.startswith('add-') and f.endswith('.json')) or f == OUTFILE:
        continue
    d = json.load(open(os.path.join(RUNTIME, f), encoding='utf-8'))
    if d['table'] == 'ItemTemplate':
        items |= {r['Id_nb'].lower() for r in d['rows']}
    elif d['table'] == 'MerchantItem':
        for r in d['rows']:
            taken.add((r['ItemListID'].lower(), r['PageNumber'], r['SlotPosition']))
            on_list.add((r['ItemListID'].lower(), r['ItemTemplateID'].lower()))

rows, updates, skipped = [], [], {}


def skip(why):
    skipped[why] = skipped.get(why, 0) + 1


for a in adds:
    if a.get('confidence') not in ('high', 'medium'):
        skip('low confidence')
        continue
    tid, lid = a['item_template_id'], a['list_id']
    if tid.lower() not in items:
        skip('item not in world or built')
        continue
    if a.get('action') == 'fix_dead_row' and a.get('dead_row_template_id'):
        updates.append({'sql': (f"UPDATE MerchantItem SET ItemTemplateID='{tid.replace(chr(39), chr(39) * 2)}' WHERE lower(ItemListID)=lower('{lid}') "
                                f"AND lower(ItemTemplateID)=lower('{a['dead_row_template_id'].replace(chr(39), chr(39) * 2)}')"),
                        'reason': f"{TAG}: {a.get('item_name')} ({a.get('merchant_period_name')}), was the missing id {a['dead_row_template_id']}"})
        continue
    if (lid.lower(), tid.lower()) in on_list:
        skip('already on the list')
        continue
    page, slot = a.get('suggested_page'), a.get('suggested_slot')
    if page is None or slot is None or (lid.lower(), page, slot) in taken:
        # take the first free slot of the list instead (5 pages x 30)
        free = next(((p, s) for p in range(5) for s in range(30) if (lid.lower(), p, s) not in taken), None)
        if free is None:
            skip('list full')
            continue
        page, slot = free
    taken.add((lid.lower(), page, slot))
    on_list.add((lid.lower(), tid.lower()))
    rows.append({'MerchantItem_ID': f'p165goods-{lid}-{page}-{slot}', 'ItemListID': lid, 'ItemTemplateID': tid,
                 'PageNumber': page, 'SlotPosition': slot, 'LastTimeRowUpdated': '2026-10-31 00:00:00'})

for folder in TARGETS:
    with open(os.path.join(folder, OUTFILE), 'w', encoding='utf-8', newline='\n') as f:
        json.dump({'table': 'MerchantItem', 'key': 'MerchantItem_ID',
                   'reason': "True 1.65 period merchant stock from the period Allakhazam merchant pages' Goods for Sale "
                             "(Wayback <= 2003-11; merchant pages pass)", 'rows': rows}, f, ensure_ascii=False, indent=1)
    rp = os.path.join(folder, 'true165-rules.json')
    raw = open(rp, 'rb').read()
    rules = json.loads(raw.decode('utf-8'))
    rules['updates'] = [u for u in rules['updates'] if not u['reason'].startswith(TAG)] + updates
    nl = b'\r\n' if b'\r\n' in raw else b'\n'
    out = json.dumps(rules, indent=1, ensure_ascii=False).encode('utf-8').replace(b'\n', nl)
    open(rp, 'wb').write(out + (nl if raw.endswith(nl) else b''))
print(f'goods rows {len(rows)}, dead-row repoints {len(updates)}, skipped {skipped}')
