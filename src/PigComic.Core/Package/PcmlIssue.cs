namespace PigComic.Core.Package;

public enum PcmlSeverity
{
    Error,
    Warning,
}

/// <summary>One validation finding (§5.7).</summary>
public sealed record PcmlIssue(PcmlSeverity Severity, string Code, string Message, string? BubbleId = null)
{
    public bool IsError => Severity == PcmlSeverity.Error;
}