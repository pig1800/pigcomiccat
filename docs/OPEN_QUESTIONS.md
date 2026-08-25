# PigComic — Open Questions

**None currently open.**

## Q8 — Should dragging a marker reorder the reading order? — RESOLVED 2026-08-25

The owner wants **drag-to-reorder on the main cross only**: when a bubble's source-marker Y
crosses a neighbour's, the chapter's reading order is renumbered (D-17) and the segment list
rebuilds. **Part (sub) markers never affect order** — they influence only PSD export (§27.2).
Implemented as `BubbleMutations.RenumberByMarkerY(doc)`, called after a source-marker drag
commit (D-57). Core tests + a smoke check (main-cross drag reorders; part-cross drag doesn't)
guard it.

**Previously open / resolved:** Q7 (M-TSF scheduling, resolved 2026-08-24) below; Q1–Q6 resolved 2026-08-22/23.

## Q7 — M-TSF scheduling — RESOLVED 2026-08-24

The modern-flavor IME plan (`docs/IME_MODERN_COMPOSITION.md`, D-43) fixed clause capture over IMM32 first (M2.6/M2.7) and scheduled the full TSF text store (M-TSF, ≈3–5 weeks) after M6, with the caveat that it would jump ahead of M5 if ATOK turned out to deliver no clause data even in-message.
**Outcome: the first branch.** The M2.6 diagnostics captured real henkan state over IMM32 (373 composition messages, multi-clause, TARGET_CONVERTED/CONVERTED attributes), and the owner's gate run confirmed ATOK renders the moving clause highlight correctly. The IMM32 path carries JA and ATOK today, so **M-TSF stays scheduled after M6** and does not jump ahead of M5.

Its remaining value is unchanged and still worth doing later: per-IME colour fidelity (ATOK users' own 表示色カスタマイズ palette), exact Notepad parity, and the groundwork for reconversion.

---

**Previously resolved (Q1–Q6).**

## Q6 — Avalonia version strategy for the IME gate — RESOLVED 2026-08-23

Owner chose **option B: upgrade to Avalonia 12.1.1** ("the app isn't finished yet, any overhaul can be accepted"). Done — see D-41 for the full consequence list and `docs/IME_HANDOFF.md` §3.1 for the upstream evidence. In short: the caret is now Avalonia's job (upstream PR #21632, released in 12.1.0), and PigComic's custom IME client is reduced to the one thing no Avalonia release provides — henkan conversion-clause highlighting (upstream issue #21647 still open).

The only remaining IME work is the **owner-run manual gate** (SPEC §21.1, PLAN M2.5), which still blocks M5.

---

**Previously resolved.** The five questions from the planning session were answered by the owner on 2026-08-22 and folded into SPEC.md; the resolutions are recorded below for history (details in DECISIONS.md D-33…D-36 and the updated D-09/D-18).

| # | Question | Owner's resolution |
|---|---|---|
| 1 | Chapter-export source column header | Header follows the **actual source language** (`ja`→`日文`, etc.). Legacy `中文` fixed-header compatibility dropped — counting is built into PigComic. → SPEC §18, D-33. |
| 2 | TM multiplicity | **Per-speaker entries**, one target per (source, speaker), newest wins. No multiple-variants mode. → SPEC §7.1, D-09 confirmed. |
| 3 | Split-part default placement / text direction | Plain **LTR top-to-bottom** everywhere in the translation view; only Y drives ordering/placement logic (X recorded and drawn, but never consulted). RTL/vertical belongs solely to the deferred PSD export. → SPEC §15 Y-only rule, D-18 confirmed, D-36. |
| 4 | Journal granularity | **Confirm-only** — one flushed bubble snapshot per confirm, nothing else; keeps the app light and lag-free. → SPEC §23, D-34. |
| 5 | LLM QA configuration | Prompt is per-project editable; per-project **memory file** the LLM updates after each QA run; provider + model user-selectable, default **claude / claude-opus-5**. → SPEC §13, §25.1, D-35. |

If a future task hits something the spec doesn't answer, add the question here (numbered, with the affected milestone) and stop rather than guessing — per CLAUDE.md.
