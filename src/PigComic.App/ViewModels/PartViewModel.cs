using CommunityToolkit.Mvvm.ComponentModel;

namespace PigComic.App.ViewModels;

/// <summary>
/// One target-part editor cell (SPEC §14.3): stacked parts, each a
/// PartTextEditor. Text changes flow back to the bubble via the owner row
/// (which demotes Untranslated → Draft, SPEC §14.4).
/// </summary>
public partial class PartViewModel : ObservableObject
{
    private readonly BubbleRowViewModel _owner;

    public PartViewModel(BubbleRowViewModel owner, int index, string text, bool isReadOnly)
    {
        _owner = owner;
        Index = index;
        _text = text;
        _editorReadOnly = isReadOnly;
    }

    /// <summary>1-based part index (SPEC §5.3).</summary>
    public int Index { get; }

    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    private bool _editorReadOnly;

    partial void OnTextChanged(string value) => _owner.OnPartTextChanged(this, value);
}