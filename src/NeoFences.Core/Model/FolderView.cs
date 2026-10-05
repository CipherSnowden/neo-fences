namespace NeoFences.Core.Model;

/// <summary>What a folder view lists (M21): everything, only files, or only subfolders.</summary>
public enum ViewShow { All, Files, Folders }

/// <summary>The three kinds of fence (M21): virtual items (items.json), the Game Library, or a folder view.</summary>
public enum FenceKind { Items, Library, View }

/// <summary>
/// A fence that shows one folder live, read-only (M21, ADR-044): NeoFences never writes to the folder. Sort uses the
/// fence's "Sort by" values (Manual is read as Name); <see cref="Newest"/> keeps only the latest N entries by date.
/// </summary>
public sealed record FolderView
{
    public required string Path { get; init; }
    public ViewShow Show { get; init; } = ViewShow.All;
    public FenceSort Sort { get; init; } = FenceSort.Name;

    /// <summary>Only the newest N entries (by date), then sorted; null = all of them.</summary>
    public int? Newest { get; init; }

    /// <summary>File-name patterns, e.g. "*.png;*.jpg"; empty = everything.</summary>
    public string Patterns { get; init; } = "";
}
