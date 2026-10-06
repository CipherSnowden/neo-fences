namespace NeoFences.Core.Layouts;

/// <summary>A fence (a box) on screen for the keyboard order (M38): its rect in screen pixels and its monitor.</summary>
/// <param name="MonitorLeft">The left edge of its monitor: screens other than the main one are taken from left to right.</param>
public sealed record FenceSpot(string Id, double X, double Y, double W, double H, bool OnPrimary, double MonitorLeft);

/// <summary>
/// Which fence the keyboard goes to (M38, spec §1, ADR-060): Peek puts it in the fence under the mouse, else the one used
/// last, else the first in reading order; Tab / Shift+Tab walk the reading order and wrap. Pure.
/// </summary>
public static class KeyboardOrder
{
    /// <summary>
    /// Left to right, top to bottom, the main screen first and the others from left to right. Fences whose tops are within
    /// half the shortest height of a row's first fence count as that row (fences are rarely exactly level).
    /// </summary>
    public static IReadOnlyList<string> ReadingOrder(IReadOnlyList<FenceSpot> spots)
    {
        var ordered = new List<string>();
        foreach (var screen in spots.GroupBy(spot => (spot.OnPrimary, spot.MonitorLeft))
                     .OrderBy(group => group.Key.OnPrimary ? 0 : 1).ThenBy(group => group.Key.MonitorLeft))
        {
            var rows = new List<List<FenceSpot>>();
            foreach (var spot in screen.OrderBy(spot => spot.Y).ThenBy(spot => spot.X))
            {
                var row = rows.Count > 0 ? rows[^1] : null;
                if (row is not null && spot.Y < row[0].Y + row.Min(member => member.H) / 2) row.Add(spot);
                else rows.Add([spot]);
            }
            foreach (var row in rows) ordered.AddRange(row.OrderBy(spot => spot.X).Select(spot => spot.Id));
        }
        return ordered;
    }

    /// <summary>The fence Peek gives the keyboard to: under the mouse, else the last used, else the first; null without fences.</summary>
    public static string? Start(IReadOnlyList<string> order, string? underMouse, string? lastUsed) =>
        underMouse is not null && order.Contains(underMouse) ? underMouse
        : lastUsed is not null && order.Contains(lastUsed) ? lastUsed
        : order.Count > 0 ? order[0] : null;

    /// <summary>The next (<paramref name="step"/> 1) or previous (−1) fence, wrapping; a fence that is gone gives the first.</summary>
    public static string? Next(IReadOnlyList<string> order, string current, int step)
    {
        if (order.Count == 0) return null;
        var index = -1;
        for (var at = 0; at < order.Count; at++)
        {
            if (order[at] == current) index = at;
        }
        if (index < 0) return order[0];
        return order[((index + step) % order.Count + order.Count) % order.Count];
    }
}
