using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Model;

public class NeoFencesConfigTests
{
    [Fact]
    public void Default_HasExactlyOneEmptyInbox()
    {
        var config = NeoFencesConfig.CreateDefault();

        var inbox = Assert.Single(config.Fences);
        Assert.True(inbox.IsInbox);
        Assert.Equal("Inbox", inbox.Title);
        Assert.Empty(inbox.Items);
        Assert.Same(inbox, config.Inbox);
    }

    [Fact]
    public void Default_TakeoverIsOff_UntilSignOutTestPasses()
    {
        // ADR-011: Takeover must not ship enabled by default until C8 passes.
        Assert.False(NeoFencesConfig.CreateDefault().Settings.Takeover);
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
        var config = NeoFencesConfig.CreateDefault() with { Fences = [NeoFencesConfig.CreateDefault().Inbox, games] };

        var updated = config.WithFence(games with { Title = "Games 2" });

        Assert.Equal(["Inbox", "Games 2"], updated.Fences.Select(fence => fence.Title));
        Assert.Equal("Games", config.Fences[1].Title); // original untouched
    }

    [Fact]
    public void Default_TakeoverPromptNotAnsweredYet()
    {
        // M2b first run: the Inbox asks once whether to hide desktop icons (user decision 2026-10-02).
        Assert.False(NeoFencesConfig.CreateDefault().Settings.TakeoverPromptAnswered);
    }
}
