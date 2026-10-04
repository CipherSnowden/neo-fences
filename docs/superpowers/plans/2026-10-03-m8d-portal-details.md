# M8d — v1.1 Portal Details Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The last carry-over batch (ROADMAP M8, batch d): "Safely Remove" works while a Portal shows a USB drive, Portal watcher errors back off, Enter on several items opens each, and the cheap M8c review minors.

**Architecture:**
- `NeoFences.Shell`:
  - `DeviceRemovalNotice` (new): a folder handle registered for `DBT_DEVICEQUERYREMOVE`;
  - `FolderWatcher.Failed` split from `Changed`;
  - `FenceWindowChrome.IsLiveWindow`;
  - `ShellDragDrop` never asks a virtual source for CF_HDROP.
- `NeoFences.App`:
  - `SystemMessageWindow.DeviceRemovalRequested`;
  - `PortalState` registers the notice, releases on request, and backs off with Core `WatcherBackoff`;
  - `FenceWindow.OpenManyRequested`, and the recognised double-click is used once;
  - FenceHost wiring, the live-owner check, and special icons deferred in games.
- No Core change: Core `WatcherBackoff` (M8c) is reused.

**Tech Stack:** .NET 10, WPF, CsWin32 0.3.335, Serilog, xUnit, Velopack 1.2.161. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-10-02-neofences-v1-design.md` §4.6 (Portals), §7 (reliability). **Decisions:** ADR-018, ADR-026; **ADR-027** (new, Task 3). The user lent the USB stick LOCO_DUCK for tests (only `G:\NeoFences-test` on it).

**Pre-verified (2026-10-03):** every code block below was compiled together (0 warnings, 0 errors), and **283/283 tests pass**. On the real desktop (installed 1.1.0 restored after each run):
- **Safely Remove** (`CM_Request_Device_Eject` on the stick, a Portal of `G:\NeoFences-test` shown): v1.1.0 was **vetoed** (volume in use); M8d **ejected**, logged the release, showed "not available", and kept running.
- A new file in the Portal kept the selection.
- Enter on two folders opened both in Explorer.
- The M8b regressions passed.
- There is no new Core logic, so Shell/App behaviour is verified live (checklist U).

## Global Constraints

- **Hard rule 2 / 7:** a removal notice that cannot be registered is logged, and that drive then just cannot be removed while shown (as before). Nothing on the drive is touched; the Portal only closes its own handles.
- **Hard rules 4, 5:** new Win32 (`CreateFile`, `RegisterDeviceNotification`, `IsWindow`) goes through CsWin32 in Shell.
- The removal answer must be fast: handles close before `WM_DEVICECHANGE` returns `TRUE`.
- Commits: single line, Conventional Commits, past tense, no `Co-Authored-By` trailer. Test command: `dotnet test NeoFences.slnx`.
- **NeoFences 1.1.0 is installed.** Smokes restore the installed copy. Ask before mouse runs (the user allowed unattended runs and the stick on 2026-10-03), print TEST RUNNING / TEST COMPLETE, and never type into Windows Terminal. The removal test **ejects the stick**: run it last and tell the user to replug it.

## Review Focus

1. **The removal notice's lifetime.** A Portal refresh racing a removal request; the fence deleted during a request; a refresh in flight while the drive goes. Expected: no handle leaks (every `DeviceRemovalNotice` disposed once), no re-open of the folder after the release, no crash.
2. **Several Portals on the same stick; a Portal and Explorer on the stick.** Expected: every Portal releases; Windows names Explorer if it still holds the drive.
3. **Fixed and network drives.** Expected: a notice on local fixed drives is harmless; none on network or UNC paths; no repeated warnings in the log.
4. **The Portal backoff.** A watcher failing on every arm; the drive returning in the middle. Expected: growing waits up to a minute, then a normal refresh.
5. **Enter on a mix** (files and folders, in a Portal and a desktop fence). Expected: each opens; nothing browses.

---

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/NeoFences.Shell/DeviceRemovalNotice.cs` (new), `FolderItems.cs`, `FenceWindowChrome.cs`, `ShellDragDrop.cs`, `NativeMethods.txt` | removal notice, watcher failure, live window, virtual drops | 1 |
| `src/NeoFences.App/SystemMessageWindow.cs`, `PortalState.cs`, `FenceWindow.xaml.cs`, `FenceHost.cs` | wiring | 2 |
| `docs/TEST-CHECKLIST.md` (U), `docs/research/m8d-portal-details.md`, `DECISIONS.md` (ADR-027), `ROADMAP.md`, `ARCHITECTURE.md`, `FEATURES.md`, `SESSION-LOG.md`, hub | docs | 3 |

---

### Task 1: Shell — removal notice, watcher failure, live window, virtual drops

**Files:**
- Modify: `docs/ROADMAP.md` (claim M8d).
- Create: `src/NeoFences.Shell/DeviceRemovalNotice.cs`.
- Replace: `src/NeoFences.Shell/FolderItems.cs`, `FenceWindowChrome.cs`, `ShellDragDrop.cs` and `NativeMethods.txt`.

**Interfaces:**
- Produces:
  - `DeviceRemovalNotice.TryRegister(nint ownerHandle, string folderPath, Action<Exception> logFailure)`, `.Handle`, `HandleOf(nint lParam)`, `WmDeviceChange`, `QueryRemove`;
  - `FolderWatcher.Failed`;
  - `FenceWindowChrome.IsLiveWindow(nint)`.

- [ ] **Step 1: Branch and claim**

```powershell
git switch -c m8d-portal-details
```
In `docs/ROADMAP.md`, after the M8c review minors list, add `- [x] M8d implementation plan` and `- [~] M8d — claimed by session 2026-10-03 m8d`.
```powershell
git add docs/ROADMAP.md
git commit -m "docs: claimed M8d"
```

- [ ] **Step 2: Files**

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
DWM_WINDOW_CORNER_PREFERENCE
DwmSetWindowAttribute
DWMWINDOWATTRIBUTE
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
SHChangeNotifyRegister
SHChangeNotifyDeregister
SHChangeNotifyEntry
SHCNE_ID
SHCNRF_SOURCE
SHGetKnownFolderIDList
FOLDERID_RecycleBinFolder
CoTaskMemFree
RegNotifyChangeKeyValue
REG_NOTIFY_FILTER
RegisterClipboardFormat
FILEGROUPDESCRIPTORW
GlobalLock
GlobalUnlock
GetMessageTime
GlobalSize
CreateFile
RegisterDeviceNotification
UnregisterDeviceNotification
DEV_BROADCAST_HANDLE
DEV_BROADCAST_HDR
IsWindow
```
`src/NeoFences.Shell/DeviceRemovalNotice.cs`:
```csharp
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Shell;

/// <summary>
/// "Safely Remove" / Eject for a Portal's drive (M8d, M4 carry-over): any open handle on a drive vetoes its removal, and
/// a Portal keeps two (its folder watcher and its parent's). Windows asks every window registered for a handle on the
/// drive first (DBT_DEVICEQUERYREMOVE to the owner window); the Portal then closes everything it holds there.
/// </summary>
public sealed class DeviceRemovalNotice : IDisposable
{
    public const int WmDeviceChange = 0x0219;
    public const int QueryRemove = 0x8001; // DBT_DEVICEQUERYREMOVE

    private readonly SafeFileHandle _folder;
    private readonly HDEVNOTIFY _registration;

    /// <summary>The folder handle Windows names in its notice.</summary>
    public nint Handle { get; }

    private DeviceRemovalNotice(SafeFileHandle folder, HDEVNOTIFY registration)
    {
        _folder = folder;
        _registration = registration;
        Handle = folder.DangerousGetHandle();
    }

    /// <summary>
    /// Registers for removal requests of the drive holding this folder. Only local drives can be removed this way
    /// (USB sticks and disks, card readers, optical drives); null for network folders or when Windows refuses.
    /// </summary>
    public static unsafe DeviceRemovalNotice? TryRegister(nint ownerHandle, string folderPath, Action<Exception> logFailure)
    {
        try
        {
            var root = Path.GetPathRoot(folderPath);
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\", StringComparison.Ordinal)) return null; // UNC share
            if (new DriveInfo(root).DriveType is not (DriveType.Removable or DriveType.Fixed or DriveType.CDRom)) return null;
            // Attributes only, sharing everything: this handle never gets in the way of anything but the removal itself.
            var folder = PInvoke.CreateFile(folderPath, 0x80, // FILE_READ_ATTRIBUTES
                FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE | FILE_SHARE_MODE.FILE_SHARE_DELETE,
                null, FILE_CREATION_DISPOSITION.OPEN_EXISTING, FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS, null);
            if (folder.IsInvalid) throw new System.ComponentModel.Win32Exception();
            var filter = new DEV_BROADCAST_HANDLE
            {
                dbch_size = (uint)sizeof(DEV_BROADCAST_HANDLE),
                dbch_devicetype = (uint)DEV_BROADCAST_HDR_DEVICE_TYPE.DBT_DEVTYP_HANDLE,
                dbch_handle = (HANDLE)folder.DangerousGetHandle(),
            };
            var registration = PInvoke.RegisterDeviceNotification((HANDLE)ownerHandle, &filter, REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_WINDOW_HANDLE);
            if (registration.IsNull)
            {
                var failure = new System.ComponentModel.Win32Exception();
                folder.Dispose();
                throw failure;
            }
            return new DeviceRemovalNotice(folder, registration);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            logFailure(failure); // the drive then just cannot be removed safely while the Portal shows it (as before)
            return null;
        }
    }

    /// <summary>The handle a WM_DEVICECHANGE notice is about, or 0 when it is not a handle notice.</summary>
    public static unsafe nint HandleOf(nint lParam)
    {
        if (lParam == 0) return 0;
        var header = (DEV_BROADCAST_HDR*)lParam;
        return header->dbch_devicetype == DEV_BROADCAST_HDR_DEVICE_TYPE.DBT_DEVTYP_HANDLE ? ((DEV_BROADCAST_HANDLE*)lParam)->dbch_handle : 0;
    }

    public void Dispose()
    {
        PInvoke.UnregisterDeviceNotification(_registration);
        _folder.Dispose();
    }
}
```
`src/NeoFences.Shell/FolderItems.cs`:
```csharp
using NeoFences.Core.Model;

namespace NeoFences.Shell;

/// <summary>The contents of a Portal's folder, and the facts sorting needs about any item (M4).</summary>
public static class FolderItems
{
    /// <summary>Visible entries (not Hidden or System, like Explorer), or null when the folder cannot be read (missing, offline, denied).</summary>
    public static IReadOnlyList<ItemInfo>? TryList(string folderPath)
    {
        try
        {
            return new DirectoryInfo(folderPath).EnumerateFileSystemInfos()
                .Where(DesktopItems.IsVisibleOnDesktop)
                .Select(Describe)
                .ToList();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Sorting facts for items of a desktop fence ("Sort by", one time). Special items sort as folders by name.</summary>
    public static IReadOnlyList<ItemInfo> Describe(IEnumerable<string> itemRefs) => itemRefs.Select(itemRef =>
    {
        if (itemRef.StartsWith("::", StringComparison.Ordinal))
            return new ItemInfo(itemRef, ShellItems.TryGetDisplayName(itemRef) ?? itemRef, IsFolder: true, TypeName: "", DateTimeOffset.MinValue);
        FileSystemInfo entry = Directory.Exists(itemRef) ? new DirectoryInfo(itemRef) : new FileInfo(itemRef);
        // Keep the caller's ref: Windows path normalisation drops trailing spaces/dots ("foo "), so FullName may differ
        // and the one-time sort would then not match the fence's items (M3 review I3).
        return Describe(entry) with { ItemRef = itemRef };
    }).ToList();

    // ponytail: type = extension, not the shell's type name (SHGetFileInfo per item); upgrade if users sort by type often.
    private static ItemInfo Describe(FileSystemInfo entry)
    {
        var isFolder = entry is DirectoryInfo;
        return new ItemInfo(entry.FullName, entry.Name, isFolder, isFolder ? "" : entry.Extension.ToUpperInvariant(),
            entry.Exists ? entry.LastWriteTime : DateTimeOffset.MinValue);
    }
}

/// <summary>Says when a folder's contents changed (a Portal re-lists it). Events arrive on thread-pool threads.</summary>
public sealed class FolderWatcher : IDisposable
{
    private readonly FileSystemWatcher? _watcher;
    private readonly FileSystemWatcher? _parentWatcher; // the folder itself renamed, moved or deleted (and back)

    public event Action? Changed;
    /// <summary>The watcher lost events or stopped (a drive going away, a network hiccup): re-list and re-arm, with backoff (M8d).</summary>
    public event Action? Failed;

    /// <summary>False when the folder itself could not be watched (missing, offline): the Portal then retries.</summary>
    public bool IsWatching => _watcher is not null;

    /// <param name="logFailure">Told when the folder cannot be watched; the Portal then only refreshes on navigation.</param>
    public FolderWatcher(string folderPath, Action<Exception> logFailure)
    {
        try
        {
            _watcher = new FileSystemWatcher(folderPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                InternalBufferSize = 64 * 1024,
            };
            _watcher.Created += (_, _) => Changed?.Invoke();
            _watcher.Deleted += (_, _) => Changed?.Invoke();
            _watcher.Renamed += (_, _) => Changed?.Invoke();
            _watcher.Changed += (_, _) => Changed?.Invoke();
            _watcher.Error += (_, _) => Failed?.Invoke(); // lost events or stopped: a full re-list covers them, not too often
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception failure) when (failure is IOException or ArgumentException or UnauthorizedAccessException)
        {
            logFailure(failure);
        }
        // A watcher follows its directory when that is renamed and reports nothing, so the Portal would keep showing
        // a folder that is gone (M4 smoke). The parent tells when the folder itself goes away or comes back.
        try
        {
            var trimmed = folderPath.TrimEnd('\\', '/');
            if (Path.GetDirectoryName(trimmed) is { } parent && Directory.Exists(parent))
            {
                _parentWatcher = new FileSystemWatcher(parent, Path.GetFileName(trimmed))
                {
                    NotifyFilter = NotifyFilters.DirectoryName,
                    IncludeSubdirectories = false,
                };
                _parentWatcher.Created += (_, _) => Changed?.Invoke();
                _parentWatcher.Deleted += (_, _) => Changed?.Invoke();
                _parentWatcher.Renamed += (_, _) => Changed?.Invoke();
                _parentWatcher.EnableRaisingEvents = true;
            }
        }
        catch (Exception failure) when (failure is IOException or ArgumentException or UnauthorizedAccessException)
        {
            logFailure(failure);
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _parentWatcher?.Dispose();
    }
}
```
`src/NeoFences.Shell/FenceWindowChrome.cs`:
```csharp
using System.Runtime.InteropServices;
using NeoFences.Core.Layouts;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Shell;

/// <summary>Native look and placement of a fence window: tool-window styles, accent blur, rounded corners, pixel placement.</summary>
public static class FenceWindowChrome
{
    /// <summary>
    /// Tint (AABBGGRR) passed with the accent blur. M2c found Windows ignores it for ACCENT_ENABLE_BLURBEHIND (changing it
    /// changed nothing); the light-mode veil is drawn by the fence itself (ADR-015).
    /// </summary>
    public const uint DefaultTint = 0x40201A16;

    /// <summary>
    /// Not in the taskbar or Alt+Tab, not activated just by being shown, and no maximize/minimize boxes: without
    /// them a double-click on the title (a Fences habit, roll-up in M5) or Aero Snap cannot maximize the fence
    /// and save a full-screen rect (M2a review).
    /// </summary>
    public static void ApplyToolWindowStyles(nint handle)
    {
        var style = PInvoke.GetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        PInvoke.SetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE,
            style & ~(nint)(WINDOW_STYLE.WS_MAXIMIZEBOX | WINDOW_STYLE.WS_MINIMIZEBOX));
        var exStyle = PInvoke.GetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE,
            exStyle | (nint)(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE));
    }

    /// <summary>Blur of whatever is behind the window, focus-independent. Requires a layered (AllowsTransparency) window.</summary>
    /// <returns>False if Windows rejected the call; the fence then shows its plain tint (fallback).</returns>
    public static unsafe bool ApplyAccentBlur(nint handle, uint tintAbgr = DefaultTint)
    {
        var policy = new AccentPolicy { AccentState = AccentEnableBlurBehind, GradientColor = tintAbgr };
        var data = new WindowCompositionAttributeData { Attribute = WcaAccentPolicy, Data = (nint)(&policy), SizeOfData = sizeof(AccentPolicy) };
        return SetWindowCompositionAttribute(handle, ref data) != 0;
    }

    /// <summary>
    /// Rounded corners drawn by Windows 11 itself (DWMWA_WINDOW_CORNER_PREFERENCE), which also round the accent blur.
    /// A window region does not: Windows draws the blur over the whole rectangle, so a square of blur showed outside
    /// the rounded border (user screenshot 2026-10-03, M8a). On Windows 10 the call is refused and the fence keeps
    /// square blur corners under its rounded border (the region WindowChrome keeps still shapes clicks).
    /// </summary>
    /// <returns>False when Windows refused (Windows 10): the caller logs it once (M8a review).</returns>
    public static unsafe bool UseRoundedCorners(nint handle)
    {
        var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        return PInvoke.DwmSetWindowAttribute((HWND)handle, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &preference, sizeof(DWM_WINDOW_CORNER_PREFERENCE)).Succeeded;
    }

    /// <summary>False once the window is destroyed (a fence deleted while a shell operation waited, M8c review).</summary>
    public static bool IsLiveWindow(nint handle) => handle != 0 && PInvoke.IsWindow((HWND)handle);

    /// <summary>When the message being handled was posted (ms tick, wraps): a click's own time, not the time it is handled.</summary>
    public static uint MessageTime() => unchecked((uint)PInvoke.GetMessageTime());

    /// <summary>The user's double-click time and size: a title double-click is recognised by NeoFences itself (M8b).</summary>
    public static (uint Milliseconds, int WidthPx, int HeightPx) DoubleClickSettings() =>
        (PInvoke.GetDoubleClickTime(), PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXDOUBLECLK), PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYDOUBLECLK));

    /// <summary>Reads the RECT that WM_MOVING / WM_SIZING point to (lParam).</summary>
    public static unsafe PixelRect ReadRect(nint rectPointer)
    {
        var rect = (RECT*)rectPointer;
        return new PixelRect(rect->left, rect->top, rect->right - rect->left, rect->bottom - rect->top);
    }

    /// <summary>Writes back the RECT of WM_MOVING / WM_SIZING; Windows then moves or sizes the window there.</summary>
    public static unsafe void WriteRect(nint rectPointer, PixelRect value) =>
        *(RECT*)rectPointer = new RECT(value.X, value.Y, value.X + value.Width, value.Y + value.Height);

    public static PixelRect GetPixelRect(nint handle)
    {
        PInvoke.GetWindowRect((HWND)handle, out var rect);
        return new PixelRect(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
    }

    /// <summary>
    /// Sends the fence below every app window. Showing a window puts it on top of the z-order; because the fence is
    /// owned by Progman, "bottom" ends just above the desktop (an owned window always stays above its owner).
    /// </summary>
    public static void SendToBack(nint handle) =>
        PInvoke.SetWindowPos((HWND)handle, HWND.HWND_BOTTOM, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    /// <summary>
    /// Call from WM_WINDOWPOSCHANGING (lParam): any z-order change (activation by a click, Show, another app being
    /// restored) ends at the bottom, just above the desktop. Being owned by Progman alone does not keep an activated
    /// fence below apps (user report 2026-10-02: the Inbox drew over Firefox).
    /// </summary>
    public static unsafe void KeepAtBottom(nint windowPosPointer)
    {
        var windowPos = (WINDOWPOS*)windowPosPointer;
        if ((windowPos->flags & SET_WINDOW_POS_FLAGS.SWP_NOZORDER) == 0) windowPos->hwndInsertAfter = HWND.HWND_BOTTOM;
    }

    /// <summary>Peek (spec §4.6): fences above every window while on; back to the bottom (above the desktop) when off.</summary>
    public static void SetTopmost(nint handle, bool topmost)
    {
        const SET_WINDOW_POS_FLAGS flags = SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;
        PInvoke.SetWindowPos((HWND)handle, topmost ? HWND.HWND_TOPMOST : HWND.HWND_NOTOPMOST, 0, 0, 0, 0, flags);
        if (!topmost) SendToBack(handle);
    }

    /// <summary>A topmost window the mouse passes through (the draw-a-fence rectangle): never activated, never hit.</summary>
    public static void MakeOverlay(nint handle)
    {
        var exStyle = PInvoke.GetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle | (nint)(WINDOW_EX_STYLE.WS_EX_TRANSPARENT
            | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOPMOST));
    }

    /// <summary>Mouse position in physical screen pixels (roll-up hover).</summary>
    public static (int X, int Y) GetCursorPosition()
    {
        PInvoke.GetCursorPos(out var position);
        return (position.X, position.Y);
    }

    /// <summary>Moves and sizes without changing z-order or activating.</summary>
    public static void SetPixelRect(nint handle, PixelRect rect)
    {
        const SET_WINDOW_POS_FLAGS flags = SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;
        // ponytail: set twice so a move onto a monitor with another DPI (WM_DPICHANGED resizes us) still ends at the requested size.
        PInvoke.SetWindowPos((HWND)handle, HWND.Null, rect.X, rect.Y, rect.Width, rect.Height, flags);
        PInvoke.SetWindowPos((HWND)handle, HWND.Null, rect.X, rect.Y, rect.Width, rect.Height, flags);
    }

    // ponytail: undocumented user32 API with no CsWin32 metadata (CLAUDE.md rule 5 exception, ADR-011); fallback is the tint.
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
    private const int AccentEnableBlurBehind = 3;
}
```
`src/NeoFences.Shell/ShellDragDrop.cs`:
```csharp
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Shell;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace NeoFences.Shell;

/// <summary>Where a drop on a fence lands, asked of the fence for a screen point (physical pixels).</summary>
/// <param name="ItemRef">The item whose "drop into" zone is under the point (Core DropZones), if any.</param>
/// <param name="InsertAt">Index in the fence's shown list to insert before (see FenceMembership.MoveItems).</param>
public readonly record struct FenceDropPoint(string? ItemRef, int InsertAt);

/// <summary>What a fence does with drops; NeoFences.App supplies these (all called on the UI thread).</summary>
/// <param name="HitTest">Item and insert position under a screen point.</param>
/// <param name="MoveItems">Desktop items dropped on the fence (from a fence or from Explorer's Desktop): membership only, no file operation.</param>
/// <param name="ExpectArrivals">Desktop refs Windows is about to copy or move onto the Desktop for this drop, and where they go.</param>
/// <param name="Recycle">Files dropped on the Recycle Bin item: always recycled by NeoFences, never deleted (hard rule 1).</param>
/// <param name="ShowFeedback">Where the drop would land (null hides it): an insertion caret, or a highlighted container when into is true.</param>
/// <param name="LogFailure">A drop that could not be handed to Windows.</param>
public sealed record FenceDropHandlers(
    Func<int, int, FenceDropPoint> HitTest,
    Action<IReadOnlyList<string>, int> MoveItems,
    Action<IReadOnlyList<string>, int> ExpectArrivals,
    Action<IReadOnlyList<string>> Recycle,
    Action<FenceDropPoint?, bool> ShowFeedback,
    Action<Exception> LogFailure);

/// <summary>
/// Drag-drop with Windows' own engine (spec §6, M3b). Dragging out uses the shell's data object for the items, so
/// apps and Explorer get real files (copy/move/link as they decide) and Windows draws the drag image. Drops on a fence:
/// <list type="bullet">
/// <item>desktop items (from any fence, or Explorer showing the Desktop folder) only change membership: no file operation;</item>
/// <item>on an item that takes drops (folder, Recycle Bin, program) they go to that item, as on the desktop;</item>
/// <item>anything else goes to the Desktop folder's own drop target (Windows copies/moves, with progress and Undo),
/// and the arriving files are placed in this fence at the drop position.</item>
/// </list>
/// </summary>
public static class ShellDragDrop
{
    /// <summary>Items being dragged out of a fence right now (one drag at a time; drops on fences are membership moves).</summary>
    internal static IReadOnlyList<string>? CurrentDrag { get; private set; }

    /// <summary>Starts a drag of these items; returns when it ends. The drag image, cursor and effects are Windows'.</summary>
    /// <returns>False when the drag could not start (an item vanished, a broken shell extension).</returns>
    public static unsafe bool TryDrag(nint ownerHandle, IReadOnlyList<string> itemRefs, Action<Exception> logFailure)
    {
        ComDataObject? dataObject = null;
        try
        {
            dataObject = (ComDataObject)DesktopNamespace.GetUIObject((HWND)ownerHandle, itemRefs, typeof(ComDataObject).GUID);
            CurrentDrag = itemRefs;
            PInvoke.SHDoDragDrop((HWND)ownerHandle, dataObject, null,
                DROPEFFECT.DROPEFFECT_COPY | DROPEFFECT.DROPEFFECT_MOVE | DROPEFFECT.DROPEFFECT_LINK, out _);
            return true;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            logFailure(failure);
            return false;
        }
        finally
        {
            CurrentDrag = null;
            if (dataObject is not null) Marshal.ReleaseComObject(dataObject);
        }
    }

    /// <summary>Makes the fence window a drop target. Dispose (before the window is destroyed) to unregister.</summary>
    /// <param name="portalFolder">For a Portal fence: the folder it shows right now (drops become real moves/copies into it, M4).</param>
    public static IDisposable RegisterFence(nint fenceHandle, FenceDropHandlers handlers, Func<string?>? portalFolder = null)
    {
        var target = new FenceDropTarget((HWND)fenceHandle, handlers, portalFolder);
        // WPF registers its own drop target on every window; NeoFences does not use WPF drag-drop, so replace it.
        PInvoke.RevokeDragDrop((HWND)fenceHandle);
        PInvoke.RegisterDragDrop((HWND)fenceHandle, target).ThrowOnFailure();
        return target;
    }

    private static readonly ushort FileGroupDescriptorFormat = (ushort)PInvoke.RegisterClipboardFormat("FileGroupDescriptorW");

    /// <summary>True when the source describes virtual files (zip contents, phones, mail attachments).</summary>
    internal static unsafe bool OffersVirtualFiles(IDataObject dataObject)
    {
        var format = DescriptorFormat();
        try { return dataObject.QueryGetData(&format).Value == 0; } // S_OK; S_FALSE and errors mean no
        catch (Exception failure) when (failure is not OutOfMemoryException) { return false; }
    }

    /// <summary>The names of virtual files being dropped (no extraction); empty when there are none.</summary>
    internal static unsafe List<string> VirtualFileNames(IDataObject dataObject)
    {
        var names = new List<string>();
        var format = DescriptorFormat();
        STGMEDIUM medium = default;
        try
        {
            dataObject.GetData(&format, out medium);
            // The source is another program: only a memory block, and only as many descriptors as it really holds (a bad
            // count would read past it, which .NET cannot catch; M8c review I5).
            if (medium.tymed != TYMED.TYMED_HGLOBAL) return names;
            var size = (long)(nuint)PInvoke.GlobalSize(medium.u.hGlobal);
            var group = (FILEGROUPDESCRIPTORW*)PInvoke.GlobalLock(medium.u.hGlobal);
            if (group is null) return names;
            try
            {
                if (size < sizeof(uint) || group->cItems > (size - sizeof(uint)) / sizeof(FILEDESCRIPTORW)) return names;
                var descriptors = &group->fgd.e0;
                for (var index = 0; index < group->cItems; index++)
                {
                    var name = descriptors[index].cFileName.ToString();
                    if (!name.Contains('\\')) names.Add(name); // files in subfolders arrive inside their folder
                }
            }
            finally
            {
                PInvoke.GlobalUnlock(medium.u.hGlobal);
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // No descriptors: nothing to place; Windows still handles the drop.
        }
        finally
        {
            if (medium.u.hGlobal.Value != null) PInvoke.ReleaseStgMedium(ref medium);
        }
        return names;
    }

    private static FORMATETC DescriptorFormat() => new()
    {
        cfFormat = FileGroupDescriptorFormat,
        dwAspect = (uint)DVASPECT.DVASPECT_CONTENT,
        lindex = -1,
        tymed = (uint)TYMED.TYMED_HGLOBAL,
    };

    /// <summary>Paths in the drop's file list (CF_HDROP); empty for virtual items (zip contents, phones).</summary>
    internal static unsafe List<string> DroppedFiles(IDataObject dataObject)
    {
        var files = new List<string>();
        var format = new FORMATETC
        {
            cfFormat = (ushort)CLIPBOARD_FORMAT.CF_HDROP,
            dwAspect = (uint)DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = (uint)TYMED.TYMED_HGLOBAL,
        };
        STGMEDIUM medium = default;
        try
        {
            dataObject.GetData(&format, out medium);
            var drop = (HDROP)(nint)medium.u.hGlobal.Value;
            var count = PInvoke.DragQueryFile(drop, uint.MaxValue, default, 0);
            var buffer = new char[32768];
            for (uint index = 0; index < count; index++)
            {
                var length = PInvoke.DragQueryFile(drop, index, buffer);
                files.Add(new string(buffer, 0, (int)length));
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // No file list: nothing to place; Windows still handles the drop.
        }
        finally
        {
            if (medium.u.hGlobal.Value != null) PInvoke.ReleaseStgMedium(ref medium);
        }
        return files;
    }

    /// <summary>
    /// IDropTarget for one fence. Forwards to the shell (the hovered item's drop target, or the Desktop folder's) for
    /// real file drops, and keeps Windows' drag image visible over the fence (IDropTargetHelper).
    /// </summary>
    private sealed unsafe class FenceDropTarget(HWND fence, FenceDropHandlers handlers, Func<string?>? portalFolder) : IDropTarget, IDisposable
    {
        private IDataObject? _dataObject;
        private bool _desktopItemsOnly;      // desktop fence: items already on the Desktop (membership only)
        private string? _portalFolder;       // Portal fence: the folder it shows (drops are real file operations)
        private bool _sameFolderOnly;        // Portal fence: items already in that folder (nothing to do)
        private string? _hoveredItem;        // item whose own drop target is active
        private readonly Dictionary<string, bool> _containers = new(StringComparer.OrdinalIgnoreCase); // per drag: DragOver runs per mouse move
        private bool _overRecycleBin;         // NeoFences recycles itself here (never the Recycle Bin's own drop)
        private bool _virtualSource;         // zip contents, phones: never asked for CF_HDROP (that extracts every file)
        private IDropTarget? _shellTarget;   // forwarded-to target while it is entered
        private IDropTargetHelper? _imageHelper;

        public void DragEnter(IDataObject pDataObj, MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect)
        {
            LeaveShellTarget(); // a previous drag that failed half-way must not leak into this one
            Reset();
            _dataObject = pDataObj;
            _portalFolder = portalFolder?.Invoke();
            // A virtual source (zip contents, a phone) would extract every file to answer CF_HDROP: not on enter (M3b review).
            _virtualSource = CurrentDrag is null && OffersVirtualFiles(pDataObj);
            // A virtual source (zip contents, a phone) would extract every file to answer CF_HDROP: not here, not on drop (M3b/M8c review).
            IReadOnlyList<string> dragged = CurrentDrag ?? (_virtualSource ? [] : DroppedFiles(pDataObj));
            _desktopItemsOnly = _portalFolder is null && dragged.Count > 0 && dragged.All(DesktopNamespace.IsDesktopItem);
            _sameFolderOnly = _portalFolder is not null && dragged.Count > 0
                && dragged.All(itemRef => string.Equals(Path.GetDirectoryName(itemRef), _portalFolder, StringComparison.OrdinalIgnoreCase));
            _imageHelper = TryCreateImageHelper();
            var allowed = *pdwEffect;
            Update(grfKeyState, pt, pdwEffect, allowed);
            var effect = *pdwEffect;
            WithImageHelper(helper =>
            {
                var point = new System.Drawing.Point(pt.x, pt.y);
                helper.DragEnter(fence, pDataObj, &point, effect);
            });
        }

        public void DragOver(MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect)
        {
            Update(grfKeyState, pt, pdwEffect, *pdwEffect);
            var effect = *pdwEffect;
            WithImageHelper(helper =>
            {
                var point = new System.Drawing.Point(pt.x, pt.y);
                helper.DragOver(&point, effect);
            });
        }

        public void DragLeave()
        {
            LeaveShellTarget();
            WithImageHelper(helper => helper.DragLeave());
            handlers.ShowFeedback(null, false);
            Reset();
        }

        public void Drop(IDataObject pDataObj, MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect)
        {
            var allowed = *pdwEffect;
            try
            {
                Update(grfKeyState, pt, pdwEffect, allowed);
                var shownEffect = *pdwEffect;
                WithImageHelper(helper =>
                {
                    var point = new System.Drawing.Point(pt.x, pt.y);
                    helper.Drop(pDataObj, &point, shownEffect);
                });
                var drop = handlers.HitTest(pt.x, pt.y);
                if (_overRecycleBin)
                {
                    handlers.Recycle(CurrentDrag ?? (_virtualSource ? [] : DroppedFiles(pDataObj))); // always the Recycle Bin, never a delete
                    *pdwEffect = DROPEFFECT.DROPEFFECT_NONE;
                    return;
                }
                if (_shellTarget is null && _sameFolderOnly)
                {
                    *pdwEffect = DROPEFFECT.DROPEFFECT_NONE; // a Portal item dropped back into its own folder
                    return;
                }
                if (_shellTarget is null)
                {
                    // Membership only. Report "none" so the source never deletes anything after a "move".
                    handlers.MoveItems(CurrentDrag ?? DroppedFiles(pDataObj), drop.InsertAt);
                    *pdwEffect = DROPEFFECT.DROPEFFECT_NONE;
                    return;
                }
                if (_hoveredItem is null && _portalFolder is null)
                {
                    // Desktop items in a mixed drag join this fence; the rest arrive through Windows (M3b review).
                    var files = CurrentDrag ?? (_virtualSource ? [] : DroppedFiles(pDataObj));
                    var onDesktop = files.Where(DesktopNamespace.IsDesktopItem).ToList();
                    if (onDesktop.Count > 0) handlers.MoveItems(onDesktop, drop.InsertAt);
                    var arriving = files.Count > 0 ? files.Except(onDesktop).Select(Path.GetFileName).OfType<string>().ToList() : VirtualFileNames(pDataObj);
                    // Files and their possible shortcuts are separate lists at the same place: each file keeps its own slot
                    // (one interleaved list spread the files over every other position; M8c review I2).
                    handlers.ExpectArrivals(DesktopRefsFor(arriving), drop.InsertAt + onDesktop.Count);
                    handlers.ExpectArrivals(DesktopRefsFor(arriving.Select(ShortcutName)), drop.InsertAt + onDesktop.Count);
                }
                var target = _shellTarget;
                _shellTarget = null; // Drop replaces DragLeave for it
                try
                {
                    *pdwEffect = allowed; // the source's choices, so a right-button drop menu offers them all
                    target.Drop(pDataObj, grfKeyState, pt, pdwEffect);
                }
                finally
                {
                    Marshal.ReleaseComObject(target);
                }
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                handlers.LogFailure(failure);
                *pdwEffect = DROPEFFECT.DROPEFFECT_NONE;
            }
            finally
            {
                LeaveShellTarget();
                handlers.ShowFeedback(null, false);
                Reset();
            }
        }

        public void Dispose()
        {
            PInvoke.RevokeDragDrop(fence);
            Reset();
        }

        /// <summary>Picks where the drag would land now and asks it for the effect (or computes ours).</summary>
        private void Update(MODIFIERKEYS_FLAGS keys, POINTL pt, DROPEFFECT* effect, DROPEFFECT allowed)
        {
            try
            {
                var drop = handlers.HitTest(pt.x, pt.y);
                var hovered = drop.ItemRef;
                var dropsOnItem = hovered is not null && !(CurrentDrag?.Contains(hovered, StringComparer.OrdinalIgnoreCase) ?? false)
                                  && IsDropContainer(hovered);
                var wantedItem = dropsOnItem ? hovered : null;
                var wantsShell = dropsOnItem || !(_desktopItemsOnly || _sameFolderOnly);

                handlers.ShowFeedback(drop, wantedItem is not null);
                // The Recycle Bin's own drop target deletes permanently with Shift held, or refuses: NeoFences recycles
                // itself and only shows "move" here, whatever keys are held (M3b review C1).
                _overRecycleBin = string.Equals(wantedItem, DesktopItems.RecycleBinRef, StringComparison.OrdinalIgnoreCase);
                if (_overRecycleBin)
                {
                    LeaveShellTarget();
                    *effect = allowed & DROPEFFECT.DROPEFFECT_MOVE;
                    return;
                }
                if (_shellTarget is not null && (!wantsShell || wantedItem != _hoveredItem)) LeaveShellTarget();
                if (!wantsShell)
                {
                    // Desktop fence: a membership move, nothing on disk changes. Portal: already in this folder.
                    *effect = _sameFolderOnly ? DROPEFFECT.DROPEFFECT_NONE : allowed & DROPEFFECT.DROPEFFECT_MOVE;
                    return;
                }
                if (_shellTarget is null)
                {
                    _shellTarget = (IDropTarget)(wantedItem is null
                        ? (_portalFolder is null ? DesktopNamespace.DesktopDropTarget(fence) : DesktopNamespace.FolderDropTarget(fence, _portalFolder))
                        : DesktopNamespace.GetUIObject(fence, [wantedItem], typeof(IDropTarget).GUID));
                    _hoveredItem = wantedItem;
                    *effect = allowed;
                    _shellTarget.DragEnter(_dataObject!, keys, pt, effect);
                    return;
                }
                *effect = allowed;
                _shellTarget.DragOver(keys, pt, effect);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                handlers.LogFailure(failure);
                *effect = DROPEFFECT.DROPEFFECT_NONE;
            }
        }

        /// <summary>The drag image is cosmetic: a failing helper must not break the drop or leave state behind.</summary>
        private void WithImageHelper(Action<IDropTargetHelper> call)
        {
            if (_imageHelper is null) return;
            try { call(_imageHelper); }
            catch (Exception failure) when (failure is not OutOfMemoryException) { handlers.LogFailure(failure); }
        }

        private void LeaveShellTarget()
        {
            if (_shellTarget is null) return;
            try { _shellTarget.DragLeave(); }
            catch (Exception failure) when (failure is not OutOfMemoryException) { handlers.LogFailure(failure); }
            Marshal.ReleaseComObject(_shellTarget);
            _shellTarget = null;
            _hoveredItem = null;
        }

        private bool IsDropContainer(string itemRef)
        {
            if (!_containers.TryGetValue(itemRef, out var container)) _containers[itemRef] = container = DesktopNamespace.IsDropContainer(fence, itemRef);
            return container;
        }

        private void Reset()
        {
            _containers.Clear();
            _dataObject = null;
            _desktopItemsOnly = false;
            _portalFolder = null;
            _sameFolderOnly = false;
            _overRecycleBin = false;
            _virtualSource = false;
            if (_imageHelper is not null) Marshal.ReleaseComObject(_imageHelper);
            _imageHelper = null;
        }

        private static IDropTargetHelper? TryCreateImageHelper()
        {
            try
            {
                return (IDropTargetHelper)Activator.CreateInstance(Type.GetTypeFromCLSID(PInvoke.CLSID_DragDropHelper)!)!;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                return null; // no drag image over fences; the drop still works
            }
        }

        /// <summary>Where Windows will put dropped files: the user's Desktop, same names. Renamed copies ("name (2)") go to the Inbox.</summary>
        private static List<string> DesktopRefsFor(IEnumerable<string> fileNames) =>
            fileNames.Select(name => Path.Combine(DesktopItems.UserDesktop, name)).ToList();

        /// <summary>The name Windows gives a shortcut it makes on a drop (Alt, or a link-only source).</summary>
        // ponytail: the English " - Shortcut" suffix; other display languages name links differently (their links go to the Inbox).
        private static string ShortcutName(string fileName) => fileName + " - Shortcut.lnk";
    }
}
```

- [ ] **Step 3: Build and test**

Run: `dotnet build NeoFences.slnx`, then `dotnet test NeoFences.slnx`
Expected: 0 warnings, 0 errors (the App still builds: nothing it uses changed shape); `Passed: 283`.

- [ ] **Step 4: Commit**

```powershell
git add src/NeoFences.Shell
git commit -m "feat: added drive removal notices for portal folders, told watcher failures apart and kept virtual drops from being extracted"
```

---

### Task 2: App — Portal release and backoff, Enter on several items, M8c minors

**Files:** replace `src/NeoFences.App/SystemMessageWindow.cs`, `PortalState.cs`, `FenceWindow.xaml.cs` and `FenceHost.cs`.

**Interfaces:**
- Consumes: Task 1; Core `WatcherBackoff` (M8c).
- Produces:
  - `SystemMessageWindow.DeviceRemovalRequested(nint)`;
  - `PortalState(root, noticeOwner, show, logFailure)` and `ReleaseForRemoval(nint) → bool`;
  - `FenceWindow.OpenManyRequested`.

- [ ] **Step 1: Files**

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
    /// <summary>The Recycle Bin (or another icon image) changed: refresh the special icons (M8c).</summary>
    public event Action? SpecialIconsChanged;
    /// <summary>Windows asks to remove the drive holding this registered handle (Safely Remove, Eject): release it now (M8d).</summary>
    public event Action<nint>? DeviceRemovalRequested;

    /// <summary>False when Windows refused unlock notices (very early at sign-in): the hook is then not re-installed on unlock.</summary>
    public bool SessionNotificationsActive { get; }

    /// <summary>Receives the global hotkeys (Peek, M5).</summary>
    public nint Handle => _source.Handle;

    public SystemMessageWindow()
    {
        _source = new HwndSource(new HwndSourceParameters("NeoFences.SystemMessages") { WindowStyle = 0, Width = 0, Height = 0 });
        _source.AddHook(OnMessage);
        SessionNotificationsActive = SessionNotifications.Register(_source.Handle);
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        switch (message)
        {
            case DeviceRemovalNotice.WmDeviceChange when wParam == DeviceRemovalNotice.QueryRemove && DeviceRemovalNotice.HandleOf(lParam) is var removed and not 0:
                DeviceRemovalRequested?.Invoke(removed); // handles closed before returning: Windows then removes the drive
                handled = true;
                return 1; // TRUE: removal allowed
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
        if (SpecialIconNotifications.IsRecycleBinNotice(message))
        {
            SpecialIconsChanged?.Invoke();
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
    private DeviceRemovalNotice? _removal;   // asks before the Portal's drive is removed (USB stick, M8d)
    private readonly nint _noticeOwner;
    private readonly DispatcherTimer _backoffTimer;
    private TimeSpan _failureDelay = NeoFences.Core.Lifecycle.WatcherBackoff.First;
    private DateTime _lastArm = DateTime.MinValue;
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
    /// <param name="noticeOwner">The window that receives "may this drive be removed?" (the app's message window).</param>
    public PortalState(string root, nint noticeOwner, Action<IReadOnlyList<ItemInfo>?> show, Action<Exception> logFailure)
    {
        _noticeOwner = noticeOwner;
        _backoffTimer = new DispatcherTimer();
        _backoffTimer.Tick += (_, _) =>
        {
            _backoffTimer.Stop();
            if (!_paused) Refresh();
        };
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
            var removal = watcher.IsWatching ? DeviceRemovalNotice.TryRegister(_noticeOwner, folder, _logFailure) : null;
            var items = FolderItems.TryList(folder);
            dispatcher.BeginInvoke(() =>
            {
                if (generation == _generation) _refreshing = false;
                if (_disposed || generation != _generation)
                {
                    watcher.Dispose(); // a newer refresh (or the fence's deletion) superseded this one
                    removal?.Dispose();
                    return;
                }
                _watcher?.Dispose();
                _watcher = watcher;
                _removal?.Dispose();
                _removal = removal;
                _lastArm = DateTime.UtcNow;
                watcher.Changed += () => dispatcher.BeginInvoke(ScheduleRefresh);
                watcher.Failed += () => dispatcher.BeginInvoke(OnWatcherFailed);
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

    /// <summary>
    /// Windows asks to remove the drive holding this handle: close everything the Portal holds there, so "Safely Remove"
    /// and Eject work while it is shown (M8d). The Portal shows the folder as unavailable and comes back by itself when
    /// the drive does (the retry timer), or right away if the removal was refused.
    /// </summary>
    public bool ReleaseForRemoval(nint handle)
    {
        if (_removal is null || _removal.Handle != handle) return false;
        ++_generation; // a listing in flight must not re-open the folder
        _refreshing = false;
        _refreshTimer.Stop();
        _backoffTimer.Stop();
        _watcher?.Dispose();
        _watcher = null;
        _removal.Dispose();
        _removal = null;
        _listedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _show(null);
        _retryTimer.Start();
        return true;
    }

    /// <summary>A watcher that fails again soon after each re-arm waits longer each time, up to a minute (M8d, M4 review).</summary>
    private void OnWatcherFailed()
    {
        if (_disposed || _backoffTimer.IsEnabled) return;
        _failureDelay = NeoFences.Core.Lifecycle.WatcherBackoff.Next(_failureDelay, lastRearm: _lastArm, failureAt: DateTime.UtcNow);
        _backoffTimer.Interval = _failureDelay;
        _backoffTimer.Start();
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
        _backoffTimer.Stop();
        _watcher?.Dispose();
        _watcher = null;
        _removal?.Dispose();
        _removal = null;
    }
}
```
`src/NeoFences.App/FenceWindow.xaml.cs`:
```csharp
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using NeoFences.Core.Config;
using NeoFences.Core.Layouts;
using NeoFences.Core.Model;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>One fence on the desktop. Placement and persistence are the host's job; this window reports what the user did.</summary>
public partial class FenceWindow : Window
{
    private const int WmWindowPosChanging = 0x0046;
    private const int WmSizing = 0x0214;
    private const int WmMoving = 0x0216;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const int WmNcLeftButtonDoubleClick = 0x00A3;
    private const int HitTestCaption = 2;
    private const double CornerRadiusDips = 8;
    private const double CaptionHeightDips = 30;
    private const double ResizeBorderDips = 6;

    // The menu lists ConfigNormalizer.IconSizes (one list, M2c review carry-over); these are only their names.
    private static readonly Dictionary<int, string> IconSizeNames = new() { [32] = "Small", [48] = "Medium", [64] = "Large", [96] = "Extra large" };

    private readonly ObservableCollection<FenceItemView> _items = [];
    private readonly IconLoader _iconLoader;
    private ListBoxItem? _hoverLabelContainer; // the cell the pop-under name belongs to
    private int _iconSizeDips;
    private bool _renaming;
    private string _title = "";
    private readonly bool _isPortal;
    private DragTracker? _drag;
    private bool _locked;
    private bool _rolledUp;
    private readonly RollUpExpansion _expansion; // rolled up, but open right now (hover or click, M5/M6b)
    private int _fullHeightPx;              // height when not rolled up (physical pixels)
    private readonly System.Windows.Threading.DispatcherTimer _hoverTimer;
    private readonly System.Windows.Threading.DispatcherTimer _heightAnimation;
    private System.Diagnostics.Stopwatch _heightClock = new();
    private int _heightFrom;
    private int _heightTo;
    private int _fadeGeneration;
    private int? _moveHeightPx; // a move that interrupted a roll-up animation keeps this height (M6b review M2)
    private static readonly TimeSpan RollUpDuration = TimeSpan.FromMilliseconds(200); // spec §6
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(150)); // spec §6

    public string FenceId { get; }

    public nint Handle { get; private set; }

    /// <summary>Windows refused the rounded-corner preference (Windows 10); the host logs it once.</summary>
    public bool CornersUnavailable { get; private set; }

    /// <summary>Peek (M5): while set the fence may rise above apps instead of staying at the bottom.</summary>
    public bool Peeking { get; set; }

    /// <summary>
    /// Asked before each roll-up or fade (spec §6): off when Windows' "Animation effects" are off, and in game mode
    /// (spec §4.7). Set by the host.
    /// </summary>
    public Func<bool> AnimationsAllowed { get; set; } = () => false;

    /// <summary>Asked while the user drags an edge or the title: returns where the window should go (snapping).</summary>
    public Func<PixelRect, SnapEdges, PixelRect>? SnapRect { get; set; }

    /// <summary>Raised after the user finishes moving or resizing, with the new physical-pixel rect.</summary>
    public event Action<FenceWindow, PixelRect>? MovedByUser;

    public event Action? NewFenceRequested;
    public event Action<bool>? TakeoverToggled;
    public event Action? ExitRequested;
    public event Action<string>? OpenRequested;
    /// <summary>Enter with several items selected: open each (a Portal does not browse into one of several folders, M8d).</summary>
    public event Action<IReadOnlyList<string>>? OpenManyRequested;
    /// <summary>The first-run question was answered: true = hide the desktop icons.</summary>
    public event Action<bool>? TakeoverPromptAnswered;
    public event Action<string>? RenameRequested;
    public event Action<int>? IconSizeRequested;
    public event Action<bool>? LockToggled;
    public event Action? DeleteRequested;
    /// <summary>Right-click (or the menu key) on items: show Windows' item menu for these refs at this screen point (px).</summary>
    /// <summary>Right-click or menu key on items: refs (the clicked item first), screen point, opened from the keyboard.</summary>
    public event Action<IReadOnlyList<string>, int, int, bool>? ItemMenuRequested;
    /// <summary>Del: send these items to the Recycle Bin.</summary>
    public event Action<IReadOnlyList<string>>? RecycleRequested;
    /// <summary>An in-place rename was confirmed: item ref, new name as typed.</summary>
    public event Action<string, string>? ItemRenameRequested;
    /// <summary>The user started dragging these items out of the fence (M3b).</summary>
    public event Action<IReadOnlyList<string>>? DragRequested;
    /// <summary>Portal (M4): back to the parent folder (Back button, Backspace).</summary>
    public event Action? BackRequested;
    public event Action? NewPortalRequested;
    public event Action<FenceSort>? SortRequested;
    public event Action? OpenFolderRequested;
    /// <summary>The "Start with Windows" toggle changed (ADR-019).</summary>
    public event Action<bool>? StartupToggled;
    /// <summary>"Settings…" in the fence menu (M6b).</summary>
    public event Action? SettingsRequested;
    /// <summary>Double-click on the title: roll up to the title bar, or back down (M5).</summary>
    public event Action? RollUpToggled;
    /// <summary>Fence menu → Labels (M8b): always, or only on hover / selection.</summary>
    public event Action<LabelMode>? LabelModeRequested;

    private Point? _pressPoint;                 // left button pressed on an item: a drag may start
    private ListBoxItem? _deferredSelect;       // pressed on an already selected item: select it alone only on release
    private Point? _bandStart;                  // left button pressed on empty space: rubber band
    private ListBoxItem? _deferredToggle;       // Ctrl+press on a selected item: unselect it on release unless it was dragged
    private LabelMode _labelMode = LabelMode.Always;
    private ListBoxItem? _hoveredContainer;
    private (uint At, Point Where)? _lastCaptionPress; // a title double-click recognised by NeoFences itself (M8b); message time
    private uint? _recognisedDoubleClickAt;

    public FenceWindow(Fence fence, bool takeoverActive, bool lightTheme, IconLoader iconLoader, RollupExpand rollupExpand)
    {
        _expansion = new RollUpExpansion(rollupExpand);
        FenceId = fence.Id;
        _iconLoader = iconLoader;
        InitializeComponent();
        _title = fence.Title;
        TitleText.Text = fence.Title;
        _isPortal = fence.Source.Kind == FenceSourceKind.Portal;
        OpenFolderItem.Visibility = _isPortal ? Visibility.Visible : Visibility.Collapsed;
        DeleteItem.Header = _isPortal ? "Delete fence (the folder is not touched)" : "Delete fence (items go to the Inbox)";
        // Desktop fences sort once (dragging keeps working); Portals keep the chosen order live, so it is checked.
        foreach (var (sort, name) in new[] { (FenceSort.Name, "Name"), (FenceSort.Type, "Type"), (FenceSort.Date, "Date (newest first)") })
        {
            var sortItem = new MenuItem { Header = name, Tag = sort, IsCheckable = _isPortal, IsChecked = _isPortal && fence.Sort == sort };
            sortItem.Click += (_, _) => SortRequested?.Invoke(sort);
            SortItem.Items.Add(sortItem);
        }
        NewPortalItem.Click += (_, _) => NewPortalRequested?.Invoke();
        OpenFolderItem.Click += (_, _) => OpenFolderRequested?.Invoke();
        BackButton.Click += (_, _) => BackRequested?.Invoke();
        TakeoverItem.IsChecked = takeoverActive;
        DeleteItem.Visibility = fence.IsInbox ? Visibility.Collapsed : Visibility.Visible;
        foreach (var size in ConfigNormalizer.IconSizes)
        {
            var sizeItem = new MenuItem { Header = IconSizeNames.GetValueOrDefault(size, $"{size} px"), Tag = size, IsCheckable = true };
            sizeItem.Click += (_, _) => IconSizeRequested?.Invoke(size);
            IconSizeItem.Items.Add(sizeItem);
        }
        NewFenceItem.Click += (_, _) => NewFenceRequested?.Invoke();
        RenameItem.Click += (_, _) => BeginRename();
        LockItem.Click += (_, _) => LockToggled?.Invoke(LockItem.IsChecked);
        DeleteItem.Click += (_, _) => DeleteRequested?.Invoke();
        TakeoverItem.Click += (_, _) => TakeoverToggled?.Invoke(TakeoverItem.IsChecked);
        ExitItem.Click += (_, _) => ExitRequested?.Invoke();
        StartupItem.Click += (_, _) => StartupToggled?.Invoke(StartupItem.IsChecked);
        SettingsItem.Click += (_, _) => SettingsRequested?.Invoke();
        LabelsAlwaysItem.Click += (_, _) => LabelModeRequested?.Invoke(LabelMode.Always);
        LabelsOnHoverItem.Click += (_, _) => LabelModeRequested?.Invoke(LabelMode.OnHover);
        TitleBox.MaxLength = FenceEdits.MaxTitleLength; // the cut in FenceEdits.Rename never surprises the user (M2c review)
        ItemList.SelectionChanged += (_, _) => UpdateHoverLabel();
        ItemList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => UpdateHoverLabel()));
        HoverLabel.SizeChanged += (_, _) => PlaceHoverLabel(); // the bound name arrived or changed (final review I3)
        Loaded += (_, _) => ReloadIcons(); // placed on its monitor: one load at the right size (mixed DPI, M2b review)
        ItemList.ItemsSource = _items;
        ItemList.MouseDoubleClick += OnItemDoubleClick;
        ItemList.PreviewMouseLeftButtonDown += OnListPress;
        ItemList.PreviewMouseMove += OnListMove;
        ItemList.PreviewMouseLeftButtonUp += OnListRelease;
        ItemList.LostMouseCapture += (_, _) => EndBand();
        ItemList.KeyDown += OnItemListKeyDown;
        Body.ContextMenuOpening += OnBodyContextMenuOpening;
        TitleBox.KeyDown += OnTitleBoxKeyDown;
        TitleBox.LostKeyboardFocus += (_, _) => EndRename(commit: true);
        PromptHideButton.Click += (_, _) => TakeoverPromptAnswered?.Invoke(true);
        PromptLaterButton.Click += (_, _) => TakeoverPromptAnswered?.Invoke(false);
#if DEBUG
        // Checklist B11: a hung fence UI thread must not freeze the desktop or taskbar (owner input-queue attachment, ADR-011).
        var freezeItem = new MenuItem { Header = "Debug: freeze this UI thread for 10 s (B11)" };
        freezeItem.Click += (_, _) => Thread.Sleep(TimeSpan.FromSeconds(10));
        BodyContextMenu.Items.Add(freezeItem);
#endif
        ApplyTheme(lightTheme);
        _labelMode = fence.Labels;
        SetIconSize(fence.IconSize);
        SetLabelMode(fence.Labels);
        _rolledUp = fence.RolledUp;
        // Rolled up, the fence opens on hover or click (setting) and closes again shortly after the pointer leaves.
        _hoverTimer = new System.Windows.Threading.DispatcherTimer { Interval = HoverTick };
        _hoverTimer.Tick += (_, _) => OnHoverTick();
        if (_rolledUp) _hoverTimer.Start();
        _heightAnimation = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        _heightAnimation.Tick += (_, _) => StepHeight();
        Closed += (_, _) =>
        {
            _hoverTimer.Stop(); // a deleted fence must not keep ticking on its dead handle (M5 review M1)
            _heightAnimation.Stop();
        };
        SetLocked(fence.Locked);
        // Unlocked, the title is caption (WM_NCLBUTTONDBLCLK); locked, it is client area.
        TitleBar.MouseLeftButtonDown += (_, click) =>
        {
            if (click.ClickCount == 2) RollUpToggled?.Invoke();
            else if (click.ClickCount == 1) ClickToOpen();
        };
        SourceInitialized += OnSourceInitialized;
        // Icons are rendered for one DPI. DpiChanged is routed: every new item raises it too, so react only to the window's own.
        DpiChanged += (_, dpiChange) =>
        {
            if (dpiChange.OriginalSource != this || dpiChange.OldDpi.PixelsPerDip == dpiChange.NewDpi.PixelsPerDip) return;
            // A rolled-up fence keeps its full height in physical pixels: rescale it with the monitor (M5 review carry-over).
            _fullHeightPx = (int)Math.Round(_fullHeightPx * dpiChange.NewDpi.DpiScaleY / dpiChange.OldDpi.DpiScaleY);
            ReloadIcons();
        };
    }

    public void SetTakeoverChecked(bool active) => TakeoverItem.IsChecked = active;

    public void SetStartupChecked(bool startWithWindows) => StartupItem.IsChecked = startWithWindows;

    public void ShowTakeoverPrompt(bool visible) => TakeoverPrompt.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public void SetTitle(string title)
    {
        _title = title;
        TitleText.Text = title;
    }

    /// <summary>Portal (M4): what the title shows while browsing ("Downloads › Mods"), and whether Back is offered.</summary>
    public void SetPortalLocation(string breadcrumb, bool canGoBack)
    {
        TitleText.Text = breadcrumb;
        BackButton.Visibility = canGoBack ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Portal (M4): a message instead of items (folder missing or unreadable); null hides it.</summary>
    public void ShowPortalMessage(string? message)
    {
        PortalMessage.Text = message ?? "";
        PortalMessage.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Portal (M4): the sort shown as checked.</summary>
    public void SetSortChecked(FenceSort sort)
    {
        foreach (var sortItem in SortItem.Items.OfType<MenuItem>()) sortItem.IsChecked = _isPortal && (FenceSort)sortItem.Tag == sort;
    }

    /// <summary>
    /// Shows exactly these items in this order, by moving, adding and removing views in place: items already shown keep
    /// their name, icon, selection and the scroll position (M2b review carry-over; duplicate refs are tolerated).
    /// </summary>
    public void SetItems(IReadOnlyList<string> itemRefs)
    {
        if (_items.Select(view => view.ItemRef).SequenceEqual(itemRefs, StringComparer.Ordinal)) return;
        var wanted = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var itemRef in itemRefs) wanted[itemRef] = wanted.GetValueOrDefault(itemRef) + 1;
        for (var index = _items.Count - 1; index >= 0; index--)
        {
            var itemRef = _items[index].ItemRef;
            if (wanted.GetValueOrDefault(itemRef) > 0) wanted[itemRef]--;
            else RemoveItemAt(index);
        }
        // ponytail: O(n²) moves in the worst case (a full reorder of hundreds of items); a keyed diff when that shows up.
        var iconSizePx = IconSizePx;
        for (var index = 0; index < itemRefs.Count; index++)
        {
            if (index < _items.Count && _items[index].ItemRef == itemRefs[index]) continue;
            var found = -1;
            for (var later = index + 1; later < _items.Count && found < 0; later++)
            {
                if (_items[later].ItemRef == itemRefs[index]) found = later;
            }
            if (found >= 0)
            {
                CancelRename(_items[found]);
                _items.Move(found, index);
                continue;
            }
            var view = new FenceItemView(itemRefs[index]);
            if (IsLoaded) _iconLoader.Request(view, iconSizePx); // before that, Loaded requests them at the right DPI (M2b review)
            _items.Insert(index, view);
        }
        // Cells may have shifted under a shown name without a scroll or selection event (final review I3).
        Dispatcher.BeginInvoke(UpdateHoverLabel, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// A rename whose own item is moved or removed would lose its box (and commit half-typed text): cancel only that one,
    /// right before. An item that merely shifts keeps its box (M3a review I3; final review M1).
    /// </summary>
    private static void CancelRename(FenceItemView view)
    {
        if (view.IsEditing) view.IsEditing = false;
    }

    private void RemoveItemAt(int index)
    {
        CancelRename(_items[index]);
        _items.RemoveAt(index);
    }

    private int IconSizePx => (int)Math.Round(_iconSizeDips * VisualTreeHelper.GetDpi(this).DpiScaleX);

    /// <summary>One of <see cref="ConfigNormalizer.IconSizes"/> (DIPs). Icons are reloaded at the new size.</summary>
    public void SetIconSize(int iconSizeDips)
    {
        if (iconSizeDips == _iconSizeDips) return; // nothing to reload (M2c review carry-over)
        _iconSizeDips = iconSizeDips;
        Resources["IconSize"] = (double)iconSizeDips;
        Resources["ArrowSize"] = Math.Max(12.0, Math.Round(iconSizeDips * 0.36));
        ApplyItemWidth();
        foreach (var sizeItem in IconSizeItem.Items.OfType<MenuItem>()) sizeItem.IsChecked = (int)sizeItem.Tag == iconSizeDips;
        ReloadIcons();
    }

    /// <summary>Labels always, or icons only with the name popping under the hovered or selected icon (M8b, user choice).</summary>
    public void SetLabelMode(LabelMode labelMode)
    {
        _labelMode = labelMode;
        Resources["LabelVisibility"] = labelMode == LabelMode.Always ? Visibility.Visible : Visibility.Collapsed;
        Resources["ItemToolTips"] = labelMode == LabelMode.Always; // the pop-under name replaces the tooltip
        LabelsAlwaysItem.IsChecked = labelMode == LabelMode.Always;
        LabelsOnHoverItem.IsChecked = labelMode == LabelMode.OnHover;
        ApplyItemWidth();
        UpdateHoverLabel();
    }

    /// <summary>Settings → "Show shortcut arrows" (M8b, off by default).</summary>
    public void SetShortcutArrows(bool show) => Resources["ShortcutArrowVisibility"] = show ? Visibility.Visible : Visibility.Collapsed;

    private void ApplyItemWidth()
    {
        Resources["EditItemWidth"] = LabelledItemWidth;
        ApplyCellWidth();
    }

    private void ApplyCellWidth() =>
        // With labels: room for two short words under small icons. Icons only: a tight grid.
        Resources["ItemWidth"] = _labelMode == LabelMode.Always ? LabelledItemWidth : _iconSizeDips + 12.0;

    private double LabelledItemWidth => Math.Max(76.0, _iconSizeDips + 28.0);

    private void OnItemMouseEnter(object sender, MouseEventArgs args)
    {
        _hoveredContainer = sender as ListBoxItem;
        UpdateHoverLabel();
    }

    private void OnItemMouseLeave(object sender, MouseEventArgs args)
    {
        if (ReferenceEquals(_hoveredContainer, sender)) _hoveredContainer = null;
        UpdateHoverLabel();
    }

    /// <summary>
    /// Icon-only fences: the name of the hovered item (else the one selected item) pops up under its icon, over the
    /// neighbours, kept inside the fence (above the icon when there is no room below). Drawn in the fence window itself,
    /// so it stays at the fence's place in the window stack (no popup window above other apps).
    /// </summary>
    private void UpdateHoverLabel()
    {
        var container = _labelMode != LabelMode.OnHover ? null
            : _hoveredContainer ?? (ItemList.SelectedItems.Count == 1 ? ItemList.ItemContainerGenerator.ContainerFromItem(ItemList.SelectedItem) as ListBoxItem : null);
        if (container is not { DataContext: FenceItemView { IsEditing: false } view, IsVisible: true })
        {
            HoverLabel.Visibility = Visibility.Collapsed;
            return;
        }
        _hoverLabelContainer = container;
        HoverLabel.DataContext = view; // bound: a name that loads later shows up (final review I3)
        // Never wider than the fence (narrow icon-only fences, final review I1); 18 = padding + border + margins.
        HoverLabelText.MaxWidth = Math.Clamp(ItemList.ActualWidth - 18, 24, 180);
        HoverLabel.Visibility = Visibility.Visible;
        PlaceHoverLabel();
    }

    /// <summary>Under the cell, clamped to the fence's sides; above it when there is no room; hidden when it is scrolled away.</summary>
    private void PlaceHoverLabel()
    {
        if (HoverLabel.Visibility != Visibility.Visible || _hoverLabelContainer is not { IsVisible: true } container) return;
        HoverLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = HoverLabel.DesiredSize;
        var cell = container.TransformToAncestor(ItemList).TransformBounds(new Rect(container.RenderSize));
        if (cell.Bottom <= 0 || cell.Top >= ItemList.ActualHeight)
        {
            HoverLabel.Visibility = Visibility.Collapsed; // a selected item scrolled out of view (final review I1)
            return;
        }
        var left = Math.Clamp(cell.Left + cell.Width / 2 - size.Width / 2, 2, Math.Max(2, ItemList.ActualWidth - size.Width - 2));
        var top = cell.Bottom - 2;
        if (top + size.Height > ItemList.ActualHeight - 2) top = Math.Max(2, cell.Top - size.Height + 2);
        Canvas.SetLeft(HoverLabel, left);
        Canvas.SetTop(HoverLabel, top);
    }

    /// <summary>Locked: the title no longer drags and the edges no longer resize.</summary>
    public void SetLocked(bool locked)
    {
        LockItem.IsChecked = locked;
        _locked = locked;
        ApplyChrome();
    }

    /// <summary>Locked: no drag, no resize. Rolled up: drag, no resize (its height is the stored full height).</summary>
    private void ApplyChrome()
    {
        // A fresh WindowChrome each time: editing the attached one in place is not re-applied after an unlock.
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            GlassFrameThickness = new Thickness(0),
            CaptionHeight = _locked ? 0 : CaptionHeightDips,
            ResizeBorderThickness = new Thickness(_locked || _rolledUp ? 0 : ResizeBorderDips),
            // WindowChrome owns the window region and re-applies it on every resize: a radius of 0 kept resetting it to
            // a square, so the blur showed outside the rounded border (user screenshot 2026-10-03). Same radius as the border.
            CornerRadius = new CornerRadius(CornerRadiusDips),
            UseAeroCaptionButtons = false,
        });
    }

    /// <summary>Puts the fence at its full rect (physical pixels); rolled up, only the title bar of it shows.</summary>
    public void Place(PixelRect fullRect)
    {
        _heightAnimation.Stop();
        _fullHeightPx = fullRect.Height;
        FenceWindowChrome.SetPixelRect(Handle, _rolledUp && !_expansion.Expanded ? fullRect with { Height = RolledUpHeightPx } : fullRect);
    }

    public void SetRolledUp(bool rolledUp)
    {
        if (rolledUp == _rolledUp) return;
        var current = FenceWindowChrome.GetPixelRect(Handle);
        if (rolledUp && !_expansion.Expanded) _fullHeightPx = _heightAnimation.IsEnabled ? _heightTo : current.Height;
        _rolledUp = rolledUp;
        _expansion.Reset();
        ApplyChrome();
        AnimateHeight(rolledUp ? RolledUpHeightPx : _fullHeightPx);
        if (rolledUp) _hoverTimer.Start();
        else _hoverTimer.Stop();
    }

    /// <summary>Roll-up expand mode from settings (M6b): hover or click.</summary>
    public void SetRollupExpand(RollupExpand mode) => _expansion.Mode = mode;

    /// <summary>Shows at once (Pause, layout): a quick-hide fade still running must not hide the fence afterwards (M8a).</summary>
    public void ShowNow()
    {
        ++_fadeGeneration;
        BeginAnimation(OpacityProperty, null);
        Show();
    }

    /// <summary>Hides at once (Pause).</summary>
    public void HideNow()
    {
        ++_fadeGeneration;
        BeginAnimation(OpacityProperty, null);
        Hide();
    }

    /// <summary>Quick-hide (spec §6): a 150 ms fade, then hidden. Without animations it hides at once.</summary>
    public void HideFaded()
    {
        var generation = ++_fadeGeneration;
        if (!IsVisible || !AnimationsAllowed())
        {
            BeginAnimation(OpacityProperty, null);
            Hide();
            return;
        }
        var fade = new DoubleAnimation(0, FadeDuration);
        fade.Completed += (_, _) =>
        {
            if (generation != _fadeGeneration) return; // shown again meanwhile
            Hide();
            BeginAnimation(OpacityProperty, null);
        };
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Shows the fence (fading in when animations are on). The host sends it to the bottom afterwards.</summary>
    public void ShowFaded()
    {
        ++_fadeGeneration;
        var fadeIn = AnimationsAllowed();
        BeginAnimation(OpacityProperty, null);
        Show();
        if (fadeIn) BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, FadeDuration));
    }

    /// <summary>Click mode: a single click on the rolled-up title opens it.</summary>
    private bool ClickToOpen()
    {
        if (!_rolledUp || !_expansion.Click()) return false;
        AnimateHeight(_fullHeightPx);
        return true;
    }

    /// <summary>Roll-up and roll-down move the bottom edge over 200 ms (ease-out); at once without animations.</summary>
    private void AnimateHeight(int targetPx)
    {
        var current = FenceWindowChrome.GetPixelRect(Handle);
        if (!AnimationsAllowed() || _drag is not null || current.Height == targetPx)
        {
            _heightAnimation.Stop();
            FenceWindowChrome.SetPixelRect(Handle, current with { Height = targetPx });
            return;
        }
        _heightFrom = current.Height;
        _heightTo = targetPx;
        _heightClock = System.Diagnostics.Stopwatch.StartNew();
        _heightAnimation.Start();
    }

    private void StepHeight()
    {
        var current = FenceWindowChrome.GetPixelRect(Handle);
        var progress = Math.Min(1.0, _heightClock.Elapsed / RollUpDuration);
        if (_drag is not null) progress = 1; // never fight a move: jump to the end
        var eased = 1 - Math.Pow(1 - progress, 3);
        FenceWindowChrome.SetPixelRect(Handle, current with { Height = (int)Math.Round(_heightFrom + (_heightTo - _heightFrom) * eased) });
        if (progress >= 1) _heightAnimation.Stop();
    }

    private static readonly TimeSpan HoverTick = TimeSpan.FromMilliseconds(100);

    /// <summary>Title row plus the 1-DIP border above and below it.</summary>
    private int RolledUpHeightPx => (int)Math.Round((CaptionHeightDips + 2) * VisualTreeHelper.GetDpi(this).DpiScaleY);

    // ponytail: polls the cursor every 100 ms while rolled up (no mouse-leave on a no-activate layered window when
    // the pointer leaves fast); in hover mode it also opens during a file drag, which is wanted.
    private void OnHoverTick()
    {
        if (Handle == 0 || !_rolledUp) return;
        if (_drag is not null || BodyContextMenu.IsOpen || _renaming || _items.Any(view => view.IsEditing)) return; // never close under the user
        var (cursorX, cursorY) = FenceWindowChrome.GetCursorPosition();
        var rect = FenceWindowChrome.GetPixelRect(Handle);
        // While the height animates, judge "inside" against where the fence is going, so it does not flicker shut.
        var height = _heightAnimation.IsEnabled ? Math.Max(rect.Height, _heightTo) : rect.Height;
        var inside = cursorX >= rect.X && cursorX < rect.X + rect.Width && cursorY >= rect.Y && cursorY < rect.Y + height;
        if (!_expansion.Tick(inside)) return;
        AnimateHeight(_expansion.Expanded ? _fullHeightPx : RolledUpHeightPx);
    }

    /// <summary>Colours for Windows' light or dark app mode (M2c: fences follow Windows).</summary>
    public void ApplyTheme(bool light)
    {
        var ink = light ? Colors.Black : Colors.White;
        SolidColorBrush Ink(byte alpha)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, ink.R, ink.G, ink.B));
            brush.Freeze();
            return brush;
        }
        // The accent blur ignores its tint colour (ACCENT_ENABLE_BLURBEHIND), so the veil is drawn here: none in dark mode
        // (the look the user approved in M2a), a dense light veil in light mode so dark text reads on any wallpaper.
        var veil = new SolidColorBrush(light ? Color.FromArgb(0xB8, 0xF2, 0xF2, 0xF2) : Colors.Transparent);
        veil.Freeze();
        Resources["FenceVeil"] = veil;
        Resources["FenceText"] = Ink(light ? (byte)0xE6 : (byte)0xFF);
        Resources["FenceSubtleText"] = Ink(0xA0);
        Resources["FenceBorder"] = Ink(light ? (byte)0x33 : (byte)0x40);
        Resources["FenceDivider"] = Ink(light ? (byte)0x1F : (byte)0x26);
        Resources["FenceHover"] = Ink(light ? (byte)0x14 : (byte)0x22);
        Resources["FenceSelected"] = Ink(light ? (byte)0x2A : (byte)0x44);
        Resources["FenceScrollThumb"] = Ink(light ? (byte)0x44 : (byte)0x55);
        var hoverLabel = new SolidColorBrush(light ? Color.FromArgb(0xF0, 0xF4, 0xF4, 0xF4) : Color.FromArgb(0xE6, 0x20, 0x22, 0x28));
        hoverLabel.Freeze();
        Resources["HoverLabelBackground"] = hoverLabel;
        // Soft halo behind labels, like desktop icon labels: readable on busy or bright wallpapers (user choice).
        var shadow = new DropShadowEffect
        {
            Color = light ? Colors.White : Colors.Black,
            ShadowDepth = light ? 0 : 1,
            BlurRadius = 4,
            Opacity = 0.9,
        };
        shadow.Freeze();
        Resources["LabelShadow"] = shadow;
    }

    /// <summary>The Recycle Bin turned full or empty, or another special icon changed (M8c).</summary>
    public void ReloadSpecialIcons()
    {
        var iconSizePx = IconSizePx;
        foreach (var view in _items.Where(view => view.ItemRef.StartsWith("::", StringComparison.Ordinal))) _iconLoader.Request(view, iconSizePx);
    }

    /// <summary>New size or DPI: every icon is requested again in place; names, selection and renames stay (M2c review carry-over).</summary>
    private void ReloadIcons()
    {
        var iconSizePx = IconSizePx;
        foreach (var view in _items) _iconLoader.Request(view, iconSizePx);
    }

    /// <summary>Starts renaming the fence title (menu, or a freshly drawn fence).</summary>
    public void BeginRename()
    {
        _renaming = true;
        TitleBox.Text = _title;
        TitleText.Visibility = Visibility.Hidden;
        TitleBox.Visibility = Visibility.Visible;
        Activate(); // keyboard input needs the fence active; it stays at the bottom (owned by Progman, ADR-011)
        // With another app in front the fence may not get focus (ADR-015); then typing would go elsewhere and the box
        // could never close, so give up instead (M2c review).
        if (!TitleBox.Focus() || !IsActive)
        {
            EndRename(commit: false);
            return;
        }
        TitleBox.SelectAll();
    }

    private void EndRename(bool commit)
    {
        if (!_renaming) return;
        _renaming = false;
        TitleBox.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
        if (commit && TitleBox.Text != _title) RenameRequested?.Invoke(TitleBox.Text);
    }

    private void OnTitleBoxKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key is not (Key.Enter or Key.Escape)) return;
        EndRename(commit: args.Key == Key.Enter);
        ItemList.Focus();
        args.Handled = true;
    }

    private void OnItemListKeyDown(object sender, KeyEventArgs args)
    {
        if (args.OriginalSource is TextBox) return; // keys typed into the rename box
        var selected = ItemList.SelectedItems.OfType<FenceItemView>().ToList();
        switch (args.Key)
        {
            case Key.Enter when selected.Count == 1:
                OpenRequested?.Invoke(selected[0].ItemRef);
                break;
            case Key.Enter when selected.Count > 1:
                OpenManyRequested?.Invoke(selected.Select(view => view.ItemRef).ToList());
                break;
            case Key.Delete when selected.Count > 0:
                RecycleRequested?.Invoke(selected.Select(view => view.ItemRef).ToList()); // Shift+Del too: always the Recycle Bin
                break;
            case Key.Back when _isPortal:
                BackRequested?.Invoke();
                break;
            case Key.F2 when selected.Count == 1:
                BeginItemRename(selected[0].ItemRef);
                break;
            default:
                return;
        }
        args.Handled = true;
    }

    /// <summary>Starts renaming one item in place (F2, or Rename in Windows' item menu). Special items cannot be renamed.</summary>
    public void BeginItemRename(string itemRef)
    {
        var view = _items.FirstOrDefault(candidate => candidate.ItemRef == itemRef);
        if (view is null || itemRef.StartsWith("::", StringComparison.Ordinal)) return;
        ItemList.ScrollIntoView(view);
        view.EditName = view.Label;
        view.IsEditing = true; // the box shows; OnLabelBoxVisibleChanged focuses it
    }

    private void OnLabelBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not TextBox { IsVisible: true, DataContext: FenceItemView view } box) return;
        Activate();
        // Without focus typing would go to another app and the box could never close (ADR-015): give up instead.
        if (!box.Focus() || !IsActive)
        {
            view.IsEditing = false;
            return;
        }
        // Like Explorer: select the name, not the extension.
        var extensionStart = box.Text.LastIndexOf('.');
        box.Select(0, extensionStart > 0 ? extensionStart : box.Text.Length);
    }

    private void OnLabelBoxKeyDown(object sender, KeyEventArgs args)
    {
        if (sender is not TextBox { DataContext: FenceItemView view } || args.Key is not (Key.Enter or Key.Escape)) return;
        EndItemRename(view, commit: args.Key == Key.Enter);
        ItemList.Focus();
        args.Handled = true;
    }

    private void OnLabelBoxLostFocus(object sender, KeyboardFocusChangedEventArgs args)
    {
        if (sender is TextBox { DataContext: FenceItemView view }) EndItemRename(view, commit: true);
    }

    private void EndItemRename(FenceItemView view, bool commit)
    {
        if (!view.IsEditing) return;
        view.IsEditing = false;
        var newName = view.EditName.Trim();
        if (commit && newName.Length > 0 && newName != view.Label) ItemRenameRequested?.Invoke(view.ItemRef, newName);
    }

    /// <summary>Right-click on an item opens Windows' item menu instead of the fence menu.</summary>
    private void OnBodyContextMenuOpening(object sender, ContextMenuEventArgs args)
    {
        if (ItemsControl.ContainerFromElement(ItemList, (DependencyObject)args.OriginalSource) is not ListBoxItem { DataContext: FenceItemView clicked } container) return;
        args.Handled = true;
        if (!container.IsSelected)
        {
            ItemList.SelectedItems.Clear();
            container.IsSelected = true;
        }
        // From the mouse, or (menu key: CursorLeft < 0) from the item's corner. PointToScreen gives physical pixels.
        var anchor = args.CursorLeft >= 0 ? PointToScreen(Mouse.GetPosition(this)) : container.PointToScreen(new Point(container.ActualWidth / 2, container.ActualHeight / 2));
        // The clicked item first: menu → Rename renames it, whatever else is selected (user choice 2026-10-03).
        List<string> refs = [clicked.ItemRef, .. ItemList.SelectedItems.OfType<FenceItemView>().Select(view => view.ItemRef).Where(itemRef => itemRef != clicked.ItemRef)];
        ItemMenuRequested?.Invoke(refs, (int)anchor.X, (int)anchor.Y, args.CursorLeft < 0);
    }

    /// <summary>The item under a screen point (physical pixels) and the index to insert before when dropping there.</summary>
    public FenceDropPoint HitTest(int screenX, int screenY)
    {
        var point = ItemList.PointFromScreen(new Point(screenX, screenY));
        string? hovered = null;
        var cells = new List<(double Left, double Top, double Width, double Height)>();
        var cellIndexes = new List<int>();
        for (var index = 0; index < _items.Count; index++)
        {
            if (ItemList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container) continue;
            var bounds = container.TransformToAncestor(ItemList).TransformBounds(new Rect(container.RenderSize));
            // Only the middle of an item means "into it" (folders, Recycle Bin); its edges reorder (M3b review I2).
            if (DropZones.IsInto(bounds.Left, bounds.Top, bounds.Width, bounds.Height, point.X, point.Y)) hovered = _items[index].ItemRef;
            cells.Add((bounds.Left, bounds.Top, bounds.Width, bounds.Height));
            cellIndexes.Add(index);
        }
        // Rows reach down to their tallest item (mixed label heights, M3b review).
        var cellAt = DropZones.InsertIndex(cells, point.X, point.Y);
        return new FenceDropPoint(hovered, cellAt < cellIndexes.Count ? cellIndexes[cellAt] : _items.Count);
    }

    /// <summary>Shows where a drag would land: a caret before the insert position, or a highlight on the container taking it.</summary>
    public void ShowDropFeedback(FenceDropPoint? drop, bool into)
    {
        InsertCaret.Visibility = Visibility.Collapsed;
        DropHighlight.Visibility = Visibility.Collapsed;
        if (drop is not { } point) return;
        if (into && _items.FirstOrDefault(view => view.ItemRef == point.ItemRef) is { } target
            && ItemList.ItemContainerGenerator.ContainerFromItem(target) is ListBoxItem targetContainer)
        {
            var cell = targetContainer.TransformToAncestor(ItemList).TransformBounds(new Rect(targetContainer.RenderSize));
            Canvas.SetLeft(DropHighlight, cell.Left);
            Canvas.SetTop(DropHighlight, cell.Top);
            DropHighlight.Width = cell.Width;
            DropHighlight.Height = cell.Height;
            DropHighlight.Visibility = Visibility.Visible;
            return;
        }
        // Caret at the left edge of the item it goes before, or after the last item.
        var before = point.InsertAt < _items.Count ? ItemList.ItemContainerGenerator.ContainerFromIndex(point.InsertAt) as ListBoxItem : null;
        var anchor = before ?? (_items.Count > 0 ? ItemList.ItemContainerGenerator.ContainerFromIndex(_items.Count - 1) as ListBoxItem : null);
        if (anchor is null) return;
        var bounds = anchor.TransformToAncestor(ItemList).TransformBounds(new Rect(anchor.RenderSize));
        Canvas.SetLeft(InsertCaret, (before is null ? bounds.Right : bounds.Left) - 1);
        Canvas.SetTop(InsertCaret, bounds.Top + 4);
        InsertCaret.Height = Math.Max(0, bounds.Height - 8);
        InsertCaret.Visibility = Visibility.Visible;
    }

    private static TAncestor? FindAncestor<TAncestor>(DependencyObject source) where TAncestor : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TAncestor match) return match;
        }
        return null;
    }

    private void OnListPress(object sender, MouseButtonEventArgs args)
    {
        if (args.OriginalSource is DependencyObject source && FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) is not null) return;
        // Text selection in the rename box must never start a drag of the file (M3b review I3).
        if (args.OriginalSource is DependencyObject pressed && FindAncestor<TextBox>(pressed) is not null) return;
        var container = args.OriginalSource is DependencyObject element ? FindAncestor<ListBoxItem>(element) : null;
        if (container is null)
        {
            // Empty space: rubber band. Without Ctrl it starts a new selection. The list takes focus, so a pending rename
            // commits and the keys after the band go to the list (M3b review carry-over).
            ItemList.Focus();
            _bandStart = args.GetPosition(ItemList);
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) ItemList.SelectedItems.Clear();
            ItemList.CaptureMouse();
            args.Handled = true;
            return;
        }
        _pressPoint = args.GetPosition(ItemList);
        // Ctrl+press on a selected item: unselect it on release, not now, so Ctrl+drag can still copy the selection (M3b review).
        if (container.IsSelected && Keyboard.Modifiers == ModifierKeys.Control && args.ClickCount == 1)
        {
            _deferredToggle = container;
            container.Focus();
            args.Handled = true;
            return;
        }
        // Pressing one of several selected items must keep the selection, so they can be dragged together.
        if (container.IsSelected && ItemList.SelectedItems.Count > 1 && Keyboard.Modifiers == ModifierKeys.None && args.ClickCount == 1)
        {
            _deferredSelect = container;
            container.Focus();
            args.Handled = true;
        }
    }

    private void OnListMove(object sender, MouseEventArgs args)
    {
        if (args.LeftButton != MouseButtonState.Pressed)
        {
            _pressPoint = null;
            return;
        }
        var position = args.GetPosition(ItemList);
        if (_bandStart is { } bandStart)
        {
            UpdateBand(bandStart, position);
            return;
        }
        if (_pressPoint is not { } pressPoint) return;
        if (Math.Abs(position.X - pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _pressPoint = null;
        _deferredSelect = null;
        _deferredToggle = null; // dragged: the item stays selected
        var dragged = ItemList.SelectedItems.OfType<FenceItemView>().Select(view => view.ItemRef).ToList();
        if (dragged.Count > 0) DragRequested?.Invoke(dragged); // returns when the drag ends (Windows' modal loop)
    }

    private void OnListRelease(object sender, MouseButtonEventArgs args)
    {
        _pressPoint = null;
        if (_deferredToggle is { } toggled)
        {
            toggled.IsSelected = false;
            _deferredToggle = null;
        }
        if (_deferredSelect is { } container)
        {
            ItemList.SelectedItems.Clear();
            container.IsSelected = true;
            _deferredSelect = null;
        }
        if (_bandStart is not null)
        {
            ItemList.ReleaseMouseCapture(); // ends the band via LostMouseCapture
            args.Handled = true;
        }
    }

    /// <summary>Selects every item the band touches (added to the selection when Ctrl was held at the start).</summary>
    private void UpdateBand(Point start, Point current)
    {
        var band = new Rect(start, current);
        Canvas.SetLeft(SelectionBand, band.Left);
        Canvas.SetTop(SelectionBand, band.Top);
        SelectionBand.Width = band.Width;
        SelectionBand.Height = band.Height;
        SelectionBand.Visibility = Visibility.Visible;
        for (var index = 0; index < _items.Count; index++)
        {
            if (ItemList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container) continue;
            var bounds = container.TransformToAncestor(ItemList).TransformBounds(new Rect(container.RenderSize));
            if (bounds.IntersectsWith(band)) container.IsSelected = true;
            else if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) container.IsSelected = false;
        }
    }

    private void EndBand()
    {
        _bandStart = null;
        SelectionBand.Visibility = Visibility.Collapsed;
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left) return;
        // A double-click in the rename box selects a word; it must not open the file (M3a review carry-over).
        if (args.OriginalSource is DependencyObject source && FindAncestor<TextBox>(source) is not null) return;
        if (ItemsControl.ContainerFromElement(ItemList, (DependencyObject)args.OriginalSource) is ListBoxItem { DataContext: FenceItemView view })
            OpenRequested?.Invoke(view.ItemRef);
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        Handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(Handle).AddHook(OnMessage);
        FenceWindowChrome.ApplyToolWindowStyles(Handle);
        FenceWindowChrome.ApplyAccentBlur(Handle);
        if (!FenceWindowChrome.UseRoundedCorners(Handle)) CornersUnavailable = true; // Windows 10: square blur corners (ADR-024)
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // Windows' item menu draws "Send to", "Open with" and shell-extension entries through its owner window.
        if (ShellItemMenu.HandleMenuMessage(message, wParam, lParam, out var menuResult))
        {
            handled = true;
            return menuResult;
        }
        switch (message)
        {
            case WmWindowPosChanging:
                if (!Peeking) FenceWindowChrome.KeepAtBottom(lParam); // fences never rise above apps, except during Peek
                break;
            // Click mode: the first press on a rolled-up title opens it instead of starting a move (M6b).
            case WmNcLeftButtonDown when wParam == HitTestCaption && IsSecondCaptionClick(lParam):
                RollUpToggled?.Invoke();
                handled = true;
                return 0;
            case WmNcLeftButtonDown when wParam == HitTestCaption && ClickToOpen():
                handled = true;
                return 0;
            case WmNcLeftButtonDoubleClick when wParam == HitTestCaption:
                _lastCaptionPress = null;
                // A third quick click after a recognised double-click arrives as DBLCLK: not a second toggle (M8b review).
                if (!IsRightAfterRecognisedDoubleClick()) RollUpToggled?.Invoke();
                _recognisedDoubleClickAt = null; // used once: a later genuine double-click toggles (M8c review M12)
                handled = true;
                return 0;
            case WmEnterSizeMove:
                // A move that starts during a roll-up animation finishes it. Windows' move loop captured the partial rect
                // and proposes it on every step, so WM_MOVING keeps the finished height too (M6b review M2).
                _moveHeightPx = null;
                if (_heightAnimation.IsEnabled)
                {
                    _heightAnimation.Stop();
                    _moveHeightPx = _heightTo;
                    FenceWindowChrome.SetPixelRect(Handle, FenceWindowChrome.GetPixelRect(Handle) with { Height = _heightTo });
                }
                _drag = new DragTracker(FenceWindowChrome.GetPixelRect(Handle));
                break;
            case WmMoving when SnapRect is not null && _drag is not null:
                var proposed = FenceWindowChrome.ReadRect(lParam);
                if (_moveHeightPx is { } heightPx) proposed = proposed with { Height = heightPx };
                FenceWindowChrome.WriteRect(lParam, _drag.Step(proposed, snap: rect => SnapRect(rect, SnapEdges.Move)));
                handled = true;
                return 1;
            case WmSizing when SnapRect is not null && _drag is not null:
                var edges = SizingEdges((int)wParam);
                FenceWindowChrome.WriteRect(lParam, _drag.Step(FenceWindowChrome.ReadRect(lParam), snap: rect => SnapRect(rect, edges)));
                handled = true;
                return 1;
            case WmExitSizeMove:
                _drag = null;
                _moveHeightPx = null;
                // Rolled up, the window is shorter than the fence: the stored rect keeps the full height.
                var moved = FenceWindowChrome.GetPixelRect(Handle);
                MovedByUser?.Invoke(this, _rolledUp ? moved with { Height = _fullHeightPx } : moved);
                if (_rolledUp && !_expansion.Expanded && moved.Height != RolledUpHeightPx) AnimateHeight(RolledUpHeightPx); // a move during an animation
                break;
        }
        return 0;
    }

    /// <summary>
    /// Every caption press is remembered; a second one within the double-click time and size counts as a double-click.
    /// Right after another window hands the fence the foreground, Windows sends the second press as a plain
    /// WM_NCLBUTTONDOWN instead of WM_NCLBUTTONDBLCLK, so the first double-click did nothing (M6a finding 4, M8b).
    /// </summary>
    /// <param name="pointParam">The press's screen point (WM_NCLBUTTONDOWN's lParam): where it happened, not where the
    /// cursor is when a busy UI thread gets to it; its time is the message's own (M8b review).</param>
    private bool IsSecondCaptionClick(nint pointParam)
    {
        var (milliseconds, widthPx, heightPx) = FenceWindowChrome.DoubleClickSettings();
        var at = FenceWindowChrome.MessageTime();
        var (x, y) = ((short)(pointParam & 0xFFFF), (short)((pointParam >> 16) & 0xFFFF));
        var isSecond = _lastCaptionPress is { } last && unchecked(at - last.At) <= milliseconds
                       && Math.Abs(x - last.Where.X) <= widthPx / 2.0 && Math.Abs(y - last.Where.Y) <= heightPx / 2.0;
        _lastCaptionPress = isSecond ? null : (at, new Point(x, y));
        if (isSecond) _recognisedDoubleClickAt = at;
        return isSecond;
    }

    private bool IsRightAfterRecognisedDoubleClick() =>
        _recognisedDoubleClickAt is { } recognised && unchecked(FenceWindowChrome.MessageTime() - recognised) <= FenceWindowChrome.DoubleClickSettings().Milliseconds;

    /// <summary>WM_SIZING's WMSZ_* value as the edges being dragged.</summary>
    private static SnapEdges SizingEdges(int sizingEdge) => sizingEdge switch
    {
        1 => SnapEdges.Left,
        2 => SnapEdges.Right,
        3 => SnapEdges.Top,
        4 => SnapEdges.Top | SnapEdges.Left,
        5 => SnapEdges.Top | SnapEdges.Right,
        6 => SnapEdges.Bottom,
        7 => SnapEdges.Bottom | SnapEdges.Left,
        8 => SnapEdges.Bottom | SnapEdges.Right,
        _ => SnapEdges.None,
    };
}
```
`src/NeoFences.App/FenceHost.cs`:
```csharp
using System.IO;
using System.Windows;
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
    private static readonly TimeSpan TrayRetryInterval = TimeSpan.FromSeconds(2);
    private const int TrayRetryAttempts = 15;

    private readonly ConfigStore _store = new(AppPaths.DataDirectory);
    private readonly Watchdog _watchdog = new(AppPaths.DataDirectory, message => Log.Information("watchdog: {Message}", message));
    private readonly SystemMessageWindow _messages = new();
    private readonly Dictionary<string, FenceWindow> _windows = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _saveTimer;
    private readonly IconLoader _iconLoader = new(Dispatcher.CurrentDispatcher);
    private DesktopWatcher? _desktopWatcher;
    private readonly ShellWorker _shellWorker = new(); // open, recycle, rename: off the UI thread, on STA (M3a review)
    private SpecialIconNotifications? _specialIcons;
    private bool _specialIconsDeferred;
    private readonly DispatcherTimer _specialIconsTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    // Watcher trouble arrives in bursts: one re-arm and one reconcile per burst; a watcher that fails again at once waits longer (M8a review).
    private readonly DispatcherTimer _watcherRecoveryTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _rearmWatcher;
    private DateTime _lastWatcherRearm = DateTime.MinValue;
    private TimeSpan _watcherRearmDelay = WatcherBackoff.First;
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
    private bool _iconsHiddenByUser;        // RunState.IconsHiddenByUser: set when quick-hide begins, cleared when it ends
    private DrawFenceOverlay? _drawOverlay; // right-drag on the desktop: the fence being drawn
    private DispatcherTimer? _drawTimer;
    private (int X, int Y) _drawStart;
    private GlobalHotkey? _peekHotkey;
    private GlobalHotkey? _peekEscapeHotkey; // Esc ends Peek; registered only while peeking
    private bool _peeking;
    private bool _recordingHotkey;          // Settings' hotkey box has the keyboard: the Peek hotkey is released
    private string? _peekHotkeyProblem;     // why the Peek hotkey is not registered (Settings shows it), null when it is
    private bool _cornersLogged;
    // M6a
    private bool _paused;                    // tray: Pause NeoFences (not saved)
    private bool _gameMode;                  // a full-screen app is in front: idle (spec §4.7, ADR-021)
    private ForegroundWatcher? _foregroundWatcher;
    private TrayIcon? _trayIcon;
    private readonly List<DesktopChange> _deferredDesktopChanges = [];
    private bool _reconcileDeferred;
    private const int MaxDeferredDesktopChanges = 500;
    private const int TrayNewFence = 1, TrayQuickHide = 2, TrayPeek = 3, TrayPause = 4, TrayExit = 5, TraySettings = 6;
    private SettingsWindow? _settingsWindow; // M6b: one at a time

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
        _messages.SpecialIconsChanged += ScheduleSpecialIconRefresh;
        _messages.DeviceRemovalRequested += handle =>
        {
            foreach (var (fenceId, portal) in _portals)
            {
                if (portal.ReleaseForRemoval(handle)) Log.Information("drive of Portal {FenceId} is being removed: released it", fenceId);
            }
        };
        _specialIconsTimer.Tick += (_, _) => RefreshSpecialIcons();
        _watcherRecoveryTimer.Tick += (_, _) => RecoverDesktopWatcher();
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
        StartDesktopWatcher(); // first: an item created while the startup reconcile lists the desktop is not missed (M8a)
        ReconcileDesktop();
        StartSpecialIconNotifications();
        ApplyLayout();
        if (_config.Settings.Takeover) SetTakeover(true);
        else if (_watchdog.IsTakeoverActiveMarked)
        {
            // Icons may still be hidden from a run whose Takeover-off never got saved (both processes killed): show them.
            Log.Warning("takeover-active marker found while Takeover is off; showing desktop icons");
            SetIconsHidden(false);
        }
        StartGestures();
        if (!_messages.SessionNotificationsActive) Log.Warning("unlock notices unavailable: the mouse hook is re-installed only after an Explorer restart");
        try
        {
            _trayIcon = new TrayIcon(_messages.Handle, TrayTooltip(), log: message => Log.Warning("{Message}", message));
            ShowTrayIcon();
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Error(failure, "tray icon unavailable; fences and gestures keep working"); // hard rule 7: only the tray is lost
        }
        StartGameMode();
        ScheduleSave();
    }

    /// <summary>
    /// The clock for safe-save and drop memory: wall time at start plus a monotonic stopwatch, so a clock change (time
    /// sync, daylight saving, the user) cannot expire or extend a memory early (M8a).
    /// </summary>
    private static readonly DateTimeOffset ClockStart = DateTimeOffset.Now;
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static DateTimeOffset Now => ClockStart + Clock.Elapsed;

    /// <summary>All of NeoFences' run-time modes together (Core rules: which fences, icons and hooks they imply).</summary>
    private RunState Current => new(Takeover: _takeoverActive, QuickHidden: _quickHidden, Paused: _paused, GameMode: _gameMode,
        IconsHiddenByUser: _iconsHiddenByUser);

    /// <summary>
    /// Windows asked to end the session (WPF's SessionEnding, inside WM_QUERYENDSESSION). WPF then shuts the app
    /// down, which may be the last thing that ever runs, so the icons come back now and the watchdog is told it is
    /// a session end, not a user exit: if the user cancels the shutdown, the watchdog restarts NeoFences (ADR-013).
    /// </summary>
    public void OnSessionEnding()
    {
        _sessionEnding = true;
        ApplyDeferredShellWork(); // Desktop changes queued during a game must be saved too (M6a review M2)
        SaveNow();
        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) SetIconsHidden(false);
        TryMarker(() => _watchdog.MarkSessionEnding(Environment.ProcessId), what: "session-ending marker");
    }

    /// <summary>Orderly exit: save, bring icons back, tell the watchdog all is well.</summary>
    public void Shutdown()
    {
        ApplyDeferredShellWork(); // Desktop changes queued during a game must be saved too (M6a review M2)
        SaveNow();
        _trayIcon?.Dispose(); // also on session end: a cancelled shutdown restarts us, and the old icon would linger (M8a)
        _trayIcon = null;
        if (_sessionEnding) return; // OnSessionEnding already restored and marked; no clean marker, so a cancel restarts us
        // The marker also catches a show that failed earlier (quick-hide ending while Explorer was busy; M5 review M6).
        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) SetIconsHidden(false);
        TryMarker(() => _watchdog.MarkCleanShutdown(Environment.ProcessId), what: "clean-shutdown marker");
        _foregroundWatcher?.Dispose();
        _mouseHook?.Dispose();
        _peekHotkey?.Dispose();
        _peekEscapeHotkey?.Dispose();
        _desktopWatcher?.Dispose();
        _desktopWatcher = null;
        _specialIcons?.Dispose();
        _shellWorker.Dispose();
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
        var window = new FenceWindow(fence, takeoverActive: _takeoverActive, lightTheme: _lightTheme, iconLoader: _iconLoader, rollupExpand: _config.Settings.RollupExpand)
        {
            // Spec §6: no animations when Windows' "Animation effects" are off; spec §4.7: none while gaming.
            AnimationsAllowed = () => !_gameMode && SystemParameters.ClientAreaAnimation,
        };
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
        window.OpenManyRequested += itemRefs =>
        {
            foreach (var itemRef in itemRefs) OpenItem(itemRef, ownerHandle: window.Handle); // folders open in Explorer
            SetPeek(false);
        };
        window.ItemMenuRequested += (itemRefs, screenX, screenY, fromKeyboard) => ShowItemMenu(window, itemRefs, screenX, screenY, fromKeyboard);
        window.RecycleRequested += itemRefs => RecycleItems(window, itemRefs);
        window.ItemRenameRequested += (itemRef, newName) => RenameItem(window, itemRef, newName);
        window.BackRequested += () => BrowsePortal(window, back: true);
        window.NewPortalRequested += () => CreatePortal(window);
        window.StartupToggled += SetStartWithWindows;
        window.SettingsRequested += OpenSettings;
        window.LabelModeRequested += labels => SetFenceLabels(window, labels);
        window.SetShortcutArrows(_config.Settings.ShowShortcutArrows);
        window.SetStartupChecked(_config.Settings.StartWithWindows);
        window.SortRequested += sort => SortFence(window, sort);
        window.OpenFolderRequested += () => { if (_portals.TryGetValue(window.FenceId, out var portal)) OpenItem(portal.Current, ownerHandle: window.Handle); };
        if (fence.Source is { Kind: FenceSourceKind.Portal, Path: { } portalPath })
        {
            _portals[fence.Id] = new PortalState(portalPath, noticeOwner: _messages.Handle, show: items => ShowPortal(window, items),
                logFailure: failure => Log.Warning(failure, "cannot watch Portal folder of {FenceId}", fence.Id));
        }
        window.DragRequested += itemRefs =>
            ShellDragDrop.TryDrag(window.Handle, itemRefs, logFailure: failure => Log.Warning(failure, "could not start dragging {ItemRefs}", itemRefs));
        window.TakeoverPromptAnswered += AnswerTakeoverPrompt;
        window.RollUpToggled += () => ToggleRollUp(window);
        new WindowInteropHelper(window).EnsureHandle(); // HWND exists (styles, blur) before the first Show
        if (window.CornersUnavailable && !_cornersLogged)
        {
            _cornersLogged = true;
            Log.Information("Windows refused rounded corners (Windows 10?): fences keep square blur corners"); // M8a review
        }
        RegisterDrops(window); // needs the HWND
        if (!DesktopHost.AttachToDesktop(window.Handle)) Log.Warning("fence {FenceId}: not attached to the desktop yet (no Progman)", fence.Id);
        _windows[fence.Id] = window;
    }

    /// <summary>Full pass over the Desktop folders: at start and whenever watcher events were lost.</summary>
    private void ReconcileDesktop()
    {
        var listing = DesktopItems.Enumerate();
        var (reconciled, report) = FenceMembership.Reconcile(_config, listing.ItemRefs, listing.UnavailableFolders,
            remembered: _rememberedPlacements, now: Now);
        _config = reconciled;
        _rememberedPlacements = [.. _rememberedPlacements.Except(report.UsedMemories)]; // each memory places one item once (M8a review)
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
        _desktopWatcher.Overflowed += () => dispatcher.BeginInvoke(() => OnDesktopWatcherTrouble(rearm: true));
        _desktopWatcher.ReconcileNeeded += () => dispatcher.BeginInvoke(() => OnDesktopWatcherTrouble(rearm: false));
    }

    /// <summary>
    /// Events were lost or the watcher stopped (re-arm: .NET disables it after a non-overflow error), or one event could
    /// not be read (reconcile only). Bursts are gathered into one recovery (M8a review).
    /// </summary>
    private void OnDesktopWatcherTrouble(bool rearm)
    {
        if (_desktopWatcher is null) return; // shut down meanwhile
        if (rearm && !_rearmWatcher)
        {
            _rearmWatcher = true;
            // Measured when the failure arrives: the wait itself is not quiet time (Core WatcherBackoff, M8c review I3).
            _watcherRearmDelay = WatcherBackoff.Next(_watcherRearmDelay, lastRearm: _lastWatcherRearm, failureAt: DateTime.UtcNow);
            _watcherRecoveryTimer.Stop(); // a reconcile-only recovery already waiting now waits for the re-arm delay
            _watcherRecoveryTimer.Interval = _watcherRearmDelay;
            _watcherRecoveryTimer.Start();
            return;
        }
        if (_watcherRecoveryTimer.IsEnabled) return;
        _watcherRecoveryTimer.Interval = WatcherBackoff.First;
        _watcherRecoveryTimer.Start();
    }

    private void RecoverDesktopWatcher()
    {
        _watcherRecoveryTimer.Stop();
        if (_desktopWatcher is null) return;
        if (_rearmWatcher)
        {
            _rearmWatcher = false;
            _lastWatcherRearm = DateTime.UtcNow;
            Log.Warning("desktop watcher lost events or stopped; re-armed after {Delay} and reconciling", _watcherRearmDelay);
            _desktopWatcher.Dispose();
            StartDesktopWatcher();
        }
        else
        {
            Log.Information("a desktop change could not be read; reconciling");
        }
        if (Current.ShellWorkDeferred) _reconcileDeferred = true; // after the game
        else ReconcileDesktop();
    }

    /// <summary>Also after an Explorer restart: Explorer brokers the shell's change notices and forgets them (M8c review I6).</summary>
    private void StartSpecialIconNotifications()
    {
        _specialIcons?.Dispose();
        var dispatcher = Dispatcher.CurrentDispatcher;
        _specialIcons = new SpecialIconNotifications(_messages.Handle,
            settingsChanged: () => dispatcher.BeginInvoke(ScheduleSpecialIconRefresh),
            log: (what, failure) => Log.Warning(failure, "{What} unavailable: special icons change after a restart", what));
    }

    private void ScheduleSpecialIconRefresh()
    {
        _specialIconsTimer.Stop(); // a burst (several icons, a Recycle Bin emptying) is one refresh
        _specialIconsTimer.Start();
    }

    /// <summary>"Desktop icon settings" changed (reconcile), or the Recycle Bin turned full or empty (new icon) (M8c).</summary>
    private void RefreshSpecialIcons()
    {
        _specialIconsTimer.Stop();
        if (Current.ShellWorkDeferred)
        {
            _specialIconsDeferred = true; // games get every bit of the machine: after the game (M8c review)
            return;
        }
        var shown = DesktopItems.SpecialIconRefs().ToHashSet(ItemRef.Comparer);
        var fenced = _config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop).SelectMany(fence => fence.Items)
            .Where(itemRef => itemRef.StartsWith("::", StringComparison.Ordinal)).ToHashSet(ItemRef.Comparer);
        if (!shown.SetEquals(fenced))
        {
            Log.Information("desktop icon settings changed; reconciling");
            if (Current.ShellWorkDeferred) _reconcileDeferred = true;
            else ReconcileDesktop();
        }
        foreach (var window in _windows.Values) window.ReloadSpecialIcons();
        Log.Information("special icons refreshed");
    }

    private void OnDesktopChanged(DesktopChange change)
    {
        if (Current.ShellWorkDeferred)
        {
            // Applied in order when the game is left; a flood (a big download unpacking) becomes one reconcile instead (M8a).
            if (_deferredDesktopChanges.Count < MaxDeferredDesktopChanges) _deferredDesktopChanges.Add(change);
            else _reconcileDeferred = true;
            return;
        }
        (_config, _rememberedPlacements) = FenceMembership.Apply(_config, change, _rememberedPlacements, Now);
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

    private void OpenItem(string itemRef, nint ownerHandle)
    {
        // Off the UI thread: ShellExecute can block on a network timeout or a UAC prompt, freezing every fence (M2b review I6);
        // on an STA thread, as shell handlers and Windows' error dialog expect (M3a review).
        // Each open on its own thread: one waiting on an offline share (and its error dialog) holds up nothing else (M8c review I4).
        ShellWorker.RunAlone(() =>
        {
            if (!ShellItems.TryOpen(itemRef, ownerHandle)) Log.Warning("could not open {ItemRef}", itemRef);
        }, name: "NeoFences open");
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
                    _rememberedPlacements = FenceMembership.ExpectArrivals(_rememberedPlacements, itemRefs, window.FenceId, insertAt, Now),
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

    private void ShowItemMenu(FenceWindow window, IReadOnlyList<string> itemRefs, int screenX, int screenY, bool fromKeyboard)
    {
        // Shift+right-click is the extended menu; Shift+F10 is just the keyboard's normal menu (M3a review).
        var extended = !fromKeyboard && System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);
        var choice = ShellItemMenu.Show(window.Handle, itemRefs, screenX, screenY, extended,
            logFailure: failure => Log.Warning(failure, "item menu or its command failed for {ItemRefs}", itemRefs));
        switch (choice)
        {
            case ItemMenuChoice.Rename:
                window.BeginItemRename(itemRefs[0]); // the right-clicked item (it comes first)
                break;
            case ItemMenuChoice.Delete:
                RecycleItems(window, itemRefs); // always the Recycle Bin, even with Shift held (hard rule 1)
                break;
        }
    }

    /// <summary>
    /// Windows moves them to the Recycle Bin (with its own dialogs) on the shell worker, so a long recycle never freezes
    /// the fences (M3a review); the watcher then removes them from the fence.
    /// </summary>
    private void RecycleItems(FenceWindow window, IReadOnlyList<string> itemRefs)
    {
        Log.Information("recycling {Count} item(s)", itemRefs.Count);
        var ownerHandle = window.Handle;
        var dispatcher = Dispatcher.CurrentDispatcher;
        _shellWorker.Run(() =>
        {
            var (started, refused, missing) = ShellFileOps.TryRecycle(LiveOwner(ownerHandle), itemRefs);
            if (!started) Log.Warning("recycle did not run or was cancelled: {ItemRefs}", itemRefs);
            if (missing.Count > 0) Log.Information("skipped {Count} item(s) that no longer exist: {ItemRefs}", missing.Count, missing);
            if (refused.Count > 0) dispatcher.BeginInvoke(() => ExplainRefusedRecycle(window, refused));
        });
    }

    /// <summary>No owner when the fence was deleted while the operation waited: Windows' dialogs then stand alone (M8c review).</summary>
    private static nint LiveOwner(nint ownerHandle) => FenceWindowChrome.IsLiveWindow(ownerHandle) ? ownerHandle : 0;

    private static void ExplainRefusedRecycle(FenceWindow window, IReadOnlyList<string> refused)
    {
        Log.Information("not deleting {Count} item(s) on drives without a Recycle Bin", refused.Count);
        System.Windows.MessageBox.Show(window,
            (refused.Count == 1 ? $"\"{Path.GetFileName(refused[0])}\" is" : $"{refused.Count} items are") +
            " on a drive without a Recycle Bin (a USB stick or network drive), so NeoFences won't delete it." +
            " Deleting there would be permanent; if you really mean it, delete it in Explorer.",
            "NeoFences", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    /// <summary>Windows renames the file (on the shell worker: its conflict dialogs); the watcher keeps it in its fence and position.</summary>
    private void RenameItem(FenceWindow window, string itemRef, string newName)
    {
        var ownerHandle = window.Handle;
        _shellWorker.Run(() =>
        {
            if (!ShellFileOps.TryRename(LiveOwner(ownerHandle), itemRef, newName)) Log.Warning("rename did not run or was cancelled: {ItemRef}", itemRef);
        });
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
                    window.ShowNow();
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
            thresholdPx: (int)Math.Round(SnapThresholdDips * monitor.Scale),
            // A resize snap never makes the fence smaller than its minimum (M2c review carry-over).
            minWidthPx: (int)Math.Round(LayoutEngine.MinWidth * monitor.Scale),
            minHeightPx: (int)Math.Round(LayoutEngine.MinHeight * monitor.Scale));
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
        RefreshSettings();
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
        RefreshSettings();
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
        ShowTrayIcon(); // Explorer forgot every tray icon
        ReinstallMouseHook(); // a hook Windows dropped silently comes back here at the latest (M5 review)
        StartSpecialIconNotifications();
        ScheduleSpecialIconRefresh(); // the Recycle Bin may have changed meanwhile
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
        UpdatePeekHotkey();
    }

    /// <summary>The Peek hotkey is registered exactly while wanted: released to Windows and the game while paused or gaming.</summary>
    private void UpdatePeekHotkey()
    {
        // Released while Settings records a new one, so pressing the current combination is recorded, not Peek (M6b review).
        if ((Current.PeekHotkeyWanted && !_recordingHotkey) == _peekHotkey is not null) return;
        if (_peekHotkey is not null)
        {
            _peekHotkey.Dispose();
            _peekHotkey = null;
            return;
        }
        _peekHotkey = TryRegisterPeekHotkey(_config.Settings.PeekHotkey, out var problem);
        _peekHotkeyProblem = _peekHotkey is null ? problem : null;
        if (_peekHotkey is null) Log.Warning("Peek is off: {Problem}", problem);
    }

    private GlobalHotkey? TryRegisterPeekHotkey(string text, out string problem)
    {
        if (!Hotkey.TryParse(text, out var hotkey) || !Enum.TryParse<System.Windows.Input.Key>(hotkey.Key, ignoreCase: true, out var key))
        {
            problem = $"\"{text}\" is not a hotkey: it needs Ctrl, Alt, Shift or Win plus one key.";
            return null;
        }
        var virtualKey = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            problem = $"{hotkey.DisplayText} has no key Windows can watch for. Pick another key."; // would say "Saved" and never fire (M6b review M4)
            return null;
        }
        var registration = new GlobalHotkey(_messages.Handle, PeekHotkeyId);
        if (registration.TryRegister(hotkey, (uint)virtualKey))
        {
            problem = "";
            Log.Information("Peek hotkey {Hotkey} registered", hotkey);
            return registration;
        }
        registration.Dispose();
        problem = $"{hotkey.DisplayText} is already taken by Windows or another app. Pick another combination.";
        return null;
    }

    /// <summary>Settings: a new Peek hotkey. It is kept only if Windows accepts it; otherwise the old one stays.</summary>
    private (bool Saved, string Message) SetPeekHotkey(string text)
    {
        if (!Hotkey.TryParse(text, out var parsed)) return (false, $"\"{text}\" is not a hotkey: it needs Ctrl, Alt, Shift or Win plus one key.");
        // A global hotkey swallows its keys in every app (M6b review I1): only safe combinations are recorded.
        if (!parsed.IsSafeToRecord) return (false, $"{parsed.DisplayText} would stop working everywhere else (typing, Tab, closing windows). Use Alt or Win with a key, or an F-key.");
        var normalized = parsed.ToString();
        // The same combination again is a retry when it is not registered (Settings showed it as not active, M6b review).
        if (normalized == _config.Settings.PeekHotkey && _peekHotkeyProblem is null) return (true, $"Peek: {parsed.DisplayText}");
        _peekHotkey?.Dispose();
        _peekHotkey = null;
        var registration = TryRegisterPeekHotkey(normalized, out var problem);
        if (registration is null)
        {
            UpdatePeekHotkey(); // the old one again
            return (false, problem);
        }
        registration.Dispose(); // proven free; UpdatePeekHotkey registers it whenever it is wanted
        _peekHotkeyProblem = null;
        _config = _config with { Settings = _config.Settings with { PeekHotkey = normalized } };
        UpdatePeekHotkey();
        SaveNow();
        return (true, $"Saved. Peek: {parsed.DisplayText}");
    }

    /// <summary>Tray or fence menu → Settings… (M6b). One window; a second request brings it to the front.</summary>
    private void OpenSettings()
    {
        if (_settingsWindow is { } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        var window = new SettingsWindow();
        window.StartWithWindowsChanged += SetStartWithWindows;
        window.TakeoverChanged += SetTakeover;
        window.PeekHotkeyChosen += text =>
        {
            var (saved, message) = SetPeekHotkey(text);
            RefreshSettings();
            window.ShowHotkeyResult(saved, message);
        };
        window.RollupExpandChanged += SetRollupExpand;
        window.HotkeyRecording += recording =>
        {
            _recordingHotkey = recording;
            UpdatePeekHotkey();
            // Leaving the box re-registers: Settings must show if that failed (final review I5).
            if (!recording) RefreshSettings();
        };
        window.DefaultLabelsChanged += labels =>
        {
            _config = _config with { Settings = _config.Settings with { DefaultLabels = labels } };
            SaveNow();
        };
        window.LabelsAppliedToAll += labels =>
        {
            _config = FenceEdits.SetLabelsEverywhere(_config, labels);
            foreach (var fenceWindow in _windows.Values) fenceWindow.SetLabelMode(labels);
            Log.Information("labels for every fence: {Labels}", labels);
            SaveNow();
            RefreshSettings();
        };
        window.ShortcutArrowsChanged += show =>
        {
            _config = _config with { Settings = _config.Settings with { ShowShortcutArrows = show } };
            foreach (var fenceWindow in _windows.Values) fenceWindow.SetShortcutArrows(show);
            SaveNow();
        };
        window.GameModeChanged += SetGameModeEnabled;
        window.OpenLogsRequested += () => OpenItem(AppPaths.LogsDirectory, ownerHandle: 0);
        window.OpenDataRequested += () => OpenItem(AppPaths.DataDirectory, ownerHandle: 0);
        window.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _recordingHotkey = false;
            UpdatePeekHotkey();
        };
        _settingsWindow = window;
        RefreshSettings();
        window.Show();
        window.Activate();
    }

    private void RefreshSettings() => _settingsWindow?.Show(new SettingsView(
        StartWithWindows: _config.Settings.StartWithWindows,
        Takeover: _takeoverActive,
        PeekHotkey: PeekHotkeyDisplay,
        PeekHotkeyActive: _peekHotkeyProblem is null,
        RollupExpand: _config.Settings.RollupExpand,
        GameModeEnabled: _config.Settings.GameMode,
        GameModeActive: _gameMode,
        Version: typeof(FenceHost).Assembly.GetName().Version?.ToString(3) ?? "",
        DataFolder: AppPaths.DataDirectory,
        DefaultLabels: _config.Settings.DefaultLabels,
        ShowShortcutArrows: _config.Settings.ShowShortcutArrows));

    /// <summary>The Peek hotkey as a person reads it ("Ctrl+Shift+=", not "Ctrl+Shift+OemPlus").</summary>
    private string PeekHotkeyDisplay =>
        Hotkey.TryParse(_config.Settings.PeekHotkey, out var hotkey) ? hotkey.DisplayText : _config.Settings.PeekHotkey;

    /// <summary>Fence menu → Labels (M8b).</summary>
    private void SetFenceLabels(FenceWindow window, LabelMode labels)
    {
        _config = FenceEdits.SetLabels(_config, window.FenceId, labels);
        window.SetLabelMode(labels);
        ScheduleSave();
    }

    private void SetRollupExpand(RollupExpand mode)
    {
        _config = _config with { Settings = _config.Settings with { RollupExpand = mode } };
        foreach (var window in _windows.Values) window.SetRollupExpand(mode);
        Log.Information("roll-up expands on: {Mode}", mode);
        SaveNow();
    }

    private void SetGameModeEnabled(bool enabled)
    {
        _config = _config with { Settings = _config.Settings with { GameMode = enabled } };
        Log.Information("game mode enabled: {Enabled}", enabled);
        SaveNow();
        CheckGameMode(); // turning it off in a game ends idle at once
        RefreshSettings();
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
        // Backstop for a game that goes full screen after the last re-check (M6a review I1).
        var poll = new DispatcherTimer { Interval = GameModePolicy.PollInterval };
        poll.Tick += (_, _) => CheckGameMode();
        poll.Start();
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
        UpdatePeekHotkey();
        _trayIcon?.SetTooltip(TrayTooltip());
        RefreshSettings();
    }

    /// <summary>The game was left: Desktop changes made meanwhile apply in order (or one reconcile if events were lost).</summary>
    private void ApplyDeferredShellWork()
    {
        if (_specialIconsDeferred)
        {
            _specialIconsDeferred = false;
            ScheduleSpecialIconRefresh();
        }
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
            (_config, _rememberedPlacements) = FenceMembership.Apply(_config, change, _rememberedPlacements, Now);
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
        _paused = paused;
        if (paused)
        {
            _quickHidden = false; // resuming shows everything
            _iconsHiddenByUser = false;
        }
        foreach (var window in _windows.Values)
        {
            if (!Current.FencesVisible) window.HideNow();
            else if (!window.IsVisible)
            {
                window.ShowNow();
                FenceWindowChrome.SendToBack(window.Handle);
            }
        }
        EnsureIconState(); // also shows icons left hidden by an earlier failed show: Pause "restores icons" (spec §6, M6a review M1)
        UpdateMouseHook();
        UpdatePeekHotkey();
        RefreshSettings(); // a hotkey that cannot be registered on resume shows in an open Settings (final review I5)
        _trayIcon?.SetTooltip(TrayTooltip());
        Log.Information("paused: {Paused}", paused);
    }

    private string TrayTooltip() =>
        _paused ? "NeoFences — paused" : _gameMode ? "NeoFences — idle while a game runs" : "NeoFences";

    /// <summary>Tray menu (spec §6; user choice 2026-10-03: left-click opens it too).</summary>
    private void ShowTrayMenu(int screenX, int screenY)
    {
        var chosen = TrayMenu.Show(_messages.Handle,
        [
            new TrayMenuItem(TrayNewFence, "New fence", Enabled: !_paused),
            new TrayMenuItem(TrayQuickHide, "Quick-hide", Checked: _quickHidden, Enabled: !_paused),
            new TrayMenuItem(TrayPeek, $"Peek\t{PeekHotkeyDisplay}", Checked: _peeking, Enabled: !_paused),
            TrayMenuItem.Separator,
            new TrayMenuItem(TraySettings, "Settings…"),
            new TrayMenuItem(TrayPause, "Pause NeoFences", Checked: _paused),
            TrayMenuItem.Separator,
            new TrayMenuItem(TrayExit, "Exit NeoFences"),
        ], screenX, screenY);
        switch (chosen)
        {
            case TrayNewFence:
                SetQuickHidden(false); // a new fence must be visible (M6a review I3)
                CreateFence();
                break;
            case TrayQuickHide: SetQuickHidden(!_quickHidden); break;
            case TrayPeek: SetPeek(!_peeking); break;
            case TrayPause: SetPaused(!_paused); break;
            case TraySettings: OpenSettings(); break;
            case TrayExit: ExitRequested?.Invoke(); break;
        }
    }

    private void OnDesktopGesture(DesktopGesture gesture, int screenX, int screenY)
    {
        switch (gesture)
        {
            // A double-click on a visible native icon opens it; only empty desktop toggles quick-hide.
            case DesktopGesture.DoubleClick when Current.IconsHidden
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
        // Icons the user had hidden through Explorer stay theirs: quick-hide neither hides nor later shows them (M8a).
        // Hidden now without the takeover-active marker: the user hid them in Explorer, so they stay the user's. With the marker
        // set, NeoFences hid them (an earlier show failed) and the end of this quick-hide retries the show (M8a review I1).
        if (hidden && !_takeoverActive) _iconsHiddenByUser = DesktopIcons.TryIsHidden() == true && !_watchdog.IsTakeoverActiveMarked;
        var iconsWereHidden = Current.IconsHidden;
        _quickHidden = hidden;
        foreach (var window in _windows.Values)
        {
            if (hidden) window.HideFaded(); // 150 ms fade (spec §6)
            else
            {
                window.ShowFaded();
                FenceWindowChrome.SendToBack(window.Handle);
            }
        }
        if (Current.IconsHidden != iconsWereHidden) SetIconsHidden(Current.IconsHidden); // RunState decides, user-hidden icons included
        if (!hidden) _iconsHiddenByUser = false;
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
        CheckGameMode(); // fresh: a game may have gone full screen since the last check (M6a review I1)
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

    /// <summary>Adds the tray icon, retrying while Explorer is still busy (sign-in autostart, Explorer restart).</summary>
    private void ShowTrayIcon()
    {
        if (_trayIcon is not { } trayIcon || trayIcon.Show()) return;
        var attempts = 0;
        var retry = new DispatcherTimer { Interval = TrayRetryInterval };
        retry.Tick += (_, _) =>
        {
            if (trayIcon.Show() || ++attempts >= TrayRetryAttempts)
            {
                retry.Stop();
                Log.Information("tray icon retry finished after {Attempts} attempt(s)", attempts + 1);
            }
        };
        retry.Start();
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
            else if (_store.LastBackupFailure is { } backupFailure) Log.Warning(backupFailure, "config saved, but the daily backups could not be written or pruned");
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Error(failure, "config save failed");
        }
    }
}
```

- [ ] **Step 2: Build and test**

Run: `dotnet build NeoFences.slnx`, then `dotnet test NeoFences.slnx`
Expected: 0 warnings, 0 errors; `Passed: 283`.

- [ ] **Step 3: Live checks (ask first; print TEST RUNNING / TEST COMPLETE; restore the installed copy; the stick must be plugged in)**

Run with Windows PowerShell (the scripts live in the session scratchpad):
1. `m8b-smoke.ps1 -Exe <branch exe>`: the regressions.
2. `m8d-smoke.ps1 -Exe <branch exe>`: U4, U5, then U1. It ejects the stick through `eject-helper.ps1`, so run it last and tell the user to replug.

Expected: as in `docs/research/m8d-portal-details.md`: the eject succeeds, and the log says "being removed: released it".

- [ ] **Step 4: Commit**

```powershell
git add src/NeoFences.App
git commit -m "feat: released portal folders for safely remove, backed off failing portal watchers and opened every selected item on enter"
```

---

### Task 3: Verification and docs

- [ ] **Step 1: Append section U to `docs/TEST-CHECKLIST.md`**

```markdown

## U — v1.1 Portal details (M8d, ADR-027)
| ID | Steps | Expected |
|---|---|---|
| U1 | A Portal of a folder on a USB stick; tray → Safely Remove Hardware → the stick | "safe to remove"; the Portal shows "not available"; NeoFences keeps running |
| U2 | **[USER]** Plug the stick back in | the Portal shows the folder again within ~7 s |
| U3 | A Portal on the stick; Safely Remove while a file on it is open in another app | Windows names that app; the Portal comes back right away |
| U4 | In a Portal, select a file; copy a new file into the folder | the selection and scroll stay |
| U5 | In a Portal, select two folders, press Enter | both open in Explorer; the Portal stays at its folder |
| U6 | One folder selected, Enter | the Portal browses into it (as before) |
| U7 | **[USER]** A Portal of a network folder; disconnect the network for a minute | it re-tries with growing waits (log), no flood; comes back after |
| U8 | Drag files out of a .zip onto a fence | no pause while hovering or dropping; the files arrive |
| U9 | Rename or recycle from a fence, then delete that fence at once | no crash; Windows' dialogs (if any) still appear |
| U10 | During a game, empty the Recycle Bin; leave the game | the Recycle Bin icon updates after the game |
```

- [ ] **Step 2: Append ADR-027 to `docs/DECISIONS.md`**

```markdown

## ADR-027 — Portal details (M8d): Safely Remove, watcher backoff, Enter on several items
**Date:** 2026-10-03 · **Status:** Accepted · **Refines:** ADR-018 (Portals), ADR-026

**Context.** The fourth and last carry-over batch: Portal details from the M4 review, plus the cheap minors from the
M8c review. The user lent a USB stick (LOCO_DUCK) for the removal test.

**Decision.**
- **Safely Remove / Eject works while a Portal shows a removable drive.** Any open handle vetoes a removal, and a
  Portal holds two (its folder watcher and its parent's). Each Portal on a local drive registers a folder handle for
  removal notices (`DeviceRemovalNotice`, `RegisterDeviceNotification` with `DBT_DEVTYP_HANDLE`, to the message
  window). On `DBT_DEVICEQUERYREMOVE` the Portal closes its watchers and the handle, shows "not available", and the
  existing 7 s retry brings it back when the drive returns (or right away if another program refused the removal).
  Live: with v1.1.0 the eject was vetoed (the volume in use); with M8d it ejected and NeoFences kept running.
- **Portal watcher errors back off** like the desktop watcher (`WatcherBackoff`, M8c): a watcher that fails again soon
  after each re-arm re-lists after 0.5 s, 1 s, 2 s… up to a minute, instead of every 250 ms.
- **Enter with several items selected opens each one** (folders in Explorer); a Portal no longer browses into the last
  of several folders. One item: as before (a Portal browses into a folder).
- **Already covered (no new code):** Portal updates keep selection, scroll and the rename box (the M8b in-place
  `SetItems` diff; checked live on the USB stick); container checks are cached per drag (M8c); a Portal of the Desktop
  folder uses the merged desktop as parent, which acts on the same files except a Public item whose name the user's
  Desktop also has, which M8c routes through its own folder.
- **M8c review minors folded in:** a virtual source (zip contents) is not asked for CF_HDROP at drop time either;
  shell operations whose fence was deleted meanwhile run without an owner window; a recognised title double-click is
  used once; special-icon reloads wait for the end of a game.

**Consequences.**
- The removal notice is registered on every refresh of a local Portal (an open/close of one handle); network Portals
  have none (a network drive is not "removed").
- Still deferred: mixed drops handing desktop items to Windows, a mixed selection with a shadowed Public item, queued
  shell operations at shutdown, `--exit` during the single-instance wait, US key caps.
```

- [ ] **Step 3: Write `docs/research/m8d-portal-details.md`**

```markdown
# M8d — Portal details: results

**Date:** 2026-10-03 · **Machine:** Windows 11 Pro 25H2 (26200), NeoFences 1.1.0 installed · **Stick:** LOCO_DUCK (HP USB
Flash Drive, exFAT, G:), lent by the user for tests; only `G:\NeoFences-test` was used.
**Build:** M8d prototype (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section U · **Decision:** ADR-027.

## Safely Remove (U1)

`CM_Request_Device_Eject` on the stick's USB storage device (what Safely Remove does), with a Portal of
`G:\NeoFences-test` shown:

| Build | Result |
|---|---|
| v1.1.0 (installed) | **Vetoed**: veto type 6 (device), `STORAGE\Volume\{…}` — the volume was in use (the Portal's watchers). |
| M8d prototype | **Ejected**; log "drive of Portal … is being removed: released it"; the Portal shows "This folder is not available right now"; NeoFences kept running. |

The stick stayed ejected afterwards (the user replugs it; U2).

Script lessons:
- Explorer's `FolderItem.InvokeVerb('Eject')` did nothing from a script (the verb's name is "E&ject").
- Ejecting the disk node itself is an illegal request (veto 8): Safely Remove ejects its parent, the USB storage device.

## Other checks on the prototype

| Check | Result |
|---|---|
| U4 a new file arrives while one is selected | **Pass**: items grew (gamma.txt), alpha.txt stayed selected. |
| U5 Enter on two folders | **Pass**: two Explorer windows; the Portal stayed at its folder. |
| M8b regressions (Settings, first title double-click, Peek, quick-hide) | **Pass**. |
| Log | 0 errors. |
```

- [ ] **Step 4: Update the other docs**
  - `ROADMAP.md`:
    - `[x]` the four M4 review lines: USB Safely Remove; incremental updates (via M8b); Portal of the Desktop folder (covered by M8c's same-name rule); Enter / cache / backoff;
    - `[x]` the M8c review minors done here: zip at drop time, owner handle, double-click clear, special icons in game mode;
    - add `- [x] M8d — done <date> (ADR-027, research/m8d-portal-details.md)` and `- [ ] U2, U7 — user checks`;
    - add `- [ ] v1.1.1 release decision (M8c + M8d)`.
  - `FEATURES.md`: the Portal row notes "Safely Remove works while shown (M8d)".
  - `ARCHITECTURE.md`: add an "M8d complete" line (the M8 carry-over batches are done).
  - `SESSION-LOG.md`: add an entry.

- [ ] **Step 5: Refresh the hub** (`node --check`, publish with `url`).

- [ ] **Step 6: Commit**

```powershell
git add docs
git commit -m "docs: added ADR-027, M8d checks and results, and ticked the M8d carry-overs"
```
