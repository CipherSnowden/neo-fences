# M25 — Widgets: clock, date, system stats (0.14.0) — design

**Date:** 2026-10-05 · **Status:** approved in brainstorm (sections 1–3), awaiting written-spec review
**Decisions:** ADR-047 (this milestone; continues ADR-045 one kind of fence and ADR-046 element sizes)

## Goal

The user's vision (ADR-045): a fence holds *elements*; widgets are one kind. This milestone adds three widgets any fence
can hold, at any size from 1×1 to 4×4: a **Clock**, a **Date** page and **System stats** (CPU, RAM, GPU, C: used, as
bars). Qualities the user asked for throughout: flexible, robust, safe, modern, performant enough.

Hard rules stand: NeoFences never modifies a user file; Win32/COM only in Shell via CsWin32; no new NuGet dependency;
failures degrade one feature, never crash. Gamer first: nothing polls while nobody can see it (game mode, pause,
quick-hide, rolled up, hidden tab).

Success: the user adds a 2×1 clock and a 2×2 stats widget to Apps; the time follows Windows' 12/24-hour setting; stats
move while a game loads in the background and stop updating in game mode; widgets drag, size, copy and restore like
any element; NeoFences' CPU use stays negligible.

## 1. Model

- A widget is stored like an item (its own id, size, cell, name) with a target `neofences:widget/<kind>`:
  `neofences:widget/clock`, `neofences:widget/date`, `neofences:widget/stats`.
- Core: `enum WidgetKind { Clock, Date, Stats }`; `ItemKind.Widget` (`ItemKinds.Of` returns it for those targets);
  `ItemKinds.WidgetTarget(WidgetKind) → string`; `ItemKinds.WidgetOf(string target) → WidgetKind?`.
- `VirtualItem.Widget` (`WidgetOptions? { bool Seconds, bool Date }`): the clock's options (default: no seconds, no
  date line); other widgets ignore it. Written to items.json only when set (schema 1).
- Default sizes (`FenceGrid.SpanOf`): Clock 2×1, Date 2×2, Stats 2×2; any 1×1–4×4 allowed.
- Widgets are never checked, watched, relocated, sorted by target facts (they sort by name), dragged out to other apps,
  or "Missing". A target `neofences:widget/<unknown>` reads as a plain item (shows Missing, can be removed).
- Several widgets per fence, copies included.

## 2. User interface

- **Fence menu → Add widget ▸ Clock / Date / System stats** (items fences): added at the end (Flow) or at the first free
  spot (Free).
- **Clock:** the time in Windows' short time format (12/24-hour follows Windows), optionally with seconds and a date line
  under it. Right-click: Show seconds ✓ · Show date ✓ · Size ▸ · Remove from fence. Double-click opens Windows' Clock app
  (`ms-clock:`).
- **Date:** a desk-calendar page — weekday on top, a big day number, month and year under it (Windows' culture). Turns
  over at midnight, and after a time change or waking from sleep. Right-click: Size ▸ · Remove from fence.
- **System stats:** four rows — CPU, RAM, GPU, C: (used space) — each a label, a bar and a percent; every 2 seconds. A
  value Windows cannot give shows "—". Double-click opens Task Manager. Right-click: Size ▸ · Remove from fence.
- **Look:** the fence's text colour and title font; content scales with the element's size (no label under a widget).
- **Updates stop** while nobody can see them: paused, quick-hidden, game mode, the fence rolled up, its tab hidden, or no
  visible widget at all (then no timer exists).
- Properties on a widget: name and note only (the target box is read-only, Browse off).

## 3. Units

- **Core** (test-first): `WidgetKind`, `ItemKind.Widget`, `ItemKinds.WidgetTarget` / `WidgetOf`, `WidgetOptions`,
  `FenceGrid.SpanOf` defaults, `Widgets.StatRow(name, percent?) → (Label, Percent 0–100, Text)` ("—" for null),
  `Widgets.DatePage(DateTime, CultureInfo) → (Weekday, Day, MonthYear)`, `Widgets.NextTick(DateTime now, bool seconds)`
  (the next second or minute boundary); widgets out of `CheckedTargets` / `PathTargets` / relocation.
- **Shell:** `SystemStats` — `Sample() → StatsSample(double? Cpu, double? Ram, double? Gpu, double? DiskC)`:
  CPU from `GetSystemTimes` (difference between two samples), RAM from `GlobalMemoryStatusEx`, C: from
  `GetDiskFreeSpaceEx`, GPU from PDH `\GPU Engine(*engtype_3D)\Utilization Percentage` (summed over instances, capped at
  100). One PDH query kept open; each value null when Windows refuses it (logged once). CsWin32 bindings only.
- **App:** `WidgetPresenter` (a control chosen by the item template for widget elements; clock / date / stats layouts in
  a Viewbox); `FenceHost.Widgets.cs` (one shared `DispatcherTimer`: each second while a shown clock has seconds, else each
  minute boundary; stats sampled every 2 s on a worker; started only while a visible fence holds a widget; time change /
  resume re-aligns); Add widget ▸ in the fence menu; the widget item menu; Properties read-only target for widgets.

## 4. Failures and performance

- A failed counter shows "—" for its row (logged once per counter); a failed sample keeps the last values.
- One timer and at most one sample in flight; no work while nothing is visible. Target: NeoFences' CPU use with a stats
  widget shown well under 0.1 % on the user's PC.
- A widget's double-click target missing (no Clock app): logged, nothing else.

## 5. Testing and release

- Core xUnit: kinds and targets, unknown kinds, default spans, options round trip, stat row text, date page parts, next
  tick boundaries, widgets out of checks/watching.
- `TEST-CHECKLIST` section **AK**: add each widget in Flow and Free; sizes 1×1 to 4×4; clock seconds and date options;
  12/24-hour follows Windows; date page; stats move (a busy CPU) and stop in game mode / pause / rolled up; drag between
  fences, Ctrl+copy, remove; snapshot restore; double-clicks; NeoFences' CPU with stats shown.
- Prototype first, then the plan with replay-verified patches; live check by script on backed-up data; screenshots sent;
  merged locally (until 1.0). Docs: ADR-047, ARCHITECTURE, FEATURES, ROADMAP, SESSION-LOG, hub. Release **0.14.0**, asked
  first.

## Out of scope

The folder panel element; weather, media or calendar-event widgets; widget themes; history graphs; per-core CPU.
