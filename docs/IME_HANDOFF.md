# IME Composition Rendering — Status

> **2026-08-24:** PLAN M2.6 + M2.7 are implemented — clause data is now captured *inside* the
> composition message by `Ime/ImeMessageMonitor`, and the preedit renders in the modern
> flavor (coloured text + coloured active-clause background) instead of reverse-video. The
> investigation and the remaining TSF track live in **`docs/IME_MODERN_COMPOSITION.md`**
> (D-43/D-44). §4 below reflects the new code; §5 is the do-not-reintroduce list.
> What still blocks M5 is the **owner's manual gate run** (`docs/IME_REPORT.md`).

**Last updated:** 2026-08-24 (PLAN M2.6 + M2.7) · **Repo:** `C:\PIG\src\pigcomic`

Read this before touching anything under `src/PigComic.App/Ime/`. It records what the
defect was, who is responsible for which piece today, and the environment traps that have
already cost two sessions.

---

## 1. TL;DR

- **Original defect** (owner-reported 2026-08-23): JA IME showed no in-composition caret and
  no henkan-segment highlight; ZH Pinyin showed no caret; KO appeared fine.
- **Root cause**: Avalonia's Win32 IMM32 path forwarded only the raw composition string.
- **Now**: the project runs **Avalonia 12.1.1**, where the caret half is fixed upstream. The
  custom IME client survives only to add **conversion-clause highlighting**, which no
  Avalonia release provides.
- **Build/tests/smoke**: green — 213/213 tests, `--smoke` 17/17.
- **The M2.5 IME gate is still NOT passed.** It needs a manual run with real MS IME sessions
  (SPEC §21.1); only the owner can do it. **M5+ stays blocked until `docs/IME_REPORT.md`
  records 6/6 PASS.**

## 2. Who provides what (the important table)

| Data | Provider | Detail |
|---|---|---|
| Preedit string (`GCS_COMPSTR`) | Avalonia | `TextInputMethodClient.SetPreeditText` |
| In-composition caret (`GCS_CURSORPOS`) | **Avalonia 12.1.0+** | the `cursorPos` parameter. **Never read this from IMM32.** |
| Clause boundaries + attributes (`GCS_COMPCLAUSE`/`GCS_COMPATTR`) | **PigComic** | `Ime/ImeMessageMonitor`, read **inside** WM_IME_COMPOSITION, Windows-guarded |

## 3. Upstream status — verified 2026-08-23

Avalonia's default branch is **`main`** (there is no `master`; raw URLs against it 404).

- **Caret position — FIXED AND RELEASED.** PR
  [#21632](https://github.com/AvaloniaUI/Avalonia/pull/21632) "Respect IME preedit cursor
  position on Win32" merged 2026-06-24; first shipped in **12.1.0** (2026-07-09), also in
  12.1.1. **Never backported to 11.x** — absent from 11.2.5 and 11.3.20.
- **Henkan clause highlighting — NOT FIXED ANYWHERE.** Nothing on `main` reads
  `GCS_COMPCLAUSE` or `GCS_COMPATTR` (the constants exist in `UnmanagedMethods.cs` but are
  unused). It exists only in [#21648](https://github.com/AvaloniaUI/Avalonia/pull/21648), an
  unmerged prototype (CI failing, zero reviews, untouched since 2026-07-09), tracked by open
  issue [#21647](https://github.com/AvaloniaUI/Avalonia/issues/21647). A repo-wide search for
  `GCS_COMPATTR` across all issues and PRs returns 0 results.
- **API note (verified by reflection):** `SetPreeditText(string, int? cursorPos)` already
  existed in 11.2.5 — the overload is not new; only the Win32 wiring was missing. 12.1.1 has
  **no** clause/attribute-carrying overload and no `TextInputMethodPreeditSegment` type.

**Re-check before removing our custom client**: if #21648 ever merges and ships, most of
`src/PigComic.App/Ime/` can be deleted in favour of the stock presenter.

## 4. Current implementation

All under `src/PigComic.App/`:

| File | Role |
|---|---|
| `Ime/ImeMessageMonitor.cs` | **The only file that touches `imm32.dll`.** A `Win32Properties.AddWndProcHookCallback` hook (refcounted per window, observer-only — never sets `handled`) that reads `GCS_COMPCLAUSE`/`GCS_COMPATTR` synchronously inside `WM_IME_COMPOSITION`. Owns the diagnostics log and the `CaptureEnabled` kill switch (`PIGCOMIC_IME_NO_HOOK=1`). |
| `Ime/ImeCompositionSnapshot.cs` | Pure merge rules: which GCS fields lParam says to read (zero lParam = read everything), retention of unread fields across messages, and clause-array normalisation (byte→char offsets, monotonicity, missing start/end). |
| `Ime/ImeSegment.cs` | `ImeSegmentKind` (`Input/Converted/ConvertedTarget/TargetNotConverted/InputError`, named to match upstream `TextInputDecorationKind`) and the builder that turns attribute runs into segments, split at clause edges, always tiling the preedit exactly. |
| `Ime/ImeComposition.cs` | Model: text, caret, clause boundaries, attributes; exposes `Segments` and `ActiveClause`. No interop. |
| `Ime/ImeTextBoxInputMethodClient.cs` | Custom `TextInputMethodClient`; mirrors the built-in TextBox client (surrounding text, cursor rect, selection, context menu). `SetPreeditText(text, cursorPos)` takes the caret from Avalonia and merges in the captured snapshot, dropping clause data when the snapshot's text has drifted from the preedit. Makes **no** IMM32 calls. |
| `Ime/ImeTextPresenter.cs` | `TextPresenter` subclass; renders each segment in the modern flavor — coloured text, coloured background on the active clause, dashed underline for input errors — from four theme-bound brushes. |
| `Ime/PartTextEditorTheme.axaml` | `ControlTheme` cloned from the Fluent TextBox template with `PART_TextPresenter` = `ImeTextPresenter`; merged in `App.axaml`. **This clone is the fragile part across Avalonia upgrades** — hence `--smoke`. |
| `Controls/PartTextEditor.cs` | Installs the client on the Tunnel route (`handledEventsToo: true`) so the base TextBox bubble handler is skipped; applies the theme via `TryFindResource`; owns the Enter/confirm guard (D-32) and `ConfirmOnEnter` (dialogs set it False so Enter still reaches a default button). |

**Tests**: `ImeCompositionTests.cs` (7 — clause mapping, caret clamping), `ImeSnapshotTests.cs`
(12 — lParam gating, cross-message retention, clause normalisation, a full JA conversion
sequence), `ImeSegmentTests.cs` (17 — attribute→kind mapping, run grouping, clause-split
edges, degrade paths). UI behaviour is covered by `--smoke` (17 checks) and the manual gate.

## 5. Bugs already fixed — do not reintroduce

0. **Never pass a finite height to the composition TextLayout.** `ImeTextPresenter` clones
   `TextPresenter.CreateTextLayoutInternal`; the base measures with
   `(width, PositiveInfinity)`. Passing `Bounds.Height` makes `TextLayout` silently drop
   lines that do not fit — the multi-line editor's last line went blank, its text vanished
   as you typed, the caret stranded on the line above and the candidate window followed it
   down. `--smoke` reproduces this exact case (D-46).
0b. **The client must raise `CursorRectangleChanged`.** Avalonia re-reads `CursorRectangle`
   only when told; without subscribing to `TextPresenter.CaretBoundsChanged` the candidate
   window is placed once and then stays put while the caret moves away (D-46).

1. **Caret stuck at position 0.** The old client read `GCS_CURSORPOS` through `ReadBytes`,
   i.e. with buffer semantics. `ImmGetCompositionString(hIMC, GCS_CURSORPOS, NULL, 0)`
   returns the cursor position **as its return value**; treating that as a byte count always
   yielded 0. Fixed structurally by the 12.1.1 upgrade: Avalonia supplies the caret and we no
   longer read that flag at all.
2. **`TextAlignment.Center` blamed for the defect** (D-39). It was not the cause — the defect
   reproduced in a plain left-aligned TextBox. Left/top alignment is retained because it keeps
   the caret in-flow, not as a fix.
3. **Missing Windows guard.** A previous session deleted `!OperatingSystem.IsWindows()` from
   the IMM32 entry point. The App targets plain `net8.0`; without the guard, `imm32.dll` throws
   `DllNotFoundException` on Linux/macOS, and nothing warns at build time.
4. **Reading IMM32 outside the composition message.** The clause reads used to happen in
   `SetPreeditText`, i.e. after Avalonia's own `ImmGetContext`/`ImmReleaseContext` cycle for
   that message. MSDN: the IMM removes composition information once the context is released.
   MS-IME tolerated it; **ATOK returned nothing, which is why no henkan highlight appeared**.
   All IMM32 access now lives in `ImeMessageMonitor` and runs on the message stack — do not
   add an `imm32.dll` import anywhere else.

## 6. Environment pitfalls (hard-won)

- **Do NOT write xunit tests that instantiate Avalonia controls.** The headless dispatcher
  and xunit worker threads deadlock ("Call from invalid thread" / hangs). The
  `Avalonia.Headless` package and a `HeadlessApp` fixture were tried and removed **twice**;
  the second time by a session that also broke unrelated things. Use `--smoke` instead.
- **PowerShell corrupts non-ASCII `.cs` files** — it mangled Japanese test strings once. Use
  the Read/Write/Edit tools for any file containing CJK; never `Set-Content`.
- **The IME fix cannot be verified by an AI.** It needs a real Windows IME session; only the
  owner can confirm. `ImeTestWindow` (Debug menu → "IME gate test") hosts the 6-item
  checklist and a confirm counter that must stay 0 while composing.
- `Application.Current.FindResource` throws on a missing key — use `TryFindResource`.
- `TextBoxTextInputMethodClient`, `TextPresenter.GetCombinedText` /
  `CreateTextLayoutInternal`, and `StringBuilderCache` are internal/private in Avalonia; the
  custom client and presenter re-implement them by hand (see code comments).

## 7. Verify

```
dotnet build PigComic.sln
dotnet test                                                   # expect 213/213
src/PigComic.App/bin/Debug/net8.0/PigComic.App.exe --smoke     # expect 17/17, exit 0
dotnet run --project src/PigComic.App                          # Debug menu → IME gate test
```
