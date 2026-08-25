namespace PigComic.App.ViewModels;

/// <summary>One diff run for the source-diff underline (SPEC §9: stored source code points not in the LCS with the query are underlined).</summary>
public sealed record DiffRun(string Text, bool Underline);

/// <summary>
/// Per-code-point LCS diff between a stored TM source and the current query.
/// Underlines the stored code points that are NOT part of the LCS (added/edited
/// in the current source) — memoQ-style edit markers.
/// </summary>
public static class SourceDiff
{
    public static IReadOnlyList<DiffRun> Build(string stored, string query)
    {
        var s = stored.EnumerateRunes().Select(r => r.ToString()).ToArray();
        var q = query.EnumerateRunes().Select(r => r.ToString()).ToArray();
        if (s.Length == 0)
        {
            return [new DiffRun("", false)];
        }

        if (q.Length == 0)
        {
            return [new DiffRun(stored, false)];
        }

        // LCS table.
        var dp = new int[s.Length + 1, q.Length + 1];
        for (var i = s.Length - 1; i >= 0; i--)
        {
            for (var j = q.Length - 1; j >= 0; j--)
            {
                dp[i, j] = string.Equals(s[i], q[j], StringComparison.Ordinal)
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        var inLcs = new bool[s.Length];
        var (ci, cj) = (0, 0);
        while (ci < s.Length && cj < q.Length)
        {
            if (string.Equals(s[ci], q[cj], StringComparison.Ordinal))
            {
                inLcs[ci] = true;
                ci++;
                cj++;
            }
            else if (dp[ci + 1, cj] >= dp[ci, cj + 1])
            {
                ci++;
            }
            else
            {
                cj++;
            }
        }

        var runs = new List<DiffRun>();
        var text = new System.Text.StringBuilder();
        var underline = inLcs[0];
        for (var k = 0; k < s.Length; k++)
        {
            if (inLcs[k] != underline)
            {
                runs.Add(new DiffRun(text.ToString(), underline));
                text.Clear();
                underline = inLcs[k];
            }

            text.Append(s[k]);
        }

        runs.Add(new DiffRun(text.ToString(), underline));
        return runs;
    }
}