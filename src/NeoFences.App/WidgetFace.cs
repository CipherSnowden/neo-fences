using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using NeoFences.Core.Items;

namespace NeoFences.App;

/// <summary>
/// A widget's face (M41a, spec 2026-10-11-m41a-feel-design §6.1–6.2): one glass card per widget, in the widget's size —
/// Small, Medium or Large, each its own layout (a bigger one shows more). Built in code for its kind and size, scaled into
/// its cells by a Viewbox; its texts and bars change in place every tick. Now playing has its own card (NowPlayingCard).
/// </summary>
public sealed class WidgetFace : Grid
{
    private FenceItemView? _view;
    private (WidgetKind? Kind, WidgetSize Size) _built = (null, WidgetSize.Small);
    private Action? _refresh;
    private int _monthOffset; // the calendar's ‹ ›: months away from today's

    public WidgetFace()
    {
        DataContextChanged += (_, change) =>
        {
            if (change.OldValue is FenceItemView old) old.PropertyChanged -= OnViewChanged;
            _view = change.NewValue as FenceItemView;
            if (_view is not null) _view.PropertyChanged += OnViewChanged;
            Build();
        };
    }

    private void OnViewChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName is nameof(FenceItemView.Widget) or nameof(FenceItemView.WidgetSize)) Build();
        else if (change.PropertyName is nameof(FenceItemView.WidgetNow) or nameof(FenceItemView.StatTiles) or nameof(FenceItemView.Options)) _refresh?.Invoke();
    }

    private void Build()
    {
        // Final review I3: the card follows its own size changes; it is made once per item and let go of when replaced.
        if (_view?.Widget == WidgetKind.NowPlaying && Children.OfType<NowPlayingCard>().FirstOrDefault() is { } existing && ReferenceEquals(existing.DataContext, _view)) return;
        if (_view?.Widget is not { } kind || _view.Widget == WidgetKind.NowPlaying)
        {
            Clear();
            _refresh = null;
            _built = (null, WidgetSize.Small);
            if (_view?.Widget != WidgetKind.NowPlaying) return;
            Children.Add(new NowPlayingCard { DataContext = _view });
            AddHandles();
            return;
        }
        if (_built == (kind, _view.WidgetSize) && Children.Count > 0) return;
        _built = (kind, _view.WidgetSize);
        Clear();
        var (content, nominal) = kind switch
        {
            WidgetKind.Clock => Clock(_view.WidgetSize),
            WidgetKind.Stats => Stats(_view.WidgetSize),
            _ => Calendar(_view.WidgetSize),
        };
        content.Width = nominal.Width;
        content.Height = nominal.Height;
        var card = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(2) };
        card.SetResourceReference(Border.BackgroundProperty, "WidgetCard");
        card.SetResourceReference(Border.BorderBrushProperty, "WidgetCardEdge");
        card.Child = new Viewbox { Stretch = Stretch.Uniform, Child = content };
        Children.Add(card);
        AddHandles();
        _refresh?.Invoke();
    }

    // ---------- the handles: ⚙ flips the widget to its settings, the corner snaps it to Small, Medium or Large ----------

    private void AddHandles()
    {
        if (!IsHitTestVisible) return; // a gallery preview
        var gear = new Button
        {
            Width = 24, Height = 22, Margin = new Thickness(0, 6, 6, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Focusable = false, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Widget settings", Visibility = Visibility.Collapsed,
            Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12 },
        };
        ((TextBlock)gear.Content).SetResourceReference(TextBlock.ForegroundProperty, "FenceText");
        gear.SetResourceReference(StyleProperty, "WidgetArrow");
        System.Windows.Automation.AutomationProperties.SetName(gear, "Widget settings");
        gear.Click += (_, _) => { if (_view is { } view) Owner?.FlipWidget(view.Key); };
        var grip = new System.Windows.Controls.Primitives.Thumb
        {
            Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 3, 3),
            Cursor = System.Windows.Input.Cursors.SizeNWSE, Visibility = Visibility.Collapsed, Template = GripTemplate,
        };
        var label = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 3, 10, 4), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        label.SetResourceReference(Border.BackgroundProperty, "HoverLabelBackground");
        var labelText = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold };
        labelText.SetResourceReference(TextBlock.ForegroundProperty, "FenceText");
        label.Child = labelText;
        Size start = default;
        var (dragX, dragY) = (0.0, 0.0);
        WidgetSize? snapped = null;
        grip.DragStarted += (_, _) => { start = new Size(ActualWidth, ActualHeight); (dragX, dragY) = (0, 0); };
        grip.DragDelta += (_, drag) =>
        {
            if (Owner is not { } window) return;
            (dragX, dragY) = (dragX + drag.HorizontalChange, dragY + drag.VerticalChange);
            var cell = window.CellSize;
            snapped = WidgetSizes.Snap((start.Width + dragX) / cell.Width, (start.Height + dragY) / cell.Height);
            labelText.Text = snapped.ToString();
            label.Visibility = Visibility.Visible;
        };
        grip.DragCompleted += (_, _) =>
        {
            label.Visibility = Visibility.Collapsed;
            if (snapped is { } size && _view is { } view && size != view.ChosenWidgetSize) Owner?.ChooseWidgetSize(view.Key, size); // rc.5: the chosen size, not the one that fits
            snapped = null;
        };
        MouseEnter += (_, _) => gear.Visibility = grip.Visibility = Visibility.Visible;
        MouseLeave += (_, _) => { if (!grip.IsDragging) gear.Visibility = grip.Visibility = Visibility.Collapsed; };
        Children.Add(gear);
        Children.Add(grip);
        Children.Add(label);
    }

    private FenceWindow? Owner => Window.GetWindow(this) as FenceWindow;

    /// <summary>The corner grip: three short diagonal lines, like a window's size grip.</summary>
    private static readonly ControlTemplate GripTemplate = Grip();

    private static ControlTemplate Grip()
    {
        var path = new FrameworkElementFactory(typeof(Path));
        path.SetValue(Path.DataProperty, Geometry.Parse("M 14,4 L 4,14 M 14,8 L 8,14 M 14,12 L 12,14"));
        path.SetResourceReference(Shape.StrokeProperty, "FenceSubtleText");
        path.SetValue(Shape.StrokeThicknessProperty, 1.2);
        var back = new FrameworkElementFactory(typeof(Border));
        back.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        back.AppendChild(path);
        var template = new ControlTemplate(typeof(System.Windows.Controls.Primitives.Thumb)) { VisualTree = back };
        template.Seal();
        return template;
    }

    /// <summary>Empties the face; a Now playing card is let go of its item first (it listens to it).</summary>
    private void Clear()
    {
        foreach (var card in Children.OfType<NowPlayingCard>()) card.DataContext = null;
        Children.Clear();
    }

    // ---------- pieces ----------

    private static TextBlock Text(double size, FontWeight weight, double opacity = 1, bool display = false, bool caps = false)
    {
        var text = new TextBlock { FontSize = size, FontWeight = weight, Opacity = opacity, TextTrimming = TextTrimming.CharacterEllipsis };
        text.SetResourceReference(TextBlock.ForegroundProperty, "FenceText");
        text.SetResourceReference(TextBlock.FontFamilyProperty, display ? "WidgetDisplayFont" : "WidgetTextFont");
        if (display) Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
        if (caps) Typography.SetCapitals(text, FontCapitals.AllSmallCaps);
        return text;
    }

    private static StackPanel Stack(params UIElement[] children)
    {
        var stack = new StackPanel();
        foreach (var child in children) stack.Children.Add(child);
        return stack;
    }

    private static Grid Columns(params GridLength[] widths)
    {
        var grid = new Grid();
        foreach (var width in widths) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        return grid;
    }

    private static T At<T>(T element, int column = 0, int row = 0, int columnSpan = 1) where T : UIElement
    {
        SetColumn(element, column);
        SetRow(element, row);
        SetColumnSpan(element, columnSpan);
        return element;
    }

    private static readonly GridLength Star = new(1, GridUnitType.Star);

    // ---------- clock ----------

    private (FrameworkElement, Size) Clock(WidgetSize size)
    {
        var time = Text(size switch { WidgetSize.Small => 30, WidgetSize.Medium => 44, _ => 54 }, FontWeights.SemiBold, display: true);
        var seconds = Text(18, FontWeights.SemiBold, opacity: 0.55, display: true);
        var weekday = Text(size == WidgetSize.Small ? 11 : 13, FontWeights.SemiBold, opacity: 0.8);
        var date = Text(12, FontWeights.Normal, opacity: 0.65);
        var timeRow = new StackPanel { Orientation = Orientation.Horizontal };
        timeRow.Children.Add(time);
        if (size == WidgetSize.Large) timeRow.Children.Add(seconds);
        seconds.VerticalAlignment = VerticalAlignment.Bottom;
        seconds.Margin = new Thickness(4, 0, 0, 7);
        var left = Stack(timeRow, weekday, date);
        left.VerticalAlignment = VerticalAlignment.Center;
        left.Margin = new Thickness(12, 0, 8, 0);
        var cities = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        var shownCities = "";
        _refresh = () =>
        {
            if (_view is not { } view) return;
            var culture = CultureInfo.CurrentCulture;
            var now = view.WidgetNow;
            time.Text = Widgets.ClockText(now, seconds: false, culture);
            seconds.Text = now.ToString("ss", culture);
            weekday.Text = culture.DateTimeFormat.GetDayName(now.DayOfWeek);
            date.Text = size == WidgetSize.Small ? "" : now.ToString(culture.DateTimeFormat.MonthDayPattern, culture);
            date.Visibility = size == WidgetSize.Small ? Visibility.Collapsed : Visibility.Visible;
            if (size != WidgetSize.Large) return;
            var shown = (view.Options.Cities ?? []).Select(id => WorldClocks.Show(id, new DateTimeOffset(now), culture)).ToList();
            var key = string.Join("|", shown.Select(city => $"{city.Name} {city.Time}"));
            if (key == shownCities) return; // M41b (rc.3 leftover): rebuilt when a city's time changes (once a minute), not every second
            shownCities = key;
            cities.Children.Clear();
            foreach (var (name, at) in shown)
            {
                cities.Children.Add(Text(10, FontWeights.SemiBold, opacity: 0.65, caps: true).With(name));
                cities.Children.Add(Text(19, FontWeights.SemiBold, display: true).With(at, margin: new Thickness(0, 0, 0, 6)));
            }
        };
        if (size != WidgetSize.Large) return (left, size == WidgetSize.Small ? new Size(160, 66) : new Size(160, 150));
        var grid = Columns(Star, GridLength.Auto);
        grid.Children.Add(At(left));
        grid.Children.Add(At(cities, column: 1));
        return (grid, new Size(330, 150));
    }

    // ---------- system stats ----------

    private (FrameworkElement, Size) Stats(WidgetSize size)
    {
        int[] shown = size switch { WidgetSize.Small => [0, 2], WidgetSize.Medium => [0, 1, 2, 3], _ => [0, 1, 2, 3, 4] };
        var columns = size == WidgetSize.Large ? 3 : 2;
        var grid = new Grid { Margin = new Thickness(6) };
        for (var column = 0; column < columns; column++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        var rows = size == WidgetSize.Small ? 1 : 2;
        for (var row = 0; row < rows; row++) grid.RowDefinitions.Add(new RowDefinition());
        var tiles = new List<(TextBlock Label, TextBlock Value, TextBlock Unit, ScaleTransform Bar, Border Track)>();
        for (var index = 0; index < shown.Length; index++)
        {
            var label = Text(9.5, FontWeights.SemiBold, opacity: 0.65, caps: true);
            var value = Text(19, FontWeights.SemiBold, display: true);
            var unit = Text(10, FontWeights.SemiBold, opacity: 0.6);
            unit.VerticalAlignment = VerticalAlignment.Bottom;
            unit.Margin = new Thickness(2, 0, 0, 3);
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(value);
            line.Children.Add(unit);
            var bar = new ScaleTransform(0, 1);
            var warm = shown[index] is 1 or 3; // temperatures: a warm bar
            var fill = new Border { CornerRadius = new CornerRadius(2.5), RenderTransform = bar, RenderTransformOrigin = new Point(0, 0.5) };
            if (warm) fill.Background = WarmBar;
            else fill.SetResourceReference(Border.BackgroundProperty, shown[index] == 4 ? "WidgetBarRam" : "WidgetBar");
            var track = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), Margin = new Thickness(0, 5, 0, 0), Child = fill };
            track.SetResourceReference(Border.BackgroundProperty, "FenceHover");
            var tile = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 6, 9, 8), Margin = new Thickness(3), Child = Stack(label, line, track) };
            tile.SetResourceReference(Border.BackgroundProperty, "FenceHover");
            var (column, row) = (index % columns, index / columns);
            grid.Children.Add(At(tile, column, row, columnSpan: size == WidgetSize.Large && index == 4 ? 2 : 1));
            tiles.Add((label, value, unit, bar, track));
        }
        _refresh = () =>
        {
            if (_view is not { StatTiles: { Count: >= 5 } all }) return;
            for (var index = 0; index < shown.Length; index++)
            {
                var stat = all[shown[index]];
                var (label, value, unit, bar, track) = tiles[index];
                label.Text = stat.Label;
                value.Text = stat.Value;
                unit.Text = stat.Unit;
                bar.ScaleX = Math.Clamp(stat.Percent / 100, 0, 1);
                track.Visibility = stat.Value == "—" ? Visibility.Hidden : Visibility.Visible;
            }
        };
        return (grid, size switch { WidgetSize.Small => new Size(170, 66), WidgetSize.Medium => new Size(170, 150), _ => new Size(330, 150) });
    }

    private static readonly Brush WarmBar = Freeze(new LinearGradientBrush(Color.FromRgb(0xFF, 0xB3, 0x47), Color.FromRgb(0xFF, 0x6A, 0x3D), 0));

    private static T Freeze<T>(T brush) where T : Freezable
    {
        brush.Freeze();
        return brush;
    }

    // ---------- calendar ----------

    private (FrameworkElement, Size) Calendar(WidgetSize size)
    {
        if (size == WidgetSize.Small) return (CalendarSmall(), new Size(160, 66));
        var month = MonthGrid(out var refreshMonth);
        if (size == WidgetSize.Medium)
        {
            month.Margin = new Thickness(10, 6, 10, 6);
            _refresh = refreshMonth;
            return (month, new Size(170, 160));
        }
        var day = Text(46, FontWeights.SemiBold, display: true);
        var weekday = Text(15, FontWeights.SemiBold);
        var line = Text(11.5, FontWeights.Normal, opacity: 0.65);
        var today = Stack(Text(9.5, FontWeights.SemiBold, opacity: 0.65, caps: true).With("Today"), day, weekday, line);
        today.Margin = new Thickness(14, 10, 8, 10);
        today.VerticalAlignment = VerticalAlignment.Center;
        var grid = Columns(new GridLength(120), Star);
        grid.Children.Add(At(today));
        month.Margin = new Thickness(4, 8, 12, 8);
        grid.Children.Add(At(month, column: 1));
        _refresh = () =>
        {
            refreshMonth();
            if (_view is not { } view) return;
            var culture = CultureInfo.CurrentCulture;
            var now = view.WidgetNow;
            day.Text = now.Day.ToString(culture);
            weekday.Text = culture.DateTimeFormat.GetDayName(now.DayOfWeek);
            line.Text = $"{now.ToString(culture.DateTimeFormat.YearMonthPattern, culture)} · week {CalendarMonth.WeekOf(DateOnly.FromDateTime(now))}";
        };
        return (grid, new Size(340, 160));
    }

    /// <summary>The old Date widget's page as the Calendar's Small size: a tear-off date tile beside the weekday and month.</summary>
    private FrameworkElement CalendarSmall()
    {
        var band = Text(9, FontWeights.Bold).With("", align: TextAlignment.Center);
        band.Foreground = Brushes.White;
        var bandBox = new Border { CornerRadius = new CornerRadius(8, 8, 0, 0), Padding = new Thickness(0, 2, 0, 2), Child = band };
        bandBox.SetResourceReference(Border.BackgroundProperty, "WidgetBar");
        var day = Text(23, FontWeights.SemiBold, display: true).With("", align: TextAlignment.Center);
        var tile = new Border { Width = 46, Height = 52, CornerRadius = new CornerRadius(8), Child = Stack(bandBox, day), VerticalAlignment = VerticalAlignment.Center };
        tile.SetResourceReference(Border.BackgroundProperty, "FenceHover");
        var weekday = Text(16, FontWeights.SemiBold);
        var month = Text(11.5, FontWeights.Normal, opacity: 0.65);
        var words = Stack(weekday, month);
        words.VerticalAlignment = VerticalAlignment.Center;
        words.Margin = new Thickness(10, 0, 0, 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 8, 0) };
        row.Children.Add(tile);
        row.Children.Add(words);
        _refresh = () =>
        {
            if (_view is not { } view) return;
            var culture = CultureInfo.CurrentCulture;
            var now = view.WidgetNow;
            band.Text = culture.DateTimeFormat.GetAbbreviatedMonthName(now.Month).ToUpper(culture);
            day.Text = now.Day.ToString(culture);
            weekday.Text = culture.DateTimeFormat.GetDayName(now.DayOfWeek);
            month.Text = now.ToString(culture.DateTimeFormat.YearMonthPattern, culture);
        };
        return row;
    }

    /// <summary>The month with ‹ › to the months around it; today in the accent, weekends tinted, days of the weeks around dimmed.</summary>
    private FrameworkElement MonthGrid(out Action refresh)
    {
        var title = Text(12.5, FontWeights.SemiBold);
        Button Arrow(string glyph, int step)
        {
            var arrow = new Button { Content = glyph, Width = 22, Height = 20, Padding = new Thickness(0), FontSize = 13, Cursor = System.Windows.Input.Cursors.Hand };
            arrow.SetResourceReference(StyleProperty, "WidgetArrow");
            arrow.Click += (_, _) => { _monthOffset += step; _refresh?.Invoke(); };
            return arrow;
        }
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var arrows = new StackPanel { Orientation = Orientation.Horizontal };
        arrows.Children.Add(Arrow("‹", -1));
        arrows.Children.Add(Arrow("›", 1));
        DockPanel.SetDock(arrows, Dock.Right);
        header.Children.Add(arrows);
        header.Children.Add(title);
        var days = new UniformGrid7();
        var panel = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(days);
        (DateOnly Today, DateOnly Month, string Culture)? shownKey = null;
        refresh = () =>
        {
            if (_view is not { } view) return;
            var culture = CultureInfo.CurrentCulture;
            var today = DateOnly.FromDateTime(view.WidgetNow);
            var shownMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(_monthOffset);
            if (shownKey == (today, shownMonth, culture.Name)) return; // M41b (rc.3 leftover): rebuilt on a new day or month, not every second
            shownKey = (today, shownMonth, culture.Name);
            title.Text = shownMonth.ToDateTime(TimeOnly.MinValue).ToString(culture.DateTimeFormat.YearMonthPattern, culture);
            var firstDay = culture.DateTimeFormat.FirstDayOfWeek;
            days.Children.Clear();
            foreach (var letter in CalendarMonth.Headers(culture, firstDay)) days.Children.Add(Text(8.5, FontWeights.Bold, opacity: 0.55).With(letter, align: TextAlignment.Center));
            foreach (var day in CalendarMonth.Build(shownMonth, firstDay, today).Days)
            {
                var number = Text(10.5, day.Today ? FontWeights.Bold : FontWeights.Normal, opacity: !day.InMonth ? 0.32 : day.Weekend ? 0.7 : 0.92)
                    .With(day.Day.ToString(culture), align: TextAlignment.Center);
                if (!day.Today) { days.Children.Add(number); continue; }
                number.Foreground = Brushes.White;
                var mark = new Border { CornerRadius = new CornerRadius(6), Child = number, Margin = new Thickness(1, 0, 1, 0) };
                mark.SetResourceReference(Border.BackgroundProperty, "WidgetBar");
                days.Children.Add(mark);
            }
        };
        return panel;
    }

    /// <summary>Seven equal columns, as many rows as the month needs.</summary>
    private sealed class UniformGrid7 : System.Windows.Controls.Primitives.UniformGrid
    {
        public UniformGrid7() => Columns = 7;
    }
}

/// <summary>Small fluent helpers for the faces' text.</summary>
internal static class WidgetText
{
    public static TextBlock With(this TextBlock text, string value, TextAlignment align = TextAlignment.Left, Thickness? margin = null)
    {
        text.Text = value;
        text.TextAlignment = align;
        if (margin is { } room) text.Margin = room;
        return text;
    }
}
