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

- No separate 1 s settle: the folder lister's 250 ms coalescing, and arrivals found by comparing listings — files
  renamed or moved in while NeoFences runs count too (the spec named created and renamed).
- A rule's `Watermark` is updated when a listing had arrivals or was the first one (not on every change), so config.json
  is not rewritten while a file keeps being written.
- At start, the source's earliest watermark is used for all its rules.
- A rule whose settings change starts looking again from then (its watermark resets).
- "Add these N too?" is asked after OK for each new rule, from the listings the dialog made; at most the first 200.
- The fence menu shows the count ("Auto-collect… (2 rules)") instead of a separate mark.