using PigComic.Core.Tm;

namespace PigComic.App.ViewModels;

/// <summary>One combined results-box row presentation (SPEC §9).</summary>
public sealed class MatchRowViewModel
{
    public MatchRowViewModel(MatchListItem item, string querySource)
    {
        Item = item;
        Number = item.Number;
        ScoreText = item.Kind == MatchItemKind.Tm ? item.DisplayScore : "";
        IsTm = item.Kind == MatchItemKind.Tm;
        IsForbidden = !item.IsInsertable;
        TooltipText = item.Tb?.Notes ?? "";

        if (item.Tm is { } tm)
        {
            // D-61: TM stores \t-separated segments; display replaces \t with ⏎ for readability.
            TargetText = tm.TargetRaw.Replace("\t", "⏎").Replace("\n", "⏎");
            DiffRuns = SourceDiff.Build(tm.SourceRaw, querySource);
            MetaText = string.Join(" · ",
                new[]
                {
                    string.IsNullOrEmpty(tm.Chapter) ? null : $"ch.{tm.Chapter}",
                    string.IsNullOrEmpty(tm.Character) ? null : tm.Character,
                }.Where(s => s is not null));
        }
        else if (item.Tb is { } tb)
        {
            TargetText = $"{tb.SourceTerm} → {tb.TargetTerm}";
            DiffRuns = [];
        }
    }

    public MatchListItem Item { get; }

    public int Number { get; }
    public string ScoreText { get; }
    public bool IsTm { get; }
    public bool IsForbidden { get; }
    public string TooltipText { get; }
    public string TargetText { get; }
    public IReadOnlyList<DiffRun> DiffRuns { get; }
    public string MetaText { get; }
}