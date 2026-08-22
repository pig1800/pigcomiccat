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
/// (no such property exists).</item>
/// <item>On the Win32 backend (Avalonia.Win32 + TSF), an active composition
/// consumes Enter inside the IME: the framework never delivers KeyDown(Enter)
/// to the control while composing, and committed text arrives via TextInput
/// events. The guard's primary effect is therefore: Enter is handled here ONLY
/// if it actually arrives as an unhandled raw key.</item>
/// </list>
/// Enter = confirm (multi-line editors: Shift+Enter = line break), both
/// re-used verbatim by the M5 editor (D-32). Confirmations are counted and
/// exposed so the checklist can detect a leaking path.
/// </summary>
/// <remarks>
/// <para><b>IME rendering (SPEC §21 escalation — JA henkan word + ZH Pinyin
/// cursor).</b> The built-in <see cref="TextBox"/> IME client
/// (TextBoxTextInputMethodClient) renders the live composition inline by
/// setting TextPresenter.PreeditText / PreeditTextCursorPosition; the presenter
/// inserts the preedit string at the caret into a combined layout, underlines
/// it, and moves the caret to CaretIndex + preeditCursorPos.</para>
/// <para>That pipeline is correct ONLY for left-aligned (top) layout. The
/// presenter's caret geometry (<c>UpdateCaret</c> →
/// <c>GetDistanceFromCharacterHit</c>) is computed in the raw, left-origin
/// inline coordinate space, while <c>RenderInternal</c> draws the combined text
/// through <c>TextLayout.Draw</c>, which applies <see cref="TextAlignment"/>.
/// With <c>TextAlignment.Center</c> the composition is drawn at the centered
/// offset but the caret is drawn at the left-flow coordinate, so the caret and
/// the henkan/Pinyin word separate and the in-composition cursor disappears
/// (JA: current word + cursor lost; ZH: Pinyin cursor lost). KO jamo composes a
/// single glyph at the caret so the offset is negligible — which is why KO
/// "seems to work".</para>
/// <para>Therefore this editor must keep <see cref="TextAlignment.Left"/> and
/// <see cref="Avalonia.Layout.VerticalAlignment.Top"/> while focused/composing.
/// The centered look is purely cosmetic; correctness of the composition caret
/// takes priority, so centering is not used here at all (D-39).</para>
/// </remarks>
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
            // Multi-line target editor: top-align so the composition caret stays in-flow
            // (see class remarks). Single-line keeps vertical centering (cosmetic only;
            // it does not affect the horizontal IME caret/composition alignment).
            VerticalContentAlignment = value
                ? Avalonia.Layout.VerticalAlignment.Top
                : Avalonia.Layout.VerticalAlignment.Center;
        }
    }

    public PartTextEditor()
    {
        // Left/top alignment is REQUIRED for correct IME preedit + caret
        // rendering (see class remarks). Do not center horizontally.
        TextAlignment = TextAlignment.Left;
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalContentAlignment = _multiLine
            ? Avalonia.Layout.VerticalAlignment.Top
            : Avalonia.Layout.VerticalAlignment.Center;
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