using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NeoFences.Core.Model;

namespace NeoFences.App;

/// <summary>What the settings window shows; the host builds it from the config and the run state.</summary>
/// <param name="PeekHotkey">As a person reads it (key caps).</param>
/// <param name="PeekHotkeyActive">False when Windows refused it (another app owns it): the window says so.</param>
public sealed record SettingsView(
    bool StartWithWindows, bool HideDesktopIcons, string PeekHotkey, bool PeekHotkeyActive, RollupExpand RollupExpand,
    bool GameModeEnabled, bool GameModeActive, string Version, string DataFolder, LabelMode DefaultLabels, bool ShowShortcutArrows,
    IReadOnlyList<NeoFences.Core.Config.SnapshotEntry> Snapshots, LibraryView Library, AppearanceView Appearance, UpdatesView Updates,
    bool QuickHideGesture = true, bool DrawGesture = true, string? Banner = null);

/// <summary>One row of the Snapshots list (M10).</summary>
public sealed record SnapshotRow(string Path, string Name, string When)
{
    public string Spoken => $"{Name}, {When}";
}

/// <summary>
/// The settings window (spec §6, M6b): General, Fences, Game mode, About and logs. Every change is reported to the host
/// at once; the host applies, saves and calls <see cref="Show(SettingsView)"/> back with the result.
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
    /// <summary>A new Peek hotkey was pressed in the box (text like "Ctrl+Alt+P"); answer with <see cref="ShowHotkeyResult"/>.</summary>
    public event Action<string>? PeekHotkeyChosen;
    /// <summary>Settings → Updates (M17).</summary>
    public event Action<bool>? AutoUpdateChanged;
    public event Action? CheckForUpdatesRequested;
    public event Action? RestartToUpdateRequested;
    /// <summary>The keyboard layout changed while Settings is open (M16): the hotkey label shows the new layout's characters.</summary>
    public event Action? KeyboardLayoutChanged;
    public event Action<RollupExpand>? RollupExpandChanged;
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
    public event Action<LabelMode>? DefaultLabelsChanged;
    public event Action<LabelMode>? LabelsAppliedToAll;
    public event Action<bool>? ShortcutArrowsChanged;

    public SettingsWindow()
    {
        InitializeComponent();
        BuildSections(); // M35
        // Checked/Unchecked, not Click: UI Automation (Narrator, Toggle) changes the box without a click (M6b smoke).
        OnToggled(StartupBox, isChecked => StartWithWindowsChanged?.Invoke(isChecked));
        OnToggled(HideIconsBox, isChecked => HideDesktopIconsChanged?.Invoke(isChecked));
        OnToggled(QuickHideGestureBox, isChecked => QuickHideGestureChanged?.Invoke(isChecked)); // M33
        OnToggled(DrawGestureBox, isChecked => DrawGestureChanged?.Invoke(isChecked));
        BannerLogsButton.Click += (_, _) => OpenLogsRequested?.Invoke();
        OnToggled(GameModeBox, isChecked => GameModeChanged?.Invoke(isChecked));
        RollupBox.SelectionChanged += (_, _) =>
        {
            if (!_updating && RollupBox.SelectedItem is ComboBoxItem { Tag: string mode }) RollupExpandChanged?.Invoke(Enum.Parse<RollupExpand>(mode));
        };
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
        LabelsBox.SelectionChanged += (_, _) => { if (!_updating) DefaultLabelsChanged?.Invoke(SelectedLabels); };
        LabelsApplyAllButton.Click += (_, _) => LabelsAppliedToAll?.Invoke(SelectedLabels);
        // Screen readers read each setting's description with it (M6b review carry-over).
        foreach (var (control, description) in new (UIElement, TextBlock)[]
                 { (StartupBox, StartupDescription), (HideIconsBox, HideIconsDescription), (QuickHideGestureBox, QuickHideGestureDescription), (DrawGestureBox, DrawGestureDescription), (HotkeyBox, HotkeyDescription),
                   (LabelsBox, LabelsDescription), (ArrowsBox, ArrowsDescription), (RollupBox, RollupDescription), (GameModeBox, GameModeDescription) })
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

    private LabelMode SelectedLabels => LabelsBox.SelectedIndex == 1 ? LabelMode.OnHover : LabelMode.Always;

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
    public void ShowSnapshotsCard() => ShowPage("Snapshots"); // M35: its own page

    /// <summary>
    /// The section list (M35, spec §2): General, Fences, Appearance, Games, Game mode, Snapshots, Updates, About — the open one
    /// marked, only its page shown; the arrow keys move through the list, Tab goes into the page.
    /// </summary>
    private (string Name, string Glyph, FrameworkElement Page)[] Sections =>
    [
        ("General", MenuGlyph.Settings, GeneralPage), ("Fences", MenuGlyph.NewFence, FencesPage), ("Appearance", MenuGlyph.Colour, AppearancePage),
        ("Games", MenuGlyph.Games, GamesPage), ("Game mode", MenuGlyph.GameMode, GameModePage), ("Snapshots", MenuGlyph.Snapshot, SnapshotsPage),
        ("Updates", MenuGlyph.Refresh, UpdatesPage), ("About", MenuGlyph.Properties, AboutPage),
    ];

    private void BuildSections()
    {
        foreach (var (name, glyph, _) in Sections)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 15,
                Width = 22, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            var item = new ListBoxItem { Content = row, Tag = name, Padding = new Thickness(10, 7, 10, 7) };
            System.Windows.Automation.AutomationProperties.SetName(item, name);
            SectionList.Items.Add(item);
        }
        SectionList.SelectionChanged += (_, _) =>
        {
            if (SectionList.SelectedItem is not ListBoxItem { Tag: string name }) return;
            foreach (var (section, _, page) in Sections) page.Visibility = section == name ? Visibility.Visible : Visibility.Collapsed;
            PageTitle.Text = name;
            PageScroller.ScrollToTop();
        };
        SectionList.SelectedIndex = 0;
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
        LabelsBox.SelectedIndex = view.DefaultLabels == LabelMode.OnHover ? 1 : 0;
        ArrowsBox.IsChecked = view.ShowShortcutArrows;
        if (!view.PeekHotkeyActive && !HotkeyBox.IsKeyboardFocused)
        {
            ShowHotkeyResult(saved: false, message: $"{view.PeekHotkey} is not active: Windows or another app owns it. Record another combination.");
        }
        StartupBox.IsChecked = view.StartWithWindows;
        HideIconsBox.IsChecked = view.HideDesktopIcons;
        QuickHideGestureBox.IsChecked = view.QuickHideGesture; // M33
        DrawGestureBox.IsChecked = view.DrawGesture;
        BannerText.Text = view.Banner ?? "";
        Banner.Visibility = view.Banner is null ? Visibility.Collapsed : Visibility.Visible;
        HotkeyBox.Text = view.PeekHotkey;
        RollupBox.SelectedIndex = view.RollupExpand == RollupExpand.Click ? 1 : 0;
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
