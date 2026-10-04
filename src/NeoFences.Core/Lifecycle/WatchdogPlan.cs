namespace NeoFences.Core.Lifecycle;

public enum WatchdogRestart
{
    /// <summary>The user exited NeoFences on purpose.</summary>
    Never,
    /// <summary>Windows asked to end the session; restart only if that was cancelled (ADR-013).</summary>
    IfSessionContinues,
    /// <summary>Unclean exit (crash, kill): restart, at most 3 times in 10 minutes (<see cref="RestartThrottle"/>).</summary>
    Throttled,
}

/// <summary>What the watchdog does once the main process has exited (ADR-005, ADR-013).</summary>
public sealed record WatchdogPlan(bool RestoreIcons, WatchdogRestart Restart)
{
    /// <param name="cleanShutdown">Main wrote <c>clean-shutdown-&lt;pid&gt;</c> (orderly exit).</param>
    /// <param name="sessionEnding">Main wrote <c>session-ending-&lt;pid&gt;</c> (Windows asked to end the session).</param>
    /// <param name="iconsHidden"><c>icons-hidden</c> exists: NeoFences may have left the icons hidden.</param>
    public static WatchdogPlan For(bool cleanShutdown, bool sessionEnding, bool iconsHidden) => new(
        // Restoring is idempotent, so it never depends on how main exited: a marker alone must not leave icons hidden.
        RestoreIcons: iconsHidden,
        Restart: cleanShutdown ? WatchdogRestart.Never
            : sessionEnding ? WatchdogRestart.IfSessionContinues
            : WatchdogRestart.Throttled);
}
