using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NeoFences.Core.Model;

namespace NeoFences.App;

/// <summary>What the settings window shows; the host builds it from the config and the run state.</summary>
/// <param name="PeekHotkey">As a person reads it (key caps).</param>
/// <param name="PeekHotkeyActive">False when Windows refused it (another app owns it): the window says so.</param>
public sealed record SettingsView(
    bool StartWithWindows, bool HideDesktopIcons, string PeekHotkey, bool PeekHotkeyActive,
    bool GameModeEnabled, bool GameModeActive, string Version, string DataFolder, bool ShowShortcutArrows,
    IReadOnlyList<NeoFences.Core.Config.SnapshotEntry> Snapshots, LibraryView Library, AppearanceView Appearance, UpdatesView Updates,
    bool QuickHideGesture = true, bool DrawGesture = true, string? Banner = null,
    bool RevealGesture = false, FenceMenuStyle FenceMenu = FenceMenuStyle.Guided);

/// <summary>One row of the Snapshots list (M10).</summary>
public sealed record SnapshotRow(string Path, string Name, string When)
{
    public string Spoken => $"{Name}, {When}";
}

/// <summary>
/// The settings window (M41b, spec 2026-10-11-m41b-settings-design §3): a search box over seven pages — Look &amp; feel,
/// Desktop, Games, Peek &amp; keys, Snapshots &amp; safety, Updates &amp; privacy, Help &amp; about; every row says what it does
/// (<see cref="SettingsCatalog"/>). A fence's look and clicks live in Fence settings, which the search opens too. Every change
/// is reported to the host at once; the host applies, saves and calls <see cref="Show(SettingsView)"/> back with the result.
/// </summary>
public partial class SettingsWindow : Window
{
    private bool _updating; // filling the controls from the host must not report changes back

    public event Action<bool>? StartWithWindowsChanged;
    /// <summary>"Hide desktop icons while NeoFences runs" (M18).</summary>
    public event Action<bool>? HideDesktopIconsChanged;
    /// <summary>Settings → the desktop gesture switches (M33).</summary>
    public event Action<bool>? QuickHideGestureChanged;
    public event Action<bool>? DrawGestureChanged;
    /// <summary>M41a: "Click wallpaper to reveal desktop", "Menu style".</summary>
    public event Action<bool>? RevealGestureChanged;
    public event Action<FenceMenuStyle>? FenceMenuChanged;
    /// <summary>M41b: Look &amp; feel → "Change the look of all fences…" (Fence settings in All fences mode).</summary>
    public event Action? ChangeAllFencesLookRequested;
    /// <summary>M41b: a search result that lives in Fence settings: its catalog id.</summary>
    public event Action<string>? FenceSettingRequested;
    /// <summary>M41b: Updates &amp; privacy → Privacy (PRIVACY next to NeoFences.exe).</summary>
    public event Action? PrivacyRequested;
    /// <summary>A new Peek hotkey was pressed in the box (text like "Ctrl+Alt+P"); answer with <see cref="ShowHotkeyResult"/>.</summary>
    public event Action<string>? PeekHotkeyChosen;
    /// <summary>Settings → Updates (M17).</summary>
    public event Action<bool>? AutoUpdateChanged;
    public event Action? CheckForUpdatesRequested;
    public event Action? RestartToUpdateRequested;
    /// <summary>The keyboard layout changed while Settings is open (M16): the hotkey label shows the new layout's characters.</summary>
    public event Action? KeyboardLayoutChanged;
    public event Action<bool>? GameModeChanged;
    public event Action? OpenLogsRequested;
    public event Action? OpenDataRequested;
    /// <summary>"Help (online guide)" (M29): the guide on GitHub.</summary>
    public event Action? HelpRequested;
    /// <summary>"Licence and notices" (M39): LICENSE.txt next to NeoFences.exe.</summary>
    public event Action? LicenceRequested;
    public event Action? TakeSnapshotRequested;
    public event Action<string>? RestoreSnapshotRequested;
    public event Action<string, string>? RenameSnapshotRequested;
    public event Action<string>? DeleteSnapshotRequested;
    public event Action? OpenSnapshotsRequested;
    /// <summary>M36: Settings → Snapshots → Export setup… / Import setup…; Settings → About → Reset settings to defaults….</summary>
    public event Action? ExportSetupRequested;
    public event Action? ImportSetupRequested;
    public event Action? ResetSettingsRequested;
    /// <summary>The hotkey box got (true) or lost (false) the keyboard: the host releases the Peek hotkey meanwhile (M6b review).</summary>
    public event Action<bool>? HotkeyRecording;
    public event Action<bool>? ShortcutArrowsChanged;

    private Action<int>? _selectFenceMenu;

    public SettingsWindow()
    {
        InitializeComponent();
        BuildSections(); // M35
        // Checked/Unchecked, not Click: UI Automation (Narrator, Toggle) changes the box without a click (M6b smoke).
        OnToggled(StartupBox, isChecked => StartWithWindowsChanged?.Invoke(isChecked));
        OnToggled(HideIconsBox, isChecked => HideDesktopIconsChanged?.Invoke(isChecked));
        OnToggled(QuickHideGestureBox, isChecked => QuickHideGestureChanged?.Invoke(isChecked)); // M33
        OnToggled(DrawGestureBox, isChecked => DrawGestureChanged?.Invoke(isChecked));
        OnToggled(RevealGestureBox, isChecked => RevealGestureChanged?.Invoke(isChecked)); // M41a
        _selectFenceMenu = Segmented(FenceMenuChoice, ["Guided", "Compact"], index => FenceMenuChanged?.Invoke(index == 1 ? FenceMenuStyle.Compact : FenceMenuStyle.Guided));
        ChangeAllLookButton.Click += (_, _) => ChangeAllFencesLookRequested?.Invoke(); // M41b
        PrivacyButton.Click += (_, _) => PrivacyRequested?.Invoke();
        SearchBox.TextChanged += (_, _) =>
        {
            SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            ShowSearch();
        };
        SearchBox.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter && SearchResults.Children.OfType<Button>().FirstOrDefault() is { } first)
            {
                key.Handled = true;
                first.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }
            else if (key.Key == Key.Escape && SearchBox.Text.Length > 0)
            {
                key.Handled = true;
                SearchBox.Text = "";
            }
        };
        QuickHidePicture.Content = GesturePicture(clicks: 2, rightDrag: false);
        RevealPicture.Content = GesturePicture(clicks: 1, rightDrag: false);
        DrawPicture.Content = GesturePicture(clicks: 0, rightDrag: true);
        BannerLogsButton.Click += (_, _) => OpenLogsRequested?.Invoke();
        OnToggled(GameModeBox, isChecked => GameModeChanged?.Invoke(isChecked));
        HotkeyBox.PreviewKeyDown += OnHotkeyKeyDown;
        HotkeyBox.GotKeyboardFocus += (_, _) =>
        {
            HotkeyRecording?.Invoke(true); // so pressing the current combination is recorded, not Peek
            ShowHotkeyHint("Press the new combination… (Esc keeps the current one)");
        };
        HotkeyBox.LostKeyboardFocus += (_, _) =>
        {
            HotkeyRecording?.Invoke(false);
            if (HotkeyStatus.Tag is null) HotkeyStatus.Visibility = Visibility.Collapsed;
        };
        OnToggled(ArrowsBox, isChecked => ShortcutArrowsChanged?.Invoke(isChecked));
        // M41b: each row's line is the catalog's (the same words as Fence settings' explainer and the guide).
        foreach (var (description, id) in new (TextBlock, string)[]
                 { (StartupDescription, "desktop.startWithWindows"), (HideIconsDescription, "desktop.hideIcons"), (QuickHideGestureDescription, "desktop.quickHide"),
                   (DrawGestureDescription, "desktop.draw"), (RevealGestureDescription, "desktop.reveal"), (ArrowsDescription, "desktop.shortcutArrows"),
                   (GameModeDescription, "games.gameMode"), (FenceMenuDescription, "look.menu"), (MotionDescription, "look.motion"), (NamesDescription, "look.names") })
        {
            var info = SettingsCatalog.Find(id);
            description.Text = info.Tip.Length == 0 ? info.What : $"{info.What} {info.Tip}";
        }
        // Screen readers read each setting's description with it (M6b review carry-over).
        foreach (var (control, description) in new (UIElement, TextBlock)[]
                 { (StartupBox, StartupDescription), (HideIconsBox, HideIconsDescription), (QuickHideGestureBox, QuickHideGestureDescription), (DrawGestureBox, DrawGestureDescription), (RevealGestureBox, RevealGestureDescription), (HotkeyBox, HotkeyDescription),
                   (ArrowsBox, ArrowsDescription), (GameModeBox, GameModeDescription) })
        {
            System.Windows.Automation.AutomationProperties.SetHelpText(control, description.Text);
        }
        OpenLogsButton.Click += (_, _) => OpenLogsRequested?.Invoke();
        OpenDataButton.Click += (_, _) => OpenDataRequested?.Invoke();
        HelpButton.Click += (_, _) => HelpRequested?.Invoke();
        LicenceButton.Click += (_, _) => LicenceRequested?.Invoke();
        TakeSnapshotButton.Click += (_, _) => TakeSnapshotRequested?.Invoke();
        OpenSnapshotsButton.Click += (_, _) => OpenSnapshotsRequested?.Invoke();
        ExportSetupButton.Click += (_, _) => ExportSetupRequested?.Invoke(); // M36
        ImportSetupButton.Click += (_, _) => ImportSetupRequested?.Invoke();
        ResetSettingsButton.Click += (_, _) => ResetSettingsRequested?.Invoke();
        RestoreSnapshotButton.Click += (_, _) => { if (SelectedSnapshot is { } row) RestoreSnapshotRequested?.Invoke(row.Path); };
        DeleteSnapshotButton.Click += (_, _) => { if (SelectedSnapshot is { } row) DeleteSnapshotRequested?.Invoke(row.Path); };
        RenameSnapshotButton.Click += (_, _) => BeginSnapshotRename();
        SnapshotList.SelectionChanged += (_, _) => UpdateSnapshotButtons();
        // Only a double-click on a row restores; the scrollbar or the empty space below the rows do nothing (M13c).
        SnapshotList.MouseDoubleClick += (_, click) =>
        {
            if (click.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(SnapshotList, source) is ListBoxItem
                && SelectedSnapshot is { } row) RestoreSnapshotRequested?.Invoke(row.Path);
        };
        SnapshotNameBox.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter) EndSnapshotRename(commit: true, backToRow: true);
            else if (key.Key == Key.Escape) EndSnapshotRename(commit: false, backToRow: true);
            else return;
            key.Handled = true;
        };
        SnapshotNameBox.LostKeyboardFocus += (_, _) => EndSnapshotRename(commit: true, backToRow: false); // the user went elsewhere: focus stays there
        System.Windows.Automation.AutomationProperties.SetHelpText(SnapshotList, SnapshotsDescription.Text);
        InitializeLibrary();
        InitializeAppearance();
        OnToggled(AutoUpdateBox, isChecked => AutoUpdateChanged?.Invoke(isChecked)); // M17
        CheckUpdatesButton.Click += (_, _) => CheckForUpdatesRequested?.Invoke();
        RestartToUpdateButton.Click += (_, _) => RestartToUpdateRequested?.Invoke();
        // WM_INPUTLANGCHANGE (0x0051): every layout switch, also between two layouts of one language (US → US-International),
        // which WPF's InputLanguageChanged does not report (final review M2). The hook dies with the window.
        SourceInitialized += (_, _) => System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle)
            .AddHook((nint _, int message, nint _, nint _, ref bool _) =>
            {
                if (message == 0x0051) KeyboardLayoutChanged?.Invoke();
                return 0;
            });
    }

    private void OnToggled(CheckBox box, Action<bool> report)
    {
        box.Checked += (_, _) => { if (!_updating) report(true); };
        box.Unchecked += (_, _) => { if (!_updating) report(false); };
    }


    private SnapshotRow? SelectedSnapshot => SnapshotList.SelectedItem as SnapshotRow;
    private string? _renamingPath;

    private void UpdateSnapshotButtons()
    {
        var selected = SelectedSnapshot is not null;
        RestoreSnapshotButton.IsEnabled = selected;
        RenameSnapshotButton.IsEnabled = selected;
        DeleteSnapshotButton.IsEnabled = selected;
    }

    private void BeginSnapshotRename()
    {
        if (SelectedSnapshot is not { } row) return;
        _renamingPath = row.Path;
        SnapshotNameBox.Text = row.Name;
        SnapshotNameBox.Visibility = Visibility.Visible;
        SnapshotNameBox.Focus();
        SnapshotNameBox.SelectAll();
    }

    /// <param name="backToRow">Enter or Esc: the keyboard goes back to the row, not nowhere (M13c). Never after a click elsewhere.</param>
    private void EndSnapshotRename(bool commit, bool backToRow)
    {
        if (_renamingPath is not { } path) return;
        _renamingPath = null;
        SnapshotNameBox.Visibility = Visibility.Collapsed;
        if (backToRow) _focusSnapshotPath = path;
        if (commit && SnapshotNameBox.Text.Trim() is { Length: > 0 } name) RenameSnapshotRequested?.Invoke(path, name);
        else FocusSnapshotRow();
    }

    private string? _focusSnapshotPath;

    /// <summary>Puts the keyboard on the row of <see cref="_focusSnapshotPath"/> once the list is laid out.</summary>
    private void FocusSnapshotRow()
    {
        if (_focusSnapshotPath is not { } path) return;
        _focusSnapshotPath = null;
        Dispatcher.BeginInvoke(() =>
        {
            if (SnapshotList.ItemsSource is IEnumerable<SnapshotRow> rows && rows.FirstOrDefault(row => row.Path == path) is { } row
                && SnapshotList.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem item) item.Focus();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>A snapshot action's result, in the card (M13c): Settings-started actions say what happened where the user looks.</summary>
    public void ShowSnapshotNotice(string message, bool failed)
    {
        SnapshotsStatus.Text = message;
        SnapshotsStatus.Foreground = failed ? System.Windows.Media.Brushes.IndianRed : SecondaryText;
        SnapshotsStatus.Visibility = Visibility.Visible;
        System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(SnapshotsStatus)
            .RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }

    /// <summary>Tray → Restore snapshot → "More in Settings…": the Snapshots card in view (M13c).</summary>
    public void ShowSnapshotsCard() => ShowPage(SnapshotsPageName); // M35: its own page

    private const string SnapshotsPageName = "Snapshots & safety";

    /// <summary>
    /// The pages (M41b, global-settings-v2): coloured tiles, the open one marked, only its page shown; the arrow keys move
    /// through the list, Tab goes into the page.
    /// </summary>
    private (string Name, string Emoji, string Colour, string Subtitle, SettingsPlace Place, FrameworkElement Page)[] Sections =>
    [
        ("Look & feel", "🎨", "#FF8A5CF6", "How everything moves and reads. Each fence's look is in Fence settings.", SettingsPlace.LookAndFeel, LookPage),
        ("Desktop", "🖥", "#FF3B82F6", "What NeoFences does with your desktop and its icons.", SettingsPlace.Desktop, DesktopPage),
        ("Games", "🎮", "#FF22C55E", "Finding your games, their covers, and staying out of the way while you play.", SettingsPlace.Games, GamesPage),
        ("Peek & keys", "⌨", "#FFF59E0B", "Your fences above all windows, and the keys that work in them.", SettingsPlace.PeekAndKeys, KeysPage),
        (SnapshotsPageName, "🛟", "#FFEF4444", "Ways back: snapshots, your whole setup in one file, a reset.", SettingsPlace.Snapshots, SnapshotsPage),
        ("Updates & privacy", "🔄", "#FF06B6D4", "Staying up to date, and what leaves your PC.", SettingsPlace.Updates, UpdatesPage),
        ("Help & about", "ℹ", "#FF64748B", "The guide, the logs, the licence.", SettingsPlace.Help, AboutPage),
    ];

    private void BuildSections()
    {
        foreach (var (name, emoji, colour, _, _, _) in Sections)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(colour)!,
                Child = new TextBlock { Text = emoji, FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            var item = new ListBoxItem { Content = row, Tag = name, Padding = new Thickness(10, 6, 10, 6) };
            System.Windows.Automation.AutomationProperties.SetName(item, name);
            SectionList.Items.Add(item);
        }
        SectionList.SelectionChanged += (_, _) =>
        {
            if (SectionList.SelectedItem is not ListBoxItem { Tag: string name }) return;
            if (SearchBox.Text.Length > 0) SearchBox.Text = ""; // a page click leaves the search
            ShowSelectedPage();
        };
        SectionList.SelectedIndex = 0;
    }

    private void ShowSelectedPage()
    {
        if (SectionList.SelectedItem is not ListBoxItem { Tag: string name }) return;
        SearchPage.Visibility = Visibility.Collapsed;
        foreach (var (section, _, _, subtitle, _, page) in Sections)
        {
            page.Visibility = section == name ? Visibility.Visible : Visibility.Collapsed;
            if (section == name) PageSubtitle.Text = subtitle;
        }
        PageTitle.Text = name;
        PageScroller.ScrollToTop();
    }

    /// <summary>The search (M41b, spec §3): every setting whose words match, here and in Fence settings; a click goes there.</summary>
    private void ShowSearch()
    {
        var query = SearchBox.Text;
        if (string.IsNullOrWhiteSpace(query))
        {
            ShowSelectedPage();
            return;
        }
        foreach (var (_, _, _, _, _, page) in Sections) page.Visibility = Visibility.Collapsed;
        SearchPage.Visibility = Visibility.Visible;
        PageTitle.Text = "Search";
        PageSubtitle.Text = $"Settings that match “{query.Trim()}”.";
        SearchResults.Children.Clear();
        var found = SettingsCatalog.Search(query);
        SearchEmpty.Visibility = found.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var setting in found)
        {
            var where = SettingsCatalog.InFenceSettings(setting.Place)
                ? $"Fence settings → {setting.Place.ToString()[5..]} (all fences, or one fence)"
                : $"Settings → {Sections.First(section => section.Place == setting.Place).Name}";
            var result = new Button
            {
                HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = setting.Label, FontWeight = FontWeights.SemiBold },
                        new TextBlock { Text = where, Style = (Style)FindResource("Description") },
                        new TextBlock { Text = setting.What, Style = (Style)FindResource("Description"), TextWrapping = TextWrapping.Wrap },
                    },
                },
            };
            System.Windows.Automation.AutomationProperties.SetName(result, $"{setting.Label}, {where}");
            result.Click += (_, _) => GoToSetting(setting);
            SearchResults.Children.Add(result);
        }
    }

    private void GoToSetting(SettingInfo setting)
    {
        if (SettingsCatalog.InFenceSettings(setting.Place))
        {
            FenceSettingRequested?.Invoke(setting.Id);
            return;
        }
        var page = Sections.First(section => section.Place == setting.Place);
        SearchBox.Text = "";
        ShowPage(page.Name);
        Dispatcher.BeginInvoke(() =>
        {
            if (FindCard(page.Page, setting.Id) is not { } card) return;
            card.BringIntoView();
            var flash = new System.Windows.Media.Animation.DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(260)) { AutoReverse = true, RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(2) };
            card.BeginAnimation(OpacityProperty, flash);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static FrameworkElement? FindCard(DependencyObject root, string id)
    {
        if (root is FrameworkElement { Tag: string tag } element && tag == id) return element;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (FindCard(child, id) is { } found) return found;
        }
        return null;
    }

    /// <summary>A small drawn picture of a desktop gesture (spec §3): a wallpaper, a pointer and ×1 / ×2, or a dashed right-drag box.</summary>
    private static UIElement GesturePicture(int clicks, bool rightDrag)
    {
        var canvas = new System.Windows.Controls.Canvas { Width = 64, Height = 44 };
        canvas.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Width = 64, Height = 44, RadiusX = 5, RadiusY = 5,
            Fill = new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Color.FromRgb(0x3B, 0x5B, 0x9A), System.Windows.Media.Color.FromRgb(0x1E, 0x2B, 0x4A), 60),
        });
        if (rightDrag)
        {
            var box = new System.Windows.Shapes.Rectangle { Width = 30, Height = 18, Stroke = System.Windows.Media.Brushes.White, StrokeThickness = 1.2, StrokeDashArray = [3, 2] };
            System.Windows.Controls.Canvas.SetLeft(box, 10);
            System.Windows.Controls.Canvas.SetTop(box, 10);
            canvas.Children.Add(box);
        }
        var pointer = new TextBlock { Text = "➤", Foreground = System.Windows.Media.Brushes.White, FontSize = 14, RenderTransform = new System.Windows.Media.RotateTransform(-120) };
        System.Windows.Controls.Canvas.SetLeft(pointer, rightDrag ? 46 : 30);
        System.Windows.Controls.Canvas.SetTop(pointer, rightDrag ? 40 : 34);
        canvas.Children.Add(pointer);
        var label = new TextBlock { Text = rightDrag ? "right" : $"×{clicks}", Foreground = System.Windows.Media.Brushes.White, FontSize = 10, FontWeight = FontWeights.SemiBold };
        System.Windows.Controls.Canvas.SetLeft(label, 6);
        System.Windows.Controls.Canvas.SetTop(label, 30);
        canvas.Children.Add(label);
        return canvas;
    }

    /// <summary>Segmented buttons (M41b): the host's answer decides which one is lit.</summary>
    private Action<int> Segmented(Panel host, string[] names, Action<int> chosen)
    {
        var buttons = new List<System.Windows.Controls.Primitives.ToggleButton>();
        var selected = -1;
        for (var index = 0; index < names.Length; index++)
        {
            var at = index;
            var button = new System.Windows.Controls.Primitives.ToggleButton { Content = names[index], Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 4, 0) };
            button.Click += (_, _) =>
            {
                button.IsChecked = at == selected;
                if (!_updating && at != selected) chosen(at);
            };
            buttons.Add(button);
            host.Children.Add(button);
        }
        return index =>
        {
            selected = index;
            for (var at = 0; at < buttons.Count; at++) buttons[at].IsChecked = at == index;
        };
    }

    /// <summary>Opens a section by name ("Snapshots", "Games", …).</summary>
    public void ShowPage(string name) =>
        SectionList.SelectedItem = SectionList.Items.OfType<ListBoxItem>().FirstOrDefault(item => Equals(item.Tag, name)) ?? SectionList.SelectedItem;

    /// <summary>The snapshot list, keeping the selection where the same file is still listed.</summary>
    private void ShowSnapshots(IReadOnlyList<NeoFences.Core.Config.SnapshotEntry> snapshots)
    {
        var selectedPath = SelectedSnapshot?.Path;
        var rows = snapshots.Select(entry => new SnapshotRow(entry.Path, entry.Name, entry.TakenAt.ToLocalTime().ToString("d MMM yyyy, HH:mm"))).ToList();
        if (SnapshotList.ItemsSource is IEnumerable<SnapshotRow> shown && shown.SequenceEqual(rows))
        {
            FocusSnapshotRow(); // a refresh with nothing new keeps the list; a pending focus is used now, never left for later (final review I1)
            return;
        }
        var hadFocus = SnapshotList.IsKeyboardFocusWithin;
        SnapshotList.ItemsSource = rows;
        SnapshotList.SelectedItem = rows.FirstOrDefault(row => row.Path == selectedPath);
        UpdateSnapshotButtons();
        if (hadFocus && _focusSnapshotPath is null && SelectedSnapshot is { } selected) _focusSnapshotPath = selected.Path;
        FocusSnapshotRow();
    }

    public void Show(SettingsView view)
    {
        ShowSnapshots(view.Snapshots);
        _updating = true;
        ArrowsBox.IsChecked = view.ShowShortcutArrows;
        if (!view.PeekHotkeyActive && !HotkeyBox.IsKeyboardFocused)
        {
            ShowHotkeyResult(saved: false, message: $"{view.PeekHotkey} is not active: Windows or another app owns it. Record another combination.");
        }
        StartupBox.IsChecked = view.StartWithWindows;
        HideIconsBox.IsChecked = view.HideDesktopIcons;
        QuickHideGestureBox.IsChecked = view.QuickHideGesture; // M33
        DrawGestureBox.IsChecked = view.DrawGesture;
        RevealGestureBox.IsChecked = view.RevealGesture; // M41a
        _selectFenceMenu?.Invoke(view.FenceMenu == FenceMenuStyle.Compact ? 1 : 0);
        BannerText.Text = view.Banner ?? "";
        Banner.Visibility = view.Banner is null ? Visibility.Collapsed : Visibility.Visible;
        HotkeyBox.Text = view.PeekHotkey;
        GameModeBox.IsChecked = view.GameModeEnabled;
        ShowLibrary(view.Library);
        ShowAppearance(view.Appearance);
        AutoUpdateBox.IsChecked = view.Updates.AutoUpdate; // M17
        AutoUpdateBox.IsEnabled = view.Updates.Available;
        CheckUpdatesButton.IsEnabled = view.Updates.Available;
        UpdateStatus.Text = view.Updates.Status;
        RestartToUpdateButton.Visibility = view.Updates.ReadyVersion is null ? Visibility.Collapsed : Visibility.Visible;
        RestartToUpdateButton.Content = view.Updates.ReadyVersion is { } ready ? $"Restart to update to v{ready}" : "";
        GameModeStatus.Text = !view.GameModeEnabled ? "Off: NeoFences stays fully active during games."
            : view.GameModeActive ? "Right now: idle, a full-screen app is in front." : "Right now: active (no full-screen app in front).";
        VersionText.Text = $"NeoFences {view.Version}";
        DataFolderText.Text = $"Settings, backups and logs: {view.DataFolder}";
        _updating = false;
    }

    /// <summary>The host's answer to <see cref="PeekHotkeyChosen"/>: saved, or why not (invalid, or taken by another app).</summary>
    public void ShowHotkeyResult(bool saved, string message)
    {
        var changed = HotkeyStatus.Text != message || HotkeyStatus.Visibility != Visibility.Visible;
        HotkeyStatus.Tag = saved ? null : "error"; // an error stays visible after the box loses focus
        HotkeyStatus.Text = message;
        HotkeyStatus.Foreground = saved ? SecondaryText : System.Windows.Media.Brushes.IndianRed;
        HotkeyStatus.Visibility = Visibility.Visible;
        if (changed) AnnounceHotkeyStatus(); // Settings refreshes often (game mode): say a warning once
    }

    private void AnnounceHotkeyStatus() =>
        System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(HotkeyStatus)
            .RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);

    /// <summary>Fluent's secondary text colour (the grey a missing theme resource falls back to).</summary>
    private System.Windows.Media.Brush SecondaryText => TryFindResource("TextFillColorSecondaryBrush") as System.Windows.Media.Brush ?? SystemColors.GrayTextBrush;

    private void ShowHotkeyHint(string hint)
    {
        HotkeyStatus.Tag = null;
        HotkeyStatus.Text = hint;
        HotkeyStatus.Foreground = SecondaryText;
        HotkeyStatus.Visibility = Visibility.Visible;
    }

    /// <summary>Records a combination: modifiers alone only preview; Esc leaves the box; the first other key decides.</summary>
    private void OnHotkeyKeyDown(object sender, KeyEventArgs pressed)
    {
        var key = pressed.Key == Key.System ? pressed.SystemKey : pressed.Key; // Alt combinations arrive as Key.System
        // Tab / Shift+Tab move on and Alt+F4 closes, as everywhere: never trap them in the box (M6b review I1).
        if (key == Key.Tab && (Keyboard.Modifiers & ~ModifierKeys.Shift) == ModifierKeys.None) return;
        if (key == Key.F4 && Keyboard.Modifiers == ModifierKeys.Alt) return;
        pressed.Handled = true;
        key = key switch { Key.ImeProcessed => pressed.ImeProcessedKey, Key.DeadCharProcessed => pressed.DeadCharProcessedKey, _ => key };
        if (key == Key.Escape)
        {
            Keyboard.ClearFocus();
            return;
        }
        var modifiers = Keyboard.Modifiers;
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            ShowHotkeyHint(string.Join("+", parts.Append("…")));
            return;
        }
        parts.Add(key.ToString());
        PeekHotkeyChosen?.Invoke(string.Join("+", parts));
    }
}
