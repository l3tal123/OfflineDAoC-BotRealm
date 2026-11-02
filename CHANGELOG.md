# Changelog

The newest version is first.

Offline DAoC True 1.65 grew out of [Offline DAoC](https://github.com/shadowofze/OfflineDAoC) (0.3 to 0.37c, now on
hold). Its changelog has the history of everything this version inherits.

## Coming in 0.2

- Gamebots: bots buy and wield weapons of the line they trained, and dual wielders hold the trained weapon in their
  main hand.
- Gamebots: a bot that keeps dying with poor gear goes shopping for an upgrade it can afford, and bots on a shopping
  trip no longer stand at a merchant for 15 minutes.
- Gamebots and companions: strength buffs go to fighters and acuity buffs to casters.
- Gamebots and companions: a bot that levels shows its new level and con colour at once, and in the group window.
- Companions: you can give your own companions quest and no-trade items.
- Companions: a companion that dies shows as dead on your companion roster at once.
- Horses: stable horses ride over the bridges on every horse route (Prydwen Bridge, the bridge into Camelot and the
  rest) instead of through or under them ([#1](https://github.com/shadowofze/OfflineDAoC-True165/issues/1)). Still
  slightly buggy: crossing Prydwen Bridge, the horse can dip into the deck a few times, and in the new client horse
  rides rubber-band (the horse jumps back and forth along the route).
- Horses: a lag spike no longer throws you off a stable horse in the middle of a route (both clients).
- Horses: stable horses no longer rubber-band along their route.
- Horses: gamebots ride horses sized for their race, as players do.
- Companions: your hired companions come with you at once when you use a teleporter or release to your bind point.
- Items: merchant caster staves have their focus levels again (lower spell power cost), and about 470 items regain
  bonuses from their 1.65 descriptions (instrument and weapon skills, resists, staff focus).
- Quests: you can still hand in quest items after outleveling a quest you already have (for example the Necromancer
  quest Inconnu Poison).
- Companions: a companion whose bow is taken away in the middle of a pull switches back to its melee weapons.
- Controls: Mouse Look Toggle, as in the original client, can be switched on in Options > Controls (off by default).
  Once on, right-click on open ground to turn mouse look on (the mouse turns you with no button held), and right-click
  again or press Esc to turn it off. Right-clicking NPCs and objects still works as before, and the key can be rebound.
- Chat: Shift+Up and Shift+Down go through your last 10 sent lines, as in the original 1.64 client; Shift+Up with the
  chat line closed opens it with your last line. One minor convenience over the real 1.64 behavior: stepping past the
  newest line with Shift+Down brings back what you were typing (1.64 shows an empty line).
- Controls: /qbar works as in the 1.64 client: /qbar # shows quickbar bank # (1-10).
- Loading screens: the 1.64 client's classic loading bar and layout - the loading picture at its own size, the glass
  bar in its bottom right corner and "Loading..." beside it.
- Loot: dropped loot shows the 1.64 client's classic bag instead of the later Catacombs one.
- Death: one death window with the release timer, its bar (the 1.64 client's green bar art) and /RELEASE, /RELEASE
  CITY and /RELEASE HOUSE, all inside the window - the separate Release Timer window is gone, and /RELEASE BIND (the
  same as /RELEASE) was removed. Crafting, salvage and repair timer bars stay inside their window too.
- Items: the Destroy Item key (Shift+D) now works, as in the 1.64 client - right-click an item to select it, press
  the key and answer "Do you really wish to destroy this item?" (indestructible items, relics and worn items stay).
- Interface: window transparency, as in the 1.64 client - right-click any window (the chat boxes too) for the Alpha
  panel and its two arrows, or set ALPHA in Options > Interface; text and icons stay solid.
- Spells: Shift+right-click one of your own buff icons to cancel it.
- Items: a highlighted item in your backpack, vault or a merchant's list, and the right-click box, show the item's name
  in its con colour to you.
- Player housing, as in the 1.64 client and with its own models: the 12 period housing zones (Caerwent, Old Sarum,
  Rilan, Dalton; Erikstaad, Arothi, Carlingford, Wyndham; Meath, Dunshire, Torrylin, Kilcullen) through the realms'
  housing gates; buying a lot and building a house with a deed; the houses' roof, wall, door and wood materials,
  porch, guild banner and shield; the garden; going inside; placing, rotating and taking back furniture; hookpoints
  (vaults, bindstones, tools, house merchants); house vaults; friend permissions; paying rent by dropping coins on
  your house; and the 1.64 housing windows (External / Internal Options, Garden Placement, Friend Level Permissions,
  Permissions List, Rotate Object). Lots, deeds and rent cost the period prices, the decorators carry their full
  stock, stable routes run inside the housing zones, and hanging a weapon on a wall no longer leaves a copy in your
  bag. Checked offline; not yet played through on a live server.
- Launcher: the Stop button stops the server again (it could keep running).
- NPCs: Half Ogre, Frostalf and Shar NPCs (about 150 of them, General Vaul among them) draw with their own classic
  figures instead of broken.
- Companions: reviving a companion at a healer after it died while casting no longer breaks its spell effects
  (reported as a client crash when reviving at Epona in Mag Mell).
- NPCs: NPCs stored a little below their floor now stand on it, as the original client drew them - Gothwaite Harbor's
  dye merchants were sunk to the neck (GitHub issue #4).
- Camelot: the walls around the round table area no longer show up white, and the palace's closed double door by
  Guard Eraedus is back (GitHub issue #2).
- World: missing doors, doorways and doorsteps now show in Caer Sidi, Muspelheim, Yggdra Forest, Odin's Gate,
  Trollheim and Tur Suil, and a Darkness Falls tunnel has its textures again.
- World: sharper textures on the new traveler's inns (Avalon Marsh, West Svealand, Shannon Estuary) and on some grass
  (Iarnwood, Cothrom Gorge, World's End).

## 0.1 (2026-11-01)

The first version of True 1.65: Classic and Shrouded Isles as they were at patch 1.65 (1 November 2003). The only
additions to the period game are the bot and companion systems and the Realm Exchange. It is early in development:
most of the systems below have been tested in autonomous bot runs, but not yet by players.

### Compared with Offline DAoC

- **One mode:** True 1.65 only. The launcher has no normal mode and refuses a world that isn't a True 1.65 one.
- **One client:** the new client. The original DAoC client is not supported, and there is no DXVK.
- **A fresh start:** nothing transfers from Offline DAoC, and there is no progress importer.
- **No Sluaghbinder:** the custom class is gone.

### The world, as in 1.65

- **Freeshard and post-1.65 content removed.** Removed rows are archived in the world database, not just deleted.
  Removed:
  - click teleporters;
  - town and frontier hasteners (they came in 1.70 and New Frontiers);
  - Atlas master trainers, item merchants, the buff merchant, the Market Explorer, account vault keepers and
    training dummies;
  - Atlas event controllers, "new mob" placeholders and helper NPCs;
  - the random "ROG" item drops;
  - the Atlas daily, weekly and monthly quests (deleted from the source).
- **Battlegrounds** as in the period game: Abermenai (15-19), Thidranki (20-24), Murdaigean (25-29) and Caledonia
  (30-35). Cathal Valley, a post-1.65 battleground, is removed.
- **Regions off:** the tutorial, Cathal Valley, the duplicate Summoner's Hall, the New Frontiers battlegrounds,
  Passage of Conflict and every Trials of Atlantis region.
- **Period drops:**
  - rebuilt from Uthgard's measured drops, period Allakhazam loot lists and period item pages, for about 1,950
    monsters;
  - 886 freeshard and dead loot rows removed: Atlas XP items, orbs, trophies, timed drops and items no source knows;
  - 1,334 period items built from their period pages;
  - the 8 period boss trophies stay (they came with housing in June 2003).
- **Kill tasks** (turning in monster items for XP) pay the period rewards: a fixed XP per item, and the period
  turn-in cap of 15 below level 40 and 25 for the level 40+ bounties (before: the freeshard 1 level per item, and 30).
  143 of the 227 values come from period pages; the other 84 are estimated from them. The freeshard turn-ins with no
  period source are removed: the level 11-19 "Dr" turn-ins, a stand-in for the Ghoul Hair Belt quest, a duplicate
  Giant Snow Crab Claw task, and the 10 items only they used.
- **Kill-task items drop again:** 140 kill tasks with dated period evidence have their item back on the monster that
  dropped it, at Uthgard's measured rate where known, otherwise 6% (the median Uthgard kill-task rate). 181 of the
  227 kill tasks can be completed.
- **Coin as in 1.65:** a monster carries coin or it doesn't. The chance comes from Uthgard's measured kills, and the
  amounts are unchanged.
- **Item stats:** about 7,200 corrected to the period item pages. Invented quest rewards were replaced by the real
  items.
- **Spawns:** 16,292 period spawns restored and 453 period spawns added. Spawns off the walkable ground were moved
  onto it. Levels and factions were corrected where two sources agree.
- **Merchants:** period stock from the period Allakhazam item and merchant pages (1,458 stock rows, 8 new lists), and
  7 missing period merchants added where CapnBry's radar saw them.
- **Crafting:**
  - the recipe list is the 1.64 client's (about 380 later recipes removed);
  - skills are per character;
  - no speed bonuses; quality is 94-99%; hand tools are needed; salvage gives the full yield;
  - frontier siege equipment is crafted (Siegecraft), not sold.
- **Travel:**
  - the Shrouded Isles entrances are the 1.64 client's jump boxes (Avalon Marsh, Gotar/Nalliten, Shannon Estuary);
  - the Darkness Falls entrances are the period ones;
  - frontier portal masters walk between their post and the pad;
  - horse routes follow bridge decks and dips;
  - there is no personal bind recall stone.

### Rules for players

- XP is 1x, with no free levels and no `/level`.
- Coin belongs to each character, not to the account. Each realm has 8 character slots.
- Base classes at creation; you choose your class at a trainer at level 5. A starting class and race with no class to
  promote to is refused.
- New characters start with a weapon and a training shield.
- No autoloot. Songs need their own instrument (drum, lute or flute).
- Charm is cast on a real target, with the original Dawn of Light level limit and resist rules. A freed charmed
  monster turns on its charmer.
- Cheat and convenience commands are for GM accounts only. That includes `/tele`, `/tc`, `/grind`, `/level`,
  `/mobs`, `/buff`, `/raid` and `/spawn`, and the launcher's "Teleport to" on a bot.
  [ALL SERVER COMMANDS.txt](ALL%20SERVER%20COMMANDS.txt) has the full list.
  - `/switch`, `/sell`, `/craftmacro` and `/faction` are GM only too: neither the 1.64 client's command help nor
    the DAoC Manual lists them. `/transfercorpse` is in both, so players keep it.
- No player horses: horses, horse vouchers, horse armour and barding are off every merchant. Horse travel is by the stablemasters'
  routes.
- `/task` follows the period rules: kill targets 3 levels below you to 1 above, period task XP, and plain guards say
  who gives tasks.
- The quest markers, the objective highlights and the quest guide (`/questguide`) stay.

### The traveler's inns (new)

- One inn per realm: near Caer Witrin (Albion), near Huginfell (Midgard) and near Connla (Hibernia). Aggressive
  monsters were moved away from the inns and the roads to them.
- The **innkeeper** hires companions.
- The **Horn of Rallying:** a level-50 player who has done its courier quest starts a PvE realm event there (a dragon,
  an epic dungeon, Summoner's Hall or Darkness Falls). PvE events never start by themselves; keep sieges and relic
  raids still do.
- The **Realm Exchange**, the auction house where gamebots and players buy and sell to each other, moved from the
  capitals to the inns. Valuable items ask more, within limits. Unsold bot
  listings are relisted at 90%, then 80%, then returned.
- Couriers at levels 5 and 50 bring the inn and horn quests, with map markers. Road patrols, wandering arrow and
  poison traders, and guards complete the inns.

### Hired companions (new; they replace `/spawn` and `/raid` for players)

- Hired at the innkeeper. Slots open at levels 5, 10, 15, 20, 30, 40 and 45. You choose the race, class, gender,
  look, height and name.
- They are persistent: they log in and out with you, ride your horse routes with you, and arrive where you are.
- You manage them in their own windows:
  - gear, with a 10-slot bag;
  - spec training, with a free respec;
  - realm abilities, stats and XP.

  A smith repairs their gear.
- They stop at your level, never loot, heal your pet, and talk about what is going on. A healer revives them for
  free.

### Gamebots on the player rules

- Bots use the player rules for crit, skill, resists, crowd control, speed, procs, dual wield, focus, concentration
  and regeneration.
- **Armour works like a player's.** Before, a bot's armour did nothing when it was hit.
- They start with their class's period starting weapon only, buy their armour from vendors when they can afford it,
  and plan trips for upgrades. A bot that walks to the merchant its plan names is served there at once, and it
  wields a weapon it buys straight away.
- Arrows, bolts and poisons come from their bags and are restocked on town errands.
- Their gear wears and is repaired, for coin, by a smith or by crafting.
- **Deaths cost them:** Constitution loss on a PvE release (once per death, at most 3 points), resurrection sickness,
  and a healer visit or a wait.
- They sit to rest at the player rates, and walk to their loot bags.
- They start as a base class and are promoted at a trainer at level 5.
- About 1 in 5 bots craft: they make real recipes, gain skill, and list what they make on the Realm Exchange.
  Siegecrafters supply the siege crews.
- Camps and pulls:
  - solo bots pull from range with a bow, a throwing weapon or a spell. They move into range and line of sight
    first; if a shot is refused as out of range or not visible, they step closer and try again, and after 4 tries
    they pull in melee. A shot that hits, misses or is resisted always alerts the monster, so it comes for the
    bot (before, many ranged pulls went unnoticed). A monster's AI used to wake only for a player who could see it,
    or a bot inside its aggro radius, so a bot shooting from range drew aggro the monster never acted on; now the
    monster comes for the bot, and the bot waits for a monster that is on its way. A monster that still takes no
    notice within 3 seconds is pulled the ordinary way;
  - camps are chosen by challenge colour;
  - grey targets are a last resort;
  - outleveled camps are left alone for 30 minutes.

### Also in Offline DAoC

These were built during this work and are shared with Offline DAoC.

- **Relic raids:**
  - the army goes to the shrine;
  - a chosen carrier runs home through its own gates;
  - sentinel keeps are taken first;
  - campaigns survive a restart.
- Solo bots pick up their own coin and loot again.
- Dead bots stay where they died. Gamebots face their melee target.

### The new client

- The period 1.64 pre-game: Choose Your Realm, the standing stones, the one-page creation with stat points, height,
  face and hair, and the summary page.
- The classic character figures and looks, and the 1.64 spell table.
- The companion windows: hire, roster, gear and train, with baseline spells.
- The Horn of Rallying window. The inns on the map, with their markers and turn-in marks.
- Models numbered past 2047. Riders seated on stable horses. Bots sitting to rest. Fighters facing their target.
  Objects that stay put across zone borders.
- The window title is "Offline DAoC True 1.65 v0.1".

### The launcher

- Fixed to True 1.65. START SERVER applies the world content and keeps every save. One ENTER REALM button starts the
  new client.
- A fresh install makes its own local login, so ENTER REALM works the first time and the server creates the account.
- Removed:
  - the XP rate controls;
  - the level-50 bot buttons (bots are added at level 1);
  - forced realm events (PvE events start from the Horn of Rallying);
  - the Realm Exchange tab (the Exchange is in game only);
  - the Reset Keeps & Relics button.

The known issues in v0.1 are listed in the [README](README.md#known-issues-in-v01), and the fixes taken from
community forks are credited in [THIRD_PARTY.md](THIRD_PARTY.md#fork-credits).
