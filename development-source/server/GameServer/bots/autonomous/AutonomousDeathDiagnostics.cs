using System;
using System.Reflection;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// One server-log fragment per autonomous gamebot death (appended to the existing BOT_DEATH_165 line): the kind of
    /// killer, whether the bot had arrived at its camp, what it was doing and where, and the danger memory written for it.
    /// The overnight log of 2026-10-19 had 3,553 deaths and nothing that named a killer, so the cause of the half that
    /// happen on the way to a camp could only be guessed.
    ///
    /// Ported from the Living Server's AutonomousDeathDiagnostics: the reference writes its own AUTONOMOUS_BOT_DEATH line;
    /// this fork keeps its established BOT_DEATH_165 / BOT_DEATH_POS tags and appends this fragment to the former, so the
    /// log has one death line, not two. The danger records are the reference's, in the same order and under the same
    /// guards (AutonomousPveBossDanger and AutonomousRepeatDeathSpots, which live in bots/autonomous).
    /// </summary>
    public static class AutonomousDeathDiagnostics
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>The killer's kind: a monster, a pet, an enemy bot, a real player or something else.</summary>
        public static string KillerKind(GameObject killer) => killer switch
        {
            null => "none",
            GamePlayer => "player",
            GameBot => "bot",
            GameNPC { Brain: DOL.AI.Brain.IControlledBrain } => "pet",
            GameNPC => "monster",
            _ => killer.GetType().Name,
        };

        /// <summary>
        /// Records the reference's danger memory for this death (a boss far above the bot's level, and a spot where the
        /// same bot keeps dying) and returns the fragment to append to BOT_DEATH_165. Null when the dead actor is not an
        /// autonomous world bot, so a player or a companion writes nothing.
        /// </summary>
        public static string Record(GameBot bot, GameObject killer, string activityBefore)
        {
            if (bot?.IsAutonomousWorldBot != true)
                return null;

            string kind = KillerKind(killer);
            string danger = "none";
            try
            {
                // Same guard order as the reference: a deadly boss marks a route spot, then a monster kill feeds the
                // repeat-death-spot memory that route and camp planning read.
                if (killer is GameLiving deadly && AutonomousPveBossDanger.Marks(kind, deadly.Level, bot.Level))
                {
                    AutonomousPveBossDanger.Record(bot.CurrentRegionID, bot.X, bot.Y, deadly.Level, DateTime.UtcNow);
                    danger = "boss";
                }

                if (string.Equals(kind, "monster", StringComparison.Ordinal))
                {
                    AutonomousRepeatDeathSpots.Record(bot.DatabaseID, bot.CurrentRegionID, bot.X, bot.Y, DateTime.UtcNow);
                    danger = danger == "boss" ? "boss+spot" : "spot";
                }
            }
            catch (Exception exception)
            {
                Log.Warn($"BOT_DEATH danger memory failed for bot {bot.Name}: {exception.Message}");
            }

            try
            {
                int attackers = bot.attackComponent?.AttackerTracker?.Count ?? 0;
                bool arrived = bot.GoalDiagnosticAttempt?.ArrivedUtc != null;
                string activity = string.IsNullOrWhiteSpace(activityBefore) ? "-" : activityBefore.Trim();
                return $"killer_kind={kind} attackers={attackers} arrivedAtCamp={arrived} " +
                       $"activity=\"{activity}\" goal=\"{bot.PersistentRecord?.CurrentGoal}\" " +
                       $"camp=\"{bot.PersistentRecord?.CurrentCampId}\" zone=\"{bot.CurrentZone?.Description}\" " +
                       $"region={bot.CurrentRegionID} danger={danger}";
            }
            catch (Exception exception)
            {
                Log.Warn($"BOT_DEATH diagnostics could not be written for bot {bot.Name}: {exception.Message}");
                return $"killer_kind={kind} danger={danger}";
            }
        }
    }
}
