using PigComic.Core.Domain;
using PigComic.Core.Qa;
using PigComic.Core.Tb;
using Xunit;

namespace PigComic.Core.Tests;

/// <summary>SPEC §12.1 — the tate-chū-yoko aware VisualLength table.</summary>
public class VisualLengthTests
{
    [Theory]
    [InlineData("100人", "ja", 2)]     // tcy run of 3
    [InlineData("1000人", "ja", 5)]     // run of 4 > 3 → 1 per digit
    [InlineData("3人と5人", "ja", 5)]   // two runs of 1
    [InlineData("こんにちは", "ja", 5)]
    [InlineData("100人", "zh-Hant", 4)] // horizontal: per code point
    [InlineData("第12話", "ja", 3)]
    public void Section12_1_Row(string line, string lang, int expected)
    {
        Assert.Equal(expected, VisualLength.Of(line, lang));
    }

    [Fact]
    public void Custom_TcyMaxDigitRun_Changes_Rule()
    {
        Assert.Equal(4, VisualLength.Of("1234", "ja"));
        Assert.Equal(1, VisualLength.Of("1234", "ja", 4));
    }

    [Fact]
    public void Empty_Line_Is_Zero() => Assert.Equal(0, VisualLength.Of("", "ja"));
}

/// <summary>SPEC §12 — every QA rule, positive and negative.</summary>
public sealed class QaRuleTests : IDisposable
{
    private readonly string _db;

    public QaRuleTests()
        => _db = Path.Combine(Path.GetTempPath(), "pigcomic-qa", Guid.NewGuid().ToString("N") + ".db");

    public void Dispose()
    {
        try { File.Delete(_db); } catch { }
        try { File.Delete(_db + "-wal"); } catch { }
        try { File.Delete(_db + "-shm"); } catch { }
    }

    private static Bubble MakeBubble(
        BubbleStatus status = BubbleStatus.Translated,
        string source = "",
        string target = "",
        BubbleKind kind = BubbleKind.Speech,
        int parts = 1)
    {
        var marker = new PixelPoint(10, 10);
        var partsList = Enumerable.Range(1, parts)
            .Select(p => new TargetPart(p, marker, p == 1 ? target : ""))
            .ToList();
        return new Bubble("b", 1, kind, status, null, marker, source, partsList);
    }

    private static void AssertOnly(
        IReadOnlyList<QaIssue> issues,
        string ruleId,
        QaSeverity severity = QaSeverity.Warning)
    {
        Assert.Contains(issues, i => i.RuleId == ruleId && i.Severity == severity);
        Assert.True(issues.All(i => i.RuleId == ruleId),
            $"Expected only {ruleId}, got {string.Join(", ", issues.Select(i => i.RuleId))}");
    }

    // ---------------------------------------------------------------- QA-EMPTY

    [Fact]
    public void Confirmed_With_Empty_Target_Errors()
        => AssertOnly(NewQaEngine().RunOnBubble(
            MakeBubble(BubbleStatus.Translated, "こんにちは", ""), "zh-CN", "ja"),
            QaEngine.Empty, QaSeverity.Error);

    [Fact]
    public void Draft_With_Empty_Target_Is_Not_Empty_Issue()
        => Assert.Empty(NewQaEngine().RunOnBubble(
            MakeBubble(BubbleStatus.Draft, "こんにちは", ""), "zh-CN", "ja"));

    [Fact]
    public void One_Part_Empty_While_Another_Is_Not_Errors()
        => AssertOnly(NewQaEngine().RunOnBubble(
            MakeBubble(BubbleStatus.Translated, "s", "abc", parts: 2), "zh-CN", "ja"),
            QaEngine.Empty, QaSeverity.Error);

    // ---------------------------------------------------------------- QA-SAME

    [Fact]
    public void Identical_Non_Exempt_Text_Warns()
        => AssertOnly(NewQaEngine().RunOnBubble(
            MakeBubble(source: "s", target: "s"), "en", "ja"), QaEngine.Same);

    [Fact]
    public void Exempt_Kind_Does_Not_Warn()
    {
        var issues = new QaEngine(new QaConfig(IdenticalExemptKinds: ["Sfx"])).RunOnBubble(
            MakeBubble(source: "boooom", target: "boooom", kind: BubbleKind.Sfx), "en", "ja");
        Assert.DoesNotContain(issues, i => i.RuleId == QaEngine.Same);
    }

    // ---------------------------------------------------------------- QA-TERM / QA-FORBID

    private async Task<(QaEngine engine, TbStore store)> EngineWithTbAsync(params (string src, string tgt, bool forbidden)[] terms)
    {
        var store = new TbStore(_db, "zh-CN", "ja");
        foreach (var (src, tgt, forbidden) in terms)
        {
            await store.UpsertAsync(src, tgt, forbidden, "", CancellationToken.None);
        }

        return (new QaEngine(new QaConfig(), store), store);
    }

    [Fact]
    public async Task Term_In_Source_And_Not_In_Target_Errors()
    {
        var (engine, store) = await EngineWithTbAsync(("悟空", "悟空", false));
        using var _ = store;
        AssertOnly(engine.RunOnBubble(
            MakeBubble(source: "悟空だ!", target: "オラ!"), "ja", "ja"),
            QaEngine.Term, QaSeverity.Error);
    }

    [Fact]
    public async Task Term_Translated_Is_Not_An_Issue()
    {
        var (engine, store) = await EngineWithTbAsync(("悟空", "悟空", false));
        using var _ = store;
        var issues = engine.RunOnBubble(
            MakeBubble(source: "悟空だ!", target: "悟空だ!"), "ja", "ja");
        Assert.DoesNotContain(issues, i => i.RuleId == QaEngine.Term);
    }

    [Fact]
    public async Task Forbidden_In_Target_Errors()
    {
        var (engine, store) = await EngineWithTbAsync(("", "ゲス", true));
        using var _ = store;
        AssertOnly(engine.RunOnBubble(
            MakeBubble(source: "s", target: "お前はゲスだ"), "zh-CN", "ja"),
            QaEngine.Forbid, QaSeverity.Error);
    }

    // ---------------------------------------------------------------- QA-LINELEN / QA-LINECOUNT / TRAILING / BRACKET

    [Fact]
    public void Line_Longer_Than_Max_Warns()
        => AssertOnly(new QaEngine(new QaConfig(MaxCharsPerLine: 2)).RunOnBubble(
            MakeBubble(target: "長いセリフ"), "zh-CN", "ja"),
            QaEngine.LineLen);

    [Fact]
    public void Too_Many_Lines_Warns()
        => AssertOnly(new QaEngine(new QaConfig(MaxLinesPerPart: 1)).RunOnBubble(
            MakeBubble(target: "a\nb"), "zh-CN", "ja"),
            QaEngine.LineCount);

    [Fact]
    public void Forbidden_Trailing_Char_Warns()
        => AssertOnly(new QaEngine(new QaConfig(ForbiddenTrailing: ["!"])).RunOnBubble(
            MakeBubble(target: "no!"), "zh-CN", "ja"),
            QaEngine.Trailing);

    [Fact]
    public void Unbalanced_Bracket_Errors()
        => AssertOnly(new QaEngine(new QaConfig()).RunOnBubble(
            MakeBubble(target: "「開かない"), "zh-CN", "ja"),
            QaEngine.Bracket, QaSeverity.Error);

    [Fact]
    public void Balanced_Bracket_Is_Fine()
    {
        var issues = new QaEngine(new QaConfig()).RunOnBubble(
            MakeBubble(target: "「合ってる」"), "zh-CN", "ja");
        Assert.DoesNotContain(issues, i => i.RuleId == QaEngine.Bracket);
    }

    // ---------------------------------------------------------------- chapter / project

    [Fact]
    public void Chapter_Run_Includes_Untranslated_Marker()
    {
        var chapter = new Chapter("t", "1", "zh-CN", "ja", [],
            [], [MakeBubble(BubbleStatus.Untranslated, "こんにちは", "")]);
        var issues = new QaEngine(new QaConfig()).RunOnChapter(chapter);
        Assert.Contains(issues, i => i.RuleId == QaEngine.Untranslated);
    }

    [Fact]
    public void Project_Run_Prefixes_Chapter_Title()
    {
        var chapter = new Chapter("My Chapter", "1", "zh-CN", "ja", [],
            [], [MakeBubble(BubbleStatus.Translated, "こんにちは", "")]);
        var issues = new QaEngine(new QaConfig()).RunOnProject([chapter]);
        Assert.Contains(issues, i => i.Message.StartsWith("[My Chapter]"));
    }

    private static QaEngine NewQaEngine() => new();
}
