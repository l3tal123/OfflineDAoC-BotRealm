"""Pass 2: DF seal-merchant swaps (dfseal_swaps.json) and new lists (new_lists.json).
Runs merchant_match.py first (same globals), read-only on the snapshot."""
import os
HERE = os.path.dirname(os.path.abspath(__file__))
exec(open(os.path.join(HERE, 'merchant_match.py'), encoding='utf-8').read())

# ---------- built period items (other agent) ----------
built_rows = json.load(open(os.path.join(HERE, 'add-vendoritems165.json'), encoding='utf-8'))['rows']
vrep = {x['page_key']: x for x in json.load(open(os.path.join(HERE, 'vendor_items_report.json'), encoding='utf-8'))}
built = {b['Id_nb'].lower(): b for b in built_rows}
built_by = collections.defaultdict(list)
for b in built_rows:
    built_by[(item_norm(b['Name']), b['Realm'])].append(b)
pages_by = collections.defaultdict(list)   # (norm name, realm num) -> pages
for p in P:
    pages_by[(item_norm(p['name']), RNUM.get(p['realm'], 0))].append(p)
allpages = [json.loads(l) for l in open(os.path.join(HERE, 'period_item_pages.jsonl'), encoding='utf-8')]
anypage_by = collections.defaultdict(list)
for p in allpages:
    anypage_by[(item_norm(p['name']), RNUM.get(p['realm'], 0))].append(p)


def seal_of_prices(prices):
    for x in prices:
        mm = re.search(r'(Diamond|Emerald|Sapphire)\s+Seals', x, re.I)
        if mm:
            return mm.group(1).capitalize()
    return None


SEALCLASS = {'Diamond': 'DOL.GS.GameDiamondSealsMerchant', 'Emerald': 'DOL.GS.GameEmeraldSealsMerchant', 'Sapphire': 'DOL.GS.GameSapphireSealsMerchant'}
seal_mobs = [m for m in mobs if 'SealsMerchant' in (m['ClassType'] or '')]
seal_lists = collections.defaultdict(list)
for m in seal_mobs:
    seal_lists[m['list']].append(m)

swaps, no_counterpart, dead_seal_rows, plain_rows = [], [], [], []
for lid, ms in seal_lists.items():
    seal = re.search(r'Game(\w+?)SealsMerchant', ms[0]['ClassType']).group(1)
    names = sorted({m['Name'] for m in ms})
    on_list = {r['ItemTemplateID'].lower() for r in lists.get(lid, [])}
    for r in sorted(lists.get(lid, []), key=lambda r: (r['PageNumber'], r['SlotPosition'])):
        t = tmpl.get(r['ItemTemplateID'].lower())
        base_rec = {'list_id': orig_list.get(lid, lid), 'merchants': names, 'merchant_seal': seal, 'page': r['PageNumber'], 'slot': r['SlotPosition'], 'old_template_id': r['ItemTemplateID']}
        if not t:
            dead_seal_rows.append(base_rec)
            continue
        n = item_norm(t['Name'])
        base = strip_q(n)
        if base == n:
            plain_rows.append(dict(base_rec, name=t['Name'], period_page=bool(anypage_by.get((n, t['Realm'])))))
            continue
        mr = ms[0]['eff_realm']
        rr = [mr] + ([t['Realm']] if t['Realm'] not in (mr, 0) else [])
        cands = [('built', b['Id_nb'], int(b['Id_nb'].split('_')[-1])) for r_ in rr for b in built_by.get((base, r_), [])]
        if not cands:
            ex = [x for x in by_name.get(base, []) if x['Realm'] in (mr, 0) and not x['Id_nb'].lower().startswith('p165_item_')]
            for x in ex:
                pk = [p['key'] for p in anypage_by.get((base, mr), [])]
                cands.append(('existing', x['Id_nb'], pk[0] if pk else None))
        prs = [p for r_ in rr for p in anypage_by.get((base, r_), [])]
        if t['Realm'] != mr:
            base_rec['template_realm_differs'] = 'template Realm %s, merchant realm %s' % (t['Realm'], mr)
        if not cands:
            no_counterpart.append(dict(base_rec, name=t['Name'], period_pages=[p['key'] for p in prs],
                                       note='period page exists but no item was built (see vendor_items_report skip_reason)' if prs else 'no period page with the plain name: possibly a post-1.65 item',
                                       skip_reason=[vrep.get(p['key'], {}).get('skip_reason') for p in prs][:3]))
            continue
        kind, new_id, pk = cands[0]
        page = next((p for p in allpages if p['key'] == pk), None)
        pseal = seal_of_prices(page['prices'] if page else []) or seal_of_prices(page.get('found_in', []) if page else [])
        if not pseal and kind == 'built':
            pc = (vrep.get(pk) or {}).get('price_currency') or ''
            pseal = pc.split()[0] if 'Seals' in pc else None
        currency_ok = None if not pseal else (pseal == seal)
        named = page and any(norm(m) == norm(nm) for m in [x['name'] for pp in P if pp['key'] == pk for x in pp['merchants']] for nm in names)
        conf = 'high' if currency_ok and (len(cands) == 1) else ('medium' if currency_ok is None or len(cands) > 1 else 'low')
        why = ['%s period item "%s"' % (kind, base)]
        if named:
            why.append('the period page names this merchant')
        if currency_ok is False:
            why.append('CURRENCY MISMATCH: period price in %s Seals, merchant takes %s' % (pseal, seal))
        if len(cands) > 1:
            why.append('several counterparts: ' + ', '.join(c[1] for c in cands[:4]))
        if new_id.lower() in on_list:
            why.append('the period item is already on this list too (old row would be a duplicate once swapped; consider leaving it)')
            conf = 'low'
        swaps.append(dict(base_rec, old_name=t['Name'], new_template_id=new_id, page_key=pk, currency_ok=currency_ok, period_seal=pseal, confidence=conf, why='; '.join(why)))

# low-confidence additions vs swaps
A2 = additions
swap_new = {(s['list_id'].lower(), s['new_template_id'].lower()) for s in swaps}
swap_names = {(s['list_id'].lower(), strip_q(item_norm(s['old_name']))) for s in swaps}
low_map = []
for a in A2:
    if a['confidence'] != 'low':
        continue
    k1 = (a['list_id'].lower(), a['item_template_id'].lower())
    k2 = (a['list_id'].lower(), item_norm(a['item_name']))
    if k1 in swap_new or k2 in swap_names:
        st = 'covered by a DF seal swap (do the swap instead of adding a row)'
    elif a['list_id'].lower() in seal_lists:
        st = 'DF seal list, no swap covers it: stays a low addition'
    else:
        st = 'not a DF question'
    low_map.append({'page_key': a['page_key'], 'item_name': a['item_name'], 'list_id': a['list_id'], 'item_template_id': a['item_template_id'], 'status': st})

json.dump({'swaps': swaps, 'no_period_counterpart': no_counterpart, 'plain_named_rows': plain_rows, 'dead_rows_on_seal_lists': dead_seal_rows, 'low_additions_vs_swaps': low_map},
          open(os.path.join(HERE, 'dfseal_swaps.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)

# ---------- C: new lists ----------
TARGETS = [('signy', 'Midgard', None), ('holsvi hallgrim', 'Midgard', None), ('brach leof', 'Albion', None), ('chelseigh stillman', 'Albion', 'chelseigh stilman'),
           ('gremain watford', 'Albion', None), ('shayly parke', 'Albion', None), ('caley', 'Hibernia', None), ('eluned', 'Hibernia', None)]


def resolve_template(p, mrealm, own_list):
    inm = item_norm(p['name'])
    want = {mrealm, 0}
    if p['realm'] in ('Albion', 'Midgard', 'Hibernia'):
        want &= {RNUM[p['realm']], 0}
    co = collections.Counter()
    for m in p['merchants']:
        k = resolve_key(m, p)
        if k:
            for t in list_tmpl_by_name[choice[k]].get(inm, []):
                co[t['Id_nb']] += 1
    if own_list:
        for t in list_tmpl_by_name[own_list].get(inm, []):
            co[t['Id_nb']] += 2
    cands = [t for t in by_name.get(inm, []) if t['Realm'] in want]
    note = ''
    if not cands:
        pool = sorted({n for rr in want for n in all_names_by_realm.get(rr, [])})
        for cn in difflib.get_close_matches(inm, pool, n=3, cutoff=0.9):
            ok = [t for t in by_name[cn] if t['Realm'] in want]
            if ok:
                cands, note = ok, 'template name differs (spelling): "%s"' % ok[0]['Name']
                break
    if cands:
        sc = sorted(((stat_score(t, p)[0] + 3 * bool(co.get(t['Id_nb'])), t['Realm'] == mrealm, in_lists[t['Id_nb'].lower()], t['Id_nb']) for t in cands), reverse=True)
        conf = 'high' if (co.get(sc[0][3]) or stats_agree(tmpl[sc[0][3].lower()], p)) and not note else 'medium'
        if len(sc) > 1 and sc[1][:3] == sc[0][:3]:
            conf = 'medium'
        return sc[0][3], conf, note
    if own_list:   # same item on the NPC's current list under another name, page numbers equal
        for n in near_names(inm, list_names.get(own_list, set())):
            for t in list_tmpl_by_name[own_list][n]:
                if stats_agree(t, p):
                    return t['Id_nb'], 'medium', 'period name differs from template "%s" (stats agree)' % t['Name']
    pid = 'p165_item_%d' % p['key']
    return pid, ('high' if pid in built else 'low'), ('built period item' if pid in built else 'NOT BUILT: planned id, the item does not exist yet')


new_lists = []
for nn, realm, world_nn in TARGETS:
    grp = groups.get((nn, realm), [])
    rec = next((r for r in matching if norm(r['period_name']) == nn and r['realm'] == realm), None)
    own = choice.get((nn, realm))
    rn = RNUM[realm]
    npcs = [m for m in mobs if norm(m['Name']) == (world_nn or nn) and m['eff_realm'] == rn]
    rows, seen_t = [], set()
    for p, m, _ in sorted(grp, key=lambda x: x[0]['key']):
        tid, conf, note = resolve_template(p, rn, own)
        if tid.lower() in seen_t:
            continue
        seen_t.add(tid.lower())
        rows.append({'template_id': tid, 'page_key': p['key'], 'item_name': p['name'], 'confidence': conf, 'note': note, 'prices': p['prices']})
    for i, r in enumerate(rows):
        r['page'], r['slot'] = (i // 30, i % 30) if i < 150 else (None, None)
    seal = {seal_of_prices(r['prices']) for r in rows} - {None}
    target_class = SEALCLASS[seal.pop()] if len(seal) == 1 else 'DOL.GS.GameMerchant'
    new_lists.append({
        'new_list_id': 'p165list_' + (rec['period_name'] if rec else nn).replace(' ', '_'),
        'period_name': rec['period_name'] if rec else nn, 'realm': realm,
        'npc': [{'name': m['Name'], 'mob_id': m['Mob_ID'], 'region': m['Region'], 'zone': m['zone'], 'x': m['X'], 'y': m['Y'],
                 'current_class': m['ClassType'], 'current_guild': m['Guild'], 'current_list': orig_list.get(m['list'], m['list']) or None} for m in npcs],
        'target_class': target_class,
        'change': ('point the NPC at the new list (old list stays for the NPCs that share it: %s)' % ', '.join(sorted(list_users.get(own, set()) - {m['Name'] for m in npcs})) if own
                   else 'turn the NPC into a merchant: set ClassType to target_class, ItemsListTemplateID to new_list_id, and Guild (period guild title not in the pages: owner/coordinator to choose)'),
        'rows_total': len(rows), 'rows_over_150': sum(1 for r in rows if r['page'] is None),
        'rows_by_confidence': dict(collections.Counter(r['confidence'] for r in rows)),
        'rows': rows,
    })
json.dump(new_lists, open(os.path.join(HERE, 'new_lists.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)

print('SWAPS', len(swaps), collections.Counter(s['confidence'] for s in swaps), 'currency', collections.Counter(s['currency_ok'] for s in swaps),
      'kinds', collections.Counter(s['why'].split()[0] for s in swaps))
print('NO COUNTERPART', len(no_counterpart), collections.Counter(x['note'][:30] for x in no_counterpart), 'PLAIN', len(plain_rows), 'DEAD', len(dead_seal_rows))
print('LOW MAP', collections.Counter(x['status'][:40] for x in low_map))
for nl in new_lists:
    print('NEWLIST', nl['new_list_id'], 'npcs', [(n['name'], n['current_class'], n['current_guild'], n['current_list']) for n in nl['npc']], nl['rows_total'], nl['rows_by_confidence'], 'over150', nl['rows_over_150'], nl['target_class'])
