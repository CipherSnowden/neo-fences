using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace NeoFences.Shell;

/// <summary>What the user's keyboard layout prints on a key (M13c): hotkey labels show "+" for OemPlus on a German keyboard, not "=".</summary>
public static class KeyboardLayout
{
    /// <summary>The character the current layout gives this virtual key, or null when it gives none.</summary>
    public static string? CharacterOf(int virtualKey)
    {
        var mapped = PInvoke.MapVirtualKey((uint)virtualKey, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_CHAR) & 0xFFFF; // the high bit marks a dead key
        return mapped is 0 or < 32 ? null : char.ToUpperInvariant((char)mapped).ToString();
    }
}
