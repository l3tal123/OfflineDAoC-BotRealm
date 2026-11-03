"""BotRealm housing windows: the 1.64 client's own housing UI art, which it keeps apart from data/ in
phousing/data/ (game.dll skin table indices 0x31-0x37, loaded from phousing/data/<skin>/ - research 2026-11-02,
scratchpad housing/ui164.md): garden.bmp (House Garden Placement's frame, 375 x 417) and the garden grid's cell marks
ctrlpoint_none / _full / _emptygr / _nonerd / _fullrd (20 x 20). The other housing windows use data/ bitmaps
build_ui_classic.py already has (options, leftbot, buttons*, combo_*, listicon1). Keyed as build_ui_classic keys the
classic UI: (1,0,1) and pure green. No x4plus copies (UiArt scales the original up; ESRGAN smears 20 px marks).

    python build_ui_housing_164.py      -> godot/content/ui/classic/original/<name>.png (+ classic.json entries)
"""
import io, json, zipfile
import numpy as np
from PIL import Image
import paths as _paths
from assets_pipeline import ROOT

OUT = ROOT / 'godot/content/ui/classic'
NAMES = ['garden', 'ctrlpoint_none', 'ctrlpoint_full', 'ctrlpoint_emptygr', 'ctrlpoint_nonerd', 'ctrlpoint_fullrd',
         'ctrlpoint_empty']
KEYS = [(1, 0, 1), (0, 255, 0)]


def keyed(im):
    a = np.asarray(im.convert('RGB')).astype(np.int16)
    mask = np.zeros(a.shape[:2], bool)
    for k in KEYS:
        mask |= (np.abs(a - np.array(k)).max(axis=2) == 0)
    rgba = np.dstack([a.astype(np.uint8), np.where(mask, 0, 255).astype(np.uint8)])
    return Image.fromarray(rgba, 'RGBA'), int(mask.sum())


def main():
    z = zipfile.ZipFile(_paths.need(_paths.ZIP_164, 'ZIP_164'))
    names = {n.lower(): n for n in z.namelist()}
    meta_path = OUT / 'classic.json'
    meta = json.loads(meta_path.read_text(encoding='utf-8')) if meta_path.exists() else {}
    (OUT / 'original').mkdir(parents=True, exist_ok=True)
    for name in NAMES:
        src = names.get(f'phousing/data/{name}.bmp')
        if src is None:
            print('missing', name)
            continue
        img, n = keyed(Image.open(io.BytesIO(z.read(src))))
        img.save(OUT / f'original/{name}.png')
        meta[name] = {'size': list(img.size), 'keyed_pixels': n, 'file': src}
        print(name, img.size, 'keyed', n)
    meta_path.write_text(json.dumps(meta, indent=1), encoding='utf-8')


if __name__ == '__main__':
    main()
