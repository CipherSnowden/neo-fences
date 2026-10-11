using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using NeoFences.Core.Model;

namespace NeoFences.App;

/// <summary>
/// M41a (spec 2026-10-11-m41a-feel-design §3, ADR-063): how items answer the mouse. Calm: a 2 px lift with a soft accent
/// glow, a dip when pressed, items fading in as they arrive. Lively adds a slight tilt and a light sweep on hover, a ripple
/// on press, and items rising in one after another. Off (also in a game, in safe mode, with Windows' animation effects off):
/// nothing moves. Only render transforms and brushes move — no layout passes; the one glow is on the hovered item only.
/// </summary>
public partial class FenceWindow
{
    private static readonly Duration Quick = new(TimeSpan.FromMilliseconds(160));
    private static readonly IEasingFunction Spring = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };

    static FenceWindow() =>
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.ToolTip), System.Windows.Controls.ToolTip.OpenedEvent, new RoutedEventHandler(OnToolTipOpened));

    private void InitializeMotion()
    {
        // handledEventsToo: the one-click press handles the event, the dip must still play.
        ItemList.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, press) => PressFeedback(press, pressed: true)), handledEventsToo: true);
        ItemList.AddHandler(PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler((_, release) => PressFeedback(release, pressed: false)), handledEventsToo: true);
    }

    /// <summary>The chrome that moves; none for covers (they lift themselves, M34), folder panels (too big to tilt) and widgets.</summary>
    private static Border? ChromeOf(ListBoxItem container) =>
        container.DataContext is FenceItemView { IsTile: false, IsPanel: false, IsWidget: false } ? container.Template?.FindName("Chrome", container) as Border : null; // final review: a widget never tilts (its text would blur)

    /// <summary>The chrome's own transforms, made once: a lift, a press scale and a tilt.</summary>
    private static (TranslateTransform Lift, ScaleTransform Press, RotateTransform Tilt) TransformsOf(FrameworkElement element)
    {
        if (element.RenderTransform is TransformGroup { Children: [TranslateTransform lift, ScaleTransform press, RotateTransform tilt] }) return (lift, press, tilt);
        (lift, press, tilt) = (new TranslateTransform(), new ScaleTransform(1, 1), new RotateTransform());
        element.RenderTransform = new TransformGroup { Children = [lift, press, tilt] };
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        return (lift, press, tilt);
    }

    private static void To(IAnimatable target, DependencyProperty property, double value, Duration duration, IEasingFunction? easing = null)
    {
        var animation = new DoubleAnimation(value, duration) { EasingFunction = easing };
        target.BeginAnimation(property, animation);
    }

    /// <summary>Hover: lift and glow (Calm); a tilt toward the pointer and a light sweep across the icon too (Lively).</summary>
    private void LiftItem(ListBoxItem container, bool lifted)
    {
        if (ChromeOf(container) is not { } chrome) return;
        if (_motion == Motion.Off)
        {
            if (chrome.Effect is not null) chrome.Effect = null;
            return;
        }
        var (lift, _, tilt) = TransformsOf(chrome);
        To(lift, TranslateTransform.YProperty, lifted ? -2 : 0, Quick, Spring);
        chrome.Effect = lifted && _style is { } style ? Glow(AccentColour(style), radius: 14, opacity: 0.45) : null;
        if (_motion != Motion.Lively) return;
        var toward = lifted ? Math.Clamp((Mouse.GetPosition(chrome).X / Math.Max(1, chrome.ActualWidth) - 0.5) * 3, -1.5, 1.5) : 0;
        To(tilt, RotateTransform.AngleProperty, toward, Quick, Spring);
        if (lifted) Sweep(chrome);
    }

    /// <summary>Lively: a soft band of light crosses the item once (the hover tint stays underneath).</summary>
    private void Sweep(Border chrome)
    {
        var tint = (Resources["FenceHover"] as SolidColorBrush)?.Color ?? Colors.Transparent;
        var band = Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF);
        var move = new TranslateTransform(-1, 0);
        chrome.Background = new LinearGradientBrush(new GradientStopCollection { new(tint, 0.3), new(band, 0.5), new(tint, 0.7) }, new Point(0, 0), new Point(1, 1))
        {
            RelativeTransform = move,
        };
        var sweep = new DoubleAnimation(-1, 1, new Duration(TimeSpan.FromMilliseconds(650))) { EasingFunction = new SineEase() };
        sweep.Completed += (_, _) => chrome.ClearValue(Border.BackgroundProperty); // the template's hover / selection again
        move.BeginAnimation(TranslateTransform.XProperty, sweep);
    }

    /// <summary>
    /// M41b (rc.3 leftover): every item back at rest — after a drag (Windows' drag loop swallows the release, so the dip stayed
    /// at 96 %) and when Motion turns Off (a lifted item stayed lifted).
    /// </summary>
    private void SettleItems()
    {
        for (var index = 0; index < ItemList.Items.Count; index++)
        {
            if (ItemList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container || ChromeOf(container) is not { } chrome
                || chrome.RenderTransform is not TransformGroup) continue;
            var (lift, press, tilt) = TransformsOf(chrome);
            lift.BeginAnimation(TranslateTransform.YProperty, null);
            press.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            press.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            tilt.BeginAnimation(RotateTransform.AngleProperty, null);
            (lift.Y, press.ScaleX, press.ScaleY, tilt.Angle) = (0, 1, 1, 0);
            chrome.Effect = null;
        }
    }

    /// <summary>Press: a dip to 96 % and back (Calm, Lively); a ripple from the pointer (Lively).</summary>
    private void PressFeedback(MouseButtonEventArgs press, bool pressed)
    {
        if (_motion == Motion.Off || press.OriginalSource is not DependencyObject source || FindAncestor<ListBoxItem>(source) is not { } container
            || ChromeOf(container) is not { } chrome) return;
        var (_, scale, _) = TransformsOf(chrome);
        To(scale, ScaleTransform.ScaleXProperty, pressed ? 0.96 : 1, new Duration(TimeSpan.FromMilliseconds(pressed ? 80 : 180)), pressed ? null : Spring);
        To(scale, ScaleTransform.ScaleYProperty, pressed ? 0.96 : 1, new Duration(TimeSpan.FromMilliseconds(pressed ? 80 : 180)), pressed ? null : Spring);
        if (!pressed || _motion != Motion.Lively || _style is not { } style) return;
        var accent = AccentColour(style);
        var at = press.GetPosition(chrome);
        var ripple = new RadialGradientBrush(Color.FromArgb(0x55, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B))
        {
            Center = new Point(at.X / Math.Max(1, chrome.ActualWidth), at.Y / Math.Max(1, chrome.ActualHeight)),
            GradientOrigin = new Point(at.X / Math.Max(1, chrome.ActualWidth), at.Y / Math.Max(1, chrome.ActualHeight)),
            RadiusX = 0.05, RadiusY = 0.05,
        };
        chrome.Background = ripple;
        var grow = new DoubleAnimation(0.05, 1.1, new Duration(TimeSpan.FromMilliseconds(420))) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        grow.Completed += (_, _) => chrome.ClearValue(Border.BackgroundProperty);
        ripple.BeginAnimation(RadialGradientBrush.RadiusXProperty, grow);
        ripple.BeginAnimation(RadialGradientBrush.RadiusYProperty, grow);
    }

    /// <summary>Items arriving (start, a tab switch, items added, the fences shown again): they fade in (Calm), rising one after another (Lively).</summary>
    private void Arrive(IReadOnlyCollection<FenceItemView> views)
    {
        if (_motion == Motion.Off || views.Count == 0 || !IsVisible) return;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            var index = 0;
            foreach (var view in views)
            {
                if (ItemList.ItemContainerGenerator.ContainerFromItem(view) is not ListBoxItem container) continue;
                var delay = TimeSpan.FromMilliseconds(_motion == Motion.Lively ? Math.Min(index++ * 25, 300) : 0);
                container.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220))) { BeginTime = delay, FillBehavior = FillBehavior.Stop });
                if (_motion != Motion.Lively) continue;
                var rise = container.RenderTransform as TranslateTransform ?? new TranslateTransform();
                container.RenderTransform = rise;
                rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, new Duration(TimeSpan.FromMilliseconds(260)))
                {
                    BeginTime = delay, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop,
                });
            }
        });
    }

    /// <summary>A tooltip over a fence grows in from 96 % with its fade (Lively and Calm).</summary>
    private static void OnToolTipOpened(object sender, RoutedEventArgs opened)
    {
        if (sender is not System.Windows.Controls.ToolTip { PlacementTarget: { } target } tip || Window.GetWindow(target) is not FenceWindow { Motion: not Motion.Off }) return;
        var grow = new ScaleTransform(0.96, 0.96);
        tip.RenderTransform = grow;
        tip.RenderTransformOrigin = new Point(0.5, 0);
        var duration = new Duration(TimeSpan.FromMilliseconds(140));
        grow.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, duration) { EasingFunction = Spring });
        grow.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, duration) { EasingFunction = Spring });
    }

    /// <summary>Title bar on hover (M36) fades in and out over 150 ms with motion on; at once with it off.</summary>
    private void FadeTitle(double opacity)
    {
        foreach (var element in new UIElement[] { TitleBar, TitleStripFill, TitleColorBar })
        {
            if (_motion == Motion.Off) { element.BeginAnimation(OpacityProperty, null); element.Opacity = opacity; }
            else element.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, new Duration(TimeSpan.FromMilliseconds(150))));
        }
    }

    private static DropShadowEffect Glow(Color colour, double radius, double opacity)
    {
        var glow = new DropShadowEffect { Color = colour, ShadowDepth = 0, BlurRadius = radius, Opacity = opacity };
        glow.Freeze();
        return glow;
    }
}
