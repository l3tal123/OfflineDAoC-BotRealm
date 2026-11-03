using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// BotRealm crafter gamebots (owner 2026-10-30, plan section 6: crafting for bots, a BotRealm-only feature).
    /// - About one gamebot in five has a primary craft (record CraftSkill / CraftLevel): weapon, armour, tailoring,
    ///   fletching, spellcraft, alchemy or siegecraft.
    /// - On a town errand (between tasks, out of combat) a crafter buys the materials of a real recipe of its craft near
    ///   its skill (the recipe's own ingredient prices), crafts it at 94-99% quality, gains skill like a player (more
    ///   for harder recipes, none for grey ones), and puts the product on the Realm Exchange.
    /// - A siegecrafter builds siege kits instead (ram first, catapult and trebuchet as its skill grows) and keeps a few;
    ///   siege crews buy them from it (SupplySiege). In BotRealm there are no free siege engines and no frontier siege
    ///   merchants: period siege equipment was crafted (the 1.64 client's Siegecraft recipes).
    /// </summary>
    public static class CrafterBots165
    {
        public const int MaxSkill = 1100;
        public const int KitsKept = 2;
        private const long CraftEveryMilliseconds = 10 * 60_000;

        private static readonly eCraftingSkill[] Crafts =
        [
            eCraftingSkill.WeaponCrafting, eCraftingSkill.ArmorCrafting, eCraftingSkill.Tailoring, eCraftingSkill.Fletching,
            eCraftingSkill.SpellCrafting, eCraftingSkill.Alchemy, eCraftingSkill.SiegeCrafting, eCraftingSkill.SiegeCrafting,
        ];

        /// <summary>Siege kits a siegecrafter can build, by the skill it needs.</summary>
        private static readonly (BotSiegeKind Kind, int Skill)[] SiegeKits =
            [(BotSiegeKind.Ram, 1), (BotSiegeKind.Catapult, 300), (BotSiegeKind.Trebuchet, 600)];

        private sealed record Recipe(string ProductId, int Level, long MaterialCost);
        private static readonly ConcurrentDictionary<eCraftingSkill, Recipe[]> Recipes = new();
        private static readonly ConcurrentDictionary<long, long> NextCraft = new();

        /// <summary>A gamebot's craft, given once (fresh bots at creation; older bots the first time they work).</summary>
        public static eCraftingSkill CraftOf(OfflineWorldBotRecord record)
        {
            if (record == null || HiredCompanions165.IsHired(record.BotId))
                return eCraftingSkill.NoCrafting;
            if (record.CraftSkill == 0)
            {
                // one bot in five, spread evenly by id; siegecraft twice as likely (every siege needs it)
                record.CraftSkill = record.BotId % 5 == 0 ? (int)Crafts[(int)(record.BotId / 5 % Crafts.Length)] : -1;
                record.CraftLevel = record.CraftSkill > 0 ? 1 : 0;
                record.Dirty = true;
            }
            return record.CraftSkill > 0 ? (eCraftingSkill)record.CraftSkill : eCraftingSkill.NoCrafting;
        }

        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(CrafterBots165));

        /// <summary>A crafter goes to town for a session when its last one was 30 minutes or more ago (TownErrands165).</summary>
        public static bool WantsSession(GameBot bot) =>
            bot?.PersistentRecord is { } record && CraftOf(record) != eCraftingSkill.NoCrafting &&
            (!NextCraft.TryGetValue(bot.DatabaseID, out long next) || GameLoop.GameLoopTime >= next + 20 * 60_000);

        /// <summary>On a town errand: craft one thing (at most every ten minutes).</summary>
        public static void Work(GameBot bot)
        {
            if (!Classic165Mode.IsOn || bot?.PersistentRecord is not { } record || bot.Inventory == null)
                return;
            eCraftingSkill craft = CraftOf(record);
            if (craft == eCraftingSkill.NoCrafting)
                return;
            long now = GameLoop.GameLoopTime;
            if (NextCraft.TryGetValue(bot.DatabaseID, out long next) && now < next)
                return;
            NextCraft[bot.DatabaseID] = now + CraftEveryMilliseconds;
            // A session is several combines, as a player's is (run 5, 2026-10-30: one combine per 30-minute errand left the
            // best bot crafter at skill 5 after five hours, so siege kits beyond rams and real crafted gear never came).
            // It stops when the bot can't pay for the next materials or has no room.
            for (int i = 0; i < CombinesPerSession; i++)
            {
                long before = AutonomousBotEconomy.GetMoney(bot.DatabaseID);
                if (craft == eCraftingSkill.SiegeCrafting)
                    BuildSiegeKit(bot, record);
                else
                    CraftRecipe(bot, record, craft);
                if (AutonomousBotEconomy.GetMoney(bot.DatabaseID) >= before)
                    break;   // nothing bought: out of coin, room or recipes
            }
        }

        private const int CombinesPerSession = 10;

        private static void CraftRecipe(GameBot bot, OfflineWorldBotRecord record, eCraftingSkill craft)
        {
            Recipe[] all = Recipes.GetOrAdd(craft, LoadRecipes);
            int skill = Math.Max(1, record.CraftLevel);
            // the hardest recipe it can just make (players craft at the edge of their skill)
            // a new crafter starts on the line's easiest recipes: weapon, armour and tailoring begin at 15, beyond skill 1 + 10,
            // so those crafters never crafted and never gained (run 6, 2026-10-30)
            Recipe recipe = all.Where(r => r.Level <= skill + 10).OrderByDescending(r => r.Level).FirstOrDefault() ?? all.FirstOrDefault();
            if (recipe == null || bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack) == eInventorySlot.Invalid ||
                !AutonomousBotEconomy.TrySpend(bot.DatabaseID, recipe.MaterialCost))
                return;
            Gain(bot, record, recipe.Level - skill);
            if (Util.Chance(Math.Clamp((recipe.Level - skill) * 2, 0, 40)))
                return;   // failed: the materials are spent
            DbItemTemplate template = GameServer.Database.FindObjectByKey<DbItemTemplate>(recipe.ProductId);
            if (template == null)
                return;
            DbInventoryItem product = GameInventoryItem.Create(template);
            product.IsCrafted = true;
            product.Creator = bot.Name;
            if (template.MaxCount <= 1)
                product.Quality = Util.Chance(2) ? 100 : 94 + Util.Random(5);
            eInventorySlot slot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
            if (slot == eInventorySlot.Invalid || !bot.Inventory.AddItem(slot, product))
                return;
            AutonomousBotEconomy.MarkInventoryChanged(bot);
            int price = Math.Max(1, AutonomousBotEconomy.RecommendListingPrice(product, Util.Random(30) / 100.0));
            bool listed = AutonomousBotEconomy.TryList(bot, product, price);
            if (!listed)
            {
                bot.Inventory.RemoveItem(product);   // no exchange space: it goes to a vendor's scrap pile
                AutonomousBotEconomy.AddMoney(bot.DatabaseID, recipe.MaterialCost / 2);
            }
            Log.Info($"BOT_CRAFTED_165 bot={bot.Name} id={bot.DatabaseID} craft={craft} skill={record.CraftLevel} " +
                     $"item=\"{template.Name}\" recipe={recipe.Level} listed={listed} price={price}");
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
        }

        private static void BuildSiegeKit(GameBot bot, OfflineWorldBotRecord record)
        {
            int skill = Math.Max(1, record.CraftLevel);
            foreach ((BotSiegeKind kind, int need) in SiegeKits.Reverse())
            {
                if (skill < need)
                    continue;
                string kitId = BotSiegeRuntime.Kit(bot.Realm, kind);
                int have = bot.Inventory.AllItems.Where(i => i != null && i.Id_nb == kitId).Sum(i => Math.Max(1, i.Count));
                if (have >= KitsKept)
                    continue;
                DbItemTemplate template = GameServer.Database.FindObjectByKey<DbItemTemplate>(kitId);
                long cost = template == null ? 0 : Math.Max(1, template.Price / 5);   // the wood, metal, leather and parts
                eInventorySlot slot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                if (template == null || slot == eInventorySlot.Invalid || !AutonomousBotEconomy.TrySpend(bot.DatabaseID, cost))
                    return;
                Gain(bot, record, need + 100 - skill);
                DbInventoryItem kit = GameInventoryItem.Create(template);
                kit.IsCrafted = true;
                kit.Creator = bot.Name;
                if (!bot.Inventory.AddItem(slot, kit))
                {
                    AutonomousBotEconomy.AddMoney(bot.DatabaseID, cost);
                    return;
                }
                AutonomousBotEconomy.MarkInventoryChanged(bot);
                bot.MarkAutonomousStateDirty();
                AutonomousBotStatusPersistence.Queue(bot, true);
                Log.Info($"BOT_CRAFTED_165 bot={bot.Name} id={bot.DatabaseID} craft=SiegeCrafting skill={record.CraftLevel} kit={kind}");
                return;
            }
            // nothing to build: practise on the cheapest kit's parts for skill (materials only)
            if (skill < MaxSkill && AutonomousBotEconomy.TrySpend(bot.DatabaseID, 5_000))
                Gain(bot, record, 0);
        }

        /// <summary>Skill gain like a player's: likelier the harder the recipe compared to the skill, none when grey.</summary>
        private static void Gain(GameBot bot, OfflineWorldBotRecord record, int difficulty)
        {
            int chance = difficulty >= 0 ? 80 : difficulty >= -15 ? 60 : difficulty >= -30 ? 35 : difficulty >= -50 ? 15 : 0;
            if (record.CraftLevel < MaxSkill && Util.Chance(chance))
            {
                record.CraftLevel++;
                record.Dirty = true;
            }
        }

        /// <summary>The recipes of a craft with their material cost (each ingredient's template price x count).</summary>
        private static Recipe[] LoadRecipes(eCraftingSkill craft)
        {
            var list = new List<Recipe>();
            foreach (DbCraftedItem crafted in DOLDB<DbCraftedItem>.SelectObjects(DB.Column("CraftingSkillType").IsEqualTo((int)craft)))
            {
                long cost = 0;
                foreach (DbCraftedXItem part in DOLDB<DbCraftedXItem>.SelectObjects(DB.Column("CraftedItemId_nb").IsEqualTo(crafted.Id_nb)))
                {
                    DbItemTemplate material = GameServer.Database.FindObjectByKey<DbItemTemplate>(part.IngredientId_nb);
                    cost += (material?.Price ?? 0) * Math.Max(1, part.Count);
                }
                if (cost > 0)
                    list.Add(new Recipe(crafted.Id_nb, crafted.CraftingLevel, cost));
            }
            return list.OrderBy(r => r.Level).ToArray();
        }

        /// <summary>
        /// A siege crew buys a kit from a siegecrafter of its realm (the realm's crafters, wherever they are; the kit is
        /// handed over as a trade by courier would be). Returns true when the crew now holds the kit.
        /// </summary>
        public static bool SupplySiege(GameBot crew, string kitId)
        {
            if (!Classic165Mode.IsOn || crew?.Inventory == null || BotSiegeRuntime.Item(crew, kitId) != null)
                return BotSiegeRuntime.Item(crew, kitId) != null;
            eInventorySlot slot = crew.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
            if (slot == eInventorySlot.Invalid)
                return false;
            foreach (GameBot crafter in AutonomousBotRegistry.Snapshot())
            {
                if (crafter == crew || crafter.Realm != crew.Realm || crafter.Inventory == null ||
                    CraftOf(crafter.PersistentRecord) != eCraftingSkill.SiegeCrafting)
                    continue;
                DbInventoryItem kit = BotSiegeRuntime.Item(crafter, kitId);
                if (kit == null)
                    continue;
                long price = Math.Max(1, (GameServer.Database.FindObjectByKey<DbItemTemplate>(kitId)?.Price ?? 50_000) / 4);
                if (!AutonomousBotEconomy.TrySpend(crew.DatabaseID, price))
                    return false;
                lock (crafter.Inventory.Lock)
                {
                    if (!crafter.Inventory.RemoveItemWithoutDbDeletion(kit))
                    {
                        AutonomousBotEconomy.AddMoney(crew.DatabaseID, price);
                        continue;
                    }
                }
                kit.OwnerID = AutonomousBotEconomy.GetOwnerId(crew.DatabaseID);
                if (!crew.Inventory.AddItem(slot, kit))
                {
                    crafter.Inventory.AddItem(crafter.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack), kit);
                    AutonomousBotEconomy.AddMoney(crew.DatabaseID, price);
                    return false;
                }
                AutonomousBotEconomy.AddMoney(crafter.DatabaseID, price);
                AutonomousBotEconomy.MarkInventoryChanged(crafter);
                AutonomousBotEconomy.MarkInventoryChanged(crew);
                crafter.MarkAutonomousStateDirty();
                crew.MarkAutonomousStateDirty();
                AutonomousBotStatusPersistence.Queue(crafter, true);
                AutonomousBotStatusPersistence.Queue(crew, true);
                return true;
            }
            return false;
        }
    }
}
