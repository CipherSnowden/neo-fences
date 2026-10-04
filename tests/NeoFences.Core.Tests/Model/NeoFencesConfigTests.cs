using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Model;

public class NeoFencesConfigTests
{
    [Fact]
    public void Default_HasExactlyOneOrdinaryFence()
    {
        // First run (M18 spec §5): one empty fence; its hint says how to fill it. No Inbox, no auto-fill.
        var fence = Assert.Single(NeoFencesConfig.CreateDefault().Fences);
        Assert.Equal("Fence", fence.Title);
        Assert.False(fence.IsLibrary);
    }

    [Fact]
    public void Default_KeepsTheDesktopIconsVisible()
    {
        // ADR-040: native desktop icons stay visible unless the user switches "Hide desktop icons" on.
        Assert.False(NeoFencesConfig.CreateDefault().Settings.HideDesktopIcons);
    }

    [Fact]
    public void Default_UsesSpecSettings()
    {
        var settings = NeoFencesConfig.CreateDefault().Settings;

        Assert.Equal("Ctrl+Alt+Space", settings.PeekHotkey);
        Assert.True(settings.StartWithWindows);
        Assert.True(settings.GameMode);
        Assert.Equal(RollupExpand.Hover, settings.RollupExpand);
    }

    [Fact]
    public void NewFences_GetUniqueIds()
    {
        Assert.NotEqual(Fence.Create("A").Id, Fence.Create("B").Id);
    }

    [Fact]
    public void WithFence_ReplacesByIdAndKeepsOrder()
    {
        var games = Fence.Create("Games");
        var config = NeoFencesConfig.CreateDefault() with { Fences = [Fence.Create("Tools"), games] };

        var updated = config.WithFence(games with { Title = "Games 2" });

        Assert.Equal(["Tools", "Games 2"], updated.Fences.Select(fence => fence.Title));
        Assert.Equal("Games", config.Fences[1].Title); // original untouched
    }
}
