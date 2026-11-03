"""drops agent, parts A-C: decide every non-period (base) LootTemplate row, the Chance -3 rows, the 127 REVIEW items and
the dead MobXLootTemplate rows. Read-only on the snapshot. Writes work/base_decisions.json (used by build_outputs.py).

    python -I analyze_base.py
"""
import csv, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sources import *

ATLAS_PKGS = {'atlas_orbs_item', 'drmed', 'atlasxpitem', 'dratlasxpitem', 'atlas_trophys', 'atlas_sell'}
SEALS = {'diamondseal', 'emeraldseal', 'sapphireseal'}


def main():
    W = World()
    S = Sources(W)
    A = {x['item_id'].lower(): x for x in jload(os.path.join(AUDIT, 'out', 'item_actions.json'))}

    # template -> mobs that read it (own name + MobXLootTemplate rows)
    tmpl_mobs = collections.defaultdict(set)
    for m in W.mobx:
        tmpl_mobs[m['LootTemplateName'].lower()].add(m['MobName'].lower())
    for t in {r['TemplateName'].lower() for r in W.loot}:
        tmpl_mobs[t].add(t)

    def mobs_of(t):
        return sorted(tmpl_mobs.get(t.lower(), {t.lower()}))

    def realms_of(t):
        rs = set()
        for m in mobs_of(t):
            rs |= S.allowed(m)
        return rs or {0, 1, 2, 3}

    # ---- evidence that a monster drops an item (by item name) -------------------------------------------------
    def backing(t, item):
        """[(level, text)] sources saying a monster that reads template t drops this item"""
        nm = norm(strip_suffix(item['Name']))
        realms = realms_of(t)
        ev = []
        for m in mobs_of(t):
            mv = mvariants(m)
            for p, how in S.period_pages_for_mob(m):
                if any(norm(strip_suffix(x)) == nm for x in p.get('loot') or []):
                    ev.append(('L1', f"period-zam mob {p['key']} '{p['name']}' (capture {p['timestamp'][:8]}) Known Loot"))
            for pg in S.period_item_pages(item['Name'], realms):
                if S.pitem_droppers.get(pg['key'], set()) & mv:
                    ev.append(('L1', f"period-zam item {pg['key']} '{pg['name']}' (capture {pg['capture'][:8]}) dropped by {m}"))
            ok, _ = S.uthgard_for_mob(m)
            for r in ok:
                for d in r['drops']:
                    if d['sec'] != 'rog' and norm(d['item']) == nm:
                        ev.append(('L1', f"Uthgard monster {r['id']} '{r['name']}' ({r['zone']}): {d['n']} in {r['killed']} kills"))
            for u in S.uitem_by_name.get(nm, []):
                for dm, n, rate in u.get('dropped_by') or []:
                    if mkey(dm) in mv:
                        ev.append(('L1', f"Uthgard item page {u['url'].split('=')[-1]} '{u['name']}' drops from {dm} ({n}, {rate})"))
            for e, early in S.live_mob_early(m):
                if any(norm(strip_suffix(x[1])) == nm for x in e['loot']):
                    ev.append(('L2', f"live-zam mob {e['cmob']} '{e['name']}' Known Loot (page has comments from {early[0]})"))
            zones = S.info(m)[1]
            for pg in S.period_item_pages(item['Name'], realms):
                fz = {zkey(z) for z in pg['found_in']}
                if pg['drops_off_monsters'] and fz & zones and not S.pitem_droppers.get(pg['key']):
                    ev.append(('L3', f"period-zam item {pg['key']} '{pg['name']}' (capture {pg['capture'][:8]}) found in "
                                     f"{', '.join(sorted(set(pg['found_in']) & set(z for z in pg['found_in'] if zkey(z) in zones)))}, drops off monsters (no dropper named)"))
        return sorted(set(ev))

    # ---- item-level evidence (does any source know this item at all, before 1 Nov 2003) -----------------------
    def item_known(item, realms):
        ev = known_loot(item)[:2]
        for pg in S.period_item_pages(item['Name'], realms):
            ev.append(f"period-zam item {pg['key']} '{pg['name']}' (capture {pg['capture'][:8]})")
        nm = norm(strip_suffix(item['Name']))
        for u in S.uitem_by_name.get(nm, [])[:3]:
            ev.append(f"Uthgard item page {u['url'].split('=')[-1]} '{u['name']}'")
        if nm in S.uth_item_rates and not any(e.startswith('Uthgard item') for e in ev):
            mob, zone, n, k = S.uth_item_rates[nm][0]
            ev.append(f"Uthgard monster drop list: {mob} ({zone}) {n} in {k} kills")
        for e, early in S.live_item_early(item['Name'], realms):
            ev.append(f"live-zam item {e['citem']} '{e['name']}' comment dated {early[0]}")
        if not ev:
            ev = S.variant_known(item['Name'], realms)
        return ev

    TOA = ({115, 153, 154, 155, 168} | set(range(169, 250)) | set(range(251, 257)) | set(range(99, 115))
           | set(range(157, 163)) | {156, 163, 164, 165})          # the audit's post-1.65 bonus types (work/items)
    kl_index = collections.defaultdict(list)
    for p in S.pmobs:
        for x in p.get('loot') or []:
            kl_index[norm(strip_suffix(x))].append((p, x))

    def known_loot(item):
        """period mob pages (Wayback <= 2003-11) whose Known Loot lists this item, realm-compatible"""
        ir = item['Realm'] or 0
        out = []
        for p, x in kl_index.get(norm(strip_suffix(item['Name'])), []):
            tr = tag_realm(x)
            pr = REALM_OF_NAME.get(p.get('realm'))
            dfz = any(zkey(h) in ('darknessfalls', 'summonershall') for h in p.get('habitats') or [])
            if ir and tr and tr != ir:
                continue
            if ir and not tr and pr and pr != ir and not dfz:
                continue
            out.append(f"period-zam mob {p['key']} '{p['name']}' (capture {p['timestamp'][:8]}) Known Loot '{x}'")
        return out

    # the stats agent (fix/stats/items.json) fixes or strips the post-1.65 bonus types of every item in its scope;
    # only items outside it would keep them
    sp = os.path.join(AUDIT, 'fix', 'stats', 'items.json')
    stats_scope = {x['id'].lower() for x in jload(sp)} if os.path.exists(sp) else set()

    def later_bonus(item):
        if item['Id_nb'].lower() in stats_scope:
            return []
        return [item[f'Bonus{k}Type'] for k in range(1, 11) if item[f'Bonus{k}Type'] in TOA] +             ([item['ExtraBonusType']] if item.get('ExtraBonusType') in TOA else [])

    period_loot_override = {}      # REMOVE / REVIEW items a period mob page lists as loot -> evidence
    period_loot_blocked = {}       # ... but whose template carries post-1.65 bonus types (stay removed)
    for iid, a in A.items():
        it = W.items.get(iid)
        if not it or not (a['action'].startswith('REMOVE') or a['action'].startswith('REVIEW')):
            continue
        kl = known_loot(it)
        if kl:
            if later_bonus(it):
                period_loot_blocked[it['Id_nb']] = kl[:2] + [f'post-1.65 bonus types {later_bonus(it)}']
            else:
                period_loot_override[iid] = kl

    def uth_only(a):
        """owner ruling 2026-10-31: Uthgard items are not freeshard; the audit left 6 "Uthgard-only" items on REMOVE
        (kept unless the template carries post-1.65 bonus types)"""
        it = W.items.get(a['item_id'].lower())
        return (a['evidence'].endswith('Uthgard-only') and norm(a['name']) in S.uth_item_rates and it is not None
                and not later_bonus(it))

    out_uth_only = sorted(a['item_id'] for a in A.values() if a['action'].startswith('REMOVE') and uth_only(a))
    base = [r for r in W.loot if r['LootTemplate_ID'] not in W.period_added]
    out = {'remove_items': {}, 'remove_pairs': {}, 'killtask': [], 'review': [], 'dangling': [], 'dead_mobx': [],
           'kept': collections.Counter(), 'notes': []}

    # ---- C. the 127 REVIEW items -------------------------------------------------------------------------------
    review_keep = set()
    for iid, a in sorted(A.items()):
        if not a['action'].startswith('REVIEW'):
            continue
        it = W.items.get(iid)
        realms = {0, 1, 2, 3}
        rows = [r for r in base if r['ItemTemplateID'].lower() == iid]
        if rows:
            realms = set()
            for r in rows:
                realms |= realms_of(r['TemplateName'])
        ev = list(dict.fromkeys((period_loot_override.get(iid, []) + item_known(it, realms)))) if it else []
        early = [e for e in ev if e.startswith('live-zam item') or e.startswith('period') or e.startswith('Uthgard')]
        keep = bool(early)
        if keep and it and later_bonus(it):
            keep = False
            ev = ev + [f"but the template carries post-1.65 bonus types {later_bonus(it)} and no period item page or "
                       f"Uthgard item gives its 1.65 stats"]
        if keep:
            review_keep.add(iid)
        out['review'].append({'item_id': it['Id_nb'] if it else iid, 'name': a['name'], 'package': a['package'],
                              'mobs': a['mobs'], 'decision': 'KEEP' if keep else 'REMOVE',
                              'evidence': '; '.join(ev) if ev else 'no period page, no Uthgard item or drop, no live '
                                                                     'Allakhazam comment before 1 Nov 2003'})

    # ---- A/B. every base row -----------------------------------------------------------------------------------
    for r in base:
        t, iid, ch = r['TemplateName'], r['ItemTemplateID'].lower(), r['Chance']
        it = W.items.get(iid)
        pair = f"{t.lower()}|{iid}"
        a = A.get(iid)
        act = a['action'] if a else None
        if it is None:
            out['dangling'].append({'template': t, 'item_id': r['ItemTemplateID'], 'chance': ch,
                                    'pages': [f"{p['key']} {p['name']} ({p['realm']})" for p in S.period_item_pages(r['ItemTemplateID'].replace('_', ' '))]})
            continue
        pkg = (it['PackageID'] or '').lower()
        if iid in SEALS:
            out['kept']['Darkness Falls seal (left as is)'] += 1
            continue
        if act and act.startswith('REMOVE') and not uth_only(a) and iid not in period_loot_override:
            out['remove_items'].setdefault(it['Id_nb'], f"item_actions REMOVE: {a['evidence']}")
            continue
        if act and act.startswith('REVIEW') and iid not in review_keep:
            out['remove_items'].setdefault(it['Id_nb'], 'REVIEW item without period evidence (see review_decisions.csv)')
            continue
        if pkg == 'atlas_trophys' or re.search(r'trophy', it['Name'], re.I) and it['Object_Type'] in (50, 51):
            out['remove_items'].setdefault(it['Id_nb'], f"Atlas boss trophy (package {it['PackageID']}, Object_Type "
                                                        f"{it['Object_Type']}): trophies/taxidermy are 2004+ (ToA); Atlas loot")
            continue
        if ch < 0:
            # Atlas timed XP-item drop: keep only period items (period page / mob Known Loot) or kept REVIEW items
            ev = backing(t, it)
            pages = S.period_item_pages(it['Name'], realms_of(t))
            l1 = [e for lv, e in ev if lv == 'L1' and e.startswith('period')]
            if pages or l1 or iid in review_keep:
                out['killtask'].append({'template': t, 'item_id': r['ItemTemplateID'], 'name': it['Name'], 'mobs': mobs_of(t),
                                        'period': [f"period-zam item {p['key']} '{p['name']}' (capture {p['capture'][:8]})"
                                                   + (' Kill Task item' if p.get('quest_item') else '') for p in pages] + l1,
                                        'review_kept': iid in review_keep, 'evidence': [e for _, e in ev]})
            else:
                out['remove_pairs'][pair] = (f"Atlas timed XP-item drop (Chance {ch}): {it['Name']} has no period item page "
                                             f"and is not on the monster's period Known Loot")
            continue
        if not a:
            # an item the audit did not grade: does any source know it?
            ev = item_known(it, realms_of(t))
            if not ev:
                out['remove_items'].setdefault(it['Id_nb'], f"no source knows the item (no period page, no Uthgard item or drop, "
                                                            f"no live Allakhazam comment before 1 Nov 2003); package {it['PackageID']}")
                continue
        if pkg in ATLAS_PKGS:
            ev = backing(t, it)
            if not ev:
                out['remove_pairs'][pair] = (f"Atlas-era loot row (item package {it['PackageID']}): no period or Uthgard source "
                                             f"says {', '.join(mobs_of(t))} dropped {it['Name']}")
                continue
            out['kept'][f'Atlas-package row backed ({ev[0][0]})'] += 1
            continue
        out['kept']['non-Atlas row, item known'] += 1

    # every REMOVE item of item_actions and every rejected REVIEW item, also those that only the pass-1 period drops
    # (replaced anyway) or no row uses today: the rules then also stop a later file from bringing them back
    for iid, a in A.items():
        it = W.items.get(iid)
        idnb = it['Id_nb'] if it else a['item_id']
        if a['action'].startswith('REMOVE') and not uth_only(a) and iid not in period_loot_override:
            out['remove_items'].setdefault(idnb, f"item_actions REMOVE: {a['evidence']}")
        elif a['action'].startswith('REVIEW') and iid not in review_keep:
            out['remove_items'].setdefault(idnb, 'REVIEW item without period evidence (see review_decisions.csv)')

    # rows whose pair is removed but whose item is removed anyway: drop the pair (item rule covers it)
    rem_ids = {k.lower() for k in out['remove_items']}
    out['remove_pairs'] = {k: v for k, v in out['remove_pairs'].items() if k.split('|', 1)[1] not in rem_ids}

    # ---- the dead MobXLootTemplate rows (LootTemplateName with no LootTemplate rows, not the mob's own name) ------
    tn = {r['TemplateName'].lower() for r in W.loot}
    for m in W.mobx:
        if m['LootTemplateName'].lower() != m['MobName'].lower() and m['LootTemplateName'].lower() not in tn:
            out['dead_mobx'].append({'mob': m['MobName'], 'template': m['LootTemplateName'], 'id': m['MobXLootTemplate_ID']})

    # ---- LootOTD check ------------------------------------------------------------------------------------------
    for o in W.otd:
        iid = o['ItemTemplateID'].lower()
        a = A.get(iid)
        if iid not in W.items or (a and (a['action'].startswith('REMOVE') or a['action'].startswith('REVIEW') and iid not in review_keep)):
            out['notes'].append(f"LootOTD {o['MobName']} -> {o['ItemTemplateID']}: "
                                f"{'missing item' if iid not in W.items else a['action']}")
    out['uthgard_only_kept'] = out_uth_only
    out['period_loot_override'] = {W.items[k]['Id_nb']: v for k, v in period_loot_override.items()}
    out['period_loot_blocked'] = period_loot_blocked
    jdump(out, os.path.join(WORK, 'base_decisions.json'))
    print('remove items', len(out['remove_items']), 'remove pairs', len(out['remove_pairs']), 'killtask', len(out['killtask']),
          'review keep', len(review_keep), 'of', len(out['review']), 'dangling', len(out['dangling']), 'dead mobx',
          len(out['dead_mobx']))
    print(out['kept'])
    for n in out['notes']:
        print(n)


if __name__ == '__main__':
    main()
