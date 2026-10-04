namespace NeoFences.Core.Updates;

/// <param name="LastCheck">When NeoFences last asked for an update (this run), or null.</param>
/// <param name="LastFailed">That check failed (offline, GitHub down): the next one comes sooner.</param>
public sealed record UpdateState(DateTimeOffset? LastCheck, bool LastFailed);

/// <summary>
/// When NeoFences looks for an update (M17, spec 2026-10-04-public-releases-design §4): at start, then once a day, 6 h after
/// a failure; never while a game is in front, never when switched off (no network at all), never in a developer build.
/// </summary>
public static class UpdatePolicy
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(6);

    public static bool ShouldCheck(DateTimeOffset now, UpdateState state, bool enabled, bool installed, bool gameMode) =>
        enabled && installed && !gameMode && NextCheckIn(now, state) == TimeSpan.Zero;

    /// <summary>How long until the schedule wants the next check (zero: now).</summary>
    public static TimeSpan NextCheckIn(DateTimeOffset now, UpdateState state)
    {
        if (state.LastCheck is not { } last) return TimeSpan.Zero;
        var due = last + (state.LastFailed ? RetryAfterFailure : Interval);
        return due <= now ? TimeSpan.Zero : due - now;
    }
}
