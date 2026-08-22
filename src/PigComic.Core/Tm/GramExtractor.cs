using System.Text;

namespace PigComic.Core.Tm;

/// <summary>
/// SPEC §7.4 retrieval grams for a NORMALIZED source string:
/// - ja/zh: distinct adjacent code-point bigrams; a single code point is its own gram.
/// - others: distinct whitespace-delimited tokens.
/// </summary>
public static class GramExtractor
{
    public static IReadOnlyList<string> Extract(string normalizedText, string sourceLang)
    {
        var cls = LangClass.Get(sourceLang);
        return cls is "ja" or "zh"
            ? Bigrams(normalizedText)
            : Tokens(normalizedText);
    }

    private static IReadOnlyList<string> Bigrams(string text)
    {
        var runes = new List<Rune>(text.EnumerateRunes());
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (runes.Count == 0)
        {
            return [];
        }

        if (runes.Count == 1)
        {
            set.Add(runes[0].ToString());
            return set.ToList();
        }

        for (var i = 0; i < runes.Count - 1; i++)
        {
            set.Add(runes[i].ToString() + runes[i + 1]);
        }

        return set.ToList();
    }

    private static IReadOnlyList<string> Tokens(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            set.Add(token);
        }

        // D-38: the full normalized string is indexed as one additional gram. This
        // is what makes §7.7 rows 2/18 (ko space-collapse fuzzies) retrievable —
        // their token sets share no token, yet the normative table expects a fuzzy
        // 83% — while row 12 (no shared token) stays unretrieved.
        if (text.Length >= 2)
        {
            set.Add(text);
        }

        return set.ToList();
    }
}