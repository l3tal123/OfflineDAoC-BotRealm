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
- Upstream is **fetch-only**. Pushing to `upstream` is not possible with the fork's permissions and is not intended.
- The fork's `main` is only ever moved forward by a merge or fast-forward. **Never force-push `main`** and never
  rebase published fork commits; both make future upstream merges and anyone else's clone harder.

The checkout is already configured. To reproduce or repair it (for example after unpacking a ZIP export, which has
no `.git` folder, or after moving the folder):

```
scripts\setup-fork.cmd
```

`scripts/bootstrap-fork.ps1` is idempotent: it initialises git if needed, attaches the extracted working tree to
`origin/main` without touching any file, adds/repairs both remotes, applies the repo-local git settings, fetches
both branches and prints the state. For reference, the "attach a ZIP export" procedure it performs is:

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
```

## 2. Syncing the fork with upstream

Use one or more of these; they all do the same operation (fetch `upstream/main`, merge it into fork `main`, push):

| When | How |
|---|---|
| Manual, when you want it | `scripts\sync-fork.cmd` |
| Preview only | `scripts\sync-fork.cmd -DryRun` |
| Merge locally, push later | `scripts\sync-fork.cmd -NoPush` |
| Automatic on this PC | `scripts\register-sync-task.cmd` (daily Windows Scheduled Task) |
| Automatic in the cloud | Enable Actions in the fork; `.github/workflows/sync-upstream.yml` runs daily at 05:17 UTC and on demand |

The scripts run on the **Windows PowerShell 5.1** that ships with Windows 10/11 (`powershell.exe`), so nothing has to
be installed first. The `.cmd` wrappers call them with `-ExecutionPolicy Bypass`; if you run a `.ps1` directly, use
`powershell -NoProfile -ExecutionPolicy Bypass -File scripts\sync-upstream.ps1 ...`. If you have PowerShell 7, `pwsh`
works as well.

### 2.1 The local script

`scripts/sync-upstream.ps1` refuses to run on a dirty tracked working tree (untracked files are left alone),
refuses to run on a branch other than `main`, fetches `upstream/main`, prints the incoming commits, merges with
`--no-edit`, and pushes `HEAD:main` to `origin`. On a merge conflict it **aborts the merge and pushes nothing**,
exits with code 2 and lists the conflicted files. Other exit codes: 1 = precondition/setup error, 3 = push failed
(for example not signed in).

It never force-pushes and never rewrites history.

### 2.2 The scheduled task (recommended for this PC)

GitHub disables Actions in forks by default, and scheduled workflows stay paused until the owner enables them, so a
local task is the dependable "sync while I work" option:

```
scripts\register-sync-task.cmd                 # daily at 09:00 local time
scripts\register-sync-task.cmd -DailyAt 18:30  # another time
scripts\register-sync-task.cmd -RunNow         # register and run once
scripts\register-sync-task.cmd -Remove         # delete it
```

The task runs as your Windows user, only while you are logged on, with a 15-minute limit. It pushes with the
credentials **Git Credential Manager** already stored: sign in once interactively (`git push origin main`) so the
task can reuse the credential silently. Check its result in **Task Scheduler > Task Scheduler Library >
OfflineDAoC-ForkSync > History** (a non-zero last run result means the sync failed, usually a conflict or an expired
credential).

Without a stored credential the task can still merge locally but its push fails; run `git push origin main` by hand
to see the sign-in prompt.

### 2.3 The GitHub Actions workflow

`.github/workflows/sync-upstream.yml` (fork additions live in new files so upstream merges stay clean) merges
`upstream/main` into `main` with `GITHUB_TOKEN` and `contents: write`. To turn it on:

1. GitHub > the fork > **Settings > Actions > General**: allow actions (the default "Allow all actions" is fine).
2. **Actions > Sync fork with upstream**: if it is paused, enable it. Scheduled workflows in a fork are disabled
   until this is done.
3. Optional: run it once with **Run workflow** (tick `dry_run` for a report-only run).

A conflicting upstream change makes the job red; resolve it locally as in section 2.4, then push. GitHub notifies
the repository owner of failed scheduled runs.

### 2.4 Conflict handling (merge policy)

Conflicts should be rare because the fork adds **new files** and keeps edits to upstream files small and focused.
When one happens:

1. `git switch main` (on a clean tree) and `git merge upstream/main`.
2. Resolve each conflicted file **keeping both sides**: upstream's correctness/compatibility changes plus the
   fork's bot work. Never accept one side wholesale to "get past" a conflict.
3. For content files (`runtime/true165/*.json`, `tools/claude-version/true165` outputs), prefer the generator:
   regenerate from the merged generator sources rather than hand-merging thousands of JSON rows, and keep the two
   copies of each content file identical (AGENTS.md rule 7).
4. Build and test before committing (section 3), then `git add` the resolved files, `git commit --no-edit`,
   `git push origin main`.
5. `git rerere` is enabled in this checkout, so repeated upstream conflicts are recorded and replayable.

If a merge is already in progress and you want to start over: `git merge --abort`.

## 3. Development environment

The verified toolchain and everything still missing is in [docs/DEV-ENVIRONMENT.md](DEV-ENVIRONMENT.md). Short
version for this PC:

- **.NET 10 SDK** - installed (`dotnet --version`); the server is `net10.0`, the launcher `net10.0-windows`.
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

Verified in this checkout (2026-11-01, see [docs/DEV-ENVIRONMENT.md](DEV-ENVIRONMENT.md)): the server builds with the
current .NET 10 SDK (0 errors), the NUnit suite passes (3006 tests: 2937 passed, 0 failed, 69 skipped `Explicit`), and
the launcher builds. Two prerequisites/build notes that are not obvious from the upstream docs:

- **`CoreServer/config/serverconfig.xml` must exist before the server solution builds.** It is git-ignored (a local
  config), so a fresh clone or ZIP export does not have it and the build stops with `MSB3030: Could not copy the file
  ...serverconfig.xml because it was not found`. `scripts\setup-fork.cmd` creates it from
  `CoreServer/config/serverconfig.example.xml` when missing; do it by hand if you skip the script.
- **Inside the AI harness sandbox** only: the documented plain build can fail with `Build FAILED. 0 Warning(s)
  0 Error(s)` and `dotnet test` can fail with `Win32Exception (5): Access is denied` because the sandbox blocks
  MSBuild worker nodes and VSTest process handles. That is an environment limit, not a repo defect; in a normal
  Windows terminal both commands work. When you must build under the sandbox, add
  `-p:RestoreBuildInParallel=false -p:BuildInParallel=false -m:1`.

The machine is offline for NuGet (`api.nuget.org` unreachable); restore works from the local package cache, but a
machine without that cache cannot restore.

Do not build or test while a server is running (AGENTS.md rule 10). The first build is the long one; later builds
are incremental. The launcher has its own project in `development-source/tools/OfflineDaoc.Launcher`.

Client script checks need `Godot --headless`; see the client README's "Checks and tests" section. Treat a client
change as untested until `load_check.gd` passes on the edited scripts.

### 3.1 Working copies: source vs. playable folder

Keep them separate, as [docs/LLM-QUICKSTART.md](LLM-QUICKSTART.md) recommends:

- **this folder** - the source checkout; commit here;
- **a playable folder** - a copy of the release install where you deploy tested builds and play.

`runtime/data/`, `runtime/server/state-165/`, logs, the client's `godot/content/` and `.godot/`, and a release
downloader's multi-GB part cache (`release/.downloads/`) are all git-ignored. Never commit a played database,
`account.txt`, test characters, logs, bot rosters, backups or credentials (AGENTS.md rule 8).

## 4. Day-to-day git workflow

1. `git switch main && scripts\sync-fork.cmd` - start from an up-to-date fork.
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
of [AGENTS.md](../AGENTS.md). Fork-specific scripts belong in `scripts/`, automation in `.github/workflows/`.

### 4.1 What the fork changed at setup time

Setup only added tooling; no gameplay code or content was changed:

- `scripts/bootstrap-fork.ps1` + `scripts/setup-fork.cmd` - attach/repair this checkout (remotes, config, fetches);
- `scripts/sync-upstream.ps1` + `scripts/sync-fork.cmd` - merge upstream into the fork and push;
- `scripts/register-sync-task.ps1` + `scripts/register-sync-task.cmd` - optional daily Windows Scheduled Task for the sync;
- `.github/workflows/sync-upstream.yml` - optional GitHub Actions sync;
- `docs/FORK.md` (this page), `docs/BOT-DEVELOPMENT.md`, `docs/DEV-ENVIRONMENT.md`;
- a fork note appended to `AGENTS.md`, and the ignored `release/.downloads/` cache added to `.gitignore`.

## 5. Troubleshooting

| Symptom | Fix |
|---|---|
| `schannel: ... SEC_E_NO_CREDENTIALS` on fetch/push | This checkout sets `http.sslBackend=openssl` (Git for Windows bundles OpenSSL). If it was reset: `git config --local http.sslBackend openssl`. |
| `Filename too long` | `git config --local core.longpaths true` (already set; a future fresh clone needs `git config --global core.longpaths true` before cloning). |
| `sync-upstream.ps1` says the tree is dirty | Commit or stash tracked changes; untracked files are fine and left alone. |
| Push asks for credentials every time | Sign in once interactively; Git Credential Manager stores it in Windows Credential Manager. In the scheduled task, also check the task's History tab. |
| Scheduled task runs but nothing syncs | The task's last run result is non-zero: usually a merge conflict (resolve locally) or missing git credentials. Task Scheduler > History shows the exit code. |
| GitHub Actions workflow never runs | Actions are off or the workflow is paused in the fork (Settings > Actions, Actions tab). Fork schedules are disabled until enabled. |
| CRLF / line-ending noise in `git diff` | The repo stores LF; `core.autocrlf=true` (this machine's system setting) checks out CRLF for text files and normalises on commit. Keep a file's existing line endings when editing (AGENTS.md rule 15). |
| The upstream merge wants to delete a fork file | Upstream never had the file; a delete/modify conflict means upstream renamed/moved something the fork extended. Resolve by moving the fork code onto upstream's new location, not by dropping upstream's change. |
