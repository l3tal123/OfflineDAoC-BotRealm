"""Build the update patch for a new BotRealm version, and add it to that version's download manifest.

    python -I make_patch_true165.py --base 0.1=<baseline-0.1.sha256> [--base 0.2=...]
                                    --game <new game folder> --repo <repository>
                                    --out <assets folder> --version 0.2

Run it AFTER make_release_true165.py has made the new version's full download in <assets folder>\\v<version>\\ (new
players still get the full download). This adds:
    BotRealm-v<version>-patch[-partNN].zip   only the files that changed since any released version
and a "Patch" section in download-manifest.json, then refreshes SHA256SUMS.txt. Each --base is the
baseline-<version>.sha256 that make_release_true165.py wrote beside an earlier release's assets.

The patch is cumulative: it brings ANY released version (every --base) up to the new one, so a player who skipped a
version still needs one download. For every file it carries, the manifest lists the SHA-256 that file had in each
released version ("From"). The downloader's update mode only replaces a file whose current SHA-256 is one of those
(or already the new one); any other file was changed on that PC, and the update stops before touching anything.

The game client is not part of a release and is never in the patch: new-client/ belongs to the player, who supplies the
client themselves, so an update leaves it exactly as it is.

Never patched (the player's own data): NEVER_TOUCH. The world database there is the player's save; world content
changes ship as runtime/botrealm JSON instead, which the launcher applies at the next START SERVER, keeping every save.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import make_release_true165 as mr  # noqa: E402

# Paths a patch never writes, replaces or removes (lower case, relative to the game folder).
NEVER_TOUCH = ("new-client/", "runtime/data/", "runtime/account.txt", "runtime/logs/", "runtime/server/logs/",
               "runtime/server/state-165/", "patch-backups/", ".install/", ".patch/")


def never_touch(rel: str) -> bool:
    low = rel.lower()
    return any(low == p.rstrip("/") or low.startswith(p) for p in NEVER_TOUCH)


def read_baseline(path: Path) -> dict[str, str]:
    files = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.strip():
            digest, rel = line.split("  ", 1)
            files[rel] = digest.lower()
    return files


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--base", action="append", required=True, help="<version>=<baseline .sha256>, once per release")
    parser.add_argument("--game", type=Path, required=True)
    parser.add_argument("--repo", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--part-budget", type=int, default=mr.PART_BUDGET, help=argparse.SUPPRESS)  # tests only
    args = parser.parse_args()
    out, version = args.out.resolve(), args.version
    release = out / f"v{version}"
    manifest_file = release / "download-manifest.json"
    if not manifest_file.is_file():
        raise SystemExit(f"{manifest_file} is missing: run make_release_true165.py for v{version} first")
    manifest = json.loads(manifest_file.read_text(encoding="utf-8"))
    if manifest.get("Version") != version or "Patch" in manifest:
        raise SystemExit("the manifest is for another version, or already has a patch")

    bases: dict[str, dict[str, str]] = {}
    for item in args.base:
        label, _, path = item.partition("=")
        if not label or not path:
            raise SystemExit(f"--base must be <version>=<file>: {item}")
        bases[label] = read_baseline(Path(path))
    if version in bases:
        raise SystemExit("--base must list earlier versions only")

    print("Checking the new version's files...", flush=True)
    snapshot = out / f"player-docs-{version}-patch"
    if snapshot.exists():
        raise SystemExit(f"{snapshot} exists; remove it first")
    files = mr.check_package(mr.collect(args.game.resolve(), args.repo.resolve(), snapshot))
    new_baseline = read_baseline(out / f"baseline-{version}.sha256")
    new = {r: p for r, p in files.items() if not never_touch(r)}
    for rel, path in new.items():   # the full download and the patch must be the same files
        if mr.sha(path) != new_baseline.get(rel):
            raise SystemExit(f"{rel} differs from the v{version} baseline: rebuild both from the same folders")

    changed, entries = {}, []
    for rel, path in sorted(new.items()):
        target = new_baseline[rel]
        olds = sorted({b[rel] for b in bases.values() if rel in b and b[rel] != target})
        missing_somewhere = any(rel not in b for b in bases.values())
        if not olds and not missing_somewhere:
            continue   # the same in every released version
        changed[rel] = path
        entries.append({"Path": rel, "Sha256": target, "Bytes": path.stat().st_size, "From": olds})
    removed = []
    for rel in sorted({r for b in bases.values() for r in b} - set(new)):
        if never_touch(rel):
            continue
        removed.append({"Path": rel, "From": sorted({b[rel] for b in bases.values() if rel in b})})

    root = f"BotRealm-v{version}-patch"
    print(f"Packing the patch: {len(entries):,} changed or new file(s), {len(removed):,} removed...", flush=True)
    parts = mr.pack(changed, root, release, root, False, args.part_budget) if changed else []
    manifest["Patch"] = {
        "Version": version,
        "From": sorted(bases),
        "RootFolder": root,
        "Parts": parts,
        "Files": entries,
        "Remove": removed,
        "NeverTouch": list(NEVER_TOUCH),
        "Bytes": sum(p["Bytes"] for p in parts),
        "UnpackedBytes": sum(e["Bytes"] for e in entries),
    }
    manifest_file.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    lines = [f"{mr.sha(p)}  {p.name}" for p in sorted(release.iterdir()) if p.is_file() and p.name != "SHA256SUMS.txt"]
    (release / "SHA256SUMS.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(json.dumps({"version": version, "from": sorted(bases), "files": len(entries), "removed": len(removed),
                      "patch_mb": round(manifest["Patch"]["Bytes"] / 1e6, 1), "parts": len(parts)}, indent=2))


if __name__ == "__main__":
    main()
