# Inside a NeoFences test VM: helpers for the live checks and probes of a branch build (M38.1; M41a: split out of
# live-checks.ps1 so probe-looks.ps1 shares them), dot-sourced after guest-checks.ps1's own. Windows PowerShell 5.1. ASCII only.
Add-Type -Namespace NfVmL -Name W -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
public struct RECT { public int Left, Top, Right, Bottom; }
'@
function Fg { [NfVm.W]::GetForegroundWindow() }
function IsFence([IntPtr]$window) { [NfVm.W]::Title($window) -eq 'NeoFences fence' }
function Rect([IntPtr]$window) { $rect = New-Object NfVmL.W+RECT; [void][NfVmL.W]::GetWindowRect($window, [ref]$rect); $rect }
function Height([IntPtr]$window) { $rect = Rect $window; $rect.Bottom - $rect.Top }
function Pointer([int]$x, [int]$y) { [void][NfVmL.W]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 400 }
function Taskbar { $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds; Pointer ([int]($bounds.Width / 2)) ($bounds.Height - 10) }
function Peek { Keys @(0x11, 0x12, 0x20) }
function PeekEnds { @(LogText | Select-String -SimpleMatch 'peek: False').Count }
function Front([IntPtr]$window) { Keys @(0x12); [void][NfVm.W]::SetForegroundWindow($window); Start-Sleep -Milliseconds 800 }
function WindowLike([string]$pattern) { @([NfVm.W]::TopLevel() | Where-Object { [NfVm.W]::IsWindowVisible($_) -and [NfVm.W]::Title($_) -like $pattern }) | Select-Object -First 1 }
# M41a: real mouse clicks (through Windows' input, so the hook and Explorer see them) and UI Automation to find things.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
function Press([int]$x, [int]$y, [uint32]$down, [uint32]$up) { Pointer $x $y; [NfVmL.W]::mouse_event($down, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [NfVmL.W]::mouse_event($up, 0, 0, 0, [UIntPtr]::Zero) }
function LeftClick([int]$x, [int]$y) { Press $x $y 0x02 0x04; Start-Sleep -Milliseconds 700 }
function RightClick([int]$x, [int]$y) { Press $x $y 0x08 0x10; Start-Sleep -Milliseconds 1200 }
function DoubleClick([int]$x, [int]$y) { Press $x $y 0x02 0x04; Start-Sleep -Milliseconds 90
  [NfVmL.W]::mouse_event(0x02, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [NfVmL.W]::mouse_event(0x04, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 1500 }
function CloseApps { Get-Process notepad, mspaint -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 1 }
function Element([string]$name, $type) {
  $byName = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $name
  $byType = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), $type
  $condition = New-Object System.Windows.Automation.AndCondition $byName, $byType
  [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition) }
function Center($element) { $box = $element.Current.BoundingRectangle; @([int]($box.X + $box.Width / 2), [int]($box.Y + $box.Height / 2)) }
function EmptyWallpaper { $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds; @([int]($bounds.Width * 0.93), [int]($bounds.Height * 0.8)) }
function DesktopMenuShown { [bool](Element 'Refresh' ([System.Windows.Automation.ControlType]::MenuItem)) }


# M41a: a 512 px cover for the fake player (an orange-to-violet gradient with a sun).
function CoverImage([string]$path) {
  Add-Type -AssemblyName System.Drawing
  $cover = New-Object System.Drawing.Bitmap 512, 512; $g = [System.Drawing.Graphics]::FromImage($cover)
  $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point 512, 512), ([System.Drawing.Color]::FromArgb(255, 255, 106, 61)), ([System.Drawing.Color]::FromArgb(255, 123, 47, 247))
  $g.FillRectangle($brush, 0, 0, 512, 512); $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(230, 255, 210, 122))), 250, 110, 170, 170)
  $g.Dispose(); $cover.Save($path); $cover.Dispose()
}

# M41a: FakePlayer (tools\vm\FakePlayer) puts "Midnight City - M83" into Windows' media controls; its log lists the buttons pressed.
$fakeLog = 'C:\NeoFencesTest\fakeplayer.log'
function StartFakePlayer {
  Remove-Item -LiteralPath $fakeLog -ErrorAction SilentlyContinue
  $cover = 'C:\NeoFencesTest\cover.png'; CoverImage $cover
  Start-Process (Join-Path $run 'fakeplayer\FakePlayer.exe') -ArgumentList $fakeLog, $cover
  [void](Wait { (Test-Path -LiteralPath $fakeLog) -and @(Get-Content -LiteralPath $fakeLog) -contains 'ready' } 30)
}
function FakePressed([string]$button) { (Test-Path -LiteralPath $fakeLog) -and @(Get-Content -LiteralPath $fakeLog) -contains $button }
function Invoke($element) { $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 800 }

# M41a: two checks shared by the live checks and the release-candidate pass (guest-checks.ps1).
function OneClickOpen { # a folder item in the first fence opens with one click
  StopNeoFences; CloseApps # nothing from the checks before may cover the fence
  $config = Get-Content -LiteralPath (Join-Path $data 'config.json') -Raw | ConvertFrom-Json; $fenceId = $config.fences[0].id
  $folder = 'C:\NeoFencesTest\OneClick'; New-Item -ItemType Directory -Force $folder | Out-Null
  $items = [ordered]@{ schema = 1; fences = @{ $fenceId = @(@{ id = 'oneclick'; target = $folder }) } }
  $items | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $data 'items.json') -Encoding UTF8
  StartNeoFences
  $item = Element 'OneClick' ([System.Windows.Automation.ControlType]::ListItem); if (-not $item) { throw 'the item is not in the fence' }
  $x, $y = Center $item; LeftClick $x $y
  $opened = Wait { [bool](WindowLike '*OneClick*') } 15; Shot 'one-click-opened'
  if ($opened) { [void][NfVm.W]::SetForegroundWindow((WindowLike '*OneClick*')); Keys @(0x12, 0x73) } # Alt+F4
  "one click opened the folder: $opened"; $opened
}
function HiddenDesktopPassthrough { # ADR-066: hidden by a double-click, the desktop's right-click menu is Windows' own
  $x, $y = EmptyWallpaper; $before = @(LogText | Select-String -SimpleMatch 'quick-hide: True').Count
  DoubleClick $x $y; $hidden = @(LogText | Select-String -SimpleMatch 'quick-hide: True').Count -gt $before
  RightClick $x $y; $menu = DesktopMenuShown; Shot 'hidden-desktop-menu'; Keys @(0x1B); Start-Sleep -Milliseconds 600
  DoubleClick $x $y; $back = @(Fences).Count -gt 0
  RightClick $x $y; $shownMenu = DesktopMenuShown; Keys @(0x1B)
  "hidden: $hidden; Explorer's menu while hidden: $menu; fences back: $back; menu with fences shown (replayed): $shownMenu"
  $hidden -and $menu -and $back -and $shownMenu
}
# M41b: buttons by name, a real click (WPF's ToggleButton fires Click only for the mouse), and the fence menu's entries.
function Button([string]$name) { Element $name ([System.Windows.Automation.ControlType]::Button) }
function ClickOn($element) { if (-not $element) { return $false }; $x, $y = Center $element; LeftClick $x $y; $true }
function OpenFromFenceMenu([string]$entryName) { # empty fence space, else the title; a Guided menu entry or a Compact icon-row button
  $fence = @(Fences | Sort-Object { (Rect $_).Left })[0]; $rect = Rect $fence
  foreach ($y in @(($rect.Bottom - 12), ($rect.Top + 14))) {
    RightClick ([int](($rect.Left + $rect.Right) / 2)) $y
    $entry = Element $entryName ([System.Windows.Automation.ControlType]::MenuItem)
    if (-not $entry) { $entry = Element $entryName ([System.Windows.Automation.ControlType]::Button) }
    if ($entry) { $entry.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Seconds 2; return $true }
    Keys @(0x1B); Start-Sleep -Milliseconds 500
  }
  $false
}
function ItemHeight([string]$name) { $item = Element $name ([System.Windows.Automation.ControlType]::ListItem); if ($item) { [int]$item.Current.BoundingRectangle.Height } else { 0 } }
