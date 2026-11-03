"""Probe: items that lost bonuses the 1.65 period pages give them (owner 2026-11-01: merchant caster staves had no focus
at all; a sample of 3,181 items found 73 with no bonuses and 39 with fewer - skills like Instruments / Smiting, some
resists, and focus on dropped staves).

Reads the archived Allakhazam item pages (Wayback captures up to 2004-12-07 first; the live archive only with
--allow-live), maps every bonus name to the server's eProperty with the server's own tables (SkillBase property names,
spec -> skill, spec -> focus) plus the period display names of the spell lines, and compares with the world DB.
Read-only on the DB and the archive. Writes, next to this file:
  missing_bonuses165.json   every item with missing (or lower) bonuses: id, name, in use, period values, source page
  missing_bonuses165.csv    the same, one line per item
  missing_bonuses165_updates.json   proposed {"sql","reason"} entries for true165-rules.json "updates" (NOT installed:
                            the owner reviews first)
Never proposes Trials of Atlantis or later bonus types (eProperty 99-119, 145-256, as the 2026-10-31 stats audit).

Usage: python -I probe_missing_bonuses165.py [--sample N] [--allow-live]
  --sample N   read about N period pages, picked at random (a quick check); default: every page"""
import csv, json, os, random, re, sqlite3, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
PD = os.path.join(ROOT, 'development-source', 'research', 'period-data')
SRV = os.path.join(ROOT, 'development-source', 'server', 'GameServer')
DB = os.path.join(ROOT, 'runtime', 'data', 'opendaoc-165.sqlite3.db')
sys.path.insert(0, PD)
from read_archive import records  # noqa: E402

args = sys.argv[1:]
sample = int(args[args.index('--sample') + 1]) if '--sample' in args else 0
allow_live = '--allow-live' in args
REALMS = {'Albion': 1, 'Midgard': 2, 'Hibernia': 3}

# ---- the server's own names and tables
enum = {m.group(1): int(m.group(2)) for m in
        re.finditer(r'^\s*(\w+)\s*=\s*(\d+)', open(os.path.join(SRV, 'Enums', 'eProperty.cs'), encoding='utf-8').read(), re.M)}
sb = open(os.path.join(SRV, 'gameutils', 'SkillBase.cs'), encoding='utf-8', errors='replace').read()
en = {}
for line in open(os.path.join(SRV, 'language', 'EN', 'SkillHandler.txt'), encoding='utf-8', errors='replace'):
    m = re.match(r'SkillBase\.RegisterPropertyNames\.(\w+):\s*(.+?)\s*$', line)
    if m:
        en[m.group(1)] = m.group(2)
DISPLAY = {en[key].lower().replace('\t', ' '): prop for prop, key in
           re.findall(r'm_propertyNames\.Add\(eProperty\.(\w+),\s*LanguageMgr\.GetTranslation\([^,]+,\s*"SkillBase\.RegisterPropertyNames\.(\w+)"', sb, re.S)
           if key in en and prop in enum}
consts = open(os.path.join(SRV, 'gameutils', 'SkillConstants.cs'), encoding='utf-8').read()
specs = {m.group(1): m.group(2) for m in re.finditer(r'public const string (\w+)\s*=\s*"([^"]+)"', consts[consts.index('class Specs'):])}
SKILL = {specs.get(a, a).lower(): b for a, b in re.findall(r'm_specToSkill\.Add\(Specs\.(\w+),\s*eProperty\.(\w+)\)', sb)}
FOCUS = {specs.get(a, a).lower(): b for a, b in re.findall(r'm_specToFocus\.Add\(Specs\.(\w+),\s*eProperty\.(\w+)\)', sb)}

# period (in-game 1.65) display names of spell lines and skills -> the server's spec names
LINE = {
    'path of earth': 'earth magic', 'path of ice': 'cold magic', 'path of fire': 'fire magic', 'path of air': 'wind magic',
    'way of the moon': 'mana', 'way of the sun': 'light', 'void mastery': 'void', 'enchantment': 'enchantments',
    'body destruction': 'body magic', 'mind twisting': 'mind magic', 'essence manipulation': 'spirit magic',
    'matter': 'matter magic', 'matter manipulation': 'matter magic', 'arborial path': 'arboreal path',
    'verdant path spec': 'verdant path', 'bard nurture spec': 'nurture', 'flexible weapons': 'flexible',
    'smiting': 'smite', 'subterranean spec': 'subterranean', 'subterranean incantations': 'subterranean',
    'death servant': 'death servant', 'deathsight': 'deathsight', 'painworking': 'painworking',
    'bone army': 'bone army', 'cold': 'cold magic', 'wind': 'wind magic', 'earth': 'earth magic', 'fire': 'fire magic',
    'polearms': 'polearm', 'bard music': 'music', 'critical strikes': 'critical strike', 'scythes': 'scythe',
    'reaver soulrending': 'soulrending', 'druid nature magic': 'nature', 'druid nature spec': 'nature',
    'death servant spec': 'death servant', 'two handed weapons': 'two handed', 'large weapon': 'large weapons',
    'celtic spear': 'celtic spear', 'shield': 'shields', 'stealth spec': 'stealth', 'parry spec': 'parry',
    'necro painworking': 'painworking', 'runes of darkness': 'darkness', 'runes of supression': 'suppression',
    'runes of suppression': 'suppression', 'creeping path spec': 'creeping path', 'druid nurture spec': 'nurture',
}
# Left for a person to judge (reported as unmapped, never guessed): resists that are not 1.65 damage types (Mind,
# Light, Wind, Earth, Dark, Mana, Void), "Bow" (which bow skill), "Spirit Animation", "Evade", "Berserk".
RESIST = {'body': 'Resist_Body', 'cold': 'Resist_Cold', 'heat': 'Resist_Heat', 'energy': 'Resist_Energy',
          'matter': 'Resist_Matter', 'spirit': 'Resist_Spirit', 'crush': 'Resist_Crush', 'slash': 'Resist_Slash',
          'thrust': 'Resist_Thrust'}


def later(value):
    return 99 <= value <= 119 or 145 <= value <= 256


def prop_for(name, unit, focus):
    """eProperty name for a period bonus, or None (reported as unmapped)."""
    n = re.sub(r'\s+', ' ', name.strip().lower())
    if focus:
        spec = LINE.get(n, n)
        return FOCUS.get(spec)
    m = re.match(r'(\w+) resist(?:ance)?$', n) or re.match(r'resist (\w+)$', n)
    if m:
        return RESIST.get(m.group(1))   # light / dark / mind / earth resists: not 1.65 types -> unmapped
    if unit == 'lvls' or n in LINE or n in SKILL:
        spec = LINE.get(n, n)
        if spec in SKILL:
            return SKILL[spec]
    return DISPLAY.get(n)


def clean(body):
    t = re.sub(r'<[^>]+>', ' ', body)
    t = re.sub(r'(\\[nrt])+', ' ', t)
    return re.sub(r'(&nbsp;|\s)+', ' ', t)


def section(t, head, stops):
    m = re.search(head + r' ?: (.*?) (?:' + '|'.join(stops) + r'|$)', t)
    return m.group(1) if m else ''


STOPS = ['Focus Bonuses', 'Damage Modifiers', 'Magical Ability', 'Starting Cond', 'Charged', 'Armor Modifiers',
         'Default Bonus', 'Requires Class']


def parse(t):
    m = re.search(r"#Albion \{color:gold;\} (.{2,80}?) Realm: (\w+)", t)
    if not m:
        return None
    mag = re.findall(r"([A-Za-z' ]+?) ?: \+?(-?\d+) ?(%|pts|lvls)", section(t, 'Magical Bonuses', STOPS))
    foc = re.findall(r"([A-Za-z' ]+?) ?: \+?(\d+) ?lvls", section(t, 'Focus Bonuses', STOPS[1:]))
    foc += re.findall(r"- ([A-Za-z' ]+?): (\d+) (?:lvls|levels)", t[t.find('Focus Bonuses'):] if 'Focus Bonuses' in t else '')
    lvl = re.search(r'Level to Attain: (\d+)', t)
    name = m.group(1).strip()
    # merchant caster staves on the older page layout list their focus as "lvls" magical bonuses (the same values the
    # newer layout prints under Focus Bonuses: elm 5/2/2 ...): taken as focus
    # (the Apr-28-2003 bulk pages write them as "pts": Runewood Cabalist Staff of Body +21 pts)
    # The same holds for any staff on that layout (no Focus Bonuses section) whose spell-line "lvls" reach 5 or more:
    # skill bonuses on staves are small (Varulv Staff of Spirit Magic: +2 skill next to +35 focus), focus is not
    # (Runewood Cabalist Staff of Body 21/11/11, Aged Stone Staff 14/14).
    line_lvls = [int(b) for a, b, u in mag if u in ('lvls', 'pts') and FOCUS.get(LINE.get(a.strip().lower(), a.strip().lower()))]
    staff_focus = 'staff' in name.lower() and not foc and line_lvls and max(line_lvls) >= 5
    bonus = []
    for a, b, u in mag:
        if staff_focus and u in ('lvls', 'pts') and FOCUS.get(LINE.get(a.strip().lower(), a.strip().lower())):
            foc.append((a, b))
        else:
            bonus.append((a.strip(), int(b), u))
    return {'name': name, 'realm': m.group(2), 'level': int(lvl.group(1)) if lvl else None, 'bonus': bonus,
            'focus': sorted({(a.strip(), int(b)) for a, b in foc}), 'merchant': 'purchased from Merchants' in t}


SOURCES = [('wayback', os.path.join(PD, 'archive', 'wayback-allakhazam', 'period_items.jsonl.gz')),
           ('wayback', os.path.join(PD, 'archive', 'wayback-allakhazam', 'items_direct.jsonl.gz'))]
if allow_live:
    SOURCES.append(('live', os.path.join(PD, 'archive', 'allakhazam-live', 'item.jsonl.gz')))
rng = random.Random(11)
period = {}
for era, path in SOURCES:
    for r in records(path):
        if sample and (len(period) >= sample or rng.random() > 0.35):
            continue
        body = r.get('body') or r.get('text') or r.get('html')
        if not isinstance(body, str) or 'Realm:' not in body:
            continue
        p = parse(clean(body))
        if not p or (not p['bonus'] and not p['focus']):
            continue
        key = (p['name'].lower(), REALMS.get(p['realm'], 0))
        ts = r.get('timestamp') or ''
        old = period.get(key)
        if old and (old['era'] == 'wayback' and era == 'live' or old['era'] == era and old['ts'] >= ts):
            continue
        p.update(era=era, ts=ts, url=r.get('url', ''))
        period[key] = p

db = sqlite3.connect(f'file:{DB}?mode=ro', uri=True)
used = {(i or '').lower() for (i,) in db.execute('SELECT ItemTemplateID FROM MerchantItem UNION SELECT ItemTemplateID FROM LootTemplate')}
for (txt,) in db.execute('SELECT FinalRewardItemTemplates FROM DataQuest WHERE FinalRewardItemTemplates IS NOT NULL'):
    used.update(x.strip().lower() for x in re.split(r'[|;,]', txt or ''))
cols = ['Id_nb', 'Name', 'Realm', 'Level', 'Object_Type'] + [f'Bonus{i}Type' for i in range(1, 11)] + \
       [f'Bonus{i}' for i in range(1, 11)] + ['ExtraBonusType', 'ExtraBonus']
results, unmapped, updates = [], {}, []
for row in db.execute(f"SELECT {','.join(cols)} FROM ItemTemplate"):
    d = dict(zip(cols, row))
    p = period.get((d['Name'].lower(), d['Realm'])) or period.get((d['Name'].lower(), 0))
    if not p:
        continue
    # the same name at another level is another item (two 'Bow of the Stonewatch', L48 and L49): the page must fit
    if p['level'] and d['Level'] and abs(p['level'] - d['Level']) > 2:
        continue
    have = {d[f'Bonus{i}Type']: d[f'Bonus{i}'] for i in range(1, 11) if d[f'Bonus{i}Type'] and d[f'Bonus{i}']}
    if d['ExtraBonusType'] and d['ExtraBonus']:
        have[d['ExtraBonusType']] = d['ExtraBonus']
    missing, lower, skipped = [], [], []
    # A staff in the DB (Object_Type 8) whose page has no Focus Bonuses section: its spell-line values of 5 or more are
    # focus, whatever the name says (Tali Shard of the Triumvirate, a level 46 staff: Mentalism / Mana / Light +36)
    page_bonus, page_focus = list(p['bonus']), list(p['focus'])
    lines = [(n, v, u) for n, v, u in page_bonus if FOCUS.get(LINE.get(n.lower(), n.lower()))]
    if d['Object_Type'] == 8 and not page_focus and lines and max(v for _, v, _ in lines) >= 5:
        page_focus = [(n, v) for n, v, _ in lines]
        page_bonus = [x for x in page_bonus if x not in lines]
    for (name, value, unit) in page_bonus:
        prop = prop_for(name, unit, False)
        if not prop:
            unmapped[name] = unmapped.get(name, 0) + 1
            skipped.append(name)
            continue
        e = enum[prop]
        if later(e):
            skipped.append(name)
        elif e not in have:
            missing.append((prop, e, value))
        elif have[e] < value:
            lower.append((prop, e, have[e], value))
    for (name, value) in page_focus:
        prop = prop_for(name, 'lvls', True)
        if not prop:
            unmapped['focus: ' + name] = unmapped.get('focus: ' + name, 0) + 1
            skipped.append(name)
            continue
        e = enum[prop]
        if e not in have:
            missing.append((prop, e, value))
        elif have[e] < value:
            lower.append((prop, e, have[e], value))
    if not missing and not lower:
        continue
    rec = {'id': d['Id_nb'], 'name': d['Name'], 'realm': d['Realm'], 'level': d['Level'], 'object_type': d['Object_Type'],
           'in_use': d['Id_nb'].lower() in used, 'db_bonuses': len(have), 'missing': missing, 'lower': lower,
           'unmapped': skipped, 'period_era': p['era'], 'ts': p['ts'], 'url': p['url']}
    results.append(rec)
    free = [i for i in range(1, 11) if not d[f'Bonus{i}Type'] or not d[f'Bonus{i}']]
    sets = []
    for (prop, e, value), slot in zip(missing, free):
        sets.append(f'Bonus{slot}Type = {e}, Bonus{slot} = {value}')
    if sets and p['era'] == 'wayback':
        updates.append({'sql': f"UPDATE ItemTemplate SET {', '.join(sets)} WHERE Id_nb = '" + d['Id_nb'].replace("'", "''") + "'",
                        'reason': f"True 1.65 lost bonuses restored: {d['Name']} takes the bonuses its period page lists and the DB "
                                  f"row lacked ({', '.join(f'{x[0]} {x[2]}' for x in missing)}). Period source: Allakhazam "
                                  f"{p['url']}, Wayback capture {p['ts']}"})

out = {'period_pages_with_bonuses': len(period), 'items_flagged': len(results),
       'no_bonuses_at_all': sum(1 for r in results if r['db_bonuses'] == 0),
       'in_use': sum(1 for r in results if r['in_use']), 'unmapped_names': dict(sorted(unmapped.items(), key=lambda x: -x[1])),
       'items': results}
json.dump(out, open(os.path.join(HERE, 'missing_bonuses165.json'), 'w', encoding='utf-8'), indent=1)
json.dump(updates, open(os.path.join(HERE, 'missing_bonuses165_updates.json'), 'w', encoding='utf-8'), indent=1)
with open(os.path.join(HERE, 'missing_bonuses165.csv'), 'w', newline='', encoding='utf-8') as f:
    w = csv.writer(f)
    w.writerow(['id', 'name', 'realm', 'level', 'object_type', 'in_use', 'db_bonuses', 'missing', 'lower', 'unmapped', 'source', 'capture'])
    for r in results:
        w.writerow([r['id'], r['name'], r['realm'], r['level'], r['object_type'], r['in_use'], r['db_bonuses'],
                    '; '.join(f'{m[0]} {m[2]}' for m in r['missing']), '; '.join(f'{m[0]} {m[2]}->{m[3]}' for m in r['lower']),
                    '; '.join(r['unmapped']), r['url'], r['ts']])
print(f"period pages with bonuses {len(period)}; items flagged {len(results)} ({out['no_bonuses_at_all']} with no bonuses at all, "
      f"{out['in_use']} sold/dropped/rewarded); proposed updates {len(updates)}")
print('unmapped bonus names (top):', list(out['unmapped_names'].items())[:20])
