# M34 — Icons and game tiles (0.21.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sharp icons at any scaling, modern 2:3 game tiles (rounded, shadow, accent ring), a glow tile for games without art, Normal / Large cover sizes with even spacing, opt-in online covers from the Steam store and website icons from the sites themselves, and "Choose cover…".

**Architecture:** Core decides (test-first): `GameArt` (art order, strict names, search term, miss retry), `CoverSizes` (Normal 1×2 / Large 2×4, used by `FenceGrid.SpanOf`), `CoversIndex` (`covers\index.json`), `SiteIcons` (declared-icon parser, letter badge, retry), `WatchdogFiles` (stale files), `LibrarySettings.OnlineArt` / `CoverChoices` kept by the normalizer. Shell does the network and Win32: `OnlineArt` (Steam store search, asset service, image download, site icons), `ShellLinks.AppsFolderOf`, jumbo generic icons, `Watchdog.RemoveStaleFiles`. The App draws (tile template, glow backdrop, accent and shadow brushes, cover cell geometry, layout rounding and high-quality scaling) and runs the flow (`FenceHost.Covers`: ask once, lookup after each scan, site icons, Choose cover…, cover size menu; Settings switch; the letter badge in `IconLoader`).

**Tech Stack:** .NET 10, C#, WPF, CsWin32 (`SHGetImageList`, `IImageList` added), `System.Net.Http` (in the BCL — no new package), xUnit; PowerShell for the live check.

**Spec:** `docs/superpowers/specs/2026-10-06-icons-and-game-tiles-design.md` (approved 2026-10-06, amended during planning: cover sizes, art order, Settings → Game Library, Steam lookup details). Decision: ADR-055 — added by Task 3.

## How this plan is written

Built in a scratch worktree (`m34-proto`), probed on a copy of the owner's data (owner's OK: the question window, 5 covers
found in ~10 s, the Choose cover… picker with Steam's results, Settings), and replay-verified on a fresh worktree of
`main` at `bc09cac`: the tests patch alone fails to build (70 error lines — the RED), the Core patch makes 747 tests pass,
the App patch builds with 0 warnings and 747 pass, and the tree is identical to the prototype. Tasks 1–2 are **patches to
apply** (`git apply --whitespace=nowarn <file>`; if one does not apply, stop). Between Task 1 and Task 2 only
`dotnet test tests/NeoFences.Core.Tests` runs.

**Calls made while prototyping (ledger them as rulings at Task 2):** a launcher logo comes after an online cover (the
lookup runs for logo-only games); the glow tile's icon is about half the tile (a ponytail for "at most twice its real
size"); the soft shadow is two faint rounded layers, not a `DropShadowEffect` (effects render in software on layered
windows); the glow backdrop is the icon shrunk to 6 px and stretched (no blur effect, same reason); site icons are fetched
on demand from the icon loader's request (one at a time per host), not in a scan; Choose cover… shows Steam's public results
unfiltered; generic icons above 32 px come from the system image list (extra-large 48, jumbo 256).

## Global Constraints

- Hard rules stand: user files never touched (a chosen picture is copied into `covers\`); no new NuGet dependency
  (`System.Net.Http` is in the BCL); Win32 and network only in `NeoFences.Shell`; failures degrade the one feature.
- Online art is off until the user says yes (`LibrarySettings.OnlineArt`: null = not asked); asked once; never in safe
  mode or a read-only session; held while a game runs.
- Only game names (to the Steam store) and web links' addresses (to those sites) leave the PC; replies ≤ 5 MB, images
  only (pages ≤ 1 MB); time-outs 10 s; a miss waits 30 days or a rename; offline records nothing.
- Copy, exactly: "Find covers online?", "Find covers", "Not now"; Settings "Find covers and website icons online";
  menu "Choose cover…", Size ▸ "Normal", "Large (twice as big)"; window "Choose cover — <name>", "From a file…",
  "Reset to automatic", "Close".
- Commits: single line, Conventional Commits, past tense, **no Co-Authored-By trailer**; secret scan before each commit.
- Version stays `0.20.0` until the release step; the release is 0.21.0.

## Review Focus

1. **Offline, slow or refusing Steam** (no network, HTTP 429/500, a reply that is not JSON, a 10 s stall): nothing is
   recorded, the glow tiles stay, the UI never waits, the next scan tries again; a cover whose download fails is not a miss.
2. **Free fences whose covers change size** (the owner's 2×2 covers become Large 2×4 and overlap stored cells): nothing is
   lost or pushed off the grid; overlapped elements land on the nearest free cells; Normal covers fill their rows with no gap.
3. **Odd websites** (a huge page, a redirect to another host, an SVG-only icon, a non-image favicon, an .ico with many
   frames, a dead site): the letter badge shows, no crash; one fetch per host at a time; a miss waits 30 days.
4. **Modes:** safe mode and read-only sessions never ask or look up; a question held during a game shows when it ends;
   turning the switch off keeps found covers; "Not now" (or closing the window) asks again at the next scan.
5. **Sharpness and geometry under change:** 125 / 150 % and a monitor change (DPI) keep icons sharp; switching labels
   (Always ↔ On hover) or the icon size re-sizes cover cells consistently; a cover's name overlay shows only with labels on
   hover.

---

### Task 0: Worktree and baseline

- [ ] `git worktree add -b m34-tiles ..\neo_fences-m34 main` (main at `bc09cac` or later docs-only commits).
- [ ] `dotnet build` → 0 warnings; `dotnet test` → 715 passed.

### Task 1: Core — art order, strict names, cover sizes, covers index, site icons, stale watchdog files, settings

**Files:**
- Create: `tests/NeoFences.Core.Tests/Library/GameArtTests.cs`, `src/NeoFences.Core/Library/GameArt.cs` (`CoverArt`,
  `GameArt`, `CoverSizes`), `src/NeoFences.Core/Library/CoversIndex.cs` (`CoverRecord`, `SiteRecord`, `CoversIndex`),
  `src/NeoFences.Core/Library/SiteIcons.cs`, `src/NeoFences.Core/Lifecycle/WatchdogFiles.cs`
- Modify: `src/NeoFences.Core/Items/GridLayout.cs` (`SpanOf`: covers via `CoverSizes`), `src/NeoFences.Core/Model/LibrarySettings.cs`
  (`OnlineArt`, `CoverChoices`), `src/NeoFences.Core/Config/ConfigNormalizer.cs` (keeps both; choices as plain file names)

**Interfaces:**
- Produces: `CoverArt(string Path, bool IsPoster)`; `GameArt.Choose(chosen, poster, online, logo) → CoverArt?`,
  `GameArt.NeedsLookup(chosen, poster)`, `GameArt.SameName(ours, theirs)`, `GameArt.SearchTerm(name)`,
  `GameArt.ShouldLookUp(CoverRecord?, name, now)`, `GameArt.MissRetry`; `CoverSizes.Normal/Large/SpanOf(GridSpan?)`;
  `CoverRecord(Name, Checked, SteamAppId?, File?)`, `SiteRecord(Checked, File?)`, `CoversIndex.Empty/Load(path)/Save(path)/
  WithGame/WithSite/Games/Sites`; `SiteIcons.DeclaredIcon(html, page)`, `SiteIcons.FallbackIcon(page)`,
  `SiteIcons.Badge(url) → (char Letter, uint Color)`, `SiteIcons.BadgeColors`, `SiteIcons.ShouldFetch(SiteRecord?, now)`;
  `WatchdogFiles.Stale(names, isRunning)`; `LibrarySettings.OnlineArt (bool?)`, `LibrarySettings.CoverChoices`.

- [ ] **Step 1: Write the failing tests.** Write this patch to `m34-1a-tests.patch` and apply it:

```diff
diff --git a/tests/NeoFences.Core.Tests/Library/GameArtTests.cs b/tests/NeoFences.Core.Tests/Library/GameArtTests.cs
new file mode 100644
index 0000000..a80d5c2
--- /dev/null
+++ b/tests/NeoFences.Core.Tests/Library/GameArtTests.cs
@@ -0,0 +1,235 @@
+using NeoFences.Core.Items;
+using NeoFences.Core.Library;
+using NeoFences.Core.Lifecycle;
+using NeoFences.Core.Tests.TestSupport;
+
+namespace NeoFences.Core.Tests.Library;
+
+/// <summary>M34 (spec 2026-10-06-icons-and-game-tiles-design, ADR-055): where a game's art comes from, strict names, misses.</summary>
+public class GameArtTests : IDisposable
+{
+    private readonly TempDirectory _directory = new();
+    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.FromHours(5.5));
+
+    public void Dispose() => _directory.Dispose();
+
+    // ---------- the order ----------
+
+    [Fact]
+    public void Choose_TheUsersChoiceWins_ThenTheDiskCover_ThenOnline_ThenTheLogo()
+    {
+        Assert.Equal(new CoverArt(@"C:\c\choice.jpg", IsPoster: true), GameArt.Choose(chosen: @"C:\c\choice.jpg", poster: @"C:\s\p.jpg", online: @"C:\c\o.jpg", logo: @"C:\x\l.png"));
+        Assert.Equal(new CoverArt(@"C:\s\p.jpg", IsPoster: true), GameArt.Choose(chosen: null, poster: @"C:\s\p.jpg", online: @"C:\c\o.jpg", logo: @"C:\x\l.png"));
+        Assert.Equal(new CoverArt(@"C:\c\o.jpg", IsPoster: true), GameArt.Choose(chosen: null, poster: null, online: @"C:\c\o.jpg", logo: @"C:\x\l.png"));
+        Assert.Equal(new CoverArt(@"C:\x\l.png", IsPoster: false), GameArt.Choose(chosen: null, poster: null, online: null, logo: @"C:\x\l.png"));
+        Assert.Null(GameArt.Choose(chosen: null, poster: null, online: null, logo: null)); // the glow tile
+    }
+
+    [Fact]
+    public void Choose_ALogoThatIsNotAPicture_IsNoArt()
+    {
+        Assert.Null(GameArt.Choose(chosen: null, poster: null, online: null, logo: @"D:\Games\Blur\Blur.exe"));
+    }
+
+    [Fact]
+    public void NeedsLookup_OnlyWithoutAChoiceOrADiskCover_ALogoStillLooksUp()
+    {
+        Assert.True(GameArt.NeedsLookup(chosen: null, poster: null));
+        Assert.False(GameArt.NeedsLookup(chosen: "a.jpg", poster: null));
+        Assert.False(GameArt.NeedsLookup(chosen: null, poster: "p.jpg"));
+    }
+
+    // ---------- strict names (the owner's games) ----------
+
+    [Theory]
+    [InlineData("Clair Obscur - Expedition 33", "Clair Obscur: Expedition 33")]
+    [InlineData("Detroit - Become Human", "Detroit: Become Human")]
+    [InlineData("Mafia II Definitive Edition", "Mafia II: Definitive Edition")]
+    [InlineData("Plague Inc - Evolved", "Plague Inc: Evolved")]
+    [InlineData("Wallpaper Engine", "Wallpaper Engine")]
+    [InlineData("Forza Horizon 6", "FORZA HORIZON 6™")]
+    [InlineData("Tom Clancy's The Division®", "Tom Clancys The Division")]
+    public void SameName_IgnoresCasePunctuationSpacingAndMarks(string ours, string steams) => Assert.True(GameArt.SameName(ours, steams));
+
+    [Theory]
+    [InlineData("Blur", "Ricochet Blur")]
+    [InlineData("AC Black Flag Resynced", "Assassin's Creed IV Black Flag")]
+    [InlineData("Mafia - Definitive Edition", "Mafia II: Definitive Edition")]
+    [InlineData("!!!", "???")] // nothing left to compare is never a match
+    public void SameName_IsStrict(string ours, string steams) => Assert.False(GameArt.SameName(ours, steams));
+
+    [Theory]
+    [InlineData("Clair Obscur - Expedition 33", "Clair Obscur Expedition 33")] // Steam's search finds nothing for " - "
+    [InlineData("Plague Inc - Evolved", "Plague Inc Evolved")]
+    [InlineData("  Forza   Horizon 6 ", "Forza Horizon 6")]
+    [InlineData("Tom Clancy's The Division®", "Tom Clancy s The Division")]
+    public void SearchTerm_TurnsPunctuationIntoSpaces(string name, string term) => Assert.Equal(term, GameArt.SearchTerm(name));
+
+    // ---------- misses ----------
+
+    [Fact]
+    public void ShouldLookUp_NeverAsked_Yes_Found_No()
+    {
+        Assert.True(GameArt.ShouldLookUp(record: null, name: "Blur", now: Now));
+        Assert.False(GameArt.ShouldLookUp(new CoverRecord("Blur", Now.AddDays(-400), SteamAppId: 1, File: "steam-1.jpg"), "Blur", Now));
+    }
+
+    [Fact]
+    public void ShouldLookUp_AMiss_WaitsThirtyDays_OrARename()
+    {
+        var miss = new CoverRecord("Blur", Now.AddDays(-29));
+        Assert.False(GameArt.ShouldLookUp(miss, "Blur", Now));
+        Assert.True(GameArt.ShouldLookUp(miss with { Checked = Now.AddDays(-31) }, "Blur", Now));
+        Assert.True(GameArt.ShouldLookUp(miss, "Blur (2010)", Now)); // renamed: asked again at once
+    }
+
+    // ---------- the index ----------
+
+    [Fact]
+    public void CoversIndex_RoundTrips_AndADamagedFileIsEmpty()
+    {
+        var path = Path.Combine(_directory.Path, "covers", "index.json");
+        Assert.Empty(CoversIndex.Load(path).Games); // no file yet
+        var index = CoversIndex.Empty
+            .WithGame("steam:1903340", new CoverRecord("Clair Obscur - Expedition 33", Now, 1903340, "steam-1903340.jpg"))
+            .WithGame("folder:d:\\gamelibrary\\blur", new CoverRecord("Blur", Now))
+            .WithSite("www.youtube.com", new SiteRecord(Now, "www.youtube.com.png"));
+        index.Save(path);
+
+        var read = CoversIndex.Load(path);
+        Assert.Equal("steam-1903340.jpg", read.Games["steam:1903340"].File);
+        Assert.Null(read.Games["folder:d:\\gamelibrary\\blur"].File);
+        Assert.Equal(Now, read.Games["folder:d:\\gamelibrary\\blur"].Checked);
+        Assert.Equal("www.youtube.com.png", read.Sites["www.youtube.com"].File);
+
+        File.WriteAllText(path, "{ not json");
+        Assert.Empty(CoversIndex.Load(path).Games);
+    }
+
+    [Fact]
+    public void CoversIndex_GameIdsIgnoreCase()
+    {
+        var index = CoversIndex.Empty.WithGame("Steam:1", new CoverRecord("A", Now, 1, "steam-1.jpg"));
+        Assert.True(index.Games.ContainsKey("steam:1"));
+    }
+
+    // ---------- website icons ----------
+
+    [Fact]
+    public void DeclaredIcon_TakesTheLargest_ResolvedAgainstThePage()
+    {
+        const string html = """
+            <html><head>
+            <link rel="icon" href="/favicon-16.png" sizes="16x16">
+            <link rel="icon" type="image/png" sizes="192x192" href="/icons/big.png">
+            <link rel="apple-touch-icon" href="https://cdn.example.com/touch.png">
+            <link rel="stylesheet" href="/site.css">
+            </head></html>
+            """;
+        Assert.Equal(new Uri("https://example.com/icons/big.png"), SiteIcons.DeclaredIcon(html, new Uri("https://example.com/watch?v=1")));
+    }
+
+    [Fact]
+    public void DeclaredIcon_AppleTouchIconWithoutSizes_Beats_APlainIcon_AndSvgIsSkipped()
+    {
+        const string html = """
+            <link rel="shortcut icon" href="favicon.ico">
+            <link rel="icon" href="/logo.svg" type="image/svg+xml" sizes="any">
+            <link href="apple.png" rel="apple-touch-icon">
+            """;
+        Assert.Equal(new Uri("https://example.com/a/apple.png"), SiteIcons.DeclaredIcon(html, new Uri("https://example.com/a/page")));
+    }
+
+    [Fact]
+    public void DeclaredIcon_None_IsNull_AndTheFallbackIsFaviconIco()
+    {
+        Assert.Null(SiteIcons.DeclaredIcon("<html><head><title>x</title></head></html>", new Uri("https://example.com/")));
+        Assert.Equal(new Uri("https://example.com/favicon.ico"), SiteIcons.FallbackIcon(new Uri("https://example.com/deep/page?q=1")));
+    }
+
+    [Fact]
+    public void Badge_IsTheSiteNamesFirstLetter_WithAStableColour()
+    {
+        var youtube = SiteIcons.Badge("https://www.youtube.com/watch?v=1");
+        Assert.Equal('Y', youtube.Letter);
+        Assert.Equal(youtube, SiteIcons.Badge("https://youtube.com/"));
+        Assert.Equal('G', SiteIcons.Badge("http://github.com").Letter);
+        Assert.Contains(youtube.Color, SiteIcons.BadgeColors);
+        Assert.Equal('?', SiteIcons.Badge("not a url").Letter);
+    }
+
+    [Fact]
+    public void ShouldFetch_Site_LikeGames_ThirtyDaysForAMiss()
+    {
+        Assert.True(SiteIcons.ShouldFetch(record: null, now: Now));
+        Assert.False(SiteIcons.ShouldFetch(new SiteRecord(Now.AddDays(-100), "x.png"), Now));
+        Assert.False(SiteIcons.ShouldFetch(new SiteRecord(Now.AddDays(-2)), Now));
+        Assert.True(SiteIcons.ShouldFetch(new SiteRecord(Now.AddDays(-31)), Now));
+    }
+
+    // ---------- cover sizes ----------
+
+    [Fact]
+    public void CoverSizes_NormalAndLarge_OldSizesMapToTheNearest()
+    {
+        Assert.Equal(new GridSpan(1, 2), CoverSizes.SpanOf(null));
+        Assert.Equal(new GridSpan(1, 2), CoverSizes.SpanOf(new GridSpan(1, 1)));
+        Assert.Equal(new GridSpan(2, 4), CoverSizes.SpanOf(new GridSpan(2, 2))); // the owner's two big games: twice now
+        Assert.Equal(new GridSpan(2, 4), CoverSizes.SpanOf(new GridSpan(4, 4)));
+        Assert.Equal(CoverSizes.Large, CoverSizes.SpanOf(CoverSizes.Large));
+    }
+
+    [Fact]
+    public void SpanOf_ACover_UsesCoverSizes_AnIconGameKeepsItsGridSize()
+    {
+        var cover = VirtualItem.Create(@"C:\lib\Detroit.url") with { GameId = "steam:1222140", Size = new GridSpan(2, 2) };
+        Assert.Equal(new GridSpan(2, 4), FenceGrid.SpanOf(cover));
+        Assert.Equal(new GridSpan(2, 2), FenceGrid.SpanOf(cover with { ShowAs = ItemShow.Icon }));
+        Assert.Equal(new GridSpan(1, 2), FenceGrid.SpanOf(cover with { Size = null }));
+    }
+
+    // ---------- settings ----------
+
+    [Fact]
+    public void Settings_OnlineArtIsNotAskedYet_AndChoicesRoundTrip()
+    {
+        var fresh = NeoFences.Core.Model.NeoFencesConfig.CreateDefault();
+        Assert.Null(fresh.Library.OnlineArt);
+        Assert.Empty(fresh.Library.CoverChoices);
+        var chosen = fresh with { Library = fresh.Library with { OnlineArt = true, CoverChoices = new Dictionary<string, string> { ["steam:1"] = "choice-steam-1.jpg" } } };
+        var read = NeoFences.Core.Config.ConfigJson.Deserialize(NeoFences.Core.Config.ConfigJson.Serialize(chosen));
+        Assert.True(read.Library.OnlineArt);
+        Assert.Equal("choice-steam-1.jpg", read.Library.CoverChoices["steam:1"]);
+        Assert.DoesNotContain("onlineArt", NeoFences.Core.Config.ConfigJson.Serialize(fresh)); // not asked: nothing written
+    }
+
+    [Fact]
+    public void Normalize_KeepsOnlineArtAndChoices_AsPlainFileNames()
+    {
+        var config = NeoFences.Core.Model.NeoFencesConfig.CreateDefault();
+        config = config with { Library = config.Library with { OnlineArt = false, CoverChoices = new Dictionary<string, string>
+        {
+            ["steam:1"] = "choice-steam-1.jpg",
+            ["steam:2"] = @"..\..\Windows\evil.jpg", // hand-edited: only a file name in the covers folder counts
+            ["steam:3"] = " ",
+            [" "] = "x.jpg",
+        } } };
+        var normal = NeoFences.Core.Config.ConfigNormalizer.Normalize(config);
+        Assert.False(normal.Library.OnlineArt);
+        Assert.Equal("choice-steam-1.jpg", normal.Library.CoverChoices["steam:1"]);
+        Assert.Equal("evil.jpg", normal.Library.CoverChoices["steam:2"]);
+        Assert.Equal(2, normal.Library.CoverChoices.Count);
+        Assert.True(normal.Library.CoverChoices.ContainsKey("STEAM:1")); // ids ignore case
+        Assert.Empty(NeoFences.Core.Config.ConfigNormalizer.Normalize(config with { Library = config.Library with { CoverChoices = null! } }).Library.CoverChoices);
+    }
+
+    // ---------- leftover watchdog files ----------
+
+    [Fact]
+    public void StaleWatchdogFiles_OnlyThoseOfEndedProcesses()
+    {
+        string[] names = ["watchdog-6992", "watchdog-25208", "watchdog-restarts.txt", "watchdog-.log", "config.json", "watchdog-12x"];
+        var stale = WatchdogFiles.Stale(names, isRunning: processId => processId == 25208);
+        Assert.Equal(["watchdog-6992"], stale);
+    }
+}
```

- [ ] **Step 2: Run them.** `dotnet test tests/NeoFences.Core.Tests` → Expected: build fails, 70 `error CS` lines (`GameArt`, `SiteIcons`, `CoverSizes`, `CoversIndex`, `CoverRecord`, `SiteRecord`, `CoverArt`, `WatchdogFiles`, `OnlineArt`, `CoverChoices` do not exist).

- [ ] **Step 3: Implement.** Write this patch to `m34-1b-core.patch` and apply it:

```diff
diff --git a/src/NeoFences.Core/Config/ConfigNormalizer.cs b/src/NeoFences.Core/Config/ConfigNormalizer.cs
index 4bd6fc9..007f9d9 100644
--- a/src/NeoFences.Core/Config/ConfigNormalizer.cs
+++ b/src/NeoFences.Core/Config/ConfigNormalizer.cs
@@ -85,6 +85,12 @@ public static class ConfigNormalizer
         Folders = (library?.Folders ?? []).Where(folder => !string.IsNullOrWhiteSpace(folder)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
         Sources = library?.Sources ?? new LibrarySources(),
         Hidden = (library?.Hidden ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
+        OnlineArt = library?.OnlineArt, // M34
+        // M34: only a file name in NeoFences' covers folder counts (a hand-edited path never points elsewhere).
+        CoverChoices = (library?.CoverChoices ?? new Dictionary<string, string>())
+            .Where(choice => !string.IsNullOrWhiteSpace(choice.Key) && !string.IsNullOrWhiteSpace(Path.GetFileName(choice.Value ?? "")))
+            .GroupBy(choice => choice.Key, StringComparer.OrdinalIgnoreCase)
+            .ToDictionary(group => group.Key, group => Path.GetFileName(group.First().Value), StringComparer.OrdinalIgnoreCase),
     };
 
     private static Dictionary<string, Layout> NormalizeLayouts(IReadOnlyDictionary<string, Layout>? loadedLayouts)
diff --git a/src/NeoFences.Core/Items/GridLayout.cs b/src/NeoFences.Core/Items/GridLayout.cs
index f388d47..7afed37 100644
--- a/src/NeoFences.Core/Items/GridLayout.cs
+++ b/src/NeoFences.Core/Items/GridLayout.cs
@@ -36,11 +36,12 @@ public static class FenceGrid
     /// <summary>The farthest row or column a stored cell may name (a hand-edited 10,000,000 would grow the map every layout).</summary>
     public const int MaxCell = 1000;
 
-    /// <summary>An item's span: its own size, else a widget's or a panel's (M25, M26), else 1×2 for a game shown as a cover (a 2:3 poster fits), else 1×1.</summary>
+    /// <summary>An item's span: a game shown as a cover is Normal (1×2) or Large (2×4, M34); else its own size, else a widget's or a panel's (M25, M26), else 1×1.</summary>
     public static GridSpan SpanOf(VirtualItem item) =>
-        item.Size ?? (Widgets.Of(item.Target) is { } widget ? Widgets.DefaultSpan(widget)
+        GameItems.ShowsCover(item) ? CoverSizes.SpanOf(item.Size) // M34: covers are Normal (1×2) or Large (2×4)
+        : item.Size ?? (Widgets.Of(item.Target) is { } widget ? Widgets.DefaultSpan(widget)
             : FolderPanels.IsPanel(item) ? FolderPanels.DefaultSpan
-            : GameItems.ShowsCover(item) ? new GridSpan(1, 2) : GridSpan.One);
+            : GridSpan.One);
 
     public static GridArrangement Arrange(IReadOnlyList<GridElement> elements, int columns, FenceLayout layout)
     {
diff --git a/src/NeoFences.Core/Library/CoversIndex.cs b/src/NeoFences.Core/Library/CoversIndex.cs
new file mode 100644
index 0000000..8667a8f
--- /dev/null
+++ b/src/NeoFences.Core/Library/CoversIndex.cs
@@ -0,0 +1,54 @@
+using System.Text.Json;
+using NeoFences.Core.Config;
+
+namespace NeoFences.Core.Library;
+
+/// <summary>A game's online lookup (M34): found (<see cref="File"/> in the covers folder) or a miss (null), and when.</summary>
+/// <param name="Name">The game's name when it was looked up: a renamed game is looked up again.</param>
+public sealed record CoverRecord(string Name, DateTimeOffset Checked, int? SteamAppId = null, string? File = null);
+
+/// <summary>A website's icon (M34): found (<see cref="File"/> in the covers folder's <c>sites</c>) or a miss (null), and when.</summary>
+public sealed record SiteRecord(DateTimeOffset Checked, string? File = null);
+
+/// <summary>
+/// NeoFences' record of online art (M34, ADR-055): <c>covers\index.json</c> next to the files it names. Only NeoFences'
+/// own data; a damaged or missing file is an empty index (the art is looked up again).
+/// </summary>
+public sealed record CoversIndex(IReadOnlyDictionary<string, CoverRecord> Games, IReadOnlyDictionary<string, SiteRecord> Sites)
+{
+    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
+
+    public static CoversIndex Empty { get; } = new(new Dictionary<string, CoverRecord>(StringComparer.OrdinalIgnoreCase),
+        new Dictionary<string, SiteRecord>(StringComparer.OrdinalIgnoreCase));
+
+    public CoversIndex WithGame(string gameId, CoverRecord record) =>
+        this with { Games = new Dictionary<string, CoverRecord>(Games, StringComparer.OrdinalIgnoreCase) { [gameId] = record } };
+
+    public CoversIndex WithSite(string host, SiteRecord record) =>
+        this with { Sites = new Dictionary<string, SiteRecord>(Sites, StringComparer.OrdinalIgnoreCase) { [host] = record } };
+
+    public static CoversIndex Load(string path)
+    {
+        try
+        {
+            if (!File.Exists(path)) return Empty;
+            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path), Options);
+            if (stored is null) return Empty;
+            return new CoversIndex(new Dictionary<string, CoverRecord>(stored.Games ?? [], StringComparer.OrdinalIgnoreCase),
+                new Dictionary<string, SiteRecord>(stored.Sites ?? [], StringComparer.OrdinalIgnoreCase));
+        }
+        catch (Exception failure) when (failure is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
+        {
+            return Empty; // looked up again; the image files stay
+        }
+    }
+
+    /// <exception cref="IOException">The file could not be written (the caller logs it; the art shows anyway).</exception>
+    public void Save(string path)
+    {
+        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
+        SafeFile.Write(path, JsonSerializer.Serialize(new Stored(new(Games), new(Sites)), Options), backupPath: null);
+    }
+
+    private sealed record Stored(Dictionary<string, CoverRecord>? Games, Dictionary<string, SiteRecord>? Sites);
+}
diff --git a/src/NeoFences.Core/Library/GameArt.cs b/src/NeoFences.Core/Library/GameArt.cs
new file mode 100644
index 0000000..55df77d
--- /dev/null
+++ b/src/NeoFences.Core/Library/GameArt.cs
@@ -0,0 +1,78 @@
+using System.Text;
+using NeoFences.Core.Items;
+
+namespace NeoFences.Core.Library;
+
+/// <summary>A tile's art (M12, M34): a 2:3 poster fills the tile; a logo is centred like an icon.</summary>
+public sealed record CoverArt(string Path, bool IsPoster);
+
+/// <summary>
+/// Where a game's art comes from (M34, spec 2026-10-06-icons-and-game-tiles-design §1–2, ADR-055): the user's choice, the
+/// launcher's cover on disk, a cover found online, the launcher's logo, else none (the glow tile). Strict names for the
+/// online lookup and when to look up again. Pure.
+/// </summary>
+public static class GameArt
+{
+    /// <summary>A miss is asked again after this long (or at once when the game's name changes).</summary>
+    public static readonly TimeSpan MissRetry = TimeSpan.FromDays(30);
+
+    private static readonly string[] PictureExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif"];
+
+    /// <param name="chosen">"Choose cover…" (a file in NeoFences' covers folder).</param>
+    /// <param name="poster">The launcher's 2:3 cover on disk (Steam).</param>
+    /// <param name="online">A cover found online (the covers index).</param>
+    /// <param name="logo">The launcher's logo (an Xbox package's); a program's path is no picture.</param>
+    public static CoverArt? Choose(string? chosen, string? poster, string? online, string? logo)
+    {
+        // ponytail: a logo comes after an online cover (ruling at planning: a real cover beats a centred square logo).
+        if (chosen is not null) return new CoverArt(chosen, IsPoster: true);
+        if (poster is not null) return new CoverArt(poster, IsPoster: true);
+        if (online is not null) return new CoverArt(online, IsPoster: true);
+        return logo is not null && PictureExtensions.Contains(Path.GetExtension(logo).ToLowerInvariant()) ? new CoverArt(logo, IsPoster: false) : null;
+    }
+
+    /// <summary>The online lookup runs for games without a chosen cover or a cover on disk (a logo still looks up).</summary>
+    public static bool NeedsLookup(string? chosen, string? poster) => chosen is null && poster is null;
+
+    /// <summary>Strict: equal after ignoring case, punctuation, spacing and ™ ® ©; an empty name never matches.</summary>
+    public static bool SameName(string ours, string theirs)
+    {
+        var normal = Normalize(ours);
+        return normal.Length > 0 && normal == Normalize(theirs);
+    }
+
+    /// <summary>The name for a store search: punctuation as spaces (Steam finds nothing for "Clair Obscur - Expedition 33").</summary>
+    public static string SearchTerm(string name)
+    {
+        var term = new StringBuilder(name.Length);
+        foreach (var character in name) term.Append(char.IsLetterOrDigit(character) ? character : ' ');
+        return string.Join(' ', term.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
+    }
+
+    /// <summary>Never asked: yes. Found: no. A miss: after <see cref="MissRetry"/>, or at once when the name changed.</summary>
+    public static bool ShouldLookUp(CoverRecord? record, string name, DateTimeOffset now) =>
+        record is null
+        || record.File is null && (!string.Equals(record.Name, name, StringComparison.Ordinal) || now - record.Checked > MissRetry);
+
+    private static string Normalize(string name)
+    {
+        var normal = new StringBuilder(name.Length);
+        foreach (var character in name.ToLowerInvariant())
+        {
+            if (char.IsLetterOrDigit(character)) normal.Append(character);
+        }
+        return normal.ToString();
+    }
+}
+
+/// <summary>
+/// Game cover sizes (M34, owner's choice at planning): Normal (1×2, the default) and Large (2×4, twice). A cover is
+/// 2:3 and fills its cells; an older size maps to the nearest: one column Normal, two or more Large.
+/// </summary>
+public static class CoverSizes
+{
+    public static GridSpan Normal { get; } = new(1, 2);
+    public static GridSpan Large { get; } = new(2, 4);
+
+    public static GridSpan SpanOf(GridSpan? size) => size is { Columns: >= 2 } ? Large : Normal;
+}
diff --git a/src/NeoFences.Core/Library/SiteIcons.cs b/src/NeoFences.Core/Library/SiteIcons.cs
new file mode 100644
index 0000000..818feda
--- /dev/null
+++ b/src/NeoFences.Core/Library/SiteIcons.cs
@@ -0,0 +1,80 @@
+using System.Text.RegularExpressions;
+
+namespace NeoFences.Core.Library;
+
+/// <summary>
+/// Website icons (M34, ADR-055): the page's own declared icon (the largest), else <c>/favicon.ico</c>; without one, a
+/// coloured letter badge instead of the browser's icon. Pure: the fetching is NeoFences.Shell's.
+/// </summary>
+public static partial class SiteIcons
+{
+    /// <summary>The badge colours (ARGB): Windows 11-like, readable with white text.</summary>
+    public static IReadOnlyList<uint> BadgeColors { get; } = [0xFFC4302B, 0xFF0F6CBD, 0xFF107C10, 0xFF8764B8, 0xFFCA5010, 0xFF038387, 0xFFB146C2, 0xFF5C6970];
+
+    /// <summary>The largest <c>&lt;link rel="icon" | "apple-touch-icon"&gt;</c> of a page, resolved against it; SVG skipped (WPF cannot draw it).</summary>
+    public static Uri? DeclaredIcon(string html, Uri page)
+    {
+        Uri? best = null;
+        var bestSize = -1;
+        foreach (Match tag in LinkTag().Matches(html))
+        {
+            var attributes = Attributes(tag.Value);
+            if (!attributes.TryGetValue("rel", out var rel) || !attributes.TryGetValue("href", out var href) || href.Length == 0) continue;
+            var rels = rel.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
+            var touch = rels.Contains("apple-touch-icon") || rels.Contains("apple-touch-icon-precomposed");
+            if (!touch && !rels.Contains("icon")) continue;
+            if (attributes.TryGetValue("type", out var type) && type.Contains("svg", StringComparison.OrdinalIgnoreCase)) continue;
+            if (!Uri.TryCreate(page, href, out var url) || url.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;
+            // An apple-touch icon without sizes is 180 px by convention; a plain icon without sizes is a small favicon.
+            var size = attributes.TryGetValue("sizes", out var sizes) && SizeOf(sizes) is { } declared ? declared : touch ? 180 : 16;
+            if (size <= bestSize) continue;
+            (best, bestSize) = (url, size);
+        }
+        return best;
+    }
+
+    public static Uri FallbackIcon(Uri page) => new(page, "/favicon.ico");
+
+    /// <summary>The first letter of the site's name (the host without "www.") and a colour that stays the same for it.</summary>
+    public static (char Letter, uint Color) Badge(string url)
+    {
+        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Host.Length == 0) return ('?', BadgeColors[^1]);
+        var host = parsed.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? parsed.Host[4..] : parsed.Host;
+        var letter = host.FirstOrDefault(char.IsLetterOrDigit);
+        // FNV-1a: string.GetHashCode differs per run.
+        var hash = 2166136261u;
+        foreach (var character in host.ToLowerInvariant()) hash = (hash ^ character) * 16777619u;
+        return (letter == default ? '?' : char.ToUpperInvariant(letter), BadgeColors[(int)(hash % (uint)BadgeColors.Count)]);
+    }
+
+    /// <summary>Never fetched: yes. Found: no. A miss: after 30 days.</summary>
+    public static bool ShouldFetch(SiteRecord? record, DateTimeOffset now) =>
+        record is null || record.File is null && now - record.Checked > GameArt.MissRetry;
+
+    private static int? SizeOf(string sizes)
+    {
+        var largest = (int?)null;
+        foreach (var size in sizes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
+        {
+            var parts = size.ToLowerInvariant().Split('x');
+            if (parts.Length == 2 && int.TryParse(parts[0], out var width) && (largest is null || width > largest)) largest = width;
+        }
+        return largest;
+    }
+
+    private static Dictionary<string, string> Attributes(string tag)
+    {
+        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
+        foreach (Match attribute in Attribute().Matches(tag))
+        {
+            attributes.TryAdd(attribute.Groups["name"].Value, System.Net.WebUtility.HtmlDecode(attribute.Groups["value"].Value.Trim()));
+        }
+        return attributes;
+    }
+
+    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)]
+    private static partial Regex LinkTag();
+
+    [GeneratedRegex("""(?<name>[a-zA-Z-]+)\s*=\s*(?:"(?<value>[^"]*)"|'(?<value>[^']*)'|(?<value>[^\s>]+))""")]
+    private static partial Regex Attribute();
+}
diff --git a/src/NeoFences.Core/Lifecycle/WatchdogFiles.cs b/src/NeoFences.Core/Lifecycle/WatchdogFiles.cs
new file mode 100644
index 0000000..d950786
--- /dev/null
+++ b/src/NeoFences.Core/Lifecycle/WatchdogFiles.cs
@@ -0,0 +1,15 @@
+namespace NeoFences.Core.Lifecycle;
+
+/// <summary>
+/// The watchdog's per-process files (M33 <c>watchdog-&lt;pid&gt;</c>) left by a power cut: NeoFences removes those of
+/// processes that no longer run, at start (M34). Pure: the caller lists the data folder and asks Windows.
+/// </summary>
+public static class WatchdogFiles
+{
+    private const string Prefix = "watchdog-";
+
+    public static IReadOnlyList<string> Stale(IEnumerable<string> fileNames, Func<int, bool> isRunning) =>
+        [.. fileNames.Where(name => name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
+                                    && int.TryParse(name.AsSpan(Prefix.Length), System.Globalization.NumberStyles.None, null, out var processId)
+                                    && !isRunning(processId))];
+}
diff --git a/src/NeoFences.Core/Model/LibrarySettings.cs b/src/NeoFences.Core/Model/LibrarySettings.cs
index 0d1bd2f..97c2a9b 100644
--- a/src/NeoFences.Core/Model/LibrarySettings.cs
+++ b/src/NeoFences.Core/Model/LibrarySettings.cs
@@ -13,6 +13,12 @@ public sealed record LibrarySettings
 
     /// <summary>The fence newly installed games go to (M22), or null: nowhere.</summary>
     public string? NewGamesFence { get; init; }
+
+    /// <summary>Find covers and website icons online (M34, ADR-055): null until asked once, then the answer (Settings → Games too).</summary>
+    public bool? OnlineArt { get; init; }
+
+    /// <summary>"Choose cover…" (M34): game id → a picture in NeoFences' covers folder. Reset to automatic removes the entry.</summary>
+    public IReadOnlyDictionary<string, string> CoverChoices { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
 }
 
 /// <summary>Which sources the library reads; all on by default.</summary>
```

- [ ] **Step 4: Run.** `dotnet test tests/NeoFences.Core.Tests` → Expected: `Passed: 747`.

- [ ] **Step 5: Commit.** `git add -A && git commit -m "feat: added the game art order, strict names, cover sizes, the covers index, site icons and stale watchdog files in Core"`

### Task 2: Shell + App — the look, online art, Choose cover…

**Files:**
- Create: `src/NeoFences.Shell/OnlineArt.cs`, `src/NeoFences.App/FenceHost.Covers.cs`, `src/NeoFences.App/OnlineArtWindow.xaml(.cs)`,
  `src/NeoFences.App/ChooseCoverWindow.xaml(.cs)`
- Modify: `src/NeoFences.Shell/NativeMethods.txt` (`SHGetImageList`, `Windows.Win32.UI.Controls.IImageList`),
  `ShellItems.cs` (generic icons at their real size), `ShellLinks.cs` (`AppsFolderOf`), `Watchdog.cs` (`RemoveStaleFiles`);
  `src/NeoFences.App/FenceWindow.xaml(.cs)` (tile template, accent and shadow brushes, name overlay, cover cell geometry,
  layout rounding, high-quality scaling, no pop-under name on tiles), `FenceItemView.cs` (glow backdrop, tile icon size),
  `IconLoader.cs` (site icons, letter badge, Store-app icon, generic size), `FolderPanelView.xaml` (accent selection),
  `FenceHost.cs` (stale files, covers load, held question), `FenceHost.Library.cs` (art via `GameArt`, ask + lookup after a scan,
  Settings), `FenceHost.GameItems.cs` / `FenceHost.Items.cs` (Choose cover…, cover sizes), `AppPaths.cs` (`CoversDirectory`,
  `SiteIconsDirectory`), `SettingsWindow.Library.cs` / `SettingsWindow.xaml` (the switch; two texts)

**Interfaces:**
- Consumes: Task 1's types.
- Produces: `OnlineArt.SearchAsync/CoverUrlsAsync/GetImageAsync/DownloadImageAsync/FetchSiteIconAsync`, `OnlineArt.StoreGame`;
  `ShellLinks.AppsFolderOf(path)`; `ShellItems.TryGetGenericImage(target, isFolder, sizePx = 32)`; `Watchdog.RemoveStaleFiles()`;
  `IconLoader.SiteIconFiles`, `IconLoader.SiteIconWanted`; `FenceItemView.Glow`; `LibraryView(..., bool OnlineArt = false)`;
  `SettingsWindow.OnlineArtChanged`; `AppPaths.CoversDirectory`, `AppPaths.SiteIconsDirectory`.

- [ ] **Step 1: Implement.** Write this patch to `m34-2-app.patch` and apply it:

```diff
diff --git a/src/NeoFences.App/AppPaths.cs b/src/NeoFences.App/AppPaths.cs
index 5e9e5b5..06bdb09 100644
--- a/src/NeoFences.App/AppPaths.cs
+++ b/src/NeoFences.App/AppPaths.cs
@@ -15,4 +15,9 @@ public static class AppPaths
 
     /// <summary>Pictures chosen as item icons (M18), copied in so they survive the original being moved: NeoFences' own files.</summary>
     public static string IconsDirectory { get; } = Path.Combine(DataDirectory, "icons");
+
+    /// <summary>Covers and website icons (M34, ADR-055): chosen ones, found ones and <c>index.json</c>; NeoFences' own files.</summary>
+    public static string CoversDirectory { get; } = Path.Combine(DataDirectory, "covers");
+
+    public static string SiteIconsDirectory { get; } = Path.Combine(CoversDirectory, "sites");
 }
diff --git a/src/NeoFences.App/ChooseCoverWindow.xaml b/src/NeoFences.App/ChooseCoverWindow.xaml
new file mode 100644
index 0000000..f2423aa
--- /dev/null
+++ b/src/NeoFences.App/ChooseCoverWindow.xaml
@@ -0,0 +1,20 @@
+<Window x:Class="NeoFences.App.ChooseCoverWindow"
+        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
+        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
+        Title="Choose cover" Width="560" SizeToContent="Height" MaxHeight="720" ResizeMode="NoResize"
+        WindowStartupLocation="CenterScreen" ShowInTaskbar="True" ThemeMode="System">
+    <!-- M34 (spec §3): Steam's results for the name (online art on), a picture of your own, or back to automatic. -->
+    <DockPanel Margin="24,18,24,20">
+        <TextBlock x:Name="HeadingText" DockPanel.Dock="Top" FontSize="16" FontWeight="SemiBold" TextWrapping="Wrap" Margin="0,0,0,8" />
+        <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,16,0,0">
+            <Button x:Name="FromFileButton" Content="From a file…" MinWidth="100" Margin="0,0,8,0" />
+            <Button x:Name="ResetButton" Content="Reset to automatic" MinWidth="100" Margin="0,0,8,0" />
+            <Button x:Name="CloseButton" Content="Close" IsCancel="True" MinWidth="80" />
+        </StackPanel>
+        <TextBlock x:Name="StatusText" DockPanel.Dock="Top" TextWrapping="Wrap" FontSize="12" Foreground="{DynamicResource TextFillColorSecondaryBrush}" Margin="0,0,0,10"
+                   AutomationProperties.LiveSetting="Polite" />
+        <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
+            <WrapPanel x:Name="Results" />
+        </ScrollViewer>
+    </DockPanel>
+</Window>
diff --git a/src/NeoFences.App/ChooseCoverWindow.xaml.cs b/src/NeoFences.App/ChooseCoverWindow.xaml.cs
new file mode 100644
index 0000000..d3ee86c
--- /dev/null
+++ b/src/NeoFences.App/ChooseCoverWindow.xaml.cs
@@ -0,0 +1,95 @@
+using System.IO;
+using System.Windows;
+using System.Windows.Controls;
+using System.Windows.Media;
+using System.Windows.Media.Imaging;
+using NeoFences.Shell;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// "Choose cover…" on a game tile (M34, spec §3): up to 8 Steam store results for the game's name as thumbnails (when
+/// online art is on), a picture of the user's own, or back to automatic. The host stores the choice and copies the picture
+/// into NeoFences' covers folder; the original file is never moved or changed.
+/// </summary>
+public partial class ChooseCoverWindow : Window
+{
+    private const int MaxResults = 8;
+
+    /// <summary>A Steam result was picked: its cover's address.</summary>
+    public event Action<Uri>? StoreCoverChosen;
+
+    /// <summary>A picture of the user's own was picked (its path; the host copies it).</summary>
+    public event Action<string>? FileChosen;
+
+    public event Action? ResetRequested;
+
+    public ChooseCoverWindow(string gameName, bool online, bool hasChoice)
+    {
+        InitializeComponent();
+        Title = $"Choose cover — {gameName}";
+        HeadingText.Text = $"Choose a cover for {gameName}";
+        ResetButton.IsEnabled = hasChoice;
+        CloseButton.Click += (_, _) => Close(); // modeless: IsCancel alone would not close it
+        ResetButton.Click += (_, _) => { ResetRequested?.Invoke(); Close(); };
+        FromFileButton.Click += (_, _) =>
+        {
+            var picker = new Microsoft.Win32.OpenFileDialog { Title = "Choose a cover picture", Filter = "Pictures (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
+            if (picker.ShowDialog(this) != true) return;
+            FileChosen?.Invoke(picker.FileName);
+            Close();
+        };
+        if (online) Loaded += async (_, _) => await ShowResultsAsync(gameName);
+        else StatusText.Text = "Turn on \"Find covers and website icons online\" in Settings → Game Library to see covers from the Steam store.";
+    }
+
+    private async Task ShowResultsAsync(string gameName)
+    {
+        StatusText.Text = "Looking for covers on the Steam store…";
+        var results = await OnlineArt.SearchAsync(gameName);
+        if (results is null)
+        {
+            StatusText.Text = "The Steam store could not be reached. Try again later, or choose a picture of your own.";
+            return;
+        }
+        var shown = results.Take(MaxResults).ToList();
+        var covers = await OnlineArt.CoverUrlsAsync([.. shown.Select(result => result.AppId)]);
+        var found = shown.Where(result => covers.ContainsKey(result.AppId)).ToList();
+        StatusText.Text = found.Count == 0 ? "The Steam store has no cover for this name. Choose a picture of your own."
+            : "Click a cover to use it.";
+        foreach (var result in found)
+        {
+            var button = new Button { Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(4), ToolTip = result.Name, Width = 112 };
+            System.Windows.Automation.AutomationProperties.SetName(button, result.Name);
+            var content = new StackPanel();
+            var picture = new Image { Width = 100, Height = 150, Stretch = Stretch.UniformToFill };
+            content.Children.Add(picture);
+            content.Children.Add(new TextBlock { Text = result.Name, FontSize = 11, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 30, Margin = new Thickness(0, 4, 0, 0) });
+            button.Content = content;
+            var cover = covers[result.AppId];
+            button.Click += (_, _) => { StoreCoverChosen?.Invoke(cover); Close(); };
+            Results.Children.Add(button);
+            if (await OnlineArt.GetImageAsync(cover) is { } bytes) picture.Source = Thumbnail(bytes);
+        }
+    }
+
+    private static BitmapImage? Thumbnail(byte[] bytes)
+    {
+        try
+        {
+            var image = new BitmapImage();
+            image.BeginInit();
+            image.CacheOption = BitmapCacheOption.OnLoad;
+            image.DecodePixelWidth = 200;
+            image.StreamSource = new MemoryStream(bytes);
+            image.EndInit();
+            image.Freeze();
+            return image;
+        }
+        catch (Exception failure) when (failure is not OutOfMemoryException)
+        {
+            Serilog.Log.Warning(failure, "a Steam cover could not be shown");
+            return null;
+        }
+    }
+}
diff --git a/src/NeoFences.App/FenceHost.Covers.cs b/src/NeoFences.App/FenceHost.Covers.cs
new file mode 100644
index 0000000..6eec6fa
--- /dev/null
+++ b/src/NeoFences.App/FenceHost.Covers.cs
@@ -0,0 +1,246 @@
+using System.IO;
+using System.Windows.Controls;
+using System.Windows.Threading;
+using NeoFences.Core.Items;
+using NeoFences.Core.Library;
+using NeoFences.Shell;
+using Serilog;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// Game covers and website icons (M34, spec 2026-10-06-icons-and-game-tiles-design, ADR-055): the art order, the ask-once
+/// question, the online lookup after each library scan (only after a yes), website icons from the sites themselves,
+/// "Choose cover…" and the cover sizes. Only NeoFences' own files change (the covers folder and its index).
+/// </summary>
+public sealed partial class FenceHost
+{
+    private CoversIndex _coversIndex = CoversIndex.Empty;
+    private bool _coverLookupRunning, _onlineArtAskPending;
+    private OnlineArtWindow? _onlineArtWindow;
+    private readonly HashSet<string> _siteIconsFetching = new(StringComparer.OrdinalIgnoreCase);
+
+    private static string CoversIndexPath => Path.Combine(AppPaths.CoversDirectory, "index.json");
+
+    private void LoadCovers()
+    {
+        _coversIndex = CoversIndex.Load(CoversIndexPath);
+        PublishSiteIcons();
+        _iconLoader.SiteIconWanted += OnSiteIconWanted;
+    }
+
+    /// <summary>The art a game shows: the user's choice, the cover on disk, a cover found online, the logo; null: the glow tile.</summary>
+    private CoverArt? ArtOf(GameEntry game) =>
+        GameArt.Choose(chosen: ChosenCover(game), poster: game.Poster, online: FoundCover(game), logo: game.IconPath);
+
+    private string? ChosenCover(GameEntry game) =>
+        _config.Library.CoverChoices.TryGetValue(game.Id, out var file) && Path.Combine(AppPaths.CoversDirectory, file) is var path && File.Exists(path) ? path : null;
+
+    /// <summary>A cover found online stays when the switch is turned off later (spec §2: what was found is kept).</summary>
+    private string? FoundCover(GameEntry game) =>
+        _coversIndex.Games.TryGetValue(game.Id, out var record) && record.File is { } file && Path.Combine(AppPaths.CoversDirectory, file) is var path && File.Exists(path) ? path : null;
+
+    private List<GameEntry> GamesWithoutCover() =>
+        [.. _library.Items.Select(item => item.Game).Where(game => GameArt.NeedsLookup(ChosenCover(game), game.Poster))];
+
+    /// <summary>
+    /// Asked once (spec §2): the first time a scan leaves games without a cover. Held while a game runs; closing the window
+    /// without an answer asks again at the next scan. Never in safe mode or a read-only session.
+    /// </summary>
+    private void MaybeAskOnlineArt()
+    {
+        if (_config.Library.OnlineArt is not null || SafeMode || _configReadOnly || _onlineArtWindow is not null) return;
+        var without = GamesWithoutCover();
+        if (without.Count == 0) return;
+        if (_gameMode)
+        {
+            _onlineArtAskPending = true; // CheckGameMode asks once the game ends
+            return;
+        }
+        _onlineArtAskPending = false;
+        _onlineArtWindow = new OnlineArtWindow(without.Count);
+        _onlineArtWindow.Answered += SetOnlineArt;
+        _onlineArtWindow.Closed += (_, _) => _onlineArtWindow = null;
+        _onlineArtWindow.Show();
+        Log.Information("online art: asked ({Count} games without a cover)", without.Count);
+    }
+
+    private void SetOnlineArt(bool on)
+    {
+        if (_config.Library.OnlineArt == on) return;
+        _config = _config with { Library = _config.Library with { OnlineArt = on } };
+        Log.Information("online art: {State}", on ? "on" : "off");
+        SaveNow();
+        RefreshSettings();
+        if (!on) return;
+        LookUpCovers();
+        foreach (var window in _windows.Values) window.ReloadIcons(); // websites ask for their icons
+    }
+
+    /// <summary>
+    /// After a library scan (spec §2): games still without a cover are looked up on the Steam store, one at a time, in the
+    /// background; strict names only. A miss is remembered (30 days, or until the name changes); offline records nothing.
+    /// </summary>
+    private void LookUpCovers()
+    {
+        if (_config.Library.OnlineArt != true || _coverLookupRunning || !Current.ExtrasWanted || _gameMode || _libraryStopped) return;
+        var now = DateTimeOffset.Now;
+        var index = _coversIndex;
+        var wanted = GamesWithoutCover().Where(game => GameArt.ShouldLookUp(index.Games.GetValueOrDefault(game.Id), game.Name, now)).ToList();
+        if (wanted.Count == 0) return;
+        _coverLookupRunning = true;
+        Log.Information("online art: looking up {Count} game(s)", wanted.Count);
+        Task.Run(async () =>
+        {
+            var matched = new Dictionary<GameEntry, int>();
+            var missed = new List<GameEntry>();
+            foreach (var game in wanted)
+            {
+                if (await OnlineArt.SearchAsync(game.Name) is not { } results) break; // offline: the rest waits for the next scan
+                if (results.FirstOrDefault(result => GameArt.SameName(game.Name, result.Name)) is { } match) matched[game] = match.AppId;
+                else missed.Add(game);
+                await Task.Delay(300); // one request at a time, gently
+            }
+            var urls = await OnlineArt.CoverUrlsAsync([.. matched.Values.Distinct()]);
+            var found = new Dictionary<GameEntry, (int AppId, string File)>();
+            foreach (var (game, appId) in matched)
+            {
+                var file = $"steam-{appId}.jpg";
+                if (File.Exists(Path.Combine(AppPaths.CoversDirectory, file))
+                    || urls.TryGetValue(appId, out var url) && await OnlineArt.DownloadImageAsync(url, Path.Combine(AppPaths.CoversDirectory, file)))
+                {
+                    found[game] = (appId, file);
+                }
+            }
+            return (Found: found, Missed: missed);
+        }).ContinueWith(lookup =>
+        {
+            _coverLookupRunning = false;
+            if (lookup.IsFaulted)
+            {
+                Log.Warning(lookup.Exception, "online art: the lookup failed"); // hard rule 7: the glow tiles stay
+                return;
+            }
+            var (found, missed) = lookup.Result;
+            var checkedAt = DateTimeOffset.Now;
+            foreach (var (game, cover) in found) _coversIndex = _coversIndex.WithGame(game.Id, new CoverRecord(game.Name, checkedAt, cover.AppId, cover.File));
+            foreach (var game in missed) _coversIndex = _coversIndex.WithGame(game.Id, new CoverRecord(game.Name, checkedAt));
+            SaveCoversIndex();
+            Log.Information("online art: {Found} cover(s) found, {Missed} not on the Steam store", found.Count, missed.Count);
+            if (found.Count == 0) return;
+            _libraryLister?.Refresh();
+            RefreshWindows();
+        }, TaskScheduler.FromCurrentSynchronizationContext());
+    }
+
+    private void SaveCoversIndex()
+    {
+        try
+        {
+            _coversIndex.Save(CoversIndexPath);
+        }
+        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
+        {
+            Log.Warning(failure, "online art: the covers index could not be saved; the art is looked up again next time");
+        }
+    }
+
+    /// <summary>The icon files found so far, for the icon loader's threads (a new dictionary each time: they only read it).</summary>
+    private void PublishSiteIcons() =>
+        _iconLoader.SiteIconFiles = _coversIndex.Sites.Where(site => site.Value.File is not null)
+            .ToDictionary(site => site.Key, site => Path.Combine(AppPaths.SiteIconsDirectory, site.Value.File!), StringComparer.OrdinalIgnoreCase);
+
+    /// <summary>A website showed its letter badge: with online art on, its own icon is fetched once (a miss waits 30 days).</summary>
+    private void OnSiteIconWanted(string url)
+    {
+        if (_config.Library.OnlineArt != true || !Current.ExtrasWanted || !Uri.TryCreate(url, UriKind.Absolute, out var page)
+            || page.Scheme is not ("http" or "https") || !SiteIcons.ShouldFetch(_coversIndex.Sites.GetValueOrDefault(page.Host), DateTimeOffset.Now)
+            || !_siteIconsFetching.Add(page.Host)) return;
+        Task.Run(() => OnlineArt.FetchSiteIconAsync(page, AppPaths.SiteIconsDirectory)).ContinueWith(fetch =>
+        {
+            _siteIconsFetching.Remove(page.Host);
+            var file = fetch.IsCompletedSuccessfully ? fetch.Result : null;
+            _coversIndex = _coversIndex.WithSite(page.Host, new SiteRecord(DateTimeOffset.Now, file));
+            SaveCoversIndex();
+            Log.Information("online art: website icon for {Host}: {Result}", page.Host, file ?? "none");
+            if (file is null) return;
+            PublishSiteIcons();
+            foreach (var window in _windows.Values) window.ReloadIcons();
+        }, TaskScheduler.FromCurrentSynchronizationContext());
+    }
+
+    /// <summary>"Choose cover…" (spec §3): Steam's results, a picture of the user's own, or back to automatic.</summary>
+    private void ChooseCover(VirtualItem item)
+    {
+        if (LibraryItemOf(item.Target)?.Game is not { } game) return;
+        var window = new ChooseCoverWindow(game.Name, online: _config.Library.OnlineArt == true && Current.ExtrasWanted,
+            hasChoice: _config.Library.CoverChoices.ContainsKey(game.Id));
+        var choiceFile = $"choice-{FileNameOf(game.Id)}";
+        window.StoreCoverChosen += cover => Task.Run(() => OnlineArt.DownloadImageAsync(cover, Path.Combine(AppPaths.CoversDirectory, choiceFile + ".jpg")))
+            .ContinueWith(download =>
+            {
+                if (download.IsCompletedSuccessfully && download.Result) SetCoverChoice(game, choiceFile + ".jpg");
+                else Log.Warning("online art: the chosen cover for {Game} could not be downloaded", game.Name);
+            }, TaskScheduler.FromCurrentSynchronizationContext());
+        window.FileChosen += picture =>
+        {
+            var file = choiceFile + Path.GetExtension(picture).ToLowerInvariant();
+            try
+            {
+                Directory.CreateDirectory(AppPaths.CoversDirectory);
+                File.Copy(picture, Path.Combine(AppPaths.CoversDirectory, file), overwrite: true); // a copy: the user's file is never moved or changed
+                SetCoverChoice(game, file);
+            }
+            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
+            {
+                Log.Warning(failure, "the cover picture {Picture} could not be copied", picture);
+            }
+        };
+        window.ResetRequested += () => SetCoverChoice(game, file: null);
+        window.Show();
+    }
+
+    private void SetCoverChoice(GameEntry game, string? file)
+    {
+        var choices = new Dictionary<string, string>(_config.Library.CoverChoices, StringComparer.OrdinalIgnoreCase);
+        if (file is null)
+        {
+            if (choices.Remove(game.Id, out var old)) TryDeleteCover(old);
+        }
+        else choices[game.Id] = file;
+        _config = _config with { Library = _config.Library with { CoverChoices = choices } };
+        Log.Information("cover of {Game}: {Choice}", game.Name, file ?? "automatic");
+        SaveNow();
+        _libraryLister?.Refresh();
+        RefreshWindows();
+    }
+
+    private static void TryDeleteCover(string file)
+    {
+        try
+        {
+            File.Delete(Path.Combine(AppPaths.CoversDirectory, Path.GetFileName(file))); // NeoFences' own copy only
+        }
+        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
+        {
+            Log.Warning(failure, "an old cover copy could not be removed");
+        }
+    }
+
+    private static string FileNameOf(string gameId) =>
+        string.Concat(gameId.Take(80).Select(character => char.IsAsciiLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-'));
+
+    /// <summary>Size ▸ for covers (owner's choice at planning): Normal (1×2) or Large (2×4, twice).</summary>
+    private MenuItem CoverSizeMenu(IReadOnlyList<VirtualItem> items)
+    {
+        var size = new MenuItem { Header = "Size" };
+        var spans = items.Select(FenceGrid.SpanOf).Distinct().ToList();
+        foreach (var (name, span) in new[] { ("Normal", CoverSizes.Normal), ("Large (twice as big)", CoverSizes.Large) })
+        {
+            var entry = new MenuItem { Header = name, IsCheckable = true, IsChecked = spans.Count == 1 && spans[0] == span };
+            entry.Click += (_, _) => SetSize([.. items.Select(item => item.Id)], span == CoverSizes.Normal ? null : span);
+            size.Items.Add(entry);
+        }
+        return size;
+    }
+}
diff --git a/src/NeoFences.App/FenceHost.GameItems.cs b/src/NeoFences.App/FenceHost.GameItems.cs
index 186e5c7..4f34c8b 100644
--- a/src/NeoFences.App/FenceHost.GameItems.cs
+++ b/src/NeoFences.App/FenceHost.GameItems.cs
@@ -132,7 +132,8 @@ public sealed partial class FenceHost
         Command(showAs, "Cover tile", () => SetShowAs(item.Id, ItemShow.Cover), isChecked: GameItems.ShowsCover(item));
         Command(showAs, "Icon", () => SetShowAs(item.Id, ItemShow.Icon), isChecked: !GameItems.ShowsCover(item));
         menu.Items.Add(showAs);
-        menu.Items.Add(SizeMenu(menu, [item])); // M24
+        if (GameItems.ShowsCover(item)) Command(menu, "Choose cover…", () => ChooseCover(item)); // M34
+        menu.Items.Add(GameItems.ShowsCover(item) ? CoverSizeMenu([item]) : SizeMenu(menu, [item])); // M24; M34: Normal / Large covers
         var openFolder = new MenuItem { Header = "Open install folder", IsEnabled = installed && LibraryItemOf(item.Target)?.Game.InstallFolder is not null }; // M23
         openFolder.Click += (_, _) => OpenInstallFolder(window, item.Target);
         menu.Items.Add(openFolder);
diff --git a/src/NeoFences.App/FenceHost.Items.cs b/src/NeoFences.App/FenceHost.Items.cs
index ffed5dd..abcba08 100644
--- a/src/NeoFences.App/FenceHost.Items.cs
+++ b/src/NeoFences.App/FenceHost.Items.cs
@@ -109,7 +109,7 @@ public sealed partial class FenceHost
         if (items.Count > 1)
         {
             Command("Open", () => { foreach (var item in items) OpenKey(window, item.Id); }); // a widget opens its own app, never ShellExecute on its target (M25 review I2)
-            menu.Items.Add(SizeMenu(menu, items)); // M24
+            menu.Items.Add(items.All(GameItems.ShowsCover) ? CoverSizeMenu(items) : SizeMenu(menu, items)); // M24; M34: covers' own sizes
             Command($"Remove {items.Count} items from fence", () => RemoveItems(window, [.. items.Select(item => item.Id)]));
         }
         else if (FolderPanels.IsPanel(items[0]))
diff --git a/src/NeoFences.App/FenceHost.Library.cs b/src/NeoFences.App/FenceHost.Library.cs
index 5fdbe92..1d552d2 100644
--- a/src/NeoFences.App/FenceHost.Library.cs
+++ b/src/NeoFences.App/FenceHost.Library.cs
@@ -149,6 +149,11 @@ public sealed partial class FenceHost
         else ReplaceLibraryWatchers(watch);
         _libraryLister?.Refresh(); // new art, order
         if (state is not null) ApplyGames(previous, state); // M22: game items follow the scan; new games go to their fence
+        if (state is not null)
+        {
+            MaybeAskOnlineArt(); // M34 (ADR-055): asked once
+            LookUpCovers(); // only after a yes
+        }
         RefreshSettings();
         if (!_libraryScanAgain) return;
         _libraryScanAgain = false;
@@ -240,8 +245,7 @@ public sealed partial class FenceHost
         foreach (var item in _library.Items)
         {
             var itemRef = Path.Combine(AppPaths.LibraryDirectory, item.FileName);
-            if (item.Game.Poster is { } poster) art[itemRef] = (poster, true);
-            else if (item.Game.IconPath is { } logo && Path.GetExtension(logo).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg") art[itemRef] = (logo, false);
+            if (ArtOf(item.Game) is { } chosen) art[itemRef] = (chosen.Path, chosen.IsPoster); // M34: choice, disk, online, logo
         }
         return art;
     }
@@ -312,7 +316,8 @@ public sealed partial class FenceHost
         Hidden: HiddenGamesForSettings(),
         Status: LibraryWanted ? _libraryStatus : "No games in any fence yet: fence menu → Add games…, or choose where new games go.",
         Fences: [.. _config.Fences.Where(fence => fence.Kind == FenceKind.Items).Select(fence => (fence.Id, fence.Title))],
-        NewGamesFence: _config.Library.NewGamesFence);
+        NewGamesFence: _config.Library.NewGamesFence,
+        OnlineArt: _config.Library.OnlineArt == true); // M34
 
     private void WireLibrarySettings(SettingsWindow window)
     {
@@ -332,6 +337,7 @@ public sealed partial class FenceHost
             Change(library => library with { Hidden = [.. library.Hidden.Where(hiddenId => !ids.Contains(hiddenId))] }, "show again");
         };
         window.RefreshLibraryRequested += () => ScanLibrary(full: true);
+        window.OnlineArtChanged += SetOnlineArt; // M34 (ADR-055)
         window.NewGamesFenceChanged += fenceId =>
         {
             Change(library => library with { NewGamesFence = fenceId }, "new games go to");
diff --git a/src/NeoFences.App/FenceHost.cs b/src/NeoFences.App/FenceHost.cs
index ade5ce3..0ad9db3 100644
--- a/src/NeoFences.App/FenceHost.cs
+++ b/src/NeoFences.App/FenceHost.cs
@@ -134,6 +134,7 @@ public sealed partial class FenceHost
 
     public void Start()
     {
+        _watchdog.RemoveStaleFiles(); // M34: files a power cut left behind
         // M33 review I8: the watchdog first, so a crash while loading or migrating the data is caught too.
         _watchdog.LaunchDetached(Environment.ProcessId);
         if (SafeMode) TryMarker(() => _watchdog.MarkSafeMode(Environment.ProcessId), what: "safe-mode marker"); // M33: a crash now stops and asks
@@ -148,6 +149,7 @@ public sealed partial class FenceHost
             loadedItems.Source, loadedItems.IsReadOnly, loadedItems.CorruptCopyPath, loadedItems.Document.Fences.Values.Sum(items => items.Count));
         _items = loadedItems.Document;
         _itemsReadOnly = loadedItems.IsReadOnly;
+        LoadCovers(); // M34
         LoadNotices(configSource: loaded.Source, configReadOnly: loaded.IsReadOnly, itemsSource: loadedItems.Source, itemsReadOnly: loadedItems.IsReadOnly); // M33
         MigrateGames(LibraryWriter.ReadIndex(AppPaths.LibraryDirectory)); // M22: an old Game Library fence becomes game items (a snapshot first)
         MigrateFolderViews(); // M26: an old folder view becomes a fence holding one panel (a snapshot first)
@@ -1220,6 +1222,7 @@ public sealed partial class FenceHost
             ApplyDeferredShellWork();
             OnWidgetTick(); // M28: widgets right at once
             ShowFirstStartNotice(); // M32: a notice held back by a game
+            if (_onlineArtAskPending) MaybeAskOnlineArt(); // M34: the covers question held back by a game
             FlushNotices(); // M33 review I7: safe mode or save notices held back by a game
         }
         UpdatePeekHotkey();
diff --git a/src/NeoFences.App/FenceItemView.cs b/src/NeoFences.App/FenceItemView.cs
index 8dc4521..59b4e5d 100644
--- a/src/NeoFences.App/FenceItemView.cs
+++ b/src/NeoFences.App/FenceItemView.cs
@@ -199,6 +199,7 @@ public sealed class FenceItemView : INotifyPropertyChanged
         var height = Span.Rows * cellHeight - CellPaddingY - labelHeight;
         var icon = Span == GridSpan.One ? iconDips : Math.Clamp(Math.Floor(Math.Min(width - 8, height)), iconDips, MaxIcon);
         var tileHeight = Math.Max(16, Math.Floor(Math.Min(height, (width - 4) * 1.5)));
+        if (Tile) icon = Math.Clamp(Math.Round(tileHeight / 1.5 * 0.55), 24, 96); // M34: the glow tile's icon, about half the tile (Blur's 32 px icon at about twice)
         (WidgetWidth, WidgetHeight) = (width, Span.Rows * cellHeight - CellPaddingY); // M25: no label under a widget (M26: a panel's area too)
         var changed = Math.Abs(icon - IconDips) > 0.5;
         (ContentWidth, IconDips, TileWidth, TileHeight) = (width, icon, Math.Floor(tileHeight / 1.5), tileHeight);
@@ -211,7 +212,26 @@ public sealed class FenceItemView : INotifyPropertyChanged
     public ImageSource? Icon
     {
         get;
-        set { field = value; Changed(); }
+        set { field = value; Changed(); Glow = Tile ? GlowOf(value) : null; } // only a tile has a glow
+    }
+
+    /// <summary>
+    /// The glow tile's backdrop (M34, spec §4): the icon shrunk to a few pixels; stretched over the 2:3 tile it becomes a soft
+    /// field of the icon's colours (no blur effect: effects render in software on a layered window).
+    /// </summary>
+    public ImageSource? Glow
+    {
+        get;
+        private set { field = value; Changed(); }
+    }
+
+    private static ImageSource? GlowOf(ImageSource? icon)
+    {
+        if (icon is not System.Windows.Media.Imaging.BitmapSource { PixelWidth: > 0, PixelHeight: > 0 } bitmap) return null;
+        var scale = 6.0 / Math.Max(bitmap.PixelWidth, bitmap.PixelHeight);
+        var glow = new System.Windows.Media.Imaging.TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
+        glow.Freeze();
+        return glow;
     }
 
     /// <summary>The host wants a tile for this item (M22: a game item's cover), and its art; the window decides <see cref="IsTile"/>.</summary>
diff --git a/src/NeoFences.App/FenceWindow.xaml b/src/NeoFences.App/FenceWindow.xaml
index 7ef0f13..4bc4260 100644
--- a/src/NeoFences.App/FenceWindow.xaml
+++ b/src/NeoFences.App/FenceWindow.xaml
@@ -4,7 +4,9 @@
         xmlns:local="clr-namespace:NeoFences.App"
         Title="NeoFences fence" Width="320" Height="220"
         WindowStyle="None" ResizeMode="CanResize" AllowsTransparency="True"
-        Background="#01000000" ShowInTaskbar="False" ShowActivated="False">
+        Background="#01000000" ShowInTaskbar="False" ShowActivated="False"
+        UseLayoutRounding="True" RenderOptions.BitmapScalingMode="HighQuality">
+    <!-- M34: edges on whole pixels and high-quality scaling, so icons and covers stay sharp at 125/150 %. -->
     <!-- Layered window (AllowsTransparency) + accent blur, owned by Progman: ADR-011.
          The 1/255-alpha background keeps the empty area hit-testable (fully transparent pixels click through).
          Colours are DynamicResources set by ApplyTheme (light/dark follows Windows, M2c). -->
@@ -17,6 +19,8 @@
         <sys:Double x:Key="ItemWidth" xmlns:sys="clr-namespace:System;assembly=System.Runtime">76</sys:Double>
         <!-- Icon-only fences (M8b): labels collapse, the name pops under the hovered or selected icon instead. -->
         <Visibility x:Key="LabelVisibility">Visible</Visibility>
+        <!-- M34: a cover's name over its bottom, on fences with labels on hover. -->
+        <Visibility x:Key="TileNameVisibility">Collapsed</Visibility>
         <sys:Boolean x:Key="ItemToolTips" xmlns:sys="clr-namespace:System;assembly=System.Runtime">True</sys:Boolean>
         <Visibility x:Key="ShortcutArrowVisibility">Collapsed</Visibility>
         <sys:Double x:Key="TileWidth" xmlns:sys="clr-namespace:System;assembly=System.Runtime">72</sys:Double>
@@ -283,7 +287,8 @@
                                             <!-- The outer border fills the gaps between cells, so the pointer is always over some cell and the
                                                  pop-under name does not blink between neighbours (final review I2). -->
                                             <Border Background="Transparent" Padding="2">
-                                                <Border x:Name="Chrome" Background="Transparent" CornerRadius="4" Padding="2,4">
+                                                <!-- M34 (spec §4, "Clean"): no slab; a faint rounded hover, the accent for the selection. -->
+                                                <Border x:Name="Chrome" Background="Transparent" BorderBrush="Transparent" BorderThickness="1" CornerRadius="6" Padding="1,3">
                                                     <ContentPresenter HorizontalAlignment="Center" />
                                                 </Border>
                                             </Border>
@@ -292,11 +297,18 @@
                                                     <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceHover}" />
                                                 </Trigger>
                                                 <Trigger Property="IsSelected" Value="True">
-                                                    <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceSelected}" />
+                                                    <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource ItemSelectedFill}" />
+                                                    <Setter TargetName="Chrome" Property="BorderBrush" Value="{DynamicResource ItemSelectedEdge}" />
                                                 </Trigger>
+                                                <!-- M34: a cover tile lifts and rings itself (style A); the cell around it stays clear. -->
+                                                <DataTrigger Binding="{Binding IsTile}" Value="True">
+                                                    <Setter TargetName="Chrome" Property="Background" Value="Transparent" />
+                                                    <Setter TargetName="Chrome" Property="BorderBrush" Value="Transparent" />
+                                                </DataTrigger>
                                                 <!-- M26: a panel's rows show hover and selection themselves; the whole panel never tints (last: it wins). -->
                                                 <DataTrigger Binding="{Binding IsPanel}" Value="True">
                                                     <Setter TargetName="Chrome" Property="Background" Value="Transparent" />
+                                                    <Setter TargetName="Chrome" Property="BorderBrush" Value="Transparent" />
                                                 </DataTrigger>
                                                 <!-- A selected panel (by its name row or menu) shows a thin outline instead (final review M12). -->
                                                 <MultiDataTrigger>
@@ -340,22 +352,53 @@
                                             </Canvas>
                                         </Viewbox>
                                     </Grid>
-                                    <!-- Game Library tile (M12): poster, logo, or the icon centred on a dark 2:3 tile. -->
-                                    <Border x:Name="Tile" Visibility="Collapsed" HorizontalAlignment="Center" CornerRadius="4" Background="#66000000"
-                                            Width="{Binding TileWidth}" Height="{Binding TileHeight}" ClipToBounds="True">
-                                        <Grid>
-                                            <Image x:Name="TileIcon" Source="{Binding Icon}" Width="{Binding IconDips}" Height="{Binding IconDips}" />
-                                            <Image x:Name="TileLogo" Source="{Binding Art}" Margin="10" Stretch="Uniform" Visibility="Collapsed" />
-                                            <Image x:Name="TilePoster" Source="{Binding Art}" Stretch="UniformToFill" Visibility="Collapsed" />
-                                            <!-- A not-installed game shown as its cover (M22, final review I1): the same badge as on icons. -->
-                                            <Border x:Name="TileBadge" Visibility="Collapsed" HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="4"
-                                                    Width="16" Height="16" CornerRadius="8" Background="#FFE8A33D" BorderBrush="#FF3A2A10" BorderThickness="1"
-                                                    IsHitTestVisible="False">
-                                                <TextBlock Text="!" Foreground="#FF3A2A10" FontWeight="Bold" FontSize="11"
-                                                           HorizontalAlignment="Center" VerticalAlignment="Center" />
-                                            </Border>
-                                        </Grid>
-                                    </Border>
+                                    <!-- Game tile (M12, M34 style A): a rounded 2:3 cover with a soft shadow; without art, the glow tile (the icon's
+                                         colours as a backdrop, the icon in the middle). Hover lifts it, selection rings it in the accent; on a fence
+                                         with labels on hover the name fades in over the bottom (style B's overlay). -->
+                                    <Grid x:Name="Tile" Visibility="Collapsed" HorizontalAlignment="Center" Width="{Binding TileWidth}" Height="{Binding TileHeight}">
+                                        <Border Margin="-2,1,-2,-3" CornerRadius="11" Background="{DynamicResource TileShadowFar}" IsHitTestVisible="False" />
+                                        <Border Margin="-1,1,-1,-1" CornerRadius="9" Background="{DynamicResource TileShadowNear}" IsHitTestVisible="False" />
+                                        <Border x:Name="TileGlow" CornerRadius="8">
+                                            <Border.Background>
+                                                <ImageBrush ImageSource="{Binding Glow}" Stretch="UniformToFill" />
+                                            </Border.Background>
+                                        </Border>
+                                        <Border x:Name="TileShade" CornerRadius="8">
+                                            <Border.Background>
+                                                <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
+                                                    <GradientStop Color="#33000000" Offset="0" />
+                                                    <GradientStop Color="#8C000000" Offset="1" />
+                                                </LinearGradientBrush>
+                                            </Border.Background>
+                                        </Border>
+                                        <Image x:Name="TileIcon" Source="{Binding Icon}" Width="{Binding IconDips}" Height="{Binding IconDips}" Margin="0,0,0,10" />
+                                        <Image x:Name="TileLogo" Source="{Binding Art}" Margin="14" Stretch="Uniform" Visibility="Collapsed" />
+                                        <Border x:Name="TilePoster" CornerRadius="8" Visibility="Collapsed">
+                                            <Border.Background>
+                                                <ImageBrush ImageSource="{Binding Art}" Stretch="UniformToFill" />
+                                            </Border.Background>
+                                        </Border>
+                                        <Border x:Name="TileName" VerticalAlignment="Bottom" CornerRadius="0,0,8,8" Padding="6,22,6,7" Opacity="0"
+                                                Visibility="{DynamicResource TileNameVisibility}" IsHitTestVisible="False">
+                                            <Border.Background>
+                                                <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
+                                                    <GradientStop Color="#00000000" Offset="0" />
+                                                    <GradientStop Color="#D9000000" Offset="1" />
+                                                </LinearGradientBrush>
+                                            </Border.Background>
+                                            <TextBlock Text="{Binding Label}" Foreground="White" FontSize="12" FontWeight="SemiBold" TextAlignment="Center"
+                                                       TextWrapping="Wrap" TextTrimming="CharacterEllipsis" MaxHeight="32" />
+                                        </Border>
+                                        <Border x:Name="TileRing" Margin="-3" CornerRadius="10" BorderThickness="2" BorderBrush="{DynamicResource ItemSelectedEdge}"
+                                                Visibility="Collapsed" IsHitTestVisible="False" />
+                                        <!-- A not-installed game shown as its cover (M22, final review I1): the same badge as on icons. -->
+                                        <Border x:Name="TileBadge" Visibility="Collapsed" HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="4"
+                                                Width="16" Height="16" CornerRadius="8" Background="#FFE8A33D" BorderBrush="#FF3A2A10" BorderThickness="1"
+                                                IsHitTestVisible="False">
+                                            <TextBlock Text="!" Foreground="#FF3A2A10" FontWeight="Bold" FontSize="11"
+                                                       HorizontalAlignment="Center" VerticalAlignment="Center" />
+                                        </Border>
+                                    </Grid>
                                     <!-- M25 widgets, M26 panels: their own templates, built only for those elements (M28: lighter items). -->
                                     <!-- Collapsed for plain items: without a template it would print the item's type name. -->
                                     <ContentControl x:Name="Extra" Content="{Binding}" Focusable="False" IsTabStop="False" HorizontalAlignment="Center" Visibility="Collapsed" />
@@ -386,6 +429,17 @@
                                     <DataTrigger Binding="{Binding ArtKind}" Value="Poster">
                                         <Setter TargetName="TilePoster" Property="Visibility" Value="Visible" />
                                         <Setter TargetName="TileIcon" Property="Visibility" Value="Collapsed" />
+                                        <Setter TargetName="TileGlow" Property="Visibility" Value="Collapsed" />
+                                        <Setter TargetName="TileShade" Property="Visibility" Value="Collapsed" />
+                                    </DataTrigger>
+                                    <!-- M34: hover lifts the tile a little and shows the name over it (labels on hover); selection rings it. -->
+                                    <DataTrigger Binding="{Binding IsMouseOver, RelativeSource={RelativeSource AncestorType=ListBoxItem}}" Value="True">
+                                        <Setter TargetName="Tile" Property="Margin" Value="0,-2,0,2" />
+                                        <Setter TargetName="TileName" Property="Opacity" Value="1" />
+                                    </DataTrigger>
+                                    <DataTrigger Binding="{Binding IsSelected, RelativeSource={RelativeSource AncestorType=ListBoxItem}}" Value="True">
+                                        <Setter TargetName="TileRing" Property="Visibility" Value="Visible" />
+                                        <Setter TargetName="TileName" Property="Opacity" Value="1" />
                                     </DataTrigger>
                                     <DataTrigger Binding="{Binding ArtKind}" Value="Logo">
                                         <Setter TargetName="TileLogo" Property="Visibility" Value="Visible" />
diff --git a/src/NeoFences.App/FenceWindow.xaml.cs b/src/NeoFences.App/FenceWindow.xaml.cs
index 1aa3222..424ed23 100644
--- a/src/NeoFences.App/FenceWindow.xaml.cs
+++ b/src/NeoFences.App/FenceWindow.xaml.cs
@@ -937,6 +937,7 @@ public partial class FenceWindow : Window
     {
         _labelMode = labelMode;
         Resources["LabelVisibility"] = labelMode == LabelMode.Always ? Visibility.Visible : Visibility.Collapsed;
+        Resources["TileNameVisibility"] = labelMode == LabelMode.Always ? Visibility.Collapsed : Visibility.Visible; // M34: a cover shows its name over itself
         Resources["ItemToolTips"] = labelMode == LabelMode.Always; // the pop-under name replaces the tooltip
         LabelsAlwaysItem.IsChecked = labelMode == LabelMode.Always;
         LabelsOnHoverItem.IsChecked = labelMode == LabelMode.OnHover;
@@ -963,8 +964,16 @@ public partial class FenceWindow : Window
     /// An element's width in a fence: with labels, room for two short words under small icons; icons only, a tight grid —
     /// as wide as a 2:3 cover when the fence shows covers (M28: covers keep their size).
     /// </summary>
-    private static double ItemWidthFor(int iconSize, LabelMode labels, bool covers) =>
-        labels == LabelMode.Always ? Math.Max(76.0, iconSize + 28.0) : covers ? Math.Round(iconSize * 1.5) + 12.0 : iconSize + 12.0;
+    private static double ItemWidthFor(int iconSize, LabelMode labels, bool covers)
+    {
+        var plain = labels == LabelMode.Always ? Math.Max(76.0, iconSize + 28.0) : iconSize + 12.0;
+        if (!covers) return plain;
+        // M34 (spec §4): a Normal cover (1×2) fills its two rows exactly: (cell width − 12) × 1.5 = 2 × cell height − 12 − label,
+        // so no gap opens between cover rows and a Large cover (2×4) is about twice as big.
+        var label = labels == LabelMode.Always ? 36.0 : 0.0;
+        var cellHeight = iconSize + 12 + label;
+        return Math.Max(plain, Math.Floor((2 * cellHeight - 12 - label) / 1.5 + 12) - 8);
+    }
 
     /// <summary>A fence's columns at this window's width (M28: a hidden tab's own, for its new elements in a Free fence).</summary>
     public int ColumnsFor(Fence fence, bool covers) =>
@@ -992,7 +1001,7 @@ public partial class FenceWindow : Window
     {
         var container = _labelMode != LabelMode.OnHover ? null
             : _hoveredContainer ?? (ItemList.SelectedItems.Count == 1 ? ItemList.ItemContainerGenerator.ContainerFromItem(ItemList.SelectedItem) as ListBoxItem : null);
-        if (container is not { DataContext: FenceItemView { IsPanel: false } view, IsVisible: true }) // M28: never over a panel
+        if (container is not { DataContext: FenceItemView { IsPanel: false, IsTile: false } view, IsVisible: true }) // M28: never over a panel; M34: a cover shows its name itself
         {
             HoverLabel.Visibility = Visibility.Collapsed;
             return;
@@ -1179,9 +1188,10 @@ public partial class FenceWindow : Window
     {
         _lightTheme = light;
         var ink = light ? Colors.Black : Colors.White;
-        SolidColorBrush Ink(byte alpha)
+        SolidColorBrush Ink(byte alpha, Color? colour = null)
         {
-            var brush = new SolidColorBrush(Color.FromArgb(alpha, ink.R, ink.G, ink.B));
+            var tone = colour ?? ink;
+            var brush = new SolidColorBrush(Color.FromArgb(alpha, tone.R, tone.G, tone.B));
             brush.Freeze();
             return brush;
         }
@@ -1217,6 +1227,9 @@ public partial class FenceWindow : Window
         var widgetShadow = new DropShadowEffect { Color = light ? Colors.White : Colors.Black, ShadowDepth = light ? 0 : 1, BlurRadius = 8, Opacity = 0.45 };
         widgetShadow.Freeze();
         Resources["WidgetShadow"] = widgetShadow;
+        // M34: a cover's soft shadow, drawn as two faint rounded layers (an effect per tile would render in software here).
+        Resources["TileShadowFar"] = Ink(light ? (byte)0x10 : (byte)0x18, Colors.Black);
+        Resources["TileShadowNear"] = Ink(light ? (byte)0x1C : (byte)0x30, Colors.Black);
         ApplyWidgetBar(light);
     }
 
@@ -1226,6 +1239,13 @@ public partial class FenceWindow : Window
         var bar = light ? new LinearGradientBrush(SystemColors.AccentColorDark1, SystemColors.AccentColor, 0) : new LinearGradientBrush(SystemColors.AccentColorLight2, SystemColors.AccentColorLight1, 0);
         bar.Freeze();
         Resources["WidgetBar"] = bar;
+        // M34 (spec §4): items and tiles select in the accent too: a tinted rounded box with an accent edge, a ring around a cover.
+        var accent = light ? SystemColors.AccentColorDark1 : SystemColors.AccentColorLight1;
+        var fill = new SolidColorBrush(Color.FromArgb(light ? (byte)0x30 : (byte)0x40, accent.R, accent.G, accent.B));
+        var edge = new SolidColorBrush(Color.FromArgb(0xD9, accent.R, accent.G, accent.B));
+        fill.Freeze();
+        edge.Freeze();
+        (Resources["ItemSelectedFill"], Resources["ItemSelectedEdge"]) = (fill, edge);
     }
 
     private FenceStyle? _style;
diff --git a/src/NeoFences.App/FolderPanelView.xaml b/src/NeoFences.App/FolderPanelView.xaml
index 08074d5..6ada37b 100644
--- a/src/NeoFences.App/FolderPanelView.xaml
+++ b/src/NeoFences.App/FolderPanelView.xaml
@@ -20,7 +20,7 @@
                                 <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceHover}" />
                             </Trigger>
                             <Trigger Property="IsSelected" Value="True">
-                                <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceSelected}" />
+                                <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource ItemSelectedFill}" /> <!-- M34: the accent -->
                             </Trigger>
                         </ControlTemplate.Triggers>
                     </ControlTemplate>
@@ -39,7 +39,7 @@
                                 <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceHover}" />
                             </Trigger>
                             <Trigger Property="IsSelected" Value="True">
-                                <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceSelected}" />
+                                <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource ItemSelectedFill}" /> <!-- M34: the accent -->
                             </Trigger>
                         </ControlTemplate.Triggers>
                     </ControlTemplate>
@@ -60,7 +60,7 @@
                                 <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceHover}" />
                             </Trigger>
                             <Trigger Property="IsSelected" Value="True">
-                                <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceSelected}" />
+                                <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource ItemSelectedFill}" /> <!-- M34: the accent -->
                             </Trigger>
                         </ControlTemplate.Triggers>
                     </ControlTemplate>
diff --git a/src/NeoFences.App/IconLoader.cs b/src/NeoFences.App/IconLoader.cs
index 0a9f63f..203bd39 100644
--- a/src/NeoFences.App/IconLoader.cs
+++ b/src/NeoFences.App/IconLoader.cs
@@ -4,6 +4,7 @@ using System.Windows.Media;
 using System.Windows.Media.Imaging;
 using System.Windows.Threading;
 using NeoFences.Core.Items;
+using NeoFences.Core.Library;
 using NeoFences.Shell;
 using Serilog;
 
@@ -11,8 +12,8 @@ namespace NeoFences.App;
 
 /// <summary>
 /// Loads display names and icons on two background STA threads (shell extensions expect STA; thumbnails of big
-/// files are slow) and hands them to the UI thread. An item's own name and icon win (M18); a website shows the default
-/// browser's icon. A failed load leaves the placeholder, never throws.
+/// files are slow) and hands them to the UI thread. An item's own name and icon win (M18); a website shows its own icon
+/// when NeoFences found one, else a letter badge (M34). A failed load leaves the placeholder, never throws.
 /// </summary>
 public sealed class IconLoader : IDisposable
 {
@@ -93,10 +94,68 @@ public sealed class IconLoader : IDisposable
         }
     }
 
-    private static BitmapSource? TargetIcon(LoadRequest request, ItemKind kind) =>
-        kind == ItemKind.Website
-            ? ShellItems.DefaultBrowserPath() is { } browser ? Frozen(ShellItems.TryGetImage(browser, request.SizePx)) : null
-            : Frozen(ShellItems.TryGetImage(request.Target, request.SizePx));
+    /// <summary>
+    /// Website icons found online (M34, ADR-055): host → icon file, kept by the host (set on the UI thread, read here). A website
+    /// without one asks the host once per load (<see cref="SiteIconWanted"/>, on the UI thread) and shows its letter badge.
+    /// </summary>
+    public volatile IReadOnlyDictionary<string, string> SiteIconFiles = new Dictionary<string, string>();
+
+    public event Action<string>? SiteIconWanted;
+
+    private BitmapSource? TargetIcon(LoadRequest request, ItemKind kind)
+    {
+        if (kind == ItemKind.Website) return SiteIcon(request);
+        // M34: a shortcut that starts a Store app through Explorer (Minecraft Launcher's) shows the app's icon, not Explorer's.
+        if (ShellLinks.AppsFolderOf(request.Target) is { } app && Frozen(ShellItems.TryGetImage(app, request.SizePx)) is { } appIcon) return appIcon;
+        return Frozen(ShellItems.TryGetImage(request.Target, request.SizePx));
+    }
+
+    /// <summary>The site's own icon when NeoFences found one, else its letter badge (never the browser's icon, M34).</summary>
+    private BitmapSource SiteIcon(LoadRequest request)
+    {
+        var host = Uri.TryCreate(request.Target, UriKind.Absolute, out var url) ? url.Host : "";
+        if (host.Length > 0 && SiteIconFiles.TryGetValue(host, out var file))
+        {
+            try
+            {
+                var decoder = BitmapDecoder.Create(new Uri(file), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
+                // An .ico holds several sizes: the largest frame, scaled to the item.
+                var frame = decoder.Frames.OrderByDescending(candidate => candidate.PixelWidth).First();
+                var scale = (double)request.SizePx / Math.Max(frame.PixelWidth, frame.PixelHeight);
+                BitmapSource sized = Math.Abs(scale - 1) < 0.01 ? frame : new TransformedBitmap(frame, new ScaleTransform(scale, scale));
+                sized.Freeze();
+                return sized;
+            }
+            catch (Exception failure) when (failure is not OutOfMemoryException)
+            {
+                Log.Warning(failure, "website icon {File} could not be read; its letter shows", file);
+            }
+        }
+        else if (host.Length > 0) _uiDispatcher.BeginInvoke(() => SiteIconWanted?.Invoke(request.Target));
+        return Badge(request.Target, request.SizePx);
+    }
+
+    /// <summary>The letter badge (M34): the site's first letter in white on its colour, a rounded square like an app icon.</summary>
+    private static BitmapSource Badge(string url, int sizePx)
+    {
+        var (letter, argb) = SiteIcons.Badge(url);
+        var visual = new DrawingVisual();
+        using (var drawing = visual.RenderOpen())
+        {
+            var fill = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
+            fill.Freeze();
+            var corner = sizePx * 0.22;
+            drawing.DrawRoundedRectangle(fill, null, new System.Windows.Rect(0, 0, sizePx, sizePx), corner, corner);
+            var text = new FormattedText(letter.ToString(), System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
+                new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), System.Windows.FontStyles.Normal, System.Windows.FontWeights.SemiBold,
+                    System.Windows.FontStretches.Normal), sizePx * 0.52, Brushes.White, 1.0);
+            drawing.DrawText(text, new System.Windows.Point((sizePx - text.Width) / 2, (sizePx - text.Height) / 2));
+        }
+        var bitmap = new RenderTargetBitmap(sizePx, sizePx, 96, 96, PixelFormats.Pbgra32);
+        bitmap.Render(visual);
+        bitmap.Freeze();
+        return bitmap;
+    }
 
     private static bool _websiteIconFailureLogged;
 
@@ -114,7 +173,7 @@ public sealed class IconLoader : IDisposable
         }
         if (kind != ItemKind.Path && !ItemKinds.IsApp(request.Target)) return null; // a special item without an icon stays as it is
         var looksLikeFolder = kind == ItemKind.Path && !Path.HasExtension(request.Target.TrimEnd('\\')); // ponytail: by its name; the check knows better
-        return Frozen(ShellItems.TryGetGenericImage(request.Target, looksLikeFolder));
+        return Frozen(ShellItems.TryGetGenericImage(request.Target, looksLikeFolder, request.SizePx)); // M34: at its real size, not 32 px stretched
     }
 
     private static BitmapSource? Frozen(ShellImage? image)
diff --git a/src/NeoFences.App/OnlineArtWindow.xaml b/src/NeoFences.App/OnlineArtWindow.xaml
new file mode 100644
index 0000000..a4d9dce
--- /dev/null
+++ b/src/NeoFences.App/OnlineArtWindow.xaml
@@ -0,0 +1,17 @@
+<Window x:Class="NeoFences.App.OnlineArtWindow"
+        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
+        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
+        Title="NeoFences" Width="440" SizeToContent="Height" ResizeMode="NoResize"
+        WindowStartupLocation="CenterScreen" ShowInTaskbar="True" ShowActivated="False" ThemeMode="System">
+    <!-- M34 (ADR-055): asked once, the first time a scan leaves games without a cover; the same switch is in Settings. -->
+    <StackPanel Margin="24,18,24,20">
+        <TextBlock Text="Find covers online?" FontSize="16" FontWeight="SemiBold" TextWrapping="Wrap" Margin="0,0,0,8" />
+        <TextBlock x:Name="CountText" TextWrapping="Wrap" Margin="0,0,0,6" />
+        <TextBlock TextWrapping="Wrap" FontSize="12" Foreground="{DynamicResource TextFillColorSecondaryBrush}" Margin="0,0,0,18"
+                   Text="NeoFences sends the names of games without a cover to the Steam store, and asks your web links' sites for their icons. Nothing else leaves your PC. You can change this in Settings → Game Library." />
+        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
+            <Button x:Name="YesButton" Content="Find covers" IsDefault="True" Style="{DynamicResource AccentButtonStyle}" MinWidth="100" Margin="0,0,8,0" />
+            <Button x:Name="NoButton" Content="Not now" IsCancel="True" MinWidth="90" />
+        </StackPanel>
+    </StackPanel>
+</Window>
diff --git a/src/NeoFences.App/OnlineArtWindow.xaml.cs b/src/NeoFences.App/OnlineArtWindow.xaml.cs
new file mode 100644
index 0000000..a1811aa
--- /dev/null
+++ b/src/NeoFences.App/OnlineArtWindow.xaml.cs
@@ -0,0 +1,24 @@
+using System.Windows;
+
+namespace NeoFences.App;
+
+/// <summary>"Find covers online?" (M34, ADR-055): asked once; closing it without an answer asks again at the next scan.</summary>
+public partial class OnlineArtWindow : Window
+{
+    /// <summary>The answer: true to find covers and website icons online, false for no.</summary>
+    public event Action<bool>? Answered;
+
+    public OnlineArtWindow(int gamesWithoutCover)
+    {
+        InitializeComponent();
+        CountText.Text = gamesWithoutCover == 1 ? "1 of your games has no cover on this PC." : $"{gamesWithoutCover} of your games have no cover on this PC.";
+        YesButton.Click += (_, _) => Answer(yes: true);
+        NoButton.Click += (_, _) => Answer(yes: false); // IsCancel closes only a dialog: this window is modeless (M33 lesson)
+    }
+
+    private void Answer(bool yes)
+    {
+        Answered?.Invoke(yes);
+        Close();
+    }
+}
diff --git a/src/NeoFences.App/SettingsWindow.Library.cs b/src/NeoFences.App/SettingsWindow.Library.cs
index 159d2db..d5aa00a 100644
--- a/src/NeoFences.App/SettingsWindow.Library.cs
+++ b/src/NeoFences.App/SettingsWindow.Library.cs
@@ -17,7 +17,7 @@ public sealed record HiddenGame(string Id, string Name, IReadOnlyList<string> Al
 /// <summary>What Settings → Game Library shows (M12; M22: the fences new games can go to, and the chosen one).</summary>
 /// <param name="HasFence">The scan runs (games are wanted somewhere).</param>
 public sealed record LibraryView(bool HasFence, IReadOnlyList<string> Folders, LibrarySources Sources, IReadOnlyList<HiddenGame> Hidden, string Status,
-    IReadOnlyList<(string Id, string Title)> Fences, string? NewGamesFence);
+    IReadOnlyList<(string Id, string Title)> Fences, string? NewGamesFence, bool OnlineArt = false);
 
 /// <summary>
 /// Settings → Game Library (M12, spec §4): game folders, a checkbox per source, hidden games with "Show again", and
@@ -31,6 +31,8 @@ public partial class SettingsWindow
     public event Action? RefreshLibraryRequested;
     /// <summary>"New games go to" (M22): a fence id, or null for nowhere.</summary>
     public event Action<string?>? NewGamesFenceChanged;
+    /// <summary>"Find covers and website icons online" (M34, ADR-055).</summary>
+    public event Action<bool>? OnlineArtChanged;
 
     private IReadOnlyList<string> _libraryFolders = [];
     private IReadOnlyList<(string Id, string Title)> _newGamesChoices = [(Id: "", Title: "\u0000")]; // never equal to a real list: the first show builds it
@@ -62,6 +64,7 @@ public partial class SettingsWindow
         HiddenGameList.SelectionChanged += (_, _) => ShowGameAgainButton.IsEnabled = HiddenGameList.SelectedItem is not null;
         ShowGameAgainButton.Click += (_, _) => { if (HiddenGameList.SelectedItem is HiddenGame game) ShowGameAgainRequested?.Invoke(game.Id); };
         RefreshLibraryButton.Click += (_, _) => RefreshLibraryRequested?.Invoke();
+        OnToggled(OnlineArtBox, isChecked => OnlineArtChanged?.Invoke(isChecked)); // M34
         NewGamesBox.SelectionChanged += (_, _) =>
         {
             if (_updating || NewGamesBox.SelectedItem is not ComboBoxItem chosen) return;
@@ -105,5 +108,6 @@ public partial class SettingsWindow
         }
         NewGamesBox.SelectedItem = NewGamesBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == view.NewGamesFence) ?? NewGamesBox.Items[0];
         RefreshLibraryButton.IsEnabled = view.HasFence;
+        OnlineArtBox.IsChecked = view.OnlineArt; // M34
     }
 }
diff --git a/src/NeoFences.App/SettingsWindow.xaml b/src/NeoFences.App/SettingsWindow.xaml
index b212cae..9c6b33c 100644
--- a/src/NeoFences.App/SettingsWindow.xaml
+++ b/src/NeoFences.App/SettingsWindow.xaml
@@ -267,7 +267,15 @@
             <Border x:Name="LibraryCard" Style="{StaticResource Card}">
                 <StackPanel>
                     <TextBlock x:Name="LibraryDescription" Style="{StaticResource Description}" Margin="0,0,0,8"
-                               Text="Finds the games installed on this PC. Put them in any fence with fence menu → Add games…; newly installed games go to the fence chosen below. Launcher games start through their launcher. Nothing is downloaded, and your games and files are never changed." />
+                               Text="Finds the games installed on this PC. Put them in any fence with fence menu → Add games…; newly installed games go to the fence chosen below. Launcher games start through their launcher. Nothing is downloaded unless you turn on finding covers below, and your games and files are never changed." />
+                    <!-- M34 (ADR-055): online art, off until the owner says yes (asked once). -->
+                    <DockPanel Margin="0,4,0,10">
+                        <CheckBox x:Name="OnlineArtBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Find covers and website icons online" />
+                        <StackPanel>
+                            <TextBlock Text="Find covers and website icons online" TextWrapping="Wrap" />
+                            <TextBlock Style="{StaticResource Description}" Text="Sends the names of games without a cover to the Steam store, and asks your web links' sites for their icons. Nothing else leaves your PC. Off: covers already found stay." />
+                        </StackPanel>
+                    </DockPanel>
                     <StackPanel Orientation="Horizontal" Margin="0,4,0,8">
                         <TextBlock Text="New games go to" VerticalAlignment="Center" Margin="0,0,10,0" />
                         <ComboBox x:Name="NewGamesBox" MinWidth="200" AutomationProperties.Name="New games go to" />
@@ -308,7 +316,7 @@
                         <CheckBox x:Name="AutoUpdateBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Download updates automatically" />
                         <StackPanel>
                             <TextBlock Text="Download updates automatically" TextWrapping="Wrap" />
-                            <TextBlock Style="{StaticResource Description}" Text="Checks GitHub at start and once a day, never during a game. Off: NeoFences makes no network calls." />
+                            <TextBlock Style="{StaticResource Description}" Text="Checks GitHub at start and once a day, never during a game. Off: no update checks." />
                         </StackPanel>
                     </DockPanel>
                     <TextBlock x:Name="UpdateStatus" Style="{StaticResource Description}" Margin="0,10,0,0" AutomationProperties.LiveSetting="Polite" />
diff --git a/src/NeoFences.Shell/NativeMethods.txt b/src/NeoFences.Shell/NativeMethods.txt
index ff8c486..3473792 100644
--- a/src/NeoFences.Shell/NativeMethods.txt
+++ b/src/NeoFences.Shell/NativeMethods.txt
@@ -222,3 +222,5 @@ PdhCloseQuery
 PDH_FMT
 PDH_MORE_DATA
 GetPhysicallyInstalledSystemMemory
+SHGetImageList
+Windows.Win32.UI.Controls.IImageList
diff --git a/src/NeoFences.Shell/OnlineArt.cs b/src/NeoFences.Shell/OnlineArt.cs
new file mode 100644
index 0000000..c044948
--- /dev/null
+++ b/src/NeoFences.Shell/OnlineArt.cs
@@ -0,0 +1,154 @@
+using System.Net.Http;
+using System.Net.Http.Headers;
+using System.Text.Json;
+using NeoFences.Core.Library;
+
+namespace NeoFences.Shell;
+
+/// <summary>
+/// Online art (M34, ADR-055), only when the user said yes: game covers from the Steam store's public search and asset
+/// service (no account, no key), website icons from the sites themselves. Only names and addresses are sent; replies are
+/// size-capped and must be images. Every failure is null (the caller keeps the glow tile or the letter badge).
+/// </summary>
+public static class OnlineArt
+{
+    private const long MaxImageBytes = 5 * 1024 * 1024;
+    private const long MaxPageBytes = 1024 * 1024;
+    private const string AssetHost = "https://shared.akamai.steamstatic.com/store_item_assets/";
+
+    private static readonly HttpClient Http = CreateClient();
+
+    private static HttpClient CreateClient()
+    {
+        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = MaxImageBytes };
+        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NeoFences", typeof(OnlineArt).Assembly.GetName().Version?.ToString(3) ?? "0"));
+        return client;
+    }
+
+    /// <summary>A Steam store result: the app id and its store name.</summary>
+    public sealed record StoreGame(int AppId, string Name);
+
+    /// <summary>The Steam store's search for a game's name (at most 10 results; the term is <see cref="GameArt.SearchTerm"/>).</summary>
+    public static async Task<IReadOnlyList<StoreGame>?> SearchAsync(string name, CancellationToken cancel = default)
+    {
+        try
+        {
+            var url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(GameArt.SearchTerm(name))}&l=english&cc=US";
+            using var reply = JsonDocument.Parse(await Http.GetStringAsync(url, cancel));
+            if (!reply.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return [];
+            return [.. items.EnumerateArray()
+                .Where(item => item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && item.TryGetProperty("name", out _))
+                .Select(item => new StoreGame(item.GetProperty("id").GetInt32(), item.GetProperty("name").GetString() ?? ""))];
+        }
+        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
+        {
+            return null; // offline or Steam unreachable: nothing recorded, tried again at the next scan
+        }
+    }
+
+    /// <summary>
+    /// The 2:3 library covers of these apps: the asset service names each app's current file (new games keep it under a
+    /// hashed folder; the old fixed address gives 404 for them).
+    /// </summary>
+    public static async Task<IReadOnlyDictionary<int, Uri>> CoverUrlsAsync(IReadOnlyList<int> appIds, CancellationToken cancel = default)
+    {
+        var covers = new Dictionary<int, Uri>();
+        if (appIds.Count == 0) return covers;
+        try
+        {
+            var request = JsonSerializer.Serialize(new
+            {
+                ids = appIds.Select(appId => new { appid = appId }),
+                context = new { language = "english", country_code = "US" },
+                data_request = new { include_assets = true },
+            });
+            var url = $"https://api.steampowered.com/IStoreBrowseService/GetItems/v1?input_json={Uri.EscapeDataString(request)}";
+            using var reply = JsonDocument.Parse(await Http.GetStringAsync(url, cancel));
+            if (!reply.RootElement.TryGetProperty("response", out var response) || !response.TryGetProperty("store_items", out var items)) return covers;
+            foreach (var item in items.EnumerateArray())
+            {
+                if (!item.TryGetProperty("appid", out var appId) || !item.TryGetProperty("assets", out var assets)) continue;
+                var format = assets.TryGetProperty("asset_url_format", out var formatValue) ? formatValue.GetString() : null;
+                var file = (assets.TryGetProperty("library_capsule_2x", out var big) ? big.GetString() : null) is { Length: > 0 } large ? large
+                    : assets.TryGetProperty("library_capsule", out var small) ? small.GetString() : null;
+                if (format is null || string.IsNullOrEmpty(file) || !format.Contains("${FILENAME}", StringComparison.Ordinal)) continue;
+                if (Uri.TryCreate(AssetHost + format.Replace("${FILENAME}", file, StringComparison.Ordinal), UriKind.Absolute, out var cover)) covers[appId.GetInt32()] = cover;
+            }
+        }
+        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or FormatException)
+        {
+            // none: the caller records nothing and tries again later
+        }
+        return covers;
+    }
+
+    /// <summary>An image's bytes (≤ 5 MB, an image content type); null on any failure.</summary>
+    public static async Task<byte[]?> GetImageAsync(Uri url, CancellationToken cancel = default)
+    {
+        try
+        {
+            using var reply = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
+            if (!reply.IsSuccessStatusCode || reply.Content.Headers.ContentLength > MaxImageBytes) return null;
+            var type = reply.Content.Headers.ContentType?.MediaType ?? "";
+            if (!type.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && !type.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)) return null;
+            var bytes = await reply.Content.ReadAsByteArrayAsync(cancel);
+            return bytes.Length == 0 || bytes.Length > MaxImageBytes ? null : bytes;
+        }
+        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
+        {
+            return null;
+        }
+    }
+
+    /// <summary>Downloads an image (≤ 5 MB, an image content type) to <paramref name="path"/>; false on any failure.</summary>
+    public static async Task<bool> DownloadImageAsync(Uri url, string path, CancellationToken cancel = default)
+    {
+        try
+        {
+            if (await GetImageAsync(url, cancel) is not { } bytes) return false;
+            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
+            var temp = path + ".tmp";
+            await File.WriteAllBytesAsync(temp, bytes, cancel);
+            File.Move(temp, path, overwrite: true);
+            return true;
+        }
+        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidOperationException)
+        {
+            return false;
+        }
+    }
+
+    /// <summary>
+    /// A website's own icon into <paramref name="folder"/>: the page's declared icon (the largest), else <c>/favicon.ico</c>.
+    /// The file name, or null when the site gave none.
+    /// </summary>
+    public static async Task<string?> FetchSiteIconAsync(Uri page, string folder, CancellationToken cancel = default)
+    {
+        Uri? declared = null;
+        try
+        {
+            using var reply = await Http.GetAsync(page, HttpCompletionOption.ResponseHeadersRead, cancel);
+            if (reply.IsSuccessStatusCode && (reply.Content.Headers.ContentType?.MediaType ?? "").Contains("html", StringComparison.OrdinalIgnoreCase))
+            {
+                await using var stream = await reply.Content.ReadAsStreamAsync(cancel);
+                var buffer = new byte[MaxPageBytes];
+                var read = 0;
+                int chunk;
+                while (read < buffer.Length && (chunk = await stream.ReadAsync(buffer.AsMemory(read), cancel)) > 0) read += chunk;
+                declared = SiteIcons.DeclaredIcon(System.Text.Encoding.UTF8.GetString(buffer, 0, read), reply.RequestMessage?.RequestUri ?? page);
+            }
+        }
+        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
+        {
+            // the page could not be read: the favicon may still be there
+        }
+        foreach (var icon in declared is null ? [SiteIcons.FallbackIcon(page)] : new[] { declared, SiteIcons.FallbackIcon(page) })
+        {
+            if (icon.Scheme is not ("http" or "https")) continue;
+            var extension = Path.GetExtension(icon.AbsolutePath).ToLowerInvariant() is { Length: > 1 and <= 5 } known && known != ".svg" ? known : ".png";
+            var file = $"{page.Host.ToLowerInvariant()}{extension}";
+            if (await DownloadImageAsync(icon, Path.Combine(folder, file), cancel)) return file;
+        }
+        return null;
+    }
+}
diff --git a/src/NeoFences.Shell/ShellItems.cs b/src/NeoFences.Shell/ShellItems.cs
index 513a972..ba433ee 100644
--- a/src/NeoFences.Shell/ShellItems.cs
+++ b/src/NeoFences.Shell/ShellItems.cs
@@ -215,21 +215,22 @@ public static class ShellItems
     /// Windows' icon for this kind of item, by its name only (M19 R8): a folder icon, or the icon of its file type ("a .txt
     /// file"). Never touches the disk, so a missing or unreachable target still gets one. Null when Windows has none.
     /// </summary>
-    public static unsafe ShellImage? TryGetGenericImage(string target, bool isFolder)
+    /// <param name="sizePx">M34: above 32 px the system image list's extra-large (48) or jumbo (256) icon, not the 32 px one stretched.</param>
+    public static unsafe ShellImage? TryGetGenericImage(string target, bool isFolder, int sizePx = 32)
     {
         var info = new SHFILEINFOW();
         ICONINFO iconInfo = default;
         try
         {
-            // ponytail: the 32 px "large" icon, scaled by WPF for bigger sizes; the system image list's jumbo icons if it looks soft.
             var attributes = isFolder ? FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_DIRECTORY : FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL;
-            var flags = SHGFI_FLAGS.SHGFI_ICON | SHGFI_FLAGS.SHGFI_LARGEICON | SHGFI_FLAGS.SHGFI_USEFILEATTRIBUTES;
+            var flags = SHGFI_FLAGS.SHGFI_ICON | SHGFI_FLAGS.SHGFI_LARGEICON | SHGFI_FLAGS.SHGFI_USEFILEATTRIBUTES | SHGFI_FLAGS.SHGFI_SYSICONINDEX;
             nuint found;
             fixed (char* name = target)
             {
                 found = PInvoke.SHGetFileInfo(name, attributes, &info, (uint)sizeof(SHFILEINFOW), flags);
             }
             if (found == 0 || info.hIcon.IsNull) return null;
+            if (sizePx > 32 && TryGetSystemImage(info.iIcon, sizePx) is { } large) return large;
             if (!PInvoke.GetIconInfo(info.hIcon, &iconInfo)) return null;
             return ReadPixels(iconInfo.hbmColor, premultiply: true);
         }
@@ -245,6 +246,36 @@ public static class ShellItems
         }
     }
 
+    /// <summary>The system image list's icon at index <paramref name="iconIndex"/>: extra-large (48 px) up to 48, else jumbo (256 px).</summary>
+    private static unsafe ShellImage? TryGetSystemImage(int iconIndex, int sizePx)
+    {
+        const int ExtraLarge = 2, Jumbo = 4; // SHIL_EXTRALARGE, SHIL_JUMBO (#defines CsWin32 does not carry)
+        ICONINFO iconInfo = default;
+        HICON icon = default;
+        try
+        {
+            var listId = typeof(Windows.Win32.UI.Controls.IImageList).GUID;
+            PInvoke.SHGetImageList(sizePx <= 48 ? ExtraLarge : Jumbo, &listId, out var listPointer).ThrowOnFailure();
+            var list = (Windows.Win32.UI.Controls.IImageList)Marshal.GetObjectForIUnknown((nint)listPointer);
+            Marshal.Release((nint)listPointer);
+            HICON listed;
+            list.GetIcon(iconIndex, 1 /* ILD_TRANSPARENT */, &listed);
+            icon = listed;
+            if (icon.IsNull || !PInvoke.GetIconInfo(icon, &iconInfo)) return null;
+            return ReadPixels(iconInfo.hbmColor, premultiply: true);
+        }
+        catch (Exception failure) when (failure is not OutOfMemoryException)
+        {
+            return null; // the 32 px icon instead
+        }
+        finally
+        {
+            if (!iconInfo.hbmColor.IsNull) PInvoke.DeleteObject(iconInfo.hbmColor);
+            if (!iconInfo.hbmMask.IsNull) PInvoke.DeleteObject(iconInfo.hbmMask);
+            if (!icon.IsNull) PInvoke.DestroyIcon(icon);
+        }
+    }
+
     private static IShellItem Create(string itemRef)
     {
         PInvoke.SHCreateItemFromParsingName(itemRef, null, out IShellItem item).ThrowOnFailure();
diff --git a/src/NeoFences.Shell/ShellLinks.cs b/src/NeoFences.Shell/ShellLinks.cs
index 449fc3a..e32c683 100644
--- a/src/NeoFences.Shell/ShellLinks.cs
+++ b/src/NeoFences.Shell/ShellLinks.cs
@@ -77,6 +77,17 @@ public static class ShellLinks
         }
     }
 
+    /// <summary>
+    /// A shortcut that starts a Store app through Explorer (<c>explorer.exe shell:AppsFolder&lt;id&gt;</c>, like Minecraft
+    /// Launcher's, M34): the app's <c>shell:AppsFolder&lt;id&gt;</c>, whose icon is the app's own (the shortcut shows Explorer's).
+    /// Null for any other file. STA thread.
+    /// </summary>
+    public static string? AppsFolderOf(string path) =>
+        Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) && Read(path) is { } launch
+        && Path.GetFileName(launch.Target).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)
+        && launch.Arguments?.Trim() is { } arguments && arguments.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase)
+            ? arguments : null;
+
     /// <summary>
     /// A .url file's URL= line, reading at most the first 64 KB (a huge or binary file named .url never fills memory,
     /// also without line breaks; M13c). Null when there is none.
diff --git a/src/NeoFences.Shell/Watchdog.cs b/src/NeoFences.Shell/Watchdog.cs
index 427e157..bce93aa 100644
--- a/src/NeoFences.Shell/Watchdog.cs
+++ b/src/NeoFences.Shell/Watchdog.cs
@@ -212,6 +212,33 @@ public sealed class Watchdog(string dataDirectory, Action<string> log)
         }
     }
 
+    /// <summary>At start (M34): the <c>watchdog-&lt;pid&gt;</c> files of processes that ended without cleaning up (a power cut) go.</summary>
+    public void RemoveStaleFiles()
+    {
+        try
+        {
+            var names = Directory.Exists(dataDirectory) ? Directory.EnumerateFiles(dataDirectory, "watchdog-*").Select(Path.GetFileName).OfType<string>().ToList() : [];
+            foreach (var stale in WatchdogFiles.Stale(names, isRunning: IsRunning)) TryDelete(Path.Combine(dataDirectory, stale));
+        }
+        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
+        {
+            log($"could not list the data folder: {failure.Message}");
+        }
+    }
+
+    private static bool IsRunning(int processId)
+    {
+        try
+        {
+            using var process = Process.GetProcessById(processId);
+            return !process.HasExited;
+        }
+        catch (Exception failure) when (failure is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
+        {
+            return false;
+        }
+    }
+
     private void TryDelete(string path)
     {
         try
```

- [ ] **Step 2: Build and test.** `dotnet build` → `0 Warning(s)`; `dotnet test` → `Passed: 747`.

- [ ] **Step 3: Commit.** `git add -A && git commit -m "feat: added modern game tiles, the glow tile, clean icons, cover sizes, online covers and website icons, and Choose cover"`

### Task 3: Docs

**Files:**
- Modify: `docs/DECISIONS.md` (ADR-055 opt-in online art), `docs/ARCHITECTURE.md` (0.21.0 paragraph), `docs/GUIDE.md` (§3 Games:
  covers, sizes Normal / Large, Choose cover…, the glow tile, online covers and the question; §2 items: website icons and the
  letter badge; §12 Settings: the switch; §11 Updates wording; §13 "Your files are safe": what online art sends), `README.md`
  (if it says NeoFences makes no network calls or downloads nothing), `docs/FEATURES.md` (rows), `docs/TEST-CHECKLIST.md`
  (section AT)
- Create: `docs/research/m34-icons-and-game-tiles.md`

- [ ] **Step 1: Write.** AT: AT1 Apps fence sharp at 100 % and 150 % (no slab, faint hover, accent selection); AT2 covers rounded
  with a shadow, name below, Normal covers' rows without a gap; AT3 Size ▸ Large is twice Normal, an old 2×2 shows Large; AT4 glow
  tiles (AC Black Flag, Blur) — colours, the icon in the middle; AT5 the accent ring on a selected tile, the accent box on an icon,
  a folder panel row in the accent; AT6 labels on hover: a cover's name fades in over its bottom, no pop-under name; AT7 first scan
  with games without a cover → "Find covers online?"; Not now → asked again at the next scan; Find covers → covers within ~15 s
  (log "online art: N cover(s) found"); AT8 Settings switch off keeps the found covers, on looks up again; AT9 Choose cover…:
  Steam results, one picked shows; From a file… shows (the file unchanged); Reset to automatic; AT10 a web link: the site's icon
  with online art on; the letter badge off or for a dead site; AT11 Minecraft Launcher shows its own icon; AT12 a missing item's
  icon at its real size with the badge; AT13 light mode; AT14 a stale `watchdog-<pid>` file is gone after start; AT15 safe mode:
  no question, no lookups.
- [ ] **Step 2: Check** the UI strings named against the code; secret scan of `git diff main`.
- [ ] **Step 3: Commit.** `git add -A && git commit -m "docs: described icons and game tiles in ADR-055, architecture, guide, features, checklist AT and the M34 note"`

### Task 4: Final review and fix pass

- [ ] Whole-branch review on the most capable model (Review Focus above); findings re-graded by effect; Critical/Important
  fixed in one pass, each with a failing test first where Core-testable.

### Task 5: Live check (asked first)

- [ ] On a backed-up copy of the owner's data (`m34-switch.ps1`, branch Release build): AT1–AT15 as far as scriptable
  (screenshots sent as they are taken), the rest by reading the logs; keys only after a click on bare desktop (ADR-015);
  restore the data and the Run value; start the installed copy; refocus Terminal; results into the research note; commit
  `docs: added the M34 live check results`.

### Task 6: Dogfood (after the merge, asked first)

- [ ] A snapshot of the owner's setup first; answer the covers question with the owner (or set online art on if they say
  so); give the Games fence room for its two Large covers; screenshots to the owner; ask before switching its labels to
  "on hover".
