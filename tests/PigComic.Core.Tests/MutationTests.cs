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
        var path = PcmlTestBuilder.New("t", "ja", "zh-Hant").Chapter("1")
            .Character("ピッグ")
            .Page("p1", "a.jpg", 1000, 2000)
            .Bubble("p1-b1", "p1", 1, "Speech", "Untranslated", "ピッグ",
                new PixelRect(100, 100, 300, 100), "こんにちは")
            .Bubble("p1-b2", "p1", 2, "Speech", "Untranslated", null,
                new PixelRect(100, 500, 300, 100), "さようなら")
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
        // Source region 100x100 split into 3 horizontal bands (34+33+33).
        Assert.Equal(100, bubble.Parts[0].Region.Y);
        Assert.Equal(134, bubble.Parts[1].Region.Y);
        Assert.Equal(167, bubble.Parts[2].Region.Y);

        BubbleMutations.SetPartText(bubble, 1, "一");
        BubbleMutations.SetPartText(bubble, 2, "二");
        BubbleMutations.SetPartText(bubble, 3, "三");

        BubbleMutations.SetPartCount(bubble, 1);
        Assert.Single(bubble.Parts);
        Assert.Equal("一\n二\n三", bubble.Parts[0].Text);
        Assert.Equal(new PixelRect(100, 100, 300, 100), bubble.Parts[0].Region);
    }

    [Fact]
    public void AddBubble_Between_Bubbles_Gets_Order_Between_And_Renumbers()
    {
        using var doc = OpenValid();
        var bubble = doc.Model.Bubbles[0];
        BubbleMutations.SetPartText(bubble, 1, "x");

        BubbleMutations.AddBubble(doc, "p1", new PixelRect(100, 300, 300, 100), out var created);
        Assert.StartsWith("u", created.Id);
        Assert.Equal(9, created.Id.Length); // "u" + 8 hex chars
        Assert.Equal("Speech", created.Kind.ToString());
        Assert.Equal(BubbleStatus.Untranslated, created.Status);
        Assert.Equal("", created.SourceText);
        Assert.Single(created.Parts);
        Assert.Equal(new PixelRect(100, 300, 300, 100), created.Parts[0].Region);

        // Orders: original Y=100 -> 1, new Y=300 -> 2, original Y=500 -> 3.
        var docOrders = doc.Model.Bubbles.OrderBy(b => b.Order).Select(b => b.Order).ToList();
        Assert.Equal([1, 2, 3], docOrders);
        var ids = doc.Model.Bubbles.OrderBy(b => b.Order).Select(b => b.Id).ToList();
        Assert.Equal(["p1-b1", created.Id, "p1-b2"], ids);
    }

    [Fact]
    public void AddBubble_At_Top_Gets_Order_1()
    {
        using var doc = OpenValid();
        BubbleMutations.AddBubble(doc, "p1", new PixelRect(100, 0, 300, 100), out var created);
        Assert.Equal(1, created.Order);
        Assert.Equal([1, 2, 3], doc.Model.Bubbles.OrderBy(b => b.Order).Select(b => b.Order).ToList());
        var b1 = doc.Model.Bubbles.First(b => b.Id == "p1-b1");
        Assert.Equal(2, b1.Order);
    }

    [Fact]
    public async Task AddBubble_Id_Collision_Checked()
    {
        var builder = PcmlTestBuilder.New("t", "ja", "zh-Hant").Chapter("1")
            .Page("p1", "a.jpg", 1000, 2000);
        for (var i = 1; i <= 5; i++)
        {
            builder.Bubble($"pb{i}", "p1", i, "Speech", "Untranslated", source: $"{i}");
        }

        var path = builder.BuildZip(Path.Combine(_tempDir, "col.pcml"));
        using var doc = PcmlDocument.Open(path);
        var ids = new HashSet<string>(doc.Model.Bubbles.Select(b => b.Id));
        for (var i = 0; i < 50; i++)
        {
            BubbleMutations.AddBubble(doc, "p1", new PixelRect(0, i * 10, 10, 10), out var created);
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

        Assert.DoesNotContain(doc.Model.Bubbles, b => b.Id == "p1-b1");
        Assert.Empty(doc.ContentXml.Root!.Element("bubbles")!.Elements("bubble")
            .Where(e => (string?)e.Attribute("id") == "p1-b1"));
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
        Assert.Contains("\"id\":\"p1-b1\"", rec.Payload);

        var statusRec = BubbleMutations.SetStatus(bubble, BubbleStatus.Draft);
        Assert.Contains("\"before\":\"Untranslated\"", statusRec.Payload);

        BubbleMutations.SetSourceRegion(bubble, new PixelRect(0, 0, 5, 5));
        var kindRec = BubbleMutations.SetKind(bubble, BubbleKind.Narration);
        Assert.Contains("\"before\":\"Speech\"", kindRec.Payload);
    }
}

public static class PcDocExtensions
{
    public static System.Xml.Linq.XElement Content(this PcmlDocument doc, string x)
        => doc.ContentXml.Root!;
}