# Runs the NeoFences checklist in a test VM (M38, ADR-060): back to the 'clean' checkpoint (the tester's desktop), the
# previous release's Setup.exe and the release candidate's update files in, then the guest script runs on the desktop and
# its results and screenshots come back out. No admin needed (a member of "Hyper-V Administrators"). ASCII only.
#   tools\vm\Invoke-VmChecks.ps1 -Name NF-Win11 -PreviousSetup <Setup.exe of the last release> -Feed <folder with the candidate's releases.win.json and .nupkg> -Candidate 0.25.0
param(
  [Parameter(Mandatory)][string]$Name,
  [Parameter(Mandatory)][string]$PreviousSetup,
  [Parameter(Mandatory)][string]$Feed,
  [Parameter(Mandatory)][string]$Candidate,
  [string]$Out = (Join-Path ([IO.Path]::GetTempPath()) "neofences-vm-$Name"),
  [string]$CoreDll = (Join-Path $PSScriptRoot '..\..\src\NeoFences.Core\bin\Debug\net10.0\NeoFences.Core.dll')
)
$ErrorActionPreference = 'Stop'
$credential = New-Object PSCredential('tester', (ConvertTo-SecureString 'NeoFences-Test-1' -AsPlainText -Force))
if (Test-Path -LiteralPath $Out) { Remove-Item -LiteralPath $Out -Recurse -Force }
New-Item -ItemType Directory -Force $Out | Out-Null

"${Name}: back to the clean checkpoint"
Stop-VM -Name $Name -TurnOff -Force -ErrorAction SilentlyContinue
Restore-VMSnapshot -VMName $Name -Name 'clean' -Confirm:$false
# The checkpoint holds the running desktop; resumed, Windows would ask for a sign-in (a locked desktop takes no keys or
# screenshots). Without its saved memory the VM boots fresh and the tester signs in by itself.
Remove-VMSavedState -VMName $Name -ErrorAction SilentlyContinue
Start-VM -Name $Name
$session = $null
# Whatever goes wrong below, the VM is turned off and the report still printed ("no results" when nothing came out).
try {
  $deadline = (Get-Date).AddMinutes(10)
  while (-not $session -and (Get-Date) -lt $deadline) {
    try { $session = New-PSSession -VMName $Name -Credential $credential -ErrorAction Stop } catch { Start-Sleep -Seconds 5 }
  }
  if (-not $session) { throw "$Name does not answer PowerShell Direct" }
  $machine = Invoke-Command -Session $session -ScriptBlock { $os = Get-CimInstance Win32_OperatingSystem; "$($os.Caption) ($($os.BuildNumber))" }
  "${Name}: $machine; copying the run in"
  Invoke-Command -Session $session -ScriptBlock { Remove-Item 'C:\NeoFencesTest\run' -Recurse -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory 'C:\NeoFencesTest\run\feed' -Force | Out-Null }
  Copy-Item -ToSession $session -LiteralPath $PreviousSetup -Destination 'C:\NeoFencesTest\run\previous-Setup.exe'
  Copy-Item -ToSession $session -Path (Join-Path $Feed '*') -Destination 'C:\NeoFencesTest\run\feed' -Recurse
  Copy-Item -ToSession $session -Path (Join-Path $PSScriptRoot 'guest\*.ps1') -Destination 'C:\NeoFencesTest' # the newest checks
  $runJson = [ordered]@{ machine = $machine; candidate = $Candidate } | ConvertTo-Json
  Invoke-Command -Session $session -ScriptBlock { param($json) Set-Content 'C:\NeoFencesTest\run\run.json' $json; Set-Content 'C:\NeoFencesTest\go.txt' 'go' } -ArgumentList $runJson

  "${Name}: the checks run on the VM's desktop (up to 25 minutes)"
  $deadline = (Get-Date).AddMinutes(25)
  try {
    while ((Get-Date) -lt $deadline -and -not (Invoke-Command -Session $session -ScriptBlock { Test-Path 'C:\NeoFencesTest\out\finished.txt' })) { Start-Sleep -Seconds 15 }
  } catch { "${Name}: lost the VM while waiting: $($_.Exception.Message)" }
  Copy-Item -FromSession $session -Path 'C:\NeoFencesTest\out\*' -Destination $Out -Recurse -ErrorAction SilentlyContinue
  Copy-Item -FromSession $session -Path 'C:\NeoFencesTest\*.log' -Destination $Out -ErrorAction SilentlyContinue
} catch {
  "${Name}: $($_.Exception.Message)"
} finally {
  if ($session) { Remove-PSSession $session }
  Stop-VM -Name $Name -TurnOff -Force -ErrorAction SilentlyContinue
}
if (-not ('NeoFences.Core.Lifecycle.CheckReport' -as [type])) { Add-Type -Path $CoreDll }
$results = Join-Path $Out 'results.json'
$report = [NeoFences.Core.Lifecycle.CheckReport]::Read($(if (Test-Path -LiteralPath $results) { Get-Content -LiteralPath $results -Raw } else { '' }))
foreach ($check in $report.Checks) { '  {0} {1} {2}' -f $(if ($check.Ok) { 'PASS' } else { 'FAIL' }), $check.Id, $check.Note }
$report.Summary
"screenshots and logs: $Out"
exit $(if ($report.Failed -gt 0) { 1 } else { 0 })
