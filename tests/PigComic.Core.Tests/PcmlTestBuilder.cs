using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using PigComic.Core.Domain;

namespace PigComic.Core.Tests;

/// <summary>
/// Builds valid .pcml packages for tests. Also usable as a raw-XML-string source
/// for malformed fixtures (validation tests).
/// </summary>
public sealed class PcmlTestBuilder
{
    private readonly XDocument _doc;
    private readonly List<(string Name, byte[] Bytes)> _media = [];

    private PcmlTestBuilder(XDocument doc) => _doc = doc;

    public static PcmlTestBuilder New(string title, string srcLang, string tgtLang)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("pcml", new XAttribute("version", "2"),
                new XElement("meta",
                    new XElement("title", title),
                    new XElement("chapter", "001"),
                    new XElement("sourceLanguage", srcLang),
                    new XElement("targetLanguage", tgtLang)),
                new XElement("characters"),
                new XElement("images"),
                new XElement("bubbles")));
        return new PcmlTestBuilder(doc);
    }

    public static PcmlTestBuilder FromXmlString(string xml)
    {
        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        return new PcmlTestBuilder(doc);
    }

    private XElement Root => _doc.Root!;

    public PcmlTestBuilder Chapter(string chapter)
    {
        Root.Element("meta")!.Element("chapter")!.Value = chapter;
        return this;
    }

    public PcmlTestBuilder Character(string name)
    {
        Root.Element("characters")!.Add(new XElement("character", new XAttribute("name", name)));
        return this;
    }

    /// <summary>Appends one image to the chapter strip (D-49); document order is strip order.</summary>
    public PcmlTestBuilder Image(string file, int width, int height)
    {
        Root.Element("images")!.Add(new XElement("image",
            new XAttribute("file", file),
            new XAttribute("width", width), new XAttribute("height", height)));
        return this;
    }

    /// <summary>Adds an arbitrary zip entry (media or unknown, e.g. thumbs/x).</summary>
    public PcmlTestBuilder Media(string name, byte[] bytes)
    {
        _media.Add((name, bytes));
        return this;
    }

    public PcmlTestBuilder Bubble(
        string id,
        int order,
        string kind = "Speech",
        string status = "Untranslated",
        string? character = null,
        PixelPoint? marker = null,
        string source = "",
        string notes = "",
        string llmComment = "",
        params (int Index, PixelPoint Marker, string Text)[] parts)
    {
        var m = marker ?? new PixelPoint(100, 100);
        var attrs = new List<object>
        {
            new XAttribute("id", id),
            new XAttribute("order", order),
            new XAttribute("kind", kind),
            new XAttribute("status", status),
        };
        if (character is not null)
        {
            attrs.Add(new XAttribute("character", character));
        }

        var bubble = new XElement("bubble", attrs.ToArray(),
            new XElement("marker", new XAttribute("x", m.X), new XAttribute("y", m.Y)),
            new XElement("source", source),
            new XElement("target"));

        var partsEl = bubble.Element("target")!;
        var partList = parts.Length == 0 ? new[] { (Index: 1, Marker: m, Text: "") } : parts;
        foreach (var p in partList)
        {
            partsEl.Add(new XElement("part", new XAttribute("index", p.Index),
                new XElement("marker", new XAttribute("x", p.Marker.X), new XAttribute("y", p.Marker.Y)),
                new XElement("text", p.Text)));
        }

        if (!string.IsNullOrEmpty(notes))
        {
            bubble.Add(new XElement("notes", notes));
        }

        if (!string.IsNullOrEmpty(llmComment))
        {
            bubble.Add(new XElement("llmComment", llmComment));
        }

        Root.Element("bubbles")!.Add(bubble);
        return this;
    }

    public string BuildXml() => _doc.ToString(SaveOptions.DisableFormatting);

    /// <summary>
    /// Writes a complete package: content.xml (deflated) + all media/unknown
    /// entries (no compression). Fills tiny placeholder images for page files.
    /// Returns the path written.
    /// </summary>
    public string BuildZip(string outputPath)
    {
        return BuildZip(outputPath, fillMissingMedia: true);
    }

    /// <summary>
    /// Writes a complete package: content.xml (deflated) + all media/unknown
    /// entries (no compression). When <paramref name="fillMissingMedia"/> is true,
    /// tiny placeholder images fill strip files without an explicit Media() entry.
    /// </summary>
    public string BuildZip(string outputPath, bool fillMissingMedia)
    {
        var dir = System.IO.Path.GetDirectoryName(outputPath)!;
        Directory.CreateDirectory(dir);

        var media = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (name, bytes) in _media)
        {
            media[name] = bytes;
        }

        if (fillMissingMedia)
        {
            foreach (var image in Root.Element("images")?.Elements("image") ?? [])
            {
                var file = (string?)image.Attribute("file") ?? "";
                if (file.Length > 0 && !media.ContainsKey($"media/{file}"))
                {
                    media[$"media/{file}"] = TinyPng;
                }
            }
        }

        using (var zip = ZipFile.Open(outputPath, ZipArchiveMode.Create))
        {
            var content = zip.CreateEntry("content.xml", CompressionLevel.Optimal);
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = false,
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
                CloseOutput = false,
            };
            using (var entryStream = content.Open())
            using (var writer = XmlWriter.Create(entryStream, settings))
            {
                _doc.Save(writer);
            }

            foreach (var (name, bytes) in media)
            {
                var entry = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.NoCompression);
                using var es = entry.Open();
                es.Write(bytes);
            }
        }

        return outputPath;
    }

    public static readonly byte[] TinyPng =
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public static readonly byte[] TinyJpeg =
        Convert.FromBase64String(
            "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AVN//2Q==");
}