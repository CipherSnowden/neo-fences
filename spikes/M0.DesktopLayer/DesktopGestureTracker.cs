namespace NeoFences.Spikes.M0;

public enum MouseAction { LeftDown, RightDown, RightUp, Move }

public enum DesktopGesture { None, DoubleClick, RightDragStarted, RightDragCompleted }

/// <summary>
/// Pure state machine turning raw low-level mouse events into desktop gestures.
/// Low-level hooks never see WM_LBUTTONDBLCLK, so double-clicks are rebuilt from two LeftDowns.
/// </summary>
public sealed class DesktopGestureTracker(uint doubleClickMilliseconds, int doubleClickSlopPixels, int dragThresholdPixels)
{
    private (int X, int Y, uint Time)? _lastLeftDown;
    private (int X, int Y)? _rightDownAt;
    private bool _rightDragging;

    public (int X, int Y)? DragStart => _rightDownAt;

    public DesktopGesture OnMouse(MouseAction action, int x, int y, uint timeMilliseconds, bool overDesktop)
    {
        switch (action)
        {
            case MouseAction.LeftDown:
                if (!overDesktop)
                {
                    _lastLeftDown = null;
                    return DesktopGesture.None;
                }
                if (_lastLeftDown is { } previous
                    && unchecked(timeMilliseconds - previous.Time) <= doubleClickMilliseconds
                    && Math.Abs(x - previous.X) <= doubleClickSlopPixels
                    && Math.Abs(y - previous.Y) <= doubleClickSlopPixels)
                {
                    _lastLeftDown = null;
                    return DesktopGesture.DoubleClick;
                }
                _lastLeftDown = (x, y, timeMilliseconds);
                return DesktopGesture.None;

            case MouseAction.RightDown:
                _rightDownAt = overDesktop ? (x, y) : null;
                _rightDragging = false;
                return DesktopGesture.None;

            case MouseAction.Move:
                if (_rightDownAt is { } start && !_rightDragging
                    && (Math.Abs(x - start.X) >= dragThresholdPixels || Math.Abs(y - start.Y) >= dragThresholdPixels))
                {
                    _rightDragging = true;
                    return DesktopGesture.RightDragStarted;
                }
                return DesktopGesture.None;

            case MouseAction.RightUp:
                var completedDrag = _rightDragging;
                _rightDownAt = null;
                _rightDragging = false;
                return completedDrag ? DesktopGesture.RightDragCompleted : DesktopGesture.None;

            default:
                return DesktopGesture.None;
        }
    }
}
