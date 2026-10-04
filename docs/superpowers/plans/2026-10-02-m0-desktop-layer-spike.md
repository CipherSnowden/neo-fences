# M0 — Desktop Layer Spike Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove or disprove, on the real Windows 11 24H2 dev PC with Wallpaper Engine, every desktop-integration assumption in spec §4.1–4.5 and §4.7, and record a GO/NO-GO per assumption.

**Architecture:** A throwaway WPF "lab" app in `spikes/M0.DesktopLayer/`. A Lab window holds buttons that switch experiments on and off and a live log, and one acrylic "spike fence" window is the subject of the experiments. Pure logic (gesture detection, restart throttle, fullscreen check) is unit-tested in `spikes/M0.DesktopLayer.Tests/`. Win32/COM behaviour is verified manually against `docs/TEST-CHECKLIST.md`, with the results written to `docs/research/desktop-layer.md`. Nothing in `src/` may reference spike code; M2 re-implements the winners properly in `NeoFences.Shell`.

**Tech Stack:** .NET 10 SDK 10.0.401, WPF, `Microsoft.Windows.CsWin32` 0.3.335, xUnit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-10-02-neofences-v1-design.md` (§4 Desktop integration, §7 Reliability rules, §9 M0 exit criteria). Also read `CLAUDE.md`, `docs/DECISIONS.md` (ADR-003, ADR-005, ADR-007).

**Pre-verified:** the final state of Tasks 1–7 (every file, with `App.xaml.cs` version 5) was compiled together on 2026-10-02 with 0 errors and 0 warnings, and the 18 unit tests pass. The intermediate `App.xaml.cs` versions 1–4 are strict subsets of version 5 and were not compiled separately; if one fails to build, compare it with version 5. CsWin32 gotchas already handled:
- `PlatformTarget=x64` is required, because `GetWindowLongPtr`, `IShellBrowser` and `IShellView` are only generated for a specific CPU.
- Generated types are `internal`, so members that expose `HWND` are `internal`.
- `IServiceProvider` is ambiguous with `System.IServiceProvider`; an alias resolves it.
- `VARIANT` parameters are `object`.

## Global Constraints

- Target: Windows 10/11 x64 only; `<TargetFramework>net10.0-windows</TargetFramework>`, `<PlatformTarget>x64</PlatformTarget>`.
- No DLL injection, no in-process global hooks; only out-of-context WinEvent hooks and `WH_MOUSE_LL` (ADR-007, CLAUDE.md rule 3).
- Win32 via CsWin32 `NativeMethods.txt`; the only hand-written `DllImport` is the undocumented `SetWindowCompositionAttribute`, marked with a `ponytail:` comment (CLAUDE.md rule 5).
- Native desktop icons must always come back: clean exit, crash, kill, Explorer restart, sign-out (CLAUDE.md rule 2).
- Spike code lives only in `spikes/`, is labeled throwaway, and is never referenced from `src/`.
- Commits: single line, Conventional Commits, past tense (`feat: added …`), **no** `Co-Authored-By` trailer, one logical change per commit.
- Runtime data of the spike: `%LOCALAPPDATA%\NeoFences\spike-m0\` (`lab.log`, `restarts.txt`, `clean-shutdown-<pid>`).
- Steps marked **[USER]** need the human at the keyboard (visual judgement, real keypresses). Everything else the agent can do itself.

## Review Focus

The five conditions most likely to bite a real user that unit tests cannot exercise. Each one has a checklist row owned by a task:

1. **Sign-out or shutdown while icons are hidden.** The watchdog dies with the session and Explorer persists `FWF_NOICONS`, so the user could log back in to an empty desktop. Expected: icons visible after the next sign-in. Owned by Task 6 (`SessionEnding` handler, checklist C8).
2. **Explorer restart while icons are hidden or the fence is owned by Progman.** Expected: icons re-hidden within ~5 s, fence still alive under the default strategy. Owned by Tasks 5–6 (checklist B7, C7).
3. **Elevated foreground window** (Task Manager run as admin). UIPI may block the hook or z-order. Expected: Win+D still keeps the fence visible and desktop gestures still fire. Owned by Tasks 5 and 7 (checklist B8, D7).
4. **Second monitor.** Win+D, double-click and right-drag on a non-primary monitor's desktop must behave like on the primary. Owned by Tasks 5 and 7 (checklist F1, F2; N/A on a single-monitor PC).
5. **Auto-hide taskbar with a maximized browser** looks like it covers the whole monitor. Expected: not flagged as a game, thanks to the `WS_CAPTION` guard. Owned by Task 7 (checklist E4).

---

## File map

| File | Responsibility | Task |
|---|---|---|
| `.gitignore` | ignore `bin/`, `obj/`, `.vs/`, `*.user` | 1 |
| `spikes/README.md` | "throwaway" label + how to run | 1 |
| `spikes/M0.slnx` | spike solution | 1 |
| `spikes/M0.DesktopLayer/M0.DesktopLayer.csproj` | WPF x64 app + CsWin32 | 1 |
| `spikes/M0.DesktopLayer/NativeMethods.txt` | every Win32 name the spike uses | 1 |
| `spikes/M0.DesktopLayer/Lab.cs` | logger (window + file) | 1 |
| `spikes/M0.DesktopLayer/LabWindow.xaml(.cs)` | buttons + log + `TaskbarCreated` detection | 1 |
| `spikes/M0.DesktopLayer/App.xaml(.cs)` | wiring; grows each task | 1,4,5,6,7 |
| `spikes/M0.DesktopLayer.Tests/*.csproj` | xUnit, references the spike | 1 |
| `spikes/M0.DesktopLayer/DesktopGestureTracker.cs` | pure double-click / right-drag state machine | 2 |
| `spikes/M0.DesktopLayer/RestartThrottle.cs` | pure 3-per-10-min policy | 3 |
| `spikes/M0.DesktopLayer/GameDetector.cs` | pure `CoversMonitor` (T3) + live logging (T7) | 3,7 |
| `spikes/M0.DesktopLayer/Backdrop.cs` | 4 backdrop modes | 4 |
| `spikes/M0.DesktopLayer/SpikeFenceWindow.xaml(.cs)` | the acrylic tool window | 4 |
| `spikes/M0.DesktopLayer/DesktopWindows.cs` | class-name helpers, "is over desktop" | 5 |
| `spikes/M0.DesktopLayer/DesktopZOrder.cs` | 3 z-order strategies + foreground hook | 5 |
| `spikes/M0.DesktopLayer/DesktopIcons.cs` | `FWF_NOICONS` hide/show/query | 6 |
| `spikes/M0.DesktopLayer/Watchdog.cs` | spawn, run, clean-shutdown marker | 6 |
| `spikes/M0.DesktopLayer/MouseHookThread.cs` | `WH_MOUSE_LL` thread + S1/S2 suppression | 7 |
| `docs/TEST-CHECKLIST.md` | permanent manual test script | 1 |
| `docs/research/desktop-layer.md` | results table + conclusions | 4–8 |

## Agent helper commands (PowerShell)

Use these during manual verification. Save screenshots to your session scratchpad, then view them with the Read tool.

```powershell
# Tail the lab log
Get-Content "$env:LOCALAPPDATA\NeoFences\spike-m0\lab.log" -Tail 40

# Full-desktop screenshot (all monitors)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
$bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
$bitmap.Save("<scratchpad>\m0-shot.png"); $graphics.Dispose(); $bitmap.Dispose()

# "Show desktop" toggle (same shell command as the taskbar corner button)
(New-Object -ComObject Shell.Application).ToggleDesktop()

# Restart Explorer (Windows relaunches it automatically)
Stop-Process -Name explorer -Force

# Simulate a Task Manager kill of the lab (NOT the watchdog: pick the PID without --watchdog)
Get-CimInstance Win32_Process -Filter "Name='M0.DesktopLayer.exe'" | Select-Object ProcessId, CommandLine
Stop-Process -Id <labPid> -Force
```

---

### Task 1: Scaffold the spike, lab window and test checklist

**Files:**
- Modify: `docs/ROADMAP.md` (claim M0)
- Create: `.gitignore`, `spikes/README.md`, `spikes/M0.slnx`
- Create: `spikes/M0.DesktopLayer/M0.DesktopLayer.csproj`, `NativeMethods.txt`, `Lab.cs`, `LabWindow.xaml`, `LabWindow.xaml.cs`, `App.xaml`, `App.xaml.cs`, `AssemblyInfo.cs` (template)
- Create: `spikes/M0.DesktopLayer.Tests/M0.DesktopLayer.Tests.csproj`
- Create: `docs/TEST-CHECKLIST.md`

**Interfaces:**
- Produces: `Lab.Log(string message)`, `Lab.DataDir`, `Lab.Logged` event; `LabWindow.AddButton(string label, Action onClick)`; `LabWindow.ExplorerRestarted` event (UI thread).

- [ ] **Step 1: Branch and claim**

```powershell
git switch -c m0-desktop-layer-spike
```
In `docs/ROADMAP.md`, change the M0 heading line to:
```
## M0 — Spike: desktop layer (GO/NO-GO) — [~] claimed by session 2026-10-02 m0-spike
```
```powershell
git add docs/ROADMAP.md
git commit -m "docs: claimed M0 desktop layer spike"
```

- [ ] **Step 2: Generate projects from templates**

```powershell
dotnet new wpf -n M0.DesktopLayer -o spikes/M0.DesktopLayer
dotnet new xunit -n M0.DesktopLayer.Tests -o spikes/M0.DesktopLayer.Tests
Remove-Item spikes/M0.DesktopLayer/MainWindow.xaml, spikes/M0.DesktopLayer/MainWindow.xaml.cs, spikes/M0.DesktopLayer.Tests/UnitTest1.cs
dotnet new sln -n M0 --format slnx -o spikes
dotnet sln spikes/M0.slnx add spikes/M0.DesktopLayer/M0.DesktopLayer.csproj spikes/M0.DesktopLayer.Tests/M0.DesktopLayer.Tests.csproj
```

- [ ] **Step 3: Replace `spikes/M0.DesktopLayer/M0.DesktopLayer.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <RootNamespace>NeoFences.Spikes.M0</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Windows.CsWin32" Version="0.3.335">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Replace `spikes/M0.DesktopLayer.Tests/M0.DesktopLayer.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\M0.DesktopLayer\M0.DesktopLayer.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

</Project>
```

- [ ] **Step 5: Create `spikes/M0.DesktopLayer/NativeMethods.txt`** (full list for all tasks; unused names cost nothing)

```
// Win32 APIs used by the M0 spike. CsWin32 generates bindings for each name.
CallNextHookEx
DefWindowProc
DwmExtendFrameIntoClientArea
DwmSetWindowAttribute
DWM_SYSTEMBACKDROP_TYPE
DWM_WINDOW_CORNER_PREFERENCE
DWMWINDOWATTRIBUTE
EVENT_SYSTEM_FOREGROUND
FindWindow
FOLDERFLAGS
GET_ANCESTOR_FLAGS
GetAncestor
GetClassName
GetCurrentThreadId
GetDoubleClickTime
GetMessage
GetModuleHandle
GetMonitorInfo
GetSystemMetrics
GetWindowLongPtr
GetWindowRect
HWND_BOTTOM
HWND_TOP
IFolderView2
IServiceProvider
IShellBrowser
IShellView
IShellWindows
MONITOR_FROM_FLAGS
MONITORINFO
MonitorFromWindow
MSLLHOOKSTRUCT
PostThreadMessage
QUERY_USER_NOTIFICATION_STATE
RegisterWindowMessage
SendInput
SET_WINDOW_POS_FLAGS
SetWindowLongPtr
SetWindowPos
SetWindowsHookEx
SetWinEventHook
SHQueryUserNotificationState
ShellWindows
SID_STopLevelBrowser
SYSTEM_METRICS_INDEX
UnhookWinEvent
WINDOW_EX_STYLE
WINDOW_LONG_PTR_INDEX
WINDOW_STYLE
WINDOWPOS
WindowFromPoint
WINDOWS_HOOK_ID
WINEVENT_OUTOFCONTEXT
WM_APP
WM_LBUTTONDOWN
WM_MOUSEMOVE
WM_NCACTIVATE
WM_QUIT
WM_RBUTTONDOWN
WM_RBUTTONUP
WM_WINDOWPOSCHANGING
```

- [ ] **Step 6: Create `spikes/M0.DesktopLayer/Lab.cs`**

```csharp
using System.IO;

namespace NeoFences.Spikes.M0;

/// <summary>Throwaway spike logger: appends to the Lab window and to %LOCALAPPDATA%\NeoFences\spike-m0\lab.log.</summary>
public static class Lab
{
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeoFences", "spike-m0");

    private static readonly Lock FileLock = new();

    public static event Action<string>? Logged;

    public static void Log(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{Environment.ProcessId}] {message}";
        lock (FileLock)
        {
            Directory.CreateDirectory(DataDir);
            File.AppendAllText(Path.Combine(DataDir, "lab.log"), line + Environment.NewLine);
        }
        Logged?.Invoke(line);
    }
}
```

- [ ] **Step 7: Create `spikes/M0.DesktopLayer/LabWindow.xaml`**

```xml
<Window x:Class="NeoFences.Spikes.M0.LabWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="NeoFences M0 Lab (throwaway spike)" Width="820" Height="600">
    <DockPanel Margin="12">
        <WrapPanel x:Name="ActionsPanel" DockPanel.Dock="Top" Margin="0,0,0,8" />
        <ListBox x:Name="LogList" FontFamily="Consolas" FontSize="12" />
    </DockPanel>
</Window>
```

- [ ] **Step 8: Create `spikes/M0.DesktopLayer/LabWindow.xaml.cs`**

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace NeoFences.Spikes.M0;

public partial class LabWindow : Window
{
    private static readonly uint TaskbarCreatedMessage = Windows.Win32.PInvoke.RegisterWindowMessage("TaskbarCreated");

    /// <summary>Raised on the UI thread when Explorer restarts (TaskbarCreated broadcast).</summary>
    public event Action? ExplorerRestarted;

    public LabWindow()
    {
        InitializeComponent();
        Lab.Logged += line => Dispatcher.BeginInvoke(() =>
        {
            LogList.Items.Add(line);
            LogList.ScrollIntoView(line);
        });
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook(OnWindowMessage);
    }

    public void AddButton(string label, Action onClick)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 4, 10, 4) };
        button.Click += (_, _) =>
        {
            try { onClick(); }
            catch (Exception failure) { Lab.Log($"'{label}' failed: {failure.GetType().Name}: {failure.Message}"); }
        };
        ActionsPanel.Children.Add(button);
    }

    private nint OnWindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)message == TaskbarCreatedMessage)
        {
            Lab.Log("TaskbarCreated received (Explorer restarted)");
            ExplorerRestarted?.Invoke();
        }
        return 0;
    }
}
```

- [ ] **Step 9: Replace `spikes/M0.DesktopLayer/App.xaml`** (no `StartupUri`; App code creates windows)

```xml
<Application x:Class="NeoFences.Spikes.M0.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnMainWindowClose">
</Application>
```

- [ ] **Step 10: Replace `spikes/M0.DesktopLayer/App.xaml.cs`** (version 1)

```csharp
using System.Windows;

namespace NeoFences.Spikes.M0;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs startupArgs)
    {
        base.OnStartup(startupArgs);
        DispatcherUnhandledException += (_, unhandled) => Lab.Log($"UNHANDLED: {unhandled.Exception}");

        var labWindow = new LabWindow();
        MainWindow = labWindow;
        labWindow.Show();
        Lab.Log($"M0 lab started. OS {Environment.OSVersion.Version}");
    }

    protected override void OnExit(ExitEventArgs exitArgs)
    {
        Lab.Log("clean exit");
        base.OnExit(exitArgs);
    }
}
```

- [ ] **Step 11: Create `.gitignore` at repo root**

```
bin/
obj/
.vs/
*.user
TestResults/
```

- [ ] **Step 12: Create `spikes/README.md`**

```markdown
# Spikes — THROWAWAY

Feasibility experiments. Never referenced from `src/`. Findings live in `docs/research/`;
the code here may be deleted once its milestone closes.

## M0.DesktopLayer
Proves spec §4.1–4.5/§4.7 (acrylic, Win+D, Explorer restart, icon hide + watchdog, mouse hook,
game detection). Plan: `docs/superpowers/plans/2026-10-02-m0-desktop-layer-spike.md`.

    dotnet test spikes/M0.slnx
    dotnet run --project spikes/M0.DesktopLayer

Log: `%LOCALAPPDATA%\NeoFences\spike-m0\lab.log`.
If desktop icons stay hidden: right-click desktop → View → Show desktop icons.
```

- [ ] **Step 13: Create `docs/TEST-CHECKLIST.md`** (permanent; later milestones extend it)

```markdown
# Manual test checklist

Shell/UI behaviour that unit tests can't cover. Run the relevant sections at the end of every
milestone; record results in that milestone's research note or the session log.
Mark each row PASS / FAIL / N/A with a short note. "Fence" = the spike fence in M0, real fences later.

## A — Backdrop (spec §4.1)
| ID | Steps | Expected |
|---|---|---|
| A1 | Static wallpaper, backdrop "DWM acrylic" | fence shows blurred wallpaper, rounded corners |
| A2 | Wallpaper Engine animated wallpaper running | blur shows the animation moving, no stutter |
| A3 | Click another app so the fence is inactive; repeat for modes DWM acrylic / keep active / accent | note which modes keep the blur while inactive |
| A4 | Drag and resize the fence by its title / edges | smooth; no black flashes |
| A5 | Look at taskbar and Alt+Tab | fence appears in neither |

## B — Z-order and Win+D (spec §4.2, §4.4)
| ID | Steps | Expected (strategy "raise on Win+D" unless stated) |
|---|---|---|
| B1 | Open Notepad over the fence | Notepad covers the fence |
| B2 | Click inside the fence while Notepad is focused | Notepad keeps focus; fence does not jump above it |
| B3 | Win+D | apps hide, fence stays visible |
| B4 | Taskbar "Show desktop" corner button | same as B3 |
| B5 | Win+D again / click an app | apps return; fence back under them |
| B6 | Win+M, then Win+Shift+M | fence never minimizes |
| B7 | `Stop-Process -Name explorer -Force` | fence survives; log shows TaskbarCreated + re-applied. Repeat with "owned by Progman": record whether the fence is destroyed |
| B8 | Run Task Manager as admin, focus it, Win+D | fence stays visible |
| B9 | Strategy "bottom only", Win+D | baseline: fence expected to disappear (confirms the problem is real) |

## C — Desktop icons and watchdog (spec §4.3, ADR-005)
| ID | Steps | Expected |
|---|---|---|
| C1 | Hide desktop icons | icons vanish; Query reports hidden=True |
| C2 | Show desktop icons | icons return |
| C3 | Hide, then close the Lab window | icons return; log "watchdog: clean shutdown" |
| C4 | Hide, then CRASH (FailFast) | icons return within ~2 s; lab restarts |
| C5 | Hide, then kill the lab PID (`Stop-Process -Force`) | same as C4 |
| C6 | Crash 4 times within 10 min | 4th time: icons return, lab stays down ("restart limit") |
| C7 | Hide, then restart Explorer | icons hidden again within ~5 s |
| C8 | Hide, then sign out and back in | icons visible after sign-in |

## D — Desktop gestures (spec §4.5, ADR-007)
| ID | Steps | Expected |
|---|---|---|
| D1 | Mouse hook start; double-click empty desktop | log `GESTURE DoubleClick` |
| D2 | Double-click inside the fence / an app / the taskbar | no gesture |
| D3 | Right-click: observe; right-drag on empty desktop | `RightDragStarted` then `RightDragCompleted`; Explorer menu appears (baseline) |
| D4 | Right-click: S1; right-drag | gestures logged; record whether Explorer's menu still appears and whether the desktop gets stuck |
| D5 | Right-click: S2; right-drag, then a plain right-click | drag: no menu. Plain click: normal desktop menu appears (replayed) |
| D6 | Any mode: right-drag starting on an app window | no gesture |
| D7 | Elevated Task Manager focused; double-click desktop | record whether the gesture fires |
| D8 | Hook running: move the mouse fast, play a game briefly | no perceptible lag |
| D9 | Mouse hook stop; double-click desktop | no gesture; log "WH_MOUSE_LL removed" |

## E — Game detection (spec §4.7)
| ID | Steps | Expected |
|---|---|---|
| E1 | Focus a borderless-fullscreen game | `gameLike=True` |
| E2 | Focus an exclusive-fullscreen game (if any) | `quns=QUNS_RUNNING_D3D_FULL_SCREEN`, `gameLike=True` |
| E3 | Focus a maximized browser | `gameLike=False` |
| E4 | Taskbar auto-hide on; focus a maximized browser | `gameLike=False` (WS_CAPTION guard) |
| E5 | Click the desktop with Wallpaper Engine running | no game check logged for Progman/WorkerW |

## F — Multi-monitor (N/A on one monitor)
| ID | Steps | Expected |
|---|---|---|
| F1 | Move the fence to monitor 2; Win+D | fence stays visible |
| F2 | Double-click / right-drag on monitor 2's desktop | gestures fire |
```

- [ ] **Step 14: Build and run the empty lab**

```powershell
dotnet build spikes/M0.slnx
dotnet run --project spikes/M0.DesktopLayer
```
Expected: build has 0 errors. The Lab window opens with an empty button bar, and its log shows `M0 lab started. OS 10.0.26…`. Close it, and `lab.log` ends with `clean exit`.

- [ ] **Step 15: Commit**

```powershell
git add .gitignore spikes docs/TEST-CHECKLIST.md
git commit -m "chore: scaffolded M0 desktop layer spike with lab window and test checklist"
```

---

### Task 2: Desktop gesture tracker (pure, test-first)

**Files:**
- Create: `spikes/M0.DesktopLayer.Tests/DesktopGestureTrackerTests.cs`
- Create: `spikes/M0.DesktopLayer/DesktopGestureTracker.cs`

**Interfaces:**
- Produces: `enum MouseAction { LeftDown, RightDown, RightUp, Move }`, `enum DesktopGesture { None, DoubleClick, RightDragStarted, RightDragCompleted }`, `sealed class DesktopGestureTracker(uint doubleClickMilliseconds, int doubleClickSlopPixels, int dragThresholdPixels)` with `DesktopGesture OnMouse(MouseAction action, int x, int y, uint timeMilliseconds, bool overDesktop)` and `(int X, int Y)? DragStart`.

- [ ] **Step 1: Write the failing tests** — `spikes/M0.DesktopLayer.Tests/DesktopGestureTrackerTests.cs`

```csharp
using NeoFences.Spikes.M0;

namespace M0.DesktopLayer.Tests;

public class DesktopGestureTrackerTests
{
    private static DesktopGestureTracker NewTracker() =>
        new(doubleClickMilliseconds: 500, doubleClickSlopPixels: 2, dragThresholdPixels: 8);

    [Fact]
    public void TwoLeftDownsOnDesktopWithinTimeAndSlop_IsDoubleClick()
    {
        var tracker = NewTracker();
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_000, overDesktop: true));
        Assert.Equal(DesktopGesture.DoubleClick, tracker.OnMouse(MouseAction.LeftDown, 101, 99, 1_300, overDesktop: true));
    }

    [Fact]
    public void SecondClickTooLate_IsNotDoubleClick()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_000, overDesktop: true);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_501, overDesktop: true));
    }

    [Fact]
    public void SecondClickTooFar_IsNotDoubleClick()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_000, overDesktop: true);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.LeftDown, 110, 100, 1_100, overDesktop: true));
    }

    [Fact]
    public void ClicksNotOverDesktop_AreIgnored()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_000, overDesktop: false);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_100, overDesktop: true));
    }

    [Fact]
    public void FirstClickOnAppThenDesktop_IsNotDoubleClick()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_000, overDesktop: true);
        tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_100, overDesktop: false);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_200, overDesktop: true));
    }

    [Fact]
    public void TripleClick_YieldsOneDoubleClickOnly()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_000, overDesktop: true);
        Assert.Equal(DesktopGesture.DoubleClick, tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_100, overDesktop: true));
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_200, overDesktop: true));
    }

    [Fact]
    public void DoubleClickAcrossTickCountWrap_IsDetected()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.LeftDown, 100, 100, uint.MaxValue - 100, overDesktop: true);
        Assert.Equal(DesktopGesture.DoubleClick, tracker.OnMouse(MouseAction.LeftDown, 100, 100, 100, overDesktop: true));
    }

    [Fact]
    public void RightDragBeyondThreshold_StartsOnceThenCompletes()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.RightDown, 200, 200, 1_000, overDesktop: true);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.Move, 205, 203, 1_010, overDesktop: false));
        Assert.Equal(DesktopGesture.RightDragStarted, tracker.OnMouse(MouseAction.Move, 208, 200, 1_020, overDesktop: false));
        Assert.Equal((200, 200), tracker.DragStart);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.Move, 300, 300, 1_030, overDesktop: false));
        Assert.Equal(DesktopGesture.RightDragCompleted, tracker.OnMouse(MouseAction.RightUp, 300, 300, 1_040, overDesktop: true));
        Assert.Null(tracker.DragStart);
    }

    [Fact]
    public void RightClickWithoutMovement_CompletesNothing()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.RightDown, 200, 200, 1_000, overDesktop: true);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.RightUp, 201, 200, 1_050, overDesktop: true));
    }

    [Fact]
    public void RightDragStartedOverApp_IsIgnored()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.RightDown, 200, 200, 1_000, overDesktop: false);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.Move, 400, 400, 1_010, overDesktop: false));
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.RightUp, 400, 400, 1_020, overDesktop: false));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test spikes/M0.slnx`
Expected: build FAILS with `CS0246: The type or namespace name 'DesktopGestureTracker' could not be found`.

- [ ] **Step 3: Implement** — `spikes/M0.DesktopLayer/DesktopGestureTracker.cs`

```csharp
namespace NeoFences.Spikes.M0;

public enum MouseAction { LeftDown, RightDown, RightUp, Move }

public enum DesktopGesture { None, DoubleClick, RightDragStarted, RightDragCompleted }

/// <summary>
/// Pure state machine turning raw low-level mouse events into desktop gestures.
/// Low-level hooks never see WM_LBUTTONDBLCLK, so double-clicks are rebuilt from two LeftDowns.
/// </summary>
public sealed class DesktopGestureTracker(uint doubleClickMilliseconds, int doubleClickSlopPixels, int dragThresholdPixels)
{
    private (int X, int Y, uint Time)? _lastLeftDown;
    private (int X, int Y)? _rightDownAt;
    private bool _rightDragging;

    public (int X, int Y)? DragStart => _rightDownAt;

    public DesktopGesture OnMouse(MouseAction action, int x, int y, uint timeMilliseconds, bool overDesktop)
    {
        switch (action)
        {
            case MouseAction.LeftDown:
                if (!overDesktop)
                {
                    _lastLeftDown = null;
                    return DesktopGesture.None;
                }
                if (_lastLeftDown is { } previous
                    && unchecked(timeMilliseconds - previous.Time) <= doubleClickMilliseconds
                    && Math.Abs(x - previous.X) <= doubleClickSlopPixels
                    && Math.Abs(y - previous.Y) <= doubleClickSlopPixels)
                {
                    _lastLeftDown = null;
                    return DesktopGesture.DoubleClick;
                }
                _lastLeftDown = (x, y, timeMilliseconds);
                return DesktopGesture.None;

            case MouseAction.RightDown:
                _rightDownAt = overDesktop ? (x, y) : null;
                _rightDragging = false;
                return DesktopGesture.None;

            case MouseAction.Move:
                if (_rightDownAt is { } start && !_rightDragging
                    && (Math.Abs(x - start.X) >= dragThresholdPixels || Math.Abs(y - start.Y) >= dragThresholdPixels))
                {
                    _rightDragging = true;
                    return DesktopGesture.RightDragStarted;
                }
                return DesktopGesture.None;

            case MouseAction.RightUp:
                var completedDrag = _rightDragging;
                _rightDownAt = null;
                _rightDragging = false;
                return completedDrag ? DesktopGesture.RightDragCompleted : DesktopGesture.None;

            default:
                return DesktopGesture.None;
        }
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test spikes/M0.slnx`
Expected: `Passed! - Failed: 0, Passed: 10`

- [ ] **Step 5: Commit**

```powershell
git add spikes/M0.DesktopLayer/DesktopGestureTracker.cs spikes/M0.DesktopLayer.Tests/DesktopGestureTrackerTests.cs
git commit -m "feat: added desktop gesture tracker to M0 spike"
```

---

### Task 3: Restart throttle and fullscreen check (pure, test-first)

**Files:**
- Create: `spikes/M0.DesktopLayer.Tests/RestartThrottleTests.cs`, `spikes/M0.DesktopLayer.Tests/GameDetectorTests.cs`
- Create: `spikes/M0.DesktopLayer/RestartThrottle.cs`, `spikes/M0.DesktopLayer/GameDetector.cs` (pure version; Task 7 replaces it)

**Interfaces:**
- Produces: `RestartThrottle.ShouldRestart(IReadOnlyList<DateTimeOffset> recentRestarts, DateTimeOffset now, int maxRestarts = 3, TimeSpan? window = null) -> bool`; `record struct ScreenRect(int Left, int Top, int Right, int Bottom)`; `GameDetector.CoversMonitor(ScreenRect window, ScreenRect monitor) -> bool`.

- [ ] **Step 1: Write the failing tests**

`spikes/M0.DesktopLayer.Tests/RestartThrottleTests.cs`:
```csharp
using NeoFences.Spikes.M0;

namespace M0.DesktopLayer.Tests;

public class RestartThrottleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NoRecentRestarts_Restarts() =>
        Assert.True(RestartThrottle.ShouldRestart(recentRestarts: [], now: Now));

    [Fact]
    public void TwoRestartsInWindow_StillRestarts() =>
        Assert.True(RestartThrottle.ShouldRestart(recentRestarts: [Now.AddMinutes(-1), Now.AddMinutes(-5)], now: Now));

    [Fact]
    public void ThreeRestartsInWindow_StaysDown() =>
        Assert.False(RestartThrottle.ShouldRestart(recentRestarts: [Now.AddMinutes(-1), Now.AddMinutes(-2), Now.AddMinutes(-9)], now: Now));

    [Fact]
    public void OldRestartsOutsideWindow_DoNotCount() =>
        Assert.True(RestartThrottle.ShouldRestart(recentRestarts: [Now.AddMinutes(-1), Now.AddMinutes(-11), Now.AddHours(-2)], now: Now));
}
```

`spikes/M0.DesktopLayer.Tests/GameDetectorTests.cs`:
```csharp
using NeoFences.Spikes.M0;

namespace M0.DesktopLayer.Tests;

public class GameDetectorTests
{
    private static readonly ScreenRect Monitor = new(0, 0, 3840, 2160);

    [Fact]
    public void ExactMonitorRect_Covers() =>
        Assert.True(GameDetector.CoversMonitor(window: Monitor, monitor: Monitor));

    [Fact]
    public void BorderlessOverhangingByPixels_Covers() =>
        Assert.True(GameDetector.CoversMonitor(window: new ScreenRect(-8, -8, 3848, 2168), monitor: Monitor));

    [Fact]
    public void MaximizedWindowAboveTaskbar_DoesNotCover() =>
        Assert.False(GameDetector.CoversMonitor(window: new ScreenRect(-8, -8, 3848, 2088), monitor: Monitor));

    [Fact]
    public void WindowOnSecondMonitor_DoesNotCoverFirst() =>
        Assert.False(GameDetector.CoversMonitor(window: new ScreenRect(3840, 0, 5760, 1080), monitor: Monitor));
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test spikes/M0.slnx`
Expected: build FAILS with `CS0103: The name 'RestartThrottle' does not exist` and `CS0246: ... 'ScreenRect' could not be found`.

- [ ] **Step 3: Implement**

`spikes/M0.DesktopLayer/RestartThrottle.cs`:
```csharp
namespace NeoFences.Spikes.M0;

/// <summary>Watchdog restart policy: at most <c>maxRestarts</c> within <c>window</c> (ADR-005: 3 per 10 min).</summary>
public static class RestartThrottle
{
    public static bool ShouldRestart(IReadOnlyList<DateTimeOffset> recentRestarts, DateTimeOffset now, int maxRestarts = 3, TimeSpan? window = null)
    {
        var span = window ?? TimeSpan.FromMinutes(10);
        return recentRestarts.Count(restartTime => now - restartTime < span) < maxRestarts;
    }
}
```

`spikes/M0.DesktopLayer/GameDetector.cs` (pure version):
```csharp
namespace NeoFences.Spikes.M0;

public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom);

public static class GameDetector
{
    /// <summary>True when the window rect covers the whole monitor rect (borderless games often overhang by a few pixels).</summary>
    public static bool CoversMonitor(ScreenRect window, ScreenRect monitor) =>
        window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test spikes/M0.slnx`
Expected: `Passed! - Failed: 0, Passed: 18`

- [ ] **Step 5: Commit**

```powershell
git add spikes/M0.DesktopLayer/RestartThrottle.cs spikes/M0.DesktopLayer/GameDetector.cs spikes/M0.DesktopLayer.Tests/RestartThrottleTests.cs spikes/M0.DesktopLayer.Tests/GameDetectorTests.cs
git commit -m "feat: added watchdog restart throttle and fullscreen check to M0 spike"
```

---

### Task 4: Acrylic spike fence window (spec §4.1)

**Files:**
- Create: `spikes/M0.DesktopLayer/Backdrop.cs`, `SpikeFenceWindow.xaml`, `SpikeFenceWindow.xaml.cs`
- Modify: `spikes/M0.DesktopLayer/App.xaml.cs` (version 2)
- Create: `docs/research/desktop-layer.md`

**Interfaces:**
- Produces: `enum BackdropMode { None, DwmAcrylic, DwmAcrylicKeepActive, AccentAcrylic }`; `Backdrop.Apply(HWND hwnd, BackdropMode mode)` (internal); `SpikeFenceWindow` with `internal HWND Handle`, `Func<bool> ForceBottom` (default `() => false`; Task 5 sets it), `SetBackdrop(BackdropMode mode)`.

- [ ] **Step 1: Create `spikes/M0.DesktopLayer/Backdrop.cs`**

```csharp
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.Controls;

namespace NeoFences.Spikes.M0;

public enum BackdropMode
{
    /// <summary>No blur; tinted only. Windows 10 fallback look.</summary>
    None,
    /// <summary>Documented DWM acrylic (DWMSBT_TRANSIENTWINDOW). Risk: may go solid while the window is inactive.</summary>
    DwmAcrylic,
    /// <summary>DWM acrylic + the window always answers WM_NCACTIVATE as active.</summary>
    DwmAcrylicKeepActive,
    /// <summary>Undocumented SetWindowCompositionAttribute acrylic (used by TranslucentTB etc.). Focus-independent.</summary>
    AccentAcrylic,
}

public static class Backdrop
{
    internal static unsafe void Apply(HWND hwnd, BackdropMode mode)
    {
        var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
        PInvoke.DwmExtendFrameIntoClientArea(hwnd, margins);

        BOOL darkMode = true;
        PInvoke.DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, &darkMode, (uint)sizeof(BOOL));
        var corners = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        PInvoke.DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &corners, (uint)sizeof(DWM_WINDOW_CORNER_PREFERENCE));

        var useDwmAcrylic = mode is BackdropMode.DwmAcrylic or BackdropMode.DwmAcrylicKeepActive;
        var backdropType = useDwmAcrylic ? DWM_SYSTEMBACKDROP_TYPE.DWMSBT_TRANSIENTWINDOW : DWM_SYSTEMBACKDROP_TYPE.DWMSBT_NONE;
        var hresult = PInvoke.DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, &backdropType, (uint)sizeof(DWM_SYSTEMBACKDROP_TYPE));

        SetAccent(hwnd, enableAcrylic: mode == BackdropMode.AccentAcrylic);
        Lab.Log($"backdrop {mode}: DWMWA_SYSTEMBACKDROP_TYPE hr=0x{hresult.Value:X8}");
    }

    // ponytail: undocumented user32 API, no CsWin32 metadata; hand-written per CLAUDE.md rule 5 exception. Spike only.
    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public nint Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);

    private const int WcaAccentPolicy = 19;
    private const int AccentDisabled = 0;
    private const int AccentEnableAcrylicBlurBehind = 4;

    private static unsafe void SetAccent(HWND hwnd, bool enableAcrylic)
    {
        var policy = new AccentPolicy
        {
            AccentState = enableAcrylic ? AccentEnableAcrylicBlurBehind : AccentDisabled,
            GradientColor = 0x99201A16, // AABBGGRR: ~60% dark tint
        };
        var data = new WindowCompositionAttributeData { Attribute = WcaAccentPolicy, Data = (nint)(&policy), SizeOfData = sizeof(AccentPolicy) };
        SetWindowCompositionAttribute((nint)hwnd.Value, ref data);
    }
}
```

- [ ] **Step 2: Create `spikes/M0.DesktopLayer/SpikeFenceWindow.xaml`**

```xml
<Window x:Class="NeoFences.Spikes.M0.SpikeFenceWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Spike fence" Width="340" Height="230" Left="120" Top="120"
        WindowStyle="None" ResizeMode="CanResize" AllowsTransparency="False"
        Background="Transparent" ShowInTaskbar="False" ShowActivated="False">
    <WindowChrome.WindowChrome>
        <WindowChrome GlassFrameThickness="-1" CaptionHeight="30" ResizeBorderThickness="6"
                      CornerRadius="0" UseAeroCaptionButtons="False" />
    </WindowChrome.WindowChrome>
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="30" />
            <RowDefinition />
        </Grid.RowDefinitions>
        <TextBlock Text="Games (spike fence)" Foreground="White" FontWeight="SemiBold" Margin="12,0" VerticalAlignment="Center" />
        <Border Grid.Row="1" BorderBrush="#40FFFFFF" BorderThickness="0,1,0,0">
            <TextBlock x:Name="StatusText" Foreground="#DDFFFFFF" Margin="12" TextWrapping="Wrap" />
        </Border>
    </Grid>
</Window>
```

- [ ] **Step 3: Create `spikes/M0.DesktopLayer/SpikeFenceWindow.xaml.cs`**

```csharp
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Spikes.M0;

public partial class SpikeFenceWindow : Window
{
    private BackdropMode _backdropMode = BackdropMode.DwmAcrylic;

    internal HWND Handle { get; private set; }

    /// <summary>When it returns true, every z-order change is forced to HWND_BOTTOM (set by DesktopZOrder).</summary>
    public Func<bool> ForceBottom { get; set; } = () => false;

    public SpikeFenceWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    public void SetBackdrop(BackdropMode mode)
    {
        _backdropMode = mode;
        Backdrop.Apply(Handle, mode);
        StatusText.Text = $"Backdrop: {mode}\nClick another window: does the blur stay?";
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        Handle = (HWND)new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(Handle);
        source.CompositionTarget.BackgroundColor = Colors.Transparent;
        source.AddHook(OnWindowMessage);

        var exStyle = PInvoke.GetWindowLongPtr(Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLongPtr(Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE,
            exStyle | (nint)(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE));
        SetBackdrop(_backdropMode);
    }

    private unsafe nint OnWindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)message == PInvoke.WM_NCACTIVATE && _backdropMode == BackdropMode.DwmAcrylicKeepActive)
        {
            handled = true;
            return PInvoke.DefWindowProc((HWND)hwnd, PInvoke.WM_NCACTIVATE, 1, lParam);
        }
        if ((uint)message == PInvoke.WM_WINDOWPOSCHANGING && ForceBottom())
        {
            var windowPos = (WINDOWPOS*)lParam;
            if (!windowPos->flags.HasFlag(SET_WINDOW_POS_FLAGS.SWP_NOZORDER))
            {
                windowPos->hwndInsertAfter = HWND.HWND_BOTTOM;
            }
        }
        return 0;
    }
}
```

- [ ] **Step 4: Replace `spikes/M0.DesktopLayer/App.xaml.cs`** (version 2: adds the fence and backdrop buttons)

```csharp
using System.Windows;

namespace NeoFences.Spikes.M0;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs startupArgs)
    {
        base.OnStartup(startupArgs);
        DispatcherUnhandledException += (_, unhandled) => Lab.Log($"UNHANDLED: {unhandled.Exception}");

        var labWindow = new LabWindow();
        MainWindow = labWindow;
        labWindow.Show();
        Lab.Log($"M0 lab started. OS {Environment.OSVersion.Version}");

        var fenceWindow = new SpikeFenceWindow();
        fenceWindow.Show();

        labWindow.AddButton(label: "Backdrop: none", onClick: () => fenceWindow.SetBackdrop(BackdropMode.None));
        labWindow.AddButton(label: "Backdrop: DWM acrylic", onClick: () => fenceWindow.SetBackdrop(BackdropMode.DwmAcrylic));
        labWindow.AddButton(label: "Backdrop: DWM acrylic + keep active", onClick: () => fenceWindow.SetBackdrop(BackdropMode.DwmAcrylicKeepActive));
        labWindow.AddButton(label: "Backdrop: accent acrylic", onClick: () => fenceWindow.SetBackdrop(BackdropMode.AccentAcrylic));
    }

    protected override void OnExit(ExitEventArgs exitArgs)
    {
        Lab.Log("clean exit");
        base.OnExit(exitArgs);
    }
}
```

- [ ] **Step 5: Build and run**

Run: `dotnet build spikes/M0.slnx` then `dotnet run --project spikes/M0.DesktopLayer`
Expected: 0 errors. A 340×230 fence with the title "Games (spike fence)" appears at (120,120), and the log shows `backdrop DwmAcrylic: DWMWA_SYSTEMBACKDROP_TYPE hr=0x00000000`.

- [ ] **Step 6: Verify checklist A1–A5** (take screenshots with the helper; A2 and A3 are **[USER]** judgements)

Cycle through the four backdrop buttons. For A3, click the Lab window after each mode so the fence is inactive, and screenshot.

- [ ] **Step 7: Create `docs/research/desktop-layer.md`** with the results so far

```markdown
# Desktop layer research (M0 spike)

Spike: `spikes/M0.DesktopLayer` (throwaway). Checklist IDs: `docs/TEST-CHECKLIST.md`.

**Machine:** Windows <winver build>, <GPU>, <monitor count + resolutions + scaling>,
Wallpaper Engine <version or "not installed">, taskbar auto-hide <on/off>.

## Results

| ID | Result | Notes (mode/strategy, what was seen, log line) |
|---|---|---|
| A1 | | |
| A2 | | |
| A3 | | which of DwmAcrylic / KeepActive / AccentAcrylic keep blur while inactive |
| A4 | | |
| A5 | | |

## Conclusions

(Written in Task 8.)
```
Fill in the machine line and the A rows with what you actually observed: PASS / FAIL / N/A plus one-line notes.

- [ ] **Step 8: Commit**

```powershell
git add spikes/M0.DesktopLayer docs/research/desktop-layer.md
git commit -m "feat: added acrylic spike fence window with backdrop modes"
```

---

### Task 5: Z-order strategies, Win+D and Explorer restart (spec §4.2, §4.4)

**Files:**
- Create: `spikes/M0.DesktopLayer/DesktopWindows.cs`, `spikes/M0.DesktopLayer/DesktopZOrder.cs`
- Modify: `spikes/M0.DesktopLayer/App.xaml.cs` (version 3), `docs/research/desktop-layer.md`

**Interfaces:**
- Consumes: `SpikeFenceWindow.Handle`, `SpikeFenceWindow.ForceBottom`, `LabWindow.ExplorerRestarted`.
- Produces: `DesktopWindows.ClassOf(HWND) -> string` (internal), `DesktopWindows.IsDesktopClass(string) -> bool`, `DesktopWindows.IsOverDesktop(System.Drawing.Point) -> bool`; `enum ZOrderStrategy { BottomOnly, RaiseOnShowDesktop, OwnedByProgman }`; `DesktopZOrder : IDisposable` with `Track(SpikeFenceWindow)`, `SetStrategy(ZOrderStrategy)`, `Reapply()`, `Strategy`, and the internal event `ForegroundChanged(HWND foreground, string className)`.

- [ ] **Step 1: Create `spikes/M0.DesktopLayer/DesktopWindows.cs`**

```csharp
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Spikes.M0;

public static class DesktopWindows
{
    internal static unsafe string ClassOf(HWND hwnd)
    {
        char* buffer = stackalloc char[256];
        var length = PInvoke.GetClassName(hwnd, buffer, 256);
        return new string(buffer, 0, length);
    }

    /// <summary>Progman (24H2+ icons host) or WorkerW (pre-24H2 icons host, wallpaper layer) — the desktop itself.</summary>
    public static bool IsDesktopClass(string className) => className is "Progman" or "WorkerW";

    public static bool IsOverDesktop(System.Drawing.Point screenPoint)
    {
        var underCursor = PInvoke.WindowFromPoint(screenPoint);
        var root = PInvoke.GetAncestor(underCursor, GET_ANCESTOR_FLAGS.GA_ROOT);
        return IsDesktopClass(ClassOf(root));
    }
}
```

- [ ] **Step 2: Create `spikes/M0.DesktopLayer/DesktopZOrder.cs`**

```csharp
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Spikes.M0;

public enum ZOrderStrategy
{
    /// <summary>Pin to HWND_BOTTOM only. Expected to vanish on Win+D (baseline).</summary>
    BottomOnly,
    /// <summary>ADR-003: pin to bottom; when the desktop becomes foreground (Win+D) raise to HWND_TOP.</summary>
    RaiseOnShowDesktop,
    /// <summary>Owner = Progman, so the fence rides above the desktop. Risk: destroyed when Explorer restarts.</summary>
    OwnedByProgman,
}

/// <summary>Spike for spec §4.1–4.2: keeps fence windows at desktop level and survives Win+D.</summary>
public sealed class DesktopZOrder : IDisposable
{
    private static readonly SET_WINDOW_POS_FLAGS ZOrderOnly =
        SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;

    private readonly WINEVENTPROC _foregroundCallback; // field keeps the delegate alive while the hook exists
    private readonly HWINEVENTHOOK _foregroundHook;
    private readonly List<SpikeFenceWindow> _fences = [];
    private bool _raisedAboveDesktop;

    public ZOrderStrategy Strategy { get; private set; } = ZOrderStrategy.RaiseOnShowDesktop;

    /// <summary>Raised on the UI thread for every foreground change, with the window class name.</summary>
    internal event Action<HWND, string>? ForegroundChanged;

    public DesktopZOrder()
    {
        _foregroundCallback = OnForegroundEvent;
        _foregroundHook = PInvoke.SetWinEventHook(PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND,
            HMODULE.Null, _foregroundCallback, 0, 0, PInvoke.WINEVENT_OUTOFCONTEXT);
        Lab.Log($"foreground WinEvent hook installed: {!_foregroundHook.IsNull}");
    }

    public void Track(SpikeFenceWindow fence)
    {
        fence.ForceBottom = () => Strategy != ZOrderStrategy.OwnedByProgman && !_raisedAboveDesktop;
        fence.Closed += (_, _) => Lab.Log("fence window CLOSED (destroyed)");
        _fences.Add(fence);
        ApplyToFence(fence);
    }

    public void SetStrategy(ZOrderStrategy strategy)
    {
        Strategy = strategy;
        _raisedAboveDesktop = false;
        _fences.ForEach(ApplyToFence);
        Lab.Log($"z-order strategy = {strategy}");
    }

    /// <summary>Call after Explorer restarts: Progman is a new window, ownership must be re-applied.</summary>
    public void Reapply() => _fences.ForEach(ApplyToFence);

    private unsafe void ApplyToFence(SpikeFenceWindow fence)
    {
        var owner = Strategy == ZOrderStrategy.OwnedByProgman ? PInvoke.FindWindow("Progman", null) : HWND.Null;
        PInvoke.SetWindowLongPtr(fence.Handle, WINDOW_LONG_PTR_INDEX.GWLP_HWNDPARENT, (nint)owner.Value);
        PInvoke.SetWindowPos(fence.Handle, HWND.HWND_BOTTOM, 0, 0, 0, 0, ZOrderOnly);
    }

    private unsafe void OnForegroundEvent(HWINEVENTHOOK hook, uint eventType, HWND foreground, int objectId, int childId, uint threadId, uint eventTime)
    {
        var className = DesktopWindows.ClassOf(foreground);
        Lab.Log($"foreground -> {className} 0x{(nint)foreground.Value:X}");
        ForegroundChanged?.Invoke(foreground, className);

        if (Strategy != ZOrderStrategy.RaiseOnShowDesktop || _fences.Any(fence => fence.Handle == foreground)) return;

        if (DesktopWindows.IsDesktopClass(className))
        {
            _raisedAboveDesktop = true;
            _fences.ForEach(fence => PInvoke.SetWindowPos(fence.Handle, HWND.HWND_TOP, 0, 0, 0, 0, ZOrderOnly));
            Lab.Log("desktop is foreground (Win+D?) -> fences raised to HWND_TOP");
        }
        else if (_raisedAboveDesktop)
        {
            _raisedAboveDesktop = false;
            _fences.ForEach(fence => PInvoke.SetWindowPos(fence.Handle, HWND.HWND_BOTTOM, 0, 0, 0, 0, ZOrderOnly));
            Lab.Log("app is foreground -> fences back to HWND_BOTTOM");
        }
    }

    public void Dispose() => PInvoke.UnhookWinEvent(_foregroundHook);
}
```

- [ ] **Step 3: Replace `spikes/M0.DesktopLayer/App.xaml.cs`** (version 3: adds z-order)

```csharp
using System.Windows;

namespace NeoFences.Spikes.M0;

public partial class App : Application
{
    private DesktopZOrder? _zOrder;

    protected override void OnStartup(StartupEventArgs startupArgs)
    {
        base.OnStartup(startupArgs);
        DispatcherUnhandledException += (_, unhandled) => Lab.Log($"UNHANDLED: {unhandled.Exception}");

        var labWindow = new LabWindow();
        MainWindow = labWindow;
        labWindow.Show();
        Lab.Log($"M0 lab started. OS {Environment.OSVersion.Version}");

        var fenceWindow = new SpikeFenceWindow();
        fenceWindow.Show();

        _zOrder = new DesktopZOrder();
        _zOrder.Track(fenceWindow);
        labWindow.ExplorerRestarted += () => _zOrder.Reapply();

        labWindow.AddButton(label: "Backdrop: none", onClick: () => fenceWindow.SetBackdrop(BackdropMode.None));
        labWindow.AddButton(label: "Backdrop: DWM acrylic", onClick: () => fenceWindow.SetBackdrop(BackdropMode.DwmAcrylic));
        labWindow.AddButton(label: "Backdrop: DWM acrylic + keep active", onClick: () => fenceWindow.SetBackdrop(BackdropMode.DwmAcrylicKeepActive));
        labWindow.AddButton(label: "Backdrop: accent acrylic", onClick: () => fenceWindow.SetBackdrop(BackdropMode.AccentAcrylic));

        labWindow.AddButton(label: "Z: bottom only", onClick: () => _zOrder.SetStrategy(ZOrderStrategy.BottomOnly));
        labWindow.AddButton(label: "Z: raise on Win+D", onClick: () => _zOrder.SetStrategy(ZOrderStrategy.RaiseOnShowDesktop));
        labWindow.AddButton(label: "Z: owned by Progman", onClick: () => _zOrder.SetStrategy(ZOrderStrategy.OwnedByProgman));
    }

    protected override void OnExit(ExitEventArgs exitArgs)
    {
        _zOrder?.Dispose();
        Lab.Log("clean exit");
        base.OnExit(exitArgs);
    }
}
```

- [ ] **Step 4: Build and run**

Run: `dotnet build spikes/M0.slnx` then `dotnet run --project spikes/M0.DesktopLayer`
Expected: 0 errors. The log shows `foreground WinEvent hook installed: True`, and every focus change logs `foreground -> <class>`.

- [ ] **Step 5: Verify checklist B1–B9 and F1** (B3/B4/B6/B8 need real keypresses: **[USER]**; B7 and the ToggleDesktop variant of B3 can be scripted)

Test B3–B5 and B7 under each of the three strategies, and record which strategy wins. Note the exact class name the log shows when Win+D is pressed: `Progman` or `WorkerW`.

- [ ] **Step 6: Append B and F rows to `docs/research/desktop-layer.md`**

```markdown
| B1 | | |
| B2 | | |
| B3 | | class logged on Win+D: ___ ; per strategy: bottom-only __ / raise __ / owned __ |
| B4 | | |
| B5 | | |
| B6 | | |
| B7 | | raise: survived? owned-by-Progman: fence destroyed? |
| B8 | | |
| B9 | | |
| F1 | | |
```
Fill in every cell from what you actually observed.

- [ ] **Step 7: Commit**

```powershell
git add spikes/M0.DesktopLayer docs/research/desktop-layer.md
git commit -m "feat: added z-order strategies and Win+D handling to M0 spike"
```

---

### Task 6: Hide desktop icons and watchdog (spec §4.3, ADR-005)

**Files:**
- Create: `spikes/M0.DesktopLayer/DesktopIcons.cs`, `spikes/M0.DesktopLayer/Watchdog.cs`
- Modify: `spikes/M0.DesktopLayer/App.xaml.cs` (version 4), `docs/research/desktop-layer.md`

**Interfaces:**
- Consumes: `RestartThrottle.ShouldRestart` (Task 3), `DesktopZOrder.Reapply` (Task 5).
- Produces: `DesktopIcons.TrySetHidden(bool hidden) -> bool`, `DesktopIcons.TryIsHidden() -> bool?`; `Watchdog.Spawn()`, `Watchdog.MarkCleanShutdown()`, `Watchdog.Run(int mainProcessId)`.

- [ ] **Step 1: Create `spikes/M0.DesktopLayer/DesktopIcons.cs`**

```csharp
using Windows.Win32;
using ComServiceProvider = Windows.Win32.System.Com.IServiceProvider;
using Windows.Win32.UI.Shell;

namespace NeoFences.Spikes.M0;

/// <summary>Spike for spec §4.3: hide/show native desktop icons via the documented folder-view flag (no injection).</summary>
public static class DesktopIcons
{
    public static bool TrySetHidden(bool hidden)
    {
        try
        {
            var folderView = GetDesktopFolderView();
            folderView.SetCurrentFolderFlags((uint)FOLDERFLAGS.FWF_NOICONS, hidden ? (uint)FOLDERFLAGS.FWF_NOICONS : 0u);
            Lab.Log($"desktop icons hidden={hidden} (now reports hidden={IsHidden(folderView)})");
            return true;
        }
        catch (Exception failure)
        {
            Lab.Log($"desktop icons set hidden={hidden} FAILED: {failure.GetType().Name}: {failure.Message}");
            return false;
        }
    }

    public static bool? TryIsHidden()
    {
        try { return IsHidden(GetDesktopFolderView()); }
        catch (Exception failure)
        {
            Lab.Log($"desktop icons query FAILED: {failure.GetType().Name}: {failure.Message}");
            return null;
        }
    }

    private static bool IsHidden(IFolderView2 folderView)
    {
        folderView.GetCurrentFolderFlags(out uint flags);
        return (flags & (uint)FOLDERFLAGS.FWF_NOICONS) != 0;
    }

    // Raymond Chen, "Manipulating the positions of desktop icons": ShellWindows -> desktop -> top-level browser -> active view.
    private static IFolderView2 GetDesktopFolderView()
    {
        var shellWindows = (IShellWindows)new ShellWindows();
        object location = 0;   // CSIDL_DESKTOP as VT_I4
        object root = null!;   // VT_EMPTY
        var dispatch = shellWindows.FindWindowSW(location, root, ShellWindowTypeConstants.SWC_DESKTOP, out _,
            ShellWindowFindWindowOptions.SWFO_NEEDDISPATCH);
        var serviceProvider = (ComServiceProvider)dispatch;
        serviceProvider.QueryService(PInvoke.SID_STopLevelBrowser, out IShellBrowser browser);
        browser.QueryActiveShellView(out IShellView view);
        return (IFolderView2)view;
    }
}
```

- [ ] **Step 2: Create `spikes/M0.DesktopLayer/Watchdog.cs`**

```csharp
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace NeoFences.Spikes.M0;

/// <summary>Spike for ADR-005: a second copy of the exe restores desktop icons if the main process dies uncleanly.</summary>
public static class Watchdog
{
    private static string RestartLogPath => Path.Combine(Lab.DataDir, "restarts.txt");

    private static string MarkerPath(int processId) => Path.Combine(Lab.DataDir, $"clean-shutdown-{processId}");

    public static void Spawn()
    {
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--watchdog {Environment.ProcessId}") { UseShellExecute = false });
        Lab.Log("watchdog spawned");
    }

    public static void MarkCleanShutdown()
    {
        Directory.CreateDirectory(Lab.DataDir);
        File.WriteAllText(MarkerPath(Environment.ProcessId), DateTimeOffset.Now.ToString("O"));
    }

    /// <summary>Runs in the watchdog process (STA, no windows). Returns when the main process has exited.</summary>
    public static void Run(int mainProcessId)
    {
        Lab.Log($"watchdog: watching {mainProcessId}");
        try
        {
            using var mainProcess = Process.GetProcessById(mainProcessId);
            mainProcess.WaitForExit();
        }
        catch (ArgumentException)
        {
            Lab.Log("watchdog: main process already gone");
        }

        var marker = MarkerPath(mainProcessId);
        if (File.Exists(marker))
        {
            File.Delete(marker);
            Lab.Log("watchdog: clean shutdown, nothing to do");
            return;
        }

        Lab.Log("watchdog: UNCLEAN exit -> restoring desktop icons");
        DesktopIcons.TrySetHidden(false);

        var now = DateTimeOffset.Now;
        if (!RestartThrottle.ShouldRestart(recentRestarts: ReadRestarts(), now: now))
        {
            Lab.Log("watchdog: restart limit reached (3 per 10 min), staying down");
            return;
        }
        File.AppendAllText(RestartLogPath, now.ToString("O") + Environment.NewLine);
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false });
        Lab.Log("watchdog: main restarted");
    }

    private static List<DateTimeOffset> ReadRestarts() =>
        File.Exists(RestartLogPath)
            ? File.ReadAllLines(RestartLogPath)
                .Select(line => DateTimeOffset.TryParse(line, CultureInfo.InvariantCulture, DateTimeStyles.None, out var restartTime) ? restartTime : (DateTimeOffset?)null)
                .OfType<DateTimeOffset>()
                .ToList()
            : [];
}
```

- [ ] **Step 3: Replace `spikes/M0.DesktopLayer/App.xaml.cs`** (version 4: watchdog mode, icons, crash, sign-out, Explorer-restart retry)

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace NeoFences.Spikes.M0;

public partial class App : Application
{
    private DesktopZOrder? _zOrder;
    private bool _iconsHidden;

    protected override void OnStartup(StartupEventArgs startupArgs)
    {
        base.OnStartup(startupArgs);

        if (startupArgs.Args is ["--watchdog", var processIdText])
        {
            Watchdog.Run(mainProcessId: int.Parse(processIdText, CultureInfo.InvariantCulture));
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, unhandled) => Lab.Log($"UNHANDLED: {unhandled.Exception}");
        // Sign-out/shutdown kills the watchdog too, and Explorer persists FWF_NOICONS: restore while we still can.
        SessionEnding += (_, sessionEnding) =>
        {
            Lab.Log($"session ending ({sessionEnding.ReasonSessionEnding}) -> restoring icons");
            if (_iconsHidden) DesktopIcons.TrySetHidden(false);
            Watchdog.MarkCleanShutdown();
        };

        var labWindow = new LabWindow();
        MainWindow = labWindow;
        labWindow.Show();
        Lab.Log($"M0 lab started. OS {Environment.OSVersion.Version}");

        Watchdog.Spawn();

        var fenceWindow = new SpikeFenceWindow();
        fenceWindow.Show();

        _zOrder = new DesktopZOrder();
        _zOrder.Track(fenceWindow);
        labWindow.ExplorerRestarted += () => ReapplyAfterExplorerRestart(zOrder: _zOrder, iconsWereHidden: _iconsHidden);

        labWindow.AddButton(label: "Backdrop: none", onClick: () => fenceWindow.SetBackdrop(BackdropMode.None));
        labWindow.AddButton(label: "Backdrop: DWM acrylic", onClick: () => fenceWindow.SetBackdrop(BackdropMode.DwmAcrylic));
        labWindow.AddButton(label: "Backdrop: DWM acrylic + keep active", onClick: () => fenceWindow.SetBackdrop(BackdropMode.DwmAcrylicKeepActive));
        labWindow.AddButton(label: "Backdrop: accent acrylic", onClick: () => fenceWindow.SetBackdrop(BackdropMode.AccentAcrylic));

        labWindow.AddButton(label: "Z: bottom only", onClick: () => _zOrder.SetStrategy(ZOrderStrategy.BottomOnly));
        labWindow.AddButton(label: "Z: raise on Win+D", onClick: () => _zOrder.SetStrategy(ZOrderStrategy.RaiseOnShowDesktop));
        labWindow.AddButton(label: "Z: owned by Progman", onClick: () => _zOrder.SetStrategy(ZOrderStrategy.OwnedByProgman));

        labWindow.AddButton(label: "Hide desktop icons", onClick: () => { if (DesktopIcons.TrySetHidden(true)) _iconsHidden = true; });
        labWindow.AddButton(label: "Show desktop icons", onClick: () => { if (DesktopIcons.TrySetHidden(false)) _iconsHidden = false; });
        labWindow.AddButton(label: "Query icons", onClick: () => Lab.Log($"icons hidden = {DesktopIcons.TryIsHidden()?.ToString() ?? "unknown"}"));
        labWindow.AddButton(label: "CRASH (FailFast)", onClick: () => Environment.FailFast("M0 spike: simulated crash"));
    }

    private void ReapplyAfterExplorerRestart(DesktopZOrder zOrder, bool iconsWereHidden)
    {
        // The new Explorer's desktop view may not exist yet when TaskbarCreated arrives: retry for ~5 s.
        var attempts = 0;
        var retryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        retryTimer.Tick += (_, _) =>
        {
            attempts++;
            zOrder.Reapply();
            var iconsOk = !iconsWereHidden || DesktopIcons.TrySetHidden(true);
            if (iconsOk || attempts >= 10)
            {
                retryTimer.Stop();
                Lab.Log($"re-applied after Explorer restart in {attempts} attempt(s), icons ok={iconsOk}");
            }
        };
        retryTimer.Start();
    }

    protected override void OnExit(ExitEventArgs exitArgs)
    {
        _zOrder?.Dispose();
        if (_iconsHidden) DesktopIcons.TrySetHidden(false);
        Watchdog.MarkCleanShutdown();
        Lab.Log("clean exit");
        base.OnExit(exitArgs);
    }
}
```

- [ ] **Step 4: Build, run, smoke-test icons**

Run: `dotnet build spikes/M0.slnx` then `dotnet run --project spikes/M0.DesktopLayer`
Expected: the log shows `watchdog spawned` and, from the watchdog's PID, `watchdog: watching <pid>`. "Query icons" logs `icons hidden = False`. If it instead logs `FAILED: COMException …`, write down the HRESULT; that counts as a C1 FAIL and must be investigated before moving on.

- [ ] **Step 5: Verify checklist C1–C8**

Before C4–C6, delete `%LOCALAPPDATA%\NeoFences\spike-m0\restarts.txt` so the throttle starts fresh. For C5, use the kill helper on the lab PID (the process without `--watchdog`). C8 is **[USER]**: sign out and sign back in with icons hidden.

**Safety:** if any C test leaves icons hidden, restore them immediately (desktop right-click → View → Show desktop icons) and mark the row FAIL.

- [ ] **Step 6: Append C rows to `docs/research/desktop-layer.md`**

```markdown
| C1 | | |
| C2 | | |
| C3 | | |
| C4 | | time from crash to icons visible: ___ s |
| C5 | | |
| C6 | | |
| C7 | | attempts needed: ___ |
| C8 | | |
```

- [ ] **Step 7: Commit**

```powershell
git add spikes/M0.DesktopLayer docs/research/desktop-layer.md
git commit -m "feat: added desktop icon hiding and watchdog to M0 spike"
```

---

### Task 7: Low-level mouse hook gestures and game detection logging (spec §4.5, §4.7)

**Files:**
- Create: `spikes/M0.DesktopLayer/MouseHookThread.cs`
- Replace: `spikes/M0.DesktopLayer/GameDetector.cs` (adds live logging; keeps `CoversMonitor`)
- Modify: `spikes/M0.DesktopLayer/App.xaml.cs` (version 5, final), `docs/research/desktop-layer.md`

**Interfaces:**
- Consumes: `DesktopGestureTracker` (Task 2), `DesktopWindows.IsOverDesktop` (Task 5), `DesktopZOrder.ForegroundChanged` (Task 5).
- Produces: `enum RightClickSuppression { None, SwallowUpAfterDrag, SwallowDownAndReplay }`; `MouseHookThread(Action<DesktopGesture, int, int> onGesture) : IDisposable` with `Suppression`; `GameDetector.LogForeground(HWND, string)` (internal).

- [ ] **Step 1: Create `spikes/M0.DesktopLayer/MouseHookThread.cs`**

```csharp
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Spikes.M0;

public enum RightClickSuppression
{
    /// <summary>Observe only. Explorer's desktop menu will appear after a right-drag.</summary>
    None,
    /// <summary>S1: pass RightDown through, swallow RightUp after a drag.</summary>
    SwallowUpAfterDrag,
    /// <summary>S2: swallow every desktop RightDown; on release without drag, replay a synthetic right-click.</summary>
    SwallowDownAndReplay,
}

/// <summary>Spike for spec §4.5: WH_MOUSE_LL on its own thread with its own message loop (ADR-007).</summary>
public sealed class MouseHookThread : IDisposable
{
    /// <summary>dwExtraInfo stamped on replayed input so the hook lets it through ("NFNC").</summary>
    private const nuint ReplayMarker = 0x4E464E43;

    private static readonly uint ReplayRightClickMessage = PInvoke.WM_APP + 1;

    private readonly HOOKPROC _hookCallback; // field keeps the delegate alive while the hook exists
    private readonly Action<DesktopGesture, int, int> _onGesture;
    private readonly DesktopGestureTracker _tracker;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private uint _threadId;
    private bool _swallowedRightDown;

    public RightClickSuppression Suppression { get; set; } = RightClickSuppression.SwallowUpAfterDrag;

    /// <param name="onGesture">Invoked on the hook thread; marshal to the UI thread yourself and return fast.</param>
    public MouseHookThread(Action<DesktopGesture, int, int> onGesture)
    {
        _onGesture = onGesture;
        _hookCallback = OnLowLevelMouse;
        _tracker = new DesktopGestureTracker(
            doubleClickMilliseconds: PInvoke.GetDoubleClickTime(),
            doubleClickSlopPixels: PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXDOUBLECLK) / 2,
            dragThresholdPixels: 8);
        _thread = new Thread(RunHookLoop) { IsBackground = true, Name = "NeoFences.MouseHook" };
        _thread.Start();
        _started.Wait();
    }

    private void RunHookLoop()
    {
        _threadId = PInvoke.GetCurrentThreadId();
        using var module = PInvoke.GetModuleHandle((string?)null);
        using var hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _hookCallback, module, 0);
        Lab.Log($"WH_MOUSE_LL installed: {!hook.IsInvalid}");
        _started.Set();

        while (PInvoke.GetMessage(out MSG message, HWND.Null, 0, 0) > 0)
        {
            if (message.message == ReplayRightClickMessage) ReplayRightClick();
        }
        Lab.Log("WH_MOUSE_LL removed");
    }

    private unsafe LRESULT OnLowLevelMouse(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code < 0) return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);

        var hookData = (MSLLHOOKSTRUCT*)lParam.Value;
        if (hookData->dwExtraInfo == ReplayMarker) return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);

        MouseAction? action = (uint)wParam.Value switch
        {
            PInvoke.WM_LBUTTONDOWN => MouseAction.LeftDown,
            PInvoke.WM_RBUTTONDOWN => MouseAction.RightDown,
            PInvoke.WM_RBUTTONUP => MouseAction.RightUp,
            PInvoke.WM_MOUSEMOVE => MouseAction.Move,
            _ => null,
        };
        if (action is null) return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);

        var point = hookData->pt;
        // WindowFromPoint only on button events: moves arrive at up to 1000 Hz and must stay cheap.
        var overDesktop = action != MouseAction.Move && DesktopWindows.IsOverDesktop(point);
        var gesture = _tracker.OnMouse(action.Value, point.X, point.Y, hookData->time, overDesktop);
        if (gesture != DesktopGesture.None) _onGesture(gesture, point.X, point.Y);

        switch (Suppression)
        {
            case RightClickSuppression.SwallowUpAfterDrag when gesture == DesktopGesture.RightDragCompleted:
                return (LRESULT)1;

            case RightClickSuppression.SwallowDownAndReplay when action == MouseAction.RightDown && overDesktop:
                _swallowedRightDown = true;
                return (LRESULT)1;

            case RightClickSuppression.SwallowDownAndReplay when action == MouseAction.RightUp && _swallowedRightDown:
                _swallowedRightDown = false;
                // SendInput re-enters low-level hooks, so replay from the message loop, not from inside this callback.
                if (gesture != DesktopGesture.RightDragCompleted) PInvoke.PostThreadMessage(_threadId, ReplayRightClickMessage, 0, 0);
                return (LRESULT)1;
        }
        return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
    }

    private static void ReplayRightClick()
    {
        Span<INPUT> inputs =
        [
            new INPUT { type = INPUT_TYPE.INPUT_MOUSE, Anonymous = { mi = new MOUSEINPUT { dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN, dwExtraInfo = ReplayMarker } } },
            new INPUT { type = INPUT_TYPE.INPUT_MOUSE, Anonymous = { mi = new MOUSEINPUT { dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP, dwExtraInfo = ReplayMarker } } },
        ];
        var sent = PInvoke.SendInput(inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
        Lab.Log($"replayed right-click (sent {sent}/2)");
    }

    public void Dispose()
    {
        PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(1));
        _started.Dispose();
    }
}
```

- [ ] **Step 2: Replace `spikes/M0.DesktopLayer/GameDetector.cs`** (keeps `CoversMonitor`; adds live logging)

```csharp
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Spikes.M0;

public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom);

/// <summary>Spike: logs whether the foreground window looks like a game (spec §4.7). Decides nothing yet.</summary>
public static class GameDetector
{
    private static readonly string[] ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    /// <summary>True when the window rect covers the whole monitor rect (borderless games often overhang by a few pixels).</summary>
    public static bool CoversMonitor(ScreenRect window, ScreenRect monitor) =>
        window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;

    internal static void LogForeground(HWND foreground, string className)
    {
        if (ShellClasses.Contains(className)) return;

        PInvoke.SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE notificationState);
        var monitor = PInvoke.MonitorFromWindow(foreground, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        PInvoke.GetMonitorInfo(monitor, ref monitorInfo);
        PInvoke.GetWindowRect(foreground, out RECT windowRect);
        var style = (WINDOW_STYLE)PInvoke.GetWindowLongPtr(foreground, WINDOW_LONG_PTR_INDEX.GWL_STYLE);

        var covers = CoversMonitor(
            window: new ScreenRect(windowRect.left, windowRect.top, windowRect.right, windowRect.bottom),
            monitor: new ScreenRect(monitorInfo.rcMonitor.left, monitorInfo.rcMonitor.top, monitorInfo.rcMonitor.right, monitorInfo.rcMonitor.bottom));
        var hasCaption = style.HasFlag(WINDOW_STYLE.WS_CAPTION);
        var gameLike = notificationState is QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN or QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY
                       || (covers && !hasCaption);
        Lab.Log($"game check: class={className} quns={notificationState} coversMonitor={covers} hasCaption={hasCaption} => gameLike={gameLike}");
    }
}
```

- [ ] **Step 3: Replace `spikes/M0.DesktopLayer/App.xaml.cs`** (version 5, final)

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace NeoFences.Spikes.M0;

public partial class App : Application
{
    private DesktopZOrder? _zOrder;
    private MouseHookThread? _mouseHook;
    private bool _iconsHidden;

    protected override void OnStartup(StartupEventArgs startupArgs)
    {
        base.OnStartup(startupArgs);

        if (startupArgs.Args is ["--watchdog", var processIdText])
        {
            Watchdog.Run(mainProcessId: int.Parse(processIdText, CultureInfo.InvariantCulture));
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, unhandled) => Lab.Log($"UNHANDLED: {unhandled.Exception}");
        // Sign-out/shutdown kills the watchdog too, and Explorer persists FWF_NOICONS: restore while we still can.
        SessionEnding += (_, sessionEnding) =>
        {
            Lab.Log($"session ending ({sessionEnding.ReasonSessionEnding}) -> restoring icons");
            if (_iconsHidden) DesktopIcons.TrySetHidden(false);
            Watchdog.MarkCleanShutdown();
        };

        var labWindow = new LabWindow();
        MainWindow = labWindow;
        labWindow.Show();
        Lab.Log($"M0 lab started. OS {Environment.OSVersion.Version}");

        Watchdog.Spawn();

        var fenceWindow = new SpikeFenceWindow();
        fenceWindow.Show();

        _zOrder = new DesktopZOrder();
        _zOrder.Track(fenceWindow);
        _zOrder.ForegroundChanged += GameDetector.LogForeground;

        labWindow.ExplorerRestarted += () => ReapplyAfterExplorerRestart(zOrder: _zOrder, iconsWereHidden: _iconsHidden);

        labWindow.AddButton(label: "Backdrop: none", onClick: () => fenceWindow.SetBackdrop(BackdropMode.None));
        labWindow.AddButton(label: "Backdrop: DWM acrylic", onClick: () => fenceWindow.SetBackdrop(BackdropMode.DwmAcrylic));
        labWindow.AddButton(label: "Backdrop: DWM acrylic + keep active", onClick: () => fenceWindow.SetBackdrop(BackdropMode.DwmAcrylicKeepActive));
        labWindow.AddButton(label: "Backdrop: accent acrylic", onClick: () => fenceWindow.SetBackdrop(BackdropMode.AccentAcrylic));

        labWindow.AddButton(label: "Z: bottom only", onClick: () => _zOrder.SetStrategy(ZOrderStrategy.BottomOnly));
        labWindow.AddButton(label: "Z: raise on Win+D", onClick: () => _zOrder.SetStrategy(ZOrderStrategy.RaiseOnShowDesktop));
        labWindow.AddButton(label: "Z: owned by Progman", onClick: () => _zOrder.SetStrategy(ZOrderStrategy.OwnedByProgman));

        labWindow.AddButton(label: "Hide desktop icons", onClick: () => { if (DesktopIcons.TrySetHidden(true)) _iconsHidden = true; });
        labWindow.AddButton(label: "Show desktop icons", onClick: () => { if (DesktopIcons.TrySetHidden(false)) _iconsHidden = false; });
        labWindow.AddButton(label: "Query icons", onClick: () => Lab.Log($"icons hidden = {DesktopIcons.TryIsHidden()?.ToString() ?? "unknown"}"));
        labWindow.AddButton(label: "CRASH (FailFast)", onClick: () => Environment.FailFast("M0 spike: simulated crash"));

        labWindow.AddButton(label: "Mouse hook: start", onClick: StartMouseHook);
        labWindow.AddButton(label: "Mouse hook: stop", onClick: StopMouseHook);
        labWindow.AddButton(label: "Right-click: observe", onClick: () => SetSuppression(RightClickSuppression.None));
        labWindow.AddButton(label: "Right-click: S1 swallow up", onClick: () => SetSuppression(RightClickSuppression.SwallowUpAfterDrag));
        labWindow.AddButton(label: "Right-click: S2 swallow+replay", onClick: () => SetSuppression(RightClickSuppression.SwallowDownAndReplay));
    }

    private void ReapplyAfterExplorerRestart(DesktopZOrder zOrder, bool iconsWereHidden)
    {
        // The new Explorer's desktop view may not exist yet when TaskbarCreated arrives: retry for ~5 s.
        var attempts = 0;
        var retryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        retryTimer.Tick += (_, _) =>
        {
            attempts++;
            zOrder.Reapply();
            var iconsOk = !iconsWereHidden || DesktopIcons.TrySetHidden(true);
            if (iconsOk || attempts >= 10)
            {
                retryTimer.Stop();
                Lab.Log($"re-applied after Explorer restart in {attempts} attempt(s), icons ok={iconsOk}");
            }
        };
        retryTimer.Start();
    }

    private void StartMouseHook()
    {
        if (_mouseHook is not null) return;
        _mouseHook = new MouseHookThread(onGesture: (gesture, x, y) =>
            Dispatcher.BeginInvoke(() => Lab.Log($"GESTURE {gesture} at ({x},{y})")));
    }

    private void StopMouseHook()
    {
        _mouseHook?.Dispose();
        _mouseHook = null;
    }

    private void SetSuppression(RightClickSuppression suppression)
    {
        if (_mouseHook is null) StartMouseHook();
        _mouseHook!.Suppression = suppression;
        Lab.Log($"right-click suppression = {suppression}");
    }

    protected override void OnExit(ExitEventArgs exitArgs)
    {
        StopMouseHook();
        _zOrder?.Dispose();
        if (_iconsHidden) DesktopIcons.TrySetHidden(false);
        Watchdog.MarkCleanShutdown();
        Lab.Log("clean exit");
        base.OnExit(exitArgs);
    }
}
```

- [ ] **Step 4: Build, test, run**

Run: `dotnet build spikes/M0.slnx`, then `dotnet test spikes/M0.slnx`, then `dotnet run --project spikes/M0.DesktopLayer`
Expected: 0 errors; `Passed: 18`. Clicking "Mouse hook: start" logs `WH_MOUSE_LL installed: True`.

- [ ] **Step 5: Verify checklist D1–D9, E1–E5, F2** (gestures and games are **[USER]**)

Run D3 → D4 → D5 in that order and note exactly what Explorer does in each, including whether the desktop gets stuck in a selection-rectangle state under S1. Run E1/E2 with whichever borderless or exclusive-fullscreen game is installed (e.g. an AC or Crysis title).

- [ ] **Step 6: Append D, E, F2 rows to `docs/research/desktop-layer.md`**

```markdown
| D1 | | |
| D2 | | |
| D3 | | |
| D4 | | S1: menu suppressed? desktop stuck? |
| D5 | | S2: drag clean? plain right-click menu replayed? |
| D6 | | |
| D7 | | |
| D8 | | |
| D9 | | |
| E1 | | game used: ___ |
| E2 | | |
| E3 | | |
| E4 | | |
| E5 | | |
| F2 | | |
```

- [ ] **Step 7: Commit**

```powershell
git add spikes/M0.DesktopLayer docs/research/desktop-layer.md
git commit -m "feat: added low-level mouse hook gestures and game detection logging to M0 spike"
```

---

### Task 8: Conclusions, GO/NO-GO and doc sync

**Files:**
- Modify: `docs/research/desktop-layer.md` (Conclusions), `docs/DECISIONS.md` (ADR-011), `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/SESSION-LOG.md`, `docs/hub/neofences-hq.html`, `CLAUDE.md` (only if a hard rule changes)

**Interfaces:**
- Consumes: every result row from Tasks 4–7.
- Produces: the decisions M2 builds on. Name them exactly: the backdrop mode, the z-order strategy, and the right-click suppression mode.

- [ ] **Step 1: Apply the GO/NO-GO rules** and write them under `## Conclusions` in `docs/research/desktop-layer.md`:

| Assumption | GO when | If NO-GO |
|---|---|---|
| §4.1 acrylic fence | A1 + A2 pass and ≥1 mode passes A3 | Use the best mode even if it goes solid while inactive (document it), or fall back to `None` with a tint |
| §4.2 Win+D | B1–B5 pass under RaiseOnShowDesktop or OwnedByProgman | Supersede ADR-003: parent fences into the desktop host window (spec fallback) |
| §4.3 icons | C1–C5, C7, C8 pass | Stop: Takeover (ADR-002) is unsafe; discuss with the user before M1 |
| §4.4 Explorer restart | B7 passes for the chosen strategy and C7 passes | Increase the retry window; if still failing, discuss |
| §4.5 gestures | D1 passes and D4 or D5 passes | Ctrl+right-drag and menu entries only (spec fallback) |
| §4.7 game mode | E1, E3, E4 pass | Note the heuristic gaps for M6 |

Write one paragraph per assumption: its verdict, the chosen mode or strategy, and any surprises (for example, the class name seen on Win+D, or whether OwnedByProgman destroyed the fence).

- [ ] **Step 2: Add ADR-011 to `docs/DECISIONS.md`**

```markdown
## ADR-011 — M0 results: desktop layer choices
**Date:** <today> · **Status:** Accepted · **Refines:** ADR-003, ADR-005, ADR-007

**Context.** M0 spike on <machine line from research note>. Evidence: `docs/research/desktop-layer.md`.

**Decision.**
- Backdrop mode: <winning BackdropMode> (<why: A1–A3 results>).
- Z-order strategy: <winning ZOrderStrategy> (<B results>).
- Desktop icons: FWF_NOICONS via IFolderView2 + watchdog + SessionEnding restore (<C results>).
- Right-click suppression: <None/S1/S2> (<D results>); draw-fence gesture is <right-drag / Ctrl+right-drag>.
- Game detection: QUNS + covers-monitor + no-WS_CAPTION (<E results>).

**Consequences.** <what M2 must implement differently from the spec, if anything>.
```
Fill in every `<…>` from the research note. If any assumption is NO-GO, also mark the affected ADR `Superseded by ADR-011` and update spec §10 with an erratum line.

- [ ] **Step 3: Update `docs/ARCHITECTURE.md`**

In the `| Shell/DesktopLayer |` and `| Shell/Watchdog |` rows, add the chosen strategy and the SessionEnding restore. Replace the `## Status` section with: `M0 spike complete (<GO / partial>). Next: M1 Core + config.`

- [ ] **Step 4: Update `docs/ROADMAP.md`**

Tick every M0 checkbox that passed (`[x]`), mark failures `[!] <id> failed — see research note`, remove the `[~] claimed` marker from the M0 heading, and add this line under "Now": `- [ ] M1 implementation plan`.

- [ ] **Step 5: Append to `docs/SESSION-LOG.md`**

```markdown
## <today> — M0 desktop layer spike

**Done:** Built `spikes/M0.DesktopLayer`, ran TEST-CHECKLIST A–F, wrote `docs/research/desktop-layer.md`.
**Decisions:** ADR-011 (<one-line summary of the three choices>).
**Next:** M1 plan (Core + config).
**Open questions:** <anything unresolved, or "none">.
```

- [ ] **Step 6: Refresh the hub** (`docs/hub/neofences-hq.html`, `HUB` object only)

- `updated` → today; `commit` → the current `git rev-parse --short HEAD`.
- `phase` → `"M0 complete"`; `current` → `"M1 plan"`; `waiting` → `[]`, or a question for the user if a NO-GO needs their input.
- `tasks[1].items` → each M0 item's `s` set to `"done"`, or `"blocked"` for FAILs.
- `milestones[0].s` → `"done"`; `milestones[1].s` → `"next"`.
- `risks` → set the first two rows' level to `"done"` if resolved, else keep `"risk"` and update the plan text.
- `research` → append `{ t: "M0 results", b: "<2–3 sentence summary>", impact: "ADR-011", src: [] }`.
- `adrs` → append the ADR-011 entry (`id`, `t`, `s`, `d`, `c`).
- `sessions` → append today's entry; `commits` → append each M0 commit `[hash, message]`.

Then check the script and republish:
```powershell
$html = Get-Content docs/hub/neofences-hq.html -Raw
$script = [regex]::Match($html, '(?s)<script>(.*)</script>').Groups[1].Value
Set-Content -Path "<scratchpad>\hub.js" -Value $script
node --check "<scratchpad>\hub.js"
```
Expected: no output (syntax OK). Publish with the Artifact tool: `file_path` = `docs/hub/neofences-hq.html`, `url` = `<private hub link: see CLAUDE.local.md>`.

- [ ] **Step 7: Commit (two commits)**

```powershell
git add docs/research/desktop-layer.md docs/DECISIONS.md docs/ARCHITECTURE.md
git commit -m "docs: recorded M0 desktop layer findings and ADR-011"
git add docs/ROADMAP.md docs/SESSION-LOG.md docs/hub/neofences-hq.html
git commit -m "docs: updated roadmap, session log and hub after M0"
```

- [ ] **Step 8: Finish the branch**

Use superpowers:finishing-a-development-branch to merge `m0-desktop-layer-spike` into `main`, after the user confirms.
