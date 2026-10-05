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

## Final review (Opus, 2026-10-05): with fixes

No critical findings. Fixed (App and Shell; checklist rows AK11–AK13):
- **I1** — stats failures were queued to a dispatcher created on the worker thread, so "logged once" never happened:
  the UI's dispatcher is captured first.
- **I2** — Open on several selected items with a widget among them ran ShellExecute on `neofences:widget/…` (Windows'
  "get an app" prompt): the menu opens each through the same path as a double-click (AK12).
- **I3** — a rolled-up fence opened by hover or click showed frozen widgets: "can be seen" includes an opened roll-up, and
  opening it updates them at once (AK11).
- **I4** — the stats throttle used wall-clock time (a clock change froze it): the monotonic clock, with timer jitter
  allowed for, so stats come every 2 s (also review M4).
- **I5** — 12/24-hour and time-zone changes were not followed until a restart (.NET caches them): Windows' "intl" and
  time-change messages clear the caches and update the widgets at once (AK13; AK4).
- **M1 → fixed** (re-graded: a possible fault at exit) — a reading and Dispose never overlap (a lock; a disposed reader
  returns no values).
- **M2 → fixed** (re-graded: a GPU driver update left GPU at "—" until restart) — only an unopenable counter is final;
  a failed reading reopens the query; "new data" readings count.

Deferred minors:
- Stale content for up to a second after pause, quick-hide or game mode ends; the first CPU/GPU reading after a long
  hidden spell averages over it.
- The stats rows are rebuilt every second; the widget layouts are created (collapsed) for every element.
- Properties on a widget still offers Change icon and asks the shell to preview the widget target.
- An unknown widget kind reads as an item that opens through Windows (not "Missing").
- GPU usage of two active adapters is summed (Task Manager takes the busiest).
- "MMMM yyyy" gives the wrong month/year order in a few cultures (ja, zh, ko, hu); `YearMonthPattern` would fix it.

Set aside by the reviewer, ruled to stand: the 1 Hz timer while widgets are unseen (a plan departure), title colour and
default font (a departure), first readings "—" (a departure), C: only (the spec), unreadable stats at 1 × 1, Viewbox
scale shifts, tone contrast (to the live check), name pop-ups on widgets, Windows' own error dialog when the Clock app
is missing, Enter opening widgets' apps, PDH cost (measured live), comma lists in hand-edited targets, older versions.

## Live check

2026-10-05, branch build on a copy of the user's data (restored afterwards), scripted while the PC was unattended;
screenshots sent to the user.

- **Pass:** AK1 (Clock, Date, System stats in Apps; a clock in a Free fence at the first free spot), AK2 (1 × 1 and 4 × 4
  scale), AK3 (seconds tick, date line), AK5 (CPU 6 % idle → 98 % under load; RAM, GPU and C: shown), AK6 (after Pause and
  after roll-up the clock is right at once), AK7 (drag to another fence, Ctrl+copy, remove), AK8 (Clock app; Task
  Manager), AK9 (a snapshot restore brings the widgets back with the clock's options), AK10 (0.016 % of the machine over
  30 s with four widgets shown), AK11 (a rolled-up fence hovered open shows the current time), AK12 (Open with a widget
  and an app selected: the app opened, no Windows prompt).
- **By hand later:** AK4 (12/24-hour switch) and AK13 (time-zone change) — both change Windows settings.
- Script notes: the stats percentages are not all exposed to UI Automation, so AK5 was read from screenshots; Task
  Manager opened by AK8 runs elevated and could not be closed by the script (left for the user).