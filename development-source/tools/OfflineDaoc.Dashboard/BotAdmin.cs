using System.Data.SQLite;
using OfflineDaoc.Launcher;

namespace OfflineDaoc.Dashboard;

/// <summary>
/// The launcher's bot roster changes: create, delete, delete all and teleport. Same SQL and the same rules as the
/// old launcher (bot generation and deletion are launcher-side by design: the launcher edits the world database
/// while the server is stopped, or queues a command for the running server). This is a port of the old launcher's
/// MainForm methods so the web UI can do the same work.
/// </summary>
internal static class BotAdmin
{
    internal const long StartingCopper = 10_000;

    /// <summary>
    /// Creates level-1 bots in a realm, race and class-valid starting area. They earn their levels in the world like
    /// a player does; there is no level-50 shortcut.
    /// </summary>
    internal static int Generate(string database, bool is165, int realm, int count)
    {
        if (!File.Exists(database))
            throw new InvalidOperationException("The prepared world database is missing.");
        if (realm is not (1 or 2 or 3))
            throw new ArgumentOutOfRangeException(nameof(realm));
        if (count is not (1 or 10 or 100))
            throw new ArgumentOutOfRangeException(nameof(count));

        using var connection = new SQLiteConnection($"Data Source={database};Version=3;Pooling=False;Default Timeout=10");
        connection.Open();
        using (var busy = connection.CreateCommand())
        {
            busy.CommandText = "PRAGMA busy_timeout=10000; PRAGMA foreign_keys=OFF;";
            busy.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var names = connection.CreateCommand())
        {
            names.Transaction = transaction;
            names.CommandText = "SELECT Name FROM offline_world_bots UNION SELECT Name FROM DOLCharacters";
            using var reader = names.ExecuteReader();
            while (reader.Read()) reserved.Add(reader.GetString(0));
        }

        // BotRealm rules: a new level-1 bot starts as the base class of the class it rolled, which it keeps as its
        // planned class (column added by the server; until the server has run once, the server converts it instead).
        if (is165 && !HasColumn(connection, transaction, "offline_world_bots", "PlannedClassId"))
        {
            using var add = connection.CreateCommand();
            add.Transaction = transaction;
            add.CommandText = "ALTER TABLE offline_world_bots ADD COLUMN PlannedClassId INT(11) NOT NULL DEFAULT 0";
            add.ExecuteNonQuery();
        }
        bool basePlan = is165 && HasColumn(connection, transaction, "offline_world_bots", "PlannedClassId");
        for (int index = 0; index < count; index++)
        {
            BotCharacterGenerator.Identity identity = BotCharacterGenerator.Generate(realm, reserved, is165);
            int plannedClass = 0;
            if (basePlan && BotCharacterGenerator.BaseClass(identity.ClassId) is { } baseClass)
            {
                plannedClass = identity.ClassId;
                identity = identity with { ClassId = baseClass.Id, ClassName = baseClass.Name };
            }
            BotStartingLocation start = ChooseBotStartingLocation(connection, transaction, identity.Realm,
                identity.RaceId, identity.ClassId, plannedClass);
            string now = DateTime.UtcNow.ToString("O");
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO offline_world_bots
                    (Name, Realm, ClassId, ClassName, RaceId, RaceName, Gender, Level, Experience, RealmPoints,
                     ZoneName, Activity, IsOnline, IsAlive, LastUpdateUtc, MoneyCopper, InventoryRevision,
                     ZoneId, X, Y, Z, RegionId, Health, Mana, Endurance, BindRegionId, BindX, BindY, BindZ,
                     CurrentGoal, ObjectiveProgress, LastMeaningfulProgressUtc, IsRetired)
                VALUES
                    (@name, @realm, @classId, @className, @raceId, @raceName, @gender, 1, 0, 0,
                     @zoneName, 'Queued at randomized starting location', 0, 1, @now, @money, 0,
                     @zoneId, @x, @y, @z, @region, 1, 0, 0, @region, @x, @y, @z,
                     'Awaiting staggered login queue', @placement, @now, 0)
                """;
            if (basePlan)
                insert.CommandText = insert.CommandText.Replace("IsRetired)", "IsRetired, PlannedClassId)").Replace("@now, 0)", "@now, 0, @planned)");
            insert.Parameters.AddWithValue("@planned", plannedClass);
            insert.Parameters.AddWithValue("@name", identity.Name);
            insert.Parameters.AddWithValue("@money", StartingCopper);
            insert.Parameters.AddWithValue("@realm", identity.Realm);
            insert.Parameters.AddWithValue("@classId", identity.ClassId);
            insert.Parameters.AddWithValue("@className", identity.ClassName);
            insert.Parameters.AddWithValue("@raceId", identity.RaceId);
            insert.Parameters.AddWithValue("@raceName", identity.RaceName);
            insert.Parameters.AddWithValue("@gender", identity.Gender);
            insert.Parameters.AddWithValue("@now", now);
            insert.Parameters.AddWithValue("@region", start.RegionId);
            insert.Parameters.AddWithValue("@x", start.X);
            insert.Parameters.AddWithValue("@y", start.Y);
            insert.Parameters.AddWithValue("@z", start.Z);
            insert.Parameters.AddWithValue("@zoneId", start.ZoneId);
            insert.Parameters.AddWithValue("@zoneName", start.RegionId is 51 or 151 or 181
                ? "Shrouded Isles starting area"
                : "Classic starting area");
            insert.Parameters.AddWithValue("@placement",
                $"Launcher assigned a realm/race/class-valid random start in region {start.RegionId} before server startup");
            insert.ExecuteNonQuery();
        }

        UpdatePopulationTarget(connection, transaction);
        transaction.Commit();
        return count;
    }

    /// <summary>Queues the deletion when the server runs (it removes the live bot first); deletes now when stopped.</summary>
    internal static bool Delete(string database, long botId, bool serverRunning)
    {
        using var connection = new SQLiteConnection($"Data Source={database};Version=3;Pooling=False;Default Timeout=10");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (serverRunning)
        {
            using var retire = connection.CreateCommand();
            retire.Transaction = transaction;
            retire.CommandText = "UPDATE offline_world_bots SET IsRetired=1, Activity='Deletion requested', CurrentGoal='', LastUpdateUtc=@now WHERE BotId=@id";
            retire.Parameters.AddWithValue("@id", botId);
            retire.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
            if (retire.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("That character no longer exists.");

            using var queue = connection.CreateCommand();
            queue.Transaction = transaction;
            queue.CommandText = "INSERT INTO offline_bot_commands (BotId, CommandType, RequestedUtc, State) VALUES (@id, 'Delete', @now, 'Pending')";
            queue.Parameters.AddWithValue("@id", botId);
            queue.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
            queue.ExecuteNonQuery();
        }
        else
        {
            PurgeBotData(connection, transaction, botId);
        }

        UpdatePopulationTarget(connection, transaction);
        transaction.Commit();
        return serverRunning;
    }

    /// <summary>Deletes every world bot (never hired companions, never player characters).</summary>
    internal static bool DeleteAll(string database, bool serverRunning)
    {
        using var connection = new SQLiteConnection($"Data Source={database};Version=3;Pooling=False;Default Timeout=10");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        string now = DateTime.UtcNow.ToString("O");
        string hired = NotHiredCompanion(connection, transaction, "BotId"), hiredB = NotHiredCompanion(connection, transaction, "b.BotId");
        string worldBots = $"(SELECT BotId FROM offline_world_bots WHERE 1=1{hired})";
        string hiredInventory = hired.Length == 0 ? string.Empty
            : " AND OwnerID NOT IN (SELECT 'offlinebot:' || BotId FROM offline_hired_companions)";
        if (serverRunning)
        {
            using (var queue = connection.CreateCommand())
            {
                queue.Transaction = transaction;
                queue.CommandText = $"""
                    INSERT INTO offline_bot_commands (BotId, CommandType, RequestedUtc, State)
                    SELECT b.BotId, 'Delete', @now, 'Pending'
                    FROM offline_world_bots b
                    WHERE b.IsRetired=0{hiredB}
                      AND NOT EXISTS (
                          SELECT 1 FROM offline_bot_commands c
                          WHERE c.BotId=b.BotId AND c.CommandType='Delete' AND c.State IN ('Pending','Processing'))
                    """;
                queue.Parameters.AddWithValue("@now", now);
                queue.ExecuteNonQuery();
            }

            using var retire = connection.CreateCommand();
            retire.Transaction = transaction;
            retire.CommandText = "UPDATE offline_world_bots SET IsRetired=1, Activity='Deletion requested', CurrentGoal='', LastUpdateUtc=@now WHERE IsRetired=0" + hired;
            retire.Parameters.AddWithValue("@now", now);
            retire.ExecuteNonQuery();
        }
        else
        {
            foreach (string sql in new[]
            {
                "DELETE FROM Inventory WHERE OwnerID LIKE 'offlinebot:%'" + hiredInventory,
                $"DELETE FROM offline_auction_escrow WHERE BidderBotId IN {worldBots}",
                $"DELETE FROM offline_auction_ledger WHERE SellerBotId IN {worldBots} OR BuyerBotId IN {worldBots}",
                $"DELETE FROM offline_auction_listings WHERE SellerBotId IN {worldBots} OR HighBidderBotId IN {worldBots}",
                $"DELETE FROM offline_bot_commands WHERE BotId IN {worldBots}",
                "DELETE FROM offline_world_bots WHERE 1=1" + hired,
            })
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        UpdatePopulationTarget(connection, transaction);
        transaction.Commit();
        return serverRunning;
    }

    /// <summary>Asks the running server to move the player's character next to a bot (GM only, as the launcher does).</summary>
    internal static void Teleport(string database, long botId, string account, bool serverRunning)
    {
        if (!serverRunning)
            throw new InvalidOperationException("Start the server before teleporting: the bot must be online.");
        if (string.IsNullOrWhiteSpace(account))
            throw new InvalidOperationException("The launcher account is not configured.");
        using var connection = new SQLiteConnection($"Data Source={database};Version=3;Pooling=False;Default Timeout=5");
        connection.Open();
        if (!TableExists(connection, "offline_world_bots") || !TableExists(connection, "offline_bot_commands") ||
            !HasColumn(connection, null, "offline_bot_commands", "RequestedByAccount"))
            throw new InvalidOperationException("The server is still starting. Wait until ENTER REALM is available, then try again.");

        using var transaction = connection.BeginTransaction();
        using (var target = connection.CreateCommand())
        {
            target.Transaction = transaction;
            target.CommandText = "SELECT COUNT(*) FROM offline_world_bots WHERE BotId=@id AND IsOnline=1 AND IsRetired=0";
            target.Parameters.AddWithValue("@id", botId);
            if (Convert.ToInt32(target.ExecuteScalar()) != 1)
                throw new InvalidOperationException("That bot is no longer online.");
        }

        string now = DateTime.UtcNow.ToString("O");
        int replaced;
        using (var replace = connection.CreateCommand())
        {
            replace.Transaction = transaction;
            replace.CommandText = """
                UPDATE offline_bot_commands
                SET BotId=@id, RequestedUtc=@now, State='Pending', CompletedUtc=NULL, Error=NULL
                WHERE CommandId = (
                    SELECT CommandId FROM offline_bot_commands
                    WHERE CommandType='TeleportToBot' AND RequestedByAccount=@account AND State<>'Processing'
                    ORDER BY CASE WHEN State='Pending' THEN 0 ELSE 1 END, CommandId DESC
                    LIMIT 1)
                """;
            replace.Parameters.AddWithValue("@id", botId);
            replace.Parameters.AddWithValue("@now", now);
            replace.Parameters.AddWithValue("@account", account.Trim());
            replaced = replace.ExecuteNonQuery();
        }

        if (replaced == 0)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO offline_bot_commands
                    (BotId, CommandType, RequestedUtc, State, RequestedByAccount)
                VALUES (@id, 'TeleportToBot', @now, 'Pending', @account)
                """;
            insert.Parameters.AddWithValue("@id", botId);
            insert.Parameters.AddWithValue("@now", now);
            insert.Parameters.AddWithValue("@account", account.Trim());
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    internal static int Count(string database)
    {
        try
        {
            using var connection = new SQLiteConnection($"Data Source={database};Version=3;Read Only=True;Pooling=False;Default Timeout=5");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM offline_world_bots WHERE IsRetired=0" + NotHiredCompanion(connection, null, "BotId");
            return Convert.ToInt32(command.ExecuteScalar());
        }
        catch (Exception exception) when (exception is SQLiteException or InvalidOperationException)
        {
            return 0;
        }
    }

    private sealed record BotStartingLocation(int RegionId, int X, int Y, int Z, int ZoneId = 0);

    private static BotStartingLocation ChooseBotStartingLocation(SQLiteConnection connection, SQLiteTransaction transaction,
        int realm, int raceId, int classId, int plannedClassId = 0)
    {
        List<BotStartingLocation> starts = FindBotStartingLocations(connection, transaction, realm, raceId, classId);
        if (starts.Count == 0 && plannedClassId > 0 && plannedClassId != classId)
            starts = FindBotStartingLocations(connection, transaction, realm, raceId, plannedClassId);

        if (starts.Count == 0)
            throw new InvalidOperationException(
                $"No authoritative Classic/Shrouded Isles starting location exists for realm {realm}, race {raceId}, class {classId}" +
                (plannedClassId > 0 && plannedClassId != classId ? $" (planned class {plannedClassId})" : "") + ". No capital fallback was used.");

        return starts[Random.Shared.Next(starts.Count)];
    }

    private static List<BotStartingLocation> FindBotStartingLocations(SQLiteConnection connection, SQLiteTransaction transaction,
        int realm, int raceId, int classId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Region, XPos, YPos, ZPos
            FROM StartupLocation
            WHERE MinVersion <= 168
              AND (RealmID=0 OR RealmID=@realm)
              AND (RaceID=0 OR RaceID=@race)
              AND (ClassID=0 OR ClassID=@class)
              AND (XPos<>0 OR YPos<>0 OR ZPos<>0)
              AND ((@realm=1 AND Region IN (1,51))
                OR (@realm=2 AND Region IN (100,151))
                OR (@realm=3 AND Region IN (200,181)))
            GROUP BY Region, XPos, YPos, ZPos
            """;
        command.Parameters.AddWithValue("@realm", realm);
        command.Parameters.AddWithValue("@race", raceId);
        command.Parameters.AddWithValue("@class", classId);

        var starts = new List<BotStartingLocation>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            starts.Add(new BotStartingLocation(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)));
        return starts;
    }

    private static void PurgeBotData(SQLiteConnection connection, SQLiteTransaction transaction, long botId)
    {
        string owner = $"offlinebot:{botId}";
        foreach (string sql in new[]
        {
            "DELETE FROM Inventory WHERE OwnerID=@owner",
            "DELETE FROM offline_auction_escrow WHERE BidderBotId=@id",
            "DELETE FROM offline_auction_ledger WHERE SellerBotId=@id OR BuyerBotId=@id",
            "DELETE FROM offline_auction_listings WHERE SellerBotId=@id OR HighBidderBotId=@id",
            "DELETE FROM offline_bot_commands WHERE BotId=@id",
            "DELETE FROM offline_world_bots WHERE BotId=@id",
        })
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("@id", botId);
            command.Parameters.AddWithValue("@owner", owner);
            command.ExecuteNonQuery();
        }
    }

    private static void UpdatePopulationTarget(SQLiteConnection connection, SQLiteTransaction transaction)
    {
        int count;
        using (var countCommand = connection.CreateCommand())
        {
            countCommand.Transaction = transaction;
            countCommand.CommandText = "SELECT COUNT(*) FROM offline_world_bots WHERE IsRetired=0" + NotHiredCompanion(connection, transaction, "BotId");
            count = Convert.ToInt32(countCommand.ExecuteScalar());
        }

        using (var settings = connection.CreateCommand())
        {
            settings.Transaction = transaction;
            settings.CommandText = "UPDATE offline_population_settings SET Value=CASE Key WHEN 'PopulationEnabled' THEN @enabled WHEN 'ActiveTarget' THEN @target WHEN 'HardActiveCap' THEN '0' ELSE Value END WHERE Key IN ('PopulationEnabled','ActiveTarget','HardActiveCap')";
            settings.Parameters.AddWithValue("@enabled", count > 0 ? "true" : "false");
            settings.Parameters.AddWithValue("@target", count.ToString());
            settings.ExecuteNonQuery();
        }

        using (var serverProperties = connection.CreateCommand())
        {
            serverProperties.Transaction = transaction;
            serverProperties.CommandText = "UPDATE ServerProperty SET Value=CASE Key WHEN 'population_enabled' THEN @enabled WHEN 'active_target' THEN @target WHEN 'hard_active_cap' THEN '0' ELSE Value END WHERE Key IN ('population_enabled','active_target','hard_active_cap')";
            serverProperties.Parameters.AddWithValue("@enabled", count > 0 ? "true" : "false");
            serverProperties.Parameters.AddWithValue("@target", count.ToString());
            serverProperties.ExecuteNonQuery();
        }
    }

    private static bool TableExists(SQLiteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@name";
        command.Parameters.AddWithValue("@name", table);
        return command.ExecuteScalar() != null;
    }

    private static bool HasColumn(SQLiteConnection connection, SQLiteTransaction? transaction, string table, string column)
    {
        using var info = connection.CreateCommand();
        if (transaction != null)
            info.Transaction = transaction;
        info.CommandText = $"PRAGMA table_info(\"{table}\")";
        using SQLiteDataReader reader = info.ExecuteReader();
        while (reader.Read())
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static string NotHiredCompanion(SQLiteConnection connection, SQLiteTransaction? transaction, string botIdColumn)
    {
        using var command = connection.CreateCommand();
        if (transaction != null)
            command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='offline_hired_companions'";
        return command.ExecuteScalar() == null ? string.Empty
            : $" AND {botIdColumn} NOT IN (SELECT BotId FROM offline_hired_companions)";
    }
}
