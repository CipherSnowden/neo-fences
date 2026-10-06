# M37 — Performance at scale (0.24.0): measurements and calls

**Date:** 2026-10-06 · **Spec:** `docs/superpowers/specs/2026-10-06-performance-at-scale-design.md` · **Decision:** ADR-058
**Plan:** `docs/superpowers/plans/2026-10-06-m37-performance-at-scale.md`

## How it was measured

`tools/perf/measure-scale.ps1` on a backed-up copy of the owner's data (owner's OK each time; data, startup entry and the
installed copy restored after each run): the owner's four fences plus generated ones to 50 fences and 500 items (programs,
pictures, folders, sounds, Start apps, websites; one 200-item and one 100-item fence). A cold start (no icon cache, frame
statistics on: scroll the big fence, drag and resize the medium one), then a warm start (CPU over 60 s idle and behind a
full-screen window). Owner's PC, one 1920 × 1080 monitor at 100 %.

## Six rounds on the prototype

| Round | Change measured | Fences shown (cold) | Icons settled (cold) | Requests | 200-item fence | Scroll (slow / frames, worst) | Drag / resize worst |
|---|---|---|---|---|---|---|---|
| 1 | baseline (marks, idle timers; no cache) | 3.6 s | 11.1 s | 4,967 | — | — | — |
| 2 | icon cache, visible first, fences first | 3.3 s | 3.6 s | 2,487 | — | (not read) | 604 ms |
| 3 | + one shell ask per key per run | 3.3 s | 3.4 s | 503 | 242 ms | 12 / 268, 49 ms | 420 ms |
| 4 | + labels drawn once (BitmapCache) | 3.4 s | 3.4 s | 503 | 241 ms | 4 / 271, 42 ms | 370 ms |
| 5 | + cover tile only for covers | 3.1 s | 3.1 s | 503 | 132 ms | 7 / 448, 84 ms | 215 ms |
| 6 | (same build, again) | 3.0 s | 3.0 s | 503 | 160 ms | 12 / 389, 85 ms | 264 ms |

Warm start (rounds 4–6): fences and icons together in 2.75–2.96 s, 272 of 503 icons from the cache (the rest are targets
without a stamp or loaded behind their cached icon). CPU over 60 s idle: 0.00–0.03 s.

**Where the start goes now:** ~1.1–1.3 s creating 50 windows, ~1.6 s showing them (each lays out its items); icons are no
longer the bottleneck.

**Ready-to-run** (two self-contained publishes, start only): 3.87 s vs 4.17 s fences shown — 7 %, below the spec's 20 %:
dropped (the installer would have grown only ~2 MB).

**Virtualization:** the 200-item fence came down from 242 ms to 130–160 ms with the lighter items — at the spec's 150 ms
line. The owner chose to plan without it (after 1.0); scrolling a 200-item fence keeps ~3 % frames over 33 ms.

**Game mode:** the script's fake game (a borderless window, then one over the whole screen) never engaged game mode —
Windows did not count it as a full-screen app in the foreground. The "game mode" CPU numbers of rounds 1–6 are "behind a
covering window" (0.00–1.16 s). Game mode is checked in the live check with a real full-screen app.

## Findings on the way

- Each item asked for its icon about ten times at start (every layout pass, target-check result and refresh): the session
  layer (one shell ask per key per run) took the requests from 4,967 to 503.
- Two cache index saves could run at once and collide on `index.json.tmp`: one save at a time.
- On a warm start the icons settled before the fences were drawn: the mark is logged with "fences shown" then.
- A log NeoFences holds open keeps an old LastWriteTime: the script reads the newest daily logs instead.

## Calls made while prototyping

- The timing marks and their reader live together in Core (`PerfLog`), loaded by the script.
- Names are stored with every icon; an unreachable target's generic icon is not cached; Refresh and special icons go past
  the caches; "can be seen" is the item's container inside the list's view.
- The hover poll and the auto-collect timer stop in game mode and while hidden or paused, like the widget timer.
- Labels keep their shadow and are cached as bitmaps instead of a new drawn halo; the cover tile is its own template.

## Live check

(Filled in by the measurement and live check.)
