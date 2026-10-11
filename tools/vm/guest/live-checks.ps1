# Inside a NeoFences test VM (M38.1): the live checks of a branch build, dot-sourced by guest-checks.ps1 (its helpers,
# Check, Shot and the report). The build is a self-contained publish in run\build; run\data, when there, is a copy of a
# NeoFences data folder to start from (else a fresh start). Peek and the keyboard (ADR-060). Windows PowerShell 5.1. ASCII only.
. (Join-Path $root 'live-helpers.ps1')
$exe = Join-Path $run 'build\NeoFences.exe' # this checkout's build, not an installed one
$withData = Test-Path -LiteralPath (Join-Path $run 'data')
"machine: $($settings.machine); live checks of $exe; data: $(if ($withData) { 'a copy' } else { 'fresh start' })"
Check 'start' {
  if ($withData) {
    Copy-Item -LiteralPath (Join-Path $run 'data') -Destination $data -Recurse -Force
    Remove-Item -LiteralPath (Join-Path $data 'logs') -Recurse -Force -ErrorAction SilentlyContinue # the copy's logs would muddle LogText
  }
  StartNeoFences
  "fences: $(@(Fences).Count)"; @(Fences).Count -gt 0
}
Check 'peek-keyboard' {
  $notepad = Notepad; Taskbar; Peek
  $inFence = IsFence (Fg); Shot 'peek-keyboard-on'
  Keys @(0x27); Keys @(0x28); Shot 'peek-arrows'
  Keys @(0x1B); Start-Sleep -Seconds 1; $back = (Fg) -eq $notepad
  "keyboard in a fence: $inFence; back to Notepad after Esc: $back"; $inFence -and $back
}
Check 'under-mouse' {
  $target = @(Fences)[-1]; $rect = Rect $target; $notepad = Notepad
  Pointer ([int](($rect.Left + $rect.Right) / 2)) ([int](($rect.Top + $rect.Bottom) / 2)); Peek
  $got = (Fg) -eq $target; Keys @(0x1B); Start-Sleep -Seconds 1
  "keyboard in the fence under the mouse: $got"; $got
}
Check 'tab-walk' {
  $count = @(Fences).Count; $notepad = Notepad; Taskbar; Peek
  $walk = @((Fg).ToInt64()); foreach ($step in 1..$count) { Keys @(0x09); $walk += (Fg).ToInt64() }
  Keys @(0x10, 0x09); $backOne = (Fg).ToInt64(); Shot 'tab-walk'; Keys @(0x1B)
  $distinct = @($walk | Select-Object -First $count | Sort-Object -Unique).Count
  $allFences = @($walk | Where-Object { -not (IsFence ([IntPtr]$_)) }).Count -eq 0
  "fences: $count; visited: $distinct; wrapped: $($walk[0] -eq $walk[-1]); Shift+Tab back one: $($backOne -eq $walk[-2])"
  $allFences -and $distinct -eq $count -and $walk[0] -eq $walk[-1] -and $backOne -eq $walk[-2]
}
if ($withData) { # a fresh start has no items to open Properties for
  Check 'esc-properties' {
    $notepad = Notepad; Taskbar; Peek; $before = PeekEnds; $dialog = [IntPtr]::Zero
    foreach ($try in 1..@(Fences).Count) { # a widget has no Properties: the next fence
      Keys @(0x71); Start-Sleep -Seconds 1
      if (-not (IsFence (Fg)) -and (Fg) -ne $notepad) { $dialog = Fg; $title = [NfVm.W]::Title($dialog); break }
      Keys @(0x09)
    }
    $opened = $dialog -ne [IntPtr]::Zero; if ($opened) { Shot 'esc-properties-dialog' }
    Keys @(0x1B); Start-Sleep -Seconds 1; $inFence = IsFence (Fg); $still = (PeekEnds) -eq $before
    Keys @(0x1B); Start-Sleep -Seconds 1; $back = (Fg) -eq $notepad
    "Properties opened: $opened ($title); after Esc back in the fence: $inFence, still peeking: $still; after the second Esc back to Notepad: $back"
    $opened -and $inFence -and $still -and $back
  }
}
Check 'esc-other-app' {
  $notepad = Notepad; Taskbar; Peek; $before = PeekEnds
  if (-not (WindowLike '*Paint')) { Start-Process mspaint; [void](Wait { [bool](WindowLike '*Paint') } 30) }
  $paint = WindowLike '*Paint'; if (-not $paint) { throw 'Paint did not open' }
  Front $paint; Keys @(0x1B); Start-Sleep -Seconds 1
  $kept = (Fg) -eq $paint; $ended = (PeekEnds) -gt $before
  Get-Process mspaint -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  "Paint kept the keyboard: $kept; Peek ended: $ended"; $kept -and $ended
}
Check 'game-ends-peek' {
  $edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
  if (-not (Test-Path -LiteralPath $edge)) { 'no Edge'; $false; return }
  $notepad = Notepad; Taskbar; Peek; $before = PeekEnds; $gameBefore = @(LogText | Select-String -SimpleMatch 'game mode: True').Count; $since = Get-Date
  Start-Process $edge -ArgumentList '--kiosk', 'about:blank', '--edge-kiosk-type=fullscreen', '--no-first-run', "--user-data-dir=$env:TEMP\nf-edge"
  $on = Wait { @(LogText | Select-String -SimpleMatch 'game mode: True').Count -gt $gameBefore } 25
  $windows = Notifications
  Get-Process msedge -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -ge $since } | Stop-Process -Force -ErrorAction SilentlyContinue
  Start-Sleep -Seconds 2
  "game mode on: $on (Windows reports: $windows); Peek ended: $((PeekEnds) -gt $before)"; $on -and (PeekEnds) -gt $before
}
# ---------- M41a (spec 2026-10-11-m41a-feel-design sections 4, 5, 7) ----------
Check 'one-click-open' { OneClickOpen }
Check 'ctrl-click-selects' {
  $item = Element 'OneClick' ([System.Windows.Automation.ControlType]::ListItem); $x, $y = Center $item
  [NfVm.W]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero); LeftClick $x $y; [NfVm.W]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero)
  Start-Sleep -Seconds 3; $opened = [bool](WindowLike '*OneClick*'); $selected = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected
  Shot 'ctrl-click-selected'; LeftClick (EmptyWallpaper)[0] (EmptyWallpaper)[1]
  "Ctrl+click opened nothing: $(-not $opened); selected: $selected"; (-not $opened) -and $selected
}
Check 'menu-guided' {
  $fence = @(Fences)[0]; $rect = Rect $fence; RightClick ([int](($rect.Left + $rect.Right) / 2)) ($rect.Bottom - 12)
  Shot 'menu-guided'; $heading = [bool](Element 'This fence' ([System.Windows.Automation.ControlType]::MenuItem)); Keys @(0x1B)
  "Guided headings: $heading"; $heading
}
Check 'menu-compact' {
  StopNeoFences; SetConfig 'fenceMenu' 'compact'; StartNeoFences
  $fence = @(Fences)[0]; $rect = Rect $fence; RightClick ([int](($rect.Left + $rect.Right) / 2)) ($rect.Bottom - 12)
  Shot 'menu-compact'; $row = [bool](Element "Fence settings$([char]0x2026)" ([System.Windows.Automation.ControlType]::Button)); Keys @(0x1B)
  "Compact icon row: $row"; $row
}
Check 'hidden-desktop-passthrough' { HiddenDesktopPassthrough }
Check 'reveal' { # Click wallpaper to reveal desktop: Notepad steps aside, a second click brings it back
  StopNeoFences; SetConfig 'revealGesture' $true; StartNeoFences
  $notepad = Notepad; $x, $y = EmptyWallpaper; LeftClick $x $y; Start-Sleep -Seconds 2
  $aside = [NfVm.W]::IsIconic($notepad) -or -not [NfVm.W]::IsWindowVisible($notepad); Shot 'reveal-aside'
  LeftClick $x $y; Start-Sleep -Seconds 2; $back = -not [NfVm.W]::IsIconic($notepad) -and [NfVm.W]::IsWindowVisible($notepad)
  StopNeoFences; SetConfig 'revealGesture' $false; StartNeoFences
  "Notepad aside after a click: $aside; back after a second click: $back"; $aside -and $back
}
Check 'now-playing' { # spec section 6.6: a song in Windows' media controls shows in the widget; its buttons reach the player
  StartFakePlayer; $ready = FakePressed 'ready'
  StopNeoFences; CloseApps
  $config = Get-Content -LiteralPath (Join-Path $data 'config.json') -Raw | ConvertFrom-Json; $fenceId = $config.fences[0].id
  $items = [ordered]@{ schema = 1; fences = @{ $fenceId = @(@{ id = 'music'; target = 'neofences:widget/nowplaying'; size = @{ columns = 4; rows = 2 } }) } }
  $items | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $data 'items.json') -Encoding UTF8
  StartNeoFences
  $shown = Wait { [bool](Element 'Midnight City' ([System.Windows.Automation.ControlType]::Text)) } 20; Start-Sleep -Seconds 2; Shot 'now-playing'
  $pause = Element 'Play or pause' ([System.Windows.Automation.ControlType]::Button); if ($pause) { Invoke $pause }
  $paused = Wait { FakePressed 'Pause' } 10; Start-Sleep -Seconds 2; Shot 'now-playing-paused'
  $next = Element 'Next' ([System.Windows.Automation.ControlType]::Button); if ($next) { Invoke $next }
  $nexted = (Wait { FakePressed 'Next' } 10) -and (Wait { [bool](Element 'Wait' ([System.Windows.Automation.ControlType]::Text)) } 10); Shot 'now-playing-next'
  Get-Process FakePlayer -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  "player ready: $ready; song shown: $shown; pause reached the player: $paused; next reached it and the next song shows: $nexted"
  $ready -and $shown -and $paused -and $nexted
}
Check 'rolled-up-click' { # last: every fence rolled up, opening on click (review I4)
  StopNeoFences
  $path = Join-Path $data 'config.json'; $config = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
  $config.settings | Add-Member -NotePropertyName rollupExpand -NotePropertyValue 'click' -Force
  foreach ($fence in $config.fences) { $fence | Add-Member -NotePropertyName rolledUp -NotePropertyValue $true -Force }
  $config | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $path -Encoding UTF8
  StartNeoFences
  $notepad = Notepad; Taskbar; Peek; $fence = Fg; Start-Sleep -Milliseconds 800
  $open = Height $fence; Shot 'rolled-up-open'
  Keys @(0x1B); Start-Sleep -Seconds 2; $closed = Height $fence
  "keyboard in a fence: $(IsFence $fence); height while peeking $open, after Esc $closed"; (IsFence $fence) -and $open -gt $closed + 40
}
