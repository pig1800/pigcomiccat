# PigComic — IME Gate Report (M2.5)

**Gate rule (SPEC §21 / PLAN M2)**: M5–M11 editor work must NOT start until all 5
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

**Result:** 0/5 PASS — **do not start M5** until all five are PASS (escalate per §21).

*Owner: replace ⬜ with ✅ PASS / ❌ FAIL and update Version/Result below when run.*