using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NeoFences.Core.Items;
using NeoFences.Core.Model;

namespace NeoFences.App;

/// <summary>
/// M41a (spec 2026-10-11-m41a-feel-design §6.3–6.4): adding widgets from a gallery that opens from the fence (the "+" on the
/// title, the menu's Widget…), and setting one up on its back — the widget flips over to its options and its size, Done
/// flips it back. Both are NeoFences' own popups in the menus' colours (MenuTheme), mouse-only (a fence never takes the
/// keyboard outside Peek: activating it would break Win+D, rc.2). The gallery is drawn in the fence; the back is a popup.
/// </summary>
public partial class FenceWindow
{
    /// <summary>A widget's back changed its options (the item's key, the options).</summary>
    public event Action<string, WidgetOptions>? WidgetOptionsChanged;

    /// <summary>Small, Medium or Large chosen for a widget (its back, or a corner drag).</summary>
    public event Action<string, WidgetSize>? WidgetSizeChosen;

    /// <summary>The fence's grid cell in DIPs: a corner drag snaps by it.</summary>
    public Size CellSize => new((double)Resources["GridCellWidth"], (double)Resources["GridCellHeight"]);

    public void ChooseWidgetSize(string key, WidgetSize size) => WidgetSizeChosen?.Invoke(key, size);

    private Popup? _widgetBack;

    private static readonly Duration Flip = new(TimeSpan.FromMilliseconds(160));

    // ---------- the back ----------

    /// <summary>The widget flips over (squash through zero) to its settings; changes apply at once; Done flips it back.</summary>
    public void FlipWidget(string key)
    {
        _widgetBack?.SetValue(Popup.IsOpenProperty, false);
        if (_items.FirstOrDefault(view => view.Key == key && view.IsWidget) is not { Widget: { } kind } view
            || ItemList.ItemContainerGenerator.ContainerFromItem(view) is not ListBoxItem container) return;
        var face = FindChild<WidgetFace>(container);
        var squash = new ScaleTransform(1, 1);
        if (face is not null) { face.RenderTransformOrigin = new Point(0.5, 0.5); face.RenderTransform = squash; }
        var panel = BackPanel(view, kind, close: () => _widgetBack!.IsOpen = false);
        var flipIn = new ScaleTransform(0, 1);
        panel.RenderTransformOrigin = new Point(0.5, 0.5);
        panel.RenderTransform = flipIn;
        var popup = new Popup
        {
            PlacementTarget = container, Placement = PlacementMode.Relative, AllowsTransparency = true, StaysOpen = false,
            Child = panel, MinWidth = Math.Max(container.ActualWidth, 270),
        };
        popup.Closed += (_, _) =>
        {
            if (_motion == Motion.Off) squash.ScaleX = 1;
            else squash.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, Flip) { EasingFunction = new CubicEase() });
        };
        popup.Opened += (_, _) => RaisePopup(panel);
        _widgetBack = popup;
        if (_motion == Motion.Off)
        {
            squash.ScaleX = 0;
            flipIn.ScaleX = 1;
            popup.IsOpen = true;
            return;
        }
        var fold = new DoubleAnimation(1, 0, Flip) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fold.Completed += (_, _) =>
        {
            popup.IsOpen = true;
            flipIn.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, Flip) { EasingFunction = new CubicEase() });
        };
        squash.BeginAnimation(ScaleTransform.ScaleXProperty, fold);
    }

    private Border BackPanel(FenceItemView view, WidgetKind kind, Action close)
    {
        var options = view.Options;
        void Set(WidgetOptions changed)
        {
            options = changed;
            WidgetOptionsChanged?.Invoke(view.Key, changed);
        }
        var stack = new StackPanel();
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var done = Pill("Done", accent: true, () => close());
        DockPanel.SetDock(done, Dock.Right);
        header.Children.Add(done);
        header.Children.Add(MenuText(Widgets.NameOf(kind), 14, FontWeights.SemiBold));
        stack.Children.Add(header);
        // M41b (audit cards 3, 9): no Size row (gallery, corner drag and right-click → Size), no Seconds / Date line (the size decides).
        switch (kind)
        {
            case WidgetKind.Clock when view.ChosenWidgetSize != WidgetSize.Large: // rc.5: a Large clock in a narrow fence keeps its cities
                stack.Children.Add(Note("Small shows the time and the day, Medium adds the date. World clocks show on the Large clock — make it Large to pick them."));
                break;
            case WidgetKind.Clock:
                if (view.WidgetSize != WidgetSize.Large) stack.Children.Add(Note("This fence is too narrow for the Large clock — widen it to see the world clocks."));
                for (var index = 0; index < WorldClocks.Max; index++)
                {
                    var slot = index;
                    var box = new ComboBox { Width = 150, ItemsSource = WorldClocks.Cities.Select(city => city.Name).Prepend("None").ToList() };
                    var chosen = options.Cities?.ElementAtOrDefault(slot);
                    box.SelectedIndex = chosen is null ? 0 : Math.Max(0, WorldClocks.Cities.ToList().FindIndex(city => city.Id == chosen) + 1);
                    box.SelectionChanged += (_, _) =>
                    {
                        var cities = (options.Cities ?? []).ToList();
                        while (cities.Count <= slot) cities.Add("");
                        cities[slot] = box.SelectedIndex > 0 ? WorldClocks.Cities[box.SelectedIndex - 1].Id : "";
                        var kept = WorldClocks.Normalize(cities.Where(id => id.Length > 0));
                        Set(options with { Cities = kept.Count == 0 ? null : kept });
                    };
                    stack.Children.Add(Row(slot == 0 ? "World clock" : "Second city", box));
                }
                break;
            case WidgetKind.Stats:
                stack.Children.Add(Row("Temperatures in °F", Switch(options.Fahrenheit, on => Set(options with { Fahrenheit = on }))));
                break;
            case WidgetKind.Date:
                stack.Children.Add(Note("The week starts as in Windows' region settings. Small: today; Medium: the month; Large: both."));
                break;
            case WidgetKind.NowPlaying:
                stack.Children.Add(Note("Plays what Windows' media controls show (Spotify, a browser, Media Player). Several players? Switch with the pills on the Large size. The mouse wheel changes the volume."));
                break;
        }
        var card = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 14, 16, 14), BorderThickness = new Thickness(1), Child = stack, Margin = new Thickness(2, 2, 12, 14) };
        card.SetResourceReference(Border.BackgroundProperty, "MenuBackground");
        card.SetResourceReference(Border.BorderBrushProperty, "MenuBorder");
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Direction = 270, Opacity = 0.32 };
        return card;
    }

    private static TextBlock MenuText(string text, double size = 12.5, FontWeight? weight = null)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "MenuText");
        return block;
    }

    private static TextBlock Note(string text)
    {
        var note = MenuText(text, 11.5);
        note.Opacity = 0.7;
        note.Margin = new Thickness(0, 6, 0, 0);
        note.MaxWidth = 300;
        return note;
    }

    private static DockPanel Row(string label, UIElement control)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(control, Dock.Right);
        row.Children.Add(control);
        row.Children.Add(MenuText(label));
        return row;
    }

    /// <summary>A pill button (Done, a size, a gallery size) in the menus' colours; the accent for the chosen one.</summary>
    private static Button Pill(string text, bool accent, Action click)
    {
        var face = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 4, 11, 5), Child = MenuText(text, 12, FontWeights.SemiBold) };
        if (accent)
        {
            face.Background = SystemColorsAccent();
            ((TextBlock)face.Child).Foreground = Brushes.White;
        }
        else face.SetResourceReference(Border.BackgroundProperty, "MenuHover");
        var button = new Button { Content = face, Cursor = Cursors.Hand, Focusable = false, Margin = new Thickness(3, 0, 0, 0) };
        button.Template = BareButton;
        button.Click += (_, _) => click();
        return button;
    }

    private static Brush SystemColorsAccent()
    {
        var accent = new SolidColorBrush(SystemColors.AccentColor);
        accent.Freeze();
        return accent;
    }

    private static readonly ControlTemplate BareButton = Bare();

    private static ControlTemplate Bare()
    {
        var template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
        template.Seal();
        return template;
    }

    private static StackPanel Segmented(IReadOnlyList<(string Text, bool On, Action Click)> choices)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (text, on, click) in choices) row.Children.Add(Pill(text, on, click));
        return row;
    }

    /// <summary>On / Off as a pill that flips (no keyboard here, so a plain mouse switch).</summary>
    private static Button Switch(bool on, Action<bool> set)
    {
        var state = on;
        Button? button = null;
        button = Pill(state ? "On" : "Off", state, () =>
        {
            state = !state;
            set(state);
            if (button?.Content is Border face && face.Child is TextBlock label)
            {
                label.Text = state ? "On" : "Off";
                if (state) { face.Background = SystemColorsAccent(); label.Foreground = Brushes.White; }
                else { face.SetResourceReference(Border.BackgroundProperty, "MenuHover"); label.SetResourceReference(TextBlock.ForegroundProperty, "MenuText"); }
            }
        });
        return button;
    }

    // ---------- the gallery ----------

    /// <summary>
    /// The widget gallery (spec §6.3), drawn in the fence itself (a popup over a fence — never the active window — stayed
    /// invisible in the VM probe): each widget's live preview with Small / Medium / Large — a click adds it in that size at
    /// the end of the fence and closes the gallery; ✕ closes it. From the "+" on the title or the menu's Widget….
    /// ponytail: click to add (no dragging a widget onto another fence yet); a drag source if the owner misses it.
    /// </summary>
    public void OpenGallery()
    {
        if (_kind != FenceKind.Items) return;
        GalleryCards.Children.Clear();
        foreach (var kind in new[] { WidgetKind.Clock, WidgetKind.Date, WidgetKind.Stats, WidgetKind.NowPlaying })
        {
            var sample = new FenceItemView(new ShownItem($"gallery-{kind}", Widgets.Target(kind), Span: WidgetSizes.SpanOf(WidgetSize.Small)));
            sample.RenderWidget(DateTime.Now, System.Globalization.CultureInfo.CurrentCulture, stats: null);
            var preview = new WidgetFace { IsHitTestVisible = false, DataContext = sample, Width = 150, Height = 64 }; // not hit-testable first: no handles on a preview
            var sizes = Segmented([.. Enum.GetValues<WidgetSize>().Select(size => (size.ToString()[..1], false, (Action)(() =>
            {
                CloseGallery();
                AddWidgetRequested?.Invoke(kind, size);
            })))]);
            sizes.HorizontalAlignment = HorizontalAlignment.Center;
            sizes.Margin = new Thickness(0, 6, 0, 0);
            var name = new TextBlock { Text = Widgets.NameOf(kind), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 4) };
            name.SetResourceReference(TextBlock.ForegroundProperty, "FenceText");
            var card = new Border
            {
                CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 8, 10, 10), Margin = new Thickness(3), Width = 178,
                Child = new StackPanel { Children = { name, preview, sizes } },
            };
            card.SetResourceReference(Border.BackgroundProperty, "FenceHover");
            GalleryCards.Children.Add(card);
        }
        GalleryHost.Visibility = Visibility.Visible;
        if (_motion != Motion.Off) GalleryHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Flip));
        Serilog.Log.Information("widget gallery opened");
    }

    private void CloseGallery()
    {
        GalleryHost.Visibility = Visibility.Collapsed;
        GalleryCards.Children.Clear();
    }

    /// <summary>
    /// A fence is never the active window, so WPF does not make its popups topmost and the gallery opened under the fence
    /// (VM probe): the popup's window is raised above everything while it is open.
    /// </summary>
    private static void RaisePopup(Visual content)
    {
        if (PresentationSource.FromVisual(content) is System.Windows.Interop.HwndSource source) NeoFences.Shell.FenceWindowChrome.SetTopmost(source.Handle, topmost: true);
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } deeper) return deeper;
        }
        return null;
    }
}
