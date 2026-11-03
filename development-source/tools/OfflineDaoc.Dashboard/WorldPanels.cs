using System.Data.SQLite;
using System.Globalization;
using System.Text.Json;
using DOL.GS;

namespace OfflineDaoc.Dashboard;

/// <summary>
/// The launcher's read-only world screens: server statistics, the Bot Inspector, active RvR (which also feeds the
/// Battlegrounds and Realm Events views), the Realm Exchange and the Realm Event Records.
///
/// Three of them are the server's own published snapshots, fetched through the same state-165 file broker the
/// launcher uses; the other two are read-only SQLite queries (the world database for the exchange, the separate
/// event ledger for the records).
/// </summary>
internal static class WorldPanels
{
    private const ushort AlbionLot = 65001;
    private const ushort MidgardLot = 65002;
    private const ushort HiberniaLot = 65003;
    private const long MaxSnapshotBytes = 8L * 1024 * 1024;

    // The state-165 broker has one request slot per snapshot. Two launcher requests writing it at the same time
    // (two inspector clicks close together, for example) would overwrite each other and one would time out, so each
    // kind of request is sent one at a time; the handler itself answers in a few hundred milliseconds.
    private static readonly SemaphoreSlim InspectorGate = new(1, 1);
    private static readonly SemaphoreSlim StatsGate = new(1, 1);

    /// <summary>What one Bot Inspector read found: the snapshot, or why there was none.</summary>
    internal sealed record InspectorRead(JsonElement? Snapshot, string Failure);

    // ------------------------------------------------------------------ published snapshots (file broker)

    /// <summary>Active RvR, battlegrounds and the realm events: one snapshot the server publishes on change.</summary>
    internal static JsonElement? ReadRvr(string serverDirectory) =>
        ReadFile(Path.Combine(serverDirectory, "state-165", "rvr-world.json"));

    /// <summary>Server statistics: refreshed on demand while the server runs, otherwise the last published file.</summary>
    internal static JsonElement? ReadStats(string serverDirectory, bool serverRunning)
    {
        if (serverRunning)
        {
            StatsGate.Wait();
            try
            {
                JsonElement? fresh = RequestSnapshot(serverDirectory, "server-stats.request", null,
                    Path.Combine("state-165", "server-stats.json"), 5_000);
                if (fresh != null)
                    return fresh;
            }
            finally
            {
                StatsGate.Release();
            }
        }

        return ReadFile(Path.Combine(serverDirectory, "state-165", "server-stats.json"));
    }

    /// <summary>
    /// One bot's full description. Only the running server can answer; without it there is nothing live. A stop is
    /// reported apart from a timeout, so the page does not say "the server is not running" when it merely was busy.
    /// </summary>
    internal static InspectorRead ReadInspector(string serverDirectory, bool serverRunning, long botId)
    {
        if (botId <= 0)
            return new InspectorRead(null, "unknown");
        if (!serverRunning)
            return new InspectorRead(null, "stopped");

        InspectorGate.Wait();
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                JsonElement? snapshot = RequestSnapshot(serverDirectory, "bot-inspect.request", botId,
                    Path.Combine("state-165", "bot-inspect.json"), 6_000);
                if (snapshot == null)
                    continue;
                if (snapshot.Value.TryGetProperty("BotId", out JsonElement id) && id.TryGetInt64(out long found) &&
                    found == botId)
                    return new InspectorRead(snapshot, string.Empty);
                // The broker answered about another bot: the request was overwritten on its way. Ask again.
            }

            return new InspectorRead(null, "timeout");
        }
        finally
        {
            InspectorGate.Release();
        }
    }

    private static JsonElement? RequestSnapshot(string serverDirectory, string requestName, long? botId,
        string responseRelative, int waitMilliseconds, int retries = 0)
    {
        for (int attempt = 0; ; attempt++)
        {
            JsonElement? snapshot = RequestSnapshotOnce(serverDirectory, requestName, botId, responseRelative,
                waitMilliseconds);
            if (snapshot != null || attempt >= retries)
                return snapshot;
        }
    }

    private static JsonElement? RequestSnapshotOnce(string serverDirectory, string requestName, long? botId,
        string responseRelative, int waitMilliseconds)
    {
        string requestId = Guid.NewGuid().ToString("N");
        string requestPath = Path.Combine(serverDirectory, requestName);
        string temporary = requestPath + $".{Environment.ProcessId}.tmp";
        try
        {
            File.WriteAllText(temporary, botId is null ? requestId : $"{requestId}|{botId}");
            File.Move(temporary, requestPath, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        DateTime deadline = DateTime.UtcNow.AddMilliseconds(waitMilliseconds);
        do
        {
            JsonElement? element = ReadFile(Path.Combine(serverDirectory, responseRelative));
            if (element != null && element.Value.TryGetProperty("RequestId", out JsonElement id) &&
                string.Equals(id.GetString(), requestId, StringComparison.Ordinal))
                return element;
            Thread.Sleep(80);
        }
        while (DateTime.UtcNow < deadline);

        return null;
    }

    private static JsonElement? ReadFile(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaxSnapshotBytes)
                return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using JsonDocument document = JsonDocument.Parse(stream);
            return document.RootElement.Clone();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"snapshot {path}: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    // ------------------------------------------------------------------ Realm Exchange (world database)

    internal static object ReadExchange(string database)
    {
        var listings = new List<object>();
        var purchases = new List<object>();
        try
        {
            using var connection = new SQLiteConnection($"Data Source={database};Version=3;Read Only=True;Pooling=False;Default Timeout=5");
            connection.Open();
            ReadListings(connection, listings);
            ReadPurchases(connection, purchases);
        }
        catch (Exception exception) when (exception is SQLiteException or InvalidOperationException)
        {
            return new { available = false, error = exception.Message, listings, purchases };
        }

        return new { available = true, error = string.Empty, listings, purchases };
    }

    private static void ReadListings(SQLiteConnection connection, List<object> listings)
    {
        if (!TableExists(connection, "Inventory") || !TableExists(connection, "ItemTemplate"))
            return;

        bool uniqueItems = TableExists(connection, "ItemUnique");
        bool characters = TableExists(connection, "DOLCharacters");
        bool bots = TableExists(connection, "offline_world_bots");
        string itemName = uniqueItems
            ? "COALESCE(NULLIF(u.Name, ''), NULLIF(t.Name, ''), 'Unknown item')"
            : "COALESCE(NULLIF(t.Name, ''), 'Unknown item')";
        string itemLevel = uniqueItems ? "COALESCE(u.Level, t.Level, 0)" : "COALESCE(t.Level, 0)";
        string seller = characters && bots ? "COALESCE(c.Name, b.Name)" : characters ? "c.Name" : bots ? "b.Name" : "NULL";
        string listedUtc = ColumnExists(connection, "Inventory", "RealmExchangeListedUtc")
            ? "COALESCE(i.RealmExchangeListedUtc, '')" : "''";
        string sql = $"""
            SELECT {itemName} AS ItemName,
                   {seller} AS SellerName,
                   {(bots ? "(b.BotId IS NOT NULL)" : "0")} AS IsBot,
                   {itemLevel} AS ItemLevel,
                   i.Count,
                   i.SellPrice,
                   CASE i.OwnerLot
                       WHEN {AlbionLot} THEN 'Albion'
                       WHEN {MidgardLot} THEN 'Midgard'
                       WHEN {HiberniaLot} THEN 'Hibernia'
                       ELSE 'Unknown' END AS RealmName,
                   {listedUtc} AS ListedUtc
            FROM Inventory i
            LEFT JOIN ItemTemplate t ON t.Id_nb = i.ITemplate_Id
            {(uniqueItems ? "LEFT JOIN ItemUnique u ON u.Id_nb = i.UTemplate_Id" : string.Empty)}
            {(characters ? "LEFT JOIN DOLCharacters c ON c.DOLCharacters_ID = i.OwnerID" : string.Empty)}
            {(bots ? "LEFT JOIN offline_world_bots b ON i.OwnerID = ('offlinebot:' || b.BotId)" : string.Empty)}
            WHERE i.OwnerLot IN ({AlbionLot}, {MidgardLot}, {HiberniaLot})
              AND i.SellPrice > 0
            ORDER BY RealmName, ItemName
            """;
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using SQLiteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string sellerName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            bool isBot = !reader.IsDBNull(2) && reader.GetInt32(2) != 0;
            string listed = reader.IsDBNull(7) ? string.Empty : reader.GetString(7);
            long price = reader.IsDBNull(5) ? 0 : reader.GetInt64(5);
            listings.Add(new
            {
                itemName = reader.IsDBNull(0) ? "Unknown item" : reader.GetString(0),
                itemLevel = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                quantity = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                seller = DescribeSeller(sellerName, isBot),
                priceCopper = price,
                price = Money(price),
                realm = reader.IsDBNull(6) ? "Unknown" : reader.GetString(6),
                expires = DescribeExpiry(isBot, listed),
            });
        }
    }

    private static void ReadPurchases(SQLiteConnection connection, List<object> purchases)
    {
        if (!TableExists(connection, "realm_exchange_sales"))
            return;

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT SoldUtc, Realm, ItemName, Quantity, SellerId, SellerName, BuyerId, BuyerName, PriceCopper
            FROM realm_exchange_sales
            WHERE SaleSequence > 0
            ORDER BY SaleSequence DESC
            """;
        using SQLiteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string soldUtc = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            DateTime soldLocal = DateTime.TryParse(soldUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime sold)
                ? sold.ToLocalTime() : DateTime.MinValue;
            long price = reader.IsDBNull(8) ? 0 : reader.GetInt64(8);
            purchases.Add(new
            {
                sold = soldLocal == DateTime.MinValue ? soldUtc : soldLocal.ToString("yyyy-MM-dd HH:mm:ss"),
                itemName = reader.IsDBNull(2) ? "Unknown item" : reader.GetString(2),
                quantity = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                seller = DescribeParticipant(reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    reader.IsDBNull(5) ? string.Empty : reader.GetString(5)),
                buyer = DescribeParticipant(reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    reader.IsDBNull(7) ? string.Empty : reader.GetString(7)),
                priceCopper = price,
                price = Money(price),
                realm = reader.IsDBNull(1) ? "Unknown"
                    : reader.GetInt32(1) switch { 1 => "Albion", 2 => "Midgard", 3 => "Hibernia", _ => "Unknown" },
            });
        }
    }

    private static string DescribeSeller(string name, bool isBot) =>
        string.IsNullOrWhiteSpace(name) ? "Unknown seller" : name + (isBot ? " (bot)" : " (player)");

    /// <summary>A bot listing lasts 24h from its first load (RealmExchangeExpiry); a player listing never expires.</summary>
    private static string DescribeExpiry(bool isBot, string listedUtc)
    {
        if (!isBot)
            return "Never (player listing)";
        if (!DateTime.TryParse(listedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime listed))
            return "24h from first load";
        DateTime expiresUtc = listed.ToUniversalTime().AddHours(24);
        TimeSpan remaining = expiresUtc - DateTime.UtcNow;
        return remaining <= TimeSpan.Zero
            ? "Expired — awaiting removal"
            : $"{(int)remaining.TotalHours}h {remaining.Minutes:00}m left • {expiresUtc.ToLocalTime():MMM d HH:mm}";
    }

    private static string DescribeParticipant(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Unknown";
        return name + (id.StartsWith("offlinebot:", StringComparison.OrdinalIgnoreCase) ? " (bot)" : " (player)");
    }

    /// <summary>Platinum/gold/silver/copper, the same wording the launcher uses.</summary>
    internal static string Money(long copper)
    {
        long platinum = copper / 1_000_000;
        long gold = copper % 1_000_000 / 10_000;
        long silver = copper % 10_000 / 100;
        long remainder = copper % 100;
        var parts = new List<string>(4);
        if (platinum > 0) parts.Add($"{platinum}p");
        if (gold > 0) parts.Add($"{gold}g");
        if (silver > 0) parts.Add($"{silver}s");
        if (remainder > 0 || parts.Count == 0) parts.Add($"{remainder}c");
        return string.Join(" ", parts);
    }

    // ------------------------------------------------------------------ Realm Event Records (separate ledger)

    internal static object ReadRecords(string serverDirectory, string realm, string kind, string outcome, string search, int page)
    {
        string path = Path.Combine(serverDirectory, "state-165", "realm-event-records.sqlite3");
        try
        {
            (List<RealmEventRecord> rows, long total) = RealmEventRecordStore.Read(path, realm, kind, outcome, search, page);
            return new
            {
                available = true,
                error = string.Empty,
                total,
                page,
                pageSize = 100,
                rows = rows.Select(row => new
                {
                    row.Id,
                    row.EventId,
                    row.Name,
                    row.Kind,
                    row.Realm,
                    row.StartedUtc,
                    row.EndedUtc,
                    row.Phase,
                    row.Outcome,
                    row.Details,
                    row.Assigned,
                    row.Present,
                }).ToList(),
            };
        }
        catch (Exception exception) when (exception is SQLiteException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new
            {
                available = false,
                error = exception.Message,
                total = 0L,
                page,
                pageSize = 100,
                rows = new List<object>(),
            };
        }
    }

    private static bool TableExists(SQLiteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@name";
        command.Parameters.AddWithValue("@name", table);
        return command.ExecuteScalar() != null;
    }

    private static bool ColumnExists(SQLiteConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info([{table.Replace("]", "]]", StringComparison.Ordinal)}])";
        using SQLiteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
