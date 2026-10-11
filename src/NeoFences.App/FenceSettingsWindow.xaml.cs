using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NeoFences.Core.Appearance;
using NeoFences.Core.Items;
using NeoFences.Core.Model;

namespace NeoFences.App;

/// <summary>
/// What Fence settings shows (M41b): the fence (null in the window opened for All fences), the whole config (All fences'
/// values, the recipes), whether it edits All fences, and what the summary line needs.
/// </summary>
public sealed record FenceSettingsView(Fence? Fence, NeoFencesConfig Config, bool LightTheme, bool AllFences, int UndoCount, int ItemCount, bool OnlyWidgets);

/// <summary>
/// Fence settings (M41b, spec 2026-10-11-m41b-settings-design §1–2, fence-settings-v4): This fence | All fences, the rows
/// built from <see cref="SettingsCatalog"/> with a badge each (own or follows), a live preview of the fence over the
/// wallpaper, an explainer, undo and recipes. Every change goes to the host at once; the host applies, saves and calls
/// <see cref="Show"/> again.
/// </summary>
public partial class FenceSettingsWindow : Window
{
    /// <summary>A setting changed: its catalog id and value; null = this fence follows All fences again.</summary>
    public event Action<string, object?>? SettingChanged;
    public event Action<bool>? ModeChanged;
    /// <summary>The fence's colour: a swatch, a custom "#RRGGBB", Windows' accent, or all null/false for none.</summary>
    public event Action<TabColor?, string?, bool>? ColourChanged;
    public event Action? CustomColourRequested;
    /// <summary>All fences: fences without a colour take the wallpaper's.</summary>
    public event Action<bool>? WallpaperColourChanged;
    public event Action<LookSetting>? MakeFollowRequested;
    public event Action? UseAllDefaultsRequested;
    public event Action? UndoRequested;
    public event Action<string>? Renamed;
    public event Action<string?>? EmojiChanged;
    public event Action? CollectRequested;
    public event Action<Recipe>? RecipeChosen;
    public event Action<string, string?>? RecipeSaved;
    public event Action<string>? RecipeDeleted;
    /// <summary>Hold to compare: true while held.</summary>
    public event Action<bool>? CompareHeld;

    private static readonly string[] EmojiChoices = ["🎮", "🕹️", "💼", "📁", "⬇️", "🎵", "🎬", "📷", "🛠️", "💻", "🌐", "📚", "⭐", "🔥", "🧪", "🗂️"];
    private static readonly FenceTheme[] Themes = [FenceTheme.Classic, FenceTheme.Aurora, FenceTheme.Hud, FenceTheme.Neon, FenceTheme.LightMica];
    private static readonly TitleStyle[] TitleStyles = [TitleStyle.Simple, TitleStyle.Clean, TitleStyle.Pill, TitleStyle.Caps];
    private static readonly ColourStyle[] ColourStyles = [ColourStyle.AccentEdge, ColourStyle.TintedGlass, ColourStyle.TitleStrip];
    private static readonly int[] TitleSizes = [12, 14, 17, 20];
    private static readonly int[] IconSizes = [32, 48, 64, 96];
    private static readonly Spacing[] Spacings = [Spacing.Compact, Spacing.Normal, Spacing.Roomy];
    private static readonly Lazy<List<string>> InstalledFonts =
        new(() => [.. Fonts.SystemFontFamilies.Select(family => family.Source).Distinct().Order(StringComparer.CurrentCultureIgnoreCase)]);

    private readonly List<Row> _rows = [];
    private readonly List<(string Name, FrameworkElement Header)> _sections = [];
    private FenceSettingsView? _view;
    private bool _updating;
    private string? _explained;
    private double _titleHeight = 30;

    /// <summary>The fence, or null for the window that edits All fences only (Settings → Change the look of all fences…).</summary>
    public string? FenceId { get; }

    private sealed record Row(string Id, Border Card, TextBlock Badge, Button UseAll, TextBlock Note, FrameworkElement Control, Action<FenceSettingsView> Update);

    public FenceSettingsWindow(string? fenceId)
    {
        FenceId = fenceId;
        InitializeComponent();
        BuildRows();
        foreach (var choice in EmojiChoices.Append(""))
        {
            var pick = new Button
            {
                Content = choice.Length == 0 ? "None" : choice, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = choice.Length == 0 ? 12 : 16,
                Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 0, 4, 4), MinWidth = 34,
            };
            System.Windows.Automation.AutomationProperties.SetName(pick, choice.Length == 0 ? "No emoji" : $"Emoji {choice}");
            pick.Click += (_, _) => EmojiBox.Text = choice;
            EmojiPicks.Children.Add(pick);
        }
        EmojiButton.Click += (_, _) => EmojiPanel.Visibility = EmojiPanel.IsVisible ? Visibility.Collapsed : Visibility.Visible;
        EmojiBox.TextChanged += (_, _) =>
        {
            var emoji = Emoji.Normalize(EmojiBox.Text);
            if ((emoji ?? "") != EmojiBox.Text) // M41b leftover: only the first emoji stays in the box
            {
                EmojiBox.Text = emoji ?? "";
                EmojiBox.CaretIndex = EmojiBox.Text.Length;
                return;
            }
            if (!_updating && _view?.Fence is { } fence && emoji != fence.Emoji) EmojiChanged?.Invoke(emoji);
        };
        NameBox.KeyDown += (_, key) =>
        {
            if (key.Key != Key.Enter) return;
            key.Handled = true;
            CommitName();
        };
        NameBox.LostKeyboardFocus += (_, _) => CommitName();
        ThisFenceMode.Click += (_, _) => { ThisFenceMode.IsChecked = !(_view?.AllFences ?? false); ModeChanged?.Invoke(false); };
        AllFencesMode.Click += (_, _) => { AllFencesMode.IsChecked = _view?.AllFences ?? true; ModeChanged?.Invoke(true); };
        UndoButton.Click += (_, _) => UndoRequested?.Invoke();
        DefaultsButton.Click += (_, _) => UseAllDefaultsRequested?.Invoke();
        DoneButton.Click += (_, _) => Close();
        CompareButton.PreviewMouseLeftButtonDown += (_, _) => CompareHeld?.Invoke(true);
        CompareButton.PreviewMouseLeftButtonUp += (_, _) => CompareHeld?.Invoke(false);
        CompareButton.MouseLeave += (_, _) => { if (Mouse.LeftButton == MouseButtonState.Pressed) CompareHeld?.Invoke(false); };
        SaveRecipeButton.Click += (_, _) =>
        {
            SavePanel.Visibility = Visibility.Visible;
            SaveProblem.Visibility = Visibility.Collapsed;
            RecipeNameBox.Text = "";
            RecipeEmojiBox.Text = _view?.Fence?.Emoji ?? "";
            RecipeNameBox.Focus();
        };
        SaveCancelButton.Click += (_, _) => SavePanel.Visibility = Visibility.Collapsed;
        SaveConfirmButton.Click += (_, _) => SaveRecipe();
        RecipeNameBox.TextChanged += (_, _) => ShowNameProblem(onlyWhenTyped: true);
        RecipeNameBox.KeyDown += (_, key) =>
        {
            if (key.Key != Key.Enter) return;
            key.Handled = true;
            SaveRecipe();
        };
        RowsScroller.ScrollChanged += (_, _) => LightSectionTab();
        PreviewKeyDown += (_, key) =>
        {
            if (key.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && key.OriginalSource is not TextBox)
            {
                key.Handled = true;
                UndoRequested?.Invoke();
            }
            else if (key.Key == Key.Escape)
            {
                key.Handled = true;
                Close();
            }
        };
    }

    /// <summary>Shows the fence (or All fences) as it is now, after every change the host made.</summary>
    public void Show(FenceSettingsView view)
    {
        if (_view == view) return;
        _updating = true;
        try
        {
            _view = view;
            var fence = view.Fence;
            Title = view.AllFences ? "All fences — fence settings" : $"{fence?.Title} — fence settings";
            ThisFenceMode.IsChecked = !view.AllFences;
            AllFencesMode.IsChecked = view.AllFences;
            ThisFenceMode.IsEnabled = fence is not null;
            NameBox.IsEnabled = EmojiButton.IsEnabled = !view.AllFences && fence is not null;
            if (!NameBox.IsKeyboardFocusWithin) NameBox.Text = view.AllFences || fence is null ? "All fences" : fence.Title;
            EmojiButton.Content = view.AllFences || fence?.Emoji is null ? "🙂" : fence.Emoji;
            if (fence is not null && Emoji.Normalize(EmojiBox.Text) != fence.Emoji) EmojiBox.Text = fence.Emoji ?? "";
            if (view.AllFences) EmojiPanel.Visibility = Visibility.Collapsed;
            UndoButton.IsEnabled = view.UndoCount > 0;
            UndoButton.ToolTip = view.UndoCount > 0 ? "Undo the last change in this window (Ctrl+Z)" : "Nothing to undo";
            DefaultsButton.Visibility = view.AllFences ? Visibility.Collapsed : Visibility.Visible;
            DefaultsButton.IsEnabled = fence is not null && Enum.GetValues<LookSetting>().Any(setting => AllFences.HasOwn(fence, setting));
            SaveRecipeButton.IsEnabled = !view.AllFences && fence is not null;
            SummaryText.Text = Summary(view);
            foreach (var row in _rows) row.Update(view);
            ShowRecipes(view);
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>
    /// The preview (spec §2): the fence window's own drawing over the wallpaper picture behind it (a relative crop of the
    /// monitor's picture), or a plain background with a note.
    /// </summary>
    public void SetPreview(Visual? fenceVisual, double width, double height, double titleHeight, ImageSource? wallpaper, Rect crop, string? note)
    {
        PreviewCanvas.Width = Math.Max(width, 40);
        PreviewCanvas.Height = Math.Max(height, 30);
        _titleHeight = titleHeight;
        PreviewFence.Fill = fenceVisual is null ? null : new VisualBrush(fenceVisual) { Stretch = Stretch.Fill };
        PreviewWallpaper.Fill = wallpaper is null
            ? new LinearGradientBrush(Color.FromRgb(0x2A, 0x2F, 0x3D), Color.FromRgb(0x12, 0x14, 0x1A), 45)
            : new ImageBrush(wallpaper) { Viewbox = crop, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Stretch = Stretch.Fill };
        WallpaperNote.Text = note ?? "Your wallpaper";
        if (_explained is { } id) Highlight(id);
    }

    /// <summary>Settings' search (spec §3): brings a setting into view and flashes it.</summary>
    public void FocusSetting(string id)
    {
        if (_rows.FirstOrDefault(row => row.Id == id) is not { } row) return;
        row.Card.BringIntoView();
        Explain(id);
        var flash = new System.Windows.Media.Animation.DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(260)) { AutoReverse = true, RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(2) };
        row.Card.BeginAnimation(OpacityProperty, flash);
    }

    // ---------- rows ----------

    private void BuildRows()
    {
        Section("Look");
        var themes = Cards(Themes.Select(theme => (object)theme).ToArray(), theme => ThemeSample((FenceTheme)theme), value => Change("fence.theme", value));
        AddRow("fence.theme", themes.Panel, view =>
        {
            var (own, all) = (view.Fence?.Look?.Theme, view.Config.Settings.Appearance.Theme);
            themes.Select(Shown(view, own, all));
            themes.Refresh(theme => ThemeSample((FenceTheme)theme));
        });
        var swatches = ColourRow();
        AddRow("fence.accent", swatches.Panel, swatches.Update);
        var strengthLabel = new TextBlock { Style = (Style)FindResource("Description"), MinWidth = 42, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var strength = new Slider { Minimum = 0, Maximum = AppearanceSettings.MaxStrength, TickFrequency = 5, IsSnapToTickEnabled = true, SmallChange = 5, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(strength, "Transparency");
        strength.ValueChanged += (_, _) =>
        {
            strengthLabel.Text = $"{(int)strength.Value} %";
            if (!_updating) Change("fence.transparency", (int)Math.Round(strength.Value));
        };
        var strengthPanel = new DockPanel();
        strengthPanel.Children.Add(Docked(new TextBlock { Text = "See-through", Style = (Style)FindResource("Description"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }, Dock.Left));
        strengthPanel.Children.Add(Docked(strengthLabel, Dock.Right));
        strengthPanel.Children.Add(Docked(new TextBlock { Text = "Solid", Style = (Style)FindResource("Description"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) }, Dock.Right));
        strengthPanel.Children.Add(strength);
        AddRow("fence.transparency", strengthPanel, view =>
        {
            var all = view.Config.Settings.Appearance;
            strength.Value = (view.AllFences ? null : view.Fence?.Look?.Strength) ?? (LightTone(view) ? all.StrengthLight : all.StrengthDark);
            strengthLabel.Text = $"{(int)strength.Value} %";
        });
        var colourStyle = Segmented(["Accent edge", "Tinted glass", "Title strip"], index => Change("fence.colourStyle", ColourStyles[index]));
        AddRow("fence.colourStyle", colourStyle.Panel, view =>
        {
            colourStyle.Select(Array.IndexOf(ColourStyles, Shown(view, view.Fence?.Look?.ColourStyle, view.Config.Settings.Appearance.ColourStyle)));
            Visible("fence.colourStyle", ShownTheme(view) == FenceTheme.Classic); // card 4: only Classic shows a colour style
        });

        Section("Title");
        var titleStyles = Cards(TitleStyles.Select(style => (object)style).ToArray(), style => TitleSample((TitleStyle)style), value => Change("fence.titleStyle", value));
        AddRow("fence.titleStyle", titleStyles.Panel, view => titleStyles.Select(Shown(view, view.Fence?.Look?.TitleStyle, view.Config.Settings.Appearance.TitleStyle)));
        var family = new ComboBox { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left, ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel))) };
        System.Windows.Automation.AutomationProperties.SetName(family, "Title font");
        family.SelectionChanged += (_, _) => { if (!_updating && family.SelectedItem is string chosen) ChangeFont(chosen, null, null); };
        var size = Segmented(["Small", "Normal", "Large", "Huge"], index => ChangeFont(null, TitleSizes[index], null));
        var weight = Segmented(["Regular", "Semibold", "Bold"], index => ChangeFont(null, null, (TitleWeight)index));
        var weightNote = new TextBlock { Style = (Style)FindResource("Description") };
        var fontPanel = new StackPanel { Children = { family, Spaced(size.Panel), Spaced(weight.Panel), weightNote } };
        AddRow("fence.titleFont", fontPanel, view =>
        {
            var all = view.Config.Settings.Appearance.TitleFont;
            var own = view.AllFences ? null : view.Fence?.Look?.TitleFont;
            var fonts = InstalledFonts.Value;
            var shownFamily = own?.Family ?? all.Family ?? "Segoe UI";
            if (!fonts.Contains(shownFamily)) fonts = [.. fonts.Append(shownFamily).Order(StringComparer.CurrentCultureIgnoreCase)];
            if (family.ItemsSource is not List<string> listed || !listed.SequenceEqual(fonts)) family.ItemsSource = fonts;
            family.SelectedItem = shownFamily;
            size.Select(Array.IndexOf(TitleSizes, own?.Size ?? all.Size ?? 14));
            var caps = ShownTitleStyle(view) == TitleStyle.Caps;
            weight.Select(caps ? 2 : (int)(own?.Weight ?? all.Weight ?? TitleWeight.SemiBold));
            weight.Panel.IsEnabled = !caps;
            weightNote.Text = caps ? "Caps sets the weight (bold)." : "";
        });
        var align = Segmented(["Left", "Centre", "Right"], index => Change("fence.titleAlign", (TitleAlign)index));
        var alignNote = new TextBlock { Style = (Style)FindResource("Description") };
        AddRow("fence.titleAlign", new StackPanel { Children = { align.Panel, alignNote } }, view =>
        {
            var style = ShownTitleStyle(view);
            var decided = style is TitleStyle.Pill or TitleStyle.Caps;
            align.Select(decided ? 0 : (int)Shown(view, view.Fence?.Look?.TitleAlign, view.Config.Settings.Appearance.TitleAlign));
            align.Panel.IsEnabled = !decided;
            alignNote.Text = decided ? $"{style} sets this (left)." : "";
        });
        var titleBar = Switch("Show the title bar", show => Change("fence.titleBar", show));
        AddRow("fence.titleBar", titleBar, view =>
        {
            var own = view.AllFences ? null : view.Fence?.Look?.TitleOnHover;
            titleBar.IsChecked = !(own ?? view.Config.Settings.Appearance.TitleOnHover);
        });

        Section("Items");
        var layout = Segmented(["Grid", "Free"], index => Change("fence.layout", index == 0 ? FenceLayout.Flow : FenceLayout.Free));
        AddRow("fence.layout", layout.Panel, view =>
        {
            layout.Select(view.Fence?.Layout == FenceLayout.Free ? 1 : 0);
            Visible("fence.layout", view.Fence is null || view.Fence.Kind == FenceKind.Items);
        }, onlyFence: true);
        var iconSize = Segmented(["Small", "Medium", "Large", "Extra large"], index => Change("fence.iconSize", IconSizes[index]));
        AddRow("fence.iconSize", iconSize.Panel, view => iconSize.Select(Array.IndexOf(IconSizes, Shown(view, view.Fence?.IconSize, view.Config.Settings.Appearance.IconSize))));
        var labels = Segmented(["Always", "On hover"], index => Change("fence.labels", index == 0 ? LabelMode.Always : LabelMode.OnHover));
        AddRow("fence.labels", labels.Panel, view => labels.Select(Shown(view, view.Fence?.Labels, view.Config.Settings.Appearance.Labels) == LabelMode.Always ? 0 : 1));
        var spacing = Segmented(["Compact", "Normal", "Roomy"], index => Change("fence.spacing", Spacings[index]));
        var spacingNote = new TextBlock { Style = (Style)FindResource("Description") };
        AddRow("fence.spacing", new StackPanel { Children = { spacing.Panel, spacingNote } }, view =>
        {
            spacing.Select(Array.IndexOf(Spacings, Shown(view, view.Fence?.Look?.Spacing, view.Config.Settings.Appearance.Spacing)));
            var free = !view.AllFences && view.Fence?.Layout == FenceLayout.Free;
            spacing.Panel.IsEnabled = !free;
            spacingNote.Text = free ? "Only for the Grid layout." : "";
        });
        var floating = Switch("Widgets float", on => Change("fence.widgetsFloat", on));
        AddRow("fence.widgetsFloat", floating, view =>
        {
            floating.IsChecked = view.Fence?.WidgetsFloat == true;
            Visible("fence.widgetsFloat", !view.AllFences && view.OnlyWidgets);
        }, onlyFence: true);

        Section("Behaviour");
        var openWith = Segmented(["One click", "Double click"], index => Change("fence.openWith", index == 0 ? OpenWith.OneClick : OpenWith.DoubleClick));
        AddRow("fence.openWith", openWith.Panel, view => openWith.Select(Shown(view, view.Fence?.OpenWith, view.Config.Settings.OpenItemsWith) == OpenWith.OneClick ? 0 : 1));
        var rollUp = Segmented(["When the mouse rests on it", "On a title click"], index => Change("fence.rollUp", index == 0 ? RollupExpand.Hover : RollupExpand.Click));
        AddRow("fence.rollUp", rollUp.Panel, view => rollUp.Select(view.Config.Settings.RollupExpand == RollupExpand.Hover ? 0 : 1), allFencesOnly: true);
        var locked = Switch("Lock position", on => Change("fence.lock", on));
        AddRow("fence.lock", locked, view => locked.IsChecked = view.Fence?.Locked == true, onlyFence: true);
        var collectSummary = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var collect = new Button { Content = "Set up…", Margin = new Thickness(12, 0, 0, 0) };
        collect.Click += (_, _) => CollectRequested?.Invoke();
        var collectPanel = new DockPanel { Children = { Docked(collect, Dock.Right), collectSummary } };
        AddRow("fence.collect", collectPanel, view =>
        {
            var rules = view.Fence?.Collect.Count ?? 0;
            collectSummary.Text = rules == 0 ? "No rules." : rules == 1 ? "1 rule." : $"{rules} rules.";
            Visible("fence.collect", view.Fence is null || view.Fence.Kind == FenceKind.Items);
        }, onlyFence: true);

        foreach (var (name, header) in _sections)
        {
            var tab = new ToggleButton { Content = name, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0) };
            tab.Click += (_, _) =>
            {
                header.BringIntoView(new Rect(0, 0, 1, RowsScroller.ViewportHeight)); // the section's header at the top
                LightSectionTab();
            };
            SectionTabs.Children.Add(tab);
        }
    }

    private void Section(string name)
    {
        var header = new TextBlock { Text = name, Style = (Style)FindResource("SectionHeader") };
        Rows.Children.Add(header);
        _sections.Add((name, header));
    }

    private void AddRow(string id, FrameworkElement control, Action<FenceSettingsView> update, bool onlyFence = false, bool allFencesOnly = false)
    {
        var info = SettingsCatalog.Find(id);
        var badge = new TextBlock { FontSize = 11, Margin = new Thickness(8, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var useAll = new Button { Content = "Use all fences'", Padding = new Thickness(8, 1, 8, 1), FontSize = 11, Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed };
        var note = new TextBlock { Style = (Style)FindResource("Description"), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        var setting = SettingEdits.LookSettingOf(id);
        useAll.Click += (_, _) => Change(id, null);
        var heading = new DockPanel();
        heading.Children.Add(Docked(useAll, Dock.Right));
        heading.Children.Add(Docked(new TextBlock { Text = info.Label, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }, Dock.Left));
        heading.Children.Add(badge);
        var body = new StackPanel { Children = { heading, new TextBlock { Text = info.What, Style = (Style)FindResource("Description"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) }, control, note } };
        var card = new Border { Style = (Style)FindResource("Card"), Child = body, Tag = id };
        card.MouseEnter += (_, _) => Explain(id);
        card.GotKeyboardFocus += (_, _) => Explain(id);
        Rows.Children.Add(card);
        _rows.Add(new Row(id, card, badge, useAll, note, control, view =>
        {
            update(view);
            var own = setting is { } look && !view.AllFences && view.Fence is { } fence && AllFences.HasOwn(fence, look);
            control.IsEnabled = !(onlyFence && view.AllFences);
            if (allFencesOnly)
            {
                Badge(badge, own: false, "All fences");
                useAll.Visibility = Visibility.Collapsed;
                note.Visibility = Visibility.Collapsed;
            }
            else if (onlyFence)
            {
                badge.Text = "";
                useAll.Visibility = Visibility.Collapsed;
                note.Text = "Set on each fence.";
                note.Visibility = view.AllFences ? Visibility.Visible : Visibility.Collapsed;
            }
            else if (view.AllFences)
            {
                badge.Text = "";
                useAll.Visibility = Visibility.Collapsed;
                var count = setting is { } counted ? AllFences.OwnCount(view.Config, counted) : 0;
                note.Inlines.Clear();
                if (count > 0)
                {
                    note.Inlines.Add(new System.Windows.Documents.Run(count == 1 ? "1 fence has its own — " : $"{count} fences have their own — "));
                    var follow = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Make them follow"));
                    follow.Click += (_, _) => MakeFollowRequested?.Invoke(setting!.Value);
                    note.Inlines.Add(follow);
                }
                note.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            else if (setting is null) // the colour: only the fence's (its "None" is no colour)
            {
                badge.Text = "";
                useAll.Visibility = Visibility.Collapsed;
                note.Visibility = Visibility.Collapsed;
            }
            else
            {
                Badge(badge, own, own ? "This fence" : "All fences");
                useAll.Visibility = own ? Visibility.Visible : Visibility.Collapsed;
                note.Visibility = Visibility.Collapsed;
            }
        }));
    }

    private static void Badge(TextBlock badge, bool own, string text)
    {
        badge.Text = text;
        badge.SetResourceReference(TextBlock.ForegroundProperty, own ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
        badge.FontWeight = own ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void Visible(string id, bool visible)
    {
        if (_rows.FirstOrDefault(row => row.Id == id) is { } row) row.Card.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- controls ----------

    private sealed record Choice(Panel Panel, Action<int> Select);

    private sealed record CardChoice(Panel Panel, Action<object?> Select, Action<Func<object, UIElement>> Refresh);

    private Choice Segmented(string[] names, Action<int> chosen)
    {
        var panel = new WrapPanel();
        var buttons = new List<ToggleButton>();
        var selected = -1;
        for (var index = 0; index < names.Length; index++)
        {
            var at = index;
            var button = new ToggleButton { Content = names[index], Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 4, 4) };
            button.Click += (_, _) =>
            {
                button.IsChecked = at == selected; // the host's answer decides which one is lit
                if (!_updating && at != selected) chosen(at);
            };
            buttons.Add(button);
            panel.Children.Add(button);
        }
        return new Choice(panel, index =>
        {
            selected = index;
            for (var at = 0; at < buttons.Count; at++) buttons[at].IsChecked = at == index;
        });
    }

    private CardChoice Cards(object[] values, Func<object, UIElement> sample, Action<object> chosen)
    {
        var panel = new WrapPanel();
        var buttons = new List<ToggleButton>();
        object? selected = null;
        foreach (var value in values)
        {
            var button = new ToggleButton { Content = sample(value), Padding = new Thickness(4), Margin = new Thickness(0, 0, 6, 6), Tag = value };
            System.Windows.Automation.AutomationProperties.SetName(button, ChoiceName(value));
            button.Click += (_, _) =>
            {
                button.IsChecked = Equals(value, selected);
                if (!_updating && !Equals(value, selected)) chosen(value);
            };
            buttons.Add(button);
            panel.Children.Add(button);
        }
        return new CardChoice(panel, value =>
        {
            selected = value;
            foreach (var button in buttons) button.IsChecked = Equals(button.Tag, value);
        }, rebuild =>
        {
            foreach (var button in buttons) button.Content = rebuild(button.Tag!);
        });
    }

    private static string ChoiceName(object value) => value switch
    {
        FenceTheme theme => Recipes.ThemeName(theme),
        _ => value.ToString() ?? "",
    };

    private CheckBox Switch(string name, Action<bool> changed)
    {
        var box = new CheckBox { Style = (Style)FindResource("Switch"), HorizontalAlignment = HorizontalAlignment.Left };
        System.Windows.Automation.AutomationProperties.SetName(box, name);
        box.Checked += (_, _) => { if (!_updating) changed(true); };
        box.Unchecked += (_, _) => { if (!_updating) changed(false); };
        return box;
    }

    /// <summary>Accent colour (card 6): None, Windows accent (follows), the eight swatches, Custom…; All fences: None or the wallpaper's.</summary>
    private (Panel Panel, Action<FenceSettingsView> Update) ColourRow()
    {
        var panel = new WrapPanel();
        var buttons = new List<(ToggleButton Button, Func<Fence, bool> IsOn)>();
        void Add(string name, Brush fill, Func<Fence, bool> isOn, Action click, string? text = null)
        {
            var dot = new Grid { Width = 26, Height = 26 };
            dot.Children.Add(new Ellipse { Fill = fill, Stroke = new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80)), StrokeThickness = 1 });
            if (text is not null) dot.Children.Add(new TextBlock { Text = text, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            var button = new ToggleButton { Content = dot, Padding = new Thickness(3), Margin = new Thickness(0, 0, 4, 4), ToolTip = name };
            System.Windows.Automation.AutomationProperties.SetName(button, name);
            button.Click += (_, _) =>
            {
                button.IsChecked = _view?.Fence is { } fence && isOn(fence);
                if (!_updating) click();
            };
            buttons.Add((button, isOn));
            panel.Children.Add(button);
        }
        Add("None", Brushes.Transparent, fence => fence.TabColor is null && fence.CustomColor is null && !fence.WindowsAccent, () => ColourChanged?.Invoke(null, null, false), "—");
        Add("Windows accent (follows Windows)", new SolidColorBrush(SystemColors.AccentColor), fence => fence.WindowsAccent, () => ColourChanged?.Invoke(null, null, true), "W");
        foreach (var (swatch, colour) in FenceLook.Swatches)
            Add(swatch.ToString(), new SolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B)), fence => fence.TabColor == swatch && fence.CustomColor is null, () => ColourChanged?.Invoke(swatch, null, false));
        Add("Custom colour…", new LinearGradientBrush(Colors.OrangeRed, Colors.DodgerBlue, 45), fence => fence.CustomColor is not null, () => CustomColourRequested?.Invoke(), "…");
        var wallpaper = Segmented(["None", "From the wallpaper"], index => WallpaperColourChanged?.Invoke(index == 1));
        var both = new StackPanel { Children = { panel, wallpaper.Panel } };
        return (both, view =>
        {
            panel.Visibility = view.AllFences ? Visibility.Collapsed : Visibility.Visible;
            wallpaper.Panel.Visibility = view.AllFences ? Visibility.Visible : Visibility.Collapsed;
            wallpaper.Select(view.Config.Settings.Appearance.WallpaperAccent ? 1 : 0);
            foreach (var (button, isOn) in buttons) button.IsChecked = view.Fence is { } fence && isOn(fence);
        });
    }

    private UIElement ThemeSample(FenceTheme theme)
    {
        var appearance = _view?.Config.Settings.Appearance ?? new AppearanceSettings();
        var sample = (_view?.Fence ?? Fence.Create("Fence")) with { Look = new OwnLook { Theme = theme } };
        var style = FenceLook.Resolve(appearance, sample, _view?.LightTheme ?? false, null);
        var accent = style.Accent ?? Argb(SystemColors.AccentColor);
        var card = new Border
        {
            Width = 76, Height = 50, CornerRadius = new CornerRadius(theme == FenceTheme.Hud ? 2 : 6), BorderThickness = new Thickness(theme is FenceTheme.Neon or FenceTheme.Hud ? 1.5 : 1),
            Background = new SolidColorBrush(Color.FromArgb((byte)Math.Max((int)style.Veil.A, 0x99), style.Veil.R, style.Veil.G, style.Veil.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(style.Border.A, style.Border.R, style.Border.G, style.Border.B)),
        };
        var title = new TextBlock { Text = theme == FenceTheme.Hud ? "GAMES" : "Games", FontSize = 10, Margin = new Thickness(6, 4, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(style.TitleText.R, style.TitleText.G, style.TitleText.B)) };
        if (theme == FenceTheme.Hud) title.FontFamily = new FontFamily(FenceLook.HudFont + ", Consolas");
        var inner = new Grid { Children = { title } };
        if (theme == FenceTheme.Aurora)
            inner.Children.Insert(0, new Ellipse { Width = 40, Height = 26, Opacity = 0.45, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Fill = new RadialGradientBrush(Color.FromArgb(0xC0, accent.R, accent.G, accent.B), Colors.Transparent) });
        card.Child = inner;
        return new StackPanel { Children = { card, new TextBlock { Text = Recipes.ThemeName(theme), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) } } };
    }

    private static UIElement TitleSample(TitleStyle style)
    {
        var text = new TextBlock { Text = style == TitleStyle.Caps ? "GAMES" : "Games", FontSize = 12, FontWeight = style == TitleStyle.Caps ? FontWeights.Bold : FontWeights.SemiBold };
        UIElement shown = style switch
        {
            TitleStyle.Clean => new StackPanel { Children = { text, new Border { Height = 2, Width = 26, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0),
                Background = new SolidColorBrush(SystemColors.AccentColor) } } },
            TitleStyle.Pill => new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 1), Background = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)),
                Child = new TextBlock { Text = "🎮 Games 12", FontSize = 11, FontFamily = new FontFamily("Segoe UI, Segoe UI Emoji") } },
            _ => text,
        };
        return new StackPanel { Width = 84, Children = { new Border { Height = 36, Child = shown, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 6, 4, 0) },
            new TextBlock { Text = style.ToString(), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center } } };
    }

    // ---------- values ----------

    /// <summary>The value the row shows: All fences' in All fences mode, else the fence's own or All fences'.</summary>
    private static T Shown<T>(FenceSettingsView view, T? own, T all) where T : struct => view.AllFences ? all : own ?? all;

    private static FenceTheme ShownTheme(FenceSettingsView view) => Shown(view, view.Fence?.Look?.Theme, view.Config.Settings.Appearance.Theme);

    private static TitleStyle ShownTitleStyle(FenceSettingsView view) => Shown(view, view.Fence?.Look?.TitleStyle, view.Config.Settings.Appearance.TitleStyle);

    private static bool LightTone(FenceSettingsView view) => ShownTheme(view) switch
    {
        FenceTheme.LightMica => true,
        FenceTheme.Classic => view.LightTheme,
        _ => false,
    };

    private void Change(string id, object? value)
    {
        if (_updating || _view is null) return;
        SettingChanged?.Invoke(id, value);
    }

    private void ChangeFont(string? family, int? size, TitleWeight? weight)
    {
        if (_updating || _view is null) return;
        var own = _view.AllFences ? null : _view.Fence?.Look?.TitleFont;
        Change("fence.titleFont", new TitleFont(family ?? own?.Family, size ?? own?.Size, weight ?? own?.Weight));
    }

    private void CommitName()
    {
        if (_updating || _view is not { AllFences: false, Fence: { } fence }) return;
        var name = NameBox.Text.Trim();
        if (name.Length > 0 && name != fence.Title) Renamed?.Invoke(name);
        else NameBox.Text = fence.Title;
    }

    private static string Summary(FenceSettingsView view)
    {
        var all = view.Config.Settings.Appearance;
        if (view.AllFences || view.Fence is not { } fence)
        {
            var following = view.Config.Fences.Count(candidate => !Enum.GetValues<LookSetting>().Any(setting => AllFences.HasOwn(candidate, setting)));
            return $"All fences: {Recipes.ThemeName(all.Theme)} · {all.TitleStyle} titles · open with {(view.Config.Settings.OpenItemsWith == OpenWith.OneClick ? "one click" : "a double click")} · " +
                   $"{following} of {view.Config.Fences.Count} fences follow everything here.";
        }
        var own = Enum.GetValues<LookSetting>().Count(setting => AllFences.HasOwn(fence, setting));
        var openWith = fence.OpenWith ?? view.Config.Settings.OpenItemsWith;
        return $"{fence.Title}: {view.ItemCount} {(view.ItemCount == 1 ? "item" : "items")} · {Recipes.ThemeName(fence.Look?.Theme ?? all.Theme)} · opens with " +
               $"{(openWith == OpenWith.OneClick ? "one click" : "a double click")} · " +
               (own == 0 ? "everything follows All fences." : $"{own} own {(own == 1 ? "setting" : "settings")}, the rest follow All fences.");
    }

    // ---------- explainer, preview, recipes ----------

    private void Explain(string id)
    {
        _explained = id;
        var info = SettingsCatalog.Find(id);
        ExplainHeading.Text = info.Label;
        ExplainWhat.Text = info.What;
        ExplainDefault.Text = $"Default: {info.Default}";
        ExplainTip.Text = info.Tip.Length == 0 ? "" : $"Tip: {info.Tip}";
        Highlight(id);
    }

    /// <summary>Lights the part of the preview a setting changes: the title bar, the items, or the whole fence.</summary>
    private void Highlight(string id)
    {
        var height = PreviewCanvas.Height;
        var title = Math.Min(_titleHeight, height);
        (Thickness Margin, bool Show) part = id switch
        {
            "fence.titleStyle" or "fence.titleFont" or "fence.titleAlign" or "fence.titleBar" => (new Thickness(0, 0, 0, height - title), true),
            "fence.iconSize" or "fence.labels" or "fence.spacing" or "fence.layout" or "fence.widgetsFloat" => (new Thickness(0, title, 0, 0), true),
            "fence.theme" or "fence.accent" or "fence.transparency" or "fence.colourStyle" => (new Thickness(0), true),
            _ => (new Thickness(0), false),
        };
        PreviewHighlight.Margin = part.Margin;
        PreviewHighlight.Visibility = part.Show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LightSectionTab()
    {
        var current = 0;
        for (var index = 0; index < _sections.Count; index++)
        {
            if (!_sections[index].Header.IsVisible) continue;
            var top = _sections[index].Header.TransformToAncestor(RowsScroller).Transform(new Point(0, 0)).Y;
            if (top <= 40) current = index;
        }
        for (var index = 0; index < SectionTabs.Children.Count; index++) ((ToggleButton)SectionTabs.Children[index]).IsChecked = index == current;
    }

    private void ShowRecipes(FenceSettingsView view)
    {
        RecipeCards.Children.Clear();
        var matching = view.Fence is { } fence && !view.AllFences ? Recipes.Matching(view.Config, fence) : null;
        foreach (var recipe in Recipes.All(view.Config))
        {
            var card = new ToggleButton
            {
                Content = $"{recipe.Emoji ?? "◆"} {recipe.Name}", IsChecked = recipe.Name == matching, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 4),
                FontFamily = new FontFamily("Segoe UI, Segoe UI Emoji"), ToolTip = string.Join("\n", Recipes.Changes(recipe)),
            };
            System.Windows.Automation.AutomationProperties.SetName(card, $"Recipe {recipe.Name}");
            card.Click += (_, _) =>
            {
                card.IsChecked = recipe.Name == matching;
                RecipeChosen?.Invoke(recipe);
            };
            if (!Recipes.BuiltIn.Contains(recipe))
            {
                var delete = new MenuItem { Header = $"Delete “{recipe.Name}”" };
                delete.Click += (_, _) => RecipeDeleted?.Invoke(recipe.Name);
                card.ContextMenu = new ContextMenu { Items = { delete } };
            }
            RecipeCards.Children.Add(card);
        }
    }

    private void SaveRecipe()
    {
        if (!ShowNameProblem(onlyWhenTyped: false)) return;
        SavePanel.Visibility = Visibility.Collapsed;
        RecipeSaved?.Invoke(RecipeNameBox.Text, Emoji.Normalize(RecipeEmojiBox.Text));
    }

    /// <returns>True when the name can be saved.</returns>
    private bool ShowNameProblem(bool onlyWhenTyped)
    {
        var problem = Recipes.NameProblem(RecipeNameBox.Text);
        if (onlyWhenTyped && RecipeNameBox.Text.Length == 0) problem = null;
        SaveProblem.Text = problem ?? "";
        SaveProblem.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
        return Recipes.NameProblem(RecipeNameBox.Text) is null;
    }

    // ---------- small helpers ----------

    private static UIElement Docked(UIElement element, Dock dock)
    {
        DockPanel.SetDock(element, dock);
        return element;
    }

    private static FrameworkElement Spaced(FrameworkElement element)
    {
        element.Margin = new Thickness(0, 6, 0, 0);
        return element;
    }

    private static NeoFences.Core.Appearance.Argb Argb(Color colour) => new(colour.A, colour.R, colour.G, colour.B);
}
