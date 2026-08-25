using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PigComic.App.Services;
using PigComic.Core.Domain;

namespace PigComic.App.ViewModels;

/// <summary>
/// M5.2 segment list: virtualized list of page-group headers + bubble rows in
/// global reading order (SPEC §14.3). Selection movement (Ctrl+Up/Down) and the
/// status-bar hook live here; image-pane sync is wired by the editor view.
/// </summary>
public partial class SegmentListViewModel : ObservableObject
{
    private readonly ChapterSession _session;

    public ObservableCollection<object> Items { get; } = [];

    [ObservableProperty]
    private object? _selectedItem;

    /// <summary>Selected bubble row (null when a header or nothing is selected).</summary>
    public BubbleRowViewModel? SelectedBubble => SelectedItem as BubbleRowViewModel;

    /// <summary>Raised when the selected bubble changes (bubble may be null).</summary>
    public event Action<BubbleRowViewModel?>? SelectionChanged;

    public SegmentListViewModel(ChapterSession session)
    {
        _session = session;
        Rebuild();
        PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectedItem))
        {
            SelectionChanged?.Invoke(SelectedBubble);
        }
    }

    /// <summary>Builds the flat item list: one header per page, then its bubbles in order.</summary>
    public void Rebuild()
    {
        Items.Clear();
        var chapter = _session.Document.Model;
        var byPage = new Dictionary<string, List<Bubble>>();
        foreach (var b in chapter.Bubbles)
        {
            byPage.TryAdd(b.PageId, []);
            byPage[b.PageId].Add(b);
        }

        var facade = new ChapterSessionFacade(_session);
        for (var i = 0; i < chapter.Pages.Count; i++)
        {
            var page = chapter.Pages[i];
            Items.Add(new SegmentPageHeaderViewModel(page.Id, i));
            if (byPage.TryGetValue(page.Id, out var bubbles))
            {
                foreach (var b in bubbles)
                {
                    Items.Add(BubbleRowViewModel.Create(b, facade));
                }
            }
        }
    }

    /// <summary>Moves the selection over bubble rows (spec Ctrl+Up/Down; skips headers).</summary>
    public void MoveSelection(int delta)
    {
        if (Items.Count == 0)
        {
            return;
        }

        var start = Math.Max(0, Items.IndexOf(SelectedItem ?? Items[0]));
        var idx = start;
        while (true)
        {
            idx += delta;
            if (idx < 0 || idx >= Items.Count)
            {
                return; // clamped at the ends
            }

            if (Items[idx] is BubbleRowViewModel)
            {
                SelectedItem = Items[idx];
                return;
            }
        }
    }

    /// <summary>Moves to the next bubble row in reading order (SPEC §14.4 next-bubble).</summary>
    public void MoveNext(bool skipConfirmed)
    {
        var start = Items.IndexOf(SelectedItem ?? Items[0]);
        if (start < 0)
        {
            start = -1;
        }

        for (var i = start + 1; i < Items.Count; i++)
        {
            if (Items[i] is BubbleRowViewModel row &&
                (!skipConfirmed || row.Status is BubbleStatus.Untranslated or BubbleStatus.Draft))
            {
                SelectedItem = row;
                return;
            }
        }
    }

    /// <summary>Selects the row for a bubble id (used by image-pane clicks / navigation).</summary>
    public void SelectBubbleId(string? bubbleId)
    {
        if (bubbleId is null)
        {
            SelectedItem = null;
            return;
        }

        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i] is BubbleRowViewModel row && row.Id == bubbleId)
            {
                SelectedItem = row;
                return;
            }
        }

        SelectedItem = null;
    }
}