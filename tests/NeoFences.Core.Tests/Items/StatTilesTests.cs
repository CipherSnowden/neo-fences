using System.Globalization;
using NeoFences.Core.Config;
using NeoFences.Core.Items;

namespace NeoFences.Core.Tests.Items;

/// <summary>M31: the System stats widget's five tiles, the °F option, and the modern date texts.</summary>
public class StatTilesTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-GB");

    [Fact]
    public void Tiles_AreCpuCpuTempGpuGpuTempAndRam_InThatOrder()
    {
        var tiles = Widgets.Tiles(new StatsSample(Cpu: 11.5, CpuTemp: 48.9, Gpu: 8, GpuTemp: 50, RamUsedGb: 13.04, RamTotalGb: 31.9), fahrenheit: false, English);

        Assert.Equal(["CPU", "CPU TEMP", "GPU", "GPU TEMP", "RAM"], tiles.Select(tile => tile.Label));
        Assert.Equal(new StatTile("CPU", 11.5, "12", "%"), tiles[0]);
        Assert.Equal(new StatTile("CPU TEMP", 48.9, "49", "°C"), tiles[1]);
        Assert.Equal(new StatTile("GPU TEMP", 50, "50", "°C"), tiles[3]);
        Assert.Equal(new StatTile("RAM", 100 * 13.04 / 31.9, "13.0", "/ 32 GB", Wide: true), tiles[4]);
    }

    [Fact]
    public void Tiles_InFahrenheit_ConvertTheTemperatures_ButTheBarsStayOnTheCelsiusScale()
    {
        var tiles = Widgets.Tiles(new StatsSample(null, CpuTemp: 54, null, GpuTemp: 100, null, null), fahrenheit: true, English);

        Assert.Equal(new StatTile("CPU TEMP", 54, "129", "°F"), tiles[1]);
        Assert.Equal(new StatTile("GPU TEMP", 100, "212", "°F"), tiles[3]);
    }

    [Fact]
    public void Tiles_WithoutValues_ShowADash_AndCpuTempSaysWhatItNeeds()
    {
        var tiles = Widgets.Tiles(null, fahrenheit: false, English);

        Assert.All(tiles, tile => Assert.Equal("—", tile.Value));
        Assert.Equal("needs Afterburner or HWiNFO", tiles[1].Hint);
        Assert.Equal("", tiles[3].Hint); // GPU TEMP: Windows usually gives it; no hint
    }

    [Fact]
    public void Tiles_ClampTheirBars()
    {
        var tiles = Widgets.Tiles(new StatsSample(Cpu: 104, CpuTemp: -5, Gpu: null, GpuTemp: 130, RamUsedGb: 40, RamTotalGb: 32), fahrenheit: false, English);
        Assert.Equal(100, tiles[0].Percent);
        Assert.Equal("100", tiles[0].Value);
        Assert.Equal(0, tiles[1].Percent);
        Assert.Equal(100, tiles[3].Percent);
        Assert.Equal(100, tiles[4].Percent);
    }

    [Fact]
    public void RamText_UsesTheCulturesDecimalSeparator()
    {
        var tile = Widgets.Tiles(new StatsSample(null, null, null, null, RamUsedGb: 13.04, RamTotalGb: 31.9), fahrenheit: false, CultureInfo.GetCultureInfo("de-DE"))[4];
        Assert.Equal("13,0", tile.Value);
    }

    [Fact]
    public void Fahrenheit_IsAWidgetOption_ThatRoundTrips()
    {
        var item = VirtualItem.Create(Widgets.Target(WidgetKind.Stats)) with { Widget = new WidgetOptions { Fahrenheit = true } };
        var restored = ConfigJson.DeserializeItems(ConfigJson.SerializeItems(new ItemsDocument().With("f", [item])));
        Assert.True(restored.Of("f")[0].Widget!.Fahrenheit);
    }

    [Theory]
    [InlineData("en-GB", "Monday, 5 October")]
    [InlineData("en-US", "Monday, October 5")]
    public void ClockDateLine_IsTheWeekdayAndTheDayInTheCulturesOrder(string culture, string expected) =>
        Assert.Equal(expected, Widgets.ClockDateLine(new DateTime(2026, 10, 5), CultureInfo.GetCultureInfo(culture)));

    [Fact]
    public void DatePage_WeekdayIsNoLongerUpperCase() =>
        Assert.Equal("Monday", Widgets.Page(new DateTime(2026, 10, 5), English).Weekday);

    [Theory]
    [InlineData("pid_1_luid_0x00000000_0x0000D1F2_phys_0_eng_0_engtype_3D", 0, 0xD1F2u)]
    [InlineData("luid_0x00000001_0x0000ABCD", 1, 0xABCDu)]
    public void LuidOf_ReadsTheAdaptersId(string instance, int high, uint low) =>
        Assert.Equal((high, low), Widgets.LuidOf(instance));

    [Fact]
    public void LuidOf_WithoutOne_IsNull() => Assert.Null(Widgets.LuidOf("engtype_3D"));

    [Fact]
    public void BusiestGpu_NamesTheAdapter()
    {
        var busiest = Widgets.BusiestGpu([("luid_0x0_0xA_phys_0_eng_0", 10), ("luid_0x0_0xB_phys_0_eng_0", 30), ("luid_0x0_0xB_phys_0_eng_1", 5)]);
        Assert.Equal(("luid_0x0_0xB", 35), busiest);
    }
}
