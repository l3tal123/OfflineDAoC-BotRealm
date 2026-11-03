"""Missing period merchants: placement + period stock (missing_merchants.json). Read-only.
Evidence gathered by mm_search.py (mm_hits.json) and the CapnBry sightings index; coordinates: CapnBry gives zone id + zone-local x,y,z,
converted with Zones.OffsetX/OffsetY*8192 (checked: Holsvi Hallgrim and Borra convert to exactly their spots in our world)."""
import os, math
HERE = os.path.dirname(os.path.abspath(__file__))
exec(open(os.path.join(HERE, 'merchant_match.py'), encoding='utf-8').read())
src = open(os.path.join(HERE, 'merchant_pass2.py'), encoding='utf-8').read()
exec(src[src.index('# ---------- built period items'):src.index('def seal_of_prices')])
exec(src[src.index('def seal_of_prices'):src.index('SEALCLASS =')])
exec(src[src.index('def resolve_template'):src.index('new_lists = []')])

AR = os.path.join(os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "..")), "development-source", "research", "period-data", "archive")  # found from this script
cb = json.load(open(os.path.join(AR, 'capnbry', 'sightings_index.json'), encoding='utf-8'))
cb_by = collections.defaultdict(list)
for k, v in cb.items():
    cb_by[v['name'].lower()].append((k, v))
zrow = {r[0]: r for r in db.execute('select ZoneID,RegionID,Name,OffsetX,OffsetY from Zones')}


def cb_spot(name):
    out = []
    for k, v in cb_by.get(name.lower(), []):
        for z, x, y, zz, lv in v['seen']:
            zr = zrow.get(z)
            if zr:
                out.append({'capnbry_mob': k, 'typetag': v['typetag'], 'zone_id': z, 'zone': zr[2], 'zone_local': [x, y, zz], 'Region': zr[1],
                            'X': zr[3] * 8192 + x, 'Y': zr[4] * 8192 + y, 'Z': zz, 'level': lv})
    return out


def nearest(region, x, y, guild_like=None, n=3):
    ms = [m for m in mobs if m['Region'] == region and abs(m['X'] - x) < 1500 and abs(m['Y'] - y) < 1500]
    if guild_like:
        g = [m for m in ms if guild_like.lower() in (m['Guild'] or '').lower()]
        ms = g or ms
    ms.sort(key=lambda m: math.hypot(m['X'] - x, m['Y'] - y))
    return ms[:n]


def full(m):
    r = db.execute('select Z,Heading,Model,Size,Level from Mob where Mob_ID=?', (m['Mob_ID'],)).fetchone()
    return {'name': m['Name'], 'guild': m['Guild'], 'mob_id': m['Mob_ID'], 'X': m['X'], 'Y': m['Y'], 'Z': r[0], 'Heading': r[1], 'Model': r[2], 'Size': r[3], 'Level': r[4], 'list': m['list']}


# per-merchant facts from the sources (see evidence strings)
F = {
 'Eileen Morton': dict(realm='Albion', role='Instrument Merchant (period Allakhazam: "Merchant (Instruments)", "Bard Instruments")', town='Camelot Hills (Cotswold, next to Ydenia Philpott)', guild='Instrument Merchant', kind='Instrument',
    ev=['period Allakhazam mob 2138 (capture 2003-10-23): Albion, Merchant (Instruments), daatlas map #1', 'CapnBry mob 1586: "Instrument Merchant", zone 0 Camelot Hills loc 10631,27415 z2496, level 21',
        'our world: Ydenia Philpott <Instrument Merchant> stands 114 units away; Ydenia is a separate period NPC (period Allakhazam mob 2137, CapnBry), so Eileen is a new spawn']),
 'Madarl': dict(realm='Hibernia', role='Merchant (period Allakhazam live: "Cloth Dye, Cloth Armor")', town='Tir na Nog (by the woven-armour merchants and mage trainers)', guild='Merchant', kind='Armor',
    ev=['CapnBry mob 2297: zone 209 Tir na Nog loc 14867,21387 z7198, level 35', 'Allakhazam live mob 6718: Tir na Nog, "Mob No Longer Exists" (removed after the period)', 'period item pages: cloth dyes, robes, cloaks']),
 'Nealcail': dict(realm='Hibernia', role='Merchant (cloth dyes, robes, cloaks)', town='Tir na Nog', guild='Merchant', kind='Armor',
    ev=['period Allakhazam mob 1520 (capture 2003-11-01): Hibernia Merchant, level 35, daatlas map #12', 'CapnBry mob 2306: zone 209 Tir na Nog loc 16034,19842 z7198, level 35', 'Allakhazam live: Tir na Nog, "Mob No Longer Exists"']),
 'Vinandr': dict(realm='Midgard', role='Armor Merchant (hats/circlets; period Allakhazam "Hats")', town='Mularn (Vale of Mularn)', guild='Armor Merchant', kind='Armor',
    ev=['period Allakhazam mob 6370 (capture 2003-07-12): Midgard Merchant, "Hats"', 'CapnBry mob 5420: "Armor Merchant", zone 100 Vale of Mularn loc 52148,53329 z4719, level 33', 'Allakhazam live mob 12562: Vale of Mularn (Mularn), level 31']),
 'Borjal': dict(realm='Midgard', role='Shield Merchant', town='Bjarken (Gripklosa Mountains)', guild='Shield Merchant', kind='Shield',
    ev=['period Allakhazam mob 6510 (capture 2003-09-04): Midgard Merchant (Shields)', 'CapnBry mob 3389: "Shield Merchant", zone 152 Gripklosa loc 9564,57484 z4160, level 50', 'Allakhazam live: Gripklosa Mountains (Bjarken)']),
 'Royd': dict(realm='Midgard', role='Merchant (Bounty Items: totems)', town='Jordheim', guild='Merchant', kind='Merchant',
    ev=['period Allakhazam mob 2779 (capture 2003-09-23): Midgard Merchant (Bounty Items), daatlas map #2', 'CapnBry mob 1810: "Merchant", zone 120 Jordheim loc 23440,25549 z8029, level 50', 'Allakhazam live: Jordheim, "Mob No Longer Exists"',
        'period item pages give no prices for the totems; Allakhazam calls them bounty items -> GameBountyMerchant (13 exist in our world) if the totems are bounty-point priced']),
 'Melanaha': dict(realm='Hibernia', role='Merchant ("Hats"); also a quest giver (Hats off to Molanoho!, live)', town='Mag Mell (Lough Derg)', guild='Merchant', kind='Armor',
    ev=['period Allakhazam mob 6371 (capture 2003-09-16): Hibernia Merchant, "Hats"', 'period item pages (35): Found in Lough Derg', 'Allakhazam live mob 6371: Lough Derg (Mag Mell), level 35', 'no coordinates in any source (not in CapnBry, Uthgard or Warcry)']),
 'Rhodyn': dict(realm='Hibernia', role='instrument seller (period item pages only)', town='Lough Derg or Tir na Nog (unknown)', guild='Instrument Merchant', kind='Instrument',
    ev=['only the 15 period item pages (captures 2003-10-26) name Rhodyn, always together with Edsoner (Lough Derg) and Fingal (Tir na Nog), Found in Lough Derg + Tir na Nog',
        'not in CapnBry, Allakhazam mob pages (period or live), Uthgard or Warcry; our world has Rhosyn <Instrument Merchant> in Tir na Nog (name ratio 0.83) but Rhosyn is a separate period seller on other pages']),
 'Tempeste': dict(realm='Hibernia', role='bow seller (period item pages only)', town='Lough Derg or Tir na Nog (unknown)', guild='Weapon Merchant', kind='Weapon',
    ev=['only the 20 period item pages (captures 2003-10-26) name Tempeste, together with Kenzia and Briana (Tir na Nog) and Mannix (Lough Derg)', 'not in CapnBry, Allakhazam mob pages, Uthgard or Warcry']),
 'Caryan': dict(realm='Hibernia', role='/level free-equipment NPC (weapons), added with the /level command in 1.62; not a merchant', town='Tir na Nog, near the mage trainers (period); later Cullin\'s Inn', guild='', kind=None,
    ev=['Warcry news 2003-07-19 (period, world builder quote): level 20 /level players "seek out Somyr and Caryan near the mage trainers" in Tir na Nog', 'Warcry news 2004-09 (after period): Somyr and Caryan are in Cullins Inn',
        'period item pages (6): "Free item given to /level characters by Caryan in TnN"', 'Allakhazam live mob 6560: "Gives free weapons to /leveled characters", Tir na Nog (Cullin\'s Inn), level 50']),
 'Somyr': dict(realm='Hibernia', role='/level free-equipment NPC (armour), added with /level in 1.62; not a merchant', town='Tir na Nog, near the mage trainers (period)', guild='', kind=None,
    ev=['period Allakhazam mob 6561 (capture 2003-09-16): "Gives free armor to /leveled characters. Loc 15,22"', 'Warcry news 2003-07-19: "Somyr and Caryan near the mage trainers"',
        'Loc 15,22 read as /loc thousands in zone 209 -> about 15000,22000 zone-local: right by Ena (Mentalist Trainer) and the woven merchants; 1000-unit precision only']),
}
SPOT_OVERRIDE = {'Somyr': {'Region': 201, 'X': 8192 + 15000, 'Y': 8192 + 22000, 'source': 'period Allakhazam "Loc 15,22" (zone-local thousands, +-500)'}}

out = []
for name, f in F.items():
    realm = f['realm']
    rn = RNUM[realm]
    nn = norm(name)
    rec = next((r for r in matching if norm(r['period_name']) == nn and r['realm'] == realm), None)
    items = sorted({p['name'] for p, _, _ in groups.get((nn, realm), [])})
    spots = cb_spot(name)
    placement = {}
    if spots:
        s0 = spots[0]
        nb = nearest(s0['Region'], s0['X'], s0['Y'], f['kind'])
        ref = full(nb[0]) if nb else None
        placement = {'kind': 'new spawn (position from CapnBry, period-era radar data)', 'Region': s0['Region'], 'X': s0['X'], 'Y': s0['Y'], 'Z': s0['Z'],
                     'Heading': ref['Heading'] if ref else 0, 'Model': ref['Model'] if ref else None, 'Size': ref['Size'] if ref else 50, 'Level': s0['level'],
                     'Realm': rn, 'Name': name, 'Guild': f['guild'], 'ClassType': 'DOL.GS.GameMerchant', 'ItemsListTemplateID': 'p165list_' + name.replace(' ', '_'),
                     'derived': 'Heading/Model/Size copied from the nearest %s (%s, Mob_ID %s); model, race and gender are not in any source' % (f['kind'] or 'NPC', ref['name'] if ref else '-', ref['mob_id'] if ref else '-'),
                     'source_spot': s0, 'nearest_now': [{'name': m['Name'], 'guild': m['Guild'], 'dist': int(math.hypot(m['X'] - s0['X'], m['Y'] - s0['Y']))} for m in nearest(s0['Region'], s0['X'], s0['Y'])]}
        if name == 'Royd':
            placement['ClassType'] = 'DOL.GS.GameBountyMerchant (if the totems are bounty-point priced; prices not on the pages) else DOL.GS.GameMerchant'
    elif name in SPOT_OVERRIDE:
        so = SPOT_OVERRIDE[name]
        nb = nearest(so['Region'], so['X'], so['Y'], 'Trainer')
        ref = full(nb[0]) if nb else None
        placement = {'kind': 'new spawn (approximate: period loc in thousands)', 'Region': so['Region'], 'X': so['X'], 'Y': so['Y'], 'Z': ref['Z'] if ref else None,
                     'Heading': ref['Heading'] if ref else 0, 'Model': None, 'Size': 50, 'Level': 50, 'Realm': rn, 'Name': name, 'Guild': f['guild'],
                     'ClassType': 'OWNER DECISION: a /level free-gear giver, not a merchant (no such class found in this pass)', 'ItemsListTemplateID': None,
                     'derived': 'Z/Heading from nearest trainer %s; level 50 from Allakhazam live' % (ref['name'] if ref else '-'), 'source_spot': so}
    elif name == 'Caryan':
        so = SPOT_OVERRIDE['Somyr']
        placement = {'kind': 'derived: next to Somyr (period: "Somyr and Caryan near the mage trainers")', 'Region': so['Region'], 'X': so['X'] + 60, 'Y': so['Y'], 'Z': None,
                     'Realm': rn, 'Name': name, 'Guild': '', 'Level': 50, 'ClassType': 'OWNER DECISION: /level free-gear giver, not a merchant', 'ItemsListTemplateID': None,
                     'derived': 'spot = Somyr\'s approximate spot + 60 units; no own coordinates in any source'}
    elif name == 'Melanaha':
        nb = [m for m in mobs if m['Region'] == 200 and m['Name'] in ('Eluned', 'Anice')]
        ref = full(nb[0]) if nb else None
        placement = {'kind': 'derived: town known (Mag Mell), spot unknown', 'Region': 200, 'X': ref['X'] + 80 if ref else None, 'Y': ref['Y'] if ref else None, 'Z': ref['Z'] if ref else None,
                     'Heading': ref['Heading'] if ref else 0, 'Model': ref['Model'] if ref else None, 'Size': 50, 'Level': 35, 'Realm': rn, 'Name': name, 'Guild': 'Merchant',
                     'ClassType': 'DOL.GS.GameMerchant', 'ItemsListTemplateID': 'p165list_Melanaha',
                     'derived': 'placed 80 units from %s (Mag Mell cloth-armour seller, Mob_ID %s); level 35 from Allakhazam live' % (ref['name'], ref['mob_id']) if ref else ''}
    else:   # Rhodyn, Tempeste
        cos = {'Rhodyn': ['Edsoner', 'Fingal'], 'Tempeste': ['Kenzia', 'Mannix', 'Briana']}[name]
        placement = {'kind': 'UNPLACEABLE from sources: town is Lough Derg or Tir na Nog', 'options': [full(m) | {'zone': m['zone']} for m in mobs if m['Name'] in cos],
                     'suggestion': 'derived only: stand next to one of the co-sellers above (owner picks the town); Guild %s, GameMerchant, list p165list_%s' % (f['guild'], name)}
    # stock
    rows, seen_t = [], set()
    for p, m, _ in sorted(groups.get((nn, realm), []), key=lambda x: x[0]['key']):
        tid, conf, note = resolve_template(p, rn, None)
        if tid.lower() in seen_t:
            continue
        seen_t.add(tid.lower())
        rows.append({'template_id': tid, 'page_key': p['key'], 'item_name': p['name'], 'confidence': conf, 'note': note, 'prices': p['prices']})
    for i, r in enumerate(rows):
        r['page'], r['slot'] = (i // 30, i % 30) if i < 150 else (None, None)
    out.append({'name': name, 'realm': realm, 'period_role': f['role'], 'town': f['town'], 'coordinates': spots or (SPOT_OVERRIDE.get(name) and [SPOT_OVERRIDE[name]]) or [],
                'model_race_gender': 'not in any source (CapnBry/Allakhazam/Uthgard/Warcry give none)', 'sold': items, 'evidence': f['ev'], 'placement': placement,
                'stock_list': {'new_list_id': 'p165list_' + name.replace(' ', '_'), 'rows_total': len(rows), 'rows_by_confidence': dict(collections.Counter(r['confidence'] for r in rows)), 'rows': rows}})

# period Allakhazam mob pages ("Goods for Sale", captures 2003) list the full stock: add goods the item pages miss
GOODS = json.load(open(os.path.join(HERE, 'period_mob_goods.json'), encoding='utf-8'))
for o in out:
    g = GOODS.get(o['name'])
    if not g or g['kind'] != 'Goods for Sale:':
        continue
    rn = RNUM[o['realm']]
    rows = o['stock_list']['rows']
    have = {item_norm(r['item_name']) for r in rows}
    for gname in g['goods']:
        gn = item_norm(gname)
        if gn in have:
            continue
        have.add(gn)
        ts = [t for t in by_name.get(gn, []) if t['Realm'] in (rn, 0)]
        bt = [b for b in built_by.get((gn, rn), [])]
        if ts:
            ts.sort(key=lambda t: (t['Realm'] == rn, in_lists[t['Id_nb'].lower()]), reverse=True)
            rows.append({'template_id': ts[0]['Id_nb'], 'page_key': None, 'item_name': gname, 'confidence': 'medium', 'note': 'from the period mob page Goods for Sale (%s); no item page; template by name' % g['capture'], 'prices': []})
        elif bt:
            rows.append({'template_id': bt[0]['Id_nb'], 'page_key': None, 'item_name': gname, 'confidence': 'medium', 'note': 'from the period mob page Goods for Sale; built period item by name', 'prices': []})
        else:
            rows.append({'template_id': None, 'page_key': None, 'item_name': gname, 'confidence': 'low', 'note': 'from the period mob page Goods for Sale; NO ItemTemplate and no item page: item must be created first', 'prices': []})
    for i, r in enumerate(rows):
        r['page'], r['slot'] = (i // 30, i % 30) if i < 150 else (None, None)
    o['stock_list']['rows_total'] = len(rows)
    o['stock_list']['rows_by_confidence'] = dict(collections.Counter(r['confidence'] for r in rows))
    o['stock_list']['sources'] = ['period item pages', 'period Allakhazam mob page %s (%s)' % (g['url'], g['capture'])]
    o['evidence'].append('period Allakhazam mob page Goods for Sale (%s): %d items' % (g['capture'], len(g['goods'])))

# extras: realm-conflict and unconfirmed-spelling names
extras = [
 {'name': 'Dyemaster Marna', 'realm': 'Albion', 'finding': 'SAME NPC AS OUR "Dyemaster Mara": period Marna at CapnBry zone 26 loc 27487,22873 -> Camelot 35679,31065 z8208; our Dyemaster Mara <Dye Merchant> (list EnamelDye3) stands at 35671,31141 z8208 (76 units). Period Allakhazam mob 1383: Albion Merchant (Enamel Dye).',
  'proposal': 'rename Dyemaster Mara -> Dyemaster Marna (spelling) and give her the period enamel stock (her period items vs EnamelDye3 in the stock check below); the earlier first-name match to "Marna" (craft components) was wrong',
  'mob': [full(m) for m in mobs if m['Name'] == 'Dyemaster Mara']},
 {'name': 'Ylaine Barret', 'realm': 'Albion', 'finding': 'period Allakhazam mob 4573 (2003-11-01) calls her a Quest NPC with known loot Greater Enervating Poison, Isle of Glass (Gothwaite Harbor); CapnBry 4405 "Ylaine Barrett" Merchant at Isle of Glass 522790,542142: same as our Ylaine Barrett (spelling).',
  'proposal': 'match confirmed (spelling): our Ylaine Barrett, list AlbAlchemy; the one item page (Greater Enervating Poison) is weak (Allakhazam lists it as loot), low confidence'},
 {'name': 'Dala', 'realm': 'Albion pages', 'finding': 'Dala is the Midgard mithril weapon merchant in Jordheim (Warcry patch 1.60 notes "A high level (mithril) one-hand weapon shopkeeper has been added to Jordheim by name of Dala"; CapnBry 1794 at our Dala\'s exact spot). The 7 Albion item pages naming her are Allakhazam listing errors.', 'proposal': 'nothing to do'},
 {'name': 'Darbo', 'realm': 'Albion page', 'finding': 'Darbo is the Hibernian Sapphire-seal armour merchant in DF (period Allakhazam mob 2870); the 1 Albion page is a listing error.', 'proposal': 'nothing to do'},
 {'name': 'Debo', 'realm': 'Hibernia page', 'finding': 'Debo is the Albion Emerald-seal merchant (period mob 3634; CapnBry sees him in DF and also in Camelot City); the 1 Hibernian page (Malign Scarab Cloak) is a listing error.', 'proposal': 'nothing to do'},
 {'name': 'Eluned', 'realm': 'Albion page', 'finding': 'Eluned is the Mag Mell (Lough Derg) cloth-armour merchant (period mob 1630; CapnBry 2025 at our Eluned\'s spot, 20 units); the 1 Albion page is a listing error. Her Hibernian stock list is in new_lists.json (pass 2).', 'proposal': 'nothing more'},
]
# Marna stock check vs EnamelDye3
mg = groups.get((norm('Dyemaster Marna'), 'Albion'), [])
mi = {item_norm(p['name']) for p, _, _ in mg}
extras[0]['stock_check'] = {'period_items': len(mi), 'already_on_EnamelDye3': len(mi & list_names.get('enameldye3', set())), 'missing': sorted(mi - list_names.get('enameldye3', set()))}
json.dump({'missing': out, 'extras': extras}, open(os.path.join(HERE, 'missing_merchants.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
for o in out:
    pl = o['placement']
    print(o['name'], '|', pl.get('kind'), pl.get('Region'), pl.get('X'), pl.get('Y'), pl.get('Z'), '| stock', o['stock_list']['rows_total'], o['stock_list']['rows_by_confidence'])
print(extras[0]['stock_check'])
