using NeoFences.Core.Config;
using NeoFences.Core.Model;

namespace NeoFences.Core.Items;

/// <summary>How a folder panel shows its entries (M26): rows with columns, rows with names, or icon tiles.</summary>
public enum PanelLook { Details, List, Icons }

/// <summary>A folder panel's sort key (M26): a header click or Sort by ▸.</summary>
public enum PanelSort { Name, Date, Type, Size }

/// <summary>
/// A folder item shown as a panel (M26, spec 2026-10-05-folder-panel-design §1): its look and what it shows of its folder
/// (the folder is the item's target). Read-only: NeoFences never writes to the folder.
/// </summary>
public sealed record FolderPanel
{
    public PanelLook Look { get; init; } = PanelLook.Details;
    public ViewShow Show { get; init; } = ViewShow.All;

    /// <summary>File-name patterns, e.g. "*.png;*.jpg"; empty = everything (subfolders always show when showing all).</summary>
    public string Patterns { get; init; } = "";

    /// <summary>Only the newest N entries (by date), then sorted; null = all of them.</summary>
    public int? Newest { get; init; }

    public PanelSort Sort { get; init; } = PanelSort.Name;
    public bool Descending { get; init; }
}

/// <summary>
/// Where a panel is browsing (M26 spec §2): the shown folder and the way back. Never saved: each start, and Home, show
/// the panel's own folder. Up never goes above it.
/// </summary>
public sealed record PanelPlace(string Current, IReadOnlyList<string> History)
{
    public static PanelPlace At(string folder) => new(folder, []);

    public bool CanGoBack => History.Count > 0;

    public bool CanGoUp(string home) => IsBelow(Current, home);

    /// <summary>A subfolder double-clicked: shown, and Back returns here.</summary>
    public PanelPlace Into(string folder) => new(folder, [.. History, Current]);

    public PanelPlace Back() => History.Count == 0 ? this : new(History[^1], [.. History.Take(History.Count - 1)]);

    public PanelPlace Up(string home) =>
        CanGoUp(home) && Path.GetDirectoryName(FolderViews.ListedFolder(Current)) is { } parent ? Into(parent) : this;

    public PanelPlace Home(string home) => FolderViews.SameFolder(Current, home) ? this : Into(home);

    /// <summary>A real subfolder of <paramref name="home"/> (any depth), not the folder itself or a neighbour that starts alike.</summary>
    public static bool IsBelow(string folder, string home)
    {
        var parent = FolderViews.ListedFolder(home).TrimEnd('\\', '/') + "\\";
        return folder.Length > parent.Length && folder.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The folder panel element (M26, ADR-048): a folder item with <see cref="VirtualItem.Panel"/> set shows its folder live,
/// inside any fence, beside items and widgets. Pure; the listing comes from NeoFences.Shell.
/// </summary>
public static class FolderPanels
{
    /// <summary>A new panel's size: room for a dozen rows.</summary>
    public static GridSpan DefaultSpan { get; } = new(4, 4);

    public static bool IsPanel(VirtualItem item) => item.Panel is not null && item.Kind == ItemKind.Path;

    /// <summary>A panel of <paramref name="folder"/>: busy folders (Downloads, Screenshots) newest first with the newest 30, the rest by name.</summary>
    public static VirtualItem Create(string folder, IReadOnlyList<string> busyFolders) =>
        VirtualItem.Create(folder) with
        {
            Panel = busyFolders.Any(busy => FolderViews.SameFolder(busy, folder))
                ? new FolderPanel { Sort = PanelSort.Date, Descending = true, Newest = FolderViews.BusyFolderNewest }
                : new FolderPanel(),
        };

    /// <summary>A folder view's settings as a panel (the migration): it looked like icon tiles, its date sort newest first.</summary>
    public static FolderPanel FromView(FolderView view) => new()
    {
        Look = PanelLook.Icons,
        Show = view.Show,
        Patterns = view.Patterns,
        Newest = view.Newest,
        Sort = view.Sort switch { FenceSort.Date => PanelSort.Date, FenceSort.Type => PanelSort.Type, _ => PanelSort.Name },
        Descending = view.Sort == FenceSort.Date,
    };

    /// <summary>A hand-edited panel repaired: known looks and sorts, a count within 1–500, valid patterns.</summary>
    public static FolderPanel? Normalize(FolderPanel? panel) => panel is null ? null : panel with
    {
        Look = Enum.IsDefined(panel.Look) ? panel.Look : PanelLook.Details,
        Show = Enum.IsDefined(panel.Show) ? panel.Show : ViewShow.All,
        Sort = Enum.IsDefined(panel.Sort) ? panel.Sort : PanelSort.Name,
        Newest = panel.Newest is >= 1 and var newest ? Math.Min(newest, FolderViews.MaxNewest) : null,
        Patterns = FolderViews.ParsePatterns(panel.Patterns) is not null ? panel.Patterns ?? "" : "",
    };

    /// <summary>
    /// What the panel shows of a listing: the chosen kind, the patterns, the newest N, the sort, at most
    /// <see cref="FolderViews.MaxShown"/> entries.
    /// </summary>
    public static FolderViews.Selection Select(IReadOnlyList<ItemInfo> entries, FolderPanel panel)
    {
        var patterns = FolderViews.ParsePatterns(panel.Patterns) ?? [];
        IEnumerable<ItemInfo> matching = entries.Where(entry => panel.Show switch
        {
            ViewShow.Files => !entry.IsFolder,
            ViewShow.Folders => entry.IsFolder,
            _ => true,
        });
        // Subfolders pass the patterns: a "*.png" panel of Screenshots still shows its game folders.
        if (patterns.Count > 0)
            matching = matching.Where(entry => entry.IsFolder || patterns.Any(pattern => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, entry.Name, ignoreCase: true)));
        if (panel.Newest is { } newest) matching = matching.OrderByDescending(entry => entry.Modified).Take(newest);
        var ordered = Order(matching, panel.Sort, panel.Descending);
        return new FolderViews.Selection([.. ordered.Take(FolderViews.MaxShown)], Math.Max(0, ordered.Count - FolderViews.MaxShown), entries.Count);
    }

    /// <summary>
    /// Name and Type keep folders first in both directions (like Explorer); Date and Size mix them (a folder has no size: it
    /// counts as the smallest). Ties (same type, date or size) go by name, A→Z.
    /// </summary>
    private static List<ItemInfo> Order(IEnumerable<ItemInfo> entries, PanelSort sort, bool descending)
    {
        var byName = ItemSorting.NaturalComparer.Instance;
        IOrderedEnumerable<ItemInfo> Key<TKey>(IEnumerable<ItemInfo> source, Func<ItemInfo, TKey> key, IComparer<TKey>? comparer = null) =>
            descending ? source.OrderByDescending(key, comparer) : source.OrderBy(key, comparer);
        IOrderedEnumerable<ItemInfo> ThenKey<TKey>(IOrderedEnumerable<ItemInfo> source, Func<ItemInfo, TKey> key, IComparer<TKey>? comparer = null) =>
            descending ? source.ThenByDescending(key, comparer) : source.ThenBy(key, comparer);
        var foldersFirst = entries.OrderBy(entry => !entry.IsFolder);
        return (sort switch
        {
            PanelSort.Type => ThenKey(foldersFirst, entry => entry.TypeName, StringComparer.CurrentCultureIgnoreCase).ThenBy(entry => entry.Name, byName),
            PanelSort.Date => Key(entries, entry => entry.Modified).ThenBy(entry => entry.Name, byName),
            PanelSort.Size => Key(entries, entry => entry.IsFolder ? -1 : entry.Size ?? 0).ThenBy(entry => entry.Name, byName),
            _ => ThenKey(foldersFirst, entry => entry.Name, byName),
        }).ToList();
    }

    /// <summary>A column header clicked: the same column again reverses it; another starts in its natural direction.</summary>
    public static FolderPanel HeaderSort(FolderPanel panel, PanelSort clicked) =>
        clicked == panel.Sort ? panel with { Descending = !panel.Descending }
            : panel with { Sort = clicked, Descending = clicked is PanelSort.Date or PanelSort.Size };

    /// <summary>
    /// The panel's header: its own name (or its folder's), and while it browses below its folder the path down to the
    /// shown one ("GameLibrary › Cyberpunk › bin").
    /// </summary>
    public static string HeaderOf(string home, string shown, string? ownName)
    {
        var name = ownName ?? FolderViews.NameOf(home);
        if (!PanelPlace.IsBelow(shown, home)) return name;
        var below = Path.GetRelativePath(FolderViews.ListedFolder(home), FolderViews.ListedFolder(shown));
        return string.Join(" › ", [name, .. below.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)]);
    }

    /// <summary>A file's size as Explorer's Details shows it: whole kilobytes, rounded up; nothing for a folder.</summary>
    public static string SizeText(long? bytes, System.Globalization.CultureInfo culture) =>
        bytes is { } size ? $"{Math.Ceiling(size / 1024.0).ToString("N0", culture)} KB" : "";

    /// <summary>Details columns for a panel this many cells wide: Name and Date up to 2, all four from 3.</summary>
    public static int Columns(int spanColumns) => spanColumns <= 2 ? 2 : 4;

    /// <summary>
    /// "Fill fence": the panel takes the whole fence while it is the fence's only element; beside anything else it sits on
    /// cells (its own size, or 4×4).
    /// </summary>
    public static bool Fills(IReadOnlyList<VirtualItem> fenceItems) => fenceItems is [{ Fill: true } only] && IsPanel(only);

    /// <param name="MigratedFenceIds">Fences that were folder views and now hold one panel.</param>
    public sealed record Migration(NeoFencesConfig Config, ItemsDocument Items, IReadOnlyList<string> MigratedFenceIds);

    /// <summary>
    /// Every folder view becomes an items fence holding one filling panel of its folder (spec §1). Run again after a save
    /// cut short between the two files (the items saved, the config not), it adds no second panel.
    /// </summary>
    public static Migration MigrateViews(NeoFencesConfig config, ItemsDocument items)
    {
        var views = config.Fences.Where(fence => fence.View is not null).ToList();
        foreach (var fence in views)
        {
            var view = fence.View!;
            if (!items.Of(fence.Id).Any(item => IsPanel(item) && FolderViews.SameFolder(item.Target, view.Path)))
                items = items.With(fence.Id, [.. items.Of(fence.Id), VirtualItem.Create(view.Path) with { Panel = FromView(view), Fill = true }]);
            config = config.WithFence(fence with { View = null });
        }
        return new Migration(config, items, [.. views.Select(fence => fence.Id)]);
    }
}
