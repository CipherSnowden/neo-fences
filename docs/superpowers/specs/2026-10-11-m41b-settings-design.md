# M41b — Settings (1.0.0-rc.4) — design

**Date:** 2026-10-11 · **Status:** approved in brainstorm, awaiting written-spec review
**Decisions:** ADR-067 (the look lives in one place; recipes replace presets) — added by the plan; amends ADR-057.
**Source:** friends' feedback on rc.1 ("settings hard to understand, duplicated, some options without effect"); the
option audit of 2026-10-11 (120 options: 11 without effect at least sometimes, 34 reachable in two or more places, 22
unclear); the owner's picks in the visual companion (`.superpowers/brainstorm/910-1791664080/content/`, git-ignored):
`fence-settings-v4`, `global-settings-v2`, `recipes`, `settings-audit` (all ten cards: the suggestion).
**Follows:** rc.4 → about a week with friends → 1.0.0.

## Goal

A friend opens a settings window and understands every line on it: nothing that does nothing, nothing in two places,
plain words, and a live picture of what a change does. A fence's look and behaviour are set in **one window** — Fence
settings — for this fence or for all fences. Settings keeps what is about NeoFences and the desktop as a whole. Recipes
give a whole look in one click. Hard rules stand: items and files are never touched by a setting or a recipe; Core is
pure and test-first; failures are logged and degrade one feature; GUIDE.md changes with every setting and menu entry.

## 1. The look lives in one place (audit card 1, ADR-067)

**Fence settings** (fence menu → Fence settings…, or from Settings → Look & feel → **Change the look of all fences…**)
has a switch at the top: **This fence | All fences**.

- **This fence** edits the fence. Each setting shows a badge: **This fence** (its own value) or **All fences** (it
  follows). An own value has **Use all fences'** next to it. **Use all defaults** (footer) drops every own value at once.
- **All fences** edits the defaults every fence follows. A line under the switch says how many fences have their own
  value for anything shown ("2 fences have their own theme — **Make them follow**").
- Settings that have an all-fences default: Theme, Accent colour (only the "Windows accent" choice — a fence's own
  colour is never global), Transparency, Title style, Title font / size / weight, Title alignment, Show the title bar,
  Icon size, Names (labels), Spacing, Open items with. **This fence only** (no badge, greyed in All fences mode with
  "Set on each fence"): Emoji, Name, Layout (Grid / Free), Widgets float, Roll up, Lock position, Auto-collect.
- **New fences follow All fences** for everything (today they copy "Labels for new fences"; that setting goes).
- **Existing setups keep their look:** on the first start of rc.4, a fence's stored icon size and labels become its own
  values only where they differ from the all-fences value; equal ones follow (nothing changes on screen). The
  all-fences icon size and labels start as the most common values among the user's fences.
- **Settings → Look & feel** keeps what is not about a fence: Animations (Motion), Item names (Crisp / Chips / Plain),
  Menu style (Guided / Compact), Colour fences from the wallpaper, and a big button **Change the look of all fences…**.
  The search (§3) finds every fence setting too and opens Fence settings in All fences mode at that setting.

## 2. Fence settings window (v4)

Side by side: controls on the left, a **live preview** on the right; the window opens next to its fence.

- **Header:** the fence's emoji and name, editable in place; the This fence | All fences switch; **↶ Undo** (every change
  in this window, newest first; Ctrl+Z) and **Use all defaults**.
- **Summary line:** "Games: 12 items · Game HUD · opens with one click · 3 own settings, the rest follow All fences."
- **Sections** Look · Title · Items · Behaviour as tabs that scroll the one list (the tab lights for the section in view).
  - **Look:** Theme cards (Classic, Aurora glass, Game HUD, Neon, Light Mica — each a small live sample), **Accent
    colour** swatches (Windows accent — follows Windows, eight colours, Custom… with the colour field), Transparency
    (one slider "See-through ←→ Solid"), Colour style (only shown for Classic: Accent edge / Tinted glass / Title strip).
  - **Title:** Title style cards (Simple, Clean, Pill, Caps), font, size, weight, alignment, Show the title bar. Weight
    and alignment are greyed with "Caps sets this" / "Pill sets this" when the style decides them.
  - **Items:** Layout (Grid / Free), Icon size, Names (Always / On hover), Spacing (greyed with "Only for the Grid
    layout" in Free), Widgets float (only when the fence holds only widgets).
  - **Behaviour:** Open items with (One click / Double click), Roll up to the title (with how it opens: on hover / on a
    title click — today's global "Rolled-up fences open" moves here as an all-fences setting), Lock position,
    Auto-collect (summary + Set up…).
- **Explainer:** hovering a setting lights its part in the preview and shows under the preview what it does, its default,
  and one tip. Every setting has this text (it doubles as the guide's wording).
- **Preview:** the fence itself, drawn live (a copy of its current window content) over the user's wallpaper picture
  (Windows' wallpaper file; with Wallpaper Engine or none, a neutral gradient and the note "Your wallpaper isn't a
  picture — showing a plain background"). The real fence on the desktop changes live too. **Hold to compare** (press and
  hold) shows the look the fence had when the window opened.
- **Recipes** row in the footer (§4) and **Done**.

## 3. Settings window (v2)

Pages with coloured tiles on the left and a **search box** on top (finds any setting by its words, in Settings and in
Fence settings; Enter or a click jumps there and flashes it):

- **Look & feel** — as §1.
- **Desktop** — Hide desktop icons while NeoFences runs; the three desktop gestures with small pictures (double-click
  empty desktop hides all fences; click wallpaper reveals the desktop; right-drag draws a new fence) and a line that
  tells the single click and the double click apart; Start with Windows.
- **Games** — Look for games in (sources), Game folders (shown greyed with "Turn on My game folders" when that source is
  off), New games go to, Find covers and website icons online, Go idle while a full-screen game runs, Refresh library.
  **Hidden games is removed** (card 7).
- **Peek & keys** — Peek hotkey, and a short list of the keys that work in a fence (read-only).
- **Snapshots & safety** — snapshots (take, restore, rename, delete, folder), Export / Import setup, Reset settings.
- **Updates & privacy** — Download updates automatically, Check now / Restart to update, what leaves the PC (online art,
  update checks) in two lines with a link to PRIVACY.
- **Help & about** — version, Help (guide), logs and data folders, licence and notices.

Toggles animate; choices are segmented buttons or picture cards; every option has one line under it saying what it does.

## 4. Recipes (card 2: presets become recipes)

A recipe sets a fence's look and behaviour — theme, accent choice, transparency, title style, icon size, names, layout
(Grid / Free), spacing, open with — **never items, never a fence's place or size**.

- Built in: **🎮 Game shelf** (Game HUD, large icons, names on hover, one click), **💼 Work desk** (Light Mica, small
  icons with names, double click), **📥 Downloads inbox** (Aurora, names always, double click; a folder panel in the
  fence is set to Details, newest first), **◻ Minimal** (Aurora at low transparency, title bar on hover, Caps titles,
  icons only).
- **Save as recipe…** saves the fence's current settings with a name and emoji. The user's existing presets become their
  own recipes (same names); the old built-in presets (Glass, Minimal, Title strip, Solid, Compact) go.
- **Apply to this fence:** one confirmation listing the changes; Ctrl+Z / Undo undoes it.
- **Apply to all fences** (from All fences mode): step 1 lists what changes and says items and places stay; step 2
  "Are you sure?", with **Only fences that hold games (n of m)**, and a snapshot "Before <recipe>" taken first; tray →
  Restore snapshot brings everything back. Applied to every fence, it sets the all-fences values and makes every fence
  follow; applied to only the fences that hold games, it sets those fences' own values (the others keep following).

## 5. The audit's other decisions

- Clock: the **Seconds** and **Date line** switches go (card 3; the size decides); the world-clock cities show on the
  back only when the clock is Large.
- **Colour style** only for Classic (card 4).
- **Transparency** one value for dark and light (card 5); Light Mica starts brighter by itself. Existing setups: the
  dark-mode value is kept as the value.
- Fence colour: **Use my accent colour** becomes the swatch **Windows accent**, which follows Windows' accent live
  (card 6); the fence menu's Colour ▸ goes (it lives in Fence settings).
- Labels only in Fence settings (card 8): Settings → Fences "Labels for new fences" and "Apply to all fences" go.
- Widget size: the Size row on a widget's back goes (card 9); gallery S/M/L, corner drag and right-click → Size stay.
- Game covers: the same **Size ▸** grid as every item (card 10); "Normal / Large" goes.

## 6. Wording and leftovers (fixed without asking)

- Fence menu **Sort by ▸** becomes **Sort now ▸**; the fence menu gets **Roll up** (and **Unroll**).
- Item menu **Run as administrator** becomes **Run as administrator once**.
- Compact menu icons get tooltips with their names.
- Auto-collect: **Anything** greys the other kinds; "Also" becomes "Also files named (for example *.torrent)".
- Folder panel "Types" becomes "Only files like (for example *.png)".
- Remove the stale texts ("each fence can choose in its own menu", "the other themes use it as their accent", "every
  fence's title uses it").
- Old Game Library fence leftovers go from the UI (the tray handler with no entry, Hide from library). Old configs still
  load (a library fence shows as a normal fence, as today).

## 7. rc.3 leftovers folded in

- The press dip resets after a drag; an item lifted when Motion turns Off settles.
- Widget bars and selection fill use the fence's theme tone.
- Now playing: an untitled session shows the app's name.
- Calendar month and the Large clock's cities rebuild only when the day or a city's offset changes.
- A pending wallpaper reveal is cancelled by a click elsewhere.
- Touchpad wheel over Now playing adds up to one volume step per 120 units.
- Player windows: processes disposed; `MediaKeys.cs` split into `VolumeKeys.cs` and `PlayerWindows.cs`.
- The emoji box keeps only the first emoji typed.
- Not done: search and categories in the widget gallery — it holds four widgets; it gets them when 1.1 adds more.

## 8. Testing

- **Core, test-first:** the all-fences resolution (own value / follows), Use all defaults, Make them follow; the rc.4
  migration (equal values follow, the most common becomes all-fences, the dark transparency kept, presets → recipes);
  recipe apply to one fence and to all (only items' fences with games, items and places untouched); the settings search
  index (every option findable by its label and words); every audit removal leaves configs loading (old fields ignored).
- **VM checks** (`Invoke-VmChecks.ps1 -Live`): open Fence settings, switch a fence to Neon (the fence changes live), All
  fences → Game HUD changes a following fence and not one with its own theme; apply Game shelf to all fences (snapshot
  taken, items unchanged, restore brings the look back); Settings search "double" finds both double-click settings.
- **Probe screenshots** for the owner: Fence settings (each tab, hover explainer, hold to compare), Settings (each page,
  search), the recipe confirmations.
- TEST-CHECKLIST section BA; GUIDE.md rewritten for Settings and Fence settings; README quick start checked.

## Not in M41b

Widget gallery search/categories (with 1.1's widgets); per-fence fonts beyond today's; desktop pages; theme packs.
