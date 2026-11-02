using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture]
public sealed class UT_TemporaryGroupStableTravel
{
    [Test]
    public void OnlyInRangeTemporaryGroupedFollowersMirrorAPlayersRide()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TemporaryGroupStableTravel.IsEligibleForMirroredRide(true, true, true, true, true, 650), Is.True);
            Assert.That(TemporaryGroupStableTravel.IsEligibleForMirroredRide(false, true, true, true, true, 10), Is.False);
            Assert.That(TemporaryGroupStableTravel.IsEligibleForMirroredRide(true, true, true, false, true, 10), Is.False);
            Assert.That(TemporaryGroupStableTravel.IsEligibleForMirroredRide(true, true, true, true, false, 10), Is.False);
            Assert.That(TemporaryGroupStableTravel.IsEligibleForMirroredRide(true, true, true, true, true, 651), Is.False);
        });
    }

    [Test]
    public void ConfirmedSameZoneMoveToRelocatesAndPreservesEligibleGroupHelpers()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TemporaryGroupStableTravel.ShouldRelocateForOwnerTransfer(true, true, true, true, 1, 1, 10, 10), Is.True);
            Assert.That(TemporaryGroupStableTravel.ShouldRelocateForOwnerTransfer(true, true, false, true, 1, 1, 10, 10), Is.False,
                "the helper must still belong to the player's existing group");
        });
    }

    [Test]
    public void ConfirmedCrossZoneAndCrossRegionTransferRelocateOnlyTemporaryGroupedFollowers()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TemporaryGroupStableTravel.ShouldRelocateForOwnerTransfer(true, true, true, true, 1, 2, 10, 10), Is.True);
            Assert.That(TemporaryGroupStableTravel.ShouldRelocateForOwnerTransfer(true, true, true, true, 1, 1, 10, 11), Is.True);
            Assert.That(TemporaryGroupStableTravel.ShouldRelocateForOwnerTransfer(true, false, true, true, 1, 2, 10, 11), Is.False,
                "persistent bots are isolated from companion transfer cohesion");
        });
    }

    [Test]
    public void OrdinaryWalkingAcrossBoundaryDoesNotTriggerTeleportCohesion()
    {
        Assert.That(TemporaryGroupStableTravel.ShouldRelocateForOwnerTransfer(false, true, true, true, 1, 1, 10, 11), Is.False);
        Assert.That(TemporaryGroupStableTravel.ShouldRelocateForOwnerTransfer(false, true, true, true, 1, 2, 10, 10), Is.False);
    }

    // Owner 2026-11-01: hired companions stayed behind after a teleporter or a release to the bind point.
    [Test]
    public void TransferCohesionIncludesHiredCompanionsButNotGamebots()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TemporaryGroupStableTravel.IsTransferCohesionCompanion(true, false), Is.True, "a /spawn helper");
            Assert.That(TemporaryGroupStableTravel.IsTransferCohesionCompanion(false, true), Is.True, "a hired companion");
            Assert.That(TemporaryGroupStableTravel.IsTransferCohesionCompanion(true, true), Is.True);
            Assert.That(TemporaryGroupStableTravel.IsTransferCohesionCompanion(false, false), Is.False,
                "a persistent gamebot in a player's group is never moved");
        });
    }
}
