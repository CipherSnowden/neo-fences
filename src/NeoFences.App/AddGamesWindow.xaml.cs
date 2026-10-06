using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using NeoFences.Core.Library;

namespace NeoFences.App;

/// <summary>
/// Fence menu → "Add games…" (M22 spec §2): one row per game the scan found (cover, name, source), ticked unless the fence
/// already has it. The list fills in again when a scan finishes while it is open; ticks the user changed are kept.
/// </summary>
public partial class AddGamesWindow : Window
{
    /// <param name="Poster">The game's cover, if the scan found one.</param>
    public sealed record Row(LibraryItem Game, string? Poster, bool AlreadyHere);

    private const int CoverWidth = 32;
    private readonly List<(CheckBox Box, LibraryItem Game)> _rows = [];
    private readonly Dictionary<string, bool> _changedByUser = new(StringComparer.OrdinalIgnoreCase);

    public string FenceId { get; }

    public IReadOnlyList<LibraryItem> Chosen { get; private set; } = [];

    public AddGamesWindow(string fenceId)
    {
        InitializeComponent();
        FenceId = fenceId;
        AddButton.Click += (_, _) =>
        {
            Chosen = [.. _rows.Where(row => row.Box.IsChecked == true).Select(row => row.Game)];
            DialogResult = true;
        };
    }

    /// <param name="scanning">A scan is still running: an empty list says so instead of "no games".</param>
    public void ShowGames(IReadOnlyList<Row> rows, bool scanning = false)
    {
        GamesPanel.Children.Clear();
        _rows.Clear();
        foreach (var row in rows.OrderBy(row => row.Game.Game.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            // ponytail: covers decoded on the UI thread while the list is built; off it if libraries of hundreds of games pause here.
            if (LoadCover(row.Poster) is { } cover) content.Children.Add(new Image { Source = cover, Width = CoverWidth, Height = CoverWidth * 1.5, Margin = new Thickness(0, 0, 10, 0) });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = row.Game.Game.Name });
            text.Children.Add(new TextBlock
            {
                Text = GameCatalog.SourceName(row.Game.Game.ScanKey) + (row.AlreadyHere ? " · already here" : ""), FontSize = 11,
                Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorSecondaryBrush"),
            });
            content.Children.Add(text);
            var id = row.Game.Game.Id;
            var box = new CheckBox { Content = content, IsChecked = _changedByUser.TryGetValue(id, out var ticked) ? ticked : !row.AlreadyHere, Margin = new Thickness(4, 3, 0, 3) };
            System.Windows.Automation.AutomationProperties.SetName(box, row.Game.Game.Name);
            box.Click += (_, _) => { _changedByUser[id] = box.IsChecked == true; UpdateAddButton(); };
            GamesPanel.Children.Add(box);
            _rows.Add((box, row.Game));
        }
        ListStatus.Text = rows.Count > 0 ? "" : scanning ? "Looking for games…" : "No games found — add your games folder in Settings → Games.";
        ListStatus.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateAddButton();
    }

    private void UpdateAddButton() => AddButton.IsEnabled = _rows.Any(row => row.Box.IsChecked == true);

    private static BitmapImage? LoadCover(string? path)
    {
        if (path is null) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // the file is not kept open
            image.DecodePixelWidth = CoverWidth * 2;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Serilog.Log.Debug(failure, "Add games: cover {Path} not shown", path); // the row keeps its name
            return null;
        }
    }
}
