using System.Globalization;
using NeoFences.Core.Config;
using NeoFences.Core.Items;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Items;

/// <summary>M25 (0.14.0): widgets — clock, date, system stats (spec 2026-10-05-widgets-design).</summary>
public class WidgetsTests
{
    [Theory]
    [InlineData(WidgetKind.Clock, "neofences:widget/clock")]
    [InlineData(WidgetKind.Date, "neofences:widget/date")]
    [InlineData(WidgetKind.Stats, "neofences:widget/stats")]
    public void Targets_RoundTrip_AndAreTheirOwnKind(WidgetKind kind, string target)
    {
        Assert.Equal(target, Widgets.Target(kind));
        Assert.Equal(kind, Widgets.Of(target));
        Assert.Equal(ItemKind.Widget, ItemKinds.Of(target));
    }

    [Theory]
    [InlineData("neofences:widget/weather")]
    [InlineData("neofences:widget/")]
    public void UnknownWidgetKinds_AreUnknownWidgets_NeverOpenedThroughWindows(string target)
    {
        // A widget kind of a newer NeoFences (M28, M25 review minor): still a widget (no shell open, no target check), shown Missing.
        Assert.Null(Widgets.Of(target));
        Assert.Equal(ItemKind.Widget, ItemKinds.Of(target));
        Assert.True(Widgets.IsUnknown(target));
        Assert.Equal("Unknown widget", Widgets.NameOfTarget(target));
    }

    [Fact]
    public void KnownWidgets_AndPaths_AreNotUnknownWidgets()
    {
        Assert.False(Widgets.IsUnknown(Widgets.Target(WidgetKind.Clock)));
        Assert.Equal("Clock", Widgets.NameOfTarget(Widgets.Target(WidgetKind.Clock)));
        Assert.False(Widgets.IsUnknown(@"C:\neofences\widget\clock"));
        Assert.NotEqual(ItemKind.Widget, ItemKinds.Of(@"C:\neofences\widget\clock"));
    }

    [Fact]
    public void DefaultSpans_ClockIsWide_DateAndStatsSquare_OwnSizeWins()
    {
        Assert.Equal(new GridSpan(2, 1), FenceGrid.SpanOf(VirtualItem.Create(Widgets.Target(WidgetKind.Clock))));
        Assert.Equal(new GridSpan(2, 2), FenceGrid.SpanOf(VirtualItem.Create(Widgets.Target(WidgetKind.Date))));
        Assert.Equal(new GridSpan(2, 2), FenceGrid.SpanOf(VirtualItem.Create(Widgets.Target(WidgetKind.Stats))));
        Assert.Equal(new GridSpan(1, 1), FenceGrid.SpanOf(VirtualItem.Create(Widgets.Target(WidgetKind.Clock)) with { Size = GridSpan.One }));
    }

    [Fact]
    public void Widgets_AreNeverCheckedOrWatched()
    {
        var fenceId = Fence.NewId();
        var items = new ItemsDocument().With(fenceId, [VirtualItem.Create(Widgets.Target(WidgetKind.Stats)), VirtualItem.Create(@"C:\a.txt")]);
        Assert.Equal([@"C:\a.txt"], ItemEdits.CheckedTargets(items));
        Assert.Equal([@"C:\a.txt"], ItemEdits.PathTargets(items));
    }

    [Fact]
    public void DatePage_IsWeekdayDayAndMonthYear_InTheCulture()
    {
        var page = Widgets.Page(new DateTime(2026, 10, 5), CultureInfo.GetCultureInfo("en-US"));
        Assert.Equal(new DatePage("Monday", "5", "October 2026"), page);
    }

    [Fact]
    public void ClockText_FollowsTheCulturesTimeFormats()
    {
        var time = new DateTime(2026, 10, 5, 16, 7, 9);
        Assert.Equal("4:07 PM", Widgets.ClockText(time, seconds: false, CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("16:07:09", Widgets.ClockText(time, seconds: true, CultureInfo.GetCultureInfo("de-DE")));
    }

    [Fact]
    public void NextTick_IsTheNextSecondOrMinuteBoundary()
    {
        var now = new DateTime(2026, 10, 5, 16, 7, 9, 250);
        Assert.Equal(new DateTime(2026, 10, 5, 16, 7, 10), Widgets.NextTick(now, seconds: true));
        Assert.Equal(new DateTime(2026, 10, 5, 16, 8, 0), Widgets.NextTick(now, seconds: false));
        Assert.Equal(new DateTime(2026, 10, 5, 16, 8, 0), Widgets.NextTick(new DateTime(2026, 10, 5, 16, 7, 0), seconds: false));
    }

    [Fact]
    public void Options_RoundTripInItemsJson_OnlyWhenSet()
    {
        var fenceId = Fence.NewId();
        var clock = VirtualItem.Create(Widgets.Target(WidgetKind.Clock)) with { Widget = new WidgetOptions { Seconds = true, Date = true } };
        var plain = VirtualItem.Create(Widgets.Target(WidgetKind.Date));
        var json = ConfigJson.SerializeItems(new ItemsDocument().With(fenceId, [clock, plain]));
        Assert.Equal(1, json.Split("\"widget\"").Length - 1);
        Assert.Equal(new WidgetOptions { Seconds = true, Date = true }, ConfigJson.DeserializeItems(json).Of(fenceId)[0].Widget);
    }

    [Fact]
    public void Names_AreReadable() =>
        Assert.Equal(["Clock", "Date", "System stats"], new[] { WidgetKind.Clock, WidgetKind.Date, WidgetKind.Stats }.Select(Widgets.NameOf));
}
