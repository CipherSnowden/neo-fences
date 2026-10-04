# M3b — Drag-drop: verification results

**Date:** 2026-10-03 · **Machine:** Windows 11 Pro 25H2 (26200), one 1920×1080 monitor at 100 %.
**Build:** `m3b-drag-drop` (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section K.

The agent ran `m3b-smoke.ps1` (in the plan) with the user's consent, while they were remote. It drove real mouse
input on the prototype, then on the repo build. It uses only `zz-m3b-*` files and a temp folder, positions an
Explorer window away from the fences, and refocuses Windows Terminal at the end.

## Results

| ID | Result | Evidence |
|---|---|---|
| K1 | **Pass** (after) | `zz-m3b-a.txt` dropped on the right half of `zz-m3b-b.txt` landed after it; no file operation. The before (left-half) case is covered by the `DragDropTests` only. |
| K2 | **Pass** (single item) | `zz-m3b-a.txt` was dragged from the Inbox into the empty "Games" fence: it is in Games and still on the Desktop. Multi-item is pinned by `SeveralItems_KeepTheOrderTheyHadInTheirFences_NotTheSelectionOrder`; live is [USER]. |
| K3 | **Pass** (Recycle Bin) | Dropped on the Recycle Bin item: the drop was forwarded to its drop target with `DROPEFFECT_MOVE`; the file left the Desktop and is in the Recycle Bin. The folder case is [USER]. |
| K4 | [USER] | Drops in from Explorer could not be scripted (see below). The code path is written; Core arrivals are pinned by `FilesDroppedFromExplorer_ArriveWhereTheyWereDropped`. |
| K5 | [USER] | Same reason. |
| K6 | **Pass** (Explorer) | A fence item dropped on an Explorer window (temp folder, same drive): Windows moved it there, and it left the Desktop and the Inbox. Apps are [USER]. |
| K7 | **Pass** | A band dragged over the Games fence selected every item in it (UIA `SelectionItemPattern`). |
| K8 | Partly | The drag image is drawn through `IDropTargetHelper`; it was not inspected visually. |
| K9 | [USER] | Name conflict on drop. |

## Findings

1. **WPF registers its own OLE drop target on every window.** `RegisterDragDrop` therefore failed with
   `DRAGDROP_E_ALREADYREGISTERED` on all four fences, and every drop was refused. The fence now revokes WPF's
   registration first (NeoFences does not use WPF drag-drop).
2. **Text files (and programs) report `SFGAO_DROPTARGET`.** Hovering one handed the drop to that file instead of
   reordering, so the first reorder attempt went nowhere. Only containers (`SFGAO_FOLDER | SFGAO_DROPTARGET`: folders,
   Recycle Bin) take drops themselves.
3. **Test-harness limit.** Explorer does not start a drag from synthetic input: neither `SetCursorPos` nor absolute
   `mouse_event` moves started one, even onto the bare desktop. An OLE drag started by a helper process (WinForms
   `DoDragDrop` with CF_HDROP) did not run either, even into an Explorer window. Drops from other processes are
   therefore left to a check by hand (K4/K5). Drags *out of* fences into Explorer (another process) work, because NeoFences'
   own drag loop runs them.
4. The smoke's first runs were misled by stale UIA positions (looking up one item scrolled the list) and by a leftover
   Explorer window from an aborted run. The script now re-reads positions and closes leftovers.

## Final review fixes (opus reviewer, 1 critical / 4 important / 11 minor)

| Finding | Fix | Verified by |
|---|---|---|
| C1: Shift+drop on the Recycle Bin item went to the bin's own drop target, Windows' permanent-delete gesture | NeoFences handles Recycle Bin hovers itself: move cursor, `ShellFileOps` recycle | live: Shift+drop → file in the Recycle Bin |
| I1: dropped files that took more than 5 s to appear went to the Inbox | per-placement expiry; drops 3 min, safe-saves 5 s | 3 Core tests RED→GREEN |
| I2: the whole cell meant "drop into" (folders, zips, Recycle Bin), with no feedback | `DropZones` (centre only) + insertion caret / highlight | Core tests RED→GREEN; live: folder edge reorders, middle moves inside |
| I3: selecting text in the rename box started a drag | TextBox ancestor check | code |
| I4: docs claimed drops from Explorer were verified by hand | wording: pending user check | docs |

Eleven minors deferred (ROADMAP "Carry-overs from the M3b review"). The first Shift check was itself wrong (the long
Inbox scrolled the test file out of view); the fixed check moves it into Games first.

## Conclusion

Drag-drop within, between and out of fences, onto the Recycle Bin, and the rubber band all work on the real desktop.
Drops in from Explorer remain for the user (K4/K5).
