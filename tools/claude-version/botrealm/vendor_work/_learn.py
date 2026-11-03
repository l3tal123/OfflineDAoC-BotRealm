import json,sqlite3,collections,re,sys
sys.path.insert(0,'.')
import pageparse
R={'Albion':1,'Midgard':2,'Hibernia':3,'All':0}
c=sqlite3.connect('file:world165_snapshot.db?mode=ro',uri=True); c.row_factory=sqlite3.Row
def n(s): return re.sub(r'\s+',' ',(s or '').lower().replace('’',"'").replace('`',"'")).strip()
db=collections.defaultdict(list)
for r in c.execute('select * from ItemTemplate'): db[n(r['Name'])].append(r)
pairs=[]
for l in open('period_item_pages.jsonl',encoding='utf-8'):
    rec=json.loads(l); p=pageparse.parse(rec); rl=R.get(rec['realm'],0)
    cands=[r for r in db.get(n(rec['name']),[]) if r['Realm'] in (rl,0) or rl==0]
    if cands: pairs.append((rec,p,cands))
print('matched pages',len(pairs))
import pickle
