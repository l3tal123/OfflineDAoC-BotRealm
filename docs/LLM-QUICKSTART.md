# Customize Offline DAoC True 1.65 with your own AI assistant

You can make Offline DAoC True 1.65 your own with an AI coding assistant such as Claude Code or Codex. You don't
need the author's permission to fork it.

## 1. Get your own copy

1. **Fork it:** click **Fork** on this GitHub page, then clone your fork, or use **Code > Download ZIP**.
2. **Get a playable folder:** download the playable release, as described in [PLAY.md](PLAY.md). The source alone
   isn't the playable game: the client's converted content, the full navigation meshes and the binaries come with
   the release.
3. **Keep two folders:** the source checkout, where you edit code, and a separate playable folder, where you test
   builds.

## 2. Start your assistant with this prompt

> This is my fork of Offline DAoC True 1.65, a single-player Dark Age of Camelot world as of patch 1.65 with
> autonomous gamebots and hired companions. Read AGENTS.md, docs/DEVELOPMENT.md and source/new-client/README.md
> first, and follow the project rules in AGENTS.md: period sources first, nothing from Trials of Atlantis or later,
> no difficulty changes, bots on the player rules. My source checkout is this folder; my playable game is at
> [path to playable folder]. Look at the current code before changing it. Don't start the server or replace files
> in the playable folder without asking me, and back up anything you replace. Protect my account, characters,
> companions, bots, items, coins and saves. Build and run the tests separately, make only the change I ask for, and
> tell me exactly what changed and how it was verified. The change I want is: [describe it].

## 3. Where things live

| To change… | Look in |
|---|---|
| True 1.65 rules: XP, crafting, charm, death penalties, GM-only commands | `development-source/server/GameServer/mode165` |
| Hired companions, the traveler's inns, the Horn of Rallying | `development-source/server/GameServer/mode165` (`Hire165`, `HiredCompanions165`, `Innkeeper165`, `HornHandler165`) |
| Autonomous gamebot goals, camps, groups, travel, RvR, relic raids | `development-source/server/GameServer/bots/autonomous` |
| Gamebot and companion combat AI | `development-source/server/GameServer/bots` |
| Commands, combat, spells, pets | `development-source/server/GameServer` |
| World content: drops, item stats, spawns, merchants, inns | `runtime/true165` (the JSON) and the generator in `tools/claude-version/true165` that writes it |
| The launcher | `development-source/tools/OfflineDaoc.Dashboard` |
| The new client: interface, world, controls | `source/new-client/godot/game` (see its [README](../source/new-client/README.md)) |
| The client's converted content | `source/new-client/pipeline` |

## Good habits

- **Change content through the data.** Edit the JSON in `runtime/true165` (or its generator), then START SERVER: the
  launcher applies it and keeps every save. Don't hand-edit a world database.
- **Test on a copy.** Use a copy of your playable folder, or at least back up
  `runtime/data/opendaoc-165.sqlite3.db` first.
- **Commit only source.** Commit source and content changes to your fork, not files from your playable folder.
- **Verify in game.** Say what was tested in game and what was only covered by automated tests.
- **Keep the licenses and credits** ([THIRD_PARTY.md](../THIRD_PARTY.md)). A pull request to this project is a
  proposal; it won't merge by itself.
