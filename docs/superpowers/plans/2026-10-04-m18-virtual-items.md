# M18 — Virtual items (0.9.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fences hold virtual items (NeoFences' own records pointing at a file, folder, app, website or special item, with their own name, icon, arguments and note); nothing NeoFences does touches a real file; the Takeover / Inbox / membership / Rules / Portals / item-file-action code is removed.

**Architecture:** Core gets `Items` (the item model, pure edits, target classification, the watch budget, the refresh throttle) and a generic safe JSON store that now backs both `config.json` (schema 5) and the new `items.json` (ADR-041). Shell's drop target only ever creates or moves items (a link effect, never a move); drag-out offers copy/link only; new pickers, a target probe, launch with arguments / run as admin, and Windows' menu behind Shift+right-click with a header line. The App shows `ShownItem`s, owns the item menu, the Properties / Add item… dialog, Locate…, target watching (≤ 64 folders, rename-follow, Missing / Unavailable) and the "Hide desktop icons while NeoFences runs" setting.

**Tech Stack:** .NET 10, WPF (Fluent `ThemeMode` for dialogs), CsWin32 0.3.335, xUnit, Serilog, Velopack (unchanged).

**Spec:** `docs/superpowers/specs/2026-10-04-virtual-items-design.md` (approved 2026-10-04). Decisions: ADR-040, ADR-041. Code map: `docs/research/m18-code-map.md`.

## How this plan is written

Every code change was built and verified in a scratch prototype first (2026-10-04): each task's patches were
replayed on a fresh clone of `main` at `62f4222` — the tests failed before the source patch and passed after it, the
solution built with 0 warnings, 400 Core tests passed at the end. So each code step is **a patch to apply**, not code to
type:

1. Write the patch block **exactly** as shown to a file in the scratchpad (use the Write tool, never a shell heredoc or
   `node -e`: backslashes get mangled). The file ends with one newline.
2. `git apply --whitespace=nowarn <file>` from the worktree root. If it does not apply, stop: something else changed the
   files (rule on it, never hand-merge silently).

Deleted files are `git rm`'d by name before the patch (patches never carry deletions).

## Where the build departs from the spec (decided while prototyping; the final review weighs them)

- **Items inside a renamed folder follow too** when that rename is seen (the folder's parent is watched): spec §4 said
  "parent folder renamed → Missing" to avoid guessing; a rename the watcher reports is not a guess. Unseen renames still
  show Missing → Locate….
- **Drives coming back** are noticed through `WM_DEVICECHANGE` (arrival / removal complete) on the fence windows plus
  the 5-minute check, not through `DeviceRemovalNotice` (that one only lets go of a drive being removed).
- **Deleting a fence with items asks first** (its items would be lost); the spec only said files are untouched.
- **No `DropPlan` class**: the drop rules (same-fence skip, move, Ctrl-duplicate) live in `ItemEdits.Add / Move /
  Duplicate`. Launcher links such as `steam://…` are path-kind targets that are never checked (always OK).
- **A game dragged from the Game Library into a fence** becomes an item pointing at its library shortcut.
- **Session safety:** item lists of unknown fences are only dropped when the config is the user's real one
  (`ConfigLoadResult.KnowsTheFences`), not in a session running on a fresh or read-only fallback.

## Global Constraints

- Hard rule 1 (ADR-040): **NeoFences never modifies, moves, renames or deletes any user file.** Drops answer
  `DROPEFFECT_LINK` (or `COPY` when the source offers no link), **never `MOVE`**; drag-out allows `COPY | LINK` only.
- Hard rule 2: native desktop icons always come back when NeoFences hid them (marker `icons-hidden`, watchdog).
- Hard rules 3–7 unchanged: no injection; Win32/COM only in NeoFences.Shell; CsWin32 bindings (`NativeMethods.txt`);
  **no new NuGet dependency**; shell failures are logged and degrade one feature, never crash.
- Files: `%LOCALAPPDATA%\NeoFences\config.json` (schema **5**), `items.json` (schema **1**,
  `{ "schema": 1, "fences": { "<fenceId>": [ item, … ] } }`), `icons\` (pictures ≤ **256 px**), backups as config.
- Config schema < 5 is not migrated: fresh start, the old file kept as `backups\pre-schema-5-config.json`.
- Limits: at most **64** watched folders; each fence refreshes at most once per **2 s**; full re-check every
  **5 minutes**; network checks time out after **2 s** (= Unavailable).
- UI copy, exactly: menu "Add item…", "Refresh", "Remove from fence", "Properties…", "Locate…", "Open file location",
  "Copy path", "Run as administrator", hint "Shift+right-click: Windows' menu", Windows-menu header
  "Windows menu — acts on the real file", empty-fence hint "Drop files, folders or links here — or right-click → Add
  item…", Settings "Hide desktop icons while NeoFences runs" (off by default).
- Commits: single line, Conventional Commits, past tense, **no Co-Authored-By trailer** (CLAUDE.md overrides the
  harness reminder).
- Version stays `0.8.0` in `NeoFences.App.csproj` until the release step (outside this plan); release is 0.9.0.

## Review Focus

1. **A drop from Explorer, the desktop or another drive never moves or deletes the source** (bug K5's class): the
   effect is negotiated between processes by OLE, so no unit test reaches it — pinned by TEST-CHECKLIST AD2, AD3, AD8
   (Task 7), and by reading `FenceDropTarget.Update` (no `DROPEFFECT_MOVE` anywhere in `ShellDragDrop.cs`).
2. **A config that is only a fallback (fresh, newer, locked) must not drop item lists from items.json** — pinned by
   `ItemStoreTests.OnlyARealConfig_MayDropItemListsOfUnknownFences` (Task 3) and `FenceHost` using
   `ConfigLoadResult.KnowsTheFences`.
3. **"Safely remove" of a drive holding watched targets must be allowed, the items go Unavailable and come back** —
   watchers register removal notices and let go (`ReleaseTargetWatcherForRemoval`): pinned by AD16 (Task 7).
4. **A share that never answers must not freeze the fences** — checks run off the UI thread with a 2 s timeout:
   pinned by AD26 (Task 7).
5. **A power cut between the config save and the items save** leaves at worst an unused list, never a fence without
   its items — config is saved first; a fence without a list is empty and unknown lists are pruned later: pinned by
   `ItemEditsTests.Add_ToAFenceWithoutAList_…`, `ItemEditsTests.RemoveFence_AndPrune_DropLists` (Task 1) and AD25.

---

### Task 0: Claim and worktree

**Files:** `docs/ROADMAP.md` (on `main`)

- [ ] **Step 1: Claim on main.** In `docs/ROADMAP.md`, change the M18 line
  `- [ ] **M18 — Virtual items (0.9.0)**: spec → plan → execute → review → release (open spec questions in PIVOT §Next steps)`
  to
  `- [~] **M18 — Virtual items (0.9.0)** — claimed by session 2026-10-04 m18-virtual-items`.
  Commit: `git commit -am "docs: claimed M18 virtual items"`.
- [ ] **Step 2: Worktree.** `git worktree add ../neo_fences-m18 -b m18-virtual-items` (superpowers:using-git-worktrees);
  all further steps run in `F:\projects\neo_fences-m18`.
- [ ] **Step 3: Baseline.** `dotnet build` → 0 warnings, 0 errors; `dotnet test` → 329 passed.

### Task 1: Core — the virtual item model and its edits

**Files:**
- Create: `src/NeoFences.Core/Items/VirtualItem.cs`, `src/NeoFences.Core/Items/ItemsDocument.cs`
- Test: `tests/NeoFences.Core.Tests/Items/VirtualItemTests.cs`, `tests/NeoFences.Core.Tests/Items/ItemEditsTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces (namespace `NeoFences.Core.Items`): `ItemIcon { string? File; int Index; string? Image }`;
  `enum ItemKind { Path, Website, Special }`; `VirtualItem { required string Id; required string Target; string? Name;
  ItemIcon? Icon; string? Arguments; bool RunAsAdmin; string? Note; ItemKind Kind; string? OwnName;
  static VirtualItem Create(string target); static string NewId() }`; `ItemKinds.{Comparer, Of(string), IsWebsite(string),
  Clean(string?) → string?, TakesArguments(ItemKind, bool isFolder), WebsiteName(string)}`;
  `ItemsDocument { int Schema; IReadOnlyDictionary<string, IReadOnlyList<VirtualItem>> Fences; Of(fenceId); With(fenceId,
  items); Find(itemId); FenceOf(itemId); const CurrentSchemaVersion = 1 }`;
  `ItemsAdded(ItemsDocument Document, IReadOnlyList<string> AddedIds, IReadOnlyList<string> AlreadyThereIds)`;
  `ItemEdits.{Add(doc, fenceId, IReadOnlyList<VirtualItem>, int? insertAt) → ItemsAdded, Move(doc, ids, toFenceId,
  insertAt), Duplicate(doc, ids, toFenceId, insertAt) → (Document, NewIds), Remove(doc, IReadOnlyCollection<string>),
  Replace(doc, item), Retarget(doc, oldPath, newPath), RemoveFence(doc, fenceId), Prune(doc, IReadOnlyCollection<string>
  fenceIds), Reorder(doc, fenceId, orderedIds), PathTargets(doc), ImagesInUse(doc), Repair(doc)}`. Every edit returns the
  **same** document when nothing changed.

- [ ] **Step 1: Write the failing tests** — apply this patch:

````diff
diff --git a/tests/NeoFences.Core.Tests/Items/ItemEditsTests.cs b/tests/NeoFences.Core.Tests/Items/ItemEditsTests.cs
new file mode 100644
index 0000000..f9075a6
--- /dev/null
+++ b/tests/NeoFences.Core.Tests/Items/ItemEditsTests.cs
@@ -0,0 +1,205 @@
+using NeoFences.Core.Items;
+
+namespace NeoFences.Core.Tests.Items;
+
+/// <summary>Adding, moving, duplicating, removing and following items (M18 spec §2, §4). Nothing here touches a file.</summary>
+public class ItemEditsTests
+{
+    private const string Games = "games";
+    private const string Tools = "tools";
+    private const string Desktop = @"C:\Users\cipher\Desktop\";
+
+    private static VirtualItem Item(string id, string target, string? name = null) => new() { Id = id, Target = target, Name = name };
+
+    private static ItemsDocument Sample() => new ItemsDocument()
+        .With(Games, [Item("a", @"D:\Games\a.exe"), Item("b", @"D:\Games\b.exe"), Item("c", @"D:\Games\c.exe")])
+        .With(Tools, [Item("t", @"C:\Tools\t.exe")]);
+
+    private static string[] Ids(ItemsDocument document, string fenceId) => [.. document.Of(fenceId).Select(item => item.Id)];
+
+    // ---------- Add (drops and Add item…) ----------
+
+    [Fact]
+    public void Add_InsertsAtThePosition_AndSkipsATargetTheFenceAlreadyHas_IgnoringCase()
+    {
+        var added = ItemEdits.Add(Sample(), Games, [VirtualItem.Create(@"d:\games\B.EXE"), Item("new", @"D:\Games\new.exe")], insertAt: 1);
+
+        Assert.Equal(["a", "new", "b", "c"], Ids(added.Document, Games));
+        Assert.Equal(["new"], added.AddedIds);
+        Assert.Equal(["b"], added.AlreadyThereIds); // flashes
+    }
+
+    [Fact]
+    public void Add_TheSameTargetMayBeInAnotherFence()
+    {
+        var added = ItemEdits.Add(Sample(), Tools, [Item("copy", @"D:\Games\a.exe")]);
+
+        Assert.Equal(["t", "copy"], Ids(added.Document, Tools));
+        Assert.Empty(added.AlreadyThereIds);
+    }
+
+    [Fact]
+    public void Add_ToAFenceWithoutAList_MakesOne_AtTheEndByDefault_AndIgnoresBlankAndRepeatedTargets()
+    {
+        var added = ItemEdits.Add(new ItemsDocument(), "new-fence",
+            [Item("one", Desktop + "x.txt"), Item("two", Desktop + "X.TXT"), Item("blank", "  ")], insertAt: 99);
+
+        Assert.Equal(["one"], Ids(added.Document, "new-fence"));
+    }
+
+    [Fact]
+    public void Add_NothingNew_ReturnsTheSameDocument()
+    {
+        var document = Sample();
+        Assert.Same(document, ItemEdits.Add(document, Games, [VirtualItem.Create(@"D:\Games\a.exe")]).Document);
+    }
+
+    // ---------- Move and Duplicate (drag between and inside fences) ----------
+
+    [Fact]
+    public void Move_ToAnotherFence_KeepsEveryField()
+    {
+        var document = ItemEdits.Replace(Sample(), Item("b", @"D:\Games\b.exe", name: "Bee"));
+
+        var moved = ItemEdits.Move(document, ["b"], Tools, insertAt: 0);
+
+        Assert.Equal(["a", "c"], Ids(moved, Games));
+        Assert.Equal(["b", "t"], Ids(moved, Tools));
+        Assert.Equal("Bee", moved.Find("b")!.Name);
+    }
+
+    [Fact]
+    public void Move_InsideTheFence_CountsTheDraggedItemsInTheShownPosition()
+    {
+        // a b c shown; a dragged to after c (insert index 3, as displayed with a still there).
+        Assert.Equal(["b", "c", "a"], Ids(ItemEdits.Move(Sample(), ["a"], Games, insertAt: 3), Games));
+        // c dragged before a (index 0).
+        Assert.Equal(["c", "a", "b"], Ids(ItemEdits.Move(Sample(), ["c"], Games, insertAt: 0), Games));
+        // a and c dragged between b and c (index 2): they keep their own order.
+        Assert.Equal(["b", "a", "c"], Ids(ItemEdits.Move(Sample(), ["c", "a"], Games, insertAt: 2), Games));
+    }
+
+    [Fact]
+    public void Move_UnknownIds_ChangeNothing()
+    {
+        var document = Sample();
+        Assert.Same(document, ItemEdits.Move(document, ["nope"], Tools, insertAt: 0));
+    }
+
+    [Fact]
+    public void Duplicate_MakesCopiesWithNewIds_AlsoInTheSameFence()
+    {
+        var (duplicated, newIds) = ItemEdits.Duplicate(Sample(), ["a"], Games, insertAt: 1);
+
+        var copyId = Assert.Single(newIds);
+        Assert.NotEqual("a", copyId);
+        Assert.Equal(["a", copyId, "b", "c"], Ids(duplicated, Games));
+        Assert.Equal(@"D:\Games\a.exe", duplicated.Find(copyId)!.Target);
+    }
+
+    // ---------- Remove, Replace, RemoveFence, Prune, Reorder ----------
+
+    [Fact]
+    public void Remove_TakesTheItemsOutOfEveryFence()
+    {
+        var removed = ItemEdits.Remove(Sample(), ["a", "t"]);
+
+        Assert.Equal(["b", "c"], Ids(removed, Games));
+        Assert.Empty(removed.Of(Tools));
+        var document = Sample();
+        Assert.Same(document, ItemEdits.Remove(document, ["nope"]));
+    }
+
+    [Fact]
+    public void Replace_ChangesOnlyThatItem()
+    {
+        var replaced = ItemEdits.Replace(Sample(), Item("b", @"E:\Moved\b.exe", name: "B") with { Arguments = "-w", RunAsAdmin = true, Note = "n" });
+
+        Assert.Equal(["a", "b", "c"], Ids(replaced, Games));
+        var item = replaced.Find("b")!;
+        Assert.Equal((@"E:\Moved\b.exe", "B", "-w", true, "n"), (item.Target, item.Name, item.Arguments, item.RunAsAdmin, item.Note));
+    }
+
+    [Fact]
+    public void RemoveFence_AndPrune_DropLists()
+    {
+        Assert.Empty(ItemEdits.RemoveFence(Sample(), Tools).Of(Tools));
+        var document = Sample();
+        Assert.Same(document, ItemEdits.Prune(document, [Games, Tools, "other"]));
+        Assert.Equal([Games], ItemEdits.Prune(document, [Games]).Fences.Keys);
+    }
+
+    [Fact]
+    public void Reorder_NeedsExactlyTheFencesItems()
+    {
+        Assert.Equal(["c", "a", "b"], Ids(ItemEdits.Reorder(Sample(), Games, ["c", "a", "b"]), Games));
+        Assert.Throws<ArgumentException>(() => ItemEdits.Reorder(Sample(), Games, ["c", "a"]));
+        Assert.Throws<ArgumentException>(() => ItemEdits.Reorder(Sample(), Games, ["c", "a", "a"]));
+    }
+
+    // ---------- Retarget (a watched target renamed in place) ----------
+
+    [Fact]
+    public void Retarget_FollowsARename_InEveryFence_AndInsideARenamedFolder()
+    {
+        var document = new ItemsDocument()
+            .With(Games, [Item("g1", @"D:\Games\Hades"), Item("g2", @"D:\Games\Hades\Hades.exe", name: "Hades"), Item("g3", @"D:\Games\Hades 2\x.exe")])
+            .With(Tools, [Item("t1", @"d:\games\hades"), Item("web", "https://hades.example")]);
+
+        var followed = ItemEdits.Retarget(document, @"D:\Games\Hades", @"D:\Games\Hades II");
+
+        Assert.Equal(@"D:\Games\Hades II", followed.Find("g1")!.Target);
+        Assert.Equal(@"D:\Games\Hades II\Hades.exe", followed.Find("g2")!.Target);
+        Assert.Equal("Hades", followed.Find("g2")!.Name); // its own name stays
+        Assert.Equal(@"D:\Games\Hades 2\x.exe", followed.Find("g3")!.Target); // a sibling with a longer name is not inside it
+        Assert.Equal(@"D:\Games\Hades II", followed.Find("t1")!.Target);
+        Assert.Equal("https://hades.example", followed.Find("web")!.Target);
+        Assert.Same(document, ItemEdits.Retarget(document, @"D:\Other", @"D:\Else"));
+    }
+
+    // ---------- watching inputs, pictures, repair ----------
+
+    [Fact]
+    public void PathTargets_AreFilesAndFolders_Once_InFenceOrder()
+    {
+        var document = Sample().With("web", [Item("w", "https://x.y"), Item("s", "::{645FF040-5081-101B-9F08-00AA002F954E}"), Item("dup", @"d:\games\A.exe")]);
+
+        Assert.Equal([@"D:\Games\a.exe", @"D:\Games\b.exe", @"D:\Games\c.exe", @"C:\Tools\t.exe"], ItemEdits.PathTargets(document));
+    }
+
+    [Fact]
+    public void ImagesInUse_ListsTheCopiedPictures()
+    {
+        var document = new ItemsDocument().With(Games,
+        [
+            Item("a", @"D:\a.exe") with { Icon = new ItemIcon { Image = "a.png" } },
+            Item("b", @"D:\b.exe") with { Icon = new ItemIcon { File = @"C:\Windows\System32\shell32.dll", Index = 4 } },
+        ]);
+
+        Assert.Equal(["a.png"], ItemEdits.ImagesInUse(document));
+    }
+
+    [Fact]
+    public void Repair_DropsBrokenEntries_AndGivesCopiedIdsNewOnes()
+    {
+        var document = new ItemsDocument
+        {
+            Fences = new Dictionary<string, IReadOnlyList<VirtualItem>>
+            {
+                [Games] = [Item("same", @"D:\a.exe"), null!, Item("same", @"D:\b.exe"), Item("", " "), Item("x", @" D:\c.exe ") with { Icon = new ItemIcon() }],
+                [""] = [Item("lost", @"D:\d.exe")],
+                [Tools] = null!,
+            },
+        };
+
+        var repaired = ItemEdits.Repair(document);
+
+        var games = repaired.Of(Games);
+        Assert.Equal([@"D:\a.exe", @"D:\b.exe", @"D:\c.exe"], games.Select(item => item.Target));
+        Assert.Equal("same", games[0].Id);
+        Assert.NotEqual("same", games[1].Id);
+        Assert.Null(games[2].Icon); // an icon with neither a file nor a picture is none
+        Assert.Empty(repaired.Of(Tools));
+        Assert.False(repaired.Fences.ContainsKey(""));
+    }
+}
diff --git a/tests/NeoFences.Core.Tests/Items/VirtualItemTests.cs b/tests/NeoFences.Core.Tests/Items/VirtualItemTests.cs
new file mode 100644
index 0000000..63b3267
--- /dev/null
+++ b/tests/NeoFences.Core.Tests/Items/VirtualItemTests.cs
@@ -0,0 +1,53 @@
+using NeoFences.Core.Items;
+
+namespace NeoFences.Core.Tests.Items;
+
+/// <summary>Virtual items (M18, spec 2026-10-04-virtual-items-design §1): kinds, typed targets, names.</summary>
+public class VirtualItemTests
+{
+    [Theory]
+    [InlineData(@"D:\Games\Hades\Hades.exe", ItemKind.Path)]
+    [InlineData(@"C:\Users\cipher\Desktop\notes", ItemKind.Path)]
+    [InlineData(@"\\nas\media\Movies", ItemKind.Path)]
+    [InlineData("::{645FF040-5081-101B-9F08-00AA002F954E}", ItemKind.Special)]
+    [InlineData("shell:AppsFolder", ItemKind.Special)]
+    [InlineData("https://github.com/", ItemKind.Website)]
+    [InlineData("HTTP://example.com/a?b=c", ItemKind.Website)]
+    [InlineData("steam://rungameid/570", ItemKind.Path)] // a launcher link is not a website: it opens through Windows like a file
+    [InlineData("ftp://example.com", ItemKind.Path)]
+    public void Kind_FollowsTheTarget(string target, ItemKind expected) => Assert.Equal(expected, ItemKinds.Of(target));
+
+    [Theory]
+    [InlineData(@"  ""D:\Games\Hades\Hades.exe""  ", @"D:\Games\Hades\Hades.exe")] // Explorer's "Copy as path"
+    [InlineData("www.github.com", "https://www.github.com")]
+    [InlineData(" https://a.b/ ", "https://a.b/")]
+    [InlineData("\"\"", null)]
+    [InlineData("   ", null)]
+    [InlineData(null, null)]
+    public void Clean_MakesATypedTargetUsable(string? typed, string? expected) => Assert.Equal(expected, ItemKinds.Clean(typed));
+
+    [Fact]
+    public void Arguments_ApplyToFilesAndApps_Only()
+    {
+        Assert.True(ItemKinds.TakesArguments(ItemKind.Path, isFolder: false));
+        Assert.False(ItemKinds.TakesArguments(ItemKind.Path, isFolder: true));
+        Assert.False(ItemKinds.TakesArguments(ItemKind.Website, isFolder: false));
+        Assert.False(ItemKinds.TakesArguments(ItemKind.Special, isFolder: false));
+    }
+
+    [Theory]
+    [InlineData("https://www.github.com/x", "github.com")]
+    [InlineData("https://docs.microsoft.com", "docs.microsoft.com")]
+    [InlineData("not a url", "not a url")]
+    public void WebsiteName_IsTheHost(string url, string expected) => Assert.Equal(expected, ItemKinds.WebsiteName(url));
+
+    [Fact]
+    public void OwnName_IsNullWhenBlank_SoWindowsNameShows()
+    {
+        var item = VirtualItem.Create(@"D:\x.exe");
+        Assert.Null(item.OwnName);
+        Assert.Null((item with { Name = "  " }).OwnName);
+        Assert.Equal("Mine", (item with { Name = "Mine" }).OwnName);
+        Assert.NotEqual(item.Id, VirtualItem.Create(@"D:\x.exe").Id); // the same target, two items
+    }
+}
````

- [ ] **Step 2: Run them to see them fail.**
  Run: `dotnet test tests/NeoFences.Core.Tests`
  Expected: build FAILS with `error CS0234: The type or namespace name 'Items' does not exist in the namespace 'NeoFences.Core'`
  (and CS0103 `'ItemKind' does not exist`, CS0246 `'ItemsDocument' could not be found`).

- [ ] **Step 3: Implement** — apply this patch:

````diff
diff --git a/src/NeoFences.Core/Items/ItemsDocument.cs b/src/NeoFences.Core/Items/ItemsDocument.cs
new file mode 100644
index 0000000..753ae4b
--- /dev/null
+++ b/src/NeoFences.Core/Items/ItemsDocument.cs
@@ -0,0 +1,188 @@
+namespace NeoFences.Core.Items;
+
+/// <summary>
+/// Every fence's virtual items, stored as <c>items.json</c> next to <c>config.json</c> (ADR-041): a list per fence id.
+/// Each tab is a fence of its own, so a box's tabs have a list each.
+/// </summary>
+public sealed record ItemsDocument
+{
+    /// <summary>An older NeoFences reads a newer number as read-only and never saves over it.</summary>
+    public const int CurrentSchemaVersion = 1;
+
+    public int Schema { get; init; } = CurrentSchemaVersion;
+
+    public IReadOnlyDictionary<string, IReadOnlyList<VirtualItem>> Fences { get; init; } = new Dictionary<string, IReadOnlyList<VirtualItem>>();
+
+    /// <summary>A fence's items in order; a fence with no list is empty.</summary>
+    public IReadOnlyList<VirtualItem> Of(string fenceId) => Fences.TryGetValue(fenceId, out var items) ? items : [];
+
+    public ItemsDocument With(string fenceId, IReadOnlyList<VirtualItem> items) =>
+        this with { Fences = new Dictionary<string, IReadOnlyList<VirtualItem>>(Fences) { [fenceId] = items } };
+
+    public VirtualItem? Find(string itemId) => Fences.Values.SelectMany(items => items).FirstOrDefault(item => item.Id == itemId);
+
+    /// <summary>The fence holding this item, or null.</summary>
+    public string? FenceOf(string itemId) => Fences.FirstOrDefault(entry => entry.Value.Any(item => item.Id == itemId)).Key;
+}
+
+/// <summary>What <see cref="ItemEdits.Add"/> did: the new items' ids, and the ids of items the fence already had for a target.</summary>
+public sealed record ItemsAdded(ItemsDocument Document, IReadOnlyList<string> AddedIds, IReadOnlyList<string> AlreadyThereIds);
+
+/// <summary>
+/// Changes to the virtual items (spec §2, §4). Pure: each returns a new document (the same one when nothing changed).
+/// None of them touches a file.
+/// </summary>
+public static class ItemEdits
+{
+    /// <summary>
+    /// Adds items to a fence at <paramref name="insertAt"/> (null or past the end: at the end). A target the fence already
+    /// holds is not added again (its existing item is reported, so it can flash); other fences may hold it too.
+    /// </summary>
+    public static ItemsAdded Add(ItemsDocument document, string fenceId, IReadOnlyList<VirtualItem> newItems, int? insertAt = null)
+    {
+        var items = document.Of(fenceId).ToList();
+        var added = new List<VirtualItem>();
+        var alreadyThere = new List<string>();
+        foreach (var newItem in newItems.Where(candidate => !string.IsNullOrWhiteSpace(candidate.Target)))
+        {
+            if (items.FirstOrDefault(item => ItemKinds.Comparer.Equals(item.Target, newItem.Target)) is { } existing)
+            {
+                if (!alreadyThere.Contains(existing.Id)) alreadyThere.Add(existing.Id);
+                continue;
+            }
+            if (added.Any(item => ItemKinds.Comparer.Equals(item.Target, newItem.Target))) continue; // the same target twice in one drop
+            added.Add(newItem);
+        }
+        if (added.Count == 0) return new ItemsAdded(document, [], alreadyThere);
+        items.InsertRange(Math.Clamp(insertAt ?? items.Count, 0, items.Count), added);
+        return new ItemsAdded(document.With(fenceId, items), [.. added.Select(item => item.Id)], alreadyThere);
+    }
+
+    /// <summary>
+    /// Drag-drop between or inside fences (spec §2): the items move, in the order they had, to <paramref name="toFenceId"/>
+    /// before the item shown at <paramref name="insertAt"/> during the drag (the dragged items still included). Unknown ids
+    /// are ignored. Every field stays.
+    /// </summary>
+    public static ItemsDocument Move(ItemsDocument document, IReadOnlyList<string> itemIds, string toFenceId, int insertAt)
+    {
+        var moving = itemIds.ToHashSet(StringComparer.Ordinal);
+        var ordered = document.Fences.Values.SelectMany(items => items).Where(item => moving.Contains(item.Id)).ToList();
+        if (ordered.Count == 0) return document;
+        var target = document.Of(toFenceId);
+        var displayedIndex = Math.Clamp(insertAt, 0, target.Count);
+        var movingBeforeDrop = target.Take(displayedIndex).Count(item => moving.Contains(item.Id));
+        var without = document with
+        {
+            Fences = document.Fences.ToDictionary(entry => entry.Key,
+                entry => (IReadOnlyList<VirtualItem>)entry.Value.Where(item => !moving.Contains(item.Id)).ToList()),
+        };
+        var items = without.Of(toFenceId).ToList();
+        items.InsertRange(displayedIndex - movingBeforeDrop, ordered);
+        return without.With(toFenceId, items);
+    }
+
+    /// <summary>Ctrl+drag (spec §2): copies with new ids at <paramref name="insertAt"/>, any fence, the same one too.</summary>
+    public static (ItemsDocument Document, IReadOnlyList<string> NewIds) Duplicate(ItemsDocument document, IReadOnlyList<string> itemIds,
+        string toFenceId, int insertAt)
+    {
+        var copying = itemIds.ToHashSet(StringComparer.Ordinal);
+        var copies = document.Fences.Values.SelectMany(items => items).Where(item => copying.Contains(item.Id))
+            .Select(item => item with { Id = VirtualItem.NewId() }).ToList();
+        if (copies.Count == 0) return (document, []);
+        var items = document.Of(toFenceId).ToList();
+        items.InsertRange(Math.Clamp(insertAt, 0, items.Count), copies);
+        return (document.With(toFenceId, items), [.. copies.Select(copy => copy.Id)]);
+    }
+
+    /// <summary>Del / "Remove from fence": the items go; their targets are never touched.</summary>
+    public static ItemsDocument Remove(ItemsDocument document, IReadOnlyCollection<string> itemIds)
+    {
+        if (!document.Fences.Values.Any(items => items.Any(item => itemIds.Contains(item.Id)))) return document;
+        return document with
+        {
+            Fences = document.Fences.ToDictionary(entry => entry.Key,
+                entry => (IReadOnlyList<VirtualItem>)entry.Value.Where(item => !itemIds.Contains(item.Id)).ToList()),
+        };
+    }
+
+    /// <summary>Properties → OK, or Locate…: the item with the same id takes these fields. Unknown id: unchanged.</summary>
+    public static ItemsDocument Replace(ItemsDocument document, VirtualItem updated)
+    {
+        if (document.FenceOf(updated.Id) is not { } fenceId) return document;
+        return document.With(fenceId, [.. document.Of(fenceId).Select(item => item.Id == updated.Id ? updated : item)]);
+    }
+
+    /// <summary>
+    /// A watched file or folder was renamed in place (spec §4): every item pointing at it, in every fence, follows, and so
+    /// does every item pointing inside a renamed folder. Names, icons and notes stay.
+    /// </summary>
+    public static ItemsDocument Retarget(ItemsDocument document, string oldPath, string newPath)
+    {
+        var oldPrefix = oldPath.TrimEnd('\\') + "\\";
+        string? Follow(string target) =>
+            ItemKinds.Comparer.Equals(target, oldPath) ? newPath
+            : target.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase) ? newPath.TrimEnd('\\') + "\\" + target[oldPrefix.Length..]
+            : null;
+        if (!document.Fences.Values.Any(items => items.Any(item => item.Kind == ItemKind.Path && Follow(item.Target) is not null))) return document;
+        return document with
+        {
+            Fences = document.Fences.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<VirtualItem>)entry.Value
+                .Select(item => item.Kind == ItemKind.Path && Follow(item.Target) is { } followed ? item with { Target = followed } : item).ToList()),
+        };
+    }
+
+    /// <summary>Delete fence: its list goes (the targets are never touched).</summary>
+    public static ItemsDocument RemoveFence(ItemsDocument document, string fenceId) =>
+        !document.Fences.ContainsKey(fenceId) ? document
+            : document with { Fences = document.Fences.Where(entry => entry.Key != fenceId).ToDictionary(entry => entry.Key, entry => entry.Value) };
+
+    /// <summary>
+    /// Before each save (ADR-041): lists of fences the config no longer has go. After a power cut between the two saves
+    /// they were kept until now; a fence without a list is simply empty.
+    /// </summary>
+    public static ItemsDocument Prune(ItemsDocument document, IReadOnlyCollection<string> fenceIds) =>
+        document.Fences.Keys.All(fenceIds.Contains) ? document
+            : document with { Fences = document.Fences.Where(entry => fenceIds.Contains(entry.Key)).ToDictionary(entry => entry.Key, entry => entry.Value) };
+
+    /// <summary>"Sort by" (one time): the new order must hold exactly the fence's items.</summary>
+    /// <exception cref="ArgumentException">Not a permutation of the fence's item ids.</exception>
+    public static ItemsDocument Reorder(ItemsDocument document, string fenceId, IReadOnlyList<string> orderedIds)
+    {
+        var items = document.Of(fenceId).ToDictionary(item => item.Id, StringComparer.Ordinal);
+        if (orderedIds.Count != items.Count || orderedIds.Distinct(StringComparer.Ordinal).Count() != items.Count || !orderedIds.All(items.ContainsKey))
+            throw new ArgumentException("The new order must contain exactly the fence's items.", nameof(orderedIds));
+        return document.With(fenceId, [.. orderedIds.Select(id => items[id])]);
+    }
+
+    /// <summary>Every file and folder target once, in fence order (for watching and checks).</summary>
+    public static IReadOnlyList<string> PathTargets(ItemsDocument document) =>
+        document.Fences.Values.SelectMany(items => items).Where(item => item.Kind == ItemKind.Path)
+            .Select(item => item.Target).Distinct(ItemKinds.Comparer).ToList();
+
+    /// <summary>Picture files in <c>icons\</c> some item still uses (the others are deleted at start).</summary>
+    public static IReadOnlySet<string> ImagesInUse(ItemsDocument document) =>
+        document.Fences.Values.SelectMany(items => items).Select(item => item.Icon?.Image).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
+
+    /// <summary>
+    /// Load-time repair of a (possibly hand-edited) file: no null lists or items, no blank targets, every id unique (a copied
+    /// block gets new ids, or one edit would hit both), and an icon with neither a file nor an image is none.
+    /// </summary>
+    public static ItemsDocument Repair(ItemsDocument document)
+    {
+        var seenIds = new HashSet<string>(StringComparer.Ordinal);
+        var fences = new Dictionary<string, IReadOnlyList<VirtualItem>>();
+        foreach (var (fenceId, items) in document.Fences ?? new Dictionary<string, IReadOnlyList<VirtualItem>>())
+        {
+            if (string.IsNullOrWhiteSpace(fenceId)) continue;
+            fences[fenceId] = (items ?? []).Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Target))
+                .Select(item => item with
+                {
+                    Id = string.IsNullOrWhiteSpace(item.Id) || !seenIds.Add(item.Id) ? VirtualItem.NewId() : item.Id,
+                    Target = item.Target.Trim(),
+                    Icon = item.Icon is { File: null or "", Image: null or "" } ? null : item.Icon,
+                })
+                .ToList();
+        }
+        return new ItemsDocument { Fences = fences };
+    }
+}
diff --git a/src/NeoFences.Core/Items/VirtualItem.cs b/src/NeoFences.Core/Items/VirtualItem.cs
new file mode 100644
index 0000000..1f26893
--- /dev/null
+++ b/src/NeoFences.Core/Items/VirtualItem.cs
@@ -0,0 +1,92 @@
+using System.Text.Json.Serialization;
+
+namespace NeoFences.Core.Items;
+
+/// <summary>An item's own icon (M18): an icon in a file (Windows' icon picker), or a picture copied into NeoFences' data.</summary>
+public sealed record ItemIcon
+{
+    /// <summary>An .ico, .exe or .dll; <see cref="Index"/> is the icon's place in it.</summary>
+    public string? File { get; init; }
+
+    public int Index { get; init; }
+
+    /// <summary>A picture's file name in NeoFences' <c>icons\</c> folder (PNG, copied in when chosen).</summary>
+    public string? Image { get; init; }
+}
+
+/// <summary>What an item points at. A path may be a file or a folder: that is known only when it is checked.</summary>
+public enum ItemKind { Path, Website, Special }
+
+/// <summary>
+/// A virtual item (ADR-040, spec 2026-10-04-virtual-items-design §1): NeoFences' own record of a target with its own
+/// name, icon, arguments and note. The same target may be in many items; nothing NeoFences does touches the target.
+/// </summary>
+public sealed record VirtualItem
+{
+    public required string Id { get; init; }
+
+    /// <summary>A file or folder path, a <c>::{GUID}</c> or <c>shell:</c> special item, or an http(s) URL.</summary>
+    public required string Target { get; init; }
+
+    /// <summary>Null or empty: Windows' display name for the target.</summary>
+    public string? Name { get; init; }
+
+    /// <summary>Null: the target's icon.</summary>
+    public ItemIcon? Icon { get; init; }
+
+    /// <summary>Files and apps only.</summary>
+    public string? Arguments { get; init; }
+
+    /// <summary>Files and apps only.</summary>
+    public bool RunAsAdmin { get; init; }
+
+    /// <summary>Shown as the item's tooltip.</summary>
+    public string? Note { get; init; }
+
+    [JsonIgnore]
+    public ItemKind Kind => ItemKinds.Of(Target);
+
+    /// <summary>The name the user gave it, or null when Windows' name is shown.</summary>
+    [JsonIgnore]
+    public string? OwnName => string.IsNullOrWhiteSpace(Name) ? null : Name;
+
+    public static VirtualItem Create(string target) => new() { Id = NewId(), Target = target };
+
+    public static string NewId() => Guid.NewGuid().ToString("N");
+}
+
+/// <summary>Target kinds, and what the Add item… box accepts.</summary>
+public static class ItemKinds
+{
+    /// <summary>Targets compare ignoring case: Windows paths do, and a URL's host does.</summary>
+    public static StringComparer Comparer { get; } = StringComparer.OrdinalIgnoreCase;
+
+    public static ItemKind Of(string target) =>
+        target.StartsWith("::", StringComparison.Ordinal) || target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ? ItemKind.Special
+        : IsWebsite(target) ? ItemKind.Website
+        : ItemKind.Path;
+
+    public static bool IsWebsite(string text) =>
+        Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
+
+    /// <summary>
+    /// A target as typed or pasted: trimmed, without the quotes of Explorer's "Copy as path", and "www.…" made a website.
+    /// Null when nothing is left.
+    /// </summary>
+    public static string? Clean(string? typed)
+    {
+        var text = (typed ?? "").Trim();
+        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"') text = text[1..^1].Trim();
+        if (text.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) text = "https://" + text;
+        return text.Length == 0 ? null : text;
+    }
+
+    /// <summary>Arguments and "Run as administrator" apply to files and apps, not folders, websites or special items.</summary>
+    public static bool TakesArguments(ItemKind kind, bool isFolder) => kind == ItemKind.Path && !isFolder;
+
+    /// <summary>A website's name before the user gives it one: its host without "www." ("github.com").</summary>
+    public static string WebsiteName(string url) =>
+        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Length > 0
+            ? (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host)
+            : url;
+}
````

- [ ] **Step 4: Run the tests.**
  Run: `dotnet test tests/NeoFences.Core.Tests`
  Expected: `Passed! - Failed: 0, Passed: 474`.

- [ ] **Step 5: Commit.**
  `git add src/NeoFences.Core/Items tests/NeoFences.Core.Tests/Items && git commit -m "feat: added virtual items and their edits to Core"`

### Task 2: Core — target states, the watch plan, the refresh throttle

**Files:**
- Create: `src/NeoFences.Core/Items/Watching.cs`
- Test: `tests/NeoFences.Core.Tests/Items/WatchingTests.cs`

**Interfaces:**
- Consumes: `ItemKinds.Of`, `ItemKinds.Comparer` (Task 1).
- Produces: `enum TargetState { Ok, Missing, Unavailable }`; `TargetCheck(TargetState State, bool IsFolder = false) { static Ok }`;
  `TargetChecks.{Classify(target, Func<string,bool> fileExists, Func<string,bool> folderExists), RootOf(path) → string?,
  IsNetworkPath(path)}` (a path without a drive or share root — `steam://…` — is always Ok); `WatchPlan.{MaxFolders = 64,
  Folders(IEnumerable<string> pathTargets, int maxFolders = 64), ParentOf(path) → string?}`;
  `RefreshThrottle.{static Interval = 2 s, DelayFor(fenceId, DateTimeOffset now) → TimeSpan, Refreshed(fenceId, now)}`.

- [ ] **Step 1: Write the failing tests** — apply this patch:

````diff
diff --git a/tests/NeoFences.Core.Tests/Items/WatchingTests.cs b/tests/NeoFences.Core.Tests/Items/WatchingTests.cs
new file mode 100644
index 0000000..b181b44
--- /dev/null
+++ b/tests/NeoFences.Core.Tests/Items/WatchingTests.cs
@@ -0,0 +1,89 @@
+using NeoFences.Core.Items;
+
+namespace NeoFences.Core.Tests.Items;
+
+/// <summary>Target states, the watch budget and the refresh throttle (M18 spec §4).</summary>
+public class WatchingTests
+{
+    // ---------- TargetChecks.Classify ----------
+
+    private static TargetCheck Classify(string target, string[] files, string[] folders) =>
+        TargetChecks.Classify(target,
+            fileExists: path => files.Contains(path, StringComparer.OrdinalIgnoreCase),
+            folderExists: path => folders.Contains(path, StringComparer.OrdinalIgnoreCase));
+
+    [Fact]
+    public void Classify_AFileThatExists_IsOk_AFolderSaysSo()
+    {
+        Assert.Equal(TargetCheck.Ok, Classify(@"D:\Games\a.exe", files: [@"D:\Games\a.exe"], folders: [@"D:\"]));
+        Assert.Equal(new TargetCheck(TargetState.Ok, IsFolder: true), Classify(@"D:\Games", files: [], folders: [@"D:\Games", @"D:\"]));
+    }
+
+    [Fact]
+    public void Classify_Gone_WithItsDriveThere_IsMissing_WithoutItsDrive_IsUnavailable()
+    {
+        Assert.Equal(TargetState.Missing, Classify(@"D:\Games\a.exe", files: [], folders: [@"D:\"]).State);
+        Assert.Equal(TargetState.Unavailable, Classify(@"G:\NeoFences-test\a.txt", files: [], folders: [@"D:\"]).State);
+        Assert.Equal(TargetState.Unavailable, Classify(@"\\nas\media\Movies", files: [], folders: []).State);
+        Assert.Equal(TargetState.Missing, Classify(@"\\nas\media\Movies", files: [], folders: [@"\\nas\media\"]).State);
+    }
+
+    [Fact]
+    public void Classify_WebsitesAndSpecialItems_AreAlwaysOk_WithoutAsking()
+    {
+        Func<string, bool> never = _ => throw new InvalidOperationException("no disk access for these");
+        Assert.Equal(TargetCheck.Ok, TargetChecks.Classify("https://github.com", never, never));
+        Assert.Equal(TargetCheck.Ok, TargetChecks.Classify("::{645FF040-5081-101B-9F08-00AA002F954E}", never, never));
+        Assert.Equal(TargetCheck.Ok, TargetChecks.Classify("steam://rungameid/570", never, never)); // a launcher link: Windows opens it
+    }
+
+    [Theory]
+    [InlineData(@"D:\Games\a.exe", @"D:\")]
+    [InlineData(@"d:\", @"d:\")]
+    [InlineData(@"\\nas\media\Movies\x.mkv", @"\\nas\media\")]
+    [InlineData(@"\\nas", null)]
+    [InlineData("relative.txt", null)]
+    public void RootOf(string path, string? expected) => Assert.Equal(expected, TargetChecks.RootOf(path));
+
+    // ---------- WatchPlan ----------
+
+    [Theory]
+    [InlineData(@"D:\Games\a.exe", @"D:\Games")]
+    [InlineData(@"D:\Games\Hades\", @"D:\Games")]
+    [InlineData(@"D:\a.exe", @"D:\")]
+    [InlineData(@"D:\", null)]
+    [InlineData(@"\\nas\media\x.mkv", @"\\nas\media")]
+    [InlineData(@"\\nas\media", null)]
+    public void ParentOf(string path, string? expected) => Assert.Equal(expected, WatchPlan.ParentOf(path));
+
+    [Fact]
+    public void Folders_TheBusiestFirst_TiesInOrder_AtMostTheBudget()
+    {
+        string[] targets = [@"C:\Tools\t.exe", @"D:\Games\a.exe", @"D:\Games\b.exe", @"d:\games\c.exe", @"E:\Docs\x.pdf", @"C:\Tools\u.exe", @"F:\"];
+
+        Assert.Equal([@"D:\Games", @"C:\Tools", @"E:\Docs"], WatchPlan.Folders(targets));
+        Assert.Equal([@"D:\Games", @"C:\Tools"], WatchPlan.Folders(targets, maxFolders: 2));
+    }
+
+    [Fact]
+    public void Folders_SeventyFolders_WatchSixtyFour()
+    {
+        var targets = Enumerable.Range(0, 70).Select(index => $@"D:\Folder{index}\file.txt");
+        Assert.Equal(WatchPlan.MaxFolders, WatchPlan.Folders(targets).Count);
+    }
+
+    // ---------- RefreshThrottle ----------
+
+    [Fact]
+    public void Throttle_OnePerFenceEveryTwoSeconds()
+    {
+        var throttle = new RefreshThrottle();
+        var start = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
+
+        Assert.Equal(TimeSpan.Zero, throttle.DelayFor("games", start)); // never refreshed: now
+        throttle.Refreshed("games", start);
+        Assert.Equal(TimeSpan.FromMilliseconds(1500), throttle.DelayFor("games", start.AddMilliseconds(500)));
+        Assert.Equal(TimeSpan.Zero, throttle.DelayFor("tools", start.AddMilliseconds(500))); // per fence
+        Assert.Equal(TimeSpan.Zero, throttle.DelayFor("games", start.AddSeconds(2)));
+    }
+}
````

- [ ] **Step 2: Run them to see them fail.**
  Run: `dotnet test tests/NeoFences.Core.Tests`
  Expected: build FAILS with `error CS0246: The type or namespace name 'TargetCheck' could not be found`.

- [ ] **Step 3: Implement** — apply this patch:

````diff
diff --git a/src/NeoFences.Core/Items/Watching.cs b/src/NeoFences.Core/Items/Watching.cs
new file mode 100644
index 0000000..d196d51
--- /dev/null
+++ b/src/NeoFences.Core/Items/Watching.cs
@@ -0,0 +1,84 @@
+namespace NeoFences.Core.Items;
+
+/// <summary>Where an item's target is now (spec §4). Websites and special items are always <see cref="Ok"/>.</summary>
+public enum TargetState { Ok, Missing, Unavailable }
+
+/// <param name="IsFolder">The target is a folder (Arguments and "Run as administrator" do not apply).</param>
+public sealed record TargetCheck(TargetState State, bool IsFolder = false)
+{
+    public static TargetCheck Ok { get; } = new(TargetState.Ok);
+}
+
+/// <summary>Target states from what the disk says (the probing, with its timeout, is NeoFences.Shell's).</summary>
+public static class TargetChecks
+{
+    /// <summary>
+    /// A path that exists is Ok; one whose drive or share root does not answer is Unavailable (a USB stick not plugged
+    /// in, a sleeping NAS), otherwise Missing (deleted, moved away, or its folder renamed).
+    /// </summary>
+    /// <param name="fileExists">True when a file is at the path.</param>
+    /// <param name="folderExists">True when a folder is at the path (also asked for the root).</param>
+    public static TargetCheck Classify(string target, Func<string, bool> fileExists, Func<string, bool> folderExists)
+    {
+        if (ItemKinds.Of(target) != ItemKind.Path) return TargetCheck.Ok;
+        // A launcher link (steam://rungameid/570) or a relative path: nothing on a disk to check, Windows opens it.
+        if (RootOf(target) is not { } root) return TargetCheck.Ok;
+        if (folderExists(target)) return new TargetCheck(TargetState.Ok, IsFolder: true);
+        if (fileExists(target)) return TargetCheck.Ok;
+        return folderExists(root) ? new TargetCheck(TargetState.Missing) : new TargetCheck(TargetState.Unavailable);
+    }
+
+    /// <summary>"D:\" for D:\Games\x.exe, "\\nas\share\" for a share; null for anything else (a relative path).</summary>
+    public static string? RootOf(string path)
+    {
+        if (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\') return path[..3];
+        if (!path.StartsWith(@"\\", StringComparison.Ordinal)) return null;
+        var parts = path[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
+        return parts.Length >= 2 ? $@"\\{parts[0]}\{parts[1]}\" : null;
+    }
+
+    /// <summary>A share path (\\server\share\…): its checks may hang for many seconds, so they get a timeout.</summary>
+    public static bool IsNetworkPath(string path) => path.StartsWith(@"\\", StringComparison.Ordinal);
+}
+
+/// <summary>Which folders NeoFences watches for its targets (spec §4): a fixed budget, the busiest folders first.</summary>
+public static class WatchPlan
+{
+    public const int MaxFolders = 64;
+
+    /// <summary>
+    /// The parent folders of these targets, the one holding the most targets first (ties: the first seen), at most
+    /// <paramref name="maxFolders"/>. A drive root has no parent and is not watched; its items are re-checked.
+    /// </summary>
+    public static IReadOnlyList<string> Folders(IEnumerable<string> pathTargets, int maxFolders = MaxFolders) =>
+        pathTargets.Select(ParentOf).OfType<string>()
+            .Select((folder, order) => (folder, order))
+            .GroupBy(entry => entry.folder, ItemKinds.Comparer)
+            .OrderByDescending(group => group.Count()).ThenBy(group => group.First().order)
+            .Take(maxFolders)
+            .Select(group => group.First().folder)
+            .ToList();
+
+    /// <summary>"D:\Games" for "D:\Games\x.exe" and for "D:\Games\x\" (a folder target); null for a root.</summary>
+    public static string? ParentOf(string path)
+    {
+        var trimmed = path.TrimEnd('\\');
+        if (TargetChecks.RootOf(trimmed + "\\") is not { } root || ItemKinds.Comparer.Equals(root.TrimEnd('\\'), trimmed)) return null;
+        var parent = trimmed[..trimmed.LastIndexOf('\\')];
+        return parent.Length == 2 ? parent + "\\" : parent; // a file right on a drive: "D:\"
+    }
+}
+
+/// <summary>At most one refresh per fence every <see cref="Interval"/> (spec §4): bursts of changes wait and come once.</summary>
+public sealed class RefreshThrottle
+{
+    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
+
+    private readonly Dictionary<string, DateTimeOffset> _lastRefresh = new(StringComparer.Ordinal);
+
+    /// <summary>How long a fence with changes waits before its refresh: zero when its last one was long enough ago.</summary>
+    public TimeSpan DelayFor(string fenceId, DateTimeOffset now) =>
+        _lastRefresh.TryGetValue(fenceId, out var last) && now - last < Interval ? Interval - (now - last) : TimeSpan.Zero;
+
+    public void Refreshed(string fenceId, DateTimeOffset now) => _lastRefresh[fenceId] = now;
+}
````

- [ ] **Step 4: Run the tests.**
  Run: `dotnet test tests/NeoFences.Core.Tests`
  Expected: `Passed! - Failed: 0, Passed: 491`.

- [ ] **Step 5: Commit.**
  `git add src/NeoFences.Core/Items tests/NeoFences.Core.Tests/Items && git commit -m "feat: added target states, the watch plan and the refresh throttle to Core"`

### Task 3: Core — schema 5, items.json, and the parked model removed

**Files:**
- Create: `src/NeoFences.Core/Config/JsonStore.cs` (`JsonStore<T>`, `SafeFile`, `ConfigLoadSource`),
  `src/NeoFences.Core/Config/ItemStore.cs`, `src/NeoFences.Core/Library/GameLaunchers.cs`,
  `tests/NeoFences.Core.Tests/Config/ItemStoreTests.cs`, `tests/NeoFences.Core.Tests/Library/GameLaunchersTests.cs`,
  `tests/NeoFences.Core.Tests/Model/M8cCoreTests.cs` (moved from Membership, reconcile tests dropped)
- Modify: `src/NeoFences.Core/Config/{ConfigJson,ConfigNormalizer,ConfigStore,SnapshotStore}.cs`,
  `src/NeoFences.Core/Model/{Fence,FenceEdits,FenceTabs,ItemSorting,NeoFencesConfig,Settings,Snapshots}.cs`,
  `src/NeoFences.Core/Lifecycle/{RunState,StartupPolicy,WatchdogPlan}.cs`, `src/NeoFences.Core/Layouts/DropZones.cs`, and
  the tests listed in the patch
- Delete: `src/NeoFences.Core/Membership/{DesktopChange,FenceMembership,RememberedPlacement}.cs`,
  `src/NeoFences.Core/Model/{FileNames,Rules}.cs`, and 10 test files (Step 1)

**Interfaces:**
- Consumes: Tasks 1–2.
- Produces: `Fence { IsLibrary; static Create(string title, bool isLibrary = false) }` (no `Items`, `Source`, `IsInbox`,
  `Sort`); `FenceSort` stays (one-time sort); `Settings.HideDesktopIcons` (no `Takeover`, `TakeoverPromptAnswered`);
  `NeoFencesConfig.CurrentSchemaVersion = 5`, `CreateDefault()` = one fence "Fence" (no `Rules`, `Inbox`);
  `FenceEdits.CreateFence(config, title) → (Config, Fence)`, `FenceEdits.DeleteFence(config, fenceId)`;
  `Snapshot.Items`; `Snapshots.Take(config, ItemsDocument items, name, now)`, `Snapshots.Restore(current, snapshot) →
  (NeoFencesConfig Config, ItemsDocument Items)`; `RunState(bool HideIcons, …)`; `WatchdogPlan.For(…, bool iconsHidden)`;
  `ConfigStore.FirstVirtualItemsSchema = 5`; `ConfigLoadResult.KnowsTheFences`; `ItemStore(string directory, TimeProvider?)
  { ItemsPath; BackupPath; BackupsDirectory; Load() → ItemsLoadResult(Document, Source, CorruptCopyPath, IsReadOnly);
  Save(ItemsDocument) → bool; LastBackupFailure }`; `ConfigJson.{SerializeItems, DeserializeItems}`;
  `GameLaunchers.LauncherOf(string?) → GameLauncher?` with `enum GameLauncher { Steam, Epic, Ubisoft, Ea, BattleNet, Gog }`;
  `DropZones.InsertIndex` only (`IsInto` removed).
- After this task NeoFences.Shell and NeoFences.App do **not** build (they still use the removed model) until Tasks 4–5.
  The Core test project builds on its own; that is this task's gate.

- [ ] **Step 1: Write the failing tests** — remove the tests of the parked model, then apply the patch:

```bash
T=tests/NeoFences.Core.Tests
git rm -q $T/Layouts/DropZonesTests.cs $T/Membership/DesktopChangeTests.cs $T/Membership/DragDropTests.cs \
  $T/Membership/FenceMembershipTests.cs $T/Membership/M8cCoreTests.cs $T/Membership/ReconcileRecoveryTests.cs \
  $T/Membership/SafeSaveTests.cs $T/Model/FileNamesTests.cs $T/Model/PortalTests.cs $T/Model/RulesTests.cs
```

````diff
diff --git a/tests/NeoFences.Core.Tests/Appearance/AppearanceTests.cs b/tests/NeoFences.Core.Tests/Appearance/AppearanceTests.cs
index 304f92a..ba57e7f 100644
--- a/tests/NeoFences.Core.Tests/Appearance/AppearanceTests.cs
+++ b/tests/NeoFences.Core.Tests/Appearance/AppearanceTests.cs
@@ -203,7 +203,7 @@ public class AppearanceTests
     [Fact]
     public void Edits_ASwatchClearsTheCustomColour()
     {
-        var config = new NeoFencesConfig { Fences = [Fence.Create("Inbox") with { IsInbox = true }, Plain] };
+        var config = new NeoFencesConfig { Fences = [Fence.Create("Inbox"), Plain] };
 
         var custom = FenceEdits.SetCustomColor(config, Plain.Id, "#e84855");
         Assert.Equal("#E84855", custom.Fences[1].CustomColor);
diff --git a/tests/NeoFences.Core.Tests/Config/ConfigJsonTests.cs b/tests/NeoFences.Core.Tests/Config/ConfigJsonTests.cs
index b45b59b..db905de 100644
--- a/tests/NeoFences.Core.Tests/Config/ConfigJsonTests.cs
+++ b/tests/NeoFences.Core.Tests/Config/ConfigJsonTests.cs
@@ -8,14 +8,9 @@ public class ConfigJsonTests
 {
     private static NeoFencesConfig SampleConfig()
     {
-        var inbox = Fence.Create("Inbox") with { IsInbox = true, Items = [@"C:\Users\cipher\Desktop\notes.txt"] };
-        var games = Fence.Create("Games") with
-        {
-            Items = [@"C:\Users\cipher\Desktop\Crysis 2.lnk", "::{645FF040-5081-101B-9F08-00AA002F954E}"],
-            IconSize = 64,
-            RolledUp = true,
-        };
-        var screenshots = Fence.Create("Screenshots", FenceSource.Portal(@"D:\Pictures\Screenshots")) with { Sort = FenceSort.Date };
+        var tools = Fence.Create("Tools");
+        var games = Fence.Create("Games") with { IconSize = 64, RolledUp = true };
+        var library = Fence.Create("Library", isLibrary: true);
         var layout = new Layout
         {
             Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(2560, 1392) },
@@ -23,7 +18,7 @@ public class ConfigJsonTests
         };
         return new NeoFencesConfig
         {
-            Fences = [inbox, games, screenshots],
+            Fences = [tools, games, library],
             Layouts = new Dictionary<string, Layout> { ["1mon:DELL-3840x2160@150%"] = layout },
             LastLayoutFingerprint = "1mon:DELL-3840x2160@150%",
         };
@@ -42,10 +37,7 @@ public class ConfigJsonTests
         Assert.Equal(original.Fences.Count, restored.Fences.Count);
         for (var fenceIdx = 0; fenceIdx < original.Fences.Count; fenceIdx++)
         {
-            var expected = original.Fences[fenceIdx];
-            var actual = restored.Fences[fenceIdx];
-            Assert.Equal(expected with { Items = [], Tabs = [] }, actual with { Items = [], Tabs = [] }); // lists compare by reference
-            Assert.Equal(expected.Items, actual.Items);
+            Assert.Equal(original.Fences[fenceIdx] with { Tabs = [] }, restored.Fences[fenceIdx] with { Tabs = [] }); // lists compare by reference
         }
         var restoredLayout = restored.Layouts["1mon:DELL-3840x2160@150%"];
         Assert.Equal(new MonitorArea(2560, 1392), restoredLayout.Monitors["DELL"]);
@@ -53,19 +45,16 @@ public class ConfigJsonTests
     }
 
     [Fact]
-    public void Json_UsesCamelCaseNamesAndEnumValues_AndOmitsComputedInbox()
+    public void Json_UsesCamelCaseNames_AndHoldsNoItems()
     {
         var json = ConfigJson.Serialize(SampleConfig());
         using var document = JsonDocument.Parse(json);
         var root = document.RootElement;
 
         Assert.Equal(NeoFencesConfig.CurrentSchemaVersion, root.GetProperty("schemaVersion").GetInt32());
-        Assert.False(root.GetProperty("settings").GetProperty("takeover").GetBoolean());
-        Assert.False(root.TryGetProperty("inbox", out _));
-        var screenshots = root.GetProperty("fences")[2];
-        Assert.Equal("portal", screenshots.GetProperty("source").GetProperty("kind").GetString());
-        Assert.Equal(@"D:\Pictures\Screenshots", screenshots.GetProperty("source").GetProperty("path").GetString());
-        Assert.Equal("date", screenshots.GetProperty("sort").GetString());
+        Assert.False(root.GetProperty("settings").GetProperty("hideDesktopIcons").GetBoolean());
+        Assert.True(root.GetProperty("fences")[2].GetProperty("isLibrary").GetBoolean());
+        Assert.False(root.GetProperty("fences")[0].TryGetProperty("items", out _)); // items are items.json's (ADR-041)
         var rect = root.GetProperty("layouts").GetProperty("1mon:DELL-3840x2160@150%").GetProperty("fences").EnumerateObject().Single().Value;
         Assert.Equal("DELL", rect.GetProperty("monitor").GetString());
         Assert.Equal(420, rect.GetProperty("w").GetDouble());
@@ -74,7 +63,7 @@ public class ConfigJsonTests
     [Fact]
     public void Deserialize_AcceptsMinimalDocument()
     {
-        var config = ConfigJson.Deserialize("""{ "schemaVersion": 1 }""");
+        var config = ConfigJson.Deserialize("""{ "schemaVersion": 5 }""");
 
         Assert.Empty(config.Fences);
         Assert.Equal(new Settings(), config.Settings);
@@ -84,16 +73,30 @@ public class ConfigJsonTests
     public void Deserialize_MissingPropertiesKeepTheirDefaults()
     {
         var config = ConfigJson.Deserialize("""
-            { "schemaVersion": 1, "settings": { "takeover": true },
+            { "schemaVersion": 5, "settings": { "hideDesktopIcons": true },
               "fences": [ { "id": "f1", "title": "Games" } ] }
             """);
 
-        Assert.True(config.Settings.Takeover);
+        Assert.True(config.Settings.HideDesktopIcons);
         Assert.Equal("Ctrl+Alt+Space", config.Settings.PeekHotkey);
         var fence = Assert.Single(config.Fences);
         Assert.Equal(48, fence.IconSize);
-        Assert.Equal(FenceSource.Desktop, fence.Source);
-        Assert.Empty(fence.Items);
+        Assert.False(fence.IsLibrary);
+    }
+
+    [Fact]
+    public void Deserialize_IgnoresWhatOlderVersionsWrote()
+    {
+        // A pre-pivot file (schema 4): Inbox, Portal sources, rules, Takeover. Read without failing; ConfigStore then starts fresh.
+        var config = ConfigJson.Deserialize("""
+            { "schemaVersion": 4, "settings": { "takeover": true, "takeoverPromptAnswered": true },
+              "fences": [ { "id": "f1", "title": "Inbox", "isInbox": true, "items": [ "C:\\Users\\x\\Desktop\\a.txt" ] },
+                          { "id": "f2", "title": "Shots", "source": { "kind": "portal", "path": "D:\\Shots" }, "sort": "date" } ],
+              "rules": [ { "id": "r1", "fenceId": "f1", "condition": { "kind": "type" } } ] }
+            """);
+
+        Assert.Equal(["Inbox", "Shots"], config.Fences.Select(fence => fence.Title));
+        Assert.False(config.Settings.HideDesktopIcons);
     }
 
     [Theory]
diff --git a/tests/NeoFences.Core.Tests/Config/ConfigNormalizerTests.cs b/tests/NeoFences.Core.Tests/Config/ConfigNormalizerTests.cs
index 2f790c3..9e6fbba 100644
--- a/tests/NeoFences.Core.Tests/Config/ConfigNormalizerTests.cs
+++ b/tests/NeoFences.Core.Tests/Config/ConfigNormalizerTests.cs
@@ -6,67 +6,35 @@ namespace NeoFences.Core.Tests.Config;
 
 public class ConfigNormalizerTests
 {
-    private static Fence DesktopFence(string title, params string[] items) => Fence.Create(title) with { Items = items };
-
     [Fact]
-    public void MissingInbox_IsAddedFirst_OtherFencesKept()
+    public void NoFences_StaysNoFences()
     {
-        var games = DesktopFence("Games", "a.lnk");
-
-        var normalized = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = [games] });
-
-        Assert.Equal(2, normalized.Fences.Count);
-        Assert.True(normalized.Fences[0].IsInbox);
-        Assert.Equal(games with { Items = [] }, normalized.Fences[1] with { Items = [] });
-        Assert.Equal(["a.lnk"], normalized.Fences[1].Items);
-    }
-
-    [Fact]
-    public void TwoInboxes_FirstStaysInbox_SecondBecomesNormalFenceWithItems()
-    {
-        var first = DesktopFence("Inbox", "a.txt") with { IsInbox = true };
-        var second = DesktopFence("Inbox 2", "b.txt") with { IsInbox = true };
-
-        var normalized = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = [first, second] });
-
-        Assert.True(normalized.Fences[0].IsInbox);
-        Assert.False(normalized.Fences[1].IsInbox);
-        Assert.Equal(["b.txt"], normalized.Fences[1].Items);
+        // M18: no Inbox is invented; the tray's "New fence" makes one.
+        Assert.Empty(ConfigNormalizer.Normalize(new NeoFencesConfig()).Fences);
     }
 
     [Fact]
     public void DuplicateFenceIds_LaterOneGetsNewId()
     {
-        var inbox = DesktopFence("Inbox") with { IsInbox = true };
-        var copy = DesktopFence("Copy") with { Id = inbox.Id };
+        var first = Fence.Create("First");
+        var copy = Fence.Create("Copy") with { Id = first.Id };
 
-        var normalized = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = [inbox, copy] });
+        var normalized = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = [first, copy] });
 
-        Assert.Equal(inbox.Id, normalized.Fences[0].Id);
-        Assert.NotEqual(inbox.Id, normalized.Fences[1].Id);
+        Assert.Equal(first.Id, normalized.Fences[0].Id);
+        Assert.NotEqual(first.Id, normalized.Fences[1].Id);
         Assert.Equal("Copy", normalized.Fences[1].Title);
     }
 
     [Fact]
-    public void ItemInTwoFences_StaysOnlyInFirst_ComparedCaseInsensitively()
-    {
-        var inbox = DesktopFence("Inbox", @"C:\Desktop\Crysis.lnk") with { IsInbox = true };
-        var games = DesktopFence("Games", @"c:\desktop\crysis.LNK", @"C:\Desktop\AC.lnk");
-
-        var normalized = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = [inbox, games] });
-
-        Assert.Equal([@"C:\Desktop\Crysis.lnk"], normalized.Fences[0].Items);
-        Assert.Equal([@"C:\Desktop\AC.lnk"], normalized.Fences[1].Items);
-    }
-
-    [Fact]
-    public void PortalFence_ItemsAreCleared()
+    public void TwoLibraryFences_OnlyTheFirstStaysTheLibrary()
     {
-        var portal = Fence.Create("Shots", FenceSource.Portal(@"D:\Shots")) with { Items = ["stray.png"] };
-
-        var normalized = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = [portal] });
+        var normalized = ConfigNormalizer.Normalize(new NeoFencesConfig
+        {
+            Fences = [Fence.Create("Games", isLibrary: true), Fence.Create("Games 2", isLibrary: true)],
+        });
 
-        Assert.Empty(normalized.Fences.Single(fence => fence.Id == portal.Id).Items);
+        Assert.Equal([true, false], normalized.Fences.Select(fence => fence.IsLibrary));
     }
 
     [Theory]
@@ -86,33 +54,32 @@ public class ConfigNormalizerTests
     public void NullsFromHandEditedJson_AreRepaired()
     {
         var config = ConfigJson.Deserialize("""
-            { "schemaVersion": 1, "settings": { "peekHotkey": null }, "layouts": null,
-              "fences": [ null, { "id": null, "title": null, "source": null, "items": [ "a.txt", null, "" ] } ] }
+            { "schemaVersion": 5, "settings": { "peekHotkey": null }, "layouts": null,
+              "fences": [ null, { "id": null, "title": null, "tabs": null } ] }
             """);
 
         var normalized = ConfigNormalizer.Normalize(config);
 
         Assert.Equal(new Settings(), normalized.Settings);
         Assert.Empty(normalized.Layouts);
-        var repaired = normalized.Fences.Single(fence => !fence.IsInbox);
+        var repaired = Assert.Single(normalized.Fences);
         Assert.False(string.IsNullOrWhiteSpace(repaired.Id));
         Assert.Equal("", repaired.Title);
-        Assert.Equal(FenceSource.Desktop, repaired.Source);
-        Assert.Equal(["a.txt"], repaired.Items);
+        Assert.Empty(repaired.Tabs);
     }
 
     [Fact]
     public void BrokenLayoutsFromHandEditedJson_AreRepaired_AndResolveStillWorks()
     {
-        var inboxId = Fence.NewId();
+        var fenceId = Fence.NewId();
         var config = ConfigJson.Deserialize($$"""
-            { "schemaVersion": 1, "lastLayoutFingerprint": "half",
-              "fences": [ { "id": "{{inboxId}}", "title": "Inbox", "isInbox": true } ],
+            { "schemaVersion": 5, "lastLayoutFingerprint": "half",
+              "fences": [ { "id": "{{fenceId}}", "title": "Fence" } ],
               "layouts": {
                 "nulled": null,
-                "half": { "monitors": null, "fences": { "{{inboxId}}": null, "noMonitor": { "x": 1 } } },
+                "half": { "monitors": null, "fences": { "{{fenceId}}": null, "noMonitor": { "x": 1 } } },
                 "zeroArea": { "monitors": { "DELL": { "workWidth": 0, "workHeight": 700 } },
-                              "fences": { "{{inboxId}}": { "monitor": "DELL", "x": 5, "y": 6, "w": 300, "h": 200 } } } } }
+                              "fences": { "{{fenceId}}": { "monitor": "DELL", "x": 5, "y": 6, "w": 300, "h": 200 } } } } }
             """);
 
         var normalized = ConfigNormalizer.Normalize(config);
@@ -124,33 +91,20 @@ public class ConfigNormalizerTests
         Assert.Single(normalized.Layouts["zeroArea"].Fences);
         var monitor = new DisplayMonitor("DELL", 1920, 1080, 100, 1280, 700, IsPrimary: true);
         var (_, layout) = LayoutEngine.Resolve(normalized, [monitor]);
-        Assert.True(layout.Fences.ContainsKey(inboxId));
-    }
-
-    [Fact]
-    public void Inbox_IsAlwaysADesktopFence()
-    {
-        var portalInbox = Fence.Create("Inbox", FenceSource.Portal(@"D:Shots")) with { IsInbox = true };
-
-        var normalized = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = [portalInbox] });
-
-        Assert.Equal(FenceSource.Desktop, normalized.Inbox.Source);
+        Assert.True(layout.Fences.ContainsKey(fenceId));
     }
 
     [Fact]
-    public void OutOfRangeEnumNumbers_FallBackToDefaults_WithoutClearingItems()
+    public void OutOfRangeEnumNumbers_FallBackToDefaults()
     {
         var config = ConfigJson.Deserialize("""
-            { "schemaVersion": 1, "settings": { "rollupExpand": 9 },
-              "fences": [ { "id": "f1", "title": "Games", "sort": 7, "source": { "kind": 5 }, "items": [ "a.lnk" ] } ] }
+            { "schemaVersion": 5, "settings": { "rollupExpand": 9 },
+              "fences": [ { "id": "f1", "title": "Games", "labels": 7 } ] }
             """);
 
         var normalized = ConfigNormalizer.Normalize(config);
 
-        var games = normalized.Fences.Single(fence => fence.Id == "f1");
-        Assert.Equal(FenceSource.Desktop, games.Source);
-        Assert.Equal(FenceSort.Manual, games.Sort);
-        Assert.Equal(["a.lnk"], games.Items);
+        Assert.Equal(LabelMode.Always, normalized.Fences.Single().Labels);
         Assert.Equal(RollupExpand.Hover, normalized.Settings.RollupExpand);
     }
 }
diff --git a/tests/NeoFences.Core.Tests/Config/ConfigStoreTests.cs b/tests/NeoFences.Core.Tests/Config/ConfigStoreTests.cs
index ec688a0..6f7c377 100644
--- a/tests/NeoFences.Core.Tests/Config/ConfigStoreTests.cs
+++ b/tests/NeoFences.Core.Tests/Config/ConfigStoreTests.cs
@@ -13,8 +13,8 @@ public class ConfigStoreTests : IDisposable
 
     private ConfigStore NewStore() => new(_directory.Path, _clock);
 
-    private static NeoFencesConfig ConfigTitled(string inboxTitle) =>
-        NeoFencesConfig.CreateDefault() is var config ? config.WithFence(config.Inbox with { Title = inboxTitle }) : throw new InvalidOperationException();
+    private static NeoFencesConfig ConfigTitled(string title) =>
+        NeoFencesConfig.CreateDefault() is var config ? config.WithFence(config.Fences[0] with { Title = title }) : throw new InvalidOperationException();
 
     private string[] DailyBackupNames(ConfigStore store) =>
         Directory.GetFiles(store.BackupsDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;
@@ -28,7 +28,7 @@ public class ConfigStoreTests : IDisposable
 
         Assert.True(store.Save(ConfigTitled("First")));
 
-        Assert.Equal("First", ConfigJson.Deserialize(File.ReadAllText(store.ConfigPath)).Inbox.Title);
+        Assert.Equal("First", ConfigJson.Deserialize(File.ReadAllText(store.ConfigPath)).Fences[0].Title);
         Assert.Equal(["config-20261002.json"], DailyBackupNames(store));
         Assert.False(File.Exists(store.ConfigPath + ".tmp"));
         Assert.False(File.Exists(store.BackupPath));
@@ -42,8 +42,8 @@ public class ConfigStoreTests : IDisposable
 
         store.Save(ConfigTitled("Second"));
 
-        Assert.Equal("Second", ConfigJson.Deserialize(File.ReadAllText(store.ConfigPath)).Inbox.Title);
-        Assert.Equal("First", ConfigJson.Deserialize(File.ReadAllText(store.BackupPath)).Inbox.Title);
+        Assert.Equal("Second", ConfigJson.Deserialize(File.ReadAllText(store.ConfigPath)).Fences[0].Title);
+        Assert.Equal("First", ConfigJson.Deserialize(File.ReadAllText(store.BackupPath)).Fences[0].Title);
     }
 
     [Fact]
@@ -56,7 +56,7 @@ public class ConfigStoreTests : IDisposable
         store.Save(ConfigTitled("Evening"));
 
         var daily = Path.Combine(store.BackupsDirectory, "config-20261002.json");
-        Assert.Equal("Morning", ConfigJson.Deserialize(File.ReadAllText(daily)).Inbox.Title);
+        Assert.Equal("Morning", ConfigJson.Deserialize(File.ReadAllText(daily)).Fences[0].Title);
     }
 
     [Fact]
@@ -112,19 +112,37 @@ public class ConfigStoreTests : IDisposable
         var result = NewStore().Load();
 
         Assert.Equal(ConfigLoadSource.Primary, result.Source);
-        Assert.Equal("Saved", result.Config.Inbox.Title);
+        Assert.Equal("Saved", result.Config.Fences[0].Title);
     }
 
     [Fact]
     public void Load_IsNormalized()
     {
         var store = NewStore();
-        File.WriteAllText(store.ConfigPath, """{ "schemaVersion": 1, "fences": [] }""");
+        File.WriteAllText(store.ConfigPath, """{ "schemaVersion": 5, "fences": [ { "id": "a", "title": "A", "iconSize": 7 } ] }""");
 
         var result = store.Load();
 
         Assert.Equal(ConfigLoadSource.Primary, result.Source);
-        Assert.True(Assert.Single(result.Config.Fences).IsInbox);
+        Assert.Equal(48, Assert.Single(result.Config.Fences).IconSize);
+    }
+
+    [Fact]
+    public void Load_ConfigFromBeforeTheVirtualItems_StartsFresh_AndTheFirstSaveKeepsTheOldFile()
+    {
+        // M18 spec §1: a schema-4 config (Inbox, Desktop membership, Portals) is not migrated.
+        var store = NewStore();
+        const string oldJson = """{ "schemaVersion": 4, "fences": [ { "id": "inbox", "title": "Inbox", "isInbox": true, "items": [ "a.txt" ] } ] }""";
+        File.WriteAllText(store.ConfigPath, oldJson);
+
+        var result = store.Load();
+        Assert.True(store.Save(result.Config));
+
+        Assert.Equal(ConfigLoadSource.Fresh, result.Source);
+        Assert.False(result.IsReadOnly);
+        Assert.Equal("Fence", Assert.Single(result.Config.Fences).Title);
+        Assert.Equal(oldJson, File.ReadAllText(Path.Combine(store.BackupsDirectory, "pre-schema-5-config.json")));
+        Assert.Equal(5, ConfigJson.Deserialize(File.ReadAllText(store.ConfigPath)).SchemaVersion);
     }
 
     [Fact]
@@ -138,7 +156,7 @@ public class ConfigStoreTests : IDisposable
         var result = NewStore().Load();
 
         Assert.Equal(ConfigLoadSource.Backup, result.Source);
-        Assert.Equal("Older", result.Config.Inbox.Title);
+        Assert.Equal("Older", result.Config.Fences[0].Title);
         Assert.Equal(_directory.File("config.corrupt-20261002-093000.json"), result.CorruptCopyPath);
         Assert.Equal("{ broken", File.ReadAllText(result.CorruptCopyPath!));
     }
@@ -157,7 +175,7 @@ public class ConfigStoreTests : IDisposable
         var result = NewStore().Load();
 
         Assert.Equal(ConfigLoadSource.DailyBackup, result.Source);
-        Assert.Equal("Day 1", result.Config.Inbox.Title);
+        Assert.Equal("Day 1", result.Config.Fences[0].Title);
     }
 
     [Fact]
@@ -205,7 +223,7 @@ public class ConfigStoreTests : IDisposable
         var saved = lockedStore.Save(result.Config);
 
         Assert.True(result.IsReadOnly);
-        Assert.Equal("Precious", result.Config.Inbox.Title); // from the daily backup
+        Assert.Equal("Precious", result.Config.Fences[0].Title); // from the daily backup
         Assert.False(saved);
         Assert.Equal(before, File.ReadAllText(store.ConfigPath));
     }
@@ -230,7 +248,7 @@ public class ConfigStoreTests : IDisposable
 
         Assert.True(store.Save(ConfigTitled("Saved")));
 
-        Assert.Equal("Saved", ConfigJson.Deserialize(File.ReadAllText(store.ConfigPath)).Inbox.Title);
+        Assert.Equal("Saved", ConfigJson.Deserialize(File.ReadAllText(store.ConfigPath)).Fences[0].Title);
         Assert.NotNull(store.LastBackupFailure);
     }
 
diff --git a/tests/NeoFences.Core.Tests/Config/DataSafetyTests.cs b/tests/NeoFences.Core.Tests/Config/DataSafetyTests.cs
index 534f163..5a4a135 100644
--- a/tests/NeoFences.Core.Tests/Config/DataSafetyTests.cs
+++ b/tests/NeoFences.Core.Tests/Config/DataSafetyTests.cs
@@ -1,4 +1,5 @@
 using NeoFences.Core.Config;
+using NeoFences.Core.Items;
 using NeoFences.Core.Library;
 using NeoFences.Core.Model;
 using NeoFences.Core.Tests.TestSupport;
@@ -7,7 +8,7 @@ namespace NeoFences.Core.Tests.Config;
 
 /// <summary>
 /// M13a (v1.6.0) data safety: carry-overs from the M9–M12 reviews — the schema version, snapshots from newer versions,
-/// duplicate rule ids, the library index after failed writes, and the M10 test gaps.
+/// the library index after failed writes, and the M10 test gaps (M18: with virtual items).
 /// </summary>
 public class DataSafetyTests : IDisposable
 {
@@ -17,10 +18,10 @@ public class DataSafetyTests : IDisposable
 
     public void Dispose() => _directory.Dispose();
 
-    // ---------- schema version (M9 carry-over: an older build would save over tabs, rules and the library) ----------
+    // ---------- schema version (M9 carry-over: an older build would save over newer fields) ----------
 
     [Fact]
-    public void Schema_IsCurrent_AndAnOldConfigIsUpgradedWhenNormalized() // 2 in v1.6, 3 since v1.7 (M14)
+    public void Schema_IsCurrent_AndAnOldConfigIsUpgradedWhenNormalized()
     {
         var old = ConfigJson.Deserialize("""{ "schemaVersion": 1, "fences": [] }""");
         Assert.Equal(1, old.SchemaVersion);
@@ -28,7 +29,7 @@ public class DataSafetyTests : IDisposable
     }
 
     [Fact]
-    public void Schema_AVersionOneFileLoads_AndIsSavedAsCurrent_WhileANewerFileIsNeverOverwritten()
+    public void Schema_AnOlderFileStartsFresh_AndIsSavedAsCurrent_WhileANewerFileIsNeverOverwritten()
     {
         var store = new ConfigStore(_directory.Path, new FixedTimeProvider(Taken));
         File.WriteAllText(store.ConfigPath, """{ "schemaVersion": 1, "fences": [ { "id": "a", "title": "Games" } ] }""");
@@ -44,25 +45,12 @@ public class DataSafetyTests : IDisposable
         Assert.False(newer.Save(NeoFencesConfig.CreateDefault()));
     }
 
-    // ---------- rules (M11 carry-over) ----------
-
-    [Fact]
-    public void Rules_DuplicateIdsGetNewOnes_TheFirstKeepsItsId()
-    {
-        var images = Rule.Create(new RuleCondition { Kind = RuleKind.Type, Group = TypeGroup.Images }, "fence") with { Id = "same" };
-        var config = ConfigNormalizer.Normalize(new NeoFencesConfig { Rules = [images, images with { FenceId = "other" }, images] });
-
-        Assert.Equal("same", config.Rules[0].Id);
-        Assert.Equal(3, config.Rules.Select(rule => rule.Id).Distinct().Count());
-    }
-
     // ---------- snapshots (M10 carry-overs and test gaps) ----------
 
-    private static NeoFencesConfig Sample(out Fence inbox, out Fence games)
+    private static (NeoFencesConfig Config, ItemsDocument Items) Sample()
     {
-        inbox = Fence.Create("Inbox") with { IsInbox = true, Items = [Desktop + "a.txt"] };
-        games = Fence.Create("Games") with { Items = [Desktop + "Game.lnk"] };
-        return new NeoFencesConfig { Fences = [inbox, games] };
+        var games = Fence.Create("Games");
+        return (new NeoFencesConfig { Fences = [games] }, new ItemsDocument().With(games.Id, [VirtualItem.Create(Desktop + "Game.lnk")]));
     }
 
     [Fact]
@@ -78,6 +66,19 @@ public class DataSafetyTests : IDisposable
         Assert.Equal([path], store.Problems.Select(problem => problem.Path));
     }
 
+    [Fact]
+    public void Snapshots_FromBeforeTheVirtualItemsAreRefused()
+    {
+        // M18: a schema-4 snapshot's fences held Desktop files; restoring it would bring back empty Inbox and Portal fences.
+        var store = new SnapshotStore(_directory.Path);
+        var path = Path.Combine(_directory.Path, "snapshot-old.json");
+        File.WriteAllText(path, """{ "schemaVersion": 4, "name": "Old", "takenAt": "2026-10-03T12:00:00+05:30", "fences": [ { "id": "i", "title": "Inbox", "isInbox": true } ] }""");
+
+        Assert.Null(store.Load(path));
+        Assert.Empty(store.List());
+        Assert.Equal([path], store.Problems.Select(problem => problem.Path));
+    }
+
     [Fact]
     public void Snapshots_AHugeStrayFileIsSkipped_NotRead()
     {
@@ -94,44 +95,27 @@ public class DataSafetyTests : IDisposable
     {
         var store = new SnapshotStore(_directory.Path);
         Directory.CreateDirectory(Path.Combine(_directory.Path, "blocked.json")); // a folder where the file should go
+        var (config, items) = Sample();
 
-        Assert.Null(store.Save(Snapshots.Take(Sample(out _, out _), name: "x", now: Taken), "blocked.json"));
+        Assert.Null(store.Save(Snapshots.Take(config, items, name: "x", now: Taken), "blocked.json"));
         Assert.NotNull(store.LastFailure);
         Assert.Empty(Directory.GetFiles(_directory.Path, "*.tmp"));
     }
 
-    [Fact]
-    public void Snapshots_RestoreKeepsAPortal_MatchesRefsIgnoringCase_AndOrdersNewcomers()
-    {
-        var current = Sample(out var inbox, out var games);
-        var portal = Fence.Create("Downloads", FenceSource.Portal(@"D:\Downloads"));
-        var snapshot = Snapshots.Take(current with { Fences = [.. current.Fences, portal] }, name: "s", now: Taken);
-        // Since then: Game.lnk renamed in case only, a.txt moved by hand into Games, three new unfenced items.
-        current = current
-            .WithFence(inbox with { Items = [] })
-            .WithFence(games with { Items = [Desktop + "a.txt", Desktop + "new-in-games.txt"] });
-        string[] desktopNow = [Desktop + "z.txt", Desktop + "GAME.LNK", Desktop + "y.txt", Desktop + "a.txt", Desktop + "new-in-games.txt", Desktop + "x.txt"];
-
-        var restored = Snapshots.Restore(current, snapshot, desktopNow);
-
-        Assert.Empty(restored.Fences.Single(fence => fence.Title == "Downloads").Items);
-        Assert.Equal([Desktop + "Game.lnk", Desktop + "new-in-games.txt"], restored.Fences.Single(fence => fence.Id == games.Id).Items);
-        Assert.Equal([Desktop + "a.txt", Desktop + "z.txt", Desktop + "y.txt", Desktop + "x.txt"], restored.Inbox.Items); // the Desktop's order
-    }
-
     [Fact]
     public void Snapshots_AFileWithNullFieldsStillRestores()
     {
         var store = new SnapshotStore(_directory.Path);
         var path = Path.Combine(_directory.Path, "nulls.json");
-        File.WriteAllText(path, """{ "schemaVersion": 1, "name": null, "takenAt": "2026-10-04T12:00:00+05:30", "fences": [ null, { "id": "g", "title": null, "items": null, "tabs": null } ], "layouts": null }""");
+        File.WriteAllText(path, """{ "schemaVersion": 5, "name": null, "takenAt": "2026-10-04T12:00:00+05:30", "fences": [ null, { "id": "g", "title": null, "tabs": null } ], "layouts": null, "items": { "g": [ null, { "id": "x", "target": "" } ] } }""");
 
         var snapshot = store.Load(path)!;
-        var restored = Snapshots.Restore(Sample(out _, out _), snapshot, [Desktop + "a.txt"]);
+        var (config, items) = Sample();
+        var (restored, restoredItems) = Snapshots.Restore(config, snapshot);
 
-        Assert.Single(restored.Fences, fence => fence.IsInbox);
-        Assert.Contains(restored.Fences, fence => fence.Id == "g");
-        Assert.Equal([Desktop + "a.txt"], restored.Fences.SelectMany(fence => fence.Items));
+        Assert.Equal("g", Assert.Single(restored.Fences).Id);
+        Assert.Empty(restoredItems.Of("g"));
+        Assert.Single(items.Fences); // the current document is untouched
     }
 
     // ---------- the Game Library index after failed writes (M12 carry-over) ----------
diff --git a/tests/NeoFences.Core.Tests/Config/ItemStoreTests.cs b/tests/NeoFences.Core.Tests/Config/ItemStoreTests.cs
new file mode 100644
index 0000000..f163b1d
--- /dev/null
+++ b/tests/NeoFences.Core.Tests/Config/ItemStoreTests.cs
@@ -0,0 +1,118 @@
+using NeoFences.Core.Config;
+using NeoFences.Core.Items;
+using NeoFences.Core.Tests.TestSupport;
+
+namespace NeoFences.Core.Tests.Config;
+
+/// <summary>items.json (ADR-041): the same safe save and recovery as config.json, in a file of its own.</summary>
+public class ItemStoreTests : IDisposable
+{
+    private readonly TempDirectory _directory = new();
+    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 10, 4, 9, 30, 0, TimeSpan.Zero));
+
+    public void Dispose() => _directory.Dispose();
+
+    private ItemStore NewStore() => new(_directory.Path, _clock);
+
+    private static ItemsDocument Holding(string name) =>
+        new ItemsDocument().With("fence", [new VirtualItem { Id = "a", Target = @"D:\Games\a.exe", Name = name, Icon = new ItemIcon { File = @"C:\x.dll", Index = 3 } }]);
+
+    [Fact]
+    public void Save_ThenLoad_RoundTripsEveryField_NoTempLeft()
+    {
+        var item = new VirtualItem
+        {
+            Id = "a", Target = @"D:\Games\a.exe", Name = "A", Arguments = "-windowed", RunAsAdmin = true, Note = "the note",
+            Icon = new ItemIcon { Image = "a.png" },
+        };
+        var store = NewStore();
+
+        Assert.True(store.Save(new ItemsDocument().With("fence", [item])));
+        var loaded = NewStore().Load();
+
+        Assert.Equal(ConfigLoadSource.Primary, loaded.Source);
+        Assert.Equal(item, loaded.Document.Of("fence").Single());
+        Assert.False(File.Exists(store.ItemsPath + ".tmp"));
+        Assert.Equal(["items-20261004.json"], Directory.GetFiles(store.BackupsDirectory).Select(Path.GetFileName));
+        Assert.Contains("\"schema\": 1", File.ReadAllText(store.ItemsPath));
+        Assert.DoesNotContain("\"kind\"", File.ReadAllText(store.ItemsPath)); // derived, never stored
+    }
+
+    [Fact]
+    public void Load_NothingOnDisk_IsEmpty()
+    {
+        var loaded = NewStore().Load();
+
+        Assert.Equal(ConfigLoadSource.Fresh, loaded.Source);
+        Assert.Empty(loaded.Document.Fences);
+        Assert.False(loaded.IsReadOnly);
+    }
+
+    [Fact]
+    public void Load_ADamagedFile_IsKept_AndTheBackupIsUsed()
+    {
+        var store = NewStore();
+        store.Save(Holding("Older"));
+        store.Save(Holding("Newer"));
+        File.WriteAllText(store.ItemsPath, "{ broken");
+
+        var loaded = NewStore().Load();
+
+        Assert.Equal(ConfigLoadSource.Backup, loaded.Source);
+        Assert.Equal("Older", loaded.Document.Find("a")!.Name);
+        Assert.Equal(_directory.File("items.corrupt-20261004-093000.json"), loaded.CorruptCopyPath);
+    }
+
+    [Fact]
+    public void Load_FromANewerVersion_IsReadOnly_AndNeverOverwritten()
+    {
+        var store = NewStore();
+        const string newer = """{ "schema": 99, "fences": {} }""";
+        File.WriteAllText(store.ItemsPath, newer);
+
+        var loaded = store.Load();
+
+        Assert.True(loaded.IsReadOnly);
+        Assert.False(store.Save(Holding("x")));
+        Assert.Equal(newer, File.ReadAllText(store.ItemsPath));
+    }
+
+    [Fact]
+    public void Load_RepairsAHandEditedFile()
+    {
+        var store = NewStore();
+        File.WriteAllText(store.ItemsPath, """{ "schema": 1, "fences": { "f": [ { "id": "x", "target": "D:\\a.exe" }, { "id": "x", "target": "D:\\b.exe" }, null ] } }""");
+
+        var items = store.Load().Document.Of("f");
+
+        Assert.Equal(2, items.Select(item => item.Id).Distinct().Count());
+    }
+
+    [Fact]
+    public void OnlyARealConfig_MayDropItemListsOfUnknownFences()
+    {
+        // A fresh or read-only config is a fallback: its fence ids are not the user's, so items.json's lists must stay (ADR-041).
+        var config = new ConfigStore(_directory.Path, _clock);
+        Assert.False(config.Load().KnowsTheFences); // nothing on disk yet: fresh
+        config.Save(NeoFences.Core.Model.NeoFencesConfig.CreateDefault());
+        Assert.True(new ConfigStore(_directory.Path, _clock).Load().KnowsTheFences);
+        File.WriteAllText(config.ConfigPath, """{ "schemaVersion": 99 }""");
+        Assert.False(new ConfigStore(_directory.Path, _clock).Load().KnowsTheFences); // a newer NeoFences': read-only
+        File.WriteAllText(config.ConfigPath, """{ "schemaVersion": 4, "fences": [] }""");
+        Assert.False(new ConfigStore(_directory.Path, _clock).Load().KnowsTheFences); // from before the virtual items: fresh
+    }
+
+    [Fact]
+    public void ADamagedItemsFile_NeverTouchesConfigJson()
+    {
+        var config = new ConfigStore(_directory.Path, _clock);
+        config.Save(NeoFences.Core.Model.NeoFencesConfig.CreateDefault());
+        var before = File.ReadAllText(config.ConfigPath);
+        File.WriteAllText(NewStore().ItemsPath, "garbage");
+
+        NewStore().Load();
+
+        Assert.Equal(before, File.ReadAllText(config.ConfigPath));
+        Assert.Equal(ConfigLoadSource.Primary, new ConfigStore(_directory.Path, _clock).Load().Source);
+    }
+}
diff --git a/tests/NeoFences.Core.Tests/Input/HotkeyTests.cs b/tests/NeoFences.Core.Tests/Input/HotkeyTests.cs
index dc1a903..30fdf5a 100644
--- a/tests/NeoFences.Core.Tests/Input/HotkeyTests.cs
+++ b/tests/NeoFences.Core.Tests/Input/HotkeyTests.cs
@@ -93,7 +93,7 @@ public class RollUpTests
     public void SetRolledUp_IsStored_AndUnrollRestores()
     {
         var games = Fence.Create("Games");
-        var config = new NeoFencesConfig { Fences = [Fence.Create("Inbox") with { IsInbox = true }, games] };
+        var config = new NeoFencesConfig { Fences = [Fence.Create("Inbox"), games] };
 
         var rolled = FenceEdits.SetRolledUp(config, games.Id, rolledUp: true);
 
diff --git a/tests/NeoFences.Core.Tests/Layouts/LayoutEngineTests.cs b/tests/NeoFences.Core.Tests/Layouts/LayoutEngineTests.cs
index 6d079c7..5786690 100644
--- a/tests/NeoFences.Core.Tests/Layouts/LayoutEngineTests.cs
+++ b/tests/NeoFences.Core.Tests/Layouts/LayoutEngineTests.cs
@@ -14,7 +14,7 @@ public class LayoutEngineTests
     {
         var games = Fence.Create("Games");
         var config = NeoFencesConfig.CreateDefault();
-        return (config with { Fences = [config.Inbox, games] }, games);
+        return (config with { Fences = [config.Fences[0], games] }, games);
     }
 
     private static NeoFencesConfig WithSavedLayout(NeoFencesConfig config, string fingerprint, Layout layout) =>
@@ -35,7 +35,7 @@ public class LayoutEngineTests
 
         var (resolved, layout) = LayoutEngine.Resolve(config, [Lg, Dell4K]);
 
-        Assert.Equal(new FenceRect("DELL", 24, 24, 320, 220), layout.Fences[config.Inbox.Id]);
+        Assert.Equal(new FenceRect("DELL", 24, 24, 320, 220), layout.Fences[config.Fences[0].Id]);
         // M5 smart placement: beside the Inbox with an 8 DIP gap, never stacked on top of it (the old cascade overlapped).
         Assert.Equal(new FenceRect("DELL", 352, 24, 320, 220), layout.Fences[games.Id]);
         Assert.Equal("2mon:DELL-3840x2160@150%+LG-1920x1080@100%", resolved.LastLayoutFingerprint);
@@ -51,7 +51,7 @@ public class LayoutEngineTests
             Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(2560, 1400), ["LG"] = new(1920, 1032) },
             Fences = new Dictionary<string, FenceRect>
             {
-                [config.Inbox.Id] = new("LG", 100, 200, 400, 300),
+                [config.Fences[0].Id] = new("LG", 100, 200, 400, 300),
                 [games.Id] = new("DELL", 1000, 500, 420, 260),
             },
         };
@@ -59,7 +59,7 @@ public class LayoutEngineTests
 
         var (_, layout) = LayoutEngine.Resolve(config, [Dell4K, Lg]);
 
-        Assert.Equal(saved.Fences[config.Inbox.Id], layout.Fences[config.Inbox.Id]);
+        Assert.Equal(saved.Fences[config.Fences[0].Id], layout.Fences[config.Fences[0].Id]);
         Assert.Equal(saved.Fences[games.Id], layout.Fences[games.Id]);
     }
 
@@ -73,14 +73,14 @@ public class LayoutEngineTests
             Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(2560, 1400) },
             Fences = new Dictionary<string, FenceRect>
             {
-                [config.Inbox.Id] = new("DELL", 0, 0, 640, 350),
+                [config.Fences[0].Id] = new("DELL", 0, 0, 640, 350),
                 [games.Id] = new("DELL", 1280, 700, 512, 280),
             },
         });
 
         var (resolved, layout) = LayoutEngine.Resolve(config, [Dell1080]);
 
-        Assert.Equal(new FenceRect("DELL", 0, 0, 320, 175), layout.Fences[config.Inbox.Id]);
+        Assert.Equal(new FenceRect("DELL", 0, 0, 320, 175), layout.Fences[config.Fences[0].Id]);
         Assert.Equal(new FenceRect("DELL", 640, 350, 256, 140), layout.Fences[games.Id]);
         Assert.True(resolved.Layouts.ContainsKey(fourKFingerprint)); // going back to 4K restores the original
 
@@ -116,7 +116,7 @@ public class LayoutEngineTests
             Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(2560, 1400) },
             Fences = new Dictionary<string, FenceRect>
             {
-                [config.Inbox.Id] = new("DELL", 500, 500, 300, 200),
+                [config.Fences[0].Id] = new("DELL", 500, 500, 300, 200),
                 ["deleted-fence"] = new("DELL", 0, 0, 300, 200),
             },
         });
@@ -124,7 +124,7 @@ public class LayoutEngineTests
         var (_, layout) = LayoutEngine.Resolve(config, [Dell4K]);
 
         Assert.False(layout.Fences.ContainsKey("deleted-fence"));
-        Assert.Equal(new FenceRect("DELL", 500, 500, 300, 200), layout.Fences[config.Inbox.Id]);
+        Assert.Equal(new FenceRect("DELL", 500, 500, 300, 200), layout.Fences[config.Fences[0].Id]);
         Assert.Equal(new FenceRect("DELL", 24, 24, 320, 220), layout.Fences[games.Id]);
     }
 
@@ -204,7 +204,7 @@ public class LayoutEngineTests
         var updated = LayoutEngine.WithFenceRect(resolved, fingerprint: resolved.LastLayoutFingerprint!, fenceId: games.Id, rect: moved);
 
         Assert.Equal(moved, updated.Layouts[resolved.LastLayoutFingerprint!].Fences[games.Id]);
-        Assert.Equal(resolved.Layouts[resolved.LastLayoutFingerprint!].Fences[config.Inbox.Id], updated.Layouts[resolved.LastLayoutFingerprint!].Fences[config.Inbox.Id]);
+        Assert.Equal(resolved.Layouts[resolved.LastLayoutFingerprint!].Fences[config.Fences[0].Id], updated.Layouts[resolved.LastLayoutFingerprint!].Fences[config.Fences[0].Id]);
     }
 
     [Fact]
@@ -217,7 +217,7 @@ public class LayoutEngineTests
             Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(1280, 700) },
             Fences = new Dictionary<string, FenceRect>
             {
-                [config.Inbox.Id] = new("DELL", 24, 24, 1232, 220), // the whole top row
+                [config.Fences[0].Id] = new("DELL", 24, 24, 1232, 220), // the whole top row
             },
         });
 
@@ -234,7 +234,7 @@ public class LayoutEngineTests
         config = WithSavedLayout(config, fingerprint, new Layout
         {
             Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(1280, 700) },
-            Fences = new Dictionary<string, FenceRect> { [config.Inbox.Id] = new("DELL", 0, 0, 1280, 700) },
+            Fences = new Dictionary<string, FenceRect> { [config.Fences[0].Id] = new("DELL", 0, 0, 1280, 700) },
         });
 
         var (_, layout) = LayoutEngine.Resolve(config, [Dell1080]);
@@ -254,11 +254,11 @@ public class LayoutEngineTests
         {
             Layouts = new Dictionary<string, Layout>
             {
-                [fourK] = new() { Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(2560, 1400) }, Fences = new Dictionary<string, FenceRect> { [config.Inbox.Id] = new("DELL", 0, 0, 640, 350) } },
+                [fourK] = new() { Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(2560, 1400) }, Fences = new Dictionary<string, FenceRect> { [config.Fences[0].Id] = new("DELL", 0, 0, 640, 350) } },
                 [fullHd] = new()
                 {
                     Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(1280, 700) },
-                    Fences = new Dictionary<string, FenceRect> { [config.Inbox.Id] = new("DELL", 0, 0, 320, 175), [games.Id] = new("DELL", 640, 350, 256, 140) },
+                    Fences = new Dictionary<string, FenceRect> { [config.Fences[0].Id] = new("DELL", 0, 0, 320, 175), [games.Id] = new("DELL", 640, 350, 256, 140) },
                 },
             },
             LastLayoutFingerprint = fullHd,
@@ -267,7 +267,7 @@ public class LayoutEngineTests
         var (resolved, layout) = LayoutEngine.Resolve(config, [Dell4K]);
 
         Assert.Equal(new FenceRect("DELL", 1280, 700, 512, 280), layout.Fences[games.Id]); // scaled ×2 from the 1080p layout
-        Assert.Equal(new FenceRect("DELL", 0, 0, 640, 350), layout.Fences[config.Inbox.Id]); // the known rect stays exact
+        Assert.Equal(new FenceRect("DELL", 0, 0, 640, 350), layout.Fences[config.Fences[0].Id]); // the known rect stays exact
         Assert.Equal(new FenceRect("DELL", 1280, 700, 512, 280), resolved.Layouts[fourK].Fences[games.Id]);
     }
 
diff --git a/tests/NeoFences.Core.Tests/Library/GameLaunchersTests.cs b/tests/NeoFences.Core.Tests/Library/GameLaunchersTests.cs
new file mode 100644
index 0000000..4ce88de
--- /dev/null
+++ b/tests/NeoFences.Core.Tests/Library/GameLaunchersTests.cs
@@ -0,0 +1,30 @@
+using NeoFences.Core.Library;
+
+namespace NeoFences.Core.Tests.Library;
+
+/// <summary>Which launcher a Desktop game shortcut starts (the Game Library, M12; moved out of Rules in M18).</summary>
+public class GameLaunchersTests
+{
+    [Theory]
+    [InlineData("steam://rungameid/1091500", GameLauncher.Steam)]
+    [InlineData("com.epicgames.launcher://apps/Fortnite?action=launch", GameLauncher.Epic)]
+    [InlineData("uplay://launch/635/0", GameLauncher.Ubisoft)]
+    [InlineData("origin2://game/launch?offerIds=1", GameLauncher.Ea)]
+    [InlineData("battlenet://WoW", GameLauncher.BattleNet)]
+    [InlineData("goggalaxy://openGameView/1207658924", GameLauncher.Gog)]
+    [InlineData(@"D:\SteamLibrary\steamapps\common\Hades\Hades.exe", GameLauncher.Steam)]
+    [InlineData(@"C:\Program Files\Epic Games\Fortnite\FortniteLauncher.exe", GameLauncher.Epic)]
+    [InlineData(@"C:\Program Files (x86)\Steam\steam.exe -applaunch 570", GameLauncher.Steam)]
+    [InlineData(@"C:\Program Files (x86)\Battle.net\Battle.net.exe --exec=""launch WoW""", GameLauncher.BattleNet)]
+    [InlineData(@"C:\Program Files (x86)\GOG Galaxy\Games\Witcher 3\witcher3.exe", GameLauncher.Gog)]
+    public void LauncherOf_KnowsTheLaunchers(string target, GameLauncher expected) => Assert.Equal(expected, GameLaunchers.LauncherOf(target));
+
+    [Theory]
+    [InlineData(null)]
+    [InlineData(@"C:\Windows\notepad.exe")]
+    // The launchers themselves are not games (probe on the user's desktop, 2026-10-03)
+    [InlineData(@"C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe")]
+    [InlineData(@"C:\Program Files (x86)\Steam\Steam.exe")]
+    [InlineData(@"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\UbisoftConnect.exe")]
+    public void LauncherOf_NotAGame(string? target) => Assert.Null(GameLaunchers.LauncherOf(target));
+}
diff --git a/tests/NeoFences.Core.Tests/Library/GameLibraryTests.cs b/tests/NeoFences.Core.Tests/Library/GameLibraryTests.cs
index 012e7a9..4a1a748 100644
--- a/tests/NeoFences.Core.Tests/Library/GameLibraryTests.cs
+++ b/tests/NeoFences.Core.Tests/Library/GameLibraryTests.cs
@@ -248,17 +248,17 @@ public class GameLibraryTests
     public void Config_LibrarySettingsDefaults_AndOneLibraryFenceAtMost()
     {
         var config = ConfigNormalizer.Normalize(ConfigJson.Deserialize("""
-            { "schemaVersion": 1,
-              "fences": [ { "id": "a", "title": "Games", "source": { "kind": "library" } },
-                          { "id": "b", "title": "Games 2", "source": { "kind": "library" } } ],
+            { "schemaVersion": 5,
+              "fences": [ { "id": "a", "title": "Games", "isLibrary": true },
+                          { "id": "b", "title": "Games 2", "isLibrary": true } ],
               "library": { "folders": null, "hidden": [ "steam:431960" ] } }
             """));
 
-        Assert.Equal([FenceSourceKind.Library, FenceSourceKind.Desktop], config.Fences.Where(fence => !fence.IsInbox).Select(fence => fence.Source.Kind));
+        Assert.Equal([true, false], config.Fences.Select(fence => fence.IsLibrary));
         Assert.Empty(config.Library.Folders);
         Assert.True(config.Library.Sources.Steam && config.Library.Sources.DesktopShortcuts);
         Assert.Equal(["steam:431960"], config.Library.Hidden);
-        Assert.Contains("\"kind\": \"library\"", ConfigJson.Serialize(config));
-        Assert.Empty(ConfigNormalizer.Normalize(ConfigJson.Deserialize("""{ "schemaVersion": 1, "fences": [] }""")).Library.Hidden);
+        Assert.Contains("\"isLibrary\": true", ConfigJson.Serialize(config));
+        Assert.Empty(ConfigNormalizer.Normalize(ConfigJson.Deserialize("""{ "schemaVersion": 5, "fences": [] }""")).Library.Hidden);
     }
 }
diff --git a/tests/NeoFences.Core.Tests/Library/LibraryPolishTests.cs b/tests/NeoFences.Core.Tests/Library/LibraryPolishTests.cs
index dd6c8e9..1d038de 100644
--- a/tests/NeoFences.Core.Tests/Library/LibraryPolishTests.cs
+++ b/tests/NeoFences.Core.Tests/Library/LibraryPolishTests.cs
@@ -56,7 +56,7 @@ public class LibraryPolishTests : IDisposable
     [InlineData(@"C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe com.epicgames.launcher://apps/fn?action=launch", GameLauncher.Epic)]
     [InlineData(@"C:\Tools\steam-helper.exe --profile steam", null)]
     public void LauncherOf_FindsALaunchersLinkInTheArguments(string target, GameLauncher? expected) =>
-        Assert.Equal(expected, Rules.LauncherOf(target));
+        Assert.Equal(expected, GameLaunchers.LauncherOf(target));
 
     // ---------- the status line names sources the way people do ----------
 
diff --git a/tests/NeoFences.Core.Tests/Lifecycle/LifecycleTests.cs b/tests/NeoFences.Core.Tests/Lifecycle/LifecycleTests.cs
index 83c31e8..4eece05 100644
--- a/tests/NeoFences.Core.Tests/Lifecycle/LifecycleTests.cs
+++ b/tests/NeoFences.Core.Tests/Lifecycle/LifecycleTests.cs
@@ -7,32 +7,32 @@ public class WatchdogPlanTests
     [Fact]
     public void CleanExit_IconsShown_NothingToDo() =>
         Assert.Equal(new WatchdogPlan(RestoreIcons: false, Restart: WatchdogRestart.Never),
-            WatchdogPlan.For(cleanShutdown: true, sessionEnding: false, takeoverActive: false));
+            WatchdogPlan.For(cleanShutdown: true, sessionEnding: false, iconsHidden: false));
 
     [Fact]
     public void CleanExit_ButIconsStillMarkedHidden_RestoresAnyway()
     {
         // M2a review I1: a clean exit whose own restore failed (Explorer restarting) must not leave icons hidden.
         Assert.Equal(new WatchdogPlan(RestoreIcons: true, Restart: WatchdogRestart.Never),
-            WatchdogPlan.For(cleanShutdown: true, sessionEnding: false, takeoverActive: true));
+            WatchdogPlan.For(cleanShutdown: true, sessionEnding: false, iconsHidden: true));
     }
 
     [Fact]
     public void Crash_WithIconsHidden_RestoresAndRestarts() =>
         Assert.Equal(new WatchdogPlan(RestoreIcons: true, Restart: WatchdogRestart.Throttled),
-            WatchdogPlan.For(cleanShutdown: false, sessionEnding: false, takeoverActive: true));
+            WatchdogPlan.For(cleanShutdown: false, sessionEnding: false, iconsHidden: true));
 
     [Fact]
     public void Crash_WithoutTakeover_RestartsWithoutTouchingIcons() =>
         Assert.Equal(new WatchdogPlan(RestoreIcons: false, Restart: WatchdogRestart.Throttled),
-            WatchdogPlan.For(cleanShutdown: false, sessionEnding: false, takeoverActive: false));
+            WatchdogPlan.For(cleanShutdown: false, sessionEnding: false, iconsHidden: false));
 
     [Fact]
     public void SessionEnding_RestartsOnlyIfTheSessionContinues()
     {
         // ADR-013: WPF exits NeoFences on WM_QUERYENDSESSION; if the user then cancels the shutdown the watchdog brings it back.
         Assert.Equal(new WatchdogPlan(RestoreIcons: true, Restart: WatchdogRestart.IfSessionContinues),
-            WatchdogPlan.For(cleanShutdown: false, sessionEnding: true, takeoverActive: true));
+            WatchdogPlan.For(cleanShutdown: false, sessionEnding: true, iconsHidden: true));
     }
 }
 
diff --git a/tests/NeoFences.Core.Tests/Lifecycle/RunStateTests.cs b/tests/NeoFences.Core.Tests/Lifecycle/RunStateTests.cs
index 2dac69e..402bb54 100644
--- a/tests/NeoFences.Core.Tests/Lifecycle/RunStateTests.cs
+++ b/tests/NeoFences.Core.Tests/Lifecycle/RunStateTests.cs
@@ -7,7 +7,7 @@ public class RunStateTests
     [Fact]
     public void Normal_WithTakeover_ShowsFences_HidesIcons_WantsTheHook()
     {
-        var state = new RunState(Takeover: true, QuickHidden: false, Paused: false, GameMode: false);
+        var state = new RunState(HideIcons: true, QuickHidden: false, Paused: false, GameMode: false);
         Assert.True(state.FencesVisible);
         Assert.True(state.IconsHidden);
         Assert.True(state.MouseHookWanted);
@@ -20,12 +20,12 @@ public class RunStateTests
     [InlineData(false, true)]
     public void PausedOrGaming_ReleasesThePeekHotkey(bool paused, bool gameMode) =>
         // Ctrl+Alt+Space goes back to Windows and the game (M6a review M5).
-        Assert.False(new RunState(Takeover: true, QuickHidden: false, Paused: paused, GameMode: gameMode).PeekHotkeyWanted);
+        Assert.False(new RunState(HideIcons: true, QuickHidden: false, Paused: paused, GameMode: gameMode).PeekHotkeyWanted);
 
     [Fact]
     public void QuickHidden_WithoutTakeover_HidesFencesAndIcons()
     {
-        var state = new RunState(Takeover: false, QuickHidden: true, Paused: false, GameMode: false);
+        var state = new RunState(HideIcons: false, QuickHidden: true, Paused: false, GameMode: false);
         Assert.False(state.FencesVisible);
         Assert.True(state.IconsHidden);
     }
@@ -37,7 +37,7 @@ public class RunStateTests
     public void Paused_ShowsIcons_HidesFences_DropsTheHook(bool takeover, bool quickHidden)
     {
         // Pause gives the desktop back to Windows, whatever else is on (hard rule 2 never depends on Pause).
-        var state = new RunState(Takeover: takeover, QuickHidden: quickHidden, Paused: true, GameMode: false);
+        var state = new RunState(HideIcons: takeover, QuickHidden: quickHidden, Paused: true, GameMode: false);
         Assert.False(state.FencesVisible);
         Assert.False(state.IconsHidden);
         Assert.False(state.MouseHookWanted);
@@ -46,7 +46,7 @@ public class RunStateTests
     [Fact]
     public void GameMode_KeepsFencesAndIcons_DropsTheHook_DefersShellWork()
     {
-        var state = new RunState(Takeover: true, QuickHidden: false, Paused: false, GameMode: true);
+        var state = new RunState(HideIcons: true, QuickHidden: false, Paused: false, GameMode: true);
         Assert.True(state.FencesVisible); // user choice: go idle, fences stay
         Assert.True(state.IconsHidden);
         Assert.False(state.MouseHookWanted);
@@ -57,7 +57,7 @@ public class RunStateTests
     [Fact]
     public void QuickHidden_WithIconsTheUserHid_NeverOwnsThem()
     {
-        var state = new RunState(Takeover: false, QuickHidden: true, Paused: false, GameMode: false, IconsHiddenByUser: true);
+        var state = new RunState(HideIcons: false, QuickHidden: true, Paused: false, GameMode: false, IconsHiddenByUser: true);
         Assert.False(state.IconsHidden); // neither hidden by quick-hide, nor shown at its end, exit or an Explorer restart
         Assert.False(state.FencesVisible);
     }
@@ -66,10 +66,10 @@ public class RunStateTests
     public void QuickHidden_WithIconsNeoFencesHid_StillOwnsThem()
     {
         // Icons left hidden by a failed show (the marker is set): not the user's, so the next quick-hide off retries the show.
-        Assert.True(new RunState(Takeover: false, QuickHidden: true, Paused: false, GameMode: false, IconsHiddenByUser: false).IconsHidden);
+        Assert.True(new RunState(HideIcons: false, QuickHidden: true, Paused: false, GameMode: false, IconsHiddenByUser: false).IconsHidden);
     }
 
     [Fact]
     public void Takeover_HidesIcons_WhateverTheUserDidInExplorer() =>
-        Assert.True(new RunState(Takeover: true, QuickHidden: true, Paused: false, GameMode: false, IconsHiddenByUser: true).IconsHidden);
+        Assert.True(new RunState(HideIcons: true, QuickHidden: true, Paused: false, GameMode: false, IconsHiddenByUser: true).IconsHidden);
 }
diff --git a/tests/NeoFences.Core.Tests/Model/FenceEditsTests.cs b/tests/NeoFences.Core.Tests/Model/FenceEditsTests.cs
index c8ffe89..cc13a70 100644
--- a/tests/NeoFences.Core.Tests/Model/FenceEditsTests.cs
+++ b/tests/NeoFences.Core.Tests/Model/FenceEditsTests.cs
@@ -6,7 +6,7 @@ public class FenceEditsTests
 {
     private static (NeoFencesConfig Config, Fence Games) Sample()
     {
-        var inbox = Fence.Create("Inbox") with { IsInbox = true };
+        var inbox = Fence.Create("Inbox");
         var games = Fence.Create("Games");
         return (new NeoFencesConfig { Fences = [inbox, games] }, games);
     }
diff --git a/tests/NeoFences.Core.Tests/Model/FenceTabsTests.cs b/tests/NeoFences.Core.Tests/Model/FenceTabsTests.cs
index 6830da6..bd52dae 100644
--- a/tests/NeoFences.Core.Tests/Model/FenceTabsTests.cs
+++ b/tests/NeoFences.Core.Tests/Model/FenceTabsTests.cs
@@ -1,6 +1,5 @@
 using NeoFences.Core.Config;
 using NeoFences.Core.Layouts;
-using NeoFences.Core.Membership;
 using NeoFences.Core.Model;
 
 namespace NeoFences.Core.Tests.Model;
@@ -13,8 +12,8 @@ public class FenceTabsTests
 
     private static (NeoFencesConfig Config, Fence Inbox, Fence Games, Fence Tools, Fence Docs) Sample()
     {
-        var inbox = Fence.Create("Inbox") with { IsInbox = true };
-        var games = Fence.Create("Games") with { Items = [@"C:\Desktop\a.lnk"] };
+        var inbox = Fence.Create("Inbox");
+        var games = Fence.Create("Games");
         var tools = Fence.Create("Tools");
         var docs = Fence.Create("Docs");
         var layout = new Layout
@@ -51,7 +50,6 @@ public class FenceTabsTests
         Assert.True(Rects(merged).ContainsKey(games.Id));
         Assert.Equal(games.Id, FenceTabs.HostOf(merged, tools.Id).Id);
         Assert.True(FenceTabs.IsMember(merged, tools.Id));
-        Assert.Equal(Get(config, tools).Items, Get(merged, tools).Items); // items never move
     }
 
     [Fact]
@@ -208,13 +206,12 @@ public class FenceTabsTests
         config = FenceTabs.Merge(config, movingFenceId: tools.Id, targetFenceId: games.Id);
         config = FenceTabs.Merge(config, movingFenceId: docs.Id, targetFenceId: games.Id);
 
-        var memberGone = FenceMembership.DeleteFence(config, tools.Id);
+        var memberGone = FenceEdits.DeleteFence(config, tools.Id);
         Assert.Equal([games.Id, docs.Id], Get(memberGone, games).Tabs);
 
-        var hostGone = FenceMembership.DeleteFence(config, games.Id);
+        var hostGone = FenceEdits.DeleteFence(config, games.Id);
         Assert.Equal([tools.Id, docs.Id], Get(hostGone, tools).Tabs);
         Assert.Equal(Box, Rects(hostGone)[tools.Id]);
-        Assert.Contains(@"C:\Desktop\a.lnk", Get(hostGone, inbox).Items); // items go to the Inbox, as always
     }
 
     [Fact]
diff --git a/tests/NeoFences.Core.Tests/Model/LabelsTests.cs b/tests/NeoFences.Core.Tests/Model/LabelsTests.cs
index 16fd6e3..710a74b 100644
--- a/tests/NeoFences.Core.Tests/Model/LabelsTests.cs
+++ b/tests/NeoFences.Core.Tests/Model/LabelsTests.cs
@@ -1,5 +1,4 @@
 using NeoFences.Core.Config;
-using NeoFences.Core.Membership;
 using NeoFences.Core.Model;
 
 namespace NeoFences.Core.Tests.Model;
@@ -10,7 +9,7 @@ public class LabelsTests
     private static (NeoFencesConfig Config, Fence Games) Sample()
     {
         var games = Fence.Create("Games");
-        return (new NeoFencesConfig { Fences = [Fence.Create("Inbox") with { IsInbox = true }, games] }, games);
+        return (new NeoFencesConfig { Fences = [Fence.Create("Inbox"), games] }, games);
     }
 
     [Fact]
@@ -38,7 +37,7 @@ public class LabelsTests
         var (config, games) = Sample();
         var changed = FenceEdits.SetLabels(config, games.Id, LabelMode.OnHover);
         Assert.Equal(LabelMode.OnHover, changed.Fences.Single(fence => fence.Id == games.Id).Labels);
-        Assert.Equal(LabelMode.Always, changed.Inbox.Labels);
+        Assert.Equal(LabelMode.Always, changed.Fences[0].Labels);
     }
 
     [Fact]
@@ -51,12 +50,11 @@ public class LabelsTests
     }
 
     [Fact]
-    public void NewFencesAndPortals_TakeTheDefault()
+    public void NewFences_TakeTheDefault()
     {
         var (config, _) = Sample();
         config = config with { Settings = config.Settings with { DefaultLabels = LabelMode.OnHover } };
-        Assert.Equal(LabelMode.OnHover, FenceMembership.CreateFence(config, "Tools").Fence.Labels);
-        Assert.Equal(LabelMode.OnHover, FenceMembership.CreatePortal(config, title: "Shots", folderPath: @"D:\Shots").Fence.Labels);
+        Assert.Equal(LabelMode.OnHover, FenceEdits.CreateFence(config, "Tools").Fence.Labels);
     }
 
     [Fact]
diff --git a/tests/NeoFences.Core.Tests/Model/M8cCoreTests.cs b/tests/NeoFences.Core.Tests/Model/M8cCoreTests.cs
new file mode 100644
index 0000000..419f876
--- /dev/null
+++ b/tests/NeoFences.Core.Tests/Model/M8cCoreTests.cs
@@ -0,0 +1,50 @@
+using NeoFences.Core.Config;
+using NeoFences.Core.Layouts;
+using NeoFences.Core.Model;
+
+namespace NeoFences.Core.Tests.Model;
+
+/// <summary>M8c carry-overs in Core: drop rows, stable monitor numbering, label validation.</summary>
+public class M8cCoreTests
+{
+    [Fact]
+    public void DropInsertIndex_UsesTheWholeRow_NotTheShortItem()
+    {
+        // Row 1: a tall item (two-line label) and a short one; row 2 starts at 80 (M3b review).
+        (double, double, double, double)[] cells = [(0, 0, 76, 80), (76, 0, 76, 50), (0, 80, 76, 50)];
+
+        // Under the short item's label but inside the row, left of its centre: before it.
+        Assert.Equal(1, DropZones.InsertIndex(cells, pointX: 90, pointY: 65));
+        // Right of its centre: after it (before row 2).
+        Assert.Equal(2, DropZones.InsertIndex(cells, pointX: 140, pointY: 65));
+        // Past the last row: at the end.
+        Assert.Equal(3, DropZones.InsertIndex(cells, pointX: 10, pointY: 200));
+        Assert.Equal(0, DropZones.InsertIndex([], pointX: 10, pointY: 10));
+    }
+
+    [Fact]
+    public void UniqueDeviceIds_NumbersDuplicatesInAStableOrder()
+    {
+        // Enumeration order can change between boots; numbering follows the GDI device name instead (M8a review).
+        Assert.Equal(["DELL#2", "LG", "DELL"],
+            DisplayFingerprint.UniqueDeviceIds(["DELL", "LG", "DELL"], orderKeys: [@"\\.\DISPLAY3", @"\\.\DISPLAY2", @"\\.\DISPLAY1"]));
+        Assert.Equal(["DELL", "DELL#2"], DisplayFingerprint.UniqueDeviceIds(["DELL", "DELL"]));
+    }
+
+    [Fact]
+    public void Normalizer_RepairsUnknownLabelModes()
+    {
+        // A hand-edited "labels": 2 would hide names with no way to show them (M8b review).
+        var games = Fence.Create("Games") with { Labels = (LabelMode)2 };
+        var config = new NeoFencesConfig
+        {
+            Fences = [Fence.Create("Other"), games],
+            Settings = new Settings { DefaultLabels = (LabelMode)7 },
+        };
+
+        var normalized = ConfigNormalizer.Normalize(config);
+
+        Assert.Equal(LabelMode.Always, normalized.Fences.Single(fence => fence.Id == games.Id).Labels);
+        Assert.Equal(LabelMode.Always, normalized.Settings.DefaultLabels);
+    }
+}
diff --git a/tests/NeoFences.Core.Tests/Model/NeoFencesConfigTests.cs b/tests/NeoFences.Core.Tests/Model/NeoFencesConfigTests.cs
index 039ccfe..b7f7623 100644
--- a/tests/NeoFences.Core.Tests/Model/NeoFencesConfigTests.cs
+++ b/tests/NeoFences.Core.Tests/Model/NeoFencesConfigTests.cs
@@ -5,22 +5,19 @@ namespace NeoFences.Core.Tests.Model;
 public class NeoFencesConfigTests
 {
     [Fact]
-    public void Default_HasExactlyOneEmptyInbox()
+    public void Default_HasExactlyOneOrdinaryFence()
     {
-        var config = NeoFencesConfig.CreateDefault();
-
-        var inbox = Assert.Single(config.Fences);
-        Assert.True(inbox.IsInbox);
-        Assert.Equal("Inbox", inbox.Title);
-        Assert.Empty(inbox.Items);
-        Assert.Same(inbox, config.Inbox);
+        // First run (M18 spec §5): one empty fence; its hint says how to fill it. No Inbox, no auto-fill.
+        var fence = Assert.Single(NeoFencesConfig.CreateDefault().Fences);
+        Assert.Equal("Fence", fence.Title);
+        Assert.False(fence.IsLibrary);
     }
 
     [Fact]
-    public void Default_TakeoverIsOff_UntilSignOutTestPasses()
+    public void Default_KeepsTheDesktopIconsVisible()
     {
-        // ADR-011: Takeover must not ship enabled by default until C8 passes.
-        Assert.False(NeoFencesConfig.CreateDefault().Settings.Takeover);
+        // ADR-040: native desktop icons stay visible unless the user switches "Hide desktop icons" on.
+        Assert.False(NeoFencesConfig.CreateDefault().Settings.HideDesktopIcons);
     }
 
     [Fact]
@@ -44,18 +41,11 @@ public class NeoFencesConfigTests
     public void WithFence_ReplacesByIdAndKeepsOrder()
     {
         var games = Fence.Create("Games");
-        var config = NeoFencesConfig.CreateDefault() with { Fences = [NeoFencesConfig.CreateDefault().Inbox, games] };
+        var config = NeoFencesConfig.CreateDefault() with { Fences = [Fence.Create("Tools"), games] };
 
         var updated = config.WithFence(games with { Title = "Games 2" });
 
-        Assert.Equal(["Inbox", "Games 2"], updated.Fences.Select(fence => fence.Title));
+        Assert.Equal(["Tools", "Games 2"], updated.Fences.Select(fence => fence.Title));
         Assert.Equal("Games", config.Fences[1].Title); // original untouched
     }
-
-    [Fact]
-    public void Default_TakeoverPromptNotAnsweredYet()
-    {
-        // M2b first run: the Inbox asks once whether to hide desktop icons (user decision 2026-10-02).
-        Assert.False(NeoFencesConfig.CreateDefault().Settings.TakeoverPromptAnswered);
-    }
 }
diff --git a/tests/NeoFences.Core.Tests/Model/SnapshotsTests.cs b/tests/NeoFences.Core.Tests/Model/SnapshotsTests.cs
index fd4776e..53f8746 100644
--- a/tests/NeoFences.Core.Tests/Model/SnapshotsTests.cs
+++ b/tests/NeoFences.Core.Tests/Model/SnapshotsTests.cs
@@ -1,11 +1,12 @@
 using NeoFences.Core.Config;
+using NeoFences.Core.Items;
 using NeoFences.Core.Layouts;
 using NeoFences.Core.Model;
 using NeoFences.Core.Tests.TestSupport;
 
 namespace NeoFences.Core.Tests.Model;
 
-/// <summary>Snapshots (M10, spec 2026-10-03-snapshots-design): save an arrangement by name, restore it later.</summary>
+/// <summary>Snapshots (M10, spec 2026-10-03-snapshots-design; M18: with the virtual items): save an arrangement, restore it later.</summary>
 public class SnapshotsTests
 {
     private const string Desktop = @"C:\Users\cipher\Desktop\";
@@ -13,47 +14,53 @@ public class SnapshotsTests
     private static readonly DateTimeOffset Taken = new(2026, 10, 3, 22, 45, 10, TimeSpan.FromHours(5.5));
     private static readonly FenceRect Box = new("mon", 100, 100, 320, 220);
 
-    private static (NeoFencesConfig Config, Fence Inbox, Fence Games, Fence Tools) Sample()
+    private static VirtualItem Item(string id, string target, string? name = null) => new() { Id = id, Target = target, Name = name };
+
+    private static (NeoFencesConfig Config, ItemsDocument Items, Fence Notes, Fence Games, Fence Tools) Sample()
     {
-        var inbox = Fence.Create("Inbox") with { IsInbox = true, Items = [Desktop + "notes.txt"] };
-        var games = Fence.Create("Games") with { Items = [Desktop + "a.lnk", Desktop + "b.lnk"], IconSize = 64 };
-        var tools = Fence.Create("Tools") with { Items = [Desktop + "t.lnk"], TabColor = TabColor.Blue };
+        var notes = Fence.Create("Notes");
+        var games = Fence.Create("Games") with { IconSize = 64 };
+        var tools = Fence.Create("Tools") with { TabColor = TabColor.Blue };
         var layout = new Layout
         {
             Monitors = new Dictionary<string, MonitorArea> { ["mon"] = new(1920, 1040) },
-            Fences = new Dictionary<string, FenceRect> { [inbox.Id] = Box with { X = 1000 }, [games.Id] = Box, [tools.Id] = Box with { X = 500 } },
+            Fences = new Dictionary<string, FenceRect> { [notes.Id] = Box with { X = 1000 }, [games.Id] = Box, [tools.Id] = Box with { X = 500 } },
         };
         var config = new NeoFencesConfig
         {
             Settings = new Settings { PeekHotkey = "Ctrl+Alt+P" },
-            Fences = [inbox, games, tools],
+            Fences = [notes, games, tools],
             Layouts = new Dictionary<string, Layout> { [Setup] = layout },
             LastLayoutFingerprint = Setup,
         };
-        return (config, inbox, games, tools);
+        var items = new ItemsDocument()
+            .With(notes.Id, [Item("n", Desktop + "notes.txt")])
+            .With(games.Id, [Item("a", Desktop + "a.lnk", name: "Alpha"), Item("b", Desktop + "b.lnk")])
+            .With(tools.Id, [Item("t", Desktop + "t.lnk")]);
+        return (config, items, notes, games, tools);
     }
 
     private static Fence Get(NeoFencesConfig config, Fence fence) => config.Fences.Single(candidate => candidate.Id == fence.Id);
 
-    private static string[] DesktopOf(NeoFencesConfig config) =>
-        config.Fences.SelectMany(fence => fence.Items).ToArray();
+    private static string[] Ids(ItemsDocument items, Fence fence) => [.. items.Of(fence.Id).Select(item => item.Id)];
 
     [Fact]
-    public void TakeThenRestore_BringsBackFencesPlacesAndIcons()
+    public void TakeThenRestore_BringsBackFencesPlacesAndItems()
     {
-        var (config, inbox, games, tools) = Sample();
-        var snapshot = Snapshots.Take(config, name: "Clean desk", now: Taken);
+        var (config, items, notes, games, tools) = Sample();
+        var snapshot = Snapshots.Take(config, items, name: "Clean desk", now: Taken);
 
-        // Shuffled: Tools merged into Games as a tab, b.lnk moved to the Inbox, Games moved and resized.
+        // Shuffled: Tools merged into Games as a tab, b moved to Notes, a renamed, Games moved and resized.
         var shuffled = FenceTabs.Merge(config, movingFenceId: tools.Id, targetFenceId: games.Id);
-        shuffled = shuffled.WithFence(Get(shuffled, games) with { Items = [Desktop + "a.lnk"] })
-                           .WithFence(Get(shuffled, inbox) with { Items = [Desktop + "notes.txt", Desktop + "b.lnk"] });
         shuffled = LayoutEngine.WithFenceRect(shuffled, Setup, games.Id, Box with { X = 1400, W = 500 });
+        var shuffledItems = ItemEdits.Move(items, ["b"], notes.Id, insertAt: 1);
+        shuffledItems = ItemEdits.Replace(shuffledItems, Item("a", Desktop + "a.lnk", name: "Renamed"));
 
-        var restored = Snapshots.Restore(shuffled, snapshot, desktopNow: DesktopOf(config));
+        var (restored, restoredItems) = Snapshots.Restore(shuffled, snapshot);
 
-        Assert.Equal([Desktop + "a.lnk", Desktop + "b.lnk"], Get(restored, games).Items);
-        Assert.Equal([Desktop + "notes.txt"], Get(restored, inbox).Items);
+        Assert.Equal(["a", "b"], Ids(restoredItems, games));
+        Assert.Equal(["n"], Ids(restoredItems, notes));
+        Assert.Equal("Alpha", restoredItems.Find("a")!.Name);
         Assert.Empty(Get(restored, games).Tabs); // the tab merge is undone
         Assert.Equal(TabColor.Blue, Get(restored, tools).TabColor);
         Assert.Equal(64, Get(restored, games).IconSize);
@@ -62,29 +69,27 @@ public class SnapshotsTests
     }
 
     [Fact]
-    public void Restore_DropsDeletedIcons_AndKeepsNewOnesWhereTheyAre()
+    public void Restore_FencesMadeAfterTheSnapshotGo_WithTheirItems()
     {
-        var (config, inbox, games, tools) = Sample();
-        var snapshot = Snapshots.Take(config, name: "Clean desk", now: Taken);
-        // Since then: b.lnk deleted; new.lnk appeared in Tools; drop.zip appeared in a fence made after the snapshot.
-        var later = Fence.Create("Later") with { Items = [Desktop + "drop.zip"] };
+        var (config, items, _, games, _) = Sample();
+        var snapshot = Snapshots.Take(config, items, name: "Clean desk", now: Taken);
+        var later = Fence.Create("Later");
         var now = config with { Fences = [.. config.Fences, later] };
-        now = now.WithFence(Get(now, tools) with { Items = [Desktop + "t.lnk", Desktop + "new.lnk"] });
-        string[] desktopNow = [Desktop + "notes.txt", Desktop + "a.lnk", Desktop + "t.lnk", Desktop + "new.lnk", Desktop + "drop.zip"];
+        var nowItems = items.With(later.Id, [Item("z", Desktop + "drop.zip")]);
 
-        var restored = Snapshots.Restore(now, snapshot, desktopNow);
+        var (restored, restoredItems) = Snapshots.Restore(now, snapshot);
 
-        Assert.Equal([Desktop + "a.lnk"], Get(restored, games).Items); // b.lnk is gone
-        Assert.Equal([Desktop + "t.lnk", Desktop + "new.lnk"], Get(restored, tools).Items); // stays in its surviving fence
-        Assert.DoesNotContain(restored.Fences, fence => fence.Id == later.Id); // made after the snapshot
-        Assert.Equal([Desktop + "notes.txt", Desktop + "drop.zip"], Get(restored, inbox).Items); // its fence is gone: Inbox
+        Assert.DoesNotContain(restored.Fences, fence => fence.Id == later.Id);
+        Assert.Null(restoredItems.Find("z"));
+        Assert.Equal(["a", "b"], Ids(restoredItems, games));
+        Assert.Single(nowItems.Of(later.Id)); // the current document itself is untouched
     }
 
     [Fact]
     public void Restore_KeepsSettingsAndOtherSetups()
     {
-        var (config, _, games, _) = Sample();
-        var snapshot = Snapshots.Take(config, name: "Clean desk", now: Taken);
+        var (config, items, _, games, _) = Sample();
+        var snapshot = Snapshots.Take(config, items, name: "Clean desk", now: Taken);
         var otherSetup = new Layout { Monitors = new Dictionary<string, MonitorArea> { ["dock"] = new(2560, 1400) },
             Fences = new Dictionary<string, FenceRect> { [games.Id] = new("dock", 10, 10, 300, 200) } };
         var now = config with
@@ -94,7 +99,7 @@ public class SnapshotsTests
             LastLayoutFingerprint = "2mon:dock",
         };
 
-        var restored = Snapshots.Restore(now, snapshot, desktopNow: DesktopOf(config));
+        var (restored, _) = Snapshots.Restore(now, snapshot);
 
         Assert.Equal("Alt+F9", restored.Settings.PeekHotkey);
         Assert.Equal("2mon:dock", restored.LastLayoutFingerprint);
@@ -103,16 +108,24 @@ public class SnapshotsTests
     }
 
     [Fact]
-    public void Restore_ADamagedSnapshotWithoutAnInbox_StillHasOne()
+    public void Restore_ADamagedSnapshot_DropsItemsOfFencesItDoesNotHave_AndRepairsIds()
     {
-        var (config, _, games, _) = Sample();
-        var snapshot = Snapshots.Take(config, name: "x", now: Taken) with { Fences = [Get(config, games)] };
+        var (config, items, _, games, _) = Sample();
+        var snapshot = Snapshots.Take(config, items, name: "x", now: Taken) with
+        {
+            Fences = [Get(config, games)],
+            Items = new Dictionary<string, IReadOnlyList<VirtualItem>>
+            {
+                [games.Id] = [Item("same", Desktop + "a.lnk"), Item("same", Desktop + "b.lnk")],
+                ["gone"] = [Item("g", Desktop + "g.lnk")],
+            },
+        };
 
-        var restored = Snapshots.Restore(config, snapshot, desktopNow: DesktopOf(config));
+        var (restored, restoredItems) = Snapshots.Restore(config, snapshot);
 
-        Assert.Single(restored.Fences, fence => fence.IsInbox);
-        Assert.Contains(Desktop + "notes.txt", restored.Inbox.Items);
-        Assert.Contains(Desktop + "t.lnk", restored.Inbox.Items); // Tools is not in this snapshot
+        Assert.Equal(games.Id, Assert.Single(restored.Fences).Id);
+        Assert.Equal(2, restoredItems.Of(games.Id).Select(item => item.Id).Distinct().Count());
+        Assert.Equal([games.Id], restoredItems.Fences.Keys);
     }
 
     [Fact]
@@ -120,17 +133,18 @@ public class SnapshotsTests
     {
         using var folder = new TempDirectory();
         var store = new SnapshotStore(folder.Path);
-        var (config, _, games, _) = Sample();
+        var (config, items, _, games, _) = Sample();
 
-        var older = store.Save(Snapshots.Take(config, name: "Older", now: Taken));
-        var newer = store.Save(Snapshots.Take(config, name: "Newer", now: Taken.AddMinutes(5)));
-        var sameSecond = store.Save(Snapshots.Take(config, name: "Same second", now: Taken.AddMinutes(5)));
+        var older = store.Save(Snapshots.Take(config, items, name: "Older", now: Taken));
+        var newer = store.Save(Snapshots.Take(config, items, name: "Newer", now: Taken.AddMinutes(5)));
+        var sameSecond = store.Save(Snapshots.Take(config, items, name: "Same second", now: Taken.AddMinutes(5)));
 
         Assert.NotNull(older);
         Assert.NotEqual(newer, sameSecond); // two in the same second get different files
         Assert.Equal(3, store.List().Count);
         Assert.Equal("Older", store.List()[^1].Name); // newest first
         Assert.Equal(64, store.Load(older!)!.Fences.Single(fence => fence.Id == games.Id).IconSize);
+        Assert.Equal("Alpha", store.Load(older!)!.Items[games.Id][0].Name);
 
         Assert.True(store.Rename(older!, "Clean desk"));
         Assert.Equal("Clean desk", store.Load(older!)!.Name);
@@ -142,8 +156,8 @@ public class SnapshotsTests
     {
         using var folder = new TempDirectory();
         var store = new SnapshotStore(folder.Path);
-        var (config, _, _, _) = Sample();
-        store.Save(Snapshots.Take(config, name: "Good", now: Taken));
+        var (config, items, _, _, _) = Sample();
+        store.Save(Snapshots.Take(config, items, name: "Good", now: Taken));
         File.WriteAllText(folder.File("snapshot-broken.json"), "{ not json");
         File.WriteAllText(folder.File("snapshot-half.json.tmp"), "{ \"name\": \"Half");
 
@@ -159,10 +173,10 @@ public class SnapshotsTests
     {
         using var folder = new TempDirectory();
         var store = new SnapshotStore(folder.Path);
-        var (config, _, _, _) = Sample();
+        var (config, items, _, _, _) = Sample();
 
-        store.Save(Snapshots.Take(config, name: "Before restore 1", now: Taken), SnapshotStore.BeforeRestoreFileName);
-        store.Save(Snapshots.Take(config, name: "Before restore 2", now: Taken.AddMinutes(1)), SnapshotStore.BeforeRestoreFileName);
+        store.Save(Snapshots.Take(config, items, name: "Before restore 1", now: Taken), SnapshotStore.BeforeRestoreFileName);
+        store.Save(Snapshots.Take(config, items, name: "Before restore 2", now: Taken.AddMinutes(1)), SnapshotStore.BeforeRestoreFileName);
 
         Assert.Equal(["Before restore 2"], store.List().Select(entry => entry.Name));
         Assert.True(store.List()[0].IsBeforeRestore);
@@ -173,11 +187,11 @@ public class SnapshotsTests
     {
         using var folder = new TempDirectory();
         var store = new SnapshotStore(folder.Path);
-        var (config, _, _, _) = Sample();
-        var path = store.Save(Snapshots.Take(config, name: "Before restore 1", now: Taken), SnapshotStore.BeforeRestoreFileName)!;
+        var (config, items, _, _, _) = Sample();
+        var path = store.Save(Snapshots.Take(config, items, name: "Before restore 1", now: Taken), SnapshotStore.BeforeRestoreFileName)!;
 
         Assert.True(store.Rename(path, "Good layout"));
-        store.Save(Snapshots.Take(config, name: "Before restore 2", now: Taken.AddMinutes(1)), SnapshotStore.BeforeRestoreFileName);
+        store.Save(Snapshots.Take(config, items, name: "Before restore 2", now: Taken.AddMinutes(1)), SnapshotStore.BeforeRestoreFileName);
 
         // final review I4: the renamed one is kept, not overwritten by the next restore
         Assert.Equal([("Before restore 2", true), ("Good layout", false)], store.List().Select(entry => (entry.Name, entry.IsBeforeRestore)));
diff --git a/tests/NeoFences.Core.Tests/Model/UxCarryOverTests.cs b/tests/NeoFences.Core.Tests/Model/UxCarryOverTests.cs
index 9783561..d2b08c6 100644
--- a/tests/NeoFences.Core.Tests/Model/UxCarryOverTests.cs
+++ b/tests/NeoFences.Core.Tests/Model/UxCarryOverTests.cs
@@ -35,7 +35,7 @@ public class UxCarryOverTests
     {
         var first = Fence.Create("A");
         var second = Fence.Create("B");
-        var merged = FenceTabs.Merge(new NeoFencesConfig { Fences = [Fence.Create("Inbox") with { IsInbox = true }, first, second] }, second.Id, first.Id, wholeBox: false);
+        var merged = FenceTabs.Merge(new NeoFencesConfig { Fences = [Fence.Create("Inbox"), first, second] }, second.Id, first.Id, wholeBox: false);
 
         Assert.Same(merged, FenceTabs.Reorder(merged, second.Id, 1));
         var coloured = FenceTabs.SetColor(merged, second.Id, TabColor.Blue);
diff --git a/tests/NeoFences.Core.Tests/Updates/UpdatePolicyTests.cs b/tests/NeoFences.Core.Tests/Updates/UpdatePolicyTests.cs
index 87d856a..5ee9070 100644
--- a/tests/NeoFences.Core.Tests/Updates/UpdatePolicyTests.cs
+++ b/tests/NeoFences.Core.Tests/Updates/UpdatePolicyTests.cs
@@ -44,13 +44,13 @@ public class UpdatePolicyTests
     }
 
     [Fact]
-    public void Config_AutoUpdateIsOnByDefault_SchemaFour_AndSurvivesARoundTrip()
+    public void Config_AutoUpdateIsOnByDefault_AndSurvivesARoundTrip()
     {
-        Assert.Equal(4, NeoFencesConfig.CurrentSchemaVersion);
+        Assert.Equal(5, NeoFencesConfig.CurrentSchemaVersion); // 4 brought the switch (M17); 5 the virtual items (M18)
         Assert.True(new Settings().AutoUpdate);
         var old = ConfigNormalizer.Normalize(ConfigJson.Deserialize("""{ "schemaVersion": 3, "fences": [] }"""));
         Assert.True(old.Settings.AutoUpdate);
-        Assert.Equal(4, old.SchemaVersion);
+        Assert.Equal(5, old.SchemaVersion);
         var off = old with { Settings = old.Settings with { AutoUpdate = false } };
         Assert.False(ConfigNormalizer.Normalize(ConfigJson.Deserialize(ConfigJson.Serialize(off))).Settings.AutoUpdate);
     }
````

- [ ] **Step 2: Run them to see them fail.**
  Run: `dotnet test tests/NeoFences.Core.Tests`
  Expected: build FAILS with `error CS0246: The type or namespace name 'ItemStore' could not be found` and
  `error CS0103: The name 'GameLauncher' does not exist in the current context` (the compiler stops at these; the
  model errors appear once they are gone).

- [ ] **Step 3: Implement** — remove the parked model, then apply the patch:

```bash
C=src/NeoFences.Core
git rm -q $C/Membership/DesktopChange.cs $C/Membership/FenceMembership.cs $C/Membership/RememberedPlacement.cs \
  $C/Model/FileNames.cs $C/Model/Rules.cs
```

````diff
diff --git a/src/NeoFences.Core/Config/ConfigJson.cs b/src/NeoFences.Core/Config/ConfigJson.cs
index 6f8ac9c..22d66a9 100644
--- a/src/NeoFences.Core/Config/ConfigJson.cs
+++ b/src/NeoFences.Core/Config/ConfigJson.cs
@@ -19,11 +19,8 @@ public static class ConfigJson
         // Hand-edited file, never embedded in HTML: write + and & as-is instead of + and &.
         Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
         DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
-        // Rule enums first (M11 final review I3): an unknown name in a hand-edited rule disables that rule, never the file.
         Converters =
         {
-            new LenientEnumConverter<RuleKind>(), new LenientEnumConverter<TypeGroup>(), new LenientEnumConverter<GameLauncher>(),
-            new LenientEnumConverter<RuleCompare>(),
             // Appearance enums (M14): a hand-edited style or weight is repaired by the normalizer, never fails the file.
             new LenientEnumConverter<ColourStyle>(), new LenientEnumConverter<TitleWeight>(),
             new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
@@ -39,6 +36,13 @@ public static class ConfigJson
     public static Snapshot DeserializeSnapshot(string json) =>
         JsonSerializer.Deserialize<Snapshot>(json, Options) ?? throw new JsonException("the snapshot file contains null");
 
+    /// <summary>items.json (M18, ADR-041): the same names and leniency as config.json.</summary>
+    public static string SerializeItems(Items.ItemsDocument document) => JsonSerializer.Serialize(document, Options);
+
+    /// <exception cref="JsonException">The text is not an items document.</exception>
+    public static Items.ItemsDocument DeserializeItems(string json) =>
+        JsonSerializer.Deserialize<Items.ItemsDocument>(json, Options) ?? throw new JsonException("items.json contains null");
+
     /// <summary>The library folder's index (M12): the same names and enums as config.json.</summary>
     public static string SerializeLibrary(Library.LibraryState state) => JsonSerializer.Serialize(state, Options);
 
@@ -54,8 +58,8 @@ public static class ConfigJson
 }
 
 /// <summary>
-/// A camelCase enum that reads an unknown name (or anything else odd) as an undefined value instead of failing, so
-/// <see cref="Rules.Repair"/> can disable the rule (M11 final review I3). Undefined values are written as numbers.
+/// A camelCase enum that reads an unknown name (or anything else odd) as an undefined value instead of failing, so the
+/// normalizer can repair it (M11 final review I3, M14). Undefined values are written as numbers.
 /// </summary>
 internal sealed class LenientEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
 {
diff --git a/src/NeoFences.Core/Config/ConfigNormalizer.cs b/src/NeoFences.Core/Config/ConfigNormalizer.cs
index 4b7e7ce..42c7049 100644
--- a/src/NeoFences.Core/Config/ConfigNormalizer.cs
+++ b/src/NeoFences.Core/Config/ConfigNormalizer.cs
@@ -3,9 +3,9 @@ using NeoFences.Core.Model;
 namespace NeoFences.Core.Config;
 
 /// <summary>
-/// Repairs a loaded (possibly hand-edited) config without losing items: exactly one Inbox, unique fence
-/// ids, no nulls, supported icon sizes and enum values, each desktop item in at most one fence, no items on
-/// portal fences, the Inbox always a desktop fence, and layouts free of null or non-finite entries.
+/// Repairs a loaded (possibly hand-edited) config: unique fence ids, no nulls, supported icon sizes and enum values, at
+/// most one Game Library fence, consistent tabs, and layouts free of null or non-finite entries. Items are items.json's
+/// (ItemEdits.Repair).
 /// </summary>
 public static class ConfigNormalizer
 {
@@ -24,50 +24,32 @@ public static class ConfigNormalizer
         };
 
         var seenFenceIds = new HashSet<string>(StringComparer.Ordinal);
-        var seenItems = new HashSet<string>(ItemRef.Comparer);
-        var inboxFound = false;
         var libraryFound = false; // M12: at most one Game Library fence
         var fences = new List<Fence>();
 
         foreach (var loadedFence in config.Fences ?? [])
         {
             if (loadedFence is null) continue;
-            var isInbox = loadedFence.IsInbox && !inboxFound;
-            var source = loadedFence.Source is { } loadedSource && Enum.IsDefined(loadedSource.Kind) ? loadedSource : FenceSource.Desktop;
-            if (source.Kind == FenceSourceKind.Library && (libraryFound || loadedFence.IsInbox)) source = FenceSource.Desktop;
-            libraryFound |= source.Kind == FenceSourceKind.Library;
             var fence = loadedFence with
             {
                 Id = string.IsNullOrWhiteSpace(loadedFence.Id) || !seenFenceIds.Add(loadedFence.Id) ? Fence.NewId() : loadedFence.Id,
                 Title = loadedFence.Title ?? "",
-                Source = isInbox ? FenceSource.Desktop : source,
-                IsInbox = isInbox,
-                Sort = Enum.IsDefined(loadedFence.Sort) ? loadedFence.Sort : FenceSort.Manual,
+                IsLibrary = loadedFence.IsLibrary && !libraryFound,
                 Labels = Enum.IsDefined(loadedFence.Labels) ? loadedFence.Labels : LabelMode.Always, // a hand-edited number (M8b review)
                 Tabs = loadedFence.Tabs ?? [], // a hand-edited "tabs": null (M9 final review)
                 IconSize = IconSizes.Contains(loadedFence.IconSize) ? loadedFence.IconSize : 48,
                 CustomColor = Appearance.Argb.FromHex(loadedFence.CustomColor)?.ToHex(), // M14: a broken colour is none
             };
             seenFenceIds.Add(fence.Id);
-            inboxFound |= fence.IsInbox;
-
-            var items = fence.Source.Kind == FenceSourceKind.Desktop
-                ? (loadedFence.Items ?? []).Where(itemRef => !string.IsNullOrWhiteSpace(itemRef) && seenItems.Add(itemRef)).ToList()
-                : [];
-            fences.Add(fence with { Items = items });
-        }
-
-        if (!inboxFound)
-        {
-            fences.Insert(0, Fence.Create("Inbox") with { IsInbox = true });
+            libraryFound |= fence.IsLibrary;
+            fences.Add(fence);
         }
 
         return config with
         {
-            SchemaVersion = NeoFencesConfig.CurrentSchemaVersion, // an older file is saved in today's format (M13a)
+            SchemaVersion = NeoFencesConfig.CurrentSchemaVersion,
             Settings = settings,
             Fences = FenceTabs.Repair(fences), // M9: one consistent box per tab
-            Rules = UniqueIds((config.Rules ?? []).Where(rule => rule is not null).Select(Rules.Repair)), // M11: a broken rule is disabled
             Layouts = NormalizeLayouts(config.Layouts),
             Library = NormalizeLibrary(config.Library),
         };
@@ -91,13 +73,6 @@ public static class ConfigNormalizer
         };
     }
 
-    /// <summary>A rule block copied in a hand edit keeps its id: the copies get new ones, or one action would hit all (M13a).</summary>
-    private static List<Rule> UniqueIds(IEnumerable<Rule> rules)
-    {
-        var seen = new HashSet<string>(StringComparer.Ordinal);
-        return rules.Select(rule => seen.Add(rule.Id) ? rule : rule with { Id = Guid.NewGuid().ToString("N") }).ToList();
-    }
-
     private static LibrarySettings NormalizeLibrary(LibrarySettings? library) => new()
     {
         Folders = (library?.Folders ?? []).Where(folder => !string.IsNullOrWhiteSpace(folder)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
diff --git a/src/NeoFences.Core/Config/ConfigStore.cs b/src/NeoFences.Core/Config/ConfigStore.cs
index a063027..07fe0a3 100644
--- a/src/NeoFences.Core/Config/ConfigStore.cs
+++ b/src/NeoFences.Core/Config/ConfigStore.cs
@@ -1,84 +1,64 @@
-using System.Text.Json;
 using NeoFences.Core.Model;
 
 namespace NeoFences.Core.Config;
 
-public enum ConfigLoadSource { Primary, Backup, DailyBackup, Fresh }
-
 /// <param name="CorruptCopyPath">Where an unreadable config.json was preserved, if it was.</param>
 /// <param name="IsReadOnly">
 /// True when config.json must not be overwritten this session: it was written by a newer NeoFences, or it
 /// could not be read (locked by antivirus, OneDrive or an editor). The config returned is the best fallback.
 /// </param>
-public sealed record ConfigLoadResult(NeoFencesConfig Config, ConfigLoadSource Source, string? CorruptCopyPath, bool IsReadOnly);
+public sealed record ConfigLoadResult(NeoFencesConfig Config, ConfigLoadSource Source, string? CorruptCopyPath, bool IsReadOnly)
+{
+    /// <summary>
+    /// True when the config's fences are the user's real ones (read from a file, writable). A fresh or read-only fallback
+    /// does not know them: item lists of fences it lacks must then be kept, not dropped at save (ADR-041).
+    /// </summary>
+    public bool KnowsTheFences => Source != ConfigLoadSource.Fresh && !IsReadOnly;
+}
 
 /// <summary>
-/// Loads and saves <c>config.json</c> (ADR-006): atomic replace with <c>.bak</c>, one backup per day
-/// (newest 10 kept), and a recovery chain for corrupt files. Debouncing saves is the caller's job.
+/// Loads and saves <c>config.json</c> (ADR-006): atomic replace with <c>.bak</c>, one backup per day (newest 10 kept),
+/// and a recovery chain for corrupt files (<see cref="JsonStore{T}"/>). A config from before the virtual items (schema
+/// &lt; 5, ADR-040) is not migrated: NeoFences starts fresh, and the old file stays in <c>backups\</c>. Debouncing saves
+/// is the caller's job.
 /// </summary>
-public sealed class ConfigStore(string directory, TimeProvider? timeProvider = null)
+public sealed class ConfigStore
 {
     public const string FileName = "config.json";
-    public const int DailyBackupsKept = 10;
+    public const int DailyBackupsKept = JsonStore<NeoFencesConfig>.DailyBackupsKept;
+
+    /// <summary>The first schema of the virtual items (M18): older configs describe Desktop membership, Portals and rules.</summary>
+    public const int FirstVirtualItemsSchema = 5;
 
     /// <summary>
-    /// One copy of the last config from before schema 2, kept in <c>backups\</c> for good (M13b): daily backups rotate out
-    /// after 10 days, and NeoFences ≤ 1.5 can only read schema 1. Not a "config-*.json" name: never picked or pruned as a daily.
+    /// One copy of the last config from before the current schema, kept in <c>backups\</c> for good (M13b): daily backups
+    /// rotate out after 10 days. Not a "config-*.json" name: never picked or pruned as a daily.
     /// </summary>
     public static string PreviousSchemaCopyName => $"pre-schema-{NeoFencesConfig.CurrentSchemaVersion}-config.json";
 
-    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
-    private bool _saveBlocked;
-
-    public string ConfigPath => Path.Combine(directory, FileName);
-    public string BackupPath => ConfigPath + ".bak";
-    public string BackupsDirectory => Path.Combine(directory, "backups");
-    private string TempPath => ConfigPath + ".tmp";
+    private readonly JsonStore<NeoFencesConfig> _store;
+    private bool _previousSchemaChecked; // once per run: with no older file to find, every save would re-read up to 11 files (M13c)
 
-    public ConfigLoadResult Load()
+    public ConfigStore(string directory, TimeProvider? timeProvider = null)
     {
-        _saveBlocked = false;
-        string? corruptCopyPath = null;
-
-        switch (TryRead(ConfigPath, out var primary))
-        {
-            case ReadOutcome.Ok:
-                return new(ConfigNormalizer.Normalize(primary!), ConfigLoadSource.Primary, null, IsReadOnly: false);
-            case ReadOutcome.NewerSchema:
-                _saveBlocked = true;
-                return new(NeoFencesConfig.CreateDefault(), ConfigLoadSource.Fresh, null, IsReadOnly: true);
-            case ReadOutcome.Unreadable:
-                // Not corrupt, just inaccessible right now: show the best fallback, but never overwrite it.
-                _saveBlocked = true;
-                break;
-            case ReadOutcome.Corrupt:
-                corruptCopyPath = Path.Combine(directory, $"config.corrupt-{_time.GetLocalNow():yyyyMMdd-HHmmss}.json");
-                try
-                {
-                    File.Copy(ConfigPath, corruptCopyPath, overwrite: true);
-                }
-                catch (Exception copyFailure) when (copyFailure is IOException or UnauthorizedAccessException)
-                {
-                    corruptCopyPath = null;
-                    _saveBlocked = true; // could not preserve it, so do not overwrite it either
-                }
-                break;
-        }
+        _store = new JsonStore<NeoFencesConfig>(directory, FileName, NeoFencesConfig.CurrentSchemaVersion, ConfigJson.Deserialize,
+            config => config.SchemaVersion, ConfigJson.Serialize, timeProvider ?? TimeProvider.System);
+    }
 
-        if (TryRead(BackupPath, out var backup) == ReadOutcome.Ok)
-        {
-            return new(ConfigNormalizer.Normalize(backup!), ConfigLoadSource.Backup, corruptCopyPath, IsReadOnly: _saveBlocked);
-        }
+    public string ConfigPath => _store.FilePath;
+    public string BackupPath => _store.BackupPath;
+    public string BackupsDirectory => _store.BackupsDirectory;
 
-        foreach (var dailyBackupPath in DailyBackupsNewestFirst())
-        {
-            if (TryRead(dailyBackupPath, out var daily) == ReadOutcome.Ok)
-            {
-                return new(ConfigNormalizer.Normalize(daily!), ConfigLoadSource.DailyBackup, corruptCopyPath, IsReadOnly: _saveBlocked);
-            }
-        }
+    /// <summary>Why the last save could not write or prune the daily backup (null when it could). Reported apart from Save.</summary>
+    public Exception? LastBackupFailure { get; private set; }
 
-        return new(NeoFencesConfig.CreateDefault(), ConfigLoadSource.Fresh, corruptCopyPath, IsReadOnly: _saveBlocked);
+    public ConfigLoadResult Load()
+    {
+        var (config, source, corruptCopyPath, isReadOnly) = _store.Load();
+        if (config is null) return new(NeoFencesConfig.CreateDefault(), ConfigLoadSource.Fresh, corruptCopyPath, isReadOnly);
+        // From before the virtual items: a fresh start (spec §1); the first save keeps the old file as PreviousSchemaCopyName.
+        if (config.SchemaVersion < FirstVirtualItemsSchema) return new(NeoFencesConfig.CreateDefault(), ConfigLoadSource.Fresh, corruptCopyPath, isReadOnly);
+        return new(ConfigNormalizer.Normalize(config), source, corruptCopyPath, isReadOnly);
     }
 
     /// <returns>
@@ -88,33 +68,11 @@ public sealed class ConfigStore(string directory, TimeProvider? timeProvider = n
     /// </returns>
     public bool Save(NeoFencesConfig config)
     {
-        if (_saveBlocked || TryRead(ConfigPath, out _) == ReadOutcome.NewerSchema) return false;
-
-        Directory.CreateDirectory(directory);
-        var json = ConfigJson.Serialize(config);
-        using (var tempFile = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
-        using (var writer = new StreamWriter(tempFile))
-        {
-            writer.Write(json);
-            writer.Flush();
-            tempFile.Flush(flushToDisk: true);
-        }
-
-        if (File.Exists(ConfigPath))
-        {
-            File.Replace(TempPath, ConfigPath, BackupPath, ignoreMetadataErrors: true);
-        }
-        else
-        {
-            File.Move(TempPath, ConfigPath);
-        }
-
-        // The daily backup is a convenience: its failure (a full disk, a file in the way) must not fail the save (M8a).
+        if (!_store.Save(config)) return false;
+        LastBackupFailure = _store.LastBackupFailure;
         try
         {
-            WriteDailyBackup(json);
             KeepPreviousSchemaCopy();
-            LastBackupFailure = null;
         }
         catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
         {
@@ -123,79 +81,21 @@ public sealed class ConfigStore(string directory, TimeProvider? timeProvider = n
         return true;
     }
 
-    /// <summary>Why the last save could not write or prune the daily backup (null when it could). Reported apart from Save.</summary>
-    public Exception? LastBackupFailure { get; private set; }
-
-    private void WriteDailyBackup(string json)
-    {
-        Directory.CreateDirectory(BackupsDirectory);
-        // First save of the day wins: a config that went bad later in the day cannot overwrite it.
-        var todayPath = Path.Combine(BackupsDirectory, $"config-{_time.GetLocalNow():yyyyMMdd}.json");
-        if (!File.Exists(todayPath)) File.WriteAllText(todayPath, json);
-
-        foreach (var expiredPath in DailyBackupsNewestFirst().Skip(DailyBackupsKept))
-        {
-            File.Delete(expiredPath);
-        }
-    }
-
-    private bool _previousSchemaChecked; // once per run: with no older file to find, every save would re-read up to 11 files (M13c)
-
     private void KeepPreviousSchemaCopy()
     {
         if (_previousSchemaChecked) return;
         var copy = Path.Combine(BackupsDirectory, PreviousSchemaCopyName);
         if (!File.Exists(copy))
         {
-            foreach (var candidate in DailyBackupsNewestFirst().Prepend(BackupPath))
+            foreach (var candidate in _store.DailyBackupsNewestFirst().Prepend(BackupPath))
             {
-                if (TryRead(candidate, out var old) != ReadOutcome.Ok || old!.SchemaVersion >= NeoFencesConfig.CurrentSchemaVersion) continue;
+                if (_store.TryRead(candidate, out var old) != JsonStore<NeoFencesConfig>.ReadOutcome.Ok
+                    || old!.SchemaVersion >= NeoFencesConfig.CurrentSchemaVersion) continue;
+                Directory.CreateDirectory(BackupsDirectory);
                 File.Copy(candidate, copy); // a failure throws past the flag below: the next save tries again (M13c final review)
                 break;
             }
         }
         _previousSchemaChecked = true;
     }
-
-    private IEnumerable<string> DailyBackupsNewestFirst() =>
-        Directory.Exists(BackupsDirectory)
-            ? Directory.GetFiles(BackupsDirectory, "config-*.json").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToList()
-            : [];
-
-    private enum ReadOutcome { Missing, Ok, Corrupt, NewerSchema, Unreadable }
-
-    private const int ReadAttempts = 3;
-    private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(100);
-
-    private static ReadOutcome TryRead(string path, out NeoFencesConfig? config)
-    {
-        config = null;
-        if (!File.Exists(path)) return ReadOutcome.Missing;
-
-        string? json = null;
-        for (var attempt = 1; json is null; attempt++)
-        {
-            try
-            {
-                json = File.ReadAllText(path);
-            }
-            catch (Exception readFailure) when (readFailure is IOException or UnauthorizedAccessException)
-            {
-                // ponytail: blocking retry (~200 ms worst case), fine for startup; make async if Load moves off-thread.
-                if (attempt == ReadAttempts) return ReadOutcome.Unreadable;
-                Thread.Sleep(ReadRetryDelay);
-            }
-        }
-
-        try
-        {
-            config = ConfigJson.Deserialize(json);
-        }
-        catch (JsonException)
-        {
-            return ReadOutcome.Corrupt;
-        }
-        if (config.SchemaVersion > NeoFencesConfig.CurrentSchemaVersion) return ReadOutcome.NewerSchema;
-        return config.SchemaVersion < 1 ? ReadOutcome.Corrupt : ReadOutcome.Ok;
-    }
 }
diff --git a/src/NeoFences.Core/Config/ItemStore.cs b/src/NeoFences.Core/Config/ItemStore.cs
new file mode 100644
index 0000000..2a86ed8
--- /dev/null
+++ b/src/NeoFences.Core/Config/ItemStore.cs
@@ -0,0 +1,35 @@
+using NeoFences.Core.Items;
+
+namespace NeoFences.Core.Config;
+
+/// <param name="IsReadOnly">True when items.json must not be overwritten this session (a newer NeoFences wrote it, or it
+/// could not be read right now): the document returned is the best fallback.</param>
+public sealed record ItemsLoadResult(ItemsDocument Document, ConfigLoadSource Source, string? CorruptCopyPath, bool IsReadOnly);
+
+/// <summary>
+/// Loads and saves <c>items.json</c> (ADR-041): the same safe save, <c>.bak</c>, daily backups and recovery chain as
+/// <c>config.json</c>, in a file of its own, so a damaged items file never takes the settings with it (or the other way).
+/// </summary>
+public sealed class ItemStore(string directory, TimeProvider? timeProvider = null)
+{
+    public const string FileName = "items.json";
+
+    private readonly JsonStore<ItemsDocument> _store = new(directory, FileName, ItemsDocument.CurrentSchemaVersion, ConfigJson.DeserializeItems,
+        document => document.Schema, ConfigJson.SerializeItems, timeProvider ?? TimeProvider.System);
+
+    public string ItemsPath => _store.FilePath;
+    public string BackupPath => _store.BackupPath;
+    public string BackupsDirectory => _store.BackupsDirectory;
+
+    /// <summary>Why the last save could not write or prune the daily backup (null when it could).</summary>
+    public Exception? LastBackupFailure => _store.LastBackupFailure;
+
+    public ItemsLoadResult Load()
+    {
+        var (document, source, corruptCopyPath, isReadOnly) = _store.Load();
+        return new(document is null ? new ItemsDocument() : ItemEdits.Repair(document), source, corruptCopyPath, isReadOnly);
+    }
+
+    /// <returns>False when saving is blocked (see <see cref="ItemsLoadResult.IsReadOnly"/>).</returns>
+    public bool Save(ItemsDocument document) => _store.Save(document);
+}
diff --git a/src/NeoFences.Core/Config/JsonStore.cs b/src/NeoFences.Core/Config/JsonStore.cs
new file mode 100644
index 0000000..920cfc5
--- /dev/null
+++ b/src/NeoFences.Core/Config/JsonStore.cs
@@ -0,0 +1,167 @@
+using System.Text.Json;
+
+namespace NeoFences.Core.Config;
+
+public enum ConfigLoadSource { Primary, Backup, DailyBackup, Fresh }
+
+/// <summary>
+/// One JSON file of NeoFences' own data (ADR-006, ADR-041): atomic replace with <c>.bak</c>, one backup per day (newest
+/// 10 kept), a recovery chain for damaged files, and never a save over a file from a newer NeoFences. <c>config.json</c>
+/// and <c>items.json</c> are each one of these.
+/// </summary>
+internal sealed class JsonStore<T>(string directory, string fileName, int currentSchema, Func<string, T> deserialize, Func<T, int> schemaOf,
+    Func<T, string> serialize, TimeProvider time) where T : class
+{
+    public const int DailyBackupsKept = 10;
+    private const int ReadAttempts = 3;
+    private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(100);
+
+    private bool _saveBlocked;
+
+    public string FilePath => Path.Combine(directory, fileName);
+    public string BackupPath => FilePath + ".bak";
+    public string BackupsDirectory => Path.Combine(directory, "backups");
+    private string Stem => Path.GetFileNameWithoutExtension(fileName);
+
+    /// <summary>Why the last save could not write or prune the daily backup (null when it could). Reported apart from Save.</summary>
+    public Exception? LastBackupFailure { get; private set; }
+
+    /// <returns>The best readable version (null: start empty), where it came from, where a damaged file was preserved, and
+    /// whether the file must not be overwritten this session (written by a newer NeoFences, or unreadable right now).</returns>
+    public (T? Value, ConfigLoadSource Source, string? CorruptCopyPath, bool IsReadOnly) Load()
+    {
+        _saveBlocked = false;
+        string? corruptCopyPath = null;
+
+        switch (TryRead(FilePath, out var primary))
+        {
+            case ReadOutcome.Ok:
+                return (primary, ConfigLoadSource.Primary, null, false);
+            case ReadOutcome.NewerSchema:
+                _saveBlocked = true;
+                return (null, ConfigLoadSource.Fresh, null, true);
+            case ReadOutcome.Unreadable:
+                // Not corrupt, just inaccessible right now: show the best fallback, but never overwrite it.
+                _saveBlocked = true;
+                break;
+            case ReadOutcome.Corrupt:
+                corruptCopyPath = Path.Combine(directory, $"{Stem}.corrupt-{time.GetLocalNow():yyyyMMdd-HHmmss}.json");
+                try
+                {
+                    File.Copy(FilePath, corruptCopyPath, overwrite: true);
+                }
+                catch (Exception copyFailure) when (copyFailure is IOException or UnauthorizedAccessException)
+                {
+                    corruptCopyPath = null;
+                    _saveBlocked = true; // could not preserve it, so do not overwrite it either
+                }
+                break;
+        }
+
+        if (TryRead(BackupPath, out var backup) == ReadOutcome.Ok) return (backup, ConfigLoadSource.Backup, corruptCopyPath, _saveBlocked);
+        foreach (var dailyBackupPath in DailyBackupsNewestFirst())
+        {
+            if (TryRead(dailyBackupPath, out var daily) == ReadOutcome.Ok) return (daily, ConfigLoadSource.DailyBackup, corruptCopyPath, _saveBlocked);
+        }
+        return (null, ConfigLoadSource.Fresh, corruptCopyPath, _saveBlocked);
+    }
+
+    /// <returns>
+    /// False when saving is blocked: the file belongs to a newer NeoFences version (checked on disk on every save, so a
+    /// second store or a save without a prior Load cannot overwrite it either), or Load found it unreadable.
+    /// </returns>
+    public bool Save(T value)
+    {
+        if (_saveBlocked || TryRead(FilePath, out _) == ReadOutcome.NewerSchema) return false;
+        Directory.CreateDirectory(directory);
+        var json = serialize(value);
+        SafeFile.Write(FilePath, json, BackupPath);
+        // The daily backup is a convenience: its failure (a full disk, a file in the way) must not fail the save (M8a).
+        try
+        {
+            WriteDailyBackup(json);
+            LastBackupFailure = null;
+        }
+        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
+        {
+            LastBackupFailure = failure;
+        }
+        return true;
+    }
+
+    private void WriteDailyBackup(string json)
+    {
+        Directory.CreateDirectory(BackupsDirectory);
+        // First save of the day wins: a file that went bad later in the day cannot overwrite it.
+        var todayPath = Path.Combine(BackupsDirectory, $"{Stem}-{time.GetLocalNow():yyyyMMdd}.json");
+        if (!File.Exists(todayPath)) File.WriteAllText(todayPath, json);
+        foreach (var expiredPath in DailyBackupsNewestFirst().Skip(DailyBackupsKept)) File.Delete(expiredPath);
+    }
+
+    public IEnumerable<string> DailyBackupsNewestFirst() =>
+        Directory.Exists(BackupsDirectory)
+            ? Directory.GetFiles(BackupsDirectory, $"{Stem}-*.json").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToList()
+            : [];
+
+    public enum ReadOutcome { Missing, Ok, Corrupt, NewerSchema, Unreadable }
+
+    public ReadOutcome TryRead(string path, out T? value)
+    {
+        value = null;
+        if (!File.Exists(path)) return ReadOutcome.Missing;
+
+        string? json = null;
+        for (var attempt = 1; json is null; attempt++)
+        {
+            try
+            {
+                json = File.ReadAllText(path);
+            }
+            catch (Exception readFailure) when (readFailure is IOException or UnauthorizedAccessException)
+            {
+                // ponytail: blocking retry (~200 ms worst case), fine for startup; make async if Load moves off-thread.
+                if (attempt == ReadAttempts) return ReadOutcome.Unreadable;
+                Thread.Sleep(ReadRetryDelay);
+            }
+        }
+
+        try
+        {
+            value = deserialize(json);
+        }
+        catch (JsonException)
+        {
+            return ReadOutcome.Corrupt;
+        }
+        var schema = schemaOf(value);
+        if (schema > currentSchema) return ReadOutcome.NewerSchema;
+        return schema < 1 ? ReadOutcome.Corrupt : ReadOutcome.Ok;
+    }
+}
+
+/// <summary>Writes to a temp file flushed to disk, then swaps it in: a power cut never leaves half a file.</summary>
+internal static class SafeFile
+{
+    /// <param name="backupPath">Where the replaced file goes (its <c>.bak</c>), or null to keep no copy.</param>
+    public static void Write(string path, string text, string? backupPath)
+    {
+        var temp = path + ".tmp";
+        try
+        {
+            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
+            using (var writer = new StreamWriter(stream))
+            {
+                writer.Write(text);
+                writer.Flush();
+                stream.Flush(flushToDisk: true);
+            }
+            if (File.Exists(path)) File.Replace(temp, path, backupPath, ignoreMetadataErrors: true);
+            else File.Move(temp, path);
+        }
+        catch
+        {
+            if (File.Exists(temp)) File.Delete(temp); // no *.tmp left behind, also after a full disk (M13a, M13b); the caller reports the failure
+            throw;
+        }
+    }
+}
diff --git a/src/NeoFences.Core/Config/SnapshotStore.cs b/src/NeoFences.Core/Config/SnapshotStore.cs
index c0e3c50..4729642 100644
--- a/src/NeoFences.Core/Config/SnapshotStore.cs
+++ b/src/NeoFences.Core/Config/SnapshotStore.cs
@@ -13,7 +13,7 @@ public sealed record SnapshotEntry(string Path, string Name, DateTimeOffset Take
 /// <summary>
 /// Snapshot files (M10): one per snapshot in <c>%LOCALAPPDATA%\NeoFences\snapshots\</c>, written to a temp file and then
 /// swapped in, so a power cut never leaves half a snapshot. A damaged file is skipped and reported, never thrown.
-/// Deleting is the App's job (the Recycle Bin, hard rule 1).
+/// Deleting is the App's job (to the Recycle Bin).
 /// </summary>
 public sealed class SnapshotStore(string directory)
 {
@@ -69,7 +69,7 @@ public sealed class SnapshotStore(string directory)
         {
             System.IO.Directory.CreateDirectory(directory);
             var path = System.IO.Path.Combine(directory, fileName ?? NewFileName(snapshot.TakenAt));
-            WriteSafely(path, ConfigJson.SerializeSnapshot(snapshot));
+            SafeFile.Write(path, ConfigJson.SerializeSnapshot(snapshot), backupPath: null);
             LastFailure = null;
             return path;
         }
@@ -94,14 +94,16 @@ public sealed class SnapshotStore(string directory)
         }
     }
 
-    /// <exception cref="InvalidDataException">Too big, or written by a newer NeoFences (M13a): restoring or renaming it
-    /// here would drop what this version does not know.</exception>
+    /// <exception cref="InvalidDataException">Too big; written by a newer NeoFences (M13a: restoring or renaming it here would
+    /// drop what this version does not know); or from before the virtual items (M18: its fences held Desktop files).</exception>
     private static Snapshot Read(string path)
     {
         if (new FileInfo(path).Length > MaxFileBytes) throw new InvalidDataException($"{path} is too big for a snapshot");
         var snapshot = ConfigJson.DeserializeSnapshot(File.ReadAllText(path));
         if (snapshot.SchemaVersion > NeoFencesConfig.CurrentSchemaVersion)
             throw new InvalidDataException($"{path} comes from a newer NeoFences (schema {snapshot.SchemaVersion})");
+        if (snapshot.SchemaVersion < ConfigStore.FirstVirtualItemsSchema)
+            throw new InvalidDataException($"{path} comes from before the virtual items (schema {snapshot.SchemaVersion})");
         return snapshot;
     }
 
@@ -136,26 +138,4 @@ public sealed class SnapshotStore(string directory)
         for (var counter = 2; File.Exists(System.IO.Path.Combine(directory, name)); counter++) name = $"{stem}-{counter}.json";
         return name;
     }
-
-    private static void WriteSafely(string path, string json)
-    {
-        var temp = path + ".tmp";
-        try
-        {
-            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
-            using (var writer = new StreamWriter(stream))
-            {
-                writer.Write(json);
-                writer.Flush();
-                stream.Flush(flushToDisk: true);
-            }
-            if (File.Exists(path)) File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
-            else File.Move(temp, path);
-        }
-        catch
-        {
-            if (File.Exists(temp)) File.Delete(temp); // no *.json.tmp left behind, also after a full disk (M13a, M13b); the caller reports the failure
-            throw;
-        }
-    }
 }
diff --git a/src/NeoFences.Core/Layouts/DropZones.cs b/src/NeoFences.Core/Layouts/DropZones.cs
index febd19d..443d6a3 100644
--- a/src/NeoFences.Core/Layouts/DropZones.cs
+++ b/src/NeoFences.Core/Layouts/DropZones.cs
@@ -1,16 +1,8 @@
 namespace NeoFences.Core.Layouts;
 
-/// <summary>
-/// Where a drop over a folder-like item means "into it" (M3b review I2): only the middle half of the cell's width and its
-/// upper three quarters (icon and first label line). The edges and the rest of the label reorder, so a drop meant to
-/// land between two folders never moves a file into one of them.
-/// </summary>
+/// <summary>Where a drop lands in a fence (M3b): the insert position under the pointer.</summary>
 public static class DropZones
 {
-    public static bool IsInto(double cellLeft, double cellTop, double cellWidth, double cellHeight, double pointX, double pointY) =>
-        pointX >= cellLeft + cellWidth / 4 && pointX <= cellLeft + cellWidth * 3 / 4
-        && pointY >= cellTop && pointY <= cellTop + cellHeight * 3 / 4;
-
     /// <summary>
     /// The index to insert before when dropping at a point, cells in reading order (left to right, rows top to bottom).
     /// A row reaches down to its tallest cell, so the space under a short label still belongs to its row (M3b review).
diff --git a/src/NeoFences.Core/Library/GameLaunchers.cs b/src/NeoFences.Core/Library/GameLaunchers.cs
new file mode 100644
index 0000000..2c3adbd
--- /dev/null
+++ b/src/NeoFences.Core/Library/GameLaunchers.cs
@@ -0,0 +1,42 @@
+namespace NeoFences.Core.Library;
+
+public enum GameLauncher { Steam, Epic, Ubisoft, Ea, BattleNet, Gog }
+
+/// <summary>Which game launcher a shortcut starts (the Game Library's Desktop shortcuts, M12; from Rules before M18).</summary>
+public static class GameLaunchers
+{
+    private static readonly (string Prefix, GameLauncher Launcher)[] LauncherUrls =
+    [
+        ("steam://", GameLauncher.Steam), ("com.epicgames.launcher://", GameLauncher.Epic), ("uplay://", GameLauncher.Ubisoft),
+        ("origin://", GameLauncher.Ea), ("origin2://", GameLauncher.Ea), ("ea://", GameLauncher.Ea), ("link2ea://", GameLauncher.Ea),
+        ("battlenet://", GameLauncher.BattleNet), ("goggalaxy://", GameLauncher.Gog),
+    ];
+
+    private static readonly (string Part, GameLauncher Launcher)[] LibraryFolders =
+    [
+        (@"\steamapps\common\", GameLauncher.Steam), (@"\Epic Games\", GameLauncher.Epic),
+        (@"\Ubisoft Game Launcher\games\", GameLauncher.Ubisoft), (@"\EA Games\", GameLauncher.Ea),
+        (@"\GOG Galaxy\Games\", GameLauncher.Gog), (@"\GOG Games\", GameLauncher.Gog),
+    ];
+
+    /// <summary>The game launcher a shortcut belongs to (its URL scheme, library folder or launcher arguments), or null.</summary>
+    public static GameLauncher? LauncherOf(string? shortcutTarget)
+    {
+        if (string.IsNullOrWhiteSpace(shortcutTarget)) return null;
+        var target = shortcutTarget.Trim();
+        foreach (var (prefix, launcher) in LauncherUrls)
+        {
+            // The link itself, or the link handed to the launcher as an argument (M13b): "steam.exe steam://rungameid/570".
+            if (target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
+                || target.Contains(" " + prefix, StringComparison.OrdinalIgnoreCase) || target.Contains("\"" + prefix, StringComparison.OrdinalIgnoreCase)) return launcher;
+        }
+        // The Epic launcher itself lives under "\Epic Games\Launcher\": not a game.
+        foreach (var (part, launcher) in LibraryFolders)
+        {
+            if (target.Contains(part, StringComparison.OrdinalIgnoreCase) && !target.Contains(@"\Epic Games\Launcher\", StringComparison.OrdinalIgnoreCase)) return launcher;
+        }
+        if (target.Contains("steam.exe", StringComparison.OrdinalIgnoreCase) && target.Contains("-applaunch", StringComparison.OrdinalIgnoreCase)) return GameLauncher.Steam;
+        if (target.Contains("battle.net.exe", StringComparison.OrdinalIgnoreCase) && target.Contains("--exec", StringComparison.OrdinalIgnoreCase)) return GameLauncher.BattleNet;
+        return null;
+    }
+}
diff --git a/src/NeoFences.Core/Lifecycle/RunState.cs b/src/NeoFences.Core/Lifecycle/RunState.cs
index df51b47..1706ab6 100644
--- a/src/NeoFences.Core/Lifecycle/RunState.cs
+++ b/src/NeoFences.Core/Lifecycle/RunState.cs
@@ -2,16 +2,17 @@ namespace NeoFences.Core.Lifecycle;
 
 /// <summary>
 /// The run-time modes of NeoFences and what they mean together (M5 quick-hide, M6a Pause and game mode, ADR-021).
-/// None of them is saved: after a restart only Takeover (a setting) applies again.
+/// None of them is saved: after a restart only "Hide desktop icons" (a setting) applies again.
 /// </summary>
-/// <param name="IconsHiddenByUser">Takeover off and the icons were already hidden in Explorer when quick-hide began: quick-hide
-/// neither hides nor shows them (M8a review I1).</param>
-public sealed record RunState(bool Takeover, bool QuickHidden, bool Paused, bool GameMode, bool IconsHiddenByUser = false)
+/// <param name="HideIcons">Settings → "Hide desktop icons while NeoFences runs" (M18).</param>
+/// <param name="IconsHiddenByUser">Hide icons off and the icons were already hidden in Explorer when quick-hide began:
+/// quick-hide neither hides nor shows them (M8a review I1).</param>
+public sealed record RunState(bool HideIcons, bool QuickHidden, bool Paused, bool GameMode, bool IconsHiddenByUser = false)
 {
     public bool FencesVisible => !Paused && !QuickHidden;
 
-    /// <summary>Pause gives the desktop back to Windows: its icons show even with Takeover on (spec §6).</summary>
-    public bool IconsHidden => !Paused && (Takeover || (QuickHidden && !IconsHiddenByUser));
+    /// <summary>Pause gives the desktop back to Windows: its icons show even with "Hide desktop icons" on (spec §6).</summary>
+    public bool IconsHidden => !Paused && (HideIcons || (QuickHidden && !IconsHiddenByUser));
 
     /// <summary>The only global hook goes away while paused or gaming (hard rule 3, spec §4.7).</summary>
     public bool MouseHookWanted => !Paused && !GameMode;
@@ -19,6 +20,6 @@ public sealed record RunState(bool Takeover, bool QuickHidden, bool Paused, bool
     /// <summary>Ctrl+Alt+Space (or the chosen Peek hotkey) is released to Windows and the game while paused or gaming.</summary>
     public bool PeekHotkeyWanted => !Paused && !GameMode;
 
-    /// <summary>Desktop and Portal changes wait until the game is left (spec §4.7).</summary>
+    /// <summary>Target re-checks, watch events and library scans wait until the game is left (spec §4.7).</summary>
     public bool ShellWorkDeferred => GameMode;
 }
diff --git a/src/NeoFences.Core/Lifecycle/StartupPolicy.cs b/src/NeoFences.Core/Lifecycle/StartupPolicy.cs
index 5729143..a771c2f 100644
--- a/src/NeoFences.Core/Lifecycle/StartupPolicy.cs
+++ b/src/NeoFences.Core/Lifecycle/StartupPolicy.cs
@@ -1,7 +1,7 @@
 namespace NeoFences.Core.Lifecycle;
 
 /// <summary>
-/// Start with Windows (ADR-019, refined by ADR-023). Takeover's hidden desktop icons survive a hard power loss (Explorer
+/// Start with Windows (ADR-019, refined by ADR-023). Desktop icons NeoFences hid survive a hard power loss (Explorer
 /// persists them), so NeoFences has to come back at sign-in by itself to show fences, or the desktop is empty.
 /// </summary>
 public static class StartupPolicy
diff --git a/src/NeoFences.Core/Lifecycle/WatchdogPlan.cs b/src/NeoFences.Core/Lifecycle/WatchdogPlan.cs
index f041a30..4002870 100644
--- a/src/NeoFences.Core/Lifecycle/WatchdogPlan.cs
+++ b/src/NeoFences.Core/Lifecycle/WatchdogPlan.cs
@@ -15,10 +15,10 @@ public sealed record WatchdogPlan(bool RestoreIcons, WatchdogRestart Restart)
 {
     /// <param name="cleanShutdown">Main wrote <c>clean-shutdown-&lt;pid&gt;</c> (orderly exit).</param>
     /// <param name="sessionEnding">Main wrote <c>session-ending-&lt;pid&gt;</c> (Windows asked to end the session).</param>
-    /// <param name="takeoverActive"><c>takeover-active</c> exists: the icons may still be hidden.</param>
-    public static WatchdogPlan For(bool cleanShutdown, bool sessionEnding, bool takeoverActive) => new(
+    /// <param name="iconsHidden"><c>icons-hidden</c> exists: NeoFences may have left the icons hidden.</param>
+    public static WatchdogPlan For(bool cleanShutdown, bool sessionEnding, bool iconsHidden) => new(
         // Restoring is idempotent, so it never depends on how main exited: a marker alone must not leave icons hidden.
-        RestoreIcons: takeoverActive,
+        RestoreIcons: iconsHidden,
         Restart: cleanShutdown ? WatchdogRestart.Never
             : sessionEnding ? WatchdogRestart.IfSessionContinues
             : WatchdogRestart.Throttled);
diff --git a/src/NeoFences.Core/Model/Fence.cs b/src/NeoFences.Core/Model/Fence.cs
index 35468ad..f768c32 100644
--- a/src/NeoFences.Core/Model/Fence.cs
+++ b/src/NeoFences.Core/Model/Fence.cs
@@ -1,35 +1,23 @@
 namespace NeoFences.Core.Model;
 
-/// <summary>Library (M12): NeoFences' own game library folder, shown like a Portal.</summary>
-public enum FenceSourceKind { Desktop, Portal, Library }
-
-/// <summary>Where a fence's items come from: the desktop (Takeover) or a folder (Portal).</summary>
-public sealed record FenceSource(FenceSourceKind Kind, string? Path = null)
-{
-    public static FenceSource Desktop { get; } = new(FenceSourceKind.Desktop);
-
-    public static FenceSource Portal(string folderPath) => new(FenceSourceKind.Portal, folderPath);
-
-    public static FenceSource Library { get; } = new(FenceSourceKind.Library);
-}
-
+/// <summary>"Sort by" in the fence menu: a one-time reorder of the fence's items (M4; M18: virtual items).</summary>
 public enum FenceSort { Manual, Name, Type, Date }
 
 /// <summary>Item names under the icons: always, or only for the hovered or selected item (icon-only fences, M8b).</summary>
 public enum LabelMode { Always, OnHover }
 
 /// <summary>
-/// One fence. Desktop fences keep an ordered list of item refs (shell parsing names: file paths or
-/// "::{GUID}" for virtual items). Portal fences keep no items; they mirror <see cref="FenceSource.Path"/>.
+/// One fence: its look and behaviour. Its virtual items live in <c>items.json</c> under its id (ADR-041); the Game Library
+/// fence (<see cref="IsLibrary"/>) shows NeoFences' own game shortcuts instead.
 /// </summary>
 public sealed record Fence
 {
     public required string Id { get; init; }
     public required string Title { get; init; }
-    public FenceSource Source { get; init; } = FenceSource.Desktop;
-    public IReadOnlyList<string> Items { get; init; } = [];
-    public bool IsInbox { get; init; }
-    public FenceSort Sort { get; init; } = FenceSort.Manual;
+
+    /// <summary>The Game Library fence (M12): at most one; it lists the library folder, never virtual items.</summary>
+    public bool IsLibrary { get; init; }
+
     public int IconSize { get; init; } = 48;
     public bool RolledUp { get; init; }
     public bool Locked { get; init; }
@@ -47,8 +35,7 @@ public sealed record Fence
     /// <summary>Any colour as "#RRGGBB" (M14, fence menu → Colour → Custom…); wins over <see cref="TabColor"/>.</summary>
     public string? CustomColor { get; init; }
 
-    public static Fence Create(string title, FenceSource? source = null) =>
-        new() { Id = NewId(), Title = title, Source = source ?? FenceSource.Desktop };
+    public static Fence Create(string title, bool isLibrary = false) => new() { Id = NewId(), Title = title, IsLibrary = isLibrary };
 
     public static string NewId() => Guid.NewGuid().ToString("N");
 }
diff --git a/src/NeoFences.Core/Model/FenceEdits.cs b/src/NeoFences.Core/Model/FenceEdits.cs
index c1a829b..d4a6ee7 100644
--- a/src/NeoFences.Core/Model/FenceEdits.cs
+++ b/src/NeoFences.Core/Model/FenceEdits.cs
@@ -2,7 +2,7 @@ using NeoFences.Core.Config;
 
 namespace NeoFences.Core.Model;
 
-/// <summary>Changes to one fence's own settings (title, icon size, lock). Pure: each returns a new config.</summary>
+/// <summary>Changes to fences (title, icon size, lock, new, delete). Pure: each returns a new config; none touches a file.</summary>
 public static class FenceEdits
 {
     public const int MaxTitleLength = 64;
@@ -53,22 +53,23 @@ public static class FenceEdits
             Settings = config.Settings with { DefaultLabels = labels },
         };
 
-    public static NeoFencesConfig SetSort(NeoFencesConfig config, string fenceId, FenceSort sort) =>
-        config.WithFence(Require(config, fenceId) with { Sort = sort });
+    /// <summary>A new empty fence (fence menu, tray, or one drawn on the desktop), with the default labels.</summary>
+    public static (NeoFencesConfig Config, Fence Fence) CreateFence(NeoFencesConfig config, string title)
+    {
+        var fence = Fence.Create(title) with { Labels = config.Settings.DefaultLabels };
+        return (config with { Fences = [.. config.Fences, fence] }, fence);
+    }
 
     /// <summary>
-    /// "Sort by" on a desktop fence: a one-time reorder (dragging still works afterwards). The new order must hold exactly
-    /// the fence's items (compared ignoring case; the stored spelling is kept), so a sort can never drop or add an item.
+    /// Deletes a fence (a tab leaves its box first; a host hands the box to the next tab, M9). Its items go with it from
+    /// items.json; their targets are never touched (hard rule 1).
     /// </summary>
-    /// <exception cref="ArgumentException">The order is not a permutation of the fence's items.</exception>
-    public static NeoFencesConfig SetItemOrder(NeoFencesConfig config, string fenceId, IReadOnlyList<string> orderedRefs)
+    /// <exception cref="ArgumentException">No fence with that id.</exception>
+    public static NeoFencesConfig DeleteFence(NeoFencesConfig config, string fenceId)
     {
-        var fence = Require(config, fenceId);
-        var spelling = fence.Items.ToDictionary(itemRef => itemRef, ItemRef.Comparer);
-        if (orderedRefs.Count != fence.Items.Count || orderedRefs.Distinct(ItemRef.Comparer).Count() != orderedRefs.Count
-            || !orderedRefs.All(spelling.ContainsKey))
-            throw new ArgumentException("The new order must contain exactly the fence's items.", nameof(orderedRefs));
-        return config.WithFence(fence with { Items = orderedRefs.Select(itemRef => spelling[itemRef]).ToList() });
+        Require(config, fenceId);
+        config = FenceTabs.Leave(config, fenceId);
+        return config with { Fences = config.Fences.Where(fence => fence.Id != fenceId).ToList() };
     }
 
     private static Fence Require(NeoFencesConfig config, string fenceId) =>
diff --git a/src/NeoFences.Core/Model/FenceTabs.cs b/src/NeoFences.Core/Model/FenceTabs.cs
index f0562f2..eeb4789 100644
--- a/src/NeoFences.Core/Model/FenceTabs.cs
+++ b/src/NeoFences.Core/Model/FenceTabs.cs
@@ -6,8 +6,8 @@ namespace NeoFences.Core.Model;
 public enum TabColor { Red, Orange, Yellow, Green, Teal, Blue, Purple, Pink }
 
 /// <summary>
-/// Fence tabs (M9, spec 2026-10-03-fence-tabs-design): fences combined into one box. Every tab stays a full fence (items,
-/// source, sort, icon size, labels). The host — the fence whose <see cref="Fence.Tabs"/> lists two or more ids, itself
+/// Fence tabs (M9, spec 2026-10-03-fence-tabs-design): fences combined into one box. Every tab stays a full fence (its own
+/// items, icon size, labels). The host — the fence whose <see cref="Fence.Tabs"/> lists two or more ids, itself
 /// included — owns the box: its window, its placement in every layout, roll-up and lock. Members have no placement.
 /// Every edit returns a new config; nothing changes items or files.
 /// </summary>
diff --git a/src/NeoFences.Core/Model/ItemSorting.cs b/src/NeoFences.Core/Model/ItemSorting.cs
index c468e4f..d0ada9f 100644
--- a/src/NeoFences.Core/Model/ItemSorting.cs
+++ b/src/NeoFences.Core/Model/ItemSorting.cs
@@ -4,7 +4,7 @@ namespace NeoFences.Core.Model;
 public sealed record ItemInfo(string ItemRef, string Name, bool IsFolder, string TypeName, DateTimeOffset Modified);
 
 /// <summary>
-/// Item order for Portals (live) and for "Sort by" on desktop fences (one time). Name and Type put folders first, like
+/// Item order for "Sort by" (one time) and the library's listing. Name and Type put folders first, like
 /// Explorer; Date is newest first with folders mixed in, so the latest file is always on top (user choice 2026-10-03).
 /// </summary>
 public static class ItemSorting
diff --git a/src/NeoFences.Core/Model/NeoFencesConfig.cs b/src/NeoFences.Core/Model/NeoFencesConfig.cs
index 75ce106..05572fe 100644
--- a/src/NeoFences.Core/Model/NeoFencesConfig.cs
+++ b/src/NeoFences.Core/Model/NeoFencesConfig.cs
@@ -1,36 +1,28 @@
-using System.Text.Json.Serialization;
-
 namespace NeoFences.Core.Model;
 
-/// <summary>Everything NeoFences persists, stored as <c>config.json</c> (ADR-006).</summary>
+/// <summary>Everything NeoFences persists except the items (those are <c>items.json</c>, ADR-041), stored as <c>config.json</c> (ADR-006).</summary>
 public sealed record NeoFencesConfig
 {
     /// <summary>
-    /// 4 since v1.8 (M17): the auto-update switch. 3 since v1.7 (M14): appearance settings and per-fence colours. 2 since v1.6 (M13a): tabs, rules and the
-    /// library. An older NeoFences reads a newer number as read-only and never saves over it (it would drop the fields).
+    /// 5 since v0.9 (M18): virtual items, no Inbox, Portals or rules (older files start fresh, ConfigStore). 4 since M17: the
+    /// auto-update switch. 3 since M14: appearance. 2 since M13a: tabs and the library. An older NeoFences reads a newer
+    /// number as read-only and never saves over it (it would drop the fields).
     /// </summary>
-    public const int CurrentSchemaVersion = 4;
+    public const int CurrentSchemaVersion = 5;
 
     public int SchemaVersion { get; init; } = CurrentSchemaVersion;
     public Settings Settings { get; init; } = new();
     public IReadOnlyList<Fence> Fences { get; init; } = [];
     public IReadOnlyDictionary<string, Layout> Layouts { get; init; } = new Dictionary<string, Layout>();
 
-    /// <summary>Rules auto-sort (M11), in order: the first matching enabled rule decides a new item's fence.</summary>
-    public IReadOnlyList<Rule> Rules { get; init; } = [];
-
     /// <summary>Game Library settings (M12): game folders, sources, hidden games.</summary>
     public LibrarySettings Library { get; init; } = new();
 
     /// <summary>Fingerprint of the display configuration seen last; new configurations are derived from it.</summary>
     public string? LastLayoutFingerprint { get; init; }
 
-    /// <summary>The single Inbox fence. Guaranteed to exist after <c>ConfigNormalizer.Normalize</c>.</summary>
-    [JsonIgnore]
-    public Fence Inbox => Fences.First(fence => fence.IsInbox);
-
-    public static NeoFencesConfig CreateDefault() =>
-        new() { Fences = [Fence.Create("Inbox") with { IsInbox = true }] };
+    /// <summary>First run (spec §5): one empty fence; its hint says how to fill it.</summary>
+    public static NeoFencesConfig CreateDefault() => new() { Fences = [Fence.Create("Fence")] };
 
     public NeoFencesConfig WithFence(Fence updated) =>
         this with { Fences = Fences.Select(fence => fence.Id == updated.Id ? updated : fence).ToList() };
diff --git a/src/NeoFences.Core/Model/Settings.cs b/src/NeoFences.Core/Model/Settings.cs
index 52fae44..92dce79 100644
--- a/src/NeoFences.Core/Model/Settings.cs
+++ b/src/NeoFences.Core/Model/Settings.cs
@@ -4,10 +4,11 @@ public enum RollupExpand { Hover, Click }
 
 public sealed record Settings
 {
-    /// <summary>Hide native desktop icons and show them in fences. Off by default until the M2 sign-out test passes (ADR-011).</summary>
-    public bool Takeover { get; init; }
-    /// <summary>The user answered the one-time "hide desktop icons?" banner in the Inbox (M2b first run).</summary>
-    public bool TakeoverPromptAnswered { get; init; }
+    /// <summary>
+    /// "Hide desktop icons while NeoFences runs" (M18, off by default): Windows' own setting, shown again on exit, crash and
+    /// Task Manager kill (the watchdog, hard rule 2).
+    /// </summary>
+    public bool HideDesktopIcons { get; init; }
     public string PeekHotkey { get; init; } = "Ctrl+Alt+Space";
     public bool StartWithWindows { get; init; } = true;
     public bool GameMode { get; init; } = true;
diff --git a/src/NeoFences.Core/Model/Snapshots.cs b/src/NeoFences.Core/Model/Snapshots.cs
index 112f110..a566d66 100644
--- a/src/NeoFences.Core/Model/Snapshots.cs
+++ b/src/NeoFences.Core/Model/Snapshots.cs
@@ -1,10 +1,11 @@
 using NeoFences.Core.Config;
+using NeoFences.Core.Items;
 
 namespace NeoFences.Core.Model;
 
 /// <summary>
-/// A saved arrangement (M10, spec 2026-10-03-snapshots-design): every fence with every field (items, tabs, colours,
-/// roll-up, lock) and every monitor setup's places. No Settings, no files. Stored as a file of its own.
+/// A saved arrangement (M10, spec 2026-10-03-snapshots-design): every fence with every field (tabs, colours, roll-up,
+/// lock), every fence's virtual items (M18) and every monitor setup's places. No Settings, no files. Stored as a file of its own.
 /// </summary>
 public sealed record Snapshot
 {
@@ -14,9 +15,12 @@ public sealed record Snapshot
     public IReadOnlyList<Fence> Fences { get; init; } = [];
     public IReadOnlyDictionary<string, Layout> Layouts { get; init; } = new Dictionary<string, Layout>();
     public string? LastLayoutFingerprint { get; init; }
+
+    /// <summary>Every fence's items (M18), as in items.json.</summary>
+    public IReadOnlyDictionary<string, IReadOnlyList<VirtualItem>> Items { get; init; } = new Dictionary<string, IReadOnlyList<VirtualItem>>();
 }
 
-/// <summary>Taking and restoring snapshots (M10). Pure: the App supplies the desktop listing and saves the result.</summary>
+/// <summary>Taking and restoring snapshots (M10). Pure: the App saves the result.</summary>
 public static class Snapshots
 {
     private const int MenuLabelLength = 60;
@@ -37,52 +41,29 @@ public static class Snapshots
         return clean.Replace("&", "&&");
     }
 
-    public static Snapshot Take(NeoFencesConfig config, string name, DateTimeOffset now) => new()
+    public static Snapshot Take(NeoFencesConfig config, ItemsDocument items, string name, DateTimeOffset now) => new()
     {
         Name = name,
         TakenAt = now,
         Fences = config.Fences,
         Layouts = config.Layouts,
         LastLayoutFingerprint = config.LastLayoutFingerprint,
+        Items = items.Fences,
     };
 
     /// <summary>
-    /// The arrangement of <paramref name="snapshot"/> applied to <paramref name="current"/> (spec §3): the snapshot's fences
-    /// and places; its icons back where they were if still on the desktop (<paramref name="desktopNow"/>); icons it does
-    /// not know stay in their current fence when that fence survives, else go to the Inbox; Settings and setups the
-    /// snapshot never saw stay as they are.
+    /// The arrangement of <paramref name="snapshot"/> applied to <paramref name="current"/> (spec §3; M18: items as saved):
+    /// the snapshot's fences, places and items; Settings and setups the snapshot never saw stay as they are. Items of
+    /// fences the snapshot does not have are dropped (a damaged or hand-edited file).
     /// </summary>
-    public static NeoFencesConfig Restore(NeoFencesConfig current, Snapshot snapshot, IEnumerable<string> desktopNow)
+    public static (NeoFencesConfig Config, ItemsDocument Items) Restore(NeoFencesConfig current, Snapshot snapshot)
     {
-        var desktop = desktopNow.ToList(); // the Desktop's own order for newcomers (M13a: not a HashSet's order)
-        var present = desktop.ToHashSet(ItemRef.Comparer);
-        // The snapshot file may be damaged or hand-edited: the normalizer gives it one Inbox, no duplicates, sane tabs.
+        // The snapshot file may be damaged or hand-edited: the normalizer gives it unique ids and sane tabs.
         var saved = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = snapshot.Fences, Layouts = snapshot.Layouts });
-        var fences = saved.Fences
-            .Select(fence => fence.Source.Kind == FenceSourceKind.Desktop ? fence with { Items = fence.Items.Where(present.Contains).ToList() } : fence)
-            .ToList();
-        var placed = fences.SelectMany(fence => fence.Items).ToHashSet(ItemRef.Comparer);
-        var desktopFenceIds = fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop).Select(fence => fence.Id).ToHashSet(StringComparer.Ordinal);
-        var inboxId = fences.First(fence => fence.IsInbox).Id;
-
-        // Newer icons, in their current order: their fence if it survives, else the Inbox; unfenced ones to the Inbox too.
-        var arrivals = new List<(string FenceId, string ItemRef)>();
-        foreach (var fence in current.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop))
-        {
-            foreach (var itemRef in fence.Items.Where(itemRef => present.Contains(itemRef) && placed.Add(itemRef)))
-            {
-                arrivals.Add((desktopFenceIds.Contains(fence.Id) ? fence.Id : inboxId, itemRef));
-            }
-        }
-        arrivals.AddRange(desktop.Where(placed.Add).Select(itemRef => (inboxId, itemRef)));
-        fences = fences.Select(fence =>
-        {
-            var joining = arrivals.Where(arrival => arrival.FenceId == fence.Id).Select(arrival => arrival.ItemRef).ToList();
-            return joining.Count == 0 ? fence : fence with { Items = [.. fence.Items, .. joining] };
-        }).ToList();
-
         var layouts = new Dictionary<string, Layout>(current.Layouts);
         foreach (var (fingerprint, layout) in saved.Layouts) layouts[fingerprint] = layout;
-        return ConfigNormalizer.Normalize(current with { Fences = fences, Layouts = layouts });
+        var config = ConfigNormalizer.Normalize(current with { Fences = saved.Fences, Layouts = layouts });
+        var items = ItemEdits.Repair(new ItemsDocument { Fences = snapshot.Items ?? new Dictionary<string, IReadOnlyList<VirtualItem>>() });
+        return (config, ItemEdits.Prune(items, config.Fences.Select(fence => fence.Id).ToHashSet(StringComparer.Ordinal)));
     }
 }
````

- [ ] **Step 4: Run the tests.**
  Run: `dotnet test tests/NeoFences.Core.Tests`
  Expected: `Passed! - Failed: 0, Passed: 400`.

- [ ] **Step 5: Commit.**
  `git add -A src/NeoFences.Core tests/NeoFences.Core.Tests && git commit -m "refactor: moved Core to config schema 5 with items.json and removed the desktop-membership model"`

### Task 4: Shell — drops create items, drag-out copies, pickers, probe, Windows' menu header

**Files:**
- Create: `src/NeoFences.Shell/PathPicker.cs` (`PathPicker`, `IconPicker`; replaces `FolderPicker.cs`),
  `src/NeoFences.Shell/TargetProbe.cs`
- Modify: `ShellDragDrop.cs` (rewritten), `ShellItems.cs`, `ShellItemMenu.cs`, `FolderItems.cs`, `DesktopNamespace.cs`,
  `DesktopItems.cs`, `DeviceRemovalNotice.cs`, `GameScanners.cs`, `ShellFileOps.cs`, `Watchdog.cs`, `NativeMethods.txt`
  (all in `src/NeoFences.Shell/`)
- Delete: `src/NeoFences.Shell/{DesktopWatcher,FolderPicker,ItemFactsReader}.cs`

**Interfaces:**
- Consumes: Task 1–3 (`ItemKinds`, `TargetChecks`, `TargetCheck`, `GameLaunchers`).
- Produces: `FenceDropHandlers(Func<int,int,int> HitTest, Func<bool> AcceptsDrops, Action<IReadOnlyList<string>,int,bool>
  ItemsDropped, Action<IReadOnlyList<string>,int> TargetsDropped, Action<int?> ShowFeedback, Action<Exception> LogFailure)`;
  `ShellDragDrop.TryDrag(nint owner, IReadOnlyList<string> keys, IReadOnlyList<string> files, IReadOnlyList<string> urls,
  Action<Exception> logFailure)`, `ShellDragDrop.RegisterFence(nint, FenceDropHandlers) → IDisposable`;
  `ShellItems.TryOpen(string target, nint owner, string? arguments = null, bool runAsAdmin = false)`,
  `ShellItems.TryShowInFolder(string)`, `ShellItems.DefaultBrowserPath()`, `ShellItems.TryGetFileIcon(file, index, sizePx)`;
  `enum ItemMenuChoice { None, Delete, Custom }`, `ShellItemMenu.Show(owner, itemRefs, x, y, extended, logFailure,
  string? header, IReadOnlyList<string> customCommands, bool handDeleteBack, out int customCommand)`;
  `PathPicker.TryPickFolder(owner, title, logFailure, startFolder?)`, `PathPicker.TryPickFile(owner, title, logFailure,
  startFolder?, filters?)`; `IconPicker.TryPick(owner, file?, index) → (File, Index)?`; `TargetProbe.Check(target) →
  TargetCheck` (`NetworkTimeout` 2 s); `FolderWatcher.Renamed(oldPath, newPath)`; `Watchdog.IsIconsHiddenMarked`,
  `Watchdog.SetIconsHiddenMarker(bool)` (marker file `icons-hidden`).

- [ ] **Step 1: Remove the parked Shell code and apply the patch.**

```bash
git rm -q src/NeoFences.Shell/DesktopWatcher.cs src/NeoFences.Shell/FolderPicker.cs src/NeoFences.Shell/ItemFactsReader.cs
```

````diff
diff --git a/src/NeoFences.Shell/DesktopItems.cs b/src/NeoFences.Shell/DesktopItems.cs
index af0b355..3ec8ca9 100644
--- a/src/NeoFences.Shell/DesktopItems.cs
+++ b/src/NeoFences.Shell/DesktopItems.cs
@@ -26,9 +26,6 @@ public static class DesktopItems
     ];
 
     // DoNotVerify: a missing (offline, unmounted) folder still has a path, so its items are recognised as unknown, not gone.
-    /// <summary>The Recycle Bin's item ref.</summary>
-    public const string RecycleBinRef = "::{645FF040-5081-101B-9F08-00AA002F954E}";
-
     public static string UserDesktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolderOption.DoNotVerify);
 
     public static string PublicDesktop => Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory, Environment.SpecialFolderOption.DoNotVerify);
diff --git a/src/NeoFences.Shell/DesktopNamespace.cs b/src/NeoFences.Shell/DesktopNamespace.cs
index aaf6ac2..2666a9b 100644
--- a/src/NeoFences.Shell/DesktopNamespace.cs
+++ b/src/NeoFences.Shell/DesktopNamespace.cs
@@ -1,20 +1,19 @@
 using System.Runtime.InteropServices;
 using Windows.Win32;
 using Windows.Win32.Foundation;
-using Windows.Win32.System.SystemServices;
 using Windows.Win32.UI.Shell;
 using Windows.Win32.UI.Shell.Common;
 
 namespace NeoFences.Shell;
 
 /// <summary>
-/// Items as children of a shell folder, for item menus, drag data and per-item drop targets. Desktop items go through
-/// the desktop folder, which merges the user's and the Public Desktop (a file name or <c>::{CLSID}</c> is a direct
-/// child); a Portal's items go through their own folder (M4). One call's items must share that parent.
+/// Items as children of a shell folder, for Windows' item menu (Shift+right-click, the Game Library). Desktop items go
+/// through the desktop folder, which merges the user's and the Public Desktop (a file name or <c>::{CLSID}</c> is a direct
+/// child); others through their own folder. One call's items must share that parent.
 /// </summary>
 internal static class DesktopNamespace
 {
-    /// <summary>A UI object (IContextMenu, IDataObject, IDropTarget, …) for these items; the caller releases it.</summary>
+    /// <summary>A UI object (IContextMenu, …) for these items; the caller releases it.</summary>
     /// <exception cref="ArgumentException">The items are not all on the desktop or all in one folder.</exception>
     public static unsafe object GetUIObject(HWND owner, IReadOnlyList<string> itemRefs, Guid interfaceId)
     {
@@ -34,47 +33,6 @@ internal static class DesktopNamespace
         }
     }
 
-    /// <summary>The Desktop folder's own drop target: what Explorer does when something is dropped on the desktop.</summary>
-    public static unsafe object DesktopDropTarget(HWND owner)
-    {
-        PInvoke.SHGetDesktopFolder(out var desktop).ThrowOnFailure();
-        return DropTargetOf(owner, desktop);
-    }
-
-    /// <summary>A folder's own drop target (a Portal's folder): Windows moves/copies into it, with its dialogs and Undo.</summary>
-    public static object FolderDropTarget(HWND owner, string folderPath) => DropTargetOf(owner, FolderObject(folderPath));
-
-    /// <summary>
-    /// True for containers that take drops themselves: folders, zips and Recycle Bin. Files (even ones Windows lists as
-    /// drop targets, like programs or text files) are not: dropping on them reorders the fence instead (M3b).
-    /// </summary>
-    public static unsafe bool IsDropContainer(HWND owner, string itemRef)
-    {
-        try
-        {
-            var parent = ParentFolder([itemRef]);
-            var childIds = ChildIds(owner, parent, [itemRef]);
-            try
-            {
-                var attributes = (uint)(SFGAO_FLAGS.SFGAO_DROPTARGET | SFGAO_FLAGS.SFGAO_FOLDER);
-                fixed (nint* ids = childIds.ToArray())
-                {
-                    parent.GetAttributesOf(1, (ITEMIDLIST**)ids, ref attributes);
-                }
-                const uint Container = (uint)(SFGAO_FLAGS.SFGAO_DROPTARGET | SFGAO_FLAGS.SFGAO_FOLDER);
-                return (attributes & Container) == Container;
-            }
-            finally
-            {
-                foreach (var childId in childIds) Marshal.FreeCoTaskMem(childId);
-            }
-        }
-        catch (Exception failure) when (failure is not OutOfMemoryException)
-        {
-            return false;
-        }
-    }
-
     /// <summary>A special icon, or a file or folder directly on the user's or the Public Desktop.</summary>
     public static bool IsDesktopItem(string itemRef) =>
         itemRef.StartsWith("::", StringComparison.Ordinal) || IsDesktopFolder(Path.GetDirectoryName(itemRef));
@@ -93,13 +51,6 @@ internal static class DesktopNamespace
                || string.Equals(trimmed, DesktopItems.PublicDesktop.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
     }
 
-    private static unsafe object DropTargetOf(HWND owner, IShellFolder folder)
-    {
-        var dropTargetId = typeof(Windows.Win32.System.Ole.IDropTarget).GUID;
-        folder.CreateViewObject(owner, &dropTargetId, out var dropTarget);
-        return dropTarget;
-    }
-
     private static IShellFolder ParentFolder(IReadOnlyList<string> itemRefs)
     {
         if (itemRefs.Count == 0) throw new ArgumentException("No items.", nameof(itemRefs));
diff --git a/src/NeoFences.Shell/DeviceRemovalNotice.cs b/src/NeoFences.Shell/DeviceRemovalNotice.cs
index 2b7cc21..58e03b1 100644
--- a/src/NeoFences.Shell/DeviceRemovalNotice.cs
+++ b/src/NeoFences.Shell/DeviceRemovalNotice.cs
@@ -7,9 +7,9 @@ using Windows.Win32.UI.WindowsAndMessaging;
 namespace NeoFences.Shell;
 
 /// <summary>
-/// "Safely Remove" / Eject for a Portal's drive (M8d, M4 carry-over): any open handle on a drive vetoes its removal, and
-/// a Portal keeps two (its folder watcher and its parent's). Windows asks every window registered for a handle on the
-/// drive first (DBT_DEVICEQUERYREMOVE to the owner window); the Portal then closes everything it holds there.
+/// "Safely Remove" / Eject for a drive NeoFences watches (M8d; item targets, the library): any open handle on a drive vetoes
+/// its removal, and a watcher keeps two (the folder and its parent). Windows asks every window registered for a handle on
+/// the drive first (DBT_DEVICEQUERYREMOVE to the owner window); NeoFences then closes everything it holds there.
 /// </summary>
 public sealed class DeviceRemovalNotice : IDisposable
 {
@@ -62,7 +62,7 @@ public sealed class DeviceRemovalNotice : IDisposable
         }
         catch (Exception failure) when (failure is not OutOfMemoryException)
         {
-            logFailure(failure); // the drive then just cannot be removed safely while the Portal shows it (as before)
+            logFailure(failure); // the drive then just cannot be removed safely while it is watched (as before)
             return null;
         }
     }
@@ -77,7 +77,7 @@ public sealed class DeviceRemovalNotice : IDisposable
 
     private bool _disposed;
 
-    /// <summary>Safe to call twice (a Portal can let go of an in-flight notice and then dispose it again, M13b).</summary>
+    /// <summary>Safe to call twice (a lister can let go of an in-flight notice and then dispose it again, M13b).</summary>
     public void Dispose()
     {
         if (_disposed) return;
diff --git a/src/NeoFences.Shell/FolderItems.cs b/src/NeoFences.Shell/FolderItems.cs
index 4345c3b..b4cce56 100644
--- a/src/NeoFences.Shell/FolderItems.cs
+++ b/src/NeoFences.Shell/FolderItems.cs
@@ -2,7 +2,7 @@ using NeoFences.Core.Model;
 
 namespace NeoFences.Shell;
 
-/// <summary>The contents of a Portal's folder, and the facts sorting needs about any item (M4).</summary>
+/// <summary>A folder's contents (the Game Library's folder), and the facts "Sort by" needs about any target (M4).</summary>
 public static class FolderItems
 {
     /// <summary>Visible entries (not Hidden or System, like Explorer), or null when the folder cannot be read (missing, offline, denied).</summary>
@@ -21,7 +21,7 @@ public static class FolderItems
         }
     }
 
-    /// <summary>Sorting facts for items of a desktop fence ("Sort by", one time). Special items sort as folders by name.</summary>
+    /// <summary>Sorting facts for targets ("Sort by", one time). Special items sort as folders by name.</summary>
     public static IReadOnlyList<ItemInfo> Describe(IEnumerable<string> itemRefs) => itemRefs.Select(itemRef =>
     {
         if (itemRef.StartsWith("::", StringComparison.Ordinal))
@@ -41,13 +41,15 @@ public static class FolderItems
     }
 }
 
-/// <summary>Says when a folder's contents changed (a Portal re-lists it). Events arrive on thread-pool threads.</summary>
+/// <summary>Says when a folder's contents changed (the library rescans, items are re-checked). Events arrive on thread-pool threads.</summary>
 public sealed class FolderWatcher : IDisposable
 {
     private readonly FileSystemWatcher? _watcher;
     private readonly FileSystemWatcher? _parentWatcher; // the folder itself renamed, moved or deleted (and back)
 
     public event Action? Changed;
+    /// <summary>An entry of the folder, or the folder itself, was renamed in place: old and new full path (items follow it, M18).</summary>
+    public event Action<string, string>? Renamed;
     /// <summary>The watcher lost events or stopped (a drive going away, a network hiccup): re-list and re-arm, with backoff (M8d).</summary>
     public event Action? Failed;
 
@@ -55,7 +57,7 @@ public sealed class FolderWatcher : IDisposable
     public bool HasFailed => _hasFailed;
     private volatile bool _hasFailed;
 
-    /// <summary>False when the folder itself could not be watched (missing, offline): the Portal then retries.</summary>
+    /// <summary>False when the folder itself could not be watched (missing, offline): the owner then retries.</summary>
     public bool IsWatching => _watcher is not null;
 
     /// <summary>
@@ -64,7 +66,7 @@ public sealed class FolderWatcher : IDisposable
     /// </summary>
     public string? HeldFolder { get; private set; }
 
-    /// <param name="logFailure">Told when the folder cannot be watched; the Portal then only refreshes on navigation.</param>
+    /// <param name="logFailure">Told when the folder cannot be watched; its items are then only re-checked.</param>
     /// <param name="directoriesOnly">Only folders appearing, going or renamed: a game folder whose games write files at their
     /// own root would otherwise rescan the library while they run (M13b).</param>
     public FolderWatcher(string folderPath, Action<Exception> logFailure, bool directoriesOnly = false)
@@ -79,7 +81,11 @@ public sealed class FolderWatcher : IDisposable
             };
             _watcher.Created += (_, _) => Changed?.Invoke();
             _watcher.Deleted += (_, _) => Changed?.Invoke();
-            _watcher.Renamed += (_, _) => Changed?.Invoke();
+            _watcher.Renamed += (_, renamed) =>
+            {
+                Renamed?.Invoke(renamed.OldFullPath, renamed.FullPath);
+                Changed?.Invoke();
+            };
             _watcher.Changed += (_, _) => Changed?.Invoke();
             _watcher.Error += (_, _) =>
             {
@@ -93,8 +99,8 @@ public sealed class FolderWatcher : IDisposable
         {
             logFailure(failure);
         }
-        // A watcher follows its directory when that is renamed and reports nothing, so the Portal would keep showing
-        // a folder that is gone (M4 smoke). The parent tells when the folder itself goes away or comes back.
+        // A watcher follows its directory when that is renamed and reports nothing (M4 smoke). The parent tells when the
+        // folder itself goes away, comes back or is renamed.
         try
         {
             var trimmed = folderPath.TrimEnd('\\', '/');
@@ -107,7 +113,11 @@ public sealed class FolderWatcher : IDisposable
                 };
                 _parentWatcher.Created += (_, _) => Changed?.Invoke();
                 _parentWatcher.Deleted += (_, _) => Changed?.Invoke();
-                _parentWatcher.Renamed += (_, _) => Changed?.Invoke();
+                _parentWatcher.Renamed += (_, renamed) =>
+                {
+                    Renamed?.Invoke(renamed.OldFullPath, renamed.FullPath);
+                    Changed?.Invoke();
+                };
                 _parentWatcher.EnableRaisingEvents = true;
                 HeldFolder ??= parent;
             }
diff --git a/src/NeoFences.Shell/GameScanners.cs b/src/NeoFences.Shell/GameScanners.cs
index 705ebe0..8d8768c 100644
--- a/src/NeoFences.Shell/GameScanners.cs
+++ b/src/NeoFences.Shell/GameScanners.cs
@@ -397,10 +397,10 @@ public static class GameScanners
             if (launch is null) continue;
             var gameFolder = gameFolders.Select(root => GameFolderOf(root, launch.Target)).FirstOrDefault(found => found is not null);
             var asText = launch.Arguments is null ? launch.Target : $"{launch.Target} {launch.Arguments}";
-            if (gameFolder is null && Rules.LauncherOf(asText) is null) continue;
+            if (gameFolder is null && GameLaunchers.LauncherOf(asText) is null) continue;
             // The target's folder only when the target is a game itself (under a game library): steam.exe, Battle.net.exe or
             // GalaxyClient.exe started with a game argument have no folder of their own game (final review I2).
-            var installFolder = gameFolder ?? (launch.IsLink || Rules.LauncherOf(launch.Target) is null ? null : Path.GetDirectoryName(launch.Target));
+            var installFolder = gameFolder ?? (launch.IsLink || GameLaunchers.LauncherOf(launch.Target) is null ? null : Path.GetDirectoryName(launch.Target));
             found.Add(new GameEntry("desktop:" + itemRef.ToLowerInvariant(), Path.GetFileNameWithoutExtension(itemRef), GameSource.DesktopShortcut, "desktop",
                 launch, installFolder, Poster: null, IconPath: launch.IsLink ? null : launch.Target) { ShortcutFile = itemRef });
         }
diff --git a/src/NeoFences.Shell/NativeMethods.txt b/src/NeoFences.Shell/NativeMethods.txt
index 38eb24f..a6327d5 100644
--- a/src/NeoFences.Shell/NativeMethods.txt
+++ b/src/NeoFences.Shell/NativeMethods.txt
@@ -7,7 +7,6 @@ BITMAPINFO
 CallNextHookEx
 CLIPBOARD_FORMAT
 CLSID_DragDropHelper
-CMF_CANRENAME
 CMF_EXTENDEDVERBS
 CMF_NORMAL
 CMIC_MASK_PTINVOKE
@@ -182,3 +181,14 @@ IDesktopWallpaper
 DesktopWallpaper
 ChooseColor
 CHOOSECOLOR_FLAGS
+PickIconDlg
+SHDefExtractIcon
+GetIconInfo
+ICONINFO
+AssocQueryString
+ASSOCF
+ASSOCSTR
+SHParseDisplayName
+SHCreateDataObject
+InsertMenu
+COMDLG_FILTERSPEC
diff --git a/src/NeoFences.Shell/PathPicker.cs b/src/NeoFences.Shell/PathPicker.cs
new file mode 100644
index 0000000..a8b70aa
--- /dev/null
+++ b/src/NeoFences.Shell/PathPicker.cs
@@ -0,0 +1,96 @@
+using System.Runtime.InteropServices;
+using Windows.Win32;
+using Windows.Win32.Foundation;
+using Windows.Win32.UI.Controls.Dialogs;
+using Windows.Win32.UI.Shell;
+using Windows.Win32.UI.Shell.Common;
+
+namespace NeoFences.Shell;
+
+/// <summary>Windows' own "Open" dialog for a file or a folder (Add item…, Properties → Browse, Locate…, the library's folders).</summary>
+public static class PathPicker
+{
+    /// <returns>The chosen folder's path, or null when cancelled, the choice has no file-system path, or the dialog failed.</returns>
+    /// <param name="startFolder">Where the dialog opens (Locate…: the nearest folder that still exists), or null for Windows' choice.</param>
+    public static string? TryPickFolder(nint ownerHandle, string title, Action<Exception> logFailure, string? startFolder = null) =>
+        Pick(ownerHandle, title, logFailure, startFolder, pickFolders: true, filters: []);
+
+    /// <param name="filters">(name, patterns) pairs such as ("Pictures", "*.png;*.jpg"); empty: every file.</param>
+    public static string? TryPickFile(nint ownerHandle, string title, Action<Exception> logFailure, string? startFolder = null,
+        IReadOnlyList<(string Name, string Patterns)>? filters = null) =>
+        Pick(ownerHandle, title, logFailure, startFolder, pickFolders: false, filters: filters ?? []);
+
+    private static unsafe string? Pick(nint ownerHandle, string title, Action<Exception> logFailure, string? startFolder, bool pickFolders,
+        IReadOnlyList<(string Name, string Patterns)> filters)
+    {
+        IFileOpenDialog? dialog = null;
+        var pinned = new List<GCHandle>();
+        try
+        {
+            dialog = (IFileOpenDialog)Activator.CreateInstance(Type.GetTypeFromCLSID(typeof(FileOpenDialog).GUID)!)!;
+            dialog.GetOptions(out var options);
+            // No dereferencing: a picked shortcut stays the shortcut (its own item), as the user chose it.
+            options |= FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM | FILEOPENDIALOGOPTIONS.FOS_NODEREFERENCELINKS;
+            if (pickFolders) options |= FILEOPENDIALOGOPTIONS.FOS_PICKFOLDERS;
+            dialog.SetOptions(options);
+            dialog.SetTitle(title);
+            if (filters.Count > 0)
+            {
+                var specs = new COMDLG_FILTERSPEC[filters.Count];
+                for (var index = 0; index < filters.Count; index++)
+                {
+                    var name = GCHandle.Alloc(filters[index].Name + "\0", GCHandleType.Pinned);
+                    var patterns = GCHandle.Alloc(filters[index].Patterns + "\0", GCHandleType.Pinned);
+                    pinned.Add(name);
+                    pinned.Add(patterns);
+                    specs[index] = new COMDLG_FILTERSPEC
+                    {
+                        pszName = (char*)name.AddrOfPinnedObject(),
+                        pszSpec = (char*)patterns.AddrOfPinnedObject(),
+                    };
+                }
+                dialog.SetFileTypes(specs);
+            }
+            if (startFolder is not null && PInvoke.SHCreateItemFromParsingName(startFolder, null, out IShellItem folder).Succeeded)
+            {
+                dialog.SetFolder(folder);
+                Marshal.ReleaseComObject(folder);
+            }
+            dialog.Show((HWND)ownerHandle); // throws ERROR_CANCELLED when the user cancels
+            dialog.GetResult(out var item);
+            return ShellItems.FileSystemPath(item);
+        }
+        catch (Exception failure) when (failure is not OutOfMemoryException)
+        {
+            if (failure.HResult != unchecked((int)0x800704C7)) logFailure(failure); // HRESULT_FROM_WIN32(ERROR_CANCELLED)
+            return null;
+        }
+        finally
+        {
+            foreach (var handle in pinned) handle.Free();
+            if (dialog is not null) Marshal.ReleaseComObject(dialog);
+        }
+    }
+}
+
+/// <summary>Windows' "Change Icon" dialog (Properties → Change icon → From a file…): an icon in an .ico, .exe or .dll.</summary>
+public static class IconPicker
+{
+    private const int MaxPath = 260;
+
+    /// <param name="file">The file to show first (the item's own icon file, or its target); null: shell32.dll.</param>
+    /// <returns>The chosen file (environment variables expanded) and index, or null when cancelled.</returns>
+    public static unsafe (string File, int Index)? TryPick(nint ownerHandle, string? file, int index)
+    {
+        var buffer = new char[MaxPath];
+        var start = file ?? @"%SystemRoot%\System32\shell32.dll";
+        if (start.Length >= MaxPath) start = @"%SystemRoot%\System32\shell32.dll";
+        start.CopyTo(buffer);
+        var chosen = index;
+        fixed (char* path = buffer)
+        {
+            if (PInvoke.PickIconDlg((HWND)ownerHandle, path, MaxPath, &chosen) == 0) return null;
+            return (Environment.ExpandEnvironmentVariables(new string(path)), chosen);
+        }
+    }
+}
diff --git a/src/NeoFences.Shell/ShellDragDrop.cs b/src/NeoFences.Shell/ShellDragDrop.cs
index 60dabd1..f0cde60 100644
--- a/src/NeoFences.Shell/ShellDragDrop.cs
+++ b/src/NeoFences.Shell/ShellDragDrop.cs
@@ -1,62 +1,67 @@
 using System.Runtime.InteropServices;
+using NeoFences.Core.Items;
 using Windows.Win32;
 using Windows.Win32.Foundation;
 using Windows.Win32.System.Com;
 using Windows.Win32.System.Ole;
 using Windows.Win32.System.SystemServices;
 using Windows.Win32.UI.Shell;
+using Windows.Win32.UI.Shell.Common;
 using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
+using ComFormat = System.Runtime.InteropServices.ComTypes.FORMATETC;
+using ComMedium = System.Runtime.InteropServices.ComTypes.STGMEDIUM;
 
 namespace NeoFences.Shell;
 
-/// <summary>Where a drop on a fence lands, asked of the fence for a screen point (physical pixels).</summary>
-/// <param name="ItemRef">The item whose "drop into" zone is under the point (Core DropZones), if any.</param>
-/// <param name="InsertAt">Index in the fence's shown list to insert before (see FenceMembership.MoveItems).</param>
-public readonly record struct FenceDropPoint(string? ItemRef, int InsertAt);
-
 /// <summary>What a fence does with drops; NeoFences.App supplies these (all called on the UI thread).</summary>
-/// <param name="HitTest">Item and insert position under a screen point.</param>
-/// <param name="MoveItems">Desktop items dropped on the fence (from a fence or from Explorer's Desktop): membership only, no file operation.</param>
-/// <param name="ExpectArrivals">Desktop refs Windows is about to copy or move onto the Desktop for this drop, and where they go.</param>
-/// <param name="Recycle">Files dropped on the Recycle Bin item: always recycled by NeoFences, never deleted (hard rule 1).</param>
-/// <param name="ShowFeedback">Where the drop would land (null hides it): an insertion caret, or a highlighted container when into is true.</param>
-/// <param name="LogFailure">A drop that could not be handed to Windows.</param>
+/// <param name="HitTest">The insert position under a screen point (physical pixels); hovering a tab header shows that tab.</param>
+/// <param name="AcceptsDrops">False for the Game Library (it shows NeoFences' own shortcuts only).</param>
+/// <param name="ItemsDropped">Keys dragged out of a fence (this process), the insert position, and Ctrl held (duplicate).</param>
+/// <param name="TargetsDropped">Paths, or one website URL, dragged in from outside (Explorer, the desktop, a browser).</param>
+/// <param name="ShowFeedback">The insert caret's position, or null to hide it.</param>
+/// <param name="LogFailure">A drop that could not be read.</param>
 public sealed record FenceDropHandlers(
-    Func<int, int, FenceDropPoint> HitTest,
-    Action<IReadOnlyList<string>, int> MoveItems,
-    Action<IReadOnlyList<string>, int> ExpectArrivals,
-    Action<IReadOnlyList<string>> Recycle,
-    Action<FenceDropPoint?, bool> ShowFeedback,
-    Action<Exception> LogFailure,
-    Func<bool>? AcceptsDrops = null);
+    Func<int, int, int> HitTest,
+    Func<bool> AcceptsDrops,
+    Action<IReadOnlyList<string>, int, bool> ItemsDropped,
+    Action<IReadOnlyList<string>, int> TargetsDropped,
+    Action<int?> ShowFeedback,
+    Action<Exception> LogFailure);
 
 /// <summary>
-/// Drag-drop with Windows' own engine (spec §6, M3b). Dragging out uses the shell's data object for the items, so
-/// apps and Explorer get real files (copy/move/link as they decide) and Windows draws the drag image. Drops on a fence:
-/// <list type="bullet">
-/// <item>desktop items (from any fence, or Explorer showing the Desktop folder) only change membership: no file operation;</item>
-/// <item>on an item that takes drops (folder, Recycle Bin, program) they go to that item, as on the desktop;</item>
-/// <item>anything else goes to the Desktop folder's own drop target (Windows copies/moves, with progress and Undo),
-/// and the arriving files are placed in this fence at the drop position.</item>
-/// </list>
+/// Drag-drop with Windows' own engine (M3b; M18, ADR-040: virtual items). A drop on a fence only ever creates or moves
+/// NeoFences' items: an outside drag is answered as a link, never a move, so Windows never moves or deletes the source
+/// (bug K5 cannot happen). Dragging out offers copy or link only: Explorer copies, the original never leaves.
 /// </summary>
 public static class ShellDragDrop
 {
-    /// <summary>Items being dragged out of a fence right now (one drag at a time; drops on fences are membership moves).</summary>
+    /// <summary>The keys being dragged out of a fence right now (one drag at a time).</summary>
     internal static IReadOnlyList<string>? CurrentDrag { get; private set; }
 
-    /// <summary>Starts a drag of these items; returns when it ends. The drag image, cursor and effects are Windows'.</summary>
-    /// <returns>False when the drag could not start (an item vanished, a broken shell extension).</returns>
-    /// <param name="copyOnly">Copy or link only, never move (a Game Library shortcut stays in the library, M12).</param>
-    public static unsafe bool TryDrag(nint ownerHandle, IReadOnlyList<string> itemRefs, Action<Exception> logFailure, bool copyOnly = false)
+    private static readonly ushort UrlFormat = (ushort)PInvoke.RegisterClipboardFormat("UniformResourceLocatorW");
+    private static readonly ushort FileGroupDescriptorFormat = (ushort)PInvoke.RegisterClipboardFormat("FileGroupDescriptorW");
+    private const ushort UnicodeTextFormat = 13; // CF_UNICODETEXT
+
+    /// <summary>
+    /// Starts a drag of fence items; returns when it ends. Other apps get the <paramref name="files"/> (Windows' own data:
+    /// Explorer copies them) or, with no files, the <paramref name="urls"/> as links; fences get the <paramref name="keys"/>.
+    /// </summary>
+    /// <returns>False when the drag could not start.</returns>
+    public static bool TryDrag(nint ownerHandle, IReadOnlyList<string> keys, IReadOnlyList<string> files, IReadOnlyList<string> urls,
+        Action<Exception> logFailure)
     {
         ComDataObject? dataObject = null;
         try
         {
-            dataObject = (ComDataObject)DesktopNamespace.GetUIObject((HWND)ownerHandle, itemRefs, typeof(ComDataObject).GUID);
-            CurrentDrag = itemRefs;
-            PInvoke.SHDoDragDrop((HWND)ownerHandle, dataObject, null,
-                DROPEFFECT.DROPEFFECT_COPY | DROPEFFECT.DROPEFFECT_LINK | (copyOnly ? 0 : DROPEFFECT.DROPEFFECT_MOVE), out _);
+            dataObject = CreateDataObject(files);
+            if (files.Count == 0 && urls.Count > 0)
+            {
+                SetText(dataObject, UrlFormat, urls[0]);
+                SetText(dataObject, UnicodeTextFormat, string.Join(Environment.NewLine, urls));
+            }
+            CurrentDrag = keys;
+            // Copy or link only, never move (hard rule 1): whatever the target decides, the original stays where it is.
+            PInvoke.SHDoDragDrop((HWND)ownerHandle, dataObject, null, DROPEFFECT.DROPEFFECT_COPY | DROPEFFECT.DROPEFFECT_LINK, out _);
             return true;
         }
         catch (Exception failure) when (failure is not OutOfMemoryException)
@@ -72,50 +77,89 @@ public static class ShellDragDrop
     }
 
     /// <summary>Makes the fence window a drop target. Dispose (before the window is destroyed) to unregister.</summary>
-    /// <param name="portalFolder">For a Portal fence: the folder it shows right now (drops become real moves/copies into it, M4).</param>
-    public static IDisposable RegisterFence(nint fenceHandle, FenceDropHandlers handlers, Func<string?>? portalFolder = null)
+    public static IDisposable RegisterFence(nint fenceHandle, FenceDropHandlers handlers)
     {
-        var target = new FenceDropTarget((HWND)fenceHandle, handlers, portalFolder);
+        var target = new FenceDropTarget((HWND)fenceHandle, handlers);
         // WPF registers its own drop target on every window; NeoFences does not use WPF drag-drop, so replace it.
         PInvoke.RevokeDragDrop((HWND)fenceHandle);
         PInvoke.RegisterDragDrop((HWND)fenceHandle, target).ThrowOnFailure();
         return target;
     }
 
-    private static readonly ushort FileGroupDescriptorFormat = (ushort)PInvoke.RegisterClipboardFormat("FileGroupDescriptorW");
+    /// <summary>Windows' data object for these files (any folders, absolute ID lists), or an empty one. Missing files are left out.</summary>
+    private static unsafe ComDataObject CreateDataObject(IReadOnlyList<string> files)
+    {
+        var idLists = new List<nint>();
+        try
+        {
+            foreach (var file in files)
+            {
+                if (PInvoke.SHParseDisplayName(file, null, out var idList, 0, out _).Succeeded) idLists.Add((nint)idList);
+            }
+            var interfaceId = typeof(ComDataObject).GUID;
+            fixed (nint* ids = idLists.ToArray())
+            {
+                PInvoke.SHCreateDataObject(null, (uint)idLists.Count, (ITEMIDLIST**)ids, null, &interfaceId, out var created).ThrowOnFailure();
+                return (ComDataObject)created;
+            }
+        }
+        finally
+        {
+            foreach (var idList in idLists) Marshal.FreeCoTaskMem(idList);
+        }
+    }
 
-    /// <summary>True when the source describes virtual files (zip contents, phones, mail attachments).</summary>
-    internal static unsafe bool OffersVirtualFiles(IDataObject dataObject)
+    private static void SetText(ComDataObject dataObject, ushort format, string text)
     {
-        var format = DescriptorFormat();
+        var formatEtc = new ComFormat { cfFormat = unchecked((short)format), dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL };
+        var medium = new ComMedium { tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL, unionmember = Marshal.StringToHGlobalUni(text) };
+        dataObject.SetData(ref formatEtc, ref medium, true); // the data object owns the memory now
+    }
+
+    /// <summary>True when the source describes virtual files (zip contents, phones, mail attachments, a browser's link).</summary>
+    private static unsafe bool OffersVirtualFiles(IDataObject dataObject)
+    {
+        var format = Format(FileGroupDescriptorFormat);
         try { return dataObject.QueryGetData(&format).Value == 0; } // S_OK; S_FALSE and errors mean no
         catch (Exception failure) when (failure is not OutOfMemoryException) { return false; }
     }
 
-    /// <summary>The names of virtual files being dropped (no extraction); empty when there are none.</summary>
-    internal static unsafe List<string> VirtualFileNames(IDataObject dataObject)
+    /// <summary>
+    /// What an outside drag brings: its files (CF_HDROP), else one website (a browser's link). Virtual files (zip contents,
+    /// a phone) are never asked for CF_HDROP — that would extract every file — and have no target an item could point at.
+    /// </summary>
+    internal static IReadOnlyList<string> DroppedTargets(IDataObject dataObject)
+    {
+        if (!OffersVirtualFiles(dataObject) && DroppedFiles(dataObject) is { Count: > 0 } files) return files;
+        var url = ReadText(dataObject, UrlFormat) ?? ReadText(dataObject, UnicodeTextFormat);
+        return ItemKinds.Clean(url?.Split('\n')[0]) is { } website && ItemKinds.IsWebsite(website) ? [website] : [];
+    }
+
+    private static FORMATETC Format(ushort format) => new()
     {
-        var names = new List<string>();
-        var format = DescriptorFormat();
+        cfFormat = format,
+        dwAspect = (uint)DVASPECT.DVASPECT_CONTENT,
+        lindex = -1,
+        tymed = (uint)TYMED.TYMED_HGLOBAL,
+    };
+
+    /// <summary>A text format's content (bounded by its memory block: the source is another program), or null.</summary>
+    private static unsafe string? ReadText(IDataObject dataObject, ushort format)
+    {
+        var formatEtc = Format(format);
         STGMEDIUM medium = default;
         try
         {
-            dataObject.GetData(&format, out medium);
-            // The source is another program: only a memory block, and only as many descriptors as it really holds (a bad
-            // count would read past it, which .NET cannot catch; M8c review I5).
-            if (medium.tymed != TYMED.TYMED_HGLOBAL) return names;
+            dataObject.GetData(&formatEtc, out medium);
+            if (medium.tymed != TYMED.TYMED_HGLOBAL) return null;
             var size = (long)(nuint)PInvoke.GlobalSize(medium.u.hGlobal);
-            var group = (FILEGROUPDESCRIPTORW*)PInvoke.GlobalLock(medium.u.hGlobal);
-            if (group is null) return names;
+            var text = (char*)PInvoke.GlobalLock(medium.u.hGlobal);
+            if (text is null) return null;
             try
             {
-                if (size < sizeof(uint) || group->cItems > (size - sizeof(uint)) / sizeof(FILEDESCRIPTORW)) return names;
-                var descriptors = &group->fgd.e0;
-                for (var index = 0; index < group->cItems; index++)
-                {
-                    var name = descriptors[index].cFileName.ToString();
-                    if (!name.Contains('\\')) names.Add(name); // files in subfolders arrive inside their folder
-                }
+                var span = new ReadOnlySpan<char>(text, (int)Math.Min(size / sizeof(char), 8192));
+                var end = span.IndexOf('\0');
+                return new string(end >= 0 ? span[..end] : span).Trim();
             }
             finally
             {
@@ -124,25 +168,16 @@ public static class ShellDragDrop
         }
         catch (Exception failure) when (failure is not OutOfMemoryException)
         {
-            // No descriptors: nothing to place; Windows still handles the drop.
+            return null;
         }
         finally
         {
             if (medium.u.hGlobal.Value != null) PInvoke.ReleaseStgMedium(ref medium);
         }
-        return names;
     }
 
-    private static FORMATETC DescriptorFormat() => new()
-    {
-        cfFormat = FileGroupDescriptorFormat,
-        dwAspect = (uint)DVASPECT.DVASPECT_CONTENT,
-        lindex = -1,
-        tymed = (uint)TYMED.TYMED_HGLOBAL,
-    };
-
-    /// <summary>Paths in the drop's file list (CF_HDROP); empty for virtual items (zip contents, phones).</summary>
-    internal static unsafe List<string> DroppedFiles(IDataObject dataObject)
+    /// <summary>Paths in the drop's file list (CF_HDROP); empty when there is none.</summary>
+    private static unsafe List<string> DroppedFiles(IDataObject dataObject)
     {
         var files = new List<string>();
         var format = new FORMATETC
@@ -167,7 +202,7 @@ public static class ShellDragDrop
         }
         catch (Exception failure) when (failure is not OutOfMemoryException)
         {
-            // No file list: nothing to place; Windows still handles the drop.
+            // No file list: nothing to place.
         }
         finally
         {
@@ -177,35 +212,22 @@ public static class ShellDragDrop
     }
 
     /// <summary>
-    /// IDropTarget for one fence. Forwards to the shell (the hovered item's drop target, or the Desktop folder's) for
-    /// real file drops, and keeps Windows' drag image visible over the fence (IDropTargetHelper).
+    /// IDropTarget for one fence: items from fences move (Ctrl: duplicate); files and links from outside become items.
+    /// Keeps Windows' drag image visible over the fence (IDropTargetHelper).
     /// </summary>
-    private sealed unsafe class FenceDropTarget(HWND fence, FenceDropHandlers handlers, Func<string?>? portalFolder) : IDropTarget, IDisposable
+    private sealed unsafe class FenceDropTarget(HWND fence, FenceDropHandlers handlers) : IDropTarget, IDisposable
     {
-        private IDataObject? _dataObject;
-        private bool _desktopItemsOnly;      // desktop fence: items already on the Desktop (membership only)
-        private string? _portalFolder;       // Portal fence: the folder it shows (drops are real file operations)
-        private bool _sameFolderOnly;        // Portal fence: items already in that folder (nothing to do)
-        private string? _hoveredItem;        // item whose own drop target is active
-        private readonly Dictionary<string, bool> _containers = new(StringComparer.OrdinalIgnoreCase); // per drag: DragOver runs per mouse move
-        private bool _overRecycleBin;         // NeoFences recycles itself here (never the Recycle Bin's own drop)
-        private IReadOnlyList<string> _dragged = []; // what this drag carries (no disk access after DragEnter)
-        private bool _virtualSource;         // zip contents, phones: never asked for CF_HDROP (that extracts every file)
-        private IDropTarget? _shellTarget;   // forwarded-to target while it is entered
+        private IReadOnlyList<string> _keys = [];    // dragged out of a fence (this process)
+        private IReadOnlyList<string> _targets = []; // brought from outside (read once, at DragEnter)
         private IDropTargetHelper? _imageHelper;
 
         public void DragEnter(IDataObject pDataObj, MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect)
         {
-            LeaveShellTarget(); // a previous drag that failed half-way must not leak into this one
             Reset();
-            _dataObject = pDataObj;
-            // A virtual source (zip contents, a phone) would extract every file to answer CF_HDROP: not here, not on drop (M3b/M8c review).
-            _virtualSource = CurrentDrag is null && OffersVirtualFiles(pDataObj);
-            _dragged = CurrentDrag ?? (_virtualSource ? [] : DroppedFiles(pDataObj));
-            UseFolder(portalFolder?.Invoke());
+            _keys = CurrentDrag ?? [];
+            if (_keys.Count == 0) _targets = DroppedTargets(pDataObj);
             _imageHelper = TryCreateImageHelper();
-            var allowed = *pdwEffect;
-            Update(grfKeyState, pt, pdwEffect, allowed);
+            Update(grfKeyState, pt, pdwEffect);
             var effect = *pdwEffect;
             WithImageHelper(helper =>
             {
@@ -216,7 +238,7 @@ public static class ShellDragDrop
 
         public void DragOver(MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect)
         {
-            Update(grfKeyState, pt, pdwEffect, *pdwEffect);
+            Update(grfKeyState, pt, pdwEffect);
             var effect = *pdwEffect;
             WithImageHelper(helper =>
             {
@@ -227,70 +249,31 @@ public static class ShellDragDrop
 
         public void DragLeave()
         {
-            LeaveShellTarget();
             WithImageHelper(helper => helper.DragLeave());
-            handlers.ShowFeedback(null, false);
+            handlers.ShowFeedback(null);
             Reset();
         }
 
         public void Drop(IDataObject pDataObj, MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect)
         {
-            var allowed = *pdwEffect;
             try
             {
-                Update(grfKeyState, pt, pdwEffect, allowed);
-                if (handlers.AcceptsDrops?.Invoke() == false)
-                {
-                    WithImageHelper(helper => helper.DragLeave()); // no drag image left on screen (M13b)
-                    return; // effect already "none"
-                }
-                var shownEffect = *pdwEffect;
+                var insertAt = Update(grfKeyState, pt, pdwEffect);
+                var effect = *pdwEffect;
                 WithImageHelper(helper =>
                 {
                     var point = new System.Drawing.Point(pt.x, pt.y);
-                    helper.Drop(pDataObj, &point, shownEffect);
+                    helper.Drop(pDataObj, &point, effect);
                 });
-                var drop = handlers.HitTest(pt.x, pt.y);
-                if (_overRecycleBin)
-                {
-                    handlers.Recycle(CurrentDrag ?? (_virtualSource ? [] : DroppedFiles(pDataObj))); // always the Recycle Bin, never a delete
-                    *pdwEffect = DROPEFFECT.DROPEFFECT_NONE;
-                    return;
-                }
-                if (_shellTarget is null && _sameFolderOnly)
-                {
-                    *pdwEffect = DROPEFFECT.DROPEFFECT_NONE; // a Portal item dropped back into its own folder
-                    return;
-                }
-                if (_shellTarget is null)
+                if (effect == DROPEFFECT.DROPEFFECT_NONE || insertAt is not { } position) return;
+                if (_keys.Count > 0)
                 {
-                    // Membership only. Report "none" so the source never deletes anything after a "move".
-                    handlers.MoveItems(CurrentDrag ?? DroppedFiles(pDataObj), drop.InsertAt);
-                    *pdwEffect = DROPEFFECT.DROPEFFECT_NONE;
-                    return;
+                    handlers.ItemsDropped(_keys, position, grfKeyState.HasFlag(MODIFIERKEYS_FLAGS.MK_CONTROL));
+                    *pdwEffect = DROPEFFECT.DROPEFFECT_NONE; // our own drag: nothing for the source to do
                 }
-                if (_hoveredItem is null && _portalFolder is null)
+                else
                 {
-                    // Desktop items in a mixed drag join this fence; the rest arrive through Windows (M3b review).
-                    var files = CurrentDrag ?? (_virtualSource ? [] : DroppedFiles(pDataObj));
-                    var onDesktop = files.Where(DesktopNamespace.IsDesktopItem).ToList();
-                    if (onDesktop.Count > 0) handlers.MoveItems(onDesktop, drop.InsertAt);
-                    var arriving = files.Count > 0 ? files.Except(onDesktop).Select(Path.GetFileName).OfType<string>().ToList() : VirtualFileNames(pDataObj);
-                    // Files and their possible shortcuts are separate lists at the same place: each file keeps its own slot
-                    // (one interleaved list spread the files over every other position; M8c review I2).
-                    handlers.ExpectArrivals(DesktopRefsFor(arriving), drop.InsertAt + onDesktop.Count);
-                    handlers.ExpectArrivals(DesktopRefsFor(arriving.Select(ShortcutName)), drop.InsertAt + onDesktop.Count);
-                }
-                var target = _shellTarget;
-                _shellTarget = null; // Drop replaces DragLeave for it
-                try
-                {
-                    *pdwEffect = allowed; // the source's choices, so a right-button drop menu offers them all
-                    target.Drop(pDataObj, grfKeyState, pt, pdwEffect);
-                }
-                finally
-                {
-                    Marshal.ReleaseComObject(target);
+                    handlers.TargetsDropped(_targets, position); // the effect stays a link (or a copy): never a move
                 }
             }
             catch (Exception failure) when (failure is not OutOfMemoryException)
@@ -300,8 +283,7 @@ public static class ShellDragDrop
             }
             finally
             {
-                LeaveShellTarget();
-                handlers.ShowFeedback(null, false);
+                handlers.ShowFeedback(null);
                 Reset();
             }
         }
@@ -312,80 +294,34 @@ public static class ShellDragDrop
             Reset();
         }
 
-        /// <summary>Picks where the drag would land now and asks it for the effect (or computes ours).</summary>
-        private void Update(MODIFIERKEYS_FLAGS keys, POINTL pt, DROPEFFECT* effect, DROPEFFECT allowed)
+        /// <summary>Where the drag would land now (null: refused) and the effect shown: never a move.</summary>
+        private int? Update(MODIFIERKEYS_FLAGS keys, POINTL pt, DROPEFFECT* effect)
         {
-            if (handlers.AcceptsDrops?.Invoke() == false)
-            {
-                // Only for a refused fence (the library tab): a header under the pointer may switch the box to a tab that takes
-                // drops (M13b). Fences that take drops hit-test once, below (M13c).
-                try { handlers.HitTest(pt.x, pt.y); } catch (Exception failure) when (failure is not OutOfMemoryException) { handlers.LogFailure(failure); }
-            }
-            if (handlers.AcceptsDrops?.Invoke() == false)
-            {
-                // The Game Library shows NeoFences' own shortcuts: nothing is dropped into it (M12).
-                LeaveShellTarget();
-                handlers.ShowFeedback(null, false);
-                *effect = DROPEFFECT.DROPEFFECT_NONE;
-                return;
-            }
+            var allowed = *effect;
+            *effect = DROPEFFECT.DROPEFFECT_NONE;
             try
             {
-                var drop = handlers.HitTest(pt.x, pt.y);
-                if (handlers.AcceptsDrops?.Invoke() == false)
-                {
-                    // The hit-test just showed the library tab (its header was hovered): refused from this frame on (M16).
-                    LeaveShellTarget();
-                    handlers.ShowFeedback(null, false);
-                    *effect = DROPEFFECT.DROPEFFECT_NONE;
-                    return;
-                }
-                // Hovering a tab header shows that tab (M9): a Portal tab and a desktop tab take drops differently.
-                if (portalFolder?.Invoke() is var shown && !string.Equals(shown, _portalFolder, StringComparison.OrdinalIgnoreCase))
+                var insertAt = handlers.HitTest(pt.x, pt.y); // first: a hovered tab header switches the tab, which may refuse
+                if (!handlers.AcceptsDrops() || (_keys.Count == 0 && _targets.Count == 0))
                 {
-                    LeaveShellTarget();
-                    UseFolder(shown);
+                    handlers.ShowFeedback(null);
+                    return null;
                 }
-                var hovered = drop.ItemRef;
-                var dropsOnItem = hovered is not null && !(CurrentDrag?.Contains(hovered, StringComparer.OrdinalIgnoreCase) ?? false)
-                                  && IsDropContainer(hovered);
-                var wantedItem = dropsOnItem ? hovered : null;
-                var wantsShell = dropsOnItem || !(_desktopItemsOnly || _sameFolderOnly);
-
-                handlers.ShowFeedback(drop, wantedItem is not null);
-                // The Recycle Bin's own drop target deletes permanently with Shift held, or refuses: NeoFences recycles
-                // itself and only shows "move" here, whatever keys are held (M3b review C1).
-                _overRecycleBin = string.Equals(wantedItem, DesktopItems.RecycleBinRef, StringComparison.OrdinalIgnoreCase);
-                if (_overRecycleBin)
-                {
-                    LeaveShellTarget();
-                    *effect = allowed & DROPEFFECT.DROPEFFECT_MOVE;
-                    return;
-                }
-                if (_shellTarget is not null && (!wantsShell || wantedItem != _hoveredItem)) LeaveShellTarget();
-                if (!wantsShell)
-                {
-                    // Desktop fence: a membership move, nothing on disk changes. Portal: already in this folder.
-                    *effect = _sameFolderOnly ? DROPEFFECT.DROPEFFECT_NONE : allowed & DROPEFFECT.DROPEFFECT_MOVE;
-                    return;
-                }
-                if (_shellTarget is null)
-                {
-                    _shellTarget = (IDropTarget)(wantedItem is null
-                        ? (_portalFolder is null ? DesktopNamespace.DesktopDropTarget(fence) : DesktopNamespace.FolderDropTarget(fence, _portalFolder))
-                        : DesktopNamespace.GetUIObject(fence, [wantedItem], typeof(IDropTarget).GUID));
-                    _hoveredItem = wantedItem;
-                    *effect = allowed;
-                    _shellTarget.DragEnter(_dataObject!, keys, pt, effect);
-                    return;
-                }
-                *effect = allowed;
-                _shellTarget.DragOver(keys, pt, effect);
+                handlers.ShowFeedback(insertAt);
+                // Fence items: Ctrl duplicates ("+"), a plain drag moves them (shown as a link: the drag offers no move,
+                // so Explorer can never take the original). From outside: a link, or a copy when the source offers no link.
+                var duplicate = _keys.Count > 0 && keys.HasFlag(MODIFIERKEYS_FLAGS.MK_CONTROL);
+                *effect = duplicate && allowed.HasFlag(DROPEFFECT.DROPEFFECT_COPY) ? DROPEFFECT.DROPEFFECT_COPY
+                    : allowed.HasFlag(DROPEFFECT.DROPEFFECT_LINK) ? DROPEFFECT.DROPEFFECT_LINK
+                    : allowed.HasFlag(DROPEFFECT.DROPEFFECT_COPY) ? DROPEFFECT.DROPEFFECT_COPY
+                    : DROPEFFECT.DROPEFFECT_NONE;
+                return *effect == DROPEFFECT.DROPEFFECT_NONE ? null : insertAt;
             }
             catch (Exception failure) when (failure is not OutOfMemoryException)
             {
                 handlers.LogFailure(failure);
-                *effect = DROPEFFECT.DROPEFFECT_NONE;
+                handlers.ShowFeedback(null);
+                return null;
             }
         }
 
@@ -397,41 +333,10 @@ public static class ShellDragDrop
             catch (Exception failure) when (failure is not OutOfMemoryException) { handlers.LogFailure(failure); }
         }
 
-        private void LeaveShellTarget()
-        {
-            if (_shellTarget is null) return;
-            try { _shellTarget.DragLeave(); }
-            catch (Exception failure) when (failure is not OutOfMemoryException) { handlers.LogFailure(failure); }
-            Marshal.ReleaseComObject(_shellTarget);
-            _shellTarget = null;
-            _hoveredItem = null;
-        }
-
-        /// <summary>What the fence shown now is: a Portal of this folder, or a desktop fence (null).</summary>
-        private void UseFolder(string? folder)
-        {
-            _portalFolder = folder;
-            _desktopItemsOnly = _portalFolder is null && _dragged.Count > 0 && _dragged.All(DesktopNamespace.IsDesktopItem);
-            _sameFolderOnly = _portalFolder is not null && _dragged.Count > 0
-                && _dragged.All(itemRef => string.Equals(Path.GetDirectoryName(itemRef), _portalFolder, StringComparison.OrdinalIgnoreCase));
-        }
-
-        private bool IsDropContainer(string itemRef)
-        {
-            if (!_containers.TryGetValue(itemRef, out var container)) _containers[itemRef] = container = DesktopNamespace.IsDropContainer(fence, itemRef);
-            return container;
-        }
-
         private void Reset()
         {
-            _containers.Clear();
-            _dataObject = null;
-            _desktopItemsOnly = false;
-            _portalFolder = null;
-            _sameFolderOnly = false;
-            _overRecycleBin = false;
-            _virtualSource = false;
-            _dragged = [];
+            _keys = [];
+            _targets = [];
             if (_imageHelper is not null) Marshal.ReleaseComObject(_imageHelper);
             _imageHelper = null;
         }
@@ -447,13 +352,5 @@ public static class ShellDragDrop
                 return null; // no drag image over fences; the drop still works
             }
         }
-
-        /// <summary>Where Windows will put dropped files: the user's Desktop, same names. Renamed copies ("name (2)") go to the Inbox.</summary>
-        private static List<string> DesktopRefsFor(IEnumerable<string> fileNames) =>
-            fileNames.Select(name => Path.Combine(DesktopItems.UserDesktop, name)).ToList();
-
-        /// <summary>The name Windows gives a shortcut it makes on a drop (Alt, or a link-only source).</summary>
-        // ponytail: the English " - Shortcut" suffix; other display languages name links differently (their links go to the Inbox).
-        private static string ShortcutName(string fileName) => fileName + " - Shortcut.lnk";
     }
 }
diff --git a/src/NeoFences.Shell/ShellFileOps.cs b/src/NeoFences.Shell/ShellFileOps.cs
index f614e0c..c5f7b3d 100644
--- a/src/NeoFences.Shell/ShellFileOps.cs
+++ b/src/NeoFences.Shell/ShellFileOps.cs
@@ -1,5 +1,4 @@
 using System.Runtime.InteropServices;
-using NeoFences.Core.Model;
 using Windows.Win32;
 using Windows.Win32.Foundation;
 using Windows.Win32.UI.Shell;
@@ -7,8 +6,8 @@ using Windows.Win32.UI.Shell;
 namespace NeoFences.Shell;
 
 /// <summary>
-/// File operations through Windows' own engine (IFileOperation): its dialogs, progress, conflicts and Undo (Ctrl+Z in
-/// Explorer). Deleting always goes to the Recycle Bin (hard rule 1); if Windows cannot recycle an item it asks the user.
+/// Deleting NeoFences' own files (snapshots) through Windows' own engine (IFileOperation): its dialogs and Undo (Ctrl+Z
+/// in Explorer), always to the Recycle Bin. Never used on items or their targets (hard rule 1, ADR-040).
 /// </summary>
 public static class ShellFileOps
 {
@@ -62,13 +61,6 @@ public static class ShellFileOps
         }
     }
 
-    /// <param name="newName">As typed. If Explorer hides this item's extension (shortcuts, or the user's setting), Windows keeps it.</param>
-    /// <returns>False also for a name that is not a plain name: "sub\x" or "..\x" would move the file off the Desktop (M3a review I2).</returns>
-    public static bool TryRename(nint ownerHandle, string itemRef, string newName) =>
-        !itemRef.StartsWith("::", StringComparison.Ordinal)
-        && FileNames.IsValidNewName(newName)
-        && Run(ownerHandle, operation => operation.RenameItem(Create(itemRef), newName, null));
-
     private static bool Run(nint ownerHandle, Action<IFileOperation> queue)
     {
         IFileOperation? operation = null;
diff --git a/src/NeoFences.Shell/ShellItemMenu.cs b/src/NeoFences.Shell/ShellItemMenu.cs
index 97e87e8..869972e 100644
--- a/src/NeoFences.Shell/ShellItemMenu.cs
+++ b/src/NeoFences.Shell/ShellItemMenu.cs
@@ -6,14 +6,14 @@ using Windows.Win32.UI.WindowsAndMessaging;
 
 namespace NeoFences.Shell;
 
-/// <summary>What the user picked in Windows' item menu that NeoFences runs itself instead of the shell.</summary>
-/// <summary>Custom: one of the caller's own commands (the Game Library's "Hide from library", M12).</summary>
-public enum ItemMenuChoice { None, Rename, Delete, Custom }
+/// <summary>What the user picked in Windows' item menu that the caller runs itself: Delete (when handed back), or one of
+/// the caller's own commands (the Game Library's "Hide from library", M12).</summary>
+public enum ItemMenuChoice { None, Delete, Custom }
 
 /// <summary>
-/// Windows' own right-click menu for desktop items (the classic menu: Open, Open with, Send to, Properties, shell
-/// extensions; the Windows 11 compact menu is Explorer-private, spec §6). Rename and Delete are handed back so
-/// NeoFences renames in place and always recycles (hard rule 1, user decision 2026-10-02).
+/// Windows' own right-click menu for a target (the classic menu: Open, Open with, Send to, Properties, shell extensions;
+/// the Windows 11 compact menu is Explorer-private, spec §6). For virtual items it is only behind Shift+right-click,
+/// under a first line saying it acts on the real file (ADR-040): there Windows itself deletes or renames, never NeoFences.
 /// </summary>
 public static class ShellItemMenu
 {
@@ -56,15 +56,13 @@ public static class ShellItemMenu
     /// Shows the menu for these items at a screen point and runs the chosen shell command.
     /// </summary>
     /// <param name="extended">Shift held: the extended menu ("Copy as path", "Open PowerShell here", …).</param>
-    /// <returns>Rename/Delete for the caller to run; None when the shell ran the command, the user cancelled, or the menu could not be built.</returns>
+    /// <returns>Delete or Custom for the caller to run; None when the shell ran the command, the user cancelled, or the menu could not be built.</returns>
     /// <param name="logFailure">Told when the menu could not be built or a command failed (a broken shell extension).</param>
-    public static ItemMenuChoice Show(nint ownerHandle, IReadOnlyList<string> itemRefs, int screenX, int screenY, bool extended, Action<Exception> logFailure) =>
-        Show(ownerHandle, itemRefs, screenX, screenY, extended, logFailure, customCommands: [], canRename: true, out _);
-
+    /// <param name="header">A disabled first line ("Windows menu — acts on the real file"), or null.</param>
     /// <param name="customCommands">Added at the end, after a separator; the chosen one comes back as <paramref name="customCommand"/>.</param>
-    /// <param name="canRename">False: the shell's Rename is not offered (a Game Library shortcut, M12).</param>
+    /// <param name="handDeleteBack">True: Delete comes back to the caller instead of running (the library hides the game, M12).</param>
     public static unsafe ItemMenuChoice Show(nint ownerHandle, IReadOnlyList<string> itemRefs, int screenX, int screenY, bool extended, Action<Exception> logFailure,
-        IReadOnlyList<string> customCommands, bool canRename, out int customCommand)
+        string? header, IReadOnlyList<string> customCommands, bool handDeleteBack, out int customCommand)
     {
         customCommand = -1;
         var owner = (HWND)ownerHandle;
@@ -76,13 +74,18 @@ public static class ShellItemMenu
             contextMenu = (IContextMenu)DesktopNamespace.GetUIObject(owner, itemRefs, typeof(IContextMenu).GUID);
 
             menu = PInvoke.CreatePopupMenu();
-            var flags = PInvoke.CMF_NORMAL | (canRename ? PInvoke.CMF_CANRENAME : 0) | (extended ? PInvoke.CMF_EXTENDEDVERBS : 0);
+            var flags = PInvoke.CMF_NORMAL | (extended ? PInvoke.CMF_EXTENDEDVERBS : 0); // no CMF_CANRENAME: Windows' Rename needs an Explorer view
             var queried = contextMenu.QueryContextMenu(menu, 0, FirstCommandId, LastCommandId, flags);
             if (queried.Failed) // a broken extension can fail the whole menu without throwing (M3a review)
             {
                 logFailure(new COMException("QueryContextMenu failed", queried.Value));
                 return ItemMenuChoice.None;
             }
+            if (header is not null)
+            {
+                fixed (char* text = header) PInvoke.InsertMenu(menu, 0, MENU_ITEM_FLAGS.MF_BYPOSITION | MENU_ITEM_FLAGS.MF_STRING | MENU_ITEM_FLAGS.MF_GRAYED, 0, text);
+                PInvoke.InsertMenu(menu, 1, MENU_ITEM_FLAGS.MF_BYPOSITION | MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (PCWSTR)null);
+            }
             if (customCommands.Count > 0) PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (PCWSTR)null);
             for (var index = 0; index < customCommands.Count; index++)
             {
@@ -104,8 +107,7 @@ public static class ShellItemMenu
             }
 
             var verb = Verb(contextMenu, command - FirstCommandId);
-            if (string.Equals(verb, "rename", StringComparison.OrdinalIgnoreCase)) return ItemMenuChoice.Rename;
-            if (string.Equals(verb, "delete", StringComparison.OrdinalIgnoreCase)) return ItemMenuChoice.Delete;
+            if (handDeleteBack && string.Equals(verb, "delete", StringComparison.OrdinalIgnoreCase)) return ItemMenuChoice.Delete;
             Invoke(contextMenu, command - FirstCommandId, owner, screenX, screenY);
             return ItemMenuChoice.None;
         }
diff --git a/src/NeoFences.Shell/ShellItems.cs b/src/NeoFences.Shell/ShellItems.cs
index 2fd4509..741cc29 100644
--- a/src/NeoFences.Shell/ShellItems.cs
+++ b/src/NeoFences.Shell/ShellItems.cs
@@ -4,13 +4,14 @@ using Windows.Win32;
 using Windows.Win32.Foundation;
 using Windows.Win32.Graphics.Gdi;
 using Windows.Win32.UI.Shell;
+using Windows.Win32.UI.WindowsAndMessaging;
 
 namespace NeoFences.Shell;
 
 /// <summary>32-bit premultiplied BGRA pixels, top-down rows (ready for WPF's Pbgra32 BitmapSource).</summary>
 public sealed record ShellImage(int Width, int Height, byte[] Pixels);
 
-/// <summary>Display names, icons/thumbnails and opening of desktop items, by item ref (parsing name).</summary>
+/// <summary>Display names, icons/thumbnails and opening of item targets (parsing names: paths, <c>::{GUID}</c>, URLs).</summary>
 public static class ShellItems
 {
     /// <summary>The name Explorer shows ("Crysis 2", not "Crysis 2.lnk"; "Recycle Bin" in the user's language).</summary>
@@ -76,17 +77,25 @@ public static class ShellItems
     }
 
     /// <summary>
-    /// Opens with the default verb, like a double-click in Explorer. If it cannot, Windows shows its own message
-    /// (broken shortcut, no app for this file type) owned by <paramref name="ownerHandle"/> (M2b finding H5).
+    /// Opens like a double-click in Explorer (websites in the default browser), with the item's own arguments, "run as
+    /// administrator", and the target's folder as the working folder, as a shortcut would. If it cannot, Windows shows its
+    /// own message (no app for this file type) owned by <paramref name="ownerHandle"/> (M2b finding H5).
     /// </summary>
-    /// <returns>False when nothing could open it (no associated app, the user cancelled a UAC prompt, the item is gone).</returns>
-    public static bool TryOpen(string itemRef, nint ownerHandle)
+    /// <returns>False when nothing could open it (no associated app, the user declined the UAC prompt, the item is gone).</returns>
+    public static bool TryOpen(string target, nint ownerHandle, string? arguments = null, bool runAsAdmin = false)
     {
         try
         {
-            var startInfo = itemRef.StartsWith("::", StringComparison.Ordinal)
-                ? new ProcessStartInfo("explorer.exe", "shell:" + itemRef) { UseShellExecute = true }
-                : new ProcessStartInfo(itemRef) { UseShellExecute = true };
+            ProcessStartInfo startInfo;
+            if (target.StartsWith("::", StringComparison.Ordinal)) startInfo = new ProcessStartInfo("explorer.exe", "shell:" + target);
+            else if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) startInfo = new ProcessStartInfo("explorer.exe", target);
+            else
+            {
+                startInfo = new ProcessStartInfo(target) { Arguments = arguments ?? "" };
+                if (runAsAdmin) startInfo.Verb = "runas";
+                if (Path.IsPathFullyQualified(target) && Path.GetDirectoryName(target) is { Length: > 0 } folder) startInfo.WorkingDirectory = folder;
+            }
+            startInfo.UseShellExecute = true;
             startInfo.ErrorDialog = true;
             startInfo.ErrorDialogParentHandle = ownerHandle;
             // The user just clicked our (never-activated) fence: let the opened window come to the front.
@@ -100,13 +109,83 @@ public static class ShellItems
         }
     }
 
+    /// <summary>Explorer with the target selected in its folder (item menu → Open file location). Read-only: nothing changes.</summary>
+    public static bool TryShowInFolder(string target)
+    {
+        try
+        {
+            PInvoke.AllowSetForegroundWindow(unchecked((uint)-1)); // ASFW_ANY
+            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{target}\"") { UseShellExecute = true })?.Dispose();
+            return true;
+        }
+        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or InvalidOperationException)
+        {
+            return false;
+        }
+    }
+
+    private static string? _defaultBrowser;
+    private static bool _defaultBrowserRead;
+
+    /// <summary>The program that opens http links for this user (a website item shows its icon), or null. Read once per run.</summary>
+    public static string? DefaultBrowserPath()
+    {
+        if (_defaultBrowserRead) return _defaultBrowser;
+        _defaultBrowserRead = true;
+        const uint AssocfIsProtocol = 0x1000; // ASSOCF_IS_PROTOCOL: honour the user's choice (UserChoice), not HKCR\http
+        var buffer = new char[1024];
+        var length = (uint)buffer.Length;
+        try
+        {
+            if (PInvoke.AssocQueryString((ASSOCF)AssocfIsProtocol, ASSOCSTR.ASSOCSTR_EXECUTABLE, "http", "open", buffer, ref length).Succeeded)
+            {
+                var end = Array.IndexOf(buffer, '\0');
+                _defaultBrowser = new string(buffer, 0, end >= 0 ? end : buffer.Length);
+            }
+        }
+        catch (Exception failure) when (failure is not OutOfMemoryException)
+        {
+            _defaultBrowser = null; // websites then show no icon
+        }
+        return _defaultBrowser;
+    }
+
+    /// <summary>
+    /// The icon at <paramref name="index"/> in an .ico, .exe or .dll (an item's own icon, Properties → From a file…), at
+    /// <paramref name="sizePx"/>. Null when the file or the icon is not there.
+    /// </summary>
+    public static ShellImage? TryGetFileIcon(string file, int index, int sizePx)
+    {
+        DestroyIconSafeHandle? large = null, small = null;
+        ICONINFO info = default;
+        try
+        {
+            if (PInvoke.SHDefExtractIcon(file, index, 0, out large, out small, (uint)sizePx).Failed || large.IsInvalid) return null;
+            if (!PInvoke.GetIconInfo(large, out info)) return null;
+            // ponytail: icons without an alpha channel (old 24-bit ones) come out with an opaque background; apply the mask if users pick those.
+            return ReadPixels(info.hbmColor, premultiply: true);
+        }
+        catch (Exception failure) when (failure is not OutOfMemoryException)
+        {
+            return null;
+        }
+        finally
+        {
+            if (!info.hbmColor.IsNull) PInvoke.DeleteObject(info.hbmColor);
+            if (!info.hbmMask.IsNull) PInvoke.DeleteObject(info.hbmMask);
+            large?.Dispose();
+            small?.Dispose();
+        }
+    }
+
     private static IShellItem Create(string itemRef)
     {
         PInvoke.SHCreateItemFromParsingName(itemRef, null, out IShellItem item).ThrowOnFailure();
         return item;
     }
 
-    private static unsafe ShellImage? ReadPixels(HBITMAP bitmap)
+    /// <param name="premultiply">Icon bitmaps carry straight alpha; WPF's Pbgra32 wants it premultiplied.</param>
+    private static unsafe ShellImage? ReadPixels(HBITMAP bitmap, bool premultiply = false)
     {
         BITMAP header;
         // Any bit depth: GetDIBits converts to 32-bit; a 24-bit (or palette) image comes back with alpha 0 and is made
@@ -144,6 +223,14 @@ public static class ShellItems
         var hasAlpha = false;
         for (var alphaIndex = 3; alphaIndex < pixels.Length && !hasAlpha; alphaIndex += 4) hasAlpha = pixels[alphaIndex] != 0;
         if (!hasAlpha) for (var alphaIndex = 3; alphaIndex < pixels.Length; alphaIndex += 4) pixels[alphaIndex] = 255;
+        else if (premultiply)
+        {
+            for (var pixel = 0; pixel < pixels.Length; pixel += 4)
+            {
+                var alpha = pixels[pixel + 3];
+                for (var channel = 0; channel < 3; channel++) pixels[pixel + channel] = (byte)(pixels[pixel + channel] * alpha / 255);
+            }
+        }
         return new ShellImage(width, height, pixels);
     }
 }
diff --git a/src/NeoFences.Shell/TargetProbe.cs b/src/NeoFences.Shell/TargetProbe.cs
new file mode 100644
index 0000000..a04be2a
--- /dev/null
+++ b/src/NeoFences.Shell/TargetProbe.cs
@@ -0,0 +1,31 @@
+using NeoFences.Core.Items;
+
+namespace NeoFences.Shell;
+
+/// <summary>Where a target is right now (M18 spec §4): the disk asked off the UI thread, a network share with a timeout.</summary>
+public static class TargetProbe
+{
+    public static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(2);
+
+    /// <summary>Call off the UI thread. A share that does not answer within <see cref="NetworkTimeout"/> is Unavailable.</summary>
+    public static TargetCheck Check(string target)
+    {
+        if (ItemKinds.Of(target) != ItemKind.Path) return TargetCheck.Ok;
+        if (!TargetChecks.IsNetworkPath(target)) return Classify(target);
+        // ponytail: a share that never answers keeps one pool thread blocked until Windows gives up; a probe thread per share if that piles up.
+        var probe = Task.Run(() => Classify(target));
+        return probe.Wait(NetworkTimeout) ? probe.Result : new TargetCheck(TargetState.Unavailable);
+    }
+
+    private static TargetCheck Classify(string target)
+    {
+        try
+        {
+            return TargetChecks.Classify(target, File.Exists, Directory.Exists);
+        }
+        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException)
+        {
+            return new TargetCheck(TargetState.Unavailable); // never a crash for one odd path (hard rule 7)
+        }
+    }
+}
diff --git a/src/NeoFences.Shell/Watchdog.cs b/src/NeoFences.Shell/Watchdog.cs
index f119a7a..4d111f5 100644
--- a/src/NeoFences.Shell/Watchdog.cs
+++ b/src/NeoFences.Shell/Watchdog.cs
@@ -20,12 +20,12 @@ public sealed class Watchdog(string dataDirectory, Action<string> log)
     private static readonly TimeSpan SessionPollInterval = TimeSpan.FromMilliseconds(500);
     private static readonly TimeSpan SessionEndGracePeriod = TimeSpan.FromSeconds(30);
 
-    private string TakeoverActivePath => Path.Combine(dataDirectory, "takeover-active");
+    private string IconsHiddenPath => Path.Combine(dataDirectory, "icons-hidden");
     private string RestartLogPath => Path.Combine(dataDirectory, "watchdog-restarts.txt");
     private string CleanMarkerPath(int processId) => Path.Combine(dataDirectory, $"clean-shutdown-{processId}");
     private string SessionEndingMarkerPath(int processId) => Path.Combine(dataDirectory, $"session-ending-{processId}");
 
-    public bool IsTakeoverActiveMarked => File.Exists(TakeoverActivePath);
+    public bool IsIconsHiddenMarked => File.Exists(IconsHiddenPath);
 
     /// <summary>Main process, at startup: drops stale markers for this PID, then launches the watchdog detached.</summary>
     public void LaunchDetached(int mainProcessId)
@@ -40,11 +40,11 @@ public sealed class Watchdog(string dataDirectory, Action<string> log)
     public static void RunLauncher(int mainProcessId) => StartSelf($"{RunArgument} {mainProcessId}");
 
     /// <summary>Main process: the icons may be hidden by NeoFences right now (the watchdog restores only in that case).</summary>
-    public void SetTakeoverActive(bool active)
+    public void SetIconsHiddenMarker(bool hidden)
     {
         Directory.CreateDirectory(dataDirectory);
-        if (active) File.WriteAllText(TakeoverActivePath, Timestamp());
-        else File.Delete(TakeoverActivePath);
+        if (hidden) File.WriteAllText(IconsHiddenPath, Timestamp());
+        else File.Delete(IconsHiddenPath);
     }
 
     /// <summary>Main process, on an orderly exit the user asked for.</summary>
@@ -70,7 +70,7 @@ public sealed class Watchdog(string dataDirectory, Action<string> log)
         var plan = WatchdogPlan.For(
             cleanShutdown: ConsumeMarker(CleanMarkerPath(mainProcessId)),
             sessionEnding: ConsumeMarker(SessionEndingMarkerPath(mainProcessId)),
-            takeoverActive: IsTakeoverActiveMarked);
+            iconsHidden: IsIconsHiddenMarked);
         log($"main exited: {plan}");
 
         if (plan.RestoreIcons) RestoreIconsWithRetry();
@@ -116,7 +116,7 @@ public sealed class Watchdog(string dataDirectory, Action<string> log)
     {
         // Explorer may be down at the same moment (e.g. it crashed together with us): keep trying for ~5 s, then fall
         // back to Explorer's persisted setting (DesktopIcons.ShowWithRetry).
-        if (DesktopIcons.ShowWithRetry(giveUpAfter: RestoreRetryDelay * RestoreAttempts, log: log)) TryDelete(TakeoverActivePath);
+        if (DesktopIcons.ShowWithRetry(giveUpAfter: RestoreRetryDelay * RestoreAttempts, log: log)) TryDelete(IconsHiddenPath);
     }
 
     private void RestartMain(string reason)
````

- [ ] **Step 2: Build the Shell.** (Its behaviour is Windows' COM and OLE: verified by the live checks in Task 7.)
  Run: `dotnet build src/NeoFences.Shell`
  Expected: `0 Warning(s)`, `0 Error(s)`.
- [ ] **Step 3: Check hard rule 1 in the drop code.**
  Run: `grep -n "DROPEFFECT_MOVE" src/NeoFences.Shell/ShellDragDrop.cs`
  Expected: no output.
- [ ] **Step 4: Commit.**
  `git add -A src/NeoFences.Shell && git commit -m "feat: made fence drops create items and drags copy only in the Shell"`

### Task 5: App — virtual items in fences

**Files:**
- Create: `src/NeoFences.App/FenceHost.Items.cs`, `FenceHost.Watching.cs`, `ItemPropertiesWindow.xaml(.cs)`,
  `MissingItemWindow.xaml(.cs)`, `LibraryLister.cs` (the trimmed `PortalState`)
- Modify: `FenceHost.cs` (rewritten), `FenceHost.Library.cs`, `FenceHost.Appearance.cs`, `FenceHost.Updates.cs`,
  `FenceWindow.xaml(.cs)`, `FenceItemView.cs` (+ `ShownItem`), `IconLoader.cs`, `SettingsWindow.xaml(.cs)`,
  `SettingsWindow.Library.cs`, `InstallHooks.cs`, `AppPaths.cs` (`IconsDirectory`), `ShellWorker.cs`
- Delete: `src/NeoFences.App/{PortalState,SettingsWindow.Rules}.cs`

**Interfaces:**
- Consumes: Tasks 1–4.
- Produces (UI): fence menu Add item… / Refresh / New fence / New Game Library fence / …; item menu and Shift+right-click
  Windows menu; keys Enter / Del / F2 / Alt+Enter; Properties and Add item… dialog; Missing question; empty-fence hint;
  Missing badge and Unavailable dimming; Settings "Hide desktop icons while NeoFences runs".

- [ ] **Step 1: Remove the parked App code and apply the patch.**

```bash
git rm -q src/NeoFences.App/PortalState.cs src/NeoFences.App/SettingsWindow.Rules.cs
```

````diff
diff --git a/src/NeoFences.App/AppPaths.cs b/src/NeoFences.App/AppPaths.cs
index ee0a3ba..5e9e5b5 100644
--- a/src/NeoFences.App/AppPaths.cs
+++ b/src/NeoFences.App/AppPaths.cs
@@ -2,7 +2,7 @@ using System.IO;
 
 namespace NeoFences.App;
 
-/// <summary>Runtime data lives in %LOCALAPPDATA%\NeoFences (config.json, backups\, logs\, watchdog state).</summary>
+/// <summary>Runtime data lives in %LOCALAPPDATA%\NeoFences (config.json, items.json, backups\, logs\, watchdog state).</summary>
 public static class AppPaths
 {
     public static string DataDirectory { get; } =
@@ -12,4 +12,7 @@ public static class AppPaths
 
     /// <summary>The Game Library's shortcuts and index (M12): NeoFences' own files.</summary>
     public static string LibraryDirectory { get; } = Path.Combine(DataDirectory, "library");
+
+    /// <summary>Pictures chosen as item icons (M18), copied in so they survive the original being moved: NeoFences' own files.</summary>
+    public static string IconsDirectory { get; } = Path.Combine(DataDirectory, "icons");
 }
diff --git a/src/NeoFences.App/FenceHost.Appearance.cs b/src/NeoFences.App/FenceHost.Appearance.cs
index 5b3c78c..c3f25b0 100644
--- a/src/NeoFences.App/FenceHost.Appearance.cs
+++ b/src/NeoFences.App/FenceHost.Appearance.cs
@@ -42,7 +42,7 @@ public sealed partial class FenceHost
     {
         if (!Appearance.WallpaperAccent) return null;
         var fallback = (_accents.Count > 0 ? _accents[0].Accent : null) ?? _windowsAccent;
-        if (window.Handle == 0) return fallback; // a fence being created (drawn, a new Portal): restyled on its first move (final review M1)
+        if (window.Handle == 0) return fallback; // a fence being created (drawn): restyled on its first move (final review M1)
         var rect = FenceWindowChrome.GetPixelRect(window.Handle);
         var (centerX, centerY) = (rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
         var under = _accents.FirstOrDefault(entry => centerX >= entry.Monitor.Left && centerX < entry.Monitor.Right
diff --git a/src/NeoFences.App/FenceHost.Items.cs b/src/NeoFences.App/FenceHost.Items.cs
new file mode 100644
index 0000000..eee2ddf
--- /dev/null
+++ b/src/NeoFences.App/FenceHost.Items.cs
@@ -0,0 +1,374 @@
+using System.IO;
+using System.Windows;
+using System.Windows.Controls;
+using System.Windows.Input;
+using System.Windows.Media;
+using System.Windows.Media.Imaging;
+using System.Windows.Threading;
+using NeoFences.Core.Items;
+using NeoFences.Core.Model;
+using NeoFences.Shell;
+using Serilog;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// Virtual items (M18, spec 2026-10-04-virtual-items-design §2–§3, ADR-040): opening, the item menu, Properties, Add
+/// item…, drops and drags, sorting and Locate…. Every change is to NeoFences' items only; no target is ever touched.
+/// </summary>
+public sealed partial class FenceHost
+{
+    private const int MaxPicturePx = 256;
+
+    // ---------- opening ----------
+
+    /// <summary>Double-click / Enter. Peek ends once something is opened from it (like Fences).</summary>
+    private void OpenKey(FenceWindow window, string key)
+    {
+        if (window.IsLibrary) OpenItem(key, ownerHandle: window.Handle); // the library's key is its shortcut's path
+        else if (_items.Find(key) is { } item) OpenVirtualItem(window, item, runAsAdmin: item.RunAsAdmin);
+        SetPeek(false);
+    }
+
+    /// <summary>
+    /// Opens with the item's arguments, as administrator when asked (spec §3). The target is checked first, on the open's
+    /// own thread: a missing one gets NeoFences' Locate… / Remove question instead of a Windows error.
+    /// </summary>
+    private void OpenVirtualItem(FenceWindow window, VirtualItem item, bool runAsAdmin)
+    {
+        var owner = window.Handle;
+        var dispatcher = Dispatcher.CurrentDispatcher;
+        ShellWorker.RunAlone(() =>
+        {
+            var check = TargetProbe.Check(item.Target);
+            if (check.State != TargetState.Ok)
+            {
+                dispatcher.BeginInvoke(() =>
+                {
+                    ApplyChecks([(item.Target, check)]);
+                    AskAboutMissing(window, item.Id, check.State);
+                });
+                return;
+            }
+            var takesArguments = ItemKinds.TakesArguments(item.Kind, check.IsFolder);
+            if (!ShellItems.TryOpen(item.Target, owner, takesArguments ? item.Arguments : null, runAsAdmin && takesArguments))
+                Log.Warning("could not open {Target} (no app for it, or the admin prompt was declined)", item.Target);
+        }, name: "NeoFences open");
+    }
+
+    /// <summary>A missing or unavailable target was opened: Locate… / Remove from fence / Cancel (spec §3).</summary>
+    private void AskAboutMissing(FenceWindow window, string itemId, TargetState state)
+    {
+        if (_items.Find(itemId) is not { } item) return;
+        var question = new MissingItemWindow(DisplayName(item), item.Target, state) { Owner = window };
+        if (question.ShowDialog() != true) return;
+        if (question.Choice == MissingItemChoice.Locate) Locate(window, itemId);
+        else if (question.Choice == MissingItemChoice.Remove) RemoveItems(window, [itemId]);
+    }
+
+    private static string DisplayName(VirtualItem item) => item.OwnName ?? item.Kind switch
+    {
+        ItemKind.Website => ItemKinds.WebsiteName(item.Target),
+        _ => Path.GetFileNameWithoutExtension(item.Target.TrimEnd('\\')) is { Length: > 0 } name ? name : item.Target,
+    };
+
+    // ---------- the item menu ----------
+
+    /// <summary>
+    /// Right-click on items (spec §3): NeoFences' own safe menu; Shift+right-click: Windows' menu for the real target,
+    /// under a line saying so. The Game Library keeps Windows' menu with its own commands (M12).
+    /// </summary>
+    private void ShowItemMenu(FenceWindow window, IReadOnlyList<string> keys, int screenX, int screenY, bool fromKeyboard)
+    {
+        var shift = !fromKeyboard && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
+        if (window.IsLibrary)
+        {
+            ShowLibraryItemMenu(window, keys, screenX, screenY, extended: shift);
+            return;
+        }
+        var items = keys.Select(_items.Find).OfType<VirtualItem>().ToList();
+        if (items.Count == 0) return;
+        if (shift)
+        {
+            ShowWindowsMenu(window, items[0], screenX, screenY);
+            return;
+        }
+        var menu = new ContextMenu();
+        void Command(string header, Action run)
+        {
+            var command = new MenuItem { Header = header };
+            command.Click += (_, _) => run();
+            menu.Items.Add(command);
+        }
+        if (items.Count > 1)
+        {
+            Command("Open", () => { foreach (var item in items) OpenVirtualItem(window, item, runAsAdmin: item.RunAsAdmin); });
+            Command($"Remove {items.Count} items from fence", () => RemoveItems(window, [.. items.Select(item => item.Id)]));
+        }
+        else
+        {
+            var item = items[0];
+            var check = CheckOf(item.Target);
+            var onDisk = item.Kind == ItemKind.Path && TargetChecks.RootOf(item.Target) is not null;
+            if (check.State != TargetState.Ok)
+            {
+                Command("Locate…", () => Locate(window, item.Id));
+                Command("Remove from fence", () => RemoveItems(window, [item.Id]));
+                menu.Items.Add(new Separator());
+            }
+            Command("Open", () => OpenVirtualItem(window, item, runAsAdmin: item.RunAsAdmin));
+            if (onDisk && !check.IsFolder) Command("Run as administrator", () => OpenVirtualItem(window, item, runAsAdmin: true));
+            if (onDisk) Command("Open file location", () => ShowInFolder(item.Target));
+            Command("Copy path", () => CopyText(item.Target));
+            menu.Items.Add(new Separator());
+            Command("Properties…", () => ShowProperties(window, item.Id, focusName: false));
+            if (check.State == TargetState.Ok) Command("Remove from fence", () => RemoveItems(window, [item.Id]));
+            menu.Items.Add(new Separator());
+            menu.Items.Add(new MenuItem { Header = "Shift+right-click: Windows' menu", IsEnabled = false });
+        }
+        window.ShowItemMenu(menu, fromKeyboard);
+    }
+
+    /// <summary>Windows' full menu for the real target (spec §3): only here can a real delete or rename happen, and it says so.</summary>
+    private static void ShowWindowsMenu(FenceWindow window, VirtualItem item, int screenX, int screenY)
+    {
+        if (item.Kind == ItemKind.Website) return; // a website has no Windows menu
+        ShellItemMenu.Show(window.Handle, [item.Target], screenX, screenY, extended: true,
+            logFailure: failure => Log.Warning(failure, "Windows' menu or its command failed for {Target}", item.Target),
+            header: "Windows menu — acts on the real file", customCommands: [], handDeleteBack: false, out _);
+    }
+
+    private static void ShowInFolder(string target) =>
+        ShellWorker.RunAlone(() =>
+        {
+            if (!ShellItems.TryShowInFolder(target)) Log.Warning("could not show {Target} in Explorer", target);
+        }, name: "NeoFences show in folder");
+
+    private static void CopyText(string text)
+    {
+        try
+        {
+            Clipboard.SetText(text);
+        }
+        catch (System.Runtime.InteropServices.ExternalException busy)
+        {
+            Log.Warning(busy, "the clipboard is busy; path not copied"); // another app holds it (hard rule 7)
+        }
+    }
+
+    // ---------- remove, Properties, Add item…, Locate… ----------
+
+    /// <summary>Del / "Remove from fence": the items go (one confirmation for several); their targets are never touched.</summary>
+    private void RemoveItems(FenceWindow window, IReadOnlyList<string> keys)
+    {
+        if (window.IsLibrary)
+        {
+            HideGames(keys); // Delete in the library hides the game; its shortcut is NeoFences' own (M12)
+            return;
+        }
+        if (keys.Count > 1 && MessageBox.Show(window, $"Remove {keys.Count} items from this fence?\n\nYour files, folders and apps are not touched.",
+                "NeoFences", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
+        _items = ItemEdits.Remove(_items, keys.ToHashSet(StringComparer.Ordinal));
+        Log.Information("{Count} item(s) removed from fence {FenceId}", keys.Count, window.FenceId);
+        ItemsChanged(checkTargets: []);
+    }
+
+    /// <summary>Properties (F2: the name selected; Alt+Enter; the menu): changes only the item (spec §3).</summary>
+    private void ShowProperties(FenceWindow window, string key, bool focusName)
+    {
+        if (window.IsLibrary || _items.Find(key) is not { } item) return;
+        var dialog = new ItemPropertiesWindow(item, adding: false, iconLoader: _iconLoader, focusName: focusName) { Owner = window };
+        if (dialog.ShowDialog() != true || dialog.Result is not { } changed) return;
+        changed = WithCopiedPicture(window, changed, dialog.PictureToCopy);
+        _items = ItemEdits.Replace(_items, changed);
+        Log.Information("item {ItemId} changed in Properties", changed.Id);
+        ItemsChanged(checkTargets: [changed.Target]);
+    }
+
+    /// <summary>Fence menu → "Add item…" (spec §2): a file, folder, app or website, with its own name if wanted.</summary>
+    private void AddItem(FenceWindow window)
+    {
+        if (window.IsLibrary) return;
+        var dialog = new ItemPropertiesWindow(VirtualItem.Create(""), adding: true, iconLoader: _iconLoader, focusName: false) { Owner = window };
+        if (dialog.ShowDialog() != true || dialog.Result is not { } created) return;
+        var added = ItemEdits.Add(_items, window.FenceId, [WithCopiedPicture(window, created, dialog.PictureToCopy)]);
+        _items = added.Document;
+        Log.Information("item added to fence {FenceId}: {Target}", window.FenceId, created.Target);
+        ItemsChanged(checkTargets: [created.Target]);
+        window.SelectItems(added.AddedIds.Count > 0 ? added.AddedIds : added.AlreadyThereIds); // already there: it flashes
+    }
+
+    /// <summary>Locate… (spec §4): a picker at the nearest folder that still exists; only the target changes.</summary>
+    private void Locate(FenceWindow window, string itemId)
+    {
+        if (_items.Find(itemId) is not { } item) return;
+        var name = DisplayName(item);
+        var asFolder = !Path.HasExtension(item.Target.TrimEnd('\\')); // ponytail: a folder named "x.y" opens the file picker; Properties → Browse covers it
+        // Off the UI thread: the old drive may be a sleeping disk or a share that does not answer.
+        Task.Run(() => NearestExistingFolder(item.Target)).ContinueWith(found =>
+        {
+            void LogFailure(Exception failure) => Log.Warning(failure, "Locate… dialog failed");
+            var picked = asFolder
+                ? PathPicker.TryPickFolder(window.Handle, $"Where is \"{name}\" now?", LogFailure, found.Result)
+                : PathPicker.TryPickFile(window.Handle, $"Where is \"{name}\" now?", LogFailure, found.Result);
+            if (picked is null || _items.Find(itemId) is not { } current) return;
+            _items = ItemEdits.Replace(_items, current with { Target = picked });
+            Log.Information("item {ItemId} located at {Target}", itemId, picked);
+            ItemsChanged(checkTargets: [picked]);
+        }, TaskScheduler.FromCurrentSynchronizationContext());
+    }
+
+    private static string? NearestExistingFolder(string target)
+    {
+        try
+        {
+            for (var folder = Path.GetDirectoryName(target.TrimEnd('\\')); folder is not null; folder = Path.GetDirectoryName(folder))
+            {
+                if (Directory.Exists(folder)) return folder;
+            }
+        }
+        catch (Exception failure) when (failure is IOException or ArgumentException or UnauthorizedAccessException)
+        {
+            Log.Debug(failure, "no folder of {Target} to start Locate… in", target);
+        }
+        return null;
+    }
+
+    /// <summary>
+    /// A picture chosen as the icon (spec §3) is copied into NeoFences' icons folder, at most 256 px, as PNG, under a new
+    /// name each time (so a cached older picture never shows). Unreadable: the item keeps its icon and the user is told.
+    /// </summary>
+    private static VirtualItem WithCopiedPicture(Window owner, VirtualItem item, string? picture)
+    {
+        if (picture is null) return item;
+        try
+        {
+            var frame = BitmapFrame.Create(new Uri(picture), BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
+            var scale = Math.Min(1.0, (double)MaxPicturePx / Math.Max(frame.PixelWidth, frame.PixelHeight));
+            BitmapSource scaled = scale < 1 ? new TransformedBitmap(frame, new ScaleTransform(scale, scale)) : frame;
+            var encoder = new PngBitmapEncoder();
+            encoder.Frames.Add(BitmapFrame.Create(scaled));
+            Directory.CreateDirectory(AppPaths.IconsDirectory);
+            var fileName = $"{item.Id}-{Guid.NewGuid():N}.png";
+            using (var file = File.Create(Path.Combine(AppPaths.IconsDirectory, fileName))) encoder.Save(file);
+            return item with { Icon = new ItemIcon { Image = fileName } };
+        }
+        catch (Exception failure) when (failure is not OutOfMemoryException)
+        {
+            Log.Warning(failure, "picture {Picture} could not be used as an icon", picture);
+            MessageBox.Show(owner, $"NeoFences could not use this picture as the icon:\n{picture}", "NeoFences", MessageBoxButton.OK, MessageBoxImage.Warning);
+            return item;
+        }
+    }
+
+    /// <summary>At start: pictures in the icons folder no item uses any more (replaced, or their item removed) go. NeoFences' own files only.</summary>
+    private static void CleanUnusedPictures(IReadOnlySet<string> inUse) =>
+        Task.Run(() =>
+        {
+            try
+            {
+                if (!Directory.Exists(AppPaths.IconsDirectory)) return;
+                foreach (var picture in Directory.GetFiles(AppPaths.IconsDirectory, "*.png").Where(path => !inUse.Contains(Path.GetFileName(path))))
+                {
+                    File.Delete(picture);
+                    Log.Information("unused item picture {Picture} deleted", Path.GetFileName(picture));
+                }
+            }
+            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
+            {
+                Log.Warning(failure, "unused item pictures could not be cleaned up"); // tried again at the next start
+            }
+        });
+
+    /// <summary>After any item change: windows, save, watching, and a check of the targets that are new.</summary>
+    private void ItemsChanged(IReadOnlyList<string> checkTargets)
+    {
+        RefreshWindows();
+        ScheduleSave();
+        UpdateWatching();
+        if (checkTargets.Count > 0) CheckTargets(checkTargets);
+    }
+
+    // ---------- drops and drags (spec §2) ----------
+
+    /// <summary>Items dragged from a fence: moved here (Ctrl: duplicated). Library games dragged in become items of their shortcut.</summary>
+    private void OnItemsDropped(FenceWindow window, IReadOnlyList<string> keys, int insertAt, bool duplicate)
+    {
+        if (window.IsLibrary) return;
+        var known = keys.Where(key => _items.Find(key) is not null).ToList();
+        var fromLibrary = keys.Except(known).ToList(); // the library's key is its shortcut's path (M12)
+        if (known.Count > 0)
+        {
+            _items = duplicate ? ItemEdits.Duplicate(_items, known, window.FenceId, insertAt).Document : ItemEdits.Move(_items, known, window.FenceId, insertAt);
+            Log.Information("{Count} item(s) {Action} to fence {FenceId}", known.Count, duplicate ? "duplicated" : "moved", window.FenceId);
+        }
+        if (fromLibrary.Count > 0) AddTargets(window, fromLibrary, insertAt);
+        else ItemsChanged(checkTargets: []);
+    }
+
+    /// <summary>Files, folders or a link from outside: new items at the drop point; the originals stay where they are.</summary>
+    private void OnTargetsDropped(FenceWindow window, IReadOnlyList<string> targets, int insertAt)
+    {
+        if (!window.IsLibrary && targets.Count > 0) AddTargets(window, targets, insertAt);
+    }
+
+    private void AddTargets(FenceWindow window, IReadOnlyList<string> targets, int insertAt)
+    {
+        var added = ItemEdits.Add(_items, window.FenceId, [.. targets.Select(VirtualItem.Create)], insertAt);
+        _items = added.Document;
+        Log.Information("{Added} item(s) added to fence {FenceId}; {Already} already there", added.AddedIds.Count, window.FenceId, added.AlreadyThereIds.Count);
+        ItemsChanged(checkTargets: targets);
+        if (added.AlreadyThereIds.Count > 0) window.SelectItems(added.AlreadyThereIds); // already in this fence: it flashes
+    }
+
+    /// <summary>Dragging items out (spec §2): other apps get the files (copied, never moved) or the websites; fences get the items.</summary>
+    private void DragItems(FenceWindow window, IReadOnlyList<string> keys)
+    {
+        IReadOnlyList<string> files, urls;
+        if (window.IsLibrary)
+        {
+            (files, urls) = (keys, []); // NeoFences' own shortcuts (M12)
+        }
+        else
+        {
+            var items = keys.Select(_items.Find).OfType<VirtualItem>().ToList();
+            files = [.. items.Where(item => item.Kind == ItemKind.Path && TargetChecks.RootOf(item.Target) is not null && CheckOf(item.Target).State == TargetState.Ok)
+                .Select(item => item.Target)];
+            urls = [.. items.Where(item => item.Kind == ItemKind.Website).Select(item => item.Target)];
+        }
+        ShellDragDrop.TryDrag(window.Handle, keys, files, urls, logFailure: failure => Log.Warning(failure, "could not start dragging {Keys}", keys));
+    }
+
+    // ---------- sort ----------
+
+    /// <summary>"Sort by" (one time): by the names shown, type or date; dragging keeps working afterwards.</summary>
+    private void SortFence(FenceWindow window, FenceSort sort)
+    {
+        if (window.IsLibrary) return;
+        try
+        {
+            // ponytail: reads each target's facts on the UI thread (a sleeping disk stalls the sort); off-thread if fences get big.
+            var facts = _items.Of(window.FenceId).Select(FactsOf).ToList();
+            _items = ItemEdits.Reorder(_items, window.FenceId, ItemSorting.Order(facts, sort));
+        }
+        catch (Exception failure) when (failure is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
+        {
+            Log.Warning(failure, "sort of fence {FenceId} failed; its order stays", window.FenceId); // never crash (M4 review I3)
+            return;
+        }
+        RefreshWindow(window);
+        ScheduleSave();
+    }
+
+    /// <summary>What sorting needs about one item: its target's facts (a file on disk), its own name first.</summary>
+    private static ItemInfo FactsOf(VirtualItem item)
+    {
+        if (item.Kind == ItemKind.Path && TargetChecks.RootOf(item.Target) is not null)
+        {
+            var described = FolderItems.Describe([item.Target])[0];
+            return described with { ItemRef = item.Id, Name = item.OwnName ?? described.Name };
+        }
+        return new ItemInfo(item.Id, DisplayName(item), IsFolder: item.Kind == ItemKind.Special, TypeName: item.Kind == ItemKind.Website ? "URL" : "",
+            DateTimeOffset.MinValue);
+    }
+}
diff --git a/src/NeoFences.App/FenceHost.Library.cs b/src/NeoFences.App/FenceHost.Library.cs
index 81474a9..9d977cf 100644
--- a/src/NeoFences.App/FenceHost.Library.cs
+++ b/src/NeoFences.App/FenceHost.Library.cs
@@ -1,7 +1,6 @@
 using System.IO;
 using System.Windows.Threading;
 using NeoFences.Core.Library;
-using NeoFences.Core.Membership;
 using NeoFences.Core.Model;
 using NeoFences.Shell;
 using Serilog;
@@ -11,7 +10,7 @@ namespace NeoFences.App;
 /// <summary>
 /// The Game Library fence (M12, spec 2026-10-03-game-library-design, ADR-032): scans the launchers, the Xbox app, the
 /// user's game folders and Desktop game shortcuts on its own STA thread, merges them (Core <see cref="GameCatalog"/>),
-/// keeps one shortcut per game in <see cref="AppPaths.LibraryDirectory"/> and shows that folder like a Portal, as tiles.
+/// keeps one shortcut per game in <see cref="AppPaths.LibraryDirectory"/> and shows that folder (LibraryLister) as tiles.
 /// Nothing is started or changed outside NeoFences' own folder.
 /// </summary>
 public sealed partial class FenceHost
@@ -27,9 +26,36 @@ public sealed partial class FenceHost
     private bool _libraryFullScan; // the next scan searches every game folder again (final review I4)
     private string _libraryStatus = "Not scanned yet.";
 
-    private bool HasLibraryFence => _config.Fences.Any(fence => fence.Source.Kind == FenceSourceKind.Library);
+    private bool HasLibraryFence => _config.Fences.Any(fence => fence.IsLibrary);
 
-    /// <summary>Starts the library when its fence appears, stops watching when it goes (EnsurePortals calls this).</summary>
+    /// <summary>The library folder's lister while the Game Library fence exists (a hidden library tab keeps listing, M9).</summary>
+    private void EnsureLibraryLister()
+    {
+        var hasFence = HasLibraryFence;
+        if (!hasFence && _libraryLister is not null)
+        {
+            _libraryLister.Dispose(); // NeoFences' own folder stays as it is
+            _libraryLister = null;
+        }
+        else if (hasFence && _libraryLister is null)
+        {
+            TryCreateFolder(AppPaths.LibraryDirectory);
+            _libraryLister = new LibraryLister(AppPaths.LibraryDirectory, noticeOwner: _messages.Handle, show: ShowLibrary,
+                logFailure: failure => Log.Warning(failure, "cannot watch the game library folder"));
+            if (_gameMode) _libraryLister.SetPaused(true);
+        }
+        UpdateLibrary(); // M12: the library scans while its fence exists
+    }
+
+    /// <summary>The library folder's listing (null: unreadable) in catalog order, as tiles, in the window showing the library.</summary>
+    private void ShowLibrary(IReadOnlyList<ItemInfo>? listed)
+    {
+        if (_windows.Values.FirstOrDefault(window => window.IsLibrary) is not { } window) return;
+        window.SetLibraryArt(LibraryArt());
+        window.SetItems(listed is null ? [] : [.. LibraryOrder(listed).Select(path => new ShownItem(path, path))]); // A–Z by game (M12)
+    }
+
+    /// <summary>Starts the library when its fence appears, stops watching when it goes (EnsureLibraryLister calls this).</summary>
     private void UpdateLibrary()
     {
         if (HasLibraryFence == _libraryActive) return;
@@ -112,7 +138,7 @@ public sealed partial class FenceHost
                          + (unreadable.Count > 0 ? $"; not readable right now: {string.Join(", ", unreadable.Select(GameCatalog.SourceName))}" : ".");
         if (state is null || !_libraryActive) DisposeWatchers(watch); // a failed scan keeps the watchers it had (final review I2)
         else ReplaceLibraryWatchers(watch);
-        foreach (var window in _windows.Values.Where(window => window.IsLibrary)) RefreshPortal(window); // new art, order
+        _libraryLister?.Refresh(); // new art, order
         RefreshSettings();
         if (!_libraryScanAgain) return;
         _libraryScanAgain = false;
@@ -167,28 +193,6 @@ public sealed partial class FenceHost
         }
     }
 
-    /// <summary>
-    /// A Desktop shortcut created, changed, renamed or deleted: the library's shortcut games follow, after 5 quiet seconds
-    /// (M13c; the library watches no Desktop folder of its own).
-    /// </summary>
-    private void OnDesktopShortcutChange(DesktopChange change)
-    {
-        OnDesktopShortcutsChanged(change switch
-        {
-            DesktopChange.Created created => [created.ItemRef],
-            DesktopChange.Deleted deleted => [deleted.ItemRef],
-            DesktopChange.Renamed renamed => [renamed.OldRef, renamed.NewRef],
-            _ => [],
-        });
-    }
-
-    /// <summary>Any of these Desktop items is a shortcut: the library's shortcut games follow after 5 quiet seconds.</summary>
-    private void OnDesktopShortcutsChanged(IReadOnlyList<string> itemRefs)
-    {
-        if (!_libraryActive || !_config.Library.Sources.DesktopShortcuts) return;
-        if (itemRefs.Any(itemRef => Path.GetExtension(itemRef).ToLowerInvariant() is ".lnk" or ".url")) ScheduleLibraryScan();
-    }
-
     private void ScheduleLibraryScan()
     {
         if (_libraryTimer is null)
@@ -238,13 +242,13 @@ public sealed partial class FenceHost
     /// <summary>Tray / fence menu → "New Game Library fence": one at most; with one already, its tab is shown.</summary>
     private void CreateLibraryFence()
     {
-        if (_config.Fences.FirstOrDefault(fence => fence.Source.Kind == FenceSourceKind.Library) is { } existing)
+        if (_config.Fences.FirstOrDefault(fence => fence.IsLibrary) is { } existing)
         {
             if (FenceTabs.HostOf(_config, existing.Id) is { } host && _windows.TryGetValue(host.Id, out var window)) SwitchTab(window, existing.Id);
             return;
         }
         SetQuickHidden(false); // a new fence must show (as "New fence" does)
-        var fence = Fence.Create("Games", FenceSource.Library) with { Sort = FenceSort.Name, IconSize = 64, Labels = _config.Settings.DefaultLabels };
+        var fence = Fence.Create("Games", isLibrary: true) with { IconSize = 64, Labels = _config.Settings.DefaultLabels };
         _config = _config with { Fences = [.. _config.Fences, fence] };
         Log.Information("Game Library fence created");
         SyncBoxes();
@@ -281,11 +285,12 @@ public sealed partial class FenceHost
         else Log.Information("game library: no install folder known for {ItemRef}", itemRef);
     }
 
+    /// <summary>Windows' menu for NeoFences' own game shortcuts (M12): Delete only hides the game, nothing goes to the Recycle Bin.</summary>
     private void ShowLibraryItemMenu(FenceWindow window, IReadOnlyList<string> itemRefs, int screenX, int screenY, bool extended)
     {
         var choice = ShellItemMenu.Show(window.Handle, itemRefs, screenX, screenY, extended,
             logFailure: failure => Log.Warning(failure, "item menu or its command failed for {ItemRefs}", itemRefs),
-            customCommands: ["Hide from library", "Open install folder"], canRename: false, out var custom);
+            header: null, customCommands: ["Hide from library", "Open install folder"], handDeleteBack: true, out var custom);
         if (choice == ItemMenuChoice.Delete || (choice == ItemMenuChoice.Custom && custom == 0)) HideGames(itemRefs); // nothing goes to the Recycle Bin
         else if (choice == ItemMenuChoice.Custom && custom == 1) OpenInstallFolder(window, itemRefs[0]);
     }
diff --git a/src/NeoFences.App/FenceHost.Updates.cs b/src/NeoFences.App/FenceHost.Updates.cs
index 268b550..655a453 100644
--- a/src/NeoFences.App/FenceHost.Updates.cs
+++ b/src/NeoFences.App/FenceHost.Updates.cs
@@ -151,7 +151,7 @@ public sealed partial class FenceHost
         if (_updates is not { } updates || _readyUpdate is not { } ready) return;
         // Updates switched off: a plain Exit does not install (an explicit "Restart to update" still does; final review M2).
         if (!_restartAfterUpdate && !_config.Settings.AutoUpdate) return;
-        if (_watchdog.IsTakeoverActiveMarked)
+        if (_watchdog.IsIconsHiddenMarked)
         {
             // The icons could not be shown just now: the watchdog is still restoring them, and Update.exe would end it
             // (hard rule 2). The update waits for the next exit (final review M1).
diff --git a/src/NeoFences.App/FenceHost.Watching.cs b/src/NeoFences.App/FenceHost.Watching.cs
new file mode 100644
index 0000000..2d431b5
--- /dev/null
+++ b/src/NeoFences.App/FenceHost.Watching.cs
@@ -0,0 +1,262 @@
+using System.Windows.Threading;
+using NeoFences.Core.Items;
+using NeoFences.Shell;
+using Serilog;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// Target watching (M18 spec §4): the folders holding targets are watched (at most <see cref="WatchPlan.MaxFolders"/>,
+/// the busiest first); a rename in place is followed, a deletion shows the item Missing, a drive that is not there shows
+/// it Unavailable. Each fence refreshes at most once every 2 s; every target is checked again every 5 minutes, when its
+/// fence becomes visible, on Refresh and when a drive comes or goes. Checks run off the UI thread; games defer them.
+/// </summary>
+public sealed partial class FenceHost
+{
+    private static readonly TimeSpan RecheckInterval = TimeSpan.FromMinutes(5);
+    private static readonly TimeSpan FolderChangeQuiet = TimeSpan.FromMilliseconds(300);
+    private static readonly TimeSpan DriveChangeQuiet = TimeSpan.FromSeconds(1);
+    private const int MaxDeferredRenames = 500;
+
+    private readonly Dictionary<string, TargetCheck> _targetChecks = new(ItemKinds.Comparer);
+    private readonly Dictionary<string, (FolderWatcher Watcher, DeviceRemovalNotice? Notice)> _targetWatchers = new(ItemKinds.Comparer);
+    private HashSet<string> _wantedFolders = new(ItemKinds.Comparer);
+    private readonly HashSet<string> _armingFolders = new(ItemKinds.Comparer); // watchers being opened off the UI thread
+    private readonly RefreshThrottle _refreshThrottle = new();
+    private readonly Dictionary<string, DispatcherTimer> _fenceRefreshTimers = new(StringComparer.Ordinal);
+    private readonly HashSet<string> _changedFolders = new(ItemKinds.Comparer);
+    private readonly List<(string OldPath, string NewPath)> _deferredRenames = [];
+    private DispatcherTimer? _folderChangeTimer, _recheckTimer, _drivesTimer;
+    private bool _checksDeferred, _watchingStopped;
+
+    /// <summary>The last check of a target; Ok while it was never checked (fences show at once, states fill in).</summary>
+    private TargetCheck CheckOf(string target) => _targetChecks.TryGetValue(target, out var check) ? check : TargetCheck.Ok;
+
+    private TargetState StateOf(string target) => CheckOf(target).State;
+
+    private void StartWatching()
+    {
+        _recheckTimer = new DispatcherTimer { Interval = RecheckInterval };
+        _recheckTimer.Tick += (_, _) => CheckAllTargets(); // also re-arms watchers that stopped or were let go for a removal
+        _recheckTimer.Start();
+        CheckAllTargets();
+    }
+
+    private void StopWatching()
+    {
+        _watchingStopped = true;
+        _recheckTimer?.Stop();
+        _folderChangeTimer?.Stop();
+        _drivesTimer?.Stop();
+        foreach (var timer in _fenceRefreshTimers.Values) timer.Stop();
+        foreach (var (watcher, notice) in _targetWatchers.Values)
+        {
+            watcher.Dispose();
+            notice?.Dispose();
+        }
+        _targetWatchers.Clear();
+    }
+
+    /// <summary>Watches the folders the plan picks now; watchers no longer wanted (or not watching any more) go.</summary>
+    private void UpdateWatching()
+    {
+        if (_watchingStopped) return;
+        _wantedFolders = WatchPlan.Folders(ItemEdits.PathTargets(_items)).ToHashSet(ItemKinds.Comparer);
+        foreach (var (folder, (watcher, notice)) in _targetWatchers.Where(entry => !_wantedFolders.Contains(entry.Key) || !entry.Value.Watcher.IsWatching).ToList())
+        {
+            watcher.Dispose();
+            notice?.Dispose();
+            _targetWatchers.Remove(folder);
+        }
+        var toArm = _wantedFolders.Where(folder => !_targetWatchers.ContainsKey(folder) && _armingFolders.Add(folder)).ToList();
+        if (toArm.Count == 0) return;
+        var noticeOwner = _messages.Handle;
+        var dispatcher = Dispatcher.CurrentDispatcher;
+        // Off the UI thread: opening a watcher on a sleeping disk or a share can take seconds (final review I2).
+        Task.Run(() => toArm.Select(folder => (Folder: folder, Watch: Watch(folder, noticeOwner))).ToList()).ContinueWith(armed =>
+        {
+            foreach (var (folder, watch) in armed.Result)
+            {
+                _armingFolders.Remove(folder);
+                if (_watchingStopped || !_wantedFolders.Contains(folder) || _targetWatchers.ContainsKey(folder))
+                {
+                    watch.Watcher.Dispose();
+                    watch.Notice?.Dispose();
+                    continue;
+                }
+                watch.Watcher.Changed += () => dispatcher.BeginInvoke(() => OnTargetFolderChanged(folder));
+                watch.Watcher.Renamed += (oldPath, newPath) => dispatcher.BeginInvoke(() => OnTargetRenamed(oldPath, newPath));
+                // Events were lost or it stopped: its targets are checked now; the 5-minute check re-arms it.
+                watch.Watcher.Failed += () => dispatcher.BeginInvoke(() => OnTargetFolderChanged(folder));
+                _targetWatchers[folder] = watch;
+            }
+        }, TaskScheduler.FromCurrentSynchronizationContext());
+    }
+
+    /// <summary>A watcher, with a removal notice where Windows offers one, so "Safely remove" still works (M8d). Off the UI thread.</summary>
+    private static (FolderWatcher Watcher, DeviceRemovalNotice? Notice) Watch(string folder, nint noticeOwner)
+    {
+        var watcher = new FolderWatcher(folder, failure => Log.Debug(failure, "cannot watch {Folder}; its items are checked every few minutes", folder));
+        var notice = watcher.HeldFolder is { } held
+            ? DeviceRemovalNotice.TryRegister(noticeOwner, held, failure => Log.Debug(failure, "no removal notice for {Folder}", held))
+            : null;
+        return (watcher, notice);
+    }
+
+    /// <summary>Windows asks to remove a drive holding a watched folder (the test stick, G:): let go of it now.</summary>
+    private bool ReleaseTargetWatcherForRemoval(nint handle)
+    {
+        if (_targetWatchers.FirstOrDefault(entry => entry.Value.Notice?.Handle == handle) is not { Key: { } folder, Value: var (watcher, notice) }) return false;
+        watcher.Dispose();
+        notice?.Dispose();
+        _targetWatchers.Remove(folder);
+        return true; // re-armed when the drive is back (DrivesChanged) or by the 5-minute check
+    }
+
+    /// <summary>Something in a watched folder changed: its targets are checked once the burst is over.</summary>
+    private void OnTargetFolderChanged(string folder)
+    {
+        if (_watchingStopped) return;
+        _changedFolders.Add(folder);
+        if (_folderChangeTimer is null)
+        {
+            _folderChangeTimer = new DispatcherTimer { Interval = FolderChangeQuiet };
+            _folderChangeTimer.Tick += (_, _) =>
+            {
+                _folderChangeTimer.Stop();
+                var targets = ItemEdits.PathTargets(_items).Where(target => WatchPlan.ParentOf(target) is { } parent && _changedFolders.Contains(parent)).ToList();
+                _changedFolders.Clear();
+                CheckTargets(targets);
+            };
+        }
+        _folderChangeTimer.Stop();
+        _folderChangeTimer.Start();
+    }
+
+    /// <summary>A target (or a folder holding targets) was renamed in place: every item pointing at it follows (spec §4).</summary>
+    private void OnTargetRenamed(string oldPath, string newPath)
+    {
+        if (_watchingStopped) return;
+        if (Current.ShellWorkDeferred)
+        {
+            if (_deferredRenames.Count < MaxDeferredRenames) _deferredRenames.Add((oldPath, newPath)); // after the game
+            else _checksDeferred = true; // too many: the check after the game shows what is missing
+            return;
+        }
+        FollowRename(oldPath, newPath);
+    }
+
+    private void FollowRename(string oldPath, string newPath)
+    {
+        var followed = ItemEdits.Retarget(_items, oldPath, newPath);
+        if (ReferenceEquals(followed, _items)) return; // not a target
+        _items = followed;
+        Log.Information("target renamed: {OldPath} -> {NewPath}; its items follow", oldPath, newPath);
+        ItemsChanged(checkTargets: [newPath]);
+    }
+
+    /// <summary>A drive came or went: every target is checked again (and watchers re-armed) after a quiet second.</summary>
+    private void OnDrivesChanged()
+    {
+        if (_watchingStopped) return;
+        if (_drivesTimer is null)
+        {
+            _drivesTimer = new DispatcherTimer { Interval = DriveChangeQuiet };
+            _drivesTimer.Tick += (_, _) =>
+            {
+                _drivesTimer.Stop();
+                CheckAllTargets();
+            };
+        }
+        _drivesTimer.Stop();
+        _drivesTimer.Start();
+    }
+
+    /// <summary>Fence menu → Refresh: its targets checked now, its names and icons loaded again (the library rescans, M12).</summary>
+    private void RefreshFence(FenceWindow window)
+    {
+        if (window.IsLibrary)
+        {
+            ScanLibrary(full: true);
+            return;
+        }
+        CheckFence(window.FenceId);
+        window.ReloadIcons();
+    }
+
+    private void CheckFence(string fenceId) =>
+        CheckTargets([.. _items.Of(fenceId).Where(item => item.Kind == ItemKind.Path).Select(item => item.Target).Distinct(ItemKinds.Comparer)]);
+
+    private void CheckAllTargets()
+    {
+        UpdateWatching();
+        CheckTargets(ItemEdits.PathTargets(_items));
+    }
+
+    /// <summary>Checks these targets off the UI thread (a share answers within 2 s or counts as Unavailable); games defer it.</summary>
+    private void CheckTargets(IReadOnlyList<string> targets)
+    {
+        if (_watchingStopped || targets.Count == 0) return;
+        if (Current.ShellWorkDeferred)
+        {
+            _checksDeferred = true; // games get every bit of the machine: all of them after the game (spec §4.7)
+            return;
+        }
+        Task.Run(() => targets.Select(target => (target, TargetProbe.Check(target))).ToList())
+            .ContinueWith(checks => ApplyChecks(checks.Result), TaskScheduler.FromCurrentSynchronizationContext());
+    }
+
+    /// <summary>New states: the fences holding a target whose state changed refresh (at most once every 2 s each).</summary>
+    private void ApplyChecks(IReadOnlyList<(string Target, TargetCheck Check)> checks)
+    {
+        if (_watchingStopped) return;
+        var changed = new HashSet<string>(ItemKinds.Comparer);
+        foreach (var (target, check) in checks)
+        {
+            if (_targetChecks.TryGetValue(target, out var before) && before == check) continue;
+            if (check.State != TargetState.Ok || before is { State: not TargetState.Ok })
+                Log.Information("target {Target}: {State}", target, check.State);
+            _targetChecks[target] = check;
+            changed.Add(target);
+        }
+        if (changed.Count == 0) return;
+        foreach (var (fenceId, items) in _items.Fences.Where(entry => entry.Value.Any(item => changed.Contains(item.Target))))
+        {
+            ScheduleFenceRefresh(fenceId);
+        }
+    }
+
+    private void ScheduleFenceRefresh(string fenceId)
+    {
+        if (_fenceRefreshTimers.TryGetValue(fenceId, out var pending) && pending.IsEnabled) return; // already coming
+        var delay = _refreshThrottle.DelayFor(fenceId, Now);
+        var timer = pending ?? new DispatcherTimer();
+        if (pending is null)
+        {
+            timer.Tick += (_, _) =>
+            {
+                timer.Stop();
+                _refreshThrottle.Refreshed(fenceId, Now);
+                if (_windows.Values.FirstOrDefault(window => window.FenceId == fenceId) is { } window) RefreshWindow(window);
+            };
+            _fenceRefreshTimers[fenceId] = timer;
+        }
+        timer.Interval = delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(1);
+        timer.Start();
+    }
+
+    /// <summary>The game was left: renames seen meanwhile are followed, and every target is checked if checks waited.</summary>
+    private void ApplyDeferredWatching()
+    {
+        if (_deferredRenames.Count > 0)
+        {
+            var renames = _deferredRenames.ToList();
+            _deferredRenames.Clear();
+            Log.Information("following {Count} rename(s) from game mode", renames.Count);
+            foreach (var (oldPath, newPath) in renames) FollowRename(oldPath, newPath); // also at a session end during a game
+        }
+        if (!_checksDeferred) return;
+        _checksDeferred = false;
+        CheckAllTargets();
+    }
+}
diff --git a/src/NeoFences.App/FenceHost.cs b/src/NeoFences.App/FenceHost.cs
index 94b77c6..b28d72f 100644
--- a/src/NeoFences.App/FenceHost.cs
+++ b/src/NeoFences.App/FenceHost.cs
@@ -4,9 +4,9 @@ using System.Windows.Interop;
 using System.Windows.Threading;
 using NeoFences.Core.Config;
 using NeoFences.Core.Input;
+using NeoFences.Core.Items;
 using NeoFences.Core.Layouts;
 using NeoFences.Core.Lifecycle;
-using NeoFences.Core.Membership;
 using NeoFences.Core.Model;
 using NeoFences.Shell;
 using Serilog;
@@ -14,10 +14,11 @@ using Serilog;
 namespace NeoFences.App;
 
 /// <summary>
-/// Owns the fences on the desktop: loads the config, places one <see cref="FenceWindow"/> per fence on the current
-/// monitors, keeps fence contents in step with the Desktop folders (reconcile at start, then watcher events),
-/// saves changes (debounced 500 ms, ADR-006), and keeps everything attached through display changes,
-/// Explorer restarts and sign-out (ADR-011, ADR-013). Every Win32/COM call goes through NeoFences.Shell.
+/// Owns the fences on the desktop: loads the config and the items, places one <see cref="FenceWindow"/> per box on the
+/// current monitors, shows each fence's virtual items (ADR-040) and keeps their targets checked (FenceHost.Watching),
+/// saves changes (debounced 500 ms, ADR-006, ADR-041), and keeps everything attached through display changes, Explorer
+/// restarts and sign-out (ADR-011, ADR-013). Nothing here touches a user's file; every Win32/COM call goes through
+/// NeoFences.Shell.
 /// </summary>
 public sealed partial class FenceHost
 {
@@ -33,33 +34,28 @@ public sealed partial class FenceHost
     private const int TrayRetryAttempts = 15;
 
     private readonly ConfigStore _store = new(AppPaths.DataDirectory);
+    private readonly ItemStore _itemStore = new(AppPaths.DataDirectory); // ADR-041: the items in a file of their own
     private readonly Watchdog _watchdog = new(AppPaths.DataDirectory, message => Log.Information("watchdog: {Message}", message));
     private readonly SystemMessageWindow _messages = new();
     private readonly Dictionary<string, FenceWindow> _windows = new(StringComparer.Ordinal);
     private readonly DispatcherTimer _saveTimer;
     private readonly IconLoader _iconLoader = new(Dispatcher.CurrentDispatcher);
-    private DesktopWatcher? _desktopWatcher;
-    private readonly ShellWorker _shellWorker = new(); // open, recycle, rename: off the UI thread, on STA (M3a review)
+    private readonly ShellWorker _shellWorker = new(); // recycling snapshots: off the UI thread, on STA (M3a review)
     private SpecialIconNotifications? _specialIcons;
     private bool _specialIconsDeferred;
     private readonly SnapshotStore _snapshots = new(Path.Combine(AppPaths.DataDirectory, "snapshots")); // M10
     private readonly HashSet<string> _loggedSnapshotProblems = new(StringComparer.OrdinalIgnoreCase);
     private readonly DispatcherTimer _specialIconsTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
-    // Rules wait until arrivals are quiet: a shortcut or file still being written reads wrong (M11 smoke X4, review M3).
-    private readonly DispatcherTimer _filingTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
-    private readonly List<string> _pendingArrivals = [];
-    // Watcher trouble arrives in bursts: one re-arm and one reconcile per burst; a watcher that fails again at once waits longer (M8a review).
-    private readonly DispatcherTimer _watcherRecoveryTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
-    private bool _rearmWatcher;
-    private DateTime _lastWatcherRearm = DateTime.MinValue;
-    private TimeSpan _watcherRearmDelay = WatcherBackoff.First;
-    // Safe-save memory and expected drop arrivals (FenceMembership.SafeSaveWindow).
-    private IReadOnlyList<RememberedPlacement> _rememberedPlacements = [];
     private readonly Dictionary<string, IDisposable> _dropRegistrations = new(StringComparer.Ordinal);
-    private readonly Dictionary<string, PortalState> _portals = new(StringComparer.Ordinal); // M4: Portal fences by id
+    private LibraryLister? _libraryLister; // the Game Library fence's folder (M12), while that fence exists
     private NeoFencesConfig _config = NeoFencesConfig.CreateDefault();
+    private ItemsDocument _items = new();
+    /// <summary>
+    /// False when this session's config is only a fallback (fresh, or read-only): its fence ids are not the real ones, so
+    /// item lists it does not know are kept rather than dropped at save (ADR-041).
+    /// </summary>
+    private bool _pruneItemLists;
     private IReadOnlyList<MonitorPlacement> _monitors = [];
-    private bool _takeoverActive;
     private bool _lightTheme = SystemTheme.AppsUseLightTheme();
     private bool _sessionEnding;
     // M5 desktop gestures
@@ -81,9 +77,6 @@ public sealed partial class FenceHost
     private bool _gameMode;                  // a full-screen app is in front: idle (spec §4.7, ADR-021)
     private ForegroundWatcher? _foregroundWatcher;
     private TrayIcon? _trayIcon;
-    private readonly List<DesktopChange> _deferredDesktopChanges = [];
-    private bool _reconcileDeferred;
-    private const int MaxDeferredDesktopChanges = 500;
     private const int TrayNewFence = 1, TrayQuickHide = 2, TrayPeek = 3, TrayPause = 4, TrayExit = 5, TraySettings = 6;
     // Snapshots (M10): "Restore snapshot" lists the newest few by id TrayRestoreFirst + index.
     private const int TrayTakeSnapshot = 7, TrayRestoreMenu = 8, TrayRestoreBefore = 9, TrayRestoreFirst = 100, TrayRestoreCount = 10;
@@ -107,15 +100,11 @@ public sealed partial class FenceHost
         _messages.SpecialIconsChanged += ScheduleSpecialIconRefresh;
         _messages.DeviceRemovalRequested += handle =>
         {
-            foreach (var (fenceId, portal) in _portals)
-            {
-                if (portal.ReleaseForRemoval(handle)) Log.Information("drive of Portal {FenceId} is being removed: released it", fenceId);
-            }
+            if (_libraryLister?.ReleaseForRemoval(handle) == true) Log.Information("the library folder's drive is being removed: released it");
             if (ReleaseLibraryForRemoval(handle)) Log.Information("a drive the game library watches is being removed: released it");
+            if (ReleaseTargetWatcherForRemoval(handle)) Log.Information("a drive holding item targets is being removed: released it");
         };
         _specialIconsTimer.Tick += (_, _) => RefreshSpecialIcons();
-        _filingTimer.Tick += (_, _) => FilePendingArrivals();
-        _watcherRecoveryTimer.Tick += (_, _) => RecoverDesktopWatcher();
     }
 
     public void Start()
@@ -124,21 +113,27 @@ public sealed partial class FenceHost
         Log.Information("config loaded from {Source} (read-only: {IsReadOnly}, corrupt copy: {CorruptCopyPath})",
             loaded.Source, loaded.IsReadOnly, loaded.CorruptCopyPath);
         _config = loaded.Config;
+        var loadedItems = _itemStore.Load();
+        Log.Information("items loaded from {Source} (read-only: {IsReadOnly}, corrupt copy: {CorruptCopyPath}): {Count} item(s)",
+            loadedItems.Source, loadedItems.IsReadOnly, loadedItems.CorruptCopyPath, loadedItems.Document.Fences.Values.Sum(items => items.Count));
+        _items = loadedItems.Document;
+        _pruneItemLists = loaded.KnowsTheFences;
+        if (!_pruneItemLists && _items.Fences.Count > 0) Log.Warning("config is a fallback this session: item lists of unknown fences are kept");
+        if (loadedItems.Source == ConfigLoadSource.Primary && !loadedItems.IsReadOnly) CleanUnusedPictures(ItemEdits.ImagesInUse(_items));
         _watchdog.LaunchDetached(Environment.ProcessId);
         ApplyStartup(); // after a power loss NeoFences must come back by itself (ADR-019)
 
         RefreshMonitors();
         foreach (var box in FenceTabs.Boxes(_config)) OpenWindow(box); // one window per box (M9)
-        EnsurePortals();
-        StartDesktopWatcher(); // first: an item created while the startup reconcile lists the desktop is not missed (M8a)
-        ReconcileDesktop();
+        EnsureLibraryLister();
+        RefreshWindows();
         StartSpecialIconNotifications();
         ApplyLayout();
-        if (_config.Settings.Takeover) SetTakeover(true);
-        else if (_watchdog.IsTakeoverActiveMarked)
+        if (_config.Settings.HideDesktopIcons) SetIconsHidden(true);
+        else if (_watchdog.IsIconsHiddenMarked)
         {
-            // Icons may still be hidden from a run whose Takeover-off never got saved (both processes killed): show them.
-            Log.Warning("takeover-active marker found while Takeover is off; showing desktop icons");
+            // Icons may still be hidden from a run whose "show" never happened (both processes killed): show them.
+            Log.Warning("icons-hidden marker found while hiding is off; showing desktop icons");
             SetIconsHidden(false);
         }
         StartGestures();
@@ -153,21 +148,22 @@ public sealed partial class FenceHost
             Log.Error(failure, "tray icon unavailable; fences and gestures keep working"); // hard rule 7: only the tray is lost
         }
         StartGameMode();
+        StartWatching(); // M18: states fill in as the checks finish (spec §4)
         StartUpdates(); // M17: the first check a minute after start
         if (Appearance.WallpaperAccent) UpdateAccents(); // M14: the accent is read once at start, then on wallpaper changes
         ScheduleSave();
     }
 
     /// <summary>
-    /// The clock for safe-save and drop memory: wall time at start plus a monotonic stopwatch, so a clock change (time
-    /// sync, daylight saving, the user) cannot expire or extend a memory early (M8a).
+    /// The clock for refresh throttling: wall time at start plus a monotonic stopwatch, so a clock change (time sync,
+    /// daylight saving, the user) cannot stall or rush a refresh (M8a).
     /// </summary>
     private static readonly DateTimeOffset ClockStart = DateTimeOffset.Now;
     private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
     private static DateTimeOffset Now => ClockStart + Clock.Elapsed;
 
     /// <summary>All of NeoFences' run-time modes together (Core rules: which fences, icons and hooks they imply).</summary>
-    private RunState Current => new(Takeover: _takeoverActive, QuickHidden: _quickHidden, Paused: _paused, GameMode: _gameMode,
+    private RunState Current => new(HideIcons: _config.Settings.HideDesktopIcons, QuickHidden: _quickHidden, Paused: _paused, GameMode: _gameMode,
         IconsHiddenByUser: _iconsHiddenByUser);
 
     /// <summary>
@@ -178,9 +174,9 @@ public sealed partial class FenceHost
     public void OnSessionEnding()
     {
         _sessionEnding = true;
-        ApplyDeferredShellWork(); // Desktop changes queued during a game must be saved too (M6a review M2)
+        ApplyDeferredShellWork(); // renames seen during a game must be saved too (M6a review M2)
         SaveNow();
-        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) SetIconsHidden(false);
+        if (Current.IconsHidden || _watchdog.IsIconsHiddenMarked) SetIconsHidden(false);
         TryMarker(() => _watchdog.MarkSessionEnding(Environment.ProcessId), what: "session-ending marker");
     }
 
@@ -189,21 +185,21 @@ public sealed partial class FenceHost
     {
         WatchWallpaperEngine(watch: false); // M14
         _libraryStopped = true; // also on session end: a library scan finishing now writes and re-arms nothing (M13a review)
-        ApplyDeferredShellWork(); // Desktop changes queued during a game must be saved too (M6a review M2)
+        ApplyDeferredShellWork(); // renames seen during a game must be saved too (M6a review M2)
         SaveNow();
         _trayIcon?.Dispose(); // also on session end: a cancelled shutdown restarts us, and the old icon would linger (M8a)
         _trayIcon = null;
         if (_sessionEnding) return; // OnSessionEnding already restored and marked; no clean marker, so a cancel restarts us
         // The marker also catches a show that failed earlier (quick-hide ending while Explorer was busy; M5 review M6).
-        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) SetIconsHidden(false);
+        if (Current.IconsHidden || _watchdog.IsIconsHiddenMarked) SetIconsHidden(false);
         TryMarker(() => _watchdog.MarkCleanShutdown(Environment.ProcessId), what: "clean-shutdown marker");
         _foregroundWatcher?.Dispose();
         _mouseHook?.Dispose();
         _peekHotkey?.Dispose();
         _peekEscapeHotkey?.Dispose();
-        _desktopWatcher?.Dispose();
-        _desktopWatcher = null;
+        StopWatching();
         _specialIcons?.Dispose();
+        _libraryLister?.Dispose();
         StopLibraryWatchers();
         _libraryTimer?.Stop();
         _shellWorker.Dispose();
@@ -216,14 +212,14 @@ public sealed partial class FenceHost
     /// <summary>Best effort from the crash handler; the watchdog restores too.</summary>
     public void EmergencyRestoreIcons()
     {
-        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) DesktopIcons.TrySetHidden(false);
+        if (Current.IconsHidden || _watchdog.IsIconsHiddenMarked) DesktopIcons.TrySetHidden(false);
     }
 
     /// <summary>One window per box (M9): it shows the box's active tab; roll-up and lock are the box's.</summary>
     private void OpenWindow(Fence box)
     {
         var shown = FenceTabs.ActiveOf(_config, box.Id);
-        var window = new FenceWindow(shown with { RolledUp = box.RolledUp, Locked = box.Locked }, takeoverActive: _takeoverActive,
+        var window = new FenceWindow(shown with { RolledUp = box.RolledUp, Locked = box.Locked },
             lightTheme: _lightTheme, iconLoader: _iconLoader, rollupExpand: _config.Settings.RollupExpand)
         {
             // Spec §6: no animations when Windows' "Animation effects" are off; spec §4.7: none while gaming.
@@ -254,33 +250,26 @@ public sealed partial class FenceHost
         window.LockToggled += locked => SetFenceLocked(window, locked);
         window.DeleteRequested += () => DeleteFence(window);
         window.NewFenceRequested += CreateFence;
-        window.TakeoverToggled += SetTakeover;
         window.ExitRequested += () => ExitRequested?.Invoke();
-        window.OpenRequested += itemRef => OpenOrBrowse(window, itemRef);
-        window.OpenManyRequested += itemRefs =>
+        window.OpenRequested += key => OpenKey(window, key);
+        window.OpenManyRequested += keys =>
         {
-            foreach (var itemRef in itemRefs) OpenItem(itemRef, ownerHandle: window.Handle); // folders open in Explorer
-            SetPeek(false);
+            foreach (var key in keys) OpenKey(window, key);
         };
-        window.ItemMenuRequested += (itemRefs, screenX, screenY, fromKeyboard) => ShowItemMenu(window, itemRefs, screenX, screenY, fromKeyboard);
-        window.RecycleRequested += itemRefs => RecycleItems(window, itemRefs);
-        window.ItemRenameRequested += (itemRef, newName) => RenameItem(window, itemRef, newName);
-        window.BackRequested += () => BrowsePortal(window, back: true);
-        window.NewPortalRequested += () => CreatePortal(window);
+        window.ItemMenuRequested += (keys, screenX, screenY, fromKeyboard) => ShowItemMenu(window, keys, screenX, screenY, fromKeyboard);
+        window.RemoveRequested += keys => RemoveItems(window, keys);
+        window.PropertiesRequested += (key, focusName) => ShowProperties(window, key, focusName);
+        window.AddItemRequested += () => AddItem(window);
+        window.RefreshRequested += () => RefreshFence(window);
+        window.DrivesChanged += OnDrivesChanged;
         window.NewLibraryRequested += CreateLibraryFence;
-        window.RefreshLibraryRequested += () => ScanLibrary(full: true);
         window.StartupToggled += SetStartWithWindows;
         window.SettingsRequested += OpenSettings;
-        window.RulesRequested += () => OpenRulesFor(window.FenceId);
         window.LabelModeRequested += labels => SetFenceLabels(window, labels);
         window.SetShortcutArrows(_config.Settings.ShowShortcutArrows);
         window.SetStartupChecked(_config.Settings.StartWithWindows);
         window.SortRequested += sort => SortFence(window, sort);
-        window.OpenFolderRequested += () => { if (_portals.TryGetValue(window.FenceId, out var portal)) OpenItem(portal.Current, ownerHandle: window.Handle); };
-        window.DragRequested += itemRefs =>
-            ShellDragDrop.TryDrag(window.Handle, itemRefs, logFailure: failure => Log.Warning(failure, "could not start dragging {ItemRefs}", itemRefs),
-                copyOnly: window.IsLibrary); // a game dragged out of the library is copied, never moved (M12)
-        window.TakeoverPromptAnswered += AnswerTakeoverPrompt;
+        window.DragRequested += keys => DragItems(window, keys);
         window.RollUpToggled += () => ToggleRollUp(window);
         new WindowInteropHelper(window).EnsureHandle(); // HWND exists (styles, blur) before the first Show
         if (window.CornersUnavailable && !_cornersLogged)
@@ -291,77 +280,8 @@ public sealed partial class FenceHost
         RegisterDrops(window); // needs the HWND
         if (!DesktopHost.AttachToDesktop(window.Handle)) Log.Warning("fence {FenceId}: not attached to the desktop yet (no Progman)", box.Id);
         _windows[box.Id] = window;
-        // A Portal tab that gets a window of its own (detached, or its host deleted) lists its folder now (final review I1).
-        if (_portals.TryGetValue(shown.Id, out var shownPortal)) shownPortal.Refresh();
-    }
-
-    /// <summary>Full pass over the Desktop folders: at start and whenever watcher events were lost.</summary>
-    private void ReconcileDesktop()
-    {
-        var listing = DesktopItems.Enumerate();
-        var (reconciled, report) = FenceMembership.Reconcile(_config, listing.ItemRefs, listing.UnavailableFolders,
-            remembered: _rememberedPlacements, now: Now);
-        _config = reconciled;
-        _rememberedPlacements = [.. _rememberedPlacements.Except(report.UsedMemories)]; // each memory places one item once (M8a review)
-        if (listing.UnavailableFolders.Count > 0) Log.Warning("desktop folders not readable: {Folders}", listing.UnavailableFolders);
-        if (report.Suspicious) Log.Warning("kept fenced items from an unreadable or empty desktop listing until a later reconcile");
-        Log.Information("desktop reconciled: {AddedCount} added to the Inbox, {RemovedCount} removed", report.AddedToInbox.Count, report.Removed.Count);
-        RefreshWindows();
-        ScheduleSave();
-        FileNewItems(report.AddedToInbox);
-        OnDesktopShortcutsChanged([.. report.Removed, .. report.AddedToInbox]); // a game-mode flood, lost watcher events, start (M16)
-    }
-
-    private void StartDesktopWatcher()
-    {
-        // A folder that cannot be watched degrades to "its changes show after a restart" (hard rule 7).
-        _desktopWatcher = new DesktopWatcher((folder, failure) => Log.Error(failure, "cannot watch {Folder}; its changes show after a restart", folder));
-        var dispatcher = Dispatcher.CurrentDispatcher;
-        _desktopWatcher.Changed += change => dispatcher.BeginInvoke(() => OnDesktopChanged(change));
-        _desktopWatcher.Overflowed += () => dispatcher.BeginInvoke(() => OnDesktopWatcherTrouble(rearm: true));
-        _desktopWatcher.ReconcileNeeded += () => dispatcher.BeginInvoke(() => OnDesktopWatcherTrouble(rearm: false));
-    }
-
-    /// <summary>
-    /// Events were lost or the watcher stopped (re-arm: .NET disables it after a non-overflow error), or one event could
-    /// not be read (reconcile only). Bursts are gathered into one recovery (M8a review).
-    /// </summary>
-    private void OnDesktopWatcherTrouble(bool rearm)
-    {
-        if (_desktopWatcher is null) return; // shut down meanwhile
-        if (rearm && !_rearmWatcher)
-        {
-            _rearmWatcher = true;
-            // Measured when the failure arrives: the wait itself is not quiet time (Core WatcherBackoff, M8c review I3).
-            _watcherRearmDelay = WatcherBackoff.Next(_watcherRearmDelay, lastRearm: _lastWatcherRearm, failureAt: DateTime.UtcNow);
-            _watcherRecoveryTimer.Stop(); // a reconcile-only recovery already waiting now waits for the re-arm delay
-            _watcherRecoveryTimer.Interval = _watcherRearmDelay;
-            _watcherRecoveryTimer.Start();
-            return;
-        }
-        if (_watcherRecoveryTimer.IsEnabled) return;
-        _watcherRecoveryTimer.Interval = WatcherBackoff.First;
-        _watcherRecoveryTimer.Start();
-    }
-
-    private void RecoverDesktopWatcher()
-    {
-        _watcherRecoveryTimer.Stop();
-        if (_desktopWatcher is null) return;
-        if (_rearmWatcher)
-        {
-            _rearmWatcher = false;
-            _lastWatcherRearm = DateTime.UtcNow;
-            Log.Warning("desktop watcher lost events or stopped; re-armed after {Delay} and reconciling", _watcherRearmDelay);
-            _desktopWatcher.Dispose();
-            StartDesktopWatcher();
-        }
-        else
-        {
-            Log.Information("a desktop change could not be read; reconciling");
-        }
-        if (Current.ShellWorkDeferred) _reconcileDeferred = true; // after the game
-        else ReconcileDesktop();
+        // The library tab gets a window of its own (detached, or its host deleted): it lists its folder now (final review I1).
+        if (shown.IsLibrary) _libraryLister?.Refresh();
     }
 
     /// <summary>Also after an Explorer restart: Explorer brokers the shell's change notices and forgets them (M8c review I6).</summary>
@@ -380,7 +300,7 @@ public sealed partial class FenceHost
         _specialIconsTimer.Start();
     }
 
-    /// <summary>"Desktop icon settings" changed (reconcile), or the Recycle Bin turned full or empty (new icon) (M8c).</summary>
+    /// <summary>The Recycle Bin turned full or empty: a Recycle Bin item shows the new icon (M8c).</summary>
     private void RefreshSpecialIcons()
     {
         _specialIconsTimer.Stop();
@@ -389,165 +309,46 @@ public sealed partial class FenceHost
             _specialIconsDeferred = true; // games get every bit of the machine: after the game (M8c review)
             return;
         }
-        var shown = DesktopItems.SpecialIconRefs().ToHashSet(ItemRef.Comparer);
-        var fenced = _config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop).SelectMany(fence => fence.Items)
-            .Where(itemRef => itemRef.StartsWith("::", StringComparison.Ordinal)).ToHashSet(ItemRef.Comparer);
-        if (!shown.SetEquals(fenced))
-        {
-            Log.Information("desktop icon settings changed; reconciling");
-            ReconcileDesktop(); // game mode returned early above: no deferral needed here (M13c, dead branch removed)
-        }
         foreach (var window in _windows.Values) window.ReloadSpecialIcons();
         Log.Information("special icons refreshed");
     }
 
-    private void OnDesktopChanged(DesktopChange change)
-    {
-        if (Current.ShellWorkDeferred)
-        {
-            // Applied in order when the game is left; a flood (a big download unpacking) becomes one reconcile instead (M8a).
-            if (_deferredDesktopChanges.Count < MaxDeferredDesktopChanges) _deferredDesktopChanges.Add(change);
-            else _reconcileDeferred = true;
-            return;
-        }
-        var arrival = ArrivalOf(change); // before Apply: was the item fenced already?
-        (_config, _rememberedPlacements) = FenceMembership.Apply(_config, change, _rememberedPlacements, Now);
-        RefreshWindows();
-        ScheduleSave();
-        if (arrival is not null) FileNewItems([arrival]);
-        OnDesktopShortcutChange(change);
-    }
-
-    /// <summary>
-    /// The item a change brings that rules may file (M11): a created item no fence holds yet (an attribute change on an
-    /// existing item also arrives as "created", final review I1), or a download that just got its final name (I2).
-    /// </summary>
-    private string? ArrivalOf(DesktopChange change) => change switch
-    {
-        DesktopChange.Created created when !_config.Fences.Any(fence => fence.Items.Contains(created.ItemRef, ItemRef.Comparer)) => created.ItemRef,
-        DesktopChange.Renamed renamed when Rules.IsDownloadRename(renamed.OldRef) => renamed.NewRef,
-        _ => null,
-    };
-
-    /// <summary>
-    /// Rules auto-sort (M11, spec §3): new items that landed in the Inbox go to the fence of the first rule they match.
-    /// Their facts are read on the shell worker (a shortcut to an offline share can be slow); an item moved or deleted
-    /// meanwhile stays put. Membership only: no file is touched.
-    /// </summary>
-    private void FileNewItems(IReadOnlyList<string> itemRefs)
-    {
-        if (itemRefs.Count == 0 || !_config.Rules.Any(rule => rule.Enabled)) return;
-        _pendingArrivals.AddRange(itemRefs);
-        _filingTimer.Stop(); // a burst (an unpack, an installer) is one read, 1.5 s after the last arrival
-        _filingTimer.Start();
-    }
-
-    private void FilePendingArrivals()
-    {
-        _filingTimer.Stop();
-        var inbox = _config.Inbox.Items.ToHashSet(ItemRef.Comparer);
-        var arrivals = _pendingArrivals.Distinct(ItemRef.Comparer).Where(inbox.Contains).ToList(); // a safe-save memory or a drop placed it already
-        _pendingArrivals.Clear();
-        if (arrivals.Count == 0) return;
-        ReadFactsThen(arrivals, facts =>
-        {
-            var stillInInbox = _config.Inbox.Items.ToHashSet(ItemRef.Comparer);
-            var filed = Rules.File(_config, [.. facts.Where(fact => stillInInbox.Contains(fact.ItemRef))], DateTimeOffset.Now);
-            if (ReferenceEquals(filed, _config)) return;
-            var moved = _config.Inbox.Items.Except(filed.Inbox.Items, ItemRef.Comparer).ToList();
-            Log.Information("rules filed {Count} new item(s): {ItemRefs}", moved.Count, moved);
-            _config = filed;
-            RefreshWindows();
-            ScheduleSave();
-        });
-    }
-
-    /// <summary>Reads item facts on the shell worker, then continues on the UI thread.</summary>
-    private void ReadFactsThen(IReadOnlyList<string> itemRefs, Action<IReadOnlyList<ItemFacts>> then)
-    {
-        var dispatcher = Dispatcher.CurrentDispatcher;
-        _shellWorker.Run(() =>
-        {
-            var facts = ItemFactsReader.Read(itemRefs, logFailure: (itemRef, failure) => Log.Warning(failure, "rules: {ItemRef} could not be read", itemRef));
-            dispatcher.BeginInvoke(() => then(facts));
-        });
-    }
-
-    /// <summary>
-    /// Settings → "Apply rules now" (M11, spec §3): every desktop item, hand-placed ones too. "Before restore" is written
-    /// first, so tray → "Undo the last restore" puts everything back; nothing moves without it.
-    /// </summary>
-    private void ApplyRulesNow()
+    /// <summary>Every window shows its fence's items as they are now (the library lists itself, LibraryLister).</summary>
+    private void RefreshWindows()
     {
-        var itemRefs = _config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop).SelectMany(fence => fence.Items).ToList();
-        ReadFactsThen(itemRefs, facts =>
-        {
-            var now = DateTimeOffset.Now;
-            var moves = Rules.CountMoves(_config, facts, now);
-            if (moves == 0)
-            {
-                _settingsWindow?.ShowRulesResult("Nothing to move: every item is where the rules want it.");
-                return;
-            }
-            if (_snapshots.Save(Snapshots.Take(_config, name: $"Before applying rules ({now:d MMM HH:mm})", now: now), SnapshotStore.BeforeRestoreFileName) is null)
-            {
-                Log.Warning(_snapshots.LastFailure, "rules not applied: 'Before restore' could not be saved");
-                _settingsWindow?.ShowRulesResult("Nothing moved: NeoFences could not save 'Before restore' first (see the log).");
-                return;
-            }
-            _config = Rules.File(_config, facts, now);
-            Log.Information("rules applied: {Count} item(s) moved", moves);
-            SaveNow();
-            RefreshWindows();
-            RefreshSettings();
-            var message = $"{moves} item{(moves == 1 ? "" : "s")} moved. Tray → Restore snapshot → Undo the last restore puts them back.";
-            _settingsWindow?.ShowRulesResult(message);
-            _trayIcon?.ShowBalloon("Rules applied", message);
-        });
+        foreach (var window in _windows.Values) RefreshWindow(window);
     }
 
-    private void RefreshWindows()
+    private void RefreshWindow(FenceWindow window)
     {
-        var showPrompt = !_config.Settings.TakeoverPromptAnswered && !_takeoverActive;
-        foreach (var window in _windows.Values)
-        {
-            if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is not { } shown) continue;
-            if (!_portals.ContainsKey(shown.Id)) window.SetItems(shown.Items); // Portals refresh on their own (M4 review I1)
-            window.ShowTakeoverPrompt(showPrompt && shown.IsInbox);
-        }
+        if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is not { IsLibrary: false } shown) return;
+        window.SetItems([.. _items.Of(shown.Id).Select(item => new ShownItem(item.Id, item.Target, item.OwnName, item.Icon, item.Note, StateOf(item.Target)))]);
     }
 
-    private void OpenItem(string itemRef, nint ownerHandle)
+    /// <summary>Opens a path NeoFences knows (a library game, the logs or data folder).</summary>
+    private static void OpenItem(string target, nint ownerHandle)
     {
         // Off the UI thread: ShellExecute can block on a network timeout or a UAC prompt, freezing every fence (M2b review I6);
         // on an STA thread, as shell handlers and Windows' error dialog expect (M3a review).
         // Each open on its own thread: one waiting on an offline share (and its error dialog) holds up nothing else (M8c review I4).
         ShellWorker.RunAlone(() =>
         {
-            if (!ShellItems.TryOpen(itemRef, ownerHandle)) Log.Warning("could not open {ItemRef}", itemRef);
+            if (!ShellItems.TryOpen(target, ownerHandle)) Log.Warning("could not open {Target}", target);
         }, name: "NeoFences open");
     }
 
-    /// <summary>Makes the fence accept drops (M3b): fence items and Desktop files move membership; other files go to Windows.</summary>
+    /// <summary>Makes the fence accept drops (M3b; M18: drops create or move items, never a file operation).</summary>
     private void RegisterDrops(FenceWindow window)
     {
         try
         {
             _dropRegistrations[window.BoxId] = ShellDragDrop.RegisterFence(window.Handle, new FenceDropHandlers(
                 HitTest: window.HitTest,
-                MoveItems: (itemRefs, insertAt) =>
-                {
-                    _config = FenceMembership.MoveItems(_config, itemRefs, window.FenceId, insertAt);
-                    RefreshWindows();
-                    ScheduleSave();
-                },
-                ExpectArrivals: (itemRefs, insertAt) =>
-                    _rememberedPlacements = FenceMembership.ExpectArrivals(_rememberedPlacements, itemRefs, window.FenceId, insertAt, Now),
-                Recycle: itemRefs => RecycleItems(window, itemRefs),
+                AcceptsDrops: () => !window.IsLibrary, // the library shows NeoFences' own shortcuts only (M12)
+                ItemsDropped: (keys, insertAt, duplicate) => OnItemsDropped(window, keys, insertAt, duplicate),
+                TargetsDropped: (targets, insertAt) => OnTargetsDropped(window, targets, insertAt),
                 ShowFeedback: window.ShowDropFeedback,
-                LogFailure: failure => Log.Warning(failure, "drop on fence {FenceId} failed", window.FenceId),
-                AcceptsDrops: () => !window.IsLibrary), // the library shows NeoFences' own shortcuts only (M12)
-                portalFolder: () => _portals.TryGetValue(window.FenceId, out var portal) ? portal.Current : null);
+                LogFailure: failure => Log.Warning(failure, "drop on fence {FenceId} failed", window.FenceId)));
         }
         catch (Exception failure) when (failure is not OutOfMemoryException)
         {
@@ -555,86 +356,6 @@ public sealed partial class FenceHost
         }
     }
 
-    private void ShowItemMenu(FenceWindow window, IReadOnlyList<string> itemRefs, int screenX, int screenY, bool fromKeyboard)
-    {
-        // Shift+right-click is the extended menu; Shift+F10 is just the keyboard's normal menu (M3a review).
-        var extended = !fromKeyboard && System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);
-        if (window.IsLibrary)
-        {
-            ShowLibraryItemMenu(window, itemRefs, screenX, screenY, extended);
-            return;
-        }
-        var choice = ShellItemMenu.Show(window.Handle, itemRefs, screenX, screenY, extended,
-            logFailure: failure => Log.Warning(failure, "item menu or its command failed for {ItemRefs}", itemRefs));
-        switch (choice)
-        {
-            case ItemMenuChoice.Rename:
-                window.BeginItemRename(itemRefs[0]); // the right-clicked item (it comes first)
-                break;
-            case ItemMenuChoice.Delete:
-                RecycleItems(window, itemRefs); // always the Recycle Bin, even with Shift held (hard rule 1)
-                break;
-        }
-    }
-
-    /// <summary>
-    /// Windows moves them to the Recycle Bin (with its own dialogs) on the shell worker, so a long recycle never freezes
-    /// the fences (M3a review); the watcher then removes them from the fence.
-    /// </summary>
-    private void RecycleItems(FenceWindow window, IReadOnlyList<string> itemRefs)
-    {
-        if (window.IsLibrary)
-        {
-            HideGames(itemRefs); // Delete in the library hides the game; its shortcut is NeoFences' own (M12)
-            return;
-        }
-        Log.Information("recycling {Count} item(s)", itemRefs.Count);
-        var ownerHandle = window.Handle;
-        var dispatcher = Dispatcher.CurrentDispatcher;
-        _shellWorker.Run(() =>
-        {
-            var (started, refused, missing) = ShellFileOps.TryRecycle(LiveOwner(ownerHandle), itemRefs);
-            if (!started) Log.Warning("recycle did not run or was cancelled: {ItemRefs}", itemRefs);
-            if (missing.Count > 0) Log.Information("skipped {Count} item(s) that no longer exist: {ItemRefs}", missing.Count, missing);
-            if (refused.Count > 0) dispatcher.BeginInvoke(() => ExplainRefusedRecycle(window, refused));
-        });
-    }
-
-    /// <summary>No owner when the fence was deleted while the operation waited: Windows' dialogs then stand alone (M8c review).</summary>
-    private static nint LiveOwner(nint ownerHandle) => FenceWindowChrome.IsLiveWindow(ownerHandle) ? ownerHandle : 0;
-
-    private static void ExplainRefusedRecycle(FenceWindow window, IReadOnlyList<string> refused)
-    {
-        Log.Information("not deleting {Count} item(s) on drives without a Recycle Bin", refused.Count);
-        System.Windows.MessageBox.Show(window,
-            (refused.Count == 1 ? $"\"{Path.GetFileName(refused[0])}\" is" : $"{refused.Count} items are") +
-            " on a drive without a Recycle Bin (a USB stick or network drive), so NeoFences won't delete it." +
-            " Deleting there would be permanent; if you really mean it, delete it in Explorer.",
-            "NeoFences", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
-    }
-
-    /// <summary>Windows renames the file (on the shell worker: its conflict dialogs); the watcher keeps it in its fence and position.</summary>
-    private void RenameItem(FenceWindow window, string itemRef, string newName)
-    {
-        var ownerHandle = window.Handle;
-        _shellWorker.Run(() =>
-        {
-            if (!ShellFileOps.TryRename(LiveOwner(ownerHandle), itemRef, newName)) Log.Warning("rename did not run or was cancelled: {ItemRef}", itemRef);
-        });
-    }
-
-    private void AnswerTakeoverPrompt(bool hideIcons)
-    {
-        Log.Information("first-run question answered: hide desktop icons {HideIcons}", hideIcons);
-        if (hideIcons) SetTakeover(true);
-        else
-        {
-            _config = _config with { Settings = _config.Settings with { TakeoverPromptAnswered = true } };
-            RefreshWindows();
-            SaveNow();
-        }
-    }
-
     private void ApplyLayout()
     {
         if (_monitors.Count == 0)
@@ -727,7 +448,6 @@ public sealed partial class FenceHost
         _config = FenceEdits.Rename(_config, window.FenceId, title);
         window.SetTitle(_config.Fences.First(fence => fence.Id == window.FenceId).Title);
         RefreshTabs(window);
-        RefreshPortal(window); // a Portal browsing a subfolder shows its breadcrumb again
         ScheduleSave();
     }
 
@@ -745,11 +465,23 @@ public sealed partial class FenceHost
         ScheduleSave();
     }
 
-    /// <summary>Removes the fence; its items go to the Inbox. Files are never touched (hard rule 1).</summary>
+    /// <summary>
+    /// Removes the shown tab's fence and its items (asked first when it has any). Their targets are never touched (hard
+    /// rule 1). The config is saved before the items: a crash in between leaves only an unused list (ADR-041).
+    /// </summary>
     private void DeleteFence(FenceWindow window)
     {
-        _config = FenceMembership.DeleteFence(_config, window.FenceId); // the shown tab; the rest of its box stays (M9)
-        SyncBoxes(); // closes the window when its box is gone; a deleted Portal's watcher ends (the folder is never touched)
+        var fence = _config.Fences.First(candidate => candidate.Id == window.FenceId);
+        var count = _items.Of(fence.Id).Count;
+        if (count > 0 && MessageBox.Show(window,
+                $"Delete \"{fence.Title}\" and its {count} item{(count == 1 ? "" : "s")}?\n\nYour files, folders and apps are not touched.",
+                "NeoFences", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
+        Log.Information("fence {FenceId} deleted with {Count} item(s)", fence.Id, count);
+        _config = FenceEdits.DeleteFence(_config, fence.Id); // the shown tab; the rest of its box stays (M9)
+        _items = ItemEdits.RemoveFence(_items, fence.Id);
+        SaveNow();
+        SyncBoxes(); // closes the window when its box is gone
+        UpdateWatching();
     }
 
     private void OnThemeChanged()
@@ -771,18 +503,6 @@ public sealed partial class FenceHost
         ScheduleSave();
     }
 
-    /// <summary>"New Portal fence…": Windows' folder dialog, then a fence that mirrors the folder (M4).</summary>
-    private void CreatePortal(FenceWindow owner)
-    {
-        var folder = FolderPicker.TryPick(owner.Handle, "Choose the folder for the new Portal fence",
-            logFailure: failure => Log.Warning(failure, "folder dialog failed"));
-        if (folder is null) return;
-        var title = Path.GetFileName(folder.TrimEnd('\\')) is { Length: > 0 } name ? name : folder;
-        (_config, _) = FenceMembership.CreatePortal(_config, title: title, folderPath: folder);
-        Log.Information("Portal fence created for {Folder}", folder);
-        SyncBoxes();
-    }
-
     /// <summary>
     /// Brings the windows in line with the boxes after any change to them (M9): a window per box, showing the box's
     /// active tab, with its tab strip, lock and roll-up. A box that changed hands (its host left) keeps its window.
@@ -807,7 +527,7 @@ public sealed partial class FenceHost
             window.Close();
         }
         foreach (var box in boxes.Where(box => !_windows.ContainsKey(box.Id))) OpenWindow(box);
-        EnsurePortals();
+        EnsureLibraryLister();
         foreach (var box in boxes)
         {
             var window = _windows[box.Id];
@@ -815,7 +535,8 @@ public sealed partial class FenceHost
             if (window.FenceId != active.Id)
             {
                 window.ShowTab(active);
-                if (_portals.TryGetValue(active.Id, out var portal)) portal.Refresh();
+                if (active.IsLibrary) _libraryLister?.Refresh();
+                else CheckFence(active.Id); // a fence becoming visible is checked again (spec §4)
             }
             window.SetTabs(FenceTabs.TabsOf(_config, box.Id), active.Id);
             window.SetLocked(box.Locked);
@@ -827,33 +548,6 @@ public sealed partial class FenceHost
         ScheduleSave();
     }
 
-    /// <summary>A watcher per Portal fence, shown or not (a hidden Portal tab keeps watching, M9); gone ones end.</summary>
-    private void EnsurePortals()
-    {
-        var portalFences = _config.Fences.Where(fence => fence.Source is { Kind: FenceSourceKind.Portal, Path: not null } or { Kind: FenceSourceKind.Library }).ToList();
-        foreach (var goneId in _portals.Keys.Where(fenceId => portalFences.All(fence => fence.Id != fenceId)).ToList())
-        {
-            _portals[goneId].Dispose(); // the folder itself is never touched
-            _portals.Remove(goneId);
-        }
-        foreach (var fence in portalFences.Where(fence => !_portals.ContainsKey(fence.Id)))
-        {
-            var fenceId = fence.Id;
-            var root = fence.Source.Kind == FenceSourceKind.Library ? AppPaths.LibraryDirectory : fence.Source.Path!;
-            if (fence.Source.Kind == FenceSourceKind.Library) TryCreateFolder(root);
-            _portals[fenceId] = new PortalState(root, noticeOwner: _messages.Handle, show: items => ShowPortalTab(fenceId, items),
-                logFailure: failure => Log.Warning(failure, "cannot watch Portal folder of {FenceId}", fenceId));
-            if (_gameMode) _portals[fenceId].SetPaused(true);
-        }
-        UpdateLibrary(); // M12: the library scans while its fence exists
-    }
-
-    /// <summary>A Portal's listing goes to the window showing it; a hidden Portal tab re-lists when shown.</summary>
-    private void ShowPortalTab(string fenceId, IReadOnlyList<ItemInfo>? items)
-    {
-        if (_windows.Values.FirstOrDefault(window => window.FenceId == fenceId) is { } window) ShowPortal(window, items);
-    }
-
     private void RefreshTabs(FenceWindow window)
     {
         window.SetTabs(FenceTabs.TabsOf(_config, window.BoxId), window.FenceId);
@@ -867,8 +561,9 @@ public sealed partial class FenceHost
         _config = FenceTabs.SetActive(_config, fenceId);
         window.ShowTab(tab);
         RefreshTabs(window);
-        if (_portals.TryGetValue(fenceId, out var portal)) portal.Refresh();
-        RefreshWindows();
+        if (tab.IsLibrary) _libraryLister?.Refresh();
+        else CheckFence(fenceId); // a fence becoming visible is checked again (spec §4)
+        RefreshWindow(window);
         ScheduleSave();
     }
 
@@ -929,74 +624,6 @@ public sealed partial class FenceHost
         SyncBoxes();
     }
 
-    /// <summary>Asks a Portal to re-list its folder (in the background; ShowPortal follows).</summary>
-    private void RefreshPortal(FenceWindow window)
-    {
-        if (_portals.TryGetValue(window.FenceId, out var portal)) portal.Refresh();
-    }
-
-    /// <summary>Shows a Portal's listing (null: the folder cannot be read) in its sort order (M4).</summary>
-    private void ShowPortal(FenceWindow window, IReadOnlyList<ItemInfo>? items)
-    {
-        if (!_portals.TryGetValue(window.FenceId, out var portal)) return;
-        if (_config.Fences.FirstOrDefault(candidate => candidate.Id == window.FenceId) is not { } fence) return;
-        window.SetPortalLocation(portal.Breadcrumb(fence.Title), portal.CanGoBack);
-        window.SetSortChecked(fence.Sort);
-        if (fence.Source.Kind == FenceSourceKind.Library)
-        {
-            window.ShowPortalMessage(null);
-            window.SetLibraryArt(LibraryArt());
-            window.SetItems(items is null ? [] : LibraryOrder(items)); // A–Z by game, only NeoFences' own shortcuts (M12)
-            return;
-        }
-        window.ShowPortalMessage(items is null ? $"This folder is not available right now:\n{portal.Current}" : null);
-        window.SetItems(items is null ? [] : ItemSorting.Order(items, fence.Sort));
-    }
-
-    /// <summary>Double-click / Enter: inside a Portal a folder is browsed in place (Ctrl opens it in Explorer, user choice 2026-10-03).</summary>
-    private void OpenOrBrowse(FenceWindow window, string itemRef)
-    {
-        var inExplorer = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control);
-        if (_portals.TryGetValue(window.FenceId, out var portal) && !inExplorer && portal.IsListedFolder(itemRef))
-        {
-            portal.Browse(itemRef); // re-lists in the background
-            return;
-        }
-        OpenItem(itemRef, ownerHandle: window.Handle);
-        SetPeek(false); // like Fences: Peek ends once something is opened from it
-    }
-
-    private void BrowsePortal(FenceWindow window, bool back)
-    {
-        if (!back || !_portals.TryGetValue(window.FenceId, out var portal) || !portal.CanGoBack) return;
-        portal.Back(); // re-lists in the background
-    }
-
-    /// <summary>"Sort by": a Portal keeps the order live; a desktop fence is sorted once (M4).</summary>
-    private void SortFence(FenceWindow window, FenceSort sort)
-    {
-        var fence = _config.Fences.First(candidate => candidate.Id == window.FenceId);
-        if (_portals.ContainsKey(fence.Id))
-        {
-            _config = FenceEdits.SetSort(_config, fence.Id, sort);
-            RefreshPortal(window);
-        }
-        else
-        {
-            try
-            {
-                _config = FenceEdits.SetItemOrder(_config, fence.Id, ItemSorting.Order(FolderItems.Describe(fence.Items), sort));
-            }
-            catch (ArgumentException mismatch)
-            {
-                Log.Warning(mismatch, "sort of fence {FenceId} refused: the sorted list did not match its items", fence.Id); // never crash (M4 review I3)
-                return;
-            }
-            RefreshWindows();
-        }
-        ScheduleSave();
-    }
-
     private void ApplyStartup() =>
         StartupRegistration.Apply(_config.Settings.StartWithWindows, Environment.ProcessPath ?? "", log: message => Log.Information("{Message}", message));
 
@@ -1011,39 +638,36 @@ public sealed partial class FenceHost
 
     private void CreateFence()
     {
-        (_config, _) = FenceMembership.CreateFence(_config, "New fence");
+        (_config, _) = FenceEdits.CreateFence(_config, "New fence");
         SyncBoxes();
     }
 
-    private void SetTakeover(bool active)
+    /// <summary>Settings → "Hide desktop icons while NeoFences runs" (M18): Windows' own setting, restored on every exit (hard rule 2).</summary>
+    private void SetHideDesktopIcons(bool hide)
     {
         SetQuickHidden(false); // an explicit icons choice ends quick-hide first, so the two never disagree
-        _takeoverActive = active;
-        // Any explicit choice (banner or menu) answers the first-run question.
-        _config = _config with { Settings = _config.Settings with { Takeover = active, TakeoverPromptAnswered = true } };
+        _config = _config with { Settings = _config.Settings with { HideDesktopIcons = hide } };
         SetIconsHidden(Current.IconsHidden);
-        foreach (var window in _windows.Values) window.SetTakeoverChecked(active);
         RefreshSettings();
-        RefreshWindows();
-        SaveNow(); // not debounced: the saved setting must match the takeover-active marker if we are killed next
+        SaveNow(); // not debounced: the saved setting must match the icons-hidden marker if we are killed next
     }
 
     /// <returns>True when Windows confirmed the new state.</returns>
     private bool SetIconsHidden(bool hidden)
     {
         // The watchdog must know whenever icons may be hidden: mark before hiding, unmark only after a confirmed show.
-        if (hidden) TryMarker(() => _watchdog.SetTakeoverActive(true), what: "takeover-active marker");
+        if (hidden) TryMarker(() => _watchdog.SetIconsHiddenMarker(true), what: "icons-hidden marker");
         var applied = DesktopIcons.TrySetHidden(hidden);
-        if (applied && !hidden) TryMarker(() => _watchdog.SetTakeoverActive(false), what: "takeover-active marker");
+        if (applied && !hidden) TryMarker(() => _watchdog.SetIconsHiddenMarker(false), what: "icons-hidden marker");
         if (applied) Log.Information("desktop icons hidden: {Hidden}", hidden);
         else Log.Warning("could not set desktop icons hidden: {Hidden}", hidden);
         return applied;
     }
 
-    /// <summary>Icons as they should be: hidden while Takeover is on, shown (and unmarked) otherwise.</summary>
+    /// <summary>Icons as they should be: hidden while the setting (or quick-hide) wants it, shown (and unmarked) otherwise.</summary>
     private bool EnsureIconState() =>
         Current.IconsHidden ? SetIconsHidden(true)
-        : !_watchdog.IsTakeoverActiveMarked || SetIconsHidden(false);
+        : !_watchdog.IsIconsHiddenMarked || SetIconsHidden(false);
 
     private static void TryMarker(Action writeMarker, string what)
     {
@@ -1167,7 +791,7 @@ public sealed partial class FenceHost
         }
         var window = new SettingsWindow();
         window.StartWithWindowsChanged += SetStartWithWindows;
-        window.TakeoverChanged += SetTakeover;
+        window.HideDesktopIconsChanged += SetHideDesktopIcons;
         window.PeekHotkeyChosen += text =>
         {
             var (saved, message) = SetPeekHotkey(text);
@@ -1187,14 +811,6 @@ public sealed partial class FenceHost
             RefreshSettings();
         };
         window.DeleteSnapshotRequested += DeleteSnapshot;
-        window.RulesChanged += rules =>
-        {
-            _config = _config with { Rules = rules };
-            Log.Information("rules changed: {Count} rule(s)", rules.Count);
-            SaveNow();
-            RefreshSettings();
-        };
-        window.ApplyRulesRequested += ApplyRulesNow;
         WireLibrarySettings(window);
         window.OpenSnapshotsRequested += () =>
         {
@@ -1257,7 +873,7 @@ public sealed partial class FenceHost
 
     private void RefreshSettings() => _settingsWindow?.Show(new SettingsView(
         StartWithWindows: _config.Settings.StartWithWindows,
-        Takeover: _takeoverActive,
+        HideDesktopIcons: _config.Settings.HideDesktopIcons,
         PeekHotkey: PeekHotkeyDisplay,
         PeekHotkeyActive: _peekHotkeyProblem is null,
         RollupExpand: _config.Settings.RollupExpand,
@@ -1268,20 +884,10 @@ public sealed partial class FenceHost
         DefaultLabels: _config.Settings.DefaultLabels,
         ShowShortcutArrows: _config.Settings.ShowShortcutArrows,
         Snapshots: ListSnapshots(),
-        Rules: _config.Rules,
-        RuleLines: [.. _config.Rules.Select(rule => Rules.Describe(rule, _config))],
-        RuleFences: [.. _config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop).Select(fence => new RuleFence(fence.Id, fence.Title))],
         Library: LibrarySettingsView(),
         Appearance: AppearanceView(),
         Updates: UpdatesView()));
 
-    /// <summary>Fence menu → "Rules for this fence…" (M11): Settings at Rules, a new rule for that fence.</summary>
-    private void OpenRulesFor(string fenceId)
-    {
-        OpenSettings();
-        _settingsWindow?.BeginNewRule(fenceId);
-    }
-
     /// <summary>The Peek hotkey as a person reads it ("Ctrl+Shift+=", not "Ctrl+Shift+OemPlus").</summary>
     private string PeekHotkeyDisplay =>
         Hotkey.TryParse(_config.Settings.PeekHotkey, out var hotkey) ? hotkey.DisplayTextWith(LayoutKeyCap) : _config.Settings.PeekHotkey;
@@ -1292,11 +898,11 @@ public sealed partial class FenceHost
             ? KeyboardLayout.CharacterOf(System.Windows.Input.KeyInterop.VirtualKeyFromKey(wpfKey))
             : null;
 
-    /// <summary>Saves the arrangement now, named by date and time (M10); renamed in Settings if wanted.</summary>
+    /// <summary>Saves the arrangement and its items now, named by date and time (M10); renamed in Settings if wanted.</summary>
     private void TakeSnapshot()
     {
         var now = DateTimeOffset.Now;
-        var snapshot = Snapshots.Take(_config, name: $"Snapshot {now:d MMM HH:mm}", now: now);
+        var snapshot = Snapshots.Take(_config, _items, name: $"Snapshot {now:d MMM HH:mm}", now: now);
         if (_snapshots.Save(snapshot) is { } path)
         {
             Log.Information("snapshot saved: {Path}", path);
@@ -1312,8 +918,8 @@ public sealed partial class FenceHost
     }
 
     /// <summary>
-    /// Puts a snapshot's arrangement back (M10, spec §3): only with a complete desktop listing, and only after "Before
-    /// restore" was written, so the restore itself can be undone. One config change, saved at once.
+    /// Puts a snapshot's arrangement and items back (M10, spec §3; M18): only after "Before restore" was written, so the
+    /// restore itself can be undone. One change, saved at once.
     /// </summary>
     private void RestoreSnapshot(string path)
     {
@@ -1323,22 +929,15 @@ public sealed partial class FenceHost
             SnapshotFailure("Snapshot not restored", "The snapshot file could not be read.");
             return;
         }
-        var listing = DesktopItems.Enumerate();
-        if (listing.UnavailableFolders.Count > 0)
-        {
-            Log.Warning("snapshot not restored: desktop folders not readable {Folders}", listing.UnavailableFolders);
-            SnapshotFailure("Snapshot not restored", "A Desktop folder cannot be read right now. Try again in a moment.");
-            return;
-        }
         var now = DateTimeOffset.Now;
-        if (_snapshots.Save(Snapshots.Take(_config, name: $"Before restore ({now:d MMM HH:mm})", now: now), SnapshotStore.BeforeRestoreFileName) is null)
+        if (_snapshots.Save(Snapshots.Take(_config, _items, name: $"Before restore ({now:d MMM HH:mm})", now: now), SnapshotStore.BeforeRestoreFileName) is null)
         {
             Log.Warning(_snapshots.LastFailure, "snapshot not restored: 'Before restore' could not be saved");
             SnapshotFailure("Snapshot not restored", "NeoFences could not save 'Before restore' first (see the log).");
             return;
         }
         Log.Information("restoring snapshot {Name} from {Path}", snapshot.Name, path);
-        _config = Snapshots.Restore(_config, snapshot, listing.ItemRefs);
+        (_config, _items) = Snapshots.Restore(_config, snapshot);
         SaveNow();
         SyncBoxes();
         // Windows that kept their fence still show its old title, icon size and labels (final review I1).
@@ -1346,8 +945,9 @@ public sealed partial class FenceHost
         {
             if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is not { } shown) continue;
             window.Refresh(shown);
-            RefreshPortal(window); // the Portal breadcrumb replaces the plain title again
+            window.SetTitle(shown.Title);
         }
+        CheckAllTargets(); // the restored items' targets may have changed since
         _settingsWindow?.ShowSnapshotNotice($"Restored \"{snapshot.Name}\".", failed: false); // replaces an earlier failure line (final review M1)
         RefreshSettings();
     }
@@ -1371,7 +971,7 @@ public sealed partial class FenceHost
         _settingsWindow?.ShowSnapshotNotice($"{title}: {reason}", failed: true);
     }
 
-    /// <summary>A snapshot file goes to the Recycle Bin, never deleted for good (hard rule 1).</summary>
+    /// <summary>A snapshot file (NeoFences' own) goes to the Recycle Bin, never deleted for good.</summary>
     private void DeleteSnapshot(string path)
     {
         var dispatcher = Dispatcher.CurrentDispatcher;
@@ -1379,7 +979,7 @@ public sealed partial class FenceHost
         var owner = _settingsWindow is { } settings ? new WindowInteropHelper(settings).Handle : 0;
         _shellWorker.Run(() =>
         {
-            var (started, refused, missing) = ShellFileOps.TryRecycle(LiveOwner(owner), [path]);
+            var (started, refused, missing) = ShellFileOps.TryRecycle(FenceWindowChrome.IsLiveWindow(owner) ? owner : 0, [path]);
             if (!started || refused.Count > 0)
             {
                 Log.Warning("snapshot {Path} could not be moved to the Recycle Bin", path);
@@ -1491,14 +1091,14 @@ public sealed partial class FenceHost
             StopUpdateDownload(); // M17: no network while a game is in front
         }
         UpdateMouseHook();
-        foreach (var portal in _portals.Values) portal.SetPaused(gameMode);
+        _libraryLister?.SetPaused(gameMode);
         if (!gameMode) ApplyDeferredShellWork();
         UpdatePeekHotkey();
         _trayIcon?.SetTooltip(TrayTooltip());
         RefreshSettings();
     }
 
-    /// <summary>The game was left: Desktop changes made meanwhile apply in order (or one reconcile if events were lost).</summary>
+    /// <summary>The game was left: the library scan, special icons, target checks and renames that waited (spec §4.7).</summary>
     private void ApplyDeferredShellWork()
     {
         if (_libraryDeferred)
@@ -1511,26 +1111,7 @@ public sealed partial class FenceHost
             _specialIconsDeferred = false;
             ScheduleSpecialIconRefresh();
         }
-        if (_reconcileDeferred)
-        {
-            _reconcileDeferred = false;
-            _deferredDesktopChanges.Clear();
-            ReconcileDesktop();
-            return;
-        }
-        if (_deferredDesktopChanges.Count == 0) return;
-        Log.Information("applying {Count} desktop change(s) from game mode", _deferredDesktopChanges.Count);
-        var arrivals = new List<string>();
-        foreach (var change in _deferredDesktopChanges)
-        {
-            if (ArrivalOf(change) is { } arrival) arrivals.Add(arrival);
-            (_config, _rememberedPlacements) = FenceMembership.Apply(_config, change, _rememberedPlacements, Now);
-            OnDesktopShortcutChange(change);
-        }
-        _deferredDesktopChanges.Clear();
-        RefreshWindows();
-        ScheduleSave();
-        FileNewItems(arrivals);
+        ApplyDeferredWatching();
     }
 
     /// <summary>Pause (tray): the desktop goes back to Windows — fences hidden, icons shown, the hook gone — until resumed.</summary>
@@ -1651,9 +1232,9 @@ public sealed partial class FenceHost
         if (hidden == _quickHidden) return;
         if (hidden) SetPeek(false);
         // Icons the user had hidden through Explorer stay theirs: quick-hide neither hides nor later shows them (M8a).
-        // Hidden now without the takeover-active marker: the user hid them in Explorer, so they stay the user's. With the marker
+        // Hidden now without the icons-hidden marker: the user hid them in Explorer, so they stay the user's. With the marker
         // set, NeoFences hid them (an earlier show failed) and the end of this quick-hide retries the show (M8a review I1).
-        if (hidden && !_takeoverActive) _iconsHiddenByUser = DesktopIcons.TryIsHidden() == true && !_watchdog.IsTakeoverActiveMarked;
+        if (hidden && !_config.Settings.HideDesktopIcons) _iconsHiddenByUser = DesktopIcons.TryIsHidden() == true && !_watchdog.IsIconsHiddenMarked;
         var iconsWereHidden = Current.IconsHidden;
         _quickHidden = hidden;
         foreach (var window in _windows.Values)
@@ -1694,7 +1275,7 @@ public sealed partial class FenceHost
         SetQuickHidden(false);
         var pixels = DrawFenceOverlay.Between(_drawStart, (endX, endY));
         var monitor = FencePlacement.ContainingMonitor(pixels, _monitors);
-        (_config, var fence) = FenceMembership.CreateFence(_config, "New fence");
+        (_config, var fence) = FenceEdits.CreateFence(_config, "New fence");
         // Too small a drag still makes a usable fence: the layout clamps it to the minimum size.
         _config = LayoutEngine.WithFenceRect(_config, fingerprint: fingerprint, fenceId: fence.Id, rect: FencePlacement.FromPixels(pixels, monitor));
         Log.Information("fence drawn on the desktop at {Pixels}", pixels);
@@ -1743,6 +1324,7 @@ public sealed partial class FenceHost
             _peekEscapeHotkey = new GlobalHotkey(_messages.Handle, PeekEscapeHotkeyId);
             if (!_peekEscapeHotkey.TryRegister(new Hotkey(Ctrl: false, Alt: false, Shift: false, Win: false, Key: "Escape"), virtualKey: 0x1B))
                 Log.Warning("Esc is taken by another app; Peek ends with its hotkey or a click outside");
+            foreach (var window in _windows.Values) CheckFence(window.FenceId); // the shown fences are checked again (spec §4)
         }
         Log.Information("peek: {Peeking}", peeking);
     }
@@ -1753,6 +1335,7 @@ public sealed partial class FenceHost
         var rolledUp = !_config.Fences.First(fence => fence.Id == window.BoxId).RolledUp; // the box's (M9)
         _config = FenceEdits.SetRolledUp(_config, window.BoxId, rolledUp);
         window.SetRolledUp(rolledUp);
+        if (!rolledUp) CheckFence(window.FenceId); // unrolled: its items are checked again (spec §4)
         ScheduleSave();
     }
 
@@ -1779,6 +1362,7 @@ public sealed partial class FenceHost
         _saveTimer.Start();
     }
 
+    /// <summary>Both files, the config first (ADR-041): a fence deleted just before a power cut leaves only an unused item list.</summary>
     private void SaveNow()
     {
         _saveTimer.Stop();
@@ -1791,6 +1375,18 @@ public sealed partial class FenceHost
         {
             Log.Error(failure, "config save failed");
         }
+        try
+        {
+            var toSave = _pruneItemLists ? ItemEdits.Prune(_items, _config.Fences.Select(fence => fence.Id).ToHashSet(StringComparer.Ordinal)) : _items;
+            if (!ReferenceEquals(toSave, _items)) Log.Information("dropped item lists of {Count} fence(s) that no longer exist", _items.Fences.Count - toSave.Fences.Count);
+            _items = toSave;
+            if (!_itemStore.Save(_items)) Log.Warning("items not saved: items.json is read-only this session");
+            else if (_itemStore.LastBackupFailure is { } backupFailure) Log.Warning(backupFailure, "items saved, but the daily backups could not be written or pruned");
+        }
+        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
+        {
+            Log.Error(failure, "items save failed");
+        }
     }
 
     private static void TryCreateFolder(string folder)
@@ -1801,7 +1397,7 @@ public sealed partial class FenceHost
         }
         catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
         {
-            Log.Warning(failure, "cannot create {Folder}", folder); // the fence then shows "not available" (hard rule 7)
+            Log.Warning(failure, "cannot create {Folder}", folder); // the fence then shows nothing (hard rule 7)
         }
     }
 }
diff --git a/src/NeoFences.App/FenceItemView.cs b/src/NeoFences.App/FenceItemView.cs
index 1b5c98f..dc3252a 100644
--- a/src/NeoFences.App/FenceItemView.cs
+++ b/src/NeoFences.App/FenceItemView.cs
@@ -1,69 +1,129 @@
 using System.ComponentModel;
 using System.IO;
+using System.Runtime.CompilerServices;
 using System.Windows.Media;
+using NeoFences.Core.Items;
 
 namespace NeoFences.App;
 
-/// <summary>One desktop item as a fence shows it. Label and icon start as placeholders and fill in from <see cref="IconLoader"/>.</summary>
-public sealed class FenceItemView(string itemRef) : INotifyPropertyChanged
+/// <summary>
+/// What a fence shows of one item (M18): a virtual item (key = its id), or a Game Library shortcut (key = its path).
+/// The host builds these from its data; the window shows them in place (<see cref="FenceWindow.SetItems"/>).
+/// </summary>
+public sealed record ShownItem(string Key, string Target, string? Name = null, ItemIcon? Icon = null, string? Note = null,
+    TargetState State = TargetState.Ok);
+
+/// <summary>One item as a fence shows it. Label and icon start as placeholders and fill in from <see cref="IconLoader"/>.</summary>
+public sealed class FenceItemView : INotifyPropertyChanged
 {
-    public string ItemRef { get; } = itemRef;
+    public FenceItemView(ShownItem shown)
+    {
+        Key = shown.Key;
+        Target = shown.Target;
+        Update(shown);
+    }
+
+    public string Key { get; }
+
+    public string Target
+    {
+        get;
+        private set { field = value; Changed(); Changed(nameof(IsShortcut)); }
+    } = "";
+
+    /// <summary>The item's own name: it always wins over Windows' name for the target.</summary>
+    public string? OwnName { get; private set; }
+
+    /// <summary>The item's own icon, or null for the target's.</summary>
+    public ItemIcon? OwnIcon { get; private set; }
+
+    public string? Note { get; private set; }
 
     /// <summary>A shortcut (.lnk, .url, .pif): gets the arrow overlay when Settings shows shortcut arrows (M8b).</summary>
-    public bool IsShortcut { get; } = Path.GetExtension(itemRef).ToLowerInvariant() is ".lnk" or ".url" or ".pif";
+    public bool IsShortcut => Path.GetExtension(Target).ToLowerInvariant() is ".lnk" or ".url" or ".pif";
 
     public string Label
     {
         get;
-        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label))); }
-    } = itemRef.StartsWith("::", StringComparison.Ordinal) ? "" : Path.GetFileNameWithoutExtension(itemRef);
-
-    /// <summary>The icon size last requested (UI thread): a slower, older load of another size never wins (final review I4).</summary>
-    public int WantedSizePx { get; set; }
+        set { field = value; Changed(); Changed(nameof(ToolTipText)); }
+    } = "";
 
-    public ImageSource? Icon
+    public TargetState State
     {
         get;
-        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon))); }
+        private set { field = value; Changed(); Changed(nameof(ToolTipText)); }
     }
 
-    /// <summary>The label is being renamed in place (F2 or the item menu's Rename).</summary>
-    public bool IsEditing
+    /// <summary>The note, or what is wrong with the target (spec §4); the name otherwise.</summary>
+    public string ToolTipText => State switch
     {
-        get;
-        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditing))); }
+        TargetState.Missing => $"Missing: {Target}",
+        TargetState.Unavailable => TargetChecks.IsNetworkPath(Target) ? $"Network location not reachable: {Target}"
+            : $"Drive {TargetChecks.RootOf(Target)?.TrimEnd('\\') ?? "?"} is not connected: {Target}",
+        _ => Note is { Length: > 0 } note ? $"{Label}\n{note}" : Label,
+    };
+
+    /// <summary>
+    /// Takes the host's latest data. True when the icon must load again: another target or icon, or a name that is no
+    /// longer the item's own (Windows' name for the target comes with the icon).
+    /// </summary>
+    public bool Update(ShownItem shown)
+    {
+        var reload = !string.Equals(Target, shown.Target, StringComparison.Ordinal) || OwnIcon != shown.Icon
+                     || (OwnName is not null && shown.Name is null);
+        Target = shown.Target;
+        OwnIcon = shown.Icon;
+        OwnName = string.IsNullOrWhiteSpace(shown.Name) ? null : shown.Name;
+        Note = shown.Note;
+        State = shown.State;
+        if (OwnName is not null) Label = OwnName;
+        else if (reload || Label.Length == 0) Label = PlaceholderName(Target);
+        else Changed(nameof(ToolTipText));
+        return reload;
     }
 
-    /// <summary>Text in the rename box.</summary>
-    public string EditName
+    /// <summary>Until Windows' display name arrives: the file name, a website's host; nothing for special items.</summary>
+    private static string PlaceholderName(string target) => ItemKinds.Of(target) switch
+    {
+        ItemKind.Website => ItemKinds.WebsiteName(target),
+        ItemKind.Special => "",
+        _ => Path.GetFileNameWithoutExtension(target.TrimEnd('\\')) is { Length: > 0 } name ? name : target,
+    };
+
+    /// <summary>Counts icon requests (UI thread): a slower, older load (another size or target) never wins (final review I4).</summary>
+    public int IconRequest { get; set; }
+
+    public ImageSource? Icon
     {
         get;
-        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditName))); }
-    } = "";
+        set { field = value; Changed(); }
+    }
 
     /// <summary>A Game Library tile (M12): a 2:3 tile with a poster, a logo or the icon centred.</summary>
     public bool IsTile
     {
         get;
-        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTile))); }
+        set { field = value; Changed(); }
     }
 
     /// <summary>The tile's poster or logo, once loaded.</summary>
     public ImageSource? Art
     {
         get;
-        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Art))); }
+        set { field = value; Changed(); }
     }
 
     /// <summary>"Poster" (fills the tile), "Logo" (centred) or "None" (the icon).</summary>
     public string ArtKind
     {
         get;
-        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ArtKind))); }
+        set { field = value; Changed(); }
     } = "None";
 
     /// <summary>The art file requested last (UI thread): a slower, older load never wins.</summary>
     public string? ArtPath { get; set; }
 
     public event PropertyChangedEventHandler? PropertyChanged;
+
+    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
 }
diff --git a/src/NeoFences.App/FenceWindow.xaml b/src/NeoFences.App/FenceWindow.xaml
index 81d790f..c2d1be4 100644
--- a/src/NeoFences.App/FenceWindow.xaml
+++ b/src/NeoFences.App/FenceWindow.xaml
@@ -14,32 +14,12 @@
     <Window.Resources>
         <sys:Double x:Key="IconSize" xmlns:sys="clr-namespace:System;assembly=System.Runtime">48</sys:Double>
         <sys:Double x:Key="ItemWidth" xmlns:sys="clr-namespace:System;assembly=System.Runtime">76</sys:Double>
-        <sys:Double x:Key="EditItemWidth" xmlns:sys="clr-namespace:System;assembly=System.Runtime">76</sys:Double>
         <!-- Icon-only fences (M8b): labels collapse, the name pops under the hovered or selected icon instead. -->
         <Visibility x:Key="LabelVisibility">Visible</Visibility>
         <sys:Boolean x:Key="ItemToolTips" xmlns:sys="clr-namespace:System;assembly=System.Runtime">True</sys:Boolean>
         <Visibility x:Key="ShortcutArrowVisibility">Collapsed</Visibility>
         <sys:Double x:Key="TileWidth" xmlns:sys="clr-namespace:System;assembly=System.Runtime">72</sys:Double>
         <sys:Double x:Key="TileHeight" xmlns:sys="clr-namespace:System;assembly=System.Runtime">108</sys:Double>
-        <Style x:Key="BannerButton" TargetType="Button">
-            <Setter Property="Foreground" Value="{DynamicResource FenceText}" />
-            <Setter Property="Padding" Value="10,3" />
-            <Setter Property="Margin" Value="0,0,6,0" />
-            <Setter Property="Template">
-                <Setter.Value>
-                    <ControlTemplate TargetType="Button">
-                        <Border x:Name="Chrome" Background="{DynamicResource FenceHover}" CornerRadius="4" Padding="{TemplateBinding Padding}">
-                            <ContentPresenter />
-                        </Border>
-                        <ControlTemplate.Triggers>
-                            <Trigger Property="IsMouseOver" Value="True">
-                                <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceSelected}" />
-                            </Trigger>
-                        </ControlTemplate.Triggers>
-                    </ControlTemplate>
-                </Setter.Value>
-            </Setter>
-        </Style>
         <!-- Thin scrollbar (spec §6): no arrows, a rounded thumb that brightens under the mouse. -->
         <Style TargetType="ScrollBar">
             <Setter Property="Width" Value="6" />
@@ -80,10 +60,6 @@
             <!-- Title strip style (M14): the coloured title bar, rounded with the window's top corners. -->
             <Border x:Name="TitleStripFill" CornerRadius="7,7,0,0" Background="Transparent" IsHitTestVisible="False" />
             <DockPanel x:Name="TitleBar" Background="Transparent">
-                <!-- Portal browsing (M4): back to the parent folder. Hit-testable inside the caption area. -->
-                <Button x:Name="BackButton" DockPanel.Dock="Left" Visibility="Collapsed" Content="‹" ToolTip="Back (Backspace)"
-                        Margin="6,3,0,3" Padding="8,0" FontSize="16" Style="{StaticResource BannerButton}"
-                        WindowChrome.IsHitTestVisibleInChrome="True" />
                 <!-- Fence tabs (M9): one header per tab, built in code; the space right of them stays caption (move, roll-up). -->
                 <UniformGrid x:Name="TabStrip" DockPanel.Dock="Left" Rows="1" Visibility="Collapsed" HorizontalAlignment="Left" Margin="6,3,0,0" />
                 <TextBlock x:Name="TitleText" Foreground="{DynamicResource FenceTitleText}" FontWeight="SemiBold" Margin="12,0"
@@ -97,25 +73,13 @@
                      VerticalContentAlignment="Center" WindowChrome.IsHitTestVisibleInChrome="True"
                      Foreground="{DynamicResource FenceText}" Background="{DynamicResource FenceHover}"
                      BorderBrush="{DynamicResource FenceBorder}" CaretBrush="{DynamicResource FenceText}" />
-            <!-- First run (Inbox only): ask once whether NeoFences should take over the desktop icons. -->
-            <Border x:Name="TakeoverPrompt" Grid.Row="1" Visibility="Collapsed" Background="{DynamicResource FenceHover}"
-                    Margin="8,0,8,6" Padding="10,8" CornerRadius="6">
-                <StackPanel>
-                    <TextBlock Foreground="{DynamicResource FenceText}" TextWrapping="Wrap"
-                               Text="Hide the desktop icons and keep them only in fences?" />
-                    <TextBlock Foreground="{DynamicResource FenceSubtleText}" FontSize="11" TextWrapping="Wrap" Margin="0,2,0,6"
-                               Text="Your files stay where they are. Turn it off any time from the right-click menu." />
-                    <StackPanel Orientation="Horizontal">
-                        <Button x:Name="PromptHideButton" Content="Hide them" Style="{StaticResource BannerButton}" />
-                        <Button x:Name="PromptLaterButton" Content="Not now" Style="{StaticResource BannerButton}" />
-                    </StackPanel>
-                </StackPanel>
-            </Border>
             <Border x:Name="Body" Grid.Row="2" BorderBrush="{DynamicResource FenceDivider}" BorderThickness="0,1,0,0" Background="#01000000">
                 <Border.ContextMenu>
                     <ContextMenu x:Name="BodyContextMenu">
+                        <MenuItem x:Name="AddItemItem" Header="Add item…" />
+                        <MenuItem x:Name="RefreshItem" Header="Refresh" />
+                        <Separator />
                         <MenuItem x:Name="NewFenceItem" Header="New fence" />
-                        <MenuItem x:Name="NewPortalItem" Header="New Portal fence…" />
                         <MenuItem x:Name="NewLibraryItem" Header="New Game Library fence" />
                         <MenuItem x:Name="RenameItem" Header="Rename fence" />
                         <MenuItem x:Name="IconSizeItem" Header="Icon size" />
@@ -126,13 +90,9 @@
                         <MenuItem x:Name="SortItem" Header="Sort by" />
                         <MenuItem x:Name="TabColorItem" Header="Colour" />
                         <MenuItem x:Name="DetachTabItem" Header="Detach tab" Visibility="Collapsed" />
-                        <MenuItem x:Name="OpenFolderItem" Header="Open folder in Explorer" Visibility="Collapsed" />
-                        <MenuItem x:Name="RefreshLibraryItem" Header="Refresh library" Visibility="Collapsed" />
-                        <MenuItem x:Name="RulesItem" Header="Rules for this fence…" />
                         <MenuItem x:Name="LockItem" Header="Lock position" IsCheckable="True" />
-                        <MenuItem x:Name="DeleteItem" Header="Delete fence (items go to the Inbox)" />
+                        <MenuItem x:Name="DeleteItem" Header="Delete fence (your files are not touched)" />
                         <Separator />
-                        <MenuItem x:Name="TakeoverItem" Header="Hide desktop icons" IsCheckable="True" />
                         <MenuItem x:Name="StartupItem" Header="Start with Windows" IsCheckable="True" />
                         <MenuItem x:Name="SettingsItem" Header="Settings…" />
                         <Separator />
@@ -150,7 +110,7 @@
                         </ListBox.ItemsPanel>
                         <ListBox.ItemContainerStyle>
                             <Style TargetType="ListBoxItem">
-                                <Setter Property="ToolTip" Value="{Binding Label}" />
+                                <Setter Property="ToolTip" Value="{Binding ToolTipText}" />
                                 <Setter Property="ToolTipService.IsEnabled" Value="{DynamicResource ItemToolTips}" />
                                 <EventSetter Event="MouseEnter" Handler="OnItemMouseEnter" />
                                 <EventSetter Event="MouseLeave" Handler="OnItemMouseLeave" />
@@ -183,7 +143,14 @@
                             <DataTemplate>
                                 <StackPanel x:Name="Cell" Width="{DynamicResource ItemWidth}">
                                     <Grid x:Name="IconGrid" HorizontalAlignment="Center">
-                                        <Image Source="{Binding Icon}" Width="{DynamicResource IconSize}" Height="{DynamicResource IconSize}" />
+                                        <Image x:Name="ItemIcon" Source="{Binding Icon}" Width="{DynamicResource IconSize}" Height="{DynamicResource IconSize}" />
+                                        <!-- Missing target (M18 spec §4): a small warning badge on the dimmed icon. -->
+                                        <Border x:Name="MissingBadge" Visibility="Collapsed" HorizontalAlignment="Right" VerticalAlignment="Bottom"
+                                                Width="16" Height="16" CornerRadius="8" Background="#FFE8A33D" BorderBrush="#FF3A2A10" BorderThickness="1"
+                                                IsHitTestVisible="False">
+                                            <TextBlock Text="!" Foreground="#FF3A2A10" FontWeight="Bold" FontSize="11"
+                                                       HorizontalAlignment="Center" VerticalAlignment="Center" />
+                                        </Border>
                                         <!-- Shortcut arrow (M8b, Settings switch): a white tile with a blue curved arrow, like Windows draws. -->
                                         <Viewbox x:Name="ShortcutArrow" Visibility="Collapsed" HorizontalAlignment="Left" VerticalAlignment="Bottom"
                                                  Width="{DynamicResource ArrowSize}" Height="{DynamicResource ArrowSize}" IsHitTestVisible="False">
@@ -202,17 +169,10 @@
                                             <Image x:Name="TilePoster" Source="{Binding Art}" Stretch="UniformToFill" Visibility="Collapsed" />
                                         </Grid>
                                     </Border>
-                                    <Grid Margin="0,4,0,0">
-                                        <TextBlock x:Name="Label" Text="{Binding Label}" Foreground="{DynamicResource FenceText}" FontSize="12"
-                                                   Visibility="{DynamicResource LabelVisibility}"
-                                                   TextAlignment="Center" TextWrapping="Wrap" TextTrimming="CharacterEllipsis"
-                                                   MaxHeight="32" Effect="{DynamicResource LabelShadow}" />
-                                        <!-- In-place rename (M3a): Enter renames through Windows, Esc cancels. -->
-                                        <TextBox x:Name="LabelBox" Visibility="Collapsed" FontSize="12" TextAlignment="Center" TextWrapping="Wrap"
-                                                 MaxHeight="48" Text="{Binding EditName, UpdateSourceTrigger=PropertyChanged}"
-                                                 KeyDown="OnLabelBoxKeyDown" LostKeyboardFocus="OnLabelBoxLostFocus"
-                                                 IsVisibleChanged="OnLabelBoxVisibleChanged" />
-                                    </Grid>
+                                    <TextBlock x:Name="Label" Margin="0,4,0,0" Text="{Binding Label}" Foreground="{DynamicResource FenceText}" FontSize="12"
+                                               Visibility="{DynamicResource LabelVisibility}"
+                                               TextAlignment="Center" TextWrapping="Wrap" TextTrimming="CharacterEllipsis"
+                                               MaxHeight="32" Effect="{DynamicResource LabelShadow}" />
                                 </StackPanel>
                                 <DataTemplate.Triggers>
                                     <DataTrigger Binding="{Binding IsTile}" Value="True">
@@ -230,24 +190,28 @@
                                     <DataTrigger Binding="{Binding IsShortcut}" Value="True">
                                         <Setter TargetName="ShortcutArrow" Property="Visibility" Value="{DynamicResource ShortcutArrowVisibility}" />
                                     </DataTrigger>
-                                    <DataTrigger Binding="{Binding IsEditing}" Value="True">
-                                        <Setter TargetName="LabelBox" Property="Visibility" Value="Visible" />
-                                        <Setter TargetName="Label" Property="Visibility" Value="Hidden" />
-                                        <!-- Icons-only cells are narrow: the rename box gets a labelled cell's width (M8b review). -->
-                                        <Setter TargetName="Cell" Property="Width" Value="{DynamicResource EditItemWidth}" />
+                                    <DataTrigger Binding="{Binding State}" Value="Missing">
+                                        <Setter TargetName="ItemIcon" Property="Opacity" Value="0.45" />
+                                        <Setter TargetName="Label" Property="Opacity" Value="0.6" />
+                                        <Setter TargetName="MissingBadge" Property="Visibility" Value="Visible" />
+                                    </DataTrigger>
+                                    <DataTrigger Binding="{Binding State}" Value="Unavailable">
+                                        <Setter TargetName="ItemIcon" Property="Opacity" Value="0.45" />
+                                        <Setter TargetName="Label" Property="Opacity" Value="0.6" />
                                     </DataTrigger>
                                 </DataTemplate.Triggers>
                             </DataTemplate>
                         </ListBox.ItemTemplate>
                     </ListBox>
-                    <!-- Portal whose folder cannot be read (missing, offline, denied). -->
-                    <TextBlock x:Name="PortalMessage" Visibility="Collapsed" Margin="12" TextWrapping="Wrap"
-                               Foreground="{DynamicResource FenceSubtleText}" IsHitTestVisible="False" />
+                    <!-- An empty fence says how to fill it (M18 spec §5). -->
+                    <TextBlock x:Name="EmptyHint" Visibility="Collapsed" Margin="12" TextWrapping="Wrap" TextAlignment="Center"
+                               VerticalAlignment="Center" Foreground="{DynamicResource FenceSubtleText}" IsHitTestVisible="False"
+                               Text="Drop files, folders or links here — or right-click → Add item…" />
                     <!-- Rubber-band selection (M3b): drawn while dragging on empty space. -->
                     <Canvas IsHitTestVisible="False" ClipToBounds="True">
                         <Rectangle x:Name="SelectionBand" Visibility="Collapsed" Fill="{DynamicResource FenceHover}"
                                    Stroke="{DynamicResource FenceSubtleText}" StrokeThickness="1" RadiusX="2" RadiusY="2" />
-                        <!-- Drag-drop feedback (M3b review I2): where a drop lands, or which folder takes it. -->
+                        <!-- Drag-drop feedback (M3b review I2): where a drop lands. -->
                         <Rectangle x:Name="InsertCaret" Visibility="Collapsed" Width="2" Fill="{DynamicResource FenceText}" RadiusX="1" RadiusY="1" />
                         <!-- Icon-only fences (M8b): the hovered or selected item's name, under its icon, over the neighbours. -->
                         <Border x:Name="HoverLabel" Visibility="Collapsed" CornerRadius="4" Padding="6,2"
@@ -255,8 +219,6 @@
                             <TextBlock x:Name="HoverLabelText" Text="{Binding Label}" Foreground="{DynamicResource FenceText}" FontSize="12"
                                        TextAlignment="Center" TextWrapping="Wrap" MaxWidth="180" />
                         </Border>
-                        <Rectangle x:Name="DropHighlight" Visibility="Collapsed" Fill="{DynamicResource FenceSelected}"
-                                   Stroke="{DynamicResource FenceText}" StrokeThickness="1" RadiusX="4" RadiusY="4" />
                     </Canvas>
                 </Grid>
             </Border>
diff --git a/src/NeoFences.App/FenceWindow.xaml.cs b/src/NeoFences.App/FenceWindow.xaml.cs
index 87b3e40..d563d86 100644
--- a/src/NeoFences.App/FenceWindow.xaml.cs
+++ b/src/NeoFences.App/FenceWindow.xaml.cs
@@ -10,6 +10,7 @@ using System.Windows.Shapes;
 using System.Windows.Shell;
 using NeoFences.Core.Appearance;
 using NeoFences.Core.Config;
+using NeoFences.Core.Items;
 using NeoFences.Core.Layouts;
 using NeoFences.Core.Model;
 using NeoFences.Shell;
@@ -27,6 +28,8 @@ public partial class FenceWindow : Window
     private const int WmNcLeftButtonDown = 0x00A1;
     private const int WmNcLeftButtonDoubleClick = 0x00A3;
     private const int WmNcRightButtonUp = 0x00A5;
+    private const int WmDeviceChange = 0x0219;
+    private const int DbtDeviceArrival = 0x8000, DbtDeviceRemoveComplete = 0x8004;
     private const int HitTestCaption = 2;
     private const double CornerRadiusDips = 8;
     private double _captionHeightDips = 30; // follows the title font (M14: 26 / 30 / 34 / 38)
@@ -41,7 +44,6 @@ public partial class FenceWindow : Window
     private int _iconSizeDips;
     private bool _renaming;
     private string _title = "";
-    private bool _isPortal; // the shown tab is a Portal (changes with the tab, M9)
     private bool _isLibrary; // the shown tab is the Game Library (M12): tiles, its own menu items
     private IReadOnlyDictionary<string, (string Path, bool IsPoster)> _libraryArt = new Dictionary<string, (string, bool)>();
     private DragTracker? _drag;
@@ -119,36 +121,32 @@ public partial class FenceWindow : Window
     public event Action<FenceWindow, PixelRect>? MovedByUser;
 
     public event Action? NewFenceRequested;
-    public event Action<bool>? TakeoverToggled;
     public event Action? ExitRequested;
+    /// <summary>Double-click or Enter on one item (its key).</summary>
     public event Action<string>? OpenRequested;
-    /// <summary>Enter with several items selected: open each (a Portal does not browse into one of several folders, M8d).</summary>
+    /// <summary>Enter with several items selected: open each.</summary>
     public event Action<IReadOnlyList<string>>? OpenManyRequested;
-    /// <summary>The first-run question was answered: true = hide the desktop icons.</summary>
-    public event Action<bool>? TakeoverPromptAnswered;
     public event Action<string>? RenameRequested;
     public event Action<int>? IconSizeRequested;
     public event Action<bool>? LockToggled;
     public event Action? DeleteRequested;
-    /// <summary>Right-click (or the menu key) on items: show Windows' item menu for these refs at this screen point (px).</summary>
-    /// <summary>Right-click or menu key on items: refs (the clicked item first), screen point, opened from the keyboard.</summary>
+    /// <summary>Right-click or menu key on items: keys (the clicked item first), screen point, opened from the keyboard.</summary>
     public event Action<IReadOnlyList<string>, int, int, bool>? ItemMenuRequested;
-    /// <summary>Del: send these items to the Recycle Bin.</summary>
-    public event Action<IReadOnlyList<string>>? RecycleRequested;
-    /// <summary>An in-place rename was confirmed: item ref, new name as typed.</summary>
-    public event Action<string, string>? ItemRenameRequested;
+    /// <summary>Del: take these items out of the fence (their targets are never touched, ADR-040).</summary>
+    public event Action<IReadOnlyList<string>>? RemoveRequested;
+    /// <summary>F2 (true: the name selected) or Alt+Enter on one item: its Properties.</summary>
+    public event Action<string, bool>? PropertiesRequested;
     /// <summary>The user started dragging these items out of the fence (M3b).</summary>
     public event Action<IReadOnlyList<string>>? DragRequested;
-    /// <summary>Portal (M4): back to the parent folder (Back button, Backspace).</summary>
-    public event Action? BackRequested;
-    public event Action? NewPortalRequested;
     public event Action<FenceSort>? SortRequested;
-    public event Action? OpenFolderRequested;
-    /// <summary>Fence menu → "New Game Library fence" / "Refresh library" (M12).</summary>
+    /// <summary>Fence menu → "Add item…" (M18).</summary>
+    public event Action? AddItemRequested;
+    /// <summary>Fence menu → "Refresh": the items' targets are checked again and their icons reloaded (the library rescans).</summary>
+    public event Action? RefreshRequested;
+    /// <summary>Fence menu → "New Game Library fence" (M12).</summary>
     public event Action? NewLibraryRequested;
-    public event Action? RefreshLibraryRequested;
-    /// <summary>Fence menu → "Rules for this fence…" (M11): Settings opens at Rules with a new rule for this fence.</summary>
-    public event Action? RulesRequested;
+    /// <summary>A drive arrived or was removed (Windows tells top-level windows): missing and unavailable items are checked again.</summary>
+    public event Action? DrivesChanged;
     /// <summary>The "Start with Windows" toggle changed (ADR-019).</summary>
     public event Action<bool>? StartupToggled;
     /// <summary>"Settings…" in the fence menu (M6b).</summary>
@@ -167,7 +165,7 @@ public partial class FenceWindow : Window
     private (uint At, Point Where)? _lastCaptionPress; // a title double-click recognised by NeoFences itself (M8b); message time
     private uint? _recognisedDoubleClickAt;
 
-    public FenceWindow(Fence fence, bool takeoverActive, bool lightTheme, IconLoader iconLoader, RollupExpand rollupExpand)
+    public FenceWindow(Fence fence, bool lightTheme, IconLoader iconLoader, RollupExpand rollupExpand)
     {
         _expansion = new RollUpExpansion(rollupExpand);
         FenceId = fence.Id;
@@ -197,13 +195,9 @@ public partial class FenceWindow : Window
         DetachTabItem.Click += (_, _) => DetachTabRequested?.Invoke();
         TitleBar.SizeChanged += (_, _) => UpdateTabStripWidth();
         PreviewKeyDown += OnTabKeys;
-        NewPortalItem.Click += (_, _) => NewPortalRequested?.Invoke();
-        OpenFolderItem.Click += (_, _) => OpenFolderRequested?.Invoke();
         NewLibraryItem.Click += (_, _) => NewLibraryRequested?.Invoke();
-        RefreshLibraryItem.Click += (_, _) => RefreshLibraryRequested?.Invoke();
-        RulesItem.Click += (_, _) => RulesRequested?.Invoke();
-        BackButton.Click += (_, _) => BackRequested?.Invoke();
-        TakeoverItem.IsChecked = takeoverActive;
+        AddItemItem.Click += (_, _) => AddItemRequested?.Invoke();
+        RefreshItem.Click += (_, _) => RefreshRequested?.Invoke();
         foreach (var size in ConfigNormalizer.IconSizes)
         {
             var sizeItem = new MenuItem { Header = IconSizeNames.GetValueOrDefault(size, $"{size} px"), Tag = size, IsCheckable = true };
@@ -214,7 +208,6 @@ public partial class FenceWindow : Window
         RenameItem.Click += (_, _) => BeginRename();
         LockItem.Click += (_, _) => LockToggled?.Invoke(LockItem.IsChecked);
         DeleteItem.Click += (_, _) => DeleteRequested?.Invoke();
-        TakeoverItem.Click += (_, _) => TakeoverToggled?.Invoke(TakeoverItem.IsChecked);
         ExitItem.Click += (_, _) => ExitRequested?.Invoke();
         StartupItem.Click += (_, _) => StartupToggled?.Invoke(StartupItem.IsChecked);
         SettingsItem.Click += (_, _) => SettingsRequested?.Invoke();
@@ -237,8 +230,6 @@ public partial class FenceWindow : Window
         TitleBar.MouseRightButtonUp += (_, click) => { click.Handled = true; OpenFenceMenu(); };
         TitleBox.KeyDown += OnTitleBoxKeyDown;
         TitleBox.LostKeyboardFocus += (_, _) => EndRename(commit: true);
-        PromptHideButton.Click += (_, _) => TakeoverPromptAnswered?.Invoke(true);
-        PromptLaterButton.Click += (_, _) => TakeoverPromptAnswered?.Invoke(false);
 #if DEBUG
         // Checklist B11: a hung fence UI thread must not freeze the desktop or taskbar (owner input-queue attachment, ADR-011).
         var freezeItem = new MenuItem { Header = "Debug: freeze this UI thread for 10 s (B11)" };
@@ -278,30 +269,19 @@ public partial class FenceWindow : Window
         };
     }
 
-    public void SetTakeoverChecked(bool active) => TakeoverItem.IsChecked = active;
-
     public void SetStartupChecked(bool startWithWindows) => StartupItem.IsChecked = startWithWindows;
 
-    /// <summary>Everything the window shows of one fence: title, Portal bits, menu state, icon size, labels.</summary>
+    /// <summary>Everything the window shows of one fence: title, menu state, icon size, labels.</summary>
     private void ApplyFence(Fence fence)
     {
         _title = fence.Title;
         TitleText.Text = fence.Title;
-        _isPortal = fence.Source.Kind == FenceSourceKind.Portal;
-        _isLibrary = fence.Source.Kind == FenceSourceKind.Library;
-        OpenFolderItem.Visibility = _isPortal ? Visibility.Visible : Visibility.Collapsed;
-        RulesItem.Visibility = fence.Source.Kind == FenceSourceKind.Desktop ? Visibility.Visible : Visibility.Collapsed; // rules fill desktop fences only (M11)
-        RefreshLibraryItem.Visibility = _isLibrary ? Visibility.Visible : Visibility.Collapsed;
-        SortItem.Visibility = _isLibrary ? Visibility.Collapsed : Visibility.Visible; // the library is always A–Z
-        DeleteItem.Header = _isLibrary ? "Delete fence (your games are not touched)"
-            : _isPortal ? "Delete fence (the folder is not touched)" : "Delete fence (items go to the Inbox)";
-        DeleteItem.Visibility = fence.IsInbox ? Visibility.Collapsed : Visibility.Visible;
-        // Desktop fences sort once (dragging keeps working); Portals keep the chosen order live, so it is checked.
-        foreach (var sortItem in SortItem.Items.OfType<MenuItem>())
-        {
-            sortItem.IsCheckable = _isPortal;
-            sortItem.IsChecked = _isPortal && (FenceSort)sortItem.Tag == fence.Sort;
-        }
+        _isLibrary = fence.IsLibrary;
+        // The library is NeoFences' own A–Z list of games: no items to add or sort (M12).
+        AddItemItem.Visibility = _isLibrary ? Visibility.Collapsed : Visibility.Visible;
+        SortItem.Visibility = _isLibrary ? Visibility.Collapsed : Visibility.Visible;
+        DeleteItem.Header = _isLibrary ? "Delete fence (your games are not touched)" : "Delete fence (your files are not touched)";
+        UpdateEmptyHint();
         _labelMode = fence.Labels;
         SetIconSize(fence.IconSize);
         SetLabelMode(fence.Labels);
@@ -320,7 +300,7 @@ public partial class FenceWindow : Window
     private void ApplyArt(FenceItemView view)
     {
         view.IsTile = _isLibrary;
-        if (!_isLibrary || !_libraryArt.TryGetValue(view.ItemRef, out var art))
+        if (!_isLibrary || !_libraryArt.TryGetValue(view.Key, out var art))
         {
             view.ArtPath = null;
             view.Art = null;
@@ -365,8 +345,8 @@ public partial class FenceWindow : Window
     public void Refresh(Fence fence) => ApplyFence(fence);
 
     /// <summary>
-    /// Another tab of this box is shown (M9): its look and menus; its items follow from the host (or its Portal). An open
-    /// rename of the previous tab is cancelled.
+    /// Another tab of this box is shown (M9): its look and menus; its items follow from the host. An open rename of the
+    /// previous tab is cancelled.
     /// </summary>
     public void ShowTab(Fence fence)
     {
@@ -375,8 +355,7 @@ public partial class FenceWindow : Window
         FenceId = fence.Id;
         ApplyFence(fence);
         _items.Clear();
-        ShowPortalMessage(null);
-        BackButton.Visibility = Visibility.Collapsed;
+        UpdateEmptyHint();
     }
 
     /// <summary>The box's tabs in order and the shown one (M9). One tab: the plain title (with its colour bar, if any).</summary>
@@ -597,87 +576,73 @@ public partial class FenceWindow : Window
         return _tabs[Math.Clamp((int)(point.X / (TabStrip.ActualWidth / _tabs.Count)), 0, _tabs.Count - 1)].Id;
     }
 
-    public void ShowTakeoverPrompt(bool visible) => TakeoverPrompt.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
-
     public void SetTitle(string title)
     {
         _title = title;
         TitleText.Text = title;
     }
 
-    /// <summary>Portal (M4): what the title shows while browsing ("Downloads › Mods"), and whether Back is offered.</summary>
-    public void SetPortalLocation(string breadcrumb, bool canGoBack)
-    {
-        TitleText.Text = breadcrumb;
-        BackButton.Visibility = canGoBack ? Visibility.Visible : Visibility.Collapsed;
-    }
-
-    /// <summary>Portal (M4): a message instead of items (folder missing or unreadable); null hides it.</summary>
-    public void ShowPortalMessage(string? message)
-    {
-        PortalMessage.Text = message ?? "";
-        PortalMessage.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
-    }
-
-    /// <summary>Portal (M4): the sort shown as checked.</summary>
-    public void SetSortChecked(FenceSort sort)
-    {
-        foreach (var sortItem in SortItem.Items.OfType<MenuItem>()) sortItem.IsChecked = _isPortal && (FenceSort)sortItem.Tag == sort;
-    }
-
     /// <summary>
     /// Shows exactly these items in this order, by moving, adding and removing views in place: items already shown keep
-    /// their name, icon, selection and the scroll position (M2b review carry-over; duplicate refs are tolerated).
+    /// their icon, selection and the scroll position (M2b review carry-over); a changed target, name or icon reloads only
+    /// that item's icon and name.
     /// </summary>
-    public void SetItems(IReadOnlyList<string> itemRefs)
+    public void SetItems(IReadOnlyList<ShownItem> shownItems)
     {
-        if (_items.Select(view => view.ItemRef).SequenceEqual(itemRefs, StringComparer.Ordinal)) return;
-        var wanted = new Dictionary<string, int>(StringComparer.Ordinal);
-        foreach (var itemRef in itemRefs) wanted[itemRef] = wanted.GetValueOrDefault(itemRef) + 1;
+        var wanted = shownItems.Select(shown => shown.Key).ToHashSet(StringComparer.Ordinal);
         for (var index = _items.Count - 1; index >= 0; index--)
         {
-            var itemRef = _items[index].ItemRef;
-            if (wanted.GetValueOrDefault(itemRef) > 0) wanted[itemRef]--;
-            else RemoveItemAt(index);
+            if (!wanted.Contains(_items[index].Key)) _items.RemoveAt(index);
         }
         // ponytail: O(n²) moves in the worst case (a full reorder of hundreds of items); a keyed diff when that shows up.
         var iconSizePx = IconSizePx;
-        for (var index = 0; index < itemRefs.Count; index++)
+        for (var index = 0; index < shownItems.Count; index++)
         {
-            if (index < _items.Count && _items[index].ItemRef == itemRefs[index]) continue;
+            var shown = shownItems[index];
             var found = -1;
-            for (var later = index + 1; later < _items.Count && found < 0; later++)
+            for (var at = index; at < _items.Count && found < 0; at++)
             {
-                if (_items[later].ItemRef == itemRefs[index]) found = later;
+                if (_items[at].Key == shown.Key) found = at;
             }
             if (found >= 0)
             {
-                CancelRename(_items[found]);
-                _items.Move(found, index);
+                if (found != index) _items.Move(found, index);
+                if (_items[index].Update(shown) && IsLoaded) _iconLoader.Request(_items[index], iconSizePx);
                 continue;
             }
-            var view = new FenceItemView(itemRefs[index]);
+            var view = new FenceItemView(shown);
             ApplyArt(view);
             if (IsLoaded) _iconLoader.Request(view, iconSizePx); // before that, Loaded requests them at the right DPI (M2b review)
             _items.Insert(index, view);
         }
+        UpdateEmptyHint();
         // Cells may have shifted under a shown name without a scroll or selection event (final review I3).
         Dispatcher.BeginInvoke(UpdateHoverLabel, System.Windows.Threading.DispatcherPriority.Loaded);
     }
 
-    /// <summary>
-    /// A rename whose own item is moved or removed would lose its box (and commit half-typed text): cancel only that one,
-    /// right before. An item that merely shifts keeps its box (M3a review I3; final review M1).
-    /// </summary>
-    private static void CancelRename(FenceItemView view)
+    /// <summary>An empty fence (not the library) says how to fill it (spec §5).</summary>
+    private void UpdateEmptyHint() => EmptyHint.Visibility = !_isLibrary && _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
+
+    /// <summary>These items selected and scrolled into view: a drop of what the fence already holds shows where it is (spec §2).</summary>
+    public void SelectItems(IReadOnlyCollection<string> keys)
     {
-        if (view.IsEditing) view.IsEditing = false;
+        ItemList.SelectedItems.Clear();
+        foreach (var view in _items.Where(view => keys.Contains(view.Key))) ItemList.SelectedItems.Add(view);
+        if (ItemList.SelectedItems.Count > 0) ItemList.ScrollIntoView(ItemList.SelectedItems[0]);
     }
 
-    private void RemoveItemAt(int index)
+    private ContextMenu? _itemMenu;
+
+    /// <summary>The host's item menu (M18 spec §3), at the pointer, or at the item for the menu key.</summary>
+    public void ShowItemMenu(ContextMenu menu, bool fromKeyboard)
     {
-        CancelRename(_items[index]);
-        _items.RemoveAt(index);
+        _itemMenu = menu;
+        var selected = ItemList.SelectedItem is { } item ? ItemList.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem : null;
+        menu.PlacementTarget = fromKeyboard && selected is not null ? selected : ItemList;
+        menu.Placement = fromKeyboard && selected is not null
+            ? System.Windows.Controls.Primitives.PlacementMode.Center
+            : System.Windows.Controls.Primitives.PlacementMode.MousePoint;
+        menu.IsOpen = true;
     }
 
     private int IconSizePx => (int)Math.Round(_iconSizeDips * VisualTreeHelper.GetDpi(this).DpiScaleX);
@@ -722,12 +687,6 @@ public partial class FenceWindow : Window
     public void SetShortcutArrows(bool show) => Resources["ShortcutArrowVisibility"] = show ? Visibility.Visible : Visibility.Collapsed;
 
     private void ApplyItemWidth()
-    {
-        Resources["EditItemWidth"] = LabelledItemWidth;
-        ApplyCellWidth();
-    }
-
-    private void ApplyCellWidth()
     {
         // Game Library tiles are 2:3, 1.5 × the icon size wide (M12).
         Resources["TileWidth"] = Math.Round(_iconSizeDips * 1.5);
@@ -760,7 +719,7 @@ public partial class FenceWindow : Window
     {
         var container = _labelMode != LabelMode.OnHover ? null
             : _hoveredContainer ?? (ItemList.SelectedItems.Count == 1 ? ItemList.ItemContainerGenerator.ContainerFromItem(ItemList.SelectedItem) as ListBoxItem : null);
-        if (container is not { DataContext: FenceItemView { IsEditing: false } view, IsVisible: true })
+        if (container is not { DataContext: FenceItemView view, IsVisible: true })
         {
             HoverLabel.Visibility = Visibility.Collapsed;
             return;
@@ -930,7 +889,7 @@ public partial class FenceWindow : Window
     private void OnHoverTick()
     {
         if (Handle == 0 || !_rolledUp) return;
-        if (_drag is not null || BodyContextMenu.IsOpen || _renaming || _items.Any(view => view.IsEditing)) return; // never close under the user
+        if (_drag is not null || BodyContextMenu.IsOpen || _itemMenu?.IsOpen == true || _renaming) return; // never close under the user
         var (cursorX, cursorY) = FenceWindowChrome.GetCursorPosition();
         var rect = FenceWindowChrome.GetPixelRect(Handle);
         // While the height animates, judge "inside" against where the fence is going, so it does not flicker shut.
@@ -1051,11 +1010,11 @@ public partial class FenceWindow : Window
     public void ReloadSpecialIcons()
     {
         var iconSizePx = IconSizePx;
-        foreach (var view in _items.Where(view => view.ItemRef.StartsWith("::", StringComparison.Ordinal))) _iconLoader.Request(view, iconSizePx);
+        foreach (var view in _items.Where(view => view.Target.StartsWith("::", StringComparison.Ordinal))) _iconLoader.Request(view, iconSizePx);
     }
 
-    /// <summary>New size or DPI: every icon is requested again in place; names, selection and renames stay (M2c review carry-over).</summary>
-    private void ReloadIcons()
+    /// <summary>New size or DPI, or "Refresh": every icon and name is requested again in place; selection stays (M2c review carry-over).</summary>
+    public void ReloadIcons()
     {
         var iconSizePx = IconSizePx;
         foreach (var view in _items) _iconLoader.Request(view, iconSizePx);
@@ -1098,24 +1057,24 @@ public partial class FenceWindow : Window
 
     private void OnItemListKeyDown(object sender, KeyEventArgs args)
     {
-        if (args.OriginalSource is TextBox) return; // keys typed into the rename box
         var selected = ItemList.SelectedItems.OfType<FenceItemView>().ToList();
-        switch (args.Key)
+        var key = args.Key == Key.System ? args.SystemKey : args.Key; // Alt+Enter arrives as Key.System
+        switch (key)
         {
+            case Key.Enter when Keyboard.Modifiers == ModifierKeys.Alt && selected.Count == 1 && !_isLibrary:
+                PropertiesRequested?.Invoke(selected[0].Key, false);
+                break;
             case Key.Enter when selected.Count == 1:
-                OpenRequested?.Invoke(selected[0].ItemRef);
+                OpenRequested?.Invoke(selected[0].Key);
                 break;
             case Key.Enter when selected.Count > 1:
-                OpenManyRequested?.Invoke(selected.Select(view => view.ItemRef).ToList());
+                OpenManyRequested?.Invoke(selected.Select(view => view.Key).ToList());
                 break;
             case Key.Delete when selected.Count > 0:
-                RecycleRequested?.Invoke(selected.Select(view => view.ItemRef).ToList()); // Shift+Del too: always the Recycle Bin
-                break;
-            case Key.Back when _isPortal:
-                BackRequested?.Invoke();
+                RemoveRequested?.Invoke(selected.Select(view => view.Key).ToList()); // Shift+Del too: only the item goes
                 break;
             case Key.F2 when selected.Count == 1 && !_isLibrary: // library shortcuts are named by their game (M12)
-                BeginItemRename(selected[0].ItemRef);
+                PropertiesRequested?.Invoke(selected[0].Key, true);
                 break;
             default:
                 return;
@@ -1123,53 +1082,7 @@ public partial class FenceWindow : Window
         args.Handled = true;
     }
 
-    /// <summary>Starts renaming one item in place (F2, or Rename in Windows' item menu). Special items cannot be renamed.</summary>
-    public void BeginItemRename(string itemRef)
-    {
-        var view = _items.FirstOrDefault(candidate => candidate.ItemRef == itemRef);
-        if (view is null || itemRef.StartsWith("::", StringComparison.Ordinal)) return;
-        ItemList.ScrollIntoView(view);
-        view.EditName = view.Label;
-        view.IsEditing = true; // the box shows; OnLabelBoxVisibleChanged focuses it
-    }
-
-    private void OnLabelBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
-    {
-        if (sender is not TextBox { IsVisible: true, DataContext: FenceItemView view } box) return;
-        Activate();
-        // Without focus typing would go to another app and the box could never close (ADR-015): give up instead.
-        if (!box.Focus() || !IsActive)
-        {
-            view.IsEditing = false;
-            return;
-        }
-        // Like Explorer: select the name, not the extension.
-        var extensionStart = box.Text.LastIndexOf('.');
-        box.Select(0, extensionStart > 0 ? extensionStart : box.Text.Length);
-    }
-
-    private void OnLabelBoxKeyDown(object sender, KeyEventArgs args)
-    {
-        if (sender is not TextBox { DataContext: FenceItemView view } || args.Key is not (Key.Enter or Key.Escape)) return;
-        EndItemRename(view, commit: args.Key == Key.Enter);
-        ItemList.Focus();
-        args.Handled = true;
-    }
-
-    private void OnLabelBoxLostFocus(object sender, KeyboardFocusChangedEventArgs args)
-    {
-        if (sender is TextBox { DataContext: FenceItemView view }) EndItemRename(view, commit: true);
-    }
-
-    private void EndItemRename(FenceItemView view, bool commit)
-    {
-        if (!view.IsEditing) return;
-        view.IsEditing = false;
-        var newName = view.EditName.Trim();
-        if (commit && newName.Length > 0 && newName != view.Label) ItemRenameRequested?.Invoke(view.ItemRef, newName);
-    }
-
-    /// <summary>Right-click on an item opens Windows' item menu instead of the fence menu.</summary>
+    /// <summary>Right-click on an item opens the item menu (the host builds it) instead of the fence menu.</summary>
     private void OnBodyContextMenuOpening(object sender, ContextMenuEventArgs args)
     {
         if (ItemsControl.ContainerFromElement(ItemList, (DependencyObject)args.OriginalSource) is not ListBoxItem { DataContext: FenceItemView clicked } container) return;
@@ -1181,53 +1094,38 @@ public partial class FenceWindow : Window
         }
         // From the mouse, or (menu key: CursorLeft < 0) from the item's corner. PointToScreen gives physical pixels.
         var anchor = args.CursorLeft >= 0 ? PointToScreen(Mouse.GetPosition(this)) : container.PointToScreen(new Point(container.ActualWidth / 2, container.ActualHeight / 2));
-        // The clicked item first: menu → Rename renames it, whatever else is selected (user choice 2026-10-03).
-        List<string> refs = [clicked.ItemRef, .. ItemList.SelectedItems.OfType<FenceItemView>().Select(view => view.ItemRef).Where(itemRef => itemRef != clicked.ItemRef)];
-        ItemMenuRequested?.Invoke(refs, (int)anchor.X, (int)anchor.Y, args.CursorLeft < 0);
+        // The clicked item first: Properties and Locate… act on it, whatever else is selected (user choice 2026-10-03).
+        List<string> keys = [clicked.Key, .. ItemList.SelectedItems.OfType<FenceItemView>().Select(view => view.Key).Where(key => key != clicked.Key)];
+        ItemMenuRequested?.Invoke(keys, (int)anchor.X, (int)anchor.Y, args.CursorLeft < 0);
     }
 
-    /// <summary>The item under a screen point (physical pixels) and the index to insert before when dropping there.</summary>
-    public FenceDropPoint HitTest(int screenX, int screenY)
+    /// <summary>The index to insert before when dropping at a screen point (physical pixels).</summary>
+    public int HitTest(int screenX, int screenY)
     {
         // Over a tab header: show that tab now, so the drop lands in it (M9).
         if (TabHeaderAt(screenX, screenY) is { } hoveredTab && hoveredTab != FenceId) TabSelected?.Invoke(hoveredTab);
         var point = ItemList.PointFromScreen(new Point(screenX, screenY));
-        string? hovered = null;
         var cells = new List<(double Left, double Top, double Width, double Height)>();
         var cellIndexes = new List<int>();
         for (var index = 0; index < _items.Count; index++)
         {
             if (ItemList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container) continue;
             var bounds = container.TransformToAncestor(ItemList).TransformBounds(new Rect(container.RenderSize));
-            // Only the middle of an item means "into it" (folders, Recycle Bin); its edges reorder (M3b review I2).
-            if (DropZones.IsInto(bounds.Left, bounds.Top, bounds.Width, bounds.Height, point.X, point.Y)) hovered = _items[index].ItemRef;
             cells.Add((bounds.Left, bounds.Top, bounds.Width, bounds.Height));
             cellIndexes.Add(index);
         }
         // Rows reach down to their tallest item (mixed label heights, M3b review).
         var cellAt = DropZones.InsertIndex(cells, point.X, point.Y);
-        return new FenceDropPoint(hovered, cellAt < cellIndexes.Count ? cellIndexes[cellAt] : _items.Count);
+        return cellAt < cellIndexes.Count ? cellIndexes[cellAt] : _items.Count;
     }
 
-    /// <summary>Shows where a drag would land: a caret before the insert position, or a highlight on the container taking it.</summary>
-    public void ShowDropFeedback(FenceDropPoint? drop, bool into)
+    /// <summary>Shows where a drag would land: a caret before the insert position (null hides it).</summary>
+    public void ShowDropFeedback(int? insertAt)
     {
         InsertCaret.Visibility = Visibility.Collapsed;
-        DropHighlight.Visibility = Visibility.Collapsed;
-        if (drop is not { } point) return;
-        if (into && _items.FirstOrDefault(view => view.ItemRef == point.ItemRef) is { } target
-            && ItemList.ItemContainerGenerator.ContainerFromItem(target) is ListBoxItem targetContainer)
-        {
-            var cell = targetContainer.TransformToAncestor(ItemList).TransformBounds(new Rect(targetContainer.RenderSize));
-            Canvas.SetLeft(DropHighlight, cell.Left);
-            Canvas.SetTop(DropHighlight, cell.Top);
-            DropHighlight.Width = cell.Width;
-            DropHighlight.Height = cell.Height;
-            DropHighlight.Visibility = Visibility.Visible;
-            return;
-        }
+        if (insertAt is not { } position) return;
         // Caret at the left edge of the item it goes before, or after the last item.
-        var before = point.InsertAt < _items.Count ? ItemList.ItemContainerGenerator.ContainerFromIndex(point.InsertAt) as ListBoxItem : null;
+        var before = position < _items.Count ? ItemList.ItemContainerGenerator.ContainerFromIndex(position) as ListBoxItem : null;
         var anchor = before ?? (_items.Count > 0 ? ItemList.ItemContainerGenerator.ContainerFromIndex(_items.Count - 1) as ListBoxItem : null);
         if (anchor is null) return;
         var bounds = anchor.TransformToAncestor(ItemList).TransformBounds(new Rect(anchor.RenderSize));
@@ -1249,7 +1147,7 @@ public partial class FenceWindow : Window
     private void OnListPress(object sender, MouseButtonEventArgs args)
     {
         if (args.OriginalSource is DependencyObject source && FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) is not null) return;
-        // Text selection in the rename box must never start a drag of the file (M3b review I3).
+        // Text selection in a text box must never start a drag (M3b review I3).
         if (args.OriginalSource is DependencyObject pressed && FindAncestor<TextBox>(pressed) is not null) return;
         var container = args.OriginalSource is DependencyObject element ? FindAncestor<ListBoxItem>(element) : null;
         if (container is null)
@@ -1300,7 +1198,7 @@ public partial class FenceWindow : Window
         _pressPoint = null;
         _deferredSelect = null;
         _deferredToggle = null; // dragged: the item stays selected
-        var dragged = ItemList.SelectedItems.OfType<FenceItemView>().Select(view => view.ItemRef).ToList();
+        var dragged = ItemList.SelectedItems.OfType<FenceItemView>().Select(view => view.Key).ToList();
         if (dragged.Count > 0) DragRequested?.Invoke(dragged); // returns when the drag ends (Windows' modal loop)
     }
 
@@ -1352,10 +1250,8 @@ public partial class FenceWindow : Window
     private void OnItemDoubleClick(object sender, MouseButtonEventArgs args)
     {
         if (args.ChangedButton != MouseButton.Left) return;
-        // A double-click in the rename box selects a word; it must not open the file (M3a review carry-over).
-        if (args.OriginalSource is DependencyObject source && FindAncestor<TextBox>(source) is not null) return;
         if (ItemsControl.ContainerFromElement(ItemList, (DependencyObject)args.OriginalSource) is ListBoxItem { DataContext: FenceItemView view })
-            OpenRequested?.Invoke(view.ItemRef);
+            OpenRequested?.Invoke(view.Key);
     }
 
     private void OnSourceInitialized(object? sender, EventArgs args)
@@ -1380,6 +1276,9 @@ public partial class FenceWindow : Window
             case WmWindowPosChanging:
                 if (!Peeking) FenceWindowChrome.KeepAtBottom(lParam); // fences never rise above apps, except during Peek
                 break;
+            case WmDeviceChange when wParam is DbtDeviceArrival or DbtDeviceRemoveComplete:
+                DrivesChanged?.Invoke(); // a USB stick plugged back in: its items come back by themselves (spec §4)
+                break;
             // Click mode: the first press on a rolled-up title opens it instead of starting a move (M6b).
             case WmNcLeftButtonDown when wParam == HitTestCaption && IsSecondCaptionClick(lParam):
                 RollUpToggled?.Invoke();
diff --git a/src/NeoFences.App/IconLoader.cs b/src/NeoFences.App/IconLoader.cs
index b6b6747..0640c4f 100644
--- a/src/NeoFences.App/IconLoader.cs
+++ b/src/NeoFences.App/IconLoader.cs
@@ -1,7 +1,9 @@
 using System.Collections.Concurrent;
+using System.IO;
 using System.Windows.Media;
 using System.Windows.Media.Imaging;
 using System.Windows.Threading;
+using NeoFences.Core.Items;
 using NeoFences.Shell;
 using Serilog;
 
@@ -9,11 +11,14 @@ namespace NeoFences.App;
 
 /// <summary>
 /// Loads display names and icons on two background STA threads (shell extensions expect STA; thumbnails of big
-/// files are slow) and hands them to the UI thread. A failed load leaves the placeholder, never throws.
+/// files are slow) and hands them to the UI thread. An item's own name and icon win (M18); a website shows the default
+/// browser's icon. A failed load leaves the placeholder, never throws.
 /// </summary>
 public sealed class IconLoader : IDisposable
 {
-    private readonly BlockingCollection<(FenceItemView View, int SizePx)> _requests = new();
+    private sealed record LoadRequest(FenceItemView View, int Number, int SizePx, string Target, bool WantsName, ItemIcon? OwnIcon);
+
+    private readonly BlockingCollection<LoadRequest> _requests = new();
     private readonly Dispatcher _uiDispatcher;
 
     public IconLoader(Dispatcher uiDispatcher)
@@ -28,40 +33,74 @@ public sealed class IconLoader : IDisposable
         }
     }
 
-    /// <summary>Called on the UI thread. With two workers an older request can finish last: only the wanted size is applied.</summary>
+    /// <summary>Called on the UI thread. With two workers an older request can finish last: only the newest is applied.</summary>
     public void Request(FenceItemView view, int sizePx)
     {
-        view.WantedSizePx = sizePx;
-        _requests.TryAdd((view, sizePx));
+        view.IconRequest++;
+        _requests.TryAdd(new LoadRequest(view, view.IconRequest, sizePx, view.Target, WantsName: view.OwnName is null, view.OwnIcon));
     }
 
     private void Work()
     {
-        foreach (var (view, sizePx) in _requests.GetConsumingEnumerable())
+        foreach (var request in _requests.GetConsumingEnumerable())
         {
             try
             {
-                var label = ShellItems.TryGetDisplayName(view.ItemRef);
-                var image = ShellItems.TryGetImage(view.ItemRef, sizePx);
-                BitmapSource? icon = null;
-                if (image is not null)
-                {
-                    icon = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Pbgra32, null, image.Pixels, image.Width * 4);
-                    icon.Freeze(); // frozen: usable from the UI thread
-                }
+                var kind = ItemKinds.Of(request.Target);
+                var label = !request.WantsName ? null
+                    : kind == ItemKind.Website ? ItemKinds.WebsiteName(request.Target)
+                    : ShellItems.TryGetDisplayName(request.Target);
+                var icon = OwnIcon(request) ?? TargetIcon(request, kind);
                 _uiDispatcher.BeginInvoke(() =>
                 {
-                    if (label is not null) view.Label = label;
-                    if (icon is not null && sizePx == view.WantedSizePx) view.Icon = icon;
+                    if (request.Number != request.View.IconRequest) return; // a newer request (size, target, icon) is on its way
+                    if (label is not null && request.View.OwnName is null) request.View.Label = label;
+                    if (icon is not null) request.View.Icon = icon;
                 });
             }
             catch (Exception failure)
             {
                 // One bad item (odd bitmap, huge thumbnail) must not kill the app and crash-loop it (M2b review I5).
-                Log.Warning(failure, "could not load the icon of {ItemRef}", view.ItemRef);
+                Log.Warning(failure, "could not load the icon of {Target}", request.Target);
             }
         }
     }
 
+    /// <summary>The item's own icon: one in a file, or a picture in NeoFences' icons folder. Null when it cannot be read (then the target's).</summary>
+    private static BitmapSource? OwnIcon(LoadRequest request)
+    {
+        if (request.OwnIcon is { File: { Length: > 0 } file } icon) return Frozen(ShellItems.TryGetFileIcon(file, icon.Index, request.SizePx));
+        if (request.OwnIcon is not { Image: { Length: > 0 } image }) return null;
+        try
+        {
+            var picture = new BitmapImage();
+            picture.BeginInit();
+            picture.CacheOption = BitmapCacheOption.OnLoad; // the file is not kept open
+            picture.DecodePixelWidth = request.SizePx;
+            picture.UriSource = new Uri(Path.Combine(AppPaths.IconsDirectory, Path.GetFileName(image)));
+            picture.EndInit();
+            picture.Freeze();
+            return picture;
+        }
+        catch (Exception failure) when (failure is not OutOfMemoryException)
+        {
+            Log.Warning(failure, "item picture {Image} could not be read; the target's icon shows", image);
+            return null;
+        }
+    }
+
+    private static BitmapSource? TargetIcon(LoadRequest request, ItemKind kind) =>
+        kind == ItemKind.Website
+            ? ShellItems.DefaultBrowserPath() is { } browser ? Frozen(ShellItems.TryGetImage(browser, request.SizePx)) : null
+            : Frozen(ShellItems.TryGetImage(request.Target, request.SizePx));
+
+    private static BitmapSource? Frozen(ShellImage? image)
+    {
+        if (image is null) return null;
+        var icon = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Pbgra32, null, image.Pixels, image.Width * 4);
+        icon.Freeze(); // frozen: usable from the UI thread
+        return icon;
+    }
+
     public void Dispose() => _requests.CompleteAdding();
 }
diff --git a/src/NeoFences.App/InstallHooks.cs b/src/NeoFences.App/InstallHooks.cs
index 27682ea..1a90111 100644
--- a/src/NeoFences.App/InstallHooks.cs
+++ b/src/NeoFences.App/InstallHooks.cs
@@ -41,8 +41,8 @@ public static class InstallHooks
     /// </summary>
     private static void OnAfterInstall() => WithLog(() =>
     {
-        // Only icons NeoFences hid itself (its takeover-active marker): icons the user hid in Explorer stay theirs (M8a, final review).
-        if (new Watchdog(AppPaths.DataDirectory, _ => { }).IsTakeoverActiveMarked)
+        // Only icons NeoFences hid itself (its icons-hidden marker): icons the user hid in Explorer stay theirs (M8a, final review).
+        if (new Watchdog(AppPaths.DataDirectory, _ => { }).IsIconsHiddenMarked)
         {
             Log.Information("install: showing the desktop icons NeoFences had hidden");
             DesktopIcons.ShowWithRetry(giveUpAfter: InstallIconRetryLimit, log: message => Log.Information("install: {Message}", message));
diff --git a/src/NeoFences.App/ItemPropertiesWindow.xaml b/src/NeoFences.App/ItemPropertiesWindow.xaml
new file mode 100644
index 0000000..b2acec0
--- /dev/null
+++ b/src/NeoFences.App/ItemPropertiesWindow.xaml
@@ -0,0 +1,83 @@
+<Window x:Class="NeoFences.App.ItemPropertiesWindow"
+        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
+        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
+        Title="Item properties" Width="520" SizeToContent="Height" ResizeMode="NoResize"
+        WindowStartupLocation="CenterScreen" ShowInTaskbar="False" ThemeMode="System">
+    <!-- M18 spec §3: everything here changes only the item, never its target. Also "Add item…" (spec §2). -->
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
+                <RowDefinition Height="Auto" />
+            </Grid.RowDefinitions>
+
+            <TextBlock Text="Name" Style="{StaticResource FieldLabel}" />
+            <TextBox x:Name="NameBox" Grid.Column="1" Grid.ColumnSpan="2" Margin="0,0,0,10" AutomationProperties.Name="Name" />
+
+            <TextBlock Grid.Row="1" Text="Target" Style="{StaticResource FieldLabel}" />
+            <TextBox x:Name="TargetBox" Grid.Row="1" Grid.Column="1" Margin="0,0,8,10" AutomationProperties.Name="Target: a file, folder, app or web address" />
+            <Button x:Name="BrowseButton" Grid.Row="1" Grid.Column="2" Content="Browse ▾" Margin="0,0,0,10" AutomationProperties.Name="Browse for the target">
+                <Button.ContextMenu>
+                    <ContextMenu>
+                        <MenuItem x:Name="BrowseFileItem" Header="A file or app…" />
+                        <MenuItem x:Name="BrowseFolderItem" Header="A folder…" />
+                    </ContextMenu>
+                </Button.ContextMenu>
+            </Button>
+            <TextBlock x:Name="TargetStatus" Grid.Row="2" Grid.Column="1" Grid.ColumnSpan="2" Style="{StaticResource Hint}" Margin="0,-6,0,10"
+                       AutomationProperties.LiveSetting="Polite" />
+
+            <TextBlock Grid.Row="3" Text="Arguments" Style="{StaticResource FieldLabel}" />
+            <TextBox x:Name="ArgumentsBox" Grid.Row="3" Grid.Column="1" Grid.ColumnSpan="2" Margin="0,0,0,10" AutomationProperties.Name="Arguments" />
+
+            <CheckBox x:Name="AdminBox" Grid.Row="4" Grid.Column="1" Grid.ColumnSpan="2" Content="Run as administrator" Margin="0,0,0,12" />
+
+            <TextBlock Grid.Row="5" Text="Icon" Style="{StaticResource FieldLabel}" />
+            <StackPanel Grid.Row="5" Grid.Column="1" Grid.ColumnSpan="2" Orientation="Horizontal" Margin="0,0,0,12">
+                <Border Width="56" Height="56" CornerRadius="6" Background="{DynamicResource CardBackgroundFillColorDefaultBrush}" Margin="0,0,12,0">
+                    <Image x:Name="IconPreview" Width="48" Height="48" />
+                </Border>
+                <Button x:Name="ChangeIconButton" Content="Change icon ▾" VerticalAlignment="Center" AutomationProperties.Name="Change icon">
+                    <Button.ContextMenu>
+                        <ContextMenu>
+                            <MenuItem x:Name="IconFromFileItem" Header="From a file… (.ico, .exe, .dll)" />
+                            <MenuItem x:Name="IconFromPictureItem" Header="From a picture… (PNG, JPG, ICO)" />
+                            <MenuItem x:Name="ResetIconItem" Header="Reset to the target's icon" />
+                        </ContextMenu>
+                    </Button.ContextMenu>
+                </Button>
+            </StackPanel>
+
+            <TextBlock Grid.Row="6" Text="Note" Style="{StaticResource FieldLabel}" VerticalAlignment="Top" Margin="0,6,12,10" />
+            <TextBox x:Name="NoteBox" Grid.Row="6" Grid.Column="1" Grid.ColumnSpan="2" Height="64" AcceptsReturn="True" TextWrapping="Wrap"
+                     VerticalScrollBarVisibility="Auto" Margin="0,0,0,10" AutomationProperties.Name="Note, shown as the tooltip" />
+        </Grid>
+        <TextBlock Style="{StaticResource Hint}" Margin="0,0,0,14"
+                   Text="These change only this item in NeoFences. The file, folder or app itself is never changed." />
+        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
+            <Button x:Name="OkButton" Content="OK" IsDefault="True" MinWidth="90" Margin="0,0,8,0" />
+            <Button Content="Cancel" IsCancel="True" MinWidth="90" />
+        </StackPanel>
+    </StackPanel>
+</Window>
diff --git a/src/NeoFences.App/ItemPropertiesWindow.xaml.cs b/src/NeoFences.App/ItemPropertiesWindow.xaml.cs
new file mode 100644
index 0000000..11f7bf1
--- /dev/null
+++ b/src/NeoFences.App/ItemPropertiesWindow.xaml.cs
@@ -0,0 +1,219 @@
+using System.ComponentModel;
+using System.IO;
+using System.Windows;
+using System.Windows.Controls;
+using System.Windows.Interop;
+using System.Windows.Media.Imaging;
+using System.Windows.Threading;
+using NeoFences.Core.Items;
+using NeoFences.Shell;
+using Serilog;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// An item's Properties (M18 spec §3), also "Add item…" (spec §2): name, target, arguments, run as administrator, icon
+/// and note. OK gives <see cref="Result"/> (and a picture to copy in); nothing is applied here.
+/// </summary>
+public partial class ItemPropertiesWindow : Window
+{
+    private readonly VirtualItem _original;
+    private readonly FenceItemView _preview;
+    private readonly IconLoader _iconLoader;
+    private readonly DispatcherTimer _checkDelay = new() { Interval = TimeSpan.FromMilliseconds(400) };
+    private ItemIcon? _icon;
+    private string? _picture;    // chosen now: copied into NeoFences' icons folder on OK (the host does it)
+    private bool _isFolder;      // from the last check of the target
+    private int _checkNumber;    // only the newest check is shown
+
+    /// <summary>The item with the fields as typed (OK), or null.</summary>
+    public VirtualItem? Result { get; private set; }
+
+    /// <summary>A picture chosen as the icon, still at its own place; null when none was chosen.</summary>
+    public string? PictureToCopy => _picture;
+
+    /// <param name="adding">"Add item…": an empty item, the title and button say so, the target box has the keyboard.</param>
+    /// <param name="focusName">F2: the name box has the keyboard, all selected.</param>
+    public ItemPropertiesWindow(VirtualItem item, bool adding, IconLoader iconLoader, bool focusName)
+    {
+        InitializeComponent();
+        _original = item;
+        _iconLoader = iconLoader;
+        _icon = item.Icon;
+        Title = adding ? "Add item" : $"{item.OwnName ?? "Item"} — properties";
+        OkButton.Content = adding ? "Add" : "OK";
+        NameBox.Text = item.Name ?? "";
+        TargetBox.Text = item.Target;
+        ArgumentsBox.Text = item.Arguments ?? "";
+        AdminBox.IsChecked = item.RunAsAdmin;
+        NoteBox.Text = item.Note ?? "";
+        _preview = new FenceItemView(new ShownItem("preview", item.Target, item.Name, item.Icon));
+        _preview.PropertyChanged += OnPreviewChanged;
+
+        BrowseButton.Click += (_, _) => OpenMenu(BrowseButton);
+        ChangeIconButton.Click += (_, _) => OpenMenu(ChangeIconButton);
+        BrowseFileItem.Click += (_, _) => Browse(folder: false);
+        BrowseFolderItem.Click += (_, _) => Browse(folder: true);
+        IconFromFileItem.Click += (_, _) => PickIconFromFile();
+        IconFromPictureItem.Click += (_, _) => PickPicture();
+        ResetIconItem.Click += (_, _) => SetIcon(icon: null, picture: null);
+        TargetBox.TextChanged += (_, _) =>
+        {
+            _checkDelay.Stop(); // typed: checked once the typing pauses
+            _checkDelay.Start();
+        };
+        _checkDelay.Tick += (_, _) =>
+        {
+            _checkDelay.Stop();
+            CheckTarget();
+        };
+        OkButton.Click += (_, _) => Accept();
+        Loaded += (_, _) =>
+        {
+            CheckTarget();
+            var box = adding ? TargetBox : focusName ? NameBox : null;
+            if (box is null) return;
+            box.Focus();
+            box.SelectAll();
+        };
+        Closed += (_, _) => _checkDelay.Stop();
+    }
+
+    private nint Handle => new WindowInteropHelper(this).Handle;
+
+    private static void OpenMenu(Button button)
+    {
+        button.ContextMenu.PlacementTarget = button;
+        button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
+        button.ContextMenu.IsOpen = true;
+    }
+
+    private string? CurrentTarget => ItemKinds.Clean(TargetBox.Text);
+
+    private void Browse(bool folder)
+    {
+        var start = CurrentTarget is { } target && ItemKinds.Of(target) == ItemKind.Path ? Path.GetDirectoryName(target.TrimEnd('\\')) : null;
+        void LogFailure(Exception failure) => Log.Warning(failure, "Browse dialog failed");
+        var picked = folder
+            ? PathPicker.TryPickFolder(Handle, "Choose a folder", LogFailure, start)
+            : PathPicker.TryPickFile(Handle, "Choose a file or app", LogFailure, start);
+        if (picked is not null) TargetBox.Text = picked; // checked by TextChanged
+    }
+
+    /// <summary>Windows' icon picker, opening at the item's icon file, else its target (an .exe or .dll offers its own icons).</summary>
+    private void PickIconFromFile()
+    {
+        var target = CurrentTarget;
+        var start = _icon?.File ?? (target is not null && Path.GetExtension(target).ToLowerInvariant() is ".exe" or ".dll" or ".ico" ? target : null);
+        if (IconPicker.TryPick(Handle, start, _icon?.Index ?? 0) is not { } chosen) return;
+        SetIcon(new ItemIcon { File = chosen.File, Index = chosen.Index }, picture: null);
+    }
+
+    private void PickPicture()
+    {
+        var picked = PathPicker.TryPickFile(Handle, "Choose a picture for the icon", failure => Log.Warning(failure, "picture dialog failed"),
+            filters: [("Pictures", "*.png;*.jpg;*.jpeg;*.ico;*.bmp;*.gif"), ("All files", "*.*")]);
+        if (picked is not null) SetIcon(icon: null, picture: picked);
+    }
+
+    /// <summary>A new icon choice: a file's icon, a picture (shown from where it is until it is copied in), or none.</summary>
+    private void SetIcon(ItemIcon? icon, string? picture)
+    {
+        _icon = icon;
+        _picture = picture;
+        RefreshPreview();
+    }
+
+    private void RefreshPreview()
+    {
+        if (_picture is not null)
+        {
+            try
+            {
+                var image = new BitmapImage();
+                image.BeginInit();
+                image.CacheOption = BitmapCacheOption.OnLoad;
+                image.DecodePixelWidth = 96;
+                image.UriSource = new Uri(_picture);
+                image.EndInit();
+                IconPreview.Source = image;
+            }
+            catch (Exception failure) when (failure is not OutOfMemoryException)
+            {
+                Log.Warning(failure, "picture {Picture} cannot be shown", _picture);
+                _picture = null;
+                MessageBox.Show(this, "This picture cannot be used as an icon.", "NeoFences", MessageBoxButton.OK, MessageBoxImage.Warning);
+            }
+            return;
+        }
+        if (_preview.Update(new ShownItem("preview", CurrentTarget ?? "", NameBox.Text, _icon)) || _preview.Icon is null) _iconLoader.Request(_preview, 48);
+        IconPreview.Source = _preview.Icon;
+    }
+
+    private void OnPreviewChanged(object? sender, PropertyChangedEventArgs change)
+    {
+        if (change.PropertyName == nameof(FenceItemView.Icon) && _picture is null) IconPreview.Source = _preview.Icon;
+    }
+
+    /// <summary>Found / Missing / Drive not connected (spec §3), off the UI thread; Arguments and admin greyed where they do not apply.</summary>
+    private void CheckTarget()
+    {
+        var target = CurrentTarget;
+        RefreshPreview();
+        if (target is null)
+        {
+            ShowStatus("Type a path or a web address, or use Browse.", takesArguments: false);
+            return;
+        }
+        var kind = ItemKinds.Of(target);
+        if (kind != ItemKind.Path)
+        {
+            ShowStatus(kind == ItemKind.Website ? "A website: opens in your browser." : "A Windows item.", takesArguments: false);
+            return;
+        }
+        var number = ++_checkNumber;
+        ShowStatus("Checking…", takesArguments: true);
+        Task.Run(() => TargetProbe.Check(target)).ContinueWith(checking =>
+        {
+            if (number != _checkNumber) return;
+            var check = checking.Result;
+            _isFolder = check.IsFolder;
+            ShowStatus(check.State switch
+            {
+                TargetState.Missing => "● Missing: nothing is there now.",
+                TargetState.Unavailable => TargetChecks.IsNetworkPath(target) ? "● Network location not reachable." : "● Drive not connected.",
+                _ when TargetChecks.RootOf(target) is null => "Opens through Windows.",
+                _ => check.IsFolder ? "● Found (a folder)." : "● Found.",
+            }, takesArguments: ItemKinds.TakesArguments(kind, check.IsFolder));
+        }, TaskScheduler.FromCurrentSynchronizationContext());
+    }
+
+    private void ShowStatus(string text, bool takesArguments)
+    {
+        TargetStatus.Text = text;
+        ArgumentsBox.IsEnabled = takesArguments;
+        AdminBox.IsEnabled = takesArguments;
+    }
+
+    private void Accept()
+    {
+        if (CurrentTarget is not { } target)
+        {
+            ShowStatus("Type a path or a web address, or use Browse.", takesArguments: false);
+            TargetBox.Focus();
+            return;
+        }
+        var takesArguments = ItemKinds.TakesArguments(ItemKinds.Of(target), _isFolder);
+        var note = NoteBox.Text.Trim();
+        Result = _original with
+        {
+            Target = target,
+            Name = NameBox.Text.Trim() is { Length: > 0 } name ? name : null,
+            Arguments = takesArguments && ArgumentsBox.Text.Trim() is { Length: > 0 } arguments ? arguments : null,
+            RunAsAdmin = takesArguments && AdminBox.IsChecked == true,
+            Note = note.Length > 0 ? note : null,
+            Icon = _icon,
+        };
+        DialogResult = true;
+    }
+}
diff --git a/src/NeoFences.App/LibraryLister.cs b/src/NeoFences.App/LibraryLister.cs
new file mode 100644
index 0000000..ceb4560
--- /dev/null
+++ b/src/NeoFences.App/LibraryLister.cs
@@ -0,0 +1,204 @@
+using System.IO;
+using System.Windows.Threading;
+using NeoFences.Core.Model;
+using NeoFences.Shell;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// The Game Library fence's folder listing (M12; Portals' code until M18): NeoFences' own library folder and its watcher.
+/// Listing and watcher setup run off the UI thread (M4 review I1); bursts of changes become one re-list at most every
+/// 250 ms (I2); an unavailable or unwatched folder is retried every few seconds (I4).
+/// </summary>
+public sealed class LibraryLister : IDisposable
+{
+    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(250);
+    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(7);
+
+    private readonly DispatcherTimer _refreshTimer;
+    private readonly DispatcherTimer _retryTimer;
+    private readonly Action<IReadOnlyList<ItemInfo>?> _show;
+    private readonly Action<Exception> _logFailure;
+    private FolderWatcher? _watcher;
+    private DeviceRemovalNotice? _removal;   // asks before the library drive is removed (USB stick, M8d)
+    private readonly nint _noticeOwner;
+    private volatile bool _noticeFailureLogged;
+    private readonly DispatcherTimer _backoffTimer;
+    private TimeSpan _failureDelay = NeoFences.Core.Lifecycle.WatcherBackoff.First;
+    private DateTime _lastArm = DateTime.MinValue;
+    private int _generation;
+    private bool _refreshing;   // a listing is running in the background (a network folder may take seconds)
+    private bool _disposed;
+    private bool _paused;        // game mode (M6a): changes wait
+    private bool _missedChanges; // something changed while paused: re-list on resume
+    // Listings in flight hold a watcher and a notice of their own until the UI takes them: those can be let go as well (M13b).
+    private readonly System.Collections.Concurrent.ConcurrentDictionary<nint, (FolderWatcher Watcher, DeviceRemovalNotice Notice)> _inFlight = new();
+    private DateTime _releasedUntil = DateTime.MinValue; // just let go of a drive being removed: no re-opening for a moment (M13a)
+    private static readonly TimeSpan ReleaseGrace = TimeSpan.FromSeconds(5);
+
+    public string Folder { get; }
+
+    /// <param name="show">Called on the UI thread with the shown folder's items, or null when it cannot be read.</param>
+    /// <param name="noticeOwner">The window that receives "may this drive be removed?" (the app's message window).</param>
+    public LibraryLister(string folder, nint noticeOwner, Action<IReadOnlyList<ItemInfo>?> show, Action<Exception> logFailure)
+    {
+        _noticeOwner = noticeOwner;
+        _backoffTimer = new DispatcherTimer();
+        _backoffTimer.Tick += (_, _) =>
+        {
+            _backoffTimer.Stop();
+            if (_paused) _missedChanges = true; // re-listed when the game ends (M8d review I1)
+            else Refresh();
+        };
+        Folder = folder.TrimEnd('\\');
+        _show = show;
+        _logFailure = logFailure;
+        _refreshTimer = new DispatcherTimer { Interval = RefreshDelay };
+        _refreshTimer.Tick += (_, _) =>
+        {
+            _refreshTimer.Stop();
+            Refresh();
+        };
+        _retryTimer = new DispatcherTimer { Interval = RetryDelay };
+        _retryTimer.Tick += (_, _) => { if (!_refreshing && !_paused) Refresh(); }; // never pile up blocked listings
+        Refresh();
+    }
+
+    /// <summary>
+    /// Re-arms the watcher and re-lists the shown folder in the background; only the newest request is shown. The
+    /// watcher is re-created each time: a folder deleted and created again ends the old one.
+    /// </summary>
+    public void Refresh()
+    {
+        if (_disposed) return;
+        if (DateTime.UtcNow < _releasedUntil)
+        {
+            _retryTimer.Start(); // a refresh queued just before the release would re-open the drive: the retry comes back later
+            return;
+        }
+        _refreshing = true;
+        var generation = ++_generation;
+        var folder = Folder;
+        var dispatcher = _refreshTimer.Dispatcher;
+        Task.Run(() =>
+        {
+            var watcher = new FolderWatcher(folder, _logFailure);
+            var removal = watcher.HeldFolder is { } held ? DeviceRemovalNotice.TryRegister(_noticeOwner, held, LogNoticeFailureOnce) : null;
+            if (removal is not null) _inFlight[removal.Handle] = (watcher, removal);
+            var items = FolderItems.TryList(folder);
+            dispatcher.BeginInvoke(() =>
+            {
+                // Only this listing's own entry: a handle value reused by a newer listing keeps its entry (M13c).
+                if (removal is not null) _inFlight.TryRemove(new KeyValuePair<nint, (FolderWatcher, DeviceRemovalNotice)>(removal.Handle, (watcher, removal)));
+                if (generation == _generation) _refreshing = false;
+                if (_disposed || generation != _generation)
+                {
+                    watcher.Dispose(); // a newer refresh (or the fence's deletion) superseded this one
+                    removal?.Dispose();
+                    return;
+                }
+                _watcher?.Dispose();
+                _watcher = watcher;
+                _removal?.Dispose();
+                _removal = removal;
+                _lastArm = DateTime.UtcNow;
+                watcher.Changed += () => dispatcher.BeginInvoke(ScheduleRefresh);
+                watcher.Failed += () => dispatcher.BeginInvoke(OnWatcherFailed);
+                if (watcher.HasFailed) OnWatcherFailed(); // it failed while arming, before this subscription (M8d review I2)
+                // Unreadable or unwatched (drive not there yet): try again every few seconds until it is.
+                if (items is null || !watcher.IsWatching) _retryTimer.Start();
+                else _retryTimer.Stop();
+                _show(items);
+            });
+        });
+    }
+
+    /// <summary>Game mode (spec §4.7): while paused, folder changes only mark the listing stale; resuming re-lists once.</summary>
+    public void SetPaused(bool paused)
+    {
+        _paused = paused;
+        if (paused || !_missedChanges) return;
+        _missedChanges = false;
+        Refresh();
+    }
+
+    /// <summary>
+    /// Windows asks to remove the drive holding this handle: close everything the lister holds there, so "Safely Remove"
+    /// and Eject work (M8d). It shows nothing and comes back by itself when the drive does, or the removal was refused
+    /// (the 7 s retry timer).
+    /// </summary>
+    public bool ReleaseForRemoval(nint handle)
+    {
+        if (_inFlight.TryRemove(handle, out var flying))
+        {
+            // A listing still on its way holds the drive: let go now; when it arrives it is stale and dropped (M13b).
+            ++_generation;
+            _refreshing = false;
+            flying.Watcher.Dispose();
+            flying.Notice.Dispose();
+            _releasedUntil = DateTime.UtcNow + ReleaseGrace;
+            _show(null);
+            _retryTimer.Start();
+            return true;
+        }
+        if (_removal is null || _removal.Handle != handle) return false;
+        ++_generation; // a listing in flight must not re-open the folder
+        _refreshing = false;
+        _refreshTimer.Stop();
+        _backoffTimer.Stop();
+        _watcher?.Dispose();
+        _watcher = null;
+        _removal.Dispose();
+        _removal = null;
+        _releasedUntil = DateTime.UtcNow + ReleaseGrace;
+        _show(null);
+        _retryTimer.Start(); // every 7 s, after the grace: back when the drive is, or when the removal was refused
+        return true;
+    }
+
+    /// <summary>A drive that refuses removal notices (a virtual drive reporting "Fixed") is said once, not on every refresh (M8d review).</summary>
+    private void LogNoticeFailureOnce(Exception failure)
+    {
+        if (_noticeFailureLogged) return;
+        _noticeFailureLogged = true;
+        _logFailure(failure);
+    }
+
+    /// <summary>A watcher that fails again soon after each re-arm waits longer each time, up to a minute (M8d, M4 review).</summary>
+    private void OnWatcherFailed()
+    {
+        if (_disposed || _backoffTimer.IsEnabled) return;
+        if (_paused)
+        {
+            _missedChanges = true; // a stopped watcher during a game: re-list (and re-arm) when it ends (M8d review I1)
+            return;
+        }
+        _failureDelay = NeoFences.Core.Lifecycle.WatcherBackoff.Next(_failureDelay, lastRearm: _lastArm, failureAt: DateTime.UtcNow);
+        _backoffTimer.Interval = _failureDelay;
+        _backoffTimer.Start();
+        Serilog.Log.Information("library folder watcher stopped ({Folder}); re-listing after {Delay}", Folder, _failureDelay);
+    }
+
+    private void ScheduleRefresh()
+    {
+        if (_paused)
+        {
+            _missedChanges = true;
+            return;
+        }
+        // Not restarted by every event: a file that keeps being written still re-lists every 250 ms.
+        if (!_disposed && !_refreshTimer.IsEnabled) _refreshTimer.Start();
+    }
+
+    public void Dispose()
+    {
+        _disposed = true;
+        _refreshTimer.Stop();
+        _retryTimer.Stop();
+        _backoffTimer.Stop();
+        _watcher?.Dispose();
+        _watcher = null;
+        _removal?.Dispose();
+        _removal = null;
+    }
+}
diff --git a/src/NeoFences.App/MissingItemWindow.xaml b/src/NeoFences.App/MissingItemWindow.xaml
new file mode 100644
index 0000000..61c695a
--- /dev/null
+++ b/src/NeoFences.App/MissingItemWindow.xaml
@@ -0,0 +1,17 @@
+<Window x:Class="NeoFences.App.MissingItemWindow"
+        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
+        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
+        Title="NeoFences" Width="440" SizeToContent="Height" ResizeMode="NoResize"
+        WindowStartupLocation="CenterScreen" ShowInTaskbar="False" ThemeMode="System">
+    <!-- M18 spec §3: opening a missing or unavailable item asks this instead of showing a Windows error. -->
+    <StackPanel Margin="24,18,24,20">
+        <TextBlock x:Name="Headline" FontSize="16" FontWeight="SemiBold" TextWrapping="Wrap" Margin="0,0,0,8" />
+        <TextBlock x:Name="Explanation" TextWrapping="Wrap" Margin="0,0,0,6" />
+        <TextBlock x:Name="TargetText" TextWrapping="Wrap" FontSize="12" Foreground="{DynamicResource TextFillColorSecondaryBrush}" Margin="0,0,0,18" />
+        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
+            <Button x:Name="LocateButton" Content="Locate…" IsDefault="True" MinWidth="90" Margin="0,0,8,0" />
+            <Button x:Name="RemoveButton" Content="Remove from fence" MinWidth="90" Margin="0,0,8,0" />
+            <Button Content="Cancel" IsCancel="True" MinWidth="90" />
+        </StackPanel>
+    </StackPanel>
+</Window>
diff --git a/src/NeoFences.App/MissingItemWindow.xaml.cs b/src/NeoFences.App/MissingItemWindow.xaml.cs
new file mode 100644
index 0000000..623e171
--- /dev/null
+++ b/src/NeoFences.App/MissingItemWindow.xaml.cs
@@ -0,0 +1,32 @@
+using System.Windows;
+using NeoFences.Core.Items;
+
+namespace NeoFences.App;
+
+public enum MissingItemChoice { None, Locate, Remove }
+
+/// <summary>An opened item's target is missing or on a drive that is not there (M18 spec §3): Locate… / Remove / Cancel.</summary>
+public partial class MissingItemWindow : Window
+{
+    public MissingItemChoice Choice { get; private set; }
+
+    public MissingItemWindow(string name, string target, TargetState state)
+    {
+        InitializeComponent();
+        Headline.Text = state == TargetState.Unavailable ? $"\"{name}\" is not available right now" : $"\"{name}\" is missing";
+        Explanation.Text = state == TargetState.Unavailable
+            ? TargetChecks.IsNetworkPath(target)
+                ? "Its network location does not answer. It comes back by itself when it does."
+                : $"It is on drive {TargetChecks.RootOf(target)?.TrimEnd('\\')}, which is not connected. Plug it in: the item comes back by itself."
+            : "Nothing is at its place any more: it was deleted, moved or renamed. Point the item at its new place, or remove the item.";
+        TargetText.Text = target;
+        LocateButton.Click += (_, _) => Choose(MissingItemChoice.Locate);
+        RemoveButton.Click += (_, _) => Choose(MissingItemChoice.Remove);
+    }
+
+    private void Choose(MissingItemChoice choice)
+    {
+        Choice = choice;
+        DialogResult = true;
+    }
+}
diff --git a/src/NeoFences.App/SettingsWindow.Library.cs b/src/NeoFences.App/SettingsWindow.Library.cs
index 6c3561c..c001065 100644
--- a/src/NeoFences.App/SettingsWindow.Library.cs
+++ b/src/NeoFences.App/SettingsWindow.Library.cs
@@ -45,7 +45,7 @@ public partial class SettingsWindow
         }
         AddGameFolderButton.Click += (_, _) =>
         {
-            var folder = FolderPicker.TryPick(new WindowInteropHelper(this).Handle, "Choose a folder whose sub-folders are games",
+            var folder = PathPicker.TryPickFolder(new WindowInteropHelper(this).Handle, "Choose a folder whose sub-folders are games",
                 logFailure: failure => Log.Warning(failure, "game library: the folder picker failed"));
             if (folder is not null && !_libraryFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) LibraryFoldersChanged?.Invoke([.. _libraryFolders, folder]);
         };
diff --git a/src/NeoFences.App/SettingsWindow.xaml b/src/NeoFences.App/SettingsWindow.xaml
index a2cf931..975d984 100644
--- a/src/NeoFences.App/SettingsWindow.xaml
+++ b/src/NeoFences.App/SettingsWindow.xaml
@@ -42,10 +42,10 @@
             </Border>
             <Border Style="{StaticResource Card}">
                 <DockPanel>
-                    <CheckBox x:Name="TakeoverBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Hide desktop icons" />
+                    <CheckBox x:Name="HideIconsBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Hide desktop icons while NeoFences runs" />
                     <StackPanel>
-                        <TextBlock Text="Hide desktop icons" TextWrapping="Wrap" />
-                        <TextBlock x:Name="TakeoverDescription" Style="{StaticResource Description}" Text="Show your desktop items only in fences. They are back on the desktop whenever NeoFences is paused or closed." />
+                        <TextBlock Text="Hide desktop icons while NeoFences runs" TextWrapping="Wrap" />
+                        <TextBlock x:Name="HideIconsDescription" Style="{StaticResource Description}" Text="Windows' own desktop icons go away while NeoFences runs and come back when it is paused, closed, or stops unexpectedly. Your files stay where they are." />
                     </StackPanel>
                 </DockPanel>
             </Border>
@@ -237,62 +237,6 @@
                 </StackPanel>
             </Border>
 
-            <TextBlock Text="Rules" Style="{StaticResource SectionHeader}" />
-            <Border x:Name="RulesCard" Style="{StaticResource Card}">
-                <StackPanel>
-                    <TextBlock x:Name="RulesDescription" Style="{StaticResource Description}" Margin="0,0,0,8"
-                               Text="New Desktop items go to the fence of the first rule they match; the rest go to the Inbox. Rules only choose fences: files are never moved or changed." />
-                    <ListBox x:Name="RuleList" MaxHeight="220" AutomationProperties.Name="Rules">
-                        <ListBox.ItemTemplate>
-                            <DataTemplate>
-                                <DockPanel>
-                                    <CheckBox IsChecked="{Binding Enabled, Mode=OneWay}" Tag="{Binding Id}" Checked="OnRuleToggled" Unchecked="OnRuleToggled"
-                                              VerticalAlignment="Center" Margin="0,0,8,0" AutomationProperties.Name="{Binding ToggleName}" />
-                                    <TextBlock Text="{Binding Text}" Opacity="{Binding Opacity}" TextTrimming="CharacterEllipsis" VerticalAlignment="Center" />
-                                </DockPanel>
-                            </DataTemplate>
-                        </ListBox.ItemTemplate>
-                        <ListBox.ItemContainerStyle>
-                            <Style TargetType="ListBoxItem" BasedOn="{StaticResource {x:Type ListBoxItem}}">
-                                <Setter Property="AutomationProperties.Name" Value="{Binding Spoken}" />
-                            </Style>
-                        </ListBox.ItemContainerStyle>
-                    </ListBox>
-                    <StackPanel x:Name="RuleEditor" Visibility="Collapsed" Margin="0,10,0,0">
-                        <WrapPanel>
-                            <TextBlock Text="When" VerticalAlignment="Center" Margin="0,0,8,6" />
-                            <ComboBox x:Name="RuleKindBox" Width="170" Margin="0,0,8,6" AutomationProperties.Name="Rule condition">
-                                <ComboBoxItem Content="Type of file" Tag="Type" />
-                                <ComboBoxItem Content="Game shortcut" Tag="Game" />
-                                <ComboBoxItem Content="Name" Tag="Name" />
-                                <ComboBoxItem Content="Date modified" Tag="Age" />
-                                <ComboBoxItem Content="Size" Tag="Size" />
-                            </ComboBox>
-                            <ComboBox x:Name="RuleChoiceBox" Width="190" Margin="0,0,8,6" />
-                            <TextBox x:Name="RuleTextBox" Width="150" Margin="0,0,8,6" />
-                            <Button x:Name="RuleBrowseButton" Content="Choose…" Margin="0,0,8,6" Visibility="Collapsed" AutomationProperties.Name="Choose the game folder" />
-                            <TextBlock x:Name="RuleUnitText" VerticalAlignment="Center" Margin="0,0,8,6" />
-                        </WrapPanel>
-                        <WrapPanel>
-                            <TextBlock Text="Put in" VerticalAlignment="Center" Margin="0,0,8,6" />
-                            <ComboBox x:Name="RuleFenceBox" Width="220" Margin="0,0,8,6" DisplayMemberPath="Title" AutomationProperties.Name="Put in fence" />
-                            <Button x:Name="SaveRuleButton" Content="Save rule" Margin="0,0,8,6" IsDefault="False" />
-                            <Button x:Name="CancelRuleButton" Content="Cancel" Margin="0,0,8,6" />
-                        </WrapPanel>
-                        <TextBlock x:Name="RuleHint" Style="{StaticResource Description}" />
-                    </StackPanel>
-                    <WrapPanel Margin="0,10,0,0">
-                        <Button x:Name="AddRuleButton" Content="Add" Margin="0,0,8,6" />
-                        <Button x:Name="EditRuleButton" Content="Edit" Margin="0,0,8,6" IsEnabled="False" />
-                        <Button x:Name="DeleteRuleButton" Content="Delete" Margin="0,0,8,6" IsEnabled="False" />
-                        <Button x:Name="MoveRuleUpButton" Content="Move up" Margin="0,0,8,6" IsEnabled="False" />
-                        <Button x:Name="MoveRuleDownButton" Content="Move down" Margin="0,0,8,6" IsEnabled="False" />
-                        <Button x:Name="ApplyRulesButton" Content="Apply rules now" Margin="0,0,0,6" />
-                    </WrapPanel>
-                    <TextBlock x:Name="RulesStatus" Style="{StaticResource Description}" Visibility="Collapsed" />
-                </StackPanel>
-            </Border>
-
             <TextBlock Text="Game Library" Style="{StaticResource SectionHeader}" />
             <Border x:Name="LibraryCard" Style="{StaticResource Card}">
                 <StackPanel>
diff --git a/src/NeoFences.App/SettingsWindow.xaml.cs b/src/NeoFences.App/SettingsWindow.xaml.cs
index 9ea1f28..0668dfb 100644
--- a/src/NeoFences.App/SettingsWindow.xaml.cs
+++ b/src/NeoFences.App/SettingsWindow.xaml.cs
@@ -9,10 +9,9 @@ namespace NeoFences.App;
 /// <param name="PeekHotkey">As a person reads it (key caps).</param>
 /// <param name="PeekHotkeyActive">False when Windows refused it (another app owns it): the window says so.</param>
 public sealed record SettingsView(
-    bool StartWithWindows, bool Takeover, string PeekHotkey, bool PeekHotkeyActive, RollupExpand RollupExpand,
+    bool StartWithWindows, bool HideDesktopIcons, string PeekHotkey, bool PeekHotkeyActive, RollupExpand RollupExpand,
     bool GameModeEnabled, bool GameModeActive, string Version, string DataFolder, LabelMode DefaultLabels, bool ShowShortcutArrows,
-    IReadOnlyList<NeoFences.Core.Config.SnapshotEntry> Snapshots, IReadOnlyList<Rule> Rules, IReadOnlyList<string> RuleLines,
-    IReadOnlyList<RuleFence> RuleFences, LibraryView Library, AppearanceView Appearance, UpdatesView Updates);
+    IReadOnlyList<NeoFences.Core.Config.SnapshotEntry> Snapshots, LibraryView Library, AppearanceView Appearance, UpdatesView Updates);
 
 /// <summary>One row of the Snapshots list (M10).</summary>
 public sealed record SnapshotRow(string Path, string Name, string When)
@@ -29,7 +28,8 @@ public partial class SettingsWindow : Window
     private bool _updating; // filling the controls from the host must not report changes back
 
     public event Action<bool>? StartWithWindowsChanged;
-    public event Action<bool>? TakeoverChanged;
+    /// <summary>"Hide desktop icons while NeoFences runs" (M18).</summary>
+    public event Action<bool>? HideDesktopIconsChanged;
     /// <summary>A new Peek hotkey was pressed in the box (text like "Ctrl+Alt+P"); answer with <see cref="ShowHotkeyResult"/>.</summary>
     public event Action<string>? PeekHotkeyChosen;
     /// <summary>Settings → Updates (M17).</summary>
@@ -58,7 +58,7 @@ public partial class SettingsWindow : Window
         InitializeComponent();
         // Checked/Unchecked, not Click: UI Automation (Narrator, Toggle) changes the box without a click (M6b smoke).
         OnToggled(StartupBox, isChecked => StartWithWindowsChanged?.Invoke(isChecked));
-        OnToggled(TakeoverBox, isChecked => TakeoverChanged?.Invoke(isChecked));
+        OnToggled(HideIconsBox, isChecked => HideDesktopIconsChanged?.Invoke(isChecked));
         OnToggled(GameModeBox, isChecked => GameModeChanged?.Invoke(isChecked));
         RollupBox.SelectionChanged += (_, _) =>
         {
@@ -80,7 +80,7 @@ public partial class SettingsWindow : Window
         LabelsApplyAllButton.Click += (_, _) => LabelsAppliedToAll?.Invoke(SelectedLabels);
         // Screen readers read each setting's description with it (M6b review carry-over).
         foreach (var (control, description) in new (UIElement, TextBlock)[]
-                 { (StartupBox, StartupDescription), (TakeoverBox, TakeoverDescription), (HotkeyBox, HotkeyDescription),
+                 { (StartupBox, StartupDescription), (HideIconsBox, HideIconsDescription), (HotkeyBox, HotkeyDescription),
                    (LabelsBox, LabelsDescription), (ArrowsBox, ArrowsDescription), (RollupBox, RollupDescription), (GameModeBox, GameModeDescription) })
         {
             System.Windows.Automation.AutomationProperties.SetHelpText(control, description.Text);
@@ -108,7 +108,6 @@ public partial class SettingsWindow : Window
         };
         SnapshotNameBox.LostKeyboardFocus += (_, _) => EndSnapshotRename(commit: true, backToRow: false); // the user went elsewhere: focus stays there
         System.Windows.Automation.AutomationProperties.SetHelpText(SnapshotList, SnapshotsDescription.Text);
-        InitializeRules();
         InitializeLibrary();
         InitializeAppearance();
         OnToggled(AutoUpdateBox, isChecked => AutoUpdateChanged?.Invoke(isChecked)); // M17
@@ -213,7 +212,6 @@ public partial class SettingsWindow : Window
     public void Show(SettingsView view)
     {
         ShowSnapshots(view.Snapshots);
-        ShowRules(view);
         _updating = true;
         LabelsBox.SelectedIndex = view.DefaultLabels == LabelMode.OnHover ? 1 : 0;
         ArrowsBox.IsChecked = view.ShowShortcutArrows;
@@ -222,7 +220,7 @@ public partial class SettingsWindow : Window
             ShowHotkeyResult(saved: false, message: $"{view.PeekHotkey} is not active: Windows or another app owns it. Record another combination.");
         }
         StartupBox.IsChecked = view.StartWithWindows;
-        TakeoverBox.IsChecked = view.Takeover;
+        HideIconsBox.IsChecked = view.HideDesktopIcons;
         HotkeyBox.Text = view.PeekHotkey;
         RollupBox.SelectedIndex = view.RollupExpand == RollupExpand.Click ? 1 : 0;
         GameModeBox.IsChecked = view.GameModeEnabled;
diff --git a/src/NeoFences.App/ShellWorker.cs b/src/NeoFences.App/ShellWorker.cs
index a31a89d..b21860c 100644
--- a/src/NeoFences.App/ShellWorker.cs
+++ b/src/NeoFences.App/ShellWorker.cs
@@ -4,7 +4,7 @@ using Serilog;
 namespace NeoFences.App;
 
 /// <summary>
-/// One STA thread for shell operations that can block or show Windows' dialogs (open, recycle, rename): the fences keep
+/// One STA thread for shell operations that can block or show Windows' dialogs (recycling a snapshot): the fences keep
 /// responding meanwhile, and shell handlers get the apartment they expect (M3a review). Operations run in order; a
 /// failure is logged, never thrown.
 /// </summary>
````

- [ ] **Step 2: Build and test everything.**
  Run: `dotnet build` then `dotnet test`
  Expected: `0 Warning(s)`, `0 Error(s)`; `Passed! - Failed: 0, Passed: 400`.
- [ ] **Step 3: No leftovers of the parked model.**
  Run: `grep -rn -E "FenceMembership|FenceSource|IsInbox|Takeover|PortalState|ItemRenameRequested|RecycleRequested" src --include=*.cs --include=*.xaml`
  Expected: no output.
- [ ] **Step 4: Commit.**
  `git add -A src/NeoFences.App && git commit -m "feat: showed virtual items in fences with their menu, Properties, Add item and target watching"`

### Task 6: Docs

**Files:** `docs/ARCHITECTURE.md` (rewritten for virtual items), `docs/FEATURES.md` (statuses: done / parked),
`docs/TEST-CHECKLIST.md` (+ section AD), `docs/ROADMAP.md` (M18 sub-items, two mangled `F:\projects\` paths fixed,
M19 line)

- [ ] **Step 1: Apply the docs patch.**

````diff
diff --git a/docs/ARCHITECTURE.md b/docs/ARCHITECTURE.md
index f581179..cc62a40 100644
--- a/docs/ARCHITECTURE.md
+++ b/docs/ARCHITECTURE.md
@@ -2,8 +2,8 @@
 
 Living document: describes the system **as it is now / as currently decided**. Update it in the
 same commit as any change that alters components, threads, data flow or storage.
-Full v1 rationale: `superpowers/specs/2026-10-02-neofences-v1-design.md`. Why-decisions:
-`DECISIONS.md`.
+Current direction: `PIVOT-2026-10-04.md` (ADR-040: virtual items). Full v1 rationale (pre-pivot):
+`superpowers/specs/2026-10-02-neofences-v1-design.md`. Why-decisions: `DECISIONS.md`.
 
 ## Where NeoFences sits on the desktop
 
@@ -14,7 +14,8 @@ Full v1 rationale: `superpowers/specs/2026-10-02-neofences-v1-design.md`. Why-de
          │ NeoFences fence windows (top-level, layered, │  ← owned by Progman: stays just above
          │ accent blur; owner = Progman)                │    the desktop on Win+D; topmost in Peek
          ├──────────────────────────────────────────────┤
-         │ desktop icons — SHELLDLL_DefView             │  ← hidden in Takeover (FWF_NOICONS)
+         │ desktop icons — SHELLDLL_DefView             │  ← visible; hidden only by the optional
+         │                                              │    "Hide desktop icons" (FWF_NOICONS)
          ├──────────────────────────────────────────────┤
          │ Wallpaper Engine / Lively (WorkerW)          │
  BOTTOM  │ static wallpaper (Progman)                   │
@@ -23,145 +24,117 @@ Full v1 rationale: `superpowers/specs/2026-10-02-neofences-v1-design.md`. Why-de
  Pre-24H2: separate top-level WorkerW windows. NeoFences must not depend on either layout.
 ```
 
+## Virtual items (ADR-040, ADR-041)
+
+A fence holds **virtual items**: NeoFences' own records (`Core.Items.VirtualItem`: id, target, own name, own icon,
+arguments, run as administrator, note) pointing at a file, folder, app, website or special item. They live in
+`items.json`, one list per fence id; `config.json` holds the fences, settings and layouts. **No NeoFences action touches
+a target**: a drop creates items (the drop answers as a link, never a move), delete removes the item, dragging out
+offers a copy only, rename/icon/path change only the item. Windows' own item menu (where a real delete or rename can
+happen) is behind Shift+right-click, under a line saying it acts on the real file.
+
+```
+ drop (Explorer, desktop, browser) ─► ShellDragDrop.FenceDropTarget ─► FenceHost.OnTargetsDropped ─► ItemEdits.Add
+ drag between fences ──────────────► (CurrentDrag keys) ────────────► FenceHost.OnItemsDropped ──► ItemEdits.Move / Duplicate
+ Properties / Add item… / Locate… ─► ItemPropertiesWindow, PathPicker ► ItemEdits.Replace / Add
+ _items (ItemsDocument) ─► FenceHost.RefreshWindow ─► ShownItem[] ─► FenceWindow.SetItems ─► IconLoader (own name/icon win)
+ FolderWatcher (≤ 64 parent folders, WatchPlan) ─► rename ─► ItemEdits.Retarget (all fences)
+                                                └► change ─► TargetProbe.Check (off the UI thread) ─► TargetState ─► fence refresh (≤ 1 / 2 s)
+ SaveNow ─► ConfigStore.Save(config) then ItemStore.Save(Prune(items))     (config first: ADR-041)
+```
+
 ## Layers
 
 ```
 ┌─────────────────────────── NeoFences.App (WPF) ───────────────────────────┐
-│  TrayIcon · SettingsWindow · FenceWindow (one per fence) · FenceViewModel │
-│  DrawFenceOverlay                                                         │
+│ FenceHost (+ .Items .Watching .Library .Appearance .Updates)              │
+│ FenceWindow · FenceItemView · IconLoader · ItemPropertiesWindow           │
+│ MissingItemWindow · SettingsWindow · LibraryLister · DrawFenceOverlay     │
 └──────────────┬────────────────────────────────────────────────────────────┘
 ┌──────────────▼─────────── NeoFences.Shell (Win32/COM, CsWin32) ───────────┐
-│ DesktopLayer · DesktopIcons · ShellItems · ShellActions · InputHook       │
-│ GameDetector · Displays · Watchdog                                        │
+│ DesktopHost · DesktopIcons · ShellItems · ShellItemMenu · ShellDragDrop   │
+│ TargetProbe · PathPicker · IconPicker · FolderWatcher · DesktopMouseHook  │
+│ GameDetection · GameScanners · Monitors · TrayIcon · Watchdog             │
 └──────────────┬────────────────────────────────────────────────────────────┘
 ┌──────────────▼─────────── NeoFences.Core (pure C#) ───────────────────────┐
-│ Model · LayoutEngine · ConfigStore · Membership                           │
+│ Items (VirtualItem, ItemEdits, TargetChecks, WatchPlan, RefreshThrottle)  │
+│ Model · Config (ConfigStore, ItemStore, JsonStore) · Layouts · Library    │
 └───────────────────────────────────────────────────────────────────────────┘
 ```
 
 | Component | Responsibility | Key APIs |
 |---|---|---|
-| Core/Model | Fence, FenceSource (Desktop / Portal), ItemRef, Layout, Settings | — |
+| Core/Items | `VirtualItem` + `ItemIcon`; `ItemKinds` (path / website / special, typed targets cleaned); `ItemsDocument` (fence id → items); `ItemEdits` (add with same-fence de-dup, move, Ctrl-duplicate, remove, replace, retarget on rename, prune, reorder, repair); `TargetChecks` (OK / Missing / Unavailable from the disk's answers); `WatchPlan` (≤ 64 parent folders, busiest first); `RefreshThrottle` (≤ 1 refresh per fence per 2 s) | — |
+| Core/Model | Fence (`IsLibrary`, tabs, colours, roll-up, lock, labels, icon size), Layout, Settings (`HideDesktopIcons`), snapshots with items | — |
+| Core/Config | `JsonStore<T>`: atomic write (`SafeFile`), `.bak`, daily backups (10), corrupt recovery, newer-schema read-only; `ConfigStore` (config.json, schema 5; older schemas start fresh and are kept as `backups\pre-schema-5-config.json`), `ItemStore` (items.json, schema 1), `SnapshotStore` | System.Text.Json, File.Replace |
 | Core/LayoutEngine | display fingerprint, map/scale layouts between monitor setups, clamp, snap, smart placement of new fences (`FreeSpot`, ADR-020) | — |
-| Core/ConfigStore | load/save JSON, atomic write, backups, corrupt recovery, schema migration | System.Text.Json, File.Replace |
-| Core/Membership | item → fence assignment, Inbox fallback, reconcile on startup | — |
-| Core/FenceEdits, Core/Snapping | rename / icon size / lock; snap to 8 px gap or aligned edges during drags, tracking the unsnapped drag rect (ADR-015) | — |
-| Core/FencePlacement, Core/Lifecycle | px↔DIP placement, containing monitor; session-end policy, restart throttle | — |
-| App/InstallHooks, Shell/StartupRegistration, Core/StartupPolicy, build/pack.ps1 | Velopack installer (per user, self-contained, packId NeoFences.App, data stays in %LOCALAPPDATA%\NeoFences); uninstall hook: clean stop, icons always shown, own sign-in entry removed, data kept; sign-in entry owned by the installed copy, dev builds never take it over (ADR-019, ADR-023) | Velopack 1.2.161, vpk, HKCU Run |
-| App/FenceHost | orchestrates config, monitors, windows, debounced saves, display changes, Explorer restarts, session end | — |
-| App/FenceWindow, FenceItemView, IconLoader | fence UI: item grid (ListBox + WrapPanel, 48-DIP icons, extended selection, double-click open), Inbox first-run banner; names/icons loaded on one background STA thread (ADR-014) | WPF, frozen BitmapSource |
-| App/SystemMessageWindow | hidden top-level window: TaskbarCreated, display/work-area changes, light/dark switch (`ImmersiveColorSet`) (session end is WPF's `SessionEnding`, ADR-013) | HwndSource |
-| App/SettingsWindow, Core/RollUpExpansion | settings (Fluent, per window): General · Fences · Game mode · About and logs, Peek hotkey recorder; rolled-up fences open on hover or click; 200 ms roll-up and 150 ms quick-hide animations, off with Windows animations or in game mode (ADR-022) | WPF ThemeMode, SystemParameters.ClientAreaAnimation |
-| Shell/DesktopLayer | keep fences at desktop level (owner = Progman, re-applied on TaskbarCreated), Peek, layered accent-blur backdrop | SetWindowLongPtr(GWLP_HWNDPARENT), SetWindowCompositionAttribute, TaskbarCreated |
-| Shell/DesktopIcons | hide/show native icons | IShellWindows, IFolderView2::SetCurrentFolderFlags |
-| Shell/Watchdog | `--watchdog <pid>` mode (no WPF, no window), started detached: `WatchdogPlan` from markers — restore icons whenever `takeover-active` exists; restart after crash (3 per 10 min) or after a cancelled session end (ADR-013) | Process.WaitForExit, SM_SHUTTINGDOWN |
-| Shell/DesktopItems, DesktopWatcher | desktop listing as item refs (user + Public Desktop files, enabled special icons); live create/delete/rename → Core `DesktopChange`, overflow → full reconcile (ADR-014) | SpecialFolder, HideDesktopIcons registry key, FileSystemWatcher |
-| Shell/ShellItems | display name, icon/thumbnail pixels, open (default verb) | SHCreateItemFromParsingName, IShellItemImageFactory, GetDIBits, ShellExecute, AllowSetForegroundWindow |
-| Shell/ShellItemMenu, Shell/ShellFileOps | Windows' classic item menu (desktop IShellFolder + IContextMenu2/3 forwarding; Rename/Delete taken over); recycle and rename via IFileOperation, delete always recycles (ADR-016) | IContextMenu3, TrackPopupMenuEx, IFileOperation |
-| Shell/DesktopNamespace, Shell/ShellDragDrop | items as children of their shell folder (desktop merged view, or a Portal's folder) (shared by menu, drag data, item drop targets); drag source with the shell's IDataObject; one IDropTarget per fence: fence/Desktop items → membership only (DROPEFFECT_NONE), containers → their own drop target, other files → the Desktop folder's drop target + expected arrivals (ADR-017) | SHDoDragDrop, RegisterDragDrop, IDropTargetHelper |
-| Shell/FolderPicker, Shell/FolderItems (+FolderWatcher), App/PortalState, Core/ItemSorting | Portal fences: Windows' folder dialog, listing (visible entries) and live changes incl. the folder itself vanishing, browsing (runtime only), name/type/newest-first sorting (ADR-018) | IFileOpenDialog, FileSystemWatcher |
-| Shell/DesktopMouseHook (+DesktopWindows), Core/Input/DesktopGestureTracker | desktop double-click (quick-hide), right-drag (draw a fence), Peek click-outside; S2 swallow + marked replay of a plain right-click; desktop / fence / native-icon hit tests (ADR-020) | WH_MOUSE_LL (own thread), WindowFromPoint, SendInput, UI Automation (icon hit test, UI thread) |
-| Shell/GlobalHotkey, Core/Input/Hotkey, App/DrawFenceOverlay | Peek hotkey (default Ctrl+Alt+Space; Esc only while peeking); the click-through rectangle while drawing a fence (ADR-020) | RegisterHotKey |
-| Shell/GameDetection (+ForegroundWatcher), Core/GameModePolicy | game mode: notification state BUSY / D3D full screen while an app (not desktop, taskbar, NeoFences) is in front; checked on foreground changes and 1 / 2.5 / 5 s later (ADR-021) | SHQueryUserNotificationState, SetWinEventHook (out-of-context) |
-| Shell/TrayIcon (+TrayMenu, SessionNotifications), Core/RunState | tray icon and menu (New fence · Quick-hide · Peek · Pause · Exit); Pause; one rule for Takeover / quick-hide / Pause / game mode → fences, icons, hook, deferred shell work; hook re-install on unlock (ADR-021) | Shell_NotifyIcon, TrackPopupMenuEx, LoadImage, WTSRegisterSessionNotification |
-| Shell/Displays | monitors, work areas, DPI | EnumDisplayMonitors, GetDpiForMonitor |
+| Core/FenceEdits, FenceTabs, Snapping | rename / icon size / lock / colours / new / delete fence; tabs (ADR-029); snap to 8 px gap or aligned edges during drags (ADR-015) | — |
+| Core/FencePlacement, Core/Lifecycle | px↔DIP placement, containing monitor; `RunState` (hide icons, quick-hide, Pause, game mode), session-end policy, restart throttle, watcher backoff | — |
+| Core/Library | Valve/Epic parsing, `GameCatalog` merge, `LibraryFiles` plan, `GameLaunchers.LauncherOf` (ADR-032) | — |
+| App/FenceHost | orchestrates config + items, monitors, windows, debounced saves (both files, config first), display changes, Explorer restarts, session end, snapshots, tray | — |
+| App/FenceHost.Items | open (arguments, run as administrator; a missing target asks Locate… / Remove), NeoFences' item menu, Windows' menu on Shift+right-click, Properties, Add item…, Locate…, drops, drag-out, one-time sort, item pictures copied into `icons\` | WPF ContextMenu, Clipboard |
+| App/FenceHost.Watching | watched folders (`FolderWatcher` + removal notices), rename-follow, target checks (start, 5 min, fence shown, Refresh, drive arrival/removal), per-fence refresh throttle, game-mode deferral | DispatcherTimer |
+| App/FenceWindow, FenceItemView, IconLoader | fence UI: item grid (ListBox + WrapPanel), keys (Enter, Del = remove, F2 / Alt+Enter = Properties), drop caret, Missing badge / Unavailable dimming, empty-fence hint, `WM_DEVICECHANGE` → drives changed; names/icons on two STA threads, an item's own name and icon win, websites show the default browser's icon | WPF, frozen BitmapSource |
+| App/ItemPropertiesWindow, MissingItemWindow | Properties and Add item… (name, target + Browse, live Found / Missing / Drive not connected, arguments, run as admin, icon from a file / a picture / reset, note); the missing-target question | WPF (Fluent) |
+| App/LibraryLister, FenceHost.Library | the Game Library fence lists NeoFences' own `library\` folder (former Portal machinery) as tiles; scans launchers, game folders and Desktop game shortcuts (ADR-032) | FileSystemWatcher |
+| App/SettingsWindow | General (Start with Windows, Hide desktop icons while NeoFences runs, Peek hotkey) · Fences · Appearance · Snapshots · Game Library · Game mode · Updates · About | WPF ThemeMode |
+| App/InstallHooks, Shell/StartupRegistration, Core/StartupPolicy, build/pack.ps1 | Velopack installer and auto-update (ADR-023, ADR-039) | Velopack 1.2.161 |
+| Shell/ShellDragDrop | drag out: a shell data object of the targets (absolute ID lists) or URLs, copy/link only; one IDropTarget per fence: fence items (keys) → move / Ctrl-duplicate, outside files (CF_HDROP) or one website (UniformResourceLocatorW / text) → new items; never a move effect | SHDoDragDrop, SHCreateDataObject, SHParseDisplayName, RegisterDragDrop, IDropTargetHelper |
+| Shell/ShellItems | display name, icon/thumbnail pixels, an icon from a file (index), the default browser's path, open (arguments, `runas`, the target's folder as working folder), show in folder | SHCreateItemFromParsingName, IShellItemImageFactory, SHDefExtractIcon, AssocQueryString, ShellExecute |
+| Shell/ShellItemMenu, DesktopNamespace | Windows' classic item menu for one target (Shift+right-click, with a disabled header line) and for the library's shortcuts (custom commands, Delete handed back) | IContextMenu3, TrackPopupMenuEx, InsertMenu |
+| Shell/TargetProbe | a target's state off the UI thread; shares time out after 2 s (Unavailable) | File/Directory.Exists |
+| Shell/PathPicker, IconPicker | Windows' Open dialog (file with filters / folder, start folder); Windows' Change Icon dialog | IFileOpenDialog, PickIconDlg |
+| Shell/FolderItems (+FolderWatcher) | folder listing (library), sort facts, watcher with `Renamed(old, new)` and the folder's own rename/removal via its parent | FileSystemWatcher |
+| Shell/DesktopIcons, Watchdog | hide/show native icons (option); `--watchdog <pid>`: restore icons whenever `icons-hidden` exists, restart after a crash (3 per 10 min) or a cancelled session end (ADR-005, ADR-013) | IShellWindows, IFolderView2 |
+| Shell/DeviceRemovalNotice | lets go of a watched drive Windows wants to remove ("Safely remove") | RegisterDeviceNotification |
+| Shell/ShellFileOps | recycles NeoFences' own snapshot files (never items or targets) | IFileOperation |
+| Shell/DesktopItems, SpecialIconNotifications | the Desktop listing for the library's game shortcuts; Recycle Bin full/empty → special icons reload | HideDesktopIcons registry key, SHChangeNotifyRegister |
+| Shell/DesktopHost, FenceWindowChrome | fences at desktop level (owner = Progman, re-applied on TaskbarCreated), Peek, accent blur, rounded corners | SetWindowLongPtr(GWLP_HWNDPARENT), SetWindowCompositionAttribute |
+| Shell/DesktopMouseHook (+DesktopWindows), Core/Input | desktop double-click (quick-hide), right-drag (draw a fence), Peek click-outside (ADR-020) | WH_MOUSE_LL (own thread), UI Automation |
+| Shell/GlobalHotkey, GameDetection, TrayIcon, Monitors | Peek hotkey; game mode (ADR-021); tray; monitors and DPI | RegisterHotKey, SHQueryUserNotificationState, Shell_NotifyIcon, EnumDisplayMonitors |
 
 ## Threads
 
 | Thread | Runs |
 |---|---|
-| UI (STA) | WPF, all fence windows, shell COM objects, WinEvent hook callbacks |
+| UI (STA) | WPF, all fence windows, shell COM objects for menus and drops, WinEvent hook callbacks |
 | InputHook | `WH_MOUSE_LL` + its own message loop; posts gestures to UI thread. Must return fast (< 1 ms). |
-| Icon loaders (2× STA) | `IconLoader`: display names + `IShellItemImageFactory`; frozen bitmaps marshalled to UI; only the newest size is applied |
-| Shell worker (STA) | `ShellWorker`: open, recycle, rename in order (Windows' dialogs and slow handlers never freeze the fences, ADR-026) |
-| Watcher callbacks (thread pool) | `DesktopWatcher` events and the "Desktop icon settings" registry notice, marshalled to UI with `Dispatcher.BeginInvoke`; Recycle Bin notices arrive as window messages |
+| Icon loaders (2× STA) | `IconLoader`: display names, icons from files and pictures, `IShellItemImageFactory`; frozen bitmaps marshalled to UI; only the newest request per item is applied |
+| Open threads (STA, one per open) | `ShellWorker.RunAlone`: check the target, then ShellExecute (a UAC prompt or a slow share holds up nothing else) |
+| Shell worker (STA) | `ShellWorker`: recycling snapshot files |
+| Thread pool | `TargetProbe` checks (shares with a 2 s timeout), opening watchers, `FolderWatcher` events (marshalled to UI with `Dispatcher.BeginInvoke`), library scans (own STA thread) |
 | Watchdog process | separate process, same exe, waits on main PID |
 
 ## Data
 
 `%LOCALAPPDATA%\NeoFences\`
-- `config.json` (+ `.bak`, `.tmp` transient) — schema in spec §5
-  - shape: `{ schemaVersion, settings, fences[], layouts: { <fingerprint>: { monitors: { <id>: { workWidth, workHeight } }, fences: { <fenceId>: { monitor, x, y, w, h } } } }, lastLayoutFingerprint }`
-- `backups\config-<yyyyMMdd>.json` (keep 10)
-- `logs\neofences-<date>.log` (keep 7)
-- `clean-shutdown-<pid>` marker consumed by the watchdog
-- `session-ending-<pid>` — written inside WPF's SessionEnding; the watchdog restarts NeoFences only if the session continues
-- `takeover-active` — exists while NeoFences has hidden the icons; the watchdog restores only then
+- `config.json` (+ `.bak`, `.tmp` transient) — schema 5
+  - shape: `{ schemaVersion, settings: { hideDesktopIcons, peekHotkey, … }, fences: [ { id, title, isLibrary, iconSize, rolledUp, locked, labels, tabs, activeTab, tabColor, customColor } ], layouts: { <fingerprint>: { monitors, fences: { <fenceId>: { monitor, x, y, w, h } } } }, library, lastLayoutFingerprint }`
+- `items.json` (+ `.bak`) — schema 1 (ADR-041)
+  - shape: `{ schema, fences: { <fenceId>: [ { id, target, name, icon: { file, index } | { image }, arguments, runAsAdmin, note } ] } }`
+  - lists of fences the config does not have are dropped at the next save — except in a session whose config is only a fallback (fresh or read-only)
+- `icons\<itemId>-<guid>.png` — pictures chosen as item icons (≤ 256 px); unused ones deleted at start
+- `backups\config-<yyyyMMdd>.json`, `backups\items-<yyyyMMdd>.json` (keep 10 each), `backups\pre-schema-5-config.json`
+- `snapshots\*.json` — fences, layouts and items; `before-restore.json`
+- `library\` — the Game Library's shortcuts and `index.json`
+- `logs\neofences-<date>.log`, `logs\watchdog-<date>.log` (keep 7)
+- `clean-shutdown-<pid>`, `session-ending-<pid>` — markers consumed by the watchdog
+- `icons-hidden` — exists while NeoFences has hidden the desktop icons; the watchdog restores only then
 - `watchdog-restarts.txt` — restart timestamps for the 3-per-10-min limit
-- `logs\watchdog-<date>.log` (keep 7)
 
 ## Status
 
-M0 spike (ADR-011), M1 Core (71 → 87 tests), **M2a complete**: NeoFences.Shell + NeoFences.App host layered,
-Progman-owned, blurred fences, verified on the real desktop (`research/m2a-fence-host.md`). **M2b complete**: desktop items in
-fences — start → `Reconcile(DesktopItems.Enumerate())` → `FenceWindow.SetItems` → `IconLoader`; watcher event →
-`FenceMembership.Apply` → `SetItems`; Inbox first-run banner (ADR-014, `research/m2b-desktop-items.md`). **M2c complete**: fence menu (rename, icon size, lock,
-delete → Inbox), snapping in `WM_MOVING`/`WM_SIZING`, keyboard (when the fence is active), thin scrollbar, label shadow,
-light/dark following Windows via `Shell/SystemTheme` with the light veil drawn by the fence (ADR-015,
-`research/m2c-fence-interactions.md`). **M3a complete**: Windows' item menu, F2 rename, Del → Recycle Bin, Windows' open
-error UI, safe-save memory in `FenceMembership.Apply` (ADR-016, `research/m3a-item-actions.md`). **M3b complete**: drag-drop within/between/out of fences, onto folders and Recycle Bin, in from Explorer (pending user check K4/K5), rubber band;
-`FenceMembership.MoveItems` / `ExpectArrivals` (ADR-017, `research/m3b-drag-drop.md`). **M4 complete**: Portal fences — live folder view, newest first, browse inside with Back, Sort by for all fences, drops
-to/from Portals as real file moves (ADR-018, `research/m4-portals.md`).
-**M5 complete**: desktop gestures — quick-hide (fences + icons), draw a fence by right-drag, Peek, roll-up with
-hover, smart placement (ADR-020, `research/m5-desktop-gestures.md`).
-**M6a complete**: game mode idle (hook removed, Peek ignored, Desktop/Portal changes deferred), tray icon and menu,
-Pause, mouse-hook hardening, app icon (ADR-021, `research/m6a-game-mode-tray.md`).
-**M6 complete** (M6b): settings window, roll-up click mode, roll-up and quick-hide animations (ADR-022,
-`research/m6b-settings-animations.md`).
-**v1.0 complete** (M7): Velopack installer with an uninstall hook that always restores the icons (ADR-023,
-`research/m7-installer.md`). Next: the v2 backlog (FEATURES) and the ROADMAP carry-overs.
-**M8a complete** (v1.1 batch 1): reliability carry-overs, and fence corners rounded by Windows 11 (the accent blur
-ignores window regions; ADR-024, `research/m8a-reliability.md`).
-**M8b complete** (v1.1 batch 2): icon-only fences whose names pop up in an overlay inside the fence window (never a
-Popup: fences stay at the bottom), optional shortcut arrows, a title double-click NeoFences recognises itself (the first
-one after another app works), in-place item diff, key-cap hotkeys (ADR-025, `research/m8b-fence-ui.md`).
-**M8c complete** (v1.1 batch 3): a shell worker for open/recycle/rename, item-menu details, live special icons, drag-drop
-details, coalesced watcher recovery, a single-instance wait after Setup (ADR-026, `research/m8c-shell-dragdrop.md`).
-**M17 complete** (v1.8.0, public releases): `Core.Updates.UpdatePolicy` decides when to check; `FenceHost.Updates` runs
-Velopack's `UpdateManager` against the public GitHub releases (a local folder via `NEOFENCES_UPDATE_SOURCE` for
-rehearsals), downloads quietly, offers "Restart to update" and applies after the clean exit; `.github/workflows` builds
-every push to main and every pull request (CI) and turns version tags into draft releases (ADR-039).
-**M16 complete** (v1.7.1, polish): one title font for all fences (`Fence.TitleFont` removed; the fence menu keeps
-Colour only); Accent-edge titles pushed until readable; review carry-overs closed (ADR-038).
-**M14 complete** (v1.7.0, appearance): `Core.Appearance.FenceLook` resolves each fence's veil, outline, title ink, strip,
-bar and font from `Settings.Appearance`, the fence's colour and font and the wallpaper accent; `AccentColor` picks the
-wallpaper's strongest vivid hue; `Shell.WallpaperSources` reads Wallpaper Engine's preview, Windows' wallpaper or accent
-(no screen capture); `FenceHost.Appearance` restyles on changes and re-reads the accent on wallpaper changes; config
-schema 3 (ADR-036).
-**M13c complete** (v1.6.2, fence and Settings UX): a title right-click opens the fence menu and a header click opens a
-rolled-up box; snapshot results show in the Settings card (failures also as warning notices); tray snapshot labels are
-one safe line (`Snapshots.MenuLabel`); Settings keeps focus on refreshes; hotkey labels use the layout's character
-(`KeyboardLayout`); Desktop shortcut changes rescan the library (ADR-035).
-**M13b complete** (v1.6.1, library polish): Epic lists games only and Xbox names resolve through the package's
-resources; a Desktop shortcut into a launcher's game folder merges with it and is copied into the library as is;
-scans reuse remembered programs and game folders are watched for folders only; hidden games live in the library index
-with their names (`LibraryState.Hidden`); the index is written through and NeoFences' temp names are swept; one
-pre-schema-2 config copy is kept in `backups\` (ADR-034).
-**M13a complete** (v1.6.0, data safety): config schema 2, stamped by the normalizer, so v1.5 and older never save over
-tabs, rules or the library; snapshots from a newer version or over 16 MB are refused; `LibraryFiles.Settle` keeps the
-library index true after failed writes; watchers report the folder they hold (`FolderWatcher.HeldFolder`) for removal
-notices; "--exit" is a manual-reset signal that a copy waiting to start also obeys (ADR-033).
-**M12 complete** (v1.5, game library): a `library` fence kind (one at most) shows NeoFences' own
-`%LOCALAPPDATA%\NeoFences\library\` (one shortcut per game + `index.json`) through the Portal machinery as 2:3 tiles;
-Core `Library` (Valve/Epic parsing, `GameCatalog` merge, `LibraryFiles` plan), Shell `GameScanners` / `ShellLinks` /
-`LibraryWriter`, FenceHost scans on an STA thread at start, on launcher/game-folder changes, on Refresh (ADR-032).
-**M11 complete** (v1.4, rules): `config.json` holds an ordered `rules` list; Core `Rules` matches item facts (type,
-game launcher or folder, name, date, size) and files matched items into desktop fences (membership only); Shell
-`ItemFactsReader` reads facts and shortcut targets on the shell worker; FenceHost files new Inbox items after watcher
-"created" events, reconciles and game-mode replays, and runs "Apply rules now" behind a "Before restore" snapshot (ADR-031).
-**M10 complete** (v1.3, snapshots): `%LOCALAPPDATA%\NeoFences\snapshots\` holds one file per snapshot (Core
-`SnapshotStore`, temp-then-swap); Core `Snapshots.Restore` applies one to the config against the live desktop listing;
-the host writes "Before restore" first and rebuilds with `SyncBoxes` (ADR-030, `research/m10-snapshots.md`).
-**M9 complete** (v1.2, fence tabs): fences combine into boxes. One window per box: `FenceWindow.FenceId` is the shown
-tab (items, icon size, labels, sort, rename, delete, drops), `BoxId` the host fence (placement, move, roll-up, lock);
-`FenceHost.SyncBoxes` keeps windows in line with boxes; Core `FenceTabs` owns every tab edit (ADR-029,
-`research/m9-fence-tabs.md`).
-**M8d complete** (v1.1 batch 4): Portals let go of a drive Windows wants to remove (device notices on the message
-window), Portal watcher backoff, Enter on several items (ADR-027, `research/m8d-portal-details.md`). The M8 carry-over
-batches are done; next: the v1.1.1 release decision, then the v2 backlog (FEATURES).
-
-Core contracts M2 relies on (and M9: `FenceTabs` edits return a new config, the same instance for most no-ops — Merge into its own box, SetActive, Leave and Detach outside a box; `Repair` leaves untouched fences as the same instances): no-op membership calls return the **same** config instance (records compare
-lists by reference, so use `ReferenceEquals`, not `==`); `ConfigStore.Load` may return `IsReadOnly` (newer
-schema or locked file) and `Save` then returns false; `Reconcile` keeps memberships under the folders
-the shell could not list (or all of them for an empty listing) and sets `ReconcileReport.Suspicious` (log it; ADR-014); `LayoutEngine.Resolve`
-throws `ArgumentException` for unusable monitor work areas (FenceHost skips that resolve).
+**0.9.0 (M18, virtual items)**: fences hold virtual items in `items.json`; drops create items (a link, never a move),
+Del removes the item, Ctrl+drag duplicates, dragging out copies; NeoFences' item menu and Properties; Windows' menu on
+Shift+right-click; targets watched (≤ 64 folders, rename-follow, Missing / Unavailable, 2 s throttle, 5-minute check,
+Refresh); "Hide desktop icons while NeoFences runs" (off by default); Takeover, Inbox, desktop membership, Rules,
+Portals and item file actions removed (ADR-040, ADR-041, `research/m18-virtual-items.md`).
+
+Pre-pivot history (v1.x dev builds, the M0–M17 notes in `research/` and SESSION-LOG) describes the Takeover model; read
+it as history. Kept from it: fences, tabs (M9), snapshots (M10), the Game Library (M12), roll-up, lock, Peek, game mode,
+Appearance (M14/M16), the installer and auto-update (M7/M17).
+
+Core contracts the App relies on: edits return a new document/config, the **same** instance when nothing changed (records
+compare lists by reference, so use `ReferenceEquals`); `ConfigStore.Load` / `ItemStore.Load` may return `IsReadOnly`
+(newer schema or locked file) and `Save` then returns false; `LayoutEngine.Resolve` throws `ArgumentException` for
+unusable monitor work areas (FenceHost skips that resolve).
diff --git a/docs/FEATURES.md b/docs/FEATURES.md
index 2f613b2..f2d8e73 100644
--- a/docs/FEATURES.md
+++ b/docs/FEATURES.md
@@ -1,8 +1,9 @@
 # Features
 
 Parity with Stardock Fences 5/6 plus NeoFences extras. Status: `—` not started · `wip` ·
-`done` · `cut` (with reason). Target: release in which it ships. Update status with the commit
-that changes it.
+`done` · `cut` (with reason) · `parked` (ADR-040: may come back as dynamic fences). Target: release in which it ships.
+Update status with the commit that changes it. Versions: 0.x since the pivot (ADR-040); "v1.x" in older rows are
+pre-reset dev builds.
 
 Sources for Fences 6: stardock.com news posts "Now Announcing: Fences 6", "Fences 6 Beta 2",
 "Releasing Fences 6.20" (checked 2026-10-02).
@@ -11,23 +12,24 @@ Sources for Fences 6: stardock.com news posts "Now Announcing: Fences 6", "Fence
 
 | Feature | Fences | Target | Milestone | Status | Notes |
 |---|---|---|---|---|---|
-| Fences holding desktop icons | 1+ | v1 | M2 | done | Takeover model, ADR-002; M2b (ADR-014) |
-| Inbox / default fence for new items | — | v1 | M2 | done | Fences uses "unfenced desktop"; we use Inbox |
+| Fences holding desktop icons (Takeover) | 1+ | — | M2 | parked | ADR-040: fences hold virtual items instead; the desktop stays as Windows shows it |
+| Virtual items: own name, icon, path, arguments, run as admin, note | — | 0.9 | M18 | done | ADR-040/041; Properties dialog; the same target in several fences |
+| Add item… (file, folder, app, website) | — | 0.9 | M18 | done | fence menu; Browse or a typed path / web address |
+| Inbox / default fence for new items | — | — | M2 | parked | no auto-fill (ADR-040) |
 | Move / resize fences | 1+ | v1 | M2 | done | snap 8 px gap / edge alignment (M2c) |
 | Scrolling inside fences | 2+ | v1 | M2 | done | thin scrollbar (M2c) |
 | Per-fence icon size | 5 | v1 | M2 | done | 32/48/64/96 (M2c) |
 | Thumbnails for images/videos | — | v1 | M2 | done | IShellItemImageFactory (M2b) |
-| Open / select | 1+ | v1 | M2 | done | double-click opens in front; Ctrl/Shift multi-select (M2b) |
-| Keyboard navigation in fences | 1+ | v1 | M2c | done | arrows, Ctrl+A, Enter once the fence is active; not after clicking from another app (ADR-015, re-checked in M8b); F2 rename and Del recycle since M3a |
-| Rename / delete fence | 1+ | v1 | M2c | done | delete moves items to the Inbox |
-| First-run prompt in the Inbox | — | v1 | M2 | done | asks once whether to hide desktop icons (user choice 2026-10-02) |
-| Shell context menu on items | 1+ | v1 | M3 | done | Windows' classic menu incl. extensions; Shift = extended (M3a, ADR-016) |
-| Rename / delete to Recycle Bin | 1+ | v1 | M3 | done | F2 in place; Del and Shift+Del always recycle (user choice, M3a) |
-| Drag-drop in/out/between fences | 1+ | v1 | M3 | done | M3b, ADR-017; drops from Explorer pending user check (K4/K5) |
+| Open / select | 1+ | v1 | M2 | done | double-click / Enter; arguments and run as admin since 0.9 |
+| Keyboard navigation in fences | 1+ | v1 | M2c | done | arrows, Ctrl+A, Enter; Del removes the item, F2 / Alt+Enter Properties (0.9) |
+| Rename / delete fence | 1+ | v1 | M2c | done | delete removes the fence and its items, asked first (0.9); files never touched |
+| Item menu | 1+ | 0.9 | M18 | done | NeoFences' own safe menu; Windows' full menu on Shift+right-click, labelled "acts on the real file" |
+| Rename / delete items | 1+ | 0.9 | M18 | done | rename = the item's own name; Del = remove the item (no file operation, ADR-040) |
+| Drag-drop in/out/between fences | 1+ | 0.9 | M18 | done | drops create items (link, never move); Ctrl+drag duplicates; dragging out copies |
+| Missing / unavailable targets | — | 0.9 | M18 | done | watched (≤ 64 folders), renames followed, Locate… / Remove, per-fence Refresh |
 | Rubber-band selection | 1+ | v1 | M3 | done | M3b; Ctrl adds |
-| Folder Portals | 3+ | v1 | M4 | done | live view, newest first by default (M4, ADR-018); Safely Remove works while shown (M8d, ADR-027) |
-| Portal subfolder navigation | 3+ | v1 | M4 | done | browse inside with Back/Backspace; Ctrl+double-click opens Explorer (user choice) |
-| Sort (name/type/date) | 2+ | v1 | M4 | done | Portals live; desktop fences one-time; type = extension |
+| Folder Portals | 3+ | — | M4 | parked | ADR-040: later as dynamic collections (M20) |
+| Sort (name/type/date) | 2+ | v1 | M4 | done | one time; by the names shown (0.9) |
 | Draw fence by right-drag on desktop | 1+ | v1 | M5 | done | S2 keeps the plain right-click menu (ADR-020) |
 | Quick-hide (double-click desktop) | 1+ | v1 | M5 | done | fences + desktop icons (user choice); a double-click on a native icon opens it |
 | Peek (fences over windows) | 3+ | v1 | M5 | done | Ctrl+Alt+Space default; ends on hotkey, Esc, click outside, opening an item |
@@ -36,31 +38,34 @@ Sources for Fences 6: stardock.com news posts "Now Announcing: Fences 6", "Fence
 | Smart placement spacing | 6.20 | v1 | M5 | done | first free spot with 8 px gaps (`FreeSpot`) |
 | Per-monitor layout, survives resolution changes | 2+ | v1 | M1/M2 | wip | M1 Core + M2a placement/persistence done; real multi-monitor untested |
 | Lock fences | 3+ | v1 | M2 | done | (M2c) |
-| Icon-only fences (name on hover) | 6 | v1.1 | M8b | done | per fence (menu → Labels) + Settings default and "apply to all"; the name pops up under the hovered or selected icon (ADR-025) |
+| Icon-only fences (name on hover) | 6 | v1.1 | M8b | done | per fence (menu → Labels) + Settings default and "apply to all" (ADR-025) |
 | Shortcut arrows | — | v1.1 | M8b | done | Settings switch, off by default; drawn by NeoFences (ADR-025) |
 | Light/dark, acrylic look | 5 | v1 | M2/M6 | done | follows Windows live; label shadow (M2c, user choice) |
-| Settings window, tray | 1+ | v1 | M6 | done | tray + Pause (M6a); Fluent settings window, hotkey recorder (M6b, ADR-022); Appearance section in v1.7 (M14) |
+| Settings window, tray | 1+ | v1 | M6 | done | tray + Pause (M6a); Fluent settings window (M6b); Appearance (M14) |
+| Hide desktop icons while NeoFences runs | — | 0.9 | M18 | done | Settings switch, off by default; restored on exit, crash and kill (watchdog) |
 | Start with Windows | 1+ | v1 | M6 | done | brought forward for power-loss recovery (ADR-019); fence-menu toggle |
-| Installer (per user, no admin), uninstall restores icons and keeps data | — | v1 | M7 | done | Velopack Setup.exe; auto-update from GitHub Releases since v1.8 (ADR-023, ADR-039) |
-| Rules auto-sort | 3+ | v1.4 | M11 | done | type, game (launchers or a folder of mine), name, date, size; Apply rules now can be undone (ADR-031) |
+| Installer (per user, no admin), uninstall restores icons and keeps data | — | v1 | M7 | done | Velopack Setup.exe; auto-update from GitHub Releases (ADR-023, ADR-039) |
+| Rules auto-sort | 3+ | — | M11 | parked | ADR-040: later as auto-collect rules (M20) |
 | Fence tabs (+ drop onto tab header, tab color) | 6 | v1.2 | M9 | done | combine by dragging a title onto another; drag a tab out to split (ADR-029) |
-| Desktop pages | 3+ | v2 | — | — | |
-| Snapshots (save/restore layouts) | 2+ | v1.3 | M10 | done | tray + Settings; a restore keeps newer items and can be undone (ADR-030) |
-| Desktop icon color tint | 6 | v2 | — | — | |
-| Per-fence colors / custom title fonts | 3+ | v1.7 | M14 | done | 8 swatches + Custom… (Windows' colour picker); 3 colour styles; one title font for all fences, in Settings (v1.7.1, ADR-038) |
+| Desktop pages | 3+ | — | — | — | |
+| Snapshots (save/restore layouts) | 2+ | v1.3 | M10 | done | tray + Settings; fences, places and items (0.9); can be undone (ADR-030) |
+| Desktop icon color tint | 6 | — | — | — | |
+| Per-fence colors / custom title fonts | 3+ | v1.7 | M14 | done | 8 swatches + Custom…; 3 colour styles; one title font for all fences (ADR-038) |
 
 ## NeoFences extras
 
 | Feature | Target | Status | Notes |
 |---|---|---|---|
-| Game-mode idle (fullscreen + borderless detection, mouse hook removed) | v1 (M6) | done | M6a: notification state + app in front; Peek ignored; shell work deferred (ADR-021) |
-| Watchdog: icons always restored | v1 (M0/M2) | wip | ADR-005/012: M2a host done (detached, takeover-active marker); sign-out (C8) pending |
-| Live blur over Wallpaper Engine | v1 (M0/M2) | wip | ADR-011: layered + accent blur, M2a host done; user approved the current tint |
+| Game-mode idle (fullscreen + borderless detection, mouse hook removed) | v1 (M6) | done | M6a; target checks and renames wait for the game's end (0.9) |
+| Watchdog: icons always restored | v1 (M0/M2) | done | when "Hide desktop icons" is on (icons-hidden marker); sign-out (C8) pending |
+| Live blur over Wallpaper Engine | v1 (M0/M2) | wip | ADR-011: layered + accent blur; user approved the current tint |
 | Blur tint preference (lighter/darker) | v1.7 (M14) | done | background strength slider, one value per Windows tone (ADR-036) |
-| Search palette across all fences | v1.8 (M15) | parked | built on branch `m15-search-palette`, not merged (user choice 2026-10-04: not needed yet) |
-| Game Library fence (Steam/Epic/GOG/Ubisoft Connect/EA, cover art) | v1.5 | M12 | done: launchers, Xbox, game folders, Desktop game shortcuts; Steam posters; hide; live rescans (ADR-032) |
+| Search palette across all fences | — | parked | built on branch `m15-search-palette` (local history bundle only), not merged |
+| Game Library fence (Steam/Epic/GOG/Ubisoft Connect/EA, cover art) | v1.5 | done | launchers, Xbox, game folders, Desktop game shortcuts; a game dragged into a fence becomes an item (0.9) (ADR-032) |
+| Store/UWP apps as items | M19+ | — | not files: need shell item lists and AppsFolder launch |
+| Dynamic collections (read-only folder views, auto-collect rules) | M20 | — | replaces Portals and Rules (ADR-040) |
 | Theme packs: "Nanosuit" (Crysis HUD), "Animus" (Assassin's Creed) | v2 | — | |
 | Custom Win11-style compact context menu | v2 | — | |
 | Wallpaper-adaptive accent color | v1.7 (M14) | done | Wallpaper Engine preview → Windows wallpaper → Windows accent; no screen capture (ADR-036) |
-| Optional local-AI auto-sort (llama.cpp) | idea | — | rules first; only if rules fall short |
+| Optional local-AI auto-sort (llama.cpp) | idea | — | |
 | Cross-platform (Avalonia) | idea | — | only if needed; Core is portable |
diff --git a/docs/ROADMAP.md b/docs/ROADMAP.md
index 4421932..1c57108 100644
--- a/docs/ROADMAP.md
+++ b/docs/ROADMAP.md
@@ -8,11 +8,16 @@ Milestone details and exit criteria: spec §9.
 
 - [x] Decided: fences hold **virtual items**; no file operations; native icons visible (optional hide); versions 0.x; fresh GitHub repo
 - [x] Docs written (PIVOT, ADR-040, CLAUDE.md hard rules)
-- [x] Reset: local history bundle in F:projects (neo_fences-history-2026-10-04.bundle), GitHub reset to one commit (0.8.0), release/tags/branch deleted
-- [x] Installed v1.8.0 and its data removed from this PC (data backed up to F:projects
-eofences-data-backup-2026-10-04)
-- [~] **M18 — Virtual items (0.9.0)** — claimed by session 2026-10-04 m18-virtual-items
-- [ ] M19 — customization polish (Locate, Ctrl-duplicates, bulk refresh)
+- [x] Reset: local history bundle in F:\projects\ (neo_fences-history-2026-10-04.bundle), GitHub reset to one commit (0.8.0), release/tags/branch deleted
+- [x] Installed v1.8.0 and its data removed from this PC (data backed up to F:\projects\neofences-data-backup-2026-10-04)
+- [ ] **M18 — Virtual items (0.9.0)** — spec `docs/superpowers/specs/2026-10-04-virtual-items-design.md`, plan `docs/superpowers/plans/2026-10-04-m18-virtual-items.md`, ADR-040/041
+  - [x] Core: virtual items and their edits; target checks, watch plan, refresh throttle
+  - [x] Core: config schema 5, items.json (`ItemStore`, `JsonStore`), snapshots with items; Membership, Rules, Portals, Inbox and Takeover removed
+  - [x] Shell: drops create items (link, never move), drag-out copies, pickers, target probe, Windows' menu with its header line
+  - [x] App: items in fences, item menu, Properties / Add item…, Locate…, target watching, "Hide desktop icons while NeoFences runs"
+  - [x] Docs: ARCHITECTURE, FEATURES, TEST-CHECKLIST section AD
+  - [ ] Live check (TEST-CHECKLIST AD, `research/m18-virtual-items.md`), final review, merge, release 0.9.0
+- [ ] M19 — customization polish (bulk re-locate of missing items, Store/UWP apps as items, deferred M18 minors)
 - [ ] M20 — dynamic collections (read-only folder views, auto-collect rules) — later
 - Everything below this section is the pre-pivot history (v1.x = pre-reset dev builds); open items there are superseded unless M18 revives them.
 
diff --git a/docs/TEST-CHECKLIST.md b/docs/TEST-CHECKLIST.md
index e3874b5..f620a64 100644
--- a/docs/TEST-CHECKLIST.md
+++ b/docs/TEST-CHECKLIST.md
@@ -448,3 +448,40 @@ M0 spike checks. In the app they are section N (D2 → N3, D7 → N14, D8 → N1
 | AC8 | A full-screen game for over a day of uptime | no check and no notice while the game is in front |
 | AC9 | A developer build (dotnet run) | Settings → Updates says updates are off in a developer build |
 | AC10 | Push a version tag | GitHub Actions builds, tests and uploads a draft release with notes; installed copies see nothing until it is published |
+
+## AD — Virtual items (M18, ADR-040, ADR-041)
+
+Test data only in `%USERPROFILE%\Desktop\NeoFences-test\` (created and recycled by the test) and on the pendrive
+`G:\NeoFences-test\` (LOCO_DUCK). "Byte for byte" = `cmp` of the file against a copy made before.
+
+| ID | Steps | Expected |
+|---|---|---|
+| AD1 | First start with no data folder | one empty fence "Fence" with the hint "Drop files, folders or links here — or right-click → Add item…"; native desktop icons visible |
+| AD2 | Drag `a.txt` from Explorer (`Desktop\NeoFences-test`) onto the fence | cursor shows the link arrow; an item appears at the drop point; `a.txt` is still in its folder, byte for byte; no Windows dialog |
+| AD3 | Drag the Desktop's own `a.txt` (from the desktop) onto the fence | as AD2 (bug K5 gone); the icon stays on the desktop |
+| AD4 | Drop `a.txt` onto the same fence again | not added again; the existing item is selected |
+| AD5 | Drag a browser link (address-bar icon) onto the fence | a website item named after the host, with the default browser's icon; double-click opens the browser |
+| AD6 | Fence menu → Add item… → Browse ▾ → A folder… → OK | a folder item; Arguments and Run as administrator greyed in Properties |
+| AD7 | Drag an item to another fence; Ctrl+drag it back | moved (name and icon kept); Ctrl: a second item, the first stays |
+| AD8 | Drag an item out onto the desktop or into an Explorer folder | Explorer makes a copy; the original stays where it was |
+| AD9 | Select an item, Del; select three, Del | one: gone at once; three: one confirmation; the files are untouched |
+| AD10 | F2 on an item; type a name; OK | the label shows the new name; the file keeps its name |
+| AD11 | Properties → Change icon → From a file… → shell32.dll, any icon | the item shows that icon; Reset brings the target's icon back |
+| AD12 | Properties → Change icon → From a picture… (a PNG) | the item shows the picture; `%LOCALAPPDATA%\NeoFences\icons\` holds a copy ≤ 256 px |
+| AD13 | Properties of an .exe item: Arguments `-x`, Run as administrator on; open it | the UAC prompt appears; Cancel: nothing breaks (logged) |
+| AD14 | Rename `a.txt` to `b.txt` in Explorer | within 2 s every item pointing at it shows `b` (own names kept) |
+| AD15 | Delete `b.txt` in Explorer (Recycle Bin) | the item dims with the ! badge, tooltip "Missing: …"; restore it from the Recycle Bin: back to normal by itself |
+| AD16 | Item on `G:\NeoFences-test\` → Safely remove the pendrive | Windows allows the removal; the item dims, tooltip "Drive G: is not connected"; plug it back: normal again within seconds |
+| AD17 | Double-click a missing item | NeoFences' "is missing" window (Locate… / Remove from fence / Cancel), no Windows error; Locate… opens at the nearest folder that exists |
+| AD18 | Shift+right-click an item | Windows' menu with a first grey line "Windows menu — acts on the real file" |
+| AD19 | Right-click an item | Open · Run as administrator · Open file location · Copy path · Properties… · Remove from fence · the grey Shift hint |
+| AD20 | Fence menu → Refresh | names and icons reload; states re-checked |
+| AD21 | Settings → "Hide desktop icons while NeoFences runs" on; Task Manager → End task on NeoFences | icons hidden while running; back within ~5 s after the kill (watchdog) |
+| AD22 | Take a snapshot; remove an item and rename another; restore the snapshot | both items back as they were; "Undo the last restore" works |
+| AD23 | Delete a fence with items | asked first (count shown); the fence and its items go; files untouched |
+| AD24 | Game Library fence | still lists games as tiles; dragging a game into a fence makes an item of its shortcut |
+| AD25 | Kill NeoFences (Task Manager) right after adding an item; restart | the item is there (or, at worst, the fence is as before the add); no damaged files |
+| AD26 | Add item… with the target `\\neofences-nohost\share\x.txt`, then Refresh the fence and drag another fence meanwhile | the item turns Unavailable within a few seconds; the fences never freeze (checks run off the UI thread, 2 s timeout) |
+
+Sections C (Takeover parts), H, J, K, L, U and X describe the pre-pivot model (Takeover, membership, item file
+actions, Portals, Rules): parked with ADR-040, not run for 0.9.
````

  (The ROADMAP hunk expects the M18 line as Task 0's claim left it.)
- [ ] **Step 2: Commit.**
  `git add docs && git commit -m "docs: described the virtual items in architecture, features, checklist and roadmap"`

### Task 7: Live check (ask the user first unless the PC is unattended)

**Files:** `docs/research/m18-virtual-items.md` (new), `docs/TEST-CHECKLIST.md` (results stay in the research note)

- [ ] **Step 1: Ask.** "M18 is built and tested. May I run the live check now (about 15 minutes; fences, a test folder
  on the Desktop, the pendrive G:\NeoFences-test)?" Wait for yes (or the unattended standing go).
- [ ] **Step 2: Prepare.** Nothing is installed (PIVOT). If `%LOCALAPPDATA%\NeoFences` exists, copy it to the scratchpad
  first. Create `%USERPROFILE%\Desktop\NeoFences-test\a.txt` and `G:\NeoFences-test\g.txt` (only that folder on G:).
  Boxed **TEST RUNNING** banner; leave Firefox Picture-in-Picture alone; never type into Windows Terminal.
- [ ] **Step 3: Run** `dotnet run --project src/NeoFences.App` from the worktree and go through TEST-CHECKLIST AD1–AD26
  (UI Automation / PowerShell where it can drive Windows, by hand where it cannot: AD13's UAC prompt, AD16's eject).
  Compare files byte for byte with `cmp` (no `Get-FileHash` on this PC).
- [ ] **Step 4: Clean up.** `--exit` every non-installed NeoFences (the watchdog may have restarted the test build);
  recycle the test folders; restore `%LOCALAPPDATA%\NeoFences` (or remove it if it did not exist); desktop icons visible.
  Boxed **TEST COMPLETE** banner.
- [ ] **Step 5: Record.** Write `docs/research/m18-virtual-items.md`: what was built, the AD results (PASS / FAIL / N/A
  with notes), findings and their fixes (each fix: a failing test first where Core can hold it). Commit:
  `git add docs && git commit -m "docs: added the M18 live check results"`.

### After the plan (superpowers:executing-plans → finishing-a-development-branch)

Final whole-branch review on the most capable model with this plan's Review Focus; one fix pass (Critical/Important,
TDD); minors to ROADMAP M19; then ask before: merge to main, push, tag `v0.9.0`, publish the draft. Bump `<Version>` to
`0.9.0` in the release commit. Session end: SESSION-LOG entry, ROADMAP ticks, PIVOT "Next steps", hub refresh (`url`
from `CLAUDE.local.md`).
