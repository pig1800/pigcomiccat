using System.Xml.Linq;
using PigComic.Core.Domain;

namespace PigComic.Core.Package;

/// <summary>
/// SPEC §5.7 validation. Returns issues and applies the two in-memory fixes:
/// W01 duplicate orders are renumbered (1..n, stable), W02 unknown characters
/// are auto-added to the chapter character list (element + model). Files with
/// Errors open read-only (PcmlDocument.IsReadOnly).
/// </summary>
public static class PcmlValidator
{
    private static readonly HashSet<string> KnownKinds =
    [
        "Speech", "Thought", "Narration", "Sfx", "Sign", "Note",
    ];

    private static readonly HashSet<string> KnownStatuses =
    [
        "Untranslated", "Draft", "Translated", "Reviewed", "Locked",
    ];

    private sealed class BubbleInfo
    {
        public List<Bubble> Bubbles { get; } = [];
        public Dictionary<string, List<Bubble>> ByPage { get; } = [];
    }

    public static IReadOnlyList<PcmlIssue> Validate(PcmlDocument doc)
    {
        var issues = new List<PcmlIssue>();
        var root = doc.ContentXml.Root;

        if (root is not null)
        {
            ValidateVersion(root, issues);
            ValidateMeta(root, issues);
            ValidateBubbles(doc, root, issues);
            ValidatePages(doc, root, issues);
            ValidateCharacters(root, issues);
            FixW01(doc, issues);
            FixW02(doc, issues);
            CheckW03(doc, issues);
            ValidateMedia(doc, issues);
        }

        var final = issues
            .OrderBy(i => i.IsError ? 0 : 1)
            .ThenBy(i => i.Code, StringComparer.Ordinal)
            .ToList();
        doc.Issues = final;
        doc.IsReadOnly = final.Any(i => i.IsError);
        return final;
    }

    // ---------------------------------------------------------------- root/meta

    private static void ValidateVersion(XElement root, List<PcmlIssue> issues)
    {
        var version = root.Attribute("version");
        if (version is null)
        {
            Error(issues, "PCML-E01", "<pcml> is missing required attribute @version.");
            return;
        }

        if (!int.TryParse(version.Value, out var v))
        {
            Error(issues, "PCML-E02", $"@version is not an integer: '{version.Value}'.");
        }
        else if (v > 1)
        {
            Error(issues, "PCML-E02", $"@version={v} is newer than supported schema version 1; opening read-only.");
        }
    }

    // ---------------------------------------------------------------- pages

    private static void ValidateMeta(XElement root, List<PcmlIssue> issues)
    {
        var meta = root.Element("meta");
        if (meta is null)
        {
            Error(issues, "PCML-E01", "Missing required element <meta>.");
            return;
        }

        foreach (var name in new[] { "title", "chapter", "sourceLanguage", "targetLanguage" })
        {
            if (meta.Element(name) is null)
            {
                Error(issues, "PCML-E01", $"Missing required element <meta/{name}>.");
            }
        }
    }

    private static void ValidatePages(PcmlDocument doc, XElement root, List<PcmlIssue> issues)
    {
        var pagesEl = root.Element("pages");
        if (pagesEl is null)
        {
            Error(issues, "PCML-E01", "Missing required element <pages>.");
            return;
        }

        var pageEls = pagesEl.Elements("page").ToList();
        if (pageEls.Count == 0)
        {
            Error(issues, "PCML-E01", "<pages> must contain at least one <page>.");
        }

        var seenIds = new HashSet<string>();
        foreach (var page in pageEls)
        {
            var id = (string?)page.Attribute("id") ?? "";
            if (id.Length == 0)
            {
                Error(issues, "PCML-E01", "Missing required attribute @id on <page>.");
            }
            else if (!seenIds.Add(id))
            {
                Error(issues, "PCML-E03", $"Duplicate page id '{id}'.");
            }

            if (page.Attribute("file") is null)
            {
                Error(issues, "PCML-E01", $"Page '{id}': missing required attribute @file.");
            }

            CheckPositiveIntAttr(page, "width", $"Page '{id}'", issues);
            CheckPositiveIntAttr(page, "height", $"Page '{id}'", issues);
        }
    }

    private static void CheckPositiveIntAttr(XElement el, string name, string where, List<PcmlIssue> issues)
    {
        var attr = el.Attribute(name);
        if (attr is null)
        {
            issues.Add(new PcmlIssue(PcmlSeverity.Error, "PCML-E01", $"{where}: missing required attribute @{name}."));
        }
        else if (!int.TryParse(attr.Value, out var v) || v < 1)
        {
            issues.Add(new PcmlIssue(PcmlSeverity.Error, "PCML-E01",
                $"{where}: @{name} must be an integer ≥ 1 (got '{attr.Value}')."));
        }
    }

    private static void ValidateCharacters(XElement root, List<PcmlIssue> issues)
    {
        if (root.Element("characters") is null)
        {
            Error(issues, "PCML-E01", "Missing required element <characters>.");
        }
    }

    // ---------------------------------------------------------------- bubbles

    private static void ValidateBubbles(PcmlDocument doc, XElement root, List<PcmlIssue> issues)
    {
        var bubblesEl = root.Element("bubbles");
        if (bubblesEl is null)
        {
            Error(issues, "PCML-E01", "Missing required element <bubbles>.");
            return;
        }

        var seenIds = new HashSet<string>();
        foreach (var el in bubblesEl.Elements("bubble"))
        {
            var id = (string?)el.Attribute("id") ?? "";
            if (el.Attribute("id") is null)
            {
                Error(issues, "PCML-E01", "Missing required attribute @id on <bubble>.");
            }
            else if (!seenIds.Add(id))
            {
                Error(issues, "PCML-E03", $"Duplicate bubble id '{id}'.");
            }

            foreach (var attrName in new[] { "page", "kind", "status", "order" })
            {
                if (el.Attribute(attrName) is null)
                {
                    Error(issues, "PCML-E01", $"Bubble '{id}': missing required attribute @{attrName}.");
                }
            }

            if (el.Attribute("order") is { } orderAttr &&
                (!int.TryParse(orderAttr.Value, out var o) || o < 1))
            {
                Error(issues, "PCML-E01",
                    $"Bubble '{id}': @order must be an integer ≥ 1 (got '{orderAttr.Value}').");
            }

            var pageId = (string?)el.Attribute("page");
            if (pageId is not null && !doc.Model.Pages.Any(p => p.Id == pageId))
            {
                Error(issues, "PCML-E04", $"Bubble '{id}' references unknown page '{pageId}'.");
            }

            var kind = (string?)el.Attribute("kind");
            if (kind is not null && !KnownKinds.Contains(kind))
            {
                Error(issues, "PCML-E07", $"Bubble '{id}': unknown kind '{kind}'.");
            }

            var status = (string?)el.Attribute("status");
            if (status is not null && !KnownStatuses.Contains(status))
            {
                Error(issues, "PCML-E07", $"Bubble '{id}': unknown status '{status}'.");
            }

            var regions = el.Elements("region").ToList();
            if (regions.Count != 1)
            {
                Error(issues, "PCML-E01", $"Bubble '{id}': must have exactly one <region> (found {regions.Count}).");
            }
            else
            {
                CheckRegionAttrs(regions[0], id, issues);
            }

            if (el.Element("source") is null)
            {
                Error(issues, "PCML-E01", $"Bubble '{id}': missing required element <source>.");
            }

            var target = el.Element("target");
            if (target is null)
            {
                Error(issues, "PCML-E01", $"Bubble '{id}': missing required element <target>.");
            }
            else
            {
                ValidateParts(target, id, issues);
            }

            if (el.Elements("notes").Count() > 1)
            {
                Error(issues, "PCML-E01", $"Bubble '{id}': at most one <notes> allowed.");
            }

            if (el.Elements("llmComment").Count() > 1)
            {
                Error(issues, "PCML-E01", $"Bubble '{id}': at most one <llmComment> allowed.");
            }
        }
    }

    private static void ValidateParts(XElement target, string id, List<PcmlIssue> issues)
    {
        var parts = target.Elements("part").ToList();
        if (parts.Count is 0 or > 3)
        {
            Error(issues, "PCML-E06", $"Bubble '{id}': target must have 1..3 <part> (found {parts.Count}).");
        }

        var contiguous = true;
        var seenIndex = new HashSet<int>();
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            var indexOk = part.Attribute("index") is { } idx &&
                          int.TryParse(idx.Value, out var index) &&
                          index == i + 1 &&
                          seenIndex.Add(index);
            if (!indexOk)
            {
                contiguous = false;
                continue;
            }

            var region = part.Elements("region").ToList();
            if (region.Count != 1)
            {
                Error(issues, "PCML-E01", $"Bubble '{id}' part {i + 1}: must have exactly one <region> (found {region.Count}).");
            }
            else
            {
                CheckRegionAttrs(region[0], $"{id} part {i + 1}", issues);
            }

            if (part.Element("text") is null)
            {
                Error(issues, "PCML-E01", $"Bubble '{id}' part {i + 1}: missing required element <text>.");
            }
        }

        if (!contiguous)
        {
            Error(issues, "PCML-E06", $"Bubble '{id}': part @index values must be contiguous from 1.");
        }
    }

    private static void CheckRegionAttrs(XElement region, string where, List<PcmlIssue> issues)
    {
        var intsOk = true;
        foreach (var name in new[] { "x", "y", "width", "height" })
        {
            var attr = region.Attribute(name);
            if (attr is null)
            {
                Error(issues, "PCML-E01", $"{where}: missing required region attribute @{name}.");
                intsOk = false;
            }
            else if (!int.TryParse(attr.Value, out _))
            {
                Error(issues, "PCML-E01", $"{where}: region attribute @{name} must be an integer (got '{attr.Value}').");
                intsOk = false;
            }
        }

        if (intsOk)
        {
            var w = int.Parse((string?)region.Attribute("width")!);
            var h = int.Parse((string?)region.Attribute("height")!);
            if (w < 1 || h < 1)
            {
                Error(issues, "PCML-E01", $"{where}: region width/height must be ≥ 1.");
            }
        }
    }

    // ---------------------------------------------------------------- fixes

    /// <summary>W01: duplicate orders within a page — renumber 1..n stably in memory.</summary>
    private static void FixW01(PcmlDocument doc, List<PcmlIssue> issues)
    {
        var pageOrder = new Dictionary<string, int>();
        for (var i = 0; i < doc.Model.Pages.Count; i++)
        {
            pageOrder.TryAdd(doc.Model.Pages[i].Id, i);
        }

        var byPage = doc.Model.Bubbles
            .GroupBy(b => b.PageId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (pageId, bubbles) in byPage.OrderBy(kv => pageOrder.TryGetValue(kv.Key, out var i) ? i : int.MaxValue))
        {
            var orders = bubbles.Select(b => b.Order).ToList();
            if (orders.Count == new HashSet<int>(orders).Count)
            {
                continue;
            }

            Warning(issues, "PCML-W01",
                $"Page '{pageId}': duplicate bubble @order values; renumbering in memory.");
            var ordered = bubbles.OrderBy(b => b.Order).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                ordered[i].Order = i + 1;
            }
        }
    }

    /// <summary>W02: @character not in the chapter list — auto-add (element + model).</summary>
    private static void FixW02(PcmlDocument doc, List<PcmlIssue> issues)
    {
        var added = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in doc.Model.Bubbles)
        {
            var name = b.Character;
            if (string.IsNullOrEmpty(name) || doc.Model.Characters.Contains(name) || !added.Add(name))
            {
                continue;
            }

            doc.Model.Characters.Add(name);
            doc.ContentXml.Root?.Element("characters")?.Add(new XElement("character", new XAttribute("name", name)));
            Warning(issues, "PCML-W02",
                $"Bubble '{b.Id}': character '{name}' not in <characters>; added to the chapter list.");
        }
    }

    /// <summary>W03: region fully outside its page bounds.</summary>
    private static void CheckW03(PcmlDocument doc, List<PcmlIssue> issues)
    {
        var pageById = new Dictionary<string, Page>();
        foreach (var p in doc.Model.Pages)
        {
            pageById.TryAdd(p.Id, p);
        }
        foreach (var bubble in doc.Model.Bubbles)
        {
            if (!pageById.TryGetValue(bubble.PageId, out var page))
            {
                continue;
            }

            var r = bubble.SourceRegion;
            var fullyOutside = r.X + r.Width <= 0 || r.Y + r.Height <= 0 || r.X >= page.Width || r.Y >= page.Height;
            if (fullyOutside)
            {
                Warning(issues, "PCML-W03",
                    $"Bubble '{bubble.Id}': region ({r.X},{r.Y},{r.Width}×{r.Height}) is fully outside page {page.Width}×{page.Height}.");
            }
        }
    }

    /// <summary>E05/W04: page files vs media entries.</summary>
    private static void ValidateMedia(PcmlDocument doc, List<PcmlIssue> issues)
    {
        var referenced = new HashSet<string>();
        foreach (var page in doc.Model.Pages)
        {
            var file = page.FileName;
            if (file.Length == 0)
            {
                continue;
            }

            if (file.Contains('/') || file.Contains('\\'))
            {
                Error(issues, "PCML-E05", $"Page '{page.Id}': @file must not contain path separators ('{file}').");
                continue;
            }

            referenced.Add(file);
            if (!doc.MediaEntries.Any(m => m.Name == $"media/{file}"))
            {
                Error(issues, "PCML-E05", $"Page file '{file}' is missing from media/.");
            }
        }

        foreach (var entry in doc.MediaEntries)
        {
            if (entry.Name.StartsWith("media/", StringComparison.Ordinal) &&
                !referenced.Contains(entry.Name["media/".Length..]))
            {
                Warning(issues, "PCML-W04", $"Media entry '{entry.Name}' is not referenced by any page.");
            }
        }
    }

    // ---------------------------------------------------------------- helpers

    private static void Error(List<PcmlIssue> issues, string code, string message)
        => issues.Add(new PcmlIssue(PcmlSeverity.Error, code, message));

    private static void Warning(List<PcmlIssue> issues, string code, string message)
        => issues.Add(new PcmlIssue(PcmlSeverity.Warning, code, message));
}