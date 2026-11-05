# BotRealm: instructions for AI assistants and developers

Read this first, then [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) (server, launcher, content) and
[source/new-client/README.md](source/new-client/README.md) (the client). Claude Code loads this file as
`CLAUDE.md` too.

## What this is

BotRealm is a single-player Dark Age of Camelot world built around autonomous bots. Its content base is the Classic and
Shrouded Isles game of the 1.65 era (1 November 2003), and on top of that base it adds:
- autonomous gamebots and hired companions;
- the Realm Exchange;
- quality-of-life changes and custom systems the owner asks for.

It has these parts:
- a server: an OpenDAoC fork, C#, .NET 10, SQLite;
- a launcher: a .NET host plus a browser dashboard;
- a new game client built in Godot 4.

BotRealm is **not** a promise of a faithful 1.65 reproduction. The 1.65-era world is the starting point; the owner
changes it as he pleases (see the rules below), and departures from the period game are expected, not defects. The old
names survive only as internal identifiers (see *Some names are older than the brand* below).

- **One world, one database.** The game runs only the BotRealm database; there is no normal mode and no mode switch.
  Some code paths of the discontinued normal mode are still in the server and the client, switched off behind
  `Classic165Mode.IsOn` (server) and `Server.world_mode` (client). Nothing new goes into them.
- **One client.** The new client is the only one. The original DAoC client is not supported, and there is no DXVK.
- **A fresh start.** Nothing transfers from Offline DAoC (the earlier project), and there is no progress importer.
- **Version:** v0.2. The launcher's version is `LauncherHost.DisplayVersion`, and
  the client's window title is `LoginScreen.GAME_TITLE` in `godot/game/login_screen.gd`.

## Layout

The repository mirrors the development folders, so the tools find their files as cloned.

| Path | What it is |
|---|---|
| `development-source/server` | The game server, all bot AI and the tests (`Tests/`). The solution is `Dawn of Light.sln`. |
| `development-source/server/GameServer/mode165` | The world rules and systems: `Classic165Mode`, `PlayerRules165`, hired companions, inns, the horn, crafting, charm, death penalties. The folder name is historical. |
| `development-source/server/GameServer/bots` | Gamebots and companions: `GameBot`, the brains, equipment, resting. |
| `development-source/server/GameServer/bots/autonomous` | The world population AI: goals, camps, groups, travel, RvR, relic raids, sieges. |
| `development-source/tools/OfflineDaoc.Dashboard` | **The launcher** (`BotRealm.exe`): starts the server and the new client, and serves the browser dashboard from `dashboard\wwwroot` on `http://127.0.0.1:10310/`. The launcher-only logic (world content and refresh, the client launcher, the console log) is in its `Launcher\` folder. |
| `development-source/tools/OpenDAoC-BuildNav` | The navigation mesh builder. |
| `tools/claude-version/botrealm` | The Python generators that build the world content files. |
| `runtime/botrealm` | The world content (JSON) that the launcher writes into the world database at START SERVER. |
| `runtime/server` | Server settings and content files, and `navmesh-165` (the inn zones' meshes). |
| `database` | The clean world database (zipped) and the scripts that make and unpack it. |
| `source/new-client` | The new client: the Godot project (`godot/`), its C# network client and the content pipeline (`pipeline/`). |
| `docs` | Player and developer guides. |

Not in the repository:
- the converted client content (about 37 GB, from the DAoC client's files);
- the full navigation meshes (2.1 GB);
- the server and launcher binaries and the bundled .NET;
- the 1.64 client's data files;
- the raw period data.

The release download carries what a player needs. [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) says what each tool
needs and where to point it.

In a playable folder:
- `runtime/BotRealm.exe` is the launcher;
- `runtime/server` holds the server and the navigation meshes;
- `runtime/data/opendaoc-165.sqlite3.db` holds the world and the player's saves;
- `runtime/botrealm` holds the content;
- `new-client/` holds the client, the engine and its `save/` folder.

## Rules

These are the project's rules. Follow them unless the person you work for asks otherwise. They are the baseline for
the world inherited from the 1.65 era; BotRealm's own quality-of-life and custom systems are additions the owner
decides on, and changing the baseline is his call, not a silent choice for an assistant.

1. **Content base sources first.** For content inherited from the 1.65 era, when period sources say how a system
   worked, follow them, even where the inherited OpenDAoC code differs. The sources are:
   - period Allakhazam pages (Wayback Machine, before 1 November 2003);
   - Camelot Warcry;
   - patch notes;
   - CapnBry's radar data;
   - Uthgard;
   - the real 1.64 client's data files.

   Use the inherited code where the sources are silent. Improvise only when both are unclear, and say so. For spell
   visuals and sounds, the 1.64 client's row is the truth. For new BotRealm systems there is no period source: design
   them with the owner, and write down what they do.
2. **Nothing from Trials of Atlantis or later** goes into the inherited world baseline. That means no ToA-or-later
   bonus types (power pool %, stat or resist caps, Acuity, all-skills), no later-patch item stats, and no post-1.65
   regions, classes or systems. The owner may ask for something custom; say so when he does.
3. **Don't change difficulty or combat numbers silently.** Monster levels, HP, damage, XP and combat formulas are the
   1.65-era values; no "keep it playable" buffs or floors. Report it if something looks wrong, and make deliberate
   difficulty changes only as the owner's explicit call. The boss changes that let bots finish raids stay.
4. **Bots follow the player rules.** Every rule a player lives by applies to gamebots and hired companions:
   - combat;
   - wear and repairs;
   - ammo;
   - death penalties;
   - resting;
   - looting.

   A `GameBot` is a `GameNPC`, so an `is GamePlayer` check gives bots the monster rules; the world code uses
   `PlayerRules165.IsPlayerLike`. There are no bot-only shortcuts.
5. **Quest quality of life stays.** The quest markers, the objective highlights and the quest guide (`/questguide`)
   work in BotRealm. Never gate them behind a mode.
6. **Only period drops in the inherited baseline.**
   - Keep a drop when two sources agree, or a period page plainly shows it.
   - Remove every freeshard (Atlas/Phoenix-era) item, with no exceptions.
   - Uthgard is a valid source.
   - Fill gaps from period data first; invent only as a last resort, minimally, and mark it.

   Custom additions the owner asks for are marked as BotRealm additions.
7. **Content goes in the data, not the database.** Change the world through the JSON in `runtime/botrealm`
   (and the generator that writes it). The launcher applies it at START SERVER. Never hand-edit a world database.
   Keep the two copies of each content file (`tools/claude-version/botrealm` and `runtime/botrealm`) the same.
8. **Protect player data.**
   - Never commit or publish a played database, `account.txt`, logs, bot rosters, backups, test characters or
     credentials.
   - The repository's database is the clean world, made by `database/make_clean_db.py`: no accounts, characters or
     bots; keeps, doors and relics at their defaults; automatic account creation on.
   - Never ship the client's `dev_mode.txt` or diagnostics switches.
9. **Tell bot types apart.** Players, hired companions and autonomous gamebots are different. Check which one a
   change affects.
10. **Don't start or deploy without permission.**
    - Never start a server, or copy builds over a running install, without asking.
    - Only one server can use port 10300.
    - Builds and tests on the same PC stall a running server, so wait for it to stop.
11. **Build and test separately.** Deploy only tested outputs, with a backup of what you replace.
    - `GameServer.dll` and `CoreDatabase.dll` go to `runtime/server`, `runtime/server/lib` and
      `runtime/server/win-x64`.
    - In the client, check every edited script with `load_check.gd` before opening a window.
12. **Navigation:** don't rebuild or replace navmeshes globally to fix one route without evidence.
13. **Be honest in reports.** Report automated tests separately from in-game checks.
14. **Keep the credits and licenses.** See [THIRD_PARTY.md](THIRD_PARTY.md), and credit fork fixes under its
    "Fork credits". BotRealm began as a fork of shadowofze/OfflineDAoC-True165; that attribution stays.
15. **Code style:** CRLF line endings and 4-space indents in the server's C# (`.editorconfig`). Keep a file's
    existing line endings.

## Useful facts

- **The mode switch in code:** `Classic165Mode.IsOn` (server property `classic165_mode`). The launcher refuses a
  database where it isn't `True`. The name is historical; there is only one world.
- **Content refresh:** START SERVER hashes `runtime/botrealm/*.json`. When the hash changes, it backs up the world and
  re-applies settings, removals, updates, drops and added rows in one transaction, keeping all progress. See
  [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).
- **GM-only commands:** `Classic165Mode.GmOnlyCommands` lists the cheat and convenience commands a normal player
  can't use (`/tele`, `/tc`, `/spawn`, `/raid`...). The full list for players is
  [ALL SERVER COMMANDS.txt](ALL%20SERVER%20COMMANDS.txt).
- **Hired companions** are persistent bots owned by a player (`offline_hired_companions`). The client talks to their
  windows over the server's DebugMode 0x21 'H' messages; see
  `source/new-client/docs/TRUE 1.65 MODE HIRE WINDOW PROTOCOL.md`.
- **The client is drawn by the server.** The client never decides game numbers. A protocol addition is agreed on
  both sides and written down.
- **The client's settings:** `source/new-client/pipeline/paths.py` lists every outside input the content pipeline
  needs, set by environment variable.
- **Server settings** are in the `ServerProperty` table (set by `true165-rules.json`), the
  `offline_population_settings` and `offline_local_options` tables, and `runtime/server/state-165/bot-goals.json`.

The person you work for may change these rules for their repository. They're safety defaults, not a ban on changes they
ask for.

## Some names are older than the brand

BotRealm was previously branded "Offline DAoC True 1.65" and then "Offline DAoC BotRealm". The 2026-11 rename is a
branding change only: player-visible names (exe, window titles, dashboard, docs, downloader) say BotRealm, while these
keep their old names on purpose because existing databases, saves and the inherited code depend on them:
- the server property `classic165_mode` and its `True`/`False` values;
- the database `runtime/data/opendaoc-165.sqlite3.db` and the `offline_true165_*` tables;
- the content file names (`true165-rules.json`, `drops165.json`, `add-*165.json`) inside `runtime/botrealm` and
  `tools/claude-version/botrealm`;
- the server/client code identifiers (`Classic165Mode`, `TrueMode`, `PlayerRules165`, `*165.cs`, `world_mode == "1.65"`);
- the state/queue folders `state-165` and `navmesh-165`.

An install that still has the old `runtime/true165` content folder is migrated to `runtime/botrealm` by the launcher
on start.

## This repository (BotRealm)

BotRealm is a standalone repository. It began as a fork of
[shadowofze/OfflineDAoC-True165](https://github.com/shadowofze/OfflineDAoC-True165) and left the GitHub fork network on
2026-11-05; the original project stays credited for everything inherited from it, but nothing is synced between the
two repositories. BotRealm's development focus is the bot system (player quality-of-life for managing bots, and bot
autonomy towards completing the whole game). The bot subsystem map, its capabilities and its gaps are in
[docs/BOT-DEVELOPMENT.md](docs/BOT-DEVELOPMENT.md), and the verified toolchain in
[docs/DEV-ENVIRONMENT.md](docs/DEV-ENVIRONMENT.md). Keep BotRealm's additions in new files where possible, and keep
the rules above unchanged unless the person you work for asks otherwise.
