using System.Globalization;
using System.Text;

namespace PigComic.Core.Tm;

/// <summary>BCP-47 primary subtag ("zh-Hant" → "zh"); classes ja/zh share CJK rules.</summary>
public static class LangClass
{
    public static string Get(string languageTag)
    {
        var tag = (languageTag ?? "").Trim();
        if (string.IsNullOrEmpty(tag))
        {
            return tag;
        }

        var dash = tag.IndexOf('-');
        return (dash < 0 ? tag : tag[..dash]).ToLowerInvariant();
    }
}

/// <summary>
/// SPEC §7.2 normalization — steps in this exact order: NFKC, whitespace by lan
/// g class, lowercase, quote unification, ellipsis unification.
/// </summary>
public static class Normalizer
{
    private static string Map(string text, string languageTag)
        => Normalize(text, languageTag);

    public static string Normalize(string text, string languageTag)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        // 1. NFKC (also converts full-width ASCII, U+3000→U+0020, …→..., ‥→..)
        var s = text.Normalize(NormalizationForm.FormKC);

        // 2. Whitespace by lang class.
        var cls = LangClass.Get(languageTag);
        if (cls is "ja" or "zh")
        {
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (!char.IsWhiteSpace(ch))
                {
                    sb.Append(ch);
                }
            }

            s = sb.ToString();
        }
        else
        {
            s = s.Trim();
            var collapsed = new StringBuilder(s.Length);
            var inRun = false;
            foreach (var ch in s)
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (inRun)
                    {
                        continue;
                    }

                    inRun = true;
                    collapsed.Append(' ');
                }
                else
                {
                    inRun = false;
                    collapsed.Append(ch);
                }
            }

            s = collapsed.ToString();
        }

        // 3. Lowercase.
        s = s.ToLowerInvariant();

        // 4. Quote unification (single-char mapping; straight quotes untouched).
        var q = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            q.Append(ch switch
            {
                '『' or '“' or '‘' => '「',
                '』' or '”' or '’' => '」',
                _ => ch,
            });
        }

        s = q.ToString();

        // 5. Ellipsis unification: maximal runs of ≥2 chars drawn from {., ・, …}
        // (post-NFKC they arrive as runs of '.') collapse to a single '…'.
        var outB = new StringBuilder(s.Length);
        var i = 0;
        while (i < s.Length)
        {
            var ch = s[i];
            if (ch is '.' or '・' or '…')
            {
                var start = i;
                while (i < s.Length && s[i] is '.' or '・' or '…')
                {
                    i++;
                }

                if (i - start >= 2)
                {
                    outB.Append('…');
                }
                else
                {
                    outB.Append(s[start]);
                }
            }
            else
            {
                outB.Append(ch);
                i++;
            }
        }

        return outB.ToString();
    }

    /// <summary>Number of Unicode code points (Rune count) of the normalized string.</summary>
    public static int NormLength(string normalizedText)
    {
        if (string.IsNullOrEmpty(normalizedText))
        {
            return 0;
        }

        var n = 0;
        foreach (var _ in normalizedText.EnumerateRunes())
        {
            n++;
        }

        return n;
    }
}