# M39 — Release readiness (0.26.0) — design

**Date:** 2026-10-10 · **Status:** approved in brainstorm, awaiting written-spec review
**Decision:** ADR-061 (added by the plan).
**Source:** `docs/research/v1-readiness.md` ("Release and trust", "Linux and macOS"), `docs/ROADMAP.md` → M39.
**Owner's picks:** all four groups in M39 — trust files, verified downloads, install help and a landing page, Linux CI
and outreach; a short custom freeware licence (work use allowed); a one-page landing site; outreach prepared now and
submitted at 1.0; .NET 10 installed in the owner's WSL Ubuntu for the Linux test runs.

## Goal

A friend or a stranger can find NeoFences, see what it is, download it, get past SmartScreen with clear steps, know what
it sends over the network and what they may do with it, check that the download is genuine, and report a bug — before
1.0.0-rc goes out. Core runs its tests on Linux too, so a later port starts clean. Hard rules stand: no new NuGet
dependency, no personal data in the public repo (never the owner's e-mail or the hub link), NeoFences never touches user
files.

## 1. Trust files

In the repo root, and the first three also next to `NeoFences.exe` in every install (packed with the app):

- **`LICENSE.txt`** — a short custom freeware licence (not lawyer-reviewed; the owner approved this wording):
  > **NeoFences Freeware Licence** · Copyright © 2026 CipherSnowden. All rights reserved.
  > 1. You may download, install and use NeoFences free of charge, at home or at work, on any number of computers.
  > 2. You may share the unmodified installer or portable zip from the official release page, free of charge, together
  >    with this licence.
  > 3. You may not sell NeoFences, charge for it, bundle it with paid products, or distribute modified versions.
  > 4. The source code is published so anyone can see what NeoFences does. You may read it and build it for your own use;
  >    you may not distribute it, or work based on it, without written permission.
  > 5. NeoFences is provided "as is", without warranty of any kind. The author is not liable for any damage arising from
  >    its use.
  > 6. Third-party components keep their own licences (see THIRD-PARTY-NOTICES.txt).
  > 7. All rights not expressly granted stay with the author.
- **`PRIVACY.md`** — no telemetry, no accounts, no ads. Network traffic: the update check and update downloads from
  GitHub (`github.com/CipherSnowden/neo-fences` releases); only when "Find covers and website icons online" is on
  (ADR-055, off by default): game names sent to Steam's store search and cover images from Steam's CDN, and a website
  item's icon from that site's root. Nothing else. Settings, items, logs, snapshots and the icon cache stay in
  `%LOCALAPPDATA%\NeoFences`; NeoFences never moves, renames or deletes your files (ADR-040). Logs contain folder and file
  names — share them only if you want to.
- **`THIRD-PARTY-NOTICES.txt`** — Serilog and Serilog.Sinks.File (Apache-2.0), Velopack (MIT), the .NET runtime (MIT, and
  its own third-party notices, included with the self-contained publish), CsWin32's generated code (MIT). Test-only
  packages are not shipped and not listed.
- **`SECURITY.md`** — report a vulnerability privately with GitHub's "Report a vulnerability" (private vulnerability
  reporting turned on in the repo settings — the owner's go is asked at that step); only the latest version gets fixes;
  what is in scope (the app, the installer, the update path).
- **In the app:** Settings → About and logs gains **Licence and notices**, which opens the folder with `LICENSE.txt`,
  `THIRD-PARTY-NOTICES.txt` and `PRIVACY.md`. `docs/GUIDE.md` updated in the same commit (ADR-050).
- **Issue templates** (`.github/ISSUE_TEMPLATE/`, GitHub forms): **Bug** (NeoFences version, Windows build, what you did,
  what you expected, what happened, optional logs with the warning that they show folder names); **Idea** (what and
  why); `config.yml` turns off blank issues and links security reports to SECURITY.md.

## 2. Verified downloads and Linux CI

- **Checksums:** the Release workflow writes `SHA256SUMS.txt` (one `hash  filename` line for each asset: Setup.exe, the
  portable zip, the full and delta packages, `releases.win.json`, `RELEASES`) and uploads it with the draft.
- **Build attestations:** `actions/attest-build-provenance` records that each asset was built by this repo's Release
  workflow from the tagged commit (permissions `id-token: write`, `attestations: write`; no stored secret). Anyone checks
  a download with `gh attestation verify NeoFences.App-win-Setup.exe --repo CipherSnowden/neo-fences`, or its hash with
  `Get-FileHash` against `SHA256SUMS.txt`.
- **Core tests on Linux:** CI gains an `ubuntu-latest` job that builds and tests `tests/NeoFences.Core.Tests` (Core only;
  Shell and the WPF App stay Windows-only). The Windows job stays. Core's Windows-path assumptions (about 10 files per the
  readiness review: backslashes, drive letters, `%LOCALAPPDATA%`-style paths, case-insensitive comparisons) are fixed
  test-first so the same tests pass on both; where a test is about Windows paths by nature, it keeps Windows inputs and
  Core treats them as text (no `System.IO.Path` behaviour that differs by OS). Failures are found first in the owner's
  WSL Ubuntu with the .NET 10 SDK installed there (owner's OK given), then confirmed in CI.

## 3. Install help and the landing page

- **README refresh:** a fresh hero screenshot and two lines on what NeoFences is; **Download** (the latest Setup.exe) and
  "Windows 11 x64"; install steps — SmartScreen's "Windows protected your PC" → **More info** → **Run anyway**, explained
  as "unsigned, not unsafe" (signing costs money; checksums and attestations instead); Smart App Control, worded
  honestly: with it on, Windows blocks unsigned apps such as NeoFences; turning it off is the reader's decision, with a
  link to Microsoft's explanation, never a recommendation; how to check a download; licence, privacy, reporting a bug.
- **Guide refresh:** the same install section in `docs/GUIDE.md`; screenshots retaken where the look changed since 0.17.0
  (the fence menu, the item menu and Settings after M35's new menus and dialogs) plus Peek with the keyboard (M38).
- **Landing page:** `site/index.html` with its CSS and images, hand-written, no framework, dark glass look like the
  fences: a hero screenshot, a **Download for Windows 11** button (the latest release's Setup.exe), 5–6 features with
  pictures (fences of links, game tiles, widgets, folder panels, Peek and the keyboard, the safety net), the install
  steps, one privacy line, links to the guide, issues and the licence. Works at phone width. A Pages workflow publishes
  `site/` on every push to main that changes it, at `ciphersnowden.github.io/neo-fences`; turning Pages on (source:
  GitHub Actions) needs the owner's go at that step.
- **Screenshots** need the owner's real setup (in a VM the owner's items have no icons): taken on the owner's PC with a
  short scripted run, asked first, nothing changed (open menus, Peek, screenshot, close), or by the owner from a list.

## 4. Release checklist, versions, testing

- **`docs/RELEASING.md`** — the release flow as it is now: bump, push, CI, tag, draft, install check in `NF-Win11`
  (`Invoke-VmChecks` with the draft's own files), publish, hub. A **1.0 section** for the outreach, done at 1.0.0:
  VirusTotal (check Setup.exe); Microsoft's false-positive submission (the WDSI form; the owner signs in and submits, the
  text prepared); **winget** — a manifest made with `wingetcreate` (Velopack's Setup.exe is silent with `--silent`),
  the pull request to `microsoft/winget-pkgs` opened from the owner's account after the owner's OK.
- **Versions:** M39 ships as **0.26.0** in the usual flow — its release proves the checksums and attestations end to
  end. **1.0.0-rc** for friends is the next step, designed separately: the Release workflow deliberately ignores
  pre-release tags (installed copies must not jump to a test build), so the RC needs its own decision.
- **Testing:** Core tests green on Linux (WSL, then the CI job) and Windows; the 0.26.0 release verified with
  `gh attestation verify` and `Get-FileHash` against `SHA256SUMS.txt`; the landing page looked at in a browser at desktop
  and phone widths; Settings → **Licence and notices** checked in `NF-Win11-Dev` (not on the owner's PC); the issue forms
  checked on GitHub after the push; a secret scan before every commit and push (the public repo).

## Docs

ADR-061; `docs/GUIDE.md` (install, Licence and notices, refreshed screenshots); README; ARCHITECTURE (release and CI
paragraph); FEATURES (an extras row); TEST-CHECKLIST section AY; `docs/RELEASING.md`; a research note
`docs/research/m39-release-readiness.md` (the Linux findings, what was verified).

## Out of scope

Code signing (after 1.0: Certum or a Microsoft Store build); the 1.0.0-rc mechanics; submitting to Microsoft, VirusTotal
and winget before 1.0.0; a website beyond one page; the guide on the site; translations; Linux or macOS builds of the app.
