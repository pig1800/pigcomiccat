namespace PigComic.App.Ime;

/// <summary>
/// The retained state of one in-progress IMM32 composition, merged across the
/// <c>WM_IME_COMPOSITION</c> messages that build it up (PLAN M2.6, SPEC §21.0).
///
/// <para>Why this type exists: <c>ImmGetCompositionString</c> is only contractually valid
/// while handling the composition message — MSDN: "the IMM removes the information when the
/// application calls <c>ImmReleaseContext</c>". Reading clause data later (which PigComic
/// used to do, from inside <c>SetPreeditText</c>, after Avalonia had already run its own
/// get/release cycle) is off-contract; MS-IME tolerated it, ATOK returned nothing at all.
/// <see cref="ImeMessageMonitor"/> therefore captures a snapshot synchronously on the
/// message stack and everything downstream reads this instead of IMM32.</para>
///
/// <para>All members here are pure and platform-independent so the merge/normalisation
/// rules can be unit-tested without Windows.</para>
/// </summary>
public sealed record ImeCompositionSnapshot(string Text, uint[]? ClauseBoundaries, byte[]? Attributes)
{
    // GCS_* retrieval flags, as they appear in WM_IME_COMPOSITION's lParam.
    public const uint GcsCompStr = 0x0008;
    public const uint GcsCompAttr = 0x0010;
    public const uint GcsCompClause = 0x0020;
    public const uint GcsCursorPos = 0x0080;
    public const uint GcsResultStr = 0x0800;

    /// <summary>
    /// Whether a given GCS field should be read for this message. lParam carries the set of
    /// fields that *changed*; a zero lParam is the "cannot be trusted, read everything" case
    /// that Gecko documents (some IMEs, ATOK among them, send composition messages with no
    /// usable flags — ATOK even sends one before WM_IME_STARTCOMPOSITION).
    /// </summary>
    public static bool ShouldRead(uint lParamFlags, uint gcsFlag)
        => lParamFlags == 0 || (lParamFlags & gcsFlag) != 0;

    /// <summary>
    /// Folds one message's freshly-read fields onto the previous snapshot. A null argument
    /// means "not flagged in lParam this time, so not read" and the previous value is
    /// retained — IMEs update only what changed (e.g. moving the henkan segment with the
    /// arrow keys re-sends attributes but not necessarily the composition string).
    /// Clause boundaries are normalised against the resulting text length.
    /// </summary>
    public static ImeCompositionSnapshot Merge(
        ImeCompositionSnapshot? previous,
        string? readText,
        uint[]? readClauseBoundaries,
        byte[]? readAttributes)
    {
        var text = readText ?? previous?.Text ?? "";
        var clauses = readClauseBoundaries ?? previous?.ClauseBoundaries;
        var attributes = readAttributes ?? previous?.Attributes;

        return new ImeCompositionSnapshot(text, NormalizeClauses(clauses, text.Length), attributes);
    }

    /// <summary>
    /// Cleans up a raw GCS_COMPCLAUSE array into ascending character offsets that start at 0
    /// and end at <paramref name="textLength"/>.
    ///
    /// <para>Defensive because the raw data is not uniform across IMEs: the documented unit
    /// for the wide API is characters, but some IMEs report byte offsets (detected here when
    /// the final boundary is exactly twice the text length — the same normalisation upstream
    /// Avalonia PR #21648 found necessary), and out-of-order or out-of-range entries appear
    /// in the wild. Returns null when nothing meaningful survives, which makes callers fall
    /// back to attribute runs alone.</para>
    /// </summary>
    public static uint[]? NormalizeClauses(uint[]? raw, int textLength)
    {
        if (raw is null || raw.Length < 2 || textLength <= 0)
        {
            return null;
        }

        var values = raw;

        // Byte offsets (UTF-16 code units × 2) rather than character offsets.
        if (values[^1] == textLength * 2L && textLength * 2L <= uint.MaxValue)
        {
            values = new uint[raw.Length];
            for (var i = 0; i < raw.Length; i++)
            {
                values[i] = raw[i] / 2;
            }
        }

        var cleaned = new List<uint>(values.Length);
        foreach (var value in values)
        {
            var clamped = value > (uint)textLength ? (uint)textLength : value;

            // Keep strictly ascending; drop duplicates and any non-monotonic entry.
            if (cleaned.Count == 0)
            {
                cleaned.Add(clamped);
            }
            else if (clamped > cleaned[^1])
            {
                cleaned.Add(clamped);
            }
        }

        if (cleaned.Count == 0 || cleaned[0] != 0)
        {
            cleaned.Insert(0, 0);
        }

        if (cleaned[^1] != (uint)textLength)
        {
            cleaned.Add((uint)textLength);
        }

        // Fewer than two boundaries describes no clause at all.
        return cleaned.Count >= 2 ? cleaned.ToArray() : null;
    }
}
