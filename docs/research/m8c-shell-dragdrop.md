# M8c — shell actions and drag-drop details: results

**Date:** 2026-10-03 · **Machine:** Windows 11 Pro 25H2 (26200), 1920×1080 at 100 %, Takeover on, NeoFences 1.1.0 installed, PowerToys installed.
**Build:** M8c prototype (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section T · **Decision:** ADR-026.

## Live smoke on the prototype (installed copy restored after each run)

| Check | Result |
|---|---|
| T1 menu → Rename with two selected | **Pass**: the box opened on the right-clicked item (`nf-m8c-b.txt`). |
| T2 Shift + right-click / Shift+F10 | Menus open; for a .txt file the extended menu adds no entries here, so the two cannot be told apart by count. |
| T3 outside click closes the menu | **Pass**. |
| T4 Del recycles on the shell worker | **Pass**: the file went to the Recycle Bin; the UI kept responding. |
| T6 Desktop icon settings → Control Panel on, then back | **Pass**: fenced within ~2 s, removed again after the setting was restored. |
| Recycle Bin notice after a recycle | **Pass** once registered for all events on the bin (first attempts with update events only, at shell level, saw nothing). |
| Watcher burst (3000 files created and deleted) | No overflow happened (no recovery needed); every file came and went. Coalescing is untested live. |
| M8b regressions (Settings, first title double-click, Peek, quick-hide) | **Pass** on the M8c build. |
| Second instance while one runs | **Pass**: waited ~5 s, logged "already running", exited. |
| Log | 0 errors or warnings. |

## Findings

1. **Not a bug: "Rename with PowerRename".** The first smoke clicked the first menu entry starting with "Rena", which on
   this PC is PowerToys' PowerRename (verb `{1861E28B-…}`), and opened PowerRename windows (closed again). The shell's
   own Rename (`Rena&me`) still has the verb `rename`, Delete `delete`: NeoFences' handling is right.
2. **"Copy as path" is in the normal Windows 11 menu**, so it is no longer a marker for the extended menu.
3. **The same-version Setup only offers Repair** ("NeoFences is already installed"), so a Setup-over-install of the same
   version cannot show the upgrade behaviour. The next real upgrade will.
4. **The Recycle Bin's changes arrive as file-system events of its folders**: the registration needs interrupt level
   and all events on the bin; update events at shell level alone never came.
