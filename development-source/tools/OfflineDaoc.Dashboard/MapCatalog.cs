using System.Text.Json;

namespace OfflineDaoc.Dashboard;

/// <summary>
/// The new client's painted map catalog: <c>godot/content/maps/maps.json</c>. It lists every zone the client has a
/// painted map for (<c>z###.png</c>) and, for each realm map (<c>r###.png</c>), where that realm map places each
/// zone's square. The launcher composes the realm overviews from those placements, which is the same layout the
/// client's own map window uses, and opens a single zone's picture for the zone view.
/// </summary>
internal sealed class MapCatalog
{
    /// <summary>One painted zone picture. <see cref="Rect"/> is the part of the zone the picture covers, in zone
    /// units (a zone is 65536 units wide); null means the picture is the whole zone.</summary>
    internal sealed record ZoneMap(int Id, string Name, string File, int Region, int Type, int[]? Rect);

    /// <summary>A zone's square on a realm map, in that map's own 256-pixel layout.</summary>
    internal sealed record Placement(int ZoneId, double Left, double Top, double Width, double Height);

    internal sealed record RegionMap(int Id, string Name, string File, List<Placement> Zones);

    private MapCatalog(Dictionary<int, ZoneMap> zones, Dictionary<int, RegionMap> regions)
    {
        Zones = zones;
        Regions = regions;
    }

    internal Dictionary<int, ZoneMap> Zones { get; }

    internal Dictionary<int, RegionMap> Regions { get; }

    /// <summary>The catalog, or null when the install has no converted client maps (the map falls back to zone
    /// outlines from the world database).</summary>
    internal static MapCatalog? Load(string? mapsDirectory)
    {
        if (mapsDirectory == null)
            return null;
        string path = Path.Combine(mapsDirectory, "maps.json");
        try
        {
            if (!File.Exists(path))
                return null;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("zones", out JsonElement zoneElement) ||
                !root.TryGetProperty("regions", out JsonElement regionElement))
                return null;

            var zones = new Dictionary<int, ZoneMap>();
            foreach (JsonProperty property in zoneElement.EnumerateObject())
            {
                if (!int.TryParse(property.Name, out int id))
                    continue;
                JsonElement value = property.Value;
                string file = Text(value, "map");
                if (file.Length == 0)
                    continue;
                zones[id] = new ZoneMap(id, Text(value, "name"), file, Number(value, "region"),
                    Number(value, "type"), Rect(value));
            }

            var regions = new Dictionary<int, RegionMap>();
            foreach (JsonProperty property in regionElement.EnumerateObject())
            {
                if (!int.TryParse(property.Name, out int id))
                    continue;
                JsonElement value = property.Value;
                var placements = new List<Placement>();
                if (value.TryGetProperty("zones", out JsonElement zoneList) && zoneList.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement placement in zoneList.EnumerateArray())
                    {
                        int zoneId = Number(placement, "zone", -1);
                        double width = Decimal(placement, "width");
                        double height = Decimal(placement, "height");
                        if (zoneId < 0 || width <= 0 || height <= 0 || !zones.ContainsKey(zoneId))
                            continue;
                        placements.Add(new Placement(zoneId, Decimal(placement, "left"), Decimal(placement, "top"),
                            width, height));
                    }
                }

                if (placements.Count > 0)
                    regions[id] = new RegionMap(id, Text(value, "name"), Text(value, "map"), placements);
            }

            return new MapCatalog(zones, regions);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"maps catalog {path}: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    /// <summary>The realm a world region belongs to: 1 Albion (with Avalon, its Shrouded Isles), 2 Midgard (with
    /// Aegir), 3 Hibernia (with Hy Brasil); 0 for the battlegrounds and everything else.</summary>
    internal static int RealmOfRegion(int region) => region switch
    {
        >= 1 and <= 99 => 1,
        >= 100 and <= 179 => 2,
        >= 180 and <= 249 => 3,
        _ => 0,
    };

    internal static string RealmName(int realm) => realm switch
    {
        1 => "Albion",
        2 => "Midgard",
        3 => "Hibernia",
        _ => "Battlegrounds",
    };

    internal static string KindName(int type, int region) => type switch
    {
        1 => "Capital",
        2 => "Dungeon",
        _ => region >= 250 ? "Battleground" : "Open world",
    };

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int Number(JsonElement element, string name) => Number(element, name, 0);

    private static int Number(JsonElement element, string name, int fallback) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out int number)
            ? number
            : fallback;

    private static double Decimal(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out double number)
            ? number
            : 0;

    private static int[]? Rect(JsonElement element)
    {
        if (!element.TryGetProperty("map_rect", out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            return null;
        var rect = new int[4];
        int index = 0;
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (index >= 4 || item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out rect[index]))
                return null;
            index++;
        }

        return index == 4 && rect[2] > 0 && rect[3] > 0 ? rect : null;
    }
}
