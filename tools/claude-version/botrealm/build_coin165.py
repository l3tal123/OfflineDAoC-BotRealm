"""Builds coin165.json, the BotRealm coin rule (owner 2026-10-31: fix "coin drops on every kill").
In 1.65 a monster either carried coin or not: Uthgard's measured kills are bimodal (238 of 415 animal types never drop
coin, most humanoids always do). So: a monster Uthgard measured (>= 30 kills) uses its own measured coin-bag rate;
any other monster uses its body type's average from the same data. Amounts are unchanged (LootGeneratorMoney).
Read by the server (mode165/Coin165.cs) from runtime/botrealm/coin165.json. Development tool, read-only on its inputs."""
import collections, json, os, sqlite3, statistics, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
UTH = os.path.join(ROOT, "development-source", "research", "1.65-mode", "loot", "uthgard_mobs.json")
DB = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "runtime", "data", "opendaoc-165.sqlite3.db")
MIN_KILLS = 30
NAMES = {0: "None", 1: "Animal", 2: "Demon", 3: "Dragon", 4: "Elemental", 5: "Giant", 6: "Humanoid", 7: "Insect",
         8: "Magical", 9: "Reptile", 10: "Plant", 11: "Undead"}

db = sqlite3.connect(f"file:{DB}?mode=ro", uri=True)
body = {}
for n, b, tb in db.execute("SELECT lower(m.Name), m.BodyType, t.BodyType FROM Mob m LEFT JOIN NpcTemplate t "
                           "ON t.TemplateId = m.NPCTemplateID WHERE m.Realm = 0"):
    body.setdefault(n, collections.Counter())[tb if tb else (b or 0)] += 1
U = json.load(open(UTH, encoding="utf-8"))
kills, coin = collections.Counter(), collections.Counter()
for entries in U.values():
    for e in (entries if isinstance(entries, list) else [entries]):
        k = e.get("killed") or 0
        if k <= 0:
            continue
        n = e["name"].lower()
        kills[n] += k
        coin[n] += k * next((d["rate"] for d in e["drops"] if d["item"].lower() == "bag of coins"), 0.0) / 100.0
monsters, per_type = {}, collections.defaultdict(list)
for n, k in kills.items():
    if k < MIN_KILLS or n not in body:
        continue
    rate = round(100.0 * coin[n] / k)
    monsters[n] = rate
    per_type[body[n].most_common(1)[0][0]].append(rate)
defaults = {NAMES[t]: round(statistics.mean(v)) for t, v in sorted(per_type.items()) if t in NAMES}
out = {"source": "Uthgard measured coin-bag rates per monster (>= %d kills); others use their body type's average" % MIN_KILLS,
       "body_type_default": defaults, "monsters": dict(sorted(monsters.items()))}
for folder in (HERE,):   # installed into runtime/botrealm with the rest of the BotRealm content
    with open(os.path.join(folder, "coin165.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
print("monsters", len(monsters), "defaults", defaults)
