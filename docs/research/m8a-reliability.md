# M8a — v1.1 reliability: results

**Date:** 2026-10-03 · **Machine:** Windows 11 Pro 25H2 (26200), 1920×1080 at 100 %, Takeover on, NeoFences 1.0 installed.
**Build:** `m8a-reliability` (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section R.

## Fence corners (user report, 2026-10-03)

The user's screenshot showed fence corners "sticking out" of the rounded border. An 8× zoom of each corner, taken with
`corner-zoom.ps1` and a 6 px margin around the window, showed a square of darker blur outside the border arc, at the
top-right and both bottom corners of the Inbox and at all four corners of another fence.

Findings, in order:
1. **The window region was rounded.** `GetWindowRgn` returned a complex region: the corner pixels were excluded and
   the centre included. So the region was not the problem.
2. **WPF's `WindowChrome` re-applies its own region on every resize.** With `CornerRadius = 0` it set a square one,
   which competed with NeoFences' rounded `SetWindowRgn`. Making the region follow every `WM_WINDOWPOSCHANGED`, and
   then giving `WindowChrome` an 8-DIP radius, still left the square patch.
3. **The accent blur ignores the window region.** Windows draws `ACCENT_ENABLE_BLURBEHIND` over the whole window
   rectangle. `DwmEnableBlurBehindWindow` with the rounded region (experiment 2) did not change that.
4. **Windows 11's `DWMWA_WINDOW_CORNER_PREFERENCE = ROUND` rounds the window, blur included** (experiment 1). All four
   corners of every fence came out smooth with no square patch. That is the fix. NeoFences' own region code is gone;
   `WindowChrome`'s 8-DIP radius keeps a rounded region for hit-testing (and the Windows 10 look).

## Live checks and regressions

| Check | Result |
|---|---|
| R1 corners, branch build | **Pass**: both fences, all corners rounded, no blur outside the border (8× zoom). |
| M5 gesture smoke (branch build, installed copy restored) | **Pass**: every line except the known roll-up-after-Explorer carry-over (M8b). |
| M6a tray / Pause / game-mode smoke | **Pass**: every line, incl. Pause (now instant) and the deferred desktop change. |
| R9 `pack.ps1 -Version 1.x` | **Pass**: fails at once with the SemVer message; the caller's folder is unchanged. |
| Core | 259 tests: reconcile recovery (safe-save memory, moved Desktop, ambiguous names), unique device ids, fences from another setup, backup failure. |
| R3–R8, R10 | [USER] / by hand: Word saves during a watcher restart, a moved Desktop folder, a clock change, user-hidden icons, cancelled-shutdown tray, a big unpack during a game. |

## Final review (opus, whole branch)

0 critical. Fixed:
- **I1:** quick-hide and icons the user hid in Explorer are now one `RunState` rule (`IconsHiddenByUser`, 3 tests). This
  fixed a regression where a failed show could leave icons hidden, and exit or an Explorer restart showing icons the user
  had hidden.
- **The watcher check (re-graded):** `File.GetAttributes` tells "not found" from "cannot read". A live run on the branch
  build tracked a Desktop file's create, rename and delete correctly.

Deferred minors are in ROADMAP.
