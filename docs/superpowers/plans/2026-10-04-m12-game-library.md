# M12 — Game Library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One Game Library fence that fills itself with every installed game. It finds them in Steam, Epic, GOG, Ubisoft Connect, the EA app, Battle.net, Xbox, the user's game folders and Desktop game shortcuts. It shows Steam posters (icons otherwise), lets the user hide games, and keeps itself up to date. Released as v1.5.0.

**Architecture:**
- **Core:**
  - parsers (Valve text, Epic manifests);
  - `GameCatalog` merges, de-duplicates, hides and sorts;
  - `ProgramPicker` and `LibraryFiles` plan the shortcut files;
  - `LibrarySettings` lives in `config.json`;
  - a new `FenceSourceKind.Library`.
- **Shell:**
  - `GameScanners` reads launchers, the registry, packages and folders, read-only;
  - `ShellLinks` reads and writes `.lnk`/`.url` files;
  - `LibraryWriter` keeps NeoFences' own `library\` folder and its index;
  - the item menu gets custom commands, drags can be copy-only, and a fence can refuse drops.
- **App:**
  - the library folder is shown through the Portal machinery as 2:3 tiles;
  - scans run on their own STA thread (at start, on folder changes, on Refresh, after game mode);
  - Hide from library, Open install folder;
  - tray and fence menu items;
  - a Settings → Game Library card.

**Tech Stack:** .NET 10, WPF, CsWin32 0.3.335 (no new bindings: `IShellLinkW`, `IPersistFile` and `ShellLink` came in M11), `Microsoft.Win32.Registry`, System.Text.Json, System.Xml.Linq, Serilog, xUnit. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-10-03-game-library-design.md` (approved 2026-10-03). **Decision:** ADR-032 (new, Task 4).
**Changes from the prototype's live test, within the spec:**
- "Hide from library" stores every id the merged game goes by. Otherwise a game hidden under its Desktop shortcut's id comes back from its game folder once that shortcut is deleted.
- The library fence starts at icon size 64 (tiles 96 × 144).

**Pre-verified (2026-10-03/04):** every code block below was compiled together (0 warnings, 0 errors), and **361/361 tests pass** (14 new). On the user's PC:
- **Read-only scanner probe:** 12 games in 0.2 s; Steam posters found; Mafia II appears once; the Steam `.url` shortcut was merged with the Steam entry.
- **Live smokes, with consent** (installed 1.4.0, config and folders restored):
  - the library fence was created with every game, A–Z, as tiles with Steam posters;
  - a new game folder appeared and disappeared without a restart;
  - a missing games folder kept its game, and the status line named it;
  - right-click → Hide from library, then Settings → Show again, worked;
  - 0 log warnings.

## Global Constraints

- **Hard rule 1:** no user file, game folder or launcher file is changed; only NeoFences' own `%LOCALAPPDATA%\NeoFences\library\` is written, and only files listed in its `index.json` are removed.
- **Hard rules 4/5:** registry, shell links, packages and file watching stay in `NeoFences.Shell`; Core holds the parsers and the catalog with no Windows calls.
- **Hard rule 6:** no new dependency; Valve's text format is read by `ValveKeyValues`.
- **Hard rule 7:** every scanner, shortcut write and art load is guarded; a failure degrades that source or entry; games of a source that could not be read are kept.
- **Nothing launches** except on the user's click; launcher games start through their launcher (`steam://`, `com.epicgames.launcher://`, `uplay://`, Galaxy).
- **One library fence at most** (repaired on load); rules never target it; nothing is dropped into it.
- **Commits:** single line, Conventional Commits, past tense, no `Co-Authored-By` trailer. Test command: `dotnet test NeoFences.slnx`.
- **Smokes:** only with the user's explicit go (the user may be at the PC); restore the installed copy (1.4.0), config and test folders; print TEST RUNNING / TEST COMPLETE; never type into Windows Terminal; aim at a tile's own list item inside the fence, never press Delete blind.

## Review Focus

1. **Big or slow libraries.** 300 Steam games, a game folder on a sleeping HDD or a network share, a scan started while another runs. Expected: the fences stay responsive, scans never overlap, and nothing waits on the UI thread.
2. **Odd launcher data:**
   - a half-written `appmanifest` during an install;
   - an Epic manifest left behind by an uninstall;
   - a GOG registry entry pointing to a deleted folder;
   - an Xbox package without an Application entry;
   - a Steam `libraryfolders.vdf` with odd entries.

   Expected: such entries are skipped, nothing crashes, and no ghost games appear.
3. **Game names:** colons, slashes, the same name from two sources, reserved device names, 200-character names, names that change with an update. Expected: valid, stable and unique file names, and no orphan shortcuts left behind.
4. **The library folder touched by hand:** shortcuts deleted or renamed in `library\`, a damaged `index.json`, extra files. Expected: the folder is rebuilt; extra files are neither shown nor deleted.
5. **The library fence with other features:** tabs, snapshot restore, deleting the fence, game mode, quick-hide/Peek, an Explorer restart, a drive removal notice. Expected: the library keeps working and no file operation ever goes into it.

---

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/NeoFences.Core/Library/*.cs` (new), `Model/LibrarySettings.cs` (new), `Model/Fence.cs`, `Model/NeoFencesConfig.cs`, `Config/ConfigNormalizer.cs`, `Config/ConfigJson.cs` + `tests/…/Library/GameLibraryTests.cs` (new) | parsing, catalog, file plan, settings | 1 |
| `src/NeoFences.Shell/GameScanners.cs`, `ShellLinks.cs`, `LibraryWriter.cs` (new), `ShellItemMenu.cs`, `ShellDragDrop.cs` | scanning, shortcut files, menu and drag options | 2 |
| `src/NeoFences.App/FenceHost.Library.cs`, `SettingsWindow.Library.cs` (new), `FenceHost.cs`, `FenceWindow.xaml(.cs)`, `FenceItemView.cs`, `SettingsWindow.xaml(.cs)`, `AppPaths.cs` | the fence, tiles, scans, menus, Settings card | 3 |
| `docs/…` (ADR-032, checklist Y, research, ROADMAP, FEATURES, ARCHITECTURE, SESSION-LOG), hub | docs | 4 |

---

### Task 1: Core — parsing, catalog, file plan, settings

**Files:**
- Modify: `docs/ROADMAP.md` (claim).
- Create: `src/NeoFences.Core/Library/ValveKeyValues.cs`, `LauncherFiles.cs`, `GameCatalog.cs`, `LibraryFiles.cs`, `src/NeoFences.Core/Model/LibrarySettings.cs`, `tests/NeoFences.Core.Tests/Library/GameLibraryTests.cs`.
- Replace: `src/NeoFences.Core/Model/Fence.cs`, `Model/NeoFencesConfig.cs`, `Config/ConfigNormalizer.cs`, `Config/ConfigJson.cs`.

**Interfaces:**
- Produces:
  - `ValveKeyValues.Parse(text)`;
  - `SteamFiles.LibraryFolders(vdf)`, `SteamFiles.ParseManifest(acf) → SteamApp?`, `SteamFiles.LaunchUri(appId)`;
  - `EpicManifest.Parse(json) → EpicGame?` (with `.LaunchUri`);
  - `enum GameSource { Steam, Epic, Gog, Ubisoft, Ea, BattleNet, Xbox, DesktopShortcut, Folder }`;
  - `GameLaunch(Target, Arguments?, WorkingFolder?) { IsLink }`;
  - `GameEntry(Id, Name, Source, ScanKey, Launch, InstallFolder?, Poster?, IconPath?) { OtherIds }`;
  - `SourceScan(ScanKey, Readable, Games)`;
  - `GameCatalog.Merge(scans, previous, hidden)`, `GameCatalog.IdsOf(game)`, `GameCatalog.IsTool(name)`, `GameCatalog.SortKey(name)`;
  - `ProgramPicker.Pick((RelativePath, Size)[]) → string?`;
  - `LibraryItem(Game, FileName, Signature)`, `LibraryState { Items }`, `LibraryPlan(Items, Write, Delete)`, `LibraryFiles.Plan(previous, games)`, `LibraryFiles.SafeName(name)`;
  - `FenceSourceKind.Library`, `FenceSource.Library`;
  - `LibrarySettings { Folders, Sources, Hidden }`, `LibrarySources { Steam … DesktopShortcuts }`, `NeoFencesConfig.Library`;
  - `ConfigJson.SerializeLibrary` / `DeserializeLibrary`.

- [ ] **Step 1: Branch and claim**

```powershell
git switch -c m12-game-library
```
In `docs/ROADMAP.md`, before the line `Tasks for M2–M8 are broken down when the previous milestone closes.`, add:
```markdown
## M12 — Game Library (v1.5, spec 2026-10-03-game-library-design)
User choices 2026-10-03: every game in one place; launchers, Xbox, my game folders, Desktop game shortcuts; posters where on disk; one A–Z fence.
- [x] M12 spec and implementation plan
- [~] M12 — claimed by session 2026-10-04 m12
```
```powershell
git add docs/ROADMAP.md
git commit -m "docs: claimed M12"
```

- [ ] **Step 2: Write the failing tests**

`tests/NeoFences.Core.Tests/Library/GameLibraryTests.cs`:
```csharp
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
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/NeoFences.Core.Tests`
Expected: build FAILS: `CS0234 … 'Library' does not exist in the namespace 'NeoFences.Core'`, `CS0246 GameEntry`, `CS0246 GameSource`.

- [ ] **Step 4: Implement**

`src/NeoFences.Core/Library/ValveKeyValues.cs`:
```csharp
using System.Text;

namespace NeoFences.Core.Library;

/// <summary>
/// Valve's text KeyValues format (Steam's <c>libraryfolders.vdf</c>, <c>appmanifest_*.acf</c>; M12): quoted keys with a
/// quoted value or a nested <c>{ … }</c> block, <c>//</c> comments, backslash escapes. Keys compare case-insensitively.
/// A small reader instead of a dependency (hard rule 6).
/// </summary>
public static class ValveKeyValues
{
    /// <summary>The top-level keys; values are <see cref="string"/> or a nested dictionary.</summary>
    /// <exception cref="FormatException">The text is not well-formed (unterminated string or block).</exception>
    public static IReadOnlyDictionary<string, object> Parse(string text)
    {
        var position = 0;
        var root = ReadBlock(text, ref position, nested: false);
        return root;
    }

    private static Dictionary<string, object> ReadBlock(string text, ref int position, bool nested)
    {
        var block = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            SkipSpaceAndComments(text, ref position);
            if (position >= text.Length)
            {
                if (nested) throw new FormatException("unterminated block");
                return block;
            }
            if (text[position] == '}')
            {
                if (!nested) throw new FormatException("unexpected '}'");
                position++;
                return block;
            }
            var key = ReadString(text, ref position);
            SkipSpaceAndComments(text, ref position);
            if (position >= text.Length) throw new FormatException($"no value for \"{key}\"");
            if (text[position] == '{')
            {
                position++;
                block[key] = ReadBlock(text, ref position, nested: true);
            }
            else
            {
                block[key] = ReadString(text, ref position);
            }
        }
    }

    private static string ReadString(string text, ref int position)
    {
        if (text[position] != '"')
        {
            // Unquoted token (allowed by the format, rare in Steam's files): up to whitespace or a brace.
            var start = position;
            while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not ('{' or '}' or '"')) position++;
            if (position == start) throw new FormatException($"unexpected '{text[position]}'");
            return text[start..position];
        }
        position++;
        var value = new StringBuilder();
        while (position < text.Length)
        {
            var current = text[position++];
            if (current == '"') return value.ToString();
            if (current == '\\' && position < text.Length)
            {
                var escaped = text[position++];
                value.Append(escaped switch { 'n' => '\n', 't' => '\t', _ => escaped });
                continue;
            }
            value.Append(current);
        }
        throw new FormatException("unterminated string");
    }

    private static void SkipSpaceAndComments(string text, ref int position)
    {
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position])) position++;
            else if (text[position] == '/' && position + 1 < text.Length && text[position + 1] == '/')
            {
                while (position < text.Length && text[position] != '\n') position++;
            }
            else return;
        }
    }
}
```
`src/NeoFences.Core/Library/LauncherFiles.cs`:
```csharp
using System.Text.Json;

namespace NeoFences.Core.Library;

/// <summary>One installed Steam app from its <c>appmanifest_&lt;appid&gt;.acf</c>.</summary>
public sealed record SteamApp(string AppId, string Name, string InstallDir);

/// <summary>Steam's library list and app manifests (M12). Parsing only; the Shell reads the files.</summary>
public static class SteamFiles
{
    /// <summary>Every library folder in <c>steamapps\libraryfolders.vdf</c>, in file order.</summary>
    /// <exception cref="FormatException">The file is damaged.</exception>
    public static IReadOnlyList<string> LibraryFolders(string vdfText)
    {
        var root = ValveKeyValues.Parse(vdfText);
        if (!root.TryGetValue("libraryfolders", out var foldersValue) || foldersValue is not IReadOnlyDictionary<string, object> folders) return [];
        return folders.Values.OfType<IReadOnlyDictionary<string, object>>()
            .Select(folder => folder.TryGetValue("path", out var path) ? path as string : null)
            .OfType<string>().Where(path => path.Length > 0).ToList();
    }

    /// <summary>The app in an <c>appmanifest_*.acf</c>, or null when it lacks an id, a name or an install folder (or is damaged).</summary>
    public static SteamApp? ParseManifest(string acfText)
    {
        try
        {
            if (ValveKeyValues.Parse(acfText).TryGetValue("AppState", out var stateValue) && stateValue is IReadOnlyDictionary<string, object> state
                && Text(state, "appid") is { } appId && Text(state, "name") is { } name && Text(state, "installdir") is { } installDir)
            {
                return new SteamApp(appId, name, installDir);
            }
        }
        catch (FormatException)
        {
            // a half-written manifest (Steam is installing): skipped this time
        }
        return null;
    }

    public static string LaunchUri(string appId) => $"steam://rungameid/{appId}";

    private static string? Text(IReadOnlyDictionary<string, object> block, string key) =>
        block.TryGetValue(key, out var value) && value is string { Length: > 0 } text ? text : null;
}

/// <summary>An installed Epic game from its launcher manifest (<c>Manifests\*.item</c>).</summary>
public sealed record EpicGame(string DisplayName, string InstallLocation, string? LaunchExecutable, string Namespace, string ItemId, string AppName)
{
    /// <summary>Starts the game through the Epic launcher (ownership, updates and cloud saves stay Epic's).</summary>
    public string LaunchUri => $"com.epicgames.launcher://apps/{Namespace}%3A{ItemId}%3A{AppName}?action=launch&silent=true";
}

public static class EpicManifest
{
    /// <summary>The game, or null for a damaged file, an unfinished install or missing fields.</summary>
    public static EpicGame? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True) return null;
            string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;
            if (Text("DisplayName") is not { } name || Text("InstallLocation") is not { } location || Text("AppName") is not { } appName) return null;
            return new EpicGame(name, location, Text("LaunchExecutable"), Text("CatalogNamespace") ?? "", Text("CatalogItemId") ?? "", appName);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
```
`src/NeoFences.Core/Library/GameCatalog.cs`:
```csharp
using System.Text.RegularExpressions;

namespace NeoFences.Core.Library;

/// <summary>Where a game was found. The order is the merge priority: launchers, then Xbox, then shortcuts, then folders.</summary>
public enum GameSource { Steam, Epic, Gog, Ubisoft, Ea, BattleNet, Xbox, DesktopShortcut, Folder }

/// <summary>How a game starts: a link (<c>steam://…</c>) or a program with arguments.</summary>
public sealed record GameLaunch(string Target, string? Arguments = null, string? WorkingFolder = null)
{
    public bool IsLink => Target.Contains("://", StringComparison.Ordinal);
}

/// <summary>One game in the library (M12).</summary>
/// <param name="Id">Stable across scans: "steam:1222140", "folder:d:\gamelibrary\blur" (the hidden list uses it).</param>
/// <param name="ScanKey">The scan that found it ("steam", "folder:d:\gamelibrary"): its games are kept while that scan cannot read.</param>
/// <param name="Poster">A 2:3 cover image on disk, when the launcher keeps one.</param>
/// <param name="IconPath">A file whose icon stands for the game (its program, a package logo).</param>
public sealed record GameEntry(string Id, string Name, GameSource Source, string ScanKey, GameLaunch Launch,
    string? InstallFolder = null, string? Poster = null, string? IconPath = null)
{
    /// <summary>Ids of the same game found by weaker sources (merged into this one): hiding covers them too.</summary>
    public IReadOnlyList<string> OtherIds { get; init; } = [];
}

/// <summary>What one scan found; <paramref name="Readable"/> false = the source could not be read this time (keep its games).</summary>
public sealed record SourceScan(string ScanKey, bool Readable, IReadOnlyList<GameEntry> Games);

/// <summary>Merges what the sources found into one A–Z list, one entry per game (M12, spec §2). Pure.</summary>
public static partial class GameCatalog
{
    [GeneratedRegex(@"redistributable|steamworks common|^proton\b|steam linux runtime|\bsdk\b|dedicated server", RegexOptions.IgnoreCase)]
    private static partial Regex ToolName();

    public static IReadOnlyList<GameEntry> Merge(IReadOnlyList<SourceScan> scans, IReadOnlyList<GameEntry> previous, IReadOnlyCollection<string> hidden)
    {
        var found = new List<GameEntry>();
        foreach (var scan in scans)
        {
            found.AddRange(scan.Readable ? scan.Games : previous.Where(game => game.ScanKey == scan.ScanKey));
        }
        var kept = new List<GameEntry>();
        foreach (var game in found.Where(game => !IsTool(game.Name)).OrderBy(game => game.Source))
        {
            var index = kept.FindIndex(existing => SameGame(existing, game));
            if (index < 0)
            {
                kept.Add(game);
                continue;
            }
            var winner = kept[index]; // stronger source: keeps its name and launch, borrows what it lacks
            kept[index] = winner with
            {
                InstallFolder = winner.InstallFolder ?? game.InstallFolder,
                Poster = winner.Poster ?? game.Poster,
                IconPath = winner.IconPath ?? game.IconPath,
                OtherIds = [.. winner.OtherIds.Concat(IdsOf(game)).Distinct(StringComparer.OrdinalIgnoreCase).Where(id => !string.Equals(id, winner.Id, StringComparison.OrdinalIgnoreCase))],
            };
        }
        var hiddenIds = hidden.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return kept.Where(game => !IdsOf(game).Any(hiddenIds.Contains))
            .OrderBy(game => SortKey(game.Name), StringComparer.CurrentCultureIgnoreCase).ThenBy(game => game.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Every id a game is known by: its own and those of the duplicates merged into it.</summary>
    public static IReadOnlyList<string> IdsOf(GameEntry game) => [game.Id, .. game.OtherIds];

    /// <summary>Launcher tools that are not games (redistributables, Proton, SDKs, dedicated servers).</summary>
    public static bool IsTool(string name) => ToolName().IsMatch(name);

    /// <summary>A–Z ignoring a leading "The ".</summary>
    public static string SortKey(string name)
    {
        var trimmed = name.Trim();
        return trimmed.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? trimmed[4..] : trimmed;
    }

    private static bool SameGame(GameEntry first, GameEntry second) =>
        (first.InstallFolder is { } firstFolder && second.InstallFolder is { } secondFolder && NormalizeFolder(firstFolder) == NormalizeFolder(secondFolder))
        || LaunchKey(first.Launch) == LaunchKey(second.Launch);

    private static string NormalizeFolder(string folder) => folder.Trim().TrimEnd('\\', '/').ToLowerInvariant();

    /// <summary>A link up to its query ("?action=launch"), or a program with its arguments; case-insensitive.</summary>
    private static string LaunchKey(GameLaunch launch)
    {
        var target = launch.Target.Trim();
        if (launch.IsLink)
        {
            var query = target.IndexOf('?');
            return (query >= 0 ? target[..query] : target).ToLowerInvariant();
        }
        return $"{target} {launch.Arguments?.Trim()}".Trim().ToLowerInvariant();
    }
}

/// <summary>Which program in a game folder starts the game (M12, spec §2), when no shortcut says.</summary>
public static partial class ProgramPicker
{
    [GeneratedRegex(@"^(_?redist|redist.*|installers?|__installer|directx|dotnet|vcredist|support|commonredist|easyanticheat|battleye|nodvd)$", RegexOptions.IgnoreCase)]
    private static partial Regex SkippedFolder();

    [GeneratedRegex(@"^(unins|setup|vc_|dxsetup|easyanticheat)|crash|redist|report|helper|install", RegexOptions.IgnoreCase)]
    private static partial Regex SkippedProgram();

    /// <summary>The largest program that is not an installer, uninstaller, crash reporter or redistributable; null when none.</summary>
    /// <param name="programs">Paths relative to the game folder, with their size in bytes.</param>
    public static string? Pick(IEnumerable<(string RelativePath, long Size)> programs) =>
        programs
            .Where(program =>
            {
                var parts = program.RelativePath.Split('\\', '/');
                return !parts[..^1].Any(SkippedFolder().IsMatch) && !SkippedProgram().IsMatch(Path.GetFileNameWithoutExtension(parts[^1]));
            })
            .OrderByDescending(program => program.Size).ThenBy(program => program.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(program => program.RelativePath)
            .FirstOrDefault();
}
```
`src/NeoFences.Core/Library/LibraryFiles.cs`:
```csharp
namespace NeoFences.Core.Library;

/// <summary>A game's shortcut in NeoFences' library folder (M12).</summary>
/// <param name="Signature">What the file was written from; a changed signature means the file is rewritten.</param>
public sealed record LibraryItem(GameEntry Game, string FileName, string Signature);

/// <summary>The library folder's index (<c>library\index.json</c>): which files NeoFences wrote, for which games.</summary>
public sealed record LibraryState
{
    public IReadOnlyList<LibraryItem> Items { get; init; } = [];
}

/// <summary>The shortcut files to write and remove, and the next index.</summary>
public sealed record LibraryPlan(IReadOnlyList<LibraryItem> Items, IReadOnlyList<LibraryItem> Write, IReadOnlyList<string> Delete);

/// <summary>Plans the library folder from the catalog (M12, spec §3): safe, stable, unique names; only changes written.</summary>
public static class LibraryFiles
{
    private static readonly char[] Forbidden = ['\\', '/', '*', '?', '"', '<', '>', '|'];
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static LibraryPlan Plan(IReadOnlyList<LibraryItem> previous, IReadOnlyList<GameEntry> games)
    {
        var previousById = previous.GroupBy(item => item.Game.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var takenBases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // First keep the names of files that still fit their game, so a new game never takes them over.
        foreach (var game in games)
        {
            if (previousById.TryGetValue(game.Id, out var old) && Fits(old.FileName, game) && takenBases.Add(Path.GetFileNameWithoutExtension(old.FileName)))
                fileNames[game.Id] = old.FileName;
        }
        foreach (var game in games.Where(game => !fileNames.ContainsKey(game.Id)))
        {
            var baseName = SafeName(game.Name);
            var candidate = baseName;
            for (var counter = 2; !takenBases.Add(candidate); counter++) candidate = $"{baseName} ({counter})";
            fileNames[game.Id] = candidate + Extension(game);
        }
        var items = games.Select(game => new LibraryItem(game, fileNames[game.Id], Signature(game))).ToList();
        var write = items.Where(item => !previousById.TryGetValue(item.Game.Id, out var old) || old.Signature != item.Signature || old.FileName != item.FileName).ToList();
        var kept = items.Select(item => item.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var delete = previous.Select(item => item.FileName).Where(fileName => !kept.Contains(fileName)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new LibraryPlan(items, write, delete);
    }

    /// <summary>A file name for a game: forbidden characters out (":" becomes " -"), no reserved device names, at most 100 characters.</summary>
    public static string SafeName(string name)
    {
        var cleaned = new string(name.Replace(":", " -").Where(character => !char.IsControl(character) && Array.IndexOf(Forbidden, character) < 0).ToArray());
        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.');
        if (cleaned.Length > 100) cleaned = cleaned[..100].TrimEnd('.', ' ');
        if (cleaned.Length == 0) cleaned = "Game";
        return Reserved.Contains(cleaned) ? cleaned + " (game)" : cleaned;
    }

    private static string Extension(GameEntry game) => game.Launch.IsLink ? ".url" : ".lnk";

    /// <summary>The old file still names this game: "Blur.lnk", or "Blur (2).url" after a clash.</summary>
    private static bool Fits(string fileName, GameEntry game)
    {
        if (!Path.GetExtension(fileName).Equals(Extension(game), StringComparison.OrdinalIgnoreCase)) return false;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var safe = SafeName(game.Name);
        return stem.Equals(safe, StringComparison.OrdinalIgnoreCase)
            || (stem.StartsWith(safe + " (", StringComparison.OrdinalIgnoreCase) && stem.EndsWith(')') && int.TryParse(stem[(safe.Length + 2)..^1], out _));
    }

    private static string Signature(GameEntry game) =>
        string.Join('|', game.Name, game.Launch.Target, game.Launch.Arguments, game.Launch.WorkingFolder, game.Poster, game.IconPath, game.InstallFolder);
}
```
`src/NeoFences.Core/Model/LibrarySettings.cs`:
```csharp
namespace NeoFences.Core.Model;

/// <summary>Game Library settings (M12, spec §4), in <c>config.json</c>.</summary>
public sealed record LibrarySettings
{
    /// <summary>Folders whose sub-folders are games (e.g. D:GameLibrary).</summary>
    public IReadOnlyList<string> Folders { get; init; } = [];

    public LibrarySources Sources { get; init; } = new();

    /// <summary>Game ids the user hid ("steam:431960").</summary>
    public IReadOnlyList<string> Hidden { get; init; } = [];
}

/// <summary>Which sources the library reads; all on by default.</summary>
public sealed record LibrarySources
{
    public bool Steam { get; init; } = true;
    public bool Epic { get; init; } = true;
    public bool Gog { get; init; } = true;
    public bool Ubisoft { get; init; } = true;
    public bool Ea { get; init; } = true;
    public bool BattleNet { get; init; } = true;
    public bool Xbox { get; init; } = true;
    public bool Folders { get; init; } = true;
    public bool DesktopShortcuts { get; init; } = true;
}
```
`src/NeoFences.Core/Model/Fence.cs`:
```csharp
namespace NeoFences.Core.Model;

/// <summary>Library (M12): NeoFences' own game library folder, shown like a Portal.</summary>
public enum FenceSourceKind { Desktop, Portal, Library }

/// <summary>Where a fence's items come from: the desktop (Takeover) or a folder (Portal).</summary>
public sealed record FenceSource(FenceSourceKind Kind, string? Path = null)
{
    public static FenceSource Desktop { get; } = new(FenceSourceKind.Desktop);

    public static FenceSource Portal(string folderPath) => new(FenceSourceKind.Portal, folderPath);

    public static FenceSource Library { get; } = new(FenceSourceKind.Library);
}

public enum FenceSort { Manual, Name, Type, Date }

/// <summary>Item names under the icons: always, or only for the hovered or selected item (icon-only fences, M8b).</summary>
public enum LabelMode { Always, OnHover }

/// <summary>
/// One fence. Desktop fences keep an ordered list of item refs (shell parsing names: file paths or
/// "::{GUID}" for virtual items). Portal fences keep no items; they mirror <see cref="FenceSource.Path"/>.
/// </summary>
public sealed record Fence
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public FenceSource Source { get; init; } = FenceSource.Desktop;
    public IReadOnlyList<string> Items { get; init; } = [];
    public bool IsInbox { get; init; }
    public FenceSort Sort { get; init; } = FenceSort.Manual;
    public int IconSize { get; init; } = 48;
    public bool RolledUp { get; init; }
    public bool Locked { get; init; }
    public LabelMode Labels { get; init; } = LabelMode.Always;

    /// <summary>Only on a box's host (M9): every tab of the box in order, itself included. Empty = not a box.</summary>
    public IReadOnlyList<string> Tabs { get; init; } = [];

    /// <summary>On a box's host (M9): the tab shown; null or unknown = the first.</summary>
    public string? ActiveTab { get; init; }

    /// <summary>This fence's tab accent (M9), or none.</summary>
    public TabColor? TabColor { get; init; }

    public static Fence Create(string title, FenceSource? source = null) =>
        new() { Id = NewId(), Title = title, Source = source ?? FenceSource.Desktop };

    public static string NewId() => Guid.NewGuid().ToString("N");
}
```
`src/NeoFences.Core/Model/NeoFencesConfig.cs`:
```csharp
using System.Text.Json.Serialization;

namespace NeoFences.Core.Model;

/// <summary>Everything NeoFences persists, stored as <c>config.json</c> (ADR-006).</summary>
public sealed record NeoFencesConfig
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public Settings Settings { get; init; } = new();
    public IReadOnlyList<Fence> Fences { get; init; } = [];
    public IReadOnlyDictionary<string, Layout> Layouts { get; init; } = new Dictionary<string, Layout>();

    /// <summary>Rules auto-sort (M11), in order: the first matching enabled rule decides a new item's fence.</summary>
    public IReadOnlyList<Rule> Rules { get; init; } = [];

    /// <summary>Game Library settings (M12): game folders, sources, hidden games.</summary>
    public LibrarySettings Library { get; init; } = new();

    /// <summary>Fingerprint of the display configuration seen last; new configurations are derived from it.</summary>
    public string? LastLayoutFingerprint { get; init; }

    /// <summary>The single Inbox fence. Guaranteed to exist after <c>ConfigNormalizer.Normalize</c>.</summary>
    [JsonIgnore]
    public Fence Inbox => Fences.First(fence => fence.IsInbox);

    public static NeoFencesConfig CreateDefault() =>
        new() { Fences = [Fence.Create("Inbox") with { IsInbox = true }] };

    public NeoFencesConfig WithFence(Fence updated) =>
        this with { Fences = Fences.Select(fence => fence.Id == updated.Id ? updated : fence).ToList() };
}
```
`src/NeoFences.Core/Config/ConfigNormalizer.cs`:
```csharp
using NeoFences.Core.Model;

namespace NeoFences.Core.Config;

/// <summary>
/// Repairs a loaded (possibly hand-edited) config without losing items: exactly one Inbox, unique fence
/// ids, no nulls, supported icon sizes and enum values, each desktop item in at most one fence, no items on
/// portal fences, the Inbox always a desktop fence, and layouts free of null or non-finite entries.
/// </summary>
public static class ConfigNormalizer
{
    public static IReadOnlyList<int> IconSizes { get; } = [32, 48, 64, 96];

    public static NeoFencesConfig Normalize(NeoFencesConfig config)
    {
        var defaults = new Settings();
        var settings = config.Settings ?? defaults;
        settings = settings with
        {
            PeekHotkey = string.IsNullOrWhiteSpace(settings.PeekHotkey) ? defaults.PeekHotkey : settings.PeekHotkey,
            RollupExpand = Enum.IsDefined(settings.RollupExpand) ? settings.RollupExpand : defaults.RollupExpand,
            DefaultLabels = Enum.IsDefined(settings.DefaultLabels) ? settings.DefaultLabels : defaults.DefaultLabels, // M8b review
        };

        var seenFenceIds = new HashSet<string>(StringComparer.Ordinal);
        var seenItems = new HashSet<string>(ItemRef.Comparer);
        var inboxFound = false;
        var libraryFound = false; // M12: at most one Game Library fence
        var fences = new List<Fence>();

        foreach (var loadedFence in config.Fences ?? [])
        {
            if (loadedFence is null) continue;
            var isInbox = loadedFence.IsInbox && !inboxFound;
            var source = loadedFence.Source is { } loadedSource && Enum.IsDefined(loadedSource.Kind) ? loadedSource : FenceSource.Desktop;
            if (source.Kind == FenceSourceKind.Library && (libraryFound || loadedFence.IsInbox)) source = FenceSource.Desktop;
            libraryFound |= source.Kind == FenceSourceKind.Library;
            var fence = loadedFence with
            {
                Id = string.IsNullOrWhiteSpace(loadedFence.Id) || !seenFenceIds.Add(loadedFence.Id) ? Fence.NewId() : loadedFence.Id,
                Title = loadedFence.Title ?? "",
                Source = isInbox ? FenceSource.Desktop : source,
                IsInbox = isInbox,
                Sort = Enum.IsDefined(loadedFence.Sort) ? loadedFence.Sort : FenceSort.Manual,
                Labels = Enum.IsDefined(loadedFence.Labels) ? loadedFence.Labels : LabelMode.Always, // a hand-edited number (M8b review)
                Tabs = loadedFence.Tabs ?? [], // a hand-edited "tabs": null (M9 final review)
                IconSize = IconSizes.Contains(loadedFence.IconSize) ? loadedFence.IconSize : 48,
            };
            seenFenceIds.Add(fence.Id);
            inboxFound |= fence.IsInbox;

            var items = fence.Source.Kind == FenceSourceKind.Desktop
                ? (loadedFence.Items ?? []).Where(itemRef => !string.IsNullOrWhiteSpace(itemRef) && seenItems.Add(itemRef)).ToList()
                : [];
            fences.Add(fence with { Items = items });
        }

        if (!inboxFound)
        {
            fences.Insert(0, Fence.Create("Inbox") with { IsInbox = true });
        }

        return config with
        {
            Settings = settings,
            Fences = FenceTabs.Repair(fences), // M9: one consistent box per tab
            Rules = (config.Rules ?? []).Where(rule => rule is not null).Select(Rules.Repair).ToList(), // M11: a broken rule is disabled
            Layouts = NormalizeLayouts(config.Layouts),
            Library = NormalizeLibrary(config.Library),
        };
    }

    private static LibrarySettings NormalizeLibrary(LibrarySettings? library) => new()
    {
        Folders = (library?.Folders ?? []).Where(folder => !string.IsNullOrWhiteSpace(folder)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        Sources = library?.Sources ?? new LibrarySources(),
        Hidden = (library?.Hidden ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
    };

    private static Dictionary<string, Layout> NormalizeLayouts(IReadOnlyDictionary<string, Layout>? loadedLayouts)
    {
        var layouts = new Dictionary<string, Layout>();
        foreach (var (fingerprint, layout) in loadedLayouts ?? new Dictionary<string, Layout>())
        {
            if (layout is null) continue;
            layouts[fingerprint] = new Layout
            {
                Monitors = (layout.Monitors ?? new Dictionary<string, MonitorArea>())
                    .Where(entry => entry.Value is { IsUsable: true })
                    .ToDictionary(entry => entry.Key, entry => entry.Value),
                Fences = (layout.Fences ?? new Dictionary<string, FenceRect>())
                    .Where(entry => entry.Value is { } rect && !string.IsNullOrWhiteSpace(rect.Monitor)
                                    && double.IsFinite(rect.X) && double.IsFinite(rect.Y) && double.IsFinite(rect.W) && double.IsFinite(rect.H))
                    .ToDictionary(entry => entry.Key, entry => entry.Value),
            };
        }
        return layouts;
    }
}
```
`src/NeoFences.Core/Config/ConfigJson.cs`:
```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using NeoFences.Core.Model;

namespace NeoFences.Core.Config;

/// <summary>
/// JSON for <see cref="NeoFencesConfig"/>: camelCase names, camelCase enum values (spec §5 shape).
/// Reflection-based on purpose: the source generator assigns every init-only property, so a property
/// missing from a hand-edited file would lose its default (e.g. iconSize 0 instead of 48). ADR-006, amended in M1.
/// </summary>
public static class ConfigJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // Hand-edited file, never embedded in HTML: write + and & as-is instead of + and &.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Rule enums first (M11 final review I3): an unknown name in a hand-edited rule disables that rule, never the file.
        Converters =
        {
            new LenientEnumConverter<RuleKind>(), new LenientEnumConverter<TypeGroup>(), new LenientEnumConverter<GameLauncher>(),
            new LenientEnumConverter<RuleCompare>(), new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
        },
    };

    public static string Serialize(NeoFencesConfig config) => JsonSerializer.Serialize(config, Options);

    /// <summary>A snapshot file (M10): the same names, enums and leniency as config.json.</summary>
    public static string SerializeSnapshot(Snapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);

    /// <exception cref="JsonException">The text is not a snapshot document.</exception>
    public static Snapshot DeserializeSnapshot(string json) =>
        JsonSerializer.Deserialize<Snapshot>(json, Options) ?? throw new JsonException("the snapshot file contains null");

    /// <summary>The library folder's index (M12): the same names and enums as config.json.</summary>
    public static string SerializeLibrary(Library.LibraryState state) => JsonSerializer.Serialize(state, Options);

    /// <exception cref="JsonException">The text is not a library index.</exception>
    public static Library.LibraryState DeserializeLibrary(string json) =>
        JsonSerializer.Deserialize<Library.LibraryState>(json, Options) ?? throw new JsonException("the library index contains null");

    /// <summary>Raw parse; explicit nulls and odd values survive. Run the result through <c>ConfigNormalizer</c>.</summary>
    /// <exception cref="JsonException">The text is not a valid config document.</exception>
    public static NeoFencesConfig Deserialize(string json) =>
        JsonSerializer.Deserialize<NeoFencesConfig>(json, Options)
        ?? throw new JsonException("config.json contains null");
}

/// <summary>
/// A camelCase enum that reads an unknown name (or anything else odd) as an undefined value instead of failing, so
/// <see cref="Rules.Repair"/> can disable the rule (M11 final review I3). Undefined values are written as numbers.
/// </summary>
internal sealed class LenientEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    private static readonly TEnum Unknown = (TEnum)Enum.ToObject(typeof(TEnum), -1);

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String when Enum.TryParse<TEnum>(reader.GetString(), ignoreCase: true, out var named) && Enum.IsDefined(named):
                return named;
            case JsonTokenType.Number when reader.TryGetInt32(out var number):
                return (TEnum)Enum.ToObject(typeof(TEnum), number);
            case JsonTokenType.StartObject or JsonTokenType.StartArray:
                reader.Skip();
                return Unknown;
            default:
                return Unknown;
        }
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        if (Enum.IsDefined(value)) writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
        else writer.WriteNumberValue(Convert.ToInt32(value));
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test NeoFences.slnx`
Expected: `Passed! - Failed: 0, Passed: 361`

- [ ] **Step 6: Commit**

```powershell
git add src/NeoFences.Core tests/NeoFences.Core.Tests
git commit -m "feat: added the game library catalog to the core with steam and epic parsing, merging and a shortcut file plan"
```

---

### Task 2: Shell — scanners, shortcut files, menu and drag options

**Files:**
- Create: `src/NeoFences.Shell/GameScanners.cs`, `ShellLinks.cs`, `LibraryWriter.cs`.
- Replace: `src/NeoFences.Shell/ShellItemMenu.cs`, `ShellDragDrop.cs`.

**Interfaces:**
- Consumes: Task 1.
- Produces:
  - `GameScanners.ScanAll(LibrarySettings, Action<string, Exception>) → IReadOnlyList<SourceScan>` (call on an STA thread), `GameScanners.WatchFolders(LibrarySettings)`, `GameScanners.FolderKey(folder)`;
  - `ShellLinks.Read(path) → GameLaunch?`, `ShellLinks.Write(path, launch, iconPath?)`;
  - `LibraryWriter.ReadIndex(folder)`, `LibraryWriter.Apply(folder, plan, logFailure) → LibraryState`, `LibraryWriter.IsIconSource(path)`;
  - `ItemMenuChoice.Custom`, `ShellItemMenu.Show(…, customCommands, canRename, out customCommand)`;
  - `ShellDragDrop.TryDrag(…, copyOnly)`, `FenceDropHandlers.AcceptsDrops`.

- [ ] **Step 1: Files**

`src/NeoFences.Shell/ShellLinks.cs`:
```csharp
using System.Runtime.InteropServices;
using NeoFences.Core.Library;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace NeoFences.Shell;

/// <summary>
/// Reads and writes shortcut files for the Game Library (M12): <c>.lnk</c> through the shell's own link object,
/// <c>.url</c> as the small INI text Windows uses. Call on an STA thread.
/// </summary>
public static class ShellLinks
{
    private const uint RawPath = 0x4; // SLGP_RAWPATH: never resolves the link (no network wait)

    /// <summary>A shortcut's launch: a .url's URL, or a .lnk's target, arguments and working folder; null when unreadable or empty.</summary>
    public static unsafe GameLaunch? Read(string path)
    {
        if (Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            var line = File.ReadLines(path).Take(64).FirstOrDefault(text => text.StartsWith("URL=", StringComparison.OrdinalIgnoreCase));
            return line is { Length: > 4 } ? new GameLaunch(line[4..].Trim()) : null;
        }
        IShellLinkW? link = null;
        try
        {
            link = (IShellLinkW)Activator.CreateInstance(Type.GetTypeFromCLSID(typeof(ShellLink).GUID)!)!;
            fixed (char* file = path) ((IPersistFile)link).Load(file, STGM.STGM_READ);
            var buffer = new char[1024];
            fixed (char* text = buffer)
            {
                link.GetPath(text, buffer.Length, null, RawPath);
                var target = new string(text);
                link.GetArguments(text, buffer.Length);
                var arguments = new string(text);
                link.GetWorkingDirectory(text, buffer.Length);
                var folder = new string(text);
                return target.Length == 0 ? null : new GameLaunch(Environment.ExpandEnvironmentVariables(target),
                    arguments.Length == 0 ? null : arguments, folder.Length == 0 ? null : Environment.ExpandEnvironmentVariables(folder));
            }
        }
        finally
        {
            if (link is not null) Marshal.ReleaseComObject(link);
        }
    }

    /// <summary>Writes a shortcut for a launch (a .url for links, a .lnk for programs), with an icon when given.</summary>
    public static unsafe void Write(string path, GameLaunch launch, string? iconPath)
    {
        if (launch.IsLink)
        {
            var lines = new List<string> { "[InternetShortcut]", $"URL={launch.Target}" };
            if (iconPath is not null) lines.AddRange([$"IconFile={iconPath}", "IconIndex=0"]);
            File.WriteAllLines(path, lines);
            return;
        }
        IShellLinkW? link = null;
        try
        {
            link = (IShellLinkW)Activator.CreateInstance(Type.GetTypeFromCLSID(typeof(ShellLink).GUID)!)!;
            fixed (char* target = launch.Target) link.SetPath(target);
            fixed (char* arguments = launch.Arguments ?? "") link.SetArguments(arguments);
            fixed (char* folder = launch.WorkingFolder ?? Path.GetDirectoryName(launch.Target) ?? "") link.SetWorkingDirectory(folder);
            if (iconPath is not null)
            {
                fixed (char* icon = iconPath) link.SetIconLocation(icon, 0);
            }
            fixed (char* file = path) ((IPersistFile)link).Save(file, true);
        }
        finally
        {
            if (link is not null) Marshal.ReleaseComObject(link);
        }
    }
}
```
`src/NeoFences.Shell/GameScanners.cs`:
```csharp
using System.Xml.Linq;
using Microsoft.Win32;
using NeoFences.Core.Library;
using NeoFences.Core.Model;

namespace NeoFences.Shell;

/// <summary>
/// Finds installed games for the Game Library (M12, spec §2): Steam, Epic, GOG, Ubisoft Connect, EA app, Battle.net,
/// Xbox / Microsoft Store, the user's game folders and game shortcuts on the Desktop. Read-only: nothing is changed or
/// started. Each scan is isolated — a failure makes only that scan "unreadable" (its games are kept). Call on an STA
/// thread (shortcuts are read through the shell).
/// </summary>
public static class GameScanners
{
    private static readonly EnumerationOptions ProgramSearch = new()
    {
        RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public static IReadOnlyList<SourceScan> ScanAll(LibrarySettings settings, Action<string, Exception> logFailure)
    {
        var scans = new List<SourceScan>();
        void Run(string scanKey, Func<IEnumerable<SourceScan>> scan)
        {
            try
            {
                scans.AddRange(scan());
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                logFailure(scanKey, failure);
                scans.Add(new SourceScan(scanKey, false, []));
            }
        }
        var sources = settings.Sources;
        if (sources.Steam) Run("steam", ScanSteam);
        if (sources.Epic) Run("epic", () => [ScanEpic()]);
        if (sources.Gog) Run("gog", () => [ScanGog()]);
        if (sources.Ubisoft) Run("ubisoft", () => [ScanUbisoft()]);
        if (sources.Ea) Run("ea", () => [ScanUninstallEntries("ea", GameSource.Ea, "Electronic Arts", ["EA app", "Origin", "EA Desktop"])]);
        if (sources.BattleNet) Run("battlenet", () => [ScanUninstallEntries("battlenet", GameSource.BattleNet, "Blizzard Entertainment", ["Battle.net"])]);
        if (sources.Xbox) Run("xbox", () => [ScanXbox()]);
        if (sources.Folders)
        {
            foreach (var folder in settings.Folders) Run(FolderKey(folder), () => [ScanGameFolder(folder)]);
        }
        if (sources.DesktopShortcuts) Run("desktop", () => [ScanDesktopShortcuts(settings.Folders, logFailure)]);
        return scans;
    }

    /// <summary>Folders whose changes mean "installed or removed a game": Steam's library folders, Epic's manifests, game folders.</summary>
    public static IReadOnlyList<string> WatchFolders(LibrarySettings settings)
    {
        var folders = new List<string>();
        try
        {
            if (settings.Sources.Steam && SteamRoot() is { } steam)
                folders.AddRange(SteamLibraries(steam).Select(library => Path.Combine(library, "steamapps")).Where(Directory.Exists));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or FormatException)
        {
            // no watching for a broken Steam install; scans still say why
        }
        if (settings.Sources.Epic && Directory.Exists(EpicManifests)) folders.Add(EpicManifests);
        if (settings.Sources.Folders) folders.AddRange(settings.Folders.Where(Directory.Exists));
        return folders;
    }

    public static string FolderKey(string folder) => "folder:" + folder.Trim().TrimEnd('\\', '/').ToLowerInvariant();

    // --- Steam -------------------------------------------------------------------------------------------------------

    private static IEnumerable<SourceScan> ScanSteam()
    {
        if (SteamRoot() is not { } steam) return [new SourceScan("steam", true, [])]; // Steam not installed
        var scans = new List<SourceScan>();
        foreach (var library in SteamLibraries(steam))
        {
            var key = "steam:" + library.TrimEnd('\\').ToLowerInvariant();
            var steamapps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamapps))
            {
                scans.Add(new SourceScan(key, false, [])); // a library on a drive that is not there: keep its games
                continue;
            }
            var games = new List<GameEntry>();
            foreach (var manifest in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
            {
                if (SteamFiles.ParseManifest(File.ReadAllText(manifest)) is not { } app) continue;
                var installFolder = Path.Combine(steamapps, "common", app.InstallDir);
                games.Add(new GameEntry($"steam:{app.AppId}", app.Name, GameSource.Steam, key, new GameLaunch(SteamFiles.LaunchUri(app.AppId)),
                    installFolder, SteamPoster(steam, app.AppId), ProgramIn(installFolder)));
            }
            scans.Add(new SourceScan(key, true, games));
        }
        return scans;
    }

    private static string? SteamRoot()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        return key?.GetValue("SteamPath") is string path && Directory.Exists(path) ? Path.GetFullPath(path) : null;
    }

    private static IReadOnlyList<string> SteamLibraries(string steam)
    {
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        var libraries = File.Exists(vdf) ? SteamFiles.LibraryFolders(File.ReadAllText(vdf)).ToList() : [];
        if (!libraries.Any(library => string.Equals(library.TrimEnd('\\'), steam.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) libraries.Insert(0, steam);
        return libraries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Steam's cached 2:3 cover: <c>librarycache\&lt;appid&gt;\library_600x900.jpg</c> or, newer, <c>…\&lt;hash&gt;\library_capsule.jpg</c>.</summary>
    private static string? SteamPoster(string steam, string appId)
    {
        var cache = Path.Combine(steam, "appcache", "librarycache");
        var folder = Path.Combine(cache, appId);
        string[] direct = [Path.Combine(folder, "library_600x900.jpg"), Path.Combine(cache, $"{appId}_library_600x900.jpg")];
        if (direct.FirstOrDefault(File.Exists) is { } poster) return poster;
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "library_capsule.jpg", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 1 }).FirstOrDefault()
            : null;
    }

    // --- Epic --------------------------------------------------------------------------------------------------------

    private static string EpicManifests =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");

    private static SourceScan ScanEpic()
    {
        if (!Directory.Exists(EpicManifests)) return new SourceScan("epic", true, []);
        var games = new List<GameEntry>();
        foreach (var manifest in Directory.EnumerateFiles(EpicManifests, "*.item"))
        {
            if (EpicManifest.Parse(File.ReadAllText(manifest)) is not { } game) continue;
            var program = game.LaunchExecutable is { } executable ? Path.Combine(game.InstallLocation, executable) : ProgramIn(game.InstallLocation);
            games.Add(new GameEntry($"epic:{game.AppName}", game.DisplayName, GameSource.Epic, "epic", new GameLaunch(game.LaunchUri),
                game.InstallLocation, Poster: null, IconPath: program is not null && File.Exists(program) ? program : null));
        }
        return new SourceScan("epic", true, games);
    }

    // --- GOG ---------------------------------------------------------------------------------------------------------

    private static SourceScan ScanGog()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var games = machine.OpenSubKey(@"SOFTWARE\GOG.com\Games");
        if (games is null) return new SourceScan("gog", true, []);
        using var galaxyPaths = machine.OpenSubKey(@"SOFTWARE\GOG.com\GalaxyClient\paths");
        var galaxy = galaxyPaths?.GetValue("client") is string clientFolder ? Path.Combine(clientFolder, "GalaxyClient.exe") : null;
        if (galaxy is not null && !File.Exists(galaxy)) galaxy = null;
        var found = new List<GameEntry>();
        foreach (var id in games.GetSubKeyNames())
        {
            using var game = games.OpenSubKey(id);
            if (game?.GetValue("gameName") is not string name || game.GetValue("path") is not string folder || game.GetValue("exe") is not string exe) continue;
            var launch = galaxy is not null
                ? new GameLaunch(galaxy, $"/command=runGame /gameId={id} /path=\"{folder}\"", Path.GetDirectoryName(galaxy))
                : new GameLaunch(exe, game.GetValue("launchParam") as string is { Length: > 0 } parameters ? parameters : null,
                    game.GetValue("workingDir") as string is { Length: > 0 } working ? working : Path.GetDirectoryName(exe));
            found.Add(new GameEntry($"gog:{id}", name, GameSource.Gog, "gog", launch, folder, Poster: null, IconPath: File.Exists(exe) ? exe : null));
        }
        return new SourceScan("gog", true, found);
    }

    // --- Ubisoft Connect ---------------------------------------------------------------------------------------------

    private static SourceScan ScanUbisoft()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var installs = machine.OpenSubKey(@"SOFTWARE\Ubisoft\Launcher\Installs");
        if (installs is null) return new SourceScan("ubisoft", true, []);
        var found = new List<GameEntry>();
        foreach (var id in installs.GetSubKeyNames())
        {
            using var install = installs.OpenSubKey(id);
            if (install?.GetValue("InstallDir") is not string raw) continue;
            var folder = Path.GetFullPath(raw.Replace('/', '\\')).TrimEnd('\\');
            if (!Directory.Exists(folder)) continue;
            found.Add(new GameEntry($"ubisoft:{id}", Path.GetFileName(folder), GameSource.Ubisoft, "ubisoft", new GameLaunch($"uplay://launch/{id}/0"),
                folder, Poster: null, IconPath: ProgramIn(folder)));
        }
        return new SourceScan("ubisoft", true, found);
    }

    // --- EA app, Battle.net (their games' uninstall entries) ---------------------------------------------------------

    private static SourceScan ScanUninstallEntries(string scanKey, GameSource source, string publisher, string[] launcherNames)
    {
        var found = new List<GameEntry>();
        foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var entry = uninstall.OpenSubKey(name);
                if (entry?.GetValue("Publisher") is not string entryPublisher || !entryPublisher.Contains(publisher, StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.GetValue("DisplayName") is not string displayName || launcherNames.Any(launcher => displayName.StartsWith(launcher, StringComparison.OrdinalIgnoreCase))) continue;
                if (entry.GetValue("InstallLocation") is not string folder || !Directory.Exists(folder) || ProgramIn(folder) is not { } program) continue;
                found.Add(new GameEntry($"{scanKey}:{name.ToLowerInvariant()}", displayName, source, scanKey, new GameLaunch(program, WorkingFolder: Path.GetDirectoryName(program)),
                    folder.TrimEnd('\\'), Poster: null, IconPath: program));
            }
        }
        return new SourceScan(scanKey, true, found);
    }

    // --- Xbox / Microsoft Store --------------------------------------------------------------------------------------

    private static SourceScan ScanXbox()
    {
        using var packages = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
        if (packages is null) return new SourceScan("xbox", true, []);
        var found = new List<GameEntry>();
        foreach (var fullName in packages.GetSubKeyNames())
        {
            using var package = packages.OpenSubKey(fullName);
            if (package?.GetValue("PackageRootFolder") is not string folder) continue;
            var gameConfig = Path.Combine(folder, "MicrosoftGame.config");
            if (!File.Exists(gameConfig)) continue; // only game packages have one
            var visuals = XDocument.Load(gameConfig).Descendants().FirstOrDefault(element => element.Name.LocalName == "ShellVisuals");
            var appId = XDocument.Load(Path.Combine(folder, "AppxManifest.xml")).Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Application")?.Attribute("Id")?.Value;
            if (appId is null) continue;
            var name = visuals?.Attribute("DefaultDisplayName")?.Value is { Length: > 0 } shown && !shown.StartsWith("ms-resource", StringComparison.OrdinalIgnoreCase)
                ? shown
                : package.GetValue("DisplayName") as string is { Length: > 0 } listed && !listed.StartsWith("@", StringComparison.Ordinal) ? listed : fullName.Split('_')[0];
            var parts = fullName.Split('_');
            var familyName = $"{parts[0]}_{parts[^1]}";
            var logo = (visuals?.Attribute("Square480x480Logo") ?? visuals?.Attribute("Square150x150Logo") ?? visuals?.Attribute("StoreLogo"))?.Value;
            found.Add(new GameEntry($"xbox:{familyName.ToLowerInvariant()}", name, GameSource.Xbox, "xbox",
                new GameLaunch(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"shell:AppsFolder\\{familyName}!{appId}"),
                folder, Poster: null, IconPath: logo is null ? null : ScaledAsset(folder, logo)));
        }
        return new SourceScan("xbox", true, found);
    }

    /// <summary>A package image: the file itself, or its largest ".scale-NNN" variant.</summary>
    private static string? ScaledAsset(string packageFolder, string relative)
    {
        var path = Path.Combine(packageFolder, relative.Replace('/', '\\'));
        if (File.Exists(path)) return path;
        var folder = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(folder)) return null;
        return Directory.EnumerateFiles(folder, $"{Path.GetFileNameWithoutExtension(path)}.scale-*{Path.GetExtension(path)}")
            .OrderByDescending(file => new FileInfo(file).Length).FirstOrDefault();
    }

    // --- My game folders ---------------------------------------------------------------------------------------------

    private static SourceScan ScanGameFolder(string folder)
    {
        var key = FolderKey(folder);
        if (!Directory.Exists(folder)) return new SourceScan(key, false, []); // a drive that is not there: keep its games
        var found = new List<GameEntry>();
        foreach (var game in Directory.EnumerateDirectories(folder))
        {
            if (ProgramIn(game) is not { } program) continue; // no program found: not listed
            found.Add(new GameEntry("folder:" + game.ToLowerInvariant(), Path.GetFileName(game), GameSource.Folder, key,
                new GameLaunch(program, WorkingFolder: Path.GetDirectoryName(program)), game, Poster: null, IconPath: program));
        }
        return new SourceScan(key, true, found);
    }

    /// <summary>The program that starts a game in this folder (<see cref="ProgramPicker"/>), or null.</summary>
    private static string? ProgramIn(string? folder)
    {
        if (folder is null || !Directory.Exists(folder)) return null;
        var programs = new DirectoryInfo(folder).EnumerateFiles("*.exe", ProgramSearch)
            .Select(file => (Path.GetRelativePath(folder, file.FullName), file.Length));
        return ProgramPicker.Pick(programs) is { } relative ? Path.Combine(folder, relative) : null;
    }

    // --- Game shortcuts on the Desktop -------------------------------------------------------------------------------

    private static SourceScan ScanDesktopShortcuts(IReadOnlyList<string> gameFolders, Action<string, Exception> logFailure)
    {
        var listing = DesktopItems.Enumerate();
        var found = new List<GameEntry>();
        foreach (var itemRef in listing.ItemRefs.Where(itemRef => Path.GetExtension(itemRef).ToLowerInvariant() is ".lnk" or ".url"))
        {
            GameLaunch? launch;
            try
            {
                launch = ShellLinks.Read(itemRef);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                logFailure(itemRef, failure);
                continue;
            }
            if (launch is null) continue;
            var gameFolder = gameFolders.Select(root => GameFolderOf(root, launch.Target)).FirstOrDefault(found => found is not null);
            var asText = launch.Arguments is null ? launch.Target : $"{launch.Target} {launch.Arguments}";
            if (gameFolder is null && Rules.LauncherOf(asText) is null) continue;
            var installFolder = gameFolder ?? (launch.IsLink ? null : Path.GetDirectoryName(launch.Target));
            found.Add(new GameEntry("desktop:" + itemRef.ToLowerInvariant(), Path.GetFileNameWithoutExtension(itemRef), GameSource.DesktopShortcut, "desktop",
                launch, installFolder, Poster: null, IconPath: launch.IsLink ? null : launch.Target));
        }
        // A Desktop folder that could not be listed: keep the shortcuts found before.
        return new SourceScan("desktop", listing.UnavailableFolders.Count == 0, found);
    }

    /// <summary>The game sub-folder of a game folder that holds this target, or null.</summary>
    private static string? GameFolderOf(string root, string target)
    {
        var prefix = root.TrimEnd('\\') + "\\";
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var slash = target.IndexOf('\\', prefix.Length);
        return slash < 0 ? null : target[..slash];
    }
}
```
`src/NeoFences.Shell/LibraryWriter.cs`:
```csharp
using NeoFences.Core.Config;
using NeoFences.Core.Library;

namespace NeoFences.Shell;

/// <summary>
/// Keeps NeoFences' library folder in step with a <see cref="LibraryPlan"/> (M12, spec §3): writes changed shortcuts
/// (temp file, then swapped in), removes only files listed in its own index, and saves the index. User files are never
/// touched: the folder is NeoFences' own. Call on an STA thread.
/// </summary>
public static class LibraryWriter
{
    public const string IndexFileName = "index.json";

    /// <summary>The saved index, or an empty one when missing or damaged (the next scan rebuilds it).</summary>
    public static LibraryState ReadIndex(string folder)
    {
        try
        {
            var path = Path.Combine(folder, IndexFileName);
            return File.Exists(path) ? ConfigJson.DeserializeLibrary(File.ReadAllText(path)) : new LibraryState();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return new LibraryState();
        }
    }

    /// <returns>The state actually on disk: an item whose file could not be written is left out (it is tried again next time).</returns>
    public static LibraryState Apply(string folder, LibraryPlan plan, Action<string, Exception> logFailure)
    {
        Directory.CreateDirectory(folder);
        foreach (var fileName in plan.Delete)
        {
            try
            {
                File.Delete(Path.Combine(folder, fileName)); // our own shortcut, listed in our index
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                logFailure(fileName, failure);
            }
        }
        var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toWrite = plan.Items.Where(item => plan.Write.Contains(item) || !File.Exists(Path.Combine(folder, item.FileName))); // also files deleted by hand
        foreach (var item in toWrite)
        {
            var path = Path.Combine(folder, item.FileName);
            var temp = Path.Combine(folder, $".{Guid.NewGuid():N}{Path.GetExtension(item.FileName)}");
            try
            {
                ShellLinks.Write(temp, item.Game.Launch, item.Game.IconPath is { } icon && IsIconSource(icon) ? icon : null);
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                logFailure(item.FileName, failure);
                failed.Add(item.FileName);
                try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        var state = new LibraryState { Items = plan.Items.Where(item => !failed.Contains(item.FileName)).ToList() };
        var index = Path.Combine(folder, IndexFileName);
        var indexTemp = index + ".tmp";
        File.WriteAllText(indexTemp, ConfigJson.SerializeLibrary(state));
        File.Move(indexTemp, index, overwrite: true);
        return state;
    }

    /// <summary>Shortcut icons come from programs and icon files, not from PNG logos (those show on the tile instead).</summary>
    public static bool IsIconSource(string path) => Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".ico" or ".dll";
}
```
`src/NeoFences.Shell/ShellItemMenu.cs`:
```csharp
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Shell;

/// <summary>What the user picked in Windows' item menu that NeoFences runs itself instead of the shell.</summary>
/// <summary>Custom: one of the caller's own commands (the Game Library's "Hide from library", M12).</summary>
public enum ItemMenuChoice { None, Rename, Delete, Custom }

/// <summary>
/// Windows' own right-click menu for desktop items (the classic menu: Open, Open with, Send to, Properties, shell
/// extensions; the Windows 11 compact menu is Explorer-private, spec §6). Rename and Delete are handed back so
/// NeoFences renames in place and always recycles (hard rule 1, user decision 2026-10-02).
/// </summary>
public static class ShellItemMenu
{
    private const uint FirstCommandId = 1;
    private const uint LastCommandId = 0x7FFF;
    private const uint CmicMaskUnicode = 0x00004000; // not in the Win32 metadata CsWin32 reads (shobjidl.h)

    /// <summary>The menu being shown, for <see cref="HandleMenuMessage"/> (owner-drawn and lazy submenus like "Send to").</summary>
    private static IContextMenu2? _openMenu;

    /// <summary>Call from the owner window's message hook while a menu may be open; true when the menu consumed it.</summary>
    public static unsafe bool HandleMenuMessage(int message, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (_openMenu is null) return false;
        const int WmInitMenuPopup = 0x0117, WmDrawItem = 0x002B, WmMeasureItem = 0x002C, WmMenuChar = 0x0120;
        if (message is not (WmInitMenuPopup or WmDrawItem or WmMeasureItem or WmMenuChar)) return false;
        try
        {
            if (_openMenu is IContextMenu3 menu3)
            {
                LRESULT handledResult;
                menu3.HandleMenuMsg2((uint)message, (WPARAM)(nuint)wParam, (LPARAM)lParam, &handledResult);
                result = handledResult;
            }
            else
            {
                _openMenu.HandleMenuMsg((uint)message, (WPARAM)(nuint)wParam, (LPARAM)lParam);
            }
            return true;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // CsWin32 turns an extension's failure HRESULT into NotImplementedException, InvalidCastException, … (M3a review C1).
            return false;
        }
    }

    /// <summary>
    /// Shows the menu for these items at a screen point and runs the chosen shell command.
    /// </summary>
    /// <param name="extended">Shift held: the extended menu ("Copy as path", "Open PowerShell here", …).</param>
    /// <returns>Rename/Delete for the caller to run; None when the shell ran the command, the user cancelled, or the menu could not be built.</returns>
    /// <param name="logFailure">Told when the menu could not be built or a command failed (a broken shell extension).</param>
    public static ItemMenuChoice Show(nint ownerHandle, IReadOnlyList<string> itemRefs, int screenX, int screenY, bool extended, Action<Exception> logFailure) =>
        Show(ownerHandle, itemRefs, screenX, screenY, extended, logFailure, customCommands: [], canRename: true, out _);

    /// <param name="customCommands">Added at the end, after a separator; the chosen one comes back as <paramref name="customCommand"/>.</param>
    /// <param name="canRename">False: the shell's Rename is not offered (a Game Library shortcut, M12).</param>
    public static unsafe ItemMenuChoice Show(nint ownerHandle, IReadOnlyList<string> itemRefs, int screenX, int screenY, bool extended, Action<Exception> logFailure,
        IReadOnlyList<string> customCommands, bool canRename, out int customCommand)
    {
        customCommand = -1;
        var owner = (HWND)ownerHandle;
        HMENU menu = default;
        IContextMenu? contextMenu = null;
        try
        {
            if (itemRefs.Count == 0) return ItemMenuChoice.None;
            contextMenu = (IContextMenu)DesktopNamespace.GetUIObject(owner, itemRefs, typeof(IContextMenu).GUID);

            menu = PInvoke.CreatePopupMenu();
            var flags = PInvoke.CMF_NORMAL | (canRename ? PInvoke.CMF_CANRENAME : 0) | (extended ? PInvoke.CMF_EXTENDEDVERBS : 0);
            var queried = contextMenu.QueryContextMenu(menu, 0, FirstCommandId, LastCommandId, flags);
            if (queried.Failed) // a broken extension can fail the whole menu without throwing (M3a review)
            {
                logFailure(new COMException("QueryContextMenu failed", queried.Value));
                return ItemMenuChoice.None;
            }
            if (customCommands.Count > 0) PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (PCWSTR)null);
            for (var index = 0; index < customCommands.Count; index++)
            {
                fixed (char* text = customCommands[index]) PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, LastCommandId + 1 + (uint)index, text);
            }
            _openMenu = contextMenu as IContextMenu2;

            PInvoke.SetForegroundWindow(owner); // the menu closes on a click elsewhere only if its owner is foreground
            var command = (uint)PInvoke.TrackPopupMenuEx(menu,
                (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON), screenX, screenY, owner, null).Value;
            // The documented menu dance: when the fence could not become foreground, this lets the next click close it (M3a review).
            PInvoke.PostMessage(owner, PInvoke.WM_NULL, 0, 0);
            _openMenu = null;
            if (command < FirstCommandId) return ItemMenuChoice.None;
            if (command > LastCommandId)
            {
                customCommand = (int)(command - LastCommandId - 1);
                return ItemMenuChoice.Custom;
            }

            var verb = Verb(contextMenu, command - FirstCommandId);
            if (string.Equals(verb, "rename", StringComparison.OrdinalIgnoreCase)) return ItemMenuChoice.Rename;
            if (string.Equals(verb, "delete", StringComparison.OrdinalIgnoreCase)) return ItemMenuChoice.Delete;
            Invoke(contextMenu, command - FirstCommandId, owner, screenX, screenY);
            return ItemMenuChoice.None;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            logFailure(failure); // the item vanished or a shell extension failed: no menu or no command, nothing lost
            return ItemMenuChoice.None;
        }
        finally
        {
            _openMenu = null;
            if (!menu.IsNull) PInvoke.DestroyMenu(menu);
            if (contextMenu is not null) Marshal.ReleaseComObject(contextMenu);
        }
    }

    private static unsafe string? Verb(IContextMenu contextMenu, uint offset)
    {
        const int Length = 64;
        var buffer = stackalloc char[Length];
        try
        {
            contextMenu.GetCommandString(offset, PInvoke.GCS_VERBW, null, (PSTR)(byte*)buffer, Length);
            var verb = new ReadOnlySpan<char>(buffer, Length); // bounded: an extension may fill it without a terminator
            var end = verb.IndexOf('\0');
            return new string(end >= 0 ? verb[..end] : verb);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return null; // many commands have no verb (E_NOTIMPL, E_INVALIDARG): they still run through Invoke
        }
    }

    private static unsafe void Invoke(IContextMenu contextMenu, uint offset, HWND owner, int screenX, int screenY)
    {
        var info = new CMINVOKECOMMANDINFOEX
        {
            cbSize = (uint)sizeof(CMINVOKECOMMANDINFOEX),
            fMask = CmicMaskUnicode | PInvoke.CMIC_MASK_PTINVOKE,
            hwnd = owner,
            lpVerb = (PCSTR)(byte*)offset,
            lpVerbW = (PCWSTR)(char*)offset,
            nShow = (int)SHOW_WINDOW_CMD.SW_SHOWNORMAL,
            ptInvoke = new System.Drawing.Point(screenX, screenY),
        };
        PInvoke.AllowSetForegroundWindow(unchecked((uint)-1)); // what the command opens may come to the front
        contextMenu.InvokeCommand((CMINVOKECOMMANDINFO*)&info);
    }
}
```
`src/NeoFences.Shell/ShellDragDrop.cs`:
```csharp
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Shell;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace NeoFences.Shell;

/// <summary>Where a drop on a fence lands, asked of the fence for a screen point (physical pixels).</summary>
/// <param name="ItemRef">The item whose "drop into" zone is under the point (Core DropZones), if any.</param>
/// <param name="InsertAt">Index in the fence's shown list to insert before (see FenceMembership.MoveItems).</param>
public readonly record struct FenceDropPoint(string? ItemRef, int InsertAt);

/// <summary>What a fence does with drops; NeoFences.App supplies these (all called on the UI thread).</summary>
/// <param name="HitTest">Item and insert position under a screen point.</param>
/// <param name="MoveItems">Desktop items dropped on the fence (from a fence or from Explorer's Desktop): membership only, no file operation.</param>
/// <param name="ExpectArrivals">Desktop refs Windows is about to copy or move onto the Desktop for this drop, and where they go.</param>
/// <param name="Recycle">Files dropped on the Recycle Bin item: always recycled by NeoFences, never deleted (hard rule 1).</param>
/// <param name="ShowFeedback">Where the drop would land (null hides it): an insertion caret, or a highlighted container when into is true.</param>
/// <param name="LogFailure">A drop that could not be handed to Windows.</param>
public sealed record FenceDropHandlers(
    Func<int, int, FenceDropPoint> HitTest,
    Action<IReadOnlyList<string>, int> MoveItems,
    Action<IReadOnlyList<string>, int> ExpectArrivals,
    Action<IReadOnlyList<string>> Recycle,
    Action<FenceDropPoint?, bool> ShowFeedback,
    Action<Exception> LogFailure,
    Func<bool>? AcceptsDrops = null);

/// <summary>
/// Drag-drop with Windows' own engine (spec §6, M3b). Dragging out uses the shell's data object for the items, so
/// apps and Explorer get real files (copy/move/link as they decide) and Windows draws the drag image. Drops on a fence:
/// <list type="bullet">
/// <item>desktop items (from any fence, or Explorer showing the Desktop folder) only change membership: no file operation;</item>
/// <item>on an item that takes drops (folder, Recycle Bin, program) they go to that item, as on the desktop;</item>
/// <item>anything else goes to the Desktop folder's own drop target (Windows copies/moves, with progress and Undo),
/// and the arriving files are placed in this fence at the drop position.</item>
/// </list>
/// </summary>
public static class ShellDragDrop
{
    /// <summary>Items being dragged out of a fence right now (one drag at a time; drops on fences are membership moves).</summary>
    internal static IReadOnlyList<string>? CurrentDrag { get; private set; }

    /// <summary>Starts a drag of these items; returns when it ends. The drag image, cursor and effects are Windows'.</summary>
    /// <returns>False when the drag could not start (an item vanished, a broken shell extension).</returns>
    /// <param name="copyOnly">Copy or link only, never move (a Game Library shortcut stays in the library, M12).</param>
    public static unsafe bool TryDrag(nint ownerHandle, IReadOnlyList<string> itemRefs, Action<Exception> logFailure, bool copyOnly = false)
    {
        ComDataObject? dataObject = null;
        try
        {
            dataObject = (ComDataObject)DesktopNamespace.GetUIObject((HWND)ownerHandle, itemRefs, typeof(ComDataObject).GUID);
            CurrentDrag = itemRefs;
            PInvoke.SHDoDragDrop((HWND)ownerHandle, dataObject, null,
                DROPEFFECT.DROPEFFECT_COPY | DROPEFFECT.DROPEFFECT_LINK | (copyOnly ? 0 : DROPEFFECT.DROPEFFECT_MOVE), out _);
            return true;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            logFailure(failure);
            return false;
        }
        finally
        {
            CurrentDrag = null;
            if (dataObject is not null) Marshal.ReleaseComObject(dataObject);
        }
    }

    /// <summary>Makes the fence window a drop target. Dispose (before the window is destroyed) to unregister.</summary>
    /// <param name="portalFolder">For a Portal fence: the folder it shows right now (drops become real moves/copies into it, M4).</param>
    public static IDisposable RegisterFence(nint fenceHandle, FenceDropHandlers handlers, Func<string?>? portalFolder = null)
    {
        var target = new FenceDropTarget((HWND)fenceHandle, handlers, portalFolder);
        // WPF registers its own drop target on every window; NeoFences does not use WPF drag-drop, so replace it.
        PInvoke.RevokeDragDrop((HWND)fenceHandle);
        PInvoke.RegisterDragDrop((HWND)fenceHandle, target).ThrowOnFailure();
        return target;
    }

    private static readonly ushort FileGroupDescriptorFormat = (ushort)PInvoke.RegisterClipboardFormat("FileGroupDescriptorW");

    /// <summary>True when the source describes virtual files (zip contents, phones, mail attachments).</summary>
    internal static unsafe bool OffersVirtualFiles(IDataObject dataObject)
    {
        var format = DescriptorFormat();
        try { return dataObject.QueryGetData(&format).Value == 0; } // S_OK; S_FALSE and errors mean no
        catch (Exception failure) when (failure is not OutOfMemoryException) { return false; }
    }

    /// <summary>The names of virtual files being dropped (no extraction); empty when there are none.</summary>
    internal static unsafe List<string> VirtualFileNames(IDataObject dataObject)
    {
        var names = new List<string>();
        var format = DescriptorFormat();
        STGMEDIUM medium = default;
        try
        {
            dataObject.GetData(&format, out medium);
            // The source is another program: only a memory block, and only as many descriptors as it really holds (a bad
            // count would read past it, which .NET cannot catch; M8c review I5).
            if (medium.tymed != TYMED.TYMED_HGLOBAL) return names;
            var size = (long)(nuint)PInvoke.GlobalSize(medium.u.hGlobal);
            var group = (FILEGROUPDESCRIPTORW*)PInvoke.GlobalLock(medium.u.hGlobal);
            if (group is null) return names;
            try
            {
                if (size < sizeof(uint) || group->cItems > (size - sizeof(uint)) / sizeof(FILEDESCRIPTORW)) return names;
                var descriptors = &group->fgd.e0;
                for (var index = 0; index < group->cItems; index++)
                {
                    var name = descriptors[index].cFileName.ToString();
                    if (!name.Contains('\\')) names.Add(name); // files in subfolders arrive inside their folder
                }
            }
            finally
            {
                PInvoke.GlobalUnlock(medium.u.hGlobal);
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // No descriptors: nothing to place; Windows still handles the drop.
        }
        finally
        {
            if (medium.u.hGlobal.Value != null) PInvoke.ReleaseStgMedium(ref medium);
        }
        return names;
    }

    private static FORMATETC DescriptorFormat() => new()
    {
        cfFormat = FileGroupDescriptorFormat,
        dwAspect = (uint)DVASPECT.DVASPECT_CONTENT,
        lindex = -1,
        tymed = (uint)TYMED.TYMED_HGLOBAL,
    };

    /// <summary>Paths in the drop's file list (CF_HDROP); empty for virtual items (zip contents, phones).</summary>
    internal static unsafe List<string> DroppedFiles(IDataObject dataObject)
    {
        var files = new List<string>();
        var format = new FORMATETC
        {
            cfFormat = (ushort)CLIPBOARD_FORMAT.CF_HDROP,
            dwAspect = (uint)DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = (uint)TYMED.TYMED_HGLOBAL,
        };
        STGMEDIUM medium = default;
        try
        {
            dataObject.GetData(&format, out medium);
            var drop = (HDROP)(nint)medium.u.hGlobal.Value;
            var count = PInvoke.DragQueryFile(drop, uint.MaxValue, default, 0);
            var buffer = new char[32768];
            for (uint index = 0; index < count; index++)
            {
                var length = PInvoke.DragQueryFile(drop, index, buffer);
                files.Add(new string(buffer, 0, (int)length));
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // No file list: nothing to place; Windows still handles the drop.
        }
        finally
        {
            if (medium.u.hGlobal.Value != null) PInvoke.ReleaseStgMedium(ref medium);
        }
        return files;
    }

    /// <summary>
    /// IDropTarget for one fence. Forwards to the shell (the hovered item's drop target, or the Desktop folder's) for
    /// real file drops, and keeps Windows' drag image visible over the fence (IDropTargetHelper).
    /// </summary>
    private sealed unsafe class FenceDropTarget(HWND fence, FenceDropHandlers handlers, Func<string?>? portalFolder) : IDropTarget, IDisposable
    {
        private IDataObject? _dataObject;
        private bool _desktopItemsOnly;      // desktop fence: items already on the Desktop (membership only)
        private string? _portalFolder;       // Portal fence: the folder it shows (drops are real file operations)
        private bool _sameFolderOnly;        // Portal fence: items already in that folder (nothing to do)
        private string? _hoveredItem;        // item whose own drop target is active
        private readonly Dictionary<string, bool> _containers = new(StringComparer.OrdinalIgnoreCase); // per drag: DragOver runs per mouse move
        private bool _overRecycleBin;         // NeoFences recycles itself here (never the Recycle Bin's own drop)
        private IReadOnlyList<string> _dragged = []; // what this drag carries (no disk access after DragEnter)
        private bool _virtualSource;         // zip contents, phones: never asked for CF_HDROP (that extracts every file)
        private IDropTarget? _shellTarget;   // forwarded-to target while it is entered
        private IDropTargetHelper? _imageHelper;

        public void DragEnter(IDataObject pDataObj, MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect)
        {
            LeaveShellTarget(); // a previous drag that failed half-way must not leak into this one
            Reset();
            _dataObject = pDataObj;
            // A virtual source (zip contents, a phone) would extract every file to answer CF_HDROP: not here, not on drop (M3b/M8c review).
            _virtualSource = CurrentDrag is null && OffersVirtualFiles(pDataObj);
            _dragged = CurrentDrag ?? (_virtualSource ? [] : DroppedFiles(pDataObj));
            UseFolder(portalFolder?.Invoke());
            _imageHelper = TryCreateImageHelper();
            var allowed = *pdwEffect;
            Update(grfKeyState, pt, pdwEffect, allowed);
            var effect = *pdwEffect;
            WithImageHelper(helper =>
            {
                var point = new System.Drawing.Point(pt.x, pt.y);
                helper.DragEnter(fence, pDataObj, &point, effect);
            });
        }

        public void DragOver(MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect)
        {
            Update(grfKeyState, pt, pdwEffect, *pdwEffect);
            var effect = *pdwEffect;
            WithImageHelper(helper =>
            {
                var point = new System.Drawing.Point(pt.x, pt.y);
                helper.DragOver(&point, effect);
            });
        }

        public void DragLeave()
        {
            LeaveShellTarget();
            WithImageHelper(helper => helper.DragLeave());
            handlers.ShowFeedback(null, false);
            Reset();
        }

        public void Drop(IDataObject pDataObj, MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect)
        {
            var allowed = *pdwEffect;
            try
            {
                Update(grfKeyState, pt, pdwEffect, allowed);
                if (handlers.AcceptsDrops?.Invoke() == false) return; // effect already "none"
                var shownEffect = *pdwEffect;
                WithImageHelper(helper =>
                {
                    var point = new System.Drawing.Point(pt.x, pt.y);
                    helper.Drop(pDataObj, &point, shownEffect);
                });
                var drop = handlers.HitTest(pt.x, pt.y);
                if (_overRecycleBin)
                {
                    handlers.Recycle(CurrentDrag ?? (_virtualSource ? [] : DroppedFiles(pDataObj))); // always the Recycle Bin, never a delete
                    *pdwEffect = DROPEFFECT.DROPEFFECT_NONE;
                    return;
                }
                if (_shellTarget is null && _sameFolderOnly)
                {
                    *pdwEffect = DROPEFFECT.DROPEFFECT_NONE; // a Portal item dropped back into its own folder
                    return;
                }
                if (_shellTarget is null)
                {
                    // Membership only. Report "none" so the source never deletes anything after a "move".
                    handlers.MoveItems(CurrentDrag ?? DroppedFiles(pDataObj), drop.InsertAt);
                    *pdwEffect = DROPEFFECT.DROPEFFECT_NONE;
                    return;
                }
                if (_hoveredItem is null && _portalFolder is null)
                {
                    // Desktop items in a mixed drag join this fence; the rest arrive through Windows (M3b review).
                    var files = CurrentDrag ?? (_virtualSource ? [] : DroppedFiles(pDataObj));
                    var onDesktop = files.Where(DesktopNamespace.IsDesktopItem).ToList();
                    if (onDesktop.Count > 0) handlers.MoveItems(onDesktop, drop.InsertAt);
                    var arriving = files.Count > 0 ? files.Except(onDesktop).Select(Path.GetFileName).OfType<string>().ToList() : VirtualFileNames(pDataObj);
                    // Files and their possible shortcuts are separate lists at the same place: each file keeps its own slot
                    // (one interleaved list spread the files over every other position; M8c review I2).
                    handlers.ExpectArrivals(DesktopRefsFor(arriving), drop.InsertAt + onDesktop.Count);
                    handlers.ExpectArrivals(DesktopRefsFor(arriving.Select(ShortcutName)), drop.InsertAt + onDesktop.Count);
                }
                var target = _shellTarget;
                _shellTarget = null; // Drop replaces DragLeave for it
                try
                {
                    *pdwEffect = allowed; // the source's choices, so a right-button drop menu offers them all
                    target.Drop(pDataObj, grfKeyState, pt, pdwEffect);
                }
                finally
                {
                    Marshal.ReleaseComObject(target);
                }
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                handlers.LogFailure(failure);
                *pdwEffect = DROPEFFECT.DROPEFFECT_NONE;
            }
            finally
            {
                LeaveShellTarget();
                handlers.ShowFeedback(null, false);
                Reset();
            }
        }

        public void Dispose()
        {
            PInvoke.RevokeDragDrop(fence);
            Reset();
        }

        /// <summary>Picks where the drag would land now and asks it for the effect (or computes ours).</summary>
        private void Update(MODIFIERKEYS_FLAGS keys, POINTL pt, DROPEFFECT* effect, DROPEFFECT allowed)
        {
            if (handlers.AcceptsDrops?.Invoke() == false)
            {
                // The Game Library shows NeoFences' own shortcuts: nothing is dropped into it (M12).
                LeaveShellTarget();
                handlers.ShowFeedback(null, false);
                *effect = DROPEFFECT.DROPEFFECT_NONE;
                return;
            }
            try
            {
                var drop = handlers.HitTest(pt.x, pt.y);
                // Hovering a tab header shows that tab (M9): a Portal tab and a desktop tab take drops differently.
                if (portalFolder?.Invoke() is var shown && !string.Equals(shown, _portalFolder, StringComparison.OrdinalIgnoreCase))
                {
                    LeaveShellTarget();
                    UseFolder(shown);
                }
                var hovered = drop.ItemRef;
                var dropsOnItem = hovered is not null && !(CurrentDrag?.Contains(hovered, StringComparer.OrdinalIgnoreCase) ?? false)
                                  && IsDropContainer(hovered);
                var wantedItem = dropsOnItem ? hovered : null;
                var wantsShell = dropsOnItem || !(_desktopItemsOnly || _sameFolderOnly);

                handlers.ShowFeedback(drop, wantedItem is not null);
                // The Recycle Bin's own drop target deletes permanently with Shift held, or refuses: NeoFences recycles
                // itself and only shows "move" here, whatever keys are held (M3b review C1).
                _overRecycleBin = string.Equals(wantedItem, DesktopItems.RecycleBinRef, StringComparison.OrdinalIgnoreCase);
                if (_overRecycleBin)
                {
                    LeaveShellTarget();
                    *effect = allowed & DROPEFFECT.DROPEFFECT_MOVE;
                    return;
                }
                if (_shellTarget is not null && (!wantsShell || wantedItem != _hoveredItem)) LeaveShellTarget();
                if (!wantsShell)
                {
                    // Desktop fence: a membership move, nothing on disk changes. Portal: already in this folder.
                    *effect = _sameFolderOnly ? DROPEFFECT.DROPEFFECT_NONE : allowed & DROPEFFECT.DROPEFFECT_MOVE;
                    return;
                }
                if (_shellTarget is null)
                {
                    _shellTarget = (IDropTarget)(wantedItem is null
                        ? (_portalFolder is null ? DesktopNamespace.DesktopDropTarget(fence) : DesktopNamespace.FolderDropTarget(fence, _portalFolder))
                        : DesktopNamespace.GetUIObject(fence, [wantedItem], typeof(IDropTarget).GUID));
                    _hoveredItem = wantedItem;
                    *effect = allowed;
                    _shellTarget.DragEnter(_dataObject!, keys, pt, effect);
                    return;
                }
                *effect = allowed;
                _shellTarget.DragOver(keys, pt, effect);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                handlers.LogFailure(failure);
                *effect = DROPEFFECT.DROPEFFECT_NONE;
            }
        }

        /// <summary>The drag image is cosmetic: a failing helper must not break the drop or leave state behind.</summary>
        private void WithImageHelper(Action<IDropTargetHelper> call)
        {
            if (_imageHelper is null) return;
            try { call(_imageHelper); }
            catch (Exception failure) when (failure is not OutOfMemoryException) { handlers.LogFailure(failure); }
        }

        private void LeaveShellTarget()
        {
            if (_shellTarget is null) return;
            try { _shellTarget.DragLeave(); }
            catch (Exception failure) when (failure is not OutOfMemoryException) { handlers.LogFailure(failure); }
            Marshal.ReleaseComObject(_shellTarget);
            _shellTarget = null;
            _hoveredItem = null;
        }

        /// <summary>What the fence shown now is: a Portal of this folder, or a desktop fence (null).</summary>
        private void UseFolder(string? folder)
        {
            _portalFolder = folder;
            _desktopItemsOnly = _portalFolder is null && _dragged.Count > 0 && _dragged.All(DesktopNamespace.IsDesktopItem);
            _sameFolderOnly = _portalFolder is not null && _dragged.Count > 0
                && _dragged.All(itemRef => string.Equals(Path.GetDirectoryName(itemRef), _portalFolder, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsDropContainer(string itemRef)
        {
            if (!_containers.TryGetValue(itemRef, out var container)) _containers[itemRef] = container = DesktopNamespace.IsDropContainer(fence, itemRef);
            return container;
        }

        private void Reset()
        {
            _containers.Clear();
            _dataObject = null;
            _desktopItemsOnly = false;
            _portalFolder = null;
            _sameFolderOnly = false;
            _overRecycleBin = false;
            _virtualSource = false;
            _dragged = [];
            if (_imageHelper is not null) Marshal.ReleaseComObject(_imageHelper);
            _imageHelper = null;
        }

        private static IDropTargetHelper? TryCreateImageHelper()
        {
            try
            {
                return (IDropTargetHelper)Activator.CreateInstance(Type.GetTypeFromCLSID(PInvoke.CLSID_DragDropHelper)!)!;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                return null; // no drag image over fences; the drop still works
            }
        }

        /// <summary>Where Windows will put dropped files: the user's Desktop, same names. Renamed copies ("name (2)") go to the Inbox.</summary>
        private static List<string> DesktopRefsFor(IEnumerable<string> fileNames) =>
            fileNames.Select(name => Path.Combine(DesktopItems.UserDesktop, name)).ToList();

        /// <summary>The name Windows gives a shortcut it makes on a drop (Alt, or a link-only source).</summary>
        // ponytail: the English " - Shortcut" suffix; other display languages name links differently (their links go to the Inbox).
        private static string ShortcutName(string fileName) => fileName + " - Shortcut.lnk";
    }
}
```

- [ ] **Step 2: Build and test**

Run: `dotnet build NeoFences.slnx`, then `dotnet test NeoFences.slnx`
Expected: 0 warnings, 0 errors; `Passed: 361`.

- [ ] **Step 3: Commit**

```powershell
git add src/NeoFences.Shell
git commit -m "feat: added game scanners, shortcut files and library menu and drag options to the shell"
```

---

### Task 3: App — the library fence, tiles, scans, menus, Settings card

**Files:**
- Create: `src/NeoFences.App/FenceHost.Library.cs`, `SettingsWindow.Library.cs`.
- Replace: `src/NeoFences.App/FenceHost.cs`, `FenceWindow.xaml`, `FenceWindow.xaml.cs`, `FenceItemView.cs`, `SettingsWindow.xaml`, `SettingsWindow.xaml.cs`, `AppPaths.cs`.

**Interfaces:**
- Consumes: Tasks 1 and 2.
- Produces:
  - `AppPaths.LibraryDirectory`;
  - `FenceWindow.IsLibrary`, `SetLibraryArt(map)`, events `NewLibraryRequested`, `RefreshLibraryRequested`;
  - `FenceItemView.IsTile / Art / ArtKind / ArtPath`;
  - `SettingsView(…, Library)`, `LibraryView`, `HiddenGame(Id, Name, AllIds)`;
  - `SettingsWindow` events `LibraryFoldersChanged`, `LibrarySourcesChanged`, `ShowGameAgainRequested`, `RefreshLibraryRequested`.

- [ ] **Step 1: Files**

`src/NeoFences.App/AppPaths.cs`:
```csharp
using System.IO;

namespace NeoFences.App;

/// <summary>Runtime data lives in %LOCALAPPDATA%\NeoFences (config.json, backups\, logs\, watchdog state).</summary>
public static class AppPaths
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeoFences");

    public static string LogsDirectory { get; } = Path.Combine(DataDirectory, "logs");

    /// <summary>The Game Library's shortcuts and index (M12): NeoFences' own files.</summary>
    public static string LibraryDirectory { get; } = Path.Combine(DataDirectory, "library");
}
```
`src/NeoFences.App/FenceItemView.cs`:
```csharp
using System.ComponentModel;
using System.IO;
using System.Windows.Media;

namespace NeoFences.App;

/// <summary>One desktop item as a fence shows it. Label and icon start as placeholders and fill in from <see cref="IconLoader"/>.</summary>
public sealed class FenceItemView(string itemRef) : INotifyPropertyChanged
{
    public string ItemRef { get; } = itemRef;

    /// <summary>A shortcut (.lnk, .url, .pif): gets the arrow overlay when Settings shows shortcut arrows (M8b).</summary>
    public bool IsShortcut { get; } = Path.GetExtension(itemRef).ToLowerInvariant() is ".lnk" or ".url" or ".pif";

    public string Label
    {
        get;
        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label))); }
    } = itemRef.StartsWith("::", StringComparison.Ordinal) ? "" : Path.GetFileNameWithoutExtension(itemRef);

    /// <summary>The icon size last requested (UI thread): a slower, older load of another size never wins (final review I4).</summary>
    public int WantedSizePx { get; set; }

    public ImageSource? Icon
    {
        get;
        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon))); }
    }

    /// <summary>The label is being renamed in place (F2 or the item menu's Rename).</summary>
    public bool IsEditing
    {
        get;
        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditing))); }
    }

    /// <summary>Text in the rename box.</summary>
    public string EditName
    {
        get;
        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditName))); }
    } = "";

    /// <summary>A Game Library tile (M12): a 2:3 tile with a poster, a logo or the icon centred.</summary>
    public bool IsTile
    {
        get;
        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTile))); }
    }

    /// <summary>The tile's poster or logo, once loaded.</summary>
    public ImageSource? Art
    {
        get;
        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Art))); }
    }

    /// <summary>"Poster" (fills the tile), "Logo" (centred) or "None" (the icon).</summary>
    public string ArtKind
    {
        get;
        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ArtKind))); }
    } = "None";

    /// <summary>The art file requested last (UI thread): a slower, older load never wins.</summary>
    public string? ArtPath { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
}
```
`src/NeoFences.App/FenceWindow.xaml`:
```xml
<Window x:Class="NeoFences.App.FenceWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="NeoFences fence" Width="320" Height="220"
        WindowStyle="None" ResizeMode="CanResize" AllowsTransparency="True"
        Background="#01000000" ShowInTaskbar="False" ShowActivated="False">
    <!-- Layered window (AllowsTransparency) + accent blur, owned by Progman: ADR-011.
         The 1/255-alpha background keeps the empty area hit-testable (fully transparent pixels click through).
         Colours are DynamicResources set by ApplyTheme (light/dark follows Windows, M2c). -->
    <WindowChrome.WindowChrome>
        <!-- Replaced in code (ApplyChrome) with the 8-DIP corner radius: ADR-024. -->
        <WindowChrome GlassFrameThickness="0" CaptionHeight="30" ResizeBorderThickness="6" UseAeroCaptionButtons="False" />
    </WindowChrome.WindowChrome>
    <Window.Resources>
        <sys:Double x:Key="IconSize" xmlns:sys="clr-namespace:System;assembly=System.Runtime">48</sys:Double>
        <sys:Double x:Key="ItemWidth" xmlns:sys="clr-namespace:System;assembly=System.Runtime">76</sys:Double>
        <sys:Double x:Key="EditItemWidth" xmlns:sys="clr-namespace:System;assembly=System.Runtime">76</sys:Double>
        <!-- Icon-only fences (M8b): labels collapse, the name pops under the hovered or selected icon instead. -->
        <Visibility x:Key="LabelVisibility">Visible</Visibility>
        <sys:Boolean x:Key="ItemToolTips" xmlns:sys="clr-namespace:System;assembly=System.Runtime">True</sys:Boolean>
        <Visibility x:Key="ShortcutArrowVisibility">Collapsed</Visibility>
        <sys:Double x:Key="TileWidth" xmlns:sys="clr-namespace:System;assembly=System.Runtime">72</sys:Double>
        <sys:Double x:Key="TileHeight" xmlns:sys="clr-namespace:System;assembly=System.Runtime">108</sys:Double>
        <Style x:Key="BannerButton" TargetType="Button">
            <Setter Property="Foreground" Value="{DynamicResource FenceText}" />
            <Setter Property="Padding" Value="10,3" />
            <Setter Property="Margin" Value="0,0,6,0" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="Chrome" Background="{DynamicResource FenceHover}" CornerRadius="4" Padding="{TemplateBinding Padding}">
                            <ContentPresenter />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceSelected}" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <!-- Thin scrollbar (spec §6): no arrows, a rounded thumb that brightens under the mouse. -->
        <Style TargetType="ScrollBar">
            <Setter Property="Width" Value="6" />
            <Setter Property="MinWidth" Value="6" />
            <Setter Property="Margin" Value="0,4,2,4" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ScrollBar">
                        <Track x:Name="PART_Track" IsDirectionReversed="True">
                            <Track.Thumb>
                                <Thumb>
                                    <Thumb.Template>
                                        <ControlTemplate TargetType="Thumb">
                                            <Border x:Name="ThumbChrome" CornerRadius="3" Background="{DynamicResource FenceScrollThumb}" />
                                            <ControlTemplate.Triggers>
                                                <Trigger Property="IsMouseOver" Value="True">
                                                    <Setter TargetName="ThumbChrome" Property="Background" Value="{DynamicResource FenceSubtleText}" />
                                                </Trigger>
                                            </ControlTemplate.Triggers>
                                        </ControlTemplate>
                                    </Thumb.Template>
                                </Thumb>
                            </Track.Thumb>
                        </Track>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>
    <Border CornerRadius="8" BorderBrush="{DynamicResource FenceBorder}" BorderThickness="1" Background="{DynamicResource FenceVeil}">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="30" />
                <RowDefinition Height="Auto" />
                <RowDefinition />
            </Grid.RowDefinitions>
            <DockPanel x:Name="TitleBar" Background="Transparent">
                <!-- Portal browsing (M4): back to the parent folder. Hit-testable inside the caption area. -->
                <Button x:Name="BackButton" DockPanel.Dock="Left" Visibility="Collapsed" Content="‹" ToolTip="Back (Backspace)"
                        Margin="6,3,0,3" Padding="8,0" FontSize="16" Style="{StaticResource BannerButton}"
                        WindowChrome.IsHitTestVisibleInChrome="True" />
                <!-- Fence tabs (M9): one header per tab, built in code; the space right of them stays caption (move, roll-up). -->
                <UniformGrid x:Name="TabStrip" DockPanel.Dock="Left" Rows="1" Visibility="Collapsed" HorizontalAlignment="Left" Margin="6,3,0,0" />
                <TextBlock x:Name="TitleText" Foreground="{DynamicResource FenceText}" FontWeight="SemiBold" Margin="12,0"
                           VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
            </DockPanel>
            <!-- A single fence's tab colour (M9): a bar under the title. -->
            <Rectangle x:Name="TitleColorBar" Height="3" Width="48" Margin="12,0,0,1" RadiusX="1.5" RadiusY="1.5"
                       VerticalAlignment="Bottom" HorizontalAlignment="Left" Visibility="Collapsed" IsHitTestVisible="False" />
            <!-- Rename: replaces the title while editing. Hit-testable inside the caption area. -->
            <TextBox x:Name="TitleBox" Visibility="Collapsed" Margin="8,4" Padding="3,1" FontWeight="SemiBold"
                     VerticalContentAlignment="Center" WindowChrome.IsHitTestVisibleInChrome="True"
                     Foreground="{DynamicResource FenceText}" Background="{DynamicResource FenceHover}"
                     BorderBrush="{DynamicResource FenceBorder}" CaretBrush="{DynamicResource FenceText}" />
            <!-- First run (Inbox only): ask once whether NeoFences should take over the desktop icons. -->
            <Border x:Name="TakeoverPrompt" Grid.Row="1" Visibility="Collapsed" Background="{DynamicResource FenceHover}"
                    Margin="8,0,8,6" Padding="10,8" CornerRadius="6">
                <StackPanel>
                    <TextBlock Foreground="{DynamicResource FenceText}" TextWrapping="Wrap"
                               Text="Hide the desktop icons and keep them only in fences?" />
                    <TextBlock Foreground="{DynamicResource FenceSubtleText}" FontSize="11" TextWrapping="Wrap" Margin="0,2,0,6"
                               Text="Your files stay where they are. Turn it off any time from the right-click menu." />
                    <StackPanel Orientation="Horizontal">
                        <Button x:Name="PromptHideButton" Content="Hide them" Style="{StaticResource BannerButton}" />
                        <Button x:Name="PromptLaterButton" Content="Not now" Style="{StaticResource BannerButton}" />
                    </StackPanel>
                </StackPanel>
            </Border>
            <Border x:Name="Body" Grid.Row="2" BorderBrush="{DynamicResource FenceDivider}" BorderThickness="0,1,0,0" Background="#01000000">
                <Border.ContextMenu>
                    <ContextMenu x:Name="BodyContextMenu">
                        <MenuItem x:Name="NewFenceItem" Header="New fence" />
                        <MenuItem x:Name="NewPortalItem" Header="New Portal fence…" />
                        <MenuItem x:Name="NewLibraryItem" Header="New Game Library fence" />
                        <MenuItem x:Name="RenameItem" Header="Rename fence" />
                        <MenuItem x:Name="IconSizeItem" Header="Icon size" />
                        <MenuItem x:Name="LabelsItem" Header="Labels">
                            <MenuItem x:Name="LabelsAlwaysItem" Header="Always" IsCheckable="True" />
                            <MenuItem x:Name="LabelsOnHoverItem" Header="On hover (icons only)" IsCheckable="True" />
                        </MenuItem>
                        <MenuItem x:Name="SortItem" Header="Sort by" />
                        <MenuItem x:Name="TabColorItem" Header="Colour" />
                        <MenuItem x:Name="DetachTabItem" Header="Detach tab" Visibility="Collapsed" />
                        <MenuItem x:Name="OpenFolderItem" Header="Open folder in Explorer" Visibility="Collapsed" />
                        <MenuItem x:Name="RefreshLibraryItem" Header="Refresh library" Visibility="Collapsed" />
                        <MenuItem x:Name="RulesItem" Header="Rules for this fence…" />
                        <MenuItem x:Name="LockItem" Header="Lock position" IsCheckable="True" />
                        <MenuItem x:Name="DeleteItem" Header="Delete fence (items go to the Inbox)" />
                        <Separator />
                        <MenuItem x:Name="TakeoverItem" Header="Hide desktop icons" IsCheckable="True" />
                        <MenuItem x:Name="StartupItem" Header="Start with Windows" IsCheckable="True" />
                        <MenuItem x:Name="SettingsItem" Header="Settings…" />
                        <Separator />
                        <MenuItem x:Name="ExitItem" Header="Exit NeoFences" />
                    </ContextMenu>
                </Border.ContextMenu>
                <Grid>
                    <ListBox x:Name="ItemList" Background="Transparent" BorderThickness="0" Padding="4"
                             SelectionMode="Extended" ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                             ScrollViewer.VerticalScrollBarVisibility="Auto">
                        <ListBox.ItemsPanel>
                            <ItemsPanelTemplate>
                                <WrapPanel />
                            </ItemsPanelTemplate>
                        </ListBox.ItemsPanel>
                        <ListBox.ItemContainerStyle>
                            <Style TargetType="ListBoxItem">
                                <Setter Property="ToolTip" Value="{Binding Label}" />
                                <Setter Property="ToolTipService.IsEnabled" Value="{DynamicResource ItemToolTips}" />
                                <EventSetter Event="MouseEnter" Handler="OnItemMouseEnter" />
                                <EventSetter Event="MouseLeave" Handler="OnItemMouseLeave" />
                                <Setter Property="AutomationProperties.Name" Value="{Binding Label}" />
                                <Setter Property="FocusVisualStyle" Value="{x:Null}" />
                                <Setter Property="Template">
                                    <Setter.Value>
                                        <ControlTemplate TargetType="ListBoxItem">
                                            <!-- The outer border fills the gaps between cells, so the pointer is always over some cell and the
                                                 pop-under name does not blink between neighbours (final review I2). -->
                                            <Border Background="Transparent" Padding="2">
                                                <Border x:Name="Chrome" Background="Transparent" CornerRadius="4" Padding="2,4">
                                                    <ContentPresenter />
                                                </Border>
                                            </Border>
                                            <ControlTemplate.Triggers>
                                                <Trigger Property="IsMouseOver" Value="True">
                                                    <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceHover}" />
                                                </Trigger>
                                                <Trigger Property="IsSelected" Value="True">
                                                    <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceSelected}" />
                                                </Trigger>
                                            </ControlTemplate.Triggers>
                                        </ControlTemplate>
                                    </Setter.Value>
                                </Setter>
                            </Style>
                        </ListBox.ItemContainerStyle>
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <StackPanel x:Name="Cell" Width="{DynamicResource ItemWidth}">
                                    <Grid x:Name="IconGrid" HorizontalAlignment="Center">
                                        <Image Source="{Binding Icon}" Width="{DynamicResource IconSize}" Height="{DynamicResource IconSize}" />
                                        <!-- Shortcut arrow (M8b, Settings switch): a white tile with a blue curved arrow, like Windows draws. -->
                                        <Viewbox x:Name="ShortcutArrow" Visibility="Collapsed" HorizontalAlignment="Left" VerticalAlignment="Bottom"
                                                 Width="{DynamicResource ArrowSize}" Height="{DynamicResource ArrowSize}" IsHitTestVisible="False">
                                            <Canvas Width="16" Height="16">
                                                <Rectangle Width="16" Height="16" RadiusX="2" RadiusY="2" Fill="White" Stroke="#FF8A8A8A" StrokeThickness="0.75" />
                                                <Path Data="M4,12 C4,7.5 6.5,5.5 10,5.5 L10,3.5 L13,6.75 L10,10 L10,8 C7.5,8 5.5,9 4,12 Z" Fill="#FF2A6FD6" />
                                            </Canvas>
                                        </Viewbox>
                                    </Grid>
                                    <!-- Game Library tile (M12): poster, logo, or the icon centred on a dark 2:3 tile. -->
                                    <Border x:Name="Tile" Visibility="Collapsed" HorizontalAlignment="Center" CornerRadius="4" Background="#66000000"
                                            Width="{DynamicResource TileWidth}" Height="{DynamicResource TileHeight}" ClipToBounds="True">
                                        <Grid>
                                            <Image x:Name="TileIcon" Source="{Binding Icon}" Width="{DynamicResource IconSize}" Height="{DynamicResource IconSize}" />
                                            <Image x:Name="TileLogo" Source="{Binding Art}" Margin="10" Stretch="Uniform" Visibility="Collapsed" />
                                            <Image x:Name="TilePoster" Source="{Binding Art}" Stretch="UniformToFill" Visibility="Collapsed" />
                                        </Grid>
                                    </Border>
                                    <Grid Margin="0,4,0,0">
                                        <TextBlock x:Name="Label" Text="{Binding Label}" Foreground="{DynamicResource FenceText}" FontSize="12"
                                                   Visibility="{DynamicResource LabelVisibility}"
                                                   TextAlignment="Center" TextWrapping="Wrap" TextTrimming="CharacterEllipsis"
                                                   MaxHeight="32" Effect="{DynamicResource LabelShadow}" />
                                        <!-- In-place rename (M3a): Enter renames through Windows, Esc cancels. -->
                                        <TextBox x:Name="LabelBox" Visibility="Collapsed" FontSize="12" TextAlignment="Center" TextWrapping="Wrap"
                                                 MaxHeight="48" Text="{Binding EditName, UpdateSourceTrigger=PropertyChanged}"
                                                 KeyDown="OnLabelBoxKeyDown" LostKeyboardFocus="OnLabelBoxLostFocus"
                                                 IsVisibleChanged="OnLabelBoxVisibleChanged" />
                                    </Grid>
                                </StackPanel>
                                <DataTemplate.Triggers>
                                    <DataTrigger Binding="{Binding IsTile}" Value="True">
                                        <Setter TargetName="IconGrid" Property="Visibility" Value="Collapsed" />
                                        <Setter TargetName="Tile" Property="Visibility" Value="Visible" />
                                    </DataTrigger>
                                    <DataTrigger Binding="{Binding ArtKind}" Value="Poster">
                                        <Setter TargetName="TilePoster" Property="Visibility" Value="Visible" />
                                        <Setter TargetName="TileIcon" Property="Visibility" Value="Collapsed" />
                                    </DataTrigger>
                                    <DataTrigger Binding="{Binding ArtKind}" Value="Logo">
                                        <Setter TargetName="TileLogo" Property="Visibility" Value="Visible" />
                                        <Setter TargetName="TileIcon" Property="Visibility" Value="Collapsed" />
                                    </DataTrigger>
                                    <DataTrigger Binding="{Binding IsShortcut}" Value="True">
                                        <Setter TargetName="ShortcutArrow" Property="Visibility" Value="{DynamicResource ShortcutArrowVisibility}" />
                                    </DataTrigger>
                                    <DataTrigger Binding="{Binding IsEditing}" Value="True">
                                        <Setter TargetName="LabelBox" Property="Visibility" Value="Visible" />
                                        <Setter TargetName="Label" Property="Visibility" Value="Hidden" />
                                        <!-- Icons-only cells are narrow: the rename box gets a labelled cell's width (M8b review). -->
                                        <Setter TargetName="Cell" Property="Width" Value="{DynamicResource EditItemWidth}" />
                                    </DataTrigger>
                                </DataTemplate.Triggers>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>
                    <!-- Portal whose folder cannot be read (missing, offline, denied). -->
                    <TextBlock x:Name="PortalMessage" Visibility="Collapsed" Margin="12" TextWrapping="Wrap"
                               Foreground="{DynamicResource FenceSubtleText}" IsHitTestVisible="False" />
                    <!-- Rubber-band selection (M3b): drawn while dragging on empty space. -->
                    <Canvas IsHitTestVisible="False" ClipToBounds="True">
                        <Rectangle x:Name="SelectionBand" Visibility="Collapsed" Fill="{DynamicResource FenceHover}"
                                   Stroke="{DynamicResource FenceSubtleText}" StrokeThickness="1" RadiusX="2" RadiusY="2" />
                        <!-- Drag-drop feedback (M3b review I2): where a drop lands, or which folder takes it. -->
                        <Rectangle x:Name="InsertCaret" Visibility="Collapsed" Width="2" Fill="{DynamicResource FenceText}" RadiusX="1" RadiusY="1" />
                        <!-- Icon-only fences (M8b): the hovered or selected item's name, under its icon, over the neighbours. -->
                        <Border x:Name="HoverLabel" Visibility="Collapsed" CornerRadius="4" Padding="6,2"
                                Background="{DynamicResource HoverLabelBackground}" BorderBrush="{DynamicResource FenceBorder}" BorderThickness="1">
                            <TextBlock x:Name="HoverLabelText" Text="{Binding Label}" Foreground="{DynamicResource FenceText}" FontSize="12"
                                       TextAlignment="Center" TextWrapping="Wrap" MaxWidth="180" />
                        </Border>
                        <Rectangle x:Name="DropHighlight" Visibility="Collapsed" Fill="{DynamicResource FenceSelected}"
                                   Stroke="{DynamicResource FenceText}" StrokeThickness="1" RadiusX="4" RadiusY="4" />
                    </Canvas>
                </Grid>
            </Border>
        </Grid>
    </Border>
</Window>
```
`src/NeoFences.App/FenceWindow.xaml.cs`:
```csharp
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Shell;
using NeoFences.Core.Config;
using NeoFences.Core.Layouts;
using NeoFences.Core.Model;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>One fence on the desktop. Placement and persistence are the host's job; this window reports what the user did.</summary>
public partial class FenceWindow : Window
{
    private const int WmWindowPosChanging = 0x0046;
    private const int WmSizing = 0x0214;
    private const int WmMoving = 0x0216;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const int WmNcLeftButtonDoubleClick = 0x00A3;
    private const int HitTestCaption = 2;
    private const double CornerRadiusDips = 8;
    private const double CaptionHeightDips = 30;
    private const double ResizeBorderDips = 6;

    // The menu lists ConfigNormalizer.IconSizes (one list, M2c review carry-over); these are only their names.
    private static readonly Dictionary<int, string> IconSizeNames = new() { [32] = "Small", [48] = "Medium", [64] = "Large", [96] = "Extra large" };

    private readonly ObservableCollection<FenceItemView> _items = [];
    private readonly IconLoader _iconLoader;
    private ListBoxItem? _hoverLabelContainer; // the cell the pop-under name belongs to
    private int _iconSizeDips;
    private bool _renaming;
    private string _title = "";
    private bool _isPortal; // the shown tab is a Portal (changes with the tab, M9)
    private bool _isLibrary; // the shown tab is the Game Library (M12): tiles, its own menu items
    private IReadOnlyDictionary<string, (string Path, bool IsPoster)> _libraryArt = new Dictionary<string, (string, bool)>();
    private DragTracker? _drag;
    private bool _locked;
    private bool _rolledUp;
    private readonly RollUpExpansion _expansion; // rolled up, but open right now (hover or click, M5/M6b)
    private int _fullHeightPx;              // height when not rolled up (physical pixels)
    private readonly System.Windows.Threading.DispatcherTimer _hoverTimer;
    private readonly System.Windows.Threading.DispatcherTimer _heightAnimation;
    private System.Diagnostics.Stopwatch _heightClock = new();
    private int _heightFrom;
    private int _heightTo;
    private int _fadeGeneration;
    private int? _moveHeightPx; // a move that interrupted a roll-up animation keeps this height (M6b review M2)
    private static readonly TimeSpan RollUpDuration = TimeSpan.FromMilliseconds(200); // spec §6
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(150)); // spec §6

    /// <summary>The fence shown now: the box's active tab (M9). Items, icon size, labels, sort and rename act on it.</summary>
    public string FenceId { get; private set; }

    /// <summary>The fence owning this box (its host, M9): placement, move, roll-up and lock act on it.</summary>
    public string BoxId { get; set; }

    /// <summary>A tab header clicked (or hovered during a drop): show that tab.</summary>
    public event Action<string>? TabSelected;
    /// <summary>A tab header dragged and released at this screen point (physical pixels): reorder, merge or detach.</summary>
    public event Action<string, int, int>? TabDropped;
    /// <summary>A dragged tab header is over this screen point (physical pixels); int.MinValue when the drag ended.</summary>
    public event Action<int, int>? TabDragMoved;
    public event Action<TabColor?>? TabColorRequested;
    public event Action? DetachTabRequested;
    /// <summary>Ctrl+Tab (+1) / Ctrl+Shift+Tab (-1).</summary>
    public event Action<int>? TabCycleRequested;

    /// <summary>The accent colours (M9), as Windows' own accent palette roughly offers them.</summary>
    public static readonly IReadOnlyDictionary<TabColor, Color> TabColors = new Dictionary<TabColor, Color>
    {
        [TabColor.Red] = Color.FromRgb(0xE8, 0x48, 0x55), [TabColor.Orange] = Color.FromRgb(0xF7, 0x63, 0x0C),
        [TabColor.Yellow] = Color.FromRgb(0xFF, 0xB9, 0x00), [TabColor.Green] = Color.FromRgb(0x16, 0xC6, 0x0C),
        [TabColor.Teal] = Color.FromRgb(0x00, 0xB7, 0xC3), [TabColor.Blue] = Color.FromRgb(0x00, 0x78, 0xD4),
        [TabColor.Purple] = Color.FromRgb(0x88, 0x64, 0xD8), [TabColor.Pink] = Color.FromRgb(0xE3, 0x00, 0x8C),
    };

    private const double TabHeaderMaxWidth = 140;
    private List<Fence> _tabs = [];
    private string? _tabPressId;      // a header pressed: a click or the start of a tab drag
    private Point _tabPressPoint;
    private TabGhost? _tabGhost;      // set once the press became a drag
    private bool _lightTheme;

    public nint Handle { get; private set; }

    /// <summary>Windows refused the rounded-corner preference (Windows 10); the host logs it once.</summary>
    public bool CornersUnavailable { get; private set; }

    /// <summary>Peek (M5): while set the fence may rise above apps instead of staying at the bottom.</summary>
    public bool Peeking { get; set; }

    /// <summary>
    /// Asked before each roll-up or fade (spec §6): off when Windows' "Animation effects" are off, and in game mode
    /// (spec §4.7). Set by the host.
    /// </summary>
    public Func<bool> AnimationsAllowed { get; set; } = () => false;

    /// <summary>Asked while the user drags an edge or the title: returns where the window should go (snapping).</summary>
    public Func<PixelRect, SnapEdges, PixelRect>? SnapRect { get; set; }

    /// <summary>Raised after the user finishes moving or resizing, with the new physical-pixel rect.</summary>
    public event Action<FenceWindow, PixelRect>? MovedByUser;

    public event Action? NewFenceRequested;
    public event Action<bool>? TakeoverToggled;
    public event Action? ExitRequested;
    public event Action<string>? OpenRequested;
    /// <summary>Enter with several items selected: open each (a Portal does not browse into one of several folders, M8d).</summary>
    public event Action<IReadOnlyList<string>>? OpenManyRequested;
    /// <summary>The first-run question was answered: true = hide the desktop icons.</summary>
    public event Action<bool>? TakeoverPromptAnswered;
    public event Action<string>? RenameRequested;
    public event Action<int>? IconSizeRequested;
    public event Action<bool>? LockToggled;
    public event Action? DeleteRequested;
    /// <summary>Right-click (or the menu key) on items: show Windows' item menu for these refs at this screen point (px).</summary>
    /// <summary>Right-click or menu key on items: refs (the clicked item first), screen point, opened from the keyboard.</summary>
    public event Action<IReadOnlyList<string>, int, int, bool>? ItemMenuRequested;
    /// <summary>Del: send these items to the Recycle Bin.</summary>
    public event Action<IReadOnlyList<string>>? RecycleRequested;
    /// <summary>An in-place rename was confirmed: item ref, new name as typed.</summary>
    public event Action<string, string>? ItemRenameRequested;
    /// <summary>The user started dragging these items out of the fence (M3b).</summary>
    public event Action<IReadOnlyList<string>>? DragRequested;
    /// <summary>Portal (M4): back to the parent folder (Back button, Backspace).</summary>
    public event Action? BackRequested;
    public event Action? NewPortalRequested;
    public event Action<FenceSort>? SortRequested;
    public event Action? OpenFolderRequested;
    /// <summary>Fence menu → "New Game Library fence" / "Refresh library" (M12).</summary>
    public event Action? NewLibraryRequested;
    public event Action? RefreshLibraryRequested;
    /// <summary>Fence menu → "Rules for this fence…" (M11): Settings opens at Rules with a new rule for this fence.</summary>
    public event Action? RulesRequested;
    /// <summary>The "Start with Windows" toggle changed (ADR-019).</summary>
    public event Action<bool>? StartupToggled;
    /// <summary>"Settings…" in the fence menu (M6b).</summary>
    public event Action? SettingsRequested;
    /// <summary>Double-click on the title: roll up to the title bar, or back down (M5).</summary>
    public event Action? RollUpToggled;
    /// <summary>Fence menu → Labels (M8b): always, or only on hover / selection.</summary>
    public event Action<LabelMode>? LabelModeRequested;

    private Point? _pressPoint;                 // left button pressed on an item: a drag may start
    private ListBoxItem? _deferredSelect;       // pressed on an already selected item: select it alone only on release
    private Point? _bandStart;                  // left button pressed on empty space: rubber band
    private ListBoxItem? _deferredToggle;       // Ctrl+press on a selected item: unselect it on release unless it was dragged
    private LabelMode _labelMode = LabelMode.Always;
    private ListBoxItem? _hoveredContainer;
    private (uint At, Point Where)? _lastCaptionPress; // a title double-click recognised by NeoFences itself (M8b); message time
    private uint? _recognisedDoubleClickAt;

    public FenceWindow(Fence fence, bool takeoverActive, bool lightTheme, IconLoader iconLoader, RollupExpand rollupExpand)
    {
        _expansion = new RollUpExpansion(rollupExpand);
        FenceId = fence.Id;
        BoxId = fence.Id;
        _iconLoader = iconLoader;
        InitializeComponent();
        foreach (var (sort, name) in new[] { (FenceSort.Name, "Name"), (FenceSort.Type, "Type"), (FenceSort.Date, "Date (newest first)") })
        {
            var sortItem = new MenuItem { Header = name, Tag = sort };
            sortItem.Click += (_, _) => SortRequested?.Invoke(sort);
            SortItem.Items.Add(sortItem);
        }
        var noColor = new MenuItem { Header = "None", IsCheckable = true };
        noColor.Click += (_, _) => TabColorRequested?.Invoke(null);
        TabColorItem.Items.Add(noColor);
        foreach (var (color, value) in TabColors)
        {
            var colorItem = new MenuItem
            {
                Header = color.ToString(), Tag = color, IsCheckable = true,
                Icon = new Rectangle { Width = 12, Height = 12, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(value) },
            };
            colorItem.Click += (_, _) => TabColorRequested?.Invoke(color);
            TabColorItem.Items.Add(colorItem);
        }
        DetachTabItem.Click += (_, _) => DetachTabRequested?.Invoke();
        TitleBar.SizeChanged += (_, _) => UpdateTabStripWidth();
        PreviewKeyDown += OnTabKeys;
        NewPortalItem.Click += (_, _) => NewPortalRequested?.Invoke();
        OpenFolderItem.Click += (_, _) => OpenFolderRequested?.Invoke();
        NewLibraryItem.Click += (_, _) => NewLibraryRequested?.Invoke();
        RefreshLibraryItem.Click += (_, _) => RefreshLibraryRequested?.Invoke();
        RulesItem.Click += (_, _) => RulesRequested?.Invoke();
        BackButton.Click += (_, _) => BackRequested?.Invoke();
        TakeoverItem.IsChecked = takeoverActive;
        foreach (var size in ConfigNormalizer.IconSizes)
        {
            var sizeItem = new MenuItem { Header = IconSizeNames.GetValueOrDefault(size, $"{size} px"), Tag = size, IsCheckable = true };
            sizeItem.Click += (_, _) => IconSizeRequested?.Invoke(size);
            IconSizeItem.Items.Add(sizeItem);
        }
        NewFenceItem.Click += (_, _) => NewFenceRequested?.Invoke();
        RenameItem.Click += (_, _) => BeginRename();
        LockItem.Click += (_, _) => LockToggled?.Invoke(LockItem.IsChecked);
        DeleteItem.Click += (_, _) => DeleteRequested?.Invoke();
        TakeoverItem.Click += (_, _) => TakeoverToggled?.Invoke(TakeoverItem.IsChecked);
        ExitItem.Click += (_, _) => ExitRequested?.Invoke();
        StartupItem.Click += (_, _) => StartupToggled?.Invoke(StartupItem.IsChecked);
        SettingsItem.Click += (_, _) => SettingsRequested?.Invoke();
        LabelsAlwaysItem.Click += (_, _) => LabelModeRequested?.Invoke(LabelMode.Always);
        LabelsOnHoverItem.Click += (_, _) => LabelModeRequested?.Invoke(LabelMode.OnHover);
        TitleBox.MaxLength = FenceEdits.MaxTitleLength; // the cut in FenceEdits.Rename never surprises the user (M2c review)
        ItemList.SelectionChanged += (_, _) => UpdateHoverLabel();
        ItemList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => UpdateHoverLabel()));
        HoverLabel.SizeChanged += (_, _) => PlaceHoverLabel(); // the bound name arrived or changed (final review I3)
        Loaded += (_, _) => ReloadIcons(); // placed on its monitor: one load at the right size (mixed DPI, M2b review)
        ItemList.ItemsSource = _items;
        ItemList.MouseDoubleClick += OnItemDoubleClick;
        ItemList.PreviewMouseLeftButtonDown += OnListPress;
        ItemList.PreviewMouseMove += OnListMove;
        ItemList.PreviewMouseLeftButtonUp += OnListRelease;
        ItemList.LostMouseCapture += (_, _) => EndBand();
        ItemList.KeyDown += OnItemListKeyDown;
        Body.ContextMenuOpening += OnBodyContextMenuOpening;
        TitleBox.KeyDown += OnTitleBoxKeyDown;
        TitleBox.LostKeyboardFocus += (_, _) => EndRename(commit: true);
        PromptHideButton.Click += (_, _) => TakeoverPromptAnswered?.Invoke(true);
        PromptLaterButton.Click += (_, _) => TakeoverPromptAnswered?.Invoke(false);
#if DEBUG
        // Checklist B11: a hung fence UI thread must not freeze the desktop or taskbar (owner input-queue attachment, ADR-011).
        var freezeItem = new MenuItem { Header = "Debug: freeze this UI thread for 10 s (B11)" };
        freezeItem.Click += (_, _) => Thread.Sleep(TimeSpan.FromSeconds(10));
        BodyContextMenu.Items.Add(freezeItem);
#endif
        ApplyTheme(lightTheme);
        ApplyFence(fence);
        _rolledUp = fence.RolledUp;
        // Rolled up, the fence opens on hover or click (setting) and closes again shortly after the pointer leaves.
        _hoverTimer = new System.Windows.Threading.DispatcherTimer { Interval = HoverTick };
        _hoverTimer.Tick += (_, _) => OnHoverTick();
        if (_rolledUp) _hoverTimer.Start();
        _heightAnimation = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        _heightAnimation.Tick += (_, _) => StepHeight();
        Closed += (_, _) =>
        {
            _hoverTimer.Stop(); // a deleted fence must not keep ticking on its dead handle (M5 review M1)
            _heightAnimation.Stop();
        };
        SetLocked(fence.Locked);
        // Unlocked, the title is caption (WM_NCLBUTTONDBLCLK); locked, it is client area.
        TitleBar.MouseLeftButtonDown += (_, click) =>
        {
            if (click.ClickCount == 2) RollUpToggled?.Invoke();
            else if (click.ClickCount == 1) ClickToOpen();
        };
        SourceInitialized += OnSourceInitialized;
        // Icons are rendered for one DPI. DpiChanged is routed: every new item raises it too, so react only to the window's own.
        DpiChanged += (_, dpiChange) =>
        {
            if (dpiChange.OriginalSource != this || dpiChange.OldDpi.PixelsPerDip == dpiChange.NewDpi.PixelsPerDip) return;
            // A rolled-up fence keeps its full height in physical pixels: rescale it with the monitor (M5 review carry-over).
            _fullHeightPx = (int)Math.Round(_fullHeightPx * dpiChange.NewDpi.DpiScaleY / dpiChange.OldDpi.DpiScaleY);
            ReloadIcons();
        };
    }

    public void SetTakeoverChecked(bool active) => TakeoverItem.IsChecked = active;

    public void SetStartupChecked(bool startWithWindows) => StartupItem.IsChecked = startWithWindows;

    /// <summary>Everything the window shows of one fence: title, Portal bits, menu state, icon size, labels.</summary>
    private void ApplyFence(Fence fence)
    {
        _title = fence.Title;
        TitleText.Text = fence.Title;
        _isPortal = fence.Source.Kind == FenceSourceKind.Portal;
        _isLibrary = fence.Source.Kind == FenceSourceKind.Library;
        OpenFolderItem.Visibility = _isPortal ? Visibility.Visible : Visibility.Collapsed;
        RulesItem.Visibility = fence.Source.Kind == FenceSourceKind.Desktop ? Visibility.Visible : Visibility.Collapsed; // rules fill desktop fences only (M11)
        RefreshLibraryItem.Visibility = _isLibrary ? Visibility.Visible : Visibility.Collapsed;
        SortItem.Visibility = _isLibrary ? Visibility.Collapsed : Visibility.Visible; // the library is always A–Z
        DeleteItem.Header = _isLibrary ? "Delete fence (your games are not touched)"
            : _isPortal ? "Delete fence (the folder is not touched)" : "Delete fence (items go to the Inbox)";
        DeleteItem.Visibility = fence.IsInbox ? Visibility.Collapsed : Visibility.Visible;
        // Desktop fences sort once (dragging keeps working); Portals keep the chosen order live, so it is checked.
        foreach (var sortItem in SortItem.Items.OfType<MenuItem>())
        {
            sortItem.IsCheckable = _isPortal;
            sortItem.IsChecked = _isPortal && (FenceSort)sortItem.Tag == fence.Sort;
        }
        _labelMode = fence.Labels;
        SetIconSize(fence.IconSize);
        SetLabelMode(fence.Labels);
    }

    /// <summary>The shown tab is the Game Library (M12).</summary>
    public bool IsLibrary => _isLibrary;

    /// <summary>The library's tile art by item ref: a 2:3 poster, or a logo shown centred (M12).</summary>
    public void SetLibraryArt(IReadOnlyDictionary<string, (string Path, bool IsPoster)> art)
    {
        _libraryArt = art;
        foreach (var view in _items) ApplyArt(view);
    }

    private void ApplyArt(FenceItemView view)
    {
        view.IsTile = _isLibrary;
        if (!_isLibrary || !_libraryArt.TryGetValue(view.ItemRef, out var art))
        {
            view.ArtPath = null;
            view.Art = null;
            view.ArtKind = "None";
            return;
        }
        if (view.ArtPath == art.Path) return;
        view.ArtPath = art.Path;
        var kind = art.IsPoster ? "Poster" : "Logo";
        // Decoded off the UI thread (a cover is a few hundred KB); only the newest request is shown.
        Task.Run(() => LoadArt(art.Path)).ContinueWith(loaded =>
        {
            if (view.ArtPath != art.Path || loaded.Result is not { } image) return;
            view.Art = image;
            view.ArtKind = kind;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static System.Windows.Media.ImageSource? LoadArt(string path)
    {
        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; // the file is not kept open
            image.DecodePixelWidth = 300;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Serilog.Log.Warning(failure, "could not load game art {Path}", path); // the icon stays
            return null;
        }
    }

    /// <summary>The shown fence changed in place (a snapshot restore, M10 final review I1): title, icon size, labels, menus.</summary>
    public void Refresh(Fence fence) => ApplyFence(fence);

    /// <summary>
    /// Another tab of this box is shown (M9): its look and menus; its items follow from the host (or its Portal). An open
    /// rename of the previous tab is cancelled.
    /// </summary>
    public void ShowTab(Fence fence)
    {
        if (fence.Id == FenceId) return;
        EndRename(commit: false);
        FenceId = fence.Id;
        ApplyFence(fence);
        _items.Clear();
        ShowPortalMessage(null);
        BackButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>The box's tabs in order and the shown one (M9). One tab: the plain title (with its colour bar, if any).</summary>
    public void SetTabs(IReadOnlyList<Fence> tabs, string activeId)
    {
        _tabs = [.. tabs];
        var many = _tabs.Count > 1;
        var active = _tabs.FirstOrDefault(tab => tab.Id == activeId) ?? _tabs[0];
        TabStrip.Children.Clear();
        if (many) foreach (var tab in _tabs) TabStrip.Children.Add(BuildTabHeader(tab, isActive: tab.Id == active.Id));
        ShowTitleOrTabs();
        DetachTabItem.Visibility = many ? Visibility.Visible : Visibility.Collapsed;
        RenameItem.Header = many ? "Rename tab" : "Rename fence";
        TitleColorBar.Visibility = !many && active.TabColor is not null ? Visibility.Visible : Visibility.Collapsed;
        if (active.TabColor is { } titleColor) TitleColorBar.Fill = new SolidColorBrush(TabColors[titleColor]);
        foreach (var colorItem in TabColorItem.Items.OfType<MenuItem>()) colorItem.IsChecked = Equals(colorItem.Tag, active.TabColor);
        UpdateTabStripWidth();
    }

    private void ShowTitleOrTabs()
    {
        var many = _tabs.Count > 1;
        TabStrip.Visibility = _renaming ? Visibility.Hidden : many ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Visibility = _renaming ? Visibility.Hidden : many ? Visibility.Collapsed : Visibility.Visible;
    }

    private Border BuildTabHeader(Fence tab, bool isActive)
    {
        var title = new TextBlock
        {
            Text = tab.Title, FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(8, 0, 8, 2),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "FenceText");
        var bar = new Rectangle
        {
            Height = 3, RadiusX = 1.5, RadiusY = 1.5, Margin = new Thickness(6, 0, 6, 1), VerticalAlignment = VerticalAlignment.Bottom,
            Fill = tab.TabColor is { } color ? new SolidColorBrush(TabColors[color]) : Brushes.Transparent, Opacity = isActive ? 1 : 0.55,
            IsHitTestVisible = false,
        };
        var cell = new Grid();
        cell.Children.Add(title);
        cell.Children.Add(bar);
        var header = new Border { Child = cell, CornerRadius = new CornerRadius(5, 5, 0, 0), Tag = tab.Id, Background = Brushes.Transparent };
        if (isActive) header.SetResourceReference(Border.BackgroundProperty, "FenceHover");
        WindowChrome.SetIsHitTestVisibleInChrome(header, true); // the title row is caption: headers take the mouse themselves
        System.Windows.Automation.AutomationProperties.SetName(header, tab.Title);
        header.MouseLeftButtonDown += OnTabPress;
        header.MouseMove += OnTabMove;
        header.MouseLeftButtonUp += OnTabRelease;
        header.MouseRightButtonDown += OnTabRightPress;
        header.MouseRightButtonUp += OnTabRightClick;
        header.LostMouseCapture += (_, _) => CancelTabGesture();
        return header;
    }

    private void UpdateTabStripWidth()
    {
        if (_tabs.Count < 2) return;
        // Headers share the strip equally and shrink with "…"; the rest of the row stays free to move the box.
        TabStrip.Width = Math.Max(0, Math.Min(_tabs.Count * TabHeaderMaxWidth, TitleBar.ActualWidth - 40));
    }

    private void OnTabPress(object sender, MouseButtonEventArgs press)
    {
        if (sender is not Border { Tag: string tabId } header) return;
        press.Handled = true;
        if (press.ClickCount == 2)
        {
            if (tabId == FenceId) BeginRename(); // the first click already showed it
            return;
        }
        _tabPressId = tabId;
        _tabPressPoint = press.GetPosition(this);
        header.CaptureMouse();
    }

    private void OnTabMove(object sender, MouseEventArgs move)
    {
        if (_tabPressId is not { } tabId || move.LeftButton != MouseButtonState.Pressed) return;
        if (_tabGhost is null)
        {
            var moved = move.GetPosition(this) - _tabPressPoint;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance * 2
                && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance * 2) return;
            _tabGhost = new TabGhost(_tabs.FirstOrDefault(tab => tab.Id == tabId)?.Title ?? "", _lightTheme);
        }
        // Esc cancels even when another app has the keyboard (fences rarely do, ADR-015; final review I3).
        if (FenceWindowChrome.IsKeyDown(0x1B))
        {
            EndTabGesture();
            return;
        }
        var (screenX, screenY) = FenceWindowChrome.GetCursorPosition();
        _tabGhost.Follow(screenX, screenY, VisualTreeHelper.GetDpi(this).DpiScaleX);
        TabDragMoved?.Invoke(screenX, screenY); // the title row it would join lights up
    }

    private void OnTabRelease(object sender, MouseButtonEventArgs release)
    {
        if (_tabPressId is not { } tabId) return;
        release.Handled = true;
        var dragged = _tabGhost is not null;
        EndTabGesture();
        if (!dragged)
        {
            TabSelected?.Invoke(tabId);
            return;
        }
        var (screenX, screenY) = FenceWindowChrome.GetCursorPosition();
        TabDropped?.Invoke(tabId, screenX, screenY);
    }

    /// <summary>A right-click during a tab drag cancels it; nothing changes.</summary>
    private void OnTabRightPress(object sender, MouseButtonEventArgs press)
    {
        if (_tabPressId is null) return;
        press.Handled = true;
        EndTabGesture();
    }

    /// <summary>Right-click on a header: that tab is shown, and the fence menu acts on it.</summary>
    private void OnTabRightClick(object sender, MouseButtonEventArgs click)
    {
        if (sender is not Border { Tag: string tabId }) return;
        click.Handled = true;
        if (tabId != FenceId) TabSelected?.Invoke(tabId);
        BodyContextMenu.PlacementTarget = Body;
        BodyContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        BodyContextMenu.IsOpen = true;
    }

    private void OnTabKeys(object sender, KeyEventArgs key)
    {
        if (key.Key == Key.Escape && _tabPressId is not null)
        {
            EndTabGesture();
            key.Handled = true;
        }
        else if (key.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && _tabs.Count > 1)
        {
            TabCycleRequested?.Invoke(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            key.Handled = true;
        }
    }

    private void EndTabGesture()
    {
        _tabPressId = null; // first: releasing the capture below raises LostMouseCapture
        CloseTabGhost();
        Mouse.Capture(null);
    }

    private void CancelTabGesture()
    {
        if (_tabPressId is null) return;
        _tabPressId = null;
        CloseTabGhost();
    }

    private void CloseTabGhost()
    {
        if (_tabGhost is null) return;
        _tabGhost.Close();
        _tabGhost = null;
        TabDragMoved?.Invoke(int.MinValue, int.MinValue); // no more highlight
    }

    /// <summary>The insertion slot (0..tab count) under a screen x (physical pixels), for a reorder or a merge.</summary>
    public int TabIndexAt(int screenX)
    {
        if (_tabs.Count < 2 || TabStrip.ActualWidth <= 0) return _tabs.Count;
        var headerWidth = TabStrip.ActualWidth / _tabs.Count;
        var x = TabStrip.PointFromScreen(new Point(screenX, 0)).X;
        return Math.Clamp((int)Math.Round(x / headerWidth), 0, _tabs.Count);
    }

    /// <summary>True when the screen point (physical pixels) is on this box's title row or tab strip.</summary>
    public bool TitleRowContains(int screenX, int screenY)
    {
        if (!IsVisible) return false;
        var rect = FenceWindowChrome.GetPixelRect(Handle);
        var titleHeightPx = TitleBar.ActualHeight * VisualTreeHelper.GetDpi(this).DpiScaleY;
        return screenX >= rect.X && screenX < rect.X + rect.Width && screenY >= rect.Y && screenY < rect.Y + titleHeightPx;
    }

    /// <summary>A fence or tab dragged over this title row would merge here: show it.</summary>
    public void SetMergeHighlight(bool highlighted)
    {
        if (highlighted) TitleBar.SetResourceReference(Panel.BackgroundProperty, "FenceSelected");
        else TitleBar.Background = Brushes.Transparent;
    }

    /// <summary>The tab whose header is under a screen point, if any (an item drop over a header shows that tab).</summary>
    private string? TabHeaderAt(int screenX, int screenY)
    {
        if (_tabs.Count < 2 || TabStrip.Visibility != Visibility.Visible) return null;
        var point = TabStrip.PointFromScreen(new Point(screenX, screenY));
        if (point.Y < 0 || point.Y > TabStrip.ActualHeight || point.X < 0 || point.X >= TabStrip.ActualWidth) return null;
        return _tabs[Math.Clamp((int)(point.X / (TabStrip.ActualWidth / _tabs.Count)), 0, _tabs.Count - 1)].Id;
    }

    public void ShowTakeoverPrompt(bool visible) => TakeoverPrompt.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public void SetTitle(string title)
    {
        _title = title;
        TitleText.Text = title;
    }

    /// <summary>Portal (M4): what the title shows while browsing ("Downloads › Mods"), and whether Back is offered.</summary>
    public void SetPortalLocation(string breadcrumb, bool canGoBack)
    {
        TitleText.Text = breadcrumb;
        BackButton.Visibility = canGoBack ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Portal (M4): a message instead of items (folder missing or unreadable); null hides it.</summary>
    public void ShowPortalMessage(string? message)
    {
        PortalMessage.Text = message ?? "";
        PortalMessage.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Portal (M4): the sort shown as checked.</summary>
    public void SetSortChecked(FenceSort sort)
    {
        foreach (var sortItem in SortItem.Items.OfType<MenuItem>()) sortItem.IsChecked = _isPortal && (FenceSort)sortItem.Tag == sort;
    }

    /// <summary>
    /// Shows exactly these items in this order, by moving, adding and removing views in place: items already shown keep
    /// their name, icon, selection and the scroll position (M2b review carry-over; duplicate refs are tolerated).
    /// </summary>
    public void SetItems(IReadOnlyList<string> itemRefs)
    {
        if (_items.Select(view => view.ItemRef).SequenceEqual(itemRefs, StringComparer.Ordinal)) return;
        var wanted = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var itemRef in itemRefs) wanted[itemRef] = wanted.GetValueOrDefault(itemRef) + 1;
        for (var index = _items.Count - 1; index >= 0; index--)
        {
            var itemRef = _items[index].ItemRef;
            if (wanted.GetValueOrDefault(itemRef) > 0) wanted[itemRef]--;
            else RemoveItemAt(index);
        }
        // ponytail: O(n²) moves in the worst case (a full reorder of hundreds of items); a keyed diff when that shows up.
        var iconSizePx = IconSizePx;
        for (var index = 0; index < itemRefs.Count; index++)
        {
            if (index < _items.Count && _items[index].ItemRef == itemRefs[index]) continue;
            var found = -1;
            for (var later = index + 1; later < _items.Count && found < 0; later++)
            {
                if (_items[later].ItemRef == itemRefs[index]) found = later;
            }
            if (found >= 0)
            {
                CancelRename(_items[found]);
                _items.Move(found, index);
                continue;
            }
            var view = new FenceItemView(itemRefs[index]);
            ApplyArt(view);
            if (IsLoaded) _iconLoader.Request(view, iconSizePx); // before that, Loaded requests them at the right DPI (M2b review)
            _items.Insert(index, view);
        }
        // Cells may have shifted under a shown name without a scroll or selection event (final review I3).
        Dispatcher.BeginInvoke(UpdateHoverLabel, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// A rename whose own item is moved or removed would lose its box (and commit half-typed text): cancel only that one,
    /// right before. An item that merely shifts keeps its box (M3a review I3; final review M1).
    /// </summary>
    private static void CancelRename(FenceItemView view)
    {
        if (view.IsEditing) view.IsEditing = false;
    }

    private void RemoveItemAt(int index)
    {
        CancelRename(_items[index]);
        _items.RemoveAt(index);
    }

    private int IconSizePx => (int)Math.Round(_iconSizeDips * VisualTreeHelper.GetDpi(this).DpiScaleX);

    /// <summary>One of <see cref="ConfigNormalizer.IconSizes"/> (DIPs). Icons are reloaded at the new size.</summary>
    public void SetIconSize(int iconSizeDips)
    {
        if (iconSizeDips == _iconSizeDips) return; // nothing to reload (M2c review carry-over)
        _iconSizeDips = iconSizeDips;
        Resources["IconSize"] = (double)iconSizeDips;
        Resources["ArrowSize"] = Math.Max(12.0, Math.Round(iconSizeDips * 0.36));
        ApplyItemWidth();
        foreach (var sizeItem in IconSizeItem.Items.OfType<MenuItem>()) sizeItem.IsChecked = (int)sizeItem.Tag == iconSizeDips;
        ReloadIcons();
    }

    /// <summary>Labels always, or icons only with the name popping under the hovered or selected icon (M8b, user choice).</summary>
    public void SetLabelMode(LabelMode labelMode)
    {
        _labelMode = labelMode;
        Resources["LabelVisibility"] = labelMode == LabelMode.Always ? Visibility.Visible : Visibility.Collapsed;
        Resources["ItemToolTips"] = labelMode == LabelMode.Always; // the pop-under name replaces the tooltip
        LabelsAlwaysItem.IsChecked = labelMode == LabelMode.Always;
        LabelsOnHoverItem.IsChecked = labelMode == LabelMode.OnHover;
        ApplyItemWidth();
        UpdateHoverLabel();
    }

    /// <summary>Settings → "Show shortcut arrows" (M8b, off by default).</summary>
    public void SetShortcutArrows(bool show) => Resources["ShortcutArrowVisibility"] = show ? Visibility.Visible : Visibility.Collapsed;

    private void ApplyItemWidth()
    {
        Resources["EditItemWidth"] = LabelledItemWidth;
        ApplyCellWidth();
    }

    private void ApplyCellWidth()
    {
        // Game Library tiles are 2:3, 1.5 × the icon size wide (M12).
        Resources["TileWidth"] = Math.Round(_iconSizeDips * 1.5);
        Resources["TileHeight"] = Math.Round(_iconSizeDips * 2.25);
        // With labels: room for two short words under small icons. Icons only: a tight grid.
        Resources["ItemWidth"] = _isLibrary ? Math.Round(_iconSizeDips * 1.5) + 12.0
            : _labelMode == LabelMode.Always ? LabelledItemWidth : _iconSizeDips + 12.0;
    }

    private double LabelledItemWidth => Math.Max(76.0, _iconSizeDips + 28.0);

    private void OnItemMouseEnter(object sender, MouseEventArgs args)
    {
        _hoveredContainer = sender as ListBoxItem;
        UpdateHoverLabel();
    }

    private void OnItemMouseLeave(object sender, MouseEventArgs args)
    {
        if (ReferenceEquals(_hoveredContainer, sender)) _hoveredContainer = null;
        UpdateHoverLabel();
    }

    /// <summary>
    /// Icon-only fences: the name of the hovered item (else the one selected item) pops up under its icon, over the
    /// neighbours, kept inside the fence (above the icon when there is no room below). Drawn in the fence window itself,
    /// so it stays at the fence's place in the window stack (no popup window above other apps).
    /// </summary>
    private void UpdateHoverLabel()
    {
        var container = _labelMode != LabelMode.OnHover ? null
            : _hoveredContainer ?? (ItemList.SelectedItems.Count == 1 ? ItemList.ItemContainerGenerator.ContainerFromItem(ItemList.SelectedItem) as ListBoxItem : null);
        if (container is not { DataContext: FenceItemView { IsEditing: false } view, IsVisible: true })
        {
            HoverLabel.Visibility = Visibility.Collapsed;
            return;
        }
        _hoverLabelContainer = container;
        HoverLabel.DataContext = view; // bound: a name that loads later shows up (final review I3)
        // Never wider than the fence (narrow icon-only fences, final review I1); 18 = padding + border + margins.
        HoverLabelText.MaxWidth = Math.Clamp(ItemList.ActualWidth - 18, 24, 180);
        HoverLabel.Visibility = Visibility.Visible;
        PlaceHoverLabel();
    }

    /// <summary>Under the cell, clamped to the fence's sides; above it when there is no room; hidden when it is scrolled away.</summary>
    private void PlaceHoverLabel()
    {
        if (HoverLabel.Visibility != Visibility.Visible || _hoverLabelContainer is not { IsVisible: true } container) return;
        HoverLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = HoverLabel.DesiredSize;
        var cell = container.TransformToAncestor(ItemList).TransformBounds(new Rect(container.RenderSize));
        if (cell.Bottom <= 0 || cell.Top >= ItemList.ActualHeight)
        {
            HoverLabel.Visibility = Visibility.Collapsed; // a selected item scrolled out of view (final review I1)
            return;
        }
        var left = Math.Clamp(cell.Left + cell.Width / 2 - size.Width / 2, 2, Math.Max(2, ItemList.ActualWidth - size.Width - 2));
        var top = cell.Bottom - 2;
        if (top + size.Height > ItemList.ActualHeight - 2) top = Math.Max(2, cell.Top - size.Height + 2);
        Canvas.SetLeft(HoverLabel, left);
        Canvas.SetTop(HoverLabel, top);
    }

    /// <summary>Locked: the title no longer drags and the edges no longer resize.</summary>
    public void SetLocked(bool locked)
    {
        LockItem.IsChecked = locked;
        _locked = locked;
        ApplyChrome();
    }

    /// <summary>Locked: no drag, no resize. Rolled up: drag, no resize (its height is the stored full height).</summary>
    private void ApplyChrome()
    {
        // A fresh WindowChrome each time: editing the attached one in place is not re-applied after an unlock.
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            GlassFrameThickness = new Thickness(0),
            CaptionHeight = _locked ? 0 : CaptionHeightDips,
            ResizeBorderThickness = new Thickness(_locked || _rolledUp ? 0 : ResizeBorderDips),
            // WindowChrome owns the window region and re-applies it on every resize: a radius of 0 kept resetting it to
            // a square, so the blur showed outside the rounded border (user screenshot 2026-10-03). Same radius as the border.
            CornerRadius = new CornerRadius(CornerRadiusDips),
            UseAeroCaptionButtons = false,
        });
    }

    /// <summary>Puts the fence at its full rect (physical pixels); rolled up, only the title bar of it shows.</summary>
    public void Place(PixelRect fullRect)
    {
        _heightAnimation.Stop();
        _fullHeightPx = fullRect.Height;
        FenceWindowChrome.SetPixelRect(Handle, _rolledUp && !_expansion.Expanded ? fullRect with { Height = RolledUpHeightPx } : fullRect);
    }

    public void SetRolledUp(bool rolledUp)
    {
        if (rolledUp == _rolledUp) return;
        var current = FenceWindowChrome.GetPixelRect(Handle);
        if (rolledUp && !_expansion.Expanded) _fullHeightPx = _heightAnimation.IsEnabled ? _heightTo : current.Height;
        _rolledUp = rolledUp;
        _expansion.Reset();
        ApplyChrome();
        AnimateHeight(rolledUp ? RolledUpHeightPx : _fullHeightPx);
        if (rolledUp) _hoverTimer.Start();
        else _hoverTimer.Stop();
    }

    /// <summary>Roll-up expand mode from settings (M6b): hover or click.</summary>
    public void SetRollupExpand(RollupExpand mode) => _expansion.Mode = mode;

    /// <summary>Shows at once (Pause, layout): a quick-hide fade still running must not hide the fence afterwards (M8a).</summary>
    public void ShowNow()
    {
        ++_fadeGeneration;
        BeginAnimation(OpacityProperty, null);
        Show();
    }

    /// <summary>Hides at once (Pause).</summary>
    public void HideNow()
    {
        ++_fadeGeneration;
        BeginAnimation(OpacityProperty, null);
        Hide();
    }

    /// <summary>Quick-hide (spec §6): a 150 ms fade, then hidden. Without animations it hides at once.</summary>
    public void HideFaded()
    {
        var generation = ++_fadeGeneration;
        if (!IsVisible || !AnimationsAllowed())
        {
            BeginAnimation(OpacityProperty, null);
            Hide();
            return;
        }
        var fade = new DoubleAnimation(0, FadeDuration);
        fade.Completed += (_, _) =>
        {
            if (generation != _fadeGeneration) return; // shown again meanwhile
            Hide();
            BeginAnimation(OpacityProperty, null);
        };
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Shows the fence (fading in when animations are on). The host sends it to the bottom afterwards.</summary>
    public void ShowFaded()
    {
        ++_fadeGeneration;
        var fadeIn = AnimationsAllowed();
        BeginAnimation(OpacityProperty, null);
        Show();
        if (fadeIn) BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, FadeDuration));
    }

    /// <summary>Click mode: a single click on the rolled-up title opens it.</summary>
    private bool ClickToOpen()
    {
        if (!_rolledUp || !_expansion.Click()) return false;
        AnimateHeight(_fullHeightPx);
        return true;
    }

    /// <summary>Roll-up and roll-down move the bottom edge over 200 ms (ease-out); at once without animations.</summary>
    private void AnimateHeight(int targetPx)
    {
        var current = FenceWindowChrome.GetPixelRect(Handle);
        if (!AnimationsAllowed() || _drag is not null || current.Height == targetPx)
        {
            _heightAnimation.Stop();
            FenceWindowChrome.SetPixelRect(Handle, current with { Height = targetPx });
            return;
        }
        _heightFrom = current.Height;
        _heightTo = targetPx;
        _heightClock = System.Diagnostics.Stopwatch.StartNew();
        _heightAnimation.Start();
    }

    private void StepHeight()
    {
        var current = FenceWindowChrome.GetPixelRect(Handle);
        var progress = Math.Min(1.0, _heightClock.Elapsed / RollUpDuration);
        if (_drag is not null) progress = 1; // never fight a move: jump to the end
        var eased = 1 - Math.Pow(1 - progress, 3);
        FenceWindowChrome.SetPixelRect(Handle, current with { Height = (int)Math.Round(_heightFrom + (_heightTo - _heightFrom) * eased) });
        if (progress >= 1) _heightAnimation.Stop();
    }

    private static readonly TimeSpan HoverTick = TimeSpan.FromMilliseconds(100);

    /// <summary>Title row plus the 1-DIP border above and below it.</summary>
    private int RolledUpHeightPx => (int)Math.Round((CaptionHeightDips + 2) * VisualTreeHelper.GetDpi(this).DpiScaleY);

    // ponytail: polls the cursor every 100 ms while rolled up (no mouse-leave on a no-activate layered window when
    // the pointer leaves fast); in hover mode it also opens during a file drag, which is wanted.
    private void OnHoverTick()
    {
        if (Handle == 0 || !_rolledUp) return;
        if (_drag is not null || BodyContextMenu.IsOpen || _renaming || _items.Any(view => view.IsEditing)) return; // never close under the user
        var (cursorX, cursorY) = FenceWindowChrome.GetCursorPosition();
        var rect = FenceWindowChrome.GetPixelRect(Handle);
        // While the height animates, judge "inside" against where the fence is going, so it does not flicker shut.
        var height = _heightAnimation.IsEnabled ? Math.Max(rect.Height, _heightTo) : rect.Height;
        var inside = cursorX >= rect.X && cursorX < rect.X + rect.Width && cursorY >= rect.Y && cursorY < rect.Y + height;
        if (!_expansion.Tick(inside)) return;
        AnimateHeight(_expansion.Expanded ? _fullHeightPx : RolledUpHeightPx);
    }

    /// <summary>Colours for Windows' light or dark app mode (M2c: fences follow Windows).</summary>
    public void ApplyTheme(bool light)
    {
        _lightTheme = light;
        var ink = light ? Colors.Black : Colors.White;
        SolidColorBrush Ink(byte alpha)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, ink.R, ink.G, ink.B));
            brush.Freeze();
            return brush;
        }
        // The accent blur ignores its tint colour (ACCENT_ENABLE_BLURBEHIND), so the veil is drawn here: none in dark mode
        // (the look the user approved in M2a), a dense light veil in light mode so dark text reads on any wallpaper.
        var veil = new SolidColorBrush(light ? Color.FromArgb(0xB8, 0xF2, 0xF2, 0xF2) : Colors.Transparent);
        veil.Freeze();
        Resources["FenceVeil"] = veil;
        Resources["FenceText"] = Ink(light ? (byte)0xE6 : (byte)0xFF);
        Resources["FenceSubtleText"] = Ink(0xA0);
        Resources["FenceBorder"] = Ink(light ? (byte)0x33 : (byte)0x40);
        Resources["FenceDivider"] = Ink(light ? (byte)0x1F : (byte)0x26);
        Resources["FenceHover"] = Ink(light ? (byte)0x14 : (byte)0x22);
        Resources["FenceSelected"] = Ink(light ? (byte)0x2A : (byte)0x44);
        Resources["FenceScrollThumb"] = Ink(light ? (byte)0x44 : (byte)0x55);
        var hoverLabel = new SolidColorBrush(light ? Color.FromArgb(0xF0, 0xF4, 0xF4, 0xF4) : Color.FromArgb(0xE6, 0x20, 0x22, 0x28));
        hoverLabel.Freeze();
        Resources["HoverLabelBackground"] = hoverLabel;
        // Soft halo behind labels, like desktop icon labels: readable on busy or bright wallpapers (user choice).
        var shadow = new DropShadowEffect
        {
            Color = light ? Colors.White : Colors.Black,
            ShadowDepth = light ? 0 : 1,
            BlurRadius = 4,
            Opacity = 0.9,
        };
        shadow.Freeze();
        Resources["LabelShadow"] = shadow;
    }

    /// <summary>The Recycle Bin turned full or empty, or another special icon changed (M8c).</summary>
    public void ReloadSpecialIcons()
    {
        var iconSizePx = IconSizePx;
        foreach (var view in _items.Where(view => view.ItemRef.StartsWith("::", StringComparison.Ordinal))) _iconLoader.Request(view, iconSizePx);
    }

    /// <summary>New size or DPI: every icon is requested again in place; names, selection and renames stay (M2c review carry-over).</summary>
    private void ReloadIcons()
    {
        var iconSizePx = IconSizePx;
        foreach (var view in _items) _iconLoader.Request(view, iconSizePx);
    }

    /// <summary>Starts renaming the fence title (menu, or a freshly drawn fence).</summary>
    public void BeginRename()
    {
        _renaming = true;
        TitleBox.Text = _title;
        ShowTitleOrTabs();
        TitleBox.Visibility = Visibility.Visible;
        Activate(); // keyboard input needs the fence active; it stays at the bottom (owned by Progman, ADR-011)
        // With another app in front the fence may not get focus (ADR-015); then typing would go elsewhere and the box
        // could never close, so give up instead (M2c review).
        if (!TitleBox.Focus() || !IsActive)
        {
            EndRename(commit: false);
            return;
        }
        TitleBox.SelectAll();
    }

    private void EndRename(bool commit)
    {
        if (!_renaming) return;
        _renaming = false;
        TitleBox.Visibility = Visibility.Collapsed;
        ShowTitleOrTabs();
        if (commit && TitleBox.Text != _title) RenameRequested?.Invoke(TitleBox.Text);
    }

    private void OnTitleBoxKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key is not (Key.Enter or Key.Escape)) return;
        EndRename(commit: args.Key == Key.Enter);
        ItemList.Focus();
        args.Handled = true;
    }

    private void OnItemListKeyDown(object sender, KeyEventArgs args)
    {
        if (args.OriginalSource is TextBox) return; // keys typed into the rename box
        var selected = ItemList.SelectedItems.OfType<FenceItemView>().ToList();
        switch (args.Key)
        {
            case Key.Enter when selected.Count == 1:
                OpenRequested?.Invoke(selected[0].ItemRef);
                break;
            case Key.Enter when selected.Count > 1:
                OpenManyRequested?.Invoke(selected.Select(view => view.ItemRef).ToList());
                break;
            case Key.Delete when selected.Count > 0:
                RecycleRequested?.Invoke(selected.Select(view => view.ItemRef).ToList()); // Shift+Del too: always the Recycle Bin
                break;
            case Key.Back when _isPortal:
                BackRequested?.Invoke();
                break;
            case Key.F2 when selected.Count == 1 && !_isLibrary: // library shortcuts are named by their game (M12)
                BeginItemRename(selected[0].ItemRef);
                break;
            default:
                return;
        }
        args.Handled = true;
    }

    /// <summary>Starts renaming one item in place (F2, or Rename in Windows' item menu). Special items cannot be renamed.</summary>
    public void BeginItemRename(string itemRef)
    {
        var view = _items.FirstOrDefault(candidate => candidate.ItemRef == itemRef);
        if (view is null || itemRef.StartsWith("::", StringComparison.Ordinal)) return;
        ItemList.ScrollIntoView(view);
        view.EditName = view.Label;
        view.IsEditing = true; // the box shows; OnLabelBoxVisibleChanged focuses it
    }

    private void OnLabelBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not TextBox { IsVisible: true, DataContext: FenceItemView view } box) return;
        Activate();
        // Without focus typing would go to another app and the box could never close (ADR-015): give up instead.
        if (!box.Focus() || !IsActive)
        {
            view.IsEditing = false;
            return;
        }
        // Like Explorer: select the name, not the extension.
        var extensionStart = box.Text.LastIndexOf('.');
        box.Select(0, extensionStart > 0 ? extensionStart : box.Text.Length);
    }

    private void OnLabelBoxKeyDown(object sender, KeyEventArgs args)
    {
        if (sender is not TextBox { DataContext: FenceItemView view } || args.Key is not (Key.Enter or Key.Escape)) return;
        EndItemRename(view, commit: args.Key == Key.Enter);
        ItemList.Focus();
        args.Handled = true;
    }

    private void OnLabelBoxLostFocus(object sender, KeyboardFocusChangedEventArgs args)
    {
        if (sender is TextBox { DataContext: FenceItemView view }) EndItemRename(view, commit: true);
    }

    private void EndItemRename(FenceItemView view, bool commit)
    {
        if (!view.IsEditing) return;
        view.IsEditing = false;
        var newName = view.EditName.Trim();
        if (commit && newName.Length > 0 && newName != view.Label) ItemRenameRequested?.Invoke(view.ItemRef, newName);
    }

    /// <summary>Right-click on an item opens Windows' item menu instead of the fence menu.</summary>
    private void OnBodyContextMenuOpening(object sender, ContextMenuEventArgs args)
    {
        if (ItemsControl.ContainerFromElement(ItemList, (DependencyObject)args.OriginalSource) is not ListBoxItem { DataContext: FenceItemView clicked } container) return;
        args.Handled = true;
        if (!container.IsSelected)
        {
            ItemList.SelectedItems.Clear();
            container.IsSelected = true;
        }
        // From the mouse, or (menu key: CursorLeft < 0) from the item's corner. PointToScreen gives physical pixels.
        var anchor = args.CursorLeft >= 0 ? PointToScreen(Mouse.GetPosition(this)) : container.PointToScreen(new Point(container.ActualWidth / 2, container.ActualHeight / 2));
        // The clicked item first: menu → Rename renames it, whatever else is selected (user choice 2026-10-03).
        List<string> refs = [clicked.ItemRef, .. ItemList.SelectedItems.OfType<FenceItemView>().Select(view => view.ItemRef).Where(itemRef => itemRef != clicked.ItemRef)];
        ItemMenuRequested?.Invoke(refs, (int)anchor.X, (int)anchor.Y, args.CursorLeft < 0);
    }

    /// <summary>The item under a screen point (physical pixels) and the index to insert before when dropping there.</summary>
    public FenceDropPoint HitTest(int screenX, int screenY)
    {
        // Over a tab header: show that tab now, so the drop lands in it (M9).
        if (TabHeaderAt(screenX, screenY) is { } hoveredTab && hoveredTab != FenceId) TabSelected?.Invoke(hoveredTab);
        var point = ItemList.PointFromScreen(new Point(screenX, screenY));
        string? hovered = null;
        var cells = new List<(double Left, double Top, double Width, double Height)>();
        var cellIndexes = new List<int>();
        for (var index = 0; index < _items.Count; index++)
        {
            if (ItemList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container) continue;
            var bounds = container.TransformToAncestor(ItemList).TransformBounds(new Rect(container.RenderSize));
            // Only the middle of an item means "into it" (folders, Recycle Bin); its edges reorder (M3b review I2).
            if (DropZones.IsInto(bounds.Left, bounds.Top, bounds.Width, bounds.Height, point.X, point.Y)) hovered = _items[index].ItemRef;
            cells.Add((bounds.Left, bounds.Top, bounds.Width, bounds.Height));
            cellIndexes.Add(index);
        }
        // Rows reach down to their tallest item (mixed label heights, M3b review).
        var cellAt = DropZones.InsertIndex(cells, point.X, point.Y);
        return new FenceDropPoint(hovered, cellAt < cellIndexes.Count ? cellIndexes[cellAt] : _items.Count);
    }

    /// <summary>Shows where a drag would land: a caret before the insert position, or a highlight on the container taking it.</summary>
    public void ShowDropFeedback(FenceDropPoint? drop, bool into)
    {
        InsertCaret.Visibility = Visibility.Collapsed;
        DropHighlight.Visibility = Visibility.Collapsed;
        if (drop is not { } point) return;
        if (into && _items.FirstOrDefault(view => view.ItemRef == point.ItemRef) is { } target
            && ItemList.ItemContainerGenerator.ContainerFromItem(target) is ListBoxItem targetContainer)
        {
            var cell = targetContainer.TransformToAncestor(ItemList).TransformBounds(new Rect(targetContainer.RenderSize));
            Canvas.SetLeft(DropHighlight, cell.Left);
            Canvas.SetTop(DropHighlight, cell.Top);
            DropHighlight.Width = cell.Width;
            DropHighlight.Height = cell.Height;
            DropHighlight.Visibility = Visibility.Visible;
            return;
        }
        // Caret at the left edge of the item it goes before, or after the last item.
        var before = point.InsertAt < _items.Count ? ItemList.ItemContainerGenerator.ContainerFromIndex(point.InsertAt) as ListBoxItem : null;
        var anchor = before ?? (_items.Count > 0 ? ItemList.ItemContainerGenerator.ContainerFromIndex(_items.Count - 1) as ListBoxItem : null);
        if (anchor is null) return;
        var bounds = anchor.TransformToAncestor(ItemList).TransformBounds(new Rect(anchor.RenderSize));
        Canvas.SetLeft(InsertCaret, (before is null ? bounds.Right : bounds.Left) - 1);
        Canvas.SetTop(InsertCaret, bounds.Top + 4);
        InsertCaret.Height = Math.Max(0, bounds.Height - 8);
        InsertCaret.Visibility = Visibility.Visible;
    }

    private static TAncestor? FindAncestor<TAncestor>(DependencyObject source) where TAncestor : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TAncestor match) return match;
        }
        return null;
    }

    private void OnListPress(object sender, MouseButtonEventArgs args)
    {
        if (args.OriginalSource is DependencyObject source && FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) is not null) return;
        // Text selection in the rename box must never start a drag of the file (M3b review I3).
        if (args.OriginalSource is DependencyObject pressed && FindAncestor<TextBox>(pressed) is not null) return;
        var container = args.OriginalSource is DependencyObject element ? FindAncestor<ListBoxItem>(element) : null;
        if (container is null)
        {
            // Empty space: rubber band. Without Ctrl it starts a new selection. The list takes focus, so a pending rename
            // commits and the keys after the band go to the list (M3b review carry-over).
            ItemList.Focus();
            _bandStart = args.GetPosition(ItemList);
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) ItemList.SelectedItems.Clear();
            ItemList.CaptureMouse();
            args.Handled = true;
            return;
        }
        _pressPoint = args.GetPosition(ItemList);
        // Ctrl+press on a selected item: unselect it on release, not now, so Ctrl+drag can still copy the selection (M3b review).
        if (container.IsSelected && Keyboard.Modifiers == ModifierKeys.Control && args.ClickCount == 1)
        {
            _deferredToggle = container;
            container.Focus();
            args.Handled = true;
            return;
        }
        // Pressing one of several selected items must keep the selection, so they can be dragged together.
        if (container.IsSelected && ItemList.SelectedItems.Count > 1 && Keyboard.Modifiers == ModifierKeys.None && args.ClickCount == 1)
        {
            _deferredSelect = container;
            container.Focus();
            args.Handled = true;
        }
    }

    private void OnListMove(object sender, MouseEventArgs args)
    {
        if (args.LeftButton != MouseButtonState.Pressed)
        {
            _pressPoint = null;
            return;
        }
        var position = args.GetPosition(ItemList);
        if (_bandStart is { } bandStart)
        {
            UpdateBand(bandStart, position);
            return;
        }
        if (_pressPoint is not { } pressPoint) return;
        if (Math.Abs(position.X - pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _pressPoint = null;
        _deferredSelect = null;
        _deferredToggle = null; // dragged: the item stays selected
        var dragged = ItemList.SelectedItems.OfType<FenceItemView>().Select(view => view.ItemRef).ToList();
        if (dragged.Count > 0) DragRequested?.Invoke(dragged); // returns when the drag ends (Windows' modal loop)
    }

    private void OnListRelease(object sender, MouseButtonEventArgs args)
    {
        _pressPoint = null;
        if (_deferredToggle is { } toggled)
        {
            toggled.IsSelected = false;
            _deferredToggle = null;
        }
        if (_deferredSelect is { } container)
        {
            ItemList.SelectedItems.Clear();
            container.IsSelected = true;
            _deferredSelect = null;
        }
        if (_bandStart is not null)
        {
            ItemList.ReleaseMouseCapture(); // ends the band via LostMouseCapture
            args.Handled = true;
        }
    }

    /// <summary>Selects every item the band touches (added to the selection when Ctrl was held at the start).</summary>
    private void UpdateBand(Point start, Point current)
    {
        var band = new Rect(start, current);
        Canvas.SetLeft(SelectionBand, band.Left);
        Canvas.SetTop(SelectionBand, band.Top);
        SelectionBand.Width = band.Width;
        SelectionBand.Height = band.Height;
        SelectionBand.Visibility = Visibility.Visible;
        for (var index = 0; index < _items.Count; index++)
        {
            if (ItemList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container) continue;
            var bounds = container.TransformToAncestor(ItemList).TransformBounds(new Rect(container.RenderSize));
            if (bounds.IntersectsWith(band)) container.IsSelected = true;
            else if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) container.IsSelected = false;
        }
    }

    private void EndBand()
    {
        _bandStart = null;
        SelectionBand.Visibility = Visibility.Collapsed;
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left) return;
        // A double-click in the rename box selects a word; it must not open the file (M3a review carry-over).
        if (args.OriginalSource is DependencyObject source && FindAncestor<TextBox>(source) is not null) return;
        if (ItemsControl.ContainerFromElement(ItemList, (DependencyObject)args.OriginalSource) is ListBoxItem { DataContext: FenceItemView view })
            OpenRequested?.Invoke(view.ItemRef);
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        Handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(Handle).AddHook(OnMessage);
        FenceWindowChrome.ApplyToolWindowStyles(Handle);
        FenceWindowChrome.ApplyAccentBlur(Handle);
        if (!FenceWindowChrome.UseRoundedCorners(Handle)) CornersUnavailable = true; // Windows 10: square blur corners (ADR-024)
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // Windows' item menu draws "Send to", "Open with" and shell-extension entries through its owner window.
        if (ShellItemMenu.HandleMenuMessage(message, wParam, lParam, out var menuResult))
        {
            handled = true;
            return menuResult;
        }
        switch (message)
        {
            case WmWindowPosChanging:
                if (!Peeking) FenceWindowChrome.KeepAtBottom(lParam); // fences never rise above apps, except during Peek
                break;
            // Click mode: the first press on a rolled-up title opens it instead of starting a move (M6b).
            case WmNcLeftButtonDown when wParam == HitTestCaption && IsSecondCaptionClick(lParam):
                RollUpToggled?.Invoke();
                handled = true;
                return 0;
            case WmNcLeftButtonDown when wParam == HitTestCaption && ClickToOpen():
                handled = true;
                return 0;
            case WmNcLeftButtonDoubleClick when wParam == HitTestCaption:
                _lastCaptionPress = null;
                // A third quick click after a recognised double-click arrives as DBLCLK: not a second toggle (M8b review).
                if (!IsRightAfterRecognisedDoubleClick()) RollUpToggled?.Invoke();
                _recognisedDoubleClickAt = null; // used once: a later genuine double-click toggles (M8c review M12)
                handled = true;
                return 0;
            case WmEnterSizeMove:
                // A move that starts during a roll-up animation finishes it. Windows' move loop captured the partial rect
                // and proposes it on every step, so WM_MOVING keeps the finished height too (M6b review M2).
                _moveHeightPx = null;
                if (_heightAnimation.IsEnabled)
                {
                    _heightAnimation.Stop();
                    _moveHeightPx = _heightTo;
                    FenceWindowChrome.SetPixelRect(Handle, FenceWindowChrome.GetPixelRect(Handle) with { Height = _heightTo });
                }
                _drag = new DragTracker(FenceWindowChrome.GetPixelRect(Handle));
                break;
            case WmMoving when SnapRect is not null && _drag is not null:
                var proposed = FenceWindowChrome.ReadRect(lParam);
                if (_moveHeightPx is { } heightPx) proposed = proposed with { Height = heightPx };
                FenceWindowChrome.WriteRect(lParam, _drag.Step(proposed, snap: rect => SnapRect(rect, SnapEdges.Move)));
                handled = true;
                return 1;
            case WmSizing when SnapRect is not null && _drag is not null:
                var edges = SizingEdges((int)wParam);
                FenceWindowChrome.WriteRect(lParam, _drag.Step(FenceWindowChrome.ReadRect(lParam), snap: rect => SnapRect(rect, edges)));
                handled = true;
                return 1;
            case WmExitSizeMove:
                _drag = null;
                _moveHeightPx = null;
                // Rolled up, the window is shorter than the fence: the stored rect keeps the full height.
                var moved = FenceWindowChrome.GetPixelRect(Handle);
                MovedByUser?.Invoke(this, _rolledUp ? moved with { Height = _fullHeightPx } : moved);
                if (_rolledUp && !_expansion.Expanded && moved.Height != RolledUpHeightPx) AnimateHeight(RolledUpHeightPx); // a move during an animation
                break;
        }
        return 0;
    }

    /// <summary>
    /// Every caption press is remembered; a second one within the double-click time and size counts as a double-click.
    /// Right after another window hands the fence the foreground, Windows sends the second press as a plain
    /// WM_NCLBUTTONDOWN instead of WM_NCLBUTTONDBLCLK, so the first double-click did nothing (M6a finding 4, M8b).
    /// </summary>
    /// <param name="pointParam">The press's screen point (WM_NCLBUTTONDOWN's lParam): where it happened, not where the
    /// cursor is when a busy UI thread gets to it; its time is the message's own (M8b review).</param>
    private bool IsSecondCaptionClick(nint pointParam)
    {
        var (milliseconds, widthPx, heightPx) = FenceWindowChrome.DoubleClickSettings();
        var at = FenceWindowChrome.MessageTime();
        var (x, y) = ((short)(pointParam & 0xFFFF), (short)((pointParam >> 16) & 0xFFFF));
        var isSecond = _lastCaptionPress is { } last && unchecked(at - last.At) <= milliseconds
                       && Math.Abs(x - last.Where.X) <= widthPx / 2.0 && Math.Abs(y - last.Where.Y) <= heightPx / 2.0;
        _lastCaptionPress = isSecond ? null : (at, new Point(x, y));
        if (isSecond) _recognisedDoubleClickAt = at;
        return isSecond;
    }

    private bool IsRightAfterRecognisedDoubleClick() =>
        _recognisedDoubleClickAt is { } recognised && unchecked(FenceWindowChrome.MessageTime() - recognised) <= FenceWindowChrome.DoubleClickSettings().Milliseconds;

    /// <summary>WM_SIZING's WMSZ_* value as the edges being dragged.</summary>
    private static SnapEdges SizingEdges(int sizingEdge) => sizingEdge switch
    {
        1 => SnapEdges.Left,
        2 => SnapEdges.Right,
        3 => SnapEdges.Top,
        4 => SnapEdges.Top | SnapEdges.Left,
        5 => SnapEdges.Top | SnapEdges.Right,
        6 => SnapEdges.Bottom,
        7 => SnapEdges.Bottom | SnapEdges.Left,
        8 => SnapEdges.Bottom | SnapEdges.Right,
        _ => SnapEdges.None,
    };
}
```
`src/NeoFences.App/SettingsWindow.xaml`:
```xml
<Window x:Class="NeoFences.App.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="NeoFences settings" Width="600" Height="680" MinWidth="460" MinHeight="400"
        WindowStartupLocation="CenterScreen" ThemeMode="System">
    <!-- Fluent (WPF's built-in theme, ThemeMode="System"): follows Windows light/dark and the accent colour (spec §6).
         Set only on this window: the fences keep their own look. Changes apply at once; there is no OK button. -->
    <Window.Resources>
        <Style x:Key="SectionHeader" TargetType="TextBlock">
            <Setter Property="FontSize" Value="14" />
            <Setter Property="FontWeight" Value="SemiBold" />
            <Setter Property="Margin" Value="2,20,0,8" />
        </Style>
        <Style x:Key="Card" TargetType="Border">
            <Setter Property="Background" Value="{DynamicResource CardBackgroundFillColorDefaultBrush}" />
            <Setter Property="BorderBrush" Value="{DynamicResource CardStrokeColorDefaultBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="CornerRadius" Value="6" />
            <Setter Property="Padding" Value="16,12" />
            <Setter Property="Margin" Value="0,0,0,4" />
        </Style>
        <Style x:Key="Description" TargetType="TextBlock">
            <Setter Property="Foreground" Value="{DynamicResource TextFillColorSecondaryBrush}" />
            <Setter Property="FontSize" Value="12" />
            <Setter Property="TextWrapping" Value="Wrap" />
            <Setter Property="Margin" Value="0,2,0,0" />
        </Style>
    </Window.Resources>
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="28,16,28,28">
            <TextBlock Text="Settings" FontSize="28" FontWeight="SemiBold" />

            <TextBlock Text="General" Style="{StaticResource SectionHeader}" />
            <Border Style="{StaticResource Card}">
                <DockPanel>
                    <CheckBox x:Name="StartupBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Start with Windows" />
                    <StackPanel>
                        <TextBlock Text="Start with Windows" TextWrapping="Wrap" />
                        <TextBlock x:Name="StartupDescription" Style="{StaticResource Description}" Text="Your fences come back by themselves after a restart or a power cut." />
                    </StackPanel>
                </DockPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <DockPanel>
                    <CheckBox x:Name="TakeoverBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Hide desktop icons" />
                    <StackPanel>
                        <TextBlock Text="Hide desktop icons" TextWrapping="Wrap" />
                        <TextBlock x:Name="TakeoverDescription" Style="{StaticResource Description}" Text="Show your desktop items only in fences. They are back on the desktop whenever NeoFences is paused or closed." />
                    </StackPanel>
                </DockPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <DockPanel>
                        <TextBox x:Name="HotkeyBox" DockPanel.Dock="Right" Width="190" IsReadOnly="True" IsReadOnlyCaretVisible="False"
                                 VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Peek hotkey" />
                        <StackPanel>
                            <TextBlock Text="Peek hotkey" TextWrapping="Wrap" />
                            <TextBlock x:Name="HotkeyDescription" Style="{StaticResource Description}" Text="Shows your fences above all windows. Click the box, then press the new combination (with Alt or Win, or an F-key)." />
                        </StackPanel>
                    </DockPanel>
                    <!-- Assertive live region: screen readers announce a refused combination at once (M6b review carry-over). -->
                    <TextBlock x:Name="HotkeyStatus" Style="{StaticResource Description}" Margin="0,8,0,0" Visibility="Collapsed"
                               AutomationProperties.LiveSetting="Assertive" />
                </StackPanel>
            </Border>

            <TextBlock Text="Fences" Style="{StaticResource SectionHeader}" />
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <DockPanel>
                        <ComboBox x:Name="LabelsBox" DockPanel.Dock="Right" Width="230" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Labels for new fences">
                            <ComboBoxItem Content="Always" Tag="Always" />
                            <ComboBoxItem Content="On hover (icons only)" Tag="OnHover" />
                        </ComboBox>
                        <StackPanel>
                            <TextBlock Text="Labels for new fences" TextWrapping="Wrap" />
                            <TextBlock x:Name="LabelsDescription" Style="{StaticResource Description}" Text="Icons only shows an item's name when you point at it or select it. Each fence can also choose in its own menu." />
                        </StackPanel>
                    </DockPanel>
                    <Button x:Name="LabelsApplyAllButton" Content="Apply to all fences" HorizontalAlignment="Left" Margin="0,10,0,0" />
                </StackPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <DockPanel>
                    <CheckBox x:Name="ArrowsBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Show shortcut arrows" />
                    <StackPanel>
                        <TextBlock Text="Show shortcut arrows" TextWrapping="Wrap" />
                        <TextBlock x:Name="ArrowsDescription" Style="{StaticResource Description}" Text="The small arrow Windows draws on shortcut icons." />
                    </StackPanel>
                </DockPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <DockPanel>
                    <ComboBox x:Name="RollupBox" DockPanel.Dock="Right" Width="230" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Rolled-up fences open">
                        <ComboBoxItem Content="When the mouse rests on them" Tag="Hover" />
                        <ComboBoxItem Content="When you click their title" Tag="Click" />
                    </ComboBox>
                    <StackPanel>
                        <TextBlock Text="Rolled-up fences open" TextWrapping="Wrap" />
                        <TextBlock x:Name="RollupDescription" Style="{StaticResource Description}" Text="Double-click a fence's title to roll it up to its title bar." />
                    </StackPanel>
                </DockPanel>
            </Border>

            <TextBlock Text="Game mode" Style="{StaticResource SectionHeader}" />
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <DockPanel>
                        <CheckBox x:Name="GameModeBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Go idle while a full-screen game runs" />
                        <StackPanel>
                            <TextBlock Text="Go idle while a full-screen game runs" TextWrapping="Wrap" />
                            <TextBlock x:Name="GameModeDescription" Style="{StaticResource Description}" Text="NeoFences removes its mouse hook and waits with background work, so games get every bit of input. Fences stay where they are." />
                        </StackPanel>
                    </DockPanel>
                    <TextBlock x:Name="GameModeStatus" Style="{StaticResource Description}" Margin="0,8,0,0" />
                </StackPanel>
            </Border>

            <TextBlock Text="Snapshots" Style="{StaticResource SectionHeader}" />
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <TextBlock x:Name="SnapshotsDescription" Style="{StaticResource Description}" Margin="0,0,0,8"
                               Text="Save how your fences are arranged and put it back later. Restoring first saves 'Before restore', so it can be undone. Your files and settings are never changed." />
                    <ListBox x:Name="SnapshotList" MaxHeight="220" AutomationProperties.Name="Snapshots">
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <DockPanel>
                                    <TextBlock DockPanel.Dock="Right" Text="{Binding When}" Opacity="0.7" Margin="16,0,0,0" />
                                    <TextBlock Text="{Binding Name}" TextTrimming="CharacterEllipsis" />
                                </DockPanel>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                        <ListBox.ItemContainerStyle>
                            <Style TargetType="ListBoxItem" BasedOn="{StaticResource {x:Type ListBoxItem}}">
                                <Setter Property="AutomationProperties.Name" Value="{Binding Spoken}" />
                            </Style>
                        </ListBox.ItemContainerStyle>
                    </ListBox>
                    <TextBox x:Name="SnapshotNameBox" Visibility="Collapsed" Margin="0,8,0,0" AutomationProperties.Name="New name for the snapshot" />
                    <WrapPanel Margin="0,10,0,0">
                        <Button x:Name="TakeSnapshotButton" Content="Take snapshot" Margin="0,0,8,6" />
                        <Button x:Name="RestoreSnapshotButton" Content="Restore" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="RenameSnapshotButton" Content="Rename" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="DeleteSnapshotButton" Content="Delete" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="OpenSnapshotsButton" Content="Open snapshots folder" Margin="0,0,0,6" />
                    </WrapPanel>
                </StackPanel>
            </Border>

            <TextBlock Text="Rules" Style="{StaticResource SectionHeader}" />
            <Border x:Name="RulesCard" Style="{StaticResource Card}">
                <StackPanel>
                    <TextBlock x:Name="RulesDescription" Style="{StaticResource Description}" Margin="0,0,0,8"
                               Text="New Desktop items go to the fence of the first rule they match; the rest go to the Inbox. Rules only choose fences: files are never moved or changed." />
                    <ListBox x:Name="RuleList" MaxHeight="220" AutomationProperties.Name="Rules">
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <DockPanel>
                                    <CheckBox IsChecked="{Binding Enabled, Mode=OneWay}" Tag="{Binding Id}" Checked="OnRuleToggled" Unchecked="OnRuleToggled"
                                              VerticalAlignment="Center" Margin="0,0,8,0" AutomationProperties.Name="{Binding ToggleName}" />
                                    <TextBlock Text="{Binding Text}" Opacity="{Binding Opacity}" TextTrimming="CharacterEllipsis" VerticalAlignment="Center" />
                                </DockPanel>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                        <ListBox.ItemContainerStyle>
                            <Style TargetType="ListBoxItem" BasedOn="{StaticResource {x:Type ListBoxItem}}">
                                <Setter Property="AutomationProperties.Name" Value="{Binding Spoken}" />
                            </Style>
                        </ListBox.ItemContainerStyle>
                    </ListBox>
                    <StackPanel x:Name="RuleEditor" Visibility="Collapsed" Margin="0,10,0,0">
                        <WrapPanel>
                            <TextBlock Text="When" VerticalAlignment="Center" Margin="0,0,8,6" />
                            <ComboBox x:Name="RuleKindBox" Width="170" Margin="0,0,8,6" AutomationProperties.Name="Rule condition">
                                <ComboBoxItem Content="Type of file" Tag="Type" />
                                <ComboBoxItem Content="Game shortcut" Tag="Game" />
                                <ComboBoxItem Content="Name" Tag="Name" />
                                <ComboBoxItem Content="Date modified" Tag="Age" />
                                <ComboBoxItem Content="Size" Tag="Size" />
                            </ComboBox>
                            <ComboBox x:Name="RuleChoiceBox" Width="190" Margin="0,0,8,6" />
                            <TextBox x:Name="RuleTextBox" Width="150" Margin="0,0,8,6" />
                            <Button x:Name="RuleBrowseButton" Content="Choose…" Margin="0,0,8,6" Visibility="Collapsed" AutomationProperties.Name="Choose the game folder" />
                            <TextBlock x:Name="RuleUnitText" VerticalAlignment="Center" Margin="0,0,8,6" />
                        </WrapPanel>
                        <WrapPanel>
                            <TextBlock Text="Put in" VerticalAlignment="Center" Margin="0,0,8,6" />
                            <ComboBox x:Name="RuleFenceBox" Width="220" Margin="0,0,8,6" DisplayMemberPath="Title" AutomationProperties.Name="Put in fence" />
                            <Button x:Name="SaveRuleButton" Content="Save rule" Margin="0,0,8,6" IsDefault="False" />
                            <Button x:Name="CancelRuleButton" Content="Cancel" Margin="0,0,8,6" />
                        </WrapPanel>
                        <TextBlock x:Name="RuleHint" Style="{StaticResource Description}" />
                    </StackPanel>
                    <WrapPanel Margin="0,10,0,0">
                        <Button x:Name="AddRuleButton" Content="Add" Margin="0,0,8,6" />
                        <Button x:Name="EditRuleButton" Content="Edit" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="DeleteRuleButton" Content="Delete" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="MoveRuleUpButton" Content="Move up" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="MoveRuleDownButton" Content="Move down" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="ApplyRulesButton" Content="Apply rules now" Margin="0,0,0,6" />
                    </WrapPanel>
                    <TextBlock x:Name="RulesStatus" Style="{StaticResource Description}" Visibility="Collapsed" />
                </StackPanel>
            </Border>

            <TextBlock Text="Game Library" Style="{StaticResource SectionHeader}" />
            <Border x:Name="LibraryCard" Style="{StaticResource Card}">
                <StackPanel>
                    <TextBlock x:Name="LibraryDescription" Style="{StaticResource Description}" Margin="0,0,0,8"
                               Text="The Game Library fence lists every installed game it finds. Launcher games start through their launcher. Nothing is downloaded, and your games and files are never changed." />
                    <TextBlock Text="Game folders (each sub-folder is a game)" Margin="0,4,0,4" />
                    <ListBox x:Name="GameFolderList" MaxHeight="120" AutomationProperties.Name="Game folders" />
                    <WrapPanel Margin="0,8,0,0">
                        <Button x:Name="AddGameFolderButton" Content="Add folder…" Margin="0,0,8,6" />
                        <Button x:Name="RemoveGameFolderButton" Content="Remove" Margin="0,0,8,6" IsEnabled="False" />
                    </WrapPanel>
                    <TextBlock Text="Look for games in" Margin="0,8,0,4" />
                    <WrapPanel>
                        <CheckBox x:Name="SteamSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="EpicSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="GogSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="UbisoftSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="EaSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="BattleNetSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="XboxSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="FoldersSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="ShortcutsSourceBox" Margin="0,0,16,6" />
                    </WrapPanel>
                    <TextBlock Text="Hidden games" Margin="0,8,0,4" />
                    <TextBlock x:Name="HiddenGamesEmpty" Style="{StaticResource Description}" Text="None. Right-click a game → Hide from library." />
                    <ListBox x:Name="HiddenGameList" MaxHeight="120" AutomationProperties.Name="Hidden games" />
                    <WrapPanel Margin="0,8,0,0">
                        <Button x:Name="ShowGameAgainButton" Content="Show again" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="RefreshLibraryButton" Content="Refresh library now" Margin="0,0,8,6" />
                    </WrapPanel>
                    <TextBlock x:Name="LibraryStatus" Style="{StaticResource Description}" />
                </StackPanel>
            </Border>

            <TextBlock Text="About and logs" Style="{StaticResource SectionHeader}" />
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <TextBlock x:Name="VersionText" />
                    <TextBlock x:Name="DataFolderText" Style="{StaticResource Description}" />
                    <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
                        <Button x:Name="OpenLogsButton" Content="Open logs folder" Margin="0,0,8,0" />
                        <Button x:Name="OpenDataButton" Content="Open data folder" />
                    </StackPanel>
                </StackPanel>
            </Border>
        </StackPanel>
    </ScrollViewer>
</Window>
```
`src/NeoFences.App/SettingsWindow.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NeoFences.Core.Model;

namespace NeoFences.App;

/// <summary>What the settings window shows; the host builds it from the config and the run state.</summary>
/// <param name="PeekHotkey">As a person reads it (key caps).</param>
/// <param name="PeekHotkeyActive">False when Windows refused it (another app owns it): the window says so.</param>
public sealed record SettingsView(
    bool StartWithWindows, bool Takeover, string PeekHotkey, bool PeekHotkeyActive, RollupExpand RollupExpand,
    bool GameModeEnabled, bool GameModeActive, string Version, string DataFolder, LabelMode DefaultLabels, bool ShowShortcutArrows,
    IReadOnlyList<NeoFences.Core.Config.SnapshotEntry> Snapshots, IReadOnlyList<Rule> Rules, IReadOnlyList<string> RuleLines,
    IReadOnlyList<RuleFence> RuleFences, LibraryView Library);

/// <summary>One row of the Snapshots list (M10).</summary>
public sealed record SnapshotRow(string Path, string Name, string When)
{
    public string Spoken => $"{Name}, {When}";
}

/// <summary>
/// The settings window (spec §6, M6b): General, Fences, Game mode, About and logs. Every change is reported to the host
/// at once; the host applies, saves and calls <see cref="Show(SettingsView)"/> back with the result.
/// </summary>
public partial class SettingsWindow : Window
{
    private bool _updating; // filling the controls from the host must not report changes back

    public event Action<bool>? StartWithWindowsChanged;
    public event Action<bool>? TakeoverChanged;
    /// <summary>A new Peek hotkey was pressed in the box (text like "Ctrl+Alt+P"); answer with <see cref="ShowHotkeyResult"/>.</summary>
    public event Action<string>? PeekHotkeyChosen;
    public event Action<RollupExpand>? RollupExpandChanged;
    public event Action<bool>? GameModeChanged;
    public event Action? OpenLogsRequested;
    public event Action? OpenDataRequested;
    public event Action? TakeSnapshotRequested;
    public event Action<string>? RestoreSnapshotRequested;
    public event Action<string, string>? RenameSnapshotRequested;
    public event Action<string>? DeleteSnapshotRequested;
    public event Action? OpenSnapshotsRequested;
    /// <summary>The hotkey box got (true) or lost (false) the keyboard: the host releases the Peek hotkey meanwhile (M6b review).</summary>
    public event Action<bool>? HotkeyRecording;
    public event Action<LabelMode>? DefaultLabelsChanged;
    public event Action<LabelMode>? LabelsAppliedToAll;
    public event Action<bool>? ShortcutArrowsChanged;

    public SettingsWindow()
    {
        InitializeComponent();
        // Checked/Unchecked, not Click: UI Automation (Narrator, Toggle) changes the box without a click (M6b smoke).
        OnToggled(StartupBox, isChecked => StartWithWindowsChanged?.Invoke(isChecked));
        OnToggled(TakeoverBox, isChecked => TakeoverChanged?.Invoke(isChecked));
        OnToggled(GameModeBox, isChecked => GameModeChanged?.Invoke(isChecked));
        RollupBox.SelectionChanged += (_, _) =>
        {
            if (!_updating && RollupBox.SelectedItem is ComboBoxItem { Tag: string mode }) RollupExpandChanged?.Invoke(Enum.Parse<RollupExpand>(mode));
        };
        HotkeyBox.PreviewKeyDown += OnHotkeyKeyDown;
        HotkeyBox.GotKeyboardFocus += (_, _) =>
        {
            HotkeyRecording?.Invoke(true); // so pressing the current combination is recorded, not Peek
            ShowHotkeyHint("Press the new combination… (Esc keeps the current one)");
        };
        HotkeyBox.LostKeyboardFocus += (_, _) =>
        {
            HotkeyRecording?.Invoke(false);
            if (HotkeyStatus.Tag is null) HotkeyStatus.Visibility = Visibility.Collapsed;
        };
        OnToggled(ArrowsBox, isChecked => ShortcutArrowsChanged?.Invoke(isChecked));
        LabelsBox.SelectionChanged += (_, _) => { if (!_updating) DefaultLabelsChanged?.Invoke(SelectedLabels); };
        LabelsApplyAllButton.Click += (_, _) => LabelsAppliedToAll?.Invoke(SelectedLabels);
        // Screen readers read each setting's description with it (M6b review carry-over).
        foreach (var (control, description) in new (UIElement, TextBlock)[]
                 { (StartupBox, StartupDescription), (TakeoverBox, TakeoverDescription), (HotkeyBox, HotkeyDescription),
                   (LabelsBox, LabelsDescription), (ArrowsBox, ArrowsDescription), (RollupBox, RollupDescription), (GameModeBox, GameModeDescription) })
        {
            System.Windows.Automation.AutomationProperties.SetHelpText(control, description.Text);
        }
        OpenLogsButton.Click += (_, _) => OpenLogsRequested?.Invoke();
        OpenDataButton.Click += (_, _) => OpenDataRequested?.Invoke();
        TakeSnapshotButton.Click += (_, _) => TakeSnapshotRequested?.Invoke();
        OpenSnapshotsButton.Click += (_, _) => OpenSnapshotsRequested?.Invoke();
        RestoreSnapshotButton.Click += (_, _) => { if (SelectedSnapshot is { } row) RestoreSnapshotRequested?.Invoke(row.Path); };
        DeleteSnapshotButton.Click += (_, _) => { if (SelectedSnapshot is { } row) DeleteSnapshotRequested?.Invoke(row.Path); };
        RenameSnapshotButton.Click += (_, _) => BeginSnapshotRename();
        SnapshotList.SelectionChanged += (_, _) => UpdateSnapshotButtons();
        SnapshotList.MouseDoubleClick += (_, _) => { if (SelectedSnapshot is { } row) RestoreSnapshotRequested?.Invoke(row.Path); };
        SnapshotNameBox.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter) EndSnapshotRename(commit: true);
            else if (key.Key == Key.Escape) EndSnapshotRename(commit: false);
            else return;
            key.Handled = true;
        };
        SnapshotNameBox.LostKeyboardFocus += (_, _) => EndSnapshotRename(commit: true);
        System.Windows.Automation.AutomationProperties.SetHelpText(SnapshotList, SnapshotsDescription.Text);
        InitializeRules();
        InitializeLibrary();
    }

    private void OnToggled(CheckBox box, Action<bool> report)
    {
        box.Checked += (_, _) => { if (!_updating) report(true); };
        box.Unchecked += (_, _) => { if (!_updating) report(false); };
    }

    private LabelMode SelectedLabels => LabelsBox.SelectedIndex == 1 ? LabelMode.OnHover : LabelMode.Always;

    private SnapshotRow? SelectedSnapshot => SnapshotList.SelectedItem as SnapshotRow;
    private string? _renamingPath;

    private void UpdateSnapshotButtons()
    {
        var selected = SelectedSnapshot is not null;
        RestoreSnapshotButton.IsEnabled = selected;
        RenameSnapshotButton.IsEnabled = selected;
        DeleteSnapshotButton.IsEnabled = selected;
    }

    private void BeginSnapshotRename()
    {
        if (SelectedSnapshot is not { } row) return;
        _renamingPath = row.Path;
        SnapshotNameBox.Text = row.Name;
        SnapshotNameBox.Visibility = Visibility.Visible;
        SnapshotNameBox.Focus();
        SnapshotNameBox.SelectAll();
    }

    private void EndSnapshotRename(bool commit)
    {
        if (_renamingPath is not { } path) return;
        _renamingPath = null;
        SnapshotNameBox.Visibility = Visibility.Collapsed;
        if (commit && SnapshotNameBox.Text.Trim() is { Length: > 0 } name) RenameSnapshotRequested?.Invoke(path, name);
    }

    /// <summary>The snapshot list, keeping the selection where the same file is still listed.</summary>
    private void ShowSnapshots(IReadOnlyList<NeoFences.Core.Config.SnapshotEntry> snapshots)
    {
        var selectedPath = SelectedSnapshot?.Path;
        var rows = snapshots.Select(entry => new SnapshotRow(entry.Path, entry.Name, entry.TakenAt.ToLocalTime().ToString("d MMM yyyy, HH:mm"))).ToList();
        SnapshotList.ItemsSource = rows;
        SnapshotList.SelectedItem = rows.FirstOrDefault(row => row.Path == selectedPath);
        UpdateSnapshotButtons();
    }

    public void Show(SettingsView view)
    {
        ShowSnapshots(view.Snapshots);
        ShowRules(view);
        _updating = true;
        LabelsBox.SelectedIndex = view.DefaultLabels == LabelMode.OnHover ? 1 : 0;
        ArrowsBox.IsChecked = view.ShowShortcutArrows;
        if (!view.PeekHotkeyActive && !HotkeyBox.IsKeyboardFocused)
        {
            ShowHotkeyResult(saved: false, message: $"{view.PeekHotkey} is not active: Windows or another app owns it. Record another combination.");
        }
        StartupBox.IsChecked = view.StartWithWindows;
        TakeoverBox.IsChecked = view.Takeover;
        HotkeyBox.Text = view.PeekHotkey;
        RollupBox.SelectedIndex = view.RollupExpand == RollupExpand.Click ? 1 : 0;
        GameModeBox.IsChecked = view.GameModeEnabled;
        ShowLibrary(view.Library);
        GameModeStatus.Text = !view.GameModeEnabled ? "Off: NeoFences stays fully active during games."
            : view.GameModeActive ? "Right now: idle, a full-screen app is in front." : "Right now: active (no full-screen app in front).";
        VersionText.Text = $"NeoFences {view.Version}";
        DataFolderText.Text = $"Settings, backups and logs: {view.DataFolder}";
        _updating = false;
    }

    /// <summary>The host's answer to <see cref="PeekHotkeyChosen"/>: saved, or why not (invalid, or taken by another app).</summary>
    public void ShowHotkeyResult(bool saved, string message)
    {
        var changed = HotkeyStatus.Text != message || HotkeyStatus.Visibility != Visibility.Visible;
        HotkeyStatus.Tag = saved ? null : "error"; // an error stays visible after the box loses focus
        HotkeyStatus.Text = message;
        HotkeyStatus.Foreground = saved ? SecondaryText : System.Windows.Media.Brushes.IndianRed;
        HotkeyStatus.Visibility = Visibility.Visible;
        if (changed) AnnounceHotkeyStatus(); // Settings refreshes often (game mode): say a warning once
    }

    private void AnnounceHotkeyStatus() =>
        System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(HotkeyStatus)
            .RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);

    /// <summary>Fluent's secondary text colour (the grey a missing theme resource falls back to).</summary>
    private System.Windows.Media.Brush SecondaryText => TryFindResource("TextFillColorSecondaryBrush") as System.Windows.Media.Brush ?? SystemColors.GrayTextBrush;

    private void ShowHotkeyHint(string hint)
    {
        HotkeyStatus.Tag = null;
        HotkeyStatus.Text = hint;
        HotkeyStatus.Foreground = SecondaryText;
        HotkeyStatus.Visibility = Visibility.Visible;
    }

    /// <summary>Records a combination: modifiers alone only preview; Esc leaves the box; the first other key decides.</summary>
    private void OnHotkeyKeyDown(object sender, KeyEventArgs pressed)
    {
        var key = pressed.Key == Key.System ? pressed.SystemKey : pressed.Key; // Alt combinations arrive as Key.System
        // Tab / Shift+Tab move on and Alt+F4 closes, as everywhere: never trap them in the box (M6b review I1).
        if (key == Key.Tab && (Keyboard.Modifiers & ~ModifierKeys.Shift) == ModifierKeys.None) return;
        if (key == Key.F4 && Keyboard.Modifiers == ModifierKeys.Alt) return;
        pressed.Handled = true;
        key = key switch { Key.ImeProcessed => pressed.ImeProcessedKey, Key.DeadCharProcessed => pressed.DeadCharProcessedKey, _ => key };
        if (key == Key.Escape)
        {
            Keyboard.ClearFocus();
            return;
        }
        var modifiers = Keyboard.Modifiers;
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            ShowHotkeyHint(string.Join("+", parts.Append("…")));
            return;
        }
        parts.Add(key.ToString());
        PeekHotkeyChosen?.Invoke(string.Join("+", parts));
    }
}
```
`src/NeoFences.App/SettingsWindow.Library.cs`:
```csharp
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>A game the user hid: one of its ids, its name, and every id it goes by (M12).</summary>
public sealed record HiddenGame(string Id, string Name, IReadOnlyList<string> AllIds)
{
    public override string ToString() => Name;
}

/// <summary>What Settings → Game Library shows (M12).</summary>
public sealed record LibraryView(bool HasFence, IReadOnlyList<string> Folders, LibrarySources Sources, IReadOnlyList<HiddenGame> Hidden, string Status);

/// <summary>
/// Settings → Game Library (M12, spec §4): game folders, a checkbox per source, hidden games with "Show again", and
/// "Refresh library now". Every change goes to the host, which saves, rescans and shows it back.
/// </summary>
public partial class SettingsWindow
{
    public event Action<IReadOnlyList<string>>? LibraryFoldersChanged;
    public event Action<LibrarySources>? LibrarySourcesChanged;
    public event Action<string>? ShowGameAgainRequested;
    public event Action? RefreshLibraryRequested;

    private IReadOnlyList<string> _libraryFolders = [];

    private (CheckBox Box, string Name)[] SourceBoxes =>
    [
        (SteamSourceBox, "Steam"), (EpicSourceBox, "Epic"), (GogSourceBox, "GOG"), (UbisoftSourceBox, "Ubisoft Connect"), (EaSourceBox, "EA app"),
        (BattleNetSourceBox, "Battle.net"), (XboxSourceBox, "Xbox / Microsoft Store"), (FoldersSourceBox, "My game folders"), (ShortcutsSourceBox, "Game shortcuts on the Desktop"),
    ];

    private void InitializeLibrary()
    {
        foreach (var (box, name) in SourceBoxes)
        {
            box.Content = name;
            OnToggled(box, _ => LibrarySourcesChanged?.Invoke(SelectedSources));
        }
        AddGameFolderButton.Click += (_, _) =>
        {
            var folder = FolderPicker.TryPick(new WindowInteropHelper(this).Handle, "Choose a folder whose sub-folders are games",
                logFailure: failure => Log.Warning(failure, "game library: the folder picker failed"));
            if (folder is not null && !_libraryFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) LibraryFoldersChanged?.Invoke([.. _libraryFolders, folder]);
        };
        RemoveGameFolderButton.Click += (_, _) =>
        {
            if (GameFolderList.SelectedItem is string folder) LibraryFoldersChanged?.Invoke([.. _libraryFolders.Where(other => !string.Equals(other, folder, StringComparison.OrdinalIgnoreCase))]);
        };
        GameFolderList.SelectionChanged += (_, _) => RemoveGameFolderButton.IsEnabled = GameFolderList.SelectedItem is not null;
        HiddenGameList.SelectionChanged += (_, _) => ShowGameAgainButton.IsEnabled = HiddenGameList.SelectedItem is not null;
        ShowGameAgainButton.Click += (_, _) => { if (HiddenGameList.SelectedItem is HiddenGame game) ShowGameAgainRequested?.Invoke(game.Id); };
        RefreshLibraryButton.Click += (_, _) => RefreshLibraryRequested?.Invoke();
        AutomationProperties.SetHelpText(GameFolderList, LibraryDescription.Text);
    }

    private LibrarySources SelectedSources => new()
    {
        Steam = SteamSourceBox.IsChecked == true, Epic = EpicSourceBox.IsChecked == true, Gog = GogSourceBox.IsChecked == true,
        Ubisoft = UbisoftSourceBox.IsChecked == true, Ea = EaSourceBox.IsChecked == true, BattleNet = BattleNetSourceBox.IsChecked == true,
        Xbox = XboxSourceBox.IsChecked == true, Folders = FoldersSourceBox.IsChecked == true, DesktopShortcuts = ShortcutsSourceBox.IsChecked == true,
    };

    /// <summary>Called inside <see cref="Show(SettingsView)"/>'s "updating" block: setting the boxes reports nothing back.</summary>
    private void ShowLibrary(LibraryView view)
    {
        _libraryFolders = view.Folders;
        var selectedFolder = GameFolderList.SelectedItem as string;
        GameFolderList.ItemsSource = view.Folders;
        GameFolderList.SelectedItem = view.Folders.FirstOrDefault(folder => folder == selectedFolder);
        RemoveGameFolderButton.IsEnabled = GameFolderList.SelectedItem is not null;
        var sources = view.Sources;
        (SteamSourceBox.IsChecked, EpicSourceBox.IsChecked, GogSourceBox.IsChecked) = (sources.Steam, sources.Epic, sources.Gog);
        (UbisoftSourceBox.IsChecked, EaSourceBox.IsChecked, BattleNetSourceBox.IsChecked) = (sources.Ubisoft, sources.Ea, sources.BattleNet);
        (XboxSourceBox.IsChecked, FoldersSourceBox.IsChecked, ShortcutsSourceBox.IsChecked) = (sources.Xbox, sources.Folders, sources.DesktopShortcuts);
        var selectedGame = (HiddenGameList.SelectedItem as HiddenGame)?.Id;
        HiddenGameList.ItemsSource = view.Hidden;
        HiddenGameList.SelectedItem = view.Hidden.FirstOrDefault(game => game.Id == selectedGame);
        ShowGameAgainButton.IsEnabled = HiddenGameList.SelectedItem is not null;
        HiddenGamesEmpty.Visibility = view.Hidden.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LibraryStatus.Text = view.Status;
        RefreshLibraryButton.IsEnabled = view.HasFence;
    }
}
```
`src/NeoFences.App/FenceHost.Library.cs`:
```csharp
using System.IO;
using System.Windows.Threading;
using NeoFences.Core.Library;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// The Game Library fence (M12, spec 2026-10-03-game-library-design, ADR-032): scans the launchers, the Xbox app, the
/// user's game folders and Desktop game shortcuts on its own STA thread, merges them (Core <see cref="GameCatalog"/>),
/// keeps one shortcut per game in <see cref="AppPaths.LibraryDirectory"/> and shows that folder like a Portal, as tiles.
/// Nothing is started or changed outside NeoFences' own folder.
/// </summary>
public sealed partial class FenceHost
{
    private static readonly TimeSpan LibraryQuietTime = TimeSpan.FromSeconds(5);

    private LibraryState _library = new();
    private IReadOnlyList<HiddenGame> _hiddenGames = []; // hidden games found by the last scan, with every id they go by
    private readonly List<FolderWatcher> _libraryWatchers = [];
    private DispatcherTimer? _libraryTimer;
    private bool _libraryActive, _libraryScanning, _libraryScanAgain, _libraryDeferred;
    private string _libraryStatus = "Not scanned yet.";

    private bool HasLibraryFence => _config.Fences.Any(fence => fence.Source.Kind == FenceSourceKind.Library);

    /// <summary>Starts the library when its fence appears, stops watching when it goes (EnsurePortals calls this).</summary>
    private void UpdateLibrary()
    {
        if (HasLibraryFence == _libraryActive) return;
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
    private void ScanLibrary()
    {
        if (!_libraryActive) return;
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
        var settings = _config.Library;
        var previous = _library;
        var dispatcher = Dispatcher.CurrentDispatcher;
        ShellWorker.RunAlone(() =>
        {
            try
            {
                void LogFailure(string what, Exception failure) => Log.Warning(failure, "game library: {What} could not be read", what);
                var scans = GameScanners.ScanAll(settings, LogFailure);
                var everything = GameCatalog.Merge(scans, previous.Items.Select(item => item.Game).ToList(), hidden: []);
                var hidden = settings.Hidden.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var games = everything.Where(game => !GameCatalog.IdsOf(game).Any(hidden.Contains)).ToList();
                var plan = LibraryFiles.Plan(previous.Items, games);
                var state = LibraryWriter.Apply(AppPaths.LibraryDirectory, plan,
                    logFailure: (file, failure) => Log.Warning(failure, "game library: {File} could not be written", file));
                var hiddenGames = everything.Where(game => GameCatalog.IdsOf(game).Any(hidden.Contains))
                    .Select(game => new HiddenGame(GameCatalog.IdsOf(game).First(hidden.Contains), game.Name, GameCatalog.IdsOf(game))).ToList();
                var unreadable = scans.Where(scan => !scan.Readable).Select(scan => scan.ScanKey).ToList();
                var watch = GameScanners.WatchFolders(settings);
                Log.Information("game library: {Count} games ({Written} written, {Removed} removed); unreadable: {Unreadable}",
                    state.Items.Count, plan.Write.Count, plan.Delete.Count, unreadable);
                dispatcher.BeginInvoke(() => OnLibraryScanned(state, hiddenGames, unreadable, watch));
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                Log.Warning(failure, "game library scan failed; the library keeps its games"); // hard rule 7
                dispatcher.BeginInvoke(() => OnLibraryScanned(null, null, ["library"], []));
            }
        }, name: "NeoFences game library");
    }

    private void OnLibraryScanned(LibraryState? state, IReadOnlyList<HiddenGame>? hiddenGames, IReadOnlyList<string> unreadable, IReadOnlyList<string> watch)
    {
        _libraryScanning = false;
        if (state is not null) _library = state;
        if (hiddenGames is not null) _hiddenGames = hiddenGames;
        _libraryStatus = $"Last scan {DateTime.Now:HH:mm}: {_library.Items.Count} games"
                         + (unreadable.Count > 0 ? $"; not readable right now: {string.Join(", ", unreadable)}" : ".");
        RearmLibraryWatchers(watch);
        foreach (var window in _windows.Values.Where(window => window.IsLibrary)) RefreshPortal(window); // new art, order
        RefreshSettings();
        if (!_libraryScanAgain) return;
        _libraryScanAgain = false;
        ScanLibrary();
    }

    /// <summary>Steam's and Epic's install lists and the game folders: a change rescans after 5 quiet seconds (downloads write a lot).</summary>
    private void RearmLibraryWatchers(IReadOnlyList<string> folders)
    {
        StopLibraryWatchers();
        if (!_libraryActive) return;
        var dispatcher = Dispatcher.CurrentDispatcher;
        foreach (var folder in folders)
        {
            var watcher = new FolderWatcher(folder, failure => Log.Warning(failure, "game library: cannot watch {Folder}", folder));
            watcher.Changed += () => dispatcher.BeginInvoke(ScheduleLibraryScan);
            _libraryWatchers.Add(watcher);
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
        foreach (var watcher in _libraryWatchers) watcher.Dispose();
        _libraryWatchers.Clear();
    }

    /// <summary>The library's shortcuts in catalog (A–Z) order; files NeoFences did not write (its index) are not shown.</summary>
    private IReadOnlyList<string> LibraryOrder(IReadOnlyList<ItemInfo> listed)
    {
        var position = _library.Items.Select((item, index) => (item.FileName, index)).ToDictionary(entry => entry.FileName, entry => entry.index, StringComparer.OrdinalIgnoreCase);
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
            if (item.Game.Poster is { } poster) art[itemRef] = (poster, true);
            else if (item.Game.IconPath is { } logo && Path.GetExtension(logo).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg") art[itemRef] = (logo, false);
        }
        return art;
    }

    private LibraryItem? LibraryItemOf(string itemRef) =>
        _library.Items.FirstOrDefault(item => string.Equals(item.FileName, Path.GetFileName(itemRef), StringComparison.OrdinalIgnoreCase));

    /// <summary>Tray / fence menu → "New Game Library fence": one at most; with one already, its tab is shown.</summary>
    private void CreateLibraryFence()
    {
        if (_config.Fences.FirstOrDefault(fence => fence.Source.Kind == FenceSourceKind.Library) is { } existing)
        {
            if (FenceTabs.HostOf(_config, existing.Id) is { } host && _windows.TryGetValue(host.Id, out var window)) SwitchTab(window, existing.Id);
            return;
        }
        var library = _config.Library;
        if (library.Folders.Count == 0 && Directory.Exists(@"D:\GameLibrary")) library = library with { Folders = [@"D:\GameLibrary"] }; // spec §4
        var fence = Fence.Create("Games", FenceSource.Library) with { Sort = FenceSort.Name, IconSize = 64, Labels = _config.Settings.DefaultLabels };
        _config = _config with { Fences = [.. _config.Fences, fence], Library = library };
        Log.Information("Game Library fence created");
        SyncBoxes();
        SaveNow();
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

    private void OpenInstallFolder(FenceWindow window, string itemRef)
    {
        if (LibraryItemOf(itemRef)?.Game.InstallFolder is { } folder && Directory.Exists(folder)) OpenItem(folder, ownerHandle: window.Handle);
        else Log.Information("game library: no install folder known for {ItemRef}", itemRef);
    }

    private void ShowLibraryItemMenu(FenceWindow window, IReadOnlyList<string> itemRefs, int screenX, int screenY, bool extended)
    {
        var choice = ShellItemMenu.Show(window.Handle, itemRefs, screenX, screenY, extended,
            logFailure: failure => Log.Warning(failure, "item menu or its command failed for {ItemRefs}", itemRefs),
            customCommands: ["Hide from library", "Open install folder"], canRename: false, out var custom);
        if (choice == ItemMenuChoice.Delete || (choice == ItemMenuChoice.Custom && custom == 0)) HideGames(itemRefs); // nothing goes to the Recycle Bin
        else if (choice == ItemMenuChoice.Custom && custom == 1) OpenInstallFolder(window, itemRefs[0]);
    }

    private LibraryView LibrarySettingsView() => new(
        HasFence: HasLibraryFence,
        Folders: _config.Library.Folders,
        Sources: _config.Library.Sources,
        Hidden: HiddenGamesForSettings(),
        Status: HasLibraryFence ? _libraryStatus : "No Game Library fence yet: tray → New Game Library fence.");

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
            var ids = (_hiddenGames.FirstOrDefault(game => game.AllIds.Contains(id, StringComparer.OrdinalIgnoreCase))?.AllIds ?? [id]).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Change(library => library with { Hidden = [.. library.Hidden.Where(hiddenId => !ids.Contains(hiddenId))] }, "show again");
        };
        window.RefreshLibraryRequested += ScanLibrary;
    }

    /// <summary>One row per hidden game (all its ids together); a hidden id no scan finds any more is listed by itself.</summary>
    private IReadOnlyList<HiddenGame> HiddenGamesForSettings()
    {
        var hidden = _config.Library.Hidden;
        var known = _hiddenGames.Where(game => game.AllIds.Any(id => hidden.Contains(id, StringComparer.OrdinalIgnoreCase))).ToList();
        var orphans = hidden.Where(id => !known.Any(game => game.AllIds.Contains(id, StringComparer.OrdinalIgnoreCase))).Select(id => new HiddenGame(id, id, [id]));
        return [.. known, .. orphans];
    }
}
```
`src/NeoFences.App/FenceHost.cs`:
```csharp
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using NeoFences.Core.Config;
using NeoFences.Core.Input;
using NeoFences.Core.Layouts;
using NeoFences.Core.Lifecycle;
using NeoFences.Core.Membership;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Owns the fences on the desktop: loads the config, places one <see cref="FenceWindow"/> per fence on the current
/// monitors, keeps fence contents in step with the Desktop folders (reconcile at start, then watcher events),
/// saves changes (debounced 500 ms, ADR-006), and keeps everything attached through display changes,
/// Explorer restarts and sign-out (ADR-011, ADR-013). Every Win32/COM call goes through NeoFences.Shell.
/// </summary>
public sealed partial class FenceHost
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ReattachInterval = TimeSpan.FromMilliseconds(500);
    private const double SnapGapDips = 8;        // spec §6: 8 px spacing from other fences and screen edges
    private const double SnapThresholdDips = 12; // how close an edge must come before it snaps
    private const int ReattachAttempts = 10;
    private const int PeekHotkeyId = 1;
    private const int PeekEscapeHotkeyId = 2;
    private static readonly TimeSpan DrawFrame = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan TrayRetryInterval = TimeSpan.FromSeconds(2);
    private const int TrayRetryAttempts = 15;

    private readonly ConfigStore _store = new(AppPaths.DataDirectory);
    private readonly Watchdog _watchdog = new(AppPaths.DataDirectory, message => Log.Information("watchdog: {Message}", message));
    private readonly SystemMessageWindow _messages = new();
    private readonly Dictionary<string, FenceWindow> _windows = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _saveTimer;
    private readonly IconLoader _iconLoader = new(Dispatcher.CurrentDispatcher);
    private DesktopWatcher? _desktopWatcher;
    private readonly ShellWorker _shellWorker = new(); // open, recycle, rename: off the UI thread, on STA (M3a review)
    private SpecialIconNotifications? _specialIcons;
    private bool _specialIconsDeferred;
    private readonly SnapshotStore _snapshots = new(Path.Combine(AppPaths.DataDirectory, "snapshots")); // M10
    private readonly HashSet<string> _loggedSnapshotProblems = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _specialIconsTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    // Rules wait until arrivals are quiet: a shortcut or file still being written reads wrong (M11 smoke X4, review M3).
    private readonly DispatcherTimer _filingTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private readonly List<string> _pendingArrivals = [];
    // Watcher trouble arrives in bursts: one re-arm and one reconcile per burst; a watcher that fails again at once waits longer (M8a review).
    private readonly DispatcherTimer _watcherRecoveryTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _rearmWatcher;
    private DateTime _lastWatcherRearm = DateTime.MinValue;
    private TimeSpan _watcherRearmDelay = WatcherBackoff.First;
    // Safe-save memory and expected drop arrivals (FenceMembership.SafeSaveWindow).
    private IReadOnlyList<RememberedPlacement> _rememberedPlacements = [];
    private readonly Dictionary<string, IDisposable> _dropRegistrations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PortalState> _portals = new(StringComparer.Ordinal); // M4: Portal fences by id
    private NeoFencesConfig _config = NeoFencesConfig.CreateDefault();
    private IReadOnlyList<MonitorPlacement> _monitors = [];
    private bool _takeoverActive;
    private bool _lightTheme = SystemTheme.AppsUseLightTheme();
    private bool _sessionEnding;
    // M5 desktop gestures
    private DesktopMouseHook? _mouseHook;
    private bool _quickHidden;              // double-click on the desktop: fences (and icons) hidden until the next one
    private bool _iconsHiddenByUser;        // RunState.IconsHiddenByUser: set when quick-hide begins, cleared when it ends
    private DrawFenceOverlay? _drawOverlay; // right-drag on the desktop: the fence being drawn
    private DispatcherTimer? _drawTimer;
    private (int X, int Y) _drawStart;
    private GlobalHotkey? _peekHotkey;
    private GlobalHotkey? _peekEscapeHotkey; // Esc ends Peek; registered only while peeking
    private bool _peeking;
    private bool _recordingHotkey;          // Settings' hotkey box has the keyboard: the Peek hotkey is released
    private string? _peekHotkeyProblem;     // why the Peek hotkey is not registered (Settings shows it), null when it is
    private bool _cornersLogged;
    private FenceWindow? _movingWindow; // the box being moved by its title (not resized): it may merge where it is dropped (M9)
    // M6a
    private bool _paused;                    // tray: Pause NeoFences (not saved)
    private bool _gameMode;                  // a full-screen app is in front: idle (spec §4.7, ADR-021)
    private ForegroundWatcher? _foregroundWatcher;
    private TrayIcon? _trayIcon;
    private readonly List<DesktopChange> _deferredDesktopChanges = [];
    private bool _reconcileDeferred;
    private const int MaxDeferredDesktopChanges = 500;
    private const int TrayNewFence = 1, TrayQuickHide = 2, TrayPeek = 3, TrayPause = 4, TrayExit = 5, TraySettings = 6;
    // Snapshots (M10): "Restore snapshot" lists the newest few by id TrayRestoreFirst + index.
    private const int TrayTakeSnapshot = 7, TrayRestoreMenu = 8, TrayRestoreBefore = 9, TrayRestoreFirst = 100, TrayRestoreCount = 10;
    private const int TrayNewLibrary = 11; // M12
    private SettingsWindow? _settingsWindow; // M6b: one at a time

    public event Action? ExitRequested;

    public FenceHost()
    {
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => SaveNow();
        _messages.ExplorerRestarted += OnExplorerRestarted;
        _messages.DisplayChanged += OnDisplayChanged;
        _messages.ThemeChanged += OnThemeChanged;
        _messages.HotkeyPressed += OnHotkey;
        _messages.TrayMenuRequested += ShowTrayMenu;
        _messages.SessionUnlocked += OnSessionUnlocked;
        _messages.SpecialIconsChanged += ScheduleSpecialIconRefresh;
        _messages.DeviceRemovalRequested += handle =>
        {
            foreach (var (fenceId, portal) in _portals)
            {
                if (portal.ReleaseForRemoval(handle)) Log.Information("drive of Portal {FenceId} is being removed: released it", fenceId);
            }
        };
        _specialIconsTimer.Tick += (_, _) => RefreshSpecialIcons();
        _filingTimer.Tick += (_, _) => FilePendingArrivals();
        _watcherRecoveryTimer.Tick += (_, _) => RecoverDesktopWatcher();
    }

    public void Start()
    {
        var loaded = _store.Load();
        Log.Information("config loaded from {Source} (read-only: {IsReadOnly}, corrupt copy: {CorruptCopyPath})",
            loaded.Source, loaded.IsReadOnly, loaded.CorruptCopyPath);
        _config = loaded.Config;
        _watchdog.LaunchDetached(Environment.ProcessId);
        ApplyStartup(); // after a power loss NeoFences must come back by itself (ADR-019)

        RefreshMonitors();
        foreach (var box in FenceTabs.Boxes(_config)) OpenWindow(box); // one window per box (M9)
        EnsurePortals();
        StartDesktopWatcher(); // first: an item created while the startup reconcile lists the desktop is not missed (M8a)
        ReconcileDesktop();
        StartSpecialIconNotifications();
        ApplyLayout();
        if (_config.Settings.Takeover) SetTakeover(true);
        else if (_watchdog.IsTakeoverActiveMarked)
        {
            // Icons may still be hidden from a run whose Takeover-off never got saved (both processes killed): show them.
            Log.Warning("takeover-active marker found while Takeover is off; showing desktop icons");
            SetIconsHidden(false);
        }
        StartGestures();
        if (!_messages.SessionNotificationsActive) Log.Warning("unlock notices unavailable: the mouse hook is re-installed only after an Explorer restart");
        try
        {
            _trayIcon = new TrayIcon(_messages.Handle, TrayTooltip(), log: message => Log.Warning("{Message}", message));
            ShowTrayIcon();
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Error(failure, "tray icon unavailable; fences and gestures keep working"); // hard rule 7: only the tray is lost
        }
        StartGameMode();
        ScheduleSave();
    }

    /// <summary>
    /// The clock for safe-save and drop memory: wall time at start plus a monotonic stopwatch, so a clock change (time
    /// sync, daylight saving, the user) cannot expire or extend a memory early (M8a).
    /// </summary>
    private static readonly DateTimeOffset ClockStart = DateTimeOffset.Now;
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static DateTimeOffset Now => ClockStart + Clock.Elapsed;

    /// <summary>All of NeoFences' run-time modes together (Core rules: which fences, icons and hooks they imply).</summary>
    private RunState Current => new(Takeover: _takeoverActive, QuickHidden: _quickHidden, Paused: _paused, GameMode: _gameMode,
        IconsHiddenByUser: _iconsHiddenByUser);

    /// <summary>
    /// Windows asked to end the session (WPF's SessionEnding, inside WM_QUERYENDSESSION). WPF then shuts the app
    /// down, which may be the last thing that ever runs, so the icons come back now and the watchdog is told it is
    /// a session end, not a user exit: if the user cancels the shutdown, the watchdog restarts NeoFences (ADR-013).
    /// </summary>
    public void OnSessionEnding()
    {
        _sessionEnding = true;
        ApplyDeferredShellWork(); // Desktop changes queued during a game must be saved too (M6a review M2)
        SaveNow();
        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) SetIconsHidden(false);
        TryMarker(() => _watchdog.MarkSessionEnding(Environment.ProcessId), what: "session-ending marker");
    }

    /// <summary>Orderly exit: save, bring icons back, tell the watchdog all is well.</summary>
    public void Shutdown()
    {
        ApplyDeferredShellWork(); // Desktop changes queued during a game must be saved too (M6a review M2)
        SaveNow();
        _trayIcon?.Dispose(); // also on session end: a cancelled shutdown restarts us, and the old icon would linger (M8a)
        _trayIcon = null;
        if (_sessionEnding) return; // OnSessionEnding already restored and marked; no clean marker, so a cancel restarts us
        // The marker also catches a show that failed earlier (quick-hide ending while Explorer was busy; M5 review M6).
        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) SetIconsHidden(false);
        TryMarker(() => _watchdog.MarkCleanShutdown(Environment.ProcessId), what: "clean-shutdown marker");
        _foregroundWatcher?.Dispose();
        _mouseHook?.Dispose();
        _peekHotkey?.Dispose();
        _peekEscapeHotkey?.Dispose();
        _desktopWatcher?.Dispose();
        _desktopWatcher = null;
        _specialIcons?.Dispose();
        StopLibraryWatchers();
        _libraryTimer?.Stop();
        _shellWorker.Dispose();
        _iconLoader.Dispose();
        _messages.Dispose();
    }

    /// <summary>Best effort from the crash handler; the watchdog restores too.</summary>
    public void EmergencyRestoreIcons()
    {
        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) DesktopIcons.TrySetHidden(false);
    }

    /// <summary>One window per box (M9): it shows the box's active tab; roll-up and lock are the box's.</summary>
    private void OpenWindow(Fence box)
    {
        var shown = FenceTabs.ActiveOf(_config, box.Id);
        var window = new FenceWindow(shown with { RolledUp = box.RolledUp, Locked = box.Locked }, takeoverActive: _takeoverActive,
            lightTheme: _lightTheme, iconLoader: _iconLoader, rollupExpand: _config.Settings.RollupExpand)
        {
            // Spec §6: no animations when Windows' "Animation effects" are off; spec §4.7: none while gaming.
            AnimationsAllowed = () => !_gameMode && SystemParameters.ClientAreaAnimation,
            BoxId = box.Id,
        };
        window.TabSelected += fenceId => SwitchTab(window, fenceId);
        window.TabDropped += (fenceId, screenX, screenY) => OnTabDropped(window, fenceId, screenX, screenY);
        window.TabDragMoved += (screenX, screenY) =>
        {
            foreach (var other in _windows.Values) other.SetMergeHighlight(other != window && other.TitleRowContains(screenX, screenY));
        };
        window.TabColorRequested += color =>
        {
            _config = FenceTabs.SetColor(_config, window.FenceId, color);
            RefreshTabs(window);
            ScheduleSave();
        };
        window.DetachTabRequested += () => DetachTab(window, window.FenceId, dropPoint: null);
        window.TabCycleRequested += step => CycleTab(window, step);
        window.SetTabs(FenceTabs.TabsOf(_config, box.Id), shown.Id);
        window.SnapRect = (rect, edges) => SnapFence(window, rect, edges);
        window.MovedByUser += OnFenceMoved;
        window.RenameRequested += title => RenameFence(window, title);
        window.IconSizeRequested += iconSize => SetFenceIconSize(window, iconSize);
        window.LockToggled += locked => SetFenceLocked(window, locked);
        window.DeleteRequested += () => DeleteFence(window);
        window.NewFenceRequested += CreateFence;
        window.TakeoverToggled += SetTakeover;
        window.ExitRequested += () => ExitRequested?.Invoke();
        window.OpenRequested += itemRef => OpenOrBrowse(window, itemRef);
        window.OpenManyRequested += itemRefs =>
        {
            foreach (var itemRef in itemRefs) OpenItem(itemRef, ownerHandle: window.Handle); // folders open in Explorer
            SetPeek(false);
        };
        window.ItemMenuRequested += (itemRefs, screenX, screenY, fromKeyboard) => ShowItemMenu(window, itemRefs, screenX, screenY, fromKeyboard);
        window.RecycleRequested += itemRefs => RecycleItems(window, itemRefs);
        window.ItemRenameRequested += (itemRef, newName) => RenameItem(window, itemRef, newName);
        window.BackRequested += () => BrowsePortal(window, back: true);
        window.NewPortalRequested += () => CreatePortal(window);
        window.NewLibraryRequested += CreateLibraryFence;
        window.RefreshLibraryRequested += ScanLibrary;
        window.StartupToggled += SetStartWithWindows;
        window.SettingsRequested += OpenSettings;
        window.RulesRequested += () => OpenRulesFor(window.FenceId);
        window.LabelModeRequested += labels => SetFenceLabels(window, labels);
        window.SetShortcutArrows(_config.Settings.ShowShortcutArrows);
        window.SetStartupChecked(_config.Settings.StartWithWindows);
        window.SortRequested += sort => SortFence(window, sort);
        window.OpenFolderRequested += () => { if (_portals.TryGetValue(window.FenceId, out var portal)) OpenItem(portal.Current, ownerHandle: window.Handle); };
        window.DragRequested += itemRefs =>
            ShellDragDrop.TryDrag(window.Handle, itemRefs, logFailure: failure => Log.Warning(failure, "could not start dragging {ItemRefs}", itemRefs),
                copyOnly: window.IsLibrary); // a game dragged out of the library is copied, never moved (M12)
        window.TakeoverPromptAnswered += AnswerTakeoverPrompt;
        window.RollUpToggled += () => ToggleRollUp(window);
        new WindowInteropHelper(window).EnsureHandle(); // HWND exists (styles, blur) before the first Show
        if (window.CornersUnavailable && !_cornersLogged)
        {
            _cornersLogged = true;
            Log.Information("Windows refused rounded corners (Windows 10?): fences keep square blur corners"); // M8a review
        }
        RegisterDrops(window); // needs the HWND
        if (!DesktopHost.AttachToDesktop(window.Handle)) Log.Warning("fence {FenceId}: not attached to the desktop yet (no Progman)", box.Id);
        _windows[box.Id] = window;
        // A Portal tab that gets a window of its own (detached, or its host deleted) lists its folder now (final review I1).
        if (_portals.TryGetValue(shown.Id, out var shownPortal)) shownPortal.Refresh();
    }

    /// <summary>Full pass over the Desktop folders: at start and whenever watcher events were lost.</summary>
    private void ReconcileDesktop()
    {
        var listing = DesktopItems.Enumerate();
        var (reconciled, report) = FenceMembership.Reconcile(_config, listing.ItemRefs, listing.UnavailableFolders,
            remembered: _rememberedPlacements, now: Now);
        _config = reconciled;
        _rememberedPlacements = [.. _rememberedPlacements.Except(report.UsedMemories)]; // each memory places one item once (M8a review)
        if (listing.UnavailableFolders.Count > 0) Log.Warning("desktop folders not readable: {Folders}", listing.UnavailableFolders);
        if (report.Suspicious) Log.Warning("kept fenced items from an unreadable or empty desktop listing until a later reconcile");
        Log.Information("desktop reconciled: {AddedCount} added to the Inbox, {RemovedCount} removed", report.AddedToInbox.Count, report.Removed.Count);
        RefreshWindows();
        ScheduleSave();
        FileNewItems(report.AddedToInbox);
    }

    private void StartDesktopWatcher()
    {
        // A folder that cannot be watched degrades to "its changes show after a restart" (hard rule 7).
        _desktopWatcher = new DesktopWatcher((folder, failure) => Log.Error(failure, "cannot watch {Folder}; its changes show after a restart", folder));
        var dispatcher = Dispatcher.CurrentDispatcher;
        _desktopWatcher.Changed += change => dispatcher.BeginInvoke(() => OnDesktopChanged(change));
        _desktopWatcher.Overflowed += () => dispatcher.BeginInvoke(() => OnDesktopWatcherTrouble(rearm: true));
        _desktopWatcher.ReconcileNeeded += () => dispatcher.BeginInvoke(() => OnDesktopWatcherTrouble(rearm: false));
    }

    /// <summary>
    /// Events were lost or the watcher stopped (re-arm: .NET disables it after a non-overflow error), or one event could
    /// not be read (reconcile only). Bursts are gathered into one recovery (M8a review).
    /// </summary>
    private void OnDesktopWatcherTrouble(bool rearm)
    {
        if (_desktopWatcher is null) return; // shut down meanwhile
        if (rearm && !_rearmWatcher)
        {
            _rearmWatcher = true;
            // Measured when the failure arrives: the wait itself is not quiet time (Core WatcherBackoff, M8c review I3).
            _watcherRearmDelay = WatcherBackoff.Next(_watcherRearmDelay, lastRearm: _lastWatcherRearm, failureAt: DateTime.UtcNow);
            _watcherRecoveryTimer.Stop(); // a reconcile-only recovery already waiting now waits for the re-arm delay
            _watcherRecoveryTimer.Interval = _watcherRearmDelay;
            _watcherRecoveryTimer.Start();
            return;
        }
        if (_watcherRecoveryTimer.IsEnabled) return;
        _watcherRecoveryTimer.Interval = WatcherBackoff.First;
        _watcherRecoveryTimer.Start();
    }

    private void RecoverDesktopWatcher()
    {
        _watcherRecoveryTimer.Stop();
        if (_desktopWatcher is null) return;
        if (_rearmWatcher)
        {
            _rearmWatcher = false;
            _lastWatcherRearm = DateTime.UtcNow;
            Log.Warning("desktop watcher lost events or stopped; re-armed after {Delay} and reconciling", _watcherRearmDelay);
            _desktopWatcher.Dispose();
            StartDesktopWatcher();
        }
        else
        {
            Log.Information("a desktop change could not be read; reconciling");
        }
        if (Current.ShellWorkDeferred) _reconcileDeferred = true; // after the game
        else ReconcileDesktop();
    }

    /// <summary>Also after an Explorer restart: Explorer brokers the shell's change notices and forgets them (M8c review I6).</summary>
    private void StartSpecialIconNotifications()
    {
        _specialIcons?.Dispose();
        var dispatcher = Dispatcher.CurrentDispatcher;
        _specialIcons = new SpecialIconNotifications(_messages.Handle,
            settingsChanged: () => dispatcher.BeginInvoke(ScheduleSpecialIconRefresh),
            log: (what, failure) => Log.Warning(failure, "{What} unavailable: special icons change after a restart", what));
    }

    private void ScheduleSpecialIconRefresh()
    {
        _specialIconsTimer.Stop(); // a burst (several icons, a Recycle Bin emptying) is one refresh
        _specialIconsTimer.Start();
    }

    /// <summary>"Desktop icon settings" changed (reconcile), or the Recycle Bin turned full or empty (new icon) (M8c).</summary>
    private void RefreshSpecialIcons()
    {
        _specialIconsTimer.Stop();
        if (Current.ShellWorkDeferred)
        {
            _specialIconsDeferred = true; // games get every bit of the machine: after the game (M8c review)
            return;
        }
        var shown = DesktopItems.SpecialIconRefs().ToHashSet(ItemRef.Comparer);
        var fenced = _config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop).SelectMany(fence => fence.Items)
            .Where(itemRef => itemRef.StartsWith("::", StringComparison.Ordinal)).ToHashSet(ItemRef.Comparer);
        if (!shown.SetEquals(fenced))
        {
            Log.Information("desktop icon settings changed; reconciling");
            if (Current.ShellWorkDeferred) _reconcileDeferred = true;
            else ReconcileDesktop();
        }
        foreach (var window in _windows.Values) window.ReloadSpecialIcons();
        Log.Information("special icons refreshed");
    }

    private void OnDesktopChanged(DesktopChange change)
    {
        if (Current.ShellWorkDeferred)
        {
            // Applied in order when the game is left; a flood (a big download unpacking) becomes one reconcile instead (M8a).
            if (_deferredDesktopChanges.Count < MaxDeferredDesktopChanges) _deferredDesktopChanges.Add(change);
            else _reconcileDeferred = true;
            return;
        }
        var arrival = ArrivalOf(change); // before Apply: was the item fenced already?
        (_config, _rememberedPlacements) = FenceMembership.Apply(_config, change, _rememberedPlacements, Now);
        RefreshWindows();
        ScheduleSave();
        if (arrival is not null) FileNewItems([arrival]);
    }

    /// <summary>
    /// The item a change brings that rules may file (M11): a created item no fence holds yet (an attribute change on an
    /// existing item also arrives as "created", final review I1), or a download that just got its final name (I2).
    /// </summary>
    private string? ArrivalOf(DesktopChange change) => change switch
    {
        DesktopChange.Created created when !_config.Fences.Any(fence => fence.Items.Contains(created.ItemRef, ItemRef.Comparer)) => created.ItemRef,
        DesktopChange.Renamed renamed when Rules.IsDownloadRename(renamed.OldRef) => renamed.NewRef,
        _ => null,
    };

    /// <summary>
    /// Rules auto-sort (M11, spec §3): new items that landed in the Inbox go to the fence of the first rule they match.
    /// Their facts are read on the shell worker (a shortcut to an offline share can be slow); an item moved or deleted
    /// meanwhile stays put. Membership only: no file is touched.
    /// </summary>
    private void FileNewItems(IReadOnlyList<string> itemRefs)
    {
        if (itemRefs.Count == 0 || !_config.Rules.Any(rule => rule.Enabled)) return;
        _pendingArrivals.AddRange(itemRefs);
        _filingTimer.Stop(); // a burst (an unpack, an installer) is one read, 1.5 s after the last arrival
        _filingTimer.Start();
    }

    private void FilePendingArrivals()
    {
        _filingTimer.Stop();
        var inbox = _config.Inbox.Items.ToHashSet(ItemRef.Comparer);
        var arrivals = _pendingArrivals.Distinct(ItemRef.Comparer).Where(inbox.Contains).ToList(); // a safe-save memory or a drop placed it already
        _pendingArrivals.Clear();
        if (arrivals.Count == 0) return;
        ReadFactsThen(arrivals, facts =>
        {
            var stillInInbox = _config.Inbox.Items.ToHashSet(ItemRef.Comparer);
            var filed = Rules.File(_config, [.. facts.Where(fact => stillInInbox.Contains(fact.ItemRef))], DateTimeOffset.Now);
            if (ReferenceEquals(filed, _config)) return;
            var moved = _config.Inbox.Items.Except(filed.Inbox.Items, ItemRef.Comparer).ToList();
            Log.Information("rules filed {Count} new item(s): {ItemRefs}", moved.Count, moved);
            _config = filed;
            RefreshWindows();
            ScheduleSave();
        });
    }

    /// <summary>Reads item facts on the shell worker, then continues on the UI thread.</summary>
    private void ReadFactsThen(IReadOnlyList<string> itemRefs, Action<IReadOnlyList<ItemFacts>> then)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        _shellWorker.Run(() =>
        {
            var facts = ItemFactsReader.Read(itemRefs, logFailure: (itemRef, failure) => Log.Warning(failure, "rules: {ItemRef} could not be read", itemRef));
            dispatcher.BeginInvoke(() => then(facts));
        });
    }

    /// <summary>
    /// Settings → "Apply rules now" (M11, spec §3): every desktop item, hand-placed ones too. "Before restore" is written
    /// first, so tray → "Undo the last restore" puts everything back; nothing moves without it.
    /// </summary>
    private void ApplyRulesNow()
    {
        var itemRefs = _config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop).SelectMany(fence => fence.Items).ToList();
        ReadFactsThen(itemRefs, facts =>
        {
            var now = DateTimeOffset.Now;
            var moves = Rules.CountMoves(_config, facts, now);
            if (moves == 0)
            {
                _settingsWindow?.ShowRulesResult("Nothing to move: every item is where the rules want it.");
                return;
            }
            if (_snapshots.Save(Snapshots.Take(_config, name: $"Before applying rules ({now:d MMM HH:mm})", now: now), SnapshotStore.BeforeRestoreFileName) is null)
            {
                Log.Warning(_snapshots.LastFailure, "rules not applied: 'Before restore' could not be saved");
                _settingsWindow?.ShowRulesResult("Nothing moved: NeoFences could not save 'Before restore' first (see the log).");
                return;
            }
            _config = Rules.File(_config, facts, now);
            Log.Information("rules applied: {Count} item(s) moved", moves);
            SaveNow();
            RefreshWindows();
            RefreshSettings();
            var message = $"{moves} item{(moves == 1 ? "" : "s")} moved. Tray → Restore snapshot → Undo the last restore puts them back.";
            _settingsWindow?.ShowRulesResult(message);
            _trayIcon?.ShowBalloon("Rules applied", message);
        });
    }

    private void RefreshWindows()
    {
        var showPrompt = !_config.Settings.TakeoverPromptAnswered && !_takeoverActive;
        foreach (var window in _windows.Values)
        {
            if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is not { } shown) continue;
            if (!_portals.ContainsKey(shown.Id)) window.SetItems(shown.Items); // Portals refresh on their own (M4 review I1)
            window.ShowTakeoverPrompt(showPrompt && shown.IsInbox);
        }
    }

    private void OpenItem(string itemRef, nint ownerHandle)
    {
        // Off the UI thread: ShellExecute can block on a network timeout or a UAC prompt, freezing every fence (M2b review I6);
        // on an STA thread, as shell handlers and Windows' error dialog expect (M3a review).
        // Each open on its own thread: one waiting on an offline share (and its error dialog) holds up nothing else (M8c review I4).
        ShellWorker.RunAlone(() =>
        {
            if (!ShellItems.TryOpen(itemRef, ownerHandle)) Log.Warning("could not open {ItemRef}", itemRef);
        }, name: "NeoFences open");
    }

    /// <summary>Makes the fence accept drops (M3b): fence items and Desktop files move membership; other files go to Windows.</summary>
    private void RegisterDrops(FenceWindow window)
    {
        try
        {
            _dropRegistrations[window.BoxId] = ShellDragDrop.RegisterFence(window.Handle, new FenceDropHandlers(
                HitTest: window.HitTest,
                MoveItems: (itemRefs, insertAt) =>
                {
                    _config = FenceMembership.MoveItems(_config, itemRefs, window.FenceId, insertAt);
                    RefreshWindows();
                    ScheduleSave();
                },
                ExpectArrivals: (itemRefs, insertAt) =>
                    _rememberedPlacements = FenceMembership.ExpectArrivals(_rememberedPlacements, itemRefs, window.FenceId, insertAt, Now),
                Recycle: itemRefs => RecycleItems(window, itemRefs),
                ShowFeedback: window.ShowDropFeedback,
                LogFailure: failure => Log.Warning(failure, "drop on fence {FenceId} failed", window.FenceId),
                AcceptsDrops: () => !window.IsLibrary), // the library shows NeoFences' own shortcuts only (M12)
                portalFolder: () => _portals.TryGetValue(window.FenceId, out var portal) ? portal.Current : null);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Error(failure, "fence {FenceId} cannot accept drops", window.FenceId); // degrade: everything else still works
        }
    }

    private void ShowItemMenu(FenceWindow window, IReadOnlyList<string> itemRefs, int screenX, int screenY, bool fromKeyboard)
    {
        // Shift+right-click is the extended menu; Shift+F10 is just the keyboard's normal menu (M3a review).
        var extended = !fromKeyboard && System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);
        if (window.IsLibrary)
        {
            ShowLibraryItemMenu(window, itemRefs, screenX, screenY, extended);
            return;
        }
        var choice = ShellItemMenu.Show(window.Handle, itemRefs, screenX, screenY, extended,
            logFailure: failure => Log.Warning(failure, "item menu or its command failed for {ItemRefs}", itemRefs));
        switch (choice)
        {
            case ItemMenuChoice.Rename:
                window.BeginItemRename(itemRefs[0]); // the right-clicked item (it comes first)
                break;
            case ItemMenuChoice.Delete:
                RecycleItems(window, itemRefs); // always the Recycle Bin, even with Shift held (hard rule 1)
                break;
        }
    }

    /// <summary>
    /// Windows moves them to the Recycle Bin (with its own dialogs) on the shell worker, so a long recycle never freezes
    /// the fences (M3a review); the watcher then removes them from the fence.
    /// </summary>
    private void RecycleItems(FenceWindow window, IReadOnlyList<string> itemRefs)
    {
        if (window.IsLibrary)
        {
            HideGames(itemRefs); // Delete in the library hides the game; its shortcut is NeoFences' own (M12)
            return;
        }
        Log.Information("recycling {Count} item(s)", itemRefs.Count);
        var ownerHandle = window.Handle;
        var dispatcher = Dispatcher.CurrentDispatcher;
        _shellWorker.Run(() =>
        {
            var (started, refused, missing) = ShellFileOps.TryRecycle(LiveOwner(ownerHandle), itemRefs);
            if (!started) Log.Warning("recycle did not run or was cancelled: {ItemRefs}", itemRefs);
            if (missing.Count > 0) Log.Information("skipped {Count} item(s) that no longer exist: {ItemRefs}", missing.Count, missing);
            if (refused.Count > 0) dispatcher.BeginInvoke(() => ExplainRefusedRecycle(window, refused));
        });
    }

    /// <summary>No owner when the fence was deleted while the operation waited: Windows' dialogs then stand alone (M8c review).</summary>
    private static nint LiveOwner(nint ownerHandle) => FenceWindowChrome.IsLiveWindow(ownerHandle) ? ownerHandle : 0;

    private static void ExplainRefusedRecycle(FenceWindow window, IReadOnlyList<string> refused)
    {
        Log.Information("not deleting {Count} item(s) on drives without a Recycle Bin", refused.Count);
        System.Windows.MessageBox.Show(window,
            (refused.Count == 1 ? $"\"{Path.GetFileName(refused[0])}\" is" : $"{refused.Count} items are") +
            " on a drive without a Recycle Bin (a USB stick or network drive), so NeoFences won't delete it." +
            " Deleting there would be permanent; if you really mean it, delete it in Explorer.",
            "NeoFences", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    /// <summary>Windows renames the file (on the shell worker: its conflict dialogs); the watcher keeps it in its fence and position.</summary>
    private void RenameItem(FenceWindow window, string itemRef, string newName)
    {
        var ownerHandle = window.Handle;
        _shellWorker.Run(() =>
        {
            if (!ShellFileOps.TryRename(LiveOwner(ownerHandle), itemRef, newName)) Log.Warning("rename did not run or was cancelled: {ItemRef}", itemRef);
        });
    }

    private void AnswerTakeoverPrompt(bool hideIcons)
    {
        Log.Information("first-run question answered: hide desktop icons {HideIcons}", hideIcons);
        if (hideIcons) SetTakeover(true);
        else
        {
            _config = _config with { Settings = _config.Settings with { TakeoverPromptAnswered = true } };
            RefreshWindows();
            SaveNow();
        }
    }

    private void ApplyLayout()
    {
        if (_monitors.Count == 0)
        {
            Log.Warning("no monitors reported; keeping the current layout");
            return;
        }
        try
        {
            var (resolved, layout) = LayoutEngine.Resolve(_config, _monitors.Select(monitor => monitor.ToDisplayMonitor()).ToList());
            _config = resolved;
            foreach (var (fenceId, rect) in layout.Fences)
            {
                if (!_windows.TryGetValue(fenceId, out var window)) continue;
                var monitor = _monitors.First(candidate => candidate.DeviceId == rect.Monitor);
                window.Place(FencePlacement.ToPixels(rect, monitor));
                if (!window.IsVisible && Current.FencesVisible)
                {
                    window.ShowNow();
                    if (_peeking)
                    {
                        // A fence made during Peek (its menus no longer end it, M5 review I1) joins the others on top.
                        window.Peeking = true;
                        FenceWindowChrome.SetTopmost(window.Handle, topmost: true);
                    }
                    else FenceWindowChrome.SendToBack(window.Handle); // Show puts it above every app; fences live just above the desktop
                }
            }
        }
        catch (ArgumentException unusableDisplay)
        {
            // Garbage from a monitor query mid-change (M1 review): skip this resolve, the next display event retries.
            Log.Warning(unusableDisplay, "display query unusable; keeping the current layout");
        }
    }

    private void RefreshMonitors()
    {
        _monitors = Monitors.Enumerate();
        Log.Information("monitors: {Monitors}", string.Join("; ", _monitors.Select(monitor =>
            $"{monitor.DeviceId} {monitor.PixelWidth}x{monitor.PixelHeight}@{monitor.ScalePercent}% " +
            $"work {monitor.WorkLeftPx},{monitor.WorkTopPx} {monitor.WorkWidthPx}x{monitor.WorkHeightPx}{(monitor.IsPrimary ? " primary" : "")}")));
    }

    private void OnFenceMoved(FenceWindow window, PixelRect pixels)
    {
        var wasMove = ReferenceEquals(_movingWindow, window);
        _movingWindow = null;
        foreach (var other in _windows.Values) other.SetMergeHighlight(false);
        // Dropped by its title onto another fence's title row: the whole box joins that box as tabs (M9).
        var (cursorX, cursorY) = FenceWindowChrome.GetCursorPosition();
        if (wasMove && _windows.Values.FirstOrDefault(other => other != window && other.TitleRowContains(cursorX, cursorY)) is { } target)
        {
            Log.Information("fence {FenceId} merged into the box of {TargetId}", window.BoxId, target.BoxId);
            _config = FenceTabs.Merge(_config, movingFenceId: window.BoxId, targetFenceId: target.BoxId);
            SyncBoxes();
            return;
        }
        if (_monitors.Count == 0 || _config.LastLayoutFingerprint is not { } fingerprint) return;
        var monitor = FencePlacement.ContainingMonitor(pixels, _monitors);
        _config = LayoutEngine.WithFenceRect(_config, fingerprint: fingerprint, fenceId: window.BoxId, rect: FencePlacement.FromPixels(pixels, monitor));
        ScheduleSave();
    }

    private PixelRect SnapFence(FenceWindow window, PixelRect rect, SnapEdges edges)
    {
        // While a box moves, the title row it would merge into lights up; a resize never merges (M9).
        _movingWindow = edges == SnapEdges.Move ? window : null;
        if (_movingWindow is not null)
        {
            var (cursorX, cursorY) = FenceWindowChrome.GetCursorPosition();
            foreach (var other in _windows.Values) other.SetMergeHighlight(other != window && other.TitleRowContains(cursorX, cursorY));
        }
        if (_monitors.Count == 0) return rect;
        var monitor = FencePlacement.ContainingMonitor(rect, _monitors);
        var others = _windows.Values.Where(other => other != window && other.IsVisible).Select(other => FenceWindowChrome.GetPixelRect(other.Handle)).ToList();
        return Snapping.Snap(rect, edges,
            workArea: new PixelRect(monitor.WorkLeftPx, monitor.WorkTopPx, monitor.WorkWidthPx, monitor.WorkHeightPx),
            others: others,
            gapPx: (int)Math.Round(SnapGapDips * monitor.Scale),
            thresholdPx: (int)Math.Round(SnapThresholdDips * monitor.Scale),
            // A resize snap never makes the fence smaller than its minimum (M2c review carry-over).
            minWidthPx: (int)Math.Round(LayoutEngine.MinWidth * monitor.Scale),
            minHeightPx: (int)Math.Round(LayoutEngine.MinHeight * monitor.Scale));
    }

    private void RenameFence(FenceWindow window, string title)
    {
        _config = FenceEdits.Rename(_config, window.FenceId, title);
        window.SetTitle(_config.Fences.First(fence => fence.Id == window.FenceId).Title);
        RefreshTabs(window);
        RefreshPortal(window); // a Portal browsing a subfolder shows its breadcrumb again
        ScheduleSave();
    }

    private void SetFenceIconSize(FenceWindow window, int iconSize)
    {
        _config = FenceEdits.SetIconSize(_config, window.FenceId, iconSize);
        window.SetIconSize(iconSize);
        ScheduleSave();
    }

    private void SetFenceLocked(FenceWindow window, bool locked)
    {
        _config = FenceEdits.SetLocked(_config, window.BoxId, locked); // the box's (M9)
        window.SetLocked(locked);
        ScheduleSave();
    }

    /// <summary>Removes the fence; its items go to the Inbox. Files are never touched (hard rule 1).</summary>
    private void DeleteFence(FenceWindow window)
    {
        _config = FenceMembership.DeleteFence(_config, window.FenceId); // the shown tab; the rest of its box stays (M9)
        SyncBoxes(); // closes the window when its box is gone; a deleted Portal's watcher ends (the folder is never touched)
    }

    private void OnThemeChanged()
    {
        var light = SystemTheme.AppsUseLightTheme();
        if (light == _lightTheme) return;
        _lightTheme = light;
        Log.Information("Windows app mode changed; light: {Light}", light);
        foreach (var window in _windows.Values) window.ApplyTheme(light);
    }

    private void OnDisplayChanged()
    {
        RefreshMonitors();
        ApplyLayout();
        ScheduleSave();
    }

    /// <summary>"New Portal fence…": Windows' folder dialog, then a fence that mirrors the folder (M4).</summary>
    private void CreatePortal(FenceWindow owner)
    {
        var folder = FolderPicker.TryPick(owner.Handle, "Choose the folder for the new Portal fence",
            logFailure: failure => Log.Warning(failure, "folder dialog failed"));
        if (folder is null) return;
        var title = Path.GetFileName(folder.TrimEnd('\\')) is { Length: > 0 } name ? name : folder;
        (_config, _) = FenceMembership.CreatePortal(_config, title: title, folderPath: folder);
        Log.Information("Portal fence created for {Folder}", folder);
        SyncBoxes();
    }

    /// <summary>
    /// Brings the windows in line with the boxes after any change to them (M9): a window per box, showing the box's
    /// active tab, with its tab strip, lock and roll-up. A box that changed hands (its host left) keeps its window.
    /// </summary>
    private void SyncBoxes()
    {
        var boxes = FenceTabs.Boxes(_config);
        var boxIds = boxes.Select(box => box.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var (boxId, window) in _windows.ToList())
        {
            if (boxIds.Contains(boxId)) continue;
            _windows.Remove(boxId);
            if (_config.Fences.Any(fence => fence.Id == window.FenceId) && FenceTabs.HostOf(_config, window.FenceId) is { } heir
                && boxIds.Contains(heir.Id) && !_windows.ContainsKey(heir.Id))
            {
                window.BoxId = heir.Id;
                _windows[heir.Id] = window;
                if (_dropRegistrations.Remove(boxId, out var moved)) _dropRegistrations[heir.Id] = moved;
                continue;
            }
            if (_dropRegistrations.Remove(boxId, out var registration)) registration.Dispose();
            window.Close();
        }
        foreach (var box in boxes.Where(box => !_windows.ContainsKey(box.Id))) OpenWindow(box);
        EnsurePortals();
        foreach (var box in boxes)
        {
            var window = _windows[box.Id];
            var active = FenceTabs.ActiveOf(_config, box.Id);
            if (window.FenceId != active.Id)
            {
                window.ShowTab(active);
                if (_portals.TryGetValue(active.Id, out var portal)) portal.Refresh();
            }
            window.SetTabs(FenceTabs.TabsOf(_config, box.Id), active.Id);
            window.SetLocked(box.Locked);
            window.SetRolledUp(box.RolledUp);
        }
        RefreshWindows();
        ApplyLayout();
        ScheduleSave();
    }

    /// <summary>A watcher per Portal fence, shown or not (a hidden Portal tab keeps watching, M9); gone ones end.</summary>
    private void EnsurePortals()
    {
        var portalFences = _config.Fences.Where(fence => fence.Source is { Kind: FenceSourceKind.Portal, Path: not null } or { Kind: FenceSourceKind.Library }).ToList();
        foreach (var goneId in _portals.Keys.Where(fenceId => portalFences.All(fence => fence.Id != fenceId)).ToList())
        {
            _portals[goneId].Dispose(); // the folder itself is never touched
            _portals.Remove(goneId);
        }
        foreach (var fence in portalFences.Where(fence => !_portals.ContainsKey(fence.Id)))
        {
            var fenceId = fence.Id;
            var root = fence.Source.Kind == FenceSourceKind.Library ? AppPaths.LibraryDirectory : fence.Source.Path!;
            if (fence.Source.Kind == FenceSourceKind.Library) TryCreateFolder(root);
            _portals[fenceId] = new PortalState(root, noticeOwner: _messages.Handle, show: items => ShowPortalTab(fenceId, items),
                logFailure: failure => Log.Warning(failure, "cannot watch Portal folder of {FenceId}", fenceId));
            if (_gameMode) _portals[fenceId].SetPaused(true);
        }
        UpdateLibrary(); // M12: the library scans while its fence exists
    }

    /// <summary>A Portal's listing goes to the window showing it; a hidden Portal tab re-lists when shown.</summary>
    private void ShowPortalTab(string fenceId, IReadOnlyList<ItemInfo>? items)
    {
        if (_windows.Values.FirstOrDefault(window => window.FenceId == fenceId) is { } window) ShowPortal(window, items);
    }

    private void RefreshTabs(FenceWindow window) => window.SetTabs(FenceTabs.TabsOf(_config, window.BoxId), window.FenceId);

    /// <summary>A header clicked (or hovered during a drop): that tab shows, now and after a restart.</summary>
    private void SwitchTab(FenceWindow window, string fenceId)
    {
        if (window.FenceId == fenceId || _config.Fences.FirstOrDefault(fence => fence.Id == fenceId) is not { } tab) return;
        _config = FenceTabs.SetActive(_config, fenceId);
        window.ShowTab(tab);
        RefreshTabs(window);
        if (_portals.TryGetValue(fenceId, out var portal)) portal.Refresh();
        RefreshWindows();
        ScheduleSave();
    }

    private void CycleTab(FenceWindow window, int step)
    {
        var tabs = FenceTabs.TabsOf(_config, window.BoxId);
        var index = tabs.ToList().FindIndex(tab => tab.Id == window.FenceId);
        SwitchTab(window, tabs[((index + step) % tabs.Count + tabs.Count) % tabs.Count].Id);
    }

    /// <summary>A tab header released after a drag: on its own strip = reorder; on another title row = merge; elsewhere = detach.</summary>
    private void OnTabDropped(FenceWindow window, string fenceId, int screenX, int screenY)
    {
        var target = _windows.Values.FirstOrDefault(candidate => candidate.TitleRowContains(screenX, screenY));
        var own = FenceWindowChrome.GetPixelRect(window.Handle);
        if (target is null && screenX >= own.X && screenX < own.X + own.Width && screenY >= own.Y && screenY < own.Y + own.Height)
        {
            return; // released over its own box: nothing happens (not a detach on top of itself; final review)
        }
        if (target == window)
        {
            var slot = window.TabIndexAt(screenX);
            var current = FenceTabs.TabsOf(_config, window.BoxId).ToList().FindIndex(tab => tab.Id == fenceId);
            _config = FenceTabs.Reorder(_config, fenceId, slot > current ? slot - 1 : slot);
        }
        else if (target is not null)
        {
            Log.Information("tab {FenceId} moved into the box of {TargetId}", fenceId, target.BoxId);
            // One tab moves, also when it is its box's host (final review C1).
            _config = FenceTabs.Merge(_config, movingFenceId: fenceId, targetFenceId: target.BoxId, insertAt: target.TabIndexAt(screenX), wholeBox: false);
        }
        else
        {
            DetachTab(window, fenceId, dropPoint: (screenX, screenY));
            return;
        }
        SyncBoxes();
    }

    /// <summary>A tab leaves its box: at the drop point (dragged out) or offset 40 DIP down-right of the box (menu), with the box's size.</summary>
    private void DetachTab(FenceWindow window, string fenceId, (int X, int Y)? dropPoint)
    {
        if (_monitors.Count == 0 || _config.LastLayoutFingerprint is not { } fingerprint
            || !_config.Layouts.TryGetValue(fingerprint, out var layout) || !layout.Fences.TryGetValue(window.BoxId, out var boxRect))
        {
            Log.Warning("tab {FenceId} not detached: its box has no place in the current layout", fenceId);
            return;
        }
        var boxMonitor = _monitors.FirstOrDefault(monitor => monitor.DeviceId == boxRect.Monitor) ?? _monitors[0];
        var box = FencePlacement.ToPixels(boxRect, boxMonitor);
        var offset = (int)Math.Round(40 * boxMonitor.Scale);
        var placed = dropPoint is { } point
            ? box with { X = point.X - offset, Y = point.Y - offset / 3 } // the pointer near its title
            : box with { X = box.X + offset, Y = box.Y + offset };
        var monitor = FencePlacement.ContainingMonitor(placed, _monitors);
        Log.Information("tab {FenceId} detached from the box of {BoxId}", fenceId, window.BoxId);
        _config = FenceTabs.Detach(_config, fenceId, fingerprint, FencePlacement.FromPixels(placed, monitor));
        SyncBoxes();
    }

    /// <summary>Asks a Portal to re-list its folder (in the background; ShowPortal follows).</summary>
    private void RefreshPortal(FenceWindow window)
    {
        if (_portals.TryGetValue(window.FenceId, out var portal)) portal.Refresh();
    }

    /// <summary>Shows a Portal's listing (null: the folder cannot be read) in its sort order (M4).</summary>
    private void ShowPortal(FenceWindow window, IReadOnlyList<ItemInfo>? items)
    {
        if (!_portals.TryGetValue(window.FenceId, out var portal)) return;
        if (_config.Fences.FirstOrDefault(candidate => candidate.Id == window.FenceId) is not { } fence) return;
        window.SetPortalLocation(portal.Breadcrumb(fence.Title), portal.CanGoBack);
        window.SetSortChecked(fence.Sort);
        if (fence.Source.Kind == FenceSourceKind.Library)
        {
            window.ShowPortalMessage(null);
            window.SetLibraryArt(LibraryArt());
            window.SetItems(items is null ? [] : LibraryOrder(items)); // A–Z by game, only NeoFences' own shortcuts (M12)
            return;
        }
        window.ShowPortalMessage(items is null ? $"This folder is not available right now:\n{portal.Current}" : null);
        window.SetItems(items is null ? [] : ItemSorting.Order(items, fence.Sort));
    }

    /// <summary>Double-click / Enter: inside a Portal a folder is browsed in place (Ctrl opens it in Explorer, user choice 2026-10-03).</summary>
    private void OpenOrBrowse(FenceWindow window, string itemRef)
    {
        var inExplorer = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control);
        if (_portals.TryGetValue(window.FenceId, out var portal) && !inExplorer && portal.IsListedFolder(itemRef))
        {
            portal.Browse(itemRef); // re-lists in the background
            return;
        }
        OpenItem(itemRef, ownerHandle: window.Handle);
        SetPeek(false); // like Fences: Peek ends once something is opened from it
    }

    private void BrowsePortal(FenceWindow window, bool back)
    {
        if (!back || !_portals.TryGetValue(window.FenceId, out var portal) || !portal.CanGoBack) return;
        portal.Back(); // re-lists in the background
    }

    /// <summary>"Sort by": a Portal keeps the order live; a desktop fence is sorted once (M4).</summary>
    private void SortFence(FenceWindow window, FenceSort sort)
    {
        var fence = _config.Fences.First(candidate => candidate.Id == window.FenceId);
        if (_portals.ContainsKey(fence.Id))
        {
            _config = FenceEdits.SetSort(_config, fence.Id, sort);
            RefreshPortal(window);
        }
        else
        {
            try
            {
                _config = FenceEdits.SetItemOrder(_config, fence.Id, ItemSorting.Order(FolderItems.Describe(fence.Items), sort));
            }
            catch (ArgumentException mismatch)
            {
                Log.Warning(mismatch, "sort of fence {FenceId} refused: the sorted list did not match its items", fence.Id); // never crash (M4 review I3)
                return;
            }
            RefreshWindows();
        }
        ScheduleSave();
    }

    private void ApplyStartup() =>
        StartupRegistration.Apply(_config.Settings.StartWithWindows, Environment.ProcessPath ?? "", log: message => Log.Information("{Message}", message));

    private void SetStartWithWindows(bool startWithWindows)
    {
        _config = _config with { Settings = _config.Settings with { StartWithWindows = startWithWindows } };
        ApplyStartup();
        foreach (var window in _windows.Values) window.SetStartupChecked(startWithWindows);
        SaveNow();
        RefreshSettings();
    }

    private void CreateFence()
    {
        (_config, _) = FenceMembership.CreateFence(_config, "New fence");
        SyncBoxes();
    }

    private void SetTakeover(bool active)
    {
        SetQuickHidden(false); // an explicit icons choice ends quick-hide first, so the two never disagree
        _takeoverActive = active;
        // Any explicit choice (banner or menu) answers the first-run question.
        _config = _config with { Settings = _config.Settings with { Takeover = active, TakeoverPromptAnswered = true } };
        SetIconsHidden(Current.IconsHidden);
        foreach (var window in _windows.Values) window.SetTakeoverChecked(active);
        RefreshSettings();
        RefreshWindows();
        SaveNow(); // not debounced: the saved setting must match the takeover-active marker if we are killed next
    }

    /// <returns>True when Windows confirmed the new state.</returns>
    private bool SetIconsHidden(bool hidden)
    {
        // The watchdog must know whenever icons may be hidden: mark before hiding, unmark only after a confirmed show.
        if (hidden) TryMarker(() => _watchdog.SetTakeoverActive(true), what: "takeover-active marker");
        var applied = DesktopIcons.TrySetHidden(hidden);
        if (applied && !hidden) TryMarker(() => _watchdog.SetTakeoverActive(false), what: "takeover-active marker");
        if (applied) Log.Information("desktop icons hidden: {Hidden}", hidden);
        else Log.Warning("could not set desktop icons hidden: {Hidden}", hidden);
        return applied;
    }

    /// <summary>Icons as they should be: hidden while Takeover is on, shown (and unmarked) otherwise.</summary>
    private bool EnsureIconState() =>
        Current.IconsHidden ? SetIconsHidden(true)
        : !_watchdog.IsTakeoverActiveMarked || SetIconsHidden(false);

    private static void TryMarker(Action writeMarker, string what)
    {
        try
        {
            writeMarker();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Error(failure, "could not update the {What}", what);
        }
    }

    private void OnExplorerRestarted()
    {
        Log.Information("Explorer restarted; re-attaching fences");
        SetPeek(false); // re-attaching sends fences to the bottom; Peek (and its global Esc) must end with it (M5 review M2)
        ShowTrayIcon(); // Explorer forgot every tray icon
        ReinstallMouseHook(); // a hook Windows dropped silently comes back here at the latest (M5 review)
        StartSpecialIconNotifications();
        ScheduleSpecialIconRefresh(); // the Recycle Bin may have changed meanwhile
        var attempts = 0;
        var retryTimer = new DispatcherTimer { Interval = ReattachInterval };
        retryTimer.Tick += (_, _) =>
        {
            attempts++;
            var unattachedCount = _windows.Values.Count(window => !DesktopHost.AttachToDesktop(window.Handle));
            foreach (var window in _windows.Values) FenceWindowChrome.SendToBack(window.Handle);
            var iconsOk = EnsureIconState();
            if ((unattachedCount == 0 && iconsOk) || attempts >= ReattachAttempts)
            {
                retryTimer.Stop();
                Log.Information("re-attach after Explorer restart: {Attempts} attempt(s), unattached {UnattachedCount}, icons ok {IconsOk}",
                    attempts, unattachedCount, iconsOk);
            }
        };
        retryTimer.Start();
    }

    /// <summary>The WH_MOUSE_LL desktop gestures and the Peek hotkey (M5). Either failing only turns that feature off.</summary>
    private void StartGestures()
    {
        UpdateMouseHook();
        UpdatePeekHotkey();
    }

    /// <summary>The Peek hotkey is registered exactly while wanted: released to Windows and the game while paused or gaming.</summary>
    private void UpdatePeekHotkey()
    {
        // Released while Settings records a new one, so pressing the current combination is recorded, not Peek (M6b review).
        if ((Current.PeekHotkeyWanted && !_recordingHotkey) == _peekHotkey is not null) return;
        if (_peekHotkey is not null)
        {
            _peekHotkey.Dispose();
            _peekHotkey = null;
            return;
        }
        _peekHotkey = TryRegisterPeekHotkey(_config.Settings.PeekHotkey, out var problem);
        _peekHotkeyProblem = _peekHotkey is null ? problem : null;
        if (_peekHotkey is null) Log.Warning("Peek is off: {Problem}", problem);
    }

    private GlobalHotkey? TryRegisterPeekHotkey(string text, out string problem)
    {
        if (!Hotkey.TryParse(text, out var hotkey) || !Enum.TryParse<System.Windows.Input.Key>(hotkey.Key, ignoreCase: true, out var key))
        {
            problem = $"\"{text}\" is not a hotkey: it needs Ctrl, Alt, Shift or Win plus one key.";
            return null;
        }
        var virtualKey = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            problem = $"{hotkey.DisplayText} has no key Windows can watch for. Pick another key."; // would say "Saved" and never fire (M6b review M4)
            return null;
        }
        var registration = new GlobalHotkey(_messages.Handle, PeekHotkeyId);
        if (registration.TryRegister(hotkey, (uint)virtualKey))
        {
            problem = "";
            Log.Information("Peek hotkey {Hotkey} registered", hotkey);
            return registration;
        }
        registration.Dispose();
        problem = $"{hotkey.DisplayText} is already taken by Windows or another app. Pick another combination.";
        return null;
    }

    /// <summary>Settings: a new Peek hotkey. It is kept only if Windows accepts it; otherwise the old one stays.</summary>
    private (bool Saved, string Message) SetPeekHotkey(string text)
    {
        if (!Hotkey.TryParse(text, out var parsed)) return (false, $"\"{text}\" is not a hotkey: it needs Ctrl, Alt, Shift or Win plus one key.");
        // A global hotkey swallows its keys in every app (M6b review I1): only safe combinations are recorded.
        if (!parsed.IsSafeToRecord) return (false, $"{parsed.DisplayText} would stop working everywhere else (typing, Tab, closing windows). Use Alt or Win with a key, or an F-key.");
        var normalized = parsed.ToString();
        // The same combination again is a retry when it is not registered (Settings showed it as not active, M6b review).
        if (normalized == _config.Settings.PeekHotkey && _peekHotkeyProblem is null) return (true, $"Peek: {parsed.DisplayText}");
        _peekHotkey?.Dispose();
        _peekHotkey = null;
        var registration = TryRegisterPeekHotkey(normalized, out var problem);
        if (registration is null)
        {
            UpdatePeekHotkey(); // the old one again
            return (false, problem);
        }
        registration.Dispose(); // proven free; UpdatePeekHotkey registers it whenever it is wanted
        _peekHotkeyProblem = null;
        _config = _config with { Settings = _config.Settings with { PeekHotkey = normalized } };
        UpdatePeekHotkey();
        SaveNow();
        return (true, $"Saved. Peek: {parsed.DisplayText}");
    }

    /// <summary>Tray or fence menu → Settings… (M6b). One window; a second request brings it to the front.</summary>
    private void OpenSettings()
    {
        if (_settingsWindow is { } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        var window = new SettingsWindow();
        window.StartWithWindowsChanged += SetStartWithWindows;
        window.TakeoverChanged += SetTakeover;
        window.PeekHotkeyChosen += text =>
        {
            var (saved, message) = SetPeekHotkey(text);
            RefreshSettings();
            window.ShowHotkeyResult(saved, message);
        };
        window.RollupExpandChanged += SetRollupExpand;
        window.TakeSnapshotRequested += () => TakeSnapshot();
        window.RestoreSnapshotRequested += RestoreSnapshot;
        window.RenameSnapshotRequested += (path, name) =>
        {
            if (!_snapshots.Rename(path, name)) Log.Warning(_snapshots.LastFailure, "snapshot {Path} could not be renamed", path);
            RefreshSettings();
        };
        window.DeleteSnapshotRequested += DeleteSnapshot;
        window.RulesChanged += rules =>
        {
            _config = _config with { Rules = rules };
            Log.Information("rules changed: {Count} rule(s)", rules.Count);
            SaveNow();
            RefreshSettings();
        };
        window.ApplyRulesRequested += ApplyRulesNow;
        WireLibrarySettings(window);
        window.OpenSnapshotsRequested += () =>
        {
            try
            {
                Directory.CreateDirectory(_snapshots.Directory);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                Log.Warning(failure, "snapshots folder {Folder} could not be created", _snapshots.Directory); // hard rule 7 (final review I3)
                return;
            }
            OpenItem(_snapshots.Directory, ownerHandle: 0);
        };
        window.HotkeyRecording += recording =>
        {
            _recordingHotkey = recording;
            UpdatePeekHotkey();
            // Leaving the box re-registers: Settings must show if that failed (final review I5).
            if (!recording) RefreshSettings();
        };
        window.DefaultLabelsChanged += labels =>
        {
            _config = _config with { Settings = _config.Settings with { DefaultLabels = labels } };
            SaveNow();
        };
        window.LabelsAppliedToAll += labels =>
        {
            _config = FenceEdits.SetLabelsEverywhere(_config, labels);
            foreach (var fenceWindow in _windows.Values) fenceWindow.SetLabelMode(labels);
            Log.Information("labels for every fence: {Labels}", labels);
            SaveNow();
            RefreshSettings();
        };
        window.ShortcutArrowsChanged += show =>
        {
            _config = _config with { Settings = _config.Settings with { ShowShortcutArrows = show } };
            foreach (var fenceWindow in _windows.Values) fenceWindow.SetShortcutArrows(show);
            SaveNow();
        };
        window.GameModeChanged += SetGameModeEnabled;
        window.OpenLogsRequested += () => OpenItem(AppPaths.LogsDirectory, ownerHandle: 0);
        window.OpenDataRequested += () => OpenItem(AppPaths.DataDirectory, ownerHandle: 0);
        window.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _recordingHotkey = false;
            UpdatePeekHotkey();
        };
        _settingsWindow = window;
        RefreshSettings();
        window.Show();
        window.Activate();
    }

    private void RefreshSettings() => _settingsWindow?.Show(new SettingsView(
        StartWithWindows: _config.Settings.StartWithWindows,
        Takeover: _takeoverActive,
        PeekHotkey: PeekHotkeyDisplay,
        PeekHotkeyActive: _peekHotkeyProblem is null,
        RollupExpand: _config.Settings.RollupExpand,
        GameModeEnabled: _config.Settings.GameMode,
        GameModeActive: _gameMode,
        Version: typeof(FenceHost).Assembly.GetName().Version?.ToString(3) ?? "",
        DataFolder: AppPaths.DataDirectory,
        DefaultLabels: _config.Settings.DefaultLabels,
        ShowShortcutArrows: _config.Settings.ShowShortcutArrows,
        Snapshots: ListSnapshots(),
        Rules: _config.Rules,
        RuleLines: [.. _config.Rules.Select(rule => Rules.Describe(rule, _config))],
        RuleFences: [.. _config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop).Select(fence => new RuleFence(fence.Id, fence.Title))],
        Library: LibrarySettingsView()));

    /// <summary>Fence menu → "Rules for this fence…" (M11): Settings at Rules, a new rule for that fence.</summary>
    private void OpenRulesFor(string fenceId)
    {
        OpenSettings();
        _settingsWindow?.BeginNewRule(fenceId);
    }

    /// <summary>The Peek hotkey as a person reads it ("Ctrl+Shift+=", not "Ctrl+Shift+OemPlus").</summary>
    private string PeekHotkeyDisplay =>
        Hotkey.TryParse(_config.Settings.PeekHotkey, out var hotkey) ? hotkey.DisplayText : _config.Settings.PeekHotkey;

    /// <summary>Saves the arrangement now, named by date and time (M10); renamed in Settings if wanted.</summary>
    private void TakeSnapshot()
    {
        var now = DateTimeOffset.Now;
        var snapshot = Snapshots.Take(_config, name: $"Snapshot {now:d MMM HH:mm}", now: now);
        if (_snapshots.Save(snapshot) is { } path)
        {
            Log.Information("snapshot saved: {Path}", path);
            _trayIcon?.ShowBalloon("Snapshot saved", snapshot.Name);
        }
        else
        {
            Log.Warning(_snapshots.LastFailure, "snapshot could not be saved");
            _trayIcon?.ShowBalloon("Snapshot not saved", "NeoFences could not write the snapshot file (see the log).");
        }
        RefreshSettings();
    }

    /// <summary>
    /// Puts a snapshot's arrangement back (M10, spec §3): only with a complete desktop listing, and only after "Before
    /// restore" was written, so the restore itself can be undone. One config change, saved at once.
    /// </summary>
    private void RestoreSnapshot(string path)
    {
        if (_snapshots.Load(path) is not { } snapshot)
        {
            Log.Warning(_snapshots.LastFailure, "snapshot {Path} could not be read", path);
            _trayIcon?.ShowBalloon("Snapshot not restored", "The snapshot file could not be read.");
            return;
        }
        var listing = DesktopItems.Enumerate();
        if (listing.UnavailableFolders.Count > 0)
        {
            Log.Warning("snapshot not restored: desktop folders not readable {Folders}", listing.UnavailableFolders);
            _trayIcon?.ShowBalloon("Snapshot not restored", "A Desktop folder cannot be read right now. Try again in a moment.");
            return;
        }
        var now = DateTimeOffset.Now;
        if (_snapshots.Save(Snapshots.Take(_config, name: $"Before restore ({now:d MMM HH:mm})", now: now), SnapshotStore.BeforeRestoreFileName) is null)
        {
            Log.Warning(_snapshots.LastFailure, "snapshot not restored: 'Before restore' could not be saved");
            _trayIcon?.ShowBalloon("Snapshot not restored", "NeoFences could not save 'Before restore' first (see the log).");
            return;
        }
        Log.Information("restoring snapshot {Name} from {Path}", snapshot.Name, path);
        _config = Snapshots.Restore(_config, snapshot, listing.ItemRefs);
        SaveNow();
        SyncBoxes();
        // Windows that kept their fence still show its old title, icon size and labels (final review I1).
        foreach (var window in _windows.Values)
        {
            if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is not { } shown) continue;
            window.Refresh(shown);
            RefreshPortal(window); // the Portal breadcrumb replaces the plain title again
        }
        RefreshSettings();
    }

    /// <summary>The snapshot list; a damaged file or an unreadable folder is logged once, not on every tray open (final review I2).</summary>
    private IReadOnlyList<SnapshotEntry> ListSnapshots()
    {
        var snapshots = _snapshots.List();
        foreach (var (path, failure) in _snapshots.Problems.Where(problem => _loggedSnapshotProblems.Add(problem.Path)))
            Log.Warning(failure, "snapshot {Path} skipped: it cannot be read", path);
        return snapshots;
    }

    /// <summary>A snapshot file goes to the Recycle Bin, never deleted for good (hard rule 1).</summary>
    private void DeleteSnapshot(string path)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        _shellWorker.Run(() =>
        {
            var (started, refused, missing) = ShellFileOps.TryRecycle(0, [path]);
            if (!started || refused.Count > 0) Log.Warning("snapshot {Path} could not be moved to the Recycle Bin", path);
            if (missing.Count > 0) Log.Information("snapshot {Path} was already gone", path);
            dispatcher.BeginInvoke(RefreshSettings);
        });
    }

    /// <summary>Fence menu → Labels (M8b).</summary>
    private void SetFenceLabels(FenceWindow window, LabelMode labels)
    {
        _config = FenceEdits.SetLabels(_config, window.FenceId, labels);
        window.SetLabelMode(labels);
        ScheduleSave();
    }

    private void SetRollupExpand(RollupExpand mode)
    {
        _config = _config with { Settings = _config.Settings with { RollupExpand = mode } };
        foreach (var window in _windows.Values) window.SetRollupExpand(mode);
        Log.Information("roll-up expands on: {Mode}", mode);
        SaveNow();
    }

    private void SetGameModeEnabled(bool enabled)
    {
        _config = _config with { Settings = _config.Settings with { GameMode = enabled } };
        Log.Information("game mode enabled: {Enabled}", enabled);
        SaveNow();
        CheckGameMode(); // turning it off in a game ends idle at once
        RefreshSettings();
    }

    /// <summary>The hook exists exactly while it is wanted: not while paused or gaming (hard rule 3, spec §4.7).</summary>
    private void UpdateMouseHook()
    {
        if (Current.MouseHookWanted == _mouseHook is not null) return;
        if (_mouseHook is not null)
        {
            EndDrawOverlay();
            _mouseHook.Dispose();
            _mouseHook = null;
            return;
        }
        var dispatcher = Dispatcher.CurrentDispatcher;
        _mouseHook = new DesktopMouseHook(
            onGesture: (gesture, screenX, screenY) => dispatcher.BeginInvoke(() => OnDesktopGesture(gesture, screenX, screenY)),
            onPeekClickOutside: () => dispatcher.BeginInvoke(() => SetPeek(false)),
            log: message => Log.Information("{Message}", message));
    }

    /// <summary>Windows drops a low-level hook silently (LowLevelHooksTimeout): a fresh one after Explorer restarts and on unlock.</summary>
    private void ReinstallMouseHook()
    {
        if (_mouseHook is null) return; // not wanted now; it comes back when it is
        EndDrawOverlay();
        _mouseHook.Dispose();
        _mouseHook = null;
        UpdateMouseHook();
    }

    private void OnSessionUnlocked()
    {
        Log.Information("session unlocked; re-installing the mouse hook");
        ReinstallMouseHook();
        CheckGameMode();
    }

    /// <summary>Game mode (spec §4.7, ADR-021): checked on every foreground change, and again shortly after (the signal lags).</summary>
    private void StartGameMode()
    {
        // Backstop for a game that goes full screen after the last re-check (M6a review I1).
        var poll = new DispatcherTimer { Interval = GameModePolicy.PollInterval };
        poll.Tick += (_, _) => CheckGameMode();
        poll.Start();
        _foregroundWatcher = new ForegroundWatcher(onForegroundChanged: OnForegroundChanged,
            logFailure: failure => Log.Error(failure, "game mode check failed"));
        if (!_foregroundWatcher.IsWatching) Log.Warning("game mode: foreground changes cannot be watched; game mode is off");
        CheckGameMode();
    }

    private void OnForegroundChanged()
    {
        CheckGameMode();
        foreach (var delay in GameModePolicy.RecheckDelays)
        {
            // ponytail: one short-lived timer per foreground change and delay; a coalescing timer if switching storms ever show up in a profile.
            var recheck = new DispatcherTimer { Interval = delay };
            recheck.Tick += (_, _) =>
            {
                recheck.Stop();
                CheckGameMode();
            };
            recheck.Start();
        }
    }

    private void CheckGameMode()
    {
        var gameMode = GameModePolicy.IsGameActive(enabled: _config.Settings.GameMode, foreground: GameDetection.TakeSnapshot());
        if (gameMode == _gameMode) return;
        _gameMode = gameMode;
        Log.Information("game mode: {GameMode}", gameMode);
        if (gameMode) SetPeek(false);
        UpdateMouseHook();
        foreach (var portal in _portals.Values) portal.SetPaused(gameMode);
        if (!gameMode) ApplyDeferredShellWork();
        UpdatePeekHotkey();
        _trayIcon?.SetTooltip(TrayTooltip());
        RefreshSettings();
    }

    /// <summary>The game was left: Desktop changes made meanwhile apply in order (or one reconcile if events were lost).</summary>
    private void ApplyDeferredShellWork()
    {
        if (_libraryDeferred)
        {
            _libraryDeferred = false;
            ScanLibrary(); // a game installed while playing shows up now (M12)
        }
        if (_specialIconsDeferred)
        {
            _specialIconsDeferred = false;
            ScheduleSpecialIconRefresh();
        }
        if (_reconcileDeferred)
        {
            _reconcileDeferred = false;
            _deferredDesktopChanges.Clear();
            ReconcileDesktop();
            return;
        }
        if (_deferredDesktopChanges.Count == 0) return;
        Log.Information("applying {Count} desktop change(s) from game mode", _deferredDesktopChanges.Count);
        var arrivals = new List<string>();
        foreach (var change in _deferredDesktopChanges)
        {
            if (ArrivalOf(change) is { } arrival) arrivals.Add(arrival);
            (_config, _rememberedPlacements) = FenceMembership.Apply(_config, change, _rememberedPlacements, Now);
        }
        _deferredDesktopChanges.Clear();
        RefreshWindows();
        ScheduleSave();
        FileNewItems(arrivals);
    }

    /// <summary>Pause (tray): the desktop goes back to Windows — fences hidden, icons shown, the hook gone — until resumed.</summary>
    private void SetPaused(bool paused)
    {
        if (paused == _paused) return;
        if (paused)
        {
            SetPeek(false);
            EndDrawOverlay();
        }
        _paused = paused;
        if (paused)
        {
            _quickHidden = false; // resuming shows everything
            _iconsHiddenByUser = false;
        }
        foreach (var window in _windows.Values)
        {
            if (!Current.FencesVisible) window.HideNow();
            else if (!window.IsVisible)
            {
                window.ShowNow();
                FenceWindowChrome.SendToBack(window.Handle);
            }
        }
        EnsureIconState(); // also shows icons left hidden by an earlier failed show: Pause "restores icons" (spec §6, M6a review M1)
        UpdateMouseHook();
        UpdatePeekHotkey();
        RefreshSettings(); // a hotkey that cannot be registered on resume shows in an open Settings (final review I5)
        _trayIcon?.SetTooltip(TrayTooltip());
        Log.Information("paused: {Paused}", paused);
    }

    private string TrayTooltip() =>
        _paused ? "NeoFences — paused" : _gameMode ? "NeoFences — idle while a game runs" : "NeoFences";

    /// <summary>Tray menu (spec §6; user choice 2026-10-03: left-click opens it too).</summary>
    private void ShowTrayMenu(int screenX, int screenY)
    {
        var snapshots = ListSnapshots();
        var restorable = snapshots.Where(entry => !entry.IsBeforeRestore).Take(TrayRestoreCount).ToList();
        var beforeRestore = snapshots.FirstOrDefault(entry => entry.IsBeforeRestore);
        List<TrayMenuItem> restoreItems = [.. restorable.Select((entry, index) => new TrayMenuItem(TrayRestoreFirst + index, entry.Name.Replace("&", "&&"))) /* & is a menu accelerator */];
        if (beforeRestore is not null) restoreItems.AddRange([TrayMenuItem.Separator, new TrayMenuItem(TrayRestoreBefore, "Undo the last restore")]);
        restoreItems.AddRange([TrayMenuItem.Separator, new TrayMenuItem(TraySettings, "More in Settings…")]);
        var chosen = TrayMenu.Show(_messages.Handle,
        [
            new TrayMenuItem(TrayNewFence, "New fence", Enabled: !_paused),
            new TrayMenuItem(TrayNewLibrary, "New Game Library fence", Enabled: !_paused),
            new TrayMenuItem(TrayQuickHide, "Quick-hide", Checked: _quickHidden, Enabled: !_paused),
            new TrayMenuItem(TrayPeek, $"Peek\t{PeekHotkeyDisplay}", Checked: _peeking, Enabled: !_paused),
            TrayMenuItem.Separator,
            new TrayMenuItem(TrayTakeSnapshot, "Take snapshot"),
            new TrayMenuItem(TrayRestoreMenu, "Restore snapshot", Enabled: snapshots.Count > 0) { Children = restoreItems },
            TrayMenuItem.Separator,
            new TrayMenuItem(TraySettings, "Settings…"),
            new TrayMenuItem(TrayPause, "Pause NeoFences", Checked: _paused),
            TrayMenuItem.Separator,
            new TrayMenuItem(TrayExit, "Exit NeoFences"),
        ], screenX, screenY);
        switch (chosen)
        {
            case TrayNewFence:
                SetQuickHidden(false); // a new fence must be visible (M6a review I3)
                CreateFence();
                break;
            case TrayNewLibrary: CreateLibraryFence(); break;
            case TrayQuickHide: SetQuickHidden(!_quickHidden); break;
            case TrayPeek: SetPeek(!_peeking); break;
            case TrayPause: SetPaused(!_paused); break;
            case TraySettings: OpenSettings(); break;
            case TrayExit: ExitRequested?.Invoke(); break;
            case TrayTakeSnapshot: TakeSnapshot(); break;
            case TrayRestoreBefore when beforeRestore is not null: RestoreSnapshot(beforeRestore.Path); break;
            case >= TrayRestoreFirst and < TrayRestoreFirst + TrayRestoreCount when chosen - TrayRestoreFirst < restorable.Count:
                RestoreSnapshot(restorable[chosen - TrayRestoreFirst].Path);
                break;
        }
    }

    private void OnDesktopGesture(DesktopGesture gesture, int screenX, int screenY)
    {
        switch (gesture)
        {
            // A double-click on a visible native icon opens it; only empty desktop toggles quick-hide.
            case DesktopGesture.DoubleClick when Current.IconsHidden
                || !DesktopWindows.IsOverDesktopIcon(screenX, screenY, log: message => Log.Warning("{Message}", message)):
                SetQuickHidden(!_quickHidden);
                break;
            case DesktopGesture.RightDragStarted:
                BeginDrawFence(screenX, screenY);
                break;
            case DesktopGesture.RightDragCompleted:
                EndDrawFence(screenX, screenY);
                break;
            case DesktopGesture.RightDragCancelled:
                EndDrawOverlay(); // the drag's right-up was never seen (M5 review I2)
                break;
        }
    }

    /// <summary>Quick-hide (M5, user choice): fences and the native desktop icons go away together and come back together.</summary>
    private void SetQuickHidden(bool hidden)
    {
        if (hidden == _quickHidden) return;
        if (hidden) SetPeek(false);
        // Icons the user had hidden through Explorer stay theirs: quick-hide neither hides nor later shows them (M8a).
        // Hidden now without the takeover-active marker: the user hid them in Explorer, so they stay the user's. With the marker
        // set, NeoFences hid them (an earlier show failed) and the end of this quick-hide retries the show (M8a review I1).
        if (hidden && !_takeoverActive) _iconsHiddenByUser = DesktopIcons.TryIsHidden() == true && !_watchdog.IsTakeoverActiveMarked;
        var iconsWereHidden = Current.IconsHidden;
        _quickHidden = hidden;
        foreach (var window in _windows.Values)
        {
            if (hidden) window.HideFaded(); // 150 ms fade (spec §6)
            else
            {
                window.ShowFaded();
                FenceWindowChrome.SendToBack(window.Handle);
            }
        }
        if (Current.IconsHidden != iconsWereHidden) SetIconsHidden(Current.IconsHidden); // RunState decides, user-hidden icons included
        if (!hidden) _iconsHiddenByUser = false;
        Log.Information("quick-hide: {Hidden}", hidden);
    }

    private void BeginDrawFence(int startX, int startY)
    {
        if (_mouseHook is not { } mouseHook) return;
        EndDrawOverlay();
        _drawStart = (startX, startY);
        var overlay = new DrawFenceOverlay(_lightTheme);
        overlay.Track(DrawFenceOverlay.Between(_drawStart, mouseHook.DragPoint));
        overlay.Show();
        _drawOverlay = overlay;
        _drawTimer = new DispatcherTimer { Interval = DrawFrame };
        // A lost right-up (Win+L, UAC, an elevated window) is ended by the next click (RightDragCancelled). The button
        // state cannot tell: the swallowed right-press never reaches Windows' key state (M5 review I2, smoke finding).
        _drawTimer.Tick += (_, _) => overlay.Track(DrawFenceOverlay.Between(_drawStart, mouseHook.DragPoint));
        _drawTimer.Start();
    }

    /// <summary>The right button came up: a new fence where the rectangle was, its title ready to type.</summary>
    private void EndDrawFence(int endX, int endY)
    {
        if (!EndDrawOverlay()) return; // the start was never seen
        if (_monitors.Count == 0 || _config.LastLayoutFingerprint is not { } fingerprint) return;
        SetQuickHidden(false);
        var pixels = DrawFenceOverlay.Between(_drawStart, (endX, endY));
        var monitor = FencePlacement.ContainingMonitor(pixels, _monitors);
        (_config, var fence) = FenceMembership.CreateFence(_config, "New fence");
        // Too small a drag still makes a usable fence: the layout clamps it to the minimum size.
        _config = LayoutEngine.WithFenceRect(_config, fingerprint: fingerprint, fenceId: fence.Id, rect: FencePlacement.FromPixels(pixels, monitor));
        Log.Information("fence drawn on the desktop at {Pixels}", pixels);
        OpenWindow(fence);
        RefreshWindows();
        ApplyLayout();
        ScheduleSave();
        if (_windows.TryGetValue(fence.Id, out var window)) window.BeginRename();
    }

    /// <returns>True when an overlay was showing.</returns>
    private bool EndDrawOverlay()
    {
        _drawTimer?.Stop();
        _drawTimer = null;
        if (_drawOverlay is null) return false;
        _drawOverlay.Close();
        _drawOverlay = null;
        return true;
    }

    private void OnHotkey(int hotkeyId)
    {
        CheckGameMode(); // fresh: a game may have gone full screen since the last check (M6a review I1)
        // Paused: the desktop belongs to Windows. Gaming: fences must not rise over the game, and the click-outside hook is off.
        if (_paused || _gameMode) return;
        if (hotkeyId == PeekHotkeyId) SetPeek(!_peeking);
        else if (hotkeyId == PeekEscapeHotkeyId) SetPeek(false);
    }

    /// <summary>Peek (M5): every fence above all windows until the hotkey again, Esc, a click outside, or an item opens.</summary>
    private void SetPeek(bool peeking)
    {
        if (peeking == _peeking) return;
        if (peeking) SetQuickHidden(false);
        _peeking = peeking;
        if (_mouseHook is not null) _mouseHook.PeekActive = peeking;
        // Every fence first: raising one restacks its siblings (all owned by Progman), and a sibling still keeping
        // itself at the bottom would drop back (M5 smoke: only one fence rose).
        foreach (var window in _windows.Values) window.Peeking = peeking;
        foreach (var window in _windows.Values) FenceWindowChrome.SetTopmost(window.Handle, peeking);
        _peekEscapeHotkey?.Dispose();
        _peekEscapeHotkey = null;
        if (peeking)
        {
            _peekEscapeHotkey = new GlobalHotkey(_messages.Handle, PeekEscapeHotkeyId);
            if (!_peekEscapeHotkey.TryRegister(new Hotkey(Ctrl: false, Alt: false, Shift: false, Win: false, Key: "Escape"), virtualKey: 0x1B))
                Log.Warning("Esc is taken by another app; Peek ends with its hotkey or a click outside");
        }
        Log.Information("peek: {Peeking}", peeking);
    }

    /// <summary>Title double-click (M5): rolled up to its title bar, or back. Stored, so it survives a restart.</summary>
    private void ToggleRollUp(FenceWindow window)
    {
        var rolledUp = !_config.Fences.First(fence => fence.Id == window.BoxId).RolledUp; // the box's (M9)
        _config = FenceEdits.SetRolledUp(_config, window.BoxId, rolledUp);
        window.SetRolledUp(rolledUp);
        ScheduleSave();
    }

    /// <summary>Adds the tray icon, retrying while Explorer is still busy (sign-in autostart, Explorer restart).</summary>
    private void ShowTrayIcon()
    {
        if (_trayIcon is not { } trayIcon || trayIcon.Show()) return;
        var attempts = 0;
        var retry = new DispatcherTimer { Interval = TrayRetryInterval };
        retry.Tick += (_, _) =>
        {
            if (trayIcon.Show() || ++attempts >= TrayRetryAttempts)
            {
                retry.Stop();
                Log.Information("tray icon retry finished after {Attempts} attempt(s)", attempts + 1);
            }
        };
        retry.Start();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            if (!_store.Save(_config)) Log.Warning("config not saved: config.json is read-only this session");
            else if (_store.LastBackupFailure is { } backupFailure) Log.Warning(backupFailure, "config saved, but the daily backups could not be written or pruned");
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Error(failure, "config save failed");
        }
    }

    private static void TryCreateFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Warning(failure, "cannot create {Folder}", folder); // the fence then shows "not available" (hard rule 7)
        }
    }
}
```

- [ ] **Step 2: Build and test**

Run: `dotnet build NeoFences.slnx`, then `dotnet test NeoFences.slnx`
Expected: 0 warnings, 0 errors; `Passed: 361`.

- [ ] **Step 3: Live checks (only with the user's go; print TEST RUNNING / TEST COMPLETE; restore the installed copy)**

Run `m12-smoke.ps1 -Exe <branch exe>` and `m12-hide.ps1 -Exe <branch exe>` with Windows PowerShell (session scratchpad; both dot-source `m8b-helpers.ps1`). Add a Y6 check to `m12-hide.ps1`: after hiding, the hidden list in `config.json` holds both the Desktop-shortcut id and the `folder:` id of AC Black Flag.
Expected: as in `docs/research/m12-game-library.md`; Y6 shows both ids.

- [ ] **Step 4: Commit**

```powershell
git add src/NeoFences.App
git commit -m "feat: added the game library fence with poster tiles, hide from library, live rescans and a settings card"
```

---

### Task 4: Verification and docs

- [ ] **Step 1: Append section Y to `docs/TEST-CHECKLIST.md`**

```markdown

## Y — Game Library (M12, ADR-032)
| ID | Steps | Expected |
|---|---|---|
| Y1 | Tray → New Game Library fence | a "Games" fence with every installed game, A–Z; Steam games as posters, others as icons on dark tiles |
| Y2 | Tray → New Game Library fence again | no second fence; the library's tab shows if it is a tab |
| Y3 | Double-click a Steam, an Epic and a `D:\GameLibrary` game | each starts (launcher games through their launcher) |
| Y4 | Right-click a game | the shell menu without Rename, plus "Hide from library" and "Open install folder" |
| Y5 | Hide from library (or Delete) | the game leaves the fence; Settings → Game Library lists it; "Show again" brings it back |
| Y6 | Hide a `D:\GameLibrary` game that also has a Desktop shortcut, then delete the shortcut | the game stays hidden |
| Y7 | Install or uninstall a Steam game (or add / remove a folder in a game folder) | the library follows within seconds, no restart |
| Y8 | Unplug the drive of a game folder (or a Steam library), Refresh library | its games stay; the status line names the unreadable source |
| Y9 | Drag a game onto the Desktop | a copy of its shortcut lands there; the game stays in the library |
| Y10 | Drag a file onto the library fence | nothing is dropped (no cursor, no file) |
| Y11 | Settings → untick Steam | Steam games leave the library; ticking brings them back |
| Y12 | Game mode: install a game while a full-screen game runs, then leave the game | the library updates after the game |
| Y13 | Merge the library into a box with another fence (tabs), switch tabs | both tabs show correctly; the library keeps its tiles |
| Y14 | Delete the library fence | only the fence goes; games and launcher files are untouched |
| Y15 | Keyboard / Narrator: tiles read by game name; the Settings card's lists and checkboxes are named | usable without the mouse |
```

- [ ] **Step 2: Append ADR-032 to `docs/DECISIONS.md`**

```markdown

## ADR-032 — Game Library: generated shortcuts shown like a Portal (M12, v1.5)
**Date:** 2026-10-04 · **Status:** Accepted · **Spec:** `superpowers/specs/2026-10-03-game-library-design.md`

**Context.** The user wants every installed game in one place (launchers, Xbox, `D:\GameLibrary`, Desktop game
shortcuts), cover art where launchers keep it on disk, one A–Z fence. A read-only probe found Steam, Epic, GOG and Xbox
games and posters in Steam's library cache.

**Decision.**
- **A new fence kind** `FenceSourceKind.Library` (at most one, repaired on load). Core treats every non-Desktop fence
  like a Portal already (no items, never a rule target), so only the App needed new code.
- **Core** (pure, tested): a small Valve text reader (`ValveKeyValues`, `SteamFiles`), `EpicManifest`, `GameCatalog`
  (merge by install folder or launch target, source priority launcher > Xbox > Desktop shortcut > folder, tool
  skipping, hidden ids, A–Z ignoring "The", games of unreadable scans kept), `ProgramPicker`, `LibraryFiles` (safe,
  stable, unique file names; only changed shortcuts rewritten), `LibrarySettings` in `config.json`.
- **Shell:** `GameScanners` (Steam per library, Epic manifests, GOG / Ubisoft registry, EA / Battle.net uninstall entries,
  Xbox packages with `MicrosoftGame.config`, game folders, Desktop shortcuts), `ShellLinks` (read/write `.lnk` via
  `IShellLinkW`, `.url` as INI text), `LibraryWriter` (temp-then-swap shortcuts, `index.json`; deletes only files in its
  own index). Item menus can add custom commands and hide Rename; drags can be copy-only; a fence can refuse drops.
- **App:** the library folder `%LOCALAPPDATA%\NeoFences\library\` is shown by the Portal machinery; scans run on their
  own STA thread at start, on changes to Steam's / Epic's / game folders (5 s quiet), on Refresh, after game mode;
  tiles 2:3 with posters or Xbox logos; Hide from library stores every id the game goes by; Settings → Game Library.

**Consequences.**
- NeoFences writes shortcut files into its own data folder; user files, game folders and launcher files are never
  changed. An older NeoFences cannot read a config with a library fence (it falls back to a backup) — downgrades only.
- A Desktop shortcut into a game folder wins over the folder entry (the user's chosen program); Steam `.url` shortcuts
  on the Desktop merge with the Steam entry.
- Steam apps that are not games (Wallpaper Engine) show until hidden; obvious tools are skipped by name.
```

- [ ] **Step 3: Write `docs/research/m12-game-library.md`**

```markdown
# M12 — Game Library: results

**Date:** 2026-10-03/04 · **Machine:** Windows 11 Pro 25H2 (26200), NeoFences 1.4.0 installed.
**Build:** M12 prototype (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section Y · **Decision:** ADR-032.

## Read-only probes on the user's PC

- Steam: `HKCU\Software\Valve\Steam\SteamPath`; libraries `C:\Program Files (x86)\Steam` (Wallpaper Engine,
  Steamworks Common Redistributables) and `D:\SteamLibrary` (Detroit, Plague Inc, Rebel Inc). Posters:
  `appcache\librarycache\<appid>\library_600x900.jpg` or, in Steam's newer layout, `…\<appid>\<hash>\library_capsule.jpg`
  (300×450) — all three games have one.
- Epic: `C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests\*.item` (Mafia DE); no art on disk.
- GOG: `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\1449710114` (Mafia II, in `D:\GameLibrary`); Galaxy not installed.
- Xbox: `HKCU\…\AppModel\Repository\Packages\<full name>\PackageRootFolder` + `MicrosoftGame.config` (Minecraft Launcher;
  logo `GraphicsLogo.png`; app id from `AppxManifest.xml`).
- `D:\GameLibrary`: the largest-program rule picks the right program for AC, Blur, Forza and PRAGMATA; the Desktop
  shortcuts decide for Clair Obscur and Mafia II.
- Scanner probe: 12 games in 0.2 s (Steam 5 entries in 2 libraries, Epic 1, GOG 1, Xbox 1, folder 6, Desktop 10 →
  merged); shortcuts written to a scratch folder and read back; a second plan rewrote nothing.

## Live smoke on the prototype (installed copy restored; test library folder and scratch games removed)

| Check | Result |
|---|---|
| Y1 tray → New Game Library fence | **Pass**: 13 entries (the user's 12 + a scratch test game), A–Z; tiles shown. |
| Posters | **Pass**: Detroit's Steam cover fills its tile (scrolling screenshots); others show the icon on a tile. |
| Y7 a new game folder appears / is removed (scratch games folder) | **Pass**: listed within 9 s; gone after removal. |
| Y8 the scratch games folder renamed away, Refresh | **Pass**: its game stays; the status line names the unreadable folder. |
| Y4/Y5 right-click → Hide from library; Settings → Show again | **Pass**: the menu ends with "Hide from library", "Open install folder" (no Rename); the game left and came back. |
| Log | 0 warnings or errors. |

**Found on the way:**
- The game was hidden under its Desktop shortcut's id; deleting that shortcut would have brought it back from the
  folder → hiding now stores every id the merged game goes by (test added).
- A first test script's right-click missed the tile (a UIA name lookup hit the hover-label popup) and opened the
  Desktop's menu; nothing was changed. Smoke scripts now aim at the tile's own list item and check the point lies inside
  the fence, and never press Delete as a fallback.
```

- [ ] **Step 4: Update the other docs**
  - `ROADMAP.md`: the claim line becomes `- [x] M12 — done <date> (ADR-032, research/m12-game-library.md)`; add `- [ ] v1.5.0 release`.
  - `FEATURES.md`: the row `| Game Library fence (Steam/Epic/GOG/Ubisoft Connect/EA, cover art) | v2 | — | |` becomes `| Game Library fence (Steam/Epic/GOG/Ubisoft Connect/EA, cover art) | v1.5 | M12 | done: launchers, Xbox, game folders, Desktop game shortcuts; Steam posters; hide; live rescans (ADR-032) |`.
  - `ARCHITECTURE.md`: add an "M12 complete" line (library fence kind, Core `Library` namespace, Shell scanners/writer, `%LOCALAPPDATA%\NeoFences\library\`).
  - `SESSION-LOG.md`: add an entry.

- [ ] **Step 5: Refresh the hub** (`node --check`, publish with `url`).

- [ ] **Step 6: Commit**

```powershell
git add docs
git commit -m "docs: added ADR-032, M12 checks and results, and recorded the game library"
```
