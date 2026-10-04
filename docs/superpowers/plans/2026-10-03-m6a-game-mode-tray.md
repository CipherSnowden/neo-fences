# M6a — Game Mode, Tray and Pause Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:**
- NeoFences goes idle while a full-screen game runs: the mouse hook is removed, Peek is ignored, and file-change work is deferred.
- NeoFences gets a tray icon. Its menu (New fence · Quick-hide · Peek · Pause · Exit) opens on any click.
- Pause gives the desktop back to Windows.
- The mouse hook survives drops: Highest priority, re-installed after an Explorer restart and on unlock.

**Architecture:**
- `NeoFences.Core`:
  - `GameModePolicy` (notification state + foreground window → game or not);
  - `RunState` (Takeover / quick-hide / Pause / game mode → fences, icons, hook, deferral).
- `NeoFences.Shell`:
  - `GameDetection` and `ForegroundWatcher` (out-of-context WinEvent hook);
  - `TrayIcon` / `TrayMenu` (Shell_NotifyIcon, TrackPopupMenuEx);
  - `SessionNotifications`;
  - `DesktopMouseHook` hardening.
- `NeoFences.App`:
  - `SystemMessageWindow` (tray clicks, unlock);
  - `PortalState.SetPaused`;
  - `FenceHost` (game mode, Pause, tray menu, deferred desktop changes, hook re-install, icon retry on exit);
  - the new `NeoFences.ico`.

**Tech Stack:** .NET 10, WPF, CsWin32 0.3.335, Serilog, xUnit. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-10-02-neofences-v1-design.md` §4.7 (game mode), §6 (tray, Pause), §7 (reliability rules). **Decisions:**
- ADR-007, ADR-011, ADR-019, ADR-020;
- **ADR-021** (new, Task 5).

User choices on 2026-10-03:
- M6 split into M6a/M6b;
- game mode = go idle (fences stay);
- a left-click on the tray icon opens the menu.

**Pre-verified (2026-10-03):** every code block below was compiled together (0 warnings, 0 errors), and **210/210 tests pass**. The Task 3 smoke passed on the real desktop with the user's consent. It covered:
- the tray menu items;
- Pause (fences hidden, icons shown, hook removed), the paused menu (Pause checked, New fence grayed), and Resume (all back);
- New fence from the tray;
- game mode with a stand-in borderless full-screen window: on, hook removed, Peek ignored, a Desktop file deferred until the game ended, then off with the hook back.

**Found in the prototype:**
- `LoadIconMetric` (comctl32 v6) crashed the app at start, because WPF does not load comctl32 v6. Now the icon loads with `LoadImage`, and a tray failure is caught.
- A borderless full-screen window flips the notification state 2–3.5 s after activation. The re-checks run at 1, 2.5 and 5 s.
- PowerShell passes `$null` as `""` to string P/Invoke parameters; the smoke uses `[NullString]::Value`.

## Global Constraints

- **Hard rule 2:** every icon decision goes through `RunState.IconsHidden` and `SetIconsHidden` (the takeover-active marker). Pause always shows the icons. Exit, session end and the crash handler also show them whenever the marker is set.
- **Hard rule 3:** the foreground watcher is an out-of-context WinEvent hook. `WH_MOUSE_LL` is removed while paused or gaming.
- **Hard rules 4 and 7:** all Win32 is in `NeoFences.Shell`. A tray or foreground-watch failure is logged and turns off only that feature; it never crashes the app.
- Pause and game mode are runtime only (not saved).
- Commits: single line, Conventional Commits, past tense, no `Co-Authored-By` trailer. Test command: `dotnet test NeoFences.slnx`.
- Smokes take the mouse. Ask first, print "TEST RUNNING" / "TEST COMPLETE", never type into Windows Terminal, and refocus it at the end. Run the smoke with Windows PowerShell (`powershell -File`): its stand-in game is compiled with Add-Type.

## Review Focus

1. **A real game** (exclusive full-screen, borderless, a launcher that goes full screen later; alt-tab in and out quickly). Expected: idle within ~5 s, back on leaving, and no flapping. By hand O5.
2. **Explorer restarting or the session locking during a game or while paused.** Expected: the tray comes back, the hook comes back only when wanted, and the icons stay right. By hand O7, O8.
3. **Many Desktop or Portal changes during a long session** (downloads, screenshots). Expected: all applied in order afterwards; no lost item, no crash. By hand O6.
4. **Tray click storms or a menu left open while NeoFences exits.** Expected: no crash, and no orphan tray icon (NIM_DELETE on exit). By hand O2, O10.
5. **Pause, then exit, crash or power cut.** Expected: icons visible (Pause shows them; marker; ADR-019). Pinned by `Paused_ShowsIcons_HidesFences_DropsTheHook`; by hand O3.

---

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/NeoFences.Core/Lifecycle/GameModePolicy.cs`, `RunState.cs` | game decision, mode rules | 1 |
| `src/NeoFences.Shell/GameDetection.cs`, `TrayIcon.cs`, `DesktopMouseHook.cs`, `NativeMethods.txt` | foreground + notification state, tray icon/menu, session notices, hook hardening | 2 |
| `src/NeoFences.App/NeoFences.ico`, `NeoFences.App.csproj`, `SystemMessageWindow.cs`, `PortalState.cs`, `FenceHost.cs` | icon, tray/unlock messages, Portal pause, wiring | 3 |
| `docs/TEST-CHECKLIST.md` (section O), `docs/research/m6a-game-mode-tray.md` | verification | 4 |
| `docs/DECISIONS.md` (ADR-021), `ARCHITECTURE.md`, `FEATURES.md`, `ROADMAP.md`, `SESSION-LOG.md`, hub | docs sync | 5 |

---

### Task 1: Core — game mode policy and run-state rules

**Files:**
- Modify: `docs/ROADMAP.md` (claim M6a)
- Create: `src/NeoFences.Core/Lifecycle/GameModePolicy.cs`, `src/NeoFences.Core/Lifecycle/RunState.cs`, `tests/NeoFences.Core.Tests/Lifecycle/GameModePolicyTests.cs`, `tests/NeoFences.Core.Tests/Lifecycle/RunStateTests.cs`

**Interfaces:**
- Produces:
  - `enum NotificationState` (QUERY_USER_NOTIFICATION_STATE values);
  - `record ForegroundSnapshot(NotificationState Notifications, string WindowClass, bool IsOwnProcess)`;
  - `GameModePolicy.IsGameActive(bool enabled, ForegroundSnapshot foreground)` and `GameModePolicy.RecheckDelays`;
  - `record RunState(bool Takeover, bool QuickHidden, bool Paused, bool GameMode)` with `FencesVisible`, `IconsHidden`, `MouseHookWanted` and `ShellWorkDeferred`.

- [ ] **Step 1: Branch and claim**

```powershell
git switch -c m6a-game-mode-tray
```
In `docs/ROADMAP.md`, under `## M6 — Polish + game mode`, replace `- [ ] M6a implementation plan` with `- [x] M6a implementation plan` and add `- [~] M6a — claimed by session 2026-10-03 m6a`.
```powershell
git add docs/ROADMAP.md
git commit -m "docs: claimed M6a"
```

- [ ] **Step 2: Write the failing tests**

`tests/NeoFences.Core.Tests/Lifecycle/GameModePolicyTests.cs`:
```csharp
using NeoFences.Core.Lifecycle;

namespace NeoFences.Core.Tests.Lifecycle;

public class GameModePolicyTests
{
    private static ForegroundSnapshot Foreground(NotificationState notifications, string windowClass = "ScimitarEngineWindowClass", bool isOwnProcess = false) =>
        new(notifications, windowClass, isOwnProcess);

    [Theory]
    [InlineData(NotificationState.Busy)]                 // borderless full screen (the M0 game)
    [InlineData(NotificationState.RunningD3DFullScreen)] // exclusive full screen
    public void FullScreenApp_InFront_IsAGame(NotificationState notifications) =>
        Assert.True(GameModePolicy.IsGameActive(enabled: true, Foreground(notifications)));

    [Theory]
    [InlineData(NotificationState.AcceptsNotifications)] // a normal maximized app or browser
    [InlineData(NotificationState.NotPresent)]           // lock screen (M0 finding 5)
    [InlineData(NotificationState.PresentationMode)]
    [InlineData(NotificationState.QuietTime)]
    [InlineData(NotificationState.Unknown)]              // the query failed
    public void OtherStates_AreNotAGame(NotificationState notifications) =>
        Assert.False(GameModePolicy.IsGameActive(enabled: true, Foreground(notifications)));

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("")] // no foreground window
    public void BusyButDesktopOrTaskbarInFront_IsNotAGame(string windowClass) =>
        Assert.False(GameModePolicy.IsGameActive(enabled: true, Foreground(NotificationState.Busy, windowClass)));

    [Fact]
    public void BusyButNeoFencesInFront_IsNotAGame() =>
        Assert.False(GameModePolicy.IsGameActive(enabled: true, Foreground(NotificationState.Busy, "HwndWrapper[NeoFences;;1]", isOwnProcess: true)));

    [Fact]
    public void GameModeOff_NeverActivates() =>
        Assert.False(GameModePolicy.IsGameActive(enabled: false, Foreground(NotificationState.RunningD3DFullScreen)));
}
```
`tests/NeoFences.Core.Tests/Lifecycle/RunStateTests.cs`:
```csharp
using NeoFences.Core.Lifecycle;

namespace NeoFences.Core.Tests.Lifecycle;

public class RunStateTests
{
    [Fact]
    public void Normal_WithTakeover_ShowsFences_HidesIcons_WantsTheHook()
    {
        var state = new RunState(Takeover: true, QuickHidden: false, Paused: false, GameMode: false);
        Assert.True(state.FencesVisible);
        Assert.True(state.IconsHidden);
        Assert.True(state.MouseHookWanted);
        Assert.False(state.ShellWorkDeferred);
    }

    [Fact]
    public void QuickHidden_WithoutTakeover_HidesFencesAndIcons()
    {
        var state = new RunState(Takeover: false, QuickHidden: true, Paused: false, GameMode: false);
        Assert.False(state.FencesVisible);
        Assert.True(state.IconsHidden);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void Paused_ShowsIcons_HidesFences_DropsTheHook(bool takeover, bool quickHidden)
    {
        // Pause gives the desktop back to Windows, whatever else is on (hard rule 2 never depends on Pause).
        var state = new RunState(Takeover: takeover, QuickHidden: quickHidden, Paused: true, GameMode: false);
        Assert.False(state.FencesVisible);
        Assert.False(state.IconsHidden);
        Assert.False(state.MouseHookWanted);
    }

    [Fact]
    public void GameMode_KeepsFencesAndIcons_DropsTheHook_DefersShellWork()
    {
        var state = new RunState(Takeover: true, QuickHidden: false, Paused: false, GameMode: true);
        Assert.True(state.FencesVisible); // user choice: go idle, fences stay
        Assert.True(state.IconsHidden);
        Assert.False(state.MouseHookWanted);
        Assert.True(state.ShellWorkDeferred);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test NeoFences.slnx`
Expected: build FAILS with `CS0246` for `ForegroundSnapshot`, `NotificationState` and `RunState`.

- [ ] **Step 4: Implement**

`src/NeoFences.Core/Lifecycle/GameModePolicy.cs`:
```csharp
namespace NeoFences.Core.Lifecycle;

/// <summary>What SHQueryUserNotificationState reports (QUERY_USER_NOTIFICATION_STATE values).</summary>
public enum NotificationState
{
    Unknown = 0,
    NotPresent = 1,           // lock screen, screen saver, fast user switching
    Busy = 2,                 // a full-screen app (borderless games land here)
    RunningD3DFullScreen = 3, // exclusive full-screen Direct3D
    PresentationMode = 4,
    AcceptsNotifications = 5,
    QuietTime = 6,
    App = 7,
}

/// <summary>The foreground window at one moment, as game mode needs it.</summary>
public sealed record ForegroundSnapshot(NotificationState Notifications, string WindowClass, bool IsOwnProcess);

/// <summary>
/// Game mode (spec §4.7, M0 findings 4–5, ADR-021): NeoFences goes idle while a full-screen app is in front. The
/// notification state is the signal (it caught the M0 game; window-rect checks never did), but it is system-wide and
/// lags about a second, so it only counts when the foreground is an app: not the desktop, the taskbar or NeoFences.
/// The lock screen reports <see cref="NotificationState.NotPresent"/> and never counts.
/// </summary>
public static class GameModePolicy
{
    /// <summary>
    /// The notification state flips 1–3.5 s after a full-screen window activates (M0 E1: ~1 s; M6a probe: 2–3.5 s for a
    /// borderless window), with no further foreground event: check again then.
    /// </summary>
    public static readonly IReadOnlyList<TimeSpan> RecheckDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(5)];

    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
    };

    public static bool IsGameActive(bool enabled, ForegroundSnapshot foreground) =>
        enabled
        && foreground.Notifications is NotificationState.Busy or NotificationState.RunningD3DFullScreen
        && !foreground.IsOwnProcess
        && foreground.WindowClass.Length > 0
        && !ShellClasses.Contains(foreground.WindowClass);
}
```
`src/NeoFences.Core/Lifecycle/RunState.cs`:
```csharp
namespace NeoFences.Core.Lifecycle;

/// <summary>
/// The run-time modes of NeoFences and what they mean together (M5 quick-hide, M6a Pause and game mode, ADR-021).
/// None of them is saved: after a restart only Takeover (a setting) applies again.
/// </summary>
public sealed record RunState(bool Takeover, bool QuickHidden, bool Paused, bool GameMode)
{
    public bool FencesVisible => !Paused && !QuickHidden;

    /// <summary>Pause gives the desktop back to Windows: its icons show even with Takeover on (spec §6).</summary>
    public bool IconsHidden => !Paused && (Takeover || QuickHidden);

    /// <summary>The only global hook goes away while paused or gaming (hard rule 3, spec §4.7).</summary>
    public bool MouseHookWanted => !Paused && !GameMode;

    /// <summary>Desktop and Portal changes wait until the game is left (spec §4.7).</summary>
    public bool ShellWorkDeferred => GameMode;
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test NeoFences.slnx`
Expected: `Passed! - Failed: 0, Passed: 210`

- [ ] **Step 6: Commit**

```powershell
git add src/NeoFences.Core tests/NeoFences.Core.Tests
git commit -m "feat: added game mode policy and run state rules to core"
```

---

### Task 2: Shell — game detection, tray, session notices, hook hardening

**Files:**
- Create: `src/NeoFences.Shell/GameDetection.cs`, `src/NeoFences.Shell/TrayIcon.cs`
- Modify (full new content): `src/NeoFences.Shell/DesktopMouseHook.cs`, `NativeMethods.txt`

**Interfaces:**
- Consumes: Task 1 (`ForegroundSnapshot`, `NotificationState`).
- Produces:
  - `GameDetection.TakeSnapshot()`;
  - `ForegroundWatcher(Action onForegroundChanged, Action<Exception> logFailure)` with `IsWatching`;
  - `TrayIcon(nint windowHandle, string tooltip, Action<string> log)`, with `Show()`, `SetTooltip`, `CallbackMessage` and `IsMenuRequest(wParam, lParam, out x, out y)`;
  - `TrayMenu.Show(nint owner, IReadOnlyList<TrayMenuItem>, x, y) -> int`;
  - `record TrayMenuItem(int Id, string Text, bool Checked, bool Enabled)` with `Separator`;
  - `SessionNotifications.Register` / `Unregister` / `IsUnlock` / `WmSessionChange`;
  - `DesktopWindows.ClassOf` is now internal.

- [ ] **Step 1: Files**

`src/NeoFences.Shell/NativeMethods.txt`:
```
// Win32 APIs used by NeoFences.Shell. CsWin32 generates bindings for each name.
AllowSetForegroundWindow
AppendMenu
BHID_SFObject
BITMAP
BITMAPINFO
CallNextHookEx
CLIPBOARD_FORMAT
CLSID_DragDropHelper
CMF_CANRENAME
CMF_EXTENDEDVERBS
CMF_NORMAL
CMIC_MASK_PTINVOKE
CMINVOKECOMMANDINFOEX
CreatePopupMenu
CreateRoundRectRgn
CUIAutomation
DeleteObject
DestroyIcon
DestroyMenu
DIB_USAGE
DISPLAY_DEVICEW
DragQueryFile
DROPEFFECT
DVASPECT
EnumDisplayDevices
EnumDisplayMonitors
EVENT_SYSTEM_FOREGROUND
FileOpenDialog
FILEOPENDIALOGOPTIONS
FileOperation
FILEOPERATION_FLAGS
FindWindow
FOLDERFLAGS
FORMATETC
GCS_VERBW
GET_ANCESTOR_FLAGS
GET_WINDOW_CMD
GetAncestor
GetClassName
GetCurrentThreadId
GetCursorPos
GetDC
GetDIBits
GetDoubleClickTime
GetDpiForMonitor
GetDpiForSystem
GetForegroundWindow
GetMessage
GetModuleHandle
GetMonitorInfo
GetObject
GetSystemMetrics
GetSystemMetricsForDpi
GetWindow
GetWindowLongPtr
GetWindowRect
GetWindowText
GetWindowThreadProcessId
HDROP
HOT_KEY_MODIFIERS
HWND_BOTTOM
HWND_NOTOPMOST
HWND_TOPMOST
IContextMenu
IContextMenu2
IContextMenu3
IDataObject
IDropTarget
IDropTargetHelper
IFileOpenDialog
IFileOperation
IFolderView2
INPUT
IServiceProvider
IShellBrowser
IShellFolder
IShellItem
IShellItemImageFactory
IShellView
IShellWindows
IUIAutomation
IUIAutomationElement
LoadImage
MODIFIERKEYS_FLAGS
MONITOR_DPI_TYPE
MONITORINFOEXW
MONITORINFOF_PRIMARY
MSLLHOOKSTRUCT
NIN_SELECT
NOTIFY_FOR_THIS_SESSION
NOTIFY_ICON_MESSAGE
NOTIFYICON_VERSION_4
NOTIFYICONDATAW
POINTL
PostMessage
PostThreadMessage
RegisterDragDrop
RegisterHotKey
RegisterWindowMessage
ReleaseDC
ReleaseStgMedium
RevokeDragDrop
SendInput
SET_WINDOW_POS_FLAGS
SetForegroundWindow
SetWindowLongPtr
SetWindowPos
SetWindowRgn
SetWindowsHookEx
SetWinEventHook
SFGAO_FLAGS
SHCreateItemFromParsingName
SHDoDragDrop
Shell_NotifyIcon
ShellWindows
SHGetDesktopFolder
SHOW_WINDOW_CMD
SHQueryUserNotificationState
SID_STopLevelBrowser
SIGDN
SIIGBF
STGMEDIUM
SYSTEM_METRICS_INDEX
TRACK_POPUP_MENU_FLAGS
TrackPopupMenuEx
TYMED
UIA_CONTROLTYPE_ID
UnhookWinEvent
UnregisterHotKey
WINDOW_EX_STYLE
WINDOW_LONG_PTR_INDEX
WINDOW_STYLE
WindowFromPoint
WINDOWPOS
WINDOWS_HOOK_ID
WINEVENT_OUTOFCONTEXT
WM_APP
WM_CONTEXTMENU
WM_LBUTTONDOWN
WM_MOUSEMOVE
WM_NULL
WM_QUIT
WM_RBUTTONDOWN
WM_RBUTTONUP
WM_WTSSESSION_CHANGE
WTS_SESSION_UNLOCK
WTSRegisterSessionNotification
WTSUnRegisterSessionNotification
```
`src/NeoFences.Shell/GameDetection.cs`:
```csharp
using NeoFences.Core.Lifecycle;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace NeoFences.Shell;

/// <summary>Reads what game mode decides on (spec §4.7, ADR-021): the notification state and the foreground window.</summary>
public static class GameDetection
{
    public static unsafe ForegroundSnapshot TakeSnapshot()
    {
        var notifications = PInvoke.SHQueryUserNotificationState(out var state).Succeeded ? (NotificationState)(int)state : NotificationState.Unknown;
        var foreground = PInvoke.GetForegroundWindow();
        uint processId = 0;
        if (!foreground.IsNull) PInvoke.GetWindowThreadProcessId(foreground, &processId);
        return new ForegroundSnapshot(notifications, foreground.IsNull ? "" : DesktopWindows.ClassOf(foreground), processId == (uint)Environment.ProcessId);
    }
}

/// <summary>
/// Raises <see cref="ForegroundWatcher"/>'s callback whenever another window comes to the front: an out-of-context
/// WinEvent hook (hard rule 3), delivered through the message loop of the thread that created it (the UI thread).
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    private readonly WINEVENTPROC _callback; // the field keeps the delegate alive while the hook exists
    private readonly UnhookWinEventSafeHandle _hook;

    public bool IsWatching => !_hook.IsInvalid;

    public ForegroundWatcher(Action onForegroundChanged, Action<Exception> logFailure)
    {
        _callback = (_, _, _, _, _, _, _) =>
        {
            try
            {
                onForegroundChanged();
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                logFailure(failure); // never unwind into Windows' event delivery
            }
        };
        _hook = PInvoke.SetWinEventHook(PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND, null, _callback, 0, 0, PInvoke.WINEVENT_OUTOFCONTEXT);
    }

    public void Dispose() => _hook.Dispose();
}
```
`src/NeoFences.Shell/TrayIcon.cs`:
```csharp
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Shell;

/// <summary>
/// The NeoFences icon in the notification area (spec §6, M6a). Its clicks arrive at <c>windowHandle</c> as
/// <see cref="CallbackMessage"/>. Explorer forgets every tray icon when it restarts: call <see cref="Show"/> again on
/// TaskbarCreated.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    public static readonly uint CallbackMessage = PInvoke.WM_APP + 2;
    private const uint IconId = 1;
    private const int ApplicationIconResource = 32512; // .NET's ApplicationIcon resource id, also IDI_APPLICATION
    private const uint KeySelect = PInvoke.NIN_SELECT | 1; // NIN_KEYSELECT (NINF_KEY), not in the metadata

    private readonly nint _windowHandle;
    private readonly Action<string> _log;
    private readonly HICON _icon;
    private bool _iconShared; // Windows' own icon (LR_SHARED): never destroyed by us
    private string _tooltip;

    public TrayIcon(nint windowHandle, string tooltip, Action<string> log)
    {
        _windowHandle = windowHandle;
        _tooltip = tooltip;
        _log = log;
        _icon = LoadSmallIcon();
        Show();
    }

    /// <summary>Adds the icon (again: after an Explorer restart). Never throws; a missing icon only loses the tray.</summary>
    public void Show()
    {
        var data = Data();
        if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, in data))
        {
            // Already there (Explorer did not restart after all): refresh it instead.
            if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, in data)) _log("tray: icon could not be added");
            return;
        }
        data.uVersion = PInvoke.NOTIFYICON_VERSION_4; // clicks as NIN_SELECT / WM_CONTEXTMENU with the point in wParam
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, in data);
    }

    public void SetTooltip(string tooltip)
    {
        _tooltip = tooltip;
        var data = Data();
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, in data);
    }

    /// <summary>
    /// A click on the icon (left, right or keyboard) asks for the menu (user choice 2026-10-03: left-click opens it too).
    /// Reads a <see cref="CallbackMessage"/>: the event in lParam's low word, the screen point in wParam.
    /// </summary>
    public static bool IsMenuRequest(nint wParam, nint lParam, out int screenX, out int screenY)
    {
        var trayEvent = (uint)(lParam & 0xFFFF);
        screenX = (short)(wParam & 0xFFFF);
        screenY = (short)((wParam >> 16) & 0xFFFF);
        return trayEvent is PInvoke.NIN_SELECT or KeySelect or PInvoke.WM_CONTEXTMENU;
    }

    private unsafe NOTIFYICONDATAW Data()
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = (HWND)_windowHandle,
            uID = IconId,
            uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
        };
        var tip = _tooltip.AsSpan(0, Math.Min(_tooltip.Length, 127));
        tip.CopyTo(data.szTip.AsSpan());
        data.szTip[tip.Length] = '\0';
        return data;
    }

    /// <summary>
    /// The exe's own icon at the tray's size for the system DPI; Windows' default app icon if it has none. LoadImage, not
    /// LoadIconMetric: that one lives in comctl32 v6, which a WPF app does not load (it crashed the M6a prototype).
    /// </summary>
    private unsafe HICON LoadSmallIcon()
    {
        var size = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, PInvoke.GetDpiForSystem());
        var module = PInvoke.GetModuleHandle((PCWSTR)null);
        var icon = PInvoke.LoadImage((HINSTANCE)(nint)module, (PCWSTR)(char*)ApplicationIconResource, GDI_IMAGE_TYPE.IMAGE_ICON, size, size, 0);
        if (!icon.IsNull) return (HICON)(nint)icon;
        _log("tray: the exe has no icon; using Windows' default");
        _iconShared = true;
        return (HICON)(nint)PInvoke.LoadImage(HINSTANCE.Null, (PCWSTR)(char*)ApplicationIconResource, GDI_IMAGE_TYPE.IMAGE_ICON, size, size, IMAGE_FLAGS.LR_SHARED);
    }

    public void Dispose()
    {
        var data = Data();
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, in data);
        if (!_icon.IsNull && !_iconShared) PInvoke.DestroyIcon(_icon);
    }
}

/// <summary>One tray-menu entry; <see cref="Id"/> 0 is a separator.</summary>
public sealed record TrayMenuItem(int Id, string Text = "", bool Checked = false, bool Enabled = true)
{
    public static readonly TrayMenuItem Separator = new(0);
}

/// <summary>The tray menu: Windows' own popup menu (it closes reliably when the user clicks elsewhere).</summary>
public static class TrayMenu
{
    /// <returns>The chosen item's id, or 0 when the menu was dismissed.</returns>
    public static unsafe int Show(nint ownerHandle, IReadOnlyList<TrayMenuItem> items, int screenX, int screenY)
    {
        var menu = PInvoke.CreatePopupMenu();
        if (menu.IsNull) return 0;
        try
        {
            foreach (var item in items)
            {
                if (item.Id == 0)
                {
                    PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (PCWSTR)null);
                    continue;
                }
                var flags = MENU_ITEM_FLAGS.MF_STRING | (item.Checked ? MENU_ITEM_FLAGS.MF_CHECKED : 0) | (item.Enabled ? 0 : MENU_ITEM_FLAGS.MF_GRAYED);
                fixed (char* text = item.Text) PInvoke.AppendMenu(menu, flags, (nuint)item.Id, text);
            }
            // The documented tray-menu dance: foreground first, so a click elsewhere closes the menu; WM_NULL after.
            PInvoke.SetForegroundWindow((HWND)ownerHandle);
            var chosen = PInvoke.TrackPopupMenuEx(menu,
                (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON | TRACK_POPUP_MENU_FLAGS.TPM_BOTTOMALIGN),
                screenX, screenY, (HWND)ownerHandle, null);
            PInvoke.PostMessage((HWND)ownerHandle, PInvoke.WM_NULL, 0, 0);
            return chosen.Value;
        }
        finally
        {
            PInvoke.DestroyMenu(menu);
        }
    }
}

/// <summary>Session lock/unlock notices (WM_WTSSESSION_CHANGE) for a window: the mouse hook is re-installed on unlock.</summary>
public static class SessionNotifications
{
    public const int WmSessionChange = 0x02B1;

    public static bool IsUnlock(nint wParam) => (uint)wParam == PInvoke.WTS_SESSION_UNLOCK;

    public static bool Register(nint windowHandle) => PInvoke.WTSRegisterSessionNotification((HWND)windowHandle, PInvoke.NOTIFY_FOR_THIS_SESSION);

    public static void Unregister(nint windowHandle) => PInvoke.WTSUnRegisterSessionNotification((HWND)windowHandle);
}
```
`src/NeoFences.Shell/DesktopMouseHook.cs`:
```csharp
using NeoFences.Core.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Shell;

/// <summary>
/// Desktop gestures (spec §4.5, M5): a <c>WH_MOUSE_LL</c> hook on its own thread with its own message loop — the only
/// global hook NeoFences uses (hard rule 3; game mode removes it in M6). It acts only when the pointer is over the
/// desktop itself (not a fence or an app):
/// <list type="bullet">
/// <item>double-click → <see cref="DesktopGesture.DoubleClick"/> (quick-hide);</item>
/// <item>right-drag of 8 px or more → <see cref="DesktopGesture.RightDragStarted"/> / <see cref="DesktopGesture.RightDragCompleted"/>
/// (draw a fence). Every desktop right-press is swallowed and, if it was a plain click, replayed so Explorer's desktop
/// menu still appears (S2, proven in M0; S1 broke Explorer's desktop state).</item>
/// </list>
/// While <see cref="PeekActive"/>, a click outside NeoFences' windows raises <see cref="PeekClickOutside"/> (and goes through).
/// Callbacks run on the hook thread: marshal to the UI thread and return fast.
/// </summary>
public sealed class DesktopMouseHook : IDisposable
{
    /// <summary>dwExtraInfo stamped on the replayed right-click so the hook lets it through ("NFNC").</summary>
    private const nuint ReplayMarker = 0x4E464E43;

    private static readonly uint ReplayRightClickMessage = PInvoke.WM_APP + 1;

    private readonly HOOKPROC _hookCallback; // the field keeps the delegate alive while the hook exists
    private readonly DesktopGestureTracker _tracker;
    private readonly Action<DesktopGesture, int, int> _onGesture;
    private readonly Action _onPeekClickOutside;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private uint _threadId;
    private bool _swallowedRightDown;
    private long _dragPoint; // latest pointer position during a right-drag, packed (x << 32 | y), read by the overlay

    /// <summary>Set by the UI while fences are shown over windows (Peek).</summary>
    public volatile bool PeekActive;

    public bool IsInstalled { get; private set; }

    /// <summary>The pointer during a right-drag (screen pixels), for the draw-a-fence overlay.</summary>
    public (int X, int Y) DragPoint
    {
        get
        {
            var packed = Interlocked.Read(ref _dragPoint);
            return ((int)(packed >> 32), (int)(packed & 0xFFFFFFFF));
        }
    }

    public DesktopMouseHook(Action<DesktopGesture, int, int> onGesture, Action onPeekClickOutside, Action<string> log)
    {
        _onGesture = onGesture;
        _onPeekClickOutside = onPeekClickOutside;
        _hookCallback = OnLowLevelMouse;
        _tracker = new DesktopGestureTracker(
            doubleClickMilliseconds: PInvoke.GetDoubleClickTime(),
            doubleClickSlopPixels: PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXDOUBLECLK) / 2,
            dragThresholdPixels: 8);
        // Highest: Windows silently drops a low-level hook whose callback misses LowLevelHooksTimeout, which a CPU-heavy
        // game can cause at normal priority (M5 review). The callback itself is short.
        _thread = new Thread(() => RunHookLoop(log)) { IsBackground = true, Name = "NeoFences.MouseHook", Priority = ThreadPriority.Highest };
        _thread.Start();
        _started.Wait(TimeSpan.FromSeconds(5));
    }

    private void RunHookLoop(Action<string> log)
    {
        _threadId = PInvoke.GetCurrentThreadId();
        using var module = PInvoke.GetModuleHandle((string?)null);
        using var hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _hookCallback, module, 0);
        IsInstalled = !hook.IsInvalid;
        log(IsInstalled ? "desktop gestures: WH_MOUSE_LL installed" : "desktop gestures: WH_MOUSE_LL could not be installed");
        _started.Set();
        if (!IsInstalled) return;

        while (PInvoke.GetMessage(out MSG message, HWND.Null, 0, 0) > 0)
        {
            if (message.message == ReplayRightClickMessage) ReplayRightClick();
        }
        log("desktop gestures: WH_MOUSE_LL removed");
    }

    private unsafe LRESULT OnLowLevelMouse(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code < 0) return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
        try
        {
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
            if (action == MouseAction.Move && _tracker.DragStart is not null)
                Interlocked.Exchange(ref _dragPoint, ((long)point.X << 32) | (uint)point.Y);

            // WindowFromPoint only on button events: moves arrive at up to 1000 Hz and must stay cheap.
            var overDesktop = action != MouseAction.Move && DesktopWindows.IsOverDesktop(point);
            if (PeekActive && action is MouseAction.LeftDown or MouseAction.RightDown && !DesktopWindows.IsOverNeoFences(point))
                _onPeekClickOutside();

            var gesture = _tracker.OnMouse(action.Value, point.X, point.Y, hookData->time, overDesktop);
            // RightDragStarted reports where the drag began (the press), every other gesture the pointer now.
            var (gestureX, gestureY) = gesture == DesktopGesture.RightDragStarted && _tracker.DragStart is { } start ? start : (point.X, point.Y);
            if (gesture != DesktopGesture.None) _onGesture(gesture, gestureX, gestureY);

            // S2: swallow every desktop right-press; replay it as a plain right-click if it did not become a drag.
            if (action == MouseAction.RightDown && overDesktop)
            {
                Interlocked.Exchange(ref _dragPoint, ((long)point.X << 32) | (uint)point.Y);
                _swallowedRightDown = true;
                return (LRESULT)1;
            }
            if (action == MouseAction.RightDown) _swallowedRightDown = false; // a lost right-up must not swallow an app's next one (M5 review I2)
            if (action == MouseAction.RightUp && _swallowedRightDown)
            {
                _swallowedRightDown = false;
                // SendInput re-enters low-level hooks, so replay from the message loop, not inside this callback.
                if (gesture != DesktopGesture.RightDragCompleted) PInvoke.PostThreadMessage(_threadId, ReplayRightClickMessage, 0, 0);
                return (LRESULT)1;
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // Never let an exception unwind into Windows' input pipeline: drop the gesture, keep the mouse working.
        }
        return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
    }

    private static void ReplayRightClick()
    {
        // The press was on the desktop; if the pointer slid onto a fence or an app before the release (a slip under the
        // drag threshold), a replay there would open that window's menu instead: drop it (M5 review M7).
        PInvoke.GetCursorPos(out var pointer);
        if (!DesktopWindows.IsOverDesktop(pointer)) return;
        Span<INPUT> inputs =
        [
            new INPUT { type = INPUT_TYPE.INPUT_MOUSE, Anonymous = { mi = new MOUSEINPUT { dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN, dwExtraInfo = ReplayMarker } } },
            new INPUT { type = INPUT_TYPE.INPUT_MOUSE, Anonymous = { mi = new MOUSEINPUT { dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP, dwExtraInfo = ReplayMarker } } },
        ];
        PInvoke.SendInput(inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
    }

    public void Dispose()
    {
        if (_threadId != 0) PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(1));
        _started.Dispose();
    }
}

/// <summary>What is under a screen point: the desktop itself, or a NeoFences fence.</summary>
public static class DesktopWindows
{
    internal static unsafe string ClassOf(HWND hwnd)
    {
        char* buffer = stackalloc char[256];
        var length = PInvoke.GetClassName(hwnd, buffer, 256);
        return new string(buffer, 0, length);
    }

    private static HWND RootAt(System.Drawing.Point screenPoint) =>
        PInvoke.GetAncestor(PInvoke.WindowFromPoint(screenPoint), GET_ANCESTOR_FLAGS.GA_ROOT);

    /// <summary>Progman (24H2+ icons host) or WorkerW (older icons host, wallpaper layer): the desktop itself.</summary>
    public static bool IsOverDesktop(System.Drawing.Point screenPoint) => ClassOf(RootAt(screenPoint)) is "Progman" or "WorkerW";

    /// <summary>
    /// A NeoFences window is under the point: a fence, or one of its menus (WPF and shell menus are top-level windows
    /// of our process). By process id, not title: GetWindowText on our own windows sends WM_GETTEXT to the UI thread,
    /// which would block the global mouse inside the hook whenever the UI is busy (M5 review I1).
    /// </summary>
    public static unsafe bool IsOverNeoFences(System.Drawing.Point screenPoint)
    {
        uint processId;
        PInvoke.GetWindowThreadProcessId(RootAt(screenPoint), &processId);
        return processId == (uint)Environment.ProcessId;
    }

    /// <summary>
    /// A visible native desktop icon is under the point (UI Automation list item). A double-click there opens the
    /// icon, so it must not also quick-hide. Cross-process COM: call from the UI thread, never inside the hook.
    /// </summary>
    public static bool IsOverDesktopIcon(int x, int y, Action<string> log)
    {
        Windows.Win32.UI.Accessibility.IUIAutomation? automation = null;
        Windows.Win32.UI.Accessibility.IUIAutomationElement? element = null;
        try
        {
            automation = (Windows.Win32.UI.Accessibility.IUIAutomation)Activator.CreateInstance(
                Type.GetTypeFromCLSID(typeof(Windows.Win32.UI.Accessibility.CUIAutomation).GUID)!)!;
            element = automation.ElementFromPoint(new System.Drawing.Point(x, y));
            return element.CurrentControlType == Windows.Win32.UI.Accessibility.UIA_CONTROLTYPE_ID.UIA_ListItemControlTypeId;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            log($"desktop gestures: icon hit-test failed ({failure.Message}); treating the point as empty desktop");
            return false;
        }
        finally
        {
            if (element is not null) System.Runtime.InteropServices.Marshal.ReleaseComObject(element);
            if (automation is not null) System.Runtime.InteropServices.Marshal.ReleaseComObject(automation);
        }
    }
}
```

- [ ] **Step 2: Build and test** (stop a running NeoFences first, or its DLL locks fail the copy: `& <exe> --exit`)

Run: `dotnet build NeoFences.slnx`, then `dotnet test NeoFences.slnx`
Expected: 0 warnings, 0 errors; `Passed: 210`.

- [ ] **Step 3: Commit**

```powershell
git add src/NeoFences.Shell
git commit -m "feat: added game detection, tray icon and menu, session notices and mouse hook hardening to shell"
```

---

### Task 3: App — icon, tray menu, Pause, game mode

**Files:**
- Create: `src/NeoFences.App/NeoFences.ico`, generated by the script below.
- Replace: `NeoFences.App.csproj`, `SystemMessageWindow.cs`, `PortalState.cs` and `FenceHost.cs`.

**Interfaces:**
- Consumes: Tasks 1 and 2.
- Produces:
  - `SystemMessageWindow.TrayMenuRequested(int, int)` and `SessionUnlocked`;
  - `PortalState.SetPaused(bool)`.

- [ ] **Step 1: Generate the icon**

Save as `<scratchpad>\make-icon.ps1` and run `powershell -NoProfile -ExecutionPolicy Bypass -File "<scratchpad>\make-icon.ps1"`:
```powershell
# Builds src/NeoFences.App/NeoFences.ico: a rounded blue tile with three white "fence" panels (title strip + body), PNG frames 16–256 px.
param([string]$Out = "F:\projects\neo_fences\src\NeoFences.App\NeoFences.ico", [string]$Preview = "$env:TEMP\neofences-icon-preview.png")
Add-Type -AssemblyName System.Drawing
function RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = [Math]::Max(1, 2 * $r)
  $path.AddArc($x, $y, $d, $d, 180, 90); $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
  $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90); $path.CloseFigure(); $path }
function Frame([int]$size) {
  $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bitmap); $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
  $s = [single]$size
  $tile = RoundedPath 0 0 ($s - 0.5) ($s - 0.5) ($s * 0.22)
  $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $s, $s), ([System.Drawing.Color]::FromArgb(255, 56, 132, 230)), ([System.Drawing.Color]::FromArgb(255, 28, 78, 168))
  $g.FillPath($brush, $tile)
  $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(245, 255, 255, 255))
  $veil = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(150, 255, 255, 255))
  $m = $s * 0.16; $gap = $s * 0.08; $r = [Math]::Max(1, $s * 0.07)
  # Left fence: tall. Right: two stacked fences (three fences on a desktop, not a pause sign).
  $leftW = ($s - 2 * $m - $gap) * 0.5; $rightX = $m + $leftW + $gap
  $fullH = $s - 2 * $m; $topH = $fullH * 0.55; $bottomY = $m + $topH + $gap; $bottomH = $fullH - $topH - $gap
  $panels = @((,@($m, $m, $leftW, $fullH)) + (,@($rightX, $m, $leftW, $topH)) + (,@($rightX, $bottomY, $leftW, $bottomH)))
  foreach ($panel in $panels) {
    $body = RoundedPath $panel[0] $panel[1] $panel[2] $panel[3] $r
    $g.FillPath($veil, $body)
    $titleH = [Math]::Max(2, $panel[3] * 0.26)
    $g.SetClip($body); $g.FillRectangle($white, $panel[0], $panel[1], $panel[2], $titleH); $g.ResetClip() }
  $g.Dispose(); $bitmap }
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$frames = foreach ($size in $sizes) { $bitmap = Frame $size; $stream = New-Object System.IO.MemoryStream; $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png); if ($size -eq 256) { $bitmap.Save($Preview) }; $bitmap.Dispose(); ,$stream.ToArray() }
$file = New-Object System.IO.MemoryStream; $writer = New-Object System.IO.BinaryWriter $file
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
  $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
  $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset); $offset += $frames[$i].Length }
foreach ($frame in $frames) { $writer.Write($frame) }
[System.IO.File]::WriteAllBytes($Out, $file.ToArray())
"wrote $Out ($((Get-Item $Out).Length) bytes)"
```
Expected: `wrote F:\projects\neo_fences\src\NeoFences.App\NeoFences.ico (~16700 bytes)`. Look at `%TEMP%\neofences-icon-preview.png`: a blue tile with one tall panel on the left and two stacked panels on the right.

- [ ] **Step 2: Files**

`src/NeoFences.App/NeoFences.App.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <!-- Windows 10 1809+ only (spec). A versioned TFM would pull in the WinRT projections, so silence the 8.1-API check instead. -->
    <NoWarn>$(NoWarn);CA1416</NoWarn>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AssemblyName>NeoFences</AssemblyName>
    <!-- Resource 32512: the tray icon loads it from the exe (M6a). -->
    <ApplicationIcon>NeoFences.ico</ApplicationIcon>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Serilog" Version="4.4.0" />
    <PackageReference Include="Serilog.Sinks.File" Version="7.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\NeoFences.Core\NeoFences.Core.csproj" />
    <ProjectReference Include="..\NeoFences.Shell\NeoFences.Shell.csproj" />
  </ItemGroup>

</Project>
```
`src/NeoFences.App/SystemMessageWindow.cs`:
```csharp
using System.Runtime.InteropServices;
using System.Windows.Interop;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>
/// Hidden top-level window that receives system broadcasts: Explorer restarts (TaskbarCreated), display or
/// work-area changes, and light/dark mode switches. Message-only windows do not get broadcasts, so this is an
/// invisible normal window.
/// It also receives global hotkeys (WM_HOTKEY), the tray icon's clicks and session lock/unlock notices.
/// Sign-out / shutdown is WPF's job (Application.SessionEnding, ADR-013), not this window's.
/// </summary>
public sealed class SystemMessageWindow : IDisposable
{
    private const int WmSettingChange = 0x001A;
    private const int WmDisplayChange = 0x007E;
    private const int WmDpiChanged = 0x02E0;
    private const int SpiSetWorkArea = 0x002F;

    private readonly HwndSource _source;

    public event Action? ExplorerRestarted;
    public event Action? DisplayChanged;
    public event Action? ThemeChanged;
    /// <summary>A RegisterHotKey hotkey of this window was pressed (its id).</summary>
    public event Action<int>? HotkeyPressed;
    /// <summary>The tray icon was clicked: show the tray menu at this screen point (px).</summary>
    public event Action<int, int>? TrayMenuRequested;
    /// <summary>The user signed back in from the lock screen (WM_WTSSESSION_CHANGE).</summary>
    public event Action? SessionUnlocked;

    /// <summary>Receives the global hotkeys (Peek, M5).</summary>
    public nint Handle => _source.Handle;

    public SystemMessageWindow()
    {
        _source = new HwndSource(new HwndSourceParameters("NeoFences.SystemMessages") { WindowStyle = 0, Width = 0, Height = 0 });
        _source.AddHook(OnMessage);
        SessionNotifications.Register(_source.Handle);
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        switch (message)
        {
            case GlobalHotkey.WmHotkey:
                HotkeyPressed?.Invoke((int)wParam);
                return 0;
            case SessionNotifications.WmSessionChange when SessionNotifications.IsUnlock(wParam):
                SessionUnlocked?.Invoke();
                return 0;
            case WmDisplayChange:
            case WmDpiChanged:
            case WmSettingChange when wParam == SpiSetWorkArea:
                DisplayChanged?.Invoke();
                return 0;
            // Light/dark switch: WM_SETTINGCHANGE with the string "ImmersiveColorSet".
            case WmSettingChange when lParam != 0 && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet":
                ThemeChanged?.Invoke();
                return 0;
        }
        if ((uint)message == DesktopHost.TaskbarCreatedMessage) ExplorerRestarted?.Invoke();
        if ((uint)message == TrayIcon.CallbackMessage && TrayIcon.IsMenuRequest(wParam, lParam, out var screenX, out var screenY))
        {
            TrayMenuRequested?.Invoke(screenX, screenY);
            handled = true;
        }
        return 0;
    }

    public void Dispose()
    {
        SessionNotifications.Unregister(_source.Handle);
        _source.Dispose();
    }
}
```
`src/NeoFences.App/PortalState.cs`:
```csharp
using System.IO;
using System.Windows.Threading;
using NeoFences.Core.Model;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>
/// One Portal fence at runtime (M4): the folder it mirrors, the subfolder it shows now (browsing is not saved; a restart
/// shows the Portal's own folder again), and its watcher. Listing and watcher setup run off the UI thread, since a
/// network or USB folder can block for seconds (M4 review I1); bursts of changes become one re-list at most every
/// 250 ms, even while a file keeps being written (I2); an unavailable or unwatched folder is retried every few
/// seconds, so a Portal comes back when its drive does (I4).
/// </summary>
public sealed class PortalState : IDisposable
{
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(7);

    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _retryTimer;
    private readonly Action<IReadOnlyList<ItemInfo>?> _show;
    private readonly Action<Exception> _logFailure;
    private FolderWatcher? _watcher;
    private HashSet<string> _listedFolders = new(StringComparer.OrdinalIgnoreCase);
    private int _generation;
    private bool _refreshing;   // a listing is running in the background (a network folder may take seconds)
    private bool _disposed;
    private bool _paused;        // game mode (M6a): changes wait
    private bool _missedChanges; // something changed while paused: re-list on resume

    public string Root { get; }

    public string Current { get; private set; }

    public bool CanGoBack => !string.Equals(Current, Root, StringComparison.OrdinalIgnoreCase);

    /// <param name="show">Called on the UI thread with the shown folder's items, or null when it cannot be read.</param>
    public PortalState(string root, Action<IReadOnlyList<ItemInfo>?> show, Action<Exception> logFailure)
    {
        Root = Normalize(root);
        Current = Root;
        _show = show;
        _logFailure = logFailure;
        _refreshTimer = new DispatcherTimer { Interval = RefreshDelay };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            Refresh();
        };
        _retryTimer = new DispatcherTimer { Interval = RetryDelay };
        _retryTimer.Tick += (_, _) => { if (!_refreshing && !_paused) Refresh(); }; // never pile up blocked listings
        Refresh();
    }

    /// <summary>"Downloads › Mods › Old" while browsing below the Portal's folder.</summary>
    public string Breadcrumb(string title)
    {
        var below = Path.GetRelativePath(Root, Current);
        return below == "." ? title : title + " › " + below.Replace("\\", " › ");
    }

    /// <summary>True when the last listing showed this ref as a folder (no disk access: a network folder may block).</summary>
    public bool IsListedFolder(string itemRef) => _listedFolders.Contains(itemRef);

    /// <summary>Shows a subfolder of the Portal (double-click on a folder inside it).</summary>
    public void Browse(string folder)
    {
        Current = Normalize(folder);
        Refresh();
    }

    /// <summary>One level up, never above the Portal's own folder.</summary>
    public void Back()
    {
        if (!CanGoBack) return;
        Current = Path.GetDirectoryName(Current) is { } parent ? Normalize(parent) : Root;
        Refresh();
    }

    /// <summary>
    /// Re-arms the watcher and re-lists the shown folder in the background; only the newest request is shown. The
    /// watcher is re-created each time: a folder deleted and created again ends the old one.
    /// </summary>
    public void Refresh()
    {
        if (_disposed) return;
        _refreshing = true;
        var generation = ++_generation;
        var folder = Current;
        var dispatcher = _refreshTimer.Dispatcher;
        Task.Run(() =>
        {
            var watcher = new FolderWatcher(folder, _logFailure);
            var items = FolderItems.TryList(folder);
            dispatcher.BeginInvoke(() =>
            {
                if (generation == _generation) _refreshing = false;
                if (_disposed || generation != _generation)
                {
                    watcher.Dispose(); // a newer refresh (or the fence's deletion) superseded this one
                    return;
                }
                _watcher?.Dispose();
                _watcher = watcher;
                watcher.Changed += () => dispatcher.BeginInvoke(ScheduleRefresh);
                _listedFolders = items is null
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(items.Where(item => item.IsFolder).Select(item => item.ItemRef), StringComparer.OrdinalIgnoreCase);
                // Unreadable or unwatched (drive not there yet): try again every few seconds until it is.
                if (items is null || !watcher.IsWatching) _retryTimer.Start();
                else _retryTimer.Stop();
                _show(items);
            });
        });
    }

    /// <summary>Game mode (spec §4.7): while paused, folder changes only mark the Portal stale; resuming re-lists once.</summary>
    public void SetPaused(bool paused)
    {
        _paused = paused;
        if (paused || !_missedChanges) return;
        _missedChanges = false;
        Refresh();
    }

    private void ScheduleRefresh()
    {
        if (_paused)
        {
            _missedChanges = true;
            return;
        }
        // Not restarted by every event: a file that keeps being written still refreshes the Portal every 250 ms.
        if (!_disposed && !_refreshTimer.IsEnabled) _refreshTimer.Start();
    }

    /// <summary>A path without a trailing separator, except a drive root ("D:\").</summary>
    private static string Normalize(string folder)
    {
        var trimmed = folder.TrimEnd('\\', '/');
        return trimmed.Length == 2 && trimmed[1] == ':' ? trimmed + "\\" : trimmed;
    }

    public void Dispose()
    {
        _disposed = true;
        _refreshTimer.Stop();
        _retryTimer.Stop();
        _watcher?.Dispose();
        _watcher = null;
    }
}
```
`src/NeoFences.App/FenceHost.cs`:
```csharp
using System.IO;
using System.Windows.Interop;
using System.Windows.Threading;
using NeoFences.Core.Config;
using NeoFences.Core.Input;
using NeoFences.Core.Layouts;
using NeoFences.Core.Lifecycle;
using NeoFences.Core.Membership;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Owns the fences on the desktop: loads the config, places one <see cref="FenceWindow"/> per fence on the current
/// monitors, keeps fence contents in step with the Desktop folders (reconcile at start, then watcher events),
/// saves changes (debounced 500 ms, ADR-006), and keeps everything attached through display changes,
/// Explorer restarts and sign-out (ADR-011, ADR-013). Every Win32/COM call goes through NeoFences.Shell.
/// </summary>
public sealed class FenceHost
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ReattachInterval = TimeSpan.FromMilliseconds(500);
    private const double SnapGapDips = 8;        // spec §6: 8 px spacing from other fences and screen edges
    private const double SnapThresholdDips = 12; // how close an edge must come before it snaps
    private const int ReattachAttempts = 10;
    private const int PeekHotkeyId = 1;
    private const int PeekEscapeHotkeyId = 2;
    private static readonly TimeSpan DrawFrame = TimeSpan.FromMilliseconds(15);

    private readonly ConfigStore _store = new(AppPaths.DataDirectory);
    private readonly Watchdog _watchdog = new(AppPaths.DataDirectory, message => Log.Information("watchdog: {Message}", message));
    private readonly SystemMessageWindow _messages = new();
    private readonly Dictionary<string, FenceWindow> _windows = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _saveTimer;
    private readonly IconLoader _iconLoader = new(Dispatcher.CurrentDispatcher);
    private DesktopWatcher? _desktopWatcher;
    // Safe-save memory and expected drop arrivals (FenceMembership.SafeSaveWindow).
    private IReadOnlyList<RememberedPlacement> _rememberedPlacements = [];
    private readonly Dictionary<string, IDisposable> _dropRegistrations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PortalState> _portals = new(StringComparer.Ordinal); // M4: Portal fences by id
    private NeoFencesConfig _config = NeoFencesConfig.CreateDefault();
    private IReadOnlyList<MonitorPlacement> _monitors = [];
    private bool _takeoverActive;
    private bool _lightTheme = SystemTheme.AppsUseLightTheme();
    private bool _sessionEnding;
    // M5 desktop gestures
    private DesktopMouseHook? _mouseHook;
    private bool _quickHidden;              // double-click on the desktop: fences (and icons) hidden until the next one
    private DrawFenceOverlay? _drawOverlay; // right-drag on the desktop: the fence being drawn
    private DispatcherTimer? _drawTimer;
    private (int X, int Y) _drawStart;
    private GlobalHotkey? _peekHotkey;
    private GlobalHotkey? _peekEscapeHotkey; // Esc ends Peek; registered only while peeking
    private bool _peeking;
    // M6a
    private bool _paused;                    // tray: Pause NeoFences (not saved)
    private bool _gameMode;                  // a full-screen app is in front: idle (spec §4.7, ADR-021)
    private ForegroundWatcher? _foregroundWatcher;
    private TrayIcon? _trayIcon;
    private readonly List<DesktopChange> _deferredDesktopChanges = [];
    private bool _reconcileDeferred;
    private const int TrayNewFence = 1, TrayQuickHide = 2, TrayPeek = 3, TrayPause = 4, TrayExit = 5;

    public event Action? ExitRequested;

    public FenceHost()
    {
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => SaveNow();
        _messages.ExplorerRestarted += OnExplorerRestarted;
        _messages.DisplayChanged += OnDisplayChanged;
        _messages.ThemeChanged += OnThemeChanged;
        _messages.HotkeyPressed += OnHotkey;
        _messages.TrayMenuRequested += ShowTrayMenu;
        _messages.SessionUnlocked += OnSessionUnlocked;
    }

    public void Start()
    {
        var loaded = _store.Load();
        Log.Information("config loaded from {Source} (read-only: {IsReadOnly}, corrupt copy: {CorruptCopyPath})",
            loaded.Source, loaded.IsReadOnly, loaded.CorruptCopyPath);
        _config = loaded.Config;
        _watchdog.LaunchDetached(Environment.ProcessId);
        ApplyStartup(); // after a power loss NeoFences must come back by itself (ADR-019)

        RefreshMonitors();
        foreach (var fence in _config.Fences) OpenWindow(fence);
        ReconcileDesktop();
        StartDesktopWatcher();
        ApplyLayout();
        if (_config.Settings.Takeover) SetTakeover(true);
        else if (_watchdog.IsTakeoverActiveMarked)
        {
            // Icons may still be hidden from a run whose Takeover-off never got saved (both processes killed): show them.
            Log.Warning("takeover-active marker found while Takeover is off; showing desktop icons");
            SetIconsHidden(false);
        }
        StartGestures();
        try
        {
            _trayIcon = new TrayIcon(_messages.Handle, TrayTooltip(), log: message => Log.Warning("{Message}", message));
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Error(failure, "tray icon unavailable; fences and gestures keep working"); // hard rule 7: only the tray is lost
        }
        StartGameMode();
        ScheduleSave();
    }

    /// <summary>All of NeoFences' run-time modes together (Core rules: which fences, icons and hooks they imply).</summary>
    private RunState Current => new(Takeover: _takeoverActive, QuickHidden: _quickHidden, Paused: _paused, GameMode: _gameMode);

    /// <summary>
    /// Windows asked to end the session (WPF's SessionEnding, inside WM_QUERYENDSESSION). WPF then shuts the app
    /// down, which may be the last thing that ever runs, so the icons come back now and the watchdog is told it is
    /// a session end, not a user exit: if the user cancels the shutdown, the watchdog restarts NeoFences (ADR-013).
    /// </summary>
    public void OnSessionEnding()
    {
        _sessionEnding = true;
        SaveNow();
        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) SetIconsHidden(false);
        TryMarker(() => _watchdog.MarkSessionEnding(Environment.ProcessId), what: "session-ending marker");
    }

    /// <summary>Orderly exit: save, bring icons back, tell the watchdog all is well.</summary>
    public void Shutdown()
    {
        SaveNow();
        if (_sessionEnding) return; // OnSessionEnding already restored and marked; no clean marker, so a cancel restarts us
        // The marker also catches a show that failed earlier (quick-hide ending while Explorer was busy; M5 review M6).
        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) SetIconsHidden(false);
        TryMarker(() => _watchdog.MarkCleanShutdown(Environment.ProcessId), what: "clean-shutdown marker");
        _trayIcon?.Dispose();
        _foregroundWatcher?.Dispose();
        _mouseHook?.Dispose();
        _peekHotkey?.Dispose();
        _peekEscapeHotkey?.Dispose();
        _desktopWatcher?.Dispose();
        _desktopWatcher = null;
        _iconLoader.Dispose();
        _messages.Dispose();
    }

    /// <summary>Best effort from the crash handler; the watchdog restores too.</summary>
    public void EmergencyRestoreIcons()
    {
        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) DesktopIcons.TrySetHidden(false);
    }

    private void OpenWindow(Fence fence)
    {
        var window = new FenceWindow(fence, takeoverActive: _takeoverActive, lightTheme: _lightTheme, iconLoader: _iconLoader);
        window.SnapRect = (rect, edges) => SnapFence(window, rect, edges);
        window.MovedByUser += OnFenceMoved;
        window.RenameRequested += title => RenameFence(window, title);
        window.IconSizeRequested += iconSize => SetFenceIconSize(window, iconSize);
        window.LockToggled += locked => SetFenceLocked(window, locked);
        window.DeleteRequested += () => DeleteFence(window);
        window.NewFenceRequested += CreateFence;
        window.TakeoverToggled += SetTakeover;
        window.ExitRequested += () => ExitRequested?.Invoke();
        window.OpenRequested += itemRef => OpenOrBrowse(window, itemRef);
        window.ItemMenuRequested += (itemRefs, screenX, screenY) => ShowItemMenu(window, itemRefs, screenX, screenY);
        window.RecycleRequested += itemRefs => RecycleItems(window, itemRefs);
        window.ItemRenameRequested += (itemRef, newName) => RenameItem(window, itemRef, newName);
        window.BackRequested += () => BrowsePortal(window, back: true);
        window.NewPortalRequested += () => CreatePortal(window);
        window.StartupToggled += SetStartWithWindows;
        window.SetStartupChecked(_config.Settings.StartWithWindows);
        window.SortRequested += sort => SortFence(window, sort);
        window.OpenFolderRequested += () => { if (_portals.TryGetValue(window.FenceId, out var portal)) OpenItem(portal.Current, ownerHandle: window.Handle); };
        if (fence.Source is { Kind: FenceSourceKind.Portal, Path: { } portalPath })
        {
            _portals[fence.Id] = new PortalState(portalPath, show: items => ShowPortal(window, items),
                logFailure: failure => Log.Warning(failure, "cannot watch Portal folder of {FenceId}", fence.Id));
        }
        window.DragRequested += itemRefs =>
            ShellDragDrop.TryDrag(window.Handle, itemRefs, logFailure: failure => Log.Warning(failure, "could not start dragging {ItemRefs}", itemRefs));
        window.TakeoverPromptAnswered += AnswerTakeoverPrompt;
        window.RollUpToggled += () => ToggleRollUp(window);
        new WindowInteropHelper(window).EnsureHandle(); // HWND exists (styles, blur) before the first Show
        RegisterDrops(window); // needs the HWND
        if (!DesktopHost.AttachToDesktop(window.Handle)) Log.Warning("fence {FenceId}: not attached to the desktop yet (no Progman)", fence.Id);
        _windows[fence.Id] = window;
    }

    /// <summary>Full pass over the Desktop folders: at start and whenever watcher events were lost.</summary>
    private void ReconcileDesktop()
    {
        var listing = DesktopItems.Enumerate();
        var (reconciled, report) = FenceMembership.Reconcile(_config, listing.ItemRefs, listing.UnavailableFolders);
        _config = reconciled;
        if (listing.UnavailableFolders.Count > 0) Log.Warning("desktop folders not readable: {Folders}", listing.UnavailableFolders);
        if (report.Suspicious) Log.Warning("kept fenced items from an unreadable or empty desktop listing until a later reconcile");
        Log.Information("desktop reconciled: {AddedCount} added to the Inbox, {RemovedCount} removed", report.AddedToInbox.Count, report.Removed.Count);
        RefreshWindows();
        ScheduleSave();
    }

    private void StartDesktopWatcher()
    {
        // A folder that cannot be watched degrades to "its changes show after a restart" (hard rule 7).
        _desktopWatcher = new DesktopWatcher((folder, failure) => Log.Error(failure, "cannot watch {Folder}; its changes show after a restart", folder));
        var dispatcher = Dispatcher.CurrentDispatcher;
        _desktopWatcher.Changed += change => dispatcher.BeginInvoke(() => OnDesktopChanged(change));
        _desktopWatcher.Overflowed += () => dispatcher.BeginInvoke(OnDesktopWatcherError);
    }

    /// <summary>Events were lost, or the watcher stopped (.NET disables it after a non-overflow error): start over.</summary>
    private void OnDesktopWatcherError()
    {
        if (_desktopWatcher is null) return; // shut down meanwhile
        Log.Warning("desktop watcher lost events or stopped; re-arming and reconciling");
        _desktopWatcher.Dispose();
        StartDesktopWatcher();
        if (Current.ShellWorkDeferred) _reconcileDeferred = true; // after the game
        else ReconcileDesktop();
    }

    private void OnDesktopChanged(DesktopChange change)
    {
        if (Current.ShellWorkDeferred)
        {
            _deferredDesktopChanges.Add(change); // applied in order when the game is left
            return;
        }
        (_config, _rememberedPlacements) = FenceMembership.Apply(_config, change, _rememberedPlacements, DateTimeOffset.Now);
        RefreshWindows();
        ScheduleSave();
    }

    private void RefreshWindows()
    {
        var showPrompt = !_config.Settings.TakeoverPromptAnswered && !_takeoverActive;
        foreach (var fence in _config.Fences)
        {
            if (!_windows.TryGetValue(fence.Id, out var window)) continue;
            if (!_portals.ContainsKey(fence.Id)) window.SetItems(fence.Items); // Portals refresh on their own (M4 review I1)
            window.ShowTakeoverPrompt(showPrompt && fence.IsInbox);
        }
    }

    private static void OpenItem(string itemRef, nint ownerHandle)
    {
        // Off the UI thread: ShellExecute can block on a network timeout or a UAC prompt, freezing every fence (M2b review I6).
        Task.Run(() =>
        {
            if (!ShellItems.TryOpen(itemRef, ownerHandle)) Log.Warning("could not open {ItemRef}", itemRef);
        });
    }

    /// <summary>Makes the fence accept drops (M3b): fence items and Desktop files move membership; other files go to Windows.</summary>
    private void RegisterDrops(FenceWindow window)
    {
        try
        {
            _dropRegistrations[window.FenceId] = ShellDragDrop.RegisterFence(window.Handle, new FenceDropHandlers(
                HitTest: window.HitTest,
                MoveItems: (itemRefs, insertAt) =>
                {
                    _config = FenceMembership.MoveItems(_config, itemRefs, window.FenceId, insertAt);
                    RefreshWindows();
                    ScheduleSave();
                },
                ExpectArrivals: (itemRefs, insertAt) =>
                    _rememberedPlacements = FenceMembership.ExpectArrivals(_rememberedPlacements, itemRefs, window.FenceId, insertAt, DateTimeOffset.Now),
                Recycle: itemRefs => RecycleItems(window, itemRefs),
                ShowFeedback: window.ShowDropFeedback,
                LogFailure: failure => Log.Warning(failure, "drop on fence {FenceId} failed", window.FenceId)),
                portalFolder: () => _portals.TryGetValue(window.FenceId, out var portal) ? portal.Current : null);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Error(failure, "fence {FenceId} cannot accept drops", window.FenceId); // degrade: everything else still works
        }
    }

    private void ShowItemMenu(FenceWindow window, IReadOnlyList<string> itemRefs, int screenX, int screenY)
    {
        var extended = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);
        var choice = ShellItemMenu.Show(window.Handle, itemRefs, screenX, screenY, extended,
            logFailure: failure => Log.Warning(failure, "item menu or its command failed for {ItemRefs}", itemRefs));
        switch (choice)
        {
            case ItemMenuChoice.Rename:
                window.BeginItemRename(itemRefs[0]);
                break;
            case ItemMenuChoice.Delete:
                RecycleItems(window, itemRefs); // always the Recycle Bin, even with Shift held (hard rule 1)
                break;
        }
    }

    /// <summary>Windows moves them to the Recycle Bin (with its own dialogs); the watcher then removes them from the fence.</summary>
    private static void RecycleItems(FenceWindow window, IReadOnlyList<string> itemRefs)
    {
        Log.Information("recycling {Count} item(s)", itemRefs.Count);
        var (started, refused) = ShellFileOps.TryRecycle(window.Handle, itemRefs);
        if (!started) Log.Warning("recycle did not run or was cancelled: {ItemRefs}", itemRefs);
        if (refused.Count == 0) return;
        Log.Information("not deleting {Count} item(s) on drives without a Recycle Bin", refused.Count);
        System.Windows.MessageBox.Show(window,
            (refused.Count == 1 ? $"\"{Path.GetFileName(refused[0])}\" is" : $"{refused.Count} items are") +
            " on a drive without a Recycle Bin (a USB stick or network drive), so NeoFences won't delete it." +
            " Deleting there would be permanent; if you really mean it, delete it in Explorer.",
            "NeoFences", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    /// <summary>Windows renames the file; the watcher's rename event keeps it in its fence and position.</summary>
    private static void RenameItem(FenceWindow window, string itemRef, string newName)
    {
        if (!ShellFileOps.TryRename(window.Handle, itemRef, newName)) Log.Warning("rename did not run or was cancelled: {ItemRef}", itemRef);
    }

    private void AnswerTakeoverPrompt(bool hideIcons)
    {
        Log.Information("first-run question answered: hide desktop icons {HideIcons}", hideIcons);
        if (hideIcons) SetTakeover(true);
        else
        {
            _config = _config with { Settings = _config.Settings with { TakeoverPromptAnswered = true } };
            RefreshWindows();
            SaveNow();
        }
    }

    private void ApplyLayout()
    {
        if (_monitors.Count == 0)
        {
            Log.Warning("no monitors reported; keeping the current layout");
            return;
        }
        try
        {
            var (resolved, layout) = LayoutEngine.Resolve(_config, _monitors.Select(monitor => monitor.ToDisplayMonitor()).ToList());
            _config = resolved;
            foreach (var (fenceId, rect) in layout.Fences)
            {
                if (!_windows.TryGetValue(fenceId, out var window)) continue;
                var monitor = _monitors.First(candidate => candidate.DeviceId == rect.Monitor);
                window.Place(FencePlacement.ToPixels(rect, monitor));
                if (!window.IsVisible && Current.FencesVisible)
                {
                    window.Show();
                    if (_peeking)
                    {
                        // A fence made during Peek (its menus no longer end it, M5 review I1) joins the others on top.
                        window.Peeking = true;
                        FenceWindowChrome.SetTopmost(window.Handle, topmost: true);
                    }
                    else FenceWindowChrome.SendToBack(window.Handle); // Show puts it above every app; fences live just above the desktop
                }
            }
        }
        catch (ArgumentException unusableDisplay)
        {
            // Garbage from a monitor query mid-change (M1 review): skip this resolve, the next display event retries.
            Log.Warning(unusableDisplay, "display query unusable; keeping the current layout");
        }
    }

    private void RefreshMonitors()
    {
        _monitors = Monitors.Enumerate();
        Log.Information("monitors: {Monitors}", string.Join("; ", _monitors.Select(monitor =>
            $"{monitor.DeviceId} {monitor.PixelWidth}x{monitor.PixelHeight}@{monitor.ScalePercent}% " +
            $"work {monitor.WorkLeftPx},{monitor.WorkTopPx} {monitor.WorkWidthPx}x{monitor.WorkHeightPx}{(monitor.IsPrimary ? " primary" : "")}")));
    }

    private void OnFenceMoved(FenceWindow window, PixelRect pixels)
    {
        if (_monitors.Count == 0 || _config.LastLayoutFingerprint is not { } fingerprint) return;
        var monitor = FencePlacement.ContainingMonitor(pixels, _monitors);
        _config = LayoutEngine.WithFenceRect(_config, fingerprint: fingerprint, fenceId: window.FenceId, rect: FencePlacement.FromPixels(pixels, monitor));
        ScheduleSave();
    }

    private PixelRect SnapFence(FenceWindow window, PixelRect rect, SnapEdges edges)
    {
        if (_monitors.Count == 0) return rect;
        var monitor = FencePlacement.ContainingMonitor(rect, _monitors);
        var others = _windows.Values.Where(other => other != window && other.IsVisible).Select(other => FenceWindowChrome.GetPixelRect(other.Handle)).ToList();
        return Snapping.Snap(rect, edges,
            workArea: new PixelRect(monitor.WorkLeftPx, monitor.WorkTopPx, monitor.WorkWidthPx, monitor.WorkHeightPx),
            others: others,
            gapPx: (int)Math.Round(SnapGapDips * monitor.Scale),
            thresholdPx: (int)Math.Round(SnapThresholdDips * monitor.Scale));
    }

    private void RenameFence(FenceWindow window, string title)
    {
        _config = FenceEdits.Rename(_config, window.FenceId, title);
        window.SetTitle(_config.Fences.First(fence => fence.Id == window.FenceId).Title);
        RefreshPortal(window); // a Portal browsing a subfolder shows its breadcrumb again
        ScheduleSave();
    }

    private void SetFenceIconSize(FenceWindow window, int iconSize)
    {
        _config = FenceEdits.SetIconSize(_config, window.FenceId, iconSize);
        window.SetIconSize(iconSize);
        ScheduleSave();
    }

    private void SetFenceLocked(FenceWindow window, bool locked)
    {
        _config = FenceEdits.SetLocked(_config, window.FenceId, locked);
        window.SetLocked(locked);
        ScheduleSave();
    }

    /// <summary>Removes the fence; its items go to the Inbox. Files are never touched (hard rule 1).</summary>
    private void DeleteFence(FenceWindow window)
    {
        _config = FenceMembership.DeleteFence(_config, window.FenceId);
        _windows.Remove(window.FenceId);
        if (_dropRegistrations.Remove(window.FenceId, out var registration)) registration.Dispose();
        if (_portals.Remove(window.FenceId, out var portal)) portal.Dispose(); // the folder itself is never touched
        window.Close();
        RefreshWindows();
        ScheduleSave();
    }

    private void OnThemeChanged()
    {
        var light = SystemTheme.AppsUseLightTheme();
        if (light == _lightTheme) return;
        _lightTheme = light;
        Log.Information("Windows app mode changed; light: {Light}", light);
        foreach (var window in _windows.Values) window.ApplyTheme(light);
    }

    private void OnDisplayChanged()
    {
        RefreshMonitors();
        ApplyLayout();
        ScheduleSave();
    }

    /// <summary>"New Portal fence…": Windows' folder dialog, then a fence that mirrors the folder (M4).</summary>
    private void CreatePortal(FenceWindow owner)
    {
        var folder = FolderPicker.TryPick(owner.Handle, "Choose the folder for the new Portal fence",
            logFailure: failure => Log.Warning(failure, "folder dialog failed"));
        if (folder is null) return;
        var title = Path.GetFileName(folder.TrimEnd('\\')) is { Length: > 0 } name ? name : folder;
        (_config, var portal) = FenceMembership.CreatePortal(_config, title: title, folderPath: folder);
        Log.Information("Portal fence created for {Folder}", folder);
        OpenWindow(portal);
        RefreshWindows();
        ApplyLayout();
        ScheduleSave();
    }

    /// <summary>Asks a Portal to re-list its folder (in the background; ShowPortal follows).</summary>
    private void RefreshPortal(FenceWindow window)
    {
        if (_portals.TryGetValue(window.FenceId, out var portal)) portal.Refresh();
    }

    /// <summary>Shows a Portal's listing (null: the folder cannot be read) in its sort order (M4).</summary>
    private void ShowPortal(FenceWindow window, IReadOnlyList<ItemInfo>? items)
    {
        if (!_portals.TryGetValue(window.FenceId, out var portal)) return;
        if (_config.Fences.FirstOrDefault(candidate => candidate.Id == window.FenceId) is not { } fence) return;
        window.SetPortalLocation(portal.Breadcrumb(fence.Title), portal.CanGoBack);
        window.SetSortChecked(fence.Sort);
        window.ShowPortalMessage(items is null ? $"This folder is not available right now:\n{portal.Current}" : null);
        window.SetItems(items is null ? [] : ItemSorting.Order(items, fence.Sort));
    }

    /// <summary>Double-click / Enter: inside a Portal a folder is browsed in place (Ctrl opens it in Explorer, user choice 2026-10-03).</summary>
    private void OpenOrBrowse(FenceWindow window, string itemRef)
    {
        var inExplorer = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control);
        if (_portals.TryGetValue(window.FenceId, out var portal) && !inExplorer && portal.IsListedFolder(itemRef))
        {
            portal.Browse(itemRef); // re-lists in the background
            return;
        }
        OpenItem(itemRef, ownerHandle: window.Handle);
        SetPeek(false); // like Fences: Peek ends once something is opened from it
    }

    private void BrowsePortal(FenceWindow window, bool back)
    {
        if (!back || !_portals.TryGetValue(window.FenceId, out var portal) || !portal.CanGoBack) return;
        portal.Back(); // re-lists in the background
    }

    /// <summary>"Sort by": a Portal keeps the order live; a desktop fence is sorted once (M4).</summary>
    private void SortFence(FenceWindow window, FenceSort sort)
    {
        var fence = _config.Fences.First(candidate => candidate.Id == window.FenceId);
        if (_portals.ContainsKey(fence.Id))
        {
            _config = FenceEdits.SetSort(_config, fence.Id, sort);
            RefreshPortal(window);
        }
        else
        {
            try
            {
                _config = FenceEdits.SetItemOrder(_config, fence.Id, ItemSorting.Order(FolderItems.Describe(fence.Items), sort));
            }
            catch (ArgumentException mismatch)
            {
                Log.Warning(mismatch, "sort of fence {FenceId} refused: the sorted list did not match its items", fence.Id); // never crash (M4 review I3)
                return;
            }
            RefreshWindows();
        }
        ScheduleSave();
    }

    private void ApplyStartup() =>
        StartupRegistration.Apply(_config.Settings.StartWithWindows, Environment.ProcessPath ?? "", log: message => Log.Information("{Message}", message));

    private void SetStartWithWindows(bool startWithWindows)
    {
        _config = _config with { Settings = _config.Settings with { StartWithWindows = startWithWindows } };
        ApplyStartup();
        foreach (var window in _windows.Values) window.SetStartupChecked(startWithWindows);
        SaveNow();
    }

    private void CreateFence()
    {
        (_config, var fence) = FenceMembership.CreateFence(_config, "New fence");
        OpenWindow(fence);
        RefreshWindows();
        ApplyLayout();
        ScheduleSave();
    }

    private void SetTakeover(bool active)
    {
        SetQuickHidden(false); // an explicit icons choice ends quick-hide first, so the two never disagree
        _takeoverActive = active;
        // Any explicit choice (banner or menu) answers the first-run question.
        _config = _config with { Settings = _config.Settings with { Takeover = active, TakeoverPromptAnswered = true } };
        SetIconsHidden(Current.IconsHidden);
        foreach (var window in _windows.Values) window.SetTakeoverChecked(active);
        RefreshWindows();
        SaveNow(); // not debounced: the saved setting must match the takeover-active marker if we are killed next
    }

    /// <returns>True when Windows confirmed the new state.</returns>
    private bool SetIconsHidden(bool hidden)
    {
        // The watchdog must know whenever icons may be hidden: mark before hiding, unmark only after a confirmed show.
        if (hidden) TryMarker(() => _watchdog.SetTakeoverActive(true), what: "takeover-active marker");
        var applied = DesktopIcons.TrySetHidden(hidden);
        if (applied && !hidden) TryMarker(() => _watchdog.SetTakeoverActive(false), what: "takeover-active marker");
        if (applied) Log.Information("desktop icons hidden: {Hidden}", hidden);
        else Log.Warning("could not set desktop icons hidden: {Hidden}", hidden);
        return applied;
    }

    /// <summary>Icons as they should be: hidden while Takeover is on, shown (and unmarked) otherwise.</summary>
    private bool EnsureIconState() =>
        Current.IconsHidden ? SetIconsHidden(true)
        : !_watchdog.IsTakeoverActiveMarked || SetIconsHidden(false);

    private static void TryMarker(Action writeMarker, string what)
    {
        try
        {
            writeMarker();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Error(failure, "could not update the {What}", what);
        }
    }

    private void OnExplorerRestarted()
    {
        Log.Information("Explorer restarted; re-attaching fences");
        SetPeek(false); // re-attaching sends fences to the bottom; Peek (and its global Esc) must end with it (M5 review M2)
        _trayIcon?.Show(); // Explorer forgot every tray icon
        ReinstallMouseHook(); // a hook Windows dropped silently comes back here at the latest (M5 review)
        var attempts = 0;
        var retryTimer = new DispatcherTimer { Interval = ReattachInterval };
        retryTimer.Tick += (_, _) =>
        {
            attempts++;
            var unattachedCount = _windows.Values.Count(window => !DesktopHost.AttachToDesktop(window.Handle));
            foreach (var window in _windows.Values) FenceWindowChrome.SendToBack(window.Handle);
            var iconsOk = EnsureIconState();
            if ((unattachedCount == 0 && iconsOk) || attempts >= ReattachAttempts)
            {
                retryTimer.Stop();
                Log.Information("re-attach after Explorer restart: {Attempts} attempt(s), unattached {UnattachedCount}, icons ok {IconsOk}",
                    attempts, unattachedCount, iconsOk);
            }
        };
        retryTimer.Start();
    }

    /// <summary>The WH_MOUSE_LL desktop gestures and the Peek hotkey (M5). Either failing only turns that feature off.</summary>
    private void StartGestures()
    {
        UpdateMouseHook();
        if (!Hotkey.TryParse(_config.Settings.PeekHotkey, out var hotkey) || !Enum.TryParse<System.Windows.Input.Key>(hotkey.Key, ignoreCase: true, out var key))
        {
            Log.Warning("Peek hotkey {PeekHotkey} is not valid; Peek is off", _config.Settings.PeekHotkey);
            return;
        }
        _peekHotkey = new GlobalHotkey(_messages.Handle, PeekHotkeyId);
        if (_peekHotkey.TryRegister(hotkey, (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(key))) Log.Information("Peek hotkey {Hotkey} registered", hotkey);
        else Log.Warning("Peek hotkey {Hotkey} is taken by another app; Peek is off", hotkey);
    }

    /// <summary>The hook exists exactly while it is wanted: not while paused or gaming (hard rule 3, spec §4.7).</summary>
    private void UpdateMouseHook()
    {
        if (Current.MouseHookWanted == _mouseHook is not null) return;
        if (_mouseHook is not null)
        {
            EndDrawOverlay();
            _mouseHook.Dispose();
            _mouseHook = null;
            return;
        }
        var dispatcher = Dispatcher.CurrentDispatcher;
        _mouseHook = new DesktopMouseHook(
            onGesture: (gesture, screenX, screenY) => dispatcher.BeginInvoke(() => OnDesktopGesture(gesture, screenX, screenY)),
            onPeekClickOutside: () => dispatcher.BeginInvoke(() => SetPeek(false)),
            log: message => Log.Information("{Message}", message));
    }

    /// <summary>Windows drops a low-level hook silently (LowLevelHooksTimeout): a fresh one after Explorer restarts and on unlock.</summary>
    private void ReinstallMouseHook()
    {
        if (_mouseHook is null) return; // not wanted now; it comes back when it is
        EndDrawOverlay();
        _mouseHook.Dispose();
        _mouseHook = null;
        UpdateMouseHook();
    }

    private void OnSessionUnlocked()
    {
        Log.Information("session unlocked; re-installing the mouse hook");
        ReinstallMouseHook();
        CheckGameMode();
    }

    /// <summary>Game mode (spec §4.7, ADR-021): checked on every foreground change, and again shortly after (the signal lags).</summary>
    private void StartGameMode()
    {
        _foregroundWatcher = new ForegroundWatcher(onForegroundChanged: OnForegroundChanged,
            logFailure: failure => Log.Error(failure, "game mode check failed"));
        if (!_foregroundWatcher.IsWatching) Log.Warning("game mode: foreground changes cannot be watched; game mode is off");
        CheckGameMode();
    }

    private void OnForegroundChanged()
    {
        CheckGameMode();
        foreach (var delay in GameModePolicy.RecheckDelays)
        {
            // ponytail: one short-lived timer per foreground change and delay; a coalescing timer if switching storms ever show up in a profile.
            var recheck = new DispatcherTimer { Interval = delay };
            recheck.Tick += (_, _) =>
            {
                recheck.Stop();
                CheckGameMode();
            };
            recheck.Start();
        }
    }

    private void CheckGameMode()
    {
        var gameMode = GameModePolicy.IsGameActive(enabled: _config.Settings.GameMode, foreground: GameDetection.TakeSnapshot());
        if (gameMode == _gameMode) return;
        _gameMode = gameMode;
        Log.Information("game mode: {GameMode}", gameMode);
        if (gameMode) SetPeek(false);
        UpdateMouseHook();
        foreach (var portal in _portals.Values) portal.SetPaused(gameMode);
        if (!gameMode) ApplyDeferredShellWork();
        _trayIcon?.SetTooltip(TrayTooltip());
    }

    /// <summary>The game was left: Desktop changes made meanwhile apply in order (or one reconcile if events were lost).</summary>
    private void ApplyDeferredShellWork()
    {
        if (_reconcileDeferred)
        {
            _reconcileDeferred = false;
            _deferredDesktopChanges.Clear();
            ReconcileDesktop();
            return;
        }
        if (_deferredDesktopChanges.Count == 0) return;
        Log.Information("applying {Count} desktop change(s) from game mode", _deferredDesktopChanges.Count);
        foreach (var change in _deferredDesktopChanges)
            (_config, _rememberedPlacements) = FenceMembership.Apply(_config, change, _rememberedPlacements, DateTimeOffset.Now);
        _deferredDesktopChanges.Clear();
        RefreshWindows();
        ScheduleSave();
    }

    /// <summary>Pause (tray): the desktop goes back to Windows — fences hidden, icons shown, the hook gone — until resumed.</summary>
    private void SetPaused(bool paused)
    {
        if (paused == _paused) return;
        if (paused)
        {
            SetPeek(false);
            EndDrawOverlay();
        }
        var iconsWereHidden = Current.IconsHidden;
        _paused = paused;
        if (paused) _quickHidden = false; // resuming shows everything
        foreach (var window in _windows.Values)
        {
            if (!Current.FencesVisible) window.Hide();
            else if (!window.IsVisible)
            {
                window.Show();
                FenceWindowChrome.SendToBack(window.Handle);
            }
        }
        if (Current.IconsHidden != iconsWereHidden) SetIconsHidden(Current.IconsHidden);
        UpdateMouseHook();
        _trayIcon?.SetTooltip(TrayTooltip());
        Log.Information("paused: {Paused}", paused);
    }

    private string TrayTooltip() =>
        _paused ? "NeoFences — paused" : _gameMode ? "NeoFences — idle while a game runs" : "NeoFences";

    /// <summary>Tray menu (spec §6; user choice 2026-10-03: left-click opens it too). Settings arrive in M6b.</summary>
    private void ShowTrayMenu(int screenX, int screenY)
    {
        var chosen = TrayMenu.Show(_messages.Handle,
        [
            new TrayMenuItem(TrayNewFence, "New fence", Enabled: !_paused),
            new TrayMenuItem(TrayQuickHide, "Quick-hide", Checked: _quickHidden, Enabled: !_paused),
            new TrayMenuItem(TrayPeek, $"Peek\t{_config.Settings.PeekHotkey}", Checked: _peeking, Enabled: !_paused),
            TrayMenuItem.Separator,
            new TrayMenuItem(TrayPause, "Pause NeoFences", Checked: _paused),
            TrayMenuItem.Separator,
            new TrayMenuItem(TrayExit, "Exit NeoFences"),
        ], screenX, screenY);
        switch (chosen)
        {
            case TrayNewFence: CreateFence(); break;
            case TrayQuickHide: SetQuickHidden(!_quickHidden); break;
            case TrayPeek: SetPeek(!_peeking); break;
            case TrayPause: SetPaused(!_paused); break;
            case TrayExit: ExitRequested?.Invoke(); break;
        }
    }

    private void OnDesktopGesture(DesktopGesture gesture, int screenX, int screenY)
    {
        switch (gesture)
        {
            // A double-click on a visible native icon opens it; only empty desktop toggles quick-hide.
            case DesktopGesture.DoubleClick when _takeoverActive || _quickHidden
                || !DesktopWindows.IsOverDesktopIcon(screenX, screenY, log: message => Log.Warning("{Message}", message)):
                SetQuickHidden(!_quickHidden);
                break;
            case DesktopGesture.RightDragStarted:
                BeginDrawFence(screenX, screenY);
                break;
            case DesktopGesture.RightDragCompleted:
                EndDrawFence(screenX, screenY);
                break;
            case DesktopGesture.RightDragCancelled:
                EndDrawOverlay(); // the drag's right-up was never seen (M5 review I2)
                break;
        }
    }

    /// <summary>Quick-hide (M5, user choice): fences and the native desktop icons go away together and come back together.</summary>
    private void SetQuickHidden(bool hidden)
    {
        if (hidden == _quickHidden) return;
        if (hidden) SetPeek(false);
        _quickHidden = hidden;
        foreach (var window in _windows.Values)
        {
            if (hidden) window.Hide();
            else
            {
                window.Show();
                FenceWindowChrome.SendToBack(window.Handle);
            }
        }
        if (!_takeoverActive) SetIconsHidden(hidden); // with Takeover the icons are hidden anyway
        Log.Information("quick-hide: {Hidden}", hidden);
    }

    private void BeginDrawFence(int startX, int startY)
    {
        if (_mouseHook is not { } mouseHook) return;
        EndDrawOverlay();
        _drawStart = (startX, startY);
        var overlay = new DrawFenceOverlay(_lightTheme);
        overlay.Track(DrawFenceOverlay.Between(_drawStart, mouseHook.DragPoint));
        overlay.Show();
        _drawOverlay = overlay;
        _drawTimer = new DispatcherTimer { Interval = DrawFrame };
        // A lost right-up (Win+L, UAC, an elevated window) is ended by the next click (RightDragCancelled). The button
        // state cannot tell: the swallowed right-press never reaches Windows' key state (M5 review I2, smoke finding).
        _drawTimer.Tick += (_, _) => overlay.Track(DrawFenceOverlay.Between(_drawStart, mouseHook.DragPoint));
        _drawTimer.Start();
    }

    /// <summary>The right button came up: a new fence where the rectangle was, its title ready to type.</summary>
    private void EndDrawFence(int endX, int endY)
    {
        if (!EndDrawOverlay()) return; // the start was never seen
        if (_monitors.Count == 0 || _config.LastLayoutFingerprint is not { } fingerprint) return;
        SetQuickHidden(false);
        var pixels = DrawFenceOverlay.Between(_drawStart, (endX, endY));
        var monitor = FencePlacement.ContainingMonitor(pixels, _monitors);
        (_config, var fence) = FenceMembership.CreateFence(_config, "New fence");
        // Too small a drag still makes a usable fence: the layout clamps it to the minimum size.
        _config = LayoutEngine.WithFenceRect(_config, fingerprint: fingerprint, fenceId: fence.Id, rect: FencePlacement.FromPixels(pixels, monitor));
        Log.Information("fence drawn on the desktop at {Pixels}", pixels);
        OpenWindow(fence);
        RefreshWindows();
        ApplyLayout();
        ScheduleSave();
        if (_windows.TryGetValue(fence.Id, out var window)) window.BeginRename();
    }

    /// <returns>True when an overlay was showing.</returns>
    private bool EndDrawOverlay()
    {
        _drawTimer?.Stop();
        _drawTimer = null;
        if (_drawOverlay is null) return false;
        _drawOverlay.Close();
        _drawOverlay = null;
        return true;
    }

    private void OnHotkey(int hotkeyId)
    {
        // Paused: the desktop belongs to Windows. Gaming: fences must not rise over the game, and the click-outside hook is off.
        if (_paused || _gameMode) return;
        if (hotkeyId == PeekHotkeyId) SetPeek(!_peeking);
        else if (hotkeyId == PeekEscapeHotkeyId) SetPeek(false);
    }

    /// <summary>Peek (M5): every fence above all windows until the hotkey again, Esc, a click outside, or an item opens.</summary>
    private void SetPeek(bool peeking)
    {
        if (peeking == _peeking) return;
        if (peeking) SetQuickHidden(false);
        _peeking = peeking;
        if (_mouseHook is not null) _mouseHook.PeekActive = peeking;
        // Every fence first: raising one restacks its siblings (all owned by Progman), and a sibling still keeping
        // itself at the bottom would drop back (M5 smoke: only one fence rose).
        foreach (var window in _windows.Values) window.Peeking = peeking;
        foreach (var window in _windows.Values) FenceWindowChrome.SetTopmost(window.Handle, peeking);
        _peekEscapeHotkey?.Dispose();
        _peekEscapeHotkey = null;
        if (peeking)
        {
            _peekEscapeHotkey = new GlobalHotkey(_messages.Handle, PeekEscapeHotkeyId);
            if (!_peekEscapeHotkey.TryRegister(new Hotkey(Ctrl: false, Alt: false, Shift: false, Win: false, Key: "Escape"), virtualKey: 0x1B))
                Log.Warning("Esc is taken by another app; Peek ends with its hotkey or a click outside");
        }
        Log.Information("peek: {Peeking}", peeking);
    }

    /// <summary>Title double-click (M5): rolled up to its title bar, or back. Stored, so it survives a restart.</summary>
    private void ToggleRollUp(FenceWindow window)
    {
        var rolledUp = !_config.Fences.First(fence => fence.Id == window.FenceId).RolledUp;
        _config = FenceEdits.SetRolledUp(_config, window.FenceId, rolledUp);
        window.SetRolledUp(rolledUp);
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            if (!_store.Save(_config)) Log.Warning("config not saved: config.json is read-only this session");
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Error(failure, "config save failed");
        }
    }
}
```

- [ ] **Step 3: Build and test**

Run: `dotnet build NeoFences.slnx`, then `dotnet test NeoFences.slnx`
Expected: 0 warnings, 0 errors; `Passed: 210`.

- [ ] **Step 4: Live smoke (ask first: about 1 min of mouse plus a dark full-screen window; print TEST RUNNING / TEST COMPLETE)**

Save as `<scratchpad>\m6a-smoke.ps1` and run it with Windows PowerShell: `powershell -NoProfile -ExecutionPolicy Bypass -File "<scratchpad>\m6a-smoke.ps1"`. It runs the branch build and leaves it running.
```powershell
# M6a live smoke: tray menu (items, Pause / Resume, New fence) and game mode (a fake borderless full-screen "game":
# hook removed, Peek ignored, Desktop changes deferred and applied after). Backs up config.json, runs -Exe, restores the
# config and starts -RestoreExe. The tray click is simulated by posting the tray callback message (Windows 11 may keep
# a new icon in the overflow); menu items are clicked by their rects, never typed. Refocuses Terminal.
param(
  [string]$Exe = "F:\projects\neo_fences\src\NeoFences.App\bin\Debug\net10.0-windows\NeoFences.exe",
  [string]$RestoreExe = $Exe)
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Namespace NfM6 -Name N -MemberDefinition @'
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
[DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
public delegate bool EnumProc(IntPtr h, IntPtr l);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern int GetMenuItemCount(IntPtr menu);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetMenuString(IntPtr menu, uint item, System.Text.StringBuilder s, int n, uint flags);
[DllImport("user32.dll")] public static extern uint GetMenuState(IntPtr menu, uint item, uint flags);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
[DllImport("user32.dll")] public static extern bool GetMenuItemRect(IntPtr h, IntPtr menu, uint item, out RECT r);
'@
# A stand-in game: a borderless window covering the primary monitor, on its own UI thread, closed on demand.
Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
public static class NfFakeGame {
    static System.Windows.Forms.Form form;
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(System.IntPtr h);
    public static void Start() {
        var ready = new System.Threading.ManualResetEvent(false);
        var thread = new System.Threading.Thread(() => {
            form = new System.Windows.Forms.Form { FormBorderStyle = System.Windows.Forms.FormBorderStyle.None, StartPosition = System.Windows.Forms.FormStartPosition.Manual,
                Bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds, BackColor = System.Drawing.Color.FromArgb(20, 20, 30), Text = "nf-fake-game", ShowInTaskbar = true };
            form.Shown += (s, e) => { SetForegroundWindow(form.Handle); ready.Set(); };
            System.Windows.Forms.Application.Run(form);
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA); thread.IsBackground = true; thread.Start(); ready.WaitOne(5000);
    }
    public static void Stop() { if (form != null) form.Invoke(new System.Action(() => form.Close())); }
}
'@
function Pause-Ms([int]$ms) { [System.Threading.Thread]::Sleep($ms) }
# Never type into Windows Terminal: an Esc there cancels Claude Code's running tool call.
function TerminalHasFocus { $processId = 0; [void][NfM6.N]::GetWindowThreadProcessId([NfM6.N]::GetForegroundWindow(), [ref]$processId); (Get-Process -Id $processId -ErrorAction SilentlyContinue).ProcessName -in 'WindowsTerminal', 'OpenConsole', 'conhost' }
function Chord([byte[]]$vks) { if (TerminalHasFocus) { "  (skipped keys: Terminal has focus)" | Out-Host; return }; foreach ($vk in $vks) { [NfM6.N]::keybd_event($vk,0,0,[UIntPtr]::Zero) }; Pause-Ms 50; [array]::Reverse($vks); foreach ($vk in $vks) { [NfM6.N]::keybd_event($vk,0,2,[UIntPtr]::Zero) }; Pause-Ms 400 }
function InputMove([int]$x, [int]$y) { [NfM6.N]::mouse_event(0x8001, [int]($x * 65535 / 1919), [int]($y * 65535 / 1079), 0, [UIntPtr]::Zero) }
function Click([int]$x, [int]$y) { InputMove $x $y; Pause-Ms 150; [NfM6.N]::mouse_event(2,0,0,0,[UIntPtr]::Zero); [NfM6.N]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Pause-Ms 700 }
function Wait-Until([scriptblock]$condition, [int]$seconds = 8) { $deadline = (Get-Date).AddSeconds($seconds); while (-not (& $condition) -and (Get-Date) -lt $deadline) { Pause-Ms 200 }; [bool](& $condition) }
function Fences { $found = New-Object System.Collections.Generic.List[IntPtr]
  [void][NfM6.N]::EnumWindows({ param($h, $l) if ([NfM6.N]::IsWindowVisible($h)) { $s = New-Object System.Text.StringBuilder 64; [void][NfM6.N]::GetWindowText($h, $s, 64); if ("$s" -eq 'NeoFences fence') { $found.Add($h) } }; $true }, [IntPtr]::Zero); @($found) }
function MenuWindow { [NfM6.N]::FindWindow('#32768', [NullString]::Value) }
function OpenTrayMenu {
  $messages = [NfM6.N]::FindWindow([NullString]::Value, 'NeoFences.SystemMessages')
  # WM_APP+2 with NIN_SELECT (left click, icon id 1); the point near the clock.
  [void][NfM6.N]::PostMessage($messages, 0x8002, [IntPtr](1800 -bor (1050 -shl 16)), [IntPtr](0x400 -bor (1 -shl 16)))
  if (-not (Wait-Until { (MenuWindow) -ne [IntPtr]::Zero } 4)) { return $null }
  Pause-Ms 300; [NfM6.N]::SendMessage((MenuWindow), 0x01E1, [IntPtr]::Zero, [IntPtr]::Zero) }  # MN_GETHMENU
function MenuItems([IntPtr]$menu) { foreach ($index in 0..([NfM6.N]::GetMenuItemCount($menu) - 1)) { $s = New-Object System.Text.StringBuilder 128; [void][NfM6.N]::GetMenuString($menu, $index, $s, 128, 0x400); $state = [NfM6.N]::GetMenuState($menu, $index, 0x400)
  [pscustomobject]@{ Index = $index; Text = "$s" -replace "`t", ' '; Checked = ($state -band 8) -ne 0; Grayed = ($state -band 1) -ne 0 } } }
function ClickMenuItem([IntPtr]$menu, [string]$prefix) { $item = MenuItems $menu | Where-Object { $_.Text -like "$prefix*" } | Select-Object -First 1
  $r = New-Object NfM6.N+RECT; [void][NfM6.N]::GetMenuItemRect([IntPtr]::Zero, $menu, $item.Index, [ref]$r); Click ([int](($r.Left + $r.Right) / 2)) ([int](($r.Top + $r.Bottom) / 2)) }
function CloseMenu { if ((MenuWindow) -ne [IntPtr]::Zero) { Click 960 300; Pause-Ms 300 } }
$log = Get-ChildItem "$env:LOCALAPPDATA\NeoFences\logs" -Filter 'neofences*' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
function LogSince([int]$line) { $current = Get-ChildItem "$env:LOCALAPPDATA\NeoFences\logs" -Filter 'neofences*' | Sort-Object LastWriteTime -Descending | Select-Object -First 1; @(Get-Content $current.FullName | Select-Object -Skip $line) }
function LogLines { $current = Get-ChildItem "$env:LOCALAPPDATA\NeoFences\logs" -Filter 'neofences*' | Sort-Object LastWriteTime -Descending | Select-Object -First 1; @(Get-Content $current.FullName).Count }
function Logged([int]$since, [string]$pattern) { [bool](LogSince $since | Where-Object { $_ -like "*$pattern*" }) }
$configPath = "$env:LOCALAPPDATA\NeoFences\config.json"
function Config { Get-Content $configPath -Raw | ConvertFrom-Json }
function Stop-App([string]$path) { & $path --exit; [void](Wait-Until { -not (Get-Process NeoFences -ErrorAction SilentlyContinue) } 15) }

# Arrange.
Stop-App $RestoreExe
$backup = "$PSScriptRoot\config-before-m6a.json"; Copy-Item $configPath $backup -Force
Start-Process $Exe; Pause-Ms 5000
$shell = New-Object -ComObject Shell.Application; $shell.MinimizeAll(); Pause-Ms 1500
Click 1000 400; "keyboard focus off Terminal: $(-not (TerminalHasFocus))"
$fenceCount = (Fences).Count; "fences shown: $fenceCount"

# 1. Tray menu items.
$menu = OpenTrayMenu
if (-not $menu) { "tray menu did not open; aborting" } else {
  "tray menu: $((MenuItems $menu | ForEach-Object { $_.Text }) -join ' | ')"
  # 2. Pause: fences hidden, icons back, hook removed.
  $mark = LogLines; ClickMenuItem $menu 'Pause'
  "Pause: fences hidden " + (Wait-Until { (Fences).Count -eq 0 }) + ", icons shown " + (Wait-Until { Logged $mark 'desktop icons hidden: False' } 4) + ", hook removed $(Logged $mark 'WH_MOUSE_LL removed')"
  $menu = OpenTrayMenu; $items = MenuItems $menu
  "paused menu: Pause checked $(($items | Where-Object Text -like 'Pause*').Checked), New fence grayed $(($items | Where-Object Text -eq 'New fence').Grayed)"
  # 3. Resume.
  $mark = LogLines; ClickMenuItem $menu 'Pause'
  "Resume: fences back " + (Wait-Until { (Fences).Count -eq $fenceCount }) + ", icons hidden again " + (Wait-Until { Logged $mark 'desktop icons hidden: True' } 4) + ", hook back $(Logged $mark 'WH_MOUSE_LL installed')"
  # 4. New fence from the tray.
  $menu = OpenTrayMenu; ClickMenuItem $menu 'New fence'
  "tray New fence: " + (Wait-Until { (Fences).Count -eq $fenceCount + 1 })
  CloseMenu
}

# 5. Game mode: a borderless full-screen window in front → idle; Peek ignored; Desktop changes wait for the end.
$desk = [Environment]::GetFolderPath('Desktop'); $testFile = Join-Path $desk 'nf-m6a-game.txt'
$mark = LogLines
[NfFakeGame]::Start()
"game mode on: " + (Wait-Until { Logged $mark 'game mode: True' } 8) + ", hook removed $(Logged $mark 'WH_MOUSE_LL removed')"
Chord @(0x11, 0x12, 0x20); Pause-Ms 500
"Peek ignored in game: $(-not (Logged $mark 'peek: True'))"
Set-Content $testFile 'game'; Pause-Ms 2000
$inbox = { @(((Config).fences | Where-Object { $_.isInbox }).items) -contains $testFile }
"desktop change deferred while gaming: $(-not (& $inbox))"
$mark = LogLines
[NfFakeGame]::Stop()
"game mode off: " + (Wait-Until { Logged $mark 'game mode: False' } 8) + ", hook back $(Logged $mark 'WH_MOUSE_LL installed'), deferred change applied " + (Wait-Until $inbox 5)

# Clean up: the test file is ours; the config comes back; the build to keep runs again.
if (Test-Path $testFile) { [System.IO.File]::Delete($testFile) }
Stop-App $Exe; Copy-Item $backup $configPath -Force; Start-Process $RestoreExe; Pause-Ms 4000
$shell.UndoMinimizeALL(); Pause-Ms 1000
$terminal = Get-Process WindowsTerminal -ErrorAction SilentlyContinue | Select-Object -First 1
if ($terminal) { [NfM6.N]::keybd_event(0x12,0,0,[UIntPtr]::Zero); [void](New-Object -ComObject WScript.Shell).AppActivate($terminal.Id); [NfM6.N]::keybd_event(0x12,0,2,[UIntPtr]::Zero) }
"NeoFences alive: " + [bool](Get-Process NeoFences -ErrorAction SilentlyContinue)
```
Expected:
- `tray menu: New fence | Quick-hide | Peek Ctrl+Alt+Space |  | Pause NeoFences |  | Exit NeoFences`;
- every other line ends `True`.

Regression: re-run `<scratchpad>\m5-smoke-branch.ps1` (gestures), because the hook's lifetime moved into `UpdateMouseHook`. Expected: as in M5.

- [ ] **Step 5: Commit**

```powershell
git add src/NeoFences.App
git commit -m "feat: added the tray icon and menu, pause, and game mode idle to the app"
```

---

### Task 4: Verification

**Files:** `docs/TEST-CHECKLIST.md` (append section O), `docs/research/m6a-game-mode-tray.md` (create)

- [ ] **Step 1: Append section O**

```markdown
## O — Tray, Pause, game mode (M6a, ADR-021)
| ID | Steps | Expected |
|---|---|---|
| O1 | Look at the notification area (open the overflow ^ if needed) | the NeoFences icon (blue tile, three panels); tooltip "NeoFences" |
| O2 | Left-click it; right-click it | the same menu both times: New fence · Quick-hide · Peek (Ctrl+Alt+Space) · Pause NeoFences · Exit NeoFences; a click elsewhere closes it |
| O3 | Pause NeoFences; then Pause again | fences hide, native icons show, gestures stop, tooltip "NeoFences — paused"; New fence / Quick-hide / Peek grayed; the second click brings everything back |
| O4 | Tray → New fence; Quick-hide; Peek | as from the fence menu / gestures / hotkey |
| O5 | Start a full-screen game (borderless or exclusive), play a minute, alt-tab out | log `game mode: True` within ~5 s, `WH_MOUSE_LL removed`; tooltip "idle while a game runs"; after alt-tab `game mode: False` and the hook back |
| O6 | While in a game: Ctrl+Alt+Space; save a file to the Desktop; take a screenshot into a Portal folder | no Peek; the file and screenshot appear in their fences after leaving the game |
| O7 | Kill Explorer (Task Manager → Restart Windows Explorer) | the tray icon comes back; gestures still work (hook re-installed) |
| O8 | Win+L, sign back in; double-click the desktop | quick-hide works (hook re-installed on unlock) |
| O9 | Full-screen video in a browser (F11) | game mode on while full screen (harmless idle), off after Esc |
| O10 | Tray → Exit NeoFences | the tray icon disappears; icons come back as usual |
```

- [ ] **Step 2: Run O1–O10.**
  - The smoke covers O2 (menu items), O3, O4 (New fence), and O5/O6 with the stand-in game.
  - **[USER]**: O1 (seeing the icon), O5/O6 with a real game, O7, O8, O9, O10.

- [ ] **Step 3: Write `docs/research/m6a-game-mode-tray.md`** with:
  - the results;
  - the `LoadIconMetric` crash;
  - the 2–3.5 s notification-state lag for a borderless window;
  - the PowerShell `$null` → `""` P/Invoke finding.

- [ ] **Step 4: Commit**

```powershell
git add docs/TEST-CHECKLIST.md docs/research/m6a-game-mode-tray.md
git commit -m "docs: added M6a game mode and tray checks and results"
```

---

### Task 5: Docs sync

- [ ] **Step 1: Append ADR-021 to `docs/DECISIONS.md`**

```markdown
## ADR-021 — Game mode, tray, Pause, and mouse-hook hardening (M6a)
**Date:** 2026-10-03 · **Status:** Accepted · **Builds on:** ADR-007, ADR-011, ADR-020 · **Refines:** spec §4.7, §6

**Context.** M6 was split by the user on 2026-10-03:
- **M6a** (this ADR): game mode, the tray, Pause, and hook hardening;
- **M6b**: the settings window and animations.

The user chose: game mode = **go idle** (fences stay, nothing is hidden), and a **left-click on the tray icon opens the tray menu**. M0 found that the notification state was the only signal that caught a real game, and that it lags behind the foreground change (findings 4–5).

**Decision.**
- **Game mode.** It is on when the notification state (`SHQueryUserNotificationState`) is `QUNS_BUSY` or `QUNS_RUNNING_D3D_FULL_SCREEN`, **and** the foreground window is an app. Not an app: the desktop (Progman/WorkerW), the taskbars, NeoFences itself, or no window (`GameModePolicy`, tested).
  - The lock screen reports `QUNS_NOT_PRESENT` and never counts.
  - It is checked on every foreground change (an out-of-context WinEvent hook, `ForegroundWatcher`), and again 1, 2.5 and 5 s later. The state flips 1–3.5 s after a full-screen window activates, with no further event.
  - The spec's "window covers its monitor" check is dropped: it never fired in M0, and with taskbar auto-hide a maximized app would also match it.
  - `Settings.GameMode` (default on) turns it off.
- **While gaming (idle):**
  - the mouse hook is removed (zero added input latency);
  - Peek is ignored: fences must not rise over the game, and the hook that ends Peek is gone;
  - Desktop watcher changes are queued and applied in order afterwards (a lost-events error becomes one reconcile afterwards);
  - Portals stop re-listing; a Portal that changed re-lists once afterwards.

  Fences and icons stay as they are.
- **Tray.** A `Shell_NotifyIcon` icon (NOTIFYICON_VERSION_4) with the new app icon (`NeoFences.ico`, also the exe icon). It is added again on TaskbarCreated.
  - Any click opens Windows' own popup menu (`TrackPopupMenuEx`, with the foreground + `WM_NULL` rule). The menu: New fence · Quick-hide · Peek (with its hotkey) · Pause NeoFences · Exit. Settings arrives in M6b.
  - The icon loads with `LoadImage` (user32). `LoadIconMetric` lives in comctl32 v6, which a WPF app does not load: it crashed the prototype.
  - A tray failure is logged and loses only the tray (hard rule 7).
- **Pause** (tray, not saved): fences hidden, native icons shown even with Takeover on, the mouse hook removed, Peek ignored. Resume restores everything; Pause also ends quick-hide.
- **One rule for the modes.** `RunState(Takeover, QuickHidden, Paused, GameMode)` decides together whether fences show, whether icons hide, whether the hook is wanted, and whether shell work waits (Core, tested). `FenceHost` derives every icon and hook decision from it.
- **Hook hardening.**
  - The hook thread runs at Highest priority, so a CPU-heavy game does not push the callback past `LowLevelHooksTimeout`, after which Windows drops the hook silently.
  - The hook is re-installed after an Explorer restart and on session unlock (`WM_WTSSESSION_CHANGE`).
  - The S2 right-click replay is skipped when the pointer has left the desktop (M5 review M7).
- **Icons on exit.** Exit, session end and the crash handler also show the icons whenever the takeover-active marker is set, so a show that failed earlier is retried (M5 review M6).

**Consequences.**
- Full-screen video or a presentation also counts as game mode (`QUNS_BUSY`). That is harmless: NeoFences only idles.
- Windows 11 may keep a new tray icon in the overflow until the user pins it; the menu is the same.
- Session lock/unlock and an Explorer restart with the tray are hand checks (O7, O8).
```

- [ ] **Step 2: Update the other docs**
  - `ARCHITECTURE.md`:
    - replace the `Shell/GameDetector` row with `GameDetection`/`ForegroundWatcher` + `Core/GameModePolicy`;
    - add `TrayIcon`/`TrayMenu`/`SessionNotifications` and `Core/RunState`;
    - replace "Next: M6" with "M6a complete … Next: M6b".
  - `FEATURES.md`:
    - Game-mode idle → done;
    - "Settings window, tray" → wip (tray done; settings M6b).
  - `ROADMAP.md`:
    - tick M6a;
    - mark the M6 game-mode line done;
    - tick "Hook thread priority Highest; re-install…" and "Retry showing icons on exit…" and "Replay the right-click only if…" in the M5 carry-overs.
  - `SESSION-LOG.md`: add an entry.

- [ ] **Step 3: Refresh the hub** (`node --check`, publish with `url`).

- [ ] **Step 4: Commit**

```powershell
git add docs
git commit -m "docs: added ADR-021 and synced docs for M6a"
```
