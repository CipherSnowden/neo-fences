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
