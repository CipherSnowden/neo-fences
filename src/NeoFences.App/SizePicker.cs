using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NeoFences.Core.Items;

namespace NeoFences.App;

/// <summary>
/// Item menu → Size ▸ (M24, spec §2): a 4×4 grid of squares; hovering highlights columns × rows with a caption, a click
/// sets it; "Default size" under it. Closes its menu when a size is picked. M28: the arrow keys move the highlight and Enter
/// picks; leaving the grid shows the current size again; several elements of different sizes check nothing.
/// </summary>
public static class SizePicker
{
    private const double Square = 16;

    public static MenuItem Create(ContextMenu menu, (bool AllSame, GridSpan? Size) common, Action<GridSpan?> pick)
    {
        var current = common.Size;
        var size = new MenuItem { Header = "Size" };
        string Shown() => !common.AllSame ? "Mixed sizes" : current is { } now ? $"{now.Columns} × {now.Rows}" : "Default size";
        var caption = new TextBlock { Margin = new Thickness(2, 6, 0, 0), FontSize = 12, Text = Shown() };
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
        var (keyColumns, keyRows) = current is { } start ? (start.Columns, start.Rows) : (1, 1);
        void ShowCurrent()
        {
            if (current is { } chosen) Highlight(chosen.Columns, chosen.Rows);
            else Highlight(0, 0);
            caption.Text = Shown();
        }
        ShowCurrent();
        grid.MouseLeave += (_, _) => ShowCurrent();
        var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        panel.Children.Add(grid);
        panel.Children.Add(caption);
        var squaresItem = new MenuItem { Header = panel, StaysOpenOnClick = true };
        System.Windows.Automation.AutomationProperties.SetName(squaresItem, "Size: arrow keys choose, Enter sets");
        // Arrow keys move the highlight; Enter picks it.
        squaresItem.GotKeyboardFocus += (_, _) => { Highlight(keyColumns, keyRows); caption.Text = $"{keyColumns} × {keyRows}"; };
        squaresItem.PreviewKeyDown += (_, key) =>
        {
            switch (key.Key)
            {
                case System.Windows.Input.Key.Left: keyColumns = Math.Max(1, keyColumns - 1); break;
                case System.Windows.Input.Key.Right: keyColumns = Math.Min(GridSpan.Max, keyColumns + 1); break;
                case System.Windows.Input.Key.Up when keyRows > 1: keyRows--; break;
                case System.Windows.Input.Key.Down when keyRows < GridSpan.Max: keyRows++; break;
                case System.Windows.Input.Key.Enter:
                    key.Handled = true;
                    menu.IsOpen = false;
                    pick(new GridSpan(keyColumns, keyRows));
                    return;
                default: return; // Up at the top row and Down at the bottom one move on through the menu
            }
            key.Handled = true;
            Highlight(keyColumns, keyRows);
            caption.Text = $"{keyColumns} × {keyRows}";
        };
        size.Items.Add(squaresItem);
        var reset = new MenuItem { Header = "Default size", IsChecked = common.AllSame && current is null };
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
