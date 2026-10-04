# M11 — Rules auto-sort: results

**Date:** 2026-10-03 · **Machine:** Windows 11 Pro 25H2 (26200), NeoFences 1.3.0 installed.
**Build:** M11 prototype (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section X · **Decision:** ADR-031.

## Fact probe on the user's Desktop (read-only)

`ItemFactsReader` over all 30 Desktop items, on an STA thread:
- `.url` games: Steam (`steam://rungameid/…`, 3) and Epic (`com.epicgames.launcher://apps/…`, 1) detected.
- `.lnk` targets read without resolving (`SLGP_RAWPATH`); arguments kept (`Update.exe --processStart Discord.exe`).
- **Found:** "Epic Games Launcher.lnk" counted as an Epic game (its path is under `\Epic Games\`) → fixed, test added.
- **Found:** 6 of 9 game shortcuts point into `D:\GameLibrary` (no launcher) → the user chose a Game rule "In a folder of
  mine…" (`launcher: "folder"`, `folder`).
- An MSIX/advertised shortcut (Minecraft Launcher) has an empty target: only name/type rules can match it.

## Live smoke on the prototype (installed copy restored; test files removed)

Three rules seeded into `config.json` (Images, Game shortcuts (any launcher), Name `*invoice*` → "New fence").

| Check | Result |
|---|---|
| Startup reconcile files an item made before the start (`nf-m11-start.png`) | **Pass**: New fence. |
| X2/X3 new .png, Steam .url, invoice .pdf, .txt | **Pass**: New fence ×3, the .txt in the Inbox. |
| Settings list | **Pass**: "Images → New fence", "Game shortcuts (any launcher) → New fence", "Name like "*invoice*" → New fence". |
| X1 editor: Other extensions… `.nfx` → New fence | **Pass**: 4th row "Extensions .nfx → New fence"; a new `.nfx` filed. |
| X5 untick Images, save `late.png` | **Pass**: Inbox. |
| X6 Apply rules now | **Pass**: "5 items moved…" (late.png and 4 of the user's Steam/Epic `.url` games); Inbox 33 → 28. |
| X7 undo via "Before applying rules" | **Pass**: Inbox back to 33, late.png in the Inbox. |
| X8 fence menu → Rules for this fence… | **Pass**: Settings scrolled to Rules, editor open, "Put in" = New fence. |
| Log | 0 warnings or errors; every filing logged. |
| X4 game folder | Core-tested here; passed in the branch smoke. |

**Seen on the way:** right-clicking a single fence's title shows Windows' system menu (Move / Size / Close), not the
fence menu — an older quirk, added to the ROADMAP carry-overs.

## Branch smoke after the final review (installed copy restored; test files removed)

Four rules seeded (the three above plus Game "In a folder of mine…" = D:\GameLibrary).

| Check | Result |
|---|---|
| X4 new shortcut into D:\GameLibrary | first run **Fail**: read the instant it was created, before WScript.Shell finished writing it (no target) → filing now waits 1.5 s after the last arrival (one read per burst); rerun **Pass**. |
| X15 `nf-m11-dl.png.crdownload` renamed to `nf-m11-dl.png` | **Pass**: New fence. |
| X16 Read-only toggled on a matching item in the Inbox | **Pass**: stays in the Inbox (attribute changes no longer re-run rules). |
| Startup, new items, editor rule, unticked rule | **Pass** as on the prototype. |
| X6/X7 Apply rules now + undo | **Pass**: 11 moved (incl. the user's D:\GameLibrary and launcher games), all back after the undo. |
| X8 fence menu → Rules for this fence… | **Pass**: editor open, "Put in" = New fence; the hint now matches the chosen group. |
| Log | 0 warnings or errors. |
