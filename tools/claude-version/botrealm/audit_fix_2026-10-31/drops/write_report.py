"""drops agent: REPORT.md from the work files (no database access)."""
import collections, csv, json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import HERE, WORK, AUDIT, jload

D = jload(os.path.join(WORK, 'base_decisions.json'))
T = jload(os.path.join(WORK, 'test_result.json'))
R = jload(os.path.join(WORK, 'drops_report.json'))
K = jload(os.path.join(WORK, 'killtask_report.json'))
B = jload(os.path.join(WORK, 'built_items.json'))
BF = jload(os.path.join(WORK, 'build_fail.json'))
CO = jload(os.path.join(WORK, 'coin_only.json'))
UC = jload(os.path.join(WORK, 'unfilled_counts.json'))
OLD = jload(os.path.join(WORK, 'old_drops_errors.json'))
COV = jload(os.path.join(WORK, 'coverage.json')) if os.path.exists(os.path.join(WORK, 'coverage.json')) else None
rules = jload(os.path.join(HERE, 'where_rules.json'))
drops = jload(os.path.join(HERE, 'drops165.json'))['monsters']
add = jload(os.path.join(HERE, 'add-dropitems165.json'))['rows'] if os.path.exists(os.path.join(HERE, 'add-dropitems165.json')) else []
unf = list(csv.DictReader(open(os.path.join(HERE, 'unfilled.csv'), encoding='utf-8')))

L = []
w = L.append
rows = sum(len(v['items']) for v in drops.values())
src = collections.Counter()
est = collections.Counter()
cls = collections.Counter()
for r in R:
    cls[r['class']] += 1
    for x in r['rows']:
        s0 = x['sources'][0]
        k = ('Uthgard measured (this monster)' if s0.startswith('Uthgard measured') else
             'seal kept from pass 1' if s0.startswith('Darkness Falls seal') else
             'period mob Known Loot' if 'Known Loot' in s0 and s0.startswith('period-zam mob') else
             'period item page names the monster' if s0.startswith('period-zam item') else
             'live Allakhazam mob page (pre-Nov-2003 comment) + period item page' if s0.startswith('live-zam') else 'other')
        src[k] += 1
        if 'rate: estimated' in s0:
            est[s0.split('rate: estimated: ')[1]] += 1
        elif 'rate: Uthgard rate of' in s0:
            est['Uthgard rate of the item on another monster'] += 1

w('# True 1.65 fix pass: monster drops (drops agent, 2026-10-31)\n')
w('Everything here was tested on a copy of the audit snapshot (`fix/drops/test/world_test.db`), applied in the '
  "launcher's order (true165-rules settings + remove rules incl. `where_rules.json`, updates incl. `updates.json`, "
  'old period drops deleted and the new `drops165.json` added, then every installed `add-*.json` plus '
  '`add-dropitems165.json`). Nothing live was touched.\n')
w('## Files\n')
w('| file | what |\n|---|---|')
w(f'| `where_rules.json` | {len(rules)} `remove.where` rules for true165-rules.json (archive, not delete) |')
w(f'| `updates.json` | {len(jload(os.path.join(HERE, "updates.json")))} idempotent UPDATEs (kill-task items: Chance -3 -> %) |')
w(f'| `drops165.json` | full replacement: {len(drops)} monsters, {rows} item rows |')
w(f'| `add-dropitems165.json` | {len(add)} ItemTemplate rows built from period item pages (Id_nb `p165_drop_<page key>`) |')
w('| `review_decisions.csv` | the 127 REVIEW items, KEEP / REMOVE with the evidence |')
w(f'| `unfilled.csv` | {len(unf)} lines: loot a source names that could not be given (and why) + period items no source ties to a monster |')
w('| `work/` | caches and per-row evidence: `drops_report.json` (every drops165 row with its sources), `base_decisions.json`, '
  '`killtask_report.json`, `built_items.json` (what each built value came from), `coin_only.json`, `test_result.json` |')
w('| scripts | `prep_live.py`, `common.py`, `sources.py`, `analyze_base.py`, `build_drops.py`, `build_outputs.py`, `test_apply.py`, `write_report.py` (run with `python -I`, in that order) |\n')

w('## Test result\n')
r1, r2 = T['run 1'], T['run 2']
w(f"- every statement ran: {'yes' if not r1['errors'] and not r2['errors'] else 'NO: ' + '; '.join(r1['errors'] + r2['errors'])}")
w(f"- loot rows pointing at a missing item after the run: {len(T['checks']['loot rows pointing at a missing item'])} "
  f"(before: 42); LootOTD rows with a missing item: {len(T['checks']['OTD rows pointing at a missing item'])}")
w(f"- Chance < 0 rows left: {T['checks']['Chance < 0 rows left']} (before: 247); dead MobXLootTemplate links left: "
  f"{T['checks']['dead MobXLootTemplate rows (other template, no rows)']} (before: 216)")
w(f"- second refresh: loot / item content unchanged: {'yes' if T['second run: loot/item content unchanged'] else 'NO'} "
  f"(LootTemplate and MobXLootTemplate content, every ItemTemplate row, Mob and MerchantItem counts); rows my rules "
  f"archived: {T['second run: rows my rules archived']}; archive growth on run 2 by reason: {T['archived in run 2 by reason']} "
  "(pre-existing loop, see 'For the coordinator')")
w('- archived by my rules on run 1 / run 2: ' + ', '.join(
    f"{k.split('(')[1].rstrip(')')} {v}/{r2['remove'].get(k)}" for k, v in r1['remove'].items() if k.startswith('drops where')))
w(f"- first run: {r1['old period rows deleted']} old period-drop rows replaced; drops165 (rows, new MobXLootTemplate rows, "
  f"raised DropCounts) = {r1['drops165 (rows, new mobx, raised dropcount)']}; second run "
  f"{r2['drops165 (rows, new mobx, raised dropcount)']}")
w(f"- loot rows by item package after the run: " + ', '.join(f'{p or "(none)"} {n}' for p, n in T['checks']['loot rows by item package']))
w(f"- **spawned monsters with coin only (no item at all): {T['coin-only spawned monsters']}** "
  f"(before the fix: {T.get('coin-only before')}); {T['coin-only named']} of them have a capitalised (named) name. "
  'List: `work/coin_only.json` (top of the list below).\n')

w('## A. Removed (where rules)\n')
cnt = collections.Counter()
for v in D['remove_items'].values():
    cnt[v.split(':')[0][:60]] += 1
w(f"Items whose every loot row goes ({len(D['remove_items'])} items):")
for k, v in cnt.most_common():
    w(f'- {v}: {k}')
w(f"\nLoot rows removed by monster+item key ({len(D['remove_pairs'])}):")
c2 = collections.Counter(v.split(':')[0] for v in D['remove_pairs'].values())
for k, v in c2.most_common():
    w(f'- {v}: {k}')
w(f"- the Chance < 0 rule is a pattern (`Chance < 0` and not one of the {len(D['killtask'])} kept kill-task rows), so any "
  'Atlas timed XP-item row that comes back later is caught too.')
w(f"\nDangling loot rows (item template does not exist): {len(D['dangling'])} removed (Apocalypse x13, Xanxicar x13, "
  "Weno'iak x4 ...); where a period page or Uthgard names the item for that monster, drops165 gives the existing or "
  'built item instead.')
w(f"Dead MobXLootTemplate links: {len(D['dead_mobx'])} removed (exact MobName|LootTemplateName keys, and only while the "
  'link still has no LootTemplate rows).\n')
w('Kept (not freeshard): ' + ', '.join(f'{v} {k}' for k, v in D['kept'].items()) + '. "L1" = period mob page / period item '
  'page / Uthgard names this monster; "L2" = live Allakhazam mob page with a pre-Nov-2003 comment; "L3" = period item '
  'page found in the monster\'s zone and drops off monsters, no dropper named (weakest; kept, not proof).\n')
w('Items no source knows (removed, judgement: also checked for spelling variants with difflib >= 0.93):')
w(', '.join(sorted(k for k, v in D['remove_items'].items() if v.startswith('no source'))) + '\n')

w('## B. Kill-task items: Chance -3 -> normal % drop (updates.json)\n')
w('| monster | item | chance | rate from |\n|---|---|---|---|')
for k in K:
    w(f"| {k['template']} | {k['name']} | {k['chance']} | {k['rate_source']} |")
w('\nJudgement: "period kill-task item" = the item has a realm-matching period item page or is on the monster\'s period '
  'Known Loot (26 rows), plus 3 kept REVIEW items on a Chance -3 row (Cockatrice Feather, Crystallized Darkfire, Unused '
  'Arrowhead: spelling variants of period / Uthgard pages). In 1.65 kill-task items dropped only while the player had the task; there is no task flag in the loot '
  'tables, so they become plain % drops as the brief asks. The Uthgard-backed Chance -3 items that are not period '
  '(e.g. Vial of Shimmering Essence) lose the timed row and come back only through drops165 when Uthgard measured them '
  'on that monster.\n')

w('## C. The 127 REVIEW items\n')
keep = [r for r in D['review'] if r['decision'] == 'KEEP']
w(f"{len(keep)} KEEP, {len(D['review']) - len(keep)} REMOVE (all in `review_decisions.csv`). Kept:")
for r in keep:
    w(f"- {r['item_id']} ({r['name']}): {r['evidence']}")
w('\nThe live Allakhazam archive holds item pages with their comments (the separate item_comments file has only 2 pages); a '
  'comment is "early" when its posted date is before 2003-11-01. REMOVE = no early comment, no period page, no Uthgard '
  'item page or drop, also under spelling variants.\n')

w('## D. New drops165.json\n')
w(f"{len(drops)} monsters ({cls['regular']} regular, {cls['named']} named, {cls['raid']} raid/epic), {rows} rows.")
w('Rows by first source: ' + ', '.join(f'{k} {v}' for k, v in src.most_common()) + '.')
w('Rates not measured on that monster: ' + ', '.join(f'{k} {v}' for k, v in est.most_common()) + ' (each row says which in '
  '`work/drops_report.json`).\n')
w('Fixes against pass 1 (2026-10-30):')
w(f"- pass 1 had {len(OLD['bad_realm'])} rows whose item belongs to a realm whose players never meet that monster "
  f"(they could never drop, e.g. Albion dyes on Midgard monsters); new file: 0. Uthgard records are used only when their "
  f"zone is in a realm (classic part) the monster lives in; 32 such records were skipped (e.g. Uthgard Albion zones for a "
  f"name that only lives in Midgard). Names in several realms (27, e.g. impling, manes demon, tree spirit) get each "
  f"realm's Uthgard drops with that realm's items; realm-0 items (gems, dyes) from one realm also reach the other realm's "
  f"spawns of the same name (one shared table per name).")
w('- kept: min kills 20, ROG section and coin bags ignored, nearest-level template, DropCount only raised.')
w('- Darkness Falls currency seals: the 7 Daemon seal rows pass 1 gave DF monsters (demoniac familiar, 3 lieutenants, '
  'soultorn grove protector / isen herra, tormented necyomancer) are carried over unchanged; no other seals added.')
w(f"- Removed-spawn names (the 2026-08-29 conversion removed them; the spawns agent may restore some): their drops are "
  f"in the file too ({sum(1 for r in R if r['where'] == 'removed spawn')} monsters), so a restored spawn has loot. "
  f"Script-spawned bosses without Mob rows (Apocalypse and the horsemen, corrupt primals, Tuscaran elders ...): "
  f"{sum(1 for r in R if r['where'] in ('script', 'period habitat'))} monsters.")
w('- Estimates (only when no Uthgard number exists for the item anywhere): named magical 15 %, raid/epic 25 %, regular '
  'magical 3 %, plain loot 20 %; drop_count named 2, raid 4, regular 1 (2 when the rolled chances add up to 50 %+). '
  'Raid/epic = dragons, Legion, DF princes/high lords/Adremal/Nahemah, Govannon, epic-dungeon (Caer Sidi, Tuscaran, '
  'Galladoria, Summoner\'s Hall) named, any named of level 62+. Named = the named-mob audit\'s list (CapnBry case) or a '
  'capitalised script name.')
w(f"- Built items: {len(add)} rows from period item pages with `build_vendoritems165.build` (stats from the page, look "
  f"from the nearest sibling; price from the page, else the sibling's sell value), Name without Allakhazam's tag, "
  f"PackageID True165Drop. Gems a one-word name has no sibling for (Sphene, Bloodstone ...) take the look of an existing "
  f"Realm-0 gem and the price from Uthgard's sell value x 2 (item_sell_ratio 50). {len(BF)} pages could not be built "
  f"(no sibling, no DPS/AF on the page, quest items): see `work/build_fail.json` and `unfilled.csv`.")
w(f"- Not covered: of the {UC['period_items_total']} period items that drop off monsters but dropped from nothing, "
  f"{UC['period_items_total'] - UC['period_items_still_unassigned']} now drop from a monster a source names; "
  f"{UC['period_items_still_unassigned']} stay unassigned (listed in unfilled.csv, kind 'period item, no monster named').")
if COV:
    w(f"- Audit drop_missing findings: {COV['summary']}")
w('')
w('Darkness Falls seals (not changed, evidence for the separate decision): apprentice necyomancer has Daemon Emerald Seal '
  '50 % x2 on the server, but two period comments on its page (period-zam mob 2821) say "they dont drop seals" '
  '(2002-09-25) and "i did not get any seals from them" (2002-07-31); Uthgard measured Emerald Seal 29.9 % of 174 kills. '
  'Molochian tempter has Daemon Sapphire Seal 40 % x3; period comment 2003-06-16 (period-zam mob 2832): "every time they '
  'dropped a seal it was emerald. So the lower ones drop emerald seals", 2002-08-01: sapphire from the purple-con ones; '
  'Uthgard: Sapphire 41 % of 1,994 kills, no emerald. (audit dungeons-116, dungeons-225)\n')

w('## Overrides of the audit grades\n')
w(f"- {len(D['period_loot_override'])} items the audit graded REMOVE/REVIEW are on a realm-matching period mob page's Known "
  "Loot (a period source the item grader did not use), so they stay and drop where that page says: "
  + ', '.join(sorted(D['period_loot_override'])) + '.')
w(f"- {len(D['period_loot_blocked'])} such period-loot items stay removed: their template carries post-1.65 bonus types and "
  "they are outside the stats agent's scope (fix/stats/items.json), so nothing would fix them: "
  + '; '.join(f"{k} ({v[-1]})" for k, v in D['period_loot_blocked'].items())
  + '. If the stats agent takes them in, drop them from where rule 0/1 and rerun build_drops.py.')
w(f"- {len(D['uthgard_only_kept'])} items the audit left on REMOVE with the evidence 'Uthgard-only' are kept (owner ruling: "
  "Uthgard is not freeshard; Uthgard measured them on the Darkness Falls bosses): " + ', '.join(D['uthgard_only_kept']) + '.')
w('- A REVIEW item with an early live comment but post-1.65 bonus types outside the stats scope is REMOVE (Dragonfury).\n')
w('## For the coordinator\n')
w("- Pre-existing loop (not my files): every refresh archives 3 Mob rows (Brynhild, Eilwen, Adalyn) with the mob_names rule "
  "'Realm Exchange moves to the traveler's inns' and add-inns165.json inserts them again; the only change a second refresh makes.")
lo = jload(os.path.join(WORK, 'option_live_zone.json'))
w(f"- Option not applied (the brief counts live pages only with a pre-Nov-2003 comment): {lo['mobs']} coin-only monsters would "
  f"get {lo['rows']} rows if a live Allakhazam mob page WITHOUT an early comment counted when a period item page puts the same "
  "item in that zone as a monster drop.")
w('- drops165 only points at templates, so its rows get the stats agent\'s fixes; the built items come from period pages and '
  'carry only 1.65 bonus types (map_bonuses maps nothing later).\n')
w('## Judgement calls\n')
for j in [
    'Where rules use exact lower-case keys (item ids, monster|item pairs, mob|template pairs) so they keep matching after a '
    'rebuild from the normal database and cannot hit the new drops165 / add rows (the builder never emits a removed item '
    'or pair). The Chance < 0 rule is a pattern on purpose.',
    'Atlas-package rows (atlas_orbs_item / drmed) are removed only when no source ties the item to that monster; the 256 '
    'L3 rows (period page found in the zone, no dropper named) are kept - removing them would empty many SI epic and '
    'classic named tables.',
    'Public_DB / unpackaged base rows of items a source knows stay as they are (their chances are the OpenDAoC values, '
    'e.g. 99 % rows on SI epic bosses); only freeshard rows were in scope.',
    'Period Known Loot matching: name (+plural/singular), realm, habitat zone must be one of the spawn zones when the page '
    'names a known zone; Darkness Falls / Summoner\'s Hall / battleground pages match on name only (their page realm is '
    'arbitrary). A tag like "(Alb)" on a Known Loot line picks that realm\'s item; several templates of one name: the one '
    'whose type/AF/DPS/bonuses match the period page, then one already used in loot, then the nearest level.',
    'Live Allakhazam mob pages count only with a comment dated before 1 Nov 2003, and only for items that have a period '
    'item page saying they drop off monsters (the live loot lists include ToA "(nld)" items).',
    'Item spelling variants (difflib >= 0.96 with the same last word, or >= 0.93 with matching type/AF/DPS) count as the '
    'same item (e.g. Luminescent Exergum / Exeregum Stone, Legend Slayer\'s / Slayer Quiver).',
    'Gjalpinulva gets both respec stones at 100 % like the other two dragons (her period page lists both; the rate is the '
    'Uthgard rate measured on Golestandt/Cuuldurach, the same rows they have).',
    'Classic quest items (a cq_ template of the name) and kill-task pages are not built as general loot.',
    'Old pass-1 DropCount raises stay (the launcher only raises DropCount); where a pass-1 table was wrong the extra cap '
    'only allows more of the (now correct) rolled items.',
    'Uthgard drops whose item has no template and no period page (small/large chest, some copper coins, cat\'s eye '
    'apatite, food...) are not invented; they are listed (unfilled.csv, kind Uthgard drop).',
]:
    w(f'- {j}')
w('')
w('## Coin-only spawned monsters (first 60 by spawn count)\n')
w('| monster | spawns | level | zones |\n|---|---|---|---|')
for x in CO[:60]:
    w(f"| {x['mob']} | {x['spawns']} | {x['level']} | {', '.join(x['zones'])} |")
w(f"\n({len(CO)} in all; most are monsters no source gives loot for: Uthgard never measured them in that realm and the "
  'period page lists none.)\n')
w('## Unfilled (summary)\n')
uc = collections.Counter(u['kind'] for u in unf)
for k, v in uc.most_common():
    w(f'- {k}: {v}')
top = collections.Counter(u['item'] for u in unf if u['kind'] == 'Uthgard drop')
w('- most frequent Uthgard items without a template or period page: ' + ', '.join(f'{k} ({v})' for k, v in top.most_common(15)))
open(os.path.join(HERE, 'REPORT.md'), 'w', encoding='utf-8').write('\n'.join(L) + '\n')
print('REPORT.md', len(L), 'lines')
