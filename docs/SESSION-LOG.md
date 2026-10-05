# Session log

Append-only, newest at the bottom. One entry per session:

```
## YYYY-MM-DD — <topic>
**Done:** …
**Decisions:** ADR-xxx …
**Next:** …
**Open questions:** …
```

---

## 2026-10-02 — Kickoff: stack, design, docs

**Done:** Brainstormed with user. Chose stack, icon model, v1 scope, look, first-run behaviour.
Wrote v1 spec (`superpowers/specs/2026-10-02-neofences-v1-design.md`), CLAUDE.md,
ARCHITECTURE, DECISIONS (ADR-001..009), FEATURES, ROADMAP, SETUP.
**Decisions:** ADR-001 .NET 10 + WPF · ADR-002 Takeover + Portals, no injection · ADR-003
top-level bottom-z windows with acrylic · ADR-004 v1 scope (core + Quick-hide + Peek; all
existing icons → Inbox on first run; acrylic card look) · ADR-005 watchdog · ADR-006 JSON
config · ADR-007 LL mouse hook only · ADR-008 docs workflow · ADR-009 name provisional.
Later same session: user installed .NET 10 SDK (10.0.401). Built and published the project hub
artifact (ADR-010, <private hub link: see CLAUDE.local.md>).
User asked about Flutter/Dart: evaluated and rejected for v1 (no stable multi-window API in
Flutter 3.44.8; COM servers need a C++ plugin); noted in ADR-001 alternatives. Spec approved.
Wrote the M0 plan (`superpowers/plans/2026-10-02-m0-desktop-layer-spike.md`); its code was
compile-verified in a scratch project (CsWin32 0.3.335 needs PlatformTarget x64; generated types
are internal). New risk found while planning: DWM acrylic may turn solid while a window is
inactive (fences never activate). M0 tests 3 backdrop modes, and also covers sign-out with icons
hidden (SessionEnding restore).
**Next:** User reviews M0 plan and picks execution method → execute M0.
**Open questions:** Win+D strategy + Explorer menu suppression unproven until M0.
User context: dev + gamer, Wallpaper Engine user, prefers visual explanations with examples
(ASCII diagrams / previews) and detailed reasoning on doubts and trade-offs.

## 2026-10-02 — M0 desktop layer spike

**Done:** Built `spikes/M0.DesktopLayer` (throwaway) inline from the plan, with 18 unit tests. Ran
TEST-CHECKLIST A–F with the user (single monitor, Wallpaper Engine, AC Black Flag running in
between); results in `docs/research/desktop-layer.md`. Fixed a watchdog stale-marker bug found by
the smoke run. Added layered-window variants when every DWM-glass backdrop stayed flat.
**Decisions:** ADR-011: layered window + accent blur (DWM backdrops go flat when inactive),
owned-by-Progman z-order (raise-on-Win+D loses the race), S2 right-click suppression (S1 locks the
desktop), detached watchdog + explicit session-end handling, foreground-keyed game mode.
ADR-003 partly superseded; spec §10 erratum added.
**Next:** Final branch review + merge, then the M1 plan (Core + config).
**Open questions:** Real sign-out test (C8) deferred to M2 (would end the Claude session). Layered
window drag smoothness and corner rounding still to judge in M2.

## 2026-10-02 — M1 core + config

**Done:** `NeoFences.Core` + `NeoFences.Core.Tests` (58 tests): immutable config model, JSON,
normalizer, atomic ConfigStore with daily backups and corrupt recovery, display fingerprints and
layout scaling, fence membership (reconcile, add/remove/rename/move, create/delete fence).
**Decisions:** ADR-006 amended (reflection-based JSON, layouts store monitor work areas).
Takeover defaults to off (ADR-011).
Final review (fresh reviewer, 14 probe tests): 0 Critical, 5 Important + 3 re-graded Minor, all fixed
test-first (71 tests): unusable monitor work areas rejected, broken layouts repaired, locked config
loads read-only from backup, newer schema checked on every save, incomplete desktop listings keep
memberships (Suspicious flag), renames onto existing refs dedupe, Inbox forced desktop, bad enum
numbers defaulted. 7 minors deferred to M2 (ROADMAP).
**Next:** M2 plan (Shell + App: layered fence windows, takeover, watchdog, re-verification block).
**Open questions:** none.

## 2026-10-02 — M2a fence host

**Done:** M2 split (M2a/M2b/M2c). Core: px↔DIP placement, unclamped layout storage, session-end
policy, restart throttle, readable JSON (87 tests). New NeoFences.Shell (monitors by device path,
Progman ownership, accent blur, rounded region, desktop icons, detached watchdog) and NeoFences.App
(FenceHost, layered FenceWindow, SystemMessageWindow, single instance, Serilog, `--exit`).
Verified on the real desktop (research/m2a-fence-host.md): blur of the live wallpaper, rounded
corners, Win+D/corner/Win+M, forced + graceful Explorer restart, UI freeze, kills with and without
the icon preview, restart persistence. Fixed: freshly shown fence sat above apps (SendToBack).
The user went remote mid-way; checks were finished with automated input where possible.
**Decisions:** ADR-012.
**Next:** final review + merge; M2b plan.
**Open questions (user):** C8 real sign-out with the preview on; cancelled shutdown; B8 elevated;
A4 smoothness; tint preference. C8 gates Takeover in M2b.
Later: final review found WPF exits the app on WM_QUERYENDSESSION and COM calls fail inside it; redone
per ADR-013 (WPF SessionEnding restores icons via an MTA hop, session-ending marker, watchdog restarts after a
cancel, helper modes without WPF) and verified by scripted G9. Also fixed: watchdog restores whenever
takeover-active exists, stale marker at startup, immediate Takeover save, broad catches, background-thread crash
handler, no maximize box. G4 run (work area). User: current tint fine, tint preference later (FEATURES v2).

## 2026-10-02 — M2b desktop items

**Done:** M2b plan written from a compile-verified prototype (5 tasks, 92 tests) and approved (inline). Core:
`DesktopChange` + `FenceMembership.Apply`, `Settings.TakeoverPromptAnswered`. Shell: `DesktopItems` (user + Public
Desktop, enabled special icons), `DesktopWatcher`, `ShellItems` (names, icons/thumbnails, open). App: `IconLoader`
(background STA), item grid in `FenceWindow`, one-time Inbox banner, menu item renamed "Hide desktop icons".
Verified H1–H9, H11 on the real desktop with scripted input (research/m2b-desktop-items.md): 29 items reconciled,
500-file burst tracked without lost events, opened items come to the front, banner Not now / Hide them persist.
**Decisions:** ADR-014 (listing, watcher, shell names/icons, ask-once first run, keyboard moved to M2c).
**Next:** final review + merge; M2c plan.
**Open questions (user):** still C8 real sign-out, cancelled shutdown, B8, A4; optional H10.
Later: final review (opus) found 0 critical / 6 important / 9 minor. Fixed I1 (unreadable Desktop folder no longer
drops memberships; ghosts no longer kept — ADR-014 amended, 93 tests), I2 (unhide), I3 (watcher re-armed), I5 (icon thread
crash loop), I6 (open off the UI thread). I4 safe-save deferred to before M3; minors in ROADMAP. Merged to main.

## 2026-10-02 — M2c fence interactions

**Done:** user choices: follow Windows light/dark, soft label shadow. Prototype on the real desktop found 4 bugs before the
plan (routed DpiChanged reload loop at 1.4 GB, snapping pinned slow drags, lock not undoable, accent tint ignored). Plan
(5 tasks, 113 tests) approved, executed inline: Core FenceEdits + Snapping (Snap, Unsnapped); Shell RECT helpers +
SystemTheme; App fence menu (rename, icon size, lock, delete → Inbox), snapping, Enter, thin scrollbar, label shadow,
light/dark live. Smokes (with consent, mouse automation) pass I1–I9, I11.
**Decisions:** ADR-015 (corrects ADR-014 on activation).
**Found:** keyboard focus only when the desktop had focus before the click; Activate()/MA_ACTIVATE did not help.
**Next:** final review + merge; M3 plan (shell actions: context menu, rename/delete files, drag-drop).
**Open questions (user):** C8 sign-out, cancelled shutdown, B8, A4; I10, I12.
Later: final review (opus) 0 critical / 1 important / 6 minor. Fixed I1 (outline-drag runaway; DragTracker, 115 tests) and
M1 (rename box without focus). Minors in ROADMAP. Merged to main. User: split M3 into M3a (item menu, rename, delete,
open feedback, safe-save) and M3b (drag-drop, rubber band).

## 2026-10-02 — M3a item actions

**Done:** user split M3 into M3a/M3b and chose that Shift+Del also recycles. Prototype on the real desktop, plan (5 tasks,
122 tests) approved, executed inline: Core safe-save memory (deletes and renames-away remembered 5 s); Shell
ShellItemMenu (Windows' classic menu, IContextMenu2/3 forwarding, Rename/Delete taken over), ShellFileOps
(IFileOperation recycle/rename), Windows' open error UI; App in-place rename box, Del/F2/Enter, item right-click, UIA
names. Smoke (with consent, terminal refocused after) passes F2, Del, menu, broken shortcut, Word-style save.
**Decisions:** ADR-016.
**Next:** final review + merge; M3b plan (drag-drop between fences, out to apps, in from Explorer, rubber band).
**Open questions (user):** J2/J4/J6/J9/J10 + real Word save; C8, cancelled shutdown, A4, I10, I12.
Later: user reported the Inbox drawing over Firefox — fences now pinned to the bottom on WM_WINDOWPOSCHANGING (verified).
Final review (opus) 1 critical / 3 important / 11 minor: fixed C1 (extension HRESULT exceptions crashed the app; verified
live on 7-Zip and Send to), I1 (Excel/atomic saves), I2 (path-like rename names), I3 (rename vs rebuild). 137 tests.
Merged to main. User went remote again (2026-10-03): continue M3b, steer via remote control.

## 2026-10-03 — M3b drag-drop (user remote)

**Done:** prototype on the real desktop (with consent): found WPF's own drop-target registration (DRAGDROP_E_ALREADYREGISTERED)
and text files claiming to be drop targets; both fixed. Plan (5 tasks, 147 tests) approved, executed inline: Core
MoveItems (drop index = list as shown during the drag) + ExpectArrivals (RememberedPlacement); Shell DesktopNamespace,
ShellDragDrop (SHDoDragDrop with the shell IDataObject; IDropTarget per fence forwarding to the shell); App drag start,
hit test, rubber band. Smoke passes: reorder, between fences, out to Explorer, onto Recycle Bin, rubber band.
**Decisions:** ADR-017.
**Found:** Explorer and helper-process OLE drags cannot be driven by synthetic input → drops from Explorer are a user check (K4/K5).
**Next:** final review + merge; M4 plan (Portals).
Later: final review (opus) 1 critical / 4 important / 11 minor — fixed C1 (Shift+drop on the Recycle Bin item could
delete permanently; now always recycled), I1 (3-minute arrival window), I2 (drop-into only at an item's centre, with caret
and highlight), I3 (rename text drag), I4 (docs). 154 tests. Merged. User chose for M4: browse subfolders inside the
Portal; new Portals sort newest first.

## 2026-10-03 — M4 Portals (user remote)

**Done:** user choices: browse subfolders inside the Portal (Ctrl opens Explorer), new Portals newest first. Prototype on
the real desktop found a renamed Portal folder going unnoticed (FileSystemWatcher follows its directory) → parent watch +
re-arm. Plan (5 tasks, 163 tests) approved, executed inline: Core ItemSorting / CreatePortal / SetSort / SetItemOrder;
Shell FolderPicker, FolderItems + FolderWatcher, any-folder DesktopNamespace, Portal drops; App PortalState, Back button,
breadcrumb, Sort by, New Portal fence…, Open folder, "not available" message. M4 smoke + M3a/M3b regressions pass.
**Decisions:** ADR-018.
**Next:** final review + merge; M5 plan (desktop gestures: draw fence, quick-hide, peek, roll-up).
Later (after a power outage, 2026-10-03): the outage showed Explorer's HideIcons=1 persists across a hard power loss
while NeoFences does not auto-start → empty desktop. User chose: start with Windows (next, before M5). Final M4 review
(0 critical / 5 important): fixed I1–I5 (I5 per user: refuse deletes on drives without a Recycle Bin) and M1/M8/M9.
Portal smoke re-run all True. Merged.

## 2026-10-03 — Start with Windows (after a power outage)

**Done:** the outage left HideIcons=1 with no NeoFences running (empty desktop). User chose to start with Windows:
Core StartupPolicy (tested), Shell StartupRegistration (HKCU Run, never for temp-folder builds), fence-menu toggle.
Verified the Run value on/off/on through the setting. **Decisions:** ADR-019.
**Next:** M5 plan (desktop gestures).
**Open (user):** M3 — real power cut / hard reset with Takeover on; C8 clean sign-out.

## 2026-10-03 — M5 desktop gestures (user remote)

**Done:** user choices: quick-hide hides fences + desktop icons; roll-up opens on hover. Prototype smoke on the real
desktop found Peek raising only one fence (Progman-owned siblings restacked while still keeping themselves at the
bottom) → mark all, then raise. Plan (5 tasks, 188 tests) approved, executed inline: Core DesktopGestureTracker,
Hotkey, FreeSpot, SetRolledUp; Shell DesktopMouseHook (S2), DesktopWindows (incl. UIA icon hit test), GlobalHotkey,
topmost/overlay chrome; App DrawFenceOverlay, WM_HOTKEY, roll-up + hover, quick-hide, Peek. M5 smoke, extra checks
(N3, N10–N12) and the M4 regression pass. Scripted Esc reaching Windows Terminal cancelled the agent's own tool call
twice → scripts now click the desktop first and never type into a terminal.
**Decisions:** ADR-020.
Final review (opus): 0 critical; fixed I1 (no WM_GETTEXT from the hook; fence menus keep Peek), I2 (lost right-up ends
the drawing on the next click — a GetAsyncKeyState net failed live: swallowed presses never reach key state), M1, M2;
190 tests; smoke re-run passed (roll-up confirmed by a direct probe). Merged.
**Next:** M6 plan (polish + game mode: drop the hook in games, animations, tray, settings).
**Open (user):** N5, N6, N8, N13, N14.

## 2026-10-03 — M6a game mode, tray, Pause

**Done:** user choices: split M6 into M6a/M6b; game mode = go idle; tray left-click opens the menu. Prototype found the
tray's LoadIconMetric (comctl32 v6) crashing the app at start → LoadImage + caught tray setup; a borderless full-screen
window flips QUNS 2–3.5 s late → re-checks 1/2.5/5 s. Plan (5 tasks, 210 tests) approved, executed inline: Core
GameModePolicy, RunState; Shell GameDetection, ForegroundWatcher, TrayIcon/TrayMenu, SessionNotifications, hook
Highest priority + replay check; App tray menu, Pause, game mode (hook off, Peek ignored, Desktop/Portal changes
deferred), hook re-install on Explorer restart/unlock, icon retry on exit, new app icon. M6a smoke + M5 regression pass
(one pre-existing roll-up finding after a window closes, carried over).
**Decisions:** ADR-021.
Final review (opus): 0 critical; fixed I1 (late full screen: 5 s poll + check before Peek, supersedes "no polling"), I2
(tray click format + retry), I3 (tray New fence during quick-hide), M1 and M2 (re-graded); live fix check passed. Merged.
**Next:** M6b plan (settings window + animations).
**Open (user):** O1, O2 (real icon), real game O5/O6, O7–O10.

## 2026-10-03 — M6b settings window and animations

**Done:** user choices: roll-up opens on hover (default) or click; no Appearance section in v1 (tint/opacity with v2
themes). Plan (4 tasks, 223 tests) approved, executed inline: Core RollUpExpansion, Hotkey digit keys,
RunState.PeekHotkeyWanted; App Fluent SettingsWindow (General · Fences · Game mode · About and logs, hotkey recorder),
Settings… in tray and fence menu, click-to-open roll-up, 200 ms roll-up animation, 150 ms quick-hide fade (off with
Windows animations or in game mode), Peek hotkey released while paused/gaming. Prototype found UI Automation Toggle
not raising Click → Checked/Unchecked. M6b smoke + M6a and M5 regressions pass (M5 roll-up-after-Explorer carry-over).
**Decisions:** ADR-022.
Final review (opus): 0 critical; fixed I1 (safe hotkey recording, Tab/Alt+F4 pass through), M2 (drag during the
animation keeps the full height — the move loop re-proposes its captured rect), VK 0; live fix check passed. Merged.
User M7 choices: installer only (no update source yet), uninstall keeps data.
**Next:** M7 plan (Velopack package, uninstall restores icons → v1.0).
**Open (user):** P2, P3, P5, P7, P9–P12.

## 2026-10-03 — M7 installer → v1.0

**Done:** user choices: installer only for now (no update source), uninstall keeps data. Prototype found Velopack
deleting %LOCALAPPDATA%\<packId> on uninstall → packId NeoFences.App (data folder untouched), and Velopack closing the
app before the uninstall hook (the hook's icon restore is what keeps hard rule 2). Plan (5 tasks, 248 tests) approved,
executed inline: Core StartupPolicy (installed copy owns sign-in, dev builds never take it over); Shell
StartupRegistration.RemoveIfUnder; App InstallHooks (Velopack first in Main), Velopack 1.2.161 (ADR-023), version 1.0.0;
build\pack.ps1 + vpk tool manifest. Install test on the PC passed twice (install, dev-build rule, uninstall: icons back,
entry gone, data kept); M6a regression passes.
**Decisions:** ADR-023.
Final review (opus): 0 critical; fixed I1 (no update hook — Setup closes NeoFences itself; docs corrected), I2 (uninstall
icon restore retries + HideIcons=0 fallback, logging cannot block it), I3 (start-with-Windows points at the installed copy
from any build); install test re-run passed. Merged.
**Next:** ask the user about installing v1.0 for real and a local v1.0.0 tag; then the v2 backlog.
**Open (user):** Q2, Q3, Q6, Q7, Q9.
**Release:** tagged `v1.0.0`; rebuilt the installer from main and installed it on this PC (silent). The installed copy runs the user's fences and owns start-with-Windows. Test scripts must now restore the installed exe afterwards (`-RestoreExe %LOCALAPPDATA%NeoFences.AppcurrentNeoFences.exe`).

## 2026-10-03 — M8a v1.1 reliability (+ rounded corners)

**Done:** user choices: carry-overs in 4 batches (reliability first), icon-only labels per fence + Settings default with
the name popping under the icon, ship v1.0.1 after M8a. User report: fence corners stuck out of the rounded border →
8× zoom investigation: the region was rounded but the accent blur ignores it; Windows 11 DWM corner preference fixes it
(experiment 2, BlurBehind + region, did not). Plan (5 tasks, 259 tests) approved, executed inline: Core reconcile
recovery (safe-save memory, moved Desktop, ambiguous names never guessed), unique monitor ids, fences from another setup,
backup failures apart; Shell DWM corners, watcher edge cases, unique ids, constant; App watcher-first, monotonic memory
clock, RunState quick-hide that keeps user-hidden icons, instant Pause, tray on session end, game queue cap, unlock log,
uninstall hook tweaks; pack.ps1 robustness. Corners checked live; M5 and M6a regressions pass.
**Decisions:** ADR-024.
Final review (opus): 0 critical; fixed I1 (user-hidden icons through RunState, 3 tests) and the watcher check (re-graded:
GetAttributes); live watcher check passed. Merged.
**Next:** v1.0.1 build + install; M8b plan.
**Release:** v1.0.1 (tag) packed and installed over 1.0.0 with Setup --silent: Velopack closed NeoFences, upgraded and started 1.0.1; fences, Takeover and start-with-Windows kept (Q7).

## 2026-10-03 — M8b v1.1 fence UI + icon-only labels

**Done:** user choice: shortcut arrows as a Settings switch (off by default). Prototype first, then a live smoke on the
user's desktop (installed copy restored): labels default + apply to all, the pop-under name for hover and selection,
fence menu Labels, arrows, hotkey key caps and recording, first title double-click after Explorer, Peek/quick-hide — all
pass, 0 log warnings. An Activate()-on-press experiment for keyboard focus did not make the fence foreground (removed;
ADR-015 stands). Plan (5 tasks, 276 tests) approved, executed inline. The plan's pack.ps1 block had been corrupted by a
JS `$'` replacement; caught by the Task 4 check and fixed.
**Decisions:** ADR-025.
Final review (opus): 0 critical; fixed I1 (pop-under name kept inside narrow fences, hidden when scrolled away), I2 (no
blink between icons: cells hit-testable edge to edge), I3 (name re-placed after list changes, text bound), I4 (only the newest
icon size applied with two loaders), I5 (Settings keeps the not-active hotkey warning), I6 (the v1.0 compatibility test now
really removes the fields), and re-graded M1 (a rename box closes only when its own item moves). Live smoke on the branch
build passed (incl. the name inside the fence and the gap sweep). Merged.
**Next:** the M8c plan (shell actions + drag-drop details).
**Open (user):** S9, S10, S16.
**Release:** user chose v1.1.0 now and "rename the right-clicked item only" for M8c. v1.1.0 (tag) packed (0.25 MB delta) and installed over 1.0.1 with Setup --silent: config and start-with-Windows kept; Setup closed 1.0.1 but did not start 1.1.0 (started by hand; ROADMAP item for M8c).

## 2026-10-03 — M8c v1.1 shell actions + drag-drop details

**Done:** user chose "rename the right-clicked item" for menu → Rename with several selected. Prototype, then live smokes
on the user's desktop (installed 1.1.0 restored each time): Rename on the right-clicked item, outside click closes the
menu, Del recycles on the new shell worker with the UI responsive, Desktop icon settings toggle fenced/unfenced the
Control Panel icon, Recycle Bin notice (after widening the registration to all events on the bin), M8b regressions,
second-instance wait. Script lessons: PowerToys' "Rename with PowerRename" matched "Rena*" (4 PowerRename windows opened
and closed again; nothing renamed), and an "outside" click landed inside a tall menu. The same-version Setup only offers
Repair, so the Setup restart is confirmed at the next upgrade. Plan (4 tasks, 281 tests) approved, executed inline.
**Decisions:** ADR-026.
Final review (opus): 0 critical; fixed I1 (registry notice could crash on a deleted key / at shutdown), I2 (dropped files
and their possible shortcuts were interleaved: multi-file drops landed on every other slot), I3 (watcher backoff never
reached a minute — now Core WatcherBackoff, tested), I4 (one stuck open held up every other shell action: opens get their
own STA thread), I5 (virtual file list from another program validated before reading), I6 (shell notices re-registered
after an Explorer restart). Branch smoke passed (M8c + M8b regressions). Merged.
**Next:** the M8d plan (Portal details); the user allowed the LOCO_DUCK pendrive (G:) for USB tests.
**Open (user):** T7, T8, T9, T10, T12, T13.

## 2026-10-03 — M8d v1.1 Portal details

**Done:** the user left the PC unattended (monitored remotely) and lent the LOCO_DUCK USB stick (G:) for tests
(memory: reference-test-pendrive). Prototype, then live checks: a Portal of G:\NeoFences-test kept its selection when a
file arrived; Enter on two folders opened both; Safely Remove (CM_Request_Device_Eject on the stick's USB storage
device) was vetoed with v1.1.0 (volume in use) and succeeded with M8d (log "being removed: released it", Portal "not
available", NeoFences running). Script lessons: InvokeVerb('Eject') did nothing; the disk node itself cannot be ejected.
The stick stayed ejected (the user replugs it). A power cut (19:35-21:04) hit after the plan commit: nothing lost,
NeoFences 1.1.0 came back by itself. Plan (3 tasks, 283 tests) approved ("yes"), executed inline.
**Decisions:** ADR-027.
Final review (opus): 0 critical; fixed I1 (a Portal watcher stopping during a game left the Portal stale after it), I2
(a watcher failing while it arms was never noticed), re-graded M4 (a refused drive notice logged on every refresh: once
now); U3/U7/ADR-027 wording corrected. Branch smoke passed, incl. a second real eject of the stick. Merged.
**Next:** v1.1.1 release (user: "merge after review and release v1.1.1").
**Open (user):** U2, U7.
**Release:** v1.1.1 (tag) packed (0.37 MB delta) and installed over 1.1.0 with Setup --silent: config and
start-with-Windows kept. T13 failed: NeoFences was not started again (no start attempt in the log); started by hand. A
silent repair with --verbose --log showed Setup running the --veloapp-install hook, then a force-stop check of the install
folder, then "Installation completed" — no app launch. ROADMAP item with a fix idea; the single-instance wait (M8c) is
harmless but does not address it.

## 2026-10-03 — v1.1.2 start after Setup

**Done:** user chose "fix now as v1.1.2". The install hook now shows the icons and starts NeoFences ~4 s after Setup
through cmd.exe (Setup force-stops the install folder right after the hook). Prototype packed as 1.1.2-beta.1 and
installed over 1.1.1 with --silent: icons shown, NeoFences started by itself 4 s later; config kept. (PowerShell's
Start-Process -Wait now waits for the started NeoFences too — wait for the Setup process alone.) Plan approved, inline.
**Decisions:** ADR-028.
**Next:** review, merge, release v1.1.2 (beta → final re-tests the fix).
Final review (opus): 0 critical; fixed I1 (the restarted NeoFences kept the install folder as working directory, which
would block the next Setup's rename: system folder for the launcher and for NeoFences itself), re-graded M2 (cmd/ping by
full path, exe path through the environment: a failing wait would have re-created the original bug) and M1 (only icons
NeoFences hid are re-shown). Launcher probed with a "%OS% & ( )" path. Merged.
**Release:** v1.1.2 (tag) packed (0.17 MB delta) and installed over 1.1.2-beta.1 with --silent: NeoFences started by itself
3.9 s after Setup ended (watchdog too); config kept. Q11 (silent) passes; the double-clicked Setup stays a user check.

## 2026-10-03 — M9 Fence tabs (v1.2)

**Done:** brainstorm (architectural): user chose combine-by-drag (Fences 6), drag-out to split, per-tab accent colours,
approach "tab groups over ordinary fences"; spec approved (specs/2026-10-03-fence-tabs-design.md). Prototype: Core
FenceTabs test-first (13 tests), one window per box, tab strip, header gestures with a ghost, merge-on-title-drop with a
highlight, drop onto a header. Live smoke on the user's desktop passed every step first time (merge, switch, colour,
restart, roll-up, item drop on a header, drag out, quick-hide; 0 warnings). Plan (4 tasks, 296 tests) approved, inline.
**Decisions:** ADR-029.
Final review (opus): 1 critical — dragging the host tab's header into another box moved the whole box (fixed: Merge
wholeBox flag, test); important: a detached Portal tab opened empty, "tabs": null crashed the start, Esc/right-click did
not cancel a header drag in the common case — fixed (Esc via key state; right-click over the bare desktop documented);
re-graded: joining fences' old roll-up/lock, no drop-target highlight, release over own body detaching — fixed. Branch
smoke passed incl. Esc cancel and the host header detach (box kept its place). Merged.
**Release:** v1.2.0 (tag) packed (0.29 MB delta) and installed over 1.1.2 with --silent: NeoFences restarted by itself 3.9 s after Setup (watchdog too); the config differs only by the new empty `tabs` fields.

## 2026-10-03 — M10 Snapshots (v1.3)

**Done:** brainstorm (architectural): undo a messy desktop; items newer than a snapshot stay in a surviving fence, else
the Inbox; tray + Settings; "Before restore" first; one file per snapshot. Spec approved. Prototype: Core Snapshots /
SnapshotStore test-first (7 tests), tray submenus and notices, a Settings card. Live smoke passed first time (take,
restore, undo the restore, tray take, delete to the Recycle Bin; 0 warnings). Plan (4 tasks, 306 tests) approved, inline.
**Decisions:** ADR-030.
**Next:** final review, branch smoke, merge, v1.3.0 release.
**Review:** final opus review: no Critical; 4 Important fixed (windows kept their old title/icon size after a restore; damaged snapshot files not logged; an unlistable snapshots folder could crash the tray; a renamed "Before restore" was overwritten by the next restore), 2 new Core tests (308). Branch smoke passed incl. W8 and the new W12, plus the M9 tabs regression. Merged. Minors deferred to ROADMAP.
**Release:** v1.3.0 (tag) packed (0.3 MB delta) and installed over 1.2.0 with --silent: NeoFences restarted by itself 4.1 s after Setup; the config is unchanged.

## 2026-10-03 — M11 Rules auto-sort (v1.4)

**Done:** brainstorm (architectural): file new items + Apply rules now; type, game, name, date, size; Settings → Rules
list; fence menu shortcut. Spec approved. Prototype: Core Rules test-first, Shell ItemFactsReader, App filing, Settings
card and editor. A facts probe on the user's Desktop found the Epic launcher counted as a game (fixed) and 6 of 9 game
shortcuts in D:\GameLibrary → the user added a Game rule "In a folder of mine…". Live smoke passed (startup and new
items filed, editor rule, unticked rule, Apply rules now 5 moved + undo, fence menu → editor; 0 warnings). Plan (4 tasks,
340 tests) approved, inline.
**Decisions:** ADR-031.
**Next:** final review, branch smoke (with X4), merge, v1.4.0 release.
**Review:** final opus review: 1 Critical (an Age rule with a huge day count overflowed and crashed) and 3 Important (attribute changes re-filed Inbox items; Chrome/Edge downloads finishing by rename were never filed; a hand-edited rule with an unknown name made config.json corrupt) — fixed test-first (347 tests). The branch smoke then caught a just-created shortcut being read before it was written: filing now waits 1.5 s after the last arrival. Branch smoke passed (X4 game folder, X15 download, X16 read-only, X8 fence menu; 0 warnings). Merged. Minors deferred to ROADMAP.
**Release:** v1.4.0 (tag) packed (0.35 MB delta) and installed over 1.3.0 with --silent: NeoFences restarted by itself 4.1 s after Setup; the config differs only by the new empty `rules` list.

## 2026-10-03/04 — M12 Game Library (v1.5)

**Done:** brainstorm (architectural): every game in one place; launchers, Xbox, my game folders, Desktop game
shortcuts; posters from disk; one A–Z fence; generated shortcuts shown like a Portal. Spec approved. Prototype: Core
catalog test-first, Shell scanners (12 games in 0.2 s on the user's PC, read-only), library fence with tiles. Live smokes
(with consent) passed: fence with Steam posters, live rescans, a missing folder keeps its games, Hide / Show again.
Found: hiding must cover every source of a merged game (fixed, test); a test script's right-click hit the bare desktop
(nothing changed; scripts now aim inside the fence and never press Delete blind). Plan (4 tasks, 361 tests) approved, inline.
**Decisions:** ADR-032.
**Next:** final review, branch smoke (with the user's go), merge, v1.5.0 release.
**Review:** final opus review: no Critical; 3 Important fixed (a failed Steam scan dropped every Steam game; ghost games from Epic manifests / GOG entries of deleted folders and queued Steam downloads; library watchers blocked "Safely remove" and were built on the UI thread) plus a re-graded minor (a damaged or hand-edited index.json could crash every start or point a delete outside the library) — 3 new Core tests (364). A read-only re-scan still finds the user's 12 games. Branch smoke passed (create, live rescans, a missing folder keeps its games, Hide / Show again with every id hidden; 0 warnings). Merged. Minors deferred to ROADMAP.
**Release:** v1.5.0 (tag) packed and installed over 1.4.0 with --silent: NeoFences restarted by itself 4.1 s after Setup; the config differs only by the new default `library` settings.

## 2026-10-04 — Hub correction; M13a v1.6 carry-overs: data safety

**Done:** the hub was rebuilt from docs/ (tasks regrouped newest first with an "Open" group, milestones and ADRs in
order, versions, dependencies, APIs, risks, research, sessions, flows, config example, commit list from git). User chose
v1.6 carry-overs in 3 batches, each released. M13a prototype: Core test-first (9 tests, 373): schema 2, newer/oversized
snapshots refused, no temp file after a failed save, newcomers in the Desktop's order, duplicate rule ids, library
index settled after failures; per-entry Epic/Xbox failures, HeldFolder removal notices and a release grace for Portals,
no library work after exit, no disk checks on the UI thread, a manual-reset --exit signal. Live check (no input,
consent): schema 1 → 2, a waiting copy gives up on --exit, a later start runs. Plan (3 tasks) approved, inline.
**Decisions:** ADR-033.
**Next:** final review, branch smoke (user's go), merge, v1.6.0; then M13b.
**Review:** final opus review: no Critical or Important; one Minor re-graded Important as a regression of this branch (a start during an --exit's shutdown gave up; 1.5 started it) — fixed: a signal set before the start is the running copy's exit, so the new copy waits for it. Branch smoke passed (schema 1 → 2, a waiting copy gives up on --exit, a later start runs, exit-then-start-at-once starts; 0 warnings). Merged. Minors deferred to M13b.
**Release:** v1.6.0 (tag) packed and installed over 1.5.0 with --silent: NeoFences restarted by itself 4.1 s after Setup; the config differs only by `schemaVersion` 1 → 2.

## 2026-10-04 — M13b v1.6 carry-overs: Game Library and rules polish

**Done:** prototype with Core test-first (19 tests, 392): Epic games only, containment merge, launcher links in
arguments, plain source names, hidden games remembered with names, lost files dropped from the index, a one-time
pre-schema-2 config copy; Shell/App: remembered programs (33 ms rescans), Xbox resource names, shortcut copies,
directory-only game-folder watchers, covers at tile size, temp sweep and written-through index, in-flight eject release,
rules editor guards and focus, drag tab switching over a library tab. Read-only probes and a live check (the PC
unattended, standing go) passed. Plan (3 tasks) approved, inline.
**Decisions:** ADR-034.
**Next:** final review, branch smoke, merge, v1.6.1; then M13c.
**Review:** final opus review: no Critical; 4 Important fixed (a hidden game split into two rows while a source was away; launcher shortcuts such as GalaxyClient.exe merging into the wrong game; covers not re-decoded after a size or DPI change; a program picked mid-copy never corrected — "Refresh library" now searches again) plus 2 minors re-graded (read-only shortcut copies breaking Hide; shortcut files not flushed before the index, with frequent power cuts) — 2 new Core tests (394). Branch smoke passed (stray temp swept, shortcut copied as is, hidden game remembered with both ids, rules editor guards; 0 warnings). Merged. Minors deferred to M13c.
**Release:** v1.6.1 (tag) packed and installed over 1.6.0 with --silent: NeoFences restarted by itself 4.0 s after Setup; the config is unchanged.

## 2026-10-04 — M13c v1.6 carry-overs: fence and Settings UX

**Done:** prototype with Core test-first (8 tests, 402): one-line tray snapshot labels, no-op tab reorders and colour
picks, layout key caps, the schema-copy check once per run; Shell/App: title right-click → fence menu, header click opens
a rolled-up box, snapshot results in Settings and warning notices, "More in Settings…" opens the card, Settings focus
and no-op refreshes, a vanished rule's edit kept, Desktop shortcut changes rescan the library, in-flight eject removal
by value, one drag hit-test, bounded .url reads. Live check passed (the PC unattended, standing go); the harness now
checks the window under the pointer before clicking (MinimizeAll left Explorer and Firefox in front). Plan (3 tasks)
approved, inline.
**Decisions:** ADR-035 (amends ADR-026; rules on the declined items).
**Next:** final review, branch smoke, merge, v1.6.2.
**Review:** final opus review: no Critical; 2 Important fixed (a locked fence's title still opened no menu; snapshot rename focus jumped back after a click elsewhere or went stale) plus 2 minors re-graded and fixed (a red failure line staying after a successful retry, with rename/delete failures silent; a failed pre-schema-2 copy never retried — a regression from the once-per-run check) — 1 new Core test (403). The German-layout example in comments and Z26 was corrected (OemPlus is "+" there). Branch smoke passed twice (unlocked and all fences locked; 0 warnings). Merged. Minors deferred to the ROADMAP.
**Release:** v1.6.2 (tag) packed and installed over 1.6.1 with --silent: NeoFences restarted by itself 4.0 s after Setup; the config is unchanged. All v1.6 carry-overs are closed or ruled.

## 2026-10-04 — M14 Appearance (v1.7)

**Done:** brainstorm with browser mockups (user: all four pieces; all three colour styles as a preference; one slider,
tone follows Windows; global font + per-fence override; accent switch for uncoloured fences; 8 colours + custom; approach
1: the wallpaper's own image instead of screen sampling); spec approved. Prototype with Core test-first (16 tests, 419):
look resolver, accent colour (a saturation floor after the probe gave a grey slate for the Kara wallpaper → #385E94),
Wallpaper Engine files, schema 3. Shell/App: wallpaper sources, colour picker, styling, Settings → Appearance. A power cut
between prototype and check: the installed 1.6.2 recovered by itself. Live check with the user's go passed (Firefox PiP
untouched). Plan (3 tasks) approved, inline.
**Decisions:** ADR-036.
**Next:** final review, branch check (ask the user first), merge, v1.7.0.
**Review:** final opus review: no Critical; 4 Important fixed (font picks dropping earlier parts; the accent read stopping on a half-written image instead of falling back; a full Settings refresh per slider step; white titles on green/teal strips — the spec's 0.5 threshold replaced by dark ink above luminance 0.22, 1 new test row set, 422) plus 4 minors re-graded and fixed (drawn fences without the accent, custom colours without a tab-header mark, the colour dialog acting on a gone fence, Spotlight images served from the cache). Branch check passed with the user's go (PiP untouched, 0 warnings, config identical). Merged. Minors deferred to the ROADMAP.
**Release:** v1.7.0 (tag) packed and installed over 1.6.2 with --silent: NeoFences restarted by itself 3.2 s after Setup; the config was upgraded to schema 3 (fences unchanged, `backups\pre-schema-3-config.json` kept).

## 2026-10-04 — M15 Search palette: built, then parked

**Done:** spec, prototype, live check, plan, inline execution and final review (1 Critical: emoji names crashed the palette;
Important: keyboard left on a revealed fence after Peek, removed Portals still listed, exponential word-start search,
palette over games — all fixed, 446 tests) on branch `m15-search-palette`. PowerToys check: no shortcut conflicts on this
PC (Command Palette Win+Alt+Space, PowerToys Peek Ctrl+Space, Run Alt+Space vs NeoFences Ctrl+Alt+F / Ctrl+Alt+Space).
**Decision (user):** NeoFences does not need search yet — the branch is parked, not merged; main stays v1.7.0.
**Next:** the user's pick of more pressing work.

## 2026-10-04 — M16 v1.7.1 polish

**Done:** prototype with Core test-first (7 tests, 429): readable Accent-edge titles with any custom colour, tray labels
cut before an emoji, one title font for all fences (user, mid-batch: the fence menu's Title font was too deep and
per-fence fonts too much — removed; an old per-fence font is ignored and dropped on save). App/Shell: Custom… Cancel tick,
WE mapping skips empty monitors, a removed font's name kept in Settings, no restyle without change, roll-up animation
stopped before a height change, hotkey messages with layout key caps, layout switch refreshes Settings, reconcile reaches
the library, drag frame over a Library header refused, pooled .url buffer. Live checks with the user's go (a diagnostic
run showed the fence menu reopens after the colour dialog — the script's lookup had missed it). Plan (2 tasks) approved, inline.
**Decisions:** ADR-038 (amends ADR-036).
**Next:** final review, branch check (ask the user first), merge, v1.7.1.
**Review:** final opus review: ready to merge (no Critical/Important). Re-graded and fixed: the Pink swatch's dark-mode title changed (threshold 0.35 → 0.30, all 8 swatches pinned in both tones, 431 tests), layout switches within one language missed (WM_INPUTLANGCHANGE hook), ADR-036 "amended by ADR-038" markers, AA9/AA24 and a code comment. Branch check passed with the user's go (0 warnings, config identical). Merged. Minors deferred.
**Release:** v1.7.1 (tag) packed and installed over 1.7.0 with --silent: NeoFences restarted by itself 4.0 s after Setup; schema 3 kept; fences unchanged.

## 2026-10-04 — M17 Public releases and auto-update

**Done:** brainstorm (user: friends download from GitHub; one public repo, not open source; secrets stay secret incl. the
personal address and the hub link; quiet download + restart prompt; GitHub Actions; signing deferred). Spec approved.
Repository made public with the user's explicit go: mirror backup, history rewritten (noreply address, hub link removed
from every commit), scan of all 377 commits 0 hits, README, CLAUDE.local.md for the hub link, pushed. Prototype with Core
test-first (7 tests, 438); updater, Settings → Updates, tray item, pack.ps1 -OutputDir/-ReleaseNotes, CI and release
workflows. Local update rehearsal 1.8.0 → 1.8.1 passed with the user's go. Plan (3 tasks) approved, inline.
**Decisions:** ADR-039.
**Next:** final review; push main (asked) → CI; tag v1.8.0 (asked) → draft → install check → publish (asked).
**Review:** final opus review: no Critical; 1 Important fixed — Velopack applied a downloaded update by force whenever NeoFences.exe started (killing the running copy with the icons hidden): apply-on-start turned off, a pending update is offered again and installed at the next clean exit. Re-graded and fixed: no update while the icon restore is unfinished, switch-off and game mode cancel the download and skip the exit install, no notice in a game, stable tags only, docs. A second rehearsal with the user's go verified it (a second start while an update waits kills nothing; a normal exit installs 1.8.1; the next start runs it; 0 warnings). Merged. Minors deferred.
**Release:** v1.8.0 — the first public release, each step with the user's go: main pushed (CI green, 438 tests on GitHub's runner), tag v1.8.0 pushed (the release workflow built, tested, packed and uploaded a draft with generated notes), the draft's Setup installed here (restarted itself in 4 s, schema 4, 2 fences, the GitHub update check ran without warnings, no rehearsal package left in Velopack's folder), published. Friends download from github.com/CipherSnowden/neo-fences/releases; installed copies update themselves from now on.

## 2026-10-04 — The pivot (ADR-040)

**Done:** hand check K4 passed (cross-drive drop copies, by Windows' rule); K5 found a real bug (a Desktop file dropped from Explorer → Windows "same source and destination" dialog). The user rethought the product: fences hold virtual items, never file operations, native icons visible (optional hide), dynamic fences later; versions restart at 0.x; GitHub repo reset; installed build removed. Recorded in docs/PIVOT-2026-10-04.md and ADR-040; CLAUDE.md hard rules updated.
**Next:** reset (bundle, one commit 0.8.0, GitHub cleanup), uninstall, then M18 spec. See PIVOT §Next steps.

## 2026-10-04 — M18 Virtual items (0.9.0)

**Done:** brainstorm (user: storage approach A — item records — but config and items in separate files), spec + ADR-041,
plan (9,125 lines, compile-verified patches), native inline execution on branch `m18-virtual-items` (Core items, watch
plan, schema 5 + `items.json`; Membership/Rules/Portals/Inbox/Takeover removed; Shell drops as links, drag-out copies,
pickers, target probe; App items, Properties, Locate…, target watching, "Hide desktop icons while NeoFences runs").
Final Opus review "with fixes": C1 (editor saves re-pointed items at temp files) and I1–I7 fixed test-first, minors
deferred to M19. Live check with the user (TEST-CHECKLIST AD, 32/32 pass): five bugs found and fixed — desktop-icon
double-click quick-hid everything (UIA sees the web wallpaper; now Explorer's folder view answers), drag-out made nothing
(shell item array data object), drops now read as shell items, a rename flashed Missing, a stick pulled without Safely
remove left a deaf watcher (DBT_DEVICEREMOVECOMPLETE now releases it). A power cut mid-check: the test build came back at
sign-in with every item intact. Merged to main (fast-forward), worktree removed, version 0.9.0.
**Decisions:** ADR-041 (separate `items.json`; amended: saves never prune item lists). Rulings: plan patches applied from
the verified scratch files (byte-identical); Task 0 baseline was 438 tests, not 329; final review ran before the remaining
live checks so they covered the final build; the reviewer's "declined to judge" items stand as the spec decides
(drag-out "x - Copy", Run as administrator offered for any file, 7-Zip temp paths go Missing).
**Release:** v0.9.0, each step with the user's go: main pushed (CI green, 404 tests on GitHub's runner), tag v0.9.0
pushed (release workflow built, tested, packed, uploaded a draft; a plain first line added to the generated notes), the
draft's Setup installed here with --silent (started itself in 6 s, version 0.9.0, schema 5, one empty fence, Start with
Windows → the installed copy, icons visible, no warnings), published. CI notes Node 20 actions (checkout/setup-dotnet v4)
are deprecated: bump to v5 some time.
**After the release:** the hub's fixed sections rewritten for virtual items (flows, drag matrix, UI mock, data model,
features with a Parked status, risks) and two stale ARCHITECTURE lines fixed; CI actions bumped to checkout/setup-dotnet
v5 (Node 24, CI green); the M18 test files and the older ones on the stick deleted at the user's request
(`G:\NeoFences-test` kept, empty).
**Next:** use 0.9.0 for a few days; decide the name (ADR-009 trademark) and code signing before 1.0.0; then plan M19
(customization polish + the deferred M18 minors in `research/m18-virtual-items.md`).
**Open:** an unreachable share item shows no icon; one unreproduced blank website icon after many restarts (both in
`research/m18-virtual-items.md`).

## 2026-10-05 — M19 Store apps, bulk fix, Add from desktop, reliability (0.10.0)

**Done:** the user skipped the "use 0.9.0 first" step and chose 0.10.0 for themselves (not yet for friends), the name
stays NeoFences (ADR-043 supersedes ADR-009), no code signing. Brainstorm → spec (ADR-042) → a prototype with live
probes on the user's desktop (182 Start apps listed in 0.34 s; `shell:AppsFolder\<id>` parses with name and icon; a
drag from Start works; four findings fixed: an uninstalled app's empty label, screen-reader names in the app list,
games sorted as apps without game folders — the Game Library's scan now counts —, a false "drop failed" warning on
Start drags) → a plan of replay-verified patches → native execution (Core test-first, 450 tests) → final Opus review
"with fixes" (no Critical; fixed: dead-share probes waited 2 s per item, mapped drives not probed, any app parse failure
= uninstalled, unquoted app launch, Locate owner guard; 460 tests) → live check AE 29/30 with the installed 0.9.0 and
its data backed up and restored → merged to main, version 0.10.0.
**Decisions:** ADR-042 (apps as `shell:AppsFolder` items; bulk fix with an undo snapshot in the restore slot; Add from
desktop points at the desktop entries), ADR-043 (the name stays). Rulings: plan patches applied from the plan's own
blocks; three review minors re-graded Important (app answers at sign-in, quoted launch, a crash guard).
**Release:** v0.10.0, each step with the user's go: main pushed (CI green), tag v0.10.0 (the release workflow built a
draft with a 0.4 MB delta from 0.9.0), install check as a **real update**: the installed 0.9.0, pointed once at the
draft's files (`NEOFENCES_UPDATE_SOURCE`), downloaded 0.10.0 quietly, offered "Restart to update to v0.10.0" and came
back as 0.10.0 in 2 s with its data, schema 5 and startup entry unchanged (the restarted copy inherited the rehearsal
switch: restarted plainly so it checks GitHub again); a plain first line added to the notes; published.
**Next:** M20 (the deferred M19 minors, dynamic collections); 1.0.0 for friends when wanted (code signing then).
**Open:** the deferred minors in `research/m19-apps-relocate-desktop-fill.md`; AE25 (a mapped network drive) not run.

## 2026-10-05 — M20 small fixes (0.10.1)

**Done:** the user picked "small fixes only" for M20: the deferred M19 review minors as a bounded change (design agreed in
chat, no spec/plan). Fixed test-first (8 new Core tests, 468): the bulk fix writes its undo snapshot only when something
is fixed ("1 item"/"N items"); programs from the app list get readable names (`ItemKinds.AppName`) and Windows' menu
(built from `shell:AppsFolder`); Add from desktop reuses the Game Library's last scan and never counts a drive root or a
system folder as a game folder (`DesktopSorting.UsableGameFolders`); shortcuts to Windows places and `file:///` links sort
as folders and files (`ShellLinks.ShellTargetOf`); stale records dropped after a restore and when a check outlives its
item; failed background checks logged. The R1 microsecond race was left as is by the user's choice. Live check AF 4/4 run
while the PC was unattended (installed data backed up and restored). Merged to main, version 0.10.1.
**Decisions:** none new (bounded fixes within ADR-042).
**Next:** release 0.10.1 (approved as one sequence); then whatever the user picks (dynamic collections later).
**Released:** 0.10.1 on 2026-10-05 — CI green, draft with the delta package, install check (the installed 0.10.0 updated itself to
0.10.1 from a local feed, config and items unchanged, plain restart back on GitHub updates), published with a plain first line;
hub refreshed.

## 2026-10-05 — M21 folder views (0.11.0), built and merged

**Done:** the user picked dynamic collections, then folder views only (auto-collect rules later): a fence that shows one
folder live and read-only (spec 2026-10-05-folder-views-design, ADR-044). Choices: subfolders open in Explorer; per view
show kind, type patterns, newest N, kept sort; Downloads/Screenshots newest first with 30; 500 cap with "+ N more".
Prototype in a worktree, plan with replay-verified patches, native execution; Opus final review "with fixes" (drive-root
views, one log line per outage, Browse on a dead share; re-graded: a crash after a restore during settings, enum typos
losing the config, known-folder failures) — fixed, 502 tests. Live check AG 19 pass; the stacking of views made from one
fence found and fixed. Merged to main.
**Decisions:** ADR-044. The user: no automatic recovery checks for a stick (AG8/AG9 skipped) — "Folder not available"
and Refresh cover it. The user asked for screenshots during tests (saved as feedback).
**Next:** release 0.11.0 when the user says so; deferred minors in `research/m21-folder-views.md`.
**Released:** 0.11.0 on 2026-10-05 — CI green, draft with the delta package, install check (the installed 0.10.1 updated itself to
0.11.0 in 2 s, config and items unchanged, plain restart back on GitHub updates), published with a plain first line; hub
refreshed. **Next candidate (user idea):** one kind of fence — items, games and folder views as per-fence settings; keep
Fence.Kind in code for later.

## 2026-10-05 — M22 one kind of fence: games become items (0.12.0), built and merged

**Done:** the user's direction: no fence types to pick — a fence holds elements whose kind and settings decide the look;
later a folder panel, widgets (clock, calendar) and element sizes. First step: games as items (spec, ADR-045). Prototype
probed on a copy of the user's data, plan with replay-verified patches, native execution; Opus final review "with
fixes" (dimmed not-installed covers, read-only game targets, no new-game flood on a first scan, safe migration saves,
returning games not copied; re-graded: Enter in the not-installed question, Windows' Delete on a game) — fixed, 515
tests. Live check AH 18/18 scripted while the PC was unattended (data restored). Merged to main.
Also this session: the desktop populated for a showcase (Games library fence, Apps from Add from desktop, a Downloads
folder view), desktop icons hidden at the user's request.
**Decisions:** ADR-045. The user: merge finished milestones into main locally until 1.0 (saved as feedback); item sizes
1×1/1×2/2×1/2×2 later.
**Next:** release 0.12.0 when the user says so; then the folder panel element, widgets or element sizes.
