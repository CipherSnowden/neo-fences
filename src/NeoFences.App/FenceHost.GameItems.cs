using System.Windows.Controls;
using NeoFences.Core.Items;
using NeoFences.Core.Library;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Games as items (M22, spec 2026-10-05-games-as-items-design, ADR-045): one kind of fence. A game is a virtual item
/// pointing at NeoFences' own shortcut for it; the library scan (FenceHost.Library) is the engine that finds games. Old
/// Game Library fences become items fences once (a snapshot first); new games go to the chosen fence.
/// </summary>
public sealed partial class FenceHost
{
    private AddGamesWindow? _addGamesWindow; // while open, the library scans even with no game items yet

    /// <summary>
    /// A Game Library fence becomes an items fence with one game item per game — once its library has games. A snapshot
    /// is saved first; without it nothing changes (tried again next time). True when something changed.
    /// </summary>
    private bool MigrateGames(LibraryState library)
    {
        if (!_config.Fences.Any(fence => fence.IsLibrary) || library.Items.Count == 0) return false;
        if (_configReadOnly || _itemsReadOnly)
        {
            // Half a migration saved (one file read-only) would empty the fence or double its games (final review I4).
            Log.Information("games not made into items: config.json or items.json is read-only this session");
            return false;
        }
        var now = DateTimeOffset.Now;
        if (_snapshots.Save(Snapshots.Take(_config, _items, name: $"Before games became items ({now:d MMM HH:mm})", now: now)) is null)
        {
            Log.Warning(_snapshots.LastFailure, "games not made into items: the snapshot before it could not be saved; tried again next time");
            return false;
        }
        var migration = GameItems.Migrate(_config, _items, library, AppPaths.LibraryDirectory);
        (_config, _items) = (migration.Config, migration.Items);
        Log.Information("Game Library fence(s) {FenceIds} now hold {Count} game items", migration.MigratedFenceIds, library.Items.Count);
        SaveNow(itemsFirst: true); // cut short after the items, the next start migrates again without doubling (final review I4)
        return true;
    }

    /// <summary>After a migration or a restore: windows show their fence's kind again, and the library's own lister goes.</summary>
    private void ShowMigratedFences()
    {
        foreach (var window in _windows.Values)
        {
            if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is { } shown) window.Refresh(shown);
        }
        EnsureLibraryLister();
        RefreshWindows();
        UpdateWatching();
        RefreshSettings();
    }

    /// <summary>
    /// A scan finished: an old library fence becomes game items; game items follow renamed shortcuts; games no earlier scan
    /// had go to the new-games fence.
    /// </summary>
    private void ApplyGames(LibraryState previous, LibraryState current)
    {
        if (MigrateGames(current)) ShowMigratedFences();
        var retargeted = GameItems.Retarget(_items, current, AppPaths.LibraryDirectory);
        var changed = !ReferenceEquals(retargeted, _items);
        _items = retargeted;
        IReadOnlyList<string> added = [];
        // A game some fence already holds is not copied in when it comes back (final review I5).
        if (_config.Library.NewGamesFence is { } fenceId && _config.Fences.Any(fence => fence.Id == fenceId && fence.Kind == FenceKind.Items) // M23: never a gone fence
            && GameItems.NotInAnyFence(_items, GameItems.NewGames(previous, current)) is { Count: > 0 } newGames)
        {
            var result = GameItems.AddNew(_items, fenceId, newGames, AppPaths.LibraryDirectory);
            _items = result.Document;
            added = [.. result.AddedIds.Select(id => _items.Find(id)!.Target)];
            if (added.Count > 0) Log.Information("game library: {Count} new game(s) added to fence {FenceId}", added.Count, fenceId);
        }
        if (changed || added.Count > 0) ItemsChanged(checkTargets: added);
        else RefreshWindows(); // new covers
        // Shortcuts the scan rewrote were not marked missing while it ran (M23): every game item is checked again now.
        CheckTargets([.. _items.Fences.Values.SelectMany(items => items).Where(GameItems.IsGame).Select(item => item.Target).Distinct(ItemKinds.Comparer)]);
        _addGamesWindow?.ShowGames(GamesForAdding(_addGamesWindow.FenceId));
    }

    /// <summary>Fence menu → "Add games…": every game the scan found; those already in this fence unticked.</summary>
    private void AddGames(FenceWindow window)
    {
        if (window.Kind != FenceKind.Items || _addGamesWindow is not null) return;
        var fenceId = window.FenceId;
        var dialog = new AddGamesWindow(fenceId) { Owner = window };
        _addGamesWindow = dialog;
        UpdateLibrary(); // the scan runs while the list is open
        if (_library.Items.Count == 0) _library = LibraryWriter.ReadIndex(AppPaths.LibraryDirectory); // the last scan, until this one finishes
        dialog.ShowGames(GamesForAdding(fenceId), scanning: _libraryScanning || _library.Items.Count == 0);
        var accepted = dialog.ShowDialog() == true;
        _addGamesWindow = null;
        if (accepted && _config.Fences.Any(fence => fence.Id == fenceId && fence.Kind == FenceKind.Items) && dialog.Chosen.Count > 0)
        {
            var result = GameItems.AddNew(_items, fenceId, dialog.Chosen, AppPaths.LibraryDirectory);
            _items = result.Document;
            Log.Information("{Count} game(s) added to fence {FenceId}", result.AddedIds.Count, fenceId);
            ItemsChanged(checkTargets: [.. result.AddedIds.Select(id => _items.Find(id)!.Target)]);
            if (_windows.Values.FirstOrDefault(shown => shown.FenceId == fenceId) is { } target) target.SelectItems(result.AddedIds);
        }
        UpdateLibrary();
    }

    private IReadOnlyList<AddGamesWindow.Row> GamesForAdding(string fenceId)
    {
        var here = _items.Of(fenceId).Where(GameItems.IsGame).Select(item => item.GameId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. _library.Items.Select(game => new AddGamesWindow.Row(game, game.Game.Poster, AlreadyHere: GameCatalog.IdsOf(game.Game).Any(here.Contains)))];
    }

    /// <summary>A game item's menu (spec §2): Open, Show as, Open install folder, Copy path, Properties, Remove.</summary>
    private void ShowGameItemMenu(FenceWindow window, VirtualItem item, bool fromKeyboard)
    {
        var menu = new ContextMenu();
        // M35 (spec §1): icons; Open first, the game's own actions, Properties…, Remove last in red.
        void Command(ItemsControl parent, string header, string? glyph, Action run, bool? isChecked = null, bool danger = false) =>
            parent.Items.Add(MenuGlyph.Entry(header, glyph, run, isChecked, danger));
        var installed = CheckOf(item.Target).State == TargetState.Ok;
        Command(menu, "Open", MenuGlyph.Run, () => OpenVirtualItem(window, item, runAsAdmin: false));
        var showAs = MenuGlyph.Entry("Show as", MenuGlyph.ShowAs);
        Command(showAs, "Cover tile", null, () => SetShowAs(item.Id, ItemShow.Cover), isChecked: GameItems.ShowsCover(item));
        Command(showAs, "Icon", null, () => SetShowAs(item.Id, ItemShow.Icon), isChecked: !GameItems.ShowsCover(item));
        menu.Items.Add(showAs);
        if (GameItems.ShowsCover(item)) Command(menu, "Choose cover…", MenuGlyph.Cover, () => ChooseCover(item)); // M34
        menu.Items.Add(GameItems.ShowsCover(item) ? CoverSizeMenu([item]) : SizeMenu(menu, [item])); // M24; M34: Normal / Large covers
        menu.Items.Add(MenuGlyph.Entry("Open install folder", MenuGlyph.OpenFolder, () => OpenInstallFolder(window, item.Target),
            enabled: installed && LibraryItemOf(item.Target)?.Game.InstallFolder is not null)); // M23
        Command(menu, "Copy path", MenuGlyph.Copy, () => CopyText(item.Target));
        menu.Items.Add(new Separator());
        Command(menu, "Properties…", MenuGlyph.Properties, () => ShowProperties(window, item.Id, focusName: false));
        Command(menu, "Remove from fence", MenuGlyph.Remove, () => RemoveItems(window, [item.Id]), danger: true);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Shift+right-click: Windows' menu", IsEnabled = false });
        window.ShowItemMenu(menu, fromKeyboard);
    }

    private void SetShowAs(string itemId, ItemShow showAs)
    {
        if (_items.Find(itemId) is not { } item) return;
        _items = ItemEdits.Replace(_items, item with { ShowAs = showAs == ItemShow.Cover ? null : showAs }); // a cover is the usual look
        ItemsChanged(checkTargets: []);
    }
}
