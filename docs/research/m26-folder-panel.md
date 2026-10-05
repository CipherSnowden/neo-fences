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
