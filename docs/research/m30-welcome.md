# M30 — First-run welcome (0.18.0): build notes and results

Spec: `docs/superpowers/specs/2026-10-06-welcome-design.md` · Decision: ADR-051 · Plan:
`docs/superpowers/plans/2026-10-06-m30-welcome.md`

## Build

- Built in a scratch worktree and replay-verified on `main` (the tests patch fails to build without the code: 13 errors;
  655 tests with it; 0 warnings).
- Every way an item reaches a fence (drops, Add item…, widgets, folder panels, games, auto-collect, Add from desktop)
  goes through `ItemsChanged`, so the welcome ends whichever way the first item arrives.
- Drops are taken at the fence window, not its list, so dropping on the welcome panel adds to the fence.
- Hide icons: Add from desktop already offered "Hide desktop icons while NeoFences runs" (applied after the items), so
  the welcome reuses it (spec §4 ruling).

## Review (Opus, whole branch)

0 critical, 1 important (fixed: a read-only fresh start — an older build on a newer config — showed the welcome and the
notice on every start; now never), one minor raised and fixed (a sort removed an empty welcome fence the user had renamed
or given auto-collect rules; now only an untouched "Fence" goes). 658 tests.

Deferred minors:
- The heading uses the title ink: a light Title-strip colour in dark mode makes it dark on glass (the default look reads).
- At the minimum fence size "Sort my desktop…" is clipped on the right (vertical scrolling works).
- An open Settings keeps listing the removed welcome fence under "New games go to" until reopened.
- The first-start notice is lost when the tray icon's first add fails (the retry path does not show it).
- A duplicate `UpdateEmptyHint` call in `ApplyFence`; `EndWelcomeIfFilled` refreshes every window, not just the one.

## Live check (branch Release build, fresh data folder; data and Run value backed up and restored)

| ID | Result |
|---|---|
| AP1 | **Pass**: one fence "Fence" with the welcome flag, the heading and all three buttons. The tray notice was not caught in the screenshot taken ~7 s after start (Windows shows it briefly, or Focus Assist held it) — [USER] |
| AP2 | **Pass**: Guide brought Firefox to the front on the guide's GitHub page. |
| AP3 | [USER] (minimum size and light mode by hand; see the deferred minor on the clipped button) |
| AP4 | [USER] |
| AP5 | **Pass**: Sort my desktop… → Cancel kept the welcome. |
| AP6 | **Pass**: the default groups made Games, Apps, Folders and files (31 links); the welcome fence was removed; the dialog offered "Hide desktop icons while NeoFences runs". |
| AP7 | [USER] |
| AP8 | **Pass**: after a restart the welcome was still there. |
| AP9 | **Pass**: the restored data started with the installed copy: 3 fences, no welcome. |

The welcome screenshot (`docs/guide/welcome.png`) is from this run, cropped to the fence.
