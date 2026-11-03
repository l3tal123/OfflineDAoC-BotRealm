# True 1.65 Mode: gamebots never get coin (or loot)

## Root cause

`IGameStaticItemOwner.OwnerEqualityComparer.Equals` (GameServer\gameobjects\GameStaticItemTimed.cs, lines 124-129) compares the two owner comparands with `==`. Both sides are typed `object`, so `==` is a **reference** comparison:

```csharp
return x.GameStaticItemOwnerComparand != null && y.GameStaticItemOwnerComparand != null ?
    x.GameStaticItemOwnerComparand == y.GameStaticItemOwnerComparand :   // object == object -> reference equality
    x.Equals(y);
```

`GameBot.GameStaticItemOwnerComparand` (GameServer\bots\GameBot.cs:1638) is `$"offlinebot:{DatabaseID}"`, which builds a **new string on every call**. So `item.Owners.Contains(bot)` matches the hash bucket (string hashes by value) but `Equals` always fails. **A bot is never found as the owner of its own bag.**

In normal mode this never mattered: DropLoot autoloots by calling `money.TryAutoPickUp(owner)` directly, with no `Owners.Contains` lookup. True 1.65 Mode skips autoloot (AbstractServerRules.cs:1708 / 1762) and relies on `BotLootWalk165.OwnerFor` (BotLootWalk165.cs:88-98), whose first check `item.Owners.Contains(bot)` is always false. For a solo kill the bot is the only owner, so OwnerFor returns null, `Takeable` is false, FindBag returns nothing, and the bag decays after 180 s. Coin and items are both affected.

Only two cases still work: leaderless bot groups (`Group` comparand is null, so reference `Equals` is used and works) and realm raid expeditions (`RealmRaidLootOwner` comparand is `this`).

## Evidence

- World snapshot: 303 bots, `sum(MoneyCopper>0) = 0`, levels 1/2/3/5 = 184/84/32/3. SoloPve has 1312 recorded kills and GroupPve has 928.
- server.log (current session from 10/31 01:20, mode=1.65): **all 685 AUTONOMOUS_GOAL_ATTEMPT lines have `groupSize:1`**, so every kill was solo. There are **0 AUTONOMOUS_FIRST_LOOT lines** (the bots picked up no item at all), against 312 AUTONOMOUS_XP_PROGRESS lines.
- Sessions by mode across the logs from 10/29 23:27 onward: normal-mode sessions log thousands of FIRST_LOOT lines (10/30 00:17: 2999; 12:25: 1327; 14:52: 1534). Most 1.65 sessions log 0 FIRST_LOOT even with heavy XP (07:02: 1351 XP lines and 0 loot; 08:40: 1915 XP lines and 0 loot). The few 1.65 sessions with loot (14:04: 25 lines, 19:03: 296 lines) match grouped bots (sampled looters Unaela, Rosoine, Ediward and Edienren all had groupSize 5-6 attempts) and level-50 expeditions (234 of 296 were level 50).
- The coin itself is fine. LootGeneratorMoney gives `minLoot = 2 + ((lvl+1)^3 >> 3)`, so a level-0 mob drops 2-3c, level 1 3-4c, level 2 5-7c, level 3 10-14c, level 4 17-25c and level 5 29-43c, times money_drop = 1. Never 0. The LootGenerator table holds LootGeneratorMoney. CanDropLoot defaults to true, and NoItemLoot only excludes keep guards, bots and pets.
- Nothing else resets or drains MoneyCopper. The only writers are AddMoney, TrySpend, the purchase in AutonomousBotEconomy.cs:1004 and its rollbacks (1017/1034), RealmExchangeTransactions (sale/rollback) and the StuckWatchdog copies. With 0 coin none of the spending paths run.

## Proposed fix (one line)

GameServer\gameobjects\GameStaticItemTimed.cs, inside `OwnerEqualityComparer.Equals` (lines 126-128):

```csharp
public bool Equals(IGameStaticItemOwner x, IGameStaticItemOwner y)
{
    return x.GameStaticItemOwnerComparand != null && y.GameStaticItemOwnerComparand != null ?
        Equals(x.GameStaticItemOwnerComparand, y.GameStaticItemOwnerComparand) :   // value equality (string comparands)
        x.Equals(y);
}
```

(`Equals(object, object)` here is the static `object.Equals`. Inside a nested class that defines an instance `Equals(IGameStaticItemOwner, IGameStaticItemOwner)`, overload resolution could be ambiguous to a reader. Writing `object.Equals(x.GameStaticItemOwnerComparand, y.GameStaticItemOwnerComparand)` is clearer and safe.)

An alternative or extra fix that only touches bots: cache the string in GameBot.cs:1638, for example
`private string _ownerComparand; public object GameStaticItemOwnerComparand => DatabaseID > 0 ? (_ownerComparand ??= $"offlinebot:{DatabaseID}") : this;`.
That fixes Contains for one live bot instance, but not after a bot despawns and respawns as a new GameBot object while its bag is still on the ground. The comparer fix covers both, so it is the recommended fix.

## Risks / side effects

- The implementers are GamePlayer (AccountName string), GameBot ("offlinebot:N" string), RealmRaidLootOwner (`this`), Group and BattleGroup (null, so they use `x.Equals`). With value equality, players and bots match by account or bot id. That is what the comment on `Owners` intends ("players logging back in to pick up items dropped during their previous session"). The old code also broke that for players, because a reloaded DBCharacter gives a new AccountName string instance. It is a small behaviour change for players: after a relog they can now pick up their own old bags.
- `GetHashCode` already uses the comparand's value hash, so it stays consistent with the new Equals.
- No collision is possible between "offlinebot:N" and real account names unless an account is literally named "offlinebot:…".
- Economy note: at 2-14c per kill for level 0-3 mobs, a 5 silver (500c) ticket costs a level 1-3 bot roughly 50-150 kills of coin. Bots will have coin after the fix, but low-level bots may still walk for a while.
- Group coin path (Group.TryAutoPickUpMoney, leaderless): it splits among bots within VISIBILITY_DISTANCE and calls AddMoney, which queues persistence. No change is needed.

## Players in 1.65 (code only)

Yes, players get coin. DropLoot adds the GameMoney to the world with the player (or the player's group/battlegroup) as owner and no autoloot. When the player clicks it, GamePlayer.PickupObject (~GamePlayer.cs:9643) checks `IsOwner(this)` (`Contains(player)` works within a session because AccountName is the same string instance). Then `money.TryPickUp(this, battlegroup / group / this)` → GamePlayer.TryPickUpMoney → AddMoney (guild dues applied). In 1.65 the coin goes to the character row (HandleMoney / SaveIntoDatabase use DBCharacter.Copper etc.). A player-led group's kills go to the player (Group.TryPickUpMoney splits among the players in the group). Companions get nothing.

## Not finished / unverified

- No runtime test of the fix (read-only investigation, no build).
- I did not confirm whether any controller branch above AutonomousWorldBotController.cs:441 regularly prevents solo bots from reaching `BotLootWalk165.Handle` (combat/rest/stable-travel early returns). The ownership bug alone fully explains the zero loot. After the fix, a log line or a count of "Looting …" statuses would confirm that bots reach it.
- I did not check what share of 1.65 kills are grey to the bot (grey kills give no loot by design).
