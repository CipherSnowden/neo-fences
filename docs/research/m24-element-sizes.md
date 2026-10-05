# M24 — Element sizes and the fence grid (0.13.0): build notes and results

Spec: `docs/superpowers/specs/2026-10-05-element-sizes-design.md` · Decision: ADR-046 · Plan:
`docs/superpowers/plans/2026-10-05-m24-element-sizes.md`

## Prototype (2026-10-05, worktree `neo_fences-m24proto`, branch `m24-proto`)

Built end to end before the plan: Core test-first (14 new tests, 540 in all), App; 0 warnings. Probed live on a copy of
the user's data (restored afterwards): Size ▸ 2 × 2 on Steam made a big icon with the smaller apps filling the gaps; the
Games covers sit on 1 × 2 cells; Apps → Free kept all 19 elements where they were; a drag onto a taken cell went to the
nearest free spot ((4,2) → (4,4); (0,0) → (3,0)) and left the old cell empty.

### Where the build departs from the spec (the final review weighs them)

- `Fence.Layout` is a plain enum, so config.json writes `layout` for every fence (`flow` by default); items.json writes
  `size` / `cell` only when set.
- The panel arranges at every measure (no cache: one pass over a map of cells is ~µs for 500 elements; a `ponytail:` note).
- Arrow keys use WPF's own directional navigation, which follows the panel's positions (no extra code).
- The Free drop marker shows a 1 × 1 cell (not the dragged element's span).
- New elements of Free fences store the spot they show at (`PinFreeCells`), so a later addition never moves them.
- The Size ▸ squares are Borders with automation names; screen readers see the caption and Default size, not each square.

## Final review (Opus, 2026-10-05): with fixes

No critical findings. Fixed:
- **I1** — a Ctrl+drag in a Free fence treated the originals as empty space (a copy could land on its original's cell and
  make it jump) and matched several copies to the wrong offsets: only the arriving items are left out; sources go in
  document order like the copies (`PlaceDropped_ACopyOnItsOriginalsCell_GoesToTheNearestFreeSpot`; AJ13).
- **I2** — covers were decoded at the default 72-DIP tile width and never again after a resize: the tile is sized before
  its cover is decoded, and a resize or label-mode change decodes it again (AJ14).
- **I3** — the panel started with default 84 × 96 cells until a later refresh: cell sizes and the layout reach the panel
  through window resources, from its first pass (AJ15).
- **I4** — a hand-edited cell row of 10,000,000 could hang or crash layout: stored cells beyond 1000 rows or columns are
  dropped at load (`Repair_DropsCellsBeyondAThousandRowsOrColumns`).
- **I5** — a sort finishing after a tab switch re-packed the newly shown Free tab: the sort's own fence only.

Deferred minors:
- A group drag anchors on the first selected element, not the one under the pointer.
- The old library fence's tiles shrink to 1 × 1 cells (that fence only exists until the M22 migration).
- An icon-size change loads every icon twice (once for the new span size, once by the reload).
- New elements of a Free fence shown in a hidden tab are pinned with that window's last (or default) column count.
- Size ▸ squares are not keyboard-reachable; the highlight does not return to the current size when the pointer leaves;
  "Default size" shows checked for several selected custom-sized items.
- Covers in icon-only (labels on hover) fences are smaller than before (56 × 84 on 1 × 2 cells).
- A drop mixing fence items and folder-view or library paths places the paths on first free spots.

Set aside by the reviewer, ruled to stand: the 1 × 1 drop marker, no arrangement cache, WPF's own arrow-key navigation
and Home/End by list order, `layout` written for every fence (all plan departures); Flow's insert caret in reading
order; items moved into a Flow fence keeping their old cells (unused there; Flow → Free re-pins); out-of-width elements to
the first free spot (the spec's rule); columns changing with the scrollbar (as the WrapPanel did); icons above 256 px
scaled by the shell; the picker's system highlight colour.

## Live check

(TEST-CHECKLIST AJ — filled in after the run.)