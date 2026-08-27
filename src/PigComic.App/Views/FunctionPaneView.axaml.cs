using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using PigComic.App.Controls;
using PigComic.App.ViewModels;
using PigComic.Core.Domain;

namespace PigComic.App.Views;

/// <summary>
/// M7 function pane (SPEC §14.5): TM/TB box on top, kind selector, character box, notes
/// and LLM comment below. Code-behind wires UI events (kind toggles, name field keys,
/// chapter buttons, master offer) into the <see cref="FunctionPaneViewModel"/>.
/// </summary>
public partial class FunctionPaneView : UserControl
{
    private bool _syncingKind;

    public FunctionPaneView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private FunctionPaneViewModel? PaneVm => DataContext as FunctionPaneViewModel;

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (PaneVm is { } vm)
        {
            vm.PropertyChanged += OnVmPropertyChanged;
            SyncKindButtons(vm.Kind);
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FunctionPaneViewModel.Kind) && PaneVm is { } vm)
        {
            SyncKindButtons(vm.Kind);
        }
    }

    private void SyncKindButtons(string rawKind)
    {
        _syncingKind = true;
        KindSpeech.IsChecked = rawKind == "Speech";
        KindThought.IsChecked = rawKind == "Thought";
        KindNarration.IsChecked = rawKind == "Narration";
        KindSfx.IsChecked = rawKind == "Sfx";
        KindSign.IsChecked = rawKind == "Sign";
        KindNote.IsChecked = rawKind == "Note";
        _syncingKind = false;
    }

    private void OnKindToggle(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_syncingKind || sender is not ToggleButton tb || !tb.IsChecked.GetValueOrDefault())
        {
            return;
        }

        if (tb.Tag is string tag && PaneVm is { } vm)
        {
            vm.Kind = tag;
        }
    }

    /// <summary>Ctrl+Shift+K focus jump (SPEC §14.6).</summary>
    public void FocusKind()
    {
        KindSpeech.Focus();
    }

    /// <summary>Ctrl+Shift+C focus jump: the character name field.</summary>
    public void FocusCharacter()
    {
        CharacterField.Focus();
        CharacterField.CaretIndex = CharacterField.Text?.Length ?? 0;
    }

    /// <summary>Ctrl+Shift+N focus jump: the notes field.</summary>
    public void FocusNotes()
    {
        NotesField.Focus();
        NotesField.CaretIndex = NotesField.Text?.Length ?? 0;
    }

    private void OnCharacterKeyDown(object? sender, KeyEventArgs e)
    {
        if (PaneVm is not { } vm)
        {
            return;
        }

        var box = vm.Characters;
        switch (e.Key)
        {
            case Key.Down when box.SuggestionsVisible:
                box.MoveHighlight(1);
                e.Handled = true;
                break;

            case Key.Up when box.SuggestionsVisible:
                box.MoveHighlight(-1);
                e.Handled = true;
                break;

            case Key.Escape:
                if (box.SuggestionsVisible)
                {
                    box.Query = "";
                    e.Handled = true;
                }

                break;
        }
    }

    private readonly HashSet<PartTextEditor> _nameFieldsWired = [];

    private void OnCharacterGotFocus(object? sender, FocusChangedEventArgs e)
    {
        // Enter commits the name: with EnterInsertsNewline=false (single-line) the field
        // fires ConfirmRequested on Enter. Hook it once per field instance.
        if (sender is PartTextEditor field && _nameFieldsWired.Add(field))
        {
            field.ConfirmRequested += (_, _) => PaneVm?.Characters.ApplyQuery();
        }
    }

    private void OnCharacterApplyClick(object? sender, RoutedEventArgs e)
        => PaneVm?.Characters.ApplyQuery();

    private void OnSuggestionTapped(object? sender, TappedEventArgs e)
    {
        var box = PaneVm?.Characters;
        if (box is not null && box.SelectedName.Length > 0)
        {
            box.ApplyQuery();
        }
    }

    private void OnChapterNameClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is string name)
        {
            PaneVm?.Characters.ApplyCharacter(name);
        }
    }

    private void OnEraseCharacterClick(object? sender, RoutedEventArgs e)
        => PaneVm?.Characters.EraseCharacter();

    /// <summary>Right-click → "Remove from chapter list" on the context-menu'd button.</summary>
    private void OnRemoveChapterNameClick(object? sender, RoutedEventArgs e)
    {
        // The context menu was attached to the chapter-name button; its DataContext is the name.
        if (ChapterNameMenu?.DataContext is string name)
        {
            PaneVm?.Characters.RemoveChapterName(name);
        }
    }

    /// <summary>Opens the master character editor from within the editor (D-58/direct entrance).</summary>
    private void OnOpenMasterClick(object? sender, RoutedEventArgs e)
        => OpenMasterRequested?.Invoke();

    /// <summary>Raised when the user wants the master editor opened (button or Ctrl+Shift+M).</summary>
    public event Action? OpenMasterRequested;

    private void OnOfferAddClick(object? sender, RoutedEventArgs e)
        => PaneVm?.Characters.AcceptMasterOffer();

    private void OnOfferDismissClick(object? sender, RoutedEventArgs e)
        => PaneVm?.Characters.DismissMasterOffer();

    private void OnClearLlmClick(object? sender, RoutedEventArgs e)
        => PaneVm?.ClearLlmComment();
}