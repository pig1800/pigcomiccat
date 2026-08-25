using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PigComic.App.Services;
using PigComic.Core.Domain;
using PigComic.Core.Package;
using PigComic.Core.Tb;
using PigComic.Core.Tm;

namespace PigComic.App.ViewModels;

/// <summary>
/// M5.5 TM/TB results box (SPEC §9). Query runs on selection change with a
/// 150 ms debounce; stale results are discarded. Ctrl+1..9 / double-click
/// insert; TM insert replaces the whole target (D-12, status → Draft); TB
/// insert raises <see cref="TbInsertRequested"/> for caret insertion.
/// </summary>
public partial class MatchListViewModel : ObservableObject
{
    private readonly ChapterSession _session;
    private readonly SegmentListViewModel _segments;
    private readonly MatchListService? _service;
    private CancellationTokenSource? _cts;

    public ObservableCollection<MatchRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>Raised when a TB term should be inserted at the caret (SPEC §9).</summary>
    public event Action<string>? TbInsertRequested;

    /// <summary>Raised after a TM insert changes the bubble (overlay/status bar refresh).</summary>
    public event Action? BubblesChanged;

    public MatchListViewModel(ChapterSession session, SegmentListViewModel segments, TmStore? tm, TbStore? tb)
    {
        _session = session;
        _segments = segments;
        if (tm is not null && tb is not null)
        {
            _service = new MatchListService(new TmQueryService(tm), tb);
        }

        segments.SelectionChanged += _ => DebouncedQuery();
    }

    private async void DebouncedQuery()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var row = _segments.SelectedBubble;
        if (row is null || _service is null)
        {
            StatusText = "";
            Rows.Clear();
            return;
        }

        var source = row.Bubble.SourceText;
        var lang = _session.Document.Model.SourceLanguage;
        try
        {
            await Task.Delay(150, cts.Token).ConfigureAwait(true);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        var context = new TmQueryContext(
            lang,
            row.Bubble.Character,
            row.Bubble.Kind.ToString(),
            PreviousHash(row));

        IReadOnlyList<MatchListItem> items;
        try
        {
            items = await _service.BuildAsync(source, lang, context, cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            StatusText = $"Query failed: {ex.Message}";
            Rows.Clear();
            return;
        }

        if (cts.IsCancellationRequested)
        {
            return; // stale selection — discard
        }

        StatusText = items.Count == 0 ? "No matches" : $"{items.Count} result(s)";
        Rows.Clear();
        foreach (var item in items)
        {
            Rows.Add(new MatchRowViewModel(item, source));
        }
    }

    private long? PreviousHash(BubbleRowViewModel row)
    {
        var bubbles = _session.Document.Model.Bubbles;
        for (var i = 0; i < bubbles.Count; i++)
        {
            if (bubbles[i].Id == row.Id && i > 0)
            {
                return TmHash.Compute(Normalizer.Normalize(bubbles[i - 1].SourceText, _session.Document.Model.SourceLanguage));
            }
        }

        return null;
    }

    /// <summary>Inserts numbered result N (Ctrl+1..9 / double-click, SPEC §9).</summary>
    public void Insert(int number)
    {
        var row = Rows.FirstOrDefault(r => r.Number == number);
        var bubble = _segments.SelectedBubble;
        if (row is null || bubble is null)
        {
            return;
        }

        if (row.IsTm && row.Item.Tm is { } tm)
        {
            ReplaceTargetWith(bubble, tm.TargetRaw);
            return;
        }

        if (row.Item.Tb is { } tb && !row.IsForbidden)
        {
            TbInsertRequested?.Invoke(tb.TargetTerm);
        }
    }

    /// <summary>D-12: TM insert replaces the whole target, collapses to 1 part, status → Draft.</summary>
    private void ReplaceTargetWith(BubbleRowViewModel bubble, string target)
    {
        BubbleMutations.SetPartCount(bubble.Bubble, 1);
        bubble.Bubble.Parts[0].Text = target;
        BubbleMutations.SetStatus(bubble.Bubble, BubbleStatus.Draft);
        bubble.RefreshParts();
        bubble.MarkDirty();
        BubblesChanged?.Invoke();
    }
}