namespace NeoFences.Core.Lifecycle;

/// <summary>How the watchdog starts NeoFences again after an unclean exit (M33, ADR-053).</summary>
public enum CrashRestart { Normal, SafeMode, StopAndAsk }

/// <summary>
/// The watchdog's decision after a crash (M33, spec 2026-10-06-safety-net-design §1): below the limit a normal restart; at
/// the limit (3 restarts in 10 minutes, <see cref="RestartThrottle"/>) safe mode; safe mode crashing again within the
/// window stops and asks the user; safe mode that ran past the window starts in safe mode again.
/// </summary>
public static class CrashRecovery
{
    public static CrashRestart Decide(IReadOnlyList<DateTimeOffset> recentRestarts, DateTimeOffset now, bool lastStartWasSafe)
    {
        var belowLimit = RestartThrottle.ShouldRestart(recentRestarts, now);
        if (lastStartWasSafe) return belowLimit ? CrashRestart.SafeMode : CrashRestart.StopAndAsk;
        return belowLimit ? CrashRestart.Normal : CrashRestart.SafeMode;
    }
}
