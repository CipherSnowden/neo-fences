# NeoFences — Search palette (M15) design

**Date:** 2026-10-04 · **Status:** draft for review · **Target:** v1.8.0 · **Parity:** NeoFences extra "Search palette
across all fences"

## 1. Goal and user choices

One hotkey to find and open anything in the fences — also items scrolled away, in a hidden tab, in a rolled-up fence, in
a Portal or in the Game Library — plus NeoFences' own commands. User choices (2026-10-04, with browser mockups):

| Question | User choice |
|---|---|
| What it finds | Fence items (desktop fences, Portals at the level they show, Game Library games) and NeoFences actions. Not fence names, not inside Portal subfolders. |
| Enter | Opens the item, like a double-click in its fence. Shift+Enter shows it in its fence. |
| Hotkey | Ctrl+Alt+F by default, changeable in Settings like Peek. |
| Layout | A: a centred bar (Spotlight / PowerToys Run): one list, items first, then actions; each row names its fence. |
| Approach | 1: search what NeoFences already holds in memory (config items, each Portal's last listing, the library index); ranking in Core. |

Success: Ctrl+Alt+F, type "fh6", Enter starts Forza Horizon 6 from the Game Library; "form" finds a PDF in the Work
Portal's "Taxes" tab although that tab is hidden; Shift+Enter on an item in a rolled-up fence opens the fence, brings it
above the windows, selects and flashes the item; "> " is not needed for actions: "snap" offers "Take snapshot"; nothing
in the palette changes a file.

Non-goals: searching file contents or folders below a Portal's shown level; Windows Search; fence names as results;
renaming, deleting or moving from the palette; a recent-picks memory; "Exit" as an action.

## 2. What is searched (Core, pure)

`SearchEntry`: either an item — item ref, display name, fence id, box id, chip text, source (Desktop / Portal / Library)
— or an action — action id, label.

- **Desktop fences:** every fence's items (also tabs not shown and rolled-up fences). Display name: the file name without
  `.lnk` / `.url` / `.appref-ms`; virtual items (`::{GUID}`) use the shell display name the host already has.
- **Portals:** each Portal's last listing at the level it shows. The host keeps the last listing per Portal fence (today
  a hidden Portal tab re-lists only when shown).
- **Game Library:** the library index's games, minus hidden ones.
- **Chip:** the box's title, then " › tab title" when the box has more than one tab; the Library adds " · Library".
- **Actions:** New fence, New Portal fence…, New Game Library fence, Take snapshot, Restore snapshot "<name>" (one per
  snapshot), Undo the last restore (when there is one), Apply rules now, Refresh library (when a library exists), Peek,
  Pause / Resume NeoFences, Settings. No Exit.

**Ranking** — `SearchPalette.Rank(query, entries, limit: 50)`:
1. the name starts with the query;
2. word starts: the query's letters start words in order ("fh6" and "h6" find **F**orza **H**orizon **6**; a word
   starts after a space, `-`, `_`, `.`, at a capital after a lower-case letter, and at a digit after a letter);
3. the name contains the query;
4. the query's letters appear in order.

Matching ignores case and accents ("pokemon" finds "Pokémon"); spaces in the query are ignored for 2 and 4. Within a
rank: items before actions, shorter names first, then A–Z (current culture). Each result carries the matched positions
for highlighting. An empty or blank query gives no results. An item listed in two fences appears once per fence.

## 3. The palette window (App)

- **Hotkey:** `Settings.SearchHotkey`, default `Ctrl+Alt+F`, registered like Peek (own id). Changeable in Settings →
  General with the same recorder; a refused combination shows "not active: Windows or another app owns it". Ignored
  during game mode (like Peek, ADR-021). Pressed while the palette is open: it closes.
- **Window:** borderless, topmost, 640 DIP wide, top-centre at 20 % of the monitor under the mouse; the fences' blur and
  Windows' dark/light ink. It takes the keyboard (the hotkey grants the foreground). Up to 9 rows, then it scrolls.
- **Rows:** a 22 px shell icon (loaded off the UI thread; the generic icon until then or on failure), the name with
  matched letters highlighted, the chip; actions show an "Action" tag. Footer: counts and keys.
- **Keys:** type to filter; ↑ ↓ PgUp PgDn move; Enter opens; Shift+Enter shows in fence; Esc closes; a click on a row
  opens. It also closes when it loses the keyboard and after Enter.
- **Open:** the same path as a double-click in a fence (desktop and Portal items through the shell worker; library games
  through their shortcut). Failures give the same feedback as in a fence.
- **Show in fence:** the box shows the item's tab; a rolled-up box opens (as click-to-open); the fences rise as in Peek
  (Esc or a click elsewhere ends it); the item is scrolled into view, selected, flashed twice; the fence gets the keyboard.
- **Paused or quick-hidden fences:** Enter still opens; Shift+Enter shows "Fences are hidden" in the footer.
- **Entries** are built when the palette opens; ranking runs on each keystroke on the UI thread (thousands of entries in
  well under a millisecond).

## 4. Settings and config

`Settings.SearchHotkey` (string, default "Ctrl+Alt+F"; blank → default, like `PeekHotkey`). Schema 4 (ADR-033 rule: new
fields; v1.7 opens a v4 config read-only and never drops the field). One `pre-schema-4-config.json` copy is kept.

## 5. Reliability and hard rules

- The palette never renames, moves or deletes; opening and selecting only (hard rule 1).
- Win32 (hotkey, foreground, monitor under the mouse) through `NeoFences.Shell` (hard rules 4/5); no new NuGet (6).
- Hard rule 7: an unregistered hotkey is reported in Settings and the rest works; an icon that cannot load shows the
  generic icon; an unreadable Portal contributes nothing; an exception while building entries closes the palette and is
  logged.

## 6. Testing

- **Core (xUnit, test-first):** ranking (prefix, word starts incl. camel case and digits, contains, in order, accents,
  spaces, tie order, items before actions, limit, empty query, positions); entries from desktop fences incl. hidden tabs,
  Portal listings, the library minus hidden games, chips for boxes and tabs; actions present or absent by state; schema 4.
- **Live check** (with the user's go; config and the installed copy restored; the Firefox Picture-in-Picture window left
  alone): open the palette by its hotkey message (no keys typed into Terminal), type through UIA, read the rows,
  Shift+Enter on a harmless item (fence shows it), Enter on the "Settings" action; never open a game.
- **Checklist:** new section AB.

## 7. Delivery

M15 → v1.8.0, one batch: prototype in scratch, live check, plan, execute, review, merge, release. ADR-037.
