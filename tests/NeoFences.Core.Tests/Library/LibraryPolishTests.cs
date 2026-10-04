using NeoFences.Core.Config;
using NeoFences.Core.Library;
using NeoFences.Core.Model;
using NeoFences.Core.Tests.TestSupport;

namespace NeoFences.Core.Tests.Library;

/// <summary>M13b (v1.6.1): Game Library and rules polish — carry-overs from the M11–M13a reviews.</summary>
public class LibraryPolishTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private static GameEntry Game(string id, string name, GameSource source, string target, string? folder = null, string? arguments = null) =>
        new(id, name, source, source.ToString().ToLowerInvariant(), new GameLaunch(target, arguments), folder);

    // ---------- Epic: DLC and Unreal Engine are not games ----------

    private static string EpicItem(string categories, string mainGame = "", string appName = "game1") => $$"""
        { "DisplayName": "Thing", "InstallLocation": "D:\\Epic\\Thing", "AppName": "{{appName}}", "MainGameAppName": "{{mainGame}}",
          "CatalogNamespace": "ns", "CatalogItemId": "item", "AppCategories": [ {{categories}} ] }
        """;

    [Fact]
    public void Epic_OnlyGamesAreListed_NotDlcOrEngines()
    {
        Assert.NotNull(EpicManifest.Parse(EpicItem("\"public\", \"games\", \"applications\"")));
        Assert.NotNull(EpicManifest.Parse(EpicItem("\"public\", \"games\"", mainGame: "game1"))); // its own main game
        Assert.Null(EpicManifest.Parse(EpicItem("\"public\", \"games\", \"addons\"", mainGame: "basegame"))); // a DLC of another game
        Assert.Null(EpicManifest.Parse(EpicItem("\"public\", \"engines\""))); // Unreal Engine
        Assert.NotNull(EpicManifest.Parse(EpicItem(""))); // an old manifest without categories: listed as before
    }

    // ---------- a Desktop shortcut into a launcher game's folder is that game ----------

    [Fact]
    public void Catalog_AShortcutIntoALaunchersInstallFolderMergesWithThatGame()
    {
        var steam = Game("steam:570", "Dota 2", GameSource.Steam, "steam://rungameid/570", folder: @"D:\SteamLibrary\steamapps\common\dota 2 beta");
        var shortcut = Game("desktop:dota", "Dota 2", GameSource.DesktopShortcut, @"D:\SteamLibrary\steamapps\common\dota 2 beta\game\bin\win64\dota2.exe",
            folder: @"D:\SteamLibrary\steamapps\common\dota 2 beta\game\bin\win64");
        var elsewhere = Game("desktop:other", "Other", GameSource.DesktopShortcut, @"D:\SteamLibrary\steamapps\common\dota 2 beta-tools\x.exe",
            folder: @"D:\SteamLibrary\steamapps\common\dota 2 beta-tools");

        var merged = GameCatalog.Merge([new SourceScan("steam", true, [steam]), new SourceScan("desktop", true, [shortcut, elsewhere])], [], []);

        Assert.Equal(["steam:570", "desktop:other"], merged.Select(game => game.Id));
        Assert.Contains("desktop:dota", GameCatalog.IdsOf(merged[0]));
    }

    // ---------- rules: steam:// in a shortcut's arguments ----------

    [Theory]
    [InlineData(@"C:\Program Files (x86)\Steam\steam.exe steam://rungameid/570", GameLauncher.Steam)]
    [InlineData(@"C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe com.epicgames.launcher://apps/fn?action=launch", GameLauncher.Epic)]
    [InlineData(@"C:\Tools\steam-helper.exe --profile steam", null)]
    public void LauncherOf_FindsALaunchersLinkInTheArguments(string target, GameLauncher? expected) =>
        Assert.Equal(expected, Rules.LauncherOf(target));

    // ---------- the status line names sources the way people do ----------

    [Theory]
    [InlineData("steam", "Steam")]
    [InlineData(@"steam:d:\steamlibrary", @"Steam library d:\steamlibrary")]
    [InlineData("epic", "Epic")]
    [InlineData("gog", "GOG")]
    [InlineData("ubisoft", "Ubisoft Connect")]
    [InlineData("ea", "EA app")]
    [InlineData("battlenet", "Battle.net")]
    [InlineData("xbox", "Xbox")]
    [InlineData(@"folder:d:\gamelibrary", @"game folder d:\gamelibrary")]
    [InlineData("desktop", "Desktop shortcuts")]
    [InlineData("library", "the library")]
    public void SourceNames_ArePlain(string scanKey, string expected) => Assert.Equal(expected, GameCatalog.SourceName(scanKey));

    // ---------- hidden games are remembered with every id and their name ----------

    [Fact]
    public void HiddenGames_KeepTheirNameAndIds_AlsoWhileTheirSourceCannotBeRead()
    {
        var shortcut = Game("desktop:ac", "AC Black Flag", GameSource.DesktopShortcut, @"D:\GameLibrary\AC\ac.exe", folder: @"D:\GameLibrary\AC");
        var folder = Game(@"folder:d:\gamelibrary\ac", "AC", GameSource.Folder, @"D:\GameLibrary\AC\ac.exe", folder: @"D:\GameLibrary\AC");
        var everything = GameCatalog.Merge([new SourceScan("desktop", true, [shortcut]), new SourceScan("folder", true, [folder])], [], []);
        string[] hidden = ["desktop:ac", @"folder:d:\gamelibrary\ac", "steam:999"];

        var first = GameCatalog.HiddenGames(everything, hidden, previous: []);
        Assert.Equal([("AC Black Flag", 2), ("steam:999", 1)], first.Select(entry => (entry.Name, entry.Ids.Count)));

        // D: unplugged: the game is found nowhere; it still shows once, by name, with both ids (Show again covers both).
        var second = GameCatalog.HiddenGames([], hidden, previous: first);
        Assert.Equal([("AC Black Flag", 2), ("steam:999", 1)], second.Select(entry => (entry.Name, entry.Ids.Count)));
        Assert.Empty(GameCatalog.HiddenGames([], hidden: [], previous: first)); // shown again: forgotten
    }

    [Fact]
    public void HiddenGames_AGameFoundByOnlySomeOfItsSourcesKeepsAllItsIds()
    {
        // final review I1: AC on the Public Desktop and in D:\GameLibrary; D: away → one row, both ids, Show again covers both
        HiddenEntry[] remembered = [new(["desktop:ac", @"folder:d:\gamelibrary\ac"], "AC Black Flag")];
        var shortcutOnly = Game("desktop:ac", "AC Black Flag", GameSource.DesktopShortcut, @"D:\GameLibrary\AC\ac.exe", folder: @"D:\GameLibrary\AC");

        var entries = GameCatalog.HiddenGames([shortcutOnly], ["desktop:ac", @"folder:d:\gamelibrary\ac"], remembered);

        Assert.Equal([("AC Black Flag", 2)], entries.Select(entry => (entry.Name, entry.Ids.Count)));
    }

    [Fact]
    public void Catalog_ALaunchersShortcutIsNeverSwallowedByAnotherGameInItsFolder()
    {
        // final review I2: a GOG Galaxy shortcut (GalaxyClient.exe /gameId=…) must join its own game, not the first GOG game
        const string Galaxy = @"C:\Program Files (x86)\GOG Galaxy\GalaxyClient.exe";
        var cyberpunk = Game("gog:1", "Cyberpunk", GameSource.Gog, Galaxy, folder: @"D:\GOG\Cyberpunk", arguments: "/command=runGame /gameId=1 /path=\"D:\\GOG\\Cyberpunk\"");
        var witcher = Game("gog:2", "Witcher 3", GameSource.Gog, Galaxy, folder: @"D:\GOG\Witcher 3", arguments: "/command=runGame /gameId=2 /path=\"D:\\GOG\\Witcher 3\"");
        var shortcut = Game("desktop:witcher", "Witcher 3", GameSource.DesktopShortcut, Galaxy, folder: @"C:\Program Files (x86)\GOG Galaxy",
            arguments: "/command=runGame /gameId=2 /path=\"D:\\GOG\\Witcher 3\"");

        var merged = GameCatalog.Merge([new SourceScan("gog", true, [cyberpunk, witcher]), new SourceScan("desktop", true, [shortcut])], [], []);

        Assert.Empty(merged.Single(game => game.Id == "gog:1").OtherIds);
        Assert.Equal(["desktop:witcher"], merged.Single(game => game.Id == "gog:2").OtherIds);
    }

    // ---------- the index never names a file that is gone ----------

    [Fact]
    public void LibraryIndex_AFailedRewriteOfAFileThatIsGoneDropsTheEntry()
    {
        var previous = LibraryFiles.Plan(previous: [], games: [Game("1", "A", GameSource.Steam, "steam://rungameid/1")]).Items;
        var plan = LibraryFiles.Plan(previous, games: [Game("1", "A", GameSource.Steam, "steam://rungameid/1") with { Poster = @"C:\a.jpg" }]);

        Assert.Single(LibraryFiles.Settle(previous, plan, failedWrites: ["A.url"], failedDeletes: [], lostFiles: []).Items);
        Assert.Empty(LibraryFiles.Settle(previous, plan, failedWrites: ["A.url"], failedDeletes: [], lostFiles: ["A.url"]).Items);
    }

    // ---------- config: one copy from before schema 2 stays (M13a review: the last one rotated out after 10 days) ----------

    [Fact]
    public void Config_ACopyFromBeforeSchemaTwoIsKeptOnce_OutsideTheDailyRotation()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero));
        var store = new ConfigStore(_directory.Path, clock);
        Directory.CreateDirectory(store.BackupsDirectory);
        File.WriteAllText(Path.Combine(store.BackupsDirectory, "config-20261003.json"), """{ "schemaVersion": 1, "fences": [ { "id": "old", "title": "Old" } ] }""");
        File.WriteAllText(Path.Combine(store.BackupsDirectory, "config-20261005.json"), $$"""{ "schemaVersion": {{NeoFencesConfig.CurrentSchemaVersion}}, "fences": [] }"""); // today's schema: not copied

        Assert.True(store.Save(NeoFencesConfig.CreateDefault()));

        var copy = Path.Combine(store.BackupsDirectory, ConfigStore.PreviousSchemaCopyName);
        Assert.Contains("\"old\"", File.ReadAllText(copy));
        Assert.DoesNotMatch(@"^config-.*\.json$", ConfigStore.PreviousSchemaCopyName); // never picked as a daily backup, never pruned
        File.WriteAllText(copy, "kept");
        Assert.True(store.Save(NeoFencesConfig.CreateDefault()));
        Assert.Equal("kept", File.ReadAllText(copy)); // once only
    }

    [Fact]
    public void Config_AFailedCopyFromBeforeSchemaTwoIsTriedAgainOnTheNextSave()
    {
        // M13c final review: the once-per-run check must not give up after a failed copy (the old file then rotates out)
        var store = new ConfigStore(_directory.Path, new FixedTimeProvider(new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero)));
        Directory.CreateDirectory(store.BackupsDirectory);
        File.WriteAllText(Path.Combine(store.BackupsDirectory, "config-20261003.json"), """{ "schemaVersion": 1, "fences": [ { "id": "old", "title": "Old" } ] }""");
        var copy = Path.Combine(store.BackupsDirectory, ConfigStore.PreviousSchemaCopyName);
        Directory.CreateDirectory(copy); // something in the way: the copy fails

        Assert.True(store.Save(NeoFencesConfig.CreateDefault()));
        Assert.NotNull(store.LastBackupFailure);

        Directory.Delete(copy);
        Assert.True(store.Save(NeoFencesConfig.CreateDefault()));
        Assert.Contains("\"old\"", File.ReadAllText(copy));
    }
}
