using PigComic.Core.Tm;
using Xunit;

namespace PigComic.Core.Tests;

/// <summary>SPEC §7.1 / PLAN M3.3 acceptance.</summary>
public class TmStoreTests : IDisposable
{
    private readonly string _db;

    public TmStoreTests() => _db = Path.Combine(Path.GetTempPath(), "pigcomic-tm", Guid.NewGuid().ToString("N") + ".db");

    public void Dispose()
    {
        try
        {
            if (File.Exists(_db)) File.Delete(_db);
            if (File.Exists(_db + "-wal")) File.Delete(_db + "-wal");
            if (File.Exists(_db + "-shm")) File.Delete(_db + "-shm");
            var dir = Path.GetDirectoryName(_db)!;
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
            }
        }
        catch
        {
            // best effort
        }
    }

    [Fact]
    public async Task Upsert_Same_Source_Character_Newest_Wins()
    {
        using var store = new TmStore(_db, "ja", "zh-Hant");
        var first = await store.UpsertAsync("こんにちは", "你好", "ピッグ", null, null, null, null, CancellationToken.None);
        var second = await store.UpsertAsync("こんにちは", "您好", "ピッグ", null, null, null, null, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(1, store.CountEntries());
        Assert.Equal("您好", store.GetById(second!.Id)!.TargetRaw);
        Assert.Equal(first!.Id, second.Id);
    }

    [Fact]
    public async Task Same_Source_Different_Character_Is_Separate_Entry()
    {
        using var store = new TmStore(_db, "ja", "zh-Hant");
        await store.UpsertAsync("こんにちは", "你好", "ピッグ", null, null, null, null, CancellationToken.None);
        await store.UpsertAsync("こんにちは", "您好", "魔王", null, null, null, null, CancellationToken.None);
        Assert.Equal(2, store.CountEntries());
    }

    [Fact]
    public async Task Empty_Target_Writes_Nothing()
    {
        using var store = new TmStore(_db, "ja", "zh-Hant");
        var r = await store.UpsertAsync("こんにちは", "  ", "ピッグ", null, null, null, null, CancellationToken.None);
        Assert.Null(r);
        Assert.Equal(0, store.CountEntries());
    }

    [Fact]
    public async Task Empty_Normalized_Source_Writes_Nothing()
    {
        using var store = new TmStore(_db, "ja", "zh-Hant");
        var r = await store.UpsertAsync("　 ", "你好", "ピッグ", null, null, null, null, CancellationToken.None);
        Assert.Null(r);
        Assert.Equal(0, store.CountEntries());
    }

    [Fact]
    public async Task Grams_Rows_Per_Entry()
    {
        using var store = new TmStore(_db, "ja", "zh-Hant");
        await store.UpsertAsync("こんにちは", "你好", null, null, null, null, null, CancellationToken.None);
        Assert.True(store.CountGrams() >= 4); // 4 distinct bigrams

        using (var conn = store.Connection)
        {
            using var list = conn.CreateCommand();
            list.CommandText = "SELECT DISTINCT gram FROM grams ORDER BY gram;";
            using var reader = list.ExecuteReader();
            var grams = new List<string>();
            while (reader.Read())
            {
                grams.Add(reader.GetString(0));
            }

            Assert.Contains("こん", grams);
            Assert.Contains("んに", grams);
            Assert.Contains("にち", grams);
            Assert.Contains("ちは", grams);
        }
    }

    [Fact]
    public async Task RebuildGrams_After_Wipe_Restores_Identical_Set()
    {
        using var store = new TmStore(_db, "ja", "zh-Hant");
        await store.UpsertAsync("こんにちは", "你好", null, null, null, null, null, CancellationToken.None);
        await store.UpsertAsync("おはよう", "早上好", null, null, null, null, null, CancellationToken.None);

        long before = store.CountGrams();
        using (var w = store.Connection.CreateCommand())
        {
            w.CommandText = "DELETE FROM grams;";
            w.ExecuteNonQuery();
        }

        await store.RebuildGramsAsync(CancellationToken.None);
        Assert.Equal(before, store.CountGrams());
    }

    [Fact]
    public void Mismatched_Language_Pair_Throws()
    {
        using var store = new TmStore(_db, "ja", "zh-Hant");
        Assert.Throws<InvalidOperationException>(() => new TmStore(_db, "ja", "ko"));
        Assert.Throws<InvalidOperationException>(() => new TmStore(_db, "en", "zh-Hant"));
    }

    [Fact]
    public async Task Delete_Removes_Entry_And_Grams()
    {
        using var store = new TmStore(_db, "ja", "zh-Hant");
        var e = await store.UpsertAsync("こんにちは", "你好", null, null, null, null, null, CancellationToken.None);
        Assert.True(await store.DeleteAsync(e!.Id, CancellationToken.None));
        Assert.Equal(0, store.CountEntries());
        Assert.Equal(0, store.CountGrams());
    }

    [Fact]
    public void OpenExisting_On_Missing_File_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            TmStore.OpenExisting(_db + "-nope", "ja", "zh-Hant"));
    }
}