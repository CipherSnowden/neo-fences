using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Shell;
using NeoFences.Core.Appearance;
using NeoFences.Core.Config;
using NeoFences.Core.Items;
using NeoFences.Core.Layouts;
using NeoFences.Core.Model;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>One fence on the desktop. Placement and persistence are the host's job; this window reports what the user did.</summary>
public partial class FenceWindow : Window
{
    private const int WmWindowPosChanging = 0x0046;
    private const int WmSizing = 0x0214;
    private const int WmMoving = 0x0216;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const int WmNcLeftButtonDoubleClick = 0x00A3;
    private const int WmNcRightButtonUp = 0x00A5;
    private const int WmDeviceChange = 0x0219;
    private const int DbtDeviceArrival = 0x8000, DbtDeviceRemoveComplete = 0x8004;
    private const int HitTestCaption = 2;
    private const double CornerRadiusDips = 8;
    private double _captionHeightDips = 30; // follows the title font (M14: 26 / 30 / 34 / 38)
    private const double ResizeBorderDips = 6;

    // The menu lists ConfigNormalizer.IconSizes (one list, M2c review carry-over); these are only their names.
    private static readonly Dictionary<int, string> IconSizeNames = new() { [32] = "Small", [48] = "Medium", [64] = "Large", [96] = "Extra large" };

    private readonly ObservableCollection<FenceItemView> _items = [];
    private readonly IconLoader _iconLoader;
    private ListBoxItem? _hoverLabelContainer; // the cell the pop-under name belongs to
    private int _iconSizeDips;
    private bool _renaming;
    private string _title = "";
    private FenceKind _kind; // the shown tab: items, the Game Library (M12: tiles, its own menu) or a folder view (M21: read-only)
    private const string ItemsHint = "Drop files, folders or links here — or right-click → Add item…";
    private string? _viewStatus; // a folder view's line instead of entries: not available, empty, nothing matching (M21)
    private IReadOnlyDictionary<string, (string Path, bool IsPoster)> _libraryArt = new Dictionary<string, (string, bool)>();
    private DragTracker? _drag;
    private bool _locked;
    private bool _rolledUp;
    private readonly RollUpExpansion _expansion; // rolled up, but open right now (hover or click, M5/M6b)
    private int _fullHeightPx;              // height when not rolled up (physical pixels)
    private readonly System.Windows.Threading.DispatcherTimer _hoverTimer;
    private readonly System.Windows.Threading.DispatcherTimer _heightAnimation;
    private System.Diagnostics.Stopwatch _heightClock = new();
    private int _heightFrom;
    private int _heightTo;
    private int _fadeGeneration;
    private int? _moveHeightPx; // a move that interrupted a roll-up animation keeps this height (M6b review M2)
    private static readonly TimeSpan RollUpDuration = TimeSpan.FromMilliseconds(200); // spec §6
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(150)); // spec §6

    /// <summary>The fence shown now: the box's active tab (M9). Items, icon size, labels, sort and rename act on it.</summary>
    public string FenceId { get; private set; }

    /// <summary>The fence owning this box (its host, M9): placement, move, roll-up and lock act on it.</summary>
    public string BoxId { get; set; }

    /// <summary>A tab header clicked (or hovered during a drop): show that tab.</summary>
    public event Action<string>? TabSelected;
    /// <summary>A tab header dragged and released at this screen point (physical pixels): reorder, merge or detach.</summary>
    public event Action<string, int, int>? TabDropped;
    /// <summary>A dragged tab header is over this screen point (physical pixels); int.MinValue when the drag ended.</summary>
    public event Action<int, int>? TabDragMoved;
    public event Action<TabColor?>? TabColorRequested;
    /// <summary>Fence menu → Colour → Custom… (M14): the host opens Windows' colour picker.</summary>
    public event Action? CustomColorRequested;
    public event Action? DetachTabRequested;
    /// <summary>Ctrl+Tab (+1) / Ctrl+Shift+Tab (-1).</summary>
    public event Action<int>? TabCycleRequested;

    /// <summary>The accent colours (M9), as Windows' own accent palette roughly offers them; defined in Core since M14.</summary>
    public static readonly IReadOnlyDictionary<TabColor, Color> TabColors =
        FenceLook.Swatches.ToDictionary(swatch => swatch.Key, swatch => ToColor(swatch.Value));

    private static Color ToColor(Argb colour) => Color.FromArgb(colour.A, colour.R, colour.G, colour.B);

    private static SolidColorBrush Frozen(Argb colour)
    {
        var brush = new SolidColorBrush(ToColor(colour));
        brush.Freeze();
        return brush;
    }

    private const double TabHeaderMaxWidth = 140;
    private List<Fence> _tabs = [];
    private string? _tabPressId;      // a header pressed: a click or the start of a tab drag
    private Point _tabPressPoint;
    private TabGhost? _tabGhost;      // set once the press became a drag
    private bool _lightTheme;

    public nint Handle { get; private set; }

    /// <summary>Windows refused the rounded-corner preference (Windows 10); the host logs it once.</summary>
    public bool CornersUnavailable { get; private set; }

    /// <summary>Peek (M5): while set the fence may rise above apps instead of staying at the bottom.</summary>
    public bool Peeking { get; set; }

    /// <summary>
    /// Asked before each roll-up or fade (spec §6): off when Windows' "Animation effects" are off, and in game mode
    /// (spec §4.7). Set by the host.
    /// </summary>
    public Func<bool> AnimationsAllowed { get; set; } = () => false;

    /// <summary>Asked while the user drags an edge or the title: returns where the window should go (snapping).</summary>
    public Func<PixelRect, SnapEdges, PixelRect>? SnapRect { get; set; }

    /// <summary>Raised after the user finishes moving or resizing, with the new physical-pixel rect.</summary>
    public event Action<FenceWindow, PixelRect>? MovedByUser;

    public event Action? NewFenceRequested;
    public event Action? ExitRequested;
    /// <summary>Double-click or Enter on one item (its key).</summary>
    public event Action<string>? OpenRequested;
    /// <summary>Enter with several items selected: open each.</summary>
    public event Action<IReadOnlyList<string>>? OpenManyRequested;
    public event Action<string>? RenameRequested;
    public event Action<int>? IconSizeRequested;
    public event Action<bool>? LockToggled;
    public event Action? DeleteRequested;
    /// <summary>Right-click or menu key on items: keys (the clicked item first), screen point, opened from the keyboard.</summary>
    public event Action<IReadOnlyList<string>, int, int, bool>? ItemMenuRequested;
    /// <summary>Del: take these items out of the fence (their targets are never touched, ADR-040).</summary>
    public event Action<IReadOnlyList<string>>? RemoveRequested;
    /// <summary>F2 (true: the name selected) or Alt+Enter on one item: its Properties.</summary>
    public event Action<string, bool>? PropertiesRequested;
    /// <summary>The user started dragging these items out of the fence (M3b).</summary>
    public event Action<IReadOnlyList<string>>? DragRequested;
    public event Action<FenceSort>? SortRequested;
    /// <summary>Fence menu → "Add item…" (M18).</summary>
    public event Action? AddItemRequested;
    /// <summary>Fence menu → "Add from desktop…" (M19).</summary>
    public event Action? AddFromDesktopRequested;
    /// <summary>Fence menu → "Refresh": the items' targets are checked again and their icons reloaded (the library rescans).</summary>
    public event Action? RefreshRequested;
    /// <summary>Fence menu → "New Game Library fence" (M12).</summary>
    public event Action? NewLibraryRequested;
    /// <summary>Fence menu → "Add games…" (M22).</summary>
    public event Action? AddGamesRequested;
    /// <summary>Fence menu → "New folder view…" (M21).</summary>
    public event Action? NewFolderViewRequested;
    /// <summary>A folder view's "Open folder" (its menu, or the "+ N more" line).</summary>
    public event Action? OpenFolderRequested;
    /// <summary>A folder view's "Folder view settings…".</summary>
    public event Action? ViewSettingsRequested;
    /// <summary>A drive arrived or was removed (Windows tells top-level windows): missing and unavailable items are checked again.</summary>
    public event Action? DrivesChanged;
    /// <summary>The "Start with Windows" toggle changed (ADR-019).</summary>
    public event Action<bool>? StartupToggled;
    /// <summary>"Settings…" in the fence menu (M6b).</summary>
    public event Action? SettingsRequested;
    /// <summary>Double-click on the title: roll up to the title bar, or back down (M5).</summary>
    public event Action? RollUpToggled;
    /// <summary>Fence menu → Labels (M8b): always, or only on hover / selection.</summary>
    public event Action<LabelMode>? LabelModeRequested;

    private Point? _pressPoint;                 // left button pressed on an item: a drag may start
    private ListBoxItem? _deferredSelect;       // pressed on an already selected item: select it alone only on release
    private Point? _bandStart;                  // left button pressed on empty space: rubber band
    private ListBoxItem? _deferredToggle;       // Ctrl+press on a selected item: unselect it on release unless it was dragged
    private LabelMode _labelMode = LabelMode.Always;
    private ListBoxItem? _hoveredContainer;
    private (uint At, Point Where)? _lastCaptionPress; // a title double-click recognised by NeoFences itself (M8b); message time
    private uint? _recognisedDoubleClickAt;

    public FenceWindow(Fence fence, bool lightTheme, IconLoader iconLoader, RollupExpand rollupExpand)
    {
        _expansion = new RollUpExpansion(rollupExpand);
        FenceId = fence.Id;
        BoxId = fence.Id;
        _iconLoader = iconLoader;
        InitializeComponent();
        foreach (var (sort, name) in new[] { (FenceSort.Name, "Name"), (FenceSort.Type, "Type"), (FenceSort.Date, "Date (newest first)") })
        {
            var sortItem = new MenuItem { Header = name, Tag = sort };
            sortItem.Click += (_, _) => SortRequested?.Invoke(sort);
            SortItem.Items.Add(sortItem);
        }
        var noColor = new MenuItem { Header = "None", IsCheckable = true };
        noColor.Click += (_, _) => TabColorRequested?.Invoke(null);
        TabColorItem.Items.Add(noColor);
        foreach (var (color, value) in TabColors)
        {
            var colorItem = new MenuItem
            {
                Header = color.ToString(), Tag = color, IsCheckable = true,
                Icon = new Rectangle { Width = 12, Height = 12, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(value) },
            };
            colorItem.Click += (_, _) => TabColorRequested?.Invoke(color);
            TabColorItem.Items.Add(colorItem);
        }
        BuildCustomColourMenu();
        DetachTabItem.Click += (_, _) => DetachTabRequested?.Invoke();
        TitleBar.SizeChanged += (_, _) => UpdateTabStripWidth();
        PreviewKeyDown += OnTabKeys;
        NewLibraryItem.Click += (_, _) => NewLibraryRequested?.Invoke();
        NewFolderViewItem.Click += (_, _) => NewFolderViewRequested?.Invoke();
        AddGamesItem.Click += (_, _) => AddGamesRequested?.Invoke();
        OpenFolderItem.Click += (_, _) => OpenFolderRequested?.Invoke();
        ViewSettingsItem.Click += (_, _) => ViewSettingsRequested?.Invoke();
        MoreLine.MouseLeftButtonUp += (_, click) => { click.Handled = true; OpenFolderRequested?.Invoke(); };
        AddItemItem.Click += (_, _) => AddItemRequested?.Invoke();
        AddFromDesktopItem.Click += (_, _) => AddFromDesktopRequested?.Invoke();
        RefreshItem.Click += (_, _) => RefreshRequested?.Invoke();
        foreach (var size in ConfigNormalizer.IconSizes)
        {
            var sizeItem = new MenuItem { Header = IconSizeNames.GetValueOrDefault(size, $"{size} px"), Tag = size, IsCheckable = true };
            sizeItem.Click += (_, _) => IconSizeRequested?.Invoke(size);
            IconSizeItem.Items.Add(sizeItem);
        }
        NewFenceItem.Click += (_, _) => NewFenceRequested?.Invoke();
        RenameItem.Click += (_, _) => BeginRename();
        LockItem.Click += (_, _) => LockToggled?.Invoke(LockItem.IsChecked);
        DeleteItem.Click += (_, _) => DeleteRequested?.Invoke();
        ExitItem.Click += (_, _) => ExitRequested?.Invoke();
        StartupItem.Click += (_, _) => StartupToggled?.Invoke(StartupItem.IsChecked);
        SettingsItem.Click += (_, _) => SettingsRequested?.Invoke();
        LabelsAlwaysItem.Click += (_, _) => LabelModeRequested?.Invoke(LabelMode.Always);
        LabelsOnHoverItem.Click += (_, _) => LabelModeRequested?.Invoke(LabelMode.OnHover);
        TitleBox.MaxLength = FenceEdits.MaxTitleLength; // the cut in FenceEdits.Rename never surprises the user (M2c review)
        ItemList.SelectionChanged += (_, _) => UpdateHoverLabel();
        ItemList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => UpdateHoverLabel()));
        HoverLabel.SizeChanged += (_, _) => PlaceHoverLabel(); // the bound name arrived or changed (final review I3)
        Loaded += (_, _) => ReloadIcons(); // placed on its monitor: one load at the right size (mixed DPI, M2b review)
        ItemList.ItemsSource = _items;
        ItemList.MouseDoubleClick += OnItemDoubleClick;
        ItemList.PreviewMouseLeftButtonDown += OnListPress;
        ItemList.PreviewMouseMove += OnListMove;
        ItemList.PreviewMouseLeftButtonUp += OnListRelease;
        ItemList.LostMouseCapture += (_, _) => EndBand();
        ItemList.KeyDown += OnItemListKeyDown;
        Body.ContextMenuOpening += OnBodyContextMenuOpening;
        // A locked fence has no caption area (no WM_NCRBUTTONUP): its title is client area outside Body (final review I2).
        TitleBar.MouseRightButtonUp += (_, click) => { click.Handled = true; OpenFenceMenu(); };
        TitleBox.KeyDown += OnTitleBoxKeyDown;
        TitleBox.LostKeyboardFocus += (_, _) => EndRename(commit: true);
#if DEBUG
        // Checklist B11: a hung fence UI thread must not freeze the desktop or taskbar (owner input-queue attachment, ADR-011).
        var freezeItem = new MenuItem { Header = "Debug: freeze this UI thread for 10 s (B11)" };
        freezeItem.Click += (_, _) => Thread.Sleep(TimeSpan.FromSeconds(10));
        BodyContextMenu.Items.Add(freezeItem);
#endif
        ApplyTheme(lightTheme);
        ApplyFence(fence);
        _rolledUp = fence.RolledUp;
        // Rolled up, the fence opens on hover or click (setting) and closes again shortly after the pointer leaves.
        _hoverTimer = new System.Windows.Threading.DispatcherTimer { Interval = HoverTick };
        _hoverTimer.Tick += (_, _) => OnHoverTick();
        if (_rolledUp) _hoverTimer.Start();
        _heightAnimation = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        _heightAnimation.Tick += (_, _) => StepHeight();
        Closed += (_, _) =>
        {
            _hoverTimer.Stop(); // a deleted fence must not keep ticking on its dead handle (M5 review M1)
            _heightAnimation.Stop();
        };
        SetLocked(fence.Locked);
        // Unlocked, the title is caption (WM_NCLBUTTONDBLCLK); locked, it is client area.
        TitleBar.MouseLeftButtonDown += (_, click) =>
        {
            if (click.ClickCount == 2) RollUpToggled?.Invoke();
            else if (click.ClickCount == 1) ClickToOpen();
        };
        SourceInitialized += OnSourceInitialized;
        // Icons are rendered for one DPI. DpiChanged is routed: every new item raises it too, so react only to the window's own.
        DpiChanged += (_, dpiChange) =>
        {
            if (dpiChange.OriginalSource != this || dpiChange.OldDpi.PixelsPerDip == dpiChange.NewDpi.PixelsPerDip) return;
            // A rolled-up fence keeps its full height in physical pixels: rescale it with the monitor (M5 review carry-over).
            _fullHeightPx = (int)Math.Round(_fullHeightPx * dpiChange.NewDpi.DpiScaleY / dpiChange.OldDpi.DpiScaleY);
            ReloadIcons();
            ReloadArt();
        };
    }

    public void SetStartupChecked(bool startWithWindows) => StartupItem.IsChecked = startWithWindows;

    /// <summary>Everything the window shows of one fence: title, menu state, icon size, labels.</summary>
    private void ApplyFence(Fence fence)
    {
        _title = fence.Title;
        TitleText.Text = fence.Title;
        _kind = fence.Kind;
        // The library is NeoFences' own A–Z list of games: no items to add or sort (M12). A folder view shows its folder
        // read-only: nothing to add; "Sort by" is its own, kept (M21).
        var items = _kind == FenceKind.Items;
        var view = _kind == FenceKind.View;
        AddItemItem.Visibility = items ? Visibility.Visible : Visibility.Collapsed;
        AddFromDesktopItem.Visibility = items ? Visibility.Visible : Visibility.Collapsed;
        AddGamesItem.Visibility = items ? Visibility.Visible : Visibility.Collapsed;
        SortItem.Visibility = _kind == FenceKind.Library ? Visibility.Collapsed : Visibility.Visible;
        foreach (var sortItem in SortItem.Items.OfType<MenuItem>()) sortItem.IsChecked = view && Equals(sortItem.Tag, fence.View!.Sort);
        OpenFolderItem.Visibility = view ? Visibility.Visible : Visibility.Collapsed;
        ViewSettingsItem.Visibility = view ? Visibility.Visible : Visibility.Collapsed;
        DeleteItem.Header = _kind switch
        {
            FenceKind.Library => "Delete fence (your games are not touched)",
            FenceKind.View => "Delete fence (the folder is not touched)",
            _ => "Delete fence (your files are not touched)",
        };
        if (!view) SetViewStatus(center: null, more: null);
        UpdateEmptyHint();
        _labelMode = fence.Labels;
        SetIconSize(fence.IconSize);
        SetLabelMode(fence.Labels);
    }

    /// <summary>The shown tab is the Game Library (M12).</summary>
    public bool IsLibrary => _kind == FenceKind.Library;

    /// <summary>The shown tab's kind (M21): items, the Game Library, or a folder view.</summary>
    public FenceKind Kind => _kind;

    /// <summary>
    /// A folder view's status (M21 spec §3): a line instead of entries (not available, empty, nothing matching), and a
    /// clickable "+ N more — Open folder" under them.
    /// </summary>
    public void SetViewStatus(string? center, string? more)
    {
        _viewStatus = center;
        MoreText.Text = more ?? "";
        MoreLine.Visibility = more is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateEmptyHint();
    }

    /// <summary>The library's tile art by item ref: a 2:3 poster, or a logo shown centred (M12).</summary>
    public void SetLibraryArt(IReadOnlyDictionary<string, (string Path, bool IsPoster)> art)
    {
        _libraryArt = art;
        foreach (var view in _items) ApplyArt(view);
    }

    private void ApplyArt(FenceItemView view)
    {
        view.IsTile = IsLibrary || view.Tile; // the library's tiles (M12), game items shown as covers (M22)
        (string Path, bool IsPoster)? chosen = IsLibrary ? (_libraryArt.TryGetValue(view.Key, out var libraryArt) ? libraryArt : null) : view.TileArt;
        if (!view.IsTile || chosen is not { } art)
        {
            view.ArtPath = null;
            view.Art = null;
            view.ArtKind = "None";
            return;
        }
        if (view.ArtPath == art.Path) return;
        view.ArtPath = art.Path;
        var kind = art.IsPoster ? "Poster" : "Logo";
        // Decoded off the UI thread (a cover is a few hundred KB); only the newest request is shown.
        // Decoded at the tile's pixel width, not more: 300 covers would otherwise hold ~160 MB (M13b).
        var decodeWidth = Math.Max(48, (int)Math.Round(Math.Round(_iconSizeDips * 1.5) * VisualTreeHelper.GetDpi(this).DpiScaleX));
        Task.Run(() => LoadArt(art.Path, decodeWidth)).ContinueWith(loaded =>
        {
            if (view.ArtPath != art.Path || loaded.Result is not { } image) return;
            view.Art = image;
            view.ArtKind = kind;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static System.Windows.Media.ImageSource? LoadArt(string path, int decodeWidth)
    {
        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; // the file is not kept open
            image.DecodePixelWidth = decodeWidth;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Serilog.Log.Warning(failure, "could not load game art {Path}", path); // the icon stays
            return null;
        }
    }

    /// <summary>The shown fence changed in place (a snapshot restore, M10 final review I1): title, icon size, labels, menus.</summary>
    public void Refresh(Fence fence) => ApplyFence(fence);

    /// <summary>
    /// Another tab of this box is shown (M9): its look and menus; its items follow from the host. An open rename of the
    /// previous tab is cancelled.
    /// </summary>
    public void ShowTab(Fence fence)
    {
        if (fence.Id == FenceId) return;
        EndRename(commit: false);
        FenceId = fence.Id;
        ApplyFence(fence);
        _items.Clear();
        UpdateEmptyHint();
    }

    /// <summary>The box's tabs in order and the shown one (M9). One tab: the plain title (with its colour bar, if any).</summary>
    public void SetTabs(IReadOnlyList<Fence> tabs, string activeId)
    {
        _tabs = [.. tabs];
        var many = _tabs.Count > 1;
        var active = _tabs.FirstOrDefault(tab => tab.Id == activeId) ?? _tabs[0];
        TabStrip.Children.Clear();
        if (many) foreach (var tab in _tabs) TabStrip.Children.Add(BuildTabHeader(tab, isActive: tab.Id == active.Id));
        ShowTitleOrTabs();
        DetachTabItem.Visibility = many ? Visibility.Visible : Visibility.Collapsed;
        RenameItem.Header = many ? "Rename tab" : "Rename fence";
        // The bar under a single fence's title comes from its look (ApplyStyle, M14); the menu shows the fence's choice.
        foreach (var colorItem in TabColorItem.Items.OfType<MenuItem>())
        {
            colorItem.IsChecked = active.CustomColor is not null ? Equals(colorItem.Tag, CustomColourTag) : Equals(colorItem.Tag, active.TabColor);
        }
        if (_style is { } style) ApplyStyle(style); // headers were rebuilt: their font and the bar follow the look again
        UpdateTabStripWidth();
    }

    private void ShowTitleOrTabs()
    {
        var many = _tabs.Count > 1;
        TabStrip.Visibility = _renaming ? Visibility.Hidden : many ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Visibility = _renaming ? Visibility.Hidden : many ? Visibility.Collapsed : Visibility.Visible;
    }

    private Border BuildTabHeader(Fence tab, bool isActive)
    {
        var title = new TextBlock
        {
            Text = tab.Title, FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(8, 0, 8, 2),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "FenceTitleText");
        if (_style is { } style)
        {
            title.FontFamily = new FontFamily(style.Font.Family);
            title.FontSize = style.Font.Size;
            if (isActive) title.FontWeight = ToWeight(style.Font.Weight);
        }
        var bar = new Rectangle
        {
            Height = 3, RadiusX = 1.5, RadiusY = 1.5, Margin = new Thickness(6, 0, 6, 1), VerticalAlignment = VerticalAlignment.Bottom,
            // A custom colour marks its header too (final review M3, spec §3)
            Fill = (Argb.FromHex(tab.CustomColor) ?? (tab.TabColor is { } color ? FenceLook.Swatches[color] : null)) is { } mark ? Frozen(mark) : Brushes.Transparent,
            Opacity = isActive ? 1 : 0.55,
            IsHitTestVisible = false,
        };
        var cell = new Grid();
        cell.Children.Add(title);
        cell.Children.Add(bar);
        var header = new Border { Child = cell, CornerRadius = new CornerRadius(5, 5, 0, 0), Tag = tab.Id, Background = Brushes.Transparent };
        if (isActive) header.SetResourceReference(Border.BackgroundProperty, "FenceHover");
        WindowChrome.SetIsHitTestVisibleInChrome(header, true); // the title row is caption: headers take the mouse themselves
        System.Windows.Automation.AutomationProperties.SetName(header, tab.Title);
        header.MouseLeftButtonDown += OnTabPress;
        header.MouseMove += OnTabMove;
        header.MouseLeftButtonUp += OnTabRelease;
        header.MouseRightButtonDown += OnTabRightPress;
        header.MouseRightButtonUp += OnTabRightClick;
        header.LostMouseCapture += (_, _) => CancelTabGesture();
        return header;
    }

    private void UpdateTabStripWidth()
    {
        if (_tabs.Count < 2) return;
        // Headers share the strip equally and shrink with "…"; the rest of the row stays free to move the box.
        TabStrip.Width = Math.Max(0, Math.Min(_tabs.Count * TabHeaderMaxWidth, TitleBar.ActualWidth - 40));
    }

    private void OnTabPress(object sender, MouseButtonEventArgs press)
    {
        if (sender is not Border { Tag: string tabId } header) return;
        press.Handled = true;
        if (press.ClickCount == 2)
        {
            if (tabId == FenceId) BeginRename(); // the first click already showed it
            return;
        }
        _tabPressId = tabId;
        _tabPressPoint = press.GetPosition(this);
        header.CaptureMouse();
    }

    private void OnTabMove(object sender, MouseEventArgs move)
    {
        if (_tabPressId is not { } tabId || move.LeftButton != MouseButtonState.Pressed) return;
        if (_tabGhost is null)
        {
            var moved = move.GetPosition(this) - _tabPressPoint;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance * 2
                && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance * 2) return;
            _tabGhost = new TabGhost(_tabs.FirstOrDefault(tab => tab.Id == tabId)?.Title ?? "", _lightTheme);
        }
        // Esc cancels even when another app has the keyboard (fences rarely do, ADR-015; final review I3).
        if (FenceWindowChrome.IsKeyDown(0x1B))
        {
            EndTabGesture();
            return;
        }
        var (screenX, screenY) = FenceWindowChrome.GetCursorPosition();
        _tabGhost.Follow(screenX, screenY, VisualTreeHelper.GetDpi(this).DpiScaleX);
        TabDragMoved?.Invoke(screenX, screenY); // the title row it would join lights up
    }

    private void OnTabRelease(object sender, MouseButtonEventArgs release)
    {
        if (_tabPressId is not { } tabId) return;
        release.Handled = true;
        var dragged = _tabGhost is not null;
        EndTabGesture();
        if (!dragged)
        {
            TabSelected?.Invoke(tabId);
            ClickToOpen(); // a rolled-up box in click mode opens too, as a click on its title does (M13c)
            return;
        }
        var (screenX, screenY) = FenceWindowChrome.GetCursorPosition();
        TabDropped?.Invoke(tabId, screenX, screenY);
    }

    /// <summary>A right-click during a tab drag cancels it; nothing changes.</summary>
    private void OnTabRightPress(object sender, MouseButtonEventArgs press)
    {
        if (_tabPressId is null) return;
        press.Handled = true;
        EndTabGesture();
    }

    /// <summary>Right-click on a header: that tab is shown, and the fence menu acts on it.</summary>
    private void OnTabRightClick(object sender, MouseButtonEventArgs click)
    {
        if (sender is not Border { Tag: string tabId }) return;
        click.Handled = true;
        if (tabId != FenceId) TabSelected?.Invoke(tabId);
        OpenFenceMenu();
    }

    /// <summary>The fence menu at the pointer (a header or the title right-clicked).</summary>
    private void OpenFenceMenu()
    {
        BodyContextMenu.PlacementTarget = Body;
        BodyContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        BodyContextMenu.IsOpen = true;
    }

    private void OnTabKeys(object sender, KeyEventArgs key)
    {
        if (key.Key == Key.Escape && _tabPressId is not null)
        {
            EndTabGesture();
            key.Handled = true;
        }
        else if (key.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && _tabs.Count > 1)
        {
            TabCycleRequested?.Invoke(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            key.Handled = true;
        }
    }

    private void EndTabGesture()
    {
        _tabPressId = null; // first: releasing the capture below raises LostMouseCapture
        CloseTabGhost();
        Mouse.Capture(null);
    }

    private void CancelTabGesture()
    {
        if (_tabPressId is null) return;
        _tabPressId = null;
        CloseTabGhost();
    }

    private void CloseTabGhost()
    {
        if (_tabGhost is null) return;
        _tabGhost.Close();
        _tabGhost = null;
        TabDragMoved?.Invoke(int.MinValue, int.MinValue); // no more highlight
    }

    /// <summary>The insertion slot (0..tab count) under a screen x (physical pixels), for a reorder or a merge.</summary>
    public int TabIndexAt(int screenX)
    {
        if (_tabs.Count < 2 || TabStrip.ActualWidth <= 0) return _tabs.Count;
        var headerWidth = TabStrip.ActualWidth / _tabs.Count;
        var x = TabStrip.PointFromScreen(new Point(screenX, 0)).X;
        return Math.Clamp((int)Math.Round(x / headerWidth), 0, _tabs.Count);
    }

    /// <summary>True when the screen point (physical pixels) is on this box's title row or tab strip.</summary>
    public bool TitleRowContains(int screenX, int screenY)
    {
        if (!IsVisible) return false;
        var rect = FenceWindowChrome.GetPixelRect(Handle);
        var titleHeightPx = TitleBar.ActualHeight * VisualTreeHelper.GetDpi(this).DpiScaleY;
        return screenX >= rect.X && screenX < rect.X + rect.Width && screenY >= rect.Y && screenY < rect.Y + titleHeightPx;
    }

    /// <summary>A fence or tab dragged over this title row would merge here: show it.</summary>
    public void SetMergeHighlight(bool highlighted)
    {
        if (highlighted) TitleBar.SetResourceReference(Panel.BackgroundProperty, "FenceSelected");
        else TitleBar.Background = Brushes.Transparent;
    }

    /// <summary>The tab whose header is under a screen point, if any (an item drop over a header shows that tab).</summary>
    private string? TabHeaderAt(int screenX, int screenY)
    {
        if (_tabs.Count < 2 || TabStrip.Visibility != Visibility.Visible) return null;
        var point = TabStrip.PointFromScreen(new Point(screenX, screenY));
        if (point.Y < 0 || point.Y > TabStrip.ActualHeight || point.X < 0 || point.X >= TabStrip.ActualWidth) return null;
        return _tabs[Math.Clamp((int)(point.X / (TabStrip.ActualWidth / _tabs.Count)), 0, _tabs.Count - 1)].Id;
    }

    public void SetTitle(string title)
    {
        _title = title;
        TitleText.Text = title;
    }

    /// <summary>
    /// Shows exactly these items in this order, by moving, adding and removing views in place: items already shown keep
    /// their icon, selection and the scroll position (M2b review carry-over); a changed target, name or icon reloads only
    /// that item's icon and name.
    /// </summary>
    public void SetItems(IReadOnlyList<ShownItem> shownItems)
    {
        var wanted = shownItems.Select(shown => shown.Key).ToHashSet(StringComparer.Ordinal);
        for (var index = _items.Count - 1; index >= 0; index--)
        {
            if (!wanted.Contains(_items[index].Key)) _items.RemoveAt(index);
        }
        // ponytail: O(n²) moves in the worst case (a full reorder of hundreds of items); a keyed diff when that shows up.
        var iconSizePx = IconSizePx;
        for (var index = 0; index < shownItems.Count; index++)
        {
            var shown = shownItems[index];
            var found = -1;
            for (var at = index; at < _items.Count && found < 0; at++)
            {
                if (_items[at].Key == shown.Key) found = at;
            }
            if (found >= 0)
            {
                if (found != index) _items.Move(found, index);
                if (_items[index].Update(shown) && IsLoaded) _iconLoader.Request(_items[index], iconSizePx);
                ApplyArt(_items[index]); // shown as a cover now, or an icon again (M22); the same art is not loaded twice
                continue;
            }
            var view = new FenceItemView(shown);
            ApplyArt(view);
            if (IsLoaded) _iconLoader.Request(view, iconSizePx); // before that, Loaded requests them at the right DPI (M2b review)
            _items.Insert(index, view);
        }
        UpdateEmptyHint();
        // Cells may have shifted under a shown name without a scroll or selection event (final review I3).
        Dispatcher.BeginInvoke(UpdateHoverLabel, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>An empty fence (not the library) says how to fill it (spec §5).</summary>
    private void UpdateEmptyHint()
    {
        // A folder view shows its status line in the same place (M21).
        var text = _kind == FenceKind.View ? _viewStatus : _kind == FenceKind.Items && _items.Count == 0 ? ItemsHint : null;
        EmptyHint.Text = text ?? "";
        EmptyHint.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>These items selected and scrolled into view: a drop of what the fence already holds shows where it is (spec §2).</summary>
    public void SelectItems(IReadOnlyCollection<string> keys)
    {
        ItemList.SelectedItems.Clear();
        foreach (var view in _items.Where(view => keys.Contains(view.Key))) ItemList.SelectedItems.Add(view);
        if (ItemList.SelectedItems.Count > 0) ItemList.ScrollIntoView(ItemList.SelectedItems[0]);
    }

    private ContextMenu? _itemMenu;

    /// <summary>The host's item menu (M18 spec §3), at the pointer, or at the item for the menu key.</summary>
    public void ShowItemMenu(ContextMenu menu, bool fromKeyboard)
    {
        _itemMenu = menu;
        var selected = ItemList.SelectedItem is { } item ? ItemList.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem : null;
        menu.PlacementTarget = fromKeyboard && selected is not null ? selected : ItemList;
        menu.Placement = fromKeyboard && selected is not null
            ? System.Windows.Controls.Primitives.PlacementMode.Center
            : System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private int IconSizePx => (int)Math.Round(_iconSizeDips * VisualTreeHelper.GetDpi(this).DpiScaleX);

    /// <summary>One of <see cref="ConfigNormalizer.IconSizes"/> (DIPs). Icons are reloaded at the new size.</summary>
    public void SetIconSize(int iconSizeDips)
    {
        if (iconSizeDips == _iconSizeDips) return; // nothing to reload (M2c review carry-over)
        _iconSizeDips = iconSizeDips;
        Resources["IconSize"] = (double)iconSizeDips;
        Resources["ArrowSize"] = Math.Max(12.0, Math.Round(iconSizeDips * 0.36));
        ApplyItemWidth();
        foreach (var sizeItem in IconSizeItem.Items.OfType<MenuItem>()) sizeItem.IsChecked = (int)sizeItem.Tag == iconSizeDips;
        ReloadIcons();
        ReloadArt();
    }

    /// <summary>Covers are decoded at the tile's pixel width: a new icon size or monitor DPI decodes them again (final review I3).</summary>
    private void ReloadArt()
    {
        foreach (var view in _items.Where(view => view.IsTile))
        {
            view.ArtPath = null;
            ApplyArt(view);
        }
    }

    /// <summary>Labels always, or icons only with the name popping under the hovered or selected icon (M8b, user choice).</summary>
    public void SetLabelMode(LabelMode labelMode)
    {
        _labelMode = labelMode;
        Resources["LabelVisibility"] = labelMode == LabelMode.Always ? Visibility.Visible : Visibility.Collapsed;
        Resources["ItemToolTips"] = labelMode == LabelMode.Always; // the pop-under name replaces the tooltip
        LabelsAlwaysItem.IsChecked = labelMode == LabelMode.Always;
        LabelsOnHoverItem.IsChecked = labelMode == LabelMode.OnHover;
        ApplyItemWidth();
        UpdateHoverLabel();
    }

    /// <summary>Settings → "Show shortcut arrows" (M8b, off by default).</summary>
    public void SetShortcutArrows(bool show) => Resources["ShortcutArrowVisibility"] = show ? Visibility.Visible : Visibility.Collapsed;

    private void ApplyItemWidth()
    {
        // Game Library tiles are 2:3, 1.5 × the icon size wide (M12).
        Resources["TileWidth"] = Math.Round(_iconSizeDips * 1.5);
        Resources["TileHeight"] = Math.Round(_iconSizeDips * 2.25);
        Resources["TileCellWidth"] = Math.Round(_iconSizeDips * 1.5) + 12.0; // a cover tile among icons (M22)
        // With labels: room for two short words under small icons. Icons only: a tight grid.
        Resources["ItemWidth"] = IsLibrary ? Math.Round(_iconSizeDips * 1.5) + 12.0
            : _labelMode == LabelMode.Always ? LabelledItemWidth : _iconSizeDips + 12.0;
    }

    private double LabelledItemWidth => Math.Max(76.0, _iconSizeDips + 28.0);

    private void OnItemMouseEnter(object sender, MouseEventArgs args)
    {
        _hoveredContainer = sender as ListBoxItem;
        UpdateHoverLabel();
    }

    private void OnItemMouseLeave(object sender, MouseEventArgs args)
    {
        if (ReferenceEquals(_hoveredContainer, sender)) _hoveredContainer = null;
        UpdateHoverLabel();
    }

    /// <summary>
    /// Icon-only fences: the name of the hovered item (else the one selected item) pops up under its icon, over the
    /// neighbours, kept inside the fence (above the icon when there is no room below). Drawn in the fence window itself,
    /// so it stays at the fence's place in the window stack (no popup window above other apps).
    /// </summary>
    private void UpdateHoverLabel()
    {
        var container = _labelMode != LabelMode.OnHover ? null
            : _hoveredContainer ?? (ItemList.SelectedItems.Count == 1 ? ItemList.ItemContainerGenerator.ContainerFromItem(ItemList.SelectedItem) as ListBoxItem : null);
        if (container is not { DataContext: FenceItemView view, IsVisible: true })
        {
            HoverLabel.Visibility = Visibility.Collapsed;
            return;
        }
        _hoverLabelContainer = container;
        HoverLabel.DataContext = view; // bound: a name that loads later shows up (final review I3)
        // Never wider than the fence (narrow icon-only fences, final review I1); 18 = padding + border + margins.
        HoverLabelText.MaxWidth = Math.Clamp(ItemList.ActualWidth - 18, 24, 180);
        HoverLabel.Visibility = Visibility.Visible;
        PlaceHoverLabel();
    }

    /// <summary>Under the cell, clamped to the fence's sides; above it when there is no room; hidden when it is scrolled away.</summary>
    private void PlaceHoverLabel()
    {
        if (HoverLabel.Visibility != Visibility.Visible || _hoverLabelContainer is not { IsVisible: true } container) return;
        HoverLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = HoverLabel.DesiredSize;
        var cell = container.TransformToAncestor(ItemList).TransformBounds(new Rect(container.RenderSize));
        if (cell.Bottom <= 0 || cell.Top >= ItemList.ActualHeight)
        {
            HoverLabel.Visibility = Visibility.Collapsed; // a selected item scrolled out of view (final review I1)
            return;
        }
        var left = Math.Clamp(cell.Left + cell.Width / 2 - size.Width / 2, 2, Math.Max(2, ItemList.ActualWidth - size.Width - 2));
        var top = cell.Bottom - 2;
        if (top + size.Height > ItemList.ActualHeight - 2) top = Math.Max(2, cell.Top - size.Height + 2);
        Canvas.SetLeft(HoverLabel, left);
        Canvas.SetTop(HoverLabel, top);
    }

    /// <summary>Locked: the title no longer drags and the edges no longer resize.</summary>
    public void SetLocked(bool locked)
    {
        LockItem.IsChecked = locked;
        _locked = locked;
        ApplyChrome();
    }

    /// <summary>Locked: no drag, no resize. Rolled up: drag, no resize (its height is the stored full height).</summary>
    private void ApplyChrome()
    {
        // A fresh WindowChrome each time: editing the attached one in place is not re-applied after an unlock.
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            GlassFrameThickness = new Thickness(0),
            CaptionHeight = _locked ? 0 : _captionHeightDips,
            ResizeBorderThickness = new Thickness(_locked || _rolledUp ? 0 : ResizeBorderDips),
            // WindowChrome owns the window region and re-applies it on every resize: a radius of 0 kept resetting it to
            // a square, so the blur showed outside the rounded border (user screenshot 2026-10-03). Same radius as the border.
            CornerRadius = new CornerRadius(CornerRadiusDips),
            UseAeroCaptionButtons = false,
        });
    }

    /// <summary>Puts the fence at its full rect (physical pixels); rolled up, only the title bar of it shows.</summary>
    public void Place(PixelRect fullRect)
    {
        _heightAnimation.Stop();
        _fullHeightPx = fullRect.Height;
        FenceWindowChrome.SetPixelRect(Handle, _rolledUp && !_expansion.Expanded ? fullRect with { Height = RolledUpHeightPx } : fullRect);
    }

    public void SetRolledUp(bool rolledUp)
    {
        if (rolledUp == _rolledUp) return;
        var current = FenceWindowChrome.GetPixelRect(Handle);
        if (rolledUp && !_expansion.Expanded) _fullHeightPx = _heightAnimation.IsEnabled ? _heightTo : current.Height;
        _rolledUp = rolledUp;
        _expansion.Reset();
        ApplyChrome();
        AnimateHeight(rolledUp ? RolledUpHeightPx : _fullHeightPx);
        if (rolledUp) _hoverTimer.Start();
        else _hoverTimer.Stop();
    }

    /// <summary>Roll-up expand mode from settings (M6b): hover or click.</summary>
    public void SetRollupExpand(RollupExpand mode) => _expansion.Mode = mode;

    /// <summary>Shows at once (Pause, layout): a quick-hide fade still running must not hide the fence afterwards (M8a).</summary>
    public void ShowNow()
    {
        ++_fadeGeneration;
        BeginAnimation(OpacityProperty, null);
        Show();
    }

    /// <summary>Hides at once (Pause).</summary>
    public void HideNow()
    {
        ++_fadeGeneration;
        BeginAnimation(OpacityProperty, null);
        Hide();
    }

    /// <summary>Quick-hide (spec §6): a 150 ms fade, then hidden. Without animations it hides at once.</summary>
    public void HideFaded()
    {
        var generation = ++_fadeGeneration;
        if (!IsVisible || !AnimationsAllowed())
        {
            BeginAnimation(OpacityProperty, null);
            Hide();
            return;
        }
        var fade = new DoubleAnimation(0, FadeDuration);
        fade.Completed += (_, _) =>
        {
            if (generation != _fadeGeneration) return; // shown again meanwhile
            Hide();
            BeginAnimation(OpacityProperty, null);
        };
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Shows the fence (fading in when animations are on). The host sends it to the bottom afterwards.</summary>
    public void ShowFaded()
    {
        ++_fadeGeneration;
        var fadeIn = AnimationsAllowed();
        BeginAnimation(OpacityProperty, null);
        Show();
        if (fadeIn) BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, FadeDuration));
    }

    /// <summary>Click mode: a single click on the rolled-up title opens it.</summary>
    private bool ClickToOpen()
    {
        if (!_rolledUp || !_expansion.Click()) return false;
        AnimateHeight(_fullHeightPx);
        return true;
    }

    /// <summary>Roll-up and roll-down move the bottom edge over 200 ms (ease-out); at once without animations.</summary>
    private void AnimateHeight(int targetPx)
    {
        var current = FenceWindowChrome.GetPixelRect(Handle);
        if (!AnimationsAllowed() || _drag is not null || current.Height == targetPx)
        {
            _heightAnimation.Stop();
            FenceWindowChrome.SetPixelRect(Handle, current with { Height = targetPx });
            return;
        }
        _heightFrom = current.Height;
        _heightTo = targetPx;
        _heightClock = System.Diagnostics.Stopwatch.StartNew();
        _heightAnimation.Start();
    }

    private void StepHeight()
    {
        var current = FenceWindowChrome.GetPixelRect(Handle);
        var progress = Math.Min(1.0, _heightClock.Elapsed / RollUpDuration);
        if (_drag is not null) progress = 1; // never fight a move: jump to the end
        var eased = 1 - Math.Pow(1 - progress, 3);
        FenceWindowChrome.SetPixelRect(Handle, current with { Height = (int)Math.Round(_heightFrom + (_heightTo - _heightFrom) * eased) });
        if (progress >= 1) _heightAnimation.Stop();
    }

    private static readonly TimeSpan HoverTick = TimeSpan.FromMilliseconds(100);

    /// <summary>Title row plus the 1-DIP border above and below it.</summary>
    private int RolledUpHeightPx => (int)Math.Round((_captionHeightDips + 2) * VisualTreeHelper.GetDpi(this).DpiScaleY);

    // ponytail: polls the cursor every 100 ms while rolled up (no mouse-leave on a no-activate layered window when
    // the pointer leaves fast); in hover mode it also opens during a file drag, which is wanted.
    private void OnHoverTick()
    {
        if (Handle == 0 || !_rolledUp) return;
        if (_drag is not null || BodyContextMenu.IsOpen || _itemMenu?.IsOpen == true || _renaming) return; // never close under the user
        var (cursorX, cursorY) = FenceWindowChrome.GetCursorPosition();
        var rect = FenceWindowChrome.GetPixelRect(Handle);
        // While the height animates, judge "inside" against where the fence is going, so it does not flicker shut.
        var height = _heightAnimation.IsEnabled ? Math.Max(rect.Height, _heightTo) : rect.Height;
        var inside = cursorX >= rect.X && cursorX < rect.X + rect.Width && cursorY >= rect.Y && cursorY < rect.Y + height;
        if (!_expansion.Tick(inside)) return;
        AnimateHeight(_expansion.Expanded ? _fullHeightPx : RolledUpHeightPx);
    }

    /// <summary>Colours for Windows' light or dark app mode (M2c: fences follow Windows).</summary>
    public void ApplyTheme(bool light)
    {
        _lightTheme = light;
        var ink = light ? Colors.Black : Colors.White;
        SolidColorBrush Ink(byte alpha)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, ink.R, ink.G, ink.B));
            brush.Freeze();
            return brush;
        }
        // The accent blur ignores its tint colour (ACCENT_ENABLE_BLURBEHIND), so the veil is drawn here: none in dark mode
        // (the look the user approved in M2a), a dense light veil in light mode so dark text reads on any wallpaper.
        var veil = new SolidColorBrush(light ? Color.FromArgb(0xB8, 0xF2, 0xF2, 0xF2) : Colors.Transparent);
        veil.Freeze();
        Resources["FenceVeil"] = veil;
        Resources["FenceText"] = Ink(light ? (byte)0xE6 : (byte)0xFF);
        Resources["FenceSubtleText"] = Ink(0xA0);
        Resources["FenceBorder"] = Ink(light ? (byte)0x33 : (byte)0x40);
        // The look's defaults until the host applies the fence's own (M14): v1.6's outline and title ink.
        Resources["FenceOutline"] = Resources["FenceBorder"];
        Resources["FenceTitleText"] = Ink(light ? (byte)0xE6 : (byte)0xFF);
        Resources["FenceDivider"] = Ink(light ? (byte)0x1F : (byte)0x26);
        Resources["FenceHover"] = Ink(light ? (byte)0x14 : (byte)0x22);
        Resources["FenceSelected"] = Ink(light ? (byte)0x2A : (byte)0x44);
        Resources["FenceScrollThumb"] = Ink(light ? (byte)0x44 : (byte)0x55);
        var hoverLabel = new SolidColorBrush(light ? Color.FromArgb(0xF0, 0xF4, 0xF4, 0xF4) : Color.FromArgb(0xE6, 0x20, 0x22, 0x28));
        hoverLabel.Freeze();
        Resources["HoverLabelBackground"] = hoverLabel;
        // Soft halo behind labels, like desktop icon labels: readable on busy or bright wallpapers (user choice).
        var shadow = new DropShadowEffect
        {
            Color = light ? Colors.White : Colors.Black,
            ShadowDepth = light ? 0 : 1,
            BlurRadius = 4,
            Opacity = 0.9,
        };
        shadow.Freeze();
        Resources["LabelShadow"] = shadow;
    }

    private FenceStyle? _style;
    private const string CustomColourTag = "custom";

    /// <summary>
    /// The fence's look (M14, spec §3): veil, outline, title ink, title strip, colour bar, title font and the title row's
    /// height (with Windows' caption area). Item labels keep the tone's ink (<see cref="ApplyTheme"/>).
    /// </summary>
    public void ApplyStyle(FenceStyle style)
    {
        if (style == _style)
        {
            ApplyTabLook(style); // only the headers SetTabs rebuilt (M16: brushes are not rebuilt when nothing changed)
            return;
        }
        _style = style;
        Resources["FenceVeil"] = Frozen(style.Veil);
        Resources["FenceOutline"] = Frozen(style.Border);
        Resources["FenceTitleText"] = Frozen(style.TitleText);
        TitleStripFill.Background = style.TitleStrip is { } strip ? Frozen(strip) : Brushes.Transparent;
        TitleText.FontFamily = new FontFamily(style.Font.Family); // a font that is gone falls back to Segoe UI; the name stays
        TitleText.FontSize = style.Font.Size;
        TitleText.FontWeight = ToWeight(style.Font.Weight);
        ApplyTabLook(style);
        if (style.TitleHeight != _captionHeightDips)
        {
            _captionHeightDips = style.TitleHeight;
            TitleRow.Height = new GridLength(style.TitleHeight);
            ApplyChrome();
            if (_rolledUp && !_expansion.Expanded && Handle != 0)
            {
                _heightAnimation.Stop(); // a roll-up running toward the old height must not end 4 px off (M16)
                FenceWindowChrome.SetPixelRect(Handle, FenceWindowChrome.GetPixelRect(Handle) with { Height = RolledUpHeightPx });
            }
        }
    }

    /// <summary>The colour bar of a single fence and the tab headers' font: they follow every header rebuild.</summary>
    private void ApplyTabLook(FenceStyle style)
    {
        TitleColorBar.Visibility = _tabs.Count <= 1 && style.Bar is not null ? Visibility.Visible : Visibility.Collapsed;
        if (style.Bar is { } bar) TitleColorBar.Fill = Frozen(bar);
        foreach (var header in TabStrip.Children.OfType<Border>())
        {
            if (header.Child is not Grid { Children: [TextBlock title, ..] }) continue;
            title.FontFamily = TitleText.FontFamily;
            title.FontSize = style.Font.Size;
            if (Equals(header.Tag, FenceId)) title.FontWeight = TitleText.FontWeight;
        }
    }

    private static FontWeight ToWeight(TitleWeight weight) => weight switch
    {
        TitleWeight.Regular => FontWeights.Normal,
        TitleWeight.Bold => FontWeights.Bold,
        _ => FontWeights.SemiBold,
    };

    /// <summary>Colour → Custom… (M14): Windows' colour picker.</summary>
    private void BuildCustomColourMenu()
    {
        TabColorItem.Items.Add(new Separator());
        var custom = new MenuItem { Header = "Custom…", Tag = CustomColourTag, IsCheckable = true };
        custom.Click += (_, _) => CustomColorRequested?.Invoke();
        TabColorItem.Items.Add(custom);
    }

    /// <summary>The Recycle Bin turned full or empty, or another special icon changed (M8c).</summary>
    public void ReloadSpecialIcons()
    {
        var iconSizePx = IconSizePx;
        foreach (var view in _items.Where(view => view.Target.StartsWith("::", StringComparison.Ordinal))) _iconLoader.Request(view, iconSizePx);
    }

    /// <summary>New size or DPI, or "Refresh": every icon and name is requested again in place; selection stays (M2c review carry-over).</summary>
    public void ReloadIcons()
    {
        var iconSizePx = IconSizePx;
        foreach (var view in _items) _iconLoader.Request(view, iconSizePx);
    }

    /// <summary>Starts renaming the fence title (menu, or a freshly drawn fence).</summary>
    public void BeginRename()
    {
        _renaming = true;
        TitleBox.Text = _title;
        ShowTitleOrTabs();
        TitleBox.Visibility = Visibility.Visible;
        Activate(); // keyboard input needs the fence active; it stays at the bottom (owned by Progman, ADR-011)
        // With another app in front the fence may not get focus (ADR-015); then typing would go elsewhere and the box
        // could never close, so give up instead (M2c review).
        if (!TitleBox.Focus() || !IsActive)
        {
            EndRename(commit: false);
            return;
        }
        TitleBox.SelectAll();
    }

    private void EndRename(bool commit)
    {
        if (!_renaming) return;
        _renaming = false;
        TitleBox.Visibility = Visibility.Collapsed;
        ShowTitleOrTabs();
        if (commit && TitleBox.Text != _title) RenameRequested?.Invoke(TitleBox.Text);
    }

    private void OnTitleBoxKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key is not (Key.Enter or Key.Escape)) return;
        EndRename(commit: args.Key == Key.Enter);
        ItemList.Focus();
        args.Handled = true;
    }

    private void OnItemListKeyDown(object sender, KeyEventArgs args)
    {
        var selected = ItemList.SelectedItems.OfType<FenceItemView>().ToList();
        var key = args.Key == Key.System ? args.SystemKey : args.Key; // Alt+Enter arrives as Key.System
        switch (key)
        {
            case Key.Enter when Keyboard.Modifiers == ModifierKeys.Alt && selected.Count == 1 && _kind == FenceKind.Items:
                PropertiesRequested?.Invoke(selected[0].Key, false);
                break;
            case Key.Enter when selected.Count == 1:
                OpenRequested?.Invoke(selected[0].Key);
                break;
            case Key.Enter when selected.Count > 1:
                OpenManyRequested?.Invoke(selected.Select(view => view.Key).ToList());
                break;
            case Key.Delete when selected.Count > 0:
                RemoveRequested?.Invoke(selected.Select(view => view.Key).ToList()); // Shift+Del too: only the item goes
                break;
            case Key.F2 when selected.Count == 1 && _kind == FenceKind.Items: // library shortcuts are named by their game (M12); views show real names (M21)
                PropertiesRequested?.Invoke(selected[0].Key, true);
                break;
            default:
                return;
        }
        args.Handled = true;
    }

    /// <summary>Right-click on an item opens the item menu (the host builds it) instead of the fence menu.</summary>
    private void OnBodyContextMenuOpening(object sender, ContextMenuEventArgs args)
    {
        if (ItemsControl.ContainerFromElement(ItemList, (DependencyObject)args.OriginalSource) is not ListBoxItem { DataContext: FenceItemView clicked } container) return;
        args.Handled = true;
        if (!container.IsSelected)
        {
            ItemList.SelectedItems.Clear();
            container.IsSelected = true;
        }
        // From the mouse, or (menu key: CursorLeft < 0) from the item's corner. PointToScreen gives physical pixels.
        var anchor = args.CursorLeft >= 0 ? PointToScreen(Mouse.GetPosition(this)) : container.PointToScreen(new Point(container.ActualWidth / 2, container.ActualHeight / 2));
        // The clicked item first: Properties and Locate… act on it, whatever else is selected (user choice 2026-10-03).
        List<string> keys = [clicked.Key, .. ItemList.SelectedItems.OfType<FenceItemView>().Select(view => view.Key).Where(key => key != clicked.Key)];
        ItemMenuRequested?.Invoke(keys, (int)anchor.X, (int)anchor.Y, args.CursorLeft < 0);
    }

    /// <summary>The index to insert before when dropping at a screen point (physical pixels).</summary>
    public int HitTest(int screenX, int screenY)
    {
        // Over a tab header: show that tab now, so the drop lands in it (M9).
        if (TabHeaderAt(screenX, screenY) is { } hoveredTab && hoveredTab != FenceId) TabSelected?.Invoke(hoveredTab);
        var point = ItemList.PointFromScreen(new Point(screenX, screenY));
        var cells = new List<(double Left, double Top, double Width, double Height)>();
        var cellIndexes = new List<int>();
        for (var index = 0; index < _items.Count; index++)
        {
            if (ItemList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container) continue;
            var bounds = container.TransformToAncestor(ItemList).TransformBounds(new Rect(container.RenderSize));
            cells.Add((bounds.Left, bounds.Top, bounds.Width, bounds.Height));
            cellIndexes.Add(index);
        }
        // Rows reach down to their tallest item (mixed label heights, M3b review).
        var cellAt = DropZones.InsertIndex(cells, point.X, point.Y);
        return cellAt < cellIndexes.Count ? cellIndexes[cellAt] : _items.Count;
    }

    /// <summary>Shows where a drag would land: a caret before the insert position (null hides it).</summary>
    public void ShowDropFeedback(int? insertAt)
    {
        InsertCaret.Visibility = Visibility.Collapsed;
        if (insertAt is not { } position) return;
        // Caret at the left edge of the item it goes before, or after the last item.
        var before = position < _items.Count ? ItemList.ItemContainerGenerator.ContainerFromIndex(position) as ListBoxItem : null;
        var anchor = before ?? (_items.Count > 0 ? ItemList.ItemContainerGenerator.ContainerFromIndex(_items.Count - 1) as ListBoxItem : null);
        if (anchor is null) return;
        var bounds = anchor.TransformToAncestor(ItemList).TransformBounds(new Rect(anchor.RenderSize));
        Canvas.SetLeft(InsertCaret, (before is null ? bounds.Right : bounds.Left) - 1);
        Canvas.SetTop(InsertCaret, bounds.Top + 4);
        InsertCaret.Height = Math.Max(0, bounds.Height - 8);
        InsertCaret.Visibility = Visibility.Visible;
    }

    private static TAncestor? FindAncestor<TAncestor>(DependencyObject source) where TAncestor : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TAncestor match) return match;
        }
        return null;
    }

    private void OnListPress(object sender, MouseButtonEventArgs args)
    {
        if (args.OriginalSource is DependencyObject source && FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) is not null) return;
        // Text selection in a text box must never start a drag (M3b review I3).
        if (args.OriginalSource is DependencyObject pressed && FindAncestor<TextBox>(pressed) is not null) return;
        var container = args.OriginalSource is DependencyObject element ? FindAncestor<ListBoxItem>(element) : null;
        if (container is null)
        {
            // Empty space: rubber band. Without Ctrl it starts a new selection. The list takes focus, so a pending rename
            // commits and the keys after the band go to the list (M3b review carry-over).
            ItemList.Focus();
            _bandStart = args.GetPosition(ItemList);
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) ItemList.SelectedItems.Clear();
            ItemList.CaptureMouse();
            args.Handled = true;
            return;
        }
        _pressPoint = args.GetPosition(ItemList);
        // Ctrl+press on a selected item: unselect it on release, not now, so Ctrl+drag can still copy the selection (M3b review).
        if (container.IsSelected && Keyboard.Modifiers == ModifierKeys.Control && args.ClickCount == 1)
        {
            _deferredToggle = container;
            container.Focus();
            args.Handled = true;
            return;
        }
        // Pressing one of several selected items must keep the selection, so they can be dragged together.
        if (container.IsSelected && ItemList.SelectedItems.Count > 1 && Keyboard.Modifiers == ModifierKeys.None && args.ClickCount == 1)
        {
            _deferredSelect = container;
            container.Focus();
            args.Handled = true;
        }
    }

    private void OnListMove(object sender, MouseEventArgs args)
    {
        if (args.LeftButton != MouseButtonState.Pressed)
        {
            _pressPoint = null;
            return;
        }
        var position = args.GetPosition(ItemList);
        if (_bandStart is { } bandStart)
        {
            UpdateBand(bandStart, position);
            return;
        }
        if (_pressPoint is not { } pressPoint) return;
        if (Math.Abs(position.X - pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _pressPoint = null;
        _deferredSelect = null;
        _deferredToggle = null; // dragged: the item stays selected
        var dragged = ItemList.SelectedItems.OfType<FenceItemView>().Select(view => view.Key).ToList();
        if (dragged.Count > 0) DragRequested?.Invoke(dragged); // returns when the drag ends (Windows' modal loop)
    }

    private void OnListRelease(object sender, MouseButtonEventArgs args)
    {
        _pressPoint = null;
        if (_deferredToggle is { } toggled)
        {
            toggled.IsSelected = false;
            _deferredToggle = null;
        }
        if (_deferredSelect is { } container)
        {
            ItemList.SelectedItems.Clear();
            container.IsSelected = true;
            _deferredSelect = null;
        }
        if (_bandStart is not null)
        {
            ItemList.ReleaseMouseCapture(); // ends the band via LostMouseCapture
            args.Handled = true;
        }
    }

    /// <summary>Selects every item the band touches (added to the selection when Ctrl was held at the start).</summary>
    private void UpdateBand(Point start, Point current)
    {
        var band = new Rect(start, current);
        Canvas.SetLeft(SelectionBand, band.Left);
        Canvas.SetTop(SelectionBand, band.Top);
        SelectionBand.Width = band.Width;
        SelectionBand.Height = band.Height;
        SelectionBand.Visibility = Visibility.Visible;
        for (var index = 0; index < _items.Count; index++)
        {
            if (ItemList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container) continue;
            var bounds = container.TransformToAncestor(ItemList).TransformBounds(new Rect(container.RenderSize));
            if (bounds.IntersectsWith(band)) container.IsSelected = true;
            else if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) container.IsSelected = false;
        }
    }

    private void EndBand()
    {
        _bandStart = null;
        SelectionBand.Visibility = Visibility.Collapsed;
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left) return;
        if (ItemsControl.ContainerFromElement(ItemList, (DependencyObject)args.OriginalSource) is ListBoxItem { DataContext: FenceItemView view })
            OpenRequested?.Invoke(view.Key);
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        Handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(Handle).AddHook(OnMessage);
        FenceWindowChrome.ApplyToolWindowStyles(Handle);
        FenceWindowChrome.ApplyAccentBlur(Handle);
        if (!FenceWindowChrome.UseRoundedCorners(Handle)) CornersUnavailable = true; // Windows 10: square blur corners (ADR-024)
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // Windows' item menu draws "Send to", "Open with" and shell-extension entries through its owner window.
        if (ShellItemMenu.HandleMenuMessage(message, wParam, lParam, out var menuResult))
        {
            handled = true;
            return menuResult;
        }
        switch (message)
        {
            case WmWindowPosChanging:
                if (!Peeking) FenceWindowChrome.KeepAtBottom(lParam); // fences never rise above apps, except during Peek
                break;
            case WmDeviceChange when wParam is DbtDeviceArrival or DbtDeviceRemoveComplete:
                DrivesChanged?.Invoke(); // a USB stick plugged back in: its items come back by themselves (spec §4)
                break;
            // Click mode: the first press on a rolled-up title opens it instead of starting a move (M6b).
            case WmNcLeftButtonDown when wParam == HitTestCaption && IsSecondCaptionClick(lParam):
                RollUpToggled?.Invoke();
                handled = true;
                return 0;
            case WmNcLeftButtonDown when wParam == HitTestCaption && ClickToOpen():
                handled = true;
                return 0;
            // The title is caption area: Windows would show its Move / Size / Close menu there. The fence menu instead (M13c).
            case WmNcRightButtonUp when wParam == HitTestCaption:
                OpenFenceMenu();
                handled = true;
                return 0;
            case WmNcLeftButtonDoubleClick when wParam == HitTestCaption:
                _lastCaptionPress = null;
                // A third quick click after a recognised double-click arrives as DBLCLK: not a second toggle (M8b review).
                if (!IsRightAfterRecognisedDoubleClick()) RollUpToggled?.Invoke();
                _recognisedDoubleClickAt = null; // used once: a later genuine double-click toggles (M8c review M12)
                handled = true;
                return 0;
            case WmEnterSizeMove:
                // A move that starts during a roll-up animation finishes it. Windows' move loop captured the partial rect
                // and proposes it on every step, so WM_MOVING keeps the finished height too (M6b review M2).
                _moveHeightPx = null;
                if (_heightAnimation.IsEnabled)
                {
                    _heightAnimation.Stop();
                    _moveHeightPx = _heightTo;
                    FenceWindowChrome.SetPixelRect(Handle, FenceWindowChrome.GetPixelRect(Handle) with { Height = _heightTo });
                }
                _drag = new DragTracker(FenceWindowChrome.GetPixelRect(Handle));
                break;
            case WmMoving when SnapRect is not null && _drag is not null:
                var proposed = FenceWindowChrome.ReadRect(lParam);
                if (_moveHeightPx is { } heightPx) proposed = proposed with { Height = heightPx };
                FenceWindowChrome.WriteRect(lParam, _drag.Step(proposed, snap: rect => SnapRect(rect, SnapEdges.Move)));
                handled = true;
                return 1;
            case WmSizing when SnapRect is not null && _drag is not null:
                var edges = SizingEdges((int)wParam);
                FenceWindowChrome.WriteRect(lParam, _drag.Step(FenceWindowChrome.ReadRect(lParam), snap: rect => SnapRect(rect, edges)));
                handled = true;
                return 1;
            case WmExitSizeMove:
                _drag = null;
                _moveHeightPx = null;
                // Rolled up, the window is shorter than the fence: the stored rect keeps the full height.
                var moved = FenceWindowChrome.GetPixelRect(Handle);
                MovedByUser?.Invoke(this, _rolledUp ? moved with { Height = _fullHeightPx } : moved);
                if (_rolledUp && !_expansion.Expanded && moved.Height != RolledUpHeightPx) AnimateHeight(RolledUpHeightPx); // a move during an animation
                break;
        }
        return 0;
    }

    /// <summary>
    /// Every caption press is remembered; a second one within the double-click time and size counts as a double-click.
    /// Right after another window hands the fence the foreground, Windows sends the second press as a plain
    /// WM_NCLBUTTONDOWN instead of WM_NCLBUTTONDBLCLK, so the first double-click did nothing (M6a finding 4, M8b).
    /// </summary>
    /// <param name="pointParam">The press's screen point (WM_NCLBUTTONDOWN's lParam): where it happened, not where the
    /// cursor is when a busy UI thread gets to it; its time is the message's own (M8b review).</param>
    private bool IsSecondCaptionClick(nint pointParam)
    {
        var (milliseconds, widthPx, heightPx) = FenceWindowChrome.DoubleClickSettings();
        var at = FenceWindowChrome.MessageTime();
        var (x, y) = ((short)(pointParam & 0xFFFF), (short)((pointParam >> 16) & 0xFFFF));
        var isSecond = _lastCaptionPress is { } last && unchecked(at - last.At) <= milliseconds
                       && Math.Abs(x - last.Where.X) <= widthPx / 2.0 && Math.Abs(y - last.Where.Y) <= heightPx / 2.0;
        _lastCaptionPress = isSecond ? null : (at, new Point(x, y));
        if (isSecond) _recognisedDoubleClickAt = at;
        return isSecond;
    }

    private bool IsRightAfterRecognisedDoubleClick() =>
        _recognisedDoubleClickAt is { } recognised && unchecked(FenceWindowChrome.MessageTime() - recognised) <= FenceWindowChrome.DoubleClickSettings().Milliseconds;

    /// <summary>WM_SIZING's WMSZ_* value as the edges being dragged.</summary>
    private static SnapEdges SizingEdges(int sizingEdge) => sizingEdge switch
    {
        1 => SnapEdges.Left,
        2 => SnapEdges.Right,
        3 => SnapEdges.Top,
        4 => SnapEdges.Top | SnapEdges.Left,
        5 => SnapEdges.Top | SnapEdges.Right,
        6 => SnapEdges.Bottom,
        7 => SnapEdges.Bottom | SnapEdges.Left,
        8 => SnapEdges.Bottom | SnapEdges.Right,
        _ => SnapEdges.None,
    };
}
