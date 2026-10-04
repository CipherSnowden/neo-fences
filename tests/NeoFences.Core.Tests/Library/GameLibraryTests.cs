using NeoFences.Core.Config;
using NeoFences.Core.Library;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Library;

/// <summary>Game Library (M12, spec 2026-10-03-game-library-design): finding, merging and filing the user's games.</summary>
public class GameLibraryTests
{
    private const string LibraryFoldersVdf = """
        "libraryfolders"
        {
        	"0"
        	{
        		"path"		"C:\\Program Files (x86)\\Steam"
        		"apps"
        		{
        			"228980"		"367436789"
        			"431960"		"1"
        		}
        	}
        	// a comment
        	"1"
        	{
        		"path"		"D:\\SteamLibrary"
        		"label"		"Games \"fast\""
        	}
        }
        """;

    private const string DetroitAcf = """
        "AppState"
        {
        	"appid"		"1222140"
        	"name"		"Detroit: Become Human"
        	"StateFlags"		"4"
        	"installdir"		"Detroit Become Human"
        	"UserConfig"
        	{
        		"language"		"english"
        	}
        }
        """;

    private const string EpicItem = """
        {
          "FormatVersion": 0,
          "DisplayName": "Mafia: Definitive Edition",
          "InstallLocation": "D:\\EpicLibrary\\MafiaDE",
          "LaunchExecutable": "launcher.exe",
          "CatalogNamespace": "ee8802651a004c48999169fa32eb4903",
          "CatalogItemId": "bc4e8f6978c640c6908fb0cc7710a693",
          "AppName": "e2e0a0f1e746426f9d8c2f2d1f697351",
          "bIsIncompleteInstall": false
        }
        """;

    private static GameEntry Game(string id, string name, GameSource source, string target, string? folder = null, string? scanKey = null, string? poster = null) =>
        new(id, name, source, scanKey ?? source.ToString().ToLowerInvariant(), new GameLaunch(target), folder, poster);

    [Fact]
    public void ValveText_ParsesNestedKeysEscapesAndComments()
    {
        var root = ValveKeyValues.Parse(LibraryFoldersVdf);
        var folders = (IReadOnlyDictionary<string, object>)root["LIBRARYFOLDERS"]; // keys are case-insensitive
        Assert.Equal(@"D:\SteamLibrary", ((IReadOnlyDictionary<string, object>)folders["1"])["path"]);
        Assert.Equal("Games \"fast\"", ((IReadOnlyDictionary<string, object>)folders["1"])["label"]);
        Assert.Throws<FormatException>(() => ValveKeyValues.Parse("\"a\" { \"b\" "));
        Assert.Throws<FormatException>(() => ValveKeyValues.Parse("\"a\" \"unterminated"));
    }

    [Fact]
    public void Steam_LibraryFoldersAndManifests()
    {
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], SteamFiles.LibraryFolders(LibraryFoldersVdf));
        Assert.Equal(new SteamApp("1222140", "Detroit: Become Human", "Detroit Become Human"), SteamFiles.ParseManifest(DetroitAcf));
        Assert.Null(SteamFiles.ParseManifest("\"AppState\" { \"appid\" \"5\" }")); // no name or folder: not listed
        Assert.Equal("steam://rungameid/1222140", SteamFiles.LaunchUri("1222140"));
    }

    [Fact]
    public void Epic_ManifestAndLaunchLink()
    {
        var game = EpicManifest.Parse(EpicItem)!;
        Assert.Equal("Mafia: Definitive Edition", game.DisplayName);
        Assert.Equal(@"D:\EpicLibrary\MafiaDE", game.InstallLocation);
        Assert.Equal("com.epicgames.launcher://apps/ee8802651a004c48999169fa32eb4903%3Abc4e8f6978c640c6908fb0cc7710a693%3Ae2e0a0f1e746426f9d8c2f2d1f697351?action=launch&silent=true",
            game.LaunchUri);
        Assert.Null(EpicManifest.Parse("""{ "DisplayName": "Half", "bIsIncompleteInstall": true, "InstallLocation": "D:\\x", "AppName": "a" }"""));
        Assert.Null(EpicManifest.Parse("not json"));
    }

    [Fact]
    public void Catalog_MergesDuplicates_ByFolderOrLaunch_StrongerSourceWins()
    {
        var gog = Game("gog:1449710114", "Mafia II Definitive Edition", GameSource.Gog, @"D:\GameLibrary\Mafia II - Definitive Edition\pc\Mafia2Launcher\Launcher.exe",
            folder: @"D:\GameLibrary\Mafia II - Definitive Edition");
        var folder = Game(@"folder:d:\gamelibrary\mafia ii - definitive edition", "Mafia II - Definitive Edition", GameSource.Folder,
            @"D:\GameLibrary\Mafia II - Definitive Edition\pc\Mafia II Definitive Edition.exe", folder: @"D:\GameLibrary\Mafia II - Definitive Edition\", scanKey: @"folder:d:\gamelibrary");
        var steam = Game("steam:1222140", "Detroit: Become Human", GameSource.Steam, "steam://rungameid/1222140", poster: @"C:\cache\1222140.jpg");
        var desktopUrl = Game("desktop:detroit", "Detroit Become Human", GameSource.DesktopShortcut, "STEAM://rungameid/1222140", scanKey: "desktop");
        var shortcut = Game("desktop:clair", "Clair Obscur - Expedition 33", GameSource.DesktopShortcut, @"D:\GameLibrary\Clair Obscur - Expedition 33\Expedition33_Steam.exe",
            folder: @"D:\GameLibrary\Clair Obscur - Expedition 33", scanKey: "desktop");
        var clairFolder = Game(@"folder:d:\gamelibrary\clair obscur - expedition 33", "Clair Obscur - Expedition 33", GameSource.Folder,
            @"D:\GameLibrary\Clair Obscur - Expedition 33\Sandfall\Binaries\Win64\SandFall-Win64-Shipping.exe", folder: @"d:\gamelibrary\clair obscur - expedition 33", scanKey: @"folder:d:\gamelibrary");

        var merged = GameCatalog.Merge(
            [new SourceScan("folder:d:\\gamelibrary", true, [folder, clairFolder]), new SourceScan("desktop", true, [desktopUrl, shortcut]),
             new SourceScan("gog", true, [gog]), new SourceScan("steam", true, [steam])],
            previous: [], hidden: []);

        Assert.Equal(["desktop:clair", "steam:1222140", "gog:1449710114"], merged.Select(game => game.Id));
        Assert.Equal(@"C:\cache\1222140.jpg", merged[1].Poster);
    }

    [Fact]
    public void Catalog_SkipsTools_HidesHidden_SortsAtoZIgnoringThe()
    {
        var merged = GameCatalog.Merge(
            [new SourceScan("steam", true,
            [
                Game("steam:228980", "Steamworks Common Redistributables", GameSource.Steam, "steam://rungameid/228980"),
                Game("steam:1", "Proton 9.0", GameSource.Steam, "steam://rungameid/1"),
                Game("steam:2", "Some Game Dedicated Server", GameSource.Steam, "steam://rungameid/2"),
                Game("steam:3", "The Witcher 3", GameSource.Steam, "steam://rungameid/3"),
                Game("steam:4", "Blur", GameSource.Steam, "steam://rungameid/4"),
                Game("steam:5", "Zuma", GameSource.Steam, "steam://rungameid/5"),
                Game("steam:431960", "Wallpaper Engine", GameSource.Steam, "steam://rungameid/431960"),
            ])],
            previous: [], hidden: ["steam:431960"]);

        Assert.Equal(["Blur", "The Witcher 3", "Zuma"], merged.Select(game => game.Name));
    }

    [Fact]
    public void Catalog_HidingAGame_CoversEverySourceItWasFoundIn()
    {
        // Live smoke 2026-10-04: AC Black Flag was hidden under its Desktop shortcut's id; with the shortcut deleted the
        // same game came back from D:\GameLibrary under another id.
        var shortcut = Game("desktop:ac", "AC Black Flag Resynced", GameSource.DesktopShortcut, @"D:\GameLibrary\AC\ACBlackFlag.exe", folder: @"D:\GameLibrary\AC", scanKey: "desktop");
        var folder = Game(@"folder:d:\gamelibrary\ac", "AC", GameSource.Folder, @"D:\GameLibrary\AC\ACBlackFlag.exe", folder: @"D:\GameLibrary\AC", scanKey: @"folder:d:\gamelibrary");

        var both = GameCatalog.Merge([new SourceScan("desktop", true, [shortcut]), new SourceScan(@"folder:d:\gamelibrary", true, [folder])], [], []);
        Assert.Equal(["desktop:ac", @"folder:d:\gamelibrary\ac"], GameCatalog.IdsOf(both[0]));

        var hidden = GameCatalog.IdsOf(both[0]); // what "Hide from library" stores
        var shortcutDeleted = GameCatalog.Merge([new SourceScan("desktop", true, []), new SourceScan(@"folder:d:\gamelibrary", true, [folder])], [], hidden);
        Assert.Empty(shortcutDeleted);
        Assert.Empty(GameCatalog.Merge([new SourceScan(@"folder:d:\gamelibrary", true, [folder]), new SourceScan("desktop", true, [shortcut])], [], [@"folder:d:\gamelibrary\ac"]));
    }

    [Fact]
    public void Catalog_KeepsGamesOfSourcesThatCouldNotBeRead_AndDropsDisabledOnes()
    {
        var blur = Game(@"folder:d:\gamelibrary\blur", "Blur", GameSource.Folder, @"D:\GameLibrary\Blur\Blur.exe", folder: @"D:\GameLibrary\Blur", scanKey: @"folder:d:\gamelibrary");
        var detroit = Game("steam:1222140", "Detroit", GameSource.Steam, "steam://rungameid/1222140");
        var mafia = Game("epic:mafia", "Mafia", GameSource.Epic, "com.epicgames.launcher://apps/x");

        var merged = GameCatalog.Merge(
            [new SourceScan(@"folder:d:\gamelibrary", false, []), new SourceScan("steam", true, [])],
            previous: [blur, detroit, mafia], hidden: []);

        // D: unplugged: its games stay; Steam read fine and found none: Detroit goes; Epic was not scanned (switched off): gone
        Assert.Equal(["Blur"], merged.Select(game => game.Name));
    }

    [Fact]
    public void Catalog_AFailedSourceKeepsTheGamesOfAllItsParts()
    {
        // final review I1: a Steam scan that throws is "steam"; its games carry "steam:<library>"
        var detroit = Game("steam:1222140", "Detroit", GameSource.Steam, "steam://rungameid/1222140", scanKey: @"steam:d:\steamlibrary");
        var steamStore = Game("steamstore:1", "Not Steam", GameSource.Steam, "steam://rungameid/1", scanKey: "steamstore");

        var merged = GameCatalog.Merge([new SourceScan("steam", false, [])], previous: [detroit, steamStore], hidden: []);

        Assert.Equal(["Detroit"], merged.Select(game => game.Name)); // "steamstore" is not a part of "steam"
    }

    [Fact]
    public void Steam_AQueuedDownloadIsNotAnInstalledGame()
    {
        // final review I3: an appmanifest exists as soon as a download is queued; installed = StateFlags bit 4
        Assert.Null(SteamFiles.ParseManifest(DetroitAcf.Replace("\"StateFlags\"\t\t\"4\"", "\"StateFlags\"\t\t\"1026\"")));
        Assert.NotNull(SteamFiles.ParseManifest(DetroitAcf.Replace("\"StateFlags\"\t\t\"4\"", "\"StateFlags\"\t\t\"6\""))); // updating: still installed
    }

    [Fact]
    public void LibraryIndex_ADamagedOrHandEditedIndexIsRepaired()
    {
        // final review (minor 3, re-graded): never a path outside the library folder, never a null to crash on
        var state = LibraryFiles.Repair(ConfigJson.DeserializeLibrary("""
            { "items": [
                null,
                { "game": null, "fileName": "a.url", "signature": "" },
                { "game": { "id": "steam:1", "name": "Escape", "source": "steam", "scanKey": "steam", "launch": { "target": "steam://rungameid/1" } }, "fileName": "..\\..\\Desktop\\x.lnk", "signature": "" },
                { "game": { "id": "steam:2", "name": "Fine", "source": "steam", "scanKey": "steam", "launch": { "target": "steam://rungameid/2" } }, "fileName": "Fine.url", "signature": "s" },
                { "game": { "id": "steam:3", "name": "Twin", "source": "steam", "scanKey": "steam", "launch": { "target": "steam://rungameid/3" } }, "fileName": "fine.URL", "signature": "s" },
                { "game": { "id": "steam:4", "name": "No launch", "source": "steam", "scanKey": "steam" }, "fileName": "No launch.url", "signature": "s" }
              ] }
            """));

        Assert.Equal(["Fine.url"], state.Items.Select(item => item.FileName));
        Assert.Empty(LibraryFiles.Repair(ConfigJson.DeserializeLibrary("""{ "items": null }""")).Items);
    }

    [Theory]
    [InlineData(@"ACBlackFlag.exe:478000000|_Redist\QuickSFV.EXE:100", "ACBlackFlag.exe")]
    [InlineData(@"forzahorizon6.exe:183000000|Installers\GamingRepair.exe:1000000|NoDVD\Online Fix\forzahorizon6_loader.exe:300", "forzahorizon6.exe")]
    [InlineData(@"unins000.exe:900000000|CrashReporter.exe:800000000|Game\Bin\game.exe:5000000|vc_redist.x64.exe:24000000", @"Game\Bin\game.exe")]
    [InlineData(@"EasyAntiCheat\EasyAntiCheat_Setup.exe:90000000|setup.exe:80000000", null)]
    public void ProgramPicker_TakesTheLargestRealProgram(string listing, string? expected)
    {
        var programs = listing.Split('|').Select(part => part.Split(':')).Select(parts => (parts[0], long.Parse(parts[1]))).ToList();
        Assert.Equal(expected, ProgramPicker.Pick(programs));
    }

    [Fact]
    public void LibraryFiles_NamesAreSafeStableAndUnique_AndOnlyChangesAreWritten()
    {
        var detroit = Game("steam:1222140", "Detroit: Become Human", GameSource.Steam, "steam://rungameid/1222140");
        var blur = Game("folder:blur", "Blur", GameSource.Folder, @"D:\GameLibrary\Blur\Blur.exe");
        var blurSteam = Game("steam:9", "Blur", GameSource.Steam, "steam://rungameid/9");
        var con = Game("steam:10", "CON", GameSource.Steam, "steam://rungameid/10");

        var first = LibraryFiles.Plan(previous: [], games: [detroit, blur, blurSteam, con]);

        Assert.Equal(["Detroit - Become Human.url", "Blur.lnk", "Blur (2).url", "CON (game).url"], first.Items.Select(item => item.FileName));
        Assert.Equal(4, first.Write.Count);
        Assert.Empty(first.Delete);

        var second = LibraryFiles.Plan(previous: first.Items, games: [detroit with { Poster = @"C:\p.jpg" }, blurSteam]);

        Assert.Equal(["Detroit - Become Human.url", "Blur (2).url"], second.Items.Select(item => item.FileName)); // names stay
        Assert.Equal(["Detroit - Become Human.url"], second.Write.Select(item => item.FileName));               // only what changed
        Assert.Equal(["Blur.lnk", "CON (game).url"], second.Delete);
    }

    [Fact]
    public void LibraryState_RoundTripsAsJson()
    {
        var plan = LibraryFiles.Plan(previous: [], games: [Game("steam:1", "A", GameSource.Steam, "steam://rungameid/1", poster: @"C:\a.jpg")]);
        var json = ConfigJson.SerializeLibrary(new LibraryState { Items = plan.Items });
        Assert.Contains("\"source\": \"steam\"", json);
        Assert.Equal(json, ConfigJson.SerializeLibrary(ConfigJson.DeserializeLibrary(json))); // lists compare by reference: compare the text
    }

    [Fact]
    public void Config_LibrarySettingsDefaults_AndOneLibraryFenceAtMost()
    {
        var config = ConfigNormalizer.Normalize(ConfigJson.Deserialize("""
            { "schemaVersion": 1,
              "fences": [ { "id": "a", "title": "Games", "source": { "kind": "library" } },
                          { "id": "b", "title": "Games 2", "source": { "kind": "library" } } ],
              "library": { "folders": null, "hidden": [ "steam:431960" ] } }
            """));

        Assert.Equal([FenceSourceKind.Library, FenceSourceKind.Desktop], config.Fences.Where(fence => !fence.IsInbox).Select(fence => fence.Source.Kind));
        Assert.Empty(config.Library.Folders);
        Assert.True(config.Library.Sources.Steam && config.Library.Sources.DesktopShortcuts);
        Assert.Equal(["steam:431960"], config.Library.Hidden);
        Assert.Contains("\"kind\": \"library\"", ConfigJson.Serialize(config));
        Assert.Empty(ConfigNormalizer.Normalize(ConfigJson.Deserialize("""{ "schemaVersion": 1, "fences": [] }""")).Library.Hidden);
    }
}
