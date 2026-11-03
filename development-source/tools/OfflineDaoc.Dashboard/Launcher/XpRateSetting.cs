using System.Data.SQLite;
using System.Globalization;

namespace OfflineDaoc.Launcher;

/// <summary>
/// The dashboard's XP rate (owner 2026-11-01): one multiplier for experience from every source, for players and for
/// every kind of gamebot. It is stored as the ServerProperty xp_rate_multiplier and applied by the server's
/// XpRateMultiplier165. The accepted value is a number from 1 to 5000; anything that is not a finite number is refused,
/// and anything outside the range is clamped to it.
/// </summary>
internal static class XpRateSetting
{
    public const string PropertyKey = "xp_rate_multiplier";
    public const double Minimum = 1.0;
    public const double Maximum = 5000.0;

    // "Check if the input is a number and make it maximum value of 5000 and minimum of 1" (owner 2026-11-01): a comma
    // is accepted as the decimal separator too, as the dashboard may run on a comma locale.
    public static bool TryParse(string? text, out double rate)
    {
        rate = Minimum;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string normalized = text.Trim().Replace(',', '.');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
            !double.IsFinite(parsed))
            return false;
        rate = Math.Clamp(parsed, Minimum, Maximum);
        return true;
    }

    public static string Format(double rate) => rate.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>The saved rate, 1 (normal) when the row is missing or unusable.</summary>
    public static double Read(SQLiteConnection connection)
    {
        try
        {
            return TryParse(ServerPropertyWrite.Read(connection, PropertyKey), out double rate) ? rate : Minimum;
        }
        catch (SQLiteException)
        {
            return Minimum;
        }
    }

    /// <summary>Writes the rate; the caller must have made sure the server is stopped.</summary>
    public static void Persist(string databasePath, double rate)
    {
        using var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;Pooling=False;Default Timeout=10");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        ServerPropertyWrite.Save(connection, transaction, "rates", PropertyKey,
            "Multiplies experience earned from every source (players and every kind of gamebot) by this rate. 1 is normal. Set in the Offline DAoC launcher.",
            "1", Format(rate));
        transaction.Commit();
    }
}
