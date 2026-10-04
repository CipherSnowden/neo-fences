using NeoFences.Core.Membership;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Membership;

/// <summary>
/// Dropping fence items (M3b). The drop index is a position in the target fence's list as the user sees it during the
/// drag, i.e. still including the dragged items: "insert before the item that is shown at this index".
/// </summary>
public class DragDropTests
{
    private const string Desktop = @"C:\Users\cipher\Desktop\";
    private const string A = Desktop + "a.lnk", B = Desktop + "b.lnk", C = Desktop + "c.lnk", D = Desktop + "d.lnk";
    private const string X = Desktop + "x.txt", Y = Desktop + "y.txt";
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 2, 0, 0, TimeSpan.Zero);

    private static (NeoFencesConfig Config, Fence Games, Fence Work) Sample()
    {
        var inbox = Fence.Create("Inbox") with { IsInbox = true };
        var games = Fence.Create("Games") with { Items = [A, B, C, D] };
        var work = Fence.Create("Work") with { Items = [X, Y] };
        return (new NeoFencesConfig { Fences = [inbox, games, work] }, games, work);
    }

    private static IReadOnlyList<string> ItemsOf(NeoFencesConfig config, string fenceId) => config.Fences.Single(fence => fence.Id == fenceId).Items;

    [Fact]
    public void WithinAFence_DraggingForward_LandsBeforeTheTargetItem()
    {
        var (config, games, _) = Sample();

        Assert.Equal([B, C, A, D], ItemsOf(FenceMembership.MoveItems(config, [A], games.Id, insertAt: 3), games.Id));
    }

    [Fact]
    public void WithinAFence_DraggingBackward_LandsBeforeTheTargetItem()
    {
        var (config, games, _) = Sample();

        Assert.Equal([A, D, B, C], ItemsOf(FenceMembership.MoveItems(config, [D], games.Id, insertAt: 1), games.Id));
    }

    [Theory]
    [InlineData(1)] // before itself
    [InlineData(2)] // just after itself
    public void DroppedOnItsOwnSpot_NothingMoves(int insertAt)
    {
        var (config, games, _) = Sample();

        Assert.Equal([A, B, C, D], ItemsOf(FenceMembership.MoveItems(config, [B], games.Id, insertAt), games.Id));
    }

    [Fact]
    public void BetweenFences_LandsAtTheDropPosition()
    {
        var (config, games, work) = Sample();

        var moved = FenceMembership.MoveItems(config, [A], work.Id, insertAt: 1);

        Assert.Equal([X, A, Y], ItemsOf(moved, work.Id));
        Assert.Equal([B, C, D], ItemsOf(moved, games.Id));
    }

    [Fact]
    public void SeveralItems_KeepTheOrderTheyHadInTheirFences_NotTheSelectionOrder()
    {
        var (config, _, work) = Sample();

        var moved = FenceMembership.MoveItems(config, [C, A], work.Id, insertAt: 2);

        Assert.Equal([X, Y, A, C], ItemsOf(moved, work.Id));
    }

    [Fact]
    public void IndexPastTheEnd_Appends_UnknownRefsAreIgnored()
    {
        var (config, games, work) = Sample();

        var moved = FenceMembership.MoveItems(config, [A, Desktop + "gone.txt"], work.Id, insertAt: 99);

        Assert.Equal([X, Y, A], ItemsOf(moved, work.Id));
        Assert.Equal([B, C, D], ItemsOf(moved, games.Id));
    }

    [Fact]
    public void OntoAPortalFence_Throws()
    {
        var (config, _, _) = Sample();
        var portal = Fence.Create("Downloads", FenceSource.Portal(@"C:\Users\cipher\Downloads"));
        config = config with { Fences = [.. config.Fences, portal] };

        Assert.Throws<ArgumentException>(() => FenceMembership.MoveItems(config, [A], portal.Id, insertAt: 0));
    }

    [Fact]
    public void FilesDroppedFromExplorer_ArriveWhereTheyWereDropped()
    {
        var (config, _, work) = Sample();
        const string first = Desktop + "photo.jpg", second = Desktop + "notes.txt";

        var expected = FenceMembership.ExpectArrivals(recent: [], itemRefs: [first, second], fenceId: work.Id, insertAt: 1, now: Now);
        var (afterFirst, stillExpected) = FenceMembership.Apply(config, new DesktopChange.Created(first), expected, now: Now.AddMilliseconds(300));
        var (afterSecond, _) = FenceMembership.Apply(afterFirst, new DesktopChange.Created(second), stillExpected, now: Now.AddMilliseconds(400));

        Assert.Equal([X, first, second, Y], ItemsOf(afterSecond, work.Id));
        Assert.Empty(afterSecond.Inbox.Items);
    }

    [Fact]
    public void ADroppedFileArrivingAMinuteLater_StillLandsWhereItWasDropped()
    {
        // M3b review I1: Windows creates each file when it starts copying it; big files or a slow USB stick take minutes.
        var (config, _, work) = Sample();
        const string slow = Desktop + "big.iso";

        var expected = FenceMembership.ExpectArrivals(recent: [], itemRefs: [slow], fenceId: work.Id, insertAt: 0, now: Now);
        var (arrived, _) = FenceMembership.Apply(config, new DesktopChange.Created(slow), expected, now: Now.AddMinutes(1));

        Assert.Equal([slow, X, Y], ItemsOf(arrived, work.Id));
    }

    [Fact]
    public void ADroppedFileThatNeverArrivesInTime_GoesToTheInbox()
    {
        var (config, _, work) = Sample();
        const string slow = Desktop + "big.iso";

        var expected = FenceMembership.ExpectArrivals(recent: [], itemRefs: [slow], fenceId: work.Id, insertAt: 0, now: Now);
        var (arrived, _) = FenceMembership.Apply(config, new DesktopChange.Created(slow), expected, now: Now.Add(FenceMembership.ArrivalWindow).AddSeconds(1));

        Assert.Equal([slow], arrived.Inbox.Items);
    }

    [Fact]
    public void SafeSaveMemory_StillExpiresQuickly_EvenWithArrivalsPending()
    {
        // A real delete followed much later by a new file of the same name is a new item: only drops get the long window.
        var (config, games, work) = Sample();
        var expected = FenceMembership.ExpectArrivals(recent: [], itemRefs: [Desktop + "later.txt"], fenceId: work.Id, insertAt: 0, now: Now);

        var (afterDelete, recent) = FenceMembership.Apply(config, new DesktopChange.Deleted(A), expected, now: Now);
        var (afterCreate, _) = FenceMembership.Apply(afterDelete, new DesktopChange.Created(A), recent, now: Now.AddMinutes(1));

        Assert.Equal([B, C, D], ItemsOf(afterCreate, games.Id));
        Assert.Equal([A], afterCreate.Inbox.Items);
    }
}
