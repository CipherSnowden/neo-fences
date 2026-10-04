# M7 — Installer: verification results

**Date:** 2026-10-03 · **Machine:** Windows 11 Pro 25H2 (26200), Takeover on.
**Build:** `m7-installer`, `build\pack.ps1 -Version 1.0.0` → `artifacts\releases\NeoFences.App-win-Setup.exe` (71.7 MB,
self-contained win-x64, unsigned). **Checklist:** `docs/TEST-CHECKLIST.md` section Q.

## How it was run

The agent ran `m7-install-test.ps1` (in the plan) with the user's consent: once on the prototype, once on the branch
build. It uses no mouse or keyboard:
1. stop the dev NeoFences cleanly;
2. run `Setup.exe --silent`;
3. check processes, the registry and files;
4. run the dev build while installed;
5. uninstall with the Apps-list `UninstallString` plus `--silent`;
6. check again;
7. start the dev build again.

The M6a smoke was re-run as a regression, because startup now goes through `InstallHooks.Run()` first.

## Results

| ID | Result | Evidence |
|---|---|---|
| Q1 | **Pass** | 248 tests, then a self-contained publish, then `Setup bundle created 'NeoFences.App-win-Setup.exe'` (71.7 MB). |
| Q2 | **Pass** (silent) | `%LOCALAPPDATA%\NeoFences.App\current\NeoFences.exe`, an Apps-list entry, a Start menu "NeoFences" shortcut. The installed NeoFences ran with the user's fences. Double-click and SmartScreen: [USER]. |
| Q3 | [USER] | The installed exe reports 1.0.0 (`1.0.0+<commit>` as the informational version). |
| Q4 | **Pass** | The sign-in entry pointed at the installed copy. HideIcons was 1 (Takeover). |
| Q5 | **Pass** | A dev build started while installed left the entry on the installed copy ("dev build keeps its hands off: True"). |
| Q6 | [USER] | A real restart or power cut with the installed copy. |
| Q7 | [USER] | Upgrade with a newer Setup.exe. |
| Q8 | **Pass** | Uninstall: NeoFences stopped, the program folder and the Apps entry gone, HideIcons 1 → 0 (icons visible), the sign-in entry removed. `%LOCALAPPDATA%\NeoFences` was kept, its config content identical (re-saved bytes only). |
| Q9 | covered by design | The hook shows the icons whatever ran before. In both runs Velopack had already closed NeoFences ("0 processes left after 0.0 s") and the icons still came back. |
| Q10 | **Pass** (data) | The data folder survived; reinstalling uses the same config. |

## Findings

1. **Velopack installs into `%LOCALAPPDATA%\<packId>` and deletes that folder on uninstall.** With the obvious packId
   `NeoFences`, uninstall would have deleted the user's layout, backups and logs. The packId is `NeoFences.App`.
2. **Velopack closes the app's own processes before the uninstall hook runs.** The hook's clean stop finds nothing to
   stop, and its unconditional icon restore is what keeps hard rule 2. The watchdog dies with the app (same folder),
   so it cannot restart a removed exe.
3. **`dotnet vpk` resolves only from the folder that holds `dotnet-tools.json`** (the repo root), so `pack.ps1`
   changes into the root first.
4. **Windows PowerShell 5.1 reads BOM-less UTF-8 scripts as ANSI**, so an em dash (`E2 80 94`) becomes a curly quote
   and breaks strings. Test scripts stay ASCII.
5. **The installed copy re-saves `config.json` at start** (same content, different bytes). Byte comparisons of the
   config are not meaningful; compare its content.
6. **Setup.exe over an existing install runs no hook of ours.** Only `Update.exe` (uninstall, UpdateManager apply)
   calls hooks; Setup force-closes the app and replaces the files. The update hook was unreachable and is removed.

## Final review (opus, whole branch)

0 critical. Fixed:
- I1: the dead update hook, and the docs, which now describe what Setup really does.
- I2: the uninstall icon restore now retries for up to 20 s, then sets HideIcons = 0 for Explorer's next start, and
  logging can no longer block it.
- I3: a dev build with no entry now points start-with-Windows at the installed copy.

The re-run install test passed, including the new "I3 no entry + dev build: entry -> installed copy True" step and
"uninstall: desktop icons shown (attempt 1)". The registry fallback (Explorer hung during uninstall) is not reproduced
live. Deferred minors are in ROADMAP.

## User check (2026-10-03)

The user uninstalled v1.0 and installed it again by hand. Desktop icons were hidden again automatically (Takeover from
the kept config) and everything worked: Q2, Q8 and Q10 confirmed outside the scripts. Q3: Settings → About shows 1.0.0. Q6: after a restart the installed NeoFences started by itself. Q7, Q9 skipped for now.
