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
