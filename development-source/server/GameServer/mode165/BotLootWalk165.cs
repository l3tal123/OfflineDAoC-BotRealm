using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace DOL.GS
{
    /// <summary>
    /// True 1.65 Mode looting (owner 2026-10-30, plan section 21; autoloot added 2026-11-01). A kill's loot and coin are
    /// paid straight to their owner when that owner has autoloot on (AutoLoot165: every player may turn it on with
    /// /autoloot, every gamebot has it on). A bag still reaches the ground when nobody who may take it has autoloot on,
    /// when the owner's backpack is full, or when the owner is offline; after the fight a gamebot walks to each such bag
    /// it may take and picks it up, as a player with autoloot off would. Its loot rules are the usual owners: its own
    /// kills, its bot group's (a random member gets it, as before) and its realm expedition's.
    /// Companions never loot: the player's group owns their kills, and a companion keeps only what the player trades it.
    /// </summary>
    public static class BotLootWalk165
    {
        private const ushort SearchRadius = 1000;       // bags further away are left (a player would not see them either)
        private const int RescanMilliseconds = 1000;
        private const int SkipMilliseconds = 60_000;    // a bag the bot could not take (full backpack) is ignored this long

        private sealed class State
        {
            public long NextScanTick;
            public GameStaticItemTimed Target;
            public readonly Dictionary<GameStaticItemTimed, long> Skipped = new();
        }

        private static readonly ConditionalWeakTable<GameBot, State> States = new();

        /// <summary>True while the bot is walking to, or picking up, a bag (the caller then skips its other plans).</summary>
        public static bool Handle(GameBot bot, out string itemName)
        {
            itemName = null;
            if (!Classic165Mode.IsOn || bot == null || bot.IsTemporaryGroupHelper || !bot.IsAlive || bot.InCombat ||
                bot.ObjectState != GameObject.eObjectState.Active)
                return false;
            State state = States.GetOrCreateValue(bot);
            long now = GameLoop.GameLoopTime;
            if (state.Target != null && !Takeable(bot, state.Target, now, state))
                state.Target = null;
            if (state.Target == null)
            {
                if (now < state.NextScanTick)
                    return false;
                state.NextScanTick = now + RescanMilliseconds;
                state.Target = FindBag(bot, now, state);
                if (state.Target == null)
                    return false;
            }
            GameStaticItemTimed bag = state.Target;
            itemName = bag.Name;
            if (!bot.IsWithinRadius(bag, ServerProperties.Properties.WORLD_PICKUP_DISTANCE - 40))
            {
                if (bot.IsRecoveryResting)
                    bot.WakeRecoveryRest();
                var spot = new Vector3(bag.X, bag.Y, bag.Z);
                if (!bot.IsMoving || Vector3.DistanceSquared(bot.movementComponent.DestinationForClient, spot) > 100f * 100f)
                    bot.PathTo(spot, bot.MaxSpeed);
                return true;
            }
            bot.StopMoving();
            TryPickUpResult result = TryPickUp(bot, bag);
            if (result != TryPickUpResult.Success)
                state.Skipped[bag] = now + SkipMilliseconds;
            state.Target = null;
            return true;
        }

        private static GameStaticItemTimed FindBag(GameBot bot, long now, State state)
        {
            foreach (GameStaticItemTimed old in state.Skipped.Where(kv => kv.Value <= now || kv.Key.ObjectState != GameObject.eObjectState.Active)
                         .Select(kv => kv.Key).ToList())
                state.Skipped.Remove(old);
            return bot.GetItemsInRadius(SearchRadius).OfType<GameStaticItemTimed>()
                .Where(item => Takeable(bot, item, now, state))
                .OrderBy(item => bot.GetDistanceTo(item))
                .FirstOrDefault();
        }

        private static bool Takeable(GameBot bot, GameStaticItemTimed item, long now, State state) =>
            item is WorldInventoryItem or GameMoney &&
            item.ObjectState == GameObject.eObjectState.Active && item.CurrentRegion == bot.CurrentRegion &&
            !(state.Skipped.TryGetValue(item, out long until) && until > now) &&
            OwnerFor(bot, item) != null;

        /// <summary>The loot owner the bot picks this bag up for: itself, its leaderless bot group, or its expedition.
        /// A group led by a player is the player's to loot.</summary>
        private static IGameStaticItemOwner OwnerFor(GameBot bot, GameStaticItemTimed item)
        {
            if (item.Owners.Contains(bot))
                return bot;
            if (bot.Group != null && bot.Group.Leader == null && item.Owners.Contains(bot.Group))
                return bot.Group;
            foreach (IGameStaticItemOwner owner in item.Owners)
                if (owner is RealmRaidLootOwner raid && raid.Members.Contains(bot))
                    return raid;
            return null;
        }

        private static TryPickUpResult TryPickUp(GameBot bot, GameStaticItemTimed bag)
        {
            IGameStaticItemOwner owner = OwnerFor(bot, bag);
            if (owner == null)
                return TryPickUpResult.DoesNotWant;
            TryPickUpResult result = bag.TryAutoPickUp(owner);
            // a bag paid out through the owner leaves the world; one still lying there was not taken
            if (result == TryPickUpResult.Success && bag.ObjectState == GameObject.eObjectState.Active)
                bag.RemoveFromWorld();
            return result;
        }
    }
}
