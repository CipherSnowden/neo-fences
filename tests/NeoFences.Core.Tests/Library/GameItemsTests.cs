using NeoFences.Core.Config;
using NeoFences.Core.Items;
using NeoFences.Core.Library;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Library;

/// <summary>M22 (0.12.0): games become items — one kind of fence (spec 2026-10-05-games-as-items-design).</summary>
public class GameItemsTests
{
    private const string Folder = @"C:\Data\NeoFences\library";

    private static LibraryItem Game(string id, string name, params string[] otherIds) =>
        new(new GameEntry(id, name, GameSource.Steam, "steam", new GameLaunch($"steam://rungameid/{id}")) { OtherIds = otherIds }, $"{name}.url", "sig");

    private static LibraryState State(params LibraryItem[] games) => new() { Items = games };

    private static (NeoFencesConfig Config, Fence Library) WithLibraryFence()
    {
        var library = Fence.Create("Games", isLibrary: true) with { IconSize = 64 };
        return (NeoFencesConfig.CreateDefault() with { Fences = [library] }, library);
    }

    [Fact]
    public void Migrate_TurnsTheLibraryFenceIntoAnItemsFence_WithOneGameItemPerGame_InOrder()
    {
        var (config, library) = WithLibraryFence();
        var migration = GameItems.Migrate(config, new ItemsDocument(), State(Game("steam:1", "Blur"), Game("steam:2", "Hades")), Folder);

        var fence = migration.Config.Fences.Single();
        Assert.Equal(FenceKind.Items, fence.Kind);
        Assert.Equal(("Games", 64), (fence.Title, fence.IconSize));
        Assert.Equal([library.Id], migration.MigratedFenceIds);
        var items = migration.Items.Of(library.Id);
        Assert.Equal([@"C:\Data\NeoFences\library\Blur.url", @"C:\Data\NeoFences\library\Hades.url"], items.Select(item => item.Target));
        Assert.Equal(["steam:1", "steam:2"], items.Select(item => item.GameId));
        Assert.All(items, item => Assert.True(GameItems.ShowsCover(item)));
        Assert.Equal(library.Id, migration.Config.Library.NewGamesFence);
    }

    [Fact]
    public void Migrate_KeepsAChosenNewGamesFence_AndLeavesConfigsWithoutALibraryFenceAlone()
    {
        var (config, _) = WithLibraryFence();
        var other = Fence.Create("Mine");
        config = config with { Fences = [.. config.Fences, other], Library = config.Library with { NewGamesFence = other.Id } };
        Assert.Equal(other.Id, GameItems.Migrate(config, new ItemsDocument(), State(Game("steam:1", "Blur")), Folder).Config.Library.NewGamesFence);

        var plain = NeoFencesConfig.CreateDefault() with { Fences = [Fence.Create("a")] };
        var items = new ItemsDocument();
        var untouched = GameItems.Migrate(plain, items, State(Game("steam:1", "Blur")), Folder);
        Assert.Same(plain, untouched.Config);
        Assert.Same(items, untouched.Items);
        Assert.Empty(untouched.MigratedFenceIds);
    }

    [Fact]
    public void Migrate_WaitsWhileTheLibraryHasNoGamesYet()
    {
        var (config, _) = WithLibraryFence(); // an unreadable index reads as empty: the fence must not become an empty items fence
        var migration = GameItems.Migrate(config, new ItemsDocument(), State(), Folder);
        Assert.Same(config, migration.Config);
        Assert.Empty(migration.MigratedFenceIds);
    }

    [Fact]
    public void Migrate_DoesNotDuplicateGamesTheFenceAlreadyHolds()
    {
        // A save cut short (power loss, a read-only items.json) leaves game items under a fence that is still a library
        // fence: the next migration must not add every game twice (final review I4).
        var (config, library) = WithLibraryFence();
        var items = new ItemsDocument().With(library.Id, [GameItems.Create(Game("steam:1", "Blur"), Folder)]);
        var migration = GameItems.Migrate(config, items, State(Game("steam:1", "Blur"), Game("steam:2", "Hades")), Folder);
        Assert.Equal(["steam:1", "steam:2"], migration.Items.Of(library.Id).Select(item => item.GameId));
    }

    [Fact]
    public void NewGames_OnAFirstScan_AreNone()
    {
        // No earlier scan (never used, or an unreadable index): the whole library is not "new" (final review I3).
        Assert.Empty(GameItems.NewGames(State(), State(Game("steam:1", "Blur"), Game("epic:7", "Alan Wake 2"))));
    }

    [Fact]
    public void NotInAnyFence_DropsGamesSomeFenceAlreadyHolds()
    {
        // A game that comes back (a drive plugged in again, Show again) is not copied into the new-games fence when the
        // user keeps it elsewhere (final review I5).
        var apps = Fence.NewId();
        var items = new ItemsDocument().With(apps, [GameItems.Create(Game("folder:hades", "Hades"), Folder)]);
        var back = GameItems.NotInAnyFence(items, [Game("steam:9", "Hades", "folder:hades"), Game("epic:7", "Alan Wake 2")]);
        Assert.Equal(["epic:7"], back.Select(game => game.Game.Id));
    }

    [Fact]
    public void Retarget_UpdatesEveryCopyOfAGame_InEveryFence()
    {
        var games = Fence.NewId();
        var apps = Fence.NewId();
        var blur = GameItems.Create(Game("steam:1", "Blur"), Folder);
        var items = new ItemsDocument().With(games, [blur]).With(apps, [blur with { Id = VirtualItem.NewId(), ShowAs = ItemShow.Icon }]);
        var result = GameItems.Retarget(items, State(Game("steam:1", "Blur (2010)")), Folder);
        Assert.All(result.Fences.Values.SelectMany(list => list), item => Assert.EndsWith("Blur (2010).url", item.Target));
    }

    [Fact]
    public void NewGames_AreTheOnesNoEarlierGameHadAnIdOf()
    {
        var previous = State(Game("steam:1", "Blur"), Game("folder:hades", "Hades"));
        var current = State(Game("steam:1", "Blur"), Game("steam:9", "Hades", "folder:hades"), Game("epic:7", "Alan Wake 2"));
        Assert.Equal(["epic:7"], GameItems.NewGames(previous, current).Select(game => game.Game.Id));
        Assert.Empty(GameItems.NewGames(current, current));
    }

    [Fact]
    public void AddNew_AppendsGameItems_ButNeverASecondItemForTheSameGame()
    {
        var fenceId = Fence.NewId();
        var start = new ItemsDocument().With(fenceId, [VirtualItem.Create(@"C:\x.txt"), GameItems.Create(Game("steam:1", "Blur"), Folder)]);
        var added = GameItems.AddNew(start, fenceId, [Game("steam:1", "Blur"), Game("epic:7", "Alan Wake 2")], Folder);
        Assert.Equal([null, "steam:1", "epic:7"], added.Document.Of(fenceId).Select(item => item.GameId));
        Assert.Single(added.AddedIds);
    }

    [Fact]
    public void Retarget_PointsGameItemsAtTheirGamesCurrentShortcut_MatchingMergedIds()
    {
        var fenceId = Fence.NewId();
        var blur = GameItems.Create(Game("steam:1", "Blur"), Folder);
        var hades = GameItems.Create(Game("folder:hades", "Hades"), Folder);
        var gone = GameItems.Create(Game("steam:5", "Old"), Folder);
        var plain = VirtualItem.Create(@"C:\notes.txt");
        var items = new ItemsDocument().With(fenceId, [blur, hades, gone, plain]);

        var renamed = State(Game("steam:1", "Blur (2010)"), Game("steam:9", "Hades", "folder:hades"));
        var result = GameItems.Retarget(items, renamed, Folder);
        Assert.Equal([@"C:\Data\NeoFences\library\Blur (2010).url", @"C:\Data\NeoFences\library\Hades.url", gone.Target, plain.Target],
            result.Of(fenceId).Select(item => item.Target));
        Assert.Same(result, GameItems.Retarget(result, renamed, Folder)); // nothing changed: the same document
    }

    [Fact]
    public void ShowsCover_IsForGameItemsUnlessSetToIcon()
    {
        var game = GameItems.Create(Game("steam:1", "Blur"), Folder);
        Assert.True(GameItems.ShowsCover(game));
        Assert.False(GameItems.ShowsCover(game with { ShowAs = ItemShow.Icon }));
        Assert.False(GameItems.ShowsCover(VirtualItem.Create(@"C:\a.exe") with { ShowAs = ItemShow.Cover }));
    }

    [Fact]
    public void NewGamesFence_IsDroppedWhenItsFenceIsDeleted_OrIsAFolderView_OrDoesNotExist()
    {
        var items = Fence.Create("Games");
        var view = Fence.Create("Downloads") with { View = new FolderView { Path = @"C:\Downloads" } };
        var config = NeoFencesConfig.CreateDefault() with { Fences = [items, view], Library = new LibrarySettings { NewGamesFence = items.Id } };
        Assert.Equal(items.Id, ConfigNormalizer.Normalize(config).Library.NewGamesFence);
        Assert.Null(FenceEdits.DeleteFence(config, items.Id).Library.NewGamesFence);
        Assert.Null(ConfigNormalizer.Normalize(config with { Library = new LibrarySettings { NewGamesFence = view.Id } }).Library.NewGamesFence);
        Assert.Null(ConfigNormalizer.Normalize(config with { Library = new LibrarySettings { NewGamesFence = "nope" } }).Library.NewGamesFence);
    }

    [Fact]
    public void ItemsJson_KeepsGameFields_WritesNoShowAsWhenUnset_AndRepairsATypo()
    {
        var fenceId = Fence.NewId();
        var game = GameItems.Create(Game("steam:1", "Blur"), Folder) with { ShowAs = ItemShow.Icon };
        var plain = VirtualItem.Create(@"C:\a.txt");
        var json = ConfigJson.SerializeItems(new ItemsDocument().With(fenceId, [game, plain]));
        Assert.Equal(1, json.Split("showAs").Length - 1);
        var back = ConfigJson.DeserializeItems(json).Of(fenceId);
        Assert.Equal(("steam:1", ItemShow.Icon), (back[0].GameId, back[0].ShowAs));
        var typo = ConfigJson.DeserializeItems(json.Replace("\"icon\"", "\"poster\""));
        Assert.True(GameItems.ShowsCover(typo.Of(fenceId)[0])); // an unknown value reads as the default: a cover
    }
}
