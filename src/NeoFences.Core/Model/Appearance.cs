namespace NeoFences.Core.Model;

/// <summary>How a fence shows its colour (M14, user choice: all three, as a preference).</summary>
public enum ColourStyle { AccentEdge, TintedGlass, TitleStrip }

public enum TitleWeight { Regular, SemiBold, Bold }

/// <summary>
/// A fence's whole look (M41a, ADR-063): Classic is the look before (its colour styles, Windows' light or dark); the others
/// have a tone of their own and a little life in them (spec 2026-10-11-m41a-feel-design §1).
/// </summary>
public enum FenceTheme { Classic, Aurora, Hud, Neon, LightMica }

/// <summary>How a fence's title is drawn (M41a, spec §2).</summary>
public enum TitleStyle { Simple, Clean, Pill, Caps }

/// <summary>How item names are drawn (M41a, spec §4): a tight shadow, a soft dark pill (ClearType), or plain.</summary>
public enum NameStyle { Crisp, Chips, Plain }

/// <summary>How much moves (M41a, spec §3): off, calm, lively.</summary>
public enum Motion { Lively, Calm, Off }

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

    /// <summary>M41a: the theme of fences without their own (Classic: the look before rc.3, kept for existing setups).</summary>
    public FenceTheme Theme { get; init; }

    /// <summary>M41a: the title style of fences without their own.</summary>
    public TitleStyle TitleStyle { get; init; }

    /// <summary>M41a: how item names are drawn, every fence.</summary>
    public NameStyle NameStyle { get; init; }

    /// <summary>M41a: Off / Calm / Lively; off anyway while gaming or with Windows' animation effects off (MotionPolicy).</summary>
    public Motion Motion { get; init; } = Motion.Lively;

    /// <summary>
    /// M41a (spec §1): how a new install looks — Aurora glass with some body, Clean titles. Existing setups keep the
    /// record's defaults (Classic, Simple): nothing changes under the user's feet.
    /// </summary>
    public static AppearanceSettings NewInstall { get; } = new() { Theme = FenceTheme.Aurora, TitleStyle = TitleStyle.Clean, StrengthDark = 30 };
}

/// <summary>M41a (spec §3): the motion that actually plays.</summary>
public static class MotionPolicy
{
    public static Motion Effective(Motion chosen, bool gameMode, bool windowsAnimations, bool safeMode) =>
        gameMode || !windowsAnimations || safeMode ? Motion.Off : chosen;
}
