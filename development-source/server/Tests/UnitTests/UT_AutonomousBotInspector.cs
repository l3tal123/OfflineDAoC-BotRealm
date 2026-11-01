using System;
using System.Linq;
using System.Text.Json;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture, NonParallelizable]
public sealed class UT_AutonomousBotInspector
{
    [TestCase("abc123|42", "abc123", 42L)]
    [TestCase("  abc123 | 42  ", "abc123", 42L)]
    [TestCase("request-id|1", "request-id", 1L)]
    public void TryParseRequest_ReadsARequestIdAndABotId(string request, string expectedId, long expectedBot)
    {
        Assert.That(AutonomousBotInspector.TryParseRequest(request, out string requestId, out long botId), Is.True);
        Assert.That(requestId, Is.EqualTo(expectedId));
        Assert.That(botId, Is.EqualTo(expectedBot));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("only-a-request-id")]
    [TestCase("id|")]
    [TestCase("|7")]
    [TestCase("id|0")]
    [TestCase("id|-4")]
    [TestCase("id|not-a-number")]
    [TestCase("id|7|8")]
    public void TryParseRequest_RejectsMalformedRequests(string request)
    {
        Assert.That(AutonomousBotInspector.TryParseRequest(request, out _, out _), Is.False);
    }

    [Test]
    public void TryParseRequest_RejectsAnOverlongRequestId()
    {
        string request = new string('x', 129) + "|5";
        Assert.That(AutonomousBotInspector.TryParseRequest(request, out _, out _), Is.False);
    }

    [Test]
    public void SpecPointsSpent_ChargesEveryLineRaiseAndSkipsGrantedLines()
    {
        var specs = new[]
        {
            new AutonomousBotInspector.SpecRow("Slash", 1, true),      // never raised: nothing spent
            new AutonomousBotInspector.SpecRow("Thrust", 5, true),     // 2+3+4+5 = 14
            new AutonomousBotInspector.SpecRow("Two Handed", 50, true), // 2+..+50 = 1274
            new AutonomousBotInspector.SpecRow("Realm Abilities", 9, false), // granted: not paid for
        };
        Assert.That(AutonomousBotInspector.SpecPointsSpent(specs), Is.EqualTo(14 + 1274));
    }

    [Test]
    public void SpecPointsSpent_IsZeroForAnUntrainedBot()
    {
        Assert.That(AutonomousBotInspector.SpecPointsSpent(Array.Empty<AutonomousBotInspector.SpecRow>()), Is.EqualTo(0));
        Assert.That(AutonomousBotInspector.SpecPointsSpent(
            new[] { new AutonomousBotInspector.SpecRow("Crush", 1, true) }), Is.EqualTo(0));
    }

    [Test]
    public void SlotLabel_NamesEquipmentAndKeepsBackpackAndVaultNumbers()
    {
        Assert.That(AutonomousBotInspector.SlotLabel(DOL.GS.eInventorySlot.RightHandWeapon), Is.EqualTo("Right hand"));
        Assert.That(AutonomousBotInspector.SlotLabel(DOL.GS.eInventorySlot.TorsoArmor), Is.EqualTo("Torso"));
        Assert.That(AutonomousBotInspector.SlotLabel(DOL.GS.eInventorySlot.LeftRing), Is.EqualTo("Left ring"));
        Assert.That(AutonomousBotInspector.SlotLabel(DOL.GS.eInventorySlot.FirstBackpack), Does.StartWith("Bag 1"));
        Assert.That(AutonomousBotInspector.SlotLabel(DOL.GS.eInventorySlot.LastBackpack), Does.StartWith("Bag 4"));
        Assert.That(AutonomousBotInspector.SlotLabel(DOL.GS.eInventorySlot.FirstVault), Does.StartWith("Vault"));
    }

    [Test]
    public void Capture_WithoutABotIdExplainsThatNoBotWasChosen()
    {
        AutonomousBotInspector.Snapshot snapshot = AutonomousBotInspector.Capture(0);
        Assert.That(snapshot.Found, Is.True);
        Assert.That(snapshot.Online, Is.False);
        Assert.That(snapshot.Error, Is.Not.Empty);
        Assert.That(snapshot.Equipment, Is.Not.Null);
        Assert.That(snapshot.Bag, Is.Not.Null);
    }

    [Test]
    public void Snapshot_SerializesAndDeserializesForTheLauncher()
    {
        AutonomousBotInspector.Snapshot original = AutonomousBotInspector.Capture(0);
        string json = JsonSerializer.Serialize(original);
        AutonomousBotInspector.Snapshot decoded = JsonSerializer.Deserialize<AutonomousBotInspector.Snapshot>(json);

        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded.BotId, Is.EqualTo(original.BotId));
        Assert.That(decoded.Error, Is.EqualTo(original.Error));
        Assert.That(decoded.Equipment.Length, Is.EqualTo(0));
        Assert.That(decoded.Bag.Length, Is.EqualTo(0));
        Assert.That(decoded.Effects.Length, Is.EqualTo(0));
    }
}
