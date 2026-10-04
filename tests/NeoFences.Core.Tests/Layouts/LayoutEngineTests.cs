using NeoFences.Core.Config;
using NeoFences.Core.Layouts;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Layouts;

public class LayoutEngineTests
{
    private static readonly DisplayMonitor Dell4K = new("DELL", 3840, 2160, 150, WorkWidth: 2560, WorkHeight: 1400, IsPrimary: true);
    private static readonly DisplayMonitor Dell1080 = new("DELL", 1920, 1080, 100, WorkWidth: 1280, WorkHeight: 700, IsPrimary: true);
    private static readonly DisplayMonitor Lg = new("LG", 1920, 1080, 100, WorkWidth: 1920, WorkHeight: 1032, IsPrimary: false);

    private static (NeoFencesConfig Config, Fence Games) ConfigWithGames()
    {
        var games = Fence.Create("Games");
        var config = NeoFencesConfig.CreateDefault();
        return (config with { Fences = [config.Fences[0], games] }, games);
    }

    private static NeoFencesConfig WithSavedLayout(NeoFencesConfig config, string fingerprint, Layout layout) =>
        config with { Layouts = new Dictionary<string, Layout> { [fingerprint] = layout }, LastLayoutFingerprint = fingerprint };

    [Fact]
    public void Fingerprint_IsOrderIndependentAndReadable()
    {
        Assert.Equal("2mon:DELL-3840x2160@150%+LG-1920x1080@100%", DisplayFingerprint.Of([Lg, Dell4K]));
        Assert.Equal(DisplayFingerprint.Of([Dell4K, Lg]), DisplayFingerprint.Of([Lg, Dell4K]));
        Assert.NotEqual(DisplayFingerprint.Of([Dell4K]), DisplayFingerprint.Of([Dell1080]));
    }

    [Fact]
    public void FirstRun_PlacesFencesSideBySideOnPrimary_AndRemembersFingerprint()
    {
        var (config, games) = ConfigWithGames();

        var (resolved, layout) = LayoutEngine.Resolve(config, [Lg, Dell4K]);

        Assert.Equal(new FenceRect("DELL", 24, 24, 320, 220), layout.Fences[config.Fences[0].Id]);
        // M5 smart placement: beside the Inbox with an 8 DIP gap, never stacked on top of it (the old cascade overlapped).
        Assert.Equal(new FenceRect("DELL", 352, 24, 320, 220), layout.Fences[games.Id]);
        Assert.Equal("2mon:DELL-3840x2160@150%+LG-1920x1080@100%", resolved.LastLayoutFingerprint);
        Assert.Equal(layout.Fences, resolved.Layouts[resolved.LastLayoutFingerprint!].Fences);
    }

    [Fact]
    public void KnownFingerprint_RestoresExactly()
    {
        var (config, games) = ConfigWithGames();
        var saved = new Layout
        {
            Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(2560, 1400), ["LG"] = new(1920, 1032) },
            Fences = new Dictionary<string, FenceRect>
            {
                [config.Fences[0].Id] = new("LG", 100, 200, 400, 300),
                [games.Id] = new("DELL", 1000, 500, 420, 260),
            },
        };
        config = WithSavedLayout(config, DisplayFingerprint.Of([Dell4K, Lg]), saved);

        var (_, layout) = LayoutEngine.Resolve(config, [Dell4K, Lg]);

        Assert.Equal(saved.Fences[config.Fences[0].Id], layout.Fences[config.Fences[0].Id]);
        Assert.Equal(saved.Fences[games.Id], layout.Fences[games.Id]);
    }

    [Fact]
    public void NewResolution_ScalesFromLastLayout_AndKeepsOldLayoutForLater()
    {
        var (config, games) = ConfigWithGames();
        var fourKFingerprint = DisplayFingerprint.Of([Dell4K]);
        config = WithSavedLayout(config, fourKFingerprint, new Layout
        {
            Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(2560, 1400) },
            Fences = new Dictionary<string, FenceRect>
            {
                [config.Fences[0].Id] = new("DELL", 0, 0, 640, 350),
                [games.Id] = new("DELL", 1280, 700, 512, 280),
            },
        });

        var (resolved, layout) = LayoutEngine.Resolve(config, [Dell1080]);

        Assert.Equal(new FenceRect("DELL", 0, 0, 320, 175), layout.Fences[config.Fences[0].Id]);
        Assert.Equal(new FenceRect("DELL", 640, 350, 256, 140), layout.Fences[games.Id]);
        Assert.True(resolved.Layouts.ContainsKey(fourKFingerprint)); // going back to 4K restores the original

        var (_, backTo4K) = LayoutEngine.Resolve(resolved, [Dell4K]);
        Assert.Equal(new FenceRect("DELL", 1280, 700, 512, 280), backTo4K.Fences[games.Id]);
    }

    [Fact]
    public void MonitorUnplugged_FenceMovesToPrimary_ScaledAndFullyOnScreen()
    {
        var (config, games) = ConfigWithGames();
        config = WithSavedLayout(config, DisplayFingerprint.Of([Dell1080, Lg]), new Layout
        {
            Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(1280, 700), ["LG"] = new(1920, 1032) },
            Fences = new Dictionary<string, FenceRect> { [games.Id] = new("LG", 1800, 900, 1900, 1000) },
        });

        var (_, layout) = LayoutEngine.Resolve(config, [Dell1080]);

        var rect = layout.Fences[games.Id];
        Assert.Equal("DELL", rect.Monitor);
        Assert.True(rect.X >= 0 && rect.Y >= 0, "fence starts on-screen");
        Assert.True(rect.X + rect.W <= 1280 && rect.Y + rect.H <= 700, "fence ends on-screen");
    }

    [Fact]
    public void DeletedFences_AreDroppedFromLayout_NewFencesGetPlaced()
    {
        var (config, games) = ConfigWithGames();
        var fingerprint = DisplayFingerprint.Of([Dell4K]);
        config = WithSavedLayout(config, fingerprint, new Layout
        {
            Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(2560, 1400) },
            Fences = new Dictionary<string, FenceRect>
            {
                [config.Fences[0].Id] = new("DELL", 500, 500, 300, 200),
                ["deleted-fence"] = new("DELL", 0, 0, 300, 200),
            },
        });

        var (_, layout) = LayoutEngine.Resolve(config, [Dell4K]);

        Assert.False(layout.Fences.ContainsKey("deleted-fence"));
        Assert.Equal(new FenceRect("DELL", 500, 500, 300, 200), layout.Fences[config.Fences[0].Id]);
        Assert.Equal(new FenceRect("DELL", 24, 24, 320, 220), layout.Fences[games.Id]);
    }

    [Theory]
    [InlineData(-50, -50, 300, 200, 0, 0, 300, 200)]          // off the top-left
    [InlineData(1200, 650, 300, 200, 980, 500, 300, 200)]     // off the bottom-right
    [InlineData(10, 10, 50, 20, 10, 10, 120, 60)]             // below minimum size
    [InlineData(10, 10, 5000, 3000, 0, 0, 1280, 700)]         // larger than the work area
    public void Clamp_KeepsFenceInsideWorkArea(double x, double y, double w, double h, double expectedX, double expectedY, double expectedW, double expectedH)
    {
        var clamped = LayoutEngine.Clamp(new FenceRect("DELL", x, y, w, h), new MonitorArea(1280, 700));

        Assert.Equal(new FenceRect("DELL", expectedX, expectedY, expectedW, expectedH), clamped);
    }

    [Fact]
    public void NoMonitors_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => LayoutEngine.Resolve(NeoFencesConfig.CreateDefault(), []));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(double.NaN, 700)]
    [InlineData(-1280, 700)]
    [InlineData(double.PositiveInfinity, 700)]
    public void InvalidWorkArea_IsRejected(double workWidth, double workHeight)
    {
        // A monitor query during an Explorer restart or driver reset can return garbage; it must never be saved.
        var badMonitor = Dell1080 with { WorkWidth = workWidth, WorkHeight = workHeight };

        Assert.Throws<ArgumentException>(() => LayoutEngine.Resolve(NeoFencesConfig.CreateDefault(), [badMonitor]));
    }

    [Fact]
    public void SavedMonitorAreaOfZero_ScalesAsOne_AndNeverProducesNonFiniteRects()
    {
        var (config, games) = ConfigWithGames();
        config = WithSavedLayout(config, "old", new Layout
        {
            Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(0, 0) },
            Fences = new Dictionary<string, FenceRect> { [games.Id] = new("DELL", 0, 0, 300, 200) },
        });

        var (resolved, layout) = LayoutEngine.Resolve(config, [Dell1080]);

        Assert.Equal(new FenceRect("DELL", 0, 0, 300, 200), layout.Fences[games.Id]);
        ConfigJson.Serialize(resolved); // throws on NaN/Infinity
    }

    [Fact]
    public void KnownLayout_StoredRectsStayUnclamped_DisplayRectsAreClamped()
    {
        // Review deferral: a temporarily smaller work area (taskbar moved) must not shift stored positions for good.
        var (config, games) = ConfigWithGames();
        var fingerprint = DisplayFingerprint.Of([Dell1080]);
        config = WithSavedLayout(config, fingerprint, new Layout
        {
            Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(1280, 700) },
            Fences = new Dictionary<string, FenceRect> { [games.Id] = new("DELL", 1000, 500, 300, 200) },
        });
        var narrowerWorkArea = Dell1080 with { WorkWidth = 1200 };

        var (resolved, layout) = LayoutEngine.Resolve(config, [narrowerWorkArea]);

        Assert.Equal(new FenceRect("DELL", 900, 500, 300, 200), layout.Fences[games.Id]);
        Assert.Equal(new FenceRect("DELL", 1000, 500, 300, 200), resolved.Layouts[fingerprint].Fences[games.Id]);
    }

    [Fact]
    public void WithFenceRect_StoresMovedRectUnderCurrentFingerprint()
    {
        var (config, games) = ConfigWithGames();
        var (resolved, _) = LayoutEngine.Resolve(config, [Dell1080]);
        var moved = new FenceRect("DELL", 700, 300, 400, 250);

        var updated = LayoutEngine.WithFenceRect(resolved, fingerprint: resolved.LastLayoutFingerprint!, fenceId: games.Id, rect: moved);

        Assert.Equal(moved, updated.Layouts[resolved.LastLayoutFingerprint!].Fences[games.Id]);
        Assert.Equal(resolved.Layouts[resolved.LastLayoutFingerprint!].Fences[config.Fences[0].Id], updated.Layouts[resolved.LastLayoutFingerprint!].Fences[config.Fences[0].Id]);
    }

    [Fact]
    public void NewFence_GoesToTheNextRow_WhenTheTopRowIsFull()
    {
        var (config, games) = ConfigWithGames();
        var fingerprint = DisplayFingerprint.Of([Dell1080]);
        config = WithSavedLayout(config, fingerprint, new Layout
        {
            Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(1280, 700) },
            Fences = new Dictionary<string, FenceRect>
            {
                [config.Fences[0].Id] = new("DELL", 24, 24, 1232, 220), // the whole top row
            },
        });

        var (_, layout) = LayoutEngine.Resolve(config, [Dell1080]);

        Assert.Equal(new FenceRect("DELL", 24, 252, 320, 220), layout.Fences[games.Id]);
    }

    [Fact]
    public void NewFence_OnAFullScreen_StillLandsOnScreen()
    {
        var (config, games) = ConfigWithGames();
        var fingerprint = DisplayFingerprint.Of([Dell1080]);
        config = WithSavedLayout(config, fingerprint, new Layout
        {
            Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(1280, 700) },
            Fences = new Dictionary<string, FenceRect> { [config.Fences[0].Id] = new("DELL", 0, 0, 1280, 700) },
        });

        var (_, layout) = LayoutEngine.Resolve(config, [Dell1080]);

        var rect = layout.Fences[games.Id];
        Assert.True(rect.X >= 0 && rect.Y >= 0 && rect.X + rect.W <= 1280 && rect.Y + rect.H <= 700);
    }

    [Fact]
    public void AFenceMadeOnAnotherSetup_KeepsItsPlaceWhenAKnownSetupComesBack()
    {
        // Made on the laptop screen (1080p), back on the known 4K setup: adapt its last rect, do not re-place it (M1 carry-over).
        var (config, games) = ConfigWithGames();
        var fourK = DisplayFingerprint.Of([Dell4K]);
        var fullHd = DisplayFingerprint.Of([Dell1080]);
        config = config with
        {
            Layouts = new Dictionary<string, Layout>
            {
                [fourK] = new() { Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(2560, 1400) }, Fences = new Dictionary<string, FenceRect> { [config.Fences[0].Id] = new("DELL", 0, 0, 640, 350) } },
                [fullHd] = new()
                {
                    Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(1280, 700) },
                    Fences = new Dictionary<string, FenceRect> { [config.Fences[0].Id] = new("DELL", 0, 0, 320, 175), [games.Id] = new("DELL", 640, 350, 256, 140) },
                },
            },
            LastLayoutFingerprint = fullHd,
        };

        var (resolved, layout) = LayoutEngine.Resolve(config, [Dell4K]);

        Assert.Equal(new FenceRect("DELL", 1280, 700, 512, 280), layout.Fences[games.Id]); // scaled ×2 from the 1080p layout
        Assert.Equal(new FenceRect("DELL", 0, 0, 640, 350), layout.Fences[config.Fences[0].Id]); // the known rect stays exact
        Assert.Equal(new FenceRect("DELL", 1280, 700, 512, 280), resolved.Layouts[fourK].Fences[games.Id]);
    }

    [Fact]
    public void DuplicateDeviceIds_AreMadeUnique_InOrder()
    {
        // Cloned / mirrored outputs can report the same device path; a duplicate key would stop every layout (M1 carry-over).
        Assert.Equal(["DELL", "LG", "DELL#2", "DELL#3"], DisplayFingerprint.UniqueDeviceIds(["DELL", "LG", "DELL", "DELL"]));
        Assert.Equal(["A", "B"], DisplayFingerprint.UniqueDeviceIds(["A", "B"]));
    }
}
