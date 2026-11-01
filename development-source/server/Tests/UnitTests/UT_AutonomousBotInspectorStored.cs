using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// The Bot Inspector's stored-bot path: a bot that is not in the world is described from its
/// OfflineWorldBotRecord and its persisted Inventory rows, exactly as the launcher receives it.
/// The database is a read-only test double: this fixture checks the inspection logic, not the ORM.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_AutonomousBotInspectorStored
{
    private GameServer _previousServer;

    private sealed class InspectorServer : GameServer
    {
        public IObjectDatabase TestDatabase;
        protected override IObjectDatabase DataBaseImpl => TestDatabase;
        protected override IServerRules ServerRulesImpl => new NormalServerRules();
    }

    /// <summary>Answers the two reads CaptureStored makes: the bot record and its Inventory rows.</summary>
    public class InspectorDatabase : DispatchProxy
    {
        public OfflineWorldBotRecord Record;
        public List<DbInventoryItem> Items = [];

        protected override object Invoke(MethodInfo method, object[] args)
        {
            Type[] arguments = method.GetGenericArguments();
            if (method.Name == nameof(IObjectDatabase.SelectObject))
            {
                if (arguments.Length == 1 && arguments[0] == typeof(OfflineWorldBotRecord))
                    return Record;
                return null;
            }

            if (method.Name == nameof(IObjectDatabase.SelectObjects) ||
                method.Name == nameof(IObjectDatabase.SelectAllObjects))
            {
                Type itemType = arguments.Length == 1 ? arguments[0] : typeof(DataObject);
                if (itemType == typeof(DbInventoryItem))
                    return new List<DbInventoryItem>(Items);
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
            }

            return null;
        }
    }

    [SetUp]
    public void SetUp()
    {
        _previousServer = GameServer.Instance;
        var database = DispatchProxy.Create<IObjectDatabase, InspectorDatabase>();
        _database = (InspectorDatabase)(object)database;
        _database.Record = null;
        _database.Items = [];

        var server = (InspectorServer)RuntimeHelpers.GetUninitializedObject(typeof(InspectorServer));
        server.TestDatabase = database;
        GameServer.LoadTestDouble(server);
    }

    [TearDown]
    public void TearDown() => GameServer.LoadTestDouble(_previousServer);

    private InspectorDatabase _database;

    [Test]
    public void Capture_DescribesAStoredBotWithGearBagSpecsAndListings()
    {
        DbItemTemplate weapon = Template("bronze_gladius", "Bronze Gladius", level: 3, quality: 89,
            objectType: 10, price: 750, dps: 30, speed: 300);
        DbItemTemplate potion = Template("minor_healing_potion", "Minor Healing Potion", level: 1, quality: 100,
            objectType: 41, price: 25);

        var record = new OfflineWorldBotRecord
        {
            BotId = 42, Name = "Maraenine", Realm = 1, ClassId = 2, ClassName = "Armsman", RaceId = 1,
            RaceName = "Briton", Gender = 1, Level = 5, Experience = 1000, RealmPoints = 0,
            MoneyCopper = 23_110, SerializedSpecs = "Crush|1;Slash|1;Thrust|5;Polearm|1;Shields|1",
            UnspentSpecPoints = 2, SerializedAbilities = "trained-level|5|realm-points|0",
            CraftSkill = 1, CraftLevel = 42, Health = 53, Mana = 0, Endurance = 16,
            IsAlive = true, Activity = "Fighting a monster", CurrentGoal = "Grind camp",
            ZoneName = "Camelot Hills", RegionId = 1, ZoneId = 20, X = 100, Y = 200, Z = 300,
            DeathCount = 2,
        };
        _database.Record = record;

        string owner = AutonomousBotEconomy.GetOwnerId(record.BotId);
        var equipped = Item(owner, 10, weapon, condition: 50, durability: 60);
        var bagged = Item(owner, 41, potion, condition: 100, durability: 100, count: 2);
        var vaulted = Item(owner, 110, potion, condition: 100, durability: 100);
        var listed = Item(owner, 42, potion, condition: 100, durability: 100, sellPrice: 5_000);
        listed.OwnerLot = 65001;
        listed.RealmExchangeListedUtc = "2026-11-01T20:00:00.0000000Z";
        _database.Items = [equipped, bagged, vaulted, listed];

        AutonomousBotInspector.Snapshot snapshot = AutonomousBotInspector.Capture(record.BotId);

        Assert.That(snapshot.Found, Is.True);
        Assert.That(snapshot.Online, Is.False, "a bot without a live actor must be described as a saved state");
        Assert.That(snapshot.Error, Is.Empty);
        Assert.That(snapshot.Name, Is.EqualTo("Maraenine"));
        Assert.That(snapshot.ClassName, Is.EqualTo("Armsman"));
        Assert.That(snapshot.RealmName, Is.EqualTo("Albion"));
        Assert.That(snapshot.Level, Is.EqualTo(5));
        Assert.That(snapshot.MoneyCopper, Is.EqualTo(23_110));
        Assert.That(snapshot.ExperienceToNext, Is.Not.Empty);
        Assert.That(snapshot.Craft, Is.EqualTo("WeaponCrafting 42"));
        Assert.That(snapshot.DeathCount, Is.EqualTo(2));
        Assert.That(snapshot.Warning, Does.Contain("not in the world"));
        Assert.That(snapshot.Stats, Is.Empty, "stats need the live actor");

        Assert.That(snapshot.Equipment.Length, Is.EqualTo(1));
        Assert.That(snapshot.Equipment[0].Name, Is.EqualTo("Bronze Gladius"));
        Assert.That(snapshot.Equipment[0].Slot, Is.EqualTo("Right hand"));
        Assert.That(snapshot.Equipment[0].Level, Is.EqualTo(3));
        Assert.That(snapshot.Equipment[0].Quality, Is.EqualTo(89));
        Assert.That(snapshot.Equipment[0].ConditionPercent, Is.EqualTo(50));
        Assert.That(snapshot.Equipment[0].DurabilityPercent, Is.EqualTo(60));
        Assert.That(snapshot.Equipment[0].Value, Is.EqualTo(750));
        Assert.That(snapshot.Equipment[0].Details, Does.Contain("DPS"));

        Assert.That(snapshot.Bag.Length, Is.EqualTo(1));
        Assert.That(snapshot.Bag[0].Name, Is.EqualTo("Minor Healing Potion"));
        Assert.That(snapshot.Bag[0].Count, Is.EqualTo(2));

        Assert.That(snapshot.Vault.Length, Is.EqualTo(1), "vault contents are separate from the bag");

        Assert.That(snapshot.Specs.Length, Is.EqualTo(5));
        Assert.That(snapshot.Specs.Single(spec => spec.Name == "Thrust").Level, Is.EqualTo(5));
        Assert.That(snapshot.UnspentSpecPoints, Is.EqualTo(2));
        Assert.That(snapshot.SpecPointsSpent, Is.EqualTo(14), "Thrust 5 costs 2+3+4+5 points");

        Assert.That(snapshot.Listings.Length, Is.EqualTo(1));
        Assert.That(snapshot.Listings[0].ItemName, Is.EqualTo("Minor Healing Potion"));
        Assert.That(snapshot.Listings[0].PriceCopper, Is.EqualTo(5_000));
        Assert.That(snapshot.Listings[0].Realm, Is.EqualTo("Albion"));
        Assert.That(snapshot.Listings[0].Expires, Is.Not.Empty);
    }

    [Test]
    public void Capture_WithoutAStoredBotSaysSoInsteadOfThrowing()
    {
        AutonomousBotInspector.Snapshot snapshot = AutonomousBotInspector.Capture(999_999);
        Assert.That(snapshot.Found, Is.True);
        Assert.That(snapshot.Online, Is.False);
        Assert.That(snapshot.Error, Does.Contain("No bot"));
    }

    private static DbItemTemplate Template(string id, string name, int level, int quality, int objectType,
        long price, int dps = 0, int speed = 0) => new()
    {
        Id_nb = id, Name = name, Level = level, Quality = quality, Object_Type = objectType,
        Price = price, MaxCondition = 100, MaxDurability = 100, DPS_AF = dps, SPD_ABS = speed,
    };

    private static DbInventoryItem Item(string ownerId, int slot, DbItemTemplate template, int condition,
        int durability, int count = 1, int sellPrice = 0)
    {
        var item = new DbInventoryItem
        {
            OwnerID = ownerId,
            OwnerLot = 0,
            SlotPosition = slot,
            ITemplate_Id = template.Id_nb,
            Count = count,
            Condition = condition,
            Durability = durability,
            SellPrice = sellPrice,
        };
        item.Template = template;
        return item;
    }
}
