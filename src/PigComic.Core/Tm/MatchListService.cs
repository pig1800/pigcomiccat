using PigComic.Core.Tb;

namespace PigComic.Core.Tm;

/// <summary>One combined results-box row (SPEC §9).</summary>
public sealed class MatchListItem
{
    public required int Number { get; init; }               // 1..9 badge
    public required MatchItemKind Kind { get; init; }
    public required string DisplayScore { get; init; }      // "100%"/"103%" or "" for TB
    public required int Score { get; init; }                // 0 for TB rows
    public string? SourceTerm { get; init; }                // TB: source_term
    public string? TargetTerm { get; init; }                // TB: target_term
    public TmMatch? Tm { get; init; }
    public TbTerm? Tb { get; init; }

    /// <summary>TB forbidden entries are never inserted via Ctrl+N (SPEC §8.3).</summary>
    public bool IsInsertable => Tb is null || !Tb.Forbidden;
}

public enum MatchItemKind
{
    Tm,
    Tb,
}

/// <summary>
/// SPEC §9 combined TM/TB results list: TM matches (highest score first) then TB
/// hits, numbered 1..9 in display order; forbidden TB rows are flagged and never
/// insertable.
/// </summary>
public sealed class MatchListService
{
    private readonly TmQueryService _tm;
    private readonly TbStore _tb;
    public const int MaxRows = 9;

    public MatchListService(TmQueryService tm, TbStore tb)
    {
        _tm = tm;
        _tb = tb;
    }

    public async Task<IReadOnlyList<MatchListItem>> BuildAsync(
        string sourceText, string sourceLang, TmQueryContext context, CancellationToken ct)
    {
        var items = new List<MatchListItem>();
        var number = 0;

        foreach (var match in await _tm.QueryAsync(sourceText, context, ct))
        {
            if (number >= MaxRows)
            {
                break;
            }

            number++;
            items.Add(new MatchListItem
            {
                Number = number,
                Kind = MatchItemKind.Tm,
                Score = match.Score,
                DisplayScore = $"{match.Score}%",
                Tm = match,
            });
        }

        // TB hits (§8.3): terms hit by the bubble source, ordered by first
        // occurrence position, then longest source_norm first.
        var terms = _tb.All()
            .Where(t => t.SourceTerm.Length > 0 &&
                        TermHitTester.ContainsTerm(sourceText, t.SourceTerm, sourceLang))
            .OrderBy(t => FirstOccurrence(sourceText, t.SourceTerm, sourceLang))
            .ThenByDescending(t => t.SourceNorm.Length)
            .ToList();

        foreach (var term in terms)
        {
            if (number >= MaxRows)
            {
                break;
            }

            number++;
            items.Add(new MatchListItem
            {
                Number = number,
                Kind = MatchItemKind.Tb,
                DisplayScore = "",
                Score = 0,
                SourceTerm = term.SourceTerm,
                TargetTerm = term.TargetTerm,
                Tb = term,
            });
        }

        return items;
    }

    private static int FirstOccurrence(string text, string term, string lang)
    {
        var normText = Normalizer.Normalize(text, lang);
        var normTerm = Normalizer.Normalize(term, lang);
        return normTerm.Length > 0 ? normText.IndexOf(normTerm, StringComparison.Ordinal) : int.MaxValue;
    }
}