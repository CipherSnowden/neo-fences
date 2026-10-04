# M3a — Item actions: verification results

**Date:** 2026-10-02 · **Machine:** Windows 11 Pro 25H2 (26200), one 1920×1080 monitor at 100 %.
**Build:** `m3a-item-actions` (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section J.

The agent ran `m3a-smoke.ps1` (in the plan) with the user's consent, twice: once on the prototype and once on the
repo build. It drives real mouse and keyboard input on the desktop and finds items by their UI Automation name.
It uses only its own `zz-m3a-*` files and refocuses Windows Terminal at the end.

## Results

| ID | Result | Evidence |
|---|---|---|
| J1 | **Pass** (single item) | Right-click shows Windows' full classic menu, including the user's shell extensions (Edit in Notepad, Open with Code/Cursor, 7-Zip ►, PowerRename, Defender, Send to ►, Copy as path, …). Screenshot `m3a-item-menu.png`. The extended and multi-select menus are [USER]. |
| J2 | [USER] | Submenus and commands. |
| J3 | **Pass** (file) | F2 → typed `zz-m3a-renamed` + Enter: the file became `zz-m3a-renamed.txt` (extension kept), at the same Inbox index. The shortcut case is [USER]. |
| J4 | [USER] | Rename conflict. |
| J5 | **Pass** (Del) | `zz-m3a-recycle.txt` left the Desktop, appeared in the Recycle Bin (Shell namespace 10) and left the fence. Shift+Del / menu Delete are [USER]; both share the same code path. |
| J6 | [USER] | Multi-select Del. |
| J7 | **Pass** (broken shortcut) | Foreground became Windows' "Missing Shortcut" dialog, owned by the fence, and the log shows `could not open`. Answered Esc (keep). |
| J8 | **Pass** (scripted) | A Word-style sequence ran on a fenced file: rename away, hidden temp renamed onto the name, unhidden, renamed-away copy deleted. The index stayed the same (30 → 30). A real Word save is [USER]. |
| J9 | [USER] | Special items. Code: `BeginItemRename` and `TryRecycle` skip `::{CLSID}`. |
| J10 | [USER] | Permanent-delete warning (`FOF_WANTNUKEWARNING`). |

## Findings from the prototype

1. **Word's renamed-away original can be visible.** The watcher then reports `Renamed(doc → ~WRL….tmp)`, not a
   delete, so the first safe-save draft (deletes only) still sent the document to the Inbox. Renames are now
   remembered too. Pinned by `WordSave_WithAVisibleTempName_KeepsTheDocumentInItsFenceAndPosition`.
2. **UI Automation's desktop-wide search does not reach fence windows**: they are owned by Progman, in another
   process. Searching from each fence's handle works. Items now expose their label as their automation name.
3. **Two single clicks 550 ms apart are not a double-click** (a smoke-script bug, fixed).
4. The first scripted run clicked at (0,0) because of finding 2: it opened the desktop's own menu, and one test file
   was left on the Desktop and deleted by hand. No user file was touched.

## User-reported z-order bug (fixed on this branch)

Screenshot from the user: the Inbox fence drew over Firefox. Z-order walk: all four fences sat above Task Manager,
Settings and other apps. The scripted smokes minimized everything, activated a fence with a click, then restored
the windows; the restored windows came back below the activated fence. Fix: `FenceWindowChrome.KeepAtBottom` on
`WM_WINDOWPOSCHANGING` (ADR-016). Re-run of that exact sequence, with and without the fence becoming the
foreground window: 0 app windows below any fence.

## Final review fixes (opus reviewer, 1 critical / 3 important / 11 minor)

| Finding | Fix | Verified by |
|---|---|---|
| C1: CsWin32 maps extension HRESULTs (E_NOTIMPL, E_NOINTERFACE) to non-COM exceptions; hovering 7-Zip or using Send to crashed NeoFences | catch everything but OOM in the menu and file-operation paths; log | reviewer probe (failing HRESULTs); live: 7-Zip submenu hovered, Send to → Desktop (create shortcut) ran, app alive |
| I1: Excel / atomic-writer saves (visible temp file) still sent the document to the Inbox | a rename onto a remembered name drops the temp and returns the document | 2 Core tests RED→GREEN |
| I2: typing `` or `..` in the rename box moved the file off the Desktop | `FileNames.IsValidNewName` checked in `TryRename` | Core tests RED→GREEN; live: `subinside` refused |
| I3: a desktop change during a rename could commit half-typed text | open renames are cancelled before a rebuild | live: interrupted rename committed nothing |

Eleven minors deferred (ROADMAP "Carry-overs from the M3a review").

## Conclusion

M3a works on the real desktop. The [USER] rows remain: extended/multi-select menus, rename conflicts, special
items, the permanent-delete warning, and a real Word save.
