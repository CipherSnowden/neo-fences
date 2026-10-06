using System.IO;
using System.Windows.Threading;
using NeoFences.Core.Items;
using NeoFences.Core.Library;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// The Game Library fence (M12, spec 2026-10-03-game-library-design, ADR-032): scans the launchers, the Xbox app, the
/// user's game folders and Desktop game shortcuts on its own STA thread, merges them (Core <see cref="GameCatalog"/>),
/// keeps one shortcut per game in <see cref="AppPaths.LibraryDirectory"/> and shows that folder (FolderLister) as tiles.
/// Nothing is started or changed outside NeoFences' own folder.
/// </summary>
public sealed partial class FenceHost
{
    private static readonly TimeSpan LibraryQuietTime = TimeSpan.FromSeconds(5);

    private LibraryState _library = new();
    // Steam's / Epic's install lists and the game folders, each with a removal notice so "Safely remove" works (final review I2).
    private readonly List<(FolderWatcher Watcher, DeviceRemovalNotice? Notice)> _libraryWatchers = [];
    private DispatcherTimer? _libraryTimer;
    private bool _libraryActive, _libraryScanning, _libraryScanAgain, _libraryDeferred;
    private bool _libraryStopped; // NeoFences is exiting: a scan finishing now re-arms nothing (M13a)
    private bool _libraryFullScan; // the next scan searches every game folder again (final review I4)
    private string _libraryStatus = "Not scanned yet.";

    private bool HasLibraryFence => _config.Fences.Any(fence => fence.IsLibrary);

    /// <summary>
    /// The scan runs while games are wanted (M22): an old library fence, a game item in any fence, a fence new games go to,
    /// or the Add games… list open. Otherwise it sleeps (no watchers).
    /// </summary>
    private bool LibraryWanted => Current.ExtrasWanted && (HasLibraryFence || _config.Library.NewGamesFence is not null || _addGamesWindow is not null
                                  || _items.Fences.Values.Any(items => items.Any(GameItems.IsGame))); // M33: safe mode scans nothing

    /// <summary>The library folder's lister while the Game Library fence exists (a hidden library tab keeps listing, M9).</summary>
    private void EnsureLibraryLister()
    {
        var hasFence = HasLibraryFence && Current.ExtrasWanted; // M33
        if (!hasFence && _libraryLister is not null)
        {
            _libraryLister.Dispose(); // NeoFences' own folder stays as it is
            _libraryLister = null;
        }
        else if (hasFence && _libraryLister is null)
        {
            TryCreateFolder(AppPaths.LibraryDirectory);
            _libraryLister = new FolderLister(AppPaths.LibraryDirectory, noticeOwner: _messages.Handle, label: "library folder", show: ShowLibrary,
                logFailure: failure => Log.Warning(failure, "cannot watch the game library folder"));
            if (_gameMode) _libraryLister.SetPaused(true);
        }
        UpdateLibrary(); // M12: the library scans while its fence exists
    }

    /// <summary>The library folder's listing (null: unreadable) in catalog order, as tiles, in the window showing the library.</summary>
    private void ShowLibrary(IReadOnlyList<ItemInfo>? listed)
    {
        if (_windows.Values.FirstOrDefault(window => window.IsLibrary) is not { } window) return;
        window.SetLibraryArt(LibraryArt());
        window.SetItems(listed is null ? [] : [.. LibraryOrder(listed).Select(path => new ShownItem(path, path))]); // A–Z by game (M12)
    }

    /// <summary>Starts the library when its fence appears, stops watching when it goes (EnsureLibraryLister calls this).</summary>
    private void UpdateLibrary()
    {
        if (LibraryWanted == _libraryActive) return;
        _libraryActive = !_libraryActive;
        if (_libraryActive)
        {
            _library = LibraryWriter.ReadIndex(AppPaths.LibraryDirectory); // the last scan, until this one finishes
            ScanLibrary();
        }
        else
        {
            StopLibraryWatchers();
            _libraryTimer?.Stop();
        }
    }

    /// <summary>Rescans in the background; one scan at a time (a request during a scan runs once after it); waits during a game.</summary>
    /// <param name="full">Search every game's folder again instead of reusing the programs found before: the way out of a
    /// program picked while a game was still being copied (final review I4). "Refresh library" asks for it.</param>
    private void ScanLibrary(bool full = false)
    {
        if (!_libraryActive || _libraryStopped) return;
        _libraryFullScan |= full;
        if (Current.ShellWorkDeferred)
        {
            _libraryDeferred = true; // after the game (ApplyDeferredShellWork)
            return;
        }
        if (_libraryScanning)
        {
            _libraryScanAgain = true;
            return;
        }
        _libraryScanning = true;
        var remember = !_libraryFullScan;
        _libraryFullScan = false;
        var settings = _config.Library;
        var previous = _library;
        var dispatcher = Dispatcher.CurrentDispatcher;
        var noticeOwner = _messages.Handle;
        ShellWorker.RunAlone(() =>
        {
            try
            {
                void LogFailure(string what, Exception failure) => Log.Warning(failure, "game library: {What} could not be read", what);
                var scans = GameScanners.ScanAll(settings, LogFailure, previous: remember ? [.. previous.Items.Select(item => item.Game)] : null);
                var everything = GameCatalog.Merge(scans, previous.Items.Select(item => item.Game).ToList(), hidden: []);
                var hidden = settings.Hidden.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var games = everything.Where(game => !GameCatalog.IdsOf(game).Any(hidden.Contains)).ToList();
                var plan = LibraryFiles.Plan(previous.Items, games);
                var hiddenGames = GameCatalog.HiddenGames(everything, settings.Hidden, previous.Hidden); // names kept while a source is away (M13b)
                var state = LibraryWriter.Apply(AppPaths.LibraryDirectory, previous.Items, plan, hiddenGames,
                    logFailure: (file, failure) => Log.Warning(failure, "game library: {File} could not be written", file));
                var unreadable = scans.Where(scan => !scan.Readable).Select(scan => scan.ScanKey).ToList();
                // Built here, off the UI thread: opening a watcher on a sleeping disk or a network share can take seconds (final review I2).
                var watch = BuildLibraryWatchers(GameScanners.WatchFolders(settings), noticeOwner, gameFolders: settings.Folders);
                Log.Information("game library: {Count} games ({Written} written, {Removed} removed); unreadable: {Unreadable}",
                    state.Items.Count, plan.Write.Count, plan.Delete.Count, unreadable);
                dispatcher.BeginInvoke(() => OnLibraryScanned(state, unreadable, watch));
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                Log.Warning(failure, "game library scan failed; the library keeps its games"); // hard rule 7
                dispatcher.BeginInvoke(() => OnLibraryScanned(null, ["library"], []));
            }
        }, name: "NeoFences game library");
    }

    private void OnLibraryScanned(LibraryState? state, IReadOnlyList<string> unreadable,
        IReadOnlyList<(FolderWatcher Watcher, DeviceRemovalNotice? Notice)> watch)
    {
        _libraryScanning = false;
        if (_libraryStopped)
        {
            DisposeWatchers(watch);
            return;
        }
        var previous = _library;
        if (state is not null) _library = state;
        _libraryStatus = $"Last scan {DateTime.Now:HH:mm}: {_library.Items.Count} games"
                         + (unreadable.Count > 0 ? $"; not readable right now: {string.Join(", ", unreadable.Select(GameCatalog.SourceName))}" : ".");
        if (state is null || !_libraryActive) DisposeWatchers(watch); // a failed scan keeps the watchers it had (final review I2)
        else ReplaceLibraryWatchers(watch);
        _libraryLister?.Refresh(); // new art, order
        if (state is not null) ApplyGames(previous, state); // M22: game items follow the scan; new games go to their fence
        if (state is not null)
        {
            MaybeAskOnlineArt(); // M34 (ADR-055): asked once
            LookUpCovers(); // only after a yes
        }
        RefreshSettings();
        if (!_libraryScanAgain) return;
        _libraryScanAgain = false;
        ScanLibrary();
    }

    /// <summary>A watcher per folder, with a removal notice where Windows offers one. Runs on the scan thread.</summary>
    private static List<(FolderWatcher Watcher, DeviceRemovalNotice? Notice)> BuildLibraryWatchers(IReadOnlyList<string> folders, nint noticeOwner,
        IReadOnlyList<string> gameFolders) =>
        folders.Select(folder =>
        {
            // A game folder: only games (sub-folders) coming or going; Steam's and Epic's lists: every file (M13b).
            var watcher = new FolderWatcher(folder, failure => Log.Warning(failure, "game library: cannot watch {Folder}", folder),
                directoriesOnly: gameFolders.Contains(folder, StringComparer.OrdinalIgnoreCase));
            var notice = watcher.HeldFolder is { } held
                ? DeviceRemovalNotice.TryRegister(noticeOwner, held, failure => Log.Debug(failure, "game library: no removal notice for {Folder}", held))
                : null;
            return (watcher, notice);
        }).ToList();

    /// <summary>A change rescans after 5 quiet seconds (downloads write a lot); a watcher that stops (a network hiccup) rescans too, which re-arms it.</summary>
    private void ReplaceLibraryWatchers(IReadOnlyList<(FolderWatcher Watcher, DeviceRemovalNotice? Notice)> watch)
    {
        StopLibraryWatchers();
        var dispatcher = Dispatcher.CurrentDispatcher;
        foreach (var (watcher, notice) in watch)
        {
            watcher.Changed += () => dispatcher.BeginInvoke(ScheduleLibraryScan);
            watcher.Failed += () => dispatcher.BeginInvoke(ScheduleLibraryScan);
            if (watcher.HasFailed) ScheduleLibraryScan();
            _libraryWatchers.Add((watcher, notice));
        }
    }

    /// <summary>Windows asks to remove a drive the library watches (a USB Steam library): let go of it now (final review I2).</summary>
    private bool ReleaseLibraryForRemoval(nint handle)
    {
        var index = _libraryWatchers.FindIndex(entry => entry.Notice?.Handle == handle);
        if (index < 0) return false;
        DisposeWatchers([_libraryWatchers[index]]);
        _libraryWatchers.RemoveAt(index);
        ScheduleLibraryScan(); // re-arms what is still there once the removal is done (or refused)
        return true;
    }

    private static void DisposeWatchers(IEnumerable<(FolderWatcher Watcher, DeviceRemovalNotice? Notice)> watch)
    {
        foreach (var (watcher, notice) in watch)
        {
            watcher.Dispose();
            notice?.Dispose();
        }
    }

    private void ScheduleLibraryScan()
    {
        if (_libraryTimer is null)
        {
            _libraryTimer = new DispatcherTimer { Interval = LibraryQuietTime };
            _libraryTimer.Tick += (_, _) =>
            {
                _libraryTimer.Stop();
                ScanLibrary();
            };
        }
        _libraryTimer.Stop();
        _libraryTimer.Start();
    }

    private void StopLibraryWatchers()
    {
        DisposeWatchers(_libraryWatchers);
        _libraryWatchers.Clear();
    }

    /// <summary>The library's shortcuts in catalog (A–Z) order; files NeoFences did not write (its index) are not shown.</summary>
    private IReadOnlyList<string> LibraryOrder(IReadOnlyList<ItemInfo> listed)
    {
        var position = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (item, index) in _library.Items.Select((item, index) => (item, index))) position.TryAdd(item.FileName, index); // never throws on the UI thread
        return listed.Where(item => position.ContainsKey(Path.GetFileName(item.ItemRef)))
            .OrderBy(item => position[Path.GetFileName(item.ItemRef)]).Select(item => item.ItemRef).ToList();
    }

    /// <summary>Tile art by item ref: a poster, or an image logo (Xbox); programs' icons come from the shell.</summary>
    private IReadOnlyDictionary<string, (string Path, bool IsPoster)> LibraryArt()
    {
        var art = new Dictionary<string, (string Path, bool IsPoster)>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _library.Items)
        {
            var itemRef = Path.Combine(AppPaths.LibraryDirectory, item.FileName);
            if (ArtOf(item.Game) is { } chosen) art[itemRef] = (chosen.Path, chosen.IsPoster); // M34: choice, disk, online, logo
        }
        return art;
    }

    private LibraryItem? LibraryItemOf(string itemRef) =>
        _library.Items.FirstOrDefault(item => string.Equals(item.FileName, Path.GetFileName(itemRef), StringComparison.OrdinalIgnoreCase));

    /// <summary>Tray / fence menu → "New Game Library fence": one at most; with one already, its tab is shown.</summary>
    private void CreateLibraryFence()
    {
        if (_config.Fences.FirstOrDefault(fence => fence.IsLibrary) is { } existing)
        {
            if (FenceTabs.HostOf(_config, existing.Id) is { } host && _windows.TryGetValue(host.Id, out var window)) SwitchTab(window, existing.Id);
            return;
        }
        SetQuickHidden(false); // a new fence must show (as "New fence" does)
        var fence = Fence.Create("Games", isLibrary: true) with { IconSize = 64, Labels = _config.Settings.DefaultLabels };
        _config = _config with { Fences = [.. _config.Fences, fence] };
        Log.Information("Game Library fence created");
        SyncBoxes();
        SaveNow();
        if (_config.Library.Folders.Count > 0) return;
        // Spec §4: D:\GameLibrary when it exists — checked off the UI thread (a sleeping disk can take seconds, M13a).
        Task.Run(() => Directory.Exists(DefaultGameFolder)).ContinueWith(found =>
        {
            if (!found.Result || _config.Library.Folders.Count > 0) return;
            _config = _config with { Library = _config.Library with { Folders = [DefaultGameFolder] } };
            SaveNow();
            ScanLibrary();
            RefreshSettings();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Right-click → "Hide from library" (or Delete): the game leaves the library until "Show again" in Settings.</summary>
    private void HideGames(IReadOnlyList<string> itemRefs)
    {
        var ids = itemRefs.Select(LibraryItemOf).OfType<LibraryItem>().SelectMany(item => GameCatalog.IdsOf(item.Game)).ToList(); // every source of the game
        if (ids.Count == 0) return;
        _config = _config with { Library = _config.Library with { Hidden = [.. _config.Library.Hidden.Union(ids, StringComparer.OrdinalIgnoreCase)] } };
        Log.Information("game library: hid {Ids}", ids);
        SaveNow();
        ScanLibrary();
    }

    private const string DefaultGameFolder = @"D:\GameLibrary";

    private void OpenInstallFolder(FenceWindow window, string itemRef)
    {
        // Opened on its own thread; a missing folder is logged there (no disk check on the UI thread, M13a).
        if (LibraryItemOf(itemRef)?.Game.InstallFolder is { } folder) OpenItem(folder, ownerHandle: window.Handle);
        else Log.Information("game library: no install folder known for {ItemRef}", itemRef);
    }

    /// <summary>Windows' menu for NeoFences' own game shortcuts (M12): Delete only hides the game, nothing goes to the Recycle Bin.</summary>
    private void ShowLibraryItemMenu(FenceWindow window, IReadOnlyList<string> itemRefs, int screenX, int screenY, bool extended)
    {
        var choice = ShellItemMenu.Show(window.Handle, itemRefs, screenX, screenY, extended,
            logFailure: failure => Log.Warning(failure, "item menu or its command failed for {ItemRefs}", itemRefs),
            header: null, customCommands: ["Hide from library", "Open install folder"], handDeleteBack: true, out var custom);
        if (choice == ItemMenuChoice.Delete || (choice == ItemMenuChoice.Custom && custom == 0)) HideGames(itemRefs); // nothing goes to the Recycle Bin
        else if (choice == ItemMenuChoice.Custom && custom == 1) OpenInstallFolder(window, itemRefs[0]);
    }

    private LibraryView LibrarySettingsView() => new(
        HasFence: LibraryWanted,
        Folders: _config.Library.Folders,
        Sources: _config.Library.Sources,
        Hidden: HiddenGamesForSettings(),
        Status: LibraryWanted ? _libraryStatus : "No games in any fence yet: fence menu → Add games…, or choose where new games go.",
        Fences: [.. _config.Fences.Where(fence => fence.Kind == FenceKind.Items).Select(fence => (fence.Id, fence.Title))],
        NewGamesFence: _config.Library.NewGamesFence,
        OnlineArt: _config.Library.OnlineArt == true); // M34

    private void WireLibrarySettings(SettingsWindow window)
    {
        void Change(Func<LibrarySettings, LibrarySettings> edit, string what)
        {
            _config = _config with { Library = edit(_config.Library) };
            Log.Information("game library settings: {What}", what);
            SaveNow();
            ScanLibrary();
            RefreshSettings();
        }
        window.LibraryFoldersChanged += folders => Change(library => library with { Folders = folders }, "folders");
        window.LibrarySourcesChanged += sources => Change(library => library with { Sources = sources }, "sources");
        window.ShowGameAgainRequested += id =>
        {
            var ids = (_library.Hidden.FirstOrDefault(game => game.Ids.Contains(id, StringComparer.OrdinalIgnoreCase))?.Ids ?? [id]).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Change(library => library with { Hidden = [.. library.Hidden.Where(hiddenId => !ids.Contains(hiddenId))] }, "show again");
        };
        window.RefreshLibraryRequested += () => ScanLibrary(full: true);
        window.OnlineArtChanged += SetOnlineArt; // M34 (ADR-055)
        window.NewGamesFenceChanged += fenceId =>
        {
            Change(library => library with { NewGamesFence = fenceId }, "new games go to");
            UpdateLibrary(); // a fence for new games: the scan runs
        };
    }

    /// <summary>One row per hidden game (all its ids together); a hidden id no scan finds any more is listed by itself.</summary>
    private IReadOnlyList<HiddenGame> HiddenGamesForSettings()
    {
        var hidden = _config.Library.Hidden.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. GameCatalog.HiddenGames([], _config.Library.Hidden, _library.Hidden)
            .Select(entry => new HiddenGame(entry.Ids.FirstOrDefault(hidden.Contains) ?? entry.Ids[0], entry.Name, entry.Ids))];
    }
}
