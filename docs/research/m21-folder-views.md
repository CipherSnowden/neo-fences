# M21 — Folder views (0.11.0): build notes and results

Spec: `docs/superpowers/specs/2026-10-05-folder-views-design.md` · Decision: ADR-044 · Plan:
`docs/superpowers/plans/2026-10-05-m21-folder-views.md`

## Prototype (2026-10-05, worktree `neo_fences-m21proto`, branch `m21-proto`)

Built end to end before the plan: Core test-first (29 new tests, 497 in all), Shell, App; the solution builds with 0
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

## Live check

(TEST-CHECKLIST AG — filled in after the run.)
