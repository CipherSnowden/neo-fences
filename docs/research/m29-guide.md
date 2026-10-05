# M29 — A guide for friends (0.17.0): build notes and results

Spec: `docs/superpowers/specs/2026-10-05-guide-design.md` · Decision: ADR-050 · Plan:
`docs/superpowers/plans/2026-10-05-m29-guide.md`

## Build

- Help entries (tray **Help**, Settings **Help (online guide)**) built in a scratch worktree and replay-verified (0
  warnings, 643 tests).
- README and `docs/GUIDE.md` written from the app's own strings: every menu entry, button and setting the docs name was
  collected from the XAML and the code and checked to exist (a scripted search for each).
- The screenshots and the live check of the Help entries run together (ruling in the ledger): one question and one desktop
  run for the user.
## Screenshots

- Demo setup on a backed-up copy of the user's data, with the Release build of the branch (the Debug build adds a
  "Debug: freeze…" entry to the fence menu). Demo folder: `G:\NeoFences-test\Downloads` (sample files, removed after).
  A "Desk" fence with Clock (with date), Date and System stats; Downloads as a Details panel of the demo folder; an
  Apps auto-collect rule on it.
- Eight shots in `docs/guide/`. The three with large areas of wallpaper (`desktop`, `games`, `item-menu`) are JPEG
  (quality 85): as PNG they were 3.7 MB, 526 KB and 467 KB. The rest stay PNG; all are under 400 KB.
- Privacy (AO4): every shot opened and checked — game titles, common app names, the demo files, no user name, email,
  private file or link.
- Gotchas: the item-menu shot needs the fence menu closed first (Esc, then a click on empty desktop); the Settings
  window is found by its exact title "NeoFences settings" (a looser match caught another window).

## Live check (branch Release build)

| ID | Result |
|---|---|
| AO1 | **Pass**: tray → Help brought Firefox (the default browser) to the front. |
| AO2 | **Pass**: Settings → Help (online guide) brought Firefox to the front. |
| AO3 | [after the push] the guide's GitHub page shows 404 until `docs/GUIDE.md` is on `main` on GitHub. |
| AO4 | **Pass** (above). |
| AO5 | [USER] |
