# M21 — Folder views (0.11.0) — design

**Date:** 2026-10-05 · **Status:** approved in brainstorm (sections 1–3), awaiting written-spec review
**Decisions:** ADR-044 (this milestone; refines ADR-040, which parked Portals "for later as dynamic collections")
**Background:** `docs/PIVOT-2026-10-04.md`; the pre-pivot Portals (ADR-018, ADR-027) for the watcher and removal lessons

## Goal

A **folder view** is a fence that shows one folder's contents live, read-only. It is the first half of "dynamic
collections"; auto-collect rules stay for later. 0.11.0 is still a personal release.

The user's folders, all of them wanted: game or app folders (stable, by name), work and project folders (mixed files
and subfolders), Downloads and Screenshots (busy, newest first), a USB stick or another drive (comes and goes).

Hard rules stand unchanged:
- **A view never writes to its folder** (ADR-040). No rename, delete, move or drop into it from NeoFences' own UI.
  Windows' own menu (Shift+right-click) stays available, as for items: what the user picks there is Windows' action.
- Every Win32/COM call lives in `NeoFences.Shell` via CsWin32; no new NuGet dependency.
- A failing folder degrades that one view (shows "not available"), never the app.

Success: the user makes a Downloads view showing the 30 newest files, a `D:\GameLibrary` view showing only folders, and
a USB-stick view; files appear and go live, the stick's view says "not available" when pulled and comes back when plugged
in, Safely Remove works while it is shown, and a file dragged from a view to a normal fence becomes an item.

## 1. Model

### `FolderView` (Core, `Model/FolderView.cs`)

```csharp
public enum ViewShow { All, Files, Folders }
public enum ViewSort { Name, Type, Date }   // Date = newest first

public sealed record FolderView
{
    public required string Path { get; init; }
    public ViewShow Show { get; init; } = ViewShow.All;
    public ViewSort Sort { get; init; } = ViewSort.Name;
    public int? Newest { get; init; }          // show only the newest N (by date), then sort those; null = all
    public string Patterns { get; init; } = ""; // "*.png;*.jpg"; empty = everything
}
```

- `Fence` gets `public FolderView? View { get; init; }`. A fence is one of three kinds: **items** (items.json),
  **Library** (`IsLibrary`), **view** (`View is not null`). `IsLibrary` and `View` are never both set; loading a config
  with both keeps the Library and drops `View` (logged).
- Plain optional JSON field: the config stays **schema 5**, no migration. An older NeoFences ignores the field and shows
  an empty items fence (acceptable: 0.x, personal).
- Snapshots carry `View` automatically (they store the whole config).

### Selection (Core, `Items/FolderViews.cs`), test-first

```csharp
public static class FolderViews
{
    public const int MaxShown = 500;
    public sealed record Selection(IReadOnlyList<ItemInfo> Shown, int Hidden);   // Hidden = matched but over the cap

    public static Selection Select(IReadOnlyList<ItemInfo> entries, FolderView view);
    public static IReadOnlyList<string>? ParsePatterns(string patterns);         // null = invalid
    public static FolderView DefaultsFor(string path, string? downloads, string? screenshots);
}
```

- `Select`, in order: show-kind (Files / Folders / All) → patterns (folders always pass patterns when `Show` is All, so
  a `*.png` view of Screenshots still shows its subfolders; with `Show = Files` they are gone anyway) → `Newest` (by
  `Modified`, descending, take N) → sort (Name: folders first then A–Z, natural order like `ItemSorting`; Type: by
  extension then name; Date: newest first) → cap at `MaxShown`, `Hidden` = the rest.
- `ParsePatterns`: split on `;` and `,`, trim, drop empties; each must be a file-name pattern (`*` and `?` allowed, no
  path separators, no `"<>|:`). A bare word like `png` becomes `*.png`. Matching is case-insensitive, Windows-style
  (`FileSystemName.MatchesSimpleExpression`).
- `DefaultsFor`: Name sort, everything shown; the Downloads or Screenshots folder (paths compared case-insensitively,
  trailing slash ignored) gets `Sort = Date, Newest = 30`.
- Hidden and System entries never show (unchanged `FolderItems.TryList`).

## 2. Creating and editing

- **"New folder view…"** in the tray menu (under "New Game Library fence") and in the fence menu's New section.
  Windows' folder dialog (`PathPicker.TryPickFolder`) → the **Folder view settings** dialog prefilled with
  `DefaultsFor` → OK creates the fence, titled with the folder's display name, placed like "New fence".
  Cancel at either step creates nothing.
- **Folder view settings** (`FolderViewWindow`): folder (text + Browse…), Show (All / Files only / Folders only), Types
  (pattern text, e.g. `*.png;*.jpg`, hint under it; an invalid pattern marks the box and disables OK), Sort (Name /
  Type / Date), "Only the newest [N]" (checkbox + number 1–500). Opened later from the view's fence menu → "Folder view
  settings…". Changing the folder keeps the fence's title unless the title still equals the old folder's name.
- **"Show as folder view"** in the item menu of a folder item (a `Path` item whose target is a reachable folder) in a
  normal fence: creates a view of that folder with `DefaultsFor`, placed beside the fence, no dialog. The item stays.
- Known folders: `KnownFolders.TryGetPath(Guid)` in Shell via `SHGetKnownFolderPath` (CsWin32), for Downloads
  (`FOLDERID_Downloads`) and Screenshots (`FOLDERID_Screenshots`); null when Windows has none.

## 3. Behaviour (read-only)

- **Keys** are full paths (as in the Library). `ShownItem(path, path)`; names and icons from the existing `IconLoader`.
- **Open**: double-click / Enter opens the entry (`OpenItem` by path, as the Library); a subfolder opens in Explorer.
  Enter on several opens each.
- **Right-click → NeoFences' safe menu**: Open · Open file location · Copy path · **Add to fence ▸** (every items fence
  by title; adds the selected entries as virtual items at the end, then flashes them there) · Shift+right-click →
  Windows' menu for the real entries, under the same "Windows' menu" line as for items.
  No Remove, Rename, Properties; Del, F2 and Alt+Enter do nothing in a view.
- **Drag out**: files to other apps as a copy, never a move (the existing `DragItems` path with keys as paths); onto a
  normal fence they become items (the existing "keys not in items.json → `AddTargets`" path the Library uses).
- **Drops onto a view** are refused (no-drop cursor), like the Library (`AcceptsDrops`).
- **Fence menu on a view**: Sort by (Name / Type / Date) sets `View.Sort` (saved, live, checked in the menu); "Open
  folder"; "Folder view settings…"; none of the items-only commands (Add item…, Add from desktop…, item refresh or
  fix commands). Title,
  look, colour, tabs, roll-up, lock, delete fence work as for any fence. A view can be a tab of a box.
- **Deleting a view fence** never touches the folder (confirmation text says so).
- **Status line**: `FenceWindow.SetStatus(string?)` shows one line of text in the fence body when set:
  - unreadable folder → "Folder not available: `<path>`" (and no items);
  - over the cap → items, then "+ 1,234 more — Open folder" as the last line (a click opens the folder);
  - empty after filtering → "Nothing here matches this view" (empty folder: "This folder is empty").
- **Live updates**: `FolderLister` per view (see §4): changes re-list within 250 ms (bursts coalesced), off the UI
  thread; an unreadable folder retries every 7 s; a failing watcher backs off (`WatcherBackoff`); game mode pauses and
  re-lists once afterwards; Safely Remove releases the drive and the view says "not available" until it is back.
- **Renamed folder**: the parent watcher's rename of the folder itself updates `View.Path` (saved), the title too if it
  still equals the old name; moved elsewhere or deleted → "not available" until it is back at the same path.
- **Selection, scroll and the hovered item survive re-lists** (`SetItems` updates in place, as for the Library).

## 4. Units

- **Core**: `Model/FolderView.cs`, `Model/Fence.cs` (+`View`), `Items/FolderViews.cs`; config load guard (Library and
  View both set). Tests: `FolderViewsTests` (select order, show kinds, patterns incl. invalid and bare words, newest N
  then sort, cap and hidden count, defaults for Downloads/Screenshots/other), config round-trip with a view.
- **Shell**: `KnownFolders.TryGetPath` (+ `SHGetKnownFolderPath`, `FOLDERID_Downloads`, `FOLDERID_Screenshots` in
  `NativeMethods.txt`). Nothing else new: `FolderItems.TryList`, `FolderWatcher`, `DeviceRemovalNotice`, `PathPicker`.
- **App**:
  - `LibraryLister` → `FolderLister` (file renamed; `label` for log lines; optional `renamed(old, new)` callback from
    the parent watcher). The Library keeps using it.
  - `FenceHost.FolderViews.cs`: one lister per view fence (created when the fence shows, disposed when it goes, paused
    with game mode, released on removal notices like the Library's), `ShowView`, `NewFolderView`, `EditFolderView`,
    `AddToFence`, `ShowAsFolderView`, `OnViewFolderRenamed`.
  - The `window.IsLibrary` branches become a kind check: `window.Kind` (`Items` / `Library` / `View`) so the read-only
    paths (open by path, no Remove/Properties/Add, drops refused, drag-out of paths, Sort) cover views; the Library's
    own bits (art, hide game, its Windows menu) stay Library-only.
  - `FolderViewWindow.xaml(.cs)`: the settings dialog. `FenceWindow.SetStatus`.
  - Tray and fence menus: the new commands.
- **Removal notices**: the app's message window routes `DBT_DEVICEQUERYREMOVE` to every lister (`ReleaseForRemoval`
  returns true for the one that owns the handle), not just the Library's.

## 5. Failures

- Listing fails (missing, offline, denied) → "not available", retried every 7 s; logged once per view per outage.
- Watcher fails → re-list with backoff (existing); logged.
- Invalid pattern → the dialog refuses it; a bad pattern found in config (hand-edited) → treated as empty, logged.
- `SHGetKnownFolderPath` fails → no special defaults; logged.
- A view of a slow network share: listing is off the UI thread; the fence shows the previous listing (or nothing) until
  it arrives; dead shares stay "not available" without freezing anything.
- Many views: each holds two `FileSystemWatcher`s; no limit enforced (personal use; logged count at start).

## 6. Testing and release

- Core test-first (xUnit), whole suite green, 0 warnings.
- `TEST-CHECKLIST` section **AG**: create from tray and fence menu (Cancel creates nothing); Downloads defaults; Show
  Folders only on `D:\GameLibrary`; `*.png;*.jpg` filter; newest N; live add / rename / delete in Explorer; the folder
  itself renamed (view follows) and deleted/re-created; USB stick: pull → "not available" → plug → back; Safely Remove
  while shown; game mode pause; drag a file out to Explorer (copy); drag onto a normal fence (item); refused drop;
  Add to fence ▸; Show as folder view; cap line with a 600-file test folder on the pendrive's `NeoFences-test`; a view
  as a tab; delete the view fence (folder untouched); snapshot restore brings a view back.
- Live check by script with the installed copy's data and Run value backed up and restored; screenshots sent to the
  user as they are taken.
- Docs: ADR-044, FEATURES (Folder Portals row → "Folder views", done in 0.11.0), ARCHITECTURE, ROADMAP, SESSION-LOG,
  hub. Release **0.11.0** with the usual flow (push → CI → tag → draft → install check as an update from 0.10.1 →
  publish), each outward step asked.

## Out of scope

Auto-collect rules; browsing into subfolders inside the fence (subfolders open in Explorer, by choice); including
subfolders' files; dropping into a view; thumbnails beyond what `IconLoader` already gives; per-item names or icons in
views.
