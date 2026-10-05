# M28 — Polish (0.16.1): build notes and results

Spec: `docs/superpowers/specs/2026-10-05-polish-design.md` · Plan: `docs/superpowers/plans/2026-10-05-m28-polish.md`

## Prototype (2026-10-05, worktree `neo_fences-m28proto`, branch `m28-proto`)

All 20 fixes built before the plan: Core test-first (10 new tests and two changed ones, 643 in all), Shell, App; 0
warnings. A visual probe on a copy of the user's data (the user said "Run now"; restored afterwards) caught one bug of the
template change: the new content holder printed the item's type name under every plain icon — it is now collapsed unless
the element is a widget or a panel. Then: a clock with seconds, CPU/RAM/GPU stats, an "Unknown widget" with the Missing
badge, and a 2 × 3 Details panel showing Name and Date.

### Where the build departs from the spec (the final review weighs them)

- **A4**: instead of stamping the watermark with the listing's time, a batch that adds items saves items.json before
  config.json — a power cut in between collects the batch again (already held items are skipped), never skips it.
- **G5**: icon-only fences that show game covers get cells as wide as a cover (all of their cells), so covers keep the
  pre-grid size.
- **G6** is decided in the App (one placement for every arrival of a drop); Core's placement already handled it.
- **W4**: an unknown widget is a widget (`ItemKind.Widget` for every `neofences:widget/…` target) with Missing state and
  the name "Unknown widget"; it shows as an item (no layout), opens nothing, and has the widget menu.
- **W1**: after a hidden spell longer than 4 s, a first stats reading only primes; the shown one comes 2 s later.
- In a narrow Details panel Name stays short (the Date column keeps its width); not in scope.