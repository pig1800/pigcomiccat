using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PigComic.Core.Qa;

namespace PigComic.App.ViewModels;

/// <summary>One row of the QA panel (SPEC §12). Record — display-only, navigated by double-click.</summary>
public sealed record QaPanelRow(
    string RuleId,
    QaSeverity Severity,
    string BubbleId,
    int? PartIndex,
    string Message)
{
    public string Glyph => Severity == QaSeverity.Error ? "✖" : "⚠";

    public IBrush GlyphBrush => Severity == QaSeverity.Error ? Brushes.IndianRed : Brushes.DarkOrange;

    public string BubbleLabel => BubbleId + (PartIndex is { } p ? $" · part {p}" : "");
}

/// <summary>
/// M8.3 mechanical-QA results: the dockable bottom panel (F8 chapter run) and the
/// per-bubble marker data behind the ⚡ row icons (on-confirm ⚡ issues + F8).
/// </summary>
public partial class QaPanelViewModel : ObservableObject
{
    public ObservableCollection<QaPanelRow> Rows { get; } = [];

    /// <summary>Panel visibility (F8 shows it; the ✕ close button hides it).</summary>
    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string _summaryLabel = "QA";

    private readonly Dictionary<string, List<QaIssue>> _byBubble = [];

    /// <summary>Raised when a row is double-clicked (editor navigates to the bubble).</summary>
    public event Action<string>? NavigateRequested;

    /// <summary>F8: a full chapter run replaces the whole list and shows the panel.</summary>
    public void RunChapterResult(IReadOnlyList<QaIssue> issues)
    {
        Rows.Clear();
        _byBubble.Clear();
        Append(issues);
        SummaryLabel = Rows.Count == 0 ? "QA: no issues" : $"QA: {Rows.Count} issue(s)";
        IsVisible = true;
    }

    /// <summary>On-confirm ⚡ issues: appended to the running list (the panel stays as it was).</summary>
    public void AppendIssues(IReadOnlyList<QaIssue> issues)
    {
        if (issues.Count == 0)
        {
            return;
        }

        Append(issues);
        SummaryLabel = $"QA: {Rows.Count} issue(s)";
    }

    /// <summary>All issues currently known for a bubble id (row ⚡ icon data).</summary>
    public IReadOnlyList<QaIssue> IssuesFor(string bubbleId)
        => _byBubble.TryGetValue(bubbleId, out var list) ? list : [];

    public void Close() => IsVisible = false;

    public void RequestNavigate(string bubbleId) => NavigateRequested?.Invoke(bubbleId);

    private void Append(IReadOnlyList<QaIssue> issues)
    {
        foreach (var issue in issues)
        {
            Rows.Add(new QaPanelRow(issue.RuleId, issue.Severity, issue.BubbleId, issue.PartIndex, issue.Message));
            if (!_byBubble.TryGetValue(issue.BubbleId, out var list))
            {
                list = [];
                _byBubble[issue.BubbleId] = list;
            }

            list.Add(issue);
        }
    }
}