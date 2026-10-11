using NeoFences.Core.Items;

namespace NeoFences.Core.Tests.Items;

/// <summary>rc.6 (the owner's icons): a 256-px icon is real large art, or a small icon centred in an empty canvas.</summary>
public class IconArtTests
{
    private static byte[] Canvas(int size, int artSize)
    {
        var pixels = new byte[size * size * 4];
        var from = (size - artSize) / 2;
        for (var y = from; y < from + artSize; y++)
        for (var x = from; x < from + artSize; x++)
            pixels[(y * size + x) * 4 + 3] = 255; // opaque art
        return pixels;
    }

    [Theory]
    [InlineData(256, true)]  // a real 256-px icon (Steam, Redragon)
    [InlineData(200, true)]  // with a little padding
    [InlineData(48, false)]  // a 48-px icon in the middle of an empty 256 canvas (AMD Ryzen Master, Sideloadly)
    [InlineData(32, false)]
    public void LargeArt_IsArtThatFillsTheCanvas(int artSize, bool large) =>
        Assert.Equal(large, IconArt.IsLarge(Canvas(256, artSize), 256, 256));

    [Fact]
    public void NothingOrOddInput_IsNotLargeArt()
    {
        Assert.False(IconArt.IsLarge(new byte[256 * 256 * 4], 256, 256)); // fully transparent
        Assert.False(IconArt.IsLarge(new byte[10], 256, 256));            // too few pixels for the size
        Assert.False(IconArt.IsLarge([], 0, 0));
    }
}
