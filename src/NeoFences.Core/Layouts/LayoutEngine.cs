using NeoFences.Core.Model;

namespace NeoFences.Core.Layouts;

/// <summary>
/// Picks fence rectangles for the current display configuration (spec §5): a known fingerprint is
/// restored exactly; an unknown one is derived from the last layout, scaled per monitor, clamped
/// on-screen and saved under the new fingerprint.
/// </summary>
public static class LayoutEngine
{
    public const double DefaultWidth = 320;
    public const double DefaultHeight = 220;
    public const double MinWidth = 120;
    public const double MinHeight = 60;
    public const double NewFenceMargin = 24;
    public const double CascadeStep = 32;
    /// <summary>Space kept between a new fence and the others (the snapping gap, spec §6 smart placement).</summary>
    public const double PlacementGap = 8;

    public static (NeoFencesConfig Config, Layout Layout) Resolve(NeoFencesConfig config, IReadOnlyList<DisplayMonitor> monitors)
    {
        if (monitors.Count == 0) throw new ArgumentException("At least one monitor is required.", nameof(monitors));
        // A monitor query during an Explorer restart or driver reset can return garbage: never let it reach a saved layout.
        if (monitors.FirstOrDefault(monitor => !new MonitorArea(monitor.WorkWidth, monitor.WorkHeight).IsUsable) is { } unusable)
        {
            throw new ArgumentException($"Monitor {unusable.DeviceId} reported an unusable work area {unusable.WorkWidth}x{unusable.WorkHeight}.", nameof(monitors));
        }

        var fingerprint = DisplayFingerprint.Of(monitors);
        var areas = monitors.ToDictionary(monitor => monitor.DeviceId, monitor => new MonitorArea(monitor.WorkWidth, monitor.WorkHeight));
        var primary = monitors.FirstOrDefault(monitor => monitor.IsPrimary) ?? monitors[0];

        var rects = config.Layouts.TryGetValue(fingerprint, out var known)
            ? new Dictionary<string, FenceRect>(known.Fences)
            : config.LastLayoutFingerprint is { } lastFingerprint && config.Layouts.TryGetValue(lastFingerprint, out var previous)
                ? Adapt(previous: previous, areas: areas, primaryId: primary.DeviceId)
                : new Dictionary<string, FenceRect>();

        // A known setup that misses fences made on another setup since (laptop vs. dock): those keep their place from
        // the last layout, scaled, instead of being placed anew (M8a).
        if (known is not null && config.LastLayoutFingerprint is { } lastSeen && lastSeen != fingerprint
            && config.Layouts.TryGetValue(lastSeen, out var lastLayout))
        {
            foreach (var (fenceId, rect) in Adapt(previous: lastLayout, areas: areas, primaryId: primary.DeviceId))
            {
                rects.TryAdd(fenceId, rect);
            }
        }

        var fenceIds = config.Fences.Select(fence => fence.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var removedFenceId in rects.Keys.Where(fenceId => !fenceIds.Contains(fenceId)).ToList())
        {
            rects.Remove(removedFenceId);
        }

        // Stored rects stay as the user left them; only the returned (display) layout is clamped, so a
        // temporarily smaller work area (taskbar moved, resolution blip) never shifts positions for good.
        var displayRects = new Dictionary<string, FenceRect>();
        var cascadeIndex = 0;
        foreach (var fence in FenceTabs.Boxes(config)) // tabs of a box share the host's placement (M9)
        {
            if (!rects.TryGetValue(fence.Id, out var rect) || !areas.ContainsKey(rect.Monitor))
            {
                var occupied = rects.Values.Where(placed => placed.Monitor == primary.DeviceId).ToList();
                var offset = NewFenceMargin + CascadeStep * cascadeIndex++;
                rect = FreeSpot(primary.DeviceId, areas[primary.DeviceId], occupied)
                       ?? new FenceRect(primary.DeviceId, offset, offset, DefaultWidth, DefaultHeight); // full screen: cascade, clamped below
                rects[fence.Id] = rect;
            }
            displayRects[fence.Id] = Clamp(rect, areas[rect.Monitor]);
        }

        var storedLayout = new Layout { Monitors = areas, Fences = rects };
        var layouts = new Dictionary<string, Layout>(config.Layouts) { [fingerprint] = storedLayout };
        return (config with { Layouts = layouts, LastLayoutFingerprint = fingerprint }, new Layout { Monitors = areas, Fences = displayRects });
    }

    /// <summary>
    /// Smart placement (M5): the first spot, reading top-left to bottom-right, where a default-size fence fits on the
    /// monitor without touching any other (keeping <see cref="PlacementGap"/>). Candidates are the margin and the spots
    /// just right of / below existing fences, so new fences line up with them. Null when the monitor is full.
    /// </summary>
    public static FenceRect? FreeSpot(string monitor, MonitorArea area, IReadOnlyList<FenceRect> occupied)
    {
        var columns = occupied.Select(other => other.X + other.W + PlacementGap).Prepend(NewFenceMargin).Distinct().Order().ToList();
        var rows = occupied.Select(other => other.Y + other.H + PlacementGap).Prepend(NewFenceMargin).Distinct().Order().ToList();
        foreach (var y in rows)
        {
            foreach (var x in columns)
            {
                if (x + DefaultWidth > area.WorkWidth || y + DefaultHeight > area.WorkHeight) continue;
                var touches = occupied.Any(other =>
                    x < other.X + other.W + PlacementGap && x + DefaultWidth + PlacementGap > other.X
                    && y < other.Y + other.H + PlacementGap && y + DefaultHeight + PlacementGap > other.Y);
                if (!touches) return new FenceRect(monitor, x, y, DefaultWidth, DefaultHeight);
            }
        }
        return null;
    }

    /// <summary>Stores a fence's rect (e.g. after the user moved it) under <paramref name="fingerprint"/>.</summary>
    public static NeoFencesConfig WithFenceRect(NeoFencesConfig config, string fingerprint, string fenceId, FenceRect rect)
    {
        var layout = config.Layouts.TryGetValue(fingerprint, out var existing) ? existing : new Layout();
        var fences = new Dictionary<string, FenceRect>(layout.Fences) { [fenceId] = rect };
        var layouts = new Dictionary<string, Layout>(config.Layouts) { [fingerprint] = layout with { Fences = fences } };
        return config with { Layouts = layouts };
    }

    /// <summary>Keeps each fence on its monitor if that monitor still exists (else the primary), scaled to the new work area.</summary>
    private static Dictionary<string, FenceRect> Adapt(Layout previous, IReadOnlyDictionary<string, MonitorArea> areas, string primaryId)
    {
        var adapted = new Dictionary<string, FenceRect>();
        foreach (var (fenceId, rect) in previous.Fences)
        {
            var targetId = areas.ContainsKey(rect.Monitor) ? rect.Monitor : primaryId;
            var target = areas[targetId];
            var source = previous.Monitors.TryGetValue(rect.Monitor, out var savedArea) && savedArea.IsUsable ? savedArea : target;
            var scaleX = target.WorkWidth / source.WorkWidth;
            var scaleY = target.WorkHeight / source.WorkHeight;
            adapted[fenceId] = new FenceRect(targetId, rect.X * scaleX, rect.Y * scaleY, rect.W * scaleX, rect.H * scaleY);
        }
        return adapted;
    }

    /// <summary>Fits the rect inside the work area: never larger than it, never past its edges.</summary>
    public static FenceRect Clamp(FenceRect rect, MonitorArea area)
    {
        var width = Math.Min(Math.Max(rect.W, MinWidth), area.WorkWidth);
        var height = Math.Min(Math.Max(rect.H, MinHeight), area.WorkHeight);
        var x = Math.Clamp(rect.X, 0, area.WorkWidth - width);
        var y = Math.Clamp(rect.Y, 0, area.WorkHeight - height);
        return rect with { X = x, Y = y, W = width, H = height };
    }
}
