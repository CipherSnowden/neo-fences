# M22 — One kind of fence: games become items (0.12.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Games become ordinary items that any fence can hold — as cover tiles or icons — while the Game Library becomes the engine that finds games, an old library fence becomes an items fence once, and new games go to a chosen fence.

**Architecture:** Core gives `VirtualItem` a `GameId` and `ShowAs`, `LibrarySettings` a `NewGamesFence`, and adds `GameItems` (migrate, new games, add, retarget). The App runs the library scan while games are wanted, applies `GameItems` after each scan, migrates at start / after a restore / after a scan (a snapshot first), and adds Add games…, a game item menu, per-item tiles in the fence grid and a Settings choice. The library fence kind stays in the code, not in the menus.

**Tech Stack:** .NET 10, WPF (Fluent `ThemeMode` for the dialog), CsWin32 0.3.335 (unchanged), xUnit, Serilog, Velopack (unchanged).

**Spec:** `docs/superpowers/specs/2026-10-05-games-as-items-design.md` (approved 2026-10-05). Decision: ADR-045 (added by Task 3). Build notes: `docs/research/m22-games-as-items.md` (added by Task 3).

## How this plan is written

Every code change was built in a scratch prototype first (2026-10-05, worktree `neo_fences-m22proto`, branch
`m22-proto`) and probed live on a copy of the user's data (the "Games" library fence became 12 game items with the same
covers; a game Ctrl-dragged into "Apps" showed as a cover, then as an icon; Add games… listed the games). Each task's
patch was then replayed on a fresh worktree of `main` at `7838bc1`: the Core tests failed to compile before the Core
patch (`CS0103: The name 'GameItems' does not exist`) and passed after it (511), the solution built with 0 warnings after
the App patch, and the replayed tree is identical to the prototype. So each code step is **a patch to apply**:

1. Write the patch block **exactly** as shown to a file in the scratchpad (use the Write tool, never a shell heredoc).
   The file ends with one newline.
2. `git apply --whitespace=nowarn <file>` from the worktree root. If it does not apply, stop: something else changed the
   files (rule on it, never hand-merge silently).

## Where the build departs from the spec (decided while prototyping; the final review weighs them)

- `ShowAs` is `ItemShow?` with `Cover` / `Icon` (null = the usual look) instead of a `Default` value, so items.json
  carries `showAs` only when set; a typo in it reads as the usual look (`LenientEnumConverter<ItemShow>`).
- Migration also runs after a scan (not only at start and after a restore): a library fence whose index was empty at
  start becomes game items as soon as a scan finds games. A library with no games yet is never migrated (an unreadable
  index reads as empty).
- The scan also runs while Add games… is open, so the list fills on a PC with no game items yet.
- Add games… lists sources by their readable names (`GameCatalog.SourceName`); covers are decoded on the UI thread while
  the list is built (a `ponytail:` note: off it if hundreds of games pause there).
- Settings → Game Library: the hidden-games hint reads "None." (it pointed at the library fence's menu); its description
  explains Add games… and "New games go to".
- `NewGamesFence` is dropped by the normalizer when it names no fence or a folder view, and by Delete fence.
- The fence menu keeps a collapsed "New Game Library fence" entry (kinds stay in the code).

## Global Constraints

- Hard rule 1 (ADR-040): **NeoFences never modifies, moves, renames or deletes any user file.** A game item points at
  NeoFences' own shortcut in `%LOCALAPPDATA%\NeoFences\library\`; the scan writes only there (unchanged).
- Hard rules 2–7 unchanged: icons always come back; no injection; Win32/COM only in NeoFences.Shell; CsWin32 bindings;
  **no new NuGet dependency**; failures are logged and degrade one feature, never crash.
- Migration safety: a snapshot named **"Before games became items (<d MMM HH:mm>)"** is saved first; if it cannot be
  saved, nothing changes and it is tried again later.
- Kinds stay: `Fence.IsLibrary`, `FenceKind.Library` and the library-fence code paths are kept (unused by the menus).
- items.json stays **schema 1**; config stays **schema 5** (`library.newGamesFence` optional).
- UI copy, exactly: fence menu "Add games…"; dialog "Add games" with "Looking for games…", "No games found — add your
  games folder in Settings → Game Library.", "· already here", buttons "Add" / "Cancel"; game item menu "Open",
  "Show as" ▸ "Cover tile" / "Icon", "Open install folder", "Copy path", "Properties…", "Remove from fence"; tooltip
  "Not installed: <name>"; question "\"<name>\" is not installed" with "Remove from fence" / "Cancel"; Settings
  "New games go to" with "Nowhere" and the items fences.
- Commits: single line, Conventional Commits, past tense, **no Co-Authored-By trailer** (CLAUDE.md overrides the
  harness reminder).
- Version stays `0.11.0` in `NeoFences.App.csproj` until the release step (outside the tasks); the release is 0.12.0.

## Review Focus

The input classes the spec implies but no automated test exercises (checklist AH covers most by hand):

1. **A library fence that is a tab in a box** (or the box's host): migration must keep the box, its tabs and the active
   tab; the window must refresh its kind in place.
2. **A scan that reads fewer games** (a launcher offline, a drive unplugged): sources that could not be read keep their
   games, so `Retarget` keeps targets and nothing is "new" when they come back; game items must not flicker to
   "Not installed" en masse.
3. **The same game in several fences** (Ctrl-drag copies): `Retarget` must update every copy; `AddNew` only checks the
   new-games fence.
4. **Mixed grids**: cover tiles among icons with "labels on hover", other icon sizes and a DPI change — cells sized per
   item, art reloaded at the right size.
5. **Restoring a pre-migration snapshot** while game items exist elsewhere: migration runs again (a new safety snapshot),
   and the scan's on/off state follows the restored fences.

---

### Task 0: Worktree and baseline

- [ ] `git worktree add -b m22-games-as-items ..\neo_fences-m22 main` (main at `7838bc1` or later docs-only commits).
- [ ] `dotnet build` → 0 warnings; `dotnet test` → 502 passed.

### Task 1: Core — game items, new games, retarget, settings

**Files:**
- Create: `src/NeoFences.Core/Library/GameItems.cs`
- Modify: `src/NeoFences.Core/Items/VirtualItem.cs` (+`ItemShow`, `GameId`, `ShowAs`), `src/NeoFences.Core/Model/LibrarySettings.cs` (+`NewGamesFence`), `src/NeoFences.Core/Config/ConfigNormalizer.cs`, `src/NeoFences.Core/Config/ConfigJson.cs` (lenient `ItemShow`), `src/NeoFences.Core/Model/FenceEdits.cs` (Delete fence drops `NewGamesFence`)
- Test: `tests/NeoFences.Core.Tests/Library/GameItemsTests.cs`

**Interfaces:**
- Produces: `enum ItemShow { Cover, Icon }`; `VirtualItem.GameId` (string?), `VirtualItem.ShowAs` (ItemShow?);
  `LibrarySettings.NewGamesFence` (string?);
  `GameItems.IsGame(VirtualItem) → bool`; `GameItems.ShowsCover(VirtualItem) → bool`;
  `GameItems.Create(LibraryItem, string libraryFolder) → VirtualItem`;
  `GameItems.Migration(NeoFencesConfig Config, ItemsDocument Items, IReadOnlyList<string> MigratedFenceIds)`;
  `GameItems.Migrate(NeoFencesConfig, ItemsDocument, LibraryState, string libraryFolder) → Migration`;
  `GameItems.NewGames(LibraryState previous, LibraryState current) → IReadOnlyList<LibraryItem>`;
  `GameItems.AddNew(ItemsDocument, string fenceId, IReadOnlyList<LibraryItem>, string libraryFolder) → ItemsAdded`;
  `GameItems.Retarget(ItemsDocument, LibraryState, string libraryFolder) → ItemsDocument` (the same instance when nothing changed).

- [ ] **Step 1: Write the failing tests.** Write this patch to `m22-1-core-tests.patch` and `git apply --whitespace=nowarn` it:

````diff
diff --git a/tests/NeoFences.Core.Tests/Library/GameItemsTests.cs b/tests/NeoFences.Core.Tests/Library/GameItemsTests.cs
new file mode 100644
index 0000000..6a46597
--- /dev/null
+++ b/tests/NeoFences.Core.Tests/Library/GameItemsTests.cs
@@ -0,0 +1,136 @@
+using NeoFences.Core.Config;
+using NeoFences.Core.Items;
+using NeoFences.Core.Library;
+using NeoFences.Core.Model;
+
+namespace NeoFences.Core.Tests.Library;
+
+/// <summary>M22 (0.12.0): games become items — one kind of fence (spec 2026-10-05-games-as-items-design).</summary>
+public class GameItemsTests
+{
+    private const string Folder = @"C:\Data\NeoFences\library";
+
+    private static LibraryItem Game(string id, string name, params string[] otherIds) =>
+        new(new GameEntry(id, name, GameSource.Steam, "steam", new GameLaunch($"steam://rungameid/{id}")) { OtherIds = otherIds }, $"{name}.url", "sig");
+
+    private static LibraryState State(params LibraryItem[] games) => new() { Items = games };
+
+    private static (NeoFencesConfig Config, Fence Library) WithLibraryFence()
+    {
+        var library = Fence.Create("Games", isLibrary: true) with { IconSize = 64 };
+        return (NeoFencesConfig.CreateDefault() with { Fences = [library] }, library);
+    }
+
+    [Fact]
+    public void Migrate_TurnsTheLibraryFenceIntoAnItemsFence_WithOneGameItemPerGame_InOrder()
+    {
+        var (config, library) = WithLibraryFence();
+        var migration = GameItems.Migrate(config, new ItemsDocument(), State(Game("steam:1", "Blur"), Game("steam:2", "Hades")), Folder);
+
+        var fence = migration.Config.Fences.Single();
+        Assert.Equal(FenceKind.Items, fence.Kind);
+        Assert.Equal(("Games", 64), (fence.Title, fence.IconSize));
+        Assert.Equal([library.Id], migration.MigratedFenceIds);
+        var items = migration.Items.Of(library.Id);
+        Assert.Equal([@"C:\Data\NeoFences\library\Blur.url", @"C:\Data\NeoFences\library\Hades.url"], items.Select(item => item.Target));
+        Assert.Equal(["steam:1", "steam:2"], items.Select(item => item.GameId));
+        Assert.All(items, item => Assert.True(GameItems.ShowsCover(item)));
+        Assert.Equal(library.Id, migration.Config.Library.NewGamesFence);
+    }
+
+    [Fact]
+    public void Migrate_KeepsAChosenNewGamesFence_AndLeavesConfigsWithoutALibraryFenceAlone()
+    {
+        var (config, _) = WithLibraryFence();
+        var other = Fence.Create("Mine");
+        config = config with { Fences = [.. config.Fences, other], Library = config.Library with { NewGamesFence = other.Id } };
+        Assert.Equal(other.Id, GameItems.Migrate(config, new ItemsDocument(), State(Game("steam:1", "Blur")), Folder).Config.Library.NewGamesFence);
+
+        var plain = NeoFencesConfig.CreateDefault() with { Fences = [Fence.Create("a")] };
+        var items = new ItemsDocument();
+        var untouched = GameItems.Migrate(plain, items, State(Game("steam:1", "Blur")), Folder);
+        Assert.Same(plain, untouched.Config);
+        Assert.Same(items, untouched.Items);
+        Assert.Empty(untouched.MigratedFenceIds);
+    }
+
+    [Fact]
+    public void Migrate_WaitsWhileTheLibraryHasNoGamesYet()
+    {
+        var (config, _) = WithLibraryFence(); // an unreadable index reads as empty: the fence must not become an empty items fence
+        var migration = GameItems.Migrate(config, new ItemsDocument(), State(), Folder);
+        Assert.Same(config, migration.Config);
+        Assert.Empty(migration.MigratedFenceIds);
+    }
+
+    [Fact]
+    public void NewGames_AreTheOnesNoEarlierGameHadAnIdOf()
+    {
+        var previous = State(Game("steam:1", "Blur"), Game("folder:hades", "Hades"));
+        var current = State(Game("steam:1", "Blur"), Game("steam:9", "Hades", "folder:hades"), Game("epic:7", "Alan Wake 2"));
+        Assert.Equal(["epic:7"], GameItems.NewGames(previous, current).Select(game => game.Game.Id));
+        Assert.Empty(GameItems.NewGames(current, current));
+    }
+
+    [Fact]
+    public void AddNew_AppendsGameItems_ButNeverASecondItemForTheSameGame()
+    {
+        var fenceId = Fence.NewId();
+        var start = new ItemsDocument().With(fenceId, [VirtualItem.Create(@"C:\x.txt"), GameItems.Create(Game("steam:1", "Blur"), Folder)]);
+        var added = GameItems.AddNew(start, fenceId, [Game("steam:1", "Blur"), Game("epic:7", "Alan Wake 2")], Folder);
+        Assert.Equal([null, "steam:1", "epic:7"], added.Document.Of(fenceId).Select(item => item.GameId));
+        Assert.Single(added.AddedIds);
+    }
+
+    [Fact]
+    public void Retarget_PointsGameItemsAtTheirGamesCurrentShortcut_MatchingMergedIds()
+    {
+        var fenceId = Fence.NewId();
+        var blur = GameItems.Create(Game("steam:1", "Blur"), Folder);
+        var hades = GameItems.Create(Game("folder:hades", "Hades"), Folder);
+        var gone = GameItems.Create(Game("steam:5", "Old"), Folder);
+        var plain = VirtualItem.Create(@"C:\notes.txt");
+        var items = new ItemsDocument().With(fenceId, [blur, hades, gone, plain]);
+
+        var renamed = State(Game("steam:1", "Blur (2010)"), Game("steam:9", "Hades", "folder:hades"));
+        var result = GameItems.Retarget(items, renamed, Folder);
+        Assert.Equal([@"C:\Data\NeoFences\library\Blur (2010).url", @"C:\Data\NeoFences\library\Hades.url", gone.Target, plain.Target],
+            result.Of(fenceId).Select(item => item.Target));
+        Assert.Same(result, GameItems.Retarget(result, renamed, Folder)); // nothing changed: the same document
+    }
+
+    [Fact]
+    public void ShowsCover_IsForGameItemsUnlessSetToIcon()
+    {
+        var game = GameItems.Create(Game("steam:1", "Blur"), Folder);
+        Assert.True(GameItems.ShowsCover(game));
+        Assert.False(GameItems.ShowsCover(game with { ShowAs = ItemShow.Icon }));
+        Assert.False(GameItems.ShowsCover(VirtualItem.Create(@"C:\a.exe") with { ShowAs = ItemShow.Cover }));
+    }
+
+    [Fact]
+    public void NewGamesFence_IsDroppedWhenItsFenceIsDeleted_OrIsAFolderView_OrDoesNotExist()
+    {
+        var items = Fence.Create("Games");
+        var view = Fence.Create("Downloads") with { View = new FolderView { Path = @"C:\Downloads" } };
+        var config = NeoFencesConfig.CreateDefault() with { Fences = [items, view], Library = new LibrarySettings { NewGamesFence = items.Id } };
+        Assert.Equal(items.Id, ConfigNormalizer.Normalize(config).Library.NewGamesFence);
+        Assert.Null(FenceEdits.DeleteFence(config, items.Id).Library.NewGamesFence);
+        Assert.Null(ConfigNormalizer.Normalize(config with { Library = new LibrarySettings { NewGamesFence = view.Id } }).Library.NewGamesFence);
+        Assert.Null(ConfigNormalizer.Normalize(config with { Library = new LibrarySettings { NewGamesFence = "nope" } }).Library.NewGamesFence);
+    }
+
+    [Fact]
+    public void ItemsJson_KeepsGameFields_WritesNoShowAsWhenUnset_AndRepairsATypo()
+    {
+        var fenceId = Fence.NewId();
+        var game = GameItems.Create(Game("steam:1", "Blur"), Folder) with { ShowAs = ItemShow.Icon };
+        var plain = VirtualItem.Create(@"C:\a.txt");
+        var json = ConfigJson.SerializeItems(new ItemsDocument().With(fenceId, [game, plain]));
+        Assert.Equal(1, json.Split("showAs").Length - 1);
+        var back = ConfigJson.DeserializeItems(json).Of(fenceId);
+        Assert.Equal(("steam:1", ItemShow.Icon), (back[0].GameId, back[0].ShowAs));
+        var typo = ConfigJson.DeserializeItems(json.Replace("\"icon\"", "\"poster\""));
+        Assert.True(GameItems.ShowsCover(typo.Of(fenceId)[0])); // an unknown value reads as the default: a cover
+    }
+}
````

- [ ] **Step 2: Run them to see them fail.** `dotnet test tests/NeoFences.Core.Tests`
  Expected: build fails with `CS0103: The name 'GameItems' does not exist in the current context` (and `ItemShow`,
  `NewGamesFence`, `ShowAs`).

- [ ] **Step 3: Implement.** Write this patch to `m22-1-core.patch` and apply it:

````diff
diff --git a/src/NeoFences.Core/Config/ConfigJson.cs b/src/NeoFences.Core/Config/ConfigJson.cs
index bd09163..cfd926c 100644
--- a/src/NeoFences.Core/Config/ConfigJson.cs
+++ b/src/NeoFences.Core/Config/ConfigJson.cs
@@ -25,6 +25,7 @@ public static class ConfigJson
             new LenientEnumConverter<ColourStyle>(), new LenientEnumConverter<TitleWeight>(),
             // Folder views (M21): a typo in a view's show or sort is repaired too, not the whole file lost (final review).
             new LenientEnumConverter<ViewShow>(), new LenientEnumConverter<FenceSort>(),
+            new LenientEnumConverter<Items.ItemShow>(), // M22: a typo in items.json shows the usual look, never fails the file
             new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
         },
     };
diff --git a/src/NeoFences.Core/Config/ConfigNormalizer.cs b/src/NeoFences.Core/Config/ConfigNormalizer.cs
index 6e48f5b..546b362 100644
--- a/src/NeoFences.Core/Config/ConfigNormalizer.cs
+++ b/src/NeoFences.Core/Config/ConfigNormalizer.cs
@@ -52,7 +52,11 @@ public static class ConfigNormalizer
             Settings = settings,
             Fences = FenceTabs.Repair(fences), // M9: one consistent box per tab
             Layouts = NormalizeLayouts(config.Layouts),
-            Library = NormalizeLibrary(config.Library),
+            Library = NormalizeLibrary(config.Library) with
+            {
+                // M22: only a fence that holds items (not a folder view, not gone)
+                NewGamesFence = fences.Any(fence => fence.Id == config.Library?.NewGamesFence && fence.View is null) ? config.Library!.NewGamesFence : null,
+            },
         };
     }
 
diff --git a/src/NeoFences.Core/Items/VirtualItem.cs b/src/NeoFences.Core/Items/VirtualItem.cs
index da1bd73..2914deb 100644
--- a/src/NeoFences.Core/Items/VirtualItem.cs
+++ b/src/NeoFences.Core/Items/VirtualItem.cs
@@ -14,6 +14,9 @@ public sealed record ItemIcon
     public string? Image { get; init; }
 }
 
+/// <summary>How an item shows (M22): a game item is a cover tile unless set to an icon; other items ignore it so far.</summary>
+public enum ItemShow { Cover, Icon }
+
 /// <summary>What an item points at. A path may be a file or a folder: that is known only when it is checked.</summary>
 public enum ItemKind { Path, Website, Special }
 
@@ -43,6 +46,12 @@ public sealed record VirtualItem
     /// <summary>Shown as the item's tooltip.</summary>
     public string? Note { get; init; }
 
+    /// <summary>A game (M22): the Game Library's id for it; the target is NeoFences' own shortcut for the game.</summary>
+    public string? GameId { get; init; }
+
+    /// <summary>Null: the item's usual look (a game's cover tile).</summary>
+    public ItemShow? ShowAs { get; init; }
+
     [JsonIgnore]
     public ItemKind Kind => ItemKinds.Of(Target);
 
diff --git a/src/NeoFences.Core/Library/GameItems.cs b/src/NeoFences.Core/Library/GameItems.cs
new file mode 100644
index 0000000..58a27a9
--- /dev/null
+++ b/src/NeoFences.Core/Library/GameItems.cs
@@ -0,0 +1,79 @@
+using NeoFences.Core.Items;
+using NeoFences.Core.Model;
+
+namespace NeoFences.Core.Library;
+
+/// <summary>
+/// Games as items (M22, spec 2026-10-05-games-as-items-design, ADR-045): a game item is a virtual item whose target is
+/// NeoFences' own shortcut for the game in its library folder, with the game's id. The library scan keeps those shortcuts;
+/// these rules keep the items pointing at them. Pure; none of them touches a file.
+/// </summary>
+public static class GameItems
+{
+    public static bool IsGame(VirtualItem item) => item.GameId is not null;
+
+    /// <summary>A game item shows its cover tile unless set to an icon.</summary>
+    public static bool ShowsCover(VirtualItem item) => IsGame(item) && item.ShowAs != ItemShow.Icon;
+
+    public static VirtualItem Create(LibraryItem game, string libraryFolder) =>
+        VirtualItem.Create(Path.Combine(libraryFolder, game.FileName)) with { GameId = game.Game.Id };
+
+    /// <param name="MigratedFenceIds">Fences that were Game Library fences and now hold game items.</param>
+    public sealed record Migration(NeoFencesConfig Config, ItemsDocument Items, IReadOnlyList<string> MigratedFenceIds);
+
+    /// <summary>
+    /// Every Game Library fence becomes an items fence with one game item per game, in the library's order, and becomes the
+    /// new-games fence when none is chosen. A library that has no games yet (or whose index could not be read) waits.
+    /// </summary>
+    public static Migration Migrate(NeoFencesConfig config, ItemsDocument items, LibraryState library, string libraryFolder)
+    {
+        var libraryFences = config.Fences.Where(fence => fence.IsLibrary).ToList();
+        if (libraryFences.Count == 0 || library.Items.Count == 0) return new Migration(config, items, []);
+        foreach (var fence in libraryFences)
+        {
+            config = config.WithFence(fence with { IsLibrary = false });
+            items = items.With(fence.Id, [.. items.Of(fence.Id), .. library.Items.Select(game => Create(game, libraryFolder))]);
+        }
+        if (config.Library.NewGamesFence is null) config = config with { Library = config.Library with { NewGamesFence = libraryFences[0].Id } };
+        return new Migration(config, items, [.. libraryFences.Select(fence => fence.Id)]);
+    }
+
+    /// <summary>Games in <paramref name="current"/> that no game of <paramref name="previous"/> had an id of (merged ids count).</summary>
+    public static IReadOnlyList<LibraryItem> NewGames(LibraryState previous, LibraryState current)
+    {
+        var known = previous.Items.SelectMany(item => GameCatalog.IdsOf(item.Game)).ToHashSet(StringComparer.OrdinalIgnoreCase);
+        return [.. current.Items.Where(item => !GameCatalog.IdsOf(item.Game).Any(known.Contains))];
+    }
+
+    /// <summary>New games at the end of a fence; a game the fence already holds (by id) is not added again.</summary>
+    public static ItemsAdded AddNew(ItemsDocument items, string fenceId, IReadOnlyList<LibraryItem> games, string libraryFolder)
+    {
+        var held = items.Of(fenceId).Where(IsGame).Select(item => item.GameId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
+        return ItemEdits.Add(items, fenceId, [.. games.Where(game => !GameCatalog.IdsOf(game.Game).Any(held.Contains)).Select(game => Create(game, libraryFolder))]);
+    }
+
+    /// <summary>
+    /// Every game item points at its game's current shortcut (a renamed game's file, a merged id). A game no longer in the
+    /// library keeps its target: its file goes, so it shows Missing. The same document when nothing changed.
+    /// </summary>
+    public static ItemsDocument Retarget(ItemsDocument items, LibraryState library, string libraryFolder)
+    {
+        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
+        foreach (var game in library.Items)
+        {
+            foreach (var id in GameCatalog.IdsOf(game.Game)) files.TryAdd(id, Path.Combine(libraryFolder, game.FileName));
+        }
+        var changed = false;
+        var fences = new Dictionary<string, IReadOnlyList<VirtualItem>>();
+        foreach (var (fenceId, list) in items.Fences)
+        {
+            fences[fenceId] = [.. list.Select(item =>
+            {
+                if (item.GameId is not { } gameId || !files.TryGetValue(gameId, out var file) || ItemKinds.Comparer.Equals(file, item.Target)) return item;
+                changed = true;
+                return item with { Target = file };
+            })];
+        }
+        return changed ? items with { Fences = fences } : items;
+    }
+}
diff --git a/src/NeoFences.Core/Model/FenceEdits.cs b/src/NeoFences.Core/Model/FenceEdits.cs
index 7c29c32..7b59f44 100644
--- a/src/NeoFences.Core/Model/FenceEdits.cs
+++ b/src/NeoFences.Core/Model/FenceEdits.cs
@@ -90,7 +90,11 @@ public static class FenceEdits
     {
         Require(config, fenceId);
         config = FenceTabs.Leave(config, fenceId);
-        return config with { Fences = config.Fences.Where(fence => fence.Id != fenceId).ToList() };
+        return config with
+        {
+            Fences = config.Fences.Where(fence => fence.Id != fenceId).ToList(),
+            Library = config.Library.NewGamesFence == fenceId ? config.Library with { NewGamesFence = null } : config.Library, // M22
+        };
     }
 
     private static Fence Require(NeoFencesConfig config, string fenceId) =>
diff --git a/src/NeoFences.Core/Model/LibrarySettings.cs b/src/NeoFences.Core/Model/LibrarySettings.cs
index 51ceb3a..0d1bd2f 100644
--- a/src/NeoFences.Core/Model/LibrarySettings.cs
+++ b/src/NeoFences.Core/Model/LibrarySettings.cs
@@ -10,6 +10,9 @@ public sealed record LibrarySettings
 
     /// <summary>Game ids the user hid ("steam:431960").</summary>
     public IReadOnlyList<string> Hidden { get; init; } = [];
+
+    /// <summary>The fence newly installed games go to (M22), or null: nowhere.</summary>
+    public string? NewGamesFence { get; init; }
 }
 
 /// <summary>Which sources the library reads; all on by default.</summary>
````

- [ ] **Step 4: Run the tests.** `dotnet test tests/NeoFences.Core.Tests`
  Expected: `Passed! - Failed: 0, Passed: 511`.

- [ ] **Step 5: Commit.** `git add -A && git commit -m "feat: added game items to the core: migration, new games, retarget and the new-games fence"`

### Task 2: App — games as items in any fence

**Files:**
- Create: `src/NeoFences.App/FenceHost.GameItems.cs`, `src/NeoFences.App/AddGamesWindow.xaml(.cs)`
- Modify: `FenceHost.cs` (migration at start and after a restore, Add games… wiring, covers in `RefreshWindow`, tray
  without "New Game Library fence"), `FenceHost.Library.cs` (`LibraryWanted`, `ApplyGames` after each scan, Settings
  view), `FenceHost.Items.cs` (game item menu, "not installed" question, scan on/off after item changes),
  `FenceItemView.cs` (`ShownItem.Tile` / `TileArt` / `IsGame`), `FenceWindow.xaml(.cs)` (per-item tiles and cell width,
  "Add games…", collapsed "New Game Library fence"), `MissingItemWindow.xaml.cs` (game wording),
  `SettingsWindow.xaml` / `SettingsWindow.Library.cs` ("New games go to")

**Interfaces:**
- Consumes: everything Task 1 produces.
- Produces: `AddGamesWindow(string fenceId)` with `Row(LibraryItem Game, string? Poster, bool AlreadyHere)`,
  `ShowGames(IReadOnlyList<Row>, bool scanning = false)`, `Chosen`; `FenceWindow.AddGamesRequested`;
  `SettingsWindow.NewGamesFenceChanged` (Action<string?>); `LibraryView(..., Fences, NewGamesFence)`.

- [ ] **Step 1: Implement.** Write this patch to `m22-2-app.patch` and apply it:

````diff
diff --git a/src/NeoFences.App/AddGamesWindow.xaml b/src/NeoFences.App/AddGamesWindow.xaml
new file mode 100644
index 0000000..f0bad57
--- /dev/null
+++ b/src/NeoFences.App/AddGamesWindow.xaml
@@ -0,0 +1,25 @@
+<Window x:Class="NeoFences.App.AddGamesWindow"
+        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
+        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
+        Title="Add games" Width="480" Height="620" MinWidth="380" MinHeight="360"
+        WindowStartupLocation="CenterScreen" ShowInTaskbar="True" ThemeMode="System">
+    <!-- M22 spec 2026-10-05-games-as-items §2: every game the scan found; ticked ones become game items in this fence. -->
+    <Grid Margin="20,16,20,18">
+        <Grid.RowDefinitions>
+            <RowDefinition Height="Auto" />
+            <RowDefinition Height="*" />
+            <RowDefinition Height="Auto" />
+        </Grid.RowDefinitions>
+        <TextBlock TextWrapping="Wrap" Margin="0,0,0,12" Foreground="{DynamicResource TextFillColorSecondaryBrush}"
+                   Text="Each ticked game becomes an item in this fence. Games already here are not ticked. Missing a game? Add your games folder in Settings → Game Library." />
+        <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
+            <StackPanel x:Name="GamesPanel" />
+        </ScrollViewer>
+        <TextBlock x:Name="ListStatus" Grid.Row="1" HorizontalAlignment="Center" VerticalAlignment="Center" TextWrapping="Wrap"
+                   Foreground="{DynamicResource TextFillColorSecondaryBrush}" Text="Looking for games…" />
+        <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,14,0,0">
+            <Button x:Name="AddButton" Content="Add" IsDefault="True" MinWidth="90" Margin="0,0,8,0" IsEnabled="False" />
+            <Button Content="Cancel" IsCancel="True" MinWidth="90" />
+        </StackPanel>
+    </Grid>
+</Window>
diff --git a/src/NeoFences.App/AddGamesWindow.xaml.cs b/src/NeoFences.App/AddGamesWindow.xaml.cs
new file mode 100644
index 0000000..bc82b3b
--- /dev/null
+++ b/src/NeoFences.App/AddGamesWindow.xaml.cs
@@ -0,0 +1,88 @@
+using System.Windows;
+using System.Windows.Controls;
+using System.Windows.Media.Imaging;
+using NeoFences.Core.Library;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// Fence menu → "Add games…" (M22 spec §2): one row per game the scan found (cover, name, source), ticked unless the fence
+/// already has it. The list fills in again when a scan finishes while it is open; ticks the user changed are kept.
+/// </summary>
+public partial class AddGamesWindow : Window
+{
+    /// <param name="Poster">The game's cover, if the scan found one.</param>
+    public sealed record Row(LibraryItem Game, string? Poster, bool AlreadyHere);
+
+    private const int CoverWidth = 32;
+    private readonly List<(CheckBox Box, LibraryItem Game)> _rows = [];
+    private readonly Dictionary<string, bool> _changedByUser = new(StringComparer.OrdinalIgnoreCase);
+
+    public string FenceId { get; }
+
+    public IReadOnlyList<LibraryItem> Chosen { get; private set; } = [];
+
+    public AddGamesWindow(string fenceId)
+    {
+        InitializeComponent();
+        FenceId = fenceId;
+        AddButton.Click += (_, _) =>
+        {
+            Chosen = [.. _rows.Where(row => row.Box.IsChecked == true).Select(row => row.Game)];
+            DialogResult = true;
+        };
+    }
+
+    /// <param name="scanning">A scan is still running: an empty list says so instead of "no games".</param>
+    public void ShowGames(IReadOnlyList<Row> rows, bool scanning = false)
+    {
+        GamesPanel.Children.Clear();
+        _rows.Clear();
+        foreach (var row in rows.OrderBy(row => row.Game.Game.Name, StringComparer.CurrentCultureIgnoreCase))
+        {
+            var content = new StackPanel { Orientation = Orientation.Horizontal };
+            // ponytail: covers decoded on the UI thread while the list is built; off it if libraries of hundreds of games pause here.
+            if (LoadCover(row.Poster) is { } cover) content.Children.Add(new Image { Source = cover, Width = CoverWidth, Height = CoverWidth * 1.5, Margin = new Thickness(0, 0, 10, 0) });
+            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
+            text.Children.Add(new TextBlock { Text = row.Game.Game.Name });
+            text.Children.Add(new TextBlock
+            {
+                Text = GameCatalog.SourceName(row.Game.Game.ScanKey) + (row.AlreadyHere ? " · already here" : ""), FontSize = 11,
+                Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorSecondaryBrush"),
+            });
+            content.Children.Add(text);
+            var id = row.Game.Game.Id;
+            var box = new CheckBox { Content = content, IsChecked = _changedByUser.TryGetValue(id, out var ticked) ? ticked : !row.AlreadyHere, Margin = new Thickness(4, 3, 0, 3) };
+            System.Windows.Automation.AutomationProperties.SetName(box, row.Game.Game.Name);
+            box.Click += (_, _) => { _changedByUser[id] = box.IsChecked == true; UpdateAddButton(); };
+            GamesPanel.Children.Add(box);
+            _rows.Add((box, row.Game));
+        }
+        ListStatus.Text = rows.Count > 0 ? "" : scanning ? "Looking for games…" : "No games found — add your games folder in Settings → Game Library.";
+        ListStatus.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
+        UpdateAddButton();
+    }
+
+    private void UpdateAddButton() => AddButton.IsEnabled = _rows.Any(row => row.Box.IsChecked == true);
+
+    private static BitmapImage? LoadCover(string? path)
+    {
+        if (path is null) return null;
+        try
+        {
+            var image = new BitmapImage();
+            image.BeginInit();
+            image.CacheOption = BitmapCacheOption.OnLoad; // the file is not kept open
+            image.DecodePixelWidth = CoverWidth * 2;
+            image.UriSource = new Uri(path);
+            image.EndInit();
+            image.Freeze();
+            return image;
+        }
+        catch (Exception failure) when (failure is not OutOfMemoryException)
+        {
+            Serilog.Log.Debug(failure, "Add games: cover {Path} not shown", path); // the row keeps its name
+            return null;
+        }
+    }
+}
diff --git a/src/NeoFences.App/FenceHost.GameItems.cs b/src/NeoFences.App/FenceHost.GameItems.cs
new file mode 100644
index 0000000..299e766
--- /dev/null
+++ b/src/NeoFences.App/FenceHost.GameItems.cs
@@ -0,0 +1,141 @@
+using System.Windows.Controls;
+using NeoFences.Core.Items;
+using NeoFences.Core.Library;
+using NeoFences.Core.Model;
+using NeoFences.Shell;
+using Serilog;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// Games as items (M22, spec 2026-10-05-games-as-items-design, ADR-045): one kind of fence. A game is a virtual item
+/// pointing at NeoFences' own shortcut for it; the library scan (FenceHost.Library) is the engine that finds games. Old
+/// Game Library fences become items fences once (a snapshot first); new games go to the chosen fence.
+/// </summary>
+public sealed partial class FenceHost
+{
+    private AddGamesWindow? _addGamesWindow; // while open, the library scans even with no game items yet
+
+    /// <summary>
+    /// A Game Library fence becomes an items fence with one game item per game — once its library has games. A snapshot
+    /// is saved first; without it nothing changes (tried again next time). True when something changed.
+    /// </summary>
+    private bool MigrateGames(LibraryState library)
+    {
+        if (!_config.Fences.Any(fence => fence.IsLibrary) || library.Items.Count == 0) return false;
+        var now = DateTimeOffset.Now;
+        if (_snapshots.Save(Snapshots.Take(_config, _items, name: $"Before games became items ({now:d MMM HH:mm})", now: now)) is null)
+        {
+            Log.Warning(_snapshots.LastFailure, "games not made into items: the snapshot before it could not be saved; tried again next time");
+            return false;
+        }
+        var migration = GameItems.Migrate(_config, _items, library, AppPaths.LibraryDirectory);
+        (_config, _items) = (migration.Config, migration.Items);
+        Log.Information("Game Library fence(s) {FenceIds} now hold {Count} game items", migration.MigratedFenceIds, library.Items.Count);
+        SaveNow();
+        return true;
+    }
+
+    /// <summary>After a migration or a restore: windows show their fence's kind again, and the library's own lister goes.</summary>
+    private void ShowMigratedFences()
+    {
+        foreach (var window in _windows.Values)
+        {
+            if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is { } shown) window.Refresh(shown);
+        }
+        EnsureLibraryLister();
+        RefreshWindows();
+        UpdateWatching();
+        RefreshSettings();
+    }
+
+    /// <summary>
+    /// A scan finished: an old library fence becomes game items; game items follow renamed shortcuts; games no earlier scan
+    /// had go to the new-games fence.
+    /// </summary>
+    private void ApplyGames(LibraryState previous, LibraryState current)
+    {
+        if (MigrateGames(current)) ShowMigratedFences();
+        var retargeted = GameItems.Retarget(_items, current, AppPaths.LibraryDirectory);
+        var changed = !ReferenceEquals(retargeted, _items);
+        _items = retargeted;
+        IReadOnlyList<string> added = [];
+        if (_config.Library.NewGamesFence is { } fenceId && GameItems.NewGames(previous, current) is { Count: > 0 } newGames)
+        {
+            var result = GameItems.AddNew(_items, fenceId, newGames, AppPaths.LibraryDirectory);
+            _items = result.Document;
+            added = [.. result.AddedIds.Select(id => _items.Find(id)!.Target)];
+            if (added.Count > 0) Log.Information("game library: {Count} new game(s) added to fence {FenceId}", added.Count, fenceId);
+        }
+        if (changed || added.Count > 0) ItemsChanged(checkTargets: added);
+        else RefreshWindows(); // new covers
+        _addGamesWindow?.ShowGames(GamesForAdding(_addGamesWindow.FenceId));
+    }
+
+    /// <summary>Fence menu → "Add games…": every game the scan found; those already in this fence unticked.</summary>
+    private void AddGames(FenceWindow window)
+    {
+        if (window.Kind != FenceKind.Items || _addGamesWindow is not null) return;
+        var fenceId = window.FenceId;
+        var dialog = new AddGamesWindow(fenceId) { Owner = window };
+        _addGamesWindow = dialog;
+        UpdateLibrary(); // the scan runs while the list is open
+        if (_library.Items.Count == 0) _library = LibraryWriter.ReadIndex(AppPaths.LibraryDirectory); // the last scan, until this one finishes
+        dialog.ShowGames(GamesForAdding(fenceId), scanning: _libraryScanning || _library.Items.Count == 0);
+        var accepted = dialog.ShowDialog() == true;
+        _addGamesWindow = null;
+        if (accepted && _config.Fences.Any(fence => fence.Id == fenceId && fence.Kind == FenceKind.Items) && dialog.Chosen.Count > 0)
+        {
+            var result = GameItems.AddNew(_items, fenceId, dialog.Chosen, AppPaths.LibraryDirectory);
+            _items = result.Document;
+            Log.Information("{Count} game(s) added to fence {FenceId}", result.AddedIds.Count, fenceId);
+            ItemsChanged(checkTargets: [.. result.AddedIds.Select(id => _items.Find(id)!.Target)]);
+            if (_windows.Values.FirstOrDefault(shown => shown.FenceId == fenceId) is { } target) target.SelectItems(result.AddedIds);
+        }
+        UpdateLibrary();
+    }
+
+    private IReadOnlyList<AddGamesWindow.Row> GamesForAdding(string fenceId)
+    {
+        var here = _items.Of(fenceId).Where(GameItems.IsGame).Select(item => item.GameId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
+        return [.. _library.Items.Select(game => new AddGamesWindow.Row(game, game.Game.Poster, AlreadyHere: GameCatalog.IdsOf(game.Game).Any(here.Contains)))];
+    }
+
+    /// <summary>A game item's menu (spec §2): Open, Show as, Open install folder, Copy path, Properties, Remove.</summary>
+    private void ShowGameItemMenu(FenceWindow window, VirtualItem item, bool fromKeyboard)
+    {
+        var menu = new ContextMenu();
+        void Command(ItemsControl parent, string header, Action run, bool? isChecked = null)
+        {
+            var command = new MenuItem { Header = header, IsChecked = isChecked == true };
+            command.Click += (_, _) => run();
+            parent.Items.Add(command);
+        }
+        var installed = CheckOf(item.Target).State == TargetState.Ok;
+        if (!installed)
+        {
+            Command(menu, "Remove from fence", () => RemoveItems(window, [item.Id]));
+            menu.Items.Add(new Separator());
+        }
+        Command(menu, "Open", () => OpenVirtualItem(window, item, runAsAdmin: false));
+        var showAs = new MenuItem { Header = "Show as" };
+        Command(showAs, "Cover tile", () => SetShowAs(item.Id, ItemShow.Cover), isChecked: GameItems.ShowsCover(item));
+        Command(showAs, "Icon", () => SetShowAs(item.Id, ItemShow.Icon), isChecked: !GameItems.ShowsCover(item));
+        menu.Items.Add(showAs);
+        Command(menu, "Open install folder", () => OpenInstallFolder(window, item.Target));
+        Command(menu, "Copy path", () => CopyText(item.Target));
+        menu.Items.Add(new Separator());
+        Command(menu, "Properties…", () => ShowProperties(window, item.Id, focusName: false));
+        if (installed) Command(menu, "Remove from fence", () => RemoveItems(window, [item.Id]));
+        menu.Items.Add(new Separator());
+        menu.Items.Add(new MenuItem { Header = "Shift+right-click: Windows' menu", IsEnabled = false });
+        window.ShowItemMenu(menu, fromKeyboard);
+    }
+
+    private void SetShowAs(string itemId, ItemShow showAs)
+    {
+        if (_items.Find(itemId) is not { } item) return;
+        _items = ItemEdits.Replace(_items, item with { ShowAs = showAs == ItemShow.Cover ? null : showAs }); // a cover is the usual look
+        ItemsChanged(checkTargets: []);
+    }
+}
diff --git a/src/NeoFences.App/FenceHost.Items.cs b/src/NeoFences.App/FenceHost.Items.cs
index 48fb2d9..2edc01e 100644
--- a/src/NeoFences.App/FenceHost.Items.cs
+++ b/src/NeoFences.App/FenceHost.Items.cs
@@ -7,6 +7,7 @@ using System.Windows.Media.Imaging;
 using System.Windows.Threading;
 using NeoFences.Core.Config;
 using NeoFences.Core.Items;
+using NeoFences.Core.Library;
 using NeoFences.Core.Model;
 using NeoFences.Shell;
 using Serilog;
@@ -62,7 +63,7 @@ public sealed partial class FenceHost
     {
         if (_items.Find(itemId) is not { } item) return;
         // The fence's window may have closed meanwhile (its box merged into another): then the question stands alone (final review M1).
-        var question = new MissingItemWindow(DisplayName(item), item.Target, state) { Owner = _windows.ContainsValue(window) ? window : null };
+        var question = new MissingItemWindow(DisplayName(item), item.Target, state, game: GameItems.IsGame(item)) { Owner = _windows.ContainsValue(window) ? window : null };
         if (question.ShowDialog() != true) return;
         if (question.Choice == MissingItemChoice.Locate) Locate(window, itemId);
         else if (question.Choice == MissingItemChoice.Remove) RemoveItems(window, [itemId]);
@@ -113,6 +114,11 @@ public sealed partial class FenceHost
             Command("Open", () => { foreach (var item in items) OpenVirtualItem(window, item, runAsAdmin: item.RunAsAdmin); });
             Command($"Remove {items.Count} items from fence", () => RemoveItems(window, [.. items.Select(item => item.Id)]));
         }
+        else if (GameItems.IsGame(items[0]))
+        {
+            ShowGameItemMenu(window, items[0], fromKeyboard); // M22
+            return;
+        }
         else
         {
             var item = items[0];
@@ -400,6 +406,7 @@ public sealed partial class FenceHost
         ScheduleSave();
         UpdateWatching();
         ForgetGoneTargets();
+        UpdateLibrary(); // M22: the scan runs while a fence holds a game item
         if (checkTargets.Count > 0) CheckTargets(checkTargets);
     }
 
diff --git a/src/NeoFences.App/FenceHost.Library.cs b/src/NeoFences.App/FenceHost.Library.cs
index 9da6ff6..6087dab 100644
--- a/src/NeoFences.App/FenceHost.Library.cs
+++ b/src/NeoFences.App/FenceHost.Library.cs
@@ -1,5 +1,6 @@
 using System.IO;
 using System.Windows.Threading;
+using NeoFences.Core.Items;
 using NeoFences.Core.Library;
 using NeoFences.Core.Model;
 using NeoFences.Shell;
@@ -28,6 +29,13 @@ public sealed partial class FenceHost
 
     private bool HasLibraryFence => _config.Fences.Any(fence => fence.IsLibrary);
 
+    /// <summary>
+    /// The scan runs while games are wanted (M22): an old library fence, a game item in any fence, a fence new games go to,
+    /// or the Add games… list open. Otherwise it sleeps (no watchers).
+    /// </summary>
+    private bool LibraryWanted => HasLibraryFence || _config.Library.NewGamesFence is not null || _addGamesWindow is not null
+                                  || _items.Fences.Values.Any(items => items.Any(GameItems.IsGame));
+
     /// <summary>The library folder's lister while the Game Library fence exists (a hidden library tab keeps listing, M9).</summary>
     private void EnsureLibraryLister()
     {
@@ -58,7 +66,7 @@ public sealed partial class FenceHost
     /// <summary>Starts the library when its fence appears, stops watching when it goes (EnsureLibraryLister calls this).</summary>
     private void UpdateLibrary()
     {
-        if (HasLibraryFence == _libraryActive) return;
+        if (LibraryWanted == _libraryActive) return;
         _libraryActive = !_libraryActive;
         if (_libraryActive)
         {
@@ -133,12 +141,14 @@ public sealed partial class FenceHost
             DisposeWatchers(watch);
             return;
         }
+        var previous = _library;
         if (state is not null) _library = state;
         _libraryStatus = $"Last scan {DateTime.Now:HH:mm}: {_library.Items.Count} games"
                          + (unreadable.Count > 0 ? $"; not readable right now: {string.Join(", ", unreadable.Select(GameCatalog.SourceName))}" : ".");
         if (state is null || !_libraryActive) DisposeWatchers(watch); // a failed scan keeps the watchers it had (final review I2)
         else ReplaceLibraryWatchers(watch);
         _libraryLister?.Refresh(); // new art, order
+        if (state is not null) ApplyGames(previous, state); // M22: game items follow the scan; new games go to their fence
         RefreshSettings();
         if (!_libraryScanAgain) return;
         _libraryScanAgain = false;
@@ -296,11 +306,13 @@ public sealed partial class FenceHost
     }
 
     private LibraryView LibrarySettingsView() => new(
-        HasFence: HasLibraryFence,
+        HasFence: LibraryWanted,
         Folders: _config.Library.Folders,
         Sources: _config.Library.Sources,
         Hidden: HiddenGamesForSettings(),
-        Status: HasLibraryFence ? _libraryStatus : "No Game Library fence yet: tray → New Game Library fence.");
+        Status: LibraryWanted ? _libraryStatus : "No games in any fence yet: fence menu → Add games…, or choose where new games go.",
+        Fences: [.. _config.Fences.Where(fence => fence.Kind == FenceKind.Items).Select(fence => (fence.Id, fence.Title))],
+        NewGamesFence: _config.Library.NewGamesFence);
 
     private void WireLibrarySettings(SettingsWindow window)
     {
@@ -320,6 +332,11 @@ public sealed partial class FenceHost
             Change(library => library with { Hidden = [.. library.Hidden.Where(hiddenId => !ids.Contains(hiddenId))] }, "show again");
         };
         window.RefreshLibraryRequested += () => ScanLibrary(full: true);
+        window.NewGamesFenceChanged += fenceId =>
+        {
+            Change(library => library with { NewGamesFence = fenceId }, "new games go to");
+            UpdateLibrary(); // a fence for new games: the scan runs
+        };
     }
 
     /// <summary>One row per hidden game (all its ids together); a hidden id no scan finds any more is listed by itself.</summary>
diff --git a/src/NeoFences.App/FenceHost.cs b/src/NeoFences.App/FenceHost.cs
index 395a99a..4cbde18 100644
--- a/src/NeoFences.App/FenceHost.cs
+++ b/src/NeoFences.App/FenceHost.cs
@@ -6,6 +6,7 @@ using NeoFences.Core.Config;
 using NeoFences.Core.Input;
 using NeoFences.Core.Items;
 using NeoFences.Core.Layouts;
+using NeoFences.Core.Library;
 using NeoFences.Core.Lifecycle;
 using NeoFences.Core.Model;
 using NeoFences.Shell;
@@ -115,6 +116,7 @@ public sealed partial class FenceHost
         Log.Information("items loaded from {Source} (read-only: {IsReadOnly}, corrupt copy: {CorruptCopyPath}): {Count} item(s)",
             loadedItems.Source, loadedItems.IsReadOnly, loadedItems.CorruptCopyPath, loadedItems.Document.Fences.Values.Sum(items => items.Count));
         _items = loadedItems.Document;
+        MigrateGames(LibraryWriter.ReadIndex(AppPaths.LibraryDirectory)); // M22: an old Game Library fence becomes game items (a snapshot first)
         if (loadedItems.Source == ConfigLoadSource.Primary && !loadedItems.IsReadOnly) CleanUnusedPictures(ItemEdits.ImagesInUse(_items), _snapshots.Directory);
         _watchdog.LaunchDetached(Environment.ProcessId);
         ApplyStartup(); // after a power loss NeoFences must come back by itself (ADR-019)
@@ -263,6 +265,7 @@ public sealed partial class FenceHost
         window.DrivesChanged += OnDrivesChanged;
         window.NewLibraryRequested += CreateLibraryFence;
         window.NewFolderViewRequested += () => NewFolderView(window.Handle); // M21
+        window.AddGamesRequested += () => AddGames(window); // M22
         window.OpenFolderRequested += () => OpenViewFolder(window);
         window.ViewSettingsRequested += () => EditFolderView(window);
         window.StartupToggled += SetStartWithWindows;
@@ -329,7 +332,9 @@ public sealed partial class FenceHost
             RenderView(window, shown); // M21
             return;
         }
-        window.SetItems([.. _items.Of(shown.Id).Select(item => new ShownItem(item.Id, item.Target, item.OwnName, item.Icon, item.Note, StateOf(item.Target)))]);
+        var covers = _items.Of(shown.Id).Any(GameItems.ShowsCover) ? LibraryArt() : null; // M22: game items shown as covers
+        window.SetItems([.. _items.Of(shown.Id).Select(item => new ShownItem(item.Id, item.Target, item.OwnName, item.Icon, item.Note, StateOf(item.Target),
+            Tile: GameItems.ShowsCover(item), TileArt: covers is not null && covers.TryGetValue(item.Target, out var cover) ? cover : null, IsGame: GameItems.IsGame(item)))]);
     }
 
     /// <summary>Opens a path NeoFences knows (a library game, the logs or data folder).</summary>
@@ -947,6 +952,7 @@ public sealed partial class FenceHost
         }
         Log.Information("restoring snapshot {Name} from {Path}", snapshot.Name, path);
         (_config, _items) = Snapshots.Restore(_config, snapshot);
+        MigrateGames(_library.Items.Count > 0 ? _library : LibraryWriter.ReadIndex(AppPaths.LibraryDirectory)); // M22: a snapshot from before games became items
         SaveNow();
         SyncBoxes();
         // Windows that kept their fence still show its old title, icon size and labels (final review I1).
@@ -957,6 +963,7 @@ public sealed partial class FenceHost
             window.SetTitle(shown.Title);
         }
         RefreshWindows(); // a fence that became (or stopped being) a folder view shows its new kind (M21)
+        UpdateLibrary(); // M22: the restored fences may hold game items, or none
         ForgetGoneTargets(); // records of items the restore took away (M20)
         CheckAllTargets(); // the restored items' targets may have changed since
         _settingsWindow?.ShowSnapshotNotice($"Restored \"{snapshot.Name}\".", failed: false); // replaces an earlier failure line (final review M1)
@@ -1180,7 +1187,6 @@ public sealed partial class FenceHost
         [
             .. UpdateTrayItems(), // M17: "Restart to update to v…" first while an update waits
             new TrayMenuItem(TrayNewFence, "New fence", Enabled: !_paused),
-            new TrayMenuItem(TrayNewLibrary, "New Game Library fence", Enabled: !_paused),
             new TrayMenuItem(TrayNewFolderView, "New folder view…", Enabled: !_paused),
             new TrayMenuItem(TrayAddFromDesktop, "Add from desktop…", Enabled: !_paused),
             new TrayMenuItem(TrayQuickHide, "Quick-hide", Checked: _quickHidden, Enabled: !_paused),
diff --git a/src/NeoFences.App/FenceItemView.cs b/src/NeoFences.App/FenceItemView.cs
index 4ba799d..8e79f4b 100644
--- a/src/NeoFences.App/FenceItemView.cs
+++ b/src/NeoFences.App/FenceItemView.cs
@@ -10,8 +10,10 @@ namespace NeoFences.App;
 /// What a fence shows of one item (M18): a virtual item (key = its id), or a Game Library shortcut (key = its path).
 /// The host builds these from its data; the window shows them in place (<see cref="FenceWindow.SetItems"/>).
 /// </summary>
+/// <param name="Tile">A 2:3 tile (M22: a game item shown as its cover); <paramref name="TileArt"/> is its poster or logo.</param>
+/// <param name="IsGame">A game item (M22): "Not installed" instead of "Missing".</param>
 public sealed record ShownItem(string Key, string Target, string? Name = null, ItemIcon? Icon = null, string? Note = null,
-    TargetState State = TargetState.Ok);
+    TargetState State = TargetState.Ok, bool Tile = false, (string Path, bool IsPoster)? TileArt = null, bool IsGame = false);
 
 /// <summary>One item as a fence shows it. Label and icon start as placeholders and fill in from <see cref="IconLoader"/>.</summary>
 public sealed class FenceItemView : INotifyPropertyChanged
@@ -57,7 +59,7 @@ public sealed class FenceItemView : INotifyPropertyChanged
     /// <summary>The note, or what is wrong with the target (spec §4); the name otherwise.</summary>
     public string ToolTipText => State switch
     {
-        TargetState.Missing => $"Missing: {Target}",
+        TargetState.Missing => IsGame ? $"Not installed: {Label}" : $"Missing: {Target}",
         TargetState.Unavailable => TargetChecks.IsNetworkPath(Target) ? $"Network location not reachable: {Target}"
             : $"Drive {TargetChecks.RootOf(Target)?.TrimEnd('\\') ?? "?"} is not connected: {Target}",
         _ => Note is { Length: > 0 } note ? $"{Label}\n{note}" : Label,
@@ -76,6 +78,9 @@ public sealed class FenceItemView : INotifyPropertyChanged
         OwnIcon = shown.Icon;
         OwnName = string.IsNullOrWhiteSpace(shown.Name) ? null : shown.Name;
         Note = shown.Note;
+        Tile = shown.Tile;
+        TileArt = shown.TileArt;
+        IsGame = shown.IsGame;
         State = shown.State;
         if (OwnName is not null) Label = OwnName;
         else if (reload || Label.Length == 0) Label = PlaceholderName(Target);
@@ -104,6 +109,13 @@ public sealed class FenceItemView : INotifyPropertyChanged
         set { field = value; Changed(); }
     }
 
+    /// <summary>The host wants a tile for this item (M22: a game item's cover), and its art; the window decides <see cref="IsTile"/>.</summary>
+    public bool Tile { get; private set; }
+
+    public (string Path, bool IsPoster)? TileArt { get; private set; }
+
+    public bool IsGame { get; private set; }
+
     /// <summary>A Game Library tile (M12): a 2:3 tile with a poster, a logo or the icon centred.</summary>
     public bool IsTile
     {
diff --git a/src/NeoFences.App/FenceWindow.xaml b/src/NeoFences.App/FenceWindow.xaml
index 67bd73d..5766f03 100644
--- a/src/NeoFences.App/FenceWindow.xaml
+++ b/src/NeoFences.App/FenceWindow.xaml
@@ -20,6 +20,7 @@
         <Visibility x:Key="ShortcutArrowVisibility">Collapsed</Visibility>
         <sys:Double x:Key="TileWidth" xmlns:sys="clr-namespace:System;assembly=System.Runtime">72</sys:Double>
         <sys:Double x:Key="TileHeight" xmlns:sys="clr-namespace:System;assembly=System.Runtime">108</sys:Double>
+        <sys:Double x:Key="TileCellWidth" xmlns:sys="clr-namespace:System;assembly=System.Runtime">84</sys:Double>
         <!-- Thin scrollbar (spec §6): no arrows, a rounded thumb that brightens under the mouse. -->
         <Style TargetType="ScrollBar">
             <Setter Property="Width" Value="6" />
@@ -78,13 +79,15 @@
                     <ContextMenu x:Name="BodyContextMenu">
                         <MenuItem x:Name="AddItemItem" Header="Add item…" />
                         <MenuItem x:Name="AddFromDesktopItem" Header="Add from desktop…" />
+                        <MenuItem x:Name="AddGamesItem" Header="Add games…" />
                         <MenuItem x:Name="RefreshItem" Header="Refresh" />
                         <!-- Folder views (M21): shown only on a view. -->
                         <MenuItem x:Name="OpenFolderItem" Header="Open folder" Visibility="Collapsed" />
                         <MenuItem x:Name="ViewSettingsItem" Header="Folder view settings…" Visibility="Collapsed" />
                         <Separator />
                         <MenuItem x:Name="NewFenceItem" Header="New fence" />
-                        <MenuItem x:Name="NewLibraryItem" Header="New Game Library fence" />
+                        <!-- M22: games are items in any fence; the library fence kind stays in the code, not in the menu. -->
+                        <MenuItem x:Name="NewLibraryItem" Header="New Game Library fence" Visibility="Collapsed" />
                         <MenuItem x:Name="NewFolderViewItem" Header="New folder view…" />
                         <MenuItem x:Name="RenameItem" Header="Rename fence" />
                         <MenuItem x:Name="IconSizeItem" Header="Icon size" />
@@ -185,6 +188,7 @@
                                 </StackPanel>
                                 <DataTemplate.Triggers>
                                     <DataTrigger Binding="{Binding IsTile}" Value="True">
+                                        <Setter TargetName="Cell" Property="Width" Value="{DynamicResource TileCellWidth}" />
                                         <Setter TargetName="IconGrid" Property="Visibility" Value="Collapsed" />
                                         <Setter TargetName="Tile" Property="Visibility" Value="Visible" />
                                     </DataTrigger>
diff --git a/src/NeoFences.App/FenceWindow.xaml.cs b/src/NeoFences.App/FenceWindow.xaml.cs
index 4161bd9..e366804 100644
--- a/src/NeoFences.App/FenceWindow.xaml.cs
+++ b/src/NeoFences.App/FenceWindow.xaml.cs
@@ -149,6 +149,8 @@ public partial class FenceWindow : Window
     public event Action? RefreshRequested;
     /// <summary>Fence menu → "New Game Library fence" (M12).</summary>
     public event Action? NewLibraryRequested;
+    /// <summary>Fence menu → "Add games…" (M22).</summary>
+    public event Action? AddGamesRequested;
     /// <summary>Fence menu → "New folder view…" (M21).</summary>
     public event Action? NewFolderViewRequested;
     /// <summary>A folder view's "Open folder" (its menu, or the "+ N more" line).</summary>
@@ -207,6 +209,7 @@ public partial class FenceWindow : Window
         PreviewKeyDown += OnTabKeys;
         NewLibraryItem.Click += (_, _) => NewLibraryRequested?.Invoke();
         NewFolderViewItem.Click += (_, _) => NewFolderViewRequested?.Invoke();
+        AddGamesItem.Click += (_, _) => AddGamesRequested?.Invoke();
         OpenFolderItem.Click += (_, _) => OpenFolderRequested?.Invoke();
         ViewSettingsItem.Click += (_, _) => ViewSettingsRequested?.Invoke();
         MoreLine.MouseLeftButtonUp += (_, click) => { click.Handled = true; OpenFolderRequested?.Invoke(); };
@@ -298,6 +301,7 @@ public partial class FenceWindow : Window
         var view = _kind == FenceKind.View;
         AddItemItem.Visibility = items ? Visibility.Visible : Visibility.Collapsed;
         AddFromDesktopItem.Visibility = items ? Visibility.Visible : Visibility.Collapsed;
+        AddGamesItem.Visibility = items ? Visibility.Visible : Visibility.Collapsed;
         SortItem.Visibility = _kind == FenceKind.Library ? Visibility.Collapsed : Visibility.Visible;
         foreach (var sortItem in SortItem.Items.OfType<MenuItem>()) sortItem.IsChecked = view && Equals(sortItem.Tag, fence.View!.Sort);
         OpenFolderItem.Visibility = view ? Visibility.Visible : Visibility.Collapsed;
@@ -342,8 +346,9 @@ public partial class FenceWindow : Window
 
     private void ApplyArt(FenceItemView view)
     {
-        view.IsTile = IsLibrary;
-        if (!IsLibrary || !_libraryArt.TryGetValue(view.Key, out var art))
+        view.IsTile = IsLibrary || view.Tile; // the library's tiles (M12), game items shown as covers (M22)
+        (string Path, bool IsPoster)? chosen = IsLibrary ? (_libraryArt.TryGetValue(view.Key, out var libraryArt) ? libraryArt : null) : view.TileArt;
+        if (!view.IsTile || chosen is not { } art)
         {
             view.ArtPath = null;
             view.Art = null;
@@ -651,6 +656,7 @@ public partial class FenceWindow : Window
             {
                 if (found != index) _items.Move(found, index);
                 if (_items[index].Update(shown) && IsLoaded) _iconLoader.Request(_items[index], iconSizePx);
+                ApplyArt(_items[index]); // shown as a cover now, or an icon again (M22); the same art is not loaded twice
                 continue;
             }
             var view = new FenceItemView(shown);
@@ -712,8 +718,7 @@ public partial class FenceWindow : Window
     /// <summary>Covers are decoded at the tile's pixel width: a new icon size or monitor DPI decodes them again (final review I3).</summary>
     private void ReloadArt()
     {
-        if (!IsLibrary) return;
-        foreach (var view in _items)
+        foreach (var view in _items.Where(view => view.IsTile))
         {
             view.ArtPath = null;
             ApplyArt(view);
@@ -740,6 +745,7 @@ public partial class FenceWindow : Window
         // Game Library tiles are 2:3, 1.5 × the icon size wide (M12).
         Resources["TileWidth"] = Math.Round(_iconSizeDips * 1.5);
         Resources["TileHeight"] = Math.Round(_iconSizeDips * 2.25);
+        Resources["TileCellWidth"] = Math.Round(_iconSizeDips * 1.5) + 12.0; // a cover tile among icons (M22)
         // With labels: room for two short words under small icons. Icons only: a tight grid.
         Resources["ItemWidth"] = IsLibrary ? Math.Round(_iconSizeDips * 1.5) + 12.0
             : _labelMode == LabelMode.Always ? LabelledItemWidth : _iconSizeDips + 12.0;
diff --git a/src/NeoFences.App/MissingItemWindow.xaml.cs b/src/NeoFences.App/MissingItemWindow.xaml.cs
index 1337afa..8a85fa1 100644
--- a/src/NeoFences.App/MissingItemWindow.xaml.cs
+++ b/src/NeoFences.App/MissingItemWindow.xaml.cs
@@ -10,9 +10,20 @@ public partial class MissingItemWindow : Window
 {
     public MissingItemChoice Choice { get; private set; }
 
-    public MissingItemWindow(string name, string target, TargetState state)
+    /// <param name="game">A game item (M22): "not installed", no Locate….</param>
+    public MissingItemWindow(string name, string target, TargetState state, bool game = false)
     {
         InitializeComponent();
+        if (game && state == TargetState.Missing)
+        {
+            Headline.Text = $"\"{name}\" is not installed";
+            Explanation.Text = "The game was uninstalled, or its launcher no longer lists it. Reinstall it and the item comes back by itself.";
+            TargetText.Text = "";
+            LocateButton.Visibility = Visibility.Collapsed;
+            RemoveButton.IsDefault = true;
+            RemoveButton.Click += (_, _) => Choose(MissingItemChoice.Remove);
+            return;
+        }
         Headline.Text = state == TargetState.Unavailable ? $"\"{name}\" is not available right now" : $"\"{name}\" is missing";
         Explanation.Text = state == TargetState.Unavailable
             ? TargetChecks.IsNetworkPath(target)
diff --git a/src/NeoFences.App/SettingsWindow.Library.cs b/src/NeoFences.App/SettingsWindow.Library.cs
index c001065..9092b9d 100644
--- a/src/NeoFences.App/SettingsWindow.Library.cs
+++ b/src/NeoFences.App/SettingsWindow.Library.cs
@@ -14,8 +14,10 @@ public sealed record HiddenGame(string Id, string Name, IReadOnlyList<string> Al
     public override string ToString() => Name;
 }
 
-/// <summary>What Settings → Game Library shows (M12).</summary>
-public sealed record LibraryView(bool HasFence, IReadOnlyList<string> Folders, LibrarySources Sources, IReadOnlyList<HiddenGame> Hidden, string Status);
+/// <summary>What Settings → Game Library shows (M12; M22: the fences new games can go to, and the chosen one).</summary>
+/// <param name="HasFence">The scan runs (games are wanted somewhere).</param>
+public sealed record LibraryView(bool HasFence, IReadOnlyList<string> Folders, LibrarySources Sources, IReadOnlyList<HiddenGame> Hidden, string Status,
+    IReadOnlyList<(string Id, string Title)> Fences, string? NewGamesFence);
 
 /// <summary>
 /// Settings → Game Library (M12, spec §4): game folders, a checkbox per source, hidden games with "Show again", and
@@ -27,6 +29,8 @@ public partial class SettingsWindow
     public event Action<LibrarySources>? LibrarySourcesChanged;
     public event Action<string>? ShowGameAgainRequested;
     public event Action? RefreshLibraryRequested;
+    /// <summary>"New games go to" (M22): a fence id, or null for nowhere.</summary>
+    public event Action<string?>? NewGamesFenceChanged;
 
     private IReadOnlyList<string> _libraryFolders = [];
 
@@ -57,6 +61,11 @@ public partial class SettingsWindow
         HiddenGameList.SelectionChanged += (_, _) => ShowGameAgainButton.IsEnabled = HiddenGameList.SelectedItem is not null;
         ShowGameAgainButton.Click += (_, _) => { if (HiddenGameList.SelectedItem is HiddenGame game) ShowGameAgainRequested?.Invoke(game.Id); };
         RefreshLibraryButton.Click += (_, _) => RefreshLibraryRequested?.Invoke();
+        NewGamesBox.SelectionChanged += (_, _) =>
+        {
+            if (_updating || NewGamesBox.SelectedItem is not ComboBoxItem chosen) return;
+            NewGamesFenceChanged?.Invoke(chosen.Tag as string);
+        };
         AutomationProperties.SetHelpText(GameFolderList, LibraryDescription.Text);
     }
 
@@ -85,6 +94,10 @@ public partial class SettingsWindow
         ShowGameAgainButton.IsEnabled = HiddenGameList.SelectedItem is not null;
         HiddenGamesEmpty.Visibility = view.Hidden.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
         LibraryStatus.Text = view.Status;
+        NewGamesBox.Items.Clear();
+        NewGamesBox.Items.Add(new ComboBoxItem { Content = "Nowhere", Tag = null });
+        foreach (var (id, title) in view.Fences) NewGamesBox.Items.Add(new ComboBoxItem { Content = title.Length > 0 ? title : "(untitled fence)", Tag = id });
+        NewGamesBox.SelectedItem = NewGamesBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == view.NewGamesFence) ?? NewGamesBox.Items[0];
         RefreshLibraryButton.IsEnabled = view.HasFence;
     }
 }
diff --git a/src/NeoFences.App/SettingsWindow.xaml b/src/NeoFences.App/SettingsWindow.xaml
index 975d984..41201f2 100644
--- a/src/NeoFences.App/SettingsWindow.xaml
+++ b/src/NeoFences.App/SettingsWindow.xaml
@@ -241,7 +241,11 @@
             <Border x:Name="LibraryCard" Style="{StaticResource Card}">
                 <StackPanel>
                     <TextBlock x:Name="LibraryDescription" Style="{StaticResource Description}" Margin="0,0,0,8"
-                               Text="The Game Library fence lists every installed game it finds. Launcher games start through their launcher. Nothing is downloaded, and your games and files are never changed." />
+                               Text="Finds the games installed on this PC. Put them in any fence with fence menu → Add games…; newly installed games go to the fence chosen below. Launcher games start through their launcher. Nothing is downloaded, and your games and files are never changed." />
+                    <StackPanel Orientation="Horizontal" Margin="0,4,0,8">
+                        <TextBlock Text="New games go to" VerticalAlignment="Center" Margin="0,0,10,0" />
+                        <ComboBox x:Name="NewGamesBox" MinWidth="200" AutomationProperties.Name="New games go to" />
+                    </StackPanel>
                     <TextBlock Text="Game folders (each sub-folder is a game)" Margin="0,4,0,4" />
                     <ListBox x:Name="GameFolderList" MaxHeight="120" AutomationProperties.Name="Game folders" />
                     <WrapPanel Margin="0,8,0,0">
@@ -261,7 +265,7 @@
                         <CheckBox x:Name="ShortcutsSourceBox" Margin="0,0,16,6" />
                     </WrapPanel>
                     <TextBlock Text="Hidden games" Margin="0,8,0,4" />
-                    <TextBlock x:Name="HiddenGamesEmpty" Style="{StaticResource Description}" Text="None. Right-click a game → Hide from library." />
+                    <TextBlock x:Name="HiddenGamesEmpty" Style="{StaticResource Description}" Text="None." />
                     <ListBox x:Name="HiddenGameList" MaxHeight="120" AutomationProperties.Name="Hidden games" />
                     <WrapPanel Margin="0,8,0,0">
                         <Button x:Name="ShowGameAgainButton" Content="Show again" Margin="0,0,8,6" IsEnabled="False" />
````

- [ ] **Step 2: Build and test.** `dotnet build` → `0 Warning(s)`; `dotnet test` → 511 passed.
  (UI behaviour is verified by TEST-CHECKLIST AH in Task 5.)

- [ ] **Step 3: Commit.** `git add -A && git commit -m "feat: made games items in any fence, shown as covers or icons, with Add games and a new-games fence"`

### Task 3: Docs

**Files:**
- Modify: `docs/DECISIONS.md` (ADR-045), `docs/ARCHITECTURE.md` (layers, components, items shape, 0.12.0 status),
  `docs/FEATURES.md` (Game Library row; one kind of fence row), `docs/TEST-CHECKLIST.md` (section AH)
- Create: `docs/research/m22-games-as-items.md`

- [ ] **Step 1: Apply.** Write this patch to `m22-3-docs.patch` and apply it:

````diff
diff --git a/docs/ARCHITECTURE.md b/docs/ARCHITECTURE.md
index 8889715..98260c8 100644
--- a/docs/ARCHITECTURE.md
+++ b/docs/ARCHITECTURE.md
@@ -50,10 +50,11 @@ happen) is behind Shift+right-click, under a line saying it acts on the real fil
 ```
 ┌─────────────────────────── NeoFences.App (WPF) ───────────────────────────┐
 │ FenceHost (+ .Items .Watching .Library .Appearance .Updates .DesktopFill  │
-│            .FolderViews)                                                  │
+│            .FolderViews .GameItems)                                       │
 │ FenceWindow · FenceItemView · IconLoader · ItemPropertiesWindow           │
 │ AppPickerWindow · RelocateWindow · DesktopFillWindow · MissingItemWindow  │
-│ FolderViewWindow · SettingsWindow · FolderLister · DrawFenceOverlay       │
+│ FolderViewWindow · AddGamesWindow · SettingsWindow · FolderLister         │
+│ DrawFenceOverlay                                                          │
 └──────────────┬────────────────────────────────────────────────────────────┘
 ┌──────────────▼─────────── NeoFences.Shell (Win32/COM, CsWin32) ───────────┐
 │ DesktopHost · DesktopIcons · ShellItems · ShellItemMenu · ShellDragDrop   │
@@ -78,7 +79,8 @@ happen) is behind Shift+right-click, under a line saying it acts on the real fil
 | Core/LayoutEngine | display fingerprint, map/scale layouts between monitor setups, clamp, snap, smart placement of new fences (`FreeSpot`, ADR-020) | — |
 | Core/FenceEdits, FenceTabs, Snapping | rename / icon size / lock / colours / new / delete fence; tabs (ADR-029); snap to 8 px gap or aligned edges during drags (ADR-015) | — |
 | Core/FencePlacement, Core/Lifecycle | px↔DIP placement, containing monitor; `RunState` (hide icons, quick-hide, Pause, game mode), session-end policy, restart throttle, watcher backoff | — |
-| Core/Library | Valve/Epic parsing, `GameCatalog` merge, `LibraryFiles` plan, `GameLaunchers.LauncherOf` (ADR-032) | — |
+| Core/Library | Valve/Epic parsing, `GameCatalog` merge, `LibraryFiles` plan, `GameLaunchers.LauncherOf` (ADR-032); `GameItems` (M22): `Migrate` (a library fence → game items), `NewGames`, `AddNew`, `Retarget` (by game id), `ShowsCover` | — |
+| App/FenceHost.GameItems, AddGamesWindow (M22) | games as items (ADR-045): migration at start / after a restore / after a scan (a snapshot first), new games into `Library.NewGamesFence`, Add games…, the game item menu (Show as cover / icon, Open install folder), "not installed" | WPF (Fluent) |
 | App/FenceHost | orchestrates config + items, monitors, windows, debounced saves (both files, config first), display changes, Explorer restarts, session end, snapshots, tray | — |
 | App/FenceHost.Items | open (arguments, run as administrator; a missing target asks Locate… / Remove), NeoFences' item menu, Windows' menu on Shift+right-click, Properties, Add item…, Locate…, drops, drag-out, one-time sort, item pictures copied into `icons\` | WPF ContextMenu, Clipboard |
 | App/FenceHost.Watching | watched folders (`FolderWatcher` + removal notices), rename-follow, target checks (start, 5 min, fence shown, Refresh, drive arrival/removal), per-fence refresh throttle, game-mode deferral | DispatcherTimer |
@@ -122,7 +124,7 @@ happen) is behind Shift+right-click, under a line saying it acts on the real fil
 - `config.json` (+ `.bak`, `.tmp` transient) — schema 5
   - shape: `{ schemaVersion, settings: { hideDesktopIcons, peekHotkey, … }, fences: [ { id, title, isLibrary, view: { path, show, sort, newest, patterns } (M21), iconSize, rolledUp, locked, labels, tabs, activeTab, tabColor, customColor } ], layouts: { <fingerprint>: { monitors, fences: { <fenceId>: { monitor, x, y, w, h } } } }, library, lastLayoutFingerprint }`
 - `items.json` (+ `.bak`) — schema 1 (ADR-041)
-  - shape: `{ schema, fences: { <fenceId>: [ { id, target, name, icon: { file, index } | { image }, arguments, runAsAdmin, note } ] } }`
+  - shape: `{ schema, fences: { <fenceId>: [ { id, target, name, icon: { file, index } | { image }, arguments, runAsAdmin, note, gameId, showAs (M22) } ] } }`
   - a list is removed only with its fence (Delete fence); lists of fences the config does not have stay (a fallback config never costs items; ADR-041 amended)
 - `icons\<itemId>-<guid>.png` — pictures chosen as item icons (≤ 256 px); unused ones deleted at start
 - `backups\config-<yyyyMMdd>.json`, `backups\items-<yyyyMMdd>.json` (keep 10 each), `backups\pre-schema-5-config.json`
@@ -153,6 +155,13 @@ reuses the Game Library's last scan and never counts a drive root or system fold
 places (Control Panel, This PC) and `file:///` links sort as folders and files (`ShellLinks.ShellTargetOf`); stale
 per-target records dropped after a restore and when a check outlives its item; failed background checks logged.
 
+**0.12.0 (M22, one kind of fence: games become items)**: a game is a virtual item (`GameId`, `ShowAs`) whose target is
+NeoFences' own shortcut for it in `library\`; any fence can hold games next to other items, as cover tiles or icons
+(cells sized per item). The library scan is the engine: it runs while game items exist, a fence takes new games, or Add
+games… is open; after each scan game items follow their game's shortcut and new games go to the chosen fence. An old
+Game Library fence becomes an items fence once (a "Before games became items" snapshot first). The library fence kind
+stays in the code, not in the menus (ADR-045, `research/m22-games-as-items.md`).
+
 **0.11.0 (M21, folder views)**: a fence can show one folder live, read-only (ADR-044): "New folder view…" (tray, fence
 menu) or "Show as folder view" on a folder item; per view: files and folders / files only / folders only, type patterns,
 sort (kept, live), only the newest N; Downloads and Screenshots start newest first with 30; at most 500 entries, then
diff --git a/docs/DECISIONS.md b/docs/DECISIONS.md
index 26272fb..026eabe 100644
--- a/docs/DECISIONS.md
+++ b/docs/DECISIONS.md
@@ -1213,4 +1213,25 @@ game and app folders, work folders, Downloads and Screenshots, and USB sticks, e
   folder's rename.
 
 **Consequences.** Each view holds two `FileSystemWatcher`s and, on a removable drive, a removal notice; no limit on the
-number of views (personal use). Auto-collect rules stay for later.
\ No newline at end of file
+number of views (personal use). Auto-collect rules stay for later.
+
+## ADR-045 — One kind of fence: games become items
+**Date:** 2026-10-05 · **Status:** Accepted · **Refines:** ADR-032 (Game Library), ADR-044 (folder views)
+
+**Context.** The user (2026-10-05): fences should not have types to choose. A fence is a container of elements whose
+kind and own settings decide how they look; later it also holds a folder panel, widgets (clock, calendar) and elements
+of several sizes. The first step (M22, 0.12.0) makes games ordinary items.
+
+**Decision.**
+- A game item is a virtual item with `GameId` whose target is NeoFences' own shortcut for the game in `library\`;
+  `ShowAs` (Cover / Icon, null = cover) picks its look. Opening, Missing, drag, Properties and snapshots are the items'.
+- The library scan is the engine, not a fence: it runs while game items exist, `Library.NewGamesFence` is set, or Add
+  games… is open. After each scan game items follow their game's current shortcut (`Retarget`) and games no earlier scan
+  had go to `NewGamesFence`.
+- An old Game Library fence becomes an items fence with one game item per game, once, after a snapshot "Before games
+  became items"; it becomes the new-games fence when none is chosen.
+- Kinds stay in the code (`Fence.IsLibrary`, `FenceKind.Library`); "New Game Library fence" leaves the menus.
+- Folder views stay a fence setting for now (not decided).
+
+**Consequences.** A removed game item is not added again; an uninstalled game's items show "not installed" and come back
+on reinstall. Hiding games stays in Settings. Mixed fences size each cell by its item (tile or icon).
\ No newline at end of file
diff --git a/docs/FEATURES.md b/docs/FEATURES.md
index 335cb2b..e784253 100644
--- a/docs/FEATURES.md
+++ b/docs/FEATURES.md
@@ -64,7 +64,8 @@ Sources for Fences 6: stardock.com news posts "Now Announcing: Fences 6", "Fence
 | Live blur over Wallpaper Engine | v1 (M0/M2) | wip | ADR-011: layered + accent blur; user approved the current tint |
 | Blur tint preference (lighter/darker) | v1.7 (M14) | done | background strength slider, one value per Windows tone (ADR-036) |
 | Search palette across all fences | — | parked | built on branch `m15-search-palette` (local history bundle only), not merged |
-| Game Library fence (Steam/Epic/GOG/Ubisoft Connect/EA, cover art) | v1.5 | done | launchers, Xbox, game folders, Desktop game shortcuts; a game dragged into a fence becomes an item (0.9) (ADR-032) |
+| Game Library (Steam/Epic/GOG/Ubisoft Connect/EA, cover art) | v1.5 | done | launchers, Xbox, game folders, Desktop game shortcuts (ADR-032); since 0.12 games are items in any fence (cover tile or icon, Add games…, new games go to a chosen fence; ADR-045) |
+| One kind of fence: any item in any fence, its look from its kind and settings | 0.12 (M22) | done (games) | ADR-045; next: a folder panel element, widgets (clock, calendar), element sizes |
 | Folder views: types, files/folders only, newest N, live sort, "+ N more" | 0.11 (M21) | done | ADR-044; Downloads/Screenshots start newest first |
 | Auto-collect rules (the other half of dynamic collections) | later | — | replaces Rules (ADR-040) |
 | Theme packs: "Nanosuit" (Crysis HUD), "Animus" (Assassin's Creed) | v2 | — | |
diff --git a/docs/TEST-CHECKLIST.md b/docs/TEST-CHECKLIST.md
index 490449a..76e6097 100644
--- a/docs/TEST-CHECKLIST.md
+++ b/docs/TEST-CHECKLIST.md
@@ -572,3 +572,21 @@ Added after the M19 final review:
 | AG20 | A view of the pendrive's root `G:\` (New folder view… → the drive) | the stick's own root is listed (not some folder on G: NeoFences was started in); title "G:" |
 | AG21 | A view of an unreachable share (`\\nosuchhost\share`) → Folder view settings… → Browse… | the window greys for at most 2 s, then the folder dialog opens at Windows' default place; no fence freezes |
 | AG22 | Leave a view's stick unplugged for a minute; read the log | one "cannot watch" warning for the outage, not one every 7 s |
+
+## AH — 0.12.0 games as items (M22)
+
+| ID | Steps | Expected |
+|---|---|---|
+| AH1 | Start 0.12.0 with a Game Library fence | the fence is now a normal fence with the same games in the same order, as cover tiles; tray → Restore snapshot lists "Before games became items (…)"; Settings → Game Library → New games go to: that fence |
+| AH2 | Tray and fence menus | no "New Game Library fence"; fence menu of an items fence has "Add games…" |
+| AH3 | Ctrl+drag a game tile into another fence (Apps) | a copy there, shown as a cover tile; its row grows, other cells keep their size |
+| AH4 | Right-click it → Show as → Icon; then back to Cover tile | an icon cell like the apps around it; then the tile again |
+| AH5 | Right-click a game item | Open · Show as · Open install folder · Copy path · Properties… · Remove from fence; Open install folder opens the game's folder |
+| AH6 | Properties → a name of your own | the name shows under the cover; the target box is NeoFences' shortcut |
+| AH7 | Fence menu → Add games… in Apps | every game listed with its source; games already in Apps unticked and marked "already here"; Add puts the ticked ones at the end |
+| AH8 | A new game: a new sub-folder with a .exe in `D:\GameLibrary` (test), wait for the scan | a new item at the end of the new-games fence; nowhere else |
+| AH9 | Remove that test folder; wait for the scan | its items show "Not installed" (dimmed, ⚠); opening asks "… is not installed" with Remove from fence / Cancel (no Locate…) |
+| AH10 | Put the test folder back | the items are back by themselves |
+| AH11 | Settings → New games go to: Nowhere; add another test game | no item added anywhere |
+| AH12 | Restore the "Before games became items" snapshot | the library fence comes back and becomes game items again (one more snapshot first) |
+| AH13 | Delete the new-games fence | Settings → New games go to: Nowhere |
diff --git a/docs/research/m22-games-as-items.md b/docs/research/m22-games-as-items.md
new file mode 100644
index 0000000..bf69f79
--- /dev/null
+++ b/docs/research/m22-games-as-items.md
@@ -0,0 +1,26 @@
+# M22 — One kind of fence: games become items (0.12.0): build notes and results
+
+Spec: `docs/superpowers/specs/2026-10-05-games-as-items-design.md` · Decision: ADR-045 · Plan:
+`docs/superpowers/plans/2026-10-05-m22-games-as-items.md`
+
+## Prototype (2026-10-05, worktree `neo_fences-m22proto`, branch `m22-proto`)
+
+Built end to end before the plan: Core test-first (9 new tests, 511 in all), App; 0 warnings. Probed live on a copy of
+the user's data (restored afterwards): the "Games" library fence became 12 game items with the same covers and order
+("Before games became items" snapshot saved first); a game Ctrl-dragged into "Apps" showed as a cover tile among the app
+icons, and Show as → Icon made it an icon cell; Add games… listed the 12 games with "already here" marking; the tray has
+no "New Game Library fence".
+
+### Where the build departs from the spec (the final review weighs them)
+
+- `ShowAs` is `ItemShow?` with `Cover` / `Icon` (null = the usual look) instead of a `Default` value: items.json carries
+  `showAs` only when set.
+- Migration also runs after a scan (not only at start and after a restore): a library fence whose index was empty at
+  start (first run, unreadable index) becomes game items as soon as a scan finds games.
+- The scan also runs while Add games… is open, so the list fills on a PC with no game items yet.
+- Add games… lists sources by their readable names (`GameCatalog.SourceName`).
+- Settings → Game Library: "Hidden games — None." (the old hint pointed at the library fence's menu).
+
+## Live check
+
+(TEST-CHECKLIST AH — filled in after the run.)
\ No newline at end of file
````

- [ ] **Step 2: Check.** The secret scan of `git diff main` (the personal email, the private hub link) finds nothing.

- [ ] **Step 3: Commit.** `git add -A && git commit -m "docs: described games as items in ADR-045, architecture, features, checklist AH and the M22 research note"`

### Task 4: Final review and fix pass

- [ ] Whole-branch review on the most capable model (superpowers:requesting-code-review, `code-reviewer.md`) of
  `main..m22-games-as-items`, with the spec, this plan, its Review Focus and departures. Critical / Important findings:
  one fix pass, each fix test-first where Core can show it; App-only fixes get an AH row. Minors are listed in
  `docs/research/m22-games-as-items.md` as deferred.
- [ ] Commit fixes (`fix: …`) with their notes.

### Task 5: Live check (the PC is unattended: standing go for test runs)

- [ ] Back up `%LOCALAPPDATA%\NeoFences` and the Run value; stop the installed copy; run the test build.
- [ ] TEST-CHECKLIST AH1–AH13 by script (boxed TEST RUNNING / TEST COMPLETE banners; never type into Windows Terminal;
  leave the Firefox Picture-in-Picture window alone). AH8–AH10 use a test game folder created and removed in
  `D:\GameLibrary\NeoFences-test-game` only. Screenshots sent to the user as they are taken.
- [ ] Restore the data and the Run value; restart the installed copy; refocus Terminal.
- [ ] Results into `docs/research/m22-games-as-items.md`; commit `docs: added the M22 live check results`.
