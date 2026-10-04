using NeoFences.Core.Lifecycle;

namespace NeoFences.Core.Tests.Lifecycle;

public class WatchdogPlanTests
{
    [Fact]
    public void CleanExit_IconsShown_NothingToDo() =>
        Assert.Equal(new WatchdogPlan(RestoreIcons: false, Restart: WatchdogRestart.Never),
            WatchdogPlan.For(cleanShutdown: true, sessionEnding: false, takeoverActive: false));

    [Fact]
    public void CleanExit_ButIconsStillMarkedHidden_RestoresAnyway()
    {
        // M2a review I1: a clean exit whose own restore failed (Explorer restarting) must not leave icons hidden.
        Assert.Equal(new WatchdogPlan(RestoreIcons: true, Restart: WatchdogRestart.Never),
            WatchdogPlan.For(cleanShutdown: true, sessionEnding: false, takeoverActive: true));
    }

    [Fact]
    public void Crash_WithIconsHidden_RestoresAndRestarts() =>
        Assert.Equal(new WatchdogPlan(RestoreIcons: true, Restart: WatchdogRestart.Throttled),
            WatchdogPlan.For(cleanShutdown: false, sessionEnding: false, takeoverActive: true));

    [Fact]
    public void Crash_WithoutTakeover_RestartsWithoutTouchingIcons() =>
        Assert.Equal(new WatchdogPlan(RestoreIcons: false, Restart: WatchdogRestart.Throttled),
            WatchdogPlan.For(cleanShutdown: false, sessionEnding: false, takeoverActive: false));

    [Fact]
    public void SessionEnding_RestartsOnlyIfTheSessionContinues()
    {
        // ADR-013: WPF exits NeoFences on WM_QUERYENDSESSION; if the user then cancels the shutdown the watchdog brings it back.
        Assert.Equal(new WatchdogPlan(RestoreIcons: true, Restart: WatchdogRestart.IfSessionContinues),
            WatchdogPlan.For(cleanShutdown: false, sessionEnding: true, takeoverActive: true));
    }
}

public class RestartThrottleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NoRecentRestarts_Restarts() =>
        Assert.True(RestartThrottle.ShouldRestart(recentRestarts: [], now: Now));

    [Fact]
    public void ThreeRestartsInTenMinutes_StaysDown() =>
        Assert.False(RestartThrottle.ShouldRestart(recentRestarts: [Now.AddMinutes(-1), Now.AddMinutes(-2), Now.AddMinutes(-9)], now: Now));

    [Fact]
    public void OldRestartsOutsideWindow_DoNotCount() =>
        Assert.True(RestartThrottle.ShouldRestart(recentRestarts: [Now.AddMinutes(-1), Now.AddMinutes(-11), Now.AddHours(-2)], now: Now));
}
