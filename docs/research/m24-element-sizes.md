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

## Live check

(TEST-CHECKLIST AJ — filled in after the run.)