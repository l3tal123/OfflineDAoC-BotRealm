using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Settings for the ported bot behaviour fixes (BotIncomingFireReaction, BotQuietSongs, AutonomousDeathDiagnostics).
    /// Ported from the Living Server reference's ServerProperties block; only the incoming-fire switch has one there
    /// (quiet twisting and the death diagnostics are always on).
    /// </summary>
    public static class BotBehaviourProperties
    {
        [ServerProperty("bots", "bot_incoming_fire_reaction", "An autonomous gamebot hit by a player-shaped attacker (a player, a bot or their pet) reacts like a person: a 0.3-2.5 s delay by hit kind before it answers, an area splash from a distance is not a personal call, and a far attacker does not pull it off another player it is fighting. False makes every hit aggro immediately, as before.", true)]
        public static bool BOT_INCOMING_FIRE_REACTION;
    }
}
