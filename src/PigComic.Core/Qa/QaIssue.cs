namespace PigComic.Core.Qa;

public enum QaSeverity
{
    Warning,
    Error,
}

/// <summary>One mechanical-QA finding (SPEC §12).</summary>
public sealed record QaIssue(
    string RuleId,
    QaSeverity Severity,
    string BubbleId,
    int? PartIndex,
    string Message);