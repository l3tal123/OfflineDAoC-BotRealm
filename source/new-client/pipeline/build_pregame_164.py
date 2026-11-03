"""BotRealm: the period 1.64 pregame art for the new client (owner 2026-10-31: "figure out what the
period 1.65 realm selection, character select screen, creation screen, play buttons, etc. actually looked like and plan
to replace the current ones" - PERIOD PREGAME PLAN.md). From the owner's real 1.64 client zip:
  charman/*.tga        Main (character select), chooserealm (+ _but emblems), chargen (+ _Summ), the button sheets
  charman/summary.mpk  summary.txt (the race + base class descriptions), charman/names.dat (Generate)
  data/login.mpk       login.bmp (the login panel), smcamback / flames (already used), title / camback
Writes godot/content/ui/pregame164/original/*.png and pregame164.json: every button sheet's grid (rows x the four
states: normal grey, hover gold, white, pressed red) with each row's name, and the text files.
    python build_pregame_164.py
"""
import io, json, sys, zipfile
import paths as _paths
from pathlib import Path
from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from assets_pipeline import ROOT
from daoc_mpk import read_mpk

ZIP = _paths.ZIP_164
OUT = ROOT / 'godot/content/ui/pregame164'

# charbut.tga: 4 states across (97 px), 51 rows down (23 px) - the names read off the sheet, top to bottom
CHARBUT_ROWS = ['Paladin', 'Armsman', 'Scout', 'Acolyte', 'Elementalist', 'Mage', 'Minstrel', 'Theurgist', 'Avalonian',
                'Briton', 'Highlander', 'Saracen', 'Male', 'Female', 'Generate', 'Short', 'Average', 'Tall', 'Delete',
                'Quit', 'Cancel', 'Armswoman', 'Elementalist (2)', 'Mage (2)', 'Fighter', 'Mage (3)', 'Acolyte (2)',
                'Rogue', 'Troll', 'Norseman', 'Kobold', 'Dwarf', 'Viking', 'Seer', 'Rogue (2)', 'Norsewoman', 'Mystic',
                'Celt', 'Lurikeen', 'Firbolg', 'Elf', 'Way of Arms', 'Way of Stealth', 'Way of Nature', 'Way of Magic',
                'Inconnu', 'Valkyn', 'Sylvan', 'Disciple', 'Way of the Grove', '']
# the other sheets: [file, columns (states), rows, row names]
SHEETS = {
    'charbut': ['charman/charbut.tga', 4, 51, CHARBUT_ROWS],
    'play': ['charman/play_main.tga', 1, 4, ['normal', 'hover', 'white', 'pressed']],
    'options': ['charman/options_main.tga', 1, 4, ['normal', 'hover', 'white', 'pressed']],
    'delete': ['charman/delchar_main.tga', 1, 4, ['normal', 'hover', 'white', 'pressed']],
    'quit': ['charman/quit_main.tga', 1, 4, ['normal', 'hover', 'white', 'pressed']],
    'cancel_proceed_back': ['charman/charcanpro.TGA', 4, 3, ['Cancel', 'Proceed', 'Back']],
    'stats': ['charman/charstat.TGA', 2, 8, ['Strength', 'Constitution', 'Dexterity', 'Quickness', 'Intelligence',
                                             'Piety', 'Empathy', 'Charisma']],
    'statbut': ['charman/statbut.tga', 4, 2, ['plus', 'minus']],
    'realm_emblems': ['charman/chooserealm_but.TGA', 4, 3, ['Albion', 'Midgard', 'Hibernia']],
}
SCREENS = {'main': 'charman/Main.tga', 'chooserealm': 'charman/chooserealm.tga', 'chargen': 'charman/chargen.tga',
           'chargen_summary': 'charman/chargen_Summ.tga', 'title': 'data/title.tga', 'camback': 'data/camback.tga',
           'dialog': 'charman/dialog.tga'}


def main():
    z = zipfile.ZipFile(ZIP)
    names = {n.replace('\\', '/').lower(): n for n in z.namelist()}

    def raw(path):
        return z.read(names[path.lower()])

    (OUT / 'original').mkdir(parents=True, exist_ok=True)
    meta = {'source': '1.64 real client (DAoC 1.64.zip)', 'screens': {}, 'sheets': {}}
    for key, path in SCREENS.items():
        im = Image.open(io.BytesIO(raw(path))).convert('RGBA')
        im.save(OUT / 'original' / f'{key}.png')
        meta['screens'][key] = {'file': f'original/{key}.png', 'size': list(im.size)}
    for key, (path, cols, rows, row_names) in SHEETS.items():
        im = Image.open(io.BytesIO(raw(path))).convert('RGBA')
        im.save(OUT / 'original' / f'{key}.png')
        meta['sheets'][key] = {'file': f'original/{key}.png', 'size': list(im.size), 'columns': cols, 'rows': rows,
                               'cell': [im.width // cols, im.height // rows], 'row_names': row_names,
                               'states': ['normal', 'hover', 'white', 'pressed'] if cols == 4 else []}
    login = read_mpk_bytes(raw('data/login.mpk'))
    Image.open(io.BytesIO(login['login.bmp'])).convert('RGBA').save(OUT / 'original' / 'login_panel.png')
    meta['screens']['login_panel'] = {'file': 'original/login_panel.png'}
    summary = read_mpk_bytes(raw('charman/summary.mpk'))['summary.txt'].decode('latin1')
    (OUT / 'summary.txt').write_text(summary, encoding='utf-8')
    (OUT / 'names.dat').write_bytes(raw('charman/names.dat'))
    meta['texts'] = {'summary': 'summary.txt', 'names': 'names.dat'}
    (OUT / 'pregame164.json').write_text(json.dumps(meta, indent=1), encoding='utf-8')
    print(len(meta['screens']), 'screens,', len(meta['sheets']), 'button sheets ->', OUT)


def read_mpk_bytes(data):
    import tempfile, os
    with tempfile.NamedTemporaryFile(suffix='.mpk', delete=False) as f:
        f.write(data)
        p = f.name
    try:
        return read_mpk(p)
    finally:
        os.remove(p)


if __name__ == '__main__':
    main()
