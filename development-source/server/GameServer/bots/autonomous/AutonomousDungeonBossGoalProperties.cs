using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Gates for the dungeon-boss party goal (<see cref="AutonomousDungeonBossGoals"/>), ported from the Living Server
    /// reference. Off by default: the owner has not approved bots targeting player quest bosses yet, and True 1.65
    /// mode must behave exactly as before until they do.
    /// </summary>
    public static class AutonomousDungeonBossGoalProperties
    {
        [ServerProperty("bots", "bot_dungeon_boss_goals", "Let a bot party that has ground a dungeon camp for five minutes take that dungeon's named boss as its next goal, once per task per boss, when the boss is the kill target of a real data quest within three levels of the party or has its own drop table and the fight suits the party's size. It then returns to its camp. The camp filter is unchanged (bots still avoid quest monsters when choosing camps); the boss goal is a separate explicit party goal. Default off.", false)]
        public static bool BOT_DUNGEON_BOSS_GOALS;

        /// <summary>The goal runs only when the owner turns it on.</summary>
        public static bool Enabled => BOT_DUNGEON_BOSS_GOALS;
    }
}
