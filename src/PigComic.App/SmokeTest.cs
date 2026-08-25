using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using PigComic.App.Controls;
using PigComic.App.Ime;
using PigComic.App.Rendering;
using PigComic.App.Services;
using PigComic.App.Views;
using PigComic.Core.Domain;

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

        // 6c. D-52 target-editor mode: plain Enter is a newline (never a confirm),
        //     Ctrl+Enter is the confirm gesture, Ctrl+Shift+Enter confirms Reviewed.
        CheckDetail("PartTextEditor D-52 mode routes Enter/Ctrl+Enter", () =>
        {
            var target = new PartTextEditor { MultiLine = true, EnterInsertsNewline = true };
            var host = new Window { Content = target, Width = 400, Height = 200 };
            host.Show();
            target.ApplyTemplate();

            var confirms = 0;
            var reviewed = 0;
            target.ConfirmRequested += (_, _) => confirms++;
            target.VariantConfirmRequested += (_, v) =>
            {
                if (v == ConfirmVariant.CtrlShiftEnter)
                {
                    reviewed++;
                }
            };

            void Press(Key key, KeyModifiers mods) => target.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = key,
                KeyModifiers = mods,
            });

            Press(Key.Enter, KeyModifiers.None);
            Press(Key.Enter, KeyModifiers.Shift);
            var afterPlain = confirms;
            Press(Key.Enter, KeyModifiers.Control);
            Press(Key.Enter, KeyModifiers.Control | KeyModifiers.Shift);
            var afterConfirm = confirms;

            host.Close();
            return afterPlain == 0 && afterConfirm == 1 && reviewed == 1
                ? null
                : $"D-52 routing wrong: plain Enter confirmed {afterPlain}x, " +
                  $"Ctrl+Enter {afterConfirm}x, Reviewed {reviewed}x (want 0/1/1)";
        });

        // 6d. Legacy mode (F2 source editor, dialogs) must keep Enter = confirm so the
        //     inline source edit still commits on Enter.
        CheckDetail("PartTextEditor legacy mode still confirms on Enter", () =>
        {
            var legacy = new PartTextEditor();
            var host = new Window { Content = legacy, Width = 400, Height = 200 };
            host.Show();
            legacy.ApplyTemplate();

            var confirms = 0;
            var variants = 0;
            legacy.ConfirmRequested += (_, _) => confirms++;
            legacy.VariantConfirmRequested += (_, v) =>
            {
                if (v == ConfirmVariant.CtrlEnter)
                {
                    variants++;
                }
            };

            void Press(Key key, KeyModifiers mods) => legacy.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = key,
                KeyModifiers = mods,
            });

            Press(Key.Enter, KeyModifiers.None);
            Press(Key.Enter, KeyModifiers.Control);

            host.Close();
            return confirms == 1 && variants == 1
                ? null
                : $"legacy routing wrong: Enter {confirms}x, Ctrl+Enter {variants}x (want 1/1)";
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
        Check("EditorView constructs (missing chapter degrades to an error banner)",
            () =>
            {
                var editor = new EditorView(Path.Combine(Path.GetTempPath(), "pigcomic-smoke-missing.pcml"));
                var vmOk = editor.DataContext is ViewModels.EditorViewModel;
                var banner = editor.FindControl<TextBlock>("LoadError") is not null;
                return vmOk && banner;
            });

        // 9b. The editor's real decode path: an actual chapter with strip media opens, its
        //     tiles arrive through DecodeQueue → OnTileReady → Install, and the window
        //     closes cleanly. This caught the M5.1 crash class: Install disposed the
        //     replaced bitmap and then re-read its PixelSize — an ObjectDisposedException
        //     that only fires when a tile key is installed twice (a normal editor flow).
        CheckDetail("EditorView renders a real chapter and closes cleanly", () =>
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pigcomic-smoke-editor", Guid.NewGuid().ToString("N"));
            try
            {
                StripImageGenerator.Generate(tmp, 640, 1280);
                var pcml = ExampleChapterBuilder.Build(
                    tmp, 640, 1280,
                    Path.Combine(tmp, "strip.jpg"), Path.Combine(tmp, "strip.png"));

                var editor = new EditorView(pcml, tmp);
                editor.Show();

                var vm = editor.DataContext as ViewModels.EditorViewModel;
                var deadline = Environment.TickCount64 + 8000;
                while (Environment.TickCount64 < deadline)
                {
                    Thread.Sleep(20);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    if (vm?.Segments?.Items.Count > 0)
                    {
                        break;
                    }
                }

                var rendered = vm?.Segments?.Items.Count > 0;

                // D-49: selecting a bubble that lives on the SECOND strip image must scroll
                // the one continuous strip to it — there is no page to switch to. b0003's
                // marker sits below the first image, so this exercises the strip mapping.
                if (vm?.Segments is { } segs)
                {
                    segs.SelectBubbleId("b0003");
                    Dispatcher.UIThread.RunJobs();
                }

                var selectionLabel = vm?.SelectionLabel ?? "";

                // M5.4: draft-on-typing + confirm loop through the real service.
                var confirmOk = false;
                var focusTargetOk = false;
                ViewModels.BubbleRowViewModel? confirmed = null;
                var segmentList = editor.FindControl<Views.SegmentListView>("SegmentList");
                if (vm?.Segments?.SelectedBubble is { } selected && segmentList?.Confirm is { } confirm)
                {
                    selected.Parts[0].Text = "ドドド";
                    Dispatcher.UIThread.RunJobs();
                    var becameDraft = selected.Status == BubbleStatus.Draft;
                    confirm.ConfirmAndMove(review: false, skipConfirmed: false);
                    Dispatcher.UIThread.RunJobs();
                    Dispatcher.UIThread.RunJobs();
                    var becameTranslated = selected.Status == BubbleStatus.Translated;
                    confirmOk = becameDraft && becameTranslated;
                    confirmed = selected;

                    // Focus-after-confirm regression guard: the next row's TARGET editor
                    // (not the collapsed inline SourceEditor) must be reachable. Before the
                    // fix, FirstOrDefault landed on the hidden SourceEditor and the caret
                    // stayed in the old bubble.
                    var target = segmentList?.FindFirstTargetEditorOfSelected();
                    focusTargetOk = target is not null && target.Name != "SourceEditor";
                }

                var segments = editor.FindControl<Views.SegmentListView>("SegmentList");
                var segmentCount = -1;
                var rowCount = -1;
                if (segments?.DataContext is ViewModels.SegmentListViewModel sl)
                {
                    segmentCount = sl.Items.Count;
                    rowCount = sl.Items.OfType<ViewModels.BubbleRowViewModel>().Count();
                }

                // D-52: the skip-confirmed option rides the status-bar checkbox into the
                // confirm service. Assert the plumbing (VM ↔ service agree), not a specific
                // value — the smoke reads the real registry.json, which interactive use may
                // have toggled off.
                var skipPlumbingOk = segments?.Confirm?.SkipConfirmed == vm?.SkipConfirmed;

                // M5.5: the function pane hosts the match-list VM once data flows.
                var functionPane = editor.FindControl<Views.FunctionPaneView>("FunctionPane");
                var matchesWired = functionPane?.DataContext is ViewModels.MatchListViewModel;

                // M5.4 TM write: a confirm must land a row in tm.db, and re-selecting the
                // confirmed bubble must surface a 100% match in the results box. This was the
                // gap that hid the post-overhaul "TM isn't working" regression — the stale
                // strips/tm.db had a ja→zh-Hant pair, the reformed chapter is zh-CN→ja, and
                // OpenStores swallowed the mismatch so the box just showed "No matches".
                var tmRowOk = false;
                var tmDiag = "";
                if (matchesWired && functionPane?.DataContext is ViewModels.MatchListViewModel mvm &&
                    vm?.Segments is { } segs2 && confirmed is { } confirmedRow)
                {
                    segs2.SelectBubbleId(confirmedRow.Id);
                    // The query is async-void with a 150 ms debounce and ConfigureAwait(true),
                    // so its continuations need the UI dispatcher. Pump in a loop rather than
                    // a single Thread.Sleep + RunJobs (which blocks the dispatcher mid-delay).
                    var qDeadline = Environment.TickCount64 + 600;
                    while (Environment.TickCount64 < qDeadline)
                    {
                        Thread.Sleep(30);
                        Dispatcher.UIThread.RunJobs();
                        if (mvm.Rows.Count > 0 || mvm.StatusText.Length > 0)
                        {
                            break;
                        }
                    }
                    Dispatcher.UIThread.RunJobs();
                    // A confirm must land a TM row that re-surfaces as an exact (or
                    // context-boosted exact, 101–103% per D-26) match on re-select.
                    tmRowOk = mvm.Rows.Count > 0 && mvm.Rows.Any(r => r.IsTm && r.ScoreText.Length > 0 &&
                              int.TryParse(r.ScoreText.TrimEnd('%'), out var s) && s >= 100);
                    if (!tmRowOk)
                    {
                        var rowDump = string.Join(" | ", mvm.Rows.Select(r => $"tm={r.IsTm} score='{r.ScoreText}'"));
                        tmDiag = $" status='{mvm.StatusText}' rows={mvm.Rows.Count} [{rowDump}]";
                    }
                }

                editor.Close();
                if (!rendered)
                {
                    return "editor did not finish loading the chapter strip";
                }

                if (!selectionLabel.Contains("b0003"))
                {
                    return $"selection did not reach the status bar ('{selectionLabel}')";
                }

                if (!confirmOk)
                {
                    return "confirm loop failed: typed text did not demote to Draft / confirm did not reach Translated";
                }

                if (!focusTargetOk)
                {
                    return "focus-after-confirm failed: the next row's target editor was not reachable (collapsed SourceEditor picked instead?)";
                }

                if (skipPlumbingOk != true)
                {
                    return "skip-confirmed option did not propagate from the VM to the confirm service";
                }

                if (!matchesWired)
                {
                    return "function pane did not receive the match-list view model";
                }

                if (!tmRowOk)
                {
                    return "TM write/read failed: a confirm did not surface a 100% match on re-select " +
                           "(store open likely swallowed a language-pair mismatch — see OpenStores)" + tmDiag;
                }

                // Three bubbles, no page headers (D-49): the list is flat.
                return segmentCount == 3 && rowCount == 3
                    ? null
                    : $"segment list wrong: {segmentCount} items, {rowCount} rows (want 3/3)";
            }
            finally
            {
                try { Directory.Delete(tmp, recursive: true); } catch { /* best effort */ }
            }
        });

        // 9c. D-52 part-walk: a confirm gesture on a NON-last part advances focus to the
        //     next part editor within the same bubble WITHOUT committing; only confirming
        //     the last part commits the bubble and advances. The example chapter's b0002
        //     is a 2-part Draft bubble, so this is observable without the M6.4 split UI.
        CheckDetail("Ctrl+Enter walks parts and commits only on the last", () =>
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pigcomic-smoke-walk", Guid.NewGuid().ToString("N"));
            try
            {
                StripImageGenerator.Generate(tmp, 640, 1280);
                var pcml = ExampleChapterBuilder.Build(
                    tmp, 640, 1280,
                    Path.Combine(tmp, "strip.jpg"), Path.Combine(tmp, "strip.png"));

                var editor = new EditorView(pcml, tmp);
                editor.Show();
                var vm = editor.DataContext as ViewModels.EditorViewModel;
                var deadline = Environment.TickCount64 + 8000;
                while (Environment.TickCount64 < deadline)
                {
                    Thread.Sleep(20);
                    Dispatcher.UIThread.RunJobs();
                    if (vm?.Segments?.Items.Count > 0)
                    {
                        break;
                    }
                }

                var sl = editor.FindControl<Views.SegmentListView>("SegmentList");
                var row = vm?.Segments?.Items.OfType<ViewModels.BubbleRowViewModel>()
                    .FirstOrDefault(r => r.Id == "b0002");
                if (row is null || sl is null)
                {
                    editor.Close();
                    return "b0002 (2-part) not found";
                }

                if (row.Parts.Count != 2)
                {
                    editor.Close();
                    return $"b0002 expected 2 parts, got {row.Parts.Count}";
                }

                vm!.Segments!.SelectBubbleId("b0002");
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();

                var part1 = sl.FindPartEditorOfRow(row, 1);
                var part2 = sl.FindPartEditorOfRow(row, 2);
                if (part1 is null || part2 is null)
                {
                    editor.Close();
                    return $"part editors not realized (p1={part1 is not null}, p2={part2 is not null})";
                }

                var statusBefore = row.Status;

                // Ctrl+Enter on part 1 (non-last) → advance to part 2, NO commit.
                Press(part1, Key.Enter, KeyModifiers.Control);
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();
                var statusAfterPart1 = row.Status;
                var focusOnPart2 = sl.LastFocusedEditor == part2;

                // Ctrl+Enter on part 2 (last) → commit + advance to the next bubble.
                Press(part2, Key.Enter, KeyModifiers.Control);
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();
                var statusAfterPart2 = row.Status;
                var advanced = vm.Segments.SelectedBubble?.Id == "b0003";

                editor.Close();
                return statusAfterPart1 == statusBefore && focusOnPart2 &&
                       statusAfterPart2 == BubbleStatus.Translated && advanced
                    ? null
                    : $"part-walk wrong: part1 status {statusBefore}->{statusAfterPart1} (want unchanged), " +
                      $"focus on part2={focusOnPart2}, part2 status {statusAfterPart2} (want Translated), " +
                      $"advanced={advanced} (want b0003)";

                static void Press(Control target, Key key, KeyModifiers mods) => target.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = key,
                    KeyModifiers = mods,
                });
            }
            finally
            {
                try { Directory.Delete(tmp, recursive: true); } catch { /* best effort */ }
            }
        });

        // 9d. M6 marker interactions (SPEC §15): drag a source marker (commit on release,
        //     clamps to the strip, marks dirty), Ctrl+B placement creates a bubble with the
        //     id/order rules, Delete removes it, Alt+1/2/3 splits/merges parts and Tab walks
        //     the part editors. Driven through the internal interaction seam with STRIP
        //     coordinates (the real pointer handlers convert screen→strip the same way).
        CheckDetail("M6 marker drag/place/delete/split", () =>
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pigcomic-smoke-m6", Guid.NewGuid().ToString("N"));
            try
            {
                StripImageGenerator.Generate(tmp, 640, 1280);
                var pcml = ExampleChapterBuilder.Build(
                    tmp, 640, 1280,
                    Path.Combine(tmp, "strip.jpg"), Path.Combine(tmp, "strip.png"));

                var editor = new EditorView(pcml, tmp);
                editor.Show();
                var vm = editor.DataContext as ViewModels.EditorViewModel;
                var deadline = Environment.TickCount64 + 8000;
                while (Environment.TickCount64 < deadline)
                {
                    Thread.Sleep(20);
                    Dispatcher.UIThread.RunJobs();
                    if (vm?.Segments?.Items.Count > 0)
                    {
                        break;
                    }
                }

                var pane = editor.FindControl<TiledImageControl>("ImagePane");
                var row1 = vm?.Segments?.Items.OfType<ViewModels.BubbleRowViewModel>()
                    .FirstOrDefault(r => r.Id == "b0001");
                if (pane is null || row1 is null)
                {
                    editor.Close();
                    return "editor/image pane not ready for M6";
                }

                // --- drag the source marker: (612,480) → (582,520), clamp to 640-wide strip.
                vm!.Segments!.SelectBubbleId("b0001");
                Dispatcher.UIThread.RunJobs();
                var orig = row1.Bubble.Marker;
                pane.InteractionPointerPressed(new Point(orig.X, orig.Y), shiftHeld: false);
                Dispatcher.UIThread.RunJobs();
                pane.InteractionPointerMoved(new Point(orig.X - 30, orig.Y + 40));
                Dispatcher.UIThread.RunJobs();
                pane.InteractionPointerReleased(new Point(orig.X - 30, orig.Y + 40));
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();
                var moved = row1.Bubble.Marker == new PigComic.Core.Domain.PixelPoint(orig.X - 30, orig.Y + 40);
                // D-18: part 1 mirrors the source — dragging the source cross must not
                // leave a stale part-1 cross at the old spot.
                var part1Synced = row1.Bubble.Parts[0].Marker == row1.Bubble.Marker;
                var dirty = vm.SaveStateLabel == "unsaved";

                // --- placement: Ctrl+B arm → click → new u-prefixed bubble at order 1 (Y=100).
                pane.PlacementArmed = true;
                pane.InteractionPointerPressed(new Point(100, 100), shiftHeld: false);
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();
                var items = vm.Segments!.Items.OfType<ViewModels.BubbleRowViewModel>().ToList();
                var created = items.FirstOrDefault(r => r.Id.StartsWith("u", StringComparison.Ordinal));
                var placed = items.Count == 4 && created is not null &&
                             created.Status == BubbleStatus.Untranslated &&
                             items[0].Id == created.Id &&
                             !pane.PlacementArmed;

                // --- delete the created bubble (dialog-free smoke path).
                if (created is not null)
                {
                    editor.ApplyDeleteSelected(created);
                    Dispatcher.UIThread.RunJobs();
                }

                var deleted = vm.Segments.Items.Count == 3 &&
                              vm.Segments.Items.OfType<ViewModels.BubbleRowViewModel>()
                                  .All(r => !r.Id.StartsWith("u", StringComparison.Ordinal));

                // --- split b0002 to 3 parts, Tab from part 1 → part 2, then merge back.
                vm.Segments.SelectBubbleId("b0002");
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();
                var row2 = vm.Segments.Items.OfType<ViewModels.BubbleRowViewModel>()
                    .First(r => r.Id == "b0002");
                editor.ApplyPartCount(3);
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();
                var split = row2.Parts.Count == 3;
                var sl = editor.FindControl<Views.SegmentListView>("SegmentList");
                var part1 = sl?.FindPartEditorOfRow(row2, 1);
                var part2 = sl?.FindPartEditorOfRow(row2, 2);
                if (part1 is not null && part2 is not null)
                {
                    Press(part1, Key.Tab, KeyModifiers.None);
                    Dispatcher.UIThread.RunJobs();
                }

                var tabWalked = sl?.LastFocusedEditor == part2;
                editor.ApplyPartCount(1);
                Dispatcher.UIThread.RunJobs();
                var merged = row2.Parts.Count == 1;

                editor.Close();
                return moved && part1Synced && dirty && placed && deleted && split && tabWalked && merged
                    ? null
                    : $"M6 failed: moved={moved} part1Synced={part1Synced} dirty={dirty} placed={placed} deleted={deleted} " +
                      $"split={split} tabWalked={tabWalked} merged={merged}";

                static void Press(Control target, Key key, KeyModifiers mods) => target.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = key,
                    KeyModifiers = mods,
                });
            }
            finally
            {
                try { Directory.Delete(tmp, recursive: true); } catch { /* best effort */ }
            }
        });

        // 9f. Drag-to-reorder (Q8 resolved, D-57): dragging a bubble's MAIN cross past a
        //     neighbour's Y renumbers the reading order and rebuilds the list. b0001(480)
        //     dragged below b0002(900) → list becomes [b0002, b0001, b0003]; selection
        //     stays on the dragged bubble. Part-marker drags do NOT reorder.
        CheckDetail("Main-cross drag reorders by Y (part drags don't)", () =>
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pigcomic-smoke-reorder", Guid.NewGuid().ToString("N"));
            try
            {
                StripImageGenerator.Generate(tmp, 640, 1280);
                var pcml = ExampleChapterBuilder.Build(
                    tmp, 640, 1280,
                    Path.Combine(tmp, "strip.jpg"), Path.Combine(tmp, "strip.png"));

                var editor = new EditorView(pcml, tmp);
                editor.Show();
                var vm = editor.DataContext as ViewModels.EditorViewModel;
                var deadline = Environment.TickCount64 + 8000;
                while (Environment.TickCount64 < deadline)
                {
                    Thread.Sleep(20);
                    Dispatcher.UIThread.RunJobs();
                    if (vm?.Segments?.Items.Count > 0)
                    {
                        break;
                    }
                }

                var pane = editor.FindControl<TiledImageControl>("ImagePane");
                var b1 = vm?.Segments?.Items.OfType<ViewModels.BubbleRowViewModel>()
                    .FirstOrDefault(r => r.Id == "b0001");
                if (pane is null || b1 is null || vm?.Segments is null)
                {
                    editor.Close();
                    return "editor/b0001 not ready";
                }

                // b0001 marker Y=480. Drag its MAIN cross to Y=950 (below b0002's 900).
                vm!.Segments!.SelectBubbleId("b0001");
                Dispatcher.UIThread.RunJobs();
                var orig = b1.Bubble.Marker;
                pane.InteractionPointerPressed(new Point(orig.X, orig.Y), shiftHeld: false);
                Dispatcher.UIThread.RunJobs();
                pane.InteractionPointerMoved(new Point(orig.X, 950));
                Dispatcher.UIThread.RunJobs();
                pane.InteractionPointerReleased(new Point(orig.X, 950));
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();

                var order = vm.Segments.Items.OfType<ViewModels.BubbleRowViewModel>()
                    .Select(r => r.Id).ToList();
                var mainReordered = order.SequenceEqual(["b0002", "b0001", "b0003"]);
                var selectionKept = vm.Segments.SelectedBubble?.Id == "b0001";

                // Part-marker drag must NOT reorder: split b0002 to 2, drag part 2's marker
                // far below b0003 — the list order stays [b0002, b0001, b0003].
                vm.Segments.SelectBubbleId("b0002");
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();
                var b2 = vm.Segments.Items.OfType<ViewModels.BubbleRowViewModel>()
                    .First(r => r.Id == "b0002");
                editor.ApplyPartCount(2);
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();
                var p2Strip = b2.Bubble.Parts[1].Marker;
                pane.InteractionPointerPressed(new Point(p2Strip.X, p2Strip.Y), shiftHeld: false);
                Dispatcher.UIThread.RunJobs();
                pane.InteractionPointerMoved(new Point(p2Strip.X, 2000));
                Dispatcher.UIThread.RunJobs();
                pane.InteractionPointerReleased(new Point(p2Strip.X, 2000));
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();
                var partDragOrder = vm.Segments.Items.OfType<ViewModels.BubbleRowViewModel>()
                    .Select(r => r.Id).ToList();
                var partDragNoReorder = partDragOrder.SequenceEqual(["b0002", "b0001", "b0003"]);

                editor.Close();
                return mainReordered && selectionKept && partDragNoReorder
                    ? null
                    : $"reorder: mainReordered={mainReordered} selectionKept={selectionKept} " +
                      $"partDragNoReorder={partDragNoReorder} (order=[{string.Join(',', order)}])";
            }
            finally
            {
                try { Directory.Delete(tmp, recursive: true); } catch { /* best effort */ }
            }
        });

        // 9e. Placement via a REAL pointer press (not the internal seam): confirms the
        //     control receives the press on gray/undecoded areas (full-bounds backdrop +
        //     IsHitTestVisible) and that off-strip clicks are rejected. This was the owner
        //     report "Ctrl+B then click only works on drawn content, not gray background."
        CheckDetail("Placement works on gray / rejects off-strip (real pointer)", () =>
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pigcomic-smoke-place", Guid.NewGuid().ToString("N"));
            try
            {
                StripImageGenerator.Generate(tmp, 640, 1280);
                var pcml = ExampleChapterBuilder.Build(
                    tmp, 640, 1280,
                    Path.Combine(tmp, "strip.jpg"), Path.Combine(tmp, "strip.png"));

                var editor = new EditorView(pcml, tmp);
                editor.Show();
                var vm = editor.DataContext as ViewModels.EditorViewModel;
                var deadline = Environment.TickCount64 + 8000;
                while (Environment.TickCount64 < deadline)
                {
                    Thread.Sleep(20);
                    Dispatcher.UIThread.RunJobs();
                    if (vm?.Segments?.Items.Count > 0)
                    {
                        break;
                    }
                }

                var pane = editor.FindControl<TiledImageControl>("ImagePane");
                if (pane is null || vm?.Segments is null)
                {
                    editor.Close();
                    return "editor/pane not ready";
                }

                var before = vm.Segments.Items.Count;

                // Arm + click on a gray/undecoded IN-STRIP area (screen 50,50 → strip ~76,76
                // at fit-width). A real PointerPressedEvent must reach the control and create
                // a bubble — the full-bounds backdrop makes the whole bounds hit-testable.
                pane.PlacementArmed = true;
                PressPointer(pane, new Point(50, 50));
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();
                var inStripCreated = vm.Segments.Items.Count == before + 1 &&
                                     vm.Segments.Items.OfType<ViewModels.BubbleRowViewModel>()
                                         .Any(r => r.Id.StartsWith("u", StringComparison.Ordinal)) &&
                                     !pane.PlacementArmed;

                // Arm + click OFF-STRIP (far right, x past the 640px strip). Rejected: no
                // bubble, stays armed (SPEC §15.2 "click on the strip").
                pane.PlacementArmed = true;
                PressPointer(pane, new Point(50_000, 50));
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();
                var offStripRejected = vm.Segments.Items.Count == before + 1 && pane.PlacementArmed;

                editor.Close();
                return inStripCreated && offStripRejected
                    ? null
                    : $"placement: inStripCreated={inStripCreated} offStripRejected={offStripRejected} " +
                      $"(before={before} after={vm.Segments.Items.Count})";

                static void PressPointer(TiledImageControl target, Point pt)
                {
                    var pointer = new Pointer(0, PointerType.Mouse, isPrimary: true);
                    var props = new PointerPointProperties(
                        RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed);
                    var args = new PointerPressedEventArgs(
                        target, pointer, target, pt, (ulong)Environment.TickCount64,
                        props, KeyModifiers.None, clickCount: 1);
                    target.RaiseEvent(args);
                }
            }
            finally
            {
                try { Directory.Delete(tmp, recursive: true); } catch { /* best effort */ }
            }
        });

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
                new EditorView(Path.Combine(Path.GetTempPath(), "pigcomic-smoke-missing.pcml")),
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
