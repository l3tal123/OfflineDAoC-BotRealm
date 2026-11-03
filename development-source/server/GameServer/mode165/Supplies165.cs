using System.Linq;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// BotRealm supplies for bots (owner 2026-10-30, plan section 1: no unlimited ammo or poison, for players and
    /// bots alike). A bot's bow or crossbow shoots real arrows or bolts from its bag, one per shot (players already do);
    /// without them it can't use the bow. Gamebots restock on their town errands from a merchant that sells the right
    /// ammunition; hired companions don't shop: their player gives them arrows (gear window, the bag part).
    /// Normal mode keeps the bots' never-used-up standard ammunition.
    /// </summary>
    public static class Supplies165
    {
        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(Supplies165));

        // How much a gamebot keeps (owner 2026-10-31): an archer (Scout, Hunter, Ranger) shoots every fight and keeps
        // 200, restocking below 100; a class that only pulls with its bow, crossbow or throwing weapons (Warrior,
        // Armsman, Hero, Shadowblade, Infiltrator...) shoots once a fight and keeps 40, restocking below 20. A bot short
        // of coin buys as many packs as it can pay for; the cheapest ammunition is bought.
        public const int ArcherKeep = 200, ArcherRestockBelow = 100, PullerKeep = 40, PullerRestockBelow = 20;
        public const int RestockBelow = PullerRestockBelow;

        public static bool IsArcher(GameBot bot) =>
            bot?.CharacterClass != null && BotRangedCombat.IsDedicatedArcher((eCharacterClass)bot.CharacterClass.ID);

        public static int RestockBelowFor(GameBot bot) => IsArcher(bot) ? ArcherRestockBelow : PullerRestockBelow;
        public static int KeepFor(GameBot bot) => IsArcher(bot) ? ArcherKeep : PullerKeep;

        /// <summary>Shots left for the ranged weapon: arrows or bolts in the bag, or a throwing weapon's own stack.</summary>
        public static int ShotsLeft(GameBot bot, DbInventoryItem weapon) =>
            weapon == null ? 0 : (eObjectType)weapon.Object_Type == eObjectType.Thrown ? weapon.Count :
            AmmoType(weapon) is { } type ? AmmoCount(bot, type) : 0;

        /// <summary>A ranged weapon this bot can use at all (any class with Thrown, Shortbows, Crossbow or a bow).</summary>
        public static bool CanUseRangedTemplate(GameBot bot, DbItemTemplate t) =>
            t != null && t.Item_Type == (int)eInventorySlot.DistanceWeapon && t.Price > 0 &&
            t.LevelRequirement <= bot.Level && t.Level <= bot.Level + 1 &&
            (eObjectType)t.Object_Type is eObjectType.Thrown or eObjectType.Fired or eObjectType.Crossbow or
                eObjectType.Longbow or eObjectType.CompositeBow or eObjectType.RecurvedBow &&
            GameServer.ServerRules.CheckAbilityToUseItem(bot, t);

        private static readonly string[] RangedAbilities =
        [
            Abilities.Weapon_Thrown, Abilities.Weapon_Shortbows, Abilities.Weapon_Crossbow,
            Abilities.Weapon_Longbows, Abilities.Weapon_CompositeBows, Abilities.Weapon_RecurvedBows,
        ];

        /// <summary>Whether the bot's class can use some ranged weapon now but has none in its ranged slot.</summary>
        public static bool WantsPullWeapon(GameBot bot) =>
            Classic165Mode.IsOn && bot?.Inventory != null && !HiredCompanions165.IsHired(bot) &&
            bot.Inventory.GetItem(eInventorySlot.DistanceWeapon) == null && RangedAbilities.Any(bot.HasAbility);

        /// <summary>On a town errand: a bot whose class can pull at range and has no ranged weapon buys one it can use
        /// (the best level it can wear, then the cheapest) from this merchant or one close by. A throwing weapon comes
        /// as a stack; a bow or crossbow then gets its arrows or bolts from Restock.</summary>
        public static void EnsurePullWeapon(GameBot bot, GameMerchant at)
        {
            if (!WantsPullWeapon(bot))
                return;
            long budget = AutonomousWorldBotController.ShoppingBudget(bot);
            var offer = new[] { at }.Concat(bot.GetNPCsInRadius(2500).OfType<GameMerchant>())
                .Where(m => m != null && AutonomousBotEconomy.SellsForCoin(m) && m.TradeItems != null &&
                            (m.Realm == eRealm.None || m.Realm == bot.Realm))
                .SelectMany(m => m.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>().Select(t => (Merchant: m, Template: t)))
                .Where(o => o.Template.Price <= budget && CanUseRangedTemplate(bot, o.Template))
                .OrderByDescending(o => o.Template.Level).ThenBy(o => o.Template.Price)
                .FirstOrDefault();
            if (offer.Template == null || !AutonomousBotEconomy.TrySpend(bot.DatabaseID, offer.Template.Price))
                return;
            DbInventoryItem weapon = GameInventoryItem.Create(offer.Template);
            if (weapon.PackSize > 1 && weapon.MaxCount >= weapon.PackSize)
                weapon.Count = weapon.PackSize;
            if (!bot.Inventory.AddItem(eInventorySlot.DistanceWeapon, weapon))
            {
                AutonomousBotEconomy.AddMoney(bot.DatabaseID, offer.Template.Price);
                return;
            }
            AutonomousBotEconomy.MarkInventoryChanged(bot);
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
            Log.Info($"BOT_PULL_WEAPON_165 bot={bot.Name} id={bot.DatabaseID} class=\"{bot.ClassName}\" level={bot.Level} " +
                     $"item=\"{weapon.Name}\" type={(eObjectType)weapon.Object_Type} count={weapon.Count} copper={offer.Template.Price} from=\"{offer.Merchant.Name}\"");
        }

        public static eObjectType? AmmoType(DbInventoryItem weapon) => weapon == null ? null : (eObjectType)weapon.Object_Type switch
        {
            eObjectType.Crossbow => eObjectType.Bolt,
            eObjectType.Longbow or eObjectType.CompositeBow or eObjectType.RecurvedBow or eObjectType.Fired => eObjectType.Arrow,
            _ => null,
        };

        /// <summary>The bot's real ammunition for this weapon (first stack in its bag), or null.</summary>
        public static DbInventoryItem RealAmmo(GameLiving bot, DbInventoryItem weapon) =>
            AmmoType(weapon) is { } type
                ? bot.Inventory?.GetFirstItemByObjectType((int)type, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack)
                : null;

        public static int AmmoCount(GameBot bot, eObjectType type) =>
            bot.Inventory?.AllItems.Where(i => i != null && i.Object_Type == (int)type &&
                                              i.SlotPosition is >= (int)eInventorySlot.FirstBackpack and <= (int)eInventorySlot.LastBackpack)
                .Sum(i => i.Count) ?? 0;

        /// <summary>In BotRealm a bow or crossbow is usable only with ammunition in the bag (thrown weapons are their own).</summary>
        public static bool HasAmmoFor(GameBot bot, DbInventoryItem weapon) =>
            !Classic165Mode.IsOn || AmmoType(weapon) == null || RealAmmo(bot, weapon) != null;

        /// <summary>On a town errand: a gamebot with a bow or crossbow low on ammunition buys a couple of stacks from this
        /// merchant, or from another merchant close by that sells them.</summary>
        public static void Restock(GameBot bot, GameMerchant at)
        {
            if (!Classic165Mode.IsOn || bot?.Inventory == null || HiredCompanions165.IsHired(bot))
                return;
            DbInventoryItem weapon = bot.Inventory.GetItem(eInventorySlot.DistanceWeapon);
            if (weapon != null && (eObjectType)weapon.Object_Type == eObjectType.Thrown)
            {
                RestockThrown(bot, at, weapon);
                return;
            }
            if (AmmoType(weapon) is not { } type || AmmoCount(bot, type) >= RestockBelowFor(bot))
                return;
            GameMerchant seller = new[] { at }.Concat(bot.GetNPCsInRadius(2500).OfType<GameMerchant>())
                .Where(m => m != null && AutonomousBotEconomy.SellsForCoin(m) && m.TradeItems != null &&
                            (m.Realm == eRealm.None || m.Realm == bot.Realm))
                .FirstOrDefault(m => Ammo(m, type, weapon) != null);
            DbItemTemplate template = seller == null ? null : Ammo(seller, type, weapon);
            if (template == null || template.Price <= 0)
                return;
            for (int i = 0; i < 20 && AmmoCount(bot, type) < KeepFor(bot); i++)
            {
                eInventorySlot slot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                if (slot == eInventorySlot.Invalid || !AutonomousBotEconomy.TrySpend(bot.DatabaseID, template.Price))
                    break;
                DbInventoryItem stack = GameInventoryItem.Create(template);
                if (stack.PackSize > 1 && stack.MaxCount >= stack.PackSize)
                    stack.Count = stack.PackSize;
                if (!bot.Inventory.AddItem(slot, stack))
                {
                    AutonomousBotEconomy.AddMoney(bot.DatabaseID, template.Price);
                    break;
                }
            }
            AutonomousBotEconomy.MarkInventoryChanged(bot);
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
            Log.Info($"BOT_RESTOCKED_165 bot={bot.Name} id={bot.DatabaseID} ammo={type} now={AmmoCount(bot, type)} from=\"{seller.Name}\"");
        }

        /// <summary>A throwing weapon is its own ammunition and is used up per throw, as a player's is: its stack is topped
        /// up with packs of the same weapon from a merchant close by, as far as the coin goes.</summary>
        private static void RestockThrown(GameBot bot, GameMerchant at, DbInventoryItem weapon)
        {
            if (weapon.Count >= RestockBelowFor(bot))
                return;
            DbItemTemplate template = new[] { at }.Concat(bot.GetNPCsInRadius(2500).OfType<GameMerchant>())
                .Where(m => m != null && AutonomousBotEconomy.SellsForCoin(m) && m.TradeItems != null &&
                            (m.Realm == eRealm.None || m.Realm == bot.Realm))
                .SelectMany(m => m.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>())
                .FirstOrDefault(t => string.Equals(t.Id_nb, weapon.Id_nb, System.StringComparison.OrdinalIgnoreCase) && t.Price > 0);
            if (template == null)
                return;
            int pack = System.Math.Max(1, template.PackSize);
            int cap = System.Math.Max(pack, System.Math.Min(KeepFor(bot), weapon.MaxCount));
            int start = weapon.Count;
            while (weapon.Count + pack <= cap && AutonomousBotEconomy.TrySpend(bot.DatabaseID, template.Price))
                weapon.Count += pack;
            if (weapon.Count == start)
                return;
            weapon.Dirty = true;
            AutonomousBotEconomy.MarkInventoryChanged(bot);
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
            Log.Info($"BOT_RESTOCKED_165 bot={bot.Name} id={bot.DatabaseID} ammo=Thrown now={weapon.Count} item=\"{weapon.Name}\"");
        }

        // ---- poisons (Infiltrators, Shadowblades, Nightshades)

        private static Spell PoisonSpell(DbInventoryItem vial) =>
            vial?.PoisonSpellID > 0 ? SkillBase.GetSpellByID(vial.PoisonSpellID) : null;

        /// <summary>Coats one weapon from the best usable vial in the bag (its own charges), using the vial up.</summary>
        public static bool CoatFromVial(GameBot bot, eInventorySlot slot, eSpellType type, int envenomLevel) =>
            CoatFromVial(bot, slot, envenomLevel, i => PoisonSpell(i)?.SpellType == type);

        /// <summary>
        /// Coats one weapon from the best vial in the bag that <paramref name="wanted"/> accepts, with a player's rules
        /// (GamePlayer.ApplyPoison): Envenom trained, the vial's level within both its Envenom spec and its level (the
        /// caller's <paramref name="envenomLevel"/>), a weapon it can use, not a thrown weapon or crossbow, an
        /// uncoated weapon. The vial's own charges; the vial is used up.
        /// </summary>
        public static bool CoatFromVial(GameBot bot, eInventorySlot slot, int envenomLevel, System.Func<DbInventoryItem, bool> wanted,
            bool bagOrder = false)
        {
            DbInventoryItem weapon = bot.Inventory?.GetItem(slot);
            if (envenomLevel < 1 || weapon == null || weapon.PoisonCharges > 0 || !GamePlayer.CanPoisonWeapon(weapon.Object_Type) ||
                !BotWeaponStats.FitsConfiguredSlot(bot, weapon, slot))
                return false;
            var vials = bot.Inventory.AllItems
                .Where(i => i != null && i.Object_Type == (int)eObjectType.Poison && i.Level <= envenomLevel &&
                            i.SlotPosition is >= (int)eInventorySlot.FirstBackpack and <= (int)eInventorySlot.LastBackpack &&
                            PoisonSpell(i) != null && wanted(i));
            // a companion uses its bag top to bottom (slot 1 first, owner 2026-11-01); a gamebot its best vial
            DbInventoryItem vial = (bagOrder ? vials.OrderBy(i => i.SlotPosition) : vials.OrderByDescending(i => i.Level)).FirstOrDefault();
            if (vial == null)
                return false;
            weapon.PoisonSpellID = vial.PoisonSpellID;
            weapon.PoisonMaxCharges = System.Math.Max(1, vial.PoisonMaxCharges);
            weapon.PoisonCharges = System.Math.Max(1, vial.PoisonCharges);
            bot.Inventory.RemoveCountFromStack(vial, 1);
            AutonomousBotEconomy.MarkInventoryChanged(bot);
            return true;
        }

        // Assassins coat both weapons from vials all day (owner 2026-10-31): they keep 5 of each kind, restocking below 3.
        public const int PoisonKeep = 5, PoisonRestockBelow = 3;

        /// <summary>On a town errand: an assassin gamebot with fewer than three vials of a kind buys the best it can use
        /// (damage-over-time, and the strength/constitution debuff) from a poison merchant close by.</summary>
        public static void RestockPoisons(GameBot bot)
        {
            if (!Classic165Mode.IsOn || bot?.Inventory == null || bot.CharacterClass == null || HiredCompanions165.IsHired(bot) ||
                !BotPoisonSupply.IsAssassin((eCharacterClass)bot.CharacterClass.ID))
                return;
            int level = BotPoisonSupply.EligibleLevel(bot.Level, bot.GetModifiedSpecLevel(Specs.Envenom));
            GameMerchant[] merchants = bot.GetNPCsInRadius(2500).OfType<GameMerchant>()
                .Where(m => AutonomousBotEconomy.SellsForCoin(m) && m.TradeItems != null && (m.Realm == eRealm.None || m.Realm == bot.Realm))
                .ToArray();
            bool bought = false;
            foreach (eSpellType type in new[] { eSpellType.DamageOverTime, eSpellType.StrengthConstitutionDebuff })
            {
                int have = bot.Inventory.AllItems.Where(i => i != null && i.Object_Type == (int)eObjectType.Poison &&
                                                            PoisonSpell(i)?.SpellType == type).Sum(i => i.Count);
                if (have >= PoisonRestockBelow)
                    continue;
                DbItemTemplate best = merchants.SelectMany(m => m.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>())
                    .Where(t => t.Object_Type == (int)eObjectType.Poison && t.Level <= level && t.Price > 0 &&
                                t.PoisonSpellID > 0 && SkillBase.GetSpellByID(t.PoisonSpellID)?.SpellType == type)
                    .OrderByDescending(t => t.Level).ThenBy(t => t.Price).FirstOrDefault();
                for (int i = have; best != null && i < PoisonKeep; i++)
                {
                    eInventorySlot slot = bot.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                    if (slot == eInventorySlot.Invalid || !AutonomousBotEconomy.TrySpend(bot.DatabaseID, best.Price))
                        break;
                    if (!bot.Inventory.AddItem(slot, GameInventoryItem.Create(best)))
                    {
                        AutonomousBotEconomy.AddMoney(bot.DatabaseID, best.Price);
                        break;
                    }
                    bought = true;
                }
            }
            if (bought)
            {
                AutonomousBotEconomy.MarkInventoryChanged(bot);
                bot.MarkAutonomousStateDirty();
                AutonomousBotStatusPersistence.Queue(bot, true);
            }
        }

        /// <summary>The cheapest plain ammunition of this type the merchant sells, matching the weapon's damage type
        /// when it can (a period archer bought ordinary arrows; specials were a choice).</summary>
        private static DbItemTemplate Ammo(GameMerchant merchant, eObjectType type, DbInventoryItem weapon) =>
            merchant.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>()
                .Where(t => t.Object_Type == (int)type && t.Price > 0 && t.LevelRequirement <= 50)
                .OrderBy(t => t.Price)   // the cheapest by default (owner 2026-10-31)
                .ThenBy(t => weapon != null && t.Type_Damage == weapon.Type_Damage ? 0 : 1)
                .FirstOrDefault();
    }
}
