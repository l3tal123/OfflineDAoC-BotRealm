"""Builds the BotRealm database from the normal one (owner 2026-10-30). Development tool; the launcher gets the
same steps in C# for players.

BotRealm: the Classic and Shrouded Isles game of the 1.65 era. The only changes from the original game are the
bot and companion systems and the Realm Exchange. It runs from its own database, so its characters, bots, Realm
Exchange, guilds, houses, keeps and relics are separate from normal mode's, and each mode keeps its own progress.

    python derive_true165_database.py            first build: runtime/data/opendaoc-165.sqlite3.db (refuses to
                                                 overwrite an existing 1.65 database: that would lose its progress)
    python derive_true165_database.py --rebuild  rebuild anyway (the old 1.65 database is backed up first)

Steps:
  1. A consistent copy of the normal database (SQLite backup API), built as a .tmp file.
  2. A fresh slate: every progress table emptied (players, bots, companions, inventories, Realm Exchange listings and
     sales, houses, guild progress), keeps / doors / relics back to their default owners, starter guilds only.
  3. The BotRealm settings (classic165_mode = True and the period rules, see SETTINGS).
  4. Content removal (and 4b, the period monster drops from drops165.json): rows that did not exist in 1.65 are moved to the archive table offline_true165_removed (never
     deleted: the row's JSON is kept with the reason). See content165.py.
  5. VACUUM and quick_check, then the .tmp replaces the target; config/serverconfig-165.xml is written next to the
     normal server config.
"""
import datetime, json, os, shutil, sqlite3, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
DATA = os.path.join(ROOT, "runtime", "data")
MAIN = os.path.join(DATA, "opendaoc.sqlite3.db")
TARGET = os.path.join(DATA, "opendaoc-165.sqlite3.db")
CONFIG = os.path.join(ROOT, "runtime", "server", "config")
POLICY = json.load(open(os.path.join(HERE, "progress-policy.json"), encoding="utf-8"))
DEFAULTS = json.load(open(os.path.join(HERE, "release-world-defaults.json"), encoding="utf-8"))
RULES = json.load(open(os.path.join(HERE, "true165-rules.json"), encoding="utf-8"))
STARTER_GUILDS = tuple(RULES["starter_guilds"])
# The period rules carried as server settings (plan sections 3, 4, 20); code-only rules check classic165_mode instead.
SETTINGS = RULES["settings"]


def server_running():
    out = subprocess.run(["tasklist"], capture_output=True, text=True).stdout.lower()
    return "coreserver" in out


def set_property(con, key, value):
    cols = [r[1] for r in con.execute("PRAGMA table_info(ServerProperty)")]
    if con.execute("SELECT COUNT(*) FROM ServerProperty WHERE lower(`Key`)=?", (key,)).fetchone()[0]:
        con.execute("UPDATE ServerProperty SET Value=? WHERE lower(`Key`)=?", (value, key))
        return
    row = {c: None for c in cols}
    row.update({"Key": key, "Value": value, "DefaultValue": value, "Category": "world",
                "Description": "True 1.65 Mode setting", "ServerProperty_ID": key,
                "LastTimeRowUpdated": datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")})
    use = [c for c in cols if row.get(c) is not None]
    con.execute(f"INSERT INTO ServerProperty ({','.join('`%s`' % c for c in use)}) VALUES ({','.join('?' * len(use))})",
                [row[c] for c in use])


def fresh_slate(con):
    tables = {r[0] for r in con.execute("SELECT name FROM sqlite_master WHERE type='table'")}
    for table in POLICY["ProgressTables"] + POLICY["ClearTables"] + ["FactionAggroLevel"]:
        if table in tables and table not in ("Guild", "GuildRank", "Account"):
            con.execute(f'DELETE FROM "{table}"')
    # Accounts stay so the player's login works in both modes; their characters do not.
    con.execute("UPDATE Guild SET RealmPoints=0,BountyPoints=0,Bank=0,MeritPoints=0,HaveGuildHouse=0,GuildHouseNumber=0")
    marks = ",".join("?" * len(STARTER_GUILDS))
    con.execute(f"DELETE FROM GuildRank WHERE GuildID NOT IN ({marks})", STARTER_GUILDS)
    con.execute(f"DELETE FROM Guild WHERE GuildID NOT IN ({marks})", STARTER_GUILDS)
    con.execute("UPDATE DBHouse SET OwnerID='',GuildName='',GuildHouse=0,HasConsignment=0,KeptMoney=0,Model=0,Name='' "
                "WHERE COALESCE(OwnerID,'')<>''")
    for keep_id, keep in DEFAULTS["Keep"].items():
        con.execute("UPDATE Keep SET Realm=?,Level=?,ClaimedGuildName='' WHERE KeepID=?",
                    (keep["Realm"], keep["Level"], int(keep_id)))
    for door_id, door in DEFAULTS["Door"].items():
        con.execute("UPDATE Door SET Health=?,State=? WHERE Door_ID=?", (door["Health"], door["State"], door_id))
    con.execute("UPDATE Relic SET Realm=OriginalRealm,LastRealm=OriginalRealm WHERE OriginalRealm IN (1,2,3)")
    if "offline_population_settings" in tables:
        con.execute("UPDATE offline_population_settings SET Value='0' WHERE Key='ActiveTarget'")
    if "offline_local_options" in tables:
        con.execute("INSERT OR REPLACE INTO offline_local_options(Key,Value) VALUES('MakeMeGM','false')")


def write_config():
    src = os.path.join(CONFIG, "serverconfig.xml")
    dst = os.path.join(CONFIG, "serverconfig-165.xml")
    text = open(src, encoding="utf-8").read()
    old = "Data Source=../data/opendaoc.sqlite3.db;"
    assert old in text, "serverconfig.xml has an unexpected database path"
    open(dst, "w", encoding="utf-8").write(text.replace(old, "Data Source=../data/opendaoc-165.sqlite3.db;"))
    return dst


def main():
    rebuild = "--rebuild" in sys.argv
    if server_running():
        sys.exit("CoreServer is running; stop the server first.")
    if os.path.exists(TARGET) and not rebuild:
        sys.exit(f"{TARGET} exists and holds BotRealm progress; use --rebuild to replace it (it is backed up).")
    tmp = TARGET + ".tmp"
    if os.path.exists(tmp):
        os.remove(tmp)
    src = sqlite3.connect(MAIN)
    dst = sqlite3.connect(tmp)
    src.backup(dst)
    src.close()
    dst.execute("PRAGMA foreign_keys=OFF")
    with dst:
        fresh_slate(dst)
        for key, value in SETTINGS.items():
            set_property(dst, key, value)
        sys.path.insert(0, HERE)
        import content165
        removed = content165.apply(dst)
        removed.update(content165.apply_drops(dst, os.path.join(HERE, "drops165.json")))
        removed.update(content165.apply_additions(dst, HERE))
    dst.execute("PRAGMA journal_mode=DELETE")
    dst.execute("VACUUM")
    check = dst.execute("PRAGMA quick_check").fetchone()[0]
    dst.close()
    if check != "ok":
        sys.exit(f"quick_check failed: {check}")
    if os.path.exists(TARGET):
        stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
        shutil.move(TARGET, os.path.join(DATA, f"opendaoc-165.sqlite3.before-rebuild-{stamp}.db"))
    os.replace(tmp, TARGET)
    print(f"BotRealm database: {TARGET} ({os.path.getsize(TARGET) // 1048576} MB)")
    print(f"server config: {write_config()}")
    for reason, count in removed.items():
        print(f"  {count:6} rows: {reason}")


if __name__ == "__main__":
    main()
