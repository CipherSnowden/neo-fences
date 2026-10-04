# M2c — Fence interactions: verification results

**Date:** 2026-10-02 · **Machine:** Windows 11 Pro 25H2 (26200), one 1920×1080 monitor at 100 %, Wallpaper Engine.
**Build:** `m2c-fence-interactions` (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section I.

The agent ran two scripted smokes with the user's consent: `m2c-smoke-interactions.ps1` and `m2c-smoke-final.ps1`
(in the plan). They drove real mouse and keyboard input on the desktop. The interaction smoke arranges its own
starting state through the config and restores it afterwards.

## Results

| ID | Result | Evidence |
|---|---|---|
| I1 | **Pass** | Menu reads: New fence, Rename fence, Icon size ►, Lock position, Delete fence (items go to the Inbox), Hide desktop icons, Exit. The Inbox has no Delete (screenshot). |
| I2 | **Pass** (Enter) | Rename → "Games" + Enter: the title showed and was saved. Esc, blank and long titles are pinned by the `Rename_*` tests; Esc was not run live. |
| I3 | **Pass** | Large (64) re-rendered sharp and was saved (`iconSize: 64`). Shortcuts whose icon has no large image get a white frame, which matches Explorer's large-icon view. |
| I4 | **Pass** | Locked: a title drag of (−60, −100) px left the fence in place. Unlocked: the same drag moved it 60 px. |
| I5 | **Pass** | A fence created from the menu and then deleted: the fence count returned to its previous value. **Note:** new fences cascade onto (24,24), where an older empty fence already sat, so the click deleted the top (older) one. Both were empty and nothing was lost. The cascade overlap is a known M1 minor. |
| I6 | **Pass** | A slow 120 px drag (2 px steps) escaped snapping. Dragging back to 1 px left / 4 px above the target snapped to (Inbox.X, Inbox.Bottom + 8) exactly. |
| I7 | **Pass** | A right edge slow-dragged out 40 px gave w = 360; dragged back to 6 px past the Inbox's right edge, it lined up with it. |
| I8 | **Pass**, with a limit | Arrows moved the selection, and Enter opened Recycle Bin in front, **when the desktop had focus** before the click. See finding 5. |
| I9 | **Pass** | Switching `AppsUseLightTheme` and broadcasting `ImmersiveColorSet` → log `Windows app mode changed; light: true/false`. In light mode the veil is light and the text dark and readable; dark mode is unchanged. |
| I10 | [USER] | Wallpaper-dependent. Label shadow and halo were visible in the screenshots. |
| I11 | **Pass** (visual) | Thin rounded scrollbar without arrows; the mouse wheel scrolls. |
| I12 | [USER] | Needs a display scaling change. The window's own `DpiChanged` reloads icons once (finding 1). |

## Findings from the prototype (all fixed, or decided, before the plan)

1. **`DpiChanged` is a routed event.** Every new item raises it on its first measure. Reloading icons from the window's
   handler therefore created new items, and so on: the prototype spun to 1.4 GB. Fix: react only when
   `OriginalSource == this` and the DPI actually changed.
2. **Snapping the `WM_MOVING` proposal pins the fence.** Windows proposes "current (snapped) rect + this mouse step",
   so every 2–3 px step snapped back. Fix: track the unsnapped rect (`Snapping.Unsnapped`) and snap that.
3. **Editing the attached `WindowChrome` in place** applied the lock but never the unlock. Fix: set a fresh
   `WindowChrome` each time.
4. **The accent blur ignores its tint colour** (`ACCENT_ENABLE_BLURBEHIND`): changing it changed nothing. The dark
   look the user approved is plain blur. Light mode draws its own veil (~72 % #F2F2F2) as the fence background.
5. **Activation needs the desktop's focus.** A click activates a fence (keyboard works) only when the desktop had
   focus: after Win+D, or after a click on the desktop or a fence. With another app in front, the click did not
   activate it. Neither `Activate()` on mouse-down nor answering `WM_MOUSEACTIVATE` with `MA_ACTIVATE` changed that,
   which fits the fence sharing Explorer's input queue as its owned window (M0 finding 9). Mouse use is unaffected.
   Follow-up in ROADMAP.

## Final review fixes (opus reviewer, 0 critical / 1 important / 6 minor)

| Finding | Fix | Verified by |
|---|---|---|
| I1: with "Show window contents while dragging" off the window stays put until release, so tracking from the window rect made the outline run away (fence saved off-screen) | `DragTracker`: each step is based on the rect written last (Core) | 2 Core tests RED→GREEN, 115/115; interaction smoke all True |
| M1 (re-graded up): rename box stuck open when the fence cannot take focus | give up the rename if the box cannot get focus | live rename and rename back |

Five minors deferred (ROADMAP "Carry-overs from the M2c review").

## Conclusion

M2c works on the real desktop. I10 and I12 remain for the user, and keyboard focus from another app is a known limit.
