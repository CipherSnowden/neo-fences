namespace NeoFences.Core.Lifecycle;

/// <summary>
/// How long to wait before re-arming a desktop watcher that stopped (M8c, ADR-026): a watcher that fails again soon
/// after its last re-arm (a folder going offline, a program writing non-stop) waits twice as long each time, up to a
/// minute; one that ran quietly for a while starts over.
/// </summary>
public static class WatcherBackoff
{
    public static readonly TimeSpan First = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan Max = TimeSpan.FromMinutes(1);
    /// <summary>A failure this soon after a re-arm counts as "failed again at once".</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    /// <param name="failureAt">When the failure arrived, not when the recovery runs: the wait itself is not quiet time (M8c review I3).</param>
    public static TimeSpan Next(TimeSpan current, DateTime lastRearm, DateTime failureAt) =>
        failureAt - lastRearm < Window ? TimeSpan.FromTicks(Math.Min(current.Ticks * 2, Max.Ticks)) : First;
}
