using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;

namespace PigComic.App.Ime;

/// <summary>
/// A <see cref="TextPresenter"/> that renders the IME preedit in the **modern flavor**
/// (SPEC §21.2, D-43): composition text is coloured rather than underlined, and the active
/// henkan clause gets a coloured background — the look of Windows 11 Notepad and current
/// Excel, rather than the thin/thick underlines of memoQ, VSCode and WPF.
///
/// <para>The colours come from theme resources (see <c>PartTextEditorTheme.axaml</c>), which
/// is legitimate rather than a hack: a live display-attribute dump showed that the Windows 11
/// Microsoft Japanese IME registers <i>no colours at all</i> — only underline styles — so
/// Notepad's blue/aqua rendering is likewise the application's own palette keyed on the
/// attribute kind.</para>
///
/// <para>The in-composition caret is untouched: it is drawn by the base class from
/// <see cref="TextPresenter.PreeditTextCursorPosition"/>, which
/// <see cref="ImeTextBoxInputMethodClient"/> sets from the value Avalonia supplies. Nothing
/// here may interfere with it — that is what keeps the Chinese caret working.</para>
/// </summary>
public class ImeTextPresenter : TextPresenter
{
    private ImeComposition? _composition;
    private TextDecorationCollection? _errorDecoration;
    private IBrush? _errorDecorationBrush;

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

    /// <summary>Text colour for composition that is not the active clause
    /// (<see cref="ImeSegmentKind.Input"/> and <see cref="ImeSegmentKind.Converted"/>).</summary>
    public static readonly StyledProperty<IBrush?> CompositionForegroundProperty =
        AvaloniaProperty.Register<ImeTextPresenter, IBrush?>(nameof(CompositionForeground));

    /// <summary>Text colour inside the active henkan clause.</summary>
    public static readonly StyledProperty<IBrush?> TargetClauseForegroundProperty =
        AvaloniaProperty.Register<ImeTextPresenter, IBrush?>(nameof(TargetClauseForeground));

    /// <summary>Background behind the active henkan clause — the segment highlight.</summary>
    public static readonly StyledProperty<IBrush?> TargetClauseBackgroundProperty =
        AvaloniaProperty.Register<ImeTextPresenter, IBrush?>(nameof(TargetClauseBackground));

    /// <summary>Underline colour for IME-flagged erroneous input.</summary>
    public static readonly StyledProperty<IBrush?> InputErrorUnderlineProperty =
        AvaloniaProperty.Register<ImeTextPresenter, IBrush?>(nameof(InputErrorUnderline));

    public IBrush? CompositionForeground
    {
        get => GetValue(CompositionForegroundProperty);
        set => SetValue(CompositionForegroundProperty, value);
    }

    public IBrush? TargetClauseForeground
    {
        get => GetValue(TargetClauseForegroundProperty);
        set => SetValue(TargetClauseForegroundProperty, value);
    }

    public IBrush? TargetClauseBackground
    {
        get => GetValue(TargetClauseBackgroundProperty);
        set => SetValue(TargetClauseBackgroundProperty, value);
    }

    public IBrush? InputErrorUnderline
    {
        get => GetValue(InputErrorUnderlineProperty);
        set => SetValue(InputErrorUnderlineProperty, value);
    }

    protected override TextLayout CreateTextLayout()
    {
        var composition = _composition;
        var preeditText = PreeditText;

        if (composition is null || string.IsNullOrEmpty(preeditText))
        {
            return base.CreateTextLayout();
        }

        var segments = composition.Segments;
        if (segments.Count == 0)
        {
            return base.CreateTextLayout();
        }

        // The base class inserts the preedit at the caret; mirror that, defensively clamped
        // so a stale caret index can never throw mid-composition.
        var text = Text;
        var caretIndex = Math.Clamp(CaretIndex, 0, text?.Length ?? 0);
        var combined = GetCombinedText(text, caretIndex, preeditText);
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);

        // NOTE (Avalonia 12): GenericTextRunProperties reordered its parameters —
        // fontFeatures moved to the end, textDecorations is now the 3rd positional.
        var overrides = new List<ValueSpan<TextRunProperties>>(segments.Count);
        foreach (var segment in segments)
        {
            if (segment.Length <= 0)
            {
                continue;
            }

            var (foreground, background, decorations) = StyleFor(segment.Kind);
            overrides.Add(new ValueSpan<TextRunProperties>(
                caretIndex + segment.Start,
                segment.Length,
                new GenericTextRunProperties(
                    typeface,
                    FontSize,
                    decorations,
                    foregroundBrush: foreground,
                    backgroundBrush: background,
                    fontFeatures: FontFeatures)));
        }

        return CreateLayoutInternal(combined, typeface, overrides);
    }

    /// <summary>Maps a segment kind to its modern-flavor styling (SPEC §21.2 palette).</summary>
    private (IBrush? Foreground, IBrush? Background, TextDecorationCollection? Decorations) StyleFor(
        ImeSegmentKind kind) => kind switch
    {
        // The active clause: coloured background, contrasting text, no underline.
        ImeSegmentKind.ConvertedTarget or ImeSegmentKind.TargetNotConverted =>
            (TargetClauseForeground ?? Foreground, TargetClauseBackground, null),

        // Erroneous input keeps the composition colour but is marked with a broken underline.
        ImeSegmentKind.InputError =>
            (CompositionForeground ?? Foreground, null, ErrorDecoration()),

        // Input and Converted: coloured text, nothing else. This is also the degrade path for
        // IMEs that report no attributes at all (KO jamo, ZH pinyin before conversion).
        _ => (CompositionForeground ?? Foreground, null, null),
    };

    /// <summary>
    /// A dashed underline standing in for the squiggle the IME asks for; Avalonia's
    /// <see cref="TextDecoration"/> has no squiggle style. Cached per brush.
    /// </summary>
    private TextDecorationCollection? ErrorDecoration()
    {
        var brush = InputErrorUnderline;
        if (brush is null)
        {
            return TextDecorations.Underline;
        }

        if (_errorDecoration is not null && ReferenceEquals(_errorDecorationBrush, brush))
        {
            return _errorDecoration;
        }

        _errorDecorationBrush = brush;
        _errorDecoration =
        [
            new TextDecoration
            {
                Location = TextDecorationLocation.Underline,
                Stroke = brush,
                StrokeThickness = 1,
                StrokeDashArray = new AvaloniaList<double> { 2, 2 },
            },
        ];

        return _errorDecoration;
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

        return string.Concat(
            text.AsSpan(0, caretIndex),
            preeditText,
            text.AsSpan(caretIndex));
    }

    // Mirrors TextPresenter.CreateTextLayoutInternal (private there).
    private TextLayout CreateLayoutInternal(string? text, Typeface typeface,
        IReadOnlyList<ValueSpan<TextRunProperties>>? overrides)
    {
        var maxWidth = Bounds.Width <= 0 ? double.PositiveInfinity : Bounds.Width;

        // The height must NEVER constrain the layout. TextLayout silently DROPS whole lines
        // that do not fit within maxHeight, and passing Bounds.Height here is what broke the
        // multi-line editor: composing on the last line laid out 4 lines as 3, discarded 13
        // characters, stranded the caret at the end of the previous line, and pinned the
        // candidate window to the last surviving line. Single-line editors never showed it
        // because one line always fits, and zh-CN mostly escaped it because narrow ASCII
        // pinyin does not push the last line past the bottom the way wide kana/hangul do.
        // The base TextPresenter measures with (width, PositiveInfinity) for exactly this
        // reason — confirmed by reading its private _constraint field, which holds
        // "382, Infinity" for our multi-line editor while Bounds.Size is "382, 68".
        const double maxHeight = double.PositiveInfinity;

        // NOTE (Avalonia 12): TextLayout's ctor dropped fontFeatures from position 3;
        // it is now a trailing named parameter.
        return new TextLayout(text, typeface, FontSize, Foreground, TextAlignment,
            TextWrapping, maxWidth: maxWidth, maxHeight: maxHeight, textStyleOverrides: overrides,
            flowDirection: FlowDirection, lineHeight: LineHeight, letterSpacing: LetterSpacing,
            fontFeatures: FontFeatures);
    }
}
