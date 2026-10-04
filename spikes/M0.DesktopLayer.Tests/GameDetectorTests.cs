using NeoFences.Spikes.M0;

namespace M0.DesktopLayer.Tests;

public class GameDetectorTests
{
    private static readonly ScreenRect Monitor = new(0, 0, 3840, 2160);

    [Fact]
    public void ExactMonitorRect_Covers() =>
        Assert.True(GameDetector.CoversMonitor(window: Monitor, monitor: Monitor));

    [Fact]
    public void BorderlessOverhangingByPixels_Covers() =>
        Assert.True(GameDetector.CoversMonitor(window: new ScreenRect(-8, -8, 3848, 2168), monitor: Monitor));

    [Fact]
    public void MaximizedWindowAboveTaskbar_DoesNotCover() =>
        Assert.False(GameDetector.CoversMonitor(window: new ScreenRect(-8, -8, 3848, 2088), monitor: Monitor));

    [Fact]
    public void WindowOnSecondMonitor_DoesNotCoverFirst() =>
        Assert.False(GameDetector.CoversMonitor(window: new ScreenRect(3840, 0, 5760, 1080), monitor: Monitor));
}
