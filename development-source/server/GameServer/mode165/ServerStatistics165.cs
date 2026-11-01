using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;
using DOL.GS.Keeps;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Server-wide statistics for the launcher's "Server Stats" tab (owner 2026-11-02): world totals and four top-10
    /// boards (mobs killed, deaths, PvP kills, keeps captured), counting players AND every kind of gamebot.
    ///
    /// Observation only: the game loop only bumps long counters on an actor entry in memory (no SQL, no file I/O).
    /// The file-based broker of <see cref="AutonomousBotDashboard"/> serves the launcher: the launcher writes
    /// "server-stats.request" next to the server executable, the timer below answers in state-165/server-stats.json.
    /// Per-actor counters are saved to state-165/server-stats-state.json every 30 seconds and on shutdown; realm
    /// points are re-read from the saved world at startup (they are already stored per character and per bot), so
    /// that total is complete even for play from before this build.
    ///
    /// Attribution: the actor credited for a kill or a keep is the participant (player, bot, or a pet resolved to its
    /// owner) that did the most damage. A victim that is a player-like actor counts a death and, to an enemy-realm
    /// actor, a PvP kill; anything else that is a real monster counts a mob kill.
    /// </summary>
    public static class ServerStatistics165
    {
        public const int TopCount = 10;
        public const int RequestPollIntervalMilliseconds = 250;
        private const int SaveIntervalMilliseconds = 30_000;
        private const int RequestIdMaxLength = 128;

        private static readonly Logger Log = LoggerManager.Create(typeof(ServerStatistics165));
        private static readonly ConcurrentDictionary<string, ActorEntry> Players = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<long, ActorEntry> Bots = new();
        private static Timer _requestTimer;
        private static Timer _saveTimer;
        private static int _publishing;
        private static int _saving;
        private static long _realmPointsTotal;
        private static string _lastRequestId = string.Empty;
        private static DateTime _nextWarningUtc;

        public static string FilePath => Classic165Mode.StatePath("server-stats.json");
        public static string StatePath => Classic165Mode.StatePath("server-stats-state.json");
        public static string RequestPath => Path.Combine(AppContext.BaseDirectory, "server-stats.request");

        /// <summary>One value of one top-10 board. Kind is "Player" or "Bot".</summary>
        public sealed record StatRow(string Name, string Kind, int Realm, int Level, long Value);

        /// <summary>The answer to one launcher request: totals, tracked counts and the four boards.</summary>
        public sealed record Snapshot(string RequestId, string GeneratedUtc,
            long MobKills, long Deaths, long MoneyLootedCopper, long PvpKills, long RealmPoints, long KeepsCaptured,
            int PlayersTracked, int BotsTracked,
            StatRow[] TopMobKills, StatRow[] TopDeaths, StatRow[] TopPvpKills, StatRow[] TopKeepsCaptured);

        private sealed class ActorEntry
        {
            public ActorEntry(string key, bool isPlayer)
            {
                Key = key;
                IsPlayer = isPlayer;
            }

            public readonly string Key;
            public readonly bool IsPlayer;
            public string Name = string.Empty;
            public int Realm;
            public int Level;
            public long MobKills;
            public long Deaths;
            public long PvpKills;
            public long MoneyLootedCopper;
            public int KeepsCaptured;
            public int Dirty;
        }

        private sealed class SavedState
        {
            public string SavedUtc { get; set; } = string.Empty;
            public SavedActor[] Actors { get; set; } = [];
        }

        private sealed class SavedActor
        {
            public string Key { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Kind { get; set; } = string.Empty;
            public int Realm { get; set; }
            public int Level { get; set; }
            public long MobKills { get; set; }
            public long Deaths { get; set; }
            public long PvpKills { get; set; }
            public long MoneyLootedCopper { get; set; }
            public int KeepsCaptured { get; set; }
        }

        // ---- lifecycle

        [GameServerStartedEvent]
        public static void Start(DOLEvent e, object sender, EventArgs args)
        {
            try
            {
                Players.Clear();
                Bots.Clear();
                _lastRequestId = string.Empty;
                LoadState();
                SeedRealmPoints();
                SeedPlayerActors();
                _requestTimer?.Dispose();
                _requestTimer = new Timer(_ => PollForRequest(), null, RequestPollIntervalMilliseconds,
                    RequestPollIntervalMilliseconds);
                _saveTimer?.Dispose();
                _saveTimer = new Timer(_ => SaveState(), null, SaveIntervalMilliseconds, SaveIntervalMilliseconds);
                Log.Info($"SERVER_STATS_165 collecting: {Players.Count} player(s), {Bots.Count} bot(s), " +
                         $"RealmPoints={_realmPointsTotal:N0}");
            }
            catch (Exception exception)
            {
                // Statistics must never stop the server from starting.
                Log.Error("SERVER_STATS_165 could not start collecting statistics.", exception);
            }
        }

        [GameServerStoppedEvent]
        public static void Stop(DOLEvent e, object sender, EventArgs args)
        {
            try
            {
                _requestTimer?.Dispose();
                _requestTimer = null;
                _saveTimer?.Dispose();
                _saveTimer = null;
                SaveState(true);
            }
            catch (Exception exception)
            {
                Log.Error("SERVER_STATS_165 could not save its statistics.", exception);
            }
        }

        // ---- observation: deaths, mob kills and PvP kills

        /// <summary>
        /// Called once for every living that dies (from GameLiving.ProcessDeath, while the victim's damage
        /// contributors are still known). Players and bots count as deaths; anything else that is a monster counts
        /// as a mob kill credited to its top contributor. Never throws into death handling.
        /// </summary>
        public static void ObserveDeath(GameLiving victim, GameObject killer)
        {
            if (victim == null)
                return;

            try
            {
                if (victim is GamePlayer or GameBot)
                {
                    ActorEntry victimActor = ActorFor(victim);
                    if (victimActor == null)
                        return;

                    Interlocked.Increment(ref victimActor.Deaths);
                    MarkDirty(victimActor);

                    ActorEntry killerActor = CreditedActor(victim, killer);
                    if (killerActor != null && IsEnemyRealm(killerActor.Realm, victimActor.Realm))
                    {
                        Interlocked.Increment(ref killerActor.PvpKills);
                        MarkDirty(killerActor);
                    }

                    return;
                }

                if (!IsCountableMob(victim))
                    return;

                ActorEntry credited = CreditedActor(victim, killer);
                if (credited == null)
                    return;

                Interlocked.Increment(ref credited.MobKills);
                MarkDirty(credited);
            }
            catch (Exception exception)
            {
                WarnThrottled("SERVER_STATS_165 could not observe a death.", exception);
            }
        }

        /// <summary>Anything that is a real monster: an NPC that is neither a player-like gamebot nor a controlled pet.</summary>
        public static bool IsCountableMob(GameLiving victim) =>
            victim is GameNPC npc && victim is not GameBot && npc.Brain is not IControlledBrain;

        /// <summary>Two different player realms fight each other; None/Door never count.</summary>
        public static bool IsEnemyRealm(int killerRealm, int victimRealm) =>
            killerRealm is >= (int)eRealm.Albion and <= (int)eRealm.Hibernia &&
            victimRealm is >= (int)eRealm.Albion and <= (int)eRealm.Hibernia &&
            killerRealm != victimRealm;

        // ---- observation: money looted

        public static void RecordMoneyLoot(GamePlayer player, long copper)
        {
            if (player == null || copper <= 0)
                return;
            try
            {
                ActorEntry actor = ActorFor(player);
                if (actor == null)
                    return;
                Interlocked.Add(ref actor.MoneyLootedCopper, copper);
                MarkDirty(actor);
            }
            catch (Exception exception)
            {
                WarnThrottled("SERVER_STATS_165 could not record looted money.", exception);
            }
        }

        public static void RecordMoneyLoot(GameBot bot, long copper)
        {
            if (bot == null || copper <= 0)
                return;
            try
            {
                ActorEntry actor = ActorFor(bot);
                if (actor == null)
                    return;
                Interlocked.Add(ref actor.MoneyLootedCopper, copper);
                MarkDirty(actor);
            }
            catch (Exception exception)
            {
                WarnThrottled("SERVER_STATS_165 could not record looted money.", exception);
            }
        }

        // ---- observation: realm points

        /// <summary>Adds to the world's realm-point total. The total is re-seeded from the saved world at startup.</summary>
        public static void RecordRealmPoints(GameLiving actor, long amount)
        {
            if (actor == null || amount <= 0 || actor is not (GamePlayer or GameBot))
                return;
            Interlocked.Add(ref _realmPointsTotal, amount);
        }

        // ---- observation: keeps captured

        /// <summary>Credits every player-like participant that damaged the keep lord, like the players' own keep stat.</summary>
        public static void ObserveKeepCapture(GuardLord lord, GameObject killer)
        {
            if (lord == null)
                return;

            try
            {
                HashSet<ActorEntry> credited = new();
                if (lord.XPGainers != null)
                {
                    lock (lord.XpGainersLock)
                    {
                        foreach (GameLiving contributor in lord.XPGainers.Keys)
                        {
                            ActorEntry actor = ActorFor(contributor);
                            if (actor != null)
                                credited.Add(actor);
                        }
                    }
                }

                if (credited.Count == 0)
                {
                    ActorEntry actor = killer is GameLiving living ? ActorFor(living) : null;
                    if (actor != null)
                        credited.Add(actor);
                }

                foreach (ActorEntry actor in credited)
                {
                    Interlocked.Increment(ref actor.KeepsCaptured);
                    MarkDirty(actor);
                }
            }
            catch (Exception exception)
            {
                WarnThrottled("SERVER_STATS_165 could not observe a keep capture.", exception);
            }
        }

        // ---- actor resolution

        /// <summary>
        /// The player-like actor behind a living: a player, a gamebot, or the root owner of a controlled pet. Returns
        /// null for ordinary monsters, doors, siege engines and ownerless pets.
        /// </summary>
        public static GameLiving RootActor(GameLiving source)
        {
            GameLiving current = source;
            for (int depth = 0; depth < 16; depth++)
            {
                if (current is GameBot || current is not GameNPC npc || npc.Brain is not IControlledBrain controlled)
                    break;
                if (controlled.GetLivingOwner() is not GameLiving owner)
                    break;
                current = owner;
            }

            return current is GamePlayer or GameBot ? current : null;
        }

        private static ActorEntry CreditedActor(GameLiving victim, GameObject killer)
        {
            ActorEntry best = null;
            double bestDamage = 0;

            if (victim.XPGainers != null)
            {
                lock (victim.XpGainersLock)
                {
                    foreach (KeyValuePair<GameLiving, double> pair in victim.XPGainers)
                    {
                        if (pair.Key == null || pair.Value <= bestDamage)
                            continue;
                        ActorEntry actor = ActorFor(pair.Key);
                        if (actor == null)
                            continue;
                        best = actor;
                        bestDamage = pair.Value;
                    }
                }
            }

            if (best != null)
                return best;

            return killer is GameLiving killerLiving ? ActorFor(killerLiving) : null;
        }

        private static ActorEntry ActorFor(GameLiving living)
        {
            GameLiving root = living == null ? null : RootActor(living) ?? living;
            return root switch
            {
                GamePlayer player => PlayerActor(player),
                GameBot bot => BotActor(bot),
                _ => null,
            };
        }

        private static ActorEntry PlayerActor(GamePlayer player)
        {
            if (player == null || string.IsNullOrEmpty(player.ObjectId))
                return null;

            ActorEntry actor = Players.GetOrAdd(player.ObjectId, id => new ActorEntry("player:" + id, true));
            actor.Name = player.Name;
            actor.Realm = (int)player.Realm;
            actor.Level = player.Level;
            return actor;
        }

        private static ActorEntry BotActor(GameBot bot)
        {
            if (bot == null || bot.DatabaseID <= 0)
                return null;

            ActorEntry actor = Bots.GetOrAdd(bot.DatabaseID, id => new ActorEntry("bot:" + id, false));
            actor.Name = bot.Name;
            actor.Realm = (int)bot.Realm;
            actor.Level = bot.Level;
            return actor;
        }

        private static void MarkDirty(ActorEntry actor) => Volatile.Write(ref actor.Dirty, 1);

        // ---- launcher request / snapshot

        /// <summary>A request carries one request id (the Bot Inspector's broker without a target id). Exposed for tests.</summary>
        public static bool TryParseRequest(string request, out string requestId)
        {
            requestId = string.Empty;
            if (string.IsNullOrWhiteSpace(request) || request.Length > RequestIdMaxLength)
                return false;

            requestId = request.Trim();
            return requestId.Length is > 0 and <= RequestIdMaxLength && !requestId.Contains('\n') && !requestId.Contains('\r');
        }

        private static void PollForRequest()
        {
            try
            {
                if (!File.Exists(RequestPath))
                    return;

                string request;
                using (var stream = new FileStream(RequestPath, FileMode.Open, FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream))
                    request = reader.ReadToEnd();

                if (!TryParseRequest(request, out string requestId) ||
                    string.Equals(requestId, _lastRequestId, StringComparison.Ordinal))
                    return;

                if (Publish(requestId))
                    _lastRequestId = requestId;
            }
            catch (IOException)
            {
                // The launcher's atomic request replacement can overlap this tiny read window.
            }
            catch (UnauthorizedAccessException exception)
            {
                WarnThrottled("SERVER_STATS_165 could not read the launcher's request.", exception);
            }
        }

        private static bool Publish(string requestId)
        {
            if (Interlocked.Exchange(ref _publishing, 1) != 0)
                return false;

            try
            {
                Snapshot snapshot = BuildSnapshot(requestId);
                string temporaryPath = FilePath + ".tmp";
                using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write,
                           FileShare.Read, 64 * 1024, FileOptions.SequentialScan))
                {
                    JsonSerializer.Serialize(stream, snapshot);
                }

                if (File.Exists(FilePath))
                    File.Delete(FilePath);
                File.Move(temporaryPath, FilePath, true);
                return true;
            }
            catch (Exception exception)
            {
                WarnThrottled("SERVER_STATS_165 could not publish its snapshot.", exception);
                return false;
            }
            finally
            {
                Volatile.Write(ref _publishing, 0);
            }
        }

        /// <summary>Builds the launcher's answer from the in-memory counters. Exposed for tests.</summary>
        public static Snapshot BuildSnapshot(string requestId)
        {
            ActorEntry[] actors = Players.Values.Concat(Bots.Values).ToArray();
            StatRow[] topMobKills = SelectTop(actors.Select(static actor => ActorRow(actor, actor.MobKills)));
            StatRow[] topDeaths = SelectTop(actors.Select(static actor => ActorRow(actor, actor.Deaths)));
            StatRow[] topPvpKills = SelectTop(actors.Select(static actor => ActorRow(actor, actor.PvpKills)));
            StatRow[] topKeeps = SelectTop(actors.Select(static actor => ActorRow(actor, actor.KeepsCaptured)));

            return new Snapshot(requestId, DateTime.UtcNow.ToString("O"),
                actors.Sum(static actor => actor.MobKills),
                actors.Sum(static actor => actor.Deaths),
                actors.Sum(static actor => actor.MoneyLootedCopper),
                actors.Sum(static actor => actor.PvpKills),
                Volatile.Read(ref _realmPointsTotal),
                actors.Sum(static actor => (long)actor.KeepsCaptured),
                Players.Count, Bots.Count,
                topMobKills, topDeaths, topPvpKills, topKeeps);
        }

        private static StatRow ActorRow(ActorEntry actor, long value) =>
            new(actor.Name, actor.IsPlayer ? "Player" : "Bot", actor.Realm, actor.Level, value);

        /// <summary>
        /// The top <see cref="TopCount"/> rows with a positive value, highest first, ties by name. Exposed for tests.
        /// </summary>
        public static StatRow[] SelectTop(IEnumerable<StatRow> rows)
        {
            if (rows == null)
                return [];

            return rows
                .Where(row => row.Value > 0)
                .OrderByDescending(row => row.Value)
                .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Kind, StringComparer.Ordinal)
                .Take(TopCount)
                .ToArray();
        }

        // ---- persistence

        private static void SaveState(bool drain = false)
        {
            if (!drain && Interlocked.Exchange(ref _saving, 1) != 0)
                return;

            try
            {
                ActorEntry[] actors = Players.Values.Concat(Bots.Values).ToArray();
                var state = new SavedState
                {
                    SavedUtc = DateTime.UtcNow.ToString("O"),
                    Actors = actors.Select(actor => new SavedActor
                    {
                        Key = actor.Key,
                        Name = actor.Name,
                        Kind = actor.IsPlayer ? "Player" : "Bot",
                        Realm = actor.Realm,
                        Level = actor.Level,
                        MobKills = Interlocked.Read(ref actor.MobKills),
                        Deaths = Interlocked.Read(ref actor.Deaths),
                        PvpKills = Interlocked.Read(ref actor.PvpKills),
                        MoneyLootedCopper = Interlocked.Read(ref actor.MoneyLootedCopper),
                        KeepsCaptured = Volatile.Read(ref actor.KeepsCaptured),
                    }).ToArray(),
                };

                string temporaryPath = StatePath + ".tmp";
                using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write,
                           FileShare.Read, 256 * 1024, FileOptions.SequentialScan))
                {
                    JsonSerializer.Serialize(stream, state);
                }

                if (File.Exists(StatePath))
                    File.Delete(StatePath);
                File.Move(temporaryPath, StatePath, true);
                foreach (ActorEntry actor in actors)
                    Volatile.Write(ref actor.Dirty, 0);
            }
            catch (Exception exception)
            {
                WarnThrottled("SERVER_STATS_165 could not save its state.", exception);
            }
            finally
            {
                Volatile.Write(ref _saving, 0);
            }
        }

        private static void LoadState()
        {
            try
            {
                if (!File.Exists(StatePath))
                    return;
                SavedState state;
                using (var stream = new FileStream(StatePath, FileMode.Open, FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete))
                    state = JsonSerializer.Deserialize<SavedState>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (state?.Actors == null)
                    return;

                foreach (SavedActor saved in state.Actors)
                {
                    if (saved == null || string.IsNullOrEmpty(saved.Key))
                        continue;

                    bool isPlayer = saved.Key.StartsWith("player:", StringComparison.Ordinal);
                    if (!isPlayer && !saved.Key.StartsWith("bot:", StringComparison.Ordinal))
                        continue;
                    if (!long.TryParse(saved.Key[(saved.Key.IndexOf(':') + 1)..], out long id))
                        continue;

                    var actor = new ActorEntry(saved.Key, isPlayer)
                    {
                        Name = saved.Name ?? string.Empty,
                        Realm = saved.Realm,
                        Level = saved.Level,
                        MobKills = Math.Max(0, saved.MobKills),
                        Deaths = Math.Max(0, saved.Deaths),
                        PvpKills = Math.Max(0, saved.PvpKills),
                        MoneyLootedCopper = Math.Max(0, saved.MoneyLootedCopper),
                        KeepsCaptured = Math.Max(0, saved.KeepsCaptured),
                    };

                    if (isPlayer)
                        Players[saved.Key[("player:".Length)..]] = actor;
                    else
                        Bots[id] = actor;
                }
            }
            catch (Exception exception)
            {
                WarnThrottled("SERVER_STATS_165 could not read its saved state.", exception);
            }
        }

        /// <summary>
        /// Realm points are stored per character and per bot record, so the world total is read from them rather than
        /// counted again; anything earned while the server runs is added on top.
        /// </summary>
        private static void SeedRealmPoints()
        {
            long total = 0;
            foreach (DbCoreCharacter character in GameServer.Database.SelectAllObjects<DbCoreCharacter>())
                total += Math.Max(0, character.RealmPoints);
            foreach (OfflineWorldBotRecord record in GameServer.Database.SelectAllObjects<OfflineWorldBotRecord>())
                total += Math.Max(0, record.RealmPoints);
            Interlocked.Exchange(ref _realmPointsTotal, total);
        }

        /// <summary>
        /// Players who already existed get their all-time PvP kills and keep captures from the character table as a
        /// seed; the counters then continue from there. Bots have no such history and start at 0.
        /// </summary>
        private static void SeedPlayerActors()
        {
            foreach (DbCoreCharacter character in GameServer.Database.SelectAllObjects<DbCoreCharacter>())
            {
                if (character == null || string.IsNullOrEmpty(character.ObjectId))
                    continue;

                ActorEntry actor = Players.GetOrAdd(character.ObjectId, id => new ActorEntry("player:" + id, true));
                actor.Name = character.Name ?? string.Empty;
                actor.Realm = character.Realm;
                actor.Level = character.Level;
                // Seed only an actor no counter has ever touched: a loaded or already seeded row keeps counting.
                if (actor.MobKills > 0 || actor.Deaths > 0 || actor.PvpKills > 0 ||
                    actor.MoneyLootedCopper > 0 || actor.KeepsCaptured > 0)
                    continue;
                if (character.KillsAlbionPlayers + character.KillsMidgardPlayers + character.KillsHiberniaPlayers +
                    character.CapturedKeeps + character.CapturedTowers <= 0)
                    continue;

                (actor.PvpKills, actor.KeepsCaptured) = HistoricalCounts(character);
                MarkDirty(actor);
            }
        }

        /// <summary>The all-time PvP kills and keep captures the character table already holds. Exposed for tests.</summary>
        public static (long PvpKills, int KeepsCaptured) HistoricalCounts(DbCoreCharacter character)
        {
            if (character == null)
                return (0, 0);

            long pvpKills = Math.Max(0, character.KillsAlbionPlayers) + Math.Max(0, character.KillsMidgardPlayers) +
                            Math.Max(0, character.KillsHiberniaPlayers);
            int keeps = Math.Max(0, character.CapturedKeeps) + Math.Max(0, character.CapturedTowers);
            return (pvpKills, keeps);
        }

        private static void WarnThrottled(string message, Exception exception)
        {
            DateTime now = DateTime.UtcNow;
            if (now < _nextWarningUtc)
                return;
            _nextWarningUtc = now.AddMinutes(1);
            Log.Warn(message, exception);
        }
    }
}
