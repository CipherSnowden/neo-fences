using NeoFences.Core.Model;

namespace NeoFences.Core.Layouts;

/// <summary>A rectangle in physical screen pixels (virtual-screen coordinates).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>A monitor in physical pixels, as NeoFences.Shell measures it. <see cref="ToDisplayMonitor"/> gives Core's DIP view.</summary>
/// <param name="DeviceId">Unique per physical monitor and port (the device interface path).</param>
public sealed record MonitorPlacement(
    string DeviceId,
    int PixelWidth,
    int PixelHeight,
    int WorkLeftPx,
    int WorkTopPx,
    int WorkWidthPx,
    int WorkHeightPx,
    int ScalePercent,
    bool IsPrimary)
{
    public double Scale => ScalePercent / 100.0;

    public DisplayMonitor ToDisplayMonitor() =>
        new(DeviceId, PixelWidth, PixelHeight, ScalePercent, WorkWidthPx / Scale, WorkHeightPx / Scale, IsPrimary);
}

/// <summary>Converts fence rects (DIPs relative to a monitor's work area) to and from physical pixels.</summary>
public static class FencePlacement
{
    public static PixelRect ToPixels(FenceRect rect, MonitorPlacement monitor) => new(
        monitor.WorkLeftPx + (int)Math.Round(rect.X * monitor.Scale),
        monitor.WorkTopPx + (int)Math.Round(rect.Y * monitor.Scale),
        (int)Math.Round(rect.W * monitor.Scale),
        (int)Math.Round(rect.H * monitor.Scale));

    public static FenceRect FromPixels(PixelRect pixels, MonitorPlacement monitor) => new(
        monitor.DeviceId,
        (pixels.X - monitor.WorkLeftPx) / monitor.Scale,
        (pixels.Y - monitor.WorkTopPx) / monitor.Scale,
        pixels.Width / monitor.Scale,
        pixels.Height / monitor.Scale);

    /// <summary>The monitor whose work area holds the rect's centre; if none does, the nearest one.</summary>
    public static MonitorPlacement ContainingMonitor(PixelRect pixels, IReadOnlyList<MonitorPlacement> monitors)
    {
        var centreX = pixels.X + pixels.Width / 2.0;
        var centreY = pixels.Y + pixels.Height / 2.0;
        return monitors.MinBy(monitor =>
        {
            var dx = Math.Max(Math.Max(monitor.WorkLeftPx - centreX, 0), centreX - (monitor.WorkLeftPx + monitor.WorkWidthPx));
            var dy = Math.Max(Math.Max(monitor.WorkTopPx - centreY, 0), centreY - (monitor.WorkTopPx + monitor.WorkHeightPx));
            return dx * dx + dy * dy;
        }) ?? throw new ArgumentException("At least one monitor is required.", nameof(monitors));
    }
}
