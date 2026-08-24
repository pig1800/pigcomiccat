using PigComic.App.Ime;
using Xunit;

namespace PigComic.Core.Tests;

/// <summary>
/// PLAN M2.7 / SPEC §21.2: turning IMM32 attribute bytes into the styled segments the
/// presenter renders in the modern flavor.
/// </summary>
public class ImeSegmentTests
{
    [Theory]
    [InlineData(ImeSegmentBuilder.AttrInput, ImeSegmentKind.Input)]
    [InlineData(ImeSegmentBuilder.AttrTargetConverted, ImeSegmentKind.ConvertedTarget)]
    [InlineData(ImeSegmentBuilder.AttrConverted, ImeSegmentKind.Converted)]
    [InlineData(ImeSegmentBuilder.AttrTargetNotConverted, ImeSegmentKind.TargetNotConverted)]
    [InlineData(ImeSegmentBuilder.AttrInputError, ImeSegmentKind.InputError)]
    [InlineData(ImeSegmentBuilder.AttrFixedConverted, ImeSegmentKind.Converted)]
    [InlineData((byte)0x7F, ImeSegmentKind.Input)] // unknown attribute degrades to Input
    public void KindFromAttribute_MapsEveryDocumentedValue(byte attribute, ImeSegmentKind expected)
        => Assert.Equal(expected, ImeSegmentBuilder.KindFromAttribute(attribute));

    [Fact]
    public void Build_EmptyTextYieldsNoSegments()
        => Assert.Empty(ImeSegmentBuilder.Build("", null, null));

    [Fact]
    public void Build_NoAttributes_DegradesToSingleInputSegment()
    {
        // The ZH pinyin / KO jamo case: no clause data at all. Must still tile the preedit,
        // so the composition renders (and its caret stays visible).
        var segments = ImeSegmentBuilder.Build("nihao", null, null);
        Assert.Equal([new ImeSegment(0, 5, ImeSegmentKind.Input)], segments);
    }

    [Fact]
    public void Build_AttributeLengthMismatch_DegradesToSingleInputSegment()
    {
        var segments = ImeSegmentBuilder.Build("あいう", [0, 3], [1, 1]);
        Assert.Equal([new ImeSegment(0, 3, ImeSegmentKind.Input)], segments);
    }

    [Fact]
    public void Build_GroupsContiguousRunsOfTheSameKind()
    {
        // にほんご: first clause converted, second is the active target.
        var segments = ImeSegmentBuilder.Build(
            "にほんご",
            clauseBoundaries: null,
            attributes: [ImeSegmentBuilder.AttrConverted, ImeSegmentBuilder.AttrConverted,
                         ImeSegmentBuilder.AttrTargetConverted, ImeSegmentBuilder.AttrTargetConverted]);

        Assert.Equal(
            [
                new ImeSegment(0, 2, ImeSegmentKind.Converted),
                new ImeSegment(2, 2, ImeSegmentKind.ConvertedTarget),
            ],
            segments);
    }

    [Fact]
    public void Build_ClauseBoundariesSplitRunsOfIdenticalAttributes()
    {
        // Two clauses that happen to share an attribute value must not merge into one
        // segment — the boundary is meaningful to the user navigating with ←/→.
        var segments = ImeSegmentBuilder.Build(
            "あいうえ",
            clauseBoundaries: [0, 2, 4],
            attributes: [ImeSegmentBuilder.AttrConverted, ImeSegmentBuilder.AttrConverted,
                         ImeSegmentBuilder.AttrConverted, ImeSegmentBuilder.AttrConverted]);

        Assert.Equal(
            [
                new ImeSegment(0, 2, ImeSegmentKind.Converted),
                new ImeSegment(2, 2, ImeSegmentKind.Converted),
            ],
            segments);
    }

    [Fact]
    public void Build_SegmentsAlwaysTileTheWholePreeditExactly()
    {
        var text = "かんじへんかん";
        var segments = ImeSegmentBuilder.Build(
            text,
            clauseBoundaries: [0, 2, 5, 7],
            attributes: [2, 2, 1, 1, 1, 0, 0]);

        var covered = 0;
        var expectedStart = 0;
        foreach (var segment in segments)
        {
            Assert.Equal(expectedStart, segment.Start);
            Assert.True(segment.Length > 0);
            expectedStart = segment.End;
            covered += segment.Length;
        }

        Assert.Equal(text.Length, covered);
        Assert.Equal(text.Length, expectedStart);
    }

    [Fact]
    public void Build_SingleTargetClauseIsFoundWhateverItsPosition()
    {
        var segments = ImeSegmentBuilder.Build(
            "あいう",
            clauseBoundaries: [0, 1, 2, 3],
            attributes: [ImeSegmentBuilder.AttrConverted, ImeSegmentBuilder.AttrTargetNotConverted,
                         ImeSegmentBuilder.AttrConverted]);

        Assert.Equal(ImeSegmentKind.TargetNotConverted, segments[1].Kind);
        Assert.Equal(1, segments[1].Start);
        Assert.Equal(1, segments[1].Length);
    }

    [Fact]
    public void Composition_ExposesSegmentsAndKeepsCaretClamped()
    {
        var composition = new ImeComposition(
            "にほんご", cursorPosition: 99,
            clauseBoundaries: [0, 2, 4],
            attributes: [2, 2, 1, 1]);

        Assert.Equal(4, composition.CursorPosition); // clamped, caret stays inside the preedit
        Assert.Equal(2, composition.Segments.Count);
        Assert.Equal(ImeSegmentKind.ConvertedTarget, composition.Segments[1].Kind);
    }
}
