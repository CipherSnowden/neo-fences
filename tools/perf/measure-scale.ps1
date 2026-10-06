# NeoFences performance at scale (M37, ADR-058): a 500-item / 50-fence setup on a backed-up copy of the data folder,
# then a cold start (no icon cache, frame statistics on: scroll the big fence, drag and resize the medium one) and a warm
# start (CPU over 60 s idle and 60 s in game mode). Everything is put back afterwards: the data folder, the startup entry,
# and the installed NeoFences is started again. Needs PowerShell 7.5+ (it reads the log with NeoFences.Core.dll).
# Moves the mouse: keep hands off while it runs. ASCII only.
param(
  [string]$Exe = (Join-Path $env:LOCALAPPDATA 'NeoFences.App\current\NeoFences.exe'),
  [int]$Fences = 50,
  [int]$Items = 500,
  [int]$BigItems = 200,
  [int]$MediumItems = 100,
  [string]$Out = (Join-Path ([IO.Path]::GetTempPath()) 'neofences-perf'),
  [switch]$SkipGame,
  [switch]$StartOnly # only the cold and warm start times (to compare builds, e.g. ready-to-run)
)
$ErrorActionPreference = 'Stop'
$data = Join-Path $env:LOCALAPPDATA 'NeoFences'
$installed = Join-Path $env:LOCALAPPDATA 'NeoFences.App\current\NeoFences.exe'
$backup = Join-Path $Out 'data-backup'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-Item -ItemType Directory -Force $Out | Out-Null
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
if (-not ('NeoFences.Core.Lifecycle.PerfLog' -as [type])) { Add-Type -Path (Join-Path (Split-Path $Exe) 'NeoFences.Core.dll') } # once per session
if (-not ('NfPerf.W' -as [type])) { Add-Type -Namespace NfPerf -Name W -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder text, int max);
[DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr data);
delegate bool EnumProc(IntPtr h, IntPtr data);
public struct RECT { public int Left, Top, Right, Bottom; }
public static System.Collections.Generic.List<IntPtr> Fences() {
  var found = new System.Collections.Generic.List<IntPtr>();
  EnumWindows((h, _) => { var text = new System.Text.StringBuilder(64); GetWindowText(h, text, 64); if (text.ToString() == "NeoFences fence") found.Add(h); return true; }, IntPtr.Zero);
  return found;
}
'@ }

function Pause-Ms([int]$ms) { Start-Sleep -Milliseconds $ms }
function Running { @(Get-Process NeoFences -ErrorAction SilentlyContinue) }
function StopNeoFences {
  # --exit is asked again every 5 s: a copy that has only just started ignores it.
  $deadline = (Get-Date).AddSeconds(40)
  while ((Running).Count -gt 0 -and (Get-Date) -lt $deadline) {
    foreach ($path in @(Running | Select-Object -ExpandProperty Path -Unique)) { & $path --exit }
    $wait = (Get-Date).AddSeconds(5); while ((Running).Count -gt 0 -and (Get-Date) -lt $wait) { Pause-Ms 300 }
  }
  if ((Running).Count -gt 0) { throw 'NeoFences did not exit' }
}
function LogSince([datetime]$since) {
  # The newest two daily logs (a file NeoFences holds open keeps an old LastWriteTime, so it cannot pick them); the reader
  # takes the last start or a time window out of them.
  Get-ChildItem (Join-Path $data 'logs') -Filter 'neofences-*.log' -ErrorAction SilentlyContinue | Sort-Object Name |
    Select-Object -Last 2 | ForEach-Object { Get-Content -LiteralPath $_.FullName }
}
function StartNeoFences([switch]$Perf) {
  $since = Get-Date
  $settledBefore = @(LogSince $since | Select-String -SimpleMatch 'timing: icons settled').Count # earlier runs' marks are in the same logs
  $env:NEOFENCES_PERF = if ($Perf) { '1' } else { $null }
  Start-Process $Exe | Out-Null
  $env:NEOFENCES_PERF = $null
  $deadline = (Get-Date).AddSeconds(90)
  while ((Get-Date) -lt $deadline -and @(LogSince $since | Select-String -SimpleMatch 'timing: icons settled').Count -le $settledBefore) { Pause-Ms 500 }
  Pause-Ms 1500
  $since
}
function CpuSeconds { (Running | Measure-Object -Property { $_.TotalProcessorTime.TotalSeconds } -Sum).Sum }
function FenceRect([string]$title) {
  # Fences are found by their window title (they may sit under the desktop layer, out of UI Automation's top level),
  # then told apart by their first text (the title).
  $text = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
  foreach ($handle in [NfPerf.W]::Fences()) {
    $first = [System.Windows.Automation.AutomationElement]::FromHandle($handle).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $text)
    if ($first -and $first.Current.Name -eq $title) { $rect = New-Object NfPerf.W+RECT; [void][NfPerf.W]::GetWindowRect($handle, [ref]$rect); return $rect }
  }
  $null
}
function Wheel([int]$x, [int]$y, [int]$notches, [int]$direction) {
  [void][NfPerf.W]::SetCursorPos($x, $y); Pause-Ms 200
  foreach ($notch in 1..$notches) { [NfPerf.W]::mouse_event(0x0800, 0, 0, 120 * $direction, [UIntPtr]::Zero); Pause-Ms 40 }
}
function Drag([int]$x, [int]$y, [int]$dx, [int]$dy) {
  [void][NfPerf.W]::SetCursorPos($x, $y); Pause-Ms 200
  [NfPerf.W]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
  foreach ($step in 1..30) { [void][NfPerf.W]::SetCursorPos([int]($x + $dx * $step / 30), [int]($y + $dy * $step / 30)); Pause-Ms 30 }
  [NfPerf.W]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero); Pause-Ms 500
}
function Targets([int]$count) {
  $pool = [System.Collections.Generic.List[string]]::new()
  $exes = @(Get-ChildItem "$env:WINDIR\System32" -Filter '*.exe' -ErrorAction SilentlyContinue | Select-Object -First 260 -ExpandProperty FullName)
  $pictures = @(Get-ChildItem "$env:WINDIR\Web" -Recurse -Include '*.jpg', '*.png' -ErrorAction SilentlyContinue | Select-Object -First 40 -ExpandProperty FullName)
  $folders = @(Get-ChildItem $env:ProgramFiles -Directory -ErrorAction SilentlyContinue | Select-Object -First 40 -ExpandProperty FullName)
  $media = @(Get-ChildItem "$env:WINDIR\Media" -Filter '*.wav' -ErrorAction SilentlyContinue | Select-Object -First 40 -ExpandProperty FullName)
  $apps = @((New-Object -ComObject Shell.Application).NameSpace('shell:AppsFolder').Items() | Select-Object -First 30 | ForEach-Object { "shell:AppsFolder\$($_.Path)" })
  $sites = @('https://www.wikipedia.org', 'https://github.com', 'https://www.youtube.com', 'https://store.steampowered.com', 'https://www.reddit.com',
    'https://learn.microsoft.com', 'https://www.bbc.com', 'https://news.ycombinator.com', 'https://www.twitch.tv', 'https://www.nexusmods.com')
  $kinds = @($exes, $pictures, $folders, $media, $apps, $sites) | Where-Object { $_.Count -gt 0 }
  $index = 0
  while ($pool.Count -lt $count) {
    $kind = $kinds[$index % $kinds.Count]; $pool.Add($kind[[int][Math]::Floor($index / $kinds.Count) % $kind.Count]); $index++
  }
  $pool
}
function NewItem([string]$target) { [ordered]@{ id = [guid]::NewGuid().ToString('N'); target = $target } }

"===== NeoFences performance at scale: keep hands off the mouse (about 4 min) ====="
if (Test-Path -LiteralPath $backup) { throw "a backup is already at ${backup}: put it back or remove it first" }
StopNeoFences
Copy-Item -LiteralPath $data -Destination $backup -Recurse
$runValue = (Get-ItemProperty $runKey -ErrorAction SilentlyContinue).NeoFences
"data backed up to $backup"
$report = [System.Collections.Generic.List[string]]::new()
try {
  # The setup: the existing fences and items, plus new fences up to the counts (one big, one medium, the rest small).
  $config = Get-Content -LiteralPath (Join-Path $data 'config.json') -Raw | ConvertFrom-Json -AsHashtable -DateKind String
  $itemsDoc = Get-Content -LiteralPath (Join-Path $data 'items.json') -Raw | ConvertFrom-Json -AsHashtable -DateKind String
  if (-not $itemsDoc.fences) { $itemsDoc.fences = [ordered]@{} }
  $existingItems = ($itemsDoc.fences.Values | ForEach-Object { @($_).Count } | Measure-Object -Sum).Sum
  $newFences = [Math]::Max(2, $Fences - @($config.fences).Count)
  $smallTotal = [Math]::Max(0, $Items - $existingItems - $BigItems - $MediumItems)
  $targets = @(Targets ($BigItems + $MediumItems + $smallTotal))
  $next = 0
  $fenceList = [System.Collections.Generic.List[object]]::new(); foreach ($fence in @($config.fences)) { $fenceList.Add($fence) }
  foreach ($number in 1..$newFences) {
    $title = if ($number -eq 1) { 'Perf big' } elseif ($number -eq 2) { 'Perf medium' } else { "Perf $number" }
    $count = if ($number -eq 1) { $BigItems } elseif ($number -eq 2) { $MediumItems } else { [int][Math]::Floor($smallTotal / ($newFences - 2)) + ([int](($number - 3) -lt ($smallTotal % ($newFences - 2)))) }
    $id = [guid]::NewGuid().ToString('N')
    $fenceList.Add([ordered]@{ id = $id; title = $title })
    $itemsDoc.fences[$id] = @(foreach ($slot in 1..$count) { if ($next -lt $targets.Count) { NewItem $targets[$next]; $next++ } })
  }
  $config.fences = $fenceList
  $config | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $data 'config.json') -Encoding utf8NoBOM
  $itemsDoc | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $data 'items.json') -Encoding utf8NoBOM
  $report.Add("setup: $($fenceList.Count) fences, $($existingItems + $next) items ($Exe)")

  # Cold start: no icon cache; frame statistics on.
  Remove-Item -LiteralPath (Join-Path $data 'cache') -Recurse -Force -ErrorAction SilentlyContinue
  $coldSince = StartNeoFences -Perf
  if ($StartOnly) { } elseif ($big = FenceRect 'Perf big') {
    $x = [int]($big.Left + ($big.Right - $big.Left) / 2); $y = [int]($big.Top + ($big.Bottom - $big.Top) / 2)
    $from = [DateTimeOffset](Get-Date); Wheel $x $y 25 -1; Wheel $x $y 25 1; Pause-Ms 2500; $to = [DateTimeOffset](Get-Date)
    $scroll = [NeoFences.Core.Lifecycle.PerfLog]::FramesBetween([string[]]@(LogSince $coldSince), $from, $to)
    $report.Add("scroll the big fence: worst frame $($scroll.Item2) ms, $($scroll.Item3) of $($scroll.Item1) over 33 ms")
  } else { $report.Add('scroll: the big fence was not found') }
  if ($StartOnly) { } elseif ($medium = FenceRect 'Perf medium') {
    $from = [DateTimeOffset](Get-Date)
    Drag ([int](($medium.Left + $medium.Right) / 2)) ([int]($medium.Top + 12)) 200 120
    Drag ([int]($medium.Right + 200 - 3)) ([int]($medium.Bottom + 120 - 3)) 160 100
    Pause-Ms 2500
    $drag = [NeoFences.Core.Lifecycle.PerfLog]::FramesBetween([string[]]@(LogSince $coldSince), $from, [DateTimeOffset](Get-Date))
    $report.Add("drag and resize the medium fence: worst frame $($drag.Item2) ms, $($drag.Item3) of $($drag.Item1) over 33 ms")
  } else { $report.Add('drag: the medium fence was not found') }
  StopNeoFences
  $cold = [NeoFences.Core.Lifecycle.PerfLog]::Read([string[]]@(LogSince $coldSince)) | Select-Object -Last 1
  $report.Add("cold start: fences shown $($cold.FencesShownMs) ms; icons settled $($cold.IconsSettledMs) ms ($($cold.IconRequests) requests, $($cold.FromCache) from the cache); frames: worst $($cold.WorstFrameMs) ms, $($cold.SlowFrames) of $($cold.Frames) over 33 ms")
  @(LogSince $coldSince | Select-String -Pattern 'timing: (windows opened|items set|layout applied|slowest fence placed)' | Select-Object -Last 4 | ForEach-Object { '  cold ' + ($_.Line -replace '^.*timing: ', '') }) | ForEach-Object { $report.Add($_) }

  # Warm start: the cache is there; CPU idle and in game mode (frame statistics off: they keep WPF drawing).
  $warmSince = StartNeoFences
  $warm = [NeoFences.Core.Lifecycle.PerfLog]::Read([string[]]@(LogSince $warmSince)) | Select-Object -Last 1
  $report.Add("warm start: fences shown $($warm.FencesShownMs) ms; icons settled $($warm.IconsSettledMs) ms ($($warm.IconRequests) requests, $($warm.FromCache) from the cache)")
  if (-not $StartOnly) {
    [void][NfPerf.W]::SetCursorPos(10, 10)
    Pause-Ms 5000; $before = CpuSeconds; Start-Sleep -Seconds 60; $report.Add(('idle: {0:N2} s CPU over 60 s' -f ((CpuSeconds) - $before)))
  }
  if (-not $SkipGame -and -not $StartOnly) {
    # A borderless window over the whole screen (the taskbar too: a maximized one is not "full screen" to Windows) is what
    # game mode reacts to. WinForms needs STA: a child pwsh -STA.
    $gameScript = 'Add-Type -AssemblyName System.Windows.Forms; $form = New-Object System.Windows.Forms.Form -Property @{ FormBorderStyle = "None"; StartPosition = "Manual"; Bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds; BackColor = "Black"; TopMost = $true; Text = "NeoFences perf game" }; $timer = New-Object System.Windows.Forms.Timer -Property @{ Interval = 75000 }; $timer.Add_Tick({ $form.Close() }); $timer.Start(); [void]$form.ShowDialog()'
    $gameFrom = Get-Date
    $game = Start-Process pwsh -ArgumentList '-NoProfile', '-STA', '-Command', $gameScript -PassThru
    Start-Sleep -Seconds 10
    $engaged = @(LogSince $gameFrom | Select-String -SimpleMatch 'game mode: True' | Where-Object { [DateTimeOffset]::ParseExact($_.Line.Substring(0, 30), 'yyyy-MM-dd HH:mm:ss.fff zzz', $null) -ge [DateTimeOffset]$gameFrom }).Count -gt 0
    $before = CpuSeconds; Start-Sleep -Seconds 60
    $report.Add(('game mode{0}: {1:N2} s CPU over 60 s' -f $(if ($engaged) { '' } else { ' (NOT engaged: the window did not count as full screen)' }), ((CpuSeconds) - $before)))
    $game.WaitForExit(15000) | Out-Null; if (-not $game.HasExited) { $game.Kill() }
  }
} finally {
  try { StopNeoFences } catch { Running | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force; Start-Sleep -Seconds 2 } # the data goes back whatever happened
  Copy-Item -LiteralPath (Join-Path $data 'logs') -Destination (Join-Path $Out 'logs') -Recurse -Force -ErrorAction SilentlyContinue # the run's logs, before the data goes back
  if ($data -ne (Join-Path $env:LOCALAPPDATA 'NeoFences')) { throw 'unexpected data path' }
  Remove-Item -LiteralPath $data -Recurse -Force
  Copy-Item -LiteralPath $backup -Destination $data -Recurse
  if ($runValue) { Set-ItemProperty $runKey -Name NeoFences -Value $runValue }
  Remove-Item -LiteralPath $backup -Recurse -Force
  if (Test-Path -LiteralPath $installed) { Start-Process $installed }
  $report | Set-Content -LiteralPath (Join-Path $Out 'report.txt')
  $report
  "===== done: data and startup entry put back, NeoFences started again; the mouse is yours ====="
}
