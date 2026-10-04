# NeoFences — Public releases and auto-update (M17) design

**Date:** 2026-10-04 · **Status:** draft for review · **Target:** v1.8.0 (the first public release)

## 1. Goal and user choices

Friends download NeoFences from GitHub once; after that it updates itself. User choices (2026-10-04):

| Question | User choice |
|---|---|
| Audience | Friends / public, downloading from GitHub. |
| Repository | One public repo with code, docs, history and releases. Not open source yet: no license file (all rights reserved). |
| Secrets | Stay secret, also in the history: the personal email address and the private project-hub link. |
| Updates | Download quietly, ask to restart; install on the next normal exit if ignored; a switch and "Check now" in Settings. |
| Build | GitHub Actions (approach 1, two stages): stage 1 unsigned releases now; stage 2 signing later. |
| Signing | Deferred: not required. Friends see SmartScreen once at first install; updates never do. Revisit if Smart App Control or an antivirus blocks a friend. |

Success: a friend opens the repo's Releases page, runs `NeoFences.App-win-Setup.exe` (one SmartScreen click), and from
then on receives each new version by itself — a tray notice, "Restart to update", the new version running with their
fences and config intact. No secret of the developer is visible anywhere in the repository or its history.

Non-goals: code signing (stage 2); an open-source license; a website; telemetry; update channels (beta); publishing the
old local releases v1.0.0–v1.7.1 as GitHub Releases.

## 2. Making the repository safe to publish (local, before the first push)

1. **Backup:** a mirror clone of every branch and tag to `F:\projects\neo_fences-backup.git`.
2. **Email:** every commit's author and committer email is rewritten to the account's GitHub noreply address
   (`<id>+CipherSnowden@users.noreply.github.com`); the repo's `user.email` is set to it for future commits.
3. **Hub link:** removed from every file in every commit and replaced by "private hub link: see CLAUDE.local.md". The real
   link moves to `CLAUDE.local.md` (loaded by Claude Code, ignored by git). `CLAUDE.md`'s hub section points there.
4. **Final scan of every commit and file:** zero hits for the old email, the hub link, and token or key patterns. A single
   hit stops the push.
5. **README.md:** what NeoFences is, install (Releases page, the one-time SmartScreen click, Smart App Control note),
   "free to use, all rights reserved", how to build, where the docs are.
6. **Push** (only after the user says "push"): `main`, all tags, and the parked `m15-search-palette` branch to
   `github.com/CipherSnowden/neo-fences` (public), created with `gh`.

## 3. Releases (GitHub Actions)

- **`ci.yml`** — on every push to `main` and on pull requests: Windows runner, .NET 10, `dotnet build`, `dotnet test`.
- **`release.yml`** — on a pushed tag `v<major>.<minor>.<patch>`:
  1. build and test (a failure stops everything; nothing is published);
  2. `vpk download github` the latest published release into `artifacts\releases` (for a delta package; none the first time);
  3. `build\pack.ps1 -Version <tag version>` (as today: self-contained publish + `vpk pack`);
  4. a marked, disabled signing step (stage 2);
  5. `vpk upload github` as a **draft** release named "NeoFences v<version>", with notes generated from the `feat:` and
     `fix:` commit subjects since the previous tag; the workflow's own `GITHUB_TOKEN` (permission `contents: write`).
- **Draft → check → publish:** drafts are invisible to friends and to the updater. The draft's Setup is installed on the
  developer PC (restart after Setup, config intact), then published with `gh release edit --draft=false`. Pushing a tag
  and publishing are each asked for, every release.
- `build\pack.ps1` keeps working for local test builds.

## 4. The in-app updater

- **Source:** Velopack's `UpdateManager` with a GitHub source for the public repo (no token: public releases). Velopack is
  already a dependency (ADR-023). A copy not installed by Setup (a developer build) never updates.
- **When to check** (`UpdatePolicy`, Core, pure): at start, then every 24 h; after a failure, again after 6 h; never while
  game mode is active; never when the switch is off — then NeoFences makes no network calls at all.
- **Flow:** a newer version is found → its (delta) package downloads in the background → a notice "NeoFences <v> is ready —
  restart to update" and a tray item "Restart to update to v<v>" at the top of the menu.
  - **Restart to update:** NeoFences' normal clean exit first (icons back, the watchdog told all is well — hard rule 2),
    then Velopack applies the update and starts the new version.
  - **Ignored:** the update is applied after the next normal Exit (not at sign-out or shutdown, which stay as fast and
    safe as today; the next normal exit picks it up).
- **Settings → Updates card:** "Download updates automatically" (on by default), the running version, a status line
  ("Up to date · checked 10:42", "v<v> ready", "Couldn't check: offline"), "Check now", "Restart to update".
- **Config:** `Settings.AutoUpdate` (bool, default true). Schema 4 (ADR-033 rule; the parked search palette would take 5).
- **Failures:** every updater error is caught, logged once per kind, retried on the policy's schedule; never a crash
  (hard rule 7). An update replaces only the app folder; `%LOCALAPPDATA%\NeoFences` (config, backups, logs) is untouched.
- **Test source:** an environment variable (`NEOFENCES_UPDATE_SOURCE`) points the updater at a local folder of releases,
  for a full update rehearsal without GitHub. It is read only by installed copies and only for that purpose.

## 5. Reliability and hard rules

- Hard rule 1: updates never touch user data or desktop files.
- Hard rule 2: an update always goes through the clean exit (icons restored, watchdog informed) before Velopack applies it.
- Hard rule 6: no new NuGet (Velopack is in place). GitHub Actions uses the official `actions/checkout` and
  `actions/setup-dotnet` and the repo-local `vpk` tool.
- Hard rule 7: no network or update failure stops NeoFences.
- Secrets: nothing secret in the repo, its history, the workflows or the app; the release token is the workflow's own.

## 6. Testing

- **Core (xUnit, test-first):** `UpdatePolicy` (start check, 24 h, 6 h after failure, game mode, switched off, not
  installed); schema 4 and the `AutoUpdate` default.
- **Local update rehearsal** (with the user's go; installed copy and config restored): install 1.8.0 from a local release
  folder, publish a local 1.8.1, let the updater find, download and apply it via "Restart to update"; icons back, config
  intact, 1.8.1 running.
- **Scrub check:** the history scan of §2.4 is part of the plan, with its output recorded.
- **CI:** both workflows must pass on GitHub before the first draft is published.
- **Checklist:** new section AC (update found, ignored until exit, switched off, offline, during a game, a friend's fresh
  install with SmartScreen).

## 7. Delivery

M17 → v1.8.0, the first public release, in order: repo preparation and push (asked), the updater (prototype, local
rehearsal with the user's go), the workflows, the first tag and draft (asked), the install check, publish (asked).
ADR-039 records the decisions (public repo without a license, secrets scrubbed from history, CI releases as drafts,
Velopack GitHub updates, signing deferred).
