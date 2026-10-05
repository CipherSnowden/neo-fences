# M27 — Auto-collect rules (0.16.0) — design

**Date:** 2026-10-05 · **Status:** approved in brainstorm (behaviour summary), awaiting written-spec review
**Decisions:** ADR-049 (this milestone; the other half of the "dynamic collections" parked in ADR-040, after folder views
/ panels ADR-044 and ADR-048)

## Goal

Fences gather matching items by themselves. The user's desktop icons are hidden, so a shortcut an installer drops on the
desktop is invisible today; a rule puts an item for it in the user's Apps fence. The same works for any folder (Downloads,
a project folder). The user chose this milestone; the defaults below were proposed and approved ("Yes").

Hard rules stand: **NeoFences never moves, renames or deletes a file** — a rule only adds an item pointing at the file;
Win32 only in Shell via CsWin32; no new NuGet dependency; failures degrade one rule, never crash. Gamer first: nothing is
collected during a game; it catches up afterwards.

Success: the user adds a rule "Desktop → Apps and shortcuts" to Apps; installing a game launcher puts its desktop shortcut
into Apps within a second; removing that item from Apps does not bring it back; a rule "Downloads → Installers" on a Setup
fence collects each new installer; a rule made over a folder that already has matches asks "Add these N too?".

## 1. Model

- A fence holds its rules: `Fence.Collect: IReadOnlyList<CollectRule>` (config.json, written only when non-empty; config
  schema stays 5).
- `record CollectRule { string Id; string Source; CollectKinds Kinds; string Patterns; DateTimeOffset? Watermark }`
  - `Source`: a full folder path, or `"desktop"` — the user's Desktop and the Public Desktop (what Explorer shows).
  - `[Flags] enum CollectKinds { None = 0, Apps = 1, Documents = 2, Pictures = 4, Archives = 8, Installers = 16, Anything = 32 }`;
    `Patterns` (`*.png;*.pdf`, the folder-view syntax) adds matches. A rule with no kinds and no patterns matches nothing.
  - `Watermark`: when the rule last looked at its folder (start catch-up, below); null until its first look.
- Kinds by file name (extension; case ignored):
  - Apps: `.lnk .url .appref-ms .exe` (an `.exe` that is an Installer is not an App);
  - Documents: `.pdf .doc .docx .xls .xlsx .ppt .pptx .odt .ods .odp .rtf .txt .md .csv`;
  - Pictures: `.png .jpg .jpeg .gif .bmp .webp .heic .tif .tiff .svg`;
  - Archives: `.zip .7z .rar .tar .gz .tgz .bz2 .xz`;
  - Installers: `.msi .msix .msixbundle .appx .appxbundle`, and `.exe` whose name contains "setup", "install" or
    "installer";
  - Anything: every file and folder.
  - Hidden and system entries (as Explorer hides them) are never collected; folders only by Anything or a pattern.

## 2. Behaviour

- **New arrivals:** a watcher per rule source (one per distinct folder; the desktop = two folders) reports entries
  created or renamed into the folder. After a 1 s settle (a download renaming `.crdownload` → `.zip`, the item watcher
  following a rename), each arrival is matched against every rule of that source in fence order; the **first** matching
  rule's fence gets an item (end of the fence; Free fences: the first free spot).
- **No duplicates:** an arrival that **any** fence already holds an item for (same target) is skipped — a collected item
  the user moved to another fence stays there.
- **Removed stays removed:** only arrivals are collected; removing an item never brings it back unless the file is deleted
  and created again (a new arrival).
- **Start catch-up:** at start (and when a rule's folder comes back), entries whose creation time is after the rule's
  `Watermark` count as arrivals (things installed while NeoFences was not running). Then `Watermark` = the time of that
  look. (A file *moved* in while NeoFences was off keeps its old creation time and is not caught up: accepted.)
- **New rule:** the rule dialog counts the entries that match now and offers **"Add these N too?"** (default: No) — Yes
  adds them once; the rule's `Watermark` starts at its creation either way.
- **Game mode / Pause:** arrivals queue (paths only) and are collected when the game ends or NeoFences resumes.
- **Missing:** a collected item whose file is later deleted shows Missing like any item (no auto-removal).
- **Folder not available** (a pendrive): the rule waits; its watcher retries like folder panels (7 s), releases the drive
  on Safely Remove, and catches up when it is back.

## 3. User interface

- Fence menu → **Auto-collect…** (items fences): the fence's rules — a list (source · kinds), Add, Edit, Remove.
- Rule editor: **Source** (Desktop / Downloads / Choose folder…), **Collect** (checkboxes: Apps and shortcuts, Documents,
  Pictures, Archives, Installers, Anything), **Also these patterns** (text, validated as folder panels' patterns), a live
  line "N items here match now", OK / Cancel. On OK for a new rule with N > 0: "Add these N too?" (Yes / No).
- A fence with rules shows a small "collects" mark in its menu entry ("Auto-collect… (2 rules)"); no other UI.
- Log lines: rule added/removed, each collected item (rule, fence, path), skips (already held).

## 4. Units

- **Core** (test-first): `CollectRule`, `CollectKinds`, `CollectRules.KindOf(name, isFolder)`, `Matches(rule, name,
  isFolder)`, `Route(arrival, fences-with-rules in order, items) → (fenceId, ruleId)?` (first matching rule; null when any
  fence holds the target), `CatchUp(entries with creation times, watermark)`, `Normalize` (config repair: ids, kinds,
  patterns, sources), `FenceEdits.SetCollect(config, fenceId, rules)`; `ItemInfo` gains `Created` (creation time).
- **Shell:** `FolderItems` reports creation times; the existing `FolderWatcher` (names only) and `DeviceRemovalNotice`.
- **App:** `FenceHost.Collect.cs` (watchers per source folder, the 1 s settle queue, routing, game-mode queue, catch-up,
  removal release), `AutoCollectWindow` (list + rule editor), the fence menu entry.

## 5. Failures and performance

- A source that cannot be watched: logged once per outage, retried (7 s); other rules work.
- Bursts (an unzip of 5,000 files into a watched folder): arrivals are coalesced per 1 s; at most 200 items are added per
  rule per burst, the rest logged as skipped ("too many at once"). Nothing is collected from subfolders.
- No timers while there are no rules; one watcher per distinct folder.

## 6. Testing and release

- Core xUnit: kinds by extension (installer vs app `.exe`, case), patterns, hidden entries, folders only by Anything or a
  pattern, routing order and skip-when-held, catch-up by watermark, normalize, the burst cap, config round trip.
- `TEST-CHECKLIST` section **AM**: Desktop → Apps rule then a new shortcut on the desktop; removal stays removed; Downloads
  → Installers; "Add these N too?"; a rule on the pendrive (out, in, Safely Remove); game mode queue; catch-up after
  NeoFences was closed; two fences' rules on the same source (first wins); a burst of files.
- Prototype first, then the plan with replay-verified patches; live check by script **asked first** (the user is at the
  PC); screenshots sent; merged locally (until 1.0). Docs: ADR-049, ARCHITECTURE, FEATURES, ROADMAP, SESSION-LOG, hub.
  Release **0.16.0**, asked first.

## Out of scope

Start-menu apps as a source; subfolders; auto-removal of items whose file is gone; rules that move or copy files (never);
rules across all fences in one global list.
