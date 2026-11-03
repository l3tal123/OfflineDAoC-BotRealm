"""BotRealm: the real 1.64 loading pictures and the 1.64
standing-stones character screen, as the original client shows them in BotRealm (2026-10-30; owner: "add the 1.64 loading screens and standing stones too").

    python build_loading_165.py

Reads (both from the BotRealm work folder, TRUE165_WORK; folders set in pipeline/paths.py):
  <TRUE165_WORK>/work/loading165.dat   region -> pictures, 1.127 loading.dat format: [regionNNN]
        screenNN=archive://data/loading/alb1.mpk:alb0 (regions the 1.64 file lists use its pictures, dungeon instances
        keep pictures that exist in 1.64, post-1.65 regions have none - screens=0)
  <DAOC_164_ZIP> (DAoC 1.64.zip)                      data/loading/*.mpk, phousing/data/loading/housing.mpk
        (800 x 600 8-bit TGAs)
  <TRUE165_WORK>/work/pregame/stones164_plate.png   the 1.64 charman Main.tga (standing stones) with its
        baked-in labels inpainted, centred on a 2:1 plate
Writes godot/content/ui/loading165/<archive>_<name>.png, godot/content/ui/loading165/loading165.json
{"regions": {region id: [picture, ...]}} and godot/content/pregame/stones164_plate.png.
"""
import io, json, re, shutil, sys, tempfile, zipfile
import paths as _paths
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from assets_pipeline import ROOT
from daoc_mpk import read_mpk
from PIL import Image

WORK = _paths.TRUE165_WORK / 'work'
ZIP164 = _paths.ZIP_164
OUT = ROOT / 'godot/content/ui/loading165'


def table():
    regions, cur = {}, None
    for line in (WORK / 'loading165.dat').read_text(encoding='latin1').splitlines():
        line = line.strip()
        m = re.match(r'\[region(\d+)\]', line)
        if m:
            cur = int(m.group(1))
            regions[cur] = []
            continue
        m = re.match(r'screen\d+=archive://(.+?):(.+)$', line)
        if m and cur is not None:
            regions[cur].append((m.group(1).lower(), m.group(2).strip()))
    return regions


def main():
    regions = table()
    OUT.mkdir(parents=True, exist_ok=True)
    z = zipfile.ZipFile(ZIP164)
    archives = {}
    tmp = Path(tempfile.mkdtemp(prefix='load164_'))
    try:
        out, missing = {}, []
        for rid, screens in sorted(regions.items()):
            names = []
            for arc, pic in screens:
                if arc not in archives:
                    member = next((n for n in z.namelist() if n.lower() == arc), None)
                    if member is None:
                        archives[arc] = {}
                    else:
                        p = tmp / Path(arc).name
                        p.write_bytes(z.read(member))
                        archives[arc] = read_mpk(str(p))
                files = archives[arc]
                raw = next((v for k, v in files.items() if k.rsplit('.', 1)[0] == pic.lower()), None)
                if raw is None:
                    missing.append((rid, arc, pic))
                    continue
                key = f'{Path(arc).stem}_{pic}'.lower()
                dst = OUT / f'{key}.png'
                if not dst.exists():
                    Image.open(io.BytesIO(raw)).convert('RGB').save(dst)
                names.append(key)
            if names:
                out[str(rid)] = names
        (OUT / 'loading165.json').write_text(json.dumps({'regions': out}, indent=1), encoding='utf-8')
        shutil.copyfile(WORK / 'pregame/stones164_plate.png', ROOT / 'godot/content/pregame/stones164_plate.png')
        print(len(out), 'regions with pictures,', len(list(OUT.glob('*.png'))), 'pictures, missing:', missing[:10])
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


if __name__ == '__main__':
    main()
