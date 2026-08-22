namespace PigComic.Core.Package;

/// <summary>
/// Description of one typed mutation, captured for undo reuse (SPEC §22 /
/// PLAN M1.6). <see cref="Payload"/> is a small JSON object carrying the
/// bubble id and the "before" state needed to revert the operation.
/// </summary>
public sealed record MutationRecord(string OpName, string? Payload = null)
{
    public override string ToString() => OpName + (Payload is null ? "" : $" {Payload}");
}