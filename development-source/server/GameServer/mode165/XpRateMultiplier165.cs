using System;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// The dashboard's XP rate (owner 2026-11-01): one number that multiplies experience earned from every source, for
    /// players and for every kind of gamebot. The launcher sets it in Options (a text field; it accepts a number from 1
    /// to 5000), the server stores it as a ServerProperty and applies it once to the final amount of each gain, after
    /// the source has paid its own bonuses and after its per-kill cap. A PvE kill, a player kill, a quest, a task, a
    /// mission, praying at a gravestone, a battleground objective and a gamebot's own kill therefore all scale the
    /// same way. Experience losses (the death penalty) are never scaled.
    /// </summary>
    public static class XpRateMultiplier165
    {
        [ServerProperty("rates", "xp_rate_multiplier",
            "Multiplies experience earned from every source (players and every kind of gamebot) by this rate. 1 is normal, 2.5 is two and a half times. The BotRealm launcher sets it in Options; it accepts a number from 1 to 5000.", 1.0)]
        public static double XP_RATE_MULTIPLIER;

        /// <summary>The smallest rate the launcher accepts; 1 leaves experience unchanged.</summary>
        public const double Minimum = 1.0;

        /// <summary>The largest rate the launcher accepts.</summary>
        public const double Maximum = 5000.0;

        /// <summary>The configured rate, clamped to the launcher's range, and 1 when the stored value is unusable.</summary>
        public static double Rate => Clamp(XP_RATE_MULTIPLIER);

        /// <summary>
        /// Scales one experience gain. Only positive amounts are multiplied: a death-penalty loss stays as it is.
        /// </summary>
        public static long Apply(long experience)
        {
            if (experience <= 0)
                return experience;

            double rate = Rate;

            if (rate == 1.0)
                return experience;

            return (long) Math.Round(experience * rate, MidpointRounding.AwayFromZero);
        }

        /// <summary>Clamps a rate to the launcher's 1..5000 range; NaN and infinities become 1.</summary>
        public static double Clamp(double rate) =>
            double.IsFinite(rate) ? Math.Clamp(rate, Minimum, Maximum) : Minimum;
    }
}
