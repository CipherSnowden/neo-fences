using NeoFences.Core.Config;
using NeoFences.Core.Layouts;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Model;

/// <summary>M8c carry-overs in Core: drop rows, stable monitor numbering, label validation.</summary>
public class M8cCoreTests
{
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
            Fences = [Fence.Create("Other"), games],
            Settings = new Settings { DefaultLabels = (LabelMode)7 },
        };

        var normalized = ConfigNormalizer.Normalize(config);

        Assert.Equal(LabelMode.Always, normalized.Fences.Single(fence => fence.Id == games.Id).Labels);
        Assert.Equal(LabelMode.Always, normalized.Settings.DefaultLabels);
    }
}
