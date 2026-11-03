using System.Collections.Generic;
using System.Linq;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// BotRealm gear wear for bots (owner 2026-10-30, plan sections 1 and 17h/2c): gamebots' and hired companions'
    /// weapons and armour lose condition exactly like a player's (GameInventoryItem.OnStrikeTarget / OnStruckByEnemy:
    /// the item_condition_loss_chance, con-based amount, doubled below 91%, down to 70%). Players' own gear already
    /// wears through GameInventoryItem; bots' items are plain inventory rows, so this covers them. Gamebots pay a smith
    /// on their town errands; a companion's player repairs its gear (CompanionWindows165.AskRepair).
    /// </summary>
    public static class ItemWear165
    {
        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(ItemWear165));

        public static void Wear(GameLiving owner, DbInventoryItem item)
        {
            // Items a bot bought, looted or was handed this session are GameInventoryItems too; their own wear hook only
            // acts for players, so wearing them here can't double up (audit 2026-10-31: they never wore until a reload).
            if (!Classic165Mode.IsOn || owner is not GameBot bot || item == null ||
                item.MaxCondition <= 0 || item.ConditionPercent <= 70 ||
                !Util.Chance(ServerProperties.Properties.ITEM_CONDITION_LOSS_CHANCE))
                return;
            int sub = GameObject.GetConLevel(bot.Level, item.Level) + 4;
            if (item.ConditionPercent < 91)
                sub *= 2;
            item.Condition = System.Math.Max(0, item.Condition - sub);
            item.Dirty = true;
            bot.MarkAutonomousStateDirty();
        }

        /// <summary>
        /// A gamebot repairs its worn gear like a player (owner 2026-10-31: "at a vendor or using crafting"). First with
        /// its own tradeskill, anywhere, using the player repair roll (Repair.CalculateSuccessChances: a failed try just
        /// wastes the attempt). In town (atTown) whatever is left goes to a smith or to a crafter gamebot with the skill,
        /// whichever is nearer, paying the normal price of each piece from its own coin, worst first, as far as its coin
        /// goes; a crafter gamebot is paid that price. Hired companions are repaired by their player (CompanionWindows165).
        /// </summary>
        public static void TryBotRepair(GameBot bot, bool atTown = true)
        {
            if (!Classic165Mode.IsOn || bot?.Inventory == null || bot.PersistentRecord == null || HiredCompanions165.IsHired(bot))
                return;
            List<DbInventoryItem> worn = bot.Inventory.AllItems
                .Where(i => i != null && i.Condition < i.MaxCondition && i.Durability > 0 && i.ConditionPercent < 90 &&
                            i.SlotPosition is >= (int)eInventorySlot.MinEquipable and <= (int)eInventorySlot.MaxEquipable)
                .OrderBy(i => i.ConditionPercent).ToList();
            if (worn.Count == 0)
                return;
            Blacksmith smith = atTown ? bot.GetNPCsInRadius(3000).OfType<Blacksmith>().OrderBy(s => bot.GetDistanceTo(s)).FirstOrDefault() : null;
            int self = 0, paidPieces = 0;
            long spent = 0;
            var paidTo = new HashSet<string>();
            foreach (DbInventoryItem item in worn)
            {
                eCraftingSkill secondary = CraftingMgr.GetSecondaryCraftingSkillToWorkOnItem(item);
                int ownChance = RepairChance(RepairSkill(bot, secondary), item);
                if (ownChance > 0)
                {
                    if (Util.Chance(ownChance))
                    {
                        Repaired(item);
                        self++;
                    }
                    continue;
                }
                if (!atTown)
                    continue;
                GameBot crafter = FindCrafter(bot, item, secondary);
                bool useCrafter = crafter != null && (smith == null || bot.GetDistanceTo(crafter) < bot.GetDistanceTo(smith));
                if (!useCrafter && smith == null)
                    continue;
                if (!AutonomousBotEconomy.TrySpend(bot.DatabaseID, item.RepairCost))
                    break;
                if (useCrafter)
                {
                    AutonomousBotEconomy.AddMoney(crafter.DatabaseID, item.RepairCost);
                    paidTo.Add(crafter.Name);
                }
                else
                    paidTo.Add(smith.Name);
                Repaired(item);
                paidPieces++;
                spent += item.RepairCost;
            }
            if (self + paidPieces > 0)
            {
                Log.Info($"BOT_REPAIRED_165 bot={bot.Name} id={bot.DatabaseID} level={bot.Level} own_skill={self} paid={paidPieces} " +
                         $"copper={spent} by=\"{string.Join(",", paidTo)}\"");
                bot.MarkAutonomousStateDirty();
                AutonomousBotStatusPersistence.Queue(bot, true);
            }
        }

        private static void Repaired(DbInventoryItem item)
        {
            Repair.ModifyConditionAndDurability(item);
            item.Dirty = true;
        }

        /// <summary>The player repair roll (Repair.CalculateSuccessChances) for a skill value, 0-100.</summary>
        public static int RepairChance(int skill, DbInventoryItem item)
        {
            int craftLevel = CraftingMgr.GetItemCraftLevel(item);
            if (skill <= 0 || craftLevel <= 0)
                return 0;
            return System.Math.Clamp((int)(90 / (craftLevel * 0.5) * skill) - 80, 0, 100);
        }

        /// <summary>A crafter gamebot's skill in the secondary skill a repair needs: its craft level when its craft works
        /// that material (weaponcraft: metal, wood, leather; armorcraft: metal, leather, cloth; tailoring: cloth, leather;
        /// fletching and siegecraft: wood). Players' secondary skills grow with their main craft the same way.</summary>
        public static int RepairSkill(GameBot bot, eCraftingSkill secondary)
        {
            if (bot?.PersistentRecord is not { } record || secondary == eCraftingSkill.NoCrafting)
                return 0;
            bool works = CrafterBots165.CraftOf(record) switch
            {
                eCraftingSkill.WeaponCrafting => secondary is eCraftingSkill.MetalWorking or eCraftingSkill.WoodWorking or eCraftingSkill.LeatherCrafting,
                eCraftingSkill.ArmorCrafting => secondary is eCraftingSkill.MetalWorking or eCraftingSkill.LeatherCrafting or eCraftingSkill.ClothWorking,
                eCraftingSkill.Tailoring => secondary is eCraftingSkill.ClothWorking or eCraftingSkill.LeatherCrafting,
                eCraftingSkill.Fletching or eCraftingSkill.SiegeCrafting => secondary is eCraftingSkill.WoodWorking,
                _ => false,
            };
            return works ? record.CraftLevel : 0;
        }

        /// <summary>A crafter gamebot of the same realm nearby, not fighting, that repairs this piece every time.</summary>
        private static GameBot FindCrafter(GameBot bot, DbInventoryItem item, eCraftingSkill secondary) =>
            secondary == eCraftingSkill.NoCrafting ? null :
            bot.GetNPCsInRadius(3000).OfType<GameBot>()
                .Where(c => c != bot && c.IsAutonomousWorldBot && !HiredCompanions165.IsHired(c) && c.IsAlive && c.Realm == bot.Realm &&
                            !c.InCombat && RepairChance(RepairSkill(c, secondary), item) >= 100)
                .OrderBy(c => bot.GetDistanceTo(c))
                .FirstOrDefault();
    }
}
