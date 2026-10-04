# M2b — Desktop items: verification results

**Date:** 2026-10-02 · **Machine:** Windows 11 Pro 25H2 (26200), one 1920×1080 monitor at 100 %, Wallpaper Engine.
**Build:** `m2b-desktop-items` (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section H.

The agent ran these checks with scripted input (mouse_event, keybd_event, file APIs) while the user was remote. Screenshots of the first two were sent to the user.

## Results

| ID | Result | Evidence |
|---|---|---|
| H1 | **Pass** | First reconcile on the real desktop: `29 added to the Inbox, 0 removed`. That is Recycle Bin, 14 items from the user's Desktop and 14 from the Public Desktop, with Recycle Bin first. |
| H2 | **Pass** | Takeover was off and the question unanswered. The banner showed in the Inbox only. "Not now" saved `takeoverPromptAnswered: true`, the icons stayed on the desktop, and the banner did not return after a restart. |
| H3 | **Pass** (100 % scale) | App shortcuts show their app icons with correct transparency (no black boxes). A PNG shows a real thumbnail. A shortcut to a missing target shows the generic icon. |
| H4 | **Pass** | Labels are Explorer names: no `.lnk`, and file extensions follow Explorer's setting (shown on this machine). Long names wrap to 2 lines and are cut with an ellipsis ("AMD Ryzen M…"). The tooltip shows the full name. |
| H5 | **Pass**, with a UX gap | Recycle Bin opened **in the foreground**: the Recycle Bin Explorer window became the foreground window, thanks to `AllowSetForegroundWindow`. A shortcut to a missing target logged `could not open …` and NeoFences kept running. **Gap:** the user gets no visible feedback, because .NET's `ErrorDialog = false` suppresses Windows' own "Problem with shortcut" dialog. |
| H6 | **Pass** | A file was created, renamed and deleted on the Desktop. The Inbox followed each step within about 1 s, and `config.json` matched (smoke script). |
| H7 | **Pass** | 500 files were written to the Desktop in 172 ms. All 500 were in the Inbox and saved within 4.3 s; all were gone within 3.2 s of deletion. No `lost events` and no warnings were logged, so the 64 KB buffer was enough. |
| H8 | **Pass** | Click selects; Ctrl+click adds to the selection; hover highlights. |
| H9 | **Pass** | Mouse wheel scrolls vertically, with no horizontal scrollbar. The WPF default scrollbar is plain white and looks out of place; a styled one is planned for M2c. |
| H10 | Not run | Needs a large video and a disconnected network drive. Icon loading runs on the background STA thread, so the UI cannot block on it. **[USER]**, optional. |
| H11 | **Pass** | "Hide them" in the banner hid the native icons, wrote the `takeover-active` marker and saved `takeover: true, takeoverPromptAnswered: true`. The banner then disappeared. |

## Limitations (accepted for M2b; tracked in ROADMAP)

1. **Silent failure when an item cannot be opened** (H5). Upgrade path: show Windows' own error UI (`ErrorDialog = true` with the fence as parent), or a small toast. Planned for M3 (shell actions).
2. **Recycle Bin icon does not switch between full and empty** while running. Also, toggling a special icon in "Desktop icon settings" shows up only after a restart. (Hiding/unhiding a file by attribute is live since review fix I2.) Upgrade path: `SHChangeNotifyRegister` (ADR-014).
3. **Icons are not re-rendered after a DPI change.** They are loaded at 48 DIP × the DPI at load time. Planned for M2c (icon size).
4. **No shortcut arrows**: `IShellItemImageFactory` does not draw overlays. To be decided in M2c, since Fences does not draw them either.
5. **Labels can be hard to read on bright wallpapers**: white text, no shadow. To be handled with M2c light/dark.
6. **No keyboard navigation** in fences (ADR-014). Planned for M2c.

## Final review fixes (opus reviewer, 0 critical / 6 important / 9 minor)

| Finding | Fix | Verified by |
|---|---|---|
| I1: incomplete-listing guard missed a whole unreadable Desktop (14 of 29 missing is not "more than half") and kept deleted items forever | listing reports unreadable folders; `Reconcile` keeps only their refs (ADR-014) | 2 Core tests RED→GREEN, 93/93 |
| I2: unhiding a file never put it in a fence | attribute changes → create/delete | live: hide removed it, unhide re-added it |
| I3: watcher stopped for good after a non-overflow error; one bad folder lost both watchers | recreate on error; per-folder try/catch | build + code review (cannot trigger on demand) |
| I5: an exception on the icon thread killed the app → crash loop on the same file | per-item catch + log | build + code review |
| I6: opening blocked the UI thread (network timeout, UAC) | open on a background thread | live: Recycle Bin still opens in front |
| I4: Word-style safe-save re-adds the document to the Inbox | **deferred** to before M3 drag-drop: today every item is in the Inbox, so nothing visible changes | ROADMAP |

Nine minors are deferred (ROADMAP "Carry-overs from the M2b review").

## Conclusion

M2b works on the real desktop. All non-optional checks pass; H10 is optional.
