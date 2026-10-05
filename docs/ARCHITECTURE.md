# Architecture

Living document: describes the system **as it is now / as currently decided**. Update it in the
same commit as any change that alters components, threads, data flow or storage.
Current direction: `PIVOT-2026-10-04.md` (ADR-040: virtual items). Full v1 rationale (pre-pivot):
`superpowers/specs/2026-10-02-neofences-v1-design.md`. Why-decisions: `DECISIONS.md`.

## Where NeoFences sits on the desktop

```
 TOP     ┌──────────────────────────────────────────────┐
         │ normal app windows (games, browser, …)       │
         ├──────────────────────────────────────────────┤
         │ NeoFences fence windows (top-level, layered, │  ← owned by Progman: stays just above
         │ accent blur; owner = Progman)                │    the desktop on Win+D; topmost in Peek
         ├──────────────────────────────────────────────┤
         │ desktop icons — SHELLDLL_DefView             │  ← visible; hidden only by the optional
         │                                              │    "Hide desktop icons" (FWF_NOICONS)
         ├──────────────────────────────────────────────┤
         │ Wallpaper Engine / Lively (WorkerW)          │
 BOTTOM  │ static wallpaper (Progman)                   │
         └──────────────────────────────────────────────┘
 24H2+: SHELLDLL_DefView and the wallpaper WorkerW are both children of Progman.
 Pre-24H2: separate top-level WorkerW windows. NeoFences must not depend on either layout.
```

## Virtual items (ADR-040, ADR-041)

A fence holds **virtual items**: NeoFences' own records (`Core.Items.VirtualItem`: id, target, own name, own icon,
arguments, run as administrator, note) pointing at a file, folder, app, website or special item. They live in
`items.json`, one list per fence id; `config.json` holds the fences, settings and layouts. **No NeoFences action touches
a target**: a drop creates items (the drop answers as a link, never a move), delete removes the item, dragging out
offers a copy only, rename/icon/path change only the item. Windows' own item menu (where a real delete or rename can
happen) is behind Shift+right-click, under a line saying it acts on the real file.

```
 drop (Explorer, desktop, browser) ─► ShellDragDrop.FenceDropTarget ─► FenceHost.OnTargetsDropped ─► ItemEdits.Add
 drag between fences ──────────────► (CurrentDrag keys) ────────────► FenceHost.OnItemsDropped ──► ItemEdits.Move / Duplicate
 Properties / Add item… / Locate… ─► ItemPropertiesWindow, PathPicker, AppPickerWindow ► ItemEdits.Replace / Add
 Locate… done ─► Relocation.Find / Candidates ─► TargetProbe (new places exist?) ─► RelocateWindow ─► snapshot ─► ItemEdits.Relocate
 Add from desktop… ─► DesktopItems + ShellLinks + GameScanners ─► DesktopSorting.GroupOf ─► DesktopFillWindow ─► FenceEdits.CreateFence + ItemEdits.Add
 _items (ItemsDocument) ─► FenceHost.RefreshWindow ─► ShownItem[] ─► FenceWindow.SetItems ─► IconLoader (own name/icon win)
 FolderWatcher (≤ 64 parent folders, WatchPlan) ─► rename ─► ItemEdits.Retarget (all fences)
                                                └► change ─► TargetProbe.Check (off the UI thread) ─► TargetState ─► fence refresh (≤ 1 / 2 s)
 SaveNow ─► ConfigStore.Save(config) then ItemStore.Save(items)     (config first; lists never pruned: ADR-041)
```

## Layers

```
┌─────────────────────────── NeoFences.App (WPF) ───────────────────────────┐
│ FenceHost (+ .Items .Watching .Library .Appearance .Updates .DesktopFill  │
│            .FolderViews .GameItems .Grid .Widgets)                        │
│ FenceWindow · FenceItemView · IconLoader · ItemPropertiesWindow           │
│ AppPickerWindow · RelocateWindow · DesktopFillWindow · MissingItemWindow  │
│ FolderViewWindow · AddGamesWindow · SettingsWindow · FolderLister         │
│ FenceGridPanel · SizePicker                                               │
│ DrawFenceOverlay                                                          │
└──────────────┬────────────────────────────────────────────────────────────┘
┌──────────────▼─────────── NeoFences.Shell (Win32/COM, CsWin32) ───────────┐
│ DesktopHost · DesktopIcons · ShellItems · ShellItemMenu · ShellDragDrop   │
│ TargetProbe · PathPicker · IconPicker · FolderWatcher · DesktopMouseHook  │
│ AppList · KnownFolders · SystemStats                                      │
│ GameDetection · GameScanners · Monitors · TrayIcon · Watchdog             │
└──────────────┬────────────────────────────────────────────────────────────┘
┌──────────────▼─────────── NeoFences.Core (pure C#) ───────────────────────┐
│ Items (VirtualItem, ItemEdits, TargetChecks, WatchPlan, RefreshThrottle,  │
│        Relocation, DesktopSorting, StaleEntries, FolderViews)             │
│ Model · Config (ConfigStore, ItemStore, JsonStore) · Layouts · Library    │
└───────────────────────────────────────────────────────────────────────────┘
```

| Component | Responsibility | Key APIs |
|---|---|---|
| Core/Items | `VirtualItem` + `ItemIcon`; `ItemKinds` (path / website / special, typed targets cleaned); `ItemsDocument` (fence id → items); `ItemEdits` (add with same-fence de-dup, move, Ctrl-duplicate, remove, replace, retarget on rename, prune, reorder, repair); `TargetChecks` (OK / Missing / Unavailable from the disk's answers); `WatchPlan` (≤ 64 parent folders, busiest first); `RefreshThrottle` (≤ 1 refresh per fence per 2 s); apps `shell:AppsFolder\<id>` (`IsApp`, checked, never watched; M19) | — |
| Core/Items (M19) | `Relocation` (Find: the differing front of the old and new path; Candidates: the other missing / unavailable path items under it); `ItemEdits.Relocate`, `CheckedTargets`; `DesktopSorting.GroupOf` (Games / Apps / Folders and files / Web links, reusing `GameLaunchers`); `StaleEntries.Gone` | — |
| Core/Items (M21) | `FolderViews.Select` (show kind → patterns → newest N → sort → cap 500), `ParsePatterns`, `DefaultsFor` (Downloads / Screenshots newest 30), `Normalize`, `Status` (not available / empty / nothing matching / "+ N more"); `FenceEdits.CreateView`, `SetView` (title follows the folder while it is its name) | System.IO.Enumeration |
| Core/Model | Fence (`IsLibrary`, `View` = `FolderView` (M21), `Kind` = Items / Library / View, tabs, colours, roll-up, lock, labels, icon size), Layout, Settings (`HideDesktopIcons`), snapshots with items | — |
| Core/Config | `JsonStore<T>`: atomic write (`SafeFile`), `.bak`, daily backups (10), corrupt recovery, newer-schema read-only; `ConfigStore` (config.json, schema 5; older schemas start fresh and are kept as `backups\pre-schema-5-config.json`), `ItemStore` (items.json, schema 1), `SnapshotStore` | System.Text.Json, File.Replace |
| Core/LayoutEngine | display fingerprint, map/scale layouts between monitor setups, clamp, snap, smart placement of new fences (`FreeSpot`, ADR-020) | — |
| Core/FenceEdits, FenceTabs, Snapping | rename / icon size / lock / colours / new / delete fence; tabs (ADR-029); snap to 8 px gap or aligned edges during drags (ADR-015) | — |
| Core/FencePlacement, Core/Lifecycle | px↔DIP placement, containing monitor; `RunState` (hide icons, quick-hide, Pause, game mode), session-end policy, restart throttle, watcher backoff | — |
| Core/Library | Valve/Epic parsing, `GameCatalog` merge, `LibraryFiles` plan, `GameLaunchers.LauncherOf` (ADR-032); `GameItems` (M22): `Migrate` (a library fence → game items), `NewGames`, `AddNew`, `Retarget` (by game id), `ShowsCover` | — |
| Core/Items (M24) | `GridSpan` (1–4 × 1–4), `GridCell`, `FenceLayout` (Flow / Free), `FenceGrid.Arrange` (dense packing; Free: stored cells, collisions and out-of-width elements to free spots), `CellAt`, `NearestFree`, `PlaceDropped`, `SpanOf` (covers 1×2); `ItemEdits.SetSize`, `Place`; repairs | — |
| Core/Items (M27) | `CollectRules`: `CollectRule` (source, `CollectKinds`, patterns, watermark), `KindOf` (installers before apps), `Matches`, `Plan` (first matching rule in fence order, nothing held, ≤ 200 per rule), `Arrivals` (new entries, or created after the watermark), `Normalize`, `Summary`; `FenceEdits.SetCollect`; `ItemInfo.Created` | System.IO.Enumeration |
| Core/Items (M26) | `FolderPanels`: `FolderPanel` (look Details / List / Icons, show, patterns, newest N, sort + direction), `IsPanel`, `Create` (busy folders newest first), `Select` (the M21 rules plus Size and direction; `FolderViews.Select` maps to it), `HeaderSort`, `Columns`, `Fills`, `HeaderOf`, `SizeText`, `MigrateViews`; `PanelPlace` (Into / Back / Up / Home, never above the panel's folder); `ItemInfo.Size` | System.IO.Enumeration |
| Core/Items (M25) | `Widgets`: `WidgetKind` (Clock / Date / Stats), `neofences:widget/<kind>` targets (`ItemKind.Widget`, never checked or watched), default spans, `WidgetOptions`, clock text, date page, stat rows, `NextTick` | System.Globalization |
| Shell/SystemStats (M25) | CPU (`GetSystemTimes` deltas), RAM (`GlobalMemoryStatusEx`), C: used (`GetDiskFreeSpaceEx`), GPU (PDH `\GPU Engine(*engtype_3D)\Utilization Percentage`, summed); null per value on failure | PDH, kernel32 |
| App/FenceHost.Widgets (M25) | Add widget ▸, the widget menu (clock options), double-clicks (Clock app, Task Manager); one timer on whole seconds while any widget exists, idle while none can be seen; stats every 2 s on a worker | DispatcherTimer |
| App/FenceGridPanel, SizePicker, FenceHost.Grid (M24) | the fence list's items panel: whole cells from the icon size and label mode, columns from the width, elements placed where `FenceGrid` says (a failed pass falls back to 1×1 in order); Size ▸ 4×4 picker; Layout ▸ (Flow → Free stores the shown cells); Free drops on the cell under the pointer (offsets kept, taken → nearest free); Sort in Free packs and stores; new elements of Free fences store their spot | WPF Panel |
| App/FenceHost.GameItems, AddGamesWindow (M22) | games as items (ADR-045): migration at start / after a restore / after a scan (a snapshot first), new games into `Library.NewGamesFence`, Add games…, the game item menu (Show as cover / icon, Open install folder), "not installed" | WPF (Fluent) |
| App/FenceHost | orchestrates config + items, monitors, windows, debounced saves (both files, config first), display changes, Explorer restarts, session end, snapshots, tray | — |
| App/FenceHost.Items | open (arguments, run as administrator; a missing target asks Locate… / Remove), NeoFences' item menu, Windows' menu on Shift+right-click, Properties, Add item…, Locate…, drops, drag-out, one-time sort, item pictures copied into `icons\` | WPF ContextMenu, Clipboard |
| App/FenceHost.Watching | watched folders (`FolderWatcher` + removal notices), rename-follow, target checks (start, 5 min, fence shown, Refresh, drive arrival/removal), per-fence refresh throttle, game-mode deferral | DispatcherTimer |
| App/FenceWindow, FenceItemView, IconLoader | fence UI: item grid (ListBox + WrapPanel), keys (Enter, Del = remove, F2 / Alt+Enter = Properties), drop caret, Missing badge / Unavailable dimming, empty-fence hint, `WM_DEVICECHANGE` → drives changed; names/icons on two STA threads, an item's own name and icon win, websites show the default browser's icon | WPF, frozen BitmapSource |
| App/ItemPropertiesWindow, MissingItemWindow | Properties and Add item… (name, target + Browse, live Found / Missing / Drive not connected, arguments, run as admin, icon from a file / a picture / reset, note); the missing-target question | WPF (Fluent) |
| App/FenceHost.FolderPanels, FolderPanelView, FolderPanelModel, FolderViewWindow (M26) | folder panels (ADR-048): one `FolderLister` per panel on the folder it shows (hidden tabs keep listing), selection off the UI thread, browsing in memory, the panel menu (Look, Sort by, settings, Open folder, Size with Fill fence, Show as icon), Add folder panel…, New folder panel…, Show as folder panel, the entries' safe menu and Windows' menu, drag-out, removal notices, game-mode pause, the folder-view migration (a snapshot first); the control: Details (`GridView`, header sort), List, Icons, virtualized rows, icons as rows load; the fence leaves a panel's own clicks, keys and wheel to it and refuses drops over it | WPF (Fluent) |
| App/FenceHost.Collect, AutoCollectWindow (M27) | auto-collect rules (ADR-049): a `FolderLister` per watched folder (the desktop is two), arrivals routed to fences as items, watermarks, game-mode and Pause hold, removal release; Fence menu → Auto-collect… (the rules, a live "N items here match now", "Add these N too?" for a new rule) | WPF (Fluent) |
| App/FolderLister, FenceHost.Library | a folder's live listing (250 ms coalescing, 7 s retry, watcher backoff, removal notices, game-mode pause, the folder's own rename) for folder panels (M21, M26) and the Game Library's own `library\` folder (former Portal machinery), shown as tiles; scans launchers, game folders and Desktop game shortcuts (ADR-032) | FileSystemWatcher |
| App/SettingsWindow | General (Start with Windows, Hide desktop icons while NeoFences runs, Peek hotkey) · Fences · Appearance · Snapshots · Game Library · Game mode · Updates · About | WPF ThemeMode |
| App/InstallHooks, Shell/StartupRegistration, Core/StartupPolicy, build/pack.ps1 | Velopack installer and auto-update (ADR-023, ADR-039) | Velopack 1.2.161 |
| Shell/ShellDragDrop | drag out: a shell item array of the targets (absolute ID lists) turned into a data object, or URLs, copy/link only; one IDropTarget per fence: fence items (keys) → move / Ctrl-duplicate, outside items read as shell items (real paths, special items as `::{GUID}`, zip contents skipped; CF_HDROP as fallback) or one website (UniformResourceLocatorW / text) → new items; never a move effect | SHDoDragDrop, SHCreateShellItemArrayFromIDLists + BHID_DataObject, SHCreateShellItemArrayFromDataObject, SHParseDisplayName, RegisterDragDrop, IDropTargetHelper |
| Shell/ShellItems | display name, icon/thumbnail pixels, an icon from a file (index), the default browser's path, open (arguments, `runas`, the target's folder as working folder), show in folder; a generic icon by file type for targets the shell cannot reach (M19) | SHCreateItemFromParsingName, IShellItemImageFactory, SHDefExtractIcon, SHGetFileInfo, AssocQueryString, ShellExecute |
| Shell/KnownFolders (M21) | Downloads and Screenshots paths (moved ones too) for a new folder view's defaults | SHGetKnownFolderPath |
| Shell/AppList (M19) | Start's All apps (`FOLDERID_AppsFolder`, ~0.3 s for ~200 apps), whether an app still exists, an app dragged from Start | SHGetKnownFolderItem, BHID_EnumItems, IEnumShellItems |
| Shell/ShellItemMenu, DesktopNamespace | Windows' classic item menu for one target (Shift+right-click, with a disabled header line) and for the library's shortcuts (custom commands, Delete handed back) | IContextMenu3, TrackPopupMenuEx, InsertMenu |
| Shell/TargetProbe | a target's state off the UI thread; shares time out after 2 s (Unavailable); apps are Missing once Windows no longer knows them (M19). Also asked before UI-thread shell calls on a target (Windows' menu, picker start folders, drag-out, icon loading of shares; M19 R2/R7) | File/Directory.Exists |
| Shell/PathPicker, IconPicker | Windows' Open dialog (file with filters / folder, start folder); Windows' Change Icon dialog | IFileOpenDialog, PickIconDlg |
| Shell/FolderItems (+FolderWatcher) | folder listing (library, folder views), sort facts, watcher with `Renamed(old, new)` and the folder's own rename/removal via its parent | FileSystemWatcher |
| Shell/DesktopIcons, Watchdog | hide/show native icons (option); `--watchdog <pid>`: restore icons whenever `icons-hidden` exists, restart after a crash (3 per 10 min) or a cancelled session end (ADR-005, ADR-013) | IShellWindows, IFolderView2 |
| Shell/DeviceRemovalNotice | lets go of a watched drive Windows wants to remove ("Safely remove", DBT_DEVICEQUERYREMOVE) or that was pulled without asking (DBT_DEVICEREMOVECOMPLETE), so its watchers are re-armed when it is back | RegisterDeviceNotification |
| Shell/ShellFileOps | recycles NeoFences' own snapshot files (never items or targets) | IFileOperation |
| Shell/DesktopItems, SpecialIconNotifications | the Desktop listing for the library's game shortcuts; Recycle Bin full/empty → special icons reload | HideDesktopIcons registry key, SHChangeNotifyRegister |
| Shell/DesktopHost, FenceWindowChrome | fences at desktop level (owner = Progman, re-applied on TaskbarCreated), Peek, accent blur, rounded corners | SetWindowLongPtr(GWLP_HWNDPARENT), SetWindowCompositionAttribute |
| Shell/DesktopMouseHook (+DesktopWindows), Core/Input | desktop double-click (quick-hide), right-drag (draw a fence), Peek click-outside (ADR-020) | WH_MOUSE_LL (own thread), UI Automation |
| Shell/GlobalHotkey, GameDetection, TrayIcon, Monitors | Peek hotkey; game mode (ADR-021); tray; monitors and DPI | RegisterHotKey, SHQueryUserNotificationState, Shell_NotifyIcon, EnumDisplayMonitors |

## Threads

| Thread | Runs |
|---|---|
| UI (STA) | WPF, all fence windows, shell COM objects for menus and drops, WinEvent hook callbacks |
| InputHook | `WH_MOUSE_LL` + its own message loop; posts gestures to UI thread. Must return fast (< 1 ms). |
| Icon loaders (2× STA) | `IconLoader`: display names, icons from files and pictures, `IShellItemImageFactory`; frozen bitmaps marshalled to UI; only the newest request per item is applied |
| Open threads (STA, one per open) | `ShellWorker.RunAlone`: check the target, then ShellExecute (a UAC prompt or a slow share holds up nothing else) |
| Shell worker (STA) | `ShellWorker`: recycling snapshot files |
| Thread pool | `TargetProbe` checks (shares with a 2 s timeout), opening watchers, `FolderWatcher` events (marshalled to UI with `Dispatcher.BeginInvoke`), library scans (own STA thread) |
| Watchdog process | separate process, same exe, waits on main PID |

## Data

`%LOCALAPPDATA%\NeoFences\`
- `config.json` (+ `.bak`, `.tmp` transient) — schema 5
  - shape: `{ schemaVersion, settings: { hideDesktopIcons, peekHotkey, … }, fences: [ { id, title, isLibrary, view: { path, show, sort, newest, patterns } (M21), layout (M24), collect: [ { id, source, kinds, patterns, watermark } ] (M27), iconSize, rolledUp, locked, labels, tabs, activeTab, tabColor, customColor } ], layouts: { <fingerprint>: { monitors, fences: { <fenceId>: { monitor, x, y, w, h } } } }, library, lastLayoutFingerprint }`
- `items.json` (+ `.bak`) — schema 1 (ADR-041)
  - shape: `{ schema, fences: { <fenceId>: [ { id, target, name, icon: { file, index } | { image }, arguments, runAsAdmin, note, gameId, showAs (M22), size: { columns, rows }, cell: { column, row } (M24), widget: { seconds, date } (M25), panel: { look, show, patterns, newest, sort, descending }, fill (M26) } ] } }`
  - a list is removed only with its fence (Delete fence); lists of fences the config does not have stay (a fallback config never costs items; ADR-041 amended)
- `icons\<itemId>-<guid>.png` — pictures chosen as item icons (≤ 256 px); unused ones deleted at start
- `backups\config-<yyyyMMdd>.json`, `backups\items-<yyyyMMdd>.json` (keep 10 each), `backups\pre-schema-5-config.json`
- `snapshots\*.json` — fences, layouts and items; `before-restore.json`
- `library\` — the Game Library's shortcuts and `index.json`
- `logs\neofences-<date>.log`, `logs\watchdog-<date>.log` (keep 7)
- `clean-shutdown-<pid>`, `session-ending-<pid>` — markers consumed by the watchdog
- `icons-hidden` — exists while NeoFences has hidden the desktop icons; the watchdog restores only then
- `watchdog-restarts.txt` — restart timestamps for the 3-per-10-min limit

## Status

**0.9.0 (M18, virtual items)**: fences hold virtual items in `items.json`; drops create items (a link, never a move),
Del removes the item, Ctrl+drag duplicates, dragging out copies; NeoFences' item menu and Properties; Windows' menu on
Shift+right-click; targets watched (≤ 64 folders, rename-follow, Missing / Unavailable, 2 s throttle, 5-minute check,
Refresh); "Hide desktop icons while NeoFences runs" (off by default); Takeover, Inbox, desktop membership, Rules,
Portals and item file actions removed (ADR-040, ADR-041, `research/m18-virtual-items.md`).

**0.10.0 (M19)**: Store and Start apps as items (the "An app…" list, drags from Start; Missing when uninstalled); after
Locate…, "Fix N more items?" with an undo snapshot; "Add from desktop…" (fence menu, tray) grouping the desktop into
fences; the M18 reliability leftovers: removal notices released while watchers arm, shell calls on unreachable targets
checked first (2 s), COM objects released, failed watcher batches retried, stale per-target records dropped, OK waits
for the target check, generic icons for unreachable targets (ADR-042, `research/m19-apps-relocate-desktop-fill.md`).

**0.10.1 (M20)**: the M19 review minors — the bulk fix writes its undo snapshot only when something is fixed; programs
from the app list get readable placeholder names and Windows' menu (built from `shell:AppsFolder`); Add from desktop
reuses the Game Library's last scan and never counts a drive root or system folder as a game folder; shortcuts to Windows
places (Control Panel, This PC) and `file:///` links sort as folders and files (`ShellLinks.ShellTargetOf`); stale
per-target records dropped after a restore and when a check outlives its item; failed background checks logged.

**0.12.0 (M22, one kind of fence: games become items)**: a game is a virtual item (`GameId`, `ShowAs`) whose target is
NeoFences' own shortcut for it in `library\`; any fence can hold games next to other items, as cover tiles or icons
(cells sized per item). The library scan is the engine: it runs while game items exist, a fence takes new games, or Add
games… is open; after each scan game items follow their game's shortcut and new games go to the chosen fence. An old
Game Library fence becomes an items fence once (a "Before games became items" snapshot first). The library fence kind
stays in the code, not in the menus (ADR-045, `research/m22-games-as-items.md`).

**0.16.1 (M28, polish)**: the deferred minors of M24–M27 — folder panels (columns by real width, no hover name over
them, the Menu key at the row, panel and widget controls built only where used), auto-collect (the offer on a slow folder,
no watermark going back, no catch-up during a game, items saved before the advanced watermark), widgets (right at once
after pause / quick-hide / a game, stats rows rebuilt only when a value changes, no Change icon, unknown kinds Missing, GPU = the busiest
adapter, month and year in the culture's order), grid and sizes (the element under the pointer leads a group drag, one
icon load per size change, hidden tabs' own columns, the Size picker by keyboard, full-size covers in icon-only fences,
mixed drops at the drop cell) (`research/m28-polish.md`).

**0.16.0 (M27, auto-collect rules)**: a fence can collect new files of a folder by itself (fence menu → Auto-collect…):
the desktop or any folder, by kind (apps and shortcuts, installers, documents, pictures, archives, anything) or pattern;
each new matching file becomes an item in the first fence whose rule matches; nothing is moved; removing an item does not
bring it back; files created while NeoFences was closed are caught up at start (ADR-049, `research/m27-auto-collect.md`).

**0.15.0 (M26, the folder panel element)**: any fence can show a folder as a panel — Details (columns that sort), List
or Icons — beside its other elements, at 1–4 × 1–4 cells or filling the fence (fence menu → Add folder panel…, tray → New
folder panel…, a folder item → Show as folder panel). Double-clicking a subfolder browses inside the panel (Back / Up /
Home). Folder views became fences holding one filling panel (a snapshot first). Read-only as before (ADR-048,
`research/m26-folder-panel.md`).

**0.14.0 (M25, widgets)**: Clock, Date and System stats elements in any fence (fence menu → Add widget ▸), any size;
the clock follows Windows' time format, with optional seconds and date line; stats show CPU, RAM, GPU and C: as bars every
2 s; nothing updates while no widget can be seen (ADR-047, `research/m25-widgets.md`).

**0.13.0 (M24, element sizes and the fence grid)**: every element spans 1–4 columns × 1–4 rows of its fence's cells
(Size ▸, a 4×4 picker); a bigger element shows a bigger icon (≤ 256 px) or cover; game covers default to 1×2. Each fence is
Flow (packed in order, smaller elements fill gaps) or Free (fixed positions; switching keeps everything where it is; a
drop lands on the cell under the pointer or the nearest free spot) (ADR-046, `research/m24-element-sizes.md`).

**0.12.1 (M23)**: the M21/M22 review minors — a folder view sorts its listing off the UI thread; a hidden view tab's title
follows its folder's rename; Folder view settings takes full paths only (variables expanded) and says why OK is greyed
out; deleting a fence refreshes Settings and stops an unneeded game scan, and new games never go to a gone fence; a game
shortcut the scan rewrites is not shown "not installed" for a moment; the "New games go to" list is rebuilt only when the
fences change; "Open install folder" is off for a game that is not installed.

**0.11.0 (M21, folder views)**: a fence can show one folder live, read-only (ADR-044): "New folder view…" (tray, fence
menu) or "Show as folder view" on a folder item; per view: files and folders / files only / folders only, type patterns,
sort (kept, live), only the newest N; Downloads and Screenshots start newest first with 30; at most 500 entries, then
"+ N more — Open folder"; "Folder not available" while the folder or its drive is gone, back by itself; Safely Remove
works while shown; the view follows its folder's rename. Entries open, show in Explorer, copy their path, go to a fence
as items (Add to fence ▸, or a drag) and drag out as copies; drops onto a view are refused (`research/m21-folder-views.md`).

Pre-pivot history (v1.x dev builds, the M0–M17 notes in `research/` and SESSION-LOG) describes the Takeover model; read
it as history. Kept from it: fences, tabs (M9), snapshots (M10), the Game Library (M12), roll-up, lock, Peek, game mode,
Appearance (M14/M16), the installer and auto-update (M7/M17).

Core contracts the App relies on: edits return a new document/config, the **same** instance when nothing changed (records
compare lists by reference, so use `ReferenceEquals`); `ConfigStore.Load` / `ItemStore.Load` may return `IsReadOnly`
(newer schema or locked file) and `Save` then returns false; `LayoutEngine.Resolve` throws `ArgumentException` for
unusable monitor work areas (FenceHost skips that resolve).
