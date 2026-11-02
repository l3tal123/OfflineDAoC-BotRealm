using System.Net;
using System.Net.Sockets;

namespace OfflineDaoc.Dashboard;

/// <summary>
/// Builds the JSON the web UI asks for. Everything here is a read: the roster comes from the world database and
/// the live status and positions come from the server's state-165 snapshot files.
/// </summary>
internal sealed class DashboardData
{
    private readonly WorldReader _world;
    private readonly LiveBots _live;
    private readonly string _runtimeDirectory;
    private readonly string _installName;
    private readonly string? _mapsDirectory;
    private readonly Dictionary<int, byte[]?> _zoneImages = [];
    private readonly object _zoneImageGate = new();
    private List<WorldReader.RegionRow>? _regions;
    private string _databaseError = string.Empty;

    internal DashboardData(string runtimeDirectory, string databasePath, string serverDirectory, string? mapsDirectory)
    {
        _runtimeDirectory = runtimeDirectory;
        _world = new WorldReader(databasePath);
        _live = new LiveBots(serverDirectory);
        _mapsDirectory = mapsDirectory;
        string name = Path.GetFileName(runtimeDirectory.TrimEnd(Path.DirectorySeparatorChar));
        // A playable folder's folder is its name; the runtime folder inside it is called "runtime".
        _installName = name.Equals("runtime", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(Path.GetDirectoryName(runtimeDirectory.TrimEnd(Path.DirectorySeparatorChar))) ?? name
            : name;
    }

    internal bool DatabaseExists => _world.Exists;

    /// <summary>
    /// The new client's painted map for one zone, or null. Same lookup the launcher uses: the <c>original</c>
    /// images first, then the <c>x4plus</c> high-resolution ones. Images are kept in memory: they are small and the
    /// map asks for the same handful of tiles every couple of seconds.
    /// </summary>
    internal byte[]? ZoneImage(int zoneId)
    {
        if (_mapsDirectory == null)
            return null;
        lock (_zoneImageGate)
        {
            if (_zoneImages.TryGetValue(zoneId, out byte[]? cached))
                return cached;
            byte[]? bytes = null;
            foreach (string subfolder in new[] { "original", "x4plus" })
            {
                string path = Path.Combine(_mapsDirectory, subfolder, $"z{zoneId:000}.png");
                try
                {
                    if (!File.Exists(path))
                        continue;
                    bytes = File.ReadAllBytes(path);
                    break;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    bytes = null;
                    break;
                }
            }

            _zoneImages[zoneId] = bytes;
            return bytes;
        }
    }

    private bool HasZoneImage(int zoneId) => ZoneImage(zoneId) != null;

    internal object Overview()
    {
        State state = Read();
        return new
        {
            serverRunning = state.ServerRunning,
            live = state.Live,
            updatedUtc = state.UpdatedUtc,
            source = state.Live ? "live" : "saved",
            install = _installName,
            databaseError = _databaseError,
            counts = Counts(state),
            regions = LiveRegionCount(state),
        };
    }

    internal object Population()
    {
        State state = Read();
        return new
        {
            serverRunning = state.ServerRunning,
            live = state.Live,
            updatedUtc = state.UpdatedUtc,
            source = state.Live ? "live" : "saved",
            databaseError = _databaseError,
            counts = Counts(state),
            bots = state.Bots.Select(bot => new
            {
                id = bot.Id,
                name = bot.Name,
                realm = bot.Realm,
                realmName = RealmName(bot.Realm),
                race = bot.Race,
                gender = GenderName(bot.Gender),
                className = bot.ClassName,
                level = bot.Level,
                zone = bot.Zone,
                activity = bot.Activity,
                online = bot.Online,
                retired = bot.Retired,
                target = bot.Target,
                regionId = bot.RegionId,
                x = bot.X,
                y = bot.Y,
                z = bot.Z,
            }).ToList(),
        };
    }

    internal object World()
    {
        State state = Read();
        List<WorldReader.RegionRow> regions = Regions();
        Dictionary<int, int> online = state.Bots.Where(bot => bot.Online && bot.RegionId > 0)
            .GroupBy(bot => bot.RegionId)
            .ToDictionary(group => group.Key, group => group.Count());
        return new
        {
            serverRunning = state.ServerRunning,
            live = state.Live,
            updatedUtc = state.UpdatedUtc,
            source = state.Live ? "live" : "saved",
            databaseError = _databaseError,
            paintedMaps = _mapsDirectory != null,
            suggestedRegionId = SuggestedRegion(regions, state.Bots),
            regions = regions.Select(region => new
            {
                id = region.Id,
                name = region.Name,
                realm = region.Realm,
                realmName = region.Realm == 0 ? "Contested" : RealmName(region.Realm),
                minX = region.MinX,
                minY = region.MinY,
                maxX = region.MaxX,
                maxY = region.MaxY,
                width = region.MaxX - region.MinX,
                height = region.MaxY - region.MinY,
                online = online.GetValueOrDefault(region.Id),
                zones = region.Zones.Select(zone => new
                {
                    id = zone.Id,
                    name = zone.Name,
                    x = zone.X,
                    y = zone.Y,
                    width = zone.Width,
                    height = zone.Height,
                    realm = zone.Realm,
                    hasMap = HasZoneImage(zone.Id),
                }).ToList(),
            }).ToList(),
            bots = state.Bots.Where(bot => bot.Online && bot.RegionId > 0).Select(bot => new
            {
                id = bot.Id,
                name = bot.Name,
                realm = bot.Realm,
                realmName = RealmName(bot.Realm),
                level = bot.Level,
                className = bot.ClassName,
                zone = bot.Zone,
                activity = bot.Activity,
                regionId = bot.RegionId,
                x = bot.X,
                y = bot.Y,
                z = bot.Z,
                alive = bot.Alive,
            }).ToList(),
        };
    }

    /// <summary>One consistent reading: the roster, the live overlay and the server state.</summary>
    private State Read()
    {
        bool serverRunning = ServerProbe.IsRunning();
        LiveBots.Snapshot? snapshot = _live.TryGet(serverRunning);
        bool live = snapshot?.Bots != null;
        Dictionary<long, LiveBots.BotStatus> statuses = live
            ? snapshot!.Bots!.GroupBy(bot => bot.BotId).ToDictionary(group => group.Key, group => group.First())
            : [];

        List<WorldReader.RosterRow> roster;
        try
        {
            roster = _world.ReadRoster();
            _databaseError = string.Empty;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Data.SQLite.SQLiteException)
        {
            _databaseError = $"The world database could not be read: {exception.Message}";
            roster = [];
        }

        var bots = new List<Bot>();
        foreach (WorldReader.RosterRow row in roster)
        {
            bool hasLive = statuses.TryGetValue(row.Id, out LiveBots.BotStatus? status);
            bool online = serverRunning && (live ? hasLive : row.IsOnline);
            int level = hasLive ? status!.Level : row.Level;
            string zone = hasLive ? status!.ZoneName ?? row.Zone : row.Zone;
            string activity = FormatActivity(
                hasLive ? status!.Activity ?? string.Empty : row.Activity,
                hasLive ? status!.CurrentGoal ?? string.Empty : row.Goal,
                hasLive ? status!.TargetName ?? string.Empty : row.Target,
                hasLive ? status!.TravelDestination ?? string.Empty : row.Destination,
                hasLive ? status!.ObjectiveProgress ?? string.Empty : row.Progress,
                online);
            bots.Add(new Bot(
                row.Id, row.Name, row.Realm, row.Race, row.Gender, row.ClassName, level, zone, activity,
                online, row.IsRetired,
                hasLive ? status!.RegionId : 0,
                hasLive ? status!.X : 0,
                hasLive ? status!.Y : 0,
                hasLive ? status!.Z : 0,
                hasLive ? status!.ObjectiveKind ?? string.Empty : string.Empty,
                hasLive ? status!.TargetName ?? string.Empty : row.Target,
                hasLive ? status!.IsAlive : false));
        }

        return new State(serverRunning, live, snapshot?.UpdatedUtc ?? DateTime.UtcNow, bots);
    }

    private static object Counts(State state) => new
    {
        total = state.Bots.Count,
        online = state.Bots.Count(bot => bot.Online),
        realms = new[] { 1, 2, 3 }
            .Select(realm => new
            {
                realm,
                realmName = RealmName(realm),
                total = state.Bots.Count(bot => bot.Realm == realm),
                online = state.Bots.Count(bot => bot.Realm == realm && bot.Online),
            })
            .ToList(),
    };

    private static int LiveRegionCount(State state) =>
        state.Bots.Where(bot => bot.Online && bot.RegionId > 0).Select(bot => bot.RegionId).Distinct().Count();

    /// <summary>
    /// The region the map should open on: where the roster says the population is. A stopped server has no live
    /// positions, so the bots' saved zones are the best guide.
    /// </summary>
    private static int SuggestedRegion(List<WorldReader.RegionRow> regions, List<Bot> bots)
    {
        if (regions.Count == 0)
            return 0;
        var zoneCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Bot bot in bots)
        {
            if (bot.Zone.Length == 0)
                continue;
            zoneCounts[bot.Zone] = zoneCounts.GetValueOrDefault(bot.Zone) + 1;
        }

        WorldReader.RegionRow? best = null;
        int bestScore = -1;
        foreach (WorldReader.RegionRow region in regions)
        {
            int score = region.Zones.Sum(zone => zoneCounts.GetValueOrDefault(zone.Name));
            if (score > bestScore)
            {
                bestScore = score;
                best = region;
            }
        }

        if (best == null || bestScore <= 0)
            best = regions.OrderByDescending(region => region.Zones.Count).First();
        return best.Id;
    }

    private List<WorldReader.RegionRow> Regions()
    {
        if (_regions != null)
            return _regions;
        try
        {
            _regions = _world.ReadRegions();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Data.SQLite.SQLiteException)
        {
            _databaseError = $"The world database could not be read: {exception.Message}";
            _regions = [];
        }

        return _regions;
    }

    /// <summary>The launcher's activity wording, so the web UI says the same thing the desktop one does.</summary>
    private static string FormatActivity(string activity, string goal, string target, string destination,
        string progress, bool online)
    {
        activity = activity.Trim();
        goal = goal.Trim();
        target = target.Trim();
        destination = destination.Trim();
        progress = progress.Trim();

        var details = new List<string>(3);
        if (goal.Length > 0)
            details.Add(goal);
        if (destination.Length > 0 && !goal.Contains(destination, StringComparison.OrdinalIgnoreCase))
            details.Add(destination);
        if (target.Length > 0 && !goal.Contains(target, StringComparison.OrdinalIgnoreCase))
            details.Add($"Target: {target}");
        if (progress.Length > 0)
            details.Add(progress);
        string live = details.Count > 0
            ? string.Join(" — ", details)
            : activity.Length > 0 ? activity : "Choosing next activity";
        if (online && activity.Length > 0 && !live.StartsWith(activity, StringComparison.OrdinalIgnoreCase))
            live = $"{activity}: {live}";
        return online ? live : $"Parked — resumes {live}";
    }

    internal static string RealmName(int realm) => realm switch
    {
        1 => "Albion",
        2 => "Midgard",
        3 => "Hibernia",
        _ => "Unknown",
    };

    private static string GenderName(int gender) => gender switch
    {
        0 => "Male",
        1 => "Female",
        _ => "—",
    };

    private sealed record State(bool ServerRunning, bool Live, DateTime UpdatedUtc, List<Bot> Bots);

    private sealed record Bot(long Id, string Name, int Realm, string Race, int Gender, string ClassName, int Level,
        string Zone, string Activity, bool Online, bool Retired, int RegionId, int X, int Y, int Z,
        string ObjectiveKind, string Target, bool Alive);
}

/// <summary>Is the game server listening? The launcher uses the same test: TCP port 10300.</summary>
internal static class ServerProbe
{
    private const int GamePort = 10300;
    private static readonly object Gate = new();
    private static DateTime _checkedUtc = DateTime.MinValue;
    private static bool _running;

    internal static bool IsRunning()
    {
        lock (Gate)
        {
            if (DateTime.UtcNow - _checkedUtc < TimeSpan.FromMilliseconds(500))
                return _running;
            _checkedUtc = DateTime.UtcNow;
            _running = Probe();
            return _running;
        }
    }

    private static bool Probe()
    {
        try
        {
            using var client = new TcpClient();
            Task connect = client.ConnectAsync(IPAddress.Loopback, GamePort);
            return connect.Wait(200) && client.Connected;
        }
        catch (Exception exception) when (exception is SocketException or AggregateException)
        {
            return false;
        }
    }
}

