"""Turn the vendor work into BotRealm content (owner 2026-10-31: "do what you can now"; second pass when the crawl is done).
Inputs (this folder): merchant_stock_additions.json, add-vendoritems165.json (optional, the new period items).
Outputs: add-vendoritems165.json + add-merchantstock165.json copied into both botrealm folders, and the dead-row repoints
added to true165-rules.json "updates" (both copies). Only high / medium confidence additions; low ones wait for pass 2."""
import json
import os
import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
CV = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", ".."))  # found from this script
TARGETS = [os.path.join(CV, 'tools', 'claude-version', 'botrealm'), os.path.join(CV, 'runtime', 'botrealm')]
STOCK_REASON = ("True 1.65 period merchant stock: items the period Allakhazam item pages (Wayback <= 2003-11) say a "
                "merchant sold, added to that merchant's list (vendor pass 1, 2026-10-31)")
REPOINT_TAG = "1.65 period merchant stock (vendor pass 1): dead row repointed"

adds = json.load(open(os.path.join(HERE, 'merchant_stock_additions.json'), encoding='utf-8'))
items_path = os.path.join(HERE, 'add-vendoritems165.json')
new_items = json.load(open(items_path, encoding='utf-8')) if os.path.exists(items_path) else {'rows': []}
built = {r['Id_nb'].lower() for r in new_items['rows']}

db = sqlite3.connect(f"file:{os.path.join(HERE, 'world165_snapshot.db')}?mode=ro", uri=True)
templates = {r[0].lower() for r in db.execute('select Id_nb from ItemTemplate')}
used_slots = {(l.lower(), p, s) for l, p, s in db.execute('select ItemListID, PageNumber, SlotPosition from MerchantItem')}

rows, repoints, skipped = [], [], {}
seen = set()
for a in adds:
    if a['confidence'] not in ('high', 'medium'):
        skipped['low confidence (pass 2)'] = skipped.get('low confidence (pass 2)', 0) + 1
        continue
    tid = a['item_template_id']
    if tid.lower().startswith('p165_item_') and tid.lower() not in built:
        skipped['planned item not built'] = skipped.get('planned item not built', 0) + 1
        continue
    if not tid.lower().startswith('p165_item_') and tid.lower() not in templates:
        skipped['template missing'] = skipped.get('template missing', 0) + 1
        continue
    if a['action'] == 'fix_dead_row':
        dead = a['dead_row_template_id'].replace("'", "''")
        repoints.append({
            'sql': (f"UPDATE MerchantItem SET ItemTemplateID='{tid.replace(chr(39), chr(39) * 2)}' "
                    f"WHERE lower(ItemListID)=lower('{a['list_id']}') AND lower(ItemTemplateID)=lower('{dead}')"),
            'reason': f"{REPOINT_TAG}: {a['item_name']} ({a['merchant_period_name']}), was the missing id {a['dead_row_template_id']}"})
        continue
    page, slot = a.get('suggested_page'), a.get('suggested_slot')
    key = (a['list_id'].lower(), page, slot)
    if page is None or slot is None or key in used_slots or key in seen:
        skipped['no free slot'] = skipped.get('no free slot', 0) + 1
        continue
    seen.add(key)
    rows.append({'MerchantItem_ID': f"p165stock-{a['list_id']}-{page}-{slot}", 'ItemListID': a['list_id'],
                 'ItemTemplateID': tid, 'PageNumber': page, 'SlotPosition': slot,
                 'LastTimeRowUpdated': '2026-10-31 00:00:00'})

# ---- pass 2 (each part only when its file exists)
PASS2_TAG = "1.65 period merchant stock (vendor pass 2)"
pass2_updates = []
dead_path = os.path.join(HERE, 'add-deaditems165.json')
dead_items = json.load(open(dead_path, encoding='utf-8')) if os.path.exists(dead_path) else None
drop_path = os.path.join(HERE, 'pass1_drop.json')   # pass-1 item rows the item agent found to be typo duplicates
if os.path.exists(drop_path):
    drop = {d.lower() for d in json.load(open(drop_path, encoding='utf-8'))}
    new_items['rows'] = [r for r in new_items['rows'] if r['Id_nb'].lower() not in drop]
    built = {r['Id_nb'].lower() for r in new_items['rows']}
    rows = [r for r in rows if not (r['ItemTemplateID'].lower().startswith('p165_item_') and r['ItemTemplateID'].lower() not in built)]
all_items = built | ({r['Id_nb'].lower() for r in dead_items['rows']} if dead_items else set()) | templates
rep_path = os.path.join(HERE, 'deaditems_report.json')
if os.path.exists(rep_path):
    # a dead id with no period page to rebuild from, whose item our world has under another id (same name, list realm):
    # point the shop rows at that item
    for d in json.load(open(rep_path, encoding='utf-8')):
        ex = d.get('existing_same_name')
        if d.get('status') != 'skipped' or not ex or ex.lower() not in templates:
            continue
        pass2_updates.append({
            'sql': (f"UPDATE MerchantItem SET ItemTemplateID='{ex.replace(chr(39), chr(39) * 2)}' "
                    f"WHERE lower(ItemTemplateID)=lower('{d['dead_id'].replace(chr(39), chr(39) * 2)}')"),
            'reason': f"{PASS2_TAG}: shop rows of the missing id {d['dead_id']} point at {ex} (same item, same name)"})
seen_items = {(l.lower(), t.lower()) for l, t in db.execute('select ItemListID, ItemTemplateID from MerchantItem')} | \
             {(r['ItemListID'].lower(), r['ItemTemplateID'].lower()) for r in rows}
swap_path = os.path.join(HERE, 'dfseal_swaps.json')
if os.path.exists(swap_path):
    swaps = json.load(open(swap_path, encoding='utf-8'))
    for sw in (swaps.get('swaps', swaps) if isinstance(swaps, dict) else swaps):
        if sw.get('confidence', 'high') not in ('high', 'medium') or sw.get('currency_ok') is False or sw.get('pass1_added_row') or \
                sw['new_template_id'].lower() not in all_items:
            continue
        # Not a swap any more (merchant pages pass, 2026-10-31): period seal-merchant pages from Oct-Nov 2003 list the
        # Superior / Exceptional / Exquisite versions too, mostly next to the plain ones, so both were sold in 1.65.
        # The later item stays; the period plain item is ADDED beside it on the same list, at the list's first free slot.
        lid = sw['list_id']
        if (lid.lower(), sw['new_template_id'].lower()) in seen_items:
            continue
        free = next(((p, s) for p in range(5) for s in range(30)
                     if (lid.lower(), p, s) not in used_slots and (lid.lower(), p, s) not in seen), None)
        if free is None:
            skipped['no free slot'] = skipped.get('no free slot', 0) + 1
            continue
        seen.add((lid.lower(), free[0], free[1]))
        seen_items.add((lid.lower(), sw['new_template_id'].lower()))
        rows.append({'MerchantItem_ID': f"p165stock-{lid}-{free[0]}-{free[1]}", 'ItemListID': lid,
                     'ItemTemplateID': sw['new_template_id'], 'PageNumber': free[0], 'SlotPosition': free[1],
                     'LastTimeRowUpdated': '2026-10-31 00:00:00'})
list_rows = []
TITLES = {'p165list_Eluned': 'Armor Merchant'}          # robes: our world's robe sellers are Armor Merchants
PERIOD_NAMES = {'Chelseigh Stilman': 'Chelseigh Stillman'}   # the period pages' spelling
lists_path = os.path.join(HERE, 'new_lists.json')
if os.path.exists(lists_path):
    nl = json.load(open(lists_path, encoding='utf-8'))
    for lst in (nl.get('lists', nl) if isinstance(nl, dict) else nl):
        lid = lst['new_list_id']
        good = [r for r in lst['rows'] if r['template_id'].lower() in all_items and r.get('confidence', 'high') in ('high', 'medium')]
        if not good:
            continue
        for r in good:
            list_rows.append({'MerchantItem_ID': f"p165list-{lid}-{r['page']}-{r['slot']}", 'ItemListID': lid,
                              'ItemTemplateID': r['template_id'], 'PageNumber': r['page'], 'SlotPosition': r['slot'],
                              'LastTimeRowUpdated': '2026-10-31 00:00:00'})
        for npc in (lst['npc'] if isinstance(lst['npc'], list) else [lst['npc']]):
            if not npc.get('mob_id'):
                continue
            cls = lst.get('target_class') or 'DOL.GS.GameMerchant'
            # owner 2026-10-31: give them titles. Our world calls every crafting-supply seller "Merchant" and the robe
            # sellers "Armor Merchant"; a merchant that already has a title keeps it. Names take the period spelling.
            title = npc.get('current_guild') or TITLES.get(lst['new_list_id'], 'Merchant')
            name = PERIOD_NAMES.get(npc['name'], npc['name']).replace("'", "''")
            pass2_updates.append({
                'sql': (f"UPDATE Mob SET ClassType='{cls}', ItemsListTemplateID='{lid}', Guild='{title}', "
                        f"Name='{name}' WHERE Mob_ID='{npc['mob_id']}'"),
                'reason': f"{PASS2_TAG}: {npc['name']} sells the period stock list {lid}"})
repoints += pass2_updates

stock = {'table': 'MerchantItem', 'key': 'MerchantItem_ID', 'reason': STOCK_REASON, 'rows': rows}
for folder in TARGETS:
    if dead_items and dead_items['rows']:
        with open(os.path.join(folder, 'add-deaditems165.json'), 'w', encoding='utf-8', newline='\n') as f:
            json.dump(dead_items, f, ensure_ascii=False, indent=1)
    if list_rows:
        with open(os.path.join(folder, 'add-merchantlists165.json'), 'w', encoding='utf-8', newline='\n') as f:
            json.dump({'table': 'MerchantItem', 'key': 'MerchantItem_ID',
                       'reason': 'True 1.65 period merchant lists for NPCs whose list held none of their period items, '
                                 'or who were period sellers but not merchants (vendor pass 2)', 'rows': list_rows}, f,
                      ensure_ascii=False, indent=1)
print(f"pass 2: dead-id items {len(dead_items['rows']) if dead_items else 0}, seal swaps / new-list NPC updates {len(pass2_updates)}, new list rows {len(list_rows)}")
for folder in TARGETS:
    with open(os.path.join(folder, 'add-merchantstock165.json'), 'w', encoding='utf-8', newline='\n') as f:
        json.dump(stock, f, ensure_ascii=False, indent=1)
    if new_items['rows']:
        with open(os.path.join(folder, 'add-vendoritems165.json'), 'w', encoding='utf-8', newline='\n') as f:
            json.dump(new_items, f, ensure_ascii=False, indent=1)
    rp = os.path.join(folder, 'true165-rules.json')
    raw = open(rp, 'rb').read()
    rules = json.loads(raw.decode('utf-8'))
    rules['updates'] = [u for u in rules['updates']
                        if not u['reason'].startswith(REPOINT_TAG) and not u['reason'].startswith(PASS2_TAG)] + repoints
    nl = b'\r\n' if b'\r\n' in raw else b'\n'
    out = json.dumps(rules, indent=1, ensure_ascii=False).encode('utf-8').replace(b'\n', nl)
    open(rp, 'wb').write(out + (nl if raw.endswith(nl) else b''))
print(f'new items {len(new_items["rows"])}, stock rows {len(rows)}, dead rows repointed {len(repoints)}, skipped {skipped}')
