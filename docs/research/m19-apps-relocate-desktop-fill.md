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

2026-10-05, test build of the branch after the final review's fix pass (commit `3c3033c`), the installed 0.9.0 stopped
and its data backed up and restored afterwards; test files only in `%USERPROFILE%\NeoFences-m19-test\` and
`G:\NeoFences-test\` (both removed). Hand steps by the user: AE4, AD8, the pendrive.

| ID | Result | Notes |
|---|---|---|
| AE1 | PASS | the app list fills in the background; search "Sticky" finds Sticky Notes |
| AE2 | PASS | target `shell:AppsFolder\Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe!App`, name filled, "● Found (an app).", Arguments and admin greyed |
| AE3 | PASS | double-click opens Sticky Notes |
| AE4 | PASS | the user dragged Paint from Start → All: `shell:AppsFolder\Microsoft.Paint_8wekyb3d8bbwe!App`, no "drop failed" warning |
| AE5 | PASS | typed bogus app: "● Not installed: …", item Missing with the label "NotAnApp.Bogus" |
| AE6 | PASS | Locate… on it opens the app list (no file dialog); Calculator chosen, the item points there |
| AE7 | PASS | "Fix 2 more items? They were in …\old and are now in …\new." (b, c); Fix: both re-pointed, `before-restore.json` written |
| AE8 | PASS | tray "Undo the last restore or fix": b and c back in `old` (a stays) |
| AE9 | PASS | d.txt deleted from `new`: not offered |
| AE10 | PASS | Locate… to a file with another name: no bulk question |
| AE11 | PASS | listed in 0.14 s: Games 5, Apps 24, Folders and files 2; all ticked; "New fence: …" |
| AE12 | PASS | 31 items into 3 new fences; items point at the desktop entries |
| AE13 | PASS | second run: 0 of 31 ticked, Add greyed |
| AE14 | PASS | with `D:\GameLibrary` as a game folder: Games 10, Apps 19 |
| AE15 | PASS | "Hide desktop icons" ticked: HideIcons 1, marker, setting on (switched off afterwards) |
| AE16 | PASS | Shift+right-click a dead-share item: "Network location not reachable" after 1.8 s, fence responsive |
| AE17 | PASS | Browse → file dialog after 1.8 s, icon picker after 0.9 s; log "did not answer" |
| AE18 | PASS | a.txt + a dead-share item dragged out: only a.txt copied |
| AE19 | PASS | OK greyed while "Checking…", enabled with "● Network location not reachable." |
| AE20 | PASS | restart with share items: the other icons load normally |
| AE21 | PASS (adapted) | the arming race cannot be hit by hand; pulled without Safely remove while running: Unavailable, released, no errors; re-plugged: Ok, `g.txt` rename followed and back |
| AE22 | PASS (Core) | stale-record clean-up covered by `StaleEntries` tests; no errors in the whole run |
| AE23 | PASS | 10 items on the dead share: the Games fence had its icons within 3 s of start |
| AE24 | PASS | an app id with spaces opens (quoted): GPUView from the Windows Kits started; the PDF example had no handler on this PC |
| AE25 | not run | no mapped network drive on this PC |
| AE26 | PASS (reading) | `AppList.Exists` treats only not-found as uninstalled (Core test) |
| AD2 | PASS | a file dropped from Explorer becomes an item; the file stays |
| AD8 | PASS | by hand: one item dragged out to Explorer is copied. A scripted single-item drag copied nothing — the same with the installed 0.9.0, and two items copy fine by script: a quirk of the scripted drag |
| AD14 | PASS | rename while running followed both ways, no Missing |
| AD18 | PASS | Shift+right-click: Windows' menu with its header line |

## Final review (Opus, dc7cceb..ce3269d) and the fix pass

Verdict "with fixes"; every Review Focus invariant held. Fixed in `3c3033c` (Core tests RED→GREEN, 460 tests): a dead
share cost 2 s per item (shared root probes now wait once); mapped network drives were not probed before icons; any app
parse failure counted as uninstalled (now only not-found); app targets opened unquoted (ids with spaces); Locate… for an
app set a closed fence as owner (crash). Deferred minors (for later):

- the bulk fix writes the undo snapshot even when every move was filtered out; "Before fixing 1 items";
- app ids containing `\` (desktop programs): the placeholder label shows the whole id; Shift+right-click Windows' menu
  fails for them (logged);
- Add from desktop runs a full Game Library scan per open (could reuse the library's last scan); a sloppy EA /
  Battle.net install location at a Program Files root would make every program a Game;
- `.lnk` files with an empty raw path (Control Panel, This PC) and `file:///` `.url` files sort as Apps;
- R5 gaps: a snapshot restore does not drop stale records; a check batch finishing late re-adds records of removed
  targets (bounded, cleaned at the next items change);
- a few continuations read `.Result` without checking `IsFaulted` (Windows' menu, picker start check, the bulk-fix offer);
- R1: a microsecond race between registering a removal notice and tracking it;
- a stray blank line in `FenceHost.Items` `DisplayName`.

Script lessons (memory `feedback-desktop-automation`): dialogs can open behind other windows (bring to front and
uncover first); a double-click on an item scrolled out of view lands on the desktop and quick-hides everything (scroll
the item into view and check the point is on the fence first); a restore can recreate fence windows (look them up again).
