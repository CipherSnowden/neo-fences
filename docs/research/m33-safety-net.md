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
