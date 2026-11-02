"""Make the small downloader zip for the release page, and add it to the release's SHA256SUMS.txt.

    python -I make_downloader_true165.py --assets <assets folder with v<version>> [--version 0.1]

Download-OfflineDAoC-True165-v<version>.zip holds one folder with GET-OFFLINE-DAOC-TRUE165.cmd (double-click it),
Get-OfflineDAoC-True165.ps1 (what it runs) and READ ME.txt. Run make_release_true165.py first.
"""
from __future__ import annotations

import argparse
import hashlib
import zipfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
CMD = "GET-OFFLINE-DAOC-TRUE165.cmd"
PS1 = "Get-OfflineDAoC-True165.ps1"


def readme(version: str) -> str:
    lines = [
        f"OFFLINE DAOC TRUE 1.65 v{version} - DOWNLOADER",
        "",
        "1. Put this folder somewhere with a short path, for example C:\\Games\\OfflineDAoC.",
        "   (Not inside OneDrive, Program Files or a very deep folder.)",
        f"2. Double-click {CMD}.",
        "   If Windows SmartScreen says it protected your PC, click More info, then Run anyway.",
        "3. It checks your PC and asks what to do:",
        "   1 = Update my game: updates the game you already have, in the folder it is in, with a",
        "       smaller download. Close the game and the launcher first. Your account, characters,",
        "       companions, bots and settings stay; the files it replaces are backed up in",
        "       patch-backups\\ in the game folder.",
        "   2 = New install: the complete game, into a new folder, OfflineDAoC-True165, next to",
        "       this downloader's folder (or C:\\Games\\OfflineDAoC-True165 if that path would be too",
        "       long: the game needs a short path to keep its graphics cache).",
        "   It shows the download size and asks before it starts.",
        "4. If the download stops or you close the window, double-click the same file again:",
        "   it continues where it stopped.",
        "5. When it's done, open the OfflineDAoC-True165 folder and double-click",
        "   START OFFLINE DAOC TRUE 1.65.cmd. Then click START SERVER, wait for RUNNING, and click",
        "   ENTER REALM.",
        "",
        "You need: 64-bit Windows 10 or 11, a CPU with AVX2, a graphics card with Vulkan",
        "(a current driver), 16 GB RAM recommended, and about 48 GB of free space for a new install.",
        "Nothing else to install: .NET comes with the game, and no original DAoC client is needed.",
        "",
        "Full guide and troubleshooting:",
        "https://github.com/shadowofze/OfflineDAoC-True165/blob/main/docs/PLAY.md",
    ]
    return "\r\n".join(lines) + "\r\n"


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--assets", type=Path, required=True)
    parser.add_argument("--version", default="0.2")
    args = parser.parse_args()
    folder = args.assets.resolve() / f"v{args.version}"
    if not (folder / "download-manifest.json").is_file():
        raise SystemExit(f"{folder} has no download-manifest.json; run make_release_true165.py first")
    cmd = (HERE / CMD).read_bytes()
    assert b"\r\n" in cmd and b"\n" not in cmd.replace(b"\r\n", b""), f"{CMD} must use CRLF line endings"
    ps1 = (HERE / PS1).read_bytes()
    assert all(b < 128 for b in ps1), f"{PS1} must be plain ASCII (Windows PowerShell 5.1 reads it as ANSI)"
    text = ps1.decode("ascii")
    assert f"$ReleaseVersion = '{args.version}'" in text, f"{PS1}'s default ReleaseVersion is not {args.version}"
    name = f"Download-OfflineDAoC-True165-v{args.version}"
    archive = folder / f"{name}.zip"
    archive.unlink(missing_ok=True)
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as zf:
        zf.writestr(f"{name}/{CMD}", cmd)
        zf.writestr(f"{name}/{PS1}", ps1)
        zf.writestr(f"{name}/READ ME.txt", readme(args.version).encode("ascii"))
    with zipfile.ZipFile(archive) as zf:
        assert zf.testzip() is None
    lines = [f"{sha(p)}  {p.name}" for p in sorted(folder.iterdir()) if p.is_file() and p.name != "SHA256SUMS.txt"]
    (folder / "SHA256SUMS.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"{archive.name}: {archive.stat().st_size:,} bytes")


if __name__ == "__main__":
    main()
