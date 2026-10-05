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
- **"Show as folder view"** places the new view in free space like a new fence (the live check found that a fixed offset stacked views).
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

Deferred minors (fixed in 0.12.1, M23 — see ARCHITECTURE and checklist AI):
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

2026-10-05, test build on backed-up data (restored afterwards), scripted; screenshots sent to the user.

- **Pass (19):** AG1, AG2 (Downloads: Date, newest 30, newest entry first), AG3 + AG15 (`D:\GameLibrary`, folders only,
  6 games), AG4 (`*.png;*.jpg`; `a|b` refused, OK greyed), AG5 (add / rename / delete within 1.5 s), AG6 (folder renamed
  and back: path and title follow), AG7 ("Folder not available", back at once when moved back), AG11 (a drag onto an
  items fence makes an item, a drag to Explorer copies, a drop onto the view is refused and the file stays), AG12, AG13,
  AG14, AG16 (500 + "+ 100 more — Open folder", which opens Explorer), AG17 (Type sort kept after a restart), AG18 (a view
  as a tab lists at once), AG19 (the folder untouched by Delete fence; the restore brings the view back), AG20 (`F:\`,
  the drive NeoFences was started on, lists its root), AG21 (Browse… on `\\nosuchhost\share`: dialog after ~1 s, every
  fence responsive), AG22 (one "cannot watch" warning in 35 s).
- **Skipped by the user's choice:** AG8 and AG9 (pulling the stick, Safely Remove) — automatic recovery for a stick is
  not needed now; "Folder not available" and the fence menu's Refresh cover it. AG10 (game mode) not run.
- **Found and fixed:** "Show as folder view" placed every new view at the same offset from its fence, so several views
  made from one fence stacked on one spot (and menus hit the top one). New views are now placed in free space like a new
  fence.
- Script note: Windows' folder dialog ignores text set into its box by a message; the scripted AG1/AG2 picks landed on
  the dialog's default folder, so AG2's defaults were checked through "Show as folder view" on a Downloads item.
