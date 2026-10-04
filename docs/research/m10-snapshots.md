# M10 — Snapshots: results

**Date:** 2026-10-03 · **Machine:** Windows 11 Pro 25H2 (26200), NeoFences 1.2.0 installed.
**Build:** M10 prototype (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section W · **Decision:** ADR-030.

## Live smoke on the prototype (installed copy restored; test snapshots deleted through the app → Recycle Bin)

| Check | Result |
|---|---|
| W1 Settings → Take snapshot | **Pass**: one row, one file. |
| Shuffle: move "New fence" 360 px left, add `nf-m10-new.txt` to the Desktop | the file landed in the Inbox. |
| W2/W3 Restore from Settings | **Pass**: New fence back at x 1311; the new file stayed in the Inbox; `before-restore.json` written. |
| W5 Restore "Before restore" | **Pass**: New fence back at the moved x 951. |
| W1 Tray → Take snapshot | **Pass**: 3 files (two snapshots and before-restore). |
| W7 Delete from Settings | **Pass**: rows 0, files 0 (in the Recycle Bin). |
| Log | 0 warnings or errors. |
