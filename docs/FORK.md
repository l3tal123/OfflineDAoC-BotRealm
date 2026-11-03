# This fork: setup, syncing with upstream, and development workflow

This checkout is the development repository for **Offline DAoC BotRealm**, a fork of
[shadowofze/OfflineDAoC-True165](https://github.com/shadowofze/OfflineDAoC-True165) whose development focus is the
**bot system**: quality-of-life for the player managing bots, and extending the bots' autonomy until they can
complete the whole game by themselves. The game itself, its rules and its content are described in
[AGENTS.md](../AGENTS.md) and [docs/DEVELOPMENT.md](DEVELOPMENT.md); this page covers only the fork's mechanics.

| | |
|---|---|
| Fork (this repo) | `https://github.com/l3tal123/OfflineDAoC-BotRealm.git` |
| Parent (upstream) | `https://github.com/shadowofze/OfflineDAoC-True165.git` |
| Default branch | `main`, tracking `origin/main` |
| Bot development plan | [docs/BOT-DEVELOPMENT.md](BOT-DEVELOPMENT.md) |
| Verified toolchain | [docs/DEV-ENVIRONMENT.md](DEV-ENVIRONMENT.md) |

Everything in [AGENTS.md](../AGENTS.md) still applies: period sources first, nothing from Trials of Atlantis or
later, never change difficulty or combat numbers, bots follow the player rules, content changes go in
`runtime/true165` through the generator, never commit a played database or player data.

## 1. Remotes and branch model

```
origin    https://github.com/l3tal123/OfflineDAoC-BotRealm.git    (your fork: push here)
upstream  https://github.com/shadowofze/OfflineDAoC-True165.git  (parent: fetch from here, never push)
```

- **`main`** mirrors the upstream release line plus the fork's merged work. It is the branch that gets synced.
- **Feature branches** (`feat/bots-...`, `fix/bot-...`, `docs/...`) for each change; merge them into `main` when
  they are tested. Keeping `main` close to upstream is what keeps syncing painless.
- **Upstream is fetch-only.** Pushing to `upstream` is not possible with the fork's permissions and is not intended.
- The fork's `main` is only ever moved forward by a merge or fast-forward. **Never force-push `main`** and never
  rebase published fork commits; both make future upstream merges and anyone else's clone harder.

This checkout is already configured: both remotes, `core.longpaths=true` (Windows' 260-character path limit),
`http.sslBackend=openssl` (Git for Windows' own TLS, which works where Windows' schannel does not), and
`rerere.enabled=true` (see 2.2). To reproduce or repair it - for example after unpacking a ZIP export, which has no
`.git` folder, or after moving the folder - run these by hand:

```
git init -b main
git remote add origin   https://github.com/l3tal123/OfflineDAoC-BotRealm.git
git remote add upstream https://github.com/shadowofze/OfflineDAoC-True165.git
git fetch origin main
git fetch upstream main
git update-ref refs/heads/main refs/remotes/origin/main   # no worktree files are written
git symbolic-ref HEAD refs/heads/main
git reset --mixed                                         # index only; extracted files stay as they are
git branch --set-upstream-to=origin/main main
git config --local core.longpaths true
git config --local http.sslBackend openssl
git config --local rerere.enabled true
```

Then create the git-ignored build config the server needs (a fresh clone or ZIP export does not have it):

```
copy development-source\server\CoreServer\config\serverconfig.example.xml development-source\server\CoreServer\config\serverconfig.xml
```

## 2. Syncing the fork with upstream

Upstream sync is **manual on purpose**: nothing merges or pushes to this fork automatically. The fork's owner decides
when upstream changes land in `main`, so upstream work can never merge while a development branch or an untested
change is in progress.

The fork used to carry `scripts\sync-fork.cmd` and `scripts\sync-upstream.ps1`; they were removed on 2026-11-02
because the merge is only a few commands and the part that needs a human is the conflict and test work. The commands
are:

| What | How |
|---|---|
| Preview what upstream has | `git fetch upstream` then `git log --oneline main..upstream/main` and `git diff --stat main..upstream/main` |
| Sync now (merge + push) | the five steps below |
| Merge locally, push later | the five steps below, without the last one |

1. `git status` must be clean (commit or stash tracked changes; untracked files are left alone). `git fetch upstream`.
2. `git switch -c upstream-sync` (or reuse an existing `upstream-sync` branch) and `git merge upstream/main`.
3. Resolve conflicts as described in 2.2.
4. Build, test and (for gameplay changes) check in game - section 3 - before `main` moves.
5. `git switch main && git merge --ff-only upstream-sync && git push origin main`, then delete the branch.

Merging into a scratch branch first (step 2) keeps `main` unchanged until the merge compiles and the tests pass.
If a merge is already in progress and you want to start over: `git merge --abort`.

### 2.1 What upstream sync looks like here

Upstream `main` carries the released line and the unreleased development that follows it: on 2026-11-02 it had the
`v0.2` release plus the 0.3 work in progress. So:

- Merging `upstream/main` brings the newest upstream code, but the fork's builds may then be **ahead of the last
  release** - the launcher shows the development version while the playable client (which comes from the release -
  see 3.2) is the released one.
- To match a release exactly, merge the release tag instead: `git fetch upstream --tags` and merge `v0.2`.

The fork keeps upstream's release tooling in `release/` (the v0.2 line added the patch builder and its tests), so
matching a released version and building the update patch both work here; `release/README.md` explains how a release
and its update patch are built and published.

### 2.2 Conflict handling (merge policy)

Conflicts should be rare because the fork adds **new files** and keeps edits to upstream files small and focused -
but they do happen, and one kind is expected: upstream and the fork both work on the bot layer. For example, the
`v0.2` merge touched `development-source/server/GameServer/bots/GameBot.cs` on both sides (weapon and shopping
behaviour, horse sizing, companion teleport/death handling, death logging). When one happens:

1. On the scratch branch from section 2 (a clean tree), `git merge upstream/main`.
2. Resolve each conflicted file **keeping both sides' intent**: upstream's correctness/compatibility changes plus the
   fork's bot work. Never accept one side wholesale to "get past" a conflict, and do not silently drop one of two
   implementations of the same feature - pick the newer/cleaner one and say so in the commit message.
3. For content files (`runtime/true165/*.json`, `tools/claude-version/true165` outputs), prefer the generator:
   regenerate from the merged generator sources rather than hand-merging thousands of JSON rows, and keep the two
   copies of each content file identical (AGENTS.md rule 7).
4. Build and test before committing (section 3), then `git add` the resolved files, `git commit --no-edit`,
   `git push origin main`.
5. `git rerere` is enabled in this checkout, so repeated upstream conflicts are recorded and replayable.

## 3. Development environment

The verified toolchain and everything still missing is in [docs/DEV-ENVIRONMENT.md](DEV-ENVIRONMENT.md). Short
version for this PC:

- **.NET 10 SDK** - installed (`dotnet --version`); the server and the launcher are `net10.0`.
- **Python 3.10+** - installed; needed by the content pipeline and `database/unpack_db.py`.
- **Godot 4.7.2 stable .NET/mono** - needed only for the client; not on `PATH` by default. See
  [source/new-client/README.md](../source/new-client/README.md).
- **World database** - `python -I database/unpack_db.py` writes the clean world to `runtime/data/` (git-ignored).
- **Not in the repository** (get from the release download, or point the tools at your own copy): the converted
  client content (~37 GB), the full navigation meshes (2.1 GB), the server/launcher binaries, and the period data
  archive. [docs/DEVELOPMENT.md](DEVELOPMENT.md) section 0 and `source/new-client/pipeline/paths.py` say what each
  tool needs.

Build and test the server (from `development-source/server`), exactly as documented in
[docs/DEVELOPMENT.md](DEVELOPMENT.md) section 1:

```
dotnet build "Dawn of Light.sln" -c Release
dotnet test Tests/Tests.csproj -c Release --no-build
```

Verified in this checkout (2026-11-02, after merging upstream `v0.2`/0.3 development): the server builds with 0
errors, the NUnit suite passes (3,222 tests: 3,153 passed, 0 failed, 69 skipped `Explicit`), and the launcher builds.
Two prerequisites/build notes that are not obvious from the upstream docs:

- **`CoreServer/config/serverconfig.xml` must exist before the server solution builds.** It is git-ignored (a local
  config), so a fresh clone or ZIP export does not have it and the build stops with `MSB3030: Could not copy the file
  ...serverconfig.xml because it was not found`. Create it from `serverconfig.example.xml` as in section 1.
- **Inside the AI harness sandbox** only: the documented plain build can fail with `Build FAILED. 0 Warning(s)
  0 Error(s)` and `dotnet test` can fail with `Win32Exception (5): Access is denied` because the sandbox blocks
  MSBuild worker nodes and VSTest process handles. That is an environment limit, not a repo defect; in a normal
  Windows terminal both commands work. When you must build under the sandbox, add
  `-p:RestoreBuildInParallel=false -p:BuildInParallel=false -m:1`.

The machine is offline for NuGet (`api.nuget.org` unreachable); restore works from the local package cache, but a
machine without that cache cannot restore.

Do not build or test while a server is running (AGENTS.md rule 10). The first build is the long one; later builds
are incremental. The launcher has its own project in `development-source/tools/OfflineDaoc.Dashboard`: build it
with `dotnet build OfflineDaoc.Dashboard.csproj -c Release`, then copy `OfflineDAoC.*` into `runtime\` and
`wwwroot\` into `runtime\dashboard\wwwroot\` with the launcher closed. The launcher's UI-free logic lives in that
project's `Launcher\` folder; the old WinForms project is no longer in the repository.

Client script checks need `Godot --headless`; see the client README's "Checks and tests" section. Treat a client
change as untested until `load_check.gd` passes on the edited scripts.

### 3.1 Working copies: source vs. playable folder

Keep them separate, as [docs/LLM-QUICKSTART.md](LLM-QUICKSTART.md) recommends:

- **this folder** - the source checkout; commit here;
- **a playable folder** - a copy of the release install where you deploy tested builds and play.

`runtime/data/`, `runtime/server/state-165/`, logs, the client's `godot/content/` and `.godot/`, a release
downloader's multi-GB part cache (`release/.downloads/`), and the playable folder itself are all git-ignored. Never
commit a played database, `account.txt`, test characters, logs, bot rosters, backups or credentials (AGENTS.md
rule 8).

### 3.2 Updating the playable folder to a new upstream release

The playable folder is updated with the release downloader that ships with the fork, `release\GET-OFFLINE-DAOC-TRUE165.cmd`
(its current release version is in `release\Get-OfflineDAoC-True165.ps1`). It is the player-facing installer:
verified parts, SHA-256 checks, `patch-backups\` before replacing anything, an undo on failure, and it never touches
`runtime/data`, `runtime/account.txt`, the logs, `runtime/server/state-165` or `new-client/save`. It has two
fork-specific switches: **`-ClientOnly`** (the client alone - the common case, see below) and **`-AllowLocalChanges`**
(the whole folder; see the whole-folder steps below). Use this checkout's copy, not the downloader zip from the
release page (that one is the stock script, without the switches); sync `release/` from upstream first so it knows
the newest release.

#### Client-only updates

The client moves with upstream's releases and contains none of the fork's work, so it does not need the whole-folder
path below. To take just the released client - keeping the server, the launcher/dashboard and `runtime\true165`
exactly as they are - double-click **`release\UPDATE-CLIENT-ONLY.cmd`** (it finds the playable folder in the usual
place and asks before changing anything; pass `-Update "<folder>"` if it is elsewhere), or run:

```
release\GET-OFFLINE-DAOC-TRUE165.cmd -Mode update -Update "<playable folder>" -ClientOnly
```

- It uses the same verified **release patch** (about 2.4 GB for v0.1 to v0.2) instead of the complete install (about
  40 GB), and plans only the patch's `new-client\` files. `runtime\` is not touched at all, so no fork build has to be
  put back and no database backup is needed for it.
- `-AllowLocalChanges` is not needed: the fork never edits `new-client\`, so those files match the released versions.
  If one does not match (a local edit), the update still stops before changing anything.
- The client and the server can then be **a release apart** (a newer client with the fork's server build); that is
  the point of the switch, and the two do not have to match. Sync and rebuild the fork (section 2) when you want the
  server on the new release too, or run the whole-folder update below.
- The client's settings and `new-client\save` are never touched, and everything replaced is backed up in
  `patch-backups\`.

#### Whole folder (client, server, launcher and content)

1. Quit the game, click **STOP SERVER**, close the launcher. Nothing may be running from the folder.
2. Sync and rebuild first (section 2): the update puts the *released* launcher, server and content into the folder,
   and the fork's builds only go back on top in step 4.
3. Run the updater against the folder:

   ```
   release\GET-OFFLINE-DAOC-TRUE165.cmd -Mode update -Update "<playable folder>" -AllowLocalChanges
   ```

   Without `-AllowLocalChanges` the updater refuses a fork install: it checks every file it would replace against the
   released versions, and the fork's own launcher, server and content files never match, so it stops and changes
   nothing (it then offers a fresh install with the save copied over). With the switch it treats those files like any
   other replacement - it backs them up and replaces them - and prints which files those were. It is a fork addition
   in `release/Get-OfflineDAoC-True165.ps1`; keep it when merging upstream.
4. Put the fork's builds back over the updated folder, after backing up what is replaced in
   `deployment-backups\<change>-<date>-\`:
   - launcher: `runtime\OfflineDAoC.exe`, `runtime\OfflineDAoC.dll`, `runtime\OfflineDAoC.pdb`,
     `runtime\OfflineDAoC.deps.json`, `runtime\OfflineDAoC.runtimeconfig.json`, and everything in
     `runtime\dashboard\wwwroot\` (the web UI);
   - server: `GameServer`, `CoreServer`, `CoreDatabase` and `CoreBase` (DLL and PDB) in `runtime\server`,
     `runtime\server\lib` and, where the install has it, `runtime\server\win-x64`;
   - content: the merged `runtime\true165\*.json`;
   - any other changed content under `runtime\server` (for example `serverconfig.xml`).
   Back up `runtime\data\opendaoc-165.sqlite3.db` (with `-wal`/`-shm` if present) before the first START SERVER after
   an update, because the new server can migrate the save.
5. Start the game as usual. The first **START SERVER** after an update applies the new content and can take a few
   minutes.

- **Do not start the game between steps 3 and 4.** At that point the folder has the released launcher and server, and
  playing it would ignore the fork's bot work (and could start the new server on the old save).
- **A failed or stopped update** is safe: run it again and it continues, or undoes what it did. `patch-backups\` and
  `deployment-backups\` can be deleted once the new version runs well.
- **Alternative:** let the updater stop on the changed files and answer yes to its fresh install (`<folder>-v0.2`):
  it copies the save (database, `account.txt`, `runtime\server\state-165`, `new-client\save`) into the new folder,
  and the fork's builds are deployed there in the same way. The old folder is only read.
- **Version pairing:** the release gives the released client; the fork's launcher/server are built from the source
  checkout. If `main` is on upstream's development line (2.1), the fork builds can be newer than the released client.
  Merge the release tag instead if client and server must match the release exactly.

## 4. Day-to-day git workflow

1. `git fetch upstream` and sync (section 2) - start from an up-to-date fork.
2. `git switch -c feat/bots-<short-name>` - one change per branch.
3. Make the change; follow the project rules and the bot plan. Keep fork-specific docs and notes in new files.
4. Build, run the tests, and (for gameplay changes) verify in game on a copy of the playable folder. Report
   automated tests separately from in-game checks (AGENTS.md rule 13).
5. Commit small, with a prefix that matches the area, e.g. `Bots: ...`, `Companions: ...`, `Docs: ...`,
   `Client: ...`, `Server: ...`, `Content: ...`. Say what was tested in the message.
6. Merge into `main` (fast-forward or `--no-ff`), `git push origin main`.
7. Optionally open a pull request to upstream for changes that are not fork-specific - upstream merges are the
   owner's decision, and a PR is a proposal, not a merge.

Keep upstream edits focused: every changed line in an upstream-owned file is a potential sync conflict, while a new
file almost never conflicts. Put fork documentation in `docs/` new files and link it from the fork note at the end
of [AGENTS.md](../AGENTS.md). Fork-specific scripts belong in `scripts/`; there are none at the moment - the sync is
the commands in section 2.

### 4.1 Fork files and tooling

- `docs/FORK.md` (this page), `docs/BOT-DEVELOPMENT.md`, `docs/DEV-ENVIRONMENT.md`, `docs/WORLD-MAP.md` (the
  launcher dashboard's live world map);
- `development-source/tools/OfflineDaoc.Dashboard/`: **the launcher**. A .NET host (`OfflineDAoC.exe`) plus a web UI
  in `wwwroot` that it serves on `http://127.0.0.1:10310/`: start/stop server, True 1.65 pre-flight and content
  refresh, ENTER REALM (Godot client), server console, live population and world map. Its `Launcher/` folder holds
  the UI-free logic (True 1.65 mode and content refresh, rolling console log, client launcher, XP-rate and
  server-property writes, bot generator); the retired WinForms launcher it replaced is no longer in the repository;
- `release/Get-OfflineDAoC-True165.ps1`: the `-ClientOnly` and `-AllowLocalChanges` switches for updating a fork
  install (3.2), and `release/UPDATE-CLIENT-ONLY.cmd`, the double-click helper for a client-only update;
- a fork note appended to `AGENTS.md`;
- git-ignored local material: the release downloader cache (`release/.downloads/`), reference repositories
  (`references/`) and the playable folder.

The fork's old setup/sync scripts (`scripts/setup-fork.cmd`, `scripts/bootstrap-fork.ps1`, `scripts/sync-fork.cmd`,
`scripts/sync-upstream.ps1`) were removed on 2026-11-02; sections 1 and 2 are the replacement.

## 5. Troubleshooting

| Symptom | Fix |
|---|---|
| `schannel: ... SEC_E_NO_CREDENTIALS` on fetch/push | This checkout sets `http.sslBackend=openssl` (Git for Windows bundles OpenSSL). If it was reset: `git config --local http.sslBackend openssl`. |
| `Filename too long` | `git config --local core.longpaths true` (already set; a future fresh clone needs `git config --global core.longpaths true` before cloning). |
| `git status` is not clean before a sync | Commit or stash tracked changes; untracked files are fine and left alone. |
| A merge conflict in a bot file | Keep both sides' intent (2.2); the fork and upstream both touch the bot layer. |
| Push asks for credentials every time | Sign in once interactively; Git Credential Manager stores it in Windows Credential Manager for later pushes. |
| `git push` fails after a merge | The merge is already committed locally; run `git push origin main` once interactively to sign in, then push again. |
| CRLF / line-ending noise in `git diff` | The repo stores LF; `core.autocrlf=true` (on machines that set it) checks out CRLF for text files and normalises on commit. Keep a file's existing line endings when editing (AGENTS.md rule 15). |
| The upstream merge wants to delete a fork file | Upstream never had the file; a delete/modify conflict means upstream renamed/moved something the fork extended. Resolve by moving the fork code onto upstream's new location, not by dropping upstream's change. |
| The downloader says "files ... were changed on this PC" | It is a fork install: for the client only, use `-ClientOnly` / `UPDATE-CLIENT-ONLY.cmd` (3.2), which never plans those files. For a whole-folder update run it with `-AllowLocalChanges`, then put the fork builds back. |
| The updater offers a fresh install (a ~40 GB download) when only the client is wanted | Use `-ClientOnly` (3.2): it applies the release patch (a few GB) to `new-client\` and leaves the server, launcher and content alone. |
| The update stopped or the PC turned off | Run the downloader again: it continues where it stopped, or puts the files it replaced back. |
| After a whole-folder update the game has no bot features / dashboard | Steps 3.2 whole-folder 4-5 were not done yet: the folder is running the released launcher and server. Deploy the fork builds and content. |
| The launcher and the client show different versions | Expected when `main` is on upstream's development line (2.1) or when only the client was updated (3.2); merge the release tag for an exact pairing. |
