# Task: produce a detailed implementation plan for PigComic

Do NOT write application code in this session. Produce planning documents only:

- `docs/SPEC.md` — the full specification derived from the requirements below (data model, package format, TM algorithm, UI behavior, keyboard bindings, file layouts)
- `docs/PLAN.md` — milestone-by-milestone task list. Each task must be small enough for a weaker coding model to complete in one session without design judgment: state the files to touch, the exact behavior, and a concrete acceptance check (a test to pass or a manual step with expected result). Put the test tables in the spec, not in prose.
- `CLAUDE.md` — short: stack, conventions, hard rules, "read docs/SPEC.md first"
- `docs/DECISIONS.md` — record every design choice you make that is not dictated below, with one-line rationale
- `docs/OPEN_QUESTIONS.md` — anything you needed to decide but feel the owner should confirm

Where requirements are silent, decide conservatively, follow Trados/memoQ conventions, and log the decision. Ask at most 5 questions at the end; otherwise proceed.

---

## What this is

PigComic: a desktop CAT tool specialized for comic/manga translation, modeled on Trados/memoQ project management. Single user, Windows first, cross-platform kept possible.

Stack: .NET 8, Avalonia 11, MVVM (CommunityToolkit.Mvvm), SQLite for TM/TB, xunit. Solution split: `PigComic.Core` (domain, package I/O, TM/TB, QA — no Avalonia references), `PigComic.App` (Avalonia UI). Every Core feature ships with tests.

External code that already exists and must be referenced, not reimplemented:
- **PigTranslate** (library): used ONLY as the LLM request client (`ILlmClient`-style). Not deeply integrated.
- **OCR/PSD→XLSX tool** (existing, business-proven): reused for XLSX export. Also the basis for the `.pcml` generator, which is a separate program outside this repo.
- **Character count tool** (existing): reused for billing/progress counts.

Plan these as thin adapter interfaces in Core with stubs; the owner will wire the real implementations.

## Package format: `.pcml` (Pig Comic Markup Language)

- One `.pcml` = one chapter = one job. A zip file:
  - `/media/` — page images, JPG and PNG both valid
  - `content.xml` — everything else
- `content.xml` is the **round-trip storage**: source text AND translation AND bubble regions AND statuses all live here. The CAT reads and writes it. Writes go to a temp file then atomic rename; never leave a half-written zip.
- Generated initially by an external program; the CAT then owns it.
- content.xml must contain:
  - schema version
  - comic title, chapter number, source language, target language (exactly one target per package)
  - ordered page list (filenames in `/media`); order is explicit, not directory order. Each page records original width/height before any downscaling (needed for later PSD export).
  - per-chapter character name list (names that appear in this chapter)
  - bubbles: stable ID (generator should emit deterministic IDs such as page index + bubble index; the CAT never renumbers), page ref, region in image pixel coordinates, kind (Speech/Thought/Narration/SFX/Sign/Note), speaking character (nullable), reading-order index, source text, target text, status, notes
  - a target bubble may be **split into at most 3 sub-bubbles**; each split part has its own region. Source bubble is always exactly one region. The TM unit is the whole bubble (source ↔ concatenated target), never the split part.
- Define the XML schema precisely (element/attribute names, types, optionality) in SPEC.md with a complete example file. Design for forward compatibility (unknown elements preserved on round-trip).
- Page add/remove/reorder inside the CAT is a later milestone but the schema must not make it hard.

## Project model

- A project = one manga title. Created from the main view with: title, project folder path, language pair. Project folder contains `project.json`, `tm.db`, `tb.db`, and a master character list (with pasted images stored in the folder).
- `project.json` references `.pcml` files by path (files are not copied into the project). On open, if a referenced file is missing → relink dialog (like Trados); if the user cancels, stay on the project view, do not enter the editor.
- Main view: list of projects; open (by existing project.json), create, remove (choose: remove from list only / delete folder).
- Exactly one TM and one TB per project. No multiple/master TMs.
- Master character list entries: name (required), picture (pasteable from clipboard), gender, age, first-appearing chapter, self-referential pronoun, comments. Everything but name optional.
- Project-wide find/replace across all chapters. Progress/character-count stats per chapter and per project (via the existing count tool adapter).

## Editor layout

Three vertical areas, left to right:

1. **Image pane**: the current page image, tiled rendering (see Non-functional). Bubble regions from content.xml drawn as overlays. Clicking a region selects that bubble in the segment list and vice versa. Scroll/zoom; auto-scroll to the selected bubble.
2. **Segment list**: one row per bubble in reading order. Two columns:
   - Source column: shows kind, speaking character, and source text. Regions are **created, deleted, dragged/resized from this column's interaction with the image pane** (source is the only side whose geometry is user-editable this way).
   - Target column: editable text. Rendered center-aligned relative to its source bubble. User may split the target into up to 3 parts (each gets its own region, placed relative to the source region, adjustable). Editing behavior mirrors Trados: Enter confirms and moves to next; Shift+Enter inserts a line break within the part.
3. **Function pane** (context for the selected bubble):
   - One combined **TM/TB results box**, Trados/memoQ style: highest TM matches first, then TB hits. Not split into two boxes. Ctrl+1..9 inserts the Nth entry.
   - **Kind** selector.
   - **Character** box: a text field with autocomplete from the master character list. Every distinct character name used in the current chapter appears as a one-click button in this box; clicking sets the speaker of the selected bubble. A new name typed here is added to the chapter list (and offered to the master list).
   - Notes.
   - LLM QA button (see LLM).

Confirming a target (Enter / Ctrl+Enter) sets status Translated and writes the source→target pair to the project TM. Statuses: Untranslated, Draft, Translated, Reviewed, Locked. Define the full keyboard map in SPEC.md following Trados defaults where applicable.

## Repetition handling (intentionally NOT memoQ-style auto-propagation)

When the user lands on a blank bubble and an earlier bubble **in the same chapter** has an identical normalized source with a confirmed target, show a non-modal popup offering to insert it. Never auto-fill. Scope is strictly the current chapter (the TM handles cross-chapter).

## TM engine (must be specified in full in SPEC.md with a test table)

Language-aware normalization in front of a plain matcher:

```
Normalize(text, lang):
  NFKC fold
  lang in {ja, zh}: remove ALL whitespace (U+0020, U+3000, tabs, newlines)
  else (ko, en, ...): trim; collapse whitespace runs to single U+0020
  lowercase (affects Latin only)
  unify quote variants 「」『』""  and ellipsis variants (… / ・・・ / ...)
```

- Store raw + normalized + hash per TM entry.
- Exact match: hash of normalized.
- Candidate retrieval: character-bigram index for ja/zh, word-token index for ko/en; Dice ≥ 0.4, cap 50 candidates.
- Score: Levenshtein similarity on normalized strings, `1 − dist/max(len)`. Show ≥ 70%.
- Context boost up to +3% for same character / same kind / neighboring bubble also matched.
- Normalized length ≤ 3 characters: exact matches only.
- Import/export: TMX and XLSX for TM; TBX and XLSX for TB; both directions. Actual file I/O may go through PigTranslate; define the adapter interface.
- Include a test table of at least 15 source pairs across ja/zh/ko/en with expected match behavior (e.g. `("おはよう ございます", "おはようございます", ja) → exact`; `("안녕 하세요", "안녕하세요", ko) → fuzzy, not exact`).

## QA

Mechanical QA (Core, runs on demand and on confirm):
- untranslated / empty target; target identical to source
- TB term mismatch; forbidden terms list
- **line length and line count per bubble part**, with per-project limits. JA length must account for **tate-chū-yoko**: a run of ASCII digits (configurable max run, default 3) counts as 1 character (so `100人` = 2). CN/KO are horizontal; count plain characters.
- trailing punctuation and bracket pairing rules (project-configurable)

LLM QA (separate, on demand, via PigTranslate): sends the chapter (or selection) with source/target/character context and asks for literary review comments; results attach as per-bubble comments. **No LLM pretranslation feature** — explicitly out of scope.

## Export

- XLSX export of a chapter via the existing tool adapter. This is the standard deliverable.
- Later "premium" milestone, design for it now but do not schedule first: PSD export with each page restored to original dimensions as background, and a `Typesetting` layer group containing one text layer per target bubble part, using a user-selected default font.

## Non-functional

- Image pane must render **long vertical strips** (e.g. 1000 × 40,000 px) and large spreads at 60 fps scroll. Mandatory: tiled rendering via a custom Skia draw operation, tile cache, at least two downsampled levels, background decode, only visible tiles ± margin resident. Never load a full-size image into an Avalonia `Bitmap`.
- **IME gate**: a dedicated early task to verify Japanese and Korean IME composition in the target editor on Windows (composition string visible, conversion candidates work, no dropped characters). Later UI milestones must not start until it passes; if Avalonia's IME is inadequate, that is an architecture decision to surface immediately.
- Undo/redo for text and region edits.
- Autosave to the package at an interval; **crash recovery via a journal**: each confirmed edit appended to `<file>.pcml.journal` immediately; on opening a chapter with a journal present, offer to replay it.
- Accessibility of keyboard-only operation for the whole translate loop.

## Milestone shape (adjust as needed, but keep this order of risk)

1. Core data model + `.pcml` read/write + schema + tests
2. Tiled image canvas spike + IME gate (both risk-retiring; do before building the editor)
3. TM/TB engine + import/export + tests
4. Project model, main view, relink
5. Segment list + target editor + confirm loop + TM/TB box
6. Region create/delete/drag, target split
7. Character list (master + chapter buttons), kinds, notes
8. Mechanical QA, repetition popup, find/replace, stats
9. Journal/crash recovery, undo/redo polish
10. XLSX export adapter, LLM QA
11. Page reorder/add inside CAT; PSD export (deferred)

For each task in PLAN.md: files, behavior, acceptance check. Keep tasks independent of design judgment — the executing model will be weaker than you.
