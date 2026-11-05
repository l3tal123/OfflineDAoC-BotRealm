> **Disclaimer.** Dark Age of Camelot is the intellectual property of Electronic Arts and Broadsword
> Online Games. BotRealm is not affiliated with, endorsed by, or connected to either company. The
> game client files, textures, models and other assets it uses belong to them, and this project is built on
> top of that copyrighted work.
>
> This is a free, non-commercial fan project. It is not a product and it is not for sale. No donations
> are accepted, and the project owner has not made, and will not make, any money from it. The client
> files are the same publicly available ones every free shard uses; the work original to this
> repository is the bot system, the server customizations and the new client's code. Because the code is open
> source, the owner cannot control what forks or other people do with it.
>
> This "time capsule" of the 2003 game was never meant to compete with the current retail Dark Age
> of Camelot, which is a very different, modern experience. If that interests you, please visit the
> official site: [darkageofcamelot.com](https://www.darkageofcamelot.com/).

> [!IMPORTANT]
> **This is an AI-made project.** All of its code was written by AI tools (Codex, then Claude). The project owner
> has no game development or coding experience: they directed the work and play-tested it. Expect bugs.
>
> **BotRealm began as a fork** of [shadowofze/OfflineDAoC-True165](https://github.com/shadowofze/OfflineDAoC-True165),
> itself built on the author's earlier Offline DAoC. The last public release from the original project is **v0.2**;
> BotRealm builds are published from this repository's Releases page. Large parts of the game haven't been played
> through by a person. Expect bugs, keep a backup of your save, and report problems in
> [Issues](https://github.com/l3tal123/OfflineDAoC-BotRealm/issues).

# BotRealm: a single-player Dark Age of Camelot world built around autonomous bots

BotRealm is a local, single-player Dark Age of Camelot that runs entirely on your own PC. Its content base is the
Classic and Shrouded Isles game of the **1.65 era** (November 2003), and it fills the world with thousands of
gamebots that level, group, trade, craft and fight on their own. On top of that base it adds the bot and companion
systems, the Realm Exchange, and its own quality-of-life and custom changes.

The 1.65-era world is the starting point, not a promise. BotRealm is not a faithful 1.65 reproduction and does not
try to be one: the owner adds and changes things as the bot game needs them. What is inherited from the period game
is described below, and so is everything BotRealm does differently.

## What this project is for, and where it's going

The point of BotRealm is the bots. The question behind it is simple: **can a world of autonomous gamebots live a
whole Dark Age of Camelot?** Level, gear up, group, craft, trade, defend and take keeps, run relic raids, and get
through the game's content on their own, under the same rules a player lives by, while you play alongside them or
just watch the world run.

So the project's priorities are:

- **Bot autonomy.** Bots that make their own decisions about where to hunt, when to rest, what to buy, whom to
  group with, which keep to defend and which event to join, with as little scripting as possible.
- **Bots on the player rules.** No bot-only shortcuts: the same combat, wear, ammo, death penalties, resting,
  looting and travel rules apply to them as to you.
- **Quality of life for the person playing.** A launcher dashboard that makes a world of thousands of bots
  manageable: the live population, the map, groups, RvR, events, records, the Realm Exchange and a bot inspector,
  plus settings such as the XP rate and the bot goals.
- **A world to support that.** The 1.65-era content base is there because it is a good, coherent, well-documented
  playground; it is not a museum piece. Custom systems and QoL changes are expected where they make the bot game
  better.

Going forward, expect the focus to stay on the bot game: deeper autonomy toward completing the whole game
(epics, raids, RvR, sieges, crafting and the economy), better controls for managing the bots, more custom and
quality-of-life systems, and performance work so a large population stays smooth. Releases are made from this
repository when the tree is stable enough; the code is AI-written and owner-directed, and every build is
play-tested by a person before release. Forks are welcome, and the credits below stay with the people who did the
original work.

## What's different

### From the 1.65-era game

- **The world is populated.** Thousands of autonomous gamebots are out there levelling, camping, travelling,
  trading, crafting, sieging keeps and raiding relics, whether or not you are watching.
- **You have a party of hired companions**, not summoned pets: you hire, level, spec, gear and revive them, and
  they come with you across the world.
- **The Realm Exchange**, an auction house where bots and players trade with each other.
- **Quality-of-life systems**: quest markers and a quest guide, an XP-rate setting, a bot dashboard and map,
  auto-loot and other conveniences.
- **Custom rules and content changes** made for the bot game, including bot-tuned bosses and faster respawns on
  named and quest monsters.
- **No Trials of Atlantis or later content.** No ToA-or-later bonus types, item stats, regions or classes.

### From the project it grew out of

BotRealm comes from the Offline DAoC lineage (see [Credits](#credits)). The main differences:

| | Offline DAoC (0.37 and earlier) | BotRealm |
|---|---|---|
| **Rules** | OpenDAoC's freeshard ruleset, plus many custom additions | The 1.65-era rules as the starting baseline, plus BotRealm's quality-of-life changes and custom systems |
| **Client** | The original DAoC client, and a beta new client in 0.37c | The new client only, built in Godot 4 |
| **Items and loot** | Freeshard items, random "ROG" drops, items with later stats | The 1.65-era items, drops and vendor stock, from period data |
| **Leveling** | XP rate options, free levels, buff merchants, hasteners | XP fixed at 1x by default (the launcher can raise it for testing), with no shortcuts |
| **Getting around** | Teleporters and `/tele` | Walking, horse routes and the period portals |
| **Your group** | `/spawn` companion bots on demand | Hired companions that you level, spec and gear |
| **Bots** | Separate bot rules | The same rules as players |
| **Saves** | Progress transfers from every older version | A fresh start: no progress import from Offline DAoC |

## Install

BotRealm runs entirely from its own folder on your PC. There is nothing to install in Windows.

### What you need

- 64-bit Windows 10 or 11, with a CPU that supports **AVX2** (the bots' navigation needs it; most CPUs from 2014
  onward do).
- A graphics card that supports **Vulkan**, with current drivers. It's developed and tested on an RTX 2060 (6 GB).
- 16 GB RAM recommended, and about 48 GB of free disk space for a new install.
- **Nothing to install or turn on in Windows:** .NET comes with the game. You don't need the Windows ".NET Framework
  3.5" feature, the Visual C++ Redistributable or an original DAoC client.

### Use the release download (players)

1. Open this repository's [Releases page](https://github.com/l3tal123/OfflineDAoC-BotRealm/releases) and, under
   **Assets**, download the small **`Download-BotRealm-v<version>.zip`** (just the downloader, a few KB).
2. Put it in a folder with a short path, such as `C:\Games\BotRealm`. Right-click it, choose **Extract All…**, and
   open the extracted folder.
3. Close the game and the launcher if they're running. Then double-click **`GET-BOTREALM.cmd`**. If Windows
   SmartScreen appears, click **More info → Run anyway**. It checks your PC and asks:
   - **1 = Update my game:** it finds your `BotRealm` folder (or asks you to pick it), checks it, and updates it in
     place (much smaller than a full download);
   - **2 = New install:** the complete game, into a new folder (about 36 GB).

   If it stops, run it again; it continues where it stopped.

There is no BotRealm release published yet: the tree is in 0.3 development. Until the first BotRealm build appears
on the Releases page, use a playable folder you already have (v0.2 from the original project) or build from source
(see below). Your characters, companions, bots and settings carry over either way.

### Run it

1. In the `BotRealm` folder, double-click **`START BOTREALM.cmd`**.
2. A console window opens and your browser shows the launcher dashboard at `http://127.0.0.1:10310/`.
3. Click **START SERVER** and wait for the state to read **RUNNING**. The first start applies the world content and
   takes a little longer; on later starts it only updates what changed.
4. Click **ENTER REALM**. Your local account is created the first time, and the game client starts logged in.
5. Create a character: pick a race and a base class, spend your stat points, and choose your look. At level 5 you
   promote to your real class at a trainer. You begin in your race and class's period starting town.

Close the launcher console window to stop the server. Leave it open while you play; a second double-click simply
brings the dashboard back up instead of starting a second launcher.

### Update it

Run the downloader again and choose **1 = Update my game**. It keeps your account, characters, companions, bots and
houses. Before any update, back up:

- `runtime\data\opendaoc-165.sqlite3.db` — the world and your saves (characters, bots, houses, keeps, relics);
- `runtime\account.txt` — your local login;
- `new-client\save\` — the client's own settings.

The launcher also keeps a few database backups under `runtime\data\mode-switch-backups\` and in its
`deployment-backups\` folder.

### Remove it

Delete the `BotRealm` folder (and its desktop shortcut). Nothing was installed anywhere else.

### Build from source (developers)

The repository is the source code, not the playable game: the converted client content (about 37 GB), the full
navigation meshes (about 2.1 GB), the binaries and the raw period data are **not** in Git. A bare clone can be
built, but it needs those assets from a release download to become playable.

1. Install the **.NET 10 SDK** and **Python 3.10+**.
2. Unpack the clean world database: `python database/unpack_db.py` (writes
   `runtime/data/opendaoc-165.sqlite3.db`).
3. Build the server: `dotnet build "Dawn of Light.sln" -c Release` in `development-source/server`. If the build
   complains that `CoreServer/config/serverconfig.xml` is missing, copy
   `CoreServer/config/serverconfig.example.xml` over it.
4. Build the launcher: `dotnet build OfflineDaoc.Dashboard.csproj -c Release` in
   `development-source/tools/OfflineDaoc.Dashboard`. The output is `BotRealm.exe`.
5. Put the built server (`Releases/` plus `Release/lib/`) and launcher into a playable folder's `runtime\` (with
   its `server\navmesh*`, `data\`, `botrealm\` content and `new-client\`), then start `BotRealm.exe`.

Full details, deploy steps and the content pipeline are in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md); the client's
own setup is in [source/new-client/README.md](source/new-client/README.md). Clone into a short folder such as
`C:\src\`; if Git reports "Filename too long", run `git config --global core.longpaths true` and clone again.

## How to use it

### First steps

- **Start with the dashboard.** It is the launcher: **START SERVER**, **STOP SERVER** and **ENTER REALM** are in the
  top bar, and the server's state and console log are on the **Server** tab.
- **Play normally.** Combat, spells, styles, crafting, quests and grouping work as in the period game; the
  [quick commands](QUICK%20COMMANDS.txt) cover the everyday ones, and every command is in
  [ALL SERVER COMMANDS.txt](ALL%20SERVER%20COMMANDS.txt) (GM-only ones are marked).
- **Meet the bots.** They are everywhere in the world and in the dashboard's **Active Population** list. You can
  group with them, trade with them, or just watch them play.
- **Hire companions** at the traveler's inns near Caer Witrin, Connla and Huginfell. A new slot opens at levels
  5, 10, 15, 20, 30, 40 and 45.
- **Set the world up** on the **Options** tab: XP rate, realm-event announcements, bot goal weights (how much of the
  population does PvE, RvR, sieges and so on), and how many bots to generate.

### The launcher dashboard

| Tab | What it shows |
|---|---|
| **Active Population** | Every bot in the world: realm, name, class, level, zone and current task. Search and filter; the **inspect** action opens the bot in the Inspector, and **del** deletes it. |
| **World Map** | The world: painted realm overviews and each zone's own map, zoomable and pannable, with every online bot as a dot. Filter by realm or region; click a bot to select it there, and double-click to select it in the population list. |
| **Groups** | The bot groups currently travelling and fighting together. |
| **RvR / Battlegrounds** | The bots in the frontier and in the battlegrounds, and what they are doing there. |
| **Events / Records** | Live realm events (keeps, relics, sieges, raids) and the record of finished ones. |
| **Exchange** | The Realm Exchange: what bots and players are listing, and what has sold. |
| **Stats** | World totals and leaderboards for kills, deaths, PvP kills and keeps taken. |
| **Inspector** | One bot in detail, in sections: stats, equipment, bag, vault, specs, abilities, realm-exchange listings and effects. |
| **Server** | Server state, pid, memory, uptime, the client, and the rolling server console log. |
| **Options** | The settings above, plus generating and deleting bots. |

### Managing the bots

- **Generate bots** on the **Options** tab: pick a realm and 1, 10 or 100 bots, then **Generate**. **Delete every
  bot…** clears the population; single bots are deleted from the **Active Population** list. Bots that already exist
  keep their progress.
- **Bot goals** are set per level bracket (1-19, 20-49, 50) as the share of time each bot spends on **solo PvE,
  group PvE, RvR and battlegrounds**. Each row must total 100%. Levels 1-19 cannot do RvR and level 50 cannot enter
  battlegrounds. This is the main knob for what kind of world you want.
- **Inspect** a bot from the population list (or the map) to see what it has and what it is trying to do.
- **Bots level and gear themselves**; they buy from merchants with their own coin, craft, repair and visit healers.
  You do not have to manage them to play, but the dashboard lets you when you want to.

### Tips while playing

- **Your save is the world database**, not a character file: characters, bots, companions, houses, keeps, relics and
  the Realm Exchange all live in `runtime\data\opendaoc-165.sqlite3.db`. Copy it to back up.
- **Quests:** quest markers, objective highlights and `/questguide` are always on. Epic quests and epic armour are
  being brought to their 1.65 versions.
- **No teleporters or boats:** walk, use horse routes and the period portals. GM accounts keep `/tele` and `/tc`.
- **Troubleshooting:** ports 10300 (game) and 10310 (dashboard) must be free; a second launcher just reopens the
  dashboard; if the browser doesn't open, go to `http://127.0.0.1:10310/`. The full guide is
  [docs/PLAY.md](docs/PLAY.md).

## What BotRealm has

**The 1.65 game:**
- The Classic and Shrouded Isles world, with the three islands added in 1.65 and the Darkness Falls and Shrouded
  Isles entrances where the 1.64 client puts them.
- Base classes at character creation: you choose your class when you promote at a trainer at level 5. You spend
  your own stat points, and choose your height, face and hair. The pre-game screens follow the 1.64 client.
- You begin in your race and class's period starting town, with a weapon and a training shield. There are 8
  character slots per realm.
- Crafting from the 1.64 client's recipe list, with the period success rates, hand tools and salvage.
- Gear wears and is repaired for coin, and arrows, throwing weapons and poisons are bought or crafted.
- Coin belongs to each character, deaths cost constitution, and songs need their own instrument type.
- Period tasks (`/task`) and housing. Quests, epic quests and epic armour are being brought to their 1.65
  versions.
- Loot comes from period sources: Allakhazam's period pages, CapnBry's radar sightings and Uthgard's measured
  drop rates. Nothing from Trials of Atlantis or later.

**Gamebots on the player rules:**
- Bots start as a base class and promote at level 5, like you.
- They sit to rest at player regeneration rates and walk to their loot bags.
- They buy their armour from vendors, repair their gear, visit healers, craft, and charm monsters only with a
  charm spell they've learned.
- They walk and ride horse routes; there are no teleporters and no boats.

**Hired companions** instead of summoned bots:
- Hire them at the traveler's inns near Caer Witrin, Connla and Huginfell.
- A new slot opens at levels 5, 10, 15, 20, 30, 40 and 45, so a full group of 8 is possible at level 45.
- You choose each companion's look, spec, realm abilities and gear.
- They level with you, and a healer revives them for free.

**Realm war:**
- Keep sieges and relic raids run on their own, and also start when you attack a keep.
- PvE realm events start only when a player sounds the Horn of Rallying at an inn.

**BotRealm's own additions, on top of the 1.65-era base:**
- The gamebots and the bot systems.
- The Realm Exchange: an auction house where gamebots and players trade with each other. Bots list what they
  craft and loot, and buy from each other's listings and yours; you can list your own items and buy theirs. Prices
  follow what an item is worth, and unsold bot listings are repriced. Only crafted and spellcrafted gear, potions,
  poisons, period drops and Darkness Falls items can be listed. Its brokers are in a small building by each inn.
- Boss changes that let bots finish raids, and faster respawns for named and quest monsters.
- Quest markers, quest objectives and the quest guide (`/questguide`).
- Slash commands that skip the period game, such as `/tele` and `/tc`. They're kept for GM accounts only.

## Built on OpenDAoC and Dawn of Light

The server is a modified [OpenDAoC](https://github.com/OpenDAoC/OpenDAoC-Core), which itself grew from
[Dawn of Light](https://github.com/Dawn-of-Light/DOLSharp) (DOL), the long-running open-source DAoC server emulator.
Their combat, spell, item, quest and world systems are the foundation everything here is built on, and their GPL
license and credits carry over to this project. BotRealm adds the bot systems, the hired companions, the Realm
Exchange and its quality-of-life changes on top of the 1.65-era baseline, and replaces content that freeshards added
later with period data.

## Built from period data

The 1.65 world is rebuilt from **about 85,000 pages of period data**, collected from the public sources players
used at the time:

| Source | Pages | What it gave |
|---|---|---|
| Uthgard bestiary (a 1.65 recreation) | about 25,000 | monsters, items and measured drop rates from players' kill logs |
| Allakhazam, the current site | about 24,700 | monsters, items, quests and zones, with player comments |
| Allakhazam as it read in 2001-2004 (Wayback Machine) | about 17,000 | period item stats, monster loot, merchant stock and quest walkthroughs |
| Camelot Warcry (Wayback Machine, 2001-2004) | about 11,600 | quest spoilers, items and news |
| CapnBry's DAoC radar | about 6,000 | monster positions and levels from 176,970 live-server sightings |

The patch 1.64 client's own data files supplied the recipe list, the class and race tables and the pre-game
screens.

Period sources come first: an item, drop or spawn stays only when the period data supports it, and nothing is
invented unless no source exists. **The period data itself is not in this repository at this time.** What's here
is the result: the world database and the content files built from it.

## Credits

BotRealm stands on other people's work. All of it stays credited:

- **The original project and its creator: [shadowofze](https://github.com/shadowofze).** BotRealm began as a fork of
  [shadowofze/OfflineDAoC-True165](https://github.com/shadowofze/OfflineDAoC-True165), which grew out of the earlier
  [Offline DAoC](https://github.com/shadowofze/OfflineDAoC). The 1.65-era world, the content pipeline, the release
  tooling, the bot foundations and much of what makes this playable are his work and his project's.
- **The emulator's ancestors:** [OpenDAoC](https://github.com/OpenDAoC/OpenDAoC-Core), which came from
  [Dawn of Light](https://github.com/Dawn-of-Light/DOLSharp) (DOL). Their server, world and combat systems are the
  foundation of the game itself.
- **Community forks whose fixes are ported here:** [stefanrows/OfflineDAoC](https://github.com/stefanrows/OfflineDAoC)
  (Stefan Rows and Aaron Zielke), [chris4aker/OfflineDAoC-LivingServer](https://github.com/chris4aker/OfflineDAoC-LivingServer)
  and [chris4aker/OfflineDAoC-SocialExpansion](https://github.com/chris4aker/OfflineDAoC-SocialExpansion),
  [Nikse77/OfflineDAoC](https://github.com/Nikse77/OfflineDAoC), lometur, and rambodolphin13. The per-fix credit
  list is under "Fork credits" in [THIRD_PARTY.md](THIRD_PARTY.md).
- **The period data and the tools behind it:** Allakhazam and its contributors, Camelot Warcry, CapnBry's DAoC
  radar, and Uthgard; the patch 1.64 client's data files; Godot Engine (MIT); NifTools' nif.xml; Real-ESRGAN. See
  [THIRD_PARTY.md](THIRD_PARTY.md) for licenses and notices.
- **The client's art, sounds, maps and models** are derived from the original Dark Age of Camelot client and belong
  to Electronic Arts / Broadsword, as the disclaimer above says.

If you are one of these authors and want a credit changed or removed, open an issue.

## For developers and AI assistants

- **Where to start:** [AGENTS.md](AGENTS.md) (also read by Claude as [CLAUDE.md](CLAUDE.md)) has the project rules
  and the layout.
- **Guides:**
  - [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md): building and testing the server and launcher, and how the BotRealm
    world content reaches the world;
  - [source/new-client/README.md](source/new-client/README.md): the client and its content pipeline;
  - [docs/LLM-QUICKSTART.md](docs/LLM-QUICKSTART.md): customizing it with your own AI.
- **Where the code is:**
  - `development-source/server`: the game server, the world rules (`GameServer/mode165`) and all bot AI.
  - `development-source/tools`: the launcher and the navigation mesh builder.
  - `tools/claude-version/botrealm` and `runtime/botrealm`: the generators and the world content they make.
  - `database`: the clean world database.
  - `source/new-client`: the new client (Godot 4 project and its content pipeline).

## Good to know

- **AI-written code can have bugs.** Keep a backup of your save before installing a new build or mod. There's
  no guarantee against regressions.
- **Licensing:** this repository holds the server, launcher, tools and the new client's source code. It does
  not hold the original game client's source. The new client's content is converted from the original
  client's files and ships only in the release download. The server's license doesn't relicense
  third-party client assets or dependencies. See [THIRD_PARTY.md](THIRD_PARTY.md).
- **Local only:** the default setup is for one PC. Running a public multiplayer server would need its own
  security work.
- **Forking:** you don't need permission to fork this repository and make it your own. Keep the existing
  licenses and credits ([THIRD_PARTY.md](THIRD_PARTY.md)), including the original project's.

## Known issues

- Horse routes: crossing Prydwen Bridge, the horse can dip into the deck now and then.
- Housing is new in v0.2: it is checked offline but not yet played through on a live server.
- Low-level gamebots (levels 6-9) die a lot: they start with no armour and have to buy it.
- Gear wears at the player rate, so bots rarely need repairs early on. This is expected.
- About 2,100 monster names drop only coin, mostly in the Shrouded Isles, where no period source recorded their loot.
- About 133 low-confidence period merchant goods are left out, and the Darkness Falls seal stock still includes later
  items beside the period ones.
- Support bots don't leave combat while only their pet fights.
- The login panel is the older one, not the period 1.64 one.

## License

The server is licensed under the [GNU General Public License v3](LICENSE), like the OpenDAoC and Dawn of Light
code it is based on. Other parts keep their own licenses; see [THIRD_PARTY.md](THIRD_PARTY.md).

## Changes

See the [changelog](CHANGELOG.md), including what's coming next.
