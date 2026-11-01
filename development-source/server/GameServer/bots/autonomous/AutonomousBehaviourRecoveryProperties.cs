using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Switches for the recovery and danger-memory behaviour ported from the Living Server reference
    /// (AutonomousRouteGoalFix, AutonomousPveBossDanger, AutonomousRepeatDeathSpots,
    /// AutonomousPveDangerBypass and AutonomousKeepGateGoals). Defaults preserve the reference behaviour;
    /// every one of them is a pure movement/selection rule and changes no combat number.
    /// </summary>
    public static class AutonomousBehaviourRecoveryProperties
    {
        [ServerProperty("autonomous", "bot_route_goal_fix",
            "Autonomous gamebots snap an off-mesh goal onto the floor of its own zone after a path failure, budget the floor step, remember route give-ups for six minutes and skip the capital relocation when the goal, not the bot, is at fault.", true)]
        public static bool ROUTE_GOAL_FIX;

        [ServerProperty("autonomous", "bot_pve_boss_danger",
            "No XP camp is offered near a spot where a monster ten or more levels above the victim killed an autonomous gamebot recently, and a route past it counts as a longer trip (in memory, six hours).", true)]
        public static bool PVE_BOSS_DANGER;

        [ServerProperty("autonomous", "bot_repeat_death_spots",
            "A bot bends its routes around, and picks no camp beside, places where it died three or more times within 1,200 units in three hours (in memory, three hours).", true)]
        public static bool REPEAT_DEATH_SPOTS;

        [ServerProperty("autonomous", "bot_pve_danger_bypass",
            "Solo levelling bots walk around red or purple monsters and remembered death spots on their way instead of walking into them.", true)]
        public static bool PVE_DANGER_BYPASS;

        [ServerProperty("autonomous", "bot_keep_gate_goals",
            "A goal inside the courtyard of a keep whose closed outer gate the bot may not pass is not offered; a bot passes the gates of its own realm's keeps only, and a keep its force is sieging keeps its targets.", true)]
        public static bool KEEP_GATE_GOALS;

        [ServerProperty("autonomous", "rvr_siege_defense_steering",
            "Recent bot-siege results move the siege defense share up or down by up to 0.25 around rvr_siege_defense_ratio, so sieges stay contested (reference SiegeDefenseBalance). False keeps the configured share fixed.", true)]
        public static bool SIEGE_DEFENSE_STEERING;
    }
}
