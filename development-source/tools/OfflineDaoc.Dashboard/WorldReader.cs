using System.Data.SQLite;

namespace OfflineDaoc.Dashboard;

/// <summary>
/// Read-only world reads: the bot roster and the zone geometry the server already ships in its database.
/// The database is opened read-only; the dashboard never writes to the world.
/// </summary>
internal sealed class WorldReader
{
    private readonly string _databasePath;

    internal WorldReader(string databasePath)
    {
        _databasePath = databasePath;
    }

    internal bool Exists => File.Exists(_databasePath);

    private SQLiteConnection Open() => new($"Data Source={_databasePath};Version=3;Read Only=True;Pooling=False;Default Timeout=5");

    /// <summary>The autonomous bot roster (hired companions have their own table and are left out).</summary>
    internal List<RosterRow> ReadRoster()
    {
        var rows = new List<RosterRow>();
        using SQLiteConnection connection = Open();
        connection.Open();
        bool hasCompanions = TableExists(connection, "offline_hired_companions");
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT BotId, Name, Realm, COALESCE(RaceName, ''), Gender, COALESCE(ClassName, ''), Level, " +
            "COALESCE(ZoneName, ''), COALESCE(Activity, ''), COALESCE(CurrentGoal, ''), COALESCE(TargetName, ''), " +
            "COALESCE(TravelDestination, ''), COALESCE(ObjectiveProgress, ''), IsOnline, IsRetired " +
            "FROM offline_world_bots WHERE 1=1" +
            (hasCompanions ? " AND BotId NOT IN (SELECT BotId FROM offline_hired_companions)" : string.Empty) +
            " ORDER BY Realm, Level DESC, Name";
        using SQLiteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new RosterRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.GetString(12),
                reader.GetBoolean(13),
                reader.GetBoolean(14)));
        }

        return rows;
    }

    /// <summary>
    /// The world's regions and their zones, in the same coordinates the server uses (zone offsets are in 8192-unit
    /// steps). This is the geometry the launcher's world map draws from.
    /// </summary>
    internal List<RegionRow> ReadRegions()
    {
        var regions = new Dictionary<int, RegionRow>();
        using SQLiteConnection connection = Open();
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT Z.ZoneID, COALESCE(Z.Name, ''), Z.RegionID, Z.OffsetX, Z.OffsetY, Z.Width, Z.Height, Z.Realm, " +
            "COALESCE(R.Description, R.Name, '') " +
            "FROM Zones Z LEFT JOIN Regions R ON R.RegionID = Z.RegionID";
        using SQLiteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            int regionId = reader.GetInt32(2);
            long width = (long)reader.GetInt32(5) * 8192;
            long height = (long)reader.GetInt32(6) * 8192;
            if (width <= 0 || height <= 0)
                continue;
            long x = (long)reader.GetInt32(3) * 8192;
            long y = (long)reader.GetInt32(4) * 8192;
            if (!regions.TryGetValue(regionId, out RegionRow? region))
            {
                string name = reader.IsDBNull(8) || reader.GetString(8).Length == 0 ? $"Region {regionId}" : reader.GetString(8);
                region = new RegionRow(regionId, name);
                regions[regionId] = region;
            }

            region.Zones.Add(new ZoneRow(
                reader.GetInt32(0),
                reader.GetString(1),
                x,
                y,
                width,
                height,
                reader.GetInt32(7)));
        }

        foreach (RegionRow region in regions.Values)
            region.Complete();
        return regions.Values
            .OrderBy(region => region.Realm == 0 ? int.MaxValue : region.Realm)
            .ThenBy(region => region.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TableExists(SQLiteConnection connection, string table)
    {
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@name";
        command.Parameters.AddWithValue("@name", table);
        return command.ExecuteScalar() != null;
    }

    internal sealed record RosterRow(long Id, string Name, int Realm, string Race, int Gender, string ClassName, int Level,
        string Zone, string Activity, string Goal, string Target, string Destination, string Progress,
        bool IsOnline, bool IsRetired);

    internal sealed class ZoneRow
    {
        internal ZoneRow(int id, string name, long x, long y, long width, long height, int realm)
        {
            Id = id;
            Name = name;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Realm = realm;
        }

        internal int Id { get; }
        internal string Name { get; }
        internal long X { get; }
        internal long Y { get; }
        internal long Width { get; }
        internal long Height { get; }
        internal int Realm { get; }
    }

    internal sealed class RegionRow
    {
        internal RegionRow(int id, string name)
        {
            Id = id;
            Name = name;
        }

        internal int Id { get; }
        internal string Name { get; }
        internal List<ZoneRow> Zones { get; } = [];
        internal long MinX { get; private set; }
        internal long MinY { get; private set; }
        internal long MaxX { get; private set; }
        internal long MaxY { get; private set; }

        /// <summary>The region's realm, or 0 when its zones disagree (a mixed or contested region).</summary>
        internal int Realm { get; private set; }

        internal void Complete()
        {
            MinX = Zones.Min(zone => zone.X);
            MinY = Zones.Min(zone => zone.Y);
            MaxX = Zones.Max(zone => zone.X + zone.Width);
            MaxY = Zones.Max(zone => zone.Y + zone.Height);
            int realm = Zones.Select(zone => zone.Realm).FirstOrDefault(value => value != 0);
            Realm = Zones.All(zone => zone.Realm == realm) ? realm : 0;
        }
    }
}
