"""End-to-end test of the BotRealm update path on a small fake game (no network, nothing outside --work).

    python -I test_patch_true165.py --work <empty scratch folder> --world <a clean opendaoc-165.sqlite3.db>
                                    --repo <this repository>

It builds a fake v0.1 and v0.2 (changed, added and removed game files), installs v0.1 with the downloader, adds a
player's save (world, login) and a client the player supplied in new-client\, and checks the update: a changed file
stops it (and the fresh-install-with-save path works), a failure halfway puts everything back, a good run gives exactly
v0.2 with the save and the client untouched and a backup of what it replaced, a second run says "already up to date",
and a half-applied update finishes on the next run.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
PS1 = HERE / "Get-BotRealm.ps1"
SAVE_FILES = ["runtime/data/opendaoc-165.sqlite3.db", "runtime/account.txt", "new-client/save/settings.cfg"]
# Files of the client the player supplied; no update may add, replace or remove any of them.
CLIENT_FILES = ["new-client/VERSION.txt", "new-client/content/asset.bin", "new-client/save/settings.cfg"]


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def tree(root: Path, skip=("patch-backups/", ".patch/", "UPDATE-CHANGED-FILES.txt")) -> dict[str, str]:
    out = {}
    for path in root.rglob("*"):
        if path.is_file():
            rel = path.relative_to(root).as_posix()
            if not rel.startswith(skip):
                out[rel] = sha(path)
    return out


def run(cmd, env=None, expect=0):
    r = subprocess.run(cmd, capture_output=True, text=True, env=env, stdin=subprocess.DEVNULL)
    if r.returncode != expect:
        print(r.stdout[-3000:], r.stderr[-2000:])
        raise SystemExit(f"exit {r.returncode} (expected {expect}): {' '.join(map(str, cmd))[:200]}")
    return r.stdout


def downloader(version, assets, *args, env=None, expect=0):
    return run(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(PS1), "-ReleaseVersion", version,
                "-LocalAssets", str(assets), "-Yes", "-NoShortcut", *map(str, args)], env=env, expect=expect)


def make_game(root: Path, world: Path, version: str):
    for rel, text in {
        "START BOTREALM.cmd": "@echo off\r\n",
        "runtime/BotRealm.exe": "launcher", "runtime/server/CoreServer.exe": "server",
        "runtime/server/config/serverconfig-165.xml": "<AutoAccountCreation>True</AutoAccountCreation>",
        "runtime/botrealm/true165-rules.json": "{}", "runtime/botrealm/drops165.json": '{"drops": 1}',
        "runtime/botrealm/old165.json": "removed in 0.2", "tools/dotnet/dotnet.exe": "dotnet",
    }.items():
        (root / rel).parent.mkdir(parents=True, exist_ok=True)
        (root / rel).write_text(text, encoding="utf-8")
    (root / "runtime/data").mkdir(parents=True, exist_ok=True)
    shutil.copyfile(world, root / "runtime/data/opendaoc-165.sqlite3.db")


def make_client(game: Path, version: str):
    """The client the player supplies: not downloaded, just placed in new-client\\ beside runtime\\. It also holds the
    player's client settings under save\\, which no update may touch."""
    c = game / "new-client"
    for rel, data in {"VERSION.txt": f"BotRealm v{version} client\r\n".encode(),
                      "client.bin": b"x", "content/.gdignore": b"",
                      "content/asset.bin": os.urandom(1_000_000),
                      "tools/engine.exe": b"engine"}.items():
        (c / rel).parent.mkdir(parents=True, exist_ok=True)
        (c / rel).write_bytes(data)
    (c / "save").mkdir(parents=True, exist_ok=True)
    (c / "save/settings.cfg").write_text("player settings", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--work", type=Path, required=True)
    parser.add_argument("--world", type=Path, required=True)
    parser.add_argument("--repo", type=Path, required=True)
    args = parser.parse_args()
    w = args.work.resolve()
    if w.exists() and any(w.iterdir()):
        raise SystemExit(f"{w} must be empty")
    w.mkdir(parents=True, exist_ok=True)
    py = [sys.executable, "-I"]

    # ---- v0.1 and v0.2
    make_game(w / "game1", args.world, "0.1")
    shutil.copytree(w / "game1", w / "game2")
    (w / "game2/runtime/botrealm/drops165.json").write_text('{"drops": 2}', encoding="utf-8")       # changed
    (w / "game2/runtime/botrealm/new165.json").write_text("added in 0.2", encoding="utf-8")         # added
    (w / "game2/runtime/botrealm/old165.json").unlink()                                             # removed
    (w / "game2/VERSION.txt").write_text("BotRealm v0.2\r\n", encoding="utf-8")                      # added
    for v, n in (("0.1", "1"), ("0.2", "2")):
        run(py + [str(HERE / "make_release_true165.py"), "--game", w / f"game{n}",
                  "--repo", args.repo, "--out", w / "assets", "--version", v, "--part-budget", "4000000"])
    run(py + [str(HERE / "make_patch_true165.py"), "--base", f"0.1={w / 'assets/baseline-0.1.sha256'}",
              "--game", w / "game2", "--repo", args.repo, "--out", w / "assets",
              "--version", "0.2", "--part-budget", "4000000"])
    print("built v0.1, v0.2 and the 0.1 -> 0.2 patch")

    def install(name):
        target = w / name
        downloader("0.1", w / "assets", "-Mode", "install", "-Destination", target)
        (target / "runtime/account.txt").write_text("Account: player123456\nPassword: x\n", encoding="utf-8")
        make_client(target, "0.1")   # the player supplies the client; the downloader never fetched it
        with open(target / "runtime/data/opendaoc-165.sqlite3.db", "ab") as db:
            db.write(b"played")   # stands in for a played world
        return target

    v02 = {}
    for line in (w / "assets/baseline-0.2.sha256").read_text(encoding="utf-8").splitlines():
        h, rel = line.split("  ", 1)
        v02[rel] = h
    assert not any(rel.startswith("new-client/") for rel in v02), "the release must not ship a client"
    patch = json.loads((w / "assets/v0.2/download-manifest.json").read_text(encoding="utf-8"))
    assert all(p["Kind"] == "game" for p in patch["Parts"]), "the release must have no client parts"
    assert not any(f["Path"].startswith("new-client/") for f in patch["Patch"]["Files"]), "the patch must not carry the client"
    assert not any(r["Path"].startswith("new-client/") for r in patch["Patch"]["Remove"]), "the patch must not remove the client"

    def check_is_v02(game: Path, save: dict):
        have = tree(game)
        for rel, h in v02.items():
            if rel in SAVE_FILES or rel.startswith("runtime/data/"):
                continue
            assert have.get(rel) == h, f"{rel} is not the v0.2 file"
        assert "runtime/botrealm/old165.json" not in have, "the removed file is still there"
        for rel in SAVE_FILES:
            assert have.get(rel) == save[rel], f"the save file {rel} was changed"
        for rel in CLIENT_FILES:
            assert have.get(rel) == save[rel], f"the player's client file {rel} was changed"

    # ---- 1. a file changed on this PC: nothing is touched; the fresh install brings the save over
    g = install("g1")
    (g / "runtime/botrealm/drops165.json").write_text("my own edit", encoding="utf-8")
    before = tree(g)
    out = downloader("0.2", w / "assets", "-Mode", "update", "-Update", g)
    assert "were changed on this PC" in out, out[-800:]
    after = tree(g)
    assert before == {k: v for k, v in after.items() if k != "UPDATE-CHANGED-FILES.txt"} or before == after, "the old folder was changed"
    fresh = Path(str(g) + "-v0.2")
    assert (fresh / "VERSION.txt").is_file(), "no fresh install"
    for rel in SAVE_FILES:
        assert sha(fresh / rel) == before[rel], f"{rel} was not brought over"
    print("1 ok: a changed file stops the update; the fresh install has the save")

    # ---- 2. a failure halfway puts everything back
    g = install("g2")
    before = tree(g)
    env = dict(os.environ, BOTREALM_TEST_FAIL_AFTER="2")
    out = downloader("0.2", w / "assets", "-Mode", "update", "-Update", g, env=env, expect=1)
    assert "back as it was" in out, out[-800:]
    assert tree(g) == before, "the failed update was not undone"
    assert not (g / "patch-backups").exists() and not (g / ".patch").exists(), "the failed update left files behind"
    print("2 ok: a failure halfway is undone")

    # ---- 3. a good update, and 4. a second run
    save = {rel: before[rel] for rel in SAVE_FILES + CLIENT_FILES}
    out = downloader("0.2", w / "assets", "-Mode", "update", "-Update", g)
    assert "updated to" in out, out[-800:]
    check_is_v02(g, save)
    backups = list((g / "patch-backups").iterdir())
    assert len(backups) == 1 and (backups[0] / "runtime/botrealm/drops165.json").read_text() == '{"drops": 1}'
    assert (backups[0] / "runtime/botrealm/old165.json").is_file(), "the removed file was not backed up"
    print("3 ok: the update gives exactly v0.2, the save is untouched, the replaced files are backed up")
    out = downloader("0.2", w / "assets", "-Mode", "update", "-Update", g)
    assert "already up to date" in out, out[-800:]
    print("4 ok: a second run says it's up to date")

    # ---- 5. a half-applied update (a crash after some files) finishes on the next run
    g = install("g5")
    save = {rel: sha(g / rel) for rel in SAVE_FILES + CLIENT_FILES}
    shutil.copyfile(w / "game2/runtime/botrealm/drops165.json", g / "runtime/botrealm/drops165.json")
    out = downloader("0.2", w / "assets", "-Mode", "update", "-Update", g)
    assert "updated to" in out, out[-800:]
    check_is_v02(g, save)
    print("5 ok: a half-applied update finishes")
    print("ALL PATCH TESTS PASSED")


if __name__ == "__main__":
    main()
