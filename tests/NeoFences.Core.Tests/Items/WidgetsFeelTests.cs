using System.Globalization;
using NeoFences.Core.Items;

namespace NeoFences.Core.Tests.Items;

/// <summary>M41a (spec 2026-10-11-m41a-feel-design §6): widget sizes, the calendar, world clocks, Now playing.</summary>
public class WidgetsFeelTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-GB");

    // ---------- §6.2 sizes ----------

    [Theory]
    [InlineData(WidgetSize.Small, 2, 1)]
    [InlineData(WidgetSize.Medium, 2, 2)]
    [InlineData(WidgetSize.Large, 4, 2)]
    public void EachSize_HasItsSpan(WidgetSize size, int columns, int rows) => Assert.Equal(new GridSpan(columns, rows), WidgetSizes.SpanOf(size));

    [Theory]
    [InlineData(WidgetKind.Clock, WidgetSize.Small)]
    [InlineData(WidgetKind.Date, WidgetSize.Medium)]
    [InlineData(WidgetKind.Stats, WidgetSize.Medium)]
    [InlineData(WidgetKind.NowPlaying, WidgetSize.Medium)]
    public void WithoutAStoredSize_EachKindHasItsDefault(WidgetKind kind, WidgetSize size) => Assert.Equal(size, WidgetSizes.Of(null, kind));

    [Theory]
    [InlineData(1, 1, WidgetSize.Small)]
    [InlineData(2, 1, WidgetSize.Small)]
    [InlineData(1, 3, WidgetSize.Medium)]
    [InlineData(2, 2, WidgetSize.Medium)]
    [InlineData(2, 4, WidgetSize.Medium)]
    [InlineData(3, 1, WidgetSize.Large)]
    [InlineData(4, 4, WidgetSize.Large)]
    public void AnOldSpan_IsTheNearestSize(int columns, int rows, WidgetSize size) => Assert.Equal(size, WidgetSizes.Of(new GridSpan(columns, rows), WidgetKind.Stats));

    // rc.5 (owner's desktop): a Large widget in a three-column fence is trimmed to 3×2; it shows the biggest design that fits.
    [Theory]
    [InlineData(WidgetSize.Large, 4, 2, WidgetSize.Large)]
    [InlineData(WidgetSize.Large, 3, 2, WidgetSize.Medium)]
    [InlineData(WidgetSize.Large, 3, 1, WidgetSize.Small)]
    [InlineData(WidgetSize.Medium, 2, 2, WidgetSize.Medium)]
    [InlineData(WidgetSize.Medium, 1, 2, WidgetSize.Small)]
    [InlineData(WidgetSize.Small, 4, 2, WidgetSize.Small)] // never bigger than chosen
    [InlineData(WidgetSize.Small, 1, 1, WidgetSize.Small)]
    public void AWidgetShows_TheBiggestDesignThatFitsItsSpace(WidgetSize chosen, int columns, int rows, WidgetSize shown) =>
        Assert.Equal(shown, WidgetSizes.Fitting(chosen, new GridSpan(columns, rows)));

    [Fact]
    public void AWidgetsLayoutSpan_IsItsSizesSpan_AnItemKeepsItsOwn()
    {
        var clock = new VirtualItem { Id = "c", Target = Widgets.Target(WidgetKind.Clock), Size = new GridSpan(3, 3) };
        Assert.Equal(new GridSpan(4, 2), FenceGrid.SpanOf(clock));
        Assert.Equal(new GridSpan(2, 1), FenceGrid.SpanOf(clock with { Size = null }));
        Assert.Equal(new GridSpan(3, 3), FenceGrid.SpanOf(new VirtualItem { Id = "i", Target = @"C:\Games", Size = new GridSpan(3, 3) }));
    }

    [Theory]
    [InlineData(1.2, 0.8, WidgetSize.Small)]
    [InlineData(2.4, 1.3, WidgetSize.Small)]
    [InlineData(2.4, 1.6, WidgetSize.Medium)]
    [InlineData(2.9, 2.8, WidgetSize.Medium)]
    [InlineData(3.1, 1.2, WidgetSize.Large)]
    [InlineData(4.6, 3.0, WidgetSize.Large)]
    public void ACornerDrag_SnapsToTheNearestSize(double columns, double rows, WidgetSize size) => Assert.Equal(size, WidgetSizes.Snap(columns, rows));

    // ---------- §6.2 calendar ----------

    [Fact]
    public void October2026_StartingMonday_HasFiveWeeks_TodayAndWeekendsMarked()
    {
        var month = CalendarMonth.Build(new DateOnly(2026, 10, 1), DayOfWeek.Monday, today: new DateOnly(2026, 10, 11));
        Assert.Equal(35, month.Days.Count); // 1 October is a Thursday: three days of September before it
        Assert.Equal((28, false), (month.Days[0].Day, month.Days[0].InMonth));
        Assert.Equal((1, true), (month.Days[3].Day, month.Days[3].InMonth));
        var today = Assert.Single(month.Days, day => day.Today);
        Assert.Equal((11, true), (today.Day, today.Weekend)); // a Sunday
        Assert.Equal((1, false), (month.Days[^1].Day, month.Days[^1].InMonth));
    }

    [Fact]
    public void AMonthStartingOnSunday_TakesSixWeeks_WhenTheWeekStartsMonday()
    {
        var month = CalendarMonth.Build(new DateOnly(2026, 3, 1), DayOfWeek.Monday, today: new DateOnly(2026, 1, 1)); // 1 March 2026 is a Sunday
        Assert.Equal(42, month.Days.Count);
        Assert.DoesNotContain(month.Days, day => day.Today);
    }

    [Fact]
    public void TheWeekStart_FollowsTheSetting()
    {
        var sunday = CalendarMonth.Build(new DateOnly(2026, 10, 1), DayOfWeek.Sunday, today: new DateOnly(2026, 10, 11));
        Assert.Equal(27, sunday.Days[0].Day); // Sunday 27 September
        Assert.Equal(["S", "M", "T", "W", "T", "F", "S"], CalendarMonth.Headers(English, DayOfWeek.Sunday));
        Assert.Equal(["M", "T", "W", "T", "F", "S", "S"], CalendarMonth.Headers(English, DayOfWeek.Monday));
    }

    [Fact]
    public void February2027_StartingMonday_FitsFourWeeks()
    {
        var month = CalendarMonth.Build(new DateOnly(2027, 2, 1), DayOfWeek.Monday, today: new DateOnly(2027, 2, 14));
        Assert.Equal(28, month.Days.Count);
        Assert.All(month.Days, day => Assert.True(day.InMonth));
    }

    [Fact]
    public void ALeapFebruary_HasItsTwentyNinth() =>
        Assert.Contains(CalendarMonth.Build(new DateOnly(2028, 2, 1), DayOfWeek.Monday, today: new DateOnly(2028, 1, 1)).Days, day => day is { Day: 29, InMonth: true });

    [Theory]
    [InlineData(2026, 10, 11, 41)]
    [InlineData(2027, 1, 1, 53)] // ISO: 1 January 2027 still belongs to 2026's week 53
    public void TheWeekNumber_IsIso(int year, int month, int day, int week) => Assert.Equal(week, CalendarMonth.WeekOf(new DateOnly(year, month, day)));

    // ---------- §6.2 world clocks ----------

    [Fact]
    public void AWorldClock_ShowsTheCitysTime()
    {
        var instant = new DateTimeOffset(2026, 10, 11, 0, 13, 0, TimeSpan.Zero); // London is on summer time (+1), Tokyo +9
        Assert.Equal(("London", "01:13"), WorldClocks.Show("Europe/London", instant, English));
        Assert.Equal(("Tokyo", "09:13"), WorldClocks.Show("Asia/Tokyo", instant, English));
    }

    [Fact]
    public void UnknownCities_AreDropped_AndAtMostTwoKept() =>
        Assert.Equal(["Europe/London", "Asia/Tokyo"], WorldClocks.Normalize(["Europe/London", "Mars/Olympus", "Asia/Tokyo", "America/New_York"]));

    [Fact]
    public void TheCityList_HasNamesAndKnownZones()
    {
        Assert.Contains(WorldClocks.Cities, city => city is { Id: "Asia/Kolkata", Name: "India (Kolkata)" } or { Id: "Asia/Kolkata" });
        Assert.All(WorldClocks.Cities, city => Assert.NotNull(TimeZoneInfo.FindSystemTimeZoneById(city.Id)));
    }

    // ---------- §6.6 Now playing ----------

    [Theory]
    [InlineData(101, 243, "1:41", "−2:22")]
    [InlineData(0, 59, "0:00", "−0:59")]
    [InlineData(3725, 7200, "1:02:05", "−57:55")]
    public void TheTimeline_ShowsElapsedAndLeft(int position, int end, string elapsed, string left) =>
        Assert.Equal((elapsed, left, false), NowPlaying.Timeline(TimeSpan.FromSeconds(position), TimeSpan.FromSeconds(end)));

    [Fact]
    public void AStreamWithoutAnEnd_IsLive() => Assert.Equal(("", "", true), NowPlaying.Timeline(TimeSpan.FromSeconds(80), end: null));

    [Fact]
    public void APositionPastTheEnd_IsClamped() => Assert.Equal(("4:03", "−0:00", false), NowPlaying.Timeline(TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(243)));

    [Fact]
    public void NothingPlaying_ShowsTheLastSongDimmed_ElseSaysSo()
    {
        var song = new MediaTrack("Midnight City", "M83", "Hurry Up, We're Dreaming", "Spotify");
        Assert.Equal(new NowPlayingFace(song, Idle: false, "Midnight City", "M83"), NowPlaying.Face(current: song, last: null));
        Assert.Equal(new NowPlayingFace(song, Idle: true, "Nothing playing", "Last: Midnight City · M83"), NowPlaying.Face(current: null, last: song));
        Assert.Equal(new NowPlayingFace(null, Idle: true, "Nothing playing", ""), NowPlaying.Face(current: null, last: null));
    }

    [Fact]
    public void NowPlaying_IsAWidgetKind() =>
        Assert.Equal((WidgetKind.NowPlaying, "Now playing"), (Widgets.Of("neofences:widget/nowplaying"), Widgets.NameOf(WidgetKind.NowPlaying)));
}

/// <summary>M41a (spec §6.6): the live position between Windows' timeline updates, and the players' names.</summary>
public class NowPlayingTimeTests
{
    private static readonly DateTimeOffset Updated = new(2026, 10, 11, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhilePlaying_ThePositionRunsOnFromTheLastUpdate() =>
        Assert.Equal(TimeSpan.FromSeconds(104), NowPlaying.LivePosition(TimeSpan.FromSeconds(101), Updated, Updated.AddSeconds(3), playing: true, end: TimeSpan.FromSeconds(243)));

    [Fact]
    public void Paused_ThePositionStays() =>
        Assert.Equal(TimeSpan.FromSeconds(101), NowPlaying.LivePosition(TimeSpan.FromSeconds(101), Updated, Updated.AddSeconds(30), playing: false, end: TimeSpan.FromSeconds(243)));

    [Fact]
    public void ThePosition_NeverRunsPastTheEnd() =>
        Assert.Equal(TimeSpan.FromSeconds(243), NowPlaying.LivePosition(TimeSpan.FromSeconds(240), Updated, Updated.AddMinutes(5), playing: true, end: TimeSpan.FromSeconds(243)));

    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
    [InlineData("Chrome", "Chrome")]
    [InlineData("MSEdge", "Edge")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "Media Player")]
    [InlineData("firefox.exe", "Firefox")]
    [InlineData("vlc.exe", "VLC")]
    [InlineData("SomeApp.exe", "SomeApp")]
    [InlineData("", "A player")]
    public void APlayersName_ComesFromItsAppId(string appId, string name) => Assert.Equal(name, NowPlaying.AppName(appId));
}

/// <summary>M41a (spec §6.5): widget-only fences may float (no glass, no title).</summary>
public class WidgetBoardTests
{
    [Fact]
    public void AFence_DoesNotFloatUntilAsked_AndTheSwitchSurvivesASave()
    {
        var fence = NeoFences.Core.Model.Fence.Create("Dashboard");
        Assert.False(fence.WidgetsFloat);
        var config = NeoFences.Core.Model.FenceEdits.SetWidgetsFloat(new NeoFences.Core.Model.NeoFencesConfig { Fences = [fence] }, fence.Id, true);
        var restored = NeoFences.Core.Config.ConfigJson.Deserialize(NeoFences.Core.Config.ConfigJson.Serialize(config));
        Assert.True(restored.Fences[0].WidgetsFloat);
    }

    [Fact]
    public void OnlyWidgets_IsAFenceOfWidgetsAlone()
    {
        string[] widgets = [Widgets.Target(WidgetKind.Clock), Widgets.Target(WidgetKind.Stats)];
        Assert.True(WidgetBoards.OnlyWidgets(widgets));
        Assert.False(WidgetBoards.OnlyWidgets([.. widgets, @"C:\Games"]));
        Assert.False(WidgetBoards.OnlyWidgets([]));
    }

    [Fact]
    public void AClocksCities_AreRepairedWhenLoaded()
    {
        var clock = new VirtualItem { Id = "c", Target = Widgets.Target(WidgetKind.Clock), Widget = new WidgetOptions { Cities = ["Asia/Tokyo", "Nowhere/City", "Asia/Tokyo", "Europe/London", "UTC"] } };
        var repaired = ItemEdits.Repair(new ItemsDocument().With("f", [clock])).Of("f")[0];
        Assert.Equal(["Asia/Tokyo", "Europe/London"], repaired.Widget!.Cities);
    }
}

/// <summary>Final review: "let them float" is offered once — when a fence first becomes widgets only, not on every widget after.</summary>
public class FloatOfferTests
{
    private static readonly string Clock = Widgets.Target(WidgetKind.Clock), Stats = Widgets.Target(WidgetKind.Stats);

    [Fact]
    public void TheFirstWidgetInAnEmptyFence_IsOffered() => Assert.True(WidgetBoards.OfferFloat(before: [], after: [Clock]));

    [Fact]
    public void TheLastItemLeavingAWidgetFence_IsOffered() => Assert.True(WidgetBoards.OfferFloat(before: [Clock, @"C:\Games"], after: [Clock]));

    [Fact]
    public void AnotherWidgetInAWidgetOnlyFence_IsNotOfferedAgain() => Assert.False(WidgetBoards.OfferFloat(before: [Clock], after: [Clock, Stats]));

    [Fact]
    public void AFenceWithItems_IsNotOffered() => Assert.False(WidgetBoards.OfferFloat(before: [@"C:\Games"], after: [@"C:\Games", Clock]));
}

/// <summary>M41b (spec §7): the rc.3 leftovers that are Core's.</summary>
public class Rc3LeftoverTests
{
    [Fact]
    public void AnUntitledSession_ShowsTheAppsName()
    {
        var face = NowPlaying.Face(new MediaTrack("", "", "", "Spotify.exe"), last: null);
        Assert.Equal(NowPlaying.AppName("Spotify.exe"), face.Title);
    }

    [Theory]
    [InlineData(0, 120, 1, 0)]
    [InlineData(0, 40, 0, 40)]
    [InlineData(80, 40, 1, 0)]
    [InlineData(0, -250, -2, -10)]
    [InlineData(30, -40, 0, -40)]   // 1.0.1 (rc.4 review minor): turning the other way drops the leftover
    [InlineData(60, -120, -1, 0)]   // so the first notch back is a whole step, not swallowed
    public void TheWheel_AddsUpToWholeSteps(int remainder, int delta, int steps, int left) =>
        Assert.Equal((steps, left), VolumeWheel.Add(remainder, delta));
}
