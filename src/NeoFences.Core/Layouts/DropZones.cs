namespace NeoFences.Core.Layouts;

/// <summary>Where a drop lands in a fence (M3b): the insert position under the pointer.</summary>
public static class DropZones
{
    /// <summary>
    /// The index to insert before when dropping at a point, cells in reading order (left to right, rows top to bottom).
    /// A row reaches down to its tallest cell, so the space under a short label still belongs to its row (M3b review).
    /// </summary>
    public static int InsertIndex(IReadOnlyList<(double Left, double Top, double Width, double Height)> cells, double pointX, double pointY)
    {
        var rowBottoms = new Dictionary<long, double>();
        foreach (var (_, top, _, height) in cells)
        {
            var row = (long)Math.Round(top);
            rowBottoms[row] = Math.Max(rowBottoms.GetValueOrDefault(row, double.MinValue), top + height);
        }
        for (var index = 0; index < cells.Count; index++)
        {
            var (left, top, width, _) = cells[index];
            var sameRow = pointY >= top && pointY < rowBottoms[(long)Math.Round(top)];
            if (pointY < top || (sameRow && pointX < left + width / 2)) return index;
        }
        return cells.Count;
    }
}
