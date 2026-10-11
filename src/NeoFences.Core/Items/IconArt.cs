namespace NeoFences.Core.Items;

/// <summary>
/// rc.6 (the owner's icons): Windows hands out a 256-px icon either as real large art or as a small icon (48 px or less)
/// centred in an empty canvas. Real large art scaled down is smoother than a rough 48-px frame (Redragon's); a small icon
/// is better taken at its own size.
/// </summary>
public static class IconArt
{
    /// <summary>True when the visible art (alpha over 0) spans at least 60 % of the canvas on its longer side.</summary>
    /// <param name="bgraPixels">Width × height pixels, 4 bytes each, alpha last.</param>
    public static bool IsLarge(byte[] bgraPixels, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgraPixels.Length < width * height * 4) return false;
        int left = width, right = -1, top = height, bottom = -1;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if (bgraPixels[(y * width + x) * 4 + 3] == 0) continue;
            (left, right, top, bottom) = (Math.Min(left, x), Math.Max(right, x), Math.Min(top, y), Math.Max(bottom, y));
        }
        if (right < 0) return false;
        return Math.Max(right - left + 1, bottom - top + 1) >= Math.Max(width, height) * 0.6;
    }
}
