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
        <pcml version="1"><meta><title>t</title><chapter>1</chapter><sourceLanguage>ja</sourceLanguage>
        <targetLanguage>zh-Hant</targetLanguage></meta><characters><character name="甲"/></characters><pages>
        <page id="p1" file="a.jpg" width="100" height="100"/></pages><bubbles>
        <bubble id="b1" page="p1" order="1" kind="Speech" status="Untranslated"><region x="0" y="0" width="50" height="50"/>
        <source>hi</source><target><part index="1"><region x="0" y="0" width="50" height="50"/><text></text></part></target></bubble>
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
    [InlineData("E01-no-page-id")]
    [InlineData("E01-no-part-text")]
    [InlineData("E02-version2")]
    [InlineData("E02-version-abc")]
    [InlineData("E03-page-id")]
    [InlineData("E03-bubble-id")]
    [InlineData("E04-unknown-page")]
    [InlineData("E05-slash-file")]
    [InlineData("E05-missing-media")]
    [InlineData("E06-non-contiguous")]
    [InlineData("E07-bad-kind")]
    [InlineData("E07-bad-status")]
    public void Error_Codes_Open_ReadOnly(string which)
    {
        var xml = which switch
        {
            "E01-missing-meta" => ValidXml.Replace("<meta><title>t</title><chapter>1</chapter><sourceLanguage>ja</sourceLanguage>\n<targetLanguage>zh-Hant</targetLanguage></meta>", ""),
            "E01-no-version" => Replace("version=\"1\"", "x=\"1\""),
            "E01-no-page-id" => Replace("id=\"b1\" ", ""),
            "E01-no-part-text" => Replace("<text></text>", ""),
            "E02-version2" => Replace("version=\"1\"", "version=\"2\""),
            "E02-version-abc" => Replace("version=\"1\"", "version=\"abc\""),
            "E03-page-id" => Replace("</pages>", "<page id=\"p1\" file=\"b.jpg\" width=\"10\" height=\"10\"/></pages>"),
            "E03-bubble-id" => Replace("<bubble id=\"b1\"", "<bubble id=\"b1\" page=\"p1\" order=\"1\" kind=\"Speech\" status=\"Untranslated\"><region x=\"0\" y=\"60\" width=\"50\" height=\"50\"/><source>x</source><target><part index=\"1\"><region x=\"0\" y=\"60\" width=\"50\" height=\"50\"/><text></text></part></target></bubble><bubble id=\"b1\""),
            "E04-unknown-page" => Replace("page=\"p1\" order=\"1\"", "page=\"nope\" order=\"1\""),
            "E05-slash-file" => Replace("file=\"a.jpg\"", "file=\"img/x.jpg\""),
            "E05-missing-media" => Replace("file=\"a.jpg\"", "file=\"zz.jpg\""),
            "E06-non-contiguous" => Replace("<target><part index=\"1\"><region x=\"0\" y=\"0\" width=\"50\" height=\"50\"/><text></text></part></target>", "<target><part index=\"1\"><region x=\"0\" y=\"0\" width=\"50\" height=\"50\"/><text></text></part><part index=\"2\"><region x=\"0\" y=\"50\" width=\"50\" height=\"50\"/><text></text></part><part index=\"2\"><region x=\"0\" y=\"100\" width=\"50\" height=\"50\"/><text></text></part></target>"),
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
        var xml = Replace("<bubble id=\"b1\"", "<bubble id=\"b2\" page=\"p1\" order=\"1\" kind=\"Speech\" status=\"Untranslated\"><region x=\"0\" y=\"60\" width=\"50\" height=\"50\"/><source>x</source><target><part index=\"1\"><region x=\"0\" y=\"60\" width=\"50\" height=\"50\"/><text></text></part></target></bubble><bubble id=\"b1\"");
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
            "<bubble id=\"b1\" page=\"p1\" order=\"1\" kind=\"Speech\" status=\"Untranslated\">",
            "<bubble id=\"b1\" page=\"p1\" order=\"1\" character=\"魔王\" kind=\"Speech\" status=\"Untranslated\">");

        using var doc = Open(xml);

        Assert.False(doc.IsReadOnly);
        Assert.Contains(doc.Issues, i => i.Code == "PCML-W02");
        Assert.Contains(doc.Model.Characters, c => c == "魔王");
        Assert.Contains(doc.ContentXml.Root!.Element("characters")!.Elements("character"),
            e => (string?)e.Attribute("name") == "魔王");
    }

    [Fact]
    public void W03_Region_Fully_Outside_Page_Bounds()
    {
        var xml = ValidXml.Replace(
            "x=\"0\" y=\"0\" width=\"50\" height=\"50\"",
            "x=\"500\" y=\"500\" width=\"50\" height=\"50\"");

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