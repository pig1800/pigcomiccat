using PigComic.Core.Domain;
using PigComic.Core.Package;
using Xunit;

namespace PigComic.Core.Tests;

public class PcmlLoadTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "pigcomic-tests", Guid.NewGuid().ToString("N"));

    public PcmlLoadTests() => Directory.CreateDirectory(_tempDir);

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

    private string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private string BuildExampleZip()
    {
        var xml = File.ReadAllText(FixturePath("example1/content.xml"));
        var builder = PcmlTestBuilder.FromXmlString(xml)
            .Media("media/0001.jpg", PcmlTestBuilder.TinyJpeg)
            .Media("media/0002.png", PcmlTestBuilder.TinyPng);
        return builder.BuildZip(Path.Combine(_tempDir, "example1.pcml"));
    }

    [Fact]
    public void Open_Loads_Example_Chapter()
    {
        var zipPath = BuildExampleZip();
        using var doc = PcmlDocument.Open(zipPath);

        var chapter = doc.Model;
        Assert.Equal("勇者ピッグ", chapter.Title);
        Assert.Equal("012", chapter.ChapterNumber);
        Assert.Equal("ja", chapter.SourceLanguage);
        Assert.Equal("zh-Hant", chapter.TargetLanguage);
    }

    [Fact]
    public void Open_Pages_In_Document_Order()
    {
        using var doc = PcmlDocument.Open(BuildExampleZip());
        var pages = doc.Model.Pages;
        Assert.Equal(2, pages.Count);
        Assert.Equal("p0001", pages[0].Id);
        Assert.Equal("0001.jpg", pages[0].FileName);
        Assert.Equal(1080, pages[0].Width);
        Assert.Equal(41250, pages[0].Height);
        Assert.Equal("p0002", pages[1].Id);
    }

    [Fact]
    public void Open_Bubbles_Sorted_By_Page_Then_Order()
    {
        using var doc = PcmlDocument.Open(BuildExampleZip());
        var ids = doc.Model.Bubbles.Select(b => b.Id).ToList();
        Assert.Equal(["p0001-b0001", "p0001-b0002", "p0002-b0001"], ids);
    }

    [Fact]
    public void Open_Bubble_Metadata()
    {
        using var doc = PcmlDocument.Open(BuildExampleZip());
        var b0001 = doc.Model.Bubbles.Single(b => b.Id == "p0001-b0001");
        Assert.Equal(BubbleKind.Speech, b0001.Kind);
        Assert.Equal("ピッグ", b0001.Character);
        Assert.Equal(BubbleStatus.Translated, b0001.Status);
        Assert.Equal("おはよう　ございます！", b0001.SourceText);
        Assert.Equal("早安！", b0001.TargetJoined);

        var b0002 = doc.Model.Bubbles.Single(b => b.Id == "p0001-b0002");
        Assert.Equal(2, b0002.Parts.Count);
        Assert.Equal("難道說…", b0002.Parts[0].Text);
        Assert.Equal("魔王竟有100人？", b0002.Parts[1].Text);
        Assert.Equal("確認：100人 or 100隻", b0002.Notes);
        Assert.NotEqual("", b0002.LlmComment);
        Assert.Equal(BubbleKind.Thought, b0002.Kind);
    }

    [Fact]
    public void Open_Media_Entries_Enumerated()
    {
        using var doc = PcmlDocument.Open(BuildExampleZip());
        var media = doc.MediaEntries.Where(m => m.Name.StartsWith("media/"))
            .Select(m => m.Name).ToList();
        Assert.Equal(["media/0001.jpg", "media/0002.png"], media);
    }

    [Fact]
    public void Open_Characters_List()
    {
        using var doc = PcmlDocument.Open(BuildExampleZip());
        Assert.Equal(["ピッグ", "魔王"], doc.Model.Characters);
    }

    [Fact]
    public void Open_Missing_File_Throws_PcmlLoadException()
    {
        Assert.Throws<PcmlLoadException>(() => PcmlDocument.Open(Path.Combine(_tempDir, "nope.pcml")));
    }

    [Fact]
    public void Open_NonZip_Throws_PcmlLoadException()
    {
        var path = Path.Combine(_tempDir, "notzip.pcml");
        File.WriteAllText(path, "this is not a zip");
        Assert.Throws<PcmlLoadException>(() => PcmlDocument.Open(path));
        Assert.Throws<PcmlLoadException>(() => PcmlDocument.Open(path)); // second call, disposed archive safe
    }

    [Fact]
    public void Crlf_Text_Normalized_To_Lf()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <pcml version="1"><meta><title>t</title><chapter>1</chapter><sourceLanguage>ja</sourceLanguage>
            <targetLanguage>zh</targetLanguage></meta><characters/><pages>
            <page id="p1" file="a.jpg" width="10" height="10"/></pages><bubbles>
            <bubble id="b1" page="p1" order="1" kind="Speech" status="Untranslated"><region x="0" y="0" width="5" height="5"/>
            <source>a</source><target><part index="1"><region x="0" y="0" width="5" height="5"/><text>a&#10;b</text></part></target></bubble>
            </bubbles></pcml>
            """;
        var path = PcmlTestBuilder.FromXmlString(xml)
            .Media("media/a.jpg", PcmlTestBuilder.TinyJpeg)
            .BuildZip(Path.Combine(_tempDir, "crlf.pcml"));

        // Rewrite content.xml with CRLF line endings to simulate a foreign writer.
        using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("content.xml")!;
            using var s = entry.Open();
            using var reader = new StreamReader(s);
            var text = reader.ReadToEnd().Replace("\r\n", "\n").Replace("\n", "\r\n");
            s.SetLength(0);
            s.Position = 0;
            using var writer = new StreamWriter(s, new System.Text.UTF8Encoding(false));
            writer.Write(text);
        }

        using var doc = PcmlDocument.Open(path);
        Assert.Equal("a\nb", doc.Model.Bubbles[0].Parts[0].Text);
    }
}