using NeoFences.Core.Layouts;
using NeoFences.Core.Config;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Config;

public class ConfigNormalizerTests
{
    [Fact]
    public void NoFences_StaysNoFences()
    {
        // M18: no Inbox is invented; the tray's "New fence" makes one.
        Assert.Empty(ConfigNormalizer.Normalize(new NeoFencesConfig()).Fences);
    }

    [Fact]
    public void DuplicateFenceIds_LaterOneGetsNewId()
    {
        var first = Fence.Create("First");
        var copy = Fence.Create("Copy") with { Id = first.Id };

        var normalized = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = [first, copy] });

        Assert.Equal(first.Id, normalized.Fences[0].Id);
        Assert.NotEqual(first.Id, normalized.Fences[1].Id);
        Assert.Equal("Copy", normalized.Fences[1].Title);
    }

    [Fact]
    public void TwoLibraryFences_OnlyTheFirstStaysTheLibrary()
    {
        var normalized = ConfigNormalizer.Normalize(new NeoFencesConfig
        {
            Fences = [Fence.Create("Games", isLibrary: true), Fence.Create("Games 2", isLibrary: true)],
        });

        Assert.Equal([true, false], normalized.Fences.Select(fence => fence.IsLibrary));
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
            { "schemaVersion": 5, "settings": { "peekHotkey": null }, "layouts": null,
              "fences": [ null, { "id": null, "title": null, "tabs": null } ] }
            """);

        var normalized = ConfigNormalizer.Normalize(config);

        Assert.Equal(new Settings(), normalized.Settings);
        Assert.Empty(normalized.Layouts);
        var repaired = Assert.Single(normalized.Fences);
        Assert.False(string.IsNullOrWhiteSpace(repaired.Id));
        Assert.Equal("", repaired.Title);
        Assert.Empty(repaired.Tabs);
    }

    [Fact]
    public void BrokenLayoutsFromHandEditedJson_AreRepaired_AndResolveStillWorks()
    {
        var fenceId = Fence.NewId();
        var config = ConfigJson.Deserialize($$"""
            { "schemaVersion": 5, "lastLayoutFingerprint": "half",
              "fences": [ { "id": "{{fenceId}}", "title": "Fence" } ],
              "layouts": {
                "nulled": null,
                "half": { "monitors": null, "fences": { "{{fenceId}}": null, "noMonitor": { "x": 1 } } },
                "zeroArea": { "monitors": { "DELL": { "workWidth": 0, "workHeight": 700 } },
                              "fences": { "{{fenceId}}": { "monitor": "DELL", "x": 5, "y": 6, "w": 300, "h": 200 } } } } }
            """);

        var normalized = ConfigNormalizer.Normalize(config);

        Assert.False(normalized.Layouts.ContainsKey("nulled"));
        Assert.Empty(normalized.Layouts["half"].Monitors);
        Assert.Empty(normalized.Layouts["half"].Fences);
        Assert.Empty(normalized.Layouts["zeroArea"].Monitors);
        Assert.Single(normalized.Layouts["zeroArea"].Fences);
        var monitor = new DisplayMonitor("DELL", 1920, 1080, 100, 1280, 700, IsPrimary: true);
        var (_, layout) = LayoutEngine.Resolve(normalized, [monitor]);
        Assert.True(layout.Fences.ContainsKey(fenceId));
    }

    [Fact]
    public void OutOfRangeEnumNumbers_FallBackToDefaults()
    {
        var config = ConfigJson.Deserialize("""
            { "schemaVersion": 5, "settings": { "rollupExpand": 9 },
              "fences": [ { "id": "f1", "title": "Games", "labels": 7 } ] }
            """);

        var normalized = ConfigNormalizer.Normalize(config);

        Assert.Equal(LabelMode.Always, normalized.Fences.Single().Labels);
        Assert.Equal(RollupExpand.Hover, normalized.Settings.RollupExpand);
    }
}
