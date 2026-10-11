using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Shell;

/// <summary>
/// Now playing's mouse wheel (M41a): Windows' own volume keys, so Windows' volume flyout shows the level.
/// ponytail: the volume keys' 2 % steps and Windows' flyout, not Core Audio and an in-widget bar; IAudioEndpointVolume if the
/// owner wants the level drawn inside the widget.
/// </summary>
public static class VolumeKeys
{
    public static void Step(bool up)
    {
        var key = up ? VIRTUAL_KEY.VK_VOLUME_UP : VIRTUAL_KEY.VK_VOLUME_DOWN;
        Span<INPUT> inputs =
        [
            new INPUT { type = INPUT_TYPE.INPUT_KEYBOARD, Anonymous = { ki = new KEYBDINPUT { wVk = key } } },
            new INPUT { type = INPUT_TYPE.INPUT_KEYBOARD, Anonymous = { ki = new KEYBDINPUT { wVk = key, dwFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP } } },
        ];
        PInvoke.SendInput(inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
    }
}
