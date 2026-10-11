# NeoFences theme CPU check (M41a, spec 2026-10-11-m41a-feel-design, plan Task 4): the user's fences plus test fences up to
# ten, each with a moving theme (Aurora, Game HUD, Neon in turn) and Motion Lively, on a backed-up copy of the data folder;
# NeoFences' CPU over 60 s idle. Everything is put back afterwards (the data folder, the startup entry) and the installed
# NeoFences is started again. PowerShell 7.5+. Moves the mouse once (to a corner). ASCII only.
param(
  [Parameter(Mandatory)][string]$Exe, # a build to measure, e.g. a publish of the branch
  [int]$Fences = 10,
  [int]$Seconds = 60,
  [string]$Out = (Join-Path ([IO.Path]::GetTempPath()) 'neofences-themes')
)
$ErrorActionPreference = 'Stop'
$data = Join-Path $env:LOCALAPPDATA 'NeoFences'
$installed = Join-Path $env:LOCALAPPDATA 'NeoFences.App\current\NeoFences.exe'
$backup = Join-Path $Out 'data-backup'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-Item -ItemType Directory -Force $Out | Out-Null
if (-not ('NfThemes.W' -as [type])) { Add-Type -Namespace NfThemes -Name W -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);' }

function Running { @(Get-Process NeoFences -ErrorAction SilentlyContinue) }
function StopNeoFences {
  # --exit is asked again every 5 s: a copy that has only just started ignores it (measure-scale.ps1).
  $deadline = (Get-Date).AddSeconds(40)
  while ((Running).Count -gt 0 -and (Get-Date) -lt $deadline) {
    foreach ($path in @(Running | Select-Object -ExpandProperty Path -Unique)) { & $path --exit }
    $wait = (Get-Date).AddSeconds(5); while ((Running).Count -gt 0 -and (Get-Date) -lt $wait) { Start-Sleep -Milliseconds 300 }
  }
  if ((Running).Count -gt 0) { throw 'NeoFences did not exit' }
}
function CpuSeconds { (Running | Measure-Object -Property { $_.TotalProcessorTime.TotalSeconds } -Sum).Sum }

"===== NeoFences theme CPU check: keep hands off the mouse (about 2 min) ====="
if (Test-Path -LiteralPath $backup) { throw "a backup is already at ${backup}: put it back or remove it first" }
StopNeoFences
Copy-Item -LiteralPath $data -Destination $backup -Recurse
$runValue = (Get-ItemProperty $runKey -ErrorAction SilentlyContinue).NeoFences
"data backed up to $backup"
try {
  $config = Get-Content -LiteralPath (Join-Path $data 'config.json') -Raw | ConvertFrom-Json -AsHashtable -DateKind String
  $fenceList = [System.Collections.Generic.List[object]]::new(); foreach ($fence in @($config.fences)) { $fenceList.Add($fence) }
  $number = 1; while ($fenceList.Count -lt $Fences) { $fenceList.Add([ordered]@{ id = [guid]::NewGuid().ToString('N'); title = "Theme test $number" }); $number++ }
  $themes = @('aurora', 'hud', 'neon'); $index = 0
  foreach ($fence in $fenceList) {
    if (-not $fence.look) { $fence.look = [ordered]@{} }
    $fence.look.theme = $themes[$index % 3]; $index++
  }
  $config.fences = $fenceList
  $config.settings.appearance.motion = 'lively'
  $config | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $data 'config.json') -Encoding utf8NoBOM
  Start-Process $Exe | Out-Null
  Start-Sleep -Seconds 15 # started and settled
  [void][NfThemes.W]::SetCursorPos(10, 10); Start-Sleep -Seconds 3
  $before = CpuSeconds; Start-Sleep -Seconds $Seconds; $used = (CpuSeconds) - $before
  $cores = [Environment]::ProcessorCount
  $report = '{0} fences (Aurora / HUD / Neon), Motion Lively, idle {1} s: {2:N2} s CPU = {3:N2} % of one core = {4:N2} % in Task Manager ({5} logical processors)' -f $fenceList.Count, $Seconds, $used, (100 * $used / $Seconds), (100 * $used / $Seconds / $cores), $cores
  $report; Set-Content -LiteralPath (Join-Path $Out 'report.txt') $report
} finally {
  try { StopNeoFences } catch { Running | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force; Start-Sleep -Seconds 2 }
  Copy-Item -LiteralPath (Join-Path $data 'logs') -Destination (Join-Path $Out 'logs') -Recurse -Force -ErrorAction SilentlyContinue
  # As measure-scale.ps1: the test setup moved aside, the backup copied back, the backup removed only once that worked.
  try {
    if ($data -ne (Join-Path $env:LOCALAPPDATA 'NeoFences')) { throw 'unexpected data path' }
    $aside = "$data.themes-test"
    if (Test-Path -LiteralPath $aside) { Remove-Item -LiteralPath $aside -Recurse -Force }
    Move-Item -LiteralPath $data -Destination $aside
    Copy-Item -LiteralPath $backup -Destination $data -Recurse
    Remove-Item -LiteralPath $aside -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $backup -Recurse -Force
    'data put back'
  } catch {
    "RESTORE FAILED: $($_.Exception.Message)"
    "Your real data is safe in $backup. Close NeoFences and anything open in $data, then run:"
    "  Remove-Item -LiteralPath '$data' -Recurse -Force; Copy-Item -LiteralPath '$backup' -Destination '$data' -Recurse"
  }
  if ($runValue) { Set-ItemProperty $runKey -Name NeoFences -Value $runValue } else { Remove-ItemProperty $runKey -Name NeoFences -ErrorAction SilentlyContinue }
  if (Test-Path -LiteralPath $installed) { Start-Process $installed | Out-Null; 'installed NeoFences started again' }
}
