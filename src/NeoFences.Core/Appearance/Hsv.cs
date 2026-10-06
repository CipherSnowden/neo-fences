namespace NeoFences.Core.Appearance;

/// <summary>
/// Hue (degrees, wraps), saturation and value (0–1, clamped) for the custom colour picker (M35, spec §3): the field is
/// saturation × value at the chosen hue. Pure.
/// </summary>
public readonly record struct Hsv(double H, double S, double V)
{
    public static Hsv FromArgb(Argb colour)
    {
        double red = colour.R / 255.0, green = colour.G / 255.0, blue = colour.B / 255.0;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var range = max - min;
        var hue = range == 0 ? 0
            : max == red ? 60 * ((green - blue) / range % 6)
            : max == green ? 60 * ((blue - red) / range + 2)
            : 60 * ((red - green) / range + 4);
        return new Hsv(hue < 0 ? hue + 360 : hue, max == 0 ? 0 : range / max, max);
    }

    /// <summary>An opaque colour; the hue wraps (−30° = 330°), saturation and value are clamped to 0–1.</summary>
    public Argb ToArgb()
    {
        var hue = (H % 360 + 360) % 360;
        double saturation = Math.Clamp(S, 0, 1), value = Math.Clamp(V, 0, 1);
        var chroma = value * saturation;
        var second = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var match = value - chroma;
        var (red, green, blue) = (int)(hue / 60) switch
        {
            0 => (chroma, second, 0.0),
            1 => (second, chroma, 0.0),
            2 => (0.0, chroma, second),
            3 => (0.0, second, chroma),
            4 => (second, 0.0, chroma),
            _ => (chroma, 0.0, second),
        };
        return new Argb(0xFF, Byte(red + match), Byte(green + match), Byte(blue + match));
    }

    private static byte Byte(double channel) => (byte)Math.Round(Math.Clamp(channel, 0, 1) * 255);
}
