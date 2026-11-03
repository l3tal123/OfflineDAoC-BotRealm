"""Merchant pages pass: period Allakhazam merchant pages ("Goods for Sale") as the stock source for every period merchant.
1. builds world165_post.db = snapshot + the integrated runtime\\botrealm add files + true165-rules.json updates (a scratch copy in this folder)
2. parses Goods for Sale from wayback-allakhazam/period_mobs.jsonl.gz
3. matches merchants, resolves goods to templates, proposes additions against the post-integration lists
Outputs: merchant_goods_pages.json, merchant_goods_additions.json, merchant_goods_unresolved.json, merchant_goods_stats.json"""
import collections, gzip, html, json, os, re, shutil, sqlite3, sys
sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
RT = os.path.join(os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "..")), "runtime", "botrealm")  # found from this script
AR = os.path.join(os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "..")), "development-source", "research", "period-data", "archive")  # found from this script
POST = os.path.join(HERE, 'world165_post.db')

# ---------- 1. post-integration copy ----------
if not os.path.exists(POST) or '--rebuild' in sys.argv:
    src = sqlite3.connect('file:' + os.path.join(HERE, 'world165_snapshot.db').replace(os.sep, '/') + '?mode=ro', uri=True)
    dst = sqlite3.connect(POST)
    src.backup(dst)
    src.close()
    log = collections.Counter()
    for f in ['add-vendoritems165.json', 'add-deaditems165.json', 'add-merchantstock165.json', 'add-merchantlists165.json', 'add-missingmerchantstock165.json', 'add-missingmerchants165.json']:
        d = json.load(open(os.path.join(RT, f), encoding='utf-8'))
        rows = d['rows'] if isinstance(d, dict) else d
        table = d.get('table') if isinstance(d, dict) else None
        cols_ok = {r[1] for r in dst.execute('pragma table_info(%s)' % table)}
        for r in rows:
            r = {k: v for k, v in r.items() if k in cols_ok}
            try:
                dst.execute('INSERT OR REPLACE INTO %s (%s) VALUES (%s)' % (table, ','.join('`%s`' % k for k in r), ','.join('?' * len(r))), list(r.values()))
                log[f] += 1
            except Exception as e:
                log[f + ' ERR ' + str(e)[:60]] += 1
    rules = json.load(open(os.path.join(RT, 'true165-rules.json'), encoding='utf-8'))
    for u in rules['updates']:
        try:
            dst.execute(u['sql'])
            log['updates ok'] += 1
        except Exception as e:
            log['updates ERR ' + str(e)[:50]] += 1
    dst.commit()
    dst.close()
    print('post db built', dict(log))

# ---------- 2. merchant mpages ----------
def t(s):
    return re.sub(r'\s+', ' ', html.unescape(re.sub(r'<[^>]+>', ' ', s))).strip()


mpages = []
for line in gzip.open(os.path.join(AR, 'wayback-allakhazam', 'period_mobs.jsonl.gz'), 'rt', encoding='utf-8', errors='replace'):
    if 'Goods for Sale' not in line:
        continue
    r = json.loads(line)
    b = r.get('body') or ''
    nm = re.search(r'Subject=Correction for (.+?) \(\d+\)', b) or re.search(r'Bestiary: ([^"]+)";', b)
    realm = re.search(r'<b>Realm: </b><span[^>]*>([^<]+)<', b)
    typ = re.search(r'<b>Type: </b>(.*?)</td>', b, re.S)
    desc = re.search(r'<b>Description:</b>(.*?)</td>', b, re.S)
    gs = re.search(r'Goods for Sale:</b>(.*?)</td>', b, re.S)
    hab = re.search(r'Known Habitats</b>:</td></tr><tr><td>(.*?)</td>', b, re.S)
    goods = []
    for li in re.findall(r'<li>(.*?)</li>', gs.group(1) if gs else '', re.S):
        k = re.search(r'citem=(\d+)', li)
        goods.append({'name': t(li), 'page_key': int(k.group(1)) if k else None})
    mpages.append({'mob_key': r.get('key'), 'url': r.get('url'), 'capture': r.get('timestamp'), 'name': t(nm.group(1)) if nm else None,
                  'realm': realm.group(1).strip() if realm else None, 'type': t(typ.group(1)) if typ else '', 'description': t(desc.group(1)) if desc else '',
                  'habitats': [t(x) for x in re.findall(r'<li>(.*?)</li>', hab.group(1), re.S)] if hab else [], 'goods': goods})
json.dump(mpages, open(os.path.join(HERE, 'merchant_goods_pages.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)

# ---------- 3. world (post) via merchant_match.py ----------
code = open(os.path.join(HERE, 'merchant_match.py'), encoding='utf-8').read()
code = code.replace("os.path.join(HERE, 'world165_snapshot.db')", "os.path.join(HERE, 'world165_post.db')")
code = code[:code.index('json.dump(matching,')]   # don't overwrite pass files
exec(code)
src2 = open(os.path.join(HERE, 'merchant_pass2.py'), encoding='utf-8').read()
exec(src2[src2.index('def seal_of_prices'):src2.index('SEALCLASS =')])
mp = open(os.path.join(HERE, 'merchant_parse.py'), encoding='utf-8').read()
exec(mp[mp.index('def levels'):mp.index('def parse')])
itempage = {}
for line in open(os.path.join(HERE, 'period_item_pages.jsonl'), encoding='utf-8'):
    d = json.loads(line)
    lou, att = levels(d['raw'])
    prices = re.findall(r'(?:Purchased|Purchase) for [^|]+', d['raw'])
    itempage[d['key']] = {'key': d['key'], 'name': d['name'], 'realm': d['realm'], 'af': stat(d['raw'], 'Armor Factor'), 'dps': stat(d['raw'], 'DPS'),
                          'delay': stat(d['raw'], 'Delay'), 'prices': prices, 'levels_of_use': lou, 'merchants': [], 'found_in': d['found_in'], 'capture': d['capture']}
page_by_name = collections.defaultdict(list)
for v in itempage.values():
    page_by_name[(item_norm(v['name']), RNUM.get(v['realm'], 0))].append(v)

# match merchants
used = collections.defaultdict(set)
for lid, rows in lists.items():
    for r in rows:
        used[lid].add((r['PageNumber'], r['SlotPosition']))
results, additions, unresolved = [], [], []
stats = collections.Counter()
for mpg in mpages:
    if not mpg['goods'] or not mpg['name']:
        continue
    stats['merchant pages with goods'] += 1
    realm = mpg['realm'] if mpg['realm'] in ('Albion', 'Midgard', 'Hibernia') else None
    nn = norm(re.sub(r'\s*\((?:NPC|DF)\)$', '', mpg['name']))
    cands, how = candidates(nn, realm)
    gn = {item_norm(g['name']) for g in mpg['goods']}
    bylist = collections.defaultdict(list)
    for m, k in cands:
        if m['list']:
            bylist[m['list']].append(m)
    chosen, note = None, ''
    if len(bylist) == 1:
        chosen = next(iter(bylist))
    elif len(bylist) > 1:
        ov = sorted(((len(gn & list_names.get(l, set())), l) for l in bylist), reverse=True)
        if ov[0][0] > 0 and ov[0][0] > ov[1][0]:
            chosen, note = ov[0][1], 'several same-name NPCs; list with most goods overlap'
        else:
            note = 'AMBIGUOUS: %d lists' % len(bylist)
    if chosen and how != 'exact_or_case' and not (gn & list_names.get(chosen, set())):
        note += '; UNCONFIRMED non-exact name with no goods on the list'
        chosen = None
    rec = {'period_name': mpg['name'], 'realm': mpg['realm'], 'type': mpg['type'], 'habitats': mpg['habitats'], 'capture': mpg['capture'], 'goods': len(mpg['goods']),
           'match': how, 'world_npcs': sorted({(m['Name'], m['zone'], m['list']) for m, _ in cands}), 'list_id': orig_list.get(chosen, chosen), 'note': note}
    results.append(rec)
    if not chosen:
        stats['merchants not matched'] += 1
        continue
    stats['merchants matched'] += 1
    mrealm = RNUM.get(realm) if realm else None
    seen = set()
    for g in mpg['goods']:
        stats['goods items'] += 1
        inm = item_norm(g['name'])
        ip = itempage.get(g['page_key']) if g['page_key'] else None
        if not ip:
            pp = page_by_name.get((inm, mrealm or 0)) or page_by_name.get((inm, 0))
            ip = pp[0] if pp else None
        if ip and item_norm(ip['name']) != inm:
            inm2 = item_norm(ip['name'])
        else:
            inm2 = inm
        if inm in list_names.get(chosen, set()) or inm2 in list_names.get(chosen, set()):
            stats['already stocked'] += 1
            rec.setdefault('stocked', 0)
            rec['stocked'] = rec['stocked'] + 1
            continue
        p = ip or {'key': None, 'name': g['name'], 'realm': mpg['realm'], 'af': None, 'dps': None, 'delay': None, 'prices': [], 'levels_of_use': None, 'merchants': []}
        nears = near_names(inm2, list_names.get(chosen, set()))
        conf_names = [(n, tt) for n in nears for tt in list_tmpl_by_name[chosen][n] if stats_agree(tt, p)]
        if conf_names:
            stats['stocked under another name (stats agree)'] += 1
            continue
        want = {mrealm, 0} if mrealm else {1, 2, 3, 0}
        if p['realm'] in ('Albion', 'Midgard', 'Hibernia'):
            want &= {RNUM[p['realm']], 0}
        tc = [x for n_ in {inm, inm2} for x in by_name.get(n_, []) if x['Realm'] in want]
        tnote = ''
        if not tc:
            pool = sorted({n for rr in want for n in all_names_by_realm.get(rr, [])})
            for cn in difflib.get_close_matches(inm2, pool, n=3, cutoff=0.9):
                ok = [x for x in by_name[cn] if x['Realm'] in want]
                if ok:
                    tc, tnote = ok, 'template name differs (spelling): "%s"' % ok[0]['Name']
                    break
        if not tc:
            k = p.get('key')
            unresolved.append({'merchant_period_name': mpg['name'], 'realm': mpg['realm'], 'list_id': orig_list.get(chosen, chosen), 'item_name': g['name'],
                               'page_key': k, 'status': 'buildable, not built (item page exists)' if k else 'no page'})
            stats['unresolvable: ' + ('buildable' if k else 'no page')] += 1
            continue
        sc = sorted(((stat_score(x, p)[0], x['Realm'] == mrealm, in_lists[x['Id_nb'].lower()], x['Id_nb']) for x in tc), reverse=True)
        tid = sc[0][3]
        if tid.lower() in seen:
            continue
        seen.add(tid.lower())
        tt = tmpl[tid.lower()]
        sa = stats_agree(tt, p)
        why = ['period merchant page %s (%s) lists "%s"' % (mpg['url'].split('=')[-1], mpg['capture'][:8], g['name'])]
        conf = 'high' if how == 'exact_or_case' else 'medium'
        if sa is True:
            why.append('page stats/price match the template')
        elif sa is False:
            why.append('page stats differ from the template'); conf = 'low'
        if tnote:
            why.append(tnote); conf = 'medium' if conf == 'high' else conf
        if len(sc) > 1 and sc[1][:3] == sc[0][:3]:
            why.append('tie with %s' % sc[1][3]); conf = 'medium' if conf == 'high' else conf
        if nears:
            why.append('list has a similar name: ' + ', '.join('"%s"' % n for n in nears[:3]))
            if sa is not True:
                conf = 'low'
        if note:
            why.append(note); conf = 'medium' if conf == 'high' else conf
        free = [(a, b) for a in range(5) for b in range(30) if (a, b) not in used[chosen]]
        pgn, sl = free[0] if free else (None, None)
        if free:
            used[chosen].add((pgn, sl))
        else:
            why.append('LIST FULL (150 slots): no free page/slot')
        stats['additions ' + conf] += 1
        additions.append({'page_key': p.get('key'), 'item_name': g['name'], 'item_realm': p['realm'], 'merchant_period_name': mpg['name'], 'merchant_realm': mpg['realm'],
                          'list_id': orig_list.get(chosen, chosen), 'item_template_id': tid, 'confidence': conf, 'action': 'add_row', 'dead_row_template_id': None,
                          'why': '; '.join(why), 'page_capture': mpg['capture'], 'page_prices': p['prices'], 'suggested_page': pgn, 'suggested_slot': sl,
                          'list_rows_now': len(lists.get(chosen, [])), 'list_used_by_npcs': sorted(list_users.get(chosen, set())), 'source': 'merchant page'})

# lists whose stock looks different from the period goods
diff = []
for rec in results:
    l = (rec['list_id'] or '').lower()
    if not l:
        continue
    mpg = next(p for p in mpages if p['name'] == rec['period_name'] and p['capture'] == rec['capture'])
    gn = {item_norm(g['name']) for g in mpg['goods']}
    names = [tmpl[r['ItemTemplateID'].lower()]['Name'] for r in lists.get(l, []) if r['ItemTemplateID'].lower() in tmpl]
    not_goods = [n for n in names if item_norm(n) not in gn and strip_q(item_norm(n)) not in {strip_q(x) for x in gn}]
    if len(names) >= 10 and len(not_goods) / len(names) > 0.5:
        diff.append({'merchant': rec['period_name'], 'realm': rec['realm'], 'list_id': rec['list_id'], 'list_rows': len(names), 'rows_not_in_period_goods': len(not_goods),
                     'period_goods': rec['goods'], 'list_shared_with': sorted(list_users.get(l, set())), 'examples': not_goods[:8]})
over = collections.Counter(a['list_id'] for a in additions if a['suggested_slot'] is None)
stats['lists that would overflow'] = len(over)
json.dump(additions, open(os.path.join(HERE, 'merchant_goods_additions.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
json.dump({'unresolved': unresolved, 'merchants': results, 'different_stock': diff, 'overflow': over}, open(os.path.join(HERE, 'merchant_goods_unresolved.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
json.dump(dict(stats), open(os.path.join(HERE, 'merchant_goods_stats.json'), 'w', encoding='utf-8'), indent=1)
print(json.dumps(dict(stats), indent=1))
print('not matched:', [(r['period_name'], r['realm'], r['note'][:60]) for r in results if not r['list_id']])
print('overflow:', dict(over))
print('different stock:', [(d['merchant'], d['list_id'], d['list_rows'], d['rows_not_in_period_goods'], d['period_goods']) for d in diff])
