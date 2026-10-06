# M37 — Performance at scale (0.24.0) — design

**Date:** 2026-10-06 · **Status:** approved in brainstorm, awaiting written-spec review
**Decisions:** ADR-058 (measure first; an icon and name cache; visible-first loading; timers only while needed; ready-to-run
only if it pays)
**Source:** `docs/research/v1-readiness.md` (Performance: no icon cache; every fence item built; a shadow per label on
layered windows; a per-second widget timer that never stops; untested at 500 items / 50 fences).
**Owner's picks:** big setups stay smooth, faster start after boot, truly idle — not memory. Approach: measure, then fix.

## Goal

With about 500 items in 50 fences, NeoFences starts, scrolls, drags and resizes like today's small setup; after a sign-in
or a power cut, fences and their icons are on screen sooner; while nothing can be seen (a game, hidden, paused) NeoFences
does no work at all. Targets on the owner's PC: fences on screen within ~1 s of start; icons within ~2 s with a warm cache;
no stutter scrolling a 200-item fence; 0.00 s NeoFences CPU over 60 s in game mode and while hidden. Hard rules stand:
user files never touched; no new NuGet dependency; Core pure and test-first; failures logged, never a crash.

## 1. Measuring (built first, kept for later releases)

- **A test setup script** (PowerShell, like the live checks): backs up the data folder, writes a 500-item / 50-fence setup
  with real targets (programs, documents, folders, websites, a few game covers and widgets), runs, restores.
- **Timing marks in the log**: "fences shown" (every window placed) and "icons settled" (no icon request waiting), in ms
  since the process started. A Core helper reads them out of a log for the report.
- **Measured**: cold start (after sign-in or a power cut) and warm start (cache present); scrolling a 200-item fence;
  dragging and resizing a 100-item fence; NeoFences CPU over 60 s idle, in game mode and while quick-hidden.
- **Before and after** each change, in the M37 research note.

## 2. Truly idle

- **The widget timer** runs only while a widget can be seen: stopped in game mode, while hidden or paused, and while every
  widget is rolled up or on a background tab; started again when one shows (today it ticks every second and returns).
- **Every DispatcherTimer** is reviewed: each either runs only while needed or is event-driven / one-shot, noted in code.
  The 5 s game-mode poll stays (it is how a game is noticed).

## 3. Icon and name cache

- **Where**: `%LOCALAPPDATA%\NeoFences\cache\icons\`, NeoFences' own folder; it never holds or touches a user's file.
- **What**: each item's icon at the pixel size its fence draws it, as a small PNG, and in an index the item's display name
  and a **stamp** of its source: the file's (or own icon file's) last-write time and length; none for Start apps,
  `shell:` / `::{GUID}` items and websites.
- **At start** every item shows its cached icon and name at once (decoded off the UI thread). The fresh load then runs
  behind it — always for items without a stamp, only when the stamp changed for files — and replaces the picture only if it
  differs. An item with no entry loads as today.
- **Pruning**: entries unused for 30 days go; the cache stays under ~64 MB, oldest first. A damaged index is logged once and
  started over; it never delays the start.
- The key, the stamp check, pruning and the index's repair are Core, test-first (like the covers index).

## 4. Visible first

- The icon queue has an order: (1) items visible now, (2) the rest of the shown fences, (3) background tabs, rolled-up
  fences and items scrolled out of view — their fresh load waits until they are shown or nothing else waits (their cached
  icon shows meanwhile).
- Scrolling, a tab switch or an unroll moves the newly visible items to the front.

## 5. Faster start

- **Ready-to-run**: the release is published pre-compiled for x64. The installer grows (likely +20–30 MB; updates stay
  deltas). Kept only if it cuts cold start by at least 20 % in the measurement; otherwise dropped and noted.
- **Fences first**: the game-library scan, cover lookups, file watching and target checks, the wallpaper colour read and the
  update check wait until "fences shown" (some already do; all will).

## 6. Fixes that depend on the numbers (checkpoints; the owner is told the result either way)

- **Label shadows**: if scrolling a 200-item fence stutters (frames over 33 ms), labels switch from a per-label shadow effect
  to a cheaper drawn halo that looks the same.
- **Virtualization**: if building a 200-item fence still takes over 150 ms, or scrolling still stutters after the cache, only
  the visible rows of a Flow fence are built (Free layouts stay as they are). Otherwise it waits until after 1.0.

## Testing

- **Core, test-first**: the cache key (target, own icon, pixel size), the stamp check (changed / unchanged / no stamp), pruning
  (age, size cap, oldest first), the index's repair (damaged, odd entries, a file name that leaves the folder), the timing
  report from log lines.
- **Measurement** before and after with the 500 / 50 setup; checklist section **AW**; a live check on a copy of the owner's data.

## Docs

ADR-058; GUIDE (only if something visible changes, e.g. the cache folder under Troubleshooting); architecture, features,
checklist AW, research note with the numbers.

## Out of scope

Memory work (not picked), trimming, native AOT, virtualizing Free layouts, a settings switch for the cache.
