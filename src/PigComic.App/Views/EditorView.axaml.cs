using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using PigComic.App.Controls;
using PigComic.App.Services;
using PigComic.App.ViewModels;
using PigComic.Core.Package;
using PigComic.Core.Project;
using PigComic.Core.Qa;
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
    private string? _storeError;
    private ConfirmService? _confirm;
    private QaEngine? _qaEngine;
    private QaPanelViewModel? _qaPanel;
    private ConfirmQa? _confirmQa;
    private MatchListViewModel? _matches;
    private FunctionPaneViewModel? _pane;
    private AutosaveTimer? _autosave;
    private BubbleRowViewModel? _statusRow;

    public EditorView(string pcmlPath, string? projectFolder = null)
    {
        InitializeComponent();
        _pcmlPath = pcmlPath;
        _projectFolder = projectFolder;
        _vm = new EditorViewModel();
        ApplyStoredLayout();
        DataContext = _vm;
        _vm.PropertyChanged += OnVmPropertyChanged;
        Closed += OnClosed;
        ImagePane.OverlayClicked += OnOverlayClicked;
        ImagePane.ScrollRequested += OnScrollRequested;
        ImagePane.StripPositionChanged += y => _vm.SetStripPosition(y);
        ImagePane.PlaceMarkerRequested += OnPlaceMarkerRequested;
        ImagePane.MarkerDragCompleted += OnMarkerDragCompleted;
        _ = LoadAsync();
    }

    /// <summary>Spec §14.1: splitter widths persist in registry.json (D-52 adds the skip-confirmed option).</summary>
    private void ApplyStoredLayout()
    {
        var (imageWidth, functionWidth, skipConfirmed) = EditorLayoutStore.Load();
        PaneGrid.ColumnDefinitions[0].Width = new GridLength(imageWidth);
        PaneGrid.ColumnDefinitions[4].Width = new GridLength(functionWidth);
        _vm.SkipConfirmed = skipConfirmed;
    }

    private void OnSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        PersistLayout();
    }

    private void PersistLayout()
    {
        var image = (int)PaneGrid.ColumnDefinitions[0].Width.Value;
        var function = (int)PaneGrid.ColumnDefinitions[4].Width.Value;
        EditorLayoutStore.Save(image, function, _vm.SkipConfirmed);
    }

    /// <summary>
    /// Keeps the skip-confirmed checkbox (D-52) and the confirm service in sync and
    /// persists the option — same best-effort path as the splitter widths.
    /// </summary>
    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.SkipConfirmed))
        {
            if (_confirm is not null)
            {
                _confirm.SkipConfirmed = _vm.SkipConfirmed;
            }

            PersistLayout();
        }
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

            _qaEngine = new QaEngine(LoadQaConfig(), _tb);
            _qaPanel = new QaPanelViewModel();
            _qaPanel.NavigateRequested += OnQaNavigate;
            QaPanel.DataContext = _qaPanel;
            _confirmQa = new ConfirmQa(_qaEngine, _qaPanel, session.Document.Model.SourceLanguage,
                session.Document.Model.TargetLanguage);
            _confirmQa.IssuesFound += (_, _) => RefreshQaMarkers();

            _confirm = new ConfirmService(session, _vm.Segments!, _tm, _confirmQa)
            {
                SkipConfirmed = _vm.SkipConfirmed,
            };
            _confirm.SelectionMoved += OnSelectionMoved;
            _confirm.BubblesChanged += OnBubblesChanged;
            SegmentList.Confirm = _confirm;

            _pane = new FunctionPaneViewModel(session, _vm.Segments!, _tm, _tb, _projectFolder);
            _matches = _pane.Matches;
            _matches.StoreError = _storeError;
            _matches.TbInsertRequested += OnTbInsertRequested;
            _matches.BubblesChanged += OnBubblesChanged;
            _pane.Characters.AddToMasterRequested += OnAddToMasterRequested;
            FunctionPane.OpenMasterRequested += OnOpenMaster;
            FunctionPane.DataContext = _pane;

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

    /// <summary>Marker drag committed on mouse-up (SPEC §15.1): apply the Core mutation.</summary>
    private void OnMarkerDragCompleted(string bubbleId, int? partIndex, PigComic.Core.Domain.PixelPoint point)
    {
        if (_session is null)
        {
            return;
        }

        var bubble = _session.Document.Model.Bubbles.FirstOrDefault(b => b.Id == bubbleId);
        if (bubble is null)
        {
            return;
        }

        if (partIndex is { } pi)
        {
            // Sub cross (part marker): moves only that part — never reorders (PSD-only).
            BubbleMutations.SetPartMarker(bubble, pi, point);
            _session.MarkDirty();
            RefreshOverlays();
            return;
        }

        // Main cross (source marker): move + renumber reading order by Y (Q8 resolved —
        // the owner wants drag-to-reorder on the main cross; sub crosses don't count).
        BubbleMutations.SetMarker(bubble, point);
        BubbleMutations.RenumberByMarkerY(_session.Document);
        _session.MarkDirty();
        _vm.Segments?.Rebuild();
        _vm.Segments?.SelectBubbleId(bubbleId); // keep selection on the dragged bubble
        RefreshOverlays();
    }

    /// <summary>Placement-mode click: create the bubble (SPEC §15.2) and select it.</summary>
    private void OnPlaceMarkerRequested(PigComic.Core.Domain.PixelPoint point)
    {
        if (_session is null)
        {
            return;
        }

        BubbleMutations.AddBubble(_session.Document, point, out var created);
        _session.MarkDirty();
        _vm.Segments?.Rebuild();
        _vm.RefreshCounts();
        _vm.Segments?.SelectBubbleId(created.Id);
        RefreshOverlays();
        OnSelectionMoved();
    }

    /// <summary>Alt+1/2/3: set the selected bubble's target part count (SPEC §15.3).</summary>
    internal void ApplyPartCount(int count)
    {
        if (_session is null || _vm.Segments?.SelectedBubble is not { } row)
        {
            return;
        }

        BubbleMutations.SetPartCount(row.Bubble, count);
        row.RefreshParts();
        _session.MarkDirty();
        RefreshOverlays();
    }

    /// <summary>Deletes the selected bubble after the confirm dialog (SPEC §15.2).</summary>
    private async Task DeleteSelectedBubbleAsync()
    {
        if (_session is null || _vm.Segments?.SelectedBubble is not { } row)
        {
            return;
        }

        var source = row.Bubble.SourceText;
        var preview = string.IsNullOrWhiteSpace(source) ? "(empty source)" : source;
        var ok = await ContentDialog.AskAsync(this, "Delete bubble",
            $"Delete bubble {row.Id}?\n\nSource: {preview}", "Delete", "Cancel");
        if (ok)
        {
            ApplyDeleteSelected(row);
        }
    }

    /// <summary>Applies the delete mutation and refreshes list/overlays (smoke path skips the dialog).</summary>
    internal void ApplyDeleteSelected(BubbleRowViewModel row)
    {
        if (_session is null)
        {
            return;
        }

        // Remember a neighbour id so the selection lands on something after Rebuild.
        var items = _vm.Segments?.Items ?? [];
        var index = items.IndexOf(row);
        string? nextId = null;
        for (var i = index + 1; i < items.Count; i++)
        {
            if (items[i] is BubbleRowViewModel r)
            {
                nextId = r.Id;
                break;
            }
        }

        nextId ??= items.Take(index).OfType<BubbleRowViewModel>().LastOrDefault()?.Id;

        BubbleMutations.DeleteBubble(_session.Document, row.Bubble);
        _session.MarkDirty();
        _vm.Segments?.Rebuild();
        _vm.RefreshCounts();
        if (nextId is not null)
        {
            _vm.Segments?.SelectBubbleId(nextId);
        }

        RefreshOverlays();
    }

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
            _storeError = null;
        }
        catch (Exception ex)
        {
            // A missing/mismatched store disables TM/TB features but never blocks editing.
            // The reason is surfaced in the match box so "TM isn't working" is explainable
            // (e.g. a stale tm.db from before the D-51 language-pair flip) instead of
            // looking like a perpetually empty result set.
            System.Diagnostics.Debug.WriteLine($"Stores unavailable: {ex.Message}");
            _storeError = ex.Message;
            _tm?.Dispose();
            _tm = null;
            _tb?.Dispose();
            _tb = null;
        }
    }

    private void OnSelectionMoved() => Dispatcher.UIThread.Post(SegmentList.FocusFirstPartOfSelected, DispatcherPriority.Background);

    /// <summary>QA config from the project's settings (SPEC §12/§6.2); falls back to defaults.</summary>
    private QaConfig LoadQaConfig()
    {
        if (_projectFolder is not null)
        {
            try
            {
                var projectPath = Path.Combine(_projectFolder, "project.json");
                if (File.Exists(projectPath))
                {
                    return QaConfig.FromProject(ProjectFile.Load(projectPath).Settings);
                }
            }
            catch
            {
                // Malformed settings fall back to the §12 defaults.
            }
        }

        return new QaConfig();
    }

    /// <summary>F8: run mechanical QA over the whole chapter and show the dock panel (SPEC §12).</summary>
    internal void RunChapterQa()
    {
        if (_session is null || _qaEngine is null || _qaPanel is null)
        {
            return;
        }

        _qaPanel.RunChapterResult(_qaEngine.RunOnChapter(_session.Document.Model));
        RefreshQaMarkers();
    }

    /// <summary>Pushes the panel's per-bubble issues into the current rows (⚡ icons, SPEC §12).</summary>
    private void RefreshQaMarkers()
    {
        if (_qaPanel is null || _vm.Segments is not { } segments)
        {
            return;
        }

        foreach (var row in segments.Items.OfType<BubbleRowViewModel>())
        {
            row.SetQaIssues(_qaPanel.IssuesFor(row.Id));
        }
    }

    /// <summary>Double-click a QA row: select the bubble, center the strip, focus its editor.</summary>
    private void OnQaNavigate(string bubbleId)
    {
        if (_vm.Segments is not { } segments)
        {
            return;
        }

        segments.SelectBubbleId(bubbleId);
        if (segments.SelectedBubble is { } row)
        {
            ImagePane.CenterOn(new Avalonia.Point(row.Bubble.Marker.X, row.Bubble.Marker.Y));
            SegmentList.FocusFirstPartOfSelected();
        }
    }

    private void OnBubblesChanged()
    {
        RefreshOverlays();
        RefreshQaMarkers();
    }

    private void OnRowStatusChanged()
    {
        _vm.RefreshSelectionLabel();
        RefreshOverlays();
    }

    /// <summary>TB term insert: at the caret of the focused part editor (SPEC §9).</summary>
    private void OnTbInsertRequested(string term) => SegmentList.InsertAtCaret(term);

    /// <summary>§14.5 "Add to master?" accepted: open the master editor prefilled with the name.</summary>
    private void OnAddToMasterRequested(string name)
    {
        if (_projectFolder is null)
        {
            return;
        }

        new CharacterMasterWindow(_projectFolder, name).Show();
    }

    /// <summary>Direct entrance to the master editor from the function pane (button or Ctrl+Shift+M).</summary>
    private void OnOpenMaster()
    {
        if (_projectFolder is null)
        {
            return;
        }

        new CharacterMasterWindow(_projectFolder).Show();
    }

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
        else if (PigComic.App.KeyBindings.IsPlaceMarker(e))
        {
            // SPEC §15.2: Ctrl+B arms/disarms placement mode; Esc also disarms.
            ImagePane.PlacementArmed = !ImagePane.PlacementArmed;
            e.Handled = true;
        }
        else if (PigComic.App.KeyBindings.IsDeleteBubble(e) && !IsEditorTextFocused())
        {
            // SPEC §15.2: Delete removes the selected bubble (not while a part editor has
            // focus — there Delete is ordinary text deletion).
            e.Handled = true;
            _ = DeleteSelectedBubbleAsync();
        }
        else if (PigComic.App.KeyBindings.SetPartCount(e) is { } count)
        {
            ApplyPartCount(count);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && ImagePane.CancelInteraction())
        {
            e.Handled = true;
        }
        else if (PigComic.App.KeyBindings.IsFocusKind(e))
        {
            FunctionPane.FocusKind();
            e.Handled = true;
        }
        else if (PigComic.App.KeyBindings.IsFocusCharacter(e))
        {
            FunctionPane.FocusCharacter();
            e.Handled = true;
        }
        else if (PigComic.App.KeyBindings.IsFocusNotes(e))
        {
            FunctionPane.FocusNotes();
            e.Handled = true;
        }
        else if (PigComic.App.KeyBindings.IsOpenMaster(e))
        {
            OnOpenMaster();
            e.Handled = true;
        }
        else if (PigComic.App.KeyBindings.IsRunQa(e))
        {
            RunChapterQa();
            e.Handled = true;
        }
    }

    /// <summary>True when keyboard focus is inside a part/source editor (Delete must stay text deletion).</summary>
    private bool IsEditorTextFocused()
        => TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is PartTextEditor;

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

