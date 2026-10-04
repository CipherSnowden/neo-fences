# Builds the NeoFences installer (M7, ADR-023): a self-contained win-x64 publish packed by Velopack into
# artifacts\releases (NeoFences.App-win-Setup.exe, the full package, and RELEASES). Run from the repo root:
#   powershell -NoProfile -ExecutionPolicy Bypass -File build\pack.ps1 -Version 1.0.0
# -OutputDir (M17): another releases folder (a local update rehearsal, or CI); default artifacts\releases.
# -ReleaseNotes (M17): a markdown file shown with the release (CI writes it from the commits).
param([Parameter(Mandatory)][string]$Version, [string]$OutputDir, [string]$ReleaseNotes)
$ErrorActionPreference = 'Stop'
# Checked first (M8a): vpk wants SemVer2, and would otherwise fail only after the tests and the publish.
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z.-]+)?$') { throw "Version '$Version' is not SemVer (like 1.1.0 or 1.1.0-beta.1)." }
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root 'artifacts\publish'
$releases = if ($OutputDir) { $OutputDir } else { Join-Path $root 'artifacts\releases' }
# vpk stops and asks when this version is already packed: refuse up front instead of hanging (M8a).
if (Test-Path (Join-Path $releases "NeoFences.App-$Version-full.nupkg")) {
  throw "Version $Version is already in $releases. Use a newer version, or delete that folder to repack it."
}

Push-Location $root # dotnet finds the vpk tool through the repo's dotnet-tools.json
try {
  if (Test-Path $publish) { [System.IO.Directory]::Delete($publish, $true) }
  dotnet tool restore
  if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed' }
  dotnet test (Join-Path $root 'NeoFences.slnx') -v q
  if ($LASTEXITCODE -ne 0) { throw 'tests failed: not packing' }
  # Self-contained: no separate .NET runtime install, nothing a runtime update can break (robust first, ADR-023).
  dotnet publish (Join-Path $root 'src\NeoFences.App') -c Release -r win-x64 --self-contained -p:Version=$Version -o $publish
  if ($LASTEXITCODE -ne 0) { throw 'publish failed' }
  # packId NeoFences.App: Velopack installs into %LOCALAPPDATA%\<packId> and deletes that folder on uninstall, so it must not
  # be NeoFences (the data folder: config, backups, logs).
  $notes = if ($ReleaseNotes) { @('--releaseNotes', (Resolve-Path $ReleaseNotes).Path) } else { @() }
  dotnet vpk pack --packId NeoFences.App --packVersion $Version --packTitle NeoFences --packAuthors NeoFences `
    --packDir $publish --mainExe NeoFences.exe --icon (Join-Path $root 'src\NeoFences.App\NeoFences.ico') --outputDir $releases @notes
  if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed' }
  Get-ChildItem $releases | Sort-Object LastWriteTime | Select-Object -Last 4 Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table | Out-String
}
finally {
  Pop-Location # the caller's folder is unchanged, even when a step fails
}
