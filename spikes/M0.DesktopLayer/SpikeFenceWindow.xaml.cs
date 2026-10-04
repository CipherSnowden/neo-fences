using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Spikes.M0;

public partial class SpikeFenceWindow : Window
{
    // Layered fence: no tint of its own in accent modes, but 1/255 alpha keeps it hit-testable
    // (fully transparent pixels of a layered window are click-through).
    private static readonly SolidColorBrush NearlyClearBrush = new(Color.FromArgb(0x01, 0, 0, 0));
    private static readonly SolidColorBrush TintBrush = new(Color.FromArgb(0x80, 0x16, 0x1A, 0x20));

    private readonly bool _layered;
    private BackdropMode _backdropMode = BackdropMode.DwmAcrylic;

    internal HWND Handle { get; private set; }

    /// <summary>When it returns true, every z-order change is forced to HWND_BOTTOM (set by DesktopZOrder).</summary>
    public Func<bool> ForceBottom { get; set; } = () => false;

    /// <param name="layered">
    /// M0 follow-up: WS_EX_LAYERED (WPF AllowsTransparency) window instead of extended DWM glass.
    /// The glass variant rendered flat in every backdrop mode on Windows 11.
    /// </param>
    public SpikeFenceWindow(bool layered = false)
    {
        _layered = layered;
        InitializeComponent();
        if (layered)
        {
            Title = "Spike fence (layered)";
            Left = 520;
            AllowsTransparency = true;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                GlassFrameThickness = new Thickness(0),
                CaptionHeight = 30,
                ResizeBorderThickness = new Thickness(6),
                UseAeroCaptionButtons = false,
            });
            _backdropMode = BackdropMode.AccentBlur;
        }
        SourceInitialized += OnSourceInitialized;
    }

    public void SetBackdrop(BackdropMode mode)
    {
        _backdropMode = mode;
        if (_layered) Background = mode == BackdropMode.None ? TintBrush : NearlyClearBrush;
        Backdrop.Apply(Handle, mode, layered: _layered);
        StatusText.Text = $"{(_layered ? "LAYERED " : "")}Backdrop: {mode}\nCan you see the wallpaper through it?";
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        Handle = (HWND)new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(Handle);
        if (!_layered) source.CompositionTarget.BackgroundColor = Colors.Transparent;
        source.AddHook(OnWindowMessage);

        var exStyle = PInvoke.GetWindowLongPtr(Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLongPtr(Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE,
            exStyle | (nint)(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE));
        SetBackdrop(_backdropMode);
    }

    private unsafe nint OnWindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)message == PInvoke.WM_NCACTIVATE && _backdropMode == BackdropMode.DwmAcrylicKeepActive)
        {
            handled = true;
            return PInvoke.DefWindowProc((HWND)hwnd, PInvoke.WM_NCACTIVATE, 1, lParam);
        }
        if ((uint)message == PInvoke.WM_WINDOWPOSCHANGING && ForceBottom())
        {
            var windowPos = (WINDOWPOS*)lParam;
            if (!windowPos->flags.HasFlag(SET_WINDOW_POS_FLAGS.SWP_NOZORDER))
            {
                windowPos->hwndInsertAfter = HWND.HWND_BOTTOM;
            }
        }
        return 0;
    }
}
