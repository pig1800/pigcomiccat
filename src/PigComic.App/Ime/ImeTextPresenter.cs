using Avalonia;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;

namespace PigComic.App.Ime;

/// <summary>
/// A <see cref="TextPresenter"/> that renders the IME preedit with conversion-clause
/// awareness (SPEC §21 escalation, D-40). The base presenter underlines the whole
/// preedit uniformly because Avalonia's Win32 IMM32 path forwards only the raw
/// composition string. This presenter additionally accepts an <see cref="ImeComposition"/>
/// so the active henkan segment (JA) is highlighted reverse-video and the rest is
/// underlined, matching Notepad. The in-composition caret is handled by the base
/// class via <see cref="TextPresenter.PreeditTextCursorPosition"/>, which the custom
/// IME client sets from GCS_CURSORPOS.
/// </summary>
public class ImeTextPresenter : TextPresenter
{
    private ImeComposition? _composition;

    /// <summary>
    /// The rich composition to render. Setting this also sets <see cref="TextPresenter.PreeditText"/>
    /// and <see cref="TextPresenter.PreeditTextCursorPosition"/> so the base caret logic keeps working.
    /// </summary>
    public ImeComposition? Composition
    {
        get => _composition;
        set
        {
            _composition = value;
            if (value is null)
            {
                PreeditText = null;
                PreeditTextCursorPosition = null;
            }
            else
            {
                PreeditText = value.Text;
                PreeditTextCursorPosition = value.CursorPosition;
            }

            InvalidateTextLayout();
        }
    }

    /// <summary>Brushes for the active (henkan) clause. Defaults follow the system selection colors.</summary>
    public static readonly StyledProperty<IBrush?> ActiveClauseBackgroundProperty =
        AvaloniaProperty.Register<ImeTextPresenter, IBrush?>(nameof(ActiveClauseBackground));

    public static readonly StyledProperty<IBrush?> ActiveClauseForegroundProperty =
        AvaloniaProperty.Register<ImeTextPresenter, IBrush?>(nameof(ActiveClauseForeground));

    public IBrush? ActiveClauseBackground
    {
        get => GetValue(ActiveClauseBackgroundProperty);
        set => SetValue(ActiveClauseBackgroundProperty, value);
    }

    public IBrush? ActiveClauseForeground
    {
        get => GetValue(ActiveClauseForegroundProperty);
        set => SetValue(ActiveClauseForegroundProperty, value);
    }

    protected override TextLayout CreateTextLayout()
    {
        var composition = _composition;
        var preeditText = PreeditText;

        // No rich composition, or no clause info → fall back to the base flat-underline
        // rendering (covers KO jamo, ZH pre-conversion, and any IME without clause data).
        var active = composition?.ActiveClause;
        if (composition is null || string.IsNullOrEmpty(preeditText) || active is null)
        {
            return base.CreateTextLayout();
        }

        var caretIndex = CaretIndex;
        var text = GetCombinedText(Text, caretIndex, preeditText);
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var foreground = Foreground;

        // Whole preedit gets an underline (it is uncommitted), exactly like the base class.
        var overrides = new List<ValueSpan<TextRunProperties>>
        {
            new(caretIndex, preeditText.Length,
                new GenericTextRunProperties(typeface, FontFeatures, FontSize,
                    foregroundBrush: foreground, textDecorations: TextDecorations.Underline)),
        };

        // The active henkan clause is drawn reverse-video on top of the underline.
        var (start, end) = active.Value;
        var length = Math.Max(0, end - start);
        if (length > 0)
        {
            overrides.Add(new ValueSpan<TextRunProperties>(
                caretIndex + start, length,
                new GenericTextRunProperties(typeface, FontFeatures, FontSize,
                    foregroundBrush: ActiveClauseForeground ?? Brushes.White,
                    backgroundBrush: ActiveClauseBackground ?? Brushes.Black,
                    textDecorations: TextDecorations.Underline)));
        }

        return CreateLayoutInternal(text, typeface, overrides);
    }

    // Mirrors TextPresenter.GetCombinedText (private there): insert the preedit at the caret.
    private static string? GetCombinedText(string? text, int caretIndex, string? preeditText)
    {
        if (string.IsNullOrEmpty(preeditText))
        {
            return text;
        }

        if (string.IsNullOrEmpty(text))
        {
            return preeditText;
        }

        var sb = new System.Text.StringBuilder(text.Length + preeditText.Length);
        sb.Append(text.Substring(0, caretIndex));
        sb.Insert(caretIndex, preeditText);
        sb.Append(text.Substring(caretIndex));
        return sb.ToString();
    }

    // Mirrors TextPresenter.CreateTextLayoutInternal (private there).
    private TextLayout CreateLayoutInternal(string? text, Typeface typeface,
        IReadOnlyList<ValueSpan<TextRunProperties>>? overrides)
    {
        var constraint = Bounds.Size;
        var maxWidth = constraint.Width <= 0 ? double.PositiveInfinity : constraint.Width;
        var maxHeight = constraint.Height <= 0 ? double.PositiveInfinity : constraint.Height;

        return new TextLayout(text, typeface, FontFeatures, FontSize, Foreground, TextAlignment,
            TextWrapping, maxWidth: maxWidth, maxHeight: maxHeight, textStyleOverrides: overrides,
            flowDirection: FlowDirection, lineHeight: LineHeight, letterSpacing: LetterSpacing);
    }
}
