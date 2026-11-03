using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// BotRealm solo ranged pulls (owner 2026-10-31: "let melee bots pull with range if they can against aggressive
    /// mobs if they have a throwing weapon or arrows, etc. like warriors. wait for mob to get to them instead of face
    /// pulling"; "classes that can have a bow and arrow or use ranged spells to pull even if they are melee coded should
    /// do so like shaman"). A solo gamebot about to take on an aggressive monster opens with a ranged damage spell
    /// (Shaman, Thane, Cleric...) or its bow, crossbow or throwing weapon (Warrior, Armsman, Hero, Shadowblade...) from a
    /// firing point at range, then puts its weapon away, stands still and lets the monster come to it, so it doesn't walk
    /// into the monster's friends. A bot with no ranged option (Savage, Skald without a spell, Paladin...) pulls in
    /// melee as before. Archers and nuking casters already fight from range and are left alone; so are groups (they use
    /// AutonomousDefensivePull).
    /// </summary>
    public static class SoloRangedPull165
    {
        public const int PrepareMilliseconds = 20_000;        // to reach the firing point and shoot
        public const int WaitAfterShotMilliseconds = 12_000;  // then it walks in if the monster never comes (leash, no path)
        public const int RetryMilliseconds = 30_000;
        public const float MaximumAdvance = 3000;

        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(SoloRangedPull165));

        private sealed class State
        {
            public bool Active, Shot, UsedBow;
            public GameLiving Target, RetryTarget;
            public Vector3 FiringPoint;
            public Spell Spell;
            public eActiveWeaponSlot PreviousWeapon;
            public long Until, ShotTick, NextMove, RetryAfter;
            public int Replans;              // new firing spots looked for after the first one
            public float RangeScale = 1f;    // shrinks after a shot the server called out of range or not visible
            public bool Reposition;          // set by a failed shot: find a closer spot before trying again
            public string LastFailure = "";
            public bool PutAwayPending;      // the shot is made: stop and take the melee weapon on the next turn
            public string ShotResult = "";
        }

        public const int MaximumReplans = 4;
        public const int RangeMargin = 100;   // shoot only well inside the range: the server measures it in 3D

        private static readonly ConditionalWeakTable<GameBot, State> States = new();

        public static bool Applies(GameBot bot) =>
            Classic165Mode.IsOn && bot?.IsAutonomousWorldBot == true && bot.Group == null && !bot.IsTemporaryGroupHelper &&
            !HiredCompanions165.IsHired(bot) && bot.CharacterClass != null &&
            !BotRangedCombat.IsDedicatedArcher((eCharacterClass)bot.CharacterClass.ID) &&
            !BotSpellPower.IsOffensiveCaster(bot) &&
            !SavageBotCombatPolicy.MustMeleePull((eCharacterClass)bot.CharacterClass.ID);

        public static bool IsAggressive(GameLiving target, GameBot bot) =>
            target is GameNPC { Brain: StandardMobBrain mob } && mob.AggroLevel > 0 && mob.AggroRange > 0 && mob.CanAggroTarget(bot);

        public static bool IsActive(GameBot bot) => bot != null && States.TryGetValue(bot, out State s) && s.Active;

        /// <summary>While it gets to its firing point and shoots, the native attack must not follow the target into melee.</summary>
        public static bool OwnsRangedPosition(GameNPC npc) =>
            npc is GameBot bot && States.TryGetValue(bot, out State s) && s.Active && !s.Shot;

        /// <summary>Starts a ranged pull on this aggressive target; false means the bot pulls the ordinary way.</summary>
        public static bool TryBegin(GameBot bot, GameLiving target)
        {
            if (!Applies(bot) || target?.IsAlive != true || target.CurrentRegion != bot.CurrentRegion || !IsAggressive(target, bot))
                return false;
            State s = States.GetOrCreateValue(bot);
            if (s.Active)
                return true;
            long now = GameLoop.GameLoopTime;
            if (now < s.RetryAfter && ReferenceEquals(s.RetryTarget, target))
                return false;
            Spell spell = AutonomousDefensivePull.SelectReadyPullSpell(bot.Spells, bot.Level, bot.Mana, bot.PowerCost,
                bot.GetSkillDisabledDuration);
            bool bow = spell == null;
            if (bow && !BotRangedCombat.CanUse(bot, bot.Inventory?.GetItem(eInventorySlot.DistanceWeapon)))
                return false;
            eActiveWeaponSlot previous = bot.ActiveWeaponSlot;
            if (bow)
                bot.SwitchWeapon(eActiveWeaponSlot.Distance);
            int range = spell?.Range ?? bot.attackComponent.AttackRange;
            if (!TryFiringPoint(bot, target, range, out Vector3 point))
            {
                if (bow)
                    bot.SwitchWeapon(MeleeSlot(bot, previous));
                s.RetryAfter = now + RetryMilliseconds;
                s.RetryTarget = target;
                return false;
            }
            s.Active = true;
            s.Shot = false;
            s.UsedBow = bow;
            s.Target = target;
            s.Spell = spell;
            s.PreviousWeapon = previous;
            s.FiringPoint = point;
            s.Until = now + PrepareMilliseconds;
            s.NextMove = 0;
            s.Replans = 0;
            s.RangeScale = 1f;
            s.Reposition = false;
            s.LastFailure = "";
            s.PutAwayPending = false;
            bot.WakeRecoveryRest();
            bot.StopAttack();
            bot.StopFollowing();
            bot.StopMovingOnPath();
            bot.StopMoving();
            bot.TargetObject = target;
            Log.Info($"SOLO_RANGED_PULL_165 begin bot={bot.Name} id={bot.DatabaseID} class=\"{bot.ClassName}\" level={bot.Level} " +
                     $"target=\"{target.Name}\" targetLevel={target.Level} with=\"{spell?.Name ?? bot.ActiveWeapon?.Name}\" range={range} " +
                     $"distance={(int)bot.GetDistanceTo(target)}");
            return true;
        }

        /// <summary>A walkable spot in range of the target with line of sight, reachable from where the bot stands. Tries
        /// the full range first, then closer spots (a hill or a wall in the way, owner 2026-11-01: "make them get into
        /// valid range/move up, and make sure they have los before trying").</summary>
        private static bool TryFiringPoint(GameBot bot, GameLiving target, int range, out Vector3 point)
        {
            Vector3 origin = new(bot.X, bot.Y, bot.Z), enemy = new(target.X, target.Y, target.Z);
            point = origin;
            IPathfindingMgr nav = PathfindingProvider.Instance;
            Zone zone = bot.CurrentZone;
            if (nav?.IsAvailable != true || zone == null || range < 600)
                return false;
            float distance = Vector3.Distance(origin, enemy);
            foreach (float scale in new[] { 1f, 0.8f, 0.6f, 0.45f })
            {
                float standOff = Math.Max(250, range * scale - RangeMargin - 50);
                float advance = Math.Max(0, distance - standOff);
                if (advance > MaximumAdvance)
                    return false;
                Vector3 desired = advance == 0 ? origin : Vector3.Lerp(origin, enemy, advance / distance);
                if (!AutonomousNavigationSurface.TryFloor(nav, zone, desired, out Vector3 spot))
                    continue;
                if (Vector3.Distance(spot, enemy) <= range - RangeMargin && HasSight(nav, zone, spot, enemy) &&
                    (advance == 0 || AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, origin, spot)))
                {
                    point = spot;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Line of sight at body height (the server checks it between the two figures, not their feet).</summary>
        private static bool HasSight(IPathfindingMgr nav, Zone zone, Vector3 from, Vector3 to) =>
            nav.HasLineOfSight(zone, from + new Vector3(0, 0, 60), to + new Vector3(0, 0, 50), nav.DefaultFilters);

        /// <summary>Called on the bot's AI turn: true while the pull holds the bot (walking to its spot, shooting, waiting).</summary>
        public static bool Hold(GameBot bot)
        {
            if (bot == null || !States.TryGetValue(bot, out State s) || !s.Active)
                return false;
            long now = GameLoop.GameLoopTime;
            GameLiving target = s.Target;
            if (!bot.IsAlive || bot.Group != null || target?.IsAlive != true ||
                target.ObjectState != GameObject.eObjectState.Active || target.CurrentRegion != bot.CurrentRegion)
            {
                Finish(bot, s, false, "target gone");
                return false;
            }
            if (!s.Shot)
            {
                if (target is GameNPC npc && npc.TargetObject == bot && npc.InCombat)
                    MarkShot(bot, s);
                else
                {
                    if (now >= s.Until)
                    {
                        Finish(bot, s, false, "no shot in time");
                        return false;
                    }
                    if (Vector3.DistanceSquared(new(bot.X, bot.Y, bot.Z), s.FiringPoint) > 45 * 45)
                    {
                        if (!bot.IsCasting && now >= s.NextMove)
                        {
                            s.NextMove = now + 1500;
                            bot.PathTo(s.FiringPoint, bot.MaxSpeed);
                        }
                        return true;
                    }
                    if (bot.IsCasting || bot.castingComponent?.HasPendingSkillRequests == true)
                        return true;
                    bot.StopMovingOnPath();
                    bot.StopMoving();
                    if (bot.IsCrowdControlled)
                        return true;
                    int range = (int)((s.Spell?.Range ?? bot.attackComponent.AttackRange) * s.RangeScale);
                    IPathfindingMgr nav = PathfindingProvider.Instance;
                    bool inRange = bot.IsWithinRadius(target, Math.Max(200, range - RangeMargin));
                    bool sight = HasSight(nav, bot.CurrentZone, new(bot.X, bot.Y, bot.Z), new(target.X, target.Y, target.Z));
                    // Not in range or no line of sight from here (the monster moved, a hill, a wall, or the last shot was
                    // refused): find a closer spot from where the bot stands and walk there before shooting.
                    if (s.Reposition || !inRange || !sight)
                    {
                        s.LastFailure = s.Reposition ? s.LastFailure : !inRange ? "out of range" : "no line of sight";
                        s.Reposition = false;
                        if (s.Replans >= MaximumReplans)
                        {
                            Finish(bot, s, false, $"no firing spot ({s.LastFailure})");
                            return false;
                        }
                        s.Replans++;
                        if (!TryFiringPoint(bot, target, range, out Vector3 closer))
                        {
                            s.RangeScale *= 0.75f;
                            return true;
                        }
                        s.FiringPoint = closer;
                        s.NextMove = 0;
                        s.Until = Math.Max(s.Until, now + 8000);
                        return true;
                    }
                    bot.TargetObject = target;
                    if (s.Spell != null)
                    {
                        if (bot.Mana >= bot.PowerCost(s.Spell) && bot.GetSkillDisabledDuration(s.Spell) == 0)
                            bot.CastSpell(s.Spell, SkillBase.GetSpellLine(GlobalSpellsLines.Mob_Spells));
                    }
                    else if (!bot.IsAttacking && bot.Endurance >= bot.rangeAttackComponent.ShotEnduranceCost)
                        bot.StartAttack(target);
                    return true;
                }
            }
            if (s.PutAwayPending && now - s.ShotTick >= 300)
            {
                s.PutAwayPending = false;
                bot.StopAttack();
                if (s.UsedBow)
                    bot.SwitchWeapon(MeleeSlot(bot, s.PreviousWeapon));
            }
            // Shot, but the monster took no notice (nothing aggroed it): pull it the ordinary way instead of waiting.
            if (now - s.ShotTick >= 3000 && !Noticed(target, bot))
            {
                // Diagnostics for these (2026-11-01: a third of the shots went unnoticed): what the monster made of it.
                if (target is GameNPC { Brain: StandardMobBrain mob } seen)
                    s.LastFailure += $"; mob_has_aggro={mob.HasAggro} mob_state={mob.FSM?.GetCurrentState()?.GetType().Name} " +
                                     $"mob_hp={seen.HealthPercent}% shot_result={s.ShotResult}";
                Finish(bot, s, true, "not noticed");
                return false;
            }
            // Keep the monster's AI awake while it comes (it wakes for players it can see, for bots only near it).
            (target as GameNPC)?.OnAutonomousBotNearby();
            // Shot: stand with the melee weapon out and let it come.
            if (bot.IsMoving)
            {
                bot.StopMovingOnPath();
                bot.StopMoving();
            }
            if (bot.IsWithinRadius(target, bot.attackComponent.AttackRange + 150) ||
                target is GameNPC coming && coming.TargetObject == bot && bot.IsWithinRadius(target, 400))
            {
                Finish(bot, s, true, "arrived");
                return false;
            }
            if (now - s.ShotTick >= WaitAfterShotMilliseconds)
            {
                Finish(bot, s, true, "never came");
                return false;
            }
            return true;
        }

        public static void OnAttack(GameLiving actor, AttackData attack)
        {
            if (actor is not GameBot bot || !States.TryGetValue(bot, out State s) || !s.Active || s.Shot ||
                attack?.Attacker != actor || attack.Target != s.Target || !attack.CausesCombat)
                return;
            // Only an attack that reached the monster pulls it (a miss or a resist does; "out of range", "not visible"
            // or "not allowed" doesn't: 2026-11-01 run, 310 of 521 pulls "shot" and the monster never noticed).
            if (attack.AttackResult is not (eAttackResult.HitUnstyled or eAttackResult.HitStyle or eAttackResult.Missed or
                eAttackResult.Evaded or eAttackResult.Blocked or eAttackResult.Parried or eAttackResult.Fumbled))
            {
                // Refused (out of range, not visible...): stop, step closer and try again.
                if (attack.AttackResult is eAttackResult.OutOfRange or eAttackResult.TargetNotVisible)
                {
                    s.Reposition = true;
                    s.RangeScale *= 0.8f;
                    s.LastFailure = attack.AttackResult == eAttackResult.OutOfRange ? "shot out of range" : "shot not visible";
                    bot.StopAttack();
                }
                return;
            }
            MarkShot(bot, s, attack);
        }

        /// <summary>Something hit the bot: its target coming in means the fight is on; any other monster ends the pull and
        /// the bot defends itself as usual.</summary>
        public static void OnThreat(GameBot bot, GameLiving attacker)
        {
            if (bot == null || attacker == null || !States.TryGetValue(bot, out State s) || !s.Active)
                return;
            Finish(bot, s, attacker == s.Target, attacker == s.Target ? "target attacked" : "another attacker");
        }

        /// <summary>Records the shot. The attack is still being made when this runs (the attacker is told before the monster
        /// is), so stopping it or putting the bow away here cancelled the shot before the monster ever heard of it
        /// (2026-11-01 runs: hundreds of "shots" the monster never noticed). That happens on the bot's next turn.</summary>
        private static void MarkShot(GameBot bot, State s, AttackData attack = null)
        {
            s.ShotResult = attack == null ? "seen coming" : $"{attack.AttackType}/{attack.AttackResult}{(attack.IsSpellResisted ? "/resisted" : "")} dmg={attack.Damage}";
            s.Shot = true;
            s.ShotTick = GameLoop.GameLoopTime;
            s.PutAwayPending = true;
        }

        /// <summary>The monster knows about the bot: in combat, targeting it, or (on its way over) holding aggro on it. The
        /// first two only turn on once it arrives (2026-11-01 run: every "unnoticed" shot had the monster in its aggro
        /// state, coming).</summary>
        private static bool Noticed(GameLiving target, GameBot bot) =>
            target.InCombat || (target as GameNPC)?.TargetObject == bot ||
            target is GameNPC { Brain: StandardMobBrain mob } && mob.HasAggro;

        private static eActiveWeaponSlot MeleeSlot(GameBot bot, eActiveWeaponSlot previous)
        {
            if (previous != eActiveWeaponSlot.Distance)
                return previous;
            return bot.Inventory?.GetItem(eInventorySlot.RightHandWeapon) == null &&
                   bot.Inventory?.GetItem(eInventorySlot.TwoHandWeapon) != null
                ? eActiveWeaponSlot.TwoHanded : eActiveWeaponSlot.Standard;
        }

        private static void Finish(GameBot bot, State s, bool engage, string why)
        {
            s.Active = false;
            s.RetryAfter = GameLoop.GameLoopTime + RetryMilliseconds;
            s.RetryTarget = s.Target;
            if (bot.ActiveWeaponSlot == eActiveWeaponSlot.Distance && s.UsedBow)
            {
                bot.StopAttack();
                bot.SwitchWeapon(MeleeSlot(bot, s.PreviousWeapon));
            }
            GameLiving target = s.Target;
            if (engage && target?.IsAlive == true && bot.IsAlive && bot.Brain is BotBrain brain)
            {
                bot.TargetObject = target;
                brain.AddToAggroList(target, Math.Max(25, target.EffectiveLevel * 10));
                brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
            }
            Log.Info($"SOLO_RANGED_PULL_165 end bot={bot.Name} id={bot.DatabaseID} target=\"{target?.Name}\" shot={s.Shot} " +
                     $"engage={engage} why=\"{why}\" last_failure=\"{s.LastFailure}\" replans={s.Replans} distance={(target == null ? -1 : (int)bot.GetDistanceTo(target))} " +
                     $"target_on_bot={(target as GameNPC)?.TargetObject == bot} target_in_combat={target?.InCombat == true} " +
                     $"target_moving={(target as GameNPC)?.IsMoving == true} bot_range={bot.attackComponent.AttackRange}");
            s.Target = null;
            s.Spell = null;
        }
    }
}
