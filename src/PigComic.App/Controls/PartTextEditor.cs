using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace PigComic.App.Controls;

/// <summary>
/// M2.5/M5.4 target-part text editor with the IME-safe Enter guard (SPEC §21
/// item 4, D-32).
///
/// <para>Guard rationale (spike finding, docs/IME_REPORT.md):</para>
/// <list type="bullet">
/// <item>Avalonia 11.2.5 exposes no public "composition active" flag — verified
/// by reflection on TextInputMethodClient / TextBox / TextInputMethodImpl
/// (no such property exists). A custom TextInputMethodClient would be the
/// escalation path (§21) if the checklist fails.</item>
/// <item>On the Win32 backend (Avalonia.Win32 + TSF), an active composition
/// consumes Enter inside the IME: the framework never delivers KeyDown(Enter)
/// to the control while composing, and committed text arrives via TextInput
/// events. The guard's primary effect is therefore: Enter is handled here ONLY
/// if it actually arrives as an unhandled raw key. The checklist in the spike
/// window verifies this empirically for JA (Microsoft IME) and KO and records
/// PASS/FAIL — this file must not change until that checklist passes.</item>
/// </list>
/// Enter = confirm (multi-line editors: Shift+Enter = line break), both
/// re-used verbatim by the M5 editor (D-32). Confirmations are counted and
/// exposed so the checklist can detect a leaking path.
/// </summary>
public class PartTextEditor : TextBox
{
    private bool _multiLine;

    public event EventHandler? ConfirmRequested;

    /// <summary>Checklist instrumentation: every confirm fired while composing would show here.</summary>
    public long ConfirmCount { get; private set; }

    public bool MultiLine
    {
        get => _multiLine;
        set
        {
            _multiLine = value;
            AcceptsReturn = value;
            TextWrapping = value ? TextWrapping.Wrap : TextWrapping.NoWrap;
            TextAlignment = TextAlignment.Center;
        }
    }

    public PartTextEditor()
    {
        TextAlignment = TextAlignment.Center;
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            if (MultiLine && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                base.OnKeyDown(e);
                e.Handled = true;
                return;
            }

            if (!e.Handled)
            {
                e.Handled = true;
                ConfirmCount++;
                ConfirmRequested?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        base.OnKeyDown(e);
    }
}

public static class PartTextEditorExtensions
{
    /// <summary>Focuses the first part editor of the target column (used by M5).</summary>
    public static void FocusAndSelectAll(this TextBox box)
    {
        box.Focus();
        box.SelectionStart = 0;
        box.SelectionEnd = box.Text?.Length ?? 0;
    }
}