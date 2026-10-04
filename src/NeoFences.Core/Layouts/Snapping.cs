namespace NeoFences.Core.Layouts;

/// <summary>The edges a drag changes: all four when moving, the grabbed ones when resizing.</summary>
[Flags]
public enum SnapEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    Move = Left | Top | Right | Bottom,
}

/// <summary>
/// Smart placement while dragging (spec §6): edges near the work-area edge or another fence snap to a fixed gap
/// beside it, or line up with it. All values in physical pixels of one monitor.
/// </summary>
public static class Snapping
{
    /// <param name="minWidthPx">A resize snap that would make the fence narrower than this is skipped (M8b).</param>
    /// <param name="minHeightPx">Likewise for the height.</param>
    public static PixelRect Snap(PixelRect rect, SnapEdges edges, PixelRect workArea, IReadOnlyList<PixelRect> others, int gapPx, int thresholdPx,
        int minWidthPx = 1, int minHeightPx = 1)
    {
        var left = rect.X;
        var top = rect.Y;
        var right = rect.X + rect.Width;
        var bottom = rect.Y + rect.Height;
        var reach = gapPx + thresholdPx;

        // Only fences level with this one (overlapping, or within reach, on the other axis) pull its edges.
        var besideIt = others.Where(other => other.Y < bottom + reach && other.Y + other.Height > top - reach).ToList();
        var aboveOrBelow = others.Where(other => other.X < right + reach && other.X + other.Width > left - reach).ToList();

        int? Best(int edge, IEnumerable<int> candidates)
        {
            var closest = candidates.OrderBy(candidate => Math.Abs(candidate - edge)).Cast<int?>().FirstOrDefault();
            return closest is { } value && Math.Abs(value - edge) <= thresholdPx ? value : null;
        }

        var leftTarget = Best(left, [workArea.X + gapPx, .. besideIt.Select(other => other.X + other.Width + gapPx), .. besideIt.Select(other => other.X)]);
        var rightTarget = Best(right, [workArea.X + workArea.Width - gapPx, .. besideIt.Select(other => other.X - gapPx), .. besideIt.Select(other => other.X + other.Width)]);
        var topTarget = Best(top, [workArea.Y + gapPx, .. aboveOrBelow.Select(other => other.Y + other.Height + gapPx), .. aboveOrBelow.Select(other => other.Y)]);
        var bottomTarget = Best(bottom, [workArea.Y + workArea.Height - gapPx, .. aboveOrBelow.Select(other => other.Y - gapPx), .. aboveOrBelow.Select(other => other.Y + other.Height)]);

        if (edges == SnapEdges.Move)
        {
            var dx = Closer(leftTarget - left, rightTarget - right);
            var dy = Closer(topTarget - top, bottomTarget - bottom);
            return rect with { X = rect.X + dx, Y = rect.Y + dy };
        }

        if (edges.HasFlag(SnapEdges.Left) && leftTarget is { } newLeft && right - newLeft >= minWidthPx) left = newLeft;
        if (edges.HasFlag(SnapEdges.Right) && rightTarget is { } newRight && newRight - left >= minWidthPx) right = newRight;
        if (edges.HasFlag(SnapEdges.Top) && topTarget is { } newTop && bottom - newTop >= minHeightPx) top = newTop;
        if (edges.HasFlag(SnapEdges.Bottom) && bottomTarget is { } newBottom && newBottom - top >= minHeightPx) bottom = newBottom;
        return new PixelRect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Tracks where a drag would put the window without snapping. Windows proposes each WM_MOVING / WM_SIZING rect
    /// as "the window now + this mouse step"; snapping that proposal directly snaps every small step back and the
    /// fence sticks. Instead add the step (proposal minus current, per edge) to the unsnapped rect and snap that.
    /// </summary>
    public static PixelRect Unsnapped(PixelRect unsnapped, PixelRect current, PixelRect proposal)
    {
        var left = unsnapped.X + (proposal.X - current.X);
        var top = unsnapped.Y + (proposal.Y - current.Y);
        var right = unsnapped.X + unsnapped.Width + (proposal.X + proposal.Width - (current.X + current.Width));
        var bottom = unsnapped.Y + unsnapped.Height + (proposal.Y + proposal.Height - (current.Y + current.Height));
        return new PixelRect(left, top, right - left, bottom - top);
    }

    /// <summary>The smaller of two optional shifts (0 when neither edge snaps).</summary>
    private static int Closer(int? first, int? second) => (first, second) switch
    {
        ({ } a, { } b) => Math.Abs(a) <= Math.Abs(b) ? a : b,
        ({ } a, null) => a,
        (null, { } b) => b,
        _ => 0,
    };
}

/// <summary>
/// One drag (WM_ENTERSIZEMOVE … WM_EXITSIZEMOVE). Windows proposes each WM_MOVING / WM_SIZING rect from the rect we
/// wrote back last time, not from the window's real rect: with "Show window contents while dragging" off the window
/// does not move until release (M2c review). So the base for each step is the last written rect.
/// </summary>
public sealed class DragTracker(PixelRect start)
{
    private PixelRect _unsnapped = start;
    private PixelRect _lastWritten = start;

    /// <summary>Adds this message's step to the unsnapped rect, snaps it, and returns the rect to write back.</summary>
    public PixelRect Step(PixelRect proposal, Func<PixelRect, PixelRect> snap)
    {
        _unsnapped = Snapping.Unsnapped(_unsnapped, current: _lastWritten, proposal: proposal);
        _lastWritten = snap(_unsnapped);
        return _lastWritten;
    }
}
