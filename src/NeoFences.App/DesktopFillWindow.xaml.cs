using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using NeoFences.Core.Items;
using NeoFences.Core.Library;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>One group's choice: an existing fence (its id), a new fence (its title), or nothing (skipped).</summary>
public sealed record DesktopFillGroup(string? FenceId, string? NewFenceTitle, IReadOnlyList<string> ItemRefs);

/// <summary>What "Add" asks for (M19 §3): items per group, and the "Hide desktop icons" option.</summary>
public sealed record DesktopFillPlan(IReadOnlyList<DesktopFillGroup> Groups, bool HideIcons);

/// <summary>
/// "Add from desktop…" (M19, spec 2026-10-05 §3, ADR-042): the desktop's entries, read off the UI thread and grouped
/// (Games, Apps, Folders and files, Web links); each group goes to a fence of the user's choice. OK gives <see cref="Plan"/>;
/// nothing is changed here.
/// </summary>
public partial class DesktopFillWindow : Window
{
    private const string SkipChoice = "Skip";
    private readonly IReadOnlyList<(string Id, string Title)> _fences;
    private readonly IReadOnlyDictionary<string, string> _alreadyIn;
    private readonly LibrarySettings _library;
    private readonly IReadOnlyList<GameEntry> _lastScan;
    private readonly IconLoader _iconLoader;
    private readonly List<(DesktopGroup Group, ComboBox PutIn, List<(CheckBox Box, string ItemRef)> Rows)> _sections = [];

    public DesktopFillPlan? Plan { get; private set; }

    /// <param name="fences">The user's fences (not the Game Library), by id and title.</param>
    /// <param name="alreadyIn">Targets already held by an item → the title of a fence holding it.</param>
    /// <param name="iconsHidden">"Hide desktop icons" is on already: the option is not offered.</param>
    /// <param name="library">The Game Library's settings: its scan tells which desktop entries are games.</param>
    /// <param name="lastScan">The Game Library's last scan (its index), reused when there is one; empty: scanned here (M20).</param>
    public DesktopFillWindow(IReadOnlyList<(string Id, string Title)> fences, IReadOnlyDictionary<string, string> alreadyIn,
        LibrarySettings library, IReadOnlyList<GameEntry> lastScan, IconLoader iconLoader, bool iconsHidden)
    {
        InitializeComponent();
        _fences = fences;
        _alreadyIn = alreadyIn;
        _library = library;
        _lastScan = lastScan;
        _iconLoader = iconLoader;
        HideIconsBox.Visibility = iconsHidden ? Visibility.Collapsed : Visibility.Visible;
        AddButton.Click += (_, _) => Accept();
        Loaded += (_, _) => Load();
    }

    public static string GroupTitle(DesktopGroup group) => group switch
    {
        DesktopGroup.Games => "Games",
        DesktopGroup.Apps => "Apps",
        DesktopGroup.WebLinks => "Web links",
        _ => "Folders and files",
    };

    private void Load()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var library = _library;
        var lastScan = _lastScan;
        // An STA thread of its own: shortcuts are read through the shell's link object.
        ShellWorker.RunAlone(() =>
        {
            List<(DesktopGroup Group, string ItemRef)>? sorted = null;
            try
            {
                var (gameFolders, knownGames) = KnownGames(library, lastScan);
                sorted = [.. DesktopItems.Enumerate().ItemRefs.Select(itemRef => (DesktopSorting.GroupOf(Read(itemRef), gameFolders, knownGames), itemRef))];
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                Log.Warning(failure, "the desktop could not be read for Add from desktop");
            }
            dispatcher.BeginInvoke(() => Show(sorted));
        }, name: "NeoFences desktop fill");
    }

    /// <summary>
    /// What the Game Library knows (read-only; it writes nothing): the user's game folders plus every game's install folder
    /// (never a drive root or a system folder, M20), and the desktop shortcuts it counts as games. Its last scan is reused
    /// when there is one; otherwise a scan runs here. A scan that fails leaves the user's folders only.
    /// </summary>
    private static (IReadOnlyList<string> Folders, IReadOnlySet<string> Shortcuts) KnownGames(LibrarySettings library, IReadOnlyList<GameEntry> lastScan)
    {
        try
        {
            var games = lastScan.Count > 0 ? lastScan
                : GameScanners.ScanAll(library, (source, failure) => Log.Debug(failure, "game scan {Source} failed for Add from desktop", source))
                    .SelectMany(scan => scan.Games).ToList();
            var folders = DesktopSorting.UsableGameFolders([.. library.Folders, .. games.Select(game => game.InstallFolder).OfType<string>()]);
            return (folders, games.Select(game => game.ShortcutFile).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Warning(failure, "the games could not be scanned for Add from desktop; launcher links and game folders still count");
            return (DesktopSorting.UsableGameFolders(library.Folders), new HashSet<string>());
        }
    }

    /// <summary>One entry's facts; a shortcut that cannot be read counts as a file (spec §3: never a failure).</summary>
    private static DesktopEntry Read(string itemRef)
    {
        if (itemRef.StartsWith("::", StringComparison.Ordinal)) return new DesktopEntry(itemRef, IsFolder: false, LinkTarget: null, LinkArguments: null);
        try
        {
            var isFolder = Directory.Exists(itemRef);
            if (isFolder || Path.GetExtension(itemRef).ToLowerInvariant() is not (".lnk" or ".url")) return new DesktopEntry(itemRef, isFolder, null, null);
            // A shortcut with no file path (a Store app's, Control Panel's, This PC's) tells what it points at instead (M20):
            // an app's shell:AppsFolder\<id> or Windows' "::{GUID}…"; nothing at all sorts it with the apps.
            var launch = ShellLinks.Read(itemRef);
            var target = launch?.Target ?? (Path.GetExtension(itemRef).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? ShellLinks.ShellTargetOf(itemRef) : null) ?? "";
            return new DesktopEntry(itemRef, IsFolder: false, LinkTarget: target, LinkArguments: launch?.Arguments);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Debug(failure, "desktop entry {ItemRef} could not be read; listed with the files", itemRef);
            return new DesktopEntry(itemRef, IsFolder: false, LinkTarget: null, LinkArguments: null);
        }
    }

    private void Show(List<(DesktopGroup Group, string ItemRef)>? sorted)
    {
        if (sorted is null || sorted.Count == 0)
        {
            ListStatus.Text = sorted is null ? "Your desktop could not be read." : "Your desktop has nothing to add.";
            return;
        }
        ListStatus.Visibility = Visibility.Collapsed;
        foreach (var group in Enum.GetValues<DesktopGroup>())
        {
            var itemRefs = sorted.Where(entry => entry.Group == group).Select(entry => entry.ItemRef).ToList();
            if (itemRefs.Count > 0) AddSection(group, itemRefs);
        }
        UpdateAddButton();
    }

    private void AddSection(DesktopGroup group, IReadOnlyList<string> itemRefs)
    {
        var title = GroupTitle(group);
        var header = new DockPanel { Margin = new Thickness(0, _sections.Count == 0 ? 0 : 18, 0, 6) };
        var putIn = new ComboBox { MinWidth = 200 };
        AutomationPropertiesName(putIn, $"Put {title} in");
        var sameTitle = _fences.FirstOrDefault(fence => string.Equals(fence.Title, title, StringComparison.CurrentCultureIgnoreCase));
        foreach (var fence in _fences) putIn.Items.Add(new ComboBoxItem { Content = fence.Title, Tag = fence.Id });
        putIn.Items.Add(new ComboBoxItem { Content = $"New fence: {title}", Tag = null });
        putIn.Items.Add(new ComboBoxItem { Content = SkipChoice, Tag = SkipChoice });
        putIn.SelectedIndex = sameTitle.Id is { } existing ? _fences.ToList().FindIndex(fence => fence.Id == existing) : _fences.Count;
        putIn.SelectionChanged += (_, _) => UpdateAddButton();
        var putInRow = new StackPanel { Orientation = Orientation.Horizontal };
        putInRow.Children.Add(new TextBlock { Text = "Put in:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        putInRow.Children.Add(putIn);
        DockPanel.SetDock(putInRow, Dock.Right);
        header.Children.Add(putInRow);
        header.Children.Add(new TextBlock { Text = $"{title} ({itemRefs.Count})", FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        GroupsPanel.Children.Add(header);

        var rows = new List<(CheckBox, string)>();
        foreach (var itemRef in itemRefs)
        {
            var view = new FenceItemView(new ShownItem(itemRef, itemRef));
            _iconLoader.Request(view, 24);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = new Image { Width = 24, Height = 24, Margin = new Thickness(0, 0, 8, 0) };
            icon.SetBinding(Image.SourceProperty, new Binding(nameof(FenceItemView.Icon)) { Source = view });
            var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            name.SetBinding(TextBlock.TextProperty, new Binding(nameof(FenceItemView.Label)) { Source = view });
            content.Children.Add(icon);
            content.Children.Add(name);
            var already = _alreadyIn.TryGetValue(itemRef, out var fenceTitle);
            if (already)
            {
                content.Children.Add(new TextBlock
                {
                    Text = $"already in {fenceTitle}", Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorSecondaryBrush"),
                });
            }
            var box = new CheckBox { Content = content, IsChecked = !already, Margin = new Thickness(4, 2, 0, 2) };
            box.Checked += (_, _) => UpdateAddButton();
            box.Unchecked += (_, _) => UpdateAddButton();
            GroupsPanel.Children.Add(box);
            rows.Add((box, itemRef));
        }
        _sections.Add((group, putIn, rows));
    }

    private static void AutomationPropertiesName(DependencyObject element, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(element, name);

    private static bool IsSkipped(ComboBox putIn) => (putIn.SelectedItem as ComboBoxItem)?.Tag as string == SkipChoice;

    private void UpdateAddButton() =>
        AddButton.IsEnabled = _sections.Any(section => !IsSkipped(section.PutIn) && section.Rows.Any(row => row.Box.IsChecked == true));

    private void Accept()
    {
        var groups = new List<DesktopFillGroup>();
        foreach (var (group, putIn, rows) in _sections)
        {
            if (IsSkipped(putIn)) continue;
            var itemRefs = rows.Where(row => row.Box.IsChecked == true).Select(row => row.ItemRef).ToList();
            if (itemRefs.Count == 0) continue;
            var fenceId = (putIn.SelectedItem as ComboBoxItem)?.Tag as string;
            groups.Add(new DesktopFillGroup(fenceId, fenceId is null ? GroupTitle(group) : null, itemRefs));
        }
        Plan = new DesktopFillPlan(groups, HideIcons: HideIconsBox.IsVisible && HideIconsBox.IsChecked == true);
        DialogResult = true;
    }
}
