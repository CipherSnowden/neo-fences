using NeoFences.Core.Config;
using NeoFences.Core.Items;
using NeoFences.Core.Library;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Items;

/// <summary>M24 (0.13.0): element sizes and the fence grid (spec 2026-10-05-element-sizes-design).</summary>
public class FenceGridTests
{
    private static GridElement E(int columns = 1, int rows = 1, int? atColumn = null, int? atRow = null) =>
        new(new GridSpan(columns, rows), atColumn is { } column ? new GridCell(column, atRow!.Value) : null);

    private static IReadOnlyList<(int, int)> Cells(GridArrangement arrangement) => [.. arrangement.Cells.Select(cell => (cell.Column, cell.Row))];

    [Fact]
    public void Flow_PacksInOrder_AndFillsTheGapsBesideBigElements()
    {
        // 4 columns: a, then a 2x2, then four 1x1s — two fill the gap under "a" row beside the 2x2, the rest go on.
        var arrangement = FenceGrid.Arrange([E(), E(2, 2), E(), E(), E(), E()], columns: 4, FenceLayout.Flow);
        Assert.Equal([(0, 0), (1, 0), (3, 0), (0, 1), (3, 1), (0, 2)], Cells(arrangement));
        Assert.Equal(3, arrangement.Rows);
    }

    [Fact]
    public void Flow_ClampsSpansToTheFenceWidth()
    {
        var arrangement = FenceGrid.Arrange([E(4, 1), E()], columns: 2, FenceLayout.Flow);
        Assert.Equal([new GridSpan(2, 1), new GridSpan(1, 1)], arrangement.Spans);
        Assert.Equal([(0, 0), (0, 1)], Cells(arrangement));
    }

    [Fact]
    public void Free_KeepsStoredCells_AndPutsCollisionsAndOutOfWidthElementsInFreeSpots()
    {
        var arrangement = FenceGrid.Arrange(
            [E(1, 1, 2, 1), E(2, 2, 0, 0), E(1, 1, 2, 1), E(1, 1, 7, 0), E()], columns: 3, FenceLayout.Free);
        // the 2x2 at (0,0), the first 1x1 at its (2,1); the second one at (2,1) collides, (7,0) is out of width, the last
        // has no cell: all three go to free spots in order — (2,0), then row 2.
        Assert.Equal([(2, 1), (0, 0), (2, 0), (0, 2), (1, 2)], Cells(arrangement));
    }

    [Fact]
    public void Arrange_OfNothing_IsEmpty_AndColumnsAreAtLeastOne()
    {
        Assert.Equal(0, FenceGrid.Arrange([], columns: 5, FenceLayout.Flow).Rows);
        Assert.Equal([(0, 0), (0, 1)], Cells(FenceGrid.Arrange([E(), E()], columns: 0, FenceLayout.Flow)));
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(159.9, 95, 1, 0)]
    [InlineData(160, 96, 2, 1)]
    [InlineData(-30, -5, 0, 0)]
    public void CellAt_FindsTheCellUnderAPoint(double x, double y, int column, int row) =>
        Assert.Equal(new GridCell(column, row), FenceGrid.CellAt(x, y, cellWidth: 80, cellHeight: 96));

    [Fact]
    public void NearestFree_IsTheTargetWhenFree_ElseTheClosestSpotThatFits()
    {
        IReadOnlyList<(GridCell, GridSpan)> placed = [(new GridCell(0, 0), new GridSpan(2, 2)), (new GridCell(3, 0), new GridSpan(1, 1))];
        Assert.Equal(new GridCell(2, 0), FenceGrid.NearestFree(placed, new GridSpan(1, 1), new GridCell(2, 0), columns: 4));
        Assert.Equal(new GridCell(2, 0), FenceGrid.NearestFree(placed, new GridSpan(1, 1), new GridCell(1, 0), columns: 4));
        Assert.Equal(new GridCell(0, 2), FenceGrid.NearestFree(placed, new GridSpan(2, 1), new GridCell(0, 0), columns: 4)); // under the 2x2, closer than (2,1)
        Assert.Equal(new GridCell(0, 2), FenceGrid.NearestFree(placed, new GridSpan(4, 1), new GridCell(0, 0), columns: 4));
    }

    [Fact]
    public void PlaceDropped_KeepsRelativeOffsets_AndMovesCollidingOnesToFreeSpots()
    {
        IReadOnlyList<(GridCell, GridSpan)> others = [(new GridCell(2, 0), new GridSpan(1, 1))];
        var cells = FenceGrid.PlaceDropped(others, [(new GridSpan(1, 1), new GridCell(0, 0)), (new GridSpan(1, 1), new GridCell(1, 0))],
            dropCell: new GridCell(1, 0), columns: 4);
        Assert.Equal([new GridCell(1, 0), new GridCell(3, 0)], cells); // the second would land on (2,0): taken → its nearest free spot
    }

    [Fact]
    public void SpanOf_IsTheItemsSize_OrTheDefault_CoversTall()
    {
        var game = GameItems.Create(new LibraryItem(new GameEntry("steam:1", "Blur", GameSource.Steam, "steam", new GameLaunch("steam://x")), "Blur.url", "s"), @"C:\lib");
        Assert.Equal(new GridSpan(1, 2), FenceGrid.SpanOf(game));
        Assert.Equal(new GridSpan(1, 1), FenceGrid.SpanOf(game with { ShowAs = ItemShow.Icon }));
        Assert.Equal(new GridSpan(1, 1), FenceGrid.SpanOf(VirtualItem.Create(@"C:\a.txt")));
        Assert.Equal(new GridSpan(3, 2), FenceGrid.SpanOf(VirtualItem.Create(@"C:\a.txt") with { Size = new GridSpan(3, 2) }));
    }

    [Fact]
    public void SetSize_AndPlace_ChangeOnlyTheGivenItems()
    {
        var fenceId = Fence.NewId();
        var a = VirtualItem.Create(@"C:\a");
        var b = VirtualItem.Create(@"C:\b");
        var items = new ItemsDocument().With(fenceId, [a, b]);
        var sized = ItemEdits.SetSize(items, [a.Id], new GridSpan(2, 2));
        Assert.Equal([new GridSpan(2, 2), null], sized.Of(fenceId).Select(item => item.Size));
        Assert.Null(ItemEdits.SetSize(sized, [a.Id], null).Of(fenceId)[0].Size);
        var placed = ItemEdits.Place(sized, new Dictionary<string, GridCell> { [b.Id] = new GridCell(3, 1) });
        Assert.Equal([null, new GridCell(3, 1)], placed.Of(fenceId).Select(item => item.Cell));
    }

    [Fact]
    public void Repair_ClampsSizesAndDropsNegativeCells_AndTheLayoutTypoIsFlow()
    {
        var fenceId = Fence.NewId();
        var odd = VirtualItem.Create(@"C:\a") with { Size = new GridSpan(9, 0), Cell = new GridCell(-1, 2) };
        var repaired = ItemEdits.Repair(new ItemsDocument().With(fenceId, [odd])).Of(fenceId)[0];
        Assert.Equal(new GridSpan(4, 1), repaired.Size);
        Assert.Null(repaired.Cell);

        var fence = Fence.Create("x") with { Layout = FenceLayout.Free };
        var json = ConfigJson.Serialize(NeoFencesConfig.CreateDefault() with { Fences = [fence] }).Replace("\"free\"", "\"diagonal\"");
        Assert.Equal(FenceLayout.Flow, ConfigNormalizer.Normalize(ConfigJson.Deserialize(json)).Fences.Single().Layout);
    }

    [Fact]
    public void ItemsJson_RoundTripsSizesAndCells_WritingThemOnlyWhenSet()
    {
        var fenceId = Fence.NewId();
        var sized = VirtualItem.Create(@"C:\a") with { Size = new GridSpan(2, 3), Cell = new GridCell(1, 4) };
        var plain = VirtualItem.Create(@"C:\b");
        var json = ConfigJson.SerializeItems(new ItemsDocument().With(fenceId, [sized, plain]));
        Assert.Equal(1, json.Split("\"size\"").Length - 1);
        var back = ConfigJson.DeserializeItems(json).Of(fenceId);
        Assert.Equal((new GridSpan(2, 3), new GridCell(1, 4)), (back[0].Size, back[0].Cell));
    }
}
