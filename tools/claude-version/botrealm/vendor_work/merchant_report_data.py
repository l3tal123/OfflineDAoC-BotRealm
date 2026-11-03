"""Step 3: numbers for merchant_report.md (read-only)."""
import collections, json, os, re, sqlite3, sys
sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
sys.argv = [sys.argv[0]]
M = json.load(open(os.path.join(HERE, 'merchant_matching.json'), encoding='utf-8'))
A = json.load(open(os.path.join(HERE, 'merchant_stock_additions.json'), encoding='utf-8'))
O = json.load(open(os.path.join(HERE, 'merchant_present_other_name.json'), encoding='utf-8'))
pages = [json.loads(l) for l in open(os.path.join(HERE, 'period_item_pages.jsonl'), encoding='utf-8')]
def item_norm(s):
    s = re.sub(r'\s*\((?:alb|mid|hib|albion|midgard|hibernia|old|new|all|item|dye|enamel|\d+)\)\s*$', '', s or '', flags=re.I)
    s = s.lower().replace("'", '')
    s = re.sub(r'[.\-_,]', ' ', s)
    return re.sub(r'\s+', ' ', s).strip()
period_names = {item_norm(p['name']) for p in pages}
db = sqlite3.connect('file:' + os.path.join(HERE, 'world165_snapshot.db').replace(os.sep, '/') + '?mode=ro', uri=True)
tn = {r[0].lower(): r[1] for r in db.execute('select Id_nb,Name from ItemTemplate')}
rows = collections.defaultdict(list)
for l, t in db.execute('select ItemListID,ItemTemplateID from MerchantItem'):
    rows[l.lower()].append(t.lower())
print('== kinds', collections.Counter(r['match_kind'] for r in M))
for k in ['exact', 'spelling', 'none']:
    print(k, [r['period_name'] + '/' + str(r['realm']) for r in M if r['match_kind'] == k][:400] if k != 'exact' else '')
print('== spelling detail')
for r in M:
    if r['match_kind'] == 'spelling':
        print(' ', r['period_name'], '->', sorted({x['mob_name'] for x in r['matched']}), r['notes'][:120])
print('== none detail')
for r in M:
    if r['match_kind'] == 'none':
        print(' ', r['period_name'], r['realm'], r['pages'], '| items:', r['period_items_sample'][:4], '| found:', [f for f in r['period_found_in'] if len(f) < 30][:6], '|', r['notes'][:220], r.get('near_names_any_realm', '')[:1] if r.get('near_names_any_realm') else '')
print('== no list / unconfirmed')
for r in M:
    if r['match_kind'] != 'none' and not r.get('chosen_list'):
        print(' ', r['period_name'], r['notes'][:150])
print('== chosen lists: zero overlap with period items, and share of list items that appear on ANY period page')
seen = set()
for r in M:
    l = r.get('chosen_list')
    if not l or l in seen:
        continue
    seen.add(l)
    names = [tn.get(t) for t in rows.get(l, [])]
    known = sum(1 for n in names if n and item_norm(n) in period_names)
    dead = sum(1 for n in names if n is None)
    ov = r.get('chosen_list_overlap', '')
    if len(names) and (known / len(names) < 0.5 or ov.startswith('0 of')):
        print('  %-40s rows=%3d on_period_pages=%3d dead=%3d | %s | %s' % (l, len(names), known, dead, r['period_name'], ov))
print('== additions', len(A), collections.Counter(a['confidence'] for a in A), collections.Counter(a['action'] for a in A))
print('   planned', sum(1 for a in A if a['item_template_id'].startswith('p165_item_')), 'no slot', sum(1 for a in A if a['action'] == 'add_row' and a['suggested_slot'] is None))
print('   by list (top):', collections.Counter(a['list_id'] for a in A).most_common(12))
print('   lists touched', len({a['list_id'].lower() for a in A}), 'shared lists touched', len({a['list_id'].lower() for a in A if len(a['list_used_by_npcs']) > 1}))
print('== present under other name', len(O), collections.Counter(re.sub(r'\s.*', '', o['world_name'].split()[0]) for o in O).most_common(8))
print('   examples', [(o['period_name'], o['world_name']) for o in O[:3]], [(o['period_name'], o['world_name']) for o in O if not o['world_name'].lower().startswith(('superior', 'exquisite', 'exceptional'))][:12])
# dead MerchantItem rows (ItemTemplateID with no ItemTemplate, case-insensitive)
users = collections.defaultdict(set)
for n, l in db.execute("select Name, ItemsListTemplateID from Mob where ItemsListTemplateID<>''"):
    users[l.lower()].add(n)
orig = {}
for l, t in db.execute('select ItemListID,ItemTemplateID from MerchantItem'):
    orig.setdefault(l.lower(), l)
dead = []
for l, ts in rows.items():
    d = [t for t in ts if t not in tn]
    if d:
        dead.append({'list_id': orig[l], 'dead_rows': len(d), 'list_rows': len(ts), 'used_by_npcs': sorted(users.get(l, set())), 'dead_template_ids': sorted(set(d))})
dead.sort(key=lambda x: (-bool(x['used_by_npcs']), -x['dead_rows']))
json.dump(dead, open(os.path.join(HERE, 'merchant_dead_rows.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
used_l = [x for x in dead if x['used_by_npcs']]
print('== dead rows', sum(x['dead_rows'] for x in dead), 'lists', len(dead), '| on NPC-used lists:', sum(x['dead_rows'] for x in used_l), 'in', len(used_l), 'lists; distinct ids', len({t for x in dead for t in x['dead_template_ids']}))
kinds = collections.Counter()
for x in dead:
    for t in x['dead_template_ids']:
        kinds[re.sub(r'\d+$', '', t).split('_')[-1]] += 1
print('   id tails', kinds.most_common(20))
for x in used_l[:30]:
    print('   %-38s %3d/%3d %s | %s' % (x['list_id'], x['dead_rows'], x['list_rows'], ','.join(x['used_by_npcs'][:3]), ', '.join(x['dead_template_ids'][:4])))
