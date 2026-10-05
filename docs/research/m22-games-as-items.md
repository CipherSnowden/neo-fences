# M22 — One kind of fence: games become items (0.12.0): build notes and results

Spec: `docs/superpowers/specs/2026-10-05-games-as-items-design.md` · Decision: ADR-045 · Plan:
`docs/superpowers/plans/2026-10-05-m22-games-as-items.md`

## Prototype (2026-10-05, worktree `neo_fences-m22proto`, branch `m22-proto`)

Built end to end before the plan: Core test-first (9 new tests, 511 in all), App; 0 warnings. Probed live on a copy of
the user's data (restored afterwards): the "Games" library fence became 12 game items with the same covers and order
("Before games became items" snapshot saved first); a game Ctrl-dragged into "Apps" showed as a cover tile among the app
icons, and Show as → Icon made it an icon cell; Add games… listed the 12 games with "already here" marking; the tray has
no "New Game Library fence".

### Where the build departs from the spec (the final review weighs them)

- `ShowAs` is `ItemShow?` with `Cover` / `Icon` (null = the usual look) instead of a `Default` value: items.json carries
  `showAs` only when set.
- Migration also runs after a scan (not only at start and after a restore): a library fence whose index was empty at
  start (first run, unreadable index) becomes game items as soon as a scan finds games.
- The scan also runs while Add games… is open, so the list fills on a PC with no game items yet.
- Add games… lists sources by their readable names (`GameCatalog.SourceName`).
- Settings → Game Library: "Hidden games — None." (the old hint pointed at the library fence's menu).

## Live check

(TEST-CHECKLIST AH — filled in after the run.)