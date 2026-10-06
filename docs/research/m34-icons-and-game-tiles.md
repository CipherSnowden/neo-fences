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
