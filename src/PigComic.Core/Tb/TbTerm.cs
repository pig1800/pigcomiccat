namespace PigComic.Core.Tb;

/// <summary>One TB term row (SPEC §8.1). Forbidden rows: target_term must NOT appear in target text.</summary>
public sealed record TbTerm(
    long Id,
    string SourceTerm,
    string SourceNorm,
    string TargetTerm,
    bool Forbidden,
    string Notes,
    string CreatedUtc,
    string ModifiedUtc);