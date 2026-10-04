# Architecture

Living document: describes the system **as it is now / as currently decided**. Update it in the
same commit as any change that alters components, threads, data flow or storage.
Full v1 rationale: `superpowers/specs/2026-10-02-neofences-v1-design.md`. Why-decisions:
`DECISIONS.md`.

## Where NeoFences sits on the desktop

```
 TOP     ┌──────────────────────────────────────────────┐
         │ normal app windows (games, browser, …)       │
         ├──────────────────────────────────────────────┤
         │ NeoFences fence windows (top-level, layered, │  ← owned by Progman: stays just above
         │ accent blur; owner = Progman)                │    the desktop on Win+D; topmost in Peek
         ├──────────────────────────────────────────────┤
         │ desktop icons — SHELLDLL_DefView             │  ← hidden in Takeover (FWF_NOICONS)
         ├──────────────────────────────────────────────┤
         │ Wallpaper Engine / Lively (WorkerW)          │
 BOTTOM  │ static wallpaper (Progman)                   │
         └──────────────────────────────────────────────┘
 24H2+: SHELLDLL_DefView and the wallpaper WorkerW are both children of Progman.
 Pre-24H2: separate top-level WorkerW windows. NeoFences must not depend on either layout.
```

## Layers

```
┌─────────────────────────── NeoFences.App (WPF) ───────────────────────────┐
│  TrayIcon · SettingsWindow · FenceWindow (one per fence) · FenceViewModel │
│  DrawFenceOverlay                                                         │
└──────────────┬────────────────────────────────────────────────────────────┘
┌──────────────▼─────────── NeoFences.Shell (Win32/COM, CsWin32) ───────────┐
│ DesktopLayer · DesktopIcons · ShellItems · ShellActions · InputHook       │
│ GameDetector · Displays · Watchdog                                        │
└──────────────┬────────────────────────────────────────────────────────────┘
┌──────────────▼─────────── NeoFences.Core (pure C#) ───────────────────────┐
│ Model · LayoutEngine · ConfigStore · Membership                           │
└───────────────────────────────────────────────────────────────────────────┘
```

| Component | Responsibility | Key APIs |
|---|---|---|
| Core/Model | Fence, FenceSource (Desktop / Portal), ItemRef, Layout, Settings | — |
| Core/LayoutEngine | display fingerprint, map/scale layouts between monitor setups, clamp, snap, smart placement of new fences (`FreeSpot`, ADR-020) | — |
| Core/ConfigStore | load/save JSON, atomic write, backups, corrupt recovery, schema migration | System.Text.Json, File.Replace |
| Core/Membership | item → fence assignment, Inbox fallback, reconcile on startup | — |
| Core/FenceEdits, Core/Snapping | rename / icon size / lock; snap to 8 px gap or aligned edges during drags, tracking the unsnapped drag rect (ADR-015) | — |
| Core/FencePlacement, Core/Lifecycle | px↔DIP placement, containing monitor; session-end policy, restart throttle | — |
| App/InstallHooks, Shell/StartupRegistration, Core/StartupPolicy, build/pack.ps1 | Velopack installer (per user, self-contained, packId NeoFences.App, data stays in %LOCALAPPDATA%\NeoFences); uninstall hook: clean stop, icons always shown, own sign-in entry removed, data kept; sign-in entry owned by the installed copy, dev builds never take it over (ADR-019, ADR-023) | Velopack 1.2.161, vpk, HKCU Run |
| App/FenceHost | orchestrates config, monitors, windows, debounced saves, display changes, Explorer restarts, session end | — |
| App/FenceWindow, FenceItemView, IconLoader | fence UI: item grid (ListBox + WrapPanel, 48-DIP icons, extended selection, double-click open), Inbox first-run banner; names/icons loaded on one background STA thread (ADR-014) | WPF, frozen BitmapSource |
| App/SystemMessageWindow | hidden top-level window: TaskbarCreated, display/work-area changes, light/dark switch (`ImmersiveColorSet`) (session end is WPF's `SessionEnding`, ADR-013) | HwndSource |
| App/SettingsWindow, Core/RollUpExpansion | settings (Fluent, per window): General · Fences · Game mode · About and logs, Peek hotkey recorder; rolled-up fences open on hover or click; 200 ms roll-up and 150 ms quick-hide animations, off with Windows animations or in game mode (ADR-022) | WPF ThemeMode, SystemParameters.ClientAreaAnimation |
| Shell/DesktopLayer | keep fences at desktop level (owner = Progman, re-applied on TaskbarCreated), Peek, layered accent-blur backdrop | SetWindowLongPtr(GWLP_HWNDPARENT), SetWindowCompositionAttribute, TaskbarCreated |
| Shell/DesktopIcons | hide/show native icons | IShellWindows, IFolderView2::SetCurrentFolderFlags |
| Shell/Watchdog | `--watchdog <pid>` mode (no WPF, no window), started detached: `WatchdogPlan` from markers — restore icons whenever `takeover-active` exists; restart after crash (3 per 10 min) or after a cancelled session end (ADR-013) | Process.WaitForExit, SM_SHUTTINGDOWN |
| Shell/DesktopItems, DesktopWatcher | desktop listing as item refs (user + Public Desktop files, enabled special icons); live create/delete/rename → Core `DesktopChange`, overflow → full reconcile (ADR-014) | SpecialFolder, HideDesktopIcons registry key, FileSystemWatcher |
| Shell/ShellItems | display name, icon/thumbnail pixels, open (default verb) | SHCreateItemFromParsingName, IShellItemImageFactory, GetDIBits, ShellExecute, AllowSetForegroundWindow |
| Shell/ShellItemMenu, Shell/ShellFileOps | Windows' classic item menu (desktop IShellFolder + IContextMenu2/3 forwarding; Rename/Delete taken over); recycle and rename via IFileOperation, delete always recycles (ADR-016) | IContextMenu3, TrackPopupMenuEx, IFileOperation |
| Shell/DesktopNamespace, Shell/ShellDragDrop | items as children of their shell folder (desktop merged view, or a Portal's folder) (shared by menu, drag data, item drop targets); drag source with the shell's IDataObject; one IDropTarget per fence: fence/Desktop items → membership only (DROPEFFECT_NONE), containers → their own drop target, other files → the Desktop folder's drop target + expected arrivals (ADR-017) | SHDoDragDrop, RegisterDragDrop, IDropTargetHelper |
| Shell/FolderPicker, Shell/FolderItems (+FolderWatcher), App/PortalState, Core/ItemSorting | Portal fences: Windows' folder dialog, listing (visible entries) and live changes incl. the folder itself vanishing, browsing (runtime only), name/type/newest-first sorting (ADR-018) | IFileOpenDialog, FileSystemWatcher |
| Shell/DesktopMouseHook (+DesktopWindows), Core/Input/DesktopGestureTracker | desktop double-click (quick-hide), right-drag (draw a fence), Peek click-outside; S2 swallow + marked replay of a plain right-click; desktop / fence / native-icon hit tests (ADR-020) | WH_MOUSE_LL (own thread), WindowFromPoint, SendInput, UI Automation (icon hit test, UI thread) |
| Shell/GlobalHotkey, Core/Input/Hotkey, App/DrawFenceOverlay | Peek hotkey (default Ctrl+Alt+Space; Esc only while peeking); the click-through rectangle while drawing a fence (ADR-020) | RegisterHotKey |
| Shell/GameDetection (+ForegroundWatcher), Core/GameModePolicy | game mode: notification state BUSY / D3D full screen while an app (not desktop, taskbar, NeoFences) is in front; checked on foreground changes and 1 / 2.5 / 5 s later (ADR-021) | SHQueryUserNotificationState, SetWinEventHook (out-of-context) |
| Shell/TrayIcon (+TrayMenu, SessionNotifications), Core/RunState | tray icon and menu (New fence · Quick-hide · Peek · Pause · Exit); Pause; one rule for Takeover / quick-hide / Pause / game mode → fences, icons, hook, deferred shell work; hook re-install on unlock (ADR-021) | Shell_NotifyIcon, TrackPopupMenuEx, LoadImage, WTSRegisterSessionNotification |
| Shell/Displays | monitors, work areas, DPI | EnumDisplayMonitors, GetDpiForMonitor |

## Threads

| Thread | Runs |
|---|---|
| UI (STA) | WPF, all fence windows, shell COM objects, WinEvent hook callbacks |
| InputHook | `WH_MOUSE_LL` + its own message loop; posts gestures to UI thread. Must return fast (< 1 ms). |
| Icon loaders (2× STA) | `IconLoader`: display names + `IShellItemImageFactory`; frozen bitmaps marshalled to UI; only the newest size is applied |
| Shell worker (STA) | `ShellWorker`: open, recycle, rename in order (Windows' dialogs and slow handlers never freeze the fences, ADR-026) |
| Watcher callbacks (thread pool) | `DesktopWatcher` events and the "Desktop icon settings" registry notice, marshalled to UI with `Dispatcher.BeginInvoke`; Recycle Bin notices arrive as window messages |
| Watchdog process | separate process, same exe, waits on main PID |

## Data

`%LOCALAPPDATA%\NeoFences\`
- `config.json` (+ `.bak`, `.tmp` transient) — schema in spec §5
  - shape: `{ schemaVersion, settings, fences[], layouts: { <fingerprint>: { monitors: { <id>: { workWidth, workHeight } }, fences: { <fenceId>: { monitor, x, y, w, h } } } }, lastLayoutFingerprint }`
- `backups\config-<yyyyMMdd>.json` (keep 10)
- `logs\neofences-<date>.log` (keep 7)
- `clean-shutdown-<pid>` marker consumed by the watchdog
- `session-ending-<pid>` — written inside WPF's SessionEnding; the watchdog restarts NeoFences only if the session continues
- `takeover-active` — exists while NeoFences has hidden the icons; the watchdog restores only then
- `watchdog-restarts.txt` — restart timestamps for the 3-per-10-min limit
- `logs\watchdog-<date>.log` (keep 7)

## Status

M0 spike (ADR-011), M1 Core (71 → 87 tests), **M2a complete**: NeoFences.Shell + NeoFences.App host layered,
Progman-owned, blurred fences, verified on the real desktop (`research/m2a-fence-host.md`). **M2b complete**: desktop items in
fences — start → `Reconcile(DesktopItems.Enumerate())` → `FenceWindow.SetItems` → `IconLoader`; watcher event →
`FenceMembership.Apply` → `SetItems`; Inbox first-run banner (ADR-014, `research/m2b-desktop-items.md`). **M2c complete**: fence menu (rename, icon size, lock,
delete → Inbox), snapping in `WM_MOVING`/`WM_SIZING`, keyboard (when the fence is active), thin scrollbar, label shadow,
light/dark following Windows via `Shell/SystemTheme` with the light veil drawn by the fence (ADR-015,
`research/m2c-fence-interactions.md`). **M3a complete**: Windows' item menu, F2 rename, Del → Recycle Bin, Windows' open
error UI, safe-save memory in `FenceMembership.Apply` (ADR-016, `research/m3a-item-actions.md`). **M3b complete**: drag-drop within/between/out of fences, onto folders and Recycle Bin, in from Explorer (pending user check K4/K5), rubber band;
`FenceMembership.MoveItems` / `ExpectArrivals` (ADR-017, `research/m3b-drag-drop.md`). **M4 complete**: Portal fences — live folder view, newest first, browse inside with Back, Sort by for all fences, drops
to/from Portals as real file moves (ADR-018, `research/m4-portals.md`).
**M5 complete**: desktop gestures — quick-hide (fences + icons), draw a fence by right-drag, Peek, roll-up with
hover, smart placement (ADR-020, `research/m5-desktop-gestures.md`).
**M6a complete**: game mode idle (hook removed, Peek ignored, Desktop/Portal changes deferred), tray icon and menu,
Pause, mouse-hook hardening, app icon (ADR-021, `research/m6a-game-mode-tray.md`).
**M6 complete** (M6b): settings window, roll-up click mode, roll-up and quick-hide animations (ADR-022,
`research/m6b-settings-animations.md`).
**v1.0 complete** (M7): Velopack installer with an uninstall hook that always restores the icons (ADR-023,
`research/m7-installer.md`). Next: the v2 backlog (FEATURES) and the ROADMAP carry-overs.
**M8a complete** (v1.1 batch 1): reliability carry-overs, and fence corners rounded by Windows 11 (the accent blur
ignores window regions; ADR-024, `research/m8a-reliability.md`).
**M8b complete** (v1.1 batch 2): icon-only fences whose names pop up in an overlay inside the fence window (never a
Popup: fences stay at the bottom), optional shortcut arrows, a title double-click NeoFences recognises itself (the first
one after another app works), in-place item diff, key-cap hotkeys (ADR-025, `research/m8b-fence-ui.md`).
**M8c complete** (v1.1 batch 3): a shell worker for open/recycle/rename, item-menu details, live special icons, drag-drop
details, coalesced watcher recovery, a single-instance wait after Setup (ADR-026, `research/m8c-shell-dragdrop.md`).
**M17 complete** (v1.8.0, public releases): `Core.Updates.UpdatePolicy` decides when to check; `FenceHost.Updates` runs
Velopack's `UpdateManager` against the public GitHub releases (a local folder via `NEOFENCES_UPDATE_SOURCE` for
rehearsals), downloads quietly, offers "Restart to update" and applies after the clean exit; `.github/workflows` builds
every push to main and every pull request (CI) and turns version tags into draft releases (ADR-039).
**M16 complete** (v1.7.1, polish): one title font for all fences (`Fence.TitleFont` removed; the fence menu keeps
Colour only); Accent-edge titles pushed until readable; review carry-overs closed (ADR-038).
**M14 complete** (v1.7.0, appearance): `Core.Appearance.FenceLook` resolves each fence's veil, outline, title ink, strip,
bar and font from `Settings.Appearance`, the fence's colour and font and the wallpaper accent; `AccentColor` picks the
wallpaper's strongest vivid hue; `Shell.WallpaperSources` reads Wallpaper Engine's preview, Windows' wallpaper or accent
(no screen capture); `FenceHost.Appearance` restyles on changes and re-reads the accent on wallpaper changes; config
schema 3 (ADR-036).
**M13c complete** (v1.6.2, fence and Settings UX): a title right-click opens the fence menu and a header click opens a
rolled-up box; snapshot results show in the Settings card (failures also as warning notices); tray snapshot labels are
one safe line (`Snapshots.MenuLabel`); Settings keeps focus on refreshes; hotkey labels use the layout's character
(`KeyboardLayout`); Desktop shortcut changes rescan the library (ADR-035).
**M13b complete** (v1.6.1, library polish): Epic lists games only and Xbox names resolve through the package's
resources; a Desktop shortcut into a launcher's game folder merges with it and is copied into the library as is;
scans reuse remembered programs and game folders are watched for folders only; hidden games live in the library index
with their names (`LibraryState.Hidden`); the index is written through and NeoFences' temp names are swept; one
pre-schema-2 config copy is kept in `backups\` (ADR-034).
**M13a complete** (v1.6.0, data safety): config schema 2, stamped by the normalizer, so v1.5 and older never save over
tabs, rules or the library; snapshots from a newer version or over 16 MB are refused; `LibraryFiles.Settle` keeps the
library index true after failed writes; watchers report the folder they hold (`FolderWatcher.HeldFolder`) for removal
notices; "--exit" is a manual-reset signal that a copy waiting to start also obeys (ADR-033).
**M12 complete** (v1.5, game library): a `library` fence kind (one at most) shows NeoFences' own
`%LOCALAPPDATA%\NeoFences\library\` (one shortcut per game + `index.json`) through the Portal machinery as 2:3 tiles;
Core `Library` (Valve/Epic parsing, `GameCatalog` merge, `LibraryFiles` plan), Shell `GameScanners` / `ShellLinks` /
`LibraryWriter`, FenceHost scans on an STA thread at start, on launcher/game-folder changes, on Refresh (ADR-032).
**M11 complete** (v1.4, rules): `config.json` holds an ordered `rules` list; Core `Rules` matches item facts (type,
game launcher or folder, name, date, size) and files matched items into desktop fences (membership only); Shell
`ItemFactsReader` reads facts and shortcut targets on the shell worker; FenceHost files new Inbox items after watcher
"created" events, reconciles and game-mode replays, and runs "Apply rules now" behind a "Before restore" snapshot (ADR-031).
**M10 complete** (v1.3, snapshots): `%LOCALAPPDATA%\NeoFences\snapshots\` holds one file per snapshot (Core
`SnapshotStore`, temp-then-swap); Core `Snapshots.Restore` applies one to the config against the live desktop listing;
the host writes "Before restore" first and rebuilds with `SyncBoxes` (ADR-030, `research/m10-snapshots.md`).
**M9 complete** (v1.2, fence tabs): fences combine into boxes. One window per box: `FenceWindow.FenceId` is the shown
tab (items, icon size, labels, sort, rename, delete, drops), `BoxId` the host fence (placement, move, roll-up, lock);
`FenceHost.SyncBoxes` keeps windows in line with boxes; Core `FenceTabs` owns every tab edit (ADR-029,
`research/m9-fence-tabs.md`).
**M8d complete** (v1.1 batch 4): Portals let go of a drive Windows wants to remove (device notices on the message
window), Portal watcher backoff, Enter on several items (ADR-027, `research/m8d-portal-details.md`). The M8 carry-over
batches are done; next: the v1.1.1 release decision, then the v2 backlog (FEATURES).

Core contracts M2 relies on (and M9: `FenceTabs` edits return a new config, the same instance for most no-ops — Merge into its own box, SetActive, Leave and Detach outside a box; `Repair` leaves untouched fences as the same instances): no-op membership calls return the **same** config instance (records compare
lists by reference, so use `ReferenceEquals`, not `==`); `ConfigStore.Load` may return `IsReadOnly` (newer
schema or locked file) and `Save` then returns false; `Reconcile` keeps memberships under the folders
the shell could not list (or all of them for an empty listing) and sets `ReconcileReport.Suspicious` (log it; ADR-014); `LayoutEngine.Resolve`
throws `ArgumentException` for unusable monitor work areas (FenceHost skips that resolve).
