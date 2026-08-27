using Microsoft.Data.Sqlite;
using PigComic.Core.Data;

namespace PigComic.Core.Project;

/// <summary>
/// SPEC §6.3 master character list, SQLite-backed (replaces the former characters.json).
/// One row per character keyed by the ORIGINAL name (the value <c>@character</c> references).
/// The portrait is a PNG BLOB (the App downscales to ≤256×256 on ingest, preserving aspect
/// ratio — Core never touches SkiaSharp). Localized + Pronunciation are metadata; the other
/// §6.3 fields (gender/age/firstChapter/pronoun/comments) survive unchanged.
/// </summary>
public sealed class CharacterStore : IDisposable
{
    public const string FileName = "characters.db";

    private readonly SqliteConnection _conn;

    public CharacterStore(string dbPath)
    {
        _conn = SqliteInit.OpenDb(dbPath);
        CreateSchema();
    }

    private void CreateSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS characters(
              name          TEXT PRIMARY KEY,
              localized     TEXT NOT NULL DEFAULT '',
              pronunciation TEXT NOT NULL DEFAULT '',
              image         BLOB,
              gender        TEXT NOT NULL DEFAULT '',
              age           TEXT NOT NULL DEFAULT '',
              firstChapter  TEXT NOT NULL DEFAULT '',
              pronoun       TEXT NOT NULL DEFAULT '',
              comments      TEXT NOT NULL DEFAULT '',
              created_utc   TEXT NOT NULL,
              modified_utc  TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public sealed record MasterCharacter(
        string Name,
        string Localized,
        string Pronunciation,
        byte[]? Image,
        string Gender,
        string Age,
        string FirstChapter,
        string Pronoun,
        string Comments);

    public IReadOnlyList<MasterCharacter> LoadAll()
    {
        var list = new List<MasterCharacter>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT name, localized, pronunciation, image, gender, age, firstChapter, pronoun, comments FROM characters ORDER BY name;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(Read(r));
        }

        return list;
    }

    public MasterCharacter? Find(string name)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT name, localized, pronunciation, image, gender, age, firstChapter, pronoun, comments FROM characters WHERE name=$n;";
        cmd.Parameters.AddWithValue("$n", name);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    /// <summary>Upserts by name. Returns false if a DIFFERENT row already uses the name (uniqueness).</summary>
    public bool AddOrUpdate(MasterCharacter c)
    {
        var utc = DateTime.UtcNow.ToString("O");
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO characters(name, localized, pronunciation, image, gender, age, firstChapter, pronoun, comments, created_utc, modified_utc)
            VALUES($n, $l, $p, $i, $g, $a, $f, $pr, $c, $u, $u)
            ON CONFLICT(name) DO UPDATE SET
              localized=excluded.localized, pronunciation=excluded.pronunciation,
              image=excluded.image, gender=excluded.gender, age=excluded.age,
              firstChapter=excluded.firstChapter, pronoun=excluded.pronoun,
              comments=excluded.comments, modified_utc=excluded.modified_utc;
            """;
        cmd.Parameters.AddWithValue("$n", c.Name);
        cmd.Parameters.AddWithValue("$l", c.Localized ?? "");
        cmd.Parameters.AddWithValue("$p", c.Pronunciation ?? "");
        cmd.Parameters.AddWithValue("$i", (object?)c.Image ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$g", c.Gender ?? "");
        cmd.Parameters.AddWithValue("$a", c.Age ?? "");
        cmd.Parameters.AddWithValue("$f", c.FirstChapter ?? "");
        cmd.Parameters.AddWithValue("$pr", c.Pronoun ?? "");
        cmd.Parameters.AddWithValue("$c", c.Comments ?? "");
        cmd.Parameters.AddWithValue("$u", utc);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool Remove(string name)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM characters WHERE name=$n;";
        cmd.Parameters.AddWithValue("$n", name);
        return cmd.ExecuteNonQuery() > 0;
    }

    private static MasterCharacter Read(SqliteDataReader r)
    {
        byte[]? img = null;
        if (!r.IsDBNull(3))
        {
            using var s = r.GetStream(3);
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            img = ms.ToArray();
        }

        return new MasterCharacter(
            r.GetString(0), r.GetString(1), r.GetString(2), img,
            r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8));
    }

    public void Dispose() => _conn.Dispose();
}
