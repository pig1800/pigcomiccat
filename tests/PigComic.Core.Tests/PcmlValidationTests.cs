using PigComic.Core.Package;
using Xunit;

namespace PigComic.Core.Tests;

/// <summary>
/// SPEC §5.7 — one test per code. Fixes (W01 renumber, W02 character add)
/// are asserted behaviorally.
/// </summary>
public class PcmlValidationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "pigcomic-tests", Guid.NewGuid().ToString("N"));

    public PcmlValidationTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private static readonly string ValidXml =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <pcml version="2"><meta><title>t</title><chapter>1</chapter><sourceLanguage>zh-CN</sourceLanguage>
        <targetLanguage>ja</targetLanguage></meta><characters><character name="甲"/></characters><images>
        <image file="a.jpg" width="100" height="100"/></images><bubbles>
        <bubble id="b1" order="1" kind="Speech" status="Untranslated"><marker x="0" y="0"/>
        <source>hi</source><target><part index="1"><marker x="0" y="0"/><text></text></part></target></bubble>
        </bubbles></pcml>
        """;

    private PcmlDocument Open(string xml, string name = "test.pcml", bool fillMedia = true)
    {
        var path = PcmlTestBuilder.FromXmlString(xml)
            .Media("media/a.jpg", PcmlTestBuilder.TinyJpeg)
            .BuildZip(Path.Combine(_tempDir, name), fillMissingMedia: fillMedia);
        return PcmlDocument.Open(path);
    }

    private static string Replace(string from, string to, string? xml = null)
        => (xml ?? ValidXml).Replace(from, to, StringComparison.Ordinal);

    [Theory]
    [InlineData("E01-missing-meta")] // <meta> removed
    [InlineData("E01-no-version")]   // @version removed
    [InlineData("E01-no-bubble-id")]
    [InlineData("E01-no-part-text")]
    [InlineData("E02-version3")]
    [InlineData("E02-version1")]
    [InlineData("E02-version-abc")]
    
    [InlineData("E03-bubble-id")]
    [InlineData("E04-no-images")]
    [InlineData("E05-slash-file")]
    [InlineData("E05-missing-media")]
    [InlineData("E06-non-contiguous")]
    [InlineData("E07-bad-kind")]
    [InlineData("E07-bad-status")]
    public void Error_Codes_Open_ReadOnly(string which)
    {
        var xml = which switch
        {
            // Rename rather than delete: newline-agnostic, and leaves the XML well-formed.
            "E01-missing-meta" => Replace("<meta>", "<metax>").Replace("</meta>", "</metax>"),
            "E01-no-version" => Replace("<pcml version=\"2\"", "<pcml x=\"2\""),
            "E01-no-bubble-id" => Replace("id=\"b1\" ", ""),
            "E01-no-part-text" => Replace("<text></text>", ""),
            "E02-version3" => Replace("<pcml version=\"2\"", "<pcml version=\"3\""),
            "E02-version1" => Replace("<pcml version=\"2\"", "<pcml version=\"1\""),
            "E02-version-abc" => Replace("<pcml version=\"2\"", "<pcml version=\"abc\""),
            "E03-bubble-id" => Replace("<bubble id=\"b1\"", "<bubble id=\"b1\" order=\"1\" kind=\"Speech\" status=\"Untranslated\"><marker x=\"0\" y=\"60\"/><source>x</source><target><part index=\"1\"><marker x=\"0\" y=\"60\"/><text></text></part></target></bubble><bubble id=\"b1\""),
            "E04-no-images" => Replace("<image file=\"a.jpg\" width=\"100\" height=\"100\"/>", ""),
            "E05-slash-file" => Replace("file=\"a.jpg\"", "file=\"img/x.jpg\""),
            "E05-missing-media" => Replace("file=\"a.jpg\"", "file=\"zz.jpg\""),
            "E06-non-contiguous" => Replace("<target><part index=\"1\"><marker x=\"0\" y=\"0\"/><text></text></part></target>", "<target><part index=\"1\"><marker x=\"0\" y=\"0\"/><text></text></part><part index=\"2\"><marker x=\"0\" y=\"50\"/><text></text></part><part index=\"2\"><marker x=\"0\" y=\"100\"/><text></text></part></target>"),
            "E07-bad-kind" => Replace("kind=\"Speech\"", "kind=\"Banana\""),
            "E07-bad-status" => Replace("status=\"Untranslated\"", "status=\"Banana\""),
            _ => ValidXml,
        };

        using var doc = Open(xml, which + ".pcml", fillMedia: which != "E05-missing-media");
        Assert.True(doc.Issues.Count > 0, $"expected issues for {which}");
        Assert.True(doc.IsReadOnly, $"expected read-only for {which}");
        var expectedCode = which.Split('-')[0] switch
        {
            "E1" => "PCML-E01",
            _ => "PCML-" + which.Split('-')[0],
        };
        Assert.Contains(doc.Issues, i => i.Code == expectedCode);
        Assert.Contains(doc.Issues, i => i.IsError);
    }

    [Fact]
    public void W01_Duplicate_Orders_Renumbered_In_Memory()
    {
        var xml = Replace("<bubble id=\"b1\"", "<bubble id=\"b2\" order=\"1\" kind=\"Speech\" status=\"Untranslated\"><marker x=\"0\" y=\"60\"/><source>x</source><target><part index=\"1\"><marker x=\"0\" y=\"60\"/><text></text></part></target></bubble><bubble id=\"b1\"");
        using var doc = Open(xml);

        Assert.False(doc.IsReadOnly);
        Assert.Contains(doc.Issues, i => i.Code == "PCML-W01");
        var orders = doc.Model.Bubbles.Select(b => b.Order).ToList();
        Assert.Equal([1, 2], orders);
    }

    [Fact]
    public void W02_Character_AutoAdded_To_Chapter_List()
    {
        var xml = ValidXml.Replace(
            "<bubble id=\"b1\" order=\"1\" kind=\"Speech\" status=\"Untranslated\">",
            "<bubble id=\"b1\" order=\"1\" character=\"魔王\" kind=\"Speech\" status=\"Untranslated\">");

        using var doc = Open(xml);

        Assert.False(doc.IsReadOnly);
        Assert.Contains(doc.Issues, i => i.Code == "PCML-W02");
        Assert.Contains(doc.Model.Characters, c => c == "魔王");
        Assert.Contains(doc.ContentXml.Root!.Element("characters")!.Elements("character"),
            e => (string?)e.Attribute("name") == "魔王");
    }

    [Fact]
    public void W03_Marker_Outside_The_Strip()
    {
        // The strip is 100×100; this marker is well past its bottom-right (D-49).
        var xml = ValidXml.Replace(
            "<marker x=\"0\" y=\"0\"/>",
            "<marker x=\"500\" y=\"500\"/>");

        using var doc = Open(xml);

        Assert.False(doc.IsReadOnly);
        Assert.Contains(doc.Issues, i => i.Code == "PCML-W03");
    }

    [Fact]
    public void W04_Unreferenced_Media_Warns()
    {
        var xmlBuilder = PcmlTestBuilder.FromXmlString(ValidXml)
            .Media("media/unused.jpg", PcmlTestBuilder.TinyJpeg);
        var path = xmlBuilder.BuildZip(Path.Combine(_tempDir, "w04.pcml"));
        using var doc = PcmlDocument.Open(path);

        Assert.False(doc.IsReadOnly);
        Assert.Contains(doc.Issues, i => i.Code == "PCML-W04");
        Assert.Contains(doc.MediaEntries, m => m.Name == "media/unused.jpg");
    }

    [Fact]
    public void Valid_File_Has_No_Issues()
    {
        using var doc = Open(ValidXml);
        Assert.Empty(doc.Issues);
        Assert.False(doc.IsReadOnly);
    }
}