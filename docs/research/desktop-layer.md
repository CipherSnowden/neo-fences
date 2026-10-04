# Desktop layer research (M0 spike)

Spike: `spikes/M0.DesktopLayer` (throwaway). Checklist IDs: `docs/TEST-CHECKLIST.md`.

**Machine:** Windows 11 Pro 25H2 build 26200.9457 (registry ProductName still says "Windows 10"), NVIDIA GeForce RTX 5070 Ti + AMD Radeon iGPU,
1 monitor 1920×1080 @ 100% (96 DPI), Wallpaper Engine running (wallpaper64), taskbar auto-hide off.

**Which fence each row used.** Most rows ran on the **glass** fence (extended DWM frame, the
spec's original design). The **layered** fence (ADR-011's winner) only existed in the final
8-minute run, which covered A1–A3 and Win+D. Rows marked "glass" must be re-run on layered +
owned in M2 (see "M2 re-verification" below).

## Results

| ID | Result | Fence | Notes (mode/strategy, what was seen, log line) |
|---|---|---|---|
| A1 | PASS (layered) | both | Glass + DWMSBT_TRANSIENTWINDOW: flat grey whenever inactive (always). Layered + accent blur: wallpaper visibly blurred through |
| A2 | PARTIAL | layered | Static frame only: a screenshot showed the Crysis wallpaper (Wallpaper Engine) blurred through. Animation motion and stutter not judged |
| A3 | PASS (layered) | both | Glass: every mode flat (DWM grey when inactive; accent black even with SYSTEMBACKDROP=AUTO). Layered, fence inactive: tint-only = see-through sharp; accent blur = soft blur (best); accent acrylic = blur, too dark at 60% tint; acrylic light = blur + noise. Layered windows lose DWM rounded corners |
| A4 | PASS (glass only) | glass | user: drag + resize smooth. Not judged on the layered fence |
| A5 | PASS | glass | user: not on taskbar, not in Alt+Tab |
| B1 | PASS | glass | user: app window covers the fence |
| B2 | PARTIAL | glass | Fence never draws above apps, but a click ACTIVATES it (log: foreground -> fence) despite WS_EX_NOACTIVATE. Possibly WPF/WindowChrome, possibly the input-queue attachment of a cross-process owner (finding 9). Acceptable: keyboard shortcuts need focus anyway |
| B3 | PASS (owned) | both | Class on Win+D: Progman. bottom-only: vanishes. raise-on-Win+D: vanishes (HWND_TOP applied on the Progman foreground event, then Explorer raises the desktop over it). owned-by-Progman: stays visible (glass and layered) |
| B4 | PASS | glass | owned: taskbar Show-desktop corner keeps the fence; second click restores windows |
| B5 | PASS | glass | owned: windows return above the fence |
| B6 | PASS | glass | agent: Win+M / Win+Shift+M, fence never iconic, stays visible |
| B7 | PASS (forced kill only) | glass | owned fence survived `Stop-Process -Name explorer -Force`; TaskbarCreated received. NOT shown: that ownership actually re-attached to the new Progman (no Win+D afterwards, `FindWindow` result unchecked). Graceful restart (Task Manager "Restart", Restart Manager) not tested |
| B8 | DEFERRED | — | Never run with an elevated Task Manager focused. The user pressed Win+D repeatedly while a game ran, and the fence stayed |
| B9 | PASS | glass | baseline confirmed: bottom-only fence disappears on Win+D |
| F1 | N/A | — | single monitor |
| C1 | PASS | glass | agent: hide -> desktop blank (screenshot), query hidden=True |
| C2 | PASS | glass | agent |
| C3 | PASS | glass | agent, icons hidden: clean close restores icons in OnExit; watchdog "clean shutdown, nothing to do" |
| C4 | PASS | glass | agent, icons hidden: FailFast -> watchdog UNCLEAN, icons restored ~30 ms after detection, main restarted. User also hit CRASH once by accident: same result |
| C5 | PASS | glass | agent, icons hidden: Stop-Process -Force -> UNCLEAN after ~100 ms, FWF_NOICONS cleared cross-process, main restarted ~400 ms later |
| C6 | PASS | glass | agent: 4th crash within 10 min -> "restart limit reached", icons restored, stays down |
| C7 | PASS (forced kill only) | glass | agent: icons re-hidden after a forced Explorer kill, in 1 attempt |
| C8 | DEFERRED (user decision) | — | Synthetic WM_QUERYENDSESSION (to the main window and to the WPF hidden window) produced no SessionEnding log line; WPF's handler may simply not be reachable that way. Real sign-out deferred by the user ("yes lets skip sign out for now", 2026-10-02). Also unverified: whether Explorer even persists FWF_NOICONS across sign-out |
| D1 | PASS | glass | GESTURE DoubleClick logged on empty desktop |
| D2 | PASS (glass only) | glass | no gesture for double-clicks on fence / taskbar. Not run over the layered fence (1/255-alpha body) |
| D3 | PASS | glass | RightDragStarted/Completed logged; Explorer menu appears (baseline) |
| D4 | FAIL | glass | S1: menu suppressed after drag, but the desktop locks up: left-click opens a frozen menu until another right-click (list view never got its RBUTTONUP) |
| D5 | PASS | glass | S2: drag shows no menu; plain right-click replayed ("replayed right-click (sent 2/2)") and the normal menu appears |
| D6 | PASS (unit) | — | not run by hand; covered by RightDragStartedOverApp_IsIgnored |
| D7 | DEFERRED | — | elevated foreground not run; re-check in M2 |
| D8 | PASS | glass | user: no perceptible mouse lag, including while gaming |
| D9 | PASS | glass | agent: start -> "WH_MOUSE_LL installed: True", stop -> "WH_MOUSE_LL removed" |
| E1 | PARTIAL | — | AC Black Flag Resynced (ScimitarEngineWindowClass). On activation (16:44:25, 16:50:08, 16:53:34) quns=QUNS_ACCEPTS_NOTIFICATIONS -> gameLike=False. QUNS flipped to QUNS_BUSY ~1 s later (16:53:35) with no foreground change in between, so only re-checks caught it. coversMonitor=False on every event (unexplained for a full-screen game); hasCaption flipped True -> False after activation |
| E2 | N/A | — | no exclusive-fullscreen game tested |
| E3 | PASS | — | Firefox (MozillaWindowClass) maximized: gameLike=False |
| E4 | N/A | — | taskbar auto-hide is off on this PC |
| E5 | PASS | — | Progman/WorkerW never game-checked |
| F2 | N/A | — | single monitor |

## Additional findings (not in the checklist)

1. **Windows 11 25H2 behaves like 24H2.** On Win+D the foreground window is `Progman` (no top-level
   WorkerW), and Wallpaper Engine renders inside Progman's WorkerW child.
2. **Watchdog wrote its own clean-shutdown marker** (`App.OnExit` ran in watchdog mode). A later
   main process reusing that PID would look "clean" after a crash and keep icons hidden. Fixed in
   the spike: watchdog skips `OnExit` cleanup; `Spawn` deletes any stale marker for its own PID.
3. **Process-tree kill takes the watchdog down too.** When the launching Claude session ended, the
   lab and its child watchdog died together with no restore. M2: start the watchdog detached
   (not a child of the main process / outside its job) so "End process tree" or a parent job
   kill cannot remove both.
4. **QUNS was the only signal that caught the game, but it is system-wide and late.**
   `QUNS_BUSY` detected AC, and the window checks never did (`coversMonitor` stayed False). But
   (a) it reads BUSY for every foreground window while the game runs (terminal, lab, shell popups),
   and (b) it flipped to BUSY about 1 s **after** the game became foreground, so a check that runs
   only on foreground changes misses the game. M6 rule: game mode = (`QUNS_BUSY` or
   `QUNS_RUNNING_D3D_FULL_SCREEN`) **and** the foreground window is a non-shell, non-lock-screen app.
   Re-check shortly after activation (a short timer or `EVENT_OBJECT_LOCATIONCHANGE`), and find out
   why the cover-monitor check failed (resize after activation? DPI or rect quirk?).
5. **Lock screen is a false positive.** `Windows.UI.Core.CoreWindow` with `QUNS_NOT_PRESENT`
   covers the monitor without a caption. M6: exclude `QUNS_NOT_PRESENT` and the lock app.
6. **Clicking a fence activates it** despite `WS_EX_NOACTIVATE` (B2). It never rises above apps
   (owned by Progman), so this is acceptable; M2 decides whether to keep activation (needed for
   keyboard shortcuts) or answer `WM_MOUSEACTIVATE` with `MA_NOACTIVATE`.
7. **Owned-by-Progman survived a forced Explorer kill.** The fence was not destroyed when
   `explorer.exe` was killed. Not yet shown: re-attachment to the new Progman (verify with
   `GetWindow(fence, GW_OWNER) == Progman` inside the retry loop), and behaviour on a graceful
   restart, where Progman is destroyed through the normal `DestroyWindow` path.
8. **Extended DWM glass is never see-through on Windows 11.** With `DwmExtendFrameIntoClientArea(-1)`
   and no active system backdrop, the client area is solid black; system backdrops go solid grey on
   inactive windows; `SetWindowCompositionAttribute` accent has no visible effect on such a window
   (with SYSTEMBACKDROP NONE or AUTO). A layered window (`WS_EX_LAYERED`, WPF `AllowsTransparency`)
   plus accent blur does work, independent of focus.
9. **Owner-by-Progman carries two risks of its own.** (a) Changing the owner after creation via
   `GWLP_HWNDPARENT` is undocumented; the docs say ownership cannot be transferred after creation.
   It is in the same class as the accent API. (b) A cross-process owner/owned pair attaches the two
   threads' input queues (Raymond Chen). A stall on the fence UI thread (GC, a synchronous shell COM
   call, an `IContextMenu` or `DoDragDrop` modal loop, a debugger break) could freeze desktop input,
   and the reverse. M2 test: a button that blocks the fence UI thread for 10 s, while checking that
   the desktop right-click menu and the taskbar stay responsive.
10. **Session-end handling must survive a cancelled shutdown.** The spike's handler restores icons
    **and** writes the clean-shutdown marker on the query. If the user then cancels shutdown, the
    app keeps running with a stale marker: a later crash looks clean, and the icons stay hidden.
    M2 design: restore icons on `WM_QUERYENDSESSION`; write the marker only on
    `WM_ENDSESSION(wParam=TRUE)`; on `WM_ENDSESSION(FALSE)` re-apply Takeover and write no marker.

## Conclusions

**Verdict: GO, conditional on C8.** The remaining assumptions hold once three spec choices are
swapped for what the spike proved. The winning combination (layered + owned) was only lightly
exercised, so M2 starts with a re-verification block.

**§4.1 fence window — GO with a different window type.** The spec's DWM system backdrop
(DWMSBT_TRANSIENTWINDOW) only blurs while the window is active. Fences are almost never active,
so it shows flat grey. Winner: **layered window + `ACCENT_ENABLE_BLURBEHIND`** (spike mode
`AccentBlur` on the layered fence). The tint (`0x40201A16`, intended ~25%) was not shown to apply
in state 3 without `AccentFlags`, so M2 must vary it and confirm. Fallback if the undocumented accent
API ever breaks: layered **tint only** (see-through, no blur). Costs:
- square corners: round them ourselves in M2, e.g. with a rounded window region;
- an undocumented API: keep it behind one function;
- untested drag smoothness and animation on layered windows: check in M2.

**§4.2 Win+D — GO with owned-by-Progman.** Raising on the Progman foreground event loses the race,
because Explorer raises the desktop afterwards. Making Progman the fence's owner keeps the fence just
above the desktop through Win+D, the Show-desktop corner and Win+M, while every app window still
covers it. It survived a forced Explorer kill. This supersedes the raise strategy in ADR-003. Its
own risks (finding 9) go into M2's checks.

**§4.3 icons — GO, conditional on C8 (user decision 2026-10-02).** `FWF_NOICONS` via `IFolderView2`
works cross-process. The watchdog restored icons within ~30 ms of detecting every crash type, and the
3-per-10-minutes limit works. The plan's gate also required C8 (sign-out); the user chose to defer it
to M2. **Takeover must not ship enabled by default until C8 passes.** M2 must add:
- a detached watchdog (finding 3);
- session-end handling that survives a cancelled shutdown (finding 10), then a real sign-out test;
- the stale-marker fix (finding 2).

**§4.4 Explorer restart — GO for a forced kill.** `TaskbarCreated` arrives, and re-hiding icons
succeeds on the first attempt. Re-ownership and graceful restarts are still to verify (finding 7).
Keep the 5 s retry.

**§4.5 gestures — GO with S2.** Double-click detection works and ignores fences and the taskbar.
Right-drag suppression S1 (swallow the release) breaks Explorer's desktop state. **S2 (swallow the
press, replay a plain right-click)** is clean. No perceptible mouse lag, even while gaming.

**§4.7 game mode — partial; M6 must redesign the trigger.** Only QUNS caught the game. It is
system-wide and arrives about 1 s after activation, so the spec's "evaluate on foreground change
only" would miss it, and the mouse hook would stay installed in games, breaking ADR-007's
zero-latency promise. The M6 rule is in finding 4.

## M2 re-verification (carried into ROADMAP)

- On **layered + owned**: re-run A2 (animation, stutter), A4 (drag/resize), B2, B7 (forced **and**
  graceful restart, with ownership checked via `GW_OWNER`) and D2 (hit-testing over the 1/255-alpha body).
- UI-thread hang test (finding 9).
- Session-end handling per finding 10, then a real sign-out (C8). Also check whether `FWF_NOICONS`
  persists across sign-out.
- Elevated foreground: B8, D7.
- Accent tint actually applied (vary `GradientColor` in state 3).
