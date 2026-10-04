using NeoFences.Core.Membership;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Membership;

public class FenceMembershipTests
{
    private const string Crysis = @"C:\Users\cipher\Desktop\Crysis 2.lnk";
    private const string Unity = @"C:\Users\cipher\Desktop\AC Unity.lnk";
    private const string Notes = @"C:\Users\cipher\Desktop\notes.txt";
    private const string RecycleBin = "::{645FF040-5081-101B-9F08-00AA002F954E}";

    private static (NeoFencesConfig Config, Fence Games, Fence Portal) Sample()
    {
        var inbox = Fence.Create("Inbox") with { IsInbox = true, Items = [Notes] };
        var games = Fence.Create("Games") with { Items = [Crysis, Unity] };
        var portal = Fence.Create("Shots", FenceSource.Portal(@"D:\Shots"));
        return (new NeoFencesConfig { Fences = [inbox, games, portal] }, games, portal);
    }

    private static IReadOnlyList<string> ItemsOf(NeoFencesConfig config, string fenceId) =>
        config.Fences.Single(fence => fence.Id == fenceId).Items;

    // ---------- Reconcile ----------

    [Fact]
    public void Reconcile_FirstRun_PutsEverythingInInboxInDesktopOrder()
    {
        var config = NeoFencesConfig.CreateDefault();

        var (reconciled, report) = FenceMembership.Reconcile(config, [Crysis, RecycleBin, Notes]);

        Assert.Equal([Crysis, RecycleBin, Notes], reconciled.Inbox.Items);
        Assert.Equal([Crysis, RecycleBin, Notes], report.AddedToInbox);
        Assert.Empty(report.Removed);
    }

    [Fact]
    public void Reconcile_DropsMissingItems_AddsUnknownToInbox_KeepsFencedOnes()
    {
        var (config, games, _) = Sample();

        var (reconciled, report) = FenceMembership.Reconcile(config, [Crysis, Notes, RecycleBin]);

        Assert.Equal([Crysis], ItemsOf(reconciled, games.Id));
        Assert.Equal([Notes, RecycleBin], reconciled.Inbox.Items);
        Assert.Equal([Unity], report.Removed);
        Assert.Equal([RecycleBin], report.AddedToInbox);
    }

    [Fact]
    public void Reconcile_CaseDifferences_AreTheSameItem_AndAdoptShellSpelling()
    {
        var (config, games, _) = Sample();
        var shellSpelling = Crysis.ToUpperInvariant();

        var (reconciled, report) = FenceMembership.Reconcile(config, [shellSpelling, Unity, Notes]);

        Assert.Equal([shellSpelling, Unity], ItemsOf(reconciled, games.Id));
        Assert.Empty(report.AddedToInbox);
        Assert.Empty(report.Removed);
    }

    [Fact]
    public void Reconcile_DuplicateDesktopEntries_AreAddedOnce()
    {
        var (reconciled, _) = FenceMembership.Reconcile(NeoFencesConfig.CreateDefault(), [Notes, Notes.ToUpperInvariant()]);

        Assert.Equal([Notes], reconciled.Inbox.Items);
    }

    // ---------- Change events ----------

    [Fact]
    public void AddItem_GoesToInbox_OrToDropTarget_AndIgnoresDuplicates()
    {
        var (config, games, _) = Sample();
        const string newFile = @"C:\Users\cipher\Desktop\screenshot.png";

        var toInbox = FenceMembership.AddItem(config, newFile);
        var toGames = FenceMembership.AddItem(config, newFile, targetFenceId: games.Id);
        var duplicate = FenceMembership.AddItem(config, Crysis.ToLowerInvariant());

        Assert.Equal([Notes, newFile], toInbox.Inbox.Items);
        Assert.Equal([Crysis, Unity, newFile], ItemsOf(toGames, games.Id));
        Assert.Same(config, duplicate);
    }

    [Fact]
    public void RemoveItem_RemovesFromOwner_UnknownIsNoOp()
    {
        var (config, games, _) = Sample();

        Assert.Equal([Unity], ItemsOf(FenceMembership.RemoveItem(config, Crysis), games.Id));
        Assert.Same(config, FenceMembership.RemoveItem(config, @"C:\nope.txt"));
    }

    [Fact]
    public void RenameItem_KeepsFenceAndPosition_IncludingCaseOnlyRename()
    {
        var (config, games, _) = Sample();
        const string renamed = @"C:\Users\cipher\Desktop\Crysis 2 Remastered.lnk";

        var afterRename = FenceMembership.RenameItem(config, Crysis, renamed);
        var afterCaseRename = FenceMembership.RenameItem(config, Crysis, Crysis.ToUpperInvariant());

        Assert.Equal([renamed, Unity], ItemsOf(afterRename, games.Id));
        Assert.Equal([Crysis.ToUpperInvariant(), Unity], ItemsOf(afterCaseRename, games.Id));
    }

    [Fact]
    public void RenameItem_UnknownOldRef_TreatedAsNewItem()
    {
        var (config, _, _) = Sample();

        var updated = FenceMembership.RenameItem(config, @"C:\gone.txt", @"C:\Users\cipher\Desktop\new.txt");

        Assert.Equal([Notes, @"C:\Users\cipher\Desktop\new.txt"], updated.Inbox.Items);
    }

    [Fact]
    public void MoveItem_BetweenFencesAndWithinFence()
    {
        var (config, games, _) = Sample();

        var moved = FenceMembership.MoveItem(config, Notes, games.Id, index: 1);
        var reordered = FenceMembership.MoveItem(config, Unity, games.Id, index: 0);
        var appendedPastEnd = FenceMembership.MoveItem(config, Notes, games.Id, index: 99);

        Assert.Empty(moved.Inbox.Items);
        Assert.Equal([Crysis, Notes, Unity], ItemsOf(moved, games.Id));
        Assert.Equal([Unity, Crysis], ItemsOf(reordered, games.Id));
        Assert.Equal([Crysis, Unity, Notes], ItemsOf(appendedPastEnd, games.Id));
    }

    [Fact]
    public void MoveItem_IntoPortalOrUnknownFence_IsRejected()
    {
        var (config, _, portal) = Sample();

        Assert.Throws<ArgumentException>(() => FenceMembership.MoveItem(config, Notes, portal.Id));
        Assert.Throws<ArgumentException>(() => FenceMembership.MoveItem(config, Notes, "no-such-fence"));
    }

    [Fact]
    public void CreateFence_AppendsEmptyDesktopFence()
    {
        var (config, _, _) = Sample();

        var (updated, fence) = FenceMembership.CreateFence(config, "Work");

        Assert.Equal(fence, updated.Fences[^1]);
        Assert.Equal("Work", fence.Title);
        Assert.Empty(fence.Items);
        Assert.Equal(FenceSourceKind.Desktop, fence.Source.Kind);
    }

    [Fact]
    public void DeleteFence_ItemsGoToInbox_NeverLost()
    {
        var (config, games, _) = Sample();

        var updated = FenceMembership.DeleteFence(config, games.Id);

        Assert.DoesNotContain(updated.Fences, fence => fence.Id == games.Id);
        Assert.Equal([Notes, Crysis, Unity], updated.Inbox.Items);
    }

    [Fact]
    public void DeleteFence_InboxIsRefused()
    {
        var (config, _, _) = Sample();

        Assert.Throws<InvalidOperationException>(() => FenceMembership.DeleteFence(config, config.Inbox.Id));
    }

    [Fact]
    public void RenameItem_OntoRefAlreadyInSameFence_LeavesOneEntry()
    {
        // Event storms: the create for the new name can arrive before the rename.
        const string draft = @"C:UserscipherDesktopNew Text Document.txt";
        const string todo = @"C:UserscipherDesktop	odo.txt";
        var config = NeoFencesConfig.CreateDefault();
        config = config.WithFence(config.Inbox with { Items = [draft, todo] });

        var renamed = FenceMembership.RenameItem(config, draft, todo);

        Assert.Equal([todo], renamed.Inbox.Items);
    }

    [Fact]
    public void Reconcile_EmptyEnumeration_KeepsMemberships_AndFlagsReport()
    {
        // Shell not ready at logon / Explorer restarting / OneDrive Desktop not mounted yet.
        var (config, games, _) = Sample();

        var (reconciled, report) = FenceMembership.Reconcile(config, []);

        Assert.Equal([Crysis, Unity], ItemsOf(reconciled, games.Id));
        Assert.Equal([Notes], reconciled.Inbox.Items);
        Assert.True(report.Suspicious);
        Assert.Empty(report.Removed);
    }

    [Fact]
    public void Reconcile_MostFencedRefsMissing_FromReadableFolders_AreRemoved()
    {
        // M2b review I1-B: items deleted while NeoFences was not running must not stay as ghosts forever.
        var inbox = Fence.Create("Inbox") with { IsInbox = true };
        var games = Fence.Create("Games") with { Items = ["a", "b", "c", "d", "e", "f"] };
        var config = new NeoFencesConfig { Fences = [inbox, games] };

        var (reconciled, report) = FenceMembership.Reconcile(config, ["a", "new"]);

        Assert.Equal(["a"], ItemsOf(reconciled, games.Id));
        Assert.Equal(["new"], reconciled.Inbox.Items);
        Assert.False(report.Suspicious);
        Assert.Equal(["b", "c", "d", "e", "f"], report.Removed);
    }

    [Fact]
    public void Reconcile_RefsUnderUnreadableFolder_AreKept_OthersPruned()
    {
        // M2b review I1-A: the user Desktop (redirected, offline) could not be listed; the Public Desktop could.
        const string userDesktop = @"\\server\home\me\Desktop";
        const string userItem = userDesktop + @"\Crysis.lnk";
        const string publicItem = @"C:\Users\Public\Desktop\Steam.lnk";
        var inbox = Fence.Create("Inbox") with { IsInbox = true };
        var games = Fence.Create("Games") with { Items = [userItem, publicItem] };
        var config = new NeoFencesConfig { Fences = [inbox, games] };

        var (reconciled, report) = FenceMembership.Reconcile(config, ["::{645FF040-5081-101B-9F08-00AA002F954E}"], unavailableFolders: [userDesktop.ToUpperInvariant()]);

        Assert.Equal([userItem], ItemsOf(reconciled, games.Id));
        Assert.True(report.Suspicious);
        Assert.Equal([publicItem], report.Removed);
    }
}
