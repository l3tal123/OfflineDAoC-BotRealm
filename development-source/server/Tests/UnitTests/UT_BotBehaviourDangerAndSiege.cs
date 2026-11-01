using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// Danger memory and siege-balance behaviour ported from the Living Server reference: boss danger spots, the route
/// bends around them, a bot's own repeat-death spots, and the drift of the siege defense share with recent results.
/// </summary>
[TestFixture]
public sealed class UT_BotBehaviourDangerAndSiege
{
    [Test]
    public void BotsKeepAwayFromWhereAMonsterFarAboveThemKilledOne()
    {
        AutonomousPveBossDanger.Clear();
        var now = new DateTime(2026, 10, 19, 13, 0, 0, DateTimeKind.Utc);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousPveBossDanger.Marks("monster", 30, 12), Is.True, "merucha 30 on a level 12");
                Assert.That(AutonomousPveBossDanger.Marks("monster", 21, 12), Is.False, "nine levels above");
                Assert.That(AutonomousPveBossDanger.Marks("bot", 50, 12), Is.False, "enemy bots are RvR, not a camp to avoid");
                AutonomousPveBossDanger.Record(200, 332000, 431500, 30, now);
                Assert.That(AutonomousPveBossDanger.IsNear(200, 333500, 432500, 12, now.AddHours(1)), Is.True);
                Assert.That(AutonomousPveBossDanger.IsNear(200, 336000, 431500, 12, now), Is.False, "4,000 away");
                Assert.That(AutonomousPveBossDanger.IsNear(100, 332000, 431500, 12, now), Is.False, "another region");
                Assert.That(AutonomousPveBossDanger.IsNear(200, 332000, 431500, 28, now), Is.False,
                    "the same camp is ordinary for a level 28");
                Assert.That(AutonomousPveBossDanger.IsNear(200, 332000, 431500, 12, now.AddHours(7)), Is.False, "forgotten after 6 hours");
                Assert.That(AutonomousPveBossDanger.RouteCrosses(200, 320000, 431000, 345000, 432000, 12, now), Is.True,
                    "a straight walk past the camp");
                Assert.That(AutonomousPveBossDanger.RouteCrosses(200, 320000, 440000, 345000, 440000, 12, now), Is.False,
                    "8,500 units to the side");
            });
        }
        finally { AutonomousPveBossDanger.Clear(); }
    }

    [Test]
    public void ALevellingBotBendsAroundAPackFarAboveIt()
    {
        // The Connacht merucha pack (levels 30-33) on a level 12's road north.
        var mobs = new[]
        {
            new AutonomousPveDangerBypass.MobView(new(332070, 431451), 30, "merucha", true),
            new AutonomousPveDangerBypass.MobView(new(332839, 430379), 30, "merucha", true),
            new AutonomousPveDangerBypass.MobView(new(333067, 431961), 33, "merucha", true),
            new AutonomousPveDangerBypass.MobView(new(331000, 429000), 11, "hill toad", true),   // blue: not a danger
        };
        var dangers = AutonomousPveDangerBypass.BuildDangers(12, mobs, null);
        Vector2 start = new(332400, 428200), goal = new(332400, 434500);
        var bends = AutonomousPveDangerBypass.BypassCandidates(start, goal, dangers);
        Assert.Multiple(() =>
        {
            Assert.That(dangers, Has.Count.EqualTo(3));
            Assert.That(dangers[0].Radius, Is.EqualTo(1650), "18 levels above: the widest circle");
            Assert.That(bends, Is.Not.Empty, "a way around inside the 6,000-unit detour limit");
            Assert.That(AutonomousPveDangerBypass.LegClear(start, bends[0], dangers), Is.True);
            Assert.That(AutonomousPveDangerBypass.LegClear(bends[0], goal, dangers), Is.True);
            Assert.That(AutonomousPveDangerBypass.BuildDangers(31, mobs, null), Is.Empty, "the same pack is no danger for a level 31");
            Assert.That(AutonomousPveDangerBypass.BypassCandidates(start, new(340000, 428200), dangers), Is.Empty,
                "a road that doesn't pass the pack is not bent");
            var remembered = AutonomousPveDangerBypass.BuildDangers(12, null, new[] { (332400, 431300, 30) });
            Assert.That(AutonomousPveDangerBypass.BypassCandidates(start, goal, remembered), Is.Not.Empty,
                "a remembered death spot is bent around even when its killer isn't in sight");
        });
    }

    [Test]
    public void TheBendLooksAboutThreeThousandUnitsAhead()
    {
        Vector3[] route = [new(0, 0, 0), new(2000, 0, 0), new(2000, 4000, 0)];
        Assert.That(AutonomousPveDangerBypass.PointAlong(route, 3000), Is.EqualTo(new Vector3(2000, 1000, 0)));
        Assert.That(AutonomousPveDangerBypass.PointAlong(route, 99999), Is.EqualTo(route[^1]));
    }

    [Test]
    public void ThreeDeathsInOnePlaceMakeItTheBotsOwnDangerSpot()
    {
        DateTime now = new(2026, 10, 20, 4, 0, 0, DateTimeKind.Utc);
        var deaths = new List<AutonomousRepeatDeathSpots.Death>
        {
            new(200, 368211, 652323, now.AddMinutes(-50)),
            new(200, 367881, 653097, now.AddMinutes(-30)),
        };
        Assert.That(AutonomousRepeatDeathSpots.Spots(deaths, 200, now), Is.Empty, "two deaths are not a pattern yet");

        deaths.Add(new(200, 368177, 652326, now.AddMinutes(-5)));
        var spots = AutonomousRepeatDeathSpots.Spots(deaths, 200, now);
        Assert.That(spots, Has.Count.EqualTo(1));
        Assert.That(spots[0], Is.EqualTo((368177, 652326)), "the latest death marks the spot");
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRepeatDeathSpots.Spots(deaths, 100, now), Is.Empty, "another region");
            Assert.That(AutonomousRepeatDeathSpots.Spots(deaths, 200, now.AddHours(3)), Is.Empty, "deaths older than three hours are forgotten");
        });

        deaths.Add(new(200, 300000, 600000, now.AddMinutes(-1)));   // far away: not part of the cluster
        Assert.That(AutonomousRepeatDeathSpots.Spots(deaths, 200, now), Has.Count.EqualTo(1));
    }

    [Test]
    public void ABotsOwnSpotsKeepItsCampsAwayAndForgetOtherBots()
    {
        AutonomousRepeatDeathSpots.Clear();
        try
        {
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < 3; i++)
                AutonomousRepeatDeathSpots.Record(42, 200, 368200 + i * 50, 652300, now.AddMinutes(-i));
            var spots = AutonomousRepeatDeathSpots.AllSpots(42, now);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRepeatDeathSpots.IsNear(spots, 200, 369000, 652800), Is.True, "a camp beside the spot");
                Assert.That(AutonomousRepeatDeathSpots.IsNear(spots, 200, 380000, 652300), Is.False, "a camp well away");
                Assert.That(AutonomousRepeatDeathSpots.AllSpots(43, now), Is.Empty, "another bot's deaths are its own");
            });
        }
        finally { AutonomousRepeatDeathSpots.Clear(); }
    }

    [Test]
    public void SiegeDefenseShareFollowsRecentResults()
    {
        SiegeDefenseBalance.Reset();
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(SiegeDefenseBalance.NextShift(0, new[] { true, true }), Is.Zero, "too few results");
                Assert.That(SiegeDefenseBalance.NextShift(0, new[] { true, true, true }), Is.EqualTo(SiegeDefenseBalance.Step),
                    "attackers took every keep: more defenders");
                Assert.That(SiegeDefenseBalance.NextShift(0, new[] { true, false, true, false, false, false }),
                    Is.EqualTo(-SiegeDefenseBalance.Step), "attackers lost most: fewer defenders");
                Assert.That(SiegeDefenseBalance.NextShift(0, new[] { true, true, false }), Is.Zero, "two in three: unchanged");
                Assert.That(SiegeDefenseBalance.NextShift(0.25, new[] { true, true, true }), Is.EqualTo(0.25),
                    "never more than 0.25 off the setting");
                Assert.That(RealmEventPolicy.DefenseRatio, Is.EqualTo(0.5), "our configured base share is kept");
            });

            // Recording three attacker wins lifts the share one step above our 0.5 base, and it stays inside the bounds.
            for (int i = 0; i < 3; i++)
                SiegeDefenseBalance.Record(true);
            Assert.That(SiegeDefenseBalance.Ratio(RealmEventPolicy.DefenseRatio),
                Is.EqualTo(0.5 + SiegeDefenseBalance.Step).Within(1e-9));
            for (int i = 0; i < 12; i++)
                SiegeDefenseBalance.Record(true);
            Assert.That(SiegeDefenseBalance.Ratio(RealmEventPolicy.DefenseRatio),
                Is.LessThanOrEqualTo(0.5 + SiegeDefenseBalance.MaximumShift + 1e-9), "bounded by the reference maximum");
        }
        finally { SiegeDefenseBalance.Reset(); }
    }

    [Test]
    public void DefendersFollowTheLargerOfMarchedAndCurrentAttackers()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RealmEventPolicy.DefendersFor(10, 50, 0.5, 128), Is.EqualTo(25), "the bigger army sizes the defense");
            Assert.That(RealmEventPolicy.DefendersFor(50, 50, 0.5, 128), Is.EqualTo(25));
            Assert.That(RealmEventPolicy.DefendersFor(0, 1, 0.5, 128), Is.EqualTo(8), "at least eight");
            Assert.That(RealmEventPolicy.DefendersFor(300, 400, 0.5, 128), Is.EqualTo(128), "never above the cap");
            Assert.That(RealmEventPolicy.DefendersFor(10, 10, 0.0, 128), Is.EqualTo(8), "ratio clamped and floored at eight");
        });
    }
}
