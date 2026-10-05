using NeoFences.Core.Library;

namespace NeoFences.Core.Items;

/// <summary>An element's size in the fence's cells (M24): 1–4 columns × 1–4 rows.</summary>
public sealed record GridSpan(int Columns, int Rows)
{
    public const int Max = 4;

    public static GridSpan One { get; } = new(1, 1);

    /// <summary>Within 1–4 each way, and no wider than the fence.</summary>
    public GridSpan Clamp(int columns) => new(Math.Clamp(Columns, 1, Math.Clamp(columns, 1, Max)), Math.Clamp(Rows, 1, Max));
}

/// <summary>A cell of the fence's grid (M24): column and row from the top-left, both ≥ 0.</summary>
public sealed record GridCell(int Column, int Row);

/// <summary>How a fence's elements sit (M24): packed in order (today's behaviour), or at fixed positions.</summary>
public enum FenceLayout { Flow, Free }

/// <summary>One element for <see cref="FenceGrid.Arrange"/>: its span, and its stored cell (Free fences only).</summary>
public sealed record GridElement(GridSpan Span, GridCell? Stored);

/// <param name="Cells">Each element's top-left cell, in input order.</param>
/// <param name="Spans">Each element's span as shown (clamped to the fence's width), in input order.</param>
/// <param name="Rows">Rows used.</param>
public sealed record GridArrangement(IReadOnlyList<GridCell> Cells, IReadOnlyList<GridSpan> Spans, int Rows);

/// <summary>
/// The fence grid (M24, spec 2026-10-05-element-sizes-design §1): where every element sits for a column count. Flow packs
/// in order and fills gaps; Free keeps stored cells and puts the rest in free spots. Pure; one pass over a map of cells.
/// </summary>
public static class FenceGrid
{
    /// <summary>The farthest row or column a stored cell may name (a hand-edited 10,000,000 would grow the map every layout).</summary>
    public const int MaxCell = 1000;

    /// <summary>An item's span: its own size, else a widget's or a panel's (M25, M26), else 1×2 for a game shown as a cover (a 2:3 poster fits), else 1×1.</summary>
    public static GridSpan SpanOf(VirtualItem item) =>
        item.Size ?? (Widgets.Of(item.Target) is { } widget ? Widgets.DefaultSpan(widget)
            : FolderPanels.IsPanel(item) ? FolderPanels.DefaultSpan
            : GameItems.ShowsCover(item) ? new GridSpan(1, 2) : GridSpan.One);

    public static GridArrangement Arrange(IReadOnlyList<GridElement> elements, int columns, FenceLayout layout)
    {
        columns = Math.Max(1, columns);
        var map = new Occupancy(columns);
        var spans = elements.Select(element => element.Span.Clamp(columns)).ToList();
        var cells = new GridCell?[elements.Count];
        if (layout == FenceLayout.Free)
        {
            for (var index = 0; index < elements.Count; index++)
            {
                if (elements[index].Stored is not { } stored || stored.Column < 0 || stored.Row < 0) continue;
                if (stored.Column + spans[index].Columns > columns || !map.IsFree(stored, spans[index])) continue;
                map.Take(stored, spans[index]);
                cells[index] = stored;
            }
        }
        for (var index = 0; index < elements.Count; index++)
        {
            if (cells[index] is not null) continue;
            var cell = map.FirstFree(spans[index]);
            map.Take(cell, spans[index]);
            cells[index] = cell;
        }
        return new GridArrangement([.. cells.Select(cell => cell!)], spans, map.Rows);
    }

    /// <summary>The cell under a point of the grid's area (negative coordinates: the first row or column).</summary>
    public static GridCell CellAt(double x, double y, double cellWidth, double cellHeight) =>
        new(Math.Max(0, (int)Math.Floor(x / cellWidth)), Math.Max(0, (int)Math.Floor(y / cellHeight)));

    /// <summary>
    /// The free spot for <paramref name="span"/> closest to <paramref name="target"/> (top-left corners; ties: the upper,
    /// then the left one), rows added below as needed.
    /// </summary>
    public static GridCell NearestFree(IReadOnlyList<(GridCell Cell, GridSpan Span)> placed, GridSpan span, GridCell target, int columns)
    {
        columns = Math.Max(1, columns);
        span = span.Clamp(columns);
        var map = new Occupancy(columns);
        foreach (var (cell, size) in placed) map.Take(cell, size.Clamp(columns));
        var lastRow = Math.Max(map.Rows, target.Row) + span.Rows;
        GridCell? best = null;
        var bestDistance = long.MaxValue;
        for (var row = 0; row <= lastRow; row++)
        {
            for (var column = 0; column + span.Columns <= columns; column++)
            {
                var candidate = new GridCell(column, row);
                if (!map.IsFree(candidate, span)) continue;
                long distance = (long)(column - target.Column) * (column - target.Column) + (long)(row - target.Row) * (row - target.Row);
                if (distance < bestDistance) (best, bestDistance) = (candidate, distance);
            }
        }
        return best ?? new GridCell(0, lastRow + 1);
    }

    /// <summary>
    /// Free fences (spec §2): dropped elements land with their top-left on <paramref name="dropCell"/>, the others of the
    /// drag keeping their offsets from the first one's <c>From</c> cell (unknown: the drop cell); a taken spot → the nearest
    /// free one. Returns a cell per dropped element, in order.
    /// </summary>
    /// <param name="anchorIndex">The dropped element that was under the pointer (M28): it lands on the drop cell, the others
    /// keep their offsets from it.</param>
    public static IReadOnlyList<GridCell> PlaceDropped(IReadOnlyList<(GridCell Cell, GridSpan Span)> others,
        IReadOnlyList<(GridSpan Span, GridCell? From)> dropped, GridCell dropCell, int columns, int anchorIndex = 0)
    {
        var placed = others.ToList();
        var anchor = dropped.ElementAtOrDefault(Math.Clamp(anchorIndex, 0, Math.Max(0, dropped.Count - 1))).From;
        var cells = new List<GridCell>();
        foreach (var (span, from) in dropped)
        {
            var target = anchor is not null && from is not null
                ? new GridCell(Math.Max(0, dropCell.Column + from.Column - anchor.Column), Math.Max(0, dropCell.Row + from.Row - anchor.Row))
                : dropCell;
            var cell = NearestFree(placed, span, target, columns);
            placed.Add((cell, span.Clamp(columns)));
            cells.Add(cell);
        }
        return cells;
    }

    /// <summary>A fence's columns for its width and cell width (M28: a hidden tab's own, not another window's); at least one.</summary>
    public static int ColumnsFor(double width, double cellWidth) =>
        double.IsFinite(width) && cellWidth > 0 ? Math.Max(1, (int)Math.Floor(width / cellWidth)) : 1;

    /// <summary>The size several elements share (Size ▸ shows it checked), or AllSame false when they differ (M28).</summary>
    public static (bool AllSame, GridSpan? Size) CommonSize(IReadOnlyList<GridSpan?> sizes) =>
        sizes.Count > 0 && sizes.All(size => size == sizes[0]) ? (true, sizes[0]) : (false, null);

    /// <summary>Which cells are taken: a row of flags per grid row, grown as elements are placed.</summary>
    private sealed class Occupancy(int columns)
    {
        private readonly List<bool[]> _rows = [];

        public int Rows => _rows.Count;

        public bool IsFree(GridCell cell, GridSpan span)
        {
            if (cell.Column + span.Columns > columns) return false;
            for (var row = cell.Row; row < cell.Row + span.Rows && row < _rows.Count; row++)
            {
                for (var column = cell.Column; column < cell.Column + span.Columns; column++)
                {
                    if (_rows[row][column]) return false;
                }
            }
            return true;
        }

        public void Take(GridCell cell, GridSpan span)
        {
            while (_rows.Count < cell.Row + span.Rows) _rows.Add(new bool[columns]);
            for (var row = cell.Row; row < cell.Row + span.Rows; row++)
            {
                for (var column = cell.Column; column < Math.Min(columns, cell.Column + span.Columns); column++) _rows[row][column] = true;
            }
        }

        /// <summary>Scanning rows top to bottom and columns left to right: the first cell where the whole span is free.</summary>
        public GridCell FirstFree(GridSpan span)
        {
            for (var row = 0; ; row++)
            {
                for (var column = 0; column + span.Columns <= columns; column++)
                {
                    var cell = new GridCell(column, row);
                    if (IsFree(cell, span)) return cell;
                }
            }
        }
    }
}
