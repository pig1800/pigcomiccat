namespace PigComic.Core.Counting;

/// <summary>Billing/progress counting seam (SPEC §11). Implementations are pure functions.</summary>
public interface ICounter
{
    /// <summary>Both counts for one text (a bubble's source or target).</summary>
    CountResult Count(string text);
}