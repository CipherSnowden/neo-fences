using System.Globalization;

namespace NeoFences.Core.Appearance;

/// <summary>A colour with alpha (M14); Core's own type so the look resolver needs no WPF.</summary>
public readonly record struct Argb(byte A, byte R, byte G, byte B)
{
    /// <summary>"#RRGGBB" (any case) as an opaque colour; anything else is null.</summary>
    public static Argb? FromHex(string? hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#'
            || !uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return null;
        return new Argb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    /// <summary>What people type in the picker's hex box (M35): "#E23A50", "e23a50", "#abc" (shorthand), spaces around; else null.</summary>
    public static Argb? FromUserHex(string? text)
    {
        var hex = text?.Trim() ?? "";
        if (hex.StartsWith('#')) hex = hex[1..];
        if (hex.Length == 3) hex = string.Concat(hex.Select(digit => new string(digit, 2)));
        return hex.Length == 6 && hex.All(Uri.IsHexDigit) ? FromHex("#" + hex) : null;
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>This colour moved <paramref name="amount"/> (0–1) of the way to <paramref name="other"/>; alpha stays.</summary>
    public Argb Mix(Argb other, double amount) =>
        new(A, Lerp(R, other.R, amount), Lerp(G, other.G, amount), Lerp(B, other.B, amount));

    private static byte Lerp(byte from, byte to, double amount) => (byte)Math.Round(from + (to - from) * amount);

    /// <summary>Relative luminance (sRGB, 0–1), ignoring alpha.</summary>
    public double Luminance => 0.2126 * Linear(R) + 0.7152 * Linear(G) + 0.0722 * Linear(B);

    private static double Linear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
