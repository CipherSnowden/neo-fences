using System.Windows;
using System.Windows.Controls;
using NeoFences.Core.Items;

namespace NeoFences.App;

/// <summary>
/// The fence's items panel (M24, spec 2026-10-05-element-sizes-design §3): every element on whole cells, spanning its
/// columns × rows, where Core's <see cref="FenceGrid"/> puts it (packed in order, or at stored cells). The columns follow
/// the fence's width; a layout pass that fails falls back to one element per cell in order.
/// </summary>
public sealed class FenceGridPanel : Panel
{
    public static readonly DependencyProperty CellWidthProperty = DependencyProperty.Register(nameof(CellWidth), typeof(double),
        typeof(FenceGridPanel), new FrameworkPropertyMetadata(84.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty CellHeightProperty = DependencyProperty.Register(nameof(CellHeight), typeof(double),
        typeof(FenceGridPanel), new FrameworkPropertyMetadata(96.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(nameof(Layout), typeof(FenceLayout),
        typeof(FenceGridPanel), new FrameworkPropertyMetadata(FenceLayout.Flow, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double CellWidth { get => (double)GetValue(CellWidthProperty); set => SetValue(CellWidthProperty, value); }
    public double CellHeight { get => (double)GetValue(CellHeightProperty); set => SetValue(CellHeightProperty, value); }
    public FenceLayout Layout { get => (FenceLayout)GetValue(LayoutProperty); set => SetValue(LayoutProperty, value); }

    /// <summary>The column count of the last layout pass (the fence's width over the cell width, at least one).</summary>
    public int Columns { get; private set; } = 1;

    /// <summary>The last layout: each child's cell and span, in child order.</summary>
    public GridArrangement Arrangement { get; private set; } = new([], [], 0);

    // ponytail: arranged at every measure (one pass over a map of cells, ~µs for 500 elements); cache per element set if a profile ever shows it.
    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren.Cast<UIElement>().ToList();
        Columns = double.IsInfinity(availableSize.Width) ? Math.Max(1, children.Count) : Math.Max(1, (int)Math.Floor(availableSize.Width / CellWidth));
        var elements = children.Select(child => (child as FrameworkElement)?.DataContext is FenceItemView view
            ? new GridElement(view.Span, view.StoredCell) : new GridElement(GridSpan.One, null)).ToList();
        try
        {
            Arrangement = FenceGrid.Arrange(elements, Columns, Layout);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Serilog.Log.Warning(failure, "fence grid: layout failed; one element per cell in order"); // never a crash (spec §4)
            Arrangement = FenceGrid.Arrange([.. elements.Select(_ => new GridElement(GridSpan.One, null))], Columns, FenceLayout.Flow);
        }
        for (var index = 0; index < children.Count; index++)
        {
            var span = Arrangement.Spans[index];
            children[index].Measure(new Size(span.Columns * CellWidth, span.Rows * CellHeight));
        }
        var width = double.IsInfinity(availableSize.Width) ? Columns * CellWidth : Math.Min(availableSize.Width, Columns * CellWidth);
        return new Size(width, Arrangement.Rows * CellHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren.Cast<UIElement>().ToList();
        for (var index = 0; index < children.Count && index < Arrangement.Cells.Count; index++)
        {
            children[index].Arrange(CellRect(Arrangement.Cells[index], Arrangement.Spans[index]));
        }
        return finalSize;
    }

    /// <summary>The cell under a point of the panel.</summary>
    public GridCell CellAt(Point point) => FenceGrid.CellAt(point.X, point.Y, CellWidth, CellHeight);

    public Rect CellRect(GridCell cell, GridSpan span) => new(cell.Column * CellWidth, cell.Row * CellHeight, span.Columns * CellWidth, span.Rows * CellHeight);
}
