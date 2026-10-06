# M34 — Icons and game tiles (0.21.0) — design

**Date:** 2026-10-06 · **Status:** approved in brainstorm, awaiting written-spec review
**Decisions:** ADR-055 (opt-in online art: Steam store covers, website icons from the sites themselves)
**Source:** `docs/research/v1-readiness.md` (Icons and game tiles), the owner's four complaints: blurry / low-res icons,
games without real covers (7 of 12 here), tiles and labels that look dated, uneven sizes and spacing.
**Mockups:** visual companion screens `cover-tiles`, `no-art-tile`, `app-icons` (owner picked A, A, A).

## Goal

Every icon is sharp at any display scaling, every game shows real art or a deliberate-looking fallback, tiles and
selection look like Windows 11, and spacing is even. Online art is opt-in, off until the owner (or a friend) says yes.
Hard rules stand: user files never touched; no new NuGet dependency; network and file work only in `NeoFences.Shell`.

## 1. Where a game's art comes from

`GameArt.Choose` (Core, test-first) picks, per game, the first that exists:

1. **The user's choice** ("Choose cover…"): a picture copied into NeoFences' data, or a Steam result the user picked.
   Stored per game id in the config (`Library.CoverChoices`: game id → file name in `covers\`), so a game in two fences
   shows the same cover. "Reset to automatic" removes the entry.
2. **Launcher art on disk**: Steam's 2:3 cover (as today, `GameEntry.Poster`); new: an Xbox / Microsoft Store game's art
   from its package folder (its largest square or tall logo; a launcher-only entry such as Minecraft Launcher shows its
   package logo instead of Explorer's folder icon).
3. **The online cache**, when online art is on: a cover found on the Steam store (`covers\index.json` + image files).
4. **The glow tile**: built from the game's largest icon (`GameEntry.IconPath` or the shortcut's target).

A poster (1–3) fills the tile; a logo is centred on the glow backdrop like an icon.

## 2. Online art (opt-in, ADR-055)

- **Asking.** The first time a library scan leaves games without art, a notice asks once: "Find covers online? NeoFences
  sends the names of games without a cover to the Steam store." Yes / No (held while a game runs, like other notices).
  The same switch: Settings → Games, "Find covers and website icons online". Off by default; turning it off stops new
  lookups and keeps what was found.
- **Covers.** After each library scan, a background step looks up only games still without art (rules 1–2), one request
  at a time, through the Steam store's public search (no account, no key). A result counts only on a **strict match**:
  names equal after ignoring case, punctuation, spacing and ™ / ® / © (`GameArt.SameName`). "Clair Obscur - Expedition 33"
  matches "Clair Obscur: Expedition 33"; "AC Black Flag Resynced" does not match "Assassin's Creed IV Black Flag" and
  keeps its glow tile. The 2:3 cover is downloaded into `covers\` and recorded in the index with the Steam app id.
- **Misses** are recorded with the date and the name looked up: the game is not looked up again for 30 days or until its
  name changes (`GameArt.ShouldLookUp`). A failed connection records nothing; the next scan tries again.
- **Website icons.** With the switch on, a web link's icon comes from the site itself: its page's declared icon
  (`<link rel="icon" | "apple-touch-icon">`, the largest; parsed in Core) or else `/favicon.ico`, fetched once and kept in
  `covers\sites\`. With it off, or when nothing is found: a coloured letter badge (first letter of the site name, colour
  from the host name) instead of the browser's icon.
- **Limits.** Only http(s) to the Steam store and to the user's own web-link hosts; time-outs of 10 s; responses over
  5 MB and non-image content are dropped; nothing but game names and website addresses leaves the PC. The cache lives in
  NeoFences' data folder and is never shared; deleting `covers\` only loses the found art.

## 3. Choose cover…

A game tile's menu gets **Choose cover…**: a small window with

- up to 8 Steam store results for the game's name, as cover thumbnails, when online art is on (any match, not only strict;
  click to use);
- **From a file…**: a PNG or JPG, copied into `covers\` (the original is never moved or changed);
- **Reset to automatic**.

It works with online art off, without the results.

## 4. The look

- **Game tiles (style A).** 2:3 covers, 8 px rounded corners, a soft shadow, the name below (two lines at most). Hover
  lifts the tile a little; selection is a ring in the Windows accent colour. On a fence whose labels are "on hover" the
  name fades in over the bottom of the cover instead (style B's overlay).
- **Glow tile.** The icon, blurred and enlarged, fills the 2:3 tile as a colour backdrop with a darker bottom; the sharp
  icon sits in the middle. An icon smaller than the space is drawn at most twice its real size.
- **Sizes.** In a fence with cover tiles, rows are sized to the 2:3 shape plus the label, so a 2×2 cover is exactly twice a
  1×1 in both directions and never cropped (the review's `ApplySize` bug). The gap between rows equals the gap between
  columns (no 44 px row gap).
- **Icons (Clean).** No grey slab behind items. Hover: a faint rounded highlight; selection: a rounded box tinted with the
  accent and an accent edge. Folder panel rows select in the same accent.
- **Sharpness.** Every icon is requested at its real pixel size on its monitor (DIPs × that monitor's scale), from the
  largest image Windows has, and scaled down with high quality; covers use high-quality scaling; layout rounding snaps
  edges to whole pixels. A missing item shows its own icon at its real size with a small "!" badge, never a stretched
  32 px icon.
- **Light and dark.** Tile shadow, hover, glow shading and label shadow follow Windows' mode like the fences do.

## 5. Small extra

At start, NeoFences deletes `watchdog-<pid>` files of processes that no longer exist (left behind by power cuts).

## Testing

- **Core, test-first:** the art order (`GameArt.Choose`), strict names with the owner's 12 game names as cases, the
  30-day / renamed miss rule, the declared-icon parser (largest wins, relative URLs, no icon), the letter badge (letter
  and stable colour), the covers index (read, write, damaged file = empty), the stale watchdog files rule.
- **Prototype probe:** the Steam store lookup with the owner's games (which match strictly) before the plan.
- **Checklist AT** (live, on a backup of the owner's data): sharp icons at 100 % and 150 %; covers and glow tiles; 2×2
  cover; spacing; accent selection; the ask-once notice; Settings switch; Choose cover… (results, file, reset); website
  icon and letter badge; offline start; light mode.

## Dogfood

After the live check: a snapshot first, then online art on for the owner's Games fence, the result shown; the Games
fence keeps its label mode unless the owner asks for "on hover".

## Out of scope

SteamGridDB or other art sources; alternative art types (heroes, logos as banners); an icon cache across restarts
(M37); per-fence tile styles (M36); animated covers.
