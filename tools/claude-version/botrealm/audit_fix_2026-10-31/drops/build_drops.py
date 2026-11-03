"""drops agent, part D: the new drops165.json (full replacement) + add-dropitems165.json (items built from period pages).

Sources, per monster (name + realm + zone matched, see sources.py):
  1. Uthgard measured drops, only from Uthgard monsters whose zone is in a realm (classic part) the server monster
     lives in; item templates only of Realm 0 or that realm (all realms in Darkness Falls / Summoner's Hall / BGs).
     Rules kept from build_drops165.py: kills counted as at least 20, ROG section and coin bags ignored.
  2. the period Allakhazam mob page's Known Loot (period_mobs_parsed.json)
  3. period Allakhazam item pages that name the monster as dropper
  4. live Allakhazam mob page Known Loot, only when the page has a comment dated before 1 Nov 2003 and the item is a
     period item (period item page that says it drops off monsters)
Rates: Uthgard rate for that monster+item; else the item's Uthgard rate on another monster; else estimated (marked):
named magical 15 % (raid/epic 25 %), regular magical 3 %, plain sellable loot 20 %.
Never: freeshard items (part A removals), Darkness Falls seals (left as they are), classic quest items (cq_*).

    python -I build_drops.py
"""
import csv, datetime, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sources import *

sys.path.insert(0, VW)
import pageparse  # noqa: E402
import build_vendoritems165 as bv  # noqa: E402

MIN_KILLS = 20
STAMP = '2026-10-31 12:00:00'
SEAL_IDS = {'diamondseal', 'emeraldseal', 'sapphireseal', 'rubyseal'}
SEAL_NAMES = {'daemon diamond seal', 'daemon emerald seal', 'daemon sapphire seal', 'daemon ruby seal', 'diamond seal',
              'emerald seal', 'sapphire seal', 'ruby seal'}
FREESHARD_PKGS = {'atlasxpitem', 'dratlasxpitem', 'atlas_trophys'}
RAID_NAME = re.compile(r"(?i)^(golestandt|cuuldurach|gjalpinulva|legion$|prince |princess nahemah|high lord |"
                       r"grand chancellor adremal|grand summoner govannon|apocalypse|olcasgean|king tuscar|queen kula|"
                       r"xanxicar|iarnvidiur|myrddraxis|evern|glacier giant|green knight|fames$|bellum$|morbus$|funus$)")
EPIC_REGIONS = {60, 160, 191, 248}
SUFFIX_REALM = {'alb': 1, 'mid': 2, 'hib': 3}


def main():
    W = World()
    S = Sources(W)
    D = jload(os.path.join(WORK, 'base_decisions.json'))
    A = {x['item_id'].lower(): x for x in jload(os.path.join(AUDIT, 'out', 'item_actions.json'))}
    review_keep = {r['item_id'].lower() for r in D['review'] if r['decision'] == 'KEEP'}
    removed_ids = {k.lower() for k in D['remove_items']}
    removed_pairs = set(D['remove_pairs'])
    dangling = {f"{d['template'].lower()}|{d['item_id'].lower()}" for d in D['dangling']}
    named_keys = set(jload(os.path.join(AUDIT, 'work', 'named', 'candidates.json')))

    def nk(n):
        n = re.sub(r'^(the|a|an) ', '', (n or '').lower().replace('`', "'").replace('’', "'").strip())
        return re.sub(r'[^a-z0-9]', '', n)

    # ---- base loot after parts A-C (what stays in the monsters' tables) ---------------------------------------
    base_after = collections.defaultdict(dict)       # template lower -> {item lower: chance}
    killtask = {(k['template'].lower(), k['item_id'].lower()) for k in D['killtask']}
    for r in W.loot:
        if r['LootTemplate_ID'] in W.period_added:
            continue
        t, i = r['TemplateName'].lower(), r['ItemTemplateID'].lower()
        pair = f'{t}|{i}'
        if i in removed_ids or pair in removed_pairs or pair in dangling:
            continue
        if r['Chance'] < 0 and (t, i) not in killtask:
            continue
        base_after[t][i] = r['Chance']
    used_ids = {r['ItemTemplateID'].lower() for r in W.loot}

    # ---- vendor builder on the snapshot (period page -> ItemTemplate row) -------------------------------------
    bv.DB = SNAP
    bv.W = bv.World()
    raw_pages = {}
    for line in open(os.path.join(VW, 'period_item_pages.jsonl'), encoding='utf-8'):
        rec = json.loads(line)
        raw_pages[rec['key']] = rec
    parsed = {k: pageparse.parse(r) for k, r in raw_pages.items()}
    built, build_fail = {}, {}
    # plain sellable loot (gems, ...): the vendor builder needs a sibling sharing a name word; a one-word gem has
    # none, so its look comes from an existing gem of the same kind of loot (Realm 0 inventory item that Uthgard lists
    # in its "craft" section), the one sharing a word or else the nearest Uthgard sell value
    def money(sv):
        m = re.match(r'^\s*(?:(\d+)p\s*)?(?:(\d+)g\s*)?(?:(\d+)s\s*)?(?:(\d+)c)?\s*$', sv or '')
        if not m or not any(m.groups()):
            return None
        pp, g, si, cc = (int(x) if x else 0 for x in m.groups())
        return ((pp * 1000 + g) * 100 + si) * 100 + cc
    craft_names = {norm(d['item']) for recs in S.uth.values() for r in recs for d in r['drops'] if d['sec'] == 'craft'}
    sell = {}
    for u in S.uitems:
        v = money(u.get('Sell value'))
        if u.get('name') and v:
            sell.setdefault(norm(u['name']), v)
    gem_sibs = [r for r in W.items.values() if (r['Realm'] or 0) == 0 and r['Object_Type'] == 0 and r['Item_Type'] == 40
                and norm(r['Name']) in craft_names and r['Model'] and norm(r['Name']) in sell
                and r['Id_nb'].lower() not in removed_ids]

    def build_plain(rec, p, st, ent, sname):
        n = norm(sname)
        if n not in craft_names or not gem_sibs:
            return None, 'no existing sibling item (plain loot that Uthgard does not list as craft loot)'
        words = set(n.split())
        target = sell.get(n)
        sib = min(gem_sibs, key=lambda r: (not (words & set(norm(r['Name']).split())),
                                           abs((sell.get(norm(r['Name'])) or 0) - (target or 0)), r['Id_nb']))
        row = {c: sib.get(c) for c in bv.W.cols}
        der = ent['derived']
        row.update({'Name': sname, 'TranslationId': None, 'ExamineArticle': None, 'MessageArticle': None,
                    'Realm': {'Albion': 1, 'Midgard': 2, 'Hibernia': 3}.get(rec['realm'], 0), 'Description': None,
                    'ClassType': None, 'LastTimeRowUpdated': STAMP})
        for k in range(1, 11):
            row[f'Bonus{k}'] = row[f'Bonus{k}Type'] = 0
        der['Name'] = 'page'
        der['Model/Weight/flags'] = f"from {sib['Id_nb']} (gem sibling: {'shared name word' if words & set(norm(sib['Name']).split()) else 'nearest Uthgard sell value'})"
        if target:
            row['Price'] = target * 100 // 50
            der['Price'] = f"Uthgard sell value {target} copper x 2 (item_sell_ratio 50); the period page gives no price"
        else:
            der['Price'] = f"from {sib['Id_nb']}"
        ent['sibling'] = sib['Id_nb']
        return row, None

    def build_item(key):
        if key in built or key in build_fail:
            return built.get(key)
        rec, p = raw_pages[key], parsed[key]
        sn = norm(strip_suffix(rec['name']))
        if any(r['Id_nb'].lower().startswith('cq_') for r in W.items_by_name.get(sn, [])):
            build_fail[key] = 'classic quest item (a cq_ template of this name exists): it drops through its quest'
            return None
        if 'Kill Task' in rec['raw'] or 'used for quests' in rec['raw']:
            build_fail[key] = 'quest / kill-task item page: not built as general loot'
            return None
        st = bv.page_stats(p)
        st['left'] = p['left_hand_usable']
        bon, unknown = bv.map_bonuses(p)
        ent = {'page_key': key, 'name': rec['name'], 'realm': rec['realm'], 'derived': {}, 'url': rec['url']}
        sname = bv.strip_suffix(rec['name'])
        try:
            row, why = bv.build(rec, p, st, bon, unknown, ent, sname, parsed, raw_pages, STAMP)
        except Exception as e:      # a page the vendor builder can't read
            row, why = None, f'builder error: {e}'
        if row is None and st['item_type'] == 40 and not bon and 'sibling' in (why or ''):
            row, why = build_plain(rec, p, st, ent, sname)
        if row is None:
            build_fail[key] = why
            return None
        row['Id_nb'] = row['ItemTemplate_ID'] = f'p165_drop_{key}'
        row['PackageID'] = 'True165Drop'
        if strip_suffix(rec['name']) != row['Name']:
            ent['derived']['Name'] = f'page (Allakhazam tag removed from "{rec["name"]}")'
            row['Name'] = strip_suffix(rec['name'])
        if not row.get('Price'):
            sib = W.items.get((ent.get('sibling') or '').lower())
            if sib and sib.get('Price'):
                row['Price'] = sib['Price']
                ent['derived']['Price'] = f"from {sib['Id_nb']} (sell value of the model sibling; page gives none)"
        W.items[row['Id_nb'].lower()] = row
        W.items_by_name[norm(row['Name'])].append(row)
        built[key] = {'row': row, 'ent': ent, 'bonus_lines_not_built': ent.get('bonus_lines_not_built'),
                      'ability_not_built': ent.get('ability_not_built')}
        return built[key]

    # ---- item resolution ----------------------------------------------------------------------------------------
    names_list = list(W.items_by_name)
    close_cache = {}

    def usable(r, realms):
        iid = r['Id_nb'].lower()
        if iid in removed_ids or iid.startswith('cq_') or iid in SEAL_IDS or norm(r['Name']) in SEAL_NAMES:
            return False
        if (r['Realm'] or 0) not in realms:
            return False
        pkg = (r['PackageID'] or '').lower()
        a = A.get(iid)
        if a and a['action'].startswith('REVIEW') and iid not in review_keep:
            return False
        if pkg in FREESHARD_PKGS and not (a and a['action'].startswith('KEEP')) and iid not in review_keep:
            return False
        return True

    def resolve(name, realms, lv, page=None):
        """-> (item row, how) or (None, why)"""
        n = norm(strip_suffix(name))
        cands = [r for r in W.items_by_name.get(n, []) if usable(r, realms)]
        how = 'name'
        st = bon = None
        if page is not None:
            p = parsed.get(page['key'])
            if p:
                st = bv.page_stats(p)
                st['left'] = p['left_hand_usable']
                bon, _ = bv.map_bonuses(p)
        if not cands:
            if n not in close_cache:
                close_cache[n] = difflib.get_close_matches(n, names_list, 3, 0.93)
            for v in close_cache[n]:
                vc = [r for r in W.items_by_name[v] if usable(r, realms)]
                if not vc:
                    continue
                close = difflib.SequenceMatcher(None, n, v).ratio() >= 0.96 and n.split()[-1:] == v.split()[-1:]
                if not close:
                    vc = [r for r in vc if st is not None and (st['af'] is not None or st['dps']) and bv.stats_equal(st, r, [])]
                if vc:
                    cands, how = vc, f'spelling variant "{vc[0]["Name"]}"'
                    break
        if not cands:
            return None, 'no ItemTemplate of that name for the realm'

        def score(r):
            ok = st is not None and bv.stats_equal(st, r, bon or [])
            okb = st is not None and bv.stats_equal(st, r, [])
            pkg = (r['PackageID'] or '').lower()
            return (not ok, not okb, r['Id_nb'].lower() not in used_ids, abs((r['Level'] or 0) - lv),
                    pkg.startswith('atlas') or pkg == 'drmed', (r['Realm'] or 0) != 0, r['Id_nb'])
        best = min(cands, key=score)
        if st is not None and len(cands) > 1:
            how += ', stats match the period page' if bv.stats_equal(st, best, bon or []) else ', nearest level'
        return best, how

    # ---- classification for estimates ---------------------------------------------------------------------------
    def mob_class(m):
        lv = max([s['Level'] or 0 for s in W.spawns.get(m, []) + W.removed_spawns.get(m, [])] or [0])
        regs = {s['Region'] for s in W.spawns.get(m, []) + W.removed_spawns.get(m, [])}
        named = nk(m) in named_keys or S.scripted_display.get(m, 'x')[0].isupper()
        if RAID_NAME.search(m) or (named and (lv >= 62 or regs & EPIC_REGIONS)):
            return 'raid'
        return 'named' if named else 'regular'

    def plain(r):
        return (r['Item_Type'] == 40 or r['Object_Type'] == 0) and not any(r[f'Bonus{k}Type'] for k in range(1, 11))

    def estimate(cls, r):
        if plain(r):
            return 20, 'estimated: plain loot 20 %'
        return {'raid': (25, 'estimated: raid/epic boss magical item 25 %'),
                'named': (15, 'estimated: named monster magical item 15 %'),
                'regular': (3, 'estimated: regular monster magical item 3 %')}[cls]

    # ---- the monsters -------------------------------------------------------------------------------------------
    # Darkness Falls seals are left as they are (separate decision): the currency-seal rows the first pass
    # (drops165.json 2026-10-30, Uthgard measured) gave some monsters are carried over unchanged
    old_drops = jload(os.path.join(CV, 'runtime', 'true165', 'drops165.json'))['monsters']
    old_seals = collections.defaultdict(list)
    for om, ov in old_drops.items():
        for iid, ch in ov['items']:
            if iid.lower() in SEAL_IDS:
                old_seals[om.lower()].append((W.items[iid.lower()], ch))
    names = set(W.spawns) | set(W.removed_spawns) | {n for n in S.scripted if S.info(n)[0]}
    out, report, unfilled = {}, [], []
    stats = collections.Counter()
    for m in sorted(names):
        realms_m, zones_m, keys_m, how_m = S.info(m)
        if not realms_m:
            continue
        allowed = S.allowed(m)
        lv = W.mob_level(m) or (sum(s['Level'] or 0 for s in W.removed_spawns.get(m, [])) /
                                max(1, len(W.removed_spawns.get(m, []))))
        cls = mob_class(m)
        have = dict(base_after.get(m, {}))
        add = {}                  # item lower -> [Id_nb, chance, sources]

        def put(it, chance, src):
            iid = it['Id_nb'].lower()
            if iid in have or f'{m}|{iid}' in removed_pairs:
                return
            if iid in add:
                add[iid][2].append(src)
                return
            add[iid] = [it['Id_nb'], chance, [src]]

        # 1. Uthgard (realm-matched)
        ok, bad = S.uthgard_for_mob(m)
        if bad:
            stats['uthgard records skipped (other realm)'] += len(bad)
        groups = collections.defaultdict(lambda: {'kills': 0, 'recs': []})
        for r in ok:
            groups[r['realm']]['kills'] += r.get('killed') or 0
            groups[r['realm']]['recs'].append(r)
        item_n = collections.defaultdict(lambda: [0, 0, None, []])   # Id_nb -> [n, kills, row, labels]
        for rr, g in groups.items():
            ir = {0, 1, 2, 3} if rr in NEUTRAL else {0, rr}
            ir &= allowed
            seen_item = collections.defaultdict(int)
            for r in g['recs']:
                for d in r['drops']:
                    if d['sec'] == 'rog' or norm(d['item']) == 'bag of coins':
                        continue
                    if norm(d['item']) in SEAL_NAMES or 'seal' in norm(d['item']).split() and 'pack' in norm(d['item']):
                        stats['Uthgard seal drops left out (seals left as they are)'] += 1
                        continue
                    it, why = resolve(d['item'], ir, lv)
                    if it is None:
                        for pg in sorted(S.period_item_pages(d['item'], ir), key=lambda p: p['key']):
                            b = build_item(pg['key'])
                            if b:
                                it, why = b['row'], f"built from period page {pg['key']}"
                                break
                            why = f"no ItemTemplate; period page {pg['key']} could not be built: {build_fail.get(pg['key'])}"
                    if it is None:
                        unfilled.append({'mob': m, 'item': d['item'], 'source': f"Uthgard {r['id']} ({r['zone']})",
                                         'why': why + ('' if S.period_item_pages(d['item']) else '; no period item page'),
                                         'kind': 'uthgard'})
                        continue
                    seen_item[it['Id_nb']] += d['n']
                    item_n[it['Id_nb']][2] = it
                    item_n[it['Id_nb']][3].append(f"Uthgard {r['id']} '{r['name']}' ({r['zone']}) {d['n']}/{r.get('killed')} kills")
            for iid, n in seen_item.items():
                item_n[iid][0] += n
        for iid, (n, _, it, labels) in item_n.items():
            ir = it['Realm'] or 0
            kills = sum(g['kills'] for rr, g in groups.items() if ir == 0 or rr == ir or rr in NEUTRAL)
            ch = max(1, min(100, round(100 * n / max(kills, MIN_KILLS))))
            put(it, ch, 'Uthgard measured: ' + '; '.join(labels[:3]))
            stats['uthgard rows'] += 1

        # 2.+3. period Known Loot and period item pages naming the monster
        wanted = []      # (item name, realms, page or None, source text)
        for p, how in S.period_pages_for_mob(m):
            for x in p.get('loot') or []:
                tr = tag_realm(x)
                wanted.append((x, {0, tr} if tr else None,
                               f"period-zam mob {p['key']} '{p['name']}' (capture {p['timestamp'][:8]}) Known Loot"
                               + ('' if how == 'zone' else ' [page has no zone; realm match]')))
        mv = mvariants(m)
        dpages = {pg['key']: pg for v in mv for pg in S.pitem_by_dropper.get(v, [])}
        for pg in dpages.values():
            if True:
                pr = REALM_OF_NAME.get(pg['realm'], 0)
                if pr and pr not in allowed:
                    continue
                wanted.append((pg['name'], {0, pr} if pr else None,
                               f"period-zam item {pg['key']} '{pg['name']}' (capture {pg['capture'][:8]}) dropped by {m}"))
        # 4. live Allakhazam mob page with a pre-Nov-2003 comment, period items only
        for e, early in S.live_mob_early(m):
            for _, x in e['loot']:
                pgs = [p for p in S.period_item_pages(x, allowed) if p['drops_off_monsters']]
                if pgs:
                    wanted.append((x, None, f"live-zam mob {e['cmob']} '{e['name']}' Known Loot (page comments from "
                                            f"{early[0]}); period item page {pgs[0]['key']} says it drops off monsters"))
        done = set()
        for x, rset, src in wanted:
            key = (norm(strip_suffix(x)), tuple(sorted(rset)) if rset else None)
            if key in done:
                for v in add.values():
                    if norm(strip_suffix(x)) == norm(strip_suffix(W.items[v[0].lower()]['Name'])):
                        if src not in v[2]:
                            v[2].append(src)
                continue
            done.add(key)
            realms_x = (rset & allowed) if rset else allowed
            pages = S.period_item_pages(x, realms_x)
            if rset is None and pages:
                realms_x = {0} | {REALM_OF_NAME.get(p['realm'], 0) for p in pages}
                realms_x &= allowed | {0}
            targets = []
            exact = [p for p in pages if p['name'].strip().lower() == x.strip().lower()]
            use_pages = exact or pages
            if not use_pages:
                it, how = resolve(x, realms_x, lv, None)
                if it is None:
                    unfilled.append({'mob': m, 'item': x, 'source': src, 'kind': 'period',
                                     'why': f"{how}; no period item page to build it from"})
                else:
                    targets.append((it, how))
            for pg in use_pages:
                pr = REALM_OF_NAME.get(pg['realm'], 0)
                rs = realms_x if pr == 0 else ({0, pr} & (allowed | {0}))
                it, how = resolve(x, rs, lv, pg)
                if it is None:
                    b = build_item(pg['key'])
                    if b:
                        it, how = b['row'], f"built from period page {pg['key']}"
                    else:
                        unfilled.append({'mob': m, 'item': x, 'source': src, 'kind': 'period',
                                         'why': f"no ItemTemplate; period page {pg['key']} could not be built: {build_fail.get(pg['key'])}"})
                        continue
                if all(t[0]['Id_nb'] != it['Id_nb'] for t in targets):
                    targets.append((it, how))
            for it, how in targets:
                iid = it['Id_nb'].lower()
                if iid in have or iid in add:
                    if iid in add and src not in add[iid][2]:
                        add[iid][2].append(src)
                    continue
                ur = S.uthgard_item_rate(it['Name']) or S.uthgard_item_rate(strip_suffix(x))
                if ur:
                    ch, lab = ur
                else:
                    ch, lab = estimate(cls, it)
                    stats['estimated rates'] += 1
                put(it, ch, f"{src} -> {it['Id_nb']} ({how}); rate: {lab}")
                stats['period rows'] += 1
        for it, ch in old_seals.get(m, []):
            if it['Id_nb'].lower() not in have:
                add[it['Id_nb'].lower()] = [it['Id_nb'], ch, ['Darkness Falls seal kept as it was (drops165 pass 1, Uthgard '
                                                              'measured); seals are a separate decision']]
                stats['seal rows kept from pass 1'] += 1
        if not add:
            continue
        rows = sorted(add.values(), key=lambda a: (-a[1], a[0]))
        rolled = sum(c for _, c, _ in rows if c < 100) + sum(c for c in have.values() if 0 < c < 100)
        cap = 4 if cls == 'raid' else 2 if cls == 'named' else (2 if rolled >= 50 else 1)
        disp = W.mob_names.get(m) or S.scripted_display.get(m) or m
        out[disp] = {'drop_count': cap, 'items': [[a[0], a[1]] for a in rows]}
        report.append({'mob': disp, 'class': cls, 'where': how_m, 'realms': sorted(map(str, realms_m)),
                       'drop_count': cap, 'rows': [{'item': a[0], 'chance': a[1], 'sources': a[2]} for a in rows]})
        stats['monsters'] += 1
        stats['rows'] += len(rows)

    data = {'source': 'True 1.65 drops pass 2 (2026-10-31): Uthgard measured drops (realm-matched) + period '
                      'Allakhazam Known Loot and item pages; estimates marked in fix/drops/REPORT.md',
            'min_kills': MIN_KILLS,
            'monsters': dict(sorted(out.items(), key=lambda kv: kv[0].lower()))}
    jdump(data, os.path.join(HERE, 'drops165.json'))
    jdump(report, os.path.join(WORK, 'drops_report.json'))
    rows = [b['row'] for k, b in sorted(built.items())]
    used = {a[0].lower() for v in out.values() for a in v['items']}
    rows = [r for r in rows if r['Id_nb'].lower() in used]
    if rows:
        jdump({'table': 'ItemTemplate', 'key': 'Id_nb',
               'reason': 'True 1.65 period drop items from Allakhazam item pages (Wayback <= 2003-11), stats from the '
                         'page, look from the nearest sibling (drops pass 2)', 'rows': rows},
              os.path.join(HERE, 'add-dropitems165.json'))
    jdump({k: {'name': raw_pages[k]['name'], 'realm': raw_pages[k]['realm'], **{x: v for x, v in b['ent'].items() if x != 'url'}}
           for k, b in built.items()}, os.path.join(WORK, 'built_items.json'))
    jdump(build_fail, os.path.join(WORK, 'build_fail.json'))
    jdump(unfilled, os.path.join(WORK, 'unfilled_raw.json'))
    print(dict(stats), 'built', len(rows), 'build failed', len(build_fail), 'unfilled', len(unfilled))


if __name__ == '__main__':
    main()
