using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using NeoFences.Core.Items;
using NeoFences.Core.Model;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>What a Now playing widget shows (M41a): the sessions, the one shown, its face, its cover and the cover's glow.</summary>
/// <param name="CoverGlow">The cover decoded a few pixels wide: scaled up smoothly it is the soft colour glow behind the glass.</param>
public sealed record NowPlayingData(IReadOnlyList<MediaSessionInfo> Sessions, MediaSessionInfo? Shown, NowPlayingFace Face, ImageSource? Cover, ImageSource? CoverGlow);

/// <summary>
/// Now playing (M41a, spec 2026-10-11-m41a-feel-design §6.6; the owner's now-playing-v3): the cover's colours glow behind
/// the glass, the record slides out of its sleeve and spins while playing (the tonearm lowers on Large); pause and the arm
/// lifts, the record spins down and slides home. A scrub bar, previous / play-pause / next, shuffle and repeat (Large), the
/// players to switch between. Small is the island pill. Nothing moves with motion off (the play/pause icon tells).
/// </summary>
public sealed class NowPlayingCard : Grid
{
    private const double SpinSeconds = 2.4;
    private FenceItemView? _view;
    private WidgetSize? _built;
    private readonly RotateTransform _spin = new();
    private readonly TranslateTransform _out = new();
    private readonly RotateTransform _arm = new(-4);
    private bool? _spinning;
    private bool _playing, _held;
    private double _recordOut;

    // filled by Build for the size
    private Image? _glow, _sleeve, _label;
    private TextBlock? _title, _artist, _album, _elapsed, _left, _live;
    private Path? _playIcon;
    private Border? _scrubFill, _scrubTrack;
    private Button? _shuffle, _repeat;
    private StackPanel? _players;
    private FrameworkElement? _art;

    public NowPlayingCard()
    {
        DataContextChanged += (_, change) =>
        {
            if (change.OldValue is FenceItemView old) old.PropertyChanged -= OnViewChanged;
            _view = change.NewValue as FenceItemView;
            if (_view is not null) _view.PropertyChanged += OnViewChanged;
            Build();
        };
        PreviewMouseWheel += (_, wheel) => // the Windows volume, with its own flyout
        {
            wheel.Handled = true;
            VolumeKeys.Step(up: wheel.Delta > 0);
        };
        Unloaded += (_, _) => Spin(false);
    }

    private FenceWindow? Window => System.Windows.Window.GetWindow(this) as FenceWindow;
    private Motion Motion => Window?.Motion ?? Motion.Off;

    private void OnViewChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName == nameof(FenceItemView.WidgetSize)) Build();
        else if (change.PropertyName is nameof(FenceItemView.Media) or nameof(FenceItemView.WidgetNow)) Show();
    }

    private void Build()
    {
        if (_view is not { } view || view.WidgetSize == _built) return;
        _built = view.WidgetSize;
        Children.Clear();
        _spinning = null;
        var (content, nominal) = view.WidgetSize switch
        {
            WidgetSize.Small => (Pill(), new Size(300, 58)),
            WidgetSize.Medium => (Square(), new Size(206, 214)),
            _ => (Wide(), new Size(420, 214)),
        };
        content.Width = nominal.Width;
        content.Height = nominal.Height;
        Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = content });
        Show();
    }

    // ---------- the three sizes ----------

    private FrameworkElement Wide()
    {
        var root = Card(radius: 20);
        _art = Art(left: 20, top: 31, size: 152, outBy: 66, arm: true, root);
        var meta = new DockPanel { Margin = new Thickness(262, 14, 16, 12) };
        _players = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(_players, Dock.Top);
        meta.Children.Add(_players);
        var words = WidgetWords(19, 12.5, album: true);
        DockPanel.SetDock(words, Dock.Top);
        meta.Children.Add(words);
        var bottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        bottom.Children.Add(Scrub());
        bottom.Children.Add(Times());
        bottom.Children.Add(Controls(full: true));
        meta.Children.Add(bottom);
        root.Children.Add(meta);
        return root;
    }

    private FrameworkElement Square()
    {
        var root = Card(radius: 20);
        _art = Art(left: 16, top: 16, size: 100, outBy: 72, arm: false, root);
        var meta = new DockPanel { Margin = new Thickness(16, 124, 14, 10) };
        var words = WidgetWords(15, 11.5, album: false);
        DockPanel.SetDock(words, Dock.Top);
        meta.Children.Add(words);
        var bottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        bottom.Children.Add(Scrub());
        bottom.Children.Add(Controls(full: false));
        meta.Children.Add(bottom);
        root.Children.Add(meta);
        return root;
    }

    private FrameworkElement Pill()
    {
        var root = Card(radius: 29);
        _art = Art(left: 9, top: 9, size: 40, outBy: 0, arm: false, root, sleeve: false);
        var words = WidgetWords(13, 11, album: false);
        words.Margin = new Thickness(58, 11, 60, 0);
        root.Children.Add(words);
        var play = PlayButton(40);
        play.HorizontalAlignment = HorizontalAlignment.Right;
        play.VerticalAlignment = VerticalAlignment.Center;
        play.Margin = new Thickness(0, 0, 9, 0);
        root.Children.Add(play);
        return root;
    }

    // ---------- parts ----------

    /// <summary>The glass card with the cover's colours behind it: the cover decoded tiny and scaled up smooth is the blur.</summary>
    private Grid Card(double radius)
    {
        var root = new Grid { ClipToBounds = true };
        _glow = new Image { Stretch = Stretch.UniformToFill, Opacity = 0.85, Margin = new Thickness(-30) };
        RenderOptions.SetBitmapScalingMode(_glow, BitmapScalingMode.HighQuality);
        var shade = new Border
        {
            Background = new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb(0x14, 8, 8, 14), 0), new(Color.FromArgb(0x8C, 8, 8, 14), 0.55), new(Color.FromArgb(0xB8, 8, 8, 14), 1) }, 0),
        };
        root.Children.Add(_glow);
        root.Children.Add(shade);
        root.Clip = new RectangleGeometry { RadiusX = radius, RadiusY = radius };
        root.SizeChanged += (_, size) => ((RectangleGeometry)root.Clip).Rect = new Rect(size.NewSize);
        var edge = new Border { CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), IsHitTestVisible = false };
        root.Children.Add(edge);
        return root;
    }

    /// <summary>The record (grooves, the cover as its label, a fixed sheen) under the cover sleeve; the tonearm on Large.</summary>
    private FrameworkElement Art(double left, double top, double size, double outBy, bool arm, Grid root, bool sleeve = true)
    {
        _recordOut = outBy;
        var canvas = new Canvas { IsHitTestVisible = true, Cursor = Cursors.Hand, Background = Brushes.Transparent, Width = left + size + outBy + 4, Height = top + size + 4, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var record = new Grid { Width = size - 2, Height = size - 2, RenderTransform = _out };
        var disc = new Grid { RenderTransform = _spin, RenderTransformOrigin = new Point(0.5, 0.5) };
        disc.Children.Add(new Ellipse { Fill = Grooves });
        disc.Children.Add(new Ellipse { Width = size * 0.34, Height = size * 0.34, Fill = Placeholder }); // a label until a cover comes
        _label = new Image { Stretch = Stretch.UniformToFill, Margin = new Thickness(size * 0.33), Clip = new EllipseGeometry(new Point(size * 0.17 - 1, size * 0.17 - 1), size * 0.17 - 1, size * 0.17 - 1) };
        disc.Children.Add(_label);
        disc.Children.Add(new Ellipse { Width = size * 0.08, Height = size * 0.08, Fill = new SolidColorBrush(Color.FromRgb(0xCF, 0xD3, 0xDA)) });
        record.Children.Add(disc);
        record.Children.Add(new Ellipse { Fill = Sheen, IsHitTestVisible = false });
        Canvas.SetLeft(record, left + 1);
        Canvas.SetTop(record, top + 1);
        canvas.Children.Add(record);
        if (sleeve)
        {
            _sleeve = new Image { Width = size, Height = size, Stretch = Stretch.UniformToFill, Clip = new RectangleGeometry(new Rect(0, 0, size, size), 7, 7) };
            var shadow = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)), RenderTransform = new TranslateTransform(5, 4) };
            // No cover (a player without one, nothing playing): a soft sleeve with a note, so the record never sits bare.
            var blank = new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(7), Background = Placeholder,
                Child = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = size * 0.32, Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            foreach (var (part, x) in new (UIElement, double)[] { (shadow, left), (blank, left), (_sleeve, left) })
            {
                Canvas.SetLeft(part, x);
                Canvas.SetTop(part, top);
                canvas.Children.Add(part);
            }
        }
        else _sleeve = null;
        if (arm)
        {
            var tonearm = new Path
            {
                Data = Geometry.Parse("M 9,9 L 9,112 Q 9,122 14,128 M 9,124 h 12 v 16 h -12 z"), Stroke = new SolidColorBrush(Color.FromRgb(0xCF, 0xD3, 0xDA)), StrokeThickness = 3,
                StrokeLineJoin = PenLineJoin.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransform = _arm, RenderTransformOrigin = new Point(0, 0),
            };
            _arm.CenterX = 9;
            _arm.CenterY = 9;
            var pivot = new Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x35)), Stroke = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xAA)), StrokeThickness = 1.5 };
            Canvas.SetLeft(tonearm, 209);
            Canvas.SetTop(tonearm, 16);
            Canvas.SetLeft(pivot, 210);
            Canvas.SetTop(pivot, 17);
            canvas.Children.Add(tonearm);
            canvas.Children.Add(pivot);
        }
        canvas.MouseLeftButtonUp += (_, click) => // the cover brings the player to the front
        {
            click.Handled = true;
            if (_view?.Media?.Shown is { } shown) Window?.RequestMedia(shown.AppId, command: null);
        };
        root.Children.Add(canvas);
        return canvas;
    }

    private StackPanel WidgetWords(double titleSize, double artistSize, bool album)
    {
        _title = Text(titleSize, FontWeights.SemiBold);
        _artist = Text(artistSize, FontWeights.Normal, 0.78);
        _album = album ? Text(11, FontWeights.Normal, 0.5) : null;
        var words = new StackPanel();
        words.Children.Add(_title);
        words.Children.Add(_artist);
        if (_album is not null) words.Children.Add(_album);
        return words;
    }

    private FrameworkElement Scrub()
    {
        _scrubFill = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
        _scrubTrack = new Border { Height = 4, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)), Child = _scrubFill };
        var hit = new Grid { Height = 14, Background = Brushes.Transparent, Cursor = Cursors.Hand, Children = { _scrubTrack } };
        hit.MouseEnter += (_, _) => _scrubTrack.Height = 6;
        hit.MouseLeave += (_, _) => _scrubTrack.Height = 4;
        hit.MouseLeftButtonUp += (_, click) =>
        {
            click.Handled = true;
            if (_view?.Media?.Shown is not { CanSeek: true, End: { } end } shown) return;
            var fraction = Math.Clamp(click.GetPosition(hit).X / Math.Max(1, hit.ActualWidth), 0, 1);
            Window?.RequestMedia(shown.AppId, MediaCommand.Seek, end * fraction);
        };
        return hit;
    }

    private FrameworkElement Times()
    {
        _elapsed = Text(10, FontWeights.SemiBold, 0.6);
        _left = Text(10, FontWeights.SemiBold, 0.6);
        _live = Text(10, FontWeights.Bold, 1);
        _live.Text = "● LIVE";
        _live.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
        var row = new DockPanel();
        DockPanel.SetDock(_left, Dock.Right);
        row.Children.Add(_left);
        row.Children.Add(_elapsed);
        row.Children.Add(_live);
        return row;
    }

    private FrameworkElement Controls(bool full)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = full ? HorizontalAlignment.Stretch : HorizontalAlignment.Center };
        if (full) { _shuffle = Glyph("", MediaCommand.Shuffle, small: true); buttons.Children.Add(_shuffle); }
        buttons.Children.Add(Glyph("", MediaCommand.Previous));
        buttons.Children.Add(PlayButton(full ? 42 : 36));
        buttons.Children.Add(Glyph("", MediaCommand.Next));
        if (full) { _repeat = Glyph("", MediaCommand.Repeat, small: true); buttons.Children.Add(_repeat); }
        row.Children.Add(buttons);
        return row;
    }

    private Button Glyph(string glyph, MediaCommand command, bool small = false)
    {
        var button = RoundButton(30);
        button.Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = small ? 13 : 16, Foreground = Brushes.White, Opacity = small ? 0.6 : 0.9 };
        button.Margin = new Thickness(small ? 2 : 4, 0, small ? 2 : 4, 0);
        System.Windows.Automation.AutomationProperties.SetName(button, command.ToString()); // Shuffle, Previous, Next, Repeat: for screen readers
        button.ToolTip = command.ToString();
        button.Click += (_, _) => { if (_view?.Media?.Shown is { } shown) Window?.RequestMedia(shown.AppId, command); };
        return button;
    }

    private Button PlayButton(double size)
    {
        var button = RoundButton(size);
        button.Background = Brushes.White;
        System.Windows.Automation.AutomationProperties.SetName(button, "Play or pause");
        _playIcon = new Path { Fill = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x18)), Width = size * 0.42, Height = size * 0.42, Stretch = Stretch.Uniform };
        button.Content = _playIcon;
        button.Click += (_, _) =>
        {
            if (_view?.Media?.Shown is { } shown) Window?.RequestMedia(shown.AppId, MediaCommand.PlayPause);
        };
        return button;
    }

    private static Button RoundButton(double size)
    {
        var button = new Button { Width = size, Height = size, Cursor = Cursors.Hand, Background = Brushes.Transparent, Focusable = false };
        button.SetResourceReference(StyleProperty, "WidgetRoundButton");
        return button;
    }

    private static TextBlock Text(double size, FontWeight weight, double opacity = 1)
    {
        var text = new TextBlock { FontSize = size, FontWeight = weight, Opacity = opacity, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
        text.SetResourceReference(TextBlock.FontFamilyProperty, size >= 15 ? "WidgetDisplayFont" : "WidgetTextFont");
        return text;
    }

    private static readonly Geometry PlayShape = Freeze(Geometry.Parse("M 2,0 L 14,8 L 2,16 Z"));
    private static readonly Geometry PauseShape = Freeze(Geometry.Parse("M 1,0 H 6 V 16 H 1 Z M 10,0 H 15 V 16 H 10 Z"));

    private static readonly Brush Grooves = Freeze(new RadialGradientBrush(new GradientStopCollection(
        Enumerable.Range(0, 24).Select(ring => new GradientStop(ring % 2 == 0 ? Color.FromRgb(0x10, 0x10, 0x15) : Color.FromRgb(0x1C, 0x1C, 0x23), 0.3 + ring * 0.029)))));

    private static readonly Brush Placeholder = Freeze(new LinearGradientBrush(Color.FromRgb(0x5A, 0x3F, 0xB8), Color.FromRgb(0xD1, 0x4A, 0x8E), 45));

    private static readonly Brush Sheen = Freeze(new LinearGradientBrush(new GradientStopCollection
    {
        new(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0.3), new(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 0.45), new(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0.6),
    }, new Point(0, 0), new Point(1, 1)));

    private static T Freeze<T>(T frozen) where T : Freezable
    {
        frozen.Freeze();
        return frozen;
    }

    // ---------- showing the state ----------

    private void Show()
    {
        if (_view is not { } view || _title is null) return;
        var media = view.Media;
        var face = media?.Face ?? NowPlaying.Face(current: null, last: null);
        var shown = face.Idle ? null : media?.Shown;
        _title.Text = face.Title;
        _artist!.Text = face.Subtitle;
        if (_album is not null) _album.Text = face.Idle ? "" : face.Track?.Album ?? "";
        var cover = media?.Cover;
        foreach (var image in new[] { _sleeve, _label }) if (image is not null && !ReferenceEquals(image.Source, cover)) image.Source = cover;
        if (_glow is not null && !ReferenceEquals(_glow.Source, media?.CoverGlow)) _glow.Source = media?.CoverGlow;
        if (_art is not null) _art.Opacity = face.Idle ? 0.55 : 1;
        var playing = shown?.Playing == true;
        if (_playIcon is not null) _playIcon.Data = playing ? PauseShape : PlayShape;
        if (shown is { } session && _scrubFill is not null && _scrubTrack is not null)
        {
            var position = NowPlaying.LivePosition(session.Position, session.Updated, DateTimeOffset.Now, session.Playing, session.End);
            var (elapsed, left, live) = NowPlaying.Timeline(position, session.End);
            _scrubFill.Width = live || session.End is not { } end ? _scrubTrack.ActualWidth : _scrubTrack.ActualWidth * Math.Clamp(position / end, 0, 1);
            if (_elapsed is not null) (_elapsed.Text, _left!.Text, _live!.Visibility) = (elapsed, left, live ? Visibility.Visible : Visibility.Collapsed);
        }
        else if (_scrubFill is not null)
        {
            _scrubFill.Width = 0;
            if (_elapsed is not null) (_elapsed.Text, _left!.Text, _live!.Visibility) = ("", "", Visibility.Collapsed);
        }
        if (_shuffle is not null) _shuffle.Visibility = shown?.Shuffle is null ? Visibility.Hidden : Visibility.Visible;
        if (_shuffle?.Content is TextBlock shuffleGlyph) shuffleGlyph.Opacity = shown?.Shuffle == true ? 1 : 0.5;
        if (_repeat is not null) _repeat.Visibility = shown?.Repeat is null ? Visibility.Hidden : Visibility.Visible;
        if (_repeat?.Content is TextBlock repeatGlyph) (repeatGlyph.Text, repeatGlyph.Opacity) = shown?.Repeat == MediaRepeat.Track ? ("", 1) : ("", shown?.Repeat == MediaRepeat.List ? 1 : 0.5);
        ShowPlayers(media);
        _playing = playing;
        Spin(playing);
    }

    /// <summary>
    /// Final review C2: the window says when nothing may move — motion off (a game, safe mode, Windows' setting), the fence
    /// hidden or rolled up. The widget's own tick stops then, so the window tells the card directly.
    /// </summary>
    public void Hold(bool held)
    {
        _held = held;
        Spin(_playing);
    }

    /// <summary>Small app pills when several players have a session; the one shown is lit.</summary>
    private void ShowPlayers(NowPlayingData? media)
    {
        if (_players is null) return;
        var sessions = media?.Sessions ?? [];
        var key = string.Join("|", sessions.Select(session => session.AppId)) + "#" + media?.Shown?.AppId;
        if (Equals(_players.Tag, key)) return;
        _players.Tag = key;
        _players.Children.Clear();
        if (sessions.Count < 2) return;
        foreach (var session in sessions)
        {
            var pill = new Button { Content = session.Track.App, Margin = new Thickness(0, 0, 5, 0), Focusable = false, Cursor = Cursors.Hand, FontSize = 10.5 };
            pill.SetResourceReference(StyleProperty, "WidgetPill");
            pill.Opacity = session.AppId == media?.Shown?.AppId ? 1 : 0.6;
            pill.Click += (_, _) => Window?.ShowMediaPlayer(_view!.Key, session.AppId);
            _players.Children.Add(pill);
        }
    }

    /// <summary>Playing (and motion on): the record slides out, spins, the arm lowers. Else it spins down, the arm lifts, it slides home.</summary>
    private void Spin(bool playing)
    {
        var moving = playing && Motion != Motion.Off && IsLoaded && !_held;
        if (_spinning == moving) return;
        _spinning = moving;
        var duration = new Duration(TimeSpan.FromMilliseconds(Motion == Motion.Off ? 1 : 700));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        if (moving)
        {
            var spin = new DoubleAnimation(_spin.Angle % 360, _spin.Angle % 360 + 360, TimeSpan.FromSeconds(SpinSeconds)) { RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(spin, 30);
            _spin.BeginAnimation(RotateTransform.AngleProperty, spin);
            _out.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(_recordOut, duration) { EasingFunction = ease });
            _arm.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(21, duration) { BeginTime = TimeSpan.FromMilliseconds(450), EasingFunction = ease });
            return;
        }
        var angle = _spin.Angle;
        var stop = new DoubleAnimation(angle, angle + (Motion == Motion.Off ? 0 : 80), new Duration(TimeSpan.FromMilliseconds(800))) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Timeline.SetDesiredFrameRate(stop, 30);
        _spin.BeginAnimation(RotateTransform.AngleProperty, stop);
        _arm.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-4, duration) { EasingFunction = ease });
        _out.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(playing ? _recordOut : 0, duration) { BeginTime = TimeSpan.FromMilliseconds(350), EasingFunction = ease });
    }
}
