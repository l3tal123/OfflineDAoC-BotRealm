using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DOL.Database;
using DOL.Database.Handlers;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture, NonParallelizable]
public sealed class UT_WelcomePurse165
{
    private string _path;
    private SqliteObjectDatabase _database;

    [SetUp]
    public void SetUp()
    {
        _path = Path.Combine(Path.GetTempPath(), "daoc-welcome-purse-" + Guid.NewGuid().ToString("N") + ".sqlite3");
        _database = Open();
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private SqliteObjectDatabase Open()
    {
        var database = new SqliteObjectDatabase($"Data Source={_path};Version=3;Pooling=False;");
        database.RegisterDataObject(typeof(DbCoreCharacter));
        database.RegisterDataObject(typeof(DbCoreCharacterXCustomParam));
        return database;
    }

    private DbCoreCharacter Character(string id, int gold = 0, int silver = 0, int copper = 0) =>
        new() { ObjectId = id, Name = id, AccountName = "test-account", Realm = 1, Class = 1, Race = 1, Level = 5, Gold = gold, Silver = silver, Copper = copper };

    [Test]
    public void PaysOneGoldToEveryCharacterThatExistedBeforeTheRule()
    {
        Assert.That(_database.AddObject(Character("poor")), Is.True);
        Assert.That(_database.AddObject(Character("rich", gold: 2, silver: 99, copper: 99)), Is.True);
        Assert.That(_database.AddObject(Character("plain", silver: 6, copper: 51)), Is.True);

        Assert.That(WelcomePurse165.Pay(_database), Is.EqualTo(3));

        var fresh = Open();
        var characters = fresh.SelectAllObjects<DbCoreCharacter>().ToDictionary(character => character.ObjectId);
        Assert.Multiple(() =>
        {
            Assert.That(characters["poor"].Gold, Is.EqualTo(1));
            Assert.That(characters["poor"].Silver, Is.EqualTo(0));
            Assert.That(characters["poor"].Copper, Is.EqualTo(0));
            Assert.That(characters["rich"].Gold, Is.EqualTo(3));
            Assert.That(characters["rich"].Silver, Is.EqualTo(99));
            Assert.That(characters["rich"].Copper, Is.EqualTo(99));
            Assert.That(characters["plain"].Gold, Is.EqualTo(1));
            Assert.That(characters["plain"].Silver, Is.EqualTo(6));
            Assert.That(characters["plain"].Copper, Is.EqualTo(51));
            Assert.That(fresh.SelectAllObjects<DbCoreCharacterXCustomParam>().Count, Is.EqualTo(3));
            Assert.That(fresh.SelectAllObjects<DbCoreCharacterXCustomParam>().Select(param => param.KeyName),
                Is.All.EqualTo(WelcomePurse165.MarkerKey));
        });
    }

    [Test]
    public void PaysEachCharacterOnlyOnceAcrossRestarts()
    {
        Assert.That(_database.AddObject(Character("once")), Is.True);
        Assert.That(WelcomePurse165.Pay(_database), Is.EqualTo(1));

        var restarted = Open();
        Assert.That(WelcomePurse165.Pay(restarted), Is.EqualTo(0));
        Assert.That(WelcomePurse165.Pay(restarted), Is.EqualTo(0));
        Assert.That(Open().SelectAllObjects<DbCoreCharacter>().Single().Gold, Is.EqualTo(1));
        Assert.That(Open().SelectAllObjects<DbCoreCharacterXCustomParam>().Count, Is.EqualTo(1));
    }

    [Test]
    public void SkipsCharactersThatAlreadyCarryTheMarker()
    {
        Assert.That(_database.AddObject(Character("paid")), Is.True);
        Assert.That(_database.AddObject(Character("unpaid", silver: 1)), Is.True);
        Assert.That(_database.AddObject(new DbCoreCharacterXCustomParam("paid", WelcomePurse165.MarkerKey, "1")), Is.True);

        Assert.That(WelcomePurse165.Pay(_database), Is.EqualTo(1));

        var characters = Open().SelectAllObjects<DbCoreCharacter>().ToDictionary(character => character.ObjectId);
        Assert.That(characters["paid"].Gold, Is.EqualTo(0));
        Assert.That(characters["unpaid"].Gold, Is.EqualTo(1));
    }

    [Test]
    public void EmptyWorldAndMissingDatabaseAreNoOps()
    {
        Assert.That(WelcomePurse165.Pay(null), Is.EqualTo(0));
        Assert.That(WelcomePurse165.Pay(_database), Is.EqualTo(0));
        Assert.That(_database.SelectAllObjects<DbCoreCharacter>(), Is.Empty);
    }

    [Test]
    public void PaysTheOneGoldWelcomePurseToACoinlessWorldBot()
    {
        var bot = new OfflineWorldBotRecord { BotId = 7, Name = "coinless", MoneyCopper = 0 };

        Assert.That(WelcomePurse165.PayBot(bot), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(bot.MoneyCopper, Is.EqualTo(WelcomePurse165.PurseCopper));
            Assert.That(bot.WelcomePursePaid, Is.True);
            Assert.That(bot.Dirty, Is.True, "the object database only saves dirty records");
        });
    }

    [Test]
    public void TopsAWorldBotUpToThePurseButNeverTakesCoinAway()
    {
        var poor = new OfflineWorldBotRecord { MoneyCopper = 250 };
        var rich = new OfflineWorldBotRecord { MoneyCopper = 50_000 };

        Assert.That(WelcomePurse165.PayBots([poor, rich]), Has.Count.EqualTo(2));

        Assert.Multiple(() =>
        {
            Assert.That(poor.MoneyCopper, Is.EqualTo(10_000), "a bot under the purse is topped up to it");
            Assert.That(rich.MoneyCopper, Is.EqualTo(50_000), "a bot with more coin keeps its coin");
            Assert.That(poor.WelcomePursePaid && rich.WelcomePursePaid, Is.True);
        });
    }

    [Test]
    public void PaysEachWorldBotOnlyOnce()
    {
        var bot = new OfflineWorldBotRecord { MoneyCopper = 0 };

        Assert.That(WelcomePurse165.PayBots([bot]), Has.Count.EqualTo(1));
        bot.MoneyCopper = 0;   // the bot spent its purse
        Assert.That(WelcomePurse165.PayBots([bot]), Is.Empty, "the marker keeps a restart from paying it again");

        Assert.Multiple(() =>
        {
            Assert.That(WelcomePurse165.PayBot(null), Is.False);
            Assert.That(WelcomePurse165.PayBots(null), Is.Empty);
            Assert.That(WelcomePurse165.PayBots([]), Is.Empty);
        });
    }
}
