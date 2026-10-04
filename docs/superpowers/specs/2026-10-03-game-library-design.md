# NeoFences — Game Library fence (M12) design

**Date:** 2026-10-03 · **Status:** draft for review · **Target:** v1.5.0 · **Parity:** Fences "Game Library"

## 1. Goal and user choices

Every installed game in one place, without making shortcuts by hand (user, 2026-10-03):

| Question | User choice |
|---|---|
| Purpose | Every game in one place: the fence fills itself; a newly installed game appears; click to play. |
| Sources | Launchers (Steam, Epic, GOG, Ubisoft Connect, EA app, Battle.net), Xbox / Microsoft Store, my game folders (`D:\GameLibrary`), game shortcuts on the Desktop. |
| Look | Cover art where the launchers already keep it on disk; the icon otherwise; nothing downloaded. |
| Layout | One fence, all games, A–Z. |
| Approach | Generated shortcuts in NeoFences' own folder, shown by a new "Library" fence kind on the Portal machinery. |

Success: the user's Steam games (Detroit, Plague Inc, Rebel Inc) show with posters, Mafia DE (Epic), Mafia II once (GOG
and `D:\GameLibrary` are the same folder), Minecraft Launcher (Xbox) and the 6 `D:\GameLibrary` games show with icons; a
game installed or uninstalled in Steam appears or disappears without a restart; clicking starts the game through its
launcher; a disconnected drive never empties the library; no user file is touched.

Non-goals: downloading art or metadata; play time, achievements or "recently played"; installing, updating or
uninstalling games; more than one library fence; launching anything on its own.

Probe of the user's PC (read-only, 2026-10-03): Steam libraries `C:\Program Files (x86)\Steam`, `D:\SteamLibrary`
(posters in `appcache\librarycache\<appid>\library_600x900.jpg` or `…\<appid>\<hash>\library_capsule.jpg`, 300×450);
Epic manifest `C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests\*.item` (no art on disk); GOG registry
`HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\<id>` (Galaxy not installed); Xbox package with `MicrosoftGame.config`
(Minecraft Launcher); `D:\GameLibrary` with 6 game folders; no Ubisoft, EA or Battle.net games installed.

## 2. Catalog (finding games)

- **Sources** (Shell scanners, each isolated; a failure logs and yields "unreadable" for that source):
  | Source | Found from | Launch | Art |
  |---|---|---|---|
  | Steam | `libraryfolders.vdf` → each library's `steamapps\appmanifest_*.acf` (`appid`, `name`, `installdir`) | `steam://rungameid/<appid>` | Steam's library cache poster |
  | Epic | `Manifests\*.item` (JSON: `DisplayName`, `InstallLocation`, `CatalogNamespace`, `CatalogItemId`, `AppName`) | `com.epicgames.launcher://apps/<ns>%3A<item>%3A<app>?action=launch&silent=true` | icon of `LaunchExecutable` |
  | GOG | registry `GOG.com\Games\<id>` (`gameName`, `path`, `exe`) | Galaxy `/command=runGame` when Galaxy is installed, else the exe | exe icon |
  | Ubisoft Connect | registry `Ubisoft\Launcher\Installs\<id>` (`InstallDir`) | `uplay://launch/<id>/0` | exe icon (folder rule) |
  | EA app, Battle.net | their registered installs (uninstall entries with the launcher as publisher / install folder) | the launcher's game link when known, else the game's exe | exe icon |
  | Xbox / Store | packages whose install folder holds `MicrosoftGame.config` | `shell:AppsFolder\<PackageFamilyName>!<AppId>` | the package's logo |
  | My game folders | each sub-folder of a configured folder (default `D:\GameLibrary` when it exists) | the program (below) | exe icon |
  | Desktop game shortcuts | Desktop `.lnk` / `.url` that an M11 Game rule would match (launcher or a game folder) | the shortcut's own target | as the shortcut |
- **Core `GameCatalog` (pure, tested):** merges the found entries into one per game. Duplicates = same install folder
  (case-insensitive, normalized; a Desktop shortcut's install folder is the game-folder sub-folder its target lies in,
  else the folder of its target) or same launch target (a Steam `.url` on the Desktop = the Steam entry); the stronger source wins (launcher > Xbox > Desktop shortcut >
  folder) and keeps the best name (launcher name, else shortcut or folder name) and any art. Known tools are skipped
  (Steamworks Common Redistributables, Proton, Steam Linux Runtime, names with "SDK", "Dedicated Server",
  "Redistributable"). The hidden list removes entries. Order A–Z (culture-aware, ignoring a leading "The ").
- **A folder's program:** a Desktop shortcut whose target lies inside the game folder wins; else the largest `.exe` up
  to 3 levels deep, skipping names and folders like `unins*`, `*crash*`, `setup`, `redist`, `_Redist`, `Installers`,
  `vc_*`, `dxsetup`, `*report*`, `*helper*`, `EasyAntiCheat*`. No program found → the game is not listed (logged).
- **Keeping games:** an entry whose source could not be read this time (drive missing, file locked, registry
  unreadable) is kept from the previous scan; only a successful read removes a game.
- **Rescan:** at start; when Steam's `steamapps` folders, Epic's manifest folder or a game folder change (watched,
  debounced); fence menu "Refresh library"; Settings "Refresh library now"; after game mode ends. Scans run off the UI
  thread; one at a time (a request during a scan runs once after it).

## 3. The Library fence

- **Fence kind** `FenceSourceKind.Library` (`source: { kind: "library" }`); at most one. Tray and fence menu: "New Game
  Library fence" (when one exists: brings it forward). It can be a tab of a box (M9); snapshots keep it (M10); rules
  never target it (M11).
- **Shortcut folder** `%LOCALAPPDATA%\NeoFences\library\`: one shortcut per catalog entry — `.url` for links, `.lnk`
  for programs (target, arguments, working folder, icon) — named by the game (characters invalid in file names
  replaced; clashes get " (2)"). NeoFences adds, rewrites (temp-then-swap) and removes only files it wrote, listed in
  `library\index.json` (entry id → file name, art path). Files it did not write are left alone.
- **Tiles:** poster tiles 2:3 (width = 1.5 × the fence's icon size, so 48 → 72 × 108 DIP) with the name below (the
  fence's label setting applies); no poster → the icon centred on a dark tile of the same shape. A–Z; no subfolders.
- **Interaction:** open like any fence item (the shell opens the shortcut → the launcher starts the game). Right-click:
  the shell menu plus "Hide from library" and "Open install folder"; Rename hidden; Delete = Hide (nothing goes to the
  Recycle Bin). Dragging a game out copies its shortcut (never moves it). Keyboard and Narrator: tiles named by game.
- **Game mode:** rescans wait with the deferred desktop work; the fence stays as it is.

## 4. Settings and config

- **Config:** `library: { folders: [string], sources: { steam, epic, gog, ubisoft, ea, battleNet, xbox, folders,
  desktopShortcuts: bool }, hidden: [string] }` (hidden = catalog entry ids, e.g. `steam:228980`, `folder:d:\gamelibrary\blur`).
  Missing → defaults (all sources on; folders: `D:\GameLibrary` added on the first library fence if it exists).
- **Settings → Game Library card:** game folders (Add / Remove), a checkbox per source, hidden games with "Show again",
  "Refresh library now", and a status line ("Last scan 23:40: 12 games; EA not found").

## 5. Reliability and hard rules

- Hard rule 1: no user file, game folder or launcher file is changed; only NeoFences' own `library\` folder is written.
- Hard rule 4/5: registry, shell links, packages and file watching go through `NeoFences.Shell` (CsWin32 where
  possible); Core holds the parsers (VDF/ACF text, Epic JSON) and the catalog, with no Windows calls.
- Hard rule 6: no new dependency — a small tested reader for Valve's text format.
- Hard rule 7: every scanner and every shortcut write is guarded; failures are logged and degrade that source or entry.
- Nothing is launched except on the user's click.

## 6. Testing

- **Core (xUnit, test-first):** Valve text parsing (`libraryfolders.vdf`, `appmanifest_*.acf`, escapes, odd input);
  Epic manifest parsing; catalog merge and duplicates (the Mafia II GOG + folder case), source priority, tool skipping,
  the hidden list, A–Z with "The", keeping entries of unreadable sources; picking a folder's program; shortcut file
  names; config defaults, repair and JSON round trip.
- **Live smoke (only with the user's go — the user may be using the PC):** create the library fence; the expected games
  above with posters for Steam; Hide → gone, Settings "Show again" → back; Refresh; a fake `appmanifest` in a Steam
  library appears and disappears; a missing game folder keeps its games; installed copy and config restored.
- **Checklist section Y.**

## 7. Delivery

Milestone **M12 — Game Library**: prototype → live smoke → plan → approval → inline execution → final review → merge →
release **v1.5.0**. ADR-032 records the decisions.
