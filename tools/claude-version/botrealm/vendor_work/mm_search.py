"""Search every archive for the missing merchants' names; write mm_hits.json (read-only on archives)."""
import gzip, html, json, os, re, sys
sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
AR = os.path.join(os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "..")), "development-source", "research", "period-data", "archive")  # found from this script
NAMES = ['Eileen Morton', 'Caryan', 'Madarl', 'Nealcail', 'Melanaha', 'Rhodyn', 'Somyr', 'Tempeste', 'Borjal', 'Royd', 'Vinandr',
         'Dala', 'Darbo', 'Debo', 'Eluned', 'Dyemaster Marna', 'Ylaine Barret']
RX = re.compile(r'\b(' + '|'.join(re.escape(n) for n in NAMES) + r')\b')
FILES = ['wayback-allakhazam/period_mobs.jsonl.gz', 'wayback-allakhazam/quest_pages.jsonl.gz', 'wayback-allakhazam/items_direct.jsonl.gz',
         'allakhazam-live/mob.jsonl.gz', 'allakhazam-live/zone.jsonl.gz', 'allakhazam-live/zoneinfo.jsonl.gz', 'allakhazam-live/quest.jsonl.gz',
         'allakhazam-live/item.jsonl.gz', 'allakhazam-live/zone_comments.jsonl.gz', 'allakhazam-live/quest_comments.jsonl.gz',
         'uthgard/raw_pages.repaired.jsonl.gz', 'capnbry/raw_pages.jsonl.gz', 'wayback-warcry/raw_pages.jsonl.gz']
hits = []


def text(s):
    s = re.sub(r'(?is)<script.*?</script>|<style.*?</style>', '', s)
    return re.sub(r'\s+', ' ', html.unescape(re.sub(r'<[^>]+>', ' | ', s)))


for f in FILES + sys.argv[1:]:
    path = os.path.join(AR, f)
    if not os.path.exists(path):
        print('missing', f); continue
    n = 0
    for line in gzip.open(path, 'rt', encoding='utf-8', errors='replace'):
        if not RX.search(line):
            continue
        try:
            r = json.loads(line)
        except Exception:
            r = {'body': line}
        body = ''
        for k in ('body', 'html', 'text', 'content', 'page'):
            if isinstance(r.get(k), str):
                body = r[k]; break
        if not body:
            body = json.dumps(r, ensure_ascii=False)
        t = text(body)
        for m in RX.finditer(t):
            hits.append({'file': f, 'name': m.group(1), 'url': r.get('url') or r.get('key') or r.get('id'), 'ts': r.get('timestamp') or r.get('capture'),
                         'ctx': t[max(0, m.start() - 300): m.end() + 400]})
            n += 1
    print(f, n)
# uthgard locations + capnbry index as plain text
for f in ['uthgard/locations.json', 'capnbry/sightings_index.json']:
    s = open(os.path.join(AR, f), encoding='utf-8', errors='replace').read()
    for m in RX.finditer(s):
        hits.append({'file': f, 'name': m.group(1), 'ctx': s[max(0, m.start() - 200): m.end() + 300]})
json.dump(hits, open(os.path.join(HERE, 'mm_hits.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=0)
import collections
print(collections.Counter((h['name'], h['file'].split('/')[0]) for h in hits))
