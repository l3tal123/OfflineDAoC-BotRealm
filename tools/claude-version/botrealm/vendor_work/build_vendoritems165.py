"""BotRealm period vendor items: ItemTemplate rows for the Allakhazam item pages (Wayback <= 2003-11) that list
merchants and that the BotRealm world does not have. Read-only on the world snapshot; writes only into this folder.

    python -I build_vendoritems165.py
 -> add-vendoritems165.json   (rows for the launcher's content loader: {table, key, reason, rows})
    vendor_items_report.json  (one entry per merchant page: built | skipped, why, what was derived and from where)

Rules (owner): stats come from the period page; a value the page does not give is only DERIVED from the closest
existing sibling item in our database, and every derived value names its source.
"""
import collections, datetime, difflib, json, os, re, sqlite3, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import pageparse  # noqa: E402

DB = os.path.join(HERE, 'world165_snapshot.db')
PAGES = os.path.join(HERE, 'period_item_pages.jsonl')
OUT = os.path.join(HERE, 'add-vendoritems165.json')
REPORT = os.path.join(HERE, 'vendor_items_report.json')

REALM = {'Albion': 1, 'Midgard': 2, 'Hibernia': 3, 'All': 0}
SLOT = {'right hand': 10, 'left hand': 11, 'two hand': 12, 'ranged': 13, 'helm': 21, 'head': 21, 'hands': 22,
        'feet': 23, 'jewelry': 24, 'torso': 25, 'chest': 25, 'back': 26, 'legs': 27, 'arms': 28, 'neck': 29,
        'waist': 32, 'wrist': 33, 'ring': 35, 'inventory': 40}
ARMOR = {'cloth': 32, 'leather': 33, 'studded': 34, 'chain': 35, 'plate': 36, 'reinforced': 37, 'scale': 38}
# "Requires Skill" weapon names -> eObjectType (checked against 7150 page/DB pairs, see _learn.py)
WEAPON = {'crush': 2, 'slash': 3, 'thrust': 4, 'two handed': 6, 'polearms': 7, 'staff': 8, 'longbow': 9,
          'crossbow': 10, 'sword': 11, 'hammer': 12, 'axe': 13, 'spear': 14, 'composite bow': 15, 'throwing': 16,
          'left axe': 17, 'recurve bow': 18, 'blades': 19, 'blunt': 20, 'piercing': 21, 'large weapons': 22,
          'celtic spear': 23, 'flexible weapons': 24, 'hand to hand': 25, 'scythes': 26, 'shields': 42,
          'instruments': 45, 'bard music': 45, 'short bow': 5}
DAMAGE = {'crush': 1, 'slash': 2, 'thrust': 3}
SHIELD = {'small': 1, 'medium': 2, 'large': 3}
INSTRUMENT = {'drum': 1, 'lute': 2, 'flute': 3, 'harp': 4}            # eInstrumentType
SEAL_CLASS = {'DOL.GS.GameDiamondSealsMerchant': 'Diamond', 'DOL.GS.GameSapphireSealsMerchant': 'Sapphire',
              'DOL.GS.GameEmeraldSealsMerchant': 'Emerald'}
CLASSES = {'acolyte': 16, 'albion rogue': 17, 'rogue (albion)': 17, 'disciple': 20, 'elementalist': 15, 'fighter': 14,
           'forester': 57, 'guardian': 52, 'mage': 18, 'magician': 51, 'rogue (midgard)': 38, 'mystic': 36,
           'naturalist': 53, 'seer': 37, 'stalker': 54, 'viking': 35, 'armsman': 2, 'cabalist': 13, 'cleric': 6,
           'friar': 10, 'heretic': 33, 'infiltrator': 9, 'mercenary': 11, 'minstrel': 4, 'necromancer': 12,
           'paladin': 1, 'reaver': 19, 'scout': 3, 'sorcerer': 8, 'theurgist': 5, 'wizard': 7, 'berserker': 31,
           'bonedancer': 30, 'healer': 26, 'hunter': 25, 'runemaster': 29, 'savage': 32, 'shadowblade': 23,
           'shaman': 28, 'skald': 24, 'spiritmaster': 27, 'thane': 21, 'warrior': 22, 'animist': 55, 'bard': 48,
           'blademaster': 43, 'champion': 45, 'druid': 47, 'eldritch': 40, 'enchanter': 41, 'hero': 44,
           'mentalist': 42, 'nightshade': 49, 'ranger': 50, 'valewalker': 56, 'warden': 46,
           'the way of arms': 52, 'the way of nature': 53, 'the way of stealth': 54, 'the way of magic': 51}

# Page bonus names -> eProperty. 'pts' / '%' lines are stats, resists and skills; 'lvls' lines on staves are focus.
# Each entry was checked against existing items whose page we have (_learn.py) or is the plain eProperty name.
STAT = {'strength': 1, 'dexterity': 2, 'constitution': 3, 'quickness': 4, 'intelligence': 5, 'piety': 6,
        'empathy': 7, 'charisma': 8, 'power': 9, 'hits': 10,
        'body resist': 11, 'cold resist': 12, 'crush resist': 13, 'energy resist': 14, 'heat resist': 15,
        'matter resist': 16, 'slash resist': 17, 'spirit resist': 18, 'thrust resist': 19}
SKILL = {'two handed': 20, 'body destruction': 21, 'critical strikes': 23, 'crossbow': 24, 'crush': 25,
         'death servant': 26, 'deathsight': 27, 'dual wield': 28, 'path of earth': 29, 'enhancement': 30,
         'envenom': 31, 'path of fire': 32, 'flexible weapons': 33, 'path of ice': 34, 'instruments': 35,
         'longbow': 36, 'matter': 37, 'mind twisting': 38, 'painworking': 39, 'parry': 40, 'polearms': 41,
         'rejuvenation': 42, 'shields': 43, 'slash': 44, 'smiting': 45, 'reaver soulrending': 46,
         'spirit animation': 47, 'staff': 48, 'stealth': 49, 'thrust': 50, 'path of air': 51, 'sword': 52,
         'hammer': 53, 'axe': 54, 'left axe': 55, 'spear': 56, 'mending': 57, 'augmentation': 58, 'darkness': 60,
         'suppression': 61, 'runecarving': 62, 'stormcalling': 63, 'way of the sun': 65, 'void mastery': 66,
         'way of the moon': 67, 'composite bow': 68, 'battlesongs': 69, 'enchantment': 70, 'blades': 72,
         'blunt': 73, 'piercing': 74, 'large weapons': 75, 'mentalism': 76, 'regrowth': 77, 'nurture': 78,
         'druid nature spec': 79, 'druid nature magic': 79, 'bard music': 80, 'celtic dual': 81,
         'celtic spear': 82, 'recurve bow': 83, 'valor': 84, 'verdant path': 87, 'creeping path': 88,
         'arborial path': 89, 'scythes': 90, 'summoning': 98}
FOCUS = {'darkness': 120, 'suppression': 121, 'runecarving': 122, 'spirit animation': 123, 'path of fire': 124,
         'path of air': 125, 'path of ice': 126, 'path of earth': 127, 'way of the sun': 128, 'body destruction': 129,
         'matter': 130, 'mind twisting': 132, 'void mastery': 133, 'way of the moon': 134, 'enchantment': 135,
         'mentalism': 136, 'summoning': 137, 'painworking': 139, 'deathsight': 140, 'death servant': 141,
         'verdant path': 142, 'creeping path': 143, 'arborial path': 144,
         # the "Focus: X - N lvls" note lines on Darkness Falls staves
         'light magic': 128, 'mana magic': 134, 'void magic': 133}

# Owner-checkable rulings on single pages (each one explained; nothing else is special-cased).
RULINGS = {
    379: 'page has no item name ("Gloves"); AF 36 Hibernian leather hands - cannot tell which vendor item it is',
    570: 'page realm looks wrong: "Gossamer Padded" is the Midgard cloth set (our world has gossamer padded '
         'sleeves/cap in Midgard with the same AF 20); Hibernia has no padded cloth set; page has no weight',
    572: 'page realm looks wrong: "Gossamer Padded" is the Midgard cloth set (our world has gossamer padded '
         'sleeves/cap in Midgard with the same AF 20); Hibernia has no padded cloth set; page has no weight',
    479: 'typo page of Rawhide Svarlaedar Leggings, which our world has (rawhide svarlaedar leggings: same AF 6, '
         'absorb 10, weight 2.8 lbs; the correctly spelt period page exists too)',
    480: 'typo page of Rawhide Svarlaedar Sleeves, which our world has (rawhide svarlaedar sleeves: same AF 6, '
         'absorb 10, weight 2.4 lbs; the correctly spelt period page exists too)',
    2033: 'page name looks wrong: Cabalists have no Mind line; its bonuses (Matter +21, Body/Essence +11) are the '
          'Runewood Cabalist Staff of Matter, which our world has (runewood_Cabalist_Staff_of_Matter, same DPS 9.0)',
}
SLOT_SYNONYMS = [('leggings', 'pants')]


def norm(s):
    s = (s or '').lower().replace('’', "'").replace('`', "'")
    return re.sub(r'\s+', ' ', s).strip()


def strip_suffix(name):
    """Allakhazam's own disambiguation tags are not part of the in-game name: "(Hib)", "(Mid)", "(Alb)", "(DF)",
    "(AF 54)", a trailing "AF 70", "(interior)"."""
    s = re.sub(r'\s*\((?:hib|mid|alb|df|af \d+|interior|exterior)\)', ' ', name, flags=re.I)
    s = re.sub(r'\s+AF \d+\s*$', '', s, flags=re.I)
    return re.sub(r'\s+', ' ', s).strip()


def page_stats(p):
    """Core stats as the page gives them (None = page silent)."""
    f = p['fields']
    st = {}
    slot = (f.get('Slot:') or '').strip().lower()
    st['slot'] = slot
    st['item_type'] = SLOT.get(slot)
    skill = (f.get('Requires Skill:') or '').strip()
    st['skill'] = skill
    st['armor'] = ARMOR.get(skill.split(':', 1)[1].strip().lower()) if skill.lower().startswith('armor:') else None
    st['weapon'] = WEAPON.get(skill.lower())
    st['af'] = pageparse.num(f.get('Armor Factor:'))
    st['absorb'] = pageparse.num(f.get('Armor Absorb:'))
    base = pageparse.num(f.get('mod Base DPS'))
    st['dps'] = base if base else pageparse.num(f.get('DPS:'))
    st['dps_field'] = pageparse.num(f.get('DPS:'))
    st['speed'] = pageparse.num(f.get('Delay:'))
    dt = (f.get('Damage Type:') or '').strip().lower()
    st['damage'] = DAMAGE.get(dt)
    ss = (f.get('Shield Size:') or '').strip().lower()
    st['shield'] = SHIELD.get(ss.split()[0]) if ss else None
    it = (f.get('Instrument Type:') or '').strip().lower()
    st['instrument'] = INSTRUMENT.get(it) if it else None
    q = pageparse.num(f.get('Quality:'))
    st['quality'] = int(q) if q else None
    w = pageparse.num(f.get('Weight:'))
    st['weight'] = int(round(w * 10)) if w else None          # 0.00 lbs on a page = not recorded
    st['bonus_pct'] = int(pageparse.num(f.get('Default Bonus:')) or 0)
    st['magic_level'] = int(pageparse.num(f.get('Magic Level:'))) if pageparse.num(f.get('Magic Level:')) else None
    st['lou'] = p['levels_of_use']
    ch = pageparse.num(f.get('Charges:'))
    st['charges'] = int(ch) if ch else None
    st['req_classes'] = [c for c in p['classes']] if f.get('Requires Class:') else []
    return st


def map_bonuses(p):
    """Page bonus lines -> [(eProperty, value, source)], plus lines that could not be mapped."""
    out, unknown = [], []
    lines = list(p['bonuses'])
    seen_lvls = collections.Counter(b['name'].lower() for b in lines if b['unit'].startswith('lvl'))
    first_of_pair = set()
    for b in lines:
        name, val, unit = b['name'].lower().strip(), b['value'], b['unit']
        if val == 0:
            unknown.append(f"{b['name']} {val} {unit} (zero value dropped)")
            continue
        prop = None
        if unit == '%' or name in STAT:
            prop = STAT.get(name)
        elif unit.startswith('lvl'):
            if seen_lvls[name] == 2 and name not in first_of_pair:
                # DF staves list the same line twice: "+1 lvls" (the skill) and "+31 lvls" (the focus);
                # their other pages write the skill as "+N pts" and the focus as a separate "Focus: - N lvls" line
                first_of_pair.add(name)
                pair = sorted(x['value'] for x in lines if x['name'].lower() == name)
                prop = SKILL.get(name) if val == pair[0] else FOCUS.get(name)
            elif seen_lvls[name] == 2:
                pair = sorted(x['value'] for x in lines if x['name'].lower() == name)
                prop = SKILL.get(name) if val == pair[0] else FOCUS.get(name)
            else:
                prop = FOCUS.get(name)
        else:
            prop = SKILL.get(name)
        if prop:
            out.append((prop, val, f"{b['name']} {val:+d} {unit}".strip()))
        else:
            unknown.append(f"{b['name']} {val} {unit}")
    for line in p['focus_lines']:
        m = re.match(r'Focus:\s*(.+?)\s*-\s*(\d+)\s*lvls', line)
        if m and FOCUS.get(m.group(1).lower()):
            prop = FOCUS[m.group(1).lower()]
            if not any(o[0] == prop for o in out):
                out.append((prop, int(m.group(2)), f'{line} (page note)'))
        elif m:
            unknown.append(line)
    return out, unknown


class World:
    def __init__(self):
        con = sqlite3.connect(f'file:{DB}?mode=ro', uri=True)
        con.row_factory = sqlite3.Row
        self.cols = [r[1] for r in con.execute('pragma table_info(ItemTemplate)')]
        self.rows = [dict(r) for r in con.execute('select * from ItemTemplate')]
        self.byname = collections.defaultdict(list)
        for r in self.rows:
            self.byname[norm(r['Name'])].append(r)
        self.sold = collections.defaultdict(set)              # Id_nb -> seal currency names / 'coin'
        merch = {}
        for cls, lid in con.execute('select ClassType, ItemsListTemplateID from Mob where ItemsListTemplateID is not null'):
            merch.setdefault(lid.lower(), SEAL_CLASS.get(cls, 'coin'))
        for lid, iid in con.execute('select ItemListID, ItemTemplateID from MerchantItem'):
            self.sold[iid.lower()].add(merch.get((lid or '').lower(), 'coin'))
        self.spells = {r['SpellID']: dict(r) for r in con.execute('select * from Spell')}
        con.close()

    def same_name(self, name, realm):
        c = self.byname.get(norm(name), [])
        return [r for r in c if realm == 0 or r['Realm'] in (realm, 0)]


def realm_ok(r, realm):
    return realm == 0 or r['Realm'] in (realm, 0)


def type_of(st):
    """(Object_Type, Item_Type, Hand) by the conventions existing vendor items use (_learn.py tables)."""
    it = st['item_type']
    if st['armor']:
        return st['armor'], it, 0
    w = st['weapon']
    if st['shield'] or w == 42:
        return 42, 11, 2
    if w == 45 or st['instrument']:
        return 45, 12, 1
    if w is None:
        if it in (24, 26, 29, 32, 33, 35):
            return 41, it, 0
        if it == 40:
            return 0, 40, 0
        return None, it, None
    if w == 16:
        return 16, 13, 0
    if it == 12 or w in (6, 7, 8, 22, 23, 26):
        return w, 12, 1
    if it == 13 or w in (5, 9, 10, 15, 18):
        return w, 13, 1
    if it == 11 or w == 17:
        return w, 11, 2
    return w, (11 if st.get('left') else 10), (2 if st.get('left') else 0)


def stats_equal(st, r, p_bon):
    """An existing row is the page's item when type, slot and the main stat agree (and bonuses, if both have any)."""
    ot, it, _ = type_of(st)
    if ot is not None and r['Object_Type'] != ot:
        return False
    if it and r['Item_Type'] != it and not (it in (10, 11) and r['Item_Type'] in (10, 11)):
        return False
    if st['af'] is not None and abs(r['DPS_AF'] - st['af']) > 1:
        return False
    if st['dps'] and abs(r['DPS_AF'] - round(st['dps'] * 10)) > 1:
        return False
    if p_bon:
        db_b = sorted((r[f'Bonus{k}Type'], r[f'Bonus{k}']) for k in range(1, 11) if r[f'Bonus{k}Type'])
        if db_b and sorted((b[0], b[1]) for b in p_bon) != db_b:
            return False
    return True


def pick_existing(cands, realm, price):
    def score(r):
        return (r['Realm'] != realm, r['Id_nb'].lower() not in W.sold, (price is not None and r['Price'] != price),
                r['Id_nb'])
    return min(cands, key=score)


W = None


def main():
    global W
    W = World()
    now = datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')
    pages = [json.loads(l) for l in open(PAGES, encoding='utf-8')]
    vendor = [r for r in pages if r['merchants']]
    allpages = {r['key']: r for r in pages}
    parsed = {r['key']: pageparse.parse(r) for r in pages}
    page_names = {(norm(strip_suffix(r['name'])), REALM.get(r['realm'], 0)) for r in pages}

    report, rows = [], []
    todo = []
    for rec in sorted(vendor, key=lambda r: r['key']):
        p = parsed[rec['key']]
        realm = REALM.get(rec['realm'], 0)
        st = page_stats(p)
        st['left'] = p['left_hand_usable']
        bon, unknown = map_bonuses(p)
        cash = [x[1] for x in p['prices'] if isinstance(x[1], int)]
        ent = {'page_key': rec['key'], 'name': rec['name'], 'realm': rec['realm'], 'status': 'skipped',
               'skip_reason': None, 'derived': {}, 'price_source': 'none', 'url': rec['url']}
        report.append(ent)
        # 1. exact name
        ex = W.same_name(rec['name'], realm)
        if ex:
            e = pick_existing(ex, realm, cash[0] if cash else None)
            ent['skip_reason'] = 'exists: same name in our world'
            ent['existing_id'] = e['Id_nb']
            continue
        # 2. the name without Allakhazam's tag
        sname = strip_suffix(rec['name'])
        ex = W.same_name(sname, realm) if sname != rec['name'] else []
        if ex:
            e = pick_existing(ex, realm, cash[0] if cash else None)
            ent['skip_reason'] = f'exists: page title carries an Allakhazam tag; our world has "{e["Name"]}"'
            ent['existing_id'] = e['Id_nb']
            ent['stats_match'] = stats_equal(st, e, bon)
            continue
        if rec['key'] in RULINGS:
            ent['skip_reason'] = 'ruling: ' + RULINGS[rec['key']]
            continue
        if p['housing']:
            ent['skip_reason'] = ('housing decoration: the page gives no object type or 3D model, and another '
                                  "decoration's model would show the wrong object")
            hits = [r for r in W.rows if r['Object_Type'] in range(49, 72) and difflib.SequenceMatcher(
                None, norm(sname), norm(r['Name'])).ratio() >= 0.9]
            if hits:
                ent['skip_reason'] = f'exists: housing item "{hits[0]["Name"]}" (page spelling differs)'
                ent['existing_id'] = hits[0]['Id_nb']
            continue
        # 3. spelling variants: a near-identical name, same realm, same type/slot/AF or DPS (and bonuses)
        sn = norm(sname)
        variants = {sn}
        for a, b in SLOT_SYNONYMS:
            variants.add(re.sub(rf'\b{a}\b', b, sn))
            variants.add(re.sub(rf'\b{b}\b', a, sn))
        found = None
        for r in W.rows:
            if not realm_ok(r, realm) or not stats_equal(st, r, bon):
                continue
            rn = norm(r['Name'])
            sm = difflib.SequenceMatcher(None, sn, rn)
            if rn in variants or (sm.quick_ratio() >= 0.88 and sm.ratio() >= 0.88):
                if (rn, r['Realm']) in page_names and rn != sn:
                    continue        # that name is another period page's own item
                if re.match(r'^(exceptional|superior|exquisite|gladiator)\b', rn):
                    continue        # the later improved Darkness Falls versions are different items
                if stats_equal(st, r, bon) and (st['af'] is not None or st['dps'] or st['item_type'] == 40):
                    found = r if found is None or difflib.SequenceMatcher(None, sn, rn).ratio() > \
                        difflib.SequenceMatcher(None, sn, norm(found['Name'])).ratio() else found
        if found:
            ent['skip_reason'] = f'exists: same item under our spelling "{found["Name"]}" (same realm, type, slot, stats)'
            ent['existing_id'] = found['Id_nb']
            continue
        todo.append((rec, p, st, bon, unknown, ent, sname))

    # 4. duplicate pages of one item (typo titles / incomplete copies): keep the most complete one
    def completeness(t):
        rec, p, st, bon = t[0], t[1], t[2], t[3]
        return (len(bon), bool(p['prices']), st['quality'] is not None, st['weight'] is not None, -rec['key'])
    groups = []
    for t in todo:
        rec, st, bon, sname = t[0], t[2], t[3], t[6]
        for g in groups:
            o = g[0]
            if REALM[o[0]['realm']] != REALM[rec['realm']] or type_of(o[2]) != type_of(st):
                continue
            if (o[2]['af'], o[2]['dps']) != (st['af'], st['dps']):
                continue
            same_bon = sorted(o[3]) == sorted(bon)
            close = difflib.SequenceMatcher(None, norm(o[6]), norm(sname)).ratio() >= 0.9
            if norm(o[6]) == norm(sname) or (close and (same_bon or not bon or not o[3])):
                g.append(t)
                break
        else:
            groups.append([t])
    keep = []
    for g in groups:
        g.sort(key=completeness, reverse=True)
        keep.append(g[0])
        for t in g[1:]:
            t[5]['skip_reason'] = f'duplicate page of the same item: page {g[0][0]["key"]} "{g[0][0]["name"]}" is built'
            t[5]['same_as'] = f'p165_item_{g[0][0]["key"]}'

    for rec, p, st, bon, unknown, ent, sname in keep:
        row, why = build(rec, p, st, bon, unknown, ent, sname, parsed, allpages, now)
        if row is None:
            ent['skip_reason'] = why
            continue
        ent['status'] = 'built'
        ent['Id_nb'] = row['Id_nb']
        rows.append(row)

    data = {'table': 'ItemTemplate', 'key': 'Id_nb',
            'reason': 'BotRealm period vendor items from Allakhazam item pages (Wayback <= 2003-11), stats from the page',
            'rows': rows}
    json.dump(data, open(OUT, 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
    json.dump(report, open(REPORT, 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
    c = collections.Counter(e['status'] for e in report)
    print('merchant pages', len(vendor), dict(c), 'rows', len(rows))


# --- siblings -------------------------------------------------------------------------------------------------------

def head_word(name):
    w = norm(name).split()
    return w[-1] if w else ''


def siblings(st, sname, realm, level_guess):
    """Existing rows to borrow page-silent values from, best first:
    1. the same item's later Darkness Falls version ("Exceptional/Superior/Exquisite <name>"), same realm and type;
    2. same realm, type and slot, sharing the most name words (same set in another tier), nearest in level."""
    ot, it, _ = type_of(st)
    sn = norm(sname)
    words = set(sn.split())
    out = []
    for r in W.rows:
        if r['Realm'] != realm or not r['Model']:
            continue
        if ot is not None and r['Object_Type'] != ot:
            continue
        if it and r['Item_Type'] != it and not (it in (10, 11) and r['Item_Type'] in (10, 11)):
            continue
        rn = norm(r['Name'])
        m = re.match(r'^(exceptional|superior|exquisite) (.*)$', rn)
        if m and m.group(2).strip() == sn:
            tier = 0
        else:
            if ot not in (None, 0, 41) and ot < 31 or ot in (42, 45):
                if head_word(rn) != head_word(sn):
                    continue    # a weapon / shield / instrument must be the same kind (broadsword, buckler, drum)
            shared = len(words & set(rn.split()))
            if shared == 0:
                continue
            tier = 1
        lv = abs((r['Level'] or 0) - (level_guess or r['Level'] or 0))
        shared = len(words & set(rn.split()))
        sold = r['Id_nb'].lower() in W.sold
        out.append(((tier, -shared, not sold, lv, r['Id_nb']), r))
    out.sort(key=lambda x: x[0])
    return [r for _, r in out]


def period_set_quality(rec, sname, parsed, allpages):
    """Quality of the other period pages of the same set (name without its last word, same realm)."""
    base = ' '.join(norm(sname).split()[:-1])
    if not base:
        return None, None
    vals = collections.Counter()
    src = {}
    for k, r in allpages.items():
        if r['realm'] != rec['realm'] or k == rec['key']:
            continue
        if ' '.join(norm(strip_suffix(r['name'])).split()[:-1]) != base:
            continue
        q = pageparse.num(parsed[k]['fields'].get('Quality:'))
        if q:
            vals[int(q)] += 1
            src.setdefault(int(q), k)
    if not vals:
        return None, None
    q, _ = vals.most_common(1)[0]
    if len(vals) > 1 and vals.most_common(2)[1][1] == vals.most_common(1)[0][1]:
        return None, None
    return q, src[q]


DF_SLOT_WORDS = {'boots', 'gloves', 'helm', 'coif', 'cap', 'vest', 'hauberk', 'robe', 'leggings', 'legs', 'pants',
                 'sleeves', 'arms', 'gauntlets'}


def df_period_quality(rec, sname, parsed, allpages):
    """Darkness Falls item whose page gives no Quality: the period quality of the same set on other period DF pages
    (same realm, sharing the set words of the name); failing that, the most common period DF quality."""
    words = [w for w in norm(sname).split()[:-1] if w not in DF_SLOT_WORDS]   # set words, not the item noun
    allq = collections.Counter()
    for same_realm in (True, False):
        best = collections.defaultdict(collections.Counter)
        src = {}
        for k, r in allpages.items():
            if 'Darkness Falls' not in r['found_in'] or k == rec['key']:
                continue
            q = pageparse.num(parsed[k]['fields'].get('Quality:'))
            if not q:
                continue
            allq[int(q)] += same_realm
            if same_realm and r['realm'] != rec['realm']:
                continue
            rw = set(norm(strip_suffix(r['name'])).split()[:-1])
            shared = sum(1 for w in words if w in rw)
            if shared:
                best[shared][int(q)] += 1
                src.setdefault((shared, int(q)), k)
        for shared in sorted(best, reverse=True):
            c = best[shared].most_common()
            if len(c) == 1 or c[0][1] > c[1][1]:
                q = c[0][0]
                where = 'same realm' if same_realm else 'any realm'
                return q, (f'from period DF page {src[(shared, q)]} (same set words, {where}: {best[shared][q]} pages '
                           f'say {q}%; page {rec["key"]} gives none)')
    if allq:
        q = allq.most_common(1)[0][0]
        return q, f'most common period Darkness Falls quality ({q}%, {allq[q]} pages); no period page of this set gives one'
    return None


def find_spell(page_ability, sib):
    """The spell for the page's charge/proc: the sibling's own when it does what the page says, else an exact
    match (type, damage type, damage) in the spell table."""
    a = {}
    for line in page_ability:
        k, _, v = line.partition(':')
        a.setdefault(k.strip().lower(), v.strip())
    fn = a.get('function', '').lower()
    stype = {'direct damage': 'DirectDamage', 'dmg-over-time': 'DamageOverTime'}.get(fn)
    dmg = pageparse.num(a.get('damage') or a.get('damage per tick'))
    dtype_s = (a.get('damage type') or '').lower() or next((l.split(':', 1)[1].strip().lower() for l in page_ability
                                                             if l.lower().startswith('damage:') and not re.search(r'\d', l)), '')
    dtype = {'crush': 1, 'slash': 2, 'thrust': 3, 'body': 10, 'cold': 11, 'energy': 12, 'heat': 13, 'matter': 14,
             'spirit': 15}.get(dtype_s)                                   # eDamageType
    radius = pageparse.num(a.get('radius'))
    rng = pageparse.num(a.get('range'))

    def ok(sp):
        if not sp or not stype or sp['Type'] != stype or dmg is None or abs((sp['Damage'] or 0) - dmg) > 0.01:
            return False
        if dtype is not None and sp['DamageType'] != dtype:
            return False
        if radius and not sp['Radius']:
            return False
        if not radius and sp['Radius']:
            return False
        return True
    for sid in ('ProcSpellID', 'SpellID', 'ProcSpellID1', 'SpellID1'):
        if sib and sib.get(sid) and ok(W.spells.get(sib[sid])):
            sp = W.spells[sib[sid]]
            if rng and sp['Range'] and sp['Range'] != rng:
                continue
            return sp, f'from {sib["Id_nb"]} ({sid} {sp["SpellID"]} {sp["Name"]})'
    cands = [sp for sp in W.spells.values() if ok(sp) and (not rng or sp['Range'] == rng)]
    if cands:
        sp = min(cands, key=lambda s: s['SpellID'])
        return sp, f'spell table exact match {sp["SpellID"]} {sp["Name"]}'
    return None, f'no spell does "{fn} {dmg} {dtype_s}"' if stype else f'page ability "{fn}" has no server equivalent'


# --- one row --------------------------------------------------------------------------------------------------------

def build(rec, p, st, bon, unknown, ent, sname, parsed, allpages, now):
    realm = REALM.get(rec['realm'], 0)
    der = ent['derived']
    ot, it, hand = type_of(st)
    if it is None:
        return None, f'page slot "{st["slot"]}" is not an equipment slot we know'
    is_magic_item = it in (24, 26, 29, 32, 33, 35)

    # level as DOL uses it for this kind (checked on 464 existing vendor armour and 269 weapon pages)
    level = None
    if st['armor'] and st['af']:
        level = int(st['af']) if st['armor'] == 32 else int(round(st['af'] / 2))
        der['Level'] = 'page (Armor Factor' + (')' if st['armor'] == 32 else ' / 2)')
    elif st['dps'] and ot not in (42, 45):
        level = max(1, min(51, int(round((st['dps'] - 1.2) / 0.3))))
        der['Level'] = 'page (DPS: level = (DPS - 1.2) / 0.3)'
    elif st['magic_level']:
        level = st['magic_level']
        der['Level'] = 'page (Magic Level)'
    elif is_magic_item and st['lou'] and st['lou'][0] > 1:
        level = st['lou'][0] + 1
        der['Level'] = "page (Levels of use + 1, the site's rule on its Magic Level pages)"

    sibs = siblings(st, sname, realm, level)
    if ot is None:
        # the page names no weapon skill: take the type of the same item's later version only
        pre = [r for r in W.rows if r['Realm'] == realm and r['Model'] and re.match(
            r'^(exceptional|superior|exquisite) ' + re.escape(norm(sname)) + '$', norm(r['Name']))]
        if not pre:
            return None, 'page names no weapon skill and no later version of the item exists to take the type from'
        s0 = pre[0]
        ot, it, hand = s0['Object_Type'], s0['Item_Type'], s0['Hand']
        st['weapon'] = ot
        der['Object_Type'] = der['Item_Type'] = der['Hand'] = f'from {s0["Id_nb"]}'
        sibs = [s0] + siblings(st, sname, realm, level)
    if not sibs:
        return None, 'no existing sibling item (same realm, type, slot, set or kind) to take the 3D model from'
    sib = sibs[0]
    pre_sib = sib if re.match(r'^(exceptional|superior|exquisite) ', norm(sib['Name'])) else None

    # magical items / DF gear: an empty bonus list on the page means the page is incomplete
    if not bon and (is_magic_item or 'Darkness Falls' in rec['found_in']):
        sb = [sib[f'Bonus{k}Type'] for k in range(1, 11) if sib[f'Bonus{k}Type']] if pre_sib else []
        if sb or is_magic_item:
            return None, 'page lists no magical bonuses (incomplete page: the item is magical' + \
                (f'; its later version {sib["Id_nb"]} has {len(sb)})' if sb else ')')

    if level is None:
        level = sib['Level']
        der['Level'] = f'from {sib["Id_nb"]}'

    row = {c: None for c in W.cols}
    row.update({
        'Id_nb': f'p165_item_{rec["key"]}', 'ItemTemplate_ID': f'p165_item_{rec["key"]}', 'TranslationId': None,
        'Name': sname, 'ExamineArticle': None, 'MessageArticle': None, 'Level': level,
        'Durability': 50000, 'MaxDurability': 50000, 'IsNotLosingDur': 0, 'Condition': 50000, 'MaxCondition': 50000,
        'Quality': 85, 'DPS_AF': 0, 'SPD_ABS': 0, 'Hand': hand, 'Type_Damage': 0, 'Object_Type': ot,
        'Item_Type': it, 'Color': 0, 'Emblem': 0, 'Effect': 0, 'Weight': 0, 'SalvageExtension': None, 'Model': 0,
        'Extension': 0, 'Bonus': st['bonus_pct'], 'ExtraBonus': 0, 'ExtraBonusType': 0, 'CanUseEvery': 0,
        'SpellID': 0, 'Charges': 0, 'MaxCharges': 0, 'SpellID1': 0, 'Charges1': 0, 'MaxCharges1': 0,
        'ProcChance': 0, 'ProcSpellID': 0, 'ProcSpellID1': 0, 'PoisonSpellID': 0, 'PoisonCharges': 0,
        'PoisonMaxCharges': 0, 'IsPickable': 1, 'IsDropable': 1, 'CanDropAsLoot': 1, 'IsTradable': 1, 'Price': 0,
        'MaxCount': 1, 'IsIndestructible': 0, 'PackSize': 1, 'Realm': realm, 'AllowedClasses': '0', 'Flags': 0,
        'BonusLevel': 0, 'LevelRequirement': 0, 'PackageID': 'True165Vendor', 'Description': None,
        'ClassType': None, 'SalvageYieldID': 0, 'LastTimeRowUpdated': now})
    for k in range(1, 11):
        row[f'Bonus{k}'] = 0
        row[f'Bonus{k}Type'] = 0
    der['Name'] = 'page' if sname == rec['name'] else f'page (Allakhazam tag removed from "{rec["name"]}")'

    # appearance: never on a page
    for c in ('Model', 'Extension', 'Color', 'Effect', 'Emblem', 'SalvageExtension'):
        row[c] = sib[c]
    der['Model'] = der['Extension'] = der['Color'] = der['Effect'] = f'from {sib["Id_nb"]}'
    der['Emblem'] = der['SalvageExtension'] = f'from {sib["Id_nb"]}'

    # armour / weapon numbers
    if st['armor'] and st['af'] is None:
        return None, 'armour page gives no Armor Factor'
    if st['armor']:
        row['DPS_AF'] = int(st['af'])
        der['DPS_AF'] = 'page (Armor Factor)'
        if st['absorb'] is not None:
            row['SPD_ABS'] = int(st['absorb'])
            der['SPD_ABS'] = 'page (Armor Absorb)'
        elif st['armor'] == 32:
            row['SPD_ABS'] = 0
            der['SPD_ABS'] = 'cloth (no absorb)'
        else:
            row['SPD_ABS'] = sib['SPD_ABS']
            der['SPD_ABS'] = f'from {sib["Id_nb"]}'
    elif ot == 45:
        inst = st['instrument'] or next((v for k, v in INSTRUMENT.items() if k in norm(sname).split()), None)
        if inst is None:
            row['DPS_AF'] = sib['DPS_AF']
            der['DPS_AF'] = f'from {sib["Id_nb"]} (instrument type)'
        else:
            row['DPS_AF'] = inst
            der['DPS_AF'] = 'page (instrument type ' + ('field)' if st['instrument'] else 'in the name)')
        row['SPD_ABS'] = sib['SPD_ABS']
        der['SPD_ABS'] = f'from {sib["Id_nb"]}'
    elif ot == 42:
        row['DPS_AF'], row['SPD_ABS'] = sib['DPS_AF'], sib['SPD_ABS']
        der['DPS_AF'] = der['SPD_ABS'] = f'from {sib["Id_nb"]} (page gives no shield DPS / speed)'
        size = st['shield'] or next((v for k, v in {'buckler': 1, 'protector': 2, 'defender': 3}.items()
                                     if head_word(sname) == k and sib['Type_Damage'] == v), None)
        if st['shield']:
            row['Type_Damage'] = st['shield']
            der['Type_Damage'] = 'page (Shield Size)'
        else:
            row['Type_Damage'] = sib['Type_Damage']
            der['Type_Damage'] = f'from {sib["Id_nb"]} (shield size)'
    elif st['dps']:
        row['DPS_AF'] = int(round(st['dps'] * 10))
        der['DPS_AF'] = 'page (DPS' + (', Base DPS line)' if p['fields'].get('mod Base DPS') else ')')
        if st['speed']:
            row['SPD_ABS'] = int(round(st['speed'] * 10))
            der['SPD_ABS'] = 'page (Delay)'
        else:
            row['SPD_ABS'] = sib['SPD_ABS']
            der['SPD_ABS'] = f'from {sib["Id_nb"]}'
        if st['damage']:
            row['Type_Damage'] = st['damage']
            der['Type_Damage'] = 'page (Damage Type)'
        else:
            row['Type_Damage'] = sib['Type_Damage']
            der['Type_Damage'] = f'from {sib["Id_nb"]}'
    elif ot not in (41, 0):
        return None, 'weapon page gives no DPS'

    # quality, weight
    if st['quality']:
        row['Quality'] = st['quality']
        der['Quality'] = 'page'
    else:
        q, k = period_set_quality(rec, sname, parsed, allpages)
        dfq = df_period_quality(rec, sname, parsed, allpages) if ('Darkness Falls' in rec['found_in'] or any(isinstance(x[1], tuple) for x in p['prices'])) else None
        if q:
            row['Quality'] = q
            der['Quality'] = f'from period page {k} (same set, same realm; page {rec["key"]} gives none)'
        elif dfq:
            row['Quality'] = dfq[0]
            der['Quality'] = dfq[1]
        else:
            row['Quality'] = sib['Quality'] or 85
            der['Quality'] = f'from {sib["Id_nb"]}'
    if st['weight']:
        row['Weight'] = st['weight']
        der['Weight'] = 'page'
    else:
        row['Weight'] = sib['Weight']
        der['Weight'] = f'from {sib["Id_nb"]} (page ' + ('says 0.00 lbs)' if p['fields'].get('Weight:') else 'gives none)')
    der['Bonus'] = 'page (Default Bonus)'

    # bonuses
    if len(bon) > 10:
        return None, 'more than 10 bonus lines'
    for k, (prop, val, src) in enumerate(bon, 1):
        row[f'Bonus{k}Type'], row[f'Bonus{k}'] = prop, val
    if bon:
        der['Bonuses'] = 'page: ' + '; '.join(b[2] for b in bon)
    if unknown:
        ent['bonus_lines_not_built'] = unknown

    # class restriction ("Requires Class:"; the class list after "Requires Skill:" only says who can wear it)
    if st['req_classes']:
        ids = []
        for c in st['req_classes']:
            cid = CLASSES.get(c.strip().lower())
            if cid is None:
                return None, f'unknown class "{c}" in Requires Class'
            ids.append(cid)
        row['AllowedClasses'] = ';'.join(str(i) for i in dict.fromkeys(ids))
        der['AllowedClasses'] = 'page (Requires Class: ' + ', '.join(st['req_classes']) + ')'

    # charges / procs
    if p['ability'] or st['charges']:
        spell_sib = pre_sib or sib
        if p['ability']:
            sp, how = find_spell(p['ability'], spell_sib)
        else:
            sid = spell_sib.get('SpellID') or spell_sib.get('ProcSpellID')
            sp = W.spells.get(sid) if pre_sib and sid else None
            how = (f'from {spell_sib["Id_nb"]} (page gives the charges but not the spell)' if sp else
                   'page gives charges but not the spell, and no later version of the item has one')
        if sp is None:
            ent['ability_not_built'] = how
        elif st['charges']:
            row['SpellID'] = sp['SpellID']
            row['Charges'] = row['MaxCharges'] = st['charges'] or spell_sib['MaxCharges'] or spell_sib['Charges']
            der['SpellID'] = how
            der['Charges'] = 'page (Charges)' if st['charges'] else f'from {spell_sib["Id_nb"]}'
            row['CanUseEvery'] = spell_sib['CanUseEvery'] if pre_sib else 0
        else:
            row['ProcSpellID'] = sp['SpellID']
            row['ProcChance'] = spell_sib['ProcChance'] if pre_sib and spell_sib['ProcChance'] else 0
            der['ProcSpellID'] = how
            der['ProcChance'] = (f'from {spell_sib["Id_nb"]}' if row['ProcChance'] else
                                 'server default (0 = DOL default proc chance; page gives none)')

    # price: the page's own (coin or seals), else the same item's later version / same-level sibling
    seals = [x for x in p['prices'] if isinstance(x[1], tuple)]
    cash = [x for x in p['prices'] if isinstance(x[1], int)]
    if seals:
        row['Price'] = seals[0][1][0]
        ent['price_source'] = 'page'
        ent['price_currency'] = seals[0][1][1] + ' Seals'
        der['Price'] = f'page ({seals[0][0]})'
    elif cash:
        row['Price'] = cash[0][1]
        ent['price_source'] = 'page'
        ent['price_currency'] = 'coin'
        der['Price'] = f'page ({cash[0][0]})'
    else:
        ps = None
        if pre_sib and W.sold.get(pre_sib['Id_nb'].lower()):
            ps = pre_sib
        else:
            for s in sibs:
                if s['Level'] == level and s['DPS_AF'] == row['DPS_AF'] and s['Id_nb'].lower() in W.sold \
                        and len(set(norm(s['Name']).split()) & set(norm(sname).split())) >= 2:
                    ps = s
                    break
        if ps:
            row['Price'] = ps['Price']
            cur = sorted(W.sold.get(ps['Id_nb'].lower(), {'coin'}))
            ent['price_source'] = 'sibling'
            ent['price_currency'] = cur[0] + ' Seals' if cur[0] != 'coin' else 'coin'
            der['Price'] = f'from {ps["Id_nb"]} (sold for {ps["Price"]} {ent["price_currency"]})'
        else:
            ent['price_source'] = 'none'
            der['Price'] = 'none: the page gives no price and no sibling sells at the same level'

    # flags of the sibling when it is itself a vendor item
    if sib['Id_nb'].lower() in W.sold:
        for c in ('IsPickable', 'IsDropable', 'CanDropAsLoot', 'IsTradable', 'MaxCount', 'PackSize'):
            row[c] = sib[c]
        der['flags'] = f'from {sib["Id_nb"]} (IsPickable/IsDropable/CanDropAsLoot/IsTradable/MaxCount/PackSize)'
    ent['sibling'] = sib['Id_nb']
    return row, None


if __name__ == '__main__':
    main()
