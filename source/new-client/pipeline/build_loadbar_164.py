"""True 1.65 Mode: the 1.64 client's loading bar (owner 2026-11-01: "you should use the classic loading bar").
1.64 game.dll (setup 0x4d4603, draw 0x4d4ba2 - read by the original-client research): data/LOADBAR1.tga is the empty
bar (grey glass tube, gold end caps), data/LOADBAR2.tga the full one (pink-red), both 82 x 19; the bar sits 10 px in from
the loading picture's bottom right corner and fills by revealing LOADBAR2's columns over LOADBAR1's, left to right,
never stretched; the tube is blitted keying out its top-left colour (black), so the picture shows around the caps.
(data/Loadbarb.tga is not used by 1.64.) The line beside it, "Loading...", is fonts/stone20.bmp row 0 (white): 5 rows of
20 px; each glyph from '!' on is the next run of inked columns in the top row (bitmap font 0x470864, column test
0x4707d2), the same x ranges in every row; space = the columns before '!'; black keyed out; no letter spacing.

    python build_loadbar_164.py
Writes godot/content/ui/loading165/bar/original/loadbar1.png, loadbar2.png, stone20.png (row 0) and their x4plus copies,
and godot/content/ui/loading165/bar/stone20.json {char: [x0, x1)}.
"""
import io, json, zipfile
import paths as _paths
from assets_pipeline import ROOT
from build_zone import upscale_dir, save_if_changed
from PIL import Image

OUT = ROOT / 'godot/content/ui/loading165/bar'


def keyed(img):
    """RGBA with the image's top-left colour transparent, as the 1.64 blitter keys it (flag 1)."""
    key = img.getpixel((0, 0))
    rgba = img.convert('RGBA')
    px = rgba.load()
    src = img.load()
    for y in range(img.height):
        for x in range(img.width):
            if src[x, y] == key:
                px[x, y] = (0, 0, 0, 0)
    return rgba


def main():
    z = zipfile.ZipFile(_paths.need(_paths.ZIP_164, 'ZIP_164'))
    names = {n.lower(): n for n in z.namelist()}
    odir = OUT / 'original'
    odir.mkdir(parents=True, exist_ok=True)
    for src, dst in (('data/loadbar1.tga', 'loadbar1'), ('data/loadbar2.tga', 'loadbar2')):
        save_if_changed(keyed(Image.open(io.BytesIO(z.read(names[src])))), odir / f'{dst}.png')
        print(dst)
    font = Image.open(io.BytesIO(z.read(names['fonts/stone20.bmp'])))
    h = font.height // 5
    idx = font.load()
    inked = [any(idx[x, y] != 0 for y in range(h)) for x in range(font.width)]
    cells, ch, x = {}, 0x21, 0
    first = None
    while x < font.width and ch <= 0xFF:
        while x < font.width and not inked[x]:
            x += 1
        if x >= font.width:
            break
        x0 = x
        while x < font.width and inked[x]:
            x += 1
        cells[chr(ch)] = [x0, x]
        first = x0 if first is None else first
        ch += 1
    cells[' '] = [0, first - 1]                            # (x 0 .. start('!') - 1: 6 px, as game.dll)
    save_if_changed(keyed(font.crop((0, 0, font.width, h))), odir / 'stone20.png')
    (OUT / 'stone20.json').write_text(json.dumps({'height': h, 'cells': cells}, indent=1), encoding='utf-8')
    print('stone20:', len(cells), 'glyphs, height', h, 'A', cells.get('A'), 'L', cells.get('L'))
    upscale_dir(odir, OUT / 'x4plus', factor_after=4)


if __name__ == '__main__':
    main()
