"""Dry run of the launcher's TrueMode.Refresh (OfflineDaoc.Dashboard\\Launcher\\TrueMode.cs) on a COPY of a True 1.65 world.
Same steps, same order, one transaction: settings, remove rules (archived), updates, period drops (replaced), add-*.json
(insert missing, update ours, delete stale). Usage: python -I dryrun.py <content folder> <source db> <out db>"""
import datetime
import json
import os
import shutil
import sqlite3
import sys
import uuid

folder, src, out = sys.argv[1:4]
shutil.copy(src, out)
for ext in ('-wal', '-shm', '-journal'):
    if os.path.exists(out + ext):
        os.remove(out + ext)
c = sqlite3.connect(out)
c.execute('PRAGMA foreign_keys=OFF')
now = datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')
rules = json.load(open(os.path.join(folder, 'true165-rules.json'), encoding='utf-8'))
log = {}


def archive(table, key, where, args, reason):
    cols = [r[1] for r in c.execute(f'PRAGMA table_info("{table}")')]
    rows = c.execute(f'SELECT * FROM "{table}" WHERE {where}', args).fetchall()
    for row in rows:
        rec = dict(zip(cols, row))
        c.execute('INSERT INTO offline_true165_removed (SourceTable,RowKey,Reason,RowJson,RemovedAt) VALUES (?,?,?,?,?)',
                  (table, str(rec.get(key)), reason, json.dumps(rec, default=str), now))
    c.execute(f'DELETE FROM "{table}" WHERE {where}', args)
    if rows:
        log['removed: ' + reason] = log.get('removed: ' + reason, 0) + len(rows)


c.execute('BEGIN')
for k, v in rules['settings'].items():
    if c.execute('UPDATE ServerProperty SET Value=? WHERE lower(`Key`)=?', (v, k.lower())).rowcount == 0:
        log['setting missing (launcher inserts it): ' + k] = 1
c.execute('CREATE TABLE IF NOT EXISTS offline_true165_removed (Id INTEGER PRIMARY KEY AUTOINCREMENT, SourceTable TEXT NOT NULL, '
          'RowKey TEXT, Reason TEXT NOT NULL, RowJson TEXT NOT NULL, RemovedAt TEXT NOT NULL)')
R = rules['remove']
for cls, why in R['mob_classes'].items():
    archive('Mob', 'Mob_ID', 'ClassType = ?', (cls,), why)
for pkg, why in R['mob_packages'].items():
    archive('Mob', 'Mob_ID', 'PackageID = ?', (pkg,), why)
for name, why in R['mob_names'].items():
    archive('Mob', 'Mob_ID', "Name = ? AND COALESCE(PackageID,'') <> ?", (name, R['mob_names_keep_package']), why)
for guild, why in R['mob_guilds'].items():
    archive('Mob', 'Mob_ID', 'Guild = ?', (guild,), why)
for cls, why in R['loot_generators'].items():
    archive('LootGenerator', 'LootGenerator_ID', 'LootGeneratorClass = ?', (cls,), why)
for w in R.get('where', []):
    archive(w['table'], w['key'], w['where'], (), w['reason'])
for u in rules.get('updates', []):
    n = c.execute(u['sql']).rowcount
    tag = u['reason'].split(':')[0]
    log['updated: ' + tag] = log.get('updated: ' + tag, 0) + max(n, 0)
c.execute('CREATE TABLE IF NOT EXISTS offline_true165_added (Id INTEGER PRIMARY KEY AUTOINCREMENT, SourceTable TEXT NOT NULL, '
          'RowKey TEXT NOT NULL, Reason TEXT NOT NULL, AddedAt TEXT NOT NULL)')
c.execute("DELETE FROM LootTemplate WHERE LootTemplate_ID IN (SELECT RowKey FROM offline_true165_added WHERE SourceTable='LootTemplate' AND Reason LIKE 'period drops:%')")
c.execute("DELETE FROM offline_true165_added WHERE SourceTable='LootTemplate' AND Reason LIKE 'period drops:%'")
dp = os.path.join(folder, 'drops165.json')
if os.path.exists(dp):
    doc = json.load(open(dp, encoding='utf-8'))
    reason = 'period drops: ' + doc.get('source', 'drops165.json')
    added = 0
    for mob, e in doc['monsters'].items():
        have = {r[0].lower() for r in c.execute('SELECT ItemTemplateID FROM LootTemplate WHERE lower(TemplateName)=lower(?)', (mob,))}
        for iid, chance in e['items']:
            if iid.lower() in have:
                continue
            have.add(iid.lower())
            k = str(uuid.uuid4())
            c.execute('INSERT INTO LootTemplate (TemplateName,ItemTemplateID,Chance,Count,LastTimeRowUpdated,LootTemplate_ID) VALUES (?,?,?,1,?,?)',
                      (mob, iid, int(chance), now, k))
            c.execute('INSERT INTO offline_true165_added (SourceTable,RowKey,Reason,AddedAt) VALUES (?,?,?,?)', ('LootTemplate', k, reason, now))
            added += 1
        row = c.execute('SELECT MobXLootTemplate_ID, DropCount FROM MobXLootTemplate WHERE lower(MobName)=lower(?) AND lower(LootTemplateName)=lower(?)',
                        (mob, mob)).fetchone()
        if row is None:
            k = str(uuid.uuid4())
            c.execute('INSERT INTO MobXLootTemplate (MobName,LootTemplateName,DropCount,LastTimeRowUpdated,MobXLootTemplate_ID) VALUES (?,?,?,?,?)',
                      (mob, mob, int(e['drop_count']), now, k))
            c.execute('INSERT INTO offline_true165_added (SourceTable,RowKey,Reason,AddedAt) VALUES (?,?,?,?)', ('MobXLootTemplate', k, reason, now))
        elif (row[1] or 0) < e['drop_count']:
            c.execute('UPDATE MobXLootTemplate SET DropCount=? WHERE MobXLootTemplate_ID=?', (int(e['drop_count']), row[0]))
    log['period drop rows added'] = added
for path in sorted(p for p in os.listdir(folder) if p.startswith('add-') and p.endswith('.json')):
    d = json.load(open(os.path.join(folder, path), encoding='utf-8'))
    table, key, reason = d['table'], d['key'], d['reason']
    cols = {r[1] for r in c.execute(f'PRAGMA table_info("{table}")')}
    ins = upd = 0
    for row in d['rows']:
        rid = str(row[key])
        use = [k for k in row if k in cols]
        if c.execute(f'SELECT 1 FROM "{table}" WHERE "{key}"=?', (rid,)).fetchone():
            if not c.execute('SELECT 1 FROM offline_true165_added WHERE SourceTable=? AND RowKey=?', (table, rid)).fetchone():
                continue
            c.execute(f'UPDATE "{table}" SET {",".join(chr(34) + k + chr(34) + "=?" for k in use)} WHERE "{key}"=?',
                      [row[k] if not isinstance(row[k], bool) else int(row[k]) for k in use] + [rid])
            upd += 1
            continue
        c.execute(f'INSERT INTO "{table}" ({",".join(chr(34) + k + chr(34) for k in use)}) VALUES ({",".join("?" * len(use))})',
                  [row[k] if not isinstance(row[k], bool) else int(row[k]) for k in use])
        c.execute('INSERT INTO offline_true165_added (SourceTable,RowKey,Reason,AddedAt) VALUES (?,?,?,?)', (table, rid, reason, now))
        ins += 1
    listed = {str(r[key]) for r in d['rows']}
    stale = [r[0] for r in c.execute('SELECT RowKey FROM offline_true165_added WHERE SourceTable=? AND Reason=?', (table, reason)) if r[0] not in listed]
    for k in stale:
        c.execute(f'DELETE FROM "{table}" WHERE "{key}"=?', (k,))
        c.execute('DELETE FROM offline_true165_added WHERE SourceTable=? AND RowKey=? AND Reason=?', (table, k, reason))
    log[f'{path}: inserted/updated/stale'] = f'{ins}/{upd}/{len(stale)}'
c.execute('COMMIT')
print('quick_check', c.execute('PRAGMA quick_check').fetchone()[0])
c.close()
for k, v in log.items():
    print(v, '|', k)
