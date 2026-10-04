using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.Controls;

namespace NeoFences.Spikes.M0;

public enum BackdropMode
{
    /// <summary>No blur; tinted only. Windows 10 fallback look.</summary>
    None,
    /// <summary>Documented DWM acrylic (DWMSBT_TRANSIENTWINDOW). Risk: may go solid while the window is inactive.</summary>
    DwmAcrylic,
    /// <summary>DWM acrylic + the window always answers WM_NCACTIVATE as active.</summary>
    DwmAcrylicKeepActive,
    /// <summary>Undocumented SetWindowCompositionAttribute acrylic (used by TranslucentTB etc.). Focus-independent.</summary>
    AccentAcrylic,
    /// <summary>Accent plain blur-behind (state 3): no acrylic noise/luminosity, light tint.</summary>
    AccentBlur,
    /// <summary>Accent acrylic with a light (25%) tint, to tell "too dark" apart from "not blurring".</summary>
    AccentAcrylicLight,
}

public static class Backdrop
{
    internal static unsafe void Apply(HWND hwnd, BackdropMode mode, bool layered = false)
    {
        if (layered)
        {
            // Layered windows get no DWM frame/system backdrop: accent policy (or the WPF brush) does all the work.
            var layeredAccent = mode switch
            {
                BackdropMode.AccentAcrylic => (AccentEnableAcrylicBlurBehind, 0x99201A16u),
                BackdropMode.AccentAcrylicLight => (AccentEnableAcrylicBlurBehind, 0x40201A16u),
                BackdropMode.AccentBlur => (AccentEnableBlurBehind, 0x40201A16u),
                _ => (AccentDisabled, 0u),
            };
            var layeredResult = SetAccent(hwnd, accentState: layeredAccent.Item1, tint: layeredAccent.Item2);
            Lab.Log($"LAYERED backdrop {mode}: accent state {layeredAccent.Item1} result={layeredResult}");
            return;
        }

        var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
        PInvoke.DwmExtendFrameIntoClientArea(hwnd, margins);

        BOOL darkMode = true;
        PInvoke.DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, &darkMode, (uint)sizeof(BOOL));
        var corners = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        PInvoke.DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &corners, (uint)sizeof(DWM_WINDOW_CORNER_PREFERENCE));

        // M0 finding: setting DWMWA_SYSTEMBACKDROP_TYPE at all (even to NONE) appeared to suppress accent blur,
        // so accent modes reset it to AUTO (the default) instead.
        var backdropType = mode switch
        {
            BackdropMode.DwmAcrylic or BackdropMode.DwmAcrylicKeepActive => DWM_SYSTEMBACKDROP_TYPE.DWMSBT_TRANSIENTWINDOW,
            BackdropMode.None => DWM_SYSTEMBACKDROP_TYPE.DWMSBT_NONE,
            _ => DWM_SYSTEMBACKDROP_TYPE.DWMSBT_AUTO,
        };
        var hresult = PInvoke.DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, &backdropType, (uint)sizeof(DWM_SYSTEMBACKDROP_TYPE));

        var (accentState, tint) = mode switch
        {
            BackdropMode.AccentAcrylic => (AccentEnableAcrylicBlurBehind, 0x99201A16u),      // AABBGGRR: ~60% dark tint
            BackdropMode.AccentAcrylicLight => (AccentEnableAcrylicBlurBehind, 0x40201A16u), // ~25% dark tint
            BackdropMode.AccentBlur => (AccentEnableBlurBehind, 0x40201A16u),
            _ => (AccentDisabled, 0u),
        };
        var accentResult = SetAccent(hwnd, accentState: accentState, tint: tint);
        Lab.Log($"backdrop {mode}: DWMWA_SYSTEMBACKDROP_TYPE={backdropType} hr=0x{hresult.Value:X8}, accent state {accentState} result={accentResult}");
    }

    // ponytail: undocumented user32 API, no CsWin32 metadata; hand-written per CLAUDE.md rule 5 exception. Spike only.
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
    private const int AccentDisabled = 0;
    private const int AccentEnableBlurBehind = 3;
    private const int AccentEnableAcrylicBlurBehind = 4;

    private static unsafe int SetAccent(HWND hwnd, int accentState, uint tint)
    {
        var policy = new AccentPolicy { AccentState = accentState, GradientColor = tint };
        var data = new WindowCompositionAttributeData { Attribute = WcaAccentPolicy, Data = (nint)(&policy), SizeOfData = sizeof(AccentPolicy) };
        return SetWindowCompositionAttribute((nint)hwnd.Value, ref data);
    }
}
