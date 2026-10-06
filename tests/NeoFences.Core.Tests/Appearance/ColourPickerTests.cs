using NeoFences.Core.Appearance;

namespace NeoFences.Core.Tests.Appearance;

/// <summary>M35 (spec 2026-10-06-modern-menus-and-dialogs-design §3): the colour picker's maths and what people type as hex.</summary>
public class ColourPickerTests
{
    [Theory]
    [InlineData(0xFF, 0x00, 0x00, 0, 1, 1)]
    [InlineData(0x00, 0xFF, 0x00, 120, 1, 1)]
    [InlineData(0x00, 0x00, 0xFF, 240, 1, 1)]
    [InlineData(0xFF, 0xFF, 0xFF, 0, 0, 1)]
    [InlineData(0x00, 0x00, 0x00, 0, 0, 0)]
    [InlineData(0x80, 0x80, 0x80, 0, 0, 0.502)]
    public void Hsv_FromArgb_KnownColours(byte red, byte green, byte blue, double hue, double saturation, double value)
    {
        var hsv = Hsv.FromArgb(new Argb(0xFF, red, green, blue));
        Assert.Equal(hue, hsv.H, 1);
        Assert.Equal(saturation, hsv.S, 3);
        Assert.Equal(value, hsv.V, 3);
    }

    [Theory]
    [InlineData("#E23A50")]
    [InlineData("#E8C13D")]
    [InlineData("#3A7BE2")]
    [InlineData("#000000")]
    [InlineData("#FFFFFF")]
    [InlineData("#808080")]
    [InlineData("#16C60C")]
    public void Hsv_RoundTrips_EveryByte(string hex)
    {
        var colour = Argb.FromHex(hex)!.Value;
        Assert.Equal(colour, Hsv.FromArgb(colour).ToArgb());
    }

    [Fact]
    public void Hsv_OutOfRange_IsClamped_HueWraps()
    {
        Assert.Equal(new Argb(0xFF, 0xFF, 0x00, 0x00), new Hsv(360, 1.5, 2).ToArgb()); // 360° is red again; S and V at most 1
        Assert.Equal(new Argb(0xFF, 0x00, 0x00, 0x00), new Hsv(-30, -1, -1).ToArgb());
        Assert.Equal(new Hsv(330, 1, 1).ToArgb(), new Hsv(-30, 1, 1).ToArgb());
    }

    [Theory]
    [InlineData("#e23a50", "#E23A50")]
    [InlineData("E23A50", "#E23A50")]
    [InlineData("  #E23A50  ", "#E23A50")]
    [InlineData("#abc", "#AABBCC")]
    [InlineData("abc", "#AABBCC")]
    public void FromUserHex_AcceptsWhatPeopleType(string typed, string expected) => Assert.Equal(expected, Argb.FromUserHex(typed)!.Value.ToHex());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#12345G")]
    [InlineData("red")]
    [InlineData("#E23A5011")]
    [InlineData("+E23A50")]
    public void FromUserHex_RejectsTheRest(string? typed) => Assert.Null(Argb.FromUserHex(typed));
}
