using NeoFences.Core.Lifecycle;

namespace NeoFences.Core.Tests.Lifecycle;

/// <summary>A desktop watcher that fails again soon after each re-arm waits longer each time, up to a minute (M8c review I3).</summary>
public class WatcherBackoffTests
{
    private static readonly DateTime Start = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FailingRightAfterEveryRearm_GrowsToAMinute_AndStaysThere()
    {
        // The failure comes 1 s after each re-arm, which itself happened after the previous delay: the waited time must not
        // count as "quiet" (the review's 0.5 → 16 s → 0.5 s cycle).
        var delay = WatcherBackoff.First;
        var rearmedAt = Start;
        var delays = new List<double>();
        for (var failure = 0; failure < 10; failure++)
        {
            var failedAt = rearmedAt + TimeSpan.FromSeconds(1);
            delay = WatcherBackoff.Next(delay, lastRearm: rearmedAt, failureAt: failedAt);
            delays.Add(delay.TotalSeconds);
            rearmedAt = failedAt + delay;
        }
        Assert.Equal([1, 2, 4, 8, 16, 32, 60, 60, 60, 60], delays);
    }

    [Fact]
    public void AFailureLongAfterTheLastRearm_StartsOver()
    {
        var delay = WatcherBackoff.Next(TimeSpan.FromSeconds(32), lastRearm: Start, failureAt: Start + TimeSpan.FromMinutes(5));
        Assert.Equal(WatcherBackoff.First, delay);
    }
}
