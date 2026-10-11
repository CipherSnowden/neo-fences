using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using NeoFences.Core.Model;

namespace NeoFences.App;

/// <summary>
/// What a theme draws behind a fence's items besides the veil (M41a, ADR-063, spec 2026-10-11-m41a-feel-design §1):
/// Aurora's drifting colour light, HUD's cut corners and scan line, Neon's breathing edge, Light Mica's grain and highlight.
/// Everything is brushes and shapes (no per-frame effects: a layered window renders effects in software); what moves runs at
/// most <see cref="FramesPerSecond"/> frames a second, only while motion is on and the fence shows.
/// </summary>
public sealed class FenceThemeLayer
{
    public const int FramesPerSecond = 20;
    private const double Chamfer = 12;

    private readonly Grid _host;
    private FenceTheme _theme;
    private Color _accent;
    private Brush _veil = Brushes.Transparent;
    private Motion _motion = Motion.Off;
    private bool _paused;
    private readonly List<(IAnimatable Target, DependencyProperty Property, Func<double, AnimationTimeline> Make)> _moving = [];
    private Path? _hudFrame;
    private Rectangle? _scan;

    public FenceThemeLayer(Grid host)
    {
        _host = host;
        _host.SizeChanged += (_, _) => Rebuild();
    }

    /// <summary>HUD draws the fence's veil itself, inside its cut-corner frame (the window's rounded frame stays empty).</summary>
    public bool DrawsVeil => _theme == FenceTheme.Hud;

    public void Apply(FenceTheme theme, Color accent, Brush veil, Motion motion)
    {
        if (theme == _theme && accent == _accent && Equals(veil, _veil) && motion == _motion) return;
        (_theme, _accent, _veil, _motion) = (theme, accent, veil, motion);
        Rebuild();
    }

    /// <summary>Hidden, rolled up or gaming: nothing moves; shown again: it moves on.</summary>
    public void Pause(bool paused)
    {
        if (paused == _paused) return;
        _paused = paused;
        Animate();
    }

    private void Rebuild()
    {
        _host.Children.Clear();
        _moving.Clear();
        _hudFrame = null;
        _scan = null;
        var (width, height) = (_host.ActualWidth, _host.ActualHeight);
        if (width < 1 || height < 1) return;
        _host.Clip = _theme == FenceTheme.Hud ? null : new RectangleGeometry(new Rect(0, 0, width, height), 7, 7);
        switch (_theme)
        {
            case FenceTheme.Aurora: BuildAurora(width, height); break;
            case FenceTheme.Hud: BuildHud(width, height); break;
            case FenceTheme.Neon: BuildNeon(); break;
            case FenceTheme.LightMica: BuildMica(); break;
        }
        Animate();
    }

    /// <summary>Three soft lights in the accent and two neighbours, drifting slowly (one loop about 20 s).</summary>
    private void BuildAurora(double width, double height)
    {
        Color[] lights = [_accent, Mix(_accent, Color.FromRgb(0x9B, 0x7B, 0xFF), 0.55), Mix(_accent, Color.FromRgb(0xFF, 0x7A, 0xB4), 0.55)];
        (double X, double Y, double DriftX, double DriftY, double Seconds)[] places =
            [(-0.25, -0.35, 0.22, 0.18, 21), (0.45, 0.25, -0.2, -0.16, 17), (-0.1, 0.55, 0.16, -0.22, 25)];
        for (var index = 0; index < lights.Length; index++)
        {
            var place = places[index];
            var size = Math.Max(width, height) * 0.9;
            var light = new Ellipse
            {
                Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Fill = new RadialGradientBrush(Color.FromArgb(0x5C, lights[index].R, lights[index].G, lights[index].B), Colors.Transparent),
                Opacity = index == 0 ? 1 : 0.8,
            };
            var move = new TranslateTransform(width * place.X, height * place.Y);
            light.RenderTransform = move;
            _host.Children.Add(light);
            Drift(move, TranslateTransform.XProperty, move.X, move.X + width * place.DriftX, place.Seconds);
            Drift(move, TranslateTransform.YProperty, move.Y, move.Y + height * place.DriftY, place.Seconds * 0.83);
        }
    }

    /// <summary>A cut-corner frame in the accent, faint corner ticks, and a thin scan line sweeping down every ~8 s.</summary>
    private void BuildHud(double width, double height)
    {
        var figure = new PathFigure { StartPoint = new Point(Chamfer, 0.5), IsClosed = true };
        foreach (var point in new[] { new Point(width - 0.5, 0.5), new Point(width - 0.5, height - Chamfer), new Point(width - Chamfer, height - 0.5),
                     new Point(0.5, height - 0.5), new Point(0.5, Chamfer) })
            figure.Segments.Add(new LineSegment(point, isStroked: true));
        var accent = new SolidColorBrush(_accent);
        _hudFrame = new Path { Data = new PathGeometry([figure]), Fill = _veil, Stroke = accent, StrokeThickness = 1 };
        _host.Children.Add(_hudFrame);
        var tick = new SolidColorBrush(Color.FromArgb(0xB0, _accent.R, _accent.G, _accent.B));
        _host.Children.Add(new Path { Data = Geometry.Parse($"M {width - 22},5 H {width - 5} V 22 M 5,{height - 22} V {height - 5} H 22"), Stroke = tick, StrokeThickness = 2 });
        _scan = new Rectangle
        {
            Height = 36, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1, 0, 1, 0),
            Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new(Colors.Transparent, 0), new(Color.FromArgb(0x38, _accent.R, _accent.G, _accent.B), 0.85), new(Color.FromArgb(0x70, _accent.R, _accent.G, _accent.B), 1),
            }, new Point(0, 0), new Point(0, 1)),
            RenderTransform = new TranslateTransform(0, -40),
        };
        _host.Children.Add(_scan);
        var sweep = (TranslateTransform)_scan.RenderTransform;
        _moving.Add((sweep, TranslateTransform.YProperty, speed =>
        {
            var seconds = 8 / speed;
            var frames = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds), RepeatBehavior = RepeatBehavior.Forever };
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(-40, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(height, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds * 0.3)), new SineEase()));
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(height, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds))));
            return frames;
        }));
    }

    /// <summary>A lit accent edge with a soft inner glow that breathes (4 s in, 4 s out).</summary>
    private void BuildNeon()
    {
        var glow = new Grid();
        foreach (var (thickness, alpha) in new[] { (9.0, (byte)0x14), (5.0, (byte)0x26), (2.5, (byte)0x48) })
            glow.Children.Add(new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(thickness), BorderBrush = new SolidColorBrush(Color.FromArgb(alpha, _accent.R, _accent.G, _accent.B)) });
        glow.Children.Add(new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1.5), BorderBrush = new SolidColorBrush(_accent) });
        _host.Children.Add(glow);
        _moving.Add((glow, UIElement.OpacityProperty, speed =>
            new DoubleAnimation(0.55, 1, TimeSpan.FromSeconds(4 / speed)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() }));
    }

    /// <summary>Fine grain and a top highlight: bright glass with some depth. Nothing moves.</summary>
    private void BuildMica()
    {
        _host.Children.Add(new Rectangle { Fill = Grain.Value, Opacity = 0.06 });
        _host.Children.Add(new Rectangle
        {
            Height = 44, VerticalAlignment = VerticalAlignment.Top,
            Fill = new LinearGradientBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 90),
        });
    }

    private void Drift(IAnimatable target, DependencyProperty property, double from, double to, double seconds) =>
        _moving.Add((target, property, speed => new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds / speed))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        }));

    /// <summary>Calm runs at half speed; Off or paused stops every animation where it is.</summary>
    private void Animate()
    {
        var running = !_paused && _motion != Motion.Off;
        var speed = _motion == Motion.Calm ? 0.5 : 1;
        foreach (var (target, property, make) in _moving)
        {
            if (!running)
            {
                target.BeginAnimation(property, null);
                continue;
            }
            var animation = make(speed);
            Timeline.SetDesiredFrameRate(animation, FramesPerSecond);
            target.BeginAnimation(property, animation);
        }
    }

    private static Color Mix(Color first, Color second, double amount) => Color.FromRgb(
        (byte)(first.R + (second.R - first.R) * amount), (byte)(first.G + (second.G - first.G) * amount), (byte)(first.B + (second.B - first.B) * amount));

    /// <summary>A 64 px tile of soft noise, made once.</summary>
    private static readonly Lazy<ImageBrush> Grain = new(() =>
    {
        const int size = 64;
        var pixels = new byte[size * size];
        var random = new Random(41);
        random.NextBytes(pixels);
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Gray8, null, pixels, size);
        bitmap.Freeze();
        var brush = new ImageBrush(bitmap) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, size, size), ViewportUnits = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    });
}
