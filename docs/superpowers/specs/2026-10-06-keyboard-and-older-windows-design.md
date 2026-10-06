# M38 — Keyboard way in and older Windows (0.25.0) — design

**Date:** 2026-10-06 · **Status:** approved in brainstorm, awaiting written-spec review
**Decisions:** ADR-059 (Peek takes the keyboard; a scripted test pass in Hyper-V VMs). Supersedes ADR-015's "keyboard focus
from an app is a ROADMAP follow-up".
**Source:** `docs/research/v1-readiness.md` (blocker 2: never run on another PC — Windows 10, Windows 11 before 24H2;
Compatibility and accessibility: no keyboard way into the fences, invisible keyboard focus).
**Owner's picks:** the older-Windows test pass and the keyboard way in (not mixed-scaling monitors, not screen reader / High
Contrast); Peek takes the keyboard (no second hotkey); VMs with Windows 10 22H2 and the current Windows 11; the test pass
scripted.

## Goal

From any app, the Peek hotkey puts the keyboard into the fences with a visible focus, and Esc gives it back. NeoFences is
proven to install, run, keep the desktop icons safe, update and uninstall on Windows 10 22H2 and a fresh current Windows 11,
by a test pass that runs by itself in two virtual machines and can run again for every release candidate. Hard rules stand:
desktop icons always come back; user files never touched; no new NuGet dependency; Win32 only in Shell; Core test-first.

## 1. Keyboard way in (through Peek)

- **The Peek hotkey** (Ctrl+Alt+Space by default) brings the fences above the windows as today and now also puts the
  keyboard in a fence. NeoFences remembers the app that had the keyboard. The fence: the one under the mouse; else the one
  last used from the keyboard; else the first in reading order (left to right, top to bottom, the main screen first).
- **Inside a fence**: arrows, Home and End move between items; Enter opens; the menu key or Shift+F10 opens the item menu;
  F2, Del and Ctrl+Z as now; Ctrl+Tab switches the fence's own tabs as now. **Tab / Shift+Tab** go to the next / previous
  fence in reading order (wrapping at the ends); a rolled-up fence opens while it has the keyboard.
- **Esc** ends Peek: the fences go back down and the remembered app gets the keyboard back. A click elsewhere ends Peek as
  today.
- **Visible focus, only when the keyboard is used**: the item under the keyboard shows a clear accent ring (the selection
  look and a focus outline); the fence that has the keyboard shows an accent outline.
- The mouse is unchanged; Peek stays off in games (game mode).
- **The risk**: a fence normally cannot take the keyboard while another app is in front (ADR-015). A process that received
  a hotkey may bring its window forward; the prototype proves it. If not, fences become able to take focus only while Peek
  is on.

## 2. Older-Windows test pass (scripted, kept in the repo)

- **Machines**: two Hyper-V VMs on `D:\NeoFences-VMs` — Windows 10 22H2 and the current Windows 11 — Gen 2 with a virtual
  TPM, 4 GB RAM, 2 cores, a dynamically growing 64 GB disk (about 20–25 GB used each), unactivated (fine for testing).
- **`tools/vm/New-TestVm.ps1`**: writes Windows from the ISO straight onto the VM's disk (DISM), with an answer file that
  skips setup and signs in a local "tester" account automatically; the guest test script is placed on the disk before the
  first boot. No clicking through Windows Setup.
- **`tools/vm/Invoke-VmChecks.ps1`**: starts the VM, copies in the installer under test and the previous release, and lets
  the guest script run on the tester's desktop at sign-in; it works through the checklist, takes screenshots and writes a
  results file that the host reads (PowerShell Direct) into a report.
- **The checklist in each VM**: (1) install the previous release (Setup.exe): the first-run welcome and the tray icon;
  (2) fences stay behind a Notepad window, and Win+D (show desktop) leaves them visible — the old desktop-layer path on
  Windows 10; (3) hide desktop icons; they come back after an exit, a kill (the watchdog) and an Explorer restart (fences
  re-attach); (4) game mode on behind a full-screen Edge window, off after; (5) update to the release candidate from a local
  folder; (6) Peek and the keyboard: Tab to another fence, Enter opens an item, Esc returns; (7) uninstall: icons visible,
  NeoFences gone.
- **The owner's part, asked first**: turning on Hyper-V (admin and a reboot); downloading the two ISOs from Microsoft's
  pages.
- **Problems the VMs find** are fixed in M38 (a Core test where possible, else a checklist row); one that cannot be fixed in
  M38 is told to the owner and recorded for the release candidate, never dropped silently.

## Testing

- **Core, test-first**: the reading order of fences (several monitors, overlapping fences, a box's tabs counting once,
  rolled-up fences); which fence the keyboard starts in (under the mouse, last used, first); Tab / Shift+Tab wrapping; the
  guest results file read into a report.
- **Prototype probe** on a copy of the owner's data (asked first): Peek → keyboard from Firefox and from a full-screen app;
  Esc returns to the app.
- **Live check** on the owner's PC (checklist section AX); the **VM pass** on both VMs (screenshots sent, results in the
  research note).

## Docs

GUIDE (Peek takes the keyboard, the keys, the cheat-sheet) in the same commit as the change (ADR-050); ADR-059;
architecture, features, checklist AX, research note; `tools/vm/README.md`.

## Out of scope

Two monitors with mixed scaling; screen reader names and High Contrast; languages other than English; ARM64; Windows 11
23H2 (no longer on Microsoft's download page; Windows 10 covers the same desktop-layer path).
