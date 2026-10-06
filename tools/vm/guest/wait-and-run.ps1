# Inside a NeoFences test VM (M38): runs at the tester's sign-in and waits for the host to drop a run in C:\NeoFencesTest
# (go.txt, written last by Invoke-VmChecks.ps1); then runs the checks on this desktop. Windows PowerShell 5.1. ASCII only.
$folder = 'C:\NeoFencesTest'
$go = Join-Path $folder 'go.txt'
$log = Join-Path $folder 'waiter.log'
# One waiter at a time (the first sign-in starts one, and the Run key another).
$created = $false
$mutex = New-Object System.Threading.Mutex($true, 'Local\NeoFencesTestWaiter', [ref]$created)
if (-not $created) { exit }
Add-Content -LiteralPath $log -Value "$(Get-Date -Format s) waiting"
while ($true) {
  if (Test-Path -LiteralPath $go) {
    Remove-Item -LiteralPath $go -Force
    Add-Content -LiteralPath $log -Value "$(Get-Date -Format s) run"
    try {
      & (Join-Path $folder 'guest-checks.ps1') *>> (Join-Path $folder 'checks.log')
    } catch {
      Add-Content -LiteralPath $log -Value "$(Get-Date -Format s) checks failed: $($_.Exception.Message)"
    }
    Add-Content -LiteralPath $log -Value "$(Get-Date -Format s) done"
  }
  Start-Sleep -Seconds 2
}
