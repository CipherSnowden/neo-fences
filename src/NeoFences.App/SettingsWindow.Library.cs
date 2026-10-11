using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;


/// <summary>What Settings → Games shows (M12; M22: the fences new games can go to, and the chosen one).</summary>
/// <param name="HasFence">The scan runs (games are wanted somewhere).</param>
public sealed record LibraryView(bool HasFence, IReadOnlyList<string> Folders, LibrarySources Sources, string Status,
    IReadOnlyList<(string Id, string Title)> Fences, string? NewGamesFence, bool OnlineArt = false);

/// <summary>
/// Settings → Games (M12; M35; M41b: no hidden games — nothing can hide one since the Game Library fence went, audit card 7):
/// the sources, game folders, where new games go, and "Refresh library now". Every change goes to the host, which saves,
/// rescans and shows it back.
/// </summary>
public partial class SettingsWindow
{
    public event Action<IReadOnlyList<string>>? LibraryFoldersChanged;
    public event Action<LibrarySources>? LibrarySourcesChanged;

    public event Action? RefreshLibraryRequested;
    /// <summary>"New games go to" (M22): a fence id, or null for nowhere.</summary>
    public event Action<string?>? NewGamesFenceChanged;
    /// <summary>"Find covers and website icons online" (M34, ADR-055).</summary>
    public event Action<bool>? OnlineArtChanged;

    private IReadOnlyList<string> _libraryFolders = [];
    private IReadOnlyList<(string Id, string Title)> _newGamesChoices = [(Id: "", Title: "\u0000")]; // never equal to a real list: the first show builds it

    private (CheckBox Box, string Name)[] SourceBoxes =>
    [
        (SteamSourceBox, "Steam"), (EpicSourceBox, "Epic"), (GogSourceBox, "GOG"), (UbisoftSourceBox, "Ubisoft Connect"), (EaSourceBox, "EA app"),
        (BattleNetSourceBox, "Battle.net"), (XboxSourceBox, "Xbox / Microsoft Store"), (FoldersSourceBox, "My game folders"), (ShortcutsSourceBox, "Game shortcuts on the Desktop"),
    ];

    private void InitializeLibrary()
    {
        foreach (var (box, name) in SourceBoxes)
        {
            box.Content = name;
            OnToggled(box, _ => LibrarySourcesChanged?.Invoke(SelectedSources));
        }
        AddGameFolderButton.Click += (_, _) =>
        {
            var folder = PathPicker.TryPickFolder(new WindowInteropHelper(this).Handle, "Choose a folder whose sub-folders are games",
                logFailure: failure => Log.Warning(failure, "game library: the folder picker failed"));
            if (folder is not null && !_libraryFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) LibraryFoldersChanged?.Invoke([.. _libraryFolders, folder]);
        };
        RemoveGameFolderButton.Click += (_, _) =>
        {
            if (GameFolderList.SelectedItem is string folder) LibraryFoldersChanged?.Invoke([.. _libraryFolders.Where(other => !string.Equals(other, folder, StringComparison.OrdinalIgnoreCase))]);
        };
        GameFolderList.SelectionChanged += (_, _) => RemoveGameFolderButton.IsEnabled = GameFolderList.SelectedItem is not null;

        RefreshLibraryButton.Click += (_, _) => RefreshLibraryRequested?.Invoke();
        OnToggled(OnlineArtBox, isChecked => OnlineArtChanged?.Invoke(isChecked)); // M34
        NewGamesBox.SelectionChanged += (_, _) =>
        {
            if (_updating || NewGamesBox.SelectedItem is not ComboBoxItem chosen) return;
            NewGamesFenceChanged?.Invoke(chosen.Tag as string);
        };
        AutomationProperties.SetHelpText(GameFolderList, LibraryDescription.Text);
    }

    private LibrarySources SelectedSources => new()
    {
        Steam = SteamSourceBox.IsChecked == true, Epic = EpicSourceBox.IsChecked == true, Gog = GogSourceBox.IsChecked == true,
        Ubisoft = UbisoftSourceBox.IsChecked == true, Ea = EaSourceBox.IsChecked == true, BattleNet = BattleNetSourceBox.IsChecked == true,
        Xbox = XboxSourceBox.IsChecked == true, Folders = FoldersSourceBox.IsChecked == true, DesktopShortcuts = ShortcutsSourceBox.IsChecked == true,
    };

    /// <summary>Called inside <see cref="Show(SettingsView)"/>'s "updating" block: setting the boxes reports nothing back.</summary>
    private void ShowLibrary(LibraryView view)
    {
        _libraryFolders = view.Folders;
        var selectedFolder = GameFolderList.SelectedItem as string;
        GameFolderList.ItemsSource = view.Folders;
        GameFolderList.SelectedItem = view.Folders.FirstOrDefault(folder => folder == selectedFolder);
        RemoveGameFolderButton.IsEnabled = GameFolderList.SelectedItem is not null;
        var sources = view.Sources;
        (SteamSourceBox.IsChecked, EpicSourceBox.IsChecked, GogSourceBox.IsChecked) = (sources.Steam, sources.Epic, sources.Gog);
        (UbisoftSourceBox.IsChecked, EaSourceBox.IsChecked, BattleNetSourceBox.IsChecked) = (sources.Ubisoft, sources.Ea, sources.BattleNet);
        (XboxSourceBox.IsChecked, FoldersSourceBox.IsChecked, ShortcutsSourceBox.IsChecked) = (sources.Xbox, sources.Folders, sources.DesktopShortcuts);
        // M41b (audit): the folders only count with "My game folders" on — greyed with a note otherwise.
        GameFolderList.IsEnabled = AddGameFolderButton.IsEnabled = sources.Folders;
        GameFoldersOffNote.Visibility = sources.Folders ? Visibility.Collapsed : Visibility.Visible;
        LibraryStatus.Text = view.Status;
        // Rebuilt only when the fences changed (M23): a rebuild closes an open dropdown, and scans refresh Settings often.
        if (!view.Fences.SequenceEqual(_newGamesChoices))
        {
            _newGamesChoices = view.Fences;
            NewGamesBox.Items.Clear();
            NewGamesBox.Items.Add(new ComboBoxItem { Content = "Nowhere", Tag = null });
            foreach (var (id, title) in view.Fences) NewGamesBox.Items.Add(new ComboBoxItem { Content = title.Length > 0 ? title : "(untitled fence)", Tag = id });
        }
        NewGamesBox.SelectedItem = NewGamesBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == view.NewGamesFence) ?? NewGamesBox.Items[0];
        RefreshLibraryButton.IsEnabled = view.HasFence;
        OnlineArtBox.IsChecked = view.OnlineArt; // M34
    }
}
