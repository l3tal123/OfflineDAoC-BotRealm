# Existing projects and notices

BotRealm keeps the credits and licenses of the projects it builds on. The server and navigation-builder source
contain GPL version 3 license texts. Preserve these and all
per-file notices; dependencies may have other licenses. The project's AI-developed additions do not remove
upstream attribution.

- Dawn of Light / OpenDAoC: server emulator foundation. See the server source's LICENSE, gpl.txt,
  gplheader.txt and source headers.
- Navigation builder (OpenDAoC-BuildNav): see its LICENSE and the native pathing dependencies' notices.
- Quest Guide walkthroughs (classic-quest-guides.json): the Allakhazam (camelot.allakhazam.com) Dark Age of
  Camelot quest pages as they read in 2001-2004, most taken from the Internet Archive's Wayback Machine copies.
  They were written by Allakhazam and its contributors, who keep their rights; each guide names its source page
  and capture date.
- Period game data (monster placement and levels, loot, vendor stock, item stats): drawn from period player
  references: Allakhazam's 2001-2004 pages (through the Wayback Machine), CapnBry's DAoC radar sightings and
  the Uthgard bestiary and measured drop rates. Those sites and their contributors keep their rights. The raw
  downloaded pages are not included in this repository.
- Recipes, class and race tables and pre-game art: read from the patch 1.64 DAoC client's own data files.
- Bundled SDK, native and Python packages keep their own licenses and notices; including them is not a
  blanket relicense.
- Fixes ported from community forks of Offline DAoC are credited to their authors under "Fork credits" below.

The original DAoC client, art, sound, maps and other third-party assets are not represented as authored by
this project. Being noncommercial does not itself change third-party rights. The GPL server license is not a
license for those assets.

## Fork credits

Fixes taken from community forks of Offline DAoC, inherited from its source:

| Fix | From |
|---|---|
| Smooth group travel, the Minstrel mez freeze, Valewalker proc ranks, the Warden speed chant, and the idea for the Savage shield/offhand loop guard | [stefanrows/OfflineDAoC](https://github.com/stefanrows/OfflineDAoC) (code by Stefan Rows and Aaron Zielke, GPL-3.0) |
| The console no longer spins a CPU core when its input closes | lometur ([shadowofze/OfflineDAoC#3](https://github.com/shadowofze/OfflineDAoC/pull/3)) |
| Dead gamebots stay where they died; a meetup member that dies 3 times is a no-show | [chris4aker/OfflineDAoC-SocialExpansion](https://github.com/chris4aker/OfflineDAoC-SocialExpansion), branches `offer-0.37/dead-bot-corpse` and `offer-0.37/meetup-death-cap` |
| `/spawn` healers hold position in a fight and don't walk away to heal or buff | [Nikse77/OfflineDAoC](https://github.com/Nikse77/OfflineDAoC), branch `mod-0.37` |
| A boss's self-recasting area spell no longer runs the server out of memory | [chris4aker/OfflineDAoC-LivingServer](https://github.com/chris4aker/OfflineDAoC-LivingServer) |
| `/tc` is only the Realm Exchange teleport | lometur/HearthDAoC |
| The siege expiry throttle, effects that never end, pooled navigation queries, a bot login handle leak, medallion merchant lag, resurrection offers, one world entry per client, Torst eddies, interrupt wait seconds, the siege weapon owner, case-free quest names | [chris4aker/OfflineDAoC-LivingServer](https://github.com/chris4aker/OfflineDAoC-LivingServer) v1.1-beta. The siege throttle was first found by stefanrows/OfflineDAoC, and the endless effects by rambodolphin13/OfflineDAoC. |
