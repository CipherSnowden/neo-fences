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

    /// <summary>
    /// A game that goes full screen long after it took the foreground (a launcher, a windowed loading screen, Alt+Enter)
    /// raises no further event, so the state is also polled this often. One cheap shell query; it supersedes the spec's
    /// "no polling" (ADR-021, M6a review I1).
    /// </summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

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
