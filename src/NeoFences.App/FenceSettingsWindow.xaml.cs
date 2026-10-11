using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NeoFences.Core.Config;
using NeoFences.Core.Items;
using NeoFences.Core.Model;

namespace NeoFences.App;

/// <summary>What Fence settings shows: the fence, Settings' look (for "Like all fences (…)"), the presets and the lit one.</summary>
public sealed record FenceSettingsView(Fence Fence, AppearanceSettings Appearance, bool LightTheme, IReadOnlyList<LookPreset> Presets, string? Matching,
    OpenWith OpenItemsWith = OpenWith.OneClick);

/// <summary>
/// Fence menu → Fence settings… (M36, spec §1): presets, then the look (each setting "Like all fences" or the fence's own),
/// then the items. Every change goes to the host at once; the host applies, saves and shows it back.
/// </summary>
public partial class FenceSettingsWindow : Window
{
    public event Action<OwnLook?>? LookChanged;
    public event Action<int>? IconSizeChanged;
    public event Action<LabelMode>? LabelsChanged;
    public event Action<FenceLayout>? LayoutChanged;
    /// <summary>M41a: the fence's own "Open items with", or null (like all fences).</summary>
    public event Action<OpenWith?>? OpenWithChanged;
    public event Action<LookPreset>? PresetChosen;
    public event Action? LikeAllChosen;
    public event Action<string>? PresetSaved;
    public event Action<string>? PresetDeleted;
    /// <summary>M41a: the fence's emoji (one grapheme), or null for none.</summary>
    public event Action<string?>? EmojiChanged;
    /// <summary>M41a: "Widgets float".</summary>
    public event Action<bool>? WidgetsFloatChanged;

    private const string LikeAllName = "Like all fences";
    private static readonly string[] ThemeNames = ["Classic", "Aurora glass", "Game HUD", "Neon", "Light Mica"]; // FenceTheme's order
    private static readonly string[] TitleStyleNames = ["Simple", "Clean", "Pill", "Caps"]; // TitleStyle's order
    private static readonly string[] EmojiChoices = ["🎮", "🕹️", "💼", "📁", "⬇️", "🎵", "🎬", "📷", "🛠️", "💻", "🌐", "📚", "⭐", "🔥", "🧪", "🗂️"];
    private static readonly ColourStyle[] Styles = [ColourStyle.AccentEdge, ColourStyle.TintedGlass, ColourStyle.TitleStrip];
    private static readonly string[] StyleNames = ["Accent edge", "Tinted glass", "Title strip"];
    private static readonly (int Size, string Name)[] TitleSizes = [(12, "Small"), (14, "Normal"), (17, "Large"), (20, "Huge")];
    private static readonly (int Size, string Name)[] IconSizes = [(32, "Small"), (48, "Medium"), (64, "Large"), (96, "Extra large")];
    private static readonly Spacing[] Spacings = [Spacing.Compact, Spacing.Normal, Spacing.Roomy];
    private static readonly Lazy<List<string>> InstalledFonts =
        new(() => [.. System.Windows.Media.Fonts.SystemFontFamilies.Select(family => family.Source).Distinct().Order(StringComparer.CurrentCultureIgnoreCase)]);

    private FenceSettingsView? _view;
    private int? _ownStrength;
    private bool _updating;

    public string FenceId { get; }

    public FenceSettingsWindow(string fenceId)
    {
        FenceId = fenceId;
        InitializeComponent();
        StyleBox.SelectionChanged += (_, _) => ReportLook();
        ThemeBox.SelectionChanged += (_, _) => ReportLook(); // M41a
        FloatBox.Checked += (_, _) => { if (!_updating) WidgetsFloatChanged?.Invoke(true); };
        FloatBox.Unchecked += (_, _) => { if (!_updating) WidgetsFloatChanged?.Invoke(false); };
        TitleStyleBox.SelectionChanged += (_, _) => ReportLook();
        EmojiBox.TextChanged += (_, _) =>
        {
            if (!_updating && _view is { } shown && Emoji.Normalize(EmojiBox.Text) != shown.Fence.Emoji) EmojiChanged?.Invoke(Emoji.Normalize(EmojiBox.Text));
        };
        foreach (var choice in EmojiChoices.Append(""))
        {
            var pick = new Button
            {
                Content = choice.Length == 0 ? "None" : choice, FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"), FontSize = choice.Length == 0 ? 12 : 16,
                Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 0, 4, 4), MinWidth = 34,
            };
            System.Windows.Automation.AutomationProperties.SetName(pick, choice.Length == 0 ? "No emoji" : $"Emoji {choice}");
            pick.Click += (_, _) => EmojiBox.Text = choice;
            EmojiPicks.Children.Add(pick);
        }
        StrengthSlider.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            _ownStrength = (int)Math.Round(StrengthSlider.Value);
            ReportLook();
        };
        StrengthLikeAllButton.Click += (_, _) =>
        {
            _ownStrength = null;
            ReportLook();
        };
        FontFamilyBox.SelectionChanged += (_, _) => ReportLook();
        FontSizeBox.SelectionChanged += (_, _) => ReportLook();
        FontWeightBox.SelectionChanged += (_, _) => ReportLook();
        AlignBox.SelectionChanged += (_, _) => ReportLook();
        TitleBarBox.Checked += (_, _) => ReportLook();
        TitleBarBox.Unchecked += (_, _) => ReportLook();
        SpacingBox.SelectionChanged += (_, _) => ReportLook();
        IconSizeBox.SelectionChanged += (_, _) =>
        {
            if (!_updating && IconSizeBox.SelectedIndex >= 0) IconSizeChanged?.Invoke(IconSizes[IconSizeBox.SelectedIndex].Size);
        };
        LabelsBox.SelectionChanged += (_, _) =>
        {
            if (!_updating && LabelsBox.SelectedIndex >= 0) LabelsChanged?.Invoke(LabelsBox.SelectedIndex == 0 ? LabelMode.Always : LabelMode.OnHover);
        };
        LayoutBox.SelectionChanged += (_, _) =>
        {
            if (!_updating && LayoutBox.SelectedIndex >= 0) LayoutChanged?.Invoke(LayoutBox.SelectedIndex == 0 ? FenceLayout.Flow : FenceLayout.Free);
        };
        OpenWithBox.SelectionChanged += (_, _) =>
        {
            if (!_updating && OpenWithBox.SelectedIndex >= 0) OpenWithChanged?.Invoke(OpenWithBox.SelectedIndex switch { 1 => OpenWith.OneClick, 2 => OpenWith.DoubleClick, _ => null });
        };
        LikeAllAgainButton.Click += (_, _) => LikeAllChosen?.Invoke();
        CloseButton.Click += (_, _) => Close(); // IsCancel does not close a window shown modeless (M35)
        SavePresetButton.Click += (_, _) =>
        {
            SavePanel.Visibility = Visibility.Visible;
            SaveProblem.Visibility = Visibility.Collapsed;
            PresetNameBox.Text = "";
            PresetNameBox.Focus();
        };
        SaveCancelButton.Click += (_, _) => SavePanel.Visibility = Visibility.Collapsed;
        SaveConfirmButton.Click += (_, _) => SavePreset();
        PresetNameBox.KeyDown += (_, key) =>
        {
            if (key.Key != System.Windows.Input.Key.Enter) return;
            key.Handled = true;
            SavePreset();
        };
        IconSizeBox.ItemsSource = IconSizes.Select(size => size.Name).ToList();
    }

    /// <summary>Shows the fence's settings as they are now (after every change the host made).</summary>
    public void Show(FenceSettingsView view)
    {
        // Rebuilt only when something changed: a refresh on activation must not disturb a list being opened.
        if (_view is { } shown && shown.Fence == view.Fence && shown.Appearance == view.Appearance && shown.LightTheme == view.LightTheme
            && shown.Matching == view.Matching && shown.Presets.SequenceEqual(view.Presets) && shown.OpenItemsWith == view.OpenItemsWith) return;
        _updating = true;
        try
        {
            _view = view;
            var fence = view.Fence;
            var own = fence.Look ?? new OwnLook();
            var settings = view.Appearance;
            Title = $"{fence.Title} — fence settings";
            ShowPresets(view);
            LikeAllAgainButton.IsEnabled = fence.Look is not null;

            ThemeBox.ItemsSource = ThemeNames.Prepend($"{LikeAllName} ({ThemeNames[(int)settings.Theme]})").ToList(); // M41a
            ThemeBox.SelectedIndex = own.Theme is { } theme ? (int)theme + 1 : 0;
            ThemeOwn.Visibility = Visible(own.Theme is not null);
            TitleStyleBox.ItemsSource = TitleStyleNames.Prepend($"{LikeAllName} ({TitleStyleNames[(int)settings.TitleStyle]})").ToList();
            TitleStyleBox.SelectedIndex = own.TitleStyle is { } titleStyle ? (int)titleStyle + 1 : 0;
            TitleStyleOwn.Visibility = Visible(own.TitleStyle is not null);
            if (Emoji.Normalize(EmojiBox.Text) != fence.Emoji) EmojiBox.Text = fence.Emoji ?? "";

            StyleBox.ItemsSource = StyleNames.Prepend($"{LikeAllName} ({StyleNames[Array.IndexOf(Styles, settings.ColourStyle)]})").ToList();
            StyleBox.SelectedIndex = own.ColourStyle is { } style ? Array.IndexOf(Styles, style) + 1 : 0;
            StyleOwn.Visibility = Visible(own.ColourStyle is not null);

            _ownStrength = own.Strength;
            var shownTheme = own.Theme ?? settings.Theme; // M41a: the strength of the tone the fence shows
            var lightTone = shownTheme == FenceTheme.LightMica || (shownTheme == FenceTheme.Classic && view.LightTheme);
            var allStrength = lightTone ? settings.StrengthLight : settings.StrengthDark;
            StrengthSlider.Value = own.Strength ?? allStrength;
            StrengthDescription.Text = own.Strength is { } strength
                ? $"This fence's own: {strength} % in light and dark mode."
                : $"{LikeAllName} ({allStrength} % for {(lightTone ? "light" : "dark")} fences).";
            StrengthDescription.SetResourceReference(TextBlock.ForegroundProperty, own.Strength is null ? "TextFillColorSecondaryBrush" : "AccentTextFillColorPrimaryBrush");
            StrengthLikeAllButton.Visibility = Visible(own.Strength is not null);

            var font = own.TitleFont;
            var fonts = InstalledFonts.Value;
            if (font?.Family is { } family && !fonts.Contains(family)) fonts = [.. fonts.Append(family).Order(StringComparer.CurrentCultureIgnoreCase)]; // a font that is gone stays listed
            FontFamilyBox.ItemsSource = fonts.Prepend($"{LikeAllName} ({settings.TitleFont.Family})").ToList();
            FontFamilyBox.SelectedIndex = font?.Family is { } chosen ? fonts.IndexOf(chosen) + 1 : 0;
            FontSizeBox.ItemsSource = TitleSizes.Select(size => size.Name).Prepend($"{LikeAllName} ({NameOf(TitleSizes, settings.TitleFont.Size)})").ToList();
            FontSizeBox.SelectedIndex = font?.Size is { } size ? Array.FindIndex(TitleSizes, entry => entry.Size == size) + 1 : 0;
            FontWeightBox.ItemsSource = Enum.GetNames<TitleWeight>().Prepend($"{LikeAllName} ({settings.TitleFont.Weight})").ToList();
            FontWeightBox.SelectedIndex = font?.Weight is { } weight ? (int)weight + 1 : 0;
            AlignBox.ItemsSource = new[] { $"{LikeAllName} (Left)", "Left", "Centre", "Right" };
            AlignBox.SelectedIndex = own.TitleAlign is { } align ? (int)align + 1 : 0;
            TitleOwn.Visibility = Visible(font is not null || own.TitleAlign is not null);
            TitleBarBox.IsChecked = own.TitleOnHover != true;

            IconSizeBox.SelectedIndex = Array.FindIndex(IconSizes, entry => entry.Size == fence.IconSize);
            LabelsBox.SelectedIndex = fence.Labels == LabelMode.Always ? 0 : 1;
            LayoutRow.Visibility = Visible(fence.Kind == FenceKind.Items); // as in View ▸: only a fence of items has a layout
            LayoutBox.SelectedIndex = fence.Layout == FenceLayout.Flow ? 0 : 1;
            SpacingBox.ItemsSource = new[] { $"{LikeAllName} (Normal)", "Compact", "Normal", "Roomy" };
            SpacingBox.SelectedIndex = own.Spacing is { } spacing ? Array.IndexOf(Spacings, spacing) + 1 : 0;
            SpacingOwn.Visibility = Visible(own.Spacing is not null);
            OpenWithBox.ItemsSource = new[] { $"{LikeAllName} ({(view.OpenItemsWith == OpenWith.OneClick ? "One click" : "Double click")})", "One click", "Double click" };
            OpenWithBox.SelectedIndex = fence.OpenWith switch { OpenWith.OneClick => 1, OpenWith.DoubleClick => 2, _ => 0 };
            OpenWithOwn.Visibility = Visible(fence.OpenWith is not null);
            FloatBox.IsChecked = fence.WidgetsFloat;
            FloatRow.Visibility = Visible(fence.Kind == FenceKind.Items);
        }
        finally
        {
            _updating = false;
        }
    }

    private void ShowPresets(FenceSettingsView view)
    {
        PresetChips.Children.Clear();
        PresetChips.Children.Add(Chip(LikeAllName, isOn: view.Fence.Look is null, () => LikeAllChosen?.Invoke()));
        foreach (var preset in view.Presets)
        {
            var chip = Chip(preset.Name, isOn: preset.Name == view.Matching, () => PresetChosen?.Invoke(preset));
            if (LookPresets.BuiltIn.Contains(preset))
            {
                PresetChips.Children.Add(chip);
                continue;
            }
            // An own preset: its chip and a small ✕ that deletes it (built-ins cannot be deleted).
            var delete = new Button
            {
                Content = "✕", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(-4, 0, 6, 6), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = $"Delete the preset “{preset.Name}” (fences keep their look)",
            };
            System.Windows.Automation.AutomationProperties.SetName(delete, $"Delete preset {preset.Name}");
            delete.Click += (_, _) => PresetDeleted?.Invoke(preset.Name);
            PresetChips.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { chip, delete } });
        }
    }

    private static ToggleButton Chip(string name, bool isOn, Action chosen)
    {
        var chip = new ToggleButton { Content = name, IsChecked = isOn, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 6) };
        chip.Click += (_, _) =>
        {
            chip.IsChecked = isOn; // the host's answer decides which chip is lit
            chosen();
        };
        return chip;
    }

    private void SavePreset()
    {
        if (LookPresets.NameProblem(PresetNameBox.Text) is { } problem)
        {
            SaveProblem.Text = problem;
            SaveProblem.Visibility = Visibility.Visible;
            return;
        }
        SavePanel.Visibility = Visibility.Collapsed;
        PresetSaved?.Invoke(PresetNameBox.Text);
    }

    /// <summary>The look the controls show, as the fence's own (each "Like all fences" choice is null).</summary>
    private void ReportLook()
    {
        if (_updating || _view is null) return;
        var family = FontFamilyBox.SelectedIndex > 0 ? FontFamilyBox.SelectedItem as string : null;
        int? size = FontSizeBox.SelectedIndex > 0 ? TitleSizes[FontSizeBox.SelectedIndex - 1].Size : null;
        TitleWeight? weight = FontWeightBox.SelectedIndex > 0 ? (TitleWeight)(FontWeightBox.SelectedIndex - 1) : null;
        var look = new OwnLook
        {
            ColourStyle = StyleBox.SelectedIndex > 0 ? Styles[StyleBox.SelectedIndex - 1] : null,
            Strength = _ownStrength,
            TitleFont = family is null && size is null && weight is null ? null : new TitleFont(family, size, weight),
            TitleAlign = AlignBox.SelectedIndex > 0 ? (TitleAlign)(AlignBox.SelectedIndex - 1) : null,
            TitleOnHover = TitleBarBox.IsChecked == true ? null : true,
            Spacing = SpacingBox.SelectedIndex > 0 ? Spacings[SpacingBox.SelectedIndex - 1] : null,
            Theme = ThemeBox.SelectedIndex > 0 ? (FenceTheme)(ThemeBox.SelectedIndex - 1) : null, // M41a
            TitleStyle = TitleStyleBox.SelectedIndex > 0 ? (TitleStyle)(TitleStyleBox.SelectedIndex - 1) : null,
        };
        if (ConfigNormalizer.NormalizeLook(look) == ConfigNormalizer.NormalizeLook(_view.Fence.Look)) return;
        LookChanged?.Invoke(look);
    }

    private static string NameOf((int Size, string Name)[] sizes, int? size) =>
        sizes.FirstOrDefault(entry => entry.Size == size).Name ?? $"{size}";

    private static Visibility Visible(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
