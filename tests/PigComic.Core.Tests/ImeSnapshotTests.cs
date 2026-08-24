using PigComic.App.Ime;
using Xunit;

namespace PigComic.Core.Tests;

/// <summary>
/// PLAN M2.6: the in-message capture rules — which GCS fields a WM_IME_COMPOSITION message
/// should be read for, how partial updates merge onto the retained snapshot, and how raw
/// clause arrays are normalised. Pure logic; no Windows needed.
/// </summary>
public class ImeSnapshotTests
{
    private const uint CompStr = ImeCompositionSnapshot.GcsCompStr;
    private const uint CompAttr = ImeCompositionSnapshot.GcsCompAttr;
    private const uint CompClause = ImeCompositionSnapshot.GcsCompClause;
    private const uint CursorPos = ImeCompositionSnapshot.GcsCursorPos;

    [Fact]
    public void ShouldRead_HonoursFlaggedFields()
    {
        Assert.True(ImeCompositionSnapshot.ShouldRead(CompStr | CompAttr, CompStr));
        Assert.True(ImeCompositionSnapshot.ShouldRead(CompStr | CompAttr, CompAttr));
        Assert.False(ImeCompositionSnapshot.ShouldRead(CompStr | CompAttr, CompClause));
    }

    [Fact]
    public void ShouldRead_ZeroLParamMeansReadEverything()
    {
        // ATOK sends composition messages with untrustworthy/zero flags (and even one before
        // WM_IME_STARTCOMPOSITION); Gecko's rule is to read every field in that case.
        Assert.True(ImeCompositionSnapshot.ShouldRead(0, CompStr));
        Assert.True(ImeCompositionSnapshot.ShouldRead(0, CompAttr));
        Assert.True(ImeCompositionSnapshot.ShouldRead(0, CompClause));
    }

    [Fact]
    public void Merge_FirstMessageWithNoPreviousSnapshot()
    {
        var snapshot = ImeCompositionSnapshot.Merge(
            previous: null,
            readText: "にほん",
            readClauseBoundaries: [0, 3],
            readAttributes: [0, 0, 0]);

        Assert.Equal("にほん", snapshot.Text);
        Assert.NotNull(snapshot.ClauseBoundaries);
        Assert.Equal([0u, 3u], snapshot.ClauseBoundaries);
        Assert.Equal(new byte[] { 0, 0, 0 }, snapshot.Attributes);
    }

    [Fact]
    public void Merge_RetainsUnreadFieldsFromPreviousMessage()
    {
        var first = ImeCompositionSnapshot.Merge(null, "にほんご", [0, 2, 4], [2, 2, 1, 1]);

        // Second message flags only attributes (the user moved the henkan segment):
        // text and clause boundaries must survive.
        var second = ImeCompositionSnapshot.Merge(first, null, null, [1, 1, 2, 2]);

        Assert.Equal("にほんご", second.Text);
        Assert.NotNull(second.ClauseBoundaries);
        Assert.Equal([0u, 2u, 4u], second.ClauseBoundaries);
        Assert.Equal(new byte[] { 1, 1, 2, 2 }, second.Attributes);
    }

    [Fact]
    public void Merge_NewTextWithoutNewAttributes_KeepsStaleAttributesButConsumerIgnoresThem()
    {
        var first = ImeCompositionSnapshot.Merge(null, "あい", null, [0, 0]);

        // Text grew but attributes were not re-sent: the retained array no longer matches.
        var second = ImeCompositionSnapshot.Merge(first, "あいう", null, null);

        Assert.Equal("あいう", second.Text);
        Assert.Equal(2, second.Attributes!.Length);

        // The segment builder must refuse to use a mismatched attribute array.
        var segments = ImeSegmentBuilder.Build(second.Text, second.ClauseBoundaries, second.Attributes);
        Assert.Equal([new ImeSegment(0, 3, ImeSegmentKind.Input)], segments);
    }

    [Fact]
    public void Merge_EmptyTextProducesEmptySnapshot()
    {
        var snapshot = ImeCompositionSnapshot.Merge(null, "", null, null);
        Assert.Equal("", snapshot.Text);
        Assert.Null(snapshot.ClauseBoundaries);
    }

    [Theory]
    [InlineData(CursorPos, false)] // caret-only update: nothing of ours changed
    [InlineData(CompStr, true)]
    public void ShouldRead_CaretOnlyUpdatesDoNotTriggerTextRead(uint flags, bool expected)
        => Assert.Equal(expected, ImeCompositionSnapshot.ShouldRead(flags, CompStr));

    [Fact]
    public void NormalizeClauses_AddsMissingEndBoundary()
    {
        var normalized = ImeCompositionSnapshot.NormalizeClauses([0, 2], textLength: 5);
        Assert.NotNull(normalized);
        Assert.Equal([0u, 2u, 5u], normalized);
    }

    [Fact]
    public void NormalizeClauses_AddsMissingStartBoundary()
    {
        var normalized = ImeCompositionSnapshot.NormalizeClauses([2, 5], textLength: 5);
        Assert.NotNull(normalized);
        Assert.Equal([0u, 2u, 5u], normalized);
    }

    [Fact]
    public void NormalizeClauses_ConvertsByteOffsetsToCharacterOffsets()
    {
        // Some IMEs report byte offsets: the final boundary is exactly 2 × text length.
        var normalized = ImeCompositionSnapshot.NormalizeClauses([0, 4, 8], textLength: 4);
        Assert.NotNull(normalized);
        Assert.Equal([0u, 2u, 4u], normalized);
    }

    [Fact]
    public void NormalizeClauses_DropsNonMonotonicAndOutOfRangeEntries()
    {
        var normalized = ImeCompositionSnapshot.NormalizeClauses([0, 3, 2, 99], textLength: 6);
        Assert.NotNull(normalized);
        Assert.Equal([0u, 3u, 6u], normalized);
    }

    [Fact]
    public void NormalizeClauses_ReturnsNullWhenNothingUsable()
    {
        Assert.Null(ImeCompositionSnapshot.NormalizeClauses(null, 5));
        Assert.Null(ImeCompositionSnapshot.NormalizeClauses([0], 5));
        Assert.Null(ImeCompositionSnapshot.NormalizeClauses([0, 5], 0));
    }

    [Fact]
    public void MessageSequence_TypicalJapaneseConversion()
    {
        // 1) typing kana — no clauses yet, everything is raw input
        var s = ImeCompositionSnapshot.Merge(null, "にほんご", null, [0, 0, 0, 0]);
        Assert.Equal([new ImeSegment(0, 4, ImeSegmentKind.Input)],
            ImeSegmentBuilder.Build(s.Text, s.ClauseBoundaries, s.Attributes));

        // 2) Space converts: two clauses, the first is the active target
        s = ImeCompositionSnapshot.Merge(s, "日本語", [0, 2, 3], [1, 1, 2]);
        Assert.Equal(
            [
                new ImeSegment(0, 2, ImeSegmentKind.ConvertedTarget),
                new ImeSegment(2, 1, ImeSegmentKind.Converted),
            ],
            ImeSegmentBuilder.Build(s.Text, s.ClauseBoundaries, s.Attributes));

        // 3) → moves the target to the second clause; only attributes are re-sent
        s = ImeCompositionSnapshot.Merge(s, null, null, [2, 2, 1]);
        Assert.Equal(
            [
                new ImeSegment(0, 2, ImeSegmentKind.Converted),
                new ImeSegment(2, 1, ImeSegmentKind.ConvertedTarget),
            ],
            ImeSegmentBuilder.Build(s.Text, s.ClauseBoundaries, s.Attributes));
    }
}
