**Offline DAoC True 1.65 v0.2**: single-player Dark Age of Camelot as it was at patch 1.65 (Classic and Shrouded
Isles, November 2003), with a world full of gamebots, hired companions, and a new client built in Godot 4.

> **This is an AI-made project.** All of the code was written by AI tools; the project owner has no coding
> experience. Large parts of the game haven't been played through by a person. Expect bugs, and keep a backup of
> your save.

## Already playing v0.1? Update in place (about 2.5 GB)

1. Quit the game, click **STOP SERVER**, and close the launcher.
2. Under **Assets** below, download **`Download-OfflineDAoC-True165-v0.2.zip`** (a few KB: just the downloader),
   extract it, and double-click **`GET-OFFLINE-DAOC-TRUE165.cmd`**. If Windows SmartScreen appears, click
   **More info → Run anyway**.
3. Press **1 = Update my game**. It finds your `OfflineDAoC-True165` folder (or asks you to pick it), checks every
   file it will replace, and updates the game where it is. Your account, characters, companions, bots and settings
   stay; the files it replaces are backed up in `patch-backups\`.
4. Start the game as usual. The first **START SERVER** after the update brings your world up to date and can take a
   few minutes.

If it finds files that were changed on your PC, it changes nothing and can make a fresh install that brings your save
over.

## New players: the complete game (about 36 GB)

1. Download **`Download-OfflineDAoC-True165-v0.2.zip`** below and put it in a folder with a short path, such as
   `C:\Games\OfflineDAoC`. Right-click it, choose **Extract All…**, and open the extracted folder.
2. Double-click **`GET-OFFLINE-DAOC-TRUE165.cmd`** and press **2 = New install**. It checks your PC, shows the
   download size and the folder it will use, and installs the game into a new folder, `OfflineDAoC-True165`. If it
   stops, run it again; it continues where it stopped.
3. Open that folder and double-click **`START OFFLINE DAOC TRUE 1.65.cmd`**. Click **START SERVER**, wait for
   **RUNNING**, then click **ENTER REALM**. Your account is created the first time.

You **don't** need to download the other files on this page by hand: the downloader fetches and checks them.

## What's new in 0.2

- **Player housing, as in 1.64:** the 12 period housing zones, buying a lot and building a house, the garden,
  furniture and hookpoints, house vaults, friend permissions, rent, and the 1.64 housing windows. It is checked
  offline but not yet played through on a live server.
- **Gamebots** buy and wield weapons of the line they trained, go shopping for an affordable upgrade when they keep
  dying, no longer get stuck at merchants, buff the right classes, and show their new level and con colour at once.
- **Companions** follow you through teleporters and releases at once, switch back to melee when their bow is taken away mid-pull, accept quest and no-trade items, show
  as dead on the roster at once, and keep their spell effects after a revive.
- **Horse routes** ride over the bridges ([#1](https://github.com/shadowofze/OfflineDAoC-True165/issues/1)), keep their rider through lag spikes and no longer
  rubber-band. Still a little buggy: the horse can dip into Prydwen Bridge's deck now and then.
- **Items:** merchant caster staves have their focus again, about 470 items regain their 1.65 bonuses, and about
  340 loot items show an icon that matches their name.
- **World:** Camelot's walls and palace door are fixed ([#2](https://github.com/shadowofze/OfflineDAoC-True165/issues/2)),
  missing doors are back in six zones, NPCs no longer stand sunk into their floor
  ([#4](https://github.com/shadowofze/OfflineDAoC-True165/issues/4)), and the inns and some grass have sharper textures.
- **Quests:** quest items can still be handed in after you outlevel a quest you already have.
- **Controls:** the 1.64 keys that did nothing now work: Follow, Stick, Face, Camera Reset, Look Up / Down, Target
  Group #2-8, Target Friend / Object, Last Attacker, Consider, Command Window, Help, Chat Logging, Toggle Names and the
  Perf. Meter, plus a Realm War Map key.
- **The new client:** a RESPEC button in the training window, a YES / NO question before a quest is removed, 47 models
  with their textures back (floors and furniture that drew in one flat colour), one death window with the release timer, the 1.64 loading bar and loot bag, chat history on Shift+Up / Shift+Down, `/qbar`, and
  Mouse Look Toggle as in the original client (off by default; switch it on in Options > Controls).

The full list is in the [changelog](https://github.com/shadowofze/OfflineDAoC-True165/blob/main/CHANGELOG.md).

## What you need

- 64-bit Windows 10 or 11, with a CPU that supports **AVX2** (most from 2014 onward). The downloader checks.
- A graphics card that supports **Vulkan**, with a current driver.
- 16 GB RAM recommended, and about 48 GB of free disk space for a new install.
- Nothing to install or turn on in Windows: .NET comes with the game, and the Windows ".NET Framework 3.5" feature,
  the Visual C++ Redistributable and an original DAoC client are **not** needed.

## More

- The full guide with troubleshooting: [docs/PLAY.md](https://github.com/shadowofze/OfflineDAoC-True165/blob/main/docs/PLAY.md)
- Bugs: [open an issue](https://github.com/shadowofze/OfflineDAoC-True165/issues). Say what you were doing, and
  attach the logs from `new-client\save\Godot\app_userdata\MMOEngine\logs` (and `spell_log.txt`, `death_log.txt` there) if you can.
- The source code, and the guides for developers and AI assistants, are in this repository; start with the
  [README](https://github.com/shadowofze/OfflineDAoC-True165#readme).

Dark Age of Camelot is the property of Electronic Arts and Broadsword. This is a free, non-commercial fan project,
not affiliated with them.
