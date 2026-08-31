using System.Text.RegularExpressions;

namespace PigComic.Core.Counting;

/// <summary>
/// SPEC §11 — a faithful port of LanMangaCount's algorithm including its quirks
/// (D-14): only U+0020 is stripped, en/em dashes are subtracted from MSWord only,
/// each run of printable ASCII (U+0021..U+007E) collapses to a single 一 which
/// then counts +1 toward MemoQ, MemoQ counts UTF-32 code points while MSWord
/// counts UTF-16 units.
/// </summary>
public sealed class LanMangaCounter : ICounter
{
    private static readonly Regex AsciiRun = new("[!-~]+", RegexOptions.Compiled);

    public CountResult Count(string text) => new(MemoQCount(text), MSWordCount(text));

    /// <summary>Code points of the preprocessed text inside the CJK ideograph blocks.</summary>
    public static int MemoQCount(string text)
    {
        var s = Preprocess(text);
        var n = 0;
        foreach (var rune in s.EnumerateRunes())
        {
            if (IsIdeograph(rune.Value))
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>UTF-16 length of the preprocessed text minus U+2013/U+2014 dashes.</summary>
    public static int MSWordCount(string text)
    {
        var s = Preprocess(text);
        var n = s.Length;
        foreach (var ch in s)
        {
            if (ch is '\u2013' or '\u2014')
            {
                n--;
            }
        }

        return n;
    }

    private static string Preprocess(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "";
        }

        s = AsciiRun.Replace(s, "一");
        return s.Replace(" ", "");
    }

    private static bool IsIdeograph(int cp)
        => cp is >= 0x4E00 and <= 0x9FFF
            or >= 0x3400 and <= 0x4DBF
            or >= 0xF900 and <= 0xFAFF
            or >= 0x20000 and <= 0x2A6DF
            or >= 0x2A700 and <= 0x2B73F
            or >= 0x2B740 and <= 0x2B81F
            or >= 0x2B820 and <= 0x2CEAF
            or >= 0x2CEB0 and <= 0x2EBEF
            or >= 0x2F800 and <= 0x2FA1F
            or >= 0x30000 and <= 0x3134F;
}