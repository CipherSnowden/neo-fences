# Decisions (ADR log)

Append-only. To change a decision, add a new ADR that **supersedes** the old one and mark the old
one `Superseded by ADR-xxx`. Format: context → decision → consequences.

---

## ADR-001 — Stack: C# / .NET 10 + WPF
**Date:** 2026-10-02 · **Status:** Accepted

**Context.** Desktop shell integration app; needs modern UI, robust shell interop (context menus,
OLE drag-drop, thumbnails, IFileOperation), decent performance, fast multi-session development.
Cross-platform is "only if feasible and needed" later.

**Decision.** C# on .NET 10 (LTS), WPF with the built-in Fluent theme, CsWin32 for bindings.
Dependencies: CsWin32, CommunityToolkit.Mvvm, H.NotifyIcon.Wpf, Serilog (+file sink), xUnit,
Velopack (M7). Projects: `NeoFences.Core` (pure), `NeoFences.Shell` (Win32/COM),
`NeoFences.App` (WPF).

**Alternatives.** Avalonia (cross-platform, but drag-drop/desktop-window friction; a fences app
is mostly platform-specific shell code anyway — Linux KDE has Folder View, macOS sandboxing
blocks most of this). Rust + windows-rs + Direct2D (~20-40 MB vs ~80-150 MB, but ~3× UI
effort). WinUI 3 (weak transparent/tool window support, packaging friction).
Flutter (re-evaluated 2026-10-02 at the user's request): Flutter 3.44.8 stable has no public
multi-window API (only `@internal` on the main channel behind `--enable-windowing`), and every
fence must be its own OS window; implementing COM servers such as `IDropTarget` in Dart FFI is
impractical, so the shell layer would have to be a C++ plugin. Revisit for a v2+ UI only if
Flutter's windowing API becomes stable.

**Consequences.** Windows-only UI. Memory ~80-150 MB. Core stays portable for an Avalonia port.
WPF has no Native AOT; use ReadyToRun for startup.

## ADR-002 — Icon model: Takeover + Portals, no injection
**Date:** 2026-10-02 · **Status:** Accepted

**Context.** Stardock Fences injects into explorer.exe to control the real desktop ListView.
Injection is fragile across Windows updates and AV-flaggy.

**Decision.** NeoFences draws all items itself. *Takeover*: native desktop icons hidden via
`IFolderView2::SetCurrentFolderFlags(FWF_NOICONS)`; every Desktop/Public Desktop item lives in a
fence; unassigned → Inbox. *Portals*: any fence can instead mirror a folder. Files are never
moved by Takeover.

**Consequences.** We implement icon interactions ourselves (open, context menu, rename, delete,
drag-drop) via shell COM. FWF_NOICONS persists in Explorer → watchdog required (ADR-005).
Windows 11 compact context menu unavailable (Explorer-private); classic menu used.

## ADR-003 — Fence windows are top-level, bottom-of-z-order (not children of WorkerW)
**Date:** 2026-10-02 · **Status:** Partly superseded by ADR-011 (top-level windows: kept; HWND_BOTTOM + raise-on-Win+D and DWM system backdrop: replaced)

**Context.** Child-of-WorkerW survives Win+D trivially but gets no DWM backdrop, and Windows 11
24H2 restructured the desktop window tree (WorkerW is now a child of Progman), which broke
wallpaper tools.

**Decision.** Top-level `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` windows kept at `HWND_BOTTOM`;
`EVENT_SYSTEM_FOREGROUND` WinEvent hook raises them above the desktop on Win+D. DWM acrylic
backdrop (Win11 22H2+), tinted fallback on Windows 10.

**Consequences.** Live blur over Wallpaper Engine. Independent of Explorer's window tree. Must
handle Win+D/Peek ourselves. If M0 fails: fall back to parenting (supersede this ADR).

## ADR-004 — v1 scope
**Date:** 2026-10-02 · **Status:** Accepted

v1 = core fences, Takeover + Portals, drag-drop, shell context menu, roll-up, scrolling,
per-monitor layouts, tray, settings, game-mode idle, Quick-hide, Peek. First run puts all
existing desktop items into one Inbox fence. Default look: acrylic card.
Deferred to v2+: rules auto-sort, game library fence, theme packs (Nanosuit/Animus), tabs,
desktop pages, snapshots, icon tint, custom compact context menu.

## ADR-005 — Watchdog process restores icons (refined by ADR-011)
**Date:** 2026-10-02 · **Status:** Accepted

**Decision.** Main process spawns `NeoFences.exe --watchdog <pid>`. Main writes a
`clean-shutdown` marker on orderly exit. Watchdog: process exited without marker → clear
FWF_NOICONS, restart main (max 3 restarts per 10 min, then stop and leave icons visible).

**Consequences.** One extra small process (~15-25 MB). Covers crashes and Task Manager kills.

## ADR-006 — Persistence: single JSON file with atomic writes and backups
**Date:** 2026-10-02 · **Status:** Accepted, amended in M1 (reflection-based JSON; layouts store monitor work areas)

**Decision.** `%LOCALAPPDATA%\NeoFences\config.json`, System.Text.Json (source-generated),
`schemaVersion` + migrations, debounced 500 ms saves via tmp + `File.Replace` (keeps `.bak`),
daily backups (keep 10), corrupt-file recovery chain. Layouts keyed by display fingerprint.
No database (data is tiny; YAGNI).

**Amendment (M1).** JSON is reflection-based System.Text.Json, not source-generated: the source
generator assigns every init-only property, so properties missing from a hand-edited file lose
their defaults (e.g. `iconSize` 0). WPF has no AOT need, so nothing is lost. Enum values are
camelCase strings. Each layout also stores `monitors: { id: { workWidth, workHeight } }`, which is
needed to scale a layout to a new display setup. Debouncing saves (500 ms) lives in the App (M2).

## ADR-007 — No input injection or in-process global hooks; low-level mouse hook only (refined by ADR-011)
**Date:** 2026-10-02 · **Status:** Accepted

**Decision.** Desktop gestures use `WH_MOUSE_LL` on a dedicated thread; window tracking uses
out-of-context WinEvent hooks. The mouse hook is removed in game mode so games get zero added
input latency.

**Consequences.** Anti-cheat/AV friendly. Gesture suppression of Explorer's desktop menu is
an M0 risk (fallback: Ctrl+right-drag, menu entries).

## ADR-008 — Docs and multi-session workflow
**Date:** 2026-10-02 · **Status:** Accepted

**Decision.** `CLAUDE.md` holds hard rules + session protocol. `docs/` holds ARCHITECTURE
(living), DECISIONS (this), FEATURES, ROADMAP (with claims), SESSION-LOG (append-only), SETUP,
TEST-CHECKLIST, `superpowers/specs` (dated snapshots), `superpowers/plans`. Git is the sync
mechanism; parallel sessions use separate worktrees/branches. Commits: single-line
Conventional Commits, no co-author trailer.

## ADR-009 — Project name
**Date:** 2026-10-02 · **Status:** Superseded by ADR-043 (the name stays NeoFences)

"NeoFences" for private use. "Fences" is a Stardock trademark → rename before any public
release (candidates: Palisade, Bastion, Enclave). Namespace/assembly names use `NeoFences`;
a rename is a mechanical find-replace.

## ADR-010 — Project hub artifact
**Date:** 2026-10-02 · **Status:** Accepted

**Context.** User understands best visually and wants one easy-to-ingest view of progress,
plan, architecture, features, decisions and research.

**Decision.** A single HTML page, source `docs/hub/neofences-hq.html`, published as a private
claude.ai artifact (URL in CLAUDE.md). Data-driven: a `HUB` object holds all list content;
SVG + mermaid diagrams for z-order, window tree, layers, flows, journeys. Refreshed and
republished at every session end.

**Consequences.** One more thing to keep in sync; `docs/` remains canonical and wins on
conflict. Sessions must republish with the existing `url` to avoid duplicates.

## ADR-011 — M0 results: desktop layer choices
**Date:** 2026-10-02 · **Status:** Accepted (Takeover conditional on C8) · **Refines:** ADR-003 (partly supersedes), ADR-005, ADR-007

**Context.** M0 spike on Windows 11 Pro 25H2 (build 26200), RTX 5070 Ti, one 1920×1080 monitor at
100%, Wallpaper Engine running. Evidence: `docs/research/desktop-layer.md`. Most checks ran on the
original glass fence; the layered fence only covered A1–A3 and Win+D, so M2 re-verifies the chosen
combination first (research note, "M2 re-verification").

**Decision.**
- **Fence window:** top-level, `WS_EX_TOOLWINDOW`, **layered** (WPF `AllowsTransparency`), with
  `SetWindowCompositionAttribute` `ACCENT_ENABLE_BLURBEHIND`. DWM system backdrops are not used:
  they render flat grey on inactive windows, and fences are almost never active. Fallback: layered
  tint without blur. The undocumented accent call lives in one function in `NeoFences.Shell`, with a
  `// ponytail:` comment naming the risk. The tint level is not settled: M2 confirms that a tint
  applies in state 3.
- **Z-order:** fence owned by `Progman` via `GWLP_HWNDPARENT`, re-applied on `TaskbarCreated` and
  verified with `GetWindow(GW_OWNER)`. It survives Win+D, the Show-desktop corner, Win+M and a forced
  Explorer kill; apps still cover it.
- **Desktop icons:** `FWF_NOICONS` via `IFolderView2`, plus the watchdog (3 restarts per 10 min).
  - The watchdog is started detached from the main process and skips its own cleanup; the main
    process clears stale markers.
  - Session end is handled in our own window hook, not WPF's `SessionEnding`: restore icons on
    `WM_QUERYENDSESSION`; write the clean-shutdown marker only on `WM_ENDSESSION(TRUE)`; on
    `WM_ENDSESSION(FALSE)` (shutdown cancelled) re-apply Takeover and write no marker.
  - **Takeover does not ship enabled by default until a real sign-out test (C8) passes.** The user
    deferred C8 to M2 on 2026-10-02.
- **Right-click suppression:** S2 (swallow the desktop right-press; on release without a drag,
  replay a marked right-click). The draw-fence gesture stays a plain right-drag.
- **Game detection:** game mode = (`QUNS_BUSY` or `QUNS_RUNNING_D3D_FULL_SCREEN`) **and** the
  foreground window is a non-shell, non-lock-screen app (`QUNS_NOT_PRESENT` excluded). Re-check
  shortly after activation, because QUNS flipped about 1 s after the game took focus. QUNS was the
  only signal that caught the tested game; the cover-monitor check never fired. Refined in M6.

**Consequences.**
- M2 rounds the fence's corners itself (layered windows lose DWM rounding), checks drag smoothness
  and animation on layered windows, implements session-end handling and runs one real sign-out test.
- **Owner-by-Progman risks:** changing the owner after creation is undocumented (the same class as
  the accent API). A cross-process owner attaches input queues, so a hung fence UI thread could
  freeze desktop input. M2 runs a UI-thread hang test before building on it.
- Clicking a fence activates it; that stays, because keyboard shortcuts need focus.

## ADR-012 — M2 split and fence-host mechanics
**Date:** 2026-10-02 · **Status:** Accepted · **Refines:** ADR-005, ADR-011

**Decision.**
- M2 is split into M2a (fence host), M2b (desktop items, Takeover + first run) and M2c (fence
  interactions); each ends runnable.
- The watchdog starts detached through a short-lived `--watchdog-launch` process (no parent-PID
  spoofing, no WMI). It restores icons only while `takeover-active` exists, retrying ~5 s if
  Explorer is down.
- `NeoFences.exe --exit` signals the running instance (event `Local\NeoFences.Exit`) to quit
  cleanly; single instance via mutex `Local\NeoFences.Main`.
- Monitors are identified by device interface path; fences are placed in physical pixels from DIP
  rects (per-monitor-v2 manifest).
- After every Show and every Explorer re-attach the fence is sent to `HWND_BOTTOM`: Show puts a window
  on top of every app, and an owned window's "bottom" is just above its owner (Progman).
- Shell/App target `net10.0-windows` with CA1416 suppressed (Windows 10 1809+ minimum); a
  versioned TFM would add the WinRT projections.
- Stored layouts keep unclamped rects; only displayed rects are clamped.

**Consequences.** If NeoFences runs inside a job object that kills children (some launchers,
debuggers), the watchdog dies with it; normal launches (Explorer, Run key) are unaffected.
Verification: `docs/research/m2a-fence-host.md` (GO for M2b; C8 sign-out still gates Takeover).

## ADR-013 — Session end goes through WPF; icon calls never run inside a sent message
**Date:** 2026-10-02 · **Status:** Accepted · **Supersedes:** the session-end part of ADR-011

**Context.** M2a review: WPF's own parking window answers WM_QUERYENDSESSION, raises
`Application.SessionEnding` and then calls `Shutdown()`, so the app always exits on the query, before
WM_ENDSESSION. A COM call to Explorer made inside that sent message fails with
RPC_E_CANTCALLOUT_ININPUTSYNCCALL. ADR-011's design (restore on the query, marker on ENDSESSION(TRUE),
re-hide on ENDSESSION(FALSE)) therefore never ran as written.

**Decision.**
- `Application.SessionEnding` is the single session-end hook (never cancelled: that would block sign-out).
  Inside it NeoFences saves, restores the icons and writes `session-ending-<pid>`; WPF then exits the app.
- `DesktopIcons` runs every Explorer COM call on a thread-pool (MTA) thread with a 4 s limit, so it works
  inside sent messages and a hung Explorer cannot hang the caller.
- The watchdog decides from markers via `WatchdogPlan`: it restores icons whenever `takeover-active`
  exists (whatever the exit kind), never restarts after a clean exit, restarts after a crash (3 per 10 min),
  and after `session-ending` restarts only once Windows stops shutting down (user cancelled; checked with
  SM_SHUTTINGDOWN for up to 30 s).
- Helper modes (`--exit`, `--watchdog-launch`, `--watchdog`) run from a custom `Main` without WPF: the
  watchdog owns no window, so it never sits on the "apps are blocking shutdown" screen.
- NeoFences does not start (and so never hides icons) while the session is shutting down.

**Consequences.** A cancelled shutdown briefly restarts NeoFences (about 2 s) instead of keeping it
running. Verified with a synthetic WM_QUERYENDSESSION sent to every top-level window: icons restored inside
the query, app exited, watchdog restarted it, icons hidden again. A real sign-out (C8) is still pending.

## ADR-014 — Desktop items: file-system listing, shell names and icons, ask-once first run
**Date:** 2026-10-02 · **Status:** Accepted

**Context.** M2b puts the real desktop into fences. It needs to decide four things: what counts as a desktop item, how changes reach the fences, where names and icons come from, and how a first run should treat Takeover. ADR-011 keeps Takeover off by default until the real sign-out check (C8) passes.

**Decision.**
- **Listing.** The desktop is the visible (not Hidden or System) entries of the user's Desktop folder (`SpecialFolder.DesktopDirectory`, which follows a OneDrive-redirected Desktop) and of the Public Desktop. On top of those come the special icons enabled under `HKCU\…\HideDesktopIcons\NewStartPanel` (Recycle Bin, This PC, User files, Network, Control Panel; Windows default: Recycle Bin only). Refs are paths or `::{CLSID}` (ADR-002). The order is special icons first, then by name.
- **Completeness.** The listing reports the folders it could not read (missing, offline, unreadable). `Reconcile` keeps
  fenced refs under those folders and trusts the listing for everything else; only an empty listing keeps everything.
  This replaces M1's "more than half missing" guess, which both missed a whole unreadable Desktop and kept deleted
  items as ghosts forever (M2b review).
- **Changes.** One `FileSystemWatcher` per folder (64 KB buffer). Each event becomes a Core `DesktopChange`, applied by `FenceMembership.Apply` on the UI thread. Attribute changes (hide/unhide) count as delete/create. On a watcher error or overflow the watcher is recreated (.NET stops one after any non-overflow error) and a full `Reconcile` runs. Special-icon toggles are not watched; they are picked up at the next start. ponytail: upgrade path `SHChangeNotifyRegister`.
- **Names and icons.** `IShellItem` `SIGDN_NORMALDISPLAY` gives the label and `IShellItemImageFactory` the icon or thumbnail at 48 DIP × DPI. Both run on one dedicated background STA thread, and the result reaches the UI as a frozen `BitmapSource`. Until it arrives, the label is the file name and the icon is empty. Items already shown keep their loaded icon across refreshes.
- **Open.** On a background thread (it can block on a network timeout or UAC prompt), `ShellExecute` with the default verb (`explorer.exe shell:::{CLSID}` for special icons), after `AllowSetForegroundWindow(ASFW_ANY)`. Fences never activate, so without that call the opened window could stay behind.
- **First run** (user decision, 2026-10-02): the Inbox shows a one-time banner, "Hide the desktop icons and keep them only in fences?" [Hide them] [Not now]. Any explicit choice, from the banner or the menu, sets `Settings.TakeoverPromptAnswered`. Takeover stays off until the user chooses it.
- **Keyboard** (arrows, Enter, Delete, F2) moves to M2c. Fences are `WS_EX_NOACTIVATE`, so keyboard input needs an activation design that keeps fences at the bottom.

**Consequences.**
- The Recycle Bin icon does not switch between full and empty while running.
- Icons are not re-rendered after a DPI change.
- Shortcut arrows are not drawn.
- Listing reads the file system, not Explorer's view. It therefore works before Explorer is ready at sign-in, but would miss shell-only desktop items such as namespace extensions (none seen on the dev machine).

Verified in `docs/research/m2b-desktop-items.md`.

## ADR-015 — Fence interactions: activation for keyboard, snapping on the unsnapped drag, theme veil drawn by the fence
**Date:** 2026-10-02 · **Status:** Accepted · **Corrects:** ADR-014's keyboard and "fences never activate" statements

**Context.** M2c adds keyboard, snap, lock, rename, icon size, delete, a styled scrollbar, label shadows and
light/dark. The user chose: fences follow Windows' light/dark mode, and labels get a soft shadow (2026-10-02).

**Decision.**
- **Keyboard.** Arrows and Ctrl+A work through the ListBox, and Enter opens the selection, once the fence is active.
  A click activates a fence despite `WS_EX_NOACTIVATE` **only when the desktop had focus** (after Win+D, or a click on
  the desktop or a fence; M0 B2). With another app in front it does not, and neither `Activate()` on mouse-down nor
  `WM_MOUSEACTIVATE` → `MA_ACTIVATE` changed that. This fits the fence sharing Explorer's input queue as Progman's
  owned window (M0 finding 9). Accepted for now: the mouse always works, and keyboard focus from an app is a ROADMAP
  follow-up. ADR-014's "fences never activate" was wrong. F2 and Del on items are shell actions (M3).
- **Snapping** (spec §6: 8 px gap, 12 px reach, line-up with neighbouring edges) runs in `WM_MOVING` / `WM_SIZING`.
  Windows proposes each rect as "the window now + this mouse step". Snapping that proposal snaps every 2–3 px step
  back, and the fence sticks. The fence therefore tracks the unsnapped drag rect (`Snapping.Unsnapped`, adding
  each step per edge) and snaps that.
- **Lock** sets a fresh `WindowChrome` with caption height 0 and no resize border. Editing the attached chrome in
  place was not re-applied after unlocking.
- **Theme.** The fence follows `AppsUseLightTheme` live, via `WM_SETTINGCHANGE "ImmersiveColorSet"`. The accent
  blur ignores its tint colour (`ACCENT_ENABLE_BLURBEHIND`), so the fence draws its own veil: none in dark mode
  (the M2a look the user approved) and ~72 % light in light mode. Text, borders and hover colours come from
  DynamicResources.
- **Icons** re-render after an icon-size change and after the window's own `DpiChanged`. That event is routed:
  every new item raises it too, and reacting to those reloaded the items forever (a 1.4 GB spin in the prototype).
- **Delete fence** moves its items to the Inbox and never touches files. There is no confirmation, because nothing
  is lost.

**Consequences.** Every fence menu action is undoable by hand. Rubber-band selection moves to M3 with drag-drop.
A light veil over a bright wallpaper looks flatter than Windows' acrylic. The tint preference planned for v2
covers that.

## ADR-016 — Item actions through Windows' own engine; delete always recycles; safe-save memory
**Date:** 2026-10-02 · **Status:** Accepted

**Context.** M3a adds what people do to desktop items in a fence: the right-click menu, rename, delete, and feedback when
something cannot open. It also fixes the deferred M2b review finding I4: an editor's "safe save" moved the document to
the Inbox. The user chose (2026-10-02) that Shift+Del also goes to the Recycle Bin, keeping hard rule 1 absolute.

**Decision.**
- **Item menu.** Windows' classic `IContextMenu` for the selected items, from the desktop `IShellFolder`. That folder
  merges the user's and the Public Desktop, so a file name or `::{CLSID}` is a child of it. The menu is shown with
  `TrackPopupMenuEx`. While it is open, the fence window forwards menu messages to `IContextMenu2/3`, which "Send to",
  "Open with" and owner-drawn extension entries need. Shift adds extended verbs. Windows 11's compact menu is
  Explorer-private (spec §6).
- **Rename and Delete verbs** from that menu are taken over: Rename edits in place, and Delete always recycles, even
  with Shift held.
- **Delete** uses `IFileOperation` with `FOF_ALLOWUNDO | FOFX_RECYCLEONDELETE | FOFX_ADDUNDORECORD |
  FOF_WANTNUKEWARNING`. That gives Windows' own confirmation, progress and Undo. If an item cannot be recycled,
  Windows warns and asks before deleting it permanently. Special items are never recycled.
- **Rename:** an in-place box selects the name without the extension, like Explorer. The rename itself is done by
  `IFileOperation.RenameItem` (conflicts, Undo, hidden extensions). The watcher's rename event keeps the item in place.
- **Open failures** show Windows' own message (`ErrorDialog`, owned by the fence), e.g. "Missing Shortcut".
- **Safe-save memory (Core).** A fenced item that disappears is remembered for 5 s with its fence and index, whether
  it was deleted or renamed away (Word renames `doc` → `~WRL….tmp` before renaming its temp file onto `doc`). If the
  same ref appears again in that time, it goes back to that fence and position.
- **Z-order (corrects ADR-011 and ADR-015).** Ownership by Progman does not keep an *activated* fence below apps:
  after a fence was activated, windows restored from minimized came back under it (user report 2026-10-02: the Inbox
  drew over Firefox). Every fence now answers `WM_WINDOWPOSCHANGING` by moving any z-order change to `HWND_BOTTOM`.
  Verified: minimize all, activate a fence, restore all → no app window below any fence. Peek (M5) will lift this.
- **Accessibility:** items expose their label as the UI Automation name. Screen readers use it, and so does the
  scripted smoke.

**Consequences.**
- Delete is never permanent from NeoFences; permanently deleting means emptying the Recycle Bin in Windows.
- A real delete followed within 5 s by a new file with the same name lands where the old one was. That is
  harmless, and arguably expected.
- Menu, rename and delete need the fence active for keyboard (F2/Del), which inherits ADR-015's activation limit.

## ADR-017 — Drag-drop through OLE with the shell's objects; drops between fences never touch files
**Date:** 2026-10-03 · **Status:** Accepted

**Context.** M3b is the last part of spec §6's interaction table: drag-drop within, between, into and out of fences,
plus rubber-band selection. Hard rule 1 still applies: a drag must never lose or hide files by accident.

**Decision.**
- **Dragging out** uses `SHDoDragDrop` with the shell's own `IDataObject` for the items (from the desktop
  `IShellFolder`). Apps and Explorer get real files and decide copy, move or link, and Windows draws the drag image.
  A drag starts after the system drag distance on a pressed item. Pressing one of several selected items keeps the
  selection, so they drag together.
- **Dropping on a fence** goes through each fence's own `IDropTarget` (`RegisterDragDrop`, replacing the one WPF
  registers on every window):
  - **Desktop items** — a fence's own drag, or files that already sit on the user's or the Public Desktop: membership
    only (`FenceMembership.MoveItems`). The drop reports `DROPEFFECT_NONE`, so the drag source can never delete
    anything after a "move".
  - **Hovering the middle of a container item** (a folder or zip: `SFGAO_FOLDER | SFGAO_DROPTARGET`; middle half of the
    cell, top three quarters, `DropZones`) forwards to that item's drop target, as on the desktop; its edges and label
    reorder. A caret shows the insert position and the container is highlighted when it takes the drop.
  - **The Recycle Bin item** is never forwarded: its own drop target deletes permanently with Shift held. NeoFences
    shows a move cursor and recycles through `ShellFileOps` (always the Recycle Bin, hard rule 1; M3b review C1). Files that also call themselves drop targets (programs, text files) do
    not: dropping on them reorders instead.
  - **Anything else** (files from another folder) forwards to the Desktop folder's own drop target. Windows then
    copies or moves, with its progress, conflict dialog and Undo. Their Desktop paths are first registered as expected
    arrivals (`FenceMembership.ExpectArrivals`, the safe-save memory generalised to `RememberedPlacement` with its own
    expiry: 3 minutes for drops, as Windows creates each file when it starts copying it; 5 s for safe-saves), so they
    land in that fence at the drop position.
  - `IDropTargetHelper` keeps Windows' drag image visible over fences.
- **Drop index:** the position in the target fence's list as shown during the drag, still including the dragged
  items, i.e. "insert before the item shown there". Reading order is rows, then left to right; the right half of an
  item counts as after it. This resolves the M1 deferred "MoveItem index semantics".
- **Rubber band:** pressing on empty space and dragging selects every item the band touches (Ctrl adds).

**Consequences.**
- A dropped file that Windows renames on a name conflict ("x - Copy") lands in the Inbox, as do links, files restored
  with a name map and virtual items without a file list (browser images, mail attachments).
- Ctrl/Alt-dragging fence items onto the bare desktop makes Windows create a copy/shortcut there (it goes to the Inbox).
- Dropping onto a program to open the file with it is not supported inside fences.
- Drops in from Explorer could not be scripted: Explorer ignores synthetic drag gestures. That path is checked by
  hand (checklist K4).

## ADR-018 — Portals: live folder view, browse inside, newest first; sorting
**Date:** 2026-10-03 · **Status:** Accepted

**Context.** M4 adds Portal fences: fences that show a folder live, the second fence kind in the spec (§5/§6). The user
chose on 2026-10-03:
- double-clicking a subfolder browses inside the Portal (Back button), and Ctrl+double-click opens Explorer;
- new Portals sort newest first.

**Decision.**
- **Model.** A Portal is a fence with `FenceSource.Portal(path)` and no item list. `FenceMembership.CreatePortal` sets
  `Sort = Date`. Reconcile and all membership code ignore Portals (as since M1).
- **Listing.** `FolderItems.TryList` lists visible entries (not Hidden or System, like Explorer), or null when the folder
  cannot be read. In that case the Portal shows "not available" instead of items.
- **Sorting.** `ItemSorting.Order` (Core) sorts by Name or Type with folders first and natural numbers (setup2 before
  setup10). Date is newest first with folders mixed in, so the latest file is on top. The type key is the file
  extension (ponytail: the shell's type name would cost a call per item).
  - Portals keep their sort live; it is checked in "Sort by" and saved.
  - On a desktop fence, "Sort by" is a one-time reorder: `FenceEdits.SetItemOrder`, which must keep exactly the same
    items. Dragging still works afterwards.
- **No permanent deletes, ever (user decision 2026-10-03).** Items on drives without a Recycle Bin (USB sticks and
  cards, network shares, optical and RAM drives) are not deleted: the fence explains that deleting there would be
  permanent and must be done in Explorer. This keeps ADR-016's "never deletes permanently" true for Portals.
- **Live updates.** One `FolderWatcher` per Portal watches the shown folder, plus its parent for the folder's own name.
  A watcher follows its directory when that is renamed and reports nothing, so without the parent watch a vanished
  folder went unnoticed. Listing and watcher setup run in the background with a generation check (a network folder
  can block). Bursts are folded into at most one re-list per 250 ms, even while a file keeps being written. The watchers
  are re-armed on each re-list, and an unreadable or unwatched folder is retried every 7 s (no overlapping listings), so
  a Portal comes back with its drive. Portals never re-list because of desktop changes.
- **Browsing** (`PortalState`) is runtime only: a restart shows the Portal's own folder. Back stops at that folder.
  The title shows a breadcrumb ("Downloads › Mods").
- **Item actions.** The item menu, rename, delete, open and drag work on Portal items through each item's parent shell
  folder. `DesktopNamespace` now handles any folder; one call's items must share a parent.
- **Drops on a Portal** go to the shown folder's own drop target: real move/copy, Windows' dialogs and Undo, as in the
  spec's table. Items already in that folder: nothing happens. Portal items dropped on a desktop fence are files from
  elsewhere: Windows moves or copies them to the Desktop and they land in that fence (ADR-017's arrivals).
- **Creating a Portal:** fence menu → "New Portal fence…" → Windows' folder dialog (`IFileOpenDialog`, folders only,
  file-system paths only).
- **Deleting a Portal fence** never touches the folder.

**Consequences.**
- Very large folders are re-listed in full (in the background) on every change, and the item list is rebuilt each
  time; virtualization and incremental updates are left for later if needed.
- A Portal on a USB stick keeps the drive "in use" for Safely Remove while it is shown.
- Libraries and virtual folders (This PC, phones) cannot be Portals.

## ADR-019 — Start with Windows, for power-loss recovery
**Date:** 2026-10-03 · **Status:** Accepted · **Brings forward:** part of M7 (packaging)

**Context.** This PC loses power often. After an outage on 2026-10-03, Explorer's `HideIcons=1` (set by Takeover via
`FWF_NOICONS`) was still set after the reboot, but NeoFences did not start, so the desktop showed neither icons nor
fences. No NeoFences code can run during a power loss, and the watchdog dies with the machine. This also settles the
C8 question ("does the hidden state persist?"): it does. The user chose on 2026-10-03 to start NeoFences with Windows.

**Decision.**
- NeoFences writes the per-user sign-in entry `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\NeoFences` =
  the quoted path of the running exe. It does this on every start (so a moved exe is followed) and removes the entry
  when `Settings.StartWithWindows` (default on) is turned off. The toggle is in the fence menu: "Start with Windows".
- Builds running from the temp folder (scratch prototypes) never register: that copy is deleted later, and sign-in
  would then start nothing (`StartupPolicy`, tested).
- If the user disables NeoFences under Task Manager → Startup apps, Windows keeps that in StartupApproved, and the
  entry does not override it.

**Consequences.**
- At sign-in after any outage, NeoFences starts, shows the fences and re-applies Takeover.
- If the exe is removed without NeoFences running (no uninstaller yet), the icons stay hidden until "Show desktop
  icons" is turned on in Explorer. The M7 uninstall hook still covers that case.

## ADR-020 — Desktop gestures: quick-hide, draw a fence, Peek, roll-up
**Date:** 2026-10-03 · **Status:** Accepted · **Builds on:** ADR-007, ADR-011 (S2), ADR-019

**Context.** M5 brings Fences' desktop gestures (spec §4.5–4.6). Choices the user made on 2026-10-03:
- Quick-hide hides the fences **and** the native desktop icons.
- A rolled-up fence opens while the pointer rests on it (hover).

**Decision.**
- **One hook.** One `WH_MOUSE_LL` runs on its own thread with its own message loop (`DesktopMouseHook`).
  - It acts only when the root window under the pointer is Progman or WorkerW.
  - `WindowFromPoint` runs on button events only; moves arrive at up to 1000 Hz.
  - The callback never throws.
  - Every desktop right-press is swallowed. If the press did not become a drag, a right-click marked "NFNC" is replayed from the hook thread's loop (S2), so Explorer's desktop menu still shows.
- **Quick-hide.** A double-click on empty desktop hides every fence. With Takeover off it also hides the native icons, through the same marked path as Takeover, so the watchdog, a crash or a session end bring them back. The next double-click shows them again.
  - The state is not saved.
  - A double-click on a visible native icon opens it and does not toggle. This is a UI Automation hit test on the UI thread, never inside the hook.
  - Turning Takeover on or off, Peek and drawing a fence all end quick-hide first.
- **Draw a fence.** A right-drag of 8 px or more shows a topmost, click-through rectangle that follows the pointer (polled every 15 ms).
  - On release a fence titled "New fence" is created at that rect. The layout clamps it to the minimum size.
  - Its title rename starts right away.
  - If the drag's right-up is lost (Win+L, a UAC prompt), the next left or right click ends the drawing (`RightDragCancelled`). The button state cannot tell: the swallowed right-press never reaches Windows' key state.
- **Peek.** `RegisterHotKey` (default `Ctrl+Alt+Space`) is sent to the system message window.
  - Every fence is marked as peeking first, then raised `HWND_TOPMOST`. All fences are owned by Progman, so raising one restacks its siblings, and a sibling still keeping itself at the bottom drops back (found in the smoke).
  - Peek ends on the same hotkey, on Esc (a hotkey registered only while Peek is on), on a click outside NeoFences' windows (seen by the hook), when an item is opened from a fence, or when Explorer restarts. A fence created during Peek joins it.
  - "Outside" is checked by process id: a title lookup would send `WM_GETTEXT` to the UI thread from inside the hook, and the fence menus are NeoFences windows too.
  - If the hotkey is invalid or another app already owns it, the problem is logged and Peek is off.
- **Roll-up.** Double-clicking the title rolls the fence up to its title bar; double-clicking again unrolls it. The title is a caption (`WM_NCLBUTTONDBLCLK`) normally, and client area when the fence is locked.
  - `Fence.RolledUp` is saved. The stored rect keeps the full height, so a move while rolled up does not shrink the fence.
  - Resizing is off while rolled up.
  - Hover: after about 300 ms on the fence it opens; after about 500 ms away it closes. This uses 100 ms cursor polling, which also opens it during a file drag.
  - It never closes during a move, a menu or a rename.
- **Smart placement.** A new fence goes to the first free spot, reading top-left to bottom-right, that keeps the 8 px gap from other fences (`LayoutEngine.FreeSpot`). Only a full monitor falls back to the cascade.

**Consequences.**
- With Takeover off, a right-drag that starts on a visible native icon draws a fence instead of starting the shell's icon right-drag. A right-click on an icon still opens its menu (replayed).
- Test scripts must move the pointer with `SendInput`/`mouse_event`: `SetCursorPos` never reaches `WH_MOUSE_LL`.
- Not in M5:
  - the roll-up and quick-hide animations (spec §6, M6 polish);
  - the tray entries (M6);
  - `RollupExpand = Click` (stored, not offered yet);
  - removing the hook in game mode (M6).

## ADR-021 — Game mode, tray, Pause, and mouse-hook hardening (M6a)
**Date:** 2026-10-03 · **Status:** Accepted · **Builds on:** ADR-007, ADR-011, ADR-020 · **Refines:** spec §4.7, §6

**Context.** M6 was split by the user on 2026-10-03:
- **M6a** (this ADR): game mode, the tray, Pause, and hook hardening;
- **M6b**: the settings window and animations.

The user chose: game mode = **go idle** (fences stay, nothing is hidden), and a **left-click on the tray icon opens the tray menu**. M0 found that the notification state was the only signal that caught a real game, and that it lags behind the foreground change (findings 4–5).

**Decision.**
- **Game mode.** It is on when the notification state (`SHQueryUserNotificationState`) is `QUNS_BUSY` or `QUNS_RUNNING_D3D_FULL_SCREEN`, **and** the foreground window is an app. Not an app: the desktop (Progman/WorkerW), the taskbars, NeoFences itself, or no window (`GameModePolicy`, tested).
  - The lock screen reports `QUNS_NOT_PRESENT` and never counts.
  - It is checked on every foreground change (an out-of-context WinEvent hook, `ForegroundWatcher`), and again 1, 2.5 and 5 s later. The state flips 1–3.5 s after a full-screen window activates, with no further event.
  - It is also polled every 5 s, plus a fresh check before Peek: a game that goes full screen later (a launcher, Alt+Enter) raises no event (final review I1). This **supersedes the spec's "no polling"** for game mode: one cheap shell query.
  - The spec's "window covers its monitor" check is dropped: it never fired in M0, and with taskbar auto-hide a maximized app would also match it.
  - `Settings.GameMode` (default on) turns it off.
- **While gaming (idle):**
  - the mouse hook is removed (zero added input latency);
  - Peek is ignored: fences must not rise over the game, and the hook that ends Peek is gone;
  - Desktop watcher changes are queued and applied in order afterwards (a lost-events error becomes one reconcile afterwards);
  - Portals stop re-listing; a Portal that changed re-lists once afterwards.

  Fences and icons stay as they are.
- **Tray.** A `Shell_NotifyIcon` icon (NOTIFYICON_VERSION_4) with the new app icon (`NeoFences.ico`, also the exe icon). It is added again on TaskbarCreated.
  - Any click opens Windows' own popup menu (`TrackPopupMenuEx`, with the foreground + `WM_NULL` rule). The menu: New fence · Quick-hide · Peek (with its hotkey) · Pause NeoFences · Exit. Settings arrives in M6b.
  - The icon loads with `LoadImage` (user32). `LoadIconMetric` lives in comctl32 v6, which a WPF app does not load: it crashed the prototype.
  - A tray failure is logged and loses only the tray (hard rule 7). A busy Explorer can time out the add: the icon is retried every 2 s, and the click format (version 4) is set whenever the add or the modify succeeded.
- **Pause** (tray, not saved): fences hidden, native icons shown even with Takeover on, the mouse hook removed, Peek ignored. Resume restores everything; Pause also ends quick-hide.
- **One rule for the modes.** `RunState(Takeover, QuickHidden, Paused, GameMode)` decides together whether fences show, whether icons hide, whether the hook is wanted, and whether shell work waits (Core, tested). `FenceHost` derives every icon and hook decision from it.
- **Hook hardening.**
  - The hook thread runs at Highest priority, so a CPU-heavy game does not push the callback past `LowLevelHooksTimeout`, after which Windows drops the hook silently.
  - The hook is re-installed after an Explorer restart and on session unlock (`WM_WTSSESSION_CHANGE`).
  - The S2 right-click replay is skipped when the pointer has left the desktop (M5 review M7).
- **Icons on exit.** Exit, session end and the crash handler also show the icons whenever the takeover-active marker is set, so a show that failed earlier is retried (M5 review M6).

**Consequences.**
- Full-screen video or a presentation also counts as game mode (`QUNS_BUSY`). That is harmless: NeoFences only idles.
- Windows 11 may keep a new tray icon in the overflow until the user pins it; the menu is the same.
- Session lock/unlock and an Explorer restart with the tray are hand checks (O7, O8).

## ADR-022 — Settings window, roll-up click mode, animations (M6b)
**Date:** 2026-10-03 · **Status:** Accepted · **Builds on:** ADR-015, ADR-020, ADR-021 · **Refines:** spec §6

**Context.** M6b completes spec §6: a settings window and animations. The user chose on 2026-10-03:
- roll-up offers **hover (default) and click**;
- **no Appearance section in v1**: tint and opacity preferences come with v2 themes. The user said earlier that the current tint is fine.

**Decision.**
- **Settings window** (`SettingsWindow`). It uses WPF's built-in Fluent theme, set as `ThemeMode="System"` on this window only, so it follows Windows light/dark and the accent colour while the fences keep their own look. No new dependency.
  - It opens from the tray (Settings…) and from the fence menu (Settings…). There is one window; a second request brings it to the front.
  - Sections: **General** (Start with Windows, Hide desktop icons, Peek hotkey), **Fences** (rolled-up fences open on hover or click), **Game mode** (on/off, plus whether it is idle right now), **About and logs** (version, data folder, open logs or data folder).
  - Changes apply at once and are saved; there is no OK button. Checkboxes react to Checked/Unchecked, not Click, so UI Automation and screen readers work too.
- **Peek hotkey recorder.** Click the box and press a combination: modifiers alone only preview, Esc leaves.
  - The new hotkey is kept only if Windows registers it; otherwise the old one stays and the window says why (invalid, or used by another app).
  - Hotkey text uses WPF key names: a digit is the top-row key (`Ctrl+Alt+1` → `D1`), and other numbers are rejected (M5 review M4).
- **Recorder safety** (final review I1). A global hotkey swallows its keys in every app, so the recorder only saves
  combinations that need Alt or Win, or use an F-key. It refuses Windows' own combinations (Alt+F4, Alt+Tab, Alt+Space…)
  and Ctrl+Alt + letter/digit (AltGr characters on many layouts), with a message (`Hotkey.IsSafeToRecord`, tested).
  Tab, Shift+Tab and Alt+F4 pass through the box. config.json stays lenient.
- **Peek hotkey lifetime.** It is registered only while `RunState.PeekHotkeyWanted` (not paused, not gaming), so Ctrl+Alt+Space goes back to Windows and the game (M6a review M5).
- **Roll-up expansion** (`RollUpExpansion`, Core, tested). Hover opens after ~300 ms of resting. Click opens on a single click on the title: for an unlocked fence that is `WM_NCLBUTTONDOWN` on the caption, handled so it does not start a move; for a locked fence it is a client click. Both close ~500 ms after the pointer leaves.
- **Animations** (spec §6):
  - roll-up, unroll and hover/click opening move the bottom edge over 200 ms with ease-out;
  - quick-hide fades over 150 ms;
  - both are off when Windows' "Animation effects" are off (`SystemParameters.ClientAreaAnimation`) and in game mode (spec §4.7);
  - a move during a height animation jumps it to the end;
  - Pause and Peek do not animate.

**Consequences.**
- In click mode, the first press on a closed rolled-up fence opens it instead of dragging it; the next press drags as usual.
- The version shown in About is the App's `<Version>` (0.6.0); packaging sets the release version (M7).
- Appearance settings wait for v2 (FEATURES: themes).
- `Window.ThemeMode` is still marked experimental (WPF0001) in .NET 10. Setting it in XAML avoids the analyzer, but the API may
  change in a later .NET; the fallback is the classic theme (the window still works).
- A move that starts during a roll-up animation keeps the finished height. Windows' move loop re-proposes the rect it
  captured, so `WM_MOVING` pins the height (final review M2).

## ADR-023 — Installer: Velopack, self-contained, uninstall restores icons; sign-in follows the installed copy
**Date:** 2026-10-03 · **Status:** Accepted · **Builds on:** ADR-001 (Velopack named for M7), ADR-005, ADR-019

**Context.** M7 ships v1.0 as an installer. The user chose on 2026-10-03:
- **Installer only for now.** No update source; GitHub auto-update can come later.
- **Uninstall keeps the data.** The data is `%LOCALAPPDATA%\NeoFences`: layout, backups, logs.

Hard rule 2 requires icons back when NeoFences is gone for good: the watchdog and session handling never cover "no
longer installed" (ROADMAP M7).

**Decision.**
- **New NuGet dependency: Velopack 1.2.161** (App), plus the `vpk` 1.2.161 CLI as a repo-local dotnet tool
  (`dotnet-tools.json`). This is the ADR that hard rule 6 requires. Why Velopack:
  - per-user install with no admin;
  - delta updates when an update source is added;
  - install/update/uninstall hooks in our own exe;
  - MIT licensed;
  - actively maintained.
- **Package.** `build\pack.ps1 -Version x.y.z` runs the tests, publishes self-contained win-x64 (no separate .NET
  runtime, nothing a runtime update can break: robustness first), and packs with Velopack into
  `artifacts\releases\NeoFences.App-win-Setup.exe` (about 72 MB), the full package and RELEASES. `artifacts\` is
  git-ignored. Unsigned for now: Windows SmartScreen may warn once.
- **Install location.** Velopack installs into `%LOCALAPPDATA%\<packId>` and deletes that folder on uninstall, so the
  packId is `NeoFences.App`, never `NeoFences`, which is the data folder. Program in `%LOCALAPPDATA%\NeoFences.App`,
  data in `%LOCALAPPDATA%\NeoFences`, as before.
- **Premise (live test, final review):** Velopack closes the app's own processes, main and watchdog, *before* any hook,
  on uninstall and when a newer Setup.exe runs over an install. Setup runs no hook of ours at all.
- **Uninstall hook** (`InstallHooks`, first line of `Main`, before any WPF; 30 s limit):
  - ask any other NeoFences to exit (≤ 5 s; usually nothing is left);
  - then **always** show the desktop icons, whatever ran before. `DesktopIcons.ShowWithRetry` retries for up to 20 s
    while Explorer is busy, then sets Explorer's persisted `HideIcons = 0` for its next start. This is the last code
    that can restore them, so it keeps hard rule 2;
  - then remove the sign-in entry if it starts this copy;
  - keep the data.

  The hook is logged (`logs\install-*.log`); a logging failure or any error never blocks the icon restore or Velopack.
- **No update hook.** v1.0 has no update source, and Setup-over-install never calls one (final review I1).
- **Sign-in entry** (refines ADR-019; `StartupPolicy.SignInTarget`, tested). The entry starts:
  - the installed copy when one exists (`<root>\current\NeoFences.exe` next to `<root>\Update.exe`; the usual root is
    `%LOCALAPPDATA%\NeoFences.App`), whichever build writes it;
  - otherwise the running build, unless it runs from the temp folder.

  So development and smoke runs, and toggling the setting in a dev build, never take sign-in away from the installed
  NeoFences (final review I3). Power-outage recovery keeps working before and after installing.
- **Version.** The App's `<Version>` is 1.0.0. `pack.ps1 -Version` sets the released one, which Settings → About shows.

**Consequences.**
- Upgrading means running a newer Setup.exe (or, later, an update source). Velopack force-closes NeoFences (at most
  the last 500 ms of unsaved layout changes are lost) and starts the new copy, which re-applies Takeover. The data stays
  put, so the fences survive. Exiting from the tray first avoids even that.
- Uninstalling by deleting the folder by hand skips the hook: the icons are not restored. Recovery: Explorer → View →
  Show desktop icons (SETUP.md). A development build would take the sign-in entry back.
- Code signing and an update source (GitHub Releases) are left for after v1.0.

## ADR-024 — v1.1 reliability batch (M8a): reconcile recovery, unique monitors, rounded corners by Windows
**Date:** 2026-10-03 · **Status:** Accepted · **Refines:** ADR-006, ADR-011 (fence look), ADR-016, ADR-021

**Context.** After v1.0 the user asked for all open carry-overs, in four batches with reliability first (M8a). During
M8a the user also reported fence corners "sticking out" of the rounded border (screenshot, 2026-10-03).

**Decision.**
- **Rounded corners by Windows 11.** `DWMWA_WINDOW_CORNER_PREFERENCE = ROUND`, set once per fence. A window region does
  *not* clip the accent blur: Windows draws it over the whole rectangle, which left a square of blur outside each
  rounded corner. 8× zoom captures confirmed this, and that the region itself was rounded.
  - `WindowChrome.CornerRadius` now matches the 8-DIP border, so WPF's own region (which it re-applies on every
    resize) stays rounded for hit-testing. A radius of 0 had kept resetting it to a square.
  - NeoFences' own `SetWindowRgn` code is removed.
  - On Windows 10 the DWM call is refused: square blur corners under the rounded border, as before.
- **Reconcile recovers instead of dropping** (`FenceMembership.Reconcile`, tested):
  - An item that comes back during a reconcile with an unexpired safe-save or drop memory returns to its fence and
    position. This covers a safe-save whose watcher events were lost.
  - A fenced item whose file name now exists **exactly once**, unfenced, under another folder takes that path in place.
    A moved Desktop (OneDrive Known Folder Move) keeps every arrangement. An ambiguous name is never guessed.
- **Memory clock.** Safe-save and drop memory use wall time at start plus a monotonic stopwatch, so a clock change
  cannot expire or extend a memory.
- **Watcher.**
  - It starts before the startup reconcile.
  - A rename split across buffers (an empty name) or an item that cannot be checked right now (sharing violation)
    asks for a reconcile, instead of being treated as gone. The check uses `File.GetAttributes`: "not found" means
    gone; any other error means reconcile (`Exists` swallowed every error as "gone", final review).
  - Events after `Dispose` are dropped.
- **Layouts and monitors.**
  - Duplicate monitor device ids (cloned outputs) become `id#2`, `id#3`…; a duplicate key had stopped every layout.
  - A fence missing from a known setup's layout (made on another setup since) keeps its last place, scaled.
- **Saves.** A daily-backup failure no longer fails `Save`; it is reported (`ConfigStore.LastBackupFailure`) and
  logged on its own.
- **Run state.**
  - Quick-hide decides icons through `RunState`, and leaves alone icons the user hid in Explorer (Takeover off):
    `RunState.IconsHiddenByUser`, set when quick-hide begins with the icons hidden and no takeover-active marker, so
    every path (quick-hide end, Explorer restart, Pause, exit, sign-out) leaves them hidden. Icons NeoFences hid
    itself (marker set) are still shown again (final review I1).
  - Pause and layout show or hide fences at once, ending any running quick-hide fade.
  - The tray icon is removed on session end too.
  - A failed unlock-notice registration is logged.
  - More than 500 Desktop changes during a game become one reconcile.
- **Installer and build.**
  - The uninstall stop-wait counts only this session's processes.
  - `InstallRoot` reuses `StartupPolicy.IsInstalledExe`.
  - `pack.ps1` validates the SemVer first, refuses an already-packed version instead of hanging on vpk's prompt, and
    restores the caller's folder.

**Consequences.**
- On Windows 10 the blur corners stay square.
- A same-name file that appears unfenced elsewhere while a fenced one disappears in the same reconcile is treated as a
  move. The rule keeps the fence and position, which is what a user expects from a moved Desktop.

## ADR-025 — Fence UI batch (M8b): icon-only labels, shortcut arrows, NeoFences' own title double-click
**Date:** 2026-10-03 · **Status:** Accepted · **Refines:** ADR-011 (fence look), ADR-015 (keyboard focus), ADR-020, ADR-022

**Context.** The user asked for fences that show only icons, with the name on hover (2026-10-03). They chose
"per fence + a Settings default" and "the name pops under the icon". Shortcut arrows were an open decision since M2b;
the user chose a Settings switch, off by default. The rest of the M8b batch is the open fence-UI carry-overs.

**Decision.**
- **Labels per fence.** `Fence.Labels` is `Always` (default; v1.0 configs read as `Always`) or `OnHover`.
  - The fence menu has **Labels ► Always / On hover (icons only)**.
  - `Settings.DefaultLabels` is used by new fences and Portals; Settings → "Apply to all fences" sets every fence and
    the default (`FenceEdits.SetLabelsEverywhere`).
  - Icons-only cells are `icon size + 12` DIPs wide, so more icons fit in a row.
- **The pop-under name is drawn inside the fence window** (a `Canvas` overlay above the list), not as a tooltip or a
  `Popup`. A popup is a separate top-level window: it would float over other apps and take its own z-order, while a
  fence stays at the bottom (ADR-011).
  - It shows the hovered item, or else the single selected one (so keyboard arrows show names too). It sits under the
    cell, over the neighbours; it is clamped to the fence's sides, and goes above the icon when there is no room below.
  - Item tooltips are off in icons-only mode (the name is already shown); in `Always` mode they still show long names.
- **Shortcut arrows** (`Settings.ShowShortcutArrows`, default off). `IShellItemImageFactory` returns icons without the
  arrow, so NeoFences draws its own overlay (white tile + blue arrow, scaled with the icon size) on `.lnk`, `.url` and
  `.pif` items.
- **Title double-click is recognised by NeoFences.** When the first click of a double-click makes the fence the
  foreground window, Windows does not send `WM_NCLBUTTONDBLCLK`, so the first double-click after another app was in
  front did nothing (M5 finding). `WM_NCLBUTTONDOWN` on the caption now compares with the previous caption press using
  the user's double-click time and size (`GetDoubleClickTime`, `SM_CXDOUBLECLK`/`SM_CYDOUBLECLK`).
- **Keyboard focus after a click from another app stays a known limit (ADR-015 stands).** An experiment calling
  `Activate()` on every press did not make the fence the foreground window (live probe, 2026-10-03): fences sit on the
  desktop's input queue. It was removed.
- **Settings and the Peek hotkey.**
  - Hotkeys are shown as key caps (`Hotkey.DisplayText`: "Ctrl+Shift+=", "Num 5"); config keeps the canonical text.
  - The Peek hotkey is released while the recorder box has the keyboard, so pressing the current combination records
    it instead of peeking; it is registered again when the box loses focus or Settings closes.
  - Settings shows when the hotkey is not registered (another app owns it); entering the same combination retries.
  - The status line is an assertive live region (Narrator announces refusals); descriptions are each control's
    `HelpText`.
- **Smaller fixes.** `SetItems` diffs in place (keeps scroll, selection and the rename box; tolerates duplicate refs);
  an icon-size change re-requests icons on the existing views and is skipped when unchanged; the icon-size menu is
  built from `ConfigNormalizer.IconSizes`; two icon-loader threads (one slow thumbnail no longer holds every icon);
  bitmaps of any bit depth are accepted and `IShellItem`s released at once; a resize snap never goes below the
  minimum size; a rename cut at 64 characters never splits an emoji, and the title box stops at 64; a double-click in
  the rename box selects a word instead of opening the file; an empty-space press focuses the list; Ctrl+press on a
  selected item unselects it on release (unless it was dragged); a rolled-up fence moved to a monitor with another DPI
  rescales its stored full height; a native icon's rename box counts as "on an icon" (no quick-hide while editing);
  a refused DWM corner preference (Windows 10) is logged once.

**Consequences.**
- The pop-under name can be cut at the fence's bottom edge for a one-row fence with very little height; it then goes
  above the icon instead.
- Measuring the label shadow cost with ~200 items is deferred until a fence that big shows up (YAGNI).

## ADR-026 — Shell actions and drag-drop details (M8c): shell worker, special-icon notices, drop rows, start after Setup
**Date:** 2026-10-03 · **Status:** Accepted · **Refines:** ADR-016 (item actions), ADR-017 (drag-drop), ADR-023 (installer), ADR-024

**Context.** M8c is the third batch of carry-overs: the item menu, recycling and opening (M3a review), drag-drop
details (M3b review), special desktop icons (M2b), the M8a review's reconcile/watcher/monitor minors and the M8b
review's minors. During the v1.1.0 upgrade (2026-10-03), Setup closed NeoFences but did not start 1.1.0 again.

**Decision.**
- **One STA shell worker** (`ShellWorker`) runs open, recycle and rename in order, off the UI thread: a long recycle
  or a conflict dialog no longer freezes the fences, and shell handlers get the apartment they expect. Open used to run
  on the thread pool (MTA). The "no Recycle Bin on this drive" message is shown back on the UI thread.
- **Item menu.**
  - The right-clicked item comes first, so menu → Rename renames *it* when several are selected (user choice
    2026-10-03: the right-clicked item only).
  - Shift+F10 opens the normal menu; only Shift + right-click asks for extended verbs.
  - A failed `QueryContextMenu` HRESULT (a broken extension) is logged instead of showing an empty menu.
  - `WM_NULL` is posted after `TrackPopupMenuEx` (the documented dance), so an outside click closes the menu.
  - Note: on this PC the menu also lists PowerToys' "Rename with PowerRename"; NeoFences takes only the shell's own
    `rename` and `delete` verbs (checked live: their verbs are still `rename` / `delete`).
- **Recycling several items** skips items that no longer exist instead of cancelling all; they are logged.
- **Same name on the user's and the Public Desktop.** The desktop folder parses a name to the user's copy, so a Public
  Desktop item whose name the user's Desktop also has is reached through its own folder (menu, drag, drop target).
- **Special icons live** (`SpecialIconNotifications`):
  - "Desktop icon settings" changes are watched on the registry key (`RegNotifyChangeKeyValue`, thread agnostic); a
    changed set of special icons reconciles.
  - The Recycle Bin's own changes (all events on its folder, shell and interrupt level) and icon-image updates reload
    the special icons, so its full/empty icon follows. Bursts are debounced (400 ms).
- **Drag-drop.**
  - A virtual source (zip contents, phones) is not asked for CF_HDROP on enter: that made Windows extract every file
    before the drop. Its arriving names come from the file descriptors.
  - Desktop items in a mixed drag join the fence; the rest arrive through Windows.
  - Drops that make shortcuts expect "name - Shortcut.lnk" too (English Windows only; other languages go to the Inbox).
  - Container checks are cached per drag; drop rows reach down to their tallest item (`DropZones.InsertIndex`, tested) (amended by ADR-035).
- **Watcher and reconcile.**
  - A stopped watcher (re-arm) is told apart from one unreadable event (reconcile only). Bursts become one recovery;
    a watcher that fails again within 10 s of a re-arm waits longer each time (up to a minute).
  - Reconcile reports the memories it used (`ReconcileReport.UsedMemories`); the host drops them, and several
    memories for one fence are inserted by index (tested).
- **Monitors.** Duplicate device ids are numbered in GDI-name order, not enumeration order (tested).
- **Start after Setup.** A new instance that finds the single-instance mutex taken waits up to 5 s for the old one to
  exit (Setup closing the old version while starting the new), and logs when it gives up. Logging is shared between
  processes for that line.
- **M8b review minors.** NeoFences' own title double-click uses the click's message time and point, and a DBLCLK right
  after a recognised double-click (a third click) does not toggle again; invalid `LabelMode` values in a hand-edited
  config are repaired; every Settings description is its control's help text and row titles wrap; the rename box in an
  icons-only fence gets a labelled cell's width; dead code removed. Icons are first requested once the window is
  placed (one load at the right DPI).

**Consequences.**
- Whether Velopack's Setup itself starts the app after a silent upgrade is still to be confirmed at the next upgrade
  (the log now says if a second instance gave up). The same-version Setup only offers Repair.
- Key caps still use US key names (M8b review minor, deferred).

## ADR-027 — Portal details (M8d): Safely Remove, watcher backoff, Enter on several items
**Date:** 2026-10-03 · **Status:** Accepted · **Refines:** ADR-018 (Portals), ADR-026

**Context.** The fourth and last carry-over batch: Portal details from the M4 review, plus the cheap minors from the
M8c review. The user lent a USB stick (LOCO_DUCK) for the removal test.

**Decision.**
- **Safely Remove / Eject works while a Portal shows a removable drive.** Any open handle vetoes a removal, and a
  Portal holds two (its folder watcher and its parent's). Each Portal on a local drive registers a folder handle for
  removal notices (`DeviceRemovalNotice`, `RegisterDeviceNotification` with `DBT_DEVTYP_HANDLE`, to the message
  window). On `DBT_DEVICEQUERYREMOVE` the Portal closes its watchers and the handle, shows "not available", and the
  existing 7 s retry brings it back when the drive returns (also when another program refused the removal: the
  notice was already given up, so Windows' "removal failed" message no longer reaches the Portal).
  Live: with v1.1.0 the eject was vetoed (the volume in use); with M8d it ejected and NeoFences kept running.
- **Portal watcher errors back off** like the desktop watcher (`WatcherBackoff`, M8c): a watcher that fails again soon
  after each re-arm re-lists after 1 s, 2 s, 4 s… up to a minute, instead of every 250 ms; a stop during a game
  re-lists when the game ends, and a watcher that fails while it arms is caught too (final review).
- **Enter with several items selected opens each one** (folders in Explorer); a Portal no longer browses into the last
  of several folders. One item: as before (a Portal browses into a folder).
- **Already covered (no new code):** Portal updates keep selection, scroll and the rename box (the M8b in-place
  `SetItems` diff; checked live on the USB stick); container checks are cached per drag (M8c); a Portal of the Desktop
  folder uses the merged desktop as parent, which acts on the same files except a Public item whose name the user's
  Desktop also has, which M8c routes through its own folder.
- **M8c review minors folded in:** a virtual source (zip contents) is not asked for CF_HDROP at drop time either;
  shell operations whose fence was deleted meanwhile run without an owner window; a recognised title double-click is
  used once; special-icon reloads wait for the end of a game.

**Consequences.**
- The removal notice is registered on every refresh of a local Portal (an open/close of one handle); network Portals
  have none (a network drive is not "removed").
- Still deferred: mixed drops handing desktop items to Windows, a mixed selection with a shadowed Public item, queued
  shell operations at shutdown, `--exit` during the single-instance wait, US key caps.

## ADR-028 — NeoFences starts itself after Setup (v1.1.2)
**Date:** 2026-10-03 · **Status:** Accepted · **Refines:** ADR-023 (installer), ADR-026 (single-instance wait)

**Context.** Upgrading 1.1.0 → 1.1.1 with `Setup.exe --silent` left NeoFences stopped: no fences, and the desktop icons
hidden until the next sign-in (hard rule 2). A verbose Setup log showed the order: Setup closes the app, runs the
installed exe's `--veloapp-install` hook, force-stops anything still running from the install folder, writes the
uninstall key and ends. It never starts the app. The 5 s single-instance wait (ADR-026) assumed a start that never came.

**Decision.** The install hook (`InstallHooks.OnAfterInstall`, Velopack's `OnAfterInstallFastCallback`):
1. shows the desktop icons if NeoFences had hidden them (its takeover-active marker; icons the user hid in Explorer
   stay hidden, as in M8a), so they can never stay hidden by NeoFences after Setup;
2. starts NeoFences ~4 s later through `cmd.exe /c ping … & start "" "%NEOFENCES_EXE%"` (both by full path; the exe path
   through the environment so cmd never parses it; working directory = the system folder): outside the install folder,
   so Setup's force-stop (~40 ms after the hook) does not end it. A second start (Setup,
   the user, or sign-in) waits for the first and exits (single instance).

Live (2026-10-03): 1.1.1 → 1.1.2-beta.1 with `--silent`: hook logged "showing desktop icons and starting NeoFences after
Setup", icons shown on attempt 1, NeoFences started by itself 4 s later.

**Consequences.**
- NeoFences itself now runs with the system folder as its working directory: an open install folder (inherited by
  everything opened from a fence) would block the next Setup's rename (final review).
- A fresh install also starts NeoFences this way; if Setup starts it too, one copy exits after its 5 s wait.
- Testing note: PowerShell's `Start-Process -Wait` on Setup now waits forever (it waits for the whole process tree,
  which includes the started NeoFences); wait for the Setup process alone instead.

## ADR-029 — Fence tabs as boxes over ordinary fences (M9, v1.2)
**Date:** 2026-10-03 · **Status:** Accepted · **Spec:** `superpowers/specs/2026-10-03-fence-tabs-design.md`

**Context.** The first v2 feature the user picked: Fences-6-style tabs to combine fences into one box (user choices:
combine existing fences by dragging a title onto another; drag a tab out to split it; a per-tab accent colour).

**Decision.**
- **Every tab stays an ordinary `Fence`** (items, source, sort, icon size, labels): membership, reconcile, Portals and
  their tests are untouched. A box is a host fence whose `Tabs` lists two or more ids (itself included); the host owns
  the window, the placements, roll-up and lock. `ActiveTab` and `TabColor` complete the model. All edits live in Core
  `FenceTabs` (Merge, Detach, Leave, Reorder, SetActive, SetColor, Repair), tested first; `FenceMembership.DeleteFence`
  leaves the box first, and the layout engine places boxes only.
- **One window per box.** `FenceWindow.FenceId` is the shown tab (items, icon size, labels, sort, rename, delete, drops
  act on it); `BoxId` is the host (placement, move, snap, roll-up, lock). `FenceHost.SyncBoxes` brings windows in line
  after any change and keeps a window when its box changes hands (the host left). Portal watchers exist per Portal fence,
  shown or not.
- **Gestures.** Header click switches; double-click renames; a header drag (NeoFences' own capture with a click-through
  ghost) reorders, merges into another title row or detaches at the drop point; a fence moved by its title (Windows'
  move loop) merges when released over another title row (the target lights up; a resize never merges). An item drag
  over a header shows that tab at once, so the drop lands there; the drop target re-reads the shown fence each step.
- **Old configs** load unchanged (`activeTab` and `tabColor` are written only when set; `tabs` is written as `[]` like
  `items`, which older builds ignore — though an older build saving the config drops the tab fields); the normalizer repairs
  unknown ids, double membership, nesting, a missing host, single-tab boxes, invalid active tabs and colours.

**Consequences.**
- Hovering a header during an item drag switches at once (the spec said "about 0.5 s"; an instant switch keeps the drop
  target simple and was smooth in the smoke).
- Tab headers are exposed to UI Automation by their text only (no tab/selected semantics yet).
- A Portal tab shows its folder title on its header; the browsing breadcrumb shows only for a single fence.
- A header drag shows the ghost and lights up the title row it would join; there is no insertion caret for a reorder
  (final review). Esc cancels it (read from the key state, so it works whichever window has the keyboard); a right-click
  cancels it over fences and apps, but over the bare desktop the mouse hook owns right-clicks (draw a fence).
- Dropping a header on its own box's body does nothing; dragging the host's header moves only that tab (final review).

## ADR-030 — Snapshots: one file each, restored against the live desktop (M10, v1.3)
**Date:** 2026-10-03 · **Status:** Accepted · **Spec:** `superpowers/specs/2026-10-03-snapshots-design.md`

**Context.** The user picked snapshots next, to undo a messy desktop (not to switch between setups): items newer than
a snapshot stay in their fence when it survives, else the Inbox; tray menu plus a Settings card; every restore first
saves "Before restore".

**Decision.**
- **One file per snapshot** in `%LOCALAPPDATA%\NeoFences\snapshots\` (Core `SnapshotStore`): the config's JSON format
  (`ConfigJson.SerializeSnapshot`), written to a temp file and swapped in; a damaged file is skipped and reported;
  `before-restore.json` is replaced on every restore. Deleting a snapshot sends the file to the Recycle Bin.
- **Restore is pure Core** (`Snapshots.Restore(current, snapshot, desktopNow)`): the snapshot is normalized first (a
  damaged one still gives one Inbox and sane tabs); its fences and places come back; icons still on the desktop return
  to their saved fence and position, deleted ones are dropped, newer ones stay in a surviving fence or go to the Inbox;
  Settings, other monitor setups and the current setup's fingerprint stay.
- **The App** refuses a restore when a Desktop folder cannot be listed, writes "Before restore" first (no restore
  without it), applies one config change, saves at once and rebuilds the windows (`SyncBoxes`).
- **UI.** Tray: "Take snapshot" (named by date and time, a tray notice confirms) and "Restore snapshot ▸" (the 10 newest,
  "Undo the last restore" for `before-restore.json`, "More in Settings…"). Settings: a Snapshots card with a list and
  Take / Restore / Rename (in place) / Delete / "Open snapshots folder". The tray needed submenus (`TrayMenuItem.Children`)
  and a notice (`TrayIcon.ShowBalloon`).

**Consequences.**
- The spec's tray label "Before restore" became "Undo the last restore" (what it does); Settings shows its real name.
- Snapshots are taken only on request; the daily config backups stay the automatic safety net.

## ADR-031 — Rules auto-sort: Core matching, Shell facts, filing new Inbox items (M11, v1.4)
**Date:** 2026-10-03 · **Status:** Accepted · **Spec:** `superpowers/specs/2026-10-03-rules-design.md`

**Context.** The user picked rules next: new Desktop items should land in the right fence without a click, and
existing items can be tidied once on request. All four kinds of condition, one ordered list in Settings, and a fence
menu shortcut. A probe on the user's desktop showed most of their games are shortcuts into `D:\GameLibrary`, outside
any launcher, and that the Epic launcher's own shortcut sits under `\Epic Games\`.

**Decision.**
- **Config:** `rules: [{ id, enabled, condition, fenceId }]` in `config.json`, in order; the first enabled rule whose
  condition matches and whose fence is an existing desktop fence (tabs included) wins. Snapshots do not hold rules.
- **Core `Rules`** (pure, tested): `Matches`, `LauncherOf`, `Match`, `File` (matched items move to the end of their
  rule's fence, membership only), `CountMoves`, `Describe` ("Images → Pictures"), `Repair` (a rule that cannot work is
  kept but disabled on load). Conditions: type (groups or custom extensions), game (a launcher by URL scheme, library
  folder or launcher arguments, or — user choice 2026-10-03 — **a folder of the user's own**), name (`*`/`?`, plain text
  = contains, the file name with its extension), age (last modified), size (files only).
- **Shell `ItemFactsReader`** reads name, type, size, date and a shortcut's target (`IShellLinkW` with `SLGP_RAWPATH`,
  or a `.url`'s `URL=` line) on the shell worker; an unreadable item keeps only its name and extension.
- **App:** items new to the Desktop (a watcher "created" event for an item no fence held, a download or temp file
  renamed to its final name, or a reconcile) that sit in the Inbox are filed 1.5 s after the last arrival, once their
  facts are read (game mode defers them with the other desktop work); "Apply rules now" writes "Before restore" first, files every
  desktop item and reports "N items moved". Settings → Rules card with an editor; fence menu "Rules for this fence…".

**Consequences.**
- The Epic launcher shortcut is not a game; launcher executables alone never match.
- Rules also run while NeoFences is paused (membership only; nothing shows until resume) — simpler than the spec's
  "waits", with no visible difference.
- Filing waits for a quiet moment: a shortcut still being written read as having no target (branch smoke). An
  attribute change on an Inbox item and a rename the user makes never re-run rules. A hand-edited rule with unknown
  names or no id is disabled, never a corrupt config.

## ADR-032 — Game Library: generated shortcuts shown like a Portal (M12, v1.5)
**Date:** 2026-10-04 · **Status:** Accepted · **Spec:** `superpowers/specs/2026-10-03-game-library-design.md`

**Context.** The user wants every installed game in one place (launchers, Xbox, `D:\GameLibrary`, Desktop game
shortcuts), cover art where launchers keep it on disk, one A–Z fence. A read-only probe found Steam, Epic, GOG and Xbox
games and posters in Steam's library cache.

**Decision.**
- **A new fence kind** `FenceSourceKind.Library` (at most one, repaired on load). Core treats every non-Desktop fence
  like a Portal already (no items, never a rule target), so only the App needed new code.
- **Core** (pure, tested): a small Valve text reader (`ValveKeyValues`, `SteamFiles`), `EpicManifest`, `GameCatalog`
  (merge by install folder or launch target, source priority launcher > Xbox > Desktop shortcut > folder, tool
  skipping, hidden ids, A–Z ignoring "The", games of unreadable scans kept), `ProgramPicker`, `LibraryFiles` (safe,
  stable, unique file names; only changed shortcuts rewritten), `LibrarySettings` in `config.json`.
- **Shell:** `GameScanners` (Steam per library, Epic manifests, GOG / Ubisoft registry, EA / Battle.net uninstall entries,
  Xbox packages with `MicrosoftGame.config`, game folders, Desktop shortcuts), `ShellLinks` (read/write `.lnk` via
  `IShellLinkW`, `.url` as INI text), `LibraryWriter` (temp-then-swap shortcuts, `index.json`; deletes only files in its
  own index). Item menus can add custom commands and hide Rename; drags can be copy-only; a fence can refuse drops.
- **App:** the library folder `%LOCALAPPDATA%\NeoFences\library\` is shown by the Portal machinery; scans run on their
  own STA thread at start, on changes to Steam's / Epic's / game folders (5 s quiet), on Refresh, after game mode;
  tiles 2:3 with posters or Xbox logos; Hide from library stores every id the game goes by; Settings → Game Library.

**Consequences.**
- NeoFences writes shortcut files into its own data folder; user files, game folders and launcher files are never
  changed. An older NeoFences cannot read a config with a library fence (it falls back to a backup) — downgrades only.
- A Desktop shortcut into a game folder wins over the folder entry (the user's chosen program); Steam `.url` shortcuts
  on the Desktop merge with the Steam entry.
- Steam apps that are not games (Wallpaper Engine) show until hidden; obvious tools are skipped by name.

## ADR-033 — Config schema version 2; data from a newer NeoFences is never rewritten (M13a, v1.6.0)
**Date:** 2026-10-04 · **Status:** Accepted

**Context.** Tabs (v1.2), rules (v1.4) and the Game Library (v1.5) added fields to `config.json` and snapshot files
while the schema version stayed 1. An older NeoFences reading such a file would drop those fields on its next save.
The M9–M12 reviews also left smaller data-safety gaps: snapshots from a newer version were renamed or restored with
fields missing, copied rule blocks shared one id, and the library index could disagree with the folder after a
failed write.

**Decision.**
- `NeoFencesConfig.CurrentSchemaVersion = 2`; the normalizer stamps every loaded config with it, so the first save of
  v1.6 writes 2. Versions ≤ 1.5 already treat a newer schema as read-only (ADR-006 store) and never save over it.
- Snapshot files carry the same version; a snapshot from a newer NeoFences, or one over 16 MB, is listed as a problem
  and never loaded, restored or renamed. A failed snapshot write leaves no temp file.
- Duplicate rule ids get new ids on load (the first keeps its id).
- The library index is settled against what really happened (`LibraryFiles.Settle`): a failed rewrite keeps the old
  entry, a failed delete stays listed, a failed new file is left out.

**Consequences.** Downgrading after v1.6 shows a fresh read-only state until the user upgrades again or restores a
pre-1.6 backup from `backups\`; nothing is lost.

## ADR-034 — Game Library polish: shortcut copies, remembered hidden games, quiet watchers (M13b, v1.6.1)
**Date:** 2026-10-04 · **Status:** Accepted

**Context.** Carry-overs from the M11–M13a reviews: Epic add-ons and Unreal Engine listed as games, Xbox titles under
package names, a Desktop shortcut into a launcher's game folder shown twice, shortcut games losing their icon and
"Run as administrator", 300+ games searched on every scan, hidden games listed by raw id while their source was away,
library watchers rescanning while games wrote files, power cuts leaving temp files and an unflushed index.

**Decision.**
- Epic lists only manifests in the "games" category whose main game is themselves; Xbox names come from the package's
  resources (`SHLoadIndirectString`).
- A Desktop shortcut whose program lies inside another entry's install folder is that game (programs only, never folders).
- A Desktop shortcut game is copied into the library as is (`GameEntry.ShortcutFile`), not rebuilt.
- Scans reuse a program found before while it still exists; game folders are watched for folders only; covers are
  decoded at the tile's pixel width.
- Hidden games are kept in the library index with every id and their name (`GameCatalog.HiddenGames`), so Settings
  lists each once, by name, also while its source cannot be read.
- `library\index.json` is written through to disk; NeoFences' own temp names (`.<32 hex>.lnk/.url`) are swept before
  each write. A failed rewrite of a file that is gone drops its entry (`lostFiles`).
- `config.json` from before schema 2 is kept once as `backups\pre-schema-2-config.json` (outside the daily rotation).
- Portals let go of a listing still in flight when a drive is being removed; the rules editor keeps its list still
  while open; a library tab no longer blocks tab switching during a drag.

**Consequences.** A user's own shortcut changes (a new icon) reach the library on the next change of its target or name,
not of its icon alone.

## ADR-035 — Fence and Settings UX carry-overs; what stays as it is (M13c, v1.6.2)
**Date:** 2026-10-04 · **Status:** Accepted · **Amends:** ADR-026 (drop rows)

**Context.** The last v1.6 batch: fence and Settings carry-overs from the M8b–M13b reviews. Right-clicking a title
showed nothing (Windows' caption menu, with no system menu on a tool window); a header click on a rolled-up box did
not open it; snapshot failures appeared only in a tray notice (easy to miss, hidden by Do Not Disturb); tray snapshot
names could be blank, multi-line or very long; and Settings lost the keyboard focus on refreshes. A further set of
items was weighed and kept as it is.

**Decision.**
- **Title right-click.** A right-click on the caption (`WM_NCRBUTTONUP` with `HTCAPTION`) opens the fence menu at the pointer,
  the same one a body right-click opens. A header click on a rolled-up box in click mode switches the tab and opens it.
- **Where results show.**
  - Snapshot failures use Windows' warning notice (`NIIF_WARNING`).
  - While Settings is open, every snapshot result also shows in its Snapshots card, as a live region.
  - "More in Settings…" opens the card.
  - The delete confirmation is owned by Settings.
- **Tray snapshot labels** (`Snapshots.MenuLabel`):
  - one line, with control characters turned into spaces;
  - cut to 60 characters plus "…";
  - `&` escaped;
  - a blank name becomes "Snapshot d MMM HH:mm".
  - Separators only sit between groups, never first.
- **Settings keeps its place.**
  - A refresh with nothing new rebuilds neither the snapshot list nor the rules list.
  - After a rename or rebuild, the focus returns to the row.
  - Only a double-click on a row restores.
  - An edit whose rule vanished meanwhile is saved as a new rule.
- **Hotkey labels** use the layout's character for OEM keys (`MapVirtualKey(MAPVK_VK_TO_CHAR)` in `NeoFences.Shell`),
  falling back to the US names.
- **Smaller fixes.**
  - Desktop shortcuts that are created, renamed or deleted rescan the library after 5 quiet seconds. A retarget in place is no Desktop change; "Refresh library" or the next start picks it up.
  - In-flight eject entries are removed only when they are still that listing's.
  - The drag handler hit-tests a header only for a refused fence.
  - `.url` reads stop at 64 KB.
  - The pre-schema-2 copy is checked once per run.
  - Same-place tab reorders and same-colour picks return the config unchanged.
- **ADR-026 amended:** with a `WrapPanel` every cell of a row already has the row's height, so `DropZones.InsertIndex`
  mostly confirms the row the pointer is in. It stays as it is: it is tested and costs nothing.

**Kept as it is (rulings).**
- **Tab strip.** No minimum header width and no insertion caret: headers share the strip evenly, and a reorder already shows where it lands.
- **Mixed DPI.** A detached tab keeps the box's DIP size, and the ghost uses the source monitor's scale. A corner case on one monitor setup, and fixing it means reworking the drag ghost.
- **Drag-drop edge cases.**
  - Mixed drops (desktop and non-desktop items, and Ctrl copies) stay with Windows.
  - Selections that include a shadowed Public item log and do nothing.
  - Virtual sources show Windows' cursor.
  - Reworking these risks the drop paths that work.
- **Small internals.**
  - A new removal notice per listing: cheap, and simpler than sharing one.
  - A repaired heir's old rect can return: harmless (it is the user's own last rect).
- **Platform and install.**
  - Label shadow measurement, the Windows 10 corner radius, `VELOPACK_*` variables and Setup's token stay open as user or platform checks in the ROADMAP.

**Consequences.** Every v1.6 carry-over is closed or ruled. The open items left in the ROADMAP are user-only hand checks and the
later update-source / signing work.

## ADR-036 — Appearance: colour styles, per-tone strength, title fonts, wallpaper accent from the wallpaper's image (M14, v1.7.0)
**Date:** 2026-10-04 · **Status:** Accepted · **Spec:** `docs/superpowers/specs/2026-10-04-appearance-design.md`

**Context.** Fences all looked the same: no veil in dark mode, a fixed light veil, the tab colour only as a 3 px bar,
one title font. The user asked for a darker/lighter background (2026-10-02), per-fence colours, title fonts and a colour
taken from the wallpaper, which is a live Wallpaper Engine wallpaper. Windows ignores the tint of the accent blur, so
every colour has to be NeoFences' own veil.

**Decision.**
- **Look resolver in Core** (`FenceLook.Resolve`): pure; the defaults reproduce v1.6 exactly. Colour order: custom
  colour, then the tab swatch, then the wallpaper accent (when the switch is on), then none.
- **Colour style is one global preference** with three looks (user: "all three look good, add a preference"):
  Accent edge (outline, title, bar), Tinted glass (at least 22 % tint), Title strip (dark title text on light strips).
- **Background strength per tone**: one slider in Settings sets the strength of the tone Windows uses now; the other tone
  keeps its own value (dark 0 %, light 72 % by default = v1.6).
- **Title font** (amended by ADR-038: one title font for all fences, no per-fence override): a global default; each fence can override family, size or weight. The title row and Windows' caption
  area grow with the size (26 / 30 / 34 / 38 DIP).
- **Wallpaper accent from the wallpaper's own image, no screen capture**: Wallpaper Engine's selected wallpaper preview
  (its `config.json` and the wallpaper's `project.json`), else Windows' wallpaper (`IDesktopWallpaper`), else Windows'
  accent colour. The strongest vivid hue of a 64 px decode, saturation ≥ 45 % and lightness 40–65 %. Re-read only on a WE
  config write (debounced 2 s), Windows' wallpaper notice, a display change or the switch. The user first picked
  "sample the desktop"; this gives the same result with Wallpaper Engine, works behind windows, and costs nothing in games.
- **Schema 3**: new fields; v1.6 opens a v3 config read-only (ADR-033); one `pre-schema-3-config.json` copy is kept.
  Unknown style or weight names in a hand edit are repaired, never fail the file.
- **Custom colours** use Windows' colour dialog (`ChooseColor`, CsWin32). No new NuGet.

**Consequences.** Coloured fences from v1.6 (a tab colour) now also get a coloured outline and title in the default
Accent edge style. Snapshots carry each fence's colour (and, until v1.7.1, its font — amended by ADR-038), not the global Appearance settings.
`// ponytail:` WE closed without a wallpaper change keeps the accent until the next change; WE's "MonitorN" is taken as
Windows' Nth wallpaper monitor.

## ADR-038 — One title font for all fences; v1.7.1 polish (M16, v1.7.1)
**Date:** 2026-10-04 · **Status:** Accepted · **Amends:** ADR-036 (per-fence title fonts)

**Context.** Testing the polish batch, the user found the fence menu's Title font (Font ▸ / Size ▸ / Weight ▸, ~300
fonts) too deep and per-fence fonts too much in practice: customizations that apply to all fences belong in Settings.
The batch also closes the M13c and M14 review carry-overs.

**Decision.**
- **One title font**, Settings → Appearance only. The fence menu's Title font is gone; `Fence.TitleFont` and
  `FenceEdits.SetTitleFont` are removed. A per-fence font in an older config is ignored and dropped on the next save
  (a cosmetic setting; the fence keeps everything else). The per-fence **Colour** menu stays (one level, per fence by nature).
- **Readable titles with any colour**: Accent edge pushes a pale custom colour darker on the light veil (luminance ≤ 0.25)
  and a dark one lighter on the dark tone (≥ 0.30, which keeps every swatch exactly as in v1.7); colours already readable keep v1.7's look.
- **Smaller fixes:**
  - Colour → Custom… → Cancel shows the fence's own choice again.
  - A detached monitor with an empty area is skipped in the Wallpaper Engine mapping.
  - Settings keeps a removed font's name listed.
  - Brushes are not rebuilt when a fence's look did not change.
  - A font-size change during a roll-up stops the animation first.
  - Hotkey messages use the layout's characters.
  - A keyboard layout switch (also between layouts of one language, `WM_INPUTLANGCHANGE`) refreshes Settings.
  - Desktop changes from a full reconcile (a game-mode flood, lost watcher events, start) reach the library.
  - A drag frame over a Library tab header is refused at once.
  - `.url` reads use a pooled buffer.
  - Tray labels never split an emoji.

**Consequences.** v1.7.0 configs with per-fence fonts lose them on the first v1.7.1 save (by design). No schema change:
the field is simply no longer written, and v1.7.0 reads the result fine.

## ADR-039 — Public repository, CI draft releases, Velopack auto-update; signing deferred (M17, v1.8.0)
**Date:** 2026-10-04 · **Status:** Accepted · **Spec:** `docs/superpowers/specs/2026-10-04-public-releases-design.md` · **Builds on:** ADR-023

**Context.** Friends should install NeoFences from GitHub and receive updates by themselves. ADR-023 shipped an installer
with no update source.

**Decision.**
- **One public repository** (`github.com/CipherSnowden/neo-fences`) with code, docs, history and releases; **not open
  source** (no license file: all rights reserved). Before the first push the history was rewritten: every commit uses the
  account's GitHub noreply address, and the private project-hub link was removed from every commit (it lives in the
  git-ignored `CLAUDE.local.md`). A scan of all commits found no personal address, hub link, token or key. The
  unscrubbed original is a local mirror only.
- **Releases from GitHub Actions:** `ci.yml` builds and tests every push to `main` and every pull request; `release.yml` turns a pushed `v*.*.*` tag into a
  **draft** release (delta from the previous release, notes from the `feat:`/`fix:` commits), using the workflow's own
  token. A draft is installed and checked on the developer PC, then published; installed copies see only published releases.
- **Auto-update with Velopack** (already a dependency; Velopack's apply-on-start is turned off, so an update is never
  applied by force when NeoFences.exe starts): `UpdatePolicy` (Core) checks at start and every 24 h, 6 h after a
  failure, never in game mode, never when switched off (`Settings.AutoUpdate`, schema 4: then no network calls) and never
  in a developer build. Updates download quietly; "Restart to update" or the next normal exit applies them, always after
  NeoFences' clean exit (hard rule 2); never at sign-out or shutdown. A local releases folder (`NEOFENCES_UPDATE_SOURCE`)
  allows a full rehearsal without GitHub.
- **Signing deferred:** not required. Friends see SmartScreen once at first install; updates never do. Smart App Control
  blocks unsigned apps; revisit then. Free OSS signing needs an open-source license; otherwise a paid certificate. The
  release workflow keeps a marked place for the signing step.

**Consequences.** Every release is two outward-facing steps (push the tag; publish the draft), each asked for. The parked
search palette, if resumed, takes schema 5.

## ADR-040 — Pivot: fences hold virtual items; no file operations; 0.x versions; fresh repository
**Date:** 2026-10-04 · **Status:** Accepted · **Supersedes:** the Takeover/membership model (ADR-011, ADR-014 and the
desktop-membership parts of later ADRs), Portals (ADR-0xx M4) and Rules auto-sort (ADR-031) for now — parked, see
`docs/PIVOT-2026-10-04.md`

**Context.** Organizing desktop items made fences do real file operations (cross-drive drops copy files onto the
Desktop; bug K5: a Desktop file dropped from Explorer hit Windows' "same source and destination" dialog). The user wants
an organizer like Rainmeter that can never harm a file and leaves the Desktop as Windows shows it.

**Decision.** Fence items are **virtual items** (target + own name, icon, path; editable; the same target in several
fences). No NeoFences action touches a real file; delete removes the item. Targets are watched (renames followed;
deletions → "missing", unplugged drives → "unavailable"; limits ~64 folders, batched refresh; a per-fence Refresh).
Native desktop icons visible by default; an optional "Hide desktop icons while NeoFences runs" keeps the watchdog.
Takeover, Inbox auto-fill, Portals, Rules and item file actions are parked (dynamic fences later). **Hard rule 1** becomes
"NeoFences never modifies, moves, renames or deletes any user file"; rule 2 applies when the user hides icons.
Versions restart at **0.x** (reset codebase 0.8.0, virtual items 0.9.0, sharing 1.0.0); the GitHub repository is reset to
one fresh commit; full history kept in a local bundle.

**Consequences.** Big removal of risky shell code in M18; v1.x mentions in older docs mean pre-reset dev builds.

## ADR-041 — Virtual items live in their own file, `items.json`
**Date:** 2026-10-04 · **Status:** Accepted · **Spec:** `docs/superpowers/specs/2026-10-04-virtual-items-design.md`

**Context.** M18 turns fence items into virtual-item records (ADR-040). They could live inside `config.json` or in a file
of their own; real `.lnk` files per fence were rejected (two sources of truth, power-cut risk).

**Decision.** Items are stored in `%LOCALAPPDATA%\NeoFences\items.json` (`{ schema, fences: { fenceId: [items] } }`),
fences and settings stay in `config.json` (schema 5). Both use the same safe save and backups; snapshots hold both.
Orphan item lists (fence id unknown) are kept until the next items save; a fence without an entry is empty; deleting a
fence saves the config before the items. Custom icon images are copied into `icons\`.

**Consequences.** A damaged items file never takes settings or layout with it (and the other way round). Two files can
disagree after a power cut — handled by the rules above, covered by Core tests.

**Amendment (2026-10-04, M18 final review I1).** Saves never drop item lists. "Delete fence" removes its own list; a list
whose fence the config does not have (a power cut between the two saves, a config that came back from a backup or
fresh) stays in items.json, a few bytes, and comes back to life if the fence does (a restored backup or snapshot).
Pruning at save lost every list after one session on a fresh or old config.

## ADR-042 — 0.10.0: Store apps as items, bulk fix with an undo snapshot, Add from desktop
**Date:** 2026-10-05 · **Status:** Accepted · **Spec:** `docs/superpowers/specs/2026-10-05-m19-apps-relocate-desktop-fill-design.md`

**Context.** After M18 the user wants apps that are not files (Store apps, Start menu entries) as items, a way to fix
many broken links at once, and a quick way to turn the desktop into fences.

**Decision.** An app item's target is `shell:AppsFolder\<AppUserModelID>`: opened through Explorer, name and icon from
the shell, **Missing** when it no longer parses (uninstalled); added from an "An app…" list (AppsFolder enumeration) or
by dragging from Start when Start hands over app entries. A Locate… on a missing or unavailable item offers to move the
other missing or unavailable items under the same old folder (matched from the end of the path) when their files exist
at the new place; the fix saves a snapshot in the "before restore" slot first, so the tray's "Undo the last restore or
fix" reverts it. "Add from desktop…" lists the desktop grouped (Games / Apps / Folders and files / Web links) and adds
items that point at the **desktop entries themselves** (like a drag from the desktop), never at a shortcut's resolved
target.

**Consequences.** App items carry no arguments or run-as-admin. One undo slot shared by restores and fixes. Deleting a
desktop shortcut later makes its item Missing; a clean desktop comes from "Hide desktop icons".

## ADR-043 — The name stays NeoFences
**Date:** 2026-10-05 · **Status:** Accepted · **Supersedes:** ADR-009

**Context.** ADR-009 planned a rename before any public release because "Fences" is a Stardock trademark. The repository
and releases are public (ADR-039); the user decided on 2026-10-05 to keep the name.

**Decision.** The product, executable, install folder, data folder and code keep the name **NeoFences**. It is a personal
project for now; the name is revisited only if it is ever distributed more widely (1.0.0 for friends or beyond).

**Consequences.** No rename work; the trademark question stays a listed risk on the hub.

## ADR-044 — Folder views: fences that show one folder live, read-only
**Date:** 2026-10-05 · **Status:** Accepted; the folder-view *fence* superseded by ADR-048 (0.15.0: a folder panel element; the read-only rules stand) · **Refines:** ADR-040 (Portals parked "for later as dynamic collections")

**Context.** ADR-040 parked Portals and Rules for "dynamic collections". The user chose folder views first (M21, 0.11.0):
game and app folders, work folders, Downloads and Screenshots, and USB sticks, each shown live in a fence.

**Decision.**
- A fence is one of three kinds: items (items.json), the Game Library, or a **folder view** (`Fence.View`, a
  `FolderView { path, show, sort, newest, patterns }` in config.json; schema stays 5, an older NeoFences ignores it).
- **Read-only**: NeoFences never writes to the folder. Drops onto a view are refused; NeoFences' own menu has Open, Open
  file location, Copy path and Add to fence ▸; Del, F2 and Alt+Enter do nothing. Windows' menu (Shift+right-click) stays,
  as for items: what the user picks there is Windows' action.
- Subfolders open in Explorer (no browsing inside the fence, user choice). Entries dragged out are copies; dragged onto an
  items fence they become items.
- Per view: show (all / files / folders), type patterns (subfolders always pass when showing all), sort (Name / Type /
  Date, kept and live), only the newest N (1–500). New views of Downloads and Screenshots start newest first with 30.
- At most 500 entries are shown, then "+ N more — Open folder".
- Listing reuses the Game Library's machinery (`FolderLister`, former Portal code): off the UI thread, 250 ms coalescing,
  7 s retry while unavailable, watcher backoff, removal notices (Safely Remove), game-mode pause; the view follows its
  folder's rename.

**Consequences.** Each view holds two `FileSystemWatcher`s and, on a removable drive, a removal notice; no limit on the
number of views (personal use). Auto-collect rules stay for later.

## ADR-045 — One kind of fence: games become items
**Date:** 2026-10-05 · **Status:** Accepted · **Refines:** ADR-032 (Game Library), ADR-044 (folder views)

**Context.** The user (2026-10-05): fences should not have types to choose. A fence is a container of elements whose
kind and own settings decide how they look; later it also holds a folder panel, widgets (clock, calendar) and elements
of several sizes. The first step (M22, 0.12.0) makes games ordinary items.

**Decision.**
- A game item is a virtual item with `GameId` whose target is NeoFences' own shortcut for the game in `library\`;
  `ShowAs` (Cover / Icon, null = cover) picks its look. Opening, Missing, drag, Properties and snapshots are the items'.
- The library scan is the engine, not a fence: it runs while game items exist, `Library.NewGamesFence` is set, or Add
  games… is open. After each scan game items follow their game's current shortcut (`Retarget`) and games no earlier scan
  had go to `NewGamesFence`.
- An old Game Library fence becomes an items fence with one game item per game, once, after a snapshot "Before games
  became items"; it becomes the new-games fence when none is chosen.
- Kinds stay in the code (`Fence.IsLibrary`, `FenceKind.Library`); "New Game Library fence" leaves the menus.
- Folder views stay a fence setting for now (not decided).

**Consequences.** Only games no fence holds go to the new-games fence, and never on a first scan (no earlier scan to
compare with); a migration cut short runs again without doubling games, and none runs while a store is read-only. An
uninstalled game's items show "not installed" (dimmed, ⚠, covers too) and come back on reinstall. Hiding games stays in Settings. Mixed fences size each cell by its item (tile or icon).

## ADR-046 — Element sizes and the fence grid
**Date:** 2026-10-05 · **Status:** Accepted · **Continues:** ADR-045 (one kind of fence)

**Context.** Widgets and a folder panel (the user's vision) need room bigger than one icon; the user also wants sizes for
any element and a choice of how elements sit, per fence.

**Decision.**
- Every element spans 1–4 columns × 1–4 rows of its fence's cells (`VirtualItem.Size`; default 1×1, 1×2 for a game
  cover). A cell is the fence's icon cell (icon size + label mode).
- Each fence is **Flow** (packed in order; smaller elements fill gaps) or **Free** (`VirtualItem.Cell`; elements without a
  usable cell go to the first free spot without changing what is stored). Switching to Free stores the shown cells.
- The layout is a pure Core function (`FenceGrid`) with a thin WPF panel (`FenceGridPanel`) replacing the WrapPanel; a
  failed pass falls back to 1×1 in order.
- Free drops land on the cell under the pointer (offsets kept; a taken spot → the nearest free one); Sort in Free packs.

**Consequences.** Folder-view entries and the old library fence stay 1×1; no resize handles (menu picker only); spans
above 4 later if wanted. config.json writes each fence's `layout`; items.json writes `size` / `cell` only when set.

## ADR-047 — Widgets: clock, date, system stats
**Date:** 2026-10-05 · **Status:** Accepted · **Continues:** ADR-045 (one kind of fence), ADR-046 (element sizes)

**Context.** The user's vision: a fence holds elements; widgets are one kind. The user chose a clock, a date page and
system stats (CPU, RAM, GPU, disk) as bars.

**Decision.**
- A widget is stored like an item with a `neofences:widget/<clock|date|stats>` target (`ItemKind.Widget`): sizes,
  layouts, drags, copies, Remove and snapshots are the items'. Never checked, watched, relocated or dragged out to apps.
- Defaults: Clock 2×1 (Windows' short time format; options: seconds, date line), Date 2×2 (weekday, day, month year),
  Stats 2×2 (CPU, RAM, GPU, C: used; "—" when Windows gives no value). Double-click: Clock app / Task Manager.
- Readings from Windows' own counters through CsWin32 (`GetSystemTimes`, `GlobalMemoryStatusEx`, `GetDiskFreeSpaceEx`,
  PDH GPU engines); no new dependency.
- One timer on whole seconds while any widget exists; no work while no widget can be seen (paused, quick-hide, game
  mode, rolled up, hidden tab); stats read every 2 s on a worker, one reading at a time.

**Consequences.** Measured 0.003 % of the user's CPU with three widgets shown. A widget kind a newer NeoFences adds reads
as a plain "missing" item in an older one. (0.16.1, M28: it stays a widget — never opened through Windows — named
"Unknown widget", shown Missing.)

## ADR-048 — The folder panel element (folder views become panels)
**Date:** 2026-10-05 · **Status:** Accepted · **Supersedes:** ADR-044's folder-view fence (its read-only rules stand) ·
**Continues:** ADR-045 (one kind of fence), ADR-046 (element sizes), ADR-047 (elements beside items)

**Context.** The user's vision: a fence holds elements; "show a specified folder in detailed view" was the last one named.
Folder views (M21) were a fence kind of their own. The user chose: the panel replaces folder views; looks Details, List
and Icons; double-clicking a subfolder browses inside the panel; sizes 1–4 × 1–4 cells or "Fill fence".

**Decision.**
- A panel is a folder item with `VirtualItem.Panel` (`FolderPanel`: look, show, patterns, newest N, sort + direction) and
  optionally `Fill`. Being a path item, it gets missing state, Locate…, the bulk fix, rename-follow, drives coming and
  going, copies and snapshots for free. Default span 4×4.
- "Fill fence" works while the panel is the fence's only element; beside anything else it sits on cells (a size pick
  turns it off). The fence list then does not scroll; the panel does.
- Browsing (Back / Up / Home, Backspace, Alt+Up) lives in memory; each start and Home show the panel's own folder. Up
  never goes above it.
- Details columns Name, Date modified, Type, Size (Name and Date at 1–2 cells wide); a header click sorts (again:
  reversed). Name and Type keep folders first; Date and Size mix them.
- Folder views migrate once at start (and after restoring an older snapshot): a "Before folder views became panels"
  snapshot, then each view fence holds one filling Icons panel with the view's settings; items are saved before the
  config, so a cut-short save only repeats the migration. `Fence.View` / `FenceKind.View` stay in the code.
- Read-only as ADR-044: drops over a panel are refused; its entry menu is NeoFences' safe one (Shift: Windows' menu).

**Consequences.** One lister per panel (hidden tabs keep listing, game mode pauses them); rows are virtualized and icons
load as rows come into view; at most 500 entries, then "+ N more". An older NeoFences shows a panel as a plain folder icon.

## ADR-049 — Auto-collect rules
**Date:** 2026-10-05 · **Status:** Accepted · **Continues:** ADR-040 (dynamic collections parked for later), ADR-048

**Context.** The user hides the native desktop icons, so a shortcut an installer drops on the desktop is invisible. The
other half of the "dynamic collections" parked in ADR-040: fences that gather matching items by themselves. The user chose
this milestone and approved the proposed defaults.

**Decision.**
- A fence holds rules (`Fence.Collect`): a source (a folder, or the desktop = the user's and the Public Desktop), kinds by
  file name (Apps and shortcuts, Installers, Documents, Pictures, Archives, Anything) and optional patterns.
- One `FolderLister` per watched folder; each listing is compared with the last one, and what arrived (created, renamed
  or moved in) goes to the first matching rule in fence order, as an item. Nothing any fence already holds; at most 200
  per rule per burst. Never a file operation.
- Removed stays removed: only arrivals count. At start (or a drive back) what was created after the rules last looked
  (`Watermark`, updated when something arrived) is caught up.
- A new rule offers what matches already once ("Add these N too?", default No). Game mode and Pause hold arrivals until
  they end.

**Consequences.** A file moved into a watched folder while NeoFences was closed keeps its old creation time and is not
caught up. config.json writes `collect` only for fences with rules (schema stays 5); an older NeoFences ignores it.

## ADR-050 — Help lives on GitHub, linked from the app
**Date:** 2026-10-05 · **Status:** Accepted

**Context.** NeoFences is ready for friends to try; the README still described the pre-pivot design and there was no
guide. The user chose help on GitHub, linked from the app, over an in-app help window.

**Decision.** The README (what it is, install, quick start, safety) and `docs/GUIDE.md` (every feature, screenshots from a
demo setup with nothing personal) live in the repository. The tray's **Help** and Settings → About and logs → **Help
(online guide)** open `https://github.com/CipherSnowden/neo-fences/blob/main/docs/GUIDE.md` in the browser through the
usual open path.

**Consequences.** Help is always the current guide and needs no in-app copy kept in sync; reading it needs a network.
Every change to a menu, a setting or a gesture also updates the guide (same commit).

## ADR-051 — The welcome lives in the first fence
**Date:** 2026-10-06 · **Status:** Accepted

**Context.** A friend who installs NeoFences saw one empty fence and nothing else: Windows 11 may hide the new tray icon
behind the ^ arrow, and nothing said what NeoFences is or how to sort the desktop. The user chose a welcome inside the
first fence over a welcome window or a tray notice alone.

**Decision.** A fresh start's first fence carries `welcome: true` (written only when set). While it is empty it shows a
welcome: **Sort my desktop…** (the existing Add from desktop dialog), **Add item…**, **Guide**. Its first item ends the
welcome (an ordinary fence from then on); an Add from desktop that puts items into new fences removes the still-empty
welcome fence (an empty fence only, still titled "Fence" and without auto-collect rules; no file is involved). A fresh start shows one tray notice saying where the icon is
(not in game mode). Hiding Windows' own icons stays the dialog's existing checkbox: no second question.

**Consequences.** Updating copies and every config after the welcome ended never show it; there is no way to show it
again (out of scope). A config that falls back to a fresh start (missing, unreadable with no backup, pre-pivot) shows the
welcome too; a read-only start (an older build on a newer config) never does, nor its notice.

## ADR-052 — Temperatures from MSI Afterburner or HWiNFO, read-only
**Date:** 2026-10-06 · **Status:** Accepted

**Context.** The user wanted CPU and GPU temperatures in the System stats widget. Windows gives a normal (non-admin) app
the GPU temperature (as Task Manager shows it) but no reliable CPU temperature: that needs a kernel driver running with
admin rights, which NeoFences never uses. The user runs MSI Afterburner; friends may run HWiNFO64. Both publish their
sensors in shared memory that any process may read.

**Decision.** The stats read MSI Afterburner's `MAHMSharedMemory`, then HWiNFO's `Global\HWiNFO_SENS_SM2` for any value
Afterburner lacks, read-only through `MemoryMappedFile` (no Win32 binding, no dependency); Core parses a copy of the bytes
(signature and size checks; anything odd is nothing). The main graphics card is the one using the most video memory — in
the Windows fallback too, by each adapter's dedicated-memory counter. Without either monitor, GPU use comes from PDH and its temperature from D3DKMT adapter perf data (a commented DllImport: CsWin32 has these WDK
functions only through the WDK metadata package, a new dependency). CPU TEMP then shows "—" with "needs Afterburner or
HWiNFO". RAM shows GB used of installed; the disk tile is gone.

**Consequences.** CPU temperature needs one of the two monitors running (HWiNFO free stops sharing after 12 hours until
switched on again). NeoFences never starts, closes or configures them. A later sensor picker can use the same readings
(clocks, power, fans, FPS are all there).

**Amended 2026-10-06 (M32, 0.19.1).** The main GPU is sticky: each source keeps its last choice until another GPU uses at
least twice its video memory and 1 GB more (`Widgets.StickyGpu`), so a hybrid laptop's card idling at 0 MB beside a
built-in GPU does not flip the tiles. HWiNFO's copy is taken while holding `Global\HWiNFO_SM2_MUTEX` (at most 20 ms;
without it as before). The copy is a rented buffer.
