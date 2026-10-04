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
