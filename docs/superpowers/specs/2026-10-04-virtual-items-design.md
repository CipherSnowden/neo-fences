# M18 — Virtual items (0.9.0) — design

**Date:** 2026-10-04 · **Status:** approved in brainstorm (sections 1–5), awaiting written-spec review
**Decisions:** ADR-040 (pivot), ADR-041 (items in their own file) · **Background:** `docs/PIVOT-2026-10-04.md`,
code map `docs/research/m18-code-map.md`

## Goal

Fences hold **virtual items**: NeoFences records that point at a target and carry their own name, icon, arguments and
note. Nothing NeoFences does touches a real file (hard rule 1). Native desktop icons stay visible unless the user switches
on "Hide desktop icons while NeoFences runs". The parked model (Takeover, Inbox, desktop membership, Rules, Portals, item
file actions) is removed from the code.

## 1. Item model and storage

### Files (`%LOCALAPPDATA%\NeoFences\`)
- `config.json` — fences (title, position, size, tabs, colours, roll-up, lock, label mode, icon size, sort), settings,
  appearance. **Schema 5.** Removed from the model: `Fence.Items`, `FenceSourceKind.Desktop`/`Portal` (only `Library`
  remains; a normal fence has no source), `Fence.IsInbox`, the Inbox in `NeoFencesConfig`, `Rules`,
  `Settings.Takeover`/`TakeoverPromptAnswered`. A config with schema < 5 is not migrated: it is backed up as
  `pre-schema-5-config.json` and NeoFences starts fresh (nothing older is installed anywhere).
- `items.json` — `{ "schema": 1, "fences": { "<fenceId>": [ VirtualItem, … ] } }`. Each tab is already its own fence,
  so lists are keyed by fence id.
- `icons\<itemId>.png` — custom images chosen by the user, copied in (NeoFences' own data, not a user file).
- `backups\` — rolling backups of both files. Snapshots store and restore both files together.

Both files are written with the existing safe save (`ConfigStore` pattern: write `.tmp`, `File.Replace` with a backup,
no `.tmp` left behind on failure). A damaged file falls back to its backup, then to empty; the other file is unaffected.

### `VirtualItem`
| Field | Type | Meaning |
|---|---|---|
| `Id` | string (GUID "N") | unique per item; the same target may appear in many items |
| `Target` | string | file/folder path, `::{GUID}` special item (also `shell:` style parsing names as today), or `http(s)://` URL |
| `Name` | string? | null/empty = Windows' display name for the target |
| `Icon` | `ItemIcon?` | null = the target's icon; `{ File, Index }` (.ico/.exe/.dll) or `{ Image }` (file name in `icons\`) |
| `Arguments` | string? | files/apps only |
| `RunAsAdmin` | bool | files/apps only |
| `Note` | string? | tooltip |

Kind is derived, never stored: `ItemKinds.Of(target)` → `File`, `Folder` (decided at check time), `Website`, `Special`.
State (OK / Missing / Unavailable) is live, never stored.

### Two-file consistency (power cuts)
- A fence with no entry in `items.json` shows empty.
- Item lists for fence ids not in the config are logged, kept in memory untouched, and dropped at the next items save.
- Deleting a fence saves the config first, then the items: a crash in between leaves only orphan items, never a fence
  that lost its items.
- Unused images in `icons\` are deleted at startup (only files NeoFences put there).

### Code shape
- **Core** (pure, test-first): `VirtualItem`, `ItemIcon`, `ItemKinds`, `ItemStore` (load/save/fallback), `ItemsDocument`
  (fence id → list; add/move/duplicate/remove/retarget-all), `DropPlan`, `WatchPlan` (folder budget), `RefreshThrottle`.
- **Shell**: display name + icon (existing `ShellItems`), launch (args, `runas`), folder watching (existing
  `FolderWatcher`, `DeviceRemovalNotice`), icon picker (`PickIconDlg`), Windows item menu (existing `ShellItemMenu`).
- **App**: UI only.

## 2. Adding and moving items

- **Fence menu → "Add item…"**: a small dialog — Target box with **Browse file…** (any file, .exe, .lnk) and
  **Browse folder…**, or a typed URL; optional Name; OK appends the item.
- **Drop from Explorer / the Windows desktop** (CF_HDROP): each path becomes an item at the drop point. The drop target
  reports **DROPEFFECT_LINK only** — never MOVE — so Windows never moves or deletes the source; the cursor shows the link
  arrow. (Ends bug K5.)
- **Drop from a browser** (`UniformResourceLocatorW`, or text that is one `http(s)` URL): a website item.
- **Same target already in the destination fence** (plain drop or Add item…): not added; the existing item flashes.
  Other fences: allowed.
- **Inside a fence**: drag reorders (manual sort only). **To another fence**: moves the item (all fields kept).
  **Ctrl+drag**: duplicates (new `Id`), any destination including the same fence. Multi-selection everywhere.
- **Drag out** to other apps: CF_HDROP of the target with **DROPEFFECT_COPY | LINK** only (no MOVE). Websites drag out as
  a URL. Special and missing items: no drag-out.
- **Not in M18:** Store/UWP apps (not files; need shell item lists and AppsFolder launch). Normal apps work via their .exe
  or Start-menu shortcut.

## 3. Item menu, Properties, opening

**Right-click (NeoFences' menu):** Open · Run as administrator (files/apps) · Open file location (Explorer with the
target selected) · Copy path · Properties… · Remove from fence (Del) · grey hint "Shift+right-click: Windows' menu".
Missing item: **Locate…** and **Remove** at the top. Multi-select: Open, Remove.

**Shift+right-click:** Windows' full menu for the real target, with a disabled first line
**"Windows menu — acts on the real file"** inserted above Windows' entries.

**Properties dialog:** Name (placeholder = Windows' name) · Target + Browse… + live status (Found / Missing / Drive not
connected) · Arguments · Run as administrator (both grey for folders, websites, special items) · Icon preview +
**Change icon ▾** (From a file… = Windows icon picker over .ico/.exe/.dll; From an image… = PNG/JPG/ICO, copied to
`icons\<id>.png`, scaled to at most 256 px; Reset to the target's icon) · Note · OK / Cancel. Only the item changes.

**Opening:** ShellExecute with the target, the item's arguments, verb `runas` when set, working folder = the target's
folder; websites in the default browser; `::{GUID}` as today. A missing target shows NeoFences' "Missing — Locate… /
Remove" prompt instead of a Windows error. Launch failures (including a declined UAC prompt) are logged, never crash.

**Keys:** Enter open · Del remove (one item: no prompt; several: one confirmation) · F2 Properties with Name selected ·
Alt+Enter Properties.

**Labels and icons:** a set `Name` always wins over Windows' display name; a set `Icon` wins over the target's icon
(`IconLoader` changed accordingly). A label with no `Name` follows the target's current Windows name.

## 4. Target watching

| State | Look | Tooltip |
|---|---|---|
| OK | normal | note, if any |
| Missing | dimmed + small ⚠ badge | "Missing: <path>" |
| Unavailable | dimmed, no badge | "Drive G: is not connected" / "Network location not reachable" |

Websites and special items are always OK and not watched. Missing/Unavailable items stay until removed and return to OK
by themselves (file restored, drive plugged back in — `DeviceRemovalNotice`).

- **Watch budget:** targets grouped by parent folder; **at most 64 folders** watched (existing `FolderWatcher` +
  backoff), folders with the most items first, ties by fence order.
- **Rename in place** (same folder): every item with that exact target, in every fence, is re-targeted. A watched folder
  that is itself a target follows the same way when its parent is watched.
- **Deleted / moved elsewhere / parent folder renamed:** Missing. No move guessing; **Locate…** opens a picker at the
  nearest existing ancestor and replaces only `Target`.
- **Batching:** events coalesce; each fence refreshes at most **once per 2 s**.
- **Unwatched items** are re-checked when their fence becomes visible (unrolled, tab switched, Peek), every **5 min**,
  and on **fence menu → Refresh** (re-checks every item, reloads names and icons).
- Existence checks run off the UI thread; network paths get a **2 s timeout** (timeout = Unavailable).
- **Game mode** pauses re-checks and defers watch events until it ends.
- **Startup:** fences appear at once; states, names and icons fill in as checks finish.
- A watcher error/overflow re-checks that folder's items.

## 5. Hide icons, first run, removals, Library

- **Settings → "Hide desktop icons while NeoFences runs"** (off by default): existing `SetIconsHidden` + watchdog; the
  takeover marker becomes the hide-icons marker. Restored on exit, crash, Task Manager kill, Explorer restart. The desktop
  double-click quick-hide of fences stays.
- **First run:** one empty fence with the hint "Drop files, folders or links here — or right-click → Add item…". No
  auto-fill. Game Library fence unchanged.
- **Removed** (per the code map): Takeover + prompt, Inbox, desktop membership/reconcile, `DesktopWatcher`, `DesktopChange`,
  `RememberedPlacement`, Rules (+ Settings section; `LauncherOf` moves to the Library code), `FileNames`, Portals
  (browse/back/breadcrumb), `ShellFileOps`, `ItemFactsReader`, in-place rename, Recycle, drop-into-folder, the forwarding
  `FenceDropTarget`, menu texts "New Portal fence…", "Open folder in Explorer", "Rules for this fence…", "Delete fence
  (items go to the Inbox)", and the ~67 tests of that code. `ConfigNormalizer` loses Inbox/Portal/Rules repair and the
  global one-fence-per-item dedup.
- **Game Library:** `PortalState` shrinks to a Library-only lister; the Desktop-shortcut scan (`DesktopItems.Enumerate`)
  stays; the live Desktop-change trigger is dropped — new Desktop game shortcuts appear at the next scan (startup or
  Rescan).
- **Snapshots:** save/restore both files as saved (no desktop listing).

## Error handling

Every shell call (display name, icon, launch, watch, icon picker, Windows menu) is caught, logged and degrades that item
or feature only (hard rule 7). A file that cannot be read (damaged JSON) falls back to its backup, then empty, and the
user's other file is never overwritten because of it.

## Testing

- **Core (xUnit, test-first):** `ItemKinds.Of`; `ItemStore` round-trip, damaged file → backup → empty, no `.tmp` left;
  two-file rules (orphans kept then dropped, missing entry = empty fence); `ItemsDocument` add/same-fence skip/move/
  Ctrl-duplicate/remove/retarget-all-fences; `DropPlan`; `WatchPlan` (64 cap, most-items-first); `RefreshThrottle`
  (injected clock); schema < 5 config → backup + fresh; normalizer without Inbox/Portal/Rules.
- **Hand/live checks (new `docs/TEST-CHECKLIST.md` section):** Explorer drop leaves the source in place (compare with
  `cmp`); drag-out produces a copy; Missing/Unavailable with the pendrive `G:\NeoFences-test` (unplug/replug); rename
  follow; Locate…; Properties fields (args, run as admin, both icon sources, reset); hide icons + Task Manager kill →
  icons return; Shift+right-click label; Game Library still lists games.

## Release

0.9.0 after review and the hand checks: bump `<Version>`, push main → CI green → tag → draft → install check → publish,
each outward step asked for. Docs updated with the code: ARCHITECTURE, FEATURES, TEST-CHECKLIST, ROADMAP, ADR-041, hub.

## Out of scope (later)

Store/UWP apps; bulk re-locate and Ctrl-duplicate polish (M19); dynamic collections — folder views, auto-collect rules
(M20); the search palette; code signing.
