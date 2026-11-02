"""True 1.65 Mode: the dropped-loot bag as the 1.64 client drew it (owner 2026-11-01: "add the old bags as drop since
the bags were updated around catacombs"). Model 488 "All purpose bag" is items/loot.NIF in both clients, but the later
client's is a 460-triangle bag with lootbag.dds; the 1.64 client's (DAoC 1.64.zip items/loot.NIF) is the old
62-triangle one with items/bag.dds. Converted next to the later one and named in weapons.json as the model's
"file_164" - WeaponModels.path_of draws it in True 1.65 Mode.

    python build_loot_164.py
Writes godot/content/weapons/loot_164/model.gltf, its texture in godot/content/weapons/textures/{original,x4plus},
and "file_164" on the models that use loot.NIF in godot/content/weapons/weapons.json.
"""
import io, json, sys, zipfile
from pathlib import Path
import paths as _paths
from assets_pipeline import ROOT
from build_zone import upscale_dir, save_if_changed
import nif_static
from PIL import Image

OUT = ROOT / 'godot/content/weapons'
NIFS = {'loot': 'items/loot.NIF'}                          # weapons.json nif name -> the 1.64 zip's file


def main():
    z = zipfile.ZipFile(_paths.need(_paths.ZIP_164, 'ZIP_164'))
    names = {n.lower(): n for n in z.namelist()}
    data = json.loads((OUT / 'weapons.json').read_text(encoding='utf-8'))
    odir = OUT / 'textures/original'
    for nif, member in NIFS.items():
        d = OUT / f'{nif}_164'
        d.mkdir(parents=True, exist_ok=True)
        info = nif_static.convert(z.read(names[member.lower()]), str(d / 'model.gltf'), '../textures/x4plus')
        for t in sorted(info['textures']):
            src = names.get(f'items/{t}.dds') or names.get(f'items/{t}.tga')
            if src is None:
                raise SystemExit(f'{nif}: texture {t} not in the 1.64 items folder')
            if (odir / f'{t}.png').exists():
                raise SystemExit(f'{nif}: a texture named {t} already exists - give the 1.64 one its own name first')
            save_if_changed(Image.open(io.BytesIO(z.read(src))).convert('RGBA'), odir / f'{t}.png')
        n = 0
        for m in data['models'].values():
            if str(m.get('nif', '')).lower() == nif:
                m['file_164'] = f'{nif}_164/model.gltf'
                n += 1
        print(f'{nif}: 1.64 model, {info.get("triangles")} triangles, textures {sorted(info["textures"])}; {n} models')
    (OUT / 'weapons.json').write_text(json.dumps(data, indent=1), encoding='utf-8')
    upscale_dir(odir, OUT / 'textures/x4plus', factor_after=4)


if __name__ == '__main__':
    main()
