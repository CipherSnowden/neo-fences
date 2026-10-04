using NeoFences.Core.Layouts;

namespace NeoFences.Core.Tests.Layouts;

/// <summary>
/// M3b review I2: only the middle of a folder / Recycle Bin item means "drop into it"; its edges and label reorder, so a
/// drop meant to land between two folders can never move a file into one of them.
/// </summary>
public class DropZonesTests
{
    // An item cell 80 wide, 100 tall at (100, 200).
    private static bool Into(double pointX, double pointY) =>
        DropZones.IsInto(cellLeft: 100, cellTop: 200, cellWidth: 80, cellHeight: 100, pointX: pointX, pointY: pointY);

    [Fact]
    public void CentreOfTheIcon_IsInto() => Assert.True(Into(140, 230));

    [Theory]
    [InlineData(105, 230)] // left quarter
    [InlineData(175, 230)] // right quarter
    [InlineData(140, 290)] // bottom of the label
    [InlineData(90, 230)]  // outside the cell
    public void EdgesAndLabel_Reorder(double pointX, double pointY) => Assert.False(Into(pointX, pointY));
}
