# IME Composition Rendering — Handoff Status

**Date:** 2026-08-23 · **Repo:** `C:\PIG\src\pigcomic` · **Branch:** main · **HEAD:** `a2e0a4e`

> This document is a handoff brief for the next model/session. It records the defect,
> the two fixes attempted, the current (partially-fixed) state, the likely remaining
> bug with an exact code location, and the environment pitfalls encountered.

---

## 1. TL;DR — Current Situation

- **Original defect** (owner-reported): JA IME shows no in-composition caret and no
  henkan-segment highlight; ZH IME shows no caret while editing Pinyin; KO appears fine.
- **Fix attempt #2 (custom IME client, commit `a2e0a4e`) is partially working:**
  - ✅ The caret now **renders** (proves the custom client + presenter pipeline is active).
  - ❌ The caret is **stuck at position 0** of the preedit.
  - ❌ The JA **henkan highlight still does not appear**.
- **The caret-at-0 bug is identified with high confidence** — `GCS_CURSORPOS` is read
  with buffer semantics, but IMM32 returns it as the function's **return value**, not
  via a buffer. See §6.1. The highlight failure is not yet diagnosed — see §6.2.
- **Tests/build green**: `dotnet build PigComic.sln` + `dotnet test` → 184/184 pass.
- **The M2 IME gate is still NOT passed** — M5+ editor work remains blocked until
  `docs/IME_REPORT.md` records 6/6 PASS (owner manual test on Windows with MS IME).

## 2. Why this matters (project context)

- PigComic is a comic/manga translation CAT tool (.NET 8, Avalonia 11.2.5, SQLite,
  SkiaSharp). `docs/SPEC.md` is the single source of truth; work follows `docs/PLAN.md`.
- **Gate rule**: M5–M11 may NOT start until `docs/IME_REPORT.md` records all 6
  checklist items PASS (SPEC §21). This IME defect blocks the gate.
- Completed milestones: M0–M4. Relevant commits:
  - `383eb53` — fix attempt #1 (alignment, D-39) — **insufficient, superseded**
  - `a2e0a4e` — fix attempt #2 (custom client, D-40) — **current, partially working**

## 3. Original defect & confirmed root cause

**Symptom**: In `PartTextEditor` (and reproducible in any plain left-aligned `TextBox`,
e.g. the New Project title field):
- JA (MS IME): no caret inside the composition, no highlight of the active
  conversion ("henkan") segment.
- ZH (Pinyin): no caret visible while editing the Pinyin composition.
- KO (2-beolsik): fine — but only because jamo composes one glyph at a time with the
  cursor always at the end, which masks the defect.

**Root cause (verified from Avalonia source, D-40 in `docs/DECISIONS.md`)**:
Avalonia 11.x's Win32 input path is **IMM32** (`Imm32InputMethod.HandleComposition`).
It reads only `GCS_COMPSTR` and calls `Client.SetPreeditText(text)` — it **never reads
or forwards**:
- `GCS_CURSORPOS` — caret position inside the composition,
- `GCS_COMPCLAUSE` / `GCS_COMPATTR` — conversion-clause boundaries + per-char attributes
  (which segment is being converted).

Checked both 11.2.5 and 11.3.20 — identical. Upstream fix PRs exist only on unreleased
`master` (#21632 cursor position; #21647/#21648 clause rendering). **Upgrading Avalonia
does not fix this.**

## 4. Fix attempt #1 — alignment (D-39, commit `383eb53`) — FAILED

- Hypothesis: `PartTextEditor` used `TextAlignment.Center`; the composition is drawn
  centered while the caret geometry is computed in left-origin space → they desync.
- Fix: keep left/top alignment. Committed; owner retested.
- **Result: defect still reproduces** (even in a plain never-centered TextBox) —
  hypothesis wrong. D-39 rewritten to record this. Left alignment retained (harmless).

## 5. Fix attempt #2 — custom IME client (D-40, commit `a2e0a4e`) — CURRENT

SPEC §21 escalation: a custom `TextInputMethodClient` that queries IMM32 directly for
the missing data + a clause-aware presenter. Composition never mutates document text.

**Files (all under `src/PigComic.App/`):**

| File | Role |
|---|---|
| `Ime/ImeComposition.cs` | `ImeComposition` model (Text, CursorPosition, ClauseBoundaries, Attributes, `ActiveClause` span picker) + `Imm32Native` P/Invoke (`ImmGetContext`/`ImmGetCompositionStringW`/`ImmReleaseContext`). |
| `Ime/ImeTextBoxInputMethodClient.cs` | Custom `TextInputMethodClient`; mirrors the built-in TextBox client (surrounding text, cursor rect, selection, context menu) and in `SetPreeditText` enriches the preedit via `Imm32Native.GetComposition(hwnd)`. Falls back to plain rendering on any failure. |
| `Ime/ImeTextPresenter.cs` | `TextPresenter` subclass; `Composition` property feeds `PreeditText`/`PreeditTextCursorPosition`; `CreateTextLayout` override underlines the whole preedit and draws the active henkan clause reverse-video (`ActiveClauseBackground`/`Foreground` styled props). |
| `Ime/PartTextEditorTheme.axaml` | `ControlTheme` cloned from the Fluent TextBox template with `PART_TextPresenter` = `ImeTextPresenter`; merged in `App.axaml`. |
| `Controls/PartTextEditor.cs` | Installs the client on the **Tunnel** route (`handledEventsToo: true`, `e.Handled = true`) so the base TextBox bubble class-handler is skipped; applies the theme via `TryFindResource`; wires presenter in `OnApplyTemplate`/`OnGotFocus`/`OnLostFocus`; has `ConfirmOnEnter` (default true) — dialogs set it `False` so Enter still triggers default buttons. |
| `Views/CreateProjectDialog.axaml` | Title + Folder boxes converted to `PartTextEditor` so the owner can test the fix there. |

**Tests**: `tests/PigComic.Core.Tests/ImeCompositionTests.cs` — 7 pure-logic tests
(clause mapping, caret clamping) — all pass. No UI-thread tests (see §8).

## 6. Current symptom analysis

Owner retest after `a2e0a4e`: **caret shows but fixed at position 0; no henkan highlight.**

### 6.1 Caret stuck at 0 — bug identified (high confidence)

`Imm32Native.GetComposition` (`Ime/ImeComposition.cs`, lines ~169–175):

```csharp
var cursor = 0;
var cursorBytes = ReadBytes(himc, GCS_CURSORPOS);   // ← WRONG
if (cursorBytes is { Length: >= 4 })
{
    cursor = BitConverter.ToInt32(cursorBytes, 0);
}
```

**`GCS_CURSORPOS` (and `GCS_DELTASTART`) are NOT buffer reads.** Per IMM32 semantics,
`ImmGetCompositionString(himc, GCS_CURSORPOS, NULL, 0)` returns the cursor position
**directly as its return value**. Established idiom (used by SDL, WINE, etc.):

```c
DWORD cursor = ImmGetCompositionString(hIMC, GCS_CURSORPOS, NULL, 0);
```

What the current code does instead: treats the returned position as a byte *count*:
- cursor = 0 → `len <= 0` → null → cursor stays **0**;
- cursor = N>0 → allocates N bytes, calls again (returns N again; nothing copied),
  buffer stays all zeros → either `Length < 4` → 0, or `BitConverter` over zeros → **0**.

Either way the cursor is always 0 → caret pinned at preedit start. **Matches the symptom
exactly.**

**Fix**: read the return value directly (guard negative → 0). `ImmGetCompositionString`
is currently `private` in `Imm32Native` — expose an internal method, e.g.:

```csharp
var cursor = ImmGetCompositionString(himc, GCS_CURSORPOS, IntPtr.Zero, 0);
if (cursor < 0) cursor = 0; // IMM_ERROR_NODATA(-1)/IMM_ERROR_GENERAL(-2)
```

### 6.2 No henkan highlight — NOT yet diagnosed

The clause path (`GCS_COMPCLAUSE`/`GCS_COMPATTR`) *is* buffer-based and the code shape
looks right, so possibilities:

1. **Reads fail at runtime** (`IMM_ERROR_NODATA`) → `clause`/`attr` null →
   `ActiveClause` null → base flat-underline rendering. Needs runtime evidence.
2. **Reads succeed but attrs are `ATTR_INPUT`** — before pressing Space (conversion),
   unconverted kana are input-only, so *by design* nothing highlights. If the owner
   only typed without converting, this is expected; if they pressed Space and still
   saw no highlight, the reads are failing.
3. **MS IME (Win10/11) IMM32-compat caveat**: the new MS Japanese IME is TSF-based
   behind an IMM32 shim. The shim generally does provide clause data (IMM32 apps like
   Notepad2 show the highlight), but this is a residual risk if reads prove empty.

**Strongly recommended next step**: add temporary diagnostics so the owner can capture
one real JA henkan session, e.g. log in `ImeTextBoxInputMethodClient.SetPreeditText`:
`compstr`, raw cursor return value, clause byte length + values, attr length + values
(write to a file or a debug text box in `ImeTestWindow`). That single capture will
disambiguate hypotheses 1/2/3.

### 6.3 What is already proven by the retest

- The tunnel-route client installation works (behavior changed vs. built-in).
- `GetComposition` returns non-null and its `Text` matches Avalonia's preedit string
  (otherwise the fallback path would put the caret at preedit end, not 0).
- The `ImeTextPresenter` theme is applied (the rich `Composition` path is taken).
- So the remaining work is localized to `Imm32Native` reads + possibly presenter styling.

## 7. Suggested next steps (in order)

1. Fix the `GCS_CURSORPOS` read per §6.1 (trivial).
2. Add temporary diagnostics per §6.2; have the owner run one JA session
   (type kana → Space to convert) and one ZH Pinyin session; capture values.
3. If clause/attr data is present → check `ImeComposition.ActiveClause` mapping and the
   `ImeTextPresenter.CreateTextLayout` override spans (offsets are relative to the
   *combined* text: `caretIndex + clauseStart`).
4. If clause/attr data is absent from the IMM32 shim → escalate: consider TSF-based
   integration (large) or an alternate approach; record in `docs/OPEN_QUESTIONS.md`.
5. Owner re-runs the 6-item gate in `docs/IME_REPORT.md`; only then un-block M5.
6. Update `docs/DECISIONS.md` (D-40 outcome) and `docs/IME_REPORT.md` with results.

## 8. Environment pitfalls (hard-won this session)

- **PowerShell corrupts non-ASCII .cs files** — it mangled Japanese test strings once.
  Use only read/write/edit tools for files containing CJK; never `Set-Content`.
- **Do NOT write xunit tests that instantiate Avalonia controls** — the headless
  dispatcher + xunit worker threads deadlock ("Call from invalid thread" / hangs).
  Two attempts (`ImeTextPresenterTests`, `ImeEditorWiringTests`, plus `HeadlessApp`
  fixture and the `Avalonia.Headless` package) were **removed**; only pure-logic tests
  remain. UI behavior is verified by the owner's manual IME gate instead.
- The IME fix **cannot be verified by the AI** — it needs a real Windows IME session;
  only the owner can confirm. `ImeTestWindow` (menu/debug entry) has the 6-checkbox gate
  UI plus a deliberately plain centered TextBox for comparison.
- `Application.Current.FindResource` throws on missing key — use `TryFindResource`.
- `TextBoxTextInputMethodClient`, `TextPresenter.GetCombinedText`/`CreateTextLayoutInternal`,
  `StringBuilderCache` are internal/private — the custom client/presenter re-implement
  them by hand (see code comments).
- Commit `2ae5856` (an earlier abandoned client attempt) was dropped via
  `git reset --mixed HEAD~1`; do not resurrect it.

## 9. Verify

```
dotnet build PigComic.sln
dotnet test                          # expect 184/184 pass
dotnet run --project src/PigComic.App
```
