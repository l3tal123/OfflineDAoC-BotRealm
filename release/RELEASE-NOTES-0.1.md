**Offline DAoC True 1.65 v0.1**: single-player Dark Age of Camelot as it was at patch 1.65 (Classic and Shrouded
Isles, November 2003), with a world full of gamebots, hired companions, and a new client built in Godot 4.

> **Historical note:** this release shipped under the earlier product name, "Offline DAoC True 1.65" (the project is
> now BotRealm). The names and commands below are kept as published.

> **This is an AI-made project and an early version.** All of the code was written by AI tools; the project owner has
> no coding experience. Large parts of the game haven't been played through by a person yet. Expect bugs, and keep a
> backup of your save.

## How to install

1. Under **Assets** below, download **`Download-OfflineDAoC-True165-v0.1.zip`** (a few KB: just the downloader).
2. Put it in a folder with a short path, such as `C:\Games\OfflineDAoC`. Right-click it, choose **Extract All…**,
   and open the extracted folder.
3. Double-click **`GET-OFFLINE-DAOC-TRUE165.cmd`**. If Windows SmartScreen appears, click **More info → Run
   anyway**. It checks your PC, shows the download size and the folder it will use, asks, and then installs the
   game into a new folder, `OfflineDAoC-True165`, next to the downloader's folder (or `C:\Games\OfflineDAoC-True165`
   if that path would be too long). If it stops, run it again; it continues where it stopped.
4. Open that folder and double-click **`START OFFLINE DAOC TRUE 1.65.cmd`**. Click **START SERVER**, wait for
   **RUNNING**, then click **ENTER REALM**. Your account is created the first time.

You **don't** need to download the other files on this page by hand: the downloader fetches and checks them.

## What you need

- 64-bit Windows 10 or 11.
- A CPU with **AVX2** (most from 2014 onward). The downloader checks.
- A graphics card that supports **Vulkan**, with a current driver.
- 16 GB RAM recommended.
- About 45 GB of free disk space while it installs.
- Nothing to install or turn on in Windows. .NET comes with the game, and the Windows ".NET Framework 3.5" feature,
  the Visual C++ Redistributable and an original DAoC client are **not** needed.

## More

- The full guide with troubleshooting: [docs/PLAY.md](https://github.com/shadowofze/OfflineDAoC-True165/blob/main/docs/PLAY.md)
- What's in this version, and the known issues: [CHANGELOG.md](https://github.com/shadowofze/OfflineDAoC-True165/blob/main/CHANGELOG.md)
- Bugs: [open an issue](https://github.com/shadowofze/OfflineDAoC-True165/issues). Say what you were doing, and
  attach the logs from `new-client\save` if you can.
- The source code, and the guides for developers and AI assistants, are in this repository; start with the
  [README](https://github.com/shadowofze/OfflineDAoC-True165#readme).

Saves from Offline DAoC 0.37 and earlier can't be brought over: True 1.65 is a fresh start.

Dark Age of Camelot is the property of Electronic Arts and Broadsword. This is a free, non-commercial fan project,
not affiliated with them.
