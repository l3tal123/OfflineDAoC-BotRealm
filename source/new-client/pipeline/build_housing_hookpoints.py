"""BotRealm housing: where each house model's hookpoints are - the server places a hookpoint item (an NPC, a vault, a
bindstone, a tool table) at house X / Y + (X, Y) and Z 25000 + Z of its row in the world DB's househookpointoffset (DOL
House.GetHookpointLocation; interior units, never turned by the house heading), and the client draws the hookpoint
markers there ("two stacked white circles on the ground", 1.64 help) for /housepoints and their right-click menus.

    python build_housing_hookpoints.py      -> godot/content/housing/hookpoints.json {model: [[id, x, y, z, heading]]}
"""
import json, sqlite3
import paths as _paths
from assets_pipeline import ROOT

OUT = ROOT / 'godot/content/housing/hookpoints.json'


def main():
    db = sqlite3.connect('file:%s?mode=ro' % _paths.need(_paths.WORLD_DB, 'WORLD_DB'), uri=True)
    out = {}
    for model, hid, x, y, z, h in db.execute(
            'select HouseModel, HookpointID, X, Y, Z, Heading from househookpointoffset order by HouseModel, HookpointID'):
        out.setdefault(str(model), []).append([int(hid), int(x), int(y), int(z), int(h)])
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(out, indent=1), encoding='utf-8')
    print(OUT, {m: len(v) for m, v in out.items()})


if __name__ == '__main__':
    main()
