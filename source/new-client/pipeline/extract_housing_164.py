"""The 1.64 client's files the 12 period housing zones are built from (pipeline/paths.py HOUSING_164), extracted
read-only from DAoC 1.64.zip (DAOC_164_ZIP) into TRUE165_WORK/work/164 (WORK_164) - only what those zones use:
  phousing/zones/zoneNNN/   the 12 zones (dat / csv / tex mpk, sounds.dat)
  phousing/textures/, phousing/phousing.mpk   the house materials and tables (for the house models, later)
  phousing/nifs/, zones/Nifs/   the models the zones' fixtures place (house signs, marketplaces, gates, message boards
                            from phousing/nifs; trees, buildings, rocks from zones/Nifs) and every texture they name
build_zone.py reads these for those zones (models and textures before the later client's own). Re-running rewrites
only files that differ.     python extract_housing_164.py"""
import csv, io, os, re, sys, tempfile, zipfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import paths as _paths
from daoc_mpk import read_mpk

TEX_DIRS = ('phousing/nifs/', 'phousing/textures/', 'zones/nifs/', 'zones/textures/')


def _mpk(z, info):
    """{entry: bytes} of an archive inside the zip (read_mpk reads files: through a temporary copy)."""
    with tempfile.NamedTemporaryFile(delete=False, suffix='.mpk') as f:
        f.write(z.read(info))
    try:
        return read_mpk(f.name)
    finally:
        os.unlink(f.name)


def main():
    z = zipfile.ZipFile(_paths.need(_paths.ZIP_164, 'ZIP_164'))
    names = {i.filename.lower(): i for i in z.infolist() if not i.is_dir()}
    want = {n for n in names if n == 'phousing/phousing.mpk' or n.startswith('phousing/textures/')
            or any(n.startswith(f'phousing/zones/zone{h:03d}/') for h in _paths.HOUSING_164)}
    models = set()
    for h in sorted(_paths.HOUSING_164):
        dat = _mpk(z, names[f'phousing/zones/zone{h:03d}/dat{h:03d}.mpk'])
        nifs = {int(r[0]): r for r in csv.reader(io.StringIO(dat['nifs.csv'].decode('latin1'))) if r and r[0].strip().isdigit()}
        for r in list(csv.reader(io.StringIO(dat['fixtures.csv'].decode('latin1'))))[2:]:
            if len(r) > 11 and r[1].strip().isdigit():
                models.add(nifs[int(r[1])][2].strip().rsplit('.', 1)[0].lower())
    texs, missing = set(), []
    for m in sorted(models):
        src = next((f'{d}/{m}.npk' for d in ('phousing/nifs', 'zones/nifs') if f'{d}/{m}.npk' in names), None)
        if src is None:
            missing.append(m)
            continue
        want.add(src)
        for k, raw in _mpk(z, names[src]).items():
            if k.endswith('.nif'):                         # the texture file names the model's NiSourceTextures hold
                texs |= {t.decode('latin1').lower() for t in re.findall(rb'([A-Za-z0-9_\- ]+)\.(?:dds|tga|bmp)', raw, re.I)}
    for n in names:
        base = n.rsplit('/', 1)[-1]
        if n.startswith(TEX_DIRS) and base.endswith(('.dds', '.tga', '.bmp')) and base.rsplit('.', 1)[0] in texs:
            want.add(n)
    written = 0
    for n in sorted(want):
        dst = _paths.WORK_164.joinpath(*names[n].filename.split('/'))
        data = z.read(names[n])
        if not dst.exists() or dst.read_bytes() != data:
            dst.parent.mkdir(parents=True, exist_ok=True)
            dst.write_bytes(data)
            written += 1
    print(f'{len(models)} models, {len(texs)} texture names, {len(want)} files ({written} written) -> {_paths.WORK_164}')
    if missing:
        print('models not in the 1.64 phousing/nifs or zones/Nifs:', missing)


if __name__ == '__main__':
    main()
