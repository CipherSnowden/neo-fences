using NeoFences.Core.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Shell;

/// <summary>
/// Desktop gestures (spec §4.5, M5): a <c>WH_MOUSE_LL</c> hook on its own thread with its own message loop — the only
/// global hook NeoFences uses (hard rule 3; game mode removes it in M6). It acts only when the pointer is over the
/// desktop itself (not a fence or an app):
/// <list type="bullet">
/// <item>double-click → <see cref="DesktopGesture.DoubleClick"/> (quick-hide);</item>
/// <item>right-drag of 8 px or more → <see cref="DesktopGesture.RightDragStarted"/> / <see cref="DesktopGesture.RightDragCompleted"/>
/// (draw a fence). Every desktop right-press is swallowed and, if it was a plain click, replayed so Explorer's desktop
/// menu still appears (S2, proven in M0; S1 broke Explorer's desktop state).</item>
/// </list>
/// While <see cref="PeekActive"/>, a click outside NeoFences' windows raises <see cref="PeekClickOutside"/> (and goes through).
/// Callbacks run on the hook thread: marshal to the UI thread and return fast.
/// </summary>
public sealed class DesktopMouseHook : IDisposable
{
    /// <summary>dwExtraInfo stamped on the replayed right-click so the hook lets it through ("NFNC").</summary>
    private const nuint ReplayMarker = 0x4E464E43;

    private static readonly uint ReplayRightClickMessage = PInvoke.WM_APP + 1;

    private readonly HOOKPROC _hookCallback; // the field keeps the delegate alive while the hook exists
    private readonly DesktopGestureTracker _tracker;
    private readonly Action<DesktopGesture, int, int> _onGesture;
    private readonly Action _onPeekClickOutside;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private uint _threadId;
    private bool _swallowedRightDown;
    private long _dragPoint; // latest pointer position during a right-drag, packed (x << 32 | y), read by the overlay

    /// <summary>Set by the UI while fences are shown over windows (Peek).</summary>
    public volatile bool PeekActive;

    /// <summary>False when the draw gesture is off (M33): right-presses go to Windows untouched, no right-drag is tracked.</summary>
    public volatile bool HandleRightButton = true;

    public bool IsInstalled { get; private set; }

    /// <summary>The pointer during a right-drag (screen pixels), for the draw-a-fence overlay.</summary>
    public (int X, int Y) DragPoint
    {
        get
        {
            var packed = Interlocked.Read(ref _dragPoint);
            return ((int)(packed >> 32), (int)(packed & 0xFFFFFFFF));
        }
    }

    public DesktopMouseHook(Action<DesktopGesture, int, int> onGesture, Action onPeekClickOutside, Action<string> log)
    {
        _onGesture = onGesture;
        _onPeekClickOutside = onPeekClickOutside;
        _hookCallback = OnLowLevelMouse;
        _tracker = new DesktopGestureTracker(
            doubleClickMilliseconds: PInvoke.GetDoubleClickTime(),
            doubleClickSlopPixels: PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXDOUBLECLK) / 2,
            dragThresholdPixels: 8);
        // Highest: Windows silently drops a low-level hook whose callback misses LowLevelHooksTimeout, which a CPU-heavy
        // game can cause at normal priority (M5 review). The callback itself is short.
        _thread = new Thread(() => RunHookLoop(log)) { IsBackground = true, Name = "NeoFences.MouseHook", Priority = ThreadPriority.Highest };
        _thread.Start();
        _started.Wait(TimeSpan.FromSeconds(5));
    }

    private void RunHookLoop(Action<string> log)
    {
        _threadId = PInvoke.GetCurrentThreadId();
        using var module = PInvoke.GetModuleHandle((string?)null);
        using var hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _hookCallback, module, 0);
        IsInstalled = !hook.IsInvalid;
        log(IsInstalled ? "desktop gestures: WH_MOUSE_LL installed" : "desktop gestures: WH_MOUSE_LL could not be installed");
        _started.Set();
        if (!IsInstalled) return;

        while (PInvoke.GetMessage(out MSG message, HWND.Null, 0, 0) > 0)
        {
            if (message.message == ReplayRightClickMessage) ReplayRightClick();
        }
        log("desktop gestures: WH_MOUSE_LL removed");
    }

    private unsafe LRESULT OnLowLevelMouse(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code < 0) return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
        try
        {
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
            if (action == MouseAction.Move && _tracker.DragStart is not null)
                Interlocked.Exchange(ref _dragPoint, ((long)point.X << 32) | (uint)point.Y);

            // WindowFromPoint only on button events: moves arrive at up to 1000 Hz and must stay cheap.
            var overDesktop = action != MouseAction.Move && DesktopWindows.IsOverDesktop(point);
            if (PeekActive && action is MouseAction.LeftDown or MouseAction.RightDown && !DesktopWindows.IsOverNeoFences(point))
                _onPeekClickOutside();
            if (!HandleRightButton && action is MouseAction.RightDown or MouseAction.RightUp) return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);

            var gesture = _tracker.OnMouse(action.Value, point.X, point.Y, hookData->time, overDesktop);
            // RightDragStarted reports where the drag began (the press), every other gesture the pointer now.
            var (gestureX, gestureY) = gesture == DesktopGesture.RightDragStarted && _tracker.DragStart is { } start ? start : (point.X, point.Y);
            if (gesture != DesktopGesture.None) _onGesture(gesture, gestureX, gestureY);

            // S2: swallow every desktop right-press; replay it as a plain right-click if it did not become a drag.
            if (action == MouseAction.RightDown && overDesktop)
            {
                Interlocked.Exchange(ref _dragPoint, ((long)point.X << 32) | (uint)point.Y);
                _swallowedRightDown = true;
                return (LRESULT)1;
            }
            if (action == MouseAction.RightDown) _swallowedRightDown = false; // a lost right-up must not swallow an app's next one (M5 review I2)
            if (action == MouseAction.RightUp && _swallowedRightDown)
            {
                _swallowedRightDown = false;
                // SendInput re-enters low-level hooks, so replay from the message loop, not inside this callback.
                if (gesture != DesktopGesture.RightDragCompleted) PInvoke.PostThreadMessage(_threadId, ReplayRightClickMessage, 0, 0);
                return (LRESULT)1;
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // Never let an exception unwind into Windows' input pipeline: drop the gesture, keep the mouse working.
        }
        return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
    }

    private static void ReplayRightClick()
    {
        // The press was on the desktop; if the pointer slid onto a fence or an app before the release (a slip under the
        // drag threshold), a replay there would open that window's menu instead: drop it (M5 review M7).
        PInvoke.GetCursorPos(out var pointer);
        if (!DesktopWindows.IsOverDesktop(pointer)) return;
        Span<INPUT> inputs =
        [
            new INPUT { type = INPUT_TYPE.INPUT_MOUSE, Anonymous = { mi = new MOUSEINPUT { dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN, dwExtraInfo = ReplayMarker } } },
            new INPUT { type = INPUT_TYPE.INPUT_MOUSE, Anonymous = { mi = new MOUSEINPUT { dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP, dwExtraInfo = ReplayMarker } } },
        ];
        PInvoke.SendInput(inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
    }

    public void Dispose()
    {
        if (_threadId != 0) PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(1));
        _started.Dispose();
    }
}

/// <summary>What is under a screen point: the desktop itself, or a NeoFences fence.</summary>
public static class DesktopWindows
{
    internal static unsafe string ClassOf(HWND hwnd)
    {
        char* buffer = stackalloc char[256];
        var length = PInvoke.GetClassName(hwnd, buffer, 256);
        return new string(buffer, 0, length);
    }

    private static HWND RootAt(System.Drawing.Point screenPoint) =>
        PInvoke.GetAncestor(PInvoke.WindowFromPoint(screenPoint), GET_ANCESTOR_FLAGS.GA_ROOT);

    /// <summary>Progman (24H2+ icons host) or WorkerW (older icons host, wallpaper layer): the desktop itself.</summary>
    public static bool IsOverDesktop(System.Drawing.Point screenPoint) => ClassOf(RootAt(screenPoint)) is "Progman" or "WorkerW";

    /// <summary>
    /// A NeoFences window is under the point: a fence, or one of its menus (WPF and shell menus are top-level windows
    /// of our process). By process id, not title: GetWindowText on our own windows sends WM_GETTEXT to the UI thread,
    /// which would block the global mouse inside the hook whenever the UI is busy (M5 review I1).
    /// </summary>
    public static unsafe bool IsOverNeoFences(System.Drawing.Point screenPoint)
    {
        uint processId;
        PInvoke.GetWindowThreadProcessId(RootAt(screenPoint), &processId);
        return processId == (uint)Environment.ProcessId;
    }

    /// <summary>
    /// A visible native desktop icon is under the point. A double-click there opens the icon, so it must not also
    /// quick-hide. Explorer's own icon positions decide; UI Automation (a list item, or the F2 rename box) is the fallback:
    /// behind a live web wallpaper it only ever sees the wallpaper's page (M18 live check). Cross-process COM: call from the
    /// UI thread, never inside the hook.
    /// </summary>
    public static bool IsOverDesktopIcon(int x, int y, Action<string> log)
    {
        if (DesktopIcons.TryIsIconAt(x, y) == true) return true;
        Windows.Win32.UI.Accessibility.IUIAutomation? automation = null;
        Windows.Win32.UI.Accessibility.IUIAutomationElement? element = null;
        try
        {
            automation = (Windows.Win32.UI.Accessibility.IUIAutomation)Activator.CreateInstance(
                Type.GetTypeFromCLSID(typeof(Windows.Win32.UI.Accessibility.CUIAutomation).GUID)!)!;
            element = automation.ElementFromPoint(new System.Drawing.Point(x, y));
            // The rename box of a native icon (F2) counts too: a double-click there selects a word, it must not quick-hide (M8b).
            return element.CurrentControlType is Windows.Win32.UI.Accessibility.UIA_CONTROLTYPE_ID.UIA_ListItemControlTypeId
                or Windows.Win32.UI.Accessibility.UIA_CONTROLTYPE_ID.UIA_EditControlTypeId;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            log($"desktop gestures: icon hit-test failed ({failure.Message}); treating the point as empty desktop");
            return false;
        }
        finally
        {
            if (element is not null) System.Runtime.InteropServices.Marshal.ReleaseComObject(element);
            if (automation is not null) System.Runtime.InteropServices.Marshal.ReleaseComObject(automation);
        }
    }
}
