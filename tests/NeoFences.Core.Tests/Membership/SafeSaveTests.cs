using NeoFences.Core.Membership;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Membership;

/// <summary>
/// Editors save by replacing the file: Word renames doc.docx away (to a hidden ~WRL….tmp) and renames its hidden temp file
/// onto doc.docx; others delete and recreate it. The document must stay in its fence and position (M2b review I4).
/// </summary>
public class SafeSaveTests
{
    private const string Desktop = @"C:\Users\cipher\Desktop\";
    private const string Doc = Desktop + "report.docx";
    private const string Before = Desktop + "a.txt";
    private const string After = Desktop + "b.txt";
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static (NeoFencesConfig Config, Fence Work) Sample()
    {
        var inbox = Fence.Create("Inbox") with { IsInbox = true };
        var work = Fence.Create("Work") with { Items = [Before, Doc, After] };
        return (new NeoFencesConfig { Fences = [inbox, work] }, work);
    }

    private static IReadOnlyList<string> ItemsOf(NeoFencesConfig config, string fenceId) => config.Fences.Single(fence => fence.Id == fenceId).Items;

    [Fact]
    public void WordSave_KeepsTheDocumentInItsFenceAndPosition()
    {
        var (config, work) = Sample();

        // doc → ~WRL0001.tmp (hidden): the watcher reports it as a delete; then ~WRD0001.tmp (hidden, never fenced) → doc.
        var (afterDelete, recent) = FenceMembership.Apply(config, new DesktopChange.Deleted(Doc), recent: [], now: Now);
        var (afterRename, _) = FenceMembership.Apply(afterDelete, new DesktopChange.Renamed(Desktop + "~WRD0001.tmp", Doc), recent, now: Now.AddMilliseconds(300));

        Assert.Equal([Before, Doc, After], ItemsOf(afterRename, work.Id));
        Assert.Empty(afterRename.Inbox.Items);
    }

    [Fact]
    public void WordSave_WithAVisibleTempName_KeepsTheDocumentInItsFenceAndPosition()
    {
        // Same save when the renamed-away original is not hidden: doc → ~WRL0001.tmp arrives as a rename, and the
        // temp file is deleted at the end.
        var (config, work) = Sample();
        const string renamedAway = Desktop + "~WRL0001.tmp";

        var (step1, recent1) = FenceMembership.Apply(config, new DesktopChange.Renamed(Doc, renamedAway), recent: [], now: Now);
        var (step2, recent2) = FenceMembership.Apply(step1, new DesktopChange.Renamed(Desktop + "~WRD0001.tmp", Doc), recent1, now: Now.AddMilliseconds(200));
        var (step3, _) = FenceMembership.Apply(step2, new DesktopChange.Deleted(renamedAway), recent2, now: Now.AddMilliseconds(400));

        Assert.Equal([Before, Doc, After], ItemsOf(step3, work.Id));
        Assert.Empty(step3.Inbox.Items);
    }

    [Fact]
    public void PlainRename_StaysPut_AndForgetsNothingUseful()
    {
        var (config, work) = Sample();
        const string renamed = Desktop + "final report.docx";

        var (afterRename, _) = FenceMembership.Apply(config, new DesktopChange.Renamed(Doc, renamed), recent: [], now: Now);

        Assert.Equal([Before, renamed, After], ItemsOf(afterRename, work.Id));
    }

    [Fact]
    public void DeleteThenCreate_KeepsTheDocumentInItsFenceAndPosition()
    {
        var (config, work) = Sample();

        var (afterDelete, recent) = FenceMembership.Apply(config, new DesktopChange.Deleted(Doc), recent: [], now: Now);
        var (afterCreate, _) = FenceMembership.Apply(afterDelete, new DesktopChange.Created(Doc), recent, now: Now.AddSeconds(1));

        Assert.Equal([Before, Doc, After], ItemsOf(afterCreate, work.Id));
    }

    [Fact]
    public void SameNameLongAfterADelete_IsANewItem_GoesToTheInbox()
    {
        var (config, work) = Sample();

        var (afterDelete, recent) = FenceMembership.Apply(config, new DesktopChange.Deleted(Doc), recent: [], now: Now);
        var (afterCreate, remaining) = FenceMembership.Apply(afterDelete, new DesktopChange.Created(Doc), recent, now: Now.Add(FenceMembership.SafeSaveWindow).AddSeconds(1));

        Assert.Equal([Before, After], ItemsOf(afterCreate, work.Id));
        Assert.Equal([Doc], afterCreate.Inbox.Items);
        Assert.Empty(remaining);
    }

    [Fact]
    public void FenceDeletedMeanwhile_GoesToTheInbox()
    {
        var (config, work) = Sample();

        var (afterDelete, recent) = FenceMembership.Apply(config, new DesktopChange.Deleted(Doc), recent: [], now: Now);
        var withoutFence = FenceMembership.DeleteFence(afterDelete, work.Id);
        var (afterCreate, _) = FenceMembership.Apply(withoutFence, new DesktopChange.Created(Doc), recent, now: Now.AddSeconds(1));

        Assert.Equal([Before, After, Doc], afterCreate.Inbox.Items);
    }

    [Fact]
    public void ReturningItem_IsRememberedOnce()
    {
        var (config, _) = Sample();

        var (afterDelete, recent) = FenceMembership.Apply(config, new DesktopChange.Deleted(Doc), recent: [], now: Now);
        var (_, remaining) = FenceMembership.Apply(afterDelete, new DesktopChange.Created(Doc), recent, now: Now.AddSeconds(1));

        Assert.Empty(remaining);
    }

    [Fact]
    public void ExcelStyleSave_WithVisibleTempFiles_KeepsTheDocumentInPlace()
    {
        // M3a review I1: Created(temp) → Renamed(book → temp2) → Renamed(temp → book) → Deleted(temp2).
        var (config, work) = Sample();
        var (s1, r1) = FenceMembership.Apply(config, new DesktopChange.Created(Desktop + "A1B2C3D4"), recent: [], now: Now);
        var (s2, r2) = FenceMembership.Apply(s1, new DesktopChange.Renamed(Doc, Desktop + "E5F6A7B8.tmp"), r1, now: Now.AddMilliseconds(100));
        var (s3, r3) = FenceMembership.Apply(s2, new DesktopChange.Renamed(Desktop + "A1B2C3D4", Doc), r2, now: Now.AddMilliseconds(200));
        var (s4, _) = FenceMembership.Apply(s3, new DesktopChange.Deleted(Desktop + "E5F6A7B8.tmp"), r3, now: Now.AddMilliseconds(300));

        Assert.Equal([Before, Doc, After], ItemsOf(s4, work.Id));
        Assert.Empty(s4.Inbox.Items);
    }

    [Fact]
    public void AtomicReplace_KeepsTheDocumentInPlace()
    {
        // M3a review I1: Created(tmp) → Deleted(doc) → Renamed(tmp → doc), as LibreOffice and atomic writers do.
        var (config, work) = Sample();
        var (s1, r1) = FenceMembership.Apply(config, new DesktopChange.Created(Desktop + "report.docx.tmp"), recent: [], now: Now);
        var (s2, r2) = FenceMembership.Apply(s1, new DesktopChange.Deleted(Doc), r1, now: Now.AddMilliseconds(100));
        var (s3, _) = FenceMembership.Apply(s2, new DesktopChange.Renamed(Desktop + "report.docx.tmp", Doc), r2, now: Now.AddMilliseconds(200));

        Assert.Equal([Before, Doc, After], ItemsOf(s3, work.Id));
        Assert.Empty(s3.Inbox.Items);
    }
}
