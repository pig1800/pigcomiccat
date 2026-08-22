using System.Xml.Linq;
using PigComic.Core.Adapters;
using PigComic.Core.Tb;

namespace PigComic.Core.Exchange;

/// <summary>
/// SPEC §19 TBX (TBX-Basic dialect) exchange. Export: one <c>termEntry</c> per
/// source term (grouping synonym rows with the same source term), langSet per
/// language, tig/term; forbidden rows carry
/// <c>termNote type="administrativeStatus"&gt;deprecatedTerm-admn-sts</c> on the
/// target term (D-22). Import accepts TBX-Basic and TBX-Min; the
/// deprecatedTerm-admn-sts marker (or a "forbidden" note) sets forbidden=1.
/// </summary>
public sealed class TbxExchange : ITbExchange
{
    private const string TbxNs = "urn:iso:std:iso:30042:ed-1";
    private const string DeprecatedMarker = "deprecatedTerm-admn-sts";

    private static XName E(string local) => XName.Get(local, TbxNs);

    public async Task<ImportReport> ImportAsync(string path, TbStore tb, CancellationToken ct)
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
                    return ImportReport.Fail($"Cannot read TBX: {ex.Message}");
                }

                if (doc.Root?.Name.LocalName != "martif")
                {
                    return ImportReport.Fail("Not a TBX file (root element must be <martif>).");
                }

                var added = 0;
                var skipped = 0;
                var updated = 0;
                foreach (var entry in doc.Descendants().Where(e => e.Name.LocalName == "termEntry"))
                {
                    ct.ThrowIfCancellationRequested();
                    var langSets = entry.Elements().Where(e => e.Name.LocalName == "langSet").ToList();
                    var src = langSets.FirstOrDefault(l => LangMatches(l, tb.SourceLanguage));
                    var tgt = langSets.FirstOrDefault(l => LangMatches(l, tb.TargetLanguage));
                    if (src is null || tgt is null)
                    {
                        skipped++;
                        continue;
                    }

                    var sourceTerm = TermText(src);
                    var targetTerm = TermText(tgt);
                    if (sourceTerm is null || targetTerm is null)
                    {
                        skipped++;
                        continue;
                    }

                    var forbidden = IsDeprecated(tgt);
                    var notes = tgt.Elements().FirstOrDefault(e => e.Name.LocalName == "desc")?.Value ?? "";

                    var exists = tb.All().Any(t => t.SourceTerm == sourceTerm);
                    await tb.UpsertAsync(sourceTerm, forbidden ? "" : targetTerm, forbidden, notes, ct);
                    if (exists)
                    {
                        updated++;
                    }
                    else
                    {
                        added++;
                    }
                }

                return new ImportReport(added, updated, skipped);
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

    public async Task ExportAsync(string path, TbStore tb, CancellationToken ct)
        => await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var root = new XElement(E("martif"), new XAttribute("type", "TBX-Basic"));
            root.Add(new XElement(E("martifHeader"),
                new XElement(E("fileDesc"),
                    new XElement(E("sourceDesc"),
                        new XElement(E("p"), "PigComic TB export")))));
            var text = new XElement(E("text"), new XElement(E("body")));
            root.Add(text);

            // Group rows by source term (synonyms share a termEntry).
            var grouped = tb.All().GroupBy(t => t.SourceTerm, StringComparer.Ordinal);
            foreach (var group in grouped)
            {
                if (group.Key.Length == 0)
                {
                    continue; // unconditional forbidden rows are not exported
                }

                var entry = new XElement(E("termEntry"));
                entry.Add(new XElement(E("langSet"), new XAttribute(XNamespace.Xml + "lang", tb.SourceLanguage),
                    new XElement(E("tig"), new XElement(E("term"), group.Key))));
                var tgtLangSet = new XElement(E("langSet"), new XAttribute(XNamespace.Xml + "lang", tb.TargetLanguage));
                foreach (var row in group)
                {
                    var tig = new XElement(E("tig"));
                    if (row.Forbidden)
                    {
                        var target = new XElement(E("term"), row.TargetTerm);
                        target.Add(new XElement(E("termNote"), new XAttribute("type", "administrativeStatus"),
                            DeprecatedMarker));
                        tig.Add(target);
                    }
                    else
                    {
                        tig.Add(new XElement(E("term"), row.TargetTerm));
                    }

                    tgtLangSet.Add(tig);
                }

                entry.Add(tgtLangSet);
                text.Element(E("body"))!.Add(entry);
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

    private static bool LangMatches(XElement langSet, string lang)
    {
        var v = langSet.Attribute(XNamespace.Xml + "lang")?.Value ?? "";
        return PigComic.Core.Tm.LangClass.Get(v) == PigComic.Core.Tm.LangClass.Get(lang);
    }

    private static string? TermText(XElement langSet)
    {
        var tig = langSet.Elements().FirstOrDefault(e => e.Name.LocalName == "tig");
        var term = tig?.Elements().FirstOrDefault(e => e.Name.LocalName == "term");
        return term?.Value;
    }

    private static bool IsDeprecated(XElement langSet)
    {
        foreach (var note in langSet.Descendants().Where(e => e.Name.LocalName == "termNote"))
        {
            if ((note.Attribute("type")?.Value ?? "") == "administrativeStatus" &&
                (note.Value ?? "") == DeprecatedMarker)
            {
                return true;
            }

            if ((note.Attribute("type")?.Value ?? "") == "forbidden")
            {
                return true;
            }
        }

        foreach (var tig in langSet.Elements().Where(e => e.Name.LocalName == "tig"))
        {
            if (tig.Attribute("status")?.Value == "forbidden")
            {
                return true;
            }
        }

        return false;
    }
}

internal static class X11
{
    public static XAttribute Lang(string v) => new(XNamespace.Xml + "lang", v);
}