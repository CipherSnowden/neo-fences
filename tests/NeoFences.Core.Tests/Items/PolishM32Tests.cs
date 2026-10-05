using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using NeoFences.Core.Config;
using NeoFences.Core.Items;

namespace NeoFences.Core.Tests.Items;

/// <summary>M32 (0.19.1, spec 2026-10-06-polish-0.19.1-design): the sticky main GPU, the clock's date line, the °F option's JSON.</summary>
public class PolishM32Tests
{
    // ---------- the sticky main GPU (item 3) ----------

    [Fact]
    public void StickyGpu_FirstChoice_IsTheOneUsingTheMostVideoMemory() =>
        Assert.Equal("2", Widgets.StickyGpu(previous: null, [("1", 300), ("2", 3900)]));

    [Fact]
    public void StickyGpu_KeepsThePreviousOne_WhenAnotherUsesSomewhatMore() =>
        // A hybrid laptop: the graphics card (2) idles at 0 MB while the built-in GPU (1) holds a few hundred.
        Assert.Equal("2", Widgets.StickyGpu(previous: "2", [("1", 600), ("2", 0)]));

    [Fact]
    public void StickyGpu_Switches_WhenAnotherUsesTwiceAsMuchAndAGigabyteMore() =>
        Assert.Equal("2", Widgets.StickyGpu(previous: "1", [("1", 400), ("2", 1500)]));

    [Fact]
    public void StickyGpu_DoesNotSwitch_OnTwiceAsMuchButLittleMore() =>
        Assert.Equal("1", Widgets.StickyGpu(previous: "1", [("1", 100), ("2", 900)]));

    [Fact]
    public void StickyGpu_WhenThePreviousOneIsGone_TakesTheBiggest() =>
        Assert.Equal("3", Widgets.StickyGpu(previous: "9", [("1", 10), ("3", 20)]));

    [Fact]
    public void StickyGpu_WithoutCandidates_IsNothing() => Assert.Null(Widgets.StickyGpu(previous: "1", []));

    private static byte[] Afterburner(params (string Name, float Value)[] entries)
    {
        const int Header = 32, Entry = 1324;
        var memory = new byte[Header + entries.Length * Entry];
        BinaryPrimitives.WriteUInt32LittleEndian(memory, 0x4D41484D);
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(8), Header);
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(12), (uint)entries.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(16), Entry);
        for (var index = 0; index < entries.Length; index++)
        {
            Encoding.Latin1.GetBytes(entries[index].Name).CopyTo(memory, Header + index * Entry);
            BinaryPrimitives.WriteSingleLittleEndian(memory.AsSpan(Header + index * Entry + 1300), entries[index].Value);
        }
        return memory;
    }

    [Fact]
    public void Afterburner_StaysOnThePreviousGpu_AndSaysWhichItIs()
    {
        var memory = Afterburner(("GPU1 temperature", 44f), ("GPU2 temperature", 50f), ("GPU1 memory usage", 600f), ("GPU2 memory usage", 0f));

        var first = SensorFormats.Afterburner(memory);
        var sticky = SensorFormats.Afterburner(memory, previousGpu: "2");

        Assert.Equal(("1", 44.0), (first!.GpuKey, first.GpuTemp)); // no history: the most memory
        Assert.Equal(("2", 50.0), (sticky!.GpuKey, sticky.GpuTemp)); // the graphics card idles at 0 MB: still the card
    }

    [Fact]
    public void MainGpu_InWindows_StaysOnThePreviousAdapter()
    {
        var engines = new List<(string, double)> { ("luid_0x0_0xA_phys_0_eng_0", 30), ("luid_0x0_0xB_phys_0_eng_0", 2) };
        var dedicated = new List<(string, double)> { ("luid_0x0_0xA_phys_0", 600e6), ("luid_0x0_0xB_phys_0", 0) };

        Assert.Equal(("luid_0x0_0xB", 2), Widgets.MainGpu(engines, dedicated, previousAdapter: "luid_0x0_0xB"));
    }

    // ---------- the clock's date line in the culture's order (item 6) ----------

    [Theory]
    [InlineData("en-GB", "Monday, 5 October")]
    [InlineData("en-US", "Monday, October 5")]
    [InlineData("de-DE", "Montag, 5. Oktober")]
    [InlineData("ja-JP", "10月5日 月曜日")]
    [InlineData("ko-KR", "10월 5일 월요일")]
    [InlineData("zh-CN", "10月5日 星期一")]
    public void ClockDateLine_PutsTheWeekdayWhereTheCulturesLongDateHasIt(string culture, string expected) =>
        Assert.Equal(expected, Widgets.ClockDateLine(new DateTime(2026, 10, 5), CultureInfo.GetCultureInfo(culture)));

    // ---------- the °F option's JSON (item 9) ----------

    [Fact]
    public void Fahrenheit_IsWrittenOnlyWhenSet()
    {
        var clock = VirtualItem.Create(Widgets.Target(WidgetKind.Clock)) with { Widget = new WidgetOptions { Date = true } };
        var stats = VirtualItem.Create(Widgets.Target(WidgetKind.Stats)) with { Widget = new WidgetOptions { Fahrenheit = true } };

        var json = ConfigJson.SerializeItems(new ItemsDocument().With("f", [clock, stats]));

        Assert.Equal(1, json.Split("fahrenheit").Length - 1);
        Assert.True(ConfigJson.DeserializeItems(json).Of("f")[1].Widget!.Fahrenheit);
    }
}

/// <summary>M32 final review: the date line with Windows' own regional formats, the sticky threshold's other half.</summary>
public class PolishM32ReviewTests
{
    [Fact]
    public void ClockDateLine_WithWindowsJapaneseFormats_PutsTheWeekdayLast()
    {
        // Final review I2: Windows' ja-JP and zh-CN long date has no weekday ("yyyy'年'M'月'd'日'"); the user's culture
        // carries that, so the culture's own (ICU) order decides.
        var japanese = (CultureInfo)CultureInfo.GetCultureInfo("ja-JP").Clone();
        japanese.DateTimeFormat.LongDatePattern = "yyyy'年'M'月'd'日'";
        Assert.Equal("10月5日 月曜日", Widgets.ClockDateLine(new DateTime(2026, 10, 5), japanese));
    }

    [Fact]
    public void ClockDateLine_WithALiteralAfterTheWeekday_StillPutsItLast()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-GB").Clone();
        culture.DateTimeFormat.LongDatePattern = "yyyy MMMM d, dddd 'day'";
        Assert.EndsWith("Monday", Widgets.ClockDateLine(new DateTime(2026, 10, 5), culture));
    }

    [Fact]
    public void StickyGpu_DoesNotSwitch_OnAGigabyteMoreButLessThanTwice() =>
        Assert.Equal("1", Widgets.StickyGpu(previous: "1", [("1", 2000), ("2", 3500)]));
}
