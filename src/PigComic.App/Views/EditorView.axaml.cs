using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using PigComic.App.Controls;
using PigComic.App.Services;
using PigComic.App.ViewModels;
using PigComic.Core.Package;
using PigComic.Core.Project;
using PigComic.Core.Tb;
using PigComic.Core.Tm;

namespace PigComic.App.Views;

/// <summary>
/// M5.1 editor shell (SPEC §14.1): three panes with persisted splitter widths,
/// status bar, page-1 image via <c>TiledImageControl</c>. Open flow: journal
/// two-button gate (§23, interim until M9.2) → validation issues shown once
/// (§5.7: Errors = read-only, Warnings open normally) → render page 1.
/// </summary>
public partial class EditorView : Window
{
    private readonly string _pcmlPath;
    private readonly string? _projectFolder;
    private readonly EditorViewModel _vm;
    private ChapterSession? _session;
    private bool _closed;
    private TmStore? _tm;
    private TbStore? _tb;
    private ConfirmService? _confirm;
    private MatchListViewModel? _matches;
    private AutosaveTimer? _autosave;
    private BubbleRowViewModel? _statusRow;

    public EditorView(string pcmlPath, string? projectFolder = null)
    {
        InitializeComponent();
        _pcmlPath = pcmlPath;
        _projectFolder = projectFolder;
        _vm = new EditorViewModel();
        DataContext = _vm;
        Closed += OnClosed;
        ApplyStoredLayout();
        ImagePane.OverlayClicked += OnOverlayClicked;
        ImagePane.ScrollRequested += OnScrollRequested;
        ImagePane.StripPositionChanged += y => _vm.SetStripPosition(y);
        _ = LoadAsync();
    }

    /// <summary>Spec §14.1: splitter widths persist in registry.json.</summary>
    private void ApplyStoredLayout()
    {
        var (imageWidth, functionWidth) = EditorLayoutStore.Load();
        PaneGrid.ColumnDefinitions[0].Width = new GridLength(imageWidth);
        PaneGrid.ColumnDefinitions[4].Width = new GridLength(functionWidth);
    }

    private void OnSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        var image = (int)PaneGrid.ColumnDefinitions[0].Width.Value;
        var function = (int)PaneGrid.ColumnDefinitions[4].Width.Value;
        EditorLayoutStore.Save(image, function);
    }

    private async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(_pcmlPath))
            {
                _vm.LoadError = $"Chapter not found:\n{_pcmlPath}\n\nUse the project's Relink flow to restore it.";
                return;
            }

            // SPEC §23 interim two-button journal gate (M9.2 replaces this with Recover).
            if (File.Exists(_pcmlPath + ".journal"))
            {
                var discard = await ContentDialog.AskAsync(this,
                    "Crash-recovery journal",
                    "This chapter has a .pcml.journal from an interrupted session. " +
                    "Recovery arrives in M9 — Discard deletes it and opens the chapter.",
                    "Discard", "Cancel").ConfigureAwait(true);
                if (!discard)
                {
                    Close();
                    return;
                }

                File.Delete(_pcmlPath + ".journal");
            }

            var session = await ChapterSession.OpenAsync(_pcmlPath, CancellationToken.None).ConfigureAwait(true);
            _session = session;
            _vm.Attach(session);
            Title = $"Editor — {session.Document.Model.Title}";
            if (_vm.Segments is { } segments)
            {
                SegmentList.DataContext = segments;
                segments.SelectionChanged += OnBubbleSelectionChanged;
            }

            OpenStores(session);
            _confirm = new ConfirmService(session, _vm.Segments!, _tm);
            _confirm.SelectionMoved += OnSelectionMoved;
            _confirm.BubblesChanged += OnBubblesChanged;
            SegmentList.Confirm = _confirm;

            _matches = new MatchListViewModel(session, _vm.Segments!, _tm, _tb);
            _matches.TbInsertRequested += OnTbInsertRequested;
            _matches.BubblesChanged += OnBubblesChanged;
            FunctionPane.DataContext = _matches;

            StartAutosave(session);

            var issues = session.Document.Issues;
            var errors = issues.Where(i => i.Severity == PcmlSeverity.Error).ToList();
            var warnings = issues.Where(i => i.Severity == PcmlSeverity.Warning).ToList();
            if (errors.Count > 0)
            {
                await ContentDialog.AskAsync(this,
                    "Opened read-only (validation errors)",
                    string.Join("\n", errors.Select(e => $"  {e.Code}: {e.Message}")),
                    "OK", null).ConfigureAwait(true);
            }
            else if (warnings.Count > 0)
            {
                await ContentDialog.AskAsync(this,
                    "Opened with warnings (shown once)",
                    string.Join("\n", warnings.Select(w => $"  {w.Code}: {w.Message}")),
                    "OK", null).ConfigureAwait(true);
            }

            LoadStrip(session);
        }
        catch (Exception ex)
        {
            _vm.LoadError = $"Cannot open chapter:\n{ex.Message}";
        }
    }

    /// <summary>Installs the whole chapter as one continuous strip (D-49).</summary>
    private void LoadStrip(ChapterSession session)
    {
        var chapter = session.Document.Model;
        try
        {
            var segments = new List<StripSegment>();
            foreach (var image in chapter.Images)
            {
                var path = session.PageImagePath(image.FileName);
                if (path is not null)
                {
                    segments.Add(new StripSegment(path, image.Width, image.Height, image.StripTop));
                }
            }

            if (segments.Count > 0)
            {
                ImagePane.SetStrip(segments);
            }

            RefreshOverlays();
            _vm.SetStripPosition(0);
        }
        catch (Exception ex)
        {
            _vm.LoadError = ex.Message;
        }
    }

    /// <summary>Rebuilds the marker overlays for the whole strip (SPEC §14.2).</summary>
    private void RefreshOverlays()
    {
        if (_session is null)
        {
            return;
        }

        var chapter = _session.Document.Model;
        var selected = _vm.Segments?.SelectedBubble;

        var overlays = new List<OverlayMarker>();
        foreach (var bubble in chapter.Bubbles)
        {
            var isSelected = selected is not null && selected.Id == bubble.Id;
            overlays.Add(new OverlayMarker(
                bubble.Id,
                new Avalonia.Point(bubble.Marker.X, bubble.Marker.Y),
                bubble.Status,
                isSelected,
                isSelected
                    ? bubble.Parts.Select(p => new Avalonia.Point(p.Marker.X, p.Marker.Y)).ToList()
                    : null));
        }

        ImagePane.SetOverlays(overlays);
    }

    /// <summary>Row selected in the list → scroll the strip to its marker (SPEC §14.2).</summary>
    private void OnBubbleSelectionChanged(BubbleRowViewModel? row)
    {
        if (_statusRow is not null)
        {
            _statusRow.StatusChanged -= OnRowStatusChanged;
            _statusRow = null;
        }

        if (row is not null)
        {
            _statusRow = row;
            row.StatusChanged += OnRowStatusChanged;
        }

        if (_session is null)
        {
            return;
        }

        RefreshOverlays();

        if (row is not null)
        {
            ImagePane.CenterOn(new Avalonia.Point(row.Bubble.Marker.X, row.Bubble.Marker.Y));
        }
    }

    /// <summary>Image click → select the row (list auto-scrolls via selection binding).</summary>
    private void OnOverlayClicked(string bubbleId)
        => _vm.Segments?.SelectBubbleId(bubbleId);

    private void OnScrollRequested(int delta) => ImagePane.ScrollByViewports(delta);

    /// <summary>Opens the project's TM/TB stores when a project folder is known (SPEC §7/§8).</summary>
    private void OpenStores(ChapterSession session)
    {
        if (_projectFolder is null)
        {
            return;
        }

        var m = session.Document.Model;
        try
        {
            _tm = new TmStore(Path.Combine(_projectFolder, "tm.db"), m.SourceLanguage, m.TargetLanguage);
            _tb = new TbStore(Path.Combine(_projectFolder, "tb.db"), m.SourceLanguage, m.TargetLanguage);
        }
        catch (Exception ex)
        {
            // A missing/mismatched store disables TM/TB features but never blocks editing.
            System.Diagnostics.Debug.WriteLine($"Stores unavailable: {ex.Message}");
            _tm?.Dispose();
            _tm = null;
            _tb?.Dispose();
            _tb = null;
        }
    }

    private void OnSelectionMoved() => Dispatcher.UIThread.Post(SegmentList.FocusFirstPartOfSelected, DispatcherPriority.Background);

    private void OnBubblesChanged() => RefreshOverlays();

    private void OnRowStatusChanged()
    {
        _vm.RefreshSelectionLabel();
        RefreshOverlays();
    }

    /// <summary>TB term insert: at the caret of the focused part editor (SPEC §9).</summary>
    private void OnTbInsertRequested(string term) => SegmentList.InsertAtCaret(term);

    /// <summary>Ctrl+1..9 inserts the Nth result (SPEC §9/§14.6): fire-and-forget key routing.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (PigComic.App.KeyBindings.IsSave(e))
        {
            e.Handled = true;
            _ = SaveNowAsync();
        }
        else if (PigComic.App.KeyBindings.IsEditSource(e))
        {
            SegmentList.BeginSourceEditSelected();
            e.Handled = true;
        }
        else if (PigComic.App.KeyBindings.NthMatch(e) is { } n)
        {
            _matches?.Insert(n);
            e.Handled = true;
        }
    }

    /// <summary>Ctrl+S manual save (SPEC §14.6).</summary>
    private async Task SaveNowAsync()
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            await _session.SaveAsync(CancellationToken.None);
            _vm.MarkSaved(DateTime.Now);
            _vm.SaveStateLabel = "saved";
        }
        catch (Exception ex)
        {
            await ContentDialog.AskAsync(this, "Save failed", ex.Message, "OK", null);
        }
    }

    /// <summary>Autosave every autosaveSeconds when dirty (SPEC §5.5/§6.2, default 180).</summary>
    private void StartAutosave(ChapterSession session)
    {
        var seconds = 180;
        if (_projectFolder is not null)
        {
            try
            {
                var projectPath = Path.Combine(_projectFolder, "project.json");
                if (File.Exists(projectPath))
                {
                    seconds = ProjectFile.Load(projectPath).Settings.AutosaveSeconds;
                }
            }
            catch
            {
                // Malformed settings just fall back to the default.
            }
        }

        _autosave = new AutosaveTimer(session, seconds);
        _autosave.Saved += when =>
        {
            _vm.MarkSaved(when);
            _vm.SaveStateLabel = "saved";
        };
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _autosave?.Dispose();
        ImagePane.Dispose();
        _session?.Dispose();
        _tm?.Dispose();
        _tb?.Dispose();
    }
}

