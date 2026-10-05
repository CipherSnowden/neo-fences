using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NeoFences.Core.Items;

namespace NeoFences.App;

/// <summary>
/// Item menu → Size ▸ (M24, spec §2): a 4×4 grid of squares; hovering highlights columns × rows with a caption, a click
/// sets it; "Default size" under it. Closes its menu when a size is picked.
/// </summary>
public static class SizePicker
{
    private const double Square = 16;

    public static MenuItem Create(ContextMenu menu, GridSpan? current, Action<GridSpan?> pick)
    {
        var size = new MenuItem { Header = "Size" };
        var caption = new TextBlock { Margin = new Thickness(2, 6, 0, 0), FontSize = 12, Text = current is { } now ? $"{now.Columns} × {now.Rows}" : "Default size" };
        var grid = new UniformGrid4();
        var squares = new Border[GridSpan.Max, GridSpan.Max];
        void Highlight(int columns, int rows)
        {
            for (var row = 0; row < GridSpan.Max; row++)
            {
                for (var column = 0; column < GridSpan.Max; column++)
                {
                    squares[row, column].Background = column < columns && row < rows ? SystemColors.HighlightBrush : Brushes.Transparent;
                }
            }
        }
        for (var row = 0; row < GridSpan.Max; row++)
        {
            for (var column = 0; column < GridSpan.Max; column++)
            {
                var (columns, rows) = (column + 1, row + 1);
                var square = new Border
                {
                    Width = Square, Height = Square, Margin = new Thickness(1), CornerRadius = new CornerRadius(2), BorderThickness = new Thickness(1),
                    BorderBrush = SystemColors.GrayTextBrush, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand,
                };
                System.Windows.Automation.AutomationProperties.SetName(square, $"{columns} × {rows}");
                square.MouseEnter += (_, _) => { Highlight(columns, rows); caption.Text = $"{columns} × {rows}"; };
                square.MouseLeftButtonUp += (_, click) =>
                {
                    click.Handled = true;
                    menu.IsOpen = false;
                    pick(new GridSpan(columns, rows));
                };
                squares[row, column] = square;
                grid.Children.Add(square);
            }
        }
        if (current is { } chosen) Highlight(chosen.Columns, chosen.Rows);
        var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        panel.Children.Add(grid);
        panel.Children.Add(caption);
        size.Items.Add(new MenuItem { Header = panel, StaysOpenOnClick = true, Focusable = false });
        var reset = new MenuItem { Header = "Default size", IsChecked = current is null };
        reset.Click += (_, _) => pick(null);
        size.Items.Add(new Separator());
        size.Items.Add(reset);
        return size;
    }

    /// <summary>Four squares a row.</summary>
    private sealed class UniformGrid4 : System.Windows.Controls.Primitives.UniformGrid
    {
        public UniformGrid4() => Columns = GridSpan.Max;
    }
}
