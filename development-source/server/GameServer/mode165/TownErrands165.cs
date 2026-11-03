using System.Collections.Concurrent;
using System.Linq;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// BotRealm reasons for a gamebot to go to town besides a full backpack (run 1, 2026-10-30: repairs, arrows and
    /// crafting happened only on a merchant visit, and only a nearly full bag sent a bot to a merchant, so worn gear was
    /// never repaired and empty quivers never refilled). The visit itself runs the same merchant errand
    /// (BotBrain.TryHandleAutonomousMerchantService: repair, restock, craft). One errand trip per bot every 30 minutes, so
    /// a town without a smith or arrows can't loop it.
    /// </summary>
    public static class TownErrands165
    {
        private const long CooldownMilliseconds = 30 * 60_000;
        private static readonly ConcurrentDictionary<long, long> LastVisit = new();

        public static void Visited(GameBot bot)
        {
            if (bot == null)
                return;
            LastVisit[bot.DatabaseID] = GameLoop.GameLoopTime;
            AutonomousBotEconomy.MarkEconomyChanged(bot.DatabaseID);   // the cached "needs a vendor" answer is re-evaluated
        }

        /// <summary>Why this bot should go to town now ("repair", "ammo", "craft"), or null.</summary>
        public static string Reason(GameBot bot)
        {
            if (!Classic165Mode.IsOn || bot?.Inventory == null || bot.PersistentRecord == null || HiredCompanions165.IsHired(bot))
                return null;
            if (LastVisit.TryGetValue(bot.DatabaseID, out long last) && GameLoop.GameLoopTime - last < CooldownMilliseconds)
                return null;
            long coin = AutonomousBotEconomy.GetMoney(bot.DatabaseID);
            DbInventoryItem worst = bot.Inventory.AllItems
                .Where(i => i != null && i.MaxCondition > 0 && i.Durability > 0 && i.ConditionPercent < 80 &&
                            i.SlotPosition is >= (int)eInventorySlot.MinEquipable and <= (int)eInventorySlot.MaxEquipable)
                .OrderBy(i => i.ConditionPercent).FirstOrDefault();
            if (worst != null && coin >= worst.RepairCost)
                return "repair";
            DbInventoryItem bow = bot.Inventory.GetItem(eInventorySlot.DistanceWeapon);
            if (bow != null && (Supplies165.AmmoType(bow) != null || (eObjectType)bow.Object_Type == eObjectType.Thrown) &&
                Supplies165.ShotsLeft(bot, bow) < Supplies165.RestockBelowFor(bot) && coin >= 100)
                return "ammo";
            if (Supplies165.WantsPullWeapon(bot) && coin >= 200)
                return "pull weapon";
            if (CrafterBots165.WantsSession(bot))
                return "craft";
            return null;
        }
    }
}
