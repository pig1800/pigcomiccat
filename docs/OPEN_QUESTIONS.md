# PigComic — Open Questions

**None currently open.** The five questions from the planning session were answered by the owner on 2026-08-22 and folded into SPEC.md; the resolutions are recorded below for history (details in DECISIONS.md D-33…D-36 and the updated D-09/D-18).

| # | Question | Owner's resolution |
|---|---|---|
| 1 | Chapter-export source column header | Header follows the **actual source language** (`ja`→`日文`, etc.). Legacy `中文` fixed-header compatibility dropped — counting is built into PigComic. → SPEC §18, D-33. |
| 2 | TM multiplicity | **Per-speaker entries**, one target per (source, speaker), newest wins. No multiple-variants mode. → SPEC §7.1, D-09 confirmed. |
| 3 | Split-part default placement / text direction | Plain **LTR top-to-bottom** everywhere in the translation view; only Y drives ordering/placement logic (X recorded and drawn, but never consulted). RTL/vertical belongs solely to the deferred PSD export. → SPEC §15 Y-only rule, D-18 confirmed, D-36. |
| 4 | Journal granularity | **Confirm-only** — one flushed bubble snapshot per confirm, nothing else; keeps the app light and lag-free. → SPEC §23, D-34. |
| 5 | LLM QA configuration | Prompt is per-project editable; per-project **memory file** the LLM updates after each QA run; provider + model user-selectable, default **claude / claude-opus-5**. → SPEC §13, §25.1, D-35. |

If a future task hits something the spec doesn't answer, add the question here (numbered, with the affected milestone) and stop rather than guessing — per CLAUDE.md.
