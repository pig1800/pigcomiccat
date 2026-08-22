using PigComic.Core.Tb;
using Xunit;

namespace PigComic.Core.Tests;

/// <summary>SPEC §8 / PLAN M3.5 acceptance.</summary>
public class TbTests : IDisposable
{
    private readonly string _db;

    public TbTests() => _db = Path.Combine(Path.GetTempPath(), "pigcomic-tb", Guid.NewGuid().ToString("N") + ".db");

    public void Dispose()
    {
        try { File.Delete(_db); } catch { }
        try { File.Delete(_db + "-wal"); } catch { }
        try { File.Delete(_db + "-shm"); } catch { }
    }

    [Fact]
    public void Ja_Substring_Hit()
    {
        Assert.True(TermHitTester.ContainsTerm("悟空だ!", "悟空", "ja"));
    }

    [Fact]
    public void En_Cat_Does_Not_Hit_Concatenate()
    {
        Assert.False(TermHitTester.ContainsTerm("concatenate", "cat", "en"));
    }

    [Fact]
    public void En_Cat_Hits_A_Cat()
    {
        Assert.True(TermHitTester.ContainsTerm("a cat!", "cat", "en"));
    }

    [Fact]
    public void En_Multiword_Term_Requires_Contiguous_Tokens()
    {
        Assert.True(TermHitTester.ContainsTerm("go to school", "to school", "en"));
        Assert.False(TermHitTester.ContainsTerm("school to go", "to school", "en"));
    }

    [Fact]
    public async Task Forbidden_Row_With_Empty_Source_Allowed()
    {
        using var store = new TbStore(_db, "ja", "zh-Hant");
        var term = await store.UpsertAsync("", "ゲス", forbidden: true, "", CancellationToken.None);
        Assert.True(term.Forbidden);
    }

    [Fact]
    public async Task NonForbidden_Empty_Target_Rejected()
    {
        using var store = new TbStore(_db, "ja", "zh-Hant");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.UpsertAsync("悟空", "", forbidden: false, "", CancellationToken.None));
    }

    [Fact]
    public async Task Synonyms_Share_Source_Term()
    {
        using var store = new TbStore(_db, "ja", "zh-Hant");
        await store.UpsertAsync("悟空", "孫悟空", false, "", CancellationToken.None);
        await store.UpsertAsync("悟空", "悟空", false, "", CancellationToken.None);
        Assert.Equal(2, store.All().Count);
    }

    [Fact]
    public void Mismatched_Language_Pair_Throws()
    {
        using (var store = new TbStore(_db, "ja", "zh-Hant")) { }
        Assert.Throws<InvalidOperationException>(() => new TbStore(_db, "ja", "ko"));
    }
}