# CLAUDE.md — NeoFences

Loaded automatically by every Claude Code session in this repo. Read it fully before acting. **Then read `docs/PIVOT-2026-10-04.md`** (the current direction).
When a decision here changes, update this file **in the same commit** as the code.

## What NeoFences is

A desktop organizer for Windows 11 x64 (the only target, ADR-059) in the spirit of Stardock Fences and Rainmeter: translucent
"fences" on the desktop that hold **virtual items** (links to files, folders, apps, websites — never the files
themselves; ADR-040). Native desktop icons stay visible unless the user hides them. Built for one power
user first (gamer, Wallpaper Engine). Priorities: **robust and reliable** > modern UI/UX >
decent performance > feature count.

## Start-of-session protocol (multi-session sync)

Several Claude sessions work on this repo, possibly in parallel. Git is the sync mechanism.

1. `git pull` (if a remote exists) and `git log --oneline -15`.
2. Read `docs/ROADMAP.md` (what's in progress / claimed) and the last 3 entries of
   `docs/SESSION-LOG.md`.
3. Before starting a task, **claim it** in `docs/ROADMAP.md` (`[~] task — claimed by session
   <date> <short-topic>`) and commit that alone (`docs: claimed M2 icon loading`).
4. Parallel sessions: each works in its own git worktree/branch (`m2-icons`, …). Never two
   sessions on the same branch.
5. Before ending: append a `docs/SESSION-LOG.md` entry (done / decisions / next / open
   questions), update `docs/ROADMAP.md` checkboxes, then **refresh the project hub** (below),
   commit.
6. A new or changed decision → add/supersede an ADR in `docs/DECISIONS.md`. Never silently
   contradict an accepted ADR — supersede it explicitly.

## Project hub (artifact) — the user's main view of the project

- URL: in `CLAUDE.local.md` (private to the user; that file is git-ignored — the repo is public, the hub link is not)
- Source: `docs/hub/neofences-hq.html`. Content lives in the `HUB` data object at the bottom
  (tasks, milestones, features, ADRs, research, risks, sessions, commits); diagrams and
  sections above it change only when architecture/flows change.
- Refresh whenever docs change meaningfully, and at every session end: edit the `HUB` object
  (and any affected diagram) to mirror `docs/`, bump `updated` + `commit`, check the script
  (`node --check` on the extracted `<script>`), then republish with the Artifact tool passing
  `url` = the URL in `CLAUDE.local.md` (a publish without `url` from a new session creates a duplicate).
- `docs/` stays canonical. If hub and docs disagree, fix the hub.

## Hard rules — do not violate without asking the user

1. **NeoFences never modifies, moves, renames or deletes any user file** (ADR-040). Organizing is
   item-only: removing an item or a fence never touches files.
2. **Native desktop icons always come back** when the user chose to hide them — clean exit, crash,
   Task Manager kill (watchdog), Explorer restart.
3. **No DLL injection, no in-process global hooks** (`SetWindowsHookEx` other than
   `WH_MOUSE_LL`). Only out-of-context WinEvent hooks. `WH_MOUSE_LL` is removed in game mode.
4. **All Win32/COM goes through `NeoFences.Shell`.** `NeoFences.Core` references nothing
   Windows-specific. `NeoFences.App` is UI only.
5. **Bindings via CsWin32** (`NativeMethods.txt`), not hand-written `DllImport`, unless CsWin32
   can't express it (comment why).
6. **No new NuGet dependency** without an ADR in `docs/DECISIONS.md`.
7. Failures in shell calls are logged and degrade the single feature; they never crash the app.

## Conventions

- **Commits:** single line, Conventional Commits, past-tense description, e.g.
  `feat: added fence rollup on title double-click`, `fix: fixed icons staying hidden after explorer restart`,
  `docs: …`, `test: …`, `chore: …`, `refactor: …`. No `Co-Authored-By` trailer. Modular: one
  logical change per commit; docs updates ride with the code they describe.
- Named/options parameters over long positional lists; descriptive identifiers (no `e`,
  `item`, `state` when a precise name exists).
- Deliberate shortcuts get a `// ponytail: <ceiling>, <upgrade path>` comment.
- `Core` logic is test-first (xUnit). Shell/UI behaviour is verified with
  `docs/TEST-CHECKLIST.md`.
- A change to a menu entry, setting, gesture or key updates `docs/GUIDE.md` (and the README quick start if it
  names it) in the same commit (ADR-050) — the app's Help opens that guide.

## Repository map

```
neo_fences/
├── CLAUDE.md                 ← you are here
├── docs/
│   ├── ARCHITECTURE.md       living technical design (source of truth for current state)
│   ├── DECISIONS.md          ADR log — why things are the way they are
│   ├── FEATURES.md           Fences 5/6 parity matrix + NeoFences extras, status per feature
│   ├── ROADMAP.md            milestones, task checkboxes, claims
│   ├── SESSION-LOG.md        append-only session history
│   ├── SETUP.md              toolchain install + build/run commands
│   ├── TEST-CHECKLIST.md     manual shell/UI test script (created in M0)
│   ├── research/             findings from spikes (Win32 behaviour, 24H2, Wallpaper Engine)
│   ├── hub/neofences-hq.html project hub artifact source (see "Project hub")
│   └── superpowers/
│       ├── specs/            dated design specs (snapshots, don't rewrite after approval)
│       └── plans/            implementation plans per milestone
├── spikes/                   throwaway feasibility code (M0) — never referenced by src/
├── src/
│   ├── NeoFences.Core/       pure C# model/layout/config        (planned, M1)
│   ├── NeoFences.Shell/      Win32/COM via CsWin32               (planned, M2)
│   └── NeoFences.App/        WPF app, entry point                (planned, M2)
└── tests/
    └── NeoFences.Core.Tests/ xUnit                               (planned, M1)
```

## Commands

See `docs/SETUP.md`. Short version (once `src/` exists):

```
dotnet build
dotnet test
dotnet run --project src/NeoFences.App
```

Runtime data: `%LOCALAPPDATA%\NeoFences\` (`config.json`, `backups\`, `logs\`).
If native icons are ever stuck hidden: right-click desktop → View → Show desktop icons.
