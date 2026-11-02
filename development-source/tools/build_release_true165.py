"""Builds the OfflineDAoC True 1.65 release package (server + launcher + bundled .NET) from a working install.

    python build_release_true165.py --install <install root, the folder holding runtime\\>
                                    --repo <OfflineDAoC-True165 clone> --out <new staging folder>

Package layout:
    START OFFLINE DAOC TRUE 1.65.cmd   sets DOTNET_ROOT to the bundled .NET, starts runtime\\OfflineDAoC.exe
    runtime\\                          launcher, server (with the full navmesh and navmesh-165), True 1.65 content, the
                                      clean world database
    tools\\dotnet\\                     .NET 10 (host fxr + Microsoft.NETCore.App, WindowsDesktop.App, AspNetCore.App)
    new-client\\                       NOT made here: the new client's own package unzips beside runtime\\ (the launcher
                                      finds ..\\new-client by itself)

The install is only read, never changed, and the server must be stopped (its world database is copied). What differs
from the install on purpose:
  * the world is the clean one (database/make_clean_db.py from the repo): no accounts, characters, bots, companions,
    Exchange, guild or house progress; keeps, doors and relics at their defaults; auto account creation on, XP 1x, GM
    off, population target 0. The content-tracking tables stay, so the first START SERVER finds the content current;
  * no account.txt (the launcher makes one per install at the first ENTER REALM), no test characters, no logs,
    backups, "before" copies, diagnostics switches, DXVK, the original client, stale self-contained publishes or run
    state;
  * AutoAccountCreation True in the server config.
Everything else is copied byte for byte; the report (<out>-build-report.json) lists every file's SHA-256.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import zipfile
from datetime import datetime, timezone
from pathlib import Path

DOTNET_VERSION = "10.0.11"
DOTNET_PARTS = (f"host/fxr/{DOTNET_VERSION}", f"shared/Microsoft.NETCore.App/{DOTNET_VERSION}",
                f"shared/Microsoft.WindowsDesktop.App/{DOTNET_VERSION}", f"shared/Microsoft.AspNetCore.App/{DOTNET_VERSION}")
DB_NAME = "opendaoc-165.sqlite3.db"
START_CMD = "START OFFLINE DAOC TRUE 1.65.cmd"

# Folders that never ship (anywhere in runtime\), and top-level runtime folders that don't.
EXCLUDED_DIRS = {"logs", "backups", "deployment-backups", "mode-switch-backups", "keep-relic-reset-backups",
                 "true165-devtool-build", "progress-backups", "dxvk-staging", "screenshots", "dumps", ".git", "testresults",
                 "state-165"}
EXCLUDED_TOP = {"client-opendaoc", "win-x64", "data"}          # original client, stale publish, the world (rebuilt)
EXCLUDED_SERVER = {"win-x64"}                                   # runtime\server\win-x64: a copy nothing starts
EXCLUDED_NAMES = {"account.txt", "ruleset.txt", "bot-world.request", "bot-goals.json", "bot-ai-delay.json", "rvr-world.json",
                  "bot-world.json", "realm-events.request.json", "realm-events.result.json", "errorlog.txt", "debug.log",
                  "chat.log", "user.dat", "d3d9.dll", "dxvk.conf", "dev_mode.txt", "memory-census.on", "gc-snapshots.on",
                  "gc-diagnostics.off", "claude_test_account.txt"}
EXCLUDED_SUFFIXES = (".log", ".dmp", ".gcdump", ".bak", ".tmp", ".db", ".db-wal", ".db-shm", ".db-journal",
                     ".sqlite3", ".sqlite3-wal", ".sqlite3-shm")
FORBIDDEN_SUFFIXES = (".on", ".off", ".dmp", ".gcdump", ".log")
REQUIRED = ("OfflineDAoC.exe", "OfflineDAoC.dll", "server/CoreServer.exe", "server/GameServer.dll", "server/lib/GameServer.dll",
            "server/config/serverconfig-165.xml", "server/navmesh/roads.json", "server/navmesh/seams.json",
            "server/navmesh-165/zone009.nav", "true165/true165-rules.json", "true165/progress-policy.json",
            f"data/{DB_NAME}", "data/world-mode.txt")


def sha(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def include(relative: Path) -> bool:
    parts = [p.lower() for p in relative.parts]
    if parts[0] in EXCLUDED_TOP or (len(parts) > 2 and parts[0] == "server" and parts[1] in EXCLUDED_SERVER):
        return False
    if any(p in EXCLUDED_DIRS or "before" in p for p in parts[:-1]):
        return False
    name = parts[-1]
    return (name not in EXCLUDED_NAMES and "before" not in name and not name.startswith("true165-test-characters")
            and not name.endswith(EXCLUDED_SUFFIXES))


def copy_tree(source: Path, target: Path, keep=lambda rel: True) -> dict[str, str]:
    copied = {}
    for path in sorted(source.rglob("*")):
        if not path.is_file():
            continue
        relative = path.relative_to(source)
        if not keep(relative):
            continue
        destination = target / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, destination)
        copied[relative.as_posix()] = sha(destination)
    return copied


def server_running() -> bool:
    if sys.platform != "win32":
        return False
    return "coreserver.exe" in subprocess.run(["tasklist"], capture_output=True, text=True).stdout.lower()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--install", type=Path, required=True)
    parser.add_argument("--repo", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--version", help='writes VERSION.txt = "Offline DAoC True 1.65 v<version>" (the downloader reads it)')
    args = parser.parse_args()
    install, repo, out = args.install.resolve(), args.repo.resolve(), args.out.resolve()
    if out.exists():
        raise SystemExit(f"{out} exists; refusing to overwrite a staging folder")
    if server_running():
        raise SystemExit("The server (CoreServer.exe) is running. Stop it first: its world database is copied.")
    live = install / "runtime"
    runtime = out / "runtime"

    print("Copying the runtime (personal, diagnostic and run-state files left out)...", flush=True)
    copied = copy_tree(live, runtime, include)

    print("Making the clean world database...", flush=True)
    zipped = out.parent / f"{out.name}-world.zip"
    subprocess.run([sys.executable, "-I", str(repo / "database" / "make_clean_db.py"), str(live / "data" / DB_NAME),
                    "--out", str(zipped)], check=True)
    (runtime / "data").mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(zipped) as z:
        z.extract(DB_NAME, runtime / "data")
    zipped.unlink()
    shutil.copy2(live / "data" / "world-mode.txt", runtime / "data" / "world-mode.txt")

    for name in ("serverconfig-165.xml", "serverconfig.xml"):
        config = runtime / "server" / "config" / name
        if config.exists():
            text = config.read_bytes().replace(b"<AutoAccountCreation>False</AutoAccountCreation>",
                                               b"<AutoAccountCreation>True</AutoAccountCreation>")
            config.write_bytes(text)
            assert b"<AutoAccountCreation>True</AutoAccountCreation>" in text, name

    print("Bundling .NET", DOTNET_VERSION, "...", flush=True)
    dotnet = Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "dotnet"
    bundled = out / "tools" / "dotnet"
    for part in DOTNET_PARTS:
        if not (dotnet / part).is_dir():
            raise SystemExit(f"{dotnet / part} is missing: install the .NET {DOTNET_VERSION} runtimes (or change DOTNET_VERSION)")
        copy_tree(dotnet / part, bundled / part)
    for name in ("dotnet.exe", "LICENSE.txt", "ThirdPartyNotices.txt"):
        if (dotnet / name).exists():
            shutil.copy2(dotnet / name, bundled / name)

    (out / START_CMD).write_bytes((
        "@echo off\r\n"
        "setlocal\r\n"
        "cd /d \"%~dp0\"\r\n"
        "rem OfflineDAoC True 1.65: the launcher, with the .NET 10 bundled in tools\\dotnet (no install needed).\r\n"
        "rem The new client goes in new-client\\ beside runtime\\; the launcher finds it there.\r\n"
        "if not exist \"%~dp0runtime\\OfflineDAoC.exe\" (\r\n"
        "  echo runtime\\OfflineDAoC.exe is missing: unzip the whole OfflineDAoC True 1.65 package here.\r\n"
        "  pause\r\n"
        "  exit /b 1\r\n"
        ")\r\n"
        "set \"DOTNET_ROOT=%~dp0tools\\dotnet\"\r\n"
        "set \"DOTNET_ROOT_X64=%~dp0tools\\dotnet\"\r\n"
        "set \"DOTNET_MULTILEVEL_LOOKUP=0\"\r\n"
        "start \"\" /d \"%~dp0runtime\" \"%~dp0runtime\\OfflineDAoC.exe\"\r\n"
        "exit /b 0\r\n").encode("ascii"))

    if args.version:
        (out / "VERSION.txt").write_bytes(f"Offline DAoC True 1.65 v{args.version}".encode("ascii"))

    print("Checking the package...", flush=True)
    bad = [p.relative_to(out).as_posix() for p in out.rglob("*") if p.is_file() and (
        p.name.lower() in EXCLUDED_NAMES or p.name.lower().endswith(FORBIDDEN_SUFFIXES) or "before" in p.name.lower()
        or p.name.lower().startswith("true165-test-characters"))]
    assert not bad, f"files that must not ship: {bad[:20]}"
    missing = [name for name in REQUIRED if not (runtime / name).is_file()]
    assert not missing, f"required files missing: {missing}"
    for part in DOTNET_PARTS:
        assert (bundled / part).is_dir(), part
    assert not (runtime / "client-opendaoc").exists() and not (runtime / "win-x64").exists()
    navmesh = sum(p.stat().st_size for p in (runtime / "server" / "navmesh").rglob("*") if p.is_file())

    report = {
        "built_utc": datetime.now(timezone.utc).isoformat(),
        "install": str(install),
        "runtime_files_copied": len(copied),
        "navmesh_bytes": navmesh,
        "world_database_sha256": sha(runtime / "data" / DB_NAME),
        "dotnet": DOTNET_VERSION,
        "package_bytes": sum(p.stat().st_size for p in out.rglob("*") if p.is_file()),
        "copied_hashes": copied,
    }
    (out.parent / f"{out.name}-build-report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({k: v for k, v in report.items() if k != "copied_hashes"}, indent=2), flush=True)


if __name__ == "__main__":
    main()
