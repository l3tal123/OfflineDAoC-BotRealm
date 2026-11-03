"""Builds add-inns165.json: the people of the three traveler's inns in BotRealm (owner 2026-10-30, plan sections
17b and 19). Development tool; the 1.65 database build (derive_true165_database.py and the launcher) only reads its
output, as generic rows to add.

Positions come from the inn layout files (DAOC HIATUS\\1.65 MODE CLIENT (local only)\\layouts\\
inn_*.json, npc_spots). Every person is a copy of a real NPC of that realm (model, equipment, level, class, brain), so
the inns use the game's own looks; only the name, title, place and heading change.
- Names match the figure's sex, read from the client's model table (monsters.csv: "Briton Female", "Dwarf Male shop 2").
- The Realm Exchange brokers (Adalyn, Brynhild, Eilwen) and their guards move from the capitals to the exchange beside
  each inn; the capital rows are already archived by content165 ("Realm Exchange moves to the traveler's inns").
- Innkeeper and Horn Handler are plain NPCs until the hiring and horn systems are built; their class is set then.
- Wandering traders (owner 2026-10-30, plan item "inns: traders, patrols"): a copy of the realm's arrow merchant and
  poison merchant (their real stock), renamed by sex, each roaming a small circle of the yard that stays clear of the
  layout's no-go circles (inn, exchange, horn platform).
- Patrols: two of the inn's guards walk the layout's yard paths back and forth (PathType 2): horn platform -> yard ->
  exchange door, and inn door -> yard -> toward town. Written as add-paths165.json + add-pathpoints165.json.
The hire preview NPC is made per player by the server.
"""
import csv, glob, json, math, os, re, sqlite3, sys, uuid

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
HIATUS = os.path.dirname(ROOT)
DB = os.path.join(ROOT, "runtime", "data", "opendaoc.sqlite3.db")
LAYOUTS = os.path.join(HIATUS, "1.65 MODE CLIENT (local only)", "layouts")
MONSTERS = os.path.join(HIATUS, "Claude Takeover GitHub", "OfflineDAoC", "source", "tools", "observer-model-inspection", "monsters.csv")
PACKAGE = "true165_inn"
NAMESPACE = uuid.UUID("6b1d5c2e-1650-4a6e-9a1e-165165165165")

# realm: (source region, sources by role). A source is (region, name) of an existing NPC of that realm.
SOURCES = {
    "alb": {"innkeeper": (1, "Aiellana"), "healer": (1, "Brother Eurius"), "horn_handler": (1, "Sir Edmun"),
            "guard": (1, "Guardian"), "broker": (10, "Adalyn"), "exchange_guard": (10, "Royal Exchange Guard")},
    "hib": {"innkeeper": (200, "Kaylee"), "healer": (200, "Keagan"), "horn_handler": (200, "Tadhg"),
            "guard": (200, "Sentinel"), "broker": (201, "Eilwen"), "exchange_guard": (201, "Sentinel Exchange Guard")},
    "mid": {"innkeeper": (100, "Valgard"), "healer": (100, "Saydyn"), "horn_handler": (100, "Thora"),
            "guard": (100, "Viking Huscarl"), "broker": (101, "Brynhild"), "exchange_guard": (101, "Valkyrie Exchange Guard")},
}
# New names by realm and sex (owner 2026-10-30: names match the sex of the NPC). Guards and brokers keep theirs.
NAMES = {
    # each inn NPC keeps its name; Osric, Niamh and Siobhan were already worn by period NPCs and were replaced
    ("alb", 2): ["Gwenllian", "Rosamund", "Elowen", "Mabyn", "Ysolde", "Tegwen"],
    ("alb", 1): ["Edric", "Wystan", "Aldwin", "Godfrey", "Leofric"],
    ("hib", 2): ["Muireann", "Saoirse", "Eithne", "Ailbhe", "Clodagh"],
    ("hib", 1): ["Fiachra", "Cormac", "Ronan", "Donal", "Tadgan", "Lorcan"],
    ("mid", 2): ["Hallveig", "Sigrun", "Ragnhild", "Torunn", "Gudny", "Asvor"],
    ("mid", 1): ["Halvar", "Bjarni", "Ketil", "Ulfr", "Orvar", "Sveinung"],
}
TITLES = {"innkeeper": "Innkeeper", "healer": "Healer", "horn_handler": "Horn Handler"}
TRADER_GUILDS = {"arrow_trader": "Arrow Merchant", "poison_trader": "Poison Merchant"}
# the inns' adventurers (owner 2026-10-30, as in the old Rebuilt project): three per realm, level-15 guards of the
# realm wearing a borrowed look (the Rebuilt project's adventurer_looks), walking inn -> yard -> up the road and back
ADVENTURER_LOOKS = {"alb": ["Armsman", "Bregusuid Dale", "Simone Innes"],
                    "mid": ["Galena", "Linna", "Dalea Gronholm"],
                    "hib": ["Sentinel Grania", "Kylirean", "Selia"]}
REALM_NAME = {"alb": "albion", "mid": "midgard", "hib": "hibernia"}
# where an adventurer route ends instead of the layout path's town end (owner 2026-10-30: the Albion ones stop at the
# near edge of the Marsh Town bridge; the layout path waded the marsh past it): keep the path from this index (counted
# from its town end) and finish at this point
ROUTE_END = {"alb": (16, (471060, 630310, 1696))}
# the inns' travelers (owner 2026-10-30): three ambience townsfolk per realm wandering the inn's rooms, downstairs and
# upstairs (Traveler165 picks its spots on the navmesh); looks borrowed from realm townsfolk
TRAVELER_LOOKS = {"alb": ["Timmo", "Calya", "Keenar Woedin"],
                  "mid": ["Magnild", "Gautr", "Liot"],
                  "hib": ["Iacob", "Ailidh", "Naois"]}
ADV_LEVEL = 15
PATROL_SPEED = 70    # the server's walk (NpcMovementComponent.DEFAULT_WALK_SPEED; owner 2026-10-30: the inn patrols ran)
NOW = "2026-10-30 00:00:00"


NAVMESH = os.path.join(ROOT, "runtime", "server", "navmesh-165")
sys.path.insert(0, os.path.join(ROOT, "development-source", "server", "tools", "navmesh_splice"))


def floor_spots(zone, floor_z, around, avoid, count, reach=420, gap=90):
    """Spots on the inn's own floor from its 1.65 navmesh: polygons at floor height (within 6), within reach of
    `around`, each at least 120 from anything higher (the counter, tables, stairs) and from every `avoid` point, and
    gap apart (owner 2026-10-30: a traveler spawned on the bar counter). Nearest to `around` first."""
    import struct
    from navdiff import tiles
    _, ts, _ = tiles(os.path.join(NAVMESH, f"zone{int(zone):03d}.nav"))
    ax, ay = around
    floor, raised, above = [], [], []
    for _, d in ts.values():
        h = struct.unpack_from("<5iI9i3f3f3ff", d, 0)
        bmin, bmax = h[18:21], h[21:24]
        if bmax[0] * 32 < ax - reach - 200 or bmin[0] * 32 > ax + reach + 200 or bmax[2] * 32 < ay - reach - 200 or bmin[2] * 32 > ay + reach + 200:
            continue
        pc, vc = h[6], h[7]
        verts = struct.unpack_from(f"<{vc * 3}f", d, 100)
        for p in range(pc):
            pd = struct.unpack_from("<I6H6HHBB", d, 100 + vc * 12 + p * 32)
            n = pd[14]
            if (pd[15] >> 6) != 0 or n == 0:
                continue
            vs = [(verts[i * 3] * 32, verts[i * 3 + 2] * 32, verts[i * 3 + 1] * 32) for i in pd[1:1 + n]]
            c = tuple(sum(v[j] for v in vs) / n for j in range(3))
            if math.dist(c[:2], around) > reach + 200:
                continue
            if abs(c[2] - floor_z) <= 6 and all(abs(v[2] - floor_z) <= 8 for v in vs):
                floor.append(c)
            elif floor_z + 15 < c[2] < floor_z + 150:
                raised.append(c)
            elif floor_z + 150 <= c[2] < floor_z + 900:
                above.append(vs)     # the upstairs floor or the roof: what makes a spot indoors
    def covered(x, y):
        for vs in above:
            inside = False
            for i in range(len(vs)):
                x1, y1, x2, y2 = vs[i][0], vs[i][1], vs[(i + 1) % len(vs)][0], vs[(i + 1) % len(vs)][1]
                if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1:
                    inside = not inside
            if inside:
                return True
        return False

    picked = []
    for c in sorted(floor, key=lambda c: math.dist(c[:2], around)):
        if not covered(c[0], c[1]):
            continue
        if math.dist(c[:2], around) > reach:
            continue
        if any(math.dist(c[:2], r[:2]) < 100 for r in raised) or any(math.dist(c[:2], a) < 130 for a in avoid):
            continue
        if any(math.dist(c[:2], q[:2]) < gap for q in picked):
            continue
        picked.append(c)
        if len(picked) == count:
            break
    if len(picked) < count:
        sys.exit(f"only {len(picked)} clear floor spots for travelers in zone {zone}")
    return [(int(x), int(y), int(round(z))) for x, y, z in picked]


def model_sex():
    sex = {}
    for row in csv.reader(open(MONSTERS, encoding="latin1")):
        if row and row[0].strip().isdigit():
            name = row[1].lower()
            sex[int(row[0])] = 2 if re.search(r"female|\bfem\b|valkyrie|woman|lady", name) else 1
    return sex


def main():
    con = sqlite3.connect(f"file:{DB}?mode=ro", uri=True)
    con.row_factory = sqlite3.Row
    # names already worn by an NPC anywhere in the world: an inn NPC never takes one (owner 2026-10-30: no NPC in two
    # places at once; Osric, Siobhan and Niamh were already a weapon merchant, a smith and a Hy Brasil NPC)
    taken = {r[0].lower() for r in con.execute("SELECT DISTINCT Name FROM Mob WHERE Name IS NOT NULL")}
    sexes = model_sex()
    rows, report, path_rows, point_rows = [], [], [], []
    for path in [os.path.join(LAYOUTS, f"inn_{r}.json") for r in ("alb", "hib", "mid")]:   # not inn_roads_*.json
        layout = json.load(open(path, encoding="utf-8"))
        realm, region, spots = layout["realm"], layout["region"], layout["npc_spots"]
        used = {1: 0, 2: 0}

        def source(role):
            if role in TRADER_GUILDS:
                # the realm's own merchant of that kind closest to the inn (its stock comes along)
                hx, hy = spots["innkeeper"]["world"]["x"], spots["innkeeper"]["world"]["y"]
                r = con.execute("SELECT * FROM Mob WHERE Region=? AND Guild=? AND Realm<>0 AND COALESCE(ItemsListTemplateID,'')<>'' "
                                "ORDER BY (X-?)*(X-?)+(Y-?)*(Y-?) LIMIT 1", (region, TRADER_GUILDS[role], hx, hx, hy, hy)).fetchone()
                if r is None:
                    sys.exit(f"source merchant missing: {realm} {role}")
                return dict(r)
            reg, name = SOURCES[realm][role]
            r = con.execute("SELECT * FROM Mob WHERE Region=? AND Name=? AND Realm<>0 LIMIT 1", (reg, name)).fetchone()
            if r is None:
                sys.exit(f"source NPC missing: {realm} {role} {name} (region {reg})")
            return dict(r)

        def place(role, spot, index, rename, roam=0, path=""):
            row = source(role)
            sex = sexes.get(row["Model"], 1)
            row.update(X=spot["world"]["x"], Y=spot["world"]["y"], Z=spot["z"], Heading=spot.get("heading", 0),
                       Region=region, Gender=sex, PackageID=PACKAGE, PathID=path, RoamingRange=roam, HouseNumber=0,
                       Mob_ID=str(uuid.uuid5(NAMESPACE, f"{realm}/{role}/{index}")))
            # no NPC template: a template with ReplaceMobValues overwrote the name (Gwenllian spawned as "Aiellana"),
            # realm and aggro of the copy at spawn (owner test 2026-10-30); the row's own model and equipment stay.
            # A row whose look lives only in its template (the Svasud Viking Huscarls: no armour, no weapon, all one
            # female figure without it; owner 2026-10-30) takes the template's equipment and, for a guard, one of its
            # male figures.
            tmpl_id = row.get("NPCTemplateID") or 0
            if tmpl_id > 0 and not (row.get("EquipmentTemplateID") or "").strip():
                t = con.execute("SELECT EquipmentTemplateID, Model FROM NpcTemplate WHERE TemplateId=?", (tmpl_id,)).fetchone()
                if t and (t["EquipmentTemplateID"] or "").strip():
                    row["EquipmentTemplateID"] = t["EquipmentTemplateID"].split(";")[0].strip()
                    if role in ("guard", "exchange_guard"):
                        males = [int(m) for m in str(t["Model"] or "").split(";") if m.strip().isdigit() and sexes.get(int(m), 1) == 1]
                        if males:
                            row["Model"] = males[index % len(males)]
                            sex = 1
                            row["Gender"] = 1
            row["NPCTemplateID"] = -1
            if role not in ("guard", "exchange_guard"):
                row["AggroLevel"] = 0
                row["AggroRange"] = 0
            # some source NPCs stand still (speed 0: the Hibernia merchants, every exchange guard); these must move
            # (traders wander, guards reach what they fight)
            if role in ("guard", "exchange_guard", "arrow_trader", "poison_trader") and (row["Speed"] or 0) < 100:
                row["Speed"] = 191
            if role in ("guard", "exchange_guard") and (row["Speed"] or 0) > 250:
                row["Speed"] = 250      # the Svasud huscarls run at 1250 (event guards); a town guard runs at a guard's pace
            if role in ("guard", "exchange_guard") and (row["Level"] or 0) > 60:
                row["Level"] = 50       # the Svasud huscarls are level 255 event guards; an inn guard is a town guard
            if role == "innkeeper":
                row["ClassType"] = "DOL.GS.Innkeeper165"   # opens the companion hiring window
            if role == "horn_handler":
                row["ClassType"] = "DOL.GS.HornHandler165"  # the Horn of Rallying: PvE events start here
            if rename:
                pool = NAMES[(realm, sex)]
                while pool[used[sex] % len(pool)].lower() in taken and used[sex] < 4 * len(pool):
                    used[sex] += 1
                if pool[used[sex] % len(pool)].lower() in taken:
                    sys.exit(f"no free inn name left for {realm} sex {sex}")
                row["Name"] = pool[used[sex] % len(pool)]
                taken.add(row["Name"].lower())
                used[sex] += 1
                row["Guild"] = TRADER_GUILDS.get(role) or TITLES[role]
            rows.append(row)
            report.append(f"{realm} {role:15} {row['Name']:24} {'F' if sex == 2 else 'M'} model {row['Model']} "
                          f"({row['ClassType']}) at {row['X']},{row['Y']},{row['Z']}")

        place("innkeeper", spots["innkeeper"], 0, True)
        place("healer", spots["healer"], 0, True)
        place("horn_handler", spots["horn_handler"], 0, True)
        for i, s in enumerate(spots["inn_guards"]):
            place("guard", s, i, False)
        place("broker", spots["exchange_brokers"][0], 0, False)
        for i, s in enumerate(spots["exchange_guards_inside"] + spots["exchange_guards_outside"]):
            place("exchange_guard", s, i, False)

        # wandering traders: on open yard points, roaming no further than the nearest no-go circle allows
        paths = {}
        for p in layout["paths"]:
            head = p["name"].split(" -> ")
            # "door -> yard" or "inn door front -> yard" = door; "yard -> horn platform" = horn; exchange; toward
            key = "door" if "door" in head[0] else head[1].split(" ")[0]
            paths[key] = p["points"]
        no_go = spots["trader_roam"]["no_go"]

        def clear(pt):
            return min(math.dist((pt["world"]["x"], pt["world"]["y"]), (n["world"]["x"], n["world"]["y"])) - n["radius_units"]
                       for n in no_go)
        yard = paths["door"][-1]
        toward_exchange = paths["exchange"][1]
        # the arrow trader stands to one side of the yard point, not on it: the yard is where both patrols start and
        # turn (owner 2026-10-30: Siobhan stood on top of a Sentinel)
        front = paths["door"][0]
        fx, fy = yard["world"]["x"] - front["world"]["x"], yard["world"]["y"] - front["world"]["y"]
        fl = math.hypot(fx, fy) or 1.0
        sides = [{"world": {"x": int(yard["world"]["x"] + sgn * -fy / fl * 170), "y": int(yard["world"]["y"] + sgn * fx / fl * 170)},
                  "z": yard["z"]} for sgn in (1, -1)]
        # away from the patrol lines too (yard <-> horn platform, yard -> toward the town)
        def seg_dist(pt, a, b):
            ax, ay, bx, by = a["world"]["x"], a["world"]["y"], b["world"]["x"], b["world"]["y"]
            px, py = pt["world"]["x"], pt["world"]["y"]
            dx_, dy_ = bx - ax, by - ay
            t = 0.0 if dx_ == dy_ == 0 else max(0.0, min(1.0, ((px - ax) * dx_ + (py - ay) * dy_) / (dx_ * dx_ + dy_ * dy_)))
            return math.dist((px, py), (ax + t * dx_, ay + t * dy_))
        patrol_lines = [(yard, spots["horn_handler"]), (yard, paths["toward"][min(1, len(paths["toward"]) - 1)])]
        arrow_spot = max(sides, key=lambda sp: min([clear(sp)] + [seg_dist(sp, a, b) for a, b in patrol_lines]))
        for role, anchor in (("arrow_trader", arrow_spot), ("poison_trader", toward_exchange)):
            roam = int(max(120, min(450, clear(anchor) - 60)))
            place(role, anchor, 0, True, roam=roam)

        # patrols: two inn guards walk the yard paths back and forth
        def pts(seq):
            return [(p["world"]["x"], p["world"]["y"], p["z"]) for p in seq]
        hs, yd = spots["horn_handler"], paths["door"][-1]
        hx, hy = hs["world"]["x"], hs["world"]["y"]
        dx, dy = yd["world"]["x"] - hx, yd["world"]["y"] - hy
        dl = max(1.0, math.hypot(dx, dy))
        horn_watch = (int(hx + dx / dl * 300), int(hy + dy / dl * 300), hs["z"])   # 300 units toward the yard
        routes = {
            # the horn area by the road (owner 2026-10-30), never the Horn Handler's own spot at the platform edge
            "yard": [horn_watch] + pts(paths["door"][-1:]) + pts(paths["toward"][1:2]),
            "road": pts(paths["door"][-1:]) + pts(paths["toward"][1:]),   # from the yard, never the doorway
        }
        for i, (name, points) in enumerate(routes.items()):
            path_id = f"true165_inn_{realm}_{name}"
            path_rows.append({"Path_ID": f"{path_id}-path", "PathID": path_id, "PathType": 2, "RegionID": region,
                              "LastTimeRowUpdated": NOW})
            for step, (x, y, z) in enumerate(points, 1):
                point_rows.append({"PathPoints_ID": f"{path_id}-{step}", "PathID": path_id, "Step": step, "X": x, "Y": y, "Z": z,
                                   "MaxSpeed": PATROL_SPEED, "WaitTime": 8 if step in (1, len(points)) else 0,
                                   "LastTimeRowUpdated": NOW})
            x, y, z = points[0]
            place("guard", {"world": {"x": x, "y": y}, "z": z, "heading": 0}, 10 + i, False, path=path_id)
        # the adventurers (owner 2026-10-30): idle in the inn yard -> walk the road to the town -> idle there -> back.
        # The road is the layout's "<town> -> yard" path (dense points along the road), walked yard-first and in straight
        # lines between its points, so they keep to the road. They never go inside (the travelers live there).
        import random
        town_paths = [p for p in layout["paths"] if p["name"].endswith("-> yard") and "door" not in p["name"]]
        town_road = town_paths[0]["points"] if town_paths else list(reversed(paths["toward"]))
        town_pts = [(p["world"]["x"], p["world"]["y"], p["z"]) for p in town_road]
        if realm in ROUTE_END:
            keep_from, end = ROUTE_END[realm]
            town_pts = [end] + town_pts[keep_from:]
        road_pts = list(reversed(town_pts))       # yard first
        yard_pt = (yard["world"]["x"], yard["world"]["y"], yard["z"])
        for i, look in enumerate(ADVENTURER_LOOKS[realm]):
            src = con.execute("SELECT * FROM Mob WHERE Name=? AND Realm=? LIMIT 1", (look, {"alb": 1, "mid": 2, "hib": 3}[realm])).fetchone()
            if src is None:
                sys.exit(f"adventurer look missing: {realm} {look}")
            row = dict(src)
            mob_id = str(uuid.uuid5(NAMESPACE, f"{realm}/adventurer/{i}"))
            # each keeps its own side of the road (a narrow lane), so they don't walk inside one another
            lane = (i - 1) * 25
            walk = []
            for k, (x, y, z) in enumerate(road_pts):
                nx_, ny_ = road_pts[min(k + 1, len(road_pts) - 1)][:2]
                px_, py_ = road_pts[max(k - 1, 0)][:2]
                dx_, dy_ = nx_ - px_, ny_ - py_
                dl = math.hypot(dx_, dy_) or 1.0
                walk.append((int(x - dy_ / dl * lane), int(y + dx_ / dl * lane), int(z), 1))
            home = walk[0]
            row.update(Name=f"{REALM_NAME[realm]} adventurer", Guild="", ClassType="DOL.GS.Adventurer165", Level=ADV_LEVEL,
                       Realm={"alb": 1, "mid": 2, "hib": 3}[realm], X=home[0], Y=home[1], Z=home[2], Heading=0,
                       Region=region, PackageID=PACKAGE, PathID="", RoamingRange=0, HouseNumber=0, NPCTemplateID=-1,
                       AggroLevel=0, AggroRange=0, Speed=191, RespawnInterval=420, Mob_ID=mob_id,
                       Gender=sexes.get(row["Model"], 1))
            rows.append(row)
            path_id = f"true165_adv_{mob_id}"
            path_rows.append({"Path_ID": f"{path_id}-path", "PathID": path_id, "PathType": 2, "RegionID": region,
                              "LastTimeRowUpdated": NOW})
            for step, (x, y, z, on_road) in enumerate(walk, 1):
                point_rows.append({"PathPoints_ID": f"{path_id}-{step}", "PathID": path_id, "Step": step, "X": int(x),
                                   "Y": int(y), "Z": int(z), "MaxSpeed": on_road, "WaitTime": 0, "LastTimeRowUpdated": NOW})
            report.append(f"{realm} adventurer      {row['Name']:24} look {look} model {row['Model']}: {len(walk)} road points "
                          f"({'town road' if town_paths else 'short road stub, no <town> -> yard path yet'}), yard {yard_pt[:2]}")
        # the travelers: around the inn's lounge, peaceful, wandering the rooms
        lounge = spots["lounge"]
        avoid = [(spots["innkeeper"]["world"]["x"], spots["innkeeper"]["world"]["y"]),
                 (spots["healer"]["world"]["x"], spots["healer"]["world"]["y"])] +                 [(d["world"]["x"], d["world"]["y"]) for d in layout["doorways"] if d["name"] == "inn door"]
        traveler_spots = floor_spots(layout["zone"], lounge["z"], (lounge["world"]["x"], lounge["world"]["y"]), avoid,
                                     len(TRAVELER_LOOKS[realm]))
        for i, look in enumerate(TRAVELER_LOOKS[realm]):
            src = con.execute("SELECT * FROM Mob WHERE Name=? AND Realm=? LIMIT 1", (look, {"alb": 1, "mid": 2, "hib": 3}[realm])).fetchone()
            if src is None:
                sys.exit(f"traveler look missing: {realm} {look}")
            row = dict(src)
            x, y, floor_z = traveler_spots[i]
            row.update(Name=f"{REALM_NAME[realm]} traveler", Guild="", ClassType="DOL.GS.Traveler165",
                       Level=max(5, min(20, row["Level"] or 10)), Realm={"alb": 1, "mid": 2, "hib": 3}[realm],
                       X=x, Y=y, Z=lounge["z"], Heading=lounge.get("heading", 0), Region=region, PackageID=PACKAGE,
                       PathID="", RoamingRange=0, HouseNumber=0, NPCTemplateID=-1, AggroLevel=0, AggroRange=0,
                       Flags=(row["Flags"] or 0) | 0x10, RespawnInterval=300, ItemsListTemplateID="",
                       Mob_ID=str(uuid.uuid5(NAMESPACE, f"{realm}/traveler/{i}")), Gender=sexes.get(row["Model"], 1))
            rows.append(row)
            report.append(f"{realm} traveler        {row['Name']:24} look {look} model {row['Model']} at {x},{y},{lounge['z']}")
    # the weapons shown (Mob.VisibleWeaponSlots: low nibble right hand, high nibble left; 0 right, 1 left, 2 two-hand,
    # 3 ranged, F none) from what each NPC really carries. The row's value wins over the server's own choice, and the
    # copied ones pointed at an empty hand (owner 2026-10-30: "none of the guardians have weapons": halberd in the
    # two-hand slot, shown hand = the empty right hand)
    for row in rows:
        slots = {r[0] for r in con.execute("SELECT Slot FROM NPCEquipment WHERE TemplateID=?", (row.get("EquipmentTemplateID") or "",))}
        if 10 in slots:
            row["VisibleWeaponSlots"] = 0x10 if 11 in slots else 0xF0
        elif 12 in slots:
            row["VisibleWeaponSlots"] = 0x22
        elif 13 in slots:
            row["VisibleWeaponSlots"] = 0x33
        else:
            row["VisibleWeaponSlots"] = 0xFF
    data = {"table": "Mob", "key": "Mob_ID",
            "reason": "True 1.65 Mode traveler's inn people (inn layouts of the original-client chat)",
            "rows": rows}
    json.dump(data, open(os.path.join(HERE, "add-inns165.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    print("\n".join(report))
    for table, key, name, data_rows in (("Path", "Path_ID", "add-paths165.json", path_rows),
                                        ("PathPoints", "PathPoints_ID", "add-pathpoints165.json", point_rows)):
        json.dump({"table": table, "key": key, "reason": "True 1.65 Mode traveler's inn patrols (inn layout paths)",
                   "rows": data_rows}, open(os.path.join(HERE, name), "w", encoding="utf-8"), indent=1)
    print(f"{len(rows)} inn people, {len(path_rows)} patrol paths ({len(point_rows)} points)")


if __name__ == "__main__":
    main()
