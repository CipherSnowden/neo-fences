using NeoFences.Core.Membership;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Model;

public class PortalTests
{
    private static NeoFencesConfig WithInbox(params Fence[] others) =>
        new() { Fences = [Fence.Create("Inbox") with { IsInbox = true }, .. others] };

    [Fact]
    public void CreatePortal_MirrorsTheFolder_NewestFirst()
    {
        var (config, portal) = FenceMembership.CreatePortal(WithInbox(), title: "Downloads", folderPath: @"C:\Users\cipher\Downloads");

        Assert.Equal(FenceSource.Portal(@"C:\Users\cipher\Downloads"), portal.Source);
        Assert.Equal(FenceSort.Date, portal.Sort); // user choice 2026-10-03
        Assert.Empty(portal.Items);
        Assert.Contains(config.Fences, fence => fence.Id == portal.Id);
    }

    [Fact]
    public void CreatePortal_BlankFolder_Throws() =>
        Assert.Throws<ArgumentException>(() => FenceMembership.CreatePortal(WithInbox(), title: "x", folderPath: "  "));

    [Fact]
    public void SetSort_IsStored()
    {
        var portal = Fence.Create("Downloads", FenceSource.Portal(@"C:\Downloads"));

        var sorted = FenceEdits.SetSort(WithInbox(portal), portal.Id, FenceSort.Name);

        Assert.Equal(FenceSort.Name, sorted.Fences.Single(fence => fence.Id == portal.Id).Sort);
    }

    [Fact]
    public void SetItemOrder_ReordersADesktopFence()
    {
        // "Sort by" on a desktop fence is a one-time reorder; dragging afterwards still works (Manual).
        var games = Fence.Create("Games") with { Items = [@"C:\D\b.lnk", @"C:\D\a.lnk", @"C:\D\c.lnk"] };

        var ordered = FenceEdits.SetItemOrder(WithInbox(games), games.Id, [@"C:\D\a.lnk", @"C:\D\b.lnk", @"C:\D\C.LNK"]);

        Assert.Equal([@"C:\D\a.lnk", @"C:\D\b.lnk", @"C:\D\c.lnk"], ordered.Fences.Single(fence => fence.Id == games.Id).Items);
    }

    [Fact]
    public void SetItemOrder_MustBeTheSameItems()
    {
        // Never drops or invents items (hard rule 1: an item must not vanish from its fence by a sort).
        var games = Fence.Create("Games") with { Items = [@"C:\D\a.lnk", @"C:\D\b.lnk"] };

        Assert.Throws<ArgumentException>(() => FenceEdits.SetItemOrder(WithInbox(games), games.Id, [@"C:\D\a.lnk"]));
    }
}
