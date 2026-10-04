# M13a — v1.6 carry-overs, data safety: results

**Date:** 2026-10-04 · **Machine:** Windows 11 Pro 25H2 (26200), NeoFences 1.5.0 installed.
**Build:** M13a prototype (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section Z · **Decision:** ADR-033.

## Live check on the prototype (no mouse or keyboard; config and installed copy restored)

| Check | Result |
|---|---|
| Z1 first start of the prototype | **Pass**: `schemaVersion` 1 → 2 in the saved config. |
| Z4 a second copy waiting for the first, then `--exit` | **Pass**: the waiting copy logged "already running … exits" and gave up; the running one exited; 0 copies left. |
| A plain start after that `--exit` | **Pass**: started and ran normally (the stale signal is reset by the owner). |
| Log | 0 warnings or errors. |

Not run live: Z5/Z6 need the LOCO_DUCK stick ejected (the user would have to replug it); they are covered by the
final review and left as hand checks.

## Core (test-first)

9 new tests (373 in all): schema 2 and the upgrade of a version-1 file, a newer file never overwritten, duplicate rule
ids, snapshots from a newer version refused, a huge stray file skipped, no temp file after a failed save, a restore
with a Portal, case-only renames and newcomers in the Desktop's order, a snapshot with null fields, the library index
after failed writes and deletes.

## Branch check after the final review (no input automation; config and installed copy restored)

| Check | Result |
|---|---|
| Z1 schema | **Pass**: 1 → 2. |
| Z4 a waiting copy and `--exit` | **Pass**: the waiting copy gave up; 0 copies left. |
| A later start | **Pass**: main + watchdog running. |
| `--exit` then a start at once (review M1) | **Pass**: the new copy waited for the exiting one and started (1 start, none gave up). |
| Log | 0 warnings or errors. |
