using System.Globalization;
using System.IO.Enumeration;
using NeoFences.Core.Model;

namespace NeoFences.Core.Items;

/// <summary>
/// What a folder view shows of its folder's listing (M21, spec 2026-10-05-folder-views-design §1): the chosen kind, the
/// patterns, the newest N, the sort, at most <see cref="MaxShown"/> entries. Pure; the listing comes from NeoFences.Shell.
/// </summary>
public static class FolderViews
{
    public const int MaxShown = 500;
    public const int MaxNewest = 500;

    /// <summary>Downloads and Screenshots start newest first with this many entries.</summary>
    public const int BusyFolderNewest = 30;

    /// <param name="Shown">Entry paths in the order shown.</param>
    /// <param name="Hidden">Entries the view would show but the cap leaves out ("+ N more").</param>
    /// <param name="Listed">Entries in the folder before any filter ("This folder is empty").</param>
    public sealed record Selection(IReadOnlyList<string> Shown, int Hidden, int Listed);

    public static Selection Select(IReadOnlyList<ItemInfo> entries, FolderView view)
    {
        var patterns = ParsePatterns(view.Patterns) ?? [];
        IEnumerable<ItemInfo> matching = entries.Where(entry => view.Show switch
        {
            ViewShow.Files => !entry.IsFolder,
            ViewShow.Folders => entry.IsFolder,
            _ => true,
        });
        // Subfolders pass the patterns: a "*.png" view of Screenshots still shows its game folders.
        if (patterns.Count > 0)
            matching = matching.Where(entry => entry.IsFolder || patterns.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, entry.Name, ignoreCase: true)));
        if (view.Newest is { } newest) matching = matching.OrderByDescending(entry => entry.Modified).Take(newest);
        var ordered = ItemSorting.Order(matching, view.Sort == FenceSort.Manual ? FenceSort.Name : view.Sort);
        return new Selection([.. ordered.Take(MaxShown)], Math.Max(0, ordered.Count - MaxShown), entries.Count);
    }

    /// <summary>
    /// "*.png; .jpg, txt" → ["*.png", "*.jpg", "*.txt"]: split on ; and ,; a bare extension becomes "*.ext". Null when a
    /// part is not a file-name pattern (a path, or a character Windows forbids in names).
    /// </summary>
    public static IReadOnlyList<string>? ParsePatterns(string? patterns)
    {
        var parts = (patterns ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parsed = new List<string>();
        foreach (var part in parts)
        {
            if (part.IndexOfAny(['\\', '/', ':', '"', '<', '>', '|']) >= 0) return null;
            parsed.Add(part.Contains('*') || part.Contains('?') ? part
                : part.StartsWith('.') ? "*" + part
                : part.Contains('.') ? part
                : "*." + part);
        }
        return parsed;
    }

    /// <summary>A new view of <paramref name="path"/>: busy folders (Downloads, Screenshots) newest first, the rest by name.</summary>
    public static FolderView DefaultsFor(string path, IReadOnlyList<string> busyFolders) =>
        busyFolders.Any(busy => SameFolder(busy, path))
            ? new FolderView { Path = path, Sort = FenceSort.Date, Newest = BusyFolderNewest }
            : new FolderView { Path = path };

    /// <summary>A hand-edited view repaired; null when it has no folder (the fence then holds items).</summary>
    public static FolderView? Normalize(FolderView? view)
    {
        if (view is null || string.IsNullOrWhiteSpace(view.Path)) return null;
        return view with
        {
            Show = Enum.IsDefined(view.Show) ? view.Show : ViewShow.All,
            Sort = view.Sort is FenceSort.Name or FenceSort.Type or FenceSort.Date ? view.Sort : FenceSort.Name,
            Newest = view.Newest is >= 1 and var newest ? Math.Min(newest, MaxNewest) : null,
            Patterns = ParsePatterns(view.Patterns) is not null ? view.Patterns ?? "" : "",
        };
    }

    /// <summary>The folder's own name ("Downloads"), the share ("share" of \\nas\share), or the drive ("D:") for a drive root.</summary>
    public static string NameOf(string path) => path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? path;

    public static bool SameFolder(string left, string right) =>
        string.Equals(left.TrimEnd('\\', '/'), right.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The line a view shows instead of (Center) or under (More) its entries: not available, empty, nothing matching, or
    /// "+ N more — Open folder" past the cap.
    /// </summary>
    public static (string? Center, string? More) Status(Selection? selection, FolderView view) =>
        selection is null ? ($"Folder not available: {view.Path}", null)
        : selection.Listed == 0 ? ("This folder is empty", null)
        : selection.Shown.Count == 0 ? ("Nothing here matches this view", null)
        : selection.Hidden > 0 ? (null, $"+ {selection.Hidden.ToString("N0", CultureInfo.InvariantCulture)} more — Open folder")
        : (null, null);
}
