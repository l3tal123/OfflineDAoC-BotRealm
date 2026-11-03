using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.GS.Spells;

namespace DOL.GS
{
    /// <summary>
    /// BotRealm charming for bots, gamebots and hired companions alike (owner 2026-10-30, plan section 25): no
    /// made-up pets. A bot charms a real monster, and only with a charm spell it has really learned from its own spec
    /// lines (some classes' charm is deep in an advanced line). It charms the first valid target it meets in combat that
    /// cons at least green: the right creature type for the spell, within the spell's level limit, nobody's pet.
    /// </summary>
    public static class Charm165
    {
        /// <summary>The best charm spell the bot has learned (none: it fights without one).</summary>
        public static Spell LearnedCharm(GameBot bot) =>
            bot.Spells?.Where(s => s != null && s.SpellType == eSpellType.Charm && s.Level <= bot.Level)
                .OrderByDescending(s => s.Level).FirstOrDefault();

        /// <summary>Whether this monster can be charmed by this bot with this spell, under the 1.65 rules.</summary>
        public static bool IsValidTarget(GameBot bot, Spell charm, GameNPC mob)
        {
            if (charm == null || mob == null || !mob.IsAlive || mob.ObjectState != GameObject.eObjectState.Active ||
                mob.CurrentRegion != bot.CurrentRegion || mob.Realm != eRealm.None || mob.BodyType is < 1 or > 11 ||
                mob.Brain is IControlledBrain || mob is GameSummonedPet or GameEpicBoss or GameEpicNPC or GameBot ||
                !AutonomousPetSupport.IsCharmBodyTypeAllowed(charm, mob.BodyType))
                return false;
            if (ServerProperties.Properties.SPELL_CHARM_NAMED_CHECK != 0 && !string.IsNullOrEmpty(mob.Name) && char.IsUpper(mob.Name[0]))
                return false;
            // the original Dawn of Light limit, as for players: the spell's maximum level and its share of the caster's level
            if (CharmSpellHandler.TooStrong165(charm, bot.Level, mob.Level))
                return false;
            return ConLevels.GetConColor(bot.GetConLevel(mob)) >= ConColor.GREEN;
        }

        /// <summary>No bot tries to charm the same monster again within this time (no charm loops).</summary>
        public const int RetryMilliseconds = 60_000;
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameNPC, object> Tried = new();

        /// <summary>
        /// With no pet of any kind (a Hunter with its wolf never charms: owner 2026-10-30), cast the learned charm on a
        /// valid monster (at least green, the right kind, within the spell's level limit). Hired companions seek one
        /// whenever their group fights; gamebots only charm a monster fighting them alone, never the group's or raid's
        /// target. Never in a realm raid, and no bot retries a monster any bot tried in the last minute, so bots can't
        /// take turns charming and stall a fight.
        /// </summary>
        public static bool TryCharm(GameBot bot, GameLiving combatTarget, out string activity)
        {
            activity = null;
            if (!Classic165Mode.IsOn || bot == null || bot.ControlledBrain != null ||
                bot.IsCasting || bot.castingComponent?.HasPendingSkillRequests == true || AutonomousRealmRaid.IsReserved(bot))
                return false;
            Spell charm = LearnedCharm(bot);
            if (charm == null || bot.Mana < bot.PowerCost(charm) || bot.GetSkillDisabledDuration(charm) > 0)
                return false;
            long now = GameLoop.GameLoopTime;
            bool Fresh(GameNPC m) => !Tried.TryGetValue(m, out object last) || now - (long)last >= RetryMilliseconds;
            GameNPC mob;
            if (HiredCompanions165.IsHired(bot))
            {
                // A hired companion seeks a charm whenever its group fights (owner 2026-10-30): its own target first,
                // then any monster fighting the group, the first valid one (at least green, the right kind, in range).
                if (bot.Group == null)
                    return false;
                var members = bot.Group.GetMembersInTheGroup().ToHashSet();
                IEnumerable<GameNPC> candidates = (combatTarget is GameNPC own ? new[] { own } : [])
                    .Concat(bot.GetNPCsInRadius((ushort)System.Math.Min(charm.Range > 0 ? charm.Range : 1000, 1500))
                        .Where(n => n.InCombat && n.TargetObject is GameLiving t && members.Contains(t)));
                mob = candidates.FirstOrDefault(m => Fresh(m) && IsValidTarget(bot, charm, m));
            }
            else
            {
                // A gamebot charms only a monster fighting it alone: never the group's or raid's target.
                mob = combatTarget as GameNPC;
                if (mob == null || !Fresh(mob) || !IsValidTarget(bot, charm, mob) ||
                    mob.attackComponent?.AttackerTracker?.Attackers.Any(a => a != bot && a is not GameSummonedPet) == true)
                    return false;
            }
            if (mob == null)
                return false;
            Tried.AddOrUpdate(mob, now);
            GameObject previous = bot.TargetObject;
            bot.TargetObject = mob;
            bool started = bot.CastSpell(charm, SkillBase.GetSpellLine(GlobalSpellsLines.Mob_Spells), true);
            if (!started)
                bot.TargetObject = previous;
            else
                activity = $"Charming {mob.Name}";
            return started;
        }
    }
}
