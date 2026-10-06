using NeoFences.Core.Model;

namespace NeoFences.Core.Appearance;

/// <summary>A title font with every part decided.</summary>
public sealed record ResolvedTitleFont(string Family, int Size, TitleWeight Weight);

/// <summary>Everything that colours one fence's chrome (M14). Item labels keep the tone's ink (spec §3.3).</summary>
/// <param name="Colour">The fence's colour after the order of spec §3.1, or null (neutral).</param>
/// <param name="TitleStrip">The title bar's background (Title strip style), or null.</param>
/// <param name="Bar">The short bar under a single fence's title (Accent edge style), or null.</param>
/// <param name="Align">Where the title sits (M36).</param>
/// <param name="TitleOnHover">The title bar shows only while the pointer is over the fence (M36).</param>
/// <param name="Spacing">Room between the cells (M36).</param>
public sealed record FenceStyle(
    Argb? Colour, Argb Veil, Argb Border, Argb TitleText, Argb? TitleStrip, Argb? Bar, ResolvedTitleFont Font, double TitleHeight,
    TitleAlign Align = TitleAlign.Left, bool TitleOnHover = false, Spacing Spacing = Spacing.Normal);

/// <summary>Decides how a fence looks (M14, spec 2026-10-04-appearance-design §3). Pure.</summary>
public static class FenceLook
{
    /// <summary>The 8 tab colours (M9), shared by tabs, menus and fences.</summary>
    public static IReadOnlyDictionary<TabColor, Argb> Swatches { get; } = new Dictionary<TabColor, Argb>
    {
        [TabColor.Red] = new(0xFF, 0xE8, 0x48, 0x55), [TabColor.Orange] = new(0xFF, 0xF7, 0x63, 0x0C),
        [TabColor.Yellow] = new(0xFF, 0xFF, 0xB9, 0x00), [TabColor.Green] = new(0xFF, 0x16, 0xC6, 0x0C),
        [TabColor.Teal] = new(0xFF, 0x00, 0xB7, 0xC3), [TabColor.Blue] = new(0xFF, 0x00, 0x78, 0xD4),
        [TabColor.Purple] = new(0xFF, 0x88, 0x64, 0xD8), [TabColor.Pink] = new(0xFF, 0xE3, 0x00, 0x8C),
    };

    private static readonly Argb White = new(0xFF, 0xFF, 0xFF, 0xFF);
    private static readonly Argb Black = new(0xFF, 0x00, 0x00, 0x00);
    private static readonly Argb DarkInk = new(0xFF, 10, 10, 14);      // the dark tone's veil
    private static readonly Argb LightInk = new(0xFF, 0xF2, 0xF2, 0xF2); // the light tone's veil (v1.6)
    private static readonly Argb DarkTitle = new(0xE6, 0x00, 0x00, 0x00); // v1.6 light-mode text

    /// <summary>Tinted glass shows even at Clear: at least 22 % (spec §3.2).</summary>
    private const byte MinimumGlassAlpha = 56;

    /// <param name="light">Windows apps use the light theme.</param>
    /// <param name="wallpaperAccent">The colour of the wallpaper behind this fence, when known.</param>
    public static FenceStyle Resolve(AppearanceSettings appearance, Fence fence, bool light, Argb? wallpaperAccent)
    {
        var own = fence.Look ?? new OwnLook(); // M36: the fence's own values over Settings' (ADR-057)
        var colour = Argb.FromHex(fence.CustomColor)
                     ?? (fence.TabColor is { } swatch && Swatches.TryGetValue(swatch, out var swatchColour) ? swatchColour : (Argb?)null)
                     ?? (appearance.WallpaperAccent ? wallpaperAccent : null);
        var ink = light ? LightInk : DarkInk;
        var veil = ink with { A = Alpha(own.Strength ?? (light ? appearance.StrengthLight : appearance.StrengthDark)) };
        var border = light ? new Argb(0x33, 0, 0, 0) : new Argb(0x40, 0xFF, 0xFF, 0xFF);
        var titleText = light ? DarkTitle : White;
        Argb? strip = null, bar = null;

        if (colour is { } fill)
        {
            switch (own.ColourStyle ?? appearance.ColourStyle)
            {
                case ColourStyle.TintedGlass:
                    veil = fill.Mix(ink, 0.25) with { A = Math.Max(veil.A, MinimumGlassAlpha) };
                    border = fill with { A = 0xB3 };
                    if (veil.A >= 0x80) titleText = ReadableOn(veil); // dense glass: the ink that reads best on it (§3.3)
                    break;
                case ColourStyle.TitleStrip:
                    strip = fill with { A = 0xBF };
                    titleText = ReadableOn(fill.Mix(ink, 0.25 * veil.A / 255.0)); // the strip as seen over the tone's veil
                    break;
                default: // Accent edge
                    border = fill;
                    bar = fill;
                    titleText = light ? fill.Mix(Black, 0.35) : fill.Mix(White, 0.4); // the colour, readable on the tone
                    // A pale custom colour on the light veil, or a dark one on the dark tone: pushed further until it reads (M16).
                    // 0.30 on dark keeps every swatch exactly as in v1.7 (Pink was 0.31: final review M1) and stays above 6:1.
                    for (var step = 1; step <= 10 && (light ? titleText.Luminance > 0.25 : titleText.Luminance < 0.30); step++)
                        titleText = light ? fill.Mix(Black, 0.35 + 0.065 * step) : fill.Mix(White, 0.4 + 0.06 * step);
                    break;
            }
        }

        // Settings' title font, part by part under the fence's own (M36, ADR-057: back in Fence settings…, not the menu).
        var global = appearance.TitleFont;
        var mine = own.TitleFont;
        var font = new ResolvedTitleFont(mine?.Family ?? global.Family ?? "Segoe UI", mine?.Size ?? global.Size ?? 14, mine?.Weight ?? global.Weight ?? TitleWeight.SemiBold);
        return new FenceStyle(colour, veil, border, titleText, strip, bar, font, TitleHeightFor(font.Size),
            own.TitleAlign ?? TitleAlign.Left, own.TitleOnHover == true, own.Spacing ?? Spacing.Normal);
    }

    /// <summary>
    /// Dark or white title ink for a background. WCAG contrast ties at luminance ≈ 0.18; near the tie white reads better on
    /// saturated blues, purples and reds (Windows itself puts white on #0078D4), so dark ink starts at 0.22.
    /// Final review I4: a 0.5 threshold gave white at ~2.3:1 on green, teal and many wallpaper accents.
    /// </summary>
    private static Argb ReadableOn(Argb background) => background.Luminance > 0.22 ? DarkTitle : White;

    /// <summary>
    /// The padding around each cell's element (M36): the room between elements; the elements keep their size. Normal is the
    /// look before M36.
    /// </summary>
    public static double CellInset(Spacing spacing) => spacing switch { Spacing.Compact => 0, Spacing.Roomy => 8, _ => 2 };

    /// <summary>The title row grows with the font so bigger titles are never clipped: 26 / 30 / 34 / 38 DIP.</summary>
    public static double TitleHeightFor(int size) => size switch { <= 12 => 26, <= 14 => 30, <= 17 => 34, _ => 38 };

    private static byte Alpha(int strengthPercent) =>
        (byte)Math.Round(Math.Clamp(strengthPercent, 0, AppearanceSettings.MaxStrength) * 255 / 100.0);
}
