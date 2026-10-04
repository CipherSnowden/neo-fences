# M17 — Public releases and auto-update: results

**Date:** 2026-10-04 · **Machine:** Windows 11 Pro 25H2 (26200), NeoFences 1.7.1 installed.
**Checklist:** `docs/TEST-CHECKLIST.md` section AC · **Decision:** ADR-039.

## Making the repository public (done before the first push, with the user's explicit go)

| Step | Result |
|---|---|
| Mirror backup of every branch and tag | 21 refs; local only. |
| History rewrite (`git filter-branch`, 376 commits, ~3 min) | every author/committer now the GitHub noreply address; the private hub link replaced in every commit. |
| Final scan of all 377 commits (contents, messages, authors) for the personal address, name, hub link/id, Claude links, GitHub/Anthropic tokens, AWS keys, private keys | **0 hits**. |
| Push | `main`, the parked `m15-search-palette` branch and 14 tags to the public repo. |

## Local update rehearsal (the user's go; installed 1.7.1 and config restored afterwards)

Prototype packed as 1.8.0 and 1.8.1 (with a delta) into a local folder; 1.8.0 installed silently and restarted with
`NEOFENCES_UPDATE_SOURCE` pointing at the folder.

| Check | Result |
|---|---|
| Settings → Updates → Check now | **Pass**: "Version 1.8.1 is ready" after ~12 s (download); button "Restart to update to v1.8.1". |
| Restart to update | **Pass**: icons shown, clean exit, "update 1.8.1 will install now that NeoFences exits (restart: true)"; 1.8.1 running 1.6 s later, config loaded, icons hidden again. |
| Data | 2 fences before and after; 0 warnings or errors. |
| Restore | 1.7.1 reinstalled and running; config byte-identical; no test-made backup left. |

`pack.ps1 -ReleaseNotes` checked locally: the notes are embedded in the package.

## Core (test-first)

7 new tests (438 in all): first check at start, then daily; 6 h after a failure; never when off, in a developer build
or in game mode; when the next check is due; `AutoUpdate` on by default, schema 4, round trip.

## Rehearsal 2 after the final review (the user's go; installed 1.7.1 and config restored)

| Check | Result |
|---|---|
| Start NeoFences.exe again while 1.8.1 waits | **Pass**: same processes, still 1.8.0 (Velopack's apply-on-start is off). |
| Normal exit (`--exit`) | **Pass**: icons shown, "update 1.8.1 will install … (restart: false)", 1.8.1 installed, NeoFences stays closed. |
| Next start | **Pass**: 1.8.1 running, 2 fences, 0 warnings. |
