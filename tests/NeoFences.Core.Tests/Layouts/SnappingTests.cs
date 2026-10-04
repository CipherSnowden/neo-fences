using NeoFences.Core.Layouts;

namespace NeoFences.Core.Tests.Layouts;

public class SnappingTests
{
    private const int Gap = 8;
    private const int Threshold = 12;
    private static readonly PixelRect WorkArea = new(0, 0, 1920, 1032);

    private static PixelRect Snap(PixelRect rect, SnapEdges edges, params PixelRect[] others) =>
        Snapping.Snap(rect, edges, workArea: WorkArea, others: others, gapPx: Gap, thresholdPx: Threshold);

    [Fact]
    public void Move_NearWorkAreaCorner_KeepsTheGapOnBothAxes() =>
        Assert.Equal(new PixelRect(8, 8, 300, 200), Snap(new PixelRect(3, 4, 300, 200), SnapEdges.Move));

    [Fact]
    public void Move_NearWorkAreaRightEdge_SnapsByTheRightEdge() =>
        Assert.Equal(new PixelRect(1612, 400, 300, 200), Snap(new PixelRect(1605, 400, 300, 200), SnapEdges.Move));

    [Fact]
    public void Move_NearAnotherFence_SnapsBesideItWithTheGap()
    {
        var neighbour = new PixelRect(8, 8, 300, 200); // right edge 308

        Assert.Equal(new PixelRect(316, 50, 300, 200), Snap(new PixelRect(320, 50, 300, 200), SnapEdges.Move, neighbour));
    }

    [Fact]
    public void Move_AlignsTopWithANeighbourBesideIt()
    {
        var neighbour = new PixelRect(8, 100, 300, 200);

        Assert.Equal(100, Snap(new PixelRect(316, 106, 300, 200), SnapEdges.Move, neighbour).Y);
    }

    [Fact]
    public void Move_FarFromEverything_IsUnchanged()
    {
        var rect = new PixelRect(500, 500, 300, 200);

        Assert.Equal(rect, Snap(rect, SnapEdges.Move, new PixelRect(8, 8, 300, 200)));
    }

    [Fact]
    public void Move_NeighbourFarAboveOrBelow_DoesNotPullSideways()
    {
        // Only fences level with the moving one count for left/right snapping.
        var neighbour = new PixelRect(8, 8, 300, 200);
        var rect = new PixelRect(320, 700, 300, 200);

        Assert.Equal(rect, Snap(rect, SnapEdges.Move, neighbour));
    }

    [Fact]
    public void Move_PicksTheClosestCandidate()
    {
        var neighbour = new PixelRect(14, 400, 300, 200); // left edge 14: aligning would be 4 px away, the work-area gap 2 px

        Assert.Equal(8, Snap(new PixelRect(10, 500, 300, 200), SnapEdges.Move, neighbour).X);
    }

    [Fact]
    public void Size_RightEdge_SnapsOnlyThatEdge()
    {
        var neighbour = new PixelRect(418, 100, 300, 200); // a gap before it means right edge 410

        Assert.Equal(new PixelRect(100, 100, 310, 200), Snap(new PixelRect(100, 100, 300, 200), SnapEdges.Right, neighbour));
    }

    [Fact]
    public void Size_TopLeftCorner_SnapsBothOfItsEdges_KeepsTheOthers() =>
        Assert.Equal(new PixelRect(8, 8, 397, 296), Snap(new PixelRect(5, 4, 400, 300), SnapEdges.Left | SnapEdges.Top));

    [Fact]
    public void Unsnapped_AddsEachProposalsStepToTheUnsnappedRect()
    {
        // Windows proposes "where the window is now + this mouse step"; the step is what counts.
        var unsnapped = new PixelRect(1240, 320, 320, 220);
        var current = new PixelRect(1249, 320, 320, 220);   // snapped back last time
        var proposal = new PixelRect(1246, 322, 320, 220);  // current + (-3, +2)

        Assert.Equal(new PixelRect(1237, 322, 320, 220), Snapping.Unsnapped(unsnapped, current: current, proposal: proposal));
    }

    [Fact]
    public void Unsnapped_Resize_MovesOnlyTheChangedEdges()
    {
        var unsnapped = new PixelRect(100, 100, 300, 200);
        var proposal = new PixelRect(100, 100, 305, 200); // right edge +5

        Assert.Equal(new PixelRect(100, 100, 305, 200), Snapping.Unsnapped(unsnapped, current: unsnapped, proposal: proposal));
    }

    [Fact]
    public void SlowDragAwayFromASnapTarget_Escapes()
    {
        // M2c smoke: snapping every WM_MOVING proposal pinned the fence, because each 2-3 px step was snapped back.
        var neighbour = new PixelRect(1249, 548, 320, 223);
        var current = new PixelRect(1249, 320, 320, 220);
        var unsnapped = current;
        for (var step = 0; step < 10; step++)
        {
            unsnapped = Snapping.Unsnapped(unsnapped, current: current, proposal: current with { X = current.X - 3 });
            current = Snap(unsnapped, SnapEdges.Move, neighbour);
        }

        Assert.Equal(1219, current.X);
    }

    [Fact]
    public void DragTracker_OutlineDrag_WindowNeverMoves_DoesNotRunAway()
    {
        // M2c review I1: with "Show window contents while dragging" off the window stays at its start rect until release.
        // Windows proposes each rect from the rect we wrote last, so that (not the window rect) is the base for each step.
        var neighbour = new PixelRect(1249, 548, 320, 223);
        var start = new PixelRect(1249, 320, 320, 220);
        var tracker = new DragTracker(start);
        var written = start;
        for (var step = 0; step < 10; step++)
            written = tracker.Step(proposal: written with { X = written.X - 3 }, snap: rect => Snap(rect, SnapEdges.Move, neighbour));

        Assert.Equal(1219, written.X);
    }

    [Fact]
    public void DragTracker_SnapsIn_ThenLetsGo()
    {
        var neighbour = new PixelRect(1249, 548, 320, 223);
        var tracker = new DragTracker(new PixelRect(1180, 320, 320, 220));
        var written = new PixelRect(1180, 320, 320, 220);
        var positions = new List<int>();
        for (var step = 0; step < 40; step++)
        {
            written = tracker.Step(proposal: written with { X = written.X + 3 }, snap: rect => Snap(rect, SnapEdges.Move, neighbour));
            positions.Add(written.X);
        }

        Assert.Contains(1249, positions);           // lined up with the neighbour on the way
        Assert.Equal(1180 + 40 * 3, written.X);     // and free again once past the reach
    }

    [Fact]
    public void Resize_SnapNeverShrinksBelowTheMinimum()
    {
        // Dragging the left edge to x=1100 (width 130): the right edge of a fence at 1124 pulls it to 1132 (with the gap),
        // which would leave 98 px, under the 120 px minimum. The snap is skipped (M2c review carry-over).
        var neighbour = new PixelRect(824, 300, 300, 200);
        var proposed = new PixelRect(1100, 300, 130, 200);
        var snapped = Snapping.Snap(proposed, SnapEdges.Left, workArea: WorkArea, others: [neighbour], gapPx: Gap, thresholdPx: 40,
            minWidthPx: 120, minHeightPx: 60);
        Assert.Equal(proposed, snapped);
    }

    [Fact]
    public void Resize_SnapAboveTheMinimum_StillSnaps()
    {
        var neighbour = new PixelRect(824, 300, 300, 200);
        var proposed = new PixelRect(1135, 300, 300, 200);
        var snapped = Snapping.Snap(proposed, SnapEdges.Left, workArea: WorkArea, others: [neighbour], gapPx: Gap, thresholdPx: Threshold,
            minWidthPx: 120, minHeightPx: 60);
        Assert.Equal(1132, snapped.X);
    }
}
