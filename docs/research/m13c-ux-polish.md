# M13c — v1.6 carry-overs, fence and Settings UX: results

**Date:** 2026-10-04 · **Machine:** Windows 11 Pro 25H2 (26200), NeoFences 1.6.1 installed.
**Build:** M13c prototype (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section Z (Z19–Z30) · **Decision:** ADR-035.

## Live check on the prototype (the PC unattended, standing go; config and installed copy restored)

| Check | Result |
|---|---|
| Z19 right-click on an uncovered fence title | **Pass**: the fence menu opened; no Windows menu. |
| Z21 tray → Restore snapshot (only "Before restore" on disk) | **Pass**: "Undo the last restore \| — \| More in Settings…" (before: a separator came first). |
| Z22 More in Settings… | **Pass**: Settings opened with the Snapshots card in view. |
| Z23 Settings → Take snapshot | **Pass**: the card said `Saved "Snapshot 4 Oct 04:11".` |
| Z27 a `.url` to `steam://rungameid/…` created, then deleted on the Desktop | **Pass**: in the library, then gone, each without Refresh library. |
| Log | 0 warnings or errors. |

**Test-harness lesson.** `Shell.Application.MinimizeAll()` left Explorer and Firefox windows in front of the fences.
The first run's right-click "inside the fence rect" opened Explorer's folder menu instead; Escape closed it and nothing
changed. The script now checks `GetAncestor(WindowFromPoint(pt), GA_ROOT)` is the fence, minimizes covering app windows
for the check and restores them afterwards.

## Core (test-first)

8 new tests (402 in all): tray labels as one safe line (accelerator, newline, tab, blank, 300 characters), same-place
reorder and same-colour picks returning the config unchanged, and hotkey labels from the layout with the US fallback.

## Branch check after the final review (config and installed copy restored)

| Check | Result |
|---|---|
| Z19 title right-click, fences unlocked and all fences locked | **Pass** both: the fence menu, no Windows menu |
| Z21–Z23, Z27 as above | **Pass** in both runs |
| Log | 0 warnings or errors. |
