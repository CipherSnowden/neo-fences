using NeoFences.Core.Appearance;
using NeoFences.Core.Config;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Model;

/// <summary>M16 (v1.7.1): polish carry-overs from the M13c and M14 reviews that Core can pin.</summary>
public class PolishTests
{
    // ---------- tray labels never split an emoji (M13c review M6) ----------

    [Fact]
    public void MenuLabel_ACutNeverSplitsAnEmoji()
    {
        var name = new string('x', 59) + "🎮🎮"; // the 60-character cut falls inside the first emoji
        var label = Snapshots.MenuLabel(new SnapshotEntry(@"C:\s\a.json", name, DateTimeOffset.Now), TimeZoneInfo.Utc);
        Assert.Equal(new string('x', 59) + "…", label);
        Assert.DoesNotContain(label, character => char.IsSurrogate(character));
    }

    // ---------- readable titles with any custom colour (M14 review M10) ----------

    [Theory]
    [InlineData("#FFF2A0", true)]  // pale yellow on the light veil: darkened until it reads
    [InlineData("#F0F0FF", true)]  // near-white
    [InlineData("#101040", false)] // dark navy on the dark tone: lightened
    [InlineData("#000000", false)] // black
    public void AccentEdge_TitleReadsOnItsTone_WhateverTheColour(string colour, bool light)
    {
        var look = FenceLook.Resolve(new AppearanceSettings(), Fence.Create("Games") with { CustomColor = colour }, light, wallpaperAccent: null);
        if (light) Assert.True(look.TitleText.Luminance <= 0.25, $"{look.TitleText.ToHex()} on the light veil");
        else Assert.True(look.TitleText.Luminance >= 0.30, $"{look.TitleText.ToHex()} on the dark tone");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AccentEdge_EverySwatchKeepsItsV17TitleInBothTones(bool light)
    {
        // final review M1: Pink in dark mode was pushed although readable; ADR-038 promises readable colours keep v1.7's look
        var white = new Argb(0xFF, 0xFF, 0xFF, 0xFF);
        var black = new Argb(0xFF, 0x00, 0x00, 0x00);
        foreach (var (swatch, colour) in FenceLook.Swatches)
        {
            var look = FenceLook.Resolve(new AppearanceSettings(), Fence.Create("Games") with { TabColor = swatch }, light, wallpaperAccent: null);
            Assert.Equal(light ? colour.Mix(black, 0.35) : colour.Mix(white, 0.4), look.TitleText);
        }
    }

    [Fact]
    public void AccentEdge_AnAlreadyReadableColourIsLeftAsItWas()
    {
        var red = FenceLook.Resolve(new AppearanceSettings(), Fence.Create("Games") with { CustomColor = "#E84855" }, light: false, wallpaperAccent: null);
        Assert.Equal(Argb.FromHex("#E84855")!.Value.Mix(new Argb(0xFF, 0xFF, 0xFF, 0xFF), 0.4), red.TitleText); // v1.7's look unchanged
    }
}
