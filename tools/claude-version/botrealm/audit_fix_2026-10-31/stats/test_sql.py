"""Run updates.json (and alt_apr28_uthgard.json) against a private COPY of the snapshot: every statement must run,
match exactly one row; a second run must change nothing."""
import hashlib, json, os, sqlite3, sys
HERE = os.path.dirname(os.path.abspath(__file__))
SNAP = os.path.normpath(os.path.join(HERE, '..', '..', 'world165_audit.db'))
TEST = os.path.join(HERE, 'test.db')


def fresh():
    if os.path.exists(TEST):
        os.remove(TEST)
    src = sqlite3.connect(f'file:{SNAP}?mode=ro', uri=True)
    dst = sqlite3.connect(TEST)
    src.backup(dst)
    src.close()
    dst.execute('create index if not exists ix_test_lower_idnb on ItemTemplate(lower(Id_nb))')   # test copy only
    return dst


def digest(con):
    h = hashlib.sha256()
    for r in con.execute('select * from ItemTemplate order by Id_nb'):
        h.update(repr(r).encode())
    return h.hexdigest()


def run(con, ups, label):
    bad = []
    for u in ups:
        cur = con.execute(u['sql'])
        if cur.rowcount != 1:
            bad.append((cur.rowcount, u['sql'][:120]))
    con.commit()
    print(label, 'statements', len(ups), 'not exactly one row:', len(bad), bad[:5])
    return bad


final = {}
for fn in ('updates_final.json', 'updates.json'):
    ups = json.load(open(os.path.join(HERE, fn), encoding='utf-8'))
    con = fresh()
    n0 = con.execute('select count(*) from ItemTemplate').fetchone()[0]
    d0 = digest(con)
    b1 = run(con, ups, fn + ' run1')
    d1 = digest(con)
    con.execute('select changes()')
    tc = con.total_changes
    b2 = run(con, ups, fn + ' run2')
    d2 = digest(con)
    n1 = con.execute('select count(*) from ItemTemplate').fetchone()[0]
    print(fn, 'rows', n0, n1, 'run1 changed table:', d0 != d1, '| run2 changed nothing:', d1 == d2)
    # no ToA-or-later bonus type left anywhere, except the period-attested ones (163/164/165/156) a page listed
    later = list(range(99, 120)) + list(range(145, 257))
    q = ' or '.join(f'Bonus{k}Type in ({",".join(map(str, later))})' for k in range(1, 11)) +         f' or ExtraBonusType in ({",".join(map(str, later))})'
    left = con.execute(f'select Id_nb from ItemTemplate where {q}').fetchall()
    rows = con.execute(f'select * from ItemTemplate where {q}').fetchall()
    cols = [d[0] for d in con.execute('select * from ItemTemplate limit 1').description]
    off = []
    for r in rows:
        d = dict(zip(cols, r))
        ts = [d[f'Bonus{k}Type'] for k in range(1, 11) if d[f'Bonus{k}']] + ([d['ExtraBonusType']] if d['ExtraBonus'] else [])
        if any(t in later and t not in (156, 163, 164, 165) for t in ts if t):   # value-0 slots are inert
            off.append(d['Id_nb'])
    print(fn, 'rows still naming a later type id:', len(rows), '| with a non-zero later bonus not attested on a period page:', len(off), off[:10])
    final[fn] = digest(con)
    con.close()
    if off:
        allowed = set()
        for r_ in json.load(open(os.path.join(HERE, 'items.json'), encoding='utf-8')):
            if r_.get('kept_epic_dragon_types'):
                allowed.add(r_['id'])
        off = [o for o in off if o not in allowed]
        print('   ... of which not kept by decision 2 (epic/dragon drops):', len(off), off[:10])
    if off:
        sys.exit(1)
    if b1 or b2 or d1 != d2 or n0 != n1:
        sys.exit(1)

print('updates.json and updates_final.json give identical tables:', final['updates.json'] == final['updates_final.json'])
# timing as the launcher will run it (no expression index on the live DB)
import time
con = sqlite3.connect(TEST)
con.execute('drop index if exists ix_test_lower_idnb')
ups = json.load(open(os.path.join(HERE, 'updates.json'), encoding='utf-8'))
ups_f = json.load(open(os.path.join(HERE, 'updates_final.json'), encoding='utf-8'))
t0 = time.time()
for u in ups[:300]:
    con.execute(u['sql'])
con.rollback()
dt = time.time() - t0
print(f'lower() form (no index): 300 statements {dt:.1f}s -> all {len(ups)} ~{dt / 300 * len(ups):.0f}s per refresh')
t0 = time.time()
for u in ups_f:
    con.execute(u['sql'])
con.rollback()
print(f'updates_final.json (Id_nb=, indexed): all {len(ups_f)} in {time.time() - t0:.2f}s')
con.close()
