# Modern IME Composition Rendering — Investigation & Plan

**Date:** 2026-08-23 · **Owner directive:** the target editor must render Japanese IME composition in the *modern* Windows flavor (Win11 Notepad / current Excel): colored composition text + colored background on the active henkan clause — not the legacy thin/thick-underline flavor (memoQ, VSCode). No other CAT tool does this. Also to fix: with ATOK, the current build shows **no clause information at all**.

This document is the normative design + plan for that work. It supersedes the highlight-related parts of `IME_HANDOFF.md`. Findings below were verified 2026-08-23 against Microsoft docs, Chromium/Firefox/Windows-Terminal/WPF sources, and Avalonia upstream; key URLs inline.

---

## 1. The two rendering worlds (why apps look different)

Every modern IME on Windows 10/11 (new MS-IME, ATOK Passport, Pinyin) is a **TSF TIP** — IMM32-only IMEs are blocked by modern Windows ([MS Learn](https://learn.microsoft.com/en-us/windows/apps/design/input/input-method-editor-requirements)). Apps see them through one of two doors:

| Door | Who uses it | What the app receives | Typical look |
|---|---|---|---|
| **IMM32 via CUAS** (compat layer, always on since Vista) | PigComic today; Chromium until ~2020; classic editors | `WM_IME_COMPOSITION` + `ImmGetCompositionString`: `GCS_COMPSTR/CURSORPOS/COMPCLAUSE/COMPATTR` — clause boundaries + per-char `ATTR_*` bytes, **no colors** | App maps ATTR→underline thin/thick (or our reverse-video) — the "classic flavor" |
| **TSF text store** (`ITextStoreACP`) | Win11 Notepad (RichEdit), Office, Windows Terminal, WPF, Chromium/Firefox now | Per-range **`TF_DISPLAYATTRIBUTE`**: attr kind + `crText`/`crBk`/`crLine` colors + `lsStyle` underline style — the TIP *specifies its own styling* | Render the TIP's colors faithfully → the "modern flavor" (blue text, aqua target background) |

Three important nuances discovered:
- **The classic look in VSCode/WPF is a choice, not a limitation**: Chromium reads full display attributes but deliberately ignores `crBk` (`tsf_text_store.cc` `GetStyle`); WPF reads them too but its renderer draws only underlines (`TextServicesDisplayAttribute.Apply()` is literally `#if NOT_YET`; `CompositionAdorner` draws lines only). **Windows Terminal renders all three colors** (`Implementation.cpp` `_doCompositionUpdate` → `SetForeground/SetBackground/SetUnderlineColor` from `TF_DA_COLOR`) — it is our reference for faithful rendering.
- **The modern look is *also* partly app-side**: a live display-attribute dump on this machine (§6) proves the Win11 MS Japanese IME ships **no colors** — only underline styles. Notepad's blue-text/aqua-target look is Notepad's own kind-keyed palette. So the modern flavor = *render TIP colors when given, apply our Notepad-style palette when the TIP declines* — achievable over **both** doors (IMM32 attr kinds now, TSF later).
- **ATOK's clause styling is user-configurable** (表示色カスタマイズ) and arrives as explicit `TF_CT_COLORREF` display attributes in TSF apps — a TSF store gives ATOK users *their own configured colors* for free, which no IMM32 mapping can (IMM32 carries kinds, never colors).

## 2. Why ATOK shows nothing in the current build (root-cause analysis)

Verdict from the evidence: **IMM32/CUAS is NOT a dead end — our read timing/context is the prime suspect.**

- CUAS does synthesize clause data for TSF TIPs: Chromium consumed `GCS_COMPCLAUSE`/`GCS_COMPATTR` from these exact IMEs for a decade over IMM32 ([imm32_manager.cc](https://source.chromium.org/chromium/chromium/src/+/main:ui/base/ime/win/imm32_manager.cc)), and there are **zero** reports of ATOK returning empty clause data via CUAS. (The famous ATOK-in-Chrome clause complaints date from Chrome's *switch to TSF*, and are about styling, not missing data.)
- The IMM32 contract is message-synchronous: *"An application calls this function in response to the WM_IME_COMPOSITION message… **The IMM removes the information when the application calls ImmReleaseContext**"* ([ImmGetCompositionStringW](https://learn.microsoft.com/en-us/windows/win32/api/imm/nf-imm-immgetcompositionstringw)). Chromium/Firefox read clause/attr **inside the message handler**, gated on the `lParam` GCS bits.
- Our code reads clause data from `ImeTextBoxInputMethodClient.SetPreeditText` — *after* Avalonia's `Imm32InputMethod` has already done its own `ImmGetContext`/…/`ImmReleaseContext` cycle for the same message ([Imm32InputMethod.cs @12.1.1](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Windows/Avalonia.Win32/Input/Imm32InputMethod.cs)). Off-contract; per-TIP CUAS behavior can legitimately punish it — MS-IME happening to tolerate it while ATOK returns nothing fits perfectly.
- Extra ATOK fragility (Firefox comment, [IMMHandler.cpp](https://searchfox.org/mozilla-central/source/widget/windows/IMMHandler.cpp)): ATOK sends `WM_IME_COMPOSITION` before `WM_IME_STARTCOMPOSITION`, and poking IMM32 APIs at the wrong moment "will sometimes fail to initialize its state". Also, some TIPs send updates where the lParam bits must be obeyed per-message (only read what changed; retain the rest).

**Fix direction (Phase A):** capture clause data *in the message*, from a `Win32Properties.AddWndProcHookCallback` hook — verified present in Avalonia 12.1.1 (`Avalonia.Controls.Win32Properties`), and hooks run **before** all Avalonia handling (`WindowImpl.WndProcMessageHandler` calls the hook first; `handled=true` would swallow — we observe only). Cache a snapshot; the IME client consumes the cache instead of re-querying.

## 3. Upstream Avalonia status (strategic context)

- **PR #20890 "[WIP] Structured Text Input"** (Gillibald — Avalonia's text-input author; open, actively updated, zero reviews, no merge timeline): contains a **complete TSF stack** — `TsfTextStore.cs` (~1,269 lines, `ITextStoreACP2` + composition sink + edit sink, full display-attribute chain via `TrackProperties(GUID_PROP_ATTRIBUTE)` → `ITfCategoryMgr` → `ITfDisplayAttributeMgr`), `TsfThreadManager.cs`, `tsf.idl` (MicroCom), gated behind experimental `Win32PlatformOptions.UseTsfTextInput` (default false). Its contract type is exactly our shape target:
  ```csharp
  readonly record struct TextInputDecoration(ITextRange Range, TextInputDecorationKind Kind,
      Color? Foreground = null, Color? Background = null, TextInputUnderline Underline = TextInputUnderline.None);
  // Kind: Input, Converted, ConvertedTarget, TargetNotConverted, InputError, ReconversionTarget
  // Underline: None, Single, Dotted, Dashed, Wavy, Thick
  ```
  Note: upstream's own TextBox renderer currently **drops `Foreground`** (renders background + underline only) — our presenter must keep it to get the full modern look.
- **PR #21648** (clause segments over IMM32, `SetPreeditText(text, cursorPos, IReadOnlyList<TextInputMethodPreeditSegment>)`) remains unmerged; classic-flavor only.
- Consequence: whatever we build must be (a) shape-compatible with `TextInputDecorationKind`, and (b) gated so it stands down if/when `UseTsfTextInput` ships upstream.

## 4. TSF feasibility (for the full-fidelity phase)

Verified facts that bound the risk:
- **CsWin32 generates working TSF interop on this machine**: a net8.0 test project with `ITextStoreACP`, `ITfThreadMgr`, `ITfDisplayAttributeMgr`, etc. in `NativeMethods.txt` builds clean, and a C# class can implement the generated interfaces (classic ComImport interop; derived interfaces auto-flattened — the exact trap WPF hand-fixed).
- **Keystroke plumbing is optional**: Chromium and Windows Terminal implement neither `ITfKeystrokeMgr` forwarding nor `ITfMessagePump` — composition works once the thread manager is active and a document has focus. (WPF/Firefox do forward keys; that's the hardening fallback if a TIP misbehaves.)
- **TS_E_NOLAYOUT** — the most notorious TSF pitfall (candidate window placement) — is largely avoidable for us: our editor lays out synchronously, so `GetTextExt` can always answer with real rectangles + `OnLayoutChange` after every composition repaint. (Firefox's per-TIP hack machine exists because Gecko's layout is async.)
- **Reference implementations**, smallest first: Windows Terminal `src/tsf` (~1,100 lines, modern colored rendering — our template), MS sample `tsfapp/textstor.cpp` (1,369 lines, method-by-method ACP reference), Chromium `tsf_text_store.cc` (lock queue + display-attr walk), WPF `TextStore.cs` (5,236 lines, MIT — full managed ACP + `UnsafeNativeMethodsTextServices.cs` 2,912 lines of copyable interop).
- **Avalonia coexistence seam** (verified against 12.1.1 source): set `InputMethod.SetIsInputMethodEnabled(control, false)` → Avalonia's manager sets `Client = null` → `DisableImm()` → `ImmAssociateContext(hwnd, 0)` and goes **fully quiet** (no candidate-window calls, no Reset, guarded on null client). Then `ITfThreadMgr` + `SetFocus/AssociateFocus` on our document when the control focuses; WndProc hook swallows stray `WM_IME_*`/`WM_CHAR` as belt-and-braces. Two ordering rules to copy from #20890: attach store client **before** `AssociateFocus`; on detach, clear association **before** the client.
- Effort estimate (from the codebase study): skeleton 3–5 days; display attributes + colored rendering 2–3 days; hardening (locks, notifications, multi-IME testing) 1.5–3 weeks. **Total ≈ 3–5 weeks**, ~1,500–2,500 lines. Fallback if ACP bogs down: Terminal-style `ITfContextOwner` (~800 lines, still colored) at the cost of voice-typing/handwriting integration.

## 5. Architecture decision (D-43)

Three phases; A+B are small and immediate, C is the strategic differentiator:

- **Phase A — in-message IMM32 clause capture + diagnostics. ✅ IMPLEMENTED 2026-08-24 (PLAN M2.6).** WndProc hook records `WM_IME_COMPOSITION` snapshots (lParam-gated reads of COMPSTR/COMPCLAUSE/COMPATTR on the message stack); the IME client consumes the snapshot. A diagnostics toggle in `ImeTestWindow` logs every snapshot (lParam bits + byte counts + values) to a file so one owner session per IME settles what each TIP provides. *Expected outcome: clause data starts flowing for ATOK and MS-IME (Chromium-proven path).* If diagnostics prove some TIP truly sends nothing even in-message, that TIP is IMM32-dead and Phase C covers it.
- **Phase B — modern-flavor segment rendering. ✅ IMPLEMENTED 2026-08-24 (PLAN M2.7).** Replace the reverse-video look with a segment model shaped after `TextInputDecorationKind`; presenter renders per-kind foreground/background/underline from theme resources (palette in §6): composition text colored (no underline in modern mode), active henkan clause = colored background. Works identically over IMM32 attr runs now and TSF decorations later. KO jamo / no-clause degrade: whole preedit = Input style.
- **Phase C — TSF text store (full fidelity). NOT STARTED** (M-TSF track; needs owner go-ahead and the Phase A diagnostics). Own C# implementation for `PartTextEditor` only: CsWin32 interop; Terminal/Chromium/#20890 as references; render **TIP-specified colors when given** (`TF_CT_COLORREF`), theme palette when `TF_CT_NONE`; Avalonia suppression per §4. Behind an app setting (default on once it passes the gate; instantly revertible to the IMM32 path). Gated on Avalonia version: stand down when upstream `UseTsfTextInput` ships with decoration rendering. This is what makes ATOK show the user's own configured colors, enables future reconversion (`ITfFnReconversion`, upstream #21948), and is the "no other CAT has this" differentiator.

Priority rule: **A and B unblock the M2.5 gate** (item 6 in its modern form) and M5 proceeds on the IMM32 path. C is scheduled after M6 (editor + regions solid) — **unless** Phase A diagnostics show ATOK provides nothing via IMM32 even in-message, in which case C jumps ahead of M5 for the owner's daily-driver IME.

## 6. Modern-flavor rendering spec (Phase B palette)

**Ground truth (live `ITfDisplayAttributeMgr` dump on this machine, 2026-08-23; dump tool kept at `C:\PIG\_rtmp\DumpDA.exe`, rerunnable):** the Win11 **Microsoft Japanese IME registers NO colors at all** — every `crText/crBk/crLine` is `TF_CT_NONE`; it specifies only underline styles (INPUT = squiggle, CONVERTED = solid, TARGET_* = solid+bold, matching Google 日本語入力's philosophy). MS Pinyin/Korean TIPs are structurally identical. **The Win11-Notepad "blue text + aqua background" look is therefore app-side styling, not TIP data** — and the palette below matches Microsoft's own in-house color language (their Table-Driven TIP registers `#0067CE` blue input text and white-on-cyan targets). **ATOK does ship explicit `TF_CT_COLORREF` colors** (its user-configurable 表示色カスタマイズ), which is exactly what the fidelity rule preserves.

Rendering algorithm (adopt Windows Terminal's sanity rule):
1. TIP supplies colors and `crText`/`crBk` types are consistent → **render the TIP's colors verbatim** (`TF_CT_SYSCOLOR` → `GetSysColor(nIndex)`, but remap `COLOR_HIGHLIGHT/HIGHLIGHTTEXT` to the theme's selection tokens in dark mode; GetSysColor is not dark-aware). This path serves ATOK and third-party Chinese IMEs.
2. Colors are `TF_CT_NONE` (all Microsoft + Google IMEs) → apply the palette below keyed on the attr kind. This is what Notepad does — a legitimate convention, not a hack.
3. A meaningful TIP `lsStyle` beyond the stock pattern (e.g. ATOK's "下線あり") renders *in addition to* colors; for the color-free MS IMEs the modern flavor deliberately replaces squiggle/solid/bold underlines with color — the same trade Notepad makes.
4. Unknown/invalid attribute → thin dashed underline, no colors (Terminal/Firefox precedent).

Palette (theme resources in `PartTextEditorTheme.axaml`, overridable; IMM32 `ATTR_*` map 1:1 to kinds):

| Kind | Light: fg / bg / underline | Dark: fg / bg / underline | Notes |
|---|---|---|---|
| Input | `#0000FF` / — / none | `#7C7CFF` / — / none | **Owner's call 2026-08-24: orthodox pure blue.** Dark keeps hue 240° and only raises lightness (pure blue on dark is ~2.3:1; this is ~5:1) |
| Converted | `#0000FF` / — / none | `#7C7CFF` / — / none | Notepad colors the whole preedit uniformly; only the target stands out |
| ConvertedTarget | normal text / `#A5E8FA` / none | `#FFFFFF` / `#0E6E8C` / none | **Never white-on-aqua** (1.25:1 contrast); black-on-`#A5E8FA` ≈ 16:1 |
| TargetNotConverted | same as ConvertedTarget | same | The MS TIP registers identical attributes for both |
| InputError | Input fg / — / squiggle `#C42B1C` | Input fg / — / squiggle `#FF99A4` | Squiggle style is TIP-specified; color is ours |

Contrast/caret rules: text on the target background must stay ≥ 4.5:1 in both themes; the caret must remain visible over every segment style. Dark-mode Notepad behavior is undocumented — our dark tokens are original and owner-tunable.

The composition blue was retuned from Microsoft's in-house `#0067CE` to plain `#0000FF` on the owner's instruction (2026-08-24): it read as too dark. The light and dark values are a hue-matched pair — retune both together, in `PartTextEditorTheme.axaml`'s `ThemeDictionaries`.

## 7. Risks

| Risk | Phase | Mitigation |
|---|---|---|
| ATOK still empty even with in-message reads | A | Diagnostics log proves it definitively; escalate Phase C earlier. Cheap A/B: Windows' "use previous version of MS IME" toggle. |
| lParam bits unreliable (ATOK sends WM_IME_COMPOSITION before STARTCOMPOSITION; lParam==0 messages) | A | Follow Gecko: treat lParam==0 as "read everything"; retain unchanged fields between messages; never call IMM APIs outside the message. |
| TSF store destabilizes typing (locks/notifications) | C | Setting-gated, instant fallback to IMM32 path; harden against the 5 documented pitfall classes (lock queue, notify-after-unlock, GetTextExt-always-answers, flattened COM vtables, one-docmgr-per-HWND focus switching). |
| Upstream #20890 merges mid-flight | C | Segment/decoration types kept shape-identical; version gate stands our store down; worst case we delete code. |
| Avalonia's IMM path fights TSF | C | Verified quiet path: `IsInputMethodEnabled=false` detaches HIMC and silences all Imm calls; hook swallows strays. |

## 8. Source index (for the executing model)

- Chromium: `ui/base/ime/win/tsf_text_store.cc` (lock queue §620-706, display-attr walk §1201-1290, GetTextExt §359-470), `tsf_bridge.cc` (thread mgr + per-field docmgr focus), `imm32_manager.cc` (in-message IMM32 reads).
- Windows Terminal: `src/tsf/Implementation.cpp` (`_doCompositionUpdate` §592-815 — colored rendering template).
- WPF (MIT): `PresentationFramework/System/Windows/Documents/TextStore.cs`, `Shared/MS/Win32/UnsafeNativeMethodsTextServices.cs` (copyable interop).
- MS sample: `Windows-classic-samples/.../winui/tsf/tsfapp/textstor.cpp`.
- Avalonia: `src/Windows/Avalonia.Win32/Input/Imm32InputMethod.cs` @12.1.1; `Avalonia.Controls.Win32Properties` (WndProc hook); PR #20890 (TSF stack + `TextInputDecoration`), PR #21648 (IMM segments), issues #21647/#21948.
- Docs: ImmGetCompositionStringW remarks (message-synchronous contract); "Using Display Attributes" (TSF); ITfThreadMgr::AssociateFocus.
