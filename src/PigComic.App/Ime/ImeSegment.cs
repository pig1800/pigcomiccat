namespace PigComic.App.Ime;

/// <summary>
/// What an IME says a stretch of the composition *is*. Names deliberately mirror upstream
/// Avalonia's <c>TextInputDecorationKind</c> (PR #20890) so that if that work ever ships,
/// converging is a rename-free deletion rather than a rewrite (D-44).
/// </summary>
public enum ImeSegmentKind
{
    /// <summary>Freshly typed, not yet converted (JA kana, ZH pinyin, KO jamo).</summary>
    Input,

    /// <summary>Converted, but not the clause the user is currently working on.</summary>
    Converted,

    /// <summary>The active henkan clause — converted and selected. This is the one that
    /// gets the highlight, and the one users navigate with the arrow keys.</summary>
    ConvertedTarget,

    /// <summary>The active clause, before conversion has been applied to it.</summary>
    TargetNotConverted,

    /// <summary>The IME flagged this stretch as erroneous input.</summary>
    InputError,
}

/// <summary>A contiguous run of composition text sharing one <see cref="ImeSegmentKind"/>.</summary>
public readonly record struct ImeSegment(int Start, int Length, ImeSegmentKind Kind)
{
    public int End => Start + Length;
}

/// <summary>
/// Turns raw IMM32 composition attributes into the segment list the presenter renders
/// (PLAN M2.7, SPEC §21.2).
/// </summary>
public static class ImeSegmentBuilder
{
    // IMM32 ATTR_* values, one byte per composition character.
    public const byte AttrInput = 0x00;
    public const byte AttrTargetConverted = 0x01;
    public const byte AttrConverted = 0x02;
    public const byte AttrTargetNotConverted = 0x03;
    public const byte AttrInputError = 0x04;
    public const byte AttrFixedConverted = 0x05;

    public static ImeSegmentKind KindFromAttribute(byte attribute) => attribute switch
    {
        AttrTargetConverted => ImeSegmentKind.ConvertedTarget,
        AttrConverted => ImeSegmentKind.Converted,
        AttrTargetNotConverted => ImeSegmentKind.TargetNotConverted,
        AttrInputError => ImeSegmentKind.InputError,
        AttrFixedConverted => ImeSegmentKind.Converted,
        _ => ImeSegmentKind.Input,
    };

    /// <summary>
    /// Builds the segments covering <paramref name="text"/>.
    ///
    /// <para>Attributes drive the segmentation; clause boundaries only *refine* it, by
    /// forbidding a segment from spanning a clause edge. When attributes are missing or
    /// their length disagrees with the text (which happens with IMEs that supply no clause
    /// data at all — KO jamo, ZH pinyin before conversion), the whole preedit degrades to a
    /// single <see cref="ImeSegmentKind.Input"/> segment. That degrade path is what keeps
    /// non-Japanese IMEs rendering correctly.</para>
    ///
    /// <para>The returned segments always tile <c>[0, text.Length)</c> exactly, with no gaps
    /// and no overlaps, so the renderer can style every character exactly once.</para>
    /// </summary>
    public static IReadOnlyList<ImeSegment> Build(string? text, uint[]? clauseBoundaries, byte[]? attributes)
    {
        var length = text?.Length ?? 0;
        if (length == 0)
        {
            return [];
        }

        if (attributes is null || attributes.Length != length)
        {
            return [new ImeSegment(0, length, ImeSegmentKind.Input)];
        }

        // Per-character clause index, so a run break is forced at every clause edge.
        var clauseOf = BuildClauseIndex(clauseBoundaries, length);

        var segments = new List<ImeSegment>();
        var runStart = 0;
        var runKind = KindFromAttribute(attributes[0]);
        var runClause = clauseOf?[0] ?? 0;

        for (var i = 1; i < length; i++)
        {
            var kind = KindFromAttribute(attributes[i]);
            var clause = clauseOf?[i] ?? 0;

            if (kind != runKind || clause != runClause)
            {
                segments.Add(new ImeSegment(runStart, i - runStart, runKind));
                runStart = i;
                runKind = kind;
                runClause = clause;
            }
        }

        segments.Add(new ImeSegment(runStart, length - runStart, runKind));
        return segments;
    }

    /// <summary>Maps each character to the index of the clause containing it, or null when
    /// there are no usable boundaries.</summary>
    private static int[]? BuildClauseIndex(uint[]? clauseBoundaries, int length)
    {
        if (clauseBoundaries is null || clauseBoundaries.Length < 2)
        {
            return null;
        }

        var clauseOf = new int[length];
        for (var clause = 0; clause + 1 < clauseBoundaries.Length; clause++)
        {
            var start = (int)Math.Min(clauseBoundaries[clause], (uint)length);
            var end = (int)Math.Min(clauseBoundaries[clause + 1], (uint)length);
            for (var i = start; i < end; i++)
            {
                clauseOf[i] = clause;
            }
        }

        return clauseOf;
    }
}
