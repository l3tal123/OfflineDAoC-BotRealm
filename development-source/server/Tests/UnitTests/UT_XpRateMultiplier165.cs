using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The dashboard's XP rate (owner 2026-11-01): one number, stored as the server property xp_rate_multiplier, that
    /// multiplies experience from every source for players and for every kind of gamebot. Pure arithmetic only; the
    /// property is set directly, as the launcher's SQLite row would set it at server start.
    /// </summary>
    [TestFixture]
    public class UT_XpRateMultiplier165
    {
        private double _saved;

        [SetUp]
        public void Save() => _saved = XpRateMultiplier165.XP_RATE_MULTIPLIER;

        [TearDown]
        public void Restore() => XpRateMultiplier165.XP_RATE_MULTIPLIER = _saved;

        [Test]
        public void NormalRateLeavesExperienceAlone()
        {
            XpRateMultiplier165.XP_RATE_MULTIPLIER = 1.0;
            Assert.That(XpRateMultiplier165.Apply(137), Is.EqualTo(137));
            Assert.That(XpRateMultiplier165.Apply(1), Is.EqualTo(1));
        }

        [TestCase(2.5, 100, 250)]
        [TestCase(2.0, 1, 2)]
        [TestCase(5000.0, 1, 5000)]
        [TestCase(1.0, 1937768448, 1937768448)]
        public void RateMultipliesTheGain(double rate, long experience, long expected)
        {
            XpRateMultiplier165.XP_RATE_MULTIPLIER = rate;
            Assert.That(XpRateMultiplier165.Apply(experience), Is.EqualTo(expected));
        }

        [Test]
        public void FractionsRoundAwayFromZero()
        {
            XpRateMultiplier165.XP_RATE_MULTIPLIER = 2.5;
            Assert.That(XpRateMultiplier165.Apply(3), Is.EqualTo(8), "3 x 2.5 = 7.5 rounds to 8");
            Assert.That(XpRateMultiplier165.Apply(1), Is.EqualTo(3), "1 x 2.5 = 2.5 rounds to 3");
        }

        [Test]
        public void LossesAndEmptyGainsAreNeverScaled()
        {
            XpRateMultiplier165.XP_RATE_MULTIPLIER = 2.5;
            Assert.That(XpRateMultiplier165.Apply(-500), Is.EqualTo(-500), "the death penalty is a loss, not a gain");
            Assert.That(XpRateMultiplier165.Apply(0), Is.Zero);
        }

        [Test]
        public void StoredRateIsClampedToTheLauncherRange()
        {
            XpRateMultiplier165.XP_RATE_MULTIPLIER = 0.5;
            Assert.That(XpRateMultiplier165.Rate, Is.EqualTo(1.0), "the minimum is 1");

            XpRateMultiplier165.XP_RATE_MULTIPLIER = 9000.0;
            Assert.That(XpRateMultiplier165.Rate, Is.EqualTo(5000.0), "the maximum is 5000");

            XpRateMultiplier165.XP_RATE_MULTIPLIER = 2.5;
            Assert.That(XpRateMultiplier165.Rate, Is.EqualTo(2.5));
        }

        [Test]
        public void UnusableStoredRateMeansNormal()
        {
            XpRateMultiplier165.XP_RATE_MULTIPLIER = double.NaN;
            Assert.That(XpRateMultiplier165.Rate, Is.EqualTo(1.0));

            XpRateMultiplier165.XP_RATE_MULTIPLIER = double.PositiveInfinity;
            Assert.That(XpRateMultiplier165.Rate, Is.EqualTo(1.0));
            Assert.That(XpRateMultiplier165.Apply(200), Is.EqualTo(200));
        }
    }
}
