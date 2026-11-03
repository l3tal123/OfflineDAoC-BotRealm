"""drops agent: how many of the audit's drop_missing findings the fix covers (on the tested copy)."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sources import *

DBT = os.path.join(HERE, 'test', 'world_test.db')


def main():
    W = World(DBT)
    S = Sources(W)
    lt = collections.defaultdict(set)
    for r in W.loot:
        it = W.items.get(r['ItemTemplateID'].lower())
        if it:
            lt[r['TemplateName'].lower()].add(norm(strip_suffix(it['Name'])))
    mx = collections.defaultdict(list)
    for m in W.mobx:
        mx[m['MobName'].lower()].append(m['LootTemplateName'].lower())
    res = collections.Counter()
    detail = []
    seen = set()
    for f in ('named', 'raids', 'dungeons', 'alb', 'mid', 'hib', 'si', 'items', 'global'):
        for x in jload(os.path.join(AUDIT, 'out', f + '.json'))['findings']:
            if x['category'] != 'drop_missing' or x['likelihood'] not in ('high', 'moderate'):
                continue
            m = (x.get('mob') or '').lower()
            if m.startswith('(') or not m or m in seen:
                continue
            seen.add(m)
            pages = S.period_pages_for_mob(m)
            want = {norm(strip_suffix(l)) for p, _ in pages for l in p.get('loot') or []}
            have = set()
            for t in mx.get(m, []) + [m]:
                have |= lt.get(t, set())
            if not S.info(m)[0]:
                res['monster not spawned / unknown'] += 1
                continue
            if not want:
                res['no period Known Loot to compare (finding rests on item pages / Uthgard)'] += 1 if have else 0
                res['no period Known Loot and still no items'] += 0 if have else 1
                continue
            got = want & have
            if got == want:
                res['all period Known Loot now drops'] += 1
            elif got:
                res['part of the period Known Loot now drops'] += 1
            else:
                res['none of the period Known Loot drops'] += 1
            if got != want:
                detail.append({'mob': m, 'finding': x['id'], 'missing': sorted(want - got)})
    summ = '; '.join(f'{k} {v}' for k, v in res.most_common() if v)
    jdump({'summary': f'{len(seen)} monsters with HIGH/MODERATE drop_missing findings: ' + summ, 'detail': detail},
          os.path.join(WORK, 'coverage.json'))
    print(summ)


if __name__ == '__main__':
    main()
