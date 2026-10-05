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
