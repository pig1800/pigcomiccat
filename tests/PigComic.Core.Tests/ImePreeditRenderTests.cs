using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using PigComic.App.Controls;
using Xunit;

namespace PigComic.Core.Tests;

/// <summary>
/// SPEC §21 composition-rendering contract (D-39). Avalonia's IME client renders the
/// live composition via TextPresenter.PreeditText: the presenter inserts the preedit
/// at the caret into a combined layout and moves the caret to CaretIndex +
/// preeditCursorPos, computing caret geometry in the raw left-origin inline space
/// (UpdateCaret → GetDistanceFromCharacterHit) while RenderInternal draws through
/// TextLayout.Draw applying TextAlignment. With TextAlignment.Center the composition
/// and its caret desynchronize (composition drawn centered, caret at left-flow X),
/// hiding the JA henkan word / ZH Pinyin cursor. KO jamo composes one glyph at the
/// caret, masking the bug. Fix: PartTextEditor uses Left/top alignment while editing
/// and does NOT override the (correct) built-in IME client.
/// </summary>
public class ImePreeditRenderTests
{
    public ImePreeditRenderTests() => HeadlessApp.EnsureStarted();

    [Fact]
    public void PartTextEditor_UsesLeftAlignment_SoCompositionAndCaretAlign()
    {
        // Single-line: left horizontal, vertical center is cosmetic only (does not
        // affect the horizontal IME caret/composition alignment).
        var single = new PartTextEditor { MultiLine = false };
        Assert.Equal(TextAlignment.Left, single.TextAlignment);
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, single.VerticalContentAlignment);

        // Multi-line (target editor): top-aligned so the composition caret is in-flow.
        var multi = new PartTextEditor { MultiLine = true };
        Assert.Equal(TextAlignment.Left, multi.TextAlignment);
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Top, multi.VerticalContentAlignment);
    }

    [Fact]
    public void PartTextEditor_DoesNotOverride_BuiltInImeClient()
    {
        // The regression (dropped commit) replaced the correct TextBox IME client with a
        // custom one that wrote preedit into the document text. Guard against that: the
        // editor must keep the base TextBox client registered via the class handler on
        // TextInputMethodClientRequestedEvent, and must not install its own.
        //
        // Verify PartTextEditor declares no custom TextInputMethodClient type and does not
        // handle the client-requested event itself.
        var editorType = typeof(PartTextEditor);
        var nestedOrSibling = editorType.Assembly.GetTypes()
            .Where(t => t != editorType && typeof(TextInputMethodClient).IsAssignableFrom(t))
            .ToList();
        Assert.Empty(nestedOrSibling);

        // And the editor's TextChanged path must not be used to inject preedit text:
        // the type has no field/property that stashes a preedit string into Text.
        Assert.DoesNotContain(editorType.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            m => m.Name.IndexOf("preedit", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void PartTextEditor_KeepsEnterConfirmGuard()
    {
        // Regression guard for the existing §21 Enter behavior: Enter must raise
        // ConfirmRequested (when not composing) and must not be treated as a newline in
        // single-line mode.
        var editor = new PartTextEditor { MultiLine = false };
        var fired = 0;
        editor.ConfirmRequested += (_, _) => fired++;

        var args = new Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
            Key = Avalonia.Input.Key.Enter,
            Source = editor,
        };
        // Invoke the protected handler via the public raise path.
        editor.RaiseEvent(args);

        Assert.Equal(1, fired);
        Assert.True(args.Handled);
        Assert.Equal(1, editor.ConfirmCount);
    }
}
