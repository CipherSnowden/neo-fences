using NeoFences.Core.Items;
using NeoFences.Core.Model;

namespace NeoFences.Core.Library;

/// <summary>
/// Games as items (M22, spec 2026-10-05-games-as-items-design, ADR-045): a game item is a virtual item whose target is
/// NeoFences' own shortcut for the game in its library folder, with the game's id. The library scan keeps those shortcuts;
/// these rules keep the items pointing at them. Pure; none of them touches a file.
/// </summary>
public static class GameItems
{
    public static bool IsGame(VirtualItem item) => item.GameId is not null;

    /// <summary>A game item shows its cover tile unless set to an icon.</summary>
    public static bool ShowsCover(VirtualItem item) => IsGame(item) && item.ShowAs != ItemShow.Icon;

    public static VirtualItem Create(LibraryItem game, string libraryFolder) =>
        VirtualItem.Create(Path.Combine(libraryFolder, game.FileName)) with { GameId = game.Game.Id };

    /// <param name="MigratedFenceIds">Fences that were Game Library fences and now hold game items.</param>
    public sealed record Migration(NeoFencesConfig Config, ItemsDocument Items, IReadOnlyList<string> MigratedFenceIds);

    /// <summary>
    /// Every Game Library fence becomes an items fence with one game item per game, in the library's order, and becomes the
    /// new-games fence when none is chosen. A library that has no games yet (or whose index could not be read) waits.
    /// </summary>
    public static Migration Migrate(NeoFencesConfig config, ItemsDocument items, LibraryState library, string libraryFolder)
    {
        var libraryFences = config.Fences.Where(fence => fence.IsLibrary).ToList();
        if (libraryFences.Count == 0 || library.Items.Count == 0) return new Migration(config, items, []);
        foreach (var fence in libraryFences)
        {
            config = config.WithFence(fence with { IsLibrary = false });
            items = items.With(fence.Id, [.. items.Of(fence.Id), .. library.Items.Select(game => Create(game, libraryFolder))]);
        }
        if (config.Library.NewGamesFence is null) config = config with { Library = config.Library with { NewGamesFence = libraryFences[0].Id } };
        return new Migration(config, items, [.. libraryFences.Select(fence => fence.Id)]);
    }

    /// <summary>Games in <paramref name="current"/> that no game of <paramref name="previous"/> had an id of (merged ids count).</summary>
    public static IReadOnlyList<LibraryItem> NewGames(LibraryState previous, LibraryState current)
    {
        var known = previous.Items.SelectMany(item => GameCatalog.IdsOf(item.Game)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. current.Items.Where(item => !GameCatalog.IdsOf(item.Game).Any(known.Contains))];
    }

    /// <summary>New games at the end of a fence; a game the fence already holds (by id) is not added again.</summary>
    public static ItemsAdded AddNew(ItemsDocument items, string fenceId, IReadOnlyList<LibraryItem> games, string libraryFolder)
    {
        var held = items.Of(fenceId).Where(IsGame).Select(item => item.GameId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ItemEdits.Add(items, fenceId, [.. games.Where(game => !GameCatalog.IdsOf(game.Game).Any(held.Contains)).Select(game => Create(game, libraryFolder))]);
    }

    /// <summary>
    /// Every game item points at its game's current shortcut (a renamed game's file, a merged id). A game no longer in the
    /// library keeps its target: its file goes, so it shows Missing. The same document when nothing changed.
    /// </summary>
    public static ItemsDocument Retarget(ItemsDocument items, LibraryState library, string libraryFolder)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in library.Items)
        {
            foreach (var id in GameCatalog.IdsOf(game.Game)) files.TryAdd(id, Path.Combine(libraryFolder, game.FileName));
        }
        var changed = false;
        var fences = new Dictionary<string, IReadOnlyList<VirtualItem>>();
        foreach (var (fenceId, list) in items.Fences)
        {
            fences[fenceId] = [.. list.Select(item =>
            {
                if (item.GameId is not { } gameId || !files.TryGetValue(gameId, out var file) || ItemKinds.Comparer.Equals(file, item.Target)) return item;
                changed = true;
                return item with { Target = file };
            })];
        }
        return changed ? items with { Fences = fences } : items;
    }
}
