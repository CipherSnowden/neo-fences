# M22 — One kind of fence: games become items (0.12.0) — design

**Date:** 2026-10-05 · **Status:** approved in brainstorm (sections 1–3), awaiting written-spec review
**Decisions:** ADR-045 (this milestone; refines ADR-032 Game Library and ADR-044 folder views)
**Background:** `docs/PIVOT-2026-10-04.md` (virtual items), M21 folder views (`research/m21-folder-views.md`)

## Goal and vision

The user's direction (2026-10-05): **there are no fence types to pick.** A fence is a container of *elements*; the
element's kind and its own settings decide how it looks. Today every virtual item kind already mixes in one fence
(folder, file, shortcut, program, Windows app, website). Later a fence also holds a folder shown in a detailed view,
widgets (clock, calendar), and elements of several sizes (1×1, 1×2, 2×1, 2×2). Fence kinds stay in the code ("maybe
later the types would be needed") but are not something the user chooses.

This milestone is the first step: **games become items.** The Game Library stops being a fence type and becomes the
engine that finds games; a game is an ordinary item that can sit in any fence, shown as a cover tile or an icon. Folder
views stay as they are (not decided yet). 0.12.0 is still a personal release.

Hard rules stand unchanged: NeoFences never modifies, moves, renames or deletes a user file (ADR-040); Win32/COM only in
`NeoFences.Shell` via CsWin32; no new NuGet dependency; failures degrade one feature, never crash.

Success: on the user's PC the "Games" fence turns into a normal fence holding the same 12 games as cover tiles, untouched
otherwise; a game can be dragged into "Apps" next to Discord and shown there as an icon; a newly installed game appears
in "Games" by itself; an uninstalled game dims and comes back when reinstalled.

## 1. Model (Core)

### Game items
- `VirtualItem` gains:
  - `GameId` (string?) — the scan's id of the game (`GameEntry.Id`, e.g. `steam:431960`); set only on game items.
  - `ShowAs` (`ItemShow { Default, Cover, Icon }`, default `Default`) — for game items `Default` means Cover; other items
    ignore it in this milestone.
- A game item's **target** is the shortcut NeoFences keeps for that game in its library folder
  (`%LOCALAPPDATA%\NeoFences\library\<game>.lnk` or `.url`). So opening, Missing / back, target watching, drag-out,
  Ctrl-duplicate, Properties and snapshots work exactly as for any item.
- items.json stays **schema 1** (optional fields).

### Settings
- `LibrarySettings.NewGamesFence` (string?, a fence id) — where newly installed games go; null = nowhere.

### `GameItems` (Core, pure, test-first)
- `Migrate(config, items, libraryState) → (config, items, migrated)`: for every fence with `IsLibrary`: its `IsLibrary`
  becomes false; its item list becomes one game item per `libraryState.Items` entry in that order (target = the library
  file, `GameId` = the game's id, name none — the shortcut's name shows); `NewGamesFence` = that fence when it was null.
  No library fence: unchanged (`migrated` false).
- `NewGames(previous, current) → games`: games in `current` whose id (any of `GameCatalog.IdsOf`) was in no game of
  `previous`.
- `AddNew(items, fenceId, games, libraryState)`: one item per game at the end of that fence; a game already in that fence
  (same `GameId`) is not added twice.
- `Retarget(items, libraryState)`: every game item points at its game's current library file (matched by `GameId`); a
  game no longer in the library keeps its target (it shows Missing once its file is gone).
- `IsGame(item)`, `ShowsCover(item)` helpers.

### Migration safety
- Runs at start (after loading config and items) and after a snapshot restore that brings back a library fence.
- First a snapshot **"Before games became items (<date time>)"** is saved; if it cannot be saved, nothing changes (logged)
  and migration is tried again at the next start.
- The library index (`library\index.json`) is read first; a library fence whose index cannot be read stays as it is.

### Kinds kept
- `Fence.IsLibrary`, `FenceKind.Library` and the library-fence code paths stay (unused: nothing creates such a fence).

## 2. User interface

- **Creating fences:** the tray and fence menus lose "New Game Library fence"; "New fence" and "New folder view…" stay.
- **Fence menu → "Add games…"** (items fences): a list of every game the scan found (cover thumbnail, name, source), with
  checkboxes; games already in this fence are listed unticked and marked "already here". Add puts the ticked ones at the
  end of the fence. Before the first scan finishes: "Looking for games…"; no games: "No games found — add your games
  folder in Settings → Game Library."
- **A game item:**
  - double-click / Enter starts the game (the shortcut, as today);
  - right-click: Open · Show as ▸ Cover tile / Icon · Open install folder · Copy path · Properties… · Remove from fence;
    Shift+right-click: Windows' menu for its shortcut (under the usual header line);
  - Properties: its own name, icon (used in Icon mode) and note; the target box is read-only for game items;
  - not installed (its shortcut is gone): dimmed with the ⚠ badge; tooltip and the open question say
    "<game> is not installed", with Remove from fence / Cancel (no Locate…).
- **Mixed fences:** cover tiles (2:3, like today's library) and icons share one flowing grid; each cell is as wide as its
  own kind (tile: 1.5 × icon size; icon: as now) and a row is as tall as its tallest cell. The fence's icon size scales
  both. Dragging a game item to another fence moves it; Ctrl+drag duplicates.
- **Settings → Game Library:** new line "New games go to: [fence ▾ / Nowhere]"; folders, sources, hidden games and
  Refresh stay. The status line keeps "Last scan HH:mm: N games".
- **The scan runs** while any game item exists or "New games go to" names a fence; otherwise it sleeps (no watchers).
  It still waits during games (game mode).

## 3. Units

- **Core:** `Items/VirtualItem.cs` (+`GameId`, `ShowAs`, `ItemShow`), `Library/GameItems.cs`, `Library/LibrarySettings`
  (+`NewGamesFence`), config normalizer (a `NewGamesFence` that names no items fence → null). Tests: `GameItemsTests`.
- **App:**
  - `FenceHost.Library.cs`: active while game items exist or `NewGamesFence` is set; after each scan → `Retarget`, then
    `NewGames` → `AddNew` into `NewGamesFence`; covers keyed by library file path for items.
  - `FenceHost.GameItems.cs`: migration at start / after restore, Add games…, the game item menu, Show as, the
    not-installed question.
  - `AddGamesWindow.xaml(.cs)`: the list (like `DesktopFillWindow`).
  - `FenceWindow` / `FenceItemView` / `ShownItem`: tile or icon decided per item (`ShownItem.Art`), cell width per item.
  - `SettingsWindow.Library`: "New games go to".
  - Menus: "New Game Library fence" removed from tray and fence menus.
- **Shell:** nothing new.

## 4. Failures

- Migration's safety snapshot fails → no migration, logged, retried next start.
- A scan fails → items stay as they are (the existing "keeps its games" behaviour).
- A cover that cannot be decoded → the item's icon instead (logged once per file).
- `NewGamesFence` deleted → the setting falls back to Nowhere (normalizer and Delete fence).
- A game item's library file missing → Missing state, as any item; never a crash.

## 5. Testing and release

- Core test-first (xUnit): migration (order, ids, targets, the new-games fence, no library fence, two library fences),
  new-game detection (merged ids, nothing new, first scan), `AddNew` de-dup, `Retarget` after a renamed shortcut,
  normalizer for `NewGamesFence`.
- `TEST-CHECKLIST` section **AH**: migration of the real "Games" fence (12 games, same order, covers; the safety snapshot
  in the tray list); a game dragged into "Apps" and shown as an icon; Show as Cover/Icon back and forth; Properties name;
  Add games… (already-here marking); a new game appearing (a test shortcut in `D:\GameLibrary`'s game folder → scan → new
  item in Games), removed again (dims) and restored (back); Settings → New games go to: Nowhere (no auto-add); snapshot
  restore of a pre-migration snapshot migrates again; the tray and fence menus have no "New Game Library fence".
- Live check by script on backed-up data (restored afterwards), screenshots sent to the user as they are taken.
- Docs: ADR-045, ARCHITECTURE, FEATURES, ROADMAP, SESSION-LOG, hub. Release **0.12.0** with the usual flow (push → CI →
  tag → draft → install check as an update from 0.11.0 → publish), each outward step asked.

## Out of scope

Folder views as elements (not decided); widgets; element sizes / grid layout; hiding games from the item menu (Settings
keeps the hidden list); games found by Add from desktop becoming game items (they stay desktop-shortcut items).
