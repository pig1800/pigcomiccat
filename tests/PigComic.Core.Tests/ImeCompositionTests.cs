using PigComic.App.Ime;
using Xunit;

namespace PigComic.Core.Tests;

/// <summary>
/// Unit tests for the SPEC §21 IME composition model (D-40): the mapping from IMM32
/// clause + attribute data to the active henkan segment, and caret clamping.
/// </summary>
public class ImeCompositionTests
{
    [Fact]
    public void ActiveClause_PicksTargetConvertedClause()
    {
        // JA "にほんごをよむ" (7 chars). Clause boundaries are char offsets: [0, 5] =>
        // clauses [0,5),[5,7); the second clause is the active conversion target.
        var text = "にほんごをよむ"; // 7 chars
        var clause = new uint[] { 0, 5 };
        var attr = new byte[]
        {
            ImeComposition.AttrConverted, ImeComposition.AttrConverted, ImeComposition.AttrConverted, ImeComposition.AttrConverted, ImeComposition.AttrConverted,
            ImeComposition.AttrTargetConverted, ImeComposition.AttrTargetConverted,
        };

        var comp = new ImeComposition(text, cursorPosition: 7, clause, attr);
        var active = comp.ActiveClause;

        Assert.NotNull(active);
        Assert.Equal((5, 7), active.Value);
    }

    [Fact]
    public void ActiveClause_TargetNotConverted_AlsoHighlights()
    {
        var comp = new ImeComposition(
            "かんじ", 3,
            new uint[] { 0 },
            new byte[] { ImeComposition.AttrTargetNotConverted, ImeComposition.AttrTargetNotConverted, ImeComposition.AttrTargetNotConverted });

        Assert.Equal((0, 3), comp.ActiveClause);
    }

    [Fact]
    public void ActiveClause_Null_WhenAllInputAttr()
    {
        // KO jamo / pure input: every char ATTR_INPUT → no convertible clause → no highlight.
        var comp = new ImeComposition(
            "한", 1,
            new uint[] { 0 },
            new byte[] { ImeComposition.AttrInput });

        Assert.Null(comp.ActiveClause);
    }

    [Fact]
    public void ActiveClause_Null_WhenNoClauseData()
    {
        var comp = new ImeComposition("nihongo", 7, null, null);
        Assert.Null(comp.ActiveClause);
    }

    [Fact]
    public void CursorPosition_ClampedToTextLength()
    {
        var comp = new ImeComposition("abc", 99, null, null);
        Assert.Equal(3, comp.CursorPosition);

        var neg = new ImeComposition("abc", -5, null, null);
        Assert.Equal(0, neg.CursorPosition);
    }

    [Fact]
    public void ActiveClause_Null_WhenAttrLengthMismatch()
    {
        // Defensive: attr array not matching text length → no highlight (no crash).
        var comp = new ImeComposition("abcd", 2, new uint[] { 0 }, new byte[] { 1, 2 });
        Assert.Null(comp.ActiveClause);
    }

    [Fact]
    public void ActiveClause_LastClauseRunsToTextEnd()
    {
        // Final clause boundary relies on Text.Length as the end.
        var comp = new ImeComposition(
            "あいう", 3,
            new uint[] { 0, 2 },
            new byte[]
            {
                ImeComposition.AttrConverted, ImeComposition.AttrConverted,
                ImeComposition.AttrTargetConverted,
            });

        Assert.Equal((2, 3), comp.ActiveClause);
    }
}
