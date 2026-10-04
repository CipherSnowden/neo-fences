using NeoFences.Core.Membership;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Membership;

public class DesktopChangeTests
{
    private const string Notes = @"C:\Users\cipher\Desktop\notes.txt";
    private const string Shot = @"C:\Users\cipher\Desktop\screenshot.png";

    private static (NeoFencesConfig Config, Fence Games) Sample()
    {
        var inbox = Fence.Create("Inbox") with { IsInbox = true };
        var games = Fence.Create("Games") with { Items = [Notes] };
        return (new NeoFencesConfig { Fences = [inbox, games] }, games);
    }

    [Fact]
    public void Created_GoesToInbox()
    {
        var (config, _) = Sample();

        var updated = FenceMembership.Apply(config, new DesktopChange.Created(Shot));

        Assert.Equal([Shot], updated.Inbox.Items);
    }

    [Fact]
    public void Deleted_LeavesItsFence()
    {
        var (config, games) = Sample();

        var updated = FenceMembership.Apply(config, new DesktopChange.Deleted(Notes));

        Assert.Empty(updated.Fences.Single(fence => fence.Id == games.Id).Items);
    }

    [Fact]
    public void Renamed_StaysInItsFence()
    {
        var (config, games) = Sample();
        const string renamed = @"C:\Users\cipher\Desktop\todo.txt";

        var updated = FenceMembership.Apply(config, new DesktopChange.Renamed(Notes, renamed));

        Assert.Equal([renamed], updated.Fences.Single(fence => fence.Id == games.Id).Items);
    }

    [Fact]
    public void CreatedTwice_IsAddedOnce()
    {
        // Watchers can report the same create twice (e.g. a download finishing); the second must be a no-op.
        var (config, _) = Sample();
        var once = FenceMembership.Apply(config, new DesktopChange.Created(Shot));

        Assert.Same(once, FenceMembership.Apply(once, new DesktopChange.Created(Shot)));
    }
}
