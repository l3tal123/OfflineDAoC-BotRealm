import os, gzip,json,re,html,sys
sys.stdout.reconfigure(encoding='utf-8')
AR = os.path.join(os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "..")), "development-source", "research", "period-data", "archive")  # found from this script
want={'2138':'Eileen Morton','1520':'Nealcail','6370':'Vinandr','6371':'Melanaha','6510':'Borjal','2779':'Royd','6561':'Somyr','1383':'Dyemaster Marna','6560':'Caryan','6718':'Madarl'}
out={}
for line in gzip.open(AR+r'\wayback-allakhazam\period_mobs.jsonl.gz','rt',encoding='utf-8',errors='replace'):
    m=re.search(r'cmob=(\d+)',line[:400])
    if not m or m.group(1) not in want: continue
    r=json.loads(line)
    t=html.unescape(re.sub(r'<[^>]+>','|',re.sub(r'(?is)<script.*?</script>','',r.get('body') or '')))
    t=re.sub(r'\s*\|[\s|]*','|',t)
    g=re.search(r'(Goods for Sale:|Known Loot:)\|(.*?)\|(?:Known Habitats|Last Updated|Related|\[)',t)
    out[want[m.group(1)]]={'url':r.get('url'),'capture':r.get('timestamp'),'kind':g.group(1) if g else None,'goods':[x for x in (g.group(2).split('|') if g else []) if x.strip()]}
json.dump(out,open('period_mob_goods.json','w',encoding='utf-8'),ensure_ascii=False,indent=1)
for k,v in out.items(): print(k,v['capture'],v['kind'],len(v['goods']),v['goods'][:6])
