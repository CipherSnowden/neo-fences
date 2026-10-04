namespace NeoFences.Core.Model;

/// <summary>How a fence shows its colour (M14, user choice: all three, as a preference).</summary>
public enum ColourStyle { AccentEdge, TintedGlass, TitleStrip }

public enum TitleWeight { Regular, SemiBold, Bold }

/// <summary>A title font (M14), Settings → Appearance; one for every fence (v1.7.1, ADR-038). The normalizer fills every part.</summary>
public sealed record TitleFont(string? Family, int? Size, TitleWeight? Weight)
{
    /// <summary>Small, Normal, Large, Huge.</summary>
    public static IReadOnlyList<int> Sizes { get; } = [12, 14, 17, 20];
}

/// <summary>Settings → Appearance (M14, spec 2026-10-04-appearance-design §2). The defaults are v1.6's look.</summary>
public sealed record AppearanceSettings
{
    public const int MaxStrength = 85;

    /// <summary>Veil strength in % while Windows apps are dark; 0 = Clear (v1.6's dark look).</summary>
    public int StrengthDark { get; init; }

    /// <summary>Veil strength in % while Windows apps are light; 72 = v1.6's light veil.</summary>
    public int StrengthLight { get; init; } = 72;

    public ColourStyle ColourStyle { get; init; } = ColourStyle.AccentEdge;

    /// <summary>Fences without a colour of their own take the wallpaper's colour.</summary>
    public bool WallpaperAccent { get; init; }

    public TitleFont TitleFont { get; init; } = new("Segoe UI", 14, Model.TitleWeight.SemiBold);
}
