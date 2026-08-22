namespace PigComic.Core.Data;

/// <summary>Creates the SQLite databases used by the TM/TB stores (SPEC §7.1/§8.1).</summary>
public static class SqliteInit
{
    public const string MetaSchemaV1 = "1";

    /// <summary>
    /// Opens (creating if needed) a database with meta key/value table and WAL
    /// journal mode. Sets schemaVersion and the given meta pairs when absent
    /// (existing values are kept intact so language-pair mismatches surface).
    /// </summary>
    public static Microsoft.Data.Sqlite.SqliteConnection OpenDb(
        string path,
        params (string Key, string Value)[] metaPairs)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "PRAGMA journal_mode=WAL; " +
                "CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT NOT NULL);";
            cmd.ExecuteNonQuery();
        }

        SetMetaDefault(conn, "schemaVersion", MetaSchemaV1);
        foreach (var (key, value) in metaPairs)
        {
            SetMetaDefault(conn, key, value);
        }

        return conn;
    }

    /// <summary>Writes the meta key only when absent (keeps existing values intact).</summary>
    private static void SetMetaDefault(Microsoft.Data.Sqlite.SqliteConnection conn, string key, string value)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO meta(key, value) VALUES($k, $v) ON CONFLICT(key) DO NOTHING;";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public static string? GetMeta(Microsoft.Data.Sqlite.SqliteConnection conn, string key)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key=$k;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }
}