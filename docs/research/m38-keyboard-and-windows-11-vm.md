# M38 — Keyboard way in and the Windows 11 VM (research note)

**Date:** 2026-10-07 · **Spec:** `docs/superpowers/specs/2026-10-06-keyboard-and-older-windows-design.md` (amended by
ADR-059: Windows 11 only) · **Decision:** ADR-060 · **Plan:** `docs/superpowers/plans/2026-10-07-m38-keyboard-way-in-and-windows-11-vm.md`

## The keyboard probe

The spec's risk: a fence is `WS_EX_NOACTIVATE` and cannot take the keyboard while another app is in front (ADR-015). The
prototype gave the keyboard with `SetForegroundWindow` right after the Peek hotkey — a process that just received its
registered hotkey may bring a window forward. Probed on a copy of the owner's data (owner's OK):

- From Firefox: Ctrl+Alt+Space put the keyboard in the fence under the mouse (outline and item ring shown); arrows moved
  between items; Esc sent the fences back and Firefox had the keyboard again.
- From a full-screen app: the same.

So fences did not need to become focusable while peeking (the spec's fallback); they stay non-activating otherwise.

## The Windows 11 VM

`NF-Win11`: Windows 11 Pro build 26300 from Microsoft's ISO, Hyper-V Gen 2 with a virtual TPM, 4 GB RAM, 2 cores, a
dynamic 64 GB disk on `D:\NeoFences-VMs`, built by `tools/vm/New-TestVm.ps1` (DISM apply, an answer file, auto-sign-in of
a local "tester", a `clean` checkpoint). The checklist ran five times on 0.24.0 → 0.25.0-proto.1:

| run | result | cause |
|---|---|---|
| 1 | stuck | the restored checkpoint resumed to a lock screen; two waiters started |
| 2 | stuck at install | PowerShell 5.1's `Start-Process -Wait` waits for the whole process tree — Setup starts NeoFences, which never exits |
| 3 | 7 of 12 | checks ran before the first fence and `config.json` existed; Windows 11's Notepad (a Store app) not found by process; game mode off |
| 4 | 10 of 12 | game mode off — Windows reported "quiet time"; the keyboard check ran on 0.24.0 (before the update) |
| 5 | **12 of 12** | — |

Run 5:

| check | result |
|---|---|
| install, welcome | 0.24.0 silent install; one welcome fence |
| behind-windows, show-desktop | fence behind Notepad; still shown after Win+D |
| icons-hidden, -after-exit, -after-kill, explorer-restart | hidden; back after exit; back after a kill (the watchdog restarts NeoFences); fences re-attach, icons stay hidden |
| game-mode | on behind full-screen Edge (Windows reports "busy") |
| update | 0.24.0 → 0.25.0-proto.1 from a local feed, icons still hidden |
| peek-keyboard | keyboard in a fence; Esc gives it back to Notepad |
| uninstall | app gone, icons shown |

No NeoFences problem was found; every failure was in the test tooling.

## Lessons (kept in `tools/vm/`)

- **Hyper-V on**: `Enable-WindowsOptionalFeature` fails with "Class not registered" in PowerShell 7; `dism.exe /Online
  /Enable-Feature /FeatureName:Microsoft-Hyper-V /All` works everywhere.
- **Restoring a checkpoint** resumes its saved memory, and Windows asks for a sign-in (a locked desktop takes no keys or
  screenshots): `Remove-VMSavedState` after the restore boots fresh, and the tester signs in by itself.
- **One waiter**: the first sign-in's command and the Run key both started it — a named mutex keeps one.
- **Enhanced session** in the VM window is a remote sign-in that shows a password box over the signed-in console; signing
  in there would take the desktop from the checks. Untick View → Enhanced session to watch.
- **`Start-Process -Wait`** in Windows PowerShell 5.1 waits for every descendant: wait for the started process itself
  (`-PassThru` and `.WaitForExit()`).
- **Windows 11's Notepad** is a Store app: `notepad.exe` hands over and exits, and a second start may open a tab — find the
  window by its title.
- **Quiet time**: for the first hour after an account first signs in, `SHQueryUserNotificationState` reports "quiet time"
  instead of a full-screen app, so game mode cannot engage. A real user's account is long past it; the VM's tester is new,
  so the README says to wait an hour after building the VM.
- **Game mode** is checked with Edge `--kiosk about:blank --edge-kiosk-type=fullscreen` (a borderless window from a script
  did not count as full screen, M37).

## Calls made while prototyping

- Reading order: rows are fences whose tops lie within half the shortest height of the row's first fence; the main screen
  first, the others left to right; a box (tabs) counts once; `Next` from a fence that is gone gives the first.
- The app to give the keyboard back to is not remembered when NeoFences' own window (tray menu, Settings) had it; the
  keyboard goes back only on Esc or the hotkey (a click elsewhere or an open leaves it where Windows puts it).
- The item ring shows only in keyboard mode (`ItemFocusVisibility`), so mouse use looks as in 0.24.0; outline and ring use
  the selection accent; a rolled-up fence counts the keyboard as "the mouse inside".
- One VM (ADR-059); the guest checks run from a waiter at the tester's sign-in (a desktop session is needed for keys and
  screenshots); the spec's VM step "Tab to another fence, Enter opens an item" is checked by hand (AX3).
- A damaged or missing `results.json` reads as one failed row, "no results (the test pass did not finish)".

## Final review fixes

The whole-branch review (fresh reviewer) found 0 Critical, 6 Important, 7 Minor. Fixed: Esc during Peek now cancels a
rename or closes the Properties dialog first (the global Esc is registered only while another app is in front; a fence
with the keyboard handles Esc itself); the keyboard goes back only while NeoFences still has it (an app switched to
meanwhile keeps it); Settings is remembered as the window to return to, and with nothing to return to the desktop gets
the keyboard; a rolled-up fence opens for the keyboard in click mode too (`RollUpExpansion.Open`); the VM host script
always turns the VM off and prints a report; a pass without `"finished": true` is a failed report. Deferred minors: the
first item stays selected in fences Peek visited; `KeyboardSpots` could use `FencePlacement.ContainingMonitor`; deleting
the fence with the keyboard leaves Tab idle until the next Peek; a folder panel's list needs the mouse; one failed
screenshot stops the VM checks; two more `KeyboardOrder` cases.

## Live check

2026-10-07, the branch's Release build on a backed-up copy of the owner's data (four fences: Desk, Games, Apps,
Downloads; restored afterwards, the installed copy started again):

| check | result |
|---|---|
| AX2 Peek from Notepad, pointer on the taskbar | PASS — the keyboard in Desk (outline and item ring shown), arrows move; Esc back to Notepad |
| AX2 pointer over Desk | PASS — the keyboard in Desk |
| AX3 Tab through every fence | PASS — Desk > Games > Apps > Downloads > Desk; Shift+Tab back to Downloads |
| I1 F2 Properties during Peek, Esc, Esc | PASS — the first Esc closed the dialog (still peeking), the second ended Peek, Notepad had the keyboard |
| I2 switched to Paint during Peek, Esc | PASS — Peek ended, Paint kept the keyboard |
| I4 Apps rolled up, click mode, Peek over it | PASS — opened (32 → 432 px) with the keyboard, closed after Esc |
| AX4 full-screen Edge during Peek | PASS — game mode on, Peek ended |

**VM pass (AX1)**, the same day: the branch packed as 0.25.0-rc.1 (`e8ef390`, with the review fixes) in `NF-Win11` —
**12 of 12** (install 0.24.0, welcome, behind windows, Win+D, icons after exit / kill / Explorer restart, game mode "busy",
update to 0.25.0-rc.1, Peek and the keyboard, uninstall); the guest marked its results finished.

Not scripted (by hand later): Enter opening an item (it would open the owner's real items), Ctrl+Tab in a tabbed fence,
Peek from Settings or the tray menu, a fence deleted while peeking.
