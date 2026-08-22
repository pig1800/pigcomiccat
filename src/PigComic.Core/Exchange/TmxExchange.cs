using System.Xml.Linq;
using PigComic.Core.Adapters;
using PigComic.Core.Tm;

namespace PigComic.Core.Exchange;

/// <summary>
/// SPEC §19 TMX 1.4b exchange. Export: <c>header srclang</c> = project source
/// language; one <c>tu</c> per entry with two <c>tuv xml:lang</c>/<c>seg</c>;
/// context props x-character / x-kind / x-chapter. Import: language match by
/// primary subtag (mismatch → error listing found languages); inline tags
/// counted for the tag-stripped warning; entries upserted per §7.1 with the
/// character from the x-character prop when present; grams rebuilt afterwards.
/// </summary>
public sealed class TmxExchange : ITmExchange
{
    internal const string TmxNamespace = "http://www.lisa.org/tmx14";

    private static readonly HashSet<string> InlineTags =
    [
        "bpt", "ept", "it", "ph", "hi", "sub", "ut",
    ];

    private static string? Seg(XElement tuv)
    => tuv.Elements().FirstOrDefault(e => e.Name.LocalName == "seg")?.Value;

    private static bool IsInlineTag(string localName) => InlineTags.Contains(localName);

    private static bool LanguageIs(XElement tuv, string lang)
    {
        var value = tuv.Attribute(XNamespace.Xml + "lang")?.Value ?? "";
        return LangClass.Get(value) == LangClass.Get(lang);
    }

    private static string? TuProp(XElement tu, string name)
        => tu.Elements().FirstOrDefault(e => e.Name.LocalName == "prop" &&
                                             (e.Attribute("type")?.Value ?? "") == name)?.Value;

    private static string? SegText(XElement tuv)
    => tuv.Elements().FirstOrDefault(e => e.Name.LocalName == "seg") is { } seg
        ? string.Concat(seg.Nodes().Select(InlineText))
        : null;

    /// <summary>Text of a seg node with inline tags (bpt/ept/…) removed entirely.</summary>
    private static string InlineText(XNode node)
    {
        if (node is XText text)
        {
            return text.Value;
        }

        if (node is XElement el)
        {
            if (IsInlineTag(el.Name.LocalName))
            {
                return ""; // strip the tag and its markup content
            }

            return string.Concat(el.Nodes().Select(InlineText));
        }

        return "";
    }

    public async Task<ImportReport> ImportAsync(string path, TmStore tm, CancellationToken ct)
    {
        try
        {
            return await Task.Run<ImportReport>(async () =>
            {
                ct.ThrowIfCancellationRequested();
                XDocument doc;
                try
                {
                    doc = XDocument.Load(path);
                }
                catch (Exception ex)
                {
                    return ImportReport.Fail($"Cannot read TMX: {ex.Message}");
                }

                var root = doc.Root;
                if (root?.Name.LocalName != "tmx")
                {
                    return ImportReport.Fail("Not a TMX file (root element must be <tmx>).");
                }

                var header = root.Elements().FirstOrDefault(e => e.Name.LocalName == "header");
                var foundLangs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    header?.Attribute("srclang")?.Value ?? "",
                };
                foreach (var tuv in root.Descendants().Where(e => e.Name.LocalName == "tuv"))
                {
                    var lang = tuv.Attribute(XNamespace.Xml + "lang")?.Value;
                    if (lang is not null)
                    {
                        foundLangs.Add(lang);
                    }
                }

                if (!foundLangs.Contains(tm.SourceLanguage) || !foundLangs.Contains(tm.TargetLanguage))
                {
                    return ImportReport.Fail(
                        $"TMX language mismatch: found {string.Join(", ", foundLangs.Where(l => l.Length > 0))}; " +
                        $"store expects source {tm.SourceLanguage} / target {tm.TargetLanguage}.");
                }

                var added = 0;
                var updated = 0;
                var skipped = 0;
                var tagStripped = 0;
                foreach (var tu in root.Descendants().Where(e => e.Name.LocalName == "tu"))
                {
                    ct.ThrowIfCancellationRequested();
                    var tuvs = tu.Elements().Where(e => e.Name.LocalName == "tuv").ToList();
                    var srcTuv = tuvs.FirstOrDefault(t => LanguageIs(t, tm.SourceLanguage));
                    var tgtTuv = tuvs.FirstOrDefault(t => LanguageIs(t, tm.TargetLanguage));
                    if (srcTuv is null || tgtTuv is null)
                    {
                        skipped++;
                        continue;
                    }

                    var srcSeg = SegText(srcTuv) ?? "";
                    var tgtSeg = SegText(tgtTuv) ?? "";
                    if (srcTuv.Descendants().Any(d => IsInlineTag(d.Name.LocalName)) ||
                        tgtTuv.Descendants().Any(d => IsInlineTag(d.Name.LocalName)))
                    {
                        tagStripped++;
                    }

                    var character = TuProp(tu, "x-character");
                    if (string.IsNullOrWhiteSpace(character))
                    {
                        character = null;
                    }

                    var kind = TuProp(tu, "x-kind");
                    var chapter = TuProp(tu, "x-chapter");

                    var norm = Normalizer.Normalize(srcSeg, tm.SourceLanguage);
                    var exists = tm.AllEntries().Any(e =>
                        e.SourceHash == TmHash.Compute(norm) &&
                        (e.Character ?? "") == (character ?? ""));
                    var result = await tm.UpsertAsync(
                        srcSeg, tgtSeg, character, kind, chapter, null, null, ct);
                    if (result is null)
                    {
                        skipped++;
                    }
                    else if (exists)
                    {
                        updated++;
                    }
                    else
                    {
                        added++;
                    }
                }

                await tm.RebuildGramsAsync(ct);
                return new ImportReport(added, updated, skipped, tagStripped);
            }, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ImportReport.Fail(ex.Message);
        }
    }

    public async Task ExportAsync(string path, TmStore tm, CancellationToken ct)
        => await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            XName E(string local) => XName.Get(local, TmxNamespace);

            var root = new XElement(E("tmx"),
                new XElement(E("header"),
                    new XAttribute("srclang", tm.SourceLanguage),
                    new XAttribute("creationtool", "PigComic"),
                    new XAttribute("datatype", "unknown")),
                new XElement(E("body")));
            var body = root.Element(E("body"))!;

            foreach (var entry in tm.AllEntries())
            {
                var tu = new XElement(E("tu"), new XAttribute("tuid", entry.Id.ToString()));
                if (entry.Character is not null)
                {
                    tu.Add(new XElement(E("prop"), new XAttribute("type", "x-character"), entry.Character));
                }

                if (entry.Kind is not null)
                {
                    tu.Add(new XElement(E("prop"), new XAttribute("type", "x-kind"), entry.Kind));
                }

                if (entry.Chapter is not null)
                {
                    tu.Add(new XElement(E("prop"), new XAttribute("type", "x-chapter"), entry.Chapter));
                }

                tu.Add(new XElement(E("tuv"), new XAttribute(XNamespace.Xml + "lang", tm.SourceLanguage),
                    new XElement(E("seg"), entry.SourceRaw)));
                tu.Add(new XElement(E("tuv"), new XAttribute(XNamespace.Xml + "lang", tm.TargetLanguage),
                    new XElement(E("seg"), entry.TargetRaw)));
                body.Add(tu);
            }

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var settings = new System.Xml.XmlWriterSettings { Indent = true };
            using var stream = File.Create(path);
            using var writer = System.Xml.XmlWriter.Create(stream, settings);
            root.Save(writer);
        }, ct);
}