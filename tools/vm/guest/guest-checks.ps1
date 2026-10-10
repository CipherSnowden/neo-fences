# Inside a NeoFences test VM (M38, spec 2026-10-06-keyboard-and-older-windows-design section 2): the checklist, on the tester's
# desktop. Reads C:\NeoFencesTest\run (previous-Setup.exe, feed\ with the release candidate, run.json), writes
# C:\NeoFencesTest\out (results.json after every check, screenshots, NeoFences' logs). Windows PowerShell 5.1. ASCII only.
$ErrorActionPreference = 'Stop'
$root = 'C:\NeoFencesTest'
$run = Join-Path $root 'run'
$out = Join-Path $root 'out'
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null
$settings = Get-Content -LiteralPath (Join-Path $run 'run.json') -Raw | ConvertFrom-Json
$appDir = Join-Path $env:LOCALAPPDATA 'NeoFences.App'
$exe = Join-Path $appDir 'current\NeoFences.exe'
$data = Join-Path $env:LOCALAPPDATA 'NeoFences'
$advanced = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace NfVm -Name W -MemberDefinition @'
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder text, int max);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr data);
[DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
public delegate bool EnumProc(IntPtr h, IntPtr data);
public static System.Collections.Generic.List<IntPtr> TopLevel() {
  var all = new System.Collections.Generic.List<IntPtr>();
  EnumWindows(delegate (IntPtr h, IntPtr d) { all.Add(h); return true; }, IntPtr.Zero);
  return all;
}
public static string Title(IntPtr h) { var t = new System.Text.StringBuilder(256); GetWindowText(h, t, 256); return t.ToString(); }
'@

$checks = New-Object System.Collections.ArrayList
function Save([bool]$finished = $false) { [ordered]@{ machine = $settings.machine; version = $settings.candidate; finished = $finished; checks = $checks } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $out 'results.json') -Encoding UTF8 }
function Check([string]$id, [scriptblock]$test) {
  $note = ''
  try { $result = & $test; $ok = [bool]($result | Select-Object -Last 1); $note = [string](($result | Select-Object -SkipLast 1) -join '; ') }
  catch { $ok = $false; $note = $_.Exception.Message }
  [void]$checks.Add([ordered]@{ id = $id; ok = $ok; note = $note })
  Save
  Shot $id
}
function Shot([string]$name) { $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $b = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height; $g = [System.Drawing.Graphics]::FromImage($b); $g.CopyFromScreen(0, 0, 0, 0, $b.Size)
  $b.Save((Join-Path $out "$name.png")); $g.Dispose(); $b.Dispose() }
Add-Type -AssemblyName System.Windows.Forms
function Wait([scriptblock]$condition, [int]$seconds) { $deadline = (Get-Date).AddSeconds($seconds); while (-not (& $condition) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }; [bool](& $condition) }
function Main { @(Get-CimInstance Win32_Process -Filter "Name = 'NeoFences.exe'" | Where-Object { $_.CommandLine -notlike '*--watchdog*' }) }
function Running { @(Get-Process NeoFences -ErrorAction SilentlyContinue).Count -gt 0 }
function Fences { @([NfVm.W]::TopLevel() | Where-Object { [NfVm.W]::Title($_) -eq 'NeoFences fence' -and [NfVm.W]::IsWindowVisible($_) }) }
function IconsHidden { (Get-ItemProperty $advanced -ErrorAction SilentlyContinue).HideIcons -eq 1 }
function LogText { Get-ChildItem (Join-Path $data 'logs') -Filter 'neofences-*.log' -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 2 | ForEach-Object { Get-Content -LiteralPath $_.FullName } }
function Keys([byte[]]$vks) { foreach ($vk in $vks) { [NfVm.W]::keybd_event($vk, 0, 0, [UIntPtr]::Zero) }; [array]::Reverse($vks); foreach ($vk in $vks) { [NfVm.W]::keybd_event($vk, 0, 2, [UIntPtr]::Zero) }; Start-Sleep -Milliseconds 800 }
function StopNeoFences { if (Test-Path -LiteralPath $exe) { & $exe --exit }; [void](Wait { -not (Running) } 30) }
function StartNeoFences { Start-Process $exe; [void](Wait { @(Fences).Count -gt 0 } 60); Start-Sleep -Seconds 4 }
function SetConfig([string]$name, $value) {
  $path = Join-Path $data 'config.json'; $config = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
  $config.settings | Add-Member -NotePropertyName $name -NotePropertyValue $value -Force
  $config | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $path -Encoding UTF8 }
# Windows 11's Notepad is a Store app: notepad.exe hands over and exits, and a second start may open a tab in the first
# window, so the window is found by its title.
function NotepadWindow { @([NfVm.W]::TopLevel() | Where-Object { [NfVm.W]::IsWindowVisible($_) -and [NfVm.W]::Title($_) -like '*Notepad' }) | Select-Object -First 1 }
function Notepad { if (-not (NotepadWindow)) { Start-Process notepad; [void](Wait { [bool](NotepadWindow) } 30) }
  $window = NotepadWindow; if (-not $window) { throw 'Notepad did not open' }
  Keys @(0x12); [void][NfVm.W]::SetForegroundWindow($window); Start-Sleep -Milliseconds 800; $window }
function Notifications { $state = 0; [void][NfVm.W]::SHQueryUserNotificationState([ref]$state)
  @{ 1 = 'not present'; 2 = 'busy'; 3 = 'D3D full screen'; 4 = 'presentation'; 5 = 'accepts notifications'; 6 = 'quiet time (the first hour after the account first signed in)'; 7 = 'app' }[$state] }

function Finish {
  Copy-Item -LiteralPath (Join-Path $data 'logs') -Destination (Join-Path $out 'logs') -Recurse -Force -ErrorAction SilentlyContinue
  Save $true # the host counts a file without it as a pass that did not finish
  Set-Content -LiteralPath (Join-Path $out 'finished.txt') -Value (Get-Date -Format s) }
# A live check of a branch build (Invoke-VmChecks.ps1 -Live, M38.1): its own checks with the helpers above.
if ($settings.script) { . (Join-Path $root $settings.script); Finish; return }

$os = Get-CimInstance Win32_OperatingSystem
"machine: $($os.Caption) $($os.Version)"
Check 'install' {
  (Start-Process (Join-Path $run 'previous-Setup.exe') -ArgumentList '--silent' -PassThru).WaitForExit() # -Wait would wait for NeoFences too (5.1 waits for the whole tree)
  if (-not (Wait { (Running) -and @(Fences).Count -gt 0 } 90)) { StartNeoFences }
  "version $((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion)"; (Running) -and @(Fences).Count -gt 0
}
Check 'welcome' { [void](Wait { Test-Path -LiteralPath (Join-Path $data 'config.json') } 30); $config = Get-Content -LiteralPath (Join-Path $data 'config.json') -Raw | ConvertFrom-Json; "fences: $(@($config.fences).Count)"; @($config.fences | Where-Object { $_.welcome }).Count -eq 1 }
Check 'behind-windows' {
  $notepad = Notepad; $order = [NfVm.W]::TopLevel(); $fence = @(Fences)[0]
  "notepad at $($order.IndexOf($notepad)), fence at $($order.IndexOf($fence))"; $order.IndexOf($notepad) -lt $order.IndexOf($fence)
}
Check 'show-desktop' {
  Keys @(0x5B, 0x44); Start-Sleep -Seconds 1; $fence = @(Fences)[0]
  $shown = $fence -and [NfVm.W]::IsWindowVisible($fence) -and -not [NfVm.W]::IsIconic($fence)
  Shot 'show-desktop-on'; Keys @(0x5B, 0x44); "fence visible after Win+D: $shown"; $shown
}
Check 'icons-hidden' { StopNeoFences; SetConfig 'hideDesktopIcons' $true; StartNeoFences; "HideIcons: $((Get-ItemProperty $advanced).HideIcons)"; IconsHidden }
Check 'icons-after-exit' { StopNeoFences; $back = Wait { -not (IconsHidden) } 15; "icons back after exit: $back"; $back }
Check 'icons-after-kill' {
  StartNeoFences; $hiddenBefore = IconsHidden
  $main = Main | Select-Object -First 1; Stop-Process -Id $main.ProcessId -Force
  $seen = Wait { -not (IconsHidden) } 15; $restarted = Wait { @(Main).Count -gt 0 } 30; Start-Sleep -Seconds 6
  "hidden before: $hiddenBefore; icons shown after the kill: $seen; NeoFences back: $restarted"; $hiddenBefore -and $seen
}
Check 'explorer-restart' {
  if (-not (Running)) { StartNeoFences }
  Stop-Process -Name explorer -Force; Start-Sleep -Seconds 4
  if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer }
  Start-Sleep -Seconds 12
  "fences visible: $(@(Fences).Count); NeoFences running: $(Running); icons hidden: $(IconsHidden)"; (Running) -and @(Fences).Count -gt 0 -and (IconsHidden)
}
Check 'game-mode' {
  $edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
  if (-not (Test-Path -LiteralPath $edge)) { 'no Edge'; $false; return }
  $before = @(LogText | Select-String -SimpleMatch 'game mode: True').Count
  $since = Get-Date
  Start-Process $edge -ArgumentList '--kiosk', 'about:blank', '--edge-kiosk-type=fullscreen', '--no-first-run', "--user-data-dir=$env:TEMP\nf-edge"
  $on = Wait { @(LogText | Select-String -SimpleMatch 'game mode: True').Count -gt $before } 25
  $windows = Notifications; Shot 'game-mode-on'
  Get-Process msedge -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -ge $since } | Stop-Process -Force -ErrorAction SilentlyContinue
  "game mode on: $on; Windows reports: $windows"; $on
}
Check 'update' {
  StopNeoFences
  $env:NEOFENCES_UPDATE_SOURCE = Join-Path $run 'feed'; Start-Process $exe; Remove-Item Env:\NEOFENCES_UPDATE_SOURCE
  $downloaded = Wait { @(LogText | Select-String -Pattern "update $([regex]::Escape($settings.candidate)) downloaded").Count -gt 0 } 240
  StopNeoFences; Start-Sleep -Seconds 20; StartNeoFences
  $version = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
  "downloaded: $downloaded; version now: $version; icons hidden: $(IconsHidden)"; $version -like "$($settings.candidate)*" -and (IconsHidden)
}
# M39 (ADR-061): the licence, the third-party notices and the privacy note are installed next to NeoFences.exe.
Check 'trust-files' {
  $missing = @('LICENSE.txt', 'THIRD-PARTY-NOTICES.txt', 'PRIVACY.md' | Where-Object { -not (Test-Path -LiteralPath (Join-Path $appDir "current\$_")) })
  "missing: $(if ($missing.Count) { $missing -join ', ' } else { 'none' })"; $missing.Count -eq 0
}
# After the update: the keyboard way in is new in the candidate.
Check 'peek-keyboard' {
  $notepad = Notepad; Keys @(0x11, 0x12, 0x20); Start-Sleep -Seconds 1
  $inFence = [NfVm.W]::Title([NfVm.W]::GetForegroundWindow()) -eq 'NeoFences fence'; Shot 'peek-keyboard-on'
  Keys @(0x1B); Start-Sleep -Seconds 1; $back = [NfVm.W]::GetForegroundWindow() -eq $notepad
  "keyboard in a fence: $inFence; back to Notepad after Esc: $back"; $inFence -and $back
}
# rc.2 (feedback #6): after Peek had the keyboard, Win+D twice must still bring a maximized window back (Windows
# Terminal, as the owner found it).
Check 'win-d-after-peek' {
  Add-Type -Namespace NfVmD -Name W -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr h, int cmd);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(System.IntPtr h, System.Text.StringBuilder s, int n);
'@
  $find = { @([NfVm.W]::TopLevel() | Where-Object { $s = New-Object System.Text.StringBuilder 256; [void][NfVmD.W]::GetClassName($_, $s, 256); $s.ToString() -eq 'CASCADIA_HOSTING_WINDOW_CLASS' }) | Select-Object -First 1 }
  if (-not (& $find)) { Start-Process wt.exe; [void](Wait { [bool](& $find) } 40) }
  $terminal = & $find; if (-not $terminal) { throw 'Windows Terminal did not open' }
  [void][NfVmD.W]::ShowWindow($terminal, 3); Keys @(0x12); [void][NfVm.W]::SetForegroundWindow($terminal); Start-Sleep -Seconds 1
  Keys @(0x11, 0x12, 0x20); Start-Sleep -Seconds 1; Keys @(0x1B); Start-Sleep -Seconds 1 # Peek takes the keyboard, Esc gives it back
  Keys @(0x5B, 0x44); Start-Sleep -Seconds 3; $hidden = [NfVm.W]::IsIconic($terminal)
  Keys @(0x5B, 0x44); Start-Sleep -Seconds 3; $back = -not [NfVm.W]::IsIconic($terminal) -and [NfVm.W]::IsWindowVisible($terminal)
  Get-Process WindowsTerminal -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  "desktop shown by the first Win+D: $hidden; Terminal back after the second: $back"; $hidden -and $back
}
Check 'uninstall' {
  StopNeoFences; (Start-Process (Join-Path $appDir 'Update.exe') -ArgumentList '--uninstall' -PassThru).WaitForExit(); Start-Sleep -Seconds 5
  $gone = -not (Test-Path -LiteralPath $exe); "app removed: $gone; icons hidden: $(IconsHidden)"; $gone -and -not (IconsHidden)
}
Finish
