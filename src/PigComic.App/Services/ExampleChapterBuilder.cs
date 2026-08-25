using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using PigComic.Core.Domain;

namespace PigComic.App.Services;

/// <summary>
/// M5.1 debug tooling: builds the SPEC §5.4-style example chapter as a real
/// .pcml package whose pages are the StripImageGenerator strips, so the editor
/// can be exercised without a real job. Only referenced from the Debug menu.
/// </summary>
public static class ExampleChapterBuilder
{
    /// <summary>
    /// Writes example1.pcml into <paramref name="outputDir"/>: content.xml plus
    /// the given page images copied as media/0001.jpg and media/0002.png.
    /// Returns the package path.
    /// </summary>
    public static string Build(string outputDir, int pageWidth, int pageHeight,
        string page1Image, string page2Image)
    {
        var outPath = Path.Combine(outputDir, "example1.pcml");
        Directory.CreateDirectory(outputDir);
        if (File.Exists(outPath))
        {
            File.Delete(outPath);
        }

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("pcml", new XAttribute("version", "1"),
                new XElement("meta",
                    new XElement("title", "勇者ピッグ"),
                    new XElement("chapter", "001"),
                    new XElement("sourceLanguage", "ja"),
                    new XElement("targetLanguage", "zh-Hant")),
                new XElement("characters",
                    new XElement("character", new XAttribute("name", "ピッグ")),
                    new XElement("character", new XAttribute("name", "魔王"))),
                new XElement("pages",
                    Page("p0001", "0001.jpg", pageWidth, pageHeight),
                    Page("p0002", "0002.png", pageWidth, pageHeight)),
                new XElement("bubbles",
                    Bubble("p0001-b0001", "p0001", 1, "Speech", "Translated", "ピッグ",
                        new PixelRect(612, 480, 240, 310), "おはよう　ございます！", "早安！"),
                    Bubble("p0001-b0002", "p0001", 2, "Thought", "Draft", "魔王",
                        new PixelRect(120, 900, 300, 640), "まさか…魔王が１００人も？",
                        "難道說…", (2, new PixelRect(120, 1050, 300, 300), "魔王竟有100人？")),
                    Bubble("p0002-b0001", "p0002", 1, "Sfx", "Untranslated", null,
                        new PixelRect(600, 200, 260, 300), "ドドド", ""))));

        using (var zip = ZipFile.Open(outPath, ZipArchiveMode.Create))
        {
            var contentEntry = zip.CreateEntry("content.xml", CompressionLevel.Optimal);
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = false,
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
                CloseOutput = false,
            };
            using (var entryStream = contentEntry.Open())
            using (var writer = XmlWriter.Create(entryStream, settings))
            {
                doc.Save(writer);
            }

            AddMedia(zip, "media/0001.jpg", page1Image);
            AddMedia(zip, "media/0002.png", page2Image);
        }

        return outPath;
    }

    private static void AddMedia(ZipArchive zip, string entryName, string filePath)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
        using var es = entry.Open();
        using var fs = File.OpenRead(filePath);
        fs.CopyTo(es);
    }

    private static XElement Page(string id, string file, int width, int height)
        => new("page",
            new XAttribute("id", id), new XAttribute("file", file),
            new XAttribute("width", width), new XAttribute("height", height));

    private static XElement Bubble(string id, string page, int order, string kind, string status,
        string? character, PixelRect region, string source, string firstPartText,
        params (int Index, PixelRect Region, string Text)[] extraParts)
    {
        var attrs = new List<object>
        {
            new XAttribute("id", id), new XAttribute("page", page),
            new XAttribute("order", order), new XAttribute("kind", kind),
            new XAttribute("status", status),
        };
        if (character is not null)
        {
            attrs.Add(new XAttribute("character", character));
        }

        var bubble = new XElement("bubble", attrs.ToArray(),
            Region(region), new XElement("source", source), new XElement("target"));

        var target = bubble.Element("target")!;
        target.Add(Part(1, region, firstPartText));
        foreach (var part in extraParts)
        {
            target.Add(Part(part.Index, part.Region, part.Text));
        }

        return bubble;
    }

    private static XElement Part(int index, PixelRect region, string text)
        => new("part", new XAttribute("index", index),
            Region(region), new XElement("text", text));

    private static XElement Region(PixelRect r)
        => new("region",
            new XAttribute("x", r.X), new XAttribute("y", r.Y),
            new XAttribute("width", r.Width), new XAttribute("height", r.Height));
}