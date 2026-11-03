using System;
using System.Linq;
using DOL.Database;
using DOL.Database.Attributes;

namespace DOL.GS
{
    /// <summary>How many times a BotRealm exchange listing has been relisted (Exchange165).</summary>
    [DataTable(TableName = "offline_true165_relists")]
    public sealed class OfflineTrue165RelistRecord : DataObject
    {
        [PrimaryKey] public string InventoryId { get; set; } = string.Empty;
        [DataElement(AllowDbNull = false)] public int Tries { get; set; }
        [DataElement(AllowDbNull = false)] public int OriginalPrice { get; set; }
    }

    /// <summary>
    /// BotRealm Realm Exchange rules for bot sellers (owner 2026-10-29/30, plan section 5).
    /// - Pricing: valuable items ask more, within reason: base by level x quality x bonuses, from the plan's level-50
    ///   table (96% plate chest about 200 gold, 99% about 320, masterpiece about 495, masterpiece with 4 gems about 640),
    ///   capped. Crafting materials keep the normal per-unit price.
    /// - Unsold: relisted at 90%, then 80% of the first price; after that it goes back to the bot, which keeps, uses or
    ///   vendors it (normal mode deletes it, as before).
    /// </summary>
    public static class Exchange165
    {
        private const long Gold = 10_000;

        /// <summary>The 1.65 asking price in copper, or 0 to use the normal price (materials, stacks).</summary>
        public static long Price(DbInventoryItem item, double sellerDisposition)
        {
            if (!Classic165Mode.IsOn || item == null || item.MaxCount > 1 || item.Count > 1 ||
                (eObjectType)item.Object_Type is eObjectType.GenericItem or eObjectType.Poison or eObjectType.Arrow or eObjectType.Bolt)
                return 0;
            int level = Math.Clamp(item.Level, 1, 50);
            double baseGold = 200 * Math.Pow(level / 50d, 3) * SlotFactor(item);
            double quality = item.Quality switch
            {
                >= 100 => 2.475, 99 => 1.6, 98 => 1.35, 97 => 1.15, 96 => 1.0, 95 => 0.85, 94 => 0.75, _ => 0.6,
            };
            int bonuses = Math.Abs(item.Bonus1) + Math.Abs(item.Bonus2) + Math.Abs(item.Bonus3) + Math.Abs(item.Bonus4) +
                          Math.Abs(item.Bonus5) + Math.Abs(item.Bonus6) + Math.Abs(item.Bonus7) + Math.Abs(item.Bonus8) +
                          Math.Abs(item.Bonus9) + Math.Abs(item.Bonus10) + Math.Abs(item.ExtraBonus);
            double utility = 1 + 0.0055 * bonuses;
            double condition = 0.45 + 0.55 * Math.Clamp((int)item.ConditionPercent, 1, 100) / 100d;
            double price = baseGold * quality * utility * condition * (1 + sellerDisposition) * Gold;
            double cap = Math.Max(1, 800 * Math.Pow(level / 50d, 3)) * Gold;
            return (long)Math.Clamp(Math.Round(price), 1, cap);
        }

        /// <summary>The body piece is the reference; other armour, shields and jewellery ask less, two-handers more.</summary>
        private static double SlotFactor(DbInventoryItem item) => (eInventorySlot)item.Item_Type switch
        {
            eInventorySlot.TorsoArmor => 1.0,
            eInventorySlot.LegsArmor => 0.85,
            eInventorySlot.HeadArmor or eInventorySlot.ArmsArmor => 0.7,
            eInventorySlot.HandsArmor or eInventorySlot.FeetArmor => 0.6,
            eInventorySlot.TwoHandWeapon => 1.1,
            eInventorySlot.RightHandWeapon or eInventorySlot.LeftHandWeapon or eInventorySlot.DistanceWeapon => 0.9,
            eInventorySlot.Cloak => 0.5,
            _ => 0.6,   // jewellery, belts, bracers, rings, necks
        };

        /// <summary>
        /// An unsold bot listing in BotRealm: relisted at 90%, then 80% of its first price; after two relists it goes
        /// back to the bot's backpack (or is deleted when that is full, as in normal mode). Returns true when handled.
        /// </summary>
        public static bool HandleExpired(DbInventoryItem item, long botId)
        {
            if (!Classic165Mode.IsOn || item == null)
                return false;
            OfflineTrue165RelistRecord record = DOLDB<OfflineTrue165RelistRecord>.SelectObject(DB.Column("InventoryId").IsEqualTo(item.ObjectId));
            if (record == null)
            {
                record = new OfflineTrue165RelistRecord { InventoryId = item.ObjectId, OriginalPrice = (int)item.SellPrice };
                GameServer.Database.AddObject(record);
            }
            if (record.Tries < 2)
            {
                record.Tries++;
                int price = Math.Max(1, (int)(record.OriginalPrice * (record.Tries == 1 ? 0.9 : 0.8)));
                record.Dirty = true;
                GameServer.Database.SaveObject(record);
                MarketCache.UpdateItem(item, static (listed, value) => listed.SellPrice = value, price);
                item.RealmExchangeListedUtc = DateTime.UtcNow.ToString("O");
                item.Dirty = true;
                GameServer.Database.SaveObject(item);
                return true;
            }
            // third time unsold: back to the bot
            GameServer.Database.DeleteObject(record);
            string ownerId = AutonomousBotEconomy.GetOwnerId(botId);
            if (AutonomousBotRegistry.TryGet(botId, out GameBot live) && live.Inventory != null)
            {
                eInventorySlot free = live.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                if (free == eInventorySlot.Invalid)
                    return false;
                MarketCache.RemoveItem(item);
                item.OwnerLot = 0;
                item.SellPrice = 0;
                item.RealmExchangeListedUtc = string.Empty;
                if (!live.Inventory.AddItem(free, item))
                    return false;
                AutonomousBotEconomy.MarkInventoryChanged(live);
                AutonomousBotStatusPersistence.Queue(live, true);
                return true;
            }
            var used = DOLDB<DbInventoryItem>.SelectObjects(DB.Column("OwnerID").IsEqualTo(ownerId))
                .Select(i => i.SlotPosition).ToHashSet();
            int slot = Enumerable.Range((int)eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack - eInventorySlot.FirstBackpack + 1)
                .FirstOrDefault(s => !used.Contains(s));
            if (slot == 0)
                return false;
            MarketCache.RemoveItem(item);
            item.OwnerLot = 0;
            item.SellPrice = 0;
            item.SlotPosition = slot;
            item.RealmExchangeListedUtc = string.Empty;
            item.Dirty = true;
            GameServer.Database.SaveObject(item);
            return true;
        }
    }
}
