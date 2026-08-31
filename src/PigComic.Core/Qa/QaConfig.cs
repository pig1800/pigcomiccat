using PigComic.Core.Project;

namespace PigComic.Core.Qa;

/// <summary>Config for the mechanical QA rules (SPEC §12). Defaults follow the §6.2 schema.</summary>
public sealed record QaConfig(
    int MaxCharsPerLine = 8,
    int MaxLinesPerPart = 4,
    int TcyMaxDigitRun = 3,
    IReadOnlyList<string>? IdenticalExemptKinds = null,
    IReadOnlyList<string>? ForbiddenTrailing = null,
    IReadOnlyList<string>? BracketPairs = null)
{
    public IReadOnlyList<string> IdenticalExemptKindsValue
        => IdenticalExemptKinds ?? ["Sfx"];

    public IReadOnlyList<string> ForbiddenTrailingValue
        => ForbiddenTrailing ?? [];

    public IReadOnlyList<string> BracketPairsValue
        => BracketPairs ?? ["「」", "『』", "（）", "()", "【】"];

    public static QaConfig FromProject(ProjectSettings settings)
        => new(
            MaxCharsPerLine: settings.MaxCharsPerLine,
            MaxLinesPerPart: settings.MaxLinesPerPart,
            TcyMaxDigitRun: settings.TcyMaxDigitRun,
            IdenticalExemptKinds: settings.IdenticalExemptKinds,
            ForbiddenTrailing: settings.ForbiddenTrailing,
            BracketPairs: settings.BracketPairs);
}