# Releasing NeoFences

Every step that leaves this PC (push, tag, publish, submissions) is asked of the owner first. Versions are `x.y.z`, and
release candidates `x.y.z-rc.N` (ADR-062): those are GitHub pre-releases, which stable copies never update to. Any other
pre-release tag starts nothing (ADR-039).

## A release candidate (1.0.0-rc.N)

For friends to try before a stable release. The same steps as "Every release" below, except:

- **Version** `1.0.0-rc.N` in the csproj; tag `v1.0.0-rc.N`. The Release workflow uploads it as a **pre-release** draft
  (`vpk upload … --pre`). Publish it the same way; it stays marked pre-release.
- **Who gets it:** nobody automatically. Installed stable copies skip pre-releases; an RC install follows the next RC and
  then the stable release (`UpdateChannel.FollowsPrereleases`). Friends install it from the RC's release page link
  (`https://github.com/CipherSnowden/neo-fences/releases/tag/v1.0.0-rc.N`), which the owner sends them. The website's
  Download button keeps fetching the latest stable release.
- **After publishing:** check in `NF-Win11` that a fresh install of the last stable release reports "up to date", not the
  RC (Settings → Updates → Check now, or its log: no "update … downloaded").
- **Feedback:** the GitHub issue forms, or friends tell the owner, who passes it on. Every crash, data loss or "desktop
  icons stuck hidden" report is fixed in the next RC.
- **To 1.0.0:** about a week after the last RC with no such report, 1.0.0 is that RC's code with the version bumped (no
  other change), released through "Every release", then "At 1.0.0" below.

## Every release

1. **Version:** set `<Version>` in `src/NeoFences.App/NeoFences.App.csproj`; commit `chore: bumped the version to x.y.z`.
   Add `- [ ] Release x.y.z (the user approved the release <date>)` under the milestone in `docs/ROADMAP.md`; commit
   `docs: marked the x.y.z release as approved`.
2. **Secret scan** of everything about to be pushed: `git diff origin/main..HEAD` must not contain the owner's e-mail
   address or a claude.ai artifact link (the repo is public).
3. **Push** main and wait for **CI** (Windows build and tests, Core tests on Linux): `gh run watch <id> --exit-status`.
4. **Tag** `vx.y.z` and push the tag. The **Release** workflow builds, tests and packs (Velopack, with a delta from the
   previous release), writes `SHA256SUMS.txt`, records **build attestations**, and uploads a **draft** release. Check the
   draft's assets: Setup.exe, the portable zip, the full and delta packages, `releases.win.json`, `RELEASES`,
   `SHA256SUMS.txt`.
5. **Install check in the VM** (not on the owner's PC): download the draft's assets
   (`gh release download vx.y.z --dir <folder>`), then
   `pwsh -File tools\vm\Invoke-VmChecks.ps1 -Name NF-Win11 -PreviousSetup <last release's Setup.exe> -Feed <folder> -Candidate x.y.z`
   → every check PASS (install the last release, update to this one from the draft's own files, and the checklist).
6. **Check a download:** `gh attestation verify <folder>\NeoFences.App-win-Setup.exe --repo CipherSnowden/neo-fences`
   passes, and `Get-FileHash` matches its line in `SHA256SUMS.txt`.
7. **Publish:** write the notes (what changed, for users), then
   `gh release edit vx.y.z --notes-file <notes.md> --draft=false`. Installed copies update themselves.
8. **Record:** tick the ROADMAP line (`Released x.y.z on <date> …`), add a **Released:** paragraph to the session's
   `docs/SESSION-LOG.md` entry, refresh the project hub, push.

## At 1.0.0 (and later major releases) — getting past the warnings

NeoFences is unsigned (ADR-039, ADR-061): Windows' SmartScreen warns until a download has a reputation, and antivirus
tools sometimes flag new unsigned installers. After publishing 1.0.0:

1. **VirusTotal:** look up Setup.exe and the portable zip by their SHA-256 on https://www.virustotal.com (upload them if
   they are unknown). Note any detections in the session log.
2. **Microsoft false-positive review** (the owner signs in and submits): https://www.microsoft.com/wdsi/filesubmission →
   "Software developer" → upload `NeoFences.App-win-Setup.exe`, detection "Incorrectly detected as malware/malicious"
   (or SmartScreen), and this text:
   > NeoFences is a free desktop organizer for Windows 11 by CipherSnowden
   > (https://github.com/CipherSnowden/neo-fences). The installer is built by GitHub Actions from the public source;
   > its SHA-256 is listed in the release's SHA256SUMS.txt and it carries a GitHub build attestation. It is unsigned
   > because a code-signing certificate is not affordable for this free project. Please review it for SmartScreen
   > reputation.
3. **winget** (after the owner's OK; the pull request comes from the owner's GitHub account):
   - `winget install Microsoft.WingetCreate` once;
   - `wingetcreate new https://github.com/CipherSnowden/neo-fences/releases/download/v1.0.0/NeoFences.App-win-Setup.exe`
     — identifier `CipherSnowden.NeoFences`, installer type `exe`, silent switch `--silent`, scope `user`, licence
     "Freeware", licence URL `https://github.com/CipherSnowden/neo-fences/blob/main/LICENSE.txt`, privacy URL
     `.../blob/main/PRIVACY.md`, short description "Translucent fences on your desktop that hold links to your apps,
     games, files and websites";
   - check it in Windows Sandbox or the VM (`winget validate`, `winget install --manifest <folder>`), then let
     wingetcreate submit (`--submit`), or open the pull request to `microsoft/winget-pkgs` with `gh`;
   - later versions: `wingetcreate update CipherSnowden.NeoFences --version x.y.z --urls <Setup.exe url> --submit`.
