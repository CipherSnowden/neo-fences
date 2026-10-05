using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NeoFences.Core.Config;
using NeoFences.Core.Items;
using NeoFences.Core.Library;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Virtual items (M18, spec 2026-10-04-virtual-items-design §2–§3, ADR-040): opening, the item menu, Properties, Add
/// item…, drops and drags, sorting and Locate…. Every change is to NeoFences' items only; no target is ever touched.
/// </summary>
public sealed partial class FenceHost
{
    private const int MaxPicturePx = 256;

    // ---------- opening ----------

    /// <summary>Double-click / Enter. Peek ends once something is opened from it (like Fences).</summary>
    private void OpenKey(FenceWindow window, string key)
    {
        if (window.Kind != FenceKind.Items) OpenItem(key, ownerHandle: window.Handle); // the library's and a view's key is a path (M12, M21)
        else if (_items.Find(key) is { } item) OpenVirtualItem(window, item, runAsAdmin: item.RunAsAdmin);
        SetPeek(false);
    }

    /// <summary>
    /// Opens with the item's arguments, as administrator when asked (spec §3). The target is checked first, on the open's
    /// own thread: a missing one gets NeoFences' Locate… / Remove question instead of a Windows error.
    /// </summary>
    private void OpenVirtualItem(FenceWindow window, VirtualItem item, bool runAsAdmin)
    {
        var owner = window.Handle;
        var dispatcher = Dispatcher.CurrentDispatcher;
        ShellWorker.RunAlone(() =>
        {
            var check = TargetProbe.Check(item.Target);
            if (check.State != TargetState.Ok)
            {
                dispatcher.BeginInvoke(() =>
                {
                    ApplyChecks([(item.Target, check)]);
                    AskAboutMissing(window, item.Id, check.State);
                });
                return;
            }
            var takesArguments = ItemKinds.TakesArguments(item.Kind, check.IsFolder);
            if (!ShellItems.TryOpen(item.Target, owner, takesArguments ? item.Arguments : null, runAsAdmin && takesArguments))
                Log.Warning("could not open {Target} (no app for it, or the admin prompt was declined)", item.Target);
        }, name: "NeoFences open");
    }

    /// <summary>A missing or unavailable target was opened: Locate… / Remove from fence / Cancel (spec §3).</summary>
    private void AskAboutMissing(FenceWindow window, string itemId, TargetState state)
    {
        if (_items.Find(itemId) is not { } item) return;
        // The fence's window may have closed meanwhile (its box merged into another): then the question stands alone (final review M1).
        var question = new MissingItemWindow(DisplayName(item), item.Target, state, game: GameItems.IsGame(item)) { Owner = _windows.ContainsValue(window) ? window : null };
        if (question.ShowDialog() != true) return;
        if (question.Choice == MissingItemChoice.Locate) Locate(window, itemId);
        else if (question.Choice == MissingItemChoice.Remove) RemoveItems(window, [itemId]);
    }

    private static string DisplayName(VirtualItem item) => item.OwnName ?? item.Kind switch
    {
        ItemKind.Website => ItemKinds.WebsiteName(item.Target),
        _ when ItemKinds.AppIdOf(item.Target) is { } appId => ItemKinds.AppName(appId), // until (or when uninstalled, never) Windows names it
        _ => Path.GetFileNameWithoutExtension(item.Target.TrimEnd('\\')) is { Length: > 0 } name ? name : item.Target,
    };

    // ---------- the item menu ----------

    /// <summary>
    /// Right-click on items (spec §3): NeoFences' own safe menu; Shift+right-click: Windows' menu for the real target,
    /// under a line saying so. The Game Library keeps Windows' menu with its own commands (M12).
    /// </summary>
    private void ShowItemMenu(FenceWindow window, IReadOnlyList<string> keys, int screenX, int screenY, bool fromKeyboard)
    {
        var shift = !fromKeyboard && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (window.IsLibrary)
        {
            ShowLibraryItemMenu(window, keys, screenX, screenY, extended: shift);
            return;
        }
        if (window.Kind == FenceKind.View)
        {
            ShowViewItemMenu(window, keys, extended: shift, screenX, screenY, fromKeyboard); // M21
            return;
        }
        var items = keys.Select(_items.Find).OfType<VirtualItem>().ToList();
        if (items.Count == 0) return;
        if (shift)
        {
            ShowWindowsMenu(window, items[0], screenX, screenY);
            return;
        }
        var menu = new ContextMenu();
        void Command(string header, Action run)
        {
            var command = new MenuItem { Header = header };
            command.Click += (_, _) => run();
            menu.Items.Add(command);
        }
        if (items.Count > 1)
        {
            Command("Open", () => { foreach (var item in items) OpenVirtualItem(window, item, runAsAdmin: item.RunAsAdmin); });
            menu.Items.Add(SizeMenu(menu, items)); // M24
            Command($"Remove {items.Count} items from fence", () => RemoveItems(window, [.. items.Select(item => item.Id)]));
        }
        else if (GameItems.IsGame(items[0]))
        {
            ShowGameItemMenu(window, items[0], fromKeyboard); // M22
            return;
        }
        else
        {
            var item = items[0];
            var check = CheckOf(item.Target);
            var onDisk = item.Kind == ItemKind.Path && TargetChecks.RootOf(item.Target) is not null;
            if (check.State != TargetState.Ok)
            {
                Command("Locate…", () => Locate(window, item.Id));
                Command("Remove from fence", () => RemoveItems(window, [item.Id]));
                menu.Items.Add(new Separator());
            }
            Command("Open", () => OpenVirtualItem(window, item, runAsAdmin: item.RunAsAdmin));
            if (onDisk && !check.IsFolder) Command("Run as administrator", () => OpenVirtualItem(window, item, runAsAdmin: true));
            if (onDisk) Command("Open file location", () => ShowInFolder(item.Target));
            if (onDisk && check.IsFolder && check.State == TargetState.Ok) Command("Show as folder view", () => ShowAsFolderView(item.Target)); // M21
            Command("Copy path", () => CopyText(item.Target));
            menu.Items.Add(new Separator());
            menu.Items.Add(SizeMenu(menu, [item])); // M24
            Command("Properties…", () => ShowProperties(window, item.Id, focusName: false));
            if (check.State == TargetState.Ok) Command("Remove from fence", () => RemoveItems(window, [item.Id]));
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "Shift+right-click: Windows' menu", IsEnabled = false });
        }
        window.ShowItemMenu(menu, fromKeyboard);
    }

    /// <summary>
    /// Windows' full menu for the real target (spec §3): only here can a real delete or rename happen, and it says so. The
    /// target is checked off the UI thread first (M19 R2): Windows builds the menu on the UI thread, and a dead share would
    /// freeze the fence; not reachable within 2 s, a one-line menu says so.
    /// </summary>
    private void ShowWindowsMenu(FenceWindow window, VirtualItem item, int screenX, int screenY)
    {
        if (item.Kind == ItemKind.Website) return; // a website has no Windows menu
        Task.Run(() => TargetProbe.Check(item.Target)).ContinueWith(checking =>
        {
            if (checking.IsFaulted)
            {
                Log.Warning(checking.Exception, "Windows' menu: {Target} could not be checked", item.Target); // M20: never silent
                return;
            }
            if (checking.Result.State == TargetState.Unavailable)
            {
                var menu = new ContextMenu();
                menu.Items.Add(new MenuItem
                {
                    Header = TargetChecks.IsNetworkPath(item.Target) ? "Network location not reachable" : "Drive not connected",
                    IsEnabled = false,
                });
                window.ShowItemMenu(menu, fromKeyboard: false);
                return;
            }
            // A game item's target is NeoFences' own shortcut: Delete there only removes the item (M22, final review M3).
            var game = GameItems.IsGame(item);
            var choice = ShellItemMenu.Show(window.Handle, [item.Target], screenX, screenY, extended: true,
                logFailure: failure => Log.Warning(failure, "Windows' menu or its command failed for {Target}", item.Target),
                header: "Windows menu — acts on the real file", customCommands: [], handDeleteBack: game, out _);
            if (game && choice == ItemMenuChoice.Delete) RemoveItems(window, [item.Id]);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static void ShowInFolder(string target) =>
        ShellWorker.RunAlone(() =>
        {
            if (!ShellItems.TryShowInFolder(target)) Log.Warning("could not show {Target} in Explorer", target);
        }, name: "NeoFences show in folder");

    private static void CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.ExternalException busy)
        {
            Log.Warning(busy, "the clipboard is busy; path not copied"); // another app holds it (hard rule 7)
        }
    }

    // ---------- remove, Properties, Add item…, Locate… ----------

    /// <summary>Del / "Remove from fence": the items go (one confirmation for several); their targets are never touched.</summary>
    private void RemoveItems(FenceWindow window, IReadOnlyList<string> keys)
    {
        if (window.IsLibrary)
        {
            HideGames(keys); // Delete in the library hides the game; its shortcut is NeoFences' own (M12)
            return;
        }
        if (window.Kind == FenceKind.View) return; // a view shows its folder as it is; nothing to remove (M21)
        if (keys.Count > 1 && MessageBox.Show(window, $"Remove {keys.Count} items from this fence?\n\nYour files, folders and apps are not touched.",
                "NeoFences", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        _items = ItemEdits.Remove(_items, keys.ToHashSet(StringComparer.Ordinal));
        Log.Information("{Count} item(s) removed from fence {FenceId}", keys.Count, window.FenceId);
        ItemsChanged(checkTargets: []);
    }

    /// <summary>Properties (F2: the name selected; Alt+Enter; the menu): changes only the item (spec §3).</summary>
    private void ShowProperties(FenceWindow window, string key, bool focusName)
    {
        if (window.Kind != FenceKind.Items || _items.Find(key) is not { } item) return;
        var dialog = new ItemPropertiesWindow(item, adding: false, iconLoader: _iconLoader, focusName: focusName) { Owner = window };
        if (dialog.ShowDialog() != true || dialog.Result is not { } changed) return;
        changed = WithCopiedPicture(window, changed, dialog.PictureToCopy);
        _items = ItemEdits.Replace(_items, changed);
        Log.Information("item {ItemId} changed in Properties", changed.Id);
        ItemsChanged(checkTargets: [changed.Target]);
    }

    /// <summary>Fence menu → "Add item…" (spec §2): a file, folder, app or website, with its own name if wanted.</summary>
    private void AddItem(FenceWindow window)
    {
        if (window.Kind != FenceKind.Items) return;
        var dialog = new ItemPropertiesWindow(VirtualItem.Create(""), adding: true, iconLoader: _iconLoader, focusName: false) { Owner = window };
        if (dialog.ShowDialog() != true || dialog.Result is not { } created) return;
        var added = ItemEdits.Add(_items, window.FenceId, [WithCopiedPicture(window, created, dialog.PictureToCopy)]);
        _items = added.Document;
        Log.Information("item added to fence {FenceId}: {Target}", window.FenceId, created.Target);
        ItemsChanged(checkTargets: [created.Target]);
        window.SelectItems(added.AddedIds.Count > 0 ? added.AddedIds : added.AlreadyThereIds); // already there: it flashes
    }

    /// <summary>
    /// Locate… (spec §4): a picker at the nearest folder that still exists; only the target changes. A missing app opens
    /// the app list (M19 §1). Afterwards the other items from the same old place are offered (M19 §2).
    /// </summary>
    private void Locate(FenceWindow window, string itemId)
    {
        if (_items.Find(itemId) is not { } item) return;
        if (ItemKinds.IsApp(item.Target))
        {
            // The fence's window may have closed meanwhile (a tray restore during the Missing question): then it stands alone.
            var picker = new AppPickerWindow(_iconLoader) { Owner = _windows.ContainsValue(window) ? window : null };
            if (picker.ShowDialog() == true && picker.Chosen is { } app) Relocated(window, itemId, ItemKinds.AppTarget(app.AppId));
            return;
        }
        var name = DisplayName(item);
        var asFolder = !Path.HasExtension(item.Target.TrimEnd('\\')); // ponytail: a folder named "x.y" opens the file picker; Properties → Browse covers it
        // Off the UI thread: the old drive may be a sleeping disk or a share that does not answer.
        Task.Run(() => NearestExistingFolder(item.Target)).ContinueWith(found =>
        {
            void LogFailure(Exception failure) => Log.Warning(failure, "Locate… dialog failed");
            var picked = asFolder
                ? PathPicker.TryPickFolder(window.Handle, $"Where is \"{name}\" now?", LogFailure, found.Result)
                : PathPicker.TryPickFile(window.Handle, $"Where is \"{name}\" now?", LogFailure, found.Result);
            if (picked is not null) Relocated(window, itemId, picked);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>One item now points at its new place; then the bulk fix is offered for its neighbours.</summary>
    private void Relocated(FenceWindow window, string itemId, string newTarget)
    {
        if (_items.Find(itemId) is not { } current) return;
        var oldTarget = current.Target;
        _items = ItemEdits.Replace(_items, current with { Target = newTarget });
        Log.Information("item {ItemId} located at {Target}", itemId, newTarget);
        ItemsChanged(checkTargets: [newTarget]);
        OfferRelocation(window, itemId, oldTarget, newTarget);
    }

    /// <summary>
    /// The bulk fix (M19 §2, ADR-042): the other missing or unavailable items under the same old place whose files are at
    /// the new place are offered in one question; Fix saves an undo snapshot first.
    /// </summary>
    private void OfferRelocation(FenceWindow window, string locatedId, string oldTarget, string newTarget)
    {
        if (Relocation.Find(oldTarget, newTarget) is not { } bases) return;
        var moves = Relocation.Candidates(_items, StateOf, bases, exceptItemId: locatedId);
        if (moves.Count == 0) return;
        // Off the UI thread: each new place is checked (a share answers within 2 s or does not count).
        Task.Run(() => TargetProbe.CheckAll([.. moves.Select(move => move.NewTarget)])).ContinueWith(checking =>
        {
            if (checking.IsFaulted)
            {
                Log.Warning(checking.Exception, "the new places of {Count} item(s) could not be checked; no bulk fix offered", moves.Count);
                return;
            }
            var found = moves.Where((_, index) => checking.Result[index].Check.State == TargetState.Ok).ToList();
            if (found.Count == 0) return;
            var labels = found.Select(move => _items.Find(move.ItemId)).OfType<VirtualItem>().Select(DisplayName).ToList();
            var question = new RelocateWindow(found.Count, bases, labels) { Owner = _windows.ContainsValue(window) ? window : null };
            if (question.ShowDialog() == true) FixItems(found, bases);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>"Fix": an undo snapshot first (tray → "Undo the last restore or fix"); not saved: nothing changes.</summary>
    private void FixItems(IReadOnlyList<Relocation.Move> moves, Relocation.Bases bases)
    {
        // Only items still pointing where they did when asked (one may have been removed or changed meanwhile); none left:
        // the undo slot keeps what it had (M20).
        var newTargets = moves.Where(move => _items.Find(move.ItemId) is { } item && ItemKinds.Comparer.Equals(item.Target, move.OldTarget))
            .ToDictionary(move => move.ItemId, move => move.NewTarget, StringComparer.Ordinal);
        if (newTargets.Count == 0)
        {
            Log.Information("bulk fix: every proposed item changed meanwhile; nothing fixed");
            return;
        }
        var now = DateTimeOffset.Now;
        if (_snapshots.Save(Snapshots.Take(_config, _items, name: Relocation.UndoName(newTargets.Count, $"{now:d MMM HH:mm}"), now: now),
                SnapshotStore.BeforeRestoreFileName) is null)
        {
            Log.Warning(_snapshots.LastFailure, "items not fixed: the undo snapshot could not be saved");
            SnapshotFailure("Items not fixed", "NeoFences could not save the undo snapshot first (see the log).");
            return;
        }
        _items = ItemEdits.Relocate(_items, newTargets);
        Log.Information("{Count} item(s) fixed: {OldBase} -> {NewBase}", newTargets.Count, bases.OldBase, bases.NewBase);
        ItemsChanged(checkTargets: [.. newTargets.Values]);
        RefreshSettings(); // the snapshot list shows the undo snapshot
    }

    private static string? NearestExistingFolder(string target)
    {
        // A drive or share that does not answer within 2 s: the dialog opens at Windows' default place (M19 R2).
        if (TargetChecks.RootOf(target) is { } root && TargetProbe.Check(root).State != TargetState.Ok) return null;
        try
        {
            for (var folder = Path.GetDirectoryName(target.TrimEnd('\\')); folder is not null; folder = Path.GetDirectoryName(folder))
            {
                if (Directory.Exists(folder)) return folder;
            }
        }
        catch (Exception failure) when (failure is IOException or ArgumentException or UnauthorizedAccessException)
        {
            Log.Debug(failure, "no folder of {Target} to start Locate… in", target);
        }
        return null;
    }

    /// <summary>
    /// A picture chosen as the icon (spec §3) is copied into NeoFences' icons folder, at most 256 px, as PNG, under a new
    /// name each time (so a cached older picture never shows). Unreadable: the item keeps its icon and the user is told.
    /// </summary>
    private static VirtualItem WithCopiedPicture(Window owner, VirtualItem item, string? picture)
    {
        if (picture is null) return item;
        try
        {
            var frame = BitmapFrame.Create(new Uri(picture), BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
            var scale = Math.Min(1.0, (double)MaxPicturePx / Math.Max(frame.PixelWidth, frame.PixelHeight));
            BitmapSource scaled = scale < 1 ? new TransformedBitmap(frame, new ScaleTransform(scale, scale)) : frame;
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(scaled));
            Directory.CreateDirectory(AppPaths.IconsDirectory);
            var fileName = $"{item.Id}-{Guid.NewGuid():N}.png";
            using (var file = File.Create(Path.Combine(AppPaths.IconsDirectory, fileName))) encoder.Save(file);
            return item with { Icon = new ItemIcon { Image = fileName } };
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Warning(failure, "picture {Picture} could not be used as an icon", picture);
            MessageBox.Show(owner, $"NeoFences could not use this picture as the icon:\n{picture}", "NeoFences", MessageBoxButton.OK, MessageBoxImage.Warning);
            return item;
        }
    }

    /// <summary>
    /// At start: pictures in the icons folder that neither an item nor a snapshot uses (replaced, or their item removed)
    /// go — a snapshot's pictures stay, so restoring it brings them back (final review I7). NeoFences' own files only.
    /// </summary>
    private static void CleanUnusedPictures(IReadOnlySet<string> itemPictures, string snapshotsDirectory) =>
        Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(AppPaths.IconsDirectory)) return;
                var snapshots = new SnapshotStore(snapshotsDirectory);
                var inUse = itemPictures.ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in snapshots.List())
                {
                    if (snapshots.Load(entry.Path) is { } snapshot) inUse.UnionWith(ItemEdits.ImagesInUse(new ItemsDocument { Fences = snapshot.Items }));
                }
                foreach (var picture in Directory.GetFiles(AppPaths.IconsDirectory, "*.png").Where(path => !inUse.Contains(Path.GetFileName(path))))
                {
                    File.Delete(picture);
                    Log.Information("unused item picture {Picture} deleted", Path.GetFileName(picture));
                }
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                Log.Warning(failure, "unused item pictures could not be cleaned up"); // tried again at the next start
            }
        });

    /// <summary>After any item change: windows, save, watching, and a check of the targets that are new.</summary>
    private void ItemsChanged(IReadOnlyList<string> checkTargets)
    {
        PinFreeCells(); // M24: new elements of Free fences keep the spot they show at
        RefreshWindows();
        ScheduleSave();
        UpdateWatching();
        ForgetGoneTargets();
        UpdateLibrary(); // M22: the scan runs while a fence holds a game item
        if (checkTargets.Count > 0) CheckTargets(checkTargets);
    }

    // ---------- drops and drags (spec §2) ----------

    /// <summary>Items dragged from a fence: moved here (Ctrl: duplicated). Library games dragged in become items of their shortcut.</summary>
    private void OnItemsDropped(FenceWindow window, IReadOnlyList<string> keys, int insertAt, bool duplicate)
    {
        if (window.Kind != FenceKind.Items) return;
        var known = keys.Where(key => _items.Find(key) is not null).ToList();
        var fromLibrary = keys.Except(known).ToList(); // the library's and a view's key is a path (M12, M21): they become items
        if (known.Count > 0)
        {
            if (duplicate)
            {
                var copies = ItemEdits.Duplicate(_items, known, window.FenceId, insertAt);
                _items = copies.Document;
                PlaceDropped(window, copies.NewIds, known); // M24: a Free fence puts them on the drop cell
            }
            else
            {
                _items = ItemEdits.Move(_items, known, window.FenceId, insertAt);
                PlaceDropped(window, known, known);
            }
            Log.Information("{Count} item(s) {Action} to fence {FenceId}", known.Count, duplicate ? "duplicated" : "moved", window.FenceId);
        }
        if (fromLibrary.Count > 0) AddTargets(window, fromLibrary, insertAt);
        else ItemsChanged(checkTargets: []);
    }

    /// <summary>Files, folders or a link from outside: new items at the drop point; the originals stay where they are.</summary>
    private void OnTargetsDropped(FenceWindow window, IReadOnlyList<string> targets, int insertAt)
    {
        if (window.Kind == FenceKind.Items && targets.Count > 0) AddTargets(window, targets, insertAt);
    }

    private void AddTargets(FenceWindow window, IReadOnlyList<string> targets, int insertAt)
    {
        var added = ItemEdits.Add(_items, window.FenceId, [.. targets.Select(VirtualItem.Create)], insertAt);
        _items = added.Document;
        PlaceDropped(window, added.AddedIds, [.. added.AddedIds.Select(_ => (string?)null)]); // M24: a Free fence puts them on the drop cell
        Log.Information("{Added} item(s) added to fence {FenceId}; {Already} already there", added.AddedIds.Count, window.FenceId, added.AlreadyThereIds.Count);
        ItemsChanged(checkTargets: targets);
        if (added.AlreadyThereIds.Count > 0) window.SelectItems(added.AlreadyThereIds); // already in this fence: it flashes
    }

    /// <summary>Dragging items out (spec §2): other apps get the files (copied, never moved) or the websites; fences get the items.</summary>
    private void DragItems(FenceWindow window, IReadOnlyList<string> keys)
    {
        IReadOnlyList<string> files, urls;
        if (window.IsLibrary)
        {
            (files, urls) = (keys, []); // NeoFences' own shortcuts (M12)
        }
        else if (window.Kind == FenceKind.View)
        {
            // A view's entries are its folder's: asked once now, at most 2 s (M19 R2), so a share gone since the listing cannot freeze the drag.
            var folder = Path.GetDirectoryName(keys[0]) ?? keys[0];
            if (TargetProbe.Check(folder).State != TargetState.Ok)
            {
                Log.Information("drag from folder view: {Folder} is not reachable", folder);
                return;
            }
            (files, urls) = (keys, []);
        }
        else
        {
            var items = keys.Select(_items.Find).OfType<VirtualItem>().ToList();
            var onDisk = items.Where(item => item.Kind == ItemKind.Path && TargetChecks.RootOf(item.Target) is not null && CheckOf(item.Target).State == TargetState.Ok)
                .Select(item => item.Target).ToList();
            // Asked again now, at most 2 s per drive (M19 R2): Windows parses each one on this thread, and a share that went
            // away since its last check would freeze the fence. A target that does not answer is left out of the drag.
            var answers = onDisk.Count > 0 ? TargetProbe.CheckAll(onDisk) : [];
            files = [.. answers.Where(answer => answer.Check.State == TargetState.Ok).Select(answer => answer.Target)];
            foreach (var (target, _) in answers.Where(answer => answer.Check.State != TargetState.Ok)) Log.Information("{Target} left out of the drag: not reachable", target);
            urls = [.. items.Where(item => item.Kind == ItemKind.Website).Select(item => item.Target)];
        }
        ShellDragDrop.TryDrag(window.Handle, keys, files, urls, logFailure: failure => Log.Warning(failure, "could not start dragging {Keys}", keys));
    }

    // ---------- sort ----------

    /// <summary>"Sort by" (one time): by the names shown, type or date; dragging keeps working afterwards.</summary>
    private void SortFence(FenceWindow window, FenceSort sort)
    {
        if (window.IsLibrary) return;
        if (window.Kind == FenceKind.View)
        {
            SortView(window, sort); // M21: the view's own sort, kept
            return;
        }
        var fenceId = window.FenceId;
        var items = _items.Of(fenceId);
        // Targets not seen OK are sorted without asking their disk (a dead share answers only after its timeout).
        var reachable = items.Where(item => CheckOf(item.Target).State == TargetState.Ok).Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        // Off the UI thread: the targets' facts come from their disks (final review I2).
        Task.Run(() => ItemSorting.Order([.. items.Select(item => FactsOf(item, askDisk: reachable.Contains(item.Id)))], sort)).ContinueWith(sorted =>
        {
            try
            {
                _items = ItemEdits.Reorder(_items, fenceId, sorted.Result); // the fence changed meanwhile: ArgumentException, its order stays
                PackFreeFence(window); // M24: a Free fence is laid out packed in the new order
            }
            catch (Exception failure) when (failure is ArgumentException or AggregateException)
            {
                Log.Warning(failure, "sort of fence {FenceId} failed; its order stays", fenceId); // never crash (M4 review I3)
                return;
            }
            RefreshWindows();
            ScheduleSave();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>What sorting needs about one item: its target's facts (a file on disk), its own name first.</summary>
    private static ItemInfo FactsOf(VirtualItem item, bool askDisk)
    {
        if (askDisk && item.Kind == ItemKind.Path && TargetChecks.RootOf(item.Target) is not null)
        {
            var described = FolderItems.Describe([item.Target])[0];
            return described with { ItemRef = item.Id, Name = item.OwnName ?? described.Name };
        }
        return new ItemInfo(item.Id, DisplayName(item), IsFolder: item.Kind == ItemKind.Special, TypeName: item.Kind == ItemKind.Website ? "URL" : "",
            DateTimeOffset.MinValue);
    }
}
