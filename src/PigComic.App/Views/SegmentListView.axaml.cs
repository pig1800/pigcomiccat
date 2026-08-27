using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PigComic.App.Controls;
using PigComic.App.ViewModels;

namespace PigComic.App.Views;

/// <summary>
/// M5.2–M5.4 segment list view. Ctrl+Up/Down move the selection; the target
/// column's part editors wire confirm variants / Ctrl+L / Ctrl+Insert / draft
/// demotion into the <see cref="ConfirmService">. Virtualized rows get their
/// editors wired on ContainerPrepared.
/// </summary>
public partial class SegmentListView : UserControl
{
    private readonly HashSet<PartTextEditor> _wired = [];
    private readonly HashSet<BubbleRowViewModel> _rowWired = [];
    private PartTextEditor? _lastFocusedEditor;

    public SegmentListView()
    {
        InitializeComponent();
    }

    /// <summary>Injected by the editor (M5.4).</summary>
    public ConfirmService? Confirm { get; set; }

    private void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is not ListBoxItem lbi ||
            e.Container.DataContext is not BubbleRowViewModel row)
        {
            return;
        }

        WireEditors(lbi, row);
    }

    private void WireEditors(ListBoxItem lbi, BubbleRowViewModel row)
    {
        WireEditorsIn(lbi, row, 0);
    }

    private void WireEditorsIn(Visual container, BubbleRowViewModel row, int depth)
    {
        if (_rowWired.Add(row))
        {
            // Parts can change under this row (Alt+1/2/3 split/merge, M6.4): the inner
            // ItemsControl realizes fresh part editors that the outer ContainerPrepared
            // never sees, so re-wire after any parts change. The outer ListBoxItem is
            // keyed to the row and survives; re-find it on the UI thread.
            row.Parts.CollectionChanged += (_, _) =>
                Dispatcher.UIThread.Post(() =>
                {
                    var c = SegmentList.ContainerFromItem(row) as Visual;
                    if (c is not null)
                    {
                        WireEditorsIn(c, row, 0);
                    }
                }, DispatcherPriority.Background);
        }

        foreach (var editor in container.GetVisualDescendants().OfType<PartTextEditor>())
        {
            if (editor.Name == "SourceEditor")
            {
                WireSourceEditor(editor, row);
            }
            else
            {
                WireEditor(editor, row);
            }
        }

        if (container.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>()
                .FirstOrDefault(t => t.Name == "SourceText") is { } sourceText)
        {
            sourceText.DoubleTapped += (_, _) => row.StartSourceEdit();
        }

        // The inner ItemsControl realizes its part containers asynchronously;
        // retry once on the background priority to catch late containers.
        if (depth == 0 && row.Parts.Count > 0)
        {
            Dispatcher.UIThread.Post(() => WireEditorsIn(container, row, 1), DispatcherPriority.Background);
        }
    }

    private void WireSourceEditor(PartTextEditor editor, BubbleRowViewModel row)
    {
        if (_wired.Contains(editor))
        {
            return;
        }

        _wired.Add(editor);
        editor.ConfirmRequested += (_, _) => row.CommitSourceEdit();
        editor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                row.CancelSourceEdit();
                e.Handled = true;
            }
        };
        editor.GotFocus += (_, _) => SelectRowFor(editor, row);
    }

    /// <summary>F2 / double-click: enters inline source editing for the selected row.</summary>
    public void BeginSourceEditSelected()
    {
        if (SegmentList.SelectedItem is not BubbleRowViewModel row || row.IsReadOnly)
        {
            return;
        }

        row.StartSourceEdit();
        Dispatcher.UIThread.Post(() =>
        {
            var container = SegmentList.ContainerFromItem(row) as Visual;
            var editor = container?.GetVisualDescendants().OfType<PartTextEditor>()
                .FirstOrDefault(e => e.Name == "SourceEditor");
            editor?.FocusAndSelectAll();
        }, DispatcherPriority.Background);
    }

    private void WireEditor(PartTextEditor editor, BubbleRowViewModel row)
    {
        lock (_wired)
        {
            if (_wired.Contains(editor))
            {
                return;
            }

            _wired.Add(editor);
        }

        editor.ConfirmRequested += (_, _) => AdvanceOrConfirm(row, editor, review: false);
        editor.VariantConfirmRequested += (_, variant) =>
            AdvanceOrConfirm(row, editor, review: variant == ConfirmVariant.CtrlShiftEnter);
        editor.CopySourceRequested += (_, _) =>
        {
            if (editor.DataContext is PartViewModel part)
            {
                Confirm?.CopySourceToSelectedPart(part.Index);
            }
        };
        editor.ToggleLockRequested += (_, _) => Confirm?.ToggleLockSelected();
        editor.BoundaryCrossRequested += (_, dir) => OnEditorBoundaryCross(row, editor, dir);
        editor.GotFocus += (_, _) =>
        {
            _lastFocusedEditor = editor;
            SelectRowFor(editor, row);
        };
        editor.KeyDown += (_, e) =>
        {
            if (!PigComic.App.KeyBindings.IsNextPart(e) &&
                !PigComic.App.KeyBindings.IsPrevPart(e))
            {
                return;
            }

            if (editor.DataContext is not PartViewModel part)
            {
                return;
            }

            var target = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? part.Index - 1 : part.Index + 1;
            e.Handled = true;
            if (target < 1 || target > row.Parts.Count)
            {
                // SPEC §14.3: at the last/first part, Tab moves focus nowhere. The spec's
                // beep is intentionally omitted (System.Windows.Extensions is Windows-only);
                // the move simply does not happen.
                return;
            }

            FocusPartEditor(row, target);
        };
    }

    /// <summary>
    /// D-60: ArrowUp/Down crossing a textbox border moves focus to the prev/next textbox
    /// (prev/next part within the row, or the prev/next bubble's nearest part), and the
    /// caret lands so the user can keep editing immediately.
    /// </summary>
    private void OnEditorBoundaryCross(BubbleRowViewModel row, PartTextEditor editor, BoundaryDirection dir)
    {
        if (editor.DataContext is PartViewModel part)
        {
            // Within the same row: move to the prev/next part.
            var target = dir == BoundaryDirection.Up ? part.Index - 1 : part.Index + 1;
            if (target >= 1 && target <= row.Parts.Count)
            {
                FocusPartEditor(row, target);
                return;
            }
        }

        // Cross to the prev/next bubble's nearest part.
        if (DataContext is SegmentListViewModel vm)
        {
            vm.MoveSelection(dir == BoundaryDirection.Up ? -1 : 1);
            var next = vm.SelectedBubble;
            if (next is null)
            {
                return;
            }

            // Land on the last part when going up, the first part when going down.
            var targetPart = dir == BoundaryDirection.Up ? next.Parts.Count : 1;
            Dispatcher.UIThread.Post(() => FocusPartEditor(next, targetPart), DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// D-52 part-walk: a confirm gesture (Ctrl+Enter / Ctrl+Shift+Enter) on a non-last
    /// part just moves focus to the next part editor within the same bubble — no status
    /// change, no TM write. Only confirming the LAST part commits the bubble (SPEC §14.4)
    /// and advances to the next bubble's first part. <see cref="PartViewModel.Index"/> is
    /// 1-based, so "last" is <c>Index == Parts.Count</c>.
    /// </summary>
    private void AdvanceOrConfirm(BubbleRowViewModel row, PartTextEditor editor, bool review)
    {
        if (editor.DataContext is PartViewModel part && part.Index < row.Parts.Count)
        {
            FocusPartEditor(row, part.Index + 1);
            return;
        }

        Confirm?.ConfirmAndMove(review, Confirm?.SkipConfirmed ?? true);
    }

    /// <summary>Inserts text at the caret of the focused part editor (TB insert, SPEC §9).</summary>
    public void InsertAtCaret(string term)
    {
        var editor = _lastFocusedEditor is not null ? _lastFocusedEditor : FirstEditorOfSelected();
        if (editor is null)
        {
            return;
        }

        var current = editor.Text ?? "";
        var idx = Math.Clamp(editor.CaretIndex, 0, current.Length);
        editor.Text = current[..idx] + term + current[idx..];
        editor.CaretIndex = idx + term.Length;
    }

    private PartTextEditor? FirstEditorOfSelected()
        => FindPartEditorOfRow(SegmentList.SelectedItem as BubbleRowViewModel, 1);

    /// <summary>
    /// Finds the target part editor for a 1-based part index under a row container. The
    /// inline source editor (x:Name="SourceEditor") is excluded — it is collapsed unless
    /// F2 source editing is active, and <see cref="GetVisualDescendants"/> returns collapsed
    /// controls too. Without this filter, focus-after-confirm landed on the hidden source
    /// editor and the caret stayed in the old bubble.
    /// </summary>
    private static PartTextEditor? FindPartEditor(Visual? container, int partIndex1Based)
        => container?.GetVisualDescendants().OfType<PartTextEditor>()
            .FirstOrDefault(e => e.Name != "SourceEditor" &&
                                 e.DataContext is PartViewModel p && p.Index == partIndex1Based);

    /// <summary>Public finder (smoke / future keyboard routing).</summary>
    public PartTextEditor? FindPartEditorOfRow(BubbleRowViewModel? row, int partIndex1Based)
        => row is null ? null : FindPartEditor(SegmentList.ContainerFromItem(row) as Visual, partIndex1Based);

    /// <summary>Test/smoke hook: the target editor <see cref="FocusFirstPartOfSelected"/> would focus.</summary>
    public PartTextEditor? FindFirstTargetEditorOfSelected()
        => FindPartEditorOfRow(SegmentList.SelectedItem as BubbleRowViewModel, 1);

    /// <summary>The part editor that currently holds focus (TB-insert target, smoke hook).</summary>
    public PartTextEditor? LastFocusedEditor => _lastFocusedEditor;

    /// <summary>Focuses a specific part editor within a row (D-52 part-walk).</summary>
    public void FocusPartEditor(BubbleRowViewModel row, int partIndex1Based)
    {
        var editor = FindPartEditorOfRow(row, partIndex1Based);
        if (editor is not null)
        {
            FocusEditor(editor);
            return;
        }

        // The inner ItemsControl may not have realized the container yet — retry once.
        Dispatcher.UIThread.Post(() =>
        {
            var e = FindPartEditorOfRow(row, partIndex1Based);
            if (e is not null)
            {
                FocusEditor(e);
            }
        }, DispatcherPriority.Background);
    }

    private void FocusEditor(PartTextEditor editor)
    {
        // Set the TB-insert target synchronously; Focus()/GotFocus may be asynchronous in
        // headless hosts, and the part-walk's contract is "focus moved to this editor".
        _lastFocusedEditor = editor;
        editor.FocusAndSelectAll();
    }

    private void SelectRowFor(PartTextEditor editor, BubbleRowViewModel row)
    {
        if (editor.IsFocused && DataContext is SegmentListViewModel vm && vm.SelectedBubble != row)
        {
            vm.SelectBubbleId(row.Id);
        }
    }

    /// <summary>
    /// After a confirm the selection moves; scrolls the new row into view and
    /// focuses its first part editor (SPEC §14.4: "move selection to the next
    /// bubble and focus its first part editor"). The parts <c>ItemsControl</c>
    /// realizes its containers asynchronously, so the target editor may not be in
    /// the visual tree on the first frame after <c>ScrollIntoView</c> — retry a
    /// few times before giving up.
    /// </summary>
    public void FocusFirstPartOfSelected()
    {
        if (SegmentList.SelectedItem is null)
        {
            return;
        }

        SegmentList.ScrollIntoView(SegmentList.SelectedItem);
        TryFocusTargetEditor(0);
    }

    private void TryFocusTargetEditor(int attempt)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (SegmentList.SelectedItem is not BubbleRowViewModel row)
            {
                return;
            }

            var editor = FindPartEditor(SegmentList.ContainerFromItem(row) as Visual, 1);
            if (editor is not null)
            {
                FocusEditor(editor);
                return;
            }

            if (attempt < 5)
            {
                TryFocusTargetEditor(attempt + 1);
            }
        }, DispatcherPriority.Background);
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SegmentListViewModel vm)
        {
            return;
        }

        if (e.Key is Key.Up && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.MoveSelection(-1);
            e.Handled = true;
        }
        else if (e.Key is Key.Down && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.MoveSelection(1);
            e.Handled = true;
        }
    }
}