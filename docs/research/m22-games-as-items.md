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

Deferred minors (fixed in 0.12.1, M23 — see ARCHITECTURE and checklist AI):
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

2026-10-05, branch build on a copy of the user's data (restored afterwards), scripted while the PC was unattended;
screenshots sent to the user.

- **Pass (all 18):** AH1 (the "Games" library fence became an items fence with the 12 games, the safety snapshot listed,
  new games go to Games), AH2 (no "New Game Library fence"; "Add games…" in the fence menu), AH3/AH4 (Blur Ctrl-dragged
  into Apps as a cover, Icon, back to Cover), AH5 (menu; Open install folder opened Forza Horizon 6's folder), AH6 (own
  name under the cover), AH7 (12 games, Blur "already here" unticked), AH8 (a test game folder in `D:\GameLibrary` → one
  new item in Games, none in Apps), AH9 (removed → "… is not installed" with Remove from fence / Cancel, no Locate…),
  AH10 (back by itself), AH11 (Nowhere: no item added), AH12 (restoring the pre-migration snapshot migrated again, a
  second safety snapshot), AH13 (deleting Games → New games go to: Nowhere), AH14 (the not-installed cover dimmed with
  the ⚠ badge, labels always and on hover), AH15 (target read-only, Browse off), AH16 (Enter does nothing), AH17 (Windows'
  menu → Delete removed only the item; NeoFences' shortcut stayed), AH18 (a game moved to Apps came back as "new": no copy
  in Games).
- Script note: the first pass lost track of the Games fence after AH6 renamed PRAGMATA; AH3/AH4, AH9/AH14/AH16 and AH18
  were rerun on reset data with title-based finders.
- Test game folders (`D:\GameLibrary\NeoFences-test-game*`) created and removed by the script only.