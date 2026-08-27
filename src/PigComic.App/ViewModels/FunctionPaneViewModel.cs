using CommunityToolkit.Mvvm.ComponentModel;
using PigComic.App.Services;
using PigComic.Core.Domain;
using PigComic.Core.Tb;
using PigComic.Core.Tm;

namespace PigComic.App.ViewModels;

/// <summary>
/// M7 function pane (SPEC §14.5): the TM/TB results box (fills), the kind selector,
/// the character box, notes and the LLM comment. Owns the child view-models and mirrors
/// the SELECTED bubble's kind/notes/llmComment into editable state; edits write through
/// to the row (Core mutation + dirty), never to the model directly.
/// </summary>
public partial class FunctionPaneViewModel : ObservableObject
{
    private readonly SegmentListViewModel _segments;

    public MatchListViewModel Matches { get; }

    public CharacterBoxViewModel Characters { get; }

    /// <summary>Kind toggle options in BubbleKind enum order (SPEC §14.5 item 2).</summary>
    public IReadOnlyList<BubbleKind> KindOptions { get; } = Enum.GetValues<BubbleKind>();

    public FunctionPaneViewModel(
        ChapterSession session, SegmentListViewModel segments, TmStore? tm, TbStore? tb, string? projectFolder)
    {
        _segments = segments;
        Matches = new MatchListViewModel(session, segments, tm, tb);
        Characters = new CharacterBoxViewModel(session, segments, projectFolder);
        segments.SelectionChanged += OnSelectionChanged;
    }

    /// <summary>The raw kind string (the 6 known values or an arbitrary one, D-59).</summary>
    [ObservableProperty]
    private string _kind = "";

    [ObservableProperty]
    private string _notes = "";

    [ObservableProperty]
    private string _llmComment = "";

    public bool HasLlmComment => LlmComment.Length > 0;

    partial void OnLlmCommentChanged(string value) => OnPropertyChanged(nameof(HasLlmComment));

    private void OnSelectionChanged(BubbleRowViewModel? row)
    {
        if (row is null)
        {
            Kind = "";
            Notes = "";
            LlmComment = "";
            return;
        }

        Kind = row.Bubble.KindRaw;
        Notes = row.Bubble.Notes;
        LlmComment = row.Bubble.LlmComment;
    }

    partial void OnKindChanged(string value) => _segments.SelectedBubble?.SetKindRaw(value);

    partial void OnNotesChanged(string value) => _segments.SelectedBubble?.SetNotes(value);

    public void ClearLlmComment()
    {
        _segments.SelectedBubble?.ClearLlmComment();
        LlmComment = "";
    }
}
