namespace NeoFences.Shell;

/// <summary>
/// "Click wallpaper to reveal desktop" (M41a, ADR-066): the shell's own "minimize all" moves the windows aside, so the
/// fences and the wallpaper come into view, and its "undo minimize all" brings them back. Not ToggleDesktop: right after a
/// click on the wallpaper the desktop has the focus, and ToggleDesktop then takes the desktop as already shown and does
/// nothing (VM probe, 2026-10-11).
/// </summary>
public static class DesktopReveal
{
    /// <summary>
    /// Shell.Application's MinimizeAll / UndoMinimizeALL, late-bound: CsWin32 has no projection of the shell's automation
    /// object (its IDispatch interfaces live in a type library, not in the Win32 metadata). Call from the UI thread (an STA).
    /// </summary>
    public static bool Set(bool revealed, Action<string> log)
    {
        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!);
            shell!.GetType().InvokeMember(revealed ? "MinimizeAll" : "UndoMinimizeALL", System.Reflection.BindingFlags.InvokeMethod,
                binder: null, target: shell, args: null);
            return true;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            log($"reveal desktop: the shell refused ({failure.Message})");
            return false;
        }
        finally
        {
            if (shell is not null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
        }
    }

    /// <summary>
    /// Final review I5: does an app window show now (visible, not minimized, not cloaked, a titled top-level window that is
    /// not a tool window and not NeoFences' or the shell's)? The reveal decides from this at each click.
    /// </summary>
    public static unsafe bool AppWindowsShown()
    {
        var shown = false;
        var ownProcess = (uint)Environment.ProcessId;
        Windows.Win32.PInvoke.EnumWindows((window, _) =>
        {
            if (!Windows.Win32.PInvoke.IsWindowVisible(window) || Windows.Win32.PInvoke.IsIconic(window) || Windows.Win32.PInvoke.GetWindowTextLength(window) == 0) return true;
            if (Windows.Win32.PInvoke.GetWindow(window, Windows.Win32.UI.WindowsAndMessaging.GET_WINDOW_CMD.GW_OWNER) != Windows.Win32.Foundation.HWND.Null) return true;
            var exStyle = (Windows.Win32.UI.WindowsAndMessaging.WINDOW_EX_STYLE)Windows.Win32.PInvoke.GetWindowLongPtr(window, Windows.Win32.UI.WindowsAndMessaging.WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
            if (exStyle.HasFlag(Windows.Win32.UI.WindowsAndMessaging.WINDOW_EX_STYLE.WS_EX_TOOLWINDOW)) return true;
            uint processId;
            Windows.Win32.PInvoke.GetWindowThreadProcessId(window, &processId);
            if (processId == ownProcess) return true;
            if (DesktopWindows.ClassOf(window) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            int cloaked;
            if (Windows.Win32.PInvoke.DwmGetWindowAttribute(window, Windows.Win32.Graphics.Dwm.DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(int)).Succeeded && cloaked != 0) return true;
            shown = true;
            return false; // one is enough
        }, 0);
        return shown;
    }

    /// <summary>Windows' double-click time (ms): a click becomes a reveal only once a second click can no longer follow.</summary>
    public static uint DoubleClickTime => Windows.Win32.PInvoke.GetDoubleClickTime();
}
