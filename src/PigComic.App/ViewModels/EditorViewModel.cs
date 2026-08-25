using CommunityToolkit.Mvvm.ComponentModel;
using PigComic.App.Services;

namespace PigComic.App.ViewModels;

/// <summary>
/// M5.1+ editor status bar / chrome state. Owns the child view-models
/// (segment list, later match list) and delegates everything else to
/// <see cref="ChapterSession"/> and Core — no business logic here.
/// </summary>
public partial class EditorViewModel : ObservableObject
{
    [ObservableProperty]
    private string _chapterLabel = "";

    [ObservableProperty]
    private string _pageLabel = "";

    [ObservableProperty]
    private string _countsLabel = "";

    [ObservableProperty]
    private string _selectionLabel = "";

    [ObservableProperty]
    private string _saveStateLabel = "";

    /// <summary>"Saved HH:mm" after the last save/autosave (SPEC §14.1 status bar).</summary>
    [ObservableProperty]
    private string _lastSavedLabel = "";

    public void MarkSaved(DateTime when) => LastSavedLabel = $"Saved {when:HH:mm}";

    [ObservableProperty]
    private string? _loadError;

    public bool HasLoadError => LoadError is not null;

    partial void OnLoadErrorChanged(string? value) => OnPropertyChanged(nameof(HasLoadError));

    [ObservableProperty]
    private bool _isReadOnly;

    /// <summary>The segment list widget (null until a chapter is attached).</summary>
    public SegmentListViewModel? Segments { get; private set; }

    private ChapterSession? _session;

    /// <summary>Attaches to the opened chapter and fills the status bar fields.</summary>
    public void Attach(ChapterSession session)
    {
        _session = session;
        session.DirtyChanged += OnDirtyChanged;

        var model = session.Document.Model;
        ChapterLabel = string.IsNullOrEmpty(model.ChapterNumber)
            ? model.Title
            : $"ch.{model.ChapterNumber} — {model.Title}";
        CountsLabel = $"{model.Bubbles.Count} bubbles · {model.Images.Count} images";
        SelectionLabel = "—";
        IsReadOnly = session.Document.IsReadOnly;

        var segments = new SegmentListViewModel(session);
        segments.SelectionChanged += OnBubbleSelectionChanged;
        Segments = segments;

        UpdatePageLabel();
        UpdateSaveState();
    }

    /// <summary>Refreshes the strip-position label (called by the view as it scrolls).</summary>
    public void SetStripPosition(long stripY)
    {
        var height = _session.Document.Model.StripHeight;
        PageLabel = height <= 0
            ? "—"
            : $"y {stripY:N0} / {height:N0}";
        OnPropertyChanged(nameof(PageLabel));
    }

    private void UpdatePageLabel()
    {
        SetStripPosition(0);
    }

    private void OnBubbleSelectionChanged(BubbleRowViewModel? row) => RefreshSelectionLabel(row);

    /// <summary>Recomputes the status-bar selection field (after status changes too).</summary>
    public void RefreshSelectionLabel() => RefreshSelectionLabel(Segments?.SelectedBubble);

    private void RefreshSelectionLabel(BubbleRowViewModel? row)
    {
        SelectionLabel = row is null
            ? "—"
            : $"{row.Id} · {(row.CharacterText.Length > 0 ? row.CharacterText + " · " : "")}{row.Status}";
    }

    private void OnDirtyChanged() => UpdateSaveState();

    private void UpdateSaveState()
    {
        SaveStateLabel = _session is { Document.IsReadOnly: true }
            ? "read-only"
            : _session?.IsDirty == true ? "unsaved" : "saved";
    }
}