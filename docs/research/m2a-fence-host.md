# M2a fence host verification

Build: branch `m2a-fence-host` (fix `bec4514` included). Machine: see `desktop-layer.md` (Windows 11 25H2,
one 1920×1080 @ 100% LG monitor, Wallpaper Engine). Date: 2026-10-02.
Fence type under test: layered (AllowsTransparency) + accent blur, owned by Progman (ADR-011).

| ID | Result | Notes |
|---|---|---|
| G1 | PASS (after fix) | Rounded corners clip the blur (SetWindowRgn works on the layered window); blur + border visible. Found: a freshly shown fence sat ABOVE app windows (Show puts it on top) — fixed by `FenceWindowChrome.SendToBack` after Show and after re-attach (commit `bec4514`); re-screenshot shows the terminal covering the fence |
| A2 | PASS (automated) | two screenshots of a fence 1 s apart: 1055 of 1250 sampled pixels changed, so the live Wallpaper Engine animation shows through the blur. Stutter not judged |
| A4 | pending user | drag/resize smoothness |
| Tint | PASS | user: current tint is fine (from screenshots); a tint preference is a later feature |
| B2 | PASS (automated) | Notepad placed over part of a fence; clicking the fence title left Notepad on top of the overlap |
| B3 | PASS (automated) | real Win+D keystrokes (twice): all 4 fences visible and not minimized |
| B4 | PASS (automated) | clicked the taskbar Show-desktop corner: all 4 fences visible over the wallpaper (screenshot sent to the user); second click restores windows |
| B6 | PASS (automated) | Win+M then Win+Shift+M: fences never minimized, stay visible |
| B7 | PASS | forced `Stop-Process explorer`: "re-attach after Explorer restart: 1 attempt(s), unattached 0, icons ok true" |
| B8 | pending user | elevated Task Manager + Win+D |
| B10 | PASS | user restarted Explorer from Task Manager: two TaskbarCreated events, each "1 attempt(s), unattached 0" |
| B11 | PASS | user: desktop menu and taskbar responsive while the fence UI thread was frozen 10 s |
| G2 | PASS | 4 fences moved/resized; `--exit` + restart: window rects pixel-identical |
| G3 | PASS | user created 3 fences. Note: a new fence goes to the first free cascade slot, (24,24), even if another fence sits there (known M1 deferral, placement work in M2c) |
| G5 | PASS | smoke run: `--exit` → clean shutdown, watchdog "clean shutdown", no processes left |
| G6 | PASS | preview on, kill: watchdog "UNCLEAN exit" → "desktop icons restored (attempt 1)" → "main restarted"; new instance "desktop icons hidden: true" |
| G7 | PASS | preview off, kill: watchdog restarted main, no icon restore (marker absent) |
| G8 | PASS | second instance exits immediately (code 0); one main process |
| G4 | PASS (work area) | taskbar auto-hide toggled by code: work area 1032 → 1080 → 1032; fences re-placed, positions pixel-identical after the round trip, stored layouts unchanged. Display-scaling change not tested (cannot be scripted safely) |
| G9 | PASS (after review fix) | scripted WM_QUERYENDSESSION to every top-level window: "session ending (Logoff)" → icons restored inside the query → exit → watchdog "main restarted (session end was cancelled)" → icons hidden again. Fence windows answer 1 (never block shutdown) |
| G10 | PASS | double-click on a fence title: no maximize (WS_MAXIMIZEBOX removed) |
| C8 | pending user | real sign-out with preview on |
| Cancel | pending user | cancelled shutdown re-hides icons, no marker |

## Final review (fresh reviewer)

One Critical: WPF exits the app on WM_QUERYENDSESSION and Explorer COM calls fail inside that sent message, so
the planned session-end path could not work (and a cancelled shutdown would have left NeoFences gone). Fixed per
ADR-013 and verified by G9. Important fixes: the watchdog restores whenever `takeover-active` exists, a stale
marker is reconciled at startup, the Takeover toggle saves immediately, icon calls and the watchdog catch every
error, G4 was run. Promoted Minors: background-thread crash handler, no maximize box.

## Conclusions

**GO for M2b.** The layered + accent-blur + Progman-owned fence works on the real desktop:
- it blurs the live Wallpaper Engine animation;
- the rounded window region clips the blur;
- it survives Win+D, the Show-desktop corner, Win+M, a forced **and** a graceful Explorer restart, a
  frozen UI thread, kills (with and without the icon preview) and restarts;
- it never rises above apps;
- it saves positions pixel-exactly.

One bug was found and fixed: a freshly shown fence sat above app windows (`bec4514`).

**Still open, gating the Takeover work in M2b (ADR-011):**
- C8 real sign-out with the preview on;
- the cancelled-shutdown path (logic is unit-tested by `SessionEndHandlerTests`; the real
  Windows behaviour is unverified);
- B8 elevated foreground (needs a UAC prompt, so it can't be automated);
- A4 drag smoothness and the tint preference (user judgement).

Takeover must not become a default or first-run feature in M2b until C8 passes.
