using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PigComic.App.Services;
using PigComic.Core.Domain;
using PigComic.Core.Package;

namespace PigComic.App.ViewModels;

/// <summary>
/// One segment-list row (SPEC §14.3/§14.4): the bubble, its display state and
/// its target part editors. Mutations go through Core (BubbleMutations /
/// write-through property setters, SPEC §5.8) and mark the session dirty.
/// </summary>
public partial class BubbleRowViewModel : ObservableObject
{
    private BubbleRowViewModel(Bubble bubble, ChapterSessionFacade session)
    {
        Bubble = bubble;
        _session = session;
    }

    public static BubbleRowViewModel Create(Bubble bubble, ChapterSessionFacade session)
    {
        var row = new BubbleRowViewModel(bubble, session);
        row.RefreshParts();
        return row;
    }

    public Bubble Bubble { get; }

    private readonly ChapterSessionFacade _session;

    /// <summary>Target part editor cells (1..3, SPEC §5.3).</summary>
    public ObservableCollection<PartViewModel> Parts { get; } = [];

    public string Id => Bubble.Id;
    public string PageId => Bubble.PageId;
    public int Order => Bubble.Order;

    public string KindText => Bubble.Kind.ToString();
    public string CharacterText => Bubble.Character ?? "";
    public string SourceText => Bubble.SourceText;

    /// <summary>Inline source edit (F2 / double-click, §14.3; Enter commits, Esc cancels).</summary>
    [ObservableProperty]
    private bool _isEditingSource;

    [ObservableProperty]
    private string _editSource = "";

    partial void OnIsEditingSourceChanged(bool value)
    {
        if (value)
        {
            EditSource = Bubble.SourceText;
        }
    }

    public void StartSourceEdit()
    {
        if (IsReadOnly)
        {
            return;
        }

        IsEditingSource = true;
        EditSource = Bubble.SourceText;
    }

    public void CommitSourceEdit()
    {
        if (!IsEditingSource)
        {
            return;
        }

        IsEditingSource = false;
        if (EditSource == Bubble.SourceText)
        {
            return;
        }

        // §14.3: source edits set no status change but mark dirty.
        BubbleMutations.SetSource(Bubble, EditSource);
        _session.MarkDirty();
        OnPropertyChanged(nameof(SourceText));
    }

    public void CancelSourceEdit() => IsEditingSource = false;

    public BubbleStatus Status => Bubble.Status;
    public bool IsLocked => Bubble.Status == BubbleStatus.Locked;
    public bool IsReadOnly => _session.IsReadOnly || IsLocked;

    /// <summary>Low-opacity row tint (SPEC §14.3 row background reflects status).</summary>
    public IBrush Tint => UiPalette.StatusTint(Status);

    public void MarkDirty() => _session.MarkDirty();

    /// <summary>Rebuilds the part cells from the bubble (used after split/insert).</summary>
    public void RefreshParts()
    {
        Parts.Clear();
        for (var i = 0; i < Bubble.Parts.Count; i++)
        {
            Parts.Add(new PartViewModel(this, i + 1, Bubble.Parts[i].Text, IsReadOnly));
        }

        OnPropertyChanged(nameof(Parts));
        NotifyStatus();
    }

    /// <summary>Part text edited: write through + Untranslated → Draft (SPEC §14.4).</summary>
    public void OnPartTextChanged(PartViewModel part, string text)
    {
        var core = Bubble.Parts[part.Index - 1];
        var changed = core.Text != text;
        if (!changed)
        {
            return;
        }

        core.Text = text;
        if (Bubble.Status == BubbleStatus.Untranslated)
        {
            BubbleMutations.SetStatus(Bubble, BubbleStatus.Draft);
        }

        _session.MarkDirty();
        NotifyStatus();
    }

    /// <summary>Applies a confirmation status (SPEC §14.4).</summary>
    public void ApplyStatus(BubbleStatus status)
    {
        if (Bubble.Status == status)
        {
            return;
        }

        BubbleMutations.SetStatus(Bubble, status);
        _session.MarkDirty();
        NotifyStatus();
    }

    /// <summary>Ctrl+L lock toggle with D-16 restore semantics.</summary>
    public void ToggleLocked()
    {
        if (Bubble.Status == BubbleStatus.Locked)
        {
            BubbleMutations.SetStatus(Bubble, Bubble.TargetJoined.Length > 0 ? BubbleStatus.Translated : BubbleStatus.Untranslated);
        }
        else if (Bubble.Status is BubbleStatus.Translated or BubbleStatus.Reviewed or BubbleStatus.Draft or BubbleStatus.Untranslated)
        {
            BubbleMutations.SetStatus(Bubble, BubbleStatus.Locked);
        }

        _session.MarkDirty();
        foreach (var p in Parts)
        {
            p.EditorReadOnly = IsReadOnly;
        }

        NotifyStatus();
    }

    /// <summary>Ctrl+Insert: replace the part's entire text with the source (SPEC §14.6).</summary>
    public void CopySourceToPart(int partIndex)
    {
        if (partIndex < 1 || partIndex > Parts.Count)
        {
            return;
        }

        if (Parts[partIndex - 1].Text == Bubble.SourceText)
        {
            return;
        }

        Parts[partIndex - 1].Text = Bubble.SourceText; // flows through OnPartTextChanged
    }

    private void NotifyStatus()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Tint));
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(IsReadOnly));
        _statusChanged?.Invoke();
    }

    private event Action? _statusChanged;

    /// <summary>Raised when status/parts change (overlay + status-bar refresh).</summary>
    public event Action? StatusChanged
    {
        add => _statusChanged += value;
        remove => _statusChanged -= value;
    }
}

/// <summary>Minimal session surface the row view-models need (UI-only; Core stays untouched).</summary>
public sealed class ChapterSessionFacade(ChapterSession session)
{
    public bool IsReadOnly => session.Document.IsReadOnly;

    public void MarkDirty() => session.MarkDirty();
}