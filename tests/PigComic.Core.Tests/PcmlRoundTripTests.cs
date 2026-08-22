using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using PigComic.Core.Package;
using Xunit;

namespace PigComic.Core.Tests;

/// <summary>SPEC §26.7 / PLAN M1.5 acceptance.</summary>
public class PcmlRoundTripTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "pigcomic-tests", Guid.NewGuid().ToString("N"));

    public PcmlRoundTripTests() => Directory.CreateDirectory(_tempDir);

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

    private const string FixtureXml =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <pcml version="1" tool="tiaoman"><meta><title>勇者ピッグ</title><chapter>012</chapter><sourceLanguage>ja</sourceLanguage>
        <targetLanguage>zh-Hant</targetLanguage></meta><characters><character name="ピッグ"/></characters><pages>
        <page id="p0001" file="0001.jpg" width="1080" height="41250"/></pages><bubbles>
        <bubble id="p0001-b0001" page="p0001" order="1" kind="Speech" character="ピッグ" status="Translated"><region x="612" y="480" width="240" height="310"/>
        <source>おはよう　ございます！</source><target><part index="1"><region x="612" y="480" width="240" height="310"/><text>早安！</text></part></target>
        <userData opaque="1"><anything>hi</anything></userData></bubble>
        </bubbles><futureFeature><weird>preserved</weird></futureFeature></pcml>
        """;

    private string BuildFixture(string name)
    {
        var builder = PcmlTestBuilder.FromXmlString(FixtureXml)
            .Media("media/0001.jpg", PcmlTestBuilder.TinyJpeg)
            .Media("thumbs/x.txt", Encoding.UTF8.GetBytes("unknown entry bytes"));
        return builder.BuildZip(Path.Combine(_tempDir, name));
    }

    [Fact]
    public async Task Noop_Load_Save_Preserves_Unknowns_And_Media()
    {
        var path = BuildFixture("a.pcml");

        using (var doc = PcmlDocument.Open(path))
        {
            Assert.Empty(doc.Issues);
            await doc.SaveAsync(CancellationToken.None);
        }

        using var saved = PcmlDocument.Open(path);

        // Media and unknown zip entries byte-identical (SPEC §5.5).
        Assert.Equal(PcmlTestBuilder.TinyJpeg, ZipEntryBytes(path, "media/0001.jpg"));
        Assert.Equal("unknown entry bytes", Encoding.UTF8.GetString(ZipEntryBytes(path, "thumbs/x.txt")));

        // Unknown elements/attributes preserved.
        var xml = LoadXml(path);
        Assert.Equal("tiaoman", xml.Root!.Attribute("tool")?.Value);
        Assert.Contains(xml.Root.Elements("futureFeature"), e => e.Element("weird")?.Value == "preserved");
        var bubble = xml.Root.Element("bubbles")!.Element("bubble")!;
        var userData = bubble.Element("userData");
        Assert.Equal("1", userData?.Attribute("opaque")?.Value);
        Assert.Equal("hi", userData?.Element("anything")?.Value);
    }

    [Fact]
    public async Task Noop_Load_Save_Is_Byte_Identical_Content()
    {
        var path = BuildFixture("a2.pcml");
        var before = ZipEntryBytes(path, "content.xml");
        using var doc = PcmlDocument.Open(path);
        Assert.Empty(doc.Issues);
        await doc.SaveAsync(CancellationToken.None);
        var after = ZipEntryBytes(path, "content.xml");

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Edit_One_Target_Save_Produces_Single_Value_Diff()
    {
        var path = BuildFixture("b2.pcml");
        using (var doc = PcmlDocument.Open(path))
        {
            doc.Model.Bubbles.Single(b => b.Id == "p0001-b0001").Parts[0].Text = "おはよう！";
            await doc.SaveAsync(CancellationToken.None);
        }

        var pristine = BuildFixture("b2-pristine-" + Guid.NewGuid().ToString("N")[..6] + ".pcml");
        using (var untouched = PcmlDocument.Open(pristine))
        {
            await untouched.SaveAsync(CancellationToken.None);
        }

        var diffs = XmlTextDiffs(LoadXml(pristine), LoadXml(path));
        Assert.Single(diffs);
        Assert.Equal("text", diffs[0].Parent);
        Assert.Equal("早安！", diffs[0].Before);
        Assert.Equal("おはよう！", diffs[0].After);
    }

    [Fact]
    public async Task Failed_MidWrite_Save_Leaves_Original_Untouched_And_No_Residue()
    {
        var path = BuildFixture("c.pcml");
        var originalContentBytes = ZipEntryBytes(path, "content.xml");

        using (var doc = PcmlDocument.Open(path))
        {
            doc.Model.Bubbles[0].Parts[0].Text = "changed";

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await doc.SaveAsync(zip => throw new InvalidOperationException("kill"), CancellationToken.None));

            Assert.False(File.Exists(path + ".tmp"), "no .tmp residue after failed save");
            Assert.Equal(originalContentBytes, ZipEntryBytes(path, "content.xml"));

            await doc.SaveAsync(CancellationToken.None); // retry succeeds
            Assert.False(File.Exists(path + ".tmp"));
        }

        using var doc2 = PcmlDocument.Open(path);
        Assert.Equal("changed", doc2.Model.Bubbles[0].Parts[0].Text);
    }

    [Fact]
    public async Task Bak_Contains_Previous_Version()
    {
        var path = BuildFixture("d.pcml");
        using (var doc = PcmlDocument.Open(path))
        {
            doc.Model.Bubbles[0].Parts[0].Text = "版本二";
            await doc.SaveAsync(CancellationToken.None);   // bak = original
            doc.Model.Bubbles[0].Parts[0].Text = "版本三";
            await doc.SaveAsync(CancellationToken.None);   // bak = 版本二
        }

        using var bak = PcmlDocument.Open(path + ".bak");
        Assert.Equal("版本二", bak.Model.Bubbles[0].Parts[0].Text);
    }

    [Fact]
    public async Task Journal_Deleted_On_Successful_Save()
    {
        var path = BuildFixture("e.pcml");
        File.WriteAllText(path + ".journal", "{}\n");
        using (var doc = PcmlDocument.Open(path))
        {
            await doc.SaveAsync(CancellationToken.None);
        }

        Assert.False(File.Exists(path + ".journal"));
    }

    [Fact]
    public async Task Writer_Outputs_Lf_And_No_Bom()
    {
        var path = BuildFixture("f.pcml");
        using (var doc = PcmlDocument.Open(path))
        {
            await doc.SaveAsync(CancellationToken.None);
        }

        var bytes = ZipEntryBytes(path, "content.xml");
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "no BOM");
        Assert.DoesNotContain("\r\n", Encoding.UTF8.GetString(bytes));
    }

    // ---------------------------------------------------------------- helpers

    private static byte[] ZipEntryBytes(string zipPath, string entryName)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry(entryName)!;
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static XDocument LoadXml(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry("content.xml")!;
        using var s = entry.Open();
        return XDocument.Load(s, LoadOptions.PreserveWhitespace);
    }

    private sealed record TextDiff(string Parent, string Before, string After);

    /// <summary>All XText value differences between two documents, in document order.</summary>
    private static List<TextDiff> XmlTextDiffs(XDocument a, XDocument b)
    {
        var list = new List<TextDiff>();
        var nodesA = a.DescendantNodes().OfType<XText>().ToList();
        var nodesB = b.DescendantNodes().OfType<XText>().ToList();
        for (var i = 0; i < Math.Max(nodesA.Count, nodesB.Count); i++)
        {
            var va = i < nodesA.Count ? nodesA[i].Value : null;
            var vb = i < nodesB.Count ? nodesB[i].Value : null;
            if (va != vb)
            {
                var parent = (i < nodesB.Count ? nodesB[i].Parent : nodesA[i].Parent)?.Name.LocalName ?? "?";
                list.Add(new TextDiff(parent, va ?? "", vb ?? ""));
            }
        }

        return list;
    }
}