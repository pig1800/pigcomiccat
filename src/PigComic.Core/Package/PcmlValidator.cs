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
            ValidateImages(doc, root, issues);
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
        else if (v < 2)
        {
            // Version 1 was the paged, rectangle-region model. It never shipped — the
            // generator that emits .pcml has not been built — so there is nothing to
            // migrate from and guessing would silently mangle coordinates (D-49, D-50).
            Error(issues, "PCML-E02",
                $"@version={v} is the pre-strip schema (pages and rectangle regions); " +
                "PigComic requires version 2. Regenerate the package.");
        }
        else if (v > 2)
        {
            Error(issues, "PCML-E02", $"@version={v} is newer than supported schema version 2; opening read-only.");
        }
    }

    // ---------------------------------------------------------------- images

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

    private static void ValidateImages(PcmlDocument doc, XElement root, List<PcmlIssue> issues)
    {
        var imagesEl = root.Element("images");
        if (imagesEl is null)
        {
            Error(issues, "PCML-E01", "Missing required element <images>.");
            return;
        }

        var imageEls = imagesEl.Elements("image").ToList();
        if (imageEls.Count == 0)
        {
            // A chapter with no strip has nothing to position markers against (D-49).
            Error(issues, "PCML-E04", "<images> must contain at least one <image>.");
            return;
        }

        foreach (var image in imageEls)
        {
            var file = (string?)image.Attribute("file") ?? "";
            if (image.Attribute("file") is null)
            {
                Error(issues, "PCML-E01", "Missing required attribute @file on <image>.");
            }
            else if (file.Contains('/') || file.Contains('\\'))
            {
                Error(issues, "PCML-E05", $"Image '{file}': @file must not contain a path separator.");
            }

            CheckPositiveIntAttr(image, "width", $"Image '{file}'", issues);
            CheckPositiveIntAttr(image, "height", $"Image '{file}'", issues);
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

            foreach (var attrName in new[] { "kind", "status", "order" })
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

            var kind = (string?)el.Attribute("kind");
            if (kind is not null && kind.Length == 0)
            {
                Error(issues, "PCML-E07", $"Bubble '{id}': empty @kind.");
            }
            // D-59: arbitrary kind values are allowed (the 6 known + custom); no longer an error.

            var status = (string?)el.Attribute("status");
            if (status is not null && !KnownStatuses.Contains(status))
            {
                Error(issues, "PCML-E07", $"Bubble '{id}': unknown status '{status}'.");
            }

            var markers = el.Elements("marker").ToList();
            if (markers.Count != 1)
            {
                Error(issues, "PCML-E01", $"Bubble '{id}': must have exactly one <marker> (found {markers.Count}).");
            }
            else
            {
                CheckMarkerAttrs(markers[0], id, issues);
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

            var marker = part.Elements("marker").ToList();
            if (marker.Count != 1)
            {
                Error(issues, "PCML-E01", $"Bubble '{id}' part {i + 1}: must have exactly one <marker> (found {marker.Count}).");
            }
            else
            {
                CheckMarkerAttrs(marker[0], $"{id} part {i + 1}", issues);
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

    private static void CheckMarkerAttrs(XElement marker, string where, List<PcmlIssue> issues)
    {
        foreach (var name in new[] { "x", "y" })
        {
            var attr = marker.Attribute(name);
            if (attr is null)
            {
                Error(issues, "PCML-E01", $"{where}: missing required marker attribute @{name}.");
            }
            else if (!int.TryParse(attr.Value, out _))
            {
                Error(issues, "PCML-E01", $"{where}: marker attribute @{name} must be an integer (got '{attr.Value}').");
            }
        }
    }

    // ---------------------------------------------------------------- fixes

    /// <summary>W01: duplicate @order values — renumber 1..n stably in memory (chapter-global, D-49).</summary>
    private static void FixW01(PcmlDocument doc, List<PcmlIssue> issues)
    {
        var orders = doc.Model.Bubbles.Select(b => b.Order).ToList();
        if (orders.Count == new HashSet<int>(orders).Count)
        {
            return;
        }

        Warning(issues, "PCML-W01", "Duplicate bubble @order values; renumbering in memory.");
        var ordered = doc.Model.Bubbles.OrderBy(b => b.Order).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i + 1;
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

    /// <summary>W03: marker outside the strip (D-49).</summary>
    private static void CheckW03(PcmlDocument doc, List<PcmlIssue> issues)
    {
        var height = doc.Model.StripHeight;
        var width = doc.Model.StripWidth;

        foreach (var bubble in doc.Model.Bubbles)
        {
            var m = bubble.Marker;
            if (m.X < 0 || m.Y < 0 || m.X >= width || m.Y >= height)
            {
                Warning(issues, "PCML-W03",
                    $"Bubble '{bubble.Id}': marker ({m.X},{m.Y}) is outside the {width}×{height} strip.");
            }
        }
    }

    /// <summary>E05/W04: image files vs media entries.</summary>
    private static void ValidateMedia(PcmlDocument doc, List<PcmlIssue> issues)
    {
        var referenced = new HashSet<string>();
        foreach (var image in doc.Model.Images)
        {
            var file = image.FileName;
            if (file.Length == 0)
            {
                continue;
            }

            if (file.Contains('/') || file.Contains('\\'))
            {
                Error(issues, "PCML-E05", $"Image '{file}': @file must not contain path separators.");
                continue;
            }

            referenced.Add(file);
            if (!doc.MediaEntries.Any(m => m.Name == $"media/{file}"))
            {
                Error(issues, "PCML-E05", $"Image file '{file}' is missing from media/.");
            }
        }

        foreach (var entry in doc.MediaEntries)
        {
            if (entry.Name.StartsWith("media/", StringComparison.Ordinal) &&
                !referenced.Contains(entry.Name["media/".Length..]))
            {
                Warning(issues, "PCML-W04", $"Media entry '{entry.Name}' is not referenced by any image.");
            }
        }
    }

    // ---------------------------------------------------------------- helpers

    private static void Error(List<PcmlIssue> issues, string code, string message)
        => issues.Add(new PcmlIssue(PcmlSeverity.Error, code, message));

    private static void Warning(List<PcmlIssue> issues, string code, string message)
        => issues.Add(new PcmlIssue(PcmlSeverity.Warning, code, message));
}