using PigComic.Core.Tm;

namespace PigComic.Core.Tb;

/// <summary>
/// SPEC §8.2 term hit test. ja/zh: substring containment of normalized forms;
/// other lang classes: whole-token match — the term token sequence appears at
/// token boundaries of the text's token list.
/// </summary>
public static class TermHitTester
{
    public static bool ContainsTerm(string text, string term, string lang)
    {
        var nText = Normalizer.Normalize(text, lang);
        var nTerm = Normalizer.Normalize(term, lang);
        if (nTerm.Length == 0)
        {
            return false;
        }

        var cls = LangClass.Get(lang);
        if (cls is "ja" or "zh")
        {
            return nText.Contains(nTerm, StringComparison.Ordinal);
        }

        // Token-boundary match: term tokens must appear as a contiguous run at
        // token boundaries of the text's token list (tok borders: punctuation
        // attached to a token does not break the boundary).
        var textTokens = Tokenize(nText);
        var termTokens = Tokenize(nTerm);
        if (termTokens.Length == 0)
        {
            return false;
        }

        for (var i = 0; i + termTokens.Length <= textTokens.Length; i++)
        {
            var matches = true;
            for (var j = 0; j < termTokens.Length; j++)
            {
                if (!string.Equals(textTokens[i + j], termTokens[j], StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static readonly char[] TokenPunct =
    [
        '!', '.', ',', '?', ':', ';', '"', '\'', '(', ')', '[', ']', '{', '}',
        '「', '」', '『', '』', '、', '。', '！', '？', '，', '．', '·', '…',
    ];

    private static string[] Tokenize(string text)
        => text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim(TokenPunct))
            .Where(t => t.Length > 0)
            .ToArray();
}