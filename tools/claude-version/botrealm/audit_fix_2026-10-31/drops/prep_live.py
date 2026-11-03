"""drops agent: index the live Allakhazam item and mob pages (name, realm, Known Loot / habitats, dated comments).
Read-only on the archive; writes work/live_items.json and work/live_mobs.json."""
import gzip, html, json, os, re, sys

sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
RES = os.path.join(os.path.normpath(os.path.join(HERE, '..', '..', '..', '..', '..')), 'development-source', 'research', 'period-data', 'archive', 'allakhazam-live')   # the local period archive (not shipped)
MON = {m: i for i, m in enumerate(['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'], 1)}
DATE = re.compile(r'(?:class="posted"><span>|<b>Posted:</b>\s*<span>)\s*([A-Z][a-z]{2}) (\d{1,2}) ((?:19|20)\d\d)')


def text(h):
    t = re.sub(r'(?is)<script.*?</script>|<style.*?</style>', '', h)
    return re.sub(r'\s*\|[\s|]*', '|', html.unescape(re.sub(r'<[^>]+>', '|', t)))


def comments(h):
    """[(yyyymmdd, text)] of each post on the page."""
    out = []
    ms = list(DATE.finditer(h))
    for i, m in enumerate(ms):
        d = int(m.group(3)) * 10000 + MON.get(m.group(1), 0) * 100 + int(m.group(2))
        seg = h[m.end(): ms[i + 1].start() if i + 1 < len(ms) else len(h)]
        b = re.search(r'(?s)<div class="post-text">(.*?)</div>', seg)
        body = text(b.group(1)) if b else text(seg)[:600]
        out.append((d, body.strip('|')[:600]))
    return out


def realm_of(h):
    m = re.search(r'<b>Realm:\s*</b>\s*(?:</td><td>)?\s*<span id="(\w+)"', h)
    return m.group(1) if m else None


def name_of(r):
    t = (r.get('title') or '').split(' :: ')[0].strip()
    return html.unescape(t)


def items():
    out = {}
    for f in ('item.jsonl.gz', 'item_comments.jsonl.gz'):
        with gzip.open(os.path.join(RES, f), 'rt', encoding='utf-8') as g:
            for line in g:
                r = json.loads(line)
                h = r.get('html') or ''
                m = re.search(r'citem=(\d+)', r['url'])
                if not m or not h:
                    continue
                k = m.group(1)
                e = out.setdefault(k, {'citem': int(k), 'name': name_of(r), 'realm': realm_of(h), 'comments': []})
                t = text(h)
                if 'Found in:' in t and 'found_in' not in e:
                    seg = t.split('Found in:', 1)[1].split('|Post Comment', 1)[0]
                    e['found_in'] = [x for x in seg.split('|') if x][:20]
                if 'dropped' in t.lower() and 'drop_text' not in e:
                    i = t.lower().find('dropped')
                    e['drop_text'] = t[max(0, i - 100): i + 300]
                e['comments'] += comments(h)
    return out


def mobs():
    out = {}
    for f in ('mob.jsonl.gz', 'mob_comments.jsonl.gz'):
        with gzip.open(os.path.join(RES, f), 'rt', encoding='utf-8') as g:
            for line in g:
                r = json.loads(line)
                h = r.get('html') or ''
                m = re.search(r'cmob=(\d+)', r['url'])
                if not m or not h:
                    continue
                k = m.group(1)
                e = out.setdefault(k, {'cmob': int(k), 'name': name_of(r), 'realm': realm_of(h), 'loot': [],
                                       'habitats': [], 'comments': []})
                lm = re.search(r'(?s)<ul id="loots">(.*?)</ul>', h)
                if lm and not e['loot']:
                    e['loot'] = [(int(a), html.unescape(re.sub(r'<[^>]+>', '', b)).strip())
                                 for a, b in re.findall(r'citem=(\d+)">(.*?)</a>', lm.group(1))]
                hm = re.search(r'(?s)Known Habitats</b>:(.*?)</td>', h)
                if hm and not e['habitats']:
                    e['habitats'] = [html.unescape(x).strip() for x in re.findall(r'czone=\d+">(.*?)</a>', hm.group(1))]
                lu = re.search(r'Last Updated:</b><small>\s*(.*?)</small>', h)
                if lu:
                    e['last_updated'] = lu.group(1).strip()
                e['comments'] += comments(h)
    return out


if __name__ == '__main__':
    os.makedirs(os.path.join(HERE, 'work'), exist_ok=True)
    it = items()
    json.dump(it, open(os.path.join(HERE, 'work', 'live_items.json'), 'w', encoding='utf-8'), ensure_ascii=False)
    print('items', len(it), 'with pre-Nov-2003 comment', sum(1 for e in it.values() if any(d < 20031101 for d, _ in e['comments'])))
    mo = mobs()
    json.dump(mo, open(os.path.join(HERE, 'work', 'live_mobs.json'), 'w', encoding='utf-8'), ensure_ascii=False)
    print('mobs', len(mo), 'with loot', sum(1 for e in mo.values() if e['loot']),
          'with pre-Nov-2003 comment', sum(1 for e in mo.values() if any(d < 20031101 for d, _ in e['comments'])))
