using NeoFences.Core.Input;
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
        // Settings is remembered too (review I3); a fence is not, and a tray menu gone by then gives the keyboard to the desktop.
        _peekReturnWindow = _windows.Values.Any(fence => fence.Handle == foreground) ? 0 : foreground;
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
        if (_keyboardWindow is { } previous && previous != window)
        {
            previous.ReleaseKeyboard();
            Reattach(previous);
        }
        DesktopHost.DetachFromDesktop(window.Handle); // rc.2: never Progman's last active window (Win+D)
        if (!KeyboardFocus.TryGive(window.Handle)) Log.Information("peek: Windows kept the keyboard elsewhere; the fence shows its ring for the mouse");
        window.TakeKeyboard();
        _keyboardWindow = window;
        _lastKeyboardFence = window.BoxId;
    }

    /// <summary>Back under Progman once the keyboard has gone elsewhere (after the give-back, so it never activates there).</summary>
    private void Reattach(FenceWindow window) =>
        window.Dispatcher.BeginInvoke(() =>
        {
            if (!DesktopHost.AttachToDesktop(window.Handle)) Log.Warning("peek: fence {FenceId} not attached to the desktop again", window.FenceId);
        }, System.Windows.Threading.DispatcherPriority.Background);

    /// <summary>Peek ended (any way): the fence lets the keyboard go.</summary>
    private void ReleaseKeyboardForPeek()
    {
        _keyboardWindow?.ReleaseKeyboard();
        if (_keyboardWindow is { } released) Reattach(released);
        _keyboardWindow = null;
    }

    /// <summary>
    /// Peek ended by Esc or its hotkey: the app that had the keyboard gets it back (not after a click elsewhere or an open) —
    /// only while NeoFences still has it: an app the user switched to meanwhile keeps it (review I2).
    /// </summary>
    private void ReturnKeyboard()
    {
        if (KeyboardFocus.IsOwn(KeyboardFocus.Foreground())) KeyboardFocus.GiveBack(_peekReturnWindow);
        _peekReturnWindow = 0;
    }

    /// <summary>Esc in the fence that has the keyboard (review I1): Peek ends as with the global Esc.</summary>
    private void EndPeekFromKeyboard()
    {
        if (!_peeking) return;
        SetPeek(false);
        ReturnKeyboard();
    }

    /// <summary>
    /// While peeking, Esc is a global hotkey only while another app is in front (review I1): with a NeoFences window in front
    /// Esc stays its own — a rename or the Properties dialog cancels, and a fence with the keyboard ends Peek itself.
    /// </summary>
    private void UpdatePeekEscape()
    {
        var wanted = _peeking && !KeyboardFocus.IsOwn(KeyboardFocus.Foreground());
        if (wanted == (_peekEscapeHotkey is not null)) return;
        _peekEscapeHotkey?.Dispose();
        _peekEscapeHotkey = null;
        if (!wanted) return;
        _peekEscapeHotkey = new GlobalHotkey(_messages.Handle, PeekEscapeHotkeyId);
        if (!_peekEscapeHotkey.TryRegister(new Hotkey(Ctrl: false, Alt: false, Shift: false, Win: false, Key: "Escape"), virtualKey: 0x1B))
            Log.Warning("Esc is taken by another app; Peek ends with its hotkey or a click outside");
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
