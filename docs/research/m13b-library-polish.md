# M13b — v1.6 carry-overs, Game Library and rules polish: results

**Date:** 2026-10-04 · **Machine:** Windows 11 Pro 25H2 (26200), NeoFences 1.6.0 installed.
**Build:** M13b prototype (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section Z (Z9–Z18) · **Decision:** ADR-034.

## Read-only probes

- Scanner probe on the user's PC: the same 12 games; Epic's Mafia DE still listed (category "games", no main game);
  a second scan reusing remembered programs took 33 ms instead of 173 ms.
- Package names: `ms-resource:AppStoreName` (Calculator) and `ms-resource:Resources/AppStoreName` (Notepad) resolve to
  "Windows Calculator" / "Windows Notepad" through `SHLoadIndirectString`.

## Live check on the prototype (the PC unattended, standing go; config and installed copy restored)

| Check | Result |
|---|---|
| A stray `.<32 hex>.lnk` in `library\` and a file NeoFences did not write | **Pass**: the stray was swept; the other file was kept. |
| Z12 a Desktop shortcut game | **Pass**: `AC Black Flag Resynced.lnk` in the library is byte-identical to the Public Desktop one. |
| Hide → Settings | **Pass**: remembered in the index as "AC Black Flag Resynced (2 ids)"; one Settings row by name; Show again cleared both ids. |
| Z16 rules editor open | **Pass**: Add and Delete disabled while editing; enabled again after Cancel. |
| Log | 0 warnings or errors. |

## Core (test-first)

19 new tests (392 in all): Epic games vs DLC and engines, a shortcut into a launcher's game folder, launcher links in
shortcut arguments, plain source names, remembered hidden games (also while their source is away), lost files dropped
from the index, the one-time pre-schema-2 config copy.

## Branch check after the final review (config and installed copy restored)

| Check | Result |
|---|---|
| Stray temp swept, other file kept; shortcut copied as is | **Pass** |
| Hide → one Settings row by name with both ids; Show again | **Pass** |
| Rules editor locked while open | **Pass** |
| Log | 0 warnings or errors. |
