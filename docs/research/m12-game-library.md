# M12 — Game Library: results

**Date:** 2026-10-03/04 · **Machine:** Windows 11 Pro 25H2 (26200), NeoFences 1.4.0 installed.
**Build:** M12 prototype (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section Y · **Decision:** ADR-032.

## Read-only probes on the user's PC

- Steam: `HKCU\Software\Valve\Steam\SteamPath`; libraries `C:\Program Files (x86)\Steam` (Wallpaper Engine,
  Steamworks Common Redistributables) and `D:\SteamLibrary` (Detroit, Plague Inc, Rebel Inc). Posters:
  `appcache\librarycache\<appid>\library_600x900.jpg` or, in Steam's newer layout, `…\<appid>\<hash>\library_capsule.jpg`
  (300×450) — all three games have one.
- Epic: `C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests\*.item` (Mafia DE); no art on disk.
- GOG: `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\1449710114` (Mafia II, in `D:\GameLibrary`); Galaxy not installed.
- Xbox: `HKCU\…\AppModel\Repository\Packages\<full name>\PackageRootFolder` + `MicrosoftGame.config` (Minecraft Launcher;
  logo `GraphicsLogo.png`; app id from `AppxManifest.xml`).
- `D:\GameLibrary`: the largest-program rule picks the right program for AC, Blur, Forza and PRAGMATA; the Desktop
  shortcuts decide for Clair Obscur and Mafia II.
- Scanner probe: 12 games in 0.2 s (Steam 5 entries in 2 libraries, Epic 1, GOG 1, Xbox 1, folder 6, Desktop 10 →
  merged); shortcuts written to a scratch folder and read back; a second plan rewrote nothing.

## Live smoke on the prototype (installed copy restored; test library folder and scratch games removed)

| Check | Result |
|---|---|
| Y1 tray → New Game Library fence | **Pass**: 13 entries (the user's 12 + a scratch test game), A–Z; tiles shown. |
| Posters | **Pass**: Detroit's Steam cover fills its tile (scrolling screenshots); others show the icon on a tile. |
| Y7 a new game folder appears / is removed (scratch games folder) | **Pass**: listed within 9 s; gone after removal. |
| Y8 the scratch games folder renamed away, Refresh | **Pass**: its game stays; the status line names the unreadable folder. |
| Y4/Y5 right-click → Hide from library; Settings → Show again | **Pass**: the menu ends with "Hide from library", "Open install folder" (no Rename); the game left and came back. |
| Log | 0 warnings or errors. |

**Found on the way:**
- The game was hidden under its Desktop shortcut's id; deleting that shortcut would have brought it back from the
  folder → hiding now stores every id the merged game goes by (test added).
- A first test script's right-click missed the tile (a UIA name lookup hit the hover-label popup) and opened the
  Desktop's menu; nothing was changed. Smoke scripts now aim at the tile's own list item and check the point lies inside
  the fence, and never press Delete as a fallback.

## Branch smoke after the final review (installed copy restored; test library folder and scratch games removed)

| Check | Result |
|---|---|
| Y1 create; Y7 a new game folder appears / goes; Y8 a missing games folder | **Pass**, as on the prototype. |
| Y4/Y5 Hide from library, Settings → Show again | **Pass**. |
| Y6 hiding covers every id | **Pass**: the hidden list holds the Desktop-shortcut id and the `folder:` id of AC Black Flag. |
| Read-only re-scan with the fixed scanners | the same 12 games, every source readable. |
| Log | 0 warnings or errors. |
