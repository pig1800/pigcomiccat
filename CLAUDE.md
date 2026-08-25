# PigComic

Desktop CAT tool for comic/manga translation.

> ## Start here, every session
> 1. **Read the `## STATUS` block at the top of `docs/PLAN.md`.** It states what is already
>    built, what the verified build/test/smoke baseline is, and which task is next. The
>    markdown docs are the authoritative record of project state — treat them, not your
>    guesses and not leftover context, as the truth.
> 2. **As of 2026-08-25: M5.1–M5.6 are CODE DONE** (editor shell, segment list, overlays,
>    confirm loop, TM/TB box, keys/save/autosave). M5.1's acceptance PASSED; the M5.2–M5.6
>    acceptances are manual and wait on the owner (a batch test is described in the PLAN
>    STATUS block). M0–M4 and the IME work are done and the M2.5 IME gate passed 6/6
>    (`docs/IME_REPORT.md`). Do not re-run finished milestones, and do not re-open the IME
>    stack — it is verified against MS-IME Japanese, ATOK, MS Pinyin and MS-IME Korean.
> 3. Then read `docs/SPEC.md` — the single source of truth for behavior — before writing code.

Work strictly from the task list in `docs/PLAN.md`, one task per session, in order, starting from the first task the STATUS block does **not** mark done. If the spec is silent on something you need, do NOT guess: add it to `docs/OPEN_QUESTIONS.md` and stop.

## Stack
- .NET 8, C# (nullable enabled, implicit usings, file-scoped namespaces, 4-space indent).
- `src/PigComic.Core` — domain, `.pcml` I/O, TM/TB (SQLite via Microsoft.Data.Sqlite), QA, counting, export (ClosedXML). **No Avalonia/SkiaSharp references, no UI types, ever.**
- `src/PigComic.App` — **Avalonia 12.1.1** (upgraded from 11.2.5 on 2026-08-23, D-41), MVVM with CommunityToolkit.Mvvm, DI via Microsoft.Extensions.DependencyInjection. No business logic in view models beyond delegating to Core.
- SkiaSharp is pinned to **3.119.4** — the version Avalonia 12.1.1 requires. Do not downgrade it (NU1605 breaks the build).
- `tests/PigComic.Core.Tests` — xunit. Every Core feature ships with tests in the same task/milestone; the normative test tables are in SPEC §7.7, §11.1, §12.1, §26.

## Editor (M5) conventions

- **The editor is one window**: `EditorView` (three-pane Grid + two `GridSplitter`s), fed by
  `Services/ChapterSession` (owns `PcmlDocument`, dirty flag, save, journal Discard gate,
  media extraction to `%TEMP%/pigcomic-media/<hash>` — the tiled control needs a real file
  path; Core is never touched for media). `EditorLayoutStore` persists splitter widths into
  the §6.4 `registry.json` (merge-preserving).
- **Selection plumbing**: `SegmentListViewModel` holds the flat reading-order item list
  (page headers + `BubbleRowViewModel`s); `BubbleRowViewModel` wraps the Core `Bubble` and
  owns the `PartViewModel` cells, Draft-on-typing, lock (D-16), source F2-edit. `ConfirmService`
  implements §14.4 (Enter/Ctrl+Enter/Ctrl+Shift+Enter, empty-target rule, TM upsert with
  prevHash, `IConfirmQa` = `NullConfirmQa` until M8). `MatchListViewModel` is the §9 box
  (150 ms debounce, stale discard). Both directions of list↔image sync live in `EditorView`
  code-behind (`RefreshOverlays`, `CenterOn`, `OnOverlayClicked`).
- **Crash lessons (already cost two sessions — do not regress)**:
  - `TiledImageControl.Install` must read a replaced bitmap's `PixelSize` **before** disposing
    it (Avalonia `Bitmap` throws `ObjectDisposedException` on post-dispose access).
  - `TileDecoder` does the whole region crop **inside `_sync`** and checks `_disposed` first;
    the shared `_full` SKBitmap must never be read outside the lock, or a page-switch
    `Dispose` frees native memory mid-`DrawBitmap` (`AccessViolationException`).
  - Debug tool `ExampleChapterBuilder.Build` deletes an existing output before zipping
    (`ZipArchiveMode.Create` throws on an existing file). Never call `ProjectFolder.Create`
    on a non-empty folder (it throws) — for the debug example the stores auto-create via
    the `TmStore`/`TbStore` constructors.
- The debug example chapter (Debug menu → "Editor: open example chapter") renders the
  synthetic `StripImageGenerator` bands (colored 500 px bands with red lines) — that is the
  test pattern, not a bug; bubble overlays draw on top.
- **Avalonia 12 note**: `GridSplitter` uses `ResizeDirection="Columns"` (not `Column`) and
  persists widths via the `DragCompleted` (`VectorEventArgs`) handler. `Window` has an
  instance `KeyBindings` property, so the app's `PigComic.App.KeyBindings` helpers must be
  reached by full name from window subclasses.

## Avalonia 12 conventions (READ BEFORE WRITING UI CODE)
The project moved from Avalonia 11 to 12; older snippets, blog posts, and your own priors are often 11-era and will not compile. The rules that bite:

- **File/folder pickers**: the legacy `OpenFileDialog` / `SaveFileDialog` / `OpenFolderDialog` / `FileDialogFilter` types are **deleted**. Use `PigComic.App.Services.FilePickers` (`OpenFileAsync` / `SaveFileAsync` / `OpenFolderAsync`) — never call `IStorageProvider` directly, and never block on a picker with `.Result` / `.GetAwaiter().GetResult()` (it deadlocks the UI thread).
- **Compiled bindings are on by default**: every `DataTemplate` with `{Binding …}` needs `x:DataType="…"`, or you get `AVLN2000: Unable to resolve property … on XamlPseudoType`.
- **Focus overrides**: `OnGotFocus` / `OnLostFocus` both take `FocusChangedEventArgs` (11's `GotFocusEventArgs` and the `RoutedEventArgs` overload are gone).
- **ToggleButton/RadioButton**: the `Checked` / `Unchecked` events are gone — use `IsCheckedChanged`.
- **Text formatting ctors reordered**: `GenericTextRunProperties(typeface, fontSize, textDecorations, foregroundBrush, backgroundBrush, …, fontFeatures)` and `TextLayout(text, typeface, fontSize, foreground, …, fontFeatures:)` — `fontFeatures` moved to the end in both. Pass everything after the first few positionally-safe args by name.
- **IME**: Avalonia 12.1.0+ supplies the composition caret via `SetPreeditText(text, cursorPos)`. Never re-read `GCS_CURSORPOS` from IMM32 — that parameter is what makes the Chinese caret work. Conversion-clause data (`GCS_COMPCLAUSE`/`GCS_COMPATTR`) is still not provided upstream and is captured by `Ime/ImeMessageMonitor`, a WndProc hook that reads it **synchronously inside `WM_IME_COMPOSITION`**. Reading IMM32 anywhere else is off-contract and silently returns nothing on some IMEs (this was the ATOK bug) — so `imm32.dll` must stay imported in that one file only, behind `OperatingSystem.IsWindows()` (the App targets plain `net8.0`). Composition rendering is the modern flavor (SPEC §21.2): coloured text plus a coloured background on the active henkan clause, never underlines.

## Hard rules
- **Every editable text field in the app is a `controls:PartTextEditor`, never a bare `TextBox`.** It carries the whole IME stack — the in-message clause capture, the modern composition rendering, and the Enter/confirm guard — and a plain `TextBox` silently loses all of it (Avalonia's default underline rendering, no henkan highlight). This applies to target editors, source edits, character/notes boxes, dialog fields, find/replace, prompt editors — everything a user types CJK into. Set `ConfirmOnEnter="False"` where Enter must reach a default button (dialogs). The only sanctioned exceptions are the deliberate comparison box in `ImeTestWindow` and the debug `SpikeWindow` path field; `--smoke` fails the build-verification if a new unapproved `TextBox` appears.
- Never leave a half-written `.pcml`: all package writes go through the atomic save path (SPEC §5.5).
- `content.xml` round-trip must preserve unknown elements/attributes/zip entries (SPEC §5.8 — the XDocument is the model).
- Bubble IDs are never renumbered or reused. `Order` may be renumbered; ids may not.
- **A chapter has no pages** (D-49): its images are one continuous vertical strip and every coordinate is a chapter-global strip coordinate (SPEC §5.6). `Chapter.Locate(stripY)` is the only way back to a file, and only the tiled renderer and PSD export need it. Do not reintroduce a page concept in the model, the segment list, or navigation.
- **A bubble is a point, not a rectangle** (D-50): `PixelPoint` markers drawn as thick crosses, one click to create, drag to move, no resize. Comic text is often not rectangular (SFX especially), so a box was inaccurate and pointless work.
- Default language pair is **zh-CN → ja** (D-51) — dialogs, fixtures and examples.
- TM writes happen only on confirm; never auto-fill or auto-propagate a translation (repetitions are offered via popup only, SPEC §10).
- No LLM pretranslation feature. LLM = on-demand QA comments only (SPEC §13) via `ILlmClient`; the app must fully work with the stub.
- M2 was a gate: it PASSED 6/6 on 2026-08-24 (`docs/IME_REPORT.md`). Do not reopen the IME stack casually — read `docs/IME_HANDOFF.md` §5 first; it lists the traps that have already cost three sessions.
- Never load a full-size page image into an Avalonia `Bitmap` (tiled rendering, SPEC §20).
- Don't weaken/delete existing tests to make a task pass. `dotnet build PigComic.sln` + `dotnet test` must be green at the end of every task.
- `PigComic.sln` must build without any sibling repos; `PigComic.Full.sln` (adds the PigTranslate adapter) may require them.

## Verify

Run all three at the end of every task; all must be clean:

```
dotnet build PigComic.sln
dotnet test
src/PigComic.App/bin/Debug/net8.0/PigComic.App.exe --smoke
```

`--smoke` is a non-interactive UI self-check (exit 0 = pass). It verifies the
`PartTextEditorTheme` still applies, that `PART_TextPresenter` is the clause-aware
`ImeTextPresenter`, that a clause-highlighted preedit lays out, that every window's
XAML loads, and that the editor opens a real strip-media chapter, renders page 1, syncs
the selection to page 2 and runs the draft/confirm loop. **Run it after any Avalonia
upgrade, theme edit, or XAML change** — this failure class compiles fine and passes
`dotnet test`, so nothing else catches it. Add a check there whenever you add a window.
It is deliberately not an xunit test: instantiating Avalonia controls under the xunit
runner deadlocks (`docs/IME_HANDOFF.md` §8).

`dotnet run --project src/PigComic.App` launches the app interactively.
