# PigComic — Tiled Rendering Spike Report (M2.4)

**Goal (SPEC §20):** long vertical strips (1000×40000) scroll at ≥55 fps; working
set < 900 MB; no full-size `Bitmap` allocation in the render path.

**How to run:** Debug menu → "Spike: tiled canvas" → either "Generate strip (+)"
(1 click, writes `bin/Debug/net8.0/strips/strip.jpg|png`) or Load an existing
strip. Scroll fast with the wheel; Ctrl+wheel zooms at the cursor; watch the FPS
overlay (yellow, top-left) and Task Manager / Perf counter for memory.

## Spike findings (2026-08-22)

- **Limit one:** the decoded image lives as one full RGBA `SKBitmap` per open
  page (160 MB for 1000×40000) inside `TileDecoder`, tiles are sliced from it.
  This is the **spec-sanctioned fallback** (see DECISIONS D-37): SkiaSharp 3.116
  returns `Unimplemented` for JPEG subset decode (`SKCodecOptions.Subset`) and
  for PNG `StartScanlineDecode`/scanline reads on Windows, so true region decode
  is not available in this build. The guard refuses images whose full RGBA size
  exceeds 256 MB.
- Limit two checkout: tile→Avalonia conversion copies once per tile
  (Rgba8888→Bgra8888); scroll performance is bound by drawing count, not decode,
  once the visible set is cached.

## Acceptance (owner-run; record real numbers)

| Check | Expected | Result |
|---|---|---|
| Scroll 1000×40000 JPEG top-to-bottom fast | ≥55 fps sustained | ⬜ |
| Scroll PNG same | ≥55 fps sustained | ⬜ |
| Sustained working set | < 900 MB (task manager) | ⬜ |
| Ctrl+wheel zoom in/out | smooth, no full-frame pops | ⬜ |
| Fit width (Ctrl+0) | whole strip visible | ⬜ |

*Entries above are recorded by the owner after running this spike.*