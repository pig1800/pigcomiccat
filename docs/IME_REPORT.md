# PigComic — IME Gate Report (M2.5)

**Gate rule (SPEC §21 / PLAN M2)**: M5–M11 editor work must NOT start until all 6
items below are recorded **PASS** on Windows, using the same control type as the target
editor (`PartTextEditor`). Item 6 additionally has a per-IME table covering **MS-IME
Japanese, ATOK, MS Pinyin and MS-IME Korean** (the 2026-08-23 defect behaved differently
per IME, so one combined verdict would hide it).

**Stack under test:** Avalonia 12.1.1, Windows. (Upgraded from 11.2.5 on 2026-08-23 — D-41.)

> **Current state (2026-08-24):** the gate is **OPEN — 0/6 recorded**, and it is the only
> thing blocking M5. The code side is finished and verified (build clean, 213/213 tests,
> `--smoke` 16/16). The owner has reported informally that the target editor and the modern
> composition rendering work as expected in normal use, but that is **not** a substitute for
> the run below: the checkboxes and the diagnostics tables must be filled from real IME
> sessions. Nobody but the owner can produce them — an AI session must not tick them.

## Spike findings (recorded 2026-08-22, before the manual run)

1. Avalonia 11.2.5/12.1.1 expose **no public "composition active" flag**: verified by
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
| 6 | **Modern composition rendering** (SPEC §21.2): composition text is drawn **coloured** (not underlined) with the caret visible inside it; after Space, the active henkan clause shows a **coloured background** that moves with ←/→. Run with **MS-IME JA and ATOK** (extra column below). | ⬜ | ⬜ |

**Result:** 0/6 PASS — **do not start M5** until all six are PASS (escalate per §21).

### Item 6 — per-IME record (added 2026-08-24 with PLAN M2.6/M2.7)

Item 6 is the one the 2026-08-23 retest failed: the caret worked (CN confirmed by the owner)
but ATOK showed no henkan clause at all. The fix moved clause capture into the composition
message; record each IME separately since the whole point is that they behaved differently.

| IME | Coloured composition text | Moving target-clause background | Caret inside preedit | Notes |
|---|---|---|---|---|
| MS-IME Japanese | ⬜ | ⬜ | ⬜ | |
| ATOK (Passport) | ⬜ | ⬜ | ⬜ | |
| MS Pinyin (ZH) | ⬜ | n/a (no clauses pre-conversion) | ⬜ | **regression watch**: this caret worked before M2.6 and must still work |
| MS-IME Korean | ⬜ | n/a (jamo) | ⬜ | |

### M2.6 diagnostics capture (owner run)

Debug menu → "IME gate test" → tick **"Log every composition message to a file"**, compose
one JA session per IME (kana → Space → ←/→ → Enter), then press **Summarise log**. Paste the
summary line here per IME. Non-zero clause/attr byte counts prove IMM32 is delivering henkan
data in-message; zero for ATOK after a real conversion means IMM32 is dead for it and M-TSF
must be re-prioritised (`docs/IME_MODERN_COMPOSITION.md` §5).

| IME | Messages | Clause bytes (max) | Attr bytes (max) | Verdict |
|---|---|---|---|---|
| MS-IME Japanese | | | | |
| ATOK | | | | |

If composition itself misbehaves at any point, untick **"Capture clause data in-message"**
(or set `PIGCOMIC_IME_NO_HOOK=1`) and retry: that isolates the new hook as the cause.

## Composition-rendering defect (recorded 2026-08-23, owner-reported)

**Symptom.** In `PartTextEditor` — and confirmed reproducible in a plain left-aligned
`TextBox` (the New Project title field) — the JA IME did not show the current caret or
the henkan-ing word, and the ZH IME did not show the caret while editing Pinyin. KO
appeared fine only because 2-beolsik jamo composes a single glyph at the caret with the
cursor always at the end, masking the defect.

**Root cause (corrected).** The earlier alignment hypothesis was wrong. The real cause was
that Avalonia 11.x's Win32 **IMM32** path (`Imm32InputMethod.HandleComposition`) read only
`GCS_COMPSTR` and called `Client.SetPreeditText(composition)` with **no caret position and no
conversion-clause data** — never forwarding `GCS_CURSORPOS`, `GCS_COMPCLAUSE`, or
`GCS_COMPATTR`. Consequence: the in-composition caret defaulted to end-of-string (ZH Pinyin
caret lost) and no active-segment highlight could be drawn (JA henkan word not shown).

**Resolution (two parts, D-40 + D-41).**

1. **Caret — solved by upgrading to Avalonia 12.1.1** (2026-08-23). Upstream PR #21632 made
   the Win32 backend read `GCS_CURSORPOS` and pass it to `SetPreeditText(text, cursorPos)`;
   it shipped in 12.1.0 and was never backported to 11.x. PigComic no longer reads that flag
   itself — which also deleted an earlier caret-stuck-at-0 bug in our own IMM32 code.
2. **Henkan highlight — still PigComic's own code**, because no Avalonia release forwards
   clause data (upstream issue #21647 open; prototype PR #21648 unmerged). Superseded on
   2026-08-24 by PLAN M2.6/M2.7: `Ime/ImeMessageMonitor` now captures
   `GCS_COMPCLAUSE`/`GCS_COMPATTR` **inside the WM_IME_COMPOSITION message** (reading them
   later was why ATOK showed nothing), and `Ime/ImeTextPresenter` renders the result in the
   modern flavor (SPEC §21.2):
   - composition text is drawn in colour, not underlined;
   - the active henkan clause (ATTR_TARGET_CONVERTED / _NOTCONVERTED) gets a coloured
     background that follows the segment as ←/→ move it;
   - KO / ZH / no-clause IMEs degrade to the whole preedit styled as plain input, with the
     caret — supplied by Avalonia — unaffected;
   - the committed document text is never mutated by composition.

`PartTextEditor` installs this client on the Tunnel route (handledEventsToo, marks Handled)
so the base TextBox bubble class-handler is skipped, and uses `Ime/PartTextEditorTheme.axaml`
whose `PART_TextPresenter` is an `ImeTextPresenter`. That wiring is verified automatically by
`PigComic.App.exe --smoke` (D-42); **items 1–6 above still require this manual run.**

*Owner: replace ⬜ with ✅ PASS / ❌ FAIL and update Version/Result below when run.*