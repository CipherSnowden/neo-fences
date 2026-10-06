using Windows.Win32;
using Windows.Win32.Foundation;

namespace NeoFences.Shell;

/// <summary>
/// The keyboard for Peek (M38, ADR-060): which window has it, giving it to a fence, giving it back. A fence is
/// WS_EX_NOACTIVATE and does not take the keyboard from a click while another app is in front (ADR-015); a process that
/// just received its hotkey may bring a window forward, which is what Peek does.
/// </summary>
public static class KeyboardFocus
{
    /// <summary>The window that has the keyboard now (0: none).</summary>
    public static nint Foreground() => PInvoke.GetForegroundWindow();

    /// <summary>Whether a window belongs to NeoFences itself (a fence, a menu, Settings).</summary>
    public static unsafe bool IsOwn(nint handle)
    {
        if (handle == 0) return false;
        uint processId = 0;
        PInvoke.GetWindowThreadProcessId((HWND)handle, &processId);
        return processId == (uint)Environment.ProcessId;
    }

    /// <returns>True when the window has the keyboard afterwards.</returns>
    public static bool TryGive(nint handle)
    {
        if (handle == 0 || !PInvoke.IsWindow((HWND)handle)) return false;
        PInvoke.SetForegroundWindow((HWND)handle);
        return PInvoke.GetForegroundWindow() == (HWND)handle;
    }

    /// <summary>
    /// The window that had the keyboard before Peek gets it back while it is still shown; else the desktop does (M38 review
    /// I3), so the keyboard never stays in a fence that went back behind the windows.
    /// </summary>
    public static void GiveBack(nint handle)
    {
        var target = handle != 0 && PInvoke.IsWindow((HWND)handle) && PInvoke.IsWindowVisible((HWND)handle) ? (HWND)handle : PInvoke.GetShellWindow();
        if (!target.IsNull) PInvoke.SetForegroundWindow(target);
    }
}
