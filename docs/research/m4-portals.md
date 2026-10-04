# M4 — Portals: verification results

**Date:** 2026-10-03 · **Machine:** Windows 11 Pro 25H2 (26200), one 1920×1080 monitor at 100 %.
**Build:** `m4-portals` (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section L.

The agent ran `m4-smoke.ps1` (in the plan) with the user's consent, while they were remote: once on the prototype and
once on the repo build. The script adds a temporary Portal fence for `%TEMP%\nf-portal` through the config (app
stopped), drives real mouse and keyboard input, finds items through UI Automation, and removes everything at the end.
The M3a and M3b smokes were re-run as regressions, since the item namespace and drop target changed. All passed.

## Results

| ID | Result | Evidence |
|---|---|---|
| L1 | **Pass** (dialog) | "New Portal fence…" opened "Choose the folder for the new Portal fence"; Esc added no fence. Picking a real folder is [USER]. |
| L2 | **Pass** | `new.txt, mid.txt, old.txt, sub` (newest first, folders mixed in by date). A new file appeared at the top within the debounce. |
| L3 | **Pass** | Double-click on "sub" showed `inner.txt` and the breadcrumb "nf-portal › sub"; Backspace returned to the root. |
| L4 | **Pass** | Ctrl+double-click opened an Explorer window on "sub"; the Portal stayed at the root. |
| L5 | **Pass** | Sort by ► Name gave `sub, mid.txt, new.txt, old.txt, zz-live.txt`, saved as `sort: name`. |
| L6 | [USER] | One-time sort on a desktop fence. Pinned by `SetItemOrder_*` and `ItemSorting` tests. |
| L7 | **Pass** (plain drag) | Portal → "Games": Windows moved `mid.txt` to the Desktop and it landed in Games. Games → Portal: moved back into the folder; Games empty again. Ctrl-drag is [USER]. |
| L8 | [USER] | Menu, rename and delete inside a Portal: same code paths as desktop items, through the item's parent folder. |
| L9 | **Pass** | Folder renamed away: "not available" message and no items. Renamed back: items return and the message goes. |
| L10 | **Pass** | Deleting the Portal fence removed it from the config; the folder was still there. |
| L11 | [USER] | Network or USB folder disconnecting. |

## Finding

**A `FileSystemWatcher` follows its directory when that directory is renamed, and reports nothing.** A Portal therefore
kept showing a folder that no longer existed under its path. The fix has two parts:
- `FolderWatcher` also watches the parent folder for the Portal folder's own name;
- `PortalState` re-arms the watchers on every re-list, so a folder deleted and created again is watched again.

## Final review fixes (opus reviewer, 0 critical / 5 important / 10 minor)

| Finding | Fix | Verified by |
|---|---|---|
| I1: every desktop change re-listed every Portal on the UI thread (a network Portal could freeze the app) | Portals refresh on their own only; listing and watcher setup in the background, newest request wins | smoke re-run all True |
| I2: the debounce restarted on every event, so a folder being written to never refreshed | at most one re-list per 250 ms | code |
| I3: "Sort by" on a desktop fence could crash on names ending in a space or dot | keep the stored ref; a refused sort is logged | code (reviewer probe) |
| I4: a Portal never came back once its drive returned | 7 s retry while unreadable or unwatched | smoke (vanished/back) |
| I5: permanent delete was reachable on drives without a Recycle Bin | refused with a message (user decision) | probe: fixed drives True, removable/UNC False |

Ten minors: M1, M8, M9 fixed with the rewrite; the rest deferred (ROADMAP).

## Conclusion

Portals work on the real desktop: live view, newest first, browsing, sorting, drag in and out, a vanished folder, and
delete. Picking a real folder, the one-time desktop sort, Portal item actions and removable or network folders remain
for the user.
