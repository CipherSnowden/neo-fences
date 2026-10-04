# NeoFences v1 — Design Spec

**Status:** Approved 2026-10-02 (brainstorming session 1).
**Scope:** v1 = core fences + Takeover + Portals + Quick-hide + Peek + game-mode idle.
**This file is a dated snapshot.** The living technical description is `docs/ARCHITECTURE.md`;
decisions with rationale are in `docs/DECISIONS.md`. If they disagree, ARCHITECTURE/DECISIONS win
and this spec gets an erratum note at the bottom.

---

## 1. Goal

A Stardock-Fences-style desktop organizer for Windows 10/11 x64, built for one power user
(gamer, Wallpaper Engine user) first. Priorities, in order: **robust and reliable** > modern
UI/UX > decent performance > feature count.

Non-goals for v1: rules auto-sort, game library fence, theme packs, fence tabs, desktop pages,
snapshots, cross-platform. (All tracked in `docs/FEATURES.md` as v2+.)

## 2. Stack

- C# / .NET 10 (LTS), WPF, built-in WPF Fluent theme.
- `Microsoft.Windows.CsWin32` for all Win32/COM bindings (source-generated).
- `CommunityToolkit.Mvvm`, `H.NotifyIcon.Wpf`, `Serilog` (+ file sink), `System.Text.Json`.
- Tests: xUnit for `NeoFences.Core`. Installer/updates: Velopack (M7).
- Rejected: Avalonia (OLE drag-drop + desktop-window friction, cross-platform value low for a
  shell-integration app), Rust + Direct2D (≈3× UI effort), WinUI 3 (poor transparent/tool
  windows, packaging friction), Electron/Tauri (WebView per fence, weak shell drag-drop).

## 3. Architecture

Single process `NeoFences.exe`. UI on one STA thread (COM shell objects need STA). Low-level
mouse hook on its own thread with its own message loop.

```
NeoFences.App   (WPF)      TrayIcon, SettingsWindow, FenceWindow (1 per fence),
                           FenceViewModel, DrawFenceOverlay
NeoFences.Shell (Win32)    DesktopLayer, DesktopIcons, ShellItems, ShellActions,
                           InputHook, GameDetector, Displays
NeoFences.Core  (pure C#)  Model, LayoutEngine, ConfigStore, Membership
```

Rule: `Core` references nothing Windows-specific (unit-testable, portable). All Win32/COM lives
in `Shell`. `App` is UI only.

Example flow — file saved to Desktop:
`ShellItems` watcher (SHChangeNotifyRegister) → "created" → `Membership` assigns Inbox →
`ConfigStore` debounced save (500 ms) → Inbox view model adds item → icon fades in.

## 4. Desktop integration

### 4.1 Fence windows
Top-level WPF windows: `WS_EX_TOOLWINDOW` (no taskbar/Alt+Tab), `WS_EX_NOACTIVATE` (no focus
steal), DWM acrylic via `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_TRANSIENTWINDOW)`
(Windows 11 22H2+; Windows 10 falls back to a tinted semi-opaque background). Kept at the bottom
of the z-order with `SetWindowPos(HWND_BOTTOM)` whenever raised.

Why top-level instead of parenting into WorkerW: DWM backdrops only apply to top-level windows,
so this gives a live blur of Wallpaper Engine for free, and it does not depend on Explorer's
internal window tree, which Windows 11 24H2 changed (WorkerW is now a child of Progman).

### 4.2 Win+D / Show desktop
`SetWinEventHook(EVENT_SYSTEM_FOREGROUND, WINEVENT_OUTOFCONTEXT)`. Foreground becomes the
desktop (Progman/WorkerW owning SHELLDLL_DefView) → raise fences just above it. Any other
window becomes foreground → fences back to HWND_BOTTOM.
**M0 risk.** Fallback: parent fences into the desktop host window, lose acrylic, use tint.

### 4.3 Takeover (hide native icons)
`IFolderView2::SetCurrentFolderFlags(FWF_NOICONS, FWF_NOICONS)` on the desktop's folder view
(obtained via `IShellWindows::FindWindowSW(SWC_DESKTOP)` → `IServiceProvider` →
`IShellBrowser::QueryActiveShellView`). Explorer persists this, so:
- Clean exit / pause → clear the flag.
- **Watchdog**: `NeoFences.exe --watchdog <pid>` waits on the main process handle. Exit without
  the clean-shutdown marker → restore icons, then restart NeoFences (max 3 restarts / 10 min).

### 4.4 Explorer restart
Listen for the registered `TaskbarCreated` message → re-resolve desktop windows, re-apply
FWF_NOICONS, re-attach hooks and folder view.

### 4.5 Desktop gestures (InputHook)
`WH_MOUSE_LL` on a dedicated thread. Only acts when the point is over the desktop (not over a
fence or any app window).
- Double-click empty desktop → toggle Quick-hide.
- Right-drag ≥ 8 px on empty desktop → rubber-band overlay → new fence. Plain right-click still
  shows Explorer's desktop menu.
**M0 risk:** suppressing Explorer's context menu after a drag. Fallback: Ctrl+right-drag, and
"New fence" in tray/fence menus.

### 4.6 Peek
`RegisterHotKey`, default `Ctrl+Alt+Space` (configurable). Fences go topmost; same hotkey, Esc,
or a click outside a fence returns them.

### 4.7 Game mode
Evaluated on every foreground change (no polling): active when `SHQueryUserNotificationState`
returns `QUNS_RUNNING_D3D_FULL_SCREEN` or `QUNS_BUSY`, **or** the foreground window rect covers
its monitor (borderless games) and is not a shell window. While active: unhook `WH_MOUSE_LL`,
stop animations, queue shell change events and apply them on exit.

## 5. Data model and persistence

One JSON file: `%LOCALAPPDATA%\NeoFences\config.json`.

```json
{
  "schemaVersion": 1,
  "settings": { "takeover": true, "peekHotkey": "Ctrl+Alt+Space",
                "startWithWindows": true, "gameMode": true,
                "rollupExpand": "hover" },
  "fences": [
    { "id": "a1f3…", "title": "Games", "source": { "kind": "desktop" },
      "items": ["C:\\Users\\cipher\\Desktop\\Crysis 2.lnk",
                "::{645FF040-5081-101B-9F08-00AA002F954E}"],
      "sort": "manual", "iconSize": 48, "rolledUp": false, "locked": false },
    { "id": "b7c2…", "title": "Inbox", "isInbox": true,
      "source": { "kind": "desktop" }, "items": [] },
    { "id": "c9d4…", "title": "Screenshots",
      "source": { "kind": "portal", "path": "D:\\Pictures\\Screenshots" }, "sort": "date" }
  ],
  "layouts": {
    "<display fingerprint>": {
      "a1f3…": { "monitor": "<device id>", "x": 40, "y": 60, "w": 420, "h": 260 }
    }
  }
}
```

- **Item identity:** shell parsing name (file path, or `::{GUID}` for virtual items). Covers
  user Desktop and Public Desktop.
- Rename (`SHCNE_RENAMEITEM`) → update ref. Delete → remove. New → Inbox.
  Startup reconcile: drop missing refs, unknown items → Inbox.
- **Desktop fence:** ordered `items`, manual order by drag, or sorted.
  **Portal fence:** no item list; live view of `path`, sort by name/type/date, subfolder
  navigation with Back.
- Exactly one Inbox exists; it can be renamed/moved but not deleted.
- **Display fingerprint:** monitors + resolutions + DPI scales. Fence coords are DIPs relative
  to the monitor's work area. Known fingerprint → restore exactly. Unknown → copy most recent
  layout, keep each fence on the same monitor if present (else primary), scale proportionally,
  clamp on-screen, save under the new fingerprint.
- **Saving:** debounced 500 ms; write `config.json.tmp` → `File.Replace` (keeps
  `config.json.bak`); daily copy to `backups/` (keep 10).
- **Load failure:** try `.bak`, then newest backup, then fresh (single Inbox). Corrupt file kept
  as `config.corrupt-<yyyyMMdd-HHmmss>.json`.
- **First run:** Takeover on, every existing desktop item goes into one Inbox fence.
- Single instance: named mutex. Autostart: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## 6. UI and interactions

Default look: **acrylic card** — live acrylic blur, 8 px radius, 1 px light border, slim title.

```
 ╭─────────────────────────────────╮
 │ Games                      ⌃  ⋯ │  title: drag = move, double-click = roll up
 ├─────────────────────────────────┤  buttons appear on hover
 │ ▣ Crysis 2   ▣ AC Unity  ▣ GOG  │
 │ ▣ Steam      ▣ Mirage    ▣ ...  │  thin scrollbar on hover
 ╰────────────────────────────────◢╯  resize edges/corner, snap 8 px
 rolled up: ╭─ Games (12) ──── ⌄ ⋯ ╮
```

- Icons: `IShellItemImageFactory` (thumbnails for images/videos), loaded off the UI thread,
  cached. Labels 2 lines + ellipsis. Icon size per fence: 32/48/64/96.
- Selection: click, Ctrl, Shift, rubber-band. Keys: arrows, Enter (open), F2 (rename),
  Del (Recycle Bin), Ctrl+A.
- Open: `ShellExecuteEx` with the item's PIDL (default verb).
- Item right-click: classic shell menu via `IContextMenu3` (Windows 11's compact menu is
  Explorer-private; custom compact menu is v2).
- Fence right-click (empty area): New fence, Rename, Roll up, Sort by ►, Icon size ►,
  Lock position, Fence settings…, Delete fence (**items → Inbox, never deletes files**).
- Roll-up expand mode (from Fences 6.20): hover (300 ms) | click. Default hover.
- Smart placement: new/moved fences snap to 8 px spacing from other fences and screen edges.
- Drag-drop:

| From → To | Behaviour |
|---|---|
| desktop fence → desktop fence | membership/order change only, no file op |
| Explorer → desktop fence | forwarded to Desktop folder's shell `IDropTarget` (native copy/move, progress, undo); item lands in target fence |
| desktop fence ↔ portal fence | real move/copy, shell semantics (Shift = move, Ctrl = copy) |
| fence → any app | `SHDoDragDrop` with the shell's `IDataObject` |
| item → Recycle Bin item | shell delete |

- Tray: New fence · Quick-hide · Peek · Settings · Pause NeoFences (restores icons) · Exit.
- Settings window (Fluent, follows Windows light/dark + accent): General, Appearance,
  Game mode, About & logs.
- Animations: roll-up 200 ms, quick-hide fade 150 ms; disabled when Windows
  "Animation effects" is off (`SPI_GETCLIENTAREAANIMATION`).

## 7. Reliability rules

1. Never lose or hide user files. File deletion only via shell Recycle Bin flow. Fence removal
   never touches files.
2. Native icons always come back: clean exit, crash, Task Manager kill (watchdog), Explorer
   restart.
3. No DLL injection, no in-process global hooks. Only out-of-context WinEvent hooks and
   `WH_MOUSE_LL` (removed in game mode).
4. All Win32/COM behind `NeoFences.Shell`. Failures logged, degraded per feature; one bad
   thumbnail never crashes the app.
5. Last-chance handler: log → restore icons → exit; watchdog restarts.
   Logs: `%LOCALAPPDATA%\NeoFences\logs`, daily rolling, keep 7.

## 8. Testing

- `NeoFences.Core`: xUnit, test-first — layout scaling, fingerprint matching, membership,
  reconcile, config load/migrate/corrupt-recovery.
- `NeoFences.Shell` / App: `docs/TEST-CHECKLIST.md` manual script, run at each milestone
  (Win+D, Explorer restart, game launch, monitor change, Wallpaper Engine on/off, kill via
  Task Manager).
- Daily dogfooding from M2.

## 9. Milestones

| ID | Name | Exit criteria |
|---|---|---|
| M0 | Spike: desktop layer | acrylic window over Wallpaper Engine survives Win+D, Explorer restart on 24H2; icons hide/restore + watchdog; desktop double-click + right-drag detected. GO/NO-GO on §4.1–4.5 |
| M1 | Core + config | models, ConfigStore, LayoutEngine, Membership with tests |
| M2 | Fences render items | Desktop + Inbox fences, icons, open, select, move/resize, Takeover first run — daily usable |
| M3 | Shell actions | context menu, rename, delete, drag-drop all directions |
| M4 | Portals | folder fences, subfolder nav, sort |
| M5 | Desktop gestures | draw fence, quick-hide, peek, roll-up + hover/click modes |
| M6 | Polish + game mode | settings, tray, animations, autostart, game mode |
| M7 | Package | Velopack installer, v1.0 |

## 10. Open items / known risks

- §4.2 Win+D strategy and §4.5 menu suppression are unproven until M0.
- **Erratum (M0, ADR-011):** §4.1 DWM system backdrop is replaced by a layered window + accent
  blur (DWM backdrops go flat on inactive windows). §4.2 raise-on-Win+D is replaced by making
  Progman the fence's owner. §4.5 uses suppression S2 (swallow + replay). §4.3 adds a detached
  watchdog and explicit session-end handling.
- Windows 10: no DWM acrylic; tinted fallback only.
- Name: "Fences" is a Stardock trademark. Fine for private use; rename before any public
  release (candidates: Palisade, Bastion, Enclave).
