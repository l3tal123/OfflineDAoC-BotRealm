"""Stage the new client for a BotRealm release package.

    python tools/stage_true165_release.py --out <package folder>\\new-client [--version 0.1]

Run from anywhere; the client is this script's parent folder (godot\\, tools\\godot_net\\). The staged folder holds:
    godot\\            the client: code, converted content, Godot's import cache and the built network client
                      (.godot\\mono\\temp\\bin\\Debug\\MMOEngine.dll)
    tools\\godot_net\\  the engine that runs it (Godot 4.7.2 .NET)
    VERSION.txt
It runs on the package's bundled .NET runtime (the package's tools\\dotnet, set as DOTNET_ROOT by its start script).
Never staged: developer mode (godot\\dev_mode.txt), the debug probes (godot\\tests), the editor's state
(godot\\.godot\\editor), the C# build's scratch (godot\\.godot\\mono\\temp\\obj), any save\\ folder, logs, and backup files.
The same rules as the 0.37c package builder (OfflineDAoC source/tools/build_newclient_037.py).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import shutil
from datetime import datetime, timezone
from pathlib import Path

CLIENT = Path(__file__).resolve().parent.parent
GODOT_EXE = "Godot_v4.7.2-stable_mono_win64.exe"
SKIP_GODOT_TOP = {"tests", "dev_mode.txt", "save"}
SKIP_PARTS = {"editor", "obj", ".vs", "__pycache__"}
SKIP_SUFFIXES = (".bak", ".tmp", ".log", ".orig", ".pre_valkyn")
FORBIDDEN = {"dev_mode.txt", "account.txt", "accounts.cfg", "login.cfg"}


def forbidden(name: str) -> bool:
    """Developer mode, saved logins and any test-account file."""
    name = name.lower()
    return name in FORBIDDEN or name.endswith("test_account.txt")


def sha(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def keep_godot(relative: Path) -> bool:
    parts = [p.lower() for p in relative.parts]
    if parts[0] in SKIP_GODOT_TOP:
        return False
    if parts[0] == ".godot":
        # the import cache and the built network client ship; the editor's state and the build scratch do not
        if len(parts) > 1 and parts[1] == "editor" or parts[1:4] == ["mono", "temp", "obj"]:
            return False
    elif any(p in SKIP_PARTS for p in parts[:-1]):
        return False
    return not parts[-1].endswith(SKIP_SUFFIXES)


def copy_tree(source: Path, target: Path, keep=lambda rel: True) -> tuple[int, int]:
    count = size = 0
    for path in sorted(source.rglob("*")):
        if not path.is_file():
            continue
        relative = path.relative_to(source)
        if not keep(relative):
            continue
        destination = target / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, destination)
        count += 1
        size += path.stat().st_size
        if count % 20000 == 0:
            print(f"  {count} files, {size / 1e9:.1f} GB", flush=True)
    return count, size


def check_network_client(godot: Path):
    """The C# network client must be built from the current code (the launcher never compiles it)."""
    dll = godot / ".godot/mono/temp/bin/Debug/MMOEngine.dll"
    if not dll.is_file():
        raise SystemExit("The network client is not built: run tools\\build_csharp.ps1 first")
    newest = max((p.stat().st_mtime, p) for p in godot.rglob("*.cs") if ".godot" not in p.parts)
    if newest[0] > dll.stat().st_mtime:
        raise SystemExit(f"{newest[1]} is newer than the built network client: run tools\\build_csharp.ps1 first")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", type=Path, required=True, help="the package's new-client folder (must not exist)")
    parser.add_argument("--version", default="0.1")
    args = parser.parse_args()
    out = args.out.resolve()
    if out.exists():
        raise SystemExit(f"{out} exists; refusing to overwrite a staging folder")
    godot, engine = CLIENT / "godot", CLIENT / "tools/godot_net"
    if not (godot / "project.godot").is_file() or not (engine / GODOT_EXE).is_file():
        raise SystemExit("the client folder must hold godot\\project.godot and tools\\godot_net\\" + GODOT_EXE)
    if not (godot / "content/.gdignore").is_file():
        raise SystemExit("godot\\content\\.gdignore is missing: Godot would try to import the whole content folder")
    check_network_client(godot)

    counts = {}
    print("Copying the client...", flush=True)
    counts["godot"] = copy_tree(godot, out / "godot", keep_godot)
    print("Copying the engine...", flush=True)
    counts["tools/godot_net"] = copy_tree(engine, out / "tools/godot_net",
                                          lambda rel: not rel.name.lower().endswith(SKIP_SUFFIXES))
    (out / "VERSION.txt").write_text(f"BotRealm v{args.version} new client\r\n", encoding="utf-8")

    bad = [p.relative_to(out).as_posix() for p in out.rglob("*") if p.is_file() and forbidden(p.name)]
    assert not bad, f"must not ship: {bad}"
    assert not (out / "godot/tests").exists() and not (out / "godot/dev_mode.txt").exists()
    assert not (out / "godot/.godot/editor").exists() and not (out / "godot/.godot/mono/temp/obj").exists()
    assert (out / "godot/.godot/mono/temp/bin/Debug/MMOEngine.dll").is_file()
    assert (out / "godot/content/.gdignore").is_file()

    files = [p for p in out.rglob("*") if p.is_file()]
    largest = max(files, key=lambda p: p.stat().st_size)
    report = {"built_utc": datetime.now(timezone.utc).isoformat(), "version": args.version,
              "counts": {k: {"files": c, "bytes": s} for k, (c, s) in counts.items()},
              "total_files": len(files), "total_bytes": sum(p.stat().st_size for p in files),
              "largest": [largest.relative_to(out).as_posix(), largest.stat().st_size],
              "network_client_sha256": sha(out / "godot/.godot/mono/temp/bin/Debug/MMOEngine.dll")}
    (out.parent / "new-client-stage-report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2), flush=True)


if __name__ == "__main__":
    main()
