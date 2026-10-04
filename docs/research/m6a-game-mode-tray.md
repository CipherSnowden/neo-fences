# M6a — Game mode, tray, Pause: verification results

**Date:** 2026-10-03 · **Machine:** Windows 11 Pro 25H2 (26200), one 1920×1080 monitor at 100 %, Takeover on.
**Build:** `m6a-game-mode-tray` (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section O.

## How it was run

The agent ran `m6a-smoke.ps1` (in the plan) with the user's consent: on the prototype, then on the branch build. It
ran under Windows PowerShell, because its stand-in game is compiled with Add-Type.
- **Tray clicks** are simulated by posting the tray callback message to `NeoFences.SystemMessages` (left click,
  `NIN_SELECT`). Windows 11 may keep a new icon in the overflow.
- **Menu items** are clicked by their `GetMenuItemRect` rects, never typed.
- **The game** is a borderless window covering the monitor, on its own UI thread.

The M5 gesture smoke was re-run as a regression.

## Results

| ID | Result | Evidence |
|---|---|---|
| O1 | [USER] | Seeing the icon in the notification area. |
| O2 | **Pass** (menu) | `New fence \| Quick-hide \| Peek Ctrl+Alt+Space \| \| Pause NeoFences \| \| Exit NeoFences`. Whether a right-click on the real icon opens it too is [USER]. |
| O3 | **Pass** | Pause hid all 4 fences. The log showed `desktop icons hidden: False` and `WH_MOUSE_LL removed`. The paused menu had Pause checked and New fence grayed. Resume brought back the 4 fences, `desktop icons hidden: True` and the hook. |
| O4 | **Pass** (New fence) | Tray → New fence added a fence. Quick-hide and Peek from the tray use the same code as M5. |
| O5 | **Pass** (stand-in) | `game mode: True` within the 1/2.5/5 s re-checks, `WH_MOUSE_LL removed`. After the window closed: `game mode: False`, hook installed. A real game is [USER]. |
| O6 | **Pass** (stand-in) | Ctrl+Alt+Space in the game logged no Peek. A file saved to the Desktop during the game was not in the Inbox; after the game it was. |
| O7–O10 | [USER] | Explorer restart, lock/unlock, full-screen video, Exit. |
| M5 regression | **Pass**, with one finding | Every line True except the roll-up after Explorer closes; see finding 4. |

## Findings

1. **`LoadIconMetric` crashed the app at start.** It lives in comctl32 v6, and a WPF app does not load comctl32 v6
   (no manifest dependency), so the call threw `EntryPointNotFoundException`. The watchdog kept restarting the
   prototype.
   - Now the icon loads with `LoadImage` (user32), at `SM_CXSMICON` for the system DPI.
   - Tray setup is wrapped, so a tray failure only loses the tray (hard rule 7).
2. **A borderless full-screen window flips the notification state 2–3.5 s after it activates** (the M0 game took
   ~1 s), with no further foreground event. The re-checks run at 1, 2.5 and 5 s.
3. **PowerShell passes `$null` as `""` to string P/Invoke parameters**, so `FindWindow($null, title)` finds
   nothing. The smoke uses `[NullString]::Value`.
4. **The first title double-click on a fence that just became the foreground does not roll it up; the second does.**
   - Seen when closing a maximized Explorer window hands the foreground to a fence.
   - Reproduced on "Games" by `m6a-rollprobe2.ps1`. Without the Explorer step, the first double-click works.
   - It is M5 behaviour, unchanged by M6a. A ROADMAP carry-over: investigate whether the first click enters the
     caption move loop of an active window.

## Final review (opus, whole branch)

0 critical. Fixed:
- I1: a game going full screen after the last re-check was missed. Now a 5 s poll runs, plus a check before Peek.
- I2: tray click format and retry after a busy Explorer.
- I3: tray New fence while quick-hidden made an invisible fence.
- M1 (re-graded): Pause now also restores icons left hidden by a failed show.
- M2 (re-graded): Desktop changes queued in a game are now saved on exit and session end.

A live fix check (`m6a-fixcheck.ps1`) passed: tray New fence while quick-hidden is visible; a stand-in game windowed for
8 s, then full screen, turned game mode on via the poll; exiting mid-game saved the queued Desktop change. Deferred
minors are in ROADMAP.
