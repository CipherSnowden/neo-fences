using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using NeoFences.Core.Items;

namespace NeoFences.App;

/// <summary>
/// What a fence shows of one item (M18): a virtual item (key = its id), or a Game Library shortcut (key = its path).
/// The host builds these from its data; the window shows them in place (<see cref="FenceWindow.SetItems"/>).
/// </summary>
/// <param name="Tile">A 2:3 tile (M22: a game item shown as its cover); <paramref name="TileArt"/> is its poster or logo.</param>
/// <param name="IsGame">A game item (M22): "Not installed" instead of "Missing".</param>
/// <param name="Span">Its size in cells (M24); null: 1×2 for a tile, else 1×1.</param>
/// <param name="Cell">Its stored cell (M24, Free fences).</param>
/// <param name="Panel">A folder shown as a panel (M26); <paramref name="Fill"/>: it takes the whole fence.</param>
public sealed record ShownItem(string Key, string Target, string? Name = null, ItemIcon? Icon = null, string? Note = null,
    TargetState State = TargetState.Ok, bool Tile = false, (string Path, bool IsPoster)? TileArt = null, bool IsGame = false,
    GridSpan? Span = null, GridCell? Cell = null, WidgetOptions? Options = null, FolderPanel? Panel = null, bool Fill = false);

/// <summary>One item as a fence shows it. Label and icon start as placeholders and fill in from <see cref="IconLoader"/>.</summary>
public sealed class FenceItemView : INotifyPropertyChanged
{
    public FenceItemView(ShownItem shown)
    {
        Key = shown.Key;
        Target = shown.Target;
        Update(shown);
    }

    public string Key { get; }

    public string Target
    {
        get;
        private set { field = value; Changed(); Changed(nameof(IsShortcut)); }
    } = "";

    /// <summary>The item's own name: it always wins over Windows' name for the target.</summary>
    public string? OwnName { get; private set; }

    /// <summary>The item's own icon, or null for the target's.</summary>
    public ItemIcon? OwnIcon { get; private set; }

    public string? Note { get; private set; }

    /// <summary>A shortcut (.lnk, .url, .pif): gets the arrow overlay when Settings shows shortcut arrows (M8b).</summary>
    public bool IsShortcut => Path.GetExtension(Target).ToLowerInvariant() is ".lnk" or ".url" or ".pif";

    public string Label
    {
        get;
        set { field = value; Changed(); Changed(nameof(ToolTipText)); }
    } = "";

    public TargetState State
    {
        get;
        private set { field = value; Changed(); Changed(nameof(ToolTipText)); }
    }

    /// <summary>The note, or what is wrong with the target (spec §4); the name otherwise.</summary>
    public string ToolTipText => State switch
    {
        TargetState.Missing => IsGame ? $"Not installed: {Label}" : $"Missing: {Target}",
        TargetState.Unavailable => TargetChecks.IsNetworkPath(Target) ? $"Network location not reachable: {Target}"
            : $"Drive {TargetChecks.RootOf(Target)?.TrimEnd('\\') ?? "?"} is not connected: {Target}",
        _ => Note is { Length: > 0 } note ? $"{Label}\n{note}" : Label,
    };

    /// <summary>
    /// Takes the host's latest data. True when the icon must load again: another target or icon, a name that is no longer
    /// the item's own (Windows' name for the target comes with the icon), or a target that is back (missing or unplugged).
    /// </summary>
    public bool Update(ShownItem shown)
    {
        var reload = !string.Equals(Target, shown.Target, StringComparison.Ordinal) || OwnIcon != shown.Icon
                     || (OwnName is not null && shown.Name is null)
                     || (State != TargetState.Ok && shown.State == TargetState.Ok); // back: its real icon and name (final review I5)
        Target = shown.Target;
        OwnIcon = shown.Icon;
        OwnName = string.IsNullOrWhiteSpace(shown.Name) ? null : shown.Name;
        Note = shown.Note;
        Tile = shown.Tile;
        TileArt = shown.TileArt;
        IsGame = shown.IsGame;
        Widget = Widgets.Of(shown.Target); // M25
        Options = shown.Options ?? new WidgetOptions();
        Span = shown.Span ?? (shown.Tile ? new GridSpan(1, 2) : GridSpan.One);
        StoredCell = shown.Cell;
        if (shown.Panel is { } panel) // M26
        {
            PanelModel ??= new FolderPanelModel(Key);
            PanelModel.Apply(panel);
        }
        else PanelModel = null;
        Fills = shown.Fill;
        State = shown.State;
        if (OwnName is not null) Label = OwnName;
        else if (reload || Label.Length == 0) Label = PlaceholderName(Target);
        else Changed(nameof(ToolTipText));
        return reload;
    }

    /// <summary>
    /// Until Windows' display name arrives: the file name, a website's host, an app's id front (an uninstalled app is never
    /// named by Windows, M19); nothing for other special items.
    /// </summary>
    private static string PlaceholderName(string target) => ItemKinds.Of(target) switch
    {
        ItemKind.Website => ItemKinds.WebsiteName(target),
        _ when ItemKinds.AppIdOf(target) is { } appId => ItemKinds.AppName(appId), // a program's id holds its path (M20)
        ItemKind.Special => "",
        ItemKind.Widget => Widgets.NameOfTarget(target), // M25; M28: an unknown kind too
        _ => Path.GetFileNameWithoutExtension(target.TrimEnd('\\')) is { Length: > 0 } name ? name : target,
    };

    /// <summary>The folder panel this element shows (M26), or null.</summary>
    public FolderPanelModel? PanelModel { get; private set { if (field == value) return; field = value; Changed(); Changed(nameof(IsPanel)); } }

    public bool IsPanel => PanelModel is not null;

    /// <summary>A panel that takes the whole fence (M26): the window sizes it to the fence instead of its span.</summary>
    public bool Fills { get; private set; }

    /// <summary>A filling panel's size: the fence's list area less the cell padding (M26).</summary>
    public void ApplyFill(double width, double height) => (ContentWidth, WidgetWidth, WidgetHeight) = (width, width, height);

    /// <summary>The widget this element is (M25), or null for an item.</summary>
    public WidgetKind? Widget { get; private set { field = value; Changed(); Changed(nameof(IsWidget)); Changed(nameof(WidgetKindName)); } }

    public bool IsWidget => Widget is not null;

    /// <summary>"Clock", "Date" or "Stats": the template shows that layout.</summary>
    public string WidgetKindName => Widget?.ToString() ?? "";

    public WidgetOptions Options { get; private set; } = new();

    /// <summary>The clock's time, or the date page's weekday (M25).</summary>
    public string WidgetMain { get; private set { field = value; Changed(); } } = "";

    /// <summary>The clock's date line, or the date page's day number.</summary>
    public string WidgetSub { get; private set { field = value; Changed(); } } = "";

    /// <summary>The date page's month and year.</summary>
    public string WidgetFoot { get; private set { field = value; Changed(); } } = "";

    /// <summary>The stats widget's tiles (M31): CPU, CPU TEMP, GPU, GPU TEMP, RAM.</summary>
    public IReadOnlyList<StatTile> StatTiles { get; private set { field = value; Changed(); } } = [];

    /// <summary>The widget's area in DIPs: its span's cells less the cell padding (no label under a widget).</summary>
    public double WidgetWidth { get; private set { field = value; Changed(); } } = 160;
    public double WidgetHeight { get; private set { field = value; Changed(); } } = 84;

    /// <summary>Shows the widget as of <paramref name="now"/> (M25); stats from the last reading (null rows show "—").</summary>
    public void RenderWidget(DateTime now, System.Globalization.CultureInfo culture, StatsSample? stats)
    {
        switch (Widget)
        {
            case WidgetKind.Clock:
                WidgetMain = Widgets.ClockText(now, Options.Seconds, culture);
                WidgetSub = Options.Date ? Widgets.ClockDateLine(now, culture) : ""; // M31: "Monday, 5 October"
                break;
            case WidgetKind.Date:
                var page = Widgets.Page(now, culture);
                (WidgetMain, WidgetSub, WidgetFoot) = (page.Weekday, page.Day, page.MonthYear);
                break;
            case WidgetKind.Stats:
                var tiles = Widgets.Tiles(stats, Options.Fahrenheit, culture); // M31
                if (!tiles.SequenceEqual(StatTiles)) StatTiles = tiles; // M28: rebuilt only when a value changed
                break;
        }
    }

    /// <summary>Its size in the fence's cells (M24).</summary>
    public GridSpan Span { get; private set; } = GridSpan.One;

    /// <summary>Its stored cell (M24, Free fences), or null.</summary>
    public GridCell? StoredCell { get; private set; }

    /// <summary>Width of its content (icon or tile and the label), in DIPs: its span's cells less the cell padding (M24).</summary>
    public double ContentWidth { get; private set { field = value; Changed(); } } = 68;

    /// <summary>Its icon's size in DIPs (M24): the fence's icon size at 1×1, bigger to fill a bigger span (≤ 256).</summary>
    public double IconDips { get; private set { field = value; Changed(); } } = 48;

    public double TileWidth { get; private set { field = value; Changed(); } } = 72;
    public double TileHeight { get; private set { field = value; Changed(); } } = 108;

    /// <summary>
    /// Sizes its content for its span on cells of this size (M24, spec §1). True when the icon size changed (the icon is
    /// loaded again at the new size).
    /// </summary>
    public bool ApplySize(double cellWidth, double cellHeight, double iconDips, double labelHeight)
    {
        const double CellPaddingX = 8, CellPaddingY = 12, MaxIcon = 256;
        var width = Span.Columns * cellWidth - CellPaddingX;
        var height = Span.Rows * cellHeight - CellPaddingY - labelHeight;
        var icon = Span == GridSpan.One ? iconDips : Math.Clamp(Math.Floor(Math.Min(width - 8, height)), iconDips, MaxIcon);
        var tileHeight = Math.Max(16, Math.Floor(Math.Min(height, (width - 4) * 1.5)));
        (WidgetWidth, WidgetHeight) = (width, Span.Rows * cellHeight - CellPaddingY); // M25: no label under a widget (M26: a panel's area too)
        var changed = Math.Abs(icon - IconDips) > 0.5;
        (ContentWidth, IconDips, TileWidth, TileHeight) = (width, icon, Math.Floor(tileHeight / 1.5), tileHeight);
        return changed;
    }

    /// <summary>Counts icon requests (UI thread): a slower, older load (another size or target) never wins (final review I4).</summary>
    public int IconRequest { get; set; }

    public ImageSource? Icon
    {
        get;
        set { field = value; Changed(); }
    }

    /// <summary>The host wants a tile for this item (M22: a game item's cover), and its art; the window decides <see cref="IsTile"/>.</summary>
    public bool Tile { get; private set; }

    public (string Path, bool IsPoster)? TileArt { get; private set; }

    public bool IsGame { get; private set; }

    /// <summary>A Game Library tile (M12): a 2:3 tile with a poster, a logo or the icon centred.</summary>
    public bool IsTile
    {
        get;
        set { field = value; Changed(); }
    }

    /// <summary>The tile's poster or logo, once loaded.</summary>
    public ImageSource? Art
    {
        get;
        set { field = value; Changed(); }
    }

    /// <summary>"Poster" (fills the tile), "Logo" (centred) or "None" (the icon).</summary>
    public string ArtKind
    {
        get;
        set { field = value; Changed(); }
    } = "None";

    /// <summary>The art file requested last (UI thread): a slower, older load never wins.</summary>
    public string? ArtPath { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
