# The new client

True 1.65 plays only in the new client, a game client built in Godot 4 for this project. The launcher's ENTER REALM
starts it; you never start it by hand.

## What it is

- It shows the game as the 1.64 and 1.65 client did:
  - the 1.64 pre-game screens (Choose Your Realm, the standing stones, the one-page creation);
  - the classic character figures;
  - the 1.64 spell effects.
- It runs that on a modern engine: Vulkan, high frame rates and crowds of hundreds of bots.
- Its zones, models, textures, sounds and interface art are converted from the original DAoC client's files. You
  don't need an original client installed.
- The server decides everything in the game. The client draws what the server sends.

## Requirements

- A graphics card that supports **Vulkan**, with current drivers. It's developed and tested on an RTX 2060 (6 GB).
- 16 GB RAM recommended.
- About 37 GB for its content, inside the game folder.

## Playing

- **Keys:** the defaults follow the RPG preset. **Options > CONTROLS** also has FPS and MMO presets. Every action
  takes two bindings: any key, a mouse button (Mouse 4/5, the wheel), or a Shift/Ctrl/Alt combination.
- **Quickbars:** quickbar 1 is on 1 to 0, quickbar 2 on Alt+1 to 0, and quickbar 3 on Ctrl+1 to 0. Shift+1 to 0
  switches quickbar 1's bank.
- **The client's own commands:** `/loc`, `/follow`, `/stick`, `/face`, `/realmwar` (`/rw`), `/craft`, `/train`,
  `/qbar`, `/stealth` (`/hide`), `/macro` and `/quit`. Every other command goes to the server.
- The full key list is
  [source/new-client/docs/CLIENT COMMANDS AND KEYS.txt](../source/new-client/docs/CLIENT%20COMMANDS%20AND%20KEYS.txt).

## Where things are

In your game folder:
- `new-client\godot` holds the game;
- `new-client\tools\godot_net` holds the engine;
- `new-client\save` holds your settings, key bindings and logs, in `new-client\save\Godot\app_userdata\MMOEngine`. Delete `new-client\save` to go
  back to the defaults.

## Reporting problems

Say what you were doing, and include the logs. They are in your game folder, in `new-client\save\Godot\app_userdata\MMOEngine`:
- `logs\godot.log`: the latest run. If the client crashed, the error is at the end of this file. Each start
  moves the previous run's log to `logs\godot<date and time>.log`, so after a crash grab `godot.log` (or the newest
  dated one) before starting the game again;
- `spell_log.txt`: spell casts and landings, model changes;
- `death_log.txt`.

If the server stopped or the game disconnected, also include the newest files in `runtime\logs` and
`runtime\server\logs`. A crash that leaves nothing in `godot.log` is usually in Windows' Event Viewer, under
Windows Logs > Application.

## For developers

The client's source, how to run it from source, and its content pipeline are in
[source/new-client/README.md](../source/new-client/README.md).
