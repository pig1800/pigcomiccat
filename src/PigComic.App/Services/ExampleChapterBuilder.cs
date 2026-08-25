using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using PigComic.Core.Domain;

namespace PigComic.App.Services;

/// <summary>
/// M5.1 debug tooling: builds the SPEC §5.4-style example chapter as a real
/// .pcml package whose strip images are the StripImageGenerator output, so the editor
/// can be exercised without a real job. Only referenced from the Debug menu.
/// </summary>
public static class ExampleChapterBuilder
{
    /// <summary>
    /// Writes example1.pcml into <paramref name="outputDir"/>: content.xml plus
    /// the given strip images copied as media/0001.jpg and media/0002.png.
    /// Returns the package path.
    /// </summary>
    public static string Build(string outputDir, int imageWidth, int imageHeight,
        string firstImage, string secondImage)
    {
        var outPath = Path.Combine(outputDir, "example1.pcml");
        Directory.CreateDirectory(outputDir);
        if (File.Exists(outPath))
        {
            File.Delete(outPath);
        }

        // Strip coordinates: the second image starts at Y = imageHeight (D-49).
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("pcml", new XAttribute("version", "2"),
                new XElement("meta",
                    new XElement("title", "勇者小猪"),
                    new XElement("chapter", "001"),
                    new XElement("sourceLanguage", "zh-CN"),
                    new XElement("targetLanguage", "ja")),
                new XElement("characters",
                    new XElement("character", new XAttribute("name", "小猪")),
                    new XElement("character", new XAttribute("name", "魔王"))),
                new XElement("images",
                    Image("0001.jpg", imageWidth, imageHeight),
                    Image("0002.png", imageWidth, imageHeight)),
                new XElement("bubbles",
                    Bubble("b0001", 1, "Speech", "Translated", "小猪",
                        new PixelPoint(612, 480), "早上好！", "おはようございます！"),
                    Bubble("b0002", 2, "Thought", "Draft", "魔王",
                        new PixelPoint(120, 900), "难道说…魔王有100个？",
                        "まさか…", (2, new PixelPoint(120, 948), "魔王が100人も？")),
                    Bubble("b0003", 3, "Sfx", "Untranslated", null,
                        new PixelPoint(600, imageHeight + 200), "咚咚咚", ""))));

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

            AddMedia(zip, "media/0001.jpg", firstImage);
            AddMedia(zip, "media/0002.png", secondImage);
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

    private static XElement Image(string file, int width, int height)
        => new("image",
            new XAttribute("file", file),
            new XAttribute("width", width), new XAttribute("height", height));

    private static XElement Bubble(string id, int order, string kind, string status,
        string? character, PixelPoint marker, string source, string firstPartText,
        params (int Index, PixelPoint Marker, string Text)[] extraParts)
    {
        var attrs = new List<object>
        {
            new XAttribute("id", id),
            new XAttribute("order", order), new XAttribute("kind", kind),
            new XAttribute("status", status),
        };
        if (character is not null)
        {
            attrs.Add(new XAttribute("character", character));
        }

        var bubble = new XElement("bubble", attrs.ToArray(),
            Marker(marker), new XElement("source", source), new XElement("target"));

        var target = bubble.Element("target")!;
        target.Add(Part(1, marker, firstPartText));
        foreach (var part in extraParts)
        {
            target.Add(Part(part.Index, part.Marker, part.Text));
        }

        return bubble;
    }

    private static XElement Part(int index, PixelPoint marker, string text)
        => new("part", new XAttribute("index", index),
            Marker(marker), new XElement("text", text));

    private static XElement Marker(PixelPoint p)
        => new("marker", new XAttribute("x", p.X), new XAttribute("y", p.Y));
}
