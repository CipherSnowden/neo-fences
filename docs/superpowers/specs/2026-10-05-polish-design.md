# M28 — Polish (0.16.1) — design

**Date:** 2026-10-05 · **Status:** scope chosen in brainstorm (all four groups), awaiting written-spec review
**Decisions:** no new ADR (fixes within ADR-046, ADR-047, ADR-048, ADR-049)

## Goal

The minors the final reviews of M24–M27 deferred, as one patch release. The user chose all four groups. No new
features, no new settings; each fix keeps today's behaviour everywhere else. Hard rules stand (no file is touched; Win32
only in Shell via CsWin32; no new dependency; failures degrade one feature).

One minor is dropped: the old Game Library fence's tiles at 1 × 1 (that fence kind no longer exists after M22's
migration).

## 1. Folder panels (ADR-048)

- **P1 Columns by real width.** Details shows Name and Date while the panel is narrower than ~300 DIP, all four columns
  from there — by its actual width, so a filling panel in a narrow fence and a 2-cell panel never clip Date.
  (`FolderPanels.Columns(widthDips)` replaces the span rule; the cell count no longer decides.)
- **P2 No hover-name pill over panels.** In icon-only fences the pop-under name never shows for a panel element.
- **P3 Menu key on a row.** The entry menu opens at the row (its centre), not at the fence's selected element.
- **P4 Lighter element template.** A panel control and the widget layouts are built only for elements that are a panel
  or a widget (a typed template / content presenter), not collapsed in every item.

## 2. Auto-collect (ADR-049)

- **A1 Offer on a slow folder.** OK while the dialog still says "Looking…": the "Add these N too?" offer lists the
  folder itself (off the UI thread) and asks when it has the answer.
- **A2 No watermark going back.** Rules unchanged in the dialog keep their current watermark (taken after the dialog,
  not from before it).
- **A3 No catch-up during a game.** A lister started while a game runs (NeoFences restarted mid-game) starts paused: its
  first listing and catch-up wait for the game to end.
- **A4 Watermark at the listing's time.** A batch advances the watermark to when its newest listing was taken, not to
  when it was processed, so a power cut between the config and items writes cannot skip arrivals (they are caught up
  again; held ones are skipped).

## 3. Widgets (ADR-047)

- **W1 Right at once.** Ending pause, quick-hide or game mode updates the widgets immediately (not up to a second
  later); the first CPU/GPU reading after a long hidden spell is a fresh 2-s rate, not an average over the spell.
- **W2 Stats rows in place.** The four rows are updated, not rebuilt every second.
- **W3 Widget Properties.** No "Change icon" for widgets; the target is not previewed through the shell.
- **W4 Unknown widget kinds** (from a newer NeoFences) show as Missing and do not open through Windows.
- **W5 GPU = busiest adapter**, like Task Manager (not the sum of two adapters).
- **W6 Month and year** use the culture's year-month pattern (`YearMonthPattern`), right in ja, zh, ko, hu.

## 4. Grid and sizes (ADR-046)

- **G1 Group drag anchor.** In Free fences a group drag keeps offsets from the element under the pointer, not the first
  selected one.
- **G2 One icon load** after an icon-size change (not one for the new span plus one for the reload).
- **G3 Hidden tabs in Free fences.** New elements of a Free fence shown in a hidden tab are placed with that fence's own
  column count (from its width), not another window's.
- **G4 Size picker:** arrow keys move the highlight and Enter picks; the highlight returns to the current size when the
  pointer leaves; "Default size" is checked only when every selected element has the default size.
- **G5 Covers in icon-only fences** keep their full size (the label's room is not taken from 1 × 2 covers).
- **G6 Mixed drops** (fence items plus panel entries or other paths) in a Free fence: the paths land at the drop cell
  with the items' offsets, not on the first free spots.

## 5. Testing and release

- Core xUnit where Core decides: P1 columns by width, A4 watermark time, W4 unknown kinds, W5 busiest adapter (pure
  selection), W6 month-year text, G1 anchor offsets, G3 columns from a width, G6 placement of mixed drops, G4 the
  default-size check.
- `TEST-CHECKLIST` section **AN**: one row per App-only fix (P2, P3, P4, A1–A3, W1–W3, G2, G4 keys, G5).
- Prototype first, plan with replay-verified patches, native execution, Opus review, live check by script on a copy of
  the user's data (asked first unless the PC is unattended), merged locally, release **0.16.1** asked first.

## Out of scope

New features; anything the reviews did not defer; the pre-1.0 work (first run, code signing, docs for friends).
