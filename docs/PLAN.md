# PigComic — Implementation Plan

## STATUS — READ THIS BEFORE ANYTHING ELSE (2026-08-24)

**Next task: M5.1.** The M2.5 IME gate passed on 2026-08-24 (6/6, owner-run), so M5–M11 are
open. Start at the first task below that is not marked done.

| Milestone | State |
|---|---|
| M0 scaffold · M1 `.pcml` core · M2.1–M2.4 tiled canvas | ✅ **DONE — do not redo** |
| M3 TM/TB engine + exchange | ✅ **DONE — do not redo** |
| M4 project model, main view, dialogs, relink | ✅ **DONE — do not redo** |
| M2.6 in-message IMM32 clause capture · M2.7 modern IME rendering | ✅ **DONE** — verified by the owner's gate run across MS-IME JA, ATOK, MS Pinyin and MS-IME Korean |
| **M2.5 IME gate** | ✅ **PASSED 2026-08-24 — 6/6 recorded** (`docs/IME_REPORT.md`) |
| M5 – M11 | ▶️ **open — start at M5.1** |
| M-TSF (TSF text store) | ⏸ not started; Q7 resolved — stays scheduled after M6 (IMM32 path carries JA/ATOK today) |

Verified baseline at that commit: `dotnet build PigComic.sln` clean, **213/213 tests**,
**`--smoke` 17/17**. If those numbers differ when you start, something regressed — find out
what before touching anything else.

**If you are an executing model:** begin at **M5.1** (editor shell + chapter open). Do not
re-run the finished milestones above, and do not re-open the IME work — the gate is closed and
`src/PigComic.App/Ime/` is verified against four IMEs. If you touch any editable text field,
it must be a `PartTextEditor` (SPEC §21.2); `--smoke` enforces that.

The owner's gate record and the per-IME evidence live in `docs/IME_REPORT.md`; the IME
architecture and its do-not-reintroduce list are in `docs/IME_HANDOFF.md`. Read the latter
before changing anything under `Ime/`.

---

Read `docs/SPEC.md` first. Every behavior detail, schema, algorithm, and test table lives there; tasks below cite spec sections (§) instead of repeating them. **Do not invent behavior — if the spec is silent, stop and add an entry to `docs/OPEN_QUESTIONS.md` rather than guessing.**

> **Stack note (2026-08-23):** the app runs on **Avalonia 12.1.1**, not 11.x (D-41). Milestones M0–M4 below were written and executed against 11.2.5 and have since been migrated; their task text is kept as the historical record, but **any UI code you write now must follow the "Avalonia 12 conventions" section of `CLAUDE.md`**. 11-era snippets (`OpenFileDialog`, `GotFocusEventArgs`, `Checked=`, un-typed `DataTemplate`s) will not compile. When a task below names one of those APIs, use the 12 equivalent and note it in your summary.

Rules for executing a task:
- Touch only the files listed (plus their csproj for new files and the test project).
- A task is done when its **Acceptance** passes AND `dotnet build PigComic.sln`, `dotnet test`, and `PigComic.App.exe --smoke` are all green.
- Any task that adds a window or edits XAML/themes must also add or update a check in `src/PigComic.App/SmokeTest.cs`.
- **Every editable text field you add is a `controls:PartTextEditor`, never a bare `TextBox`** (SPEC §21.2 control rule). It carries the IME clause capture, the modern composition rendering and the Enter guard; a plain `TextBox` silently loses all three. Use `ConfirmOnEnter="False"` in dialogs so Enter still reaches the default button. `--smoke` fails if an unapproved `TextBox` appears.
- Never weaken or delete an existing test to make a task pass.
- One task per session. Tasks within a milestone are ordered; do them in order unless marked (parallel-ok).
- Manual acceptance steps state the exact expected result; run them on Windows.

Milestone order is risk-ordered and fixed. M2 was the gate for M5–M11; it recorded PASS in `docs/IME_REPORT.md` on 2026-08-24, so the remaining milestones are open.

---

## M0 — Solution scaffold ✅ DONE (do not redo)

### M0.1 Create solution and projects
- **Files**: `PigComic.sln`, `src/PigComic.Core/PigComic.Core.csproj`, `src/PigComic.App/PigComic.App.csproj`, `tests/PigComic.Core.Tests/PigComic.Core.Tests.csproj`, `.gitignore` (Visual Studio template), `.editorconfig` (dotnet defaults, 4-space indent, file-scoped namespaces).
- **Behavior**: Core = `net8.0` classlib, nullable+implicit usings; packages: `Microsoft.Data.Sqlite`, `ClosedXML`. App = `net8.0` **Avalonia 12.1.1** desktop app (Fluent theme) referencing Core; packages: `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter` (all 12.1.1), `CommunityToolkit.Mvvm`, `Microsoft.Extensions.DependencyInjection`, `SkiaSharp` **3.119.4** (must match Avalonia 12.1.1's requirement or NU1605 fails the build). Tests = xunit referencing Core. App shows an empty window titled "PigComic". Folder layout per SPEC §3.
- **Acceptance**: `dotnet build` succeeds; `dotnet test` runs 1 placeholder test; `dotnet run --project src/PigComic.App` opens a window.

### M0.2 DI + service skeleton
- **Files**: `src/PigComic.App/Program.cs`, `src/PigComic.App/App.axaml(.cs)`, `src/PigComic.App/Services/ServiceRegistry.cs`, `src/PigComic.Core/Adapters/ILlmClient.cs`, `src/PigComic.Core/Adapters/StubLlmClient.cs`.
- **Behavior**: `ServiceCollection` built at startup; register `ILlmClient` → `StubLlmClient` (SPEC §25.1: throws `LlmNotConfiguredException`). ViewModels resolve services via constructor injection.
- **Acceptance**: unit test constructs the provider and resolves `ILlmClient`; calling it throws `LlmNotConfiguredException`.

---

## M1 — Core data model + `.pcml` read/write ✅ DONE (do not redo)

### M1.1 Domain types
- **Files**: `src/PigComic.Core/Domain/` — `BubbleKind.cs`, `BubbleStatus.cs`, `PixelRect.cs`, `TargetPart.cs`, `Bubble.cs`, `Page.cs`, `Chapter.cs`.
- **Behavior**: exactly SPEC §4 (types may be XElement-backed later; for now plain properties + the invariants: parts 1..3, `TargetJoined` join with `\n`).
- **Acceptance**: `DomainTests` — constructing a bubble with 0 or 4 parts throws; `TargetJoined` of parts ["a","b"] == "a\nb".

### M1.2 Pcml load (zip + XML → model)
- **Files**: `src/PigComic.Core/Package/PcmlDocument.cs`, `PcmlLoadException.cs`.
- **Behavior**: `PcmlDocument.Open(path)` opens the zip, loads `content.xml` into an `XDocument` (kept per SPEC §5.8), builds XElement-backed domain objects (property setters write through to the element), sorts bubbles by (page order, order). Media entries enumerated (names + sizes), not decoded. Text values read verbatim; CRLF normalized to LF on read.
- **Acceptance**: `PcmlLoadTests` — loading a fixture zip containing SPEC §5.4's example XML yields: 2 pages in order, 3 bubbles sorted, bubble `p0001-b0002` has 2 parts, character `ピッグ` on b0001, `llmComment` non-empty on b0002.

### M1.3 Test fixture builder
- **Files**: `tests/PigComic.Core.Tests/PcmlTestBuilder.cs`, `tests/PigComic.Core.Tests/Fixtures/example1/content.xml` (SPEC §5.4 verbatim) + two tiny placeholder images.
- **Behavior**: builder API `PcmlTestBuilder.New(title, srcLang, tgtLang).Page(id,file,w,h).Bubble(id,page,order,...)...BuildZip(path)` producing valid packages in temp dirs; also `BuildFromXmlString`.
- **Acceptance**: builder output opens via `PcmlDocument.Open` with zero validation issues.

### M1.4 Validation
- **Files**: `src/PigComic.Core/Package/PcmlValidator.cs`, `PcmlIssue.cs`.
- **Behavior**: SPEC §5.7 exactly (codes, severities, read-only-on-error rule surfaced as `PcmlDocument.IsReadOnly`). W01 in-memory renumber; W02 in-memory chapter-character add.
- **Acceptance**: `PcmlValidationTests` — one theory row per code in the §5.7 table (malformed fixtures built with the builder / raw XML strings).

### M1.5 Atomic save + round-trip preservation
- **Files**: `src/PigComic.Core/Package/PcmlDocument.cs` (Save), `src/PigComic.Core/Package/AtomicZipWriter.cs`.
- **Behavior**: SPEC §5.5 (tmp + `File.Replace` + `.bak`; media and unknown zip entries stream-copied uncompressed/verbatim; `content.xml` re-serialized from the live XDocument with `xml:space="preserve"` rule §5.3; writer sorts bubbles, converts CRLF→LF).
- **Acceptance**: `PcmlRoundTripTests` (SPEC §26.7) — (a) load→save: unknown elements/attrs/zip entries preserved, media byte-identical; (b) load→edit one target→save: only that node changed (XML diff); (c) kill-during-save simulation: writing to a temp path that throws mid-write leaves the original file untouched and no `.tmp` residue on retry; (d) `.bak` contains the previous version.

### M1.6 Mutation API surface
- **Files**: `src/PigComic.Core/Package/PcmlDocument.cs` (methods), `src/PigComic.Core/Package/BubbleMutations.cs`.
- **Behavior**: typed mutations used by the UI and undo system later: `SetSource`, `SetPartText`, `SetPartCount` (§15.3 semantics incl. default band regions D-18), `SetStatus`, `SetKind`, `SetCharacter` (auto-adds to chapter characters), `SetNotes`, `SetLlmComment`, `SetSourceRegion`, `SetPartRegion`, `AddBubble` (id gen `u`+8hex collision-checked, order insertion per §15.2/D-17), `DeleteBubble`, `SetOrder`. Each returns a `MutationRecord` (op name + payload) for undo reuse.
- **Acceptance**: `MutationTests` — `SetPartCount(3→1)` joins texts with `\n` and resets region to source region; `AddBubble` on a page with bubbles at top-Y 100/500 and new region top-Y 300 gets order between them and renumbers; `SetCharacter("新角色")` adds to `<characters>`.

---

## M2 — Risk spikes: tiled canvas + IME gate ✅ DONE (gate PASSED 6/6 on 2026-08-24)

### M2.1 Tile math (Core, no Skia) ✅ DONE
- **Files**: `src/PigComic.Core/Imaging/TilePyramid.cs`, `TileKey.cs`, `LruByteCache.cs` (generic, byte-budgeted).
- **Behavior**: SPEC §20 pyramid math: level count for given dims (halve until ≤1024, min 2 downsampled), tile grid per level (512), `VisibleTiles(viewportRect, zoom, margin=1)` and `SelectLevel(zoom)`; LRU cache with byte accounting + eviction callback.
- **Acceptance**: `TilePyramidTests` — for 1000×40000 the pyramid has exactly 7 levels (dims 1000×40000, 500×20000, 250×10000, 125×5000, 63×2500, 32×1250, 16×625 — generation stops at level 6, the first with max dimension ≤ 1024); level 0 tile grid is 2×79 (512 px tiles); `SelectLevel(zoom=0.24)` returns level 2 (smallest level scale ≥ 0.24 is 0.25); LRU: inserting past the byte budget evicts oldest-touched first.

### M2.2 Synthetic strip generator ✅ DONE
- **Files**: `tests/PigComic.Core.Tests/Tools/StripImageGenerator.cs` + a `dotnet run`-able entry in `src/PigComic.App` debug menu later; generator writes numbered-band JPEG and PNG strips (1000×40000, text "y=NNNN" every 500 px) to a given path using SkiaSharp — put the generator in App project (`src/PigComic.App/Rendering/StripImageGenerator.cs`) since Core has no Skia.
- **Acceptance**: generated files exist, correct dimensions (decode headers), JPEG < 30 MB.

### M2.3 Background tile decoder ✅ DONE
- **Files**: `src/PigComic.App/Rendering/TileDecoder.cs`, `DecodeQueue.cs`.
- **Behavior**: SPEC §20 decode pipeline: 2 worker threads, priority queue by viewport distance, cancellation of stale requests; JPEG subset decode (`SKCodecOptions` with subset) for level 0, scaled decode for higher levels; PNG banded incremental decode (fallback rule in §20 if infeasible — record in DECISIONS).
- **Acceptance**: unit test (App project test allowed here or manual harness): request 20 tiles of the synthetic JPEG, all arrive < 3 s, none allocates a full-image bitmap (assert via decode API used); PNG path yields pixel-correct tiles (compare a tile against a reference crop).

### M2.4 TiledImageControl + spike window ✅ DONE
- **Files**: `src/PigComic.App/Controls/TiledImageControl.cs` (custom `ICustomDrawOperation`), `src/PigComic.App/Views/SpikeWindow.axaml(.cs)`, menu entry in main window (debug builds).
- **Behavior**: SPEC §20 draw rules (resident tiles ± margin, coarse-level placeholder upscale, gray fallback, invalidate on arrival); wheel scroll, Ctrl+wheel zoom at cursor, fit-width; FPS overlay (frame time ring buffer).
- **Acceptance** (manual, record numbers in `docs/IME_REPORT.md` sibling file `docs/SPIKE_REPORT.md`): scroll the 1000×40000 JPEG and PNG top-to-bottom fast: overlay reports ≥55 fps sustained; Task Manager working set < 900 MB; zoom in/out smooth.

### M2.5 IME gate — ✅ PASSED 2026-08-24 (6/6, owner-run)
- **Status**: the code side is complete and builds on Avalonia 12.1.1 (`PartTextEditor` + `ImeTextBoxInputMethodClient` + `ImeTextPresenter` + `PartTextEditorTheme`, SPEC §21.0). What remains is the **manual run**, which only the owner can do — it needs real MS IME sessions on Windows.
- **Files**: `src/PigComic.App/Views/ImeTestWindow.axaml(.cs)` (Debug menu → "IME gate test"); `docs/IME_REPORT.md`.
- **Behavior**: SPEC §21.1 checklist embedded in the window as tickboxes; the confirm counter must stay 0 while composing (the Enter guard, D-32).
- **Acceptance**: run the SPEC §21.1 checklist manually with MS IME Japanese and Korean; all **6** items PASS recorded in `docs/IME_REPORT.md`. **Until that is recorded, M5–M11 do not start.** If items 1–5 fail, follow §21.1's escalation; if only item 6's highlight half fails, consult the owner (see §21.1).
- **Outcome**: 6/6 PASS across MS-IME Japanese, ATOK, MS Pinyin and MS-IME Korean, including the modern clause rendering and the D-32 confirm guard. Evidence and per-IME detail in `docs/IME_REPORT.md`.
- **2026-08-23 update**: the owner's partial run found the caret working (CN/ZH) but no henkan clause data with ATOK. Root cause analysis and the fix plan are in `docs/IME_MODERN_COMPOSITION.md`; tasks M2.6 and M2.7 below must land before the owner re-runs this gate. Item 6 is now judged in the **modern flavor** (SPEC §21.2).

### M2.6 In-message IMM32 clause capture + IME diagnostics — CODE DONE 2026-08-24, owner run pending
- **Files**: `src/PigComic.App/Ime/ImeMessageMonitor.cs` (new), `Ime/ImeComposition.cs` (snapshot consumption), `Ime/ImeTextBoxInputMethodClient.cs`, `Views/ImeTestWindow.axaml(.cs)` (diagnostics toggle), `SmokeTest.cs` (monitor install/uninstall check).
- **Behavior**: `ImeMessageMonitor` installs a `Win32Properties.AddWndProcHookCallback` on each TopLevel that hosts a `PartTextEditor` (refcounted install on attach/detach; never sets `handled=true`). On `WM_IME_COMPOSITION` (0x010F) it reads — **synchronously, on the message stack, from `ImmGetContext(hwnd)`, releasing afterwards** — the fields flagged in lParam (`GCS_COMPSTR` 0x0008, `GCS_COMPCLAUSE` 0x0020, `GCS_COMPATTR` 0x0010, `GCS_CURSORPOS` 0x0080); lParam==0 means read everything (Gecko rule); unflagged fields are retained from the previous snapshot of the same composition. Snapshot stored per-hwnd; cleared on `WM_IME_ENDCOMPOSITION` (0x010E). `ImeTextBoxInputMethodClient.SetPreeditText` now consumes the snapshot (text-match guarded) and **never calls IMM32 itself** — `Imm32Native.GetClauseData` is deleted; the P/Invokes move into the monitor. Diagnostics: a toggle in `ImeTestWindow` appends one JSON line per composition message to `%TEMP%/pigcomic-ime-diag.log`: lParam bits, per-field byte counts, decoded clause array, attr bytes, compstr.
- **Acceptance**: build/test/smoke green; unit tests for snapshot retention semantics (fake message sequence: STARTCOMPOSITION-less first message, lParam==0, partial updates) in a new `ImeSnapshotTests`; **owner run**: with diagnostics on, one JA session each with ATOK and MS-IME (type かな, press Space, move segment with ←/→, commit) — the log must show non-zero COMPCLAUSE/COMPATTR byte counts for at least one IME. Record the log summary per IME in `docs/IME_REPORT.md`. If ATOK shows zero clause bytes **in-message**, IMM32 is dead for ATOK: flag the owner to decide whether M-TSF jumps ahead of M5 (docs/IME_MODERN_COMPOSITION.md §5 priority rule).
- **Delivered 2026-08-24**: `Ime/ImeMessageMonitor.cs` (WndProc hook via `Win32Properties.AddWndProcHookCallback`, refcounted per TopLevel, observer-only — never sets `handled`), `Ime/ImeCompositionSnapshot.cs` (lParam gating, cross-message merge, clause normalisation incl. byte-offset and monotonicity repair). `Imm32Native` is gone: `imm32.dll` is now imported in exactly one file, which is what structurally prevents the off-contract late read from coming back. `PartTextEditor` attaches/detaches the hook with the visual tree; `ImeTextBoxInputMethodClient` consumes the snapshot text-match-guarded and makes no IMM32 calls. `ImeTestWindow` gained the diagnostics toggle, a log summariser (reports clause/attr byte counts and a verdict), and a **capture kill switch** (also `PIGCOMIC_IME_NO_HOOK=1`) so the owner can A/B instantly if composition itself ever misbehaves. Tests: `ImeSnapshotTests` (12). Smoke: hook auto-attach + refcount balance.

### M2.7 Modern-flavor composition rendering — CODE DONE 2026-08-24, owner visual check pending
- **Files**: `Ime/ImeComposition.cs` (segment model), `Ime/ImeTextPresenter.cs`, `Ime/PartTextEditorTheme.axaml` (palette resources), `SmokeTest.cs` (modern-render layout check), `CLAUDE.md` (update the IME bullet), `docs/IME_HANDOFF.md` (state update).
- **Behavior**: SPEC §21.2. Replace reverse-video with a segment list `ImeSegment(Start, Length, ImeSegmentKind)` where `ImeSegmentKind` = `Input, Converted, ConvertedTarget, TargetNotConverted, InputError` (names deliberately mirror upstream `TextInputDecorationKind`, D-44), derived from contiguous COMPATTR runs (clause boundaries refine run edges when present). The presenter maps kind → (foreground, background, underline) via theme resources per the SPEC §21.2 palette; modern mode uses colored text and background, no underline for Input; no-attr degrade = whole preedit as `Input`. The caret must remain visible over every segment style.
- **Acceptance**: build/test/smoke green; `ImeSegmentTests` — attr byte runs → expected segments (incl. clause-refined edges, mismatched-length degrade); smoke check builds a 3-segment layout; **owner visual check**: JA composition shows colored text and the moving aqua target-clause background like Win11 Notepad; then re-run the full §21.1 gate (all 6 items, JA+KO; ATOK + MS-IME for item 6).
- **Delivered 2026-08-24**: `Ime/ImeSegment.cs` (`ImeSegmentKind` + `ImeSegmentBuilder`, attribute runs split at clause edges, always tiling the preedit exactly); `ImeComposition.Segments`; `ImeTextPresenter` rewritten to per-segment styling with four theme-bound brushes (`CompositionForeground`, `TargetClauseForeground`, `TargetClauseBackground`, `InputErrorUnderline`) replacing the old reverse-video; palette added to `PartTextEditorTheme.axaml` as light/dark `ThemeDictionaries` per SPEC §21.2. Tests: `ImeSegmentTests` (17). Smoke: palette-bound, 3-segment layout, and the no-clause degrade path asserting the caret position survives (the ZH-caret regression guard).

---

## M3 — TM/TB engine + exchange ✅ DONE (do not redo)

### M3.1 Normalizer
- **Files**: `src/PigComic.Core/Tm/Normalizer.cs` (`Normalize`, `NormLength`, `LangClass`).
- **Behavior**: SPEC §7.2 exactly, in the given step order.
- **Acceptance**: `NormalizerTests` — every row of §7.7 asserting normalized equality/inequality, plus explicit steps: `Normalize("１００人","ja")=="100人"`, `Normalize("A  B","en")=="a b"`, `Normalize("そうか・・・","ja")=="そうか…"`, `Normalize("“x”","en")=="「x」"`.

### M3.2 Hash + grams
- **Files**: `src/PigComic.Core/Tm/TmHash.cs`, `src/PigComic.Core/Tm/GramExtractor.cs`.
- **Behavior**: SPEC §7.3 (SHA-256 first 8 bytes LE long), §7.4 (bigrams for ja/zh by code point incl. single-char case; tokens otherwise).
- **Acceptance**: `TmHashTests` — hash is stable across two calls and differs for different strings; `GramExtractorTests` — `こんにちは` → 4 bigrams; `x` (ja) → 1 gram `x`; `i like cats` (en) → 3 tokens.

### M3.3 TM store (SQLite)
- **Files**: `src/PigComic.Core/Tm/TmStore.cs`, `src/PigComic.Core/Tm/TmEntry.cs`, `src/PigComic.Core/Data/SqliteInit.cs`.
- **Behavior**: create/migrate schema SPEC §7.1 (WAL); `Upsert(source, target, character, kind, chapter, bubbleId, prevHash)` with the (hash, character) newest-wins rule; `Delete(id)`; `RebuildGrams()`; meta language pair set at creation, checked on open.
- **Acceptance**: `TmStoreTests` — upsert same source+character twice → 1 row, target = second; same source, different character → 2 rows; grams rows exist per §7.4; RebuildGrams after manual grams wipe restores identical set; opening a store with mismatched language pair throws.

### M3.4 Levenshtein + query
- **Files**: `src/PigComic.Core/Tm/Levenshtein.cs` (code-point based, early-exit banded optional), `src/PigComic.Core/Tm/TmQueryService.cs`, `TmMatch.cs`, `TmQueryContext.cs`.
- **Behavior**: SPEC §7.5 verbatim (short-segment rule, Dice ≥0.4 cap 50, base ≥70, boosts +1/+1/+1 cap 103, ordering, top 20).
- **Acceptance**: `TmMatchTests` — the full §7.7 table (store Stored rows, query Query, assert presence/absence and exact score).

### M3.5 TB store + hit test
- **Files**: `src/PigComic.Core/Tb/TbStore.cs`, `TbTerm.cs`, `src/PigComic.Core/Tb/TermHitTester.cs`.
- **Behavior**: SPEC §8 (schema, `ContainsTerm` per lang class, lookup ordering §8.3).
- **Acceptance**: `TbTests` — ja substring hit `悟空` in `悟空だ!`; en `cat` does NOT hit `concatenate` (token boundary) but hits `a cat!`; lookup ordering by first occurrence then longer term first; forbidden row with empty source allowed.

### M3.6 Combined results service
- **Files**: `src/PigComic.Core/Tm/MatchListService.cs`, `MatchListItem.cs`.
- **Behavior**: SPEC §9 list assembly (TM desc then TB, numbering, forbidden never insertable — expose `IsInsertable`).
- **Acceptance**: `MatchListTests` — with 2 TM matches (91%, 88%) and 2 TB hits, items are numbered 1..4 in that order; forbidden TB item has `IsInsertable == false`.

### M3.7 TMX exchange
- **Files**: `src/PigComic.Core/Exchange/TmxExchange.cs`, `ImportReport.cs`, interface file `src/PigComic.Core/Adapters/ITmExchange.cs`.
- **Behavior**: SPEC §19 TMX bullet (export props x-character/x-kind/x-chapter; import primary-subtag matching, tag stripping with count, upsert, grams rebuild, report).
- **Acceptance**: `TmxTests` — export 3 entries → reimport into a fresh store → identical rows; importing a hand-written TMX fixture with `<bpt>` tags strips them and reports 1 tag-stripped warning; wrong-language TMX → error listing its languages.

### M3.8 TBX exchange
- **Files**: `src/PigComic.Core/Exchange/TbxExchange.cs`, `src/PigComic.Core/Adapters/ITbExchange.cs`.
- **Behavior**: SPEC §19 TBX bullet (TBX-Basic export, deprecatedTerm-admn-sts = forbidden both directions).
- **Acceptance**: `TbxTests` — round-trip 3 terms incl. one forbidden; import of a minimal TBX-Min fixture.

### M3.9 XLSX exchange (TM + TB)
- **Files**: `src/PigComic.Core/Exchange/TmXlsxExchange.cs`, `TbXlsxExchange.cs`.
- **Behavior**: SPEC §19 XLSX bullets (exact headers, header-name column matching, 禁止 non-empty → forbidden).
- **Acceptance**: `XlsxExchangeTests` — round-trips; import with shuffled/extra columns still maps correctly; missing 译文 column → error.

---

## M4 — Project model + main view + relink ✅ DONE (do not redo)

### M4.1 Project files (Core)
- **Files**: `src/PigComic.Core/Project/ProjectFile.cs` (`project.json` via `JsonNode` round-trip per D-07), `CharacterList.cs` (`characters.json`), `ProjectSettings.cs` (typed accessors with SPEC §6.2 defaults), `ProjectFolder.cs` (create-new: folder layout §6.1, empty TM/TB via SqliteInit).
- **Acceptance**: `ProjectFileTests` — create → load: defaults present; unknown JSON property survives load→save; character add/remove round-trips; create-new produces tm.db/tb.db openable by the stores.

### M4.2 Registry (Core)
- **Files**: `src/PigComic.Core/Project/ProjectRegistry.cs`.
- **Behavior**: SPEC §6.4 (APPDATA path, MRU order, add/remove/touch).
- **Acceptance**: `RegistryTests` using a temp APPDATA override (constructor takes base dir).

### M4.3 Main view (project list)
- **Files**: `src/PigComic.App/Views/MainWindow.axaml(.cs)`, `ProjectListView.axaml(.cs)`, `ViewModels/ProjectListViewModel.cs`, `Services/DialogService.cs`.
- **Behavior**: SPEC §6.5 list + Open (file picker) + keyboard navigation.
- **Acceptance** (manual): create two registry entries by hand → both listed MRU-first; Enter opens (project view placeholder); Open picker adds a project.

### M4.4 Create-project dialog
- **Files**: `src/PigComic.App/Views/CreateProjectDialog.axaml(.cs)`, `ViewModels/CreateProjectViewModel.cs`.
- **Behavior**: SPEC §6.5 Create (validation: title required, folder empty-or-new; language dropdowns + free text).
- **Acceptance** (manual): creating a project produces the §6.1 folder; invalid folder shows inline error and OK disabled.

### M4.5 Remove-project dialog
- **Files**: `src/PigComic.App/Views/RemoveProjectDialog.axaml(.cs)` + VM.
- **Behavior**: SPEC §6.5 Remove (two radios, delete needs extra checkbox, permanent delete D-08).
- **Acceptance** (manual): list-only leaves folder; delete removes folder; `.pcml` files referenced from the project are untouched in both cases.

### M4.6 Project view + chapter list
- **Files**: `src/PigComic.App/Views/ProjectView.axaml(.cs)`, `ViewModels/ProjectViewModel.cs`.
- **Behavior**: chapter table (file name, meta/chapter, exists?), buttons: Add chapter (`.pcml` picker → appended to `project.json`), Remove from project (no file deletion, confirm), Move up/down, Open chapter (placeholder until M5), Statistics (placeholder until M8), Master characters (M7), TM/TB import-export menu wired to M3.7–M3.9 with file pickers + report dialog.
- **Acceptance** (manual): add/reorder/remove chapters persists to `project.json`; TM XLSX export of a seeded store produces the §19 file.

### M4.7 Relink dialog
- **Files**: `src/PigComic.App/Views/RelinkDialog.axaml(.cs)` + VM.
- **Behavior**: SPEC §6.6 exactly (per-file Browse, Search-folder bulk resolve by filename, OK gating, Cancel → stay on main list).
- **Acceptance** (manual): rename a referenced `.pcml` → open project → dialog lists it; Search-folder resolves it; Cancel keeps you on the project list without opening.

---

## M5 — Editor: segment list + target editor + confirm loop + TM/TB box ▶️ NEXT (gate cleared)

### M5.1 Editor shell + chapter open
- **Files**: `src/PigComic.App/Views/EditorView.axaml(.cs)`, `ViewModels/EditorViewModel.cs`, `Services/ChapterSession.cs` (owns PcmlDocument, dirty flag, save).
- **Behavior**: SPEC §14.1 three-pane layout with splitters (persisted); opens a chapter: validation gate (§5.7 read-only rule), image pane shows page 1 via `TiledImageControl`, status bar fields. If a `.pcml.journal` exists at open, show an interim two-button dialog — [Discard] (deletes the journal, opens the chapter) / [Cancel] (aborts opening); the full three-button [Recover] dialog replaces this in M9.2 per SPEC §23.
- **Acceptance** (manual): open the M1.3 example chapter → three panes, page image renders, status bar correct.

### M5.2 Segment list
- **Files**: `src/PigComic.App/Views/SegmentListView.axaml(.cs)`, `ViewModels/SegmentListViewModel.cs`, `BubbleRowViewModel.cs`.
- **Behavior**: SPEC §14.3 (virtualized, page group headers, source column content, status row tint; target column read-only placeholder this task).
- **Acceptance** (manual): example chapter lists 3 rows under 2 page headers in reading order; selection with `Ctrl+Up/Down` works and status bar updates.

### M5.3 Image pane overlays + selection sync
- **Files**: `src/PigComic.App/Controls/TiledImageControl.cs` (overlay layer), `ViewModels/ImagePaneViewModel.cs`.
- **Behavior**: SPEC §14.2 (status-colored source region outlines, selected style, dashed part regions, click-to-select with smallest-topmost rule, auto-scroll/page-switch to selection, media scale factor §5.6).
- **Acceptance** (manual): clicking each region selects its row; selecting the p0002 bubble from the list switches page and centers the region; a package whose media is a 50%-downscaled copy still draws overlays on the right spots (make one with the test builder).

### M5.4 Target editor + confirm loop
- **Files**: `src/PigComic.App/Controls/PartTextEditor.cs` (TextBox subclass with the M2.5 IME-safe Enter guard), `SegmentListView` target column templates, `ViewModels/ConfirmService.cs`.
- **Behavior**: SPEC §14.3 target column (parts stacked, centered) + §14.4 full confirm loop (Draft on typing, Enter/Ctrl+Enter/Ctrl+Shift+Enter, TM write with context prevHash, empty-target rule, Locked read-only, Ctrl+L per D-16, Ctrl+Insert copy-source). ⚡QA hook is a no-op until M8 (interface `IConfirmQa` with null implementation).
- **Acceptance** (manual): translate the 3 example bubbles with Enter — statuses turn Translated, selection advances, TM rows appear in tm.db (check via a temporary debug menu "dump TM count"); JA IME composition in the editor passes §21 items 1–4; Ctrl+Enter from bubble 1 skips confirmed bubble 2 to reach 3.

### M5.5 TM/TB box
- **Files**: `src/PigComic.App/Views/MatchListView.axaml(.cs)`, `ViewModels/MatchListViewModel.cs`, function pane shell `Views/FunctionPaneView.axaml`.
- **Behavior**: SPEC §9 (debounced query, numbering, Ctrl+1..9 + double-click insert semantics incl. TM-insert collapse D-12 and status→Draft, TB caret insert, row rendering incl. diff underline — a simple per-code-point LCS diff is specified as: underline code points of the stored source not present in the LCS with the query).
- **Acceptance** (manual): confirm `おはようございます` in bubble 1; create a 4th bubble via builder-made package with source `おはよう ございます` → selecting it shows a 100% match; Ctrl+1 fills the target and sets Draft; a TB term seeded via XLSX import appears after TM matches and Ctrl+N at caret inserts only the term.

### M5.6 Keyboard map + save/autosave
- **Files**: `src/PigComic.App/KeyBindings.cs`, wiring in EditorView; `Services/AutosaveTimer.cs`.
- **Behavior**: every §14.6 binding not yet wired (F2 source edit, zoom keys, PageUp/Down, Esc, focus jumps); Ctrl+S manual save; autosave per §5.5/§6.2 with status-bar "Saved HH:mm" / dirty dot.
- **Acceptance** (manual): run through §24's keyboard-only loop on the example chapter without touching the mouse (except region drawing) — every step succeeds; killing the app after an edit and before autosave leaves `.pcml` unchanged (journal comes in M9); waiting 3+ min autosaves (set `autosaveSeconds: 10` for the test).

---

## M6 — Region editing + target split

### M6.1 Region move/resize
- **Files**: `src/PigComic.App/Controls/RegionInteraction.cs` (hit-test + drag state machine), TiledImageControl integration.
- **Behavior**: SPEC §15.1 (8 handles, min size, clamp on commit, one undo record per gesture via M1.6 `SetSourceRegion`).
- **Acceptance** (manual): drag/resize each example bubble; Esc mid-drag reverts; saved file shows new coordinates (unzip and inspect).

### M6.2 Create bubble
- **Files**: `RegionInteraction.cs` (draw mode), `EditorViewModel` command.
- **Behavior**: SPEC §15.2 create (Ctrl+B arm, drag, id/order rules, Shift-hold repeat).
- **Acceptance** (manual): draw a region between two existing bubbles → new row appears between them with kind Speech/Untranslated; saved XML has `u`-prefixed id and renumbered orders.

### M6.3 Delete bubble
- **Files**: `EditorViewModel` command + confirm dialog.
- **Behavior**: SPEC §15.2 delete.
- **Acceptance** (manual): Delete removes row+overlay; Ctrl+Z restores it fully (after M9 undo exists — until then acceptance is: removed and persisted correctly on save; amend this acceptance to include undo when M9 lands).

### M6.4 Target split parts
- **Files**: `ViewModels/SegmentListViewModel.cs` + `PartTextEditor` Tab handling + `EditorViewModel` Alt+1/2/3 commands (Core work already in M1.6).
- **Behavior**: SPEC §15.3 (band defaults, merge join, Tab/Shift+Tab, part region drag).
- **Acceptance** (manual): Alt+3 on a bubble shows 3 stacked editors and 3 dashed bands; text typed in parts round-trips through save; Alt+1 merges with `\n`s; TM write after confirm stores the joined target (verify via match box on a twin bubble).

---

## M7 — Characters, kind, notes

### M7.1 Master character editor
- **Files**: `src/PigComic.App/Views/CharacterMasterWindow.axaml(.cs)` + VM; Core work is M4.1's `CharacterList`.
- **Behavior**: SPEC §16 master editor incl. Ctrl+V clipboard image paste → PNG file (§6.3), name uniqueness, delete confirm.
- **Acceptance** (manual): paste a screenshot into a character's image cell → PNG appears in `characters/` and renders in the grid; duplicate name rejected inline.

### M7.2 Character box (function pane)
- **Files**: `src/PigComic.App/Views/FunctionPaneView.axaml(.cs)`, `ViewModels/CharacterBoxViewModel.cs`, autocomplete control `Controls/AutoCompleteBox` usage.
- **Behavior**: SPEC §14.5 item 3 + §16 (chapter-name buttons, autocomplete over master, new-name flow with master-list offer).
- **Acceptance** (manual): click a chapter button → bubble's `@character` set and source column shows it; type a brand-new name + Enter → appears as a new button and in saved `<characters>`; the "add to master" prompt opens the master editor prefilled.

### M7.3 Kind selector + notes + LLM comment display
- **Files**: `FunctionPaneView` additions, VM.
- **Behavior**: SPEC §14.5 items 2/4/5; Ctrl+Shift+K/C/N focus jumps.
- **Acceptance** (manual): change kind → overlay/source column glyph update and XML round-trips; notes persist; `llmComment` from the example file displays and its clear button empties it.

---

## M8 — Mechanical QA, repetition, find/replace, stats

### M8.1 Counter port
- **Files**: `src/PigComic.Core/Counting/LanMangaCounter.cs`, `ICounter.cs`, `CountResult.cs`.
- **Behavior**: SPEC §11 exactly (D-14 deltas).
- **Acceptance**: `CounterTests` — full §11.1 table.

### M8.2 VisualLength + QA engine
- **Files**: `src/PigComic.Core/Qa/VisualLength.cs`, `QaEngine.cs`, `QaIssue.cs`, `QaConfig.cs` (bound from ProjectSettings).
- **Behavior**: SPEC §12 all rules incl. ⚡ subset entry point `RunOnBubble`, full `RunOnChapter`, `RunOnProject`; §12.1 TCY.
- **Acceptance**: `VisualLengthTests` (§12.1 table) + `QaRuleTests` (positive+negative per rule; TERM/FORBID cases seeded via TbStore).

### M8.3 QA panel + on-confirm QA
- **Files**: `src/PigComic.App/Views/QaPanelView.axaml(.cs)` + VM; ConfirmService `IConfirmQa` real implementation; inline row markers.
- **Behavior**: SPEC §12 (F8 chapter run → dockable bottom panel, double-click navigate; ⚡ issues as icons on the row with tooltip; errors don't block confirm D-15).
- **Acceptance** (manual): craft a chapter with one violation per rule (builder) → F8 lists them all; confirming a bubble with an over-long line shows the marker immediately.

### M8.4 Repetition offer
- **Files**: `src/PigComic.Core/Qa/RepetitionFinder.cs`; `src/PigComic.App/Controls/RepetitionPopup.axaml(.cs)`.
- **Behavior**: SPEC §10 exactly (trigger conditions, nearest-preceding, non-modal toast, Alt+R/Esc, Draft on insert, never auto-fill).
- **Acceptance**: `RepetitionFinderTests` (Core: trigger matrix — empty vs non-empty target, status combinations, same-chapter only) + manual: duplicate-source fixture shows the popup; Alt+R inserts; navigating away closes it.

### M8.5 Find/replace
- **Files**: `src/PigComic.Core/Search/SearchService.cs`, `SearchHit.cs`; `src/PigComic.App/Views/FindReplaceView.axaml(.cs)` + VM.
- **Behavior**: SPEC §17.1 (scopes, fields, options, Locked skip, Draft demotion D-19, project-scope background load + immediate atomic save of non-open chapters, source-replace warning).
- **Acceptance**: `SearchServiceTests` (Core: regex + case options, Locked skipped, status demotion recorded) + manual: project-wide replace across 2 chapters updates both files; result list navigates into the closed chapter by opening it.

### M8.6 Project settings dialog
- **Files**: `src/PigComic.App/Views/ProjectSettingsDialog.axaml(.cs)` + VM (Core work is M4.1's `ProjectSettings`).
- **Behavior**: edits `project.json` `settings` (SPEC §6.2): QA limits (`maxCharsPerLine`, `maxLinesPerPart`, `tcyMaxDigitRun`, exempt kinds, trailing/bracket lists), `autosaveSeconds`, export options. (LLM settings get their own dialog in M10.4.) Numeric fields validated; OK writes via the JsonNode round-trip path.
- **Acceptance** (manual): change `maxCharsPerLine` → F8 results change accordingly; hand-added unknown JSON property in `project.json` survives an OK.

### M8.7 Statistics view
- **Files**: `src/PigComic.Core/Counting/StatsService.cs`; `src/PigComic.App/Views/StatsView.axaml(.cs)` + VM.
- **Behavior**: SPEC §17.2 (columns, totals row, closed-chapter reads, XLSX export of the table).
- **Acceptance**: `StatsServiceTests` (counts per fixture chapter match hand-computed values) + manual: stats view over the example project matches; exported XLSX opens in Excel with the same numbers.

---

## M9 — Journal/crash recovery + undo/redo

### M9.1 Journal writer (confirm-only)
- **Files**: `src/PigComic.Core/Package/Journal.cs`, `BubbleSnapshot.cs`; ConfirmService integration (one snapshot line per confirm, written before the TM upsert).
- **Behavior**: SPEC §23 — confirm-only granularity (D-34): full bubble snapshot JSONL, flush-to-disk per line, seq from 1, delete-on-save. Nothing but confirms journals; no per-keystroke or per-mutation I/O.
- **Acceptance**: `JournalTests` — snapshot round-trips through JSON with all fields (incl. 2-part split and regions); confirming 3 bubbles yields exactly 3 lines; editing text without confirming yields 0 lines; save deletes the file.

### M9.2 Recovery replay
- **Files**: `Journal.cs` (Replay = snapshot upsert), `src/PigComic.App/Views/RecoveryDialog.axaml(.cs)`; replaces the M5.1 interim dialog.
- **Behavior**: SPEC §23 (three-button dialog; upsert by bubble id — overwrite existing, recreate missing bubbles on their page, skip-with-count on unknown page; replay then save; corrupt-tail tolerance).
- **Acceptance**: `JournalTests` replay-equivalence (SPEC §26.8) incl. truncated-last-line case and the created-then-confirmed-never-saved bubble; manual: kill the app after confirming 2 bubbles → reopen → Recover restores both as Translated.

### M9.3 Undo/redo
- **Files**: `src/PigComic.Core/Undo/UndoStack.cs`, `IUndoableAction.cs`, `CoalescingTextAction.cs`; wiring in ChapterSession/ConfirmService/RegionInteraction.
- **Behavior**: SPEC §22 (scope list, coalescing rules, gesture granularity, cap 200, TM non-undo D-23, replace-all composite).
- **Acceptance**: `UndoStackTests` (coalescing timing via injectable clock; composite actions; cap) + manual: type→Ctrl+Z restores prior text in one step; region drag undo restores geometry; undo of confirm reverts status but the match box still shows the TM entry; M6.3's delete-undo acceptance now passes.

---

## M10 — XLSX export + LLM QA

### M10.1 Chapter XLSX export (Core)
- **Files**: `src/PigComic.Core/Export/ChapterXlsxExporter.cs`, `IChapterXlsxExporter.cs`, `ExportOptions.cs`; thumbnail resize helper in App passed in as a delegate (`Func<Stream,int,Stream>` JPEG-425px) to keep Skia out of Core.
- **Behavior**: SPEC §18 exactly (headers/widths/styles, picture anchoring, row-walk placement, height clearing, optional `_pigcomic` very-hidden sheet D-21, multi-chapter workbook).
- **Acceptance**: `ChapterExportTests` — structural asserts via ClosedXML read-back: headers/fill/freeze correct, with C1 = `日文` for a ja-source fixture and `中文` for a zh-source fixture (§18 language-label mapping, D-33); every bubble's source appears exactly once in column C in reading order; D column holds joined targets; with `includeDataSheet` the hidden sheet has one row per bubble with correct coordinates; **count equivalence**: LanMangaCounter over column C values == chapter source counts (§18 guarantee). Manual: open in Excel — images sit beside their text like the legacy tools' output.

### M10.2 Export UI
- **Files**: `ProjectViewModel`/`EditorViewModel` commands + save dialog; progress toast.
- **Behavior**: Export current chapter (editor) / selected chapters (project view, one workbook).
- **Acceptance** (manual): both paths produce files; exporting an unsaved dirty chapter saves it first (prompt).

### M10.3 LLM QA orchestration (Core)
- **Files**: `src/PigComic.Core/Qa/LlmQaService.cs`, `LlmQaPromptBuilder.cs`, `LlmQaMemory.cs` (+ default `llm-qa-prompt.txt` template resource).
- **Behavior**: SPEC §13 (scope; prompt assembly = prompt file + character sheet + memory section + bubble JSON; provider/model/temperature from `settings.llmQa`; strict JSON-**object** extraction — first `{`..matching`}`; id filtering; llmComment writes; memory full-replace with 16,000-char cap rejection; error path leaves everything untouched).
- **Acceptance**: `LlmQaTests` with a fake ILlmClient — well-formed reply writes comments to the right bubbles AND replaces `llm-qa-memory.md`; reply without `memory` leaves the file unchanged; over-cap memory → comments applied, memory unchanged, warning reported; garbage reply writes nothing; prompt contains the character-sheet rows for exactly the in-scope speakers plus the current memory text; the fake records `Provider=="claude"` and `Model=="claude-opus-5"` from default settings.

### M10.4 LLM QA UI + settings dialog + PigTranslate adapter project
- **Files**: function-pane split-button wiring + progress/cancel dialog; `src/PigComic.App/Views/LlmQaSettingsDialog.axaml(.cs)` + VM (prompt editor tab, memory viewer/editor tab, provider dropdown + model text box + temperature); `src/PigComic.Adapters.PigTranslate/PigComic.Adapters.PigTranslate.csproj` + `PigTranslateLlmClient.cs`; `PigComic.Full.sln`.
- **Behavior**: SPEC §13 settings dialog; SPEC §25.1 (stub default; adapter dispatches provider → `CreateClaudeJobSync`/`CreateOpenAIJobSync`/`CreateGeminiJobSync` via Task.Run; Full solution only — `PigComic.sln` must keep building without the Translator repo present).
- **Acceptance**: with stub: run button → "LLM not configured" toast; settings dialog edits round-trip to `llm-qa-prompt.txt`, `llm-qa-memory.md`, and `project.json` `settings.llmQa`. Adapter project: `dotnet build PigComic.Full.sln` succeeds on the owner's machine (owner-run step); a real chapter run attaches comments and updates memory (owner-run).

---

## M11 — Deferred (do not start without owner go-ahead)

- **M11.1 Page add/remove/reorder** per SPEC §27.1 (page strip UI, media copy-in, refusal rule).
- **M11.2 PSD export** per SPEC §27.2 — first task is a written decision (DECISIONS entry) on the PSD writer library after a 1-day spike; then background+Typesetting-group writer; acceptance: exported PSD opens in Photoshop with editable text layers at part positions on an original-size canvas.

---

## M-TSF — TSF text store for PartTextEditor (independent track; Phase C of docs/IME_MODERN_COMPOSITION.md)

**Scheduling**: recommended slot is after M6 (editor + regions solid) so the store lands on the real editor; it jumps **ahead of M5** only if M2.6's diagnostics prove ATOK yields no clause data via IMM32 even in-message (owner decides). Each task must leave the IMM32 path intact — the TSF store is gated by an app setting (`tsf.enabled`, default off until MT.6) and by an Avalonia version check that stands it down when upstream `Win32PlatformOptions.UseTsfTextInput` ships with decoration rendering (watch PR #20890).

Read `docs/IME_MODERN_COMPOSITION.md` §4 and its source index before every task. Prime references: Windows Terminal `src/tsf/Implementation.cpp` (colored rendering), Chromium `tsf_text_store.cc` (lock/notification discipline), MS `tsfapp/textstor.cpp` (ACP method reference), WPF `TextStore.cs` + `UnsafeNativeMethodsTextServices.cs` (MIT, managed interop), Avalonia PR #20890 (design decisions to mirror: `TrackProperties` not `GetProperty`; attach client before `AssociateFocus`; clear association before client on detach).

### MT.1 TSF interop layer
- **Files**: `src/PigComic.App/Ime/Tsf/NativeMethods.txt` + CsWin32 package refs in `PigComic.App.csproj`, `Ime/Tsf/TsfInterop.cs` (CLSIDs/GUIDs/constants CsWin32 doesn't emit).
- **Behavior**: `Microsoft.Windows.CsWin32` generates `ITextStoreACP`, `ITextStoreACPSink`, `ITfThreadMgr`, `ITfDocumentMgr`, `ITfContext`, `ITfSource`, `ITfContextOwnerCompositionSink`, `ITfTextEditSink`, `ITfReadOnlyProperty`, `ITfRangeACP`, `ITfCategoryMgr`, `ITfDisplayAttributeMgr/Info`, `TF_DISPLAYATTRIBUTE` etc. (verified to build on this machine 2026-08-23). All TSF code Windows-guarded.
- **Acceptance**: solution builds with the generated interfaces; a unit test instantiates a class implementing generated `ITfContextOwnerCompositionSink`; smoke unchanged.

### MT.2 Thread manager + document plumbing
- **Files**: `Ime/Tsf/TsfManager.cs`.
- **Behavior**: per-UI-thread: CoCreate `TF_ThreadMgr` → `Activate`; optionally set the JA sentence-mode compartment (`TF_SENTENCEMODE_PHRASEPREDICT`, Chromium `tsf_bridge.cc:233-256`). Per editor: `CreateDocumentMgr` → `CreateContext(store)` → `Push`; advise `ITfTextEditSink`. Focus protocol on `PartTextEditor` Got/LostFocus: set `InputMethod.SetIsInputMethodEnabled(control,false)` variant flow per docs/IME_MODERN_COMPOSITION.md §4 (Avalonia goes quiet), attach store client, then `AssociateFocus`; reverse order on detach; keep docmgr refs alive (AssociateFocus does not addref).
- **Acceptance**: with `tsf.enabled` and a debug "TSF status" readout in `ImeTestWindow`: focusing the TSF test editor shows Activated/DocFocused; focusing a normal TextBox restores stock IMM32 behavior (JA typing still works there). App runs with the setting off exactly as before.

### MT.3 Minimal ITextStoreACP store (composition commits text)
- **Files**: `Ime/Tsf/TsfTextStore.cs`.
- **Behavior**: the ~12 real methods (AdviseSink/UnadviseSink, RequestLock with sync/async queue per Chromium §620-706, GetStatus, QueryInsert, Get/SetSelection, GetText/SetText, InsertTextAtSelection, GetEndACP, GetActiveView, GetWnd) + `E_NOTIMPL` stubs; notifications only after unlock, never echoing TIP edits; document = the `PartTextEditor` text + caret.
- **Acceptance** (owner run): JA text can be composed and committed into the TSF test editor with MS-IME and ATOK; backspace/escape/mid-string composition behave like Notepad; Core tests + smoke green; toggling `tsf.enabled` off restores the IMM32 path.

### MT.4 Geometry: GetTextExt / candidate window
- **Files**: `TsfTextStore.cs` (GetTextExt/GetScreenExt/GetACPFromPoint), presenter hit-test helpers.
- **Behavior**: answer `GetTextExt` from the presenter's synchronous layout (DIP→screen via `TopLevel.RenderScaling` + ClientToScreen); fire `OnLayoutChange` after every composition repaint; `TS_E_NOLAYOUT` only when truly unavoidable (docs/IME_MODERN_COMPOSITION.md §4).
- **Acceptance** (owner run): the candidate window opens adjacent to the composition caret (not screen corner) for MS-IME and ATOK, in both a top-of-window and bottom-of-window editor placement, at 100% and 150% DPI.

### MT.5 Display attributes → modern rendering
- **Files**: `TsfTextStore.cs` (`ITfTextEditSink.OnEndEdit` → `TrackProperties(GUID_PROP_ATTRIBUTE)` walk → atom → `ITfCategoryMgr.GetGUID` → `ITfDisplayAttributeMgr.GetDisplayAttributeInfo`), mapping to the M2.7 segment model extended with optional explicit colors (`Foreground?/Background?/Underline` — mirroring `TextInputDecoration`, D-44).
- **Behavior**: TIP-specified `TF_CT_COLORREF` colors render verbatim; `TF_CT_NONE`/`TF_CT_SYSCOLOR` fall back to the SPEC §21.2 palette; `bAttr` drives the kind. Per-atom cache.
- **Acceptance** (owner run): MS-IME shows the modern flavor; **ATOK shows the user's own 表示色カスタマイズ colors** (change a color in ATOK settings → PigComic follows); KO/ZH unchanged-or-better; smoke check extended with a decorated-layout build.

### MT.6 Hardening + default-on
- **Files**: store/manager fixes; `SmokeTest.cs`; `docs/IME_REPORT.md` (TSF gate section); default `tsf.enabled=true`.
- **Behavior**: survive the churn matrix: rapid focus switching between PartTextEditor/TextBox/other windows mid-composition, Esc/commit/Enter-guard interplay (D-32 holds under TSF), window close mid-composition, IME on/off toggling, language switching JA↔KO↔EN, Win+H voice typing inserts text (ACP store benefit), 8+ hour session stability.
- **Acceptance** (owner run): full §21.1 gate re-run under TSF with MS-IME JA, ATOK, MS KO, MS Pinyin — all PASS recorded in `docs/IME_REPORT.md`; any FAIL keeps default off and files the defect. Only then flip the default.

---

## External dependency (separate repo — not scheduled here)

**`.pcml` generator** ("PcmlGen"): planned and tracked in its own repo, https://github.com/pig1800/pcmlgenerator (local: `C:\PIG\src\pcmlgenerator` — see its `docs/SPEC.md` and `docs/PLAN.md`, milestones G0–G4). PigComic development does not depend on it — all fixtures come from `PcmlTestBuilder`. Format changes to SPEC §5 must be propagated to that repo's `docs/PCML_FORMAT.md` snapshot.
