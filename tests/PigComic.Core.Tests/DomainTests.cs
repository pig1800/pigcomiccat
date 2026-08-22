using PigComic.Core.Domain;
using Xunit;

namespace PigComic.Core.Tests;

public class DomainTests
{
    [Fact]
    public void Bubble_With_Zero_Parts_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bubble(
            "b1", "p1", 1, BubbleKind.Speech, parts: Array.Empty<TargetPart>()));
    }

    [Fact]
    public void Bubble_With_Four_Parts_Throws()
    {
        var parts = Enumerable.Range(1, 4).Select(_ => new TargetPart(1, new PixelRect(0, 0, 10, 10), ""))
            .ToList();
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bubble(
            "b1", "p1", 1, BubbleKind.Speech, parts: parts));
    }

    [Fact]
    public void TargetJoined_Joins_Parts_With_Newline()
    {
        var bubble = new Bubble(
            "b1", "p1", 1, BubbleKind.Speech,
            parts: new List<TargetPart>
            {
                new(1, new PixelRect(0, 0, 10, 10), "a"),
                new(2, new PixelRect(0, 10, 10, 10), "b"),
            });

        Assert.Equal("a\nb", bubble.TargetJoined);
    }

    [Fact]
    public void TargetPart_Index_Outside_1_3_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TargetPart(0, new PixelRect(0, 0, 1, 1), ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TargetPart(4, new PixelRect(0, 0, 1, 1), ""));
    }

    [Fact]
    public void TargetPart_Text_Keeps_Lf_Only()
    {
        var part = new TargetPart(1, new PixelRect(0, 0, 1, 1), "a\r\nb");
        Assert.Equal("a\nb", part.Text);
    }
}