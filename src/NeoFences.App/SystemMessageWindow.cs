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
    private const int SpiSetDeskWallpaper = 0x0014; // M14: the wallpaper accent reads the new wallpaper

    private readonly HwndSource _source;

    public event Action? ExplorerRestarted;
    public event Action? DisplayChanged;
    public event Action? ThemeChanged;
    public event Action? WallpaperChanged;
    /// <summary>A RegisterHotKey hotkey of this window was pressed (its id).</summary>
    public event Action<int>? HotkeyPressed;
    /// <summary>The tray icon was clicked: show the tray menu at this screen point (px).</summary>
    public event Action<int, int>? TrayMenuRequested;
    /// <summary>The user signed back in from the lock screen (WM_WTSSESSION_CHANGE).</summary>
    public event Action? SessionUnlocked;
    /// <summary>The Recycle Bin (or another icon image) changed: refresh the special icons (M8c).</summary>
    public event Action? SpecialIconsChanged;
    /// <summary>
    /// Windows asks to remove the drive holding this registered handle (Safely Remove, Eject), or it was pulled without
    /// asking: release it now (M8d), so the drive coming back re-arms fresh watchers (M18 live check AD28).
    /// </summary>
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
            case DeviceRemovalNotice.WmDeviceChange when wParam is DeviceRemovalNotice.QueryRemove or DeviceRemovalNotice.RemoveComplete
                                                         && DeviceRemovalNotice.HandleOf(lParam) is var removed and not 0:
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
            case WmSettingChange when wParam == SpiSetDeskWallpaper:
                WallpaperChanged?.Invoke();
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
