using PigComic.Core.Tm;
using Xunit;

namespace PigComic.Core.Tests;

/// <summary>
/// SPEC §7.7 full normative table: store each Stored row in a fresh TM, query,
/// assert presence/absence and the exact base score (before boosts).
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

    private sealed record Stored(
        string Source, string Target, string Lang,
        string? Character = null, string? Kind = null, long? PrevHash = null);

    private async Task<TmQueryService> Seed(params Stored[] rows)
    {
        var store = new TmStore(NewDb(), rows[0].Lang, "zh-Hant");
        foreach (var row in rows)
        {
            await store.UpsertAsync(row.Source, row.Target, row.Character, row.Kind, "ch1", null, row.PrevHash, CancellationToken.None);
        }

        return new TmQueryService(store);
    }

    private static Task<TmMatch?> FindOne(TmQueryService q, string query, string lang,
        string? character = null, string? kind = null, long? prevHash = null)
        => FindOneAsync(q, query, lang, character, kind, prevHash);

    private static async Task<TmMatch?> FindOneAsync(TmQueryService q, string query, string lang,
        string? character, string? kind, long? prevHash)
    {
        var results = await q.QueryAsync(query, new TmQueryContext(lang, character, kind, prevHash), CancellationToken.None);
        return results.FirstOrDefault();
    }

    // ---------------------------------------------------------------- rows 1-20

    [Fact]
    public async Task Row1_Ja_Exact_100() =>
        await Exact("おはようございます", "おはよう ございます", "ja");

    [Fact]
    public async Task Row2_Ko_Fuzzy_83()
    {
        var q = await Seed(new Stored("안녕하세요", "안녕", "ko"));
        var m = await FindOne(q, "안녕 하세요", "ko");
        Assert.NotNull(m);
        Assert.Equal(83, m!.BaseScore);
        Assert.False(m.IsExact);
    }

    [Fact]
    public async Task Row3_En_Exact_100() =>
        await Exact("hello world", "Hello  World", "en");

    [Fact]
    public async Task Row4_Ja_Quote_Unification() =>
        await Exact("「こんにちは」", "『こんにちは』", "ja");

    [Fact]
    public async Task Row5_Ja_Ellipsis() =>
        await Exact("そうか...", "そうか…", "ja");

    [Fact]
    public async Task Row6_Ja_MiddleDotRun() =>
        await Exact("そうか…", "そうか・・・", "ja");

    [Fact]
    public async Task Row7_Ja_FullwidthDigits() =>
        await Exact("100人", "１００人", "ja");

    [Fact]
    public async Task Row8_Short_Segment_NoMatch()
    {
        var q = await Seed(new Stored("だめだよ", "不行啦", "ja"));
        var m = await FindOne(q, "だめだ", "ja");
        Assert.Null(m);
    }

    [Fact]
    public async Task Row9_Zh_Fuzzy_75()
    {
        var q = await Seed(new Stored("你好吗?", "你还好吗", "zh"));
        var m = await FindOne(q, "你好嗎？", "zh");
        Assert.NotNull(m);
        Assert.Equal(75, m!.BaseScore);
    }

    [Fact]
    public async Task Row10_En_Cat_Exact() =>
        await Exact("cat", "Cat", "en");

    [Fact]
    public async Task Row11_En_Fuzzy_72()
    {
        var q = await Seed(new Stored("i like dogs", "我喜歡狗", "en"));
        var m = await FindOne(q, "I like cats", "en");
        Assert.NotNull(m);
        Assert.Equal(72, m!.BaseScore);
    }

    [Fact]
    public async Task Row12_En_NoMatch()
    {
        var q = await Seed(new Stored("we hate birds", "我們討厭鳥", "en"));
        var m = await FindOne(q, "I like cats", "en");
        Assert.Null(m);
    }

    [Fact]
    public async Task Row13_Ja_Fuzzy_90()
    {
        var q = await Seed(new Stored("おはようございます", "早安", "ja"));
        var m = await FindOne(q, "おはようございます。", "ja");
        Assert.NotNull(m);
        Assert.Equal(90, m!.BaseScore);
    }

    [Fact]
    public async Task Row14_Ja_IdeographicSpace() =>
        await Exact("東京タワー", "東京　タワー", "ja");

    [Fact]
    public async Task Row15_En_TabCollapse() =>
        await Exact("tokyo tower", "Tokyo\tTower", "en");

    [Fact]
    public async Task Row16_Ja_NoMatch_DiceBelow()
    {
        var q = await Seed(new Stored("こんばんは", "晚上好", "ja"));
        var m = await FindOne(q, "こんにちは", "ja");
        Assert.Null(m);
    }

    [Fact]
    public async Task Row17_Ja_DigitsNfkc() =>
        await Exact("５００円です", "500円です", "ja");

    [Fact]
    public async Task Row18_Ko_Fuzzy_83()
    {
        var q = await Seed(new Stored("감사합니다", "谢谢", "ko"));
        var m = await FindOne(q, "감사 합니다", "ko");
        Assert.NotNull(m);
        Assert.Equal(83, m!.BaseScore);
    }

    [Fact]
    public async Task Row19_En_CurlyQuotes() =>
        await Exact("“hello”", "「hello」", "en");

    [Fact]
    public async Task Row20_Context_Boosts_To_103()
    {
        var prev = TmHash.Compute(Normalizer.Normalize("てんし", "ja"));
        var q = await Seed(new Stored(
            "まて", "等等", "ja", Character: "魔王", Kind: "Speech", PrevHash: prev));
        var m = await FindOne(q, "まて", "ja", character: "魔王", kind: "Speech", prevHash: prev);
        Assert.NotNull(m);
        Assert.Equal(100, m!.BaseScore);
        Assert.Equal(103, m!.Score);
    }

    [Fact]
    public async Task Results_Ordered_By_Score_Then_Modified()
    {
        var q = await Seed(
            new Stored("おはようございます", "早安1", "ja"),
            new Stored("おはようございますね", "早安2", "ja"));
        var results = await q.QueryAsync("おはようございます", new TmQueryContext("ja"), CancellationToken.None);
        Assert.Equal(2, results.Count); // exact + fuzzy (97)
        Assert.Equal(100, results[0].Score);
        Assert.True(results[1].Score < 100);
    }

    // ---------------------------------------------------------------- helpers for rows

    private async Task Exact(string stored, string query, string lang)
    {
        var q = await Seed(new Stored(stored, "譯文", lang));
        var m = await FindOne(q, query, lang);
        Assert.NotNull(m);
        Assert.Equal(100, m!.BaseScore);
    }
}