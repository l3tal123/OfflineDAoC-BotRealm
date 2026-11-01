using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The living-world death diagnostic's pure naming rule: what kind of thing killed an autonomous gamebot. The line
    /// itself is written through the existing BOT_DEATH_165 log (AutonomousDeathDiagnostics.Record); this fixture covers
    /// the part that needs no world: the killer kind, and that a death which is not an autonomous bot's records nothing.
    /// </summary>
    [TestFixture]
    public class UT_AutonomousDeathDiagnostics
    {
        private static GameObject Uninitialized<T>() where T : GameObject =>
            (GameObject)RuntimeHelpers.GetUninitializedObject(typeof(T));

        [Test]
        public void AKillersKindNamesWhatKilledTheBot()
        {
            Assert.That(AutonomousDeathDiagnostics.KillerKind(null), Is.EqualTo("none"));
            Assert.That(AutonomousDeathDiagnostics.KillerKind(Uninitialized<GamePlayer>()), Is.EqualTo("player"));
            Assert.That(AutonomousDeathDiagnostics.KillerKind(Uninitialized<GameBot>()), Is.EqualTo("bot"));
            // A plain monster is not tested here: KillerKind's "monster"/"pet" arms read GameNPC.Brain, which needs a
            // constructed NPC (the effect list behind it), so that case is covered in game by the BOT_DEATH_165 log.
        }

        [Test]
        public void ADeathThatIsNotAnAutonomousBotsRecordsNothing()
        {
            Assert.That(AutonomousDeathDiagnostics.Record(null, null, "grinding"), Is.Null);
            Assert.That(AutonomousDeathDiagnostics.Record((GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot)), null, "grinding"),
                Is.Null, "an actor that is not an autonomous world bot");
        }
    }
}
