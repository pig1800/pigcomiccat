using System.Runtime.InteropServices;
using System.Text;

namespace PigComic.App.Ime;

/// <summary>
/// A rich snapshot of an in-progress IMM32 composition: the preedit string, the
/// caret position inside it, and the conversion-clause styling. Avalonia 11.x's
/// Win32 path only forwards the raw composition string (GCS_COMPSTR), never the
/// caret (GCS_CURSORPOS) or clause/attribute data (GCS_COMPCLAUSE/GCS_COMPATTR),
/// which is why the in-composition caret (ZH Pinyin) and the active henkan
/// segment highlight (JA) do not render (SPEC §21 escalation, D-40).
/// </summary>
public sealed class ImeComposition
{
    // IMM32 ATTR_* composition attributes (re-exported so callers/tests need no interop).
    public const byte AttrInput = 0x00;
    public const byte AttrTargetConverted = 0x01;
    public const byte AttrConverted = 0x02;
    public const byte AttrTargetNotConverted = 0x03;
    public const byte AttrInputError = 0x04;
    public const byte AttrFixedConverted = 0x05;

    /// <summary>The full preedit string (GCS_COMPSTR).</summary>
    public string Text { get; }

    /// <summary>Caret position within <see cref="Text"/> (GCS_CURSORPOS), clamped to [0, Text.Length].</summary>
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
    /// The [start, end) span of the "target converted" clause — the active henkan
    /// segment that should be highlighted (reverse-video). Returns null when there
    /// is no convertible clause (plain input / KO jamo / single unconverted run).
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

/// <summary>Minimal IMM32 P/Invoke used to enrich the preedit with caret + clause data.</summary>
internal static class Imm32Native
{
    // GCS_* retrieval flags.
    public const uint GCS_COMPSTR = 0x0008;
    public const uint GCS_COMPATTR = 0x0010;
    public const uint GCS_COMPCLAUSE = 0x0020;
    public const uint GCS_CURSORPOS = 0x0080;

    [DllImport("imm32.dll", SetLastError = true)]
    public static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("imm32.dll")]
    public static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    [DllImport("imm32.dll", SetLastError = false, CharSet = CharSet.Unicode, EntryPoint = "ImmGetCompositionStringW", ExactSpelling = true)]
    private static extern int ImmGetCompositionString(IntPtr hIMC, uint dwIndex, IntPtr lpBuf, uint dwBufLen);

    /// <summary>Reads a byte buffer for the given GCS flag; null when unavailable/empty.</summary>
    private static byte[]? ReadBytes(IntPtr himc, uint flag)
    {
        if (himc == IntPtr.Zero)
        {
            return null;
        }

        var len = ImmGetCompositionString(himc, flag, IntPtr.Zero, 0);
        if (len <= 0)
        {
            return null;
        }

        var buffer = new byte[len];
        var gc = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var read = ImmGetCompositionString(himc, flag, gc.AddrOfPinnedObject(), (uint)len);
            if (read < 0)
            {
                return null;
            }

            if (read < len)
            {
                Array.Resize(ref buffer, read);
            }

            return buffer;
        }
        finally
        {
            gc.Free();
        }
    }

    /// <summary>
    /// Captures the full composition state for a window handle. Returns null when no
    /// IMM context / no active composition. All reads are best-effort: any individual
    /// flag that fails simply contributes no data.
    /// </summary>
    public static ImeComposition? GetComposition(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !OperatingSystem.IsWindows())
        {
            return null;
        }

        var himc = ImmGetContext(hwnd);
        if (himc == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var textBytes = ReadBytes(himc, GCS_COMPSTR);
            var text = textBytes is null ? "" : Encoding.Unicode.GetString(textBytes).TrimEnd('\0');
            if (text.Length == 0)
            {
                return null;
            }

            // Caret position is a single DWORD.
            var cursor = 0;
            var cursorBytes = ReadBytes(himc, GCS_CURSORPOS);
            if (cursorBytes is { Length: >= 4 })
            {
                cursor = BitConverter.ToInt32(cursorBytes, 0);
            }

            // Clause boundaries: array of DWORD character offsets.
            uint[]? clause = null;
            var clauseBytes = ReadBytes(himc, GCS_COMPCLAUSE);
            if (clauseBytes is { Length: >= 8 })
            {
                clause = new uint[clauseBytes.Length / 4];
                Buffer.BlockCopy(clauseBytes, 0, clause, 0, clauseBytes.Length);
            }

            byte[]? attr = ReadBytes(himc, GCS_COMPATTR);

            return new ImeComposition(text, cursor, clause, attr);
        }
        finally
        {
            ImmReleaseContext(hwnd, himc);
        }
    }
}
