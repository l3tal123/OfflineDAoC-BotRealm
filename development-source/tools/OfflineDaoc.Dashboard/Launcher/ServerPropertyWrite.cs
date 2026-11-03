using System.Data.SQLite;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Insert or update one ServerProperty row for the launcher. A row the server already created keeps its own
/// ServerProperty_ID; a new row gets a fresh GUID, because ServerProperty_ID has a UNIQUE index and the launcher used to
/// insert an empty string there, which collided with the world's one empty ID (bot_xp_rate) the first time a new setting
/// was saved (owner 2026-11-01: "UNIQUE constraint failed: ServerProperty.ServerProperty_ID").
/// </summary>
internal static class ServerPropertyWrite
{
    public static void Save(SQLiteConnection connection, SQLiteTransaction transaction, string category, string key,
        string description, string defaultValue, string value)
    {
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE ServerProperty SET Value=@value, LastTimeRowUpdated=@updated WHERE lower(`Key`)=@key";
        update.Parameters.AddWithValue("@value", value);
        update.Parameters.AddWithValue("@updated", DateTime.UtcNow.ToString("O"));
        update.Parameters.AddWithValue("@key", key);
        if (update.ExecuteNonQuery() > 0)
            return;

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO ServerProperty (Category, `Key`, Description, DefaultValue, Value, LastTimeRowUpdated, ServerProperty_ID)
            VALUES (@category, @key, @description, @defaultValue, @value, @updated, @id)
            """;
        insert.Parameters.AddWithValue("@category", category);
        insert.Parameters.AddWithValue("@key", key);
        insert.Parameters.AddWithValue("@description", description);
        insert.Parameters.AddWithValue("@defaultValue", defaultValue);
        insert.Parameters.AddWithValue("@value", value);
        insert.Parameters.AddWithValue("@updated", DateTime.UtcNow.ToString("O"));
        insert.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        insert.ExecuteNonQuery();
    }

    /// <summary>The stored value of one property row, or null when the table or the row is missing.</summary>
    public static string? Read(SQLiteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM ServerProperty WHERE lower(`Key`)=@key LIMIT 1";
        command.Parameters.AddWithValue("@key", key);
        return command.ExecuteScalar()?.ToString();
    }
}
