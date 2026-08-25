using PigComic.Core.Tb;
using PigComic.Core.Tm;
using Xunit;

namespace PigComic.Core.Tests;

/// <summary>SPEC §9 / PLAN M3.6 acceptance.</summary>
public class MatchListTests : IDisposable
{
    private readonly string _tmDb;
    private readonly string _tbDb;

    public MatchListTests()
    {
        _tmDb = Path.Combine(Path.GetTempPath(), "pigcomic-tm", Guid.NewGuid().ToString("N") + ".db");
        _tbDb = Path.Combine(Path.GetTempPath(), "pigcomic-tb", Guid.NewGuid().ToString("N") + ".db");
    }

    public void Dispose()
    {
        foreach (var db in new[] { _tmDb, _tbDb })
        {
            try { File.Delete(db); } catch { }
            try { File.Delete(db + "-wal"); } catch { }
            try { File.Delete(db + "-shm"); } catch { }
        }
    }

    [Fact]
    public async Task Items_Numbered_Tm_First_Then_Tb()
    {
        // Two exact TM entries: same normalized source, different speakers — both
        // exact (100) for a query without character context → two TM rows.
        using var tmStore = new TmStore(_tmDb, "zh-CN", "ja");
        await tmStore.UpsertAsync("おはようございます。魔王はピッグだ。", "早安版甲", "甲", null, null, null, null, CancellationToken.None);
        await tmStore.UpsertAsync("おはようございます。魔王はピッグだ。", "早安版乙", "乙", null, null, null, null, CancellationToken.None);

        using var tbStore = new TbStore(_tbDb, "zh-CN", "ja");
        await tbStore.UpsertAsync("魔王", "魔王大人", false, "", CancellationToken.None);
        await tbStore.UpsertAsync("ピッグ", "小豬", false, "", CancellationToken.None);

        var service = new MatchListService(new TmQueryService(tmStore), tbStore);
        var items = await service.BuildAsync(
            "おはようございます。魔王はピッグだ。", "ja",
            new TmQueryContext("ja"), CancellationToken.None);

        // 2 TM matches (exact 100%), then 2 TB hits — numbered 1..4.
        Assert.Equal(4, items.Count);
        Assert.Equal(1, items[0].Number);
        Assert.Equal(2, items[1].Number);
        Assert.Equal(MatchItemKind.Tm, items[0].Kind);
        Assert.Equal(MatchItemKind.Tm, items[1].Kind);
        Assert.Equal(100, items[0].Score);
        Assert.Equal(3, items[2].Number);
        Assert.Equal(MatchItemKind.Tb, items[2].Kind);
        Assert.Equal("魔王", items[2].SourceTerm);
        Assert.Equal(4, items[3].Number);
        Assert.Equal(MatchItemKind.Tb, items[3].Kind);
        Assert.Equal("ピッグ", items[3].SourceTerm);

        Assert.True(items[0].Score >= items[1].Score);
        Assert.True(items[0].IsInsertable);
    }

    [Fact]
    public async Task Forbidden_Tb_Row_Not_Insertable()
    {
        using var tmStore = new TmStore(_tmDb, "zh-CN", "ja");
        using var tbStore = new TbStore(_tbDb, "zh-CN", "ja");
        await tbStore.UpsertAsync("ゲス", "下品", forbidden: true, "", CancellationToken.None);

        var service = new MatchListService(new TmQueryService(tmStore), tbStore);
        var items = await service.BuildAsync("ゲス野郎", "ja", new TmQueryContext("ja"), CancellationToken.None);

        var forbidden = Assert.Single(items.Where(i => i.Kind == MatchItemKind.Tb));
        Assert.False(forbidden.IsInsertable);
    }

    [Fact]
    public async Task Cap_At_Nine_Rows()
    {
        using var tmStore = new TmStore(_tmDb, "zh-CN", "ja");
        using var tbStore = new TbStore(_tbDb, "zh-CN", "ja");
        for (var i = 0; i < 12; i++)
        {
            await tbStore.UpsertAsync($"語{i}", $"訳{i}", false, "", CancellationToken.None);
        }

        var service = new MatchListService(new TmQueryService(tmStore), tbStore);
        var items = await service.BuildAsync("語0語1語2語3語4語5語6語7語8語9語10語11", "ja",
            new TmQueryContext("ja"), CancellationToken.None);
        Assert.True(items.Count <= MatchListService.MaxRows);
        Assert.Equal(1, items[0].Number);
        Assert.Equal(items.Count, items[^1].Number);
    }
}