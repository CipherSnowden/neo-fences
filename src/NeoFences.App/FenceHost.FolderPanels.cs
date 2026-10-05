using System.IO;
using System.Windows.Controls;
using NeoFences.Core.Items;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Folder panels (M26, spec 2026-10-05-folder-panel-design, ADR-048): a folder item shown live inside any fence, read-only.
/// One <see cref="FolderLister"/> per panel on the folder it shows (a hidden tab keeps listing); browsing lives in memory.
/// Folder-view fences (M21) became fences holding one filling panel. NeoFences never writes to a panel's folder: no drop
/// into it, no rename or delete from NeoFences' own menu.
/// </summary>
public sealed partial class FenceHost
{
    private readonly Dictionary<string, FolderLister> _panelListers = new(StringComparer.Ordinal);
    // Per panel item: the folder of its last listing and that listing (null: not readable); missing while the first is on its way.
    private readonly Dictionary<string, (string Folder, IReadOnlyList<ItemInfo>? Listed)> _panelListings = new(StringComparer.Ordinal);
    // The selection made off the UI thread for the panel's settings and folder at the time, and the newest request per panel (M23).
    private readonly Dictionary<string, (FolderPanel Panel, string Folder, FolderViews.Selection? Selection)> _panelSelections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _panelSelectionRequests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PanelPlace> _panelPlaces = new(StringComparer.Ordinal);
    private IReadOnlyList<string>? _busyFolders;

    private IEnumerable<VirtualItem> AllPanels => _items.Fences.Values.SelectMany(items => items).Where(FolderPanels.IsPanel);

    /// <summary>The folder a panel shows now: where it browsed to, or its own (a target changed since: its own again).</summary>
    private string ShownFolder(VirtualItem panel)
    {
        if (_panelPlaces.TryGetValue(panel.Id, out var place) && (FolderViews.SameFolder(place.Current, panel.Target) || PanelPlace.IsBelow(place.Current, panel.Target))) return place.Current;
        _panelPlaces[panel.Id] = PanelPlace.At(panel.Target);
        return panel.Target;
    }

    /// <summary>A lister for every panel, on the folder it shows; listers of panels that are gone (or moved on) go.</summary>
    private void EnsurePanelListers()
    {
        var panels = AllPanels.GroupBy(panel => panel.Id).ToDictionary(group => group.Key, group => ShownFolder(group.First()), StringComparer.Ordinal);
        foreach (var (itemId, lister) in _panelListers.ToList())
        {
            if (panels.TryGetValue(itemId, out var folder) && FolderViews.SameFolder(folder, lister.Folder)) continue;
            lister.Dispose(); // the folder stays as it is
            _panelListers.Remove(itemId);
            if (!panels.ContainsKey(itemId)) ForgetPanel(itemId);
        }
        foreach (var (itemId, folder) in panels.Where(panel => !_panelListers.ContainsKey(panel.Key)))
        {
            var lister = new FolderLister(folder, noticeOwner: _messages.Handle, label: "folder panel",
                show: listed => ShowPanelListing(itemId, folder, listed),
                logFailure: failure => Log.Warning(failure, "folder panel: cannot watch {Folder}", folder));
            if (_gameMode) lister.SetPaused(true);
            _panelListers[itemId] = lister;
        }
    }

    private void ForgetPanel(string itemId)
    {
        _panelListings.Remove(itemId);
        _panelSelections.Remove(itemId);
        _panelPlaces.Remove(itemId);
    }

    private void StopPanelListers()
    {
        foreach (var lister in _panelListers.Values) lister.Dispose();
        _panelListers.Clear();
    }

    private void SetPanelsPaused(bool paused)
    {
        foreach (var lister in _panelListers.Values) lister.SetPaused(paused);
    }

    /// <summary>Windows asks to remove a drive a panel shows (a USB stick): that panel lets go and says "not available".</summary>
    private bool ReleasePanelsForRemoval(nint handle) => _panelListers.Values.Aggregate(false, (released, lister) => lister.ReleaseForRemoval(handle) | released);

    /// <summary>A listing arrived (on the UI thread): kept, filtered and sorted off the UI thread, then shown.</summary>
    private void ShowPanelListing(string itemId, string folder, IReadOnlyList<ItemInfo>? listed)
    {
        if (!_panelListers.TryGetValue(itemId, out var lister) || !FolderViews.SameFolder(lister.Folder, folder)) return; // gone, or moved on meanwhile
        var wasAvailable = !_panelListings.TryGetValue(itemId, out var before) || before.Listed is not null;
        _panelListings[itemId] = (folder, listed);
        if (listed is null && wasAvailable) Log.Information("folder panel: {Folder} is not available", folder); // once per outage, not every 7 s retry
        if (_items.Find(itemId)?.Panel is not { } panel) return;
        // Off the UI thread (M23): 20,000 entries sorted by name take ~140 ms, at every re-list.
        var request = _panelSelectionRequests[itemId] = _panelSelectionRequests.GetValueOrDefault(itemId) + 1;
        Task.Run(() => listed is null ? null : FolderPanels.Select(listed, panel)).ContinueWith(selecting =>
        {
            if (selecting.IsFaulted)
            {
                Log.Warning(selecting.Exception, "folder panel {ItemId}: the listing could not be sorted", itemId);
                return;
            }
            if (!_panelListers.ContainsKey(itemId) || _panelSelectionRequests.GetValueOrDefault(itemId) != request) return; // gone, or a newer listing
            _panelSelections[itemId] = (panel, folder, selecting.Result);
            ShowPanel(itemId);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>The panel's content in the window that shows its fence now, if any.</summary>
    private void ShowPanel(string itemId)
    {
        if (_items.Find(itemId) is not { } panel || _items.FenceOf(itemId) is not { } fenceId) return;
        if (_windows.Values.FirstOrDefault(window => window.FenceId == fenceId) is { } shown) shown.SetPanelContent(itemId, PanelContentOf(panel));
    }

    /// <summary>Every panel of the shown fence gets its content (RefreshWindow calls this after the items).</summary>
    private void RenderPanels(FenceWindow window, IReadOnlyList<VirtualItem> items)
    {
        foreach (var panel in items.Where(FolderPanels.IsPanel)) window.SetPanelContent(panel.Id, PanelContentOf(panel));
    }

    /// <summary>What a panel shows: its header and buttons, and the entries its settings select (once here after a settings change, a user action).</summary>
    private PanelContent PanelContentOf(VirtualItem panel)
    {
        var folder = ShownFolder(panel);
        var place = _panelPlaces[panel.Id];
        var header = FolderPanels.HeaderOf(panel.Target, folder, panel.OwnName);
        var atHome = FolderViews.SameFolder(folder, panel.Target);
        // A panel filling a fence titled like it, at its own folder, needs no second title (a migrated folder view looks as before).
        var showHeader = !(atHome && !place.CanGoBack && _items.FenceOf(panel.Id) is { } fenceId && FolderPanels.Fills(_items.Of(fenceId))
            && _config.Fences.FirstOrDefault(fence => fence.Id == fenceId)?.Title == header);
        if (!_panelListings.TryGetValue(panel.Id, out var listing) || !FolderViews.SameFolder(listing.Folder, folder))
            return new PanelContent(header, showHeader, place.CanGoBack, place.CanGoUp(panel.Target), !atHome, [], Status: null, More: null); // on its way (a slow share)
        if (!_panelSelections.TryGetValue(panel.Id, out var cached) || cached.Panel != panel.Panel || !FolderViews.SameFolder(cached.Folder, folder))
        {
            cached = (panel.Panel!, folder, listing.Listed is null ? null : FolderPanels.Select(listing.Listed, panel.Panel!));
            _panelSelections[panel.Id] = cached;
        }
        var (status, more) = FolderViews.Status(cached.Selection, new FolderView { Path = folder });
        return new PanelContent(header, showHeader, place.CanGoBack, place.CanGoUp(panel.Target), !atHome, cached.Selection?.Entries ?? [], status, more);
    }

    /// <summary>What a panel asked for (M26 spec §2).</summary>
    private void OnPanelCommand(FenceWindow window, string itemId, PanelCommand command)
    {
        if (_items.Find(itemId) is not { } panel || !FolderPanels.IsPanel(panel)) return;
        switch (command)
        {
            case PanelOpen { Paths: [var only] } when IsListedFolder(itemId, only):
                Browse(panel, place => place.Into(only));
                break;
            case PanelOpen open:
                foreach (var path in open.Paths) OpenItem(path, ownerHandle: window.Handle); // a folder among several opens in Explorer
                SetPeek(false);
                break;
            case PanelNavigate { Move: PanelMove.Back }:
                Browse(panel, place => place.Back());
                break;
            case PanelNavigate { Move: PanelMove.Up }:
                Browse(panel, place => place.Up(panel.Target));
                break;
            case PanelNavigate { Move: PanelMove.Home }:
                Browse(panel, place => place.Home(panel.Target));
                break;
            case PanelSortBy sortBy:
                SetPanel(itemId, FolderPanels.HeaderSort(panel.Panel!, sortBy.Sort));
                break;
            case PanelEntryMenu menu:
                ShowEntryMenu(window, menu.Paths, menu.Extended, menu.ScreenX, menu.ScreenY, menu.FromKeyboard);
                break;
            case PanelDrag drag:
                DragEntries(window, drag.Paths);
                break;
            case PanelOpenFolder:
                OpenItem(ShownFolder(panel), ownerHandle: window.Handle);
                break;
        }
    }

    /// <summary>A path the panel's last listing has as a folder (no disk asked: a dead share cannot freeze the click).</summary>
    private bool IsListedFolder(string itemId, string path) =>
        _panelListings.TryGetValue(itemId, out var listing) && listing.Listed?.Any(entry => entry.IsFolder && ItemKinds.Comparer.Equals(entry.ItemRef, path)) == true;

    /// <summary>Back, Up, Home or into a subfolder: the panel lists its new folder (nothing saved).</summary>
    private void Browse(VirtualItem panel, Func<PanelPlace, PanelPlace> move)
    {
        ShownFolder(panel); // a place for it, its own folder if the target changed
        var place = move(_panelPlaces[panel.Id]);
        if (place == _panelPlaces[panel.Id]) return;
        _panelPlaces[panel.Id] = place;
        EnsurePanelListers();
        ShowPanel(panel.Id);
    }

    /// <summary>A panel's look, sort or filters changed: saved, and shown at once.</summary>
    private void SetPanel(string itemId, FolderPanel settings)
    {
        if (_items.Find(itemId) is not { } item) return;
        _items = ItemEdits.Replace(_items, item with { Panel = settings });
        ItemsChanged(checkTargets: []);
    }

    /// <summary>A panel's menu: Look ▸, Sort by ▸, settings, Open folder, Size ▸ with Fill fence, Show as icon, Remove.</summary>
    private void ShowPanelMenu(FenceWindow window, VirtualItem item, bool fromKeyboard)
    {
        var panel = item.Panel!;
        var menu = new ContextMenu();
        MenuItem Command(ItemsControl parent, string header, Action run, bool? isChecked = null, bool enabled = true)
        {
            var command = new MenuItem { Header = header, IsChecked = isChecked == true, IsEnabled = enabled };
            command.Click += (_, _) => run();
            parent.Items.Add(command);
            return command;
        }
        if (CheckOf(item.Target).State != TargetState.Ok)
        {
            Command(menu, "Locate…", () => Locate(window, item.Id));
            menu.Items.Add(new Separator());
        }
        var look = new MenuItem { Header = "Look" };
        foreach (var (choice, name) in new[] { (PanelLook.Details, "Details"), (PanelLook.List, "List"), (PanelLook.Icons, "Icons") })
            Command(look, name, () => SetPanel(item.Id, panel with { Look = choice }), isChecked: panel.Look == choice);
        menu.Items.Add(look);
        var sort = new MenuItem { Header = "Sort by" };
        foreach (var (choice, name) in new[] { (PanelSort.Name, "Name"), (PanelSort.Date, "Date modified"), (PanelSort.Type, "Type"), (PanelSort.Size, "Size") })
            Command(sort, name, () => SetPanel(item.Id, FolderPanels.HeaderSort(panel, choice)), isChecked: panel.Sort == choice);
        menu.Items.Add(sort);
        Command(menu, "Panel settings…", () => EditPanel(window, item.Id));
        Command(menu, "Open folder", () => OpenItem(ShownFolder(item), ownerHandle: window.Handle));
        menu.Items.Add(new Separator());
        menu.Items.Add(SizeMenu(menu, [item]));
        var alone = _items.Of(window.FenceId) is [_];
        Command(menu, "Fill fence", () => SetFill(item.Id, !item.Fill), isChecked: item.Fill && alone, enabled: alone);
        Command(menu, "Show as icon", () => SetPanelShown(item.Id, null));
        Command(menu, "Remove from fence", () => RemoveItems(window, [item.Id]));
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Shift+right-click on entries: Windows' menu", IsEnabled = false });
        window.ShowItemMenu(menu, fromKeyboard);
    }

    private void SetFill(string itemId, bool fill)
    {
        if (_items.Find(itemId) is not { } item) return;
        _items = ItemEdits.Replace(_items, item with { Fill = fill });
        Log.Information("folder panel {ItemId} fills its fence: {Fill}", itemId, fill);
        ItemsChanged(checkTargets: []);
    }

    /// <summary>Folder item → "Show as folder panel" (the defaults, 4×4, in place); panel → "Show as icon" (null).</summary>
    private void SetPanelShown(string itemId, FolderPanel? panel)
    {
        if (_items.Find(itemId) is not { } item) return;
        _items = ItemEdits.Replace(_items, item with { Panel = panel, Size = null, Fill = false });
        Log.Information("item {ItemId} shown as {Look}", itemId, panel is null ? "an icon" : "a folder panel");
        ItemsChanged(checkTargets: []);
    }

    /// <summary>Fence menu → "Add folder panel…": Windows' folder dialog, then a panel at the end (Flow) or the first free spot (Free).</summary>
    private void AddFolderPanel(FenceWindow window)
    {
        if (window.Kind != FenceKind.Items || PickPanelFolder(window.Handle) is not { } folder) return;
        var panel = FolderPanels.Create(folder, BusyFolders);
        _items = _items.With(window.FenceId, [.. _items.Of(window.FenceId), panel]); // not ItemEdits.Add: the folder's own icon may be there too
        Log.Information("folder panel added to fence {FenceId} for {Folder}", window.FenceId, folder);
        ItemsChanged(checkTargets: [folder]);
        window.SelectItems([panel.Id]);
    }

    /// <summary>Tray or fence menu → "New folder panel…": a new fence, titled with the folder's name, holding one filling panel.</summary>
    private void NewFolderPanel(nint ownerHandle)
    {
        if (PickPanelFolder(ownerHandle) is not { } folder) return;
        SetQuickHidden(false); // a new fence must show
        (_config, var fence) = FenceEdits.CreateFence(_config, FolderViews.NameOf(folder));
        _items = _items.With(fence.Id, [FolderPanels.Create(folder, BusyFolders) with { Fill = true }]);
        Log.Information("fence {FenceId} created with a folder panel of {Folder}", fence.Id, folder);
        SyncBoxes();
        ItemsChanged(checkTargets: [folder]);
        SaveNow();
    }

    private static string? PickPanelFolder(nint ownerHandle) =>
        PathPicker.TryPickFolder(ownerHandle, "Choose a folder to show in a fence", failure => Log.Warning(failure, "folder panel: the folder dialog failed"));

    /// <summary>Panel menu → "Panel settings…": the folder and what it shows (the sort is the headers' and Sort by's).</summary>
    private void EditPanel(FenceWindow window, string itemId)
    {
        if (_items.Find(itemId) is not { Panel: { } current } item) return;
        var dialog = new FolderViewWindow(item.Target, current) { Owner = window };
        if (dialog.ShowDialog() != true || dialog.Result is not { } result) return;
        // A tray restore while the dialog was open may have removed the panel (final review M5 of M21).
        if (_items.Find(itemId) is not { Panel: not null } now)
        {
            Log.Information("folder panel {ItemId} changed while its settings were open; the new settings are not applied", itemId);
            return;
        }
        _items = ItemEdits.Replace(_items, now with { Target = result.Folder, Panel = result.Panel });
        Log.Information("folder panel {ItemId} settings changed ({Folder})", itemId, result.Folder);
        ItemsChanged(checkTargets: [result.Folder]);
    }

    /// <summary>
    /// The folder views of an older version become fences holding one filling panel (spec §1) — a snapshot first; items are
    /// saved before the config, so a cut-short save only repeats the migration (it adds no second panel).
    /// </summary>
    private bool MigrateFolderViews()
    {
        if (!_config.Fences.Any(fence => fence.View is not null)) return false;
        if (_itemsReadOnly)
        {
            Log.Information("folder views not made into panels: items.json is read-only this session"); // the config must not lose the view
            return false;
        }
        var migration = FolderPanels.MigrateViews(_config, _items);
        var now = DateTimeOffset.Now;
        // A repeat (a cut-short save, a read-only config) adds no panel and needs no second snapshot (final review M7).
        if (migration.AddedPanels > 0 && _snapshots.Save(Snapshots.Take(_config, _items, name: $"Before folder views became panels ({now:d MMM HH:mm})", now: now)) is null)
        {
            Log.Warning(_snapshots.LastFailure, "folder views not made into panels: the snapshot before it could not be saved; tried again next time");
            return false;
        }
        (_config, _items) = (migration.Config, migration.Items);
        Log.Information("folder view fence(s) {FenceIds} now hold a folder panel each", migration.MigratedFenceIds);
        SaveNow(itemsFirst: true);
        return true;
    }

    /// <summary>
    /// Right-click on a panel's entries: NeoFences' safe menu (open, show, copy, add to a fence). Shift+right-click: Windows'
    /// menu for the real entries, under a line saying so — only there can a real rename or delete happen.
    /// </summary>
    private void ShowEntryMenu(FenceWindow window, IReadOnlyList<string> paths, bool extended, int screenX, int screenY, bool fromKeyboard)
    {
        if (paths.Count == 0) return;
        if (extended)
        {
            ShowEntryWindowsMenu(window, paths, screenX, screenY);
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
        if (fromKeyboard) window.ShowItemMenuAt(menu, screenX, screenY); // the Menu key: at the row (M28)
        else window.ShowItemMenu(menu, fromKeyboard: false);
    }

    /// <summary>Windows' menu for a panel's entries, after a 2 s check of the folder off the UI thread (a share may have gone, M19 R2).</summary>
    private void ShowEntryWindowsMenu(FenceWindow window, IReadOnlyList<string> paths, int screenX, int screenY)
    {
        var folder = Path.GetDirectoryName(paths[0]) ?? paths[0];
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

    /// <summary>A panel's entries dragged out: other apps get copies, fences get items (asked once now, at most 2 s, M19 R2).</summary>
    private void DragEntries(FenceWindow window, IReadOnlyList<string> paths)
    {
        var folder = Path.GetDirectoryName(paths[0]) ?? paths[0];
        if (TargetProbe.Check(folder).State != TargetState.Ok)
        {
            Log.Information("drag from folder panel: {Folder} is not reachable", folder);
            return;
        }
        ShellDragDrop.TryDrag(window.Handle, paths, paths, [], logFailure: failure => Log.Warning(failure, "could not start dragging {Paths}", paths));
    }

    /// <summary>"Add to fence ▸": the entries become virtual items at the end of that fence, which then shows them selected.</summary>
    private void AddToFence(string fenceId, IReadOnlyList<string> paths)
    {
        if (!_config.Fences.Any(fence => fence.Id == fenceId && fence.Kind == FenceKind.Items)) return;
        var added = ItemEdits.Add(_items, fenceId, [.. paths.Select(VirtualItem.Create)]);
        _items = added.Document;
        Log.Information("{Added} item(s) added to fence {FenceId} from a folder panel; {Already} already there", added.AddedIds.Count, fenceId, added.AlreadyThereIds.Count);
        ItemsChanged(checkTargets: paths);
        if (_windows.Values.FirstOrDefault(window => window.FenceId == fenceId) is { } shown) shown.SelectItems([.. added.AddedIds, .. added.AlreadyThereIds]);
    }

    /// <summary>Downloads and Screenshots, asked once (a local call; a missing one is logged and left out).</summary>
    private IReadOnlyList<string> BusyFolders => _busyFolders ??= KnownFolders.BusyFolders(failure => Log.Information(failure, "folder panel: a known folder is not available"));
}
