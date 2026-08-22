using PigComic.Core.Tm;
using Xunit;

namespace PigComic.Core.Tests;

/// <summary>SPEC §7.2 steps + §7.7 rows 1–19 normalized-equality, plus §7.3/§7.4.</summary>
public class NormalizerTests
{
    // ---------------------------------------------------------------- §7.2 steps

    [Theory]
    [InlineData("１００人", "ja", "100人")]       // NFKC fullwidth digits
    [InlineData("　全角縦", "ja", "全角縦")]       // U+3000 stripped for ja
    [InlineData("A  B", "en", "a b")]             // collapse + lowercase
    [InlineData("そうか・・・", "ja", "そうか…")]   // ellipsis run from NB: NFKC makes ・・・ a ". . ." ->
                                                         // step 1 turns … into "..."? (see below)
    [InlineData("“x”", "en", "「x」")]             // curly quotes to corner brackets
    [InlineData("『こんにちは』", "ja", "「こんにちは」")]
    [InlineData("‘a’", "ko", "「a」")]
    [InlineData("hello  world", "en", "hello world")]
    [InlineData("  trim me  ", "en", "trim me")]
    [InlineData("so　ka", "ja", "soka")]           // U+3000 removed for ja
    public void Normalize_Steps(string input, string lang, string expected)
    {
        Assert.Equal(expected, Normalizer.Normalize(input, lang));
    }

    // The spec's own step examples.
    [Fact]
    public void NormSteps_Explicit()
    {
        Assert.Equal("100人", Normalizer.Normalize("１００人", "ja"));
        Assert.Equal("a b", Normalizer.Normalize("A  B", "en"));
        Assert.Equal("そうか…", Normalizer.Normalize("そうか・・・", "ja"));
        Assert.Equal("「x」", Normalizer.Normalize("“x”", "en"));
    }

    // ---------------------------------------------------------------- §7.7 rows 1..19 (normalized equality part)

    private static void AssertNormEqual(string stored, string query, string lang)
        => Assert.Equal(Normalizer.Normalize(stored, lang), Normalizer.Normalize(query, lang));

    private static void AssertNormNotEqual(string stored, string query, string lang)
        => Assert.NotEqual(Normalizer.Normalize(stored, lang), Normalizer.Normalize(query, lang));

    [Fact]
    public void Table_Row1_WhitespaceRemoval_Ja() => AssertNormEqual("おはようございます", "おはよう ございます", "ja");

    [Fact]
    public void Table_Row2_Ko_Space_Collapse_Not_Exact_Target()
        // Row 2 is NOT an exact match (dist 1/len 6 -> fuzzy 83%) — the scores are
        // verified in TmMatchTests; here we assert the normalized forms DIFFER
        // (space remains) which is why it is fuzzy, not exact.
        => AssertNormNotEqual("안녕하세요", "안녕 하세요", "ko");

    [Fact]
    public void Table_Row3_En_Collapse_Lower() => AssertNormEqual("hello world", "Hello  World", "en");

    [Fact]
    public void Table_Row4_Quote_Unification_Ja() => AssertNormEqual("「こんにちは」", "『こんにちは』", "ja");

    [Fact]
    public void Table_Row5_Ellipsis_Nfkc() => AssertNormEqual("そうか...", "そうか…", "ja");

    [Fact]
    public void Table_Row6_MiddleDot_Run() => AssertNormEqual("そうか…", "そうか・・・", "ja");

    [Fact]
    public void Table_Row7_Fullwidth_Digits() => AssertNormEqual("100人", "１００人", "ja");

    [Fact]
    public void Table_Row8_Substring() => AssertNormNotEqual("だめだよ", "だめだ", "ja"); // no match: not exact

    [Fact]
    public void Table_Row9_Zh_Fuzzy() => AssertNormNotEqual("你好吗?", "你好嗎？", "zh"); // 嗎≠吗

    [Fact]
    public void Table_Row10_Cat_Case() => AssertNormEqual("cat", "Cat", "en");

    [Fact]
    public void Table_Row11_En_Tokens_Differ_At_One_Word()
        // dogs vs cats: normalized forms differ in one token → fuzzy distance 3/11.
        => AssertNormNotEqual("i like dogs", "I like cats", "en");

    [Fact]
    public void Table_Row13_Punct_Suffix() => AssertNormNotEqual("おはようございます", "おはようございます。", "ja");

    [Fact]
    public void Table_Row14_Ideographic_Space() => AssertNormEqual("東京タワー", "東京　タワー", "ja");

    [Fact]
    public void Table_Row15_Tab_Collapse() => AssertNormEqual("tokyo tower", "Tokyo\tTower", "en");

    [Fact]
    public void Table_Row16_Different_Bigrams() => AssertNormNotEqual("こんばんは", "こんにちは", "ja");

    [Fact]
    public void Table_Row17_Digits_Nfkc() => AssertNormEqual("５００円です", "500円です", "ja");

    [Fact]
    public void Table_Row18_Korean_Space() => AssertNormNotEqual("감사합니다", "감사 합니다", "ko");

    [Fact]
    public void Table_Row19_Quotes_En() => AssertNormEqual("“hello”", "「hello」", "en");

    // ---------------------------------------------------------------- edge behavior

    [Fact]
    public void SingleDotLeftAlone() => Assert.Equal("a.b", Normalizer.Normalize("a.b", "en"));

    [Fact]
    public void SingleDotLeftAlone_Ja() => Assert.Equal("a.b", Normalizer.Normalize("a.b", "ja"));

    [Fact]
    public void LongDotRun_Collapses() => Assert.Equal("a…b", Normalizer.Normalize("a.....b", "en"));

    [Fact]
    public void Empty_Stays_Empty() => Assert.Equal("", Normalizer.Normalize("   ", "en"));

    [Fact]
    public void NormLength_Counts_Runes() => Assert.Equal(3, Normalizer.NormLength("𠮷野家")); // 𠮷 is one rune (2 UTF-16 units)

    [Fact]
    public void LangClass_Primary_Subtag() {
        Assert.Equal("zh", LangClass.Get("zh-Hant"));
        Assert.Equal("ja", LangClass.Get("ja"));
        Assert.Equal("ko", LangClass.Get("KO"));
    }
}

public class TmHashTests
{
    [Fact]
    public void Hash_Stable_And_Different()
    {
        var h1 = TmHash.Compute("おはようございます");
        var h2 = TmHash.Compute("おはようございます");
        var h3 = TmHash.Compute("おはようございまし");
        Assert.Equal(h1, h2);
        Assert.NotEqual(h1, h3);
    }
}

public class GramExtractorTests
{
    [Fact]
    public void Ja_Bigrams_Of_Konnichiwa() => Assert.Equal(4,
        GramExtractor.Extract(Normalizer.Normalize("こんにちは", "ja"), "ja").Count);

    [Fact]
    public void Ja_Single_Char_Is_Own_Gram() => Assert.Equal(["x"],
        GramExtractor.Extract(Normalizer.Normalize("x", "ja"), "ja"));

    [Fact]
    public void En_Tokens() => Assert.Equal(["cats", "i", "i like cats", "like"],
        GramExtractor.Extract(Normalizer.Normalize("i like cats", "en"), "en").OrderBy(t => t).ToArray());

    [Fact]
    public void Distinct_Bigrams_Only() => Assert.Equal(1,
        GramExtractor.Extract(Normalizer.Normalize("あああ", "ja"), "ja").Count); // ああ occurs once distinctly
}