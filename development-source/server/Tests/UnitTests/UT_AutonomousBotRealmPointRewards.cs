using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Database.Handlers;
using DOL.GS;
using DOL.GS.ServerProperties;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public class UT_AutonomousBotRealmPointRewards
{
    private sealed class RewardServer : GameServer
    {
        protected override IServerRules ServerRulesImpl => new NormalServerRules();
        protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
    }

    private static readonly IObjectDatabase EmptyDatabase =
        DispatchProxy.Create<IObjectDatabase, EmptyReadDatabase>();

    public class EmptyReadDatabase : DispatchProxy
    {
        protected override object Invoke(MethodInfo method, object[] args)
        {
            Type result = method.ReturnType;
            if (result.IsGenericType && typeof(IEnumerable).IsAssignableFrom(result))
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(result.GetGenericArguments()[0]));
            return null;
        }
    }

    private GameServer _previousServer;

    [SetUp]
    public void SetUp()
    {
        _previousServer = GameServer.Instance;
        GameServer.LoadTestDouble((RewardServer)RuntimeHelpers.GetUninitializedObject(typeof(RewardServer)));
    }

    [TearDown]
    public void TearDown() => GameServer.LoadTestDouble(_previousServer);

    private sealed class RewardBot : GameBot
    {
        private RewardBot() : base((OfflineWorldBotRecord)null) { }
        public override byte Level { get; set; }
        public override eRealm Realm { get; set; }
    }

    private static void SetField(Type type, object target, string name, object value) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static RewardBot Bot(bool autonomous, bool temporary)
    {
        RewardBot bot = (RewardBot)RuntimeHelpers.GetUninitializedObject(typeof(RewardBot));
        SetField(typeof(GameLiving), bot, "<TempProperties>k__BackingField", new PropertyCollection());
        SetField(typeof(GameNPC), bot, "m_brains", new ArrayList());
        typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot))!.SetValue(bot, autonomous);
        typeof(GameBot).GetProperty(nameof(GameBot.IsTemporaryGroupHelper))!.SetValue(bot, temporary);
        SetField(typeof(GameNPC), bot, "m_ownBrain", new BotBrain { Body = bot });
        return bot;
    }

    [Test]
    public void OnlyPersistentAutonomousBotIsRealmPointVictim()
    {
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(Bot(true, false)), Is.True);
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(Bot(false, true)), Is.False,
            "/spawn companions must never be worth realm points");
        GameNPC ordinaryNpc = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
        SetField(typeof(GameNPC), ordinaryNpc, "m_brains", new ArrayList());
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(ordinaryNpc), Is.False,
            "ordinary NPCs and pets remain on the zero-RP NPC path");
    }

    [TestCase(25, 0, 25)]
    [TestCase(35, 0, 225)]
    [TestCase(35, 20, 245)]
    [TestCase(50, 0, 900)]
    [TestCase(50, 90, 990)]
    public void VictimValueMatchesPre181PlayerFormula(int level, int realmLevel, int expected)
    {
        Assert.That(AutonomousBotRealmPointRewards.GetPlayerEquivalentRealmPointValue(
            (byte)level, realmLevel), Is.EqualTo(expected));
    }

    [Test]
    public void RewardMatchesPlayerKillCapDamageRankAndGroupFormula()
    {
        int actual = AutonomousBotRealmPointRewards.CalculateRealmPointReward(
            victimRealmPointValue: 990,
            victimRealmLevel: 90,
            awarderRealmPointValue: 900,
            awarderRealmLevel: 0,
            participantCount: 2,
            groupContributorCount: 2,
            damagePercent: 0.75,
            applyRealmRankAdjustment: true);

        int baseShare = 990 / 2;
        int damageShare = (int)(baseShare * 0.75);
        int rankAdjusted = (int)(damageShare * (1.0 + 2.0 * 90 / 900.0));
        int expected = rankAdjusted + (int)(rankAdjusted * 0.125);
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void CompanionAndControlledPetResolveToPlayerOwnerWithoutBecomingVictims()
    {
        GamePlayer owner = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));
        RewardBot companion = Bot(false, true);
        typeof(GameBot).GetProperty(nameof(GameBot.Owner))!.SetValue(companion, owner);
        GameNPC pet = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
        SetField(typeof(GameNPC), pet, "m_brains", new ArrayList());
        SetField(typeof(GameNPC), pet, "m_ownBrain", new ControlledMobBrain(companion) { Body = pet });

        Assert.That(AutonomousBotRealmPointRewards.ResolveRootRewardOwner(companion), Is.SameAs(owner));
        Assert.That(AutonomousBotRealmPointRewards.ResolveRootRewardOwner(pet), Is.SameAs(owner));
        Assert.That(AutonomousRvrDefense.IsCombatant(owner), Is.True);
        Assert.That(AutonomousRvrDefense.IsCombatant(companion), Is.True);
        Assert.That(AutonomousRvrDefense.IsCombatant(pet), Is.True);
        SetField(typeof(GameNPC), pet, "m_ownBrain", new ControlledMobBrain(owner) { Body = pet });
        Assert.That(AutonomousRvrDefense.IsCombatant(pet), Is.True, "Direct human-owned pets are not IGamePlayer but must be defended against");
        Assert.That(AutonomousRvrDefense.IsCombatant(null), Is.False);
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(companion), Is.False);
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(pet), Is.False);
    }

    // A gamebot has no worth values of its own; in True 1.65 it is worth what a player of its level and realm level
    // would be (owner 2026-11-02: bots are players in every aspect).

    [Test]
    public void BotVictimIsWorthPlayerExperienceForItsLevel()
    {
        RewardBot bot = Bot(true, false);
        bot.Level = 25;

        Assert.That(PlayerEquivalentKillRewards165.ExperienceValue(bot),
            Is.EqualTo(GameLiving.XPForLiving[25] * 4),
            "a gamebot reports the living XP value; a player of the same level is worth four times it");
    }

    [TestCase(1, 1)]
    [TestCase(25, 16)]
    [TestCase(35, 22)]
    [TestCase(50, 31)]
    public void BotVictimBountyPointValueMatchesPlayerFormula(int level, int expected)
    {
        Assert.That(PlayerEquivalentKillRewards165.BountyPointsValueForLevel(level), Is.EqualTo(expected));
    }

    [TestCase(1)]
    [TestCase(25)]
    [TestCase(35)]
    [TestCase(50)]
    [TestCase(60)]
    public void BotVictimMoneyValueMatchesPlayerFormula(int level)
    {
        int row = Math.Min(level, GamePlayer.prcRestore.Length - 1);
        Assert.That(PlayerEquivalentKillRewards165.MoneyValueForLevel(level),
            Is.EqualTo(3 * (long)GamePlayer.prcRestore[row]));
    }

    [Test]
    public void BotVictimWorthMatchesAPlayerOfItsLevel()
    {
        RewardBot bot = Bot(true, false);
        bot.Level = 35;

        Assert.That(PlayerEquivalentKillRewards165.ExperienceValue(bot), Is.EqualTo(GameLiving.XPForLiving[35] * 4));
        Assert.That(PlayerEquivalentKillRewards165.BountyPointsValue(bot),
            Is.EqualTo(PlayerEquivalentKillRewards165.BountyPointsValueForLevel(35)));
        Assert.That(PlayerEquivalentKillRewards165.MoneyValue(bot),
            Is.EqualTo(PlayerEquivalentKillRewards165.MoneyValueForLevel(35)));
    }

    [Test]
    public void PlayerKillCapsAreThePlayerRules()
    {
        // The XP cap is a server property; the unpacked test double has no server properties table, so set the
        // production default and put the field back afterwards.
        int previousCap = Properties.XP_PVP_CAP_PERCENT;
        Properties.XP_PVP_CAP_PERCENT = 125;

        try
        {
            Assert.That(PlayerEquivalentKillRewards165.CappedExperience(8000, 20000), Is.EqualTo(10000),
                "a player kill pays at most 125% of the awarder's level value");
            Assert.That(PlayerEquivalentKillRewards165.CappedExperience(8000, 5000), Is.EqualTo(5000),
                "a small kill is not raised to the cap");
            Assert.That(PlayerEquivalentKillRewards165.CappedBountyPoints(20, 500), Is.EqualTo(40),
                "at most twice the awarder's bounty point value");
            Assert.That(PlayerEquivalentKillRewards165.CappedMoney(300, 5000), Is.EqualTo(600),
                "at most twice the awarder's money value");
        }
        finally
        {
            Properties.XP_PVP_CAP_PERCENT = previousCap;
        }
    }
}
