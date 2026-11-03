"""Step 1: parse the period merchant blocks out of period_item_pages.jsonl (raw text keeps order).
Output: period_merchant_pages.json  (one record per page that has a merchant block)."""
import json
import os
import re
import sys

sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
REALMS = {'(Albion)': 'Albion', '(Midgard)': 'Midgard', '(Hibernia)': 'Hibernia', '(All)': 'All'}
MARK = re.compile(r'can be purchased from Merchants:?\|')
PRICE = re.compile(r'^(?:Purchased|Purchase) for (.+)$', re.I)
PFROM = re.compile(r'^Purchase(?:d)? (?:from|by) (.+?)(?: for (.+))?$', re.I)
FROMT = re.compile(r'^From (.+?) \((.+)\)$')


def is_term(t):
    return (t in ('Found in:', 'Send a picture', '[', 'Magical Ability:', 'Submitted by:', 'Per Patch')
            or t.startswith('This item') or t.startswith('Per Patch') or t.startswith('Notes')
            or PRICE.match(t) is not None or PFROM.match(t) is not None)


def levels(raw):
    m = re.search(r'Levels of [Uu]se:\|(\d+)\s*-\s*(\d+)', raw)
    lou = [int(m.group(1)), int(m.group(2))] if m else None
    m = re.search(r'Level to Attain:\|(\d+)', raw)
    att = int(m.group(1)) if m else None
    return lou, att


def stat(raw, label):
    m = re.search(r'\|' + label + r':\|([\d.]+)', '|' + raw)
    return float(m.group(1)) if m else None


def parse(d):
    raw = d['raw']
    m = MARK.search(raw)
    if not m:
        return None
    toks = [t.strip() for t in raw[m.end():].split('|')]
    toks = [t for t in toks if t and t != ',']
    merchants, found, prices, notes, junk = [], [], [], [], []
    i = 0
    # 1. names, each optionally followed by a (Realm) tag
    while i < len(toks) and not is_term(toks[i]):
        t = toks[i]
        if t in REALMS:
            if merchants and merchants[-1]['realm_tag'] is None:
                merchants[-1]['realm_tag'] = REALMS[t]
        else:
            loc = None
            mm = re.match(r'^(.+?) \((DF|Darkness Falls)\)$', t)
            if mm:
                t, loc = mm.group(1), 'Darkness Falls'
            t = re.sub(r'\s*\(NPC\)$', '', t)
            if len(t.split()) > 4 or t.endswith('.') or ':' in t:
                junk.append(t)   # a user comment, not a merchant name
            else:
                merchants.append({'name': t, 'realm_tag': None, 'where': loc, 'src': 'list'})
        i += 1
    # 2. the rest of the block: Found in: zones, prices, "From X (Town)", "Purchased from X"
    in_found = False
    while i < len(toks):
        t = toks[i]
        if t in ('Send a picture', '[', 'Magical Ability:') or t.startswith('Per Patch') or t.startswith('This item is dropped'):
            break
        if t == 'Found in:':
            in_found = True
        elif PRICE.match(t):
            prices.append(t)
        elif PFROM.match(t):
            mm = PFROM.match(t)
            merchants.append({'name': mm.group(1).strip(), 'realm_tag': None, 'where': None, 'src': 'purchased_from_note'})
            if mm.group(2):
                prices.append('Purchased for ' + mm.group(2))
        elif FROMT.match(t):
            mm = FROMT.match(t)
            merchants.append({'name': mm.group(1).strip(), 'realm_tag': None, 'where': mm.group(2).strip(), 'src': 'from_note'})
        elif in_found:
            found.append(t)
        i += 1
    lou, att = levels(raw)
    return {
        'key': d['key'], 'name': d['name'], 'realm': d['realm'], 'capture': d['capture'],
        'merchants': merchants, 'found_in': found, 'prices': prices,
        'levels_of_use': lou, 'level_to_attain': att,
        'af': stat(raw, 'Armor Factor'), 'dps': stat(raw, 'DPS'), 'delay': stat(raw, 'Delay'),
        'crafted': d['crafted'], 'junk_in_merchant_block': junk,
    }


def main():
    out = []
    for line in open(os.path.join(HERE, 'period_item_pages.jsonl'), encoding='utf-8'):
        r = parse(json.loads(line))
        if r:
            out.append(r)
    json.dump(out, open(os.path.join(HERE, 'period_merchant_pages.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=0)
    named = sum(1 for r in out if r['merchants'])
    print('pages with merchant block', len(out), 'with named merchants', named, 'location only', sum(1 for r in out if not r['merchants'] and r['found_in']), 'nothing', sum(1 for r in out if not r['merchants'] and not r['found_in']))


if __name__ == '__main__':
    main()
