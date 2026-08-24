using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using PigComic.App.Controls;
using PigComic.App.Ime;
using PigComic.App.Views;

namespace PigComic.App;

/// <summary>
/// `PigComic.App.exe --smoke` — a non-interactive startup self-check.
///
/// Its job is to catch the failure class that an Avalonia upgrade produces silently:
/// XAML that still compiles but no longer *applies* at runtime. In particular
/// <c>Ime/PartTextEditorTheme.axaml</c> is a clone of the Fluent TextBox template, so a
/// renamed Fluent resource key or a restructured template would leave PartTextEditor
/// without its <see cref="ImeTextPresenter"/> — the IME highlight would quietly stop
/// working with a green build and green tests.
///
/// Run this after any Avalonia upgrade or UI-theme change. Exit code 0 = all checks pass.
/// It is deliberately NOT an xunit test: instantiating Avalonia controls under the xunit
/// runner deadlocks (docs/IME_HANDOFF.md §8).
/// </summary>
internal static class SmokeTest
{
    public static int Run()
    {
        var failures = new List<string>();
        var passes = new List<string>();

        void Check(string name, Func<bool> predicate)
            => CheckDetail(name, () => predicate() ? null : "");

        // Same, but the predicate may explain *why* it failed (null = pass).
        void CheckDetail(string name, Func<string?> predicate)
        {
            try
            {
                var detail = predicate();
                if (detail is null)
                {
                    passes.Add(name);
                }
                else
                {
                    failures.Add(detail.Length == 0 ? name : $"{name} — {detail}");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{name} — threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        Program.BuildAvaloniaApp().SetupWithoutStarting();

        // 1. The custom control theme resolves from App.axaml's merged dictionaries.
        Check("PartTextEditorTheme resource resolves",
            () => Application.Current?.TryFindResource("PartTextEditorTheme", out _) == true);

        // 2. The theme applies and yields the clause-aware presenter (the real IME risk).
        var editor = new PartTextEditor { Text = "あい" };
        var window = new Window { Content = editor, Width = 400, Height = 200 };
        window.Show();
        window.Measure(new Size(400, 200));
        window.Arrange(new Rect(0, 0, 400, 200));
        editor.ApplyTemplate();

        Check("PartTextEditor template applied", () => editor.TemplatePresenter is not null);
        Check("PART_TextPresenter is ImeTextPresenter", () => editor.TemplatePresenter is ImeTextPresenter);

        // 3. The presenter renders a clause-highlighted composition without throwing.
        Check("Clause-aware preedit layout builds", () =>
        {
            if (editor.TemplatePresenter is not ImeTextPresenter ime)
            {
                return false;
            }

            ime.Composition = new ImeComposition(
                "にほんご", 4,
                new uint[] { 0, 2 },
                [
                    ImeComposition.AttrConverted, ImeComposition.AttrConverted,
                    ImeComposition.AttrTargetConverted, ImeComposition.AttrTargetConverted,
                ]);
            window.Measure(new Size(400, 200));
            window.Arrange(new Rect(0, 0, 400, 200));
            var built = ime.TextLayout is not null;
            ime.Composition = null;
            return built;
        });

        // 4. Modern-flavor palette actually reached the presenter (a renamed theme resource
        //    would silently drop the composition colours — SPEC §21.2).
        Check("Modern composition palette bound", () =>
            editor.TemplatePresenter is ImeTextPresenter p &&
            p.CompositionForeground is not null &&
            p.TargetClauseBackground is not null &&
            p.TargetClauseForeground is not null);

        // 5. A three-segment composition (converted + active clause + raw input) lays out.
        Check("Three-segment modern layout builds", () =>
        {
            if (editor.TemplatePresenter is not ImeTextPresenter ime)
            {
                return false;
            }

            var composition = new ImeComposition(
                "かんじへんかん", 7,
                new uint[] { 0, 2, 5, 7 },
                [
                    ImeSegmentBuilder.AttrConverted, ImeSegmentBuilder.AttrConverted,
                    ImeSegmentBuilder.AttrTargetConverted, ImeSegmentBuilder.AttrTargetConverted,
                    ImeSegmentBuilder.AttrTargetConverted,
                    ImeSegmentBuilder.AttrInput, ImeSegmentBuilder.AttrInput,
                ]);

            if (composition.Segments.Count != 3)
            {
                return false;
            }

            ime.Composition = composition;
            window.Measure(new Size(400, 200));
            window.Arrange(new Rect(0, 0, 400, 200));
            var built = ime.TextLayout is not null;
            ime.Composition = null;
            return built;
        });

        // 6. The no-clause degrade path — this is the ZH pinyin / KO jamo case, and the one
        //    that must never regress the in-composition caret.
        Check("No-clause composition still lays out with caret", () =>
        {
            if (editor.TemplatePresenter is not ImeTextPresenter ime)
            {
                return false;
            }

            ime.Composition = new ImeComposition("nihao", 3, null, null);
            window.Measure(new Size(400, 200));
            window.Arrange(new Rect(0, 0, 400, 200));
            var ok = ime.TextLayout is not null && ime.PreeditTextCursorPosition == 3;
            ime.Composition = null;
            return ok;
        });

        // 6b. Multi-line composition must not lose lines. Passing the control's height as the
        //     layout's maxHeight makes TextLayout silently drop lines that do not fit: the
        //     last line of the multi-line editor rendered blank, its text vanished while
        //     typing, and the caret and candidate window were stranded on the line above.
        //     Single-line editors cannot show this, so it needs its own check.
        CheckDetail("Multi-line composition keeps every line", () =>
        {
            var multi = new PartTextEditor
            {
                MultiLine = true,
                Text = "line one\nline two\nline three\nline four",
            };
            var host = new Window { Content = multi, Width = 400, Height = 140 };
            host.Show();
            host.Measure(new Size(400, 140));
            host.Arrange(new Rect(0, 0, 400, 140));
            multi.ApplyTemplate();
            host.Measure(new Size(400, 140));
            host.Arrange(new Rect(0, 0, 400, 140));

            try
            {
                if (multi.TemplatePresenter is not ImeTextPresenter ime)
                {
                    return "multi-line editor did not get an ImeTextPresenter";
                }

                var before = ime.TextLayout.TextLines.Count;

                // Compose at the very end of the last line — the reported failure case.
                multi.CaretIndex = multi.Text!.Length;
                ime.Composition = new ImeComposition("にほんご", 4, [0, 2, 4], [1, 1, 2, 2]);
                host.Measure(new Size(400, 140));
                host.Arrange(new Rect(0, 0, 400, 140));

                var layout = ime.TextLayout;
                var expected = multi.Text!.Length + 4;
                var laidOut = layout.TextLines.Sum(l => l.Length);

                if (laidOut < expected || layout.TextLines.Count < before)
                {
                    return $"composition truncated: {before} lines -> {layout.TextLines.Count}, " +
                           $"{expected} chars -> {laidOut} laid out";
                }

                // And the caret must land on the last line, not the one above it.
                var caretY = layout.HitTestTextPosition(multi.CaretIndex + 4).Y;
                var lastLineTop = layout.TextLines.Take(layout.TextLines.Count - 1).Sum(l => l.Height);
                return caretY >= lastLineTop - 0.5
                    ? null
                    : $"caret stranded above the last line (y={caretY:F1}, last line top={lastLineTop:F1})";
            }
            finally
            {
                ime_ClearComposition(multi);
                host.Close();
            }
        });

        // 7. PartTextEditor wires the composition hook up by itself when it enters the visual
        //    tree — without this, clause capture silently never runs (PLAN M2.6).
        Check("PartTextEditor auto-attached the IME message monitor", () =>
            !OperatingSystem.IsWindows() || ImeMessageMonitor.IsAttached(window));

        // 8. The hook is reference counted, so several editors in one window share it and the
        //    last one out removes it. Uses a fresh window so the transitions are observable.
        Check("IME message monitor attach/detach is balanced", () =>
        {
            var probe = new Window();
            if (ImeMessageMonitor.IsAttached(probe))
            {
                return false;
            }

            ImeMessageMonitor.Attach(probe);
            ImeMessageMonitor.Attach(probe); // a second editor in the same window
            var afterAttach = ImeMessageMonitor.IsAttached(probe);

            ImeMessageMonitor.Detach(probe);
            var afterFirstDetach = ImeMessageMonitor.IsAttached(probe);

            ImeMessageMonitor.Detach(probe);
            var afterLastDetach = ImeMessageMonitor.IsAttached(probe);

            if (!OperatingSystem.IsWindows())
            {
                // Off-Windows the whole thing is a documented no-op.
                return !afterAttach && !afterFirstDetach && !afterLastDetach;
            }

            return afterAttach && afterFirstDetach && !afterLastDetach;
        });

        // 9. Every window's XAML loads (catches compiled-binding / template breakage).
        Check("MainWindow constructs", () => new MainWindow() is not null);
        Check("CreateProjectDialog constructs", () => new CreateProjectDialog() is not null);
        Check("RemoveProjectDialog constructs", () => new RemoveProjectDialog("x", "y") is not null);
        Check("RelinkDialog constructs", () => new RelinkDialog(["a.pcml"]) is not null);
        Check("ImeTestWindow constructs", () => new ImeTestWindow() is not null);
        Check("SpikeWindow constructs", () => new SpikeWindow() is not null);

        // 10. Enforce the SPEC §21.2 control rule: every editable field is a PartTextEditor,
        //     because a bare TextBox silently loses the whole IME stack (clause capture,
        //     modern rendering, Enter guard). Documented rules do not survive contact with
        //     a hurried implementer; this one is checked.
        CheckDetail("All editable fields use PartTextEditor", () =>
        {
            // Deliberate exceptions: the IME gate's side-by-side comparison box, and the
            // debug spike's file-path field. Anything else must be justified and added here.
            string[] approved = ["CenteredReference", "PathBox"];

            var windows = new Window[]
            {
                new MainWindow(), new CreateProjectDialog(), new RemoveProjectDialog("x", "y"),
                new RelinkDialog(["a.pcml"]), new ImeTestWindow(), new SpikeWindow(),
            };

            foreach (var w in windows)
            {
                foreach (var control in LogicalDescendants(w))
                {
                    if (control is TextBox and not PartTextEditor &&
                        !approved.Contains(control.Name ?? ""))
                    {
                        return $"{w.GetType().Name} has a plain TextBox " +
                               $"'{control.Name ?? "(unnamed)"}': use PartTextEditor (SPEC 21.2)";
                    }
                }
            }

            return null;
        });

        window.Close();

        foreach (var p in passes)
        {
            Console.WriteLine($"  PASS  {p}");
        }

        foreach (var f in failures)
        {
            Console.WriteLine($"  FAIL  {f}");
        }

        Console.WriteLine($"\nsmoke: {passes.Count} passed, {failures.Count} failed");
        return failures.Count == 0 ? 0 : 1;
    }

    private static void ime_ClearComposition(PartTextEditor editor)
    {
        if (editor.TemplatePresenter is ImeTextPresenter ime)
        {
            ime.Composition = null;
        }
    }

    /// <summary>
    /// Walks a window's logical tree. Templates have not been applied to the freshly
    /// constructed windows, so this sees only XAML-declared controls — template internals
    /// (a ComboBox's inner TextBox, say) cannot produce false positives.
    /// </summary>
    private static IEnumerable<Control> LogicalDescendants(ILogical root)
    {
        foreach (var child in root.LogicalChildren)
        {
            if (child is Control control)
            {
                yield return control;
            }

            foreach (var descendant in LogicalDescendants(child))
            {
                yield return descendant;
            }
        }
    }
}
