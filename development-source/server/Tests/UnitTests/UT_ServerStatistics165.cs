using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_ServerStatistics165
{
    [Test]
    public void RequestCarriesOneRequestId()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ServerStatistics165.TryParseRequest("0f3c9a2b8d1e4c5f9a7b6c5d4e3f2a1b", out string valid), Is.True);
            Assert.That(valid, Is.EqualTo("0f3c9a2b8d1e4c5f9a7b6c5d4e3f2a1b"));
            Assert.That(ServerStatistics165.TryParseRequest("  abc  ", out string trimmed), Is.True);
            Assert.That(trimmed, Is.EqualTo("abc"));
            Assert.That(ServerStatistics165.TryParseRequest(string.Empty, out _), Is.False);
            Assert.That(ServerStatistics165.TryParseRequest("   ", out _), Is.False);
            Assert.That(ServerStatistics165.TryParseRequest(null, out _), Is.False);
            Assert.That(ServerStatistics165.TryParseRequest(new string('x', 129), out _), Is.False);
            Assert.That(ServerStatistics165.TryParseRequest("abc\ndef", out _), Is.False);
        });
    }

    [Test]
    public void TopBoardTakesTheTenHighestValuesOnly()
    {
        var rows = Enumerable.Range(1, 12)
            .Select(value => new ServerStatistics165.StatRow("actor" + value, "Bot", 1, 50, value))
            .Append(new ServerStatistics165.StatRow("none", "Player", 2, 50, 0))
            .ToArray();

        ServerStatistics165.StatRow[] top = ServerStatistics165.SelectTop(rows);

        Assert.Multiple(() =>
        {
            Assert.That(top, Has.Length.EqualTo(10));
            Assert.That(top.Select(row => row.Value), Is.EqualTo(new long[] { 12, 11, 10, 9, 8, 7, 6, 5, 4, 3 }));
            Assert.That(top.Any(row => row.Name == "none"), Is.False, "zero values never enter a board");
            Assert.That(ServerStatistics165.SelectTop(null), Is.Empty);
            Assert.That(ServerStatistics165.SelectTop([]), Is.Empty);
        });
    }

    [Test]
    public void TiesAreOrderedByNameThenKind()
    {
        ServerStatistics165.StatRow[] top = ServerStatistics165.SelectTop(
        [
            new ServerStatistics165.StatRow("Zed", "Bot", 1, 10, 7),
            new ServerStatistics165.StatRow("alpha", "Bot", 2, 10, 7),
            new ServerStatistics165.StatRow("Beta", "Player", 3, 10, 7),
            new ServerStatistics165.StatRow("Top", "Bot", 1, 10, 9),
        ]);

        Assert.That(top.Select(row => row.Name), Is.EqualTo(new[] { "Top", "alpha", "Beta", "Zed" }));
    }

    [Test]
    public void EnemyRealmsAreTheThreePlayerRealmsAndOnlyDifferentOnes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ServerStatistics165.IsEnemyRealm((int)eRealm.Albion, (int)eRealm.Midgard), Is.True);
            Assert.That(ServerStatistics165.IsEnemyRealm((int)eRealm.Hibernia, (int)eRealm.Midgard), Is.True);
            Assert.That(ServerStatistics165.IsEnemyRealm((int)eRealm.Midgard, (int)eRealm.Midgard), Is.False);
            Assert.That(ServerStatistics165.IsEnemyRealm((int)eRealm.None, (int)eRealm.Albion), Is.False);
            Assert.That(ServerStatistics165.IsEnemyRealm((int)eRealm.Albion, (int)eRealm.None), Is.False);
            Assert.That(ServerStatistics165.IsEnemyRealm((int)eRealm.Door, (int)eRealm.Albion), Is.False);
        });
    }

    [Test]
    public void PlayerHistorySeedsTheBoardsFromTheCharacterTable()
    {
        var character = new DbCoreCharacter
        {
            ObjectId = "seed",
            Name = "Gogu",
            Realm = (int)eRealm.Albion,
            Level = 23,
            KillsAlbionPlayers = 2,
            KillsMidgardPlayers = 3,
            KillsHiberniaPlayers = 5,
            CapturedKeeps = 1,
            CapturedTowers = 4,
        };

        (long pvpKills, int keeps) = ServerStatistics165.HistoricalCounts(character);

        Assert.Multiple(() =>
        {
            Assert.That(pvpKills, Is.EqualTo(10));
            Assert.That(keeps, Is.EqualTo(5));
            Assert.That(ServerStatistics165.HistoricalCounts(null), Is.EqualTo((0L, 0)));
        });
    }

    [Test]
    public void SnapshotSerializesTheNamesTheLauncherReads()
    {
        var snapshot = new ServerStatistics165.Snapshot("request-1", "2026-11-02T10:00:00.0000000Z",
            120, 30, 4567, 8, 9000, 2, 1, 1500,
            [new ServerStatistics165.StatRow("Mobber", "Bot", 1, 42, 77)],
            [], [], []);

        string json = JsonSerializer.Serialize(snapshot);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("RequestId").GetString(), Is.EqualTo("request-1"));
            Assert.That(root.GetProperty("MobKills").GetInt64(), Is.EqualTo(120));
            Assert.That(root.GetProperty("MoneyLootedCopper").GetInt64(), Is.EqualTo(4567));
            Assert.That(root.GetProperty("RealmPoints").GetInt64(), Is.EqualTo(9000));
            Assert.That(root.GetProperty("PlayersTracked").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("BotsTracked").GetInt32(), Is.EqualTo(1500));
            Assert.That(root.GetProperty("TopMobKills")[0].GetProperty("Name").GetString(), Is.EqualTo("Mobber"));
            Assert.That(root.GetProperty("TopMobKills")[0].GetProperty("Kind").GetString(), Is.EqualTo("Bot"));
            Assert.That(root.GetProperty("TopMobKills")[0].GetProperty("Value").GetInt64(), Is.EqualTo(77));
            Assert.That(root.GetProperty("TopDeaths").GetArrayLength(), Is.Zero);
            Assert.That(root.GetProperty("TopPvpKills").GetArrayLength(), Is.Zero);
            Assert.That(root.GetProperty("TopKeepsCaptured").GetArrayLength(), Is.Zero);
        });
    }

    [Test]
    public void EmptyWorldSnapshotIsAllZero()
    {
        ServerStatistics165.Snapshot snapshot = ServerStatistics165.BuildSnapshot("request-2");

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.RequestId, Is.EqualTo("request-2"));
            Assert.That(DateTime.TryParse(snapshot.GeneratedUtc, out _), Is.True);
            Assert.That(snapshot.MobKills, Is.Zero);
            Assert.That(snapshot.Deaths, Is.Zero);
            Assert.That(snapshot.MoneyLootedCopper, Is.Zero);
            Assert.That(snapshot.PvpKills, Is.Zero);
            Assert.That(snapshot.KeepsCaptured, Is.Zero);
            Assert.That(snapshot.TopMobKills, Is.Empty);
            Assert.That(snapshot.TopDeaths, Is.Empty);
            Assert.That(snapshot.TopPvpKills, Is.Empty);
            Assert.That(snapshot.TopKeepsCaptured, Is.Empty);
        });
    }

    /// <summary>
    /// The per-actor counters are saved to state-165/server-stats-state.json so a restart continues them. The record
    /// types are private implementation details, but the file format must round-trip: serialise and read them back by
    /// reflection, exactly as the server does.
    /// </summary>
    [Test]
    public void SavedCountersRoundTripThroughJson()
    {
        Type savedStateType = typeof(ServerStatistics165).GetNestedType("SavedState", BindingFlags.NonPublic);
        Type savedActorType = typeof(ServerStatistics165).GetNestedType("SavedActor", BindingFlags.NonPublic);
        Assert.That(savedStateType, Is.Not.Null);
        Assert.That(savedActorType, Is.Not.Null);

        object actor = Activator.CreateInstance(savedActorType);
        Set(savedActorType, actor, "Key", "bot:42");
        Set(savedActorType, actor, "Name", "Grinder");
        Set(savedActorType, actor, "Kind", "Bot");
        Set(savedActorType, actor, "Realm", 2);
        Set(savedActorType, actor, "Level", 37);
        Set(savedActorType, actor, "MobKills", 1200L);
        Set(savedActorType, actor, "Deaths", 34L);
        Set(savedActorType, actor, "PvpKills", 12L);
        Set(savedActorType, actor, "MoneyLootedCopper", 987654L);
        Set(savedActorType, actor, "KeepsCaptured", 3);
        Array actors = Array.CreateInstance(savedActorType, 1);
        actors.SetValue(actor, 0);

        object state = Activator.CreateInstance(savedStateType);
        Set(savedStateType, state, "SavedUtc", "2026-11-02T10:00:00.0000000Z");
        Set(savedStateType, state, "Actors", actors);

        string json = JsonSerializer.Serialize(state, savedStateType);
        object read = JsonSerializer.Deserialize(json, savedStateType);

        object readActor = ((Array)savedStateType.GetProperty("Actors").GetValue(read)).GetValue(0);
        Assert.Multiple(() =>
        {
            Assert.That(savedStateType.GetProperty("SavedUtc").GetValue(read),
                Is.EqualTo("2026-11-02T10:00:00.0000000Z"));
            Assert.That(savedActorType.GetProperty("Key").GetValue(readActor), Is.EqualTo("bot:42"));
            Assert.That(savedActorType.GetProperty("Name").GetValue(readActor), Is.EqualTo("Grinder"));
            Assert.That(savedActorType.GetProperty("Kind").GetValue(readActor), Is.EqualTo("Bot"));
            Assert.That(savedActorType.GetProperty("Realm").GetValue(readActor), Is.EqualTo(2));
            Assert.That(savedActorType.GetProperty("Level").GetValue(readActor), Is.EqualTo(37));
            Assert.That(savedActorType.GetProperty("MobKills").GetValue(readActor), Is.EqualTo(1200L));
            Assert.That(savedActorType.GetProperty("Deaths").GetValue(readActor), Is.EqualTo(34L));
            Assert.That(savedActorType.GetProperty("PvpKills").GetValue(readActor), Is.EqualTo(12L));
            Assert.That(savedActorType.GetProperty("MoneyLootedCopper").GetValue(readActor), Is.EqualTo(987654L));
            Assert.That(savedActorType.GetProperty("KeepsCaptured").GetValue(readActor), Is.EqualTo(3));
        });
    }

    private static void Set(Type type, object instance, string property, object value) =>
        type.GetProperty(property).SetValue(instance, value);
}
