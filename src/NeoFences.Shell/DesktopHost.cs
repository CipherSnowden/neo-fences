using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Shell;

/// <summary>
/// Keeps fence windows at desktop level by making Progman their owner (ADR-011): they stay just above the
/// desktop through Win+D and Win+M while apps still cover them. Ownership must be re-applied after Explorer
/// restarts (a new Progman window); <see cref="IsAttached"/> verifies it.
/// </summary>
public static class DesktopHost
{
    /// <summary>Registered message Explorer broadcasts when the taskbar (and desktop) is recreated.</summary>
    public static uint TaskbarCreatedMessage { get; } = PInvoke.RegisterWindowMessage("TaskbarCreated");

    public static unsafe nint FindProgman() => (nint)PInvoke.FindWindow("Progman", null).Value;

    /// <returns>False when Progman does not exist yet (Explorer still starting); retry later.</returns>
    public static unsafe bool AttachToDesktop(nint fenceHandle)
    {
        var progman = FindProgman();
        if (progman == 0) return false;
        // ponytail: GWLP_HWNDPARENT changes the owner after creation, which is undocumented (ADR-011); checked via IsAttached.
        PInvoke.SetWindowLongPtr((HWND)fenceHandle, WINDOW_LONG_PTR_INDEX.GWLP_HWNDPARENT, progman);
        return IsAttached(fenceHandle);
    }

    /// <summary>
    /// rc.2 (feedback #6): the fence leaves Progman while it has Peek's keyboard. Activated while owned by Progman it would
    /// become Progman's last active window, and every later Win+D would hand the desktop's activation to it — ending "show
    /// desktop" at once, so the second Win+D minimized instead of restoring (until NeoFences restarted).
    /// </summary>
    public static void DetachFromDesktop(nint fenceHandle) => PInvoke.SetWindowLongPtr((HWND)fenceHandle, WINDOW_LONG_PTR_INDEX.GWLP_HWNDPARENT, 0);

    public static unsafe bool IsAttached(nint fenceHandle)
    {
        var progman = FindProgman();
        return progman != 0 && (nint)PInvoke.GetWindow((HWND)fenceHandle, GET_WINDOW_CMD.GW_OWNER).Value == progman;
    }
}
