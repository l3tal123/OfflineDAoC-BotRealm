"""Step 2: match period merchant names to world NPCs and propose missing stock.
Reads period_merchant_pages.json (merchant_parse.py) + world165_snapshot.db (read-only).
Writes merchant_matching.json, merchant_stock_additions.json, merchant_location_entries.json, merchant_stats.json."""
import collections
import difflib
import json
import os
import re
import sqlite3
import sys
import unicodedata

sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
db = sqlite3.connect('file:' + os.path.join(HERE, 'world165_snapshot.db').replace('\\', '/') + '?mode=ro', uri=True)
db.row_factory = sqlite3.Row
RNUM = {'Albion': 1, 'Midgard': 2, 'Hibernia': 3, 'All': 0}
RNAME = {1: 'Albion', 2: 'Midgard', 3: 'Hibernia', 0: 'none'}


def norm(s):
    s = unicodedata.normalize('NFKD', s or '')
    s = ''.join(ch for ch in s if not unicodedata.combining(ch))
    s = s.lower().replace('’', "'").replace("'", '').replace('`', '')
    s = re.sub(r'[.\-_,]', ' ', s)
    return re.sub(r'\s+', ' ', s).strip()


SUFFIX = re.compile(r'\s*\((?:alb|mid|hib|albion|midgard|hibernia|old|new|all|item|dye|enamel|\d+)\)\s*$', re.I)


def item_norm(s):
    return norm(SUFFIX.sub('', s or ''))


def copper(p):
    """'Purchased for 2g 24s 5c' -> copper, None for seals/other currencies."""
    s = re.sub(r'(?i)^purchased? for\s*', '', p).strip()
    if re.search(r'(?i)seal|token|glass|bounty|orb', s):
        return None
    tot, ok = 0, False
    for n, u in re.findall(r'(\d+)\s*([pgsc])\b', s.lower()):
        tot += int(n) * {'p': 10000000, 'g': 10000, 's': 100, 'c': 1}[u]
        ok = True
    return tot if ok else None


# ---------------- world ----------------
zones = collections.defaultdict(list)
for z in db.execute('select ZoneID,RegionID,Name,OffsetX,OffsetY,Width,Height from Zones'):
    zones[z['RegionID']].append(z)
regname = {r['RegionID']: r['Description'] for r in db.execute('select RegionID,Description from Regions')}


def zone_of(region, x, y):
    for z in zones.get(region, []):
        if z['OffsetX'] * 8192 <= x < (z['OffsetX'] + z['Width']) * 8192 and z['OffsetY'] * 8192 <= y < (z['OffsetY'] + z['Height']) * 8192:
            return z['Name']
    return regname.get(region, str(region))


reg_realm_votes = collections.defaultdict(collections.Counter)
for r in db.execute('select Region,Realm,count(*) n from Mob where Realm in (1,2,3) group by 1,2'):
    reg_realm_votes[r['Region']][r['Realm']] += r['n']
reg_realm = {k: v.most_common(1)[0][0] for k, v in reg_realm_votes.items()}

tmpl = {}
by_name = collections.defaultdict(list)
for t in db.execute('select Id_nb,Name,Realm,Level,DPS_AF,SPD_ABS,Price,Object_Type,Item_Type,PackageID from ItemTemplate'):
    tmpl[t['Id_nb'].lower()] = dict(t)
    by_name[item_norm(t['Name'])].append(dict(t))

# NOTE: ItemListID / ItemTemplateID / Id_nb are COLLATE NOCASE in the DB: key everything lower-case
lists = collections.defaultdict(list)
for m in db.execute('select ItemListID,ItemTemplateID,PageNumber,SlotPosition from MerchantItem'):
    lists[m['ItemListID'].lower()].append(dict(m))
list_names = {}
list_dead = collections.defaultdict(dict)   # list -> {name-from-id: dead template id}
list_missing_tmpl = collections.Counter()
for lid, rows in lists.items():
    s = set()
    for r in rows:
        t = tmpl.get(r['ItemTemplateID'].lower())
        if t:
            s.add(item_norm(t['Name']))
        else:
            list_missing_tmpl[lid] += 1
            list_dead[lid][norm(re.sub(r'\d+$', '', r['ItemTemplateID']))] = r['ItemTemplateID']
    list_names[lid] = s
orig_list = {}
for _rows in lists.values():
    for _r in _rows:
        orig_list.setdefault(_r['ItemListID'].lower(), _r['ItemListID'])
in_lists = collections.Counter(m['ItemTemplateID'].lower() for rows in lists.values() for m in rows)

mobs = []
for m in db.execute('select Mob_ID,Name,Guild,ClassType,Region,X,Y,Realm,ItemsListTemplateID,PackageID from Mob'):
    d = dict(m)
    d['list'] = (d['ItemsListTemplateID'] or '').lower()
    d['eff_realm'] = d['Realm'] if d['Realm'] in (1, 2, 3) else reg_realm.get(d['Region'], 0)
    d['zone'] = zone_of(d['Region'], d['X'], d['Y'])
    d['merchantish'] = bool(d['list']) or 'erchant' in (d['ClassType'] or '')
    mobs.append(d)
list_users = collections.defaultdict(set)
for m in mobs:
    if m['list']:
        list_users[m['list']].add(m['Name'])
merch = [m for m in mobs if m['merchantish']]
by_norm = collections.defaultdict(list)
for m in merch:
    by_norm[norm(m['Name'])].append(m)
all_by_norm = collections.defaultdict(list)
for m in mobs:
    all_by_norm[norm(m['Name'])].append(m)

removed = collections.defaultdict(list)
for r in db.execute('select Name,Guild,Region,X,Y,Realm,ItemsListTemplateID,ClassType,RemovalReason from offline_classic165_removed_mobs'):
    removed[norm(r['Name'])].append({'table': 'offline_classic165_removed_mobs', 'name': r['Name'], 'guild': r['Guild'], 'region': r['Region'],
                                     'zone': zone_of(r['Region'], r['X'], r['Y']), 'list_id': r['ItemsListTemplateID'], 'class': r['ClassType'], 'reason': r['RemovalReason']})
for r in db.execute("select RowJson,Reason from offline_true165_removed where SourceTable='Mob'"):
    j = json.loads(r['RowJson'])
    removed[norm(j.get('Name'))].append({'table': 'offline_true165_removed', 'name': j.get('Name'), 'guild': j.get('Guild'), 'region': j.get('Region'),
                                         'zone': zone_of(j.get('Region'), j.get('X') or 0, j.get('Y') or 0), 'list_id': j.get('ItemsListTemplateID'), 'class': j.get('ClassType'), 'reason': r['Reason']})

TITLES = {'dyemaster', 'master', 'gnr', 'sgt', 'sergeant', 'lady', 'lord', 'sir', 'scholar', 'elder', 'captain', 'grand', 'high', 'apprentice', 'young', 'old', 'sister', 'brother', 'father', 'mother', 'trainer', 'guard'}


def strip_title(n):
    toks = n.split()
    while len(toks) > 1 and toks[0] in TITLES:
        toks = toks[1:]
    return ' '.join(toks)


# ---------------- period merchants ----------------
P = json.load(open(os.path.join(HERE, 'period_merchant_pages.json'), encoding='utf-8'))
mentions = collections.defaultdict(list)   # (display name) -> list of (page, mention)
for p in P:
    for m in p['merchants']:
        mentions[m['name']].append((p, m))

# realm per mention: tag, else the item's own realm (unless All)
groups = collections.defaultdict(list)    # (norm name, realm) -> mentions
for name, ms in mentions.items():
    for p, m in ms:
        realm = m['realm_tag'] or (p['realm'] if p['realm'] in ('Albion', 'Midgard', 'Hibernia') else None)
        groups[(norm(name), realm)].append((p, m, name))
# fold realm=None into the single known realm of the same name
for (nn, realm) in list(groups):
    if realm is None:
        others = [k for k in groups if k[0] == nn and k[1] is not None]
        if len(others) == 1:
            groups[others[0]].extend(groups.pop((nn, None)))


def zone_hit(zone, found):
    zn = norm(zone)
    alias = {'city of camelot': 'camelot', 'black mtns south': 'black mountains south', 'black mtns north': 'black mountains north'}
    zn = alias.get(zn, zn)
    for f in found:
        fn = norm(f)
        if fn == zn or (len(fn) > 4 and (fn in zn or zn in fn)):
            return True
    return False


def candidates(nn, realm):
    out = []
    rn = RNUM.get(realm) if realm else None
    def ok(m):
        return rn is None or m['eff_realm'] == rn
    for m in by_norm.get(nn, []):
        if ok(m):
            out.append((m, 'exact' if True else ''))
    if out:
        return out, 'exact_or_case'
    # title-stripped / first-name-only
    st = strip_title(nn)
    for m in merch:
        if not ok(m):
            continue
        wn = norm(m['Name'])
        ws = strip_title(wn)
        if ws == st or (len(nn.split()) == 1 and wn.split()[0] == nn and len(wn.split()) > 1) or \
           (len(wn.split()) == 1 and nn.split()[0] == wn and len(nn.split()) > 1):
            out.append((m, 'title_or_firstname'))
    if out:
        return out, 'title_or_firstname'
    # fuzzy spelling
    for m in merch:
        if not ok(m):
            continue
        wn = norm(m['Name'])
        if wn[:1] != nn[:1] or abs(len(wn) - len(nn)) > 3:
            continue
        r = difflib.SequenceMatcher(None, wn, nn).ratio()
        if r >= 0.84:
            out.append((m, 'fuzzy %.2f' % r))
    return out, ('fuzzy' if out else 'none')


matching = []
choice = {}   # (nn, realm) -> list_id chosen (unambiguous)
for (nn, realm), ms in sorted(groups.items(), key=lambda kv: (kv[0][1] or '', kv[0][0])):
    disp = collections.Counter(x[2] for x in ms).most_common(1)[0][0]
    pages = sorted({p['key'] for p, _, _ in ms})
    items = {item_norm(p['name']) for p, _, _ in ms}
    found = sorted({f for p, _, _ in ms for f in p['found_in']} | {m['where'] for _, m, _ in ms if m['where']})
    cands, how = candidates(nn, realm)
    rec = {'period_name': disp, 'realm': realm, 'pages': len(pages), 'page_keys_sample': pages[:12],
           'period_items_sample': sorted({p['name'] for p, _, _ in ms})[:8], 'period_found_in': found[:12], 'matched': [], 'match_kind': 'none', 'notes': ''}
    bylist = collections.defaultdict(list)
    for m, k in cands:
        bylist[m['list']].append((m, k))
    for lid, mm in bylist.items():
        ov = len(items & list_names.get(lid, set())) if lid else 0
        for m, k in mm:
            rec['matched'].append({'mob_name': m['Name'], 'guild': m['Guild'], 'class': (m['ClassType'] or '').replace('DOL.GS.', ''), 'region': m['Region'],
                                   'region_name': regname.get(m['Region']), 'zone': m['zone'], 'x': m['X'], 'y': m['Y'], 'realm': m['eff_realm'],
                                   'list_id': lid, 'list_size': len(lists.get(lid, [])), 'stock_overlap': ov, 'how': k,
                                   'zone_in_period_found_in': zone_hit(m['zone'], found)})
    notes = []
    chosen = None
    lids = [l for l in bylist if l]
    if not cands:
        rec['match_kind'] = 'none'
        anym = [m for m in all_by_norm.get(nn, [])]
        if anym:
            notes.append('name exists only as non-merchant NPC(s): ' + '; '.join(sorted({'%s <%s> %s r%s' % (m['Name'], m['Guild'], m['zone'], m['eff_realm']) for m in anym}))[:300])
        if nn in removed:
            notes.append('found in removed-mob tables: ' + '; '.join(sorted({'%s <%s> %s list=%s (%s: %s)' % (r['name'], r['guild'], r['zone'], r['list_id'], r['table'], (r['reason'] or '')[:60]) for r in removed[nn]}))[:400])
        # other-realm merchants with this name
        other = [m for m in by_norm.get(nn, [])]
        if other:
            rec['realm_conflict'] = True
            notes.append('REALM CONFLICT: same name exists as merchant in another realm (likely an Allakhazam listing error or a cross-realm item page): ' + '; '.join(sorted({'%s <%s> %s r%s list=%s' % (m['Name'], m['Guild'], m['zone'], m['eff_realm'], m['list']) for m in other})))
        if not anym and not other and nn not in removed:
            hint = sorted({(round(difflib.SequenceMatcher(None, norm(m['Name']), nn).ratio(), 2), m['Name'], m['Guild'] or '', m['zone'], m['eff_realm'], m['list']) for m in mobs
                           if m['Name'][:1].lower() == nn[:1] and abs(len(m['Name']) - len(nn)) <= 4 and difflib.SequenceMatcher(None, norm(m['Name']), nn).ratio() >= 0.75}, reverse=True)[:5]
            if hint:
                rec['near_names_any_realm'] = [{'ratio': h[0], 'mob_name': h[1], 'guild': h[2], 'zone': h[3], 'realm': h[4], 'list_id': h[5]} for h in hint]
                notes.append('no NPC of this name; nearest names listed in near_names_any_realm (not used)')
            else:
                notes.append('TRULY MISSING: no NPC of this (or a close) name anywhere in the world or the removed tables')
    else:
        kinds = {k for _, k in cands}
        if how == 'exact_or_case':
            rec['match_kind'] = 'exact' if any(m['Name'] == disp for m, _ in cands) else 'spelling'
            if rec['match_kind'] == 'spelling':
                notes.append('case/accent/punctuation difference')
        elif how == 'title_or_firstname':
            rec['match_kind'] = 'spelling'
            notes.append('title-stripped or first-name-only match')
        else:
            rec['match_kind'] = 'spelling'
            notes.append('fuzzy spelling match ' + ','.join(sorted(kinds)))
        if not lids:
            notes.append('matched NPC has no merchant list')
        elif len(lids) == 1:
            chosen = lids[0]
        else:
            ovs = sorted(((len(items & list_names.get(l, set())), l) for l in lids), reverse=True)
            zl = [l for l in lids if any(x['zone_in_period_found_in'] for x in rec['matched'] if x['list_id'] == l)]
            if ovs[0][0] > 0 and ovs[0][0] > ovs[1][0]:
                chosen = ovs[0][1]
                notes.append('several NPCs/lists share this name; chose list with most period-item overlap (%d vs %d)' % (ovs[0][0], ovs[1][0]))
            elif len(zl) == 1:
                chosen = zl[0]
                notes.append('several lists; chose the only one whose zone is in the period Found-in')
            else:
                notes.append('AMBIGUOUS: %d lists, overlap %s' % (len(lids), [o for o, _ in ovs]))
        # weak fuzzy/firstname match without any evidence -> don't trust
        if chosen and how != 'exact_or_case':
            best = [x for x in rec['matched'] if x['list_id'] == chosen]
            if not any(x['stock_overlap'] > 0 for x in best):
                notes.append('UNCONFIRMED: non-exact name and the list holds none of the period items; not used for additions')
                rec['unconfirmed'] = True
                chosen = None
        if chosen:
            ov = len(items & list_names.get(chosen, set()))
            rec['chosen_list'] = chosen
            rec['chosen_list_overlap'] = '%d of %d period items already on the list' % (ov, len(items))
            if ov == 0:
                notes.append('chosen list holds NONE of the period items (check guild/list)')
            others = sorted(list_users.get(chosen, set()) - {m['Name'] for m, _ in cands})
            if others:
                notes.append('list shared with other NPCs: ' + ', '.join(others[:8]) + (' ...' if len(others) > 8 else ''))
            choice[(nn, realm)] = chosen
        else:
            rec['chosen_list'] = None
    rec['notes'] = '; '.join(notes)
    if rec['match_kind'] != 'none' and rec.get('chosen_list') is None and 'AMBIGUOUS' in rec['notes']:
        rec['match_kind_detail'] = 'ambiguous'
    matching.append(rec)

# ---------------- stock additions ----------------
def stat_score(t, p):
    """how well a template fits the page stats; returns (score, reasons)"""
    sc, why = 0, []
    pr = [copper(x) for x in p['prices']]
    pr = [x for x in pr if x]
    if pr:
        if t['Price'] in pr:
            sc += 3; why.append('price matches %dc' % t['Price'])
        else:
            why.append('price differs (page %s, template %dc)' % (pr, t['Price'] or 0))
            sc -= 1
    if p['af'] is not None and t['Object_Type'] in range(32, 39):
        if abs((t['DPS_AF'] or 0) - p['af']) < 0.6:
            sc += 2; why.append('AF %g matches' % p['af'])
        else:
            sc -= 2; why.append('AF differs (page %g, template %s)' % (p['af'], t['DPS_AF']))
    if p['dps'] is not None and t['Object_Type'] not in range(32, 39) and (t['DPS_AF'] or 0) > 0:
        if abs((t['DPS_AF'] or 0) - p['dps'] * 10) < 1.1:
            sc += 2; why.append('DPS %g matches' % p['dps'])
        else:
            sc -= 2; why.append('DPS differs (page %g, template %s)' % (p['dps'], t['DPS_AF']))
    if p['delay'] is not None and (t['SPD_ABS'] or 0) > 0 and t['Object_Type'] not in range(32, 39):
        if abs((t['SPD_ABS'] or 0) - p['delay'] * 10) < 1.1:
            sc += 1; why.append('speed matches')
        else:
            sc -= 1; why.append('speed differs')
    if in_lists[t['Id_nb'].lower()]:
        sc += 1; why.append('template already sold by %d list(s)' % in_lists[t['Id_nb'].lower()])
    return sc, why


additions = []
present_other_name = []
skipped = collections.Counter()
seen = {}
# per list: normalised item name -> template rows on that list
list_tmpl_by_name = collections.defaultdict(lambda: collections.defaultdict(list))
for lid, rows in lists.items():
    for r in rows:
        t = tmpl.get(r['ItemTemplateID'].lower())
        if t:
            list_tmpl_by_name[lid][item_norm(t['Name'])].append(t)
all_names_by_realm = collections.defaultdict(list)
for nmk, ts in by_name.items():
    for rr in {t['Realm'] for t in ts}:
        all_names_by_realm[rr].append(nmk)
QUALITY_WORDS = {'superior', 'exquisite', 'exceptional'}


def resolve_key(m, p):
    nn = norm(m['name'])
    realm = m['realm_tag'] or (p['realm'] if p['realm'] in ('Albion', 'Midgard', 'Hibernia') else None)
    key = (nn, realm)
    if key in choice:
        return key
    ks = [k for k in choice if k[0] == nn]
    if realm is None and len(ks) == 1:
        return ks[0]
    return None


def seal_price(p):
    for x in p['prices']:
        mm = re.search(r'(\d+)\s+(Diamond|Emerald|Sapphire)\s+Seals', x, re.I)
        if mm:
            return int(mm.group(1))
    return None


def stats_agree(t, p):
    """True if the page's own numbers confirm the template (AF, or DPS+speed, or seal/coin price); None if nothing to compare"""
    checks = []
    if p['af'] is not None and t['Object_Type'] in range(32, 39):
        checks.append(abs((t['DPS_AF'] or 0) - p['af']) < 0.6)
    if p['dps'] is not None and (t['DPS_AF'] or 0) > 0 and t['Object_Type'] not in range(32, 39):
        checks.append(abs(t['DPS_AF'] - p['dps'] * 10) < 1.1)
    if p['delay'] is not None and (t['SPD_ABS'] or 0) > 0 and t['Object_Type'] not in range(32, 39):
        checks.append(abs(t['SPD_ABS'] - p['delay'] * 10) < 1.1)
    sp = seal_price(p)
    if sp is not None:
        checks.append(t['Price'] == sp)
    cp = [copper(x) for x in p['prices']]
    cp = [x for x in cp if x]
    if cp:
        checks.append(t['Price'] in cp)
    if not checks:
        return None
    return all(checks)


def strip_q(n):
    return ' '.join(w for w in n.split() if w not in QUALITY_WORDS)


def near_names(inm, names):
    """names on a list that look like the same item under another name"""
    a = set(strip_q(inm).split())
    out = []
    for n in names:
        b = set(strip_q(n).split())
        if n.split()[-1:] != inm.split()[-1:]:
            continue   # different slot/kind word (Sleeves vs Gloves): never the same item
        diff = a ^ b
        if (len(diff) <= 1 and len(a & b) >= 2) or difflib.SequenceMatcher(None, strip_q(inm), strip_q(n)).ratio() >= 0.88:
            out.append(n)
    return out


for p in P:
    inm = item_norm(p['name'])
    # templates this item already has on co-sellers' lists (same page, other matched merchants)
    co_t = collections.Counter()
    for m in p['merchants']:
        k = resolve_key(m, p)
        if k:
            for t in list_tmpl_by_name[choice[k]].get(inm, []):
                co_t[t['Id_nb']] += 1
    for m in p['merchants']:
        key = resolve_key(m, p)
        if not key:
            skipped['merchant not matched unambiguously'] += 1
            continue
        lid = choice[key]
        if (p['key'], lid) in seen:
            continue
        seen[(p['key'], lid)] = 1
        if inm in list_names.get(lid, set()):
            skipped['already on list'] += 1
            continue
        mrealm = RNUM[key[1]] if key[1] else None
        rec = next(r for r in matching if norm(r['period_name']) == key[0] and r['realm'] == key[1])
        # already on the list under another (later / Allakhazam-typo) name, confirmed by the page's numbers?
        nears = near_names(inm, list_names.get(lid, set()))
        confirmed = [(n, t) for n in nears for t in list_tmpl_by_name[lid][n] if stats_agree(t, p)]
        prefix_only = [n for n in nears if strip_q(n) == strip_q(inm) and n != inm and all(stats_agree(t, p) is None for t in list_tmpl_by_name[lid][n])]
        if confirmed:
            n, t = confirmed[0]
            present_other_name.append({'page_key': p['key'], 'period_name': p['name'], 'list_id': orig_list.get(lid, lid), 'merchant_period_name': rec['period_name'],
                                       'world_name': t['Name'], 'world_template_id': t['Id_nb'], 'evidence': 'page stats/price equal the list item'})
            skipped['on list under another name (stats agree)'] += 1
            continue
        why = []
        conf = 'high' if rec['match_kind'] == 'exact' or 'case/accent' in rec['notes'] else 'medium'
        if 'chose list' in rec['notes'] or 'chose the only one' in rec['notes']:
            conf = 'medium' if conf == 'high' else 'low'
            why.append('merchant has several same-name NPCs; list chosen by overlap/zone')
        action = 'add_row'
        dead = None
        for dn, did in list_dead.get(lid, {}).items():
            if dn == inm or difflib.SequenceMatcher(None, dn, inm).ratio() >= 0.9:
                dead = did
                break
        if dead:
            action = 'fix_dead_row'
            why.append('list already has a row for "%s" but ItemTemplate %s does not exist (dead row): create/point that row instead of adding' % (p['name'], dead))
        near_note = None
        if nears and not dead:
            near_note = 'list has a similar name (stats not confirmed): ' + ', '.join('"%s"' % n for n in nears[:3])
            why.append(near_note)
        if prefix_only:
            why.append('the list has "%s": same name plus a quality prefix, the page gives no numbers to tell whether it is the same item (other DF prefixed items differ slightly from the period ones)' % prefix_only[0])
        want = {mrealm, 0} if mrealm else {1, 2, 3, 0}
        if p['realm'] in ('Albion', 'Midgard', 'Hibernia'):
            want &= {RNUM[p['realm']], 0}
        cands = [t for t in by_name.get(inm, []) if t['Realm'] in want]
        tname_note = ''
        if not cands:
            # Allakhazam spelling slips (Svarlaekar / Starkalaedar ...): close template names in the right realm
            pool = sorted({n for rr in want for n in all_names_by_realm.get(rr, [])})
            close = difflib.get_close_matches(inm, pool, n=3, cutoff=0.9)
            for cn in close:
                ok = [t for t in by_name[cn] if t['Realm'] in want]
                if ok:
                    cands = ok
                    tname_note = 'template name differs from page: "%s" (spelling)' % ok[0]['Name']
                    break
        tid = None
        if cands:
            scored = []
            for t in cands:
                sc, swhy = stat_score(t, p)
                if co_t.get(t['Id_nb']):
                    sc += 3
                    swhy = swhy + ["same template the co-sellers' lists use"]
                scored.append((sc, t['Realm'] == mrealm, in_lists[t['Id_nb'].lower()], t['Id_nb'], swhy))
            scored.sort(key=lambda x: (x[0], x[1], x[2]), reverse=True)
            sc, _, _, tid, swhy = scored[0]
            t = tmpl[tid.lower()]
            strong = stats_agree(t, p) is True or bool(co_t.get(tid))
            if near_note and not strong:
                conf = 'low'
            lou = p['levels_of_use']
            if lou and t['Level'] and not (lou[0] - 5 <= t['Level'] <= lou[1] + 5):
                swhy.append('level differs (page %s, template %s)' % (lou, t['Level']))
                sc -= 1
            why += swhy
            if tname_note:
                why.append(tname_note)
                conf = 'medium' if conf == 'high' else conf
            if len(scored) > 1 and scored[1][:3] == scored[0][:3]:
                why.append('tie with %s' % scored[1][3])
                conf = 'medium' if conf == 'high' else conf
            if sc < 0:
                conf = 'medium' if conf == 'high' else conf
            if sc < -1:
                conf = 'low'
        else:
            tid = 'p165_item_%d' % p['key']
            if near_note:
                conf = 'low'
            others = [t['Id_nb'] for t in by_name.get(inm, [])]
            why.append('no ItemTemplate with this name for realm %s: planned new item%s' % (key[1] or p['realm'], (' (same name exists for other realms: %s)' % ', '.join(others[:3])) if others else ''))
        if prefix_only:
            conf = 'low'
        if re.search(r'\(old\)', p['name'], re.I):
            conf = 'low'
            why.append('Allakhazam marks this page (old)')
        additions.append({'page_key': p['key'], 'item_name': p['name'], 'item_realm': p['realm'], 'merchant_period_name': rec['period_name'],
                          'merchant_realm': key[1], 'list_id': orig_list.get(lid, lid), 'item_template_id': tid, 'confidence': conf, 'action': action,
                          'dead_row_template_id': dead,
                          'why': '; '.join(['merchant %s (%s)' % (rec['match_kind'], rec['period_name'])] + why),
                          'page_capture': p['capture'], 'page_prices': p['prices']})

# same template proposed twice for one list (two Allakhazam pages of one item): keep the first, list the extra page keys
dedup = {}
for a in additions:
    k = (a['list_id'].lower(), a['item_template_id'].lower())
    if k in dedup:
        dedup[k].setdefault('also_page_keys', []).append(a['page_key'])
        skipped['duplicate page of the same template'] += 1
    else:
        dedup[k] = a
additions = list(dedup.values())

# free slots: pages 0-4 x slots 0-29 (MerchantTradeItems.MAX_PAGES_IN_TRADEWINDOWS=5, MAX_ITEM_IN_TRADEWINDOWS=30)
used = collections.defaultdict(set)
for lid, rows in lists.items():
    for r in rows:
        used[lid].add((r['PageNumber'], r['SlotPosition']))
order = {'high': 0, 'medium': 1, 'low': 2}
for a in sorted(additions, key=lambda a: (a['list_id'].lower(), order[a['confidence']], a['page_key'])):
    lid = a['list_id'].lower()
    if a['action'] == 'fix_dead_row':
        a['suggested_page'] = a['suggested_slot'] = None
        continue
    free = [(pg, sl) for pg in range(5) for sl in range(30) if (pg, sl) not in used[lid]]
    if free:
        pg, sl = free[0]
        used[lid].add((pg, sl))
        a['suggested_page'], a['suggested_slot'] = pg, sl
    else:
        a['suggested_page'] = a['suggested_slot'] = None
        a['why'] += '; LIST FULL (150 slots): no free page/slot'
for a in additions:
    lid = a['list_id'].lower()
    a['list_rows_now'] = len(lists.get(lid, []))
    a['list_used_by_npcs'] = sorted(list_users.get(lid, set()))
additions.sort(key=lambda a: (a['list_id'].lower(), a['page_key']))

# ---------------- location-only entries (no merchant named) ----------------
loc = collections.defaultdict(list)
for p in P:
    if not p['merchants']:
        loc[tuple(p['found_in'][:20])].append(p)
loc_out = []
for k, ps in sorted(loc.items(), key=lambda kv: -len(kv[1])):
    sample = [{'key': p['key'], 'name': p['name'], 'realm': p['realm'], 'prices': p['prices']} for p in ps]
    # which world lists carry these items?
    hits = collections.Counter()
    for p in ps:
        inm = item_norm(p['name'])
        for lid, names in list_names.items():
            if inm in names:
                hits[lid] += 1
    top = [{'list_id': l, 'items_present': n, 'npcs': sorted(list_users.get(l, set()))[:6],
            'regions': sorted({regname.get(m['Region']) for m in merch if m['list'] == l})[:6]} for l, n in hits.most_common(6)]
    loc_out.append({'found_in': list(k), 'pages': len(ps), 'items': sample, 'world_lists_holding_these_items': top})

stats = {
    'pages_with_merchant_block': len(P), 'pages_with_named_merchant': sum(1 for p in P if p['merchants']),
    'pages_location_only': sum(1 for p in P if not p['merchants'] and p['found_in']),
    'pages_no_merchant_info': sum(1 for p in P if not p['merchants'] and not p['found_in']),
    'period_merchants': len(matching),
    'match_kinds': collections.Counter(r['match_kind'] for r in matching),
    'chosen_unambiguous': sum(1 for r in matching if r.get('chosen_list')),
    'ambiguous': sum(1 for r in matching if 'AMBIGUOUS' in r['notes']),
    'unconfirmed': sum(1 for r in matching if r.get('unconfirmed')),
    'present_under_other_name': len(present_other_name),
    'additions_fix_dead_row': sum(1 for a in additions if a['action'] == 'fix_dead_row'),
    'additions_no_free_slot': sum(1 for a in additions if a['action'] == 'add_row' and a['suggested_slot'] is None),
    'additions': len(additions), 'additions_by_conf': collections.Counter(a['confidence'] for a in additions),
    'additions_planned_items': sum(1 for a in additions if a['item_template_id'].startswith('p165_item_')),
    'skipped': skipped,
    'merchantitem_rows_with_missing_template': sum(list_missing_tmpl.values()),
    'lists_with_missing_templates': len(list_missing_tmpl),
}
json.dump(matching, open(os.path.join(HERE, 'merchant_matching.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
json.dump(additions, open(os.path.join(HERE, 'merchant_stock_additions.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
json.dump(present_other_name, open(os.path.join(HERE, 'merchant_present_other_name.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
json.dump(loc_out, open(os.path.join(HERE, 'merchant_location_entries.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
json.dump(stats, open(os.path.join(HERE, 'merchant_stats.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print(json.dumps(stats, indent=1))
