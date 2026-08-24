namespace PigComic.App.Ime;

/// <summary>
/// A rich snapshot of an in-progress IME composition: the preedit string, the caret
/// position inside it, and the conversion-clause styling.
///
/// Division of labour (D-41, D-43):
/// <list type="bullet">
/// <item>The preedit string and the caret position are supplied by Avalonia itself, via
/// <c>TextInputMethodClient.SetPreeditText(text, cursorPos)</c> — the Win32 backend reads
/// GCS_CURSORPOS since upstream PR #21632 (shipped in 12.1.0). This is what makes the
/// Chinese in-composition caret work; never re-read that flag from IMM32.</item>
/// <item>Conversion clause data (GCS_COMPCLAUSE / GCS_COMPATTR) is still not forwarded by
/// any Avalonia release (upstream issue #21647), so <see cref="ImeMessageMonitor"/> captures
/// it in-message and it arrives here already merged and normalised.</item>
/// </list>
/// </summary>
public sealed class ImeComposition
{
    // IMM32 ATTR_* composition attributes, re-exported so callers/tests need no interop.
    // Canonical definitions live in ImeSegmentBuilder.
    public const byte AttrInput = ImeSegmentBuilder.AttrInput;
    public const byte AttrTargetConverted = ImeSegmentBuilder.AttrTargetConverted;
    public const byte AttrConverted = ImeSegmentBuilder.AttrConverted;
    public const byte AttrTargetNotConverted = ImeSegmentBuilder.AttrTargetNotConverted;
    public const byte AttrInputError = ImeSegmentBuilder.AttrInputError;
    public const byte AttrFixedConverted = ImeSegmentBuilder.AttrFixedConverted;

    private IReadOnlyList<ImeSegment>? _segments;

    /// <summary>The full preedit string, as given to us by Avalonia.</summary>
    public string Text { get; }

    /// <summary>Caret position within <see cref="Text"/>, clamped to [0, Text.Length].</summary>
    public int CursorPosition { get; }

    /// <summary>
    /// Clause boundaries in characters (GCS_COMPCLAUSE), ascending, starting at 0.
    /// May be null when the IME supplies no clause info (e.g. KO jamo, ZH before
    /// conversion). When present, <see cref="Attributes"/> has one entry per char.
    /// </summary>
    public uint[]? ClauseBoundaries { get; }

    /// <summary>Per-character attributes (GCS_COMPATTR): ATTR_* values, or null.</summary>
    public byte[]? Attributes { get; }

    public ImeComposition(string text, int cursorPosition, uint[]? clauseBoundaries, byte[]? attributes)
    {
        Text = text;
        CursorPosition = Math.Clamp(cursorPosition, 0, text.Length);
        ClauseBoundaries = clauseBoundaries;
        Attributes = attributes;
    }

    /// <summary>
    /// The composition split into styled runs (SPEC §21.2). Always tiles the whole preedit;
    /// degrades to a single <see cref="ImeSegmentKind.Input"/> segment when the IME supplies
    /// no usable attributes.
    /// </summary>
    public IReadOnlyList<ImeSegment> Segments =>
        _segments ??= ImeSegmentBuilder.Build(Text, ClauseBoundaries, Attributes);

    /// <summary>
    /// The [start, end) span of the active henkan clause — the converted-and-selected
    /// segment. Null when there is no convertible clause (plain input / KO jamo / a single
    /// unconverted run).
    /// </summary>
    public (int Start, int End)? ActiveClause
    {
        get
        {
            if (ClauseBoundaries is null || Attributes is null ||
                ClauseBoundaries.Length < 1 || Attributes.Length != Text.Length || Text.Length == 0)
            {
                return null;
            }

            // Find the clause that contains a TARGET_CONVERTED (or TARGET_NONCONVERTED)
            // character. Clause boundaries are start offsets of each clause, ascending,
            // with the first being 0.
            for (var clause = 0; clause < ClauseBoundaries.Length; clause++)
            {
                var start = (int)ClauseBoundaries[clause];
                var end = clause + 1 < ClauseBoundaries.Length ? (int)ClauseBoundaries[clause + 1] : Text.Length;
                if (start >= end || start >= Attributes.Length)
                {
                    continue;
                }

                var attr = Attributes[Math.Min(start, Attributes.Length - 1)];
                if (attr is AttrTargetConverted or AttrTargetNotConverted)
                {
                    return (start, Math.Min(end, Text.Length));
                }
            }

            return null;
        }
    }
}
