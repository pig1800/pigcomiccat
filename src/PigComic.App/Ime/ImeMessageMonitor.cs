using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;

namespace PigComic.App.Ime;

/// <summary>
/// Captures IMM32 conversion-clause data **synchronously, inside the WM_IME_COMPOSITION
/// message**, and hands it to <see cref="ImeTextBoxInputMethodClient"/> (PLAN M2.6,
/// SPEC §21.0).
///
/// <para><b>Why a WndProc hook.</b> <c>ImmGetCompositionString</c> is only valid while the
/// composition message is being handled; MSDN states the IMM removes the information once
/// <c>ImmReleaseContext</c> is called. PigComic previously read clause data from
/// <c>SetPreeditText</c> — i.e. after Avalonia's own <c>Imm32InputMethod</c> had already
/// done a get/release cycle for that message. MS-IME tolerated the late read; ATOK returned
/// nothing, which is why no henkan highlight appeared. Avalonia 12.1.1 exposes
/// <c>Win32Properties.AddWndProcHookCallback</c>, whose hooks run *before* any Avalonia
/// handling, so we read first and cache the result.</para>
///
/// <para><b>What this does NOT read: GCS_CURSORPOS.</b> Avalonia 12.1.0+ reads the caret
/// itself and passes it to <c>SetPreeditText(text, cursorPos)</c> (upstream PR #21632). That
/// is the authoritative source and the reason the Chinese in-composition caret works; never
/// re-read it here (D-41).</para>
///
/// <para>The hook is strictly an observer: it never sets <c>handled</c>, so every message
/// continues to Avalonia exactly as before.</para>
/// </summary>
public static class ImeMessageMonitor
{
    private const uint WmImeStartComposition = 0x010D;
    private const uint WmImeEndComposition = 0x010E;
    private const uint WmImeComposition = 0x010F;

    private sealed class Registration
    {
        public int RefCount;
        public Win32Properties.CustomWndProcHookCallback Callback = null!;
        public ImeCompositionSnapshot? Snapshot;
    }

    private static readonly Dictionary<TopLevel, Registration> Registrations = [];

    /// <summary>Number of TopLevels currently hooked (smoke-check instrumentation).</summary>
    public static int AttachedCount
    {
        get
        {
            lock (Registrations)
            {
                return Registrations.Count;
            }
        }
    }

    /// <summary>
    /// Escape hatch. When false the hook stays installed but captures nothing, so the editor
    /// falls back to rendering the whole preedit as plain input — i.e. the behaviour before
    /// PLAN M2.6. Exists because reading IMM32 inside the message is the one part of this
    /// design that cannot be verified without a real IME session: if composition itself ever
    /// misbehaves, turning this off is the instant A/B that isolates the cause.
    /// Also settable out-of-band via <c>PIGCOMIC_IME_NO_HOOK=1</c> for a session that cannot
    /// reach the debug UI.
    /// </summary>
    public static bool CaptureEnabled { get; set; } =
        Environment.GetEnvironmentVariable("PIGCOMIC_IME_NO_HOOK") is not "1";

    /// <summary>
    /// When true, every composition message is appended as one JSON line to
    /// <see cref="DiagnosticsPath"/>. Off by default; the IME gate window toggles it so the
    /// owner can capture one real session per IME (PLAN M2.6 acceptance).
    /// </summary>
    public static bool DiagnosticsEnabled { get; set; }

    public static string DiagnosticsPath { get; } =
        Path.Combine(Path.GetTempPath(), "pigcomic-ime-diag.log");

    /// <summary>Installs the hook for the TopLevel hosting an IME-aware editor. Reference
    /// counted: several editors in one window share a single hook.</summary>
    public static void Attach(TopLevel? topLevel)
    {
        if (topLevel is null || !OperatingSystem.IsWindows())
        {
            return;
        }

        lock (Registrations)
        {
            if (Registrations.TryGetValue(topLevel, out var existing))
            {
                existing.RefCount++;
                return;
            }

            var registration = new Registration { RefCount = 1 };
            registration.Callback = (IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                // Observer only — never set handled; Avalonia must see every message.
                OnMessage(registration, hwnd, msg, lParam);
                return IntPtr.Zero;
            };

            Registrations[topLevel] = registration;
            Win32Properties.AddWndProcHookCallback(topLevel, registration.Callback);
        }
    }

    /// <summary>Removes one reference; uninstalls the hook when the last editor detaches.</summary>
    public static void Detach(TopLevel? topLevel)
    {
        if (topLevel is null || !OperatingSystem.IsWindows())
        {
            return;
        }

        lock (Registrations)
        {
            if (!Registrations.TryGetValue(topLevel, out var registration))
            {
                return;
            }

            if (--registration.RefCount > 0)
            {
                return;
            }

            Win32Properties.RemoveWndProcHookCallback(topLevel, registration.Callback);
            Registrations.Remove(topLevel);
        }
    }

    /// <summary>Whether this window currently has the composition hook installed.</summary>
    public static bool IsAttached(TopLevel? topLevel)
    {
        if (topLevel is null)
        {
            return false;
        }

        lock (Registrations)
        {
            return Registrations.ContainsKey(topLevel);
        }
    }

    /// <summary>The clause data captured for the composition currently running in this
    /// window, or null when there is none.</summary>
    public static ImeCompositionSnapshot? TryGetSnapshot(TopLevel? topLevel)
    {
        if (topLevel is null)
        {
            return null;
        }

        lock (Registrations)
        {
            return Registrations.TryGetValue(topLevel, out var registration) ? registration.Snapshot : null;
        }
    }

    private static void OnMessage(Registration registration, IntPtr hwnd, uint msg, IntPtr lParam)
    {
        if (!CaptureEnabled)
        {
            registration.Snapshot = null;
            return;
        }

        switch (msg)
        {
            case WmImeStartComposition:
                registration.Snapshot = null;
                break;

            case WmImeEndComposition:
                registration.Snapshot = null;
                break;

            case WmImeComposition:
                registration.Snapshot = Capture(hwnd, unchecked((uint)lParam.ToInt64()), registration.Snapshot);
                break;
        }
    }

    private static ImeCompositionSnapshot? Capture(IntPtr hwnd, uint flags, ImeCompositionSnapshot? previous)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero)
        {
            return previous;
        }

        // A result string with no composition string means the IME just committed:
        // the composition is over and any retained clause data is stale.
        if ((flags & ImeCompositionSnapshot.GcsResultStr) != 0 &&
            (flags & ImeCompositionSnapshot.GcsCompStr) == 0)
        {
            return null;
        }

        var himc = ImmGetContext(hwnd);
        if (himc == IntPtr.Zero)
        {
            return previous;
        }

        try
        {
            string? text = null;
            uint[]? clauses = null;
            byte[]? attributes = null;
            int textBytes = 0, clauseBytes = 0, attrBytes = 0;

            if (ImeCompositionSnapshot.ShouldRead(flags, ImeCompositionSnapshot.GcsCompStr))
            {
                var raw = ReadBytes(himc, ImeCompositionSnapshot.GcsCompStr);
                textBytes = raw?.Length ?? 0;

                // We asked, so whatever came back is the truth — including "nothing", which
                // means the composition was cleared. Leaving this null would instead retain
                // the previous text and keep a dead snapshot alive.
                text = raw is null ? "" : Encoding.Unicode.GetString(raw).TrimEnd('\0');
            }

            if (ImeCompositionSnapshot.ShouldRead(flags, ImeCompositionSnapshot.GcsCompClause))
            {
                var raw = ReadBytes(himc, ImeCompositionSnapshot.GcsCompClause);
                clauseBytes = raw?.Length ?? 0;
                if (raw is { Length: >= 8 })
                {
                    clauses = new uint[raw.Length / 4];
                    Buffer.BlockCopy(raw, 0, clauses, 0, clauses.Length * 4);
                }
            }

            if (ImeCompositionSnapshot.ShouldRead(flags, ImeCompositionSnapshot.GcsCompAttr))
            {
                attributes = ReadBytes(himc, ImeCompositionSnapshot.GcsCompAttr);
                attrBytes = attributes?.Length ?? 0;
            }

            var merged = ImeCompositionSnapshot.Merge(previous, text, clauses, attributes);

            if (DiagnosticsEnabled)
            {
                LogDiagnostics(flags, merged, textBytes, clauseBytes, attrBytes);
            }

            return merged.Text.Length == 0 ? null : merged;
        }
        finally
        {
            ImmReleaseContext(hwnd, himc);
        }
    }

    private static void LogDiagnostics(
        uint flags, ImeCompositionSnapshot snapshot, int textBytes, int clauseBytes, int attrBytes)
    {
        try
        {
            var names = new List<string>();
            if ((flags & ImeCompositionSnapshot.GcsCompStr) != 0) names.Add("COMPSTR");
            if ((flags & ImeCompositionSnapshot.GcsCompAttr) != 0) names.Add("COMPATTR");
            if ((flags & ImeCompositionSnapshot.GcsCompClause) != 0) names.Add("COMPCLAUSE");
            if ((flags & ImeCompositionSnapshot.GcsCursorPos) != 0) names.Add("CURSORPOS");
            if ((flags & ImeCompositionSnapshot.GcsResultStr) != 0) names.Add("RESULTSTR");

            var line = new StringBuilder();
            line.Append("{\"utc\":\"").Append(DateTime.UtcNow.ToString("O")).Append('"');
            line.Append(",\"lParam\":\"0x").Append(flags.ToString("X8")).Append('"');
            line.Append(",\"flags\":[").Append(string.Join(",", names.Select(n => $"\"{n}\""))).Append(']');
            line.Append(",\"bytes\":{\"compstr\":").Append(textBytes)
                .Append(",\"clause\":").Append(clauseBytes)
                .Append(",\"attr\":").Append(attrBytes).Append('}');
            line.Append(",\"text\":").Append(JsonString(snapshot.Text));
            line.Append(",\"clauses\":[")
                .Append(snapshot.ClauseBoundaries is null ? "" : string.Join(",", snapshot.ClauseBoundaries))
                .Append(']');
            line.Append(",\"attrs\":[")
                .Append(snapshot.Attributes is null ? "" : string.Join(",", snapshot.Attributes))
                .Append(']');
            line.Append('}');

            File.AppendAllText(DiagnosticsPath, line.ToString() + Environment.NewLine);
        }
        catch (Exception)
        {
            // Diagnostics must never disturb input handling.
        }
    }

    private static string JsonString(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            if (c is '"' or '\\')
            {
                sb.Append('\\').Append(c);
            }
            else if (c < 0x20 || c > 0x7E)
            {
                sb.Append("\\u").Append(((int)c).ToString("x4"));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.Append('"').ToString();
    }

    /// <summary>Reads one GCS field into a byte buffer; null when unavailable/empty.</summary>
    private static byte[]? ReadBytes(IntPtr himc, uint flag)
    {
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

    // The app targets plain net8.0, so every imm32 entry point stays behind
    // OperatingSystem.IsWindows() at the call sites above.
    [DllImport("imm32.dll", SetLastError = false, ExactSpelling = true)]
    private static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("imm32.dll", SetLastError = false, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    [DllImport("imm32.dll", SetLastError = false, CharSet = CharSet.Unicode,
        EntryPoint = "ImmGetCompositionStringW", ExactSpelling = true)]
    private static extern int ImmGetCompositionString(IntPtr hIMC, uint dwIndex, IntPtr lpBuf, uint dwBufLen);
}
