using PigComic.Core.Domain;
using PigComic.Core.Qa;

namespace PigComic.App.ViewModels;

/// <summary>
/// M8.3 real <see cref="IConfirmQa"/>: after every confirm, runs the ⚡ rules of
/// SPEC §12 on the confirmed bubble (QA-UNTRANS is on-demand only, never here).
/// Issues append to the QA panel and raise <see cref="IssuesFound"/> so the editor
/// can light the row's ⚡ icon. D-15: issues never block the confirm.
/// </summary>
public sealed class ConfirmQa : IConfirmQa
{
    private readonly QaEngine _engine;
    private readonly QaPanelViewModel _panel;
    private readonly string _sourceLang;
    private readonly string _targetLang;

    public ConfirmQa(QaEngine engine, QaPanelViewModel panel, string sourceLang, string targetLang)
    {
        _engine = engine;
        _panel = panel;
        _sourceLang = sourceLang;
        _targetLang = targetLang;
    }

    /// <summary>Raised for the confirmed bubble when the ⚡ run found at least one issue.</summary>
    public event Action<Bubble, IReadOnlyList<QaIssue>>? IssuesFound;

    public void RunOnBubble(Bubble bubble)
    {
        var issues = _engine.RunOnBubble(bubble, _sourceLang, _targetLang);
        if (issues.Count == 0)
        {
            return;
        }

        _panel.AppendIssues(issues);
        IssuesFound?.Invoke(bubble, issues);
    }
}