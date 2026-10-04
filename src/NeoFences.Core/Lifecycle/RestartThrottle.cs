namespace NeoFences.Core.Lifecycle;

/// <summary>Watchdog restart policy: at most <c>maxRestarts</c> within <c>window</c> (ADR-005: 3 per 10 min).</summary>
public static class RestartThrottle
{
    public static bool ShouldRestart(IReadOnlyList<DateTimeOffset> recentRestarts, DateTimeOffset now, int maxRestarts = 3, TimeSpan? window = null)
    {
        var span = window ?? TimeSpan.FromMinutes(10);
        return recentRestarts.Count(restartTime => now - restartTime < span) < maxRestarts;
    }
}
