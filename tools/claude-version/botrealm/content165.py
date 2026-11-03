"""BotRealm content removal, used by derive_true165_database.py (owner 2026-10-30; plan sections 3 and 19).

Every removed row is moved to offline_true165_removed (source table, row key, reason, the row as JSON), never just
deleted, so it can be restored. Only rows that did not exist in 1.65 go; the real 1.65 services stay:
  kept: GameVaultKeeper (town vaults), Recharger (1.50 patch), GameBountyMerchant (bounty point stores, 1.50),
        OFTeleporter / OFAssistant / OFMerchant / OFMerchantHome (the border keep medallion portal ceremony),
        the SI town channelers, and the period epic encounter controllers bots raid (Apocalypse, Olcasgean,
        Hjalmar & Suttung).
Code-made NPCs (bounty masters, reputation emissaries, Atlas quest givers) are switched off in the server code, not here.
"""
import datetime, json, uuid

import os
_RULES = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "true165-rules.json"), encoding="utf-8"))["remove"]
MOB_CLASSES = _RULES["mob_classes"]           # every list lives in true165-rules.json (also read by the launcher)
MOB_PACKAGES = _RULES["mob_packages"]
MOB_NAMES = _RULES["mob_names"]
MOB_GUILDS = _RULES["mob_guilds"]
LOOT_GENERATORS = _RULES["loot_generators"]
KEEP_PACKAGE = _RULES["mob_names_keep_package"]


def _archive(con, table, key_col, where, args, reason, counts):
    cols = [r[1] for r in con.execute(f'PRAGMA table_info("{table}")')]
    rows = con.execute(f'SELECT * FROM "{table}" WHERE {where}', args).fetchall()
    now = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    for row in rows:
        rec = dict(zip(cols, row))
        con.execute("INSERT INTO offline_true165_removed (SourceTable, RowKey, Reason, RowJson, RemovedAt) VALUES (?,?,?,?,?)",
                    (table, str(rec.get(key_col)), reason, json.dumps(rec, default=str), now))
    con.execute(f'DELETE FROM "{table}" WHERE {where}', args)
    counts[reason] = counts.get(reason, 0) + len(rows)


def apply(con):
    con.execute("CREATE TABLE IF NOT EXISTS offline_true165_removed (Id INTEGER PRIMARY KEY AUTOINCREMENT, "
                "SourceTable TEXT NOT NULL, RowKey TEXT, Reason TEXT NOT NULL, RowJson TEXT NOT NULL, RemovedAt TEXT NOT NULL)")
    counts = {}
    for cls, why in MOB_CLASSES.items():
        _archive(con, "Mob", "Mob_ID", "ClassType = ?", (cls,), why, counts)
    for pkg, why in MOB_PACKAGES.items():
        _archive(con, "Mob", "Mob_ID", "PackageID = ?", (pkg,), why, counts)
    for name, why in MOB_NAMES.items():
        _archive(con, "Mob", "Mob_ID", "Name = ? AND COALESCE(PackageID,'') <> ?", (name, KEEP_PACKAGE), why, counts)
    for guild, why in MOB_GUILDS.items():
        _archive(con, "Mob", "Mob_ID", "Guild = ?", (guild,), why, counts)
    for cls, why in LOOT_GENERATORS.items():
        _archive(con, "LootGenerator", "LootGenerator_ID", "LootGeneratorClass = ?", (cls,), why, counts)
    for rule in _RULES.get("where", []):            # generic: {table, key, where, reason}
        _archive(con, rule["table"], rule["key"], rule["where"], (), rule["reason"], counts)
    for rule in json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "true165-rules.json"), encoding="utf-8")).get("updates", []):
        counts[rule["reason"]] = con.execute(rule["sql"]).rowcount   # generic SQL updates (period values)
    return counts


def apply_drops(con, path):
    """The period monster drops (drops165.json, made by build_drops165.py). Each item joins the monster's own loot
    template; its per-kill item cap is raised to fit. Added rows are listed in offline_true165_added."""
    if not os.path.exists(path):
        return {}
    data = json.load(open(path, encoding="utf-8"))
    con.execute("CREATE TABLE IF NOT EXISTS offline_true165_added (Id INTEGER PRIMARY KEY AUTOINCREMENT, "
                "SourceTable TEXT NOT NULL, RowKey TEXT NOT NULL, Reason TEXT NOT NULL, AddedAt TEXT NOT NULL)")
    now = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    reason = "period drops: " + data.get("source", "drops165.json")
    added = capped = 0
    for mob, entry in data["monsters"].items():
        have = {r[0].lower() for r in con.execute(
            "SELECT ItemTemplateID FROM LootTemplate WHERE lower(TemplateName)=lower(?)", (mob,))}
        for idnb, chance in entry["items"]:
            if idnb.lower() in have:
                continue
            key = str(uuid.uuid4())
            con.execute("INSERT INTO LootTemplate (TemplateName, ItemTemplateID, Chance, Count, LastTimeRowUpdated, "
                        "LootTemplate_ID) VALUES (?,?,?,1,?,?)", (mob, idnb, int(chance), now, key))
            con.execute("INSERT INTO offline_true165_added (SourceTable, RowKey, Reason, AddedAt) VALUES (?,?,?,?)",
                        ("LootTemplate", key, reason, now))
            added += 1
        row = con.execute("SELECT MobXLootTemplate_ID, DropCount FROM MobXLootTemplate WHERE lower(MobName)=lower(?) "
                          "AND lower(LootTemplateName)=lower(?)", (mob, mob)).fetchone()
        if row is None:
            key = str(uuid.uuid4())
            con.execute("INSERT INTO MobXLootTemplate (MobName, LootTemplateName, DropCount, LastTimeRowUpdated, "
                        "MobXLootTemplate_ID) VALUES (?,?,?,?,?)", (mob, mob, int(entry["drop_count"]), now, key))
            con.execute("INSERT INTO offline_true165_added (SourceTable, RowKey, Reason, AddedAt) VALUES (?,?,?,?)",
                        ("MobXLootTemplate", key, reason, now))
            capped += 1
        elif (row[1] or 0) < entry["drop_count"]:
            con.execute("UPDATE MobXLootTemplate SET DropCount=? WHERE MobXLootTemplate_ID=?", (int(entry["drop_count"]), row[0]))
            capped += 1
    return {"period drop rows added": added, "monster drop caps set": capped}


def apply_additions(con, folder):
    """Generic BotRealm rows to add: every add-*.json in the folder ({table, key, reason, rows}). Rows already there (same
    key) are left alone, so a rebuild never doubles them. Added rows are listed in offline_true165_added."""
    import glob
    counts = {}
    con.execute("CREATE TABLE IF NOT EXISTS offline_true165_added (Id INTEGER PRIMARY KEY AUTOINCREMENT, "
                "SourceTable TEXT NOT NULL, RowKey TEXT NOT NULL, Reason TEXT NOT NULL, AddedAt TEXT NOT NULL)")
    now = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    for path in sorted(glob.glob(os.path.join(folder, "add-*.json"))):
        data = json.load(open(path, encoding="utf-8"))
        table, key = data["table"], data["key"]
        cols = {r[1] for r in con.execute(f'PRAGMA table_info("{table}")')}
        added = 0
        for row in data["rows"]:
            if con.execute(f'SELECT 1 FROM "{table}" WHERE "{key}"=?', (row[key],)).fetchone():
                continue
            use = [c for c in row if c in cols]
            con.execute(f'INSERT INTO "{table}" ({",".join(chr(34) + c + chr(34) for c in use)}) VALUES ({",".join("?" * len(use))})',
                        [row[c] for c in use])
            con.execute("INSERT INTO offline_true165_added (SourceTable, RowKey, Reason, AddedAt) VALUES (?,?,?,?)",
                        (table, str(row[key]), data["reason"], now))
            added += 1
        counts[data["reason"]] = added
    return counts
