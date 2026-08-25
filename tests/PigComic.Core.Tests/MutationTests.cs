using PigComic.Core.Domain;
using PigComic.Core.Package;
using Xunit;

namespace PigComic.Core.Tests;

public class MutationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "pigcomic-tests", Guid.NewGuid().ToString("N"));

    public MutationTests() => Directory.CreateDirectory(_tempDir);

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

    private PcmlDocument OpenValid()
    {
        var path = PcmlTestBuilder.New("t", "zh-CN", "ja").Chapter("1")
            .Character("ピッグ")
            .Image("a.jpg", 1000, 2000)
            .Bubble("b1", 1, "Speech", "Untranslated", "ピッグ",
                new PixelPoint(100, 100), "こんにちは")
            .Bubble("b2", 2, "Speech", "Untranslated", null,
                new PixelPoint(100, 500), "さようなら")
            .BuildZip(Path.Combine(_tempDir, "mut.pcml"));
        return PcmlDocument.Open(path);
    }

    [Fact]
    public void SetPartCount_3_To_1_Joins_And_Resets_Region()
    {
        using var doc = OpenValid();
        var bubble = doc.Model.Bubbles[0];
        BubbleMutations.SetPartCount(bubble, 3);
        Assert.Equal(3, bubble.Parts.Count);
        // Markers stack from the source marker by PartMarkerStep (D-18/D-50), not bands.
        Assert.Equal(100, bubble.Parts[0].Marker.Y);
        Assert.Equal(100 + BubbleMutations.PartMarkerStep, bubble.Parts[1].Marker.Y);
        Assert.Equal(100 + (2 * BubbleMutations.PartMarkerStep), bubble.Parts[2].Marker.Y);

        BubbleMutations.SetPartText(bubble, 1, "一");
        BubbleMutations.SetPartText(bubble, 2, "二");
        BubbleMutations.SetPartText(bubble, 3, "三");

        BubbleMutations.SetPartCount(bubble, 1);
        Assert.Single(bubble.Parts);
        Assert.Equal("一\n二\n三", bubble.Parts[0].Text);
        Assert.Equal(new PixelPoint(100, 100), bubble.Parts[0].Marker);
    }

    [Fact]
    public void AddBubble_Between_Bubbles_Gets_Order_Between_And_Renumbers()
    {
        using var doc = OpenValid();
        var bubble = doc.Model.Bubbles[0];
        BubbleMutations.SetPartText(bubble, 1, "x");

        BubbleMutations.AddBubble(doc, new PixelPoint(100, 300), out var created);
        Assert.StartsWith("u", created.Id);
        Assert.Equal(9, created.Id.Length); // "u" + 8 hex chars
        Assert.Equal("Speech", created.Kind.ToString());
        Assert.Equal(BubbleStatus.Untranslated, created.Status);
        Assert.Equal("", created.SourceText);
        Assert.Single(created.Parts);
        Assert.Equal(new PixelPoint(100, 300), created.Parts[0].Marker);

        // Orders: original Y=100 -> 1, new Y=300 -> 2, original Y=500 -> 3.
        var docOrders = doc.Model.Bubbles.OrderBy(b => b.Order).Select(b => b.Order).ToList();
        Assert.Equal([1, 2, 3], docOrders);
        var ids = doc.Model.Bubbles.OrderBy(b => b.Order).Select(b => b.Id).ToList();
        Assert.Equal(["b1", created.Id, "b2"], ids);
    }

    [Fact]
    public void AddBubble_At_Top_Gets_Order_1()
    {
        using var doc = OpenValid();
        BubbleMutations.AddBubble(doc, new PixelPoint(100, 0), out var created);
        Assert.Equal(1, created.Order);
        Assert.Equal([1, 2, 3], doc.Model.Bubbles.OrderBy(b => b.Order).Select(b => b.Order).ToList());
        var b1 = doc.Model.Bubbles.First(b => b.Id == "b1");
        Assert.Equal(2, b1.Order);
    }

    [Fact]
    public async Task AddBubble_Id_Collision_Checked()
    {
        var builder = PcmlTestBuilder.New("t", "zh-CN", "ja").Chapter("1")
            .Image("a.jpg", 1000, 2000);
        for (var i = 1; i <= 5; i++)
        {
            builder.Bubble($"pb{i}", i, "Speech", "Untranslated", source: $"{i}");
        }

        var path = builder.BuildZip(Path.Combine(_tempDir, "col.pcml"));
        using var doc = PcmlDocument.Open(path);
        var ids = new HashSet<string>(doc.Model.Bubbles.Select(b => b.Id));
        for (var i = 0; i < 50; i++)
        {
            BubbleMutations.AddBubble(doc, new PixelPoint(0, i * 10), out var created);
            Assert.True(ids.Add(created.Id), $"collision on {created.Id}");
        }

        await doc.SaveAsync(CancellationToken.None);

        using var reloaded = PcmlDocument.Open(path);
        Assert.Equal(5 + 50, reloaded.Model.Bubbles.Count);
        Assert.All(reloaded.Model.Bubbles.Where(b => b.Id.StartsWith("u")), b => Assert.Matches("^u[0-9a-f]{8}$", b.Id));
    }

    [Fact]
    public void SetCharacter_Adds_To_Chapter_Characters()
    {
        using var doc = OpenValid();
        var bubble = doc.Model.Bubbles[1];
        BubbleMutations.SetCharacter(doc, bubble, "新角色");

        Assert.Equal("新角色", bubble.Character);
        Assert.Contains(doc.Model.Characters, c => c == "新角色");
        Assert.Contains(doc.ContentXml.Root!.Element("characters")!.Elements("character"),
            e => (string?)e.Attribute("name") == "新角色");
    }

    [Fact]
    public void SetCharacter_Existing_Name_Not_Duplicated()
    {
        using var doc = OpenValid();
        var bubble = doc.Model.Bubbles[1];
        BubbleMutations.SetCharacter(doc, bubble, "ピッグ");
        Assert.Equal(1, doc.Model.Characters.Count(c => c == "ピッグ"));
    }

    [Fact]
    public void DeleteBubble_Removes_From_Model_And_Dom()
    {
        using var doc = OpenValid();
        var target = doc.Model.Bubbles[0];
        BubbleMutations.DeleteBubble(doc, target);

        Assert.DoesNotContain(doc.Model.Bubbles, b => b.Id == "b1");
        Assert.Empty(doc.ContentXml.Root!.Element("bubbles")!.Elements("bubble")
            .Where(e => (string?)e.Attribute("id") == "b1"));
    }

    [Fact]
    public void Records_Carry_Before_Values_For_Undo()
    {
        using var doc = OpenValid();
        var bubble = doc.Model.Bubbles[0];
        BubbleMutations.SetPartText(bubble, 1, "最初");
        var rec = BubbleMutations.SetPartText(bubble, 1, "更新");
        Assert.Equal("SetPartText", rec.OpName);
        Assert.Contains("\"before\":\"\\u6700\\u521D\"", rec.Payload); // "最初"
        Assert.Contains("\"id\":\"b1\"", rec.Payload);

        var statusRec = BubbleMutations.SetStatus(bubble, BubbleStatus.Draft);
        Assert.Contains("\"before\":\"Untranslated\"", statusRec.Payload);

        BubbleMutations.SetMarker(bubble, new PixelPoint(0, 0));
        var kindRec = BubbleMutations.SetKind(bubble, BubbleKind.Narration);
        Assert.Contains("\"before\":\"Speech\"", kindRec.Payload);
    }
}

public static class PcDocExtensions
{
    public static System.Xml.Linq.XElement Content(this PcmlDocument doc, string x)
        => doc.ContentXml.Root!;
}