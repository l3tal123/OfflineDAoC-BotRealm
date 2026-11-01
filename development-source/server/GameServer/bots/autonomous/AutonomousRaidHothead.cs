using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// The raid hothead (ported from the reference's raid "Leeroy" beat, AutonomousRaidLeeroy.cs; the reference's 2005
    /// meme flavour has been rewritten in period-neutral wording and the setting is off by default):
    /// one end-game raid in twenty has a raider who cannot wait. While the raid musters for its final encounter
    /// (a landed dragon), once part of the raid is there but not all of it, the hothead charges in alone; the boss is
    /// in the fight, so the raid follows with whoever is there. The raid's own attempt and wipe rules take it from
    /// there (a wipe still musters again).
    ///
    /// Rules reused verbatim: the 5% roll (<see cref="Chance"/>), the muster/staging tests (<see cref="ShouldCharge"/>,
    /// <see cref="JumpsThePull"/>, <see cref="ShouldChargeStaging"/>) and the damage-dealer-only requirement
    /// (<see cref="CanBeHothead"/>). Adapted for our tree: the reference weighted the picker by bot personality
    /// (AutonomousBotPersonality, which our tree does not have), so <see cref="Pick"/> draws uniformly; the reference's
    /// voice corpus and chat-news integration are replaced by fixed lines and AutonomousBotChatCoordinator; the
    /// dungeon-route hook lives in RealmRaidDungeonRoute.cs, which is outside this port's write scope, so only the
    /// dragon staging path is wired here.
    /// </summary>
    public static class AutonomousRaidHothead
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousRaidHothead));

        public static bool On => AutonomousLivingServerProperties.BOT_RAID_HOTHEAD;

        /// <summary>The share of raids that roll a hothead (the reference's 5%).</summary>
        public const double Chance = 0.05;
        /// <summary>It waits until the muster has stood this long (long enough to look like the raid is getting ready).</summary>
        public const long EarliestMusterMilliseconds = 60_000;
        public const long EarliestStagingMilliseconds = 3 * 60_000;
        /// <summary>The fewest raiders at the muster before it goes.</summary>
        public const int FewestFollowers = 8;

        public static bool Rolls(double draw) => draw < Chance;

        private static int _forceNextRaid, _chargeNow;

        /// <summary>Sets the next raid to form with a hothead (tests and a future GM command).</summary>
        public static void ForceNextRaid() => System.Threading.Volatile.Write(ref _forceNextRaid, 1);

        /// <summary>The raid forming now has a hothead: the 5% roll, or once after <see cref="ForceNextRaid"/>.</summary>
        public static bool RollForRaid(double draw) => System.Threading.Interlocked.Exchange(ref _forceNextRaid, 0) == 1 || Rolls(draw);

        /// <summary>Makes a hothead raid that is gathering charge at once (tests and a future GM command).</summary>
        public static void ChargeNow() => System.Threading.Volatile.Write(ref _chargeNow, 1);

        /// <summary>Whether a hothead raid goes now: its own rules, or a forced "now" with at least two raiders there (used once).</summary>
        public static bool GoesNow(bool rulesSay, int present) =>
            rulesSay || present >= 2 && System.Threading.Interlocked.Exchange(ref _chargeNow, 0) == 1;

        /// <summary>
        /// Whether the hothead charges now: a first attempt at the encounter (never a regroup after a wipe), the muster
        /// standing for a minute, a quarter of what the raid waits for already there, and not all of it (then it is just
        /// the pull). Pure.
        /// </summary>
        public static bool ShouldCharge(int mustered, int needed, long musterMilliseconds, int failedAttempts, bool regrouping) =>
            !regrouping && failedAttempts == 0 && musterMilliseconds >= EarliestMusterMilliseconds &&
            mustered < needed && mustered >= Math.Max(FewestFollowers, needed / 4);

        /// <summary>
        /// The raid reached the muster all at once, so the moment "part of it is there" never came: it jumps the pull
        /// instead, a first attempt with the whole muster there. Pure.
        /// </summary>
        public static bool JumpsThePull(int mustered, int needed, int failedAttempts, bool regrouping) =>
            !regrouping && failedAttempts == 0 && mustered >= needed && mustered >= FewestFollowers;

        /// <summary>A dragon raid at its staging: half the raid there, not all, after three minutes. Pure.</summary>
        public static bool ShouldChargeStaging(int present, int required, long stagingMilliseconds) =>
            stagingMilliseconds >= EarliestStagingMilliseconds && present < required &&
            present >= Math.Max(FewestFollowers, required / 2);

        /// <summary>Who may be the hothead: a melee or ranged damage dealer, never a healer or a support class. Pure.</summary>
        public static bool CanBeHothead(eCharacterClass characterClass) =>
            !BotPartyRoles.IsHealingClass(characterClass) && BotPartyRoles.For(characterClass) != BotPartyRole.Support &&
            !BotPartyRoles.IsHybridSupport(characterClass);

        /// <summary>Who charges: a living, active, damage-dealing autonomous gamebot from the raiders. Uniform draw
        /// (the reference weighted it by personality, which our tree does not have).</summary>
        public static GameBot Pick(IEnumerable<GameLiving> raiders, Random random = null)
        {
            random ??= Random.Shared;
            GameBot[] candidates = (raiders ?? Array.Empty<GameLiving>()).OfType<GameBot>()
                .Where(b => b.IsAutonomousWorldBot && b.IsAlive && b.ObjectState == GameObject.eObjectState.Active &&
                            b.CharacterClass != null && CanBeHothead((eCharacterClass)b.CharacterClass.ID) &&
                            b.Brain is BotBrain)
                .ToArray();
            return candidates.Length == 0 ? null : candidates[random.Next(candidates.Length)];
        }

        // Period-neutral shouts: the reference's "LEEEEROOOOY JENKINS" meme is from 2005, after the 1.65 lock.
        public static readonly string[] Shouts =
        [
            "For the realm! With me, now!",
            "Enough waiting. At them!",
            "I will not stand here while it still breathes!",
        ];

        /// <summary>What the raiders beside it say, a few seconds apart.</summary>
        public static readonly string[] Reactions =
        [
            "it has gone in alone",
            "hold the line, wait for the rest!",
            "someone follow it in!",
            "we are not formed up yet!",
            "oh no, here we go",
            "go go go, after it!",
            "was that the plan? nobody said that was the plan",
            "it is going to get us all killed",
            "well, in we go",
        ];

        /// <summary>The /faction line a raider sends after the charge ({hothead}, {boss}).</summary>
        public static readonly string[] FactionCalls =
        [
            "{hothead} charged {boss} alone, the whole expedition is going in after them",
            "{hothead} pulled {boss} before we were ready. here we go",
            "raid update: {hothead} went in first. we are fighting {boss} now",
        ];

        /// <summary>After the raid: a raider's /faction line, by outcome ({hothead}, {boss}).</summary>
        public static readonly string[] AfterWin =
        [
            "cannot believe that worked. {hothead} is a legend",
            "{boss} down even after {hothead} charged early. never doing that again",
            "we won. {hothead} still owes the healers an apology",
        ];

        public static readonly string[] AfterWipe =
        [
            "thanks a lot {hothead}",
            "wiped at {boss}. we all know who went in first, {hothead}",
            "{hothead} cost us the expedition. regroup and go again",
        ];

        public static string Fill(string line, string hothead, string boss) =>
            line.Replace("{hothead}", hothead ?? "someone").Replace("{boss}", boss ?? "the boss");

        /// <summary>
        /// It yells and charges. False when the bot can't attack the boss (then nothing is said and the raid goes on as
        /// it was). The reactions and the realm call follow on timers.
        /// </summary>
        public static bool Charge(GameBot hothead, GameNPC boss, IReadOnlyCollection<GameLiving> raiders, string raidName, Random random = null)
        {
            random ??= Random.Shared;
            if (hothead?.Brain is not BotBrain brain || boss == null || !boss.IsAlive)
                return false;
            if (!brain.OrderPull(boss))
                return false;
            string shout = Shouts[random.Next(Shouts.Length)];
            if (hothead.Yell(shout))
                AutonomousBotChatCoordinator.BroadcastFaction(hothead.Name, hothead.Realm, shout);
            Log.Info($"RAID_HOTHEAD bot=\"{hothead.Name}\" class=\"{hothead.CharacterClass?.Name}\" boss=\"{boss.Name}\" " +
                     $"raid=\"{raidName}\" region={hothead.CurrentRegionID} raiders={raiders?.Count ?? 0}");

            // Two or three raiders react, a few seconds apart; then one tells the realm.
            GameBot[] others = (raiders ?? Array.Empty<GameLiving>()).OfType<GameBot>()
                .Where(b => b != hothead && b.IsAlive && b.IsAutonomousWorldBot && b.ObjectState == GameObject.eObjectState.Active)
                .OrderBy(_ => random.Next()).Take(3).ToArray();
            int delay = 1_500;
            foreach (GameBot speaker in others.Take(2 + random.Next(2)))
            {
                string line = Reactions[random.Next(Reactions.Length)];
                GameBot who = speaker;
                delay += 1_200 + random.Next(2_500);
                _ = new ECSGameTimer(who, _ =>
                {
                    if (who.ObjectState == GameObject.eObjectState.Active && who.IsAlive)
                        who.Say(line);
                    return 0;
                }, delay);
            }
            GameBot caller = others.FirstOrDefault();
            if (caller != null)
            {
                string call = Fill(FactionCalls[random.Next(FactionCalls.Length)], hothead.Name, boss.Name);
                _ = new ECSGameTimer(caller, _ =>
                {
                    if (caller.ObjectState == GameObject.eObjectState.Active)
                        AutonomousBotChatCoordinator.BroadcastFaction(caller.Name, caller.Realm, call);
                    return 0;
                }, delay + 6_000 + random.Next(6_000));
            }
            return true;
        }

        /// <summary>When the raid ends, a raider tells /faction how it went with the hothead.</summary>
        public static void AfterRaid(eRealm realm, string hothead, string boss, bool won, IEnumerable<GameLiving> raiders, Random random = null)
        {
            random ??= Random.Shared;
            if (string.IsNullOrEmpty(hothead))
                return;
            GameBot speaker = (raiders ?? Array.Empty<GameLiving>()).OfType<GameBot>()
                .Where(b => b.IsAutonomousWorldBot && b.Name != hothead && b.ObjectState == GameObject.eObjectState.Active)
                .OrderBy(_ => random.Next()).FirstOrDefault();
            if (speaker == null)
                return;
            string[] lines = won ? AfterWin : AfterWipe;
            string line = Fill(lines[random.Next(lines.Length)], hothead, boss);
            _ = new ECSGameTimer(speaker, _ =>
            {
                AutonomousBotChatCoordinator.BroadcastFaction(speaker.Name, speaker.Realm, line);
                return 0;
            }, 20_000 + random.Next(40_000));
        }
    }
}
