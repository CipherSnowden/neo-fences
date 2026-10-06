using NeoFences.Core.Items;
using NeoFences.Core.Library;
using NeoFences.Core.Lifecycle;
using NeoFences.Core.Tests.TestSupport;

namespace NeoFences.Core.Tests.Library;

/// <summary>M34 (spec 2026-10-06-icons-and-game-tiles-design, ADR-055): where a game's art comes from, strict names, misses.</summary>
public class GameArtTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.FromHours(5.5));

    public void Dispose() => _directory.Dispose();

    // ---------- the order ----------

    [Fact]
    public void Choose_TheUsersChoiceWins_ThenTheDiskCover_ThenOnline_ThenTheLogo()
    {
        Assert.Equal(new CoverArt(@"C:\c\choice.jpg", IsPoster: true), GameArt.Choose(chosen: @"C:\c\choice.jpg", poster: @"C:\s\p.jpg", online: @"C:\c\o.jpg", logo: @"C:\x\l.png"));
        Assert.Equal(new CoverArt(@"C:\s\p.jpg", IsPoster: true), GameArt.Choose(chosen: null, poster: @"C:\s\p.jpg", online: @"C:\c\o.jpg", logo: @"C:\x\l.png"));
        Assert.Equal(new CoverArt(@"C:\c\o.jpg", IsPoster: true), GameArt.Choose(chosen: null, poster: null, online: @"C:\c\o.jpg", logo: @"C:\x\l.png"));
        Assert.Equal(new CoverArt(@"C:\x\l.png", IsPoster: false), GameArt.Choose(chosen: null, poster: null, online: null, logo: @"C:\x\l.png"));
        Assert.Null(GameArt.Choose(chosen: null, poster: null, online: null, logo: null)); // the glow tile
    }

    [Fact]
    public void Choose_ALogoThatIsNotAPicture_IsNoArt()
    {
        Assert.Null(GameArt.Choose(chosen: null, poster: null, online: null, logo: @"D:\Games\Blur\Blur.exe"));
    }

    [Fact]
    public void NeedsLookup_OnlyWithoutAChoiceOrADiskCover_ALogoStillLooksUp()
    {
        Assert.True(GameArt.NeedsLookup(chosen: null, poster: null));
        Assert.False(GameArt.NeedsLookup(chosen: "a.jpg", poster: null));
        Assert.False(GameArt.NeedsLookup(chosen: null, poster: "p.jpg"));
    }

    // ---------- strict names (the owner's games) ----------

    [Theory]
    [InlineData("Clair Obscur - Expedition 33", "Clair Obscur: Expedition 33")]
    [InlineData("Detroit - Become Human", "Detroit: Become Human")]
    [InlineData("Mafia II Definitive Edition", "Mafia II: Definitive Edition")]
    [InlineData("Plague Inc - Evolved", "Plague Inc: Evolved")]
    [InlineData("Wallpaper Engine", "Wallpaper Engine")]
    [InlineData("Forza Horizon 6", "FORZA HORIZON 6™")]
    [InlineData("Tom Clancy's The Division®", "Tom Clancys The Division")]
    public void SameName_IgnoresCasePunctuationSpacingAndMarks(string ours, string steams) => Assert.True(GameArt.SameName(ours, steams));

    [Theory]
    [InlineData("Blur", "Ricochet Blur")]
    [InlineData("AC Black Flag Resynced", "Assassin's Creed IV Black Flag")]
    [InlineData("Mafia - Definitive Edition", "Mafia II: Definitive Edition")]
    [InlineData("!!!", "???")] // nothing left to compare is never a match
    public void SameName_IsStrict(string ours, string steams) => Assert.False(GameArt.SameName(ours, steams));

    [Theory]
    [InlineData("Clair Obscur - Expedition 33", "Clair Obscur Expedition 33")] // Steam's search finds nothing for " - "
    [InlineData("Plague Inc - Evolved", "Plague Inc Evolved")]
    [InlineData("  Forza   Horizon 6 ", "Forza Horizon 6")]
    [InlineData("Tom Clancy's The Division®", "Tom Clancy s The Division")]
    public void SearchTerm_TurnsPunctuationIntoSpaces(string name, string term) => Assert.Equal(term, GameArt.SearchTerm(name));

    // ---------- misses ----------

    [Fact]
    public void ShouldLookUp_NeverAsked_Yes_Found_No()
    {
        Assert.True(GameArt.ShouldLookUp(record: null, name: "Blur", now: Now));
        Assert.False(GameArt.ShouldLookUp(new CoverRecord("Blur", Now.AddDays(-400), SteamAppId: 1, File: "steam-1.jpg"), "Blur", Now));
    }

    [Fact]
    public void ShouldLookUp_AMiss_WaitsThirtyDays_OrARename()
    {
        var miss = new CoverRecord("Blur", Now.AddDays(-29));
        Assert.False(GameArt.ShouldLookUp(miss, "Blur", Now));
        Assert.True(GameArt.ShouldLookUp(miss with { Checked = Now.AddDays(-31) }, "Blur", Now));
        Assert.True(GameArt.ShouldLookUp(miss, "Blur (2010)", Now)); // renamed: asked again at once
    }

    // ---------- the index ----------

    [Fact]
    public void CoversIndex_RoundTrips_AndADamagedFileIsEmpty()
    {
        var path = Path.Combine(_directory.Path, "covers", "index.json");
        Assert.Empty(CoversIndex.Load(path).Games); // no file yet
        var index = CoversIndex.Empty
            .WithGame("steam:1903340", new CoverRecord("Clair Obscur - Expedition 33", Now, 1903340, "steam-1903340.jpg"))
            .WithGame("folder:d:\\gamelibrary\\blur", new CoverRecord("Blur", Now))
            .WithSite("www.youtube.com", new SiteRecord(Now, "www.youtube.com.png"));
        index.Save(path);

        var read = CoversIndex.Load(path);
        Assert.Equal("steam-1903340.jpg", read.Games["steam:1903340"].File);
        Assert.Null(read.Games["folder:d:\\gamelibrary\\blur"].File);
        Assert.Equal(Now, read.Games["folder:d:\\gamelibrary\\blur"].Checked);
        Assert.Equal("www.youtube.com.png", read.Sites["www.youtube.com"].File);

        File.WriteAllText(path, "{ not json");
        Assert.Empty(CoversIndex.Load(path).Games);
    }

    [Fact]
    public void CoversIndex_GameIdsIgnoreCase()
    {
        var index = CoversIndex.Empty.WithGame("Steam:1", new CoverRecord("A", Now, 1, "steam-1.jpg"));
        Assert.True(index.Games.ContainsKey("steam:1"));
    }

    // ---------- website icons ----------

    [Fact]
    public void DeclaredIcon_TakesTheLargest_ResolvedAgainstThePage()
    {
        const string html = """
            <html><head>
            <link rel="icon" href="/favicon-16.png" sizes="16x16">
            <link rel="icon" type="image/png" sizes="192x192" href="/icons/big.png">
            <link rel="apple-touch-icon" href="https://cdn.example.com/touch.png">
            <link rel="stylesheet" href="/site.css">
            </head></html>
            """;
        Assert.Equal(new Uri("https://example.com/icons/big.png"), SiteIcons.DeclaredIcon(html, new Uri("https://example.com/watch?v=1")));
    }

    [Fact]
    public void DeclaredIcon_AppleTouchIconWithoutSizes_Beats_APlainIcon_AndSvgIsSkipped()
    {
        const string html = """
            <link rel="shortcut icon" href="favicon.ico">
            <link rel="icon" href="/logo.svg" type="image/svg+xml" sizes="any">
            <link href="apple.png" rel="apple-touch-icon">
            """;
        Assert.Equal(new Uri("https://example.com/a/apple.png"), SiteIcons.DeclaredIcon(html, new Uri("https://example.com/a/page")));
    }

    [Fact]
    public void DeclaredIcon_None_IsNull_AndTheFallbackIsFaviconIco()
    {
        Assert.Null(SiteIcons.DeclaredIcon("<html><head><title>x</title></head></html>", new Uri("https://example.com/")));
        Assert.Equal(new Uri("https://example.com/favicon.ico"), SiteIcons.FallbackIcon(new Uri("https://example.com/deep/page?q=1")));
    }

    [Fact]
    public void Badge_IsTheSiteNamesFirstLetter_WithAStableColour()
    {
        var youtube = SiteIcons.Badge("https://www.youtube.com/watch?v=1");
        Assert.Equal('Y', youtube.Letter);
        Assert.Equal(youtube, SiteIcons.Badge("https://youtube.com/"));
        Assert.Equal('G', SiteIcons.Badge("http://github.com").Letter);
        Assert.Contains(youtube.Color, SiteIcons.BadgeColors);
        Assert.Equal('?', SiteIcons.Badge("not a url").Letter);
    }

    [Fact]
    public void ShouldFetch_Site_LikeGames_ThirtyDaysForAMiss()
    {
        Assert.True(SiteIcons.ShouldFetch(record: null, now: Now));
        Assert.False(SiteIcons.ShouldFetch(new SiteRecord(Now.AddDays(-100), "x.png"), Now));
        Assert.False(SiteIcons.ShouldFetch(new SiteRecord(Now.AddDays(-2)), Now));
        Assert.True(SiteIcons.ShouldFetch(new SiteRecord(Now.AddDays(-31)), Now));
    }

    // ---------- cover sizes ----------

    [Fact]
    public void CoverSizes_NormalAndLarge_OldSizesMapToTheNearest()
    {
        Assert.Equal(new GridSpan(1, 2), CoverSizes.SpanOf(null));
        Assert.Equal(new GridSpan(1, 2), CoverSizes.SpanOf(new GridSpan(1, 1)));
        Assert.Equal(new GridSpan(2, 4), CoverSizes.SpanOf(new GridSpan(2, 2))); // the owner's two big games: twice now
        Assert.Equal(new GridSpan(2, 4), CoverSizes.SpanOf(new GridSpan(4, 4)));
        Assert.Equal(CoverSizes.Large, CoverSizes.SpanOf(CoverSizes.Large));
    }

    [Fact]
    public void SpanOf_ACover_UsesCoverSizes_AnIconGameKeepsItsGridSize()
    {
        var cover = VirtualItem.Create(@"C:\lib\Detroit.url") with { GameId = "steam:1222140", Size = new GridSpan(2, 2) };
        Assert.Equal(new GridSpan(2, 4), FenceGrid.SpanOf(cover));
        Assert.Equal(new GridSpan(2, 2), FenceGrid.SpanOf(cover with { ShowAs = ItemShow.Icon }));
        Assert.Equal(new GridSpan(1, 2), FenceGrid.SpanOf(cover with { Size = null }));
    }

    // ---------- settings ----------

    [Fact]
    public void Settings_OnlineArtIsNotAskedYet_AndChoicesRoundTrip()
    {
        var fresh = NeoFences.Core.Model.NeoFencesConfig.CreateDefault();
        Assert.Null(fresh.Library.OnlineArt);
        Assert.Empty(fresh.Library.CoverChoices);
        var chosen = fresh with { Library = fresh.Library with { OnlineArt = true, CoverChoices = new Dictionary<string, string> { ["steam:1"] = "choice-steam-1.jpg" } } };
        var read = NeoFences.Core.Config.ConfigJson.Deserialize(NeoFences.Core.Config.ConfigJson.Serialize(chosen));
        Assert.True(read.Library.OnlineArt);
        Assert.Equal("choice-steam-1.jpg", read.Library.CoverChoices["steam:1"]);
        Assert.DoesNotContain("onlineArt", NeoFences.Core.Config.ConfigJson.Serialize(fresh)); // not asked: nothing written
    }

    [Fact]
    public void Normalize_KeepsOnlineArtAndChoices_AsPlainFileNames()
    {
        var config = NeoFences.Core.Model.NeoFencesConfig.CreateDefault();
        config = config with { Library = config.Library with { OnlineArt = false, CoverChoices = new Dictionary<string, string>
        {
            ["steam:1"] = "choice-steam-1.jpg",
            ["steam:2"] = @"..\..\Windows\evil.jpg", // hand-edited: only a file name in the covers folder counts
            ["steam:3"] = " ",
            [" "] = "x.jpg",
        } } };
        var normal = NeoFences.Core.Config.ConfigNormalizer.Normalize(config);
        Assert.False(normal.Library.OnlineArt);
        Assert.Equal("choice-steam-1.jpg", normal.Library.CoverChoices["steam:1"]);
        Assert.Equal("evil.jpg", normal.Library.CoverChoices["steam:2"]);
        Assert.Equal(2, normal.Library.CoverChoices.Count);
        Assert.True(normal.Library.CoverChoices.ContainsKey("STEAM:1")); // ids ignore case
        Assert.Empty(NeoFences.Core.Config.ConfigNormalizer.Normalize(config with { Library = config.Library with { CoverChoices = null! } }).Library.CoverChoices);
    }

    // ---------- leftover watchdog files ----------

    [Fact]
    public void StaleWatchdogFiles_OnlyThoseOfEndedProcesses()
    {
        string[] names = ["watchdog-6992", "watchdog-25208", "watchdog-restarts.txt", "watchdog-.log", "config.json", "watchdog-12x"];
        var stale = WatchdogFiles.Stale(names, isRunning: processId => processId == 25208);
        Assert.Equal(["watchdog-6992"], stale);
    }
}
