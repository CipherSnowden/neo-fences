namespace NeoFences.Core.Model;

/// <summary>Where a fence's title sits in its title bar (M36).</summary>
public enum TitleAlign { Left, Centre, Right }

/// <summary>Room between a fence's cells (M36): the elements keep their size.</summary>
public enum Spacing { Normal, Compact, Roomy }

/// <summary>
/// A fence's own look (M36, ADR-057): each part set here wins over Settings → Appearance; a null part is "Like all fences".
/// The normalizer turns a look with nothing set into none (<see cref="Fence.Look"/> null).
/// </summary>
public sealed record OwnLook
{
    public ColourStyle? ColourStyle { get; init; }

    /// <summary>Background strength in %, one value for light and dark mode (Settings keeps one per mode).</summary>
    public int? Strength { get; init; }

    /// <summary>Each part that is null is the Settings font's part.</summary>
    public TitleFont? TitleFont { get; init; }

    public TitleAlign? TitleAlign { get; init; }

    /// <summary>True: the title bar shows only while the pointer is over the fence. Null: always, like all fences.</summary>
    public bool? TitleOnHover { get; init; }

    public Spacing? Spacing { get; init; }
}
