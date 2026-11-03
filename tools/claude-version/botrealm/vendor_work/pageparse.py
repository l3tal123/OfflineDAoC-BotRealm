"""Parser for the period Allakhazam item blocks in period_item_pages.jsonl (raw = pipe-separated text).
parse(rec) -> dict of the page's own fields, nothing derived."""
import re

KEYS = {
    'Realm:', 'Slot:', 'Requires Skill:', 'Requires Class:', 'Armor Factor:', 'Armor Absorb:', 'Effective AF:',
    'DPS:', 'Delay:', 'Effective DPS:', 'Damage Type:', 'Shield Size:', 'Instrument Type:', 'Quality:', 'Weight:',
    'Level to Attain:', 'Levels of use:', 'Levels of Use:', 'Levels of use :', 'Rarity:', 'Starting Cond.:',
    'Starting Dur.:', 'Starting Condition:', 'Starting Duration:', 'Default Bonus:', 'NPC Price:', 'Average Price:',
    'Last Updated:', 'Magic Level:', 'Charges:', 'Level Requirement:', 'Utility:', 'Level of Proc:', 'Bonus Level:',
    'Submitted by:', 'Found in:', 'Recipe:', 'Magical Ability:', 'Charged Magic Item:', 'Max Charges:',
}
BONUS_RE = re.compile(r'^:\s+([+-]?\d+)\s*(pts|%|lvls?)?\s*$')
MONEY_RE = re.compile(r'^\s*(?:(\d+)p\s*)?(?:(\d+)g\s*)?(?:(\d+)s\s*)?(?:(\d+)c)?\s*$')
SEAL_RE = re.compile(r'(\d+)\s+(Diamond|Sapphire|Emerald|Ruby)\s+Seals?', re.I)


def money(s):
    """'1g 44s' -> 14400 copper; '23 Emerald Seals' -> (23, 'Emerald'); None when not a price."""
    if s is None:
        return None
    s = s.strip()
    m = SEAL_RE.search(s)
    if m:
        return (int(m.group(1)), m.group(2).capitalize())
    s2 = re.sub(r'^(?:Purchased|Purchase)\s+for\s+', '', s, flags=re.I)
    s2 = re.sub(r'\s+from\s+.*$', '', s2, flags=re.I).strip()
    m = MONEY_RE.match(s2)
    if not m or not any(m.groups()):
        return None
    p, g, si, c = (int(x) if x else 0 for x in m.groups())
    return ((p * 1000 + g) * 100 + si) * 100 + c


def num(s):
    m = re.search(r'-?\d+(?:\.\d+)?', s or '')
    return float(m.group(0)) if m else None


def parse(rec):
    t = [x.strip() for x in rec['raw'].split('|')]
    out = {'name': t[0], 'fields': {}, 'classes': [], 'bonuses': [], 'focus_lines': [], 'ability': [],
           'left_hand_usable': False, 'housing': False, 'flags': []}
    f = out['fields']
    i = 1
    n = len(t)
    while i < n:
        tok = t[i]
        if tok == '[' and i + 1 < n and t[i + 1] == 'Post':
            break                                   # the forum comments start here
        if tok == 'Usable in the left hand.':
            out['left_hand_usable'] = True
        elif tok == 'Housing Required':
            out['housing'] = True
        elif tok in ('Minor Item', 'Incomplete info'):
            out['flags'].append(tok)
        elif tok == 'Requires Skill:':
            f['Requires Skill:'] = t[i + 1] if i + 1 < n else ''
            i += 2
            if i < n and t[i] == '(':
                i += 1
                while i < n and t[i] != ')':
                    if t[i] != ',':
                        out['classes'].append(t[i])
                    i += 1
            continue
        elif tok == 'Requires Class:':
            i += 1
            while i < n and t[i] not in KEYS and not t[i].endswith(':') and t[i] not in ('Magical Bonuses', 'Focus Bonuses', 'Damage Modifiers', 'Armor Modifiers'):
                out['classes'].append(t[i])
                i += 1
            f['Requires Class:'] = ','.join(out['classes'])
            continue
        elif tok in ('Magical Bonuses', 'Magical Bonuses:', 'Focus Bonuses', 'Focus Bonuses:'):
            kind = 'focus' if tok.startswith('Focus') else 'magic'
            i += 1
            if i < n and t[i] == ':':
                i += 1
            while i + 1 < n and BONUS_RE.match(t[i + 1]):
                m = BONUS_RE.match(t[i + 1])
                out['bonuses'].append({'name': t[i], 'value': int(m.group(1)), 'unit': (m.group(2) or ''), 'block': kind})
                i += 2
            continue
        elif tok in ('Damage Modifiers', 'Armor Modifiers'):
            i += 1
            if i < n and t[i] == ':':
                i += 1
            while i < n and re.match(r'^(Base DPS|Clamped DPS|Base Factor|Clamped Factor|Absorption|Quality|Condition):', t[i]):
                k, v = t[i].split(':', 1)
                f['mod ' + k] = v.strip()
                i += 1
            continue
        elif tok == 'Magical Ability:':
            i += 1
            while i < n and re.match(r'^[A-Za-z ]+:\s*\S', t[i]) and t[i] not in KEYS:
                out['ability'].append(t[i])
                i += 1
            continue
        elif tok.startswith('Focus:'):
            out['focus_lines'].append(tok)
        elif tok in KEYS:
            f[tok] = t[i + 1] if i + 1 < n else ''
            i += 2
            continue
        i += 1
    # normalise a few spellings
    for a, b in (('Levels of Use:', 'Levels of use:'), ('Levels of use :', 'Levels of use:'),
                 ('Starting Condition:', 'Starting Cond.:'), ('Starting Duration:', 'Starting Dur.:')):
        if a in f and b not in f:
            f[b] = f.pop(a)
    # every price the page gives (NPC Price field, "Purchased for" lines anywhere in the block)
    prices = []
    if f.get('NPC Price:') and f['NPC Price:'] != 'Average Price:':
        p = money(f['NPC Price:'])
        if p is not None:
            prices.append(('NPC Price', p))
    for x in t:
        if re.match(r'^(Purchased|Purchase)\s+for\s', x, re.I):
            p = money(x)
            if p is not None:
                prices.append((x, p))
    out['prices'] = prices
    lv = f.get('Levels of use:')
    m = re.match(r'\s*(\d+)\s*-\s*(\d+)', lv or '')
    out['levels_of_use'] = (int(m.group(1)), int(m.group(2))) if m else None
    return out
