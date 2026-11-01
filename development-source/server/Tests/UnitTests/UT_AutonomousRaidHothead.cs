using System;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The raid hothead ported from the reference's raid "Leeroy" beat: the 5% roll, the muster/staging charge rules
    /// (ported verbatim) and the period-neutral wording (the reference's 2005 meme must not come back).
    /// </summary>
    [TestFixture]
    public class UT_AutonomousRaidHothead
    {
        [TestCase(0.0, true)]
        [TestCase(0.049, true)]
        [TestCase(0.05, false)]
        [TestCase(0.5, false)]
        [TestCase(0.999, false)]
        public void OneRaidInTwentyRollsAHothead(double draw, bool rolled) =>
            Assert.That(AutonomousRaidHothead.Rolls(draw), Is.EqualTo(rolled));

        [Test]
        public void ForcedRollsAreUsedOnce()
        {
            AutonomousRaidHothead.ForceNextRaid();
            Assert.That(AutonomousRaidHothead.RollForRaid(0.99), Is.True, "forced once");
            Assert.That(AutonomousRaidHothead.RollForRaid(0.99), Is.False, "then the normal roll");
        }

        [Test]
        public void ItChargesOnlyWhenPartOfTheRaidIsThereAndNeverOnARegroup()
        {
            // needed 200: a quarter is 50, and it must not be all of it.
            Assert.That(AutonomousRaidHothead.ShouldCharge(120, 200, 61_000, 0, false), Is.True);
            Assert.That(AutonomousRaidHothead.ShouldCharge(50, 200, 61_000, 0, false), Is.True, "exactly a quarter");
            Assert.That(AutonomousRaidHothead.ShouldCharge(49, 200, 61_000, 0, false), Is.False, "under a quarter");
            Assert.That(AutonomousRaidHothead.ShouldCharge(200, 200, 61_000, 0, false), Is.False, "all of the raid is just the pull");
            Assert.That(AutonomousRaidHothead.ShouldCharge(120, 200, 59_999, 0, false), Is.False, "the muster has not stood a minute");
            Assert.That(AutonomousRaidHothead.ShouldCharge(120, 200, 61_000, 1, false), Is.False, "not a retry after a wipe");
            Assert.That(AutonomousRaidHothead.ShouldCharge(120, 200, 61_000, 0, true), Is.False, "not a regroup");
            Assert.That(AutonomousRaidHothead.ShouldCharge(7, 40, 61_000, 0, false), Is.False, "never fewer than the minimum");
        }

        [Test]
        public void ARaidThatWasReadyAtOnceJumpsThePullInstead()
        {
            Assert.That(AutonomousRaidHothead.JumpsThePull(200, 200, 0, false), Is.True);
            Assert.That(AutonomousRaidHothead.JumpsThePull(8, 8, 0, false), Is.True, "the minimum is enough");
            Assert.That(AutonomousRaidHothead.JumpsThePull(7, 7, 0, false), Is.False, "too few");
            Assert.That(AutonomousRaidHothead.JumpsThePull(200, 200, 1, false), Is.False, "retry");
            Assert.That(AutonomousRaidHothead.JumpsThePull(200, 200, 0, true), Is.False, "regroup");
        }

        [Test]
        public void AtADragonStagingItChargesOnHalfTheRaidAfterThreeMinutes()
        {
            Assert.That(AutonomousRaidHothead.ShouldChargeStaging(120, 200, 3 * 60_000), Is.True);
            Assert.That(AutonomousRaidHothead.ShouldChargeStaging(99, 200, 3 * 60_000), Is.False, "under half");
            Assert.That(AutonomousRaidHothead.ShouldChargeStaging(200, 200, 3 * 60_000), Is.False, "all of it is just the pull");
            Assert.That(AutonomousRaidHothead.ShouldChargeStaging(120, 200, 3 * 60_000 - 1), Is.False, "too early");
            Assert.That(AutonomousRaidHothead.ShouldChargeStaging(7, 8, 3 * 60_000), Is.False, "never fewer than the minimum");
        }

        [Test]
        public void AForcedChargeGoesOnce()
        {
            // Drain a flag a failed earlier run might have left set.
            AutonomousRaidHothead.GoesNow(false, 2);
            AutonomousRaidHothead.ChargeNow();
            Assert.That(AutonomousRaidHothead.GoesNow(false, 2), Is.True, "a forced charge with two raiders there");
            Assert.That(AutonomousRaidHothead.GoesNow(false, 2), Is.False, "used once");
            AutonomousRaidHothead.ChargeNow();
            Assert.That(AutonomousRaidHothead.GoesNow(false, 1), Is.False, "one raider is not enough");
            Assert.That(AutonomousRaidHothead.GoesNow(true, 0), Is.True, "the raid's own rules still go");
        }

        [TestCase(eCharacterClass.Cleric, false)]
        [TestCase(eCharacterClass.Friar, false)]
        [TestCase(eCharacterClass.Druid, false)]
        [TestCase(eCharacterClass.Warden, false)]
        [TestCase(eCharacterClass.Healer, false)]
        [TestCase(eCharacterClass.Shaman, false)]
        [TestCase(eCharacterClass.Paladin, false)]
        [TestCase(eCharacterClass.Bard, false)]
        [TestCase(eCharacterClass.Minstrel, false)]
        [TestCase(eCharacterClass.Skald, false)]
        [TestCase(eCharacterClass.Sorcerer, false)]
        [TestCase(eCharacterClass.Mercenary, true)]
        [TestCase(eCharacterClass.Wizard, true)]
        [TestCase(eCharacterClass.Armsman, true)]
        [TestCase(eCharacterClass.Warrior, true)]
        public void HealersAndSupportNeverChargeAndDamageDealersMay(eCharacterClass characterClass, bool can) =>
            Assert.That(AutonomousRaidHothead.CanBeHothead(characterClass), Is.EqualTo(can));

        [Test]
        public void TheWordingIsPeriodNeutral()
        {
            // The reference's raid beat was a 2005 internet meme ("Leeroy Jenkins", "at least he has chicken").
            // It must not survive in this port: the game is locked to 1 November 2003.
            var lines = AutonomousRaidHothead.Shouts
                .Concat(AutonomousRaidHothead.Reactions)
                .Concat(AutonomousRaidHothead.FactionCalls)
                .Concat(AutonomousRaidHothead.AfterWin)
                .Concat(AutonomousRaidHothead.AfterWipe)
                .ToArray();
            Assert.That(lines, Is.Not.Empty);
            foreach (string line in lines)
            {
                Assert.That(line, Does.Not.Contain("leeroy"), line);
                Assert.That(line, Does.Not.Contain("chicken"), line);
                Assert.That(line.Trim(), Is.Not.Empty);
            }
        }

        [Test]
        public void TheSlotsAreFilledWithoutLeavingPlaceholders()
        {
            foreach (string line in AutonomousRaidHothead.FactionCalls
                         .Concat(AutonomousRaidHothead.AfterWin)
                         .Concat(AutonomousRaidHothead.AfterWipe))
                Assert.That(AutonomousRaidHothead.Fill(line, "Aldric", "Golestandt"), Does.Not.Contain("{"), line);
            Assert.That(AutonomousRaidHothead.Fill("{hothead} and {boss}", "Aldric", "Golestandt"),
                Is.EqualTo("Aldric and Golestandt"));
        }
    }
}
