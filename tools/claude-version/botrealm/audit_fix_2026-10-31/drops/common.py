"""drops agent: shared loaders (read-only on the audit snapshot)."""
import collections, json, os, re, sqlite3, sys

sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
AUDIT = os.path.normpath(os.path.join(HERE, '..', '..'))
SNAP = os.path.join(AUDIT, 'world165_audit.db')
CV = os.path.normpath(os.path.join(HERE, '..', '..', '..', '..', '..'))   # the install root
RES = os.path.join(CV, 'development-source', 'research')
VW = os.path.join(CV, 'tools', 'claude-version', 'true165', 'vendor_work')
WORK = os.path.join(HERE, 'work')
os.makedirs(WORK, exist_ok=True)

DISABLED = {27, 165, 244} | set(range(234, 243))      # 244 Passage of Conflict: 1.70 dungeon, unreachable
REALM_NAME = {1: 'Albion', 2: 'Midgard', 3: 'Hibernia'}
REALM_OF_NAME = {'Albion': 1, 'Midgard': 2, 'Hibernia': 3}
# region -> realm (1/2/3) or a neutral tag (players of every realm kill these monsters)
REGION_REALM = {}
for r in (1, 2, 10, 20, 21, 22, 23, 24, 50, 51, 60, 61, 62, 245, 277):
    REGION_REALM[r] = 1
for r in (100, 101, 102, 125, 126, 127, 128, 129, 150, 151, 160, 161, 246):
    REGION_REALM[r] = 2
for r in (200, 201, 202, 220, 221, 222, 223, 224, 180, 181, 190, 191, 247, 276):
    REGION_REALM[r] = 3
for r in (249,):
    REGION_REALM[r] = 'DF'
for r in (248,):
    REGION_REALM[r] = 'SH'
for r in (250, 251, 252, 253):
    REGION_REALM[r] = 'BG'
NEUTRAL = {'DF', 'SH', 'BG'}

ALIAS = {'campacorentinesforest': 'campacorentinforest', 'svealandeast': 'eastsvealand', 'svealandwest': 'westsvealand',
         'camelot': 'cityofcamelot', 'iarnwoodforest': 'iarnwood', 'krondonogrecitadel': 'krondon',
         'plainsofgwyddneau': 'gwyddneau', 'tuscarenglacier': 'tuscaranglacier', 'eirikstaad': 'erikstaad',
         'caledonia': 'caledon', 'koalinthtribalcaverns': 'koalinthcaverns', 'hillsofclaret': 'thehillsofclaret',
         'briefine': 'breifine', 'marfachcavern': 'marfachcaverns', 'saerenthal': 'saeranthal', 'myrkwood': 'myrkwoodforest',
         'stonehengebarrow': 'stonehengebarrows', 'lyonnesse': 'lyonesse', 'cornwal': 'cornwall', 'allta': 'alltafearann',
         'krondoncrypt': 'krondon', 'thridanki': 'thidranki', 'coruscatingmines': 'coruscatingmine',
         'odinsgate': 'odinsgate', 'hadrianswall': 'hadrianswall', 'tepoksmine': 'tepoksmine', 'summonershall': 'summonershall',
         'silvermts': 'silvermts', 'silverminemts': 'silverminemts'}


def zkey(n):
    k = re.sub(r"[^a-z]", "", (n or '').lower().replace("mountains", "mts").replace("mtns", "mts"))
    return ALIAS.get(k, k)


def norm(s):
    """item / mob name key as build_drops165 uses it"""
    s = (s or "").lower().replace("`", "'").replace("’", "'")
    s = re.sub(r"\((alb|mid|hib|u)\)", " ", s)
    return re.sub(r"[^a-z0-9]+", " ", s).strip()


def mkey(s):
    """mob name key: no leading article, no punctuation"""
    s = norm(s)
    s = re.sub(r'^(the|a|an) ', '', s)
    return s


def mvariants(s):
    k = mkey(s)
    out = {k}
    if k.endswith('ies'):
        out.add(k[:-3] + 'y')
    if k.endswith('es'):
        out.add(k[:-2])
    if k.endswith('s'):
        out.add(k[:-1])
    if k.endswith('men'):
        out.add(k[:-3] + 'man')
    return out


def ro(path=SNAP):
    return sqlite3.connect(f'file:{path}?mode=ro', uri=True)


def jload(path):
    return json.load(open(path, encoding='utf-8'))


def jdump(obj, path, indent=1):
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(obj, f, indent=indent, ensure_ascii=False)


class World:
    """the parts of the snapshot the drops work needs"""

    def __init__(self, path=SNAP):
        c = ro(path)
        c.row_factory = sqlite3.Row
        q = lambda s, *a: c.execute(s, a).fetchall()
        self.regions = {r['RegionID']: dict(r) for r in q('select * from Regions')}
        self.zones = [dict(r) for r in q('select * from Zones')]
        self.zones_by_region = collections.defaultdict(list)
        for z in self.zones:
            self.zones_by_region[z['RegionID']].append(z)
        self.items = {}
        for r in q('select * from ItemTemplate'):
            self.items[r['Id_nb'].lower()] = dict(r)
        self.items_by_name = collections.defaultdict(list)
        for r in self.items.values():
            self.items_by_name[norm(r['Name'])].append(r)
        self.period_added = {r[0] for r in q("select RowKey from offline_true165_added where SourceTable='LootTemplate' "
                                             "and Reason like 'period drops:%'")}
        self.loot = [dict(r) for r in q('select * from LootTemplate')]
        self.mobx = [dict(r) for r in q('select * from MobXLootTemplate')]
        self.otd = [dict(r) for r in q('select * from LootOTD')]
        # service NPCs among the Realm 0 rows (scripted bosses such as DOL.GS.CryptLord / UaimhLairmaster are monsters)
        service = re.compile(r"^DOL\.GS\.(Keeps\.Guard\w*|GameMerchant|GameStableMaster|Blacksmith|GameHealer|Enchanter|"
                             r"NameRegistrar|GuildRegistrar|BeranSupplyMaster|EmblemNPC|GameVaultKeeper|LurfosHerald|"
                             r"GameBountyMerchant|\w*Teleporter\w*|\w*Trainer\w*|\w*Banker\w*)$", re.I)
        self.spawns = collections.defaultdict(list)      # mob key -> spawn dicts
        self.mob_names = {}                               # lower name -> display name
        for r in q('select Mob_ID, Name, Region, X, Y, Level, ClassType, Flags, NPCTemplateID, RespawnInterval, '
                   'PackageID from Mob where Realm=0'):
            reg = r['Region']
            exp = self.regions.get(reg, {}).get('Expansion', 9)
            if exp not in (0, 1) or reg in DISABLED or reg == 163:
                continue
            plain = not r['ClassType'] or r['ClassType'] == 'DOL.GS.GameNPC'
            if plain and (r['Flags'] or 0) & (16 | 8 | 128) or (r['ClassType'] and service.search(r['ClassType'])):
                continue
            z = self.zone_of(reg, r['X'], r['Y'])
            d = dict(r)
            d['zone'] = z['Name'] if z else None
            d['zone_id'] = z['ZoneID'] if z else None
            d['realm'] = REGION_REALM.get(reg)
            d['exp'] = exp
            self.spawns[r['Name'].lower()].append(d)
            self.mob_names.setdefault(r['Name'].lower(), r['Name'])
        # spawns the 1.65 conversion removed (offline_classic165_removed_mobs): their drops are listed too, so a
        # restored spawn has its loot (flagged 'removed spawn' in the report)
        self.removed_spawns = collections.defaultdict(list)
        for r in q('select Mob_ID, Name, Region, X, Y, Level, ClassType, Flags from offline_classic165_removed_mobs '
                   'where Realm=0'):
            reg = r['Region']
            exp = self.regions.get(reg, {}).get('Expansion', 9)
            if exp not in (0, 1) or reg in DISABLED or reg == 163 or r['Name'].lower() in self.spawns:
                continue
            if r['ClassType'] and service.search(r['ClassType']):
                continue
            z = self.zone_of(reg, r['X'], r['Y'])
            d = dict(r)
            d['zone'] = z['Name'] if z else None
            d['realm'] = REGION_REALM.get(reg)
            d['exp'] = exp
            self.removed_spawns[r['Name'].lower()].append(d)
            self.mob_names.setdefault(r['Name'].lower(), r['Name'])
        c.close()

    def zone_of(self, reg, x, y):
        for z in self.zones_by_region.get(reg, []):
            x0, y0 = z['OffsetX'] * 8192, z['OffsetY'] * 8192
            if x0 <= (x or 0) < x0 + z['Width'] * 8192 and y0 <= (y or 0) < y0 + z['Height'] * 8192:
                return z
        return None

    def mob_realms(self, name_l):
        return {s['realm'] for s in self.spawns.get(name_l, []) if s['realm'] is not None}

    def mob_zones(self, name_l):
        return {zkey(s['zone']) for s in self.spawns.get(name_l, []) if s['zone']}

    def mob_level(self, name_l):
        lv = [s['Level'] or 1 for s in self.spawns.get(name_l, [])]
        return sum(lv) / len(lv) if lv else 0

    def allowed_item_realms(self, name_l):
        rs = self.mob_realms(name_l)
        if rs & NEUTRAL:
            return {0, 1, 2, 3}
        return {0} | {r for r in rs if isinstance(r, int)}
