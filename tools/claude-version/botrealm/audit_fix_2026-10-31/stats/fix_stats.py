"""stats agent (True 1.65 fix pass): ItemTemplate rows with ToA-or-later stats -> true 1.65 values.
Read-only on the audit snapshot; writes only into this folder.
    python -I fix_stats.py      -> updates.json, stats_changes.csv, items.json (per-item decisions), alt_apr28.json
Sources: period Allakhazam item pages (cache work/items/item_sources.json, built from period_item_pages.jsonl with
pageparse.parse + build_vendoritems165.page_stats/map_bonuses, plus analyze.page_bonuses for the alias lines),
then Uthgard item pages (same cache, parsed from raw_pages.repaired.jsonl.gz), else strip the later bonus types."""
import collections, csv, json, os, re, sqlite3, sys

sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
AUDIT = os.path.normpath(os.path.join(HERE, '..', '..'))
sys.path.insert(0, os.path.join(AUDIT, 'work', 'items'))
import analyze as A  # noqa  (pure helpers; also imports build_vendoritems165 as A.bv)
bv = A.bv

DB = os.path.join(AUDIT, 'world165_audit.db')

# ---------------------------------------------------------------- what counts as ToA-or-later
# 99-114 Catacombs class lines, 115 unified Archery, 116-119, 145-256 (cap bonuses, PowerPool %, Acuity, All*,
# speed/damage/ToA bonuses, ExtraHP %, mythical, XP/RP/BP ...), 157-162 Catacombs focus.
LATER = set(range(99, 120)) | set(range(145, 257))
# types that period pages (<= 2003-11-01) prove existed on 1.64/1.65 SI epic-dungeon / dragon loot:
# kept ONLY where the item's own period or Uthgard page lists them (never on stripped-only items)
PERIOD_ATTESTED = {156, 163, 164, 165}
NAME = dict(A.PROPNAME)
NAME.update({9: 'Power(pts)', 10: 'Hits', 188: 'ArcherySpeed', 201: 'StrCapBonus', 221: 'BodyResCapBonus',
             236: 'MythicalStrCapBonus'})
BOW_BY_OT = {9: 36, 15: 68, 18: 83}           # Longbow / Composite / Recurve object types -> period skills
BOW_BY_REALM = {1: 36, 2: 68, 3: 83}
ARCHAIC = re.compile(r'^(earth|wind|dark|light|mana|mind|void) resist', re.I)   # pre-rename (2001) page data

# pages whose own patch note says the item's bonuses changed while the listed bonuses are still the old ones
# (or none): read by hand, see REPORT.md
STALE_NOTE = {
    4173: 'patch 1.62 note "now has stat increases" but the page lists no bonuses',
    2532: 'patch 1.53 note "+pac stat increase changed" but the page still lists Pacification',
    2882: 'patch 1.53 note "+pac stat increase changed" but the page still lists Pacification',
    2003: 'patch 1.53 note "+beastcraft stat increase changed" but the page still lists Beastcraft',
    6355: 'patch 1.61 note "+valor replaced" but the page still lists Valor',
    4596: 'patch note "matter resist in place of a body resist that occurred twice" but the page lists Body +10',
    2478: 'patch note "granted higher bonuses due to the level" - cannot tell if the listed values are the new ones',
    5987: 'patch note "received additional bonuses due to its level" - cannot tell if the listed values are the new ones',
}
ARMOR_OT = set(range(32, 39))
UTH_SLOT = {'torso': 25, 'body': 25, 'helm': 21, 'head': 21, 'feet': 23, 'hands': 22, 'hand': 22, 'arms': 28,
            'sleeves': 28, 'legs': 27, 'wrist': 33, 'bracer': 33, 'ring': 35, 'neck': 29, 'waist': 32, 'jewel': 24,
            'cloak': 26, 'back': 26, 'rhand': 10, 'lhand': 11, '2hand': 12, 'ranged': 13}
UTH_ARMOR = {'cloth': 32, 'leather': 33, 'studded': 34, 'chain': 35, 'plate': 36, 'reinforced': 37, 'scale': 38}


def nm(p):
    return NAME.get(p, str(p))


def fmt(bl):
    return ', '.join(f'{nm(p)} {v:+d}' for p, v in bl) or '(none)'


def db_bon(it):
    out = []
    for k in range(1, 11):
        t, v = it[f'Bonus{k}Type'], it[f'Bonus{k}']
        if t and v:
            out.append((t, v))
    if it['ExtraBonusType'] and it['ExtraBonus']:
        out.append((it['ExtraBonusType'], it['ExtraBonus']))
    return out


def sl(s):
    """quote for SQL"""
    return "'" + str(s).replace("'", "''") + "'"


def bow_skill(it):
    return BOW_BY_OT.get(it['Object_Type']) or BOW_BY_REALM.get(it['Realm'])


# ---------------------------------------------------------------- page bonuses (period)
def period_bonuses(p, it):
    """-> (list[(prop,val,line)], unmapped lines). Uses map_bonuses output + analyze's alias lines;
    'ALL Magic Skills N' twice = skill + focus (the larger one is All Focus Levels); 'Bow N pts' = the realm's bow."""
    out = [(b[0], b[1], b[2]) for b in p['bonuses']]
    unk = []
    allmagic = []
    for u in p.get('unknown_bonuses', []):
        m = re.match(r'(.+?)\s+(-?\d+)\s*(pts|%|lvls?)?\s*$', u)
        if not m:
            unk.append(u)
            continue
        name, val, unit = m.group(1).strip(), int(m.group(2)), m.group(3) or ''
        if val == 0:
            continue
        if name.lower() == 'all magic skills':
            allmagic.append((val, u))
            continue
        if name.lower() == 'bow':
            b = bow_skill(it)
            if b:
                out.append((b, val, u + ' (realm bow skill)'))
                continue
        prop = A.canon_line(name, val, unit)
        if prop:
            out.append((prop, val, u))
        else:
            unk.append(u)
    # old page format: "Focus Bonuses:|- Earth Magic: 34 lvls" (pageparse does not read these)
    for m in re.finditer(r'\|Focus Bonuses:?((?:\|- [^|]*)+)', RAW.get(p['key'], '')):
        for line in m.group(1).split('|- ')[1:]:
            mm = re.match(r'\s*(.+?):\s*(\d+)\s*(?:lvls?|levels)', line)
            if not mm:
                continue
            prop = A.canon_line(mm.group(1), int(mm.group(2)), 'lvls', focus_block=True)
            if prop and not any(o[0] == prop for o in out):
                out.append((prop, int(mm.group(2)), 'Focus Bonuses: ' + line.strip()))
            elif not prop:
                unk.append('Focus Bonuses: ' + line.strip())
    # staves on old pages write focus levels as "+43 pts": no skill bonus is that high
    if it['Object_Type'] == 8 or (p['raw_fields'].get('Requires Skill:') or '').strip().lower() == 'staff':
        fixed = []
        for prop, val, line in out:
            if 20 <= prop <= 98 and val > 15:
                nm_ = re.sub(r'\s*[+-]?\d+.*$', '', line).strip().lower()
                f = bv.FOCUS.get(nm_) or (A.ALIAS.get(nm_) or (None, None))[1]
                if f and not any(o[0] == f for o in out):
                    fixed.append((f, val, line + ' (staff: focus levels)'))
                    continue
            fixed.append((prop, val, line))
        out = fixed
    allmagic.sort()
    for i, (val, u) in enumerate(allmagic):
        if len(allmagic) == 2 and i == 1:
            out.append((165, val, u + ' (second, larger line = All Focus Levels)'))
        else:
            out.append((163, val, u))
    return out, unk


def uth_bonus(u, it):
    out = [(b[0], b[1], b[2]) for b in u['bonuses']]
    unk = []
    for v in u.get('unknown', []):
        m = re.match(r'(.+?):\s*([+-]?\d+)\s*(pts|%|lvls?)?\s*$', v)
        if not m:
            continue                       # "N Level", "N Charges", shield size ... (not bonuses)
        name, val, unit = m.group(1).strip(), int(m.group(2)), m.group(3) or ''
        if name.lower() == 'all spell lines':
            out.append((163, val, v))
            continue
        if name.lower() in ('bow', 'archery'):
            b = bow_skill(it)
            if b:
                out.append((b, val, v + ' (realm bow skill)'))
                continue
        prop = A.canon_line(name, val, unit)
        if prop:
            out.append((prop, val, v))
        else:
            unk.append(v)
    return out, unk


# ---------------------------------------------------------------- load
con = sqlite3.connect(f'file:{DB}?mode=ro', uri=True)
con.row_factory = sqlite3.Row
COLS = [r[1] for r in con.execute('pragma table_info(ItemTemplate)')]
items = [dict(r) for r in con.execute('select * from ItemTemplate')]
loot = collections.defaultdict(set)
for r in con.execute('select TemplateName, ItemTemplateID from LootTemplate'):
    loot[r[1].lower()].add(r[0])
for r in con.execute('select MobName, ItemTemplateID from LootOTD'):
    loot[r[1].lower()].add(r[0] + ' (OTD)')
merch = set(r[0].lower() for r in con.execute('select ItemTemplateID from MerchantItem'))
spells = set(r[0] for r in con.execute('select SpellID from Spell'))
# mob name -> zones (for the coordinator's "keep the 1.64 epic/dragon types" rule)
_zones = [(r[1], r[2], r[3] * 8192, (r[3] + r[5]) * 8192, r[4] * 8192, (r[4] + r[6]) * 8192)
          for r in con.execute('select ZoneID, RegionID, Name, OffsetX, OffsetY, Width, Height from Zones')]
MOBZONES = collections.defaultdict(set)
for n_, reg_, x_, y_ in con.execute('select Name, Region, X, Y from Mob where Realm=0'):
    for z in _zones:
        if z[0] == reg_ and z[2] <= x_ < z[3] and z[4] <= y_ < z[5]:
            MOBZONES[n_.lower()].add(z[1])
            break
TEMPLATE_MOBS = collections.defaultdict(set)
for m_, t_ in con.execute('select MobName, LootTemplateName from MobXLootTemplate'):
    TEMPLATE_MOBS[t_.lower()].add(m_.lower())
con.close()
# coordinator decision 2 (2026-10-31): on stripped-only items, keep Acuity / AllMagic / AllMelee / AllFocus when the
# item drops from the 1.64 SI epic dungeons or the dragons' zones/lairs (period pages Aug-Oct 2003 show these types
# there): Galladoria, Caer Sidi, Tuscaran Glacier, Sheeroe Hills (Cuuldurach), Malmohus (Gjalpinulva), Dartmoor
# (Golestandt), Crystal Cave (Xanxicar). Summoner's Hall: no period page shows them -> stripped.
KEEP_ZONES = {'galladoria', 'caer sidi', 'tuscaran glacier', 'sheeroe hills', 'malmohus', 'dartmoor', 'crystal cave'}
KEEP_MOBS = {'apocalypse': 'Caer Sidi', 'bellum': 'Caer Sidi', 'corrupt primal of water': 'Galladoria'}  # scripted


def family_of(dropped_by):
    hits = []
    for t_ in dropped_by:
        t0 = t_.replace(' (OTD)', '').lower()
        for m_ in sorted(TEMPLATE_MOBS.get(t0) or {t0}):
            zs = {z.lower() for z in MOBZONES.get(m_, set())} & KEEP_ZONES
            if m_ in KEEP_MOBS:
                zs.add(KEEP_MOBS[m_].lower())
            if zs:
                hits.append(f"{m_} ({', '.join(sorted(zs))})")
    return hits

src = json.load(open(os.path.join(AUDIT, 'work', 'items', 'item_sources.json'), encoding='utf-8'))
RAW = {}
RAWALL = {}
for line in open(os.path.join(A.VW, 'period_item_pages.jsonl'), encoding='utf-8'):
    r_ = json.loads(line)
    if 'Focus Bonuses' in r_['raw']:
        RAW[r_['key']] = r_['raw']
    RAWALL[r_['key']] = r_['raw']
PP = collections.defaultdict(list)
for p in src['period']:
    if any('Unique Item' in x or 'Unique Object' in x or 'Stats will very' in x for x in p['found_in']):
        continue                     # Allakhazam's random "Unique Item" entries (ROGs), not this item
    PP[A.norm(p['name'])].append(p)
UT = collections.defaultdict(list)
for u in src['uthgard']:
    if u.get('bonuses_raw') or u.get('Level') not in (None, '', '0'):
        UT[A.norm(u['name'])].append(u)

later_list = {r['item_id'].lower(): r for r in json.load(open(os.path.join(AUDIT, 'out', 'later_stat_items.json'), encoding='utf-8'))}
actions = {r['item_id'].lower(): r for r in json.load(open(os.path.join(AUDIT, 'out', 'item_actions.json'), encoding='utf-8'))}
fixstats = {k for k, r in actions.items() if r['action'].startswith('KEEP, FIX STATS')}
remove_ids = {k for k, r in actions.items() if r['action'].startswith('REMOVE')}
try:
    fs = json.load(open(os.path.join(AUDIT, 'out', 'freeshard_items.json'), encoding='utf-8'))
    fs_list = fs if isinstance(fs, list) else fs.get('items', fs.get('findings', []))
    freeshard_ids = {str(r.get('item_id', '')).lower() for r in fs_list if isinstance(r, dict)}
except Exception:
    freeshard_ids = set()


# ---------------------------------------------------------------- matching
def classes_of(it):
    s = (it['AllowedClasses'] or '').strip()
    if s in ('', '0'):
        return set()
    return {int(x) for x in re.split(r'[;,]', s) if x.strip().isdigit() and int(x) > 0}


def page_ok(p, it):
    """type / slot / realm / class consistency of a period page with a DB row -> (ok, why)"""
    pr = A.REALM.get(p['realm'], 0)
    if it['Realm'] and pr and pr != it['Realm']:
        return False, 'realm'
    st = bv.page_stats({'fields': p['raw_fields'], 'classes': p['classes'], 'levels_of_use': p['lou']})
    ot, ityp, _ = bv.type_of(st)
    dot = it['Object_Type']
    if ot is not None and dot and ot != dot:
        return False, f'object type page {ot} vs db {dot}'
    if ityp and it['Item_Type'] and ityp != it['Item_Type'] and not ({ityp, it['Item_Type']} <= {10, 11, 12, 13}):
        return False, f'slot page {ityp} vs db {it["Item_Type"]}'
    cl = classes_of(it)
    if cl and p['raw_fields'].get('Requires Class:'):
        pc = {bv.CLASSES.get(c.strip().lower()) for c in p['classes']} - {None}
        if pc and not (pc & cl):
            return False, 'class'
    return True, ''


def uth_ok(u, it):
    s = (u.get('Slot') or '').lower()
    if s in UTH_SLOT and it['Item_Type'] and UTH_SLOT[s] != it['Item_Type'] and \
            not ({UTH_SLOT[s], it['Item_Type']} <= {10, 11, 12, 13}):
        return False
    t = (u.get('Item type') or '').lower()
    if t in UTH_ARMOR and it['Object_Type'] in ARMOR_OT and UTH_ARMOR[t] != it['Object_Type']:
        return False
    if t in UTH_ARMOR and it['Object_Type'] not in ARMOR_OT:
        return False
    if it['Object_Type'] in ARMOR_OT and t and t not in UTH_ARMOR:
        return False
    return True


def bon_dist(a, b):
    da, db_ = collections.Counter(), collections.Counter()
    for p, v, *_ in a:
        if p not in LATER:
            da[10 if p == 250 else p] += v
    for p, v, *_ in b:
        db_[10 if p == 250 else p] += v
    return sum(abs(da[k] - db_[k]) for k in set(da) | set(db_))


def num_dist(it, af, dps):
    if it['Object_Type'] in ARMOR_OT and af:
        return abs(it['DPS_AF'] - af)
    if dps:
        return abs(it['DPS_AF'] - dps * 10) / 3
    return 0


# group same-name DB rows: if they carry different (non-later) bonus type sets, a single page fits only the closest
by_name = collections.defaultdict(list)
for it in items:
    by_name[A.norm(it['Name'])].append(it)


def choose_period(it):
    cands = []
    rej = []
    for p in PP.get(A.norm(it['Name']), []):
        ok, why = page_ok(p, it)
        if not ok:
            rej.append(f"{p['key']}:{why}")
            continue
        bl, unk = period_bonuses(p, it)
        pdps = A.pageparse_num(p['raw_fields']['DPS:']) if p['raw_fields'].get('DPS:') else p['dps']
        cands.append((num_dist(it, p['af'], pdps), bon_dist(db_bon(it), bl), -int(p['capture'] or 0), p, bl, unk))
    if not cands:
        return None, rej
    cands.sort(key=lambda c: c[:3])
    return cands[0], rej


def choose_uth(it):
    cands = []
    for u in UT.get(A.norm(it['Name']), []):
        if not uth_ok(u, it):
            continue
        bl, unk = uth_bonus(u, it)
        try:
            lv = int(u.get('Level') or 0)
        except ValueError:
            lv = 0
        cands.append((abs(lv - it['Level']) if lv else 5, num_dist(it, u.get('af'), u.get('dps')),
                      bon_dist(db_bon(it), bl), u, bl, unk))
    if not cands:
        return None
    cands.sort(key=lambda c: c[:3])
    return cands[0]


def variant_conflict(it, bl):
    """True when another same-name, same-realm DB row fits this page's bonus TYPES better (class variants of
    quest rewards etc.), so the page is not clearly this row's."""
    pt = {10 if p == 250 else p for p, *_ in bl if p not in LATER}
    if not pt:
        return False
    mine = {10 if p == 250 else p for p, _ in db_bon(it) if p not in LATER}

    def jac(s):
        return len(s & pt) / len(s | pt) if s | pt else 0
    me = jac(mine)
    for o in by_name[A.norm(it['Name'])]:
        if o is it or (o['Realm'] and it['Realm'] and o['Realm'] != it['Realm']):
            continue
        co, ci = classes_of(o), classes_of(it)
        if (co == ci or not co or not ci) and o['Object_Type'] == it['Object_Type']:
            continue                 # a plain duplicate row of the same item (e.g. an Atlas copy): same page fits both
        ot_ = {10 if p == 250 else p for p, _ in db_bon(o) if p not in LATER}
        if ot_ != mine and jac(ot_) > me + 1e-9:
            return True
    return False


# ---------------------------------------------------------------- decide per item
def target_from_bonuses(bl):
    """bonus list -> {Bonus1..10, types, ExtraBonus} (11th line goes to ExtraBonus)"""
    t = {}
    bl = [(p, v) for p, v, *_ in bl]
    for k in range(1, 11):
        p, v = bl[k - 1] if k <= len(bl) else (0, 0)
        t[f'Bonus{k}Type'], t[f'Bonus{k}'] = p, v
    p, v = bl[10] if len(bl) > 10 else (0, 0)
    t['ExtraBonusType'], t['ExtraBonus'] = p, v
    return t


def same_bonus_set(it, bl):
    return sorted(db_bon(it)) == sorted((p, v) for p, v, *_ in bl)


def num_targets(it, af, absorb, dps, speed, quality):
    t = {}
    ot = it['Object_Type']
    if ot in ARMOR_OT:
        if af:
            t['DPS_AF'] = int(round(af))
            if absorb is not None:
                t['SPD_ABS'] = int(round(absorb))
            elif ot == 32:
                t['SPD_ABS'] = 0
    elif (1 <= ot <= 31) and dps:
        t['DPS_AF'] = int(round(dps * 10))
        if speed:
            t['SPD_ABS'] = int(round(speed * 10))
    if quality:
        t['Quality'] = int(quality)
    return t


records = []
alt = []
for it in items:
    iid = it['Id_nb']
    key = iid.lower()
    cur = db_bon(it)
    later_now = [(p, v) for p, v in cur if p in LATER]
    best, rej = choose_period(it)
    reason_scope = []
    if later_now:
        reason_scope.append('later bonus types: ' + ', '.join(sorted({nm(p) for p, _ in later_now})))
    if key in later_list and later_list[key]['exceeds_period_page']:
        reason_scope.append('above period page (audit): ' + '; '.join(later_list[key]['exceeds_period_page']))
    if key in fixstats:
        reason_scope.append('item_actions: ' + actions[key]['action'])
    # "values above their period page": any bonus higher than / missing from the page (no later types needed)
    if best and not later_now:
        _, _, _, p, bl, unk = best
        if not unk and not ARCHAIC.search(' '.join(p.get('unknown_bonuses', []))):
            pv = collections.Counter()
            for q, v, *_ in bl:
                pv[q] += v
            sv = collections.Counter()
            for q, v in cur:
                sv[10 if q == 250 else q] += v
            higher = [f'{nm(q)} {sv[q]:+d} vs page {pv[q]:+d}' for q in sv if sv[q] > pv.get(q, 0)]
            if higher and (bl or (p['raw_fields'].get('Default Bonus:', '0%').strip() in ('0%', '') and not p['flags'])):
                reason_scope.append('above its period page: ' + '; '.join(higher))
    if not reason_scope:
        continue
    rec = {'id': iid, 'name': it['Name'], 'realm': it['Realm'], 'level': it['Level'], 'package': it['PackageID'],
           'object_type': it['Object_Type'], 'item_type': it['Item_Type'], 'scope': reason_scope,
           'dropped_by': sorted(loot.get(key, []))[:12], 'dropped_by_all': sorted(loot.get(key, [])), 'merchant': key in merch,
           'remove_listed': key in remove_ids, 'before': fmt(cur)}
    if rej:
        rec['period_pages_rejected'] = rej
    source = None
    notes = []
    target = {}
    # ---- 1. period page
    if best:
        _, _, _, p, bl, unk = best
        raw_unk = ' | '.join(p.get('unknown_bonuses', []))
        dflt = (p['raw_fields'].get('Default Bonus:') or '0%').strip()
        u = choose_uth(it)
        usable = True
        server_plain = [q for q, _ in cur if q not in LATER]
        lu = (p['last_updated'] or '')
        if p['key'] in STALE_NOTE:
            usable = False
            notes.append(f"period page {p['key']} is stale by its own patch note: {STALE_NOTE[p['key']]}")
        elif ARCHAIC.search(raw_unk) or any(ARCHAIC.search(x) for x in unk):
            usable = False
            notes.append(f"period page {p['key']} uses the pre-rename resist names ({raw_unk}): stale 2001-era "
                         'data, not the 1.65 item')
        elif unk:
            usable = False
            notes.append(f"period page {p['key']} has bonus lines with no server property ({'; '.join(unk)})")
        elif not bl and (dflt not in ('0%', '') or 'Incomplete info' in p['flags'] or it['Item_Type'] in (24, 26, 29, 32, 33, 35)
                         and cur and not (u and not u[4])):
            usable = False
            notes.append(f"period page {p['key']} lists no bonuses for a magical item (Default Bonus {dflt}"
                         f"{', jewelry/cloak slot' if it['Item_Type'] in (24, 26, 29, 32, 33, 35) else ''}): incomplete")
        elif not bl and server_plain and 'Apr 28 12:12:32 2003' in lu and not (u and not u[4]):
            usable = False
            notes.append(f"period page {p['key']} lists no bonuses and was never edited after Allakhazam's "
                         "Apr-28-2003 bulk import: not proof that the 1.65 item had none")
        elif not bl and any(120 <= q <= 144 for q in server_plain) and it['Object_Type'] == 8:
            usable = False
            notes.append(f"period page {p['key']} lists no bonuses for a focus staff: incomplete")
        elif not bl and cur and u and u[4]:
            usable = False
            notes.append(f"period page {p['key']} lists no bonuses but Uthgard {u[3]['url'].rsplit('=', 1)[-1]} "
                         'lists them: page incomplete')
        elif variant_conflict(it, bl):
            usable = False
            notes.append(f"period page {p['key']} fits another same-name variant of this item better (class/realm "
                         'variants); not applied to this row')
        lu = (p['last_updated'] or '')
        if usable and u and 'Apr 28 12:12:32 2003' in lu:
            ub = [(q, v) for q, v, *_ in u[4]]
            srv = [(10 if q == 250 else (bow_skill(it) if q == 115 else q), v) for q, v in cur
                   if q not in LATER or q in (115, 250)]
            if ub and sorted(ub) == sorted(srv) and sorted(ub) != sorted((q, v) for q, v, *_ in bl):
                usable = False
                pb_ = fmt([(q, v) for q, v, *_ in bl])
                rec['apr28_uthgard_agrees'] = {'period_page': p['key'], 'period_bonuses': pb_,
                                               'uthgard': u[3]['url'], 'uthgard_bonuses': fmt(ub)}
                notes.append(f"Apr-28-2003 bulk-dated period page {p['key']} ({pb_}) is lower than the server, and "
                             f"Uthgard {u[3]['url'].rsplit('=', 1)[-1]} agrees exactly with the server ({fmt(ub)}): "
                             'Uthgard used (coordinator decision 1)')
        if usable and not bl and server_plain and u and not u[4]:
            notes.append(f"period page {p['key']} and Uthgard {u[3]['url'].rsplit('=', 1)[-1]} both list no bonuses: "
                         'plain item in 1.65')
        if usable:
            source = ('period', f"period-zam item {p['key']} '{p['name']}' ({p['realm']}, capture {p['capture'][:8]}"
                      f", last updated {(p['last_updated'] or '?').strip()})")
            target.update(target_from_bonuses(bl))
            # the page's own "DPS:" field; the "Damage Modifiers / Base DPS" line is the viewer's clamped value on
            # some pages (Truesilver Recurve Bow: DPS 14.6, "Base DPS: 3.10")
            pdps = A.pageparse_num(p['raw_fields']['DPS:']) if p['raw_fields'].get('DPS:') else p['dps']
            target.update(num_targets(it, p['af'], p['absorb'], pdps, p['speed'], p['quality']))
            # item Level: period pages give no DOL item level; only when the page changes AF / DPS so much that
            # the server level no longer fits it (> 2 levels off the DOL rule) is Level re-derived from the page
            if target.get('DPS_AF') is not None and target['DPS_AF'] != it['DPS_AF']:
                ot = it['Object_Type']

                def rule(x):
                    if ot in ARMOR_OT:
                        return x if ot == 32 else int(round(x / 2))
                    return max(1, min(51, int(round((x / 10 - 1.2) / 0.3))))
                lv = rule(target['DPS_AF'])
                if abs(lv - rule(it['DPS_AF'])) > 2 and lv != it['Level']:
                    target['Level'] = lv
                    notes.append(f"Level {it['Level']} -> {lv}: derived from the page's "
                                 f"{'Armor Factor' if ot in ARMOR_OT else 'DPS'} by the DOL rule")
            later_kept = [nm(q) for q, *_ in bl if q in LATER]
            if later_kept:
                notes.append('kept because the period page lists them: ' + ', '.join(later_kept))
            if p['ability'] and not (it['SpellID'] or it['ProcSpellID'] or it['SpellID1'] or it['ProcSpellID1']):
                notes.append('page lists a magical ability the server row lacks (not added): ' + '; '.join(p['ability']))
            srv_spell = it['SpellID'] or it['ProcSpellID'] or it['SpellID1'] or it['ProcSpellID1']
            if srv_spell and not (p['ability'] or p['charges'] or p['charged'] or p['proc_level']) and                     'Charged' not in RAWALL.get(p['key'], '') and 'Proc' not in RAWALL.get(p['key'], ''):
                rec['spell_not_on_page'] = {c: it[c] for c in ('SpellID', 'Charges', 'MaxCharges', 'SpellID1',
                                            'ProcSpellID', 'ProcSpellID1', 'ProcChance') if it[c]}
                notes.append('server row has a spell/proc the period page does not show (listed, not changed)')
    # ---- 2. Uthgard
    if source is None:
        u = choose_uth(it)
        if u and not u[5]:
            _, _, _, up, bl, unk = u
            bad = [nm(q) for q, *_ in bl if q in LATER and q not in PERIOD_ATTESTED]
            bl2 = [b for b in bl if not (b[0] in LATER and b[0] not in PERIOD_ATTESTED)]
            if bad:
                notes.append('Uthgard page has ToA-only types, dropped: ' + ', '.join(bad))
            if not bl2 and [q for q, _ in cur if q not in LATER]:
                notes.append(f"Uthgard {up['url']} lists no bonuses while the item has 1.65-type ones: "
                             'not proof the item had none')
            else:
                source = ('uthgard', f"uthgard item {up['url'].rsplit('=', 1)[-1]} '{up['name']}'" +
                          (f" (Apr-28-2003 period page {rec['apr28_uthgard_agrees']['period_page']} is lower)"
                           if rec.get('apr28_uthgard_agrees') else ''))
                target.update(target_from_bonuses(bl2))
                target.update(num_targets(it, up.get('af'), up.get('absorb'), up.get('dps'), up.get('speed'),
                                          up.get('quality')))
                try:
                    ulv = int(up.get('Level') or 0)
                except ValueError:
                    ulv = 0
                if 1 <= ulv <= 51:
                    target['Level'] = ulv
        elif u and u[5]:
            notes.append(f"Uthgard {u[3]['url']} has unmapped bonus lines: {'; '.join(u[5])}")
    own_scope_only = all(x.startswith('above its period page') for x in reason_scope)
    if own_scope_only and (source is None or source[0] != 'period') and \
            not (rec.get('apr28_uthgard_agrees') and source and source[0] == 'uthgard'):
        rec['decision'] = 'above its period page, but that page is not usable for it: left unchanged'
        rec['notes'] = notes
        records.append(rec)
        continue
    # ---- 3. strip only
    if source is None:
        if not later_now:
            rec['decision'] = 'no source usable and no later bonus types: left unchanged'
            rec['notes'] = notes
            records.append(rec)
            continue
        source = ('strip', 'no usable period or Uthgard page')
        bl = []
        conv = []
        fam = family_of(rec['dropped_by_all'])
        keep_att = []
        for q, v in cur:
            if q in PERIOD_ATTESTED and fam:
                bl.append((q, v))
                keep_att.append(f'{nm(q)} {v:+d}')
                continue
            if q == 115 and bow_skill(it):
                bl.append((bow_skill(it), v))
                conv.append(f'Archery {v:+d} -> {nm(bow_skill(it))}')
            elif q in LATER:
                continue
            else:
                bl.append((q, v))
        if keep_att:
            rec['kept_epic_dragon_types'] = {'kept': keep_att, 'dropped_by': fam[:6]}
            notes.append('kept ' + ', '.join(keep_att) + ' (1.64 epic-dungeon/dragon loot type, coordinator '
                         'decision 2): dropped by ' + '; '.join(fam[:4]))
        if conv:
            notes.append('unified Archery renamed to the period bow skill: ' + '; '.join(conv))
        target.update(target_from_bonuses(bl))
    # ---- diff
    changes = {c: v for c, v in target.items() if it[c] != v and not (it[c] in (None, '') and v == 0)}
    # bonus slots: unchanged when the same multiset is already there (only positions differ)
    tb = [(target[f'Bonus{k}Type'], target[f'Bonus{k}']) for k in range(1, 11) if target.get(f'Bonus{k}Type')]
    if target.get('ExtraBonusType'):
        tb.append((target['ExtraBonusType'], target['ExtraBonus']))
    bonus_cols = [c for c in changes if c.startswith('Bonus') or c.startswith('ExtraBonus')]
    if bonus_cols and sorted(tb) == sorted(cur):
        junk = any((it[f'Bonus{k}Type'] and not it[f'Bonus{k}']) or (it[f'Bonus{k}'] and not it[f'Bonus{k}Type'])
                   for k in range(1, 11))
        if not junk:
            for c in bonus_cols:
                changes.pop(c)
    rec.update({'source': source[0], 'source_key': source[1], 'notes': notes,
                'after': fmt(tb) if source else rec['before']})
    if not changes:
        rec['decision'] = 'already matches its source'
        records.append(rec)
        continue
    # write ALL bonus columns when any bonus changes (absolute, idempotent)
    if any(c.startswith('Bonus') or c.startswith('ExtraBonus') for c in changes):
        for k in range(1, 11):
            changes[f'Bonus{k}Type'] = target[f'Bonus{k}Type']
            changes[f'Bonus{k}'] = target[f'Bonus{k}']
        changes['ExtraBonusType'] = target['ExtraBonusType']
        changes['ExtraBonus'] = target['ExtraBonus']
    rec['decision'] = {'period': 'set to period page', 'uthgard': 'set to Uthgard page', 'strip': 'stripped only'}[source[0]]
    rec['changes'] = {c: [it[c], v] for c, v in changes.items()}
    records.append(rec)

# ---------------------------------------------------------------- outputs
ORDER = ['Level', 'Quality', 'DPS_AF', 'SPD_ABS'] + [f'Bonus{k}' for k in range(1, 11)] + \
        [f'Bonus{k}Type' for k in range(1, 11)] + ['ExtraBonus', 'ExtraBonusType']


def sql_for(iid, ch):
    sets = ', '.join(f'{c}={int(ch[c])}' for c in ORDER if c in ch)
    return f'UPDATE ItemTemplate SET {sets} WHERE lower(Id_nb)=lower({sl(iid)})'


updates, rows_csv = [], []
for r in records:
    if 'changes' not in r:
        continue
    ch = {c: v[1] for c, v in r['changes'].items()}
    label = {'period': '1.65 period item stats', 'uthgard': '1.65 item stats (Uthgard)',
             'strip': '1.65: ToA-or-later bonus types removed (no source)'}[r['source']]
    updates.append({'sql': sql_for(r['id'], ch), 'reason': f"{label}: {r['name']} ({r['source_key']})"})
    before_num = ' '.join(f"{c}={r['changes'][c][0]}" for c in ('Level', 'Quality', 'DPS_AF', 'SPD_ABS') if c in r['changes'])
    after_num = ' '.join(f"{c}={r['changes'][c][1]}" for c in ('Level', 'Quality', 'DPS_AF', 'SPD_ABS') if c in r['changes'])
    rows_csv.append([r['name'], r['id'], r['source_key'], (r['before'] + (' | ' + before_num if before_num else '')),
                     (r['after'] + (' | ' + after_num if after_num else ''))])
for r in records:
    r.pop('dropped_by_all', None)

json.dump(updates, open(os.path.join(HERE, 'updates.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
# FINAL: same statements keyed on the NOCASE primary key (indexed; identical rows matched)
fast = [{'sql': u['sql'].replace('WHERE lower(Id_nb)=lower(', 'WHERE Id_nb=(', 1), 'reason': u['reason']} for u in updates]
json.dump(fast, open(os.path.join(HERE, 'updates_final.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
json.dump(records, open(os.path.join(HERE, 'items.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
with open(os.path.join(HERE, 'stats_changes.csv'), 'w', encoding='utf-8-sig', newline='') as f:
    w = csv.writer(f)
    w.writerow(['item', 'id', 'source', 'before', 'after'])
    w.writerows(rows_csv)

c = collections.Counter(r['decision'] if isinstance(r['decision'], str) else '?' for r in records)
print('in scope', len(records), dict(c))
print('updates', len(updates))
print(collections.Counter(r.get('source') for r in records if 'changes' in r))
