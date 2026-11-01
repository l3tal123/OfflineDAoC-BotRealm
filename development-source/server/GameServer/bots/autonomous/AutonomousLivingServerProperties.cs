using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Living-server switches ported from the reference (bot_schedule_mode, bot_living_breaks and the raid hothead,
    /// which the reference shipped as bot_living_leeroy). Every one defaults to OFF, so a world that does not turn
    /// them on behaves exactly as before. Values live in the ServerProperty table like every other bot setting and
    /// are written from the launcher/console with the usual key.
    /// </summary>
    public static class AutonomousLivingServerProperties
    {
        /// <summary>
        /// When autonomous gamebots are online. "off" = the whole roster stays online (today's behaviour);
        /// "session" = each bot plays 1-4 hour sessions with 20-120 minutes away, timed from server start;
        /// "clock" = follows the PC's local time of day (busy evenings and weekends, thin at 5 a.m.). Any other
        /// value is read as "off". Companion bots and real players are never affected.
        /// </summary>
        [ServerProperty("bots", "bot_schedule_mode",
            "When autonomous gamebots are online: off (the whole roster stays online, default), session (each bot plays 1-4 hour sessions with 20-120 minutes away, timed from server start) or clock (the PC's time of day: busy evenings and weekends, thin at 5 a.m.).",
            "off")]
        public static string BOT_SCHEDULE_MODE;

        /// <summary>
        /// Short breaks: an idle, ungrouped bot now and then says it is away and logs off for 5-25 minutes (it
        /// logs back in on its own), and, rarely, a bot grouped with a real player goes linkdead for 30-90 seconds.
        /// Only autonomous gamebots; companions and players are never touched.
        /// </summary>
        [ServerProperty("bots", "bot_living_breaks",
            "Short breaks for autonomous gamebots: an idle, ungrouped bot now and then says it is away and logs off for 5-25 minutes; a bot grouped with you rarely goes linkdead for 30-90 seconds. Off by default.",
            false)]
        public static bool BOT_LIVING_BREAKS;

        /// <summary>
        /// One end-game raid in twenty has a raider who cannot wait: it charges the final encounter while the muster
        /// is still incomplete, so the raid goes in short with whoever is there. Off by default.
        /// </summary>
        [ServerProperty("bots", "bot_raid_hothead",
            "Raid hothead: one end-game raid in twenty has a raider who charges the final encounter before the muster is complete, forcing the raid in with whoever is there. Off by default.",
            false)]
        public static bool BOT_RAID_HOTHEAD;
    }
}
