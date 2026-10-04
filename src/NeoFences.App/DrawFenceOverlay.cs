using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using NeoFences.Core.Layouts;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>
/// The rectangle shown while the user right-drags on the desktop to draw a fence (M5). Topmost, never activated,
/// and the mouse passes through it, so the drag keeps going to the desktop.
/// </summary>
public sealed class DrawFenceOverlay : Window
{
    private readonly nint _handle;

    public DrawFenceOverlay(bool lightTheme)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        var ink = lightTheme ? Colors.Black : Colors.White;
        Content = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xC0, ink.R, ink.G, ink.B)),
            Background = new SolidColorBrush(Color.FromArgb(0x30, ink.R, ink.G, ink.B)),
        };
        _handle = new WindowInteropHelper(this).EnsureHandle();
        FenceWindowChrome.MakeOverlay(_handle);
    }

    /// <summary>Physical-pixel rect between the drag start and the pointer.</summary>
    public void Track(PixelRect rect) => FenceWindowChrome.SetPixelRect(_handle, rect);

    public static PixelRect Between((int X, int Y) start, (int X, int Y) end) => new(
        Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
}
