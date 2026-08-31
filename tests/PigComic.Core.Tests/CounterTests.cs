using PigComic.Core.Counting;
using Xunit;

namespace PigComic.Core.Tests;

/// <summary>SPEC §11.1 — the full normative counter table plus the MemoQ &lt;= MSWord guarantee.</summary>
public class CounterTests
{
    private readonly LanMangaCounter _counter = new();

    [Theory]
    [InlineData("Hello world", 2, 2)]     // two ASCII runs → 一一
    [InlineData("こんにちは。", 0, 6)]     // kana+punct: no ideographs
    [InlineData("第100話", 3, 3)]         // 100 → 一
    [InlineData("勇者–魔王", 4, 4)]       // en dash excluded from MSWord; 4 ideographs
    [InlineData("気持ち\nいい", 2, 6)]     // \n counts in MSWord, not MemoQ
    [InlineData("𠮷野家", 3, 4)]          // 𠮷 = 1 code point but 2 UTF-16 units
    [InlineData("「はい」", 0, 4)]         // CJK punctuation counts in MSWord only
    [InlineData("A B C", 3, 3)]           // three runs
    [InlineData("　全角", 2, 3)]           // U+3000 not stripped
    [InlineData("don't stop", 2, 2)]      // apostrophe inside run
    public void Section11_1_Row(string input, int memoQ, int msWord)
    {
        var result = _counter.Count(input);
        Assert.Equal(memoQ, result.MemoQ);
        Assert.Equal(msWord, result.MSWord);
    }

    [Fact]
    public void MemoQ_IsNeverGreaterThan_MsWord()
    {
        string[] samples =
        [
            "Hello world",
            "こんにちは。",
            "第100話",
            "勇者–魔王",
            "気持ち\nいい",
            "𠮷野家",
            "「はい」",
            "A B C",
            "　全角",
            "don't stop",
            "",
            "——",
            "…の…",
        ];

        foreach (var s in samples)
        {
            var result = _counter.Count(s);
            Assert.True(result.MemoQ <= result.MSWord, $"MemoQ ({result.MemoQ}) > MSWord ({result.MSWord}) for {s}");
        }
    }

    [Fact]
    public void EmptyText_CountsZero()
    {
        var result = _counter.Count("");
        Assert.Equal(0, result.MemoQ);
        Assert.Equal(0, result.MSWord);
    }

    [Fact]
    public void Interface_Instance_And_Static_Methods_Agree()
    {
        var text = "第100話–𠮷野家";
        var viaInstance = _counter.Count(text);
        Assert.Equal(LanMangaCounter.MemoQCount(text), viaInstance.MemoQ);
        Assert.Equal(LanMangaCounter.MSWordCount(text), viaInstance.MSWord);
    }
}