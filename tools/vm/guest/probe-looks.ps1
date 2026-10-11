# Inside a NeoFences test VM (M41a, spec 2026-10-11-m41a-feel-design sections 1-4): screenshots of the looks - six fences
# side by side, one per theme and title style, then the name styles, the hover and the two settings windows - for the owner
# to judge. Dot-sourced by guest-checks.ps1 (run with Invoke-VmChecks.ps1 -Live -Script probe-looks.ps1). Every step is a
# check that passes when it ran; the pictures are the result. Windows PowerShell 5.1. ASCII only.
. (Join-Path $root 'live-helpers.ps1')
$exe = Join-Path $run 'build\NeoFences.exe' # this checkout's build, not an installed one
$withData = Test-Path -LiteralPath (Join-Path $run 'data')
function Emoji([int]$code) { [char]::ConvertFromUtf32($code) }

Check 'start' { StartNeoFences; StopNeoFences; Test-Path -LiteralPath (Join-Path $data 'config.json') }
Check 'six-fences' {
  $config = Get-Content -LiteralPath (Join-Path $data 'config.json') -Raw | ConvertFrom-Json
  $fingerprint = $config.lastLayoutFingerprint
  $monitor = @($config.layouts.$fingerprint.fences.PSObject.Properties)[0].Value.monitor
  $fences = @(
    @{ id = 'aurora'; title = 'Games'; tabColor = 'purple'; emoji = (Emoji 0x1F3AE); look = @{ theme = 'aurora'; titleStyle = 'clean'; strength = 30 } },
    @{ id = 'hud'; title = 'Arsenal'; look = @{ theme = 'hud'; titleStyle = 'caps'; strength = 55 } },
    @{ id = 'neon'; title = 'Music'; tabColor = 'pink'; emoji = (Emoji 0x1F3B5); look = @{ theme = 'neon'; titleStyle = 'pill'; strength = 45 } },
    @{ id = 'mica'; title = 'Work'; tabColor = 'blue'; emoji = (Emoji 0x1F4BC); look = @{ theme = 'lightMica'; titleStyle = 'clean'; strength = 60 } },
    @{ id = 'classic'; title = 'Downloads'; tabColor = 'orange'; look = @{ theme = 'classic'; titleStyle = 'simple'; strength = 25 } },
    @{ id = 'pill'; title = 'Projects'; tabColor = 'teal'; emoji = (Emoji 0x1F6E0); look = @{ theme = 'aurora'; titleStyle = 'caps'; strength = 40 } })
  $rects = @{}; $index = 0
  foreach ($fence in $fences) {
    $rects[$fence.id] = @{ monitor = $monitor; x = 12 + ($index % 3) * 336; y = 12 + [math]::Floor($index / 3) * 300; w = 320; h = 270 }; $index++ }
  $config.fences = $fences
  $config.layouts.$fingerprint.fences = $rects
  $config | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath (Join-Path $data 'config.json') -Encoding UTF8
  $targets = @('C:\Windows', 'C:\Users', 'C:\Program Files', 'C:\Windows\notepad.exe', 'C:\Windows\regedit.exe', 'https://github.com', 'C:\Windows\System32\calc.exe')
  $items = @{}
  foreach ($fence in $fences) { $items[$fence.id] = @($targets | ForEach-Object -Begin { $n = 0 } -Process { @{ id = "$($fence.id)-$n"; target = $_ }; $n++ }) }
  [ordered]@{ schema = 1; fences = $items } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $data 'items.json') -Encoding UTF8
  StartNeoFences; Start-Sleep -Seconds 4; Shot 'looks-lively'
  "fences: $(@(Fences).Count)"; @(Fences).Count -eq 6
}
Check 'hover' {
  $item = Element 'Windows' ([System.Windows.Automation.ControlType]::ListItem)
  if ($item) { $x, $y = Center $item; Pointer $x $y; Start-Sleep -Milliseconds 300; Shot 'looks-hover' }
  [bool]$item
}
Check 'scan-later' { Start-Sleep -Seconds 3; Shot 'looks-later'; $true } # the lights drifted, the scan line moved
foreach ($names in 'chips', 'plain') {
  Check "names-$names" {
    StopNeoFences; $path = Join-Path $data 'config.json'; $config = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    $config.settings.appearance | Add-Member -NotePropertyName nameStyle -NotePropertyValue $names -Force
    $config | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $path -Encoding UTF8
    StartNeoFences; Start-Sleep -Seconds 3; Shot "looks-names-$names"; $true
  }
}
Check 'fence-settings' {
  $fence = @(Fences | Where-Object { (Rect $_).Left -lt 300 -and (Rect $_).Top -lt 200 })[0]; $rect = Rect $fence
  RightClick ([int](($rect.Left + $rect.Right) / 2)) ($rect.Bottom - 12)
  $entry = Element "Fence settings$([char]0x2026)" ([System.Windows.Automation.ControlType]::MenuItem)
  if (-not $entry) { Keys @(0x1B); 'no menu entry'; $false; return }
  $entry.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Seconds 2; Shot 'looks-fence-settings'
  $window = WindowLike '*fence settings'; if ($window) { [void][NfVm.W]::SetForegroundWindow($window); Keys @(0x12, 0x73) }
  [bool]$window
}
