# M5 — Desktop gestures: verification results

**Date:** 2026-10-03 · **Machine:** Windows 11 Pro 25H2 (26200), one 1920×1080 monitor at 100 %, Takeover on.
**Build:** `m5-gestures` (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section N.

## How it was run

The agent ran `m5-smoke.ps1` (in the plan) with the user's consent, while they were remote: on the prototype, and
again on the branch build. The M4 Portal smoke was re-run as a regression, because `FenceHost` and `FenceWindow`
changed. A second script (`m5-extra.ps1`) covered N3 and N10–N12.
- It added two temporary fences, one of them locked, through the config with the app stopped.
- It restored the config afterwards.

All passed.

## Results

| ID | Result | Evidence |
|---|---|---|
| N1 | **Pass** | A plain right-click on empty desktop opened Windows 11's desktop menu (two `Microsoft.UI.Content.PopupWindowSiteBridge` windows): the S2 replay works. |
| N2 | **Pass** | A right-drag from (300,700) by 300×180 created a fence at exactly 300,700, 300×180, saved, with its title rename open. |
| N3 | **Pass** | Right-drags starting on a fence, or on an Explorer window, created no fence. |
| N4 | **Pass** | A double-click on empty desktop hid every fence; the next one brought them back (Takeover on, so the icons stayed hidden). |
| N5 | [USER] | Needs Takeover off (it changes the user's setup). |
| N6 | [USER] | Same. The code path is `SetIconsHidden`, the same marked path as Takeover. |
| N7 | **Pass** | Over a maximized Explorer window: Ctrl+Alt+Space made all 4 fences topmost and on top at their centre. Esc, and then a click outside, each put the app back on top. |
| N8 | [USER] | Changes `peekHotkey`. |
| N9 | **Pass** (no restart) | A title double-click rolled the fence from 180 to 32 px, saved as `rolledUp`. Resting on the title opened it to 180 px; leaving closed it to 32; a double-click unrolled it. |
| N10 | **Pass** | Rolled up, the fence was moved by its title to 500,600 at 32 px with the stored height still 200. Unrolled, it was 300×200 at 500,600. |
| N11 | **Pass** | A locked fence rolled up to 32 px and back to 200 px. |
| N12 | **Pass** | "New fence" from the menu landed at 748,24, 320×220, clear of every other fence by at least 8 px. |
| N13, N14 | [USER] | Long use with games; an elevated foreground window. |

## Findings

1. **Peek raised only one fence.** Every fence is owned by Progman. Raising one with `HWND_TOPMOST` restacks its
   siblings. A sibling that was not yet marked as peeking ran `KeepAtBottom` in `WM_WINDOWPOSCHANGING`, which sent it
   back to the bottom. The fix: mark every fence as peeking first, then raise them all.
   - A probe confirmed that ownership by Progman does not block topmost: a Progman-owned test window became topmost
     both while owned and while un-owned.
2. **`SetCursorPos` never reaches `WH_MOUSE_LL`.** Only real input, or `SendInput`/`mouse_event`, does. Scripted
   drags made with it were invisible to the hook. Smokes move the pointer with `mouse_event` (absolute coordinates).
3. **A scripted Esc landing in Windows Terminal cancels Claude Code's running tool call.**
   - `MinimizeAll` does not move keyboard focus, and a right-drag from a fence that ended on the desktop popped
     Explorer's menu without taking focus.
   - Scripts now click empty desktop first, and skip any keystroke while a terminal is in the foreground.
4. **A right-drag that starts on a fence and ends on the desktop shows Explorer's desktop menu.** The right button
   comes up over the desktop. This is Explorer's own behaviour and needs no change; it is noted for users.
5. **A swallowed right-press never reaches Windows' key state.** `GetAsyncKeyState(VK_RBUTTON)` reads "up" during an S2
   drag, so it cannot detect a lost right-up (a review suggestion cancelled every drawing in the smoke). A lost drag is
   ended by the next left or right click instead (`RightDragCancelled`, tested).

## Final review (opus, whole branch)

0 critical. Fixed: I1 (hook title lookup sent WM_GETTEXT to the UI thread; fence menus ended Peek), I2 (a lost
right-up left the draw rectangle topmost), M1 (hover timer outlived deleted fences) and M2 (Explorer restart during
Peek left Esc captured). After the fixes the smoke passed again, including "Peek survives a fence menu". Its roll-up
line missed once on timing; a direct probe on "Games" (plain, and right after Peek + fence menu) rolled 223 → 32 → 223
both times. Deferred minors are in ROADMAP.
