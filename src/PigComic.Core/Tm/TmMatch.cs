namespace PigComic.Core.Tm;

/// <summary>One ranked TM match (SPEC §7.5 result set).</summary>
public sealed record TmMatch(
    long EntryId,
    int Score,           // 70..103 after boosts; 100 = exact, 101-103 = context-boosted exact
    int BaseScore,       // before context boosts
    string SourceRaw,
    string SourceNorm,
    string TargetRaw,
    string? Character,
    string? Kind,
    string? Chapter,
    DateTime ModifiedUtc)
{
    public bool IsExact => BaseScore == 100;
}