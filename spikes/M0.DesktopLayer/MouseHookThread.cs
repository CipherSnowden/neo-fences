using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Spikes.M0;

public enum RightClickSuppression
{
    /// <summary>Observe only. Explorer's desktop menu will appear after a right-drag.</summary>
    None,
    /// <summary>S1: pass RightDown through, swallow RightUp after a drag.</summary>
    SwallowUpAfterDrag,
    /// <summary>S2: swallow every desktop RightDown; on release without drag, replay a synthetic right-click.</summary>
    SwallowDownAndReplay,
}

/// <summary>Spike for spec §4.5: WH_MOUSE_LL on its own thread with its own message loop (ADR-007).</summary>
public sealed class MouseHookThread : IDisposable
{
    /// <summary>dwExtraInfo stamped on replayed input so the hook lets it through ("NFNC").</summary>
    private const nuint ReplayMarker = 0x4E464E43;

    private static readonly uint ReplayRightClickMessage = PInvoke.WM_APP + 1;

    private readonly HOOKPROC _hookCallback; // field keeps the delegate alive while the hook exists
    private readonly Action<DesktopGesture, int, int> _onGesture;
    private readonly DesktopGestureTracker _tracker;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private uint _threadId;
    private bool _swallowedRightDown;

    public RightClickSuppression Suppression { get; set; } = RightClickSuppression.SwallowUpAfterDrag;

    /// <param name="onGesture">Invoked on the hook thread; marshal to the UI thread yourself and return fast.</param>
    public MouseHookThread(Action<DesktopGesture, int, int> onGesture)
    {
        _onGesture = onGesture;
        _hookCallback = OnLowLevelMouse;
        _tracker = new DesktopGestureTracker(
            doubleClickMilliseconds: PInvoke.GetDoubleClickTime(),
            doubleClickSlopPixels: PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXDOUBLECLK) / 2,
            dragThresholdPixels: 8);
        _thread = new Thread(RunHookLoop) { IsBackground = true, Name = "NeoFences.MouseHook" };
        _thread.Start();
        _started.Wait();
    }

    private void RunHookLoop()
    {
        _threadId = PInvoke.GetCurrentThreadId();
        using var module = PInvoke.GetModuleHandle((string?)null);
        using var hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _hookCallback, module, 0);
        Lab.Log($"WH_MOUSE_LL installed: {!hook.IsInvalid}");
        _started.Set();

        while (PInvoke.GetMessage(out MSG message, HWND.Null, 0, 0) > 0)
        {
            if (message.message == ReplayRightClickMessage) ReplayRightClick();
        }
        Lab.Log("WH_MOUSE_LL removed");
    }

    private unsafe LRESULT OnLowLevelMouse(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code < 0) return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);

        var hookData = (MSLLHOOKSTRUCT*)lParam.Value;
        if (hookData->dwExtraInfo == ReplayMarker) return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);

        MouseAction? action = (uint)wParam.Value switch
        {
            PInvoke.WM_LBUTTONDOWN => MouseAction.LeftDown,
            PInvoke.WM_RBUTTONDOWN => MouseAction.RightDown,
            PInvoke.WM_RBUTTONUP => MouseAction.RightUp,
            PInvoke.WM_MOUSEMOVE => MouseAction.Move,
            _ => null,
        };
        if (action is null) return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);

        var point = hookData->pt;
        // WindowFromPoint only on button events: moves arrive at up to 1000 Hz and must stay cheap.
        var overDesktop = action != MouseAction.Move && DesktopWindows.IsOverDesktop(point);
        var gesture = _tracker.OnMouse(action.Value, point.X, point.Y, hookData->time, overDesktop);
        if (gesture != DesktopGesture.None) _onGesture(gesture, point.X, point.Y);

        switch (Suppression)
        {
            case RightClickSuppression.SwallowUpAfterDrag when gesture == DesktopGesture.RightDragCompleted:
                return (LRESULT)1;

            case RightClickSuppression.SwallowDownAndReplay when action == MouseAction.RightDown && overDesktop:
                _swallowedRightDown = true;
                return (LRESULT)1;

            case RightClickSuppression.SwallowDownAndReplay when action == MouseAction.RightUp && _swallowedRightDown:
                _swallowedRightDown = false;
                // SendInput re-enters low-level hooks, so replay from the message loop, not from inside this callback.
                if (gesture != DesktopGesture.RightDragCompleted) PInvoke.PostThreadMessage(_threadId, ReplayRightClickMessage, 0, 0);
                return (LRESULT)1;
        }
        return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
    }

    private static void ReplayRightClick()
    {
        Span<INPUT> inputs =
        [
            new INPUT { type = INPUT_TYPE.INPUT_MOUSE, Anonymous = { mi = new MOUSEINPUT { dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN, dwExtraInfo = ReplayMarker } } },
            new INPUT { type = INPUT_TYPE.INPUT_MOUSE, Anonymous = { mi = new MOUSEINPUT { dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP, dwExtraInfo = ReplayMarker } } },
        ];
        var sent = PInvoke.SendInput(inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
        Lab.Log($"replayed right-click (sent {sent}/2)");
    }

    public void Dispose()
    {
        PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(1));
        _started.Dispose();
    }
}
