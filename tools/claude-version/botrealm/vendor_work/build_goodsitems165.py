"""Merchant pages pass: build the goods items that have a period item page but no ItemTemplate (owner 2026-10-31:
"build them now"). Same parser, encodings, sibling and period-quality rules as build_vendoritems165.py (its build()).
Id_nb = p165_item_<page key>. Writes add-goodsitems165.json, goods_built_additions.json (one add_row per merchant list,
same record format as merchant_stock_additions.json) and goodsitems_report.json in this folder."""
import datetime
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import pageparse  # noqa: E402
import build_vendoritems165 as bv  # noqa: E402


def main():
    bv.W = bv.World()
    now = datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')
    pages = [json.loads(l) for l in open(bv.PAGES, encoding='utf-8')]
    allpages = {r['key']: r for r in pages}
    parsed = {r['key']: pageparse.parse(r) for r in pages}
    taken_ids = {r['Id_nb'].lower() for r in bv.W.rows}   # the world's items, then the built ones
    for f in ('add-vendoritems165.json', 'add-deaditems165.json'):
        taken_ids |= {r['Id_nb'].lower() for r in json.load(open(os.path.join(HERE, f), encoding='utf-8'))['rows']}
    unresolved = json.load(open(os.path.join(HERE, 'merchant_goods_unresolved.json'), encoding='utf-8'))['unresolved']
    wanted = [u for u in unresolved if u['status'].startswith('buildable') and u.get('page_key')]

    rows, adds, report, done = [], [], [], {}
    for u in wanted:
        key = u['page_key']
        iid = f'p165_item_{key}'
        if key not in done:
            ent = {'page_key': key, 'name': u['item_name'], 'status': 'skipped', 'reason': None}
            report.append(ent)
            done[key] = ent
            rec = allpages.get(key)
            if rec is None:
                ent['reason'] = 'page not in the crawl'
            elif iid.lower() in taken_ids:
                ent['status'] = 'exists'      # built in an earlier pass: only its stock row is new
                ent['reason'] = f'{iid} already exists (stocked only)'
            else:
                p = parsed[key]
                st = bv.page_stats(p)
                st['left'] = p['left_hand_usable']
                if st['item_type'] == 40 or st['item_type'] is None:
                    ent['reason'] = 'inventory / crafting item: the page gives no object type, model or spell'
                else:
                    bon, unknown = bv.map_bonuses(p)
                    tmp = {'derived': {}, 'price_source': 'none'}
                    row, why = bv.build(rec, p, st, bon, unknown, tmp, bv.strip_suffix(rec['name']), parsed, allpages, now)
                    if row is None:
                        ent['reason'] = why
                    else:
                        row['Id_nb'] = row['ItemTemplate_ID'] = iid
                        rows.append(row)
                        taken_ids.add(iid.lower())
                        ent.update(status='built', derived=tmp['derived'], price_source=tmp['price_source'],
                                   sibling=tmp.get('sibling'), item={k: row.get(k) for k in
                                   ('Name', 'Level', 'Realm', 'Object_Type', 'Item_Type', 'DPS_AF', 'SPD_ABS', 'Quality', 'Price')})
        if done[key]['status'] in ('built', 'exists'):
            adds.append({'page_key': key, 'item_name': u['item_name'], 'item_realm': u['realm'],
                         'merchant_period_name': u['merchant_period_name'], 'list_id': u['list_id'],
                         'item_template_id': iid, 'confidence': 'high', 'action': 'add_row',
                         'why': 'merchant pages pass: the period merchant page lists it; item built from its period item page',
                         'suggested_page': None, 'suggested_slot': None})
    json.dump({'table': 'ItemTemplate', 'key': 'Id_nb',
               'reason': 'True 1.65 period items sold on the period merchant pages (Goods for Sale), built from their item pages (merchant pages pass)',
               'rows': rows}, open(os.path.join(HERE, 'add-goodsitems165.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
    json.dump(adds, open(os.path.join(HERE, 'goods_built_additions.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
    json.dump(report, open(os.path.join(HERE, 'goodsitems_report.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
    print(f'goods entries {len(wanted)}, pages {len(done)}, built {len(rows)}, stock additions {len(adds)}')
    for e in report:
        if e['status'] != 'built':
            print('  skipped', e['page_key'], e['name'], '-', e['reason'])


if __name__ == '__main__':
    main()
