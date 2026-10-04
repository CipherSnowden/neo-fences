namespace NeoFences.Core.Appearance;

/// <summary>The wallpaper's colour (M14, spec §4): the strongest vivid hue of a small decoded image. Pure.</summary>
public static class AccentColor
{
    private const int HueBuckets = 36;
    private const double MinimumLightness = 0.40, MaximumLightness = 0.65;
    private const double MinimumSaturation = 0.45; // a muted wallpaper (snow, fog) still gives a colour, not grey

    /// <summary>
    /// Pixels as BGRA bytes (WPF's Bgra32). Near-black, near-white and grey pixels are ignored; the rest vote by hue,
    /// weighted by saturation × value. Null when the image has no real colour (the caller falls back to Windows' accent).
    /// </summary>
    public static Argb? FromPixels(ReadOnlySpan<byte> bgra)
    {
        var weights = new double[HueBuckets];
        var sums = new double[HueBuckets, 3];
        var pixelCount = bgra.Length / 4;
        for (var offset = 0; offset + 3 < bgra.Length; offset += 4)
        {
            double blue = bgra[offset] / 255.0, green = bgra[offset + 1] / 255.0, red = bgra[offset + 2] / 255.0;
            var max = Math.Max(red, Math.Max(green, blue));
            var min = Math.Min(red, Math.Min(green, blue));
            var saturation = max == 0 ? 0 : (max - min) / max;
            if (max < 0.2 || saturation < 0.25) continue; // near-black or grey (also near-white)
            var hue = Hue(red, green, blue, max, min);
            var bucket = (int)(hue / (360.0 / HueBuckets)) % HueBuckets;
            var weight = saturation * max;
            weights[bucket] += weight;
            sums[bucket, 0] += red * weight;
            sums[bucket, 1] += green * weight;
            sums[bucket, 2] += blue * weight;
        }
        var best = Array.IndexOf(weights, weights.Max());
        if (pixelCount == 0 || weights[best] < pixelCount * 0.005) return null;
        var (r, g, b) = (sums[best, 0] / weights[best], sums[best, 1] / weights[best], sums[best, 2] / weights[best]);
        return ClampLightness(r, g, b);
    }

    private static double Hue(double red, double green, double blue, double max, double min)
    {
        var delta = max - min;
        if (delta == 0) return 0;
        var hue = max == red ? 60 * ((green - blue) / delta % 6)
                : max == green ? 60 * ((blue - red) / delta + 2)
                : 60 * ((red - green) / delta + 4);
        return hue < 0 ? hue + 360 : hue;
    }

    /// <summary>Through HSL: the same hue, saturation at least <see cref="MinimumSaturation"/>, lightness readable as a border or a tint.</summary>
    private static Argb ClampLightness(double red, double green, double blue)
    {
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var lightness = (max + min) / 2;
        var saturation = max == min ? 0 : (max - min) / (1 - Math.Abs(2 * lightness - 1));
        var hue = Hue(red, green, blue, max, min);
        lightness = Math.Clamp(lightness, MinimumLightness, MaximumLightness);
        var chroma = (1 - Math.Abs(2 * lightness - 1)) * Math.Clamp(saturation, MinimumSaturation, 1);
        var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var (r1, g1, b1) = (hue / 60) switch
        {
            < 1 => (chroma, x, 0.0), < 2 => (x, chroma, 0.0), < 3 => (0.0, chroma, x),
            < 4 => (0.0, x, chroma), < 5 => (x, 0.0, chroma), _ => (chroma, 0.0, x),
        };
        var m = lightness - chroma / 2;
        return new Argb(0xFF, Byte(r1 + m), Byte(g1 + m), Byte(b1 + m));
    }

    private static byte Byte(double channel) => (byte)Math.Round(Math.Clamp(channel, 0, 1) * 255);
}
