# Fork developer environment (verified 2026-11-01)

Verification of a fresh checkout at `C:\Users\andro\Desktop\OfflineDaoc-BotRealm` (the downloaded ZIP export, now
attached to `origin/main`; fork of `shadowofze/OfflineDAoC-True165`, origin `l3tal123/OfflineDAoC-BotRealm`). Nothing
was deployed and no server or launcher was started. No game server was running before or after
(`Get-Process -Name CoreServer,OfflineDAoC` empty).

## Summary

| Component | Status | Version / notes |
|---|---|---|
| .NET SDK | OK | `10.0.100` at `C:\Program Files\dotnet`. Docs say dev builds use `10.0.11`; not required here. |
| .NET runtimes | OK | `Microsoft.NETCore.App 10.0.0`, `Microsoft.WindowsDesktop.App 10.0.0`, `Microsoft.AspNetCore.App 10.0.0` (plus 8.0.30). .NET 10 present. |
| Python | OK | `3.11.9` (`C:\Users\andro\AppData\Local\Programs\Python\Python311\python.exe`), meets the 3.10+ need. |
| Godot | **MISSING** | No `godot*` on PATH, none in Program Files/registry. Only `C:\Users\andro\Desktop\Godot 4.8  dev 3.exe` (`4.8.dev3.official`, standard build, no `GodotSharp`/mono). Godot 4.7.2 stable **.NET** build absent. |
| `tools/dotnet` | absent (expected) | Repo ships no .NET binaries; present only in the release download. |
| Database unpack | OK | `runtime/data/opendaoc-165.sqlite3.db`, `quick_check ok`, 133 tables, empty `Account`. |
| Server build | OK with workaround | `Build succeeded`, **619 warnings, 0 errors**, 56.95 s. Documented plain command fails in this sandbox (see Blockers). |
| Tests | OK with workaround | **3006 total, 2937 passed, 0 failed, 69 skipped** (all `Explicit`), 19.6 s. Ran via in-process NUnit engine, not VSTest (see Blockers). |
| Launcher build | OK | Re-verified 2026-11-03 (the web launcher, `net10.0`): **0 warnings, 0 errors**, smoke test passed. |

## Commands run and observed results

### Toolchain
```
dotnet --version            -> 10.0.100
dotnet --list-sdks          -> 10.0.100 [C:\Program Files\dotnet\sdk]
dotnet --list-runtimes      -> Microsoft.AspNetCore.App 10.0.0; Microsoft.NETCore.App 8.0.30, 10.0.0;
                               Microsoft.WindowsDesktop.App 8.0.30, 10.0.0
python --version            -> Python 3.11.9
Get-Command godot*          -> (nothing)
```

### Server (`development-source/server`)
```
dotnet build "Dawn of Light.sln" -c Release
  -> Build FAILED. 0 Warning(s) 0 Error(s)   (exit 1, ~1 s; environment parallel-node limit, see Blockers)

dotnet build "Dawn of Light.sln" -c Release -p:RestoreBuildInParallel=false -p:BuildInParallel=false -m:1
  -> Build succeeded. 619 Warning(s) 0 Error(s), 00:56.95
```
- First run without the flags failed for a second reason: `error MSB3030: Could not copy the file
  "...\CoreServer\config\serverconfig.xml" because it was not found` plus a cascading
  `error MSB4181: The "MSBuild" task returned false but did not log an error` in `Tests.csproj`
  (`Tests` references `CoreServer`). `CoreServer/config/serverconfig.xml` is git-ignored and not in a fresh clone.
  **Fix used:** `Copy-Item CoreServer\config\serverconfig.example.xml CoreServer\config\serverconfig.xml` (still ignored
  by git; no source change).
- Warning mix: 934 `CS0618` (obsolete APIs), 72 `CS0162`, 32 `CS0414`, 26 `CS0169`, 26 `CS9216`, 20 `NU1900`
  (nuget.org unreachable — offline; restore came from the local package cache, all packages present), 4 `MSB3042`, etc.
- Outputs: `Release/CoreServer.exe` (173,568 B); `Release/lib/GameServer.dll` (28,500,480 B),
  `CoreDatabase.dll` (293,888 B), `CoreBase.dll` (71,168 B).

### Tests (`development-source/server`)
```
dotnet test Tests/Tests.csproj -c Release --no-build
  -> Testhost process ... exited with error: Unhandled exception.
     System.ComponentModel.Win32Exception (5): Access is denied.
     at System.Diagnostics.ProcessManager.OpenProcess(...)
     at ...DefaultEngineInvoker.SetParentProcessExitCallback(...)
     Test Run Aborted.
```
VSTest cannot start under this sandbox: process-handle access is denied (even
`[System.Diagnostics.Process]::GetCurrentProcess().EnableRaisingEvents = $true` returns "Access is denied").
Workaround used: the NUnit engine shipped in the test output (`nunit.engine` 3.22,
`ProcessModel=InProcess`) was run from a throw-away runner outside the repo source; the runner and its files were
removed afterwards. Result XML: `result="Passed" total="3006" passed="2937" failed="0" skipped="69"
inconclusive="0" warnings="0" duration="19.62"`. All 69 skips are `Explicit` tool/probe fixtures. This is the real
NUnit suite, but it is **not** the documented `dotnet test` invocation, and it is reported separately for that reason.

### Launcher (`development-source/tools/OfflineDaoc.Dashboard`, re-verified 2026-11-03 after the old WinForms project was removed)
```
dotnet build OfflineDaoc.Dashboard.csproj -c Release -p:RestoreBuildInParallel=false -p:BuildInParallel=false -m:1
  -> Build succeeded, 0 Warning(s), 0 Error(s), 2.3 s
```
Outputs `bin/Release/net10.0/OfflineDAoC.exe` (162,816 B), `OfflineDAoC.dll` (317,440 B) and a `wwwroot/` folder. The
launcher's UI-free logic now lives in its `Launcher/` folder (the old WinForms project is no longer in the repository),
and its csproj still compiles the two server files (`BotGoalSettings.cs`, `RealmEventRecordStore.cs`) by relative path.
Smoke test: run from the build output with `--port 10311 --root <playable>\runtime --no-open`; it reported launcher 0.3
with the install detected, and `/`, `/app.js`, `/styles.css`, the vendored Vue build, `/api/health`, `/api/world` and
`/api/population` all answered 200.

### Database (repo root)
```
python database/unpack_db.py
  -> unpacked runtime\data\opendaoc-165.sqlite3.db   (exit 0, 131,616,768 B)
```
Python `sqlite3` check: `PRAGMA quick_check` = `ok`, `PRAGMA integrity_check` = `ok`, 133 tables,
`ServerProperty` 477 rows, `ItemTemplate` 38,661 rows, `Account` 0 rows, `classic165_mode` = `True`.

### Git state
`git status` before writing this file: `M .gitignore`, `M AGENTS.md`, untracked `.github/`, `docs/BOT-DEVELOPMENT.md`,
`docs/FORK.md`, `scripts/`. These are local fork additions, not produced by this verification. Build outputs, the
unpacked database and `serverconfig.xml` are all git-ignored. Nothing was committed.

## What is missing or needs installing

- **Godot 4.7.2 stable .NET/mono** (`Godot_v4.7.2-stable_mono_win64`): required to open or run the client; only a
  non-.NET 4.8 dev build is on this machine. See `source/new-client/README.md` (setup step 1).
- **Client content** `source/new-client/godot/content` (~37 GB): absent. Required to run the client; comes from the
  release download or the Python pipeline. See `source/new-client/README.md`.
- **Full navigation meshes** `runtime/server/navmesh` (2.1 GB): only `runtime/server/navmesh-165` exists. From the
  release download or `development-source/tools/OpenDAoC-BuildNav`. See `docs/DEVELOPMENT.md` §0 item 3, §7.
- **Server deployables** `runtime/server` has only `config/` and `navmesh-165/` (no `CoreServer.exe`/`lib/`). Deploy
  `Release/` + `Release/lib/` per `docs/DEVELOPMENT.md` §1 (not done here).
- **`CoreServer/config/serverconfig.xml`**: git-ignored, absent from a fresh clone; copy
  `serverconfig.example.xml` or the runtime 1.65 config. Not documented in `docs/DEVELOPMENT.md`; worth adding.
- **1.64 client data files** and **raw period data**: only needed to regenerate content; pointers in
  `docs/DEVELOPMENT.md` §0 and §4.
- Not needed for development: `tools/dotnet` (release bundle only, `docs/DEVELOPMENT.md` §7).

## Blockers / caveats

1. **MSBuild parallel nodes are blocked in this sandbox.** The documented solution build fails silently
   (`Build FAILED. 0 Warning(s) 0 Error(s)`). Reproduced with a minimal throw-away `net10.0` app having two
   `ProjectReference`s (>1 reference triggers the nested restore walk on extra nodes), so this is an environment
   limitation, not a repo defect. Workaround that built everything:
   `-p:RestoreBuildInParallel=false -p:BuildInParallel=false -m:1`.
2. **VSTest/testhost cannot start here**: `Win32Exception (5) Access is denied` opening its parent process handle.
   The NUnit suite had to be run in-process (numbers above); no `dotnet test` result exists for this environment.
3. **Fresh clone cannot build the full solution as documented** until `CoreServer/config/serverconfig.xml` exists
   (MSB3030). This affects `Tests` too because it references `CoreServer`.
4. Machine is **offline** for NuGet (`api.nuget.org` unreachable): every restore warns `NU1900`. Builds/tests still
   work from the local package cache; a machine without that cache cannot restore.
5. Not verified here (out of scope / not run): server start, launcher start, content refresh, client build/run,
   in-game checks, navmesh generation, content pipeline.
