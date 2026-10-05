# M25 — Widgets: clock, date, system stats (0.14.0): build notes and results

Spec: `docs/superpowers/specs/2026-10-05-widgets-design.md` · Decision: ADR-047 · Plan:
`docs/superpowers/plans/2026-10-05-m25-widgets.md`

## Prototype (2026-10-05, worktree `neo_fences-m25proto`, branch `m25-proto`)

Built end to end before the plan: Core test-first (17 new tests, 559 in all), Shell, App; 0 warnings. Probed live on a
copy of the user's data (restored afterwards): Clock (17:16, then 17:17:28 with seconds), Date (MONDAY / 5 / October
2026) and System stats (CPU 2 %, RAM 37 %, GPU 19 %, C: 74 %) added to Apps; NeoFences used 0.003 % of the machine over
30 s with all three shown (16 logical CPUs).

### Where the build departs from the spec (the final review weighs them)

- The timer exists while any widget exists (on whole seconds) and does no work while none can be seen, instead of being
  created and destroyed with visibility; stats are read only while a stats widget can be seen.
- Big text uses the fence's title colour, small text its text colour; the font is WPF's default (not the title font).
- The first GPU and CPU readings show "—" (a rate needs two readings, 2 s apart).
- A second widget of a kind is appended directly (not through `ItemEdits.Add`, which merges the same target).
- An unknown `neofences:widget/…` kind reads as a plain item (shows Missing; can be removed).

## Live check

(TEST-CHECKLIST AK — filled in after the run.)