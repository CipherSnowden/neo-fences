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

## Final review (Opus, 2026-10-05): with fixes

No critical findings. Fixed (Core test-first where Core can show it; App-only fixes have checklist rows AH14–AH18):
- **I1** — a not-installed game shown as a cover was neither dimmed nor badged (the triggers reached only the icon): the
  tile dims and shows the ⚠ badge (AH14).
- **I2** — a game item's target could be edited in Properties and the next scan silently undid it: the target box is
  read-only and Browse off for game items (AH15).
- **I3** — the first scan (no index) counted every game as new, so choosing "New games go to" dumped the library into
  that fence: no new games without an earlier scan (`NewGames_OnAFirstScan_AreNone`).
- **I4** — a migration whose save was cut short (power loss) or half read-only could empty the Games fence or double its
  games: no migration while a store is read-only, items saved before the config, and `Migrate` never adds a game the
  fence already holds (`Migrate_DoesNotDuplicateGamesTheFenceAlreadyHolds`).
- **I5** — a game that came back (drive plugged in, reinstall, Show again) was copied into the new-games fence though
  the user keeps it elsewhere: only games no fence holds are added (`NotInAnyFence_DropsGamesSomeFenceAlreadyHolds`;
  AH18). ADR-045's consequence reworded.
- **M2 → fixed** (re-graded: Enter removed an item) — the "not installed" question has no default button (AH16).
- **M3 → fixed** (re-graded: Delete in Windows' menu recycled NeoFences' own shortcut, the game then "not installed") —
  Delete there removes only the item (AH17).
- Coverage added for focus 3: `Retarget_UpdatesEveryCopyOfAGame_InEveryFence`.

Deferred minors:
- Deleting a fence does not refresh an open Settings window or stop an idle scan (a stale "New games go to" entry could
  be picked while Settings stays open).
- A game whose shortcut is renamed may show "Not installed" for a moment until the scan finishes.
- The "New games go to" list is rebuilt on every Settings refresh (an open dropdown closes when a scan lands).
- "Open install folder" stays enabled for a not-installed game (it only logs).
- No test for two library fences migrating at once (the normalizer allows only one).

Set aside by the reviewer, ruled to stand: restoring "Before games became items" migrates again (by design, AH12);
`AddNew` checks only its fence (manual Add games… may add a game kept elsewhere — wanted); covers decoded on the UI
thread (ponytail note); the kept library fence kind and collapsed menu entry (the user's instruction); migration retried
after scans when the snapshot fails (harmless); no way to hide a game from the item menu (out of scope); no tooltips with
labels on hover (existing design; the badge covers it now); cover decode failures logged per reload (existing); scanners
dropping games on unplugged drives (existing; its consequence fixed by I5); two fences with the same title in the list
(spec silent); `FindResource` for the Fluent brush (the pattern DesktopFillWindow uses).

## Live check

(TEST-CHECKLIST AH — filled in after the run.)