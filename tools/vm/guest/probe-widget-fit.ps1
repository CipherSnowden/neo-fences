# Inside a NeoFences test VM (rc.5, the owner's desktop): widgets from before rc.3 (clock 3x1, calendar 3x1, stats 3x3) in a
# 275-px fence - the owner's "Desk" - showed their Large design cut off on the right. Each now shows the biggest design that
# fits; a wide fence still shows Large. Screenshots for a look. Dot-sourced by guest-checks.ps1
# (Invoke-VmChecks.ps1 -Live -Script probe-widget-fit.ps1). Windows PowerShell 5.1. ASCII only.
. (Join-Path $root 'live-helpers.ps1')
$exe = Join-Path $run 'build\NeoFences.exe' # this checkout's build, not an installed one

function Widget([string]$id, [string]$kind, [int]$columns, [int]$rows) {
  [ordered]@{ id = $id; target = "neofences:widget/$kind"; size = @{ columns = $columns; rows = $rows } }
}

Check 'start' { StartNeoFences; StopNeoFences; Test-Path -LiteralPath (Join-Path $data 'config.json') }
Check 'desk-like-the-owners' {
  $config = Get-Content -LiteralPath (Join-Path $data 'config.json') -Raw | ConvertFrom-Json
  $fingerprint = $config.lastLayoutFingerprint
  $monitor = @($config.layouts.$fingerprint.fences.PSObject.Properties)[0].Value.monitor
  $look = @{ colourStyle = 'accentEdge'; strength = 4; titleOnHover = $true }
  $config.fences = @(
    @{ id = 'desk'; title = 'Desk'; layout = 'flow'; labels = 'onHover'; look = $look },
    @{ id = 'wide'; title = 'Wide'; layout = 'flow' })
  $config.layouts.$fingerprint.fences = @{
    desk = @{ monitor = $monitor; x = 12; y = 12; w = 275; h = 560 }; wide = @{ monitor = $monitor; x = 320; y = 12; w = 470; h = 420 } }
  $config | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath (Join-Path $data 'config.json') -Encoding UTF8
  $items = @{
    desk = @((Widget 'clock' 'clock' 3 1), (Widget 'date' 'date' 3 1), (Widget 'stats' 'stats' 3 3))
    wide = @((Widget 'wclock' 'clock' 4 2), (Widget 'wdate' 'date' 4 2)) }
  [ordered]@{ schema = 1; fences = $items } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $data 'items.json') -Encoding UTF8
  StartNeoFences; Start-Sleep -Seconds 5; Shot 'widget-fit'
  "fences: $(@(Fences).Count)"; @(Fences).Count -eq 2
}
