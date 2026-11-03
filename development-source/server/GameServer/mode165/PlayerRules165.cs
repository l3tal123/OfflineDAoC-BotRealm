namespace DOL.GS
{
    /// <summary>
    /// BotRealm: bots live by the player rules (owner 2026-10-31: "no rules that players have to deal with don't
    /// apply to the bots"). A GameBot is a GameNPC underneath, so code that asks `is GamePlayer` gave
    /// gamebots and hired companions the monster rules (no base crit, no +skill or item resists, monster crowd control,
    /// mez failing under 75% health, uncapped resist buffs, and so on; BOT GEAR AND COMBAT PARITY AUDIT 2026-10-31).
    /// Those checks ask IsPlayerLike instead. Normal mode is unchanged.
    /// </summary>
    public static class PlayerRules165
    {
        /// <summary>A real player, or a gamebot / hired companion in BotRealm.</summary>
        public static bool IsPlayerLike(GameObject living) =>
            living is GamePlayer || living is GameBot && Classic165Mode.IsOn;

        /// <summary>A gamebot or hired companion that lives by the player rules (BotRealm only).</summary>
        public static bool IsPlayerRulesBot(GameObject living) => living is GameBot && Classic165Mode.IsOn;
    }
}
