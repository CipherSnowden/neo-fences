using System.Globalization;
using NeoFences.Core.Items;

namespace NeoFences.Core.Tests.Items;

/// <summary>M28 (0.16.1): the deferred minors of M24–M27 that Core decides (spec 2026-10-05-polish-design).</summary>
public class PolishTests
{
    [Theory]
    [InlineData("en-US", "October 2026")]
    [InlineData("ja-JP", "2026年10月")]
    [InlineData("hu-HU", "2026. október")]
    public void DatePage_MonthAndYear_InTheCulturesOwnOrder(string culture, string expected) => // W6
        Assert.Equal(expected, Widgets.Page(new DateTime(2026, 10, 5), CultureInfo.GetCultureInfo(culture)).MonthYear);

    [Fact]
    public void GpuPercent_IsTheBusiestAdapter_LikeTaskManager() // W5
    {
        IReadOnlyList<(string Instance, double Value)> engines =
        [
            ("pid_100_luid_0x00000000_0x0000AAAA_phys_0_eng_0_engtype_3D", 30),
            ("pid_200_luid_0x00000000_0x0000AAAA_phys_0_eng_0_engtype_3D", 15),
            ("pid_100_luid_0x00000000_0x0000BBBB_phys_0_eng_0_engtype_3D", 60),
        ];
        Assert.Equal(60, Widgets.GpuPercent(engines));
        Assert.Equal(100, Widgets.GpuPercent([("pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 80), ("pid_2_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 70)]));
        Assert.Equal(0, Widgets.GpuPercent([]));
        Assert.Equal(25, Widgets.GpuPercent([("odd instance name", 25)])); // no luid: one adapter
    }

    [Fact]
    public void PlaceDropped_KeepsOffsetsFromTheElementUnderThePointer() // G1
    {
        // Two elements dragged from (0,0) and (2,0); the pointer held the second one and dropped on (3,1).
        var cells = FenceGrid.PlaceDropped([], [(GridSpan.One, new GridCell(0, 0)), (GridSpan.One, new GridCell(2, 0))], new GridCell(3, 1), columns: 6, anchorIndex: 1);
        Assert.Equal([new GridCell(1, 1), new GridCell(3, 1)], cells);
        // Without an anchor the first one leads, as before.
        Assert.Equal([new GridCell(3, 1), new GridCell(5, 1)], FenceGrid.PlaceDropped([], [(GridSpan.One, new GridCell(0, 0)), (GridSpan.One, new GridCell(2, 0))], new GridCell(3, 1), columns: 6));
    }

    [Theory]
    [InlineData(400.0, 84.0, 4)]
    [InlineData(83.0, 84.0, 1)]
    [InlineData(0.0, 84.0, 1)]
    [InlineData(double.NaN, 84.0, 1)]
    public void ColumnsFor_AFencesWidth_AtLeastOne(double width, double cellWidth, int expected) => // G3
        Assert.Equal(expected, FenceGrid.ColumnsFor(width, cellWidth));

    [Fact]
    public void CommonSize_OfSeveralElements_OnlyWhenAllAgree() // G4
    {
        Assert.Equal((true, (GridSpan?)null), FenceGrid.CommonSize([null, null]));
        Assert.Equal((true, (GridSpan?)new GridSpan(2, 2)), FenceGrid.CommonSize([new GridSpan(2, 2), new GridSpan(2, 2)]));
        Assert.False(FenceGrid.CommonSize([new GridSpan(2, 2), null]).AllSame);
        Assert.False(FenceGrid.CommonSize([new GridSpan(2, 2), new GridSpan(1, 1)]).AllSame);
    }
}