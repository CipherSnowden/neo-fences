using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Shell;

public static class SessionState
{
    /// <summary>True while Windows is signing out or shutting down this session (SM_SHUTTINGDOWN).</summary>
    public static bool IsShuttingDown() => PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_SHUTTINGDOWN) != 0;
}
