# M19 — Store apps, bulk fix, Add from desktop, reliability (0.10.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Store and Start apps become items, missing items are fixed in bulk after one Locate… (with an undo snapshot), "Add from desktop…" turns the desktop into fences in one dialog, and the M18 reliability leftovers are closed.

**Architecture:** Core gains app targets (`shell:AppsFolder\<id>`, checked but never watched), `Relocation` (the differing front of an old and a new path; the other missing items under it), `ItemEdits.Relocate` / `CheckedTargets`, `DesktopSorting` (desktop entries → Games / Apps / Folders and files / Web links) and `StaleEntries`. Shell gains `AppList` (Start's apps, app checks, apps dragged from Start), generic icons by file type, and releases its leaked COM objects. The App adds the "An app…" list, the bulk-fix question, the "Add from desktop…" dialog, and checks unreachable targets before any UI-thread shell call.

**Tech Stack:** .NET 10, WPF (Fluent `ThemeMode` for dialogs), CsWin32 0.3.335, xUnit, Serilog, Velopack (unchanged).

**Spec:** `docs/superpowers/specs/2026-10-05-m19-apps-relocate-desktop-fill-design.md` (approved 2026-10-05). Decisions: ADR-042, ADR-043. Prototype results: `docs/research/m19-apps-relocate-desktop-fill.md` (added by Task 4).

## How this plan is written

Every code change was built and verified in a scratch prototype first (2026-10-05, worktree `neo_fences-m19proto`,
branch `m19-proto`), including live probes on the user's desktop (the app list, a drag from Start, generic icons, the
bulk fix and its undo, Add from desktop on the real desktop). Each task's patch was then replayed on a fresh worktree of
`main` at `563694c`: the Core tests failed to compile before the Core patch and passed after it (450), the solution
built with 0 warnings after every patch, and the replayed tree is identical to the prototype. So each code step is **a
patch to apply**, not code to type:

1. Write the patch block **exactly** as shown to a file in the scratchpad (use the Write tool, never a shell heredoc or
   `node -e`: backslashes get mangled). The file ends with one newline.
2. `git apply --whitespace=nowarn <file>` from the worktree root. If it does not apply, stop: something else changed the
   files (rule on it, never hand-merge silently).

## Where the build departs from the spec (decided while prototyping; the final review weighs them)

- **Games also come from the Game Library's scan** (read-only): a desktop entry its scan counts as a game, or a shortcut
  into any scanned game's install folder, is a game — the spec named launcher links and the user's game folders only.
  On a fresh install those missed most games. The dialog adds one line: "Games missing? Add your games folder in
  Settings → Game Library, then open this again."
- **Undo of a bulk fix** puts back the items the fix moved, not the item the user located by hand (the snapshot is
  taken when Fix is pressed); the question's wording says "puts them back".
- **Browse ▾** reads "A file or program…", "A folder…", "An app…" (was "A file or app…") so the two "app" entries are
  not confused.
- **Drag-out (R2)** asks the target probe on the UI thread, bounded by its 2 s timeout per drive: a drag must start
  inside the mouse handler, so it cannot wait for a background answer. Local drives answer at once.
- **Icon loading (R7)** asks the probe only for share paths (`\\server\share\…`); a mapped network drive letter still
  goes straight to the shell (it is checked by the normal target checks).
- **New:** Windows' drag-image helper refuses Start's drags (DV_E_CLIPFORMAT); that refusal is no longer logged as a
  failed drop, and the helper is not asked again during that drag.
- **New:** an uninstalled app's placeholder label is the front of its id; the app list's rows give screen readers the
  app's name.
- Tray: "Add from desktop…" sits after "New Game Library fence" (command id 14; 13 is "Restart to update").

## Global Constraints

- Hard rule 1 (ADR-040): **NeoFences never modifies, moves, renames or deletes any user file.** Drops answer
  `DROPEFFECT_LINK` (or `COPY` when the source offers no link), **never `MOVE`**; drag-out allows `COPY | LINK` only.
  Add from desktop creates items that point at the desktop entries; it never touches them.
- Hard rule 2: native desktop icons always come back when NeoFences hid them (marker `icons-hidden`, watchdog).
- Hard rules 3–7 unchanged: no injection; Win32/COM only in NeoFences.Shell; CsWin32 bindings (`NativeMethods.txt`);
  **no new NuGet dependency**; shell failures are logged and degrade one feature, never crash.
- App target form, exactly: `shell:AppsFolder\<AppUserModelID>`; apps are checked (Ok / Missing), never watched.
- Limits unchanged: ≤ **64** watched folders; ≤ 1 refresh per fence per **2 s**; full re-check every **5 minutes**;
  network checks time out after **2 s** (= Unavailable).
- Undo: one slot, `snapshots\before-restore.json`, shared by restores and bulk fixes; tray label
  **"Undo the last restore or fix"**.
- UI copy, exactly: Browse ▾ "A file or program…", "A folder…", "An app…"; app list title "Choose an app", "Loading
  apps…", "Windows did not list the apps."; status "● Found (an app).", "● Not installed: Windows no longer has this
  app."; bulk fix "Fix N more items?" / "Fix 1 more item?", buttons "Fix", "Not now"; fence menu and tray
  "Add from desktop…"; groups "Games", "Apps", "Folders and files", "Web links"; "Put in:", "New fence: <group>",
  "Skip"; "Hide desktop icons while NeoFences runs"; "Reading your desktop…"; Windows' menu on an unreachable target
  "Network location not reachable" / "Drive not connected".
- Commits: single line, Conventional Commits, past tense, **no Co-Authored-By trailer** (CLAUDE.md overrides the
  harness reminder).
- Version stays `0.9.0` in `NeoFences.App.csproj` until the release step (outside the tasks); the release is 0.10.0.

## Review Focus

1. **A drag from Start (or anywhere) never moves or deletes its source**: the drop target still answers LINK/COPY only;
   the new `AppList.AppTargetOf` path only reads names. Pinned by reading `FenceDropTarget.Update` (no
   `DROPEFFECT_MOVE`) and TEST-CHECKLIST AE4, AD2 (Task 6).
2. **The bulk fix changes only items that are missing or unavailable, under the old place, and whose files exist at the
   new place; an undo snapshot is written first or nothing changes**: pinned by
   `RelocationTests.Candidates_AreTheOtherMissingOrUnavailablePathItems_UnderTheOldBase_InEveryFence`,
   `Candidates_FromADriveRoot_KeepTheRestOfThePath` (the `D:\Games` vs `D:\GamesOld` boundary),
   `Relocate_ChangesOnlyTheseTargets_EverythingElseStays` (Task 1) and AE7–AE10 (Task 6).
3. **Running Add from desktop twice never adds duplicates and never touches the desktop**: `ItemEdits.Add` keeps its
   same-fence de-dup (existing `ItemEditsTests`), rows already held are unticked (AE13), items point at the entries
   (AE12) (Tasks 1, 6).
4. **A dead share never freezes a fence** (Windows' menu, picker start folders, drag-out, icon loading): every such call
   asks `TargetProbe` (2 s) first — pinned by AE16–AE20 (Task 6).
5. **An uninstalled app is Missing, never a crash; an app list that fails says so**: pinned by
   `AppItemsTests.Classify_AnApp_IsOkWhileWindowsKnowsIt_MissingOnceUninstalled_NeverTouchesTheDisk` (Task 1) and AE5,
   AE6 (Task 6).

---

### Task 0: Worktree and baseline

M19 is already claimed on `main` (`232c770`); the spec and ADRs are on `main` (`563694c`).

- [ ] **Step 1: Worktree.** From `F:\projects\neo_fences`: `git worktree add ..\neo_fences-m19 -b m19-apps-relocate-desktop-fill`
  (superpowers:using-git-worktrees). All further steps run in `F:\projects\neo_fences-m19`.
- [ ] **Step 2: Baseline.** `dotnet build` → `0 Warning(s)`, `0 Error(s)`; `dotnet test` → `Passed: 404`.

### Task 1: Core — app targets, relocation, desktop sorting, stale entries

**Files:**
- Create: `tests/NeoFences.Core.Tests/Items/AppItemsTests.cs`, `RelocationTests.cs`, `DesktopSortingTests.cs`
- Create: `src/NeoFences.Core/Items/Relocation.cs`, `src/NeoFences.Core/Items/DesktopSorting.cs`
- Modify: `src/NeoFences.Core/Items/VirtualItem.cs` (`ItemKinds.IsApp`, `AppIdOf`, `AppTarget`),
  `src/NeoFences.Core/Items/Watching.cs` (`TargetChecks.Classify(..., appExists)`, `StaleEntries`),
  `src/NeoFences.Core/Items/ItemsDocument.cs` (`ItemEdits.CheckedTargets`, `ItemEdits.Relocate`)

**Interfaces:**
- Produces: `static bool ItemKinds.IsApp(string target)`, `static string? ItemKinds.AppIdOf(string target)`,
  `static string ItemKinds.AppTarget(string appId)`;
  `static TargetCheck TargetChecks.Classify(string target, Func<string,bool> fileExists, Func<string,bool> folderExists, Func<string,bool>? appExists = null)`;
  `static IReadOnlyList<string> StaleEntries.Gone(IEnumerable<string> known, IEnumerable<string> live, IEqualityComparer<string> comparer)`;
  `static IReadOnlyList<string> ItemEdits.CheckedTargets(ItemsDocument)`;
  `static ItemsDocument ItemEdits.Relocate(ItemsDocument, IReadOnlyDictionary<string,string> newTargets)` (item id → target);
  `Relocation.Bases(string OldBase, string NewBase)`, `Relocation.Move(string ItemId, string OldTarget, string NewTarget)`,
  `static Relocation.Bases? Relocation.Find(string oldTarget, string newTarget)`,
  `static IReadOnlyList<Relocation.Move> Relocation.Candidates(ItemsDocument, Func<string,TargetState> stateOf, Relocation.Bases, string exceptItemId)`;
  `enum DesktopGroup { Games, Apps, FoldersAndFiles, WebLinks }`,
  `DesktopEntry(string ItemRef, bool IsFolder, string? LinkTarget, string? LinkArguments)`,
  `static DesktopGroup DesktopSorting.GroupOf(DesktopEntry, IReadOnlyList<string> gameFolders, IReadOnlySet<string>? knownGames = null)`.

- [ ] **Step 1: Write the failing tests.** Write this patch to `m19-1-core-tests.patch` and `git apply --whitespace=nowarn` it:

````diff
diff --git a/tests/NeoFences.Core.Tests/Items/AppItemsTests.cs b/tests/NeoFences.Core.Tests/Items/AppItemsTests.cs
new file mode 100644
index 0000000..3de23a0
--- /dev/null
+++ b/tests/NeoFences.Core.Tests/Items/AppItemsTests.cs
@@ -0,0 +1,65 @@
+using NeoFences.Core.Items;
+
+namespace NeoFences.Core.Tests.Items;
+
+/// <summary>Apps as items (M19, spec 2026-10-05 §1): <c>shell:AppsFolder\&lt;id&gt;</c> targets, their check, their place among the checked targets.</summary>
+public class AppItemsTests
+{
+    [Theory]
+    [InlineData(@"shell:AppsFolder\SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", true)]
+    [InlineData(@"SHELL:appsfolder\308046B0AF4A39CB", true)] // Firefox: a desktop program's entry has no "!"
+    [InlineData(@"shell:AppsFolder\", false)] // no app id
+    [InlineData(@"shell:AppsFolder", false)] // the folder itself
+    [InlineData(@"shell:Downloads", false)]
+    [InlineData("::{645FF040-5081-101B-9F08-00AA002F954E}", false)]
+    [InlineData(@"D:\Games\a.exe", false)]
+    public void IsApp_KnowsAnAppEntry(string target, bool expected) => Assert.Equal(expected, ItemKinds.IsApp(target));
+
+    [Fact]
+    public void AppTarget_AndAppIdOf_RoundTrip_AndAnAppIsSpecial()
+    {
+        var target = ItemKinds.AppTarget("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");
+        Assert.Equal(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", target);
+        Assert.Equal("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", ItemKinds.AppIdOf(target));
+        Assert.Equal(ItemKind.Special, ItemKinds.Of(target));
+        Assert.Null(ItemKinds.AppIdOf(@"D:\Games\a.exe"));
+        Assert.False(ItemKinds.TakesArguments(ItemKinds.Of(target), isFolder: false)); // Store apps take no arguments
+    }
+
+    [Fact]
+    public void Classify_AnApp_IsOkWhileWindowsKnowsIt_MissingOnceUninstalled_NeverTouchesTheDisk()
+    {
+        Func<string, bool> noDisk = _ => throw new InvalidOperationException("an app has no path");
+        const string app = @"shell:AppsFolder\Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe!App";
+        Assert.Equal(TargetCheck.Ok, TargetChecks.Classify(app, noDisk, noDisk, appExists: _ => true));
+        Assert.Equal(new TargetCheck(TargetState.Missing), TargetChecks.Classify(app, noDisk, noDisk, appExists: _ => false));
+        Assert.Equal(TargetCheck.Ok, TargetChecks.Classify(app, noDisk, noDisk)); // nobody to ask: as before, Ok
+        Assert.Equal(TargetCheck.Ok, TargetChecks.Classify("::{645FF040-5081-101B-9F08-00AA002F954E}", noDisk, noDisk,
+            appExists: _ => throw new InvalidOperationException("not an app")));
+    }
+
+    [Fact]
+    public void CheckedTargets_ArePathsAndApps_OnceEach_WatchedOnesArePathsOnly()
+    {
+        var document = new ItemsDocument()
+            .With("f1", [
+                new VirtualItem { Id = "1", Target = @"D:\Games\a.exe" },
+                new VirtualItem { Id = "2", Target = @"shell:AppsFolder\Spotify!App" },
+                new VirtualItem { Id = "3", Target = "https://github.com" },
+                new VirtualItem { Id = "4", Target = "::{645FF040-5081-101B-9F08-00AA002F954E}" },
+            ])
+            .With("f2", [
+                new VirtualItem { Id = "5", Target = @"SHELL:APPSFOLDER\spotify!app" },
+                new VirtualItem { Id = "6", Target = @"d:\games\A.EXE" },
+            ]);
+        Assert.Equal([@"D:\Games\a.exe", @"shell:AppsFolder\Spotify!App"], ItemEdits.CheckedTargets(document));
+        Assert.Equal([@"D:\Games\a.exe"], ItemEdits.PathTargets(document));
+    }
+
+    [Fact]
+    public void Gone_ListsKeysNoLongerLive_IgnoringCase()
+    {
+        Assert.Equal([@"D:\old.txt"], StaleEntries.Gone([@"D:\a.exe", @"D:\old.txt"], [@"d:\A.EXE", @"D:\new.txt"], ItemKinds.Comparer));
+        Assert.Empty(StaleEntries.Gone([], [@"D:\a.exe"], ItemKinds.Comparer));
+    }
+}
diff --git a/tests/NeoFences.Core.Tests/Items/DesktopSortingTests.cs b/tests/NeoFences.Core.Tests/Items/DesktopSortingTests.cs
new file mode 100644
index 0000000..cb45555
--- /dev/null
+++ b/tests/NeoFences.Core.Tests/Items/DesktopSortingTests.cs
@@ -0,0 +1,69 @@
+using NeoFences.Core.Items;
+
+namespace NeoFences.Core.Tests.Items;
+
+/// <summary>"Add from desktop…" groups (M19, spec 2026-10-05 §3).</summary>
+public class DesktopSortingTests
+{
+    private static readonly string[] GameFolders = [@"D:\GameLibrary"];
+
+    private static DesktopGroup Group(string itemRef, bool isFolder = false, string? linkTarget = null, string? linkArguments = null) =>
+        DesktopSorting.GroupOf(new DesktopEntry(itemRef, isFolder, linkTarget, linkArguments), GameFolders);
+
+    [Theory]
+    [InlineData(@"C:\Users\c\Desktop\Dota 2.url", "steam://rungameid/570", null)] // a Steam desktop link
+    [InlineData(@"C:\Users\c\Desktop\Fortnite.url", "com.epicgames.launcher://apps/Fortnite?action=launch", null)]
+    [InlineData(@"C:\Users\c\Desktop\Hades.lnk", @"C:\Program Files (x86)\Steam\steamapps\common\Hades\Hades.exe", null)]
+    [InlineData(@"C:\Users\c\Desktop\Dota.lnk", @"C:\Program Files (x86)\Steam\steam.exe", "-applaunch 570")]
+    [InlineData(@"C:\Users\c\Desktop\Crysis 2.lnk", @"D:\GameLibrary\Crysis 2\Bin32\Crysis2.exe", null)] // under a game folder of the user's
+    [InlineData(@"C:\Users\c\Desktop\Crysis 3.lnk", @"d:\gamelibrary\Crysis 3\crysis3.exe", null)]
+    public void Games_AreWhatTheGameLibraryCountsAsGames(string itemRef, string target, string? arguments) =>
+        Assert.Equal(DesktopGroup.Games, Group(itemRef, linkTarget: target, linkArguments: arguments));
+
+    [Theory]
+    [InlineData(@"C:\Users\c\Desktop\Discord.lnk", @"C:\Users\c\AppData\Local\Discord\Update.exe")]
+    [InlineData(@"C:\Users\Public\Desktop\VLC.lnk", @"C:\Program Files\VideoLAN\VLC\vlc.exe")]
+    [InlineData(@"C:\Users\c\Desktop\Spotify.lnk", @"shell:AppsFolder\SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify")]
+    [InlineData(@"C:\Users\c\Desktop\WhatsApp.lnk", "")] // a Store app shortcut: no file path
+    [InlineData(@"C:\Users\c\Desktop\Backup.lnk", @"C:\Tools\backup.cmd")]
+    [InlineData(@"C:\Users\c\Desktop\Settings.url", "ms-settings:display")] // not a website, not a game
+    public void Apps_AreOtherProgramShortcuts(string itemRef, string target) =>
+        Assert.Equal(DesktopGroup.Apps, Group(itemRef, linkTarget: target));
+
+    [Fact]
+    public void AProgramRightOnTheDesktop_IsAnApp() => Assert.Equal(DesktopGroup.Apps, Group(@"C:\Users\c\Desktop\tool.exe"));
+
+    [Theory]
+    [InlineData(@"C:\Users\c\Desktop\GitHub.url", "https://github.com/")]
+    [InlineData(@"C:\Users\c\Desktop\Docs.url", "http://intranet/docs")]
+    public void WebLinks_AreUrlFilesForWebsites(string itemRef, string target) =>
+        Assert.Equal(DesktopGroup.WebLinks, Group(itemRef, linkTarget: target));
+
+    [Fact]
+    public void FoldersFilesAndWindowsIcons_GoTogether()
+    {
+        Assert.Equal(DesktopGroup.FoldersAndFiles, Group(@"C:\Users\c\Desktop\Screenshots", isFolder: true));
+        Assert.Equal(DesktopGroup.FoldersAndFiles, Group(@"C:\Users\c\Desktop\notes.txt"));
+        Assert.Equal(DesktopGroup.FoldersAndFiles, Group("::{645FF040-5081-101B-9F08-00AA002F954E}")); // Recycle Bin
+        Assert.Equal(DesktopGroup.FoldersAndFiles, Group(@"C:\Users\c\Desktop\Projects.lnk", linkTarget: @"F:\projects"));
+        Assert.Equal(DesktopGroup.FoldersAndFiles, Group(@"C:\Users\c\Desktop\CV.lnk", linkTarget: @"C:\Users\c\Documents\cv.pdf"));
+    }
+
+    [Fact]
+    public void AShortcutThatCouldNotBeRead_IsAFile() =>
+        Assert.Equal(DesktopGroup.FoldersAndFiles, Group(@"C:\Users\c\Desktop\broken.lnk", linkTarget: null));
+
+    [Fact]
+    public void AShortcutTheGameLibraryKnows_IsAGame_EvenWithoutALauncher()
+    {
+        var entry = new DesktopEntry(@"C:\Users\c\Desktop\Blur.lnk", IsFolder: false, LinkTarget: @"C:\Games\Blur\Blur.exe", LinkArguments: null);
+        Assert.Equal(DesktopGroup.Apps, DesktopSorting.GroupOf(entry, GameFolders));
+        Assert.Equal(DesktopGroup.Games, DesktopSorting.GroupOf(entry, GameFolders, knownGames: new HashSet<string>([@"c:\users\c\desktop\blur.lnk"], StringComparer.OrdinalIgnoreCase)));
+        // A game's install folder found by the library's scan counts like a game folder of the user's.
+        Assert.Equal(DesktopGroup.Games, DesktopSorting.GroupOf(entry, [@"C:\Games\Blur"]));
+    }
+
+    [Fact]
+    public void AGameFolderPrefix_MustEndAtASeparator() =>
+        Assert.Equal(DesktopGroup.Apps, Group(@"C:\Users\c\Desktop\x.lnk", linkTarget: @"D:\GameLibraryTools\x.exe"));
+}
diff --git a/tests/NeoFences.Core.Tests/Items/RelocationTests.cs b/tests/NeoFences.Core.Tests/Items/RelocationTests.cs
new file mode 100644
index 0000000..4640b12
--- /dev/null
+++ b/tests/NeoFences.Core.Tests/Items/RelocationTests.cs
@@ -0,0 +1,112 @@
+using NeoFences.Core.Items;
+
+namespace NeoFences.Core.Tests.Items;
+
+/// <summary>Fixing many missing items after one Locate… (M19, spec 2026-10-05 §2).</summary>
+public class RelocationTests
+{
+    // ---------- Relocation.Find ----------
+
+    [Theory]
+    [InlineData(@"D:\Games\Crysis 2\Crysis2.exe", @"E:\Games\Crysis 2\Crysis2.exe", @"D:\", @"E:\")] // a drive letter changed
+    [InlineData(@"D:\Games\X\x.exe", @"D:\MyGames\X\x.exe", @"D:\Games", @"D:\MyGames")] // a folder renamed
+    [InlineData(@"D:\Games\X\x.exe", @"D:\Archive\Games\X\x.exe", @"D:\", @"D:\Archive")] // moved one level down
+    [InlineData(@"D:\Archive\Games\X\x.exe", @"D:\Games\X\x.exe", @"D:\Archive", @"D:\")] // and back up
+    [InlineData(@"\\nas\old\Movies\a.mkv", @"\\nas\new\Movies\a.mkv", @"\\nas\old", @"\\nas\new")] // another share
+    [InlineData(@"D:\games\x\X.EXE", @"E:\Games\X\x.exe", @"D:\", @"E:\")] // case does not matter
+    [InlineData(@"D:\Games\Hades\", @"E:\Games\Hades", @"D:\", @"E:\")] // a folder target, with and without a trailing "\"
+    public void Find_TheDifferingFront_FromTheEnd(string oldTarget, string newTarget, string oldBase, string newBase)
+    {
+        var found = Relocation.Find(oldTarget, newTarget);
+        Assert.NotNull(found);
+        Assert.Equal(oldBase, found.OldBase, ignoreCase: true);
+        Assert.Equal(newBase, found.NewBase, ignoreCase: true);
+    }
+
+    [Theory]
+    [InlineData(@"D:\Games\a.exe", @"D:\Games\b.exe")] // the file itself was renamed: nothing to share
+    [InlineData(@"D:\Games\a.exe", @"D:\Games\a.exe")] // nothing moved
+    [InlineData(@"D:\a.exe", @"relative\a.exe")] // no root
+    [InlineData("https://a.com/x", @"D:\x")]
+    public void Find_Nothing_WhenTheNameChanged_OrNothingMoved_OrNoRoot(string oldTarget, string newTarget) =>
+        Assert.Null(Relocation.Find(oldTarget, newTarget));
+
+    [Fact]
+    public void Find_AFileRightOnADrive_MovesTheDrive()
+    {
+        var found = Relocation.Find(@"D:\a.exe", @"E:\a.exe");
+        Assert.Equal(new Relocation.Bases(@"D:\", @"E:\"), found);
+    }
+
+    // ---------- Relocation.Candidates ----------
+
+    private static ItemsDocument Sample() => new ItemsDocument()
+        .With("games", [
+            new VirtualItem { Id = "located", Target = @"D:\Games\Crysis 2\Crysis2.exe" },
+            new VirtualItem { Id = "c3", Target = @"D:\Games\Crysis 3\Crysis3.exe", Name = "Crysis 3" },
+            new VirtualItem { Id = "ok", Target = @"D:\Games\Hades\Hades.exe" },
+            new VirtualItem { Id = "web", Target = "https://github.com" },
+        ])
+        .With("other", [
+            new VirtualItem { Id = "mirage", Target = @"D:\Games\AC Mirage" },
+            new VirtualItem { Id = "near", Target = @"D:\GamesOld\x.exe" }, // "D:\Games" must not match "D:\GamesOld"
+            new VirtualItem { Id = "unplugged", Target = @"D:\Games\Doom\doom.exe" },
+            new VirtualItem { Id = "app", Target = @"shell:AppsFolder\Spotify!App" },
+        ]);
+
+    private static TargetState StateOf(string target) => target switch
+    {
+        @"D:\Games\Hades\Hades.exe" => TargetState.Ok,
+        @"D:\Games\Doom\doom.exe" => TargetState.Unavailable,
+        "https://github.com" or @"shell:AppsFolder\Spotify!App" => TargetState.Ok,
+        _ => TargetState.Missing,
+    };
+
+    [Fact]
+    public void Candidates_AreTheOtherMissingOrUnavailablePathItems_UnderTheOldBase_InEveryFence()
+    {
+        var moves = Relocation.Candidates(Sample(), StateOf, new Relocation.Bases(@"D:\Games", @"E:\Games"), exceptItemId: "located");
+        Assert.Equal(
+            [
+                new Relocation.Move("c3", @"D:\Games\Crysis 3\Crysis3.exe", @"E:\Games\Crysis 3\Crysis3.exe"),
+                new Relocation.Move("mirage", @"D:\Games\AC Mirage", @"E:\Games\AC Mirage"),
+                new Relocation.Move("unplugged", @"D:\Games\Doom\doom.exe", @"E:\Games\Doom\doom.exe"),
+            ],
+            moves);
+    }
+
+    [Fact]
+    public void Candidates_FromADriveRoot_KeepTheRestOfThePath()
+    {
+        var moves = Relocation.Candidates(Sample(), StateOf, new Relocation.Bases(@"D:\", @"E:\"), exceptItemId: "located");
+        Assert.Contains(new Relocation.Move("near", @"D:\GamesOld\x.exe", @"E:\GamesOld\x.exe"), moves);
+        Assert.DoesNotContain(moves, move => move.ItemId is "ok" or "web" or "app" or "located");
+    }
+
+    [Fact]
+    public void Candidates_TheSameTargetInTwoItems_BothMove()
+    {
+        var document = new ItemsDocument()
+            .With("a", [new VirtualItem { Id = "1", Target = @"D:\G\x.exe" }])
+            .With("b", [new VirtualItem { Id = "2", Target = @"D:\G\x.exe" }]);
+        var moves = Relocation.Candidates(document, _ => TargetState.Missing, new Relocation.Bases(@"D:\G", @"E:\G"), exceptItemId: "none");
+        Assert.Equal(["1", "2"], moves.Select(move => move.ItemId));
+    }
+
+    // ---------- ItemEdits.Relocate ----------
+
+    [Fact]
+    public void Relocate_ChangesOnlyTheseTargets_EverythingElseStays()
+    {
+        var document = new ItemsDocument().With("f", [
+            new VirtualItem { Id = "1", Target = @"D:\G\a.exe", Name = "A", Arguments = "-x", Note = "n", Icon = new ItemIcon { Image = "p.png" } },
+            new VirtualItem { Id = "2", Target = @"D:\G\b.exe" },
+            new VirtualItem { Id = "3", Target = @"D:\G\c.exe" },
+        ]);
+        var moved = ItemEdits.Relocate(document, new Dictionary<string, string> { ["1"] = @"E:\G\a.exe", ["3"] = @"E:\G\c.exe", ["unknown"] = @"E:\x" });
+        Assert.Equal([@"E:\G\a.exe", @"D:\G\b.exe", @"E:\G\c.exe"], moved.Of("f").Select(item => item.Target));
+        var first = moved.Of("f")[0];
+        Assert.Equal(("A", "-x", "n", "p.png"), (first.Name, first.Arguments, first.Note, first.Icon?.Image));
+        Assert.Same(document, ItemEdits.Relocate(document, new Dictionary<string, string> { ["unknown"] = @"E:\x" }));
+    }
+}
````

- [ ] **Step 2: Run them to see them fail.**
  Run: `dotnet test`
  Expected: build error `CS0246: The type or namespace name 'DesktopGroup' could not be found` (the Core types do not exist yet).

- [ ] **Step 3: Implement.** Write this patch to `m19-1-core.patch` and apply it:

````diff
diff --git a/src/NeoFences.Core/Items/DesktopSorting.cs b/src/NeoFences.Core/Items/DesktopSorting.cs
new file mode 100644
index 0000000..1e4ee26
--- /dev/null
+++ b/src/NeoFences.Core/Items/DesktopSorting.cs
@@ -0,0 +1,44 @@
+using NeoFences.Core.Library;
+
+namespace NeoFences.Core.Items;
+
+/// <summary>The groups of "Add from desktop…" (M19, spec 2026-10-05 §3), in the order the dialog shows them.</summary>
+public enum DesktopGroup { Games, Apps, FoldersAndFiles, WebLinks }
+
+/// <summary>One desktop entry as the dialog reads it.</summary>
+/// <param name="ItemRef">Its full path, or <c>::{CLSID}</c> for a Windows icon (Recycle Bin, This PC).</param>
+/// <param name="LinkTarget">A .lnk / .url's target ("" when it has no file path, e.g. a Store app shortcut); null when it is not a
+/// shortcut or could not be read.</param>
+public sealed record DesktopEntry(string ItemRef, bool IsFolder, string? LinkTarget, string? LinkArguments);
+
+public static class DesktopSorting
+{
+    private static readonly string[] ProgramExtensions = [".exe", ".bat", ".cmd", ".com", ".msc", ".ps1", ".appref-ms"];
+
+    /// <summary>
+    /// Games: what the Game Library counts as a game (a launcher link or library folder, a target under one of the user's
+    /// game folders or a scanned game's install folder, or an entry its scan already knows as a game). Web links: a .url to
+    /// an http(s) address. Apps: any other shortcut to a program, an app or a link without a file path, and programs right on
+    /// the desktop. Folders and files: everything else, Windows' icons too.
+    /// </summary>
+    /// <param name="gameFolders">The user's game folders and the install folders the Game Library's scan found.</param>
+    /// <param name="knownGames">Desktop entries the Game Library's scan counts as games (its Desktop shortcuts), or null.</param>
+    public static DesktopGroup GroupOf(DesktopEntry entry, IReadOnlyList<string> gameFolders, IReadOnlySet<string>? knownGames = null)
+    {
+        if (entry.ItemRef.StartsWith("::", StringComparison.Ordinal) || entry.IsFolder) return DesktopGroup.FoldersAndFiles;
+        if (knownGames?.Contains(entry.ItemRef) == true) return DesktopGroup.Games;
+        if (entry.LinkTarget is not { } target)
+        {
+            var isShortcut = Path.GetExtension(entry.ItemRef).ToLowerInvariant() is ".lnk" or ".url";
+            return !isShortcut && IsProgram(entry.ItemRef) ? DesktopGroup.Apps : DesktopGroup.FoldersAndFiles;
+        }
+        if (GameLaunchers.LauncherOf($"{target} {entry.LinkArguments}".Trim()) is not null
+            || gameFolders.Any(folder => target.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))) return DesktopGroup.Games;
+        if (ItemKinds.IsWebsite(target)) return DesktopGroup.WebLinks;
+        if (target.Length == 0 || ItemKinds.IsApp(target) || IsProgram(target) || target.Contains(':') && !target.Contains('\\'))
+            return DesktopGroup.Apps; // "ms-settings:display", another app's link scheme
+        return DesktopGroup.FoldersAndFiles;
+    }
+
+    private static bool IsProgram(string path) => ProgramExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
+}
diff --git a/src/NeoFences.Core/Items/ItemsDocument.cs b/src/NeoFences.Core/Items/ItemsDocument.cs
index 8bb330a..69650b3 100644
--- a/src/NeoFences.Core/Items/ItemsDocument.cs
+++ b/src/NeoFences.Core/Items/ItemsDocument.cs
@@ -159,6 +159,25 @@ public static class ItemEdits
         document.Fences.Values.SelectMany(items => items).Where(item => item.Kind == ItemKind.Path)
             .Select(item => item.Target).Distinct(ItemKinds.Comparer).ToList();
 
+    /// <summary>Every target NeoFences checks, once, in fence order: files and folders, and apps (M19; never watched).</summary>
+    public static IReadOnlyList<string> CheckedTargets(ItemsDocument document) =>
+        document.Fences.Values.SelectMany(items => items).Where(item => item.Kind == ItemKind.Path || ItemKinds.IsApp(item.Target))
+            .Select(item => item.Target).Distinct(ItemKinds.Comparer).ToList();
+
+    /// <summary>
+    /// The bulk fix after Locate… (M19 §2): these items (by id) take these targets; names, icons, arguments, notes and the
+    /// order stay. Unknown ids are ignored; nothing to change: the same document.
+    /// </summary>
+    public static ItemsDocument Relocate(ItemsDocument document, IReadOnlyDictionary<string, string> newTargets)
+    {
+        if (!document.Fences.Values.Any(items => items.Any(item => newTargets.ContainsKey(item.Id)))) return document;
+        return document with
+        {
+            Fences = document.Fences.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<VirtualItem>)entry.Value
+                .Select(item => newTargets.TryGetValue(item.Id, out var target) ? item with { Target = target } : item).ToList()),
+        };
+    }
+
     /// <summary>Picture files in <c>icons\</c> some item still uses (the others are deleted at start).</summary>
     public static IReadOnlySet<string> ImagesInUse(ItemsDocument document) =>
         document.Fences.Values.SelectMany(items => items).Select(item => item.Icon?.Image).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
diff --git a/src/NeoFences.Core/Items/Relocation.cs b/src/NeoFences.Core/Items/Relocation.cs
new file mode 100644
index 0000000..ffc0b24
--- /dev/null
+++ b/src/NeoFences.Core/Items/Relocation.cs
@@ -0,0 +1,73 @@
+namespace NeoFences.Core.Items;
+
+/// <summary>
+/// The bulk fix after one Locate… (M19, spec 2026-10-05 §2, ADR-042): where the located item moved tells where its
+/// neighbours went, and the other missing items from the same old place are offered at once.
+/// </summary>
+public static class Relocation
+{
+    /// <summary>The differing front of the old and new path: everything under <see cref="OldBase"/> is now under <see cref="NewBase"/>.</summary>
+    public sealed record Bases(string OldBase, string NewBase);
+
+    /// <summary>One item's proposed new target.</summary>
+    public sealed record Move(string ItemId, string OldTarget, string NewTarget);
+
+    /// <summary>
+    /// Compares the two paths segment by segment from the end (ignoring case and a trailing "\"): what they share at the
+    /// end stayed, the rest moved. Null when the last segment (the file or folder name) differs, nothing moved, or
+    /// either path has no drive or share root.
+    /// </summary>
+    public static Bases? Find(string oldTarget, string newTarget)
+    {
+        if (Split(oldTarget) is not { } old || Split(newTarget) is not { } found) return null;
+        var shared = 0;
+        while (shared < old.Parts.Length && shared < found.Parts.Length
+               && ItemKinds.Comparer.Equals(old.Parts[^(shared + 1)], found.Parts[^(shared + 1)]))
+        {
+            shared++;
+        }
+        if (shared == 0) return null;
+        var oldBase = Join(old.Root, old.Parts[..^shared]);
+        var newBase = Join(found.Root, found.Parts[..^shared]);
+        return ItemKinds.Comparer.Equals(oldBase, newBase) ? null : new Bases(oldBase, newBase);
+    }
+
+    /// <summary>
+    /// The other items (not <paramref name="exceptItemId"/>), in every fence and in fence order, that point at a file or
+    /// folder under <see cref="Bases.OldBase"/> and are Missing or Unavailable, with their target under the new base.
+    /// Websites, apps, special items and items that are fine are never moved.
+    /// </summary>
+    public static IReadOnlyList<Move> Candidates(ItemsDocument document, Func<string, TargetState> stateOf, Bases bases, string exceptItemId)
+    {
+        var oldPrefix = bases.OldBase.TrimEnd('\\') + "\\";
+        var newPrefix = bases.NewBase.TrimEnd('\\') + "\\";
+        var moves = new List<Move>();
+        foreach (var item in document.Fences.Values.SelectMany(items => items))
+        {
+            if (item.Id == exceptItemId || item.Kind != ItemKind.Path) continue;
+            var target = item.Target.TrimEnd('\\');
+            string? moved = ItemKinds.Comparer.Equals(target, bases.OldBase.TrimEnd('\\')) ? bases.NewBase
+                : target.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase) ? newPrefix + target[oldPrefix.Length..]
+                : null;
+            if (moved is null || stateOf(item.Target) is not (TargetState.Missing or TargetState.Unavailable)) continue;
+            moves.Add(new Move(item.Id, item.Target, moved));
+        }
+        return moves;
+    }
+
+    /// <summary>"D:\" + ["Games", "X"] or "\\nas\share\" + [...]; null without a root.</summary>
+    private static (string Root, string[] Parts)? Split(string path)
+    {
+        var trimmed = path.TrimEnd('\\');
+        if (TargetChecks.RootOf(trimmed + "\\") is not { } root) return null;
+        var rest = trimmed.Length > root.Length ? trimmed[root.Length..] : "";
+        return (root, rest.Split('\\', StringSplitOptions.RemoveEmptyEntries));
+    }
+
+    /// <summary>A drive root keeps its "\" ("D:\"); anything longer has none ("D:\Games", "\\nas\share").</summary>
+    private static string Join(string root, string[] parts)
+    {
+        var joined = root + string.Join('\\', parts);
+        return joined.Length == 3 && joined[1] == ':' ? joined : joined.TrimEnd('\\');
+    }
+}
diff --git a/src/NeoFences.Core/Items/VirtualItem.cs b/src/NeoFences.Core/Items/VirtualItem.cs
index 1f26893..276830f 100644
--- a/src/NeoFences.Core/Items/VirtualItem.cs
+++ b/src/NeoFences.Core/Items/VirtualItem.cs
@@ -66,6 +66,18 @@ public static class ItemKinds
         : IsWebsite(target) ? ItemKind.Website
         : ItemKind.Path;
 
+    private const string AppsFolderPrefix = @"shell:AppsFolder\";
+
+    /// <summary>An entry of Start's All apps (M19, ADR-042): <c>shell:AppsFolder\&lt;AppUserModelID&gt;</c>, a Store app or a program.</summary>
+    public static bool IsApp(string target) => AppIdOf(target) is not null;
+
+    /// <summary>The app's id (AppUserModelID), or null when the target is not an app.</summary>
+    public static string? AppIdOf(string target) =>
+        target.Length > AppsFolderPrefix.Length && target.StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase)
+            ? target[AppsFolderPrefix.Length..] : null;
+
+    public static string AppTarget(string appId) => AppsFolderPrefix + appId;
+
     public static bool IsWebsite(string text) =>
         Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
 
diff --git a/src/NeoFences.Core/Items/Watching.cs b/src/NeoFences.Core/Items/Watching.cs
index d196d51..464b6aa 100644
--- a/src/NeoFences.Core/Items/Watching.cs
+++ b/src/NeoFences.Core/Items/Watching.cs
@@ -1,6 +1,9 @@
 namespace NeoFences.Core.Items;
 
-/// <summary>Where an item's target is now (spec §4). Websites and special items are always <see cref="Ok"/>.</summary>
+/// <summary>
+/// Where an item's target is now (spec §4). Websites and special items are always <see cref="Ok"/>; an app (M19) is
+/// <see cref="Missing"/> once Windows no longer knows it.
+/// </summary>
 public enum TargetState { Ok, Missing, Unavailable }
 
 /// <param name="IsFolder">The target is a folder (Arguments and "Run as administrator" do not apply).</param>
@@ -18,8 +21,11 @@ public static class TargetChecks
     /// </summary>
     /// <param name="fileExists">True when a file is at the path.</param>
     /// <param name="folderExists">True when a folder is at the path (also asked for the root).</param>
-    public static TargetCheck Classify(string target, Func<string, bool> fileExists, Func<string, bool> folderExists)
+    /// <param name="appExists">True when Windows still knows this app (M19); null: apps count as Ok.</param>
+    public static TargetCheck Classify(string target, Func<string, bool> fileExists, Func<string, bool> folderExists,
+        Func<string, bool>? appExists = null)
     {
+        if (ItemKinds.IsApp(target)) return appExists is null || appExists(target) ? TargetCheck.Ok : new TargetCheck(TargetState.Missing);
         if (ItemKinds.Of(target) != ItemKind.Path) return TargetCheck.Ok;
         // A launcher link (steam://rungameid/570) or a relative path: nothing on a disk to check, Windows opens it.
         if (RootOf(target) is not { } root) return TargetCheck.Ok;
@@ -41,6 +47,17 @@ public static class TargetChecks
     public static bool IsNetworkPath(string path) => path.StartsWith(@"\\", StringComparison.Ordinal);
 }
 
+/// <summary>Per-target and per-fence records that outlived their target or fence (M19 R5).</summary>
+public static class StaleEntries
+{
+    /// <summary>The keys of <paramref name="known"/> that are not in <paramref name="live"/>.</summary>
+    public static IReadOnlyList<string> Gone(IEnumerable<string> known, IEnumerable<string> live, IEqualityComparer<string> comparer)
+    {
+        var liveKeys = live.ToHashSet(comparer);
+        return [.. known.Where(key => !liveKeys.Contains(key))];
+    }
+}
+
 /// <summary>Which folders NeoFences watches for its targets (spec §4): a fixed budget, the busiest folders first.</summary>
 public static class WatchPlan
 {
````

- [ ] **Step 4: Run the tests.**
  Run: `dotnet test`
  Expected: `Passed: 450` (404 + 46 new), 0 failed.

- [ ] **Step 5: Commit.**
  `git add src/NeoFences.Core tests` then
  `git commit -m "feat: added app targets, the bulk-fix relocation, desktop sorting and stale-entry clean-up to Core"`

### Task 2: Shell — the app list, app checks, apps dragged from Start, generic icons, COM releases

**Files:**
- Create: `src/NeoFences.Shell/AppList.cs` (`InstalledApp`, `AppList.Enumerate`, `Exists`, `AppTargetOf`)
- Modify: `src/NeoFences.Shell/NativeMethods.txt` (SHGetFileInfo, SHFILEINFOW, SHGFI_FLAGS, SHGetKnownFolderItem,
  FOLDERID_AppsFolder, KNOWN_FOLDER_FLAG, BHID_EnumItems, IEnumShellItems), `TargetProbe.cs` (apps checked),
  `ShellDragDrop.cs` (apps from Start kept; the drag-image helper's refusal quiet), `ShellItems.cs`
  (`TryGetGenericImage`), `PathPicker.cs` and `DesktopNamespace.cs` (R3 releases)

**Interfaces:**
- Consumes: Task 1's `ItemKinds.IsApp/AppTarget`, `TargetChecks.Classify(..., appExists)`.
- Produces: `sealed record InstalledApp(string AppId, string Name)`; `static IReadOnlyList<InstalledApp> AppList.Enumerate()`
  (STA thread, A–Z); `static bool AppList.Exists(string appTarget)`; `static ShellImage? ShellItems.TryGetGenericImage(string target, bool isFolder)`.

- [ ] **Step 1: Apply.** Write this patch to `m19-2-shell.patch` and apply it:

````diff
diff --git a/src/NeoFences.Shell/AppList.cs b/src/NeoFences.Shell/AppList.cs
new file mode 100644
index 0000000..0ba3006
--- /dev/null
+++ b/src/NeoFences.Shell/AppList.cs
@@ -0,0 +1,111 @@
+using System.Runtime.InteropServices;
+using NeoFences.Core.Items;
+using Windows.Win32;
+using Windows.Win32.UI.Shell;
+
+namespace NeoFences.Shell;
+
+/// <summary>An entry of Start's All apps: its AppUserModelID and the name Start shows.</summary>
+public sealed record InstalledApp(string AppId, string Name);
+
+/// <summary>
+/// Start's All apps (M19, ADR-042): Store apps and programs alike, as <c>shell:AppsFolder\&lt;id&gt;</c> items. Listing and
+/// checking ask the shell; neither touches an app.
+/// </summary>
+public static class AppList
+{
+    /// <summary>The AppsFolder's parsing name: an app dragged from Start is one of its children.</summary>
+    private const string AppsFolderRef = "::{4234D49B-0245-4DF3-B780-3893943456E1}";
+
+    /// <summary>Every app, A–Z by name. Call on an STA thread that is not the UI thread (~0.3 s for ~200 apps).</summary>
+    public static unsafe IReadOnlyList<InstalledApp> Enumerate()
+    {
+        var apps = new List<InstalledApp>();
+        IShellItem? folder = null;
+        IEnumShellItems? children = null;
+        try
+        {
+            var folderId = PInvoke.FOLDERID_AppsFolder;
+            var itemId = typeof(IShellItem).GUID;
+            PInvoke.SHGetKnownFolderItem(&folderId, KNOWN_FOLDER_FLAG.KF_FLAG_DEFAULT, default, &itemId, out var created).ThrowOnFailure();
+            folder = (IShellItem)created;
+            var handler = PInvoke.BHID_EnumItems;
+            var enumId = typeof(IEnumShellItems).GUID;
+            folder.BindToHandler(null, &handler, &enumId, out var enumerator);
+            children = (IEnumShellItems)enumerator;
+            var batch = new IShellItem[1];
+            while (true)
+            {
+                uint fetched;
+                children.Next(1, batch, &fetched);
+                if (fetched == 0) break;
+                var child = batch[0];
+                try
+                {
+                    if (Name(child, SIGDN.SIGDN_PARENTRELATIVEPARSING) is { Length: > 0 } appId && Name(child, SIGDN.SIGDN_NORMALDISPLAY) is { Length: > 0 } name)
+                        apps.Add(new InstalledApp(appId, name));
+                }
+                finally
+                {
+                    Marshal.ReleaseComObject(child);
+                }
+            }
+        }
+        finally
+        {
+            if (children is not null) Marshal.ReleaseComObject(children);
+            if (folder is not null) Marshal.ReleaseComObject(folder);
+        }
+        return [.. apps.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)];
+    }
+
+    /// <summary>Windows still knows this app (an uninstalled one no longer parses). Call off the UI thread.</summary>
+    public static bool Exists(string appTarget)
+    {
+        try
+        {
+            PInvoke.SHCreateItemFromParsingName(appTarget, null, out IShellItem item).ThrowOnFailure();
+            Marshal.ReleaseComObject(item);
+            return true;
+        }
+        catch (Exception failure) when (failure is not OutOfMemoryException)
+        {
+            return false;
+        }
+    }
+
+    /// <summary>An item dragged from Start's All apps: its <c>shell:AppsFolder\&lt;id&gt;</c> target; null for anything else.</summary>
+    internal static string? AppTargetOf(IShellItem item)
+    {
+        IShellItem? parent = null;
+        try
+        {
+            item.GetParent(out parent);
+            return string.Equals(Name(parent, SIGDN.SIGDN_DESKTOPABSOLUTEPARSING), AppsFolderRef, StringComparison.OrdinalIgnoreCase)
+                   && Name(item, SIGDN.SIGDN_PARENTRELATIVEPARSING) is { Length: > 0 } appId
+                ? ItemKinds.AppTarget(appId) : null;
+        }
+        catch (Exception failure) when (failure is not OutOfMemoryException)
+        {
+            return null; // no parent (the desktop itself), or a handler that refuses
+        }
+        finally
+        {
+            if (parent is not null) Marshal.ReleaseComObject(parent);
+        }
+    }
+
+    private static unsafe string? Name(IShellItem item, SIGDN form)
+    {
+        try
+        {
+            item.GetDisplayName(form, out var name);
+            try { return name.ToString(); }
+            finally { Marshal.FreeCoTaskMem((nint)name.Value); }
+        }
+        catch (Exception failure) when (failure is not OutOfMemoryException)
+        {
+            return null;
+        }
+    }
+}
diff --git a/src/NeoFences.Shell/DesktopNamespace.cs b/src/NeoFences.Shell/DesktopNamespace.cs
index 2666a9b..441002b 100644
--- a/src/NeoFences.Shell/DesktopNamespace.cs
+++ b/src/NeoFences.Shell/DesktopNamespace.cs
@@ -18,18 +18,25 @@ internal static class DesktopNamespace
     public static unsafe object GetUIObject(HWND owner, IReadOnlyList<string> itemRefs, Guid interfaceId)
     {
         var parent = ParentFolder(itemRefs);
-        var childIds = ChildIds(owner, parent, itemRefs);
         try
         {
-            fixed (nint* ids = childIds.ToArray())
+            var childIds = ChildIds(owner, parent, itemRefs);
+            try
             {
-                parent.GetUIObjectOf(owner, (uint)childIds.Count, (ITEMIDLIST**)ids, &interfaceId, null, out var uiObject);
-                return uiObject;
+                fixed (nint* ids = childIds.ToArray())
+                {
+                    parent.GetUIObjectOf(owner, (uint)childIds.Count, (ITEMIDLIST**)ids, &interfaceId, null, out var uiObject);
+                    return uiObject;
+                }
+            }
+            finally
+            {
+                foreach (var childId in childIds) Marshal.FreeCoTaskMem(childId);
             }
         }
         finally
         {
-            foreach (var childId in childIds) Marshal.FreeCoTaskMem(childId);
+            Marshal.ReleaseComObject(parent); // M19 R3: the desktop or parent folder object, released now
         }
     }
 
@@ -70,10 +77,17 @@ internal static class DesktopNamespace
     private static unsafe IShellFolder FolderObject(string folderPath)
     {
         PInvoke.SHCreateItemFromParsingName(folderPath, null, out IShellItem folderItem).ThrowOnFailure();
-        var handler = PInvoke.BHID_SFObject;
-        var folderId = typeof(IShellFolder).GUID;
-        folderItem.BindToHandler(null, &handler, &folderId, out var folder);
-        return (IShellFolder)folder;
+        try
+        {
+            var handler = PInvoke.BHID_SFObject;
+            var folderId = typeof(IShellFolder).GUID;
+            folderItem.BindToHandler(null, &handler, &folderId, out var folder);
+            return (IShellFolder)folder;
+        }
+        finally
+        {
+            Marshal.ReleaseComObject(folderItem); // M19 R3
+        }
     }
 
     private static unsafe List<nint> ChildIds(HWND owner, IShellFolder parent, IReadOnlyList<string> itemRefs)
diff --git a/src/NeoFences.Shell/NativeMethods.txt b/src/NeoFences.Shell/NativeMethods.txt
index 358b28f..c52010d 100644
--- a/src/NeoFences.Shell/NativeMethods.txt
+++ b/src/NeoFences.Shell/NativeMethods.txt
@@ -199,3 +199,11 @@ ScreenToClient
 _SVGIO
 SHCreateShellItemArrayFromIDLists
 BHID_DataObject
+SHGetFileInfo
+SHFILEINFOW
+SHGFI_FLAGS
+SHGetKnownFolderItem
+FOLDERID_AppsFolder
+KNOWN_FOLDER_FLAG
+BHID_EnumItems
+IEnumShellItems
diff --git a/src/NeoFences.Shell/PathPicker.cs b/src/NeoFences.Shell/PathPicker.cs
index a8b70aa..a69bf1e 100644
--- a/src/NeoFences.Shell/PathPicker.cs
+++ b/src/NeoFences.Shell/PathPicker.cs
@@ -58,7 +58,8 @@ public static class PathPicker
             }
             dialog.Show((HWND)ownerHandle); // throws ERROR_CANCELLED when the user cancels
             dialog.GetResult(out var item);
-            return ShellItems.FileSystemPath(item);
+            try { return ShellItems.FileSystemPath(item); }
+            finally { Marshal.ReleaseComObject(item); } // M19 R3: released now, not by the finalizer
         }
         catch (Exception failure) when (failure is not OutOfMemoryException)
         {
diff --git a/src/NeoFences.Shell/ShellDragDrop.cs b/src/NeoFences.Shell/ShellDragDrop.cs
index efa74cc..4cf6380 100644
--- a/src/NeoFences.Shell/ShellDragDrop.cs
+++ b/src/NeoFences.Shell/ShellDragDrop.cs
@@ -158,7 +158,8 @@ public static class ShellDragDrop
 
     /// <summary>
     /// The dragged shell items (Explorer, the desktop): a file or folder gives its path, a special item (Recycle Bin, This
-    /// PC) its <c>::{GUID}</c>. Items with neither (zip contents, a phone) are left out — nothing is extracted.
+    /// PC) its <c>::{GUID}</c>, an app from Start its <c>shell:AppsFolder\&lt;id&gt;</c> (M19). Items with none of these
+    /// (zip contents, a phone) are left out — nothing is extracted.
     /// </summary>
     private static unsafe List<string> ShellItemTargets(IDataObject dataObject)
     {
@@ -176,7 +177,7 @@ public static class ShellDragDrop
                 array.GetItemAt(index, out var item);
                 try
                 {
-                    if ((ShellItems.FileSystemPath(item) ?? ShellItems.SpecialItemRef(item)) is { } target) targets.Add(target);
+                    if ((ShellItems.FileSystemPath(item) ?? ShellItems.SpecialItemRef(item) ?? AppList.AppTargetOf(item)) is { } target) targets.Add(target);
                 }
                 finally
                 {
@@ -385,12 +386,22 @@ public static class ShellDragDrop
             }
         }
 
-        /// <summary>The drag image is cosmetic: a failing helper must not break the drop or leave state behind.</summary>
+        /// <summary>
+        /// The drag image is cosmetic: a failing helper must not break the drop or leave state behind. After one failure it is
+        /// not asked again for this drag. A source without a drag image (Start's All apps, M19) is refused with
+        /// DV_E_CLIPFORMAT: expected, not logged as a failed drop.
+        /// </summary>
         private void WithImageHelper(Action<IDropTargetHelper> call)
         {
             if (_imageHelper is null) return;
             try { call(_imageHelper); }
-            catch (Exception failure) when (failure is not OutOfMemoryException) { handlers.LogFailure(failure); }
+            catch (Exception failure) when (failure is not OutOfMemoryException)
+            {
+                const int NoDragImage = unchecked((int)0x8004006A); // DV_E_CLIPFORMAT
+                if (failure.HResult != NoDragImage) handlers.LogFailure(failure);
+                Marshal.ReleaseComObject(_imageHelper);
+                _imageHelper = null;
+            }
         }
 
         private void Reset()
diff --git a/src/NeoFences.Shell/ShellItems.cs b/src/NeoFences.Shell/ShellItems.cs
index 58cb5bf..4b64764 100644
--- a/src/NeoFences.Shell/ShellItems.cs
+++ b/src/NeoFences.Shell/ShellItems.cs
@@ -3,6 +3,7 @@ using System.Runtime.InteropServices;
 using Windows.Win32;
 using Windows.Win32.Foundation;
 using Windows.Win32.Graphics.Gdi;
+using Windows.Win32.Storage.FileSystem;
 using Windows.Win32.UI.Shell;
 using Windows.Win32.UI.WindowsAndMessaging;
 
@@ -193,6 +194,40 @@ public static class ShellItems
         }
     }
 
+    /// <summary>
+    /// Windows' icon for this kind of item, by its name only (M19 R8): a folder icon, or the icon of its file type ("a .txt
+    /// file"). Never touches the disk, so a missing or unreachable target still gets one. Null when Windows has none.
+    /// </summary>
+    public static unsafe ShellImage? TryGetGenericImage(string target, bool isFolder)
+    {
+        var info = new SHFILEINFOW();
+        ICONINFO iconInfo = default;
+        try
+        {
+            // ponytail: the 32 px "large" icon, scaled by WPF for bigger sizes; the system image list's jumbo icons if it looks soft.
+            var attributes = isFolder ? FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_DIRECTORY : FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL;
+            var flags = SHGFI_FLAGS.SHGFI_ICON | SHGFI_FLAGS.SHGFI_LARGEICON | SHGFI_FLAGS.SHGFI_USEFILEATTRIBUTES;
+            nuint found;
+            fixed (char* name = target)
+            {
+                found = PInvoke.SHGetFileInfo(name, attributes, &info, (uint)sizeof(SHFILEINFOW), flags);
+            }
+            if (found == 0 || info.hIcon.IsNull) return null;
+            if (!PInvoke.GetIconInfo(info.hIcon, &iconInfo)) return null;
+            return ReadPixels(iconInfo.hbmColor, premultiply: true);
+        }
+        catch (Exception failure) when (failure is not OutOfMemoryException)
+        {
+            return null;
+        }
+        finally
+        {
+            if (!iconInfo.hbmColor.IsNull) PInvoke.DeleteObject(iconInfo.hbmColor);
+            if (!iconInfo.hbmMask.IsNull) PInvoke.DeleteObject(iconInfo.hbmMask);
+            if (!info.hIcon.IsNull) PInvoke.DestroyIcon(info.hIcon);
+        }
+    }
+
     private static IShellItem Create(string itemRef)
     {
         PInvoke.SHCreateItemFromParsingName(itemRef, null, out IShellItem item).ThrowOnFailure();
diff --git a/src/NeoFences.Shell/TargetProbe.cs b/src/NeoFences.Shell/TargetProbe.cs
index 8db62b7..e5d1502 100644
--- a/src/NeoFences.Shell/TargetProbe.cs
+++ b/src/NeoFences.Shell/TargetProbe.cs
@@ -25,6 +25,12 @@ public static class TargetProbe
         var rootAnswers = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
         foreach (var target in targets)
         {
+            // An app (M19): Missing once Windows no longer knows it; no disk, no timeout needed.
+            if (ItemKinds.IsApp(target))
+            {
+                results.Add((target, TargetChecks.Classify(target, File.Exists, Directory.Exists, appExists: AppList.Exists)));
+                continue;
+            }
             // Websites, special items, launcher links (steam://…): nothing on a disk to ask.
             if (ItemKinds.Of(target) != ItemKind.Path || TargetChecks.RootOf(target) is not { } root)
             {
````

- [ ] **Step 2: Build.**
  Run: `dotnet build`
  Expected: `0 Warning(s)`, `0 Error(s)` (CsWin32 generates the new bindings).

- [ ] **Step 3: Commit.**
  `git add src/NeoFences.Shell` then
  `git commit -m "feat: added the app list, app checks, apps dragged from Start and generic icons to the Shell, and released leaked COM objects"`

### Task 3: App — Store apps, the bulk fix, Add from desktop, reliability fixes

**Files:**
- Create: `src/NeoFences.App/AppPickerWindow.xaml(.cs)`, `RelocateWindow.xaml(.cs)`, `DesktopFillWindow.xaml(.cs)`,
  `FenceHost.DesktopFill.cs`
- Modify: `FenceHost.cs` (tray "Add from desktop…", "Undo the last restore or fix", `ForgetGoneTargets` after Delete
  fence), `FenceHost.Items.cs` (Locate… for apps, `Relocated` / `OfferRelocation` / `FixItems`, R2 checks for Windows'
  menu, Locate…'s start folder and drag-out, `ForgetGoneTargets`), `FenceHost.Watching.cs` (apps checked, R1
  `_armingNotices`, R4 `TryWatch` / faulted batch, R5 `ForgetGoneTargets`), `FenceItemView.cs` (app placeholder name),
  `FenceWindow.xaml(.cs)` ("Add from desktop…"), `IconLoader.cs` (R7, R8 `GenericIcon`), `ItemPropertiesWindow.xaml(.cs)`
  ("An app…", app status, R2 `WhenReachable`, R6 OK waits), `MissingItemWindow.xaml.cs` (uninstalled app text)

**Interfaces:**
- Consumes: everything Tasks 1–2 produce.
- Produces (App-internal): `AppPickerWindow(IconLoader)` → `InstalledApp? Chosen`; `RelocateWindow(int count, Relocation.Bases, IReadOnlyList<string> names)`;
  `DesktopFillWindow(IReadOnlyList<(string Id, string Title)> fences, IReadOnlyDictionary<string,string> alreadyIn, LibrarySettings library, IconLoader, bool iconsHidden)` → `DesktopFillPlan? Plan`;
  `FenceWindow.AddFromDesktopRequested`.

- [ ] **Step 1: Apply.** Write this patch to `m19-3-app.patch` and apply it:

````diff
diff --git a/src/NeoFences.App/AppPickerWindow.xaml b/src/NeoFences.App/AppPickerWindow.xaml
new file mode 100644
index 0000000..3c4d1a8
--- /dev/null
+++ b/src/NeoFences.App/AppPickerWindow.xaml
@@ -0,0 +1,37 @@
+<Window x:Class="NeoFences.App.AppPickerWindow"
+        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
+        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
+        Title="Choose an app" Width="440" Height="560" MinWidth="360" MinHeight="360"
+        WindowStartupLocation="CenterOwner" ShowInTaskbar="True" ThemeMode="System">
+    <!-- M19 spec 2026-10-05 §1: every entry of Start's All apps (Store apps and programs), searchable. -->
+    <Grid Margin="20,16,20,18">
+        <Grid.RowDefinitions>
+            <RowDefinition Height="Auto" />
+            <RowDefinition Height="*" />
+            <RowDefinition Height="Auto" />
+        </Grid.RowDefinitions>
+        <TextBox x:Name="SearchBox" Margin="0,0,0,10" AutomationProperties.Name="Search apps" />
+        <ListBox x:Name="AppListBox" Grid.Row="1" AutomationProperties.Name="Apps" ScrollViewer.HorizontalScrollBarVisibility="Disabled">
+            <ListBox.ItemContainerStyle>
+                <!-- Screen readers say the app's name, not the row's type. -->
+                <Style TargetType="ListBoxItem" BasedOn="{StaticResource {x:Type ListBoxItem}}">
+                    <Setter Property="AutomationProperties.Name" Value="{Binding Label}" />
+                </Style>
+            </ListBox.ItemContainerStyle>
+            <ListBox.ItemTemplate>
+                <DataTemplate>
+                    <StackPanel Orientation="Horizontal" Margin="2">
+                        <Image Source="{Binding Icon}" Width="24" Height="24" Margin="0,0,10,0" />
+                        <TextBlock Text="{Binding Label}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
+                    </StackPanel>
+                </DataTemplate>
+            </ListBox.ItemTemplate>
+        </ListBox>
+        <TextBlock x:Name="ListStatus" Grid.Row="1" HorizontalAlignment="Center" VerticalAlignment="Center" TextWrapping="Wrap"
+                   Foreground="{DynamicResource TextFillColorSecondaryBrush}" Text="Loading apps…" />
+        <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,14,0,0">
+            <Button x:Name="OkButton" Content="OK" IsDefault="True" MinWidth="90" Margin="0,0,8,0" IsEnabled="False" />
+            <Button Content="Cancel" IsCancel="True" MinWidth="90" />
+        </StackPanel>
+    </Grid>
+</Window>
diff --git a/src/NeoFences.App/AppPickerWindow.xaml.cs b/src/NeoFences.App/AppPickerWindow.xaml.cs
new file mode 100644
index 0000000..e629bfb
--- /dev/null
+++ b/src/NeoFences.App/AppPickerWindow.xaml.cs
@@ -0,0 +1,87 @@
+using System.ComponentModel;
+using System.Windows;
+using System.Windows.Data;
+using System.Windows.Input;
+using System.Windows.Threading;
+using NeoFences.Core.Items;
+using NeoFences.Shell;
+using Serilog;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// "An app…" (M19, spec 2026-10-05 §1, ADR-042): Start's All apps, listed off the UI thread, with icons and a search box.
+/// OK gives <see cref="Chosen"/>; nothing is changed here.
+/// </summary>
+public partial class AppPickerWindow : Window
+{
+    private readonly IconLoader _iconLoader;
+    private ICollectionView? _view;
+
+    /// <summary>The app picked (OK or double-click), or null.</summary>
+    public InstalledApp? Chosen { get; private set; }
+
+    public AppPickerWindow(IconLoader iconLoader)
+    {
+        InitializeComponent();
+        _iconLoader = iconLoader;
+        SearchBox.TextChanged += (_, _) => _view?.Refresh();
+        SearchBox.PreviewKeyDown += (_, key) =>
+        {
+            if (key.Key != Key.Down || AppListBox.Items.Count == 0) return;
+            AppListBox.SelectedIndex = Math.Max(AppListBox.SelectedIndex, 0); // arrow down: from the search into the list
+            (AppListBox.ItemContainerGenerator.ContainerFromIndex(AppListBox.SelectedIndex) as UIElement)?.Focus();
+            key.Handled = true;
+        };
+        AppListBox.SelectionChanged += (_, _) => OkButton.IsEnabled = AppListBox.SelectedItem is not null;
+        AppListBox.MouseDoubleClick += (_, _) => Accept();
+        OkButton.Click += (_, _) => Accept();
+        Loaded += (_, _) =>
+        {
+            SearchBox.Focus();
+            Load();
+        };
+    }
+
+    private void Load()
+    {
+        var dispatcher = Dispatcher.CurrentDispatcher;
+        // An STA thread of its own: the shell's folder enumeration expects one; ~0.3 s for ~200 apps.
+        ShellWorker.RunAlone(() =>
+        {
+            IReadOnlyList<InstalledApp>? apps = null;
+            try
+            {
+                apps = AppList.Enumerate();
+            }
+            catch (Exception failure) when (failure is not OutOfMemoryException)
+            {
+                Log.Warning(failure, "Windows did not list the apps"); // hard rule 7: this dialog says so, nothing else changes
+            }
+            dispatcher.BeginInvoke(() => Show(apps));
+        }, name: "NeoFences app list");
+    }
+
+    private void Show(IReadOnlyList<InstalledApp>? apps)
+    {
+        if (apps is null || apps.Count == 0)
+        {
+            ListStatus.Text = apps is null ? "Windows did not list the apps." : "No apps found.";
+            return;
+        }
+        ListStatus.Visibility = Visibility.Collapsed;
+        var views = apps.Select(app => new FenceItemView(new ShownItem(app.AppId, ItemKinds.AppTarget(app.AppId), app.Name))).ToList();
+        foreach (var view in views) _iconLoader.Request(view, 32);
+        AppListBox.ItemsSource = views;
+        _view = CollectionViewSource.GetDefaultView(views);
+        _view.Filter = entry => SearchBox.Text.Trim() is not { Length: > 0 } search
+                                || ((FenceItemView)entry).Label.Contains(search, StringComparison.CurrentCultureIgnoreCase);
+    }
+
+    private void Accept()
+    {
+        if (AppListBox.SelectedItem is not FenceItemView view) return;
+        Chosen = new InstalledApp(view.Key, view.Label);
+        DialogResult = true;
+    }
+}
diff --git a/src/NeoFences.App/DesktopFillWindow.xaml b/src/NeoFences.App/DesktopFillWindow.xaml
new file mode 100644
index 0000000..b51997f
--- /dev/null
+++ b/src/NeoFences.App/DesktopFillWindow.xaml
@@ -0,0 +1,27 @@
+<Window x:Class="NeoFences.App.DesktopFillWindow"
+        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
+        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
+        Title="Add from desktop" Width="560" Height="680" MinWidth="440" MinHeight="400"
+        WindowStartupLocation="CenterScreen" ShowInTaskbar="True" ThemeMode="System">
+    <!-- M19 spec 2026-10-05 §3: the desktop's entries, grouped, each group into a fence; items point at the entries themselves. -->
+    <Grid Margin="20,16,20,18">
+        <Grid.RowDefinitions>
+            <RowDefinition Height="Auto" />
+            <RowDefinition Height="*" />
+            <RowDefinition Height="Auto" />
+            <RowDefinition Height="Auto" />
+        </Grid.RowDefinitions>
+        <TextBlock TextWrapping="Wrap" Margin="0,0,0,12" Foreground="{DynamicResource TextFillColorSecondaryBrush}"
+                   Text="Each ticked item becomes a link in the fence its group goes to. Nothing on your desktop is moved or changed. Games missing? Add your games folder in Settings → Game Library, then open this again." />
+        <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
+            <StackPanel x:Name="GroupsPanel" />
+        </ScrollViewer>
+        <TextBlock x:Name="ListStatus" Grid.Row="1" HorizontalAlignment="Center" VerticalAlignment="Center" TextWrapping="Wrap"
+                   Foreground="{DynamicResource TextFillColorSecondaryBrush}" Text="Reading your desktop…" />
+        <CheckBox x:Name="HideIconsBox" Grid.Row="2" Margin="0,12,0,0" Content="Hide desktop icons while NeoFences runs" />
+        <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,14,0,0">
+            <Button x:Name="AddButton" Content="Add" IsDefault="True" MinWidth="90" Margin="0,0,8,0" IsEnabled="False" />
+            <Button Content="Cancel" IsCancel="True" MinWidth="90" />
+        </StackPanel>
+    </Grid>
+</Window>
diff --git a/src/NeoFences.App/DesktopFillWindow.xaml.cs b/src/NeoFences.App/DesktopFillWindow.xaml.cs
new file mode 100644
index 0000000..ad677e7
--- /dev/null
+++ b/src/NeoFences.App/DesktopFillWindow.xaml.cs
@@ -0,0 +1,208 @@
+using System.IO;
+using System.Windows;
+using System.Windows.Controls;
+using System.Windows.Data;
+using System.Windows.Threading;
+using NeoFences.Core.Items;
+using NeoFences.Core.Model;
+using NeoFences.Shell;
+using Serilog;
+
+namespace NeoFences.App;
+
+/// <summary>One group's choice: an existing fence (its id), a new fence (its title), or nothing (skipped).</summary>
+public sealed record DesktopFillGroup(string? FenceId, string? NewFenceTitle, IReadOnlyList<string> ItemRefs);
+
+/// <summary>What "Add" asks for (M19 §3): items per group, and the "Hide desktop icons" option.</summary>
+public sealed record DesktopFillPlan(IReadOnlyList<DesktopFillGroup> Groups, bool HideIcons);
+
+/// <summary>
+/// "Add from desktop…" (M19, spec 2026-10-05 §3, ADR-042): the desktop's entries, read off the UI thread and grouped
+/// (Games, Apps, Folders and files, Web links); each group goes to a fence of the user's choice. OK gives <see cref="Plan"/>;
+/// nothing is changed here.
+/// </summary>
+public partial class DesktopFillWindow : Window
+{
+    private const string SkipChoice = "Skip";
+    private readonly IReadOnlyList<(string Id, string Title)> _fences;
+    private readonly IReadOnlyDictionary<string, string> _alreadyIn;
+    private readonly LibrarySettings _library;
+    private readonly IconLoader _iconLoader;
+    private readonly List<(DesktopGroup Group, ComboBox PutIn, List<(CheckBox Box, string ItemRef)> Rows)> _sections = [];
+
+    public DesktopFillPlan? Plan { get; private set; }
+
+    /// <param name="fences">The user's fences (not the Game Library), by id and title.</param>
+    /// <param name="alreadyIn">Targets already held by an item → the title of a fence holding it.</param>
+    /// <param name="iconsHidden">"Hide desktop icons" is on already: the option is not offered.</param>
+    /// <param name="library">The Game Library's settings: its scan tells which desktop entries are games.</param>
+    public DesktopFillWindow(IReadOnlyList<(string Id, string Title)> fences, IReadOnlyDictionary<string, string> alreadyIn,
+        LibrarySettings library, IconLoader iconLoader, bool iconsHidden)
+    {
+        InitializeComponent();
+        _fences = fences;
+        _alreadyIn = alreadyIn;
+        _library = library;
+        _iconLoader = iconLoader;
+        HideIconsBox.Visibility = iconsHidden ? Visibility.Collapsed : Visibility.Visible;
+        AddButton.Click += (_, _) => Accept();
+        Loaded += (_, _) => Load();
+    }
+
+    public static string GroupTitle(DesktopGroup group) => group switch
+    {
+        DesktopGroup.Games => "Games",
+        DesktopGroup.Apps => "Apps",
+        DesktopGroup.WebLinks => "Web links",
+        _ => "Folders and files",
+    };
+
+    private void Load()
+    {
+        var dispatcher = Dispatcher.CurrentDispatcher;
+        var library = _library;
+        // An STA thread of its own: shortcuts are read through the shell's link object.
+        ShellWorker.RunAlone(() =>
+        {
+            List<(DesktopGroup Group, string ItemRef)>? sorted = null;
+            try
+            {
+                var (gameFolders, knownGames) = KnownGames(library);
+                sorted = [.. DesktopItems.Enumerate().ItemRefs.Select(itemRef => (DesktopSorting.GroupOf(Read(itemRef), gameFolders, knownGames), itemRef))];
+            }
+            catch (Exception failure) when (failure is not OutOfMemoryException)
+            {
+                Log.Warning(failure, "the desktop could not be read for Add from desktop");
+            }
+            dispatcher.BeginInvoke(() => Show(sorted));
+        }, name: "NeoFences desktop fill");
+    }
+
+    /// <summary>
+    /// What the Game Library's scan knows (read-only; it writes nothing): the user's game folders plus every scanned game's
+    /// install folder, and the desktop shortcuts it counts as games. A scan that fails leaves the user's folders only.
+    /// </summary>
+    private static (IReadOnlyList<string> Folders, IReadOnlySet<string> Shortcuts) KnownGames(LibrarySettings library)
+    {
+        try
+        {
+            var games = GameScanners.ScanAll(library, (source, failure) => Log.Debug(failure, "game scan {Source} failed for Add from desktop", source))
+                .SelectMany(scan => scan.Games).ToList();
+            IReadOnlyList<string> folders = [.. library.Folders, .. games.Select(game => game.InstallFolder).OfType<string>().Where(folder => folder.TrimEnd('\\').Length > 3)];
+            return (folders, games.Select(game => game.ShortcutFile).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase));
+        }
+        catch (Exception failure) when (failure is not OutOfMemoryException)
+        {
+            Log.Warning(failure, "the games could not be scanned for Add from desktop; launcher links and game folders still count");
+            return (library.Folders, new HashSet<string>());
+        }
+    }
+
+    /// <summary>One entry's facts; a shortcut that cannot be read counts as a file (spec §3: never a failure).</summary>
+    private static DesktopEntry Read(string itemRef)
+    {
+        if (itemRef.StartsWith("::", StringComparison.Ordinal)) return new DesktopEntry(itemRef, IsFolder: false, LinkTarget: null, LinkArguments: null);
+        try
+        {
+            var isFolder = Directory.Exists(itemRef);
+            if (isFolder || Path.GetExtension(itemRef).ToLowerInvariant() is not (".lnk" or ".url")) return new DesktopEntry(itemRef, isFolder, null, null);
+            // A shortcut with no file path (a Store app's) reads as nothing: its target is "", which sorts it with the apps.
+            var launch = ShellLinks.Read(itemRef);
+            return new DesktopEntry(itemRef, IsFolder: false, LinkTarget: launch?.Target ?? "", LinkArguments: launch?.Arguments);
+        }
+        catch (Exception failure) when (failure is not OutOfMemoryException)
+        {
+            Log.Debug(failure, "desktop entry {ItemRef} could not be read; listed with the files", itemRef);
+            return new DesktopEntry(itemRef, IsFolder: false, LinkTarget: null, LinkArguments: null);
+        }
+    }
+
+    private void Show(List<(DesktopGroup Group, string ItemRef)>? sorted)
+    {
+        if (sorted is null || sorted.Count == 0)
+        {
+            ListStatus.Text = sorted is null ? "Your desktop could not be read." : "Your desktop has nothing to add.";
+            return;
+        }
+        ListStatus.Visibility = Visibility.Collapsed;
+        foreach (var group in Enum.GetValues<DesktopGroup>())
+        {
+            var itemRefs = sorted.Where(entry => entry.Group == group).Select(entry => entry.ItemRef).ToList();
+            if (itemRefs.Count > 0) AddSection(group, itemRefs);
+        }
+        UpdateAddButton();
+    }
+
+    private void AddSection(DesktopGroup group, IReadOnlyList<string> itemRefs)
+    {
+        var title = GroupTitle(group);
+        var header = new DockPanel { Margin = new Thickness(0, _sections.Count == 0 ? 0 : 18, 0, 6) };
+        var putIn = new ComboBox { MinWidth = 200 };
+        AutomationPropertiesName(putIn, $"Put {title} in");
+        var sameTitle = _fences.FirstOrDefault(fence => string.Equals(fence.Title, title, StringComparison.CurrentCultureIgnoreCase));
+        foreach (var fence in _fences) putIn.Items.Add(new ComboBoxItem { Content = fence.Title, Tag = fence.Id });
+        putIn.Items.Add(new ComboBoxItem { Content = $"New fence: {title}", Tag = null });
+        putIn.Items.Add(new ComboBoxItem { Content = SkipChoice, Tag = SkipChoice });
+        putIn.SelectedIndex = sameTitle.Id is { } existing ? _fences.ToList().FindIndex(fence => fence.Id == existing) : _fences.Count;
+        putIn.SelectionChanged += (_, _) => UpdateAddButton();
+        var putInRow = new StackPanel { Orientation = Orientation.Horizontal };
+        putInRow.Children.Add(new TextBlock { Text = "Put in:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
+        putInRow.Children.Add(putIn);
+        DockPanel.SetDock(putInRow, Dock.Right);
+        header.Children.Add(putInRow);
+        header.Children.Add(new TextBlock { Text = $"{title} ({itemRefs.Count})", FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
+        GroupsPanel.Children.Add(header);
+
+        var rows = new List<(CheckBox, string)>();
+        foreach (var itemRef in itemRefs)
+        {
+            var view = new FenceItemView(new ShownItem(itemRef, itemRef));
+            _iconLoader.Request(view, 24);
+            var content = new StackPanel { Orientation = Orientation.Horizontal };
+            var icon = new Image { Width = 24, Height = 24, Margin = new Thickness(0, 0, 8, 0) };
+            icon.SetBinding(Image.SourceProperty, new Binding(nameof(FenceItemView.Icon)) { Source = view });
+            var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
+            name.SetBinding(TextBlock.TextProperty, new Binding(nameof(FenceItemView.Label)) { Source = view });
+            content.Children.Add(icon);
+            content.Children.Add(name);
+            var already = _alreadyIn.TryGetValue(itemRef, out var fenceTitle);
+            if (already)
+            {
+                content.Children.Add(new TextBlock
+                {
+                    Text = $"already in {fenceTitle}", Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
+                    Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorSecondaryBrush"),
+                });
+            }
+            var box = new CheckBox { Content = content, IsChecked = !already, Margin = new Thickness(4, 2, 0, 2) };
+            box.Checked += (_, _) => UpdateAddButton();
+            box.Unchecked += (_, _) => UpdateAddButton();
+            GroupsPanel.Children.Add(box);
+            rows.Add((box, itemRef));
+        }
+        _sections.Add((group, putIn, rows));
+    }
+
+    private static void AutomationPropertiesName(DependencyObject element, string name) =>
+        System.Windows.Automation.AutomationProperties.SetName(element, name);
+
+    private static bool IsSkipped(ComboBox putIn) => (putIn.SelectedItem as ComboBoxItem)?.Tag as string == SkipChoice;
+
+    private void UpdateAddButton() =>
+        AddButton.IsEnabled = _sections.Any(section => !IsSkipped(section.PutIn) && section.Rows.Any(row => row.Box.IsChecked == true));
+
+    private void Accept()
+    {
+        var groups = new List<DesktopFillGroup>();
+        foreach (var (group, putIn, rows) in _sections)
+        {
+            if (IsSkipped(putIn)) continue;
+            var itemRefs = rows.Where(row => row.Box.IsChecked == true).Select(row => row.ItemRef).ToList();
+            if (itemRefs.Count == 0) continue;
+            var fenceId = (putIn.SelectedItem as ComboBoxItem)?.Tag as string;
+            groups.Add(new DesktopFillGroup(fenceId, fenceId is null ? GroupTitle(group) : null, itemRefs));
+        }
+        Plan = new DesktopFillPlan(groups, HideIcons: HideIconsBox.IsVisible && HideIconsBox.IsChecked == true);
+        DialogResult = true;
+    }
+}
diff --git a/src/NeoFences.App/FenceHost.DesktopFill.cs b/src/NeoFences.App/FenceHost.DesktopFill.cs
new file mode 100644
index 0000000..65dc2c5
--- /dev/null
+++ b/src/NeoFences.App/FenceHost.DesktopFill.cs
@@ -0,0 +1,63 @@
+using NeoFences.Core.Items;
+using NeoFences.Core.Model;
+using Serilog;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// "Add from desktop…" (M19, spec 2026-10-05 §3, ADR-042): from the fence menu or the tray. Items point at the desktop
+/// entries themselves, exactly like a drag from the desktop; nothing on the desktop is moved or changed.
+/// </summary>
+public sealed partial class FenceHost
+{
+    private bool _desktopFillOpen;
+
+    private void ShowDesktopFill()
+    {
+        if (_desktopFillOpen) return; // one window at a time
+        var fences = _config.Fences.Where(fence => !fence.IsLibrary).Select(fence => (fence.Id, fence.Title)).ToList();
+        var titles = fences.ToDictionary(fence => fence.Id, fence => fence.Title, StringComparer.Ordinal);
+        // Each target already held by an item → a fence holding it (shown as "already in …", unticked).
+        var alreadyIn = new Dictionary<string, string>(ItemKinds.Comparer);
+        foreach (var (fenceId, items) in _items.Fences)
+        {
+            if (!titles.TryGetValue(fenceId, out var title)) continue;
+            foreach (var item in items) alreadyIn.TryAdd(item.Target, title);
+        }
+        var dialog = new DesktopFillWindow(fences, alreadyIn, _config.Library, _iconLoader, iconsHidden: _config.Settings.HideDesktopIcons);
+        _desktopFillOpen = true;
+        try
+        {
+            if (dialog.ShowDialog() == true && dialog.Plan is { } plan) ApplyDesktopFill(plan);
+        }
+        finally
+        {
+            _desktopFillOpen = false;
+        }
+    }
+
+    /// <summary>New fences first (placed in free space like "New fence"), then the items, one save; then the hide option.</summary>
+    private void ApplyDesktopFill(DesktopFillPlan plan)
+    {
+        var added = 0;
+        var newFences = 0;
+        foreach (var group in plan.Groups)
+        {
+            var fenceId = group.FenceId;
+            if (fenceId is null || _config.Fences.All(fence => fence.Id != fenceId))
+            {
+                (_config, var fence) = FenceEdits.CreateFence(_config, group.NewFenceTitle ?? "New fence");
+                fenceId = fence.Id;
+                newFences++;
+            }
+            var result = ItemEdits.Add(_items, fenceId, [.. group.ItemRefs.Select(VirtualItem.Create)]);
+            _items = result.Document;
+            added += result.AddedIds.Count;
+        }
+        Log.Information("Add from desktop: {Added} item(s) added, {NewFences} new fence(s)", added, newFences);
+        SyncBoxes();
+        ItemsChanged(checkTargets: [.. plan.Groups.SelectMany(group => group.ItemRefs)]);
+        SaveNow();
+        if (plan.HideIcons) SetHideDesktopIcons(true);
+    }
+}
diff --git a/src/NeoFences.App/FenceHost.Items.cs b/src/NeoFences.App/FenceHost.Items.cs
index 5e6718e..3f9b300 100644
--- a/src/NeoFences.App/FenceHost.Items.cs
+++ b/src/NeoFences.App/FenceHost.Items.cs
@@ -71,6 +71,8 @@ public sealed partial class FenceHost
     private static string DisplayName(VirtualItem item) => item.OwnName ?? item.Kind switch
     {
         ItemKind.Website => ItemKinds.WebsiteName(item.Target),
+        _ when ItemKinds.AppIdOf(item.Target) is { } appId => appId.Split('_', '!')[0], // "SpotifyAB.SpotifyMusic" until Windows names it
+
         _ => Path.GetFileNameWithoutExtension(item.Target.TrimEnd('\\')) is { Length: > 0 } name ? name : item.Target,
     };
 
@@ -131,13 +133,31 @@ public sealed partial class FenceHost
         window.ShowItemMenu(menu, fromKeyboard);
     }
 
-    /// <summary>Windows' full menu for the real target (spec §3): only here can a real delete or rename happen, and it says so.</summary>
-    private static void ShowWindowsMenu(FenceWindow window, VirtualItem item, int screenX, int screenY)
+    /// <summary>
+    /// Windows' full menu for the real target (spec §3): only here can a real delete or rename happen, and it says so. The
+    /// target is checked off the UI thread first (M19 R2): Windows builds the menu on the UI thread, and a dead share would
+    /// freeze the fence; not reachable within 2 s, a one-line menu says so.
+    /// </summary>
+    private void ShowWindowsMenu(FenceWindow window, VirtualItem item, int screenX, int screenY)
     {
         if (item.Kind == ItemKind.Website) return; // a website has no Windows menu
-        ShellItemMenu.Show(window.Handle, [item.Target], screenX, screenY, extended: true,
-            logFailure: failure => Log.Warning(failure, "Windows' menu or its command failed for {Target}", item.Target),
-            header: "Windows menu — acts on the real file", customCommands: [], handDeleteBack: false, out _);
+        Task.Run(() => TargetProbe.Check(item.Target)).ContinueWith(checking =>
+        {
+            if (checking.Result.State == TargetState.Unavailable)
+            {
+                var menu = new ContextMenu();
+                menu.Items.Add(new MenuItem
+                {
+                    Header = TargetChecks.IsNetworkPath(item.Target) ? "Network location not reachable" : "Drive not connected",
+                    IsEnabled = false,
+                });
+                window.ShowItemMenu(menu, fromKeyboard: false);
+                return;
+            }
+            ShellItemMenu.Show(window.Handle, [item.Target], screenX, screenY, extended: true,
+                logFailure: failure => Log.Warning(failure, "Windows' menu or its command failed for {Target}", item.Target),
+                header: "Windows menu — acts on the real file", customCommands: [], handDeleteBack: false, out _);
+        }, TaskScheduler.FromCurrentSynchronizationContext());
     }
 
     private static void ShowInFolder(string target) =>
@@ -200,10 +220,19 @@ public sealed partial class FenceHost
         window.SelectItems(added.AddedIds.Count > 0 ? added.AddedIds : added.AlreadyThereIds); // already there: it flashes
     }
 
-    /// <summary>Locate… (spec §4): a picker at the nearest folder that still exists; only the target changes.</summary>
+    /// <summary>
+    /// Locate… (spec §4): a picker at the nearest folder that still exists; only the target changes. A missing app opens
+    /// the app list (M19 §1). Afterwards the other items from the same old place are offered (M19 §2).
+    /// </summary>
     private void Locate(FenceWindow window, string itemId)
     {
         if (_items.Find(itemId) is not { } item) return;
+        if (ItemKinds.IsApp(item.Target))
+        {
+            var picker = new AppPickerWindow(_iconLoader) { Owner = window };
+            if (picker.ShowDialog() == true && picker.Chosen is { } app) Relocated(window, itemId, ItemKinds.AppTarget(app.AppId));
+            return;
+        }
         var name = DisplayName(item);
         var asFolder = !Path.HasExtension(item.Target.TrimEnd('\\')); // ponytail: a folder named "x.y" opens the file picker; Properties → Browse covers it
         // Off the UI thread: the old drive may be a sleeping disk or a share that does not answer.
@@ -213,15 +242,66 @@ public sealed partial class FenceHost
             var picked = asFolder
                 ? PathPicker.TryPickFolder(window.Handle, $"Where is \"{name}\" now?", LogFailure, found.Result)
                 : PathPicker.TryPickFile(window.Handle, $"Where is \"{name}\" now?", LogFailure, found.Result);
-            if (picked is null || _items.Find(itemId) is not { } current) return;
-            _items = ItemEdits.Replace(_items, current with { Target = picked });
-            Log.Information("item {ItemId} located at {Target}", itemId, picked);
-            ItemsChanged(checkTargets: [picked]);
+            if (picked is not null) Relocated(window, itemId, picked);
         }, TaskScheduler.FromCurrentSynchronizationContext());
     }
 
+    /// <summary>One item now points at its new place; then the bulk fix is offered for its neighbours.</summary>
+    private void Relocated(FenceWindow window, string itemId, string newTarget)
+    {
+        if (_items.Find(itemId) is not { } current) return;
+        var oldTarget = current.Target;
+        _items = ItemEdits.Replace(_items, current with { Target = newTarget });
+        Log.Information("item {ItemId} located at {Target}", itemId, newTarget);
+        ItemsChanged(checkTargets: [newTarget]);
+        OfferRelocation(window, itemId, oldTarget, newTarget);
+    }
+
+    /// <summary>
+    /// The bulk fix (M19 §2, ADR-042): the other missing or unavailable items under the same old place whose files are at
+    /// the new place are offered in one question; Fix saves an undo snapshot first.
+    /// </summary>
+    private void OfferRelocation(FenceWindow window, string locatedId, string oldTarget, string newTarget)
+    {
+        if (Relocation.Find(oldTarget, newTarget) is not { } bases) return;
+        var moves = Relocation.Candidates(_items, StateOf, bases, exceptItemId: locatedId);
+        if (moves.Count == 0) return;
+        // Off the UI thread: each new place is checked (a share answers within 2 s or does not count).
+        Task.Run(() => TargetProbe.CheckAll([.. moves.Select(move => move.NewTarget)])).ContinueWith(checking =>
+        {
+            if (checking.IsFaulted) return;
+            var found = moves.Where((_, index) => checking.Result[index].Check.State == TargetState.Ok).ToList();
+            if (found.Count == 0) return;
+            var labels = found.Select(move => _items.Find(move.ItemId)).OfType<VirtualItem>().Select(DisplayName).ToList();
+            var question = new RelocateWindow(found.Count, bases, labels) { Owner = _windows.ContainsValue(window) ? window : null };
+            if (question.ShowDialog() == true) FixItems(found, bases);
+        }, TaskScheduler.FromCurrentSynchronizationContext());
+    }
+
+    /// <summary>"Fix": an undo snapshot first (tray → "Undo the last restore or fix"); not saved: nothing changes.</summary>
+    private void FixItems(IReadOnlyList<Relocation.Move> moves, Relocation.Bases bases)
+    {
+        var now = DateTimeOffset.Now;
+        if (_snapshots.Save(Snapshots.Take(_config, _items, name: $"Before fixing {moves.Count} items ({now:d MMM HH:mm})", now: now),
+                SnapshotStore.BeforeRestoreFileName) is null)
+        {
+            Log.Warning(_snapshots.LastFailure, "items not fixed: the undo snapshot could not be saved");
+            SnapshotFailure("Items not fixed", "NeoFences could not save the undo snapshot first (see the log).");
+            return;
+        }
+        // Only items still pointing where they did when asked (one may have been removed or changed meanwhile).
+        var newTargets = moves.Where(move => _items.Find(move.ItemId) is { } item && ItemKinds.Comparer.Equals(item.Target, move.OldTarget))
+            .ToDictionary(move => move.ItemId, move => move.NewTarget, StringComparer.Ordinal);
+        _items = ItemEdits.Relocate(_items, newTargets);
+        Log.Information("{Count} item(s) fixed: {OldBase} -> {NewBase}", newTargets.Count, bases.OldBase, bases.NewBase);
+        ItemsChanged(checkTargets: [.. newTargets.Values]);
+        RefreshSettings(); // the snapshot list shows the undo snapshot
+    }
+
     private static string? NearestExistingFolder(string target)
     {
+        // A drive or share that does not answer within 2 s: the dialog opens at Windows' default place (M19 R2).
+        if (TargetChecks.RootOf(target) is { } root && TargetProbe.Check(root).State != TargetState.Ok) return null;
         try
         {
             for (var folder = Path.GetDirectoryName(target.TrimEnd('\\')); folder is not null; folder = Path.GetDirectoryName(folder))
@@ -297,6 +377,7 @@ public sealed partial class FenceHost
         RefreshWindows();
         ScheduleSave();
         UpdateWatching();
+        ForgetGoneTargets();
         if (checkTargets.Count > 0) CheckTargets(checkTargets);
     }
 
@@ -343,8 +424,13 @@ public sealed partial class FenceHost
         else
         {
             var items = keys.Select(_items.Find).OfType<VirtualItem>().ToList();
-            files = [.. items.Where(item => item.Kind == ItemKind.Path && TargetChecks.RootOf(item.Target) is not null && CheckOf(item.Target).State == TargetState.Ok)
-                .Select(item => item.Target)];
+            var onDisk = items.Where(item => item.Kind == ItemKind.Path && TargetChecks.RootOf(item.Target) is not null && CheckOf(item.Target).State == TargetState.Ok)
+                .Select(item => item.Target).ToList();
+            // Asked again now, at most 2 s per drive (M19 R2): Windows parses each one on this thread, and a share that went
+            // away since its last check would freeze the fence. A target that does not answer is left out of the drag.
+            var answers = onDisk.Count > 0 ? TargetProbe.CheckAll(onDisk) : [];
+            files = [.. answers.Where(answer => answer.Check.State == TargetState.Ok).Select(answer => answer.Target)];
+            foreach (var (target, _) in answers.Where(answer => answer.Check.State != TargetState.Ok)) Log.Information("{Target} left out of the drag: not reachable", target);
             urls = [.. items.Where(item => item.Kind == ItemKind.Website).Select(item => item.Target)];
         }
         ShellDragDrop.TryDrag(window.Handle, keys, files, urls, logFailure: failure => Log.Warning(failure, "could not start dragging {Keys}", keys));
diff --git a/src/NeoFences.App/FenceHost.Watching.cs b/src/NeoFences.App/FenceHost.Watching.cs
index 4647277..c3f1100 100644
--- a/src/NeoFences.App/FenceHost.Watching.cs
+++ b/src/NeoFences.App/FenceHost.Watching.cs
@@ -23,6 +23,9 @@ public sealed partial class FenceHost
     private readonly Dictionary<string, (FolderWatcher Watcher, DeviceRemovalNotice? Notice)> _targetWatchers = new(ItemKinds.Comparer);
     private HashSet<string> _wantedFolders = new(ItemKinds.Comparer);
     private readonly HashSet<string> _armingFolders = new(ItemKinds.Comparer); // watchers being opened off the UI thread
+    // Removal notices registered by a batch still arming, by handle (M19 R1, like LibraryLister._inFlight): a drive removed
+    // meanwhile is let go of at once; the batch then drops that folder.
+    private readonly System.Collections.Concurrent.ConcurrentDictionary<nint, (FolderWatcher Watcher, DeviceRemovalNotice Notice)> _armingNotices = new();
     private readonly RefreshThrottle _refreshThrottle = new();
     private readonly Dictionary<string, DispatcherTimer> _fenceRefreshTimers = new(StringComparer.Ordinal);
     private readonly HashSet<string> _changedFolders = new(ItemKinds.Comparer);
@@ -60,6 +63,12 @@ public sealed partial class FenceHost
             notice?.Dispose();
         }
         _targetWatchers.Clear();
+        foreach (var (watcher, notice) in _armingNotices.Values)
+        {
+            watcher.Dispose();
+            notice.Dispose();
+        }
+        _armingNotices.Clear();
     }
 
     /// <summary>Watches the folders the plan picks now; watchers no longer wanted, not watching or stopped (final review I3) are replaced.</summary>
@@ -78,40 +87,69 @@ public sealed partial class FenceHost
         var noticeOwner = _messages.Handle;
         var dispatcher = Dispatcher.CurrentDispatcher;
         // Off the UI thread: opening a watcher on a sleeping disk or a share can take seconds (final review I2).
-        Task.Run(() => toArm.Select(folder => (Folder: folder, Watch: Watch(folder, noticeOwner))).ToList()).ContinueWith(armed =>
+        Task.Run(() => toArm.Select(folder => (Folder: folder, Watch: TryWatch(folder, noticeOwner))).ToList()).ContinueWith(armed =>
         {
+            if (armed.IsFaulted)
+            {
+                // Never silently lost (M19 R4): logged, and the next UpdateWatching (an edit, a drive, the 5-minute check) retries.
+                Log.Warning(armed.Exception, "watchers for {Count} folder(s) could not be armed; retried at the next check", toArm.Count);
+                foreach (var folder in toArm) _armingFolders.Remove(folder);
+                return;
+            }
             foreach (var (folder, watch) in armed.Result)
             {
                 _armingFolders.Remove(folder);
+                if (watch is null) continue; // logged in TryWatch; retried at the next check
+                var released = watch.Value.Notice is { } armedNotice && !_armingNotices.TryRemove(armedNotice.Handle, out _);
+                if (released) continue; // its drive was removed while it armed: already let go of (M19 R1)
                 if (_watchingStopped || !_wantedFolders.Contains(folder) || _targetWatchers.ContainsKey(folder))
                 {
-                    watch.Watcher.Dispose();
-                    watch.Notice?.Dispose();
+                    watch.Value.Watcher.Dispose();
+                    watch.Value.Notice?.Dispose();
                     continue;
                 }
-                watch.Watcher.Changed += () => dispatcher.BeginInvoke(() => OnTargetFolderChanged(folder));
-                watch.Watcher.Renamed += (oldPath, newPath) => dispatcher.BeginInvoke(() => OnTargetRenamed(oldPath, newPath));
+                var (watcher, _) = watch.Value;
+                watcher.Changed += () => dispatcher.BeginInvoke(() => OnTargetFolderChanged(folder));
+                watcher.Renamed += (oldPath, newPath) => dispatcher.BeginInvoke(() => OnTargetRenamed(oldPath, newPath));
                 // Events were lost or it stopped: its targets are checked now; the next UpdateWatching (an edit, a drive, the 5-minute check) re-arms it.
-                watch.Watcher.Failed += () => dispatcher.BeginInvoke(() => OnTargetFolderChanged(folder));
-                _targetWatchers[folder] = watch;
+                watcher.Failed += () => dispatcher.BeginInvoke(() => OnTargetFolderChanged(folder));
+                _targetWatchers[folder] = watch.Value;
             }
         }, TaskScheduler.FromCurrentSynchronizationContext());
     }
 
-    /// <summary>A watcher, with a removal notice where Windows offers one, so "Safely remove" still works (M8d). Off the UI thread.</summary>
-    private static (FolderWatcher Watcher, DeviceRemovalNotice? Notice) Watch(string folder, nint noticeOwner)
+    /// <summary>
+    /// A watcher, with a removal notice where Windows offers one, so "Safely remove" still works (M8d). Off the UI thread.
+    /// Null when arming failed in a way FolderWatcher does not handle (logged; M19 R4): the batch goes on without it.
+    /// </summary>
+    private (FolderWatcher Watcher, DeviceRemovalNotice? Notice)? TryWatch(string folder, nint noticeOwner)
     {
-        // Names only: a game writing logs and caches next to its .exe must not wake NeoFences on every write (final review I4).
-        var watcher = new FolderWatcher(folder, failure => Log.Debug(failure, "cannot watch {Folder}; its items are checked every few minutes", folder), namesOnly: true);
-        var notice = watcher.HeldFolder is { } held
-            ? DeviceRemovalNotice.TryRegister(noticeOwner, held, failure => Log.Debug(failure, "no removal notice for {Folder}", held))
-            : null;
-        return (watcher, notice);
+        try
+        {
+            // Names only: a game writing logs and caches next to its .exe must not wake NeoFences on every write (final review I4).
+            var watcher = new FolderWatcher(folder, failure => Log.Debug(failure, "cannot watch {Folder}; its items are checked every few minutes", folder), namesOnly: true);
+            var notice = watcher.HeldFolder is { } held
+                ? DeviceRemovalNotice.TryRegister(noticeOwner, held, failure => Log.Debug(failure, "no removal notice for {Folder}", held))
+                : null;
+            if (notice is not null) _armingNotices[notice.Handle] = (watcher, notice);
+            return (watcher, notice);
+        }
+        catch (Exception failure) when (failure is not OutOfMemoryException)
+        {
+            Log.Warning(failure, "could not watch {Folder}; retried at the next check", folder);
+            return null;
+        }
     }
 
-    /// <summary>Windows asks to remove a drive holding a watched folder (the test stick, G:): let go of it now.</summary>
+    /// <summary>Windows asks to remove a drive holding a watched folder (the test stick, G:), or it was pulled: let go of it now.</summary>
     private bool ReleaseTargetWatcherForRemoval(nint handle)
     {
+        if (_armingNotices.TryRemove(handle, out var arming))
+        {
+            arming.Watcher.Dispose(); // still arming (M19 R1): the batch sees it gone and drops the folder
+            arming.Notice.Dispose();
+            return true;
+        }
         if (_targetWatchers.FirstOrDefault(entry => entry.Value.Notice?.Handle == handle) is not { Key: { } folder, Value: var (watcher, notice) }) return false;
         watcher.Dispose();
         notice?.Dispose();
@@ -222,12 +260,29 @@ public sealed partial class FenceHost
     }
 
     private void CheckFence(string fenceId) =>
-        CheckTargets([.. _items.Of(fenceId).Where(item => item.Kind == ItemKind.Path).Select(item => item.Target).Distinct(ItemKinds.Comparer)]);
+        CheckTargets([.. _items.Of(fenceId).Where(item => item.Kind == ItemKind.Path || ItemKinds.IsApp(item.Target))
+            .Select(item => item.Target).Distinct(ItemKinds.Comparer)]);
 
     private void CheckAllTargets()
     {
         UpdateWatching();
-        CheckTargets(ItemEdits.PathTargets(_items));
+        CheckTargets(ItemEdits.CheckedTargets(_items)); // files, folders and apps (M19)
+    }
+
+    /// <summary>
+    /// Records of targets and fences that are gone (M19 R5): their last check, check batch and refresh timer. After an items
+    /// change and after Delete fence, so a long session does not keep every target it ever saw.
+    /// </summary>
+    private void ForgetGoneTargets()
+    {
+        var live = ItemEdits.CheckedTargets(_items);
+        foreach (var target in StaleEntries.Gone(_targetChecks.Keys, live, ItemKinds.Comparer)) _targetChecks.Remove(target);
+        foreach (var target in StaleEntries.Gone(_checkBatchOf.Keys, live, ItemKinds.Comparer)) _checkBatchOf.Remove(target);
+        foreach (var fenceId in StaleEntries.Gone(_fenceRefreshTimers.Keys, _config.Fences.Select(fence => fence.Id), StringComparer.Ordinal))
+        {
+            _fenceRefreshTimers[fenceId].Stop();
+            _fenceRefreshTimers.Remove(fenceId);
+        }
     }
 
     /// <summary>Checks these targets off the UI thread (a share answers within 2 s or counts as Unavailable); games defer it.</summary>
diff --git a/src/NeoFences.App/FenceHost.cs b/src/NeoFences.App/FenceHost.cs
index 1219c16..7f945b6 100644
--- a/src/NeoFences.App/FenceHost.cs
+++ b/src/NeoFences.App/FenceHost.cs
@@ -77,6 +77,7 @@ public sealed partial class FenceHost
     private const int TrayTakeSnapshot = 7, TrayRestoreMenu = 8, TrayRestoreBefore = 9, TrayRestoreFirst = 100, TrayRestoreCount = 10;
     private const int TrayNewLibrary = 11; // M12
     private const int TraySnapshotsSettings = 12; // M13c: "More in Settings…" opens the Snapshots card
+    private const int TrayAddFromDesktop = 14; // M19 §3 (13 is TrayRestartToUpdate)
     private SettingsWindow? _settingsWindow; // M6b: one at a time
 
     public event Action? ExitRequested;
@@ -253,6 +254,7 @@ public sealed partial class FenceHost
         window.RemoveRequested += keys => RemoveItems(window, keys);
         window.PropertiesRequested += (key, focusName) => ShowProperties(window, key, focusName);
         window.AddItemRequested += () => AddItem(window);
+        window.AddFromDesktopRequested += ShowDesktopFill;
         window.RefreshRequested += () => RefreshFence(window);
         window.DrivesChanged += OnDrivesChanged;
         window.NewLibraryRequested += CreateLibraryFence;
@@ -475,6 +477,7 @@ public sealed partial class FenceHost
         SaveNow();
         SyncBoxes(); // closes the window when its box is gone
         UpdateWatching();
+        ForgetGoneTargets();
     }
 
     private void OnThemeChanged()
@@ -1153,7 +1156,7 @@ public sealed partial class FenceHost
         if (beforeRestore is not null)
         {
             if (restoreItems.Count > 0) restoreItems.Add(TrayMenuItem.Separator); // never a separator first (M13c)
-            restoreItems.Add(new TrayMenuItem(TrayRestoreBefore, "Undo the last restore"));
+            restoreItems.Add(new TrayMenuItem(TrayRestoreBefore, "Undo the last restore or fix")); // a bulk fix shares the slot (M19 §2)
         }
         if (restoreItems.Count > 0) restoreItems.Add(TrayMenuItem.Separator);
         restoreItems.Add(new TrayMenuItem(TraySnapshotsSettings, "More in Settings…"));
@@ -1162,6 +1165,7 @@ public sealed partial class FenceHost
             .. UpdateTrayItems(), // M17: "Restart to update to v…" first while an update waits
             new TrayMenuItem(TrayNewFence, "New fence", Enabled: !_paused),
             new TrayMenuItem(TrayNewLibrary, "New Game Library fence", Enabled: !_paused),
+            new TrayMenuItem(TrayAddFromDesktop, "Add from desktop…", Enabled: !_paused),
             new TrayMenuItem(TrayQuickHide, "Quick-hide", Checked: _quickHidden, Enabled: !_paused),
             new TrayMenuItem(TrayPeek, $"Peek\t{PeekHotkeyDisplay}", Checked: _peeking, Enabled: !_paused),
             TrayMenuItem.Separator,
@@ -1180,6 +1184,10 @@ public sealed partial class FenceHost
                 CreateFence();
                 break;
             case TrayNewLibrary: CreateLibraryFence(); break;
+            case TrayAddFromDesktop:
+                SetQuickHidden(false); // the new items must be seen landing
+                ShowDesktopFill();
+                break;
             case TrayQuickHide: SetQuickHidden(!_quickHidden); break;
             case TrayPeek: SetPeek(!_peeking); break;
             case TrayPause: SetPaused(!_paused); break;
diff --git a/src/NeoFences.App/FenceItemView.cs b/src/NeoFences.App/FenceItemView.cs
index f10de9c..8f83505 100644
--- a/src/NeoFences.App/FenceItemView.cs
+++ b/src/NeoFences.App/FenceItemView.cs
@@ -83,10 +83,14 @@ public sealed class FenceItemView : INotifyPropertyChanged
         return reload;
     }
 
-    /// <summary>Until Windows' display name arrives: the file name, a website's host; nothing for special items.</summary>
+    /// <summary>
+    /// Until Windows' display name arrives: the file name, a website's host, an app's id front (an uninstalled app is never
+    /// named by Windows, M19); nothing for other special items.
+    /// </summary>
     private static string PlaceholderName(string target) => ItemKinds.Of(target) switch
     {
         ItemKind.Website => ItemKinds.WebsiteName(target),
+        _ when ItemKinds.AppIdOf(target) is { } appId => appId.Split('_', '!')[0],
         ItemKind.Special => "",
         _ => Path.GetFileNameWithoutExtension(target.TrimEnd('\\')) is { Length: > 0 } name ? name : target,
     };
diff --git a/src/NeoFences.App/FenceWindow.xaml b/src/NeoFences.App/FenceWindow.xaml
index c2d1be4..9dbac79 100644
--- a/src/NeoFences.App/FenceWindow.xaml
+++ b/src/NeoFences.App/FenceWindow.xaml
@@ -77,6 +77,7 @@
                 <Border.ContextMenu>
                     <ContextMenu x:Name="BodyContextMenu">
                         <MenuItem x:Name="AddItemItem" Header="Add item…" />
+                        <MenuItem x:Name="AddFromDesktopItem" Header="Add from desktop…" />
                         <MenuItem x:Name="RefreshItem" Header="Refresh" />
                         <Separator />
                         <MenuItem x:Name="NewFenceItem" Header="New fence" />
diff --git a/src/NeoFences.App/FenceWindow.xaml.cs b/src/NeoFences.App/FenceWindow.xaml.cs
index d563d86..c3b7f72 100644
--- a/src/NeoFences.App/FenceWindow.xaml.cs
+++ b/src/NeoFences.App/FenceWindow.xaml.cs
@@ -141,6 +141,8 @@ public partial class FenceWindow : Window
     public event Action<FenceSort>? SortRequested;
     /// <summary>Fence menu → "Add item…" (M18).</summary>
     public event Action? AddItemRequested;
+    /// <summary>Fence menu → "Add from desktop…" (M19).</summary>
+    public event Action? AddFromDesktopRequested;
     /// <summary>Fence menu → "Refresh": the items' targets are checked again and their icons reloaded (the library rescans).</summary>
     public event Action? RefreshRequested;
     /// <summary>Fence menu → "New Game Library fence" (M12).</summary>
@@ -197,6 +199,7 @@ public partial class FenceWindow : Window
         PreviewKeyDown += OnTabKeys;
         NewLibraryItem.Click += (_, _) => NewLibraryRequested?.Invoke();
         AddItemItem.Click += (_, _) => AddItemRequested?.Invoke();
+        AddFromDesktopItem.Click += (_, _) => AddFromDesktopRequested?.Invoke();
         RefreshItem.Click += (_, _) => RefreshRequested?.Invoke();
         foreach (var size in ConfigNormalizer.IconSizes)
         {
@@ -279,6 +282,7 @@ public partial class FenceWindow : Window
         _isLibrary = fence.IsLibrary;
         // The library is NeoFences' own A–Z list of games: no items to add or sort (M12).
         AddItemItem.Visibility = _isLibrary ? Visibility.Collapsed : Visibility.Visible;
+        AddFromDesktopItem.Visibility = _isLibrary ? Visibility.Collapsed : Visibility.Visible;
         SortItem.Visibility = _isLibrary ? Visibility.Collapsed : Visibility.Visible;
         DeleteItem.Header = _isLibrary ? "Delete fence (your games are not touched)" : "Delete fence (your files are not touched)";
         UpdateEmptyHint();
diff --git a/src/NeoFences.App/IconLoader.cs b/src/NeoFences.App/IconLoader.cs
index 0640c4f..c61bd55 100644
--- a/src/NeoFences.App/IconLoader.cs
+++ b/src/NeoFences.App/IconLoader.cs
@@ -47,10 +47,13 @@ public sealed class IconLoader : IDisposable
             try
             {
                 var kind = ItemKinds.Of(request.Target);
+                // A share is asked first, at most 2 s (M19 R7): a dead one would hold this worker in the shell for minutes.
+                var reachable = kind != ItemKind.Path || !TargetChecks.IsNetworkPath(request.Target)
+                                || TargetProbe.Check(request.Target).State == TargetState.Ok;
                 var label = !request.WantsName ? null
                     : kind == ItemKind.Website ? ItemKinds.WebsiteName(request.Target)
-                    : ShellItems.TryGetDisplayName(request.Target);
-                var icon = OwnIcon(request) ?? TargetIcon(request, kind);
+                    : reachable ? ShellItems.TryGetDisplayName(request.Target) : null;
+                var icon = OwnIcon(request) ?? (reachable ? TargetIcon(request, kind) : null) ?? GenericIcon(request, kind);
                 _uiDispatcher.BeginInvoke(() =>
                 {
                     if (request.Number != request.View.IconRequest) return; // a newer request (size, target, icon) is on its way
@@ -94,6 +97,25 @@ public sealed class IconLoader : IDisposable
             ? ShellItems.DefaultBrowserPath() is { } browser ? Frozen(ShellItems.TryGetImage(browser, request.SizePx)) : null
             : Frozen(ShellItems.TryGetImage(request.Target, request.SizePx));
 
+    private static bool _websiteIconFailureLogged;
+
+    /// <summary>
+    /// No icon from the shell (M19 R8): a file or folder (missing, unreachable) or an app (uninstalled) gets Windows' icon for
+    /// its type, by name only; a website's failed browser icon is logged once (the unreproduced blank icon of the M18 check).
+    /// </summary>
+    private static BitmapSource? GenericIcon(LoadRequest request, ItemKind kind)
+    {
+        if (kind == ItemKind.Website)
+        {
+            if (!_websiteIconFailureLogged) Log.Information("no browser icon for website {Target}; it shows without one", request.Target);
+            _websiteIconFailureLogged = true;
+            return null;
+        }
+        if (kind != ItemKind.Path && !ItemKinds.IsApp(request.Target)) return null; // a special item without an icon stays as it is
+        var looksLikeFolder = kind == ItemKind.Path && !Path.HasExtension(request.Target.TrimEnd('\\')); // ponytail: by its name; the check knows better
+        return Frozen(ShellItems.TryGetGenericImage(request.Target, looksLikeFolder));
+    }
+
     private static BitmapSource? Frozen(ShellImage? image)
     {
         if (image is null) return null;
diff --git a/src/NeoFences.App/ItemPropertiesWindow.xaml b/src/NeoFences.App/ItemPropertiesWindow.xaml
index 7c43c2a..f23d094 100644
--- a/src/NeoFences.App/ItemPropertiesWindow.xaml
+++ b/src/NeoFences.App/ItemPropertiesWindow.xaml
@@ -40,8 +40,9 @@
             <Button x:Name="BrowseButton" Grid.Row="1" Grid.Column="2" Content="Browse ▾" Margin="0,0,0,10" AutomationProperties.Name="Browse for the target">
                 <Button.ContextMenu>
                     <ContextMenu>
-                        <MenuItem x:Name="BrowseFileItem" Header="A file or app…" />
+                        <MenuItem x:Name="BrowseFileItem" Header="A file or program…" />
                         <MenuItem x:Name="BrowseFolderItem" Header="A folder…" />
+                        <MenuItem x:Name="BrowseAppItem" Header="An app…" />
                     </ContextMenu>
                 </Button.ContextMenu>
             </Button>
diff --git a/src/NeoFences.App/ItemPropertiesWindow.xaml.cs b/src/NeoFences.App/ItemPropertiesWindow.xaml.cs
index 11f7bf1..1ff98f2 100644
--- a/src/NeoFences.App/ItemPropertiesWindow.xaml.cs
+++ b/src/NeoFences.App/ItemPropertiesWindow.xaml.cs
@@ -54,6 +54,7 @@ public partial class ItemPropertiesWindow : Window
         ChangeIconButton.Click += (_, _) => OpenMenu(ChangeIconButton);
         BrowseFileItem.Click += (_, _) => Browse(folder: false);
         BrowseFolderItem.Click += (_, _) => Browse(folder: true);
+        BrowseAppItem.Click += (_, _) => PickApp();
         IconFromFileItem.Click += (_, _) => PickIconFromFile();
         IconFromPictureItem.Click += (_, _) => PickPicture();
         ResetIconItem.Click += (_, _) => SetIcon(icon: null, picture: null);
@@ -61,6 +62,7 @@ public partial class ItemPropertiesWindow : Window
         {
             _checkDelay.Stop(); // typed: checked once the typing pauses
             _checkDelay.Start();
+            OkButton.IsEnabled = false; // until this target's check says file or folder (M19 R6)
         };
         _checkDelay.Tick += (_, _) =>
         {
@@ -93,11 +95,23 @@ public partial class ItemPropertiesWindow : Window
     private void Browse(bool folder)
     {
         var start = CurrentTarget is { } target && ItemKinds.Of(target) == ItemKind.Path ? Path.GetDirectoryName(target.TrimEnd('\\')) : null;
-        void LogFailure(Exception failure) => Log.Warning(failure, "Browse dialog failed");
-        var picked = folder
-            ? PathPicker.TryPickFolder(Handle, "Choose a folder", LogFailure, start)
-            : PathPicker.TryPickFile(Handle, "Choose a file or app", LogFailure, start);
-        if (picked is not null) TargetBox.Text = picked; // checked by TextChanged
+        WhenReachable(start, reachable =>
+        {
+            void LogFailure(Exception failure) => Log.Warning(failure, "Browse dialog failed");
+            var picked = folder
+                ? PathPicker.TryPickFolder(Handle, "Choose a folder", LogFailure, reachable)
+                : PathPicker.TryPickFile(Handle, "Choose a file or program", LogFailure, reachable);
+            if (picked is not null) TargetBox.Text = picked; // checked by TextChanged
+        });
+    }
+
+    /// <summary>"An app…" (M19 §1): Start's All apps; the name box takes the app's name when it is empty.</summary>
+    private void PickApp()
+    {
+        var picker = new AppPickerWindow(_iconLoader) { Owner = this };
+        if (picker.ShowDialog() != true || picker.Chosen is not { } app) return;
+        TargetBox.Text = ItemKinds.AppTarget(app.AppId);
+        if (string.IsNullOrWhiteSpace(NameBox.Text)) NameBox.Text = app.Name;
     }
 
     /// <summary>Windows' icon picker, opening at the item's icon file, else its target (an .exe or .dll offers its own icons).</summary>
@@ -105,8 +119,31 @@ public partial class ItemPropertiesWindow : Window
     {
         var target = CurrentTarget;
         var start = _icon?.File ?? (target is not null && Path.GetExtension(target).ToLowerInvariant() is ".exe" or ".dll" or ".ico" ? target : null);
-        if (IconPicker.TryPick(Handle, start, _icon?.Index ?? 0) is not { } chosen) return;
-        SetIcon(new ItemIcon { File = chosen.File, Index = chosen.Index }, picture: null);
+        WhenReachable(start, reachable =>
+        {
+            if (IconPicker.TryPick(Handle, reachable, _icon?.Index ?? 0) is not { } chosen) return;
+            SetIcon(new ItemIcon { File = chosen.File, Index = chosen.Index }, picture: null);
+        });
+    }
+
+    /// <summary>
+    /// A dialog's start place is checked off the UI thread first (M19 R2): Windows' pickers parse it on the UI thread, and a
+    /// dead share would freeze the window. Not reachable within 2 s: the dialog opens at Windows' default place.
+    /// </summary>
+    private void WhenReachable(string? path, Action<string?> open)
+    {
+        if (path is null || TargetChecks.RootOf(path) is null)
+        {
+            open(path);
+            return;
+        }
+        IsEnabled = false; // no second click while the check runs (at most 2 s)
+        Task.Run(() => TargetProbe.Check(path).State == TargetState.Ok).ContinueWith(checking =>
+        {
+            IsEnabled = true;
+            if (!checking.Result) Log.Information("{Path} did not answer; the dialog opens at its default place", path);
+            open(checking.Result ? path : null);
+        }, TaskScheduler.FromCurrentSynchronizationContext());
     }
 
     private void PickPicture()
@@ -166,13 +203,15 @@ public partial class ItemPropertiesWindow : Window
             return;
         }
         var kind = ItemKinds.Of(target);
-        if (kind != ItemKind.Path)
+        var isApp = ItemKinds.IsApp(target);
+        if (kind != ItemKind.Path && !isApp)
         {
+            _checkNumber++; // an older path check still running must not overwrite this
             ShowStatus(kind == ItemKind.Website ? "A website: opens in your browser." : "A Windows item.", takesArguments: false);
             return;
         }
         var number = ++_checkNumber;
-        ShowStatus("Checking…", takesArguments: true);
+        ShowStatus("Checking…", takesArguments: !isApp, checking: true);
         Task.Run(() => TargetProbe.Check(target)).ContinueWith(checking =>
         {
             if (number != _checkNumber) return;
@@ -180,19 +219,23 @@ public partial class ItemPropertiesWindow : Window
             _isFolder = check.IsFolder;
             ShowStatus(check.State switch
             {
+                TargetState.Missing when isApp => "● Not installed: Windows no longer has this app.",
                 TargetState.Missing => "● Missing: nothing is there now.",
                 TargetState.Unavailable => TargetChecks.IsNetworkPath(target) ? "● Network location not reachable." : "● Drive not connected.",
+                _ when isApp => "● Found (an app).",
                 _ when TargetChecks.RootOf(target) is null => "Opens through Windows.",
                 _ => check.IsFolder ? "● Found (a folder)." : "● Found.",
             }, takesArguments: ItemKinds.TakesArguments(kind, check.IsFolder));
         }, TaskScheduler.FromCurrentSynchronizationContext());
     }
 
-    private void ShowStatus(string text, bool takesArguments)
+    /// <param name="checking">A check is running: OK waits for it (M19 R6), at most 2 s, so the folder answer it saves is this target's.</param>
+    private void ShowStatus(string text, bool takesArguments, bool checking = false)
     {
         TargetStatus.Text = text;
         ArgumentsBox.IsEnabled = takesArguments;
         AdminBox.IsEnabled = takesArguments;
+        OkButton.IsEnabled = !checking;
     }
 
     private void Accept()
diff --git a/src/NeoFences.App/MissingItemWindow.xaml.cs b/src/NeoFences.App/MissingItemWindow.xaml.cs
index 623e171..1337afa 100644
--- a/src/NeoFences.App/MissingItemWindow.xaml.cs
+++ b/src/NeoFences.App/MissingItemWindow.xaml.cs
@@ -18,7 +18,9 @@ public partial class MissingItemWindow : Window
             ? TargetChecks.IsNetworkPath(target)
                 ? "Its network location does not answer. It comes back by itself when it does."
                 : $"It is on drive {TargetChecks.RootOf(target)?.TrimEnd('\\')}, which is not connected. Plug it in: the item comes back by itself."
-            : "Nothing is at its place any more: it was deleted, moved or renamed. Point the item at its new place, or remove the item.";
+            : ItemKinds.IsApp(target)
+                ? "Windows no longer has this app: it was uninstalled. Point the item at another app, or remove the item."
+                : "Nothing is at its place any more: it was deleted, moved or renamed. Point the item at its new place, or remove the item.";
         TargetText.Text = target;
         LocateButton.Click += (_, _) => Choose(MissingItemChoice.Locate);
         RemoveButton.Click += (_, _) => Choose(MissingItemChoice.Remove);
diff --git a/src/NeoFences.App/RelocateWindow.xaml b/src/NeoFences.App/RelocateWindow.xaml
new file mode 100644
index 0000000..bcd959f
--- /dev/null
+++ b/src/NeoFences.App/RelocateWindow.xaml
@@ -0,0 +1,18 @@
+<Window x:Class="NeoFences.App.RelocateWindow"
+        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
+        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
+        Title="NeoFences" Width="460" SizeToContent="Height" ResizeMode="NoResize"
+        WindowStartupLocation="CenterScreen" ShowInTaskbar="True" ThemeMode="System">
+    <!-- M19 spec 2026-10-05 §2: after one Locate…, the other items from the same old place are offered at once. -->
+    <StackPanel Margin="24,18,24,20">
+        <TextBlock x:Name="Headline" FontSize="16" FontWeight="SemiBold" TextWrapping="Wrap" Margin="0,0,0,8" />
+        <TextBlock x:Name="Explanation" TextWrapping="Wrap" Margin="0,0,0,6" />
+        <TextBlock x:Name="ItemNames" TextWrapping="Wrap" FontSize="12" Foreground="{DynamicResource TextFillColorSecondaryBrush}" Margin="0,0,0,8" />
+        <TextBlock TextWrapping="Wrap" FontSize="12" Foreground="{DynamicResource TextFillColorSecondaryBrush}" Margin="0,0,0,18"
+                   Text="Only these links change; no file is touched. Tray → Restore snapshot → Undo the last restore or fix puts them back." />
+        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
+            <Button x:Name="FixButton" Content="Fix" IsDefault="True" MinWidth="90" Margin="0,0,8,0" />
+            <Button Content="Not now" IsCancel="True" MinWidth="90" />
+        </StackPanel>
+    </StackPanel>
+</Window>
diff --git a/src/NeoFences.App/RelocateWindow.xaml.cs b/src/NeoFences.App/RelocateWindow.xaml.cs
new file mode 100644
index 0000000..cab02f1
--- /dev/null
+++ b/src/NeoFences.App/RelocateWindow.xaml.cs
@@ -0,0 +1,19 @@
+using System.Windows;
+using NeoFences.Core.Items;
+
+namespace NeoFences.App;
+
+/// <summary>The bulk fix's question (M19 spec §2): "Fix N more items?" with up to five names. Fix = DialogResult true.</summary>
+public partial class RelocateWindow : Window
+{
+    private const int NamesShown = 5;
+
+    public RelocateWindow(int count, Relocation.Bases bases, IReadOnlyList<string> names)
+    {
+        InitializeComponent();
+        Headline.Text = count == 1 ? "Fix 1 more item?" : $"Fix {count} more items?";
+        Explanation.Text = $"{(count == 1 ? "It was" : "They were")} in {bases.OldBase} and {(count == 1 ? "is" : "are")} now in {bases.NewBase}.";
+        ItemNames.Text = string.Join(", ", names.Take(NamesShown)) + (names.Count > NamesShown ? $" … and {names.Count - NamesShown} more" : "");
+        FixButton.Click += (_, _) => DialogResult = true;
+    }
+}
````

- [ ] **Step 2: Build and test.**
  Run: `dotnet build` then `dotnet test`
  Expected: `0 Warning(s)`, `0 Error(s)`; `Passed: 450`.

- [ ] **Step 3: Commit.**
  `git add src/NeoFences.App` then
  `git commit -m "feat: added Store apps, the bulk fix of missing items, Add from desktop and the M18 reliability fixes to the App"`

### Task 4: Docs

**Files:** `docs/ARCHITECTURE.md`, `docs/FEATURES.md`, `docs/TEST-CHECKLIST.md` (section AE),
`docs/research/m19-apps-relocate-desktop-fill.md` (new: prototype probes and findings)

- [ ] **Step 1: Apply.** Write this patch to `m19-4-docs.patch` and apply it:

````diff
diff --git a/docs/ARCHITECTURE.md b/docs/ARCHITECTURE.md
index 9870810..bea2923 100644
--- a/docs/ARCHITECTURE.md
+++ b/docs/ARCHITECTURE.md
@@ -36,7 +36,9 @@ happen) is behind Shift+right-click, under a line saying it acts on the real fil
 ```
  drop (Explorer, desktop, browser) ─► ShellDragDrop.FenceDropTarget ─► FenceHost.OnTargetsDropped ─► ItemEdits.Add
  drag between fences ──────────────► (CurrentDrag keys) ────────────► FenceHost.OnItemsDropped ──► ItemEdits.Move / Duplicate
- Properties / Add item… / Locate… ─► ItemPropertiesWindow, PathPicker ► ItemEdits.Replace / Add
+ Properties / Add item… / Locate… ─► ItemPropertiesWindow, PathPicker, AppPickerWindow ► ItemEdits.Replace / Add
+ Locate… done ─► Relocation.Find / Candidates ─► TargetProbe (new places exist?) ─► RelocateWindow ─► snapshot ─► ItemEdits.Relocate
+ Add from desktop… ─► DesktopItems + ShellLinks + GameScanners ─► DesktopSorting.GroupOf ─► DesktopFillWindow ─► FenceEdits.CreateFence + ItemEdits.Add
  _items (ItemsDocument) ─► FenceHost.RefreshWindow ─► ShownItem[] ─► FenceWindow.SetItems ─► IconLoader (own name/icon win)
  FolderWatcher (≤ 64 parent folders, WatchPlan) ─► rename ─► ItemEdits.Retarget (all fences)
                                                 └► change ─► TargetProbe.Check (off the UI thread) ─► TargetState ─► fence refresh (≤ 1 / 2 s)
@@ -47,24 +49,28 @@ happen) is behind Shift+right-click, under a line saying it acts on the real fil
 
 ```
 ┌─────────────────────────── NeoFences.App (WPF) ───────────────────────────┐
-│ FenceHost (+ .Items .Watching .Library .Appearance .Updates)              │
+│ FenceHost (+ .Items .Watching .Library .Appearance .Updates .DesktopFill) │
 │ FenceWindow · FenceItemView · IconLoader · ItemPropertiesWindow           │
-│ MissingItemWindow · SettingsWindow · LibraryLister · DrawFenceOverlay     │
+│ AppPickerWindow · RelocateWindow · DesktopFillWindow · MissingItemWindow  │
+│ SettingsWindow · LibraryLister · DrawFenceOverlay                         │
 └──────────────┬────────────────────────────────────────────────────────────┘
 ┌──────────────▼─────────── NeoFences.Shell (Win32/COM, CsWin32) ───────────┐
 │ DesktopHost · DesktopIcons · ShellItems · ShellItemMenu · ShellDragDrop   │
 │ TargetProbe · PathPicker · IconPicker · FolderWatcher · DesktopMouseHook  │
+│ AppList                                                                   │
 │ GameDetection · GameScanners · Monitors · TrayIcon · Watchdog             │
 └──────────────┬────────────────────────────────────────────────────────────┘
 ┌──────────────▼─────────── NeoFences.Core (pure C#) ───────────────────────┐
-│ Items (VirtualItem, ItemEdits, TargetChecks, WatchPlan, RefreshThrottle)  │
+│ Items (VirtualItem, ItemEdits, TargetChecks, WatchPlan, RefreshThrottle,  │
+│        Relocation, DesktopSorting, StaleEntries)                          │
 │ Model · Config (ConfigStore, ItemStore, JsonStore) · Layouts · Library    │
 └───────────────────────────────────────────────────────────────────────────┘
 ```
 
 | Component | Responsibility | Key APIs |
 |---|---|---|
-| Core/Items | `VirtualItem` + `ItemIcon`; `ItemKinds` (path / website / special, typed targets cleaned); `ItemsDocument` (fence id → items); `ItemEdits` (add with same-fence de-dup, move, Ctrl-duplicate, remove, replace, retarget on rename, prune, reorder, repair); `TargetChecks` (OK / Missing / Unavailable from the disk's answers); `WatchPlan` (≤ 64 parent folders, busiest first); `RefreshThrottle` (≤ 1 refresh per fence per 2 s) | — |
+| Core/Items | `VirtualItem` + `ItemIcon`; `ItemKinds` (path / website / special, typed targets cleaned); `ItemsDocument` (fence id → items); `ItemEdits` (add with same-fence de-dup, move, Ctrl-duplicate, remove, replace, retarget on rename, prune, reorder, repair); `TargetChecks` (OK / Missing / Unavailable from the disk's answers); `WatchPlan` (≤ 64 parent folders, busiest first); `RefreshThrottle` (≤ 1 refresh per fence per 2 s); apps `shell:AppsFolder\<id>` (`IsApp`, checked, never watched; M19) | — |
+| Core/Items (M19) | `Relocation` (Find: the differing front of the old and new path; Candidates: the other missing / unavailable path items under it); `ItemEdits.Relocate`, `CheckedTargets`; `DesktopSorting.GroupOf` (Games / Apps / Folders and files / Web links, reusing `GameLaunchers`); `StaleEntries.Gone` | — |
 | Core/Model | Fence (`IsLibrary`, tabs, colours, roll-up, lock, labels, icon size), Layout, Settings (`HideDesktopIcons`), snapshots with items | — |
 | Core/Config | `JsonStore<T>`: atomic write (`SafeFile`), `.bak`, daily backups (10), corrupt recovery, newer-schema read-only; `ConfigStore` (config.json, schema 5; older schemas start fresh and are kept as `backups\pre-schema-5-config.json`), `ItemStore` (items.json, schema 1), `SnapshotStore` | System.Text.Json, File.Replace |
 | Core/LayoutEngine | display fingerprint, map/scale layouts between monitor setups, clamp, snap, smart placement of new fences (`FreeSpot`, ADR-020) | — |
@@ -80,9 +86,10 @@ happen) is behind Shift+right-click, under a line saying it acts on the real fil
 | App/SettingsWindow | General (Start with Windows, Hide desktop icons while NeoFences runs, Peek hotkey) · Fences · Appearance · Snapshots · Game Library · Game mode · Updates · About | WPF ThemeMode |
 | App/InstallHooks, Shell/StartupRegistration, Core/StartupPolicy, build/pack.ps1 | Velopack installer and auto-update (ADR-023, ADR-039) | Velopack 1.2.161 |
 | Shell/ShellDragDrop | drag out: a shell item array of the targets (absolute ID lists) turned into a data object, or URLs, copy/link only; one IDropTarget per fence: fence items (keys) → move / Ctrl-duplicate, outside items read as shell items (real paths, special items as `::{GUID}`, zip contents skipped; CF_HDROP as fallback) or one website (UniformResourceLocatorW / text) → new items; never a move effect | SHDoDragDrop, SHCreateShellItemArrayFromIDLists + BHID_DataObject, SHCreateShellItemArrayFromDataObject, SHParseDisplayName, RegisterDragDrop, IDropTargetHelper |
-| Shell/ShellItems | display name, icon/thumbnail pixels, an icon from a file (index), the default browser's path, open (arguments, `runas`, the target's folder as working folder), show in folder | SHCreateItemFromParsingName, IShellItemImageFactory, SHDefExtractIcon, AssocQueryString, ShellExecute |
+| Shell/ShellItems | display name, icon/thumbnail pixels, an icon from a file (index), the default browser's path, open (arguments, `runas`, the target's folder as working folder), show in folder; a generic icon by file type for targets the shell cannot reach (M19) | SHCreateItemFromParsingName, IShellItemImageFactory, SHDefExtractIcon, SHGetFileInfo, AssocQueryString, ShellExecute |
+| Shell/AppList (M19) | Start's All apps (`FOLDERID_AppsFolder`, ~0.3 s for ~200 apps), whether an app still exists, an app dragged from Start | SHGetKnownFolderItem, BHID_EnumItems, IEnumShellItems |
 | Shell/ShellItemMenu, DesktopNamespace | Windows' classic item menu for one target (Shift+right-click, with a disabled header line) and for the library's shortcuts (custom commands, Delete handed back) | IContextMenu3, TrackPopupMenuEx, InsertMenu |
-| Shell/TargetProbe | a target's state off the UI thread; shares time out after 2 s (Unavailable) | File/Directory.Exists |
+| Shell/TargetProbe | a target's state off the UI thread; shares time out after 2 s (Unavailable); apps are Missing once Windows no longer knows them (M19). Also asked before UI-thread shell calls on a target (Windows' menu, picker start folders, drag-out, icon loading of shares; M19 R2/R7) | File/Directory.Exists |
 | Shell/PathPicker, IconPicker | Windows' Open dialog (file with filters / folder, start folder); Windows' Change Icon dialog | IFileOpenDialog, PickIconDlg |
 | Shell/FolderItems (+FolderWatcher) | folder listing (library), sort facts, watcher with `Renamed(old, new)` and the folder's own rename/removal via its parent | FileSystemWatcher |
 | Shell/DesktopIcons, Watchdog | hide/show native icons (option); `--watchdog <pid>`: restore icons whenever `icons-hidden` exists, restart after a crash (3 per 10 min) or a cancelled session end (ADR-005, ADR-013) | IShellWindows, IFolderView2 |
@@ -130,6 +137,12 @@ Shift+right-click; targets watched (≤ 64 folders, rename-follow, Missing / Una
 Refresh); "Hide desktop icons while NeoFences runs" (off by default); Takeover, Inbox, desktop membership, Rules,
 Portals and item file actions removed (ADR-040, ADR-041, `research/m18-virtual-items.md`).
 
+**0.10.0 (M19)**: Store and Start apps as items (the "An app…" list, drags from Start; Missing when uninstalled); after
+Locate…, "Fix N more items?" with an undo snapshot; "Add from desktop…" (fence menu, tray) grouping the desktop into
+fences; the M18 reliability leftovers: removal notices released while watchers arm, shell calls on unreachable targets
+checked first (2 s), COM objects released, failed watcher batches retried, stale per-target records dropped, OK waits
+for the target check, generic icons for unreachable targets (ADR-042, `research/m19-apps-relocate-desktop-fill.md`).
+
 Pre-pivot history (v1.x dev builds, the M0–M17 notes in `research/` and SESSION-LOG) describes the Takeover model; read
 it as history. Kept from it: fences, tabs (M9), snapshots (M10), the Game Library (M12), roll-up, lock, Peek, game mode,
 Appearance (M14/M16), the installer and auto-update (M7/M17).
diff --git a/docs/FEATURES.md b/docs/FEATURES.md
index f2d8e73..6d82457 100644
--- a/docs/FEATURES.md
+++ b/docs/FEATURES.md
@@ -14,7 +14,9 @@ Sources for Fences 6: stardock.com news posts "Now Announcing: Fences 6", "Fence
 |---|---|---|---|---|---|
 | Fences holding desktop icons (Takeover) | 1+ | — | M2 | parked | ADR-040: fences hold virtual items instead; the desktop stays as Windows shows it |
 | Virtual items: own name, icon, path, arguments, run as admin, note | — | 0.9 | M18 | done | ADR-040/041; Properties dialog; the same target in several fences |
-| Add item… (file, folder, app, website) | — | 0.9 | M18 | done | fence menu; Browse or a typed path / web address |
+| Add item… (file, folder, app, website) | — | 0.9 | M18 | done | fence menu; Browse or a typed path / web address; "An app…" lists Start's apps (0.10) |
+| Store / Start apps as items | — | 0.10 | M19 | done | `shell:AppsFolder\<id>`: from the app list or dragged from Start; Missing when uninstalled (ADR-042) |
+| Add from desktop… | — | 0.10 | M19 | done | fence menu or tray: desktop entries grouped (Games / Apps / Folders and files / Web links), each group into a fence; items point at the desktop entries (ADR-042) |
 | Inbox / default fence for new items | — | — | M2 | parked | no auto-fill (ADR-040) |
 | Move / resize fences | 1+ | v1 | M2 | done | snap 8 px gap / edge alignment (M2c) |
 | Scrolling inside fences | 2+ | v1 | M2 | done | thin scrollbar (M2c) |
@@ -26,7 +28,8 @@ Sources for Fences 6: stardock.com news posts "Now Announcing: Fences 6", "Fence
 | Item menu | 1+ | 0.9 | M18 | done | NeoFences' own safe menu; Windows' full menu on Shift+right-click, labelled "acts on the real file" |
 | Rename / delete items | 1+ | 0.9 | M18 | done | rename = the item's own name; Del = remove the item (no file operation, ADR-040) |
 | Drag-drop in/out/between fences | 1+ | 0.9 | M18 | done | drops create items (link, never move); Ctrl+drag duplicates; dragging out copies |
-| Missing / unavailable targets | — | 0.9 | M18 | done | watched (≤ 64 folders), renames followed, Locate… / Remove, per-fence Refresh |
+| Missing / unavailable targets | — | 0.9 | M18 | done | watched (≤ 64 folders), renames followed, Locate… / Remove, per-fence Refresh; a generic icon for their type (0.10) |
+| Bulk fix of missing items | — | 0.10 | M19 | done | after one Locate…, "Fix N more items?" for the others from the same old place; undo from the tray (ADR-042) |
 | Rubber-band selection | 1+ | v1 | M3 | done | M3b; Ctrl adds |
 | Folder Portals | 3+ | — | M4 | parked | ADR-040: later as dynamic collections (M20) |
 | Sort (name/type/date) | 2+ | v1 | M4 | done | one time; by the names shown (0.9) |
@@ -62,7 +65,6 @@ Sources for Fences 6: stardock.com news posts "Now Announcing: Fences 6", "Fence
 | Blur tint preference (lighter/darker) | v1.7 (M14) | done | background strength slider, one value per Windows tone (ADR-036) |
 | Search palette across all fences | — | parked | built on branch `m15-search-palette` (local history bundle only), not merged |
 | Game Library fence (Steam/Epic/GOG/Ubisoft Connect/EA, cover art) | v1.5 | done | launchers, Xbox, game folders, Desktop game shortcuts; a game dragged into a fence becomes an item (0.9) (ADR-032) |
-| Store/UWP apps as items | M19+ | — | not files: need shell item lists and AppsFolder launch |
 | Dynamic collections (read-only folder views, auto-collect rules) | M20 | — | replaces Portals and Rules (ADR-040) |
 | Theme packs: "Nanosuit" (Crysis HUD), "Animus" (Assassin's Creed) | v2 | — | |
 | Custom Win11-style compact context menu | v2 | — | |
diff --git a/docs/TEST-CHECKLIST.md b/docs/TEST-CHECKLIST.md
index 5ca5620..afb585f 100644
--- a/docs/TEST-CHECKLIST.md
+++ b/docs/TEST-CHECKLIST.md
@@ -496,3 +496,33 @@ Added after the M18 final review:
 | AD30 | Sort by → Name on a fence holding the `\neofences-nohost\share\x.txt` item | the fences stay responsive; the sort finishes a few seconds later |
 | AD31 | Drag a fence item onto the desktop's Recycle Bin | nothing is deleted (drag-out offers copy or link only; the Recycle Bin takes moves) |
 | AD32 | Give an item a picture icon, take a snapshot, Remove the item, restart NeoFences, restore the snapshot | the item comes back with its picture |
+
+## AE — Store apps, bulk fix, Add from desktop, reliability (M19, ADR-042)
+
+Run with the test build and NeoFences' own data backed up (the installed copy stopped; its data restored afterwards).
+Test files only in `%USERPROFILE%\NeoFences-m19-test\` and the pendrive's `G:\NeoFences-test\`.
+
+| ID | Steps | Expected |
+|---|---|---|
+| AE1 | Add item… → Browse ▾ → An app… | the list opens at once ("Loading apps…"), then every Start app A–Z with icons (~0.3 s); typing filters it |
+| AE2 | Pick Sticky Notes (OK or double-click) | target `shell:AppsFolder\Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe!App`, the empty name box takes "Sticky Notes"; Arguments and Run as administrator greyed; status "● Found (an app)." |
+| AE3 | Add it; double-click the item | Sticky Notes opens; the item shows the app's icon and name |
+| AE4 | Drag an app from Start → All onto a fence (by hand) | a `shell:AppsFolder\…` item at the drop point; no "drop failed" warning in the log |
+| AE5 | Add the typed target `shell:AppsFolder\NotAnApp.Bogus_123!App` | status "● Not installed: …"; the item is Missing (dimmed, badge, a generic icon, the label "NotAnApp.Bogus") |
+| AE6 | Open that item → Locate… | the app list opens (not a file dialog); picking an app re-points the item |
+| AE7 | Items for `…\NeoFences-m19-test\old\a.txt`, `b.txt`, `c.txt`; exit NeoFences; rename `old` to `new`; start; open a → Locate… → `new\a.txt` | "Fix 2 more items? They were in …\old and are now in …\new." (b, c); Fix: b and c point into `new`, a log line "2 item(s) fixed", `snapshots\before-restore.json` written |
+| AE8 | Tray → Restore snapshot → "Undo the last restore or fix" | b and c point into `old` again (a stays: the undo reverts the fix, not the Locate) |
+| AE9 | AE7 with `d.txt` deleted from `new` before the Locate | d is not offered; only the items whose files are in `new` |
+| AE10 | Locate… a missing item to a file with another name | no "Fix N more" question |
+| AE11 | Tray → Add from desktop… | the dialog opens at once ("Reading your desktop…"), then groups Games / Apps / Folders and files / Web links with counts, every row ticked, "Put in: New fence: …" (or a fence of that title) |
+| AE12 | Add | new fences in free space, every ticked entry an item pointing at the desktop entry itself; nothing on the desktop changed; one log line with the counts |
+| AE13 | Open Add from desktop… again | every row unticked with "already in <fence>"; Add greyed |
+| AE14 | Settings → Game Library → add `D:\GameLibrary`; Add from desktop… | shortcuts into `D:\GameLibrary` are listed under Games |
+| AE15 | Tick "Hide desktop icons while NeoFences runs" and Add | the icons hide (HideIcons 1, marker); the Settings switch shows on |
+| AE16 | Shift+right-click the `\\neofences-nohost\share\x.txt` item | after ≤ 2 s a one-line menu "Network location not reachable"; the fence never freezes |
+| AE17 | Properties of that item → Browse ▾ → A file or program…; → Change icon → From a file… | each dialog opens within ~2 s at Windows' default place; log "did not answer" |
+| AE18 | Drag that item (with a reachable file item) out to Explorer | only the reachable file is copied; log "left out of the drag" |
+| AE19 | Properties: type a new path and press Enter at once | OK stays greyed until the check finishes ("Checking…"), then the saved Arguments state matches the new target |
+| AE20 | Restart NeoFences with the share item in a fence | the share item shows a generic icon within ~2 s; the other items' icons load normally (no stuck loader) |
+| AE21 | Pendrive: add an item on G:, then pull the stick right after NeoFences starts (watchers still arming) | no veto, no error; re-plug: the rename of `g.txt` is followed |
+| AE22 | Remove many items and a fence, then keep using NeoFences for a while | no growth of per-target records (log has no errors; behaviour unchanged) — covered by Core tests for the clean-up |
diff --git a/docs/research/m19-apps-relocate-desktop-fill.md b/docs/research/m19-apps-relocate-desktop-fill.md
new file mode 100644
index 0000000..7a446f6
--- /dev/null
+++ b/docs/research/m19-apps-relocate-desktop-fill.md
@@ -0,0 +1,29 @@
+# M19 — Store apps, bulk fix, Add from desktop, reliability (0.10.0): prototype and live results
+
+**Spec:** `docs/superpowers/specs/2026-10-05-m19-apps-relocate-desktop-fill-design.md` · **Decisions:** ADR-042, ADR-043
+**Plan:** `docs/superpowers/plans/2026-10-05-m19-apps-relocate-desktop-fill.md`
+
+## Prototype probes (2026-10-05, this PC, Windows 11 26200)
+
+| Probe | Result |
+|---|---|
+| AppsFolder listing | 182 apps in 0.34 s (Store apps and programs; ids like `Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe!App`, `308046B0AF4A39CB` for Firefox) |
+| `shell:AppsFolder\<id>` | `SHCreateItemFromParsingName` parses it with the app's name and icon; a made-up id fails with 0x80070002 (the Missing check) |
+| An app's desktop-absolute parsing name | just the id (not `::{4234…}\id`): drops recognise apps by their **parent** (the AppsFolder), not by the name |
+| Dragging from Start → All | works: Snipping Tool became `shell:AppsFolder\Microsoft.ScreenSketch_8wekyb3d8bbwe!App`. Windows' drag-image helper refuses Start's data object (DV_E_CLIPFORMAT): now expected and not logged as a failed drop |
+| Generic icons (`SHGetFileInfo`, by name) | a missing `.txt` shows the text-file icon, a dead share item a generic one, an uninstalled app a generic file icon; all with the Missing badge or dimming |
+| Bulk fix | three items in `…\old`, folder renamed while NeoFences was off, Locate… of one → "Fix 2 more items? They were in …\old and are now in …\new." → Fix re-points both and writes `before-restore.json`; tray "Undo the last restore or fix" puts the two back (the located one stays) |
+| Add from desktop… | 31 desktop entries in 4 s into 3 new fences; a second run shows every row "already in …" and Add greyed |
+| Games group | with no game folder configured (the fresh 0.9.0 data), only launcher links counted: 4 of the user's 10 games. The Game Library's scan (install folders, its Desktop-shortcut games) now counts too (5); the 6 games in `D:\GameLibrary` need that folder in Settings → Game Library, which the dialog says |
+
+## Findings fixed in the prototype
+
+- An uninstalled app had an empty label (its placeholder name): now the front of its id ("NotAnApp.Bogus").
+- The app list's rows told screen readers their type name: now the app's name.
+- Games sorted as Apps without game folders: the Game Library's scan is asked (read-only) and a hint names the setting.
+- "drop on fence failed" logged for every Start drag (the drag-image helper's refusal): quiet, and the helper is not
+  asked again during that drag.
+
+## Live check results
+
+(Task 6 of the plan fills this in.)
````

- [ ] **Step 2: Commit.**
  `git add docs` then
  `git commit -m "docs: described 0.10.0 in architecture, features, checklist AE and the M19 research note"`

### Task 5: Final review and fix pass

- [ ] **Step 1:** `review-package` for `main..HEAD` (merge base `563694c`); dispatch the whole-branch reviewer on the
  most capable model with the spec, this plan, its Review Focus and the ledger's rulings.
- [ ] **Step 2:** Re-grade; Critical and Important in ONE fix pass, each RED→GREEN with a Core test where the logic is
  Core, else a TEST-CHECKLIST row; full `dotnet test` green; minors to the ledger and the research note.

### Task 6: Live check (ask the user first unless the PC is unattended)

- [ ] **Step 1: Safe setup.** The installed 0.9.0 runs with the user's data: stop it (`--exit`), copy
  `%LOCALAPPDATA%\NeoFences` to the scratchpad and save the `Run\NeoFences` value; start the test build
  (`src\NeoFences.App\bin\Debug\net10.0-windows\NeoFences.exe`). Test files only in `%USERPROFILE%\NeoFences-m19-test\`
  and `G:\NeoFences-test\`. Print the boxed TEST RUNNING banner; never type into Windows Terminal; leave a Firefox
  Picture-in-Picture window alone; bring every dialog to the front and uncover it before clicking (dialogs can open
  behind other windows); click Win32 dialogs' child buttons (UIA shows no controls in #32770 dialogs here).
- [ ] **Step 2: Rows.** TEST-CHECKLIST **AE1–AE22**, plus AD2, AD8, AD15–AD18 and AD26 (touched code). AE4 (drag from
  Start) and AE21 (pull the stick right after start) are the user's physical steps; AE14 changes the test build's
  Game Library folders only.
- [ ] **Step 3: Restore.** Stop the test build; put the backed-up data folder back exactly and the `Run` value; start
  the installed copy; delete `%USERPROFILE%\NeoFences-m19-test\` (named test files only, guarded); restore minimized
  windows; refocus Windows Terminal; print TEST COMPLETE.
- [ ] **Step 4: Record.** Results table in `docs/research/m19-apps-relocate-desktop-fill.md` ("Live check results");
  commit `docs: added the M19 live check results`.

After Task 6: superpowers:finishing-a-development-branch (ask before merging), then the 0.10.0 release exactly as 0.9.0
(bump `<Version>` to `0.10.0`, push main → CI → tag `v0.10.0` → draft → install check as an **update from the installed
0.9.0** → publish; every outward step asked), session end (SESSION-LOG, ROADMAP M19 ticks, hub refresh).
