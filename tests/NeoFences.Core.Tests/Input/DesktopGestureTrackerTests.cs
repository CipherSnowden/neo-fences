using NeoFences.Core.Input;

namespace NeoFences.Core.Tests.Input;

public class DesktopGestureTrackerTests
{
    private static DesktopGestureTracker NewTracker() =>
        new(doubleClickMilliseconds: 500, doubleClickSlopPixels: 2, dragThresholdPixels: 8);

    [Fact]
    public void TwoLeftDownsOnDesktopWithinTimeAndSlop_IsDoubleClick()
    {
        var tracker = NewTracker();
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_000, overDesktop: true));
        Assert.Equal(DesktopGesture.DoubleClick, tracker.OnMouse(MouseAction.LeftDown, 101, 99, 1_300, overDesktop: true));
    }

    [Fact]
    public void SecondClickTooLate_IsNotDoubleClick()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_000, overDesktop: true);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_501, overDesktop: true));
    }

    [Fact]
    public void SecondClickTooFar_IsNotDoubleClick()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_000, overDesktop: true);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.LeftDown, 110, 100, 1_100, overDesktop: true));
    }

    [Fact]
    public void ClicksNotOverDesktop_AreIgnored()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_000, overDesktop: false);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_100, overDesktop: true));
    }

    [Fact]
    public void FirstClickOnAppThenDesktop_IsNotDoubleClick()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_000, overDesktop: true);
        tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_100, overDesktop: false);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_200, overDesktop: true));
    }

    [Fact]
    public void TripleClick_YieldsOneDoubleClickOnly()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_000, overDesktop: true);
        Assert.Equal(DesktopGesture.DoubleClick, tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_100, overDesktop: true));
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_200, overDesktop: true));
    }

    [Fact]
    public void DoubleClickAcrossTickCountWrap_IsDetected()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.LeftDown, 100, 100, uint.MaxValue - 100, overDesktop: true);
        Assert.Equal(DesktopGesture.DoubleClick, tracker.OnMouse(MouseAction.LeftDown, 100, 100, 100, overDesktop: true));
    }

    [Fact]
    public void RightDragBeyondThreshold_StartsOnceThenCompletes()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.RightDown, 200, 200, 1_000, overDesktop: true);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.Move, 205, 203, 1_010, overDesktop: false));
        Assert.Equal(DesktopGesture.RightDragStarted, tracker.OnMouse(MouseAction.Move, 208, 200, 1_020, overDesktop: false));
        Assert.Equal((200, 200), tracker.DragStart);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.Move, 300, 300, 1_030, overDesktop: false));
        Assert.Equal(DesktopGesture.RightDragCompleted, tracker.OnMouse(MouseAction.RightUp, 300, 300, 1_040, overDesktop: true));
        Assert.Null(tracker.DragStart);
    }

    [Fact]
    public void RightClickWithoutMovement_CompletesNothing()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.RightDown, 200, 200, 1_000, overDesktop: true);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.RightUp, 201, 200, 1_050, overDesktop: true));
    }

    [Fact]
    public void RightDragStartedOverApp_IsIgnored()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.RightDown, 200, 200, 1_000, overDesktop: false);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.Move, 400, 400, 1_010, overDesktop: false));
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.RightUp, 400, 400, 1_020, overDesktop: false));
    }

    [Fact]
    public void RightDownWhileStillDragging_CancelsTheLostDrag()
    {
        // The right-up of a drag was never seen (Win+L, UAC prompt, hook skipped): the next right press ends it (M5 review I2).
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.RightDown, 100, 100, 1_000, overDesktop: true);
        Assert.Equal(DesktopGesture.RightDragStarted, tracker.OnMouse(MouseAction.Move, 150, 150, 1_050, overDesktop: false));

        Assert.Equal(DesktopGesture.RightDragCancelled, tracker.OnMouse(MouseAction.RightDown, 500, 500, 9_000, overDesktop: false));
        Assert.Null(tracker.DragStart);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.RightUp, 500, 500, 9_100, overDesktop: false));
    }

    [Fact]
    public void LeftDownWhileStillDragging_CancelsTheLostDrag()
    {
        // The usual next click after a lost right-up is a left click; it ends the drawing too (M5 review I2).
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.RightDown, 100, 100, 1_000, overDesktop: true);
        tracker.OnMouse(MouseAction.Move, 150, 150, 1_050, overDesktop: false);

        Assert.Equal(DesktopGesture.RightDragCancelled, tracker.OnMouse(MouseAction.LeftDown, 600, 600, 9_000, overDesktop: true));
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.RightUp, 600, 600, 9_100, overDesktop: true));
    }
}
