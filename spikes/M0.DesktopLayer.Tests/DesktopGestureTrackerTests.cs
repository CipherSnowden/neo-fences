using NeoFences.Spikes.M0;

namespace M0.DesktopLayer.Tests;

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
}
