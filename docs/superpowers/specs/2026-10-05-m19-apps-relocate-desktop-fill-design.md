# M19 — Store apps, bulk fix, Add from desktop, reliability (0.10.0) — design

**Date:** 2026-10-05 · **Status:** approved in brainstorm (sections 1–4), awaiting written-spec review
**Decisions:** ADR-042 (this milestone), ADR-043 (the name stays NeoFences; supersedes ADR-009)
**Background:** `docs/PIVOT-2026-10-04.md`, M18 results and deferred minors in `docs/research/m18-virtual-items.md`

## Goal

0.10.0 is still a personal release (not for friends; no code signing). It makes the virtual-items model complete for
daily use:

1. **Store apps (and every Start menu app) as items** — chosen from a list or dragged from Start.
2. **Bulk fix of missing items** — after one Locate…, the other items from the same old place are offered at once,
   with an undo snapshot.
3. **Add from desktop…** — a sorting dialog that turns desktop items into items in fences in one step.
4. **The M18 reliability leftovers** — no fence stalls on a dead network path, no leaks, no silent watcher loss.

Hard rules stand unchanged: no NeoFences action touches a user file (ADR-040); every new Win32/COM call lives in
`NeoFences.Shell` via CsWin32; no new NuGet dependency.

Success: the user's 30 desktop items land in fences in one dialog; Store apps open, show their name and icon and turn
Missing when uninstalled; a moved games drive is fixed in two clicks and can be undone; no fence freezes on a dead
share.

## 1. Store apps as items

### Target form
- An app item's target is `shell:AppsFolder\<AppUserModelID>` (e.g.
  `shell:AppsFolder\SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify`). It is the same entry Start's All apps shows, so it
  covers Store apps and normal programs alike.
- `ItemKinds.Of` already classifies `shell:…` as `Special`. New Core helper `ItemKinds.IsApp(target)` (prefix
  `shell:AppsFolder\`, case-insensitive, non-empty id) and `ItemKinds.AppTarget(appId)` to build one. Test-first.
- Opening: unchanged — `ShellItems.TryOpen` already starts `shell:` targets through `explorer.exe` (the Game Library
  starts Store games the same way). Arguments and Run as administrator do not apply to app items: Properties greys
  them out (`TakesArguments` already returns false for non-Path kinds).

### Name, icon, state
- Name and icon come from `SHCreateItemFromParsingName("shell:AppsFolder\…")` through the existing
  `TryGetDisplayName` / `TryGetImage`. The prototype confirms this parses on this PC; if a plain `shell:AppsFolder\`
  string does not parse, Shell resolves the AppsFolder item through `SHGetKnownFolderItem(FOLDERID_AppsFolder)` +
  `IShellFolder.ParseDisplayName(appId)` instead (same public behaviour).
- State: special items are always Ok today. App items are checked: the target parses → **Ok**; it does not (the app was
  uninstalled) → **Missing**, with the usual dimming, badge and Locate… / Remove. `TargetChecks.Classify` keeps Core pure:
  it returns a new "needs the app check" answer for app targets, and `TargetProbe.CheckAll` (Shell, off the UI thread)
  does the parse. No folder is watched for an app; it is checked at start, every 5 minutes, on Refresh and when a fence
  is shown — like any other target.
- Locate… on a missing app opens the app list (§1 "Choose an app…") instead of the file dialog.

### "An app…" — the app list
- Add item… / Properties → **Browse ▾** gains **"An app…"**. It opens `AppPickerWindow`: every app in Start's All apps
  with its icon and name, a search box (name contains, case-insensitive), sorted A–Z. Double-click or OK fills the
  target (`shell:AppsFolder\<id>`) and, when the name box is empty, the name.
- Shell: `AppList.Enumerate()` lists `FOLDERID_AppsFolder` through `BHID_EnumItems` / `IEnumShellItems` on its own STA
  thread and returns `(AppId, DisplayName)` pairs; icons load through the existing `IconLoader`. The window opens at
  once with "Loading apps…" and fills when the list arrives. A failure shows "Windows did not list the apps" and logs.
- A normal program picked here is stored as an app entry too. For arguments or Run as administrator the user browses to
  its `.exe` as today.

### Dragging from Start
- `ShellDragDrop.ShellItemTargets` keeps nested AppsFolder items: an item whose desktop-absolute parsing name is
  `::{4234d49b-0245-4df3-b780-3893943456e1}\<id>` becomes `shell:AppsFolder\<id>`. Other nested virtual items (zip
  contents, phones, Control Panel pages) are still skipped.
- Whether Start's All apps hands app entries to a drop target is the prototype's first check. If it does not, the list
  alone is the way in, and this is noted in FEATURES.

### Bindings (`NativeMethods.txt`)
`SHGetKnownFolderItem`, `FOLDERID_AppsFolder`, `BHID_EnumItems`, `IEnumShellItems` (plus `KNOWNFOLDERID`/`KF_FLAGS` as
CsWin32 needs them).

## 2. Bulk fix of missing items

### When it is offered
After Locate… re-points one **Missing or Unavailable** item from `oldTarget` to `newTarget`:
- `Relocation.Find(oldTarget, newTarget)` (Core, test-first) compares the two paths segment by segment **from the end**
  and returns `(OldBase, NewBase)` — the differing leading parts — or null when the last segment (the file or folder
  name) differs or either path has no root. Examples: `D:\Games\Crysis 2\Crysis2.exe` → `E:\Games\Crysis 2\Crysis2.exe`
  gives `D:\` → `E:\`; `D:\Games\X\x.exe` → `D:\MyGames\X\x.exe` gives `D:\Games` → `D:\MyGames`. Comparison is
  case-insensitive; trailing separators are ignored.
- `Relocation.Candidates(items, states, oldBase, newBase)` lists every **other** Path item, in every fence, whose state is
  Missing or Unavailable and whose target is `OldBase` or starts with `OldBase\`, with its proposed new target
  (`NewBase` + the rest). Website, special and app items are never candidates.
- The candidates' new targets are checked off the UI thread (`TargetProbe.CheckAll`, shares time out after 2 s); only
  those that exist are offered. None left → no question.

### The question and the fix
- `RelocateWindow` (MissingItemWindow style, owned by the fence): "Fix N more items? They were in `OldBase` and are now in
  `NewBase`." with up to five item labels and "… and K more", buttons **Fix** and **Not now**.
- **Fix:** first a snapshot is saved in the existing "before restore" slot, named `Before fixing N items (<date time>)`;
  if that save fails, nothing is changed and the failure is shown (as for a restore). Then `ItemEdits.Relocate(document,
  moves)` (Core, test-first; `moves` = item id → new target) changes only those items' targets — name, icon, arguments,
  note and order stay — in one edit and one save, followed by a check of the moved targets.
- The tray's **"Undo the last restore"** becomes **"Undo the last restore or fix"**: it restores that snapshot exactly
  as today. There is still one undo slot (a later restore or fix replaces it).

## 3. Add from desktop…

### Entry points
Fence menu → **"Add from desktop…"** and tray → **"Add from desktop…"** open the same dialog (same defaults from either
place). One window at a time.

### What is listed
`DesktopItems.Enumerate()` (existing): the visible Windows icons (`::{CLSID}`: Recycle Bin, This PC…), then the user
and Public Desktop entries. Listing and sorting run off the UI thread (`.lnk`/`.url` read with the existing
`ShellLinks.Read`); the window opens at once with "Reading your desktop…".

### Groups
`DesktopSorting.GroupOf(entry)` (Core, test-first) — `entry` = path, is-folder, is-special, shortcut target and
arguments (null when not a shortcut) — returns one of:
- **Games:** a shortcut or `.url` that the Game Library counts as a game — `GameLaunchers.LauncherOf($"{target}
  {arguments}")` is not null, or the target sits under one of the Game Library's game folders (`library.folders`).
- **Apps:** any other shortcut to a program (`.exe`, `shell:AppsFolder\…`, a target without a file path).
- **Web links:** a `.url` whose target is an http(s) address.
- **Folders and files:** everything else — folders, documents, other shortcuts' targets that are files or folders,
  and the Windows icons.

### The dialog (`DesktopFillWindow`)
- One section per non-empty group: a header with the group name, the count and **"Put in:"** — the user's fences by title
  (not the Game Library), **"New fence: <group name>"** (default when no fence has that title; otherwise that fence),
  or **"Skip"**. Below: one row per item — tick box, icon (`IconLoader`), name.
- An entry that is already the target of an item in any fence is shown **unticked** with "already in <fence title>";
  everything else starts ticked. Running the dialog again therefore adds only what is new.
- An unticked option at the bottom: **"Hide desktop icons while NeoFences runs"** (the existing setting).
- **Add** (enabled when at least one ticked row goes to a fence): creates the needed new fences (placed with the existing
  free-spot placement, like New fence), adds every ticked entry to its group's fence with `ItemEdits.Add` (same-fence
  duplicates skipped), applies the hide option if ticked, saves once, logs counts. **Cancel** changes nothing.

### What an item points at
The desktop entry itself (the `.lnk`, `.url`, folder, file or `::{CLSID}`) — exactly what a drag from the desktop makes
today. The shortcut keeps its own icon, arguments and start folder. Deleting the desktop shortcut later makes the item
Missing; a clean desktop comes from "Hide desktop icons", never from deleting.

## 4. Reliability leftovers (M18 deferred minors)

| # | Today | 0.10.0 |
|---|---|---|
| R1 | A watcher still being armed cannot be released when Windows asks to remove its drive (`_armingFolders`; only `_targetWatchers` is searched). | Removal notices registered by an arming batch are tracked by handle (as `LibraryLister._inFlight`); `ReleaseTargetWatcherForRemoval` releases them too and the batch's continuation drops released folders. |
| R2 | UI-thread shell calls that can stall on an unreachable target: Shift+right-click menu (`ShellItemMenu`/`DesktopNamespace`), Properties' Browse and icon-picker start paths, Locate…'s start folder parse, drag-out `SHParseDisplayName`. | Each first runs `TargetProbe.Check` on the target's root off the UI thread (2 s limit). Unreachable: the menu shows "Network location not reachable" instead of Windows' menu; pickers start in their default folder; drag-out leaves that target out (logged). |
| R3 | `PathPicker` never releases the `IShellItem` from `GetResult`; `DesktopNamespace` never releases the desktop folder, the folder object and the returned `IShellFolder`. | Released (`Marshal.ReleaseComObject` in `finally`). |
| R4 | A faulted arming batch throws in its continuation; its folders stay in `_armingFolders` and are never watched again. | `IsFaulted` checked: logged once, the batch's folders leave `_armingFolders`, the next `UpdateWatching` retries them. |
| R5 | `_targetChecks`, `_checkBatchOf`, `_fenceRefreshTimers` only grow. | After an items change and after Delete fence, entries for targets and fences that no longer exist are dropped (timers stopped). Core helper for the "which keys are gone" part, test-first. |
| R6 | Properties OK while "Checking…" uses the previous file-or-folder answer. | OK (and Add) is disabled while a check runs (≤ 2 s). |
| R7 | Icon loaders call the shell on dead-share targets and can block both workers. | For network targets the loader first runs `TargetProbe.Check` (2 s); unreachable → the generic icon (R8), no shell call. |
| R8 | A target with no shell icon (missing, unreachable) shows no icon at all. | The generic icon for its type — `SHGetFileInfo` with `SHGFI_USEFILEATTRIBUTES` (by extension, folder icon for folders; no disk access). A website whose browser icon fails to load is logged once (the unreproduced blank github.com icon). |

`SHGetFileInfo` (and its flags) join `NativeMethods.txt`.

## Error handling

Every new shell call is logged and degrades only its feature (hard rule 7): an app list that fails to load says so;
an app that does not parse is Missing, never a crash; a relocation snapshot that cannot be saved cancels the fix; a
desktop entry that cannot be read is listed under Folders and files with its file name. Nothing in this milestone
writes outside `%LOCALAPPDATA%\NeoFences\`.

## Testing

- **Core (xUnit, test-first):** `ItemKinds.IsApp/AppTarget`; `TargetChecks` app answer; `Relocation.Find` (drive letter,
  folder rename, renamed file → null, UNC, case, trailing separators, root-only); `Relocation.Candidates` (states, kinds,
  prefix boundary `D:\Game` vs `D:\Games`, the located item itself excluded); `ItemEdits.Relocate` (only targets change,
  order kept, unknown ids ignored); `DesktopSorting.GroupOf` (steam/epic links, game-folder shortcut, exe shortcut,
  AppsFolder shortcut, http `.url`, folder, document, special icon); the stale-key helper (R5).
- **Prototype before the plan:** Start drag hands over AppsFolder items or not; `shell:AppsFolder\…` parses with name
  and icon; AppsFolder enumeration time on this PC; `SHGetFileInfo` generic icons; the relocation question on a real
  missing item.
- **Live check:** new TEST-CHECKLIST section **AE** — app from the list, app dragged from Start, uninstalled app →
  Missing → Locate… opens the list; bulk fix after moving a test folder (and undo from the tray); Add from desktop on
  the real desktop (groups, already-in rows, re-run adds nothing, hide option); R2 with an unreachable share (Shift+
  right-click, Properties Browse, drag-out), R1 by pulling the stick right after start, R6, R8 icons. Plus the AD rows
  this milestone touches (AD8 drag-out, AD15–AD18 missing flow, AD26 dead share).
- Final whole-branch review on Opus, then the live check with the user (physical steps only), as in M18.

## Release

0.10.0 (`<Version>` bump), same path as 0.9.0: push main → CI → tag `v0.10.0` → draft → install check (an update from
the installed 0.9.0 this time: Settings → Updates or restart) → publish; every outward step asked. Docs: ADR-042,
ADR-043, ARCHITECTURE, FEATURES, ROADMAP, TEST-CHECKLIST AE, SESSION-LOG, hub.

## Out of scope (later)

Friends release work (1.0.0: first-run guide, README for users, code signing); dynamic collections (M20); several undo
levels; relocation of website or app items; resolving desktop shortcuts into their targets (rejected: loses the start
folder and some shortcuts cannot be copied).
