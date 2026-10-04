# M13b — v1.6 Carry-overs: Game Library and Rules Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the Game Library and rules carry-overs from the M11–M13a reviews. Released as v1.6.1.
- Library contents:
  - only real Epic games;
  - real Xbox names;
  - no duplicates from shortcuts into a launcher's game folder;
  - shortcut games copied as is.
- Speed at 300+ games.
- Hidden games remembered by name.
- No orphans or empty index after a power cut.
- A one-time pre-schema-2 config copy.
- Drives released even mid-listing.
- A rules editor that cannot lose an edit.
- Tab switching while dragging over a library tab.

**Architecture:**
- **Core:** Epic category and main-game filter; containment merge; launcher links in arguments; `GameCatalog.SourceName` / `HiddenGames` with `HiddenEntry` stored in `LibraryState.Hidden`; `GameEntry.ShortcutFile`; `Settle(…, lostFiles)`; `ConfigStore.PreviousSchemaCopyName`; snapshot temp cleanup on any failure.
- **Shell:**
  - scanners reuse remembered programs, keep Epic games on a locked manifest, and resolve Xbox resource names;
  - folder watchers for directories only;
  - item facts read only the start of a `.url` and expand `%VARS%`;
  - the writer sweeps temp files, copies shortcuts, and writes the index through;
  - drop hit-test before refusal, drag image cleared;
  - removal notices disposable twice.
- **App:**
  - the library uses remembered games and hidden entries, and plain source names;
  - Portals release in-flight listings;
  - covers decoded at tile width;
  - rules editor guards and focus;
  - library stop flag set before the session-end return.

**Tech Stack:** .NET 10, WPF, CsWin32 0.3.335 (adds `SHLoadIndirectString`), Serilog, xUnit. No new dependencies.

**Spec:** none (a carry-over batch). Source: `docs/ROADMAP.md` carry-overs from the M11, M12 and M13a reviews; user choice 2026-10-04 (3 batches, each released). **Decision:** ADR-034 (new, Task 3).

**Pre-verified (2026-10-04):** every code block below was compiled together (0 warnings, 0 errors), and **392/392 tests pass** (19 new).
- **Read-only probes:** the same 12 games; a repeat scan in 33 ms; package resource names resolve ("Windows Calculator").
- **Live check** (PC unattended, standing go; config and the installed 1.6.0 restored):
  - stray temp files are swept while other files stay;
  - a Desktop shortcut game is copied byte for byte;
  - a hidden game is remembered with both ids and listed once by name;
  - Show again works;
  - the rules editor locks its list while open;
  - 0 log warnings.

## Global Constraints

- **Hard rule 1:** only NeoFences' own files in `library\` are ever deleted: index entries, and temp names matching `^\.[0-9a-f]{32}\.(lnk|url)$`. Copying the user's shortcut only reads it.
- **Hard rule 7:** a locked manifest, a broken package or a lost shortcut degrades that entry or that scan, never the app.
- **Nothing launches** on its own; game links are unchanged.
- **Commits:** single line, Conventional Commits, past tense, no `Co-Authored-By` trailer. Test command: `dotnet test NeoFences.slnx`.
- **Smokes:**
  - restore the installed copy (1.6.0), `config.json` and the test library folder;
  - print TEST RUNNING / TEST COMPLETE;
  - aim at a tile's own list item inside the fence, never press Delete blind;
  - while the user says the PC is unattended, no consent question is needed.

## Review Focus

1. **A shortcut game whose shortcut changes or goes.** The user edits, renames or deletes the Desktop shortcut, or points it elsewhere; the Public Desktop copy is unreadable. Expected: the library follows, never keeps a stale copy for long, and never touches the user's shortcut.
2. **Containment merging edge cases.** Shortcuts to `steam.exe`, `EpicGamesLauncher.exe` or `Galaxy.exe`; a shortcut into a game that lives inside another game's folder (mods, tools); a folder whose name is a prefix of another. Expected: no game is swallowed by a launcher or a neighbour.
3. **Hidden games over time.** Hide, unplug, restart, plug back, Show again, hide under a different source. Expected: one row per game, the right name, and Show again always works.
4. **Power cuts and partial writes in `library\`.** A cut during a shortcut write, between shortcut and index, or during the index write. Expected: the next start loads an index, sweeps NeoFences' temp files only, and rebuilds.
5. **The rules editor and Settings refreshes.** Game-mode status refreshes while editing; a fence renamed or deleted mid-edit; keyboard-only use. Expected: no lost edit, the fence list stays current, focus stays where the user is.

---

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/NeoFences.Core/Library/GameCatalog.cs`, `LauncherFiles.cs`, `LibraryFiles.cs`, `Model/Rules.cs`, `Config/ConfigStore.cs`, `Config/SnapshotStore.cs` + `tests/…/Library/LibraryPolishTests.cs` (new), `tests/…/Config/DataSafetyTests.cs` | catalog, Epic, index, rules, config copy | 1 |
| `src/NeoFences.Shell/GameScanners.cs`, `LibraryWriter.cs`, `FolderItems.cs`, `ItemFactsReader.cs`, `ShellDragDrop.cs`, `DeviceRemovalNotice.cs`, `NativeMethods.txt`, `src/NeoFences.App/FenceHost.Library.cs`, `FenceHost.cs`, `FenceWindow.xaml.cs`, `PortalState.cs`, `SettingsWindow.Rules.cs` | scanners, writer, drops, App wiring | 2 |
| `docs/…` (ADR-034, checklist Z9–Z18, research, ROADMAP, ARCHITECTURE, SESSION-LOG), hub | docs | 3 |

---

### Task 1: Core — catalog, Epic, index, rules, config copy

**Files:**
- Modify: `docs/ROADMAP.md` (claim).
- Create: `tests/NeoFences.Core.Tests/Library/LibraryPolishTests.cs`.
- Replace: `src/NeoFences.Core/Library/GameCatalog.cs`, `Library/LauncherFiles.cs`, `Library/LibraryFiles.cs`, `Model/Rules.cs`, `Config/ConfigStore.cs`, `Config/SnapshotStore.cs`, `tests/NeoFences.Core.Tests/Config/DataSafetyTests.cs`.

**Interfaces:**
- Produces:
  - `HiddenEntry(Ids, Name)`;
  - `GameCatalog.SourceName(scanKey)`, `GameCatalog.HiddenGames(everything, hidden, previous)`;
  - `GameEntry.ShortcutFile`;
  - `LibraryState.Hidden`;
  - `LibraryFiles.Settle(previous, plan, failedWrites, failedDeletes, lostFiles)`;
  - `ConfigStore.PreviousSchemaCopyName`.

- [ ] **Step 1: Branch and claim**

```powershell
git switch -c m13b-library-polish
```
In `docs/ROADMAP.md`, the line `- [ ] M13b — Game Library and rules polish (v1.6.1)` becomes `- [~] M13b — Game Library and rules polish (v1.6.1) — claimed by session 2026-10-04 m13b`.
```powershell
git add docs/ROADMAP.md
git commit -m "docs: claimed M13b"
```

- [ ] **Step 2: Write the failing tests**

`tests/NeoFences.Core.Tests/Library/LibraryPolishTests.cs`:
```csharp
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
        File.WriteAllText(Path.Combine(store.BackupsDirectory, "config-20261005.json"), """{ "schemaVersion": 2, "fences": [] }""");

        Assert.True(store.Save(NeoFencesConfig.CreateDefault()));

        var copy = Path.Combine(store.BackupsDirectory, ConfigStore.PreviousSchemaCopyName);
        Assert.Contains("\"old\"", File.ReadAllText(copy));
        Assert.DoesNotMatch(@"^config-.*\.json$", ConfigStore.PreviousSchemaCopyName); // never picked as a daily backup, never pruned
        File.WriteAllText(copy, "kept");
        Assert.True(store.Save(NeoFencesConfig.CreateDefault()));
        Assert.Equal("kept", File.ReadAllText(copy)); // once only
    }
}
```
`tests/NeoFences.Core.Tests/Config/DataSafetyTests.cs` (the `Settle` calls gain `lostFiles: []`):
```csharp
using NeoFences.Core.Config;
using NeoFences.Core.Library;
using NeoFences.Core.Model;
using NeoFences.Core.Tests.TestSupport;

namespace NeoFences.Core.Tests.Config;

/// <summary>
/// M13a (v1.6.0) data safety: carry-overs from the M9–M12 reviews — the schema version, snapshots from newer versions,
/// duplicate rule ids, the library index after failed writes, and the M10 test gaps.
/// </summary>
public class DataSafetyTests : IDisposable
{
    private const string Desktop = @"C:\Users\cipher\Desktop\";
    private static readonly DateTimeOffset Taken = new(2026, 10, 4, 12, 0, 0, TimeSpan.FromHours(5.5));
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    // ---------- schema version (M9 carry-over: an older build would save over tabs, rules and the library) ----------

    [Fact]
    public void Schema_IsTwo_AndAnOldConfigIsUpgradedWhenNormalized()
    {
        Assert.Equal(2, NeoFencesConfig.CurrentSchemaVersion);
        var old = ConfigJson.Deserialize("""{ "schemaVersion": 1, "fences": [] }""");
        Assert.Equal(1, old.SchemaVersion);
        Assert.Equal(2, ConfigNormalizer.Normalize(old).SchemaVersion);
    }

    [Fact]
    public void Schema_AVersionOneFileLoads_AndIsSavedAsTwo_WhileANewerFileIsNeverOverwritten()
    {
        var store = new ConfigStore(_directory.Path, new FixedTimeProvider(Taken));
        File.WriteAllText(store.ConfigPath, """{ "schemaVersion": 1, "fences": [ { "id": "a", "title": "Games" } ] }""");

        var loaded = store.Load();
        Assert.False(loaded.IsReadOnly);
        Assert.True(store.Save(loaded.Config));
        Assert.Contains("\"schemaVersion\": 2", File.ReadAllText(store.ConfigPath));

        File.WriteAllText(store.ConfigPath, """{ "schemaVersion": 3, "fences": [] }""");
        var newer = new ConfigStore(_directory.Path, new FixedTimeProvider(Taken));
        Assert.True(newer.Load().IsReadOnly);
        Assert.False(newer.Save(NeoFencesConfig.CreateDefault()));
    }

    // ---------- rules (M11 carry-over) ----------

    [Fact]
    public void Rules_DuplicateIdsGetNewOnes_TheFirstKeepsItsId()
    {
        var images = Rule.Create(new RuleCondition { Kind = RuleKind.Type, Group = TypeGroup.Images }, "fence") with { Id = "same" };
        var config = ConfigNormalizer.Normalize(new NeoFencesConfig { Rules = [images, images with { FenceId = "other" }, images] });

        Assert.Equal("same", config.Rules[0].Id);
        Assert.Equal(3, config.Rules.Select(rule => rule.Id).Distinct().Count());
    }

    // ---------- snapshots (M10 carry-overs and test gaps) ----------

    private static NeoFencesConfig Sample(out Fence inbox, out Fence games)
    {
        inbox = Fence.Create("Inbox") with { IsInbox = true, Items = [Desktop + "a.txt"] };
        games = Fence.Create("Games") with { Items = [Desktop + "Game.lnk"] };
        return new NeoFencesConfig { Fences = [inbox, games] };
    }

    [Fact]
    public void Snapshots_FromANewerVersionAreRefused_NotHalfRead()
    {
        var store = new SnapshotStore(_directory.Path);
        var path = Path.Combine(_directory.Path, "snapshot-newer.json");
        File.WriteAllText(path, """{ "schemaVersion": 3, "name": "From the future", "takenAt": "2026-10-04T12:00:00+05:30", "fences": [] }""");

        Assert.Null(store.Load(path)); // a rename would rewrite it without the fields this version does not know
        Assert.NotNull(store.LastFailure);
        Assert.Empty(store.List());
        Assert.Equal([path], store.Problems.Select(problem => problem.Path));
    }

    [Fact]
    public void Snapshots_AHugeStrayFileIsSkipped_NotRead()
    {
        var store = new SnapshotStore(_directory.Path);
        var huge = Path.Combine(_directory.Path, "huge.json");
        using (var stream = File.Create(huge)) stream.SetLength(SnapshotStore.MaxFileBytes + 1);

        Assert.Empty(store.List());
        Assert.Equal([huge], store.Problems.Select(problem => problem.Path));
    }

    [Fact]
    public void Snapshots_AFailedSaveLeavesNoTempFile()
    {
        var store = new SnapshotStore(_directory.Path);
        Directory.CreateDirectory(Path.Combine(_directory.Path, "blocked.json")); // a folder where the file should go

        Assert.Null(store.Save(Snapshots.Take(Sample(out _, out _), name: "x", now: Taken), "blocked.json"));
        Assert.NotNull(store.LastFailure);
        Assert.Empty(Directory.GetFiles(_directory.Path, "*.tmp"));
    }

    [Fact]
    public void Snapshots_RestoreKeepsAPortal_MatchesRefsIgnoringCase_AndOrdersNewcomers()
    {
        var current = Sample(out var inbox, out var games);
        var portal = Fence.Create("Downloads", FenceSource.Portal(@"D:\Downloads"));
        var snapshot = Snapshots.Take(current with { Fences = [.. current.Fences, portal] }, name: "s", now: Taken);
        // Since then: Game.lnk renamed in case only, a.txt moved by hand into Games, three new unfenced items.
        current = current
            .WithFence(inbox with { Items = [] })
            .WithFence(games with { Items = [Desktop + "a.txt", Desktop + "new-in-games.txt"] });
        string[] desktopNow = [Desktop + "z.txt", Desktop + "GAME.LNK", Desktop + "y.txt", Desktop + "a.txt", Desktop + "new-in-games.txt", Desktop + "x.txt"];

        var restored = Snapshots.Restore(current, snapshot, desktopNow);

        Assert.Empty(restored.Fences.Single(fence => fence.Title == "Downloads").Items);
        Assert.Equal([Desktop + "Game.lnk", Desktop + "new-in-games.txt"], restored.Fences.Single(fence => fence.Id == games.Id).Items);
        Assert.Equal([Desktop + "a.txt", Desktop + "z.txt", Desktop + "y.txt", Desktop + "x.txt"], restored.Inbox.Items); // the Desktop's order
    }

    [Fact]
    public void Snapshots_AFileWithNullFieldsStillRestores()
    {
        var store = new SnapshotStore(_directory.Path);
        var path = Path.Combine(_directory.Path, "nulls.json");
        File.WriteAllText(path, """{ "schemaVersion": 1, "name": null, "takenAt": "2026-10-04T12:00:00+05:30", "fences": [ null, { "id": "g", "title": null, "items": null, "tabs": null } ], "layouts": null }""");

        var snapshot = store.Load(path)!;
        var restored = Snapshots.Restore(Sample(out _, out _), snapshot, [Desktop + "a.txt"]);

        Assert.Single(restored.Fences, fence => fence.IsInbox);
        Assert.Contains(restored.Fences, fence => fence.Id == "g");
        Assert.Equal([Desktop + "a.txt"], restored.Fences.SelectMany(fence => fence.Items));
    }

    // ---------- the Game Library index after failed writes (M12 carry-over) ----------

    private static GameEntry Game(string id, string name) => new(id, name, GameSource.Steam, "steam", new GameLaunch($"steam://rungameid/{id}"));

    [Fact]
    public void LibraryIndex_AFailedRewriteKeepsTheOldEntry_AFailedDeleteStaysListed_AFailedNewFileIsLeftOut()
    {
        var previous = LibraryFiles.Plan(previous: [], games: [Game("1", "Old Name"), Game("2", "Gone"), Game("3", "Same")]).Items;
        var plan = LibraryFiles.Plan(previous, games: [Game("1", "Old Name") with { Poster = @"C:\new.jpg" }, Game("3", "Same"), Game("4", "New")]);

        var state = LibraryFiles.Settle(previous, plan, failedWrites: ["Old Name.url", "New.url"], failedDeletes: ["Gone.url"], lostFiles: []);

        Assert.Equal(["Old Name.url", "Same.url", "Gone.url"], state.Items.Select(item => item.FileName));
        Assert.Null(state.Items[0].Game.Poster); // the old file is still the old one: it is retried next time
        Assert.Equal(plan.Items, LibraryFiles.Settle(previous, plan, failedWrites: [], failedDeletes: [], lostFiles: []).Items);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/NeoFences.Core.Tests`
Expected: build FAILS: `CS0117 'ConfigStore' does not contain a definition for 'PreviousSchemaCopyName'`, `CS0117 'GameCatalog' … 'HiddenGames'`, `CS0117 'GameCatalog' … 'SourceName'`, `CS1739 … 'Settle' does not have a parameter named 'lostFiles'`.

- [ ] **Step 4: Implement**

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
    /// <summary>A Desktop shortcut's own file: the library copies it, keeping its icon and "Run as administrator" (M13b).</summary>
    public string? ShortcutFile { get; init; }

    /// <summary>Ids of the same game found by weaker sources (merged into this one): hiding covers them too.</summary>
    public IReadOnlyList<string> OtherIds { get; init; } = [];
}

/// <summary>A hidden game as Settings lists it: every id it goes by, and its name (M13b).</summary>
public sealed record HiddenEntry(IReadOnlyList<string> Ids, string Name);

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
            // An unreadable source keeps its games, also those of its parts ("steam" keeps "steam:d:\steamlibrary"; final review I1).
            found.AddRange(scan.Readable ? scan.Games : previous.Where(game => game.ScanKey == scan.ScanKey
                || game.ScanKey.StartsWith(scan.ScanKey + ":", StringComparison.Ordinal)));
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
        || LaunchKey(first.Launch) == LaunchKey(second.Launch)
        || StartsInside(first, second) || StartsInside(second, first);

    /// <summary>
    /// The program of <paramref name="game"/> lies inside <paramref name="other"/>'s install folder: a Desktop shortcut to
    /// "…\steamapps\common\Dota 2\game\bin\dota2.exe" is the Steam game (M13b). Programs only, never folders: a
    /// shortcut to steam.exe must not swallow a game installed under Steam's folder.
    /// </summary>
    private static bool StartsInside(GameEntry game, GameEntry other) =>
        !game.Launch.IsLink && other.InstallFolder is { } folder
        && NormalizeFolder(game.Launch.Target).StartsWith(NormalizeFolder(folder) + "\\", StringComparison.Ordinal);

    /// <summary>A scan key as people say it, for the status line: "Steam library d:\steamlibrary", "game folder d:\gamelibrary".</summary>
    public static string SourceName(string scanKey)
    {
        var colon = scanKey.IndexOf(':');
        var (kind, detail) = colon < 0 ? (scanKey, "") : (scanKey[..colon], scanKey[(colon + 1)..]);
        return (kind, detail) switch
        {
            ("steam", "") => "Steam",
            ("steam", _) => $"Steam library {detail}",
            ("folder", _) => $"game folder {detail}",
            ("epic", _) => "Epic",
            ("gog", _) => "GOG",
            ("ubisoft", _) => "Ubisoft Connect",
            ("ea", _) => "EA app",
            ("battlenet", _) => "Battle.net",
            ("xbox", _) => "Xbox",
            ("desktop", _) => "Desktop shortcuts",
            ("library", _) => "the library",
            _ => scanKey,
        };
    }

    /// <summary>
    /// The hidden games for Settings, one per game with every id it goes by and its name (M13b): found now, else as
    /// remembered from before (its source cannot be read right now), else by its id alone.
    /// </summary>
    public static IReadOnlyList<HiddenEntry> HiddenGames(IReadOnlyList<GameEntry> everything, IReadOnlyCollection<string> hidden, IReadOnlyList<HiddenEntry> previous)
    {
        var hiddenIds = hidden.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<HiddenEntry>();
        foreach (var game in everything.Where(game => IdsOf(game).Any(hiddenIds.Contains)))
        {
            entries.Add(new HiddenEntry(IdsOf(game), game.Name));
            covered.UnionWith(IdsOf(game));
        }
        foreach (var entry in previous.Where(entry => entry.Ids.Any(hiddenIds.Contains) && !entry.Ids.Any(covered.Contains)))
        {
            entries.Add(entry);
            covered.UnionWith(entry.Ids);
        }
        entries.AddRange(hidden.Where(id => covered.Add(id)).Select(id => new HiddenEntry([id], id)));
        return entries;
    }

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
                // Bit 4 = fully installed (also while updating); a queued download has a manifest too (final review I3).
                if (Text(state, "StateFlags") is { } flags && int.TryParse(flags, out var stateFlags) && (stateFlags & 4) == 0) return null;
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
            // Only games: an add-on names its main game; Unreal Engine and other tools carry no "games" category (M13b).
            if (Text("MainGameAppName") is { } mainGame && !string.Equals(mainGame, appName, StringComparison.OrdinalIgnoreCase)) return null;
            if (root.TryGetProperty("AppCategories", out var categories) && categories.ValueKind == JsonValueKind.Array && categories.GetArrayLength() > 0
                && !categories.EnumerateArray().Any(category => category.ValueKind == JsonValueKind.String && category.GetString() == "games")) return null;
            return new EpicGame(name, location, Text("LaunchExecutable"), Text("CatalogNamespace") ?? "", Text("CatalogItemId") ?? "", appName);
        }
        catch (JsonException)
        {
            return null;
        }
    }
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

    /// <summary>Hidden games with their names, so Settings can list them while their source cannot be read (M13b).</summary>
    public IReadOnlyList<HiddenEntry> Hidden { get; init; } = [];
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

    /// <summary>
    /// The index that matches the folder after <see cref="Plan"/> was carried out with some failures (M13a): a file that
    /// could not be rewritten keeps its old entry (the old file is still there, retried next time), a new file that could
    /// not be written is left out, and a file that could not be deleted stays listed (deleted next time).
    /// </summary>
    /// <param name="lostFiles">Failed writes whose file is not on disk at all (deleted by hand, then not re-created): dropped (M13b).</param>
    public static LibraryState Settle(IReadOnlyList<LibraryItem> previous, LibraryPlan plan, IReadOnlyCollection<string> failedWrites, IReadOnlyCollection<string> failedDeletes,
        IReadOnlyCollection<string> lostFiles)
    {
        var lost = lostFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var failedWrite = failedWrites.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var failedDelete = failedDeletes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previousByFile = new Dictionary<string, LibraryItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in previous) previousByFile.TryAdd(item.FileName, item);
        var items = plan.Items
            .Where(item => !lost.Contains(item.FileName))
            .Select(item => !failedWrite.Contains(item.FileName) ? item : previousByFile.GetValueOrDefault(item.FileName))
            .OfType<LibraryItem>()
            .Concat(previous.Where(item => failedDelete.Contains(item.FileName)))
            .ToList();
        return new LibraryState { Items = items };
    }

    /// <summary>
    /// A loaded index made safe (final review, re-graded minor 3): entries with missing fields, a file name that is a path
    /// (a delete must never leave the library folder, hard rule 1) or a second entry for the same file are dropped.
    /// </summary>
    public static LibraryState Repair(LibraryState state)
    {
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = (state.Items ?? [])
            .Where(item => item?.Game is { Id.Length: > 0, Name: not null, ScanKey: not null, Launch.Target.Length: > 0 }
                           && item.FileName is { Length: > 0 } fileName && fileName == Path.GetFileName(fileName)
                           && fileName is not ("." or "..") && fileName.IndexOfAny(['/', '\\', ':']) < 0
                           && fileNames.Add(fileName))
            .Select(item => item with { Signature = item.Signature ?? "", Game = item.Game with { OtherIds = item.Game.OtherIds ?? [] } })
            .ToList();
        var hidden = (state.Hidden ?? []).Where(entry => entry is { Ids: not null, Name: not null } && entry.Ids.All(id => id is not null)).ToList();
        return new LibraryState { Items = items, Hidden = hidden };
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
        string.Join('|', game.Name, game.Launch.Target, game.Launch.Arguments, game.Launch.WorkingFolder, game.Poster, game.IconPath, game.InstallFolder, game.ShortcutFile);
}
```
`src/NeoFences.Core/Model/Rules.cs`:
```csharp
using System.IO.Enumeration;
using NeoFences.Core.Membership;

namespace NeoFences.Core.Model;

public enum RuleKind { Type, Game, Name, Age, Size }

public enum TypeGroup { ShortcutsApps, Images, Videos, Documents, Archives, Folders }

/// <summary>Folder: shortcuts into a folder of the user's own (games outside any launcher, user choice 2026-10-03).</summary>
public enum GameLauncher { Any, Steam, Epic, Ubisoft, Ea, BattleNet, Gog, Folder }

public enum RuleCompare { OlderThan, NewerThan, BiggerThan, SmallerThan }

/// <summary>What a rule looks at (M11). Only the fields of its <see cref="Kind"/> matter.</summary>
public sealed record RuleCondition
{
    public RuleKind Kind { get; init; }
    /// <summary>Type: a ready-made group, or <see cref="Extensions"/> instead.</summary>
    public TypeGroup? Group { get; init; }
    /// <summary>Type: custom extensions, ".iso .torrent" (dots optional; spaces or commas between).</summary>
    public string? Extensions { get; init; }
    public GameLauncher? Launcher { get; init; }
    /// <summary>Game with <see cref="GameLauncher.Folder"/>: the folder the shortcuts point into, like D:\GameLibrary.</summary>
    public string? Folder { get; init; }
    /// <summary>Name: * and ? wildcards; plain text means "contains". Case-insensitive.</summary>
    public string? Pattern { get; init; }
    public RuleCompare? Compare { get; init; }
    public double? Days { get; init; }
    public double? Megabytes { get; init; }
}

/// <summary>"When the condition matches, put the item in this fence" (M11). Rules are ordered; the first match wins.</summary>
public sealed record Rule
{
    public string Id { get; init; } = ""; // not required: a hand-edited rule without one is repaired, not a corrupt file
    public bool Enabled { get; init; } = true;
    public RuleCondition Condition { get; init; } = new();
    public string FenceId { get; init; } = "";

    public static Rule Create(RuleCondition condition, string fenceId) =>
        new() { Id = Guid.NewGuid().ToString("N"), Condition = condition, FenceId = fenceId };
}

/// <summary>What NeoFences knows about a desktop item, read by the Shell layer (M11).</summary>
/// <param name="ShortcutTarget">For .lnk: target path and arguments; for .url: the URL. Null otherwise or when unreadable.</param>
public sealed record ItemFacts(string ItemRef, string Name, string Extension, bool IsFolder, long? SizeBytes, DateTimeOffset? Modified, string? ShortcutTarget);

/// <summary>
/// Rules auto-sort (M11, spec 2026-10-03-rules-design): which fence a desktop item belongs in. Pure: the App reads the
/// facts and saves the result. Membership only; nothing touches files.
/// </summary>
public static class Rules
{
    public static readonly IReadOnlyDictionary<TypeGroup, string[]> Groups = new Dictionary<TypeGroup, string[]>
    {
        [TypeGroup.ShortcutsApps] = [".lnk", ".url", ".exe", ".appref-ms", ".msi", ".bat", ".cmd"],
        [TypeGroup.Images] = [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".tif", ".tiff", ".svg", ".ico"],
        [TypeGroup.Videos] = [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v"],
        [TypeGroup.Documents] = [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".odt", ".ods", ".md", ".csv"],
        [TypeGroup.Archives] = [".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso", ".cab"],
        [TypeGroup.Folders] = [],
    };

    private static readonly (string Prefix, GameLauncher Launcher)[] LauncherUrls =
    [
        ("steam://", GameLauncher.Steam), ("com.epicgames.launcher://", GameLauncher.Epic), ("uplay://", GameLauncher.Ubisoft),
        ("origin://", GameLauncher.Ea), ("origin2://", GameLauncher.Ea), ("ea://", GameLauncher.Ea), ("link2ea://", GameLauncher.Ea),
        ("battlenet://", GameLauncher.BattleNet), ("goggalaxy://", GameLauncher.Gog),
    ];

    private static readonly (string Part, GameLauncher Launcher)[] LibraryFolders =
    [
        (@"\steamapps\common\", GameLauncher.Steam), (@"\Epic Games\", GameLauncher.Epic),
        (@"\Ubisoft Game Launcher\games\", GameLauncher.Ubisoft), (@"\EA Games\", GameLauncher.Ea),
        (@"\GOG Galaxy\Games\", GameLauncher.Gog), (@"\GOG Games\", GameLauncher.Gog),
    ];

    /// <summary>The game launcher a shortcut belongs to (its URL scheme, library folder or launcher arguments), or null.</summary>
    public static GameLauncher? LauncherOf(string? shortcutTarget)
    {
        if (string.IsNullOrWhiteSpace(shortcutTarget)) return null;
        var target = shortcutTarget.Trim();
        foreach (var (prefix, launcher) in LauncherUrls)
        {
            // The link itself, or the link handed to the launcher as an argument (M13b): "steam.exe steam://rungameid/570".
            if (target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || target.Contains(" " + prefix, StringComparison.OrdinalIgnoreCase) || target.Contains("\"" + prefix, StringComparison.OrdinalIgnoreCase)) return launcher;
        }
        // The Epic launcher itself lives under "\Epic Games\Launcher\": not a game.
        foreach (var (part, launcher) in LibraryFolders)
        {
            if (target.Contains(part, StringComparison.OrdinalIgnoreCase) && !target.Contains(@"\Epic Games\Launcher\", StringComparison.OrdinalIgnoreCase)) return launcher;
        }
        if (target.Contains("steam.exe", StringComparison.OrdinalIgnoreCase) && target.Contains("-applaunch", StringComparison.OrdinalIgnoreCase)) return GameLauncher.Steam;
        if (target.Contains("battle.net.exe", StringComparison.OrdinalIgnoreCase) && target.Contains("--exec", StringComparison.OrdinalIgnoreCase)) return GameLauncher.BattleNet;
        return null;
    }

    private static readonly string[] DownloadExtensions = [".crdownload", ".part", ".partial", ".download", ".opdownload", ".tmp"];

    /// <summary>
    /// A rename from a download or temp name (Chrome's .crdownload, Firefox's .part, save-then-rename .tmp) is a new item
    /// for rules; a rename the user makes is not (final review I2).
    /// </summary>
    public static bool IsDownloadRename(string oldItemRef) =>
        DownloadExtensions.Contains(Path.GetExtension(oldItemRef), StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether one condition matches an item (a broken condition never matches).</summary>
    public static bool Matches(RuleCondition condition, ItemFacts facts, DateTimeOffset now) => condition.Kind switch
    {
        RuleKind.Type when condition.Group == TypeGroup.Folders => facts.IsFolder,
        RuleKind.Type when condition.Group is { } group && Groups.TryGetValue(group, out var extensions) =>
            !facts.IsFolder && extensions.Contains(facts.Extension, StringComparer.OrdinalIgnoreCase),
        RuleKind.Type when condition.Group is null => !facts.IsFolder && ExtensionsOf(condition.Extensions).Contains(facts.Extension, StringComparer.OrdinalIgnoreCase),
        RuleKind.Game when condition.Launcher == GameLauncher.Folder => InFolder(facts.ShortcutTarget, condition.Folder),
        RuleKind.Game => LauncherOf(facts.ShortcutTarget) is { } launcher && (condition.Launcher is GameLauncher.Any or null || condition.Launcher == launcher),
        RuleKind.Name when !string.IsNullOrWhiteSpace(condition.Pattern) => NameMatches(condition.Pattern.Trim(), facts.Name),
        RuleKind.Age when facts.Modified is { } modified && condition.Days is { } days => condition.Compare switch
        {
            RuleCompare.OlderThan => (now - modified).TotalDays > days, // no TimeSpan: "99999999 days" must not overflow (final review C1)
            RuleCompare.NewerThan => (now - modified).TotalDays < days,
            _ => false,
        },
        RuleKind.Size when !facts.IsFolder && facts.SizeBytes is { } size && condition.Megabytes is { } megabytes => condition.Compare switch
        {
            RuleCompare.BiggerThan => size > megabytes * 1024 * 1024,
            RuleCompare.SmallerThan => size < megabytes * 1024 * 1024,
            _ => false,
        },
        _ => false,
    };

    /// <summary>The fence of the first enabled rule that matches and names an existing desktop fence; null = stays put.</summary>
    public static string? Match(ItemFacts facts, NeoFencesConfig config, DateTimeOffset now)
    {
        var desktopFenceIds = config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop).Select(fence => fence.Id).ToHashSet(StringComparer.Ordinal);
        return config.Rules.FirstOrDefault(rule => rule.Enabled && desktopFenceIds.Contains(rule.FenceId) && Matches(rule.Condition, facts, now))?.FenceId;
    }

    /// <summary>Moves every matched item to the end of its rule's fence, in the order given; the same config when nothing moves.</summary>
    public static NeoFencesConfig File(NeoFencesConfig config, IReadOnlyList<ItemFacts> facts, DateTimeOffset now)
    {
        foreach (var item in facts)
        {
            if (Match(item, config, now) is not { } fenceId) continue;
            var owner = config.Fences.FirstOrDefault(fence => fence.Items.Contains(item.ItemRef, ItemRef.Comparer));
            if (owner is null || owner.Id == fenceId) continue; // gone meanwhile, or already there
            config = FenceMembership.MoveItem(config, item.ItemRef, fenceId);
        }
        return config;
    }

    /// <summary>How many of these items <see cref="File"/> would move (the "N items moved" notice).</summary>
    public static int CountMoves(NeoFencesConfig config, IReadOnlyList<ItemFacts> facts, DateTimeOffset now) =>
        facts.Count(item => Match(item, config, now) is { } fenceId
                            && config.Fences.FirstOrDefault(fence => fence.Items.Contains(item.ItemRef, ItemRef.Comparer)) is { } owner
                            && owner.Id != fenceId);

    /// <summary>A rule as one line for Settings: "Images → Pictures".</summary>
    public static string Describe(Rule rule, NeoFencesConfig config)
    {
        var condition = rule.Condition;
        var what = condition.Kind switch
        {
            RuleKind.Type when condition.Group is { } group => GroupName(group),
            RuleKind.Type => $"Extensions {string.Join(" ", ExtensionsOf(condition.Extensions))}",
            RuleKind.Game when condition.Launcher == GameLauncher.Folder => $"Game shortcuts in {condition.Folder}",
            RuleKind.Game => condition.Launcher is GameLauncher.Any or null ? "Game shortcuts (any launcher)" : $"{LauncherName(condition.Launcher.Value)} game shortcuts",
            RuleKind.Name => $"Name like \"{condition.Pattern}\"",
            RuleKind.Age => $"{(condition.Compare == RuleCompare.NewerThan ? "Newer" : "Older")} than {condition.Days:0.##} days",
            RuleKind.Size => $"{(condition.Compare == RuleCompare.SmallerThan ? "Smaller" : "Bigger")} than {condition.Megabytes:0.##} MB",
            _ => "(broken rule)",
        };
        var fence = config.Fences.FirstOrDefault(candidate => candidate.Id == rule.FenceId && candidate.Source.Kind == FenceSourceKind.Desktop);
        return $"{what} → {(fence is null ? "(fence missing)" : fence.Title)}";
    }

    public static string GroupName(TypeGroup group) => group == TypeGroup.ShortcutsApps ? "Shortcuts and apps" : group.ToString();

    public static string LauncherName(GameLauncher launcher) => launcher switch
    {
        GameLauncher.Ea => "EA",
        GameLauncher.BattleNet => "Battle.net",
        GameLauncher.Gog => "GOG",
        _ => launcher.ToString(),
    };

    /// <summary>Load-time repair: a rule whose condition cannot work is kept but disabled (M11 spec §2).</summary>
    public static Rule Repair(Rule rule)
    {
        var condition = rule.Condition ?? new RuleCondition { Kind = (RuleKind)(-1) };
        var valid = condition.Kind switch
        {
            RuleKind.Type => condition.Group is { } group ? Enum.IsDefined(group) : ExtensionsOf(condition.Extensions).Count > 0,
            RuleKind.Game when condition.Launcher == GameLauncher.Folder => !string.IsNullOrWhiteSpace(condition.Folder),
            RuleKind.Game => condition.Launcher is null || Enum.IsDefined(condition.Launcher.Value),
            RuleKind.Name => !string.IsNullOrWhiteSpace(condition.Pattern),
            RuleKind.Age => condition.Days is >= 0 && condition.Compare is RuleCompare.OlderThan or RuleCompare.NewerThan,
            RuleKind.Size => condition.Megabytes is >= 0 && condition.Compare is RuleCompare.BiggerThan or RuleCompare.SmallerThan,
            _ => false,
        };
        var repaired = rule with
        {
            Id = string.IsNullOrWhiteSpace(rule.Id) ? Guid.NewGuid().ToString("N") : rule.Id,
            FenceId = rule.FenceId ?? "",
            Condition = condition,
        };
        return valid ? repaired : repaired with { Enabled = false };
    }

    /// <summary>Whether a shortcut points inside a folder (the folder itself, not a name starting the same).</summary>
    private static bool InFolder(string? shortcutTarget, string? folder) =>
        !string.IsNullOrWhiteSpace(shortcutTarget) && !string.IsNullOrWhiteSpace(folder)
        && shortcutTarget.Trim().StartsWith(folder.Trim().TrimEnd('\\') + '\\', StringComparison.OrdinalIgnoreCase);

    private static List<string> ExtensionsOf(string? text) =>
        (text ?? "").Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(extension => extension.StartsWith('.') ? extension : "." + extension).ToList();

    private static bool NameMatches(string pattern, string name) =>
        pattern.Contains('*') || pattern.Contains('?')
            ? FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true)
            : name.Contains(pattern, StringComparison.OrdinalIgnoreCase);
}
```
`src/NeoFences.Core/Config/ConfigStore.cs`:
```csharp
using System.Text.Json;
using NeoFences.Core.Model;

namespace NeoFences.Core.Config;

public enum ConfigLoadSource { Primary, Backup, DailyBackup, Fresh }

/// <param name="CorruptCopyPath">Where an unreadable config.json was preserved, if it was.</param>
/// <param name="IsReadOnly">
/// True when config.json must not be overwritten this session: it was written by a newer NeoFences, or it
/// could not be read (locked by antivirus, OneDrive or an editor). The config returned is the best fallback.
/// </param>
public sealed record ConfigLoadResult(NeoFencesConfig Config, ConfigLoadSource Source, string? CorruptCopyPath, bool IsReadOnly);

/// <summary>
/// Loads and saves <c>config.json</c> (ADR-006): atomic replace with <c>.bak</c>, one backup per day
/// (newest 10 kept), and a recovery chain for corrupt files. Debouncing saves is the caller's job.
/// </summary>
public sealed class ConfigStore(string directory, TimeProvider? timeProvider = null)
{
    public const string FileName = "config.json";
    public const int DailyBackupsKept = 10;

    /// <summary>
    /// One copy of the last config from before schema 2, kept in <c>backups\</c> for good (M13b): daily backups rotate out
    /// after 10 days, and NeoFences ≤ 1.5 can only read schema 1. Not a "config-*.json" name: never picked or pruned as a daily.
    /// </summary>
    public static string PreviousSchemaCopyName => $"pre-schema-{NeoFencesConfig.CurrentSchemaVersion}-config.json";

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private bool _saveBlocked;

    public string ConfigPath => Path.Combine(directory, FileName);
    public string BackupPath => ConfigPath + ".bak";
    public string BackupsDirectory => Path.Combine(directory, "backups");
    private string TempPath => ConfigPath + ".tmp";

    public ConfigLoadResult Load()
    {
        _saveBlocked = false;
        string? corruptCopyPath = null;

        switch (TryRead(ConfigPath, out var primary))
        {
            case ReadOutcome.Ok:
                return new(ConfigNormalizer.Normalize(primary!), ConfigLoadSource.Primary, null, IsReadOnly: false);
            case ReadOutcome.NewerSchema:
                _saveBlocked = true;
                return new(NeoFencesConfig.CreateDefault(), ConfigLoadSource.Fresh, null, IsReadOnly: true);
            case ReadOutcome.Unreadable:
                // Not corrupt, just inaccessible right now: show the best fallback, but never overwrite it.
                _saveBlocked = true;
                break;
            case ReadOutcome.Corrupt:
                corruptCopyPath = Path.Combine(directory, $"config.corrupt-{_time.GetLocalNow():yyyyMMdd-HHmmss}.json");
                try
                {
                    File.Copy(ConfigPath, corruptCopyPath, overwrite: true);
                }
                catch (Exception copyFailure) when (copyFailure is IOException or UnauthorizedAccessException)
                {
                    corruptCopyPath = null;
                    _saveBlocked = true; // could not preserve it, so do not overwrite it either
                }
                break;
        }

        if (TryRead(BackupPath, out var backup) == ReadOutcome.Ok)
        {
            return new(ConfigNormalizer.Normalize(backup!), ConfigLoadSource.Backup, corruptCopyPath, IsReadOnly: _saveBlocked);
        }

        foreach (var dailyBackupPath in DailyBackupsNewestFirst())
        {
            if (TryRead(dailyBackupPath, out var daily) == ReadOutcome.Ok)
            {
                return new(ConfigNormalizer.Normalize(daily!), ConfigLoadSource.DailyBackup, corruptCopyPath, IsReadOnly: _saveBlocked);
            }
        }

        return new(NeoFencesConfig.CreateDefault(), ConfigLoadSource.Fresh, corruptCopyPath, IsReadOnly: _saveBlocked);
    }

    /// <returns>
    /// False when saving is blocked: config.json belongs to a newer NeoFences version (checked on disk on every
    /// save, so a second store or a save without a prior Load cannot overwrite it either), or Load found it
    /// unreadable.
    /// </returns>
    public bool Save(NeoFencesConfig config)
    {
        if (_saveBlocked || TryRead(ConfigPath, out _) == ReadOutcome.NewerSchema) return false;

        Directory.CreateDirectory(directory);
        var json = ConfigJson.Serialize(config);
        using (var tempFile = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        using (var writer = new StreamWriter(tempFile))
        {
            writer.Write(json);
            writer.Flush();
            tempFile.Flush(flushToDisk: true);
        }

        if (File.Exists(ConfigPath))
        {
            File.Replace(TempPath, ConfigPath, BackupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(TempPath, ConfigPath);
        }

        // The daily backup is a convenience: its failure (a full disk, a file in the way) must not fail the save (M8a).
        try
        {
            WriteDailyBackup(json);
            KeepPreviousSchemaCopy();
            LastBackupFailure = null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            LastBackupFailure = failure;
        }
        return true;
    }

    /// <summary>Why the last save could not write or prune the daily backup (null when it could). Reported apart from Save.</summary>
    public Exception? LastBackupFailure { get; private set; }

    private void WriteDailyBackup(string json)
    {
        Directory.CreateDirectory(BackupsDirectory);
        // First save of the day wins: a config that went bad later in the day cannot overwrite it.
        var todayPath = Path.Combine(BackupsDirectory, $"config-{_time.GetLocalNow():yyyyMMdd}.json");
        if (!File.Exists(todayPath)) File.WriteAllText(todayPath, json);

        foreach (var expiredPath in DailyBackupsNewestFirst().Skip(DailyBackupsKept))
        {
            File.Delete(expiredPath);
        }
    }

    private void KeepPreviousSchemaCopy()
    {
        var copy = Path.Combine(BackupsDirectory, PreviousSchemaCopyName);
        if (File.Exists(copy)) return;
        foreach (var candidate in DailyBackupsNewestFirst().Prepend(BackupPath))
        {
            if (TryRead(candidate, out var old) != ReadOutcome.Ok || old!.SchemaVersion >= NeoFencesConfig.CurrentSchemaVersion) continue;
            File.Copy(candidate, copy);
            return;
        }
    }

    private IEnumerable<string> DailyBackupsNewestFirst() =>
        Directory.Exists(BackupsDirectory)
            ? Directory.GetFiles(BackupsDirectory, "config-*.json").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToList()
            : [];

    private enum ReadOutcome { Missing, Ok, Corrupt, NewerSchema, Unreadable }

    private const int ReadAttempts = 3;
    private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(100);

    private static ReadOutcome TryRead(string path, out NeoFencesConfig? config)
    {
        config = null;
        if (!File.Exists(path)) return ReadOutcome.Missing;

        string? json = null;
        for (var attempt = 1; json is null; attempt++)
        {
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception readFailure) when (readFailure is IOException or UnauthorizedAccessException)
            {
                // ponytail: blocking retry (~200 ms worst case), fine for startup; make async if Load moves off-thread.
                if (attempt == ReadAttempts) return ReadOutcome.Unreadable;
                Thread.Sleep(ReadRetryDelay);
            }
        }

        try
        {
            config = ConfigJson.Deserialize(json);
        }
        catch (JsonException)
        {
            return ReadOutcome.Corrupt;
        }
        if (config.SchemaVersion > NeoFencesConfig.CurrentSchemaVersion) return ReadOutcome.NewerSchema;
        return config.SchemaVersion < 1 ? ReadOutcome.Corrupt : ReadOutcome.Ok;
    }
}
```
`src/NeoFences.Core/Config/SnapshotStore.cs`:
```csharp
using System.Text.Json;
using NeoFences.Core.Model;

namespace NeoFences.Core.Config;

/// <summary>A snapshot file as the list shows it.</summary>
public sealed record SnapshotEntry(string Path, string Name, DateTimeOffset TakenAt)
{
    /// <summary>The automatic one written before every restore (replaced each time).</summary>
    public bool IsBeforeRestore => string.Equals(System.IO.Path.GetFileName(Path), SnapshotStore.BeforeRestoreFileName, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Snapshot files (M10): one per snapshot in <c>%LOCALAPPDATA%\NeoFences\snapshots\</c>, written to a temp file and then
/// swapped in, so a power cut never leaves half a snapshot. A damaged file is skipped and reported, never thrown.
/// Deleting is the App's job (the Recycle Bin, hard rule 1).
/// </summary>
public sealed class SnapshotStore(string directory)
{
    public const string BeforeRestoreFileName = "before-restore.json";

    /// <summary>Bigger files are not snapshots (a stray file): skipped unread, so a tray click never loads it (M13a).</summary>
    public const long MaxFileBytes = 16 * 1024 * 1024;

    public string Directory => directory;

    /// <summary>Files the last <see cref="List"/> could not read (path and reason).</summary>
    public IReadOnlyList<(string Path, Exception Failure)> Problems { get; private set; } = [];

    /// <summary>Why the last <see cref="Save"/> or <see cref="Rename"/> failed, or null.</summary>
    public Exception? LastFailure { get; private set; }

    /// <summary>Every readable snapshot, newest first.</summary>
    public IReadOnlyList<SnapshotEntry> List()
    {
        var entries = new List<SnapshotEntry>();
        var problems = new List<(string, Exception)>();
        string[] paths;
        try
        {
            paths = System.IO.Directory.Exists(directory) ? System.IO.Directory.GetFiles(directory, "*.json") : [];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            paths = []; // the folder itself cannot be listed: no snapshots, never a crash (final review I3, hard rule 7)
            problems.Add((directory, failure));
        }
        foreach (var path in paths)
        {
            try
            {
                var snapshot = Read(path);
                entries.Add(new SnapshotEntry(path, snapshot.Name ?? "", snapshot.TakenAt));
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                problems.Add((path, failure));
            }
        }
        Problems = problems;
        return entries.OrderByDescending(entry => entry.TakenAt).ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Writes a snapshot; a new file (named by its time) unless <paramref name="fileName"/> is given.</summary>
    /// <returns>The file's path, or null when it could not be written (<see cref="LastFailure"/>).</returns>
    public string? Save(Snapshot snapshot, string? fileName = null)
    {
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, fileName ?? NewFileName(snapshot.TakenAt));
            WriteSafely(path, ConfigJson.SerializeSnapshot(snapshot));
            LastFailure = null;
            return path;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            LastFailure = failure;
            return null;
        }
    }

    /// <summary>The snapshot in a file, or null when it cannot be read.</summary>
    public Snapshot? Load(string path)
    {
        try
        {
            return Read(path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            LastFailure = failure;
            return null;
        }
    }

    /// <exception cref="InvalidDataException">Too big, or written by a newer NeoFences (M13a): restoring or renaming it
    /// here would drop what this version does not know.</exception>
    private static Snapshot Read(string path)
    {
        if (new FileInfo(path).Length > MaxFileBytes) throw new InvalidDataException($"{path} is too big for a snapshot");
        var snapshot = ConfigJson.DeserializeSnapshot(File.ReadAllText(path));
        if (snapshot.SchemaVersion > NeoFencesConfig.CurrentSchemaVersion)
            throw new InvalidDataException($"{path} comes from a newer NeoFences (schema {snapshot.SchemaVersion})");
        return snapshot;
    }

    /// <summary>
    /// A new name for a snapshot (its time stays). A renamed "Before restore" becomes an ordinary snapshot file, so the
    /// next restore does not overwrite it (final review I4); our own file is moved, nothing is deleted.
    /// </summary>
    public bool Rename(string path, string newName)
    {
        if (Load(path) is not { } snapshot) return false;
        var fileName = System.IO.Path.GetFileName(path);
        if (string.Equals(fileName, BeforeRestoreFileName, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                fileName = NewFileName(snapshot.TakenAt);
                File.Move(path, System.IO.Path.Combine(directory, fileName));
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                LastFailure = failure;
                return false;
            }
        }
        return Save(snapshot with { Name = newName.Trim() }, fileName) is not null;
    }

    private string NewFileName(DateTimeOffset takenAt)
    {
        var stem = $"snapshot-{takenAt:yyyy-MM-dd_HH-mm-ss}";
        var name = stem + ".json";
        for (var counter = 2; File.Exists(System.IO.Path.Combine(directory, name)); counter++) name = $"{stem}-{counter}.json";
        return name;
    }

    private static void WriteSafely(string path, string json)
    {
        var temp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else File.Move(temp, path);
        }
        catch
        {
            if (File.Exists(temp)) File.Delete(temp); // no *.json.tmp left behind, also after a full disk (M13a, M13b); the caller reports the failure
            throw;
        }
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test NeoFences.slnx`
Expected: build FAILS in `NeoFences.Shell` (`LibraryWriter` still calls the old `Settle`) — run the Core tests alone: `dotnet test tests/NeoFences.Core.Tests` → `Passed! - Failed: 0, Passed: 392`. (Task 2 makes the whole solution build again.)

- [ ] **Step 6: Commit**

```powershell
git add src/NeoFences.Core tests/NeoFences.Core.Tests
git commit -m "fix: fixed epic add-ons listed as games, shortcut duplicates of launcher games, hidden games losing their names and the pre-schema-2 config rotating out"
```

---

### Task 2: Shell + App — scanners, writer, drops, wiring

**Files:** replace `src/NeoFences.Shell/GameScanners.cs`, `LibraryWriter.cs`, `FolderItems.cs`, `ItemFactsReader.cs`, `ShellDragDrop.cs`, `DeviceRemovalNotice.cs`, `NativeMethods.txt`, `src/NeoFences.App/FenceHost.Library.cs`, `FenceHost.cs`, `FenceWindow.xaml.cs`, `PortalState.cs`, `SettingsWindow.Rules.cs`.

**Interfaces:**
- Consumes: Task 1.
- Produces:
  - `GameScanners.ScanAll(settings, logFailure, previous)`;
  - `LibraryWriter.Apply(folder, previous, plan, hidden, logFailure)`;
  - `FolderWatcher(folder, logFailure, directoriesOnly)`.

- [ ] **Step 1: Files**

`src/NeoFences.Shell/NativeMethods.txt`:
```text
// Win32 APIs used by NeoFences.Shell. CsWin32 generates bindings for each name.
AllowSetForegroundWindow
AppendMenu
BHID_SFObject
BITMAP
BITMAPINFO
CallNextHookEx
CLIPBOARD_FORMAT
CLSID_DragDropHelper
CMF_CANRENAME
CMF_EXTENDEDVERBS
CMF_NORMAL
CMIC_MASK_PTINVOKE
CMINVOKECOMMANDINFOEX
CreatePopupMenu
CreateRoundRectRgn
CUIAutomation
DeleteObject
DestroyIcon
DestroyMenu
DIB_USAGE
DISPLAY_DEVICEW
DragQueryFile
DROPEFFECT
DVASPECT
DWM_WINDOW_CORNER_PREFERENCE
DwmSetWindowAttribute
DWMWINDOWATTRIBUTE
EnumDisplayDevices
EnumDisplayMonitors
EVENT_SYSTEM_FOREGROUND
FileOpenDialog
FILEOPENDIALOGOPTIONS
FileOperation
FILEOPERATION_FLAGS
FindWindow
FOLDERFLAGS
FORMATETC
GCS_VERBW
GET_ANCESTOR_FLAGS
GET_WINDOW_CMD
GetAncestor
GetClassName
GetCurrentThreadId
GetCursorPos
GetDC
GetDIBits
GetDoubleClickTime
GetDpiForMonitor
GetDpiForSystem
GetForegroundWindow
GetMessage
GetModuleHandle
GetMonitorInfo
GetObject
GetSystemMetrics
GetSystemMetricsForDpi
GetWindow
GetWindowLongPtr
GetWindowRect
GetWindowText
GetWindowThreadProcessId
HDROP
HOT_KEY_MODIFIERS
HWND_BOTTOM
HWND_NOTOPMOST
HWND_TOPMOST
IContextMenu
IContextMenu2
IContextMenu3
IDataObject
IDropTarget
IDropTargetHelper
IFileOpenDialog
IFileOperation
IFolderView2
INPUT
IServiceProvider
IShellBrowser
IShellFolder
IShellItem
IShellItemImageFactory
IShellView
IShellWindows
IUIAutomation
IUIAutomationElement
LoadImage
MODIFIERKEYS_FLAGS
MONITOR_DPI_TYPE
MONITORINFOEXW
MONITORINFOF_PRIMARY
MSLLHOOKSTRUCT
NIN_SELECT
NOTIFY_FOR_THIS_SESSION
NOTIFY_ICON_MESSAGE
NOTIFYICON_VERSION_4
NOTIFYICONDATAW
POINTL
PostMessage
PostThreadMessage
RegisterDragDrop
RegisterHotKey
RegisterWindowMessage
ReleaseDC
ReleaseStgMedium
RevokeDragDrop
SendInput
SET_WINDOW_POS_FLAGS
SetForegroundWindow
SetWindowLongPtr
SetWindowPos
SetWindowRgn
SetWindowsHookEx
SetWinEventHook
SFGAO_FLAGS
SHCreateItemFromParsingName
SHDoDragDrop
Shell_NotifyIcon
ShellWindows
SHGetDesktopFolder
SHOW_WINDOW_CMD
SHQueryUserNotificationState
SID_STopLevelBrowser
SIGDN
SIIGBF
STGMEDIUM
SYSTEM_METRICS_INDEX
TRACK_POPUP_MENU_FLAGS
TrackPopupMenuEx
TYMED
UIA_CONTROLTYPE_ID
UnhookWinEvent
UnregisterHotKey
WINDOW_EX_STYLE
WINDOW_LONG_PTR_INDEX
WINDOW_STYLE
WindowFromPoint
WINDOWPOS
WINDOWS_HOOK_ID
WINEVENT_OUTOFCONTEXT
WM_APP
WM_CONTEXTMENU
WM_LBUTTONDOWN
WM_MOUSEMOVE
WM_NULL
WM_QUIT
WM_RBUTTONDOWN
WM_RBUTTONUP
WM_WTSSESSION_CHANGE
WTS_SESSION_UNLOCK
WTSRegisterSessionNotification
WTSUnRegisterSessionNotification
SHChangeNotifyRegister
SHChangeNotifyDeregister
SHChangeNotifyEntry
SHCNE_ID
SHCNRF_SOURCE
SHGetKnownFolderIDList
FOLDERID_RecycleBinFolder
CoTaskMemFree
RegNotifyChangeKeyValue
REG_NOTIFY_FILTER
RegisterClipboardFormat
FILEGROUPDESCRIPTORW
GlobalLock
GlobalUnlock
GetMessageTime
GlobalSize
CreateFile
RegisterDeviceNotification
UnregisterDeviceNotification
DEV_BROADCAST_HANDLE
DEV_BROADCAST_HDR
IsWindow
GetAsyncKeyState
IPersistFile
IShellLinkW
ShellLink
SHLoadIndirectString
```
`src/NeoFences.Shell/GameScanners.cs`:
```csharp
using System.Xml.Linq;
using Windows.Win32;
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

    /// <param name="previous">The last scan's games: a program found then is reused while it is still there, instead of searching
    /// every game folder again (M13b: 300+ games).</param>
    public static IReadOnlyList<SourceScan> ScanAll(LibrarySettings settings, Action<string, Exception> logFailure, IReadOnlyList<GameEntry>? previous = null)
    {
        var remembered = new Dictionary<string, GameEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in previous ?? []) remembered.TryAdd(game.Id, game);
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
        if (sources.Steam) Run("steam", () => ScanSteam(remembered));
        if (sources.Epic) Run("epic", () => [ScanEpic(logFailure)]);
        if (sources.Gog) Run("gog", () => [ScanGog()]);
        if (sources.Ubisoft) Run("ubisoft", () => [ScanUbisoft(remembered)]);
        if (sources.Ea) Run("ea", () => [ScanUninstallEntries("ea", GameSource.Ea, "Electronic Arts", ["EA app", "Origin", "EA Desktop"], remembered)]);
        if (sources.BattleNet) Run("battlenet", () => [ScanUninstallEntries("battlenet", GameSource.BattleNet, "Blizzard Entertainment", ["Battle.net"], remembered)]);
        if (sources.Xbox) Run("xbox", () => [ScanXbox(logFailure)]);
        if (sources.Folders)
        {
            foreach (var folder in settings.Folders) Run(FolderKey(folder), () => [ScanGameFolder(folder, remembered)]);
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

    /// <summary>A file an earlier scan found for this game (its program or icon), while it is still there (M13b).</summary>
    private static string? StillThere(IReadOnlyDictionary<string, GameEntry> remembered, string id, Func<GameEntry, string?> pick) =>
        remembered.TryGetValue(id, out var game) && pick(game) is { Length: > 0 } path && File.Exists(path) ? path : null;

    public static string FolderKey(string folder) => "folder:" + folder.Trim().TrimEnd('\\', '/').ToLowerInvariant();

    // --- Steam -------------------------------------------------------------------------------------------------------

    private static IEnumerable<SourceScan> ScanSteam(IReadOnlyDictionary<string, GameEntry> remembered)
    {
        var steamPath = SteamPath();
        if (steamPath is null) return [new SourceScan("steam", true, [])]; // Steam not installed
        if (!Directory.Exists(steamPath)) return [new SourceScan("steam", false, [])]; // its drive is not there (yet): keep its games (final review I1)
        var steam = Path.GetFullPath(steamPath);
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
            try
            {
                var games = new List<GameEntry>();
                foreach (var manifest in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
                {
                    if (SteamFiles.ParseManifest(File.ReadAllText(manifest)) is not { } app) continue;
                    var installFolder = Path.Combine(steamapps, "common", app.InstallDir);
                    games.Add(new GameEntry($"steam:{app.AppId}", app.Name, GameSource.Steam, key, new GameLaunch(SteamFiles.LaunchUri(app.AppId)),
                        installFolder, SteamPoster(steam, app.AppId), StillThere(remembered, $"steam:{app.AppId}", game => game.IconPath) ?? ProgramIn(installFolder)));
                }
                scans.Add(new SourceScan(key, true, games));
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // A manifest Steam is rewriting right now (the watcher fires then): this library keeps its games this time.
                scans.Add(new SourceScan(key, false, []));
            }
        }
        return scans;
    }

    /// <summary>Steam's folder as the registry names it (it may be on a drive that is not there), or null when Steam is not installed.</summary>
    private static string? SteamPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        return key?.GetValue("SteamPath") is string { Length: > 0 } path ? path : null;
    }

    private static string? SteamRoot() => SteamPath() is { } path && Directory.Exists(path) ? Path.GetFullPath(path) : null;

    /// <summary>
    /// An install folder that is gone while its drive is there: an uninstalled game left in a launcher's list (skip it).
    /// A drive that is not there: unknown (the caller keeps the source's games). Final review I3.
    /// </summary>
    private static bool? InstalledAt(string folder)
    {
        if (Directory.Exists(folder)) return true;
        return Path.GetPathRoot(folder) is { Length: > 0 } root && Directory.Exists(root) ? false : null;
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

    private static SourceScan ScanEpic(Action<string, Exception> logFailure)
    {
        if (!Directory.Exists(EpicManifests)) return new SourceScan("epic", true, []);
        var games = new List<GameEntry>();
        var readable = true;
        foreach (var manifest in Directory.EnumerateFiles(EpicManifests, "*.item"))
        {
            try
            {
                if (EpicManifest.Parse(File.ReadAllText(manifest)) is not { } game) continue;
                var installed = InstalledAt(game.InstallLocation);
                readable &= installed is not null;
                if (installed != true) continue;
                var program = game.LaunchExecutable is { } executable ? Path.Combine(game.InstallLocation, executable) : ProgramIn(game.InstallLocation);
                games.Add(new GameEntry($"epic:{game.AppName}", game.DisplayName, GameSource.Epic, "epic", new GameLaunch(game.LaunchUri),
                    game.InstallLocation, Poster: null, IconPath: program is not null && File.Exists(program) ? program : null));
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // A manifest Epic is rewriting (an update) or a folder going away mid-scan: Epic keeps its games this time, like
                // a Steam library (M13a review M2) — no tile flickers away and back.
                logFailure(manifest, failure);
                readable = false;
            }
        }
        return new SourceScan("epic", readable, games);
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
        var readable = true;
        foreach (var id in games.GetSubKeyNames())
        {
            using var game = games.OpenSubKey(id);
            if (game?.GetValue("gameName") is not string name || game.GetValue("path") is not string folder || game.GetValue("exe") is not string exe) continue;
            var installed = InstalledAt(folder);
            readable &= installed is not null;
            if (installed != true) continue;
            var launch = galaxy is not null
                ? new GameLaunch(galaxy, $"/command=runGame /gameId={id} /path=\"{folder}\"", Path.GetDirectoryName(galaxy))
                : new GameLaunch(exe, game.GetValue("launchParam") as string is { Length: > 0 } parameters ? parameters : null,
                    game.GetValue("workingDir") as string is { Length: > 0 } working ? working : Path.GetDirectoryName(exe));
            found.Add(new GameEntry($"gog:{id}", name, GameSource.Gog, "gog", launch, folder, Poster: null, IconPath: File.Exists(exe) ? exe : null));
        }
        return new SourceScan("gog", readable, found);
    }

    // --- Ubisoft Connect ---------------------------------------------------------------------------------------------

    private static SourceScan ScanUbisoft(IReadOnlyDictionary<string, GameEntry> remembered)
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
                folder, Poster: null, IconPath: StillThere(remembered, $"ubisoft:{id}", game => game.IconPath) ?? ProgramIn(folder)));
        }
        return new SourceScan("ubisoft", true, found);
    }

    // --- EA app, Battle.net (their games' uninstall entries) ---------------------------------------------------------

    private static SourceScan ScanUninstallEntries(string scanKey, GameSource source, string publisher, string[] launcherNames, IReadOnlyDictionary<string, GameEntry> remembered)
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
                var id = $"{scanKey}:{name.ToLowerInvariant()}";
                if (entry.GetValue("InstallLocation") is not string folder || !Directory.Exists(folder)
                    || (StillThere(remembered, id, game => game.Launch.Target) ?? ProgramIn(folder)) is not { } program) continue;
                found.Add(new GameEntry(id, displayName, source, scanKey, new GameLaunch(program, WorkingFolder: Path.GetDirectoryName(program)),
                    folder.TrimEnd('\\'), Poster: null, IconPath: program));
            }
        }
        return new SourceScan(scanKey, true, found);
    }

    // --- Xbox / Microsoft Store --------------------------------------------------------------------------------------

    private static SourceScan ScanXbox(Action<string, Exception> logFailure)
    {
        using var packages = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
        if (packages is null) return new SourceScan("xbox", true, []);
        var found = new List<GameEntry>();
        foreach (var fullName in packages.GetSubKeyNames())
        {
            try
            {
                if (XboxGame(packages, fullName) is { } game) found.Add(game);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                logFailure(fullName, failure); // one broken package (a damaged manifest): that package only (M13a)
            }
        }
        return new SourceScan("xbox", true, found);
    }

    private static GameEntry? XboxGame(RegistryKey packages, string fullName)
    {
            using var package = packages.OpenSubKey(fullName);
            if (package?.GetValue("PackageRootFolder") is not string folder) return null;
            var gameConfig = Path.Combine(folder, "MicrosoftGame.config");
            if (!File.Exists(gameConfig)) return null; // only game packages have one
            var visuals = XDocument.Load(gameConfig).Descendants().FirstOrDefault(element => element.Name.LocalName == "ShellVisuals");
            var appId = XDocument.Load(Path.Combine(folder, "AppxManifest.xml")).Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Application")?.Attribute("Id")?.Value;
            if (appId is null) return null;
            var name = DisplayName(visuals?.Attribute("DefaultDisplayName")?.Value, fullName)
                       ?? DisplayName(package.GetValue("DisplayName") as string, fullName)
                       ?? fullName.Split('_')[0];
            var parts = fullName.Split('_');
            var familyName = $"{parts[0]}_{parts[^1]}";
            var logo = (visuals?.Attribute("Square480x480Logo") ?? visuals?.Attribute("Square150x150Logo") ?? visuals?.Attribute("StoreLogo"))?.Value;
            return new GameEntry($"xbox:{familyName.ToLowerInvariant()}", name, GameSource.Xbox, "xbox",
                new GameLaunch(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"shell:AppsFolder\\{familyName}!{appId}"),
                folder, Poster: null, IconPath: logo is null ? null : ScaledAsset(folder, logo));
    }

    /// <summary>
    /// A package's name as people see it: plain text as is; "ms-resource:…" and "@{…}" looked up in the package's own
    /// resources (Game Pass titles carry only those; M13b), so the library never shows "Microsoft.624F8B84B80".
    /// </summary>
    private static unsafe string? DisplayName(string? value, string packageFullName)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string indirect;
        if (value.StartsWith('@')) indirect = value;
        else if (value.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
        {
            var resource = value["ms-resource:".Length..];
            var packageName = packageFullName.Split('_')[0];
            var uri = resource.StartsWith("//", StringComparison.Ordinal) ? "ms-resource:" + resource
                : resource.StartsWith('/') ? $"ms-resource://{packageName}{resource}"
                : resource.Contains('/') ? $"ms-resource://{packageName}/{resource}"
                : $"ms-resource://{packageName}/Resources/{resource}";
            indirect = $"@{{{packageFullName}?{uri}}}";
        }
        else return value;
        var buffer = new char[512];
        fixed (char* source = indirect)
        fixed (char* output = buffer)
        {
            if (PInvoke.SHLoadIndirectString(source, output, (uint)buffer.Length).Failed) return null;
            var text = new string(output);
            return text.Length > 0 && !text.StartsWith("ms-resource", StringComparison.OrdinalIgnoreCase) ? text : null;
        }
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

    private static SourceScan ScanGameFolder(string folder, IReadOnlyDictionary<string, GameEntry> remembered)
    {
        var key = FolderKey(folder);
        if (!Directory.Exists(folder)) return new SourceScan(key, false, []); // a drive that is not there: keep its games
        var found = new List<GameEntry>();
        foreach (var game in Directory.EnumerateDirectories(folder))
        {
            var id = "folder:" + game.ToLowerInvariant();
            if ((StillThere(remembered, id, entry => entry.Launch.Target) ?? ProgramIn(game)) is not { } program) continue; // no program found: not listed
            found.Add(new GameEntry(id, Path.GetFileName(game), GameSource.Folder, key,
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
                launch, installFolder, Poster: null, IconPath: launch.IsLink ? null : launch.Target) { ShortcutFile = itemRef });
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
public static partial class LibraryWriter
{
    [System.Text.RegularExpressions.GeneratedRegex(@"^\.[0-9a-f]{32}\.(lnk|url)$")]
    private static partial System.Text.RegularExpressions.Regex TempName();

    public const string IndexFileName = "index.json";

    /// <summary>The saved index, or an empty one when missing or damaged (the next scan rebuilds it).</summary>
    public static LibraryState ReadIndex(string folder)
    {
        try
        {
            var path = Path.Combine(folder, IndexFileName);
            return File.Exists(path) ? LibraryFiles.Repair(ConfigJson.DeserializeLibrary(File.ReadAllText(path))) : new LibraryState();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return new LibraryState();
        }
    }

    /// <returns>The state actually on disk: an item whose file could not be written is left out (it is tried again next time).</returns>
    /// <param name="previous">The index before this plan: what is on disk when a write or delete fails (M13a).</param>
    /// <param name="hidden">The hidden games to remember in the index (M13b).</param>
    public static LibraryState Apply(string folder, IReadOnlyList<LibraryItem> previous, LibraryPlan plan, IReadOnlyList<HiddenEntry> hidden, Action<string, Exception> logFailure)
    {
        Directory.CreateDirectory(folder);
        // Temp files of a write a power cut interrupted: NeoFences' own names (".<32 hex>.lnk/.url"), never anything else (M13b).
        foreach (var stray in Directory.EnumerateFiles(folder, ".*").Where(path => TempName().IsMatch(Path.GetFileName(path))))
        {
            try { File.Delete(stray); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        var failedDeletes = new List<string>();
        foreach (var fileName in plan.Delete)
        {
            try
            {
                File.Delete(Path.Combine(folder, fileName)); // our own shortcut, listed in our index
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                logFailure(fileName, failure);
                failedDeletes.Add(fileName);
            }
        }
        var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lost = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toWrite = plan.Items.Where(item => plan.Write.Contains(item) || !File.Exists(Path.Combine(folder, item.FileName))); // also files deleted by hand
        foreach (var item in toWrite)
        {
            var path = Path.Combine(folder, item.FileName);
            var temp = Path.Combine(folder, $".{Guid.NewGuid():N}{Path.GetExtension(item.FileName)}");
            try
            {
                if (item.Game.ShortcutFile is { } source && File.Exists(source)
                    && Path.GetExtension(source).Equals(Path.GetExtension(item.FileName), StringComparison.OrdinalIgnoreCase))
                    File.Copy(source, temp, overwrite: true); // the user's own shortcut: its icon and "Run as administrator" stay (M13b)
                else ShellLinks.Write(temp, item.Game.Launch, item.Game.IconPath is { } icon && IsIconSource(icon) ? icon : null);
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                logFailure(item.FileName, failure);
                (File.Exists(path) ? failed : lost).Add(item.FileName); // no old file to fall back on: dropped from the index (M13b)
                try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        var state = LibraryFiles.Settle(previous, plan, failedWrites: failed, failedDeletes: failedDeletes, lostFiles: lost) with { Hidden = hidden };
        var index = Path.Combine(folder, IndexFileName);
        var indexTemp = index + ".tmp";
        // Written through to the disk before the swap, like config.json: a power cut never leaves an empty index (M13b).
        using (var stream = new FileStream(indexTemp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(ConfigJson.SerializeLibrary(state));
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
        File.Move(indexTemp, index, overwrite: true);
        return state;
    }

    /// <summary>Shortcut icons come from programs and icon files, not from PNG logos (those show on the tile instead).</summary>
    public static bool IsIconSource(string path) => Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".ico" or ".dll";
}
```
`src/NeoFences.Shell/FolderItems.cs`:
```csharp
using NeoFences.Core.Model;

namespace NeoFences.Shell;

/// <summary>The contents of a Portal's folder, and the facts sorting needs about any item (M4).</summary>
public static class FolderItems
{
    /// <summary>Visible entries (not Hidden or System, like Explorer), or null when the folder cannot be read (missing, offline, denied).</summary>
    public static IReadOnlyList<ItemInfo>? TryList(string folderPath)
    {
        try
        {
            return new DirectoryInfo(folderPath).EnumerateFileSystemInfos()
                .Where(DesktopItems.IsVisibleOnDesktop)
                .Select(Describe)
                .ToList();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Sorting facts for items of a desktop fence ("Sort by", one time). Special items sort as folders by name.</summary>
    public static IReadOnlyList<ItemInfo> Describe(IEnumerable<string> itemRefs) => itemRefs.Select(itemRef =>
    {
        if (itemRef.StartsWith("::", StringComparison.Ordinal))
            return new ItemInfo(itemRef, ShellItems.TryGetDisplayName(itemRef) ?? itemRef, IsFolder: true, TypeName: "", DateTimeOffset.MinValue);
        FileSystemInfo entry = Directory.Exists(itemRef) ? new DirectoryInfo(itemRef) : new FileInfo(itemRef);
        // Keep the caller's ref: Windows path normalisation drops trailing spaces/dots ("foo "), so FullName may differ
        // and the one-time sort would then not match the fence's items (M3 review I3).
        return Describe(entry) with { ItemRef = itemRef };
    }).ToList();

    // ponytail: type = extension, not the shell's type name (SHGetFileInfo per item); upgrade if users sort by type often.
    private static ItemInfo Describe(FileSystemInfo entry)
    {
        var isFolder = entry is DirectoryInfo;
        return new ItemInfo(entry.FullName, entry.Name, isFolder, isFolder ? "" : entry.Extension.ToUpperInvariant(),
            entry.Exists ? entry.LastWriteTime : DateTimeOffset.MinValue);
    }
}

/// <summary>Says when a folder's contents changed (a Portal re-lists it). Events arrive on thread-pool threads.</summary>
public sealed class FolderWatcher : IDisposable
{
    private readonly FileSystemWatcher? _watcher;
    private readonly FileSystemWatcher? _parentWatcher; // the folder itself renamed, moved or deleted (and back)

    public event Action? Changed;
    /// <summary>The watcher lost events or stopped (a drive going away, a network hiccup): re-list and re-arm, with backoff (M8d).</summary>
    public event Action? Failed;

    /// <summary>True once the watcher failed, even before anyone subscribed (it can fail as it arms, M8d review I2).</summary>
    public bool HasFailed => _hasFailed;
    private volatile bool _hasFailed;

    /// <summary>False when the folder itself could not be watched (missing, offline): the Portal then retries.</summary>
    public bool IsWatching => _watcher is not null;

    /// <summary>
    /// The folder this watcher holds open: the folder itself, or its parent while the folder is missing (M13a). A removal
    /// notice must be registered for it, or the open handle vetoes "Safely remove".
    /// </summary>
    public string? HeldFolder { get; private set; }

    /// <param name="logFailure">Told when the folder cannot be watched; the Portal then only refreshes on navigation.</param>
    /// <param name="directoriesOnly">Only folders appearing, going or renamed: a game folder whose games write files at their
    /// own root would otherwise rescan the library while they run (M13b).</param>
    public FolderWatcher(string folderPath, Action<Exception> logFailure, bool directoriesOnly = false)
    {
        try
        {
            _watcher = new FileSystemWatcher(folderPath)
            {
                NotifyFilter = directoriesOnly ? NotifyFilters.DirectoryName : NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                InternalBufferSize = 64 * 1024,
            };
            _watcher.Created += (_, _) => Changed?.Invoke();
            _watcher.Deleted += (_, _) => Changed?.Invoke();
            _watcher.Renamed += (_, _) => Changed?.Invoke();
            _watcher.Changed += (_, _) => Changed?.Invoke();
            _watcher.Error += (_, _) =>
            {
                _hasFailed = true;
                Failed?.Invoke(); // lost events or stopped: a full re-list covers them, not too often
            };
            _watcher.EnableRaisingEvents = true;
            HeldFolder = folderPath;
        }
        catch (Exception failure) when (failure is IOException or ArgumentException or UnauthorizedAccessException)
        {
            logFailure(failure);
        }
        // A watcher follows its directory when that is renamed and reports nothing, so the Portal would keep showing
        // a folder that is gone (M4 smoke). The parent tells when the folder itself goes away or comes back.
        try
        {
            var trimmed = folderPath.TrimEnd('\\', '/');
            if (Path.GetDirectoryName(trimmed) is { } parent && Directory.Exists(parent))
            {
                _parentWatcher = new FileSystemWatcher(parent, Path.GetFileName(trimmed))
                {
                    NotifyFilter = NotifyFilters.DirectoryName,
                    IncludeSubdirectories = false,
                };
                _parentWatcher.Created += (_, _) => Changed?.Invoke();
                _parentWatcher.Deleted += (_, _) => Changed?.Invoke();
                _parentWatcher.Renamed += (_, _) => Changed?.Invoke();
                _parentWatcher.EnableRaisingEvents = true;
                HeldFolder ??= parent;
            }
        }
        catch (Exception failure) when (failure is IOException or ArgumentException or UnauthorizedAccessException)
        {
            logFailure(failure);
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _parentWatcher?.Dispose();
    }
}
```
`src/NeoFences.Shell/ItemFactsReader.cs`:
```csharp
using System.Runtime.InteropServices;
using NeoFences.Core.Model;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace NeoFences.Shell;

/// <summary>
/// What a rule needs to know about desktop items (M11): name, type, size, date and a shortcut's target. Call on the shell
/// worker (an STA thread; a shortcut to an offline share can be slow). An item that cannot be read keeps only its name and
/// extension, so only name/type rules can match it; special items (the Recycle Bin, ::{…}) are left out.
/// </summary>
public static class ItemFactsReader
{
    public static IReadOnlyList<ItemFacts> Read(IReadOnlyList<string> itemRefs, Action<string, Exception> logFailure)
    {
        var facts = new List<ItemFacts>(itemRefs.Count);
        foreach (var itemRef in itemRefs.Where(itemRef => !itemRef.StartsWith("::", StringComparison.Ordinal)))
        {
            var name = Path.GetFileName(itemRef);
            var extension = Path.GetExtension(itemRef);
            try
            {
                if (Directory.Exists(itemRef))
                {
                    facts.Add(new ItemFacts(itemRef, name, "", IsFolder: true, SizeBytes: null, Directory.GetLastWriteTime(itemRef), ShortcutTarget: null));
                    continue;
                }
                var file = new FileInfo(itemRef);
                if (!file.Exists) continue; // gone meanwhile: nothing to file
                facts.Add(new ItemFacts(itemRef, name, extension, IsFolder: false, file.Length, file.LastWriteTime, ShortcutTarget(itemRef, extension)));
            }
            catch (Exception failure) when (failure is not OutOfMemoryException) // a bad shortcut must never stop the rest (hard rule 7)
            {
                logFailure(itemRef, failure);
                facts.Add(new ItemFacts(itemRef, name, extension, IsFolder: false, SizeBytes: null, Modified: null, ShortcutTarget: null));
            }
        }
        return facts;
    }

    /// <summary>A .url's URL= line, or a .lnk's target path and arguments; null for other files.</summary>
    private static string? ShortcutTarget(string path, string extension)
    {
        if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            var line = File.ReadLines(path).Take(64).FirstOrDefault(text => text.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)); // never a whole odd file (M13b)
            return line?[4..].Trim();
        }
        return extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? LinkTarget(path) : null;
    }

    private static unsafe string? LinkTarget(string path)
    {
        IShellLinkW? link = null;
        try
        {
            link = (IShellLinkW)Activator.CreateInstance(Type.GetTypeFromCLSID(typeof(ShellLink).GUID)!)!;
            fixed (char* file = path) ((IPersistFile)link).Load(file, STGM.STGM_READ);
            var buffer = new char[1024];
            fixed (char* text = buffer)
            {
                link.GetPath(text, buffer.Length, null, 0x4 /* SLGP_RAWPATH: no resolving, never a network wait */);
                var target = Environment.ExpandEnvironmentVariables(new string(text)); // raw paths keep %ProgramFiles% (M13b)
                link.GetArguments(text, buffer.Length);
                var arguments = new string(text);
                return arguments.Length == 0 ? target : $"{target} {arguments}";
            }
        }
        finally
        {
            if (link is not null) Marshal.ReleaseComObject(link);
        }
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
                if (handlers.AcceptsDrops?.Invoke() == false)
                {
                    WithImageHelper(helper => helper.DragLeave()); // no drag image left on screen (M13b)
                    return; // effect already "none"
                }
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
            if (handlers.AcceptsDrops is { } accepts)
            {
                // A tab header first: hovering one switches the box to that tab, also from the library tab (M13b).
                try { handlers.HitTest(pt.x, pt.y); } catch (Exception failure) when (failure is not OutOfMemoryException) { handlers.LogFailure(failure); }
            }
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
`src/NeoFences.Shell/DeviceRemovalNotice.cs`:
```csharp
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Shell;

/// <summary>
/// "Safely Remove" / Eject for a Portal's drive (M8d, M4 carry-over): any open handle on a drive vetoes its removal, and
/// a Portal keeps two (its folder watcher and its parent's). Windows asks every window registered for a handle on the
/// drive first (DBT_DEVICEQUERYREMOVE to the owner window); the Portal then closes everything it holds there.
/// </summary>
public sealed class DeviceRemovalNotice : IDisposable
{
    public const int WmDeviceChange = 0x0219;
    public const int QueryRemove = 0x8001; // DBT_DEVICEQUERYREMOVE

    private readonly SafeFileHandle _folder;
    private readonly HDEVNOTIFY _registration;

    /// <summary>The folder handle Windows names in its notice.</summary>
    public nint Handle { get; }

    private DeviceRemovalNotice(SafeFileHandle folder, HDEVNOTIFY registration)
    {
        _folder = folder;
        _registration = registration;
        Handle = folder.DangerousGetHandle();
    }

    /// <summary>
    /// Registers for removal requests of the drive holding this folder. Only local drives can be removed this way
    /// (USB sticks and disks, card readers, optical drives); null for network folders or when Windows refuses.
    /// </summary>
    public static unsafe DeviceRemovalNotice? TryRegister(nint ownerHandle, string folderPath, Action<Exception> logFailure)
    {
        try
        {
            var root = Path.GetPathRoot(folderPath);
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\", StringComparison.Ordinal)) return null; // UNC share
            if (new DriveInfo(root).DriveType is not (DriveType.Removable or DriveType.Fixed or DriveType.CDRom)) return null;
            // Attributes only, sharing everything: this handle never gets in the way of anything but the removal itself.
            var folder = PInvoke.CreateFile(folderPath, 0x80, // FILE_READ_ATTRIBUTES
                FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE | FILE_SHARE_MODE.FILE_SHARE_DELETE,
                null, FILE_CREATION_DISPOSITION.OPEN_EXISTING, FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS, null);
            if (folder.IsInvalid) throw new System.ComponentModel.Win32Exception();
            var filter = new DEV_BROADCAST_HANDLE
            {
                dbch_size = (uint)sizeof(DEV_BROADCAST_HANDLE),
                dbch_devicetype = (uint)DEV_BROADCAST_HDR_DEVICE_TYPE.DBT_DEVTYP_HANDLE,
                dbch_handle = (HANDLE)folder.DangerousGetHandle(),
            };
            var registration = PInvoke.RegisterDeviceNotification((HANDLE)ownerHandle, &filter, REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_WINDOW_HANDLE);
            if (registration.IsNull)
            {
                var failure = new System.ComponentModel.Win32Exception();
                folder.Dispose();
                throw failure;
            }
            return new DeviceRemovalNotice(folder, registration);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            logFailure(failure); // the drive then just cannot be removed safely while the Portal shows it (as before)
            return null;
        }
    }

    /// <summary>The handle a WM_DEVICECHANGE notice is about, or 0 when it is not a handle notice.</summary>
    public static unsafe nint HandleOf(nint lParam)
    {
        if (lParam == 0) return 0;
        var header = (DEV_BROADCAST_HDR*)lParam;
        return header->dbch_devicetype == DEV_BROADCAST_HDR_DEVICE_TYPE.DBT_DEVTYP_HANDLE ? ((DEV_BROADCAST_HANDLE*)lParam)->dbch_handle : 0;
    }

    private bool _disposed;

    /// <summary>Safe to call twice (a Portal can let go of an in-flight notice and then dispose it again, M13b).</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PInvoke.UnregisterDeviceNotification(_registration);
        _folder.Dispose();
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
    // Steam's / Epic's install lists and the game folders, each with a removal notice so "Safely remove" works (final review I2).
    private readonly List<(FolderWatcher Watcher, DeviceRemovalNotice? Notice)> _libraryWatchers = [];
    private DispatcherTimer? _libraryTimer;
    private bool _libraryActive, _libraryScanning, _libraryScanAgain, _libraryDeferred;
    private bool _libraryStopped; // NeoFences is exiting: a scan finishing now re-arms nothing (M13a)
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
        if (!_libraryActive || _libraryStopped) return;
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
        var noticeOwner = _messages.Handle;
        ShellWorker.RunAlone(() =>
        {
            try
            {
                void LogFailure(string what, Exception failure) => Log.Warning(failure, "game library: {What} could not be read", what);
                var scans = GameScanners.ScanAll(settings, LogFailure, previous: [.. previous.Items.Select(item => item.Game)]);
                var everything = GameCatalog.Merge(scans, previous.Items.Select(item => item.Game).ToList(), hidden: []);
                var hidden = settings.Hidden.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var games = everything.Where(game => !GameCatalog.IdsOf(game).Any(hidden.Contains)).ToList();
                var plan = LibraryFiles.Plan(previous.Items, games);
                var hiddenGames = GameCatalog.HiddenGames(everything, settings.Hidden, previous.Hidden); // names kept while a source is away (M13b)
                var state = LibraryWriter.Apply(AppPaths.LibraryDirectory, previous.Items, plan, hiddenGames,
                    logFailure: (file, failure) => Log.Warning(failure, "game library: {File} could not be written", file));
                var unreadable = scans.Where(scan => !scan.Readable).Select(scan => scan.ScanKey).ToList();
                // Built here, off the UI thread: opening a watcher on a sleeping disk or a network share can take seconds (final review I2).
                var watch = BuildLibraryWatchers(GameScanners.WatchFolders(settings), noticeOwner, gameFolders: settings.Folders);
                Log.Information("game library: {Count} games ({Written} written, {Removed} removed); unreadable: {Unreadable}",
                    state.Items.Count, plan.Write.Count, plan.Delete.Count, unreadable);
                dispatcher.BeginInvoke(() => OnLibraryScanned(state, unreadable, watch));
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                Log.Warning(failure, "game library scan failed; the library keeps its games"); // hard rule 7
                dispatcher.BeginInvoke(() => OnLibraryScanned(null, ["library"], []));
            }
        }, name: "NeoFences game library");
    }

    private void OnLibraryScanned(LibraryState? state, IReadOnlyList<string> unreadable,
        IReadOnlyList<(FolderWatcher Watcher, DeviceRemovalNotice? Notice)> watch)
    {
        _libraryScanning = false;
        if (_libraryStopped)
        {
            DisposeWatchers(watch);
            return;
        }
        if (state is not null) _library = state;
        _libraryStatus = $"Last scan {DateTime.Now:HH:mm}: {_library.Items.Count} games"
                         + (unreadable.Count > 0 ? $"; not readable right now: {string.Join(", ", unreadable.Select(GameCatalog.SourceName))}" : ".");
        if (state is null || !_libraryActive) DisposeWatchers(watch); // a failed scan keeps the watchers it had (final review I2)
        else ReplaceLibraryWatchers(watch);
        foreach (var window in _windows.Values.Where(window => window.IsLibrary)) RefreshPortal(window); // new art, order
        RefreshSettings();
        if (!_libraryScanAgain) return;
        _libraryScanAgain = false;
        ScanLibrary();
    }

    /// <summary>A watcher per folder, with a removal notice where Windows offers one. Runs on the scan thread.</summary>
    private static List<(FolderWatcher Watcher, DeviceRemovalNotice? Notice)> BuildLibraryWatchers(IReadOnlyList<string> folders, nint noticeOwner,
        IReadOnlyList<string> gameFolders) =>
        folders.Select(folder =>
        {
            // A game folder: only games (sub-folders) coming or going; Steam's and Epic's lists: every file (M13b).
            var watcher = new FolderWatcher(folder, failure => Log.Warning(failure, "game library: cannot watch {Folder}", folder),
                directoriesOnly: gameFolders.Contains(folder, StringComparer.OrdinalIgnoreCase));
            var notice = watcher.HeldFolder is { } held
                ? DeviceRemovalNotice.TryRegister(noticeOwner, held, failure => Log.Debug(failure, "game library: no removal notice for {Folder}", held))
                : null;
            return (watcher, notice);
        }).ToList();

    /// <summary>A change rescans after 5 quiet seconds (downloads write a lot); a watcher that stops (a network hiccup) rescans too, which re-arms it.</summary>
    private void ReplaceLibraryWatchers(IReadOnlyList<(FolderWatcher Watcher, DeviceRemovalNotice? Notice)> watch)
    {
        StopLibraryWatchers();
        var dispatcher = Dispatcher.CurrentDispatcher;
        foreach (var (watcher, notice) in watch)
        {
            watcher.Changed += () => dispatcher.BeginInvoke(ScheduleLibraryScan);
            watcher.Failed += () => dispatcher.BeginInvoke(ScheduleLibraryScan);
            if (watcher.HasFailed) ScheduleLibraryScan();
            _libraryWatchers.Add((watcher, notice));
        }
    }

    /// <summary>Windows asks to remove a drive the library watches (a USB Steam library): let go of it now (final review I2).</summary>
    private bool ReleaseLibraryForRemoval(nint handle)
    {
        var index = _libraryWatchers.FindIndex(entry => entry.Notice?.Handle == handle);
        if (index < 0) return false;
        DisposeWatchers([_libraryWatchers[index]]);
        _libraryWatchers.RemoveAt(index);
        ScheduleLibraryScan(); // re-arms what is still there once the removal is done (or refused)
        return true;
    }

    private static void DisposeWatchers(IEnumerable<(FolderWatcher Watcher, DeviceRemovalNotice? Notice)> watch)
    {
        foreach (var (watcher, notice) in watch)
        {
            watcher.Dispose();
            notice?.Dispose();
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
        DisposeWatchers(_libraryWatchers);
        _libraryWatchers.Clear();
    }

    /// <summary>The library's shortcuts in catalog (A–Z) order; files NeoFences did not write (its index) are not shown.</summary>
    private IReadOnlyList<string> LibraryOrder(IReadOnlyList<ItemInfo> listed)
    {
        var position = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (item, index) in _library.Items.Select((item, index) => (item, index))) position.TryAdd(item.FileName, index); // never throws on the UI thread
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
        SetQuickHidden(false); // a new fence must show (as "New fence" does)
        var fence = Fence.Create("Games", FenceSource.Library) with { Sort = FenceSort.Name, IconSize = 64, Labels = _config.Settings.DefaultLabels };
        _config = _config with { Fences = [.. _config.Fences, fence] };
        Log.Information("Game Library fence created");
        SyncBoxes();
        SaveNow();
        if (_config.Library.Folders.Count > 0) return;
        // Spec §4: D:\GameLibrary when it exists — checked off the UI thread (a sleeping disk can take seconds, M13a).
        Task.Run(() => Directory.Exists(DefaultGameFolder)).ContinueWith(found =>
        {
            if (!found.Result || _config.Library.Folders.Count > 0) return;
            _config = _config with { Library = _config.Library with { Folders = [DefaultGameFolder] } };
            SaveNow();
            ScanLibrary();
            RefreshSettings();
        }, TaskScheduler.FromCurrentSynchronizationContext());
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

    private const string DefaultGameFolder = @"D:\GameLibrary";

    private void OpenInstallFolder(FenceWindow window, string itemRef)
    {
        // Opened on its own thread; a missing folder is logged there (no disk check on the UI thread, M13a).
        if (LibraryItemOf(itemRef)?.Game.InstallFolder is { } folder) OpenItem(folder, ownerHandle: window.Handle);
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
            var ids = (_library.Hidden.FirstOrDefault(game => game.Ids.Contains(id, StringComparer.OrdinalIgnoreCase))?.Ids ?? [id]).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Change(library => library with { Hidden = [.. library.Hidden.Where(hiddenId => !ids.Contains(hiddenId))] }, "show again");
        };
        window.RefreshLibraryRequested += ScanLibrary;
    }

    /// <summary>One row per hidden game (all its ids together); a hidden id no scan finds any more is listed by itself.</summary>
    private IReadOnlyList<HiddenGame> HiddenGamesForSettings()
    {
        var hidden = _config.Library.Hidden.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. GameCatalog.HiddenGames([], _config.Library.Hidden, _library.Hidden)
            .Select(entry => new HiddenGame(entry.Ids.FirstOrDefault(hidden.Contains) ?? entry.Ids[0], entry.Name, entry.Ids))];
    }
}
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
        // Decoded at the tile's pixel width, not more: 300 covers would otherwise hold ~160 MB (M13b).
        var decodeWidth = Math.Max(48, (int)Math.Round(Math.Round(_iconSizeDips * 1.5) * VisualTreeHelper.GetDpi(this).DpiScaleX));
        Task.Run(() => LoadArt(art.Path, decodeWidth)).ContinueWith(loaded =>
        {
            if (view.ArtPath != art.Path || loaded.Result is not { } image) return;
            view.Art = image;
            view.ArtKind = kind;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static System.Windows.Media.ImageSource? LoadArt(string path, int decodeWidth)
    {
        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; // the file is not kept open
            image.DecodePixelWidth = decodeWidth;
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
`src/NeoFences.App/PortalState.cs`:
```csharp
using System.IO;
using System.Windows.Threading;
using NeoFences.Core.Model;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>
/// One Portal fence at runtime (M4): the folder it mirrors, the subfolder it shows now (browsing is not saved; a restart
/// shows the Portal's own folder again), and its watcher. Listing and watcher setup run off the UI thread, since a
/// network or USB folder can block for seconds (M4 review I1); bursts of changes become one re-list at most every
/// 250 ms, even while a file keeps being written (I2); an unavailable or unwatched folder is retried every few
/// seconds, so a Portal comes back when its drive does (I4).
/// </summary>
public sealed class PortalState : IDisposable
{
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(7);

    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _retryTimer;
    private readonly Action<IReadOnlyList<ItemInfo>?> _show;
    private readonly Action<Exception> _logFailure;
    private FolderWatcher? _watcher;
    private DeviceRemovalNotice? _removal;   // asks before the Portal's drive is removed (USB stick, M8d)
    private readonly nint _noticeOwner;
    private volatile bool _noticeFailureLogged;
    private readonly DispatcherTimer _backoffTimer;
    private TimeSpan _failureDelay = NeoFences.Core.Lifecycle.WatcherBackoff.First;
    private DateTime _lastArm = DateTime.MinValue;
    private HashSet<string> _listedFolders = new(StringComparer.OrdinalIgnoreCase);
    private int _generation;
    private bool _refreshing;   // a listing is running in the background (a network folder may take seconds)
    private bool _disposed;
    private bool _paused;        // game mode (M6a): changes wait
    private bool _missedChanges; // something changed while paused: re-list on resume
    // Listings in flight hold a watcher and a notice of their own until the UI takes them: those can be let go as well (M13b).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<nint, (FolderWatcher Watcher, DeviceRemovalNotice Notice)> _inFlight = new();
    private DateTime _releasedUntil = DateTime.MinValue; // just let go of a drive being removed: no re-opening for a moment (M13a)
    private static readonly TimeSpan ReleaseGrace = TimeSpan.FromSeconds(5);

    public string Root { get; }

    public string Current { get; private set; }

    public bool CanGoBack => !string.Equals(Current, Root, StringComparison.OrdinalIgnoreCase);

    /// <param name="show">Called on the UI thread with the shown folder's items, or null when it cannot be read.</param>
    /// <param name="noticeOwner">The window that receives "may this drive be removed?" (the app's message window).</param>
    public PortalState(string root, nint noticeOwner, Action<IReadOnlyList<ItemInfo>?> show, Action<Exception> logFailure)
    {
        _noticeOwner = noticeOwner;
        _backoffTimer = new DispatcherTimer();
        _backoffTimer.Tick += (_, _) =>
        {
            _backoffTimer.Stop();
            if (_paused) _missedChanges = true; // re-listed when the game ends (M8d review I1)
            else Refresh();
        };
        Root = Normalize(root);
        Current = Root;
        _show = show;
        _logFailure = logFailure;
        _refreshTimer = new DispatcherTimer { Interval = RefreshDelay };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            Refresh();
        };
        _retryTimer = new DispatcherTimer { Interval = RetryDelay };
        _retryTimer.Tick += (_, _) => { if (!_refreshing && !_paused) Refresh(); }; // never pile up blocked listings
        Refresh();
    }

    /// <summary>"Downloads › Mods › Old" while browsing below the Portal's folder.</summary>
    public string Breadcrumb(string title)
    {
        var below = Path.GetRelativePath(Root, Current);
        return below == "." ? title : title + " › " + below.Replace("\\", " › ");
    }

    /// <summary>True when the last listing showed this ref as a folder (no disk access: a network folder may block).</summary>
    public bool IsListedFolder(string itemRef) => _listedFolders.Contains(itemRef);

    /// <summary>Shows a subfolder of the Portal (double-click on a folder inside it).</summary>
    public void Browse(string folder)
    {
        Current = Normalize(folder);
        Refresh();
    }

    /// <summary>One level up, never above the Portal's own folder.</summary>
    public void Back()
    {
        if (!CanGoBack) return;
        Current = Path.GetDirectoryName(Current) is { } parent ? Normalize(parent) : Root;
        Refresh();
    }

    /// <summary>
    /// Re-arms the watcher and re-lists the shown folder in the background; only the newest request is shown. The
    /// watcher is re-created each time: a folder deleted and created again ends the old one.
    /// </summary>
    public void Refresh()
    {
        if (_disposed) return;
        if (DateTime.UtcNow < _releasedUntil)
        {
            _retryTimer.Start(); // a refresh queued just before the release would re-open the drive: the retry comes back later
            return;
        }
        _refreshing = true;
        var generation = ++_generation;
        var folder = Current;
        var dispatcher = _refreshTimer.Dispatcher;
        Task.Run(() =>
        {
            var watcher = new FolderWatcher(folder, _logFailure);
            var removal = watcher.HeldFolder is { } held ? DeviceRemovalNotice.TryRegister(_noticeOwner, held, LogNoticeFailureOnce) : null;
            if (removal is not null) _inFlight[removal.Handle] = (watcher, removal);
            var items = FolderItems.TryList(folder);
            dispatcher.BeginInvoke(() =>
            {
                if (removal is not null) _inFlight.TryRemove(removal.Handle, out _);
                if (generation == _generation) _refreshing = false;
                if (_disposed || generation != _generation)
                {
                    watcher.Dispose(); // a newer refresh (or the fence's deletion) superseded this one
                    removal?.Dispose();
                    return;
                }
                _watcher?.Dispose();
                _watcher = watcher;
                _removal?.Dispose();
                _removal = removal;
                _lastArm = DateTime.UtcNow;
                watcher.Changed += () => dispatcher.BeginInvoke(ScheduleRefresh);
                watcher.Failed += () => dispatcher.BeginInvoke(OnWatcherFailed);
                if (watcher.HasFailed) OnWatcherFailed(); // it failed while arming, before this subscription (M8d review I2)
                _listedFolders = items is null
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(items.Where(item => item.IsFolder).Select(item => item.ItemRef), StringComparer.OrdinalIgnoreCase);
                // Unreadable or unwatched (drive not there yet): try again every few seconds until it is.
                if (items is null || !watcher.IsWatching) _retryTimer.Start();
                else _retryTimer.Stop();
                _show(items);
            });
        });
    }

    /// <summary>Game mode (spec §4.7): while paused, folder changes only mark the Portal stale; resuming re-lists once.</summary>
    public void SetPaused(bool paused)
    {
        _paused = paused;
        if (paused || !_missedChanges) return;
        _missedChanges = false;
        Refresh();
    }

    /// <summary>
    /// Windows asks to remove the drive holding this handle: close everything the Portal holds there, so "Safely Remove"
    /// and Eject work while it is shown (M8d). The Portal shows the folder as unavailable and comes back by itself when
    /// the drive does, or the removal was refused (the 7 s retry timer).
    /// </summary>
    public bool ReleaseForRemoval(nint handle)
    {
        if (_inFlight.TryRemove(handle, out var flying))
        {
            // A listing still on its way holds the drive: let go now; when it arrives it is stale and dropped (M13b).
            ++_generation;
            _refreshing = false;
            flying.Watcher.Dispose();
            flying.Notice.Dispose();
            _releasedUntil = DateTime.UtcNow + ReleaseGrace;
            _show(null);
            _retryTimer.Start();
            return true;
        }
        if (_removal is null || _removal.Handle != handle) return false;
        ++_generation; // a listing in flight must not re-open the folder
        _refreshing = false;
        _refreshTimer.Stop();
        _backoffTimer.Stop();
        _watcher?.Dispose();
        _watcher = null;
        _removal.Dispose();
        _removal = null;
        _listedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _releasedUntil = DateTime.UtcNow + ReleaseGrace;
        _show(null);
        _retryTimer.Start(); // every 7 s, after the grace: back when the drive is, or when the removal was refused
        return true;
    }

    /// <summary>A drive that refuses removal notices (a virtual drive reporting "Fixed") is said once, not on every refresh (M8d review).</summary>
    private void LogNoticeFailureOnce(Exception failure)
    {
        if (_noticeFailureLogged) return;
        _noticeFailureLogged = true;
        _logFailure(failure);
    }

    /// <summary>A watcher that fails again soon after each re-arm waits longer each time, up to a minute (M8d, M4 review).</summary>
    private void OnWatcherFailed()
    {
        if (_disposed || _backoffTimer.IsEnabled) return;
        if (_paused)
        {
            _missedChanges = true; // a stopped watcher during a game: re-list (and re-arm) when it ends (M8d review I1)
            return;
        }
        _failureDelay = NeoFences.Core.Lifecycle.WatcherBackoff.Next(_failureDelay, lastRearm: _lastArm, failureAt: DateTime.UtcNow);
        _backoffTimer.Interval = _failureDelay;
        _backoffTimer.Start();
        Serilog.Log.Information("Portal folder watcher stopped ({Folder}); re-listing after {Delay}", Current, _failureDelay);
    }

    private void ScheduleRefresh()
    {
        if (_paused)
        {
            _missedChanges = true;
            return;
        }
        // Not restarted by every event: a file that keeps being written still refreshes the Portal every 250 ms.
        if (!_disposed && !_refreshTimer.IsEnabled) _refreshTimer.Start();
    }

    /// <summary>A path without a trailing separator, except a drive root ("D:\").</summary>
    private static string Normalize(string folder)
    {
        var trimmed = folder.TrimEnd('\\', '/');
        return trimmed.Length == 2 && trimmed[1] == ':' ? trimmed + "\\" : trimmed;
    }

    public void Dispose()
    {
        _disposed = true;
        _refreshTimer.Stop();
        _retryTimer.Stop();
        _backoffTimer.Stop();
        _watcher?.Dispose();
        _watcher = null;
        _removal?.Dispose();
        _removal = null;
    }
}
```
`src/NeoFences.App/SettingsWindow.Rules.cs`:
```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>A desktop fence a rule can put items in (M11), by title.</summary>
public sealed record RuleFence(string Id, string Title)
{
    public override string ToString() => Title; // what Narrator reads for the item
}

/// <summary>One row of the Rules list (M11): "Images → Pictures", greyed when off or its fence is gone.</summary>
public sealed record RuleRow(string Id, bool Enabled, string Text, bool FenceMissing)
{
    public string Spoken => Enabled ? Text : $"{Text}, off";
    public string ToggleName => $"Use the rule {Text}";
    public double Opacity => Enabled && !FenceMissing ? 1 : 0.55;
}

/// <summary>
/// Settings → Rules (M11, spec §4): an ordered list (first match wins) and an editor under it. Every change goes to the
/// host as the whole new list; the host saves and shows it back.
/// </summary>
public partial class SettingsWindow
{
    public event Action<IReadOnlyList<Rule>>? RulesChanged;
    public event Action? ApplyRulesRequested;

    private IReadOnlyList<Rule> _rules = [];
    private IReadOnlyList<RuleFence> _ruleFences = [];
    private bool _ruleEditorOpen;
    private string? _editingRuleId; // null while adding
    private string? _selectRuleAfterUpdate;

    private void InitializeRules()
    {
        AddRuleButton.Click += (_, _) => BeginRuleEdit(rule: null, fenceId: null);
        EditRuleButton.Click += (_, _) => { if (SelectedRule is { } rule) BeginRuleEdit(rule, rule.FenceId); };
        DeleteRuleButton.Click += (_, _) => { if (SelectedRule is { } rule) ReportRules([.. _rules.Where(candidate => candidate.Id != rule.Id)]); };
        MoveRuleUpButton.Click += (_, _) => MoveSelectedRule(-1);
        MoveRuleDownButton.Click += (_, _) => MoveSelectedRule(+1);
        ApplyRulesButton.Click += (_, _) =>
        {
            ShowRulesResult("Reading your Desktop items…"); // the button stays off until the host answers
            ApplyRulesRequested?.Invoke();
        };
        RuleList.SelectionChanged += (_, _) => UpdateRuleButtons();
        RuleKindBox.SelectionChanged += (_, _) => ShowConditionFields(condition: null);
        RuleChoiceBox.SelectionChanged += (_, _) => UpdateTextField();
        SaveRuleButton.Click += (_, _) => SaveRule();
        CancelRuleButton.Click += (_, _) => EndRuleEdit();
        RuleBrowseButton.Click += (_, _) =>
        {
            var folder = FolderPicker.TryPick(new WindowInteropHelper(this).Handle, "Choose the folder your games are in",
                logFailure: failure => Log.Warning(failure, "rules: the folder picker failed"));
            if (folder is not null) RuleTextBox.Text = folder;
        };
        RuleEditor.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter) SaveRule();
            else if (key.Key == Key.Escape) EndRuleEdit();
            else return;
            key.Handled = true;
        };
        AutomationProperties.SetHelpText(RuleList, RulesDescription.Text);
    }

    private Rule? SelectedRule => RuleList.SelectedItem is RuleRow row ? _rules.FirstOrDefault(rule => rule.Id == row.Id) : null;

    private RuleKind SelectedKind => RuleKindBox.SelectedItem is ComboBoxItem { Tag: string kind } ? Enum.Parse<RuleKind>(kind) : RuleKind.Type;

    private object? SelectedChoice => (RuleChoiceBox.SelectedItem as ComboBoxItem)?.Tag;

    /// <summary>Fence menu → "Rules for this fence…": the editor opens with a new rule for that fence.</summary>
    public void BeginNewRule(string fenceId)
    {
        if (_ruleEditorOpen)
        {
            // A rule is being edited: keep it (M13b); the user finishes or cancels it first.
            ShowRuleProblem("Finish or cancel the rule you are editing first.");
            Dispatcher.BeginInvoke(() => RulesCard.BringIntoView(), DispatcherPriority.Loaded);
            return;
        }
        BeginRuleEdit(rule: null, fenceId);
        Dispatcher.BeginInvoke(() => RulesCard.BringIntoView(), DispatcherPriority.Loaded); // after the first layout
    }

    /// <summary>The host's answer to "Apply rules now" (also read out by Narrator). A message ending in "…" is still working.</summary>
    public void ShowRulesResult(string message)
    {
        ApplyRulesButton.IsEnabled = !message.EndsWith('…');
        RulesStatus.Text = message;
        RulesStatus.Visibility = Visibility.Visible;
        System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(RulesStatus)
            .RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }

    private void ShowRules(SettingsView view)
    {
        _rules = view.Rules;
        _ruleFences = view.RuleFences;
        var selectedId = _selectRuleAfterUpdate ?? (RuleList.SelectedItem as RuleRow)?.Id;
        _selectRuleAfterUpdate = null;
        var fenceIds = view.RuleFences.Select(fence => fence.Id).ToHashSet(StringComparer.Ordinal);
        var rows = view.Rules.Select((rule, index) => new RuleRow(rule.Id, rule.Enabled, view.RuleLines[index], !fenceIds.Contains(rule.FenceId))).ToList();
        RuleList.ItemsSource = rows;
        RuleList.SelectedItem = rows.FirstOrDefault(row => row.Id == selectedId);
        UpdateRuleButtons();
        if (_ruleEditorOpen)
        {
            // Fences renamed or deleted meanwhile: the editor offers what exists now, keeping the choice (M13b).
            var chosen = (RuleFenceBox.SelectedItem as RuleFence)?.Id;
            RuleFenceBox.ItemsSource = _ruleFences;
            RuleFenceBox.SelectedItem = _ruleFences.FirstOrDefault(fence => fence.Id == chosen);
        }
        if (_focusRuleId is { } focusId)
        {
            // The list was rebuilt under the ticked box: give the keyboard back to it (M13b).
            _focusRuleId = null;
            Dispatcher.BeginInvoke(() =>
            {
                if (rows.FirstOrDefault(row => row.Id == focusId) is { } row
                    && RuleList.ItemContainerGenerator.ContainerFromItem(row) is DependencyObject container && FindCheckBox(container) is { } box) box.Focus();
            }, DispatcherPriority.Loaded);
        }
    }

    private string? _focusRuleId;

    private static CheckBox? FindCheckBox(DependencyObject parent)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if ((child as CheckBox ?? FindCheckBox(child)) is { } box) return box;
        }
        return null;
    }

    /// <summary>A row's checkbox. Refilling the list sets the boxes too: only a real change is reported.</summary>
    private void OnRuleToggled(object sender, RoutedEventArgs args)
    {
        if (sender is not CheckBox { Tag: string id } box) return;
        var enabled = box.IsChecked == true;
        if (_rules.FirstOrDefault(rule => rule.Id == id) is not { } toggled || toggled.Enabled == enabled) return;
        if (box.IsKeyboardFocused) _focusRuleId = id;
        ReportRules([.. _rules.Select(rule => rule.Id == id ? rule with { Enabled = enabled } : rule)]);
    }

    private void UpdateRuleButtons()
    {
        // While a rule is being edited the list stays as it is: deleting or moving its rule would lose the edit (M13b).
        var index = _ruleEditorOpen ? -1 : RuleList.SelectedIndex;
        AddRuleButton.IsEnabled = !_ruleEditorOpen;
        EditRuleButton.IsEnabled = index >= 0;
        DeleteRuleButton.IsEnabled = index >= 0;
        MoveRuleUpButton.IsEnabled = index > 0;
        MoveRuleDownButton.IsEnabled = index >= 0 && index < _rules.Count - 1;
    }

    private void MoveSelectedRule(int offset)
    {
        var index = RuleList.SelectedIndex;
        var target = index + offset;
        if (index < 0 || target < 0 || target >= _rules.Count) return;
        var rules = _rules.ToList();
        (rules[index], rules[target]) = (rules[target], rules[index]);
        _selectRuleAfterUpdate = rules[target].Id;
        ReportRules(rules);
    }

    private void ReportRules(IReadOnlyList<Rule> rules) => RulesChanged?.Invoke(rules);

    private void BeginRuleEdit(Rule? rule, string? fenceId)
    {
        _ruleEditorOpen = true;
        _editingRuleId = rule?.Id;
        RuleFenceBox.ItemsSource = _ruleFences;
        RuleFenceBox.SelectedItem = _ruleFences.FirstOrDefault(fence => fence.Id == fenceId);
        var kind = rule?.Condition.Kind is { } existing && Enum.IsDefined(existing) ? existing : RuleKind.Type;
        RuleKindBox.SelectedIndex = (int)kind; // the items are in RuleKind order
        ShowConditionFields(rule?.Condition);
        SaveRuleButton.Content = rule is null ? "Add rule" : "Save rule";
        RuleEditor.Visibility = Visibility.Visible;
        UpdateRuleButtons();
        RuleKindBox.Focus();
    }

    private void EndRuleEdit()
    {
        if (!_ruleEditorOpen) return;
        _ruleEditorOpen = false;
        _editingRuleId = null;
        RuleEditor.Visibility = Visibility.Collapsed;
        UpdateRuleButtons();
        (RuleList.ItemContainerGenerator.ContainerFromItem(RuleList.SelectedItem) as UIElement ?? AddRuleButton).Focus();
    }

    /// <summary>The fields of the chosen condition kind, filled from <paramref name="condition"/> (or defaults).</summary>
    private void ShowConditionFields(RuleCondition? condition)
    {
        var kind = SelectedKind;
        (string Label, object Value)[] choices = kind switch
        {
            RuleKind.Type => [.. Enum.GetValues<TypeGroup>().Select(group => (Rules.GroupName(group), (object)group)), ("Other extensions…", "extensions")],
            RuleKind.Game => [.. Enum.GetValues<GameLauncher>().Select(launcher => (launcher switch
            {
                GameLauncher.Any => "Any launcher",
                GameLauncher.Folder => "In a folder of mine…",
                _ => Rules.LauncherName(launcher),
            }, (object)launcher))],
            RuleKind.Age => [("Older than", RuleCompare.OlderThan), ("Newer than", RuleCompare.NewerThan)],
            RuleKind.Size => [("Bigger than", RuleCompare.BiggerThan), ("Smaller than", RuleCompare.SmallerThan)],
            _ => [],
        };
        var items = choices.Select(choice => new ComboBoxItem { Content = choice.Label, Tag = choice.Value }).ToList();
        RuleChoiceBox.ItemsSource = items;
        RuleChoiceBox.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(RuleChoiceBox, kind switch { RuleKind.Type => "File type", RuleKind.Game => "Launcher", _ => "Comparison" });
        object? chosen = condition is null ? null : kind switch
        {
            RuleKind.Type => condition.Group is { } group ? group : "extensions",
            RuleKind.Game => condition.Launcher ?? GameLauncher.Any,
            RuleKind.Age or RuleKind.Size => condition.Compare,
            _ => null,
        };
        RuleChoiceBox.SelectedItem = items.FirstOrDefault(item => Equals(item.Tag, chosen)) ?? items.FirstOrDefault();
        RuleTextBox.Text = condition is null ? "" : kind switch
        {
            RuleKind.Type => condition.Extensions ?? "",
            RuleKind.Game => condition.Folder ?? "",
            RuleKind.Name => condition.Pattern ?? "",
            RuleKind.Age => condition.Days?.ToString(CultureInfo.CurrentCulture) ?? "",
            RuleKind.Size => condition.Megabytes?.ToString(CultureInfo.CurrentCulture) ?? "",
            _ => "",
        };
        UpdateTextField();
    }

    private void UpdateTextField()
    {
        var kind = SelectedKind;
        var customExtensions = kind == RuleKind.Type && SelectedChoice is "extensions";
        var gameFolder = kind == RuleKind.Game && SelectedChoice is GameLauncher.Folder;
        var showText = kind is RuleKind.Name or RuleKind.Age or RuleKind.Size || customExtensions || gameFolder;
        RuleTextBox.Visibility = showText ? Visibility.Visible : Visibility.Collapsed;
        RuleBrowseButton.Visibility = gameFolder ? Visibility.Visible : Visibility.Collapsed;
        RuleUnitText.Text = kind switch { RuleKind.Age => "days", RuleKind.Size => "MB", _ => "" };
        var (name, hint) = kind switch
        {
            RuleKind.Type when customExtensions => ("Extensions", "Custom extensions are separated by spaces, like .iso .torrent"),
            RuleKind.Type => ("", "By the file's extension; Folders matches folders."),
            RuleKind.Game when gameFolder => ("Game folder", @"Shortcuts that point inside this folder, like D:\GameLibrary"),
            RuleKind.Game => ("", "Shortcuts made by Steam, Epic, Ubisoft Connect, the EA app, Battle.net or GOG Galaxy."),
            RuleKind.Name => ("Name pattern", "* stands for any text, ? for one character; plain text matches names that contain it."),
            RuleKind.Age => ("Number of days", "By the date the item was last changed."),
            _ => ("Size in megabytes", "Folders never match a size rule."),
        };
        AutomationProperties.SetName(RuleTextBox, name);
        RuleHint.Text = hint;
        RuleHint.Foreground = SecondaryText;
    }

    private void SaveRule()
    {
        if (BuildCondition(out var problem) is not { } condition)
        {
            ShowRuleProblem(problem);
            return;
        }
        if (RuleFenceBox.SelectedItem is not RuleFence fence)
        {
            ShowRuleProblem("Choose the fence the items go to.");
            return;
        }
        List<Rule> rules = _editingRuleId is { } id
            ? [.. _rules.Select(rule => rule.Id == id ? rule with { Condition = condition, FenceId = fence.Id } : rule)]
            : [.. _rules, Rule.Create(condition, fence.Id)];
        _selectRuleAfterUpdate = _editingRuleId ?? rules[^1].Id;
        EndRuleEdit();
        ReportRules(rules);
    }

    private void ShowRuleProblem(string problem)
    {
        RuleHint.Text = problem;
        RuleHint.Foreground = System.Windows.Media.Brushes.IndianRed;
        System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(RuleHint)
            .RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }

    /// <summary>The editor's condition, or null with what is missing.</summary>
    private RuleCondition? BuildCondition(out string problem)
    {
        problem = "";
        var kind = SelectedKind;
        var text = RuleTextBox.Text.Trim();
        switch (kind)
        {
            case RuleKind.Type when SelectedChoice is TypeGroup group:
                return new RuleCondition { Kind = kind, Group = group };
            case RuleKind.Type when text.Length == 0:
                problem = "Type at least one extension, like .iso";
                return null;
            case RuleKind.Type:
                return new RuleCondition { Kind = kind, Extensions = text };
            case RuleKind.Game when SelectedChoice is GameLauncher.Folder && text.Length == 0:
                problem = "Choose the folder your games are in.";
                return null;
            case RuleKind.Game when SelectedChoice is GameLauncher.Folder:
                return new RuleCondition { Kind = kind, Launcher = GameLauncher.Folder, Folder = text };
            case RuleKind.Game:
                return new RuleCondition { Kind = kind, Launcher = SelectedChoice as GameLauncher? ?? GameLauncher.Any };
            case RuleKind.Name when text.Length == 0:
                problem = "Type a name or a pattern, like invoice*";
                return null;
            case RuleKind.Name:
                return new RuleCondition { Kind = kind, Pattern = text };
        }
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) || !double.IsFinite(number) || number < 0)
        {
            problem = kind == RuleKind.Age ? "Type a number of days (0 or more)." : "Type a size in MB (0 or more).";
            return null;
        }
        var compare = SelectedChoice as RuleCompare?;
        return kind == RuleKind.Age
            ? new RuleCondition { Kind = kind, Compare = compare ?? RuleCompare.OlderThan, Days = number }
            : new RuleCondition { Kind = kind, Compare = compare ?? RuleCompare.BiggerThan, Megabytes = number };
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
            if (ReleaseLibraryForRemoval(handle)) Log.Information("a drive the game library watches is being removed: released it");
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
        _libraryStopped = true; // also on session end: a library scan finishing now writes and re-arms nothing (M13a review)
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
Expected: 0 warnings, 0 errors; `Passed: 392`.

- [ ] **Step 3: Live checks (print TEST RUNNING / TEST COMPLETE; restore the installed copy; consent unless the user said the PC is unattended)**

Run `m13b-smoke.ps1 -Exe <branch exe>` with Windows PowerShell (session scratchpad; it dot-sources `m8b-helpers.ps1`).
Expected: as in `docs/research/m13b-library-polish.md`: the stray temp file swept and the other file kept; the shortcut copy identical; one hidden row by name, with Show again clearing both ids; the editor locks Add and Delete; 0 warnings.

- [ ] **Step 4: Commit**

```powershell
git add src/NeoFences.Shell src/NeoFences.App
git commit -m "fix: fixed xbox package names, shortcut games losing their icon, rescans on every game folder write, library orphans after power cuts, ejects vetoed by listings in flight and rules editor edits lost"
```

---

### Task 3: Verification and docs

- [ ] **Step 1: Add Z9–Z18 to section Z of `docs/TEST-CHECKLIST.md`** (after the Z8 line)

```markdown
| Z9 | Game Library with an Epic DLC or Unreal Engine installed | neither shows as a game |
| Z10 | A Game Pass game (or another Xbox game with a resource name) | its real name, not "Microsoft.…" |
| Z11 | A Desktop shortcut into a Steam game's own folder (`…\steamapps\common\…\game.exe`) | the game shows once |
| Z12 | A Desktop game shortcut with a custom icon or "Run as administrator" | the library's copy keeps both |
| Z13 | Hide a `D:\GameLibrary` game, unplug D: (or rename the folder), open Settings | the hidden game is listed once, by name; Show again brings it back when D: returns |
| Z14 | A game writing files at its own root inside `D:\GameLibrary` while it runs | no library rescans while it plays |
| Z15 | Drag a file over a box whose front tab is the library, onto another tab's header | that tab shows and takes the drop; no drag image left behind on a refused drop |
| Z16 | Settings → Rules: open the editor, then try Delete / Move / Add, or fence menu → Rules for this fence… | the list stays as it is; a hint says to finish or cancel first |
| Z17 | Tick a rule's box with the keyboard (Space) | the focus stays on that box |
| Z18 | After a power cut during a library update | no stray `.…lnk` files in `library\`; the index loads |
```

- [ ] **Step 2: Append ADR-034 to `docs/DECISIONS.md`**

```markdown

## ADR-034 — Game Library polish: shortcut copies, remembered hidden games, quiet watchers (M13b, v1.6.1)
**Date:** 2026-10-04 · **Status:** Accepted

**Context.** Carry-overs from the M11–M13a reviews: Epic add-ons and Unreal Engine listed as games, Xbox titles under
package names, a Desktop shortcut into a launcher's game folder shown twice, shortcut games losing their icon and
"Run as administrator", 300+ games searched on every scan, hidden games listed by raw id while their source was away,
library watchers rescanning while games wrote files, power cuts leaving temp files and an unflushed index.

**Decision.**
- Epic lists only manifests in the "games" category whose main game is themselves; Xbox names come from the package's
  resources (`SHLoadIndirectString`).
- A Desktop shortcut whose program lies inside another entry's install folder is that game (programs only, never folders).
- A Desktop shortcut game is copied into the library as is (`GameEntry.ShortcutFile`), not rebuilt.
- Scans reuse a program found before while it still exists; game folders are watched for folders only; covers are
  decoded at the tile's pixel width.
- Hidden games are kept in the library index with every id and their name (`GameCatalog.HiddenGames`), so Settings
  lists each once, by name, also while its source cannot be read.
- `library\index.json` is written through to disk; NeoFences' own temp names (`.<32 hex>.lnk/.url`) are swept before
  each write. A failed rewrite of a file that is gone drops its entry (`lostFiles`).
- `config.json` from before schema 2 is kept once as `backups\pre-schema-2-config.json` (outside the daily rotation).
- Portals let go of a listing still in flight when a drive is being removed; the rules editor keeps its list still
  while open; a library tab no longer blocks tab switching during a drag.

**Consequences.** A user's own shortcut changes (a new icon) reach the library on the next change of its target or name,
not of its icon alone.
```

- [ ] **Step 3: Write `docs/research/m13b-library-polish.md`**

```markdown
# M13b — v1.6 carry-overs, Game Library and rules polish: results

**Date:** 2026-10-04 · **Machine:** Windows 11 Pro 25H2 (26200), NeoFences 1.6.0 installed.
**Build:** M13b prototype (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section Z (Z9–Z18) · **Decision:** ADR-034.

## Read-only probes

- Scanner probe on the user's PC: the same 12 games; Epic's Mafia DE still listed (category "games", no main game);
  a second scan reusing remembered programs took 33 ms instead of 173 ms.
- Package names: `ms-resource:AppStoreName` (Calculator) and `ms-resource:Resources/AppStoreName` (Notepad) resolve to
  "Windows Calculator" / "Windows Notepad" through `SHLoadIndirectString`.

## Live check on the prototype (the PC unattended, standing go; config and installed copy restored)

| Check | Result |
|---|---|
| A stray `.<32 hex>.lnk` in `library\` and a file NeoFences did not write | **Pass**: the stray was swept; the other file was kept. |
| Z12 a Desktop shortcut game | **Pass**: `AC Black Flag Resynced.lnk` in the library is byte-identical to the Public Desktop one. |
| Hide → Settings | **Pass**: remembered in the index as "AC Black Flag Resynced (2 ids)"; one Settings row by name; Show again cleared both ids. |
| Z16 rules editor open | **Pass**: Add and Delete disabled while editing; enabled again after Cancel. |
| Log | 0 warnings or errors. |

## Core (test-first)

19 new tests (392 in all): Epic games vs DLC and engines, a shortcut into a launcher's game folder, launcher links in
shortcut arguments, plain source names, remembered hidden games (also while their source is away), lost files dropped
from the index, the one-time pre-schema-2 config copy.
```

- [ ] **Step 4: Update the other docs**
  - `ROADMAP.md`:
    - the claim line becomes `- [x] M13b — done <date> (ADR-034, research/m13b-library-polish.md)`, and add `- [ ] v1.6.1 release`;
    - tick the carry-overs closed: the M11 rules editor and item-facts lines; the M12 lines on Epic DLC / Xbox names, shortcut duplicates / custom icons, 300+ games, status line / hidden list, drag over a library tab; the M13a review lines (Epic locked manifest, power-cut orphans / index, Settle doc and snapshot temp, pre-v2 copy, in-flight veto, tidy).
  - `ARCHITECTURE.md`: add an "M13b complete" line.
  - `SESSION-LOG.md`: add an entry.

- [ ] **Step 5: Refresh the hub** (`node --check`, publish with `url`).

- [ ] **Step 6: Commit**

```powershell
git add docs
git commit -m "docs: added ADR-034, M13b checks and results, and recorded the library polish batch"
```
