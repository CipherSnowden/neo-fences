using NeoFences.Core.Appearance;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls.Dialogs;

namespace NeoFences.Shell;

/// <summary>Windows' colour dialog (M14, fence menu → Colour → Custom…): any colour, no new dependency.</summary>
public static class ColorPicker
{
    private static readonly uint[] CustomColors = new uint[16]; // the dialog's 16 "custom colours" for this run

    /// <returns>"#RRGGBB", or null when the user cancelled.</returns>
    public static unsafe string? TryPick(nint ownerHandle, Argb? initial)
    {
        fixed (uint* custom = CustomColors)
        {
            var dialog = new CHOOSECOLORW
            {
                lStructSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<CHOOSECOLORW>(),
                hwndOwner = (HWND)ownerHandle,
                rgbResult = initial is { } start ? (COLORREF)(uint)(start.R | start.G << 8 | start.B << 16) : default,
                lpCustColors = (COLORREF*)custom,
                Flags = CHOOSECOLOR_FLAGS.CC_RGBINIT | CHOOSECOLOR_FLAGS.CC_FULLOPEN | CHOOSECOLOR_FLAGS.CC_ANYCOLOR,
            };
            if (!PInvoke.ChooseColor(ref dialog)) return null;
            var rgb = dialog.rgbResult.Value;
            return new Argb(0xFF, (byte)rgb, (byte)(rgb >> 8), (byte)(rgb >> 16)).ToHex();
        }
    }
}
