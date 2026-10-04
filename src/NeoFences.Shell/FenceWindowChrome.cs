using System.Runtime.InteropServices;
using NeoFences.Core.Layouts;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Shell;

/// <summary>Native look and placement of a fence window: tool-window styles, accent blur, rounded corners, pixel placement.</summary>
public static class FenceWindowChrome
{
    /// <summary>
    /// Tint (AABBGGRR) passed with the accent blur. M2c found Windows ignores it for ACCENT_ENABLE_BLURBEHIND (changing it
    /// changed nothing); the light-mode veil is drawn by the fence itself (ADR-015).
    /// </summary>
    public const uint DefaultTint = 0x40201A16;

    /// <summary>
    /// Not in the taskbar or Alt+Tab, not activated just by being shown, and no maximize/minimize boxes: without
    /// them a double-click on the title (a Fences habit, roll-up in M5) or Aero Snap cannot maximize the fence
    /// and save a full-screen rect (M2a review).
    /// </summary>
    public static void ApplyToolWindowStyles(nint handle)
    {
        var style = PInvoke.GetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        PInvoke.SetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE,
            style & ~(nint)(WINDOW_STYLE.WS_MAXIMIZEBOX | WINDOW_STYLE.WS_MINIMIZEBOX));
        var exStyle = PInvoke.GetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE,
            exStyle | (nint)(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE));
    }

    /// <summary>Blur of whatever is behind the window, focus-independent. Requires a layered (AllowsTransparency) window.</summary>
    /// <returns>False if Windows rejected the call; the fence then shows its plain tint (fallback).</returns>
    public static unsafe bool ApplyAccentBlur(nint handle, uint tintAbgr = DefaultTint)
    {
        var policy = new AccentPolicy { AccentState = AccentEnableBlurBehind, GradientColor = tintAbgr };
        var data = new WindowCompositionAttributeData { Attribute = WcaAccentPolicy, Data = (nint)(&policy), SizeOfData = sizeof(AccentPolicy) };
        return SetWindowCompositionAttribute(handle, ref data) != 0;
    }

    /// <summary>
    /// Rounded corners drawn by Windows 11 itself (DWMWA_WINDOW_CORNER_PREFERENCE), which also round the accent blur.
    /// A window region does not: Windows draws the blur over the whole rectangle, so a square of blur showed outside
    /// the rounded border (user screenshot 2026-10-03, M8a). On Windows 10 the call is refused and the fence keeps
    /// square blur corners under its rounded border (the region WindowChrome keeps still shapes clicks).
    /// </summary>
    /// <returns>False when Windows refused (Windows 10): the caller logs it once (M8a review).</returns>
    public static unsafe bool UseRoundedCorners(nint handle)
    {
        var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        return PInvoke.DwmSetWindowAttribute((HWND)handle, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &preference, sizeof(DWM_WINDOW_CORNER_PREFERENCE)).Succeeded;
    }

    /// <summary>Whether a key is down right now, whichever window has the keyboard (Esc during a tab drag, M9 review).</summary>
    public static bool IsKeyDown(int virtualKey) => PInvoke.GetAsyncKeyState(virtualKey) < 0;

    /// <summary>False once the window is destroyed (a fence deleted while a shell operation waited, M8c review).</summary>
    public static bool IsLiveWindow(nint handle) => handle != 0 && PInvoke.IsWindow((HWND)handle);

    /// <summary>When the message being handled was posted (ms tick, wraps): a click's own time, not the time it is handled.</summary>
    public static uint MessageTime() => unchecked((uint)PInvoke.GetMessageTime());

    /// <summary>The user's double-click time and size: a title double-click is recognised by NeoFences itself (M8b).</summary>
    public static (uint Milliseconds, int WidthPx, int HeightPx) DoubleClickSettings() =>
        (PInvoke.GetDoubleClickTime(), PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXDOUBLECLK), PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYDOUBLECLK));

    /// <summary>Reads the RECT that WM_MOVING / WM_SIZING point to (lParam).</summary>
    public static unsafe PixelRect ReadRect(nint rectPointer)
    {
        var rect = (RECT*)rectPointer;
        return new PixelRect(rect->left, rect->top, rect->right - rect->left, rect->bottom - rect->top);
    }

    /// <summary>Writes back the RECT of WM_MOVING / WM_SIZING; Windows then moves or sizes the window there.</summary>
    public static unsafe void WriteRect(nint rectPointer, PixelRect value) =>
        *(RECT*)rectPointer = new RECT(value.X, value.Y, value.X + value.Width, value.Y + value.Height);

    public static PixelRect GetPixelRect(nint handle)
    {
        PInvoke.GetWindowRect((HWND)handle, out var rect);
        return new PixelRect(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
    }

    /// <summary>
    /// Sends the fence below every app window. Showing a window puts it on top of the z-order; because the fence is
    /// owned by Progman, "bottom" ends just above the desktop (an owned window always stays above its owner).
    /// </summary>
    public static void SendToBack(nint handle) =>
        PInvoke.SetWindowPos((HWND)handle, HWND.HWND_BOTTOM, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    /// <summary>
    /// Call from WM_WINDOWPOSCHANGING (lParam): any z-order change (activation by a click, Show, another app being
    /// restored) ends at the bottom, just above the desktop. Being owned by Progman alone does not keep an activated
    /// fence below apps (user report 2026-10-02: the Inbox drew over Firefox).
    /// </summary>
    public static unsafe void KeepAtBottom(nint windowPosPointer)
    {
        var windowPos = (WINDOWPOS*)windowPosPointer;
        if ((windowPos->flags & SET_WINDOW_POS_FLAGS.SWP_NOZORDER) == 0) windowPos->hwndInsertAfter = HWND.HWND_BOTTOM;
    }

    /// <summary>Peek (spec §4.6): fences above every window while on; back to the bottom (above the desktop) when off.</summary>
    public static void SetTopmost(nint handle, bool topmost)
    {
        const SET_WINDOW_POS_FLAGS flags = SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;
        PInvoke.SetWindowPos((HWND)handle, topmost ? HWND.HWND_TOPMOST : HWND.HWND_NOTOPMOST, 0, 0, 0, 0, flags);
        if (!topmost) SendToBack(handle);
    }

    /// <summary>A topmost window the mouse passes through (the draw-a-fence rectangle): never activated, never hit.</summary>
    public static void MakeOverlay(nint handle)
    {
        var exStyle = PInvoke.GetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle | (nint)(WINDOW_EX_STYLE.WS_EX_TRANSPARENT
            | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOPMOST));
    }

    /// <summary>Mouse position in physical screen pixels (roll-up hover).</summary>
    public static (int X, int Y) GetCursorPosition()
    {
        PInvoke.GetCursorPos(out var position);
        return (position.X, position.Y);
    }

    /// <summary>Moves and sizes without changing z-order or activating.</summary>
    public static void SetPixelRect(nint handle, PixelRect rect)
    {
        const SET_WINDOW_POS_FLAGS flags = SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;
        // ponytail: set twice so a move onto a monitor with another DPI (WM_DPICHANGED resizes us) still ends at the requested size.
        PInvoke.SetWindowPos((HWND)handle, HWND.Null, rect.X, rect.Y, rect.Width, rect.Height, flags);
        PInvoke.SetWindowPos((HWND)handle, HWND.Null, rect.X, rect.Y, rect.Width, rect.Height, flags);
    }

    // ponytail: undocumented user32 API with no CsWin32 metadata (CLAUDE.md rule 5 exception, ADR-011); fallback is the tint.
    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public nint Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);

    private const int WcaAccentPolicy = 19;
    private const int AccentEnableBlurBehind = 3;
}
