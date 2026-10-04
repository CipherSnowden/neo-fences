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
