# Setup

Target machine: Windows 11 x64 (the only target, ADR-059). Development happens on Windows 11 (24H2+).

## Required

| Tool | Install | Check |
|---|---|---|
| .NET 10 SDK | `winget install Microsoft.DotNet.SDK.10` | `dotnet --list-sdks` shows `10.x` |
| Git | `winget install Git.Git` | `git --version` |

## Optional

- Visual Studio 2026 with the **.NET desktop development** workload (WPF designer, debugger),
  or VS Code + C# Dev Kit, or Rider.
- Wallpaper Engine (to test acrylic over a live wallpaper).

Not needed: Rust, CMake, vcpkg, JDK (present on the dev PC for other projects). Node is used only to syntax-check the project hub page (`node --check`).

## Build / run / test

```
dotnet build NeoFences.slnx
dotnet test NeoFences.slnx
dotnet test spikes/M0.slnx          # throwaway M0 spike tests
```
```
dotnet run --project src/NeoFences.App      # starts NeoFences (fences, no icons yet)
src\NeoFences.App\bin\Debug\net10.0-windows\NeoFences.exe --exit   # quit it cleanly
```

## Release / install (M7, ADR-023)

```
powershell -NoProfile -ExecutionPolicy Bypass -File build\pack.ps1 -Version 1.0.0
```
Runs the tests, publishes self-contained win-x64, and packs with Velopack (`vpk` from `dotnet-tools.json`; `dotnet tool
restore` fetches it) into `artifacts\releases\` (git-ignored). Install:
- **Install / upgrade:** run `NeoFences.App-win-Setup.exe` (per user, no admin; SmartScreen may warn on the unsigned
  file: More info → Run anyway). The program goes to `%LOCALAPPDATA%\NeoFences.App`; the data stays in
  `%LOCALAPPDATA%\NeoFences`.
- **Uninstall:** Settings → Apps → NeoFences. The icons come back, and the data (layout, backups, logs) is kept, so a
  reinstall restores the fences.
- **Start with Windows** follows the installed copy. A development build run from the repo never takes it over (it does
  only when nothing is installed).

## Recovery

- Native desktop icons stuck hidden (e.g. the install folder was deleted by hand, so no uninstall hook ran):
  right-click desktop → View → Show desktop icons.
- Reset NeoFences: exit it, delete or rename `%LOCALAPPDATA%\NeoFences\config.json`
  (backups in `backups\`).
- Logs: `%LOCALAPPDATA%\NeoFences\logs\`.

## Releasing (M17, ADR-039)

1. Bump `<Version>` in `src/NeoFences.App/NeoFences.App.csproj`, commit, push `main`; wait for CI to pass.
2. `git tag vX.Y.Z` and `git push origin vX.Y.Z`: GitHub Actions builds, tests and uploads a **draft** release.
3. Install the draft's `NeoFences.App-win-Setup.exe` here with `--silent`; check it restarts and the config is intact.
4. Publish: `gh release edit vX.Y.Z --draft=false`. Installed copies then update by themselves.

**Update rehearsal without GitHub:** `build\pack.ps1 -Version A -OutputDir <folder>`, then `-Version B` into the same
folder; install A's Setup, start NeoFences with `NEOFENCES_UPDATE_SOURCE=<folder>`, Settings → Updates → Check now.

Never commit the personal email address or the private hub link: the repository is public.
