using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using NeoFences.Core.Layouts;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>
/// The tab header that follows the pointer while a tab is dragged (M9): topmost, never activated, and the mouse passes
/// through it, so the fence that started the drag keeps the mouse.
/// </summary>
public sealed class TabGhost : Window
{
    private const int WidthDips = 140;
    private const int HeightDips = 28;
    private readonly nint _handle;

    public TabGhost(string title, bool lightTheme)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        var ink = lightTheme ? Colors.Black : Colors.White;
        var paper = lightTheme ? Colors.White : Color.FromRgb(0x20, 0x20, 0x20);
        Content = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, ink.R, ink.G, ink.B)),
            Background = new SolidColorBrush(Color.FromArgb(0xD8, paper.R, paper.G, paper.B)),
            Padding = new Thickness(10, 0, 10, 0),
            Child = new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(ink),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };
        _handle = new WindowInteropHelper(this).EnsureHandle();
        FenceWindowChrome.MakeOverlay(_handle);
    }

    /// <summary>Just below and right of the pointer (physical pixels), sized for the monitor's scale.</summary>
    public void Follow(int screenX, int screenY, double scale)
    {
        FenceWindowChrome.SetPixelRect(_handle, new PixelRect(screenX + (int)(12 * scale), screenY + (int)(12 * scale),
            (int)Math.Round(WidthDips * scale), (int)Math.Round(HeightDips * scale)));
        if (!IsVisible) Show();
    }
}
