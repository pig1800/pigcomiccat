using System.IO.Compression;
using System.Xml.Linq;
using PigComic.Core.Domain;

namespace PigComic.Core.Package;

/// <summary>One entry inside a .pcml archive (media or unknown), enumerated but not decoded.</summary>
public sealed record MediaEntry(string Name, long Length);

/// <summary>
/// An open .pcml chapter package. The archive stays open for the document's
/// lifetime so saves can stream-copy media entries verbatim (SPEC §5.5).
/// Per SPEC §5.8 the loaded <see cref="ContentXml"/> XDocument IS the
/// persistence model; domain objects back onto it.
/// </summary>
public sealed class PcmlDocument : IDisposable
{
    private readonly ZipArchive _archive;

    private PcmlDocument(string path, ZipArchive archive, XDocument contentXml, Chapter model)
    {
        Path = path;
        _archive = archive;
        ContentXml = contentXml;
        Model = model;
    }

    public string Path { get; }

    public XDocument ContentXml { get; }

    public Chapter Model { get; internal set; }

    /// <summary>True when validation found Errors (§5.7) — file opens read-only.</summary>
    public bool IsReadOnly { get; internal set; }

    /// <summary>Validation issues (filled by <c>PcmlValidator</c>, M1.4).</summary>
    public IReadOnlyList<PcmlIssue> Issues { get; internal set; } = Array.Empty<PcmlIssue>();

    /// <summary>Sibling crash-recovery journal path (SPEC §23; written from M9).</summary>
    public string JournalPath => Path + ".journal";

    /// <summary>
    /// Rebuilds <see cref="Model"/> from the current DOM after structural mutations
    /// (bubble create/delete, part count changes). Page and character lists are
    /// rebuilt fresh; callers re-read <see cref="Model"/> after mutations.
    /// </summary>
    internal void RefreshModel()
    {
        Model = BuildModel(ContentXml);
    }

    /// <summary>Media + unknown zip entries enumerated by name and length.</summary>
    public IReadOnlyList<MediaEntry> MediaEntries
        => _archive.Entries.Select(e => new MediaEntry(e.FullName, e.Length)).ToList();

    private bool _disposed;

    /// <summary>
    /// Opens the package: reads <c>content.xml</c> into an XDocument and builds
    /// the domain model (bubbles sorted by page order, then Order).
    /// </summary>
    public static PcmlDocument Open(string path)
    {
        if (!File.Exists(path))
        {
            throw new PcmlLoadException($"Package not found: {path}");
        }

        try
        {
            // FileShare.ReadWrite|Delete so the atomic-save File.Replace can
            // replace the target while this handle is still open (SPEC §5.5).
            var archiveStream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            ZipArchive archive;
            try
            {
                archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
            }
            catch
            {
                archiveStream.Dispose();
                throw;
            }
            try
            {
                var contentEntry = archive.GetEntry("content.xml")
                    ?? throw new PcmlLoadException("Package has no content.xml entry.");
                XDocument xdoc;
                using (var contentStream = contentEntry.Open())
                {
                    xdoc = XDocument.Load(contentStream, LoadOptions.PreserveWhitespace);
                }

                if (xdoc.Root is null || xdoc.Root.Name.LocalName != "pcml")
                {
                    throw new PcmlLoadException("content.xml root element must be <pcml>.");
                }

                var model = BuildModel(xdoc);
                var doc2 = new PcmlDocument(path, archive, xdoc, model);
                PcmlValidator.Validate(doc2);
                return doc2;
            }
            catch
            {
                archive.Dispose();
                throw;
            }
        }
        catch (PcmlLoadException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            throw new PcmlLoadException($"Cannot open {path}: {ex.Message}", ex);
        }
    }

    public static Task<PcmlDocument> OpenAsync(string path, CancellationToken ct)
        => Task.Run(() => Open(path), ct);

    private static Chapter BuildModel(XDocument xdoc)
    {
        var root = xdoc.Root!;
        var meta = root.Element("meta");
        var title = meta?.Element("title")?.Value ?? "";
        var chapter = meta?.Element("chapter")?.Value ?? "";
        var srcLang = meta?.Element("sourceLanguage")?.Value ?? "";
        var tgtLang = meta?.Element("targetLanguage")?.Value ?? "";

        var names = new List<string>();
        if (root.Element("characters") is { } chars)
        {
            names.AddRange(chars.Elements("character").Select(c => (string?)c.Attribute("name") ?? ""));
        }

        var pages = new List<Page>();
        if (root.Element("pages") is { } pagesEl)
        {
            foreach (var pageEl in pagesEl.Elements("page"))
            {
                pages.Add(new Page(pageEl));
            }
        }

        var pageOrder = new Dictionary<string, int>();
        for (var i = 0; i < pages.Count; i++)
        {
            pageOrder.TryAdd(pages[i].Id, i);
        }
        var bubbles = new List<Bubble>();
        if (root.Element("bubbles") is { } bubblesEl)
        {
            foreach (var bubbleEl in bubblesEl.Elements("bubble"))
            {
                bubbles.Add(new Bubble(bubbleEl));
            }
        }

        var sorted = bubbles
            .OrderBy(b => pageOrder.TryGetValue(b.PageId, out var idx) ? idx : int.MaxValue)
            .ThenBy(b => b.Order)
            .ToList();

        return new Chapter(title, chapter, srcLang, tgtLang, names, pages, sorted);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _archive.Dispose();
    }

    // ---------------------------------------------------------------- save (SPEC §5.5)

    /// <summary>
    /// Atomic save: snapshot DOM on the caller thread, then write a temp archive
    /// (media/unknown entries stream-copied verbatim, bubbles sorted, CRLF→LF,
    /// xml:space="preserve" rule §5.3), then File.Replace onto the target with
    /// the previous version kept as .bak. Deletes the journal on success.
    /// </summary>
    public Task SaveAsync(CancellationToken ct)
        => SaveAsync(null, ct);

    internal Task SaveAsync(Action<ZipArchive>? midWriteHook, CancellationToken ct)
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException(
                "The package has validation errors and is open read-only; saving is disabled.");
        }

        PrepareContentXml();
        var contentBytes = AtomicZipWriter.SerializeContentXml(ContentXml);
        var targetPath = Path;
        var tmpPath = targetPath + ".tmp";
        var backupPath = targetPath + ".bak";

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using (var source = new ZipArchive(
                           new FileStream(targetPath, FileMode.Open, FileAccess.Read,
                               FileShare.ReadWrite | FileShare.Delete),
                           ZipArchiveMode.Read))
                {
                    AtomicZipWriter.WriteTemp(tmpPath, source, contentBytes, midWriteHook);
                }

                AtomicZipWriter.Commit(tmpPath, targetPath, backupPath);
                File.Delete(JournalPath);
            }
            catch
            {
                try
                {
                    File.Delete(tmpPath);
                }
                catch
                {
                    // best effort: never mask the original failure
                }

                throw;
            }
        }, ct);
    }

    /// <summary>
    /// Reorders <c>&lt;bubble&gt;</c> elements in the DOM to (page order, @order)
    /// and applies the §5.3 xml:space="preserve" rule to user-text elements.
    /// </summary>
    private void PrepareContentXml()
    {
        var root = ContentXml.Root;
        if (root is null)
        {
            return;
        }

        var bubblesEl = root.Element("bubbles");
        if (bubblesEl is not null)
        {
            var pageIndex = new Dictionary<string, int>();
            var pagesEl = root.Element("pages");
            var i = 0;
            foreach (var page in pagesEl?.Elements("page") ?? [])
            {
                var id = (string?)page.Attribute("id");
                if (id is not null)
                {
                    pageIndex.TryAdd(id, i);
                }

                i++;
            }

            var els = bubblesEl.Elements("bubble").ToList();
            var sorted = els
                .OrderBy(el => pageIndex.TryGetValue((string?)el.Attribute("page") ?? "", out var idx) ? idx : int.MaxValue)
                .ThenBy(el => int.TryParse((string?)el.Attribute("order"), out var o) ? o : int.MaxValue)
                .ToList();
            if (!els.SequenceEqual(sorted))
            {
                // Re-order in place, keeping each bubble's surrounding whitespace nodes.
                for (var k = 1; k < sorted.Count; k++)
                {
                    if (sorted[k - 1].NextNode != sorted[k])
                    {
                        sorted[k].Remove();
                        sorted[k - 1].AddAfterSelf(sorted[k]);
                    }
                }
            }
        }

        foreach (var el in root.Descendants())
        {
            if (el.Name.LocalName is "source" or "text" or "notes" or "llmComment" &&
                NeedsSpacePreserve(el.Value))
            {
                el.SetAttributeValue(XNamespace.Xml + "space", "preserve");
            }
        }
    }

    private static bool NeedsSpacePreserve(string text)
        => text.Length == 0 || char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]);
}