namespace PigComic.Core.Tm;

/// <summary>One TM entry row (SPEC §7.1).</summary>
public sealed record TmEntry(
    long Id,
    string SourceRaw,
    string SourceNorm,
    long SourceHash,
    string TargetRaw,
    string? Character,
    string? Kind,
    string? Chapter,
    string? BubbleId,
    long? PrevHash,
    string CreatedUtc,
    string ModifiedUtc);