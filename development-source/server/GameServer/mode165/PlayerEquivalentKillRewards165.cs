using System;
using DOL.GS.Keeps;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Player-equivalent worth of a victim and the reward arithmetic a player kill uses (owner 2026-11-02: in True 1.65
    /// an autonomous gamebot is a player in every aspect, so it is worth and earns exactly what a player of the same
    /// level and realm level would).
    /// <para>
    /// A gamebot has no worth values of its own: <see cref="GameLiving"/> returns 0 for realm, bounty and money value,
    /// and its raw level XP value instead of a player's four times that. These helpers supply the player formulas and
    /// are shared by <c>AbstractServerRules</c> (players and gamebots killing a player or a bot) so the two reward paths
    /// cannot drift apart.
    /// </para>
    /// </summary>
    public static class PlayerEquivalentKillRewards165
    {
        /// <summary>XP a player of this victim's level is worth. <see cref="GamePlayer.ExperienceValue"/> is the
        /// living value times four; a gamebot reports the living value, so it is multiplied here.</summary>
        public static long ExperienceValue(GameLiving victim) =>
            victim is GamePlayer ? victim.ExperienceValue : victim.ExperienceValue * 4;

        /// <summary>Bounty points a player of this victim's level is worth (<see cref="GamePlayer.BountyPointsValue"/>).</summary>
        public static int BountyPointsValue(GameLiving victim) =>
            victim is GamePlayer player ? player.BountyPointsValue : BountyPointsValueForLevel(victim.Level);

        /// <summary>The pre-existing player bounty point value formula (<c>1 + level * 0.6</c>).</summary>
        public static int BountyPointsValueForLevel(int level) => (int)(1 + level * 0.6);

        /// <summary>Money a player of this victim's level is worth (<see cref="GamePlayer.MoneyValue"/>).</summary>
        public static long MoneyValue(GameLiving victim) =>
            victim is GamePlayer player ? player.MoneyValue : MoneyValueForLevel(victim.Level);

        /// <summary>The player money value formula (three times the level's restore cost row).</summary>
        public static long MoneyValueForLevel(int level) =>
            3 * GamePlayer.prcRestore[Math.Clamp(level, 0, GamePlayer.prcRestore.Length - 1)];

        /// <summary>A player kill pays at most <c>XP_PVP_Cap_Percent</c> of the awarder's own level XP value.</summary>
        public static long CappedExperience(long awardeeExperienceValue, long baseXpReward) =>
            Math.Min(baseXpReward, awardeeExperienceValue * Properties.XP_PVP_CAP_PERCENT / 100);

        /// <summary>A player kill pays at most twice the awarder's bounty point value.</summary>
        public static int CappedBountyPoints(int awardeeBountyPointsValue, long baseBountyPointsReward) =>
            (int)Math.Min(baseBountyPointsReward, awardeeBountyPointsValue * 2L);

        /// <summary>A player kill pays at most twice the awarder's money value.</summary>
        public static long CappedMoney(long awardeeMoneyValue, long baseMoneyReward) =>
            Math.Min(baseMoneyReward, awardeeMoneyValue * 2);

        /// <summary>Final XP for a kill: capped, split by damage percent, plus the keep outpost / realm bonus.</summary>
        public static long Experience(GameLiving awardee, long baseXpReward, double damagePercent)
        {
            long cappedExperience = CappedExperience(ExperienceValue(awardee), baseXpReward);
            return (long)(cappedExperience * damagePercent) + OutpostExperienceBonus(awardee, cappedExperience);
        }

        /// <summary>Final bounty points for a kill, with the realm's keep bounty point bonus (players only).</summary>
        public static int BountyPoints(GamePlayer awardee, long baseBountyPointsReward, double damagePercent)
        {
            int bountyPoints = (int)(CappedBountyPoints(awardee.BountyPointsValue, baseBountyPointsReward) * damagePercent);

            // Keep bonus: 5% or 3% of the kill's bounty points while the realm holds the matching keep bonus.
            if (KeepBonusMgr.RealmHasBonus(eKeepBonusType.Bounty_Points_5, awardee.Realm))
                return bountyPoints + (int)(bountyPoints / 100.0 * 5);

            if (KeepBonusMgr.RealmHasBonus(eKeepBonusType.Bounty_Points_3, awardee.Realm))
                return bountyPoints + (int)(bountyPoints / 100.0 * 3);

            return bountyPoints;
        }

        /// <summary>Final money for a kill: capped and split by damage percent.</summary>
        public static long Money(GameLiving awardee, long baseMoneyReward, double damagePercent) =>
            (long)(CappedMoney(MoneyValue(awardee), baseMoneyReward) * damagePercent);

        /// <summary>
        /// 1.54 outpost experience bonus: players now receive an experience bonus when fighting within 16,000 units of a
        /// keep controlled by their realm or their guild - 20% if their guild owns the keep, 10% if their realm does
        /// (http://www.camelotherald.com/more/567.shtml). Merged with the realm's global experience keep bonuses.
        /// </summary>
        public static long OutpostExperienceBonus(GameLiving awardee, long baseXpReward)
        {
            const double GUILD_OUTPOST_PERCENT_BONUS = 0.2;
            const double REALM_OUTPOST_PERCENT_BONUS = 0.1;
            const int OUTPOST_RADIUS = 16000;

            AbstractGameKeep keep = GameServer.KeepManager.GetClosestKeepToSpot(awardee.CurrentRegionID, awardee, OUTPOST_RADIUS);
            Guild guild = awardee switch
            {
                GamePlayer player => player.Guild,
                GameBot bot => bot.Guild,
                _ => null
            };

            double outpostPercentBonus = 0.0;

            if (keep != null)
            {
                if (keep.Guild != null && keep.Guild == guild)
                    outpostPercentBonus = GUILD_OUTPOST_PERCENT_BONUS;
                else if (keep.Realm == awardee.Realm && GameServer.Instance.Configuration.ServerType is EGameServerType.GST_Normal)
                    outpostPercentBonus = REALM_OUTPOST_PERCENT_BONUS;
            }

            long outpostBonus = (long)(baseXpReward * outpostPercentBonus);

            if (KeepBonusMgr.RealmHasBonus(eKeepBonusType.Experience_5, awardee.Realm))
                outpostBonus += (long)(baseXpReward / 100.0 * 5);
            else if (KeepBonusMgr.RealmHasBonus(eKeepBonusType.Experience_3, awardee.Realm))
                outpostBonus += (long)(baseXpReward / 100.0 * 3);

            return outpostBonus;
        }
    }
}
