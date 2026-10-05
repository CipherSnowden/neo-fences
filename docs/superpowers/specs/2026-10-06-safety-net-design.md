# M33 — Safety net (0.20.0) — design

**Date:** 2026-10-06 · **Status:** approved in brainstorm, awaiting written-spec review
**Decisions:** ADR-053 (safe mode after a crash loop), ADR-054 (one-level undo for removals and deletions)
**Source:** `docs/research/v1-readiness.md` (blockers 1, reliability must/should items)

## Goal

On a stranger's PC NeoFences must fail visibly and recoverably: no silent disappearance after crashes, no silently lost
changes, no destructive action without an undo. Where nothing goes wrong, behaviour stays as today. Hard rules stand
(files never touched; desktop icons always come back; no new dependency).

The user chose: automatic safe mode after a crash loop, then a message window if safe mode crashes too; an undo bar and
Ctrl+Z for removals and fence deletions (deleting a fence no longer asks); gesture switches defaulting to today's
behaviour (on).

## 1. Crashes and safe mode

- **Safe mode.** The watchdog keeps counting unclean exits (3 within 10 minutes, `RestartThrottle`). Where today it
  stays down, it starts NeoFences once more with `--safe-mode`. In safe mode:
  - fences show and items open, edit and save as usual;
  - off: hiding desktop icons (shown if hidden), the mouse hook (desktop gestures), widgets, folder panels (shown as plain
    folder items), auto-collect, the game library scan, wallpaper-accent watching;
  - a notice "NeoFences started in safe mode — it stopped unexpectedly several times. Fences work; extras are off." and
    a Settings banner with **Open logs**; the tray menu leads with **Leave safe mode** (restarts normally, clearing the
    crash count).
  - The rules (what runs in which mode) are one Core record (`RunState.SafeMode`), test-first.
- **If safe mode crashes too** (the next unclean exit within the window), the watchdog does not restart; it shows a
  window "NeoFences stopped after repeated crashes" with **Open logs**, **Start from a backup** (a snapshot "Before
  starting from a backup" first, then the newest daily backups of `config.json` and `items.json` become current, then
  NeoFences starts in safe mode) and **Close**. Never a loop: one window, one start.
- **The watchdog stays alive.** The main app waits on the watchdog's process handle (background thread) and launches a
  new watchdog if it ends while NeoFences runs (not during shutdown).
- **Marker before hiding.** Desktop icons are hidden only after the `icons-hidden` marker was written; if it cannot be
  written, icons stay visible, a notice explains, and "Hide desktop icons" stays on for the next try (hard rule 2).
- **Updates after a crash.** A start by the watchdog (`--restarted` or `--safe-mode`) checks for an update at once
  instead of after a minute; a ready update is offered as usual ("Restart to update"), also in safe mode.
- **Background failures.** `TaskScheduler.UnobservedTaskException` is logged once per kind; the folder lister's refresh
  task and the target-check continuation are guarded so a failure ends that refresh, never the panel or the app.

## 2. Saving and undo

- **Save failures are shown.** A failed save of `config.json` or `items.json`, a load that fell back to `.bak` or a
  daily backup, and a session that cannot save show a tray notice and a Settings banner (as snapshot failures do today),
  once per cause until it changes.
- **A locked file at start** (antivirus, OneDrive) is retried 3 times over about 5 seconds before NeoFences decides the
  session is read-only.
- **Damaged files fall back.** Any exception while reading, parsing or repairing a file (not only `JsonException`;
  never out-of-memory) counts as damaged: the `.bak` and then the daily backups are tried, the damaged copy is kept.
- **Undo.** One level, in memory: the last removal of items or deletion of a fence keeps the config and items from
  before it (`UndoEntry(Label, Config, Items)`, Core, test-first). **Ctrl+Z** in any fence restores it; a new removal
  replaces it; a restore, an import or a snapshot restore clears it.
  - Removing items (Del, menu): a bar at the bottom of that fence for about 10 seconds: "Removed 3 items · **Undo**".
  - Deleting a fence: no confirm box any more; a snapshot "Before deleting <name>" is saved first (it survives a
    restart), then the fence goes; a notice "Fence <name> deleted — Ctrl+Z or tray → Undo delete" and a tray entry
    **Undo delete** for 2 minutes. If the snapshot cannot be saved, the delete asks as today.

## 3. Switches and logs

- **Gesture switches** (Settings → General): "Double-click the desktop to quick-hide" and "Right-drag on the desktop to
  draw a fence", both on by default. With both off the mouse hook is never installed (`RunState.MouseHookWanted`).
- **Logs** are capped at 10 MB per file (rolling over, 7 days kept), and the user profile path (`C:\Users\<name>`) is
  written as `%USERPROFILE%`, so a shared log does not carry the Windows user name. The watchdog's log too.

## Testing

- Core (xUnit, test-first): `RunState` in safe mode (hook, icons, widgets, panels, collect, library off); the
  watchdog's decision (restart, safe mode, stop and ask) from the crash history and the last start's mode; the undo
  entry (remove, delete, Ctrl+Z, cleared by restore); file loading treating any parse/repair exception as damaged; the
  profile-path mask; the mouse-hook rule with both gestures off.
- Live check (asked first; a test build on a backed-up data folder): forced crashes (a test-only crash switch in Debug
  builds) → safe mode notice and tray entry → crash in safe mode → the window and Start from a backup; the undo bar and
  Ctrl+Z; delete fence + Undo delete; a locked config at start; gestures off → no hook in the log.

## Out of scope

Crash reports sent anywhere (no network); a full undo history; automatic rollback of a bad update by the watchdog.
