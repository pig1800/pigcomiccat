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

**Symptom.** In `PartTextEditor` the JA IME did not show the current caret or the
henkan-ing word; ZH IME did not show the caret while editing Pinyin; KO appeared
fine only because 2-beolsik jamo composes a single glyph at the caret.

**Root cause.** `PartTextEditor` set `TextAlignment.Center` (and vertical center).
Avalonia 11.2.5's IME client renders the live composition by setting
`TextPresenter.PreeditText`/`PreeditTextCursorPosition`; the presenter inserts the
preedit at the caret into a combined layout (`GetCombinedText`) and moves the caret
to `CaretIndex + preeditCursorPos`. The caret geometry (`UpdateCaret` →
`GetDistanceFromCharacterHit`) is computed in the raw **left-origin** inline space,
but `RenderInternal` draws the combined text through `TextLayout.Draw`, which
applies `TextAlignment`. With `Center`, the composition is drawn at the centered
offset while the caret is drawn at the left-flow coordinate → the two separate and
the in-composition caret is lost (henkan word / Pinyin cursor invisible).

**Fix.** `PartTextEditor` no longer centers text during editing: it keeps
`TextAlignment.Left` and top/left content alignment while focused/composing, so the
native preedit pipeline renders the composition and its caret at the same position.
No custom `TextInputMethodClient` is used (per §21 the built-in client is correct
once alignment is left). The candidate-window `CursorRectangle` derives from the
same `_caretBounds`, so this also corrects candidate placement. **Item 6** above
validates this fix manually.

*Owner: replace ⬜ with ✅ PASS / ❌ FAIL and update Version/Result below when run.*