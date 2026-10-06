using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NeoFences.App;

/// <summary>
/// Menu icons and destructive entries (M35, spec §1, ADR-056): a glyph from Windows' icon font (Segoe Fluent Icons, else
/// Segoe MDL2 Assets) as a menu entry's icon, and a red ink for Delete / Remove. Set in XAML
/// (<c>local:MenuGlyph.Glyph="&amp;#xE710;"</c>) or with <see cref="Entry"/> for menus built in code.
/// </summary>
public static class MenuGlyph
{
    public const string Add = "", View = "", Collect = "", Rename = "", Colour = "", Lock = "",
        Refresh = "", NewFence = "", Settings = "", Delete = "", Item = "", Desktop = "",
        Games = "", Folder = "", Widget = "", Size = "", Labels = "", Sort = "", Layout = "",
        Open = "", OpenFolder = "", Copy = "", Properties = "", Remove = "", ShowAs = "",
        Cover = "", Detach = "", Pause = "", Snapshot = "", Restore = "", Help = "",
        Exit = "", Undo = "", SafeMode = "", Peek = "", Hide = "", Run = "", Admin = "",
        Missing = "", GameMode = "";

    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.RegisterAttached("Glyph", typeof(string), typeof(MenuGlyph), new PropertyMetadata(null, OnGlyphChanged));

    public static string? GetGlyph(DependencyObject element) => (string?)element.GetValue(GlyphProperty);
    public static void SetGlyph(DependencyObject element, string? glyph) => element.SetValue(GlyphProperty, glyph);

    /// <summary>A destructive entry (Delete fence, Remove …): shown in the menu's red ink.</summary>
    public static readonly DependencyProperty DangerProperty =
        DependencyProperty.RegisterAttached("Danger", typeof(bool), typeof(MenuGlyph), new PropertyMetadata(false));

    public static bool GetDanger(DependencyObject element) => (bool)element.GetValue(DangerProperty);
    public static void SetDanger(DependencyObject element, bool danger) => element.SetValue(DangerProperty, danger);

    private static void OnGlyphChanged(DependencyObject element, DependencyPropertyChangedEventArgs change)
    {
        if (element is MenuItem item) item.Icon = change.NewValue is string { Length: > 0 } glyph ? Icon(glyph) : null;
    }

    private static TextBlock Icon(string glyph) =>
        new() { Text = glyph, FontFamily = IconFont, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>A menu entry built in code: its icon, its click, checked or not, red when destructive.</summary>
    public static MenuItem Entry(string header, string? glyph, Action? click = null, bool? isChecked = null, bool danger = false, bool enabled = true)
    {
        // M35 review: a name with "_" (a fence, a snapshot) keeps it; WPF would take it as an access key.
        var entry = new MenuItem { Header = header.Replace("_", "__"), IsChecked = isChecked == true, IsEnabled = enabled };
        if (glyph is not null) entry.Icon = Icon(glyph);
        if (danger) SetDanger(entry, true);
        if (click is not null) entry.Click += (_, _) => click();
        return entry;
    }
}

/// <summary>The menus' colours (M35): light or dark like the fences; set on the application, so every menu follows at once.</summary>
public static class MenuTheme
{
    public static void Apply(ResourceDictionary resources, bool light)
    {
        resources["MenuBackground"] = Frozen(light ? Color.FromArgb(0xF7, 0xF9, 0xF9, 0xF9) : Color.FromArgb(0xF7, 0x2C, 0x2C, 0x30));
        resources["MenuBorder"] = Frozen(light ? Color.FromArgb(0x1F, 0, 0, 0) : Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
        resources["MenuText"] = Frozen(light ? Color.FromArgb(0xE6, 0, 0, 0) : Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3));
        resources["MenuHover"] = Frozen(light ? Color.FromArgb(0x0F, 0, 0, 0) : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        resources["MenuSeparator"] = Frozen(light ? Color.FromArgb(0x14, 0, 0, 0) : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        resources["MenuDanger"] = Frozen(light ? Color.FromRgb(0xC4, 0x2B, 0x1C) : Color.FromRgb(0xFF, 0x99, 0xA4));
    }

    private static SolidColorBrush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// The tray menu (M35, spec §1): the host's entries as NeoFences' own menu at the pointer, with icons; a click elsewhere or
/// Esc closes it; the chosen entry's id goes to <c>chosen</c> (submenus' own ids never do).
/// </summary>
public static class TrayMenuView
{
    public static void Show(IReadOnlyList<NeoFences.Shell.TrayMenuItem> items, Point at, Func<int, string?> glyphOf, Action<int> chosen)
    {
        var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint, HorizontalOffset = at.X, VerticalOffset = at.Y };
        Fill(menu, items, glyphOf, chosen);
        menu.IsOpen = true;
    }

    private static void Fill(ItemsControl parent, IReadOnlyList<NeoFences.Shell.TrayMenuItem> items, Func<int, string?> glyphOf, Action<int> chosen)
    {
        foreach (var item in items)
        {
            if (item.Id == 0)
            {
                parent.Items.Add(new Separator());
                continue;
            }
            var parts = item.Text.Split('\t'); // "Peek\tCtrl+Alt+Space": the keys at the right
            var entry = MenuGlyph.Entry(parts[0], glyphOf(item.Id), item.Children is { Count: > 0 } ? null : () => chosen(item.Id),
                isChecked: item.Checked, danger: false, enabled: item.Enabled);
            if (parts.Length > 1) entry.InputGestureText = parts[1];
            if (item.Children is { Count: > 0 } children) Fill(entry, children, glyphOf, chosen);
            parent.Items.Add(entry);
        }
    }
}
