# M33 — Safety net (0.20.0): build notes and results

Spec: `docs/superpowers/specs/2026-10-06-safety-net-design.md` · Plan: `docs/superpowers/plans/2026-10-06-m33-safety-net.md`

## Build

- Core first (test-first, 700 → 714 tests): `CrashRecovery.Decide` (normal / safe mode / stop and ask), `RunState`
  safe mode and gesture switches (both off: no mouse hook), `Undo` (one level: removals and fence deletions),
  `JsonStore` (a locked file retried 4 times about 1.6 s apart; any parse or repair failure is damage), `LogPrivacy`.
- Shell and App: start modes `--restarted`, `--safe-mode`, `--stopped`; the "NeoFences stopped" window; the watchdog keeper
  (`watchdog-<pid>` file, a new watchdog when it ends); the icons-hidden marker written before hiding; the undo bar and
  Ctrl+Z; save problems as a notice once and a Settings banner; Logs at 10 MB per file with `%USERPROFILE%`.
- Calls made while prototyping (rulings): removing several items no longer asks (the undo bar replaces it); Start from a
  backup uses the newest daily backups after a snapshot of the current setup; in safe mode widgets keep their last drawn
  state and panels show as plain folder items; the test crash is `NEOFENCES_TEST_CRASH=1` in Debug builds only.
- Replay-verified on `main` (49 build errors before the Core code, 714 tests after, 0 warnings).

## Review (Opus, whole branch)

0 critical, 8 important, all fixed: safe mode still hid desktop icons and read sensors (a widget tick from game end,
unpause or a rolled-up stats fence); the draw switch off still took every desktop right-press; the keeper could start a
second watchdog when the first was slow to show (a clean Exit would then restart NeoFences) — it now replaces only a
watchdog it saw end; a failed config save skipped the items save; every save waited on a locked file with the 4.8 s
start-up retry (`Save` now probes once, test `Save_ProbesTheFileOnce_…`), and on session end the icons now come back
before the save; notices held by a game showed only with the next notice; a crash while loading was unwatched (the
watchdog now starts first). 715 tests. Deferred minors: log masking at line ends and in JSON-escaped paths; the 7-file
log limit with size rolling; restored items not re-checked; "New games go to" not restored; "Before deleting" snapshots
not pruned; one banner at a time; Peek's click-outside needs the mouse hook (gestures off or safe mode); Start from a
backup edge cases (locked file wait, newer-version config, half copy); a failed folder listing keeps its watcher; PID
reuse in the keeper; a repeating "icons not hidden" notice.

## Live check (branch builds, backed-up data; Debug with `NEOFENCES_TEST_CRASH=1` for AS1–AS4)

| ID | Result |
|---|---|
| AS1 | **Pass**: 4 unclean exits restarted ("main restarted (unclean exit)"), about 5 s apart. |
| AS2 | **Pass**: safe mode — tray "Leave safe mode" first, the Settings banner, no `WH_MOUSE_LL installed` line. |
| AS3 | **Pass after a fix**: the stopped window showed after the safe-mode crash, but its **Close** did nothing (`IsCancel` closes only a dialog; the window is modeless) — fixed with a click handler; after Close no NeoFences process is left. |
| AS4 | **Pass**: "Before starting from a backup" snapshot, the newest daily backups in place, safe mode started. |
| AS5 | **Pass**: a killed watchdog was replaced at once ("the watchdog is not running; starting another"); none left after Exit. |
| AS6 | **Pass**: Del → "Removed 1 item" bar; Undo and Ctrl+Z each put the item back (19 → 18 → 19). |
| AS7 | **Pass**: Delete fence asked nothing, took a snapshot; tray "Undo delete (Apps)" and Ctrl+Z each brought it back with its 19 items. |
| AS8 | **Pass**: config.json locked at start → "read-only: true" from the backup, the banner, the file untouched at exit. |
| AS9 | **Pass**: both switches off → "WH_MOUSE_LL removed"; draw on again → installed. |
| AS10 | **Pass**: the test builds wrote the profile path only as `%USERPROFILE%` (0 unmasked lines). |
| AS11 | **Pass**: Leave safe mode → a normal start with the mouse hook. |

Keys reach a fence only when the desktop had focus before the click (ADR-015); from another app Del and Ctrl+Z go to
that app, while the undo bar and tray Undo delete always work (GUIDE says so now). A click on a game tile in the Games
fence left the desktop in front even then (pre-existing; for the M35 menus and keyboard work). Further minors: "There is
no daily backup yet" in the stopped window is replaced by safe mode at once; the watchdog's "restart limit reached: safe
mode" wording also shows for a safe-mode retry after Start from a backup; the stopped window does not answer `--exit`.
