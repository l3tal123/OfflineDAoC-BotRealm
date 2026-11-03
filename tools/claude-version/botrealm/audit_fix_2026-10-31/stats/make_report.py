"""stats agent: numbers + lists for REPORT.md (reads items.json / updates.json written by fix_stats.py)."""
import collections, json, os, re, sys

sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
R = json.load(open(os.path.join(HERE, 'items.json'), encoding='utf-8'))
U = json.load(open(os.path.join(HERE, 'updates_final.json'), encoding='utf-8'))
out = {}

dec = collections.Counter(r['decision'] for r in R)
out['decisions'] = dict(dec)
ch = [r for r in R if 'changes' in r]
out['statements'] = len(U)
out['by_source_reach'] = {s: {'drops': sum(1 for r in ch if r['source'] == s and r['dropped_by']),
                              'merchant': sum(1 for r in ch if r['source'] == s and r['merchant']),
                              'neither': sum(1 for r in ch if r['source'] == s and not r['dropped_by'] and not r['merchant']),
                              'all': sum(1 for r in ch if r['source'] == s)} for s in ('period', 'uthgard', 'strip')}
LATER = set(range(99, 120)) | set(range(145, 257))
rem = collections.Counter()
kept = collections.Counter()
for r in ch:
    b = {}
    for c, (old, new) in r['changes'].items():
        pass
    olds = [(r['changes'].get(f'Bonus{k}Type', [None])[0], r['changes'].get(f'Bonus{k}', [None])[0]) for k in range(1, 11)]
    for n in re.findall(r'(\w+) [+-]\d+', r['before']):
        pass
names_before = collections.Counter()
names_after = collections.Counter()
for r in ch:
    for m in re.finditer(r'([A-Za-z_()]+) ([+-]\d+)', r['before']):
        names_before[m.group(1)] += 1
    for m in re.finditer(r'([A-Za-z_()]+) ([+-]\d+)', r['after']):
        names_after[m.group(1)] += 1
later_names = ['Skill_Archery', 'Acuity', 'AllMagicSkills', 'AllMeleeWeaponSkills', 'AllFocusLevels',
               'AllDualWieldingSkills', 'AllArcherySkills', 'PowerPool', 'ArmorFactor', 'MeleeSpeed', 'CastingSpeed',
               'ArcherySpeed', 'MeleeDamage', 'SpellDamage', 'StyleDamage', 'BuffEffectiveness', 'HealingEffectiveness',
               'Fatigue', 'ExtraHP', 'StrCapBonus', 'DexCapBonus', 'ConCapBonus', 'QuiCapBonus', 'IntCapBonus',
               'PieCapBonus', 'EmpCapBonus', 'ChaCapBonus', 'AcuCapBonus', 'MaxHealthCapBonus', 'PowerPoolCapBonus',
               'SpellRange', 'ResistPierce', 'SpellDuration', 'BladeturnReinforcement', 'PieceAblative',
               'ReactionaryStyleDamage', 'EvadeChance', 'BlockChance', 'ArrowRecovery', 'AllSkills']
out['later_types_before_after'] = {n: [names_before[n], names_after[n]] for n in later_names if names_before[n]}
out['kept_attested'] = [(r['id'], r['name'], r['after'], r['source_key']) for r in ch
                        if any(x.startswith('kept because') for x in r['notes'])]
out['apr28'] = [(r['id'], r['decision'], r['apr28_uthgard_agrees']) for r in R if r.get('apr28_uthgard_agrees')]
out['kept_epic_dragon'] = [(r['id'], r['name'], r['decision'], r['kept_epic_dragon_types']) for r in R if r.get('kept_epic_dragon_types')]
out['apr28_pages_used'] = sum(1 for r in ch if r['source'] == 'period' and 'Apr 28 12:12:32 2003' in r['source_key'])
out['uth_empty'] = [(r['id'], n) for r in R for n in r.get('notes', []) if 'not proof the item had none' in n and n.startswith('Uthgard')]
out['stale_note'] = [(r['id'], n) for r in R for n in r.get('notes', []) if 'stale by its own patch note' in n]
out['archaic'] = [(r['id'], n[:160]) for r in R for n in r.get('notes', []) if 'pre-rename resist names' in n]
out['unmapped'] = [(r['id'], n) for r in R for n in r.get('notes', []) if 'no server property' in n or 'unmapped bonus' in n]
out['empty_bulk'] = [(r['id'], r['decision']) for r in R for n in r.get('notes', []) if 'never edited after' in n]
out['incomplete'] = [(r['id'], n) for r in R for n in r.get('notes', []) if 'incomplete' in n and 'never edited' not in n and not n.startswith('Apr-28')]
out['variants'] = [(r['id'], n) for r in R for n in r.get('notes', []) if 'same-name variant' in n]
out['rejected_type'] = [(r['id'], r['decision'], r['period_pages_rejected']) for r in R if r.get('period_pages_rejected')
                        and r.get('source') != 'period']
out['spell_not_on_page'] = [(r['id'], r['name'], r['spell_not_on_page'], r['source_key']) for r in R if r.get('spell_not_on_page')]
out['ability_missing'] = [(r['id'], n) for r in R for n in r.get('notes', []) if n.startswith('page lists a magical ability')]
out['archery_renamed'] = [(r['id'], n) for r in R for n in r.get('notes', []) if n.startswith('unified Archery')]
out['level_changes'] = [(r['id'], r['changes']['Level'], r['source']) for r in ch if 'Level' in r['changes']]
out['unchanged'] = [(r['id'], r['name'], r['decision'], r['scope'], r.get('notes')) for r in R
                    if r['decision'].startswith(('no source', 'above its period page'))]
out['remove_listed_updated'] = [r['id'] for r in ch if r['remove_listed']]
strip_reach = [(r['id'], r['name'], r['before'], r['after'], r['dropped_by'][:3], r['merchant'], r['package'])
               for r in ch if r['source'] == 'strip' and (r['dropped_by'])]
out['strip_dropped'] = strip_reach
out['strip_pkg'] = collections.Counter(str(r['package']) for r in ch if r['source'] == 'strip').most_common()
out['strip_merchant'] = sum(1 for r in ch if r['source'] == 'strip' and r['merchant'])
pref = re.compile(r'^(\d_|shining|bright|brilliant|brillinat|luminous|shimmering|glowing|radiant|sparkling)', re.I)
out['prefixed_copies'] = sum(1 for r in ch if r['source'] == 'period' and pref.match(r['id']))
json.dump(out, open(os.path.join(HERE, 'report_data.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
for k, v in out.items():
    print(k, v if not isinstance(v, list) else (len(v), v[:4]))
