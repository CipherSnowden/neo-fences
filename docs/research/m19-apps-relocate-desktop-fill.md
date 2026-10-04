# M19 — Store apps, bulk fix, Add from desktop, reliability (0.10.0): prototype and live results

**Spec:** `docs/superpowers/specs/2026-10-05-m19-apps-relocate-desktop-fill-design.md` · **Decisions:** ADR-042, ADR-043
**Plan:** `docs/superpowers/plans/2026-10-05-m19-apps-relocate-desktop-fill.md`

## Prototype probes (2026-10-05, this PC, Windows 11 26200)

| Probe | Result |
|---|---|
| AppsFolder listing | 182 apps in 0.34 s (Store apps and programs; ids like `Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe!App`, `308046B0AF4A39CB` for Firefox) |
| `shell:AppsFolder\<id>` | `SHCreateItemFromParsingName` parses it with the app's name and icon; a made-up id fails with 0x80070002 (the Missing check) |
| An app's desktop-absolute parsing name | just the id (not `::{4234…}\id`): drops recognise apps by their **parent** (the AppsFolder), not by the name |
| Dragging from Start → All | works: Snipping Tool became `shell:AppsFolder\Microsoft.ScreenSketch_8wekyb3d8bbwe!App`. Windows' drag-image helper refuses Start's data object (DV_E_CLIPFORMAT): now expected and not logged as a failed drop |
| Generic icons (`SHGetFileInfo`, by name) | a missing `.txt` shows the text-file icon, a dead share item a generic one, an uninstalled app a generic file icon; all with the Missing badge or dimming |
| Bulk fix | three items in `…\old`, folder renamed while NeoFences was off, Locate… of one → "Fix 2 more items? They were in …\old and are now in …\new." → Fix re-points both and writes `before-restore.json`; tray "Undo the last restore or fix" puts the two back (the located one stays) |
| Add from desktop… | 31 desktop entries in 4 s into 3 new fences; a second run shows every row "already in …" and Add greyed |
| Games group | with no game folder configured (the fresh 0.9.0 data), only launcher links counted: 4 of the user's 10 games. The Game Library's scan (install folders, its Desktop-shortcut games) now counts too (5); the 6 games in `D:\GameLibrary` need that folder in Settings → Game Library, which the dialog says |

## Findings fixed in the prototype

- An uninstalled app had an empty label (its placeholder name): now the front of its id ("NotAnApp.Bogus").
- The app list's rows told screen readers their type name: now the app's name.
- Games sorted as Apps without game folders: the Game Library's scan is asked (read-only) and a hint names the setting.
- "drop on fence failed" logged for every Start drag (the drag-image helper's refusal): quiet, and the helper is not
  asked again during that drag.

## Live check results

(Task 6 of the plan fills this in.)
