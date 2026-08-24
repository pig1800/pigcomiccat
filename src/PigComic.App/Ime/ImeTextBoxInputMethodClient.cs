using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input.TextInput;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.VisualTree;

namespace PigComic.App.Ime;

/// <summary>
/// A <see cref="TextInputMethodClient"/> that enriches the Win32 IMM32 preedit with the
/// in-composition caret (GCS_CURSORPOS) and conversion-clause styling
/// (GCS_COMPCLAUSE/GCS_COMPATTR) — data Avalonia 11.x's IMM32 path reads but never
/// forwards, which is why the ZH Pinyin caret and JA henkan highlight do not render
/// (SPEC §21 escalation, D-40). Behavior otherwise mirrors the built-in TextBox client
/// (surrounding text, cursor rectangle, selection). The committed document text is never
/// touched by composition; only the presenter's preedit is set.
/// </summary>
public sealed class ImeTextBoxInputMethodClient : TextInputMethodClient
{
    private TextBox? _parent;
    private TextPresenter? _presenter;
    private bool _selectionChanged;
    private bool _isInChange;

    public override Visual TextViewVisual => _presenter!;

    public override bool SupportsPreedit => true;

    public override bool SupportsSurroundingText => true;

    public override string SurroundingText
    {
        get
        {
            if (_presenter is null || _parent is null)
            {
                return "";
            }

            if (_parent.CaretIndex != _presenter.CaretIndex)
            {
                _presenter.SetCurrentValue(TextPresenter.CaretIndexProperty, _parent.CaretIndex);
            }

            if (_parent.Text != _presenter.Text)
            {
                _presenter.SetCurrentValue(TextPresenter.TextProperty, _parent.Text);
            }

            var lineIndex = _presenter.TextLayout.GetLineIndexFromCharacterIndex(_presenter.CaretIndex, false);
            var textLine = _presenter.TextLayout.TextLines[lineIndex];
            return GetTextLineText(textLine);
        }
    }

    public override Rect CursorRectangle
    {
        get
        {
            if (_parent is null || _presenter is null)
            {
                return default;
            }

            var transform = _presenter.TransformToVisual(_parent);
            if (transform is null)
            {
                return default;
            }

            return GetCursorRect(_presenter).TransformToAABB(transform.Value);
        }
    }

    public override TextSelection Selection
    {
        get
        {
            if (_presenter is null || _parent is null)
            {
                return default;
            }

            var lineIndex = _presenter.TextLayout.GetLineIndexFromCharacterIndex(_parent.CaretIndex, false);
            var textLine = _presenter.TextLayout.TextLines[lineIndex];
            var lineStart = textLine.FirstTextSourceIndex;
            var selectionStart = Math.Max(0, _parent.SelectionStart - lineStart);
            var selectionEnd = Math.Max(0, _parent.SelectionEnd - lineStart);
            return new TextSelection(selectionStart, selectionEnd);
        }
        set
        {
            if (_parent is null || _presenter is null)
            {
                return;
            }

            var lineIndex = _presenter.TextLayout.GetLineIndexFromCharacterIndex(_parent.CaretIndex, false);
            var textLine = _presenter.TextLayout.TextLines[lineIndex];
            var lineStart = textLine.FirstTextSourceIndex;

            _parent.SelectionStart = lineStart + value.Start;
            _parent.SelectionEnd = lineStart + value.End;

            RaiseSelectionChanged();
        }
    }

    public void SetPresenter(TextPresenter? presenter, TextBox? parent)
    {
        if (_parent is not null)
        {
            _parent.PropertyChanged -= OnParentPropertyChanged;
        }

        _parent = parent;

        if (_parent is not null)
        {
            _parent.PropertyChanged += OnParentPropertyChanged;
        }

        if (_presenter is not null)
        {
            if (_presenter is ImeTextPresenter ime)
            {
                ime.Composition = null;
            }
            else
            {
                _presenter.ClearValue(TextPresenter.PreeditTextProperty);
            }
        }

        _presenter = presenter;

        RaiseTextViewVisualChanged();
        RaiseCursorRectangleChanged();
    }

    public override void SetPreeditText(string? preeditText) => SetPreeditText(preeditText, null);

    /// <summary>
    /// Avalonia 12.1.0+ supplies <paramref name="cursorPos"/> from GCS_CURSORPOS itself
    /// (upstream PR #21632), so it is taken as authoritative and never re-read from IMM32 —
    /// that parameter is what makes the Chinese in-composition caret work (D-41).
    ///
    /// <para>Conversion-clause styling comes from <see cref="ImeMessageMonitor"/>, which
    /// captured it synchronously inside the WM_IME_COMPOSITION message. This client performs
    /// no IMM32 calls of its own: reading here is off-contract and was why ATOK never showed
    /// a henkan highlight (D-43, PLAN M2.6).</para>
    /// </summary>
    public override void SetPreeditText(string? preeditText, int? cursorPos)
    {
        if (_presenter is null || _parent is null)
        {
            return;
        }

        if (_presenter is not ImeTextPresenter ime)
        {
            // Plain presenter: replicate the built-in behavior.
            _presenter.SetCurrentValue(TextPresenter.PreeditTextProperty, preeditText);
            _presenter.SetCurrentValue(TextPresenter.PreeditTextCursorPositionProperty, cursorPos);
            return;
        }

        if (string.IsNullOrEmpty(preeditText))
        {
            ime.Composition = null;
            return;
        }

        // Caret comes from Avalonia; default to end-of-preedit when it declines to say.
        var caret = cursorPos is >= 0 && cursorPos <= preeditText.Length
            ? cursorPos.Value
            : preeditText.Length;

        // Clause styling is best-effort. If the captured snapshot describes a different
        // string than the one Avalonia just handed us, the two have drifted apart and the
        // clause data would mis-position; drop it and render the whole preedit as Input.
        var snapshot = ImeMessageMonitor.TryGetSnapshot(TopLevel.GetTopLevel(_parent));
        var inSync = snapshot is not null && snapshot.Text == preeditText;

        ime.Composition = new ImeComposition(
            preeditText,
            caret,
            inSync ? snapshot!.ClauseBoundaries : null,
            inSync ? snapshot!.Attributes : null);
    }

    public IDisposable BeginChange()
    {
        if (_isInChange)
        {
            return EmptyDisposable.Instance;
        }

        _isInChange = true;
        return new CallbackDisposable(RaiseEvents);
    }

    private void RaiseEvents()
    {
        _isInChange = false;
        if (_selectionChanged)
        {
            RaiseSelectionChanged();
        }

        _selectionChanged = false;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }

    private sealed class CallbackDisposable : IDisposable
    {
        private readonly Action _onDispose;
        private bool _disposed;

        public CallbackDisposable(Action onDispose) => _onDispose = onDispose;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _onDispose();
            }
        }
    }

    private void OnParentPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty)
        {
            RaiseSurroundingTextChanged();
        }

        if (e.Property == TextBox.SelectionStartProperty || e.Property == TextBox.SelectionEndProperty)
        {
            if (_isInChange)
            {
                _selectionChanged = true;
            }
            else
            {
                RaiseSelectionChanged();
            }
        }
    }

    public override void ExecuteContextMenuAction(ContextMenuAction action)
    {
        base.ExecuteContextMenuAction(action);
        switch (action)
        {
            case ContextMenuAction.Copy:
                _parent?.Copy();
                break;
            case ContextMenuAction.Cut:
                _parent?.Cut();
                break;
            case ContextMenuAction.Paste:
                _parent?.Paste();
                break;
            case ContextMenuAction.SelectAll:
                _parent?.SelectAll();
                break;
        }
    }

    private static string GetTextLineText(TextLine textLine)
    {
        if (textLine.Length == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(textLine.Length);
        foreach (var run in textLine.TextRuns)
        {
            if (run.Length > 0)
            {
                builder.Append(run.Text.Span);
            }
        }

        return builder.ToString();
    }

    // TextPresenter.GetCursorRectangle() is internal to Avalonia.Controls; mirror it via
    // the caret's public hit-test so the candidate window tracks the caret.
    private static Rect GetCursorRect(TextPresenter presenter)
    {
        var caret = presenter.CaretIndex;
        var rect = presenter.TextLayout.HitTestTextPosition(caret);
        return rect;
    }
}
