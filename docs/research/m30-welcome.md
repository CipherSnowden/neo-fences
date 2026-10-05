# M30 — First-run welcome (0.18.0): build notes and results

Spec: `docs/superpowers/specs/2026-10-06-welcome-design.md` · Decision: ADR-051 · Plan:
`docs/superpowers/plans/2026-10-06-m30-welcome.md`

## Build

- Built in a scratch worktree and replay-verified on `main` (the tests patch fails to build without the code: 13 errors;
  655 tests with it; 0 warnings).
- Every way an item reaches a fence (drops, Add item…, widgets, folder panels, games, auto-collect, Add from desktop)
  goes through `ItemsChanged`, so the welcome ends whichever way the first item arrives.
- Drops are taken at the fence window, not its list, so dropping on the welcome panel adds to the fence.
- Hide icons: Add from desktop already offered "Hide desktop icons while NeoFences runs" (applied after the items), so
  the welcome reuses it (spec §4 ruling).
