# NeoFences — Rules auto-sort (M11) design

**Date:** 2026-10-03 · **Status:** draft for review · **Target:** v1.4.0 · **Parity:** Fences "Rules"

## 1. Goal and user choices

New desktop items go straight to the right fence; existing items can be tidied once on request (user, 2026-10-03):

| Question | User choice |
|---|---|
| Purpose | File new items, plus "Apply rules now" to sort existing items on demand (items placed by hand otherwise stay). |
| Conditions | File type groups (+ custom extensions), game shortcuts, name text / pattern, date, size — all four. |
| Where | One ordered list in Settings → Rules (first match wins); fence menu → "Rules for this fence…" as a shortcut. |
| Approach | Rules in `config.json`; the Shell layer reads item facts; Core `Rules.Match` picks the fence (tested). |

Success: a downloaded image, a new archive or a game installer's new shortcut lands in its fence without a click; a
non-matching item still lands in the Inbox; "Apply rules now" tidies the desktop and can be undone; nothing moves files,
Portals or hand-placed items (except on "Apply rules now"); a broken rule never breaks NeoFences.

Non-goals: rules that move or rename files; rules for Portals; regular expressions; rules on file contents; keeping the
desktop permanently sorted against the user's own moves.

## 2. Rules and matching

- **Config:** `Rules: [Rule]`, in order. `Rule { id, enabled, condition, fenceId }`.
- **Conditions** (camelCase JSON):
  | `kind` | Fields | Matches |
  |---|---|---|
  | `type` | `group` (`shortcutsApps`, `images`, `videos`, `documents`, `archives`, `folders`) or `extensions` (".iso .torrent") | the item's extension is in the group or list (a folder for `folders`) |
  | `game` | `launcher` (`any`, `steam`, `epic`, `ubisoft`, `ea`, `battleNet`, `gog`) | a shortcut whose target/URL belongs to that launcher |
  | `name` | `pattern` (`*` and `?`; plain text = contains) | the display file name, case-insensitive |
  | `age` | `compare` (`olderThan` / `newerThan`), `days` | the item's last-modified date |
  | `size` | `compare` (`biggerThan` / `smallerThan`), `megabytes` | a file's size (folders never match) |
- **Groups:** shortcutsApps = .lnk .url .exe .appref-ms .msi .bat .cmd; images = .jpg .jpeg .png .gif .bmp .webp .heic
  .tif .tiff .svg .ico; videos = .mp4 .mkv .avi .mov .wmv .webm .m4v; documents = .pdf .doc .docx .xls .xlsx .ppt .pptx
  .txt .rtf .odt .ods .md .csv; archives = .zip .rar .7z .tar .gz .bz2 .xz .iso .cab.
- **Game shortcuts:** a `.url` whose URL starts with `steam://`, `com.epicgames.launcher://`, `uplay://`, `origin://`,
  `origin2://`, `ea://` (EA app), `battlenet://`, `goggalaxy://`; or a `.lnk` whose target lies under a known library
  folder (`…\steamapps\common\`, `…\Epic Games\`, `…\Ubisoft Game Launcher\games\`, `…\EA Games\`, `…\GOG Galaxy\Games\`)
  or is a launcher exe started with a game argument (`steam.exe -applaunch`, `…Battle.net.exe --exec`).
- **Item facts** (Shell, `ItemFacts { itemRef, name, extension, isFolder, sizeBytes, modified, shortcutTarget }`): read
  from the file system and, for `.lnk` / `.url`, the shell link or the `URL=` line. Unreadable facts → the item matches
  only name/type rules (whatever is known).
- **Core `Rules.Match(facts, rules, desktopFenceIds) → fenceId?`:** the first enabled rule whose condition matches and
  whose fence exists as a desktop fence (tabs included). **`Rules.File(config, facts) → config`:** every matched item
  moves to the end of its rule's fence, in current order; unmatched items stay.
- **Repair on load:** an unknown kind/group/launcher/compare, a negative number, an empty pattern or no extensions
  disables the rule; a rule whose fence is missing stays (shown as "fence missing", never matched).

## 3. When rules run

- **New items:** after a watcher "created" event or a reconcile (startup, recovery) has put new items into the Inbox,
  the host reads their facts on the shell worker (a network shortcut can be slow) and, back on the UI thread, files the
  matching ones (one edit, one save). Safe-save memory and drop arrivals win over rules (the item is not in the Inbox
  then). An item that vanished or moved meanwhile is skipped.
- **Game mode and Pause:** filing waits with the deferred desktop work and runs after the game / on resume.
- **"Apply rules now"** (Settings → Rules): writes the safety snapshot `before-restore.json` first (tray → "Undo the
  last restore" undoes it; no apply without it), reads facts for every desktop item (off the UI thread), files every
  matching item — hand-placed ones too — and shows a notice "N items moved".
- Rules never move files, never touch Portals, never run on their own except for new items.

## 4. UI

- **Settings → Rules card:** an ordered list; each row `☑ When <condition> → <fence>` (enable checkbox), "(fence
  missing)" greyed; buttons Add, Edit, Delete, Move up, Move down, Apply rules now.
- **Editor** (under the list): Condition kind (Type / Game / Name / Age / Size) and its fields (group or extensions;
  launcher; pattern; older/newer + days; bigger/smaller + MB); "Put in" — desktop fences (tabs included, by title);
  Save / Cancel (Enter / Esc). Every field named for Narrator.
- **Fence menu → "Rules for this fence…":** opens Settings at Rules with a new rule targeting that fence.

## 5. Reliability and hard rules

- Rules live in `config.json` (safe save, daily backups); snapshots do not contain rules (a restore never changes them).
- A broken rule is disabled on load; fact reading copes with locked, vanished or unreadable items (they stay put).
- Hard rule 1: membership only; nothing touches files. Hard rule 7: failures degrade to "the Inbox".

## 6. Testing

- **Core (xUnit, test-first):** every condition kind and comparison; groups and custom extensions; patterns (contains,
  `*`, `?`, case); game URL and path detection per launcher; first match wins; disabled rules; a missing or Portal target
  fence; `Rules.File` moving matched items only, in order, hand-placed ones included; config repair; JSON round trip and
  an old config without rules.
- **Live smoke (with consent; installed copy restored):** add rules (Images → a fence, Game shortcuts → a fence, Name
  "invoice*" → a fence); create a .png, a Steam-style .url and "invoice-1.pdf" on the Desktop → each lands in its fence; a
  .txt lands in the Inbox; Apply rules now moves a hand-placed matching item; Undo restores it.
- **Checklist section X.**

## 7. Delivery

Milestone **M11 — Rules**: prototype → live smoke → plan → approval → inline execution → final review → merge → release
**v1.4.0**. ADR-031 records the decisions.
