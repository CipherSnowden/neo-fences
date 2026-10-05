# M27 — Auto-collect rules (0.16.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A fence can collect new files of the desktop or any folder by itself — by kind or pattern, as items, never moving a file — with removed items staying removed and a catch-up at start.

**Architecture:** Core adds `CollectRules` (`CollectRule` on `Fence.Collect`, kinds by file name, matching, routing arrivals to the first matching rule in fence order, arrivals by comparing listings or by watermark, repair, summary). Shell reports creation times and the Downloads folder. The App adds `FenceHost.Collect.cs` (one `FolderLister` per watched folder, routing, watermarks, game-mode and Pause hold) and `AutoCollectWindow` (fence menu → Auto-collect…).

**Tech Stack:** .NET 10, WPF (Fluent dialogs), CsWin32 0.3.335 (no new bindings), xUnit, Serilog, Velopack (unchanged).

**Spec:** `docs/superpowers/specs/2026-10-05-auto-collect-design.md` (approved 2026-10-05). Decision: ADR-049 (added by Task 3). Build notes: `docs/research/m27-auto-collect.md` (added by Task 3).

## How this plan is written

Every code change was built in a scratch prototype first (2026-10-05, worktree `neo_fences-m27proto`, branch
`m27-proto`) and probed live on a copy of the user's data with a rule on Apps watching the probe's own folder: the old
file there was not collected; `new.url` and `ToolSetup.exe` became items within a second, `notes.txt` did not; a removed
item stayed removed (also after a restart); a file created while NeoFences was closed was caught up at start; the dialog
showed the rule and "3 items here match now"; 0.013 % CPU over 30 s. Each task's patch was replayed on a fresh worktree of
`main` at `cdf9d5c`: the Core tests failed to compile before the Core patch (`CS0246: 'CollectKinds' could not be
found`) and passed after it (632), the solution built with 0 warnings after the App patch, and the replayed tree is
identical to the prototype. So each code step is **a patch to apply**:

1. Write the patch block **exactly** as shown to a file in the scratchpad (use the Write tool, never a shell heredoc).
   The file ends with one newline.
2. `git apply --whitespace=nowarn <file>` from the worktree root. If it does not apply, stop (rule on it, never
   hand-merge silently).

## Where the build departs from the spec (decided while prototyping; the final review weighs them)

- No separate 1 s settle: the folder lister's 250 ms coalescing; arrivals found by comparing listings, so files renamed
  or moved in while NeoFences runs count too.
- A rule's `Watermark` is updated when a listing had arrivals or was the first one (not on every change).
- At start the source's earliest watermark serves all its rules; a rule whose settings change starts looking again then.
- "Add these N too?" is asked after OK for each new rule, from the dialog's listings; at most the first 200.
- The fence menu shows the rule count ("Auto-collect… (2 rules)") as the "collects" mark.

## Global Constraints

- Hard rule 1: **NeoFences never moves, renames or deletes a file** — a rule only adds an item pointing at the file.
- Hard rules 2–7: Win32 only in NeoFences.Shell via **CsWin32**; **no new NuGet dependency**; failures degrade one rule
  (logged, retried), never crash.
- `Fence.Collect` in config.json as `collect: [ { id, source, kinds, patterns, watermark } ]`, written only when non-empty;
  config schema stays **5**. Source `"desktop"` = the user's and the Public Desktop.
- Kinds: Apps `.lnk .url .appref-ms .exe`; Installers `.msi .msix .msixbundle .appx .appxbundle` and `.exe` named
  *setup*/*install*; Documents, Pictures, Archives as the spec lists; Anything = every file and folder; hidden/system
  entries never; folders only by Anything or a pattern.
- First matching rule in fence order wins; nothing any fence holds; at most **200** per rule per burst.
- UI copy: fence menu "Auto-collect…"; dialog "Auto-collect into "<fence>"", "New rule", "Remove", "Watch", "Collect",
  "Also", "N items here match now."; "Add these N too?".
- Commits: single line, Conventional Commits, past tense, **no Co-Authored-By trailer**.
- Version stays `0.15.0` until the release step; the release is 0.16.0.

## Review Focus

1. **Never a file operation**: no code path that adds, moves, renames or deletes a file in a watched folder; drags,
   menus and dialogs only touch items.
2. **Duplicates and re-adds**: a file renamed in place (the item follows it), a `.crdownload` becoming its final name,
   two rules on one folder, the same file on the user's and the Public Desktop, a restore of an older snapshot — no
   second item, nothing removed coming back.
3. **Watermarks and catch-up**: a pendrive out and back with old files, a clock change, a rule edited, NeoFences killed —
   no flood of old files collected as new, nothing missed that arrived while running.
4. **Bursts and big folders**: thousands of files at once (cap, UI responsiveness, a single save), a slow network folder
   (listing off the UI thread, retry), game mode holding arrivals.
5. **Dialog**: rules added, edited, removed, cancelled; an invalid pattern; a folder that is gone; "Add these N too?"
   after a large match.

---

### Task 0: Worktree and baseline

- [ ] `git worktree add -b m27-auto-collect ..\neo_fences-m27 main` (main at `cdf9d5c` or later docs-only commits).
- [ ] `dotnet build` → 0 warnings; `dotnet test` → 606 passed.

### Task 1: Core — collect rules

**Files:**
- Create: `src/NeoFences.Core/Items/CollectRules.cs`, `tests/NeoFences.Core.Tests/Items/CollectRulesTests.cs`
- Modify: `src/NeoFences.Core/Model/Fence.cs` (`Collect`), `Model/FenceEdits.cs` (`SetCollect`), `Model/ItemSorting.cs`
  (`ItemInfo.Created`), `Config/ConfigNormalizer.cs` (repair)

**Interfaces:**
- Produces: `[Flags] enum CollectKinds`; `record CollectRule { Id; Source; Kinds; Patterns; Watermark }`;
  `record CollectPlan(string FenceId, string RuleId, string Path)`; `CollectRules.DesktopSource`, `MaxPerBurst`,
  `KindOf(string name, bool isFolder)`, `Matches(CollectRule, string name, bool isFolder)`, `SameSource(string, string)`,
  `Plan(NeoFencesConfig, ItemsDocument, string source, IReadOnlyList<ItemInfo> arrivals)`,
  `Arrivals(IReadOnlyList<ItemInfo>? before, IReadOnlyList<ItemInfo> now, DateTimeOffset? watermark)`,
  `Normalize(IReadOnlyList<CollectRule>?)`, `Summary(CollectRule)`; `Fence.Collect`; `FenceEdits.SetCollect(config,
  fenceId, rules)`; `ItemInfo(…, long? Size = null, DateTimeOffset Created = default)`.

- [ ] **Step 1: Write the failing tests.** Write this patch to `m27-1-core-tests.patch` and `git apply --whitespace=nowarn` it:

````diff
diff --git a/tests/NeoFences.Core.Tests/Items/CollectRulesTests.cs b/tests/NeoFences.Core.Tests/Items/CollectRulesTests.cs
new file mode 100644
index 0000000..5eb9bbd
--- /dev/null
+++ b/tests/NeoFences.Core.Tests/Items/CollectRulesTests.cs
@@ -0,0 +1,159 @@
+using NeoFences.Core.Config;
+using NeoFences.Core.Items;
+using NeoFences.Core.Model;
+
+namespace NeoFences.Core.Tests.Items;
+
+/// <summary>M27 (0.16.0): auto-collect rules — new files in a watched folder become items (spec 2026-10-05-auto-collect-design).</summary>
+public class CollectRulesTests
+{
+    private static readonly DateTimeOffset Day = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
+
+    private static ItemInfo File(string name, string folder = @"C:\Users\me\Desktop", int minutesAgo = 0) =>
+        new($@"{folder}\{name}", name, IsFolder: false, TypeName: Path.GetExtension(name).ToUpperInvariant(), Day.AddMinutes(-minutesAgo), Created: Day.AddMinutes(-minutesAgo));
+
+    private static ItemInfo Folder(string name, string folder = @"C:\Users\me\Desktop") =>
+        new($@"{folder}\{name}", name, IsFolder: true, TypeName: "", Day, Created: Day);
+
+    private static CollectRule Rule(CollectKinds kinds, string patterns = "", string source = CollectRules.DesktopSource) =>
+        new() { Source = source, Kinds = kinds, Patterns = patterns };
+
+    [Theory]
+    [InlineData("Steam.lnk", CollectKinds.Apps)]
+    [InlineData("site.URL", CollectKinds.Apps)]
+    [InlineData("game.exe", CollectKinds.Apps)]
+    [InlineData("SteamSetup.exe", CollectKinds.Installers)]
+    [InlineData("discord_installer.EXE", CollectKinds.Installers)]
+    [InlineData("tool-Install.exe", CollectKinds.Installers)]
+    [InlineData("app.msi", CollectKinds.Installers)]
+    [InlineData("app.msixbundle", CollectKinds.Installers)]
+    [InlineData("resume.pdf", CollectKinds.Documents)]
+    [InlineData("notes.MD", CollectKinds.Documents)]
+    [InlineData("shot.png", CollectKinds.Pictures)]
+    [InlineData("photo.JPEG", CollectKinds.Pictures)]
+    [InlineData("mods.7z", CollectKinds.Archives)]
+    [InlineData("backup.tar.gz", CollectKinds.Archives)]
+    [InlineData("save.dat", CollectKinds.None)]
+    [InlineData("download.crdownload", CollectKinds.None)]
+    public void KindOf_ByExtension_InstallersBeforeApps(string name, CollectKinds expected) =>
+        Assert.Equal(expected, CollectRules.KindOf(name, isFolder: false));
+
+    [Fact]
+    public void Matches_TheRulesKinds_ItsPatterns_AndFoldersOnlyByAnythingOrAPattern()
+    {
+        Assert.True(CollectRules.Matches(Rule(CollectKinds.Apps), "Steam.lnk", isFolder: false));
+        Assert.False(CollectRules.Matches(Rule(CollectKinds.Apps), "SteamSetup.exe", isFolder: false)); // an installer is not an app
+        Assert.True(CollectRules.Matches(Rule(CollectKinds.Apps | CollectKinds.Installers), "SteamSetup.exe", isFolder: false));
+        Assert.True(CollectRules.Matches(Rule(CollectKinds.None, patterns: "*.dat; iso"), "save.dat", isFolder: false));
+        Assert.True(CollectRules.Matches(Rule(CollectKinds.None, patterns: "iso"), "ubuntu.ISO", isFolder: false));
+        Assert.False(CollectRules.Matches(Rule(CollectKinds.None), "Steam.lnk", isFolder: false)); // nothing chosen: nothing collected
+        Assert.True(CollectRules.Matches(Rule(CollectKinds.Anything), "save.dat", isFolder: false));
+        Assert.True(CollectRules.Matches(Rule(CollectKinds.Anything), "Mods", isFolder: true));
+        Assert.False(CollectRules.Matches(Rule(CollectKinds.Apps | CollectKinds.Documents), "Mods.lnk", isFolder: true)); // a folder named like a file
+        Assert.True(CollectRules.Matches(Rule(CollectKinds.None, patterns: "Project*"), "Project X", isFolder: true));
+    }
+
+    [Fact]
+    public void Plan_FirstMatchingRuleInFenceOrder_Wins_AndNothingAnyFenceHolds()
+    {
+        var setup = Fence.Create("Setup") with { Collect = [Rule(CollectKinds.Installers)] };
+        var apps = Fence.Create("Apps") with { Collect = [Rule(CollectKinds.Apps | CollectKinds.Installers)] };
+        var games = Fence.Create("Games");
+        var config = NeoFencesConfig.CreateDefault() with { Fences = [setup, apps, games] };
+        var held = VirtualItem.Create(@"C:\Users\me\Desktop\Old.lnk");
+        var items = new ItemsDocument().With(games.Id, [held]); // moved by the user to Games: it stays there
+
+        var plan = CollectRules.Plan(config, items, CollectRules.DesktopSource,
+            [File("Steam.lnk"), File("SteamSetup.exe"), File("Old.lnk"), File("save.dat")]);
+
+        Assert.Equal([(apps.Id, @"C:\Users\me\Desktop\Steam.lnk"), (setup.Id, @"C:\Users\me\Desktop\SteamSetup.exe")],
+            plan.Select(entry => (entry.FenceId, entry.Path)));
+        Assert.Equal(apps.Collect[0].Id, plan[0].RuleId);
+    }
+
+    [Fact]
+    public void Plan_OnlyRulesOfThatSource_AndOnlyItemsFences()
+    {
+        var downloads = Fence.Create("Downloads rule") with { Collect = [Rule(CollectKinds.Pictures, source: @"D:\Downloads")] };
+        var view = Fence.Create("Old view") with { View = new FolderView { Path = @"D:\x" }, Collect = [Rule(CollectKinds.Pictures)] };
+        var config = NeoFencesConfig.CreateDefault() with { Fences = [view, downloads] };
+        Assert.Empty(CollectRules.Plan(config, new ItemsDocument(), CollectRules.DesktopSource, [File("shot.png")]));
+        var fromDownloads = CollectRules.Plan(config, new ItemsDocument(), @"d:\downloads\", [File("shot.png", folder: @"D:\Downloads")]);
+        Assert.Equal(downloads.Id, Assert.Single(fromDownloads).FenceId);
+    }
+
+    [Fact]
+    public void Plan_ABurst_IsCappedPerRule()
+    {
+        var apps = Fence.Create("Apps") with { Collect = [Rule(CollectKinds.Anything)] };
+        var config = NeoFencesConfig.CreateDefault() with { Fences = [apps] };
+        var burst = Enumerable.Range(0, CollectRules.MaxPerBurst + 50).Select(index => File($"f{index}.txt")).ToList();
+        Assert.Equal(CollectRules.MaxPerBurst, CollectRules.Plan(config, new ItemsDocument(), CollectRules.DesktopSource, burst).Count);
+    }
+
+    [Fact]
+    public void Plan_TheSameFileTwiceInOneBurst_OnlyOnce()
+    {
+        var apps = Fence.Create("Apps") with { Collect = [Rule(CollectKinds.Apps)] };
+        var config = NeoFencesConfig.CreateDefault() with { Fences = [apps] };
+        Assert.Single(CollectRules.Plan(config, new ItemsDocument(), CollectRules.DesktopSource, [File("Steam.lnk"), File("STEAM.LNK")]));
+    }
+
+    [Fact]
+    public void Arrivals_AreTheNewEntries_OrByCreationTimeWhenThereWasNoEarlierListing()
+    {
+        IReadOnlyList<ItemInfo> before = [File("a.lnk", minutesAgo: 90), File("b.lnk", minutesAgo: 60)];
+        IReadOnlyList<ItemInfo> now = [File("a.lnk", minutesAgo: 90), File("c.lnk", minutesAgo: 1), File("d.lnk", minutesAgo: 120)]; // d moved in: old date
+        Assert.Equal(["c.lnk", "d.lnk"], CollectRules.Arrivals(before, now, watermark: Day.AddMinutes(-30)).Select(entry => entry.Name));
+        // First listing (start, a pendrive back): only what was created after the rule last looked.
+        Assert.Equal(["c.lnk"], CollectRules.Arrivals(null, now, watermark: Day.AddMinutes(-30)).Select(entry => entry.Name));
+        Assert.Empty(CollectRules.Arrivals(null, now, watermark: null)); // never looked: nothing counts as new
+    }
+
+    [Fact]
+    public void Normalize_RepairsHandEditedRules()
+    {
+        var repaired = CollectRules.Normalize([
+            new CollectRule { Id = "", Source = "DESKTOP", Kinds = (CollectKinds)(1 | 128), Patterns = @"C:\bad" },
+            new CollectRule { Id = "same", Source = "   ", Kinds = CollectKinds.Apps },
+            new CollectRule { Id = "same", Source = "relative\\path", Kinds = CollectKinds.Apps },
+            new CollectRule { Id = "same", Source = @"D:\Downloads", Kinds = CollectKinds.Pictures },
+            new CollectRule { Id = "same", Source = @"D:\Other", Kinds = CollectKinds.Pictures },
+        ]);
+        Assert.Equal(3, repaired.Count); // blank and relative sources go
+        Assert.Equal((CollectRules.DesktopSource, CollectKinds.Apps, ""), (repaired[0].Source, repaired[0].Kinds, repaired[0].Patterns));
+        Assert.NotEmpty(repaired[0].Id);
+        Assert.Equal(3, repaired.Select(rule => rule.Id).Distinct().Count()); // a copied id gets a new one
+        Assert.Empty(CollectRules.Normalize(null));
+    }
+
+    [Fact]
+    public void Config_RoundTrip_WritesCollectOnlyWhenSet_AndNormalizes()
+    {
+        var plain = Fence.Create("Plain");
+        var apps = Fence.Create("Apps") with { Collect = [Rule(CollectKinds.Apps | CollectKinds.Installers, patterns: "*.iso") with { Watermark = Day }] };
+        var json = ConfigJson.Serialize(NeoFencesConfig.CreateDefault() with { Fences = [plain, apps] });
+        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"collect\""));
+        var loaded = ConfigNormalizer.Normalize(ConfigJson.Deserialize(json));
+        Assert.Empty(loaded.Fences[0].Collect);
+        Assert.Equal(apps.Collect, loaded.Fences[1].Collect);
+    }
+
+    [Fact]
+    public void SetCollect_ReplacesAFencesRules()
+    {
+        var apps = Fence.Create("Apps");
+        var config = NeoFencesConfig.CreateDefault() with { Fences = [apps] };
+        var rules = new[] { Rule(CollectKinds.Apps) };
+        Assert.Equal(rules, FenceEdits.SetCollect(config, apps.Id, rules).Fences[0].Collect);
+        Assert.Throws<ArgumentException>(() => FenceEdits.SetCollect(config, "nope", rules));
+    }
+
+    [Fact]
+    public void Summary_SaysSourceAndKinds()
+    {
+        Assert.Equal("Desktop · Apps and shortcuts, Installers", CollectRules.Summary(Rule(CollectKinds.Apps | CollectKinds.Installers)));
+        Assert.Equal("Downloads · Pictures, *.iso", CollectRules.Summary(Rule(CollectKinds.Pictures, patterns: "*.iso", source: @"D:\Downloads")));
+        Assert.Equal("Desktop · nothing chosen", CollectRules.Summary(Rule(CollectKinds.None)));
+    }
+}
````

- [ ] **Step 2: Run them to see them fail.** `dotnet test tests/NeoFences.Core.Tests`
  Expected: build fails with `CS0246: The type or namespace name 'CollectKinds' could not be found`.

- [ ] **Step 3: Implement.** Write this patch to `m27-1-core.patch` and apply it:

````diff
diff --git a/src/NeoFences.Core/Config/ConfigNormalizer.cs b/src/NeoFences.Core/Config/ConfigNormalizer.cs
index 8e8b0f0..4bd6fc9 100644
--- a/src/NeoFences.Core/Config/ConfigNormalizer.cs
+++ b/src/NeoFences.Core/Config/ConfigNormalizer.cs
@@ -40,6 +40,7 @@ public static class ConfigNormalizer
                 IconSize = IconSizes.Contains(loadedFence.IconSize) ? loadedFence.IconSize : 48,
                 CustomColor = Appearance.Argb.FromHex(loadedFence.CustomColor)?.ToHex(), // M14: a broken colour is none
                 Layout = Enum.IsDefined(loadedFence.Layout) ? loadedFence.Layout : Items.FenceLayout.Flow, // M24: a typo is Flow
+                Collect = Items.CollectRules.Normalize(loadedFence.Collect), // M27
             };
             fence = fence with { View = fence.IsLibrary ? null : Items.FolderViews.Normalize(loadedFence.View) }; // M21: never on the Library
             seenFenceIds.Add(fence.Id);
diff --git a/src/NeoFences.Core/Items/CollectRules.cs b/src/NeoFences.Core/Items/CollectRules.cs
new file mode 100644
index 0000000..c44fb89
--- /dev/null
+++ b/src/NeoFences.Core/Items/CollectRules.cs
@@ -0,0 +1,145 @@
+using System.IO.Enumeration;
+using NeoFences.Core.Model;
+
+namespace NeoFences.Core.Items;
+
+/// <summary>What a rule collects (M27), by file name; several may be chosen.</summary>
+[Flags]
+public enum CollectKinds { None = 0, Apps = 1, Documents = 2, Pictures = 4, Archives = 8, Installers = 16, Anything = 32 }
+
+/// <summary>
+/// An auto-collect rule of a fence (M27, spec 2026-10-05-auto-collect-design §1): new entries of <see cref="Source"/> that
+/// match become items in the fence. Never a file operation: an item only points at the file.
+/// </summary>
+public sealed record CollectRule
+{
+    public string Id { get; init; } = Guid.NewGuid().ToString("N");
+
+    /// <summary>A full folder path, or <see cref="CollectRules.DesktopSource"/> (the user's and the Public Desktop).</summary>
+    public string Source { get; init; } = CollectRules.DesktopSource;
+
+    public CollectKinds Kinds { get; init; }
+
+    /// <summary>File-name patterns that also match ("*.iso;*.pdf"), the folder-panel syntax.</summary>
+    public string Patterns { get; init; } = "";
+
+    /// <summary>When the rule last looked at its folder: entries created after it are new at the next start; null: never.</summary>
+    public DateTimeOffset? Watermark { get; init; }
+}
+
+/// <param name="RuleId">The rule that collected it (logs).</param>
+public sealed record CollectPlan(string FenceId, string RuleId, string Path);
+
+/// <summary>The auto-collect rules (M27, ADR-049): kinds by name, matching, routing new arrivals to fences. Pure.</summary>
+public static class CollectRules
+{
+    public const string DesktopSource = "desktop";
+
+    /// <summary>At most this many items per rule from one burst (an unzip into a watched folder).</summary>
+    public const int MaxPerBurst = 200;
+
+    private static readonly HashSet<string> AppExtensions = new([".lnk", ".url", ".appref-ms", ".exe"], StringComparer.OrdinalIgnoreCase);
+    private static readonly HashSet<string> InstallerExtensions = new([".msi", ".msix", ".msixbundle", ".appx", ".appxbundle"], StringComparer.OrdinalIgnoreCase);
+    private static readonly HashSet<string> DocumentExtensions = new([".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp", ".rtf", ".txt", ".md", ".csv"], StringComparer.OrdinalIgnoreCase);
+    private static readonly HashSet<string> PictureExtensions = new([".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".heic", ".tif", ".tiff", ".svg"], StringComparer.OrdinalIgnoreCase);
+    private static readonly HashSet<string> ArchiveExtensions = new([".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz"], StringComparer.OrdinalIgnoreCase);
+
+    private static readonly (CollectKinds Kind, string Name)[] KindNames =
+        [(CollectKinds.Apps, "Apps and shortcuts"), (CollectKinds.Documents, "Documents"), (CollectKinds.Pictures, "Pictures"),
+         (CollectKinds.Archives, "Archives"), (CollectKinds.Installers, "Installers"), (CollectKinds.Anything, "Anything")];
+
+    private const CollectKinds AllKinds = CollectKinds.Apps | CollectKinds.Documents | CollectKinds.Pictures | CollectKinds.Archives | CollectKinds.Installers | CollectKinds.Anything;
+
+    /// <summary>A file's kind by its name: an installer before an app (SteamSetup.exe), None for folders and anything else.</summary>
+    public static CollectKinds KindOf(string name, bool isFolder)
+    {
+        if (isFolder) return CollectKinds.None;
+        var extension = Path.GetExtension(name);
+        if (InstallerExtensions.Contains(extension)) return CollectKinds.Installers;
+        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
+            && (name.Contains("setup", StringComparison.OrdinalIgnoreCase) || name.Contains("install", StringComparison.OrdinalIgnoreCase)))
+            return CollectKinds.Installers;
+        return AppExtensions.Contains(extension) ? CollectKinds.Apps
+            : DocumentExtensions.Contains(extension) ? CollectKinds.Documents
+            : PictureExtensions.Contains(extension) ? CollectKinds.Pictures
+            : ArchiveExtensions.Contains(extension) ? CollectKinds.Archives
+            : CollectKinds.None;
+    }
+
+    /// <summary>The rule's kinds or patterns match; a folder only by Anything or a pattern.</summary>
+    public static bool Matches(CollectRule rule, string name, bool isFolder) =>
+        rule.Kinds.HasFlag(CollectKinds.Anything)
+        || (rule.Kinds & KindOf(name, isFolder)) != CollectKinds.None
+        || (FolderViews.ParsePatterns(rule.Patterns) ?? []).Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true));
+
+    public static bool SameSource(string left, string right) =>
+        string.Equals(left, right, StringComparison.OrdinalIgnoreCase) || FolderViews.SameFolder(left, right);
+
+    /// <summary>
+    /// Where new arrivals of one source go (spec §2): for each, the first matching rule of that source in fence order (items
+    /// fences only); nothing that any fence already holds (an item the user moved elsewhere stays there), nothing twice, at
+    /// most <see cref="MaxPerBurst"/> per rule.
+    /// </summary>
+    public static IReadOnlyList<CollectPlan> Plan(NeoFencesConfig config, ItemsDocument items, string source, IReadOnlyList<ItemInfo> arrivals)
+    {
+        var held = items.Fences.Values.SelectMany(list => list).Select(item => item.Target).ToHashSet(ItemKinds.Comparer);
+        var rules = config.Fences.Where(fence => fence.Kind == FenceKind.Items)
+            .SelectMany(fence => fence.Collect.Where(rule => SameSource(rule.Source, source)).Select(rule => (FenceId: fence.Id, Rule: rule))).ToList();
+        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
+        var plan = new List<CollectPlan>();
+        foreach (var arrival in arrivals)
+        {
+            if (!held.Add(arrival.ItemRef)) continue; // held already, or twice in this burst
+            if (rules.FirstOrDefault(candidate => Matches(candidate.Rule, arrival.Name, arrival.IsFolder)) is not { Rule: { } rule } match) continue;
+            var count = counts.GetValueOrDefault(rule.Id);
+            if (count >= MaxPerBurst) continue;
+            counts[rule.Id] = count + 1;
+            plan.Add(new CollectPlan(match.FenceId, rule.Id, arrival.ItemRef));
+        }
+        return plan;
+    }
+
+    /// <summary>
+    /// The entries that arrived: those not in the previous listing, or — with no previous listing (start, a drive back) —
+    /// those created after the rule last looked. Never looked (no watermark): none.
+    /// </summary>
+    public static IReadOnlyList<ItemInfo> Arrivals(IReadOnlyList<ItemInfo>? before, IReadOnlyList<ItemInfo> now, DateTimeOffset? watermark)
+    {
+        if (before is not null)
+        {
+            var known = before.Select(entry => entry.ItemRef).ToHashSet(ItemKinds.Comparer);
+            return [.. now.Where(entry => !known.Contains(entry.ItemRef))];
+        }
+        return watermark is { } since ? [.. now.Where(entry => entry.Created > since)] : [];
+    }
+
+    /// <summary>A hand-edited list repaired: known kinds, valid patterns, a desktop or full-path source (else the rule goes), unique ids.</summary>
+    public static IReadOnlyList<CollectRule> Normalize(IReadOnlyList<CollectRule>? rules)
+    {
+        var ids = new HashSet<string>(StringComparer.Ordinal);
+        var repaired = new List<CollectRule>();
+        foreach (var rule in rules ?? [])
+        {
+            if (rule is null) continue;
+            var source = string.Equals(rule.Source?.Trim(), DesktopSource, StringComparison.OrdinalIgnoreCase) ? DesktopSource : FolderViews.FolderPath(rule.Source ?? "");
+            if (source is null) continue;
+            repaired.Add(rule with
+            {
+                Id = string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id) ? new CollectRule().Id : rule.Id,
+                Source = source,
+                Kinds = rule.Kinds & AllKinds,
+                Patterns = FolderViews.ParsePatterns(rule.Patterns) is not null ? rule.Patterns ?? "" : "",
+            });
+            ids.Add(repaired[^1].Id);
+        }
+        return repaired;
+    }
+
+    /// <summary>"Desktop · Apps and shortcuts, Installers": the rule list's line.</summary>
+    public static string Summary(CollectRule rule)
+    {
+        var source = rule.Source == DesktopSource ? "Desktop" : FolderViews.NameOf(rule.Source);
+        List<string> parts = [.. KindNames.Where(kind => rule.Kinds.HasFlag(kind.Kind)).Select(kind => kind.Name), .. FolderViews.ParsePatterns(rule.Patterns) ?? []];
+        return $"{source} · {(parts.Count == 0 ? "nothing chosen" : string.Join(", ", parts))}";
+    }
+}
diff --git a/src/NeoFences.Core/Model/Fence.cs b/src/NeoFences.Core/Model/Fence.cs
index ff41d39..7800faa 100644
--- a/src/NeoFences.Core/Model/Fence.cs
+++ b/src/NeoFences.Core/Model/Fence.cs
@@ -21,6 +21,14 @@ public sealed record Fence
     /// <summary>A folder view (M21): the fence shows this folder live instead of virtual items. Never set on the Library.</summary>
     public FolderView? View { get; init; }
 
+    /// <summary>Its auto-collect rules (M27): new matching files of their folders become items here. Empty: none.</summary>
+    [System.Text.Json.Serialization.JsonIgnore]
+    public IReadOnlyList<Items.CollectRule> Collect { get; init; } = [];
+
+    /// <summary>config.json's "collect": written only when the fence has rules.</summary>
+    [System.Text.Json.Serialization.JsonPropertyName("collect"), System.Text.Json.Serialization.JsonInclude]
+    private IReadOnlyList<Items.CollectRule>? CollectJson { get => Collect.Count == 0 ? null : Collect; init => Collect = value ?? []; }
+
     /// <summary>How its elements sit (M24): packed in order, or at fixed positions.</summary>
     public Items.FenceLayout Layout { get; init; }
 
diff --git a/src/NeoFences.Core/Model/FenceEdits.cs b/src/NeoFences.Core/Model/FenceEdits.cs
index 6314f8a..38faaab 100644
--- a/src/NeoFences.Core/Model/FenceEdits.cs
+++ b/src/NeoFences.Core/Model/FenceEdits.cs
@@ -45,6 +45,11 @@ public static class FenceEdits
     public static NeoFencesConfig SetLayout(NeoFencesConfig config, string fenceId, Items.FenceLayout layout) =>
         config.WithFence(Require(config, fenceId) with { Layout = layout });
 
+    /// <summary>Auto-collect… (M27): the fence's rules, replaced as a whole.</summary>
+    /// <exception cref="ArgumentException">No fence with that id.</exception>
+    public static NeoFencesConfig SetCollect(NeoFencesConfig config, string fenceId, IReadOnlyList<Items.CollectRule> rules) =>
+        config.WithFence(Require(config, fenceId) with { Collect = [.. rules] });
+
     /// <summary>Icon-only (M8b): labels always shown, or only on hover / selection.</summary>
     public static NeoFencesConfig SetLabels(NeoFencesConfig config, string fenceId, LabelMode labels) =>
         config.WithFence(Require(config, fenceId) with { Labels = labels });
diff --git a/src/NeoFences.Core/Model/ItemSorting.cs b/src/NeoFences.Core/Model/ItemSorting.cs
index 7ed29f5..15520e1 100644
--- a/src/NeoFences.Core/Model/ItemSorting.cs
+++ b/src/NeoFences.Core/Model/ItemSorting.cs
@@ -2,7 +2,8 @@ namespace NeoFences.Core.Model;
 
 /// <summary>What sorting needs to know about one item (filled in by NeoFences.Shell).</summary>
 /// <param name="Size">A file's size in bytes (M26: a folder panel's Size column); null for folders and when unknown.</param>
-public sealed record ItemInfo(string ItemRef, string Name, bool IsFolder, string TypeName, DateTimeOffset Modified, long? Size = null);
+/// <param name="Created">When it was created (M27: an auto-collect rule's catch-up at start); MinValue when unknown.</param>
+public sealed record ItemInfo(string ItemRef, string Name, bool IsFolder, string TypeName, DateTimeOffset Modified, long? Size = null, DateTimeOffset Created = default);
 
 /// <summary>
 /// Item order for "Sort by" (one time) and the library's listing. Name and Type put folders first, like
````

- [ ] **Step 4: Run the tests.** `dotnet test tests/NeoFences.Core.Tests`
  Expected: `Passed! - Failed: 0, Passed: 632`.

- [ ] **Step 5: Commit.** `git add -A && git commit -m "feat: added auto-collect rules to the core: kinds, matching, routing, arrivals and repair"`

### Task 2: Shell and App — rules at work

**Files:**
- Create: `src/NeoFences.App/FenceHost.Collect.cs`, `AutoCollectWindow.xaml(.cs)`
- Modify: `src/NeoFences.Shell/FolderItems.cs` (creation times), `KnownFolders.cs` (`Downloads`),
  `src/NeoFences.App/FenceWindow.xaml(.cs)` (menu entry), `FenceHost.cs` (wiring, pause, removal, stop)

**Interfaces:**
- Consumes: everything Task 1 produces.
- Produces: `FenceWindow.AutoCollectRequested`; `KnownFolders.Downloads(Action<Exception>)`.

- [ ] **Step 1: Implement.** Write this patch to `m27-2-app.patch` and apply it:

````diff
diff --git a/src/NeoFences.App/AutoCollectWindow.xaml b/src/NeoFences.App/AutoCollectWindow.xaml
new file mode 100644
index 0000000..09406e6
--- /dev/null
+++ b/src/NeoFences.App/AutoCollectWindow.xaml
@@ -0,0 +1,70 @@
+<Window x:Class="NeoFences.App.AutoCollectWindow"
+        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
+        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
+        Title="Auto-collect" Width="580" SizeToContent="Height" ResizeMode="NoResize"
+        WindowStartupLocation="CenterScreen" ShowInTaskbar="True" ThemeMode="System">
+    <!-- M27 spec 2026-10-05-auto-collect-design §3: a fence's rules. NeoFences never moves a file: rules only add items. -->
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
+        <TextBlock x:Name="Heading" FontSize="16" FontWeight="SemiBold" Margin="0,0,0,4" />
+        <TextBlock Style="{StaticResource Hint}" Margin="0,0,0,12"
+                   Text="New files that match a rule show up in this fence as items. Your files are never moved; removing an item does not bring it back." />
+        <Grid Margin="0,0,0,14">
+            <Grid.ColumnDefinitions>
+                <ColumnDefinition />
+                <ColumnDefinition Width="Auto" />
+            </Grid.ColumnDefinitions>
+            <ListBox x:Name="RuleList" Height="112" AutomationProperties.Name="Rules" />
+            <StackPanel Grid.Column="1" Margin="8,0,0,0">
+                <Button x:Name="NewButton" Content="New rule" MinWidth="96" AutomationProperties.Name="New rule" />
+                <Button x:Name="RemoveButton" Content="Remove" MinWidth="96" Margin="0,8,0,0" AutomationProperties.Name="Remove rule" />
+            </StackPanel>
+        </Grid>
+        <Grid x:Name="Editor">
+            <Grid.ColumnDefinitions>
+                <ColumnDefinition Width="Auto" />
+                <ColumnDefinition />
+            </Grid.ColumnDefinitions>
+            <Grid.RowDefinitions>
+                <RowDefinition Height="Auto" />
+                <RowDefinition Height="Auto" />
+                <RowDefinition Height="Auto" />
+                <RowDefinition Height="Auto" />
+                <RowDefinition Height="Auto" />
+            </Grid.RowDefinitions>
+            <TextBlock Text="Watch" Style="{StaticResource FieldLabel}" />
+            <ComboBox x:Name="SourceBox" Grid.Column="1" Margin="0,0,0,10" AutomationProperties.Name="Watch folder" />
+
+            <TextBlock Grid.Row="1" Text="Collect" Style="{StaticResource FieldLabel}" VerticalAlignment="Top" Margin="0,4,12,10" />
+            <WrapPanel x:Name="KindsPanel" Grid.Row="1" Grid.Column="1" Margin="0,0,0,6">
+                <CheckBox x:Name="AppsCheck" Content="Apps and shortcuts" Margin="0,0,16,4" />
+                <CheckBox x:Name="InstallersCheck" Content="Installers" Margin="0,0,16,4" />
+                <CheckBox x:Name="DocumentsCheck" Content="Documents" Margin="0,0,16,4" />
+                <CheckBox x:Name="PicturesCheck" Content="Pictures" Margin="0,0,16,4" />
+                <CheckBox x:Name="ArchivesCheck" Content="Archives" Margin="0,0,16,4" />
+                <CheckBox x:Name="AnythingCheck" Content="Anything" Margin="0,0,16,4" />
+            </WrapPanel>
+
+            <TextBlock Grid.Row="2" Text="Also" Style="{StaticResource FieldLabel}" Margin="0,0,12,2" />
+            <TextBox x:Name="PatternsBox" Grid.Row="2" Grid.Column="1" Margin="0,0,0,2"
+                     AutomationProperties.Name="Also these file name patterns, for example *.iso" />
+            <TextBlock x:Name="PatternsHint" Grid.Row="3" Grid.Column="1" Style="{StaticResource Hint}" Margin="0,0,0,10"
+                       Text="Patterns like *.iso;*.torrent — empty adds nothing. Subfolders are not looked into." />
+            <TextBlock x:Name="MatchText" Grid.Row="4" Grid.Column="1" Style="{StaticResource Hint}" Margin="0,0,0,14" />
+        </Grid>
+        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
+            <Button x:Name="OkButton" Content="OK" IsDefault="True" MinWidth="90" Margin="0,0,8,0" />
+            <Button Content="Cancel" IsCancel="True" MinWidth="90" />
+        </StackPanel>
+    </StackPanel>
+</Window>
diff --git a/src/NeoFences.App/AutoCollectWindow.xaml.cs b/src/NeoFences.App/AutoCollectWindow.xaml.cs
new file mode 100644
index 0000000..6dfee29
--- /dev/null
+++ b/src/NeoFences.App/AutoCollectWindow.xaml.cs
@@ -0,0 +1,182 @@
+using System.Windows;
+using System.Windows.Controls;
+using System.Windows.Interop;
+using NeoFences.Core.Items;
+using NeoFences.Core.Model;
+using NeoFences.Shell;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// "Auto-collect…" (M27 spec §3): a fence's rules — the list, and the selected rule's folder, kinds and patterns with a live
+/// "N items here match now". OK gives <see cref="Result"/>; <see cref="Listings"/> are the folders it listed (by source),
+/// so a new rule's "Add these N too?" needs no second listing.
+/// </summary>
+public partial class AutoCollectWindow : Window
+{
+    private const string ChooseTag = "choose";
+    private readonly List<CollectRule> _rules;
+    private readonly string? _downloads;
+    private readonly Dictionary<string, IReadOnlyList<ItemInfo>?> _listings = new(StringComparer.OrdinalIgnoreCase);
+    private readonly HashSet<string> _listing = new(StringComparer.OrdinalIgnoreCase); // folders being listed now
+    private readonly string _patternsHint;
+    private bool _showing; // the editor is being filled from a rule: its change events are not edits
+    private object? _lastSource;
+
+    public IReadOnlyList<CollectRule>? Result { get; private set; }
+
+    public IReadOnlyDictionary<string, IReadOnlyList<ItemInfo>?> Listings => _listings;
+
+    private readonly (CheckBox Box, CollectKinds Kind)[] _kinds;
+
+    public AutoCollectWindow(string fenceTitle, IReadOnlyList<CollectRule> rules, string? downloads)
+    {
+        InitializeComponent();
+        _rules = [.. rules];
+        _downloads = downloads;
+        _patternsHint = PatternsHint.Text;
+        Heading.Text = $"Auto-collect into \"{fenceTitle}\"";
+        _kinds = [(AppsCheck, CollectKinds.Apps), (InstallersCheck, CollectKinds.Installers), (DocumentsCheck, CollectKinds.Documents),
+                  (PicturesCheck, CollectKinds.Pictures), (ArchivesCheck, CollectKinds.Archives), (AnythingCheck, CollectKinds.Anything)];
+        foreach (var (box, _) in _kinds) box.Click += (_, _) => Edit(rule => rule with { Kinds = KindsChecked() });
+        PatternsBox.TextChanged += (_, _) => Edit(rule => rule with { Patterns = PatternsBox.Text.Trim() });
+        SourceBox.SelectionChanged += (_, _) => OnSourceChosen();
+        RuleList.SelectionChanged += (_, _) => ShowRule();
+        NewButton.Click += (_, _) =>
+        {
+            _rules.Add(new CollectRule { Kinds = CollectKinds.Apps | CollectKinds.Installers });
+            FillList(select: _rules.Count - 1);
+        };
+        RemoveButton.Click += (_, _) =>
+        {
+            if (RuleList.SelectedIndex < 0) return;
+            _rules.RemoveAt(RuleList.SelectedIndex);
+            FillList(select: Math.Min(RuleList.SelectedIndex, _rules.Count - 1));
+        };
+        OkButton.Click += (_, _) =>
+        {
+            Result = [.. _rules];
+            DialogResult = true;
+        };
+        FillList(select: _rules.Count > 0 ? 0 : -1);
+    }
+
+    private CollectRule? Selected => RuleList.SelectedIndex >= 0 && RuleList.SelectedIndex < _rules.Count ? _rules[RuleList.SelectedIndex] : null;
+
+    private void FillList(int select)
+    {
+        RuleList.Items.Clear();
+        foreach (var rule in _rules) RuleList.Items.Add(new ListBoxItem { Content = CollectRules.Summary(rule) });
+        RuleList.SelectedIndex = select;
+        ShowRule();
+    }
+
+    /// <summary>The selected rule in the editor (or an empty, disabled editor).</summary>
+    private void ShowRule()
+    {
+        _showing = true;
+        var rule = Selected;
+        Editor.IsEnabled = rule is not null;
+        RemoveButton.IsEnabled = rule is not null;
+        FillSources(rule?.Source);
+        foreach (var (box, kind) in _kinds) box.IsChecked = rule?.Kinds.HasFlag(kind) == true;
+        PatternsBox.Text = rule?.Patterns ?? "";
+        _showing = false;
+        Validate();
+    }
+
+    /// <summary>Desktop, Downloads, the rule's own folder, and "Choose folder…".</summary>
+    private void FillSources(string? source)
+    {
+        SourceBox.Items.Clear();
+        SourceBox.Items.Add(new ComboBoxItem { Content = "Desktop", Tag = CollectRules.DesktopSource });
+        if (_downloads is not null) SourceBox.Items.Add(new ComboBoxItem { Content = $"Downloads ({_downloads})", Tag = _downloads });
+        if (source is not null && source != CollectRules.DesktopSource && (_downloads is null || !FolderViews.SameFolder(source, _downloads)))
+            SourceBox.Items.Add(new ComboBoxItem { Content = source, Tag = source });
+        SourceBox.Items.Add(new ComboBoxItem { Content = "Choose folder…", Tag = ChooseTag });
+        SourceBox.SelectedItem = SourceBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => source is not null && item.Tag is string tag && tag != ChooseTag && CollectRules.SameSource(tag, source));
+        _lastSource = SourceBox.SelectedItem;
+    }
+
+    private void OnSourceChosen()
+    {
+        if (_showing || SourceBox.SelectedItem is not ComboBoxItem { Tag: string tag } chosen) return;
+        if (tag != ChooseTag)
+        {
+            _lastSource = chosen;
+            Edit(rule => rule with { Source = tag });
+            return;
+        }
+        var picked = PathPicker.TryPickFolder(new WindowInteropHelper(this).Handle, "Choose a folder to watch",
+            failure => Serilog.Log.Warning(failure, "auto-collect: the folder dialog failed"));
+        if (picked is null)
+        {
+            _showing = true;
+            SourceBox.SelectedItem = _lastSource; // cancelled: the folder it had
+            _showing = false;
+            return;
+        }
+        Edit(rule => rule with { Source = picked });
+        ShowRule();
+    }
+
+    private CollectKinds KindsChecked() => _kinds.Where(entry => entry.Box.IsChecked == true).Aggregate(CollectKinds.None, (kinds, entry) => kinds | entry.Kind);
+
+    /// <summary>The selected rule takes the editor's change; its line in the list follows.</summary>
+    private void Edit(Func<CollectRule, CollectRule> change)
+    {
+        if (_showing || Selected is not { } rule) return;
+        var index = RuleList.SelectedIndex;
+        _rules[index] = change(rule);
+        if (RuleList.Items[index] is ListBoxItem line) line.Content = CollectRules.Summary(_rules[index]);
+        Validate();
+    }
+
+    private void Validate()
+    {
+        var patternsOk = FolderViews.ParsePatterns(PatternsBox.Text) is not null;
+        PatternsHint.Text = patternsOk ? _patternsHint : "Use file name patterns like *.iso;*.torrent — no paths, and none of \\ / : \" < > |";
+        PatternsHint.Foreground = patternsOk ? (System.Windows.Media.Brush)FindResource("TextFillColorSecondaryBrush") : System.Windows.Media.Brushes.IndianRed;
+        OkButton.IsEnabled = _rules.All(rule => FolderViews.ParsePatterns(rule.Patterns) is not null);
+        ShowMatches();
+    }
+
+    /// <summary>"N items here match now", from a listing made off the UI thread (a dead share must not freeze the dialog).</summary>
+    private void ShowMatches()
+    {
+        if (Selected is not { } rule)
+        {
+            MatchText.Text = "";
+            return;
+        }
+        if (!_listings.TryGetValue(rule.Source, out var entries))
+        {
+            MatchText.Text = "Looking…";
+            if (!_listing.Add(rule.Source)) return;
+            var source = rule.Source;
+            Task.Run(() => ListSource(source)).ContinueWith(listed =>
+            {
+                _listing.Remove(source);
+                _listings[source] = listed.IsFaulted ? null : listed.Result;
+                ShowMatches();
+            }, TaskScheduler.FromCurrentSynchronizationContext());
+            return;
+        }
+        if (entries is null)
+        {
+            MatchText.Text = "This folder is not available now; the rule waits for it.";
+            return;
+        }
+        var count = entries.Count(entry => CollectRules.Matches(rule, entry.Name, entry.IsFolder));
+        MatchText.Text = count == 1 ? "1 item here matches now." : $"{count:N0} items here match now.";
+    }
+
+    /// <summary>The desktop is the user's and the Public Desktop; null when the folder (or both desktops) cannot be read.</summary>
+    private static IReadOnlyList<ItemInfo>? ListSource(string source)
+    {
+        if (source != CollectRules.DesktopSource) return FolderItems.TryList(source);
+        var user = FolderItems.TryList(DesktopItems.UserDesktop);
+        var common = FolderItems.TryList(DesktopItems.PublicDesktop);
+        return user is null && common is null ? null : [.. user ?? [], .. common ?? []];
+    }
+}
diff --git a/src/NeoFences.App/FenceHost.Collect.cs b/src/NeoFences.App/FenceHost.Collect.cs
new file mode 100644
index 0000000..cf2012e
--- /dev/null
+++ b/src/NeoFences.App/FenceHost.Collect.cs
@@ -0,0 +1,142 @@
+using System.Windows;
+using NeoFences.Core.Items;
+using NeoFences.Core.Model;
+using NeoFences.Shell;
+using Serilog;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// Auto-collect rules (M27, spec 2026-10-05-auto-collect-design, ADR-049): one <see cref="FolderLister"/> per watched folder
+/// (the desktop is two); each listing is compared with the last one, and what arrived goes to the first matching rule's
+/// fence as items. Never a file operation. Paused in game mode and while NeoFences is paused: the next listing catches up.
+/// </summary>
+public sealed partial class FenceHost
+{
+    private readonly Dictionary<string, (string Source, FolderLister Lister)> _collectListers = new(StringComparer.OrdinalIgnoreCase);
+    // The last listing per watched folder; missing or null: none yet, or the folder was not available (the next one catches up).
+    private readonly Dictionary<string, IReadOnlyList<ItemInfo>?> _collectListings = new(StringComparer.OrdinalIgnoreCase);
+    private string? _downloadsFolder;
+
+    /// <summary>Every folder some rule watches, with its rule source (the desktop source is the user's and the Public Desktop).</summary>
+    private IEnumerable<(string Source, string Folder)> CollectFolders() =>
+        _config.Fences.Where(fence => fence.Kind == FenceKind.Items).SelectMany(fence => fence.Collect).Select(rule => rule.Source)
+            .Distinct(StringComparer.OrdinalIgnoreCase)
+            .SelectMany(source => source == CollectRules.DesktopSource
+                ? new[] { (source, DesktopItems.UserDesktop), (source, DesktopItems.PublicDesktop) }
+                : [(source, source)]);
+
+    /// <summary>A lister per watched folder; listers of folders no rule watches any more go (after every config change).</summary>
+    private void EnsureCollectListers()
+    {
+        var wanted = CollectFolders().GroupBy(entry => entry.Folder, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First().Source, StringComparer.OrdinalIgnoreCase);
+        foreach (var (folder, (_, lister)) in _collectListers.ToList())
+        {
+            if (wanted.ContainsKey(folder)) continue;
+            lister.Dispose();
+            _collectListers.Remove(folder);
+            _collectListings.Remove(folder);
+        }
+        foreach (var (folder, source) in wanted.Where(entry => !_collectListers.ContainsKey(entry.Key)))
+        {
+            var lister = new FolderLister(folder, noticeOwner: _messages.Handle, label: "auto-collect",
+                show: listed => OnCollectListing(source, folder, listed),
+                logFailure: failure => Log.Warning(failure, "auto-collect: cannot watch {Folder}", folder));
+            if (_gameMode || _paused) lister.SetPaused(true);
+            _collectListers[folder] = (source, lister);
+        }
+    }
+
+    private void StopCollectListers()
+    {
+        foreach (var (_, lister) in _collectListers.Values) lister.Dispose();
+        _collectListers.Clear();
+    }
+
+    /// <summary>Game mode or Pause: changes wait; resuming lists once, and what arrived meanwhile is collected then.</summary>
+    private void SetCollectPaused()
+    {
+        foreach (var (_, lister) in _collectListers.Values) lister.SetPaused(_gameMode || _paused);
+    }
+
+    private bool ReleaseCollectForRemoval(nint handle) => _collectListers.Values.Aggregate(false, (released, entry) => entry.Lister.ReleaseForRemoval(handle) | released);
+
+    /// <summary>
+    /// A watched folder was listed (on the UI thread): what arrived since the last listing — or, with none (start, a drive
+    /// back), what was created after its rules last looked — goes to the rules' fences.
+    /// </summary>
+    private void OnCollectListing(string source, string folder, IReadOnlyList<ItemInfo>? listed)
+    {
+        if (!_collectListers.ContainsKey(folder)) return; // its rules went meanwhile
+        var hadListing = _collectListings.TryGetValue(folder, out var before) && before is not null;
+        _collectListings[folder] = listed;
+        if (listed is null) return; // not available: the next listing catches up by the watermark
+        var rules = _config.Fences.SelectMany(fence => fence.Collect).Where(rule => CollectRules.SameSource(rule.Source, source)).ToList();
+        if (rules.Count == 0) return;
+        var watermark = rules.Min(rule => rule.Watermark);
+        var arrivals = CollectRules.Arrivals(hadListing ? before : null, listed, watermark);
+        if (hadListing && arrivals.Count == 0) return; // nothing new: the watermark stays (no config write per change)
+        var plan = CollectRules.Plan(_config, _items, source, arrivals);
+        foreach (var fence in plan.GroupBy(entry => entry.FenceId))
+        {
+            _items = ItemEdits.Add(_items, fence.Key, [.. fence.Select(entry => VirtualItem.Create(entry.Path))]).Document;
+            foreach (var entry in fence) Log.Information("auto-collect: {Path} -> fence {FenceId} (rule {RuleId})", entry.Path, entry.FenceId, entry.RuleId);
+        }
+        if (arrivals.Count > plan.Count) Log.Information("auto-collect: {Skipped} of {Count} new entries in {Folder} not collected (no rule matched, already held, or too many at once)", arrivals.Count - plan.Count, arrivals.Count, folder);
+        // What the rules of this source have seen: a restart does not collect it again (an item the user removed stays removed).
+        var now = DateTimeOffset.Now;
+        _config = _config with
+        {
+            Fences = [.. _config.Fences.Select(fence => fence.Collect.Any(rule => CollectRules.SameSource(rule.Source, source))
+                ? fence with { Collect = [.. fence.Collect.Select(rule => CollectRules.SameSource(rule.Source, source) ? rule with { Watermark = now } : rule)] }
+                : fence)],
+        };
+        if (plan.Count > 0) ItemsChanged(checkTargets: [.. plan.Select(entry => entry.Path)]);
+        else ScheduleSave();
+    }
+
+    /// <summary>Fence menu → Auto-collect…: the fence's rules; a new rule offers the files that match now ("Add these N too?").</summary>
+    private void EditCollectRules(FenceWindow window)
+    {
+        var fenceId = window.FenceId;
+        if (_config.Fences.FirstOrDefault(fence => fence.Id == fenceId) is not { Kind: FenceKind.Items } fence) return;
+        _downloadsFolder ??= KnownFolders.Downloads(failure => Log.Information(failure, "auto-collect: the Downloads folder is not known"));
+        var dialog = new AutoCollectWindow(fence.Title, fence.Collect, _downloadsFolder) { Owner = window };
+        if (dialog.ShowDialog() != true || dialog.Result is not { } rules) return;
+        if (!_config.Fences.Any(candidate => candidate.Id == fenceId)) return; // a restore took the fence meanwhile
+        var now = DateTimeOffset.Now;
+        var known = fence.Collect.Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal);
+        // A new or changed rule starts looking now: what is there already is offered once, not collected as "new".
+        rules = [.. rules.Select(rule => fence.Collect.FirstOrDefault(old => old.Id == rule.Id) is { } old && old with { Watermark = null } == rule with { Watermark = null } ? old : rule with { Watermark = now })];
+        _config = FenceEdits.SetCollect(_config, fenceId, rules);
+        Log.Information("fence {FenceId} auto-collect rules: {Rules}", fenceId, rules.Select(CollectRules.Summary));
+        EnsureCollectListers();
+        ScheduleSave();
+        RefreshFenceMenus();
+        foreach (var rule in rules.Where(rule => !known.Contains(rule.Id))) OfferExisting(window, fenceId, rule, dialog.Listings);
+    }
+
+    /// <summary>"Add these N too?" for a new rule: the files of its folder that match now and no fence holds (at most 200).</summary>
+    private void OfferExisting(FenceWindow window, string fenceId, CollectRule rule, IReadOnlyDictionary<string, IReadOnlyList<ItemInfo>?> listings)
+    {
+        var entries = listings.Where(listing => listing.Value is not null && CollectRules.SameSource(listing.Key, rule.Source)).SelectMany(listing => listing.Value!).ToList();
+        var lone = _config with { Fences = [.. _config.Fences.Where(fence => fence.Id == fenceId).Select(fence => fence with { Collect = [rule] })] };
+        var plan = CollectRules.Plan(lone, _items, rule.Source, entries);
+        if (plan.Count == 0) return;
+        var answer = MessageBox.Show(window, $"{plan.Count}{(plan.Count == CollectRules.MaxPerBurst ? " (the first)" : "")} item{(plan.Count == 1 ? "" : "s")} in {CollectRules.Summary(rule).Split(" · ")[0]} match this rule already.\n\nAdd these {plan.Count} too?\n\nYour files are not moved: the fence only shows them.",
+            "NeoFences — auto-collect", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
+        if (answer != MessageBoxResult.Yes) return;
+        _items = ItemEdits.Add(_items, fenceId, [.. plan.Select(entry => VirtualItem.Create(entry.Path))]).Document;
+        Log.Information("auto-collect: {Count} existing item(s) added to fence {FenceId} by a new rule", plan.Count, fenceId);
+        ItemsChanged(checkTargets: [.. plan.Select(entry => entry.Path)]);
+    }
+
+    /// <summary>The fence menus say how many rules each fence has.</summary>
+    private void RefreshFenceMenus()
+    {
+        foreach (var window in _windows.Values)
+        {
+            if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is { } shown) window.Refresh(shown);
+        }
+    }
+}
diff --git a/src/NeoFences.App/FenceHost.cs b/src/NeoFences.App/FenceHost.cs
index bd28478..698efd5 100644
--- a/src/NeoFences.App/FenceHost.cs
+++ b/src/NeoFences.App/FenceHost.cs
@@ -109,6 +109,7 @@ public sealed partial class FenceHost
             if (_libraryLister?.ReleaseForRemoval(handle) == true) Log.Information("the library folder's drive is being removed: released it");
             if (ReleaseLibraryForRemoval(handle)) Log.Information("a drive the game library watches is being removed: released it");
             if (ReleasePanelsForRemoval(handle)) Log.Information("a drive a folder panel shows is being removed: released it");
+            if (ReleaseCollectForRemoval(handle)) Log.Information("a drive an auto-collect rule watches is being removed: released it");
             if (ReleaseTargetWatcherForRemoval(handle)) Log.Information("a drive holding item targets is being removed: released it");
         };
         _specialIconsTimer.Tick += (_, _) => RefreshSpecialIcons();
@@ -210,6 +211,7 @@ public sealed partial class FenceHost
         _specialIcons?.Dispose();
         _libraryLister?.Dispose();
         StopPanelListers(); // M26
+        StopCollectListers(); // M27
         _widgetTimer?.Stop(); // M25
         _systemStats?.Dispose();
         StopLibraryWatchers();
@@ -278,6 +280,7 @@ public sealed partial class FenceHost
         window.NewLibraryRequested += CreateLibraryFence;
         window.NewFolderPanelRequested += () => NewFolderPanel(window.Handle); // M26
         window.AddFolderPanelRequested += () => AddFolderPanel(window);
+        window.AutoCollectRequested += () => EditCollectRules(window); // M27
         window.PanelCommandRequested += (itemId, command) => OnPanelCommand(window, itemId, command);
         window.AddGamesRequested += () => AddGames(window); // M22
         window.LayoutRequested += layout => SetFenceLayout(window, layout); // M24
@@ -337,6 +340,7 @@ public sealed partial class FenceHost
     private void RefreshWindows()
     {
         EnsurePanelListers(); // M26: a lister per panel, on the folder it shows
+        EnsureCollectListers(); // M27: a lister per folder a rule watches
         foreach (var window in _windows.Values) RefreshWindow(window);
         UpdateWidgetTimer(); // M25: a timer only while some fence holds a widget
     }
@@ -1133,6 +1137,7 @@ public sealed partial class FenceHost
         UpdateMouseHook();
         _libraryLister?.SetPaused(gameMode);
         SetPanelsPaused(gameMode); // M21, M26
+        SetCollectPaused(); // M27
         if (!gameMode) ApplyDeferredShellWork();
         UpdatePeekHotkey();
         _trayIcon?.SetTooltip(TrayTooltip());
@@ -1165,6 +1170,7 @@ public sealed partial class FenceHost
             EndDrawOverlay();
         }
         _paused = paused;
+        SetCollectPaused(); // M27: new files wait while paused
         if (paused)
         {
             _quickHidden = false; // resuming shows everything
diff --git a/src/NeoFences.App/FenceWindow.xaml b/src/NeoFences.App/FenceWindow.xaml
index cb7a63b..8abb0e7 100644
--- a/src/NeoFences.App/FenceWindow.xaml
+++ b/src/NeoFences.App/FenceWindow.xaml
@@ -91,6 +91,8 @@
                         </MenuItem>
                         <!-- M26: a folder inside the fence, beside its other elements. -->
                         <MenuItem x:Name="AddFolderPanelItem" Header="Add folder panel…" />
+                        <!-- M27: new matching files of a folder become items here. -->
+                        <MenuItem x:Name="AutoCollectItem" Header="Auto-collect…" />
                         <MenuItem x:Name="RefreshItem" Header="Refresh" />
                         <Separator />
                         <MenuItem x:Name="NewFenceItem" Header="New fence" />
diff --git a/src/NeoFences.App/FenceWindow.xaml.cs b/src/NeoFences.App/FenceWindow.xaml.cs
index 76273ac..9684b16 100644
--- a/src/NeoFences.App/FenceWindow.xaml.cs
+++ b/src/NeoFences.App/FenceWindow.xaml.cs
@@ -158,6 +158,8 @@ public partial class FenceWindow : Window
     public event Action? NewFolderPanelRequested;
     /// <summary>Fence menu → "Add folder panel…" (M26).</summary>
     public event Action? AddFolderPanelRequested;
+    /// <summary>Fence menu → "Auto-collect…" (M27).</summary>
+    public event Action? AutoCollectRequested;
     /// <summary>A folder panel asks for something (M26): the panel item's key and what.</summary>
     public event Action<string, PanelCommand>? PanelCommandRequested;
     /// <summary>A drive arrived or was removed (Windows tells top-level windows): missing and unavailable items are checked again.</summary>
@@ -213,6 +215,7 @@ public partial class FenceWindow : Window
         NewLibraryItem.Click += (_, _) => NewLibraryRequested?.Invoke();
         NewFolderPanelItem.Click += (_, _) => NewFolderPanelRequested?.Invoke(); // M26
         AddFolderPanelItem.Click += (_, _) => AddFolderPanelRequested?.Invoke();
+        AutoCollectItem.Click += (_, _) => AutoCollectRequested?.Invoke(); // M27
         AddGamesItem.Click += (_, _) => AddGamesRequested?.Invoke();
         AddClockItem.Click += (_, _) => AddWidgetRequested?.Invoke(WidgetKind.Clock); // M25
         AddDateItem.Click += (_, _) => AddWidgetRequested?.Invoke(WidgetKind.Date);
@@ -316,6 +319,8 @@ public partial class FenceWindow : Window
         AddGamesItem.Visibility = items ? Visibility.Visible : Visibility.Collapsed;
         AddWidgetItem.Visibility = items ? Visibility.Visible : Visibility.Collapsed;
         AddFolderPanelItem.Visibility = items ? Visibility.Visible : Visibility.Collapsed;
+        AutoCollectItem.Visibility = items ? Visibility.Visible : Visibility.Collapsed;
+        AutoCollectItem.Header = fence.Collect.Count switch { 0 => "Auto-collect…", 1 => "Auto-collect… (1 rule)", var count => $"Auto-collect… ({count} rules)" }; // M27
         SortItem.Visibility = _kind == FenceKind.Library ? Visibility.Collapsed : Visibility.Visible;
         foreach (var sortItem in SortItem.Items.OfType<MenuItem>()) sortItem.IsChecked = view && Equals(sortItem.Tag, fence.View!.Sort);
         DeleteItem.Header = _kind switch
diff --git a/src/NeoFences.Shell/FolderItems.cs b/src/NeoFences.Shell/FolderItems.cs
index 444b715..4b17094 100644
--- a/src/NeoFences.Shell/FolderItems.cs
+++ b/src/NeoFences.Shell/FolderItems.cs
@@ -38,7 +38,8 @@ public static class FolderItems
         var isFolder = entry is DirectoryInfo;
         return new ItemInfo(entry.FullName, entry.Name, isFolder, isFolder ? "" : entry.Extension.ToUpperInvariant(),
             entry.Exists ? entry.LastWriteTime : DateTimeOffset.MinValue,
-            Size: entry is FileInfo { Exists: true } file ? file.Length : null); // M26: a panel's Size column (read with the listing)
+            Size: entry is FileInfo { Exists: true } file ? file.Length : null, // M26: a panel's Size column (read with the listing)
+            Created: entry.Exists ? entry.CreationTime : DateTimeOffset.MinValue); // M27: an auto-collect rule's catch-up
     }
 }
 
diff --git a/src/NeoFences.Shell/KnownFolders.cs b/src/NeoFences.Shell/KnownFolders.cs
index 07356b8..e441f6a 100644
--- a/src/NeoFences.Shell/KnownFolders.cs
+++ b/src/NeoFences.Shell/KnownFolders.cs
@@ -11,6 +11,9 @@ public static class KnownFolders
     public static IReadOnlyList<string> BusyFolders(Action<Exception> logFailure) =>
         [.. new[] { PInvoke.FOLDERID_Downloads, PInvoke.FOLDERID_Screenshots }.Select(folderId => TryGetPath(folderId, logFailure)).OfType<string>()];
 
+    /// <summary>Downloads, where Windows has it (M27: a rule's source); null when it cannot be told (logged).</summary>
+    public static string? Downloads(Action<Exception> logFailure) => TryGetPath(PInvoke.FOLDERID_Downloads, logFailure);
+
     private static unsafe string? TryGetPath(Guid folderId, Action<Exception> logFailure)
     {
         PWSTR path = default;
````

- [ ] **Step 2: Build and test.** `dotnet build` → `0 Warning(s)`; `dotnet test` → 632 passed.

- [ ] **Step 3: Commit.** `git add -A && git commit -m "feat: added auto-collect rules to fences: watched folders, the rules dialog and catch-up"`

### Task 3: Docs

**Files:**
- Modify: `docs/DECISIONS.md` (ADR-049), `docs/ARCHITECTURE.md`, `docs/FEATURES.md`, `docs/TEST-CHECKLIST.md` (section AM)
- Create: `docs/research/m27-auto-collect.md`

- [ ] **Step 1: Apply.** Write this patch to `m27-3-docs.patch` and apply it:

````diff
diff --git a/docs/ARCHITECTURE.md b/docs/ARCHITECTURE.md
index f582b4a..993f6c4 100644
--- a/docs/ARCHITECTURE.md
+++ b/docs/ARCHITECTURE.md
@@ -82,6 +82,7 @@ happen) is behind Shift+right-click, under a line saying it acts on the real fil
 | Core/FencePlacement, Core/Lifecycle | px↔DIP placement, containing monitor; `RunState` (hide icons, quick-hide, Pause, game mode), session-end policy, restart throttle, watcher backoff | — |
 | Core/Library | Valve/Epic parsing, `GameCatalog` merge, `LibraryFiles` plan, `GameLaunchers.LauncherOf` (ADR-032); `GameItems` (M22): `Migrate` (a library fence → game items), `NewGames`, `AddNew`, `Retarget` (by game id), `ShowsCover` | — |
 | Core/Items (M24) | `GridSpan` (1–4 × 1–4), `GridCell`, `FenceLayout` (Flow / Free), `FenceGrid.Arrange` (dense packing; Free: stored cells, collisions and out-of-width elements to free spots), `CellAt`, `NearestFree`, `PlaceDropped`, `SpanOf` (covers 1×2); `ItemEdits.SetSize`, `Place`; repairs | — |
+| Core/Items (M27) | `CollectRules`: `CollectRule` (source, `CollectKinds`, patterns, watermark), `KindOf` (installers before apps), `Matches`, `Plan` (first matching rule in fence order, nothing held, ≤ 200 per rule), `Arrivals` (new entries, or created after the watermark), `Normalize`, `Summary`; `FenceEdits.SetCollect`; `ItemInfo.Created` | System.IO.Enumeration |
 | Core/Items (M26) | `FolderPanels`: `FolderPanel` (look Details / List / Icons, show, patterns, newest N, sort + direction), `IsPanel`, `Create` (busy folders newest first), `Select` (the M21 rules plus Size and direction; `FolderViews.Select` maps to it), `HeaderSort`, `Columns`, `Fills`, `HeaderOf`, `SizeText`, `MigrateViews`; `PanelPlace` (Into / Back / Up / Home, never above the panel's folder); `ItemInfo.Size` | System.IO.Enumeration |
 | Core/Items (M25) | `Widgets`: `WidgetKind` (Clock / Date / Stats), `neofences:widget/<kind>` targets (`ItemKind.Widget`, never checked or watched), default spans, `WidgetOptions`, clock text, date page, stat rows, `NextTick` | System.Globalization |
 | Shell/SystemStats (M25) | CPU (`GetSystemTimes` deltas), RAM (`GlobalMemoryStatusEx`), C: used (`GetDiskFreeSpaceEx`), GPU (PDH `\GPU Engine(*engtype_3D)\Utilization Percentage`, summed); null per value on failure | PDH, kernel32 |
@@ -94,6 +95,7 @@ happen) is behind Shift+right-click, under a line saying it acts on the real fil
 | App/FenceWindow, FenceItemView, IconLoader | fence UI: item grid (ListBox + WrapPanel), keys (Enter, Del = remove, F2 / Alt+Enter = Properties), drop caret, Missing badge / Unavailable dimming, empty-fence hint, `WM_DEVICECHANGE` → drives changed; names/icons on two STA threads, an item's own name and icon win, websites show the default browser's icon | WPF, frozen BitmapSource |
 | App/ItemPropertiesWindow, MissingItemWindow | Properties and Add item… (name, target + Browse, live Found / Missing / Drive not connected, arguments, run as admin, icon from a file / a picture / reset, note); the missing-target question | WPF (Fluent) |
 | App/FenceHost.FolderPanels, FolderPanelView, FolderPanelModel, FolderViewWindow (M26) | folder panels (ADR-048): one `FolderLister` per panel on the folder it shows (hidden tabs keep listing), selection off the UI thread, browsing in memory, the panel menu (Look, Sort by, settings, Open folder, Size with Fill fence, Show as icon), Add folder panel…, New folder panel…, Show as folder panel, the entries' safe menu and Windows' menu, drag-out, removal notices, game-mode pause, the folder-view migration (a snapshot first); the control: Details (`GridView`, header sort), List, Icons, virtualized rows, icons as rows load; the fence leaves a panel's own clicks, keys and wheel to it and refuses drops over it | WPF (Fluent) |
+| App/FenceHost.Collect, AutoCollectWindow (M27) | auto-collect rules (ADR-049): a `FolderLister` per watched folder (the desktop is two), arrivals routed to fences as items, watermarks, game-mode and Pause hold, removal release; Fence menu → Auto-collect… (the rules, a live "N items here match now", "Add these N too?" for a new rule) | WPF (Fluent) |
 | App/FolderLister, FenceHost.Library | a folder's live listing (250 ms coalescing, 7 s retry, watcher backoff, removal notices, game-mode pause, the folder's own rename) for folder panels (M21, M26) and the Game Library's own `library\` folder (former Portal machinery), shown as tiles; scans launchers, game folders and Desktop game shortcuts (ADR-032) | FileSystemWatcher |
 | App/SettingsWindow | General (Start with Windows, Hide desktop icons while NeoFences runs, Peek hotkey) · Fences · Appearance · Snapshots · Game Library · Game mode · Updates · About | WPF ThemeMode |
 | App/InstallHooks, Shell/StartupRegistration, Core/StartupPolicy, build/pack.ps1 | Velopack installer and auto-update (ADR-023, ADR-039) | Velopack 1.2.161 |
@@ -129,7 +131,7 @@ happen) is behind Shift+right-click, under a line saying it acts on the real fil
 
 `%LOCALAPPDATA%\NeoFences\`
 - `config.json` (+ `.bak`, `.tmp` transient) — schema 5
-  - shape: `{ schemaVersion, settings: { hideDesktopIcons, peekHotkey, … }, fences: [ { id, title, isLibrary, view: { path, show, sort, newest, patterns } (M21), layout (M24), iconSize, rolledUp, locked, labels, tabs, activeTab, tabColor, customColor } ], layouts: { <fingerprint>: { monitors, fences: { <fenceId>: { monitor, x, y, w, h } } } }, library, lastLayoutFingerprint }`
+  - shape: `{ schemaVersion, settings: { hideDesktopIcons, peekHotkey, … }, fences: [ { id, title, isLibrary, view: { path, show, sort, newest, patterns } (M21), layout (M24), collect: [ { id, source, kinds, patterns, watermark } ] (M27), iconSize, rolledUp, locked, labels, tabs, activeTab, tabColor, customColor } ], layouts: { <fingerprint>: { monitors, fences: { <fenceId>: { monitor, x, y, w, h } } } }, library, lastLayoutFingerprint }`
 - `items.json` (+ `.bak`) — schema 1 (ADR-041)
   - shape: `{ schema, fences: { <fenceId>: [ { id, target, name, icon: { file, index } | { image }, arguments, runAsAdmin, note, gameId, showAs (M22), size: { columns, rows }, cell: { column, row } (M24), widget: { seconds, date } (M25), panel: { look, show, patterns, newest, sort, descending }, fill (M26) } ] } }`
   - a list is removed only with its fence (Delete fence); lists of fences the config does not have stay (a fallback config never costs items; ADR-041 amended)
@@ -169,6 +171,11 @@ games… is open; after each scan game items follow their game's shortcut and ne
 Game Library fence becomes an items fence once (a "Before games became items" snapshot first). The library fence kind
 stays in the code, not in the menus (ADR-045, `research/m22-games-as-items.md`).
 
+**0.16.0 (M27, auto-collect rules)**: a fence can collect new files of a folder by itself (fence menu → Auto-collect…):
+the desktop or any folder, by kind (apps and shortcuts, installers, documents, pictures, archives, anything) or pattern;
+each new matching file becomes an item in the first fence whose rule matches; nothing is moved; removing an item does not
+bring it back; files created while NeoFences was closed are caught up at start (ADR-049, `research/m27-auto-collect.md`).
+
 **0.15.0 (M26, the folder panel element)**: any fence can show a folder as a panel — Details (columns that sort), List
 or Icons — beside its other elements, at 1–4 × 1–4 cells or filling the fence (fence menu → Add folder panel…, tray → New
 folder panel…, a folder item → Show as folder panel). Double-clicking a subfolder browses inside the panel (Back / Up /
diff --git a/docs/DECISIONS.md b/docs/DECISIONS.md
index 482ffa8..117ab95 100644
--- a/docs/DECISIONS.md
+++ b/docs/DECISIONS.md
@@ -1298,3 +1298,24 @@ and Icons; double-clicking a subfolder browses inside the panel; sizes 1–4 ×
 
 **Consequences.** One lister per panel (hidden tabs keep listing, game mode pauses them); rows are virtualized and icons
 load as rows come into view; at most 500 entries, then "+ N more". An older NeoFences shows a panel as a plain folder icon.
+
+## ADR-049 — Auto-collect rules
+**Date:** 2026-10-05 · **Status:** Accepted · **Continues:** ADR-040 (dynamic collections parked for later), ADR-048
+
+**Context.** The user hides the native desktop icons, so a shortcut an installer drops on the desktop is invisible. The
+other half of the "dynamic collections" parked in ADR-040: fences that gather matching items by themselves. The user chose
+this milestone and approved the proposed defaults.
+
+**Decision.**
+- A fence holds rules (`Fence.Collect`): a source (a folder, or the desktop = the user's and the Public Desktop), kinds by
+  file name (Apps and shortcuts, Installers, Documents, Pictures, Archives, Anything) and optional patterns.
+- One `FolderLister` per watched folder; each listing is compared with the last one, and what arrived (created, renamed
+  or moved in) goes to the first matching rule in fence order, as an item. Nothing any fence already holds; at most 200
+  per rule per burst. Never a file operation.
+- Removed stays removed: only arrivals count. At start (or a drive back) what was created after the rules last looked
+  (`Watermark`, updated when something arrived) is caught up.
+- A new rule offers what matches already once ("Add these N too?", default No). Game mode and Pause hold arrivals until
+  they end.
+
+**Consequences.** A file moved into a watched folder while NeoFences was closed keeps its old creation time and is not
+caught up. config.json writes `collect` only for fences with rules (schema stays 5); an older NeoFences ignores it.
diff --git a/docs/FEATURES.md b/docs/FEATURES.md
index 04971a0..9ab935e 100644
--- a/docs/FEATURES.md
+++ b/docs/FEATURES.md
@@ -70,6 +70,7 @@ Sources for Fences 6: stardock.com news posts "Now Announcing: Fences 6", "Fence
 | Widgets: clock, date, system stats (CPU, RAM, GPU, C:) | 0.14 (M25) | done | ADR-047; any size; idle while unseen |
 | Folder views: types, files/folders only, newest N, live sort, "+ N more" | 0.11 (M21) | done | ADR-044; Downloads/Screenshots start newest first; panels since 0.15 |
 | Folder panel element: Details / List / Icons, header sort, browse in (Back / Up / Home), Fill fence | 0.15 (M26) | done | ADR-048; folder views migrate to panels |
+| Auto-collect rules: new files of the desktop or a folder become items, by kind or pattern | 0.16 (M27) | done | ADR-049; never moves files; removed stays removed; catch-up at start |
 | Auto-collect rules (the other half of dynamic collections) | later | — | replaces Rules (ADR-040) |
 | Theme packs: "Nanosuit" (Crysis HUD), "Animus" (Assassin's Creed) | v2 | — | |
 | Custom Win11-style compact context menu | v2 | — | |
diff --git a/docs/TEST-CHECKLIST.md b/docs/TEST-CHECKLIST.md
index f4447e2..828d189 100644
--- a/docs/TEST-CHECKLIST.md
+++ b/docs/TEST-CHECKLIST.md
@@ -669,3 +669,19 @@ Added after the M19 final review:
 | AL18 | Click empty space inside a panel → Delete; select the panel by its name row | nothing removed; the panel shows a thin outline when selected (M12) |
 | AL19 | Fence menu → Sort by on a fence its panel fills (a migrated view) | the panel sorts (Date newest first) (M11) |
 | AL20 | A panel taller than its fence: wheel over its rows to their top, then on | the fence scrolls up to the panel's name row |
+
+## AM — 0.16.0 auto-collect rules (M27)
+
+| ID | Steps | Expected |
+|---|---|---|
+| AM1 | Apps → Auto-collect… → New rule (Desktop, Apps and shortcuts + Installers) → OK | "Add these N too?" when desktop entries match that no fence holds; the menu says "(1 rule)" |
+| AM2 | A new shortcut appears on the desktop (install something, or copy a .lnk there) | an item in Apps within a second; the file stays on the desktop |
+| AM3 | Remove that item from Apps; change something on the desktop; restart NeoFences | it does not come back |
+| AM4 | A rule on Downloads → Installers on another fence; download an installer (a .crdownload first) | one item with the final name |
+| AM5 | Two fences' rules on the same folder and kind | the fence higher in the list gets it |
+| AM6 | Exit NeoFences; create a matching file in a watched folder; start NeoFences | the file is collected (catch-up) |
+| AM7 | A game in front (game mode) or Pause; files arrive; leave the game / resume | collected then, not during |
+| AM8 | A rule on the pendrive's `NeoFences-test` folder: Safely Remove; plug back in with a new file | ejects; the new file is collected |
+| AM9 | Copy 300 matching files into a watched folder at once | 200 items; the rest logged |
+| AM10 | Edit a rule's kinds; remove a rule; Cancel the dialog | saved / gone / nothing changed |
+| AM11 | NeoFences' CPU with two rules, idle 30 s | well under 0.1 % |
diff --git a/docs/research/m27-auto-collect.md b/docs/research/m27-auto-collect.md
new file mode 100644
index 0000000..f88e7cb
--- /dev/null
+++ b/docs/research/m27-auto-collect.md
@@ -0,0 +1,29 @@
+# M27 — Auto-collect rules (0.16.0): build notes and results
+
+Spec: `docs/superpowers/specs/2026-10-05-auto-collect-design.md` · Decision: ADR-049 · Plan:
+`docs/superpowers/plans/2026-10-05-m27-auto-collect.md`
+
+## Prototype (2026-10-05, worktree `neo_fences-m27proto`, branch `m27-proto`)
+
+Built end to end before the plan: Core test-first (26 new tests, 632 in all), Shell, App; 0 warnings. Probed live on a
+copy of the user's data (restored afterwards; the PC was unattended, standing go), with a rule on Apps watching the
+probe's own folder `%USERPROFILE%\NeoFences-m27-test` (removed afterwards):
+- the file there before the rule was not collected; `new.url` and `ToolSetup.exe` (an empty file, never run) became items
+  in Apps within a second; `notes.txt` did not (no matching kind);
+- removing `new.url`'s item: it stayed removed, after further changes and after a restart;
+- `while-off.url`, created while NeoFences was closed, was collected at the next start;
+- the dialog showed the rule ("NeoFences-m27-test · Apps and shortcuts, Installers") and "3 items here match now";
+- NeoFences used 0.013 % of the machine over 30 s.
+Probe-script lessons: the first `--exit` right after a start took ~45 s (the switch script now waits up to 90 s, so a
+config edit never lands while the app runs); a test folder must start empty (left-over files are not "new").
+
+### Where the build departs from the spec (the final review weighs them)
+
+- No separate 1 s settle: the folder lister's 250 ms coalescing, and arrivals found by comparing listings — files
+  renamed or moved in while NeoFences runs count too (the spec named created and renamed).
+- A rule's `Watermark` is updated when a listing had arrivals or was the first one (not on every change), so config.json
+  is not rewritten while a file keeps being written.
+- At start, the source's earliest watermark is used for all its rules.
+- A rule whose settings change starts looking again from then (its watermark resets).
+- "Add these N too?" is asked after OK for each new rule, from the listings the dialog made; at most the first 200.
+- The fence menu shows the count ("Auto-collect… (2 rules)") instead of a separate mark.
\ No newline at end of file
````

- [ ] **Step 2: Check.** The secret scan of `git diff main` (the personal email, the private hub link) finds nothing.

- [ ] **Step 3: Commit.** `git add -A && git commit -m "docs: described auto-collect rules in ADR-049, architecture, features, checklist AM and the M27 research note"`

### Task 4: Final review and fix pass

- [ ] Whole-branch review on the most capable model with the spec, this plan, its Review Focus and departures; Critical /
  Important fixed in one pass (test-first where Core can show it; App-only fixes get an AM row); minors deferred in the
  research note.

### Task 5: Live check (asked first unless the user said the PC is unattended)

- [ ] Back up the data and Run value; run the branch build; TEST-CHECKLIST AM by script on the script's own test folder
  (banners; never type into Windows Terminal); screenshots sent; restore; remove the test folder; refocus Terminal;
  results into the research note; commit.
