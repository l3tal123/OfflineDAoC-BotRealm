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
    private readonly object _mapGate = new();
    private readonly object _worldGate = new();
    private List<WorldReader.RegionRow>? _regions;
    private Dictionary<int, WorldReader.ZoneRow>? _zoneById;
    private Dictionary<string, int>? _zoneIdByName;
    private Dictionary<int, WorldReader.RegionRow>? _regionById;
    private MapCatalog? _mapCatalog;
    private bool _mapCatalogLoaded;
    private Dictionary<int, string>? _overviewByZone;
    private Dictionary<int, string>? _mapViews;
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
    /// images first, then the <c>x4plus</c> high-resolution ones. <paramref name="full"/> asks for the detailed
    /// 4x picture the zone view uses; the smaller one is enough for the overview tiles. The small pictures are kept
    /// in memory (a handful of tiles at a time); the 4x ones are read from disk per request and left to the
    /// browser's cache, so an install carrying every map cannot fill the launcher's memory.
    /// </summary>
    internal byte[]? ZoneImage(int zoneId, bool full = false)
    {
        if (_mapsDirectory == null)
            return null;
        string[] folders = full ? ["x4plus", "original"] : ["original", "x4plus"];
        if (!full)
        {
            lock (_zoneImageGate)
            {
                if (_zoneImages.TryGetValue(zoneId, out byte[]? cached))
                    return cached;
            }
        }

        byte[]? bytes = null;
        foreach (string subfolder in folders)
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

        if (!full)
        {
            lock (_zoneImageGate)
                _zoneImages[zoneId] = bytes;
        }

        return bytes;
    }

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
        Dictionary<int, string> mapViews = MapViews();
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
                zoneId = bot.ZoneId,
                mapView = mapViews.GetValueOrDefault(bot.ZoneId, string.Empty),
                x = bot.X,
                y = bot.Y,
                z = bot.Z,
                objectiveKind = bot.ObjectiveKind,
                objectivePhase = bot.ObjectivePhase,
                objectiveAssignmentId = bot.ObjectiveAssignmentId,
                objectiveExpiresUtc = bot.ObjectiveExpiresUtc,
                groupId = bot.GroupId,
                groupPhase = bot.GroupPhase,
                groupRole = bot.GroupRole,
                groupLeader = bot.GroupLeader,
                groupRendezvous = bot.GroupRendezvous,
                groupPuller = bot.GroupPuller,
                hasGroupTaskClock = bot.HasGroupTaskClock,
                taskTimerPaused = bot.TaskTimerPaused,
                taskRemainingMilliseconds = bot.TaskRemainingMilliseconds,
                groupTaskExpiresUtc = bot.GroupTaskExpiresUtc,
                meetUpDeadlineUtc = bot.MeetUpDeadlineUtc,
            }).ToList(),
        };
    }

    // ------------------------------------------------------------------ the world map

    /// <summary>
    /// The map the World Map tab draws. A <paramref name="requestedView"/> is one of the overviews the map offers
    /// (<c>realm:1</c>, <c>frontier:2</c>) or one zone's own map (<c>zone:100</c>); an unknown or missing view falls
    /// back to where the population is. Only the bots that belong to the view are returned, so the map poll stays
    /// small even with three thousand bots in the world.
    /// </summary>
    internal object Map(string requestedView)
    {
        State state = Read();
        MapCatalog? catalog = Catalog();
        Dictionary<int, int> zoneOnline = ZoneOnline(state.Bots);
        List<MapOverview> overviews = BuildOverviews(catalog, zoneOnline);
        List<MapZoneInfo> zoneList = BuildZoneList(catalog, zoneOnline);

        string view = ResolveView(requestedView, overviews, catalog, zoneOnline);
        MapOverview? overview = overviews.FirstOrDefault(candidate => candidate.Id == view);
        MapCatalog.ZoneMap? zone = null;
        if (overview == null && catalog != null && view.StartsWith("zone:", StringComparison.Ordinal) &&
            int.TryParse(view["zone:".Length..], out int zoneId))
            catalog.Zones.TryGetValue(zoneId, out zone);

        Dictionary<int, MapTile> tiles = overview?.Tiles.ToDictionary(tile => tile.ZoneId) ?? [];
        var bots = new List<object>();
        foreach (Bot bot in state.Bots)
        {
            if (!bot.Online || bot.ZoneId <= 0)
                continue;
            double mapX;
            double mapY;
            if (overview != null)
            {
                if (!tiles.ContainsKey(bot.ZoneId))
                    continue;
                (mapX, mapY) = MapPosition(bot, catalog, useMapRect: false);
                mapX = Clamp(mapX);
                mapY = Clamp(mapY);
            }
            else if (zone != null && zone.Id == bot.ZoneId)
            {
                (mapX, mapY) = MapPosition(bot, catalog, useMapRect: true);
                // The picture of a capital or a dungeon covers only its playable square: a bot outside it is not on
                // the picture, and pinning it to the border would be a lie.
                if (mapX is < 0 or > 1 || mapY is < 0 or > 1)
                    continue;
            }
            else
            {
                continue;
            }

            bots.Add(new
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
                zoneId = bot.ZoneId,
                mx = Math.Round(mapX, 5),
                my = Math.Round(mapY, 5),
                alive = bot.Alive,
            });
        }

        int unplaced = overview == null || overview.Kind != "realm"
            ? 0
            : state.Bots.Count(bot => bot.Online && bot.ZoneId > 0 && bot.RegionId == overview.RegionId &&
                                      !tiles.ContainsKey(bot.ZoneId));

        object? zoneView = zone == null
            ? null
            : new
            {
                id = zone.Id,
                name = zone.Name,
                kind = MapCatalog.KindName(zone.Type, zone.Region),
                realm = MapCatalog.RealmOfRegion(zone.Region),
                realmName = MapCatalog.RealmName(MapCatalog.RealmOfRegion(zone.Region)),
                regionId = zone.Region,
                overviewId = catalog != null && OverviewByZone(catalog).TryGetValue(zone.Id, out string? zoneOverview)
                    ? zoneOverview
                    : string.Empty,
                online = zoneOnline.GetValueOrDefault(zone.Id),
            };

        return new
        {
            serverRunning = state.ServerRunning,
            live = state.Live,
            updatedUtc = state.UpdatedUtc,
            source = state.Live ? "live" : "saved",
            databaseError = _databaseError,
            paintedMaps = catalog != null,
            view,
            viewKind = overview?.Kind ?? (zone != null ? "zone" : string.Empty),
            overviews = overviews.Select(candidate => new
            {
                id = candidate.Id,
                kind = candidate.Kind,
                name = candidate.Name,
                realm = candidate.Realm,
                realmName = MapCatalog.RealmName(candidate.Realm),
                regionId = candidate.RegionId,
                width = candidate.Width,
                height = candidate.Height,
                online = candidate.Online,
                hasImage = candidate.HasImage,
                imageLeft = candidate.ImageLeft,
                imageTop = candidate.ImageTop,
                zones = candidate.Tiles.Select(tile => new
                {
                    id = tile.ZoneId,
                    name = tile.Name,
                    left = tile.Left,
                    top = tile.Top,
                    width = tile.Width,
                    height = tile.Height,
                    hasMap = tile.HasMap,
                    online = tile.Online,
                }).ToList(),
            }).ToList(),
            zones = zoneList.Select(candidate => new
            {
                id = candidate.Id,
                name = candidate.Name,
                kind = candidate.Kind,
                realm = candidate.Realm,
                realmName = MapCatalog.RealmName(candidate.Realm),
                regionId = candidate.RegionId,
                overviewId = candidate.OverviewId,
                online = candidate.Online,
            }).ToList(),
            zone = zoneView,
            bots,
            unplaced,
        };
    }

    private static Dictionary<int, int> ZoneOnline(List<Bot> bots)
    {
        var online = new Dictionary<int, int>();
        foreach (Bot bot in bots)
        {
            if (bot.Online && bot.ZoneId > 0)
                online[bot.ZoneId] = online.GetValueOrDefault(bot.ZoneId) + 1;
        }

        return online;
    }

    /// <summary>Where a bot stands inside its zone's picture, 0..1 on each axis. Capital and dungeon pictures cover
    /// only the playable part of the zone, so the catalog's rectangle is applied for those.</summary>
    private (double X, double Y) MapPosition(Bot bot, MapCatalog? catalog, bool useMapRect)
    {
        if (!ZoneIndex().TryGetValue(bot.ZoneId, out WorldReader.ZoneRow? zone))
            return (0.5, 0.5);
        double left = zone.X;
        double top = zone.Y;
        double width = zone.Width;
        double height = zone.Height;
        if (useMapRect && catalog != null && catalog.Zones.TryGetValue(bot.ZoneId, out MapCatalog.ZoneMap? map) &&
            map.Rect is { Length: 4 } rect)
        {
            left = zone.X + rect[0];
            top = zone.Y + rect[1];
            width = rect[2];
            height = rect[3];
        }

        return ((bot.X - left) / Math.Max(1, width), (bot.Y - top) / Math.Max(1, height));
    }

    private static double Clamp(double value) => value < 0 ? 0 : value > 1 ? 1 : value;

    /// <summary>The painted realm map (<c>r###.png</c>) the overview draws behind its zone squares, when the
    /// install carries it. Same lookup as the zone pictures: the 4x one first.</summary>
    internal byte[]? RegionImage(int regionId)
    {
        if (_mapsDirectory == null)
            return null;
        foreach (string subfolder in new[] { "x4plus", "original" })
        {
            string path = Path.Combine(_mapsDirectory, subfolder, $"r{regionId:000}.png");
            try
            {
                if (File.Exists(path))
                    return File.ReadAllBytes(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    private bool HasRegionImage(int regionId) =>
        _mapsDirectory != null &&
        (File.Exists(Path.Combine(_mapsDirectory, "x4plus", $"r{regionId:000}.png")) ||
         File.Exists(Path.Combine(_mapsDirectory, "original", $"r{regionId:000}.png")));

    private List<MapOverview> BuildOverviews(MapCatalog? catalog, Dictionary<int, int> zoneOnline)
    {
        var overviews = new List<MapOverview>();
        if (catalog != null)
        {
            // The client's own realm maps: each places its zone squares on a painted realm picture. Composing the
            // zone pictures at those squares is the connected realm overview the map window shows.
            foreach (MapCatalog.RegionMap region in catalog.Regions.Values
                         .OrderBy(region => MapCatalog.RealmOfRegion(region.Id)).ThenBy(region => region.Id))
            {
                double minLeft = region.Zones.Min(placement => placement.Left);
                double minTop = region.Zones.Min(placement => placement.Top);
                var tiles = new List<MapTile>(region.Zones.Count);
                foreach (MapCatalog.Placement placement in region.Zones.OrderBy(placement => placement.ZoneId))
                {
                    catalog.Zones.TryGetValue(placement.ZoneId, out MapCatalog.ZoneMap? zone);
                    tiles.Add(new MapTile(placement.ZoneId, zone?.Name ?? $"Zone {placement.ZoneId}",
                        placement.Left - minLeft, placement.Top - minTop, placement.Width, placement.Height, true,
                        zoneOnline.GetValueOrDefault(placement.ZoneId)));
                }

                overviews.Add(CreateOverview($"realm:{region.Id}", "realm", region.Name,
                    MapCatalog.RealmOfRegion(region.Id), region.Id, tiles, HasRegionImage(region.Id),
                    minLeft, minTop));
            }

            // The old frontiers are not on the client's realm maps: the world database places their squares, which
            // tile together the way the game world does.
            var placed = new HashSet<int>(catalog.Regions.Values
                .SelectMany(region => region.Zones).Select(placement => placement.ZoneId));
            for (int realm = 1; realm <= 3; realm++)
            {
                int homeRegion = realm switch { 1 => 1, 2 => 100, _ => 200 };
                if (!RegionIndex().TryGetValue(homeRegion, out WorldReader.RegionRow? home))
                    continue;
                List<WorldReader.ZoneRow> frontier = home.Zones
                    .Where(zone => catalog.Zones.ContainsKey(zone.Id) && !placed.Contains(zone.Id)).ToList();
                if (frontier.Count == 0)
                    continue;
                overviews.Add(CreateOverview($"frontier:{realm}", "frontier",
                    MapCatalog.RealmName(realm) + " Frontiers", realm, homeRegion,
                    DbGridTiles(frontier, catalog, zoneOnline)));
            }
        }
        else
        {
            // Without the converted client pictures there is nothing to compose: the zone outlines from the world
            // database still give each realm a map.
            (int Id, string Name)[] homes =
            [
                (1, "Albion"), (51, "Avalon (Albion)"), (100, "Midgard"),
                (151, "Aegir (Midgard)"), (181, "Hy Brasil (Hibernia)"), (200, "Hibernia"),
            ];
            foreach ((int id, string name) in homes)
            {
                if (!RegionIndex().TryGetValue(id, out WorldReader.RegionRow? region))
                    continue;
                overviews.Add(CreateOverview($"realm:{id}", "realm", name, MapCatalog.RealmOfRegion(id), id,
                    DbGridTiles(region.Zones, catalog, zoneOnline)));
            }
        }

        return overviews;
    }

    /// <summary>A zone layout taken from the world database's own squares (8192-unit steps), used for the old
    /// frontiers and for installs without the painted maps.</summary>
    private static List<MapTile> DbGridTiles(List<WorldReader.ZoneRow> zones, MapCatalog? catalog,
        Dictionary<int, int> zoneOnline)
    {
        if (zones.Count == 0)
            return [];
        long minX = zones.Min(zone => zone.X);
        long minY = zones.Min(zone => zone.Y);
        var tiles = new List<MapTile>(zones.Count);
        foreach (WorldReader.ZoneRow zone in zones.OrderBy(zone => zone.Name, StringComparer.OrdinalIgnoreCase))
        {
            bool hasMap = catalog != null && catalog.Zones.ContainsKey(zone.Id);
            tiles.Add(new MapTile(zone.Id, zone.Name, (zone.X - minX) / 8192.0, (zone.Y - minY) / 8192.0,
                zone.Width / 8192.0, zone.Height / 8192.0, hasMap, zoneOnline.GetValueOrDefault(zone.Id)));
        }

        return tiles;
    }

    private static MapOverview CreateOverview(string id, string kind, string name, int realm, int regionId,
        List<MapTile> tiles, bool hasImage = false, double imageLeft = 0, double imageTop = 0) =>
        new(id, kind, name, realm, regionId,
            tiles.Count == 0 ? 0 : tiles.Max(tile => tile.Left + tile.Width),
            tiles.Count == 0 ? 0 : tiles.Max(tile => tile.Top + tile.Height),
            tiles, tiles.Sum(tile => tile.Online), hasImage, imageLeft, imageTop);

    /// <summary>Every zone the client has a painted map for, with the overview it belongs to (empty when it has no
    /// overview of its own: a capital, a dungeon, a battleground).</summary>
    private List<MapZoneInfo> BuildZoneList(MapCatalog? catalog, Dictionary<int, int> zoneOnline)
    {
        if (catalog == null)
            return [];
        Dictionary<int, string> overviewByZone = OverviewByZone(catalog);
        var zones = new List<MapZoneInfo>(catalog.Zones.Count);
        foreach (MapCatalog.ZoneMap zone in catalog.Zones.Values
                     .OrderBy(zone => MapCatalog.RealmOfRegion(zone.Region))
                     .ThenBy(zone => zone.Name, StringComparer.OrdinalIgnoreCase))
        {
            zones.Add(new MapZoneInfo(zone.Id, zone.Name, MapCatalog.KindName(zone.Type, zone.Region),
                MapCatalog.RealmOfRegion(zone.Region), zone.Region,
                overviewByZone.GetValueOrDefault(zone.Id, string.Empty),
                zoneOnline.GetValueOrDefault(zone.Id)));
        }

        return zones;
    }

    private Dictionary<int, string> OverviewByZone(MapCatalog catalog)
    {
      lock (_mapGate)
      {
        if (_overviewByZone != null)
            return _overviewByZone;
        var map = new Dictionary<int, string>();
        foreach (MapCatalog.RegionMap region in catalog.Regions.Values)
            foreach (MapCatalog.Placement placement in region.Zones)
                map[placement.ZoneId] = $"realm:{region.Id}";
        var placed = new HashSet<int>(map.Keys);
        for (int realm = 1; realm <= 3; realm++)
        {
            int homeRegion = realm switch { 1 => 1, 2 => 100, _ => 200 };
            if (!RegionIndex().TryGetValue(homeRegion, out WorldReader.RegionRow? home))
                continue;
            foreach (WorldReader.ZoneRow zone in home.Zones)
                if (catalog.Zones.ContainsKey(zone.Id) && !placed.Contains(zone.Id))
                    map[zone.Id] = $"frontier:{realm}";
        }

        _overviewByZone = map;
        return map;
      }
    }

    /// <summary>Every painted zone's best map view: the overview that places it, otherwise its own map.</summary>
    private Dictionary<int, string> MapViews()
    {
        lock (_mapGate)
        {
            if (_mapViews != null)
                return _mapViews;
            var views = new Dictionary<int, string>();
            MapCatalog? catalog = Catalog();
            if (catalog != null)
            {
                Dictionary<int, string> overviewByZone = OverviewByZone(catalog);
                foreach (int zoneId in catalog.Zones.Keys)
                    views[zoneId] = overviewByZone.GetValueOrDefault(zoneId, $"zone:{zoneId}");
            }

            _mapViews = views;
            return views;
        }
    }

    private string ResolveView(string requested, List<MapOverview> overviews, MapCatalog? catalog,
        Dictionary<int, int> zoneOnline)
    {
        if (overviews.Any(overview => overview.Id == requested))
            return requested;
        if (catalog != null && requested.StartsWith("zone:", StringComparison.Ordinal) &&
            int.TryParse(requested["zone:".Length..], out int zoneId) && catalog.Zones.ContainsKey(zoneId))
            return requested;

        MapOverview? busiest = overviews.OrderByDescending(overview => overview.Online)
            .ThenBy(overview => overview.Realm).ThenBy(overview => overview.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (busiest != null && (busiest.Online > 0 || catalog == null))
            return busiest.Id;
        if (catalog != null)
        {
            KeyValuePair<int, int> top = zoneOnline.OrderByDescending(pair => pair.Value).FirstOrDefault();
            if (top.Key > 0 && catalog.Zones.ContainsKey(top.Key))
                return $"zone:{top.Key}";
        }

        return busiest?.Id ?? string.Empty;
    }

    /// <summary>The painted map catalog, read once. The dashboard answers several requests at the same time, so the
    /// first load is guarded: a caller that saw a half-loaded catalog would leave the map without its zones.</summary>
    private MapCatalog? Catalog()
    {
        lock (_mapGate)
        {
            if (!_mapCatalogLoaded)
            {
                _mapCatalog = MapCatalog.Load(_mapsDirectory);
                _mapCatalogLoaded = true;
            }

            return _mapCatalog;
        }
    }

    private Dictionary<int, WorldReader.ZoneRow> ZoneIndex()
    {
        lock (_worldGate)
            return ZoneIndexLocked();
    }

    private Dictionary<int, WorldReader.ZoneRow> ZoneIndexLocked()
    {
        if (_zoneById != null)
            return _zoneById;
        var index = new Dictionary<int, WorldReader.ZoneRow>();
        var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (WorldReader.RegionRow region in Regions())
            foreach (WorldReader.ZoneRow zone in region.Zones)
            {
                index[zone.Id] = zone;
                names.TryAdd(zone.Name, zone.Id);
            }

        _zoneIdByName = names;
        _zoneById = index;
        return index;
    }

    private Dictionary<string, int> ZoneNameIndex()
    {
        lock (_worldGate)
        {
            ZoneIndexLocked();
            return _zoneIdByName!;
        }
    }

    /// <summary>The world zone a live position stands in. The position is what the server reports each snapshot, so
    /// it stays right while a bot crosses a zone connection (the zone record can lag a tick behind).</summary>
    private int ZoneAt(int regionId, int x, int y)
    {
        if (regionId <= 0 || !RegionIndex().TryGetValue(regionId, out WorldReader.RegionRow? region))
            return 0;
        foreach (WorldReader.ZoneRow zone in region.Zones)
        {
            if (x >= zone.X && x < zone.X + zone.Width && y >= zone.Y && y < zone.Y + zone.Height)
                return zone.Id;
        }

        return 0;
    }

    private Dictionary<int, WorldReader.RegionRow> RegionIndex()
    {
        lock (_worldGate)
        {
            if (_regionById != null)
                return _regionById;
            var index = new Dictionary<int, WorldReader.RegionRow>();
            foreach (WorldReader.RegionRow region in Regions())
                index[region.Id] = region;
            _regionById = index;
            return index;
        }
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

        List<WorldReader.RosterRow> roster = ReadRoster();

        var bots = new List<Bot>();
        foreach (WorldReader.RosterRow row in roster)
        {
            bool hasLive = statuses.TryGetValue(row.Id, out LiveBots.BotStatus? status);
            bool online = serverRunning && (live ? hasLive : row.IsOnline);
            int level = hasLive ? status!.Level : row.Level;
            string zone = hasLive ? status!.ZoneName ?? row.Zone : row.Zone;
            int zoneId = hasLive ? ZoneAt(status!.RegionId, status.X, status.Y) : 0;
            if (zoneId == 0 && hasLive && status!.ZoneId > 0)
                zoneId = status.ZoneId;
            if (zoneId == 0)
                zoneId = ZoneNameIndex().GetValueOrDefault(row.Zone);
            GroupMetadata? group = online ? ParseGroupMetadata(row.Itinerary) : null;
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
                hasLive ? status!.ObjectiveKind ?? string.Empty : row.ObjectiveKind,
                hasLive ? status!.TargetName ?? string.Empty : row.Target,
                hasLive ? status!.IsAlive : false)
            {
                GroupId = group?.GroupId ?? string.Empty,
                GroupPhase = group?.Phase ?? string.Empty,
                GroupGoal = group?.SharedGoal ?? string.Empty,
                GroupStatus = group?.Status ?? string.Empty,
                GroupLeader = group?.LeaderName ?? string.Empty,
                GroupRendezvous = group?.RendezvousName ?? string.Empty,
                GroupPuller = group?.PullerName ?? string.Empty,
                GroupRole = group?.MemberRole ?? string.Empty,
                ObjectiveAssignmentId = hasLive ? status!.ObjectiveAssignmentId ?? string.Empty : row.ObjectiveAssignmentId,
                ObjectivePhase = hasLive ? status!.ObjectivePhase ?? string.Empty : row.ObjectivePhase,
                ObjectiveExpiresUtc = hasLive ? status!.ObjectiveExpiresUtc ?? string.Empty : row.ObjectiveExpiresUtc,
                HasGroupTaskClock = group?.HasTaskClock == true,
                TaskTimerPaused = group?.TaskTimerPaused == true,
                TaskRemainingMilliseconds = group?.TaskRemainingMilliseconds ?? 0,
                GroupTaskExpiresUtc = group?.TaskExpiresUtc ?? string.Empty,
                MeetUpDeadlineUtc = group?.MeetUpDeadlineUtc ?? string.Empty,
                ZoneId = zoneId,
            });
        }

        return new State(serverRunning, live, snapshot?.UpdatedUtc ?? DateTime.UtcNow, bots);
    }

    /// <summary>Active bot groups: the same grouping the launcher's Active Groups tab shows.</summary>
    internal object Groups()
    {
        State state = Read();
        List<GroupRow> groups = state.Bots
            .Where(bot => bot.Online && !bot.Retired && bot.GroupId.Length > 0)
            .GroupBy(bot => bot.GroupId, StringComparer.Ordinal)
            .Select(group =>
            {
                List<Bot> members = group.ToList();
                // Status writes are asynchronous: prefer a member whose published leader is actually present.
                Bot first = members.First();
                Bot? coherent = members.FirstOrDefault(member =>
                    members.Any(candidate => candidate.Name.Equals(member.GroupLeader, StringComparison.OrdinalIgnoreCase)));
                Bot source = coherent ?? first;
                return new GroupRow(group.Key, source, coherent != null ? source.GroupLeader : "Re-electing…",
                    members.OrderByDescending(member => member.Level).ToList());
            })
            .Where(group => group.Members.Count >= 2)
            .OrderByDescending(group => group.Members.Count)
            .ThenBy(group => group.Source.Realm)
            .ToList();

        return new
        {
            serverRunning = state.ServerRunning,
            live = state.Live,
            updatedUtc = state.UpdatedUtc,
            groups = groups.Select(group => new
            {
                groupId = group.Id,
                realm = group.Source.Realm,
                realmName = RealmName(group.Source.Realm),
                phase = group.Source.GroupPhase,
                goal = group.Source.GroupGoal,
                status = group.Source.GroupStatus,
                leader = group.Leader,
                rendezvous = group.Source.GroupRendezvous,
                puller = group.Source.GroupPuller,
                members = group.Members.Select(member => new
                {
                    id = member.Id,
                    name = member.Name,
                    level = member.Level,
                    className = member.ClassName,
                    realm = member.Realm,
                    realmName = RealmName(member.Realm),
                    zone = member.Zone,
                    activity = member.Activity,
                    online = member.Online,
                    groupPhase = member.GroupPhase,
                    groupRole = member.GroupRole,
                    groupLeader = member.GroupLeader,
                    groupPuller = member.GroupPuller,
                    objectiveAssignmentId = member.ObjectiveAssignmentId,
                    objectiveExpiresUtc = member.ObjectiveExpiresUtc,
                    hasGroupTaskClock = member.HasGroupTaskClock,
                    taskTimerPaused = member.TaskTimerPaused,
                    taskRemainingMilliseconds = member.TaskRemainingMilliseconds,
                    groupTaskExpiresUtc = member.GroupTaskExpiresUtc,
                    meetUpDeadlineUtc = member.MeetUpDeadlineUtc,
                }).ToList(),
            }).ToList(),
        };
    }

    private sealed record GroupRow(string Id, Bot Source, string Leader, List<Bot> Members);
    /// <summary>The group metadata a bot publishes in its itinerary JSON (the launcher's format).</summary>
    private static GroupMetadata? ParseGroupMetadata(string raw)
    {
        const string prefix = "offline-group-v1:";
        if (string.IsNullOrWhiteSpace(raw) || !raw.StartsWith(prefix, StringComparison.Ordinal))
            return null;
        try
        {
            GroupMetadata? metadata = System.Text.Json.JsonSerializer.Deserialize<GroupMetadata>(
                raw[prefix.Length..], new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return string.IsNullOrWhiteSpace(metadata?.GroupId) ? null : metadata;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private sealed class GroupMetadata
    {
        public string GroupId { get; set; } = string.Empty;
        public string Phase { get; set; } = string.Empty;
        public string SharedGoal { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool HasTaskClock { get; set; }
        public bool TaskTimerPaused { get; set; }
        public long TaskRemainingMilliseconds { get; set; }
        public string TaskExpiresUtc { get; set; } = string.Empty;
        public string MeetUpDeadlineUtc { get; set; } = string.Empty;
        public string LeaderName { get; set; } = string.Empty;
        public string RendezvousName { get; set; } = string.Empty;
        public string PullerName { get; set; } = string.Empty;
        public string MemberRole { get; set; } = string.Empty;
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

    private List<WorldReader.RegionRow> Regions()
    {
        if (_regions != null)
            return _regions;
        lock (_worldGate)
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
    }

    /// <summary>
    /// The bot roster. The world database is a WAL SQLite file that the running server writes to; a reader can lose
    /// the race with a checkpoint (SQLite reports "unable to open database file" for a moment), so one failed open
    /// is retried before the screen is told the database is unreadable.
    /// </summary>
    private List<WorldReader.RosterRow> ReadRoster()
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                List<WorldReader.RosterRow> rows = _world.ReadRoster();
                _databaseError = string.Empty;
                return rows;
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.Data.SQLite.SQLiteException)
            {
                if (attempt == 0)
                {
                    Thread.Sleep(60);
                    continue;
                }

                _databaseError = $"The world database could not be read: {exception.Message}";
                return [];
            }
        }
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
        string ObjectiveKind, string Target, bool Alive,
        string GroupId = "", string GroupPhase = "", string GroupGoal = "", string GroupStatus = "",
        string GroupLeader = "", string GroupRendezvous = "", string GroupPuller = "", string GroupRole = "")
    {
        public string ObjectiveAssignmentId { get; init; } = string.Empty;
        public string ObjectivePhase { get; init; } = string.Empty;
        public string ObjectiveExpiresUtc { get; init; } = string.Empty;
        public bool HasGroupTaskClock { get; init; }
        public bool TaskTimerPaused { get; init; }
        public long TaskRemainingMilliseconds { get; init; }
        public string GroupTaskExpiresUtc { get; init; } = string.Empty;
        public string MeetUpDeadlineUtc { get; init; } = string.Empty;

        /// <summary>The world database zone the bot stands in: live from the server snapshot, or looked up by name
        /// for a stopped server. 0 when it cannot be placed.</summary>
        public int ZoneId { get; init; }
    }

    /// <summary>One zone's square on a map overview, in the overview's own layout units.</summary>
    private sealed record MapTile(int ZoneId, string Name, double Left, double Top, double Width, double Height,
        bool HasMap, int Online);

    private sealed record MapOverview(string Id, string Kind, string Name, int Realm, int RegionId,
        double Width, double Height, List<MapTile> Tiles, int Online,
        bool HasImage = false, double ImageLeft = 0, double ImageTop = 0);

    private sealed record MapZoneInfo(int Id, string Name, string Kind, int Realm, int RegionId,
        string OverviewId, int Online);
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

