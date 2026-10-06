using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NeoFences.Core.Appearance;

namespace NeoFences.App;

/// <summary>
/// Colour ▸ Custom colour… (M35, spec §3): replaces Windows' 1995 colour dialog. Drag in the field (saturation × brightness),
/// slide the hue, or type a hex colour ("#E23A50", "e23a50", "#abc"); OK takes it, Cancel or Esc changes nothing.
/// </summary>
public partial class ColourWindow : Window
{
    private Hsv _colour;
    private bool _updating;

    private ColourWindow(Argb initial)
    {
        InitializeComponent();
        _colour = Hsv.FromArgb(initial);
        Field.MouseLeftButtonDown += (_, press) => { Field.CaptureMouse(); FromField(press.GetPosition(Field)); };
        Field.MouseMove += (_, move) => { if (Field.IsMouseCaptured) FromField(move.GetPosition(Field)); };
        Field.MouseLeftButtonUp += (_, _) => Field.ReleaseMouseCapture();
        Field.SizeChanged += (_, _) => Show(updateHex: false);
        HueSlider.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            _colour = _colour with { H = HueSlider.Value };
            Show(updateHex: true);
        };
        HexBox.TextChanged += (_, _) =>
        {
            if (_updating || Argb.FromUserHex(HexBox.Text) is not { } typed) return;
            _colour = Hsv.FromArgb(typed);
            Show(updateHex: false);
        };
        OkButton.Click += (_, _) => DialogResult = true;
        Loaded += (_, _) => { Show(updateHex: true); HexBox.Focus(); HexBox.SelectAll(); };
    }

    /// <summary>The picked colour as "#RRGGBB", or null when cancelled.</summary>
    public static string? Pick(Window? owner, Argb? initial)
    {
        var window = new ColourWindow(initial ?? new Argb(0xFF, 0xE2, 0x3A, 0x50)) { Owner = owner };
        if (owner is null) window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return window.ShowDialog() == true ? window._colour.ToArgb().ToHex() : null;
    }

    private void FromField(Point point)
    {
        _colour = _colour with
        {
            S = Math.Clamp(point.X / Math.Max(1, Field.ActualWidth), 0, 1),
            V = 1 - Math.Clamp(point.Y / Math.Max(1, Field.ActualHeight), 0, 1),
        };
        Show(updateHex: true);
    }

    private void Show(bool updateHex)
    {
        _updating = true;
        var pure = new Hsv(_colour.H, 1, 1).ToArgb();
        HueFill.Background = new SolidColorBrush(Color.FromRgb(pure.R, pure.G, pure.B));
        var colour = _colour.ToArgb();
        Preview.Background = new SolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B));
        HueSlider.Value = _colour.H;
        Canvas.SetLeft(FieldThumb, _colour.S * Field.ActualWidth - FieldThumb.Width / 2);
        Canvas.SetTop(FieldThumb, (1 - _colour.V) * Field.ActualHeight - FieldThumb.Height / 2);
        if (updateHex) HexBox.Text = colour.ToHex();
        _updating = false;
    }
}
