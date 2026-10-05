using System.Windows.Controls;
using NeoFences.Core.Items;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Folder views (M21, spec 2026-10-05-folder-views-design, ADR-044): fences that show one folder live, read-only. One
/// <see cref="FolderLister"/> per view (a hidden tab keeps listing); the entries are paths, like the Game Library's.
/// NeoFences never writes to the folder: no drop into it, no rename or delete from NeoFences' own menu.
/// </summary>
public sealed partial class FenceHost
{
    private readonly Dictionary<string, FolderLister> _viewListers = new(StringComparer.Ordinal);
    // The last listing per view (null: not readable); missing while the first listing is on its way.
    private readonly Dictionary<string, IReadOnlyList<ItemInfo>?> _viewListings = new(StringComparer.Ordinal);
    // The selection made off the UI thread for the view's settings at the time, and the newest request per view (M23).
    private readonly Dictionary<string, (FolderView View, FolderViews.Selection? Selection)> _viewSelections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _viewSelectionRequests = new(StringComparer.Ordinal);
    private IReadOnlyList<string>? _busyFolders;

    /// <summary>A lister for every view fence, on its current folder; listers of views that are gone (or changed folder) go.</summary>
    private void EnsureViewListers()
    {
        var views = _config.Fences.Where(fence => fence.View is not null).ToDictionary(fence => fence.Id, fence => fence.View!.Path, StringComparer.Ordinal);
        foreach (var (fenceId, lister) in _viewListers.ToList())
        {
            if (views.TryGetValue(fenceId, out var path) && FolderViews.SameFolder(path, lister.Folder)) continue;
            lister.Dispose(); // the folder stays as it is
            _viewListers.Remove(fenceId);
            _viewListings.Remove(fenceId);
            _viewSelections.Remove(fenceId);
        }
        foreach (var (fenceId, path) in views.Where(view => !_viewListers.ContainsKey(view.Key)))
        {
            var lister = new FolderLister(path, noticeOwner: _messages.Handle, label: "folder view",
                show: listed => ShowView(fenceId, listed),
                logFailure: failure => Log.Warning(failure, "folder view: cannot watch {Folder}", path),
                renamed: (oldPath, newPath) => OnViewFolderRenamed(fenceId, oldPath, newPath));
            if (_gameMode) lister.SetPaused(true);
            _viewListers[fenceId] = lister;
        }
    }

    private void StopViewListers()
    {
        foreach (var lister in _viewListers.Values) lister.Dispose();
        _viewListers.Clear();
        _viewListings.Clear();
        _viewSelections.Clear();
    }

    /// <summary>A listing arrived (on the UI thread): kept, and shown when the view is the shown tab of its box.</summary>
    private void ShowView(string fenceId, IReadOnlyList<ItemInfo>? listed)
    {
        if (!_viewListers.ContainsKey(fenceId)) return; // its fence went meanwhile
        var wasAvailable = !_viewListings.TryGetValue(fenceId, out var before) || before is not null;
        _viewListings[fenceId] = listed;
        if (listed is null && wasAvailable && _config.Fences.FirstOrDefault(fence => fence.Id == fenceId)?.View is { } view)
            Log.Information("folder view: {Folder} is not available", view.Path); // once per outage, not every 7 s retry
        if (_config.Fences.FirstOrDefault(fence => fence.Id == fenceId)?.View is not { } settings) return;
        // Filtered and sorted off the UI thread (M23): 20,000 entries sorted by name take ~140 ms, at every re-list.
        var request = _viewSelectionRequests[fenceId] = _viewSelectionRequests.GetValueOrDefault(fenceId) + 1;
        Task.Run(() => listed is null ? null : FolderViews.Select(listed, settings)).ContinueWith(selecting =>
        {
            if (selecting.IsFaulted)
            {
                Log.Warning(selecting.Exception, "folder view {FenceId}: the listing could not be sorted", fenceId);
                return;
            }
            if (!_viewListers.ContainsKey(fenceId) || _viewSelectionRequests.GetValueOrDefault(fenceId) != request) return; // gone, or a newer listing
            _viewSelections[fenceId] = (settings, selecting.Result);
            if (_windows.Values.FirstOrDefault(window => window.FenceId == fenceId) is { } shown) RefreshWindow(shown);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>The view's entries as its settings select them, and its status line (RefreshWindow calls this for views).</summary>
    private void RenderView(FenceWindow window, Fence fence)
    {
        if (!_viewListings.TryGetValue(fence.Id, out var listed))
        {
            window.SetItems([]); // the first listing is on its way (a slow share): nothing yet, no status
            window.SetViewStatus(center: null, more: null);
            return;
        }
        // The background selection when it matches the settings; after a settings change, once here (a user action).
        if (!_viewSelections.TryGetValue(fence.Id, out var cached) || cached.View != fence.View)
        {
            cached = (fence.View!, listed is null ? null : FolderViews.Select(listed, fence.View!));
            _viewSelections[fence.Id] = cached;
        }
        var selection = cached.Selection;
        window.SetItems(selection is null ? [] : [.. selection.Shown.Select(path => new ShownItem(path, path))]);
        var (center, more) = FolderViews.Status(selection, fence.View!);
        window.SetViewStatus(center, more);
    }

    private void SetViewsPaused(bool paused)
    {
        foreach (var lister in _viewListers.Values) lister.SetPaused(paused);
    }

    /// <summary>Windows asks to remove a drive a view shows (a USB stick): that view lets go and says "not available".</summary>
    private bool ReleaseViewsForRemoval(nint handle) => _viewListers.Values.Aggregate(false, (released, lister) => lister.ReleaseForRemoval(handle) | released);

    /// <summary>Tray or fence menu → "New folder view…": Windows' folder dialog, then the settings with busy-folder defaults.</summary>
    private void NewFolderView(nint ownerHandle)
    {
        var picked = PathPicker.TryPickFolder(ownerHandle, "Choose a folder to show in a fence",
            failure => Log.Warning(failure, "folder view: the folder dialog failed"));
        if (picked is null) return;
        var dialog = new FolderViewWindow(FolderViews.DefaultsFor(picked, BusyFolders));
        if (dialog.ShowDialog() != true || dialog.Result is not { } view) return;
        SetQuickHidden(false); // a new fence must show
        (_config, var fence) = FenceEdits.CreateView(_config, view);
        Log.Information("folder view {FenceId} created for {Folder}", fence.Id, view.Path);
        SyncBoxes();
        SaveNow();
    }

    /// <summary>
    /// Folder item menu → "Show as folder view": a view of that folder with the defaults, placed in free space like a new
    /// fence (live check: a fixed offset from the fence stacked every view made from it on the same spot).
    /// </summary>
    private void ShowAsFolderView(string folder)
    {
        SetQuickHidden(false); // a new fence must show
        (_config, var fence) = FenceEdits.CreateView(_config, FolderViews.DefaultsFor(folder, BusyFolders));
        Log.Information("folder view {FenceId} created for {Folder} from an item", fence.Id, folder);
        SyncBoxes();
        SaveNow();
    }

    /// <summary>View fence menu → "Folder view settings…".</summary>
    private void EditFolderView(FenceWindow window)
    {
        var fenceId = window.FenceId; // the window may show another tab by the time the dialog closes
        if (_config.Fences.FirstOrDefault(fence => fence.Id == fenceId)?.View is not { } current) return;
        var dialog = new FolderViewWindow(current) { Owner = window };
        if (dialog.ShowDialog() != true || dialog.Result is not { } view) return;
        // A tray restore while the dialog was open may have removed the view or made it another kind (final review M5).
        if (_config.Fences.FirstOrDefault(fence => fence.Id == fenceId)?.View is null)
        {
            Log.Information("folder view {FenceId} changed while its settings were open; the new settings are not applied", fenceId);
            return;
        }
        SetView(fenceId, view);
    }

    /// <summary>A view's new settings: saved, its lister on the (new) folder, its window, title and menus updated.</summary>
    private void SetView(string fenceId, FolderView view)
    {
        _config = FenceEdits.SetView(_config, fenceId, view);
        EnsureViewListers();
        // Its tab header too when the view is a hidden tab of a box (M23).
        if (FenceTabs.HostOf(_config, fenceId) is { } host && _windows.TryGetValue(host.Id, out var boxWindow) && boxWindow.FenceId != fenceId) RefreshTabs(boxWindow);
        if (_windows.Values.FirstOrDefault(window => window.FenceId == fenceId) is { } window && _config.Fences.First(fence => fence.Id == fenceId) is var fence)
        {
            window.Refresh(fence);
            window.SetTitle(fence.Title);
            RefreshTabs(window);
            RefreshWindow(window);
        }
        ScheduleSave();
    }

    /// <summary>"Sort by" on a view: its own sort, kept and live (M21), not the one-time reorder of items.</summary>
    private void SortView(FenceWindow window, FenceSort sort)
    {
        if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId)?.View is { } view) SetView(window.FenceId, view with { Sort = sort });
    }

    /// <summary>The view's folder was renamed in place (Explorer): the view follows it, its title too while it is the folder's name.</summary>
    private void OnViewFolderRenamed(string fenceId, string oldPath, string newPath)
    {
        if (_config.Fences.FirstOrDefault(fence => fence.Id == fenceId)?.View is not { } view || !FolderViews.SameFolder(view.Path, oldPath)) return;
        Log.Information("folder view {FenceId}: {OldPath} renamed to {NewPath}; following it", fenceId, oldPath, newPath);
        SetView(fenceId, view with { Path = newPath });
    }

    /// <summary>"Open folder" (the view's menu or its "+ N more" line): Explorer at the view's folder.</summary>
    private void OpenViewFolder(FenceWindow window)
    {
        if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId)?.View is { } view) OpenItem(view.Path, ownerHandle: window.Handle);
    }

    /// <summary>
    /// Right-click on a view's entries: NeoFences' safe menu (open, show, copy, add to a fence). Shift+right-click: Windows'
    /// menu for the real entries, under a line saying so — only there can a real rename or delete happen.
    /// </summary>
    private void ShowViewItemMenu(FenceWindow window, IReadOnlyList<string> paths, bool extended, int screenX, int screenY, bool fromKeyboard)
    {
        if (paths.Count == 0) return;
        if (extended)
        {
            ShowViewWindowsMenu(window, paths, screenX, screenY);
            return;
        }
        var menu = new ContextMenu();
        void Command(ItemsControl parent, string header, Action run)
        {
            var command = new MenuItem { Header = header };
            command.Click += (_, _) => run();
            parent.Items.Add(command);
        }
        Command(menu, "Open", () => { foreach (var path in paths) OpenItem(path, ownerHandle: window.Handle); });
        if (paths.Count == 1) Command(menu, "Open file location", () => ShowInFolder(paths[0]));
        Command(menu, paths.Count == 1 ? "Copy path" : "Copy paths", () => CopyText(string.Join(Environment.NewLine, paths)));
        var itemFences = _config.Fences.Where(fence => fence.Kind == FenceKind.Items).ToList();
        var addTo = new MenuItem { Header = "Add to fence", IsEnabled = itemFences.Count > 0 };
        foreach (var fence in itemFences) Command(addTo, fence.Title.Length > 0 ? fence.Title : "(untitled fence)", () => AddToFence(fence.Id, paths));
        menu.Items.Add(addTo);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Shift+right-click: Windows' menu", IsEnabled = false });
        window.ShowItemMenu(menu, fromKeyboard);
    }

    /// <summary>Windows' menu for a view's entries, after a 2 s check of the folder off the UI thread (a share may have gone, M19 R2).</summary>
    private void ShowViewWindowsMenu(FenceWindow window, IReadOnlyList<string> paths, int screenX, int screenY)
    {
        var folder = System.IO.Path.GetDirectoryName(paths[0]) ?? paths[0];
        Task.Run(() => TargetProbe.Check(folder)).ContinueWith(checking =>
        {
            if (checking.IsFaulted)
            {
                Log.Warning(checking.Exception, "Windows' menu: {Folder} could not be checked", folder);
                return;
            }
            if (checking.Result.State != TargetState.Ok)
            {
                var menu = new ContextMenu();
                menu.Items.Add(new MenuItem { Header = TargetChecks.IsNetworkPath(folder) ? "Network location not reachable" : "Drive not connected", IsEnabled = false });
                window.ShowItemMenu(menu, fromKeyboard: false);
                return;
            }
            ShellItemMenu.Show(window.Handle, paths, screenX, screenY, extended: true,
                logFailure: failure => Log.Warning(failure, "Windows' menu or its command failed for {Paths}", paths),
                header: "Windows menu — acts on the real files", customCommands: [], handDeleteBack: false, out _);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>"Add to fence ▸": the entries become virtual items at the end of that fence, which then shows them selected.</summary>
    private void AddToFence(string fenceId, IReadOnlyList<string> paths)
    {
        if (!_config.Fences.Any(fence => fence.Id == fenceId && fence.Kind == FenceKind.Items)) return;
        var added = ItemEdits.Add(_items, fenceId, [.. paths.Select(VirtualItem.Create)]);
        _items = added.Document;
        Log.Information("{Added} item(s) added to fence {FenceId} from a folder view; {Already} already there", added.AddedIds.Count, fenceId, added.AlreadyThereIds.Count);
        ItemsChanged(checkTargets: paths);
        if (_windows.Values.FirstOrDefault(window => window.FenceId == fenceId) is { } shown) shown.SelectItems([.. added.AddedIds, .. added.AlreadyThereIds]);
    }

    /// <summary>Downloads and Screenshots, asked once (a local call; a missing one is logged and left out).</summary>
    private IReadOnlyList<string> BusyFolders => _busyFolders ??= KnownFolders.BusyFolders(failure => Log.Information(failure, "folder view: a known folder is not available"));
}
