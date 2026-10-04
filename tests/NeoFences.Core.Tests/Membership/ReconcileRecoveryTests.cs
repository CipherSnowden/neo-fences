using NeoFences.Core.Membership;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Membership;

/// <summary>Reconcile keeps arrangements through lost watcher events and a moved Desktop folder (M8a).</summary>
public class ReconcileRecoveryTests
{
    private const string Desktop = @"C:\Users\cipher\Desktop\";
    private const string OneDriveDesktop = @"C:\Users\cipher\OneDrive\Desktop\";
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<string> ItemsOf(NeoFencesConfig config, string fenceId) =>
        config.Fences.Single(fence => fence.Id == fenceId).Items;

    [Fact]
    public void ARememberedItem_ComingBackDuringAReconcile_ReturnsToItsFence()
    {
        // A safe-save (delete + create) whose events were lost: the reconcile sees the file again (M3a carry-over).
        var games = Fence.Create("Games") with { Items = [Desktop + "a.lnk"] };
        var config = new NeoFencesConfig { Fences = [Fence.Create("Inbox") with { IsInbox = true }, games] };
        var report = Desktop + "report.docx";
        RememberedPlacement[] remembered = [new(report, games.Id, 0, Now + FenceMembership.SafeSaveWindow)];

        var (reconciled, result) = FenceMembership.Reconcile(config, [Desktop + "a.lnk", report], remembered: remembered, now: Now);

        Assert.Equal([report, Desktop + "a.lnk"], ItemsOf(reconciled, games.Id));
        Assert.Empty(reconciled.Inbox.Items);
        Assert.Empty(result.AddedToInbox);
    }

    [Fact]
    public void AnExpiredMemory_IsIgnored()
    {
        var games = Fence.Create("Games");
        var config = new NeoFencesConfig { Fences = [Fence.Create("Inbox") with { IsInbox = true }, games] };
        RememberedPlacement[] remembered = [new(Desktop + "old.txt", games.Id, 0, Now - TimeSpan.FromSeconds(1))];

        var (reconciled, _) = FenceMembership.Reconcile(config, [Desktop + "old.txt"], remembered: remembered, now: Now);

        Assert.Equal([Desktop + "old.txt"], reconciled.Inbox.Items);
    }

    [Fact]
    public void AMovedDesktopFolder_KeepsEveryItemInItsFenceAndPlace()
    {
        // OneDrive's Known Folder Move: every path changes folder, names stay (M1 carry-over).
        var games = Fence.Create("Games") with { Items = [Desktop + "Crysis 2.lnk", Desktop + "AC Unity.lnk"] };
        var inbox = Fence.Create("Inbox") with { IsInbox = true, Items = [Desktop + "notes.txt"] };
        var config = new NeoFencesConfig { Fences = [inbox, games] };

        var (reconciled, report) = FenceMembership.Reconcile(config,
            [OneDriveDesktop + "notes.txt", OneDriveDesktop + "AC Unity.lnk", OneDriveDesktop + "Crysis 2.lnk", OneDriveDesktop + "new.txt"]);

        Assert.Equal([OneDriveDesktop + "Crysis 2.lnk", OneDriveDesktop + "AC Unity.lnk"], ItemsOf(reconciled, games.Id));
        Assert.Equal([OneDriveDesktop + "notes.txt", OneDriveDesktop + "new.txt"], reconciled.Inbox.Items);
        Assert.Empty(report.Removed);
        Assert.Equal([OneDriveDesktop + "new.txt"], report.AddedToInbox);
    }

    [Fact]
    public void ADeletedItem_WithAnUnrelatedNameElsewhere_IsStillRemoved()
    {
        var games = Fence.Create("Games") with { Items = [Desktop + "gone.lnk"] };
        var config = new NeoFencesConfig { Fences = [Fence.Create("Inbox") with { IsInbox = true }, games] };

        var (reconciled, report) = FenceMembership.Reconcile(config, [Desktop + "other.lnk"]);

        Assert.Empty(ItemsOf(reconciled, games.Id));
        Assert.Equal([Desktop + "gone.lnk"], report.Removed);
    }

    [Fact]
    public void AnAmbiguousName_IsNotGuessed()
    {
        // Two candidates with the same name (user and Public Desktop): no silent guess, the usual drop + Inbox.
        var games = Fence.Create("Games") with { Items = [Desktop + "app.lnk"] };
        var config = new NeoFencesConfig { Fences = [Fence.Create("Inbox") with { IsInbox = true }, games] };

        var (reconciled, _) = FenceMembership.Reconcile(config, [OneDriveDesktop + "app.lnk", @"C:\Users\Public\Desktop\app.lnk"]);

        Assert.Empty(ItemsOf(reconciled, games.Id));
        Assert.Equal(2, reconciled.Inbox.Items.Count);
    }
}
