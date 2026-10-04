namespace NeoFences.Core.Layouts;

/// <summary>
/// Where a drop over a folder-like item means "into it" (M3b review I2): only the middle half of the cell's width and its
/// upper three quarters (icon and first label line). The edges and the rest of the label reorder, so a drop meant to
/// land between two folders never moves a file into one of them.
/// </summary>
public static class DropZones
{
    public static bool IsInto(double cellLeft, double cellTop, double cellWidth, double cellHeight, double pointX, double pointY) =>
        pointX >= cellLeft + cellWidth / 4 && pointX <= cellLeft + cellWidth * 3 / 4
        && pointY >= cellTop && pointY <= cellTop + cellHeight * 3 / 4;

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
