namespace NeoFences.Core.Model;

/// <summary>"Sort by" in the fence menu: a one-time reorder of the fence's items (M4; M18: virtual items).</summary>
public enum FenceSort { Manual, Name, Type, Date }

/// <summary>Item names under the icons: always, or only for the hovered or selected item (icon-only fences, M8b).</summary>
public enum LabelMode { Always, OnHover }

/// <summary>
/// One fence: its look and behaviour. Its virtual items live in <c>items.json</c> under its id (ADR-041); the Game Library
/// fence (<see cref="IsLibrary"/>) shows NeoFences' own game shortcuts instead.
/// </summary>
public sealed record Fence
{
    public required string Id { get; init; }
    public required string Title { get; init; }

    /// <summary>The Game Library fence (M12): at most one; it lists the library folder, never virtual items.</summary>
    public bool IsLibrary { get; init; }

    /// <summary>A folder view (M21): the fence shows this folder live instead of virtual items. Never set on the Library.</summary>
    public FolderView? View { get; init; }

    /// <summary>Its auto-collect rules (M27): new matching files of their folders become items here. Empty: none.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<Items.CollectRule> Collect { get; init; } = [];

    /// <summary>config.json's "collect": written only when the fence has rules.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("collect"), System.Text.Json.Serialization.JsonInclude]
    private IReadOnlyList<Items.CollectRule>? CollectJson { get => Collect.Count == 0 ? null : Collect; init => Collect = value ?? []; }

    /// <summary>How its elements sit (M24): packed in order, or at fixed positions.</summary>
    public Items.FenceLayout Layout { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public FenceKind Kind => IsLibrary ? FenceKind.Library : View is not null ? FenceKind.View : FenceKind.Items;

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

    /// <summary>Its own look (M36, Fence settings…); null: like all fences (Settings → Appearance).</summary>
    public OwnLook? Look { get; init; }

    /// <summary>The first-run welcome (M30, ADR-051): only a fresh start's first fence; cleared by its first item.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool Welcome { get; init; }

    public static Fence Create(string title, bool isLibrary = false) => new() { Id = NewId(), Title = title, IsLibrary = isLibrary };

    public static string NewId() => Guid.NewGuid().ToString("N");
}
