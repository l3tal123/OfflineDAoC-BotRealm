"""Makes the CLEAN BotRealm world database that ships with BotRealm (and that this repository carries,
zipped, as database/opendaoc-165.sqlite3.zip).

It copies a world database with SQLite's backup API (the source is opened read-only and never changed), then gives the
copy a fresh start, the same rules the launcher's TrueMode.FreshSlate uses:
- every save table in runtime/botrealm/progress-policy.json emptied (accounts, characters, items, money, bots, hired
  companions, guilds except the starter guilds, exchange listings/sales/escrow, houses, quests, tasks, effects) and
  every log/state table in its ClearTables (news, logs, run state...);
- keeps, doors and relics back to their defaults (runtime/botrealm/release-world-defaults.json; relics on their home
  shrines), houses unowned;
- auto account creation on, experience 1x, nobody made GM, population target 0 (the launcher sets it when bots are
  added);
- the content-tracking tables (offline_true165_content, offline_true165_added, offline_true165_removed) are KEPT, so
  the first START SERVER doesn't apply the world content again.
Then VACUUM, an integrity check, a check that no save table has rows left, and the zip.

Usage (from the repository root, with the server stopped):
    python database/make_clean_db.py <source world db> [--out database/opendaoc-165.sqlite3.zip]
The source is normally runtime/data/opendaoc-165.sqlite3.db of a working install.
"""
import argparse
import json
import os
import sqlite3
import subprocess
import sys
import tempfile
import zipfile
from datetime import datetime

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
CONTENT = os.path.join(ROOT, 'runtime', 'botrealm')
DB_NAME = 'opendaoc-165.sqlite3.db'

# Kept on purpose: content and its tracking, not progress.
KEEP = {'offline_true165_content', 'offline_true165_added', 'offline_true165_removed', 'offline_level50_loadouts',
        'offline_classic165_removed_mobs', 'offline_classic165_restored_mobs', 'offline_world_migrations'}


def tables(c):
    return {r[0] for r in c.execute("SELECT name FROM sqlite_master WHERE type='table'")}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('source')
    ap.add_argument('--out', default=os.path.join(ROOT, 'database', 'opendaoc-165.sqlite3.zip'))
    a = ap.parse_args()
    # Never copy a world the server has open (2026-10-31: copying a live database once locked the running server).
    if sys.platform == 'win32' and 'coreserver.exe' in subprocess.run(['tasklist'], capture_output=True, text=True).stdout.lower():
        sys.exit('The server (CoreServer.exe) is running. Stop it first.')
    policy = json.load(open(os.path.join(CONTENT, 'progress-policy.json'), encoding='utf-8'))
    defaults = json.load(open(os.path.join(CONTENT, 'release-world-defaults.json'), encoding='utf-8'))
    rules = json.load(open(os.path.join(CONTENT, 'true165-rules.json'), encoding='utf-8'))
    starter = rules.get('starter_guilds') or []

    work = tempfile.mkdtemp(prefix='clean165-')
    db_path = os.path.join(work, DB_NAME)
    src = sqlite3.connect(f'file:{os.path.abspath(a.source)}?mode=ro', uri=True)
    dst = sqlite3.connect(db_path)
    src.backup(dst)
    src.close()
    c = dst
    c.execute('PRAGMA foreign_keys=OFF')
    have = tables(c)
    clear = set(policy['ProgressTables']) | set(policy['ClearTables']) | {'FactionAggroLevel'}
    assert not (clear & KEEP), clear & KEEP
    with c:
        for t in sorted(clear):
            if t in have and t not in ('Guild', 'GuildRank'):
                c.execute(f'DELETE FROM "{t}"')
        guilds = ','.join("'" + g.replace("'", "''") + "'" for g in starter) or "''"
        c.execute('UPDATE Guild SET RealmPoints=0,BountyPoints=0,Bank=0,MeritPoints=0,HaveGuildHouse=0,GuildHouseNumber=0')
        c.execute(f'DELETE FROM GuildRank WHERE GuildID NOT IN ({guilds})')
        c.execute(f'DELETE FROM Guild WHERE GuildID NOT IN ({guilds})')
        if 'DBHouse' in have:
            c.execute("UPDATE DBHouse SET OwnerID='',GuildName='',GuildHouse=0,HasConsignment=0,KeptMoney=0,Model=0,Name='' "
                      "WHERE COALESCE(OwnerID,'')<>''")
        for keep_id, k in defaults['Keep'].items():
            c.execute("UPDATE Keep SET Realm=?,Level=?,ClaimedGuildName='' WHERE KeepID=?", (k['Realm'], k['Level'], int(keep_id)))
        for door_id, d in defaults['Door'].items():
            c.execute('UPDATE Door SET Health=?,State=? WHERE Door_ID=?', (d['Health'], d['State'], door_id))
        # Relics home, on the shrine whose emblem matches (as the launcher's keep and relic reset does).
        pads = c.execute("SELECT Region,X,Y,Z,Heading,Emblem FROM WorldObject WHERE ClassType='DOL.GS.GameRelicPad'").fetchall()
        for rid, realm, rtype in c.execute('SELECT RelicID,OriginalRealm,relicType FROM Relic').fetchall():
            home = [p for p in pads if p[5] == realm + 10 * rtype]
            if len(home) != 1:
                sys.exit(f'relic {rid}: no single home shrine (found {len(home)})')
            r, x, y, z, h, _ = home[0]
            c.execute('UPDATE Relic SET Realm=OriginalRealm,LastRealm=OriginalRealm,Region=?,X=?,Y=?,Z=?,Heading=? WHERE RelicID=?',
                      (r, x, y, z, h, rid))
        if 'offline_population_settings' in have:
            c.execute("UPDATE offline_population_settings SET Value='0' WHERE Key='ActiveTarget'")
        if 'offline_local_options' in have:
            c.execute("INSERT OR REPLACE INTO offline_local_options(Key,Value) VALUES('MakeMeGM','false')")
        for key, value in (('allow_auto_account_creation', 'True'), ('xp_rate', '1')):
            if c.execute('UPDATE ServerProperty SET Value=? WHERE lower(`Key`)=?', (value, key)).rowcount == 0:
                sys.exit(f'server property {key} missing')
    c.execute('PRAGMA journal_mode=DELETE')
    c.execute('VACUUM')
    ok = c.execute('PRAGMA integrity_check').fetchone()[0]
    if ok != 'ok':
        sys.exit(f'integrity_check: {ok}')
    left = {t: c.execute(f'SELECT count(*) FROM "{t}"').fetchone()[0] for t in clear
            if t in have and t not in ('Guild', 'GuildRank', 'offline_local_options')}   # MakeMeGM=false is set above
    left = {t: n for t, n in left.items() if n}
    if left:
        sys.exit(f'save tables not empty: {left}')
    if not c.execute('SELECT count(*) FROM offline_true165_content').fetchone()[0]:
        sys.exit('offline_true165_content is empty: the first start would apply the content again')
    c.close()

    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with zipfile.ZipFile(a.out, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        z.write(db_path, DB_NAME)
    size_db, size_zip = os.path.getsize(db_path), os.path.getsize(a.out)
    os.remove(db_path)
    os.rmdir(work)
    print(f'clean database: {size_db / 1e6:.1f} MB, zipped {size_zip / 1e6:.1f} MB -> {a.out}  ({datetime.now():%Y-%m-%d %H:%M})')
    if size_zip > 95e6:
        print('WARNING: the zip is over 95 MB; GitHub needs it split')


if __name__ == '__main__':
    main()
