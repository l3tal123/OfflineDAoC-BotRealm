"""Extract every crawled period Allakhazam item page (wayback, <= Nov 2003) into a flat JSONL for the vendor work.
Read-only on the crawl. Output: period_item_pages.jsonl (one line per item page) in this folder."""
import gzip
import html
import json
import os
import re
import sys

sys.stdout.reconfigure(encoding='utf-8')
CRAWL = os.path.join(os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "..")), "development-source", "research", "period-data", "archive", "wayback-allakhazam", "period_items.jsonl.gz")  # found from this script
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'period_item_pages.jsonl')


def text(body):
    t = re.sub(r'(?is)<script.*?</script>|<style.*?</style>', '', body)
    t = html.unescape(re.sub(r'<[^>]+>', '|', t))
    return re.sub(r'\s*\|[\s|]*', '|', t)


n = vendor = 0
with open(OUT, 'w', encoding='utf-8') as out:
    for line in gzip.open(CRAWL, 'rt', encoding='utf-8', errors='replace'):
        r = json.loads(line)
        if str(r.get('status')) != '200':
            continue
        t = text(str(r.get('body') or ''))
        m = re.search(r'\|([^|]{2,80})\|Realm:\|([^|]+)\|', t)
        if not m:
            continue
        start = m.start(1)
        end = min([i for i in (t.find('">Send a picture', start), t.find('?Subject=DAOC', start), t.find('Send a correction', start)) if i > 0] or [len(t)])
        seg = t[start:end]
        merch = []
        prices = []
        mm = re.search(r'can be purchased from Merchants\|(.*?)\|(?:Found in:|This item|">Send|\?Subject|Per Patch|$)', seg)
        if mm:
            merch = [x.strip() for x in mm.group(1).split('|') if x.strip() and x.strip() != ',']
        prices = [x for x in merch if x.lower().startswith('purchased for')]
        merch = [x for x in merch if not x.lower().startswith('purchased for')]
        found = re.search(r'Found in:\|(.*?)(?:\|Per Patch|\|">Send|$)', seg)
        rec = {
            'key': int(r['key']), 'capture': r.get('timestamp'), 'url': r.get('url'),
            'name': m.group(1).strip(), 'realm': m.group(2).strip(),
            'merchants': merch,
            'prices': prices,   # "Purchased for 3s 50c" lines from the merchant block, when the page has them
            'found_in': [x for x in (found.group(1).split('|') if found else []) if x.strip()],
            'crafted': 'can be crafted by players' in seg,
            'raw': seg,   # the item block as pipe-separated text: "Field:|value|..." (stats, bonuses, levels)
        }
        out.write(json.dumps(rec, ensure_ascii=False) + '\n')
        n += 1
        vendor += bool(merch)
print('pages', n, 'with merchants', vendor, '->', OUT)
