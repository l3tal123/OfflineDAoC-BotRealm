"""drops agent: dry run on a COPY of the audit snapshot, in the launcher's order (TrueMode.Refresh):
settings, remove rules (existing + where_rules.json), updates (existing + updates.json), period drops replaced by the
new drops165.json, then every add-*.json (the installed ones + add-dropitems165.json). Run twice; the second run must
change nothing. Writes work/test_result.json and work/coin_only.json.

    python -I test_apply.py
"""
import datetime, hashlib, os, shutil, sqlite3, sys, uuid
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import *

T165 = os.path.join(CV, 'runtime', 'true165')
TEST = os.path.join(HERE, 'test')
DBT = os.path.join(TEST, 'world_test.db')


def exec_(c, sql, args=()):
    cur = c.execute(sql, args)
    return cur.rowcount


def archive(c, table, key, where, reason, args=()):
    now = datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')
    cur = c.execute(f'SELECT * FROM "{table}" WHERE {where}', args)
    cols = [d[0] for d in cur.description]
    rows = cur.fetchall()
    for r in rows:
        d = dict(zip(cols, r))
        c.execute('INSERT INTO offline_true165_removed (SourceTable,RowKey,Reason,RowJson,RemovedAt) VALUES (?,?,?,?,?)',
                  (table, str(d.get(key) or ''), reason, json.dumps(d, default=str), now))
    c.execute(f'DELETE FROM "{table}" WHERE {where}', args)
    return len(rows)


def add_drops(c, path):
    doc = jload(path)
    now = datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')
    reason = 'period drops: ' + doc.get('source', 'drops165.json')
    n_rows = n_mx = n_up = 0
    for mob, v in doc['monsters'].items():
        have = {r[0].lower() for r in c.execute('SELECT ItemTemplateID FROM LootTemplate WHERE lower(TemplateName)=lower(?)', (mob,))}
        for iid, ch in v['items']:
            if iid.lower() in have:
                continue
            have.add(iid.lower())
            k = str(uuid.uuid4())
            c.execute('INSERT INTO LootTemplate (TemplateName,ItemTemplateID,Chance,Count,LastTimeRowUpdated,LootTemplate_ID) '
                      'VALUES (?,?,?,1,?,?)', (mob, iid, ch, now, k))
            c.execute('INSERT INTO offline_true165_added (SourceTable,RowKey,Reason,AddedAt) VALUES (?,?,?,?)',
                      ('LootTemplate', k, reason, now))
            n_rows += 1
        cap = v['drop_count']
        r = c.execute('SELECT MobXLootTemplate_ID, DropCount FROM MobXLootTemplate WHERE lower(MobName)=lower(?) AND '
                      'lower(LootTemplateName)=lower(?)', (mob, mob)).fetchone()
        if r is None:
            k = str(uuid.uuid4())
            c.execute('INSERT INTO MobXLootTemplate (MobName,LootTemplateName,DropCount,LastTimeRowUpdated,MobXLootTemplate_ID) '
                      'VALUES (?,?,?,?,?)', (mob, mob, cap, now, k))
            c.execute('INSERT INTO offline_true165_added (SourceTable,RowKey,Reason,AddedAt) VALUES (?,?,?,?)',
                      ('MobXLootTemplate', k, reason, now))
            n_mx += 1
        elif (r[1] or 0) < cap:
            c.execute('UPDATE MobXLootTemplate SET DropCount=? WHERE MobXLootTemplate_ID=?', (cap, r[0]))
            n_up += 1
    return n_rows, n_mx, n_up


def add_rows(c, files):
    now = datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')
    stats = {}
    for path in files:
        doc = jload(path)
        table, key, reason = doc['table'], doc['key'], doc['reason']
        cols = {r[1] for r in c.execute(f'PRAGMA table_info("{table}")')}
        ins = upd = dele = 0
        for row in doc['rows']:
            rid = str(row[key])
            present = c.execute(f'SELECT 1 FROM "{table}" WHERE "{key}"=?', (rid,)).fetchone() is not None
            names = [k for k in row if k in cols]
            vals = [(1 if row[k] is True else 0 if row[k] is False else row[k]) for k in names]
            if present:
                ours = c.execute('SELECT 1 FROM offline_true165_added WHERE SourceTable=? AND RowKey=?', (table, rid)).fetchone()
                if ours is None:
                    continue
                before = c.execute(f'SELECT {",".join(chr(34) + n + chr(34) for n in names)} FROM "{table}" WHERE "{key}"=?',
                                   (rid,)).fetchone()
                c.execute(f'UPDATE "{table}" SET {",".join(chr(34) + n + chr(34) + "=?" for n in names)} WHERE "{key}"=?',
                          vals + [rid])
                if tuple(before) != tuple(vals):
                    upd += 1
                continue
            c.execute(f'INSERT INTO "{table}" ({",".join(chr(34) + n + chr(34) for n in names)}) VALUES ({",".join("?" * len(names))})', vals)
            c.execute('INSERT INTO offline_true165_added (SourceTable,RowKey,Reason,AddedAt) VALUES (?,?,?,?)',
                      (table, rid, reason, now))
            ins += 1
        listed = {str(r[key]) for r in doc['rows']}
        stale = [r[0] for r in c.execute('SELECT RowKey FROM offline_true165_added WHERE SourceTable=? AND Reason=?',
                                         (table, reason)) if r[0] not in listed]
        for g in stale:
            c.execute(f'DELETE FROM "{table}" WHERE "{key}"=?', (g,))
            c.execute('DELETE FROM offline_true165_added WHERE SourceTable=? AND RowKey=? AND Reason=?', (table, g, reason))
            dele += 1
        stats[os.path.basename(path)] = (ins, upd, dele)
    return stats


def refresh(c, rules, my_rules, my_updates, drops_path, add_files):
    log = {'remove': {}, 'updates_changed': 0, 'errors': []}
    for k, v in rules['settings'].items():
        n = exec_(c, 'UPDATE ServerProperty SET Value=? WHERE lower(`Key`)=?', (v, k))
    rm = rules['remove']
    c.execute('CREATE TABLE IF NOT EXISTS offline_true165_removed (Id INTEGER PRIMARY KEY AUTOINCREMENT, SourceTable TEXT NOT NULL, '
              'RowKey TEXT, Reason TEXT NOT NULL, RowJson TEXT NOT NULL, RemovedAt TEXT NOT NULL)')
    n = 0
    for cls, why in rm['mob_classes'].items():
        n += archive(c, 'Mob', 'Mob_ID', 'ClassType = ?', why, (cls,))
    for pkg, why in rm['mob_packages'].items():
        n += archive(c, 'Mob', 'Mob_ID', 'PackageID = ?', why, (pkg,))
    for name, why in rm['mob_names'].items():
        n += archive(c, 'Mob', 'Mob_ID', "Name = ? AND COALESCE(PackageID,'') <> ?", why, (name, rm['mob_names_keep_package']))
    for g, why in rm['mob_guilds'].items():
        n += archive(c, 'Mob', 'Mob_ID', 'Guild = ?', why, (g,))
    for cls, why in rm['loot_generators'].items():
        n += archive(c, 'LootGenerator', 'LootGenerator_ID', 'LootGeneratorClass = ?', why, (cls,))
    log['remove']['existing named rules'] = n
    for i, w in enumerate(rm.get('where') or []):
        log['remove'][f'existing where {i}'] = archive(c, w['table'], w['key'], w['where'], w['reason'])
    for i, w in enumerate(my_rules):
        try:
            log['remove'][f'drops where {i} ({w["table"]})'] = archive(c, w['table'], w['key'], w['where'], w['reason'])
        except Exception as e:
            log['errors'].append(f'where {i}: {e}')
    for u in rules.get('updates') or []:
        c.execute(u['sql'])
    tc = c.total_changes
    for i, u in enumerate(my_updates):
        try:
            before = c.execute("SELECT group_concat(Chance) FROM LootTemplate WHERE " + u['sql'].split(' WHERE ', 1)[1]).fetchone()[0]
            c.execute(u['sql'])
            after = c.execute("SELECT group_concat(Chance) FROM LootTemplate WHERE " + u['sql'].split(' WHERE ', 1)[1]).fetchone()[0]
            if before != after:
                log['updates_changed'] += 1
            if after is None:
                log['errors'].append(f'update {i} matched no row: {u["sql"]}')
        except Exception as e:
            log['errors'].append(f'update {i}: {e}')
    c.execute('CREATE TABLE IF NOT EXISTS offline_true165_added (Id INTEGER PRIMARY KEY AUTOINCREMENT, SourceTable TEXT NOT NULL, '
              'RowKey TEXT NOT NULL, Reason TEXT NOT NULL, AddedAt TEXT NOT NULL)')
    log['old period rows deleted'] = exec_(c, "DELETE FROM LootTemplate WHERE LootTemplate_ID IN (SELECT RowKey FROM "
                                              "offline_true165_added WHERE SourceTable='LootTemplate' AND Reason LIKE 'period drops:%')")
    c.execute("DELETE FROM offline_true165_added WHERE SourceTable='LootTemplate' AND Reason LIKE 'period drops:%'")
    log['drops165 (rows, new mobx, raised dropcount)'] = add_drops(c, drops_path)
    log['add files (inserted, updated, deleted)'] = add_rows(c, add_files)
    return log


def fingerprint(c):
    """content of the loot tables (without generated keys / times), the drop item rows, archive size"""
    h = {}
    lt = sorted(c.execute('SELECT lower(TemplateName), lower(ItemTemplateID), Chance, Count FROM LootTemplate').fetchall())
    h['LootTemplate'] = (len(lt), hashlib.sha1(repr(lt).encode()).hexdigest())
    mx = sorted(c.execute('SELECT lower(MobName), lower(LootTemplateName), DropCount FROM MobXLootTemplate').fetchall())
    h['MobXLootTemplate'] = (len(mx), hashlib.sha1(repr(mx).encode()).hexdigest())
    it = sorted(c.execute('SELECT * FROM ItemTemplate').fetchall(), key=repr)
    h['ItemTemplate'] = (len(it), hashlib.sha1(repr(it).encode()).hexdigest())
    h['offline_true165_removed'] = c.execute('SELECT count(*) FROM offline_true165_removed').fetchone()[0]
    h['Mob'] = c.execute('SELECT count(*) FROM Mob').fetchone()[0]
    h['MerchantItem'] = c.execute('SELECT count(*) FROM MerchantItem').fetchone()[0]
    return h


def checks(c):
    out = {}
    out['loot rows pointing at a missing item'] = c.execute(
        'SELECT TemplateName, ItemTemplateID FROM LootTemplate WHERE lower(ItemTemplateID) NOT IN '
        '(SELECT lower(Id_nb) FROM ItemTemplate)').fetchall()
    out['OTD rows pointing at a missing item'] = c.execute(
        'SELECT MobName, ItemTemplateID FROM LootOTD WHERE lower(ItemTemplateID) NOT IN (SELECT lower(Id_nb) FROM ItemTemplate)').fetchall()
    out['Chance < 0 rows left'] = c.execute('SELECT count(*) FROM LootTemplate WHERE Chance < 0').fetchone()[0]
    pk = c.execute("SELECT i.PackageID, count(*) FROM LootTemplate l JOIN ItemTemplate i ON lower(i.Id_nb)=lower(l.ItemTemplateID) "
                   "GROUP BY 1 ORDER BY 2 DESC").fetchall()
    out['loot rows by item package'] = pk
    out['dead MobXLootTemplate rows (other template, no rows)'] = c.execute(
        'SELECT count(*) FROM MobXLootTemplate WHERE lower(LootTemplateName) <> lower(MobName) AND lower(LootTemplateName) '
        'NOT IN (SELECT lower(TemplateName) FROM LootTemplate)').fetchone()[0]
    return out


def coin_only(c, W):
    """spawned monsters (Mob rows of the True 1.65 regions, no service NPCs) whose loot after all of this has no item:
    MobXLootTemplate rows -> their tables (own table when it has rows), else the own table; plus LootOTD"""
    lt = collections.defaultdict(set)
    for t, i in c.execute('SELECT lower(TemplateName), lower(ItemTemplateID) FROM LootTemplate'):
        lt[t].add(i)
    mx = collections.defaultdict(list)
    for m, t in c.execute('SELECT lower(MobName), lower(LootTemplateName) FROM MobXLootTemplate'):
        mx[m].append(t)
    otd = {r[0].lower() for r in c.execute('SELECT MobName FROM LootOTD')}
    out = []
    for m, sp in W.spawns.items():
        tabs = mx.get(m) or [m]
        items = set()
        for t in tabs + [m]:
            items |= lt.get(t, set())
        if not items and m not in otd:
            out.append({'mob': W.mob_names[m], 'spawns': len(sp), 'level': round(W.mob_level(m), 1),
                        'zones': sorted({s['zone'] or f"region {s['Region']}" for s in sp})[:4],
                        'named': W.mob_names[m][:1].isupper()})
    return sorted(out, key=lambda x: (-x['spawns'], x['mob']))


def main():
    os.makedirs(TEST, exist_ok=True)
    for f in os.listdir(TEST):
        os.remove(os.path.join(TEST, f))
    src = ro()
    dst = sqlite3.connect(DBT)
    src.backup(dst)
    src.close()
    dst.close()
    rules = jload(os.path.join(T165, 'true165-rules.json'))
    my_rules = jload(os.path.join(HERE, 'where_rules.json'))
    my_updates = jload(os.path.join(HERE, 'updates.json'))
    drops = os.path.join(HERE, 'drops165.json')
    add_files = sorted([os.path.join(T165, f) for f in os.listdir(T165) if f.startswith('add-') and f.endswith('.json')] +
                       ([os.path.join(HERE, 'add-dropitems165.json')] if os.path.exists(os.path.join(HERE, 'add-dropitems165.json')) else []),
                       key=lambda p: os.path.basename(p))
    result = {}
    c = sqlite3.connect(DBT, isolation_level=None)
    before = fingerprint(c)
    result['before'] = before
    for run in (1, 2):
        top = c.execute('SELECT max(Id) FROM offline_true165_removed').fetchone()[0] or 0
        c.execute('BEGIN')
        log = refresh(c, rules, my_rules, my_updates, drops, add_files)
        c.execute('COMMIT')
        result[f'run {run}'] = log
        result[f'after run {run}'] = fingerprint(c)
        result[f'archived in run {run} by reason'] = dict(collections.Counter(
            r[0] for r in c.execute('SELECT Reason FROM offline_true165_removed WHERE Id > ?', (top,))))
    result['second run changed nothing'] = all(result['after run 1'][k] == result['after run 2'][k] for k in result['after run 1'])
    mine = {w['reason'] for w in my_rules}
    result['second run: rows my rules archived'] = sum(v for k, v in result['archived in run 2 by reason'].items() if k in mine)
    result['second run: loot/item content unchanged'] = all(result['after run 1'][k] == result['after run 2'][k]
                                                            for k in ('LootTemplate', 'MobXLootTemplate', 'ItemTemplate', 'Mob', 'MerchantItem'))
    result['checks'] = checks(c)
    W = World(DBT)
    co = coin_only(c, W)
    result['coin-only spawned monsters'] = len(co)
    result['coin-only named'] = sum(1 for x in co if x['named'])
    c.close()
    jdump(result, os.path.join(WORK, 'test_result.json'))
    jdump(co, os.path.join(WORK, 'coin_only.json'))
    # same count on the untouched snapshot for comparison
    c0 = ro()
    W0 = World()
    co0 = coin_only(c0, W0)
    result['coin-only before'] = len(co0)
    jdump(result, os.path.join(WORK, 'test_result.json'))
    print(json.dumps({k: v for k, v in result.items() if k not in ('checks',)}, indent=1, default=str)[:6000])
    ch = result['checks']
    print('missing-item rows', len(ch['loot rows pointing at a missing item']), 'OTD missing', len(ch['OTD rows pointing at a missing item']),
          'chance<0', ch['Chance < 0 rows left'], 'dead mobx', ch['dead MobXLootTemplate rows (other template, no rows)'])
    print('packages', ch['loot rows by item package'])


if __name__ == '__main__':
    main()
