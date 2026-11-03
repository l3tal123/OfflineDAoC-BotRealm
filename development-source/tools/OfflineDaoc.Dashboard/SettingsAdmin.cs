using System.Data.SQLite;
using OfflineDaoc.Configuration;
using OfflineDaoc.Launcher;

namespace OfflineDaoc.Dashboard;

/// <summary>
/// The launcher's world settings: XP rate, GM access, announcement switches, Bot Goals and the keep/relic reset.
/// These are the same writes the WinForms launcher's Options and Bot Goals tabs do; every one of them requires a
/// fully stopped server, exactly as before.
/// </summary>
internal static class SettingsAdmin
{
    internal sealed record OptionValues(double XpRate, bool MakeMeGm, bool RvrAnnouncements, bool PveAnnouncements);

    internal static OptionValues Read(string database)
    {
        try
        {
            using var connection = new SQLiteConnection($"Data Source={database};Version=3;Read Only=True;Pooling=False;Default Timeout=5");
            connection.Open();
            double rate = XpRateSetting.Read(connection);
            bool makeMeGm = false;
            if (TableExists(connection, "offline_local_options"))
            {
                using var gm = connection.CreateCommand();
                gm.CommandText = "SELECT Value FROM offline_local_options WHERE Key='MakeMeGM'";
                makeMeGm = string.Equals(gm.ExecuteScalar()?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
            }

            return new OptionValues(rate, makeMeGm,
                ReadSwitch(connection, "rvr_battleground_announcements"),
                ReadSwitch(connection, "pve_realm_event_announcements"));
        }
        catch (Exception exception) when (exception is SQLiteException or InvalidOperationException)
        {
            return new OptionValues(XpRateSetting.Minimum, false, true, true);
        }
    }

    /// <summary>Writes the XP rate; returns the formatted value the server will use.</summary>
    internal static string SaveXpRate(string database, string text, Func<bool> serverStopped)
    {
        if (!serverStopped())
            throw new InvalidOperationException("Stop the server before changing the XP rate.");
        if (!XpRateSetting.TryParse(text, out double rate))
            throw new InvalidOperationException($"The XP rate must be a number between {XpRateSetting.Format(XpRateSetting.Minimum)} and {XpRateSetting.Format(XpRateSetting.Maximum)}.");
        XpRateSetting.Persist(database, rate);
        return XpRateSetting.Format(rate);
    }

    internal static void SaveGm(string database, string account, bool enabled, Func<bool> serverStopped)
    {
        if (!serverStopped())
            throw new InvalidOperationException("Stop the server before changing GM access.");
        using var connection = new SQLiteConnection($"Data Source={database};Version=3;Pooling=False;Default Timeout=10");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "CREATE TABLE IF NOT EXISTS offline_local_options (Key TEXT PRIMARY KEY,Value TEXT NOT NULL)";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO offline_local_options VALUES ('MakeMeGM',@value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value";
        command.Parameters.AddWithValue("@value", enabled ? "true" : "false");
        command.ExecuteNonQuery();
        command.CommandText = "UPDATE Account SET PrivLevel=@privilege WHERE Name=@account";
        command.Parameters.AddWithValue("@privilege", enabled ? 2 : 1);
        command.Parameters.AddWithValue("@account", account);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException(
                "Your local account must exist before changing GM access. Enter the game once, then stop the server and retry.");
        transaction.Commit();
    }

    internal static void SaveAnnouncements(string database, bool rvr, bool pve, Func<bool> serverStopped)
    {
        if (!serverStopped())
            throw new InvalidOperationException("Stop the server before changing announcements.");
        using var connection = new SQLiteConnection($"Data Source={database};Version=3;Pooling=False;Default Timeout=10");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        ServerPropertyWrite.Save(connection, transaction, "autonomous", "rvr_battleground_announcements",
            "RvR and battleground announcements (keeps, relics, sieges), set in the Offline DAoC launcher.", "True",
            rvr ? "True" : "False");
        ServerPropertyWrite.Save(connection, transaction, "autonomous", "pve_realm_event_announcements",
            "PvE realm event announcements (raids forming, raid final bosses), set in the Offline DAoC launcher.", "True",
            pve ? "True" : "False");
        using var retire = connection.CreateCommand();
        retire.Transaction = transaction;
        retire.CommandText = "DELETE FROM ServerProperty WHERE `Key`='game_wide_announcements'";
        retire.ExecuteNonQuery();
        transaction.Commit();
    }

    // ------------------------------------------------------------------ Bot Goals Setting

    internal static BotGoalSettings ReadGoals(string path)
    {
        try
        {
            return BotGoalSettings.Load(path);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return BotGoalSettings.Defaults;
        }
    }

    internal static void SaveGoals(string path, BotGoalSettings settings, Func<bool> serverStopped) =>
        settings.Save(path, serverStopped);

    /// <summary>
    /// Builds the settings from the panel. Levels 1–19 have no RvR goals and level 50 cannot enter the battlegrounds,
    /// so those weights are always 0 (the server refuses any other value).
    /// </summary>
    internal static BotGoalSettings BuildGoals(int l1Solo, int l1Group, int l1Bg,
        int l2Solo, int l2Group, int l2RvR, int l2Bg, int l50Solo, int l50Group, int l50RvR) =>
        new()
        {
            Levels1To19 = new BotGoalWeights(l1Solo, l1Group, 0, l1Bg),
            Levels20To49 = new BotGoalWeights(l2Solo, l2Group, l2RvR, l2Bg),
            Level50 = new BotGoalWeights(l50Solo, l50Group, l50RvR, 0),
        };

    private static bool ReadSwitch(SQLiteConnection connection, string key)
    {
        if (!TableExists(connection, "ServerProperty"))
            return true;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM ServerProperty WHERE `Key`=@key LIMIT 1";
        command.Parameters.AddWithValue("@key", key);
        return !string.Equals(command.ExecuteScalar()?.ToString(), "false", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TableExists(SQLiteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@name";
        command.Parameters.AddWithValue("@name", table);
        return command.ExecuteScalar() != null;
    }
}
