# Inside a NeoFences test VM (M41a, spec 2026-10-11-m41a-feel-design section 6): screenshots of the widgets - a fence with
# Clock, Calendar, System stats and Now playing in their sizes, a floating board, a sound playing in Media Player, the hover
# handles, a widget's back and the gallery - for the owner to judge. Dot-sourced by guest-checks.ps1 (Invoke-VmChecks.ps1
# -Live -Script probe-widgets.ps1). Windows PowerShell 5.1. ASCII only.
. (Join-Path $root 'live-helpers.ps1')
$exe = Join-Path $run 'build\NeoFences.exe' # this checkout's build, not an installed one
$withData = Test-Path -LiteralPath (Join-Path $run 'data')

function Widget([string]$id, [string]$kind, [int]$columns, [int]$rows, $options = $null) {
  $item = [ordered]@{ id = $id; target = "neofences:widget/$kind"; size = @{ columns = $columns; rows = $rows } }
  if ($options) { $item.widget = $options }
  $item
}

# A quiet ninety-second tone as a WAV for the player page below.
function ToneFile([string]$path) {
  $rate = 8000; $seconds = 90; $samples = $rate * $seconds
  $stream = [IO.File]::Create($path); $writer = New-Object IO.BinaryWriter $stream
  $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $writer.Write([int](36 + $samples * 2)); $writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
  $writer.Write([int]16); $writer.Write([int16]1); $writer.Write([int16]1); $writer.Write([int]$rate); $writer.Write([int]($rate * 2)); $writer.Write([int16]2); $writer.Write([int16]16)
  $writer.Write([Text.Encoding]::ASCII.GetBytes('data')); $writer.Write([int]($samples * 2))
  $tone = New-Object byte[] ($samples * 2)
  for ($n = 0; $n -lt $samples; $n++) { $value = [int16](600 * [math]::Sin(2 * [math]::PI * 220 * $n / $rate)); $tone[2 * $n] = $value -band 0xFF; $tone[2 * $n + 1] = ($value -shr 8) -band 0xFF }
  $writer.Write($tone); $writer.Close()
}

# A song for Windows' media controls: Edge plays the tone from a page that names it (title, artist, album) and gives it a
# cover through the Media Session API - the way Spotify or YouTube Music show in Windows' media flyout.
function Player {
  $folder = 'C:\NeoFencesTest\player'; New-Item -ItemType Directory -Force $folder | Out-Null
  ToneFile (Join-Path $folder 'tone.wav')
  $cover = New-Object System.Drawing.Bitmap 512, 512; $g = [System.Drawing.Graphics]::FromImage($cover)
  $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point 512, 512), ([System.Drawing.Color]::FromArgb(255, 255, 106, 61)), ([System.Drawing.Color]::FromArgb(255, 123, 47, 247))
  $g.FillRectangle($brush, 0, 0, 512, 512); $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(230, 255, 210, 122))), 250, 110, 170, 170)
  $g.Dispose(); $cover.Save((Join-Path $folder 'cover.png')); $cover.Dispose()
  $page = @'
<!doctype html><html><body style="background:#111;color:#eee;font:16px sans-serif">
<audio id="a" src="tone.wav" autoplay loop controls></audio>
<script>
navigator.mediaSession.metadata = new MediaMetadata({ title: 'Midnight City', artist: 'M83', album: 'Hurry Up, We Are Dreaming',
  artwork: [{ src: 'cover.png', sizes: '512x512', type: 'image/png' }] });
document.getElementById('a').play();
</script></body></html>
'@
  Set-Content -LiteralPath (Join-Path $folder 'player.html') -Value $page -Encoding ASCII
  $edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
  Start-Process $edge -ArgumentList "--app=file:///C:/NeoFencesTest/player/player.html", '--autoplay-policy=no-user-gesture-required', '--no-first-run', "--user-data-dir=$env:TEMP\nf-player", '--window-size=400,200', '--window-position=1200,900'
  Start-Sleep -Seconds 6
}

Check 'start' { StartNeoFences; StopNeoFences; Test-Path -LiteralPath (Join-Path $data 'config.json') }
Check 'widget-fences' {
  $config = Get-Content -LiteralPath (Join-Path $data 'config.json') -Raw | ConvertFrom-Json
  $fingerprint = $config.lastLayoutFingerprint
  $monitor = @($config.layouts.$fingerprint.fences.PSObject.Properties)[0].Value.monitor
  $config.fences = @(
    @{ id = 'desk'; title = 'Desk'; tabColor = 'blue'; look = @{ theme = 'aurora'; titleStyle = 'clean'; strength = 35 } },
    @{ id = 'board'; title = 'Board'; widgetsFloat = $true })
  $config.layouts.$fingerprint.fences = @{
    desk = @{ monitor = $monitor; x = 12; y = 12; w = 470; h = 520 }; board = @{ monitor = $monitor; x = 500; y = 12; w = 500; h = 420 } }
  $config | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath (Join-Path $data 'config.json') -Encoding UTF8
  $items = @{
    desk = @((Widget 'clock' 'clock' 2 1), (Widget 'calendar' 'date' 2 2), (Widget 'stats' 'stats' 2 2), (Widget 'music' 'nowplaying' 4 2))
    board = @((Widget 'bigclock' 'clock' 4 2 @{ date = $true; cities = @('Europe/London', 'Asia/Tokyo') }), (Widget 'month' 'date' 4 2), (Widget 'pill' 'nowplaying' 2 1)) }
  [ordered]@{ schema = 1; fences = $items } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $data 'items.json') -Encoding UTF8
  Player; StartNeoFences; Start-Sleep -Seconds 10; Shot 'widgets'
  "fences: $(@(Fences).Count)"; @(Fences).Count -eq 2
}
Check 'later' { Start-Sleep -Seconds 4; Shot 'widgets-later'; $true } # the record turned, the time ran on
Check 'handles' {
  $desk = @(Fences | Where-Object { (Rect $_).Left -lt 300 })[0]; $rect = Rect $desk
  Pointer ($rect.Left + 120) ($rect.Top + 120); Start-Sleep -Milliseconds 500; Shot 'widgets-hover'; $true
}
Check 'back' {
  $desk = @(Fences | Where-Object { (Rect $_).Left -lt 300 })[0]; $rect = Rect $desk
  RightClick ($rect.Left + 80) ($rect.Top + 60)
  $entry = Element "Widget settings$([char]0x2026)" ([System.Windows.Automation.ControlType]::MenuItem)
  if (-not $entry) { Keys @(0x1B); 'no menu entry'; $false; return }
  $entry.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Seconds 1; Shot 'widget-back'
  Keys @(0x1B); LeftClick (EmptyWallpaper)[0] (EmptyWallpaper)[1]; $true
}
Check 'gallery' {
  $desk = @(Fences | Where-Object { (Rect $_).Left -lt 300 })[0]; $rect = Rect $desk
  Pointer ($rect.Left + 200) ($rect.Top + 14); Start-Sleep -Milliseconds 600
  Pointer ($rect.Left + 200) ($rect.Top + 60); Start-Sleep -Milliseconds 300; Pointer ($rect.Left + 200) ($rect.Top + 14); Start-Sleep -Milliseconds 400
  $plus = Element 'Add a widget' ([System.Windows.Automation.ControlType]::Button)
  if (-not $plus) { 'no + on the title'; $false; return }
  $x, $y = Center $plus; LeftClick $x $y; Start-Sleep -Seconds 1; Shot 'gallery' # a real click, as a person does
  $title = Element 'Add a widget' ([System.Windows.Automation.ControlType]::Text)
  $where = if ($title) { "$($title.Current.BoundingRectangle), offscreen: $($title.Current.IsOffscreen)" } else { 'none' }
  LeftClick (EmptyWallpaper)[0] (EmptyWallpaper)[1]
  "gallery title at: $where"; [bool]$title
}
