# PigComic — IME Gate Report (M2.5)

**Gate rule (SPEC §21 / PLAN M2)**: M5–M11 editor work must NOT start until all 6
items below are recorded **PASS** on Windows with both Microsoft IME Japanese and
Korean, using the same control type as the target editor (`PartTextEditor`).

**Stack under test:** Avalonia 11.2.5, Windows.

## Spike findings (recorded 2026-08-22, before the manual run)

1. Avalonia 11.2.5 exposes **no public "composition active" flag**: verified by
   reflection over `TextInputMethodClient`, `TextBox`, `TextInputMethodImpl`,
   `ITextInputMethodRoot` and `TextInputMethodManager` — there is no
   composition/preedit state getter, only `SetPreeditText` and
   `SetClient`/`Reset` on the platform side.
2. Guard implementation (`Controls/PartTextEditor.cs`): on the Win32 backend an
   active TSF composition consumes Enter inside the IME — KeyDown(Enter) never
   reaches the control while composing, and committed text arrives via TextInput.
   `PartTextEditor` therefore treats an Enter as "confirm" only when it actually
   arrives as an unhandled raw key; its confirm count is exposed for manual
   verification. **If the checklist below shows a confirm firing during
   composition, STOP and follow §21's escalation (custom `TextInputMethodClient`),
   then a framework decision — do not proceed to M5.**

## Checklist

Run with **MS IME Japanese** (romaji: "nihongo" → にほんご → 日本語) then
**MS IME Korean** (2-beolsik: "gksrmf" → 한국어) in the IME test window
(Debug menu → "IME gate test"). The confirm log at the bottom of the window
must remain at 0 while composing.

| # | Item (SPEC §21) | MS IME JA | MS IME KO |
|---|---|---|---|
| 1 | Romaji/jamo composition visible at the caret (not a floating box); Space shows candidates; Enter commits; no dropped/duplicated chars at fast typing | ⬜ | ⬜ |
| 2 | Composition inside existing text (middle of string) inserts correctly | ⬜ | ⬜ |
| 3 | (KO) 2-beolsik jamo assembles syllables in place; committing/backspace behaves like Notepad | ⬜ | ⬜ |
| 4 | Shift+Enter during composition does not break composition state; Enter during composition never triggers a confirm (log stays 0) | ⬜ | ⬜ |
| 5 | IME on/off toggling (Alt+~ / Han-Eng) works while the control is focused | ⬜ | ⬜ |
| 6 | **Composition caret + inline preedit render together**: while typing, the underlined henkan word (JA) / Pinyin string (ZH) is drawn inline at the caret AND a caret is visible inside it. Verify in `PartTextEditor`; compare against the centered reference `TextBox` shown beside it. | ⬜ | ⬜ |

**Result:** 0/6 PASS — **do not start M5** until all six are PASS (escalate per §21).

## Composition-rendering defect (recorded 2026-08-23, owner-reported)

**Symptom.** In `PartTextEditor` — and confirmed reproducible in a plain left-aligned
`TextBox` (the New Project title field) — the JA IME did not show the current caret or
the henkan-ing word, and the ZH IME did not show the caret while editing Pinyin. KO
appeared fine only because 2-beolsik jamo composes a single glyph at the caret with the
cursor always at the end, masking the defect.

**Root cause (corrected).** The earlier alignment hypothesis was wrong. The real cause is
that Avalonia 11.x's Win32 **IMM32** path (`Imm32InputMethod.HandleComposition`) reads only
`GCS_COMPSTR` and calls `Client.SetPreeditText(composition)` with **no caret position and no
conversion-clause data** — it never forwards `GCS_CURSORPOS`, `GCS_COMPCLAUSE`, or
`GCS_COMPATTR`. (Verified in both 11.2.5 and the latest 11.3.20; the cursor-position fix
PR #21632 and the clause-highlight PRs #21647/#21648 exist only on unreleased `master`.)
Consequence: the in-composition caret defaults to end-of-string (ZH Pinyin caret lost) and
no active-segment highlight can be drawn (JA henkan word not shown).

**Fix (SPEC §21 escalation, D-40).** A custom `TextInputMethodClient`
(`Ime/ImeTextBoxInputMethodClient`) that on `SetPreeditText` queries the focused HWND's IMM
context directly (`ImmGetCompositionString` with `GCS_CURSORPOS` / `GCS_COMPCLAUSE` /
`GCS_COMPATTR`) and feeds a clause-aware `Ime/ImeTextPresenter`:
- the in-composition caret is placed from GCS_CURSORPOS (fixes ZH Pinyin caret);
- the active henkan clause (ATTR_TARGET_CONVERTED / _NOTCONVERTED) is highlighted
  reverse-video, other clauses underlined (fixes JA henkan highlight);
- KO / no-clause IMEs degrade to the base flat-underline rendering;
- the committed document text is never mutated by composition.

`PartTextEditor` installs this client on the Tunnel route (handledEventsToo, marks Handled)
so the base TextBox bubble class-handler is skipped, and uses `Ime/PartTextEditorTheme.axaml`
whose `PART_TextPresenter` is an `ImeTextPresenter`. **Items 1–6** above are run against this
build.

*Owner: replace ⬜ with ✅ PASS / ❌ FAIL and update Version/Result below when run.*