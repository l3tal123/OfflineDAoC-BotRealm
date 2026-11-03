# How to download and play

This guide is for players. You don't need Git, an AI assistant or any programming tools.

## Before you start

**Your PC needs:**
- 64-bit Windows 10 or 11.
- A CPU that supports **AVX2**. The bots' navigation needs it. Most CPUs from 2014 onward have it; the downloader
  checks for you.
- A graphics card that supports **Vulkan**, with a current driver. The game is developed and tested on an RTX 2060
  (6 GB). If the downloader says Vulkan wasn't found, install the latest driver from NVIDIA, AMD or Intel.
- 16 GB RAM recommended.
- About **48 GB of free disk space** for a new install. Once installed, it takes about 43 GB. An update needs
  about 7 GB free.

**Nothing to install or turn on in Windows.**
- .NET comes with the game, in its own folder, and the game uses only that copy.
- You don't need to turn on the Windows ".NET Framework 3.5" feature. The old Offline DAoC needed it for the original
  DAoC client; BotRealm doesn't use that client.
- You don't need the Visual C++ Redistributable, an original DAoC client, or an account anywhere.

## 1. Download

> **Already playing v0.1?** See [Updating](#updating): the same downloader updates your game in place.

1. Open the [latest release](https://github.com/shadowofze/OfflineDAoC-True165/releases/latest) and, under
   **Assets**, download **`Download-BotRealm-v0.2.zip`**. It's small: just the downloader.
2. Make a folder with a short path, for example **`C:\Games\BotRealm`**, and move the zip there. Avoid OneDrive,
   Program Files and very deep folders.
3. Right-click the zip, choose **Extract All…**, and open the extracted folder.
4. Double-click **`GET-BOTREALM.cmd`**.
   - If Windows SmartScreen says it protected your PC, click **More info**, then **Run anyway**. The files are
     unsigned community builds.
5. The downloader checks your PC and asks what to do. Press **2** for a new install. It shows the download size
   and asks before it starts; press **Y**.
6. It downloads the game in checked parts and installs it into a new folder, **`BotRealm`**, next to the
   downloader's folder (for example `C:\Games\BotRealm\BotRealm`). If that path would be too long, it
   uses `C:\Games\BotRealm` instead: the game needs a short path to keep its graphics cache. It shows
   you the folder before it starts. This takes a while: about 36 GB comes down. If it stops, or you close the
   window, double-click the same file again; it continues where it stopped.
7. At the end it offers a desktop shortcut. When it says the game is installed, you can delete the downloader
   folder.

> **Code > Download ZIP** on the repository page gives you the *source code*, not the playable game.

BotRealm is a fresh start: saves from Offline DAoC 0.37 and earlier can't be brought over.

## 2. Play

1. Open the `BotRealm` folder and double-click **START BOTREALM.cmd** (or use the desktop
   shortcut). The launcher shows **VERSION 0.3**.
2. Click **START SERVER** and wait until it says **RUNNING**. The first start sets up the world and can take a few
   minutes.
3. Click **ENTER REALM**. The game opens. Your own local account is created automatically the first time.
4. Choose a realm and create a character, as in the 1.64 client:
   - you start as a base class and choose your class at a trainer at level 5;
   - you start with **1 gold**, enough to buy basic gear; characters you made before this build get the same 1 gold
     added once the next time you start the server;
   - you spend your own stat points, and choose your height, face and hair;
   - each realm has 8 character slots.
5. To finish: quit the game, click **STOP SERVER**, and wait for it to save.

Only one server can run at a time.

### Bots

The world fills with gamebots that level, group, craft, trade and fight on their own.

- **Add bots** with the **+ Lv.1** button on a realm's card in the launcher. Hold **Shift** for 10 at a time, or
  **Ctrl** for 100. Bots start at level 1 and level up in the world, like you.
- Every bot starts with **1 gold**, like a new character, so it can buy its first gear. Bots you added before this
  build are topped up to the same 1 gold once, the next time you start the server; a bot that already has more
  coin keeps it.
- **Start small** and add more as your PC allows. Every bot is simulated by the server on your PC.
- The launcher's tabs show the active population, a live world map of every painted zone and realm overview, the **Realm Exchange**, the **Bot Inspector**,
  **Server Stats**, groups, RvR, battlegrounds, realm events and realm records. **Bot Goals Setting** sets what the
  bots aim for.
- **Server Stats** (launcher tab) shows the world's tallies: **mobs killed**, **deaths**, **money looted**,
  **PvP kills**, **realm points earned** and **keeps captured**, counting you and every gamebot, plus four **Top 10**
  boards: most mobs killed, most deaths, most PvP kills and most keeps captured, with each character's name, whether
  it is a player or a bot, its realm and its count. It reads the counters the server keeps from this build on (realm
  points include all earlier play), asks the server once when you open the tab or press **REFRESH SERVER STATS**, and
  never polls. Open it while the server is running for fresh numbers; with the server stopped it shows the last
  numbers it published. Double-click a **bot** in any top list to open its **Bot Inspector**.
- **Realm Exchange** (launcher tab) lists every item on the exchange — the item, who listed it and its price — for
  all realms or one, and its **Purchase history** view shows the trades the server has recorded: item, seller,
  buyer, price and time. Open the tab or press **REFRESH REALM EXCHANGE** to read the world database; it never
  reads in the background.
- **Bot Inspector** (launcher tab) shows one bot in detail: coin, health, power and endurance, its eight stats with
  the item and buff parts, equipped gear with condition and durability, backpack and vault contents, specializations
  and spec points spent and available, realm abilities, abilities, spells and styles, its Realm Exchange listings
  and active buffs, plus where it is and what it is doing. Right-click a bot in **Active Population** and choose
  **Inspect bot**, double-click a bot in a **Server Stats** top list, or use the **BOT** selector; **SHOW ON MAP**
  jumps to that bot on the **World Map**. It reads one bot when you ask — opening the tab, changing the selector or
  pressing **REFRESH** — and never polls.
- **World Map** (launcher tab) draws every online world bot as a dot, live, coloured by realm. Right-click a bot in
  **Active Population** and choose **Show on map**, or simply double-click its row: the map opens on that bot's zone
  at 300%, rings it in gold with its name and follows it until you drag the map. The wheel zooms to **1000%**, the
  region selector opens a single region large, a corner legend counts the bots per realm, and hovering a dot shows
  its name, level, class, task and zone.
- **Options** (launcher tab) sets the **XP rate**: one number from 1 to 5000 that multiplies experience from every
  source — kills, quests, tasks, missions, praying — for you and for every kind of gamebot. 1 is normal, 2.5 is two
  and a half times. It saves with the server stopped and applies at the next start. The same tab has **Make Me a GM**
  and the announcement switches.

### Companions

You hire your own group at your realm's **traveler's inn**: near Caer Witrin in Albion, near Huginfell in Midgard,
near Connla in Hibernia.

- At level 5 a courier brings you the inn's quest. Turn it in at the **innkeeper**, who opens the hiring window.
- A new slot opens at levels 5, 10, 15, 20, 30, 40 and 45. Your first companion is free; later ones cost coin.
- You choose each companion's race, class, look and name. It joins at level 5 with a training weapon. You give it
  gear, train its specs and pick its realm abilities, and it shares your group's XP.
- Companions stop at your level, never loot, and are revived for free by any healer of your realm. A smith repairs
  their gear.

### Realm war

- Keep sieges and relic raids happen on their own, and also start when you attack a keep.
- At level 50, the **Horn of Rallying** at the inn starts a great hunt: a dragon, an epic dungeon, Summoner's Hall
  or Darkness Falls. These PvE events never start by themselves.

### Commands

**QUICK COMMANDS.txt** in your game folder has the everyday commands and the launcher shortcuts, and **ALL SERVER
COMMANDS.txt** has every command. Shortcut commands such as `/tele` and `/tc` are for GM accounts only.
The client's keys and its own commands are in [NEW-CLIENT.md](NEW-CLIENT.md).

## Updating

A new version comes as an update to the game you already have, in the folder you installed it in. Your account,
characters, companions, bots and settings stay as they are.

1. Quit the game and click **STOP SERVER**, then close the launcher.
2. Download the new version's downloader zip from the
   [latest release](https://github.com/shadowofze/OfflineDAoC-True165/releases/latest) and extract it, as in
   [1. Download](#1-download).
3. Double-click **`GET-BOTREALM.cmd`** and press **1 = Update my game**. It finds your
   `BotRealm` folder (or asks you to pick it) and shows what it will download.
4. It checks every file it will replace first. Then it downloads the update (about 2.5 GB for v0.1 to v0.2), backs up
   each file it replaces in `patch-backups\` in your game folder, and puts the new files in.
5. Start the game as usual. The first **START SERVER** after an update brings the world up to date and can take a
   few minutes.

- **If it says files were changed on this PC** (for example by a mod), it changes nothing. It can make a fresh
  install in a new folder and bring your save over; your old folder is only read.
- **If the update fails or the PC turns off**, run the downloader again: it puts your files back or finishes the
  update.
- Your save (`runtime\data`), your login (`runtime\account.txt`) and your client settings (`new-client\save`) are
  never touched. `patch-backups\` can be deleted once the new version runs well.

## Troubleshooting

| Problem | Try this |
|---|---|
| Double-clicking the downloader says it was started from inside the zip | Right-click the zip, choose **Extract All…**, and run the file from the extracted folder. |
| "Your CPU does not support AVX2" | The game can't run on that PC. The bots' navigation needs AVX2. |
| "Vulkan was not found" | Install the latest graphics driver from NVIDIA, AMD or Intel, then run the downloader again. |
| "needs about N GB of free space" | Free up space, or move the downloader folder to another drive and run it again. |
| A download stops or a part fails its check | Run the downloader again. It keeps finished parts and fetches only what's missing. |
| "The folder … already exists" | The downloader never overwrites a folder. Rename or move the old one, or run the downloader from another folder. |
| Windows SmartScreen blocks a file | Choose **More info → Run anyway**. The files are unsigned community builds. |
| ENTER REALM stays greyed out | Wait until the launcher shows RUNNING. The first start can take a few minutes. |
| START SERVER shows an error | The launcher couldn't apply the world content, and it changes nothing when that happens. Note the message and report it. |
| "Port 10300 is in use", or the server won't start | Another server is running. Stop it first; only one can run at a time. |
| The game window stays black or closes at once | Update your graphics driver. The game needs Vulkan. |
| Your antivirus removes a game file | Restore it and allow the game folder. The programs are unsigned, so some antivirus tools flag them. |

**Keep a backup of your save** before you try a new build or a mod. Your world, characters, companions and bots are
in `runtime\data\opendaoc-165.sqlite3.db`.

**Reporting a bug:** open an [issue](https://github.com/shadowofze/OfflineDAoC-True165/issues). Say what you were
doing, and attach the logs from `new-client\save\Godot\app_userdata\MMOEngine` (`logs\godot.log`, `spell_log.txt`, `death_log.txt`) and
`runtime\logs` if there are any. [docs/NEW-CLIENT.md](NEW-CLIENT.md#reporting-problems) says which files to send.
