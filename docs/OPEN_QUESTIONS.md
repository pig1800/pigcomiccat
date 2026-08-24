# PigComic — Open Questions

## Q7 — M-TSF scheduling (OPEN, decision point after PLAN M2.6's diagnostics)

The modern-flavor IME plan (`docs/IME_MODERN_COMPOSITION.md`, D-43) fixes clause capture over IMM32 first (M2.6/M2.7) and schedules the full TSF text store (M-TSF, ≈3–5 weeks) after M6. Owner decides once M2.6's diagnostics log is in hand:
- ATOK delivers clause data in-message → keep M-TSF after M6 (recommended), gate passes on the IMM32 path;
- ATOK delivers nothing even in-message → M-TSF jumps ahead of M5, since the gate's item 6 cannot pass for ATOK otherwise.

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
