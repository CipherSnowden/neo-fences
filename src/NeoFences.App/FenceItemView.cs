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
public sealed record ShownItem(string Key, string Target, string? Name = null, ItemIcon? Icon = null, string? Note = null,
    TargetState State = TargetState.Ok, bool Tile = false, (string Path, bool IsPoster)? TileArt = null, bool IsGame = false,
    GridSpan? Span = null, GridCell? Cell = null);

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
        Span = shown.Span ?? (shown.Tile ? new GridSpan(1, 2) : GridSpan.One);
        StoredCell = shown.Cell;
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
        _ => Path.GetFileNameWithoutExtension(target.TrimEnd('\\')) is { Length: > 0 } name ? name : target,
    };

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
