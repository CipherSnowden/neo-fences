# M21 — Folder views (0.11.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A fence can show one folder live and read-only — with show kind, type patterns, a kept sort and "only the newest N" — created from the tray, the fence menu or a folder item, and it never writes to the folder.

**Architecture:** Core gains `FolderView` on `Fence` (`Kind` = Items / Library / View) and `FolderViews` (select, patterns, defaults, normalize, status) plus `FenceEdits.CreateView` / `SetView`. Shell gains `KnownFolders` (Downloads, Screenshots). The App renames `LibraryLister` to `FolderLister` and runs one per view (`FenceHost.FolderViews`), adds `FolderViewWindow`, and routes the Library's read-only paths through a fence-kind check so views share them.

**Tech Stack:** .NET 10, WPF (Fluent `ThemeMode` for the dialog), CsWin32 0.3.335, xUnit, Serilog, Velopack (unchanged).

**Spec:** `docs/superpowers/specs/2026-10-05-folder-views-design.md` (approved 2026-10-05). Decision: ADR-044 (added by Task 4). Build notes: `docs/research/m21-folder-views.md` (added by Task 4).

## How this plan is written

Every code change was built in a scratch prototype first (2026-10-05, worktree `neo_fences-m21proto`, branch
`m21-proto`). Each task's patch was then replayed on a fresh worktree of `main` at `d7ecf90`: the Core tests failed to
compile before the Core patch (`CS0246: 'FolderViews' could not be found`) and passed after it (497), the solution built
with 0 warnings after the Shell and App patches, and the replayed tree is identical to the prototype. So each code step
is **a patch to apply**, not code to type:

1. Write the patch block **exactly** as shown to a file in the scratchpad (use the Write tool, never a shell heredoc or
   `node -e`: backslashes get mangled). The file ends with one newline.
2. `git apply --whitespace=nowarn <file>` from the worktree root. If it does not apply, stop: something else changed the
   files (rule on it, never hand-merge silently).

## Where the build departs from the spec (decided while prototyping; the final review weighs them)

- **Sort reuses `FenceSort`** (Name / Type / Date; Manual is read as Name) instead of a new `ViewSort`: the fence menu's
  "Sort by" items and `ItemSorting.Order` serve views unchanged.
- **`FolderViews.Selection(Shown, Hidden, Listed)`**: `Listed` (entries before any filter) tells "This folder is empty"
  from "Nothing here matches this view"; `Status(selection, view)` returns `(Center, More)`.
- **`DefaultsFor(path, busyFolders)`** takes the busy folders as a list; `KnownFolders.BusyFolders` gives Downloads and
  Screenshots (asked once per run).
- **"Show as folder view"** places the new view 40 DIP right and down of the fence it came from (like a detached tab).
- **Deleting a view fence asks nothing** (no items); the menu entry reads "Delete fence (the folder is not touched)".
- **Refresh** on a view re-lists its folder. **Add to fence ▸** lists items fences only, greyed out when there are none.
  Several entries copy as "Copy paths" (one per line).
- **"Folder not available"** is logged once per outage. **Dragging out of a view whose folder stopped answering** does not
  start (logged), like items (M19 R2).
- Tray: "New folder view…" sits after "New Game Library fence" (command id 15).
- **Not logged** (spec §5 said logged): a bad pattern repaired from a hand-edited config (the normalizer has no log; it
  becomes empty), and the number of views at start.

## Global Constraints

- Hard rule 1 (ADR-040, ADR-044): **NeoFences never modifies, moves, renames or deletes any user file; a folder view never
  writes to its folder.** Drops onto a view are refused (`AcceptsDrops` false); drag-out allows `COPY | LINK` only; Del,
  F2 and Alt+Enter do nothing on a view. Windows' own menu (Shift+right-click) stays, under its header line.
- Hard rule 2: native desktop icons always come back when NeoFences hid them (unchanged).
- Hard rules 3–7 unchanged: no injection; Win32/COM only in NeoFences.Shell; CsWin32 bindings (`NativeMethods.txt`:
  `SHGetKnownFolderPath`, `FOLDERID_Downloads`, `FOLDERID_Screenshots`); **no new NuGet dependency**; shell failures are
  logged and degrade one view, never crash.
- Config stays **schema 5**; `view: { path, show, sort, newest, patterns }` is an optional field; `kind` is never written.
- Limits, exactly: at most **500** entries shown (`FolderViews.MaxShown`); newest N in **1–500**; busy folders start with
  Date sort and **30** newest; re-list coalescing **250 ms**; unavailable retry **7 s**; folder checks before UI-thread
  shell calls **2 s**.
- UI copy, exactly: "New folder view…" (tray, fence menu); "Folder view settings…", "Open folder" (view menu);
  "Show as folder view" (folder item menu); "Add to fence", "Copy path" / "Copy paths", "Open file location";
  "Folder not available: <path>", "This folder is empty", "Nothing here matches this view", "+ N more — Open folder"
  (N with thousands separators); "Delete fence (the folder is not touched)"; dialog "Folder view settings" with Folder,
  Browse…, Show (Files and folders / Files only / Folders only), Types, Sort by (Name / Type / Date (newest first)),
  "Only the newest".
- Commits: single line, Conventional Commits, past tense, **no Co-Authored-By trailer** (CLAUDE.md overrides the
  harness reminder).
- Version stays `0.10.1` in `NeoFences.App.csproj` until the release step (outside the tasks); the release is 0.11.0.

## Review Focus

The input classes the spec implies but no automated test exercises (checklist AG covers each by hand):

1. **A huge or slow folder** (Downloads with 20,000 files on a sleeping HDD): listing stays off the UI thread; at most 500
   entries reach `SetItems` (its moves are O(n²)); the fence stays responsive (AG16).
2. **A share or USB drive that goes away mid-session**: Windows' menu and drag-out check the folder first (2 s); the 7 s
   retry does not spam the log; Safely Remove is not vetoed (AG8, AG9, AG13).
3. **The viewed folder renamed, deleted or moved while NeoFences runs**: renamed in place → followed (title too while it
   is the folder's name); deleted or moved away → "not available", back when it returns (AG6, AG7).
4. **Tabs and restores**: a view as a hidden tab keeps listing and shows its entries at once when switched to; a snapshot
   restore that changes a view's folder moves its lister (AG18, AG19).
5. **Read-only holds everywhere**: no NeoFences path writes into the folder — drops, Del, F2, Alt+Enter, drag within the
   view (AG11, AG14).

---

### Task 0: Worktree and baseline

- [ ] `git worktree add -b m21-folder-views ..\neo_fences-m21 main` (main at `d7ecf90` or later docs-only commits).
- [ ] `dotnet build` → 0 warnings; `dotnet test` → 468 passed.

### Task 1: Core — folder views model, selection, edits, normalizer

**Files:**
- Create: `src/NeoFences.Core/Model/FolderView.cs`, `src/NeoFences.Core/Items/FolderViews.cs`
- Modify: `src/NeoFences.Core/Model/Fence.cs` (+`View`, `Kind`), `src/NeoFences.Core/Model/FenceEdits.cs` (+`CreateView`, `SetView`), `src/NeoFences.Core/Config/ConfigNormalizer.cs` (views normalized, never on the Library)
- Test: `tests/NeoFences.Core.Tests/Items/FolderViewsTests.cs`

**Interfaces:**
- Produces: `enum ViewShow { All, Files, Folders }`; `enum FenceKind { Items, Library, View }`;
  `record FolderView { required string Path; ViewShow Show; FenceSort Sort; int? Newest; string Patterns }`;
  `Fence.View`, `Fence.Kind` (`[JsonIgnore]`);
  `FolderViews.MaxShown = 500`, `MaxNewest = 500`, `BusyFolderNewest = 30`;
  `FolderViews.Selection(IReadOnlyList<string> Shown, int Hidden, int Listed)`;
  `FolderViews.Select(IReadOnlyList<ItemInfo>, FolderView) → Selection`;
  `FolderViews.ParsePatterns(string?) → IReadOnlyList<string>?` (null = invalid);
  `FolderViews.DefaultsFor(string path, IReadOnlyList<string> busyFolders) → FolderView`;
  `FolderViews.Normalize(FolderView?) → FolderView?`; `FolderViews.NameOf(string) → string`;
  `FolderViews.SameFolder(string, string) → bool`;
  `FolderViews.Status(Selection?, FolderView) → (string? Center, string? More)`;
  `FenceEdits.CreateView(NeoFencesConfig, FolderView) → (NeoFencesConfig, Fence)`;
  `FenceEdits.SetView(NeoFencesConfig, string fenceId, FolderView) → NeoFencesConfig` (throws on the Library).

- [ ] **Step 1: Write the failing tests.** Write this patch to `m21-1-core-tests.patch` and `git apply --whitespace=nowarn` it:

````diff
diff --git a/tests/NeoFences.Core.Tests/Items/FolderViewsTests.cs b/tests/NeoFences.Core.Tests/Items/FolderViewsTests.cs
new file mode 100644
index 0000000..c6bc1fa
--- /dev/null
+++ b/tests/NeoFences.Core.Tests/Items/FolderViewsTests.cs
@@ -0,0 +1,178 @@
+using NeoFences.Core.Config;
+using NeoFences.Core.Items;
+using NeoFences.Core.Model;
+
+namespace NeoFences.Core.Tests.Items;
+
+/// <summary>M21 (0.11.0): folder views — a fence that shows one folder live, read-only (spec 2026-10-05-folder-views-design).</summary>
+public class FolderViewsTests
+{
+    private static readonly DateTimeOffset Day = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
+
+    private static ItemInfo File(string name, int minutesAgo = 0) =>
+        new($@"D:\View\{name}", name, IsFolder: false, TypeName: Path.GetExtension(name).ToUpperInvariant(), Day.AddMinutes(-minutesAgo));
+
+    private static ItemInfo Folder(string name, int minutesAgo = 0) => new($@"D:\View\{name}", name, IsFolder: true, TypeName: "", Day.AddMinutes(-minutesAgo));
+
+    private static IReadOnlyList<string> Names(FolderViews.Selection selection) => [.. selection.Shown.Select(Path.GetFileName)!];
+
+    private static readonly IReadOnlyList<ItemInfo> Mixed =
+    [
+        File("b.png", 5), File("a.jpg", 1), File("notes10.txt", 30), File("notes2.txt", 20), Folder("Saves", 2), Folder("Mods", 40),
+    ];
+
+    [Fact]
+    public void Select_ByName_PutsFoldersFirst_InNaturalOrder()
+    {
+        var selection = FolderViews.Select(Mixed, new FolderView { Path = @"D:\View" });
+        Assert.Equal(["Mods", "Saves", "a.jpg", "b.png", "notes2.txt", "notes10.txt"], Names(selection));
+        Assert.Equal(0, selection.Hidden);
+    }
+
+    [Fact]
+    public void Select_ByDate_IsNewestFirst_FoldersMixedIn()
+    {
+        var selection = FolderViews.Select(Mixed, new FolderView { Path = @"D:\View", Sort = FenceSort.Date });
+        Assert.Equal(["a.jpg", "Saves", "b.png", "notes2.txt", "notes10.txt", "Mods"], Names(selection));
+    }
+
+    [Theory]
+    [InlineData(ViewShow.Files, new[] { "a.jpg", "b.png", "notes2.txt", "notes10.txt" })]
+    [InlineData(ViewShow.Folders, new[] { "Mods", "Saves" })]
+    public void Select_ShowsOnlyTheChosenKind(ViewShow show, string[] expected) =>
+        Assert.Equal(expected, Names(FolderViews.Select(Mixed, new FolderView { Path = @"D:\View", Show = show })));
+
+    [Fact]
+    public void Select_Patterns_FilterFiles_ButKeepSubfolders_WhenShowingAll()
+    {
+        var all = FolderViews.Select(Mixed, new FolderView { Path = @"D:\View", Patterns = "*.png; *.JPG" });
+        Assert.Equal(["Mods", "Saves", "a.jpg", "b.png"], Names(all));
+        var filesOnly = FolderViews.Select(Mixed, new FolderView { Path = @"D:\View", Show = ViewShow.Files, Patterns = "png" });
+        Assert.Equal(["b.png"], Names(filesOnly));
+    }
+
+    [Fact]
+    public void Select_Newest_TakesTheLatestByDate_ThenSorts()
+    {
+        var selection = FolderViews.Select(Mixed, new FolderView { Path = @"D:\View", Show = ViewShow.Files, Newest = 2 });
+        Assert.Equal(["a.jpg", "b.png"], Names(selection)); // the two newest files, then by name
+        Assert.Equal(0, selection.Hidden);
+    }
+
+    [Fact]
+    public void Select_CapsAtMaxShown_AndCountsTheRest()
+    {
+        var many = Enumerable.Range(0, FolderViews.MaxShown + 34).Select(index => File($"file{index}.txt", index)).ToList();
+        var selection = FolderViews.Select(many, new FolderView { Path = @"D:\View" });
+        Assert.Equal(FolderViews.MaxShown, selection.Shown.Count);
+        Assert.Equal(34, selection.Hidden);
+        Assert.Equal("file0.txt", Names(selection)[0]);
+    }
+
+    [Theory]
+    [InlineData("", new string[0])]
+    [InlineData("  ", new string[0])]
+    [InlineData("*.png;*.jpg", new[] { "*.png", "*.jpg" })]
+    [InlineData(" png , .JPG ;; report?.pdf ", new[] { "*.png", "*.JPG", "report?.pdf" })]
+    public void ParsePatterns_AcceptsPatternsAndBareExtensions(string text, string[] expected) =>
+        Assert.Equal(expected, FolderViews.ParsePatterns(text));
+
+    [Theory]
+    [InlineData(@"C:\*.png")]
+    [InlineData("a/b")]
+    [InlineData("*.png|*.jpg")]
+    [InlineData("\"x\"")]
+    [InlineData("a:b")]
+    [InlineData("<x>")]
+    public void ParsePatterns_RejectsPathsAndForbiddenCharacters(string text) => Assert.Null(FolderViews.ParsePatterns(text));
+
+    [Fact]
+    public void DefaultsFor_BusyFolders_AreNewestFirst_OthersByName()
+    {
+        string[] busy = [@"C:\Users\c\Downloads", @"C:\Users\c\Pictures\Screenshots"];
+        var downloads = FolderViews.DefaultsFor(@"c:\users\c\downloads\", busy);
+        Assert.Equal(new FolderView { Path = @"c:\users\c\downloads\", Sort = FenceSort.Date, Newest = FolderViews.BusyFolderNewest }, downloads);
+        Assert.Equal(new FolderView { Path = @"D:\GameLibrary" }, FolderViews.DefaultsFor(@"D:\GameLibrary", busy));
+    }
+
+    [Theory]
+    [InlineData(@"D:\GameLibrary", "GameLibrary")]
+    [InlineData(@"D:\GameLibrary\", "GameLibrary")]
+    [InlineData(@"D:\", "D:")]
+    [InlineData(@"\\nas\share", "share")]
+    public void NameOf_IsTheFolderName_OrTheDrive(string path, string expected) => Assert.Equal(expected, FolderViews.NameOf(path));
+
+    [Fact]
+    public void Status_SaysWhatTheFenceShowsInsteadOfItems()
+    {
+        var view = new FolderView { Path = @"D:\View", Patterns = "*.zip" };
+        Assert.Equal((@"Folder not available: D:\View", null), FolderViews.Status(null, view));
+        Assert.Equal(("This folder is empty", null), FolderViews.Status(FolderViews.Select([], view), view));
+        Assert.Equal(("Nothing here matches this view", null), FolderViews.Status(FolderViews.Select([File("a.txt")], view), view));
+        var many = Enumerable.Range(0, FolderViews.MaxShown + 1234).Select(index => File($"f{index}.zip")).ToList();
+        Assert.Equal((null, "+ 1,234 more — Open folder"), FolderViews.Status(FolderViews.Select(many, view), view));
+    }
+
+    [Fact]
+    public void Normalize_RepairsHandEditedViews()
+    {
+        Assert.Null(FolderViews.Normalize(null));
+        Assert.Null(FolderViews.Normalize(new FolderView { Path = "  " }));
+        var repaired = FolderViews.Normalize(new FolderView { Path = @"D:\X", Show = (ViewShow)9, Sort = FenceSort.Manual, Newest = 9999, Patterns = "a|b" });
+        Assert.Equal(new FolderView { Path = @"D:\X", Newest = FolderViews.MaxNewest }, repaired);
+        Assert.Null(FolderViews.Normalize(new FolderView { Path = @"D:\X", Newest = 0, Patterns = null! })!.Newest);
+        Assert.Equal("", FolderViews.Normalize(new FolderView { Path = @"D:\X", Patterns = null! })!.Patterns);
+    }
+
+    [Fact]
+    public void Kind_TellsItemsLibraryAndViewFencesApart()
+    {
+        Assert.Equal(FenceKind.Items, Fence.Create("a").Kind);
+        Assert.Equal(FenceKind.Library, Fence.Create("Games", isLibrary: true).Kind);
+        Assert.Equal(FenceKind.View, (Fence.Create("v") with { View = new FolderView { Path = @"D:\" } }).Kind);
+    }
+
+    [Fact]
+    public void CreateView_TitlesTheFenceAfterItsFolder()
+    {
+        var (config, fence) = FenceEdits.CreateView(NeoFencesConfig.CreateDefault(), new FolderView { Path = @"C:\Users\c\Downloads", Sort = FenceSort.Date });
+        Assert.Equal("Downloads", fence.Title);
+        Assert.Equal(FenceKind.View, fence.Kind);
+        Assert.Contains(config.Fences, candidate => candidate.Id == fence.Id && candidate.View!.Sort == FenceSort.Date);
+    }
+
+    [Fact]
+    public void SetView_TitleFollowsTheFolder_OnlyWhileItStillHasTheFoldersName()
+    {
+        var (config, fence) = FenceEdits.CreateView(NeoFencesConfig.CreateDefault(), new FolderView { Path = @"D:\Shots" });
+        var moved = FenceEdits.SetView(config, fence.Id, new FolderView { Path = @"D:\Screenshots" });
+        Assert.Equal("Screenshots", moved.Fences.Single(candidate => candidate.Id == fence.Id).Title);
+        var renamed = FenceEdits.Rename(moved, fence.Id, "My pictures");
+        var movedAgain = FenceEdits.SetView(renamed, fence.Id, new FolderView { Path = @"E:\Pics" });
+        Assert.Equal("My pictures", movedAgain.Fences.Single(candidate => candidate.Id == fence.Id).Title);
+    }
+
+    [Fact]
+    public void SetView_RefusesTheLibrary()
+    {
+        var library = Fence.Create("Games", isLibrary: true);
+        var config = NeoFencesConfig.CreateDefault() with { Fences = [library] };
+        Assert.Throws<ArgumentException>(() => FenceEdits.SetView(config, library.Id, new FolderView { Path = @"D:\" }));
+    }
+
+    [Fact]
+    public void Config_KeepsViews_AndTheNormalizerDropsOneOnTheLibrary()
+    {
+        var (config, fence) = FenceEdits.CreateView(NeoFencesConfig.CreateDefault(),
+            new FolderView { Path = @"D:\GameLibrary", Show = ViewShow.Folders, Patterns = "*.lnk", Newest = 20, Sort = FenceSort.Type });
+        var json = ConfigJson.Serialize(config);
+        Assert.DoesNotContain("\"kind\"", json);
+        var back = ConfigNormalizer.Normalize(ConfigJson.Deserialize(json));
+        Assert.Equal(fence.View, back.Fences.Single(candidate => candidate.Id == fence.Id).View);
+
+        var both = Fence.Create("Games", isLibrary: true) with { View = new FolderView { Path = @"D:\" } };
+        var repaired = ConfigNormalizer.Normalize(NeoFencesConfig.CreateDefault() with { Fences = [both] });
+        Assert.Equal(FenceKind.Library, repaired.Fences.Single().Kind);
+        Assert.Null(repaired.Fences.Single().View);
+    }
+}
````

- [ ] **Step 2: Run them to see them fail.** `dotnet test tests/NeoFences.Core.Tests`
  Expected: build fails with `CS0246: The type or namespace name 'FolderViews' could not be found` (and `ViewShow`).

- [ ] **Step 3: Implement.** Write this patch to `m21-1-core.patch` and apply it:

````diff
diff --git a/src/NeoFences.Core/Config/ConfigNormalizer.cs b/src/NeoFences.Core/Config/ConfigNormalizer.cs
index 42c7049..6e48f5b 100644
--- a/src/NeoFences.Core/Config/ConfigNormalizer.cs
+++ b/src/NeoFences.Core/Config/ConfigNormalizer.cs
@@ -40,6 +40,7 @@ public static class ConfigNormalizer
                 IconSize = IconSizes.Contains(loadedFence.IconSize) ? loadedFence.IconSize : 48,
                 CustomColor = Appearance.Argb.FromHex(loadedFence.CustomColor)?.ToHex(), // M14: a broken colour is none
             };
+            fence = fence with { View = fence.IsLibrary ? null : Items.FolderViews.Normalize(loadedFence.View) }; // M21: never on the Library
             seenFenceIds.Add(fence.Id);
             libraryFound |= fence.IsLibrary;
             fences.Add(fence);
diff --git a/src/NeoFences.Core/Items/FolderViews.cs b/src/NeoFences.Core/Items/FolderViews.cs
new file mode 100644
index 0000000..1217902
--- /dev/null
+++ b/src/NeoFences.Core/Items/FolderViews.cs
@@ -0,0 +1,95 @@
+using System.Globalization;
+using System.IO.Enumeration;
+using NeoFences.Core.Model;
+
+namespace NeoFences.Core.Items;
+
+/// <summary>
+/// What a folder view shows of its folder's listing (M21, spec 2026-10-05-folder-views-design §1): the chosen kind, the
+/// patterns, the newest N, the sort, at most <see cref="MaxShown"/> entries. Pure; the listing comes from NeoFences.Shell.
+/// </summary>
+public static class FolderViews
+{
+    public const int MaxShown = 500;
+    public const int MaxNewest = 500;
+
+    /// <summary>Downloads and Screenshots start newest first with this many entries.</summary>
+    public const int BusyFolderNewest = 30;
+
+    /// <param name="Shown">Entry paths in the order shown.</param>
+    /// <param name="Hidden">Entries the view would show but the cap leaves out ("+ N more").</param>
+    /// <param name="Listed">Entries in the folder before any filter ("This folder is empty").</param>
+    public sealed record Selection(IReadOnlyList<string> Shown, int Hidden, int Listed);
+
+    public static Selection Select(IReadOnlyList<ItemInfo> entries, FolderView view)
+    {
+        var patterns = ParsePatterns(view.Patterns) ?? [];
+        IEnumerable<ItemInfo> matching = entries.Where(entry => view.Show switch
+        {
+            ViewShow.Files => !entry.IsFolder,
+            ViewShow.Folders => entry.IsFolder,
+            _ => true,
+        });
+        // Subfolders pass the patterns: a "*.png" view of Screenshots still shows its game folders.
+        if (patterns.Count > 0)
+            matching = matching.Where(entry => entry.IsFolder || patterns.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, entry.Name, ignoreCase: true)));
+        if (view.Newest is { } newest) matching = matching.OrderByDescending(entry => entry.Modified).Take(newest);
+        var ordered = ItemSorting.Order(matching, view.Sort == FenceSort.Manual ? FenceSort.Name : view.Sort);
+        return new Selection([.. ordered.Take(MaxShown)], Math.Max(0, ordered.Count - MaxShown), entries.Count);
+    }
+
+    /// <summary>
+    /// "*.png; .jpg, txt" → ["*.png", "*.jpg", "*.txt"]: split on ; and ,; a bare extension becomes "*.ext". Null when a
+    /// part is not a file-name pattern (a path, or a character Windows forbids in names).
+    /// </summary>
+    public static IReadOnlyList<string>? ParsePatterns(string? patterns)
+    {
+        var parts = (patterns ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
+        var parsed = new List<string>();
+        foreach (var part in parts)
+        {
+            if (part.IndexOfAny(['\\', '/', ':', '"', '<', '>', '|']) >= 0) return null;
+            parsed.Add(part.Contains('*') || part.Contains('?') ? part
+                : part.StartsWith('.') ? "*" + part
+                : part.Contains('.') ? part
+                : "*." + part);
+        }
+        return parsed;
+    }
+
+    /// <summary>A new view of <paramref name="path"/>: busy folders (Downloads, Screenshots) newest first, the rest by name.</summary>
+    public static FolderView DefaultsFor(string path, IReadOnlyList<string> busyFolders) =>
+        busyFolders.Any(busy => SameFolder(busy, path))
+            ? new FolderView { Path = path, Sort = FenceSort.Date, Newest = BusyFolderNewest }
+            : new FolderView { Path = path };
+
+    /// <summary>A hand-edited view repaired; null when it has no folder (the fence then holds items).</summary>
+    public static FolderView? Normalize(FolderView? view)
+    {
+        if (view is null || string.IsNullOrWhiteSpace(view.Path)) return null;
+        return view with
+        {
+            Show = Enum.IsDefined(view.Show) ? view.Show : ViewShow.All,
+            Sort = view.Sort is FenceSort.Name or FenceSort.Type or FenceSort.Date ? view.Sort : FenceSort.Name,
+            Newest = view.Newest is >= 1 and var newest ? Math.Min(newest, MaxNewest) : null,
+            Patterns = ParsePatterns(view.Patterns) is not null ? view.Patterns ?? "" : "",
+        };
+    }
+
+    /// <summary>The folder's own name ("Downloads"), the share ("share" of \\nas\share), or the drive ("D:") for a drive root.</summary>
+    public static string NameOf(string path) => path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? path;
+
+    public static bool SameFolder(string left, string right) =>
+        string.Equals(left.TrimEnd('\\', '/'), right.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
+
+    /// <summary>
+    /// The line a view shows instead of (Center) or under (More) its entries: not available, empty, nothing matching, or
+    /// "+ N more — Open folder" past the cap.
+    /// </summary>
+    public static (string? Center, string? More) Status(Selection? selection, FolderView view) =>
+        selection is null ? ($"Folder not available: {view.Path}", null)
+        : selection.Listed == 0 ? ("This folder is empty", null)
+        : selection.Shown.Count == 0 ? ("Nothing here matches this view", null)
+        : selection.Hidden > 0 ? (null, $"+ {selection.Hidden.ToString("N0", CultureInfo.InvariantCulture)} more — Open folder")
+        : (null, null);
+}
diff --git a/src/NeoFences.Core/Model/Fence.cs b/src/NeoFences.Core/Model/Fence.cs
index f768c32..aa8a62c 100644
--- a/src/NeoFences.Core/Model/Fence.cs
+++ b/src/NeoFences.Core/Model/Fence.cs
@@ -18,6 +18,12 @@ public sealed record Fence
     /// <summary>The Game Library fence (M12): at most one; it lists the library folder, never virtual items.</summary>
     public bool IsLibrary { get; init; }
 
+    /// <summary>A folder view (M21): the fence shows this folder live instead of virtual items. Never set on the Library.</summary>
+    public FolderView? View { get; init; }
+
+    [System.Text.Json.Serialization.JsonIgnore]
+    public FenceKind Kind => IsLibrary ? FenceKind.Library : View is not null ? FenceKind.View : FenceKind.Items;
+
     public int IconSize { get; init; } = 48;
     public bool RolledUp { get; init; }
     public bool Locked { get; init; }
diff --git a/src/NeoFences.Core/Model/FenceEdits.cs b/src/NeoFences.Core/Model/FenceEdits.cs
index d4a6ee7..7c29c32 100644
--- a/src/NeoFences.Core/Model/FenceEdits.cs
+++ b/src/NeoFences.Core/Model/FenceEdits.cs
@@ -60,6 +60,27 @@ public static class FenceEdits
         return (config with { Fences = [.. config.Fences, fence] }, fence);
     }
 
+    /// <summary>A new folder view (M21), titled with its folder's name.</summary>
+    public static (NeoFencesConfig Config, Fence Fence) CreateView(NeoFencesConfig config, FolderView view)
+    {
+        var (created, fence) = CreateFence(config, Items.FolderViews.NameOf(view.Path));
+        fence = fence with { View = view };
+        return (created.WithFence(fence), fence);
+    }
+
+    /// <summary>
+    /// A view's settings or folder changed (M21; its folder renamed, or chosen again): the title follows the folder while it
+    /// still is the old folder's name.
+    /// </summary>
+    /// <exception cref="ArgumentException">No fence with that id, or it is the Game Library.</exception>
+    public static NeoFencesConfig SetView(NeoFencesConfig config, string fenceId, FolderView view)
+    {
+        var fence = Require(config, fenceId);
+        if (fence.IsLibrary) throw new ArgumentException("The Game Library cannot be a folder view.", nameof(fenceId));
+        var follows = fence.View is { } old && fence.Title == Items.FolderViews.NameOf(old.Path) && !Items.FolderViews.SameFolder(old.Path, view.Path);
+        return config.WithFence(fence with { View = view, Title = follows ? Items.FolderViews.NameOf(view.Path) : fence.Title });
+    }
+
     /// <summary>
     /// Deletes a fence (a tab leaves its box first; a host hands the box to the next tab, M9). Its items go with it from
     /// items.json; their targets are never touched (hard rule 1).
diff --git a/src/NeoFences.Core/Model/FolderView.cs b/src/NeoFences.Core/Model/FolderView.cs
new file mode 100644
index 0000000..273d718
--- /dev/null
+++ b/src/NeoFences.Core/Model/FolderView.cs
@@ -0,0 +1,24 @@
+namespace NeoFences.Core.Model;
+
+/// <summary>What a folder view lists (M21): everything, only files, or only subfolders.</summary>
+public enum ViewShow { All, Files, Folders }
+
+/// <summary>The three kinds of fence (M21): virtual items (items.json), the Game Library, or a folder view.</summary>
+public enum FenceKind { Items, Library, View }
+
+/// <summary>
+/// A fence that shows one folder live, read-only (M21, ADR-044): NeoFences never writes to the folder. Sort uses the
+/// fence's "Sort by" values (Manual is read as Name); <see cref="Newest"/> keeps only the latest N entries by date.
+/// </summary>
+public sealed record FolderView
+{
+    public required string Path { get; init; }
+    public ViewShow Show { get; init; } = ViewShow.All;
+    public FenceSort Sort { get; init; } = FenceSort.Name;
+
+    /// <summary>Only the newest N entries (by date), then sorted; null = all of them.</summary>
+    public int? Newest { get; init; }
+
+    /// <summary>File-name patterns, e.g. "*.png;*.jpg"; empty = everything.</summary>
+    public string Patterns { get; init; } = "";
+}
````

- [ ] **Step 4: Run the tests.** `dotnet test tests/NeoFences.Core.Tests`
  Expected: `Passed! - Failed: 0, Passed: 497`.

- [ ] **Step 5: Commit.** `git add -A && git commit -m "feat: added folder views to the core: the view model, selection, patterns, defaults and status"`

### Task 2: Shell — known folders

**Files:**
- Create: `src/NeoFences.Shell/KnownFolders.cs`
- Modify: `src/NeoFences.Shell/NativeMethods.txt` (+`SHGetKnownFolderPath`, `FOLDERID_Downloads`, `FOLDERID_Screenshots`)

**Interfaces:**
- Produces: `KnownFolders.BusyFolders(Action<Exception> logFailure) → IReadOnlyList<string>` (Downloads, Screenshots
  where Windows has them; a failure is logged and skipped).

- [ ] **Step 1: Implement.** Write this patch to `m21-2-shell.patch` and apply it:

````diff
diff --git a/src/NeoFences.Shell/KnownFolders.cs b/src/NeoFences.Shell/KnownFolders.cs
new file mode 100644
index 0000000..98529ec
--- /dev/null
+++ b/src/NeoFences.Shell/KnownFolders.cs
@@ -0,0 +1,32 @@
+using Windows.Win32;
+using Windows.Win32.Foundation;
+using Windows.Win32.UI.Shell;
+
+namespace NeoFences.Shell;
+
+/// <summary>Where Windows keeps the user's busy folders (M21): a new folder view of one of them starts newest first.</summary>
+public static class KnownFolders
+{
+    /// <summary>Downloads and Screenshots, where Windows has them (moved ones included); a failure is logged and skipped.</summary>
+    public static IReadOnlyList<string> BusyFolders(Action<Exception> logFailure) =>
+        [.. new[] { PInvoke.FOLDERID_Downloads, PInvoke.FOLDERID_Screenshots }.Select(folderId => TryGetPath(folderId, logFailure)).OfType<string>()];
+
+    private static unsafe string? TryGetPath(Guid folderId, Action<Exception> logFailure)
+    {
+        PWSTR path = default;
+        try
+        {
+            PInvoke.SHGetKnownFolderPath(&folderId, KNOWN_FOLDER_FLAG.KF_FLAG_DEFAULT, default, &path).ThrowOnFailure();
+            return path.ToString();
+        }
+        catch (Exception failure) when (failure is System.Runtime.InteropServices.COMException or FileNotFoundException or UnauthorizedAccessException)
+        {
+            logFailure(failure); // no such folder on this PC (Screenshots before the first one): no busy-folder default
+            return null;
+        }
+        finally
+        {
+            if (path.Value is not null) PInvoke.CoTaskMemFree(path.Value);
+        }
+    }
+}
diff --git a/src/NeoFences.Shell/NativeMethods.txt b/src/NeoFences.Shell/NativeMethods.txt
index 49a6370..8ae4145 100644
--- a/src/NeoFences.Shell/NativeMethods.txt
+++ b/src/NeoFences.Shell/NativeMethods.txt
@@ -208,3 +208,6 @@ KNOWN_FOLDER_FLAG
 BHID_EnumItems
 IEnumShellItems
 SHCreateItemFromIDList
+SHGetKnownFolderPath
+FOLDERID_Downloads
+FOLDERID_Screenshots
````

- [ ] **Step 2: Build.** `dotnet build` → Expected: `0 Warning(s)`, `0 Error(s)`.
  (No unit test: a thin CsWin32 call; checked live by AG2.)

- [ ] **Step 3: Commit.** `git add -A && git commit -m "feat: added the Downloads and Screenshots known folders to the shell layer"`

### Task 3: App — folder view fences

**Files:**
- Rename: `src/NeoFences.App/LibraryLister.cs` → `src/NeoFences.App/FolderLister.cs` (label, rename callback)
- Create: `src/NeoFences.App/FenceHost.FolderViews.cs`, `src/NeoFences.App/FolderViewWindow.xaml(.cs)`
- Modify: `FenceHost.cs` (listers at start / sync / shutdown, removal notices, game mode, tray "New folder view…",
  window events, `RefreshWindow` renders views, drops only on items fences, refresh after restores),
  `FenceHost.Items.cs` (kind checks; view item menu; "Show as folder view"; view drag-out; view sort),
  `FenceHost.Library.cs`, `FenceHost.Watching.cs` (Refresh re-lists a view), `FenceHost.DesktopFill.cs` (items fences
  only), `FenceWindow.xaml(.cs)` (`Kind`, view menu items, status line and "+ N more" line)

**Interfaces:**
- Consumes: everything Task 1 and Task 2 produce.
- Produces: `FolderLister(string folder, nint noticeOwner, string label, Action<IReadOnlyList<ItemInfo>?> show,
  Action<Exception> logFailure, Action<string, string>? renamed = null)`; `FenceWindow.Kind`,
  `FenceWindow.SetViewStatus(string? center, string? more)`, events `NewFolderViewRequested`, `OpenFolderRequested`,
  `ViewSettingsRequested`; `FolderViewWindow(FolderView)` with `Result`.

- [ ] **Step 1: Implement.** Write this patch to `m21-3-app.patch` and apply it (it renames `LibraryLister.cs`):

````diff
diff --git a/src/NeoFences.App/FenceHost.DesktopFill.cs b/src/NeoFences.App/FenceHost.DesktopFill.cs
index 707dc55..9bb1a3e 100644
--- a/src/NeoFences.App/FenceHost.DesktopFill.cs
+++ b/src/NeoFences.App/FenceHost.DesktopFill.cs
@@ -15,7 +15,7 @@ public sealed partial class FenceHost
     private void ShowDesktopFill()
     {
         if (_desktopFillOpen) return; // one window at a time
-        var fences = _config.Fences.Where(fence => !fence.IsLibrary).Select(fence => (fence.Id, fence.Title)).ToList();
+        var fences = _config.Fences.Where(fence => fence.Kind == FenceKind.Items).Select(fence => (fence.Id, fence.Title)).ToList(); // not the library or views (M21)
         var titles = fences.ToDictionary(fence => fence.Id, fence => fence.Title, StringComparer.Ordinal);
         // Each target already held by an item → a fence holding it (shown as "already in …", unticked).
         var alreadyIn = new Dictionary<string, string>(ItemKinds.Comparer);
diff --git a/src/NeoFences.App/FenceHost.FolderViews.cs b/src/NeoFences.App/FenceHost.FolderViews.cs
new file mode 100644
index 0000000..aef4c44
--- /dev/null
+++ b/src/NeoFences.App/FenceHost.FolderViews.cs
@@ -0,0 +1,231 @@
+using System.Windows.Controls;
+using NeoFences.Core.Items;
+using NeoFences.Core.Layouts;
+using NeoFences.Core.Model;
+using NeoFences.Shell;
+using Serilog;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// Folder views (M21, spec 2026-10-05-folder-views-design, ADR-044): fences that show one folder live, read-only. One
+/// <see cref="FolderLister"/> per view (a hidden tab keeps listing); the entries are paths, like the Game Library's.
+/// NeoFences never writes to the folder: no drop into it, no rename or delete from NeoFences' own menu.
+/// </summary>
+public sealed partial class FenceHost
+{
+    private readonly Dictionary<string, FolderLister> _viewListers = new(StringComparer.Ordinal);
+    // The last listing per view (null: not readable); missing while the first listing is on its way.
+    private readonly Dictionary<string, IReadOnlyList<ItemInfo>?> _viewListings = new(StringComparer.Ordinal);
+    private IReadOnlyList<string>? _busyFolders;
+
+    /// <summary>A lister for every view fence, on its current folder; listers of views that are gone (or changed folder) go.</summary>
+    private void EnsureViewListers()
+    {
+        var views = _config.Fences.Where(fence => fence.View is not null).ToDictionary(fence => fence.Id, fence => fence.View!.Path, StringComparer.Ordinal);
+        foreach (var (fenceId, lister) in _viewListers.ToList())
+        {
+            if (views.TryGetValue(fenceId, out var path) && FolderViews.SameFolder(path, lister.Folder)) continue;
+            lister.Dispose(); // the folder stays as it is
+            _viewListers.Remove(fenceId);
+            _viewListings.Remove(fenceId);
+        }
+        foreach (var (fenceId, path) in views.Where(view => !_viewListers.ContainsKey(view.Key)))
+        {
+            var lister = new FolderLister(path, noticeOwner: _messages.Handle, label: "folder view",
+                show: listed => ShowView(fenceId, listed),
+                logFailure: failure => Log.Warning(failure, "folder view: cannot watch {Folder}", path),
+                renamed: (oldPath, newPath) => OnViewFolderRenamed(fenceId, oldPath, newPath));
+            if (_gameMode) lister.SetPaused(true);
+            _viewListers[fenceId] = lister;
+        }
+    }
+
+    private void StopViewListers()
+    {
+        foreach (var lister in _viewListers.Values) lister.Dispose();
+        _viewListers.Clear();
+        _viewListings.Clear();
+    }
+
+    /// <summary>A listing arrived (on the UI thread): kept, and shown when the view is the shown tab of its box.</summary>
+    private void ShowView(string fenceId, IReadOnlyList<ItemInfo>? listed)
+    {
+        if (!_viewListers.ContainsKey(fenceId)) return; // its fence went meanwhile
+        var wasAvailable = !_viewListings.TryGetValue(fenceId, out var before) || before is not null;
+        _viewListings[fenceId] = listed;
+        if (listed is null && wasAvailable && _config.Fences.FirstOrDefault(fence => fence.Id == fenceId)?.View is { } view)
+            Log.Information("folder view: {Folder} is not available", view.Path); // once per outage, not every 7 s retry
+        if (_windows.Values.FirstOrDefault(window => window.FenceId == fenceId) is { } shown) RefreshWindow(shown);
+    }
+
+    /// <summary>The view's entries as its settings select them, and its status line (RefreshWindow calls this for views).</summary>
+    private void RenderView(FenceWindow window, Fence fence)
+    {
+        if (!_viewListings.TryGetValue(fence.Id, out var listed))
+        {
+            window.SetItems([]); // the first listing is on its way (a slow share): nothing yet, no status
+            window.SetViewStatus(center: null, more: null);
+            return;
+        }
+        var selection = listed is null ? null : FolderViews.Select(listed, fence.View!);
+        window.SetItems(selection is null ? [] : [.. selection.Shown.Select(path => new ShownItem(path, path))]);
+        var (center, more) = FolderViews.Status(selection, fence.View!);
+        window.SetViewStatus(center, more);
+    }
+
+    private void SetViewsPaused(bool paused)
+    {
+        foreach (var lister in _viewListers.Values) lister.SetPaused(paused);
+    }
+
+    /// <summary>Windows asks to remove a drive a view shows (a USB stick): that view lets go and says "not available".</summary>
+    private bool ReleaseViewsForRemoval(nint handle) => _viewListers.Values.Aggregate(false, (released, lister) => lister.ReleaseForRemoval(handle) | released);
+
+    /// <summary>Tray or fence menu → "New folder view…": Windows' folder dialog, then the settings with busy-folder defaults.</summary>
+    private void NewFolderView(nint ownerHandle)
+    {
+        var picked = PathPicker.TryPickFolder(ownerHandle, "Choose a folder to show in a fence",
+            failure => Log.Warning(failure, "folder view: the folder dialog failed"));
+        if (picked is null) return;
+        var dialog = new FolderViewWindow(FolderViews.DefaultsFor(picked, BusyFolders));
+        if (dialog.ShowDialog() != true || dialog.Result is not { } view) return;
+        SetQuickHidden(false); // a new fence must show
+        (_config, var fence) = FenceEdits.CreateView(_config, view);
+        Log.Information("folder view {FenceId} created for {Folder}", fence.Id, view.Path);
+        SyncBoxes();
+        SaveNow();
+    }
+
+    /// <summary>Folder item menu → "Show as folder view": a view of that folder beside the fence, with the defaults.</summary>
+    private void ShowAsFolderView(FenceWindow window, string folder)
+    {
+        (_config, var fence) = FenceEdits.CreateView(_config, FolderViews.DefaultsFor(folder, BusyFolders));
+        if (_monitors.Count > 0 && _config.LastLayoutFingerprint is { } fingerprint && _config.Layouts.TryGetValue(fingerprint, out var layout)
+            && layout.Fences.TryGetValue(window.BoxId, out var besideRect))
+        {
+            var monitor = _monitors.FirstOrDefault(candidate => candidate.DeviceId == besideRect.Monitor) ?? _monitors[0];
+            var box = FencePlacement.ToPixels(besideRect, monitor);
+            var offset = (int)Math.Round(40 * monitor.Scale);
+            var placed = box with { X = box.X + offset, Y = box.Y + offset }; // like a detached tab (M9); the layout keeps it on screen
+            _config = LayoutEngine.WithFenceRect(_config, fingerprint: fingerprint, fenceId: fence.Id,
+                rect: FencePlacement.FromPixels(placed, FencePlacement.ContainingMonitor(placed, _monitors)));
+        }
+        Log.Information("folder view {FenceId} created for {Folder} from an item", fence.Id, folder);
+        SyncBoxes();
+        SaveNow();
+    }
+
+    /// <summary>View fence menu → "Folder view settings…".</summary>
+    private void EditFolderView(FenceWindow window)
+    {
+        if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId)?.View is not { } current) return;
+        var dialog = new FolderViewWindow(current) { Owner = window };
+        if (dialog.ShowDialog() != true || dialog.Result is not { } view) return;
+        SetView(window.FenceId, view);
+    }
+
+    /// <summary>A view's new settings: saved, its lister on the (new) folder, its window, title and menus updated.</summary>
+    private void SetView(string fenceId, FolderView view)
+    {
+        _config = FenceEdits.SetView(_config, fenceId, view);
+        EnsureViewListers();
+        if (_windows.Values.FirstOrDefault(window => window.FenceId == fenceId) is { } window && _config.Fences.First(fence => fence.Id == fenceId) is var fence)
+        {
+            window.Refresh(fence);
+            window.SetTitle(fence.Title);
+            RefreshTabs(window);
+            RefreshWindow(window);
+        }
+        ScheduleSave();
+    }
+
+    /// <summary>"Sort by" on a view: its own sort, kept and live (M21), not the one-time reorder of items.</summary>
+    private void SortView(FenceWindow window, FenceSort sort)
+    {
+        if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId)?.View is { } view) SetView(window.FenceId, view with { Sort = sort });
+    }
+
+    /// <summary>The view's folder was renamed in place (Explorer): the view follows it, its title too while it is the folder's name.</summary>
+    private void OnViewFolderRenamed(string fenceId, string oldPath, string newPath)
+    {
+        if (_config.Fences.FirstOrDefault(fence => fence.Id == fenceId)?.View is not { } view || !FolderViews.SameFolder(view.Path, oldPath)) return;
+        Log.Information("folder view {FenceId}: {OldPath} renamed to {NewPath}; following it", fenceId, oldPath, newPath);
+        SetView(fenceId, view with { Path = newPath });
+    }
+
+    /// <summary>"Open folder" (the view's menu or its "+ N more" line): Explorer at the view's folder.</summary>
+    private void OpenViewFolder(FenceWindow window)
+    {
+        if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId)?.View is { } view) OpenItem(view.Path, ownerHandle: window.Handle);
+    }
+
+    /// <summary>
+    /// Right-click on a view's entries: NeoFences' safe menu (open, show, copy, add to a fence). Shift+right-click: Windows'
+    /// menu for the real entries, under a line saying so — only there can a real rename or delete happen.
+    /// </summary>
+    private void ShowViewItemMenu(FenceWindow window, IReadOnlyList<string> paths, bool extended, int screenX, int screenY, bool fromKeyboard)
+    {
+        if (paths.Count == 0) return;
+        if (extended)
+        {
+            ShowViewWindowsMenu(window, paths, screenX, screenY);
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
+    /// <summary>Windows' menu for a view's entries, after a 2 s check of the folder off the UI thread (a share may have gone, M19 R2).</summary>
+    private void ShowViewWindowsMenu(FenceWindow window, IReadOnlyList<string> paths, int screenX, int screenY)
+    {
+        var folder = System.IO.Path.GetDirectoryName(paths[0]) ?? paths[0];
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
+    /// <summary>"Add to fence ▸": the entries become virtual items at the end of that fence, which then shows them selected.</summary>
+    private void AddToFence(string fenceId, IReadOnlyList<string> paths)
+    {
+        if (!_config.Fences.Any(fence => fence.Id == fenceId && fence.Kind == FenceKind.Items)) return;
+        var added = ItemEdits.Add(_items, fenceId, [.. paths.Select(VirtualItem.Create)]);
+        _items = added.Document;
+        Log.Information("{Added} item(s) added to fence {FenceId} from a folder view; {Already} already there", added.AddedIds.Count, fenceId, added.AlreadyThereIds.Count);
+        ItemsChanged(checkTargets: paths);
+        if (_windows.Values.FirstOrDefault(window => window.FenceId == fenceId) is { } shown) shown.SelectItems([.. added.AddedIds, .. added.AlreadyThereIds]);
+    }
+
+    /// <summary>Downloads and Screenshots, asked once (a local call; a missing one is logged and left out).</summary>
+    private IReadOnlyList<string> BusyFolders => _busyFolders ??= KnownFolders.BusyFolders(failure => Log.Information(failure, "folder view: a known folder is not available"));
+}
diff --git a/src/NeoFences.App/FenceHost.Items.cs b/src/NeoFences.App/FenceHost.Items.cs
index 450135c..38ce482 100644
--- a/src/NeoFences.App/FenceHost.Items.cs
+++ b/src/NeoFences.App/FenceHost.Items.cs
@@ -26,7 +26,7 @@ public sealed partial class FenceHost
     /// <summary>Double-click / Enter. Peek ends once something is opened from it (like Fences).</summary>
     private void OpenKey(FenceWindow window, string key)
     {
-        if (window.IsLibrary) OpenItem(key, ownerHandle: window.Handle); // the library's key is its shortcut's path
+        if (window.Kind != FenceKind.Items) OpenItem(key, ownerHandle: window.Handle); // the library's and a view's key is a path (M12, M21)
         else if (_items.Find(key) is { } item) OpenVirtualItem(window, item, runAsAdmin: item.RunAsAdmin);
         SetPeek(false);
     }
@@ -89,6 +89,11 @@ public sealed partial class FenceHost
             ShowLibraryItemMenu(window, keys, screenX, screenY, extended: shift);
             return;
         }
+        if (window.Kind == FenceKind.View)
+        {
+            ShowViewItemMenu(window, keys, extended: shift, screenX, screenY, fromKeyboard); // M21
+            return;
+        }
         var items = keys.Select(_items.Find).OfType<VirtualItem>().ToList();
         if (items.Count == 0) return;
         if (shift)
@@ -122,6 +127,7 @@ public sealed partial class FenceHost
             Command("Open", () => OpenVirtualItem(window, item, runAsAdmin: item.RunAsAdmin));
             if (onDisk && !check.IsFolder) Command("Run as administrator", () => OpenVirtualItem(window, item, runAsAdmin: true));
             if (onDisk) Command("Open file location", () => ShowInFolder(item.Target));
+            if (onDisk && check.IsFolder && check.State == TargetState.Ok) Command("Show as folder view", () => ShowAsFolderView(window, item.Target)); // M21
             Command("Copy path", () => CopyText(item.Target));
             menu.Items.Add(new Separator());
             Command("Properties…", () => ShowProperties(window, item.Id, focusName: false));
@@ -192,6 +198,7 @@ public sealed partial class FenceHost
             HideGames(keys); // Delete in the library hides the game; its shortcut is NeoFences' own (M12)
             return;
         }
+        if (window.Kind == FenceKind.View) return; // a view shows its folder as it is; nothing to remove (M21)
         if (keys.Count > 1 && MessageBox.Show(window, $"Remove {keys.Count} items from this fence?\n\nYour files, folders and apps are not touched.",
                 "NeoFences", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
         _items = ItemEdits.Remove(_items, keys.ToHashSet(StringComparer.Ordinal));
@@ -202,7 +209,7 @@ public sealed partial class FenceHost
     /// <summary>Properties (F2: the name selected; Alt+Enter; the menu): changes only the item (spec §3).</summary>
     private void ShowProperties(FenceWindow window, string key, bool focusName)
     {
-        if (window.IsLibrary || _items.Find(key) is not { } item) return;
+        if (window.Kind != FenceKind.Items || _items.Find(key) is not { } item) return;
         var dialog = new ItemPropertiesWindow(item, adding: false, iconLoader: _iconLoader, focusName: focusName) { Owner = window };
         if (dialog.ShowDialog() != true || dialog.Result is not { } changed) return;
         changed = WithCopiedPicture(window, changed, dialog.PictureToCopy);
@@ -214,7 +221,7 @@ public sealed partial class FenceHost
     /// <summary>Fence menu → "Add item…" (spec §2): a file, folder, app or website, with its own name if wanted.</summary>
     private void AddItem(FenceWindow window)
     {
-        if (window.IsLibrary) return;
+        if (window.Kind != FenceKind.Items) return;
         var dialog = new ItemPropertiesWindow(VirtualItem.Create(""), adding: true, iconLoader: _iconLoader, focusName: false) { Owner = window };
         if (dialog.ShowDialog() != true || dialog.Result is not { } created) return;
         var added = ItemEdits.Add(_items, window.FenceId, [WithCopiedPicture(window, created, dialog.PictureToCopy)]);
@@ -401,9 +408,9 @@ public sealed partial class FenceHost
     /// <summary>Items dragged from a fence: moved here (Ctrl: duplicated). Library games dragged in become items of their shortcut.</summary>
     private void OnItemsDropped(FenceWindow window, IReadOnlyList<string> keys, int insertAt, bool duplicate)
     {
-        if (window.IsLibrary) return;
+        if (window.Kind != FenceKind.Items) return;
         var known = keys.Where(key => _items.Find(key) is not null).ToList();
-        var fromLibrary = keys.Except(known).ToList(); // the library's key is its shortcut's path (M12)
+        var fromLibrary = keys.Except(known).ToList(); // the library's and a view's key is a path (M12, M21): they become items
         if (known.Count > 0)
         {
             _items = duplicate ? ItemEdits.Duplicate(_items, known, window.FenceId, insertAt).Document : ItemEdits.Move(_items, known, window.FenceId, insertAt);
@@ -416,7 +423,7 @@ public sealed partial class FenceHost
     /// <summary>Files, folders or a link from outside: new items at the drop point; the originals stay where they are.</summary>
     private void OnTargetsDropped(FenceWindow window, IReadOnlyList<string> targets, int insertAt)
     {
-        if (!window.IsLibrary && targets.Count > 0) AddTargets(window, targets, insertAt);
+        if (window.Kind == FenceKind.Items && targets.Count > 0) AddTargets(window, targets, insertAt);
     }
 
     private void AddTargets(FenceWindow window, IReadOnlyList<string> targets, int insertAt)
@@ -436,6 +443,17 @@ public sealed partial class FenceHost
         {
             (files, urls) = (keys, []); // NeoFences' own shortcuts (M12)
         }
+        else if (window.Kind == FenceKind.View)
+        {
+            // A view's entries are its folder's: asked once now, at most 2 s (M19 R2), so a share gone since the listing cannot freeze the drag.
+            var folder = Path.GetDirectoryName(keys[0]) ?? keys[0];
+            if (TargetProbe.Check(folder).State != TargetState.Ok)
+            {
+                Log.Information("drag from folder view: {Folder} is not reachable", folder);
+                return;
+            }
+            (files, urls) = (keys, []);
+        }
         else
         {
             var items = keys.Select(_items.Find).OfType<VirtualItem>().ToList();
@@ -457,6 +475,11 @@ public sealed partial class FenceHost
     private void SortFence(FenceWindow window, FenceSort sort)
     {
         if (window.IsLibrary) return;
+        if (window.Kind == FenceKind.View)
+        {
+            SortView(window, sort); // M21: the view's own sort, kept
+            return;
+        }
         var fenceId = window.FenceId;
         var items = _items.Of(fenceId);
         // Targets not seen OK are sorted without asking their disk (a dead share answers only after its timeout).
diff --git a/src/NeoFences.App/FenceHost.Library.cs b/src/NeoFences.App/FenceHost.Library.cs
index 9d977cf..9da6ff6 100644
--- a/src/NeoFences.App/FenceHost.Library.cs
+++ b/src/NeoFences.App/FenceHost.Library.cs
@@ -10,7 +10,7 @@ namespace NeoFences.App;
 /// <summary>
 /// The Game Library fence (M12, spec 2026-10-03-game-library-design, ADR-032): scans the launchers, the Xbox app, the
 /// user's game folders and Desktop game shortcuts on its own STA thread, merges them (Core <see cref="GameCatalog"/>),
-/// keeps one shortcut per game in <see cref="AppPaths.LibraryDirectory"/> and shows that folder (LibraryLister) as tiles.
+/// keeps one shortcut per game in <see cref="AppPaths.LibraryDirectory"/> and shows that folder (FolderLister) as tiles.
 /// Nothing is started or changed outside NeoFences' own folder.
 /// </summary>
 public sealed partial class FenceHost
@@ -40,7 +40,7 @@ public sealed partial class FenceHost
         else if (hasFence && _libraryLister is null)
         {
             TryCreateFolder(AppPaths.LibraryDirectory);
-            _libraryLister = new LibraryLister(AppPaths.LibraryDirectory, noticeOwner: _messages.Handle, show: ShowLibrary,
+            _libraryLister = new FolderLister(AppPaths.LibraryDirectory, noticeOwner: _messages.Handle, label: "library folder", show: ShowLibrary,
                 logFailure: failure => Log.Warning(failure, "cannot watch the game library folder"));
             if (_gameMode) _libraryLister.SetPaused(true);
         }
diff --git a/src/NeoFences.App/FenceHost.Watching.cs b/src/NeoFences.App/FenceHost.Watching.cs
index a71c368..4692ca9 100644
--- a/src/NeoFences.App/FenceHost.Watching.cs
+++ b/src/NeoFences.App/FenceHost.Watching.cs
@@ -23,7 +23,7 @@ public sealed partial class FenceHost
     private readonly Dictionary<string, (FolderWatcher Watcher, DeviceRemovalNotice? Notice)> _targetWatchers = new(ItemKinds.Comparer);
     private HashSet<string> _wantedFolders = new(ItemKinds.Comparer);
     private readonly HashSet<string> _armingFolders = new(ItemKinds.Comparer); // watchers being opened off the UI thread
-    // Removal notices registered by a batch still arming, by handle (M19 R1, like LibraryLister._inFlight): a drive removed
+    // Removal notices registered by a batch still arming, by handle (M19 R1, like FolderLister._inFlight): a drive removed
     // meanwhile is let go of at once; the batch then drops that folder.
     private readonly System.Collections.Concurrent.ConcurrentDictionary<nint, (FolderWatcher Watcher, DeviceRemovalNotice Notice)> _armingNotices = new();
     private readonly RefreshThrottle _refreshThrottle = new();
@@ -255,6 +255,7 @@ public sealed partial class FenceHost
             ScanLibrary(full: true);
             return;
         }
+        if (_viewListers.TryGetValue(window.FenceId, out var viewLister)) viewLister.Refresh(); // a folder view lists again (M21)
         CheckFence(window.FenceId);
         window.ReloadIcons();
     }
diff --git a/src/NeoFences.App/FenceHost.cs b/src/NeoFences.App/FenceHost.cs
index b60556d..395a99a 100644
--- a/src/NeoFences.App/FenceHost.cs
+++ b/src/NeoFences.App/FenceHost.cs
@@ -47,7 +47,7 @@ public sealed partial class FenceHost
     private readonly HashSet<string> _loggedSnapshotProblems = new(StringComparer.OrdinalIgnoreCase);
     private readonly DispatcherTimer _specialIconsTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
     private readonly Dictionary<string, IDisposable> _dropRegistrations = new(StringComparer.Ordinal);
-    private LibraryLister? _libraryLister; // the Game Library fence's folder (M12), while that fence exists
+    private FolderLister? _libraryLister; // the Game Library fence's folder (M12), while that fence exists
     private NeoFencesConfig _config = NeoFencesConfig.CreateDefault();
     private ItemsDocument _items = new();
     private IReadOnlyList<MonitorPlacement> _monitors = [];
@@ -78,6 +78,7 @@ public sealed partial class FenceHost
     private const int TrayNewLibrary = 11; // M12
     private const int TraySnapshotsSettings = 12; // M13c: "More in Settings…" opens the Snapshots card
     private const int TrayAddFromDesktop = 14; // M19 §3 (13 is TrayRestartToUpdate)
+    private const int TrayNewFolderView = 15; // M21
     private SettingsWindow? _settingsWindow; // M6b: one at a time
 
     public event Action? ExitRequested;
@@ -98,6 +99,7 @@ public sealed partial class FenceHost
         {
             if (_libraryLister?.ReleaseForRemoval(handle) == true) Log.Information("the library folder's drive is being removed: released it");
             if (ReleaseLibraryForRemoval(handle)) Log.Information("a drive the game library watches is being removed: released it");
+            if (ReleaseViewsForRemoval(handle)) Log.Information("a drive a folder view shows is being removed: released it");
             if (ReleaseTargetWatcherForRemoval(handle)) Log.Information("a drive holding item targets is being removed: released it");
         };
         _specialIconsTimer.Tick += (_, _) => RefreshSpecialIcons();
@@ -120,6 +122,7 @@ public sealed partial class FenceHost
         RefreshMonitors();
         foreach (var box in FenceTabs.Boxes(_config)) OpenWindow(box); // one window per box (M9)
         EnsureLibraryLister();
+        EnsureViewListers(); // M21
         RefreshWindows();
         StartSpecialIconNotifications();
         ApplyLayout();
@@ -194,6 +197,7 @@ public sealed partial class FenceHost
         StopWatching();
         _specialIcons?.Dispose();
         _libraryLister?.Dispose();
+        StopViewListers();
         StopLibraryWatchers();
         _libraryTimer?.Stop();
         _shellWorker.Dispose();
@@ -258,6 +262,9 @@ public sealed partial class FenceHost
         window.RefreshRequested += () => RefreshFence(window);
         window.DrivesChanged += OnDrivesChanged;
         window.NewLibraryRequested += CreateLibraryFence;
+        window.NewFolderViewRequested += () => NewFolderView(window.Handle); // M21
+        window.OpenFolderRequested += () => OpenViewFolder(window);
+        window.ViewSettingsRequested += () => EditFolderView(window);
         window.StartupToggled += SetStartWithWindows;
         window.SettingsRequested += OpenSettings;
         window.LabelModeRequested += labels => SetFenceLabels(window, labels);
@@ -308,7 +315,7 @@ public sealed partial class FenceHost
         Log.Information("special icons refreshed");
     }
 
-    /// <summary>Every window shows its fence's items as they are now (the library lists itself, LibraryLister).</summary>
+    /// <summary>Every window shows its fence's items as they are now (the library lists itself, FolderLister; views show their last listing).</summary>
     private void RefreshWindows()
     {
         foreach (var window in _windows.Values) RefreshWindow(window);
@@ -317,6 +324,11 @@ public sealed partial class FenceHost
     private void RefreshWindow(FenceWindow window)
     {
         if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is not { IsLibrary: false } shown) return;
+        if (shown.View is not null)
+        {
+            RenderView(window, shown); // M21
+            return;
+        }
         window.SetItems([.. _items.Of(shown.Id).Select(item => new ShownItem(item.Id, item.Target, item.OwnName, item.Icon, item.Note, StateOf(item.Target)))]);
     }
 
@@ -339,7 +351,7 @@ public sealed partial class FenceHost
         {
             _dropRegistrations[window.BoxId] = ShellDragDrop.RegisterFence(window.Handle, new FenceDropHandlers(
                 HitTest: window.HitTest,
-                AcceptsDrops: () => !window.IsLibrary, // the library shows NeoFences' own shortcuts only (M12)
+                AcceptsDrops: () => window.Kind == FenceKind.Items, // the library shows its own shortcuts (M12); a view never writes to its folder (M21)
                 ItemsDropped: (keys, insertAt, duplicate) => OnItemsDropped(window, keys, insertAt, duplicate),
                 TargetsDropped: (targets, insertAt) => OnTargetsDropped(window, targets, insertAt),
                 ShowFeedback: window.ShowDropFeedback,
@@ -524,6 +536,7 @@ public sealed partial class FenceHost
         }
         foreach (var box in boxes.Where(box => !_windows.ContainsKey(box.Id))) OpenWindow(box);
         EnsureLibraryLister();
+        EnsureViewListers(); // M21
         foreach (var box in boxes)
         {
             var window = _windows[box.Id];
@@ -943,6 +956,7 @@ public sealed partial class FenceHost
             window.Refresh(shown);
             window.SetTitle(shown.Title);
         }
+        RefreshWindows(); // a fence that became (or stopped being) a folder view shows its new kind (M21)
         ForgetGoneTargets(); // records of items the restore took away (M20)
         CheckAllTargets(); // the restored items' targets may have changed since
         _settingsWindow?.ShowSnapshotNotice($"Restored \"{snapshot.Name}\".", failed: false); // replaces an earlier failure line (final review M1)
@@ -1089,6 +1103,7 @@ public sealed partial class FenceHost
         }
         UpdateMouseHook();
         _libraryLister?.SetPaused(gameMode);
+        SetViewsPaused(gameMode); // M21
         if (!gameMode) ApplyDeferredShellWork();
         UpdatePeekHotkey();
         _trayIcon?.SetTooltip(TrayTooltip());
@@ -1166,6 +1181,7 @@ public sealed partial class FenceHost
             .. UpdateTrayItems(), // M17: "Restart to update to v…" first while an update waits
             new TrayMenuItem(TrayNewFence, "New fence", Enabled: !_paused),
             new TrayMenuItem(TrayNewLibrary, "New Game Library fence", Enabled: !_paused),
+            new TrayMenuItem(TrayNewFolderView, "New folder view…", Enabled: !_paused),
             new TrayMenuItem(TrayAddFromDesktop, "Add from desktop…", Enabled: !_paused),
             new TrayMenuItem(TrayQuickHide, "Quick-hide", Checked: _quickHidden, Enabled: !_paused),
             new TrayMenuItem(TrayPeek, $"Peek\t{PeekHotkeyDisplay}", Checked: _peeking, Enabled: !_paused),
@@ -1185,6 +1201,7 @@ public sealed partial class FenceHost
                 CreateFence();
                 break;
             case TrayNewLibrary: CreateLibraryFence(); break;
+            case TrayNewFolderView: NewFolderView(ownerHandle: 0); break;
             case TrayAddFromDesktop:
                 SetQuickHidden(false); // the new items must be seen landing
                 ShowDesktopFill();
diff --git a/src/NeoFences.App/FenceWindow.xaml b/src/NeoFences.App/FenceWindow.xaml
index 9dbac79..67bd73d 100644
--- a/src/NeoFences.App/FenceWindow.xaml
+++ b/src/NeoFences.App/FenceWindow.xaml
@@ -79,9 +79,13 @@
                         <MenuItem x:Name="AddItemItem" Header="Add item…" />
                         <MenuItem x:Name="AddFromDesktopItem" Header="Add from desktop…" />
                         <MenuItem x:Name="RefreshItem" Header="Refresh" />
+                        <!-- Folder views (M21): shown only on a view. -->
+                        <MenuItem x:Name="OpenFolderItem" Header="Open folder" Visibility="Collapsed" />
+                        <MenuItem x:Name="ViewSettingsItem" Header="Folder view settings…" Visibility="Collapsed" />
                         <Separator />
                         <MenuItem x:Name="NewFenceItem" Header="New fence" />
                         <MenuItem x:Name="NewLibraryItem" Header="New Game Library fence" />
+                        <MenuItem x:Name="NewFolderViewItem" Header="New folder view…" />
                         <MenuItem x:Name="RenameItem" Header="Rename fence" />
                         <MenuItem x:Name="IconSizeItem" Header="Icon size" />
                         <MenuItem x:Name="LabelsItem" Header="Labels">
@@ -101,6 +105,10 @@
                     </ContextMenu>
                 </Border.ContextMenu>
                 <Grid>
+                    <Grid.RowDefinitions>
+                        <RowDefinition />
+                        <RowDefinition Height="Auto" />
+                    </Grid.RowDefinitions>
                     <ListBox x:Name="ItemList" Background="Transparent" BorderThickness="0" Padding="4"
                              SelectionMode="Extended" ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                              ScrollViewer.VerticalScrollBarVisibility="Auto">
@@ -204,10 +212,14 @@
                             </DataTemplate>
                         </ListBox.ItemTemplate>
                     </ListBox>
-                    <!-- An empty fence says how to fill it (M18 spec §5). -->
+                    <!-- An empty fence says how to fill it (M18 spec §5); a folder view says why it shows nothing (M21). -->
                     <TextBlock x:Name="EmptyHint" Visibility="Collapsed" Margin="12" TextWrapping="Wrap" TextAlignment="Center"
-                               VerticalAlignment="Center" Foreground="{DynamicResource FenceSubtleText}" IsHitTestVisible="False"
-                               Text="Drop files, folders or links here — or right-click → Add item…" />
+                               VerticalAlignment="Center" Foreground="{DynamicResource FenceSubtleText}" IsHitTestVisible="False" />
+                    <!-- A folder view past its cap (M21): "+ N more — Open folder", clickable. -->
+                    <Border x:Name="MoreLine" Grid.Row="1" Visibility="Collapsed" Padding="8,3,8,5" Cursor="Hand" Background="#01000000">
+                        <TextBlock x:Name="MoreText" Foreground="{DynamicResource FenceSubtleText}" FontSize="12" TextAlignment="Center"
+                                   TextDecorations="Underline" />
+                    </Border>
                     <!-- Rubber-band selection (M3b): drawn while dragging on empty space. -->
                     <Canvas IsHitTestVisible="False" ClipToBounds="True">
                         <Rectangle x:Name="SelectionBand" Visibility="Collapsed" Fill="{DynamicResource FenceHover}"
diff --git a/src/NeoFences.App/FenceWindow.xaml.cs b/src/NeoFences.App/FenceWindow.xaml.cs
index c3b7f72..4161bd9 100644
--- a/src/NeoFences.App/FenceWindow.xaml.cs
+++ b/src/NeoFences.App/FenceWindow.xaml.cs
@@ -44,7 +44,9 @@ public partial class FenceWindow : Window
     private int _iconSizeDips;
     private bool _renaming;
     private string _title = "";
-    private bool _isLibrary; // the shown tab is the Game Library (M12): tiles, its own menu items
+    private FenceKind _kind; // the shown tab: items, the Game Library (M12: tiles, its own menu) or a folder view (M21: read-only)
+    private const string ItemsHint = "Drop files, folders or links here — or right-click → Add item…";
+    private string? _viewStatus; // a folder view's line instead of entries: not available, empty, nothing matching (M21)
     private IReadOnlyDictionary<string, (string Path, bool IsPoster)> _libraryArt = new Dictionary<string, (string, bool)>();
     private DragTracker? _drag;
     private bool _locked;
@@ -147,6 +149,12 @@ public partial class FenceWindow : Window
     public event Action? RefreshRequested;
     /// <summary>Fence menu → "New Game Library fence" (M12).</summary>
     public event Action? NewLibraryRequested;
+    /// <summary>Fence menu → "New folder view…" (M21).</summary>
+    public event Action? NewFolderViewRequested;
+    /// <summary>A folder view's "Open folder" (its menu, or the "+ N more" line).</summary>
+    public event Action? OpenFolderRequested;
+    /// <summary>A folder view's "Folder view settings…".</summary>
+    public event Action? ViewSettingsRequested;
     /// <summary>A drive arrived or was removed (Windows tells top-level windows): missing and unavailable items are checked again.</summary>
     public event Action? DrivesChanged;
     /// <summary>The "Start with Windows" toggle changed (ADR-019).</summary>
@@ -198,6 +206,10 @@ public partial class FenceWindow : Window
         TitleBar.SizeChanged += (_, _) => UpdateTabStripWidth();
         PreviewKeyDown += OnTabKeys;
         NewLibraryItem.Click += (_, _) => NewLibraryRequested?.Invoke();
+        NewFolderViewItem.Click += (_, _) => NewFolderViewRequested?.Invoke();
+        OpenFolderItem.Click += (_, _) => OpenFolderRequested?.Invoke();
+        ViewSettingsItem.Click += (_, _) => ViewSettingsRequested?.Invoke();
+        MoreLine.MouseLeftButtonUp += (_, click) => { click.Handled = true; OpenFolderRequested?.Invoke(); };
         AddItemItem.Click += (_, _) => AddItemRequested?.Invoke();
         AddFromDesktopItem.Click += (_, _) => AddFromDesktopRequested?.Invoke();
         RefreshItem.Click += (_, _) => RefreshRequested?.Invoke();
@@ -279,12 +291,24 @@ public partial class FenceWindow : Window
     {
         _title = fence.Title;
         TitleText.Text = fence.Title;
-        _isLibrary = fence.IsLibrary;
-        // The library is NeoFences' own A–Z list of games: no items to add or sort (M12).
-        AddItemItem.Visibility = _isLibrary ? Visibility.Collapsed : Visibility.Visible;
-        AddFromDesktopItem.Visibility = _isLibrary ? Visibility.Collapsed : Visibility.Visible;
-        SortItem.Visibility = _isLibrary ? Visibility.Collapsed : Visibility.Visible;
-        DeleteItem.Header = _isLibrary ? "Delete fence (your games are not touched)" : "Delete fence (your files are not touched)";
+        _kind = fence.Kind;
+        // The library is NeoFences' own A–Z list of games: no items to add or sort (M12). A folder view shows its folder
+        // read-only: nothing to add; "Sort by" is its own, kept (M21).
+        var items = _kind == FenceKind.Items;
+        var view = _kind == FenceKind.View;
+        AddItemItem.Visibility = items ? Visibility.Visible : Visibility.Collapsed;
+        AddFromDesktopItem.Visibility = items ? Visibility.Visible : Visibility.Collapsed;
+        SortItem.Visibility = _kind == FenceKind.Library ? Visibility.Collapsed : Visibility.Visible;
+        foreach (var sortItem in SortItem.Items.OfType<MenuItem>()) sortItem.IsChecked = view && Equals(sortItem.Tag, fence.View!.Sort);
+        OpenFolderItem.Visibility = view ? Visibility.Visible : Visibility.Collapsed;
+        ViewSettingsItem.Visibility = view ? Visibility.Visible : Visibility.Collapsed;
+        DeleteItem.Header = _kind switch
+        {
+            FenceKind.Library => "Delete fence (your games are not touched)",
+            FenceKind.View => "Delete fence (the folder is not touched)",
+            _ => "Delete fence (your files are not touched)",
+        };
+        if (!view) SetViewStatus(center: null, more: null);
         UpdateEmptyHint();
         _labelMode = fence.Labels;
         SetIconSize(fence.IconSize);
@@ -292,7 +316,22 @@ public partial class FenceWindow : Window
     }
 
     /// <summary>The shown tab is the Game Library (M12).</summary>
-    public bool IsLibrary => _isLibrary;
+    public bool IsLibrary => _kind == FenceKind.Library;
+
+    /// <summary>The shown tab's kind (M21): items, the Game Library, or a folder view.</summary>
+    public FenceKind Kind => _kind;
+
+    /// <summary>
+    /// A folder view's status (M21 spec §3): a line instead of entries (not available, empty, nothing matching), and a
+    /// clickable "+ N more — Open folder" under them.
+    /// </summary>
+    public void SetViewStatus(string? center, string? more)
+    {
+        _viewStatus = center;
+        MoreText.Text = more ?? "";
+        MoreLine.Visibility = more is null ? Visibility.Collapsed : Visibility.Visible;
+        UpdateEmptyHint();
+    }
 
     /// <summary>The library's tile art by item ref: a 2:3 poster, or a logo shown centred (M12).</summary>
     public void SetLibraryArt(IReadOnlyDictionary<string, (string Path, bool IsPoster)> art)
@@ -303,8 +342,8 @@ public partial class FenceWindow : Window
 
     private void ApplyArt(FenceItemView view)
     {
-        view.IsTile = _isLibrary;
-        if (!_isLibrary || !_libraryArt.TryGetValue(view.Key, out var art))
+        view.IsTile = IsLibrary;
+        if (!IsLibrary || !_libraryArt.TryGetValue(view.Key, out var art))
         {
             view.ArtPath = null;
             view.Art = null;
@@ -625,7 +664,13 @@ public partial class FenceWindow : Window
     }
 
     /// <summary>An empty fence (not the library) says how to fill it (spec §5).</summary>
-    private void UpdateEmptyHint() => EmptyHint.Visibility = !_isLibrary && _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
+    private void UpdateEmptyHint()
+    {
+        // A folder view shows its status line in the same place (M21).
+        var text = _kind == FenceKind.View ? _viewStatus : _kind == FenceKind.Items && _items.Count == 0 ? ItemsHint : null;
+        EmptyHint.Text = text ?? "";
+        EmptyHint.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
+    }
 
     /// <summary>These items selected and scrolled into view: a drop of what the fence already holds shows where it is (spec §2).</summary>
     public void SelectItems(IReadOnlyCollection<string> keys)
@@ -667,7 +712,7 @@ public partial class FenceWindow : Window
     /// <summary>Covers are decoded at the tile's pixel width: a new icon size or monitor DPI decodes them again (final review I3).</summary>
     private void ReloadArt()
     {
-        if (!_isLibrary) return;
+        if (!IsLibrary) return;
         foreach (var view in _items)
         {
             view.ArtPath = null;
@@ -696,7 +741,7 @@ public partial class FenceWindow : Window
         Resources["TileWidth"] = Math.Round(_iconSizeDips * 1.5);
         Resources["TileHeight"] = Math.Round(_iconSizeDips * 2.25);
         // With labels: room for two short words under small icons. Icons only: a tight grid.
-        Resources["ItemWidth"] = _isLibrary ? Math.Round(_iconSizeDips * 1.5) + 12.0
+        Resources["ItemWidth"] = IsLibrary ? Math.Round(_iconSizeDips * 1.5) + 12.0
             : _labelMode == LabelMode.Always ? LabelledItemWidth : _iconSizeDips + 12.0;
     }
 
@@ -1065,7 +1110,7 @@ public partial class FenceWindow : Window
         var key = args.Key == Key.System ? args.SystemKey : args.Key; // Alt+Enter arrives as Key.System
         switch (key)
         {
-            case Key.Enter when Keyboard.Modifiers == ModifierKeys.Alt && selected.Count == 1 && !_isLibrary:
+            case Key.Enter when Keyboard.Modifiers == ModifierKeys.Alt && selected.Count == 1 && _kind == FenceKind.Items:
                 PropertiesRequested?.Invoke(selected[0].Key, false);
                 break;
             case Key.Enter when selected.Count == 1:
@@ -1077,7 +1122,7 @@ public partial class FenceWindow : Window
             case Key.Delete when selected.Count > 0:
                 RemoveRequested?.Invoke(selected.Select(view => view.Key).ToList()); // Shift+Del too: only the item goes
                 break;
-            case Key.F2 when selected.Count == 1 && !_isLibrary: // library shortcuts are named by their game (M12)
+            case Key.F2 when selected.Count == 1 && _kind == FenceKind.Items: // library shortcuts are named by their game (M12); views show real names (M21)
                 PropertiesRequested?.Invoke(selected[0].Key, true);
                 break;
             default:
diff --git a/src/NeoFences.App/LibraryLister.cs b/src/NeoFences.App/FolderLister.cs
similarity index 87%
rename from src/NeoFences.App/LibraryLister.cs
rename to src/NeoFences.App/FolderLister.cs
index ceb4560..d762f6a 100644
--- a/src/NeoFences.App/LibraryLister.cs
+++ b/src/NeoFences.App/FolderLister.cs
@@ -6,11 +6,12 @@ using NeoFences.Shell;
 namespace NeoFences.App;
 
 /// <summary>
-/// The Game Library fence's folder listing (M12; Portals' code until M18): NeoFences' own library folder and its watcher.
+/// A folder's live listing and its watcher: the Game Library fence's own folder (M12; Portals' code until M18) and every
+/// folder view's folder (M21).
 /// Listing and watcher setup run off the UI thread (M4 review I1); bursts of changes become one re-list at most every
 /// 250 ms (I2); an unavailable or unwatched folder is retried every few seconds (I4).
 /// </summary>
-public sealed class LibraryLister : IDisposable
+public sealed class FolderLister : IDisposable
 {
     private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(250);
     private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(7);
@@ -19,6 +20,8 @@ public sealed class LibraryLister : IDisposable
     private readonly DispatcherTimer _retryTimer;
     private readonly Action<IReadOnlyList<ItemInfo>?> _show;
     private readonly Action<Exception> _logFailure;
+    private readonly string _label; // "library folder" / "folder view": log lines say which
+    private readonly Action<string, string>? _renamed;
     private FolderWatcher? _watcher;
     private DeviceRemovalNotice? _removal;   // asks before the library drive is removed (USB stick, M8d)
     private readonly nint _noticeOwner;
@@ -40,8 +43,12 @@ public sealed class LibraryLister : IDisposable
 
     /// <param name="show">Called on the UI thread with the shown folder's items, or null when it cannot be read.</param>
     /// <param name="noticeOwner">The window that receives "may this drive be removed?" (the app's message window).</param>
-    public LibraryLister(string folder, nint noticeOwner, Action<IReadOnlyList<ItemInfo>?> show, Action<Exception> logFailure)
+    /// <param name="renamed">Called on the UI thread with the old and new path when the folder itself is renamed in place (M21).</param>
+    public FolderLister(string folder, nint noticeOwner, string label, Action<IReadOnlyList<ItemInfo>?> show, Action<Exception> logFailure,
+        Action<string, string>? renamed = null)
     {
+        _label = label;
+        _renamed = renamed;
         _noticeOwner = noticeOwner;
         _backoffTimer = new DispatcherTimer();
         _backoffTimer.Tick += (_, _) =>
@@ -104,6 +111,13 @@ public sealed class LibraryLister : IDisposable
                 _lastArm = DateTime.UtcNow;
                 watcher.Changed += () => dispatcher.BeginInvoke(ScheduleRefresh);
                 watcher.Failed += () => dispatcher.BeginInvoke(OnWatcherFailed);
+                if (_renamed is { } renamed)
+                {
+                    watcher.Renamed += (oldPath, newPath) =>
+                    {
+                        if (NeoFences.Core.Items.FolderViews.SameFolder(oldPath, folder)) dispatcher.BeginInvoke(() => { if (!_disposed) renamed(oldPath, newPath); });
+                    };
+                }
                 if (watcher.HasFailed) OnWatcherFailed(); // it failed while arming, before this subscription (M8d review I2)
                 // Unreadable or unwatched (drive not there yet): try again every few seconds until it is.
                 if (items is null || !watcher.IsWatching) _retryTimer.Start();
@@ -176,7 +190,7 @@ public sealed class LibraryLister : IDisposable
         _failureDelay = NeoFences.Core.Lifecycle.WatcherBackoff.Next(_failureDelay, lastRearm: _lastArm, failureAt: DateTime.UtcNow);
         _backoffTimer.Interval = _failureDelay;
         _backoffTimer.Start();
-        Serilog.Log.Information("library folder watcher stopped ({Folder}); re-listing after {Delay}", Folder, _failureDelay);
+        Serilog.Log.Information("{Label} watcher stopped ({Folder}); re-listing after {Delay}", _label, Folder, _failureDelay);
     }
 
     private void ScheduleRefresh()
diff --git a/src/NeoFences.App/FolderViewWindow.xaml b/src/NeoFences.App/FolderViewWindow.xaml
new file mode 100644
index 0000000..6be5eb6
--- /dev/null
+++ b/src/NeoFences.App/FolderViewWindow.xaml
@@ -0,0 +1,68 @@
+<Window x:Class="NeoFences.App.FolderViewWindow"
+        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
+        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
+        Title="Folder view settings" Width="500" SizeToContent="Height" ResizeMode="NoResize"
+        WindowStartupLocation="CenterScreen" ShowInTaskbar="True" ThemeMode="System">
+    <!-- M21 spec 2026-10-05-folder-views-design §2: what a folder view shows. NeoFences never writes to the folder. -->
+    <Window.Resources>
+        <Style x:Key="FieldLabel" TargetType="TextBlock">
+            <Setter Property="VerticalAlignment" Value="Center" />
+            <Setter Property="Margin" Value="0,0,12,10" />
+        </Style>
+        <Style x:Key="Hint" TargetType="TextBlock">
+            <Setter Property="Foreground" Value="{DynamicResource TextFillColorSecondaryBrush}" />
+            <Setter Property="FontSize" Value="12" />
+            <Setter Property="TextWrapping" Value="Wrap" />
+        </Style>
+    </Window.Resources>
+    <StackPanel Margin="24,16,24,20">
+        <Grid>
+            <Grid.ColumnDefinitions>
+                <ColumnDefinition Width="Auto" />
+                <ColumnDefinition />
+                <ColumnDefinition Width="Auto" />
+            </Grid.ColumnDefinitions>
+            <Grid.RowDefinitions>
+                <RowDefinition Height="Auto" />
+                <RowDefinition Height="Auto" />
+                <RowDefinition Height="Auto" />
+                <RowDefinition Height="Auto" />
+                <RowDefinition Height="Auto" />
+                <RowDefinition Height="Auto" />
+            </Grid.RowDefinitions>
+
+            <TextBlock Text="Folder" Style="{StaticResource FieldLabel}" />
+            <TextBox x:Name="FolderBox" Grid.Column="1" Margin="0,0,8,10" AutomationProperties.Name="Folder" />
+            <Button x:Name="BrowseButton" Grid.Column="2" Content="Browse…" Margin="0,0,0,10" AutomationProperties.Name="Browse for the folder" />
+
+            <TextBlock Grid.Row="1" Text="Show" Style="{StaticResource FieldLabel}" />
+            <ComboBox x:Name="ShowBox" Grid.Row="1" Grid.Column="1" Grid.ColumnSpan="2" Margin="0,0,0,10" AutomationProperties.Name="Show">
+                <ComboBoxItem Content="Files and folders" />
+                <ComboBoxItem Content="Files only" />
+                <ComboBoxItem Content="Folders only" />
+            </ComboBox>
+
+            <TextBlock Grid.Row="2" Text="Types" Style="{StaticResource FieldLabel}" Margin="0,0,12,2" />
+            <TextBox x:Name="PatternsBox" Grid.Row="2" Grid.Column="1" Grid.ColumnSpan="2" Margin="0,0,0,2"
+                     AutomationProperties.Name="Types: file name patterns, for example *.png;*.jpg" />
+            <TextBlock x:Name="PatternsHint" Grid.Row="3" Grid.Column="1" Grid.ColumnSpan="2" Style="{StaticResource Hint}" Margin="0,0,0,10"
+                       Text="e.g. *.png;*.jpg or pdf — empty shows every type. Subfolders always show." />
+
+            <TextBlock Grid.Row="4" Text="Sort by" Style="{StaticResource FieldLabel}" />
+            <ComboBox x:Name="SortBox" Grid.Row="4" Grid.Column="1" Grid.ColumnSpan="2" Margin="0,0,0,10" AutomationProperties.Name="Sort by">
+                <ComboBoxItem Content="Name" />
+                <ComboBoxItem Content="Type" />
+                <ComboBoxItem Content="Date (newest first)" />
+            </ComboBox>
+
+            <CheckBox x:Name="NewestCheck" Grid.Row="5" Grid.ColumnSpan="2" Content="Only the newest" VerticalAlignment="Center" Margin="0,0,0,10" />
+            <TextBox x:Name="NewestBox" Grid.Row="5" Grid.Column="2" Width="64" Margin="0,0,0,10" AutomationProperties.Name="How many of the newest" />
+        </Grid>
+        <TextBlock Style="{StaticResource Hint}" Margin="0,4,0,18"
+                   Text="The fence shows this folder as it is, live. Removing the fence or an entry from it never touches your files." />
+        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
+            <Button x:Name="OkButton" Content="OK" IsDefault="True" MinWidth="90" Margin="0,0,8,0" />
+            <Button Content="Cancel" IsCancel="True" MinWidth="90" />
+        </StackPanel>
+    </StackPanel>
+</Window>
diff --git a/src/NeoFences.App/FolderViewWindow.xaml.cs b/src/NeoFences.App/FolderViewWindow.xaml.cs
new file mode 100644
index 0000000..756344f
--- /dev/null
+++ b/src/NeoFences.App/FolderViewWindow.xaml.cs
@@ -0,0 +1,79 @@
+using System.Windows;
+using System.Windows.Interop;
+using NeoFences.Core.Items;
+using NeoFences.Core.Model;
+using NeoFences.Shell;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// "Folder view settings" (M21 spec §2): the folder, what it shows, the types, the sort and "only the newest N". OK gives
+/// <see cref="Result"/>; an invalid pattern or count marks its box and disables OK.
+/// </summary>
+public partial class FolderViewWindow : Window
+{
+    private static readonly FenceSort[] Sorts = [FenceSort.Name, FenceSort.Type, FenceSort.Date];
+    private readonly string _patternsHint;
+
+    public FolderView? Result { get; private set; }
+
+    public FolderViewWindow(FolderView view)
+    {
+        InitializeComponent();
+        _patternsHint = PatternsHint.Text;
+        FolderBox.Text = view.Path;
+        ShowBox.SelectedIndex = (int)view.Show;
+        PatternsBox.Text = view.Patterns;
+        SortBox.SelectedIndex = Math.Max(0, Array.IndexOf(Sorts, view.Sort));
+        NewestCheck.IsChecked = view.Newest is not null;
+        NewestBox.Text = (view.Newest ?? FolderViews.BusyFolderNewest).ToString();
+        FolderBox.TextChanged += (_, _) => Validate();
+        PatternsBox.TextChanged += (_, _) => Validate();
+        NewestBox.TextChanged += (_, _) => Validate();
+        NewestCheck.Click += (_, _) => Validate();
+        BrowseButton.Click += (_, _) =>
+        {
+            var picked = PathPicker.TryPickFolder(new WindowInteropHelper(this).Handle, "Choose the folder to show",
+                failure => Serilog.Log.Warning(failure, "folder view: the folder dialog failed"), startFolder: FolderBox.Text.Trim());
+            if (picked is not null) FolderBox.Text = picked;
+        };
+        OkButton.Click += (_, _) =>
+        {
+            if (Read() is not { } read) return;
+            Result = read;
+            DialogResult = true;
+        };
+        Loaded += (_, _) => FolderBox.Focus();
+        Validate();
+    }
+
+    private void Validate()
+    {
+        var patternsOk = FolderViews.ParsePatterns(PatternsBox.Text) is not null;
+        PatternsHint.Text = patternsOk ? _patternsHint : "Use file name patterns like *.png;*.jpg — no paths, and none of \\ / : \" < > |";
+        PatternsHint.Foreground = patternsOk ? (System.Windows.Media.Brush)FindResource("TextFillColorSecondaryBrush") : System.Windows.Media.Brushes.IndianRed;
+        NewestBox.IsEnabled = NewestCheck.IsChecked == true;
+        OkButton.IsEnabled = Read() is not null;
+    }
+
+    /// <summary>The view as entered, or null while something is not valid.</summary>
+    private FolderView? Read()
+    {
+        var folder = FolderBox.Text.Trim();
+        if (folder.Length == 0 || FolderViews.ParsePatterns(PatternsBox.Text) is null) return null;
+        int? newest = null;
+        if (NewestCheck.IsChecked == true)
+        {
+            if (!int.TryParse(NewestBox.Text.Trim(), out var count) || count < 1 || count > FolderViews.MaxNewest) return null;
+            newest = count;
+        }
+        return new FolderView
+        {
+            Path = folder,
+            Show = (ViewShow)Math.Max(0, ShowBox.SelectedIndex),
+            Patterns = PatternsBox.Text.Trim(),
+            Sort = Sorts[Math.Max(0, SortBox.SelectedIndex)],
+            Newest = newest,
+        };
+    }
+}
````

- [ ] **Step 2: Build and test.** `dotnet build` → `0 Warning(s)`; `dotnet test` → 497 passed.
  (UI behaviour is verified by TEST-CHECKLIST AG in Task 6.)

- [ ] **Step 3: Commit.** `git add -A && git commit -m "feat: added folder view fences that show one folder live and read-only"`

### Task 4: Docs

**Files:**
- Modify: `docs/DECISIONS.md` (ADR-044), `docs/ARCHITECTURE.md` (layers, components, data shape, 0.11.0 status),
  `docs/FEATURES.md` (Portals row → folder views; auto-collect rules later), `docs/TEST-CHECKLIST.md` (section AG)
- Create: `docs/research/m21-folder-views.md`

- [ ] **Step 1: Apply.** Write this patch to `m21-4-docs.patch` and apply it:

````diff
diff --git a/docs/ARCHITECTURE.md b/docs/ARCHITECTURE.md
index bd0d7b6..8889715 100644
--- a/docs/ARCHITECTURE.md
+++ b/docs/ARCHITECTURE.md
@@ -49,20 +49,21 @@ happen) is behind Shift+right-click, under a line saying it acts on the real fil
 
 ```
 ┌─────────────────────────── NeoFences.App (WPF) ───────────────────────────┐
-│ FenceHost (+ .Items .Watching .Library .Appearance .Updates .DesktopFill) │
+│ FenceHost (+ .Items .Watching .Library .Appearance .Updates .DesktopFill  │
+│            .FolderViews)                                                  │
 │ FenceWindow · FenceItemView · IconLoader · ItemPropertiesWindow           │
 │ AppPickerWindow · RelocateWindow · DesktopFillWindow · MissingItemWindow  │
-│ SettingsWindow · LibraryLister · DrawFenceOverlay                         │
+│ FolderViewWindow · SettingsWindow · FolderLister · DrawFenceOverlay       │
 └──────────────┬────────────────────────────────────────────────────────────┘
 ┌──────────────▼─────────── NeoFences.Shell (Win32/COM, CsWin32) ───────────┐
 │ DesktopHost · DesktopIcons · ShellItems · ShellItemMenu · ShellDragDrop   │
 │ TargetProbe · PathPicker · IconPicker · FolderWatcher · DesktopMouseHook  │
-│ AppList                                                                   │
+│ AppList · KnownFolders                                                    │
 │ GameDetection · GameScanners · Monitors · TrayIcon · Watchdog             │
 └──────────────┬────────────────────────────────────────────────────────────┘
 ┌──────────────▼─────────── NeoFences.Core (pure C#) ───────────────────────┐
 │ Items (VirtualItem, ItemEdits, TargetChecks, WatchPlan, RefreshThrottle,  │
-│        Relocation, DesktopSorting, StaleEntries)                          │
+│        Relocation, DesktopSorting, StaleEntries, FolderViews)             │
 │ Model · Config (ConfigStore, ItemStore, JsonStore) · Layouts · Library    │
 └───────────────────────────────────────────────────────────────────────────┘
 ```
@@ -71,7 +72,8 @@ happen) is behind Shift+right-click, under a line saying it acts on the real fil
 |---|---|---|
 | Core/Items | `VirtualItem` + `ItemIcon`; `ItemKinds` (path / website / special, typed targets cleaned); `ItemsDocument` (fence id → items); `ItemEdits` (add with same-fence de-dup, move, Ctrl-duplicate, remove, replace, retarget on rename, prune, reorder, repair); `TargetChecks` (OK / Missing / Unavailable from the disk's answers); `WatchPlan` (≤ 64 parent folders, busiest first); `RefreshThrottle` (≤ 1 refresh per fence per 2 s); apps `shell:AppsFolder\<id>` (`IsApp`, checked, never watched; M19) | — |
 | Core/Items (M19) | `Relocation` (Find: the differing front of the old and new path; Candidates: the other missing / unavailable path items under it); `ItemEdits.Relocate`, `CheckedTargets`; `DesktopSorting.GroupOf` (Games / Apps / Folders and files / Web links, reusing `GameLaunchers`); `StaleEntries.Gone` | — |
-| Core/Model | Fence (`IsLibrary`, tabs, colours, roll-up, lock, labels, icon size), Layout, Settings (`HideDesktopIcons`), snapshots with items | — |
+| Core/Items (M21) | `FolderViews.Select` (show kind → patterns → newest N → sort → cap 500), `ParsePatterns`, `DefaultsFor` (Downloads / Screenshots newest 30), `Normalize`, `Status` (not available / empty / nothing matching / "+ N more"); `FenceEdits.CreateView`, `SetView` (title follows the folder while it is its name) | System.IO.Enumeration |
+| Core/Model | Fence (`IsLibrary`, `View` = `FolderView` (M21), `Kind` = Items / Library / View, tabs, colours, roll-up, lock, labels, icon size), Layout, Settings (`HideDesktopIcons`), snapshots with items | — |
 | Core/Config | `JsonStore<T>`: atomic write (`SafeFile`), `.bak`, daily backups (10), corrupt recovery, newer-schema read-only; `ConfigStore` (config.json, schema 5; older schemas start fresh and are kept as `backups\pre-schema-5-config.json`), `ItemStore` (items.json, schema 1), `SnapshotStore` | System.Text.Json, File.Replace |
 | Core/LayoutEngine | display fingerprint, map/scale layouts between monitor setups, clamp, snap, smart placement of new fences (`FreeSpot`, ADR-020) | — |
 | Core/FenceEdits, FenceTabs, Snapping | rename / icon size / lock / colours / new / delete fence; tabs (ADR-029); snap to 8 px gap or aligned edges during drags (ADR-015) | — |
@@ -82,16 +84,18 @@ happen) is behind Shift+right-click, under a line saying it acts on the real fil
 | App/FenceHost.Watching | watched folders (`FolderWatcher` + removal notices), rename-follow, target checks (start, 5 min, fence shown, Refresh, drive arrival/removal), per-fence refresh throttle, game-mode deferral | DispatcherTimer |
 | App/FenceWindow, FenceItemView, IconLoader | fence UI: item grid (ListBox + WrapPanel), keys (Enter, Del = remove, F2 / Alt+Enter = Properties), drop caret, Missing badge / Unavailable dimming, empty-fence hint, `WM_DEVICECHANGE` → drives changed; names/icons on two STA threads, an item's own name and icon win, websites show the default browser's icon | WPF, frozen BitmapSource |
 | App/ItemPropertiesWindow, MissingItemWindow | Properties and Add item… (name, target + Browse, live Found / Missing / Drive not connected, arguments, run as admin, icon from a file / a picture / reset, note); the missing-target question | WPF (Fluent) |
-| App/LibraryLister, FenceHost.Library | the Game Library fence lists NeoFences' own `library\` folder (former Portal machinery) as tiles; scans launchers, game folders and Desktop game shortcuts (ADR-032) | FileSystemWatcher |
+| App/FenceHost.FolderViews, FolderViewWindow (M21) | folder views: one `FolderLister` per view (hidden tabs keep listing), the last listing per view, rendering through `FolderViews.Select`, New folder view…, settings, Show as folder view, Add to fence ▸, the safe menu and Windows' menu for entries, rename-follow, removal notices, game-mode pause (ADR-044) | WPF (Fluent) |
+| App/FolderLister, FenceHost.Library | a folder's live listing (250 ms coalescing, 7 s retry, watcher backoff, removal notices, game-mode pause, the folder's own rename) for folder views (M21) and the Game Library's own `library\` folder (former Portal machinery), shown as tiles; scans launchers, game folders and Desktop game shortcuts (ADR-032) | FileSystemWatcher |
 | App/SettingsWindow | General (Start with Windows, Hide desktop icons while NeoFences runs, Peek hotkey) · Fences · Appearance · Snapshots · Game Library · Game mode · Updates · About | WPF ThemeMode |
 | App/InstallHooks, Shell/StartupRegistration, Core/StartupPolicy, build/pack.ps1 | Velopack installer and auto-update (ADR-023, ADR-039) | Velopack 1.2.161 |
 | Shell/ShellDragDrop | drag out: a shell item array of the targets (absolute ID lists) turned into a data object, or URLs, copy/link only; one IDropTarget per fence: fence items (keys) → move / Ctrl-duplicate, outside items read as shell items (real paths, special items as `::{GUID}`, zip contents skipped; CF_HDROP as fallback) or one website (UniformResourceLocatorW / text) → new items; never a move effect | SHDoDragDrop, SHCreateShellItemArrayFromIDLists + BHID_DataObject, SHCreateShellItemArrayFromDataObject, SHParseDisplayName, RegisterDragDrop, IDropTargetHelper |
 | Shell/ShellItems | display name, icon/thumbnail pixels, an icon from a file (index), the default browser's path, open (arguments, `runas`, the target's folder as working folder), show in folder; a generic icon by file type for targets the shell cannot reach (M19) | SHCreateItemFromParsingName, IShellItemImageFactory, SHDefExtractIcon, SHGetFileInfo, AssocQueryString, ShellExecute |
+| Shell/KnownFolders (M21) | Downloads and Screenshots paths (moved ones too) for a new folder view's defaults | SHGetKnownFolderPath |
 | Shell/AppList (M19) | Start's All apps (`FOLDERID_AppsFolder`, ~0.3 s for ~200 apps), whether an app still exists, an app dragged from Start | SHGetKnownFolderItem, BHID_EnumItems, IEnumShellItems |
 | Shell/ShellItemMenu, DesktopNamespace | Windows' classic item menu for one target (Shift+right-click, with a disabled header line) and for the library's shortcuts (custom commands, Delete handed back) | IContextMenu3, TrackPopupMenuEx, InsertMenu |
 | Shell/TargetProbe | a target's state off the UI thread; shares time out after 2 s (Unavailable); apps are Missing once Windows no longer knows them (M19). Also asked before UI-thread shell calls on a target (Windows' menu, picker start folders, drag-out, icon loading of shares; M19 R2/R7) | File/Directory.Exists |
 | Shell/PathPicker, IconPicker | Windows' Open dialog (file with filters / folder, start folder); Windows' Change Icon dialog | IFileOpenDialog, PickIconDlg |
-| Shell/FolderItems (+FolderWatcher) | folder listing (library), sort facts, watcher with `Renamed(old, new)` and the folder's own rename/removal via its parent | FileSystemWatcher |
+| Shell/FolderItems (+FolderWatcher) | folder listing (library, folder views), sort facts, watcher with `Renamed(old, new)` and the folder's own rename/removal via its parent | FileSystemWatcher |
 | Shell/DesktopIcons, Watchdog | hide/show native icons (option); `--watchdog <pid>`: restore icons whenever `icons-hidden` exists, restart after a crash (3 per 10 min) or a cancelled session end (ADR-005, ADR-013) | IShellWindows, IFolderView2 |
 | Shell/DeviceRemovalNotice | lets go of a watched drive Windows wants to remove ("Safely remove", DBT_DEVICEQUERYREMOVE) or that was pulled without asking (DBT_DEVICEREMOVECOMPLETE), so its watchers are re-armed when it is back | RegisterDeviceNotification |
 | Shell/ShellFileOps | recycles NeoFences' own snapshot files (never items or targets) | IFileOperation |
@@ -116,7 +120,7 @@ happen) is behind Shift+right-click, under a line saying it acts on the real fil
 
 `%LOCALAPPDATA%\NeoFences\`
 - `config.json` (+ `.bak`, `.tmp` transient) — schema 5
-  - shape: `{ schemaVersion, settings: { hideDesktopIcons, peekHotkey, … }, fences: [ { id, title, isLibrary, iconSize, rolledUp, locked, labels, tabs, activeTab, tabColor, customColor } ], layouts: { <fingerprint>: { monitors, fences: { <fenceId>: { monitor, x, y, w, h } } } }, library, lastLayoutFingerprint }`
+  - shape: `{ schemaVersion, settings: { hideDesktopIcons, peekHotkey, … }, fences: [ { id, title, isLibrary, view: { path, show, sort, newest, patterns } (M21), iconSize, rolledUp, locked, labels, tabs, activeTab, tabColor, customColor } ], layouts: { <fingerprint>: { monitors, fences: { <fenceId>: { monitor, x, y, w, h } } } }, library, lastLayoutFingerprint }`
 - `items.json` (+ `.bak`) — schema 1 (ADR-041)
   - shape: `{ schema, fences: { <fenceId>: [ { id, target, name, icon: { file, index } | { image }, arguments, runAsAdmin, note } ] } }`
   - a list is removed only with its fence (Delete fence); lists of fences the config does not have stay (a fallback config never costs items; ADR-041 amended)
@@ -149,6 +153,13 @@ reuses the Game Library's last scan and never counts a drive root or system fold
 places (Control Panel, This PC) and `file:///` links sort as folders and files (`ShellLinks.ShellTargetOf`); stale
 per-target records dropped after a restore and when a check outlives its item; failed background checks logged.
 
+**0.11.0 (M21, folder views)**: a fence can show one folder live, read-only (ADR-044): "New folder view…" (tray, fence
+menu) or "Show as folder view" on a folder item; per view: files and folders / files only / folders only, type patterns,
+sort (kept, live), only the newest N; Downloads and Screenshots start newest first with 30; at most 500 entries, then
+"+ N more — Open folder"; "Folder not available" while the folder or its drive is gone, back by itself; Safely Remove
+works while shown; the view follows its folder's rename. Entries open, show in Explorer, copy their path, go to a fence
+as items (Add to fence ▸, or a drag) and drag out as copies; drops onto a view are refused (`research/m21-folder-views.md`).
+
 Pre-pivot history (v1.x dev builds, the M0–M17 notes in `research/` and SESSION-LOG) describes the Takeover model; read
 it as history. Kept from it: fences, tabs (M9), snapshots (M10), the Game Library (M12), roll-up, lock, Peek, game mode,
 Appearance (M14/M16), the installer and auto-update (M7/M17).
diff --git a/docs/DECISIONS.md b/docs/DECISIONS.md
index 68775da..26272fb 100644
--- a/docs/DECISIONS.md
+++ b/docs/DECISIONS.md
@@ -1190,3 +1190,27 @@ and releases are public (ADR-039); the user decided on 2026-10-05 to keep the na
 project for now; the name is revisited only if it is ever distributed more widely (1.0.0 for friends or beyond).
 
 **Consequences.** No rename work; the trademark question stays a listed risk on the hub.
+
+## ADR-044 — Folder views: fences that show one folder live, read-only
+**Date:** 2026-10-05 · **Status:** Accepted · **Refines:** ADR-040 (Portals parked "for later as dynamic collections")
+
+**Context.** ADR-040 parked Portals and Rules for "dynamic collections". The user chose folder views first (M21, 0.11.0):
+game and app folders, work folders, Downloads and Screenshots, and USB sticks, each shown live in a fence.
+
+**Decision.**
+- A fence is one of three kinds: items (items.json), the Game Library, or a **folder view** (`Fence.View`, a
+  `FolderView { path, show, sort, newest, patterns }` in config.json; schema stays 5, an older NeoFences ignores it).
+- **Read-only**: NeoFences never writes to the folder. Drops onto a view are refused; NeoFences' own menu has Open, Open
+  file location, Copy path and Add to fence ▸; Del, F2 and Alt+Enter do nothing. Windows' menu (Shift+right-click) stays,
+  as for items: what the user picks there is Windows' action.
+- Subfolders open in Explorer (no browsing inside the fence, user choice). Entries dragged out are copies; dragged onto an
+  items fence they become items.
+- Per view: show (all / files / folders), type patterns (subfolders always pass when showing all), sort (Name / Type /
+  Date, kept and live), only the newest N (1–500). New views of Downloads and Screenshots start newest first with 30.
+- At most 500 entries are shown, then "+ N more — Open folder".
+- Listing reuses the Game Library's machinery (`FolderLister`, former Portal code): off the UI thread, 250 ms coalescing,
+  7 s retry while unavailable, watcher backoff, removal notices (Safely Remove), game-mode pause; the view follows its
+  folder's rename.
+
+**Consequences.** Each view holds two `FileSystemWatcher`s and, on a removable drive, a removal notice; no limit on the
+number of views (personal use). Auto-collect rules stay for later.
\ No newline at end of file
diff --git a/docs/FEATURES.md b/docs/FEATURES.md
index 6d82457..335cb2b 100644
--- a/docs/FEATURES.md
+++ b/docs/FEATURES.md
@@ -31,7 +31,7 @@ Sources for Fences 6: stardock.com news posts "Now Announcing: Fences 6", "Fence
 | Missing / unavailable targets | — | 0.9 | M18 | done | watched (≤ 64 folders), renames followed, Locate… / Remove, per-fence Refresh; a generic icon for their type (0.10) |
 | Bulk fix of missing items | — | 0.10 | M19 | done | after one Locate…, "Fix N more items?" for the others from the same old place; undo from the tray (ADR-042) |
 | Rubber-band selection | 1+ | v1 | M3 | done | M3b; Ctrl adds |
-| Folder Portals | 3+ | — | M4 | parked | ADR-040: later as dynamic collections (M20) |
+| Folder Portals → folder views | 3+ | 0.11 | M21 | done | ADR-044: read-only (no drop into the folder, no rename/delete from NeoFences' menu); subfolders open in Explorer |
 | Sort (name/type/date) | 2+ | v1 | M4 | done | one time; by the names shown (0.9) |
 | Draw fence by right-drag on desktop | 1+ | v1 | M5 | done | S2 keeps the plain right-click menu (ADR-020) |
 | Quick-hide (double-click desktop) | 1+ | v1 | M5 | done | fences + desktop icons (user choice); a double-click on a native icon opens it |
@@ -65,7 +65,8 @@ Sources for Fences 6: stardock.com news posts "Now Announcing: Fences 6", "Fence
 | Blur tint preference (lighter/darker) | v1.7 (M14) | done | background strength slider, one value per Windows tone (ADR-036) |
 | Search palette across all fences | — | parked | built on branch `m15-search-palette` (local history bundle only), not merged |
 | Game Library fence (Steam/Epic/GOG/Ubisoft Connect/EA, cover art) | v1.5 | done | launchers, Xbox, game folders, Desktop game shortcuts; a game dragged into a fence becomes an item (0.9) (ADR-032) |
-| Dynamic collections (read-only folder views, auto-collect rules) | M20 | — | replaces Portals and Rules (ADR-040) |
+| Folder views: types, files/folders only, newest N, live sort, "+ N more" | 0.11 (M21) | done | ADR-044; Downloads/Screenshots start newest first |
+| Auto-collect rules (the other half of dynamic collections) | later | — | replaces Rules (ADR-040) |
 | Theme packs: "Nanosuit" (Crysis HUD), "Animus" (Assassin's Creed) | v2 | — | |
 | Custom Win11-style compact context menu | v2 | — | |
 | Wallpaper-adaptive accent color | v1.7 (M14) | done | Wallpaper Engine preview → Windows wallpaper → Windows accent; no screen capture (ADR-036) |
diff --git a/docs/TEST-CHECKLIST.md b/docs/TEST-CHECKLIST.md
index 5ad9fdd..e8f2c6f 100644
--- a/docs/TEST-CHECKLIST.md
+++ b/docs/TEST-CHECKLIST.md
@@ -545,3 +545,27 @@ Added after the M19 final review:
 | AF3 | A test shortcut to This PC and one to Control Panel on the desktop; Add from desktop… | both listed under Folders and files; removed afterwards |
 | AF4 | Add from desktop… twice with a Game Library fence present | the second opening lists at once (the library's last scan reused); log has no scan errors |
 | AF5 | Bulk fix: Fix after every proposed item was changed by hand meanwhile | log "nothing fixed"; `before-restore.json` unchanged |
+
+## AG — 0.11.0 folder views (M21)
+
+| ID | Steps | Expected |
+|---|---|---|
+| AG1 | Tray → New folder view… → Cancel in the folder dialog; again → pick a folder → Cancel in the settings | nothing created either time |
+| AG2 | Tray → New folder view… → Downloads | settings prefilled: Date (newest first), only the newest 30; OK → a fence titled "Downloads" with the newest 30 entries, newest first |
+| AG3 | Fence menu → New folder view… → `D:\GameLibrary`, Show: Folders only | only the game folders, A–Z; double-click one → Explorer opens it |
+| AG4 | Folder view settings… → Types `*.png;*.jpg`; then `a|b` | only those files (and subfolders when showing all); `a|b` marks the hint red and disables OK |
+| AG5 | In Explorer: add, rename and delete a file in a viewed folder | the view follows within a second each time; selection and scroll stay |
+| AG6 | Rename the viewed folder itself in Explorer | the view follows; its title follows while it was the folder's name |
+| AG7 | Delete the viewed folder (to the Recycle Bin), then restore it | "Folder not available: <path>", then the entries again within ~7 s |
+| AG8 | A view of `G:\NeoFences-test` (pendrive): pull the stick; plug it back | "Folder not available", then back by itself |
+| AG9 | With that view shown: Safely Remove the stick | Windows allows it; the view says "not available"; back after replugging |
+| AG10 | A game in front (game mode), change the viewed folder, leave the game | no change during the game; one re-list afterwards |
+| AG11 | Drag an entry from a view to Explorer; drag one onto an items fence; drop a file onto the view | a copy in Explorer (the original stays); a new item in the fence; the drop is refused |
+| AG12 | Right-click an entry → Add to fence ▸ <fence>; Copy path; Open file location | the item is added and selected there; the path is on the clipboard; Explorer shows the entry |
+| AG13 | Shift+right-click an entry | Windows' menu under "Windows menu — acts on the real files" |
+| AG14 | Del, F2, Alt+Enter on a selected entry | nothing happens; the file is untouched |
+| AG15 | A folder item in an items fence → right-click → Show as folder view | a view of that folder beside the fence |
+| AG16 | A view of a folder with 600 files (`G:\NeoFences-test\many`) | 500 entries, then "+ 100 more — Open folder"; clicking it opens Explorer |
+| AG17 | Sort by → Type on a view; restart NeoFences | the sort is checked in the menu and kept after the restart |
+| AG18 | Merge a view into another fence's box as a tab; switch tabs | the view lists as a tab; switching back shows its entries at once |
+| AG19 | Take snapshot; delete the view fence; restore the snapshot | the folder is untouched by the delete; the restore brings the view back, listing |
\ No newline at end of file
diff --git a/docs/research/m21-folder-views.md b/docs/research/m21-folder-views.md
new file mode 100644
index 0000000..d694c49
--- /dev/null
+++ b/docs/research/m21-folder-views.md
@@ -0,0 +1,30 @@
+# M21 — Folder views (0.11.0): build notes and results
+
+Spec: `docs/superpowers/specs/2026-10-05-folder-views-design.md` · Decision: ADR-044 · Plan:
+`docs/superpowers/plans/2026-10-05-m21-folder-views.md`
+
+## Prototype (2026-10-05, worktree `neo_fences-m21proto`, branch `m21-proto`)
+
+Built end to end before the plan: Core test-first (29 new tests, 497 in all), Shell, App; the solution builds with 0
+warnings. Each plan task's patch was then replayed on a fresh worktree of `main`.
+
+### Where the build departs from the spec (the final review weighs them)
+
+- **Sort reuses `FenceSort`** (Name / Type / Date; Manual is read as Name) instead of a new `ViewSort`: the fence menu's
+  "Sort by" items and `ItemSorting.Order` serve views unchanged.
+- **`FolderViews.Selection` carries `Listed`** (entries before any filter) so `Status` tells "This folder is empty" from
+  "Nothing here matches this view"; `Status(selection, view)` returns `(Center, More)`.
+- **`DefaultsFor(path, busyFolders)`** takes the busy folders as a list; `KnownFolders.BusyFolders` gives Downloads and
+  Screenshots (asked once per run).
+- **"Show as folder view"** places the new view 40 DIP right and down of the fence it came from (like a detached tab).
+- **Deleting a view fence asks nothing** (it has no items); its menu entry reads "Delete fence (the folder is not
+  touched)".
+- **Refresh** on a view re-lists its folder.
+- **Add to fence ▸** lists the items fences only (not the Library, not other views) and is greyed out when there are none;
+  several entries copy as "Copy paths", one per line.
+- **"Folder not available"** is logged once per outage, not on every 7 s retry.
+- **Dragging out of a view whose folder stopped answering** (a share gone since the listing) does not start; it is logged.
+
+## Live check
+
+(TEST-CHECKLIST AG — filled in after the run.)
````

- [ ] **Step 2: Check.** The secret scan of `git diff main` (the personal email, the private hub link) finds nothing.

- [ ] **Step 3: Commit.** `git add -A && git commit -m "docs: described folder views in ADR-044, architecture, features, checklist AG and the M21 research note"`

### Task 5: Final review and fix pass

- [ ] Whole-branch review on the most capable model (superpowers:requesting-code-review, `code-reviewer.md`) of
  `main..m21-folder-views`, with the spec, this plan, its Review Focus and departures. Critical / Important findings:
  one fix pass, each fix test-first where Core can show it (a failing test, then green, then the whole suite); App-only
  fixes get an AG row. Minors are listed in `docs/research/m21-folder-views.md` as deferred.
- [ ] Commit fixes (`fix: …`) and the review notes (`docs: recorded the M21 final review, its fixes and the deferred minors`).

### Task 6: Live check (ask the user first unless the PC is unattended)

- [ ] Back up `%LOCALAPPDATA%\NeoFences` and the Run value; stop the installed copy; run the test build.
- [ ] TEST-CHECKLIST AG1–AG19 by script where possible (boxed TEST RUNNING / TEST COMPLETE banners; never type into
  Windows Terminal; leave the Firefox Picture-in-Picture window alone); AG8/AG9 (pendrive pull, Safely Remove) and the
  drags by hand with the user. Screenshots sent to the user as they are taken.
- [ ] Restore the data and the Run value; restart the installed copy; refocus Terminal.
- [ ] Results into `docs/research/m21-folder-views.md`; commit `docs: added the M21 live check results`.
