using NeoFences.Core.Layouts;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// The keyboard way in (M38, spec 2026-10-06-keyboard-and-older-windows-design §1, ADR-060): Peek gives the keyboard to the
/// fence under the mouse (else the one used last, else the first in reading order); Tab / Shift+Tab move to the next /
/// previous fence; Esc or the hotkey ends Peek and gives the keyboard back to the app that had it.
/// </summary>
public sealed partial class FenceHost
{
    private nint _peekReturnWindow;
    private FenceWindow? _keyboardWindow;
    private string? _lastKeyboardFence;

    /// <summary>Peek started: the app with the keyboard is remembered and a fence takes it.</summary>
    private void TakeKeyboardForPeek()
    {
        var foreground = KeyboardFocus.Foreground();
        _peekReturnWindow = KeyboardFocus.IsOwn(foreground) ? 0 : foreground; // the tray menu or Settings: nothing to give back
        var spots = KeyboardSpots();
        var (cursorX, cursorY) = FenceWindowChrome.GetCursorPosition();
        var underMouse = spots.FirstOrDefault(spot => cursorX >= spot.X && cursorX < spot.X + spot.W && cursorY >= spot.Y && cursorY < spot.Y + spot.H)?.Id;
        var start = KeyboardOrder.Start(KeyboardOrder.ReadingOrder(spots), underMouse, _lastKeyboardFence);
        if (start is not null && _windows.TryGetValue(start, out var window)) GiveKeyboard(window);
    }

    /// <summary>Tab / Shift+Tab in a fence while peeking: the next / previous fence in reading order.</summary>
    private void CycleKeyboard(FenceWindow from, int step)
    {
        if (!_peeking) return;
        var next = KeyboardOrder.Next(KeyboardOrder.ReadingOrder(KeyboardSpots()), from.BoxId, step);
        if (next is not null && _windows.TryGetValue(next, out var window)) GiveKeyboard(window);
    }

    private void GiveKeyboard(FenceWindow window)
    {
        if (_keyboardWindow is { } previous && previous != window) previous.ReleaseKeyboard();
        if (!KeyboardFocus.TryGive(window.Handle)) Log.Information("peek: Windows kept the keyboard elsewhere; the fence shows its ring for the mouse");
        window.TakeKeyboard();
        _keyboardWindow = window;
        _lastKeyboardFence = window.BoxId;
    }

    /// <summary>Peek ended (any way): the fence lets the keyboard go.</summary>
    private void ReleaseKeyboardForPeek()
    {
        _keyboardWindow?.ReleaseKeyboard();
        _keyboardWindow = null;
    }

    /// <summary>Peek ended by Esc or its hotkey: the app that had the keyboard gets it back (not after a click elsewhere or an open).</summary>
    private void ReturnKeyboard()
    {
        KeyboardFocus.GiveBack(_peekReturnWindow);
        _peekReturnWindow = 0;
    }

    /// <summary>Every shown fence (a box once) with its rect and monitor, for the reading order.</summary>
    private List<FenceSpot> KeyboardSpots()
    {
        var spots = new List<FenceSpot>();
        foreach (var (boxId, window) in _windows)
        {
            if (!window.IsVisible || window.Handle == 0) continue;
            var rect = FenceWindowChrome.GetPixelRect(window.Handle);
            var (centerX, centerY) = (rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            var monitor = _monitors.FirstOrDefault(candidate => centerX >= candidate.WorkLeftPx && centerX < candidate.WorkLeftPx + candidate.WorkWidthPx
                                                                && centerY >= candidate.WorkTopPx && centerY < candidate.WorkTopPx + candidate.WorkHeightPx);
            spots.Add(new FenceSpot(boxId, rect.X, rect.Y, rect.Width, rect.Height, monitor?.IsPrimary ?? true, monitor?.WorkLeftPx ?? 0));
        }
        return spots;
    }
}
