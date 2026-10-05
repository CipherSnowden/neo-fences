using NeoFences.Core.Config;
using NeoFences.Core.Items;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Model;

/// <summary>The first-run welcome (M30, ADR-051): which fence it is, and when it ends.</summary>
public class WelcomeTests
{
    private static ItemsDocument WithItem(ItemsDocument items, string fenceId) =>
        ItemEdits.Add(items, fenceId, [VirtualItem.Create(@"C:\Users\Public\Desktop\Game.lnk")]).Document;

    [Fact]
    public void Default_FirstFenceIsTheWelcome()
    {
        var fence = Assert.Single(NeoFencesConfig.CreateDefault().Fences);
        Assert.True(fence.Welcome);
        Assert.Equal("Fence", fence.Title);
    }

    [Fact]
    public void NewFences_AreNeverTheWelcome()
    {
        var (_, fence) = FenceEdits.CreateFence(NeoFencesConfig.CreateDefault(), "Games");
        Assert.False(fence.Welcome);
        Assert.False(Fence.Create("Apps").Welcome);
    }

    [Fact]
    public void Welcome_RoundTrips_AndIsWrittenOnlyWhenSet()
    {
        var config = NeoFencesConfig.CreateDefault();
        var (withOther, _) = FenceEdits.CreateFence(config, "Games");

        var json = ConfigJson.Serialize(withOther);
        var restored = ConfigJson.Deserialize(json);

        Assert.Equal(1, json.Split("\"welcome\"").Length - 1); // only the welcome fence carries it
        Assert.True(restored.Fences[0].Welcome);
        Assert.False(restored.Fences[1].Welcome);
    }

    [Fact]
    public void ExistingConfig_WithoutTheField_HasNoWelcome()
    {
        var config = ConfigJson.Deserialize("""{ "schemaVersion": 5, "fences": [ { "id": "a", "title": "Games" } ] }""");
        Assert.False(Assert.Single(config.Fences).Welcome);
    }

    [Fact]
    public void ClearIfFilled_ClearsTheFlag_WhenTheWelcomeFenceHasItems()
    {
        var config = NeoFencesConfig.CreateDefault();
        var welcomeId = config.Fences[0].Id;

        var cleared = WelcomeEdits.ClearIfFilled(config, WithItem(new ItemsDocument(), welcomeId));

        Assert.False(Assert.Single(cleared.Fences).Welcome);
    }

    [Fact]
    public void ClearIfFilled_ReturnsTheSameConfig_WhileTheWelcomeFenceIsEmpty()
    {
        var config = NeoFencesConfig.CreateDefault();
        var (withOther, other) = FenceEdits.CreateFence(config, "Games");

        var result = WelcomeEdits.ClearIfFilled(withOther, WithItem(new ItemsDocument(), other.Id));

        Assert.Same(withOther, result); // the host saves only on a change
    }

    [Fact]
    public void AfterDesktopFill_RemovesTheEmptyWelcomeFence_WhenNewFencesGotItems()
    {
        var config = NeoFencesConfig.CreateDefault();
        var (filled, games) = FenceEdits.CreateFence(config, "Games");
        var items = WithItem(new ItemsDocument(), games.Id);

        var result = WelcomeEdits.AfterDesktopFill(filled, items, added: 1, newFences: 1);

        Assert.Equal("Games", Assert.Single(result.Fences).Title);
    }

    [Theory]
    [InlineData(0, 1)] // nothing added
    [InlineData(3, 0)] // added only into existing fences
    public void AfterDesktopFill_KeepsTheWelcome_WhenTheSortMadeNoFilledFence(int added, int newFences)
    {
        var config = NeoFencesConfig.CreateDefault();

        var result = WelcomeEdits.AfterDesktopFill(config, new ItemsDocument(), added, newFences);

        Assert.Same(config, result);
    }

    [Fact]
    public void AfterDesktopFill_KeepsTheWelcomeFence_WhenItReceivedItems()
    {
        var config = NeoFencesConfig.CreateDefault();
        var welcomeId = config.Fences[0].Id;
        var (filled, games) = FenceEdits.CreateFence(config, "Games");
        var items = WithItem(WithItem(new ItemsDocument(), games.Id), welcomeId);

        var result = WelcomeEdits.AfterDesktopFill(filled, items, added: 2, newFences: 1);

        Assert.Equal(2, result.Fences.Count);
    }

    [Fact]
    public void AfterDesktopFill_RemovesAWelcomeFenceThatIsATab_AndTheBoxStaysWhole()
    {
        var config = NeoFencesConfig.CreateDefault();
        var welcomeId = config.Fences[0].Id;
        var (withNotes, notes) = FenceEdits.CreateFence(config, "Notes");
        var boxed = FenceTabs.Merge(withNotes, movingFenceId: welcomeId, targetFenceId: notes.Id); // the welcome is a tab of Notes' box
        var (filled, games) = FenceEdits.CreateFence(boxed, "Games");
        var items = WithItem(WithItem(new ItemsDocument(), games.Id), notes.Id);

        var result = WelcomeEdits.AfterDesktopFill(filled, items, added: 1, newFences: 1);

        Assert.DoesNotContain(result.Fences, fence => fence.Id == welcomeId);
        Assert.DoesNotContain(result.Fences.SelectMany(fence => fence.Tabs), tab => tab == welcomeId);
        Assert.Contains(result.Fences, fence => fence.Id == notes.Id);
    }

    [Fact]
    public void AfterDesktopFill_NeverRemovesAnOrdinaryEmptyFence()
    {
        var (config, _) = FenceEdits.CreateFence(new NeoFencesConfig(), "Empty");
        var (filled, games) = FenceEdits.CreateFence(config, "Games");
        var items = WithItem(new ItemsDocument(), games.Id);

        var result = WelcomeEdits.AfterDesktopFill(filled, items, added: 1, newFences: 1);

        Assert.Same(filled, result);
    }
}
