using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NeoFences.Core.Model;

namespace NeoFences.App;

/// <summary>What Settings → Appearance shows (M14): the settings, the tone now, and the wallpaper accent with its source.</summary>
public sealed record AppearanceView(AppearanceSettings Settings, bool LightTheme, string? AccentHex, string AccentOrigin);

/// <summary>
/// Settings → Appearance (M14, spec §5): colour style, background strength for the current tone, the wallpaper accent
/// switch and the title font. Changes apply live: every change goes to the host, which restyles, saves and shows it back.
/// </summary>
public partial class SettingsWindow
{
    public event Action<AppearanceSettings>? AppearanceChanged;

    private AppearanceView? _appearance;

    private void InitializeAppearance()
    {
        foreach (var style in new[] { AccentEdgeStyle, TintedGlassStyle, TitleStripStyle })
        {
            style.Checked += (_, _) => ReportAppearance();
        }
        StrengthSlider.ValueChanged += (_, _) => ReportAppearance();
        AccentBox.Checked += (_, _) => ReportAppearance();
        AccentBox.Unchecked += (_, _) => ReportAppearance();
        FontFamilyBox.ItemsSource = Fonts.SystemFontFamilies.Select(family => family.Source).Distinct().Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        FontFamilyBox.SelectionChanged += (_, _) => ReportAppearance();
        FontSizeBox.SelectionChanged += (_, _) => ReportAppearance();
        FontWeightBox.SelectionChanged += (_, _) => ReportAppearance();
    }

    private void ShowAppearance(AppearanceView view)
    {
        _appearance = view;
        var settings = view.Settings;
        AccentEdgeStyle.IsChecked = settings.ColourStyle == ColourStyle.AccentEdge;
        TintedGlassStyle.IsChecked = settings.ColourStyle == ColourStyle.TintedGlass;
        TitleStripStyle.IsChecked = settings.ColourStyle == ColourStyle.TitleStrip;
        StrengthSlider.Value = view.LightTheme ? settings.StrengthLight : settings.StrengthDark;
        StrengthDescription.Text = view.LightTheme
            ? "Light mode (Windows is light now). Dark mode keeps its own value."
            : "Dark mode (Windows is dark now). Light mode keeps its own value.";
        AccentBox.IsChecked = settings.WallpaperAccent;
        AccentSwatch.Visibility = view.AccentHex is null ? Visibility.Collapsed : Visibility.Visible;
        if (view.AccentHex is { } hex) AccentSwatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        AccentSource.Text = view.AccentOrigin;
        AccentSource.Visibility = view.AccentOrigin.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (FontFamilyBox.ItemsSource is List<string> fonts && settings.TitleFont.Family is { } family && !fonts.Contains(family))
        {
            // A font that is gone (uninstalled): its name stays listed and selected, the fences fall back to Segoe UI (M16).
            FontFamilyBox.ItemsSource = fonts.Append(family).Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        FontFamilyBox.SelectedItem = settings.TitleFont.Family;
        FontSizeBox.SelectedItem = FontSizeBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, settings.TitleFont.Size.ToString()));
        FontWeightBox.SelectedItem = FontWeightBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, settings.TitleFont.Weight.ToString()));
    }

    private void ReportAppearance()
    {
        if (_updating || _appearance is not { } shown) return;
        var current = shown.Settings;
        var strength = (int)Math.Round(StrengthSlider.Value);
        var style = TintedGlassStyle.IsChecked == true ? ColourStyle.TintedGlass
            : TitleStripStyle.IsChecked == true ? ColourStyle.TitleStrip : ColourStyle.AccentEdge;
        var font = new TitleFont(
            FontFamilyBox.SelectedItem as string ?? current.TitleFont.Family,
            (FontSizeBox.SelectedItem as ComboBoxItem)?.Tag is string size && int.TryParse(size, out var points) ? points : current.TitleFont.Size,
            (FontWeightBox.SelectedItem as ComboBoxItem)?.Tag is string weight && Enum.TryParse<TitleWeight>(weight, out var parsed) ? parsed : current.TitleFont.Weight);
        var changed = current with
        {
            ColourStyle = style,
            StrengthDark = shown.LightTheme ? current.StrengthDark : strength,
            StrengthLight = shown.LightTheme ? strength : current.StrengthLight,
            WallpaperAccent = AccentBox.IsChecked == true,
            TitleFont = font,
        };
        if (changed == current) return;
        _appearance = shown with { Settings = changed };
        AppearanceChanged?.Invoke(changed);
    }
}
