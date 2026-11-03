# Proposed edits to `CV\runtime\true165\true165-rules.json` (spawns agent)

All four were dry-run together with the other spawns files on a copy of the snapshot (`work\s8_test.py`): no SQL error,
the second run changes nothing.

## 1. settings: disable the two unreachable / post-period regions (Task 3)

```diff
-    "disabled_regions": "27;165;234-242"
+    "disabled_regions": "27;165;233-242;244"
```

- **244 Passage of Conflict** (404 monster spawns: accursed/doomed brigands, Oar cult...): a New Frontiers-era (post-1.65)
  dungeon. Nothing in the world leads there: no `ZonePoint` has TargetRegion 244 and no other table refers to it. The bot
  code already treats it as not part of the 1.65 world (`AutonomousDungeonPolicy.cs`: "not the post-1.65 Passage of
  Conflict (244)"; it is not in `IsSharedFrontierDungeon` / `IsSupportedDungeonZone`).
- **233 Summoner's Hall (duplicate)**: 111 monster spawns (a second Grand Summoner Govannon, Aidon the Archwizard,
  Summoner Cunovinda/Lossren/Roesia, nodes...), but the region has **no `Zones` row** and **no `ZonePoint`** leads to it.
  The real Summoner's Hall is region 248 (zone 248; the three ZonePoints from Dodens Gruva 246, Hall of the Corrupt 277 and
  Marfach Caverns 276 all target 248; the bot raids use 248 `AutonomousRealmRaid.cs`).
- `233-242` replaces `234-242` (one range, `Util.SplitCSV(..., rangeCheck: true)` expands it). 243 (Kobold Undercity,
  Expansion 3 Catacombs) is not included; it has no spawns and is already off with the Catacombs expansion.
- Disabling (not deleting) keeps the rows, so it is reversible; `Region.IsDisabled` stops the region loading.

## 2. remove.mob_classes: give the Spectral Provisioner back his spawner

```diff
-      "DOL.GS.SpectralProvisionerSpawner": "Atlas event controller",
```

and install `fix\spawns\add-spectralprovisioner165.json` (the archived spawner row, Mob_ID
`fcc2f7d8-dd28-4161-be85-b4599442a94b`, Region 60 Caer Sidi at 30063, 40880, 17004, ClassType
`DOL.GS.SpectralProvisionerSpawner`, Guild "DO NOT REMOVE", Flags 28 (hidden, peace), PackageID set to `true165-restored`).

Why: the Spectral Provisioner is a real period Caer Sidi boss (period Allakhazam bestiary key 4224, capture 2003-10-03,
level 77, 8 Known Loot items; raids audit). In this server he exists only through that spawner
(`scripts\namedmobs\CaerSidi\SpectralProvisioner.cs`: the invisible spawner spawns the boss when a player comes within
500 units and no Provisioner is alive within 8000). With the spawner gone the boss and his 14-row loot template are dead
content. The rule's other "Atlas event controller" classes (Parthanan farm, Vetusta Abbey bell, Unnatural Storm, Blight)
stay as they are. **Both edits must go in together**: with the class rule kept, the add file would re-insert the row after
the rule archives it on every refresh.

## 3. remove.where: one roaming haunt in Nisse's Lair

Append the entry in `fix\spawns\where_rules.json`:

```json
{"table": "Mob", "key": "Mob_ID",
 "where": "lower(Name) = 'haunt' AND Region = 129 AND Mob_ID <> 'c8bc4a4a-ac59-46c7-9c10-4858d071fa84'",
 "reason": "1.65: Nisse's Lair has one roaming haunt (...): the 21 extra copies go"}
```

The kept row is the one nearest the CapnBry sightings (id 283, 10 sightings, zone 129). Its template level and respawn
are set in `updates.json`. No restored row is a Nisse's Lair haunt (checked), so the rule never touches restored spawns.

## 4. No other rule change is needed for the restored spawns

Every restored row (`add-restoredspawns165.json`, 16,838 rows) was checked against every remove rule in the current file:
- `mob_classes`: all restored rows are `DOL.GS.GameNPC` (not listed).
- `mob_packages`: their PackageID is now `true165-restored` (was Public_DB / NULL / QuestWorld / *Baf); `QuestWorld`
  no longer matches the 172 QuestWorld-package rows restored.
- `mob_names` (`new mob`, Apprentice Taxidermist, Consignment Banker, Sir Lukas, Gohanz, ...): no restored name matches.
- `mob_guilds` (`Item Merchant`): no restored row has a guild.
- `where` (Mob): siege merchants (`ItemsListTemplateID LIKE 'SiegeMerchant%'`), Hibernian scourge rats (Realm 3), and the
  new haunt rule: none match.
The dry run confirms it: 0 restored rows archived on either run.

## Install notes for the coordinator
- New data files for `runtime\true165\`: `add-restoredspawns165.json`, `add-npctemplates165.json`,
  `add-spectralprovisioner165.json` (only with edit 2).
- Append `updates.json` (158 entries) to `"updates"`. They are idempotent (absolute SETs keyed by NpcTemplate_ID /
  Mob_ID / name+FactionID=0). Mob updates exclude restored rows; the restored rows carry their fixed values in the add file.
- `add-*.json` runs after `updates`, so the Mob updates that point 4 groups at the new templates (`NPCTemplateID`
  916500001-916500006) run before those templates exist in the very first refresh; it is one transaction and nothing reads
  the templates until the server starts, so that is fine (dry run: no Mob points at a missing template afterwards).
