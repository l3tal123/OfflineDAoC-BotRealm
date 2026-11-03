using System.Data.SQLite;
using System.Text;
using System.Text.Json;

namespace OfflineDaoc.Launcher;

/// <summary>
/// True 1.65 Mode (owner 2026-10-30): Classic and Shrouded Isles as they were in patch 1.65. The only changes from the
/// original game are the bot and companion systems and the Realm Exchange. It runs from its own database
/// (data\opendaoc-165.sqlite3.db, built from the normal one the first time it is switched on), so its characters, bots,
/// Realm Exchange, guilds, houses, keeps and relics are separate, and each mode keeps its own progress.
/// The mode marker data\world-mode.txt (key=value lines) says which mode is active; the new client's launcher reads
/// the same file. Switching only happens with the server stopped, after a backup of the database being left.
/// </summary>
internal static class TrueMode
{
    public const string Name = "True 1.65 Mode";
    public const string Summary =
        "Classic and Shrouded Isles as they were in patch 1.65. The only changes from the original game are the bot " +
        "and companion systems and the Realm Exchange.";
    public const string NormalDatabase = "opendaoc.sqlite3.db";
    public const string Database165 = "opendaoc-165.sqlite3.db";
    public const string NormalConfig = @"config\serverconfig.xml";
    public const string Config165 = @"config\serverconfig-165.xml";
    private const int BackupsKeptPerMode = 3;

    public sealed record Mode(bool Is165, string DatabasePath, string ConfigRelative);

    public static string MarkerPath(string root) => Path.Combine(root, "data", "world-mode.txt");

    /// <summary>
    /// OfflineDAoC True 1.65 (owner 2026-10-31) has no normal mode: always True 1.65 Mode. The marker is kept at
    /// mode=1.65 (the server and the new client read it); the normal-mode data is archived in normal-mode-archive\.
    /// </summary>
    public static Mode Read(string root)
    {
        try
        {
            string marker = MarkerPath(root);
            string wanted = $"mode=1.65\r\ndatabase={Database165}\r\nconfig={Config165}";
            if (!File.Exists(marker) || File.ReadAllText(marker).Trim() != wanted)
            {
                File.WriteAllText(marker + ".tmp", wanted, new UTF8Encoding(false));
                File.Move(marker + ".tmp", marker, true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return Describe(root, true);
    }

    public static Mode Describe(string root, bool is165) => new(is165,
        Path.Combine(root, "data", is165 ? Database165 : NormalDatabase), is165 ? Config165 : NormalConfig);

    /// <summary>The database's own flag must agree with the marker (owner rule: refuse to start on a mismatch).</summary>
    public static bool DatabaseAgrees(Mode mode, out string problem)
    {
        problem = string.Empty;
        if (!File.Exists(mode.DatabasePath))
        {
            problem = $"{Path.GetFileName(mode.DatabasePath)} is missing.";
            return false;
        }
        try
        {
            using var c = new SQLiteConnection($"Data Source={mode.DatabasePath};Version=3;Read Only=True;Pooling=False");
            c.Open();
            using var cmd = new SQLiteCommand("SELECT Value FROM ServerProperty WHERE lower(`Key`)='classic165_mode'", c);
            bool flag = string.Equals(cmd.ExecuteScalar() as string, "True", StringComparison.OrdinalIgnoreCase);
            if (flag != mode.Is165)
                problem = mode.Is165
                    ? $"{Database165} is not a {Name} database."
                    : $"{NormalDatabase} is marked as a {Name} database.";
        }
        catch (Exception ex)
        {
            problem = "The database could not be read: " + ex.Message;
        }
        return problem.Length == 0;
    }

    /// <summary>
    /// Switches mode: backs up the database being left, builds the True 1.65 database on the first switch-on, and writes
    /// the marker last (temp file + rename), so a failure leaves the previous mode in place.
    /// </summary>
    private static void Backup(string root, Mode mode)
    {
        string folder = Path.Combine(root, "data", "mode-switch-backups");
        Directory.CreateDirectory(folder);
        string prefix = mode.Is165 ? "true165-" : "normal-";
        string dest = Path.Combine(folder, $"{prefix}{DateTime.Now:yyyyMMdd-HHmmss}.db");
        using (var src = new SQLiteConnection($"Data Source={mode.DatabasePath};Version=3;Pooling=False"))
        using (var dst = new SQLiteConnection($"Data Source={dest};Version=3;Pooling=False"))
        {
            src.Open();
            dst.Open();
            src.BackupDatabase(dst, "main", "main", -1, null, 0);
        }
        foreach (string old in Directory.GetFiles(folder, prefix + "*.db").OrderByDescending(f => f).Skip(BackupsKeptPerMode))
            File.Delete(old);
    }

    // ---- building the True 1.65 database (the launcher copy of tools\claude-version\true165\derive_true165_database.py)

    private sealed record Rules(Dictionary<string, string> Settings, RemoveRules Remove, List<string> StarterGuilds,
        List<UpdateRule>? Updates);

    /// <summary>A generic SQL update of period values (e.g. medallions worn on the neck in 1.65).</summary>
    private sealed record UpdateRule(string Sql, string Reason);

    private sealed record RemoveRules(Dictionary<string, string> MobClasses, Dictionary<string, string> MobPackages,
        Dictionary<string, string> MobNames, string MobNamesKeepPackage, Dictionary<string, string> MobGuilds,
        Dictionary<string, string> LootGenerators, List<WhereRule>? Where);

    /// <summary>A generic removal: rows of a table matching a SQL condition (e.g. the 1.65 starting gear).</summary>
    private sealed record WhereRule(string Table, string Key, string Where, string Reason);

    public static void Derive(string root, Action<string> progress)
    {
        string folder = Path.Combine(root, "true165");
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        Rules rules = JsonSerializer.Deserialize<Rules>(File.ReadAllText(Path.Combine(folder, "true165-rules.json")), json)
                      ?? throw new InvalidDataException("true165-rules.json");
        using JsonDocument policy = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "progress-policy.json")));
        using JsonDocument defaults = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "release-world-defaults.json")));
        string main = Path.Combine(root, "data", NormalDatabase);
        string target = Path.Combine(root, "data", Database165);
        string tmp = target + ".tmp";
        File.Delete(tmp);
        using (var src = new SQLiteConnection($"Data Source={main};Version=3;Pooling=False"))
        using (var dst = new SQLiteConnection($"Data Source={tmp};Version=3;Pooling=False"))
        {
            src.Open();
            dst.Open();
            src.BackupDatabase(dst, "main", "main", -1, null, 0);
            Exec(dst, "PRAGMA foreign_keys=OFF");
            using (SQLiteTransaction tx = dst.BeginTransaction())
            {
                progress("Clearing characters, bots and the Realm Exchange...");
                FreshSlate(dst, policy.RootElement, defaults.RootElement, rules.StarterGuilds);
                foreach ((string key, string value) in rules.Settings)
                    SetProperty(dst, key, value);
                progress("Removing what did not exist in 1.65...");
                RemoveContent(dst, rules.Remove);
                foreach (UpdateRule update in rules.Updates ?? [])
                    Exec(dst, update.Sql);
                progress("Adding the period monster drops...");
                AddDrops(dst, Path.Combine(folder, "drops165.json"));
                progress("Adding the traveler's inns...");
                AddRows(dst, folder);
                StoreContentStamp(dst, ContentStamp(root));
                tx.Commit();
            }
            Exec(dst, "PRAGMA journal_mode=DELETE");
            Exec(dst, "VACUUM");
            using var check = new SQLiteCommand("PRAGMA quick_check", dst);
            if (check.ExecuteScalar() as string != "ok")
                throw new InvalidDataException("The new True 1.65 database failed its check.");
        }
        SQLiteConnection.ClearAllPools();
        File.Move(tmp, target, true);
        WriteConfig(root);
    }

    // ---- keeping an existing True 1.65 world up to date (owner 2026-10-30: test the new menus without losing the world)

    private const string ContentTable = "offline_true165_content";

    /// <summary>A fingerprint of the True 1.65 data files (rules, drops, add-*.json) the database was built from.</summary>
    public static string ContentStamp(string root)
    {
        string folder = Path.Combine(root, "true165");
        using var sha = System.Security.Cryptography.SHA256.Create();
        foreach (string path in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            byte[] name = Encoding.UTF8.GetBytes(Path.GetFileName(path).ToLowerInvariant() + "\n");
            sha.TransformBlock(name, 0, name.Length, null, 0);
            byte[] data = File.ReadAllBytes(path);
            sha.TransformBlock(data, 0, data.Length, null, 0);
        }
        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    private static void StoreContentStamp(SQLiteConnection c, string stamp)
    {
        Exec(c, $"CREATE TABLE IF NOT EXISTS {ContentTable} (Key TEXT PRIMARY KEY, Value TEXT NOT NULL)");
        Exec(c, $"INSERT OR REPLACE INTO {ContentTable} (Key,Value) VALUES ('stamp',@s)", ("@s", stamp));
        Exec(c, $"INSERT OR REPLACE INTO {ContentTable} (Key,Value) VALUES ('applied',@t)", ("@t", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
    }

    private static string? StoredContentStamp(SQLiteConnection c)
    {
        using var exists = new SQLiteCommand("SELECT 1 FROM sqlite_master WHERE type='table' AND name=@n", c);
        exists.Parameters.AddWithValue("@n", ContentTable);
        if (exists.ExecuteScalar() == null)
            return null;
        using var cmd = new SQLiteCommand($"SELECT Value FROM {ContentTable} WHERE Key='stamp'", c);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>
    /// Brings an existing True 1.65 database up to the current data files without touching anyone's progress:
    /// characters, companions, bots, the Realm Exchange, guilds, houses, keeps and relics stay as they are. Only the
    /// world content is re-applied (settings, removals, period updates, drops, and the added rows such as the inn
    /// people, whose changed values are written over). Runs only when the data files changed since the last build or
    /// refresh, and backs the database up first (the same 3 kept per mode). The server must be stopped.
    /// Returns true when the database was updated.
    /// </summary>
    public static bool Refresh(string root, Action<string> progress)
    {
        Mode mode = Read(root);
        string folder = Path.Combine(root, "true165");
        if (!mode.Is165 || !File.Exists(mode.DatabasePath) || !File.Exists(Path.Combine(folder, "true165-rules.json")))
            return false;
        string stamp = ContentStamp(root);
        using (var c = new SQLiteConnection($"Data Source={mode.DatabasePath};Version=3;Read Only=True;Pooling=False"))
        {
            c.Open();
            if (StoredContentStamp(c) == stamp)
                return false;
        }
        progress($"Backing up {Database165} before the content update...");
        Backup(root, mode);
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        Rules rules = JsonSerializer.Deserialize<Rules>(File.ReadAllText(Path.Combine(folder, "true165-rules.json")), json)
                      ?? throw new InvalidDataException("true165-rules.json");
        using (var c = new SQLiteConnection($"Data Source={mode.DatabasePath};Version=3;Pooling=False"))
        {
            c.Open();
            using SQLiteTransaction tx = c.BeginTransaction();
            progress($"Updating the {Name} world content...");
            foreach ((string key, string value) in rules.Settings)
                SetProperty(c, key, value);
            RemoveContent(c, rules.Remove);
            foreach (UpdateRule update in rules.Updates ?? [])
                Exec(c, update.Sql);
            // The period drops are replaced, not added to, so a new drop list (e.g. the second drops pass) is exact.
            Exec(c, "CREATE TABLE IF NOT EXISTS offline_true165_added (Id INTEGER PRIMARY KEY AUTOINCREMENT, SourceTable TEXT NOT NULL, " +
                    "RowKey TEXT NOT NULL, Reason TEXT NOT NULL, AddedAt TEXT NOT NULL)");
            Exec(c, "DELETE FROM LootTemplate WHERE LootTemplate_ID IN (SELECT RowKey FROM offline_true165_added " +
                    "WHERE SourceTable='LootTemplate' AND Reason LIKE 'period drops:%')");
            Exec(c, "DELETE FROM offline_true165_added WHERE SourceTable='LootTemplate' AND Reason LIKE 'period drops:%'");
            AddDrops(c, Path.Combine(folder, "drops165.json"));
            AddRows(c, folder);
            StoreContentStamp(c, stamp);
            tx.Commit();
        }
        SQLiteConnection.ClearAllPools();
        return true;
    }

    private static void FreshSlate(SQLiteConnection c, JsonElement policy, JsonElement defaults, List<string> starterGuilds)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = new SQLiteCommand("SELECT name FROM sqlite_master WHERE type='table'", c))
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                tables.Add(r.GetString(0));
        var clear = policy.GetProperty("ProgressTables").EnumerateArray().Concat(policy.GetProperty("ClearTables").EnumerateArray())
            .Select(e => e.GetString()!).Append("FactionAggroLevel");
        foreach (string table in clear)
            if (tables.Contains(table) && table is not ("Guild" or "GuildRank" or "Account"))
                Exec(c, $"DELETE FROM \"{table}\"");
        Exec(c, "UPDATE Guild SET RealmPoints=0,BountyPoints=0,Bank=0,MeritPoints=0,HaveGuildHouse=0,GuildHouseNumber=0");
        string guilds = string.Join(",", starterGuilds.Select(g => $"'{g.Replace("'", "''")}'"));
        Exec(c, $"DELETE FROM GuildRank WHERE GuildID NOT IN ({guilds})");
        Exec(c, $"DELETE FROM Guild WHERE GuildID NOT IN ({guilds})");
        Exec(c, "UPDATE DBHouse SET OwnerID='',GuildName='',GuildHouse=0,HasConsignment=0,KeptMoney=0,Model=0,Name='' WHERE COALESCE(OwnerID,'')<>''");
        foreach (JsonProperty keep in defaults.GetProperty("Keep").EnumerateObject())
            Exec(c, "UPDATE Keep SET Realm=@r,Level=@l,ClaimedGuildName='' WHERE KeepID=@k",
                ("@r", keep.Value.GetProperty("Realm").GetInt32()), ("@l", keep.Value.GetProperty("Level").GetInt32()),
                ("@k", int.Parse(keep.Name)));
        foreach (JsonProperty door in defaults.GetProperty("Door").EnumerateObject())
            Exec(c, "UPDATE Door SET Health=@h,State=@s WHERE Door_ID=@d",
                ("@h", door.Value.GetProperty("Health").GetInt32()), ("@s", door.Value.GetProperty("State").GetInt32()),
                ("@d", door.Name));
        Exec(c, "UPDATE Relic SET Realm=OriginalRealm,LastRealm=OriginalRealm WHERE OriginalRealm IN (1,2,3)");
        if (tables.Contains("offline_population_settings"))
            Exec(c, "UPDATE offline_population_settings SET Value='0' WHERE Key='ActiveTarget'");
        if (tables.Contains("offline_local_options"))
            Exec(c, "INSERT OR REPLACE INTO offline_local_options(Key,Value) VALUES('MakeMeGM','false')");
    }

    private static void SetProperty(SQLiteConnection c, string key, string value)
    {
        int n = Exec(c, "UPDATE ServerProperty SET Value=@v WHERE lower(`Key`)=@k", ("@v", value), ("@k", key));
        if (n == 0)
            Exec(c, "INSERT INTO ServerProperty (`Key`,Value,DefaultValue,Category,Description,ServerProperty_ID,LastTimeRowUpdated) " +
                    "VALUES (@k,@v,@v,'world','True 1.65 Mode setting',@k,@t)",
                ("@k", key), ("@v", value), ("@t", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
    }

    private static void RemoveContent(SQLiteConnection c, RemoveRules rules)
    {
        Exec(c, "CREATE TABLE IF NOT EXISTS offline_true165_removed (Id INTEGER PRIMARY KEY AUTOINCREMENT, SourceTable TEXT NOT NULL, " +
                "RowKey TEXT, Reason TEXT NOT NULL, RowJson TEXT NOT NULL, RemovedAt TEXT NOT NULL)");
        foreach ((string cls, string why) in rules.MobClasses)
            Archive(c, "Mob", "Mob_ID", "ClassType = @a", why, ("@a", cls));
        foreach ((string pkg, string why) in rules.MobPackages)
            Archive(c, "Mob", "Mob_ID", "PackageID = @a", why, ("@a", pkg));
        foreach ((string name, string why) in rules.MobNames)
            Archive(c, "Mob", "Mob_ID", "Name = @a AND COALESCE(PackageID,'') <> @b", why, ("@a", name), ("@b", rules.MobNamesKeepPackage));
        foreach ((string guild, string why) in rules.MobGuilds)
            Archive(c, "Mob", "Mob_ID", "Guild = @a", why, ("@a", guild));
        foreach ((string cls, string why) in rules.LootGenerators)
            Archive(c, "LootGenerator", "LootGenerator_ID", "LootGeneratorClass = @a", why, ("@a", cls));
        foreach (WhereRule rule in rules.Where ?? [])
            Archive(c, rule.Table, rule.Key, rule.Where, rule.Reason);
    }

    /// <summary>
    /// The period monster drops (drops165.json, from tools\claude-version\true165\build_drops165.py; content165.apply_drops
    /// does the same). Each item joins the monster's own loot template and its per-kill item cap is raised to fit.
    /// </summary>
    private static void AddDrops(SQLiteConnection c, string path)
    {
        if (!File.Exists(path))
            return;
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        Exec(c, "CREATE TABLE IF NOT EXISTS offline_true165_added (Id INTEGER PRIMARY KEY AUTOINCREMENT, SourceTable TEXT NOT NULL, " +
                "RowKey TEXT NOT NULL, Reason TEXT NOT NULL, AddedAt TEXT NOT NULL)");
        string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string reason = "period drops: " + (doc.RootElement.TryGetProperty("source", out JsonElement s) ? s.GetString() : "drops165.json");
        void Added(string table, string key) =>
            Exec(c, "INSERT INTO offline_true165_added (SourceTable,RowKey,Reason,AddedAt) VALUES (@t,@k,@r,@n)",
                ("@t", table), ("@k", key), ("@r", reason), ("@n", now));
        foreach (JsonProperty mob in doc.RootElement.GetProperty("monsters").EnumerateObject())
        {
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = new SQLiteCommand("SELECT ItemTemplateID FROM LootTemplate WHERE lower(TemplateName)=lower(@m)", c))
            {
                cmd.Parameters.AddWithValue("@m", mob.Name);
                using SQLiteDataReader r = cmd.ExecuteReader();
                while (r.Read())
                    have.Add(r.GetString(0));
            }
            foreach (JsonElement item in mob.Value.GetProperty("items").EnumerateArray())
            {
                string id = item[0].GetString()!;
                if (!have.Add(id))
                    continue;
                string key = Guid.NewGuid().ToString();
                Exec(c, "INSERT INTO LootTemplate (TemplateName,ItemTemplateID,Chance,Count,LastTimeRowUpdated,LootTemplate_ID) " +
                        "VALUES (@m,@i,@c,1,@n,@k)", ("@m", mob.Name), ("@i", id), ("@c", item[1].GetInt32()), ("@n", now), ("@k", key));
                Added("LootTemplate", key);
            }
            int cap = mob.Value.GetProperty("drop_count").GetInt32();
            string? rowId = null;
            long current = 0;
            using (var cmd = new SQLiteCommand("SELECT MobXLootTemplate_ID, DropCount FROM MobXLootTemplate WHERE lower(MobName)=lower(@m) " +
                                               "AND lower(LootTemplateName)=lower(@m)", c))
            {
                cmd.Parameters.AddWithValue("@m", mob.Name);
                using SQLiteDataReader r = cmd.ExecuteReader();
                if (r.Read())
                {
                    rowId = r.GetString(0);
                    current = r.IsDBNull(1) ? 0 : r.GetInt64(1);
                }
            }
            if (rowId == null)
            {
                string key = Guid.NewGuid().ToString();
                Exec(c, "INSERT INTO MobXLootTemplate (MobName,LootTemplateName,DropCount,LastTimeRowUpdated,MobXLootTemplate_ID) " +
                        "VALUES (@m,@m,@d,@n,@k)", ("@m", mob.Name), ("@d", cap), ("@n", now), ("@k", key));
                Added("MobXLootTemplate", key);
            }
            else if (current < cap)
                Exec(c, "UPDATE MobXLootTemplate SET DropCount=@d WHERE MobXLootTemplate_ID=@k", ("@d", cap), ("@k", rowId));
        }
    }

    /// <summary>
    /// Generic True 1.65 rows to add: every add-*.json in the folder ({table, key, reason, rows}), e.g. the inn people
    /// (content165.apply_additions does the same). A row whose key is already there is left alone.
    /// </summary>
    private static void AddRows(SQLiteConnection c, string folder)
    {
        string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        Exec(c, "CREATE TABLE IF NOT EXISTS offline_true165_added (Id INTEGER PRIMARY KEY AUTOINCREMENT, SourceTable TEXT NOT NULL, " +
                "RowKey TEXT NOT NULL, Reason TEXT NOT NULL, AddedAt TEXT NOT NULL)");
        foreach (string path in Directory.EnumerateFiles(folder, "add-*.json").Order(StringComparer.Ordinal))
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            string table = doc.RootElement.GetProperty("table").GetString()!;
            string key = doc.RootElement.GetProperty("key").GetString()!;
            string reason = doc.RootElement.GetProperty("reason").GetString()!;
            var columns = new HashSet<string>(StringComparer.Ordinal);
            using (var info = new SQLiteCommand($"PRAGMA table_info(\"{table}\")", c))
            using (SQLiteDataReader r = info.ExecuteReader())
                while (r.Read())
                    columns.Add(r.GetString(1));
            foreach (JsonElement row in doc.RootElement.GetProperty("rows").EnumerateArray())
            {
                string id = row.GetProperty(key).ToString();
                bool present;
                using (var exists = new SQLiteCommand($"SELECT 1 FROM \"{table}\" WHERE \"{key}\"=@k", c))
                {
                    exists.Parameters.AddWithValue("@k", id);
                    present = exists.ExecuteScalar() != null;
                }
                var names = new List<string>();
                using var insert = new SQLiteCommand(c);
                foreach (JsonProperty p in row.EnumerateObject())
                {
                    if (!columns.Contains(p.Name))
                        continue;
                    string param = "@p" + names.Count;
                    names.Add(p.Name);
                    insert.Parameters.AddWithValue(param, p.Value.ValueKind switch
                    {
                        JsonValueKind.Null => DBNull.Value,
                        JsonValueKind.Number => p.Value.TryGetInt64(out long l) ? l : p.Value.GetDouble(),
                        JsonValueKind.True => 1,
                        JsonValueKind.False => 0,
                        _ => (object)p.Value.GetString()!,
                    });
                }
                if (present)
                {
                    // Already there: a row this mode added gets the file's current values (a refresh after the data
                    // changed, e.g. the Horn Handler's class); any other row with that key is left alone.
                    using var ours = new SQLiteCommand("SELECT 1 FROM offline_true165_added WHERE SourceTable=@t AND RowKey=@k", c);
                    ours.Parameters.AddWithValue("@t", table);
                    ours.Parameters.AddWithValue("@k", id);
                    if (ours.ExecuteScalar() == null)
                        continue;
                    insert.CommandText = $"UPDATE \"{table}\" SET {string.Join(",", names.Select((n, i) => $"\"{n}\"=@p{i}"))} " +
                                         $"WHERE \"{key}\"=@key";
                    insert.Parameters.AddWithValue("@key", id);
                    insert.ExecuteNonQuery();
                    continue;
                }
                insert.CommandText = $"INSERT INTO \"{table}\" ({string.Join(",", names.Select(n => $"\"{n}\""))}) " +
                                     $"VALUES ({string.Join(",", names.Select((_, i) => "@p" + i))})";
                insert.ExecuteNonQuery();
                Exec(c, "INSERT INTO offline_true165_added (SourceTable,RowKey,Reason,AddedAt) VALUES (@t,@k,@r,@n)",
                    ("@t", table), ("@k", id), ("@r", reason), ("@n", now));
            }

            // Rows this file added before that it no longer lists (a layout dropped an object, a path got shorter)
            // are removed, so the world matches the data files.
            var listed = new HashSet<string>(doc.RootElement.GetProperty("rows").EnumerateArray()
                .Select(row => row.GetProperty(key).ToString()), StringComparer.Ordinal);
            var stale = new List<string>();
            using (var tracked = new SQLiteCommand("SELECT RowKey FROM offline_true165_added WHERE SourceTable=@t AND Reason=@r", c))
            {
                tracked.Parameters.AddWithValue("@t", table);
                tracked.Parameters.AddWithValue("@r", reason);
                using SQLiteDataReader r = tracked.ExecuteReader();
                while (r.Read())
                    if (!listed.Contains(r.GetString(0)))
                        stale.Add(r.GetString(0));
            }
            foreach (string gone in stale)
            {
                Exec(c, $"DELETE FROM \"{table}\" WHERE \"{key}\"=@k", ("@k", gone));
                Exec(c, "DELETE FROM offline_true165_added WHERE SourceTable=@t AND RowKey=@k AND Reason=@r",
                    ("@t", table), ("@k", gone), ("@r", reason));
            }
        }
    }

    private static void Archive(SQLiteConnection c, string table, string keyColumn, string where, string reason,
        params (string Name, object Value)[] args)
    {
        string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        using var select = new SQLiteCommand($"SELECT * FROM \"{table}\" WHERE {where}", c);
        foreach ((string n, object v) in args)
            select.Parameters.AddWithValue(n, v);
        var rows = new List<(string Key, string Json)>();
        using (SQLiteDataReader r = select.ExecuteReader())
            while (r.Read())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < r.FieldCount; i++)
                    row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
                rows.Add((Convert.ToString(row.GetValueOrDefault(keyColumn)) ?? string.Empty, JsonSerializer.Serialize(row)));
            }
        foreach ((string key, string json) in rows)
            Exec(c, "INSERT INTO offline_true165_removed (SourceTable,RowKey,Reason,RowJson,RemovedAt) VALUES (@t,@k,@r,@j,@n)",
                ("@t", table), ("@k", key), ("@r", reason), ("@j", json), ("@n", now));
        using var delete = new SQLiteCommand($"DELETE FROM \"{table}\" WHERE {where}", c);
        foreach ((string n, object v) in args)
            delete.Parameters.AddWithValue(n, v);
        delete.ExecuteNonQuery();
    }

    private static void WriteConfig(string root)
    {
        string src = Path.Combine(root, "server", NormalConfig);
        string dst = Path.Combine(root, "server", Config165);
        string text = File.ReadAllText(src);
        const string old = "Data Source=../data/opendaoc.sqlite3.db;";
        if (!text.Contains(old))
            throw new InvalidDataException("serverconfig.xml has an unexpected database path.");
        File.WriteAllText(dst, text.Replace(old, "Data Source=../data/opendaoc-165.sqlite3.db;"));
    }

    private static int Exec(SQLiteConnection c, string sql, params (string Name, object Value)[] args)
    {
        using var cmd = new SQLiteCommand(sql, c);
        foreach ((string n, object v) in args)
            cmd.Parameters.AddWithValue(n, v);
        return cmd.ExecuteNonQuery();
    }
}
