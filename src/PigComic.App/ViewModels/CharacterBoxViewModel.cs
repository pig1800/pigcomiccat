using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PigComic.App.Services;
using PigComic.Core.Package;
using PigComic.Core.Project;

namespace PigComic.App.ViewModels;

/// <summary>
/// M7.2 character box (SPEC §14.5 item 3 / §16): a name field with suggestions over the
/// MASTER list (prefix-first, then substring), one-click chapter-name buttons, and the
/// "add to master" offer. Setting a speaker writes the bubble's @character and adds the
/// name to the chapter list (SetCharacter).
/// </summary>
public partial class CharacterBoxViewModel : ObservableObject
{
    private readonly ChapterSession _session;
    private readonly SegmentListViewModel _segments;
    private readonly CharacterList? _master;

    public CharacterBoxViewModel(ChapterSession session, SegmentListViewModel segments, string? projectFolder)
    {
        _session = session;
        _segments = segments;
        if (projectFolder is not null)
        {
            var path = Path.Combine(projectFolder, CharacterList.FileName);
            try
            {
                _master = File.Exists(path) ? CharacterList.Load(path) : CharacterList.CreateNew(path);
            }
            catch
            {
                _master = null; // a corrupt master list disables autocomplete, never the editor
            }
        }

        segments.SelectionChanged += _ => RefreshChapterNames();
        RefreshChapterNames();
    }

    /// <summary>Raised when the user accepts "Add 『X』 to master list?" — the editor opens the master window prefilled.</summary>
    public event Action<string>? AddToMasterRequested;

    /// <summary>Raised after a speaker change (the row's header cell must refresh).</summary>
    public event Action? CharacterChanged;

    /// <summary>The name field text (a PartTextEditor).</summary>
    [ObservableProperty]
    private string _query = "";

    /// <summary>Currently highlighted suggestion (Enter/click commits it).</summary>
    [ObservableProperty]
    private string _selectedName = "";

    [ObservableProperty]
    private IReadOnlyList<string> _suggestions = [];

    [ObservableProperty]
    private bool _suggestionsVisible;

    /// <summary>Every distinct name used in this chapter (chapter &lt;characters&gt; plus @character values).</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _chapterNames = [];

    /// <summary>Non-empty when the "Add 『X』 to master list?" prompt is showing.</summary>
    [ObservableProperty]
    private string _masterOfferName = "";

    public bool HasMasterOffer => MasterOfferName.Length > 0;

    partial void OnMasterOfferNameChanged(string value) => OnPropertyChanged(nameof(HasMasterOffer));

    public bool HasMaster => _master is not null;

    public IReadOnlyList<string> MasterNames
        => _master?.Characters.Select(c => c.Name).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToList()
           ?? [];

    partial void OnQueryChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Suggestions = [];
            SuggestionsVisible = false;
            SelectedName = "";
            return;
        }

        var masters = MasterNames;
        var prefix = masters.Where(n => n.StartsWith(value, StringComparison.OrdinalIgnoreCase));
        var rest = masters.Where(n => !n.StartsWith(value, StringComparison.OrdinalIgnoreCase) &&
                                      n.Contains(value, StringComparison.OrdinalIgnoreCase));
        var list = prefix.Concat(rest).ToList();
        Suggestions = list;
        SuggestionsVisible = list.Count > 0;
        SelectedName = list.FirstOrDefault() ?? "";
    }

    /// <summary>Commits the field: the highlighted suggestion, or the literal text as a new name (§14.5).</summary>
    public void ApplyQuery()
    {
        var name = SelectedName.Length > 0 ? SelectedName : Query.Trim();
        if (name.Length == 0)
        {
            return;
        }

        ApplyCharacter(name);
        Query = "";
        Suggestions = [];
        SuggestionsVisible = false;
    }

    /// <summary>Sets the selected bubble's speaker (chapter button or committed field).</summary>
    public void ApplyCharacter(string name)
    {
        var row = _segments.SelectedBubble;
        if (row is null || name.Length == 0)
        {
            return;
        }

        if (row.Bubble.Character != name)
        {
            BubbleMutations.SetCharacter(_session.Document, row.Bubble, name);
            row.MarkDirty();
        }

        row.NotifyCharacterChanged();
        CharacterChanged?.Invoke();
        RefreshChapterNames();

        // §14.5: offer to add a brand-new name to the master list.
        if (_master is not null && _master.Find(name) is null)
        {
            MasterOfferName = name;
        }
    }

    /// <summary>Moves the suggestion highlight (ArrowUp/Down).</summary>
    public void MoveHighlight(int delta)
    {
        if (Suggestions.Count == 0)
        {
            return;
        }

        var idx = Array.IndexOf(Suggestions.ToArray(), SelectedName);
        idx = Math.Clamp(idx + delta, 0, Suggestions.Count - 1);
        SelectedName = Suggestions[idx];
    }

    /// <summary>Not now — dismiss the master-list offer.</summary>
    public void DismissMasterOffer()
    {
        MasterOfferName = "";
    }

    /// <summary>Accept the offer: raise the open-master-editor event with the name prefilled.</summary>
    public void AcceptMasterOffer()
    {
        var name = MasterOfferName;
        MasterOfferName = "";
        if (name.Length > 0)
        {
            AddToMasterRequested?.Invoke(name);
        }
    }

    private void RefreshChapterNames()
    {
        var names = _session.Document.Model.Characters.ToList();
        foreach (var r in _segments.Items.OfType<BubbleRowViewModel>())
        {
            var c = r.CharacterText;
            if (c.Length > 0 && !names.Contains(c))
            {
                names.Add(c);
            }
        }

        ChapterNames = names;
    }
}
