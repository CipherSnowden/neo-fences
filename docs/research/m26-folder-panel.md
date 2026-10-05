# M26 — The folder panel element (0.15.0): build notes and results

Spec: `docs/superpowers/specs/2026-10-05-folder-panel-design.md` · Decision: ADR-048 · Plan:
`docs/superpowers/plans/2026-10-05-m26-folder-panel.md`

## Prototype (2026-10-05, worktree `neo_fences-m26proto`, branch `m26-proto`)

Built end to end before the plan: Core test-first (42 new tests, 601 in all), Shell, App; 0 warnings. Probed live on a
copy of the user's data (restored afterwards; the user paused the first run, the second ran with their go):

- Start over data with the Downloads folder view: a "Before folder views became panels" snapshot, then the fence held one
  filling Icons panel (newest 30, date, newest first) — 14 entries, the same look as the view, no second title.
- A 4 × 3 Details panel of `D:\GameLibrary` in Games: the Date modified header sorted newest first (arrow on the header);
  double-clicking `Blur` browsed in ("GameLibrary › Blur", Back / Up / Home shown, its files with dates, types and
  sizes); Back returned.
- Downloads → panel menu → Look ▸ Details: columns with sizes in the user's number format.
- NeoFences' CPU over 30 s with both panels shown, idle: 0–0.01 % of the machine.

Found and fixed while probing: a filling panel repeated the fence's title (now hidden while it shows its own folder under
the same name); the whole panel tinted with the fence's hover and selection (now only its rows do).

### Where the build departs from the spec (the final review weighs them)

- "Fill fence" is stored and works while the panel is alone: beside another element it sits on cells, and fills again
  when it is alone again; a size pick turns it off (the spec: adding anything "turns it back to cells").
- A "Before folder views became panels" snapshot is saved before the migration (as M22 did for games); the migration is
  skipped (logged) while items.json is read-only — that fence then shows nothing for the session.
- A filling panel at its own folder under the fence's own title hides its header row.
- "Show as folder panel" resets the item's size (the panel's 4 × 4) and Fill; "Show as icon" clears the panel settings.
- Add folder panel… in a Free fence uses the first free spot for 4 × 4 (rows grow; it never shrinks).
- The fence menu's "New folder view…" became "New folder panel…" (a new fence with one filling panel), beside Add folder
  panel….
- Panel settings is the folder view dialog without the sort (headers and Sort by ▸ sort); the panel menu has no
  Properties… (F2 / Alt+Enter on the selected panel still open it).
- The Type column shows the extension (as Sort by does), not Windows' type names.
- The panel's name area belongs to the fence (select, drag, right-click → panel menu); its rows, buttons and lines are the
  panel's own.

## Final review (Opus, 2026-10-05): with fixes

No critical findings. Fixed (Core test-first where Core shows it; App fixes have checklist rows AL14–AL19):
- **I1** — a panel that stopped filling (an item added, Fill off, a 4 × 4 pick) kept the fence's size and was clipped in
  its cells: a change of Fill now resizes it like a change of span (AL14).
- **I2** — in a Free fence a panel could not be moved a short way by its name row (every drop over a panel was refused):
  only drops from outside are refused over panels; fence elements move freely (AL15).
- **I3** — an arrow at a panel's first or last row bubbled to the fence's list, moving the selection to a fence element
  (where Delete would act): the panel keeps its arrows (AL16).
- **I4** — a file that changed (a growing download) got a new row each re-list: its icon blinked and its selection went.
  Rows are kept by path and only their facts update (AL17).
- **M6 → fixed** (re-graded: a failed items save during the migration still saved the config without the view) — the
  config is not saved when the items save first fails.
- **M7 → fixed** (re-graded: a read-only config.json made a new snapshot at every start) — the migration reports the
  panels it added (`AddedPanels`); a repeat that adds none takes no snapshot (test).
- **M11 → fixed** (re-graded: the user's own Downloads fence lost its Sort by) — Sort by on a fence its panel fills sorts
  the panel as the view did (`FolderPanels.SortedBy`, tests; AL19).
- **M12 → fixed** (re-graded: an invisible selection Delete removed) — a press on a panel's own space never selects the
  panel element; a selected panel shows a thin outline (AL18).

Deferred minors:
- Details columns follow the stored span, not the real width (a filling panel in a narrow fence squeezes Name; at 2 cells
  Date can clip).
- In icon-only fences the hover name pill can sit over a filling panel's top rows.
- Every element's template builds a collapsed panel control (unmeasured cost; a ContentPresenter with a typed template
  would build it only for panels).
- The Menu key on a panel row places the entry menu at the fence's selected element, not the row.
- DECISIONS.md: no blank line between ADR-047's last line and ADR-048's heading.
## Live check (2026-10-05, branch build on a copy of the user's data; the user gave the go)

Scripted, three runs (the first two found script misses: clicks on rows and buttons outside the visible part of a
panel taller than its fence). Passed: AL1 (migration, snapshot first, no second title), AL2/AL7 (Add item → Show as
folder panel 4 × 4; Show as icon), AL3 (List, Icons, Details; 2 × 3 shows Name and Date), AL4 (Date newest / oldest
first, Size, Name; arrows on the headers), AL5 (into Blur › levels; Up, Back, Home, Backspace; Up off at home), AL6
(2 × 2 turns Fill off; Fill fence on again), AL9 (Add to fence ▸ Apps), AL12 (0 % CPU over 30 s), AL13 (the snapshot's
view came back as a filling panel), AL14 (an item beside: the panel on 4 × 4 cells), AL16/AL18 (Down and Left at the last
row, Delete, Delete after a click on empty panel space: nothing removed), AL19 (Sort by Name / Date on the filled
fence sorts its panel). A Delete while a panel *element* was selected (from its menu) removed that element, as for
any item (the outline shows the selection). By hand later: AL8 (a drop from Explorer onto a panel), AL10 (pendrive),
AL11 (game mode), AL15 (Free move by one cell), AL17 (a growing download), AL20.

Found and fixed: a panel taller than its fence hid its own name row and buttons, and the wheel over it never scrolled the
fence — at the panel's top or bottom the wheel now scrolls the fence (AL20; the spec said the wheel scrolls only the
panel).