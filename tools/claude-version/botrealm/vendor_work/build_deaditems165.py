"""Vendor pass 2: ItemTemplate rows for MerchantItem rows that point at ids missing from ItemTemplate.
Id_nb = the dead id itself (exact case), stats from the matching period Allakhazam page, same encodings and sibling
rules as build_vendoritems165.py. Read-only on the snapshot; writes only into this folder.

    python -I build_deaditems165.py -> add-deaditems165.json, deaditems_report.json"""
import collections, datetime, difflib, json, os, re, sqlite3, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import pageparse  # noqa: E402
import build_vendoritems165 as bv  # noqa: E402

LIST_ARMOR = {'leather': 33, 'cloth': 32, 'studded': 34, 'chain': 35, 'plate': 36, 'reinforced': 37, 'scale': 38}


def id_name(i):
    s = re.sub(r'\d+$', '', i)
    return bv.norm(s.replace('_', ' '))


def main():
    bv.W = W = bv.World()
    now = datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')
    con = sqlite3.connect(f'file:{bv.DB}?mode=ro', uri=True)
    exist = {r[0].lower() for r in con.execute('select Id_nb from ItemTemplate')}
    dead = collections.defaultdict(lambda: {'lists': set(), 'rows': 0, 'used_rows': 0, 'realms': set()})
    list_realm = collections.defaultdict(set)
    for lid, realm in con.execute('select ItemsListTemplateID, Realm from Mob where ItemsListTemplateID is not null'):
        list_realm[lid.lower()].add(realm)
    used_lists = {l for l in list_realm}
    for lid, iid in con.execute('select ItemListID, ItemTemplateID from MerchantItem'):
        if iid.lower() in exist:
            continue
        d = dead[iid]
        d['lists'].add(lid)
        d['rows'] += 1
        if (lid or '').lower() in used_lists:
            d['used_rows'] += 1
        rs = set(list_realm.get((lid or '').lower(), set())) - {0}
        m = re.match(r'^(Alb|Mid|Hib)', lid or '')
        if m:
            rs.add({'Alb': 1, 'Mid': 2, 'Hib': 3}[m.group(1)])
        d['realms'] |= rs
    con.close()

    pages = [json.loads(l) for l in open(bv.PAGES, encoding='utf-8')]
    allpages = {r['key']: r for r in pages}
    parsed = {r['key']: pageparse.parse(r) for r in pages}
    byname = collections.defaultdict(list)
    for r in pages:
        byname[bv.norm(bv.strip_suffix(r['name']))].append(r)
    names = list(byname)

    report, rows = [], []
    for iid in sorted(dead, key=str.lower):
        d = dead[iid]
        nm = id_name(iid)
        ent = {'dead_id': iid, 'lists': sorted(d['lists']), 'merchant_rows': d['rows'], 'used_list_rows': d['used_rows'],
               'list_realms': sorted(d['realms']), 'page_key': None, 'status': 'skipped', 'reason': None, 'derived': {}}
        report.append(ent)
        same = [r for r in W.byname.get(nm, []) if not d['realms'] or r['Realm'] in d['realms'] | {0}]
        if same:
            ent['existing_same_name'] = same[0]['Id_nb']
        armor_hint = next((v for k, v in LIST_ARMOR.items() if any(k in l.lower() for l in d['lists'])), None)

        def ok(r):
            if d['realms'] and bv.REALM.get(r['realm'], 0) not in d['realms'] | {0}:
                return False
            if armor_hint:
                st = bv.page_stats(parsed[r['key']])
                if st['armor'] and st['armor'] != armor_hint:
                    return False
            return True
        cands = [r for r in byname.get(nm, []) if ok(r)]
        how = 'same name'
        if not cands:
            last = nm.split()[-1] if nm else ''
            def wordwise(n):     # same words, each spelt nearly alike (never another tier: alloy/fine alloy, ironwood/runewood)
                a, b = nm.split(), n.split()
                return len(a) == len(b) and all(difflib.SequenceMatcher(None, x, y).ratio() >= 0.8 for x, y in zip(a, b))
            close = [n for n in difflib.get_close_matches(nm, names, n=5, cutoff=0.88) if n.split()[-1] == last and wordwise(n)]
            for n in close:
                cands = [r for r in byname[n] if ok(r)]
                if cands:
                    how = f'spelling variant "{n}"'
                    break
        if not cands:
            ent['reason'] = 'no period page for this item' + (f' (our world has it as {ent["existing_same_name"]}: repoint the row)'
                                                              if same else '')
            continue
        cands.sort(key=lambda r: (not r['merchants'], -len(parsed[r['key']]['bonuses']), -len(r['raw'])))
        rec = cands[0]
        p = parsed[rec['key']]
        ent['page_key'] = rec['key']
        ent['page_name'] = rec['name']
        ent['match'] = how
        st = bv.page_stats(p)
        st['left'] = p['left_hand_usable']
        if st['item_type'] == 40 or st['item_type'] is None:
            ent['reason'] = ('inventory / crafting item: the page gives no object type, model or spell'
                             + (f'; our world has it as {ent["existing_same_name"]}: repoint the row' if same else ''))
            continue
        bon, unknown = bv.map_bonuses(p)
        sname = bv.strip_suffix(rec['name'])
        if same:
            sname = same[0]['Name']     # keep our world's spelling of the name the merchant list uses
        tmp = {'derived': {}, 'price_source': 'none'}
        row, why = bv.build(rec, p, st, bon, unknown, tmp, sname, parsed, allpages, now)
        if row is None:
            ent['reason'] = why
            continue
        row['Id_nb'] = row['ItemTemplate_ID'] = iid
        if tmp['price_source'] == 'none' and same:
            s0 = min(same, key=lambda r: (r['DPS_AF'] != row['DPS_AF'], r['Level'] != row['Level'], r['Id_nb'].lower() not in W.sold))
            row['Price'] = s0['Price']
            tmp['price_source'] = 'sibling'
            tmp['price_currency'] = 'coin'
            tmp['derived']['Price'] = f'from {s0["Id_nb"]} (the same item in our world; the page gives no price)'
        rows.append(row)
        ent['status'] = 'built'
        ent['reason'] = None
        ent['derived'] = tmp['derived']
        ent['derived']['Name'] = 'our world\'s spelling (same-name item ' + same[0]['Id_nb'] + ')' if same else tmp['derived'].get('Name')
        for k in ('price_source', 'price_currency', 'sibling', 'ability_not_built', 'bonus_lines_not_built'):
            if k in tmp:
                ent[k] = tmp[k]
    json.dump({'table': 'ItemTemplate', 'key': 'Id_nb',
               'reason': 'True 1.65 period items for merchant rows that pointed at missing items (period Allakhazam pages, vendor pass 2)',
               'rows': rows}, open(os.path.join(HERE, 'add-deaditems165.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
    json.dump(report, open(os.path.join(HERE, 'deaditems_report.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
    b = [e for e in report if e['status'] == 'built']
    print('dead ids', len(report), 'matched', sum(1 for e in report if e['page_key']), 'built', len(b),
          'rows', sum(e['merchant_rows'] for e in b), 'used rows', sum(e['used_list_rows'] for e in b),
          'of', sum(e['used_list_rows'] for e in report))


if __name__ == '__main__':
    main()
