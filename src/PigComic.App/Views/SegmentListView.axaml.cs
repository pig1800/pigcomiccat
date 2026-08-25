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

        editor.ConfirmRequested += (_, _) => Confirm?.ConfirmAndMove(review: false, skipConfirmed: false);
        editor.VariantConfirmRequested += (_, variant) => Confirm?.ConfirmAndMove(
            review: variant == ConfirmVariant.CtrlShiftEnter,
            skipConfirmed: variant == ConfirmVariant.CtrlEnter);
        editor.CopySourceRequested += (_, _) =>
        {
            if (editor.DataContext is PartViewModel part)
            {
                Confirm?.CopySourceToSelectedPart(part.Index);
            }
        };
        editor.ToggleLockRequested += (_, _) => Confirm?.ToggleLockSelected();
        editor.GotFocus += (_, _) =>
        {
            _lastFocusedEditor = editor;
            SelectRowFor(editor, row);
        };
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
    {
        if (SegmentList.SelectedItem is not BubbleRowViewModel row)
        {
            return null;
        }

        var container = SegmentList.ContainerFromItem(row) as Visual;
        return container?.GetVisualDescendants().OfType<PartTextEditor>().FirstOrDefault();
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
    /// bubble and focus its first part editor").
    /// </summary>
    public void FocusFirstPartOfSelected()
    {
        if (SegmentList.SelectedItem is null)
        {
            return;
        }

        SegmentList.ScrollIntoView(SegmentList.SelectedItem);
        Dispatcher.UIThread.Post(() =>
        {
            var row = SegmentList.SelectedItem as BubbleRowViewModel;
            if (row is null)
            {
                return;
            }

            var container = SegmentList.ContainerFromItem(row) as Visual;
            var editor = container?.GetVisualDescendants().OfType<PartTextEditor>().FirstOrDefault();
            editor?.FocusAndSelectAll();
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