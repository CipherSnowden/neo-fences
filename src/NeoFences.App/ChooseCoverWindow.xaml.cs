using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>
/// "Choose cover…" on a game tile (M34, spec §3): up to 8 Steam store results for the game's name as thumbnails (when
/// online art is on), a picture of the user's own, or back to automatic. The host stores the choice and copies the picture
/// into NeoFences' covers folder; the original file is never moved or changed.
/// </summary>
public partial class ChooseCoverWindow : Window
{
    private const int MaxResults = 8;

    /// <summary>A Steam result was picked: its cover's address.</summary>
    public event Action<Uri>? StoreCoverChosen;

    /// <summary>A picture of the user's own was picked (its path; the host copies it).</summary>
    public event Action<string>? FileChosen;

    public event Action? ResetRequested;

    public ChooseCoverWindow(string gameName, bool online, bool hasChoice)
    {
        InitializeComponent();
        Title = $"Choose cover — {gameName}";
        HeadingText.Text = $"Choose a cover for {gameName}";
        ResetButton.IsEnabled = hasChoice;
        CloseButton.Click += (_, _) => Close(); // modeless: IsCancel alone would not close it
        ResetButton.Click += (_, _) => { ResetRequested?.Invoke(); Close(); };
        FromFileButton.Click += (_, _) =>
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Title = "Choose a cover picture", Filter = "Pictures (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
            if (picker.ShowDialog(this) != true) return;
            FileChosen?.Invoke(picker.FileName);
            Close();
        };
        if (online) Loaded += async (_, _) => await ShowResultsAsync(gameName);
        else StatusText.Text = "Turn on \"Find covers and website icons online\" in Settings → Game Library to see covers from the Steam store.";
    }

    private async Task ShowResultsAsync(string gameName)
    {
        StatusText.Text = "Looking for covers on the Steam store…";
        var results = await OnlineArt.SearchAsync(gameName);
        if (results is null)
        {
            StatusText.Text = "The Steam store could not be reached. Try again later, or choose a picture of your own.";
            return;
        }
        var shown = results.Take(MaxResults).ToList();
        var covers = await OnlineArt.CoverUrlsAsync([.. shown.Select(result => result.AppId)]);
        var found = shown.Where(result => covers.ContainsKey(result.AppId)).ToList();
        StatusText.Text = found.Count == 0 ? "The Steam store has no cover for this name. Choose a picture of your own."
            : "Click a cover to use it.";
        foreach (var result in found)
        {
            var button = new Button { Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(4), ToolTip = result.Name, Width = 112 };
            System.Windows.Automation.AutomationProperties.SetName(button, result.Name);
            var content = new StackPanel();
            var picture = new Image { Width = 100, Height = 150, Stretch = Stretch.UniformToFill };
            content.Children.Add(picture);
            content.Children.Add(new TextBlock { Text = result.Name, FontSize = 11, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 30, Margin = new Thickness(0, 4, 0, 0) });
            button.Content = content;
            var cover = covers[result.AppId];
            button.Click += (_, _) => { StoreCoverChosen?.Invoke(cover); Close(); };
            Results.Children.Add(button);
            if (await OnlineArt.GetImageAsync(cover) is { } bytes) picture.Source = Thumbnail(bytes);
        }
    }

    private static BitmapImage? Thumbnail(byte[] bytes)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 200;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Serilog.Log.Warning(failure, "a Steam cover could not be shown");
            return null;
        }
    }
}
