"""Pack Offline DAoC True 1.65 into GitHub release parts, and record its baseline.

    python -I make_release_true165.py --game <game folder> --client <client folder> --repo <this repository>
                                      --out <assets folder> [--version 0.1]

What a player ends up with comes from three places, and none of them is written to:
    --game     START OFFLINE DAOC TRUE 1.65.cmd, runtime\\ (the launcher, the server, the navigation meshes, the
               True 1.65 content and the clean world) and tools\\dotnet\\ (the bundled .NET 10), made by
               development-source/tools/build_release_true165.py
    --client   a folder holding new-client\\ (godot\\ with its content, tools\\godot_net\\, VERSION.txt), made by the
               new client's packaging (source/new-client/tools)
    --repo     the player docs (PLAYER_DOCS), placed at the same paths in the game folder

<assets folder>\\v<version>\\ gets:
    OfflineDAoC-True165-v<version>-game[-partNN].zip     everything but new-client\\
    OfflineDAoC-True165-v<version>-client-partNN.zip     new-client\\
    download-manifest.json                              the parts, their sizes and SHA-256, for the downloader
    SHA256SUMS.txt                                      every asset's SHA-256
and <assets folder>\\baseline-<version>.sha256 is the SHA-256 of every installed file. Commit it to the repository as
release-baselines/<version>/package.sha256, so an install can be checked against it.

Every part is a verified zip under GitHub's 2 GB limit. The downloader unpacks and deletes one part before the next, so
an install never needs much more than the game's own size on disk. The package is checked first: no account, login,
log, backup, test character, developer switch or played world may be in it.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import sqlite3
import zipfile
from pathlib import Path

PART_BUDGET = 1_900_000_000          # bytes of files per part (GitHub's limit is 2 GiB per asset)
# The player docs every install gets, from the repository, at the same paths in the game folder.
PLAYER_DOCS = ["READ ME FIRST.txt", "QUICK COMMANDS.txt", "ALL SERVER COMMANDS.txt", "CHANGELOG.md", "LICENSE",
               "THIRD_PARTY.md", "docs/PLAY.md", "docs/NEW-CLIENT.md"]
# Already-compressed formats are stored, not deflated again.
STORED = {".png", ".jpg", ".jpeg", ".ogg", ".mp3", ".zip", ".7z", ".webp", ".ctex", ".scn", ".res", ".pck"}

REQUIRED = [
    "START OFFLINE DAOC TRUE 1.65.cmd",
    "runtime/OfflineDAoC.exe",
    "runtime/server/CoreServer.exe",
    "runtime/server/config/serverconfig-165.xml",
    "runtime/data/opendaoc-165.sqlite3.db",
    "runtime/true165/true165-rules.json",
    "tools/dotnet/dotnet.exe",
    "READ ME FIRST.txt",
    "new-client/VERSION.txt",
    "new-client/godot/project.godot",
    "new-client/godot/content/.gdignore",
    "new-client/godot/.godot/mono/temp/bin/Debug/MMOEngine.dll",
]
FORBIDDEN_NAMES = {"account.txt", "dev_mode.txt", "claude_test_account.txt", "d3d9.dll", "dxvk.conf"}
FORBIDDEN_PARTS = ("/logs/", "/deployment-backups/", "/mode-switch-backups/", "/keep-relic-reset-backups/",
                   "/true165-devtool-build/", "/client-opendaoc/", "/new-client/save/", "/new-client/godot/tests/",
                   "/new-client/godot/.godot/editor/", "/runtime/win-x64/", "/runtime/server/state-165/")
FORBIDDEN_SUFFIXES = (".log", ".dmp", ".bak", ".tmp", ".db-wal", ".db-shm", "-wal", "-shm")
# Tables that must be empty in the shipped world (saved progress); database/make_clean_db.py empties them all.
SAVE_TABLES = ("Account", "DOLCharacters", "Inventory", "AccountXMoney", "offline_world_bots", "offline_hired_companions")


def sha(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def collect(game: Path, client: Path, repo: Path, snapshot: Path) -> dict[str, Path]:
    """Every file of the install, as <path in the game folder> -> <file on disk>. The player docs are copied to
    snapshot first, so an edit to the repository while the release packs can't make the baseline and the parts differ."""
    files: dict[str, Path] = {}
    for path in game.rglob("*"):
        if path.is_file():
            rel = path.relative_to(game).as_posix()
            if rel.startswith("new-client/"):
                raise SystemExit(f"--game must not hold new-client ({rel}); the client comes from --client")
            files[rel] = path
    if not (client / "new-client").is_dir():
        raise SystemExit("--client must hold a new-client folder")
    for path in client.iterdir():
        if path.name != "new-client" and not path.name.endswith("stage-report.json"):
            raise SystemExit(f"--client holds something besides new-client: {path.name}")
    for path in (client / "new-client").rglob("*"):
        if path.is_file():
            files[path.relative_to(client).as_posix()] = path
    for rel in PLAYER_DOCS:
        source = repo / rel
        if not source.is_file():
            raise SystemExit(f"the repository has no {rel}")
        if rel in files:
            raise SystemExit(f"--game already has {rel}; the player docs come from the repository")
        frozen = snapshot / rel
        frozen.parent.mkdir(parents=True, exist_ok=True)
        frozen.write_bytes(source.read_bytes())
        files[rel] = frozen
    return files


def check_package(files: dict[str, Path]) -> dict[str, Path]:
    for rel in REQUIRED:
        if rel not in files:
            raise SystemExit(f"the package has no {rel}")
    problems = []
    for rel, path in sorted(files.items()):
        low = "/" + rel.lower()
        name = path.name.lower()
        if (name in FORBIDDEN_NAMES or any(p in low for p in FORBIDDEN_PARTS) or name.endswith(FORBIDDEN_SUFFIXES)
                or ".before-" in name or "true165-test-characters" in name or name.startswith("ruleset")):
            problems.append(rel)
    if problems:
        raise SystemExit("must not ship:\n  " + "\n  ".join(problems[:50]))
    world = files["runtime/data/opendaoc-165.sqlite3.db"]
    with sqlite3.connect(f"file:{world.as_posix()}?mode=ro", uri=True) as db:
        for table in SAVE_TABLES:
            rows = db.execute(f'SELECT COUNT(*) FROM "{table}"').fetchone()[0]
            if rows:
                raise SystemExit(f"the world is not clean: {table} has {rows} row(s); use database/make_clean_db.py")
        prop = dict(db.execute("SELECT `Key`, Value FROM ServerProperty WHERE `Key` IN "
                               "('allow_auto_account_creation','classic165_mode','xp_rate')").fetchall())
        if prop.get("allow_auto_account_creation") != "True" or prop.get("classic165_mode") != "True":
            raise SystemExit(f"the world's release settings are wrong: {prop}")
        if db.execute("PRAGMA quick_check").fetchone()[0] != "ok":
            raise SystemExit("the world database fails quick_check")
    config = files["runtime/server/config/serverconfig-165.xml"].read_text(encoding="utf-8", errors="replace")
    if "<AutoAccountCreation>True</AutoAccountCreation>" not in config.replace(" ", ""):
        raise SystemExit("serverconfig-165.xml must have AutoAccountCreation True")
    return files


def pack(files: dict[str, Path], root: str, folder: Path, prefix: str, numbered: bool, budget: int) -> list[dict]:
    groups, current, size = [], [], 0
    for rel in sorted(files):
        length = files[rel].stat().st_size
        if current and size + length > budget:
            groups.append(current)
            current, size = [], 0
        current.append(rel)
        size += length
    if current:
        groups.append(current)
    parts = []
    for number, group in enumerate(groups, start=1):
        name = f"{prefix}-part{number:02d}.zip" if numbered or len(groups) > 1 else f"{prefix}.zip"
        archive = folder / name
        print(f"  {name}: {len(group):,} files", flush=True)
        expected = {}
        with zipfile.ZipFile(archive, "w", allowZip64=True) as zf:
            for rel in group:
                method = zipfile.ZIP_STORED if files[rel].suffix.lower() in STORED else zipfile.ZIP_DEFLATED
                zf.write(files[rel], f"{root}/{rel}", compress_type=method, compresslevel=6)
                expected[f"{root}/{rel}"] = sha(files[rel])
        with zipfile.ZipFile(archive) as zf:
            for item in zf.infolist():
                digest = hashlib.sha256()
                with zf.open(item) as stream:
                    for block in iter(lambda: stream.read(1 << 20), b""):
                        digest.update(block)
                if expected.pop(item.filename, None) != digest.hexdigest():
                    raise SystemExit(f"{name}: {item.filename} does not match what was packed")
        if expected:
            raise SystemExit(f"{name} is missing {len(expected)} file(s)")
        if archive.stat().st_size >= 2_000_000_000:
            raise SystemExit(f"{name} is too large for GitHub")
        parts.append({"Name": name, "Bytes": archive.stat().st_size, "SHA256": sha(archive).upper(),
                      "UnpackedBytes": sum(files[r].stat().st_size for r in group), "Files": len(group)})
    return parts


def old_groups(old: Path, kind: str, files: dict[str, Path], baseline: dict[str, str]):
    """The previous release's grouping of these files: [(part entry, [rel...], unchanged)], and the files that are new
    since then (they go into new parts of their own, so the previous parts can stay byte-identical)."""
    manifest = json.loads((old / "download-manifest.json").read_text(encoding="utf-8"))
    prefix = manifest["RootFolder"] + "/"
    groups, placed = [], set()
    for part in (p for p in manifest["Parts"] if p["Kind"] == kind):
        with zipfile.ZipFile(old / part["Name"]) as zf:
            members = [n[len(prefix):] for n in zf.namelist() if not n.endswith("/")]
        kept = [r for r in members if r in files]
        same = len(kept) == len(members) and all(sha(files[r]) == baseline.get(r) for r in kept)
        groups.append([part, kept, same])
        placed.update(kept)
    return manifest["RootFolder"], groups, sorted(set(files) - placed)


def pack_like(files: dict[str, Path], old: Path, kind: str, baseline: dict[str, str], folder: Path,
              budget: int) -> tuple[list[dict], int]:
    """Pack like the previous release: a part whose files are all unchanged is the previous zip itself (same bytes,
    so it needn't be uploaded again); a changed part is repacked under its old name; files new since then go into new
    parts numbered after the last one, within the part budget."""
    root, groups, added = old_groups(old, kind, files, baseline)
    parts, reused = [], 0
    if added:
        last = groups[-1][0]["Name"]
        match = re.match(r"(.*)-part(\d+)\.zip$", last)
        stem, number = (match.group(1), int(match.group(2))) if match else (last[:-len(".zip")], 1)
        new_groups, current, size = [], [], 0
        for rel in added:
            length = files[rel].stat().st_size
            if current and size + length > budget:
                new_groups.append(current)
                current, size = [], 0
            current.append(rel)
            size += length
        new_groups.append(current)
        for offset, members in enumerate(new_groups, start=1):
            groups.append([{"Name": f"{stem}-part{number + offset:02d}.zip"}, members, False])
    for part, members, same in groups:
        if same:
            target = folder / part["Name"]
            target.write_bytes((old / part["Name"]).read_bytes())
            if sha(target).upper() != part["SHA256"]:
                raise SystemExit(f"{part['Name']}: the previous release's file is damaged")
            parts.append({k: part[k] for k in ("Name", "Bytes", "SHA256", "UnpackedBytes", "Files")})
            reused += 1
            print(f"  {part['Name']}: unchanged (the uploaded file is reused)", flush=True)
            continue
        name = part["Name"]
        # one group, packed under the previous name (pack() names a single group <prefix>.zip)
        new = pack({r: files[r] for r in members}, root, folder, name[:-len(".zip")], False, 1 << 62)
        assert new[0]["Name"] == name
        parts.append(new[0])
        print(f"  {name}: CHANGED, repacked", flush=True)
    return parts, reused


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--like", type=Path, help="a previous release folder (<assets>\\v<version>) to reuse unchanged "
                        "parts from, with --like-baseline; the parts keep its names and root folder")
    parser.add_argument("--like-baseline", type=Path)
    parser.add_argument("--game", type=Path, required=True)
    parser.add_argument("--client", type=Path, required=True)
    parser.add_argument("--repo", type=Path, required=True, help="this repository (for the player docs)")
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--version", default="0.1")
    parser.add_argument("--part-budget", type=int, default=PART_BUDGET, help=argparse.SUPPRESS)  # tests only
    args = parser.parse_args()
    out, version = args.out.resolve(), args.version
    release = out / f"v{version}"
    if release.exists():
        raise SystemExit(f"{release} exists; refusing to overwrite release assets")

    print("Checking the package...", flush=True)
    out.mkdir(parents=True, exist_ok=True)
    snapshot = out / f"player-docs-{version}"
    if snapshot.exists():
        raise SystemExit(f"{snapshot} exists; remove it first")
    files = check_package(collect(args.game.resolve(), args.client.resolve(), args.repo.resolve(), snapshot))
    print(f"  {len(files):,} files, {sum(p.stat().st_size for p in files.values()) / 1e9:.2f} GB", flush=True)

    print("Recording the baseline (SHA-256 of every file)...", flush=True)
    out.mkdir(parents=True, exist_ok=True)
    baseline = out / f"baseline-{version}.sha256"
    with baseline.open("w", encoding="utf-8", newline="\n") as handle:
        for rel in sorted(files):
            handle.write(f"{sha(files[rel])}  {rel}\n")

    release.mkdir(parents=True)
    root = f"OfflineDAoC-True165-v{version}"
    game = {r: p for r, p in files.items() if not r.startswith("new-client/")}
    client = {r: p for r, p in files.items() if r.startswith("new-client/")}
    if args.like:
        old = args.like.resolve()
        old_baseline = {}
        for line in args.like_baseline.read_text(encoding="utf-8").splitlines():
            digest, rel = line.split("  ", 1)
            old_baseline[rel] = digest
        root = json.loads((old / "download-manifest.json").read_text(encoding="utf-8"))["RootFolder"]
        print("Packing the game like the previous release...", flush=True)
        game_parts, g_reused = pack_like(game, old, "game", old_baseline, release, args.part_budget)
        print("Packing the new client like the previous release...", flush=True)
        client_parts, c_reused = pack_like(client, old, "client", old_baseline, release, args.part_budget)
        print(f"  {g_reused + c_reused} of {len(game_parts) + len(client_parts)} parts unchanged", flush=True)
    else:
        print("Packing the game...", flush=True)
        game_parts = pack(game, root, release, f"{root}-game", False, args.part_budget)
        print("Packing the new client...", flush=True)
        client_parts = pack(client, root, release, f"{root}-client", True, args.part_budget)

    manifest = {
        "Product": "Offline DAoC True 1.65",
        "Version": version,
        "RootFolder": root,
        "Parts": [dict(p, Kind="game") for p in game_parts] + [dict(p, Kind="client") for p in client_parts],
        "InstalledBytes": sum(p["UnpackedBytes"] for p in game_parts + client_parts),
        "InstalledFiles": len(files),
        "Required": ["START OFFLINE DAOC TRUE 1.65.cmd", "runtime/OfflineDAoC.exe", "new-client/godot/project.godot",
                     "new-client/VERSION.txt"],
        "BaselineSHA256": sha(baseline).upper(),
    }
    (release / "download-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    lines = [f"{sha(p)}  {p.name}" for p in sorted(release.iterdir()) if p.is_file() and p.name != "SHA256SUMS.txt"]
    (release / "SHA256SUMS.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    total = sum(p["Bytes"] for p in game_parts + client_parts)
    print(json.dumps({"version": version, "files": len(files), "installed_gb": round(manifest["InstalledBytes"] / 1e9, 2),
                      "download_gb": round(total / 1e9, 2), "game_parts": len(game_parts),
                      "client_parts": len(client_parts), "baseline": str(baseline)}, indent=2), flush=True)


if __name__ == "__main__":
    main()
