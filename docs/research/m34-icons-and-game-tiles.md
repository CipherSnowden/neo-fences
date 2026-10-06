# M34 — Icons and game tiles (0.21.0): build notes and results

Spec: `docs/superpowers/specs/2026-10-06-icons-and-game-tiles-design.md` · Plan: `docs/superpowers/plans/2026-10-06-m34-icons-and-game-tiles.md`

## Build

- Mockups with the owner (visual companion, the owner's real covers and icons): game tiles style A (name below; style B's
  name-over-cover when labels are on hover), the glow tile for games without art, clean icons with the accent selection.
- Steam store probe with the owner's 12 game names: Steam's search finds nothing for names with " - " (the term now turns
  punctuation into spaces); new games' covers live under a hashed folder, so their address comes from the public asset
  service (`IStoreBrowseService/GetItems`). 9 of 12 match strictly; AC Black Flag Resynced, Blur and Minecraft Launcher do
  not (Minecraft now shows its own app icon instead of Explorer's).
- Cover sizes (owner's choice at planning): Normal 1×2 and Large 2×4; the owner's two 2×2 covers become Large. Cover fences'
  cells are as wide as a Normal cover needs to fill its two rows, which removes the 44 px row gap.
- Prototype probe on a copy of the owner's data (owner's OK): the question window ("8 of your games have no cover"), 5
  covers found in about 10 s (Clair Obscur, Forza Horizon 6, Mafia DE, Mafia II, PRAGMATA), Choose cover… listing Steam's
  results for "Blur", Settings switch on afterwards.
- Calls made while prototyping (rulings): a logo comes after an online cover; the glow tile's icon is about half the tile;
  the shadow is two faint layers and the glow a shrunken icon stretched (no effects on the layered window); site icons are
  fetched on demand; the picker shows Steam's results unfiltered; generic icons above 32 px come from the system image list.
- Replay-verified on `main` (70 build-error lines before the Core code, 747 tests after, 0 warnings).

## Review (Opus, whole branch)

0 critical, 4 important plus 2 re-graded up, all fixed: "Not now" saved a permanent No (now nothing is stored and the
next start asks again); a missing or broken shortcut lost its icon (the Store-app check threw); being offline marked every
website as a 30-day miss (a fetch now says whether the site answered); a second cover choice kept showing the first (a new
file name per choice, the old copy removed); an odd Steam id could crash Choose cover…; the site icon fetch asked a link's
full address, which can use up a one-time link (now only the site's root). 747 tests. Deferred minors: overlapped Free
elements go to the first free cell; Off does not stop a running lookup; a bad icon file is never retried; each found site
icon reloads every icon; read-only sessions still look up; file names from non-ASCII ids and URL extensions; a failed Steam
pick is only logged; the guide's letter-badge sentence; a Large cover shown as an icon keeps its 2×4 cell.

## Live check (branch Release build, a backed-up copy of the owner's data)

| ID | Result |
|---|---|
| AT1 | **Pass at 100 %**: Apps icons sharp, no slab, Discord selected in an accent box. 150 % by hand. |
| AT2 | **Pass**: rounded covers with a soft shadow, names below, no gap between Normal cover rows. |
| AT3 | **Pass**: AC Black Flag → Size ▸ Large stored 2×4 and shows twice as big; the two old 2×2 covers show Large. |
| AT4 | **Pass**: glow tiles for AC Black Flag and Blur; Minecraft's creeper on its own colours. |
| AT5 | **Pass**: the accent ring on a selected cover; the accent box on an icon. |
| AT6 | **Pass**: Labels → On hover: the name fades in over the hovered (Forza) and the selected (AC Black Flag) cover. |
| AT7 | **Pass**: "Find covers online? 8 of your games have no cover"; Find covers → 5 covers in about 6 s. Not now: by code (asked again at the next start). |
| AT8 | **Pass**: the switch off then on; the found covers stayed. |
| AT9 | **Pass**: Choose cover… for Blur listed Steam's results; "Ricochet Blur" picked and shown; Reset to automatic back to the glow tile. From a file… by hand. |
| AT10 | **Pass**: github.com's own icon fetched; the dead site recorded nothing (letter badge). |
| AT11 | **Pass**: Minecraft Launcher shows its own app icon (Apps and Games). |
| AT12 | **Pass**: the missing test file shows its type's icon at full size with the badge (fence scrolled; by log and code). |
| AT13 | Light mode by hand. |
| AT14 | **Pass**: a stale `watchdog-999999` was removed at start. |
| AT15 | **Pass**: safe mode asked nothing and looked nothing up. |
