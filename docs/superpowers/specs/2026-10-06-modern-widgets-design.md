# M31 — Modern widgets and sensors, polish (0.19.0) — design

**Date:** 2026-10-06 · **Status:** approved in brainstorm, awaiting written-spec review
**Decisions:** ADR-052 (temperatures from MSI Afterburner or HWiNFO, read-only)

## Goal

The user finds the widgets' text outdated: plain Segoe UI, heavy black shadows, a bold date number, an all-caps weekday,
11 px stats labels. They chose a new look in the visual companion — **style B ("dashboard tiles") with style A's thin
accent bars** — and new stats: **CPU usage, CPU temperature, GPU usage, GPU temperature, RAM used in GB** (no disk),
more sensors maybe later. Temperatures come from **MSI Afterburner** or **HWiNFO64** when one runs (read-only, no
admin), GPU temperature also from Windows itself. The clock stays digital (clock options later). The milestone also
carries the ten deferred minors of M29–M30, and ends with the user's own desktop set up with every feature for daily
use.

Hard rules stand: no admin rights, no driver, no injection, no new NuGet dependency; Win32 only in NeoFences.Shell
(CsWin32; a commented DllImport only where CsWin32 cannot express it); failures degrade one tile, never crash.

## 1. The look (all three widgets)

- Font: **Segoe UI Variable Display** for numbers and headings, **Segoe UI Variable Text** for small text (Windows 11;
  on Windows 10 WPF falls back to Segoe UI). Numbers use tabular figures where WPF offers them.
- No heavy black shadow: a soft one at most, so text still reads over bright wallpapers (the fence's glass is behind it).
- Colours from the fence's theme resources only (light and dark tones; no hard-coded white).
- **Clock:** time large, semibold, tight; the date line under it (when on) smaller and softer: "Monday, 5 October" in the
  user's language. 12/24-hour follows Windows, as today.
- **Date:** reads sideways — a big day number, beside it the weekday (normal case, not upper case) over "October 2026".
- **Stats:** tiles in two columns, each with a small spaced label (CPU, CPU TEMP, GPU, GPU TEMP, RAM), a big number with a
  small unit, and a thin accent bar under it (style A's gradient, from the theme's accent).
- Scaling stays as today: each widget scales uniformly to its size (1 × 1 to 4 × 4).

## 2. The stats

Five tiles, in this order: **CPU** (%), **CPU TEMP**, **GPU** (%), **GPU TEMP**, **RAM** (wide: "13.0 / 32 GB" — used
with one decimal, total rounded to whole GB; the bar is used/total). Disk (C:) is gone.

- Temperatures in °C by default; the stats widget's menu gets **Temperature in °F** (a per-widget option, stored like
  the clock's options). The temperature bars run 0–100 °C.
- A value NeoFences cannot read shows "—"; CPU TEMP then says **"needs Afterburner or HWiNFO"**.

### Where each value comes from

| Value | Source, in order |
|---|---|
| CPU % | Windows (as today) |
| RAM | Windows (`GlobalMemoryStatusEx`, as today) |
| GPU %, GPU TEMP | the main graphics card from Afterburner → HWiNFO → Windows (GPU % as today; temperature the way Task Manager reads it) |
| CPU TEMP | Afterburner → HWiNFO; none → "—" |

- **MSI Afterburner**: its shared memory `MAHMSharedMemory` (signature `MAHM`, version 2.x) — entries by name: "CPU
  temperature", "GPUn usage", "GPUn temperature", "GPUn memory usage". Verified on the user's PC (Afterburner running
  elevated, read by a normal process).
- **HWiNFO64**: its shared memory `Global\HWiNFO_SENS_SM2` (signature `HWiS`) — readings by type (temperature, usage)
  and label (the CPU package/die temperature; "GPU Temperature", "GPU Core Load"). The free version stops sharing after
  12 hours until it is switched on again in HWiNFO; NeoFences then falls back. Not installed on the user's PC: tested
  from a saved sample only.
- **The main graphics card**: the one using the most video memory (stable: the integrated GPU uses little), so the tiles
  never flip between two GPUs. Without a source, Windows' busiest adapter as today.
- Read only while a stats widget is shown and not in game mode (as today); a source that vanishes (app closed) is
  dropped at the next reading, and found again when it returns.

The layout parsing is pure Core code over a byte buffer (test-first with saved samples of both formats); the Shell maps
the shared memory read-only and passes the bytes. Opening a missing mapping is the normal case, not an error.

## 3. Polish (the deferred minors of M29–M30)

1. Welcome heading uses the body ink (`FenceText`), not the title ink.
2. The welcome's buttons wrap or shrink at the minimum fence size instead of clipping.
3. After a sort removes the welcome fence, an open Settings refreshes ("New games go to").
4. The first-start notice waits for the tray icon when its first add failed (shown from the retry).
5. One `UpdateEmptyHint` call in `ApplyFence`; `EndWelcomeIfFilled` refreshes only the affected window.
6. GUIDE §3: the game menu in the app's order, with Size.
7. GUIDE §14: link the GitHub Issues page.
8. `TrayHelp` / `GuideUrl` placed after the tray ids in order.
9. Screenshots retaken with the new widgets (`desktop`, `widgets`) and taller fences so bottom labels show (`games`,
   `panel`); nothing personal; a demo source path, never the user's profile path.
10. AP3 in the checklist matches the minimum-size behaviour of item 2.

## 4. The user's desktop with every feature

After the release (asked first; a snapshot taken first, so Restore snapshot undoes it; config written only while
NeoFences is stopped): a **Desk** fence with Clock (date on), Date and Stats; **Downloads** as a live Details panel of
the user's Downloads folder, newest first; an **auto-collect** rule bringing new apps, shortcuts and installers from the
Desktop and Downloads into **Apps**; bigger tiles for a few favourite games (2 × 2) and a Free layout on Games. The user
picks the corner and the games when asked.

## 5. Docs

GUIDE §5 Widgets (the new stats, the sources, °F) and §14 (CPU temperature needs Afterburner or HWiNFO); ADR-052;
ARCHITECTURE 0.19.0; FEATURES row; TEST-CHECKLIST AQ; `docs/research/m31-widgets.md` (the formats, the user's
Afterburner sensor list).

## 6. Testing

- Core (xUnit, test-first): the Afterburner and HWiNFO parsers on saved byte samples (signature/version checks, a short or
  garbled buffer → nothing, the GPU choice by video memory, CPU temperature found or absent); °C → °F; the RAM text
  ("13.0 / 32 GB"); the tile list and its "—" cases; the °F option round-trips.
- Live check (asked first): the restyled widgets in light and dark tones; Stats with Afterburner running (CPU TEMP and
  GPU TEMP match Afterburner's own values), °F; the no-source case (CPU TEMP "—", GPU TEMP from Windows) by the tests, and live only if the user closes Afterburner themselves (NeoFences never closes another app); the
  polish items; new screenshots.

## Out of scope

Clock options (analog face, time zones, formats); a sensor picker or more tiles (later, from the same sources); HWiNFO
tested live (not installed here).
