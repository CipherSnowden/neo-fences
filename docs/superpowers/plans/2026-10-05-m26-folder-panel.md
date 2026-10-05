# M26 — The folder panel element (0.15.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Any fence can show a folder as a panel element — Details, List or Icons, browsing into subfolders, 1–4 × 1–4 cells or filling the fence — and folder-view fences become fences holding one filling panel.

**Architecture:** Core adds `FolderPanels` (the `FolderPanel` settings on a folder item, selection with Size and direction, header sort, columns, Fill rule, header text, size text, the view migration) and `PanelPlace` (browsing). Shell reports file sizes. The App adds a `FolderPanelView` control in the item template, a `FolderPanelModel` per panel, and `FenceHost.FolderPanels.cs` (one lister per panel, commands, menus, migration), replacing the fence-level folder-view code.

**Tech Stack:** .NET 10, WPF (`ListView` + `GridView`), CsWin32 0.3.335 (no new bindings), xUnit, Serilog, Velopack (unchanged).

**Spec:** `docs/superpowers/specs/2026-10-05-folder-panel-design.md` (approved 2026-10-05). Decision: ADR-048 (added by Task 3). Build notes: `docs/research/m26-folder-panel.md` (added by Task 3).

## How this plan is written

Every code change was built in a scratch prototype first (2026-10-05, worktree `neo_fences-m26proto`, branch
`m26-proto`) and probed live on a copy of the user's data: the Downloads folder view became a filling Icons panel (a
snapshot first; 14 entries, no second title); a 4 × 3 Details panel of `D:\GameLibrary` sorted by a header click, browsed
into `Blur` ("GameLibrary › Blur") and back; Look ▸ Details from the panel menu; NeoFences used 0–0.01 % of the machine
over 30 s. Each task's patch was replayed on a fresh worktree of `main` at `ff11ff7`: the Core tests failed to compile
before the Core patch (`CS0246: 'PanelSort' could not be found`) and passed after it (601), the solution built with 0
warnings after the App patch, and the replayed tree is identical to the prototype. So each code step is **a patch to
apply**:

1. Write the patch block **exactly** as shown to a file in the scratchpad (use the Write tool, never a shell heredoc).
   The file ends with one newline.
2. `git apply --whitespace=nowarn <file>` from the worktree root. If it does not apply, stop (rule on it, never
   hand-merge silently).

## Where the build departs from the spec (decided while prototyping; the final review weighs them)

- "Fill fence" is stored and works while the panel is alone: beside another element it sits on cells and fills again when
  alone again; a size pick turns it off (spec: adding anything "turns it back to cells").
- A "Before folder views became panels" snapshot precedes the migration (as M22); the migration is skipped (logged) while
  items.json is read-only.
- A filling panel at its own folder under the fence's own title hides its header row (no second title).
- "Show as folder panel" resets size and Fill; "Show as icon" clears the panel settings.
- Add folder panel… in a Free fence takes the first free 4 × 4 spot (rows grow; never shrinks).
- The fence menu's "New folder view…" became "New folder panel…" beside "Add folder panel…".
- Panel settings is the folder view dialog without its sort; the panel menu has no Properties….
- The Type column shows the extension (as Sort by does).
- The panel's name area belongs to the fence (select, drag, menu); rows, buttons and lines are the panel's.
- The fence's hover and selection never tint a whole panel (its rows show their own).

## Global Constraints

- Hard rule 1: **NeoFences never modifies a user file.** Panels are read-only: drops over a panel are refused; no rename
  or delete from NeoFences' own menu (Shift+right-click: Windows' menu under its warning line).
- Hard rules 2–7: Win32 only in NeoFences.Shell via **CsWin32**; **no new NuGet dependency**; failures degrade one panel
  ("Folder not available", a log line), never crash.
- A panel is a folder item: `VirtualItem.Panel` (`FolderPanel { Look, Show, Patterns, Newest, Sort, Descending }`) and
  `VirtualItem.Fill`. Default span **4 × 4**. items.json stays **schema 1** (`panel` / `fill` only when set).
- UI copy, exactly: fence menu "Add folder panel…", "New folder panel…"; tray "New folder panel…"; item menu "Show as
  folder panel"; panel menu "Look" ▸ "Details" / "List" / "Icons", "Sort by" ▸ "Name" / "Date modified" / "Type" /
  "Size", "Panel settings…", "Open folder", "Size", "Fill fence", "Show as icon", "Remove from fence"; columns "Name",
  "Date modified", "Type", "Size"; statuses as M21.
- At most **500** entries shown; listing, filtering and sorting off the UI thread; listers paused in game mode.
- Commits: single line, Conventional Commits, past tense, **no Co-Authored-By trailer**.
- Version stays `0.14.0` until the release step; the release is 0.15.0.

## Review Focus

1. **Input routing between the fence and a panel**: clicks, double-clicks, keys (Enter, Delete, F2, arrows), wheel,
   right-clicks and drags inside a panel never act on the fence's own items, and the panel's name area still selects,
   drags and opens the panel menu like any element.
2. **Migration safety**: a crash between the two saves, a read-only file, a restore of an older snapshot, two view
   fences, a view whose folder is gone — no view lost, no second panel.
3. **Listers**: a panel removed, retargeted (rename, Locate…, settings), browsed, on a hidden tab, on a pendrive being
   removed, in game mode — no lister leaks, none on a stale folder, Safely Remove works.
4. **Fill**: a lone filling panel through resizes, roll-up, tabs, icon size and label changes, Free layout, items added
   and removed — never clipped, never a fence scrollbar fighting the panel's.
5. **Performance**: a 20,000-entry folder (cap 500, off-thread sort, virtualized rows, icons only for visible rows), no
   work while unseen beyond the watcher.

---

### Task 0: Worktree and baseline

- [ ] `git worktree add -b m26-folder-panel ..\neo_fences-m26 main` (main at `ff11ff7` or later docs-only commits).
- [ ] `dotnet build` → 0 warnings; `dotnet test` → 559 passed.

### Task 1: Core — folder panels

**Files:**
- Create: `src/NeoFences.Core/Items/FolderPanels.cs`, `tests/NeoFences.Core.Tests/Items/FolderPanelsTests.cs`
- Modify: `src/NeoFences.Core/Items/VirtualItem.cs` (`Panel`, `Fill`), `GridLayout.cs` (panel span), `ItemsDocument.cs`
  (repair, `SetSize` clears Fill), `FolderViews.cs` (`Selection.Entries`, `Select` maps to panels),
  `src/NeoFences.Core/Model/ItemSorting.cs` (`ItemInfo.Size`, `NaturalComparer` internal),
  `src/NeoFences.Core/Config/ConfigJson.cs` (lenient `PanelLook` / `PanelSort`)

**Interfaces:**
- Produces: `enum PanelLook { Details, List, Icons }`; `enum PanelSort { Name, Date, Type, Size }`;
  `record FolderPanel { Look; Show; Patterns; Newest; Sort; Descending }`; `VirtualItem.Panel`, `VirtualItem.Fill`;
  `FolderPanels.DefaultSpan`, `IsPanel(VirtualItem)`, `Create(string folder, IReadOnlyList<string> busyFolders) →
  VirtualItem`, `FromView(FolderView)`, `Normalize(FolderPanel?)`, `Select(IReadOnlyList<ItemInfo>, FolderPanel) →
  FolderViews.Selection`, `HeaderSort(FolderPanel, PanelSort)`, `HeaderOf(string home, string shown, string? ownName)`,
  `SizeText(long?, CultureInfo)`, `Columns(int)`, `Fills(IReadOnlyList<VirtualItem>)`, `MigrateViews(config, items) →
  Migration(Config, Items, MigratedFenceIds)`; `record PanelPlace(string Current, IReadOnlyList<string> History)` with
  `At`, `CanGoBack`, `CanGoUp(home)`, `Into`, `Back`, `Up(home)`, `Home(home)`, `IsBelow(folder, home)`;
  `FolderViews.Selection(IReadOnlyList<ItemInfo> Entries, int Hidden, int Listed)` (+ `Shown` paths);
  `ItemInfo(…, long? Size = null)`.

- [ ] **Step 1: Write the failing tests.** Write this patch to `m26-1-core-tests.patch` and `git apply --whitespace=nowarn` it:

````diff
diff --git a/tests/NeoFences.Core.Tests/Items/FolderPanelsTests.cs b/tests/NeoFences.Core.Tests/Items/FolderPanelsTests.cs
new file mode 100644
index 0000000..4eb4638
--- /dev/null
+++ b/tests/NeoFences.Core.Tests/Items/FolderPanelsTests.cs
@@ -0,0 +1,258 @@
+using NeoFences.Core.Config;
+using NeoFences.Core.Items;
+using NeoFences.Core.Model;
+
+namespace NeoFences.Core.Tests.Items;
+
+/// <summary>M26 (0.15.0): the folder panel element — a folder shown inside any fence (spec 2026-10-05-folder-panel-design).</summary>
+public class FolderPanelsTests
+{
+    private static readonly DateTimeOffset Day = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
+
+    private static ItemInfo File(string name, int minutesAgo = 0, long size = 0) =>
+        new($@"D:\Panel\{name}", name, IsFolder: false, TypeName: Path.GetExtension(name).ToUpperInvariant(), Day.AddMinutes(-minutesAgo), size);
+
+    private static ItemInfo Folder(string name, int minutesAgo = 0) => new($@"D:\Panel\{name}", name, IsFolder: true, TypeName: "", Day.AddMinutes(-minutesAgo));
+
+    private static IReadOnlyList<string> Names(FolderViews.Selection selection) => [.. selection.Entries.Select(entry => entry.Name)];
+
+    private static readonly IReadOnlyList<ItemInfo> Mixed =
+    [
+        File("b.png", 5, size: 300), File("a.jpg", 1, size: 2000), File("notes10.txt", 30, size: 10), File("notes2.txt", 20, size: 10),
+        Folder("Saves", 2), Folder("Mods", 40),
+    ];
+
+    [Fact]
+    public void Panels_AreFolderItems_WithTheirOwnDefaultSpan()
+    {
+        var panel = VirtualItem.Create(@"D:\Panel") with { Panel = new FolderPanel() };
+        Assert.Equal(ItemKind.Path, panel.Kind); // a path item: missing state, Locate… and the bulk fix work as for any item
+        Assert.True(FolderPanels.IsPanel(panel));
+        Assert.Equal(new GridSpan(4, 4), FenceGrid.SpanOf(panel));
+        Assert.Equal(new GridSpan(2, 3), FenceGrid.SpanOf(panel with { Size = new GridSpan(2, 3) }));
+        Assert.False(FolderPanels.IsPanel(VirtualItem.Create(@"D:\Panel")));
+        Assert.False(FolderPanels.IsPanel(VirtualItem.Create("https://example.com") with { Panel = new FolderPanel() })); // only a path can be a panel
+    }
+
+    [Fact]
+    public void Create_BusyFolders_StartNewestFirst_OthersByName()
+    {
+        var downloads = FolderPanels.Create(@"C:\Users\me\Downloads", [@"C:\Users\me\Downloads\"]);
+        Assert.Equal(@"C:\Users\me\Downloads", downloads.Target);
+        Assert.Equal(new FolderPanel { Sort = PanelSort.Date, Descending = true, Newest = FolderViews.BusyFolderNewest }, downloads.Panel);
+        Assert.Equal(new FolderPanel(), FolderPanels.Create(@"D:\GameLibrary", [@"C:\Users\me\Downloads"]).Panel);
+        Assert.Equal(PanelLook.Details, new FolderPanel().Look);
+    }
+
+    [Fact]
+    public void Select_ByName_PutsFoldersFirst_BothWays()
+    {
+        Assert.Equal(["Mods", "Saves", "a.jpg", "b.png", "notes2.txt", "notes10.txt"], Names(FolderPanels.Select(Mixed, new FolderPanel())));
+        Assert.Equal(["Saves", "Mods", "notes10.txt", "notes2.txt", "b.png", "a.jpg"], Names(FolderPanels.Select(Mixed, new FolderPanel { Descending = true })));
+    }
+
+    [Fact]
+    public void Select_ByDate_AndBySize_MixFolders_TiesByName()
+    {
+        Assert.Equal(["a.jpg", "Saves", "b.png", "notes2.txt", "notes10.txt", "Mods"],
+            Names(FolderPanels.Select(Mixed, new FolderPanel { Sort = PanelSort.Date, Descending = true })));
+        Assert.Equal(["Mods", "notes10.txt", "notes2.txt", "b.png", "Saves", "a.jpg"],
+            Names(FolderPanels.Select(Mixed, new FolderPanel { Sort = PanelSort.Date })));
+        // Biggest first; equal sizes by name; folders (no size) last.
+        Assert.Equal(["a.jpg", "b.png", "notes2.txt", "notes10.txt", "Mods", "Saves"],
+            Names(FolderPanels.Select(Mixed, new FolderPanel { Sort = PanelSort.Size, Descending = true })));
+    }
+
+    [Fact]
+    public void Select_ByType_KeepsFoldersFirst()
+    {
+        Assert.Equal(["Mods", "Saves", "a.jpg", "b.png", "notes2.txt", "notes10.txt"], Names(FolderPanels.Select(Mixed, new FolderPanel { Sort = PanelSort.Type })));
+        Assert.Equal(["Mods", "Saves", "notes2.txt", "notes10.txt", "b.png", "a.jpg"], // types reversed, ties still A→Z
+            Names(FolderPanels.Select(Mixed, new FolderPanel { Sort = PanelSort.Type, Descending = true })));
+    }
+
+    [Fact]
+    public void Select_FiltersLikeFolderViews_AndCaps()
+    {
+        Assert.Equal(["Mods", "Saves", "a.jpg", "b.png"], Names(FolderPanels.Select(Mixed, new FolderPanel { Patterns = "png; .JPG" })));
+        Assert.Equal(["a.jpg", "b.png"], Names(FolderPanels.Select(Mixed, new FolderPanel { Show = ViewShow.Files, Newest = 2 })));
+        var many = Enumerable.Range(0, FolderViews.MaxShown + 7).Select(index => File($"f{index}.txt")).ToList();
+        var capped = FolderPanels.Select(many, new FolderPanel());
+        Assert.Equal(FolderViews.MaxShown, capped.Entries.Count);
+        Assert.Equal(7, capped.Hidden);
+    }
+
+    [Theory]
+    [InlineData(PanelSort.Name, false, PanelSort.Name, PanelSort.Name, true)] // the same column again: reversed
+    [InlineData(PanelSort.Name, true, PanelSort.Name, PanelSort.Name, false)]
+    [InlineData(PanelSort.Name, false, PanelSort.Date, PanelSort.Date, true)] // a new column: its natural direction
+    [InlineData(PanelSort.Date, true, PanelSort.Size, PanelSort.Size, true)]
+    [InlineData(PanelSort.Size, true, PanelSort.Type, PanelSort.Type, false)]
+    public void HeaderSort_NewColumnNatural_SameColumnReversed(PanelSort sort, bool descending, PanelSort clicked, PanelSort expectedSort, bool expectedDescending)
+    {
+        var sorted = FolderPanels.HeaderSort(new FolderPanel { Sort = sort, Descending = descending, Newest = 5 }, clicked);
+        Assert.Equal(expectedSort, sorted.Sort);
+        Assert.Equal(expectedDescending, sorted.Descending);
+        Assert.Equal(5, sorted.Newest); // nothing else changes
+    }
+
+    [Theory]
+    [InlineData(1, 2)]
+    [InlineData(2, 2)]
+    [InlineData(3, 4)]
+    [InlineData(4, 4)]
+    public void Columns_NarrowPanelsShowNameAndDate(int spanColumns, int expected) => Assert.Equal(expected, FolderPanels.Columns(spanColumns));
+
+    [Fact]
+    public void Normalize_RepairsHandEdits()
+    {
+        Assert.Null(FolderPanels.Normalize(null));
+        var repaired = FolderPanels.Normalize(new FolderPanel { Look = (PanelLook)9, Sort = (PanelSort)9, Show = (ViewShow)9, Newest = 0, Patterns = @"C:\x" })!;
+        Assert.Equal(new FolderPanel(), repaired);
+        Assert.Equal(FolderViews.MaxNewest, FolderPanels.Normalize(new FolderPanel { Newest = 100_000 })!.Newest);
+    }
+
+    [Fact]
+    public void Fills_OnlyALonePanelThatAsks()
+    {
+        var filling = VirtualItem.Create(@"D:\Panel") with { Panel = new FolderPanel(), Fill = true };
+        Assert.True(FolderPanels.Fills([filling]));
+        Assert.False(FolderPanels.Fills([filling, VirtualItem.Create(@"D:\other.txt")])); // not alone: cells (its own size or 4×4)
+        Assert.False(FolderPanels.Fills([filling with { Panel = null }])); // a plain folder item never fills
+        Assert.False(FolderPanels.Fills([filling with { Fill = false }]));
+        Assert.False(FolderPanels.Fills([]));
+    }
+
+    [Fact]
+    public void SetSize_PutsAFillingPanelBackOnCells()
+    {
+        var filling = VirtualItem.Create(@"D:\Panel") with { Panel = new FolderPanel(), Fill = true };
+        var items = new ItemsDocument().With("f", [filling]);
+        var sized = ItemEdits.SetSize(items, [filling.Id], new GridSpan(3, 2)).Of("f")[0];
+        Assert.False(sized.Fill);
+        Assert.Equal(new GridSpan(3, 2), sized.Size);
+    }
+
+    [Fact]
+    public void FromView_KeepsWhatTheViewShowed()
+    {
+        var panel = FolderPanels.FromView(new FolderView { Path = @"D:\Shots", Show = ViewShow.Files, Patterns = "png", Newest = 30, Sort = FenceSort.Date });
+        Assert.Equal(new FolderPanel { Look = PanelLook.Icons, Show = ViewShow.Files, Patterns = "png", Newest = 30, Sort = PanelSort.Date, Descending = true }, panel);
+        Assert.Equal(PanelSort.Type, FolderPanels.FromView(new FolderView { Path = @"D:\Shots", Sort = FenceSort.Type }).Sort);
+        Assert.Equal(PanelSort.Name, FolderPanels.FromView(new FolderView { Path = @"D:\Shots", Sort = FenceSort.Manual }).Sort);
+    }
+
+    [Fact]
+    public void MigrateViews_EachViewBecomesOneFillingPanel_ThenTheViewGoes()
+    {
+        var view = Fence.Create("Downloads") with { View = new FolderView { Path = @"C:\Users\me\Downloads", Sort = FenceSort.Date, Newest = 30 }, IconSize = 64 };
+        var plain = Fence.Create("Apps");
+        var config = NeoFencesConfig.CreateDefault() with { Fences = [view, plain] };
+        var items = new ItemsDocument().With(plain.Id, [VirtualItem.Create(@"C:\a.lnk")]);
+
+        var migrated = FolderPanels.MigrateViews(config, items);
+
+        Assert.Equal([view.Id], migrated.MigratedFenceIds);
+        var fence = migrated.Config.Fences.Single(candidate => candidate.Id == view.Id);
+        Assert.Null(fence.View);
+        Assert.Equal(FenceKind.Items, fence.Kind);
+        Assert.Equal(("Downloads", 64), (fence.Title, fence.IconSize)); // its look stays
+        var panel = Assert.Single(migrated.Items.Of(view.Id));
+        Assert.Equal(@"C:\Users\me\Downloads", panel.Target);
+        Assert.True(panel.Fill);
+        Assert.Equal(new FolderPanel { Look = PanelLook.Icons, Sort = PanelSort.Date, Descending = true, Newest = 30 }, panel.Panel);
+        Assert.Equal(items.Of(plain.Id), migrated.Items.Of(plain.Id));
+    }
+
+    [Fact]
+    public void MigrateViews_RunAgainAfterACutShortSave_AddsNoSecondPanel()
+    {
+        var view = Fence.Create("Downloads") with { View = new FolderView { Path = @"C:\Users\me\Downloads" } };
+        var config = NeoFencesConfig.CreateDefault() with { Fences = [view] };
+        var once = FolderPanels.MigrateViews(config, new ItemsDocument());
+        // The items were saved, the config was not: the next start sees the view again with its panel already there.
+        var again = FolderPanels.MigrateViews(config, once.Items);
+        Assert.Single(again.Items.Of(view.Id));
+        Assert.Null(again.Config.Fences.Single().View);
+        Assert.Empty(FolderPanels.MigrateViews(again.Config, again.Items).MigratedFenceIds); // nothing left to do
+    }
+
+    [Fact]
+    public void Json_WritesPanelAndFill_OnlyWhenSet_AndReadsThemBack()
+    {
+        const string fenceId = "f";
+        var plain = ConfigJson.SerializeItems(new ItemsDocument().With(fenceId, [VirtualItem.Create(@"D:\x")]));
+        Assert.DoesNotContain("panel", plain);
+        Assert.DoesNotContain("fill", plain);
+        var panel = VirtualItem.Create(@"D:\x") with { Panel = new FolderPanel { Look = PanelLook.List, Sort = PanelSort.Size, Descending = true }, Fill = true };
+        var json = ConfigJson.SerializeItems(new ItemsDocument().With(fenceId, [panel]));
+        Assert.Contains("\"fill\": true", json);
+        Assert.Contains("\"look\": \"list\"", json);
+        Assert.Equal(panel, ConfigJson.DeserializeItems(json).Of(fenceId)[0]);
+    }
+
+    [Fact]
+    public void Browsing_IntoBackUpHome_NeverAboveTheHomeFolder()
+    {
+        const string home = @"D:\GameLibrary";
+        var place = PanelPlace.At(home);
+        Assert.False(place.CanGoBack);
+        Assert.False(place.CanGoUp(home));
+
+        place = place.Into(@"D:\GameLibrary\Cyberpunk").Into(@"D:\GameLibrary\Cyberpunk\bin");
+        Assert.Equal(@"D:\GameLibrary\Cyberpunk\bin", place.Current);
+        Assert.True(place.CanGoUp(home));
+
+        var up = place.Up(home);
+        Assert.Equal(@"D:\GameLibrary\Cyberpunk", up.Current);
+        Assert.Equal(@"D:\GameLibrary\Cyberpunk\bin", up.Back().Current); // Up is a step Back undoes
+
+        var back = place.Back();
+        Assert.Equal(@"D:\GameLibrary\Cyberpunk", back.Current);
+        Assert.Equal(home, back.Back().Current);
+        Assert.Equal(home, back.Back().Back().Current); // nothing more to go back to: it stays
+
+        Assert.Equal(home, place.Home(home).Current);
+        Assert.Equal(place.Current, place.Home(home).Back().Current);
+        var atHome = PanelPlace.At(home);
+        Assert.Same(atHome, atHome.Up(home)); // at home, Up and Home do nothing
+        Assert.Same(atHome, atHome.Home(home));
+    }
+
+    [Theory]
+    [InlineData(@"D:\GameLibrary", @"D:\GameLibrary", null, "GameLibrary")]
+    [InlineData(@"D:\GameLibrary\", @"D:\GameLibrary", null, "GameLibrary")]
+    [InlineData(@"D:\GameLibrary", @"D:\GameLibrary\Cyberpunk\bin", null, "GameLibrary › Cyberpunk › bin")]
+    [InlineData(@"D:\GameLibrary", @"D:\GameLibrary", "My games", "My games")]
+    [InlineData(@"D:\GameLibrary", @"D:\GameLibrary\Saves", "My games", "My games › Saves")]
+    [InlineData(@"D:\", @"D:\Music", null, "D: › Music")]
+    public void HeaderOf_NamesTheFolderAndThePathBelowIt(string home, string shown, string? ownName, string expected) =>
+        Assert.Equal(expected, FolderPanels.HeaderOf(home, shown, ownName));
+
+    [Theory]
+    [InlineData(@"D:\GameLibrary", @"D:\GameLibrary\Saves", true)]
+    [InlineData(@"D:\GameLibrary", @"d:\gamelibrary\saves\x", true)]
+    [InlineData(@"D:\GameLibrary", @"D:\GameLibrary", false)]
+    [InlineData(@"D:\GameLibrary", @"D:\GameLibraryOld", false)]
+    [InlineData(@"D:\GameLibrary", @"D:\", false)]
+    [InlineData(@"D:\", @"D:\Music", true)]
+    public void IsBelow_OnlyRealSubfolders(string home, string folder, bool expected) => Assert.Equal(expected, PanelPlace.IsBelow(folder, home));
+
+    [Theory]
+    [InlineData(null, "")]
+    [InlineData(0L, "0 KB")]
+    [InlineData(1L, "1 KB")]
+    [InlineData(1024L, "1 KB")]
+    [InlineData(1025L, "2 KB")]
+    [InlineData(5_000_000L, "4,883 KB")]
+    public void SizeText_LikeExplorer_InWholeKilobytes(long? bytes, string expected) =>
+        Assert.Equal(expected, FolderPanels.SizeText(bytes, System.Globalization.CultureInfo.InvariantCulture));
+
+    [Fact]
+    public void Repair_NormalizesAHandEditedPanel_AndATypoNeverFailsTheFile()
+    {
+        const string json = """{ "schema": 1, "fences": { "f": [ { "id": "a", "target": "D:\\x", "panel": { "look": "tiles", "sort": "colour", "newest": -3 } } ] } }""";
+        var item = ItemEdits.Repair(ConfigJson.DeserializeItems(json)).Of("f")[0];
+        Assert.Equal(new FolderPanel(), item.Panel);
+    }
+}
````

- [ ] **Step 2: Run them to see them fail.** `dotnet test tests/NeoFences.Core.Tests`
  Expected: build fails with `CS0246: The type or namespace name 'PanelSort' could not be found`.

- [ ] **Step 3: Implement.** Write this patch to `m26-1-core.patch` and apply it:

````diff
diff --git a/src/NeoFences.Core/Config/ConfigJson.cs b/src/NeoFences.Core/Config/ConfigJson.cs
index 57da39c..30e15a7 100644
--- a/src/NeoFences.Core/Config/ConfigJson.cs
+++ b/src/NeoFences.Core/Config/ConfigJson.cs
@@ -26,6 +26,7 @@ public static class ConfigJson
             // Folder views (M21): a typo in a view's show or sort is repaired too, not the whole file lost (final review).
             new LenientEnumConverter<ViewShow>(), new LenientEnumConverter<FenceSort>(),
             new LenientEnumConverter<Items.ItemShow>(), new LenientEnumConverter<Items.FenceLayout>(), // M24 // M22: a typo in items.json shows the usual look, never fails the file
+            new LenientEnumConverter<Items.PanelLook>(), new LenientEnumConverter<Items.PanelSort>(), // M26
             new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
         },
     };
diff --git a/src/NeoFences.Core/Items/FolderPanels.cs b/src/NeoFences.Core/Items/FolderPanels.cs
new file mode 100644
index 0000000..cedbe12
--- /dev/null
+++ b/src/NeoFences.Core/Items/FolderPanels.cs
@@ -0,0 +1,193 @@
+using NeoFences.Core.Config;
+using NeoFences.Core.Model;
+
+namespace NeoFences.Core.Items;
+
+/// <summary>How a folder panel shows its entries (M26): rows with columns, rows with names, or icon tiles.</summary>
+public enum PanelLook { Details, List, Icons }
+
+/// <summary>A folder panel's sort key (M26): a header click or Sort by ▸.</summary>
+public enum PanelSort { Name, Date, Type, Size }
+
+/// <summary>
+/// A folder item shown as a panel (M26, spec 2026-10-05-folder-panel-design §1): its look and what it shows of its folder
+/// (the folder is the item's target). Read-only: NeoFences never writes to the folder.
+/// </summary>
+public sealed record FolderPanel
+{
+    public PanelLook Look { get; init; } = PanelLook.Details;
+    public ViewShow Show { get; init; } = ViewShow.All;
+
+    /// <summary>File-name patterns, e.g. "*.png;*.jpg"; empty = everything (subfolders always show when showing all).</summary>
+    public string Patterns { get; init; } = "";
+
+    /// <summary>Only the newest N entries (by date), then sorted; null = all of them.</summary>
+    public int? Newest { get; init; }
+
+    public PanelSort Sort { get; init; } = PanelSort.Name;
+    public bool Descending { get; init; }
+}
+
+/// <summary>
+/// Where a panel is browsing (M26 spec §2): the shown folder and the way back. Never saved: each start, and Home, show
+/// the panel's own folder. Up never goes above it.
+/// </summary>
+public sealed record PanelPlace(string Current, IReadOnlyList<string> History)
+{
+    public static PanelPlace At(string folder) => new(folder, []);
+
+    public bool CanGoBack => History.Count > 0;
+
+    public bool CanGoUp(string home) => IsBelow(Current, home);
+
+    /// <summary>A subfolder double-clicked: shown, and Back returns here.</summary>
+    public PanelPlace Into(string folder) => new(folder, [.. History, Current]);
+
+    public PanelPlace Back() => History.Count == 0 ? this : new(History[^1], [.. History.Take(History.Count - 1)]);
+
+    public PanelPlace Up(string home) =>
+        CanGoUp(home) && Path.GetDirectoryName(FolderViews.ListedFolder(Current)) is { } parent ? Into(parent) : this;
+
+    public PanelPlace Home(string home) => FolderViews.SameFolder(Current, home) ? this : Into(home);
+
+    /// <summary>A real subfolder of <paramref name="home"/> (any depth), not the folder itself or a neighbour that starts alike.</summary>
+    public static bool IsBelow(string folder, string home)
+    {
+        var parent = FolderViews.ListedFolder(home).TrimEnd('\\', '/') + "\\";
+        return folder.Length > parent.Length && folder.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
+    }
+}
+
+/// <summary>
+/// The folder panel element (M26, ADR-048): a folder item with <see cref="VirtualItem.Panel"/> set shows its folder live,
+/// inside any fence, beside items and widgets. Pure; the listing comes from NeoFences.Shell.
+/// </summary>
+public static class FolderPanels
+{
+    /// <summary>A new panel's size: room for a dozen rows.</summary>
+    public static GridSpan DefaultSpan { get; } = new(4, 4);
+
+    public static bool IsPanel(VirtualItem item) => item.Panel is not null && item.Kind == ItemKind.Path;
+
+    /// <summary>A panel of <paramref name="folder"/>: busy folders (Downloads, Screenshots) newest first with the newest 30, the rest by name.</summary>
+    public static VirtualItem Create(string folder, IReadOnlyList<string> busyFolders) =>
+        VirtualItem.Create(folder) with
+        {
+            Panel = busyFolders.Any(busy => FolderViews.SameFolder(busy, folder))
+                ? new FolderPanel { Sort = PanelSort.Date, Descending = true, Newest = FolderViews.BusyFolderNewest }
+                : new FolderPanel(),
+        };
+
+    /// <summary>A folder view's settings as a panel (the migration): it looked like icon tiles, its date sort newest first.</summary>
+    public static FolderPanel FromView(FolderView view) => new()
+    {
+        Look = PanelLook.Icons,
+        Show = view.Show,
+        Patterns = view.Patterns,
+        Newest = view.Newest,
+        Sort = view.Sort switch { FenceSort.Date => PanelSort.Date, FenceSort.Type => PanelSort.Type, _ => PanelSort.Name },
+        Descending = view.Sort == FenceSort.Date,
+    };
+
+    /// <summary>A hand-edited panel repaired: known looks and sorts, a count within 1–500, valid patterns.</summary>
+    public static FolderPanel? Normalize(FolderPanel? panel) => panel is null ? null : panel with
+    {
+        Look = Enum.IsDefined(panel.Look) ? panel.Look : PanelLook.Details,
+        Show = Enum.IsDefined(panel.Show) ? panel.Show : ViewShow.All,
+        Sort = Enum.IsDefined(panel.Sort) ? panel.Sort : PanelSort.Name,
+        Newest = panel.Newest is >= 1 and var newest ? Math.Min(newest, FolderViews.MaxNewest) : null,
+        Patterns = FolderViews.ParsePatterns(panel.Patterns) is not null ? panel.Patterns ?? "" : "",
+    };
+
+    /// <summary>
+    /// What the panel shows of a listing: the chosen kind, the patterns, the newest N, the sort, at most
+    /// <see cref="FolderViews.MaxShown"/> entries.
+    /// </summary>
+    public static FolderViews.Selection Select(IReadOnlyList<ItemInfo> entries, FolderPanel panel)
+    {
+        var patterns = FolderViews.ParsePatterns(panel.Patterns) ?? [];
+        IEnumerable<ItemInfo> matching = entries.Where(entry => panel.Show switch
+        {
+            ViewShow.Files => !entry.IsFolder,
+            ViewShow.Folders => entry.IsFolder,
+            _ => true,
+        });
+        // Subfolders pass the patterns: a "*.png" panel of Screenshots still shows its game folders.
+        if (patterns.Count > 0)
+            matching = matching.Where(entry => entry.IsFolder || patterns.Any(pattern => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, entry.Name, ignoreCase: true)));
+        if (panel.Newest is { } newest) matching = matching.OrderByDescending(entry => entry.Modified).Take(newest);
+        var ordered = Order(matching, panel.Sort, panel.Descending);
+        return new FolderViews.Selection([.. ordered.Take(FolderViews.MaxShown)], Math.Max(0, ordered.Count - FolderViews.MaxShown), entries.Count);
+    }
+
+    /// <summary>
+    /// Name and Type keep folders first in both directions (like Explorer); Date and Size mix them (a folder has no size: it
+    /// counts as the smallest). Ties (same type, date or size) go by name, A→Z.
+    /// </summary>
+    private static List<ItemInfo> Order(IEnumerable<ItemInfo> entries, PanelSort sort, bool descending)
+    {
+        var byName = ItemSorting.NaturalComparer.Instance;
+        IOrderedEnumerable<ItemInfo> Key<TKey>(IEnumerable<ItemInfo> source, Func<ItemInfo, TKey> key, IComparer<TKey>? comparer = null) =>
+            descending ? source.OrderByDescending(key, comparer) : source.OrderBy(key, comparer);
+        IOrderedEnumerable<ItemInfo> ThenKey<TKey>(IOrderedEnumerable<ItemInfo> source, Func<ItemInfo, TKey> key, IComparer<TKey>? comparer = null) =>
+            descending ? source.ThenByDescending(key, comparer) : source.ThenBy(key, comparer);
+        var foldersFirst = entries.OrderBy(entry => !entry.IsFolder);
+        return (sort switch
+        {
+            PanelSort.Type => ThenKey(foldersFirst, entry => entry.TypeName, StringComparer.CurrentCultureIgnoreCase).ThenBy(entry => entry.Name, byName),
+            PanelSort.Date => Key(entries, entry => entry.Modified).ThenBy(entry => entry.Name, byName),
+            PanelSort.Size => Key(entries, entry => entry.IsFolder ? -1 : entry.Size ?? 0).ThenBy(entry => entry.Name, byName),
+            _ => ThenKey(foldersFirst, entry => entry.Name, byName),
+        }).ToList();
+    }
+
+    /// <summary>A column header clicked: the same column again reverses it; another starts in its natural direction.</summary>
+    public static FolderPanel HeaderSort(FolderPanel panel, PanelSort clicked) =>
+        clicked == panel.Sort ? panel with { Descending = !panel.Descending }
+            : panel with { Sort = clicked, Descending = clicked is PanelSort.Date or PanelSort.Size };
+
+    /// <summary>
+    /// The panel's header: its own name (or its folder's), and while it browses below its folder the path down to the
+    /// shown one ("GameLibrary › Cyberpunk › bin").
+    /// </summary>
+    public static string HeaderOf(string home, string shown, string? ownName)
+    {
+        var name = ownName ?? FolderViews.NameOf(home);
+        if (!PanelPlace.IsBelow(shown, home)) return name;
+        var below = Path.GetRelativePath(FolderViews.ListedFolder(home), FolderViews.ListedFolder(shown));
+        return string.Join(" › ", [name, .. below.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)]);
+    }
+
+    /// <summary>A file's size as Explorer's Details shows it: whole kilobytes, rounded up; nothing for a folder.</summary>
+    public static string SizeText(long? bytes, System.Globalization.CultureInfo culture) =>
+        bytes is { } size ? $"{Math.Ceiling(size / 1024.0).ToString("N0", culture)} KB" : "";
+
+    /// <summary>Details columns for a panel this many cells wide: Name and Date up to 2, all four from 3.</summary>
+    public static int Columns(int spanColumns) => spanColumns <= 2 ? 2 : 4;
+
+    /// <summary>
+    /// "Fill fence": the panel takes the whole fence while it is the fence's only element; beside anything else it sits on
+    /// cells (its own size, or 4×4).
+    /// </summary>
+    public static bool Fills(IReadOnlyList<VirtualItem> fenceItems) => fenceItems is [{ Fill: true } only] && IsPanel(only);
+
+    /// <param name="MigratedFenceIds">Fences that were folder views and now hold one panel.</param>
+    public sealed record Migration(NeoFencesConfig Config, ItemsDocument Items, IReadOnlyList<string> MigratedFenceIds);
+
+    /// <summary>
+    /// Every folder view becomes an items fence holding one filling panel of its folder (spec §1). Run again after a save
+    /// cut short between the two files (the items saved, the config not), it adds no second panel.
+    /// </summary>
+    public static Migration MigrateViews(NeoFencesConfig config, ItemsDocument items)
+    {
+        var views = config.Fences.Where(fence => fence.View is not null).ToList();
+        foreach (var fence in views)
+        {
+            var view = fence.View!;
+            if (!items.Of(fence.Id).Any(item => IsPanel(item) && FolderViews.SameFolder(item.Target, view.Path)))
+                items = items.With(fence.Id, [.. items.Of(fence.Id), VirtualItem.Create(view.Path) with { Panel = FromView(view), Fill = true }]);
+            config = config.WithFence(fence with { View = null });
+        }
+        return new Migration(config, items, [.. views.Select(fence => fence.Id)]);
+    }
+}
diff --git a/src/NeoFences.Core/Items/FolderViews.cs b/src/NeoFences.Core/Items/FolderViews.cs
index f4397d9..6a614a3 100644
--- a/src/NeoFences.Core/Items/FolderViews.cs
+++ b/src/NeoFences.Core/Items/FolderViews.cs
@@ -1,5 +1,4 @@
 using System.Globalization;
-using System.IO.Enumeration;
 using NeoFences.Core.Model;
 
 namespace NeoFences.Core.Items;
@@ -16,28 +15,18 @@ public static class FolderViews
     /// <summary>Downloads and Screenshots start newest first with this many entries.</summary>
     public const int BusyFolderNewest = 30;
 
-    /// <param name="Shown">Entry paths in the order shown.</param>
+    /// <param name="Entries">The entries shown, in order (M26: with their facts, for a panel's columns).</param>
     /// <param name="Hidden">Entries the view would show but the cap leaves out ("+ N more").</param>
     /// <param name="Listed">Entries in the folder before any filter ("This folder is empty").</param>
-    public sealed record Selection(IReadOnlyList<string> Shown, int Hidden, int Listed);
-
-    public static Selection Select(IReadOnlyList<ItemInfo> entries, FolderView view)
+    public sealed record Selection(IReadOnlyList<ItemInfo> Entries, int Hidden, int Listed)
     {
-        var patterns = ParsePatterns(view.Patterns) ?? [];
-        IEnumerable<ItemInfo> matching = entries.Where(entry => view.Show switch
-        {
-            ViewShow.Files => !entry.IsFolder,
-            ViewShow.Folders => entry.IsFolder,
-            _ => true,
-        });
-        // Subfolders pass the patterns: a "*.png" view of Screenshots still shows its game folders.
-        if (patterns.Count > 0)
-            matching = matching.Where(entry => entry.IsFolder || patterns.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, entry.Name, ignoreCase: true)));
-        if (view.Newest is { } newest) matching = matching.OrderByDescending(entry => entry.Modified).Take(newest);
-        var ordered = ItemSorting.Order(matching, view.Sort == FenceSort.Manual ? FenceSort.Name : view.Sort);
-        return new Selection([.. ordered.Take(MaxShown)], Math.Max(0, ordered.Count - MaxShown), entries.Count);
+        /// <summary>Entry paths in the order shown.</summary>
+        public IReadOnlyList<string> Shown => [.. Entries.Select(entry => entry.ItemRef)];
     }
 
+    /// <summary>A view's listing as its panel shows it (M26: views became panels; the same rules).</summary>
+    public static Selection Select(IReadOnlyList<ItemInfo> entries, FolderView view) => FolderPanels.Select(entries, FolderPanels.FromView(view));
+
     /// <summary>
     /// "*.png; .jpg, txt" → ["*.png", "*.jpg", "*.txt"]: split on ; and ,; a bare extension becomes "*.ext". Null when a
     /// part is not a file-name pattern (a path, or a character Windows forbids in names).
diff --git a/src/NeoFences.Core/Items/GridLayout.cs b/src/NeoFences.Core/Items/GridLayout.cs
index 6b2bfd3..3877c10 100644
--- a/src/NeoFences.Core/Items/GridLayout.cs
+++ b/src/NeoFences.Core/Items/GridLayout.cs
@@ -36,9 +36,11 @@ public static class FenceGrid
     /// <summary>The farthest row or column a stored cell may name (a hand-edited 10,000,000 would grow the map every layout).</summary>
     public const int MaxCell = 1000;
 
-    /// <summary>An item's span: its own size, else 1×2 for a game shown as a cover (a 2:3 poster fits), else 1×1.</summary>
+    /// <summary>An item's span: its own size, else a widget's or a panel's (M25, M26), else 1×2 for a game shown as a cover (a 2:3 poster fits), else 1×1.</summary>
     public static GridSpan SpanOf(VirtualItem item) =>
-        item.Size ?? (Widgets.Of(item.Target) is { } widget ? Widgets.DefaultSpan(widget) : GameItems.ShowsCover(item) ? new GridSpan(1, 2) : GridSpan.One);
+        item.Size ?? (Widgets.Of(item.Target) is { } widget ? Widgets.DefaultSpan(widget)
+            : FolderPanels.IsPanel(item) ? FolderPanels.DefaultSpan
+            : GameItems.ShowsCover(item) ? new GridSpan(1, 2) : GridSpan.One);
 
     public static GridArrangement Arrange(IReadOnlyList<GridElement> elements, int columns, FenceLayout layout)
     {
diff --git a/src/NeoFences.Core/Items/ItemsDocument.cs b/src/NeoFences.Core/Items/ItemsDocument.cs
index 1076c42..95a6690 100644
--- a/src/NeoFences.Core/Items/ItemsDocument.cs
+++ b/src/NeoFences.Core/Items/ItemsDocument.cs
@@ -178,9 +178,12 @@ public static class ItemEdits
         };
     }
 
-    /// <summary>Size ▸ (M24): these items take this size (null: the default). The same document when none of them is known.</summary>
+    /// <summary>
+    /// Size ▸ (M24): these items take this size (null: the default); a panel filling its fence goes back on cells (M26).
+    /// The same document when none of them is known.
+    /// </summary>
     public static ItemsDocument SetSize(ItemsDocument document, IReadOnlyCollection<string> itemIds, GridSpan? size) =>
-        Change(document, item => itemIds.Contains(item.Id) ? item with { Size = size?.Clamp(GridSpan.Max) } : null);
+        Change(document, item => itemIds.Contains(item.Id) ? item with { Size = size?.Clamp(GridSpan.Max), Fill = false } : null);
 
     /// <summary>Free fences (M24): these items (by id) store these cells.</summary>
     public static ItemsDocument Place(ItemsDocument document, IReadOnlyDictionary<string, GridCell> cells) =>
@@ -218,6 +221,7 @@ public static class ItemEdits
                     Icon = item.Icon is { File: null or "", Image: null or "" } ? null : item.Icon,
                     Size = item.Size?.Clamp(GridSpan.Max), // M24: a hand-edited size within 1–4 each way
                     Cell = item.Cell is { Column: >= 0 and <= FenceGrid.MaxCell, Row: >= 0 and <= FenceGrid.MaxCell } ? item.Cell : null, // M24 final review I4
+                    Panel = FolderPanels.Normalize(item.Panel), // M26: a hand-edited look, sort or count
                 })
                 .ToList();
         }
diff --git a/src/NeoFences.Core/Items/VirtualItem.cs b/src/NeoFences.Core/Items/VirtualItem.cs
index ea76ce6..f31e6dd 100644
--- a/src/NeoFences.Core/Items/VirtualItem.cs
+++ b/src/NeoFences.Core/Items/VirtualItem.cs
@@ -61,6 +61,13 @@ public sealed record VirtualItem
     /// <summary>A clock widget's options (M25); null: the defaults.</summary>
     public WidgetOptions? Widget { get; init; }
 
+    /// <summary>A folder shown as a panel (M26); null: a plain folder icon.</summary>
+    public FolderPanel? Panel { get; init; }
+
+    /// <summary>A panel takes the whole fence while it is the fence's only element (M26, <see cref="FolderPanels.Fills"/>).</summary>
+    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
+    public bool Fill { get; init; }
+
     [JsonIgnore]
     public ItemKind Kind => ItemKinds.Of(Target);
 
diff --git a/src/NeoFences.Core/Model/ItemSorting.cs b/src/NeoFences.Core/Model/ItemSorting.cs
index d0ada9f..7ed29f5 100644
--- a/src/NeoFences.Core/Model/ItemSorting.cs
+++ b/src/NeoFences.Core/Model/ItemSorting.cs
@@ -1,7 +1,8 @@
 namespace NeoFences.Core.Model;
 
 /// <summary>What sorting needs to know about one item (filled in by NeoFences.Shell).</summary>
-public sealed record ItemInfo(string ItemRef, string Name, bool IsFolder, string TypeName, DateTimeOffset Modified);
+/// <param name="Size">A file's size in bytes (M26: a folder panel's Size column); null for folders and when unknown.</param>
+public sealed record ItemInfo(string ItemRef, string Name, bool IsFolder, string TypeName, DateTimeOffset Modified, long? Size = null);
 
 /// <summary>
 /// Item order for "Sort by" (one time) and the library's listing. Name and Type put folders first, like
@@ -19,7 +20,7 @@ public static class ItemSorting
     }).Select(item => item.ItemRef).ToList();
 
     /// <summary>"setup2" before "setup10": runs of digits compare by value, the rest ignoring case (like Explorer).</summary>
-    private sealed class NaturalComparer : IComparer<string>
+    internal sealed class NaturalComparer : IComparer<string>
     {
         public static NaturalComparer Instance { get; } = new();
 
````

- [ ] **Step 4: Run the tests.** `dotnet test tests/NeoFences.Core.Tests`
  Expected: `Passed! - Failed: 0, Passed: 601`.

- [ ] **Step 5: Commit.** `git add -A && git commit -m "feat: added folder panels to the core: settings, sorting, browsing and the folder-view migration"`

### Task 2: Shell and App — panels in fences

**Files:**
- Create: `src/NeoFences.App/FolderPanelView.xaml(.cs)`, `FolderPanelModel.cs`, `FenceHost.FolderPanels.cs`
- Delete: `src/NeoFences.App/FenceHost.FolderViews.cs`
- Modify: `src/NeoFences.Shell/FolderItems.cs` (sizes), `FenceItemView.cs` (panel state, Fill), `FenceGridPanel.cs`
  (Fill), `FenceWindow.xaml(.cs)` (template, menus, input routing, drops, fill), `FenceHost.cs` (wiring, migration at
  start and after a restore, tray), `FenceHost.Items.cs` (panel menu, Show as folder panel), `FenceHost.Watching.cs`
  (Refresh), `FolderViewWindow.xaml(.cs)` (panel settings)

**Interfaces:**
- Consumes: everything Task 1 produces.
- Produces: `PanelCommand` records (`PanelOpen`, `PanelNavigate(PanelMove)`, `PanelSortBy`, `PanelEntryMenu`,
  `PanelDrag`, `PanelOpenFolder`); `PanelContent`; `FenceWindow.PanelCommandRequested`, `AddFolderPanelRequested`,
  `NewFolderPanelRequested`, `SetPanelContent(key, content)`, `DropOverPanel`; `ShownItem.Panel`, `ShownItem.Fill`.

- [ ] **Step 1: Implement.** Write this patch to `m26-2-app.patch` and apply it:

````diff
diff --git a/src/NeoFences.App/FenceGridPanel.cs b/src/NeoFences.App/FenceGridPanel.cs
index ca6036d..6faa9c3 100644
--- a/src/NeoFences.App/FenceGridPanel.cs
+++ b/src/NeoFences.App/FenceGridPanel.cs
@@ -30,6 +30,9 @@ public sealed class FenceGridPanel : Panel
     /// <summary>The last layout: each child's cell and span, in child order.</summary>
     public GridArrangement Arrangement { get; private set; } = new([], [], 0);
 
+    // M26: a lone panel set to "Fill fence" takes the whole area (the list does not scroll then: its height is finite).
+    private bool _filling;
+
     // ponytail: arranged at every measure (one pass over a map of cells, ~µs for 500 elements); cache per element set if a profile ever shows it.
     protected override Size MeasureOverride(Size availableSize)
     {
@@ -46,6 +49,12 @@ public sealed class FenceGridPanel : Panel
             Serilog.Log.Warning(failure, "fence grid: layout failed; one element per cell in order"); // never a crash (spec §4)
             Arrangement = FenceGrid.Arrange([.. elements.Select(_ => new GridElement(GridSpan.One, null))], Columns, FenceLayout.Flow);
         }
+        _filling = children is [FrameworkElement { DataContext: FenceItemView { Fills: true } }] && double.IsFinite(availableSize.Width) && double.IsFinite(availableSize.Height);
+        if (_filling)
+        {
+            children[0].Measure(availableSize);
+            return availableSize;
+        }
         for (var index = 0; index < children.Count; index++)
         {
             var span = Arrangement.Spans[index];
@@ -58,6 +67,11 @@ public sealed class FenceGridPanel : Panel
     protected override Size ArrangeOverride(Size finalSize)
     {
         var children = InternalChildren.Cast<UIElement>().ToList();
+        if (_filling && children.Count == 1)
+        {
+            children[0].Arrange(new Rect(finalSize));
+            return finalSize;
+        }
         for (var index = 0; index < children.Count && index < Arrangement.Cells.Count; index++)
         {
             children[index].Arrange(CellRect(Arrangement.Cells[index], Arrangement.Spans[index]));
diff --git a/src/NeoFences.App/FenceHost.FolderPanels.cs b/src/NeoFences.App/FenceHost.FolderPanels.cs
new file mode 100644
index 0000000..bf84955
--- /dev/null
+++ b/src/NeoFences.App/FenceHost.FolderPanels.cs
@@ -0,0 +1,400 @@
+using System.IO;
+using System.Windows.Controls;
+using NeoFences.Core.Items;
+using NeoFences.Core.Model;
+using NeoFences.Shell;
+using Serilog;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// Folder panels (M26, spec 2026-10-05-folder-panel-design, ADR-048): a folder item shown live inside any fence, read-only.
+/// One <see cref="FolderLister"/> per panel on the folder it shows (a hidden tab keeps listing); browsing lives in memory.
+/// Folder-view fences (M21) became fences holding one filling panel. NeoFences never writes to a panel's folder: no drop
+/// into it, no rename or delete from NeoFences' own menu.
+/// </summary>
+public sealed partial class FenceHost
+{
+    private readonly Dictionary<string, FolderLister> _panelListers = new(StringComparer.Ordinal);
+    // Per panel item: the folder of its last listing and that listing (null: not readable); missing while the first is on its way.
+    private readonly Dictionary<string, (string Folder, IReadOnlyList<ItemInfo>? Listed)> _panelListings = new(StringComparer.Ordinal);
+    // The selection made off the UI thread for the panel's settings and folder at the time, and the newest request per panel (M23).
+    private readonly Dictionary<string, (FolderPanel Panel, string Folder, FolderViews.Selection? Selection)> _panelSelections = new(StringComparer.Ordinal);
+    private readonly Dictionary<string, int> _panelSelectionRequests = new(StringComparer.Ordinal);
+    private readonly Dictionary<string, PanelPlace> _panelPlaces = new(StringComparer.Ordinal);
+    private IReadOnlyList<string>? _busyFolders;
+
+    private IEnumerable<VirtualItem> AllPanels => _items.Fences.Values.SelectMany(items => items).Where(FolderPanels.IsPanel);
+
+    /// <summary>The folder a panel shows now: where it browsed to, or its own (a target changed since: its own again).</summary>
+    private string ShownFolder(VirtualItem panel)
+    {
+        if (_panelPlaces.TryGetValue(panel.Id, out var place) && (FolderViews.SameFolder(place.Current, panel.Target) || PanelPlace.IsBelow(place.Current, panel.Target))) return place.Current;
+        _panelPlaces[panel.Id] = PanelPlace.At(panel.Target);
+        return panel.Target;
+    }
+
+    /// <summary>A lister for every panel, on the folder it shows; listers of panels that are gone (or moved on) go.</summary>
+    private void EnsurePanelListers()
+    {
+        var panels = AllPanels.GroupBy(panel => panel.Id).ToDictionary(group => group.Key, group => ShownFolder(group.First()), StringComparer.Ordinal);
+        foreach (var (itemId, lister) in _panelListers.ToList())
+        {
+            if (panels.TryGetValue(itemId, out var folder) && FolderViews.SameFolder(folder, lister.Folder)) continue;
+            lister.Dispose(); // the folder stays as it is
+            _panelListers.Remove(itemId);
+            if (!panels.ContainsKey(itemId)) ForgetPanel(itemId);
+        }
+        foreach (var (itemId, folder) in panels.Where(panel => !_panelListers.ContainsKey(panel.Key)))
+        {
+            var lister = new FolderLister(folder, noticeOwner: _messages.Handle, label: "folder panel",
+                show: listed => ShowPanelListing(itemId, folder, listed),
+                logFailure: failure => Log.Warning(failure, "folder panel: cannot watch {Folder}", folder));
+            if (_gameMode) lister.SetPaused(true);
+            _panelListers[itemId] = lister;
+        }
+    }
+
+    private void ForgetPanel(string itemId)
+    {
+        _panelListings.Remove(itemId);
+        _panelSelections.Remove(itemId);
+        _panelPlaces.Remove(itemId);
+    }
+
+    private void StopPanelListers()
+    {
+        foreach (var lister in _panelListers.Values) lister.Dispose();
+        _panelListers.Clear();
+    }
+
+    private void SetPanelsPaused(bool paused)
+    {
+        foreach (var lister in _panelListers.Values) lister.SetPaused(paused);
+    }
+
+    /// <summary>Windows asks to remove a drive a panel shows (a USB stick): that panel lets go and says "not available".</summary>
+    private bool ReleasePanelsForRemoval(nint handle) => _panelListers.Values.Aggregate(false, (released, lister) => lister.ReleaseForRemoval(handle) | released);
+
+    /// <summary>A listing arrived (on the UI thread): kept, filtered and sorted off the UI thread, then shown.</summary>
+    private void ShowPanelListing(string itemId, string folder, IReadOnlyList<ItemInfo>? listed)
+    {
+        if (!_panelListers.TryGetValue(itemId, out var lister) || !FolderViews.SameFolder(lister.Folder, folder)) return; // gone, or moved on meanwhile
+        var wasAvailable = !_panelListings.TryGetValue(itemId, out var before) || before.Listed is not null;
+        _panelListings[itemId] = (folder, listed);
+        if (listed is null && wasAvailable) Log.Information("folder panel: {Folder} is not available", folder); // once per outage, not every 7 s retry
+        if (_items.Find(itemId)?.Panel is not { } panel) return;
+        // Off the UI thread (M23): 20,000 entries sorted by name take ~140 ms, at every re-list.
+        var request = _panelSelectionRequests[itemId] = _panelSelectionRequests.GetValueOrDefault(itemId) + 1;
+        Task.Run(() => listed is null ? null : FolderPanels.Select(listed, panel)).ContinueWith(selecting =>
+        {
+            if (selecting.IsFaulted)
+            {
+                Log.Warning(selecting.Exception, "folder panel {ItemId}: the listing could not be sorted", itemId);
+                return;
+            }
+            if (!_panelListers.ContainsKey(itemId) || _panelSelectionRequests.GetValueOrDefault(itemId) != request) return; // gone, or a newer listing
+            _panelSelections[itemId] = (panel, folder, selecting.Result);
+            ShowPanel(itemId);
+        }, TaskScheduler.FromCurrentSynchronizationContext());
+    }
+
+    /// <summary>The panel's content in the window that shows its fence now, if any.</summary>
+    private void ShowPanel(string itemId)
+    {
+        if (_items.Find(itemId) is not { } panel || _items.FenceOf(itemId) is not { } fenceId) return;
+        if (_windows.Values.FirstOrDefault(window => window.FenceId == fenceId) is { } shown) shown.SetPanelContent(itemId, PanelContentOf(panel));
+    }
+
+    /// <summary>Every panel of the shown fence gets its content (RefreshWindow calls this after the items).</summary>
+    private void RenderPanels(FenceWindow window, IReadOnlyList<VirtualItem> items)
+    {
+        foreach (var panel in items.Where(FolderPanels.IsPanel)) window.SetPanelContent(panel.Id, PanelContentOf(panel));
+    }
+
+    /// <summary>What a panel shows: its header and buttons, and the entries its settings select (once here after a settings change, a user action).</summary>
+    private PanelContent PanelContentOf(VirtualItem panel)
+    {
+        var folder = ShownFolder(panel);
+        var place = _panelPlaces[panel.Id];
+        var header = FolderPanels.HeaderOf(panel.Target, folder, panel.OwnName);
+        var atHome = FolderViews.SameFolder(folder, panel.Target);
+        // A panel filling a fence titled like it, at its own folder, needs no second title (a migrated folder view looks as before).
+        var showHeader = !(atHome && !place.CanGoBack && _items.FenceOf(panel.Id) is { } fenceId && FolderPanels.Fills(_items.Of(fenceId))
+            && _config.Fences.FirstOrDefault(fence => fence.Id == fenceId)?.Title == header);
+        if (!_panelListings.TryGetValue(panel.Id, out var listing) || !FolderViews.SameFolder(listing.Folder, folder))
+            return new PanelContent(header, showHeader, place.CanGoBack, place.CanGoUp(panel.Target), !atHome, [], Status: null, More: null); // on its way (a slow share)
+        if (!_panelSelections.TryGetValue(panel.Id, out var cached) || cached.Panel != panel.Panel || !FolderViews.SameFolder(cached.Folder, folder))
+        {
+            cached = (panel.Panel!, folder, listing.Listed is null ? null : FolderPanels.Select(listing.Listed, panel.Panel!));
+            _panelSelections[panel.Id] = cached;
+        }
+        var (status, more) = FolderViews.Status(cached.Selection, new FolderView { Path = folder });
+        return new PanelContent(header, showHeader, place.CanGoBack, place.CanGoUp(panel.Target), !atHome, cached.Selection?.Entries ?? [], status, more);
+    }
+
+    /// <summary>What a panel asked for (M26 spec §2).</summary>
+    private void OnPanelCommand(FenceWindow window, string itemId, PanelCommand command)
+    {
+        if (_items.Find(itemId) is not { } panel || !FolderPanels.IsPanel(panel)) return;
+        switch (command)
+        {
+            case PanelOpen { Paths: [var only] } when IsListedFolder(itemId, only):
+                Browse(panel, place => place.Into(only));
+                break;
+            case PanelOpen open:
+                foreach (var path in open.Paths) OpenItem(path, ownerHandle: window.Handle); // a folder among several opens in Explorer
+                SetPeek(false);
+                break;
+            case PanelNavigate { Move: PanelMove.Back }:
+                Browse(panel, place => place.Back());
+                break;
+            case PanelNavigate { Move: PanelMove.Up }:
+                Browse(panel, place => place.Up(panel.Target));
+                break;
+            case PanelNavigate { Move: PanelMove.Home }:
+                Browse(panel, place => place.Home(panel.Target));
+                break;
+            case PanelSortBy sortBy:
+                SetPanel(itemId, FolderPanels.HeaderSort(panel.Panel!, sortBy.Sort));
+                break;
+            case PanelEntryMenu menu:
+                ShowEntryMenu(window, menu.Paths, menu.Extended, menu.ScreenX, menu.ScreenY, menu.FromKeyboard);
+                break;
+            case PanelDrag drag:
+                DragEntries(window, drag.Paths);
+                break;
+            case PanelOpenFolder:
+                OpenItem(ShownFolder(panel), ownerHandle: window.Handle);
+                break;
+        }
+    }
+
+    /// <summary>A path the panel's last listing has as a folder (no disk asked: a dead share cannot freeze the click).</summary>
+    private bool IsListedFolder(string itemId, string path) =>
+        _panelListings.TryGetValue(itemId, out var listing) && listing.Listed?.Any(entry => entry.IsFolder && ItemKinds.Comparer.Equals(entry.ItemRef, path)) == true;
+
+    /// <summary>Back, Up, Home or into a subfolder: the panel lists its new folder (nothing saved).</summary>
+    private void Browse(VirtualItem panel, Func<PanelPlace, PanelPlace> move)
+    {
+        ShownFolder(panel); // a place for it, its own folder if the target changed
+        var place = move(_panelPlaces[panel.Id]);
+        if (place == _panelPlaces[panel.Id]) return;
+        _panelPlaces[panel.Id] = place;
+        EnsurePanelListers();
+        ShowPanel(panel.Id);
+    }
+
+    /// <summary>A panel's look, sort or filters changed: saved, and shown at once.</summary>
+    private void SetPanel(string itemId, FolderPanel settings)
+    {
+        if (_items.Find(itemId) is not { } item) return;
+        _items = ItemEdits.Replace(_items, item with { Panel = settings });
+        ItemsChanged(checkTargets: []);
+    }
+
+    /// <summary>A panel's menu: Look ▸, Sort by ▸, settings, Open folder, Size ▸ with Fill fence, Show as icon, Remove.</summary>
+    private void ShowPanelMenu(FenceWindow window, VirtualItem item, bool fromKeyboard)
+    {
+        var panel = item.Panel!;
+        var menu = new ContextMenu();
+        MenuItem Command(ItemsControl parent, string header, Action run, bool? isChecked = null, bool enabled = true)
+        {
+            var command = new MenuItem { Header = header, IsChecked = isChecked == true, IsEnabled = enabled };
+            command.Click += (_, _) => run();
+            parent.Items.Add(command);
+            return command;
+        }
+        if (CheckOf(item.Target).State != TargetState.Ok)
+        {
+            Command(menu, "Locate…", () => Locate(window, item.Id));
+            menu.Items.Add(new Separator());
+        }
+        var look = new MenuItem { Header = "Look" };
+        foreach (var (choice, name) in new[] { (PanelLook.Details, "Details"), (PanelLook.List, "List"), (PanelLook.Icons, "Icons") })
+            Command(look, name, () => SetPanel(item.Id, panel with { Look = choice }), isChecked: panel.Look == choice);
+        menu.Items.Add(look);
+        var sort = new MenuItem { Header = "Sort by" };
+        foreach (var (choice, name) in new[] { (PanelSort.Name, "Name"), (PanelSort.Date, "Date modified"), (PanelSort.Type, "Type"), (PanelSort.Size, "Size") })
+            Command(sort, name, () => SetPanel(item.Id, FolderPanels.HeaderSort(panel, choice)), isChecked: panel.Sort == choice);
+        menu.Items.Add(sort);
+        Command(menu, "Panel settings…", () => EditPanel(window, item.Id));
+        Command(menu, "Open folder", () => OpenItem(ShownFolder(item), ownerHandle: window.Handle));
+        menu.Items.Add(new Separator());
+        menu.Items.Add(SizeMenu(menu, [item]));
+        var alone = _items.Of(window.FenceId) is [_];
+        Command(menu, "Fill fence", () => SetFill(item.Id, !item.Fill), isChecked: item.Fill && alone, enabled: alone);
+        Command(menu, "Show as icon", () => SetPanelShown(item.Id, null));
+        Command(menu, "Remove from fence", () => RemoveItems(window, [item.Id]));
+        menu.Items.Add(new Separator());
+        menu.Items.Add(new MenuItem { Header = "Shift+right-click on entries: Windows' menu", IsEnabled = false });
+        window.ShowItemMenu(menu, fromKeyboard);
+    }
+
+    private void SetFill(string itemId, bool fill)
+    {
+        if (_items.Find(itemId) is not { } item) return;
+        _items = ItemEdits.Replace(_items, item with { Fill = fill });
+        Log.Information("folder panel {ItemId} fills its fence: {Fill}", itemId, fill);
+        ItemsChanged(checkTargets: []);
+    }
+
+    /// <summary>Folder item → "Show as folder panel" (the defaults, 4×4, in place); panel → "Show as icon" (null).</summary>
+    private void SetPanelShown(string itemId, FolderPanel? panel)
+    {
+        if (_items.Find(itemId) is not { } item) return;
+        _items = ItemEdits.Replace(_items, item with { Panel = panel, Size = null, Fill = false });
+        Log.Information("item {ItemId} shown as {Look}", itemId, panel is null ? "an icon" : "a folder panel");
+        ItemsChanged(checkTargets: []);
+    }
+
+    /// <summary>Fence menu → "Add folder panel…": Windows' folder dialog, then a panel at the end (Flow) or the first free spot (Free).</summary>
+    private void AddFolderPanel(FenceWindow window)
+    {
+        if (window.Kind != FenceKind.Items || PickPanelFolder(window.Handle) is not { } folder) return;
+        var panel = FolderPanels.Create(folder, BusyFolders);
+        _items = _items.With(window.FenceId, [.. _items.Of(window.FenceId), panel]); // not ItemEdits.Add: the folder's own icon may be there too
+        Log.Information("folder panel added to fence {FenceId} for {Folder}", window.FenceId, folder);
+        ItemsChanged(checkTargets: [folder]);
+        window.SelectItems([panel.Id]);
+    }
+
+    /// <summary>Tray or fence menu → "New folder panel…": a new fence, titled with the folder's name, holding one filling panel.</summary>
+    private void NewFolderPanel(nint ownerHandle)
+    {
+        if (PickPanelFolder(ownerHandle) is not { } folder) return;
+        SetQuickHidden(false); // a new fence must show
+        (_config, var fence) = FenceEdits.CreateFence(_config, FolderViews.NameOf(folder));
+        _items = _items.With(fence.Id, [FolderPanels.Create(folder, BusyFolders) with { Fill = true }]);
+        Log.Information("fence {FenceId} created with a folder panel of {Folder}", fence.Id, folder);
+        SyncBoxes();
+        ItemsChanged(checkTargets: [folder]);
+        SaveNow();
+    }
+
+    private static string? PickPanelFolder(nint ownerHandle) =>
+        PathPicker.TryPickFolder(ownerHandle, "Choose a folder to show in a fence", failure => Log.Warning(failure, "folder panel: the folder dialog failed"));
+
+    /// <summary>Panel menu → "Panel settings…": the folder and what it shows (the sort is the headers' and Sort by's).</summary>
+    private void EditPanel(FenceWindow window, string itemId)
+    {
+        if (_items.Find(itemId) is not { Panel: { } current } item) return;
+        var dialog = new FolderViewWindow(item.Target, current) { Owner = window };
+        if (dialog.ShowDialog() != true || dialog.Result is not { } result) return;
+        // A tray restore while the dialog was open may have removed the panel (final review M5 of M21).
+        if (_items.Find(itemId) is not { Panel: not null } now)
+        {
+            Log.Information("folder panel {ItemId} changed while its settings were open; the new settings are not applied", itemId);
+            return;
+        }
+        _items = ItemEdits.Replace(_items, now with { Target = result.Folder, Panel = result.Panel });
+        Log.Information("folder panel {ItemId} settings changed ({Folder})", itemId, result.Folder);
+        ItemsChanged(checkTargets: [result.Folder]);
+    }
+
+    /// <summary>
+    /// The folder views of an older version become fences holding one filling panel (spec §1) — a snapshot first; items are
+    /// saved before the config, so a cut-short save only repeats the migration (it adds no second panel).
+    /// </summary>
+    private bool MigrateFolderViews()
+    {
+        if (!_config.Fences.Any(fence => fence.View is not null)) return false;
+        if (_itemsReadOnly)
+        {
+            Log.Information("folder views not made into panels: items.json is read-only this session"); // the config must not lose the view
+            return false;
+        }
+        var now = DateTimeOffset.Now;
+        if (_snapshots.Save(Snapshots.Take(_config, _items, name: $"Before folder views became panels ({now:d MMM HH:mm})", now: now)) is null)
+        {
+            Log.Warning(_snapshots.LastFailure, "folder views not made into panels: the snapshot before it could not be saved; tried again next time");
+            return false;
+        }
+        var migration = FolderPanels.MigrateViews(_config, _items);
+        (_config, _items) = (migration.Config, migration.Items);
+        Log.Information("folder view fence(s) {FenceIds} now hold a folder panel each", migration.MigratedFenceIds);
+        SaveNow(itemsFirst: true);
+        return true;
+    }
+
+    /// <summary>
+    /// Right-click on a panel's entries: NeoFences' safe menu (open, show, copy, add to a fence). Shift+right-click: Windows'
+    /// menu for the real entries, under a line saying so — only there can a real rename or delete happen.
+    /// </summary>
+    private void ShowEntryMenu(FenceWindow window, IReadOnlyList<string> paths, bool extended, int screenX, int screenY, bool fromKeyboard)
+    {
+        if (paths.Count == 0) return;
+        if (extended)
+        {
+            ShowEntryWindowsMenu(window, paths, screenX, screenY);
+            return;
+        }
+        var menu = new ContextMenu();
+        void Command(ItemsControl parent, string header, Action run)
+        {
+            var command = new MenuItem { Header = header };
+            command.Click += (_, _) => run();
+            parent.Items.Add(command);
+        }
+        Command(menu, "Open", () => { foreach (var path in paths) OpenItem(path, ownerHandle: window.Handle); });
+        if (paths.Count == 1) Command(menu, "Open file location", () => ShowInFolder(paths[0]));
+        Command(menu, paths.Count == 1 ? "Copy path" : "Copy paths", () => CopyText(string.Join(Environment.NewLine, paths)));
+        var itemFences = _config.Fences.Where(fence => fence.Kind == FenceKind.Items).ToList();
+        var addTo = new MenuItem { Header = "Add to fence", IsEnabled = itemFences.Count > 0 };
+        foreach (var fence in itemFences) Command(addTo, fence.Title.Length > 0 ? fence.Title : "(untitled fence)", () => AddToFence(fence.Id, paths));
+        menu.Items.Add(addTo);
+        menu.Items.Add(new Separator());
+        menu.Items.Add(new MenuItem { Header = "Shift+right-click: Windows' menu", IsEnabled = false });
+        window.ShowItemMenu(menu, fromKeyboard);
+    }
+
+    /// <summary>Windows' menu for a panel's entries, after a 2 s check of the folder off the UI thread (a share may have gone, M19 R2).</summary>
+    private void ShowEntryWindowsMenu(FenceWindow window, IReadOnlyList<string> paths, int screenX, int screenY)
+    {
+        var folder = Path.GetDirectoryName(paths[0]) ?? paths[0];
+        Task.Run(() => TargetProbe.Check(folder)).ContinueWith(checking =>
+        {
+            if (checking.IsFaulted)
+            {
+                Log.Warning(checking.Exception, "Windows' menu: {Folder} could not be checked", folder);
+                return;
+            }
+            if (checking.Result.State != TargetState.Ok)
+            {
+                var menu = new ContextMenu();
+                menu.Items.Add(new MenuItem { Header = TargetChecks.IsNetworkPath(folder) ? "Network location not reachable" : "Drive not connected", IsEnabled = false });
+                window.ShowItemMenu(menu, fromKeyboard: false);
+                return;
+            }
+            ShellItemMenu.Show(window.Handle, paths, screenX, screenY, extended: true,
+                logFailure: failure => Log.Warning(failure, "Windows' menu or its command failed for {Paths}", paths),
+                header: "Windows menu — acts on the real files", customCommands: [], handDeleteBack: false, out _);
+        }, TaskScheduler.FromCurrentSynchronizationContext());
+    }
+
+    /// <summary>A panel's entries dragged out: other apps get copies, fences get items (asked once now, at most 2 s, M19 R2).</summary>
+    private void DragEntries(FenceWindow window, IReadOnlyList<string> paths)
+    {
+        var folder = Path.GetDirectoryName(paths[0]) ?? paths[0];
+        if (TargetProbe.Check(folder).State != TargetState.Ok)
+        {
+            Log.Information("drag from folder panel: {Folder} is not reachable", folder);
+            return;
+        }
+        ShellDragDrop.TryDrag(window.Handle, paths, paths, [], logFailure: failure => Log.Warning(failure, "could not start dragging {Paths}", paths));
+    }
+
+    /// <summary>"Add to fence ▸": the entries become virtual items at the end of that fence, which then shows them selected.</summary>
+    private void AddToFence(string fenceId, IReadOnlyList<string> paths)
+    {
+        if (!_config.Fences.Any(fence => fence.Id == fenceId && fence.Kind == FenceKind.Items)) return;
+        var added = ItemEdits.Add(_items, fenceId, [.. paths.Select(VirtualItem.Create)]);
+        _items = added.Document;
+        Log.Information("{Added} item(s) added to fence {FenceId} from a folder panel; {Already} already there", added.AddedIds.Count, fenceId, added.AlreadyThereIds.Count);
+        ItemsChanged(checkTargets: paths);
+        if (_windows.Values.FirstOrDefault(window => window.FenceId == fenceId) is { } shown) shown.SelectItems([.. added.AddedIds, .. added.AlreadyThereIds]);
+    }
+
+    /// <summary>Downloads and Screenshots, asked once (a local call; a missing one is logged and left out).</summary>
+    private IReadOnlyList<string> BusyFolders => _busyFolders ??= KnownFolders.BusyFolders(failure => Log.Information(failure, "folder panel: a known folder is not available"));
+}
diff --git a/src/NeoFences.App/FenceHost.FolderViews.cs b/src/NeoFences.App/FenceHost.FolderViews.cs
deleted file mode 100644
index c6f11cd..0000000
--- a/src/NeoFences.App/FenceHost.FolderViews.cs
+++ /dev/null
@@ -1,257 +0,0 @@
-using System.Windows.Controls;
-using NeoFences.Core.Items;
-using NeoFences.Core.Model;
-using NeoFences.Shell;
-using Serilog;
-
-namespace NeoFences.App;
-
-/// <summary>
-/// Folder views (M21, spec 2026-10-05-folder-views-design, ADR-044): fences that show one folder live, read-only. One
-/// <see cref="FolderLister"/> per view (a hidden tab keeps listing); the entries are paths, like the Game Library's.
-/// NeoFences never writes to the folder: no drop into it, no rename or delete from NeoFences' own menu.
-/// </summary>
-public sealed partial class FenceHost
-{
-    private readonly Dictionary<string, FolderLister> _viewListers = new(StringComparer.Ordinal);
-    // The last listing per view (null: not readable); missing while the first listing is on its way.
-    private readonly Dictionary<string, IReadOnlyList<ItemInfo>?> _viewListings = new(StringComparer.Ordinal);
-    // The selection made off the UI thread for the view's settings at the time, and the newest request per view (M23).
-    private readonly Dictionary<string, (FolderView View, FolderViews.Selection? Selection)> _viewSelections = new(StringComparer.Ordinal);
-    private readonly Dictionary<string, int> _viewSelectionRequests = new(StringComparer.Ordinal);
-    private IReadOnlyList<string>? _busyFolders;
-
-    /// <summary>A lister for every view fence, on its current folder; listers of views that are gone (or changed folder) go.</summary>
-    private void EnsureViewListers()
-    {
-        var views = _config.Fences.Where(fence => fence.View is not null).ToDictionary(fence => fence.Id, fence => fence.View!.Path, StringComparer.Ordinal);
-        foreach (var (fenceId, lister) in _viewListers.ToList())
-        {
-            if (views.TryGetValue(fenceId, out var path) && FolderViews.SameFolder(path, lister.Folder)) continue;
-            lister.Dispose(); // the folder stays as it is
-            _viewListers.Remove(fenceId);
-            _viewListings.Remove(fenceId);
-            _viewSelections.Remove(fenceId);
-        }
-        foreach (var (fenceId, path) in views.Where(view => !_viewListers.ContainsKey(view.Key)))
-        {
-            var lister = new FolderLister(path, noticeOwner: _messages.Handle, label: "folder view",
-                show: listed => ShowView(fenceId, listed),
-                logFailure: failure => Log.Warning(failure, "folder view: cannot watch {Folder}", path),
-                renamed: (oldPath, newPath) => OnViewFolderRenamed(fenceId, oldPath, newPath));
-            if (_gameMode) lister.SetPaused(true);
-            _viewListers[fenceId] = lister;
-        }
-    }
-
-    private void StopViewListers()
-    {
-        foreach (var lister in _viewListers.Values) lister.Dispose();
-        _viewListers.Clear();
-        _viewListings.Clear();
-        _viewSelections.Clear();
-    }
-
-    /// <summary>A listing arrived (on the UI thread): kept, and shown when the view is the shown tab of its box.</summary>
-    private void ShowView(string fenceId, IReadOnlyList<ItemInfo>? listed)
-    {
-        if (!_viewListers.ContainsKey(fenceId)) return; // its fence went meanwhile
-        var wasAvailable = !_viewListings.TryGetValue(fenceId, out var before) || before is not null;
-        _viewListings[fenceId] = listed;
-        if (listed is null && wasAvailable && _config.Fences.FirstOrDefault(fence => fence.Id == fenceId)?.View is { } view)
-            Log.Information("folder view: {Folder} is not available", view.Path); // once per outage, not every 7 s retry
-        if (_config.Fences.FirstOrDefault(fence => fence.Id == fenceId)?.View is not { } settings) return;
-        // Filtered and sorted off the UI thread (M23): 20,000 entries sorted by name take ~140 ms, at every re-list.
-        var request = _viewSelectionRequests[fenceId] = _viewSelectionRequests.GetValueOrDefault(fenceId) + 1;
-        Task.Run(() => listed is null ? null : FolderViews.Select(listed, settings)).ContinueWith(selecting =>
-        {
-            if (selecting.IsFaulted)
-            {
-                Log.Warning(selecting.Exception, "folder view {FenceId}: the listing could not be sorted", fenceId);
-                return;
-            }
-            if (!_viewListers.ContainsKey(fenceId) || _viewSelectionRequests.GetValueOrDefault(fenceId) != request) return; // gone, or a newer listing
-            _viewSelections[fenceId] = (settings, selecting.Result);
-            if (_windows.Values.FirstOrDefault(window => window.FenceId == fenceId) is { } shown) RefreshWindow(shown);
-        }, TaskScheduler.FromCurrentSynchronizationContext());
-    }
-
-    /// <summary>The view's entries as its settings select them, and its status line (RefreshWindow calls this for views).</summary>
-    private void RenderView(FenceWindow window, Fence fence)
-    {
-        if (!_viewListings.TryGetValue(fence.Id, out var listed))
-        {
-            window.SetItems([]); // the first listing is on its way (a slow share): nothing yet, no status
-            window.SetViewStatus(center: null, more: null);
-            return;
-        }
-        // The background selection when it matches the settings; after a settings change, once here (a user action).
-        if (!_viewSelections.TryGetValue(fence.Id, out var cached) || cached.View != fence.View)
-        {
-            cached = (fence.View!, listed is null ? null : FolderViews.Select(listed, fence.View!));
-            _viewSelections[fence.Id] = cached;
-        }
-        var selection = cached.Selection;
-        window.SetItems(selection is null ? [] : [.. selection.Shown.Select(path => new ShownItem(path, path))]);
-        var (center, more) = FolderViews.Status(selection, fence.View!);
-        window.SetViewStatus(center, more);
-    }
-
-    private void SetViewsPaused(bool paused)
-    {
-        foreach (var lister in _viewListers.Values) lister.SetPaused(paused);
-    }
-
-    /// <summary>Windows asks to remove a drive a view shows (a USB stick): that view lets go and says "not available".</summary>
-    private bool ReleaseViewsForRemoval(nint handle) => _viewListers.Values.Aggregate(false, (released, lister) => lister.ReleaseForRemoval(handle) | released);
-
-    /// <summary>Tray or fence menu → "New folder view…": Windows' folder dialog, then the settings with busy-folder defaults.</summary>
-    private void NewFolderView(nint ownerHandle)
-    {
-        var picked = PathPicker.TryPickFolder(ownerHandle, "Choose a folder to show in a fence",
-            failure => Log.Warning(failure, "folder view: the folder dialog failed"));
-        if (picked is null) return;
-        var dialog = new FolderViewWindow(FolderViews.DefaultsFor(picked, BusyFolders));
-        if (dialog.ShowDialog() != true || dialog.Result is not { } view) return;
-        SetQuickHidden(false); // a new fence must show
-        (_config, var fence) = FenceEdits.CreateView(_config, view);
-        Log.Information("folder view {FenceId} created for {Folder}", fence.Id, view.Path);
-        SyncBoxes();
-        SaveNow();
-    }
-
-    /// <summary>
-    /// Folder item menu → "Show as folder view": a view of that folder with the defaults, placed in free space like a new
-    /// fence (live check: a fixed offset from the fence stacked every view made from it on the same spot).
-    /// </summary>
-    private void ShowAsFolderView(string folder)
-    {
-        SetQuickHidden(false); // a new fence must show
-        (_config, var fence) = FenceEdits.CreateView(_config, FolderViews.DefaultsFor(folder, BusyFolders));
-        Log.Information("folder view {FenceId} created for {Folder} from an item", fence.Id, folder);
-        SyncBoxes();
-        SaveNow();
-    }
-
-    /// <summary>View fence menu → "Folder view settings…".</summary>
-    private void EditFolderView(FenceWindow window)
-    {
-        var fenceId = window.FenceId; // the window may show another tab by the time the dialog closes
-        if (_config.Fences.FirstOrDefault(fence => fence.Id == fenceId)?.View is not { } current) return;
-        var dialog = new FolderViewWindow(current) { Owner = window };
-        if (dialog.ShowDialog() != true || dialog.Result is not { } view) return;
-        // A tray restore while the dialog was open may have removed the view or made it another kind (final review M5).
-        if (_config.Fences.FirstOrDefault(fence => fence.Id == fenceId)?.View is null)
-        {
-            Log.Information("folder view {FenceId} changed while its settings were open; the new settings are not applied", fenceId);
-            return;
-        }
-        SetView(fenceId, view);
-    }
-
-    /// <summary>A view's new settings: saved, its lister on the (new) folder, its window, title and menus updated.</summary>
-    private void SetView(string fenceId, FolderView view)
-    {
-        _config = FenceEdits.SetView(_config, fenceId, view);
-        EnsureViewListers();
-        // Its tab header too when the view is a hidden tab of a box (M23).
-        if (FenceTabs.HostOf(_config, fenceId) is { } host && _windows.TryGetValue(host.Id, out var boxWindow) && boxWindow.FenceId != fenceId) RefreshTabs(boxWindow);
-        if (_windows.Values.FirstOrDefault(window => window.FenceId == fenceId) is { } window && _config.Fences.First(fence => fence.Id == fenceId) is var fence)
-        {
-            window.Refresh(fence);
-            window.SetTitle(fence.Title);
-            RefreshTabs(window);
-            RefreshWindow(window);
-        }
-        ScheduleSave();
-    }
-
-    /// <summary>"Sort by" on a view: its own sort, kept and live (M21), not the one-time reorder of items.</summary>
-    private void SortView(FenceWindow window, FenceSort sort)
-    {
-        if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId)?.View is { } view) SetView(window.FenceId, view with { Sort = sort });
-    }
-
-    /// <summary>The view's folder was renamed in place (Explorer): the view follows it, its title too while it is the folder's name.</summary>
-    private void OnViewFolderRenamed(string fenceId, string oldPath, string newPath)
-    {
-        if (_config.Fences.FirstOrDefault(fence => fence.Id == fenceId)?.View is not { } view || !FolderViews.SameFolder(view.Path, oldPath)) return;
-        Log.Information("folder view {FenceId}: {OldPath} renamed to {NewPath}; following it", fenceId, oldPath, newPath);
-        SetView(fenceId, view with { Path = newPath });
-    }
-
-    /// <summary>"Open folder" (the view's menu or its "+ N more" line): Explorer at the view's folder.</summary>
-    private void OpenViewFolder(FenceWindow window)
-    {
-        if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId)?.View is { } view) OpenItem(view.Path, ownerHandle: window.Handle);
-    }
-
-    /// <summary>
-    /// Right-click on a view's entries: NeoFences' safe menu (open, show, copy, add to a fence). Shift+right-click: Windows'
-    /// menu for the real entries, under a line saying so — only there can a real rename or delete happen.
-    /// </summary>
-    private void ShowViewItemMenu(FenceWindow window, IReadOnlyList<string> paths, bool extended, int screenX, int screenY, bool fromKeyboard)
-    {
-        if (paths.Count == 0) return;
-        if (extended)
-        {
-            ShowViewWindowsMenu(window, paths, screenX, screenY);
-            return;
-        }
-        var menu = new ContextMenu();
-        void Command(ItemsControl parent, string header, Action run)
-        {
-            var command = new MenuItem { Header = header };
-            command.Click += (_, _) => run();
-            parent.Items.Add(command);
-        }
-        Command(menu, "Open", () => { foreach (var path in paths) OpenItem(path, ownerHandle: window.Handle); });
-        if (paths.Count == 1) Command(menu, "Open file location", () => ShowInFolder(paths[0]));
-        Command(menu, paths.Count == 1 ? "Copy path" : "Copy paths", () => CopyText(string.Join(Environment.NewLine, paths)));
-        var itemFences = _config.Fences.Where(fence => fence.Kind == FenceKind.Items).ToList();
-        var addTo = new MenuItem { Header = "Add to fence", IsEnabled = itemFences.Count > 0 };
-        foreach (var fence in itemFences) Command(addTo, fence.Title.Length > 0 ? fence.Title : "(untitled fence)", () => AddToFence(fence.Id, paths));
-        menu.Items.Add(addTo);
-        menu.Items.Add(new Separator());
-        menu.Items.Add(new MenuItem { Header = "Shift+right-click: Windows' menu", IsEnabled = false });
-        window.ShowItemMenu(menu, fromKeyboard);
-    }
-
-    /// <summary>Windows' menu for a view's entries, after a 2 s check of the folder off the UI thread (a share may have gone, M19 R2).</summary>
-    private void ShowViewWindowsMenu(FenceWindow window, IReadOnlyList<string> paths, int screenX, int screenY)
-    {
-        var folder = System.IO.Path.GetDirectoryName(paths[0]) ?? paths[0];
-        Task.Run(() => TargetProbe.Check(folder)).ContinueWith(checking =>
-        {
-            if (checking.IsFaulted)
-            {
-                Log.Warning(checking.Exception, "Windows' menu: {Folder} could not be checked", folder);
-                return;
-            }
-            if (checking.Result.State != TargetState.Ok)
-            {
-                var menu = new ContextMenu();
-                menu.Items.Add(new MenuItem { Header = TargetChecks.IsNetworkPath(folder) ? "Network location not reachable" : "Drive not connected", IsEnabled = false });
-                window.ShowItemMenu(menu, fromKeyboard: false);
-                return;
-            }
-            ShellItemMenu.Show(window.Handle, paths, screenX, screenY, extended: true,
-                logFailure: failure => Log.Warning(failure, "Windows' menu or its command failed for {Paths}", paths),
-                header: "Windows menu — acts on the real files", customCommands: [], handDeleteBack: false, out _);
-        }, TaskScheduler.FromCurrentSynchronizationContext());
-    }
-
-    /// <summary>"Add to fence ▸": the entries become virtual items at the end of that fence, which then shows them selected.</summary>
-    private void AddToFence(string fenceId, IReadOnlyList<string> paths)
-    {
-        if (!_config.Fences.Any(fence => fence.Id == fenceId && fence.Kind == FenceKind.Items)) return;
-        var added = ItemEdits.Add(_items, fenceId, [.. paths.Select(VirtualItem.Create)]);
-        _items = added.Document;
-        Log.Information("{Added} item(s) added to fence {FenceId} from a folder view; {Already} already there", added.AddedIds.Count, fenceId, added.AlreadyThereIds.Count);
-        ItemsChanged(checkTargets: paths);
-        if (_windows.Values.FirstOrDefault(window => window.FenceId == fenceId) is { } shown) shown.SelectItems([.. added.AddedIds, .. added.AlreadyThereIds]);
-    }
-
-    /// <summary>Downloads and Screenshots, asked once (a local call; a missing one is logged and left out).</summary>
-    private IReadOnlyList<string> BusyFolders => _busyFolders ??= KnownFolders.BusyFolders(failure => Log.Information(failure, "folder view: a known folder is not available"));
-}
diff --git a/src/NeoFences.App/FenceHost.Items.cs b/src/NeoFences.App/FenceHost.Items.cs
index f0cfb37..b62c340 100644
--- a/src/NeoFences.App/FenceHost.Items.cs
+++ b/src/NeoFences.App/FenceHost.Items.cs
@@ -92,11 +92,6 @@ public sealed partial class FenceHost
             ShowLibraryItemMenu(window, keys, screenX, screenY, extended: shift);
             return;
         }
-        if (window.Kind == FenceKind.View)
-        {
-            ShowViewItemMenu(window, keys, extended: shift, screenX, screenY, fromKeyboard); // M21
-            return;
-        }
         var items = keys.Select(_items.Find).OfType<VirtualItem>().ToList();
         if (items.Count == 0) return;
         if (shift)
@@ -117,6 +112,11 @@ public sealed partial class FenceHost
             menu.Items.Add(SizeMenu(menu, items)); // M24
             Command($"Remove {items.Count} items from fence", () => RemoveItems(window, [.. items.Select(item => item.Id)]));
         }
+        else if (FolderPanels.IsPanel(items[0]))
+        {
+            ShowPanelMenu(window, items[0], fromKeyboard); // M26
+            return;
+        }
         else if (items[0].Kind == ItemKind.Widget)
         {
             ShowWidgetMenu(window, items[0], fromKeyboard); // M25
@@ -141,7 +141,7 @@ public sealed partial class FenceHost
             Command("Open", () => OpenVirtualItem(window, item, runAsAdmin: item.RunAsAdmin));
             if (onDisk && !check.IsFolder) Command("Run as administrator", () => OpenVirtualItem(window, item, runAsAdmin: true));
             if (onDisk) Command("Open file location", () => ShowInFolder(item.Target));
-            if (onDisk && check.IsFolder && check.State == TargetState.Ok) Command("Show as folder view", () => ShowAsFolderView(item.Target)); // M21
+            if (onDisk && check.IsFolder && check.State == TargetState.Ok) Command("Show as folder panel", () => SetPanelShown(item.Id, FolderPanels.Create(item.Target, BusyFolders).Panel)); // M26
             Command("Copy path", () => CopyText(item.Target));
             menu.Items.Add(new Separator());
             menu.Items.Add(SizeMenu(menu, [item])); // M24
@@ -477,17 +477,6 @@ public sealed partial class FenceHost
         {
             (files, urls) = (keys, []); // NeoFences' own shortcuts (M12)
         }
-        else if (window.Kind == FenceKind.View)
-        {
-            // A view's entries are its folder's: asked once now, at most 2 s (M19 R2), so a share gone since the listing cannot freeze the drag.
-            var folder = Path.GetDirectoryName(keys[0]) ?? keys[0];
-            if (TargetProbe.Check(folder).State != TargetState.Ok)
-            {
-                Log.Information("drag from folder view: {Folder} is not reachable", folder);
-                return;
-            }
-            (files, urls) = (keys, []);
-        }
         else
         {
             var items = keys.Select(_items.Find).OfType<VirtualItem>().ToList();
@@ -508,12 +497,7 @@ public sealed partial class FenceHost
     /// <summary>"Sort by" (one time): by the names shown, type or date; dragging keeps working afterwards.</summary>
     private void SortFence(FenceWindow window, FenceSort sort)
     {
-        if (window.IsLibrary) return;
-        if (window.Kind == FenceKind.View)
-        {
-            SortView(window, sort); // M21: the view's own sort, kept
-            return;
-        }
+        if (window.Kind != FenceKind.Items) return;
         var fenceId = window.FenceId;
         var items = _items.Of(fenceId);
         // Targets not seen OK are sorted without asking their disk (a dead share answers only after its timeout).
diff --git a/src/NeoFences.App/FenceHost.Watching.cs b/src/NeoFences.App/FenceHost.Watching.cs
index 70dde2e..538093c 100644
--- a/src/NeoFences.App/FenceHost.Watching.cs
+++ b/src/NeoFences.App/FenceHost.Watching.cs
@@ -255,7 +255,10 @@ public sealed partial class FenceHost
             ScanLibrary(full: true);
             return;
         }
-        if (_viewListers.TryGetValue(window.FenceId, out var viewLister)) viewLister.Refresh(); // a folder view lists again (M21)
+        foreach (var panel in _items.Of(window.FenceId).Where(FolderPanels.IsPanel))
+        {
+            if (_panelListers.TryGetValue(panel.Id, out var panelLister)) panelLister.Refresh(); // its panels list again (M21, M26)
+        }
         CheckFence(window.FenceId);
         window.ReloadIcons();
     }
diff --git a/src/NeoFences.App/FenceHost.cs b/src/NeoFences.App/FenceHost.cs
index aab6fa2..54dc161 100644
--- a/src/NeoFences.App/FenceHost.cs
+++ b/src/NeoFences.App/FenceHost.cs
@@ -80,7 +80,7 @@ public sealed partial class FenceHost
     private const int TrayNewLibrary = 11; // M12
     private const int TraySnapshotsSettings = 12; // M13c: "More in Settings…" opens the Snapshots card
     private const int TrayAddFromDesktop = 14; // M19 §3 (13 is TrayRestartToUpdate)
-    private const int TrayNewFolderView = 15; // M21
+    private const int TrayNewFolderPanel = 15; // M21 (folder views), M26 (folder panels)
     private SettingsWindow? _settingsWindow; // M6b: one at a time
 
     public event Action? ExitRequested;
@@ -108,7 +108,7 @@ public sealed partial class FenceHost
         {
             if (_libraryLister?.ReleaseForRemoval(handle) == true) Log.Information("the library folder's drive is being removed: released it");
             if (ReleaseLibraryForRemoval(handle)) Log.Information("a drive the game library watches is being removed: released it");
-            if (ReleaseViewsForRemoval(handle)) Log.Information("a drive a folder view shows is being removed: released it");
+            if (ReleasePanelsForRemoval(handle)) Log.Information("a drive a folder panel shows is being removed: released it");
             if (ReleaseTargetWatcherForRemoval(handle)) Log.Information("a drive holding item targets is being removed: released it");
         };
         _specialIconsTimer.Tick += (_, _) => RefreshSpecialIcons();
@@ -127,6 +127,7 @@ public sealed partial class FenceHost
         _items = loadedItems.Document;
         _itemsReadOnly = loadedItems.IsReadOnly;
         MigrateGames(LibraryWriter.ReadIndex(AppPaths.LibraryDirectory)); // M22: an old Game Library fence becomes game items (a snapshot first)
+        MigrateFolderViews(); // M26: an old folder view becomes a fence holding one panel (a snapshot first)
         if (loadedItems.Source == ConfigLoadSource.Primary && !loadedItems.IsReadOnly) CleanUnusedPictures(ItemEdits.ImagesInUse(_items), _snapshots.Directory);
         _watchdog.LaunchDetached(Environment.ProcessId);
         ApplyStartup(); // after a power loss NeoFences must come back by itself (ADR-019)
@@ -134,8 +135,7 @@ public sealed partial class FenceHost
         RefreshMonitors();
         foreach (var box in FenceTabs.Boxes(_config)) OpenWindow(box); // one window per box (M9)
         EnsureLibraryLister();
-        EnsureViewListers(); // M21
-        RefreshWindows();
+        RefreshWindows(); // M26: panels list their folders from here
         StartSpecialIconNotifications();
         ApplyLayout();
         if (_config.Settings.HideDesktopIcons) SetIconsHidden(true);
@@ -209,7 +209,7 @@ public sealed partial class FenceHost
         StopWatching();
         _specialIcons?.Dispose();
         _libraryLister?.Dispose();
-        StopViewListers();
+        StopPanelListers(); // M26
         _widgetTimer?.Stop(); // M25
         _systemStats?.Dispose();
         StopLibraryWatchers();
@@ -276,13 +276,13 @@ public sealed partial class FenceHost
         window.RefreshRequested += () => RefreshFence(window);
         window.DrivesChanged += OnDrivesChanged;
         window.NewLibraryRequested += CreateLibraryFence;
-        window.NewFolderViewRequested += () => NewFolderView(window.Handle); // M21
+        window.NewFolderPanelRequested += () => NewFolderPanel(window.Handle); // M26
+        window.AddFolderPanelRequested += () => AddFolderPanel(window);
+        window.PanelCommandRequested += (itemId, command) => OnPanelCommand(window, itemId, command);
         window.AddGamesRequested += () => AddGames(window); // M22
         window.LayoutRequested += layout => SetFenceLayout(window, layout); // M24
         window.AddWidgetRequested += kind => AddWidget(window, kind); // M25
         window.ItemsShownChanged += OnWidgetTick; // a rolled-up fence opened: its widgets show the right time at once
-        window.OpenFolderRequested += () => OpenViewFolder(window);
-        window.ViewSettingsRequested += () => EditFolderView(window);
         window.StartupToggled += SetStartWithWindows;
         window.SettingsRequested += OpenSettings;
         window.LabelModeRequested += labels => SetFenceLabels(window, labels);
@@ -336,6 +336,7 @@ public sealed partial class FenceHost
     /// <summary>Every window shows its fence's items as they are now (the library lists itself, FolderLister; views show their last listing).</summary>
     private void RefreshWindows()
     {
+        EnsurePanelListers(); // M26: a lister per panel, on the folder it shows
         foreach (var window in _windows.Values) RefreshWindow(window);
         UpdateWidgetTimer(); // M25: a timer only while some fence holds a widget
     }
@@ -343,16 +344,15 @@ public sealed partial class FenceHost
     private void RefreshWindow(FenceWindow window)
     {
         if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is not { IsLibrary: false } shown) return;
-        if (shown.View is not null)
-        {
-            RenderView(window, shown); // M21
-            return;
-        }
-        var covers = _items.Of(shown.Id).Any(GameItems.ShowsCover) ? LibraryArt() : null; // M22: game items shown as covers
-        window.SetItems([.. _items.Of(shown.Id).Select(item => new ShownItem(item.Id, item.Target, item.OwnName, item.Icon, item.Note, StateOf(item.Target),
+        var items = _items.Of(shown.Id);
+        var covers = items.Any(GameItems.ShowsCover) ? LibraryArt() : null; // M22: game items shown as covers
+        var fills = FolderPanels.Fills(items); // M26: a lone panel set to fill takes the whole fence
+        window.SetItems([.. items.Select(item => new ShownItem(item.Id, item.Target, item.OwnName, item.Icon, item.Note, StateOf(item.Target),
             Tile: GameItems.ShowsCover(item), TileArt: covers is not null && covers.TryGetValue(item.Target, out var cover) ? cover : null, IsGame: GameItems.IsGame(item),
-            Span: FenceGrid.SpanOf(item), Cell: item.Cell, Options: item.Widget))]); // M24, M25
+            Span: FenceGrid.SpanOf(item), Cell: item.Cell, Options: item.Widget, // M24, M25
+            Panel: FolderPanels.IsPanel(item) ? item.Panel : null, Fill: fills))]); // M26
         window.UpdateWidgets(DateTime.Now, _lastStats); // M25: a new widget shows its content at once
+        RenderPanels(window, items); // M26
     }
 
     /// <summary>Opens a path NeoFences knows (a library game, the logs or data folder).</summary>
@@ -374,7 +374,7 @@ public sealed partial class FenceHost
         {
             _dropRegistrations[window.BoxId] = ShellDragDrop.RegisterFence(window.Handle, new FenceDropHandlers(
                 HitTest: window.HitTest,
-                AcceptsDrops: () => window.Kind == FenceKind.Items, // the library shows its own shortcuts (M12); a view never writes to its folder (M21)
+                AcceptsDrops: () => window.Kind == FenceKind.Items && !window.DropOverPanel, // the library shows its own shortcuts (M12); a panel never writes to its folder (M21, M26)
                 ItemsDropped: (keys, insertAt, duplicate) => OnItemsDropped(window, keys, insertAt, duplicate),
                 TargetsDropped: (targets, insertAt) => OnTargetsDropped(window, targets, insertAt),
                 ShowFeedback: window.ShowDropFeedback,
@@ -561,7 +561,6 @@ public sealed partial class FenceHost
         }
         foreach (var box in boxes.Where(box => !_windows.ContainsKey(box.Id))) OpenWindow(box);
         EnsureLibraryLister();
-        EnsureViewListers(); // M21
         foreach (var box in boxes)
         {
             var window = _windows[box.Id];
@@ -973,6 +972,7 @@ public sealed partial class FenceHost
         Log.Information("restoring snapshot {Name} from {Path}", snapshot.Name, path);
         (_config, _items) = Snapshots.Restore(_config, snapshot);
         MigrateGames(_library.Items.Count > 0 ? _library : LibraryWriter.ReadIndex(AppPaths.LibraryDirectory)); // M22: a snapshot from before games became items
+        MigrateFolderViews(); // M26: a snapshot from before folder views became panels
         SaveNow();
         SyncBoxes();
         // Windows that kept their fence still show its old title, icon size and labels (final review I1).
@@ -982,7 +982,7 @@ public sealed partial class FenceHost
             window.Refresh(shown);
             window.SetTitle(shown.Title);
         }
-        RefreshWindows(); // a fence that became (or stopped being) a folder view shows its new kind (M21)
+        RefreshWindows();
         UpdateLibrary(); // M22: the restored fences may hold game items, or none
         ForgetGoneTargets(); // records of items the restore took away (M20)
         CheckAllTargets(); // the restored items' targets may have changed since
@@ -1130,7 +1130,7 @@ public sealed partial class FenceHost
         }
         UpdateMouseHook();
         _libraryLister?.SetPaused(gameMode);
-        SetViewsPaused(gameMode); // M21
+        SetPanelsPaused(gameMode); // M21, M26
         if (!gameMode) ApplyDeferredShellWork();
         UpdatePeekHotkey();
         _trayIcon?.SetTooltip(TrayTooltip());
@@ -1207,7 +1207,7 @@ public sealed partial class FenceHost
         [
             .. UpdateTrayItems(), // M17: "Restart to update to v…" first while an update waits
             new TrayMenuItem(TrayNewFence, "New fence", Enabled: !_paused),
-            new TrayMenuItem(TrayNewFolderView, "New folder view…", Enabled: !_paused),
+            new TrayMenuItem(TrayNewFolderPanel, "New folder panel…", Enabled: !_paused),
             new TrayMenuItem(TrayAddFromDesktop, "Add from desktop…", Enabled: !_paused),
             new TrayMenuItem(TrayQuickHide, "Quick-hide", Checked: _quickHidden, Enabled: !_paused),
             new TrayMenuItem(TrayPeek, $"Peek\t{PeekHotkeyDisplay}", Checked: _peeking, Enabled: !_paused),
@@ -1227,7 +1227,7 @@ public sealed partial class FenceHost
                 CreateFence();
                 break;
             case TrayNewLibrary: CreateLibraryFence(); break;
-            case TrayNewFolderView: NewFolderView(ownerHandle: 0); break;
+            case TrayNewFolderPanel: NewFolderPanel(ownerHandle: 0); break;
             case TrayAddFromDesktop:
                 SetQuickHidden(false); // the new items must be seen landing
                 ShowDesktopFill();
diff --git a/src/NeoFences.App/FenceItemView.cs b/src/NeoFences.App/FenceItemView.cs
index d41d9b4..b8ed44e 100644
--- a/src/NeoFences.App/FenceItemView.cs
+++ b/src/NeoFences.App/FenceItemView.cs
@@ -14,9 +14,10 @@ namespace NeoFences.App;
 /// <param name="IsGame">A game item (M22): "Not installed" instead of "Missing".</param>
 /// <param name="Span">Its size in cells (M24); null: 1×2 for a tile, else 1×1.</param>
 /// <param name="Cell">Its stored cell (M24, Free fences).</param>
+/// <param name="Panel">A folder shown as a panel (M26); <paramref name="Fill"/>: it takes the whole fence.</param>
 public sealed record ShownItem(string Key, string Target, string? Name = null, ItemIcon? Icon = null, string? Note = null,
     TargetState State = TargetState.Ok, bool Tile = false, (string Path, bool IsPoster)? TileArt = null, bool IsGame = false,
-    GridSpan? Span = null, GridCell? Cell = null, WidgetOptions? Options = null);
+    GridSpan? Span = null, GridCell? Cell = null, WidgetOptions? Options = null, FolderPanel? Panel = null, bool Fill = false);
 
 /// <summary>One item as a fence shows it. Label and icon start as placeholders and fill in from <see cref="IconLoader"/>.</summary>
 public sealed class FenceItemView : INotifyPropertyChanged
@@ -88,6 +89,13 @@ public sealed class FenceItemView : INotifyPropertyChanged
         Options = shown.Options ?? new WidgetOptions();
         Span = shown.Span ?? (shown.Tile ? new GridSpan(1, 2) : GridSpan.One);
         StoredCell = shown.Cell;
+        if (shown.Panel is { } panel) // M26
+        {
+            PanelModel ??= new FolderPanelModel(Key);
+            PanelModel.Apply(panel, Span);
+        }
+        else PanelModel = null;
+        Fills = shown.Fill;
         State = shown.State;
         if (OwnName is not null) Label = OwnName;
         else if (reload || Label.Length == 0) Label = PlaceholderName(Target);
@@ -108,6 +116,17 @@ public sealed class FenceItemView : INotifyPropertyChanged
         _ => Path.GetFileNameWithoutExtension(target.TrimEnd('\\')) is { Length: > 0 } name ? name : target,
     };
 
+    /// <summary>The folder panel this element shows (M26), or null.</summary>
+    public FolderPanelModel? PanelModel { get; private set { if (field == value) return; field = value; Changed(); Changed(nameof(IsPanel)); } }
+
+    public bool IsPanel => PanelModel is not null;
+
+    /// <summary>A panel that takes the whole fence (M26): the window sizes it to the fence instead of its span.</summary>
+    public bool Fills { get; private set; }
+
+    /// <summary>A filling panel's size: the fence's list area less the cell padding (M26).</summary>
+    public void ApplyFill(double width, double height) => (ContentWidth, WidgetWidth, WidgetHeight) = (width, width, height);
+
     /// <summary>The widget this element is (M25), or null for an item.</summary>
     public WidgetKind? Widget { get; private set { field = value; Changed(); Changed(nameof(IsWidget)); Changed(nameof(WidgetKindName)); } }
 
@@ -178,7 +197,7 @@ public sealed class FenceItemView : INotifyPropertyChanged
         var height = Span.Rows * cellHeight - CellPaddingY - labelHeight;
         var icon = Span == GridSpan.One ? iconDips : Math.Clamp(Math.Floor(Math.Min(width - 8, height)), iconDips, MaxIcon);
         var tileHeight = Math.Max(16, Math.Floor(Math.Min(height, (width - 4) * 1.5)));
-        (WidgetWidth, WidgetHeight) = (width, Span.Rows * cellHeight - CellPaddingY); // M25: no label under a widget
+        (WidgetWidth, WidgetHeight) = (width, Span.Rows * cellHeight - CellPaddingY); // M25: no label under a widget (M26: a panel's area too)
         var changed = Math.Abs(icon - IconDips) > 0.5;
         (ContentWidth, IconDips, TileWidth, TileHeight) = (width, icon, Math.Floor(tileHeight / 1.5), tileHeight);
         return changed;
diff --git a/src/NeoFences.App/FenceWindow.xaml b/src/NeoFences.App/FenceWindow.xaml
index 40d00a6..989dbc9 100644
--- a/src/NeoFences.App/FenceWindow.xaml
+++ b/src/NeoFences.App/FenceWindow.xaml
@@ -89,15 +89,15 @@
                             <MenuItem x:Name="AddDateItem" Header="Date" />
                             <MenuItem x:Name="AddStatsItem" Header="System stats" />
                         </MenuItem>
+                        <!-- M26: a folder inside the fence, beside its other elements. -->
+                        <MenuItem x:Name="AddFolderPanelItem" Header="Add folder panel…" />
                         <MenuItem x:Name="RefreshItem" Header="Refresh" />
-                        <!-- Folder views (M21): shown only on a view. -->
-                        <MenuItem x:Name="OpenFolderItem" Header="Open folder" Visibility="Collapsed" />
-                        <MenuItem x:Name="ViewSettingsItem" Header="Folder view settings…" Visibility="Collapsed" />
                         <Separator />
                         <MenuItem x:Name="NewFenceItem" Header="New fence" />
                         <!-- M22: games are items in any fence; the library fence kind stays in the code, not in the menu. -->
                         <MenuItem x:Name="NewLibraryItem" Header="New Game Library fence" Visibility="Collapsed" />
-                        <MenuItem x:Name="NewFolderViewItem" Header="New folder view…" />
+                        <!-- M26: a new fence holding one panel that fills it (folder views became panels). -->
+                        <MenuItem x:Name="NewFolderPanelItem" Header="New folder panel…" />
                         <MenuItem x:Name="RenameItem" Header="Rename fence" />
                         <MenuItem x:Name="IconSizeItem" Header="Icon size" />
                         <MenuItem x:Name="LabelsItem" Header="Labels">
@@ -160,10 +160,20 @@
                                                 <Trigger Property="IsSelected" Value="True">
                                                     <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceSelected}" />
                                                 </Trigger>
+                                                <!-- M26: a panel's rows show hover and selection themselves; the whole panel never tints (last: it wins). -->
+                                                <DataTrigger Binding="{Binding IsPanel}" Value="True">
+                                                    <Setter TargetName="Chrome" Property="Background" Value="Transparent" />
+                                                </DataTrigger>
                                             </ControlTemplate.Triggers>
                                         </ControlTemplate>
                                     </Setter.Value>
                                 </Setter>
+                                <Style.Triggers>
+                                    <!-- M26: a panel's entries have their own tooltips (their paths). -->
+                                    <DataTrigger Binding="{Binding IsPanel}" Value="True">
+                                        <Setter Property="ToolTipService.IsEnabled" Value="False" />
+                                    </DataTrigger>
+                                </Style.Triggers>
                             </Style>
                         </ListBox.ItemContainerStyle>
                         <ListBox.ItemTemplate>
@@ -247,6 +257,10 @@
                                             </ItemsControl>
                                         </Viewbox>
                                     </Grid>
+                                    <!-- M26: a folder panel fills its span (or, alone and set to fill, the whole fence) and takes its own clicks. -->
+                                    <local:FolderPanelView x:Name="PanelView" Visibility="Collapsed" DataContext="{Binding PanelModel}"
+                                                           Width="{Binding DataContext.WidgetWidth, ElementName=Cell}"
+                                                           Height="{Binding DataContext.WidgetHeight, ElementName=Cell}" />
                                     <TextBlock x:Name="Label" Margin="0,4,0,0" Text="{Binding Label}" Foreground="{DynamicResource FenceText}" FontSize="12"
                                                Visibility="{DynamicResource LabelVisibility}"
                                                TextAlignment="Center" TextWrapping="Wrap" TextTrimming="CharacterEllipsis"
@@ -263,6 +277,12 @@
                                         <Setter TargetName="Label" Property="Visibility" Value="Collapsed" />
                                         <Setter TargetName="WidgetPanel" Property="Visibility" Value="Visible" />
                                     </DataTrigger>
+                                    <DataTrigger Binding="{Binding IsPanel}" Value="True">
+                                        <Setter TargetName="IconGrid" Property="Visibility" Value="Collapsed" />
+                                        <Setter TargetName="Tile" Property="Visibility" Value="Collapsed" />
+                                        <Setter TargetName="Label" Property="Visibility" Value="Collapsed" />
+                                        <Setter TargetName="PanelView" Property="Visibility" Value="Visible" />
+                                    </DataTrigger>
                                     <DataTrigger Binding="{Binding WidgetKindName}" Value="Clock">
                                         <Setter TargetName="ClockView" Property="Visibility" Value="Visible" />
                                     </DataTrigger>
@@ -302,14 +322,9 @@
                             </DataTemplate>
                         </ListBox.ItemTemplate>
                     </ListBox>
-                    <!-- An empty fence says how to fill it (M18 spec §5); a folder view says why it shows nothing (M21). -->
+                    <!-- An empty fence says how to fill it (M18 spec §5). -->
                     <TextBlock x:Name="EmptyHint" Visibility="Collapsed" Margin="12" TextWrapping="Wrap" TextAlignment="Center"
                                VerticalAlignment="Center" Foreground="{DynamicResource FenceSubtleText}" IsHitTestVisible="False" />
-                    <!-- A folder view past its cap (M21): "+ N more — Open folder", clickable. -->
-                    <Border x:Name="MoreLine" Grid.Row="1" Visibility="Collapsed" Padding="8,3,8,5" Cursor="Hand" Background="#01000000">
-                        <TextBlock x:Name="MoreText" Foreground="{DynamicResource FenceSubtleText}" FontSize="12" TextAlignment="Center"
-                                   TextDecorations="Underline" />
-                    </Border>
                     <!-- Rubber-band selection (M3b): drawn while dragging on empty space. -->
                     <Canvas IsHitTestVisible="False" ClipToBounds="True">
                         <Rectangle x:Name="SelectionBand" Visibility="Collapsed" Fill="{DynamicResource FenceHover}"
diff --git a/src/NeoFences.App/FenceWindow.xaml.cs b/src/NeoFences.App/FenceWindow.xaml.cs
index 2f76707..a3d6795 100644
--- a/src/NeoFences.App/FenceWindow.xaml.cs
+++ b/src/NeoFences.App/FenceWindow.xaml.cs
@@ -46,7 +46,6 @@ public partial class FenceWindow : Window
     private string _title = "";
     private FenceKind _kind; // the shown tab: items, the Game Library (M12: tiles, its own menu) or a folder view (M21: read-only)
     private const string ItemsHint = "Drop files, folders or links here — or right-click → Add item…";
-    private string? _viewStatus; // a folder view's line instead of entries: not available, empty, nothing matching (M21)
     private IReadOnlyDictionary<string, (string Path, bool IsPoster)> _libraryArt = new Dictionary<string, (string, bool)>();
     private DragTracker? _drag;
     private bool _locked;
@@ -155,12 +154,12 @@ public partial class FenceWindow : Window
     public event Action<FenceLayout>? LayoutRequested;
     /// <summary>Fence menu → "Add games…" (M22).</summary>
     public event Action? AddGamesRequested;
-    /// <summary>Fence menu → "New folder view…" (M21).</summary>
-    public event Action? NewFolderViewRequested;
-    /// <summary>A folder view's "Open folder" (its menu, or the "+ N more" line).</summary>
-    public event Action? OpenFolderRequested;
-    /// <summary>A folder view's "Folder view settings…".</summary>
-    public event Action? ViewSettingsRequested;
+    /// <summary>Fence menu → "New folder panel…" (M26; M21's "New folder view…").</summary>
+    public event Action? NewFolderPanelRequested;
+    /// <summary>Fence menu → "Add folder panel…" (M26).</summary>
+    public event Action? AddFolderPanelRequested;
+    /// <summary>A folder panel asks for something (M26): the panel item's key and what.</summary>
+    public event Action<string, PanelCommand>? PanelCommandRequested;
     /// <summary>A drive arrived or was removed (Windows tells top-level windows): missing and unavailable items are checked again.</summary>
     public event Action? DrivesChanged;
     /// <summary>The "Start with Windows" toggle changed (ADR-019).</summary>
@@ -212,16 +211,15 @@ public partial class FenceWindow : Window
         TitleBar.SizeChanged += (_, _) => UpdateTabStripWidth();
         PreviewKeyDown += OnTabKeys;
         NewLibraryItem.Click += (_, _) => NewLibraryRequested?.Invoke();
-        NewFolderViewItem.Click += (_, _) => NewFolderViewRequested?.Invoke();
+        NewFolderPanelItem.Click += (_, _) => NewFolderPanelRequested?.Invoke(); // M26
+        AddFolderPanelItem.Click += (_, _) => AddFolderPanelRequested?.Invoke();
         AddGamesItem.Click += (_, _) => AddGamesRequested?.Invoke();
         AddClockItem.Click += (_, _) => AddWidgetRequested?.Invoke(WidgetKind.Clock); // M25
         AddDateItem.Click += (_, _) => AddWidgetRequested?.Invoke(WidgetKind.Date);
         AddStatsItem.Click += (_, _) => AddWidgetRequested?.Invoke(WidgetKind.Stats);
         LayoutFlowItem.Click += (_, _) => LayoutRequested?.Invoke(FenceLayout.Flow);
         LayoutFreeItem.Click += (_, _) => LayoutRequested?.Invoke(FenceLayout.Free);
-        OpenFolderItem.Click += (_, _) => OpenFolderRequested?.Invoke();
-        ViewSettingsItem.Click += (_, _) => ViewSettingsRequested?.Invoke();
-        MoreLine.MouseLeftButtonUp += (_, click) => { click.Handled = true; OpenFolderRequested?.Invoke(); };
+        ItemList.SizeChanged += (_, _) => ApplyFill(); // M26: a filling panel follows the fence's size
         AddItemItem.Click += (_, _) => AddItemRequested?.Invoke();
         AddFromDesktopItem.Click += (_, _) => AddFromDesktopRequested?.Invoke();
         RefreshItem.Click += (_, _) => RefreshRequested?.Invoke();
@@ -317,17 +315,15 @@ public partial class FenceWindow : Window
         AddFromDesktopItem.Visibility = items ? Visibility.Visible : Visibility.Collapsed;
         AddGamesItem.Visibility = items ? Visibility.Visible : Visibility.Collapsed;
         AddWidgetItem.Visibility = items ? Visibility.Visible : Visibility.Collapsed;
+        AddFolderPanelItem.Visibility = items ? Visibility.Visible : Visibility.Collapsed;
         SortItem.Visibility = _kind == FenceKind.Library ? Visibility.Collapsed : Visibility.Visible;
         foreach (var sortItem in SortItem.Items.OfType<MenuItem>()) sortItem.IsChecked = view && Equals(sortItem.Tag, fence.View!.Sort);
-        OpenFolderItem.Visibility = view ? Visibility.Visible : Visibility.Collapsed;
-        ViewSettingsItem.Visibility = view ? Visibility.Visible : Visibility.Collapsed;
         DeleteItem.Header = _kind switch
         {
             FenceKind.Library => "Delete fence (your games are not touched)",
             FenceKind.View => "Delete fence (the folder is not touched)",
             _ => "Delete fence (your files are not touched)",
         };
-        if (!view) SetViewStatus(center: null, more: null);
         UpdateEmptyHint();
         _labelMode = fence.Labels;
         SetIconSize(fence.IconSize);
@@ -340,18 +336,6 @@ public partial class FenceWindow : Window
     /// <summary>The shown tab's kind (M21): items, the Game Library, or a folder view.</summary>
     public FenceKind Kind => _kind;
 
-    /// <summary>
-    /// A folder view's status (M21 spec §3): a line instead of entries (not available, empty, nothing matching), and a
-    /// clickable "+ N more — Open folder" under them.
-    /// </summary>
-    public void SetViewStatus(string? center, string? more)
-    {
-        _viewStatus = center;
-        MoreText.Text = more ?? "";
-        MoreLine.Visibility = more is null ? Visibility.Collapsed : Visibility.Visible;
-        UpdateEmptyHint();
-    }
-
     /// <summary>The library's tile art by item ref: a 2:3 poster, or a logo shown centred (M12).</summary>
     public void SetLibraryArt(IReadOnlyDictionary<string, (string Path, bool IsPoster)> art)
     {
@@ -686,6 +670,10 @@ public partial class FenceWindow : Window
             if (IsLoaded) RequestIcon(view); // before that, Loaded requests them at the right DPI (M2b review)
             _items.Insert(index, view);
         }
+        foreach (var view in _items) HookPanel(view);
+        // M26: a lone panel set to fill takes the whole fence; the list then does not scroll (the panel does).
+        ScrollViewer.SetVerticalScrollBarVisibility(ItemList, shownItems is [{ Fill: true }] ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
+        ApplyFill();
         UpdateEmptyHint();
         GridPanel?.InvalidateMeasure(); // M24: spans or stored cells may have changed
         // Cells may have shifted under a shown name without a scroll or selection event (final review I3).
@@ -695,8 +683,7 @@ public partial class FenceWindow : Window
     /// <summary>An empty fence (not the library) says how to fill it (spec §5).</summary>
     private void UpdateEmptyHint()
     {
-        // A folder view shows its status line in the same place (M21).
-        var text = _kind == FenceKind.View ? _viewStatus : _kind == FenceKind.Items && _items.Count == 0 ? ItemsHint : null;
+        var text = _kind == FenceKind.Items && _items.Count == 0 ? ItemsHint : null;
         EmptyHint.Text = text ?? "";
         EmptyHint.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
     }
@@ -725,12 +712,48 @@ public partial class FenceWindow : Window
 
     private int IconSizePx => (int)Math.Round(_iconSizeDips * VisualTreeHelper.GetDpi(this).DpiScaleX);
 
-    /// <summary>An item's icon at its own size; a widget draws itself (M25: no icon to load).</summary>
+    /// <summary>An item's icon at its own size; a widget draws itself (M25), a panel shows its entries' icons (M26).</summary>
     private void RequestIcon(FenceItemView view)
     {
-        if (!view.IsWidget) _iconLoader.Request(view, PxOf(view));
+        if (!view.IsWidget && !view.IsPanel) _iconLoader.Request(view, PxOf(view));
+    }
+
+    /// <summary>A panel's requests go to the host; its rows ask for icons as they come into view (M26).</summary>
+    private void HookPanel(FenceItemView view)
+    {
+        if (view.PanelModel is not { Commands: null } model) return;
+        model.Commands = command => PanelCommandRequested?.Invoke(view.Key, command);
+        model.IconWanted = entry => RequestEntryIcon(model, entry);
     }
 
+    /// <summary>Small icons in rows (Details, List), the fence's icon size as tiles (Icons); asked again after a look or DPI change.</summary>
+    private void RequestEntryIcon(FolderPanelModel model, PanelEntry entry)
+    {
+        var dips = model.Look == PanelLook.Icons ? _iconSizeDips : 16;
+        var px = (int)Math.Round(dips * VisualTreeHelper.GetDpi(this).DpiScaleX);
+        if (entry.RequestedPx == px) return;
+        entry.RequestedPx = px;
+        _iconLoader.Request(entry.Item, px);
+    }
+
+    /// <summary>The host's latest listing for a panel element (M26).</summary>
+    public void SetPanelContent(string key, PanelContent content) =>
+        _items.FirstOrDefault(view => view.Key == key)?.PanelModel?.SetContent(content, System.Globalization.CultureInfo.CurrentCulture);
+
+    /// <summary>A filling panel takes the list's area less its padding (4 each side) and the cell's (2 + 2 across, 2 + 4 down).</summary>
+    private void ApplyFill()
+    {
+        if (_items is not [{ Fills: true } filling]) return;
+        filling.ApplyFill(Math.Max(0, ItemList.ActualWidth - 16), Math.Max(0, ItemList.ActualHeight - 20));
+    }
+
+    /// <summary>A mouse or key event inside a panel that the panel handles itself (its entries, buttons, lines).</summary>
+    private static bool InPanel(object source) =>
+        source is DependencyObject element && FindAncestor<FolderPanelView>(element) is { } panel && panel.OwnsInput(element);
+
+    /// <summary>The last drop hover was over a panel (M26): drops there are refused, never written into the folder.</summary>
+    public bool DropOverPanel { get; private set; }
+
     /// <summary>Widgets show <paramref name="now"/> and the last stats reading (M25; the host's timer calls this).</summary>
     public void UpdateWidgets(DateTime now, StatsSample? stats)
     {
@@ -820,6 +843,7 @@ public partial class FenceWindow : Window
                 ApplyArt(view);
             }
         }
+        ApplyFill(); // M26: a filling panel keeps the fence's size, not its span's
     }
 
     private void SizeView(FenceItemView view)
@@ -1239,6 +1263,7 @@ public partial class FenceWindow : Window
 
     private void OnItemListKeyDown(object sender, KeyEventArgs args)
     {
+        if (InPanel(args.OriginalSource)) return; // M26: Enter, Delete and the arrows inside a panel are the panel's
         var selected = ItemList.SelectedItems.OfType<FenceItemView>().ToList();
         var key = args.Key == Key.System ? args.SystemKey : args.Key; // Alt+Enter arrives as Key.System
         switch (key)
@@ -1288,12 +1313,14 @@ public partial class FenceWindow : Window
         if (TabHeaderAt(screenX, screenY) is { } hoveredTab && hoveredTab != FenceId) TabSelected?.Invoke(hoveredTab);
         var point = ItemList.PointFromScreen(new Point(screenX, screenY));
         LastDropCell = GridPanel is { } panel ? panel.CellAt(ItemList.TranslatePoint(point, panel)) : null; // M24: Free fences drop on a cell
+        DropOverPanel = false;
         var cells = new List<(double Left, double Top, double Width, double Height)>();
         var cellIndexes = new List<int>();
         for (var index = 0; index < _items.Count; index++)
         {
             if (ItemList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container) continue;
             var bounds = container.TransformToAncestor(ItemList).TransformBounds(new Rect(container.RenderSize));
+            if (_items[index].IsPanel && bounds.Contains(point)) DropOverPanel = true; // M26: never into the folder
             cells.Add((bounds.Left, bounds.Top, bounds.Width, bounds.Height));
             cellIndexes.Add(index);
         }
@@ -1340,6 +1367,7 @@ public partial class FenceWindow : Window
 
     private void OnListPress(object sender, MouseButtonEventArgs args)
     {
+        if (InPanel(args.OriginalSource)) return; // M26: a panel selects and drags its own entries
         if (args.OriginalSource is DependencyObject source && FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) is not null) return;
         // Text selection in a text box must never start a drag (M3b review I3).
         if (args.OriginalSource is DependencyObject pressed && FindAncestor<TextBox>(pressed) is not null) return;
@@ -1443,7 +1471,7 @@ public partial class FenceWindow : Window
 
     private void OnItemDoubleClick(object sender, MouseButtonEventArgs args)
     {
-        if (args.ChangedButton != MouseButton.Left) return;
+        if (args.ChangedButton != MouseButton.Left || InPanel(args.OriginalSource)) return; // M26: the panel opens its own entries
         if (ItemsControl.ContainerFromElement(ItemList, (DependencyObject)args.OriginalSource) is ListBoxItem { DataContext: FenceItemView view })
             OpenRequested?.Invoke(view.Key);
     }
diff --git a/src/NeoFences.App/FolderPanelModel.cs b/src/NeoFences.App/FolderPanelModel.cs
new file mode 100644
index 0000000..4f1f6b6
--- /dev/null
+++ b/src/NeoFences.App/FolderPanelModel.cs
@@ -0,0 +1,136 @@
+using System.Collections.ObjectModel;
+using System.ComponentModel;
+using System.Globalization;
+using System.Runtime.CompilerServices;
+using NeoFences.Core.Items;
+using NeoFences.Core.Model;
+
+namespace NeoFences.App;
+
+/// <summary>What a folder panel asks its fence to do (M26): the host acts on the panel's item.</summary>
+public abstract record PanelCommand;
+
+/// <summary>Double-click or Enter on entries: files open; one folder is browsed into.</summary>
+public sealed record PanelOpen(IReadOnlyList<string> Paths) : PanelCommand;
+
+public enum PanelMove { Back, Up, Home }
+
+public sealed record PanelNavigate(PanelMove Move) : PanelCommand;
+
+/// <summary>A Details column header clicked.</summary>
+public sealed record PanelSortBy(PanelSort Sort) : PanelCommand;
+
+/// <summary>Right-click on entries (Shift: Windows' menu), at a screen point in physical pixels.</summary>
+public sealed record PanelEntryMenu(IReadOnlyList<string> Paths, bool Extended, int ScreenX, int ScreenY, bool FromKeyboard) : PanelCommand;
+
+/// <summary>Entries dragged out of the panel (copies to other apps; items in fences).</summary>
+public sealed record PanelDrag(IReadOnlyList<string> Paths) : PanelCommand;
+
+/// <summary>The "+ N more — Open folder" line.</summary>
+public sealed record PanelOpenFolder : PanelCommand;
+
+/// <summary>What the host shows in a panel (M26): the header, the browse buttons, the entries or the line instead of them.</summary>
+/// <param name="ShowHeader">False for a panel filling a fence titled like it, at its own folder: the title already says it.</param>
+public sealed record PanelContent(string Header, bool ShowHeader, bool CanGoBack, bool CanGoUp, bool CanGoHome, IReadOnlyList<ItemInfo> Entries, string? Status, string? More);
+
+/// <summary>One entry of a panel: its facts as text for the columns, and an item view for its icon and name.</summary>
+public sealed class PanelEntry
+{
+    public PanelEntry(ItemInfo info, CultureInfo culture)
+    {
+        Info = info;
+        Item = new FenceItemView(new ShownItem(info.ItemRef, info.ItemRef, Name: info.Name));
+        DateText = info.Modified == DateTimeOffset.MinValue ? "" : info.Modified.LocalDateTime.ToString("g", culture);
+        TypeText = info.IsFolder ? "Folder" : info.TypeName.TrimStart('.');
+        SizeText = info.IsFolder ? "" : FolderPanels.SizeText(info.Size, culture);
+    }
+
+    public ItemInfo Info { get; }
+    public string Path => Info.ItemRef;
+    public FenceItemView Item { get; }
+    public string DateText { get; }
+    public string TypeText { get; }
+    public string SizeText { get; }
+
+    /// <summary>The icon size (physical pixels) last asked for: a new look or DPI asks again.</summary>
+    public int RequestedPx { get; set; }
+}
+
+/// <summary>
+/// A folder panel as its fence shows it (M26): the item's look and sort, and the host's latest content. Entries that stay
+/// keep their icon and selection; the window wires <see cref="Commands"/> and <see cref="IconWanted"/>.
+/// </summary>
+public sealed class FolderPanelModel : INotifyPropertyChanged
+{
+    public FolderPanelModel(string itemKey) => ItemKey = itemKey;
+
+    public string ItemKey { get; }
+
+    public ObservableCollection<PanelEntry> Entries { get; } = [];
+
+    /// <summary>Set by the window: the panel's requests go to the host.</summary>
+    public Action<PanelCommand>? Commands { get; set; }
+
+    /// <summary>Set by the window: a row came into view and needs its icon.</summary>
+    public Action<PanelEntry>? IconWanted { get; set; }
+
+    public PanelLook Look { get; private set { if (field == value) return; field = value; Changed(); LookChanged?.Invoke(); } }
+
+    /// <summary>The look changed: the control swaps its view and rows.</summary>
+    public event Action? LookChanged;
+
+    public PanelSort Sort { get; private set { field = value; Changed(); } }
+    public bool Descending { get; private set { field = value; Changed(); } }
+
+    /// <summary>Details columns: 2 (Name, Date) or 4.</summary>
+    public int Columns { get; private set { if (field == value) return; field = value; Changed(); LookChanged?.Invoke(); } } = 4;
+
+    public string Header { get; private set { field = value; Changed(); } } = "";
+    public bool ShowHeader { get; private set { field = value; Changed(); } } = true;
+    public bool CanGoBack { get; private set { field = value; Changed(); Changed(nameof(Browsing)); } }
+    public bool CanGoUp { get; private set { field = value; Changed(); Changed(nameof(Browsing)); } }
+    public bool CanGoHome { get; private set { field = value; Changed(); Changed(nameof(Browsing)); } }
+
+    /// <summary>The browse buttons show: away from home, or with somewhere to go back to.</summary>
+    public bool Browsing => CanGoBack || CanGoUp || CanGoHome;
+
+    /// <summary>A line instead of entries: not available, empty, nothing matching; null while there are entries (or the first listing is on its way).</summary>
+    public string? Status { get; private set { field = value; Changed(); } }
+
+    /// <summary>"+ N more — Open folder", or null.</summary>
+    public string? More { get; private set { field = value; Changed(); } }
+
+    public void Apply(FolderPanel panel, GridSpan span)
+    {
+        Look = panel.Look;
+        Sort = panel.Sort;
+        Descending = panel.Descending;
+        Columns = FolderPanels.Columns(span.Columns);
+    }
+
+    /// <summary>The host's latest listing for this panel, in place: entries that stay keep their icon, selection and scroll.</summary>
+    public void SetContent(PanelContent content, CultureInfo culture)
+    {
+        (Header, ShowHeader) = (content.Header, content.ShowHeader);
+        (CanGoBack, CanGoUp, CanGoHome) = (content.CanGoBack, content.CanGoUp, content.CanGoHome);
+        (Status, More) = (content.Status, content.More);
+        var kept = Entries.GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
+        var wanted = content.Entries.Select(info => kept.TryGetValue(info.ItemRef, out var entry) && entry.Info == info ? entry : new PanelEntry(info, culture)).ToList();
+        var wantedSet = wanted.ToHashSet();
+        for (var index = Entries.Count - 1; index >= 0; index--)
+        {
+            if (!wantedSet.Contains(Entries[index])) Entries.RemoveAt(index);
+        }
+        // ponytail: O(n²) moves in the worst case (a re-sort of 500 entries); a keyed diff when that shows up.
+        for (var index = 0; index < wanted.Count; index++)
+        {
+            var at = index < Entries.Count && ReferenceEquals(Entries[index], wanted[index]) ? index : Entries.IndexOf(wanted[index]);
+            if (at < 0) Entries.Insert(index, wanted[index]);
+            else if (at != index) Entries.Move(at, index);
+        }
+    }
+
+    public event PropertyChangedEventHandler? PropertyChanged;
+
+    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
+}
diff --git a/src/NeoFences.App/FolderPanelView.xaml b/src/NeoFences.App/FolderPanelView.xaml
new file mode 100644
index 0000000..08074d5
--- /dev/null
+++ b/src/NeoFences.App/FolderPanelView.xaml
@@ -0,0 +1,167 @@
+<UserControl x:Class="NeoFences.App.FolderPanelView"
+             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
+             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
+    <!-- M26 (spec 2026-10-05-folder-panel-design §2): a folder inside a fence, read-only. DataContext: FolderPanelModel.
+         Colours are the fence's DynamicResources (FenceText, FenceHover, …), so a panel follows the fence's look. -->
+    <UserControl.Resources>
+        <Style x:Key="PlainRow" TargetType="ListViewItem">
+            <Setter Property="FocusVisualStyle" Value="{x:Null}" />
+            <Setter Property="AutomationProperties.Name" Value="{Binding Item.Label}" />
+            <Setter Property="ToolTip" Value="{Binding Path}" />
+            <EventSetter Event="Loaded" Handler="OnRowLoaded" />
+            <Setter Property="Template">
+                <Setter.Value>
+                    <ControlTemplate TargetType="ListViewItem">
+                        <Border x:Name="Chrome" Background="Transparent" CornerRadius="3" Padding="4,1">
+                            <ContentPresenter />
+                        </Border>
+                        <ControlTemplate.Triggers>
+                            <Trigger Property="IsMouseOver" Value="True">
+                                <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceHover}" />
+                            </Trigger>
+                            <Trigger Property="IsSelected" Value="True">
+                                <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceSelected}" />
+                            </Trigger>
+                        </ControlTemplate.Triggers>
+                    </ControlTemplate>
+                </Setter.Value>
+            </Setter>
+        </Style>
+        <Style x:Key="DetailsRow" TargetType="ListViewItem" BasedOn="{StaticResource PlainRow}">
+            <Setter Property="Template">
+                <Setter.Value>
+                    <ControlTemplate TargetType="ListViewItem">
+                        <Border x:Name="Chrome" Background="Transparent" CornerRadius="3" Padding="0,1">
+                            <GridViewRowPresenter Columns="{TemplateBinding GridView.ColumnCollection}" Content="{TemplateBinding Content}" />
+                        </Border>
+                        <ControlTemplate.Triggers>
+                            <Trigger Property="IsMouseOver" Value="True">
+                                <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceHover}" />
+                            </Trigger>
+                            <Trigger Property="IsSelected" Value="True">
+                                <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceSelected}" />
+                            </Trigger>
+                        </ControlTemplate.Triggers>
+                    </ControlTemplate>
+                </Setter.Value>
+            </Setter>
+        </Style>
+        <Style x:Key="IconTile" TargetType="ListViewItem" BasedOn="{StaticResource PlainRow}">
+            <Setter Property="Template">
+                <Setter.Value>
+                    <ControlTemplate TargetType="ListViewItem">
+                        <Border Background="Transparent" Padding="2">
+                            <Border x:Name="Chrome" Background="Transparent" CornerRadius="4" Padding="2,4">
+                                <ContentPresenter HorizontalAlignment="Center" />
+                            </Border>
+                        </Border>
+                        <ControlTemplate.Triggers>
+                            <Trigger Property="IsMouseOver" Value="True">
+                                <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceHover}" />
+                            </Trigger>
+                            <Trigger Property="IsSelected" Value="True">
+                                <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceSelected}" />
+                            </Trigger>
+                        </ControlTemplate.Triggers>
+                    </ControlTemplate>
+                </Setter.Value>
+            </Setter>
+        </Style>
+        <Style TargetType="GridViewColumnHeader">
+            <Setter Property="HorizontalContentAlignment" Value="Left" />
+            <Setter Property="Template">
+                <Setter.Value>
+                    <ControlTemplate TargetType="GridViewColumnHeader">
+                        <Border x:Name="Chrome" Background="Transparent" CornerRadius="3" Padding="4,1">
+                            <TextBlock Text="{TemplateBinding Content}" Foreground="{DynamicResource FenceSubtleText}" FontSize="11"
+                                       TextTrimming="CharacterEllipsis" />
+                        </Border>
+                        <ControlTemplate.Triggers>
+                            <Trigger Property="IsMouseOver" Value="True">
+                                <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceHover}" />
+                            </Trigger>
+                        </ControlTemplate.Triggers>
+                    </ControlTemplate>
+                </Setter.Value>
+            </Setter>
+        </Style>
+        <Style x:Key="BrowseButton" TargetType="Button">
+            <Setter Property="Width" Value="22" />
+            <Setter Property="Height" Value="20" />
+            <Setter Property="FontFamily" Value="Segoe Fluent Icons, Segoe MDL2 Assets" />
+            <Setter Property="FontSize" Value="11" />
+            <Setter Property="Foreground" Value="{DynamicResource FenceText}" />
+            <Setter Property="Focusable" Value="False" />
+            <Setter Property="Template">
+                <Setter.Value>
+                    <ControlTemplate TargetType="Button">
+                        <Border x:Name="Chrome" Background="Transparent" CornerRadius="3">
+                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
+                        </Border>
+                        <ControlTemplate.Triggers>
+                            <Trigger Property="IsMouseOver" Value="True">
+                                <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceHover}" />
+                            </Trigger>
+                            <Trigger Property="IsEnabled" Value="False">
+                                <Setter TargetName="Chrome" Property="Opacity" Value="0.35" />
+                            </Trigger>
+                        </ControlTemplate.Triggers>
+                    </ControlTemplate>
+                </Setter.Value>
+            </Setter>
+        </Style>
+        <DataTemplate x:Key="NameCell">
+            <StackPanel Orientation="Horizontal">
+                <Image Source="{Binding Item.Icon}" Width="16" Height="16" Margin="0,0,6,0" />
+                <TextBlock Text="{Binding Item.Label}" TextTrimming="CharacterEllipsis" VerticalAlignment="Center" />
+            </StackPanel>
+        </DataTemplate>
+        <DataTemplate x:Key="IconCell">
+            <StackPanel Width="{DynamicResource ItemWidth}">
+                <Image Source="{Binding Item.Icon}" Width="{DynamicResource IconSize}" Height="{DynamicResource IconSize}" HorizontalAlignment="Center" />
+                <TextBlock Margin="0,4,0,0" Text="{Binding Item.Label}" TextAlignment="Center" TextWrapping="Wrap" TextTrimming="CharacterEllipsis"
+                           MaxHeight="32" Effect="{DynamicResource LabelShadow}" />
+            </StackPanel>
+        </DataTemplate>
+        <ItemsPanelTemplate x:Key="RowsPanel">
+            <VirtualizingStackPanel />
+        </ItemsPanelTemplate>
+        <ItemsPanelTemplate x:Key="TilesPanel">
+            <WrapPanel />
+        </ItemsPanelTemplate>
+        <Style x:Key="Hidden" TargetType="TextBlock">
+            <Style.Triggers>
+                <Trigger Property="Text" Value="">
+                    <Setter Property="Visibility" Value="Collapsed" />
+                </Trigger>
+            </Style.Triggers>
+        </Style>
+    </UserControl.Resources>
+    <Grid>
+        <Grid.RowDefinitions>
+            <RowDefinition Height="Auto" />
+            <RowDefinition />
+            <RowDefinition Height="Auto" />
+        </Grid.RowDefinitions>
+        <DockPanel x:Name="HeaderRow" Margin="2,0,2,2" Background="#01000000">
+            <StackPanel x:Name="BrowseButtons" Orientation="Horizontal" DockPanel.Dock="Left" Margin="0,0,4,0" Visibility="Collapsed">
+                <Button x:Name="BackButton" Style="{StaticResource BrowseButton}" Content="&#xE72B;" IsEnabled="{Binding CanGoBack}"
+                        ToolTip="Back (Backspace)" AutomationProperties.Name="Back" />
+                <Button x:Name="UpButton" Style="{StaticResource BrowseButton}" Content="&#xE74A;" IsEnabled="{Binding CanGoUp}"
+                        ToolTip="Up (Alt+Up)" AutomationProperties.Name="Up" />
+                <Button x:Name="HomeButton" Style="{StaticResource BrowseButton}" Content="&#xE80F;" IsEnabled="{Binding CanGoHome}"
+                        ToolTip="Back to the panel's folder" AutomationProperties.Name="Home" />
+            </StackPanel>
+            <TextBlock x:Name="HeaderText" Text="{Binding Header}" Foreground="{DynamicResource FenceTitleText}" FontWeight="SemiBold" FontSize="12"
+                       VerticalAlignment="Center" TextTrimming="CharacterEllipsis" Effect="{DynamicResource LabelShadow}" />
+        </DockPanel>
+        <ListView x:Name="EntryList" Grid.Row="1" Background="Transparent" BorderThickness="0" Padding="0" SelectionMode="Extended"
+                  ItemsSource="{Binding Entries}" Foreground="{DynamicResource FenceText}" FontSize="12"
+                  ScrollViewer.HorizontalScrollBarVisibility="Disabled" ScrollViewer.VerticalScrollBarVisibility="Auto"
+                  VirtualizingPanel.IsVirtualizing="True" VirtualizingPanel.ScrollUnit="Pixel" AutomationProperties.Name="Folder entries" />
+        <TextBlock x:Name="StatusText" Grid.Row="1" Text="{Binding Status}" Style="{StaticResource Hidden}" Margin="8" TextWrapping="Wrap"
+                   TextAlignment="Center" VerticalAlignment="Center" Foreground="{DynamicResource FenceSubtleText}" IsHitTestVisible="False" />
+        <TextBlock x:Name="MoreText" Grid.Row="2" Text="{Binding More}" Style="{StaticResource Hidden}" Padding="4,2,4,2" Cursor="Hand"
+                   Foreground="{DynamicResource FenceSubtleText}" FontSize="11" TextAlignment="Center" TextDecorations="Underline" />
+    </Grid>
+</UserControl>
diff --git a/src/NeoFences.App/FolderPanelView.xaml.cs b/src/NeoFences.App/FolderPanelView.xaml.cs
new file mode 100644
index 0000000..9538112
--- /dev/null
+++ b/src/NeoFences.App/FolderPanelView.xaml.cs
@@ -0,0 +1,237 @@
+using System.ComponentModel;
+using System.Windows;
+using System.Windows.Controls;
+using System.Windows.Input;
+using System.Windows.Media;
+using NeoFences.Core.Items;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// A folder panel (M26, spec 2026-10-05-folder-panel-design §2): Details (columns that sort), List or Icons, a header with
+/// Back / Up / Home while browsing, and the status lines. It handles its own clicks, keys, wheel and drags; everything it
+/// does goes to the host as a <see cref="PanelCommand"/>. Nothing here writes to the folder.
+/// </summary>
+public partial class FolderPanelView : UserControl
+{
+    private const double DateWidth = 112, TypeWidth = 56, SizeWidth = 72, ScrollbarRoom = 12;
+    private readonly GridView _details = new() { AllowsColumnReorder = false };
+    private readonly Dictionary<GridViewColumn, PanelSort> _sorts = [];
+    private readonly GridViewColumn _nameColumn, _dateColumn, _typeColumn, _sizeColumn;
+    private FolderPanelModel? _model;
+    private Point? _pressPoint;
+    private ListViewItem? _deferredSelect; // pressed on one of several selected entries: selected alone only on release
+
+    public FolderPanelView()
+    {
+        InitializeComponent();
+        _nameColumn = Column("Name", PanelSort.Name, cell: (DataTemplate)Resources["NameCell"]);
+        _dateColumn = Column("Date modified", PanelSort.Date, text: nameof(PanelEntry.DateText));
+        _typeColumn = Column("Type", PanelSort.Type, text: nameof(PanelEntry.TypeText));
+        _sizeColumn = Column("Size", PanelSort.Size, text: nameof(PanelEntry.SizeText));
+        DataContextChanged += (_, _) => Attach(DataContext as FolderPanelModel);
+        SizeChanged += (_, _) => SizeColumns();
+        EntryList.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(OnHeaderClick));
+        EntryList.PreviewMouseLeftButtonDown += OnPress;
+        EntryList.PreviewMouseMove += OnMove;
+        EntryList.PreviewMouseLeftButtonUp += OnRelease;
+        EntryList.MouseDoubleClick += OnDoubleClick;
+        EntryList.KeyDown += OnKeyDown;
+        EntryList.ContextMenuOpening += OnEntryMenu;
+        BackButton.Click += (_, _) => Send(new PanelNavigate(PanelMove.Back));
+        UpButton.Click += (_, _) => Send(new PanelNavigate(PanelMove.Up));
+        HomeButton.Click += (_, _) => Send(new PanelNavigate(PanelMove.Home));
+        MoreText.MouseLeftButtonUp += (_, click) => { click.Handled = true; Send(new PanelOpenFolder()); };
+    }
+
+    /// <summary>
+    /// True when a mouse or key event from <paramref name="source"/> is the panel's own (its entries, buttons and lines);
+    /// the header's name is the fence's, so the panel can be selected and dragged by it like any element.
+    /// </summary>
+    public bool OwnsInput(DependencyObject source) => !IsWithin(source, HeaderRow) || IsWithin(source, BrowseButtons);
+
+    private GridViewColumn Column(string header, PanelSort sort, DataTemplate? cell = null, string? text = null)
+    {
+        var column = new GridViewColumn { Header = header };
+        if (cell is not null) column.CellTemplate = cell;
+        else column.DisplayMemberBinding = new System.Windows.Data.Binding(text);
+        _sorts[column] = sort;
+        return column;
+    }
+
+    private void Attach(FolderPanelModel? model)
+    {
+        if (_model is not null)
+        {
+            _model.LookChanged -= ApplyLook;
+            _model.PropertyChanged -= OnModelChanged;
+        }
+        _model = model;
+        if (model is null) return;
+        model.LookChanged += ApplyLook;
+        model.PropertyChanged += OnModelChanged;
+        ApplyLook();
+        ShowBrowsing();
+        HeaderRow.Visibility = model.ShowHeader ? Visibility.Visible : Visibility.Collapsed;
+    }
+
+    private void OnModelChanged(object? sender, PropertyChangedEventArgs change)
+    {
+        if (change.PropertyName is nameof(FolderPanelModel.Sort) or nameof(FolderPanelModel.Descending)) ShowSort();
+        if (change.PropertyName == nameof(FolderPanelModel.Browsing)) ShowBrowsing();
+        if (change.PropertyName == nameof(FolderPanelModel.ShowHeader)) HeaderRow.Visibility = _model?.ShowHeader == false ? Visibility.Collapsed : Visibility.Visible;
+    }
+
+    private void ShowBrowsing() => BrowseButtons.Visibility = _model?.Browsing == true ? Visibility.Visible : Visibility.Collapsed;
+
+    /// <summary>Details: the columns (Name and Date when narrow); List: names in rows; Icons: tiles like the fence's own.</summary>
+    private void ApplyLook()
+    {
+        if (_model is null) return;
+        var look = _model.Look;
+        _details.Columns.Clear();
+        if (look == PanelLook.Details)
+        {
+            GridViewColumn[] columns = _model.Columns == 2 ? [_nameColumn, _dateColumn] : [_nameColumn, _dateColumn, _typeColumn, _sizeColumn];
+            foreach (var column in columns) _details.Columns.Add(column);
+        }
+        EntryList.View = look == PanelLook.Details ? _details : null;
+        EntryList.ItemContainerStyle = (Style)Resources[look switch { PanelLook.Details => "DetailsRow", PanelLook.Icons => "IconTile", _ => "PlainRow" }];
+        EntryList.ItemTemplate = look switch { PanelLook.Icons => (DataTemplate)Resources["IconCell"], PanelLook.List => (DataTemplate)Resources["NameCell"], _ => null };
+        EntryList.ItemsPanel = (ItemsPanelTemplate)Resources[look == PanelLook.Icons ? "TilesPanel" : "RowsPanel"];
+        ShowSort();
+        SizeColumns();
+    }
+
+    /// <summary>The sorted column says so with an arrow.</summary>
+    private void ShowSort()
+    {
+        if (_model is null) return;
+        foreach (var (column, sort) in _sorts)
+        {
+            var name = sort switch { PanelSort.Date => "Date modified", PanelSort.Type => "Type", PanelSort.Size => "Size", _ => "Name" };
+            column.Header = sort == _model.Sort ? $"{name} {(_model.Descending ? "▾" : "▴")}" : name;
+        }
+    }
+
+    /// <summary>Name takes what the other columns leave; nothing scrolls sideways.</summary>
+    private void SizeColumns()
+    {
+        var others = _details.Columns.Where(column => column != _nameColumn).Sum(column => column == _dateColumn ? DateWidth : column == _typeColumn ? TypeWidth : SizeWidth);
+        foreach (var column in _details.Columns) column.Width = column == _nameColumn ? Math.Max(60, ActualWidth - others - ScrollbarRoom) : _sorts[column] switch
+        {
+            PanelSort.Date => DateWidth,
+            PanelSort.Type => TypeWidth,
+            _ => SizeWidth,
+        };
+    }
+
+    private void OnHeaderClick(object sender, RoutedEventArgs click)
+    {
+        if (click.OriginalSource is GridViewColumnHeader { Column: { } column } && _sorts.TryGetValue(column, out var sort)) Send(new PanelSortBy(sort));
+    }
+
+    /// <summary>A row came into view (rows are virtualized): its icon is asked for now, not for all 500 at once.</summary>
+    private void OnRowLoaded(object sender, RoutedEventArgs loaded)
+    {
+        if (sender is ListViewItem { DataContext: PanelEntry entry }) _model?.IconWanted?.Invoke(entry);
+    }
+
+    private ListViewItem? RowOf(object source) => source is DependencyObject element ? FindAncestor<ListViewItem>(element) : null;
+
+    private IReadOnlyList<string> SelectedPaths(PanelEntry? first = null) =>
+        [.. new[] { first }.OfType<PanelEntry>().Concat(EntryList.SelectedItems.OfType<PanelEntry>().Where(entry => entry != first)).Select(entry => entry.Path)];
+
+    private void OnPress(object sender, MouseButtonEventArgs press)
+    {
+        _pressPoint = null;
+        if (RowOf(press.OriginalSource) is not { } row) return;
+        _pressPoint = press.GetPosition(EntryList);
+        // Pressing one of several selected entries keeps them all, so they can be dragged together.
+        if (row.IsSelected && EntryList.SelectedItems.Count > 1 && Keyboard.Modifiers == ModifierKeys.None && press.ClickCount == 1)
+        {
+            _deferredSelect = row;
+            row.Focus();
+            press.Handled = true;
+        }
+    }
+
+    private void OnMove(object sender, MouseEventArgs move)
+    {
+        if (move.LeftButton != MouseButtonState.Pressed || _pressPoint is not { } pressed) return;
+        var position = move.GetPosition(EntryList);
+        if (Math.Abs(position.X - pressed.X) < SystemParameters.MinimumHorizontalDragDistance
+            && Math.Abs(position.Y - pressed.Y) < SystemParameters.MinimumVerticalDragDistance) return;
+        _pressPoint = null;
+        _deferredSelect = null;
+        var paths = SelectedPaths();
+        if (paths.Count > 0) Send(new PanelDrag(paths)); // returns when the drag ends (Windows' modal loop)
+    }
+
+    private void OnRelease(object sender, MouseButtonEventArgs release)
+    {
+        _pressPoint = null;
+        if (_deferredSelect is not { } row) return;
+        EntryList.SelectedItems.Clear();
+        row.IsSelected = true;
+        _deferredSelect = null;
+    }
+
+    private void OnDoubleClick(object sender, MouseButtonEventArgs click)
+    {
+        if (click.ChangedButton != MouseButton.Left || RowOf(click.OriginalSource) is not { DataContext: PanelEntry entry }) return;
+        click.Handled = true;
+        Send(new PanelOpen([entry.Path]));
+    }
+
+    private void OnKeyDown(object sender, KeyEventArgs key)
+    {
+        var pressed = key.Key == Key.System ? key.SystemKey : key.Key;
+        PanelCommand? command = pressed switch
+        {
+            Key.Enter when EntryList.SelectedItems.Count > 0 && Keyboard.Modifiers == ModifierKeys.None => new PanelOpen(SelectedPaths()),
+            Key.Back => new PanelNavigate(PanelMove.Back),
+            Key.Left when Keyboard.Modifiers == ModifierKeys.Alt => new PanelNavigate(PanelMove.Back),
+            Key.Up when Keyboard.Modifiers == ModifierKeys.Alt => new PanelNavigate(PanelMove.Up),
+            _ => null,
+        };
+        if (command is null) return;
+        key.Handled = true;
+        Send(command);
+    }
+
+    /// <summary>Right-click on an entry: the entry menu (the host's); on empty space the fence shows the panel's own menu.</summary>
+    private void OnEntryMenu(object sender, ContextMenuEventArgs args)
+    {
+        if (RowOf(args.OriginalSource) is not { DataContext: PanelEntry entry } row) return;
+        args.Handled = true;
+        if (!row.IsSelected)
+        {
+            EntryList.SelectedItems.Clear();
+            row.IsSelected = true;
+        }
+        var fromKeyboard = args.CursorLeft < 0;
+        var anchor = fromKeyboard ? row.PointToScreen(new Point(row.ActualWidth / 2, row.ActualHeight / 2)) : PointToScreen(Mouse.GetPosition(this));
+        Send(new PanelEntryMenu(SelectedPaths(entry), Extended: !fromKeyboard && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), (int)anchor.X, (int)anchor.Y, fromKeyboard));
+    }
+
+    private void Send(PanelCommand command) => _model?.Commands?.Invoke(command);
+
+    private static bool IsWithin(DependencyObject source, DependencyObject ancestor)
+    {
+        for (var current = source; current is not null; current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
+        {
+            if (current == ancestor) return true;
+        }
+        return false;
+    }
+
+    private static TAncestor? FindAncestor<TAncestor>(DependencyObject source) where TAncestor : DependencyObject
+    {
+        for (var current = source; current is not null; current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
+        {
+            if (current is TAncestor match) return match;
+        }
+        return null;
+    }
+}
diff --git a/src/NeoFences.App/FolderViewWindow.xaml b/src/NeoFences.App/FolderViewWindow.xaml
index 202d7a3..01cb565 100644
--- a/src/NeoFences.App/FolderViewWindow.xaml
+++ b/src/NeoFences.App/FolderViewWindow.xaml
@@ -1,9 +1,10 @@
 <Window x:Class="NeoFences.App.FolderViewWindow"
         xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
-        Title="Folder view settings" Width="500" SizeToContent="Height" ResizeMode="NoResize"
+        Title="Folder panel settings" Width="500" SizeToContent="Height" ResizeMode="NoResize"
         WindowStartupLocation="CenterScreen" ShowInTaskbar="True" ThemeMode="System">
-    <!-- M21 spec 2026-10-05-folder-views-design §2: what a folder view shows. NeoFences never writes to the folder. -->
+    <!-- M21 spec 2026-10-05-folder-views-design §2, M26: what a folder panel shows (its sort is the headers' and Sort by's).
+         NeoFences never writes to the folder. -->
     <Window.Resources>
         <Style x:Key="FieldLabel" TargetType="TextBlock">
             <Setter Property="VerticalAlignment" Value="Center" />
@@ -28,7 +29,6 @@
                 <RowDefinition Height="Auto" />
                 <RowDefinition Height="Auto" />
                 <RowDefinition Height="Auto" />
-                <RowDefinition Height="Auto" />
             </Grid.RowDefinitions>
 
             <TextBlock Text="Folder" Style="{StaticResource FieldLabel}" />
@@ -48,20 +48,13 @@
             <TextBlock x:Name="PatternsHint" Grid.Row="3" Grid.Column="1" Grid.ColumnSpan="2" Style="{StaticResource Hint}" Margin="0,0,0,10"
                        Text="e.g. *.png;*.jpg or pdf — empty shows every type. Subfolders always show." />
 
-            <TextBlock Grid.Row="4" Text="Sort by" Style="{StaticResource FieldLabel}" />
-            <ComboBox x:Name="SortBox" Grid.Row="4" Grid.Column="1" Grid.ColumnSpan="2" Margin="0,0,0,10" AutomationProperties.Name="Sort by">
-                <ComboBoxItem Content="Name" />
-                <ComboBoxItem Content="Type" />
-                <ComboBoxItem Content="Date (newest first)" />
-            </ComboBox>
-
-            <CheckBox x:Name="NewestCheck" Grid.Row="5" Grid.ColumnSpan="2" Content="Only the newest" VerticalAlignment="Center" Margin="0,0,0,10" />
-            <TextBox x:Name="NewestBox" Grid.Row="5" Grid.Column="2" Width="64" Margin="0,0,0,10" AutomationProperties.Name="How many of the newest" />
+            <CheckBox x:Name="NewestCheck" Grid.Row="4" Grid.ColumnSpan="2" Content="Only the newest" VerticalAlignment="Center" Margin="0,0,0,10" />
+            <TextBox x:Name="NewestBox" Grid.Row="4" Grid.Column="2" Width="64" Margin="0,0,0,10" AutomationProperties.Name="How many of the newest" />
         </Grid>
         <!-- M23: what keeps OK greyed out (a folder that is not a full path, a count out of range). -->
         <TextBlock x:Name="ProblemText" Style="{StaticResource Hint}" Foreground="IndianRed" Visibility="Collapsed" Margin="0,0,0,4" />
         <TextBlock Style="{StaticResource Hint}" Margin="0,4,0,18"
-                   Text="The fence shows this folder as it is, live. Removing the fence or an entry from it never touches your files." />
+                   Text="The panel shows this folder as it is, live. Removing the panel or its fence never touches your files." />
         <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
             <Button x:Name="OkButton" Content="OK" IsDefault="True" MinWidth="90" Margin="0,0,8,0" />
             <Button Content="Cancel" IsCancel="True" MinWidth="90" />
diff --git a/src/NeoFences.App/FolderViewWindow.xaml.cs b/src/NeoFences.App/FolderViewWindow.xaml.cs
index 74941f3..cebcd6b 100644
--- a/src/NeoFences.App/FolderViewWindow.xaml.cs
+++ b/src/NeoFences.App/FolderViewWindow.xaml.cs
@@ -7,26 +7,27 @@ using NeoFences.Shell;
 namespace NeoFences.App;
 
 /// <summary>
-/// "Folder view settings" (M21 spec §2): the folder, what it shows, the types, the sort and "only the newest N". OK gives
-/// <see cref="Result"/>; an invalid pattern or count marks its box and disables OK.
+/// "Folder panel settings" (M21 spec §2 as folder view settings; M26): the folder, what it shows, the types and "only the
+/// newest N" (the sort is the headers' and Sort by's). OK gives <see cref="Result"/>; an invalid pattern or count marks its
+/// box and disables OK.
 /// </summary>
 public partial class FolderViewWindow : Window
 {
-    private static readonly FenceSort[] Sorts = [FenceSort.Name, FenceSort.Type, FenceSort.Date];
     private readonly string _patternsHint;
+    private readonly FolderPanel _panel;
 
-    public FolderView? Result { get; private set; }
+    public (string Folder, FolderPanel Panel)? Result { get; private set; }
 
-    public FolderViewWindow(FolderView view)
+    public FolderViewWindow(string folder, FolderPanel panel)
     {
         InitializeComponent();
+        _panel = panel;
         _patternsHint = PatternsHint.Text;
-        FolderBox.Text = view.Path;
-        ShowBox.SelectedIndex = (int)view.Show;
-        PatternsBox.Text = view.Patterns;
-        SortBox.SelectedIndex = Math.Max(0, Array.IndexOf(Sorts, view.Sort));
-        NewestCheck.IsChecked = view.Newest is not null;
-        NewestBox.Text = (view.Newest ?? FolderViews.BusyFolderNewest).ToString();
+        FolderBox.Text = folder;
+        ShowBox.SelectedIndex = (int)panel.Show;
+        PatternsBox.Text = panel.Patterns;
+        NewestCheck.IsChecked = panel.Newest is not null;
+        NewestBox.Text = (panel.Newest ?? FolderViews.BusyFolderNewest).ToString();
         FolderBox.TextChanged += (_, _) => Validate();
         PatternsBox.TextChanged += (_, _) => Validate();
         NewestBox.TextChanged += (_, _) => Validate();
@@ -53,10 +54,10 @@ public partial class FolderViewWindow : Window
         Task.Run(() => start.Length > 0 && TargetChecks.RootOf(start) is not null && TargetProbe.Check(start).State == TargetState.Ok).ContinueWith(checking =>
         {
             IsEnabled = true;
-            if (checking.IsFaulted) Serilog.Log.Warning(checking.Exception, "folder view: {Folder} could not be checked; the dialog opens at its default place", start);
+            if (checking.IsFaulted) Serilog.Log.Warning(checking.Exception, "folder panel: {Folder} could not be checked; the dialog opens at its default place", start);
             var reachable = !checking.IsFaulted && checking.Result;
             var picked = PathPicker.TryPickFolder(new WindowInteropHelper(this).Handle, "Choose the folder to show",
-                failure => Serilog.Log.Warning(failure, "folder view: the folder dialog failed"), startFolder: reachable ? start : null);
+                failure => Serilog.Log.Warning(failure, "folder panel: the folder dialog failed"), startFolder: reachable ? start : null);
             if (picked is not null) FolderBox.Text = picked;
         }, TaskScheduler.FromCurrentSynchronizationContext());
     }
@@ -78,8 +79,8 @@ public partial class FolderViewWindow : Window
         OkButton.IsEnabled = Read() is not null;
     }
 
-    /// <summary>The view as entered, or null while something is not valid.</summary>
-    private FolderView? Read()
+    /// <summary>The folder and settings as entered, or null while something is not valid.</summary>
+    private (string Folder, FolderPanel Panel)? Read()
     {
         // A full path only, variables expanded (M23): a relative one would be read against NeoFences' own folder.
         if (FolderViews.FolderPath(FolderBox.Text) is not { } folder || FolderViews.ParsePatterns(PatternsBox.Text) is null) return null;
@@ -89,13 +90,6 @@ public partial class FolderViewWindow : Window
             if (!int.TryParse(NewestBox.Text.Trim(), out var count) || count < 1 || count > FolderViews.MaxNewest) return null;
             newest = count;
         }
-        return new FolderView
-        {
-            Path = folder,
-            Show = (ViewShow)Math.Max(0, ShowBox.SelectedIndex),
-            Patterns = PatternsBox.Text.Trim(),
-            Sort = Sorts[Math.Max(0, SortBox.SelectedIndex)],
-            Newest = newest,
-        };
+        return (folder, _panel with { Show = (ViewShow)Math.Max(0, ShowBox.SelectedIndex), Patterns = PatternsBox.Text.Trim(), Newest = newest });
     }
 }
diff --git a/src/NeoFences.Shell/FolderItems.cs b/src/NeoFences.Shell/FolderItems.cs
index 39c2abd..444b715 100644
--- a/src/NeoFences.Shell/FolderItems.cs
+++ b/src/NeoFences.Shell/FolderItems.cs
@@ -37,7 +37,8 @@ public static class FolderItems
     {
         var isFolder = entry is DirectoryInfo;
         return new ItemInfo(entry.FullName, entry.Name, isFolder, isFolder ? "" : entry.Extension.ToUpperInvariant(),
-            entry.Exists ? entry.LastWriteTime : DateTimeOffset.MinValue);
+            entry.Exists ? entry.LastWriteTime : DateTimeOffset.MinValue,
+            Size: entry is FileInfo { Exists: true } file ? file.Length : null); // M26: a panel's Size column (read with the listing)
     }
 }
 
````

- [ ] **Step 2: Build and test.** `dotnet build` → `0 Warning(s)`; `dotnet test` → 601 passed.

- [ ] **Step 3: Commit.** `git add -A && git commit -m "feat: added folder panels to fences and made folder views into panels"`

### Task 3: Docs

**Files:**
- Modify: `docs/DECISIONS.md` (ADR-048; ADR-044 status), `docs/ARCHITECTURE.md`, `docs/FEATURES.md`, `docs/TEST-CHECKLIST.md` (section AL)
- Create: `docs/research/m26-folder-panel.md`

- [ ] **Step 1: Apply.** Write this patch to `m26-3-docs.patch` and apply it:

````diff
diff --git a/docs/ARCHITECTURE.md b/docs/ARCHITECTURE.md
index 8290249..f582b4a 100644
--- a/docs/ARCHITECTURE.md
+++ b/docs/ARCHITECTURE.md
@@ -82,6 +82,7 @@ happen) is behind Shift+right-click, under a line saying it acts on the real fil
 | Core/FencePlacement, Core/Lifecycle | px↔DIP placement, containing monitor; `RunState` (hide icons, quick-hide, Pause, game mode), session-end policy, restart throttle, watcher backoff | — |
 | Core/Library | Valve/Epic parsing, `GameCatalog` merge, `LibraryFiles` plan, `GameLaunchers.LauncherOf` (ADR-032); `GameItems` (M22): `Migrate` (a library fence → game items), `NewGames`, `AddNew`, `Retarget` (by game id), `ShowsCover` | — |
 | Core/Items (M24) | `GridSpan` (1–4 × 1–4), `GridCell`, `FenceLayout` (Flow / Free), `FenceGrid.Arrange` (dense packing; Free: stored cells, collisions and out-of-width elements to free spots), `CellAt`, `NearestFree`, `PlaceDropped`, `SpanOf` (covers 1×2); `ItemEdits.SetSize`, `Place`; repairs | — |
+| Core/Items (M26) | `FolderPanels`: `FolderPanel` (look Details / List / Icons, show, patterns, newest N, sort + direction), `IsPanel`, `Create` (busy folders newest first), `Select` (the M21 rules plus Size and direction; `FolderViews.Select` maps to it), `HeaderSort`, `Columns`, `Fills`, `HeaderOf`, `SizeText`, `MigrateViews`; `PanelPlace` (Into / Back / Up / Home, never above the panel's folder); `ItemInfo.Size` | System.IO.Enumeration |
 | Core/Items (M25) | `Widgets`: `WidgetKind` (Clock / Date / Stats), `neofences:widget/<kind>` targets (`ItemKind.Widget`, never checked or watched), default spans, `WidgetOptions`, clock text, date page, stat rows, `NextTick` | System.Globalization |
 | Shell/SystemStats (M25) | CPU (`GetSystemTimes` deltas), RAM (`GlobalMemoryStatusEx`), C: used (`GetDiskFreeSpaceEx`), GPU (PDH `\GPU Engine(*engtype_3D)\Utilization Percentage`, summed); null per value on failure | PDH, kernel32 |
 | App/FenceHost.Widgets (M25) | Add widget ▸, the widget menu (clock options), double-clicks (Clock app, Task Manager); one timer on whole seconds while any widget exists, idle while none can be seen; stats every 2 s on a worker | DispatcherTimer |
@@ -92,8 +93,8 @@ happen) is behind Shift+right-click, under a line saying it acts on the real fil
 | App/FenceHost.Watching | watched folders (`FolderWatcher` + removal notices), rename-follow, target checks (start, 5 min, fence shown, Refresh, drive arrival/removal), per-fence refresh throttle, game-mode deferral | DispatcherTimer |
 | App/FenceWindow, FenceItemView, IconLoader | fence UI: item grid (ListBox + WrapPanel), keys (Enter, Del = remove, F2 / Alt+Enter = Properties), drop caret, Missing badge / Unavailable dimming, empty-fence hint, `WM_DEVICECHANGE` → drives changed; names/icons on two STA threads, an item's own name and icon win, websites show the default browser's icon | WPF, frozen BitmapSource |
 | App/ItemPropertiesWindow, MissingItemWindow | Properties and Add item… (name, target + Browse, live Found / Missing / Drive not connected, arguments, run as admin, icon from a file / a picture / reset, note); the missing-target question | WPF (Fluent) |
-| App/FenceHost.FolderViews, FolderViewWindow (M21) | folder views: one `FolderLister` per view (hidden tabs keep listing), the last listing per view, rendering through `FolderViews.Select`, New folder view…, settings, Show as folder view, Add to fence ▸, the safe menu and Windows' menu for entries, rename-follow, removal notices, game-mode pause (ADR-044) | WPF (Fluent) |
-| App/FolderLister, FenceHost.Library | a folder's live listing (250 ms coalescing, 7 s retry, watcher backoff, removal notices, game-mode pause, the folder's own rename) for folder views (M21) and the Game Library's own `library\` folder (former Portal machinery), shown as tiles; scans launchers, game folders and Desktop game shortcuts (ADR-032) | FileSystemWatcher |
+| App/FenceHost.FolderPanels, FolderPanelView, FolderPanelModel, FolderViewWindow (M26) | folder panels (ADR-048): one `FolderLister` per panel on the folder it shows (hidden tabs keep listing), selection off the UI thread, browsing in memory, the panel menu (Look, Sort by, settings, Open folder, Size with Fill fence, Show as icon), Add folder panel…, New folder panel…, Show as folder panel, the entries' safe menu and Windows' menu, drag-out, removal notices, game-mode pause, the folder-view migration (a snapshot first); the control: Details (`GridView`, header sort), List, Icons, virtualized rows, icons as rows load; the fence leaves a panel's own clicks, keys and wheel to it and refuses drops over it | WPF (Fluent) |
+| App/FolderLister, FenceHost.Library | a folder's live listing (250 ms coalescing, 7 s retry, watcher backoff, removal notices, game-mode pause, the folder's own rename) for folder panels (M21, M26) and the Game Library's own `library\` folder (former Portal machinery), shown as tiles; scans launchers, game folders and Desktop game shortcuts (ADR-032) | FileSystemWatcher |
 | App/SettingsWindow | General (Start with Windows, Hide desktop icons while NeoFences runs, Peek hotkey) · Fences · Appearance · Snapshots · Game Library · Game mode · Updates · About | WPF ThemeMode |
 | App/InstallHooks, Shell/StartupRegistration, Core/StartupPolicy, build/pack.ps1 | Velopack installer and auto-update (ADR-023, ADR-039) | Velopack 1.2.161 |
 | Shell/ShellDragDrop | drag out: a shell item array of the targets (absolute ID lists) turned into a data object, or URLs, copy/link only; one IDropTarget per fence: fence items (keys) → move / Ctrl-duplicate, outside items read as shell items (real paths, special items as `::{GUID}`, zip contents skipped; CF_HDROP as fallback) or one website (UniformResourceLocatorW / text) → new items; never a move effect | SHDoDragDrop, SHCreateShellItemArrayFromIDLists + BHID_DataObject, SHCreateShellItemArrayFromDataObject, SHParseDisplayName, RegisterDragDrop, IDropTargetHelper |
@@ -130,7 +131,7 @@ happen) is behind Shift+right-click, under a line saying it acts on the real fil
 - `config.json` (+ `.bak`, `.tmp` transient) — schema 5
   - shape: `{ schemaVersion, settings: { hideDesktopIcons, peekHotkey, … }, fences: [ { id, title, isLibrary, view: { path, show, sort, newest, patterns } (M21), layout (M24), iconSize, rolledUp, locked, labels, tabs, activeTab, tabColor, customColor } ], layouts: { <fingerprint>: { monitors, fences: { <fenceId>: { monitor, x, y, w, h } } } }, library, lastLayoutFingerprint }`
 - `items.json` (+ `.bak`) — schema 1 (ADR-041)
-  - shape: `{ schema, fences: { <fenceId>: [ { id, target, name, icon: { file, index } | { image }, arguments, runAsAdmin, note, gameId, showAs (M22), size: { columns, rows }, cell: { column, row } (M24), widget: { seconds, date } (M25) } ] } }`
+  - shape: `{ schema, fences: { <fenceId>: [ { id, target, name, icon: { file, index } | { image }, arguments, runAsAdmin, note, gameId, showAs (M22), size: { columns, rows }, cell: { column, row } (M24), widget: { seconds, date } (M25), panel: { look, show, patterns, newest, sort, descending }, fill (M26) } ] } }`
   - a list is removed only with its fence (Delete fence); lists of fences the config does not have stay (a fallback config never costs items; ADR-041 amended)
 - `icons\<itemId>-<guid>.png` — pictures chosen as item icons (≤ 256 px); unused ones deleted at start
 - `backups\config-<yyyyMMdd>.json`, `backups\items-<yyyyMMdd>.json` (keep 10 each), `backups\pre-schema-5-config.json`
@@ -168,6 +169,12 @@ games… is open; after each scan game items follow their game's shortcut and ne
 Game Library fence becomes an items fence once (a "Before games became items" snapshot first). The library fence kind
 stays in the code, not in the menus (ADR-045, `research/m22-games-as-items.md`).
 
+**0.15.0 (M26, the folder panel element)**: any fence can show a folder as a panel — Details (columns that sort), List
+or Icons — beside its other elements, at 1–4 × 1–4 cells or filling the fence (fence menu → Add folder panel…, tray → New
+folder panel…, a folder item → Show as folder panel). Double-clicking a subfolder browses inside the panel (Back / Up /
+Home). Folder views became fences holding one filling panel (a snapshot first). Read-only as before (ADR-048,
+`research/m26-folder-panel.md`).
+
 **0.14.0 (M25, widgets)**: Clock, Date and System stats elements in any fence (fence menu → Add widget ▸), any size;
 the clock follows Windows' time format, with optional seconds and date line; stats show CPU, RAM, GPU and C: as bars every
 2 s; nothing updates while no widget can be seen (ADR-047, `research/m25-widgets.md`).
diff --git a/docs/DECISIONS.md b/docs/DECISIONS.md
index b442236..482ffa8 100644
--- a/docs/DECISIONS.md
+++ b/docs/DECISIONS.md
@@ -1192,7 +1192,7 @@ project for now; the name is revisited only if it is ever distributed more widel
 **Consequences.** No rename work; the trademark question stays a listed risk on the hub.
 
 ## ADR-044 — Folder views: fences that show one folder live, read-only
-**Date:** 2026-10-05 · **Status:** Accepted · **Refines:** ADR-040 (Portals parked "for later as dynamic collections")
+**Date:** 2026-10-05 · **Status:** Accepted; the folder-view *fence* superseded by ADR-048 (0.15.0: a folder panel element; the read-only rules stand) · **Refines:** ADR-040 (Portals parked "for later as dynamic collections")
 
 **Context.** ADR-040 parked Portals and Rules for "dynamic collections". The user chose folder views first (M21, 0.11.0):
 game and app folders, work folders, Downloads and Screenshots, and USB sticks, each shown live in a fence.
@@ -1272,4 +1272,29 @@ system stats (CPU, RAM, GPU, disk) as bars.
   mode, rolled up, hidden tab); stats read every 2 s on a worker, one reading at a time.
 
 **Consequences.** Measured 0.003 % of the user's CPU with three widgets shown. A widget kind a newer NeoFences adds reads
-as a plain "missing" item in an older one.
\ No newline at end of file
+as a plain "missing" item in an older one.
+## ADR-048 — The folder panel element (folder views become panels)
+**Date:** 2026-10-05 · **Status:** Accepted · **Supersedes:** ADR-044's folder-view fence (its read-only rules stand) ·
+**Continues:** ADR-045 (one kind of fence), ADR-046 (element sizes), ADR-047 (elements beside items)
+
+**Context.** The user's vision: a fence holds elements; "show a specified folder in detailed view" was the last one named.
+Folder views (M21) were a fence kind of their own. The user chose: the panel replaces folder views; looks Details, List
+and Icons; double-clicking a subfolder browses inside the panel; sizes 1–4 × 1–4 cells or "Fill fence".
+
+**Decision.**
+- A panel is a folder item with `VirtualItem.Panel` (`FolderPanel`: look, show, patterns, newest N, sort + direction) and
+  optionally `Fill`. Being a path item, it gets missing state, Locate…, the bulk fix, rename-follow, drives coming and
+  going, copies and snapshots for free. Default span 4×4.
+- "Fill fence" works while the panel is the fence's only element; beside anything else it sits on cells (a size pick
+  turns it off). The fence list then does not scroll; the panel does.
+- Browsing (Back / Up / Home, Backspace, Alt+Up) lives in memory; each start and Home show the panel's own folder. Up
+  never goes above it.
+- Details columns Name, Date modified, Type, Size (Name and Date at 1–2 cells wide); a header click sorts (again:
+  reversed). Name and Type keep folders first; Date and Size mix them.
+- Folder views migrate once at start (and after restoring an older snapshot): a "Before folder views became panels"
+  snapshot, then each view fence holds one filling Icons panel with the view's settings; items are saved before the
+  config, so a cut-short save only repeats the migration. `Fence.View` / `FenceKind.View` stay in the code.
+- Read-only as ADR-044: drops over a panel are refused; its entry menu is NeoFences' safe one (Shift: Windows' menu).
+
+**Consequences.** One lister per panel (hidden tabs keep listing, game mode pauses them); rows are virtualized and icons
+load as rows come into view; at most 500 entries, then "+ N more". An older NeoFences shows a panel as a plain folder icon.
diff --git a/docs/FEATURES.md b/docs/FEATURES.md
index 3defd28..04971a0 100644
--- a/docs/FEATURES.md
+++ b/docs/FEATURES.md
@@ -31,7 +31,7 @@ Sources for Fences 6: stardock.com news posts "Now Announcing: Fences 6", "Fence
 | Missing / unavailable targets | — | 0.9 | M18 | done | watched (≤ 64 folders), renames followed, Locate… / Remove, per-fence Refresh; a generic icon for their type (0.10) |
 | Bulk fix of missing items | — | 0.10 | M19 | done | after one Locate…, "Fix N more items?" for the others from the same old place; undo from the tray (ADR-042) |
 | Rubber-band selection | 1+ | v1 | M3 | done | M3b; Ctrl adds |
-| Folder Portals → folder views | 3+ | 0.11 | M21 | done | ADR-044: read-only (no drop into the folder, no rename/delete from NeoFences' menu); subfolders open in Explorer |
+| Folder Portals → folder views → folder panels | 3+ | 0.11 / 0.15 | M21, M26 | done | ADR-044, ADR-048: read-only (no drop into the folder, no rename/delete from NeoFences' menu); since 0.15 a panel element in any fence, subfolders browse inside it |
 | Sort (name/type/date) | 2+ | v1 | M4 | done | one time; by the names shown (0.9) |
 | Draw fence by right-drag on desktop | 1+ | v1 | M5 | done | S2 keeps the plain right-click menu (ADR-020) |
 | Quick-hide (double-click desktop) | 1+ | v1 | M5 | done | fences + desktop icons (user choice); a double-click on a native icon opens it |
@@ -65,10 +65,11 @@ Sources for Fences 6: stardock.com news posts "Now Announcing: Fences 6", "Fence
 | Blur tint preference (lighter/darker) | v1.7 (M14) | done | background strength slider, one value per Windows tone (ADR-036) |
 | Search palette across all fences | — | parked | built on branch `m15-search-palette` (local history bundle only), not merged |
 | Game Library (Steam/Epic/GOG/Ubisoft Connect/EA, cover art) | v1.5 | done | launchers, Xbox, game folders, Desktop game shortcuts (ADR-032); since 0.12 games are items in any fence (cover tile or icon, Add games…, new games go to a chosen fence; ADR-045) |
-| One kind of fence: any item in any fence, its look from its kind and settings | 0.12 (M22) | done (games, sizes) | ADR-045, ADR-046; next: a folder panel element, widgets (clock, calendar) |
+| One kind of fence: any item in any fence, its look from its kind and settings | 0.12 (M22) | done (games, sizes, widgets, folder panels) | ADR-045–ADR-048; later: auto-collect rules |
 | Element sizes 1–4 × 1–4 and a fence grid (Flow packed / Free fixed positions) | 0.13 (M24) | done | ADR-046; Size ▸ picker, Layout ▸ per fence |
 | Widgets: clock, date, system stats (CPU, RAM, GPU, C:) | 0.14 (M25) | done | ADR-047; any size; idle while unseen |
-| Folder views: types, files/folders only, newest N, live sort, "+ N more" | 0.11 (M21) | done | ADR-044; Downloads/Screenshots start newest first |
+| Folder views: types, files/folders only, newest N, live sort, "+ N more" | 0.11 (M21) | done | ADR-044; Downloads/Screenshots start newest first; panels since 0.15 |
+| Folder panel element: Details / List / Icons, header sort, browse in (Back / Up / Home), Fill fence | 0.15 (M26) | done | ADR-048; folder views migrate to panels |
 | Auto-collect rules (the other half of dynamic collections) | later | — | replaces Rules (ADR-040) |
 | Theme packs: "Nanosuit" (Crysis HUD), "Animus" (Assassin's Creed) | v2 | — | |
 | Custom Win11-style compact context menu | v2 | — | |
diff --git a/docs/TEST-CHECKLIST.md b/docs/TEST-CHECKLIST.md
index 0b5c15d..2953c8b 100644
--- a/docs/TEST-CHECKLIST.md
+++ b/docs/TEST-CHECKLIST.md
@@ -644,3 +644,21 @@ Added after the M19 final review:
 | AK11 | A rolled-up fence holding a clock: hover it open | the clock shows the current time at once and keeps ticking while open |
 | AK12 | Select a widget and an app → right-click → Open | the app opens; no Windows "get an app for this link" prompt |
 | AK13 | Change the time zone (or the time) in Windows' settings | the clock follows at once |
+
+## AL — 0.15.0 the folder panel element (M26)
+
+| ID | Steps | Expected |
+|---|---|---|
+| AL1 | Start 0.15.0 over data with a folder view (Downloads) | the fence holds one panel filling it, Icons look, same entries and order; no second title; a "Before folder views became panels" snapshot |
+| AL2 | Fence menu → Add folder panel… (Flow fence); again in a Free fence | a 4 × 4 Details panel at the end / the first free spot |
+| AL3 | Panel menu → Look ▸ Details, List, Icons; Size ▸ 2 × 3 | each look; at 1–2 cells wide Details shows Name and Date only |
+| AL4 | Click the Date modified header, then again; Name; Size | newest first, then oldest first; folders first by name; biggest first |
+| AL5 | Double-click a subfolder; Back; Up; Home; Backspace; Alt+Up | browses in; the header shows the path below the folder; Up never goes above it |
+| AL6 | Panel alone in a fence → Fill fence; resize the fence; Add item… | fills and follows the size; with the new item it sits on 4 × 4 cells again |
+| AL7 | Folder item → Show as folder panel; panel → Show as icon | a panel in place; the folder icon again |
+| AL8 | Drop a file from Explorer onto a panel; onto empty space beside it | refused over the panel; an item beside it |
+| AL9 | Drag entries out to Explorer; Add to fence ▸; Shift+right-click an entry | copies (never moved); items in that fence; Windows' menu under its warning line |
+| AL10 | A panel of the pendrive: Safely Remove while shown; plug back in | the drive ejects; "Folder not available"; back by itself |
+| AL11 | A game in front (game mode); change files in the folder; leave the game | no listing during the game; current after |
+| AL12 | NeoFences' CPU with two panels shown, idle 30 s | well under 0.1 % |
+| AL13 | Snapshot from before 0.15.0 (with a folder view) → restore | the view comes back as a filling panel |
diff --git a/docs/research/m26-folder-panel.md b/docs/research/m26-folder-panel.md
new file mode 100644
index 0000000..d779342
--- /dev/null
+++ b/docs/research/m26-folder-panel.md
@@ -0,0 +1,37 @@
+# M26 — The folder panel element (0.15.0): build notes and results
+
+Spec: `docs/superpowers/specs/2026-10-05-folder-panel-design.md` · Decision: ADR-048 · Plan:
+`docs/superpowers/plans/2026-10-05-m26-folder-panel.md`
+
+## Prototype (2026-10-05, worktree `neo_fences-m26proto`, branch `m26-proto`)
+
+Built end to end before the plan: Core test-first (42 new tests, 601 in all), Shell, App; 0 warnings. Probed live on a
+copy of the user's data (restored afterwards; the user paused the first run, the second ran with their go):
+
+- Start over data with the Downloads folder view: a "Before folder views became panels" snapshot, then the fence held one
+  filling Icons panel (newest 30, date, newest first) — 14 entries, the same look as the view, no second title.
+- A 4 × 3 Details panel of `D:\GameLibrary` in Games: the Date modified header sorted newest first (arrow on the header);
+  double-clicking `Blur` browsed in ("GameLibrary › Blur", Back / Up / Home shown, its files with dates, types and
+  sizes); Back returned.
+- Downloads → panel menu → Look ▸ Details: columns with sizes in the user's number format.
+- NeoFences' CPU over 30 s with both panels shown, idle: 0–0.01 % of the machine.
+
+Found and fixed while probing: a filling panel repeated the fence's title (now hidden while it shows its own folder under
+the same name); the whole panel tinted with the fence's hover and selection (now only its rows do).
+
+### Where the build departs from the spec (the final review weighs them)
+
+- "Fill fence" is stored and works while the panel is alone: beside another element it sits on cells, and fills again
+  when it is alone again; a size pick turns it off (the spec: adding anything "turns it back to cells").
+- A "Before folder views became panels" snapshot is saved before the migration (as M22 did for games); the migration is
+  skipped (logged) while items.json is read-only — that fence then shows nothing for the session.
+- A filling panel at its own folder under the fence's own title hides its header row.
+- "Show as folder panel" resets the item's size (the panel's 4 × 4) and Fill; "Show as icon" clears the panel settings.
+- Add folder panel… in a Free fence uses the first free spot for 4 × 4 (rows grow; it never shrinks).
+- The fence menu's "New folder view…" became "New folder panel…" (a new fence with one filling panel), beside Add folder
+  panel….
+- Panel settings is the folder view dialog without the sort (headers and Sort by ▸ sort); the panel menu has no
+  Properties… (F2 / Alt+Enter on the selected panel still open it).
+- The Type column shows the extension (as Sort by does), not Windows' type names.
+- The panel's name area belongs to the fence (select, drag, right-click → panel menu); its rows, buttons and lines are the
+  panel's own.
````

- [ ] **Step 2: Check.** The secret scan of `git diff main` (the personal email, the private hub link) finds nothing.

- [ ] **Step 3: Commit.** `git add -A && git commit -m "docs: described folder panels in ADR-048, architecture, features, checklist AL and the M26 research note"`

### Task 4: Final review and fix pass

- [ ] Whole-branch review on the most capable model with the spec, this plan, its Review Focus and departures; Critical /
  Important fixed in one pass (test-first where Core can show it; App-only fixes get an AL row); minors deferred in the
  research note.

### Task 5: Live check (asked first: the user may be at the PC)

- [ ] Ask; back up the data and Run value; run the branch build; TEST-CHECKLIST AL by script (banners; never type into
  Windows Terminal); screenshots sent; restore; refocus Terminal; results into the research note; commit.
