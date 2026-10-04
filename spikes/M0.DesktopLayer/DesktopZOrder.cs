using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Spikes.M0;

public enum ZOrderStrategy
{
    /// <summary>Pin to HWND_BOTTOM only. Expected to vanish on Win+D (baseline).</summary>
    BottomOnly,
    /// <summary>ADR-003: pin to bottom; when the desktop becomes foreground (Win+D) raise to HWND_TOP.</summary>
    RaiseOnShowDesktop,
    /// <summary>Owner = Progman, so the fence rides above the desktop. Risk: destroyed when Explorer restarts.</summary>
    OwnedByProgman,
}

/// <summary>Spike for spec §4.1–4.2: keeps fence windows at desktop level and survives Win+D.</summary>
public sealed class DesktopZOrder : IDisposable
{
    private static readonly SET_WINDOW_POS_FLAGS ZOrderOnly =
        SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;

    private readonly WINEVENTPROC _foregroundCallback; // field keeps the delegate alive while the hook exists
    private readonly HWINEVENTHOOK _foregroundHook;
    private readonly List<SpikeFenceWindow> _fences = [];
    private bool _raisedAboveDesktop;

    public ZOrderStrategy Strategy { get; private set; } = ZOrderStrategy.RaiseOnShowDesktop;

    /// <summary>Raised on the UI thread for every foreground change, with the window class name.</summary>
    internal event Action<HWND, string>? ForegroundChanged;

    public DesktopZOrder()
    {
        _foregroundCallback = OnForegroundEvent;
        _foregroundHook = PInvoke.SetWinEventHook(PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND,
            HMODULE.Null, _foregroundCallback, 0, 0, PInvoke.WINEVENT_OUTOFCONTEXT);
        Lab.Log($"foreground WinEvent hook installed: {!_foregroundHook.IsNull}");
    }

    public void Track(SpikeFenceWindow fence)
    {
        fence.ForceBottom = () => Strategy != ZOrderStrategy.OwnedByProgman && !_raisedAboveDesktop;
        fence.Closed += (_, _) => Lab.Log("fence window CLOSED (destroyed)");
        _fences.Add(fence);
        ApplyToFence(fence);
    }

    public void SetStrategy(ZOrderStrategy strategy)
    {
        Strategy = strategy;
        _raisedAboveDesktop = false;
        _fences.ForEach(ApplyToFence);
        Lab.Log($"z-order strategy = {strategy}");
    }

    /// <summary>Call after Explorer restarts: Progman is a new window, ownership must be re-applied.</summary>
    public void Reapply() => _fences.ForEach(ApplyToFence);

    private unsafe void ApplyToFence(SpikeFenceWindow fence)
    {
        var owner = Strategy == ZOrderStrategy.OwnedByProgman ? PInvoke.FindWindow("Progman", null) : HWND.Null;
        PInvoke.SetWindowLongPtr(fence.Handle, WINDOW_LONG_PTR_INDEX.GWLP_HWNDPARENT, (nint)owner.Value);
        PInvoke.SetWindowPos(fence.Handle, HWND.HWND_BOTTOM, 0, 0, 0, 0, ZOrderOnly);
    }

    private unsafe void OnForegroundEvent(HWINEVENTHOOK hook, uint eventType, HWND foreground, int objectId, int childId, uint threadId, uint eventTime)
    {
        var className = DesktopWindows.ClassOf(foreground);
        Lab.Log($"foreground -> {className} 0x{(nint)foreground.Value:X}");
        ForegroundChanged?.Invoke(foreground, className);

        if (Strategy != ZOrderStrategy.RaiseOnShowDesktop || _fences.Any(fence => fence.Handle == foreground)) return;

        if (DesktopWindows.IsDesktopClass(className))
        {
            _raisedAboveDesktop = true;
            _fences.ForEach(fence => PInvoke.SetWindowPos(fence.Handle, HWND.HWND_TOP, 0, 0, 0, 0, ZOrderOnly));
            Lab.Log("desktop is foreground (Win+D?) -> fences raised to HWND_TOP");
        }
        else if (_raisedAboveDesktop)
        {
            _raisedAboveDesktop = false;
            _fences.ForEach(fence => PInvoke.SetWindowPos(fence.Handle, HWND.HWND_BOTTOM, 0, 0, 0, 0, ZOrderOnly));
            Lab.Log("app is foreground -> fences back to HWND_BOTTOM");
        }
    }

    public void Dispose() => PInvoke.UnhookWinEvent(_foregroundHook);
}
