using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

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
/// <para><b>IME rendering (SPEC §21 escalation, D-40).</b> This editor installs a
/// clause-aware IME client (<see cref="Ime.ImeTextBoxInputMethodClient"/>) and a
/// clause-aware presenter (<see cref="Ime.ImeTextPresenter"/>, via the
/// <c>PartTextEditorTheme</c>) because Avalonia 11.x's Win32 IMM32 path forwards only
/// the raw composition string — never the in-composition caret (GCS_CURSORPOS) or the
/// conversion-clause data (GCS_COMPCLAUSE/COMPATTR). Without those, the ZH Pinyin
/// caret and the JA henkan-segment highlight cannot render. The custom client queries
/// the focused window's IMM context for that data on each composition update and never
/// mutates the committed document text.</para>
/// <para>Text stays left-aligned (<see cref="TextAlignment.Left"/>) and the multi-line
/// editor is top-aligned so the composition caret stays in-flow; centering is cosmetic
/// and not used here.</para>
/// </remarks>
public class PartTextEditor : TextBox
{
    private bool _multiLine;
    private readonly Ime.ImeTextBoxInputMethodClient _imClient = new();
    private TextPresenter? _presenter;
    private TopLevel? _monitoredTopLevel;

    public event EventHandler? ConfirmRequested;

    /// <summary>Checklist instrumentation: every confirm fired while composing would show here.</summary>
    public long ConfirmCount { get; private set; }

    /// <summary>
    /// The templated presenter, exposed so the <c>--smoke</c> self-check can verify that the
    /// PartTextEditorTheme actually applied and produced an <see cref="Ime.ImeTextPresenter"/>.
    /// Null until <see cref="OnApplyTemplate"/> runs.
    /// </summary>
    internal TextPresenter? TemplatePresenter => _presenter;

    /// <summary>
    /// When true (default), Enter fires <see cref="ConfirmRequested"/> and is marked handled
    /// (SPEC §21 item 4 guard for the part editor). Set false in contexts where Enter must
    /// fall through to a default button (e.g., dialogs).
    /// </summary>
    public bool ConfirmOnEnter { get; set; } = true;

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

        // Use the clause-aware theme (PART_TextPresenter = ImeTextPresenter) when available.
        if (Application.Current?.TryFindResource("PartTextEditorTheme", out var theme) == true)
        {
            Theme = theme as ControlTheme;
        }

        // Install the clause-aware IME client on the TUNNEL route and mark handled so the
        // base TextBox bubble class-handler (which sets its plain client) is skipped.
        AddHandler(
            InputElement.TextInputMethodClientRequestedEvent,
            OnImeClientRequested,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private void OnImeClientRequested(object? sender, TextInputMethodClientRequestedEventArgs e)
    {
        if (!IsReadOnly)
        {
            e.Client = _imClient;
            e.Handled = true;
        }
    }

    protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _presenter = e.NameScope.Get<TextPresenter>("PART_TextPresenter");
        _imClient.SetPresenter(_presenter, this);
        if (IsFocused)
        {
            _presenter.ShowCaret();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Install the in-message IMM32 clause capture for this window (PLAN M2.6).
        // Reference counted, so several editors in one window share one WndProc hook.
        _monitoredTopLevel = TopLevel.GetTopLevel(this);
        Ime.ImeMessageMonitor.Attach(_monitoredTopLevel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        Ime.ImeMessageMonitor.Detach(_monitoredTopLevel);
        _monitoredTopLevel = null;
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (_presenter is not null)
        {
            _imClient.SetPresenter(_presenter, this);
        }
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        _imClient.SetPresenter(null, null);
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

            // Dialog mode: leave Enter unhandled so an IsDefault button still fires.
            if (!ConfirmOnEnter)
            {
                base.OnKeyDown(e);
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