using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// Vendor shopping (owner 2026-10-31). Now that worn armor counts for bots, a gamebot buys its gear from vendors when
    /// it can afford it, as players did. It knows every coin merchant's stock and prices in the regions it can reach (the
    /// same live merchant index the vendor errand uses), picks the best affordable upgrade it can wear, and walks to that
    /// merchant between tasks instead of browsing whichever vendor is nearest. Items more than one level above the bot are
    /// skipped (their bonuses are capped at its level), and a small reserve is kept for repairs and arrows.
    /// </summary>
    public sealed partial class AutonomousWorldBotController
    {
        public sealed record ShoppingPlan(GameMerchant Merchant, DbItemTemplate Template, long Price, int Gain);

        private sealed record StockEntry(GameMerchant Merchant, DbItemTemplate Template);

        private const long ShoppingPlanLifetimeMilliseconds = 120_000;
        private const long StockIndexLifetimeMilliseconds = 600_000;
        private const int ShoppingCandidatesChecked = 6;

        private static readonly ConcurrentDictionary<long, (ShoppingPlan Plan, long Until)> ShoppingPlans = new();
        private static volatile Dictionary<ushort, Dictionary<int, List<StockEntry>>> _stockIndex;   // region -> level -> entries
        private static long _stockIndexBuiltTick = long.MinValue;
        private static readonly object StockIndexLock = new();

        /// <summary>Copper a bot may spend on gear: its coin less a small reserve for repairs and arrows.</summary>
        public static long ShoppingBudget(GameBot bot) =>
            AutonomousBotEconomy.GetMoney(bot.DatabaseID) - Math.Max(100L, bot.Level * bot.Level * 10L);

        /// <summary>A vendor item a bot would consider buying: priced in coin within its budget, at most one level above it.</summary>
        public static bool ShoppingConsiders(GameBot bot, DbItemTemplate template, long budget) =>
            template != null && template.Price > 0 && template.Price <= budget && template.Level <= bot.Level + 1;

        private static readonly eInventorySlot[] BodyArmorSlots =
        [
            eInventorySlot.HeadArmor, eInventorySlot.TorsoArmor, eInventorySlot.LegsArmor,
            eInventorySlot.ArmsArmor, eInventorySlot.HandsArmor, eInventorySlot.FeetArmor,
        ];

        /// <summary>Body armor slots the bot has nothing in.</summary>
        public static int EmptyBodyArmorSlots(GameBot bot) =>
            bot?.Inventory == null ? 0 : BodyArmorSlots.Count(slot => bot.Inventory.GetItem(slot) == null);

        /// <summary>
        /// Armor first (owner 2026-10-31 runs: level 6-9 bots still had bare slots and bought shields and jewelry
        /// instead, then died to single green and blue monsters). While a body armor slot is empty the bot only buys
        /// pieces for its empty slots, and each may cost at most its share of the budget, so the coin covers every bare
        /// slot instead of one dear piece; with less than that it saves up, as a player would.
        /// </summary>
        public static bool ShoppingAllowsWhileArmorMissing(GameBot bot, DbItemTemplate template, long budget)
        {
            int empty = EmptyBodyArmorSlots(bot);
            if (empty == 0 || template == null)
                return true;
            if (!BodyArmorSlots.Contains((eInventorySlot)template.Item_Type) || bot.Inventory.GetItem((eInventorySlot)template.Item_Type) != null)
                return false;
            return template.Price <= budget / empty;
        }

        private const long ShoppingSkipMilliseconds = 30 * 60_000;
        private static readonly ConcurrentDictionary<(long Bot, string Merchant), long> ShoppingSkips = new();

        /// <summary>The bot arrived and couldn't buy its planned item here: leave this merchant out of its plans for 30 minutes.</summary>
        public static void SkipShoppingAt(GameBot bot, GameMerchant merchant)
        {
            if (bot == null || merchant == null)
                return;
            ShoppingSkips[(bot.DatabaseID, merchant.InternalID)] = GameLoop.GameLoopTime + ShoppingSkipMilliseconds;
            ClearShoppingPlan(bot);
        }

        private static bool ShoppingSkipped(GameBot bot, GameMerchant merchant) =>
            ShoppingSkips.TryGetValue((bot.DatabaseID, merchant.InternalID), out long until) && GameLoop.GameLoopTime < until;

        public static void ClearShoppingPlan(GameBot bot)
        {
            if (bot != null)
                ShoppingPlans.TryRemove(bot.DatabaseID, out _);
        }

        /// <summary>The bot's current shopping trip, or null. Re-planned at most every two minutes.</summary>
        public static ShoppingPlan ShoppingPlanFor(GameBot bot)
        {
            if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.IsPlayerLedGroup ||
                HiredCompanions165.IsHired(bot) || bot.Inventory == null || bot.CurrentRegion == null)
                return null;
            long now = GameLoop.GameLoopTime;
            if (ShoppingPlans.TryGetValue(bot.DatabaseID, out var cached))
            {
                ShoppingPlan plan = cached.Plan;
                if (now < cached.Until && plan == null)
                    return null;
                if (plan != null && plan.Merchant.ObjectState == GameObject.eObjectState.Active && plan.Price <= ShoppingBudget(bot))
                {
                    // A trip keeps its merchant until it is done there (2026-11-01 run 3: the plan was worked out again
                    // every two minutes and could name another merchant mid-trip; the bot then stood "Ready to use" at the
                    // first one, which no longer served it, until the 15-minute watchdog - 850 times in an hour).
                    if (now >= cached.Until && !ShoppingSkipped(bot, plan.Merchant))
                        ShoppingPlans[bot.DatabaseID] = (plan, now + ShoppingPlanLifetimeMilliseconds);
                    if (now < cached.Until || !ShoppingSkipped(bot, plan.Merchant))
                        return plan;
                }
            }
            ShoppingPlan fresh = null;
            try
            {
                fresh = FindShoppingPlan(bot);
            }
            catch (Exception e)
            {
                Log.Warn($"AUTONOMOUS_SHOPPING_PLAN_FAILED bot={bot.Name} id={bot.DatabaseID}: {e.Message}");
            }
            ShoppingPlans[bot.DatabaseID] = (fresh, now + ShoppingPlanLifetimeMilliseconds);
            return fresh;
        }

        private static ShoppingPlan FindShoppingPlan(GameBot bot)
        {
            long budget = ShoppingBudget(bot);
            if (budget <= 0)
                return null;
            Dictionary<ushort, Dictionary<int, List<StockEntry>>> index = StockIndex();
            HashSet<ushort> reachable = ReachableRegions(bot.Realm, bot.CurrentRegionID);
            var equippedValue = new Dictionary<int, int>();
            var candidates = new List<(StockEntry Entry, int QuickGain, double Minutes)>();
            foreach (ushort regionId in reachable)
            {
                if (!index.TryGetValue(regionId, out Dictionary<int, List<StockEntry>> byLevel))
                    continue;
                for (int level = Math.Min(51, bot.Level + 1); level >= 0; level--)
                {
                    if (!byLevel.TryGetValue(level, out List<StockEntry> entries))
                        continue;
                    foreach (StockEntry entry in entries)
                    {
                        DbItemTemplate t = entry.Template;
                        if (!ShoppingConsiders(bot, t, budget) || !ShoppingAllowsWhileArmorMissing(bot, t, budget))
                            continue;
                        int gain;
                        if (BotWeaponStats.IsMeleeWeapon((eObjectType)t.Object_Type))
                            gain = WeaponGain(bot, t);
                        else
                        {
                            int slot = SlotKey(t.Item_Type);
                            if (!equippedValue.TryGetValue(slot, out int worn))
                                equippedValue[slot] = worn = WornValue(bot, t.Item_Type);
                            gain = TemplateValue(t) - worn;
                        }
                        if (gain <= AutonomousBotEconomy.MinimumEquipmentUpgrade || !MightWear(bot, t))
                            continue;
                        GameMerchant merchant = entry.Merchant;
                        if (merchant.ObjectState != GameObject.eObjectState.Active || !AutonomousBotEconomy.SellsForCoin(merchant) ||
                            merchant.Realm != eRealm.None && merchant.Realm != bot.Realm || !IsServiceCandidate(bot, merchant) ||
                            ShoppingSkipped(bot, merchant) ||
                            AutonomousServiceReachability.IsSkipped(AutonomousServiceReachability.Key(merchant), bot.DatabaseID, GameLoop.GameLoopTime))
                            continue;
                        candidates.Add((entry, gain, EstimateTravelMinutes(bot, merchant.CurrentRegionID, merchant.X, merchant.Y)));
                    }
                }
            }
            // Biggest gain first, nearer merchants preferred: a long walk has to buy a clearly better piece.
            foreach (var c in candidates.OrderByDescending(c => c.QuickGain / (1.0 + c.Minutes / 10.0)).Take(ShoppingCandidatesChecked))
            {
                DbInventoryItem item = GameInventoryItem.Create(c.Entry.Template);
                if (item == null || !AutonomousBotEconomy.TryGetEquipmentUpgrade(bot, item, out eInventorySlot slot))
                    continue;
                int gain = AutonomousBotEconomy.EquipmentValue(item) - AutonomousBotEconomy.EquipmentValue(bot.Inventory.GetItem(slot));
                return new ShoppingPlan(c.Entry.Merchant, c.Entry.Template, c.Entry.Template.Price, gain);
            }
            return null;
        }

        /// <summary>
        /// The template checks of AutonomousBotEconomy.TryGetEquipmentUpgrade, before ranking (run 12, 2026-10-31: only the
        /// six best-valued candidates were tried, and those were plate, chain or weapons the class can't use, so most bots
        /// never got a shopping plan and kept their level-3 rags and training weapons). TryGetEquipmentUpgrade still makes
        /// the final call on the slot.
        /// </summary>
        private static bool MightWear(GameBot bot, DbItemTemplate t)
        {
            if (t.LevelRequirement > bot.Level)
                return false;
            eObjectType type = (eObjectType)t.Object_Type;
            if (BotWeaponStats.IsMeleeWeapon(type))
                return BotWeaponStats.IsConfiguredMeleeWeapon(bot, t) && BotWeaponStats.HasConfiguredWeaponProficiency(bot, t);
            return GameServer.ServerRules.CheckAbilityToUseItem(bot, t);
        }

        // Rings and bracers compare against the worse of the pair; every other piece against its own slot.
        private static int SlotKey(int itemType) => itemType switch
        {
            (int)eInventorySlot.RightRing => (int)eInventorySlot.LeftRing,
            (int)eInventorySlot.RightBracer => (int)eInventorySlot.LeftBracer,
            _ => itemType,
        };

        private static int WornValue(GameBot bot, int itemType)
        {
            if (itemType is (int)eInventorySlot.LeftRing or (int)eInventorySlot.RightRing)
                return Math.Min(AutonomousBotEconomy.EquipmentValue(bot.Inventory.GetItem(eInventorySlot.LeftRing)),
                    AutonomousBotEconomy.EquipmentValue(bot.Inventory.GetItem(eInventorySlot.RightRing)));
            if (itemType is (int)eInventorySlot.LeftBracer or (int)eInventorySlot.RightBracer)
                return Math.Min(AutonomousBotEconomy.EquipmentValue(bot.Inventory.GetItem(eInventorySlot.LeftBracer)),
                    AutonomousBotEconomy.EquipmentValue(bot.Inventory.GetItem(eInventorySlot.RightBracer)));
            // Weapons can go to either hand: TryGetEquipmentUpgrade settles the real slot for the final candidates.
            return AutonomousBotEconomy.EquipmentValue(bot.Inventory.GetItem((eInventorySlot)itemType));
        }

        /// <summary>
        /// A vendor melee weapon's gain over the weapon it would replace, with the bot's trained spec counted
        /// (AutonomousBotEconomy.FitValue): a two-hander against the two-hand slot, anything else against the main hand
        /// - the hand a player fights with (2026-11-01 run: a same-level axe for a Shadowblade trained in Axe scored no
        /// gain against its buckler or training sword, so it was never bought). TryGetEquipmentUpgrade still settles the
        /// hand for the final candidates.
        /// </summary>
        private static int WeaponGain(GameBot bot, DbItemTemplate t)
        {
            eInventorySlot hand = t.Item_Type == Slot.TWOHAND ? eInventorySlot.TwoHandWeapon : eInventorySlot.RightHandWeapon;
            return TemplateValue(t) + AutonomousBotEconomy.WeaponSpecBonus(bot, t.Object_Type, hand) -
                   AutonomousBotEconomy.FitValue(bot, bot.Inventory.GetItem(hand), hand);
        }

        /// <summary>AutonomousBotEconomy.EquipmentValue for a vendor template (no item made per bot and template).</summary>
        private static int TemplateValue(DbItemTemplate t)
        {
            if (!BotWeaponStats.HasFunctionalMeleeStats(t))
                return 0;
            int utility = Math.Abs(t.Bonus) + Math.Abs(t.ExtraBonus) + Math.Abs(t.Bonus1) + Math.Abs(t.Bonus2) + Math.Abs(t.Bonus3) +
                          Math.Abs(t.Bonus4) + Math.Abs(t.Bonus5) + Math.Abs(t.Bonus6) + Math.Abs(t.Bonus7) + Math.Abs(t.Bonus8) +
                          Math.Abs(t.Bonus9) + Math.Abs(t.Bonus10);
            int absorb = (eObjectType)t.Object_Type is >= eObjectType._FirstArmor and <= eObjectType._LastArmor
                ? Math.Max(0, t.SPD_ABS) * 2 : 0;   // as AutonomousBotEconomy.EquipmentValue
            return Math.Max(1, t.Level) * 10 + Math.Max(1, t.Quality) + Math.Max(0, t.DPS_AF) * 2 + utility * 3 + absorb;
        }

        /// <summary>Every coin merchant's wearable stock, by region and item level; rebuilt every ten minutes.</summary>
        private static Dictionary<ushort, Dictionary<int, List<StockEntry>>> StockIndex()
        {
            long now = GameLoop.GameLoopTime;
            Dictionary<ushort, Dictionary<int, List<StockEntry>>> index = _stockIndex;
            if (index != null && now - _stockIndexBuiltTick < StockIndexLifetimeMilliseconds)
                return index;
            lock (StockIndexLock)
            {
                if (_stockIndex != null && now - _stockIndexBuiltTick < StockIndexLifetimeMilliseconds)
                    return _stockIndex;
                var built = new Dictionary<ushort, Dictionary<int, List<StockEntry>>>();
                int count = 0;
                foreach (Region region in WorldMgr.GetAllRegions())
                {
                    if (region == null || region.IsHousing)
                        continue;
                    foreach (GameMerchant merchant in region.Merchants)
                    {
                        if (!AutonomousBotEconomy.SellsForCoin(merchant) || merchant.TradeItems == null)
                            continue;
                        foreach (DbItemTemplate t in merchant.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>())
                        {
                            if (t.Price <= 0 || t.Item_Type < (int)eInventorySlot.MinEquipable || t.Item_Type > (int)eInventorySlot.MaxEquipable ||
                                (eObjectType)t.Object_Type == eObjectType.GenericItem)
                                continue;
                            if (!built.TryGetValue(region.ID, out var byLevel))
                                built[region.ID] = byLevel = new Dictionary<int, List<StockEntry>>();
                            int level = Math.Clamp(t.Level, 0, 51);
                            if (!byLevel.TryGetValue(level, out var list))
                                byLevel[level] = list = new List<StockEntry>();
                            list.Add(new StockEntry(merchant, t));
                            count++;
                        }
                    }
                }
                _stockIndex = built;
                _stockIndexBuiltTick = now;
                Log.Info($"AUTONOMOUS_SHOPPING_INDEX regions={built.Count} items={count}");
                return built;
            }
        }
    }
}
