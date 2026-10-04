using NeoFences.Core.Config;
using NeoFences.Core.Layouts;
using NeoFences.Core.Membership;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Membership;

/// <summary>M8c carry-overs in Core: reconcile memories, drop rows, stable monitor numbering, label validation.</summary>
public class M8cCoreTests
{
    private const string Desktop = @"C:\Users\cipher\Desktop\";
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<string> ItemsOf(NeoFencesConfig config, string fenceId) =>
        config.Fences.Single(fence => fence.Id == fenceId).Items;

    [Fact]
    public void Reconcile_ReportsTheMemoriesItUsed()
    {
        // Used memories are consumed by the caller, so a later, unrelated file of the same name does not follow them (M8a review).
        var games = Fence.Create("Games");
        var config = new NeoFencesConfig { Fences = [Fence.Create("Inbox") with { IsInbox = true }, games] };
        RememberedPlacement used = new(Desktop + "report.docx", games.Id, 0, Now + FenceMembership.SafeSaveWindow);
        RememberedPlacement unused = new(Desktop + "other.txt", games.Id, 0, Now + FenceMembership.SafeSaveWindow);

        var (_, report) = FenceMembership.Reconcile(config, [Desktop + "report.docx"], remembered: [used, unused], now: Now);

        Assert.Equal([used], report.UsedMemories);
    }

    [Fact]
    public void Reconcile_InsertsSeveralMemoriesForOneFence_ByTheirIndex()
    {
        // A drop of z.txt then a.txt at positions 1 and 2: the listing (by name) brings a.txt first (M8a review).
        var games = Fence.Create("Games") with { Items = [Desktop + "m.lnk", Desktop + "n.lnk"] };
        var config = new NeoFencesConfig { Fences = [Fence.Create("Inbox") with { IsInbox = true }, games] };
        var later = Now + FenceMembership.ArrivalWindow;
        RememberedPlacement[] remembered = [new(Desktop + "z.txt", games.Id, 1, later), new(Desktop + "a.txt", games.Id, 2, later)];

        var (reconciled, _) = FenceMembership.Reconcile(config,
            [Desktop + "a.txt", Desktop + "m.lnk", Desktop + "n.lnk", Desktop + "z.txt"], remembered: remembered, now: Now);

        Assert.Equal([Desktop + "m.lnk", Desktop + "z.txt", Desktop + "a.txt", Desktop + "n.lnk"], ItemsOf(reconciled, games.Id));
    }

    [Fact]
    public void DropInsertIndex_UsesTheWholeRow_NotTheShortItem()
    {
        // Row 1: a tall item (two-line label) and a short one; row 2 starts at 80 (M3b review).
        (double, double, double, double)[] cells = [(0, 0, 76, 80), (76, 0, 76, 50), (0, 80, 76, 50)];

        // Under the short item's label but inside the row, left of its centre: before it.
        Assert.Equal(1, DropZones.InsertIndex(cells, pointX: 90, pointY: 65));
        // Right of its centre: after it (before row 2).
        Assert.Equal(2, DropZones.InsertIndex(cells, pointX: 140, pointY: 65));
        // Past the last row: at the end.
        Assert.Equal(3, DropZones.InsertIndex(cells, pointX: 10, pointY: 200));
        Assert.Equal(0, DropZones.InsertIndex([], pointX: 10, pointY: 10));
    }

    [Fact]
    public void UniqueDeviceIds_NumbersDuplicatesInAStableOrder()
    {
        // Enumeration order can change between boots; numbering follows the GDI device name instead (M8a review).
        Assert.Equal(["DELL#2", "LG", "DELL"],
            DisplayFingerprint.UniqueDeviceIds(["DELL", "LG", "DELL"], orderKeys: [@"\\.\DISPLAY3", @"\\.\DISPLAY2", @"\\.\DISPLAY1"]));
        Assert.Equal(["DELL", "DELL#2"], DisplayFingerprint.UniqueDeviceIds(["DELL", "DELL"]));
    }

    [Fact]
    public void Normalizer_RepairsUnknownLabelModes()
    {
        // A hand-edited "labels": 2 would hide names with no way to show them (M8b review).
        var games = Fence.Create("Games") with { Labels = (LabelMode)2 };
        var config = new NeoFencesConfig
        {
            Fences = [Fence.Create("Inbox") with { IsInbox = true }, games],
            Settings = new Settings { DefaultLabels = (LabelMode)7 },
        };

        var normalized = ConfigNormalizer.Normalize(config);

        Assert.Equal(LabelMode.Always, normalized.Fences.Single(fence => fence.Id == games.Id).Labels);
        Assert.Equal(LabelMode.Always, normalized.Settings.DefaultLabels);
    }
}
