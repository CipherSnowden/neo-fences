using NeoFences.Spikes.M0;

namespace M0.DesktopLayer.Tests;

public class RestartThrottleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NoRecentRestarts_Restarts() =>
        Assert.True(RestartThrottle.ShouldRestart(recentRestarts: [], now: Now));

    [Fact]
    public void TwoRestartsInWindow_StillRestarts() =>
        Assert.True(RestartThrottle.ShouldRestart(recentRestarts: [Now.AddMinutes(-1), Now.AddMinutes(-5)], now: Now));

    [Fact]
    public void ThreeRestartsInWindow_StaysDown() =>
        Assert.False(RestartThrottle.ShouldRestart(recentRestarts: [Now.AddMinutes(-1), Now.AddMinutes(-2), Now.AddMinutes(-9)], now: Now));

    [Fact]
    public void OldRestartsOutsideWindow_DoNotCount() =>
        Assert.True(RestartThrottle.ShouldRestart(recentRestarts: [Now.AddMinutes(-1), Now.AddMinutes(-11), Now.AddHours(-2)], now: Now));
}
