using Microsoft.Data.Sqlite;
using PigComic.Core.Data;
using PigComic.Core.Tm;

namespace PigComic.Core.Tb;

/// <summary>
/// SPEC §8 termbase store. Schema §8.1, WAL; language pair fixed at creation and
/// checked on open. Multiple rows may share source_term (synonyms). A forbidden
/// row may have an empty source_term (unconditional forbidden word).
/// </summary>
public sealed class TbStore : IDisposable
{
    private readonly SqliteConnection _conn;

    public string SourceLanguage { get; }
    public string TargetLanguage { get; }

    public TbStore(string dbPath, string sourceLanguage, string targetLanguage)
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
                $"TB database language pair mismatch: file has {existingSrc ?? "?"}/{existingTgt ?? "?"}, " +
                $"store expects {sourceLanguage}/{targetLanguage}.");
        }

        CreateSchema();
    }

    private void CreateSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS terms(
              id          INTEGER PRIMARY KEY,
              source_term TEXT NOT NULL,
              source_norm TEXT NOT NULL,
              target_term TEXT NOT NULL,
              forbidden   INTEGER NOT NULL DEFAULT 0,
              notes       TEXT NOT NULL DEFAULT '',
              created_utc TEXT NOT NULL,
              modified_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_terms_norm ON terms(source_norm);
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Adds a term (or updates by id when provided). Returns the stored row.</summary>
    public Task<TbTerm> UpsertAsync(string sourceTerm, string targetTerm, bool forbidden, string notes, CancellationToken ct)
        => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var norm = Normalizer.Normalize(sourceTerm, SourceLanguage);
            if (!forbidden && string.IsNullOrWhiteSpace(targetTerm))
            {
                throw new InvalidOperationException("A non-forbidden term requires a target.");
            }

            var utc = DateTime.UtcNow.ToString("O");
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO terms(source_term, source_norm, target_term, forbidden, notes, created_utc, modified_utc)
                VALUES($s, $sn, $t, $f, $n, $c, $m) RETURNING id;
                """;
            cmd.Parameters.AddWithValue("$s", sourceTerm);
            cmd.Parameters.AddWithValue("$sn", norm);
            cmd.Parameters.AddWithValue("$t", targetTerm);
            cmd.Parameters.AddWithValue("$f", forbidden ? 1 : 0);
            cmd.Parameters.AddWithValue("$n", notes);
            cmd.Parameters.AddWithValue("$c", utc);
            cmd.Parameters.AddWithValue("$m", utc);
            var id = (long)cmd.ExecuteScalar()!;
            return GetById(id)!;
        }, ct);

    public Task<bool> DeleteAsync(long id, CancellationToken ct)
        => Task.Run(() =>
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM terms WHERE id=$i;";
            cmd.Parameters.AddWithValue("$i", id);
            return cmd.ExecuteNonQuery() > 0;
        }, ct);

    public TbTerm? GetById(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM terms WHERE id=$i;";
        cmd.Parameters.AddWithValue("$i", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadTerm(reader) : null;
    }

    /// <summary>All terms in the base.</summary>
    public List<TbTerm> All()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM terms ORDER BY id;";
        using var reader = cmd.ExecuteReader();
        var terms = new List<TbTerm>();
        while (reader.Read())
        {
            terms.Add(ReadTerm(reader));
        }

        return terms;
    }

    internal SqliteConnection Connection => _conn;

    internal static TbTerm ReadTerm(SqliteDataReader reader)
        => new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt64(4) != 0,
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7));

    public void Dispose() => _conn.Dispose();
}