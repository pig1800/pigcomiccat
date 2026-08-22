namespace PigComic.Core.Tm;

/// <summary>
/// SPEC §7.5 query context. <see cref="PrevSourceHash"/> = hash of the normalized
/// source of the bubble immediately before the queried bubble in reading order
/// (null for the first bubble).
/// </summary>
public sealed record TmQueryContext(
    string SourceLanguage,
    string? Character = null,
    string? Kind = null,
    long? PrevSourceHash = null);