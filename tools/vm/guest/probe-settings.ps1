# Inside a NeoFences test VM (M41b, spec 2026-10-11-m41b-settings-design sections 1-4): screenshots of the new settings -
# Fence settings (this fence, its tabs, the explainer, All fences, a theme for all), the recipe confirmations, and Settings
# (every page and the search) - for the owner to judge. Dot-sourced by guest-checks.ps1 (Invoke-VmChecks.ps1 -Live -Script
# probe-settings.ps1). Every step is a check that passes when it ran; the pictures are the result. Windows PowerShell 5.1.
# ASCII only.
. (Join-Path $root 'live-helpers.ps1')
$exe = Join-Path $run 'build\NeoFences.exe' # this checkout's build, not an installed one
$withData = Test-Path -LiteralPath (Join-Path $run 'data')
function Emoji([int]$code) { [char]::ConvertFromUtf32($code) }
Check 'start' { StartNeoFences; StopNeoFences; Test-Path -LiteralPath (Join-Path $data 'config.json') }
Check 'three-fences' {
  $config = Get-Content -LiteralPath (Join-Path $data 'config.json') -Raw | ConvertFrom-Json
  $fingerprint = $config.lastLayoutFingerprint
  $monitor = @($config.layouts.$fingerprint.fences.PSObject.Properties)[0].Value.monitor
  $fences = @(
    @{ id = 'games'; title = 'Games'; emoji = (Emoji 0x1F3AE); look = @{ theme = 'hud'; titleStyle = 'caps' } },
    @{ id = 'apps'; title = 'Apps' },
    @{ id = 'downloads'; title = 'Downloads'; tabColor = 'orange' })
  $rects = @{}; $index = 0
  foreach ($fence in $fences) { $rects[$fence.id] = @{ monitor = $monitor; x = 12 + $index * 250; y = 12; w = 236; h = 230 }; $index++ }
  $config.fences = $fences
  $config.layouts.$fingerprint.fences = $rects
  $config | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath (Join-Path $data 'config.json') -Encoding UTF8
  $targets = @('C:\Windows', 'C:\Users', 'C:\Windows\notepad.exe', 'C:\Windows\regedit.exe', 'https://github.com')
  $items = @{}
  foreach ($fence in $fences) { $items[$fence.id] = @($targets | ForEach-Object -Begin { $n = 0 } -Process { @{ id = "$($fence.id)-$n"; target = $_ }; $n++ }) }
  [ordered]@{ schema = 1; fences = $items } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $data 'items.json') -Encoding UTF8
  StartNeoFences; Start-Sleep -Seconds 3; Shot 'settings-fences'
  "fences: $(@(Fences).Count)"; @(Fences).Count -eq 3
}
Check 'fence-settings-open' {
  $opened = OpenFromFenceMenu "Fence settings$([char]0x2026)"; Start-Sleep -Seconds 2; Shot 'fs-this-fence'
  $window = WindowLike '*fence settings'; "opened: $opened; window: $([bool]$window)"; [bool]$window
}
Check 'fence-settings-explainer' {
  $row = Element 'Title style' ([System.Windows.Automation.ControlType]::Text)
  if ($row) { $x, $y = Center $row; Pointer $x $y; Start-Sleep -Milliseconds 500 }
  Shot 'fs-explainer'; [bool]$row
}
foreach ($tab in 'Title', 'Items', 'Behaviour') {
  Check "fence-settings-tab-$tab" { $clicked = ClickOn (Button $tab); Start-Sleep -Milliseconds 600; Shot "fs-tab-$($tab.ToLower())"; $clicked }
}
Check 'fence-settings-theme-this' {
  ClickOn (Button 'Look') | Out-Null; Start-Sleep -Milliseconds 400
  $clicked = ClickOn (Button 'Neon'); Start-Sleep -Seconds 2; Shot 'fs-this-neon'
  $log = @(LogText | Select-String -SimpleMatch 'fence settings: fence.theme = Neon (this fence)').Count -gt 0
  "clicked: $clicked; logged: $log"; $log
}
Check 'fence-settings-all' {
  $clicked = ClickOn (Button 'All fences'); Start-Sleep -Seconds 1; Shot 'fs-all-fences'
  $theme = ClickOn (Button 'Game HUD'); Start-Sleep -Seconds 2; Shot 'fs-all-hud'
  $log = @(LogText | Select-String -SimpleMatch 'fence settings: fence.theme = Hud (All fences)').Count -gt 0
  "clicked: $clicked $theme; logged: $log"; $log
}
Check 'recipe-all-dialogs' {
  $clicked = ClickOn (Button 'Recipe Minimal'); Start-Sleep -Seconds 1; Shot 'recipe-all-step1'
  Keys @(0x0D); Start-Sleep -Seconds 1; Shot 'recipe-all-step2'
  Keys @(0x1B); Start-Sleep -Milliseconds 600
  "clicked: $clicked"; $clicked
}
Check 'recipe-one-dialog' {
  ClickOn (Button 'This fence') | Out-Null; Start-Sleep -Milliseconds 600
  $clicked = ClickOn (Button 'Recipe Game shelf'); Start-Sleep -Seconds 1; Shot 'recipe-one'
  Keys @(0x1B); Start-Sleep -Milliseconds 600
  $window = WindowLike '*fence settings'; if ($window) { [void][NfVm.W]::SetForegroundWindow($window); Keys @(0x1B) }
  "clicked: $clicked"; $clicked
}
Check 'settings-open' {
  $opened = OpenFromFenceMenu "Settings$([char]0x2026)"; Start-Sleep -Seconds 2; Shot 'settings-look'
  $window = WindowLike 'NeoFences settings'; "opened: $opened; window: $([bool]$window)"; [bool]$window
}
foreach ($page in 'Desktop', 'Games', 'Peek & keys', 'Snapshots & safety', 'Updates & privacy', 'Help & about') {
  Check "settings-page-$($page.Split(' ')[0].ToLower())" {
    $item = Element $page ([System.Windows.Automation.ControlType]::ListItem)
    $clicked = ClickOn $item; Start-Sleep -Milliseconds 700; Shot "settings-$($page.Split(' ')[0].ToLower())"; $clicked
  }
}
Check 'settings-search' {
  $box = Element 'Search settings' ([System.Windows.Automation.ControlType]::Edit)
  if (-not $box) { 'no search box'; $false; return }
  $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('double'); Start-Sleep -Seconds 1; Shot 'settings-search'
  $results = @((Element 'Double-click empty desktop hides all fences, Settings - Desktop' ([System.Windows.Automation.ControlType]::Button)), (Button "Open items with, Fence settings $([char]0x2192) Behaviour (all fences, or one fence)"))
  "results found: $(@($results | Where-Object { $_ }).Count)"; $true
}
Check 'settings-search-to-fence-settings' {
  $result = @([System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Button))) |
    Where-Object { $_.Current.Name -like 'Open items with*' })[0]
  $clicked = ClickOn $result; Start-Sleep -Seconds 2; Shot 'settings-search-opened'
  "clicked: $clicked; window: $([bool](WindowLike 'All fences*'))"; $clicked
}
