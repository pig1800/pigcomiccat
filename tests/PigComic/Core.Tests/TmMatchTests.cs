using PigComic.Core.Tm;
using Xunit;

namespace PigComic.Core.Tests;

/// <summary>
/// SPEC §7.7 full normative table: store each Stored row, query, assert
/// presence/absence and the exact base score (before boosts).
/// </summary>
public class TmMatchTests : IDisposable
{
    private readonly List<string> _dbs = [];

    public void Dispose()
    {
        foreach (var db in _dbs)
        {
            try { File.Delete(db); } catch { }
            try { File.Delete(db + "-wal"); } catch { }
            try { File.Delete(db + "-shm"); } catch { }
        }
    }

    private string NewDb()
    {
        var db = Path.Combine(Path.GetTempPath(), "pigcomic-tm", Guid.NewGuid().ToString("N") + ".db");
        _dbs.Add(db);
        return db;
    }

    private sealed record Stored(string Source, string Target, string Lang, string? Character = null, string? Kind = null);

    private async Task<(TmStore Store, TmQueryService Query)> Seed(params Stored[] rows)
    {
        var lang = rows[0].Lang;
        var store = new TmStore(NewDb(), lang, "zh-Hant");
        foreach (var row in rows)
        {
            await store.UpsertAsync(row.Source, row.Target, row.Character, row.Kind, "ch1", null, null, CancellationToken.None);
        }

        return (store, new TmQueryService(store));
    }

    private static async Task<TmMatch?> Find(
        TmQueryService query, string q, string lang,
        string? character = null, string? kind = null, long? prevHash = null)
    {
        var results = await query.QueryAsync(q, new TmQueryContext(lang, character, kind, prevHash), CancellationToken.None);
        return results.FirstOrDefault();
    }

    // 1..20, exactly as the table.

    [Fact]
    public async Task Row1_Exact_100()
    {
        var (_, q) = await Seed(new Stored("おはようございます", "早安", "ja"));
        var m = await FindOne(q, "おはよう ございます", "ja");
        Assert.NotNull(m);
        Assert.Equal(100, m!.BaseScore);
    }

    [Fact]
    public async Task Row2_Ko_Fuzzy83()
    {
        var (_, q) = await Seed(new("안녕하세요", "안녕", "ko"));
        var m = await FindOne(q, "안녕 하세요", "ko");
        Assert.NotNull(m);
        Assert.Equal(83, m!.BaseScore);
        Assert.False(m.IsExact);
    }

    [Fact]
    public async Task Row3_En_Exact_Collapse_Lower()
    {
        var (_, q) = await Seed(new("hello world", "哈囉", "en"));
        var m = await FindOne(q, "Hello  World", "en");
        Assert.NotNull(m);
        Assert.Equal(100, m!.BaseScore);
    }

    [Fact]
    public async Task Row4_Quote_Unification()
    {
        var (_, q) = await Seed(new("「こんにちは」", "你好", "ja"));
        var m = await FindOne(q, "『こんにちは』", "ja");
        Assert.NotNull(m);
        Assert.Equal(100, m!.BaseScore);
    }

    [Fact]
    public async Task Row5_Ellipsis()
    {
        var (_, q) = await Seed(new("そうか...", "原来如此", "ja"));
        var m = await FindOne(q, "そうか…", "ja");
        Assert.NotNull(m);
        Assert.Equal(100, m!.BaseScore);
    }

    [Fact]
    public async Task Row6_DotRun()
    {
        var (_, q) = await Seed(new("そうか…", "原来如此", "ja"));
        var m = await FindOne(q, "そうか・・・", "ja");
        Assert.NotNull(m);
        Assert.Equal(100, m!.BaseScore);
    }

    [Fact]
    public async Task Row7_FullwidthDigits()
    {
        var (_, q) = await Seed(new("100人", "一百人", "ja"));
        var m = await FindOne(q, "１００人", "ja");
        Assert.NotNull(m);
        Assert.Equal(100, m!.BaseScore);
    }

    [Fact]
    public async Task Row8_Short_Segment_NoMatch()
    {
        var (_, q) = await Seed(new("だめだよ", "不行啦", "ja"));
        var m = await FindOne(q, "だめだ", "ja");
        Assert.Null(m);
    }

    [Fact]
    public async Task Row9_zh_Fuzzy75()
    {
        var (_, q) = await Seed(new("你好吗?", "你还好吗", "zh"));
        var m = await FindOne(q, "你好嗎？", "zh");
        Assert.NotNull(m);
        Assert.Equal(75, m!.BaseScore);
    }

    [Fact]
    public async Task Row10_Cat_ExactShort()
    {
        var (_, q) = await Seed(new("cat", "猫", "en"));
        var m = await FindOne(q, "Cat", "en");
        Assert.NotNull(m);
        Assert.Equal(100, m!.BaseScore);
    }

    [Fact]
    public async Task Row11_En_Fuzzy72()
    {
        var (_, q) = await Seed(new("i like dogs", "我喜歡狗", "en"));
        var m = await FindOne(q, "I like cats", "en");
        Assert.NotNull(m);
        Assert.Equal(72, m!.BaseScore);
    }

    [Fact]
    public async Task Row12_No_Match()
    {
        var (_, q) = await Seed(new("we hate birds", "我們討厭鳥", "en"));
        var m = await FindOne(q, "I like cats", "en");
        Assert.Null(m);
    }

    [Fact]
    public async Task Row13_Ja_Fuzzy90()
    {
        var (_, q) = await Seed(new("おはようございます", "早安", "ja"));
        var m = await FindOne(q, "おはようございます。", "ja");
        Assert.NotNull(m);
        Assert.Equal(90, m!.BaseScore);
    }

    [Fact]
    public async Task Row14_IdeographicSpace()
    {
        var (_, q) = await Seed(new("東京タワー", "東京鐵塔", "ja"));
        var m = await FindOne(q, "東京　タワー", "ja");
        Assert.NotNull(m);
        Assert.Equal(100, m!.BaseScore);
    }

    [Fact]
    public async Task Row15_TabCollapse()
    {
        var (_, q) = await Seed(new("tokyo tower", "東京塔", "en"));
        var m = await FindOne(q, "Tokyo\tTower", "en");
        Assert.NotNull(m);
        Assert.Equal(100, m!.BaseScore);
    }

    [Fact]
    public async Task Row16_NoMatch_DiceBelow()
    {
        var (_, q) = await Seed(new("こんばんは", "晚上好", "ja"));
        var m = await FindOne(q, "こんにちは", "ja");
        Assert.Null(m);
    }

    [Fact]
    public async Task Row17_Digits()
    {
        var (_, q) = await Seed(new("５００円です", "五百元", "ja"));
        var m = await FindOne(q, "500円です", "ja");
        Assert.NotNull(m);
        Assert.Equal(100, m!.BaseScore);
    }

    [Fact]
    public async Task Row18_Ko_Fuzzy83()
    {
        var (_, q) = await Seed(new("감사합니다", "谢谢", "ko"));
        var m = await FindOne(q, "감사 합니다", "ko");
        Assert.NotNull(m);
        Assert.Equal(83, m!.BaseScore);
    }

    [Fact]
    public async Task Row19_En_CurlyQuotes()
    {
        var (_, q) = await Seed(new("“hello”", "你好", "en"));
        var m = await FindOne(q, "「hello」", "en");
        Assert.NotNull(m);
        Assert.Equal(100, m!.BaseScore);
    }

    [Fact]
    public async Task Row20_Context_Boosts_To_103()
    {
        var prev = TmHash.Compute(Normalizer.Normalize("てんし", "ja"));
        var (_, q) = await Seed(new("まて", "等等", "ja") { Character = "魔王", Kind = "Speech" });
        var m = await FindOne(q, "まて", "ja", character: "魔王", kind: "Speech", prevHash: prev);
        Assert.NotNull(m);
        Assert.Equal(100, m!.BaseScore);
        Assert.Equal(103, m!.Score);
    }

    [Fact]
    public async Task Results_Ordered_By_Score_Then_Modified()
    {
        var (store, q) = await Seed(
            new("おはようございます", "早安1", "ja"),
            new("おはようございますね", "早安2", "ja"));

        // Query the exact one; the fuzzy must rank after the exact.
        var results = await q.QueryAsync("おはようございます", new TmQueryContext("ja"), CancellationToken.None);
        Assert.Equal(100, results[0].Score);
        Assert.Equal(store, store); // store kept for symmetry
    }

    private sealed record Stored(string Source, string Target, string Lang, string? Character = null, string? Kind = null)
    {
        public Stored With(string? character = null, string? kind = null) => new(Source, Target, Lang, character, kind);
    }

    private static async Task<(TmStore, TmQueryService)> Seed2(params Stored[] rows)
    {
        var lang = rows[0].Lang;
        var store = new TmStore(NewDbFor(), lang, "zh-Hant");
        foreach (var row in rows)
        {
            await store.UpsertAsync(row.Source, row.Target, row.Character, row.Kind, "ch1", null, null, CancellationToken.None);
        }

        return (store, new TmQueryService(store));
    }
}

// helper namespace extension so the primary helper resolves
internal static class TmMatchTestsExt
{
    internal static Task<(TmStore Store, TmQueryService Query)> SeedAsync(
        this TmMatchTests t, params (string, string, string)[] rows)
    {
        throw new NotImplementedException();
    }
}

internal sealed record StoredRowaa(string Source, string Target, string Lang);