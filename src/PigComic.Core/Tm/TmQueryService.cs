using PigComic.Core.Data;

namespace PigComic.Core.Tm;

/// <summary>
/// SPEC §7.5 query algorithm (normative): normalization, hash, exact rows,
/// short-segment rule, gram retrieval (Dice ≥ 0.4, cap 50, exact always kept),
/// base = 100 or floor(100·(1−lev/maxLen)), floor 70, context boosts
/// (+character/+kind/+prev-hash, cap 103), sort, top 20.
///
/// Retrieval details (D-38): candidates = exact ∪ gram-Dice ≥ 0.4 ∪ substring
/// share (any ≥2-rune query gram occurring in an entry's source_norm, or an
/// entry gram in the query's norm) — over-retrieval is pruned by the base-70
/// floor; this is what makes §7.7 rows 2/9/18 retrievable (their gram Dice is
/// below 0.4) while rows 12/16 stay "no match" via the floor.
/// </summary>
public sealed class TmQueryService
{
    private readonly TmStore _store;
    public const int MaxResults = 20;
    public const int CandidateCap = 50;
    public const double DiceThreshold = 0.4;
    public const int BaseFloor = 70;
    public const int ScoreCap = 103;

    public TmQueryService(TmStore store) => _store = store;

    /// <summary>Queries the TM; empty normalized source yields no matches (§7.5).</summary>
    public Task<IReadOnlyList<TmMatch>> QueryAsync(string sourceText, TmQueryContext context, CancellationToken ct)
    {
        var norm = Normalizer.Normalize(sourceText, context.SourceLanguage);
        if (norm.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<TmMatch>>([]);
        }

        return Task.Run<IReadOnlyList<TmMatch>>(() =>
        {
            ct.ThrowIfCancellationRequested();
            var hash = TmHash.Compute(norm);
            var exact = FetchExact(hash, norm, ct);
            var candidates = new HashSet<long>(exact.Select(e => e.Id));

            if (Normalizer.NormLength(norm) > 3)
            {
                // (a) gram-Dice ≥ 0.4, (b) substring share — together ranked by dice.
                var grams = GramExtractor.Extract(norm, context.SourceLanguage);
                var retrieval = CandidateRetrieval(grams, norm, ct);
                foreach (var kv in retrieval.OrderByDescending(kv => kv.Value).Take(CandidateCap))
                {
                    candidates.Add(kv.Key);
                }
            }

            var results = new List<TmMatch>();
            foreach (var id in candidates)
            {
                var entry = _store.GetById(id);
                if (entry is null)
                {
                    continue;
                }

                int baseScore;
                if (exact.Any(e => e.Id == id))
                {
                    baseScore = 100;
                }
                else
                {
                    var max = Math.Max(Normalizer.NormLength(norm), Normalizer.NormLength(entry.SourceNorm));
                    if (max == 0)
                    {
                        continue;
                    }

                    baseScore = (int)Math.Floor(100.0 * (1 - (double)Levenshtein.Distance(norm, entry.SourceNorm) / max));
                }

                if (baseScore < BaseFloor)
                {
                    continue;
                }

                var boost = 0;
                if (entry.Character is not null && context.Character is not null &&
                    string.Equals(entry.Character, context.Character, StringComparison.Ordinal))
                {
                    boost++;
                }

                if (entry.Kind is not null && context.Kind is not null &&
                    string.Equals(entry.Kind, context.Kind, StringComparison.Ordinal))
                {
                    boost++;
                }

                if (entry.PrevHash is not null && entry.PrevHash == context.PrevSourceHash)
                {
                    boost++;
                }

                var score = Math.Min(baseScore + boost, ScoreCap);
                results.Add(new TmMatch(
                    entry.Id, score, baseScore,
                    entry.SourceRaw, entry.SourceNorm, entry.TargetRaw,
                    entry.Character, entry.Kind, entry.Chapter,
                    DateTime.TryParse(entry.ModifiedUtc, out var dt) ? dt : DateTime.MinValue));
            }

            return results
                .OrderByDescending(m => m.Score)
                .ThenByDescending(m => m.ModifiedUtc)
                .Take(MaxResults)
                .ToList();
        }, ct);
    }

    private List<TmEntry> FetchExact(long hash, string norm, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var cmd = _store.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM entries WHERE source_hash = $h AND source_norm = $n;";
        cmd.Parameters.AddWithValue("$h", hash);
        cmd.Parameters.AddWithValue("$n", norm);
        var rows = new List<TmEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(TmStore.ReadEntry(reader));
        }

        return rows;
    }

    /// <summary>
    /// Candidate retrieval: entryId → dice. Dice over distinct gram sets for
    /// gram-sharers (D-38). Entries that only share a ≥2-rune substring get dice 0
    /// but are retained (pruned later by the base floor); ordering prefers real
    /// dice so the 50-cap prefers strong candidates.
    /// </summary>
    private Dictionary<long, double> CandidateRetrieval(IReadOnlyList<string> queryGrams, string queryNorm, CancellationToken ct)
    {
        var entryGramCounts = new Dictionary<long, int>();
        var overlaps = new Dictionary<long, int>();
        var found = new HashSet<long>();

        // Gram-shared candidates.
        foreach (var chunk in queryGrams.Chunk(500))
        {
            using var cmd = _store.Connection.CreateCommand();
            var names = new List<string>();
            for (var i = 0; i < chunk.Length; i++)
            {
                var p = "$g" + i;
                cmd.Parameters.AddWithValue(p, chunk[i]);
                names.Add(p);
            }

            cmd.CommandText = "SELECT g.entry_id, e.source_norm FROM grams g JOIN entries e ON e.id = g.entry_id " +
                              "WHERE g.gram IN (" + string.Join(",", names) + ");";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                var id = reader.GetInt64(0);
                found.Add(id);
                if (!entryGramCounts.ContainsKey(id))
                {
                    entryGramCounts[id] = GramExtractor.Extract(reader.GetString(1), _store.SourceLanguage).Count;
                }

                overlaps.TryGetValue(id, out var o);
                overlaps[id] = o + 1;
            }
        }

        var dice = new Dictionary<long, double>();
        foreach (var id in found)
        {
            var ec = Math.Max(1, entryGramCounts[id]);
            dice[id] = 2.0 * overlaps[id] / (queryGrams.Count + ec);
        }

        // Substring-share candidates (D-38): any ≥2-rune query gram contained in an
        // entry's normalized source (or an entry gram in the query norm) — retained
        // as candidates; the base-70 floor prunes junk.
        var needles = queryGrams.Where(g => g.Length >= 2).Distinct();
        foreach (var n in needles)
        {
            ct.ThrowIfCancellationRequested();
            using var cmd = _store.Connection.CreateCommand();
            cmd.CommandText = "SELECT id FROM entries WHERE instr(source_norm, $n) > 0;";
            cmd.Parameters.AddWithValue("$n", n);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                dice.TryAdd(reader.GetInt64(0), 0.0);
            }
        }

        return dice;
    }
}