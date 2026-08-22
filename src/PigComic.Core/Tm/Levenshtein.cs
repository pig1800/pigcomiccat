namespace PigComic.Core.Tm;

/// <summary>
/// Code-point Levenshtein distance (SPEC §7.5; banded early-exit optional).
/// </summary>
public static class Levenshtein
{
    public static int Distance(string a, string b)
    {
        if (a == b)
        {
            return 0;
        }

        var ar = a.EnumerateRunes().Select(r => r.ToString()).ToArray();
        var br = b.EnumerateRunes().Select(r => r.ToString()).ToArray();

        if (ar.Length == 0)
        {
            return br.Length;
        }

        if (br.Length == 0)
        {
            return ar.Length;
        }

        var prev = new int[br.Length + 1];
        var cur = new int[br.Length + 1];
        for (var j = 0; j <= br.Length; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= ar.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= br.Length; j++)
            {
                var cost = ar[i - 1] == br[j - 1] ? 0 : 1;
                cur[j] = Math.Min(
                    Math.Min(prev[j] + 1, cur[j - 1] + 1),
                    prev[j - 1] + cost);
            }

            (prev, cur) = (cur, prev);
        }

        return prev[br.Length];
    }
}