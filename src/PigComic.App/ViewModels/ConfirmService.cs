using PigComic.App.Services;
using PigComic.Core.Domain;
using PigComic.Core.Package;
using PigComic.Core.Tm;

namespace PigComic.App.ViewModels;

/// <summary>QA hook on confirm (no-op until M8; SPEC §14.4 ⚡).</summary>
public interface IConfirmQa
{
    void RunOnBubble(Bubble bubble);
}

/// <summary>Null implementation: confirmation never blocks on QA (D-15).</summary>
public sealed class NullConfirmQa : IConfirmQa
{
    public static readonly NullConfirmQa Instance = new();

    public void RunOnBubble(Bubble bubble)
    {
    }
}

/// <summary>
/// M5.4 confirm loop (SPEC §14.4): Ctrl+Enter/Ctrl+Shift+Enter (plain Enter is a
/// newline in target part editors — D-52), empty-target rule, TM upsert with
/// context (prevHash from the previous bubble in reading order), move-next
/// semantics, lock/unlock (D-16). TM writes happen only on confirm, never while
/// typing. <see cref="SkipConfirmed"/> backs the "Skip confirmed" status-bar
/// checkbox (owner directive 2026-08-25, D-52; persisted in registry.json until
/// it moves into the settings window with M8.6).
/// </summary>
public sealed class ConfirmService
{
    private readonly ChapterSession _session;
    private readonly SegmentListViewModel _segments;
    private readonly TmStore? _tm;
    private readonly IConfirmQa _qa;

    /// <summary>Raised after any bubble mutation that affects overlays/status bar.</summary>
    public event Action? BubblesChanged;

    /// <summary>Raised after the selection moved (editor focuses the next part editor).</summary>
    public event Action? SelectionMoved;

    /// <summary>
    /// When true (default), advancing after a confirm lands on the next bubble whose
    /// status is Untranslated or Draft. When false, it lands on the literal next bubble
    /// in reading order. Locked bubbles are always skipped either way.
    /// </summary>
    public bool SkipConfirmed { get; set; } = true;

    public ConfirmService(ChapterSession session, SegmentListViewModel segments, TmStore? tm = null, IConfirmQa? qa = null)
    {
        _session = session;
        _segments = segments;
        _tm = tm;
        _qa = qa ?? NullConfirmQa.Instance;
    }

    /// <summary>
    /// Confirm the selected bubble and move on. <paramref name="review"/> (Ctrl+Shift+Enter)
    /// confirms as Reviewed; plain confirm is Ctrl+Enter (D-52). The advance honors
    /// <see cref="SkipConfirmed"/> (Locked bubbles are always skipped).
    /// </summary>
    public void ConfirmAndMove(bool review, bool skipConfirmed)
    {
        var row = _segments.SelectedBubble;
        if (row is null || _session.Document.IsReadOnly)
        {
            return;
        }

        var target = row.Bubble.TargetJoined;
        if (target.Length == 0)
        {
            // §14.4: Enter on an empty target just moves on — no status change, no TM write.
            MoveNext(false);
            return;
        }

        row.ApplyStatus(review ? BubbleStatus.Reviewed : BubbleStatus.Translated);
        _qa.RunOnBubble(row.Bubble);
        if (_tm is not null)
        {
            _ = WriteTmAsync(row, target);
        }

        MoveNext(skipConfirmed);
    }

    /// <summary>Moves the selection to the next qualifying bubble (SPEC §14.4).</summary>
    public void MoveNext(bool skipConfirmed)
    {
        _segments.MoveNext(skipConfirmed);
        SelectionMoved?.Invoke();
        BubblesChanged?.Invoke();
    }

    public void ToggleLockSelected()
    {
        _segments.SelectedBubble?.ToggleLocked();
        BubblesChanged?.Invoke();
        SelectionMoved?.Invoke();
    }

    public void CopySourceToSelectedPart(int partIndex)
    {
        _segments.SelectedBubble?.CopySourceToPart(partIndex);
        BubblesChanged?.Invoke();
    }

    private async Task WriteTmAsync(BubbleRowViewModel row, string target)
    {
        try
        {
            var lang = _tm!.SourceLanguage;
            var prev = PreviousBubble(row.Bubble);
            long? prevHash = prev is null
                ? null
                : TmHash.Compute(Normalizer.Normalize(prev.SourceText, lang));
            await _tm.UpsertAsync(
                row.Bubble.SourceText, target,
                row.Bubble.Character, row.Bubble.Kind.ToString(),
                _session.Document.Model.ChapterNumber, row.Bubble.Id, prevHash,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TM write failed: {ex.Message}");
        }
    }

    private Bubble? PreviousBubble(Bubble current)
    {
        var bubbles = _session.Document.Model.Bubbles;
        for (var i = 0; i < bubbles.Count; i++)
        {
            if (bubbles[i].Id == current.Id && i > 0)
            {
                return bubbles[i - 1];
            }
        }

        return null;
    }
}