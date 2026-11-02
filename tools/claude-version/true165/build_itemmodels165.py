"""Item looks (inventory icons) that match the item (owner 2026-11-02: "boar tusk" showed a boot, "raw pork" a vial).

About 1,400 True 1.65 items were added with the look of "the closest existing sibling item", a guess. No archive keeps
their real model numbers (Uthgard and the 2003 Allakhazam pages show none), so the 1.64 client's own model list
(gamedata objects.csv: model -> client name -> icon) decides, backed by same-name items of the original database:
615 "tusk" for tusks, 102 "Meat" for meat, 106 "Tooth/Claw", 100 "Animal Skin", 108 "Spider Legs", gems by colour...
itemmodels165_decisions.json holds each change (from, to, both 1.64 names, the reason); it was merged from three
read-only research passes (scratchpad icons/: catalog, period, consensus, merge_models.py) with these rules: a new model
exists in the 1.64 client; client/period picks first; a weak pick only where today's model is not in the 1.64 client
or both passes agree; today's model is kept where its client name already fits the item.

The items are created by add files, which the launcher writes AFTER the rules "updates" (an update would be overwritten
every refresh), so the Model goes into the add-file rows themselves, both copies (tools and runtime\\true165).
Usage: python -I build_itemmodels165.py [--write]"""
import json, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
write = '--write' in sys.argv
decisions = json.load(open(os.path.join(HERE, 'itemmodels165_decisions.json'), encoding='utf-8'))
want = {k.lower(): v['to'] for k, v in decisions.items()}
seen = set()
for folder in (HERE, os.path.join(HERE, '..', '..', '..', 'runtime', 'true165')):
    for name in sorted(os.listdir(folder)):
        if not (name.startswith('add-') and name.endswith('.json')):
            continue
        path = os.path.join(folder, name)
        raw = open(path, 'rb').read()
        data = json.loads(raw.decode('utf-8'))
        if data.get('table') != 'ItemTemplate':
            continue
        n = 0
        for row in data['rows']:
            key = str(row.get('Id_nb', '')).lower()
            if key in want:
                seen.add(key)
                if row.get('Model') != want[key]:
                    row['Model'] = want[key]
                    n += 1
        if n:
            print(f'{folder == HERE and "tools" or "runtime"} {name}: {n} models')
            if write:
                nl = b'\r\n' if b'\r\n' in raw else b'\n'
                open(path, 'wb').write(json.dumps(data, indent=1, ensure_ascii=False).encode('utf-8').replace(b'\n', nl) +
                                       (nl if raw.endswith(nl) else b''))
missing = sorted(set(want) - seen)
print(f'decisions {len(want)}, found in add files {len(seen)}, not in any add file {len(missing)}', missing[:10])
