# M27 — Auto-collect rules (0.16.0): build notes and results

Spec: `docs/superpowers/specs/2026-10-05-auto-collect-design.md` · Decision: ADR-049 · Plan:
`docs/superpowers/plans/2026-10-05-m27-auto-collect.md`

## Prototype (2026-10-05, worktree `neo_fences-m27proto`, branch `m27-proto`)

Built end to end before the plan: Core test-first (26 new tests, 632 in all), Shell, App; 0 warnings. Probed live on a
copy of the user's data (restored afterwards; the PC was unattended, standing go), with a rule on Apps watching the
probe's own folder `%USERPROFILE%\NeoFences-m27-test` (removed afterwards):
- the file there before the rule was not collected; `new.url` and `ToolSetup.exe` (an empty file, never run) became items
  in Apps within a second; `notes.txt` did not (no matching kind);
- removing `new.url`'s item: it stayed removed, after further changes and after a restart;
- `while-off.url`, created while NeoFences was closed, was collected at the next start;
- the dialog showed the rule ("NeoFences-m27-test · Apps and shortcuts, Installers") and "3 items here match now";
- NeoFences used 0.013 % of the machine over 30 s.
Probe-script lessons: the first `--exit` right after a start took ~45 s (the switch script now waits up to 90 s, so a
config edit never lands while the app runs); a test folder must start empty (left-over files are not "new").

### Where the build departs from the spec (the final review weighs them)

- Arrivals are found by comparing listings — files renamed or moved in while NeoFences runs count too (the spec named
  created and renamed); the settle is 2 s of quiet (final review I2).
- A rule's `Watermark` is updated when a listing had arrivals or was the first one (not on every change), so config.json
  is not rewritten while a file keeps being written.
- At start, the source's earliest watermark is used for all its rules.
- A rule whose settings change starts looking again from then (its watermark resets).
- "Add these N too?" is asked after OK for each new rule, from the listings the dialog made; at most the first 200.
- The fence menu shows the count ("Auto-collect… (2 rules)") instead of a separate mark.

## Final review (Opus, 2026-10-05): with fixes

One critical, three important; fixed (checklist rows AM12–AM15):
- **C1** — the desktop's two folders shared one watermark: whichever listed first at start advanced it, so the other
  (usually the user's own Desktop) never caught up. Each watched folder now keeps its own catch-up point; the rules'
  watermark advances once every folder of the source was listed (AM12).
- **I2** — a file renamed in place became a second item (the collect listing saw the new name before the item watcher
  followed the rename): arrivals now wait until the folder has been quiet for 2 s, then only those still there and held by
  no fence are collected (the spec's settle, restored; AM13).
- **I3** — the 200 cap counted per listing, not per burst (a slow extraction re-lists every 250 ms): a burst is one batch
  after the 2 s quiet — one cap, one refresh (AM15).
- **I4** — restoring an older snapshot brought back its old watermarks, so a week of old files came back at the next
  start: a restore starts every rule looking at the restore time (`Snapshots.Restore(..., restoredAt)`, test; AM14).
- **M5 → fixed** (re-graded: Downloads re-listed 4 times a second during every download) — auto-collect listers watch
  names only.
- **M6 → fixed** (re-graded: a rule silently never fired) — the Desktop chosen through "Choose folder…" is the desktop
  source.
- Also: a folder that goes away and comes back (a pendrive) catches up from its last batch, not from the start.

Deferred minors:
- "Add these N too?" is skipped when OK is clicked before the dialog's listing finished (a slow network folder).
- Watermarks of unchanged rules are written back from before the dialog (they can go back by the dialog's open time).
- A power cut between the config and items writes can lose that batch's arrivals.
- A lister started during a game lists (and catches up) once before it pauses.

## Live check (2026-10-05, branch build on a copy of the user's data; the PC unattended, standing go)

Scripted on the script's own folder `%USERPROFILE%\NeoFences-m27-test` (removed afterwards), with Games collecting
installers and Apps apps + installers + `*.burst`: AM2 (`new.url` → Apps, `notes.txt` not), AM5 (`ToolSetup.exe` →
Games, the first fence), AM4 (`Big.exe.crdownload` renamed to `BigSetup.exe`: one item, the final name), AM13 (a
collected file renamed in place: still one item, the new name), AM3 (removed stays removed), AM7 (Pause holds; collected
on resume), AM9 (300 files: 200 items in one batch), AM10 (the dialog: the rule, "305 items here match now"), AM6
(`while-off.url` caught up at start; the removed item not back), AM11 (0.003 % CPU). By hand later: AM1 and AM2 on the
real desktop, AM8 (pendrive), AM12 (the user's Desktop catch-up), AM14 (restore + restart), AM7 in a game.
Script lesson: an `--exit` sent right after a start is ignored; write config only while the app is stopped.
