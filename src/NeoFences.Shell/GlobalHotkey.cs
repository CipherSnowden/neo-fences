using NeoFences.Core.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace NeoFences.Shell;

/// <summary>A system-wide hotkey (RegisterHotKey, no hook), delivered as WM_HOTKEY to <paramref name="windowHandle"/> (Peek, spec §4.6).</summary>
public sealed class GlobalHotkey(nint windowHandle, int id) : IDisposable
{
    public const int WmHotkey = 0x0312;

    public int Id => id;

    /// <param name="virtualKey">The key's virtual-key code (the App maps <see cref="Hotkey.Key"/> with WPF's KeyInterop).</param>
    /// <returns>False when another app already owns this combination (the user can pick another in settings).</returns>
    public bool TryRegister(Hotkey hotkey, uint virtualKey)
    {
        var modifiers = HOT_KEY_MODIFIERS.MOD_NOREPEAT
            | (hotkey.Ctrl ? HOT_KEY_MODIFIERS.MOD_CONTROL : 0)
            | (hotkey.Alt ? HOT_KEY_MODIFIERS.MOD_ALT : 0)
            | (hotkey.Shift ? HOT_KEY_MODIFIERS.MOD_SHIFT : 0)
            | (hotkey.Win ? HOT_KEY_MODIFIERS.MOD_WIN : 0);
        return PInvoke.RegisterHotKey((HWND)windowHandle, id, modifiers, virtualKey);
    }

    public void Dispose() => PInvoke.UnregisterHotKey((HWND)windowHandle, id);
}
