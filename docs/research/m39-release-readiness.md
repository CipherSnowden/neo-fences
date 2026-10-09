# M39 — Release readiness (research note)

**Date:** 2026-10-10 · **Spec:** `docs/superpowers/specs/2026-10-10-release-readiness-design.md` · **Decision:** ADR-061 ·
**Plan:** `docs/superpowers/plans/2026-10-10-m39-release-readiness.md`

## Core on Linux

Core (`net10.0`, no Windows references) had never run off Windows. In the owner's WSL Ubuntu 26.04 its tests gave
**45 failed, 792 passed** of 837. By cause:

| cause | tests | fix |
|---|---|---|
| `System.IO.Path` follows the OS: on Linux `\` is not a separator, so `GetFileName(@"D:\View\a.jpg")` is the whole string; `IsPathFullyQualified(@"D:\x")` is false; `Combine` puts `/` | folder views, item sorting, folder panels (Up, headers), setup pictures, game art, the icon cache index, game items, the startup entry, collect rules, Wallpaper Engine names | `WindowsPath` (Core): pure text rules for the Windows paths Core stores, used at 26 call sites |
| a name meant to be cut to a plain file name kept its `..\..\` on Linux | setup pictures, game art, the icon cache, cover choices | the same — `WindowsPath.FileName` treats `\` and `/` alike on every OS |
| tests building Windows-only inputs | the `India Standard Time` zone, `data.File("icons\\pic.png")`, `%USERPROFILE%`, helpers calling `Path.GetFileName` | a custom time zone; forward slashes; `WindowsPath.FileName` in helpers; `%USERPROFILE%` test Windows-only |
| tests of Windows-only behaviour | folder ACLs (`WindowsIdentity`), a mandatory file lock | return early off Windows, with a comment (no NuGet package for skippable tests) |
| Linux ICU formats "4:07 PM" with a narrow no-break space | the clock text | the test treats U+202F as a space |

NeoFences' own files (the JSON stores, snapshots, the caches) keep `System.IO.Path`: that is real file I/O on the OS it
runs on. After the fix: **878 of 878 pass on Linux and on Windows** (41 new `WindowsPath` cases). CI runs them on
`ubuntu-latest` (`core-linux`).

WSL setup on the owner's PC (owner's OK): `dotnet-install.sh --channel 10.0 --install-dir ~/.dotnet` (10.0.401); the SDK
needs ICU, so `apt-get install libicu78` as root (`wsl -u root`; `sudo` asked for a password). A worktree's `.git` file
points at a Windows path, so WSL clones the repo from `/mnt/f/projects/neo_fences` and pulls the branch.

## Trust files

- `LICENSE.txt`: the owner's freeware wording (not lawyer-reviewed), plus the release page's address.
- `THIRD-PARTY-NOTICES.txt`: from the package metadata (Serilog and Serilog.Sinks.File: Apache-2.0, © Serilog
  Contributors; Velopack: MIT, © 2021 Caelan Sayler, © 2024 Velopack Ltd.; CsWin32: MIT, © Microsoft), the full Apache
  2.0 text, the .NET runtime's MIT licence and its own third-party notices (dotnet/runtime release/10.0), and WPF's
  (dotnet/wpf release/10.0) — the self-contained app ships both. It legitimately contains other authors' e-mail addresses
  (e.g. Mono.Cecil's), which the repo's secret scan flags as expected.
- `PRIVACY.md` checked against the code: `FenceHost.Updates.cs` (GitHub releases), `OnlineArt.cs` (Steam store search and
  CDN, a website's own icon — both behind "Find covers and website icons online", ADR-055); logs write the profile folder
  as `%USERPROFILE%`.
- The licence, notices and privacy note are copied next to `NeoFences.exe`; Settings → About → Licence and notices opens
  the licence (the install folder holds ~400 files, so not the folder).

## Landing page

`site/` (hand-written HTML and CSS), published by `pages.yml` with the guide's pictures copied into `img/`. Looked at in
headless Edge at 1280 px and inside a 390 px frame (headless Edge enforces a minimum window width, so a narrow
`--window-size` crops instead of reflowing): one column on a phone, no sideways scrolling.

## Calls made while prototyping

- Stored paths by Windows' rules on every OS; NeoFences' own files by the OS's (`WindowsPath` vs `System.IO.Path`).
- `WindowsPath.DirectoryName` is null for a bare name (`System.IO.Path` gives ""); no caller depends on it.
- Windows-only tests return early off Windows instead of a skippable-test package.
- Licence and notices opens `LICENSE.txt`, not the folder.
- Issue forms use GitHub's default labels (`bug`, `enhancement`).
- The site uses the guide's pictures (one place to update) and shows no version number.

## VM runs

The prototype (0.26.0-proto.1): release-candidate pass in `NF-Win11` from 0.25.0 — 13 of 13 (`trust-files` included);
live checks in `NF-Win11-Dev` on a copy of the owner's data — 8 of 8. The branch after the final review's fix pass
(packed as 0.26.0-rc.1): the release-candidate pass from 0.25.0 — **13 of 13**; the live checks — **8 of 8**.

## Screenshots

Taken 2026-10-10 on the owner's PC (owner's OK) with the branch build on a backed-up copy of the owner's data, restored
afterwards: `desktop.jpg`, `panel.png`, `fence-menu.png`, `item-menu.jpg`, `settings.png` (About, with Licence and
notices) and the new `peek.jpg`. No personal data in public pictures: the Downloads panel pointed at a demo folder of
empty files for the shots, and the data folder's path (it holds the Windows user name) was painted over in Settings. A
first run skipped the Peek shot (the script's wait for Notepad returned at once; the key helper then refused to type with
Terminal in front, by design); a Peek-only rerun took it.

## Release 0.26.0

2026-10-10: Pages and private vulnerability reporting turned on before the push; CI green (Windows and `core-linux`);
the landing page live; the draft with `SHA256SUMS.txt` and attestations for Setup.exe, the portable zip and both packages
(verified with `gh attestation verify` and `sha256sum`). The feed index files (`releases.win.json`, `RELEASES`) are
rewritten by `vpk upload` after hashing, so they are no longer listed. Install check in `NF-Win11`: 13 of 13. Published.
