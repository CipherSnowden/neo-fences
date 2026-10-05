using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NeoFences.Core.Items;

namespace NeoFences.App;

/// <summary>
/// A folder panel (M26, spec 2026-10-05-folder-panel-design §2): Details (columns that sort), List or Icons, a header with
/// Back / Up / Home while browsing, and the status lines. It handles its own clicks, keys, wheel and drags; everything it
/// does goes to the host as a <see cref="PanelCommand"/>. Nothing here writes to the folder.
/// </summary>
public partial class FolderPanelView : UserControl
{
    private const double DateWidth = 112, TypeWidth = 56, SizeWidth = 72, ScrollbarRoom = 12;
    private readonly GridView _details = new() { AllowsColumnReorder = false };
    private readonly Dictionary<GridViewColumn, PanelSort> _sorts = [];
    private readonly GridViewColumn _nameColumn, _dateColumn, _typeColumn, _sizeColumn;
    private FolderPanelModel? _model;
    private Point? _pressPoint;
    private ListViewItem? _deferredSelect; // pressed on one of several selected entries: selected alone only on release

    public FolderPanelView()
    {
        InitializeComponent();
        _nameColumn = Column("Name", PanelSort.Name, cell: (DataTemplate)Resources["NameCell"]);
        _dateColumn = Column("Date modified", PanelSort.Date, text: nameof(PanelEntry.DateText));
        _typeColumn = Column("Type", PanelSort.Type, text: nameof(PanelEntry.TypeText));
        _sizeColumn = Column("Size", PanelSort.Size, text: nameof(PanelEntry.SizeText));
        DataContextChanged += (_, _) => Attach(DataContext as FolderPanelModel);
        SizeChanged += (_, _) => SizeColumns();
        EntryList.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(OnHeaderClick));
        EntryList.PreviewMouseLeftButtonDown += OnPress;
        EntryList.PreviewMouseMove += OnMove;
        EntryList.PreviewMouseLeftButtonUp += OnRelease;
        EntryList.MouseDoubleClick += OnDoubleClick;
        EntryList.KeyDown += OnKeyDown;
        EntryList.PreviewMouseWheel += OnWheel;
        // A press on the panel's own space never selects the panel element behind it (an invisible selection Delete would remove; final review M12).
        EntryList.MouseLeftButtonDown += (_, press) => { press.Handled = true; EntryList.Focus(); };
        EntryList.ContextMenuOpening += OnEntryMenu;
        BackButton.Click += (_, _) => Send(new PanelNavigate(PanelMove.Back));
        UpButton.Click += (_, _) => Send(new PanelNavigate(PanelMove.Up));
        HomeButton.Click += (_, _) => Send(new PanelNavigate(PanelMove.Home));
        MoreText.MouseLeftButtonUp += (_, click) => { click.Handled = true; Send(new PanelOpenFolder()); };
    }

    /// <summary>
    /// True when a mouse or key event from <paramref name="source"/> is the panel's own (its entries, buttons and lines);
    /// the header's name is the fence's, so the panel can be selected and dragged by it like any element.
    /// </summary>
    public bool OwnsInput(DependencyObject source) => !IsWithin(source, HeaderRow) || IsWithin(source, BrowseButtons);

    private GridViewColumn Column(string header, PanelSort sort, DataTemplate? cell = null, string? text = null)
    {
        var column = new GridViewColumn { Header = header };
        if (cell is not null) column.CellTemplate = cell;
        else column.DisplayMemberBinding = new System.Windows.Data.Binding(text);
        _sorts[column] = sort;
        return column;
    }

    private void Attach(FolderPanelModel? model)
    {
        if (_model is not null)
        {
            _model.LookChanged -= ApplyLook;
            _model.PropertyChanged -= OnModelChanged;
        }
        _model = model;
        if (model is null) return;
        model.LookChanged += ApplyLook;
        model.PropertyChanged += OnModelChanged;
        ApplyLook();
        ShowBrowsing();
        HeaderRow.Visibility = model.ShowHeader ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName is nameof(FolderPanelModel.Sort) or nameof(FolderPanelModel.Descending)) ShowSort();
        if (change.PropertyName == nameof(FolderPanelModel.Browsing)) ShowBrowsing();
        if (change.PropertyName == nameof(FolderPanelModel.ShowHeader)) HeaderRow.Visibility = _model?.ShowHeader == false ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowBrowsing() => BrowseButtons.Visibility = _model?.Browsing == true ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Details: the columns (Name and Date when narrow); List: names in rows; Icons: tiles like the fence's own.</summary>
    private void ApplyLook()
    {
        if (_model is null) return;
        var look = _model.Look;
        _details.Columns.Clear();
        if (look == PanelLook.Details)
        {
            GridViewColumn[] columns = _model.Columns == 2 ? [_nameColumn, _dateColumn] : [_nameColumn, _dateColumn, _typeColumn, _sizeColumn];
            foreach (var column in columns) _details.Columns.Add(column);
        }
        EntryList.View = look == PanelLook.Details ? _details : null;
        EntryList.ItemContainerStyle = (Style)Resources[look switch { PanelLook.Details => "DetailsRow", PanelLook.Icons => "IconTile", _ => "PlainRow" }];
        EntryList.ItemTemplate = look switch { PanelLook.Icons => (DataTemplate)Resources["IconCell"], PanelLook.List => (DataTemplate)Resources["NameCell"], _ => null };
        EntryList.ItemsPanel = (ItemsPanelTemplate)Resources[look == PanelLook.Icons ? "TilesPanel" : "RowsPanel"];
        ShowSort();
        SizeColumns();
    }

    /// <summary>The sorted column says so with an arrow.</summary>
    private void ShowSort()
    {
        if (_model is null) return;
        foreach (var (column, sort) in _sorts)
        {
            var name = sort switch { PanelSort.Date => "Date modified", PanelSort.Type => "Type", PanelSort.Size => "Size", _ => "Name" };
            column.Header = sort == _model.Sort ? $"{name} {(_model.Descending ? "▾" : "▴")}" : name;
        }
    }

    /// <summary>Name takes what the other columns leave; nothing scrolls sideways.</summary>
    private void SizeColumns()
    {
        var others = _details.Columns.Where(column => column != _nameColumn).Sum(column => column == _dateColumn ? DateWidth : column == _typeColumn ? TypeWidth : SizeWidth);
        foreach (var column in _details.Columns) column.Width = column == _nameColumn ? Math.Max(60, ActualWidth - others - ScrollbarRoom) : _sorts[column] switch
        {
            PanelSort.Date => DateWidth,
            PanelSort.Type => TypeWidth,
            _ => SizeWidth,
        };
    }

    private void OnHeaderClick(object sender, RoutedEventArgs click)
    {
        if (click.OriginalSource is GridViewColumnHeader { Column: { } column } && _sorts.TryGetValue(column, out var sort)) Send(new PanelSortBy(sort));
    }

    /// <summary>A row came into view (rows are virtualized): its icon is asked for now, not for all 500 at once.</summary>
    private void OnRowLoaded(object sender, RoutedEventArgs loaded)
    {
        if (sender is ListViewItem { DataContext: PanelEntry entry }) _model?.IconWanted?.Invoke(entry);
    }

    private ListViewItem? RowOf(object source) => source is DependencyObject element ? FindAncestor<ListViewItem>(element) : null;

    private IReadOnlyList<string> SelectedPaths(PanelEntry? first = null) =>
        [.. new[] { first }.OfType<PanelEntry>().Concat(EntryList.SelectedItems.OfType<PanelEntry>().Where(entry => entry != first)).Select(entry => entry.Path)];

    private void OnPress(object sender, MouseButtonEventArgs press)
    {
        _pressPoint = null;
        if (RowOf(press.OriginalSource) is not { } row) return;
        _pressPoint = press.GetPosition(EntryList);
        // Pressing one of several selected entries keeps them all, so they can be dragged together.
        if (row.IsSelected && EntryList.SelectedItems.Count > 1 && Keyboard.Modifiers == ModifierKeys.None && press.ClickCount == 1)
        {
            _deferredSelect = row;
            row.Focus();
            press.Handled = true;
        }
    }

    private void OnMove(object sender, MouseEventArgs move)
    {
        if (move.LeftButton != MouseButtonState.Pressed || _pressPoint is not { } pressed) return;
        var position = move.GetPosition(EntryList);
        if (Math.Abs(position.X - pressed.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - pressed.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _pressPoint = null;
        _deferredSelect = null;
        var paths = SelectedPaths();
        if (paths.Count > 0) Send(new PanelDrag(paths)); // returns when the drag ends (Windows' modal loop)
    }

    private void OnRelease(object sender, MouseButtonEventArgs release)
    {
        _pressPoint = null;
        if (_deferredSelect is not { } row) return;
        EntryList.SelectedItems.Clear();
        row.IsSelected = true;
        _deferredSelect = null;
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs click)
    {
        if (click.ChangedButton != MouseButton.Left || RowOf(click.OriginalSource) is not { DataContext: PanelEntry entry }) return;
        click.Handled = true;
        Send(new PanelOpen([entry.Path]));
    }

    private void OnKeyDown(object sender, KeyEventArgs key)
    {
        var pressed = key.Key == Key.System ? key.SystemKey : key.Key;
        PanelCommand? command = pressed switch
        {
            Key.Enter when EntryList.SelectedItems.Count > 0 && Keyboard.Modifiers == ModifierKeys.None => new PanelOpen(SelectedPaths()),
            Key.Back => new PanelNavigate(PanelMove.Back),
            Key.Left when Keyboard.Modifiers == ModifierKeys.Alt => new PanelNavigate(PanelMove.Back),
            Key.Up when Keyboard.Modifiers == ModifierKeys.Alt => new PanelNavigate(PanelMove.Up),
            _ => null,
        };
        if (command is null)
        {
            // An arrow at the first or last row (or sideways in rows) is left unhandled by the list: it must not move on to the
            // fence's own elements, where Delete or F2 would then act (final review I3).
            if (pressed is Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown) key.Handled = true;
            return;
        }
        key.Handled = true;
        Send(command);
    }

    /// <summary>
    /// The wheel scrolls the panel; at its top or bottom it scrolls the fence instead, so a panel taller than its fence never
    /// hides its own header (live check M26).
    /// </summary>
    private void OnWheel(object sender, MouseWheelEventArgs wheel)
    {
        if (FindDescendant<ScrollViewer>(EntryList) is not { } rows) return;
        var atEnd = wheel.Delta > 0 ? rows.VerticalOffset <= 0 : rows.VerticalOffset >= rows.ScrollableHeight;
        if (!atEnd || VisualTreeHelper.GetParent(this) is not UIElement parent) return;
        wheel.Handled = true;
        parent.RaiseEvent(new MouseWheelEventArgs(wheel.MouseDevice, wheel.Timestamp, wheel.Delta) { RoutedEvent = MouseWheelEvent, Source = this });
    }

    private static TDescendant? FindDescendant<TDescendant>(DependencyObject parent) where TDescendant : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is TDescendant match) return match;
            if (FindDescendant<TDescendant>(child) is { } deeper) return deeper;
        }
        return null;
    }

    /// <summary>Right-click on an entry: the entry menu (the host's); on empty space the fence shows the panel's own menu.</summary>
    private void OnEntryMenu(object sender, ContextMenuEventArgs args)
    {
        if (RowOf(args.OriginalSource) is not { DataContext: PanelEntry entry } row) return;
        args.Handled = true;
        if (!row.IsSelected)
        {
            EntryList.SelectedItems.Clear();
            row.IsSelected = true;
        }
        var fromKeyboard = args.CursorLeft < 0;
        var anchor = fromKeyboard ? row.PointToScreen(new Point(row.ActualWidth / 2, row.ActualHeight / 2)) : PointToScreen(Mouse.GetPosition(this));
        Send(new PanelEntryMenu(SelectedPaths(entry), Extended: !fromKeyboard && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), (int)anchor.X, (int)anchor.Y, fromKeyboard));
    }

    private void Send(PanelCommand command) => _model?.Commands?.Invoke(command);

    private static bool IsWithin(DependencyObject source, DependencyObject ancestor)
    {
        for (var current = source; current is not null; current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current == ancestor) return true;
        }
        return false;
    }

    private static TAncestor? FindAncestor<TAncestor>(DependencyObject source) where TAncestor : DependencyObject
    {
        for (var current = source; current is not null; current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current is TAncestor match) return match;
        }
        return null;
    }
}
