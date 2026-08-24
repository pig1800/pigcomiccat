# PigComic — Specification

Version 1.0 (planning baseline, 2026-08-22). This document is the single source of truth for behavior. `docs/PLAN.md` references sections here by number. If code and this spec disagree, the spec wins; if the spec must change, update it first and log the change in `docs/DECISIONS.md`.

---

## 1. Overview

PigComic is a single-user desktop CAT (computer-assisted translation) tool specialized for comic/manga/webtoon translation, modeled on Trados/memoQ project conventions. Windows-first; cross-platform kept possible (no Windows-only APIs in `PigComic.Core`, and `PigComic.App` uses Avalonia).

**Goals**
- Translate `.pcml` chapter packages (images + bubbles) in a three-pane editor with a Trados-style confirm loop.
- Project-level TM (translation memory) and TB (termbase) in SQLite, with fuzzy matching, TMX/TBX/XLSX exchange.
- Mechanical QA, LLM literary QA, repetition offers, find/replace, progress/billing statistics.
- XLSX deliverable export matching the existing business-proven layout.
- Crash safety: atomic package writes + append-only journal.

**Non-goals (explicitly out of scope)**
- No LLM pretranslation. LLM is used only for on-demand review comments (§13).
- No multi-user, no server, no cloud sync.
- No multiple/master TMs or TBs — exactly one TM and one TB per project.
- No machine translation integration.
- No memoQ-style auto-propagation (see §10 for the popup-based alternative).

**Stack (fixed)**
- .NET 8, C# (nullable enabled, implicit usings).
- **Avalonia 12.1.1** + CommunityToolkit.Mvvm (MVVM), Microsoft.Extensions.DependencyInjection for service wiring. (Upgraded from 11.2.5 on 2026-08-23 — D-41. SkiaSharp pinned to 3.119.4 to match. See `CLAUDE.md` "Avalonia 12 conventions" for the API differences that break 11-era code.)
- SQLite via `Microsoft.Data.Sqlite` for `tm.db` / `tb.db`.
- `ClosedXML` for XLSX (same library as the existing tools), `SkiaSharp` (via Avalonia) for image work.
- xunit for tests. Every `PigComic.Core` feature ships with tests in the same milestone.

---

## 2. Glossary

| Term | Meaning |
|---|---|
| Package / chapter | One `.pcml` file = one chapter = one job. |
| Bubble | One translation unit: a source region on a page with source text, target text, status. Includes non-balloon text (signs, SFX). |
| Part | One of up to 3 sub-regions of a bubble's **target**. The source is always exactly one region. |
| Confirm | User action (Enter/Ctrl+Enter) that sets status `Translated` and writes the pair to the TM. |
| Normalized text | Output of `Normalize(text, lang)` (§7.2). All TM hashing/matching happens on normalized text. |
| MemoQ Count | The smaller billing count: CJK-ideograph count after ASCII-run collapsing (§11). |
| MSWord Count | The larger billing count: character count after ASCII-run collapsing (§11). |
| Lang class | `LangClass(languageTag)` = the lowercased BCP-47 primary subtag (`"zh-Hant"` → `"zh"`). Classes `ja` and `zh` share CJK rules; everything else uses space-delimited rules. |

---

## 3. Repository and solution layout

```
pigcomic/                       (this repo)
├─ PigComic.sln
├─ CLAUDE.md
├─ docs/                        SPEC.md, PLAN.md, DECISIONS.md, OPEN_QUESTIONS.md, IME_REPORT.md (produced by M2)
├─ src/
│  ├─ PigComic.Core/            domain, package I/O, TM/TB, QA, counting, export, journal — NO Avalonia references
│  │  ├─ Domain/                enums, model classes
│  │  ├─ Package/               .pcml read/write, validation, journal
│  │  ├─ Project/               project.json, characters.json, registry
│  │  ├─ Tm/  Tb/               engines + SQLite stores
│  │  ├─ Exchange/              TMX, TBX, XLSX import/export for TM/TB
│  │  ├─ Qa/                    mechanical QA, LLM QA orchestration
│  │  ├─ Counting/              MemoQ/MSWord counts
│  │  ├─ Export/                chapter XLSX export
│  │  ├─ Imaging/               tile pyramid math (no Skia types), pure geometry
│  │  └─ Adapters/              ILlmClient + stub, exchange interfaces
│  ├─ PigComic.App/             Avalonia UI
│  │  ├─ Views/  ViewModels/  Controls/  Rendering/ (Skia tile decode+draw)  Services/
│  └─ PigComic.Adapters.PigTranslate/   OPTIONAL project, see §25.2. Not in PigComic.sln; separate PigComic.Full.sln.
└─ tests/
   └─ PigComic.Core.Tests/      xunit; fixtures under tests/PigComic.Core.Tests/Fixtures/
```

Rules:
- `PigComic.Core` must not reference Avalonia, SkiaSharp, or any UI assembly. (Tile *math* lives in Core; tile *decoding/drawing* lives in App.)
- `PigComic.App` contains no business logic beyond view models delegating to Core services.
- All Core public APIs are synchronous unless they do I/O; I/O APIs are `async` with `CancellationToken`.

---

## 4. Domain model (PigComic.Core/Domain)

```csharp
public enum BubbleKind { Speech, Thought, Narration, Sfx, Sign, Note }
public enum BubbleStatus { Untranslated, Draft, Translated, Reviewed, Locked }

public sealed record PixelRect(int X, int Y, int Width, int Height);   // top-left origin, ORIGINAL image pixels (§5.6)

public sealed class TargetPart {            // mutable model objects backed by the XDocument (§5.8)
    public int Index { get; }               // 1..3
    public PixelRect Region { get; set; }
    public string Text { get; set; }        // LF newlines only
}

public sealed class Bubble {
    public string Id { get; }               // immutable, never renumbered
    public string PageId { get; set; }
    public int Order { get; set; }          // reading order within page, ≥1
    public BubbleKind Kind { get; set; }
    public string? Character { get; set; }  // null = unset
    public BubbleStatus Status { get; set; }
    public PixelRect SourceRegion { get; set; }
    public string SourceText { get; set; }
    public IReadOnlyList<TargetPart> Parts { get; }   // always 1..3; mutate via SetPartCount(n)
    public string Notes { get; set; }       // "" = none
    public string LlmComment { get; set; }  // "" = none
    public string TargetJoined => string.Join("\n", Parts.Select(p => p.Text));  // the TM unit target
}

public sealed class Page {
    public string Id { get; }
    public string FileName { get; set; }    // name inside /media
    public int Width { get; set; }          // ORIGINAL dimensions before any downscale
    public int Height { get; set; }
}

public sealed class Chapter {               // in-memory view of one .pcml
    public string Title { get; }
    public string ChapterNumber { get; }
    public string SourceLanguage { get; }
    public string TargetLanguage { get; }
    public IList<string> Characters { get; }         // chapter character-name list
    public IReadOnlyList<Page> Pages { get; }        // ordered
    public IReadOnlyList<Bubble> Bubbles { get; }    // sorted (page order, Order)
}
```

Reading order: global order = page order in `<pages>`, then `Order` ascending within a page. `Order` values are unique per page; the CAT **may renumber `Order`** when inserting/reordering, but **never changes `Id`**.

---

## 5. `.pcml` package format

### 5.1 Container

A `.pcml` file is a ZIP archive:

```
chapter012.pcml
├─ content.xml            everything except pixels (UTF-8, no BOM)
└─ media/
   ├─ 0001.jpg            page images; JPG and PNG both valid
   └─ 0002.png
```

- `content.xml` is at the archive root, exact name, lowercase.
- Media entries are stored with `CompressionLevel.NoCompression` (already-compressed formats; enables fast full-archive rewrites).
- `content.xml` is stored with normal Deflate compression.
- Unknown zip entries (e.g. `thumbs/…` added by other tools) are preserved verbatim on save.

### 5.2 Lifecycle

Generated by an external program (the `.pcml` generator, separate repo — see §27.3); after that the CAT owns the file and is the only writer. `content.xml` is the **round-trip storage**: source text, translations, regions, statuses, notes all live there. There is no separate "return package".

### 5.3 `content.xml` schema (version 1)

No XML namespace (DECISIONS D-02). Root element `<pcml>`. All text content uses LF (`\n`) newlines; the writer converts CRLF on write. Elements that carry user text (`source`, `text`, `notes`, `llmComment`) must be written with `xml:space="preserve"` when their value starts/ends with whitespace or is whitespace-only.

| Element / attribute | Type | Req | Meaning |
|---|---|---|---|
| `pcml` | element | ✔ | Root. |
| `pcml/@version` | int | ✔ | Schema version. This spec defines `1`. Reader accepts `1`; higher → open read-only with warning. |
| `pcml/meta` | element | ✔ | |
| `meta/title` | string | ✔ | Comic title. |
| `meta/chapter` | string | ✔ | Chapter number, free-form (`"012"`, `"12.5"`). |
| `meta/sourceLanguage` | BCP-47 | ✔ | e.g. `ja`, `zh-Hans`. |
| `meta/targetLanguage` | BCP-47 | ✔ | Exactly one target per package. |
| `pcml/characters` | element | ✔ | May be empty. |
| `characters/character` | element | 0..n | |
| `character/@name` | string | ✔ | Unique within the file. |
| `pcml/pages` | element | ✔ | ≥1 page. **Document order of `<page>` elements = page order.** Order is explicit, never directory order. |
| `pages/page/@id` | string | ✔ | Unique within file. Generator convention: `p0001`… |
| `pages/page/@file` | string | ✔ | File name inside `media/` (no path separators). |
| `pages/page/@width`, `@height` | int ≥1 | ✔ | **Original** image dimensions before any downscaling (needed for PSD export). The media file itself may be smaller; see §5.6. |
| `pcml/bubbles` | element | ✔ | May be empty. Writer emits bubbles sorted by (page order, `@order`); reader tolerates any order and sorts. |
| `bubble/@id` | string | ✔ | Stable, unique within file. Generator emits deterministic IDs (`p0001-b0001` = page index + bubble index). The CAT **never renumbers**; CAT-created bubbles get `u` + 8 random lowercase hex chars, collision-checked (D-03). |
| `bubble/@page` | string | ✔ | Must reference an existing `page/@id`. |
| `bubble/@order` | int ≥1 | ✔ | Reading order within the page; unique per page. |
| `bubble/@kind` | enum | ✔ | `Speech` `Thought` `Narration` `Sfx` `Sign` `Note` (exact strings). |
| `bubble/@character` | string | opt | Speaking character; attribute omitted when unset. Value need not exist in `characters` (validation warns, §5.7). |
| `bubble/@status` | enum | ✔ | `Untranslated` `Draft` `Translated` `Reviewed` `Locked`. |
| `bubble/region` | element | ✔ (exactly 1) | The **source** region. |
| `region/@x @y @width @height` | int | ✔ | Top-left origin, original-image pixel coordinates. `width,height ≥ 1`. May extend past page bounds (OCR noise); the CAT clamps only on user edit. |
| `bubble/source` | string element | ✔ | Source text; may be empty. |
| `bubble/target` | element | ✔ | Always present. |
| `target/part` | element | 1..3 | `@index` = 1..3, contiguous from 1, unique. **1 part = unsplit** (the normal case). |
| `part/@index` | int | ✔ | |
| `part/region` | element | ✔ (exactly 1) | Target part region, same coordinate space as source region. For an untranslated/unsplit bubble the generator sets it equal to the source region. |
| `part/text` | string element | ✔ | Target text of this part; may be empty. |
| `bubble/notes` | string element | 0..1 | User notes. |
| `bubble/llmComment` | string element | 0..1 | LLM QA comment (§13). Kept separate from user notes (D-04). |

**The TM unit is the whole bubble**: source text ↔ parts' texts joined with `\n` (`TargetJoined`), never an individual part.

**Forward compatibility**: unknown elements and unknown attributes anywhere in the document are preserved byte-for-byte on round-trip, in their original positions relative to known siblings. Mechanism: §5.8.

### 5.4 Complete example

```xml
<?xml version="1.0" encoding="utf-8"?>
<pcml version="1">
  <meta>
    <title>勇者ピッグ</title>
    <chapter>012</chapter>
    <sourceLanguage>ja</sourceLanguage>
    <targetLanguage>zh-Hant</targetLanguage>
  </meta>
  <characters>
    <character name="ピッグ"/>
    <character name="魔王"/>
  </characters>
  <pages>
    <page id="p0001" file="0001.jpg" width="1080" height="41250"/>
    <page id="p0002" file="0002.png" width="1080" height="38200"/>
  </pages>
  <bubbles>
    <bubble id="p0001-b0001" page="p0001" order="1" kind="Speech" character="ピッグ" status="Translated">
      <region x="612" y="480" width="240" height="310"/>
      <source>おはよう　ございます！</source>
      <target>
        <part index="1">
          <region x="612" y="480" width="240" height="310"/>
          <text>早安！</text>
        </part>
      </target>
    </bubble>
    <bubble id="p0001-b0002" page="p0001" order="2" kind="Thought" status="Draft">
      <region x="120" y="900" width="300" height="280"/>
      <source>まさか…魔王が１００人も？</source>
      <target>
        <part index="1">
          <region x="120" y="900" width="300" height="130"/>
          <text>難道說…</text>
        </part>
        <part index="2">
          <region x="120" y="1050" width="300" height="130"/>
          <text>魔王竟有100人？</text>
        </part>
      </target>
      <notes>確認：100人 or 100隻</notes>
      <llmComment>「まさか」の驚きがやや弱い。語気詞の追加を検討。</llmComment>
    </bubble>
    <bubble id="p0002-b0001" page="p0002" order="1" kind="Sfx" status="Untranslated">
      <region x="0" y="200" width="400" height="600"/>
      <source>ドドド</source>
      <target>
        <part index="1">
          <region x="0" y="200" width="400" height="600"/>
          <text></text>
        </part>
      </target>
    </bubble>
  </bubbles>
</pcml>
```

### 5.5 Atomic save

Never leave a half-written zip:

1. Snapshot the model on the UI thread (writes are serialized; one save at a time).
2. Write a complete new archive to `<file>.pcml.tmp` **in the same directory** (same volume ⇒ atomic rename possible). Media entries and unknown zip entries are stream-copied from the currently open archive.
3. Flush and close the temp file.
4. `File.Replace(tmp, target, target + ".bak")` — the previous version survives as `<file>.pcml.bak` (one generation kept, D-05). On platforms/filesystems where `File.Replace` fails, fall back to `File.Move(tmp, target, overwrite: true)` after copying target→bak.
5. On success, delete `<file>.pcml.journal` (§23).

Autosave runs this same path on a background thread every `autosaveSeconds` (default 180) when the chapter is dirty.

### 5.6 Coordinate space and downscaled media

Region coordinates are **always in original-image pixel space** (`page/@width`,`@height`). The media file may be a downscaled copy; on load the App reads each media image's actual dimensions from its header and computes `scale = mediaWidth / page.Width` for overlay drawing and hit-testing. All model/geometry math stays in original space; only rendering multiplies by `scale`.

### 5.7 Validation

`PcmlValidator.Validate(doc)` returns `List<PcmlIssue>` (`Severity` Error/Warning, `Code`, `Message`, `BubbleId?`). A file with Errors opens read-only; Warnings open normally and are shown once.

| Code | Severity | Condition |
|---|---|---|
| PCML-E01 | Error | Missing/duplicate required element or attribute per §5.3 table. |
| PCML-E02 | Error | `@version` > 1 (read-only) or not an integer. |
| PCML-E03 | Error | Duplicate `page/@id` or `bubble/@id`. |
| PCML-E04 | Error | `bubble/@page` references no page. |
| PCML-E05 | Error | `page/@file` missing from `media/`, or contains `/` or `\`. |
| PCML-E06 | Error | Part count 0 or > 3; `@index` not contiguous from 1. |
| PCML-E07 | Error | Enum value outside §5.3 sets. |
| PCML-W01 | Warning | Duplicate `@order` within a page (reader re-sorts stably and renumbers in memory; file fixed on next save). |
| PCML-W02 | Warning | `@character` not in `<characters>` list (name auto-added to the chapter list in memory). |
| PCML-W03 | Warning | Region fully outside page bounds. |
| PCML-W04 | Warning | Media entry not referenced by any page (preserved, not shown). |

### 5.8 Round-trip mechanism (normative implementation approach)

The loaded `XDocument` **is** the persistence model and stays in memory for the life of the open chapter. Domain objects (§4) hold references to their backing `XElement`s; property setters write through to the element immediately. Saving serializes the (always current) `XDocument`. Creating a bubble creates a fresh `XElement`; deleting removes it. This guarantees unknown elements/attributes and their ordering survive untouched with no bookkeeping. (D-06. Memory cost is negligible next to images.)

---

## 6. Project model

### 6.1 Project folder

A project = one manga title. Its folder (chosen/created at project creation) contains:

```
MyManga/
├─ project.json
├─ characters.json
├─ characters/            pasted character images (PNG), file name = random 8-hex + .png
├─ tm.db
└─ tb.db
```

`.pcml` files are referenced by absolute path in `project.json` — **never copied** into the project folder.

### 6.2 `project.json` (schema version 1)

```json
{
  "schemaVersion": 1,
  "title": "勇者ピッグ",
  "sourceLanguage": "ja",
  "targetLanguage": "zh-Hant",
  "chapters": [
    { "path": "D:\\jobs\\pig\\ch012.pcml" }
  ],
  "settings": {
    "autosaveSeconds": 180,
    "qa": {
      "maxCharsPerLine": 8,
      "maxLinesPerPart": 4,
      "tcyMaxDigitRun": 3,
      "identicalExemptKinds": ["Sfx"],
      "forbiddenTrailing": [],
      "bracketPairs": ["「」", "『』", "（）", "()", "【】"]
    },
    "export": { "includeDataSheet": false, "defaultFont": "" },
    "llmQa": { "provider": "claude", "model": "claude-opus-5", "temperature": 0.2 }
  }
}
```

Chapter array order = display order. Unknown JSON properties are preserved on save (`JsonSerializerOptions` round-trip via `JsonNode`, not typed POCO serialization — D-07). Language pair is fixed at creation (matching a package with a different pair refuses to open into this project with a clear error).

### 6.3 `characters.json` — master character list

```json
{
  "schemaVersion": 1,
  "characters": [
    {
      "name": "ピッグ",
      "image": "characters/a3f09c12.png",
      "gender": "male",
      "age": "16",
      "firstChapter": "001",
      "pronoun": "オレ",
      "comments": "主人公。語尾「〜だぜ」"
    }
  ]
}
```

`name` required and unique; everything else optional (`""`/absent). `image` is project-folder-relative. Clipboard paste: image saved as PNG into `characters/`.

### 6.4 Projects registry (main view backing)

`%APPDATA%/PigComic/registry.json`: `{ "schemaVersion": 1, "projects": [ { "projectJsonPath": "...", "lastOpenedUtc": "..." } ] }`. Sorted most-recently-opened first. Cross-platform path via `Environment.SpecialFolder.ApplicationData`.

### 6.5 Main view behavior

- **List** of registered projects (title, path, language pair, last opened). Double-click / Enter opens.
- **Open**: file picker for an existing `project.json` (adds to registry).
- **Create**: dialog with title (required), folder path (required; created if missing; must be empty or nonexistent), source language, target language (dropdowns: `ja`, `zh-Hans`, `zh-Hant`, `ko`, `en`, plus free-text override). Creates folder contents (§6.1) with empty TM/TB.
- **Remove**: dialog with two radio choices — *Remove from list only* (default) / *Delete project folder from disk*. Deleting requires a second checkbox "I understand this permanently deletes the folder" (permanent delete, no recycle bin — D-08). `.pcml` files are outside the folder and are never deleted by this.

### 6.6 Relink (Trados-style)

On project open, verify every `chapters[].path` exists. If any are missing, show the **Relink dialog**: a list of missing paths, each with a *Browse…* button, plus a *Search folder…* button that lets the user pick one directory to resolve all missing files by exact filename match. *OK* enabled only when all are resolved (resolved paths are written to `project.json`). **Cancel → return to the main project list; do not enter the project.** Opening an individual chapter whose file disappeared while the project is open triggers the same dialog for that file.

---

## 7. TM engine

### 7.1 Storage — `tm.db`

```sql
PRAGMA journal_mode=WAL;
CREATE TABLE meta(key TEXT PRIMARY KEY, value TEXT NOT NULL);
  -- keys: schemaVersion=1, sourceLanguage, targetLanguage
CREATE TABLE entries(
  id           INTEGER PRIMARY KEY,
  source_raw   TEXT NOT NULL,
  source_norm  TEXT NOT NULL,
  source_hash  INTEGER NOT NULL,        -- §7.3
  target_raw   TEXT NOT NULL,           -- TargetJoined (LF-joined parts)
  character    TEXT,                    -- nullable context
  kind         TEXT,                    -- nullable context (enum string)
  chapter      TEXT,                    -- meta/chapter of origin
  bubble_id    TEXT,
  prev_hash    INTEGER,                 -- source_hash of the reading-order-previous bubble at confirm time
  created_utc  TEXT NOT NULL,
  modified_utc TEXT NOT NULL
);
CREATE UNIQUE INDEX ux_entries_src_char ON entries(source_hash, IFNULL(character,''));
CREATE INDEX ix_entries_hash ON entries(source_hash);
CREATE TABLE grams(                     -- retrieval index, rebuildable
  gram     TEXT NOT NULL,
  entry_id INTEGER NOT NULL REFERENCES entries(id) ON DELETE CASCADE
);
CREATE INDEX ix_grams_gram ON grams(gram);
```

**Write rule (on confirm)**: upsert keyed on `(source_hash, character-or-empty)` — same source by the same character overwrites the target (newest wins, Trados "overwrite existing" convention, D-09); same source by a different character is a separate entry. Confirming with an empty target writes nothing.

### 7.2 Normalization (normative)

`Normalize(text, lang)` where `lang` is the language tag of the *text being normalized* (source language for TM keys). Steps in this exact order:

1. **NFKC** (`text.Normalize(NormalizationForm.FormKC)`). Note: NFKC already converts full-width ASCII (`１００`→`100`), U+3000→U+0020, `…`→`...`, `‥`→`..`, `･`→`・`.
2. **Whitespace** by lang class:
   - `ja`, `zh`: remove **every** char where `char.IsWhiteSpace` is true (spaces, tabs, newlines — U+3000 already became U+0020 in step 1).
   - all others (`ko`, `en`, …): trim, then collapse each run of whitespace chars to a single U+0020.
3. **Lowercase**: `ToLowerInvariant()` (affects Latin/Cyrillic/Greek only).
4. **Quote unification** (single-char mapping): `『`→`「`, `』`→`」`, `“`→`「`, `”`→`」`, `‘`→`「`, `’`→`」`. Straight `"` and `'` are left untouched (direction is ambiguous — D-10).
5. **Ellipsis unification**: replace every maximal run of **two or more** characters drawn from the set { `.` U+002E, `・` U+30FB, `…` U+2026 } with a single `…`. (Because of step 1, `…` and `・・・` and `...` all arrive here as runs.) A single `.` is left alone.

The result may be empty (whitespace-only source). Empty normalized sources are never written to or looked up in the TM.

`NormLength(text)` = number of Unicode code points (`Rune` count) of the normalized string.

### 7.3 Hash

`source_hash` = first 8 bytes of SHA-256 of the UTF-8 bytes of the normalized string, interpreted as little-endian `long`. Deterministic across platforms; used for exact match, retrieval, and context (`prev_hash`).

### 7.4 Retrieval index

Per entry, `grams` rows are computed from `source_norm`:
- Lang class `ja`/`zh`: the set of **distinct adjacent code-point bigrams** (`Rune` pairs concatenated). If `NormLength == 1`, the single code point is the only gram.
- Other lang classes: the set of **distinct whitespace-delimited tokens** (split on U+0020).

### 7.5 Query algorithm (normative)

```
Query(sourceText, context) -> ranked matches
  norm = Normalize(sourceText, sourceLang); if norm == "" -> no matches
  h = Hash(norm)
  exactRows = SELECT * FROM entries WHERE source_hash = h AND source_norm = norm   -- guard vs 64-bit collisions
  if NormLength(norm) <= 3: candidates = exactRows only            -- short-segment rule
  else:
    grams(Q) as §7.4
    candidate entry ids: entries sharing ≥1 gram; per candidate compute
      Dice = 2·|G(Q) ∩ G(E)| / (|G(Q)| + |G(E)|)  over distinct-gram sets
    keep Dice ≥ 0.4, order by Dice desc, cap at 50 candidates (exact rows always kept)
  for each candidate:
    if exact: base = 100
    else: base = floor(100 · (1 − Levenshtein(norm, entry.source_norm) / max(NormLength both)))
          (Levenshtein over code points)
    discard if base < 70
    boost = (+1 if entry.character != null and entry.character == context.character)
          + (+1 if entry.kind != null and entry.kind == context.kind)
          + (+1 if entry.prev_hash != null and entry.prev_hash == context.prevSourceHash)
    score = min(base + boost, 103)
  sort score desc, then modified_utc desc; return top 20
```

`context.prevSourceHash` = hash of the normalized source of the bubble immediately before the queried bubble in reading order (null for the first bubble). Display: `100%` = exact; `101–103%` = context-boosted exact (memoQ-style); `70–99%+boost` = fuzzy.

### 7.6 TM maintenance

- Rebuildable index: a `Rebuild grams` maintenance command drops and recreates `grams` from `entries` (used after import).
- TM editor UI is out of scope for v1 except: delete entry from the TM/TB results box context menu (D-11).

### 7.7 TM match test table (normative — implement as xunit theory)

TM contains the *Stored* text (as raw source, normalized per rules); *Query* is looked up. Lang column = source language. Expected shows exact/fuzzy decision and the **base** score before boosts.

| # | Stored | Query | Lang | Expected |
|---|---|---|---|---|
| 1 | `おはようございます` | `おはよう ございます` | ja | **Exact 100%** (whitespace removed) |
| 2 | `안녕하세요` | `안녕 하세요` | ko | **Fuzzy 83%** (space collapsed, not removed: dist 1 / len 6) — *not* exact |
| 3 | `hello world` | `Hello  World` | en | **Exact 100%** (collapse + lowercase) |
| 4 | `「こんにちは」` | `『こんにちは』` | ja | **Exact 100%** (quote unification) |
| 5 | `そうか...` | `そうか…` | ja | **Exact 100%** (ellipsis unified; NFKC makes both `そうか...` → run → `…`) |
| 6 | `そうか…` | `そうか・・・` | ja | **Exact 100%** (middle-dot run unified) |
| 7 | `100人` | `１００人` | ja | **Exact 100%** (NFKC full-width digits) — note NormLength 4 > 3 |
| 8 | `だめだよ` | `だめだ` | ja | **No match** (query NormLength 3 ≤ 3 → exact-only; not exact) |
| 9 | `你好吗?` | `你好嗎？` | zh | **Fuzzy 75%** (NFKC `？`→`?`; 嗎≠吗, dist 1 / len 4) |
| 10 | `cat` | `Cat` | en | **Exact 100%** (short but exact is allowed) |
| 11 | `i like dogs` | `I like cats` | en | **Fuzzy 72%** (token Dice 0.67 retrieves; dist 3 / len 11 → 72) |
| 12 | `we hate birds` | `I like cats` | en | **No match** (token Dice 0 → not retrieved) |
| 13 | `おはようございます` | `おはようございます。` | ja | **Fuzzy 90%** (dist 1 / len 10) |
| 14 | `東京タワー` | `東京　タワー` | ja | **Exact 100%** (U+3000 removed) |
| 15 | `tokyo tower` | `Tokyo\tTower` | en | **Exact 100%** (tab collapsed to space) |
| 16 | `こんばんは` | `こんにちは` | ja | **No match** (bigram Dice 2/8 = 0.25 < 0.4 → not retrieved) |
| 17 | `５００円です` | `500円です` | ja | **Exact 100%** |
| 18 | `감사합니다` | `감사 합니다` | ko | **Fuzzy 83%** (dist 1 / len 6) |
| 19 | `“hello”` | `「hello」` | en | **Exact 100%** (curly quotes → corner brackets both sides) |
| 20 | boost case: stored (`まて`,char=`魔王`,kind=`Speech`) queried as `まて` with char=`魔王`, kind=`Speech`, prev matches | | ja | **Exact, displayed 103%** (100 + 1 + 1 + 1) |

---

## 8. TB engine

### 8.1 Storage — `tb.db`

```sql
PRAGMA journal_mode=WAL;
CREATE TABLE meta(key TEXT PRIMARY KEY, value TEXT NOT NULL);   -- schemaVersion=1, languages
CREATE TABLE terms(
  id          INTEGER PRIMARY KEY,
  source_term TEXT NOT NULL,
  source_norm TEXT NOT NULL,            -- Normalize(source_term, sourceLang)
  target_term TEXT NOT NULL,            -- "" allowed only when forbidden=1
  forbidden   INTEGER NOT NULL DEFAULT 0,  -- 1 = target_term must NOT appear in target text
  notes       TEXT NOT NULL DEFAULT '',
  created_utc TEXT NOT NULL,
  modified_utc TEXT NOT NULL
);
CREATE INDEX ix_terms_norm ON terms(source_norm);
```

Multiple rows may share `source_term` (synonyms: any one of the targets satisfies QA). A **forbidden** row means: `target_term` (matched per §8.2 against the *target* language) must not appear in the translation; its `source_term` may be empty (`""`) for unconditional forbidden words.

### 8.2 Term hit test

`ContainsTerm(text, term, lang)`:
- Normalize both with `Normalize(·, lang)`.
- Lang class `ja`/`zh`: substring containment.
- Other classes: whole-token match — term token sequence appears at token boundaries of the text's token list.

### 8.3 Lookup for the results box

For the selected bubble: all terms where `ContainsTerm(bubble.SourceText, terms.source_term, sourceLang)` and `source_term != ""`, ordered by first occurrence position in the source, then longest `source_norm` first. Forbidden entries are shown with a ⛔ marker and are never inserted via Ctrl+N.

---

## 9. Combined TM/TB results box

One list, Trados/memoQ style — **not** two boxes:

1. TM matches (per §7.5), highest score first — number badges `1..9` assigned in display order.
2. Then TB hits (§8.3), continuing the numbering while ≤ 9.

Interaction:
- `Ctrl+1..9` or double-click inserts entry N.
- **TM entry insert**: replaces the *entire* target — collapses to 1 part (part regions reset to the source region) and sets its text to the TM target (D-12). Status → `Draft` (not confirmed).
- **TB entry insert**: inserts `target_term` at the caret in the focused part.
- Each TM row shows: score%, target text (single line, `⏎` for newlines), source diff highlight (differences vs current source underlined), origin chapter, character. Each TB row shows: source term → target term, forbidden marker, notes tooltip.
- Query runs async on selection change with 150 ms debounce; results for a stale selection are discarded.

---

## 10. Repetition offer (NOT auto-propagation)

Trigger: selection lands on a bubble whose status is `Untranslated` **and** whose `TargetJoined` is empty, and at least one *earlier* bubble in reading order **in the same chapter** has an identical normalized source (`Normalize(source, sourceLang)`, non-empty) and status ∈ {`Translated`, `Reviewed`, `Locked`}.

Behavior: a **non-modal** popup (toast anchored above the target cell): "Same source confirmed at p.3 #2: 『早安！』 — [Insert (Alt+R)] [Dismiss (Esc)]". Multiple earlier candidates: use the **nearest preceding** one (D-13). Insert behaves like a TM insert (§9) but sets status `Draft`. **Never auto-fill.** The popup closes on navigation, Esc, or insert. Cross-chapter repetition is the TM's job — scope here is strictly the current chapter.

---

## 11. Counting (billing/progress) — port of LanMangaCount

Two counts, both computed from each bubble's **source text** (default; a UI toggle can compute over target text with the same rules). Algorithm is a faithful port of `LanMangaCount/Form1.cs` (see DECISIONS D-14 for the delta list):

```
Preprocess(s):
  s = Regex.Replace(s, "[!-~]+", "一")     # each run of printable ASCII U+0021..U+007E → single 一 (U+4E00)
  s = s.Replace(" ", "")                   # strip U+0020 ONLY (not \n, \t, U+3000)
MemoQCount(s)  = count of Unicode CODE POINTS of Preprocess(s) inside the CJK ideograph blocks:
                 4E00–9FFF, 3400–4DBF, F900–FAFF, 20000–2A6DF, 2A700–2B73F, 2B740–2B81F,
                 2B820–2CEAF, 2CEB0–2EBEF, 2F800–2FA1F, 30000–3134F
MSWordCount(s) = UTF-16 length of Preprocess(s) minus occurrences of '–' (U+2013) and '—' (U+2014)
```

Guaranteed `MemoQCount ≤ MSWordCount`. Per-bubble counts sum to chapter counts; chapters sum to project counts. Equivalence goal (acceptance test in PLAN M10.1): running this same algorithm over the source-column cell values of a PigComic XLSX export must equal PigComic's in-model chapter source counts.

### 11.1 Count test table (normative)

| # | Input | MemoQ | MSWord | Why |
|---|---|---|---|---|
| 1 | `Hello world` | 2 | 2 | two ASCII runs → `一一` |
| 2 | `こんにちは。` | 0 | 6 | kana+punct: no ideographs |
| 3 | `第100話` | 3 | 3 | `100` → `一` |
| 4 | `勇者–魔王` | 4 | 4 | dash excluded from MSWord; 4 ideographs |
| 5 | `気持ち\nいい` | 2 | 6 | `\n` counts in MSWord, not MemoQ |
| 6 | `𠮷野家` | 3 | 4 | 𠮷 = 1 code point but 2 UTF-16 units |
| 7 | `「はい」` | 0 | 4 | CJK punctuation counts in MSWord only |
| 8 | `A B C` | 3 | 3 | three runs |
| 9 | `　全角` | 2 | 3 | U+3000 not stripped |
| 10 | `don't stop` | 2 | 2 | apostrophe inside run |

---

## 12. Mechanical QA

Runs (a) on demand over a chapter or the whole project (F8 / menu), (b) on confirm for the confirmed bubble only (rules marked ⚡). Results: `QaIssue { RuleId, Severity, BubbleId, PartIndex?, Message }` listed in the QA panel; double-click navigates. Config lives in `project.json` `settings.qa` (§6.2).

| Rule | ⚡ | Severity | Condition |
|---|---|---|---|
| QA-EMPTY | ⚡ | Error | Status ≥ `Translated` and `TargetJoined` is empty/whitespace, or any part text empty while another is not. |
| QA-UNTRANS | | Warning | On-demand only: status is `Untranslated` or `Draft`. |
| QA-SAME | ⚡ | Warning | `Normalize(target, targetLang) == Normalize(source, sourceLang)`, non-empty, and kind ∉ `identicalExemptKinds` (default `["Sfx"]`). |
| QA-TERM | ⚡ | Error | Some TB row (non-forbidden, `source_term≠""`) has `ContainsTerm(source, source_term, srcLang)` true, and **no** row with the same `source_term` has `ContainsTerm(target, target_term, tgtLang)` true. |
| QA-FORBID | ⚡ | Error | Some forbidden TB row has `ContainsTerm(target, target_term, tgtLang)` true. |
| QA-LINELEN | ⚡ | Warning | Any line of any part has `VisualLength` (§12.1) > `maxCharsPerLine`. |
| QA-LINECOUNT | ⚡ | Warning | Any part has more than `maxLinesPerPart` lines (lines = LF-split). |
| QA-TRAILING | ⚡ | Warning | Last non-whitespace char of `TargetJoined` is in `forbiddenTrailing` (default empty list). |
| QA-BRACKET | ⚡ | Error | For each configured pair (2-char string "open,close"), open/close counts unbalanced or a close precedes its open (simple counter scan per pair). |

### 12.1 `VisualLength(line, targetLang)` — tate-chū-yoko aware

- If `LangClass(targetLang) == "ja"` (vertical text convention): scan code points; each **maximal run of ASCII digits** `0-9` with length ≤ `tcyMaxDigitRun` (default 3) counts as **1**; longer digit runs count 1 per digit; every other code point counts 1.
- Otherwise (`zh`, `ko` horizontal): each code point counts 1.

Test table (normative; `tcyMaxDigitRun=3`, lang=ja unless noted):

| Input line | Lang | VisualLength |
|---|---|---|
| `100人` | ja | 2 |
| `1000人` | ja | 5 (run of 4 > 3) |
| `3人と5人` | ja | 5 |
| `こんにちは` | ja | 5 |
| `100人` | zh-Hant | 4 |
| `第12話` | ja | 3 |

---

## 13. LLM QA (on demand)

- Scope: current selection (≥1 bubbles) or whole chapter. Split-button in the function pane (+ menu command); the split menu also opens the **LLM QA settings dialog** (prompt editor, memory viewer/editor, provider/model selection).
- **Prompt**: project file `llm-qa-prompt.txt` (created from a built-in default template on first use), editable in the settings dialog with a plain multi-line editor. The default template instructs the model on the reviewer role, the response contract below, and how to maintain its memory.
- **Memory**: project file `llm-qa-memory.md` — a persistent scratchpad the LLM maintains across QA runs (character voice observations, recurring terminology issues, style agreements). Included verbatim in every request; the response may replace it (below). Viewable and hand-editable in the settings dialog. Hard cap 16,000 characters: a returned memory longer than the cap is rejected with a warning (comments are still applied, memory left unchanged).
- **Provider/model**: from `project.json` `settings.llmQa` (§6.2) — `provider` ∈ {`claude`, `openai`, `gemini`} (free-text tolerated, passed through), `model` free text. Defaults: `claude` / `claude-opus-5`. Selectable in the settings dialog (provider dropdown + model text box).
- Request assembly: system prompt = `llm-qa-prompt.txt`; user content = language pair, character sheet (master-list rows for characters appearing in scope: name, gender, age, pronoun, comments), current memory (fenced section), and the bubble list as JSON `[{id, page, kind, character, source, target}]`.
- Response contract — the model must return **only** a JSON object:
  ```json
  { "comments": [ { "id": "p0001-b0002", "comment": "..." } ], "memory": "full replacement text (optional)" }
  ```
  Comments are in the target language, only for bubbles deserving literary remarks (mistranslation, tone, character voice, consistency); omitted bubbles are untouched. `memory`, when present, **fully replaces** `llm-qa-memory.md`; when absent, memory is unchanged.
- Response parsing: extract the first `{`…matching-`}` JSON object; unknown bubble ids ignored; each comment written to `bubble.llmComment` (overwrites previous — D-04); memory applied per the cap rule. Malformed JSON → error toast, nothing written, memory untouched.
- Runs through `ILlmClient` (§25.1). Progress dialog with cancel. **No pretranslation feature exists anywhere in the app.**

---

## 14. Editor UI

### 14.1 Layout

Three vertical areas, left→right, with draggable splitters (widths persisted in registry.json):

```
┌───────────────┬──────────────────────────────┬───────────────┐
│  Image pane   │  Segment list                │ Function pane │
│  (tiled       │  ┌────────────┬───────────┐  │  TM/TB box    │
│   canvas,     │  │ Source     │ Target    │  │  Kind         │
│   overlays)   │  │ col        │ col       │  │  Character    │
│               │  └────────────┴───────────┘  │  Notes        │
│               │                              │  LLM QA btn   │
└───────────────┴──────────────────────────────┴───────────────┘
 Status bar: chapter, page x/y, selected bubble id, counts, save state
```

### 14.2 Image pane

- Renders the **current page** via the tiled renderer (§20). Scroll (wheel = vertical, Shift+wheel = horizontal) and zoom (Ctrl+wheel around cursor; Ctrl+`+`/`-`/`0` = in/out/fit-width).
- Overlays: every bubble's source region on the current page as a rectangle (2 px outline; color by status — Untranslated gray, Draft amber, Translated green, Reviewed blue, Locked purple; selected bubble: thicker outline + fill at 15% opacity). When a bubble is selected, its target part regions are drawn dashed.
- Click inside a region selects that bubble (topmost smallest region wins on overlap) and focuses its row in the segment list; selecting a row auto-scrolls/pages the image so the region is visible (centered when off-screen).
- PageUp/PageDown switch pages; selecting a bubble on another page switches pages automatically.

### 14.3 Segment list

Virtualized list, one row per bubble, global reading order across all pages (page separators as thin group headers). Two columns:

- **Source column** (read-mostly): first line shows `kind` glyph + character name (if set) in small text; below, the source text. `F2` (or double-click on the text) opens inline source-text editing (OCR fixes) — Enter commits, Esc cancels; source edits set no status change but mark dirty. Region create/delete/drag/resize is driven from this column's selection interacting with the image pane (§15).
- **Target column** (editable): the bubble's parts stacked vertically; each part is a text editor, text **center-aligned** (comic convention). Part editors: Enter = confirm bubble (§14.4); Shift+Enter = line break in the part; Tab/Shift+Tab = next/previous part (at the last/first part, Tab moves focus nowhere — beep). Row height grows with content.

Row background reflects status color at low opacity. Locked bubbles: target read-only.

### 14.4 Confirm loop and statuses

- Typing in a target of an `Untranslated` bubble sets `Draft` immediately.
- **Enter** (target focused): run ⚡ QA rules on this bubble (issues shown as inline markers; Errors do NOT block confirm — D-15), set status `Translated`, write TM (§7.1; skipped if target empty — Enter on an empty target just moves on without status change), move selection to the **next bubble** and focus its first part editor.
- **Ctrl+Enter**: same, but move to the next bubble whose status is `Untranslated` or `Draft` (skips confirmed and Locked).
- **Ctrl+Shift+Enter**: confirm as `Reviewed` (also writes TM), move next.
- **Ctrl+L**: toggle `Locked` (from any status; unlocking restores `Translated` if target non-empty, else `Untranslated` — D-16).
- Re-confirming a `Translated`/`Reviewed` bubble re-writes (upserts) the TM entry.

### 14.5 Function pane (context of the selected bubble)

Top to bottom:
1. **TM/TB results box** (§9), fills remaining height.
2. **Kind** selector: 6 toggle buttons in a row (Speech/Thought/Narration/SFX/Sign/Note), keyboard accessible.
3. **Character box**: a text field with autocomplete over the **master** character list; below it, one-click buttons for **every distinct character name used in the current chapter** (from the chapter `<characters>` list plus any `@character` values). Clicking a button sets the selected bubble's speaker. Typing a new name and pressing Enter: sets the speaker, adds the name to the chapter list, and shows a small inline prompt "Add 『X』 to master list?" [Add/Not now] (Add opens the master editor prefilled).
4. **Notes**: multi-line text bound to `bubble.notes`.
5. **LLM comment**: read-only display of `bubble.llmComment` with a clear button.
6. **LLM QA button**: runs §13 for current selection/chapter (split-button).

### 14.6 Keyboard map (complete, Trados defaults where applicable)

| Shortcut | Context | Action |
|---|---|---|
| `Enter` | target editor | Confirm as Translated, next bubble (§14.4) |
| `Ctrl+Enter` | target editor | Confirm, next Untranslated/Draft |
| `Ctrl+Shift+Enter` | target editor | Confirm as Reviewed, next |
| `Shift+Enter` | target editor | Insert line break |
| `Ctrl+Up` / `Ctrl+Down` | editor | Previous / next bubble (no confirm) |
| `Tab` / `Shift+Tab` | target editor | Next / previous part |
| `Ctrl+Insert` | editor | Copy source (Trados): replace the focused part's entire text with the bubble's source text |
| `Ctrl+1`…`Ctrl+9` | editor | Insert Nth TM/TB result (§9) |
| `Ctrl+L` | editor | Toggle Locked |
| `F2` | segment list | Edit source text inline |
| `Alt+1` / `Alt+2` / `Alt+3` | editor | Set target part count 1 / 2 / 3 (§15.3) |
| `Alt+R` | popup visible | Accept repetition offer (§10) |
| `Esc` | anywhere | Dismiss popup / cancel drag / cancel inline edit |
| `Ctrl+B` | editor | Enter draw-new-bubble mode (§15.2) |
| `Delete` | image pane, region selected | Delete bubble (confirm dialog) |
| `Ctrl+F` / `Ctrl+H` | editor | Find / replace in current chapter |
| `Ctrl+Shift+F` / `Ctrl+Shift+H` | project open | Project-wide find / replace |
| `F8` | editor | Run mechanical QA on chapter |
| `Ctrl+S` | editor | Save package now |
| `Ctrl+Z` / `Ctrl+Y` | editor | Undo / redo |
| `Ctrl+Shift+K` | editor | Focus kind selector |
| `Ctrl+Shift+C` | editor | Focus character box |
| `Ctrl+Shift+N` | editor | Focus notes |
| `PageUp` / `PageDown` | image pane focus | Previous / next page |
| `Ctrl++` / `Ctrl+-` / `Ctrl+0` | image pane | Zoom in / out / fit width |

All bindings live in one `KeyBindings` class (single source; no scattered literals). Conflicting Avalonia defaults are overridden.

---

## 15. Regions and target split

**Y-only rule (owner directive, D-36)**: the translation view is plain top-to-bottom LTR. Every automatic placement/ordering decision in the CAT — new-bubble order insertion (§15.2), default split-part placement (§15.3), auto-scroll targeting — uses **only the region's Y coordinate**. X (and width) are recorded faithfully, drawn as part of the region marker in the image pane, and fully editable, but no CAT logic branches on them. RTL and vertical-text considerations exist **only** in the deferred PSD export (§27.2). Do not implement any right-to-left or vertical-writing-aware behavior anywhere in the editor.

### 15.1 Source region editing

With a bubble selected (via list or click): its source region shows 8 resize handles + move cursor inside. Drag = move; handle drag = resize (min 8×8 px original-space; clamped to page bounds on commit). Geometry commits on mouse-up as one undo step. Only source regions are editable this way — **the source side always has exactly one region**.

### 15.2 Create / delete

- **Create**: `Ctrl+B` or toolbar button arms draw mode (crosshair cursor); drag a rectangle on the image → new bubble: id per §5.3 D-03, kind `Speech`, status `Untranslated`, empty source/target, one part with region = source region. Its `Order`: inserted after the last existing bubble on that page whose region top-Y is above the new region's top-Y (ties: after); subsequent orders renumbered (D-17). Draw mode disarms after one create (hold Shift while releasing to stay armed).
- **Delete**: `Delete` with region selected, or context menu on the row → confirm dialog (shows source text preview). Removes the bubble element; undoable.

### 15.3 Target split

- `Alt+2`/`Alt+3` set part count 2/3; `Alt+1` merges back. Max 3 enforced.
- On increasing count: existing text stays in part 1; new parts empty. Default part regions: the source region divided into `n` equal **horizontal bands** top-to-bottom, part 1 topmost (D-18; owner-adjustable by drag).
- On merging to 1: texts joined with `\n` into part 1; part 1 region reset to the source region.
- Part regions are adjustable by drag/resize in the image pane whenever their bubble is selected (dashed rectangles, same handles).
- The TM unit remains the whole bubble; split geometry never affects TM content.

---

## 16. Character features

- **Master list editor** (window, reachable from project view and character box): grid of §6.3 fields; image cell supports **Ctrl+V paste from clipboard** (saved as PNG per §6.3) and file picker; delete row with confirm. Name uniqueness enforced.
- **Chapter list**: `<characters>` in content.xml; maintained automatically (adding a speaker adds the name; names are never auto-removed).
- Autocomplete field: prefix + substring matching over master names, ordered prefix-first; Enter selects the highlighted candidate or commits the literal text as a new name (§14.5).

---

## 17. Find/replace and statistics

### 17.1 Find/replace

- Scopes: current chapter (`Ctrl+F/H`) or all chapters of the project (`Ctrl+Shift+F/H`). Project scope loads chapters read-only in the background (packages opened one at a time; replace requires opening for write).
- Fields: search in Source / Target / Both (default Target for replace, Both for find). Options: case sensitive (off), regex (.NET `Regex`, off).
- Find: results list (chapter, page, bubble id, snippet with highlight); Enter/double-click navigates (opens the chapter if needed).
- Replace: replaces in target text only when the bubble is not `Locked`; each replacement sets status → `Draft` if it was `Translated`/`Reviewed` (edited-unconfirmed, Trados convention D-19), records undo entries. Replace-all reports count per chapter and saves modified non-open chapters immediately (atomic save).
- Source replace is allowed only in chapter scope with a warning banner (OCR fix use case).

### 17.2 Statistics

View reachable from project view; table per chapter + project total row:

| Column | Definition |
|---|---|
| Chapter | `meta/chapter` + file name |
| Bubbles | total bubble count |
| Untranslated / Draft / Translated / Reviewed / Locked | counts by status |
| Progress % | (Translated+Reviewed+Locked) / total, 1 decimal |
| MemoQ Count | §11 over source texts |
| MSWord Count | §11 over source texts |

Refresh on open; closed chapters are read via `PcmlDocument` without instantiating the editor. Export button: writes the table to XLSX (plain, header row + rows).

---

## 18. Chapter XLSX export (standard deliverable)

Native Core implementation with ClosedXML replicating the **business-proven layout** of PSDManga2XLSX/TiaomanOCR (they have no callable seam — D-20):

- One chapter → one worksheet (sheet name = `meta/chapter`, fallback file stem). Exporting multiple chapters into one workbook = one sheet each, project chapter order.
- Header row 1, background `#BFBFBF`, frozen: `A1=原件`, B1 empty, `C1=<source-language label>`, `D1=译文`, `E1=审校`, `F1=特殊要求备注`. The source label follows the project's **actual source language** (owner directive, D-33): lang class `zh` → `中文`, `ja` → `日文`, `ko` → `韩文`, `en` → `英文`, anything else → the BCP-47 tag verbatim. Column widths: A=60, C=30, D=30, E=15, F=15. C–F: wrap text, horizontal left, vertical center.
- Per page (in page order): the media image downscaled to width 425 px (SkiaSharp, JPEG re-encode quality 90), anchored at column A of the page's start row (`XLPicturePlacement.Move`).
- Per bubble (reading order): `C` = source text, `D` = `TargetJoined`, `F` = notes (if any). Row chosen by the cumulative row-height walk against `regionTopY * (425/pageWidth) * 0.75`, skipping rows whose C is occupied (same algorithm as the existing tools); working row heights `lines*15.0` during layout, then **all row heights cleared** at the end.
- Optional (default off, `settings.export.includeDataSheet`): a very-hidden sheet `_pigcomic` with full fidelity: columns `id, page, order, kind, character, status, x, y, width, height, source, target, notes` — one row per bubble (D-21).
- Guarantee: running the §11 algorithm over the column-C cell values of this export equals PigComic's chapter MemoQ/MSWord source counts. (For `zh`-source projects the header is still `中文`, so the original LanMangaCount tool also produces the same numbers unchanged; for other source languages counting happens inside PigComic — the legacy tool's `中文`-header detection would not find the column, which the owner has accepted.)

---

## 19. TM/TB exchange formats

All behind interfaces (§25.3) with native Core default implementations; both directions for both databases.

- **TMX 1.4b** (TM): export — `<header srclang>` = project source; one `<tu>` per entry with two `<tuv xml:lang>` / `<seg>`; `<prop type="x-character">`, `x-kind`, `x-chapter` for context fields. Import — match `tuv` languages by primary subtag against the project pair (mismatch → error listing found languages); inline tags (`<bpt>` etc.) are stripped with a summary warning; entries upserted per §7.1 rule (character context from `x-character` prop if present).
- **TBX** (TB, TBX-Basic dialect): export — `termEntry` per source term with `langSet` per language, `tig/term`; forbidden rows carry `<termNote type="administrativeStatus">deprecatedTerm-admn-sts</termNote>` on the target term (D-22). Import — accept TBX-Basic and TBX-Min; `deprecatedTerm-admn-sts`/`forbidden` markers set `forbidden=1`.
- **XLSX TM**: header row `原文 | 译文 | 角色 | 类别 | 章节` (import matches columns by exact header text, order-independent; missing optional columns OK; extra columns ignored). Export writes all five.
- **XLSX TB**: header row `原文 | 译文 | 禁止 | 备注`; on import any non-empty `禁止` cell → forbidden=1.
- After any import: rebuild `grams` (§7.6). Import report dialog: added / updated / skipped counts.

---

## 20. Tiled image rendering (mandatory design)

Requirement: long vertical strips (e.g. 1000 × 40,000 px) and large spreads scroll at 60 fps; **never load a full-size image into an Avalonia `Bitmap`**; memory bounded.

Architecture (validated by the M2 spike; deviations must be logged):

- **Pyramid**: level 0 = full resolution; level k halves each dimension; levels generated until max(width_k, height_k) ≤ 1024, minimum 2 downsampled levels always present. Tile size 512×512 (edge tiles smaller). Pure math (`TileKey(level,col,row)`, visible-set computation, level selection by zoom: smallest level whose scale ≥ current zoom) lives in `PigComic.Core/Imaging` with unit tests.
- **Cache**: LRU keyed by (pageId, TileKey) holding `SKImage` tiles; byte budget default 384 MB (configurable); eviction on insert.
- **Decode pipeline**: dedicated background worker(s) (2 threads) with a priority queue (priority = distance from viewport center; stale requests dropped). Level-k>0 tiles decode via `SKCodec` native scaled decode where supported; level 0 JPEG tiles via `SKCodecOptions.Subset` region decode. **PNG has no subset decode**: decode incrementally by scanline bands (512 rows at a time) emitting a tile row per band, never materializing the full bitmap; if incremental proves infeasible in the spike, fall back to one full decode → immediate slicing into cached tiles → drop, only for images whose full RGBA size ≤ 256 MB, and record the decision.
- **Draw**: a custom `ICustomDrawOperation` draws all resident tiles intersecting viewport ± 1 tile margin at the chosen level, upscaling coarser-level tiles as placeholders for missing fine tiles; missing everything → flat gray. Tile arrival invalidates the control. Overlays (§14.2) draw in the same operation above tiles.
- Page switch: cancel outstanding decodes for the old page; keep its tiles in cache (LRU handles eviction).

Acceptance (M2): synthetic 1000×40000 JPEG and PNG strips scroll end-to-end with sustained ≥55 fps (frame timing logged), process working set stays under budget + 300 MB overhead, and no full-size `Bitmap` allocation appears in the code path.

---

## 21. IME gate (blocking early task)

### 21.0 Composition rendering architecture (Avalonia 12.1.1)

Responsibility split — do not re-litigate this without checking upstream again (D-40, D-41):

| Data | Who provides it | Notes |
|---|---|---|
| Preedit string (`GCS_COMPSTR`) | Avalonia | via `TextInputMethodClient.SetPreeditText` |
| In-composition caret (`GCS_CURSORPOS`) | **Avalonia 12.1.0+** | via the `cursorPos` parameter (upstream PR #21632). PigComic must **not** read this from IMM32 — doing so was the old caret-stuck-at-0 bug, because `GCS_CURSORPOS` returns its value as the function's return value, not through the buffer. |
| Conversion clauses + attributes (`GCS_COMPCLAUSE` / `GCS_COMPATTR`) | **PigComic** | Not forwarded by any Avalonia release (upstream issue #21647; prototype PR #21648 unmerged). Captured **in-message** by `Ime/ImeMessageMonitor` — a `Win32Properties.AddWndProcHookCallback` hook that snapshots the fields flagged in `WM_IME_COMPOSITION`'s lParam synchronously on the message stack (the IMM32 contract: composition info may vanish after `ImmReleaseContext`; reading later broke ATOK). The client consumes the snapshot and never calls IMM32 itself. Windows-guarded. (PLAN M2.6; full rationale in `docs/IME_MODERN_COMPOSITION.md` §2.) |
| Display-attribute colors (TSF `TF_DISPLAYATTRIBUTE`) | **PigComic, M-TSF track** | Full-fidelity path: a per-control `ITextStoreACP` store renders TIP-specified colors (ATOK user palette, exact Notepad parity) and replaces the IMM32 path when enabled. Design: `docs/IME_MODERN_COMPOSITION.md` §4–5. |

Rendering path: `PartTextEditor` (installs `ImeTextBoxInputMethodClient` on the Tunnel route)
→ client combines Avalonia's caret with the captured clause snapshot into an `ImeComposition`
→ `ImeTextPresenter` renders per-segment in the **modern flavor** (§21.2). Any IME without
clause data (KO jamo, ZH pre-conversion) degrades to whole-preedit `Input` styling.
Composition never mutates committed document text.

### 21.1 Manual gate checklist

Before any editor UI milestone (M5+) starts, verify in a throwaway window (kept as a debug menu item) using **the same control type the target editor will use**:

Checklist (Windows, run manually with both Microsoft IME Japanese and Korean):
1. JA: type romaji → hiragana composition visible **at the caret** (not floating at screen corner); Space shows conversion candidates; Enter commits; no dropped/duplicated characters at fast typing.
2. JA: composition inside existing text (middle of string) inserts correctly.
3. KO: 2-beolsik jamo composition assembles syllables in place; committing/backspace behaves like Notepad.
4. Shift+Enter during composition does not break composition state.
5. IME on/off toggling (Alt+`~` / Han/Eng key) works focused in the control.
6. **Composition caret + modern clause rendering together** (§21.2): while composing, a caret is visible *inside* the preedit and tracks arrow-key movement (JA and ZH); the composition text renders colored per §21.2; after pressing Space to convert, the active henkan clause shows the colored background and it moves as the segment selection changes (test with **both MS-IME JA and ATOK**).

Record results in `docs/IME_REPORT.md` (pass/fail per item, Avalonia version, workarounds). The custom-client escalation (§21.0) has already been taken; **if items 1–5 still fail on it**, STOP and surface as an architecture decision (options: WPF interop island for the editor, or different framework) — do not proceed to M5. If only item 6's *clause-rendering* half fails while everything else passes, that is not an architecture failure: follow the M2.6 diagnostics branch (`docs/IME_MODERN_COMPOSITION.md` §5) and consult the owner (the caret half is upstream-supplied and must work).

### 21.2 Modern-flavor composition rendering (owner directive 2026-08-23, D-43)

**Control rule (normative).** Every editable text field in PigComic is a `PartTextEditor`.
It is the only control wired to the IME stack — `ImeTextBoxInputMethodClient` (installed on
the tunnel route), the `ImeTextPresenter` supplied by `PartTextEditorTheme`, and the
`ImeMessageMonitor` hook it attaches on entering the visual tree. A bare `TextBox` gets
Avalonia's stock rendering instead: no clause highlight, no modern palette. Any milestone
that adds a field where a user types — target parts, inline source editing, the character
box, notes, find/replace, the LLM prompt/memory editors — uses `PartTextEditor`
(`ConfirmOnEnter="False"` when Enter must reach a dialog's default button). Sanctioned
exceptions: the comparison `TextBox` in the IME gate window, and the debug spike's path
field. `PigComic.App.exe --smoke` enforces this.

The editor renders composition like Win11 Notepad / current Excel — **not** the legacy
thin/thick-underline flavor (memoQ, VSCode):

- Segment model: `ImeSegment(Start, Length, Kind)` with `Kind` ∈ `Input, Converted,
  ConvertedTarget, TargetNotConverted, InputError` — names mirror upstream Avalonia's
  `TextInputDecorationKind` (PR #20890) for future convergence (D-44). Segments derive from
  IMM32 `GCS_COMPATTR` runs today and TSF decorations on the M-TSF track.
- Style mapping via theme resources (values: the palette table in
  `docs/IME_MODERN_COMPOSITION.md` §6): composition text is **colored, not underlined**;
  the active henkan clause (`ConvertedTarget`) gets a **colored background**; the caret
  stays visible over every segment style.
- Fidelity rule (M-TSF): when the TIP specifies explicit `TF_CT_COLORREF` colors, render
  them verbatim (this is how ATOK users get their own configured colors); the theme
  palette applies only when the TIP declines (`TF_CT_NONE`/`TF_CT_SYSCOLOR`).

---

## 22. Undo/redo

- Per open chapter, one undo stack (`IUndoableAction { Do(); Undo(); string Label; }`), depth cap 200.
- Undoable: target text edits (coalesced per part until 1 s idle, focus change, or confirm), source text edits, status changes, kind/character/notes changes, region move/resize (one action per gesture), part count changes, bubble create/delete, order changes, find/replace replacements (replace-all in the open chapter = one composite action).
- Not undoable: TM writes (undoing a confirm reverts status/text but the TM entry remains — D-23), saves, LLM comments (cleared via its button instead).
- `Ctrl+Z/Ctrl+Y`. Stack cleared on chapter close; save does not clear it.

---

## 23. Journal and crash recovery

- File: `<file>.pcml.journal`, UTF-8 JSONL, sibling of the package.
- **Confirm-only granularity** (owner directive, D-34 — the program must stay light with no per-keystroke I/O): exactly one line is appended, flushed to disk (`FileStream.Flush(flushToDisk: true)`), per **confirm action** (Enter / Ctrl+Enter / Ctrl+Shift+Enter, §14.4), written before the TM upsert. Nothing else journals. Consequence, accepted: a crash loses at most unconfirmed work since the last save/autosave (drafts, region tweaks, notes) — never a confirmed segment.
- Line shape — a **full bubble snapshot**, sufficient to recreate the bubble from nothing:
  ```json
  {"seq":1,"utc":"2026-08-22T10:00:00Z","op":"confirm","bubble":{"id":"p0001-b0001","page":"p0001","order":1,
   "kind":"Speech","character":"ピッグ","status":"Translated","region":{"x":612,"y":480,"width":240,"height":310},
   "source":"おはよう　ございます！","parts":[{"index":1,"region":{"x":612,"y":480,"width":240,"height":310},"text":"早安！"}],
   "notes":""}}
  ```
  `seq` strictly increasing from 1 per journal file.
- On successful save (manual or autosave): delete the journal (a fresh one starts at seq 1 on the next confirm).
- On opening a chapter with a journal present: the last session crashed → dialog "Recover N confirmed segments from <time of last line>? [Recover] [Discard] [Cancel]". Recover: **upsert** each snapshot in order — if the bubble id exists, overwrite all its fields from the snapshot; if not (bubble was created and confirmed but never saved), create it on the referenced page (skip with a warning count if the page id is unknown). Then immediately save and delete the journal. Discard: delete the journal. Cancel: don't open.
- Corrupt trailing line (partial write): ignore the last line if it fails to parse; everything before it replays.

---

## 24. Accessibility / keyboard-only operation

The entire translate loop must work without a mouse: open project (list navigable by arrows + Enter), open chapter, navigate bubbles (`Ctrl+Up/Down`), edit target, split parts (`Alt+n`), set kind (`Ctrl+Shift+K` then arrows+Enter), set character (`Ctrl+Shift+C`, autocomplete), insert matches (`Ctrl+n`), confirm (`Enter`), QA (F8, results list arrow-navigable), save (`Ctrl+S`). Every dialog: Tab order defined, default/cancel buttons set, focus lands on the primary control. Region drawing is the one mouse-required feature (accepted — D-24).

---

## 25. Adapters (external code integration)

### 25.1 `ILlmClient` — the ONLY use of PigTranslate

```csharp
public interface ILlmClient {
    Task<string> CompleteAsync(LlmRequest request, IProgress<string>? progress, CancellationToken ct);
}
public sealed record LlmRequest(string Provider, string Model, string SystemPrompt, string UserContent, double Temperature = 0.2);
```

- `Provider`/`Model` come from `settings.llmQa` (§6.2); defaults `claude` / `claude-opus-5`.
- `PigComic.Core/Adapters/StubLlmClient` — default registration: throws `LlmNotConfiguredException`; the UI catches it and shows "LLM not configured" guidance. The app is fully functional without an LLM.
- `PigComic.Adapters.PigTranslate` (separate csproj, **not** in `PigComic.sln`; included in `PigComic.Full.sln`): wraps the existing `PigTranslate.Translator` class (`C:\Users\pig18\OneDrive\repos\Translator\Translator\PigTranslate.csproj`, referenced by relative path — owner adjusts). PigTranslate's API is synchronous; the adapter builds a single `Job` (`CachedSystem=[SystemPrompt]`, `User=[UserContent]`, `Model`, `Temperature`, `Think=false`) and dispatches on `Provider`: `claude` → `CreateClaudeJobSync`, `openai` → `CreateOpenAIJobSync`, `gemini` → `CreateGeminiJobSync` (unknown provider → error surfaced as a toast). Runs in `Task.Run`, honors `ct` by abandoning the task (PigTranslate has no cancellation), returns the raw text. Owner wires the DI registration; a task exists but the App must build and run with the stub alone.

### 25.2 Counting — ported, not adapted

`ICounter { CountResult Count(string text); }` with `CountResult(int MemoQCount, int MSWordCount)`. Implementation `LanMangaCounter` is a direct port (§11) — LanMangaCount is a WinForms drag-drop app with no CLI/library surface, and the algorithm is ~15 lines (D-14).

### 25.3 Exchange interfaces

```csharp
public interface ITmExchange {                     // one implementation per format: Tmx, Xlsx
    Task<ImportReport> ImportAsync(string path, TmStore tm, CancellationToken ct);
    Task ExportAsync(string path, TmStore tm, CancellationToken ct);
}
public interface ITbExchange { /* same shape for Tbx, Xlsx */ }
```

Native Core implementations per §19. If the owner later prefers routing through PigTranslate's readers (`ReadTMX`, `ReadTBX` — note PigTranslate has **no TMX/TBX writers**), they implement these interfaces in the adapter project; Core does not change.

### 25.4 Chapter XLSX export

```csharp
public interface IChapterXlsxExporter {
    // openMedia(fileName) -> readable stream of the page image inside /media
    // resizeToJpeg(imageStream, targetWidth) -> 425px JPEG stream; supplied by the App (SkiaSharp stays out of Core)
    Task ExportAsync(IReadOnlyList<Chapter> chapters, Func<Chapter, string, Stream> openMedia,
                     Func<Stream, int, Stream> resizeToJpeg, string outPath, ExportOptions options, CancellationToken ct);
}
```
Native ClosedXML implementation per §18. (PSDManga2XLSX/TiaomanOCR cannot be called: WinForms-bound, no seam — D-20.)

### 25.5 OCR / PSD ingestion

**Not part of this application.** Ingestion happens in the external `.pcml` generator (separate repo, §27.3). PigComic contains no OCR or PSD-reading code in v1.

---

## 26. Consolidated test tables

Normative xunit theories (put in the named test classes):

1. `NormalizerTests` — §7.2 rules + rows 1–19 of §7.7 asserting normalized-equality/inequality.
2. `TmMatchTests` — §7.7 full table including retrieval and scores.
3. `CounterTests` — §11.1 table.
4. `VisualLengthTests` — §12.1 table.
5. `QaRuleTests` — one positive + one negative case per §12 rule (construct chapters via `PcmlTestBuilder`).
6. `PcmlValidationTests` — §5.7 table.
7. `PcmlRoundTripTests` — golden-file round-trip incl. unknown elements/attributes/zip entries (byte-compare of `content.xml` after a no-op load/save must be semantically identical and preserve unknown nodes; a load→mutate one field→save must leave all other nodes untouched).
8. `JournalTests` — snapshot write on confirm, crash-simulate (truncate last line), replay equivalence: model-after-replay == model-at-confirm-time for every confirmed bubble, including a bubble created after the last save (recreated from its snapshot).

---

## 27. Deferred designs (design now, build later)

### 27.1 Page add/remove/reorder in the CAT (M11)

Schema already supports it: page order = element order; bubbles reference pages by id. Planned UI: page strip sidebar with drag reorder; add = file picker copying the image into `media/` (new `p####` id avoiding collisions); remove = only when no bubbles reference the page (else refuse with count). Page operations are not journaled (the journal is confirm-only, §23); they are protected by autosave.

### 27.2 PSD export (premium, M11+)

Per page: PSD canvas at **original** `page/@width×@height`; background layer = media image upscaled back to original size if it was downscaled; layer group `Typesetting` containing one text layer per target part, positioned at the part's region (original coordinates — this is why §5.6 fixes the coordinate space), text = part text, font = `settings.export.defaultFont`, size auto-fit to region height with line count from text. Writing PSD requires a PSD writer (the local `PsdParser` fork is read-oriented; Aspose.PSD or a minimal writer is an M11 decision — intentionally unresolved now).

### 27.3 `.pcml` generator (separate program, separate repo)

The generator ("PcmlGen") lives in its own repo: **https://github.com/pig1800/pcmlgenerator** (local: `C:\PIG\src\pcmlgenerator`), with its own SPEC/PLAN/DECISIONS. It merges the OCR path (from TiaomanOCR, upgraded to capture full paragraph bounding boxes) and the PSD path (from PSDManga2XLSX + PsdParser, upgraded to capture full layer rectangles and full-resolution pages) into one drag-and-drop tool emitting §5 packages with deterministic ids. That repo carries a manually-synced snapshot of §5 as `docs/PCML_FORMAT.md`; **this section (§5) remains authoritative** — any format change here must be propagated to that snapshot.
