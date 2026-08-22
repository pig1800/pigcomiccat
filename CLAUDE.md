# PigComic

Desktop CAT tool for comic/manga translation. **Read `docs/SPEC.md` before writing any code** — it is the single source of truth. Work strictly from the task list in `docs/PLAN.md`, one task per session, in order. If the spec is silent on something you need, do NOT guess: add it to `docs/OPEN_QUESTIONS.md` and stop.

## Stack
- .NET 8, C# (nullable enabled, implicit usings, file-scoped namespaces, 4-space indent).
- `src/PigComic.Core` — domain, `.pcml` I/O, TM/TB (SQLite via Microsoft.Data.Sqlite), QA, counting, export (ClosedXML). **No Avalonia/SkiaSharp references, no UI types, ever.**
- `src/PigComic.App` — Avalonia 11, MVVM with CommunityToolkit.Mvvm, DI via Microsoft.Extensions.DependencyInjection. No business logic in view models beyond delegating to Core.
- `tests/PigComic.Core.Tests` — xunit. Every Core feature ships with tests in the same task/milestone; the normative test tables are in SPEC §7.7, §11.1, §12.1, §26.

## Hard rules
- Never leave a half-written `.pcml`: all package writes go through the atomic save path (SPEC §5.5).
- `content.xml` round-trip must preserve unknown elements/attributes/zip entries (SPEC §5.8 — the XDocument is the model).
- Bubble IDs are never renumbered or reused. `Order` may be renumbered; ids may not.
- TM writes happen only on confirm; never auto-fill or auto-propagate a translation (repetitions are offered via popup only, SPEC §10).
- No LLM pretranslation feature. LLM = on-demand QA comments only (SPEC §13) via `ILlmClient`; the app must fully work with the stub.
- M2 is a gate: no M5+ editor work until `docs/IME_REPORT.md` records all IME checks as PASS.
- Never load a full-size page image into an Avalonia `Bitmap` (tiled rendering, SPEC §20).
- Don't weaken/delete existing tests to make a task pass. `dotnet build PigComic.sln` + `dotnet test` must be green at the end of every task.
- `PigComic.sln` must build without any sibling repos; `PigComic.Full.sln` (adds the PigTranslate adapter) may require them.

## Verify
```
dotnet build PigComic.sln
dotnet test
dotnet run --project src/PigComic.App
```
