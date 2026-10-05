# M26 — The folder panel element (0.15.0) — design

**Date:** 2026-10-05 · **Status:** approved in brainstorm (section 1; the user said "run it" for the rest), awaiting
written-spec review
**Decisions:** ADR-048 (this milestone; supersedes the fence-level part of ADR-044 folder views; continues ADR-045 one
kind of fence, ADR-046 element sizes, ADR-047 widgets as elements)

## Goal

The user's vision (ADR-045): a fence holds *elements*; the element's kind and settings decide its look. The last piece
named on 2026-10-05: **a folder shown inside a fence** as a list or detailed view, beside items and widgets. The user's
picks (2026-10-05):

- The panel **replaces** folder-view fences: one way to show a folder. Existing views migrate; `FenceKind.View` stays in
  code (unused by the UI), as the user asked for kinds.
- Looks per panel: **Details**, **List**, **Icons**.
- Double-clicking a subfolder **browses inside the panel** (Back / Up / Home); still read-only.
- Size: **cells 1×1–4×4, or "Fill fence"**.

Hard rules stand: NeoFences never modifies, moves, renames or deletes a user file (the panel is read-only, drops onto it
are refused); Win32/COM only in Shell via CsWin32; no new NuGet dependency; failures degrade one panel, never crash.
Gamer first: listers pause in game mode, as folder views do today.

Success: the user's Downloads view comes back after the update as a fence holding one filling panel that looks as
before; they add a Details panel of `D:\GameLibrary` to Games at 4×3, sort it by date with a header click, browse into a
game's folder and back; the panel greys to "Folder not available" while the pendrive it shows is out and returns by
itself.

## 1. Model and migration

- A panel **is a folder item** with panel settings: `Target` = the folder path (a normal path item), so missing-folder
  state, Locate…, the bulk fix, drives coming and going, copies and snapshots work as for any item.
- `VirtualItem.Panel: FolderPanel?` — null: a plain folder icon; set: the item renders as a panel.
  `record FolderPanel { PanelLook Look; ViewShow Show; string Patterns; int? Newest; PanelSort Sort; bool Descending }`.
  - `enum PanelLook { Details, List, Icons }`, default Details.
  - `enum PanelSort { Name, Date, Type, Size }`, default Name; `Descending` reverses it. A header click picks the key in
    its natural direction (Name and Type A→Z, Date newest first, Size biggest first); a second click reverses it.
    Name and Type keep folders first (as `ItemSorting` does); Date and Size mix them.
  - Show, Patterns, Newest and their limits are the folder view's (M21): `FolderViews.Select` and `Status` serve both.
- `VirtualItem.Fill: bool` — the panel takes the whole fence and resizes with it. Offered only while the panel is the
  fence's only element; adding anything else to that fence turns it back to cells (4×4). A Fill on a non-panel or on a
  panel that is not alone is ignored on load.
- Default span of a panel: **4×4**; any 1×1–4×4 allowed (ADR-046).
- items.json stays **schema 1**: `panel` and `fill` are written only when set. Older versions ignore them and show a
  plain folder item.
- **Migration on load** (once, idempotent): each fence with `View` gets one panel item in items.json — target the view's
  folder, `Look = Icons` (so it looks as before), `Fill = true`, Show / Patterns / Newest / Sort copied (Sort Name / Type /
  Date map to the same PanelSort; Date descending as today) — then `View` is cleared in config.json. Items are saved
  before config (the M22 safe-save order); a crash between the two leaves both, and the next load sees the panel already
  there and only clears `View`. The fence keeps its title, place, colours, tab and roll-up.
- **Browsing** state (the shown subfolder, the Back history) lives in memory only: each start, and Home, show the
  panel's own folder.

## 2. User interface

- **Creating a panel:**
  - Fence menu → **Add folder panel…**: Windows' folder dialog, then a panel at the end (Flow) or the first free 4×4 spot
    (Free; smaller if none fits, as `FenceGrid.PlaceDropped` does). Downloads and Screenshots start sorted by date with
    the newest 30 (`FolderViews.DefaultsFor`).
  - Tray **New folder panel…** (replaces New folder view…): a new fence, titled with the folder's name, holding one
    filling Details panel.
  - Folder item menu → **Show as folder panel**: that item becomes a panel in place (4×4). Panel menu → **Show as icon**
    turns it back (its panel settings are cleared).
- **Panel header** (one slim row): the shown folder's name; while browsing below its own folder, ◀ Back, ↑ Up and ⌂ Home
  buttons. Backspace = Back, Alt+Up = Up.
- **Looks:**
  - **Details**: rows of small icon, Name, Date modified, Type, Size (blank for folders); column headers sort. At 1–2
    cells wide only Name and Date show; Type and Size from 3 cells.
  - **List**: rows of small icon and name.
  - **Icons**: today's folder-view tiles (the fence's icon size and label mode).
- **Entries:** double-click a file opens it; a folder browses into it. Right-click: the folder view's safe menu (Open,
  Open file location, Copy path, Add to fence ▸); Shift+right-click: Windows' menu under the line saying it acts on the
  real files (M21). Ctrl / Shift multi-select. Drag out as copies; drops onto a panel are refused.
- **Panel menu** (right-click on the header or empty space): Look ▸ Details / List / Icons · Sort by ▸ · Panel settings…
  (the folder view settings dialog: folder, show, patterns, newest) · Open folder · Size ▸ (with **Fill fence** when
  alone) · Show as icon · Remove from fence. Missing folder: Locate… as for any item.
- **Status inside the panel** (M21 texts): "Folder not available: <path>" (back by itself), "This folder is empty",
  "Nothing here matches this view", "+ N more — Open folder" past 500 entries.
- **Scrolling:** the wheel over a panel scrolls the panel, not the fence.
- **Look:** the fence's text colour and font; selection and hover as items.

## 3. Units

- **Core** (test-first): `FolderPanel`, `PanelLook`, `PanelSort`; `VirtualItem.Panel`, `VirtualItem.Fill`;
  `FolderViews.Select` taking the panel's settings (folder + `FolderPanel`) with `Descending` and Size; `ItemInfo` gains
  `long? Size`; `FolderPanels` (`Create(path, busyFolders)`, `FromView(FolderView)`, `Normalize`, `HeaderSort(current,
  clicked)`, `Columns(span)`); `PanelMigration.Migrate(config, items) → (config, items, migrated)`; `FenceGrid` treats a
  lone Fill panel as the whole fence; `ItemEdits` adding an element to a fence with a Fill panel clears Fill.
- **Shell:** `FolderItems` listing reports file sizes (already enumerated).
- **App:** `FolderPanelView` (a control in the item template for panel elements: header, Details as a virtualized
  `ListView`/`GridView`, List, Icons); `FenceHost.FolderPanels.cs` replacing `FenceHost.FolderViews.cs` (one
  `FolderLister` per panel item on its shown folder, off-thread selection as M23, rename-follow retargets the item,
  game-mode pause, Safely Remove); menus above; `FolderViewWindow` reused as Panel settings; `FenceGridPanel` Fill.
  Fence-level view code paths go (the migration guarantees no `View` remains), `FenceKind.View` and `Fence.View` stay.

## 4. Failures and performance

- A folder that cannot be read shows the status inside the panel and retries as today (7 s); the rest of the fence works.
- Listing, filtering and sorting stay off the UI thread; Details and List virtualize rows; icons load for visible rows
  only (`IconLoader`). At most 500 entries shown.
- Migration failure (the items save throws): logged; config keeps `View`, the fence shows empty this run, and the next
  start retries (config is cleared only after items are saved).
- Nothing lists while in game mode (paused listers), as M21.

## 5. Testing and release

- Core xUnit: migration (each field, idempotent, crash between saves), Normalize, header-sort clicks, columns by width,
  Select with Descending and Size, Fill rules (alone only, cleared on add, ignored when invalid), Create defaults.
- `TEST-CHECKLIST` section **AL**: migrated Downloads view looks as before; add a panel in Flow and Free; each look;
  header sort; browse in / Back / Up / Home / Backspace; Fill on, add an item, Fill off; Show as folder panel / Show as
  icon; pendrive out and back (Safely Remove while shown); drag out, Add to fence, drop refused; game mode pause; CPU
  idle with panels shown.
- Prototype first on a copy of the user's data, then the plan with replay-verified patches; live check by script on
  backed-up data; screenshots sent; merged locally (until 1.0). Docs: ADR-048, ARCHITECTURE, FEATURES, ROADMAP,
  SESSION-LOG, hub. Release **0.15.0**, asked first.

## Out of scope

Writing to the folder (drops, rename, delete from NeoFences' own menu); a search box; column resizing or choosing;
thumbnails; a tree view; remembered browsing; auto-collect rules.
