using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NeoFences.Core.Items;
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
        if (window.IsLibrary) OpenItem(key, ownerHandle: window.Handle); // the library's key is its shortcut's path
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
        var question = new MissingItemWindow(DisplayName(item), item.Target, state) { Owner = window };
        if (question.ShowDialog() != true) return;
        if (question.Choice == MissingItemChoice.Locate) Locate(window, itemId);
        else if (question.Choice == MissingItemChoice.Remove) RemoveItems(window, [itemId]);
    }

    private static string DisplayName(VirtualItem item) => item.OwnName ?? item.Kind switch
    {
        ItemKind.Website => ItemKinds.WebsiteName(item.Target),
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
            Command($"Remove {items.Count} items from fence", () => RemoveItems(window, [.. items.Select(item => item.Id)]));
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
            Command("Copy path", () => CopyText(item.Target));
            menu.Items.Add(new Separator());
            Command("Properties…", () => ShowProperties(window, item.Id, focusName: false));
            if (check.State == TargetState.Ok) Command("Remove from fence", () => RemoveItems(window, [item.Id]));
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "Shift+right-click: Windows' menu", IsEnabled = false });
        }
        window.ShowItemMenu(menu, fromKeyboard);
    }

    /// <summary>Windows' full menu for the real target (spec §3): only here can a real delete or rename happen, and it says so.</summary>
    private static void ShowWindowsMenu(FenceWindow window, VirtualItem item, int screenX, int screenY)
    {
        if (item.Kind == ItemKind.Website) return; // a website has no Windows menu
        ShellItemMenu.Show(window.Handle, [item.Target], screenX, screenY, extended: true,
            logFailure: failure => Log.Warning(failure, "Windows' menu or its command failed for {Target}", item.Target),
            header: "Windows menu — acts on the real file", customCommands: [], handDeleteBack: false, out _);
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
        if (keys.Count > 1 && MessageBox.Show(window, $"Remove {keys.Count} items from this fence?\n\nYour files, folders and apps are not touched.",
                "NeoFences", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        _items = ItemEdits.Remove(_items, keys.ToHashSet(StringComparer.Ordinal));
        Log.Information("{Count} item(s) removed from fence {FenceId}", keys.Count, window.FenceId);
        ItemsChanged(checkTargets: []);
    }

    /// <summary>Properties (F2: the name selected; Alt+Enter; the menu): changes only the item (spec §3).</summary>
    private void ShowProperties(FenceWindow window, string key, bool focusName)
    {
        if (window.IsLibrary || _items.Find(key) is not { } item) return;
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
        if (window.IsLibrary) return;
        var dialog = new ItemPropertiesWindow(VirtualItem.Create(""), adding: true, iconLoader: _iconLoader, focusName: false) { Owner = window };
        if (dialog.ShowDialog() != true || dialog.Result is not { } created) return;
        var added = ItemEdits.Add(_items, window.FenceId, [WithCopiedPicture(window, created, dialog.PictureToCopy)]);
        _items = added.Document;
        Log.Information("item added to fence {FenceId}: {Target}", window.FenceId, created.Target);
        ItemsChanged(checkTargets: [created.Target]);
        window.SelectItems(added.AddedIds.Count > 0 ? added.AddedIds : added.AlreadyThereIds); // already there: it flashes
    }

    /// <summary>Locate… (spec §4): a picker at the nearest folder that still exists; only the target changes.</summary>
    private void Locate(FenceWindow window, string itemId)
    {
        if (_items.Find(itemId) is not { } item) return;
        var name = DisplayName(item);
        var asFolder = !Path.HasExtension(item.Target.TrimEnd('\\')); // ponytail: a folder named "x.y" opens the file picker; Properties → Browse covers it
        // Off the UI thread: the old drive may be a sleeping disk or a share that does not answer.
        Task.Run(() => NearestExistingFolder(item.Target)).ContinueWith(found =>
        {
            void LogFailure(Exception failure) => Log.Warning(failure, "Locate… dialog failed");
            var picked = asFolder
                ? PathPicker.TryPickFolder(window.Handle, $"Where is \"{name}\" now?", LogFailure, found.Result)
                : PathPicker.TryPickFile(window.Handle, $"Where is \"{name}\" now?", LogFailure, found.Result);
            if (picked is null || _items.Find(itemId) is not { } current) return;
            _items = ItemEdits.Replace(_items, current with { Target = picked });
            Log.Information("item {ItemId} located at {Target}", itemId, picked);
            ItemsChanged(checkTargets: [picked]);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static string? NearestExistingFolder(string target)
    {
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

    /// <summary>At start: pictures in the icons folder no item uses any more (replaced, or their item removed) go. NeoFences' own files only.</summary>
    private static void CleanUnusedPictures(IReadOnlySet<string> inUse) =>
        Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(AppPaths.IconsDirectory)) return;
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
        RefreshWindows();
        ScheduleSave();
        UpdateWatching();
        if (checkTargets.Count > 0) CheckTargets(checkTargets);
    }

    // ---------- drops and drags (spec §2) ----------

    /// <summary>Items dragged from a fence: moved here (Ctrl: duplicated). Library games dragged in become items of their shortcut.</summary>
    private void OnItemsDropped(FenceWindow window, IReadOnlyList<string> keys, int insertAt, bool duplicate)
    {
        if (window.IsLibrary) return;
        var known = keys.Where(key => _items.Find(key) is not null).ToList();
        var fromLibrary = keys.Except(known).ToList(); // the library's key is its shortcut's path (M12)
        if (known.Count > 0)
        {
            _items = duplicate ? ItemEdits.Duplicate(_items, known, window.FenceId, insertAt).Document : ItemEdits.Move(_items, known, window.FenceId, insertAt);
            Log.Information("{Count} item(s) {Action} to fence {FenceId}", known.Count, duplicate ? "duplicated" : "moved", window.FenceId);
        }
        if (fromLibrary.Count > 0) AddTargets(window, fromLibrary, insertAt);
        else ItemsChanged(checkTargets: []);
    }

    /// <summary>Files, folders or a link from outside: new items at the drop point; the originals stay where they are.</summary>
    private void OnTargetsDropped(FenceWindow window, IReadOnlyList<string> targets, int insertAt)
    {
        if (!window.IsLibrary && targets.Count > 0) AddTargets(window, targets, insertAt);
    }

    private void AddTargets(FenceWindow window, IReadOnlyList<string> targets, int insertAt)
    {
        var added = ItemEdits.Add(_items, window.FenceId, [.. targets.Select(VirtualItem.Create)], insertAt);
        _items = added.Document;
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
        else
        {
            var items = keys.Select(_items.Find).OfType<VirtualItem>().ToList();
            files = [.. items.Where(item => item.Kind == ItemKind.Path && TargetChecks.RootOf(item.Target) is not null && CheckOf(item.Target).State == TargetState.Ok)
                .Select(item => item.Target)];
            urls = [.. items.Where(item => item.Kind == ItemKind.Website).Select(item => item.Target)];
        }
        ShellDragDrop.TryDrag(window.Handle, keys, files, urls, logFailure: failure => Log.Warning(failure, "could not start dragging {Keys}", keys));
    }

    // ---------- sort ----------

    /// <summary>"Sort by" (one time): by the names shown, type or date; dragging keeps working afterwards.</summary>
    private void SortFence(FenceWindow window, FenceSort sort)
    {
        if (window.IsLibrary) return;
        try
        {
            // ponytail: reads each target's facts on the UI thread (a sleeping disk stalls the sort); off-thread if fences get big.
            var facts = _items.Of(window.FenceId).Select(FactsOf).ToList();
            _items = ItemEdits.Reorder(_items, window.FenceId, ItemSorting.Order(facts, sort));
        }
        catch (Exception failure) when (failure is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Warning(failure, "sort of fence {FenceId} failed; its order stays", window.FenceId); // never crash (M4 review I3)
            return;
        }
        RefreshWindow(window);
        ScheduleSave();
    }

    /// <summary>What sorting needs about one item: its target's facts (a file on disk), its own name first.</summary>
    private static ItemInfo FactsOf(VirtualItem item)
    {
        if (item.Kind == ItemKind.Path && TargetChecks.RootOf(item.Target) is not null)
        {
            var described = FolderItems.Describe([item.Target])[0];
            return described with { ItemRef = item.Id, Name = item.OwnName ?? described.Name };
        }
        return new ItemInfo(item.Id, DisplayName(item), IsFolder: item.Kind == ItemKind.Special, TypeName: item.Kind == ItemKind.Website ? "URL" : "",
            DateTimeOffset.MinValue);
    }
}
