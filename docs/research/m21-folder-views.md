# M21 — Folder views (0.11.0): build notes and results

Spec: `docs/superpowers/specs/2026-10-05-folder-views-design.md` · Decision: ADR-044 · Plan:
`docs/superpowers/plans/2026-10-05-m21-folder-views.md`

## Prototype (2026-10-05, worktree `neo_fences-m21proto`, branch `m21-proto`)

Built end to end before the plan: Core test-first (29 new test cases, 497 in all), Shell, App; the solution builds with 0
warnings. Each plan task's patch was then replayed on a fresh worktree of `main`.

### Where the build departs from the spec (the final review weighs them)

- **Sort reuses `FenceSort`** (Name / Type / Date; Manual is read as Name) instead of a new `ViewSort`: the fence menu's
  "Sort by" items and `ItemSorting.Order` serve views unchanged.
- **`FolderViews.Selection` carries `Listed`** (entries before any filter) so `Status` tells "This folder is empty" from
  "Nothing here matches this view"; `Status(selection, view)` returns `(Center, More)`.
- **`DefaultsFor(path, busyFolders)`** takes the busy folders as a list; `KnownFolders.BusyFolders` gives Downloads and
  Screenshots (asked once per run).
- **"Show as folder view"** places the new view 40 DIP right and down of the fence it came from (like a detached tab).
- **Deleting a view fence asks nothing** (it has no items); its menu entry reads "Delete fence (the folder is not
  touched)".
- **Refresh** on a view re-lists its folder.
- **Add to fence ▸** lists the items fences only (not the Library, not other views) and is greyed out when there are none;
  several entries copy as "Copy paths", one per line.
- **"Folder not available"** is logged once per outage, not on every 7 s retry.
- **Dragging out of a view whose folder stopped answering** (a share gone since the listing) does not start; it is logged.
- **Not logged** (the spec said logged): a view dropped from a Library fence, a bad pattern repaired from a hand-edited
  config (the normalizer has no log), and the number of views at start.

## Final review (Opus, 2026-10-05): with fixes

No critical findings. Fixed (Core test-first where Core can show it; App-only fixes have checklist rows AG20–AG22):
- **I1** — a view whose folder is gone logged a "cannot watch" warning at every 7 s retry: now once per outage
  (`FolderLister.LogWatchFailureOnce`; AG22).
- **I2** — a view of a drive root listed `C:`, which .NET reads as the current folder on that drive: the root keeps its
  separator (`FolderViews.ListedFolder`; test `ListedFolder_KeepsADriveRootsSeparator`; AG20).
- **I3** — Browse… in Folder view settings parsed a dead share's path on the UI thread: checked off it first, 2 s (AG21).
- **M5 → fixed** (re-graded: a crash) — settings applied after a tray restore removed or changed the view while the dialog
  was open: the fence is checked again first.
- **M7 → fixed** (re-graded: a typo lost the whole config to its backup) — a hand-edited view's `show` / `sort` typo is
  repaired (`LenientEnumConverter`; test `Config_RepairsATypoInAViewsShowOrSort_InsteadOfFailingTheFile`).
- **M9 → fixed** (re-graded: an exception escaping to the UI thread) — `KnownFolders` catches every failure but out of memory.

Deferred minors:
- Filtering and sorting run on the UI thread at every re-list (~140 ms for 20,000 entries sorted by name; ~3 ms by date
  or newest N, the Downloads default).
- A hidden tab's header keeps its old title after its folder's rename until the box is redrawn.
- Folder view settings accepts a relative path or `%VAR%` (it then shows "not available"), and a bad count disables OK
  without marking its box.

Set aside by the reviewer, ruled to stand: Windows' own thumbnail-cache writes and "X - Copy" drags (Windows' actions,
as for items); Windows' menu deleting for real (the spec allows it); Enter on many entries opens each (as for items); an
ancestor folder's rename shows the old listing until the next change (edge timing); `SHGetKnownFolderPath` on the UI
thread once per run (a local call on this PC); Open folder not ending Peek (as the item menu's Open);
`FindResource` for the hint colour (works under `ThemeMode`; checked live by AG4).

## Live check

(TEST-CHECKLIST AG — filled in after the run.)
