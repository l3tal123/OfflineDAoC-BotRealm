using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.Events;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// When autonomous gamebots are online (ported from the reference's AutonomousBotSchedule.cs, docs/LIVING-SERVER.md
    /// in the reference). Every switch defaults OFF, so a world that does not turn them on behaves exactly as before.
    /// <list type="bullet">
    /// <item><b>off</b> (default): every bot stays online, as before.</item>
    /// <item><b>session</b>: each bot plays sessions of 1-4 hours with 20 minutes to 2 hours away, so names come and go
    /// while you play. Timed from server start.</item>
    /// <item><b>clock</b>: follows the PC's clock like a 2002 server: busy evenings and weekends, a thin crowd at 5 a.m.</item>
    /// </list>
    /// Separately, <c>bot_living_breaks</c> gives bots short "brb" breaks (they log out and come back on their own) and,
    /// rarely, a linkdead moment when grouped with a real player. Only ungrouped (breaks) or player-grouped (linkdead),
    /// alive bots out of combat that are not carrying a relic or working a siege are touched; the population controller
    /// simply does not log a scheduled-off bot back in. Companion bots and real players are never affected.
    ///
    /// Adaptations from the reference (our tree has no living-server/voices/personality layer):
    /// - personality modifiers and rage-quits are omitted, so the draws are uniform;
    /// - the reference's bot voice lines (AutonomousBotVoices.TryEventLine) are replaced by fixed neutral lines and
    ///   plain /say, and guild chat is omitted;
    /// - the reference froze a linkdead bot by checking AutonomousBotSchedule.IsFrozen at the top of BotBrain.Think.
    ///   BotBrain is outside this port's write scope, so the freeze here stops and restarts the bot's brain
    ///   (BotBrain.Stop()/Start()), which has the same visible effect (the bot stands still and does nothing) but does
    ///   not wake on being attacked; the group-combat wake path is kept. <see cref="OnAttacked"/> is provided for a
    ///   future one-line GameBot hook;
    /// - the reference ticks this from AutonomousLivingServer.Pulse on the game loop; our tree has no such pulse, so this
    ///   class posts its own once-a-second game-loop pulse to NpcService from a server lifecycle timer.
    /// </summary>
    public static class AutonomousBotSchedule
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousBotSchedule));

        public enum Mode { Off, Session, Clock }

        public static Mode CurrentMode => Parse(AutonomousLivingServerProperties.BOT_SCHEDULE_MODE);

        public static bool BreaksOn => AutonomousLivingServerProperties.BOT_LIVING_BREAKS;

        public static bool Active => CurrentMode != Mode.Off || BreaksOn;

        public static Mode Parse(string value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "session" => Mode.Session,
            "clock" => Mode.Clock,
            _ => Mode.Off,
        };

        private static DateTime _startedUtc = DateTime.UtcNow;

        public static void NoteServerStarted(DateTime nowUtc) => _startedUtc = nowUtc;

        #region Server lifecycle and the game-loop pulse

        private static System.Threading.Timer _pulseTimer;
        private static int _running;

        /// <summary>Starts the once-a-second game-loop pulse (the reference ticks this beside its living-server pulse).</summary>
        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            _startedUtc = DateTime.UtcNow;
            BreakUntil.Clear();
            OverstaySince.Clear();
            Frozen.Clear();
            LastLinkdead.Clear();
            LeavingNow.Clear();
            RecentOverstays = 0;
            _nextLogoutScan = _nextBreakRoll = _nextLinkdeadRoll = 0;
            System.Threading.Volatile.Write(ref _running, 1);
            _pulseTimer?.Dispose();
            _pulseTimer = new System.Threading.Timer(_ => NpcService.Instance.Post(PulseOnGameLoop, 0L), null, 1_000, 1_000);
        }

        [GameServerStoppedEvent]
        public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
        {
            System.Threading.Volatile.Write(ref _running, 0);
            _pulseTimer?.Dispose();
            _pulseTimer = null;
            BreakUntil.Clear();
            OverstaySince.Clear();
            Frozen.Clear();
            LastLinkdead.Clear();
            LeavingNow.Clear();
            RecentOverstays = 0;
        }

        /// <summary>Runs on the game loop (posted by the lifecycle timer). One failing scan never stops the rest.</summary>
        private static void PulseOnGameLoop(long _)
        {
            if (System.Threading.Volatile.Read(ref _running) != 1)
                return;
            try
            {
                Pulse(GameLoop.GameLoopTime);
            }
            catch (Exception exception)
            {
                Log.Warn("Bot schedule: pulse failed", exception);
            }
        }

        #endregion

        #region Schedule math (pure, unit tested)

        /// <summary>A stable 64-bit mix of a bot id and a salt (SplitMix64), never string.GetHashCode.</summary>
        public static ulong Mix(long id, long salt)
        {
            ulong z = unchecked((ulong)id * 0x9E3779B97F4A7C15UL + (ulong)salt * 0xBF58476D1CE4E5B9UL + 0x94D049BB133111EBUL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public static double Unit(long id, long salt) => (Mix(id, salt) >> 11) * (1.0 / (1UL << 53));

        /// <summary>Session mode: minutes online and away for this bot (1-4 hours on, 20-120 minutes off).</summary>
        public static (int Online, int Away) SessionLengths(long botId)
        {
            double online = 60 + Unit(botId, 1) * 180;
            double away = 20 + Unit(botId, 2) * 100;
            return ((int)Math.Round(online), (int)Math.Round(Math.Max(10, away)));
        }

        public static bool SessionOnline(long botId, TimeSpan sinceStart)
        {
            (int online, int away) = SessionLengths(botId);
            int cycle = online + away;
            int phase = (int)(Unit(botId, 3) * cycle);
            double minute = Math.Max(0, sinceStart.TotalMinutes) + phase;
            return minute % cycle < online;
        }

        // Share of the roster online by local hour (a 2002 North American evening crowd).
        private static readonly double[] Weekday =
        [
            0.45, 0.35, 0.28, 0.22, 0.20, 0.20, 0.24, 0.30, 0.34, 0.36, 0.38, 0.42,
            0.46, 0.48, 0.50, 0.54, 0.60, 0.70, 0.82, 0.92, 0.96, 0.95, 0.85, 0.62,
        ];

        private static readonly double[] Weekend =
        [
            0.60, 0.48, 0.38, 0.30, 0.25, 0.24, 0.26, 0.32, 0.42, 0.52, 0.62, 0.70,
            0.74, 0.76, 0.78, 0.80, 0.82, 0.86, 0.90, 0.95, 0.97, 0.96, 0.90, 0.75,
        ];

        /// <summary>The share of the roster the clock mode wants online at this local time (hour curve, interpolated by minute).</summary>
        public static double ClockShare(DateTime local)
        {
            bool weekend = local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ||
                           local.DayOfWeek == DayOfWeek.Friday && local.Hour >= 18;
            double[] curve = weekend ? Weekend : Weekday;
            double here = curve[local.Hour], next = curve[(local.Hour + 1) % 24];
            return here + (next - here) * (local.Minute / 60.0);
        }

        public static bool ClockOnline(long botId, DateTime local)
        {
            double share = ClockShare(local);
            // Each bot's place in line for the day: stable for the day, so it doesn't flicker on and off.
            int day = local.Year * 400 + local.DayOfYear;
            double place = (Unit(botId, 4) + 0.25 * Unit(botId, 1000 + day)) % 1.0;
            return place < share;
        }

        public static bool ScheduledOnline(Mode mode, long botId, TimeSpan sinceStart, DateTime local) => mode switch
        {
            Mode.Session => SessionOnline(botId, sinceStart),
            Mode.Clock => ClockOnline(botId, local),
            _ => true,
        };

        #endregion

        #region Breaks

        private static readonly ConcurrentDictionary<long, DateTime> BreakUntil = new();
        private static readonly ConcurrentDictionary<long, byte> ReturningFromBreak = new();
        private static readonly ConcurrentDictionary<long, byte> LeavingNow = new();

        public static bool OnBreak(long botId, DateTime nowUtc) => BreakUntil.TryGetValue(botId, out DateTime until) && nowUtc < until;

        #endregion

        /// <summary>Whether the population controller should have this bot online now (thread-safe).</summary>
        public static bool WantsOnline(OfflineWorldBotRecord record, DateTime nowUtc)
        {
            if (record == null)
                return false;
            Mode mode = CurrentMode;
            if (mode == Mode.Off && !BreaksOn)
                return true;
            if (BreaksOn && OnBreak(record.BotId, nowUtc))
                return false;
            return ScheduledOnline(mode, record.BotId, nowUtc - _startedUtc, nowUtc.ToLocalTime());
        }

        #region Logging out (game loop)

        private static long _nextLogoutScan;
        private static long _nextBreakRoll;
        private static long _nextLinkdeadRoll;
        public const int MaxLogoutsPerScan = 3;

        /// <summary>Only bots that can leave without breaking anything: ungrouped, alive, idle, not carrying or sieging.</summary>
        public static bool CanLogOut(GameBot bot) =>
            bot?.IsAutonomousWorldBot == true && !bot.IsTemporaryGroupHelper && bot.ObjectState == GameObject.eObjectState.Active &&
            bot.IsAlive && bot.Group == null && !bot.InCombat && !bot.IsCasting && !bot.IsOnStableMasterRoute &&
            !bot.IsReturningAfterRelease && !BotSiegeRuntime.Assigned(bot) &&
            bot.TempProperties.GetProperty<GameRelic>(GameRelic.PLAYER_CARRY_RELIC_WEAK) == null &&
            string.IsNullOrEmpty(bot.TempProperties.GetProperty<string>("RvrEventForce"));

        internal static void Pulse(long now)
        {
            Mode mode = CurrentMode;
            bool breaks = BreaksOn;
            if (mode == Mode.Off && !breaks)
            {
                if (!BreakUntil.IsEmpty)
                    BreakUntil.Clear();
                if (!OverstaySince.IsEmpty)
                    OverstaySince.Clear();
                RecentOverstays = 0;
                if (!Frozen.IsEmpty)
                    foreach (long id in Frozen.Keys.ToArray())
                        Unfreeze(id, false);
                return;
            }
            DateTime nowUtc = DateTime.UtcNow;
            if (breaks && now >= _nextBreakRoll)
            {
                _nextBreakRoll = now + 60_000;
                RollBreaks(nowUtc);
            }
            if (breaks && now >= _nextLinkdeadRoll)
            {
                _nextLinkdeadRoll = now + 30_000;
                RollLinkdead(now);
            }
            PulseFrozen(now);
            if (now < _nextLogoutScan)
                return;
            _nextLogoutScan = now + 5_000;
            int leaving = 0;
            int recentOverstays = 0;
            var overstaying = new HashSet<long>();
            foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
            {
                if (WantsOnline(bot.PersistentRecord, nowUtc))
                    continue;
                overstaying.Add(bot.DatabaseID);
                DateTime since = OverstaySince.GetOrAdd(bot.DatabaseID, nowUtc);
                if (nowUtc - since < OverstayGrace)
                    recentOverstays++;
                if (leaving >= MaxLogoutsPerScan || LeavingNow.ContainsKey(bot.DatabaseID) || !CanLogOut(bot))
                    continue;
                leaving++;
                LogOut(bot, OnBreak(bot.DatabaseID, nowUtc) ? "brb" : "logoff");
            }
            foreach (long gone in OverstaySince.Keys.Where(id => !overstaying.Contains(id)).ToArray())
                OverstaySince.TryRemove(gone, out _);
            RecentOverstays = recentOverstays;
        }

        // Scheduled-off bots still online because they're busy (often in a bot group). For 15 minutes they
        // count against the scheduled number, so the total doesn't run far past it; after that they no longer
        // hold back the bots whose turn it is.
        private static readonly ConcurrentDictionary<long, DateTime> OverstaySince = new();
        public static readonly TimeSpan OverstayGrace = TimeSpan.FromMinutes(15);
        public static volatile int RecentOverstays;

        /// <summary>Neutral period lines; the reference used its bot voice corpus for these.</summary>
        public static readonly string[] AwayLines =
        [
            "brb, back in a bit",
            "stepping away for a moment",
            "afk, food",
            "back shortly, do not wait on me",
        ];

        public static readonly string[] LogoutLines =
        [
            "logging off for now, good luck out there",
            "calling it a night. see you around",
            "time for me to go. take care",
        ];

        public static readonly string[] LogonLines =
        [
            "evening all",
            "back again",
            "anyone need anything before i head out?",
        ];

        public static readonly string[] BackLines =
        [
            "back, sorry about that",
            "that took longer than expected. what did i miss?",
            "here again. anything happen?",
        ];

        private static void LogOut(GameBot bot, string topic)
        {
            LeavingNow[bot.DatabaseID] = 0;
            string[] lines = topic == "brb" ? AwayLines : LogoutLines;
            if (PlayerHears(bot))
                bot.Say(lines[Random.Shared.Next(lines.Length)]);
            _ = new ECSGameTimer(bot, _ =>
            {
                try
                {
                    if (CanLogOut(bot) && !WantsOnline(bot.PersistentRecord, DateTime.UtcNow))
                    {
                        // Only a bot that really left on a break says "back" when it returns.
                        if (topic == "brb")
                            ReturningFromBreak[bot.DatabaseID] = 0;
                        Log.Info($"BOT_SCHEDULE_LOGOUT bot=\"{bot.Name}\" id={bot.DatabaseID} reason={topic} mode={CurrentMode}");
                        bot.Delete();
                    }
                }
                catch (Exception exception)
                {
                    Log.Warn($"Bot schedule: {bot.Name} could not log out", exception);
                }
                finally
                {
                    LeavingNow.TryRemove(bot.DatabaseID, out byte _);
                }
                return 0;
            }, 2_500 + Random.Shared.Next(2_000));
        }

        private static void RollBreaks(DateTime nowUtc)
        {
            foreach (var expired in BreakUntil.Where(pair => pair.Value <= nowUtc).Select(pair => pair.Key).ToArray())
                BreakUntil.TryRemove(expired, out _);
            // About one break per bot every four hours online, 5-25 minutes long.
            foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
            {
                if (!CanLogOut(bot) || BreakUntil.ContainsKey(bot.DatabaseID) || Random.Shared.NextDouble() >= 1.0 / 240)
                    continue;
                BreakUntil[bot.DatabaseID] = nowUtc.AddMinutes(5 + Random.Shared.Next(21));
            }
        }

        /// <summary>A bot logged in (called by the population controller): a returning or scheduled bot says hello.</summary>
        public static void OnLoggedIn(GameBot bot)
        {
            if (!Active || bot == null)
                return;
            bool back = ReturningFromBreak.TryRemove(bot.DatabaseID, out _);
            // No greetings during the first logins after server start.
            if (!back && DateTime.UtcNow - _startedUtc < TimeSpan.FromMinutes(10))
                return;
            if (!back && Random.Shared.NextDouble() >= 0.5)
                return;
            string[] lines = back ? BackLines : LogonLines;
            string line = lines[Random.Shared.Next(lines.Length)];
            _ = new ECSGameTimer(bot, _ =>
            {
                if (bot.ObjectState == GameObject.eObjectState.Active && bot.IsAlive && PlayerHears(bot))
                    bot.Say(line);
                return 0;
            }, 5_000 + Random.Shared.Next(8_000));
        }

        /// <summary>True when a real player of any realm is close enough to hear the bot's /say.</summary>
        private static bool PlayerHears(GameBot bot) => bot?.CurrentRegion != null &&
            ClientService.Instance.GetPlayersOfRegion(bot.CurrentRegion)
                .Any(player => player.ObjectState == GameObject.eObjectState.Active && bot.IsWithinRadius(player, WorldMgr.SAY_DISTANCE));

        #endregion

        #region Linkdead

        private static readonly ConcurrentDictionary<long, (GameBot Bot, long Until)> Frozen = new();
        private static readonly ConcurrentDictionary<Group, long> LastLinkdead = new();
        public const long LinkdeadGroupCooldownMs = 45 * 60_000;

        public static readonly string[] LinkdeadGoneLines =
        [
            "{target} just went linkdead",
            "linkdead? {target} stopped moving",
            "think {target} dropped",
        ];

        public static readonly string[] LinkdeadBackLines =
        [
            "back, my connection dropped",
            "sorry, linkdead. here again",
            "connection is back",
        ];

        /// <summary>True while a bot is "linkdead": its brain is stopped so it stands still and does nothing.</summary>
        public static bool IsFrozen(GameBot bot) => bot != null && !Frozen.IsEmpty && Frozen.TryGetValue(bot.DatabaseID, out var entry) &&
                                                   ReferenceEquals(entry.Bot, bot) && entry.Until > GameLoop.GameLoopTime;

        /// <summary>Anything that hits a "linkdead" bot can wake it at once. Kept for a one-line GameBot hook (GameBot is
        /// outside this port's write scope, so nothing calls it yet; the group-combat path in PulseFrozen still wakes it).</summary>
        public static void OnAttacked(GameBot bot)
        {
            if (bot != null && Frozen.TryGetValue(bot.DatabaseID, out var entry) && ReferenceEquals(entry.Bot, bot))
                Unfreeze(bot.DatabaseID, true);
        }

        private static void RollLinkdead(long now)
        {
            foreach (GamePlayer player in ClientService.Instance.GetPlayers())
            {
                Group group = player?.Group;
                if (group == null || LastLinkdead.TryGetValue(group, out long last) && now - last < LinkdeadGroupCooldownMs)
                    continue;
                GameLiving[] members = group.GetMembersInTheGroup().ToArray();
                if (members.Any(member => member.InCombat) || Random.Shared.NextDouble() >= 1.0 / 90)
                    continue;
                GameBot[] candidates = members.OfType<GameBot>().Where(bot => bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper &&
                    bot.IsAlive && !bot.IsCasting && !bot.IsOnStableMasterRoute && !BotSiegeRuntime.Assigned(bot) &&
                    bot.TempProperties.GetProperty<GameRelic>(GameRelic.PLAYER_CARRY_RELIC_WEAK) == null &&
                    string.IsNullOrEmpty(bot.TempProperties.GetProperty<string>("RvrEventForce")) &&
                    !Frozen.ContainsKey(bot.DatabaseID)).ToArray();
                if (candidates.Length == 0)
                    continue;
                LastLinkdead[group] = now;
                GameBot frozen = candidates[Random.Shared.Next(candidates.Length)];
                long until = now + 30_000 + Random.Shared.Next(60_000);
                Frozen[frozen.DatabaseID] = (frozen, until);
                // The reference stopped the brain at the top of BotBrain.Think; our BotBrain is out of scope, so the
                // brain is stopped and started instead (the bot stands still and does nothing while frozen).
                frozen.Brain?.Stop();
                frozen.StopMoving();
                frozen.StopFollowing();
                Log.Info($"BOT_SCHEDULE_LINKDEAD bot=\"{frozen.Name}\" id={frozen.DatabaseID} seconds={(until - now) / 1000}");
                GameBot noticer = members.OfType<GameBot>().Where(bot => bot != frozen && bot.IsAutonomousWorldBot &&
                    !bot.IsTemporaryGroupHelper && bot.IsAlive).OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
                if (noticer != null)
                {
                    string line = LinkdeadGoneLines[Random.Shared.Next(LinkdeadGoneLines.Length)].Replace("{target}", frozen.Name);
                    _ = new ECSGameTimer(noticer, _ =>
                    {
                        if (noticer.ObjectState == GameObject.eObjectState.Active && noticer.IsAlive && PlayerHears(noticer))
                            noticer.Say(line);
                        return 0;
                    }, 15_000 + Random.Shared.Next(10_000));
                }
            }
            foreach (Group stale in LastLinkdead.Where(pair => now - pair.Value > LinkdeadGroupCooldownMs).Select(pair => pair.Key).ToArray())
                LastLinkdead.TryRemove(stale, out _);
        }

        private static void PulseFrozen(long now)
        {
            foreach (var entry in Frozen.ToArray())
            {
                GameBot bot = entry.Value.Bot;
                if (bot.ObjectState != GameObject.eObjectState.Active || !bot.IsAlive)
                {
                    Unfreeze(entry.Key, false);
                    continue;
                }
                // A fight anywhere in its group, or the group breaking up, "reconnects" it at once.
                bool groupInCombat = bot.Group != null && bot.Group.GetMembersInTheGroup().Any(member => member.IsAlive && member.InCombat);
                if (entry.Value.Until <= now || bot.InCombat || bot.Group == null || groupInCombat)
                    Unfreeze(entry.Key, true);
            }
        }

        private static void Unfreeze(long botId, bool speak)
        {
            if (!Frozen.TryRemove(botId, out var entry))
                return;
            GameBot bot = entry.Bot;
            // A dead or removed bot's brain is stopped by the normal death/removal path; only restart a live one.
            if (bot.ObjectState != GameObject.eObjectState.Active || !bot.IsAlive)
                return;
            bot.Brain?.Start();
            if (!speak)
                return;
            string line = LinkdeadBackLines[Random.Shared.Next(LinkdeadBackLines.Length)];
            _ = new ECSGameTimer(bot, _ =>
            {
                if (bot.IsAlive && PlayerHears(bot))
                    bot.Say(line);
                return 0;
            }, 1_500 + Random.Shared.Next(2_000));
        }

        #endregion
    }
}
