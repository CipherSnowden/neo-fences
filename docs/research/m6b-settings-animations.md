# M6b — Settings window and animations: verification results

**Date:** 2026-10-03 · **Machine:** Windows 11 Pro 25H2 (26200), one 1920×1080 monitor at 100 %, dark mode, Takeover on.
**Build:** `m6b-settings-animations` (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section P.

## How it was run

The agent ran `m6b-smoke.ps1` (in the plan) with the user's consent: on the prototype, then on the branch build. It ran
under Windows PowerShell.
- The tray menu opens by posting its callback message.
- Settings controls are driven through UI Automation (focus, the Toggle pattern) plus keys sent to the settings window
  only.
- Click mode and the animation run on the user's "Games" fence.

The M6a smoke and the M5 gesture smoke were re-run as regressions.

## Results

| ID | Result | Evidence |
|---|---|---|
| P1 | **Pass** (tray) | Tray → Settings… opened "NeoFences settings". The fence-menu entry uses the same code. |
| P2 | [USER] | Light/dark switch with the window open. The screenshot shows Fluent dark, the user's accent on the checkboxes, and the app icon in the title bar. |
| P3 | [USER] | Settings ↔ fence-menu agreement. |
| P4 | **Pass** | Ctrl+Shift+F9 recorded: saved and registered, and pressing it opened Peek. Set back to Ctrl+Alt+Space. |
| P5 | [USER] | A taken or invalid combination. |
| P6 | **Pass** | Click mode saved. On "Games": resting did not open it, a click opened it (full height), and it closed after leaving. |
| P7 | [USER] | A locked fence in click mode. |
| P8 | **Pass** | Roll-up animated: 223 → 36 px mid-way → 32 px. Quick-hide (with the fade) ended hidden, then all fences came back. |
| P9 | [USER] | Animation effects off. |
| P10 | **Pass** (toggle) | Game mode off was saved, and on again. With a real game: [USER]. |
| P11, P12 | [USER] | Open folders; Narrator. |
| Regressions | **Pass** | M6a: all True. M5: all True except the known carry-over (roll-up right after Explorer closes, research/m6a finding 4). |

## Findings

1. **UI Automation's Toggle pattern does not raise `Click` on a WPF CheckBox.** It calls `OnToggle`, not `OnClick`.
   The first prototype listened to Click, so a game-mode change made through UI Automation (as Narrator does) was
   never applied. The settings checkboxes now listen to Checked/Unchecked, guarded while the host fills them.
2. **The Fluent theme works per window** (`ThemeMode="System"` in XAML). It raised no experimental-API error, and
   the fence windows keep their own look.
3. **Windows' move loop keeps the rect it captured when the move began.** Resizing the window in `WM_ENTERSIZEMOVE`
   is undone by the next `WM_MOVING`, so a fence dragged within 200 ms of unrolling saved a partial height (198 of
   223 px). `WM_MOVING` now pins the finished height.

## Final review (opus, whole branch)

0 critical. Fixed:
- I1: the recorder accepted combinations that break typing or system keys (Shift+Tab, Alt+F4, Ctrl+C, AltGr) and trapped
  Tab and Alt+F4. Now `IsSafeToRecord` (16 tests) applies and those keys pass through.
- M2 (re-graded): drag during the animation, finding 3.
- Part of M4: virtual key 0 refused.

The live fix check (`m6b-fixcheck.ps1`) passed: Shift+Tab moves on; Ctrl+C and Ctrl+Alt+A are refused with a message;
Alt+F4 closes Settings; the hotkey is unchanged; a drag during the unroll stores 223 px. Deferred minors are in ROADMAP.
