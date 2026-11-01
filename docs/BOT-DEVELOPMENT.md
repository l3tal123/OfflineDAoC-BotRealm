# Bot subsystem: architecture map, capabilities and gaps

This is the fork's working document for the bot system. It describes how a bot thinks and acts today, what the
system can already do, and where the real weak points are. Read
[AGENTS.md](../AGENTS.md) and [DEVELOPMENT.md](DEVELOPMENT.md) first; the rules there are binding.

**How this was produced.** A read-only audit of the source tree at commit `d9c235c` (2026-11-01): files were
read, nothing was built, no server was started, no source was changed. Claims are tied to a file or class;
anything I could not confirm from code is marked "uncertain". This document adds no content and changes no
numbers. Measured size: `GameServer/bots` 68 top-level C# files (~17.8k lines),
`bots/autonomous` 163 files (~38.2k lines), `bots/specs` 43 files, `mode165` 27 files (~5.2k lines); ~100 bot
fixtures under `Tests/UnitTests`.
Updated 2026-11-01 after the Living Server port: section 1.12 and the affected capability/gap rows describe the
ported behaviour-quality layer and player group control; the server builds and the NUnit suite passes
(3,099 passed / 0 failed / 69 skipped), but none of the ported behaviour has been checked in a live game yet.

## 1. Architecture map

### 1.1 Three kinds of actors (keep them apart - AGENTS.md rule 9)

| Kind | Created by | Persistent? | Runs world AI? | Key code |
|---|---|---|---|---|
| Autonomous gamebot ("world bot") | Launcher ADD BOTS / `OfflineDAoC.exe --add-bots 1.65 <realm> <count> <level>` | yes, `offline_world_bots` | yes | `GameBot` with `IsAutonomousWorldBot`, driven by `AutonomousWorldBotController` |
| Hired companion | Innkeeper window (`Hire165`) at the traveler's inns | yes, `offline_hired_companions` | no; player-led | `HiredCompanions165`, `CompanionWindows165`, `GameBot.IsPlayerLedGroup` |
| Temporary companion | `/spawn` leader/party helpers | no | no; player-led | `GameBot.IsTemporaryGroupHelper`, `TemporaryCompanion*` |

`/spawn` and `/raid` are GM-only in True 1.65 (`Classic165Mode.GmOnlyCommands`), so temporary companions are a
GM/dev path. `BotManager` (max 15 player-owned bots) and `BotCommand` are inherited from the older bot system.
`BotCommand` has no `[CmdAttribute]` and `ScriptMgr` only registers attributed types, so `/bot` does not exist;
its body only toggles `AutonomousPlayerPilot` (auto-play for a real player character), which therefore has **no
live caller** and is effectively dev-only code.

### 1.2 Lifecycle and entry points

- **Login population:** `AutonomousPopulationController` (`OnServerStarted`) polls `offline_world_bots` once a
  second, computes a target with `AutonomousPopulationRamp`, picks the next realm by
  `AutonomousRealmLoginBalancer`, and spawns real actors. `AutonomousBotRegistry` is the live set.
  `AutonomousPopulationProperties` holds the gates: `population_enabled`, `active_only`, `startup_ramp_minutes`,
  `ai_budget_ms`, `world_actor_only`, `allow_character_deletion`.
- **Brain attach:** `GameBot` gets a `BotBrain` (`bots/BotBrain*.cs`, `partial`; 5.5k lines in the main file).
- **Per-tick order in `BotBrain.Think()`** (`bots/BotBrain.cs:1286`): companion buff/engagement watch ->
  poison supply -> animist target restore -> stealth -> pet focus shield -> stuck watchdog -> stable ride (owns
  movement) -> hired-companion leash -> frontier threat -> pull holds -> pet/necro holds -> pull coordinator ->
  buffs/songs -> pet upkeep -> merchant -> Realm Exchange -> **`AutonomousWorldBotController.Tick(this)`** ->
  `FSM.Think()`.
- **Order in `Tick()`** (`bots/autonomous/AutonomousWorldBotController.cs:255`): pocket escape -> RvR release ->
  group pulse -> assignment reset -> death observation -> release-return -> stable travel -> relic carrier ->
  shared-dungeon PvP -> siege hold -> combat (defense wins) -> **loot walk (`BotLootWalk165`)** -> stale DF
  evacuation -> group cohesion/dungeon entry -> group pull recovery -> solo ranged pull -> solo death recovery ->
  recovery rest -> casting hold -> raid travel -> frontier return -> capital transit -> expedition travel ->
  relic custody -> `SelectCamp` -> `TravelAcrossRegions` -> rendezvous -> objective dispatch (`ExecuteRvr`,
  between-task services, `ExecuteBattleground`, `WorkCamp`).

So the effective decision order is: **goal/assignment -> services if overdue -> camp -> travel -> combat ->
loot -> rest -> next camp**. Combat and movement are ordinary world systems; the controller only plans.

### 1.3 Goals and assignments

- `AutonomousObjectiveAssignments` owns durable work: `eAutonomousObjectiveKind`
  {SoloPve, GroupPve, RvR, Battleground}, tenures of 45-120 min, a 20-minute group-matchmaking timeout,
  and the "between tasks" service phase (`between-pve-services-T/I/D-...`) for trainer, vendor/exchange and a
  15-30 min town break.
- `AutonomousBotGoalPolicy` + `BotGoalSettings` (`bot-goals.json`): solo/group/RvR/battleground percentage
  weights per level band (1-19, 20-49, 50). Read **once at server start**
  (`Classic165Mode.OnPropertiesLoaded`); `Levels1To19` cannot have RvR and `Level50` cannot have battlegrounds
  (`BotGoalSettings.Validate`).
- `AutonomousBotDecisionEngine` is the pure policy seam (activity choice, environment choice, camp drawing,
  rest thresholds, formation, group lifecycle).

### 1.4 Camps and population spread

`AutonomousWorldBotController.SelectCamp` builds the candidate set from `BuildCampCatalogDraft`:
CapnBry-verified Classic/SI locations (`AutonomousCapnBryGoalCatalog`, embedded
`capnbry_classic_si_goals.json`) intersected with **live** NPCs, plus period-restored spawns
(`AutonomousClassic165RestoredSpawnCatalog`), battleground clusters, verified dungeon camps and starter camps
for low levels. Filters: realm, reachability, zone access, level/con ceilings per group size
(`MaximumTargetCon`), `AutonomousSoloCampFloor`, `AutonomousCampLevelPolicy`, `AutonomousAuditedCampPolicy`,
`AutonomousCampRouteQuarantine`, `AutonomousCampDangerBench`, `AutonomousCampCompetition`. Final draw is
weighted by live spawn count (cap 6) within dungeon/outdoor environment chosen by
`AutonomousBotDecisionEngine.SelectPveEnvironment`.

### 1.5 Groups

`AutonomousBotGroupCoordinator` (+`.ForcedRaid`, `.SiegeParties`) forms parties up to 8 from the live roster,
assigns roles (`BotPartyRoles`: Tank/Healer/Buffer/Attacker; `BotPveGroupRole`), plans a shared camp, meets at
the nearest safe town, publishes the camp to members, keeps formation (`AutonomousGroupFormationFollow`,
`AutonomousGroupPace`, `AutonomousGroupMotion`), and handles wipes/regroup/resurrection
(`AutonomousGroupRecoveryState`, `AutonomousGroupMemberStall`, `AutonomousGroupTaskClock`).

### 1.6 Travel and navigation

`TravelAcrossRegions` picks the next `DbZonePoint` crossing, then `AutonomousZoneRoads`/`Seams`/`Pockets`/
`BoundaryRouting`/`Itinerary`/`PointApproach` for on-foot routes, `AutonomousStableRoutePlanner` +
`HandleStableTravel` for horse tickets, `AutonomousTownTeleporters` for the realm's free `LiveTeleporter`
(server property `bot_use_town_teleporters`, on by default), and `AutonomousCapitalTransit`,
`AutonomousFrontierTransport`, `AutonomousRealmRaidStableTravel` and `ShroudedIslesPortals` for portal pads.
Threat and recovery: `AutonomousThreatAwarePathing`, `AutonomousRouteThreatPolicy`, `AutonomousDefensivePull`
(groups), `SoloRangedPull165` (solo), `AutonomousRouteRecoveryPolicy`, `AutonomousRouteHotspotRepair`,
`AutonomousCorridorRecovery`, `AutonomousStuckWatchdog` (15 min no movement, 45 min no progress -> capital
recovery).

### 1.7 Combat, support, pets

`BotBrain` partials: `AreaSpells`, `CrowdControlRespect`, `PvpCrowdControl`, `PveAddControl`, `TankPeel`,
`BardPveCrowdControl`, `ShamanPveRoot`, `PetFocusShield`. Class policies: `BotRangedCombat`,
`BotMeleeStylePolicy`, `BotSpellPower`, `BotAoePolicy`, `BotCasterPriority`, `BotSongTwistPolicy`,
`BotBuffReservations`, `BotSupportReach`, `BotPartyRoles`, `SavageBotCombatPolicy`,
`ShamanBotCombatPolicy`, `MinstrelBotCombatPolicy`, `BardBotCrowdControlPolicy`, `SluaghbinderBotPolicy`.
Per-class specs live in `bots/specs/<Realm>/<Class>.cs`. Realm abilities:
`AutonomousBotRealmAbilities` (trains a fixed archetype build at the class trainer; actives Purge, Ignore
Pain, Second Wind, First Aid are used).

### 1.8 Loot, rest, economy

- Loot: `BotLootWalk165` (no autoloot; walk to your bags), `AutonomousLootFlow` tests, `AutonomousRealmRaid`
  loot ledger for raids.
- Rest: `BotRestRecovery` + `AutonomousRestPolicy` (health 70 / power 45 / endurance 35 thresholds; recovery
  to full once seated).
- Economy: `AutonomousBotEconomy` (coin from `offline_world_bots`, vendoring, equipment upgrades, Realm
  Exchange buy/list, Darkness Falls seals), `AutonomousAuctionValuation`, `Exchange165` (90%/80% relists),
  `AutonomousBotGuilds`, `AutonomousBotChat`/`AutonomousBotChatCoordinator` (cosmetic only).

### 1.9 RvR

`AutonomousRvrDirector` chooses Regroup / HuntEnemy / DefendKeep / AssaultKeep from live objectives; keep
assault requires `AverageLevel >= 40`, `MemberCount >= 5`, `HealerCount >= 1` (`CanAttemptKeep`).
`AutonomousRvrEventLayer(.Defense/.Relic)` runs forced/automatic events, `AutonomousRvrRelicPersistence` saves
relic campaigns to `rvr-relic-campaigns.json`, `AutonomousRelicShrine`, `AutonomousSiegePolicy`,
`AutonomousSiegeJobs`, `AutonomousSiegeOwnership`, `SiegePlacement`, `BotSiegeRuntime`,
`AutonomousBattlegroundWave` (`BattlegroundBrackets`, levels 15-35). `AutonomousRealmRaid` defines 12 PvE
realm events: 3 dragons (Golestandt, Gjalpinulva, Cuuldurach), 3 epic dungeons (Caer Sidi, Tuscaran Glacier,
Galladoria), 3 Summoner's Hall and 3 Darkness Falls (Legion) expeditions.

### 1.10 The True 1.65 rules layer (`GameServer/mode165`)

| File | Role |
|---|---|
| `Classic165Mode.cs` | `IsOn`, XP locked 1x, `state-165` paths, travel time x1.5, GM-only commands, inn regions, `CLICK_165` |
| `PlayerRules165.cs` | `IsPlayerLike` / `IsPlayerRulesBot` - bots get player combat rules |
| `DeathPenalty165.cs`, `Supplies165.cs`, `ItemWear165.cs`, `TownErrands165.cs`, `BotLootWalk165.cs` | con loss/sickness/healer errand; real arrows/bolts/poisons and `EnsurePullWeapon`; gear wear + paid repair; town-trip reasons (one per 30 min); no autoloot, companions never loot |
| `SoloRangedPull165.cs`, `BaseClassBots165.cs`, `Charm165.cs`, `Coin165.cs`, `CrafterBots165.cs` | solo ranged pull; base class -> promotion at 5 (`PlannedClassId`); charm only with a learned spell; per-monster coin chance; ~1 bot in 5 crafts and lists real recipes, siegecrafters build kits |
| `Hire165.cs`, `HiredCompanions165.cs`, `CompanionWindows165.cs`, `CompanionGearVault165.cs`, `CompanionUiCommand.cs`, `CompanionChat165.cs` | companion hiring, windows (DebugMode 0x21 'H'), gear, persistence |
| `Innkeeper165.cs`, `HornHandler165.cs`, `Adventurer165.cs`, `Courier165.cs`, `InnQuestMarkers165.cs` | inns, Horn of Rallying, road patrols, courier quests |
| `Exchange165.cs`, `Epics165.cs`, `ClassicFigures165.cs`, `TestKit165.cs` | exchange rules, level-50 epic finales (player), figures, local test characters |

Relevant log tags: `BOT_DEATH_165`, `BOT_HEALER_165`, `BOT_RESTOCKED_165`, `BOT_REPAIRED_165`,
`BOT_CRAFTED_165`, `BOT_PROMOTED_165`, `BOT_PULL_WEAPON_165`, `COMPANION_*_165`, `CLICK_165`.

### 1.11 Persistence and UI surfaces

`offline_world_bots` (server: `AutonomousBotStatusPersistence`, `GameBot.SaveToDatabase`) is the roster the
launcher dashboard reads; `offline_bot_commands` (launcher `OfflineBotCommandRecord`) is polled by
`AutonomousPopulationController` (today only `TeleportToBot`); `offline_hired_companions` is server-only. State
files under `state-165/`: `bot-goals.json` (launcher writes it with the server stopped, read at start),
`bot-world.json` (dashboard -> launcher Active Population / Groups), `rvr-world.json`,
`rvr-relic-campaigns.json`, `realm-event-records.sqlite3` (launcher RvR / Battlegrounds / Realm Events tabs).
Request files (`bot-world.request`, `realm-events.request.json`, `bot-ai-delay.json`) stay in the server folder.

### 1.12 Behaviour-quality layer (ported from the Living Server reference, 2026-11-01)

The reference's behaviour-quality fixes are now in the fork. All of them only change bot *choices*; none changes
a combat, XP or monster number, and each has its own `ServerProperty` switch.

| File | What it does |
|---|---|
| `AutonomousRouteGoalFix.cs` | Snaps an off-mesh goal onto its zone's floor after a path failure, budgets the floor step, remembers failed goals/crossings for six minutes, and skips the capital relocation when the goal, not the bot, is at fault. |
| `AutonomousPveBossDanger.cs` | Remembers where a monster 10+ levels above the victim killed a bot and keeps camps and routes away from it (six hours). |
| `AutonomousRepeatDeathSpots.cs` | Remembers the bot's own three deaths within 1,200 units in three hours and avoids that spot. |
| `AutonomousPveDangerBypass.cs` | Bends solo leveling routes around red/purple monsters and remembered danger spots. |
| `AutonomousKeepGateGoals.cs` | Stops offering goals inside a keep courtyard the bot may not enter through a closed outer gate. |
| `SiegeDefenseBalance.cs` | Steers the bot-siege defense share by the last results so sieges stay contested. |
| `BotIncomingFireReaction.cs`, `BotBrain.IncomingFire.cs` | A bot hit by a player-shaped attacker reacts after 0.3-2.5 s (by hit kind, tank/caster, familiar/unseen enemy); a distant area splash is not a personal call and does not pull a bot off the mob it is fighting. Monster hits still aggro immediately. |
| `BotQuietSongs.cs` | Repeated song twists restart without playing the cast sound (the first start and harmful pulses stay audible). |
| `AutonomousDeathDiagnostics.cs` | `BOT_DEATH_165` now carries the killer kind and the danger-memory outcome, and is the single place that feeds the two danger memories above. |
| `AutonomousDungeonBossGoals.cs` | Optional (default **off**): a party that ground a dungeon camp for five minutes may take that dungeon's named boss once per task when it is a real data-quest kill target or has its own drop table. The camp filter still avoids quest monsters. |
| `AutonomousRaidHothead.cs` | Optional (default **off**): one raid in twenty has a raider who charges the final encounter before the muster is complete (period-neutral rewrite of the reference's Leeroy beat). |
| `AutonomousBotSchedule.cs` | Optional (default **off**): session or clock-based online windows, short breaks and rare linkdead freezes for autonomous bots. |

Player-side group control is in `BotGroupInvite` + `Group`: `/invite <name>` reaches a nearby world bot (6,000
units, same region), a bot on duty refuses with a reason (dead, siege, relic, realm-event force, realm raid, stable
route, temporary helper), and bots a player brought into their group are released when that player leaves the
group instead of following them from outside it.

## 2. Current capabilities

| Capability | Where | State |
|---|---|---|
| Level 1->50 at 1x XP, base class -> promotion at 5, spec/RA training, gear buying | `BaseClassBots165`, `HandlePendingTraining`, `AutonomousBotEconomy`, `BotEquipment` | Implemented |
| Grinding solo and in 8-man parties with roles | `AutonomousBotGroupCoordinator`, `SelectCamp` | Implemented, heavily patched |
| Dungeons incl. DF (solo 25+), battlegrounds 15-35 | `AutonomousDungeon*`, `AutonomousDarknessFalls*`, `BattlegroundBrackets` | Implemented |
| RvR: roams, keep defense/assault, relic campaigns | `AutonomousRvrDirector`, `AutonomousRvrEventLayer` | Implemented; keep assault needs 5+ bots, avg level 40, a healer |
| PvE realm events: dragons, epic dungeons, Summoner's Hall, DF Legion | `AutonomousRealmRaid` | Implemented (raids of 8-person parties) |
| Travel: foot routing, horse tickets, town teleporter, SI portal pads | see 1.6 | Implemented |
| Economy and loot: vendor, repair, ammo/poison, crafting, Realm Exchange, DF seals, walk-to-bag loot | `AutonomousBotEconomy`, `CrafterBots165`, `BotLootWalk165` | Implemented |
| Death: con loss, sickness, healer visit, release/return; rest at player rates | `DeathPenalty165`, `BotRestRecovery` | Implemented |
| Chat, guilds, map markers, dashboard | `AutonomousBotChat*`, `AutonomousBotGuilds`, `AutonomousBotMapMarkers`, `AutonomousBotDashboard` | Implemented (cosmetic/QoL) |
| Hired companions: hire, summon, gear, spec, RA, repairs, revive | `Hire165`, `CompanionWindows165` | Implemented |
| Quests / tasks (`/task`, classic quests) | - | **Not implemented for bots** |
| Epic quests (Lord of Deceit, Symbol of the Broken, Last Heir) | `Epics165` (player helpers only) | **Player only** |
| Behaviour quality: route give-up fix, danger/death-spot memory, danger bends, keep-gate goals, incoming-fire reaction, quiet song twists, siege-defence steering, richer death diagnostics | see 1.12 | Implemented (each has a switch) |
| Player control: invite a nearby world bot by name or target (duty refusal), bots released when their leader leaves the group | `BotGroupInvite`, `Group.GamebotsLedBy`/`ReleaseGamebotsLedBy`, `commands/playercommands/invite.cs` | Implemented |
| Housing, player<->world-bot trade, giving a world bot an item, tasking an individual world bot | - | **Not implemented** |
| Dungeon boss goals, raid hothead, bot schedule/breaks | `AutonomousDungeonBossGoals`, `AutonomousRaidHothead`, `AutonomousBotSchedule` | Implemented, all default **off** |
| Boats | - | Not in the bot travel layer (uncertain whether any boat route exists in the 1.65 world) |

Partial / thin areas: low-level survivability (README known issue: levels 6-9 die a lot); support bots hold
combat while only their pet fights (README known issue); a player can now invite a nearby world bot into their
group, but bots still never initiate aid or react to player requests beyond chat; item drops on ~2,100
coin-only monsters (content gap, not bot code).

## 3. Gaps and weak points (verified in code)

1. **No quest layer at all.** There is no quest code under `bots/` (searched `GameTask`, `AbstractQuest`,
   `DataQuest`, `RewardQuest`; only `BotDummyPacketLib` stubs), and the camp catalog deliberately *avoids* quest
   monsters (`AutonomousAuditedCampPolicy.cs:78` excludes `Quests.ClassicQuests.IsQuestMonster(npc)`). Bots can
   never complete classic quests, epic quests or the `/task` system, so they cannot "finish the game" in the
   quest sense.
2. **Epic finales are player-only.** `mode165/Epics165.cs` is a helper set for the three level-50 finales; the
   bots only ever meet those encounters as raid bosses (`AutonomousRealmRaid`).
3. **Player <-> world-bot interaction is only partly there.** Since the Living Server port a player can invite a
   nearby autonomous bot into their group: by target, or by name within 6,000 units in the same region, with a
   duty refusal (dead, siege, relic, realm-event force, realm raid, stable route, temporary helper), and a bot the
   player brought along is released when that player leaves the group instead of following from outside it.
   Still missing: trade windows are `GamePlayer`-only (`gameutils/PlayerTradeWindow.cs`), so a player cannot give a
   world bot an item, and there is no way to task or steer one world bot. Companion gear moves through a vault
   window (`CompanionGearVault165`) instead.
4. **No per-bot steering, and companion management is in-game only.** The launcher shows population, groups, the
   live world map (with bot selection), goals and RvR records, and still offers "Teleport to" (GM only,
   `MainForm.cs:1215`) and "Delete" (`MainForm.cs:1204`), but there is no in-game command to tell one world bot to
   switch goal, hold or regroup. Hired companions are not listed by the launcher at all (`MainForm.cs:2845`), so
   they are managed one at an inn or with `/companionui`.
5. **Goals need a restart.** `AutonomousBotGoalPolicy.Initialize` runs once from
   `Classic165Mode.OnPropertiesLoaded`; `BotGoalSettings.Save` refuses to run while the server is up. The
   launcher's "Bot Goals Setting" tab therefore means "set, then restart".
6. **Bot-rules parity is only partly migrated.** `PlayerRules165.IsPlayerLike` has 32 call sites, while the
   server still contains ~750 `is GamePlayer` checks (`GameServer`), 46 of them inside `bots/`/`mode165/`.
   Many of those are legitimately about real players (clients, chat, pilot), but shared rules code
   (resist/stat/skill/effect calculators, spell handlers) is exactly where the original parity audit found
   bugs; there is no test or grep gate that keeps the list from regrowing.
7. **Death/failure recovery used to be watchdog-only; the ported route fixes reduce, but do not remove, that.**
   `AutonomousStuckWatchdog` (15 min no movement, 45 min no progress) still teleports a bot to its capital
   (`AUTONOMOUS_STUCK_15M_RECOVERY`, `AUTONOMOUS_GOAL_STALL_45M_RECOVERY`). The ported goal-snap, failed-goal
   memory and pocket-escape rules (1.12) handle most of what used to reach the watchdog, but it remains the last
   resort and hides any remaining route/camp bug from the dashboard.
8. **Route/level reliability depends on hand-authored data.** Camps need CapnBry coordinates *and* a live NPC
   nearby; zones CapnBry lists as empty fall back to 1,500-unit live clusters
   (`BuildCampCatalogDraft`); dungeons use `dungeon_navigation_points.json`; DF uses proof files under
   `bots/autonomous/data/`. Any gap there silently removes camps.
9. **Think budget and fidelity tiers trade detail for CPU.** `AutonomousFidelityPolicy` spends 500 ms combat
   interval far from humans vs the combat baseline near a human, and `AutonomousAiBudget` slows background
   planning as the population rises. Behaviour far from the player is coarser by design.
10. **Legacy code is still in the tree and misleading.** `BotManager` (15 player-owned bots, `/bot` flow),
    `BotCommand` (not registered), `BotsUML.md` (describes the older design, no autonomous layer), and
    `docs/history/*` (September 2026 reports about earlier designs). A new contributor can easily follow the
    wrong architecture. (`BotsUML.md` is still accurate for `GameBot`/`BotBrain`/spec/equipment.)
    `BotDummyPacketLib` also throws `NotImplementedException` for ground-target, LOS-check and raw-message
    sends; I found no live bot path that calls them (uncertain whether they are reachable).

Known issues already documented by upstream (not re-derived here): low-level deaths, gear wear pace, 2,100
coin-only monsters, DF seal stock, support-pet combat, login panel. See `README.md` "Known issues in v0.1".

## 4. Developer entry points

**Build / test (from `development-source/server`)**
- `dotnet build "Dawn of Light.sln" -c Release`
- `dotnet test Tests/Tests.csproj -c Release --no-build` (~3,200 NUnit tests; ~100 fixtures cover bots)
- single fixture: `dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~UT_AutonomousBotDecisionEngine"`
- Never build while a server runs (it stalls the game loop, see AGENTS.md rule 10).

**Headless harness**
- Add a roster: `OfflineDAoC.exe --add-bots 1.65 <realm 1-3> <count> <level>` (writes `runtime/data/add-bots.log`);
  same rows as the launcher's ADD BOTS. Content refresh only: `OfflineDAoC.exe --refresh-165`
  (`runtime/data/true165-refresh.log`).
- Run by hand: `cd runtime/server && CoreServer.exe --start -config=./config/serverconfig-165.xml`, type `exit`;
  the launcher's console log is `runtime/logs/server-console.log`.
- Test-only overrides: `OFFLINE_DAOC_BOT_GOALS` (scratch `bot-goals.json`, logs `BOT_GOALS_OVERRIDE`),
  `OFFLINE_DAOC_BOT_TELEPORTERS=1` (town teleporters on). `mode165/TestKit165.cs` makes local test characters
  only when `data/true165-test-characters.json` exists (never shipped).

**State files (`runtime/server/state-165/`)**
`bot-goals.json` (weights), `bot-world.json` + `bot-world.request` (dashboard snapshot/refresh),
`rvr-world.json` (RvR objectives), `rvr-relic-campaigns.json` (relic campaigns), `realm-event-records.sqlite3`
(event history); `bot-ai-delay.json` (BOT AI DELAY card) and `realm-events.request.json` sit in the server
folder instead. Paths come from `Classic165Mode.StatePath`/`StateDirectory`.

**Settings**
- `ServerProperty` / `runtime/true165/true165-rules.json`: `classic165_mode`, `population_enabled`,
  `active_target`, `startup_ramp_minutes`, `ai_budget_ms`, `bot_use_town_teleporters`.
- `offline_population_settings` (`PopulationEnabled`, `ActiveTarget`, `HardActiveCap`) and
  `offline_local_options` (`MakeMeGM`): written by the launcher, polled live by the server.
- `AutonomousPopulationProperties` and `AutonomousDiagnosticsProperties` document every bot property;
  `bot_goal_attempt_tracking`, `bot_equipment_upgrade_tracking`, `bot_training_tracking` default to on.
- Ported behaviour switches (see 1.12): `bot_incoming_fire_reaction` (true), `bot_route_goal_fix`,
  `bot_pve_boss_danger`, `bot_repeat_death_spots`, `bot_pve_danger_bypass`, `bot_keep_gate_goals` and
  `rvr_siege_defense_steering` (true), `bot_dungeon_boss_goals`, `bot_raid_hothead`, `bot_living_breaks` (false),
  `bot_schedule_mode` ("off"). The group-duty rules need no switch.

**Useful log tags (grep the server console log)**
- Lifecycle: `WORLD_MODE`, `AUTONOMOUS_STATUS_PERSISTENCE`, `AUTONOMOUS_BETWEEN_TASK_TIMEOUT`,
  `AUTONOMOUS_GOAL_STALL_45M_RECOVERY`, `AUTONOMOUS_STUCK_15M_RECOVERY`, `AUTONOMOUS_XP_PROGRESS`.
- Camps/travel: `AUTONOMOUS_CAMP_UNREACHABLE_PULLS`, `AUTONOMOUS_CAMP_ROUTE_QUARANTINE`,
  `AUTONOMOUS_CAMP_DANGER_BENCH`, `AUTONOMOUS_ROUTE_STALL_TRACE`, `AUTONOMOUS_ROUTE_RECOVERY`,
  `AUTONOMOUS_ROUTE_POCKET_ESCAPE`, `AUTONOMOUS_STABLE_ROUTE_*`, `AUTONOMOUS_TOWN_TELEPORT_*`,
  `AUTONOMOUS_CAPITAL_TRANSIT`, `AUTONOMOUS_GOAL_SNAP*`, `AUTONOMOUS_FLOOR_STEP`, `AUTONOMOUS_POCKET_NUDGE`.
- Behaviour (1.12): `BOT_SCHEDULE_MODE`, `BOT_SCHEDULE_LOGOUT`, `BOT_SCHEDULE_LINKDEAD`, `BOT_RAID_HOTHEAD`,
  `AUTONOMOUS_DUNGEON_BOSS_*` (all only with their switch on).
- Groups: `AUTONOMOUS_GROUP_FORMED`, `AUTONOMOUS_GROUP_ASSEMBLED`, `AUTONOMOUS_GROUP_MEETUP_PLANNED`,
  `AUTONOMOUS_GROUP_REGROUP_*`, `AUTONOMOUS_GROUP_MATCHMAKING_TIMEOUT`, `AUTONOMOUS_GROUP_RESURRECTION_WAIT`.
- Economy/rules: `AUTONOMOUS_SERVICE_ARRIVED`, `AUTONOMOUS_SHOPPING_INDEX`, `AUTONOMOUS_SEAL_PURCHASE`,
  `AUTONOMOUS_EQUIPMENT_UPGRADE`, `BOT_REPAIRED_165`, `BOT_RESTOCKED_165`, `BOT_HEALER_165`, `BOT_CRAFTED_165`,
  `BOT_PROMOTED_165`, `BOT_DEATH_165`, `BOT_PULL_WEAPON_165`.
- RvR: `RVR_SIEGE_STARTED`, `RVR_KEEP_TAKEN`, `RVR_RELIC_CAMPAIGN*`, `RELIC_CAPTURE`, `REALM_RAID_*`,
  `REALM_EXPEDITION_*`, `AUTONOMOUS_SIEGE_RESURRECTION`.
- Companions/windows: `COMPANION_STATS_165`, `COMPANION_SKILLS_165`, `COMPANION_CAST_165`,
  `COMPANION_STUCK_165`, `COMPANION_RIDE_STOWED`, `CLICK_165`.

Fixtures worth reading first: `UT_AutonomousBotDecisionEngine`, `UT_AutonomousPopulationRamp`,
`UT_AutonomousGoalDiagnostics`, `UT_AutonomousLootFlow`, `UT_BotGoalSettings`, `UT_True165Rules`,
`UT_AutonomousRvrDirector`, `UT_RealmRaid*`, `UT_AutonomousStuckWatchdog`, `UT_AutonomousStableRoutePlanner`,
and for the 2026-11-01 port `UT_BotGroupInvite165`, `UT_BotBehaviourRouteGoalFix`, `UT_BotIncomingFireReaction`,
`UT_BotQuietSongs`, `UT_DungeonBossGoals`, `UT_AutonomousBotSchedule`, `UT_AutonomousRaidHothead`.
