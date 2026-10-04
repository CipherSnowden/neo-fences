using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using NeoFences.Core.Items;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// "An app…" (M19, spec 2026-10-05 §1, ADR-042): Start's All apps, listed off the UI thread, with icons and a search box.
/// OK gives <see cref="Chosen"/>; nothing is changed here.
/// </summary>
public partial class AppPickerWindow : Window
{
    private readonly IconLoader _iconLoader;
    private ICollectionView? _view;

    /// <summary>The app picked (OK or double-click), or null.</summary>
    public InstalledApp? Chosen { get; private set; }

    public AppPickerWindow(IconLoader iconLoader)
    {
        InitializeComponent();
        _iconLoader = iconLoader;
        SearchBox.TextChanged += (_, _) => _view?.Refresh();
        SearchBox.PreviewKeyDown += (_, key) =>
        {
            if (key.Key != Key.Down || AppListBox.Items.Count == 0) return;
            AppListBox.SelectedIndex = Math.Max(AppListBox.SelectedIndex, 0); // arrow down: from the search into the list
            (AppListBox.ItemContainerGenerator.ContainerFromIndex(AppListBox.SelectedIndex) as UIElement)?.Focus();
            key.Handled = true;
        };
        AppListBox.SelectionChanged += (_, _) => OkButton.IsEnabled = AppListBox.SelectedItem is not null;
        AppListBox.MouseDoubleClick += (_, _) => Accept();
        OkButton.Click += (_, _) => Accept();
        Loaded += (_, _) =>
        {
            SearchBox.Focus();
            Load();
        };
    }

    private void Load()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        // An STA thread of its own: the shell's folder enumeration expects one; ~0.3 s for ~200 apps.
        ShellWorker.RunAlone(() =>
        {
            IReadOnlyList<InstalledApp>? apps = null;
            try
            {
                apps = AppList.Enumerate();
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                Log.Warning(failure, "Windows did not list the apps"); // hard rule 7: this dialog says so, nothing else changes
            }
            dispatcher.BeginInvoke(() => Show(apps));
        }, name: "NeoFences app list");
    }

    private void Show(IReadOnlyList<InstalledApp>? apps)
    {
        if (apps is null || apps.Count == 0)
        {
            ListStatus.Text = apps is null ? "Windows did not list the apps." : "No apps found.";
            return;
        }
        ListStatus.Visibility = Visibility.Collapsed;
        var views = apps.Select(app => new FenceItemView(new ShownItem(app.AppId, ItemKinds.AppTarget(app.AppId), app.Name))).ToList();
        foreach (var view in views) _iconLoader.Request(view, 32);
        AppListBox.ItemsSource = views;
        _view = CollectionViewSource.GetDefaultView(views);
        _view.Filter = entry => SearchBox.Text.Trim() is not { Length: > 0 } search
                                || ((FenceItemView)entry).Label.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    private void Accept()
    {
        if (AppListBox.SelectedItem is not FenceItemView view) return;
        Chosen = new InstalledApp(view.Key, view.Label);
        DialogResult = true;
    }
}
