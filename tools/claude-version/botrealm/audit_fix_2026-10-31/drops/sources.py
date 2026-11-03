"""drops agent: the drop sources, indexed for matching against the server's monsters.

period item pages  : AUDIT/work/items/item_sources.json ['period'] (the items agent's parse of vendor_work/period_item_pages.jsonl)
period mob pages   : AUDIT/period_mobs_parsed.json (Known Loot, habitats)
Uthgard            : research/1.65-mode/loot/uthgard_mobs.json (measured drops per monster, with its zone)
                     + Uthgard item pages (item_sources.json ['uthgard'], 'Item can drop from these monsters')
live Allakhazam    : work/live_items.json, work/live_mobs.json (prep_live.py), trusted only with a pre-Nov-2003 comment
"""
import collections, difflib, re
from common import *

CUTOFF = 20031101


def strip_suffix(name):
    """Allakhazam's disambiguation tags are not part of the in-game name: "(Alb)", "(Albion)", "(nld)", "(2H)",
    "(Wind)", "AF 70" ... (every trailing parenthesis group)"""
    s = (name or '').strip()
    while True:
        t = re.sub(r'\s*\([^()]{1,24}\)\s*$', '', s)
        t = re.sub(r'\s+AF \d+\s*$', '', t, flags=re.I)
        if t == s:
            break
        s = t
    s = re.sub(r'\s*\((?:hib|mid|alb|df|af \d+|interior|exterior|nld)\)', ' ', s, flags=re.I)
    return re.sub(r'\s+', ' ', s).strip() or (name or '').strip()


TAG_REALM = re.compile(r'\((alb|albion|mid|midgard|hib|hibernia)\)', re.I)


def tag_realm(name):
    m = TAG_REALM.search(name or '')
    return {'alb': 1, 'albion': 1, 'mid': 2, 'midgard': 2, 'hib': 3, 'hibernia': 3}[m.group(1).lower()] if m else None


class Sources:
    def __init__(self, W):
        self.W = W
        # zone key -> realm tag (via the DB's classic / SI zones)
        self.zone_realm = {}
        self.zone_exp = {}
        for z in W.zones:
            reg = z['RegionID']
            if reg == 163:
                continue
            exp = W.regions.get(reg, {}).get('Expansion', 9)
            if exp not in (0, 1):
                continue
            rr = REGION_REALM.get(reg)
            if rr is None and reg in range(234, 243):
                rr = 'BG'
            if rr is None:
                continue
            k = zkey(z['Name'])
            self.zone_realm.setdefault(k, rr)
            self.zone_exp.setdefault(k, exp)
        self.zone_realm.update({'hallofthecorrupt': 1, 'marfachcaverns': 3, 'dodensgruva': 2, 'thehillsofclaret': 'BG',
                                'thidranki': 'BG', 'caledon': 'BG', 'summonershall': 'SH', 'darknessfalls': 'DF'})
        self._period_items()
        self._period_mobs()
        self._uthgard()
        self._live()
        self._scripted()

    # ---------------------------------------------------------------------------------------------- scripted mobs
    FOLDER = {'caersidi': (1, 'caersidi', 1), 'galladoria': (3, 'galladoria', 1), 'tuscarianglacier': (2, 'tuscaranglacier', 1),
              'darknessfalls': ('DF', 'darknessfalls', 0), 'summonershall': ('SH', 'summonershall', 0),
              'krondon': (1, 'krondon', 1), 'avaloncity': (1, 'avaloncity', 1), 'fomor': (3, 'fomor', 1),
              'tursuil': (3, 'tursuil', 1), 'trollheim': (2, 'trollheim', 1), 'iarnvidiurlair': (2, 'iarnvidiurslair', 1),
              'dodens': (2, 'dodensgruva', 0), 'hallofthecorrupt': (1, 'hallofthecorrupt', 0),
              'marfachcaverns': (3, 'marfachcaverns', 0)}

    def _scripted(self):
        """names the boss scripts spawn (Name = "..."), with the realm / zone of their script folder"""
        root = os.path.join(CV, 'development-source', 'server', 'GameServer', 'scripts', 'namedmobs')
        self.scripted = {}
        self.scripted_display = {}
        for dp, _, fs in os.walk(root):
            rel = os.path.relpath(dp, root).replace(os.sep, '/').split('/')
            top = re.sub(r'[^a-z]', '', rel[0].lower())
            info = self.FOLDER.get(top)
            if info is None and rel[0].endswith('Named Mobs'):
                info = ({'albion': 1, 'midgard': 2, 'hibernia': 3}[rel[0].split()[0].lower()], None,
                        1 if len(rel) > 1 and 'shrouded' in rel[1].lower() else 0)
            for f in fs:
                if not f.endswith('.cs'):
                    continue
                txt = open(os.path.join(dp, f), encoding='utf-8', errors='replace').read()
                for n in re.findall(r'(?<![.\w])Name\s*=\s*"([^"]+)"', txt):
                    if 'Initializator' in n or len(n) < 3:
                        continue
                    self.scripted.setdefault(n.lower(), info or (None, None, 0))
                    self.scripted_display.setdefault(n.lower(), n)

    def info(self, name_l):
        """(realm tags, zone keys, {(realm, exp)}, how): Mob rows first; scripted / unspawned names from their script
        folder or their period page habitats"""
        sp = self.W.spawns.get(name_l)
        how = 'spawn'
        if not sp and name_l not in self.scripted:
            sp = self.W.removed_spawns.get(name_l)
            how = 'removed spawn'
        if sp:
            return ({s['realm'] for s in sp if s['realm'] is not None}, {zkey(s['zone']) for s in sp if s['zone']},
                    {(s['realm'], s['exp']) for s in sp if s['realm'] is not None}, how)
        realms, zones, keys = set(), set(), set()
        sc = self.scripted.get(name_l)
        if sc and sc[0] is not None:
            realms.add(sc[0])
            if sc[1]:
                zones.add(sc[1])
            keys.add((sc[0], sc[2]))
        for v in mvariants(name_l):
            for p in self.pmob_by_key.get(v, []):
                for h in p.get('habitats') or []:
                    k = zkey(h)
                    if k in self.zone_realm and (not realms or self.zone_realm[k] in realms):
                        zones.add(k)
                        if not sc:
                            realms.add(self.zone_realm[k])
                            keys.add((self.zone_realm[k], self.zone_exp.get(k, 0)))
        return realms, zones, keys, ('script' if sc else 'period habitat' if realms else 'none')

    def allowed(self, name_l):
        realms = self.info(name_l)[0]
        if not realms or realms & NEUTRAL:
            return {0, 1, 2, 3}
        return {0} | {r for r in realms if isinstance(r, int)}

    # ---------------------------------------------------------------------------------------------- period items
    def _period_items(self):
        S = jload(os.path.join(AUDIT, 'work', 'items', 'item_sources.json'))
        self.pitems = S['period']
        self.uitems = S['uthgard']
        self.zitems = S['zamlive']
        self.pitem_by_name = collections.defaultdict(list)
        for p in self.pitems:
            self.pitem_by_name[norm(strip_suffix(p['name']))].append(p)
        self.uitem_names = {norm(u['name']) for u in self.uitems if u.get('name')}
        self.uitem_by_name = collections.defaultdict(list)
        for u in self.uitems:
            if u.get('name'):
                self.uitem_by_name[norm(u['name'])].append(u)
        # period item page -> droppers it names (mob keys)
        self.pitem_droppers = {}
        for p in self.pitems:
            ds = set()
            for x in p['dropped_by']:
                n = re.sub(r'^Dropped by\s+(an?\s+)?', '', x, flags=re.I).strip()
                if 2 < len(n) < 50 and not re.search(r'[\[\]@:]|\d{4}|Posted|Expand|Filter|Rating', n):
                    ds |= mvariants(n)
            self.pitem_droppers[p['key']] = ds
        self.pitem_by_dropper = collections.defaultdict(list)
        for p in self.pitems:
            for d in self.pitem_droppers[p['key']]:
                self.pitem_by_dropper[d].append(p)

    def period_item_pages(self, name, realms=None):
        """period pages of an item name, realm-compatible (realms = allowed item realm ids incl. 0)"""
        out = []
        for p in self.pitem_by_name.get(norm(strip_suffix(name)), []):
            pr = REALM_OF_NAME.get(p['realm'], 0)
            if realms is None or pr == 0 or pr in realms:
                out.append(p)
        return out

    # ---------------------------------------------------------------------------------------------- period mobs
    def _period_mobs(self):
        P = jload(os.path.join(AUDIT, 'period_mobs_parsed.json'))
        self.pmobs = [p for p in P if p.get('name')]
        self.pmob_by_key = collections.defaultdict(list)
        for p in self.pmobs:
            self.pmob_by_key[mkey(p['name'])].append(p)

    def period_pages_for_mob(self, name_l):
        """period mob pages that are this server monster: name (+plural/singular), realm, habitat zone"""
        realms, zones, _, _ = self.info(name_l)
        out = []
        seen = set()
        for v in mvariants(name_l):
            for p in self.pmob_by_key.get(v, []):
                if p['key'] in seen:
                    continue
                pr = p.get('realm')
                hz = {zkey(h) for h in (p.get('habitats') or [])} & set(self.zone_realm)
                if realms & NEUTRAL:
                    ok_realm = True
                else:
                    ok_realm = pr == 'All Realms' or REALM_OF_NAME.get(pr) in realms or not pr
                if not ok_realm:
                    continue
                if hz and not (hz & zones):
                    # habitat listed but none of the spawn zones: same realm only when the page's zones are all in a
                    # realm the monster lives in and the server monster has no zone (outside zone rectangles)
                    continue
                seen.add(p['key'])
                out.append((p, 'zone' if hz else 'realm only'))
        return out

    # ---------------------------------------------------------------------------------------------- Uthgard
    def _uthgard(self):
        U = jload(os.path.join(RES, '1.65-mode', 'loot', 'uthgard_mobs.json'))
        self.uth = collections.defaultdict(list)       # mob key -> [record] (deduped by id)
        self.uth_item_rates = collections.defaultdict(list)    # item norm -> [(mob, zone, n, kills)]
        self.unknown_uth_zones = collections.Counter()
        for key, recs in U.items():
            seen = set()
            for r in recs:
                if r['id'] in seen:
                    continue
                seen.add(r['id'])
                zk = zkey(r['zone'])
                r = dict(r)
                r['realm'] = self.zone_realm.get(zk)
                if r['realm'] is None:
                    self.unknown_uth_zones[r['zone']] += 1
                self.uth[mkey(key)].append(r)
                for d in r['drops']:
                    if d['sec'] == 'rog' or norm(d['item']) == 'bag of coins':
                        continue
                    self.uth_item_rates[norm(d['item'])].append((r['name'], r['zone'], d['n'], r.get('killed') or 0))

    def uthgard_for_mob(self, name_l):
        """Uthgard records of this monster whose zone is in a realm (and classic part) the server monster lives in"""
        keys = self.info(name_l)[2]
        ok, bad = [], []
        for v in mvariants(name_l):
            for r in self.uth.get(v, []):
                rr = r['realm']
                if (rr, 0) in keys or (rr in NEUTRAL and any(k[0] == rr for k in keys)):
                    ok.append(r)
                else:
                    bad.append(r)
        # dedupe by id across variants
        d = {}
        for r in ok:
            d.setdefault(r['id'], r)
        b = {}
        for r in bad:
            b.setdefault(r['id'], r)
        return list(d.values()), list(b.values())

    def uthgard_item_rate(self, item_name):
        """the item's measured rate on Uthgard on the monster with the most kills (any monster): (pct, label)"""
        rs = [x for x in self.uth_item_rates.get(norm(item_name), []) if x[3] >= 20]
        if not rs:
            return None
        mob, zone, n, kills = max(rs, key=lambda x: x[3])
        return max(1, min(100, round(100 * n / kills))), f'Uthgard rate of {item_name!r} on {mob} ({zone}): {n} in {kills} kills'

    # ---------------------------------------------------------------------------------------------- live zam
    def _live(self):
        self.litems = jload(os.path.join(WORK, 'live_items.json'))
        self.lmobs = jload(os.path.join(WORK, 'live_mobs.json'))
        self.litem_by_name = collections.defaultdict(list)
        for e in self.litems.values():
            self.litem_by_name[norm(strip_suffix(e['name']))].append(e)
        self.lmob_by_key = collections.defaultdict(list)
        for e in self.lmobs.values():
            self.lmob_by_key[mkey(e['name'])].append(e)

    def live_item_early(self, name, realms=None):
        """live item pages of this name (realm-compatible) with a comment dated before 1 Nov 2003"""
        out = []
        for e in self.litem_by_name.get(norm(strip_suffix(name)), []):
            er = REALM_OF_NAME.get(e.get('realm'), 0)
            if realms is not None and er and er not in realms:
                continue
            early = sorted(d for d, _ in e['comments'] if d < CUTOFF)
            if early:
                out.append((e, early))
        return out

    def live_mob_early(self, name_l):
        realms, zones, _, _ = self.info(name_l)
        out = []
        for v in mvariants(name_l):
            for e in self.lmob_by_key.get(v, []):
                early = sorted(d for d, _ in e['comments'] if d < CUTOFF)
                if not early:
                    continue
                er = REALM_OF_NAME.get(e.get('realm'))
                if not (realms & NEUTRAL) and er and er not in realms and e.get('realm') != 'All':
                    continue
                hz = {zkey(h) for h in e.get('habitats') or []} & set(self.zone_realm)
                if hz and not (hz & zones):
                    continue
                out.append((e, early))
        return out

    def variant_known(self, name, realms=None, cutoff=0.93):
        """spelling variants (difflib ratio >= cutoff) of the name on a period page / Uthgard / early live page"""
        n = norm(strip_suffix(name))
        if not hasattr(self, '_pn'):
            self._pn, self._un, self._ln = list(self.pitem_by_name), list(self.uitem_by_name), list(self.litem_by_name)
        ev = []
        for v in difflib.get_close_matches(n, self._pn, 3, cutoff):
            for p in self.period_item_pages(v, realms):
                ev.append(f"period-zam item {p['key']} '{p['name']}' (capture {p['capture'][:8]}; spelling variant)")
        for v in difflib.get_close_matches(n, self._un, 2, cutoff):
            ev.append(f"Uthgard item page {self.uitem_by_name[v][0]['url'].split('=')[-1]} '{self.uitem_by_name[v][0]['name']}' (spelling variant)")
        for v in difflib.get_close_matches(n, self._ln, 2, cutoff):
            for e, early in self.live_item_early(v, realms):
                ev.append(f"live-zam item {e['citem']} '{e['name']}' comment dated {early[0]} (spelling variant)")
        return ev
