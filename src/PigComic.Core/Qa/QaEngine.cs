using System.Text.RegularExpressions;
using PigComic.Core.Domain;
using PigComic.Core.Tb;
using PigComic.Core.Tm;

namespace PigComic.Core.Qa;

/// <summary>Mechanical QA rules of SPEC §12 — a tate-chū-yoko aware length counter.</summary>
public static class VisualLength
{
    /// <summary>
    /// SPEC §12.1 — ja (vertical convention): each maximal run of ASCII digits of length
    /// ≤ tcyMaxDigitRun counts as 1, longer runs count 1 per digit; all other code points
    /// count 1. zh/ko (horizontal): every code point counts 1.
    /// </summary>
    public static int Of(string line, string targetLang, int tcyMaxDigitRun = 3)
    {
        if (string.IsNullOrEmpty(line))
        {
            return 0;
        }

        if (LangClass.Get(targetLang) is not "ja")
        {
            var n = 0;
            foreach (var _ in line.EnumerateRunes())
            {
                n++;
            }

            return n;
        }

        var total = 0;
        var i = 0;
        var run = 0;
        while (i < line.Length)
        {
            var ch = line[i];
            if (ch is >= '0' and <= '9')
            {
                run++;
                i++;
                continue;
            }

            if (run > 0)
            {
                total += run <= tcyMaxDigitRun ? 1 : run;
                run = 0;
            }

            total++;
            i++;
        }

        if (run > 0)
        {
            total += run <= tcyMaxDigitRun ? 1 : run;
        }

        return total;
    }
}

/// <summary>
/// SPEC §12 — the mechanical QA engine. ⚡ rules run per bubble (on confirm); all
/// rules (incl. QA-UNTRANS) run per chapter / per project.
/// </summary>
public sealed class QaEngine
{
    public const string Empty = "QA-EMPTY";
    public const string Untranslated = "QA-UNTRANS";
    public const string Same = "QA-SAME";
    public const string Term = "QA-TERM";
    public const string Forbid = "QA-FORBID";
    public const string LineLen = "QA-LINELEN";
    public const string LineCount = "QA-LINECOUNT";
    public const string Trailing = "QA-TRAILING";
    public const string Bracket = "QA-BRACKET";

    private readonly QaConfig _config;
    private readonly TbStore? _tb;

    public QaEngine(QaConfig? config = null, TbStore? tb = null)
    {
        _config = config ?? new QaConfig();
        _tb = tb;
    }

    /// <summary>⚡ subset — runs the on-confirm rules for one bubble (excluding QA-UNTRANS).</summary>
    public IReadOnlyList<QaIssue> RunOnBubble(Bubble bubble, string sourceLang, string targetLang)
    {
        var issues = new List<QaIssue>();
        if (bubble is null)
        {
            return issues;
        }

        AddEmpty(bubble, issues);
        AddSame(bubble, sourceLang, targetLang, issues);
        AddTerm(bubble, sourceLang, targetLang, issues);
        AddForbid(bubble, targetLang, issues);
        AddLineLen(bubble, targetLang, issues);
        AddLineCount(bubble, issues);
        AddTrailing(bubble, issues);
        AddBracket(bubble, issues);
        return issues;
    }

    /// <summary>Full chapter run — every rule including QA-UNTRANS.</summary>
    public IReadOnlyList<QaIssue> RunOnChapter(Chapter chapter)
    {
        var issues = new List<QaIssue>();
        if (chapter is null)
        {
            return issues;
        }

        foreach (var bubble in chapter.Bubbles)
        {
            issues.AddRange(RunOnBubble(bubble, chapter.SourceLanguage, chapter.TargetLanguage));
            if (bubble.Status is BubbleStatus.Untranslated or BubbleStatus.Draft)
            {
                issues.Add(new QaIssue(
                    Untranslated, QaSeverity.Warning, bubble.Id, null,
                    $"Untranslated or Draft - \"{bubble.SourceText}\""));
            }
        }

        return issues;
    }

    /// <summary>Project run — each chapter in order, prefixing the chapter title.</summary>
    public IReadOnlyList<QaIssue> RunOnProject(IEnumerable<Chapter> chapters)
    {
        var issues = new List<QaIssue>();
        if (chapters is null)
        {
            return issues;
        }

        foreach (var chapter in chapters)
        {
            foreach (var issue in RunOnChapter(chapter))
            {
                issues.Add(issue with { Message = $"[{chapter.Title}] {issue.Message}" });
            }
        }

        return issues;
    }

    // ---------------------------------------------------------------- rules

    private void AddEmpty(Bubble bubble, List<QaIssue> issues)
    {
        if (bubble.Status < BubbleStatus.Translated)
        {
            return;
        }

        var joined = bubble.TargetJoined;
        if (string.IsNullOrWhiteSpace(joined))
        {
            issues.Add(new QaIssue(
                Empty, QaSeverity.Error, bubble.Id, null,
                "Confirmed bubble with an empty target"));
            return;
        }

        var parts = bubble.Parts;
        var anyEmpty = parts.Any(p => string.IsNullOrWhiteSpace(p.Text));
        var anyNonEmpty = parts.Any(p => !string.IsNullOrWhiteSpace(p.Text));
        if (anyEmpty && anyNonEmpty)
        {
            issues.Add(new QaIssue(
                Empty, QaSeverity.Error, bubble.Id, null,
                "One or more parts are empty while another is not"));
        }
    }

    private void AddSame(Bubble bubble, string sourceLang, string targetLang, List<QaIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(bubble.SourceText) ||
            string.IsNullOrWhiteSpace(bubble.TargetJoined))
        {
            return;
        }

        var exempt = _config.IdenticalExemptKindsValue;
        if (exempt.Count > 0 && exempt.Contains(bubble.KindRaw, StringComparer.Ordinal))
        {
            return;
        }

        var targetNorm = Normalizer.Normalize(bubble.TargetJoined, targetLang);
        var sourceNorm = Normalizer.Normalize(bubble.SourceText, sourceLang);
        if (!targetNorm.Equals(sourceNorm, StringComparison.Ordinal))
        {
            return;
        }

        issues.Add(new QaIssue(
            Same, QaSeverity.Warning, bubble.Id, null,
            "Target is identical to source"));
    }

    private void AddTerm(Bubble bubble, string sourceLang, string targetLang, List<QaIssue> issues)
    {
        if (_tb is null)
        {
            return;
        }

        var sourceHits = _tb.All().Where(t => !t.Forbidden && t.SourceTerm.Length > 0);
        foreach (var row in sourceHits)
        {
            if (!TermHitTester.ContainsTerm(bubble.SourceText, row.SourceTerm, sourceLang))
            {
                continue;
            }

            var anyTargetRow = _tb.All().Where(t => t.SourceTerm == row.SourceTerm)
                .Any(t => TermHitTester.ContainsTerm(bubble.TargetJoined, t.TargetTerm, targetLang));
            if (anyTargetRow)
            {
                continue;
            }

            issues.Add(new QaIssue(
                Term, QaSeverity.Error, bubble.Id, null,
                $"Untranslated term: \"{row.SourceTerm}\""));
        }
    }

    private void AddForbid(Bubble bubble, string targetLang, List<QaIssue> issues)
    {
        if (_tb is null)
        {
            return;
        }

        foreach (var row in _tb.All().Where(t => t.Forbidden))
        {
            if (TermHitTester.ContainsTerm(bubble.TargetJoined, row.TargetTerm, targetLang))
            {
                issues.Add(new QaIssue(
                    Forbid, QaSeverity.Error, bubble.Id, null,
                    $"Forbidden term \"{row.TargetTerm}\" in target"));
            }
        }
    }

    private void AddLineLen(Bubble bubble, string targetLang, List<QaIssue> issues)
    {
        for (var i = 0; i < bubble.Parts.Count; i++)
        {
            var part = bubble.Parts[i];
            var lines = part.Text.Split('\n');
            for (var li = 0; li < lines.Length; li++)
            {
                var len = VisualLength.Of(lines[li], targetLang, _config.TcyMaxDigitRun);
                if (len > _config.MaxCharsPerLine)
                {
                    issues.Add(new QaIssue(
                        LineLen, QaSeverity.Warning, bubble.Id, i,
                        $"Line {li + 1} in part {i + 1} is {len} chars (max {_config.MaxCharsPerLine})"));
                }
            }
        }
    }

    private void AddLineCount(Bubble bubble, List<QaIssue> issues)
    {
        for (var i = 0; i < bubble.Parts.Count; i++)
        {
            var count = bubble.Parts[i].Text.Split('\n').Length;
            if (count > _config.MaxLinesPerPart)
            {
                issues.Add(new QaIssue(
                    LineCount, QaSeverity.Warning, bubble.Id, i,
                    $"Part {i + 1} has {count} lines (max {_config.MaxLinesPerPart})"));
            }
        }
    }

    private void AddTrailing(Bubble bubble, List<QaIssue> issues)
    {
        var forbidden = _config.ForbiddenTrailingValue;
        if (forbidden.Count == 0)
        {
            return;
        }

        var joined = bubble.TargetJoined.TrimEnd();
        if (joined.Length == 0)
        {
            return;
        }

        var last = joined[^1];
        if (forbidden.Contains(last.ToString(), StringComparer.Ordinal))
        {
            issues.Add(new QaIssue(
                Trailing, QaSeverity.Warning, bubble.Id, null,
                $"Target ends with forbidden character \"{last}\""));
        }
    }

    private void AddBracket(Bubble bubble, List<QaIssue> issues)
    {
        foreach (var pair in _config.BracketPairsValue)
        {
            if (pair.Length < 2)
            {
                continue;
            }

            var open = pair[0];
            var close = pair[1];
            var depth = 0;
            var bad = false;
            foreach (var ch in bubble.TargetJoined)
            {
                if (ch == open)
                {
                    depth++;
                }
                else if (ch == close)
                {
                    depth--;
                    if (depth < 0)
                    {
                        bad = true;
                        break;
                    }
                }
            }

            if (bad || depth != 0)
            {
                issues.Add(new QaIssue(
                    Bracket, QaSeverity.Error, bubble.Id, null,
                    $"Unbalanced bracket pair \"{open}{close}\""));
            }
        }
    }
}