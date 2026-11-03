"""Builds drops165.json, the BotRealm monster drops (owner 2026-10-30: "build what you can now, and when that
data comes in you can tweak"). Development tool; the launcher and derive_true165_database.py only read its output.

Source, first pass: Uthgard's measured drops (research/1.65-mode/loot/uthgard_mobs.json: per monster, kills and how
often each item dropped). Later passes add period Allakhazam (the Wayback crawl) and then tweak.

Rules:
- Only hostile monsters of the classic and Shrouded Isles regions, matched by name.
- Uthgard's own random items ("rog" section) and coin bags are left out: ROGs are off in 1.65 Mode and coin comes
  from the money generator.
- An item is used only when an ItemTemplate of that name exists (not the classic quest items, cq_*: their quests drop
  them). Missing items are listed in drops165-missing.json
  and made later only from period stat pages, never invented.
- Several templates with one name: the one nearest the monster's level (Realm 0 / either realm on a tie).
- Chance = how often it dropped per kill, in whole percent (min 1). Few kills say little, so the kills are counted as
  at least 20 (one drop in 3 kills is 5 %, not 33 %).
- Each monster keeps its current loot rows (seals, quest drops, named drops); the Uthgard items join the same
  template, and the per-kill item cap (MobXLootTemplate.DropCount) is raised to fit them.
"""
import collections, json, math, os, re, sqlite3, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
DB = os.path.join(ROOT, "runtime", "data", "opendaoc.sqlite3.db")
LOOT = os.path.join(ROOT, "development-source", "research", "1.65-mode", "loot")
MIN_KILLS = 20
MAX_DROP_COUNT = 4


def norm(s):
    s = (s or "").lower().replace("`", "'").replace("’", "'")
    s = re.sub(r"\((alb|mid|hib|u)\)", " ", s)
    return re.sub(r"[^a-z0-9]+", " ", s).strip()


def main():
    con = sqlite3.connect(f"file:{DB}?mode=ro", uri=True)
    q = lambda s, *a: con.execute(s, a).fetchall()
    exp = {r[0]: r[1] for r in q("SELECT RegionID, Expansion FROM Regions")}
    service = re.compile(r"Merchant|Trainer|Guard|Teleport|Banker|Healer|Stable|Vault|Emblem|Guild|Herald|Crafter|"
                         r"Recharger|Enchanter|Smith|Master|Broker|Registrar|Exchange|Bounty|Lord\b", re.I)
    levels = collections.defaultdict(list)
    names = {}
    for name, reg, lvl, ct, fl in q("SELECT Name, Region, Level, ClassType, Flags FROM Mob WHERE Realm=0"):
        if exp.get(reg, 9) not in (0, 1) or (fl or 0) & (16 | 8 | 128) or (ct and service.search(ct)):
            continue
        levels[name.lower()].append(lvl or 1)
        names.setdefault(name.lower(), name)
    items = collections.defaultdict(list)
    for idnb, nm, lv, realm in q("SELECT Id_nb, Name, Level, Realm FROM ItemTemplate"):
        if idnb.lower().startswith("cq_"):
            continue    # classic quest items drop through their quests, not as loot
        items[norm(nm)].append((idnb, lv or 0, realm or 0))
    existing = collections.defaultdict(dict)
    for tn, iid, ch, cnt in q("SELECT TemplateName, ItemTemplateID, Chance, Count FROM LootTemplate"):
        existing[tn.lower()][iid.lower()] = ch
    dropcount = {mn.lower(): dc for mn, lt, dc in q("SELECT MobName, LootTemplateName, DropCount FROM MobXLootTemplate")
                 if mn.lower() == lt.lower()}

    U = json.load(open(os.path.join(LOOT, "uthgard_mobs.json"), encoding="utf-8"))
    out, missing = {}, collections.Counter()
    stats = collections.Counter()
    for key, records in U.items():
        if key not in levels:
            continue
        seen, rates, kills = set(), {}, 0
        for r in records:
            if r["id"] in seen:
                continue
            seen.add(r["id"])
            kills += r.get("killed") or 0
            for d in r["drops"]:
                if d["sec"] == "rog" or norm(d["item"]) == "bag of coins":
                    continue
                k = norm(d["item"])
                rates[k] = (rates.get(k, (0, ""))[0] + d["n"], d["item"])
        if not rates:
            continue
        lv = sum(levels[key]) / len(levels[key])
        add = []
        for k, (n, label) in rates.items():
            cands = items.get(k)
            if not cands:
                missing[label] += 1
                continue
            idnb = min(cands, key=lambda c: (abs(c[1] - lv), c[2] != 0, c[0]))[0]
            if idnb.lower() in existing.get(key, {}):
                stats["already in our loot"] += 1
                continue
            chance = max(1, min(100, round(100 * n / max(kills, MIN_KILLS))))
            add.append([idnb, chance])
        if not add:
            continue
        add.sort(key=lambda a: (-a[1], a[0]))
        rolls = sum(a[1] for a in add) + sum(max(0, c) for c in existing.get(key, {}).values() if c < 100)
        cap = max(dropcount.get(key, 1), min(MAX_DROP_COUNT, math.ceil(rolls / 100) + 1))
        out[names[key]] = {"drop_count": cap, "items": add}
        stats["monsters"] += 1
        stats["item rows"] += len(add)
    distinct = {a[0] for v in out.values() for a in v["items"]}
    data = {
        "source": "Uthgard measured drops (first pass, 2026-10-30); period Allakhazam to follow",
        "min_kills": MIN_KILLS,
        "monsters": dict(sorted(out.items(), key=lambda kv: kv[0].lower())),
    }
    json.dump(data, open(os.path.join(HERE, "drops165.json"), "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    json.dump(dict(missing.most_common()), open(os.path.join(HERE, "drops165-missing.json"), "w", encoding="utf-8"),
              indent=1, ensure_ascii=False)
    print(f"monsters {stats['monsters']}, item rows {stats['item rows']}, distinct items {len(distinct)}, "
          f"already in our loot {stats['already in our loot']}, missing item names {len(missing)}")


if __name__ == "__main__":
    sys.exit(main())
