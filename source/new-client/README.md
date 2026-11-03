# The BotRealm new client - source

The new client is the game client for the BotRealm server, built in Godot 4.7.2 (.NET). It speaks the
DAoC network protocol of the original client (game client version 1.127) to the server, which is the authority on
everything: the client draws what the server's packets say. Players get it ready to play from the release download.

It shows the game as the 1.64 / 1.65 client did - the 1.64 pregame screens, the classic character figures, the 1.64
spell table - on a modern engine (Vulkan, high frame rates, large crowds).

## Layout

| Path | What it is |
|---|---|
| `godot/` | The Godot project. `game/` is all client code: GDScript, and C# for the network client (`game/net/DaocClient.cs`) and the bot world's core (`game/world_host/*.cs`). `game/net/server.gd` and `game/net/server_world.gd` turn the server's packets into the world and the interface. |
| `godot/game/pregame_164.gd` | The period 1.64 pregame: Choose Your Realm, the standing stones, the one-page creation, the summary. |
| `godot/game/classic_figure.gd` | The classic (1.65) character figures: looks, hair, armour, cloaks. |
| `godot/game/companions/` | The hired companions' windows (hire, roster, gear, train) - the server's DebugMode 'H' messages. |
| `godot/tests/` | Headless checks and debug probes. `tests/load_check.gd` compiles scripts with the autoloads present. |
| `godot/tools/` | Content maintenance scripts run inside Godot (`pack_scenes.gd` packs converted models into `.scn` files). |
| `pipeline/` | The Python content pipeline: it reads the original DAoC client's files (zones, NIF models, textures, sounds, interface art, game data) and writes the converted content into `godot/content`. `nif_static.py` / `nif_character.py` read NetImmerse models (`spec/nif.xml` from NifTools). |
| `tools/build_csharp.ps1` | Builds the C# code into `godot/.godot/mono/temp/bin/Debug/MMOEngine.dll`. |
| `tools/stage_true165_release.py` | Stages the client for a release package's `new-client\` folder (client, content, import cache, built network client, engine; no tests, developer mode, editor state or save data). |
| `docs/` | How the world import, the classic figures, the period pregame, the spell effects and the companion protocol work, and what had to differ from 1.65 (`1.65 EXCEPTIONS MADE.txt`). |

The repository holds the source only. The converted content (`godot/content`, about 37 GB) is derived from the
original game client's files and comes with the release download.

## Running from source

1. Install the .NET 10 SDK and Godot 4.7.2 stable, .NET build (`Godot_v4.7.2-stable_mono_win64`, from godotengine.org).
2. Copy `godot/content` from a release install into `godot/content` (or build it with the pipeline, below).
   `godot/content/.gdignore` must stay: Godot must not import the content folder.
3. Build the C# code: `tools\build_csharp.ps1` (again after every change to a `.cs` file - starting the game with
   `--path` does not compile C#).
4. Start the BotRealm server (its launcher), then run
   `Godot_v4.7.2-stable_mono_win64.exe --path godot -- --server=127.0.0.1 --account=<name> --password=<password> --world-mode=1.65`.
   `--world-mode=1.65` is required: without it the client falls back to code paths of the discontinued normal mode.

The release runs it the same way: the launcher starts the engine with these arguments and `APPDATA` pointed at the
install's `new-client\save` folder (settings, key bindings, logs). Nothing from an original DAoC client install is read
at run time.

## Developer mode

The debug text (Ctrl+F12), the bot inspector (Ctrl+F11) and the Ctrl+F1..F11 test keys work only when
`godot/dev_mode.txt` exists or the game is started with `--dev` (`game/ui/boot_settings.gd`). Releases never include
that file. Logs a bug report can use are in the save folder: `spell_log.txt` (every spell cast / landing, model
changes, fighters facing away), `death_log.txt` and Godot's `logs/`.

## Checks and tests

- Compile every edited script before opening a window: `Godot --headless --path godot -s res://tests/load_check.gd -- res://game/<file>.gd ...`
  (a parse error otherwise shows only a grey window). A new `class_name` needs `Godot --headless --path godot --import`
  first, to refresh the class cache.
- Offline probes (no server) render screens and check data: e.g. `tests/pregame164_shot.tscn`, `tests/hire_window_shot.tscn`,
  `tests/spell_fx_165_check.tscn`, `tests/map_inn_shot.tscn`, `tests/rebase_objects_check.tscn`, and zone probes run
  through the viewer: `Godot --path godot -- --skip-login --zone=<id> --world-mode=1.65 --probe=<name> --out=<folder>`.
  Each probe says at its top what it does and how to run it. Give every run a hard timeout.

## The content pipeline

The pipeline needs Python 3.10+ (`pipeline/requirements-full.txt`) and Real-ESRGAN for the 4x texture upscale
(`realesrgan-ncnn-vulkan.exe` with its `models` folder, under `tools/realesrgan/`). Each `build_*.py` script says at its
top what it builds and how to run it. A full rebuild takes many hours.

Every input from outside this folder is set in one place, `pipeline/paths.py`, by environment variable (a script that
needs a missing one stops and says which):

| Variable | What | Default |
|---|---|---|
| `DAOC_CLIENT` | the DAoC client's files (the 1.127-era client's `app` folder) | none in the repository - set it |
| `OFFLINEDAOC_SERVER` | the BotRealm server's tree (its source and runtime) | this repository's root |
| `OFFLINEDAOC_WORLD_DB` | the BotRealm world database | `<server>/runtime/data/opendaoc-165.sqlite3.db` (the repository has it compressed under `database/`) |
| `PERIOD_DATA` | the period data archive (Allakhazam / Uthgard / Warcry pages) | not in the repository - set it |
| `DAOC_164_ZIP` | the real 1.64 client, `DAoC 1.64.zip` | not in the repository - set it |
| `TRUE165_WORK` | the 1.65 layouts and extracts (inn layouts, loading pictures) | not in the repository - set it |

The database-built data the BotRealm client reads while playing (classes, spells, styles, abilities for the tooltips;
`content/items/model_slots.json`) is built from the BotRealm world database; the rest of the database-built content
(items, quests, loot, NPC lists, monster spells) is only used by the client's offline test mode.

The period content comes from the real 1.64 client (`DAoC 1.64.zip`): the classic figures (`build_classic_races.py`,
`build_classic_armor.py`), the pregame art (`build_pregame_164.py`), and the spell table (`build_spell_fx_164.py` - the
1.64 spells.csv / speffects.csv / spnifs.csv and effect NIFs over the later client's, written to
`content/spells/spell_fx_165.json`). After a model or effect rebuild, repack the scenes:
`Godot --headless --path godot -s res://tools/pack_scenes.gd -- --force res://content/monsters/effects`.

## Rules for working on it (people and LLMs)

- **BotRealm only.** Normal mode is discontinued; some of its code paths are still in the client (behind
  `Server.world_mode`), but nothing new goes there.
- **Period first.** The 1.64 / 1.65 client is the reference: its game.dll layouts, its art, its data tables (check the
  1.64 row before "fixing" a missing spell glow or sound - many differ from the later client's). Use the client's own
  assets; invent nothing that the period client did not have. Every exception is listed in `docs/1.65 EXCEPTIONS MADE.txt`.
- **The server decides.** Never change combat, difficulty or game numbers in the client; it draws what the server
  sends. Protocol additions are agreed with the server side and written down (see the companion protocol doc).
- **Threads.** No GPU read-backs on worker threads and no materials made off the main thread (`game/gpu_jobs.gd`);
  anything that may wait on the main thread runs on its own thread, not the WorkerThreadPool. This caused the login
  freezes.
- **Seamless zones.** At a zone crossing everything moves by the zone offset (`zone_viewer._cross` -> each system's
  `rebase(d)`); anything that holds world positions must implement `rebase`.
- **UI text.** In-game interface text is ALL CAPS bold, as the client's; the pregame uses the 1.64 art and texts.
- **Quest quality of life** (markers, objective colours, the quest guide) is the same in BotRealm as it was in
  normal mode: never gate it by the mode.
- **Performance.** See `docs/PERFORMANCE NOTES.md` (crowds of hundreds, draw ranges, pooled effects).
