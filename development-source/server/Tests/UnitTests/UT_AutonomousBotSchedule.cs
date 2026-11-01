using System;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The living-server bot schedule ported from the reference's AutonomousBotSchedule.cs: parsing, the deterministic
    /// session/clock math, and the default-off behaviour (nothing changes unless bot_schedule_mode / bot_living_breaks
    /// are turned on).
    /// </summary>
    [TestFixture]
    public class UT_AutonomousBotSchedule
    {
        [TestCase("off", AutonomousBotSchedule.Mode.Off)]
        [TestCase("OFF", AutonomousBotSchedule.Mode.Off)]
        [TestCase("", AutonomousBotSchedule.Mode.Off)]
        [TestCase(null, AutonomousBotSchedule.Mode.Off)]
        [TestCase("bogus", AutonomousBotSchedule.Mode.Off)]
        [TestCase("session", AutonomousBotSchedule.Mode.Session)]
        [TestCase(" Session ", AutonomousBotSchedule.Mode.Session)]
        [TestCase("clock", AutonomousBotSchedule.Mode.Clock)]
        public void TheModeIsParsedFromTheSetting(string value, AutonomousBotSchedule.Mode expected) =>
            Assert.That(AutonomousBotSchedule.Parse(value), Is.EqualTo(expected));

        [Test]
        public void TheDrawsAreDeterministicAndStayInUnitRange()
        {
            for (long id = 0; id < 200; id++)
            {
                Assert.That(AutonomousBotSchedule.Mix(id, 7), Is.EqualTo(AutonomousBotSchedule.Mix(id, 7)), "stable mix");
                double unit = AutonomousBotSchedule.Unit(id, 7);
                Assert.That(unit, Is.GreaterThanOrEqualTo(0.0).And.LessThan(1.0), id.ToString());
            }
            Assert.That(AutonomousBotSchedule.Unit(1, 1), Is.Not.EqualTo(AutonomousBotSchedule.Unit(2, 1)), "different bots draw differently");
        }

        [Test]
        public void SessionLengthsStayInsideOneToFourHoursOnAndTwentyToTwoHoursOff()
        {
            for (long id = 1; id <= 500; id++)
            {
                (int online, int away) = AutonomousBotSchedule.SessionLengths(id);
                Assert.That(online, Is.InRange(60, 240), id.ToString());
                Assert.That(away, Is.InRange(20, 120), id.ToString());
                Assert.That(AutonomousBotSchedule.SessionLengths(id), Is.EqualTo((online, away)), "deterministic");
            }
        }

        [Test]
        public void ASessionBotIsBothOnlineAndAwayOverADay()
        {
            bool sawOnline = false, sawAway = false;
            for (int minute = 0; minute < 24 * 60; minute++)
            {
                if (AutonomousBotSchedule.SessionOnline(42, TimeSpan.FromMinutes(minute))) sawOnline = true;
                else sawAway = true;
            }
            Assert.That(sawOnline, Is.True);
            Assert.That(sawAway, Is.True);
        }

        [Test]
        public void TheClockCurveIsBusyInTheEveningAndThinAtFive()
        {
            DateTime weekday = new(2026, 11, 3);   // a Tuesday
            DateTime evening = weekday.AddHours(20);
            DateTime dawn = weekday.AddHours(5);
            Assert.That(AutonomousBotSchedule.ClockShare(evening), Is.GreaterThan(0.9));
            Assert.That(AutonomousBotSchedule.ClockShare(dawn), Is.LessThan(0.3));

            DateTime weekendDawn = new DateTime(2026, 11, 7).AddHours(5);   // a Saturday
            Assert.That(AutonomousBotSchedule.ClockShare(weekendDawn), Is.GreaterThan(AutonomousBotSchedule.ClockShare(dawn)));
        }

        [Test]
        public void TheClockKeepsRoughlyTheShareOfBotsOnline()
        {
            DateTime weekday = new(2026, 11, 3);
            int evening = 0, dawn = 0;
            for (long id = 1; id <= 1_000; id++)
            {
                if (AutonomousBotSchedule.ClockOnline(id, weekday.AddHours(20))) evening++;
                if (AutonomousBotSchedule.ClockOnline(id, weekday.AddHours(5))) dawn++;
            }
            Assert.That(evening, Is.InRange(700, 1_000), "evening crowd");
            Assert.That(dawn, Is.InRange(50, 350), "5 a.m. crowd");
            Assert.That(dawn, Is.LessThan(evening));
        }

        [Test]
        public void OffSchedulesEverybodyOnlineWhateverTheLengthOfTheDay()
        {
            TimeSpan sinceStart = TimeSpan.FromHours(9);
            DateTime local = new(2026, 11, 3, 5, 30, 0);
            for (long id = 1; id <= 50; id++)
                Assert.That(AutonomousBotSchedule.ScheduledOnline(AutonomousBotSchedule.Mode.Off, id, sinceStart, local), Is.True);
        }

        [Test]
        public void WithEverythingOffEveryBotIsWantedOnline()
        {
            // The server properties are not loaded in a unit test, so the static defaults are the attribute defaults:
            // bot_schedule_mode is off and bot_living_breaks is false.
            Assert.That(AutonomousBotSchedule.Active, Is.False);
            for (long id = 1; id <= 20; id++)
                Assert.That(AutonomousBotSchedule.WantsOnline(new OfflineWorldBotRecord { BotId = id }, DateTime.UtcNow), Is.True);
            Assert.That(AutonomousBotSchedule.WantsOnline(null, DateTime.UtcNow), Is.False);
        }
    }
}
