# M32 — Polish (0.19.1): build notes and results

Spec: `docs/superpowers/specs/2026-10-06-polish-0.19.1-design.md` · Plan: `docs/superpowers/plans/2026-10-06-m32-polish.md`

## Build

- The M31 deferred minors and the Free-layout bug found while setting up the user's desktop (a Free fence written
  without cells showed them arranged but stored them only at the next item change).
- The sticky threshold is 2× and **1 GB** more (ruling): with the spec's 256 MB its own example would still flip.
- The date line: a long date pattern ending with `dddd` puts the weekday last — checked for ja-JP ("10月5日 月曜日"),
  ko-KR, zh-CN; en-GB, en-US, de-DE keep "weekday, month-day".
- Checked headless against the user's running Afterburner: readings unchanged (CPU 50 °C, GPU 47 °C, RAM / 32 GB).
- Replay-verified on `main` (10 build errors before the code, 697 tests after, 0 warnings).

## Review (Opus, whole branch)

0 critical, 3 important (fixed: the first-start notice was never armed when the first start happened during a game; the
date line with Windows' own ja/zh regional formats, which leave the weekday out, put it first again — the culture's
default order decides now, quoted literals ignored; WPF refreshes its accent colours on WM_DWMCOLORIZATIONCOLORCHANGED,
which can come after "ImmersiveColorSet" — the bars are rebuilt on that message too), plus the `Page` space and a misplaced
doc comment. 700 tests. Deferred: a hybrid laptop's first choice at an idle start can be the built-in GPU (ADR-052 wording
corrected); a Free fence that loads rolled up may be pinned one column narrower; the fence look's own Windows accent is
not refreshed on an accent change (pre-existing).

## Live check (branch Release build, backed-up data)

| ID | Result |
|---|---|
| AR1 | **Pass**: Settings → Colors → Blue (scripted, with the user's OK): the bars went from red to blue at once (pixel count red 83 → 0, blue 0 → 103). Restoring: the first "Red" item is a recent-colours copy that does not apply; the grid's "Red" does — the user's accent is back (WPF #E81123, DWM red) and the installed copy was restarted so its bars read it. |
| AR2 | **Pass**: a Free fence written without cells: 0 of 3 before start, 3 of 3 stored right after (log "free layout: stored the cells shown at first layout"). |
| AR3–AR8 | [USER] (AR8 regional format not switched, by the user's choice) |
