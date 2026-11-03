"""drops agent: where_rules.json, updates.json, review_decisions.csv, unfilled.csv from work/base_decisions.json and
the build_drops.py results.

    python -I build_outputs.py
"""
import csv, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sources import *


def q(s):
    return "'" + s.replace("'", "''") + "'"


def inlist(vals):
    return '(' + ','.join(q(v) for v in sorted(set(vals))) + ')'


def main():
    W = World()
    S = Sources(W)
    D = jload(os.path.join(WORK, 'base_decisions.json'))
    PAIR = "(lower(TemplateName) || '|' || lower(ItemTemplateID))"
    rules, updates = [], []

    # ---- A. items -----------------------------------------------------------------------------------------------
    groups = collections.defaultdict(list)
    for iid, why in D['remove_items'].items():
        if why.startswith('item_actions REMOVE'):
            groups['atlas'].append(iid)
        elif why.startswith('REVIEW'):
            groups['review'].append(iid)
        elif why.startswith('Atlas boss trophy'):
            groups['trophy'].append(iid)
        else:
            groups['nosource'].append(iid)
    reasons = {
        'atlas': 'True 1.65: no freeshard drops - loot rows of items the drop audit graded REMOVE (Atlas/Phoenix-era '
                 'packages AtlasXPItem/DrAtlasXPItem/atlas_orbs_item/drmed/atlas_trophys or no source at all; '
                 'audit out/item_actions.json; drops agent 2026-10-31)',
        'review': 'True 1.65: no freeshard drops - loot rows of items known only from the later Allakhazam site with no '
                  'player comment before 1 Nov 2003 and no period page or Uthgard record (REVIEW items, '
                  'fix/drops/review_decisions.csv)',
        'trophy': 'True 1.65: no Atlas boss trophies (trophies/taxidermy came with Trials of Atlantis; Atlas loot)',
        'nosource': 'True 1.65: no freeshard drops - loot rows of items no source knows (no period Allakhazam page, no '
                    'Uthgard item or drop, no live Allakhazam comment before 1 Nov 2003; drops agent list in REPORT.md)',
    }
    for g in ('atlas', 'review', 'trophy', 'nosource'):
        if groups[g]:
            rules.append({'table': 'LootTemplate', 'key': 'LootTemplate_ID',
                          'where': f"lower(ItemTemplateID) IN {inlist(i.lower() for i in groups[g])}", 'reason': reasons[g]})

    # ---- A. Atlas timed XP-item drop (Chance < 0) that is not a period kill-task item ------------------------------
    kt = [f"{k['template'].lower()}|{k['item_id'].lower()}" for k in D['killtask']]
    rules.append({'table': 'LootTemplate', 'key': 'LootTemplate_ID',
                  'where': f"Chance < 0 AND {PAIR} NOT IN {inlist(kt)}",
                  'reason': 'True 1.65: no Atlas timed "XP item" drop (LootTemplate Chance < 0 = one guaranteed item per '
                            'player every N minutes, an Atlas freeshard mechanic); the period kill-task items keep their '
                            'row and become a normal % drop (updates)'})

    # ---- A. Atlas-package loot rows no source backs ---------------------------------------------------------------
    pairs = [p for p, why in D['remove_pairs'].items() if not why.startswith('Atlas timed')]
    if pairs:
        rules.append({'table': 'LootTemplate', 'key': 'LootTemplate_ID', 'where': f"{PAIR} IN {inlist(pairs)}",
                      'reason': 'True 1.65: no freeshard drops - Atlas-era loot rows (items of packages atlas_orbs_item / '
                                'drmed) that no period Allakhazam page, Uthgard record or early live Allakhazam page gives '
                                'to that monster (the item itself is period and stays where a source puts it)'})

    # ---- dangling rows (item template missing) ---------------------------------------------------------------------
    dang = [f"{d['template'].lower()}|{d['item_id'].lower()}" for d in D['dangling']]
    rules.append({'table': 'LootTemplate', 'key': 'LootTemplate_ID',
                  'where': f"{PAIR} IN {inlist(dang)} AND lower(ItemTemplateID) NOT IN (SELECT lower(Id_nb) FROM ItemTemplate)",
                  'reason': 'True 1.65: loot rows that point at item templates that do not exist (they never dropped; '
                            'where a period page has the item, drops165.json gives the built item instead)'})

    # ---- dead MobXLootTemplate rows ---------------------------------------------------------------------------------
    mp = [f"{m['mob'].lower()}|{m['template'].lower()}" for m in D['dead_mobx']]
    rules.append({'table': 'MobXLootTemplate', 'key': 'MobXLootTemplate_ID',
                  'where': f"(lower(MobName) || '|' || lower(LootTemplateName)) IN {inlist(mp)} AND "
                           f"lower(LootTemplateName) <> lower(MobName) AND lower(LootTemplateName) NOT IN "
                           f"(SELECT lower(TemplateName) FROM LootTemplate)",
                  'reason': 'True 1.65: dead loot-table links (Atlas XP-item hooks: LootTemplateName is an item id with no '
                            'LootTemplate rows, e.g. Ghoulic Viper -> ghoulic_viper_fang)'})

    # ---- B. kill-task items: Chance -3 -> a normal % drop -------------------------------------------------------------
    kt_report = []
    for k in D['killtask']:
        t, iid = k['template'], k['item_id']
        it = W.items[iid.lower()]
        rate, how = None, None
        for m in k['mobs']:
            ok, _ = S.uthgard_for_mob(m)
            n = kills = 0
            for r in ok:
                kills += r.get('killed') or 0
                for d in r['drops']:
                    if d['sec'] != 'rog' and norm(d['item']) == norm(it['Name']):
                        n += d['n']
            if n:
                rate = max(1, min(100, round(100 * n / max(kills, 20))))
                how = f'Uthgard measured on {m}: {n} in {kills} kills'
                break
        if rate is None:
            ur = S.uthgard_item_rate(it['Name'])
            if ur:
                rate, how = ur[0], ur[1]
        if rate is None:
            rate, how = 25, 'estimated 25 % (no Uthgard record of the item)'
        updates.append({'sql': f"UPDATE LootTemplate SET Chance = {rate} WHERE lower(TemplateName) = {q(t.lower())} "
                               f"AND lower(ItemTemplateID) = {q(iid.lower())}",
                        'reason': f"True 1.65: period kill-task item {it['Name']} on {t}: normal {rate} % drop instead of "
                                  f"the Atlas 3-minute timed drop ({how}; period: {'; '.join(k['period'][:2]) or 'kept REVIEW item'})"})
        kt_report.append({'template': t, 'item': iid, 'name': it['Name'], 'chance': rate, 'rate_source': how,
                          'period': k['period']})

    jdump(rules, os.path.join(HERE, 'where_rules.json'))
    jdump(updates, os.path.join(HERE, 'updates.json'))
    jdump(kt_report, os.path.join(WORK, 'killtask_report.json'))

    # ---- C. review decisions --------------------------------------------------------------------------------------
    with open(os.path.join(HERE, 'review_decisions.csv'), 'w', newline='', encoding='utf-8') as f:
        w = csv.writer(f)
        w.writerow(['item_id', 'name', 'package', 'monsters', 'decision', 'evidence'])
        for r in sorted(D['review'], key=lambda r: (r['decision'], r['item_id'].lower())):
            w.writerow([r['item_id'], r['name'], r['package'], '; '.join(r['mobs']), r['decision'], r['evidence']])

    # ---- unfilled -----------------------------------------------------------------------------------------------------
    U = jload(os.path.join(WORK, 'unfilled_raw.json'))
    drops = jload(os.path.join(HERE, 'drops165.json'))['monsters']
    addrows = {}
    if os.path.exists(os.path.join(HERE, 'add-dropitems165.json')):
        addrows = {r['Id_nb'].lower(): r for r in jload(os.path.join(HERE, 'add-dropitems165.json'))['rows']}
    # what drops after the fix: (item name norm, realm)
    removed_ids = {k.lower() for k in D['remove_items']}
    removed_pairs = set(D['remove_pairs'])
    dangling = {f"{d['template'].lower()}|{d['item_id'].lower()}" for d in D['dangling']}
    dropped = set()
    for r in W.loot:
        if r['LootTemplate_ID'] in W.period_added:
            continue
        i = r['ItemTemplateID'].lower()
        p = f"{r['TemplateName'].lower()}|{i}"
        if i in removed_ids or p in removed_pairs or p in dangling or (r['Chance'] < 0 and p not in set(kt)):
            continue
        it = W.items.get(i)
        if it:
            dropped.add((norm(strip_suffix(it['Name'])), it['Realm'] or 0))
    for v in drops.values():
        for i, _ in v['items']:
            it = W.items.get(i.lower()) or addrows.get(i.lower())
            dropped.add((norm(strip_suffix(it['Name'])), it['Realm'] or 0))
    for o in W.otd:
        it = W.items.get(o['ItemTemplateID'].lower())
        if it:
            dropped.add((norm(strip_suffix(it['Name'])), it['Realm'] or 0))
    MP = jload(os.path.join(AUDIT, 'work', 'items', 'missing_period_drops.json'))
    still = []
    for x in MP:
        k = (norm(strip_suffix(x['name'])), REALM_OF_NAME.get(x['realm'], 0))
        if k in dropped or (k[0], 0) in dropped:
            continue
        still.append(x)
    seen = set()
    with open(os.path.join(HERE, 'unfilled.csv'), 'w', newline='', encoding='utf-8') as f:
        w = csv.writer(f)
        w.writerow(['kind', 'monster', 'item', 'why', 'source'])
        for u in sorted(U, key=lambda u: (u['kind'] != 'period', u['mob'], u['item'])):
            key = (u['kind'], u['mob'], u['item'].lower())
            if key in seen:
                continue
            seen.add(key)
            w.writerow([('period loot of the monster' if u['kind'] == 'period' else 'Uthgard drop'), u['mob'], u['item'],
                        u['why'], u['source']])
        for d in D['dangling']:
            w.writerow(['old loot row with no item template (removed)', d['template'], d['item_id'],
                        'item template missing; ' + ('period page(s): ' + ', '.join(d['pages']) if d['pages'] else
                                                     'no period page by that id'), 'LootTemplate'])
        for x in sorted(still, key=lambda x: (x['realm'], x['name'])):
            w.writerow(['period item, no monster named', '', f"{x['name']} ({x['realm']})",
                        'period page says it drops off monsters' + (f" in {', '.join(x['zones'][:3])}" if x['zones'] else '')
                        + ' but no source names the monster (period mob Known Loot, Uthgard, early live Allakhazam)',
                        f"period-zam item {x['key']} (capture {x['capture'][:8]})"])
    jdump({'period_items_still_unassigned': len(still), 'period_items_total': len(MP)}, os.path.join(WORK, 'unfilled_counts.json'))
    print('rules', len(rules), 'updates', len(updates), 'unassigned period items', len(still), 'of', len(MP))


if __name__ == '__main__':
    main()
