namespace NeoFences.Core.Model;

/// <summary>Library (M12): NeoFences' own game library folder, shown like a Portal.</summary>
public enum FenceSourceKind { Desktop, Portal, Library }

/// <summary>Where a fence's items come from: the desktop (Takeover) or a folder (Portal).</summary>
public sealed record FenceSource(FenceSourceKind Kind, string? Path = null)
{
    public static FenceSource Desktop { get; } = new(FenceSourceKind.Desktop);

    public static FenceSource Portal(string folderPath) => new(FenceSourceKind.Portal, folderPath);

    public static FenceSource Library { get; } = new(FenceSourceKind.Library);
}

public enum FenceSort { Manual, Name, Type, Date }

/// <summary>Item names under the icons: always, or only for the hovered or selected item (icon-only fences, M8b).</summary>
public enum LabelMode { Always, OnHover }

/// <summary>
/// One fence. Desktop fences keep an ordered list of item refs (shell parsing names: file paths or
/// "::{GUID}" for virtual items). Portal fences keep no items; they mirror <see cref="FenceSource.Path"/>.
/// </summary>
public sealed record Fence
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public FenceSource Source { get; init; } = FenceSource.Desktop;
    public IReadOnlyList<string> Items { get; init; } = [];
    public bool IsInbox { get; init; }
    public FenceSort Sort { get; init; } = FenceSort.Manual;
    public int IconSize { get; init; } = 48;
    public bool RolledUp { get; init; }
    public bool Locked { get; init; }
    public LabelMode Labels { get; init; } = LabelMode.Always;

    /// <summary>Only on a box's host (M9): every tab of the box in order, itself included. Empty = not a box.</summary>
    public IReadOnlyList<string> Tabs { get; init; } = [];

    /// <summary>On a box's host (M9): the tab shown; null or unknown = the first.</summary>
    public string? ActiveTab { get; init; }

    /// <summary>This fence's tab accent (M9), or none.</summary>
    public TabColor? TabColor { get; init; }

    /// <summary>Any colour as "#RRGGBB" (M14, fence menu → Colour → Custom…); wins over <see cref="TabColor"/>.</summary>
    public string? CustomColor { get; init; }

    public static Fence Create(string title, FenceSource? source = null) =>
        new() { Id = NewId(), Title = title, Source = source ?? FenceSource.Desktop };

    public static string NewId() => Guid.NewGuid().ToString("N");
}
