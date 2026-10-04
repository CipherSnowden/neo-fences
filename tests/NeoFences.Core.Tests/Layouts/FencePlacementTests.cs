using NeoFences.Core.Layouts;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Layouts;

public class FencePlacementTests
{
    // 4K at 150%, taskbar at the bottom (work area 3840x2088 px), primary.
    private static readonly MonitorPlacement Dell = new("DELL", 3840, 2160, WorkLeftPx: 0, WorkTopPx: 0, WorkWidthPx: 3840, WorkHeightPx: 2088, ScalePercent: 150, IsPrimary: true);
    // 1080p at 100%, to the right of the 4K, taskbar on its left (work area starts 48 px in).
    private static readonly MonitorPlacement Lg = new("LG", 1920, 1080, WorkLeftPx: 3888, WorkTopPx: 0, WorkWidthPx: 1872, WorkHeightPx: 1080, ScalePercent: 100, IsPrimary: false);

    [Fact]
    public void ToDisplayMonitor_ConvertsWorkAreaToDips()
    {
        Assert.Equal(new DisplayMonitor("DELL", 3840, 2160, 150, 2560, 1392, IsPrimary: true), Dell.ToDisplayMonitor());
    }

    [Fact]
    public void ToPixels_OffsetsByWorkAreaAndScales()
    {
        Assert.Equal(new PixelRect(60, 90, 480, 330), FencePlacement.ToPixels(new FenceRect("DELL", 40, 60, 320, 220), Dell));
        Assert.Equal(new PixelRect(3928, 24, 320, 220), FencePlacement.ToPixels(new FenceRect("LG", 40, 24, 320, 220), Lg));
    }

    [Fact]
    public void FromPixels_IsTheInverse()
    {
        var rect = new FenceRect("DELL", 40, 60, 320, 220);

        Assert.Equal(rect, FencePlacement.FromPixels(FencePlacement.ToPixels(rect, Dell), Dell));
    }

    [Fact]
    public void ContainingMonitor_UsesTheRectCentre()
    {
        Assert.Equal("LG", FencePlacement.ContainingMonitor(new PixelRect(3800, 100, 400, 200), [Dell, Lg]).DeviceId);
        Assert.Equal("DELL", FencePlacement.ContainingMonitor(new PixelRect(3500, 100, 400, 200), [Dell, Lg]).DeviceId);
    }

    [Fact]
    public void ContainingMonitor_CentreOffEveryScreen_PicksNearest()
    {
        Assert.Equal("LG", FencePlacement.ContainingMonitor(new PixelRect(9000, 100, 200, 100), [Dell, Lg]).DeviceId);
    }
}
