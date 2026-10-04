using NeoFences.Core.Layouts;
using NeoFences.Core.Config;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Config;

public class ConfigNormalizerTests
{
    private static Fence DesktopFence(string title, params string[] items) => Fence.Create(title) with { Items = items };

    [Fact]
    public void MissingInbox_IsAddedFirst_OtherFencesKept()
    {
        var games = DesktopFence("Games", "a.lnk");

        var normalized = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = [games] });

        Assert.Equal(2, normalized.Fences.Count);
        Assert.True(normalized.Fences[0].IsInbox);
        Assert.Equal(games with { Items = [] }, normalized.Fences[1] with { Items = [] });
        Assert.Equal(["a.lnk"], normalized.Fences[1].Items);
    }

    [Fact]
    public void TwoInboxes_FirstStaysInbox_SecondBecomesNormalFenceWithItems()
    {
        var first = DesktopFence("Inbox", "a.txt") with { IsInbox = true };
        var second = DesktopFence("Inbox 2", "b.txt") with { IsInbox = true };

        var normalized = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = [first, second] });

        Assert.True(normalized.Fences[0].IsInbox);
        Assert.False(normalized.Fences[1].IsInbox);
        Assert.Equal(["b.txt"], normalized.Fences[1].Items);
    }

    [Fact]
    public void DuplicateFenceIds_LaterOneGetsNewId()
    {
        var inbox = DesktopFence("Inbox") with { IsInbox = true };
        var copy = DesktopFence("Copy") with { Id = inbox.Id };

        var normalized = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = [inbox, copy] });

        Assert.Equal(inbox.Id, normalized.Fences[0].Id);
        Assert.NotEqual(inbox.Id, normalized.Fences[1].Id);
        Assert.Equal("Copy", normalized.Fences[1].Title);
    }

    [Fact]
    public void ItemInTwoFences_StaysOnlyInFirst_ComparedCaseInsensitively()
    {
        var inbox = DesktopFence("Inbox", @"C:\Desktop\Crysis.lnk") with { IsInbox = true };
        var games = DesktopFence("Games", @"c:\desktop\crysis.LNK", @"C:\Desktop\AC.lnk");

        var normalized = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = [inbox, games] });

        Assert.Equal([@"C:\Desktop\Crysis.lnk"], normalized.Fences[0].Items);
        Assert.Equal([@"C:\Desktop\AC.lnk"], normalized.Fences[1].Items);
    }

    [Fact]
    public void PortalFence_ItemsAreCleared()
    {
        var portal = Fence.Create("Shots", FenceSource.Portal(@"D:\Shots")) with { Items = ["stray.png"] };

        var normalized = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = [portal] });

        Assert.Empty(normalized.Fences.Single(fence => fence.Id == portal.Id).Items);
    }

    [Theory]
    [InlineData(0, 48)]
    [InlineData(500, 48)]
    [InlineData(64, 64)]
    public void UnsupportedIconSize_FallsBackTo48(int loadedSize, int expectedSize)
    {
        var fence = Fence.Create("Games") with { IconSize = loadedSize };

        var normalized = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = [fence] });

        Assert.Equal(expectedSize, normalized.Fences.Single(candidate => candidate.Id == fence.Id).IconSize);
    }

    [Fact]
    public void NullsFromHandEditedJson_AreRepaired()
    {
        var config = ConfigJson.Deserialize("""
            { "schemaVersion": 1, "settings": { "peekHotkey": null }, "layouts": null,
              "fences": [ null, { "id": null, "title": null, "source": null, "items": [ "a.txt", null, "" ] } ] }
            """);

        var normalized = ConfigNormalizer.Normalize(config);

        Assert.Equal(new Settings(), normalized.Settings);
        Assert.Empty(normalized.Layouts);
        var repaired = normalized.Fences.Single(fence => !fence.IsInbox);
        Assert.False(string.IsNullOrWhiteSpace(repaired.Id));
        Assert.Equal("", repaired.Title);
        Assert.Equal(FenceSource.Desktop, repaired.Source);
        Assert.Equal(["a.txt"], repaired.Items);
    }

    [Fact]
    public void BrokenLayoutsFromHandEditedJson_AreRepaired_AndResolveStillWorks()
    {
        var inboxId = Fence.NewId();
        var config = ConfigJson.Deserialize($$"""
            { "schemaVersion": 1, "lastLayoutFingerprint": "half",
              "fences": [ { "id": "{{inboxId}}", "title": "Inbox", "isInbox": true } ],
              "layouts": {
                "nulled": null,
                "half": { "monitors": null, "fences": { "{{inboxId}}": null, "noMonitor": { "x": 1 } } },
                "zeroArea": { "monitors": { "DELL": { "workWidth": 0, "workHeight": 700 } },
                              "fences": { "{{inboxId}}": { "monitor": "DELL", "x": 5, "y": 6, "w": 300, "h": 200 } } } } }
            """);

        var normalized = ConfigNormalizer.Normalize(config);

        Assert.False(normalized.Layouts.ContainsKey("nulled"));
        Assert.Empty(normalized.Layouts["half"].Monitors);
        Assert.Empty(normalized.Layouts["half"].Fences);
        Assert.Empty(normalized.Layouts["zeroArea"].Monitors);
        Assert.Single(normalized.Layouts["zeroArea"].Fences);
        var monitor = new DisplayMonitor("DELL", 1920, 1080, 100, 1280, 700, IsPrimary: true);
        var (_, layout) = LayoutEngine.Resolve(normalized, [monitor]);
        Assert.True(layout.Fences.ContainsKey(inboxId));
    }

    [Fact]
    public void Inbox_IsAlwaysADesktopFence()
    {
        var portalInbox = Fence.Create("Inbox", FenceSource.Portal(@"D:Shots")) with { IsInbox = true };

        var normalized = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = [portalInbox] });

        Assert.Equal(FenceSource.Desktop, normalized.Inbox.Source);
    }

    [Fact]
    public void OutOfRangeEnumNumbers_FallBackToDefaults_WithoutClearingItems()
    {
        var config = ConfigJson.Deserialize("""
            { "schemaVersion": 1, "settings": { "rollupExpand": 9 },
              "fences": [ { "id": "f1", "title": "Games", "sort": 7, "source": { "kind": 5 }, "items": [ "a.lnk" ] } ] }
            """);

        var normalized = ConfigNormalizer.Normalize(config);

        var games = normalized.Fences.Single(fence => fence.Id == "f1");
        Assert.Equal(FenceSource.Desktop, games.Source);
        Assert.Equal(FenceSort.Manual, games.Sort);
        Assert.Equal(["a.lnk"], games.Items);
        Assert.Equal(RollupExpand.Hover, normalized.Settings.RollupExpand);
    }
}
