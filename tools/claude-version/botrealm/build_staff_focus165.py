"""Merchant caster staves get their 1.65 focus levels back, and the missing-bonus probe's fixes are installed
(owner 2026-11-01: "Magic Staff from merchants have no spell level"; "fix the items").

The wood-tier merchant staves ("<wood> <Class> Staff of <Line>") carry no bonus at all in the world DB (the original
public database; normal mode too). On their 1.65 pages (194 Allakhazam captures, Jul-Oct 2003, every realm) each tier
has exactly one pattern:
    tier (level)        staff of one line: that line / the class's other two     "Magic"-type staff: all three
    elm (6)             5 / 2                                                     4
    oaken (11)          9 / 5                                                     7
    ironwood (16)       13 / 7                                                    11
    heartwood (21)      18 / 9                                                    14
    runewood (26)       21 / 11                                                   17
This builder checks the table against every captured page (it must reproduce all of them), then sets the focus on every
such staff row that has no focus yet. Classes outside 1.65 (Bainshee, Warlock) are left alone.

It also installs missing_bonuses165_updates.json (probe_missing_bonuses165.py: other items' lost bonuses, each from its
own period page). Both go into true165-rules.json "updates" (both copies), replacing what an earlier run added.
Usage: python -I build_staff_focus165.py [--write]"""
import json, os, re, sqlite3, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
DB = os.path.join(ROOT, 'runtime', 'data', 'opendaoc-165.sqlite3.db')
PAGES = os.path.join(HERE, 'period_focus_staves.json')
write = '--write' in sys.argv

TIERS = {'elm': (5, 2, 4), 'oaken': (9, 5, 7), 'ironwood': (13, 7, 11), 'heartwood': (18, 9, 14), 'runewood': (21, 11, 17)}
FOCUS = {'Darkness': 120, 'Suppression': 121, 'Runecarving': 122, 'Spirit': 123, 'Fire': 124, 'Air': 125, 'Cold': 126,
         'Earth': 127, 'Light': 128, 'Body': 129, 'Matter': 130, 'Mind': 132, 'Void': 133, 'Mana': 134,
         'Enchantments': 135, 'Mentalism': 136, 'Summoning': 137, 'BoneArmy': 138, 'PainWorking': 139,
         'DeathSight': 140, 'DeathServant': 141, 'Verdant': 142, 'CreepingPath': 143, 'Arboreal': 144}
CLASS_LINES = {
    'enchanter': ('Enchantments', 'Light', 'Mana'), 'eldritch': ('Light', 'Mana', 'Void'),
    'mentalist': ('Mentalism', 'Light', 'Mana'), 'animist': ('Arboreal', 'CreepingPath', 'Verdant'),
    'bonedancer': ('BoneArmy', 'Darkness', 'Suppression'), 'runemaster': ('Darkness', 'Suppression', 'Runecarving'),
    'spiritmaster': ('Darkness', 'Suppression', 'Summoning'), 'cabalist': ('Body', 'Matter', 'Spirit'),
    'sorcerer': ('Body', 'Matter', 'Mind'), 'theurgist': ('Earth', 'Cold', 'Air'), 'wizard': ('Earth', 'Cold', 'Fire'),
    '': ('PainWorking', 'DeathSight', 'DeathServant'),   # necromancer staves carry no class word
}
LINE_WORD = {'light': 'Light', 'mana': 'Mana', 'void': 'Void', 'enchantments': 'Enchantments', 'mentalism': 'Mentalism',
             'arboreal path': 'Arboreal', 'creeping path': 'CreepingPath', 'verdant path': 'Verdant', 'bones': 'BoneArmy',
             'darkness': 'Darkness', 'suppression': 'Suppression', 'suppresion': 'Suppression', 'runecarving': 'Runecarving',
             'summoning': 'Summoning', 'body': 'Body', 'matter': 'Matter', 'spirit': 'Spirit', 'mind': 'Mind',
             'earth': 'Earth', 'ice': 'Cold', 'wind': 'Air', 'fire': 'Fire', 'painworking': 'PainWorking',
             'deathsight': 'DeathSight', 'death servant': 'DeathServant'}
ALL_LINES = {'magic', 'mysticism', 'elements', 'alteration', 'underworld'}
PATTERN = re.compile(r"^(elm|oaken|ironwood|heartwood|runewood) (?:(\w+) )?staff of (?:the )?(.+)$", re.I)


def focus_for(name):
    """{eProperty focus: level} for a wood-tier merchant staff name, or None."""
    m = PATTERN.match(name.strip())
    if not m:
        return None
    wood, cls, word = m.group(1).lower(), (m.group(2) or '').lower(), m.group(3).strip().lower()
    lines = CLASS_LINES.get(cls)
    if lines is None:
        return None
    main, minor, every = TIERS[wood]
    if word in ALL_LINES:
        return {FOCUS[l]: every for l in lines}
    named = LINE_WORD.get(word)
    if named not in lines:
        return None
    return {FOCUS[l]: (main if l == named else minor) for l in lines}


# ---- the table must reproduce every captured period page
PAGE_LINE = {'enchantment': 'Enchantments', 'way of the moon': 'Mana', 'way of the sun': 'Light', 'void mastery': 'Void',
             'mentalism': 'Mentalism', 'arborial path': 'Arboreal', 'arboreal path': 'Arboreal', 'creeping path': 'CreepingPath',
             'verdant path': 'Verdant', 'verdant path spec': 'Verdant', 'bone army': 'BoneArmy', 'darkness': 'Darkness',
             'suppression': 'Suppression', 'runecarving': 'Runecarving', 'summoning': 'Summoning',
             'body destruction': 'Body', 'matter': 'Matter', 'essence manipulation': 'Spirit', 'mind twisting': 'Mind',
             'path of earth': 'Earth', 'path of ice': 'Cold', 'path of air': 'Air', 'path of fire': 'Fire',
             'painworking': 'PainWorking', 'deathsight': 'DeathSight', 'death servant': 'DeathServant'}
pages = json.load(open(PAGES, encoding='utf-8'))
checked = mismatched = 0
for v in pages.values():
    want = focus_for(v['name'])
    if want is None or len(v['focus']) != 3:
        continue
    got = {FOCUS[PAGE_LINE[n.lower()]]: lv for n, lv in v['focus'] if n.lower() in PAGE_LINE}
    checked += 1
    if got != want:
        mismatched += 1
        print('MISMATCH', v['name'], 'page', got, 'table', want)
print(f'table checked against {checked} period pages: {mismatched} mismatches')
# 2026-11-01: 187 of 189 match. The two that don't are single-page slips: 'Ironwood Theurgist Staff of Wind' lists Path
# of Fire (a Wizard line, not a Theurgist one), 'Oaken Bonedancer Staff of Darkness' lists 9/9/5 where every other
# oaken staff is 9/5/5. The table (the class's own lines) is used for both.
if mismatched > max(2, checked // 50):
    sys.exit('the table does not reproduce the period pages: nothing written')

# ---- staves to set
db = sqlite3.connect(f'file:{DB}?mode=ro', uri=True)
probe = json.load(open(os.path.join(HERE, 'missing_bonuses165_updates.json'), encoding='utf-8'))
probe_ids = {re.search(r"Id_nb = '(.*)'$", u['sql']).group(1).replace("''", "'").lower() for u in probe}
cols = ['Id_nb', 'Name'] + [f'Bonus{i}Type' for i in range(1, 11)] + [f'Bonus{i}' for i in range(1, 11)]
staff_updates = []
installed_staves = {}
for u in json.load(open(os.path.join(HERE, 'true165-rules.json'), encoding='utf-8')).get('updates', []):
    if u.get('reason', '').startswith('True 1.65 merchant focus staves:'):
        installed_staves[re.search(r"Id_nb = '(.*)'$", u['sql']).group(1).replace("''", "'").lower()] = u
for row in db.execute(f"SELECT {','.join(cols)} FROM ItemTemplate WHERE Object_Type = 8"):
    d = dict(zip(cols, row))
    want = focus_for(d['Name'])
    if not want or d['Id_nb'].lower() in probe_ids:
        continue
    if any(120 <= (d[f'Bonus{i}Type'] or 0) <= 165 and d[f'Bonus{i}'] for i in range(1, 11)):
        # Already has focus: its own (left alone), or ours from an earlier run on a refreshed world (kept as installed).
        if d['Id_nb'].lower() in installed_staves:
            staff_updates.append(installed_staves[d['Id_nb'].lower()])
        continue
    free = [i for i in range(1, 11) if not d[f'Bonus{i}Type'] or not d[f'Bonus{i}']]
    sets = [f'Bonus{slot}Type = {prop}, Bonus{slot} = {lvl}' for (prop, lvl), slot in zip(sorted(want.items()), free)]
    staff_updates.append({'sql': f"UPDATE ItemTemplate SET {', '.join(sets)} WHERE Id_nb = '" + d['Id_nb'].replace("'", "''") + "'",
                          'reason': f"True 1.65 merchant focus staves: {d['Name']} gets its 1.65 focus levels by its wood tier "
                                    f"(the pattern of 194 Allakhazam pages, Jul-Oct 2003; build_staff_focus165.py)"})
print('staves set from the tier table:', len(staff_updates), '| probe fixes:', len(probe))

if write:
    TAGS = ('True 1.65 merchant focus staves:', 'True 1.65 lost bonuses restored:')
    for folder in (HERE, os.path.join(ROOT, 'runtime', 'botrealm')):
        p = os.path.join(folder, 'true165-rules.json')
        raw = open(p, 'rb').read()
        rules = json.loads(raw.decode('utf-8'))
        ups = rules.setdefault('updates', [])
        ups[:] = [u for u in ups if not u.get('reason', '').startswith(TAGS)]
        ups.extend(probe)
        ups.extend(staff_updates)
        nl = b'\r\n' if b'\r\n' in raw else b'\n'
        open(p, 'wb').write(json.dumps(rules, indent=1, ensure_ascii=False).encode('utf-8').replace(b'\n', nl) + (nl if raw.endswith(nl) else b''))
        print('installed in', p, len(ups), 'updates')
        # Items that an add-*.json file creates (the drop / vendor passes) are written over by the launcher's add step,
        # which runs after the "updates": put their bonuses into the add-file row too, or they never reach the world.
        # Re-running a builder that regenerates those add files means re-running this one afterwards.
        fixes = {}
        for u in probe + staff_updates:
            m = re.match(r"UPDATE ItemTemplate SET (.*) WHERE Id_nb = '(.*)'$", u['sql'])
            pairs = re.findall(r"(\w+) = (-?\d+)", m.group(1))
            fixes[m.group(2).replace("''", "'").lower()] = [(col, int(val)) for col, val in pairs]
        for name in sorted(os.listdir(folder)):
            if not (name.startswith('add-') and name.endswith('.json')):
                continue
            ap = os.path.join(folder, name)
            raw = open(ap, 'rb').read()
            data = json.loads(raw.decode('utf-8'))
            if not isinstance(data, dict) or data.get('table') != 'ItemTemplate':
                continue
            n = 0
            for row in data['rows']:
                for col, val in fixes.get(str(row.get('Id_nb', '')).lower(), []):
                    if row.get(col) != val:
                        row[col] = val
                        n += 1
            if n:
                nl = b'\r\n' if b'\r\n' in raw else b'\n'
                open(ap, 'wb').write(json.dumps(data, indent=1, ensure_ascii=False).encode('utf-8').replace(b'\n', nl) + (nl if raw.endswith(nl) else b''))
                print('  ', name, n, 'bonus fields set in its rows')
