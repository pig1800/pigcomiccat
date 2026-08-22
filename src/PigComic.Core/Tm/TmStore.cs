using Microsoft.Data.Sqlite;
using PigComic.Core.Data;

namespace PigComic.Core.Tm;

/// <summary>
/// SPEC §7 TM store. Schema §7.1, WAL; upsert keyed on (source_hash,
/// IFNULL(character,'')) — newest wins per (source, speaker) (D-09); same source
/// by a different character is a separate entry. Grams (§7.4) are the retrieval
/// index, rebuildable. The language pair is fixed at creation and checked on open
/// (SPEC §6.2 / D-28).
/// </summary>
public sealed class TmStore : IDisposable
{
    private readonly SqliteConnection _conn;

    public string SourceLanguage { get; }
    public string TargetLanguage { get; }

    public TmStore(string dbPath, string sourceLanguage, string targetLanguage)
    {
        SourceLanguage = sourceLanguage;
        TargetLanguage = targetLanguage;
        _conn = SqliteInit.OpenDb(dbPath, ("sourceLanguage", sourceLanguage), ("targetLanguage", targetLanguage));

        var existingSrc = SqliteInit.GetMeta(_conn, "sourceLanguage");
        var existingTgt = SqliteInit.GetMeta(_conn, "targetLanguage");
        if ((existingSrc is not null && !string.Equals(existingSrc, sourceLanguage, StringComparison.Ordinal)) ||
            (existingTgt is not null && !string.Equals(existingTgt, targetLanguage, StringComparison.Ordinal)))
        {
            _conn.Dispose();
            throw new InvalidOperationException(
                $"TM database language pair mismatch: file has {existingSrc ?? "?"}/{existingTgt ?? "?"}, " +
                $"store expects {sourceLanguage}/{targetLanguage}.");
        }

        CreateSchema();
    }

    /// <summary>Opens an existing TM for reading; throws if it does not exist or the pair mismatches.</summary>
    public static TmStore OpenExisting(string dbPath, string sourceLanguage, string targetLanguage)
    {
        if (!File.Exists(dbPath))
        {
            throw new FileNotFoundException($"TM database not found: {dbPath}");
        }

        return new TmStore(dbPath, sourceLanguage, targetLanguage);
    }

    private void CreateSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS entries(
              id           INTEGER PRIMARY KEY,
              source_raw   TEXT NOT NULL,
              source_norm  TEXT NOT NULL,
              source_hash  INTEGER NOT NULL,
              target_raw   TEXT NOT NULL,
              character    TEXT,
              kind         TEXT,
              chapter      TEXT,
              bubble_id    TEXT,
              prev_hash    INTEGER,
              created_utc  TEXT NOT NULL,
              modified_utc TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_entries_src_char ON entries(source_hash, IFNULL(character,''));
            CREATE INDEX IF NOT EXISTS ix_entries_hash ON entries(source_hash);
            CREATE TABLE IF NOT EXISTS grams(
              gram     TEXT NOT NULL,
              entry_id INTEGER NOT NULL REFERENCES entries(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_grams_gram ON grams(gram);
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Upserts an entry: normalized source keyed by (hash, character-or-empty);
    /// newest wins. Returns the stored entry (or null when nothing is written —
    /// empty target or empty normalized source, §7.1/§7.2).
    /// </summary>
    public Task<TmEntry?> UpsertAsync(
        string sourceRaw,
        string targetRaw,
        string? character,
        string? kind,
        string? chapter,
        string? bubbleId,
        long? prevHash,
        CancellationToken ct)
        => Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(targetRaw))
            {
                return (TmEntry?)null; // §7.1: empty target writes nothing
            }

            var norm = Normalizer.Normalize(sourceRaw, SourceLanguage);
            if (norm.Length == 0)
            {
                return (TmEntry?)null; // §7.2: empty normalized sources are never written
            }

            var hash = TmHash.Compute(norm);
            var utc = DateTime.UtcNow.ToString("O");
            ct.ThrowIfCancellationRequested();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO entries(source_raw, source_norm, source_hash, target_raw, character, kind, chapter, bubble_id, prev_hash, created_utc, modified_utc)
                VALUES($sr, $sn, $sh, $tr, $c, $k, $ch, $bi, $ph, $cu, $mu)
                ON CONFLICT(source_hash, IFNULL(character,'')) DO UPDATE SET
                  source_raw=excluded.source_raw, source_norm=excluded.source_norm,
                  target_raw=excluded.target_raw, kind=excluded.kind, chapter=excluded.chapter,
                  bubble_id=excluded.bubble_id, prev_hash=excluded.prev_hash, modified_utc=excluded.modified_utc
                RETURNING id;
                """;
            cmd.Parameters.AddWithValue("$sr", sourceRaw);
            cmd.Parameters.AddWithValue("$sn", norm);
            cmd.Parameters.AddWithValue("$sh", hash);
            cmd.Parameters.AddWithValue("$tr", targetRaw);
            cmd.Parameters.AddWithValue("$c", (object?)character ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$k", (object?)kind ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ch", (object?)chapter ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$bi", (object?)bubbleId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ph", (object?)prevHash ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cu", utc);
            cmd.Parameters.AddWithValue("$mu", utc);
            var id = (long?)cmd.ExecuteScalar();
            if (id is null)
            {
                return (TmEntry?)null;
            }

            InsertGrams(norm, id.Value);
            return GetById(id.Value);
        }, ct);

    private void InsertGrams(string norm, long entryId)
    {
        foreach (var gram in GramExtractor.Extract(norm, SourceLanguage))
        {
            using var gcmd = _conn.CreateCommand();
            gcmd.CommandText = "INSERT INTO grams(gram, entry_id) VALUES($g, $e);";
            gcmd.Parameters.AddWithValue("$g", gram);
            gcmd.Parameters.AddWithValue("$e", entryId);
            gcmd.ExecuteNonQuery();
        }
    }

    public TmEntry? GetById(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM entries WHERE id=$i;";
        cmd.Parameters.AddWithValue("$i", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadEntry(reader) : null;
    }

    public Task<bool> DeleteAsync(long id, CancellationToken ct)
        => Task.Run(() =>
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM entries WHERE id=$i;";
            cmd.Parameters.AddWithValue("$i", id);
            return cmd.ExecuteNonQuery() > 0;
        }, ct);

    /// <summary>SPEC §7.6: drop and rebuild the grams index from entries.</summary>
    public Task RebuildGramsAsync(CancellationToken ct)
        => Task.Run(() =>
        {
            using var wipe = _conn.CreateCommand();
            wipe.CommandText = "DELETE FROM grams;";
            wipe.ExecuteNonQuery();

            using var list = _conn.CreateCommand();
            list.CommandText = "SELECT id, source_norm FROM entries;";
            using var reader = list.ExecuteReader();
            var batch = new List<(string Norm, long Id)>();
            while (reader.Read())
            {
                batch.Add((reader.GetString(1), reader.GetInt64(0)));
            }

            foreach (var (norm, id) in batch)
            {
                InsertGrams(norm, id);
            }
        }, ct);

    public long CountEntries()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM entries;";
        return (long)cmd.ExecuteScalar()!;
    }

    public long CountGrams()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM grams;";
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>All entries in reading order (used by exchange export/import counting).</summary>
    public List<TmEntry> AllEntries()
    {
        var list = new List<TmEntry>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM entries ORDER BY id;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(ReadEntry(reader));
        }

        return list;
    }

    internal SqliteConnection Connection => _conn;

    internal static TmEntry ReadEntry(SqliteDataReader reader)
        => new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetInt64(9),
            reader.GetString(10),
            reader.GetString(11));

    public void Dispose() => _conn.Dispose();
}