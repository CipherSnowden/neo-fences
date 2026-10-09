# M39 — Release readiness (0.26.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A friend or a stranger can find NeoFences, see what it is, install it past SmartScreen with clear steps, know what it sends and what they may do with it, check that the download is genuine and report a bug — and Core's tests pass on Linux too.

**Architecture:** Core gains `WindowsPath` (pure text rules for the Windows paths Core stores) and uses it wherever it reads stored paths; NeoFences' own files keep `System.IO.Path`. The repo gains the trust files (licence, privacy, security, third-party notices), issue forms, a landing page in `site/` published by a Pages workflow, and `docs/RELEASING.md`; the App ships the licence, notices and privacy note next to `NeoFences.exe` and opens the licence from Settings → About. CI runs Core's tests on Ubuntu too; the Release workflow adds `SHA256SUMS.txt` and GitHub build attestations.

**Tech Stack:** .NET 10, C#, WPF, xUnit; GitHub Actions (`actions/attest-build-provenance@v2`, Pages actions); plain HTML/CSS; Windows PowerShell 5.1 in the VM guest.

**Spec:** `docs/superpowers/specs/2026-10-10-release-readiness-design.md` (approved 2026-10-10). Decision: ADR-061 — added by Task 5.

## How this plan is written

Built in a scratch worktree (`neo_fences-m39-proto`, branch `m39-proto` from `9e2ce1a`). Core's tests were run on Linux
in the owner's WSL Ubuntu 26.04 (the .NET 10 SDK installed there, `libicu78` added; owner's OK): **45 of 837 failed**.
Three causes: `System.IO.Path` follows the OS, so on Linux a backslash is not a separator and Core read stored Windows
paths wrongly (folder views, sorting, panels, setup pictures, game art, the icon cache, the startup entry, collect
rules) — including a name like `..\..\Windows\evil.png` that Core cuts to `evil.png` on Windows but not on Linux; tests
that build Windows-only inputs (the `India Standard Time` zone, `data.File("icons\\pic.png")`, `%USERPROFILE%`, test
helpers calling `Path.GetFileName`); and two tests of Windows-only behaviour (folder ACLs, mandatory file locks). After
the prototype: **878 of 878 pass on Linux and on Windows**. The landing page was looked at in headless Edge at 1280 px
and in a 390 px frame (one column, no sideways scrolling). VM runs of the prototype (packed as 0.26.0-proto.1): the release-candidate pass in `NF-Win11` from 0.25.0 —
**13 of 13** (`trust-files` included); the live checks in `NF-Win11-Dev` on a copy of the owner's data — **8 of 8**.

Replay-verified on a fresh worktree of `main` at `9e2ce1a`: `dotnet test` → 837 passed; patch 1a alone fails to build
(`error CS0103: The name 'WindowsPath' does not exist in the current context` — the RED); 1b builds with 0 warnings, 878
pass; patch 2 builds with 0 warnings, puts the three files next to the exe, 878 pass; patches 3 and 4 apply; `src/`,
`tests/`, `tools/`, `site/`, `.github/` and every changed doc match the prototype. Tasks 1–4 are **patches to apply**
(`git apply --whitespace=nowarn <file>`; if one does not apply, stop).

**Calls made while prototyping (ledger them as rulings at the task named):**
- Task 1: Core reads stored paths by Windows' rules on every OS (`WindowsPath`) — the app is Windows-only and its data
  holds Windows paths; a later port revisits. NeoFences' own folder (JSON stores, snapshots, caches) keeps
  `System.IO.Path`, since that is real file I/O on the running OS. `WindowsPath.DirectoryName` returns null for a bare
  name (`System.IO.Path` returns ""): no caller depends on the difference. Tests of Windows-only behaviour (ACLs, locked
  files, `%USERPROFILE%`) return early off Windows with a comment (no new NuGet package for skippable tests); the clock
  test treats Linux ICU's narrow no-break space as a space.
- Task 2: "Licence and notices" opens `LICENSE.txt` next to `NeoFences.exe` (the spec said "the folder"; the install
  folder holds ~400 files, so the licence opens and names the notices and the privacy note beside it). The licence text
  is exactly the approved wording, plus one line under it with the release page's address. The issue forms use GitHub's
  default labels (`bug`, `enhancement`). The third-party notices include the full Apache 2.0 text and the .NET runtime's
  and WPF's own third-party notices (release/10.0), since the self-contained app ships both. A `trust-files` VM check
  joins the release-candidate pass (13 checks).
- Task 4: the site's pictures are the guide's (`docs/guide/`), copied at publish time, so a screenshot lives in one
  place; no version number on the page (it would go stale; the button always fetches the latest release).

## Global Constraints

- Windows 11 x64 only (ADR-059); hard rules stand — no new NuGet dependency, Win32 only in Shell, Core test-first, user
  files never touched.
- The public repo never holds the owner's e-mail address or a claude.ai artifact link: secret scan
  (`gmail|claude.ai/artifact`) before every commit and push. Expected hit: `THIRD-PARTY-NOTICES.txt` carries other
  authors' addresses from .NET's own notices (e.g. Mono.Cecil's) — those must stay; only the owner's address is a leak.
- The licence text is the owner-approved wording, unchanged.
- PRIVACY states exactly what goes over the network: the update check and downloads from GitHub; only with "Find covers
  and website icons online" (off by default): game names to Steam's store search, covers from Steam's CDN, a website
  item's icon from that site.
- Every step that leaves this PC — push, tag, publish, turning on Pages or private vulnerability reporting, submissions —
  is asked of the owner first. Live checks run in the VMs, not on the owner's desktop; screenshots of the owner's setup
  are the one exception, asked first.
- A change to a menu entry or setting updates `docs/GUIDE.md` in the same commit (ADR-050).
- Commits: single line, Conventional Commits, past tense, **no Co-Authored-By trailer**.
- Version stays `0.25.0` until the release task; the release is 0.26.0.

## Review Focus

1. **A stored path that is odd but real**: a share root (`\\nas\share`), a drive root (`D:\`), a path with forward
   slashes, a folder name with dots, a trailing separator, mixed case — `WindowsPath` must give the same answers as
   `System.IO.Path` did on Windows, or the app changes behaviour (folder panels' Up, headers, sorting, the startup entry).
2. **A name that tries to leave its folder**: `..\x`, `../x`, `a/b`, `C:x`, reserved characters in setup pictures, cover
   choices, the icon cache index and Wallpaper Engine scene names — always cut to a plain name or refused, on any OS.
3. **The release workflow on its first real run**: no delta on a first release, the previous release's files in the same
   folder, a checksum file uploaded to a draft, attestation permissions — the release must still be drafted with every
   asset (Task 8 proves it on 0.26.0).
4. **The landing page and docs on a phone and with images missing**: the page still reads at 360–400 px; a missing
   picture leaves a sensible alt text; every link (latest download, guide, issues, licence, privacy, security, Microsoft's
   FAQ) resolves.
5. **The trust files where people look for them**: the installed folder holds `LICENSE.txt`, `THIRD-PARTY-NOTICES.txt`,
   `PRIVACY.md` after install and after an update; Settings → About → Licence and notices opens the licence even when
   Notepad is not the default for .txt (Windows' "open with" applies).

---

### Task 0: Worktree and baseline

- [ ] `git worktree add -b m39-release-readiness ..\neo_fences-m39 main` (main at `9e2ce1a` or later docs-only commits).
- [ ] `dotnet build` → 0 warnings; `dotnet test` → 837 passed.
- [ ] In WSL (owner's Ubuntu, .NET 10 at `~/.dotnet`): `git clone -b m39-release-readiness /mnt/f/projects/neo_fences ~/nf-m39-exec`,
  `~/.dotnet/dotnet test tests/NeoFences.Core.Tests -c Release` → `Failed: 45, Passed: 792` (the Linux baseline).

### Task 1: Core — Windows paths by Windows' rules on every OS

**Files:**
- Create: `tests/NeoFences.Core.Tests/WindowsPathTests.cs`, `src/NeoFences.Core/WindowsPath.cs`
- Modify (tests): `Model/UxCarryOverTests.cs`, `Model/SnapshotsTests.cs`, `Lifecycle/SafetyNetTests.cs`,
  `Items/WidgetsTests.cs`, `Items/FolderViewsTests.cs`, `Model/ItemSortingTests.cs`, `Items/ReviewMinorsTests.cs`,
  `Config/SetupFileTests.cs`
- Modify (Core): `Appearance/WallpaperEngineFiles.cs`, `Config/ConfigNormalizer.cs`, `Config/SetupFile.cs`,
  `Items/CollectRules.cs`, `Items/DesktopSorting.cs`, `Items/FolderPanels.cs`, `Items/FolderViews.cs`,
  `Items/IconCache.cs`, `Items/VirtualItem.cs`, `Library/GameArt.cs`, `Library/GameCatalog.cs`, `Library/GameItems.cs`,
  `Library/LibraryFiles.cs`, `Lifecycle/StartupPolicy.cs`

**Interfaces:**
- Produces: `static class NeoFences.Core.WindowsPath` with `FileName(string) → string`, `Extension(string) → string`,
  `FileNameWithoutExtension(string) → string`, `DirectoryName(string) → string?`, `Combine(string folder, string name) →
  string`, `TrimEndingSeparator(string) → string`, `IsFullyQualified(string) → bool`, `RelativePath(string folder,
  string path) → string`, `IsPlainFileName(string) → bool`.

- [ ] **Step 1: Write the failing tests.** Write this patch to `m39-1a-tests.patch` and apply it:

```diff
diff --git a/tests/NeoFences.Core.Tests/Config/SetupFileTests.cs b/tests/NeoFences.Core.Tests/Config/SetupFileTests.cs
index 53e909b..54728f1 100644
--- a/tests/NeoFences.Core.Tests/Config/SetupFileTests.cs
+++ b/tests/NeoFences.Core.Tests/Config/SetupFileTests.cs
@@ -29,10 +29,10 @@ public class SetupFileTests
         ]);
         Directory.CreateDirectory(data.File("icons"));
         Directory.CreateDirectory(data.File("covers"));
-        File.WriteAllBytes(data.File("icons\\pic.png"), [1, 2, 3]);
-        File.WriteAllBytes(data.File("icons\\unused.png"), [9]);
-        File.WriteAllBytes(data.File("covers\\choice-steam-1.jpg"), [4, 5]);
-        File.WriteAllText(data.File("covers\\index.json"), "{}");
+        File.WriteAllBytes(data.File("icons/pic.png"), [1, 2, 3]);
+        File.WriteAllBytes(data.File("icons/unused.png"), [9]);
+        File.WriteAllBytes(data.File("covers/choice-steam-1.jpg"), [4, 5]);
+        File.WriteAllText(data.File("covers/index.json"), "{}");
         return (config, items);
     }
 
@@ -73,8 +73,8 @@ public class SetupFileTests
 
         using var elsewhere = new TempDirectory();
         Assert.Empty(SetupFile.PlacePictures(read, elsewhere.Path));
-        Assert.Equal([1, 2, 3], File.ReadAllBytes(elsewhere.File("icons\\pic.png")));
-        Assert.Equal([4, 5], File.ReadAllBytes(elsewhere.File("covers\\choice-steam-1.jpg")));
+        Assert.Equal([1, 2, 3], File.ReadAllBytes(elsewhere.File("icons/pic.png")));
+        Assert.Equal([4, 5], File.ReadAllBytes(elsewhere.File("covers/choice-steam-1.jpg")));
     }
 
     [Fact]
@@ -82,7 +82,7 @@ public class SetupFileTests
     {
         using var data = new TempDirectory();
         var (config, items) = Setup(data);
-        File.Delete(data.File("icons\\pic.png"));
+        File.Delete(data.File("icons/pic.png"));
         var missing = SetupFile.Write(data.File("my.neofences"), config, items, data.Path, appVersion: "0.23.0", now: Now);
         Assert.Equal([new SetupPicture("icons", "pic.png")], missing);
         Assert.DoesNotContain(SetupFile.Read(data.File("my.neofences")).Pictures, picture => picture.Picture.Folder == "icons");
diff --git a/tests/NeoFences.Core.Tests/Items/FolderViewsTests.cs b/tests/NeoFences.Core.Tests/Items/FolderViewsTests.cs
index 1b4570e..ab4ce6e 100644
--- a/tests/NeoFences.Core.Tests/Items/FolderViewsTests.cs
+++ b/tests/NeoFences.Core.Tests/Items/FolderViewsTests.cs
@@ -14,7 +14,7 @@ public class FolderViewsTests
 
     private static ItemInfo Folder(string name, int minutesAgo = 0) => new($@"D:\View\{name}", name, IsFolder: true, TypeName: "", Day.AddMinutes(-minutesAgo));
 
-    private static IReadOnlyList<string> Names(FolderViews.Selection selection) => [.. selection.Shown.Select(Path.GetFileName)!];
+    private static IReadOnlyList<string> Names(FolderViews.Selection selection) => [.. selection.Shown.Select(WindowsPath.FileName)!];
 
     private static readonly IReadOnlyList<ItemInfo> Mixed =
     [
diff --git a/tests/NeoFences.Core.Tests/Items/ReviewMinorsTests.cs b/tests/NeoFences.Core.Tests/Items/ReviewMinorsTests.cs
index 6139a91..d6a3a83 100644
--- a/tests/NeoFences.Core.Tests/Items/ReviewMinorsTests.cs
+++ b/tests/NeoFences.Core.Tests/Items/ReviewMinorsTests.cs
@@ -16,6 +16,7 @@ public class ReviewMinorsTests
     [Fact]
     public void FolderPath_ExpandsVariables()
     {
+        if (!OperatingSystem.IsWindows()) return; // %USERPROFILE% is Windows' (M39: Core tests run on Linux too)
         var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
         Assert.Equal(Path.Combine(profile, "Downloads"), FolderViews.FolderPath(@"%USERPROFILE%\Downloads"));
     }
diff --git a/tests/NeoFences.Core.Tests/Items/WidgetsTests.cs b/tests/NeoFences.Core.Tests/Items/WidgetsTests.cs
index 7cfa152..ad29117 100644
--- a/tests/NeoFences.Core.Tests/Items/WidgetsTests.cs
+++ b/tests/NeoFences.Core.Tests/Items/WidgetsTests.cs
@@ -69,7 +69,8 @@ public class WidgetsTests
     public void ClockText_FollowsTheCulturesTimeFormats()
     {
         var time = new DateTime(2026, 10, 5, 16, 7, 9);
-        Assert.Equal("4:07 PM", Widgets.ClockText(time, seconds: false, CultureInfo.GetCultureInfo("en-US")));
+        // Linux's ICU puts a narrow no-break space before "PM", Windows a plain one (M39: Core tests run on both).
+        Assert.Equal("4:07 PM", Widgets.ClockText(time, seconds: false, CultureInfo.GetCultureInfo("en-US")).Replace(' ', ' '));
         Assert.Equal("16:07:09", Widgets.ClockText(time, seconds: true, CultureInfo.GetCultureInfo("de-DE")));
     }
 
diff --git a/tests/NeoFences.Core.Tests/Lifecycle/SafetyNetTests.cs b/tests/NeoFences.Core.Tests/Lifecycle/SafetyNetTests.cs
index 235c0c0..82d5ad8 100644
--- a/tests/NeoFences.Core.Tests/Lifecycle/SafetyNetTests.cs
+++ b/tests/NeoFences.Core.Tests/Lifecycle/SafetyNetTests.cs
@@ -187,6 +187,7 @@ public class SafetyNetTests
     [Fact]
     public void Save_ProbesTheFileOnce_WithoutTheLockedFileWait_OrTheRepair() // M33 review I6: a save never freezes the UI
     {
+        if (!OperatingSystem.IsWindows()) return; // a locked file is Windows' (Linux has no mandatory locks; M39)
         using var directory = new TempDirectory();
         var repairs = 0;
         var store = new JsonStore<NeoFencesConfig>(directory.Path, "config.json", NeoFencesConfig.CurrentSchemaVersion, ConfigJson.Deserialize,
diff --git a/tests/NeoFences.Core.Tests/Model/ItemSortingTests.cs b/tests/NeoFences.Core.Tests/Model/ItemSortingTests.cs
index cfe79c8..6491409 100644
--- a/tests/NeoFences.Core.Tests/Model/ItemSortingTests.cs
+++ b/tests/NeoFences.Core.Tests/Model/ItemSortingTests.cs
@@ -18,7 +18,7 @@ public class ItemSortingTests
         Folder("archive", 2),
     ];
 
-    private static IReadOnlyList<string> Names(IReadOnlyList<string> refs) => refs.Select(Path.GetFileName).ToList()!;
+    private static IReadOnlyList<string> Names(IReadOnlyList<string> refs) => refs.Select(WindowsPath.FileName).ToList()!;
 
     [Fact]
     public void ByName_FoldersFirst_NumbersInNaturalOrder() =>
diff --git a/tests/NeoFences.Core.Tests/Model/SnapshotsTests.cs b/tests/NeoFences.Core.Tests/Model/SnapshotsTests.cs
index 53f8746..1bdbfd3 100644
--- a/tests/NeoFences.Core.Tests/Model/SnapshotsTests.cs
+++ b/tests/NeoFences.Core.Tests/Model/SnapshotsTests.cs
@@ -198,9 +198,9 @@ public class SnapshotsTests
     }
 
     [Fact]
-    [System.Runtime.Versioning.SupportedOSPlatform("windows")] // folder ACLs
     public void Store_AFolderThatCannotBeListed_IsAProblemNotACrash()
     {
+        if (!OperatingSystem.IsWindows()) return; // folder ACLs are Windows' (M39: Core tests run on Linux too)
         using var folder = new TempDirectory();
         var denied = new System.Security.AccessControl.FileSystemAccessRule(
             System.Security.Principal.WindowsIdentity.GetCurrent().User!, System.Security.AccessControl.FileSystemRights.ListDirectory,
diff --git a/tests/NeoFences.Core.Tests/Model/UxCarryOverTests.cs b/tests/NeoFences.Core.Tests/Model/UxCarryOverTests.cs
index 33e0ddb..ee8276e 100644
--- a/tests/NeoFences.Core.Tests/Model/UxCarryOverTests.cs
+++ b/tests/NeoFences.Core.Tests/Model/UxCarryOverTests.cs
@@ -8,6 +8,8 @@ namespace NeoFences.Core.Tests.Model;
 public class UxCarryOverTests
 {
     private static readonly DateTimeOffset Taken = new(2026, 10, 4, 22, 45, 0, TimeSpan.FromHours(5.5));
+    // Made here, not looked up: Windows names its zones differently from Linux (M39, Core tests on both).
+    private static readonly TimeZoneInfo India = TimeZoneInfo.CreateCustomTimeZone("India Standard Time", TimeSpan.FromHours(5.5), "India Standard Time", "India Standard Time");
 
     // ---------- tray snapshot labels (M10 carry-over) ----------
 
@@ -19,7 +21,7 @@ public class UxCarryOverTests
     [InlineData("Name\twith tab", "Name with tab")]                 // a tab would right-align the rest
     [InlineData("   ", "Snapshot 4 Oct 22:45")]                   // blank: the date and time
     public void MenuLabel_IsOneSafeLine(string name, string expected) =>
-        Assert.Equal(expected, Snapshots.MenuLabel(new SnapshotEntry(@"C:\s\a.json", name, Taken), TimeZoneInfo.FindSystemTimeZoneById("India Standard Time")));
+        Assert.Equal(expected, Snapshots.MenuLabel(new SnapshotEntry(@"C:\s\a.json", name, Taken), India));
 
     [Fact]
     public void MenuLabel_LongNamesAreCut()
diff --git a/tests/NeoFences.Core.Tests/WindowsPathTests.cs b/tests/NeoFences.Core.Tests/WindowsPathTests.cs
new file mode 100644
index 0000000..8855b83
--- /dev/null
+++ b/tests/NeoFences.Core.Tests/WindowsPathTests.cs
@@ -0,0 +1,84 @@
+namespace NeoFences.Core.Tests;
+
+/// <summary>
+/// M39 (ADR-061): Core reads the Windows paths it stores (items, folders, game shortcuts, picture names) by Windows' rules on
+/// every OS, so its tests pass on Linux too. The answers match System.IO.Path on Windows.
+/// </summary>
+public class WindowsPathTests
+{
+    [Theory]
+    [InlineData(@"D:\Downloads\notes.txt", "notes.txt")]
+    [InlineData(@"..\..\Windows\evil.png", "evil.png")]
+    [InlineData("../../etc/evil.png", "evil.png")]
+    [InlineData("plain.jpg", "plain.jpg")]
+    [InlineData(@"D:\Music\", "")]
+    [InlineData("", "")]
+    public void FileName_IsWhatFollowsTheLastSeparator(string path, string expected) =>
+        Assert.Equal(expected, WindowsPath.FileName(path));
+
+    [Theory]
+    [InlineData(@"D:\Games\Hades.url", "Hades", ".url")]
+    [InlineData(@"C:\Tools\setup.v2.EXE", "setup.v2", ".EXE")]
+    [InlineData(@"D:\Folder.d\README", "README", "")]
+    [InlineData(@"D:\.hidden", "", ".hidden")]
+    public void Stem_AndExtension(string path, string stem, string extension)
+    {
+        Assert.Equal(stem, WindowsPath.FileNameWithoutExtension(path));
+        Assert.Equal(extension, WindowsPath.Extension(path));
+    }
+
+    [Theory]
+    [InlineData(@"D:\GameLibrary\Cyberpunk", @"D:\GameLibrary")]
+    [InlineData(@"D:\Music", @"D:\")]
+    [InlineData(@"D:\", null)]
+    [InlineData(@"\\nas\share\pics", @"\\nas\share")]
+    [InlineData(@"\\nas\share", null)]
+    [InlineData("name.txt", null)]
+    public void DirectoryName_IsTheParent_NoneAboveARoot(string path, string? expected) =>
+        Assert.Equal(expected, WindowsPath.DirectoryName(path));
+
+    [Theory]
+    [InlineData(@"C:\Data\NeoFences\library", "Hades.url", @"C:\Data\NeoFences\library\Hades.url")]
+    [InlineData(@"D:\", "Music", @"D:\Music")]
+    [InlineData(@"D:\Root\", "a.txt", @"D:\Root\a.txt")]
+    public void Combine_PutsOneBackslashBetween(string folder, string name, string expected) =>
+        Assert.Equal(expected, WindowsPath.Combine(folder, name));
+
+    [Theory]
+    [InlineData(@"D:\GameLibrary\", @"D:\GameLibrary")]
+    [InlineData(@"\\nas\share\", @"\\nas\share")]
+    [InlineData(@"D:\", @"D:\")]
+    [InlineData(@"D:\GameLibrary", @"D:\GameLibrary")]
+    public void TrimEndingSeparator_KeepsADrivesRoot(string path, string expected) =>
+        Assert.Equal(expected, WindowsPath.TrimEndingSeparator(path));
+
+    [Theory]
+    [InlineData(@"D:\GameLibrary", true)]
+    [InlineData("D:/GameLibrary", true)]
+    [InlineData(@"\\nas\share\pics", true)]
+    [InlineData(@"D:GameLibrary", false)]
+    [InlineData(@"\GameLibrary", false)]
+    [InlineData(@"GameLibrary\x", false)]
+    [InlineData("/home/user", false)]
+    public void IsFullyQualified_IsADriveWithItsSeparator_OrAShare(string path, bool expected) =>
+        Assert.Equal(expected, WindowsPath.IsFullyQualified(path));
+
+    [Theory]
+    [InlineData(@"D:\GameLibrary", @"D:\GameLibrary\Cyberpunk\bin", @"Cyberpunk\bin")]
+    [InlineData(@"D:\GameLibrary", @"d:\gamelibrary\Saves", "Saves")]
+    [InlineData(@"D:\", @"D:\Music", "Music")]
+    [InlineData(@"D:\GameLibrary", @"D:\GameLibrary", ".")]
+    [InlineData(@"D:\GameLibrary", @"E:\Other", @"E:\Other")]
+    public void RelativePath_IsWhatFollowsTheFolder(string folder, string path, string expected) =>
+        Assert.Equal(expected, WindowsPath.RelativePath(folder, path));
+
+    [Theory]
+    [InlineData("scene.json", true)]
+    [InlineData("a:b", false)]
+    [InlineData("what?", false)]
+    [InlineData(@"..\x", false)]
+    [InlineData("..", false)]
+    [InlineData("", false)]
+    public void IsPlainFileName_RefusesSeparatorsAndWindowsReservedCharacters(string name, bool expected) =>
+        Assert.Equal(expected, WindowsPath.IsPlainFileName(name));
+}
```

- [ ] **Step 2: Run them.** `dotnet test tests/NeoFences.Core.Tests` → Expected: build fails with `error CS0103: The name 'WindowsPath' does not exist in the current context`.
- [ ] **Step 3: Implement.** Write this patch to `m39-1b-core.patch` and apply it:

```diff
diff --git a/src/NeoFences.Core/Appearance/WallpaperEngineFiles.cs b/src/NeoFences.Core/Appearance/WallpaperEngineFiles.cs
index 3810640..3ae4cb1 100644
--- a/src/NeoFences.Core/Appearance/WallpaperEngineFiles.cs
+++ b/src/NeoFences.Core/Appearance/WallpaperEngineFiles.cs
@@ -67,7 +67,7 @@ public static class WallpaperEngineFiles
             if (document.RootElement.ValueKind != JsonValueKind.Object
                 || !document.RootElement.TryGetProperty("preview", out var preview) || preview.ValueKind != JsonValueKind.String) return null;
             var name = preview.GetString();
-            return name is { Length: > 0 } && Path.GetFileName(name) == name && name != ".." && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 ? name : null;
+            return name is { Length: > 0 } && WindowsPath.IsPlainFileName(name) ? name : null;
         }
         catch (JsonException)
         {
diff --git a/src/NeoFences.Core/Config/ConfigNormalizer.cs b/src/NeoFences.Core/Config/ConfigNormalizer.cs
index e0b6547..0b56da0 100644
--- a/src/NeoFences.Core/Config/ConfigNormalizer.cs
+++ b/src/NeoFences.Core/Config/ConfigNormalizer.cs
@@ -136,9 +136,9 @@ public static class ConfigNormalizer
         OnlineArt = library?.OnlineArt, // M34
         // M34: only a file name in NeoFences' covers folder counts (a hand-edited path never points elsewhere).
         CoverChoices = (library?.CoverChoices ?? new Dictionary<string, string>())
-            .Where(choice => !string.IsNullOrWhiteSpace(choice.Key) && !string.IsNullOrWhiteSpace(Path.GetFileName(choice.Value ?? "")))
+            .Where(choice => !string.IsNullOrWhiteSpace(choice.Key) && !string.IsNullOrWhiteSpace(WindowsPath.FileName(choice.Value ?? "")))
             .GroupBy(choice => choice.Key, StringComparer.OrdinalIgnoreCase)
-            .ToDictionary(group => group.Key, group => Path.GetFileName(group.First().Value), StringComparer.OrdinalIgnoreCase),
+            .ToDictionary(group => group.Key, group => WindowsPath.FileName(group.First().Value), StringComparer.OrdinalIgnoreCase),
     };
 
     private static Dictionary<string, Layout> NormalizeLayouts(IReadOnlyDictionary<string, Layout>? loadedLayouts)
diff --git a/src/NeoFences.Core/Config/SetupFile.cs b/src/NeoFences.Core/Config/SetupFile.cs
index 826d472..0855bf2 100644
--- a/src/NeoFences.Core/Config/SetupFile.cs
+++ b/src/NeoFences.Core/Config/SetupFile.cs
@@ -53,7 +53,7 @@ public static class SetupFile
     public static IReadOnlyList<SetupPicture> PicturesOf(NeoFencesConfig config, ItemsDocument items) =>
         items.Fences.Values.SelectMany(list => list).Select(item => item.Icon?.Image).Select(name => (Folder: IconsFolder, Name: name))
             .Concat(config.Library.CoverChoices.Values.Select(name => (Folder: CoversFolder, Name: (string?)name)))
-            .Select(picture => (picture.Folder, Name: Path.GetFileName(picture.Name ?? "")))
+            .Select(picture => (picture.Folder, Name: WindowsPath.FileName(picture.Name ?? "")))
             .Where(picture => picture.Name.Length > 0)
             .Select(picture => new SetupPicture(picture.Folder, picture.Name))
             .Distinct()
@@ -164,7 +164,7 @@ public static class SetupFile
             {
                 var folder = Path.Combine(dataDirectory, picture.Folder);
                 Directory.CreateDirectory(folder);
-                var target = Path.Combine(folder, Path.GetFileName(picture.FileName));
+                var target = Path.Combine(folder, WindowsPath.FileName(picture.FileName));
                 File.WriteAllBytes(target + ".tmp", bytes);
                 File.Move(target + ".tmp", target, overwrite: true);
             }
diff --git a/src/NeoFences.Core/Items/CollectRules.cs b/src/NeoFences.Core/Items/CollectRules.cs
index c44fb89..5a66d9e 100644
--- a/src/NeoFences.Core/Items/CollectRules.cs
+++ b/src/NeoFences.Core/Items/CollectRules.cs
@@ -54,7 +54,7 @@ public static class CollectRules
     public static CollectKinds KindOf(string name, bool isFolder)
     {
         if (isFolder) return CollectKinds.None;
-        var extension = Path.GetExtension(name);
+        var extension = WindowsPath.Extension(name);
         if (InstallerExtensions.Contains(extension)) return CollectKinds.Installers;
         if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
             && (name.Contains("setup", StringComparison.OrdinalIgnoreCase) || name.Contains("install", StringComparison.OrdinalIgnoreCase)))
diff --git a/src/NeoFences.Core/Items/DesktopSorting.cs b/src/NeoFences.Core/Items/DesktopSorting.cs
index 61fac0b..dd87f27 100644
--- a/src/NeoFences.Core/Items/DesktopSorting.cs
+++ b/src/NeoFences.Core/Items/DesktopSorting.cs
@@ -44,7 +44,7 @@ public static class DesktopSorting
         if (knownGames?.Contains(entry.ItemRef) == true) return DesktopGroup.Games;
         if (entry.LinkTarget is not { } target)
         {
-            var isShortcut = Path.GetExtension(entry.ItemRef).ToLowerInvariant() is ".lnk" or ".url";
+            var isShortcut = WindowsPath.Extension(entry.ItemRef).ToLowerInvariant() is ".lnk" or ".url";
             return !isShortcut && IsProgram(entry.ItemRef) ? DesktopGroup.Apps : DesktopGroup.FoldersAndFiles;
         }
         if (GameLaunchers.LauncherOf($"{target} {entry.LinkArguments}".Trim()) is not null
@@ -58,5 +58,5 @@ public static class DesktopSorting
         return DesktopGroup.FoldersAndFiles;
     }
 
-    private static bool IsProgram(string path) => ProgramExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
+    private static bool IsProgram(string path) => ProgramExtensions.Contains(WindowsPath.Extension(path).ToLowerInvariant());
 }
diff --git a/src/NeoFences.Core/Items/FolderPanels.cs b/src/NeoFences.Core/Items/FolderPanels.cs
index ab1c38f..76c5b1c 100644
--- a/src/NeoFences.Core/Items/FolderPanels.cs
+++ b/src/NeoFences.Core/Items/FolderPanels.cs
@@ -46,7 +46,7 @@ public sealed record PanelPlace(string Current, IReadOnlyList<string> History)
     public PanelPlace Back() => History.Count == 0 ? this : new(History[^1], [.. History.Take(History.Count - 1)]);
 
     public PanelPlace Up(string home) =>
-        CanGoUp(home) && Path.GetDirectoryName(FolderViews.ListedFolder(Current)) is { } parent ? Into(parent) : this;
+        CanGoUp(home) && WindowsPath.DirectoryName(FolderViews.ListedFolder(Current)) is { } parent ? Into(parent) : this;
 
     public PanelPlace Home(string home) => FolderViews.SameFolder(Current, home) ? this : Into(home);
 
@@ -164,7 +164,7 @@ public static class FolderPanels
     {
         var name = ownName ?? FolderViews.NameOf(home);
         if (!PanelPlace.IsBelow(shown, home)) return name;
-        var below = Path.GetRelativePath(FolderViews.ListedFolder(home), FolderViews.ListedFolder(shown));
+        var below = WindowsPath.RelativePath(FolderViews.ListedFolder(home), FolderViews.ListedFolder(shown));
         return string.Join(" › ", [name, .. below.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)]);
     }
 
diff --git a/src/NeoFences.Core/Items/FolderViews.cs b/src/NeoFences.Core/Items/FolderViews.cs
index 6a614a3..dcab97b 100644
--- a/src/NeoFences.Core/Items/FolderViews.cs
+++ b/src/NeoFences.Core/Items/FolderViews.cs
@@ -72,7 +72,7 @@ public static class FolderViews
     /// The path a view lists and watches: a trailing separator goes, except on a drive root — "C:" alone means the current
     /// folder on drive C, not its root (final review I2).
     /// </summary>
-    public static string ListedFolder(string path) => Path.TrimEndingDirectorySeparator(path);
+    public static string ListedFolder(string path) => WindowsPath.TrimEndingSeparator(path);
 
     /// <summary>
     /// The folder typed in Folder view settings (M23): variables expanded (%USERPROFILE%), and only a full path — a relative
@@ -81,7 +81,7 @@ public static class FolderViews
     public static string? FolderPath(string text)
     {
         var expanded = Environment.ExpandEnvironmentVariables(text.Trim());
-        return expanded.Length > 0 && !expanded.Contains('%') && Path.IsPathFullyQualified(expanded) ? expanded : null;
+        return expanded.Length > 0 && !expanded.Contains('%') && WindowsPath.IsFullyQualified(expanded) ? expanded : null;
     }
 
     public static bool SameFolder(string left, string right) =>
diff --git a/src/NeoFences.Core/Items/IconCache.cs b/src/NeoFences.Core/Items/IconCache.cs
index 8fdcb40..14df1b7 100644
--- a/src/NeoFences.Core/Items/IconCache.cs
+++ b/src/NeoFences.Core/Items/IconCache.cs
@@ -67,7 +67,7 @@ public sealed record IconCacheIndex
             foreach (var (key, entry) in stored.Entries ?? [])
             {
                 // Only a PNG's own name in the cache folder counts: a hand-edited "..\x" never points elsewhere.
-                if (string.IsNullOrWhiteSpace(key) || entry is null || entry.File.Length == 0 || entry.File != Path.GetFileName(entry.File)
+                if (string.IsNullOrWhiteSpace(key) || entry is null || entry.File.Length == 0 || entry.File != WindowsPath.FileName(entry.File)
                     || !entry.File.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                 entries[key] = entry;
             }
diff --git a/src/NeoFences.Core/Items/VirtualItem.cs b/src/NeoFences.Core/Items/VirtualItem.cs
index 08345a4..7a3db39 100644
--- a/src/NeoFences.Core/Items/VirtualItem.cs
+++ b/src/NeoFences.Core/Items/VirtualItem.cs
@@ -109,7 +109,7 @@ public static class ItemKinds
     /// extension (its id is <c>{GUID}\folder\x.exe</c>), else the front of a Store app's id ("Microsoft.WindowsCalculator").
     /// </summary>
     public static string AppName(string appId) =>
-        appId.Contains('\\') ? Path.GetFileNameWithoutExtension(appId) : appId.Split('_', '!')[0];
+        appId.Contains('\\') ? WindowsPath.FileNameWithoutExtension(appId) : appId.Split('_', '!')[0];
 
     /// <summary>The app's id (AppUserModelID), or null when the target is not an app.</summary>
     public static string? AppIdOf(string target) =>
diff --git a/src/NeoFences.Core/Library/GameArt.cs b/src/NeoFences.Core/Library/GameArt.cs
index 55df77d..d732777 100644
--- a/src/NeoFences.Core/Library/GameArt.cs
+++ b/src/NeoFences.Core/Library/GameArt.cs
@@ -28,7 +28,7 @@ public static class GameArt
         if (chosen is not null) return new CoverArt(chosen, IsPoster: true);
         if (poster is not null) return new CoverArt(poster, IsPoster: true);
         if (online is not null) return new CoverArt(online, IsPoster: true);
-        return logo is not null && PictureExtensions.Contains(Path.GetExtension(logo).ToLowerInvariant()) ? new CoverArt(logo, IsPoster: false) : null;
+        return logo is not null && PictureExtensions.Contains(WindowsPath.Extension(logo).ToLowerInvariant()) ? new CoverArt(logo, IsPoster: false) : null;
     }
 
     /// <summary>The online lookup runs for games without a chosen cover or a cover on disk (a logo still looks up).</summary>
diff --git a/src/NeoFences.Core/Library/GameCatalog.cs b/src/NeoFences.Core/Library/GameCatalog.cs
index af6c32e..523f61b 100644
--- a/src/NeoFences.Core/Library/GameCatalog.cs
+++ b/src/NeoFences.Core/Library/GameCatalog.cs
@@ -182,7 +182,7 @@ public static partial class ProgramPicker
             .Where(program =>
             {
                 var parts = program.RelativePath.Split('\\', '/');
-                return !parts[..^1].Any(SkippedFolder().IsMatch) && !SkippedProgram().IsMatch(Path.GetFileNameWithoutExtension(parts[^1]));
+                return !parts[..^1].Any(SkippedFolder().IsMatch) && !SkippedProgram().IsMatch(WindowsPath.FileNameWithoutExtension(parts[^1]));
             })
             .OrderByDescending(program => program.Size).ThenBy(program => program.RelativePath, StringComparer.OrdinalIgnoreCase)
             .Select(program => program.RelativePath)
diff --git a/src/NeoFences.Core/Library/GameItems.cs b/src/NeoFences.Core/Library/GameItems.cs
index ac46d8e..aa5f0ab 100644
--- a/src/NeoFences.Core/Library/GameItems.cs
+++ b/src/NeoFences.Core/Library/GameItems.cs
@@ -16,7 +16,7 @@ public static class GameItems
     public static bool ShowsCover(VirtualItem item) => IsGame(item) && item.ShowAs != ItemShow.Icon;
 
     public static VirtualItem Create(LibraryItem game, string libraryFolder) =>
-        VirtualItem.Create(Path.Combine(libraryFolder, game.FileName)) with { GameId = game.Game.Id };
+        VirtualItem.Create(WindowsPath.Combine(libraryFolder, game.FileName)) with { GameId = game.Game.Id };
 
     /// <param name="MigratedFenceIds">Fences that were Game Library fences and now hold game items.</param>
     public sealed record Migration(NeoFencesConfig Config, ItemsDocument Items, IReadOnlyList<string> MigratedFenceIds);
@@ -78,7 +78,7 @@ public static class GameItems
         var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
         foreach (var game in library.Items)
         {
-            foreach (var id in GameCatalog.IdsOf(game.Game)) files.TryAdd(id, Path.Combine(libraryFolder, game.FileName));
+            foreach (var id in GameCatalog.IdsOf(game.Game)) files.TryAdd(id, WindowsPath.Combine(libraryFolder, game.FileName));
         }
         var changed = false;
         var fences = new Dictionary<string, IReadOnlyList<VirtualItem>>();
diff --git a/src/NeoFences.Core/Library/LibraryFiles.cs b/src/NeoFences.Core/Library/LibraryFiles.cs
index 79f0f0e..16d7a15 100644
--- a/src/NeoFences.Core/Library/LibraryFiles.cs
+++ b/src/NeoFences.Core/Library/LibraryFiles.cs
@@ -34,7 +34,7 @@ public static class LibraryFiles
         // First keep the names of files that still fit their game, so a new game never takes them over.
         foreach (var game in games)
         {
-            if (previousById.TryGetValue(game.Id, out var old) && Fits(old.FileName, game) && takenBases.Add(Path.GetFileNameWithoutExtension(old.FileName)))
+            if (previousById.TryGetValue(game.Id, out var old) && Fits(old.FileName, game) && takenBases.Add(WindowsPath.FileNameWithoutExtension(old.FileName)))
                 fileNames[game.Id] = old.FileName;
         }
         foreach (var game in games.Where(game => !fileNames.ContainsKey(game.Id)))
@@ -83,7 +83,7 @@ public static class LibraryFiles
         var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
         var items = (state.Items ?? [])
             .Where(item => item?.Game is { Id.Length: > 0, Name: not null, ScanKey: not null, Launch.Target.Length: > 0 }
-                           && item.FileName is { Length: > 0 } fileName && fileName == Path.GetFileName(fileName)
+                           && item.FileName is { Length: > 0 } fileName && fileName == WindowsPath.FileName(fileName)
                            && fileName is not ("." or "..") && fileName.IndexOfAny(['/', '\\', ':']) < 0
                            && fileNames.Add(fileName))
             .Select(item => item with { Signature = item.Signature ?? "", Game = item.Game with { OtherIds = item.Game.OtherIds ?? [] } })
@@ -107,8 +107,8 @@ public static class LibraryFiles
     /// <summary>The old file still names this game: "Blur.lnk", or "Blur (2).url" after a clash.</summary>
     private static bool Fits(string fileName, GameEntry game)
     {
-        if (!Path.GetExtension(fileName).Equals(Extension(game), StringComparison.OrdinalIgnoreCase)) return false;
-        var stem = Path.GetFileNameWithoutExtension(fileName);
+        if (!WindowsPath.Extension(fileName).Equals(Extension(game), StringComparison.OrdinalIgnoreCase)) return false;
+        var stem = WindowsPath.FileNameWithoutExtension(fileName);
         var safe = SafeName(game.Name);
         return stem.Equals(safe, StringComparison.OrdinalIgnoreCase)
             || (stem.StartsWith(safe + " (", StringComparison.OrdinalIgnoreCase) && stem.EndsWith(')') && int.TryParse(stem[(safe.Length + 2)..^1], out _));
diff --git a/src/NeoFences.Core/Lifecycle/StartupPolicy.cs b/src/NeoFences.Core/Lifecycle/StartupPolicy.cs
index a771c2f..f6328c9 100644
--- a/src/NeoFences.Core/Lifecycle/StartupPolicy.cs
+++ b/src/NeoFences.Core/Lifecycle/StartupPolicy.cs
@@ -34,10 +34,10 @@ public static class StartupPolicy
     /// </summary>
     public static bool IsInstalledExe(string exePath, Func<string, bool> fileExists)
     {
-        var folder = Path.GetDirectoryName(exePath);
-        if (folder is null || !string.Equals(Path.GetFileName(folder), "current", StringComparison.OrdinalIgnoreCase)) return false;
-        var root = Path.GetDirectoryName(folder);
-        return root is not null && fileExists(Path.Combine(root, "Update.exe"));
+        var folder = WindowsPath.DirectoryName(exePath);
+        if (folder is null || !string.Equals(WindowsPath.FileName(folder), "current", StringComparison.OrdinalIgnoreCase)) return false;
+        var root = WindowsPath.DirectoryName(folder);
+        return root is not null && fileExists(WindowsPath.Combine(root, "Update.exe"));
     }
 
     /// <summary>The command line stored for sign-in: the quoted exe path.</summary>
diff --git a/src/NeoFences.Core/WindowsPath.cs b/src/NeoFences.Core/WindowsPath.cs
new file mode 100644
index 0000000..eb2a6ed
--- /dev/null
+++ b/src/NeoFences.Core/WindowsPath.cs
@@ -0,0 +1,83 @@
+namespace NeoFences.Core;
+
+/// <summary>
+/// Windows paths as Core stores them — items, folders, game shortcuts, picture names — read by Windows' rules on every OS
+/// (M39, ADR-061): System.IO.Path follows the OS it runs on, so on Linux a backslash is not a separator and Core's tests
+/// (and a later port) would read Windows paths wrongly. Pure text; NeoFences' own files are still opened with
+/// System.IO.Path. Both separators count, so a name with "..\" or "../" never leaves its folder.
+/// </summary>
+public static class WindowsPath
+{
+    private static readonly char[] Separators = ['\\', '/'];
+    private static readonly char[] Reserved = ['<', '>', ':', '"', '|', '?', '*'];
+
+    /// <summary>What follows the last separator ("" for a path that ends in one).</summary>
+    public static string FileName(string path) => path[(path.LastIndexOfAny(Separators) + 1)..];
+
+    /// <summary>The file name's last ".xyz", with its dot ("" without one).</summary>
+    public static string Extension(string path)
+    {
+        var name = FileName(path);
+        var dot = name.LastIndexOf('.');
+        return dot < 0 ? "" : name[dot..];
+    }
+
+    public static string FileNameWithoutExtension(string path)
+    {
+        var name = FileName(path);
+        var dot = name.LastIndexOf('.');
+        return dot < 0 ? name : name[..dot];
+    }
+
+    /// <summary>The parent folder; null for a drive's root, a share's root, or a bare name.</summary>
+    public static string? DirectoryName(string path)
+    {
+        var trimmed = TrimEndingSeparator(path);
+        var cut = trimmed.LastIndexOfAny(Separators);
+        if (cut < 0 || trimmed.Length <= RootLength(trimmed)) return null;
+        var parent = trimmed[..cut];
+        if (IsUnc(trimmed) && parent.Length <= 2) return null; // \\server: not a folder
+        return parent.Length == 2 && parent[1] == ':' ? parent + '\\' : parent; // D: → D:\
+    }
+
+    /// <summary>A folder and a name with one backslash between.</summary>
+    public static string Combine(string folder, string name) =>
+        folder.Length == 0 || folder[^1] is '\\' or '/' ? folder + name : folder + '\\' + name;
+
+    /// <summary>Without its trailing separator — except a drive's root ("D:\"), which needs it.</summary>
+    public static string TrimEndingSeparator(string path) =>
+        path.Length > 1 && path[^1] is '\\' or '/' && !(path.Length == 3 && path[1] == ':') ? path[..^1] : path;
+
+    /// <summary>A drive with its separator ("D:\...") or a share ("\\server\share..."); not "D:x", "\x" or "x\y".</summary>
+    public static bool IsFullyQualified(string path) =>
+        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/'
+        || IsUnc(path) && path.Length > 2 && path[2] is not ('\\' or '/');
+
+    /// <summary>The path below the folder (case-insensitive, as on Windows); "." for the folder itself; else the path unchanged.</summary>
+    public static string RelativePath(string folder, string path)
+    {
+        var from = TrimEndingSeparator(folder);
+        var to = TrimEndingSeparator(path);
+        if (to.Equals(from, StringComparison.OrdinalIgnoreCase)) return ".";
+        var prefix = from[^1] is '\\' or '/' ? from : from + '\\';
+        return to.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? to[prefix.Length..] : path;
+    }
+
+    /// <summary>A name that is only a file name on Windows: no separators, no reserved characters, not "." or "..".</summary>
+    public static bool IsPlainFileName(string name) =>
+        name.Length > 0 && name is not ("." or "..") && name.IndexOfAny(Separators) < 0 && name.IndexOfAny(Reserved) < 0
+        && name.All(character => character >= ' ');
+
+    private static bool IsUnc(string path) => path.Length >= 2 && path[0] is '\\' or '/' && path[1] is '\\' or '/';
+
+    private static int RootLength(string path)
+    {
+        if (path.Length >= 2 && path[1] == ':') return path.Length >= 3 && path[2] is '\\' or '/' ? 3 : 2;
+        if (!IsUnc(path)) return 0;
+        // \\server\share: the root is both names
+        var server = path.IndexOfAny(Separators, 2);
+        if (server < 0) return path.Length;
+        var share = path.IndexOfAny(Separators, server + 1);
+        return share < 0 ? path.Length : share;
+    }
+}
```

- [ ] **Step 4: Run on Windows.** `dotnet build` → `0 Warning(s)`; `dotnet test` → Expected: `Passed: 878`.
- [ ] **Step 5: Run on Linux.** Commit first (`git add -A && git commit -m "fix: made Core read stored Windows paths the same on every OS so its tests pass on Linux"`), then in WSL
  `cd ~/nf-m39-exec && git pull -q && ~/.dotnet/dotnet test tests/NeoFences.Core.Tests -c Release` → Expected: `Passed: 878`, `Failed: 0`.
  Ledger the Task 1 calls.

### Task 2: Trust files, Licence and notices, issue forms

**Files:**
- Create: `LICENSE.txt`, `PRIVACY.md`, `SECURITY.md`, `THIRD-PARTY-NOTICES.txt`, `.github/ISSUE_TEMPLATE/bug.yml`,
  `.github/ISSUE_TEMPLATE/idea.yml`, `.github/ISSUE_TEMPLATE/config.yml`
- Modify: `src/NeoFences.App/NeoFences.App.csproj` (the three files copied next to the exe),
  `src/NeoFences.App/SettingsWindow.xaml(.cs)` (`LicenceButton`, `LicenceRequested`), `src/NeoFences.App/FenceHost.cs`
  (opens `LICENSE.txt`), `docs/GUIDE.md` (Install section, About, Troubleshooting), `tools/vm/guest/guest-checks.ps1`
  (`trust-files` check)

**Interfaces:**
- Produces: `SettingsWindow.LicenceRequested` (`Action`); files `LICENSE.txt`, `THIRD-PARTY-NOTICES.txt`, `PRIVACY.md` in
  the build output and every install; guest check id `trust-files`; GUIDE anchor `#install`.

- [ ] **Step 1: Implement.** Write this patch to `m39-2-trust.patch` and apply it:

```diff
diff --git a/.github/ISSUE_TEMPLATE/bug.yml b/.github/ISSUE_TEMPLATE/bug.yml
new file mode 100644
index 0000000..99693db
--- /dev/null
+++ b/.github/ISSUE_TEMPLATE/bug.yml
@@ -0,0 +1,52 @@
+name: Bug
+description: Something in NeoFences does not work as it should.
+labels: [bug]
+body:
+  - type: markdown
+    attributes:
+      value: Thanks for reporting! Security problems go privately through the Security tab instead (see SECURITY.md).
+  - type: input
+    id: version
+    attributes:
+      label: NeoFences version
+      description: Settings → About shows it.
+      placeholder: "0.26.0"
+    validations:
+      required: true
+  - type: input
+    id: windows
+    attributes:
+      label: Windows version
+      description: Press Win+R, type winver, press Enter.
+      placeholder: "Windows 11 25H2 (26200)"
+    validations:
+      required: true
+  - type: textarea
+    id: steps
+    attributes:
+      label: What you did
+      description: The steps, one per line, so someone else can do the same.
+    validations:
+      required: true
+  - type: textarea
+    id: expected
+    attributes:
+      label: What you expected
+    validations:
+      required: true
+  - type: textarea
+    id: actual
+    attributes:
+      label: What happened instead
+      description: Screenshots help.
+    validations:
+      required: true
+  - type: textarea
+    id: logs
+    attributes:
+      label: Log (optional)
+      description: >-
+        Settings → About → Open logs folder; paste the lines from around the problem in the newest file. Logs never contain
+        your Windows user name, but they do name the folders and files your fences point at — everything here is public,
+        so look through it first.
+      render: text
diff --git a/.github/ISSUE_TEMPLATE/config.yml b/.github/ISSUE_TEMPLATE/config.yml
new file mode 100644
index 0000000..834ff79
--- /dev/null
+++ b/.github/ISSUE_TEMPLATE/config.yml
@@ -0,0 +1,8 @@
+blank_issues_enabled: false
+contact_links:
+  - name: Security problem
+    url: https://github.com/CipherSnowden/neo-fences/security/advisories/new
+    about: Report security problems privately (see SECURITY.md), not as a public issue.
+  - name: The guide
+    url: https://github.com/CipherSnowden/neo-fences/blob/main/docs/GUIDE.md
+    about: How everything works, a cheat-sheet and troubleshooting.
diff --git a/.github/ISSUE_TEMPLATE/idea.yml b/.github/ISSUE_TEMPLATE/idea.yml
new file mode 100644
index 0000000..fede3d1
--- /dev/null
+++ b/.github/ISSUE_TEMPLATE/idea.yml
@@ -0,0 +1,16 @@
+name: Idea
+description: Suggest something new or better.
+labels: [enhancement]
+body:
+  - type: textarea
+    id: what
+    attributes:
+      label: What would you like?
+    validations:
+      required: true
+  - type: textarea
+    id: why
+    attributes:
+      label: Why — what would it help you do?
+    validations:
+      required: true
diff --git a/LICENSE.txt b/LICENSE.txt
new file mode 100644
index 0000000..44bfaab
--- /dev/null
+++ b/LICENSE.txt
@@ -0,0 +1,21 @@
+NeoFences Freeware Licence
+Copyright © 2026 CipherSnowden. All rights reserved.
+
+1. You may download, install and use NeoFences free of charge, at home or at work, on any number of computers.
+
+2. You may share the unmodified installer or portable zip from the official release page, free of charge, together
+   with this licence.
+
+3. You may not sell NeoFences, charge for it, bundle it with paid products, or distribute modified versions.
+
+4. The source code is published so anyone can see what NeoFences does. You may read it and build it for your own use;
+   you may not distribute it, or work based on it, without written permission.
+
+5. NeoFences is provided "as is", without warranty of any kind. The author is not liable for any damage arising from
+   its use.
+
+6. Third-party components keep their own licences (see THIRD-PARTY-NOTICES.txt).
+
+7. All rights not expressly granted stay with the author.
+
+Official release page: https://github.com/CipherSnowden/neo-fences/releases
diff --git a/PRIVACY.md b/PRIVACY.md
new file mode 100644
index 0000000..0b0c5b1
--- /dev/null
+++ b/PRIVACY.md
@@ -0,0 +1,25 @@
+# Privacy
+
+NeoFences has **no telemetry, no accounts and no ads**. It does not collect, send or sell anything about you.
+
+## What goes over the network
+
+- **Updates** — NeoFences asks GitHub whether a newer version exists and, if so, downloads it from this project's
+  release page (`github.com/CipherSnowden/neo-fences/releases`). GitHub sees the request like any download.
+- **Covers and website icons — only if you turn them on.** The switch **Find covers and website icons online** (asked
+  once; also in Settings → Game Library) is off by default. When it is on:
+  - the names of your games are sent to Steam's store search, and their cover pictures are downloaded from Steam;
+  - for a website item, its icon is read from that website itself.
+
+Nothing else. Opening an item you put in a fence (a website, a program) is you opening it, as from the desktop.
+
+## What stays on your PC
+
+Your fences, items, settings, snapshots, backups, the icon cache and the logs live in `%LOCALAPPDATA%\NeoFences` and
+never leave your PC. NeoFences never moves, renames, changes or deletes your files: fences hold links to them.
+
+## Logs and bug reports
+
+The logs help find bugs. They never contain your Windows user name (your profile folder is written as `%USERPROFILE%`),
+but they do contain the names of folders and files your fences point at. If you attach logs to a bug report, they
+become public on GitHub — look through them first and leave out what you do not want to share.
diff --git a/SECURITY.md b/SECURITY.md
new file mode 100644
index 0000000..59f0a1c
--- /dev/null
+++ b/SECURITY.md
@@ -0,0 +1,20 @@
+# Security
+
+## Reporting a vulnerability
+
+Please report security problems **privately**: on this repository's **Security** tab, choose **Report a
+vulnerability**. Do not open a public issue for them. You will get an answer as soon as possible, and a fix in a new
+release when the problem is confirmed.
+
+In scope: the NeoFences app, its installer and portable zip, and its update path (how it finds, downloads and installs
+new versions). Examples: a way for a website, a file or a setup file (`.neofences`) to run code, read files it should
+not, or make NeoFences change or delete a user's files.
+
+## Supported versions
+
+Only the latest release gets fixes. NeoFences updates itself, so staying current is automatic.
+
+## Checking a download
+
+Every release has `SHA256SUMS.txt` and a GitHub build attestation. See "Check your download" in the
+[README](README.md).
diff --git a/THIRD-PARTY-NOTICES.txt b/THIRD-PARTY-NOTICES.txt
new file mode 100644
index 0000000..5ae8eff
--- /dev/null
+++ b/THIRD-PARTY-NOTICES.txt
@@ -0,0 +1,1757 @@
+NeoFences — third-party notices
+
+NeoFences (see LICENSE.txt) includes the components below. Each keeps its own licence; their notices follow.
+
+1. Serilog 4.4.0 — https://serilog.net — Copyright © Serilog Contributors — Apache License 2.0 (text in appendix A)
+2. Serilog.Sinks.File 7.0.0 — https://github.com/serilog/serilog-sinks-file — Copyright © Serilog Contributors —
+   Apache License 2.0 (text in appendix A)
+3. Velopack 1.2.161 (the installer, Update.exe and the update library) — https://github.com/velopack/velopack — MIT:
+
+   Copyright © 2021 Caelan Sayler
+   Copyright © 2024 Velopack Ltd.
+   
+   Permission is hereby granted,  free of charge,  to any person obtaining a
+   copy of this software and associated documentation files (the "Software"),
+   to deal in the Software without restriction, including without limitation
+   the rights to  use, copy, modify, merge, publish, distribute, sublicense,
+   and/or sell copies of the Software, and to permit persons to whom the
+   Software is furnished to do so, subject to the following conditions:
+   
+   The above copyright notice and this permission notice shall be included in
+   all copies or substantial portions of the Software.
+   
+   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
+   IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
+   FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
+   AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
+   LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
+   FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
+   DEALINGS IN THE SOFTWARE.
+
+4. Code generated by Microsoft.Windows.CsWin32 0.3.335 — https://github.com/microsoft/CsWin32 —
+   © Microsoft Corporation. MIT License (the same text as .NET's below).
+5. .NET 10 runtime and Windows Presentation Foundation (WPF), included in the self-contained app —
+   https://github.com/dotnet/runtime, https://github.com/dotnet/wpf — MIT:
+
+   The MIT License (MIT)
+   
+   Copyright (c) .NET Foundation and Contributors
+   
+   All rights reserved.
+   
+   Permission is hereby granted, free of charge, to any person obtaining a copy
+   of this software and associated documentation files (the "Software"), to deal
+   in the Software without restriction, including without limitation the rights
+   to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
+   copies of the Software, and to permit persons to whom the Software is
+   furnished to do so, subject to the following conditions:
+   
+   The above copyright notice and this permission notice shall be included in all
+   copies or substantial portions of the Software.
+   
+   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
+   IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
+   FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
+   AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
+   LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
+   OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
+   SOFTWARE.
+
+   The .NET runtime and WPF themselves include third-party components; their notices are in appendices B and C.
+
+================================================================================
+Appendix A — Apache License 2.0 (Serilog, Serilog.Sinks.File)
+================================================================================
+
+
+                                 Apache License
+                           Version 2.0, January 2004
+                        http://www.apache.org/licenses/
+
+   TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION
+
+   1. Definitions.
+
+      "License" shall mean the terms and conditions for use, reproduction,
+      and distribution as defined by Sections 1 through 9 of this document.
+
+      "Licensor" shall mean the copyright owner or entity authorized by
+      the copyright owner that is granting the License.
+
+      "Legal Entity" shall mean the union of the acting entity and all
+      other entities that control, are controlled by, or are under common
+      control with that entity. For the purposes of this definition,
+      "control" means (i) the power, direct or indirect, to cause the
+      direction or management of such entity, whether by contract or
+      otherwise, or (ii) ownership of fifty percent (50%) or more of the
+      outstanding shares, or (iii) beneficial ownership of such entity.
+
+      "You" (or "Your") shall mean an individual or Legal Entity
+      exercising permissions granted by this License.
+
+      "Source" form shall mean the preferred form for making modifications,
+      including but not limited to software source code, documentation
+      source, and configuration files.
+
+      "Object" form shall mean any form resulting from mechanical
+      transformation or translation of a Source form, including but
+      not limited to compiled object code, generated documentation,
+      and conversions to other media types.
+
+      "Work" shall mean the work of authorship, whether in Source or
+      Object form, made available under the License, as indicated by a
+      copyright notice that is included in or attached to the work
+      (an example is provided in the Appendix below).
+
+      "Derivative Works" shall mean any work, whether in Source or Object
+      form, that is based on (or derived from) the Work and for which the
+      editorial revisions, annotations, elaborations, or other modifications
+      represent, as a whole, an original work of authorship. For the purposes
+      of this License, Derivative Works shall not include works that remain
+      separable from, or merely link (or bind by name) to the interfaces of,
+      the Work and Derivative Works thereof.
+
+      "Contribution" shall mean any work of authorship, including
+      the original version of the Work and any modifications or additions
+      to that Work or Derivative Works thereof, that is intentionally
+      submitted to Licensor for inclusion in the Work by the copyright owner
+      or by an individual or Legal Entity authorized to submit on behalf of
+      the copyright owner. For the purposes of this definition, "submitted"
+      means any form of electronic, verbal, or written communication sent
+      to the Licensor or its representatives, including but not limited to
+      communication on electronic mailing lists, source code control systems,
+      and issue tracking systems that are managed by, or on behalf of, the
+      Licensor for the purpose of discussing and improving the Work, but
+      excluding communication that is conspicuously marked or otherwise
+      designated in writing by the copyright owner as "Not a Contribution."
+
+      "Contributor" shall mean Licensor and any individual or Legal Entity
+      on behalf of whom a Contribution has been received by Licensor and
+      subsequently incorporated within the Work.
+
+   2. Grant of Copyright License. Subject to the terms and conditions of
+      this License, each Contributor hereby grants to You a perpetual,
+      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
+      copyright license to reproduce, prepare Derivative Works of,
+      publicly display, publicly perform, sublicense, and distribute the
+      Work and such Derivative Works in Source or Object form.
+
+   3. Grant of Patent License. Subject to the terms and conditions of
+      this License, each Contributor hereby grants to You a perpetual,
+      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
+      (except as stated in this section) patent license to make, have made,
+      use, offer to sell, sell, import, and otherwise transfer the Work,
+      where such license applies only to those patent claims licensable
+      by such Contributor that are necessarily infringed by their
+      Contribution(s) alone or by combination of their Contribution(s)
+      with the Work to which such Contribution(s) was submitted. If You
+      institute patent litigation against any entity (including a
+      cross-claim or counterclaim in a lawsuit) alleging that the Work
+      or a Contribution incorporated within the Work constitutes direct
+      or contributory patent infringement, then any patent licenses
+      granted to You under this License for that Work shall terminate
+      as of the date such litigation is filed.
+
+   4. Redistribution. You may reproduce and distribute copies of the
+      Work or Derivative Works thereof in any medium, with or without
+      modifications, and in Source or Object form, provided that You
+      meet the following conditions:
+
+      (a) You must give any other recipients of the Work or
+          Derivative Works a copy of this License; and
+
+      (b) You must cause any modified files to carry prominent notices
+          stating that You changed the files; and
+
+      (c) You must retain, in the Source form of any Derivative Works
+          that You distribute, all copyright, patent, trademark, and
+          attribution notices from the Source form of the Work,
+          excluding those notices that do not pertain to any part of
+          the Derivative Works; and
+
+      (d) If the Work includes a "NOTICE" text file as part of its
+          distribution, then any Derivative Works that You distribute must
+          include a readable copy of the attribution notices contained
+          within such NOTICE file, excluding those notices that do not
+          pertain to any part of the Derivative Works, in at least one
+          of the following places: within a NOTICE text file distributed
+          as part of the Derivative Works; within the Source form or
+          documentation, if provided along with the Derivative Works; or,
+          within a display generated by the Derivative Works, if and
+          wherever such third-party notices normally appear. The contents
+          of the NOTICE file are for informational purposes only and
+          do not modify the License. You may add Your own attribution
+          notices within Derivative Works that You distribute, alongside
+          or as an addendum to the NOTICE text from the Work, provided
+          that such additional attribution notices cannot be construed
+          as modifying the License.
+
+      You may add Your own copyright statement to Your modifications and
+      may provide additional or different license terms and conditions
+      for use, reproduction, or distribution of Your modifications, or
+      for any such Derivative Works as a whole, provided Your use,
+      reproduction, and distribution of the Work otherwise complies with
+      the conditions stated in this License.
+
+   5. Submission of Contributions. Unless You explicitly state otherwise,
+      any Contribution intentionally submitted for inclusion in the Work
+      by You to the Licensor shall be under the terms and conditions of
+      this License, without any additional terms or conditions.
+      Notwithstanding the above, nothing herein shall supersede or modify
+      the terms of any separate license agreement you may have executed
+      with Licensor regarding such Contributions.
+
+   6. Trademarks. This License does not grant permission to use the trade
+      names, trademarks, service marks, or product names of the Licensor,
+      except as required for reasonable and customary use in describing the
+      origin of the Work and reproducing the content of the NOTICE file.
+
+   7. Disclaimer of Warranty. Unless required by applicable law or
+      agreed to in writing, Licensor provides the Work (and each
+      Contributor provides its Contributions) on an "AS IS" BASIS,
+      WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
+      implied, including, without limitation, any warranties or conditions
+      of TITLE, NON-INFRINGEMENT, MERCHANTABILITY, or FITNESS FOR A
+      PARTICULAR PURPOSE. You are solely responsible for determining the
+      appropriateness of using or redistributing the Work and assume any
+      risks associated with Your exercise of permissions under this License.
+
+   8. Limitation of Liability. In no event and under no legal theory,
+      whether in tort (including negligence), contract, or otherwise,
+      unless required by applicable law (such as deliberate and grossly
+      negligent acts) or agreed to in writing, shall any Contributor be
+      liable to You for damages, including any direct, indirect, special,
+      incidental, or consequential damages of any character arising as a
+      result of this License or out of the use or inability to use the
+      Work (including but not limited to damages for loss of goodwill,
+      work stoppage, computer failure or malfunction, or any and all
+      other commercial damages or losses), even if such Contributor
+      has been advised of the possibility of such damages.
+
+   9. Accepting Warranty or Additional Liability. While redistributing
+      the Work or Derivative Works thereof, You may choose to offer,
+      and charge a fee for, acceptance of support, warranty, indemnity,
+      or other liability obligations and/or rights consistent with this
+      License. However, in accepting such obligations, You may act only
+      on Your own behalf and on Your sole responsibility, not on behalf
+      of any other Contributor, and only if You agree to indemnify,
+      defend, and hold each Contributor harmless for any liability
+      incurred by, or claims asserted against, such Contributor by reason
+      of your accepting any such warranty or additional liability.
+
+   END OF TERMS AND CONDITIONS
+
+   APPENDIX: How to apply the Apache License to your work.
+
+      To apply the Apache License to your work, attach the following
+      boilerplate notice, with the fields enclosed by brackets "[]"
+      replaced with your own identifying information. (Don't include
+      the brackets!)  The text should be enclosed in the appropriate
+      comment syntax for the file format. We also recommend that a
+      file or class name and description of purpose be included on the
+      same "printed page" as the copyright notice for easier
+      identification within third-party archives.
+
+   Copyright [yyyy] [name of copyright owner]
+
+   Licensed under the Apache License, Version 2.0 (the "License");
+   you may not use this file except in compliance with the License.
+   You may obtain a copy of the License at
+
+       http://www.apache.org/licenses/LICENSE-2.0
+
+   Unless required by applicable law or agreed to in writing, software
+   distributed under the License is distributed on an "AS IS" BASIS,
+   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
+   See the License for the specific language governing permissions and
+   limitations under the License.
+
+================================================================================
+Appendix B — .NET runtime third-party notices (dotnet/runtime, release/10.0)
+================================================================================
+
+.NET Runtime uses third-party libraries or other resources that may be
+distributed under licenses different than the .NET Runtime software.
+
+In the event that we accidentally failed to list a required notice, please
+bring it to our attention. Post an issue or email us:
+
+           dotnet@microsoft.com
+
+The attached notices are provided for information only.
+
+License notice for ASP.NET
+-------------------------------
+
+Copyright (c) .NET Foundation. All rights reserved.
+Licensed under the Apache License, Version 2.0.
+
+Available at
+https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt
+
+License notice for Slicing-by-8
+-------------------------------
+
+http://sourceforge.net/projects/slicing-by-8/
+
+Copyright (c) 2004-2006 Intel Corporation - All Rights Reserved
+
+
+This software program is licensed subject to the BSD License,  available at
+http://www.opensource.org/licenses/bsd-license.html.
+
+
+License notice for Unicode data
+-------------------------------
+
+https://www.unicode.org/license.html
+
+Copyright © 1991-2024 Unicode, Inc. All rights reserved.
+Distributed under the Terms of Use in https://www.unicode.org/copyright.html.
+
+Permission is hereby granted, free of charge, to any person obtaining a
+copy of data files and any associated documentation (the "Data Files") or
+software and any associated documentation (the "Software") to deal in the
+Data Files or Software without restriction, including without limitation
+the rights to use, copy, modify, merge, publish, distribute, and/or sell
+copies of the Data Files or Software, and to permit persons to whom the
+Data Files or Software are furnished to do so, provided that either (a)
+this copyright and permission notice appear with all copies of the Data
+Files or Software, or (b) this copyright and permission notice appear in
+associated Documentation.
+
+THE DATA FILES AND SOFTWARE ARE PROVIDED "AS IS", WITHOUT WARRANTY OF ANY
+KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
+MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT OF
+THIRD PARTY RIGHTS.
+
+IN NO EVENT SHALL THE COPYRIGHT HOLDER OR HOLDERS INCLUDED IN THIS NOTICE
+BE LIABLE FOR ANY CLAIM, OR ANY SPECIAL INDIRECT OR CONSEQUENTIAL DAMAGES,
+OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS,
+WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION,
+ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THE DATA
+FILES OR SOFTWARE.
+
+Except as contained in this notice, the name of a copyright holder shall
+not be used in advertising or otherwise to promote the sale, use or other
+dealings in these Data Files or Software without prior written
+authorization of the copyright holder.
+
+License notice for zlib-ng
+-----------------------
+
+https://github.com/zlib-ng/zlib-ng/blob/d54e3769be0c522015b784eca2af258b1c026107/LICENSE.md
+
+(C) 1995-2024 Jean-loup Gailly and Mark Adler
+
+This software is provided 'as-is', without any express or implied
+warranty. In no event will the authors be held liable for any damages
+arising from the use of this software.
+
+Permission is granted to anyone to use this software for any purpose,
+including commercial applications, and to alter it and redistribute it
+freely, subject to the following restrictions:
+
+1. The origin of this software must not be misrepresented; you must not
+   claim that you wrote the original software. If you use this software
+   in a product, an acknowledgment in the product documentation would be
+   appreciated but is not required.
+
+2. Altered source versions must be plainly marked as such, and must not be
+   misrepresented as being the original software.
+
+3. This notice may not be removed or altered from any source distribution.
+
+License notice for opentelemetry-dotnet
+---------------------------------------
+
+https://github.com/open-telemetry/opentelemetry-dotnet/blob/805dd6b4abfa18ef2706d04c30d0ed28dbc2955e/LICENSE.TXT#L1
+
+Apache License
+Version 2.0, January 2004
+http://www.apache.org/licenses/
+
+Copyright The OpenTelemetry Authors
+
+
+License notice for LinuxTracepoints
+-----------------------------------
+
+https://github.com/microsoft/LinuxTracepoints/blob/main/LICENSE
+
+Copyright (c) Microsoft Corporation.
+
+MIT License
+
+Permission is hereby granted, free of charge, to any person obtaining a copy
+of this software and associated documentation files (the "Software"), to deal
+in the Software without restriction, including without limitation the rights
+to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
+copies of the Software, and to permit persons to whom the Software is
+furnished to do so, subject to the following conditions:
+
+The above copyright notice and this permission notice shall be included in all
+copies or substantial portions of the Software.
+
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
+IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
+FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
+AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
+LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
+OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
+SOFTWARE
+
+License notice for Mono
+-------------------------------
+
+http://www.mono-project.com/docs/about-mono/
+
+Copyright (c) .NET Foundation Contributors
+
+MIT License
+
+Permission is hereby granted, free of charge, to any person obtaining a copy
+of this software  and associated documentation files (the Software), to deal
+in the Software without restriction,  including without limitation the rights
+to use, copy, modify, merge, publish, distribute, sublicense,  and/or sell
+copies of the Software, and to permit persons to whom the Software is
+furnished to do so,  subject to the following conditions:
+
+The above copyright notice and this permission notice shall be included in all
+copies or substantial portions of the Software.
+
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
+EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
+MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
+NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
+LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
+OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
+WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
+
+License notice for International Organization for Standardization
+-----------------------------------------------------------------
+
+Portions (C) International Organization for Standardization 1986:
+     Permission to copy in any form is granted for use with
+     conforming SGML systems and applications as defined in
+     ISO 8879, provided this notice is included in all copies.
+
+License notice for Intel
+------------------------
+
+"Copyright (c) 2004-2006 Intel Corporation - All Rights Reserved
+
+Redistribution and use in source and binary forms, with or without
+modification, are permitted provided that the following conditions are met:
+
+1. Redistributions of source code must retain the above copyright notice, this
+list of conditions and the following disclaimer.
+
+2. Redistributions in binary form must reproduce the above copyright notice,
+this list of conditions and the following disclaimer in the documentation
+and/or other materials provided with the distribution.
+
+THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
+AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
+IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
+DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
+FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
+DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
+SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
+CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
+OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
+OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
+
+License notice for Xamarin and Novell
+-------------------------------------
+
+Copyright (c) 2015 Xamarin, Inc (http://www.xamarin.com)
+
+Permission is hereby granted, free of charge, to any person obtaining a copy
+of this software and associated documentation files (the "Software"), to deal
+in the Software without restriction, including without limitation the rights
+to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
+copies of the Software, and to permit persons to whom the Software is
+furnished to do so, subject to the following conditions:
+
+The above copyright notice and this permission notice shall be included in
+all copies or substantial portions of the Software.
+
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
+IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
+FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
+AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
+LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
+OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
+THE SOFTWARE.
+
+Copyright (c) 2011 Novell, Inc (http://www.novell.com)
+
+Permission is hereby granted, free of charge, to any person obtaining a copy
+of this software and associated documentation files (the "Software"), to deal
+in the Software without restriction, including without limitation the rights
+to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
+copies of the Software, and to permit persons to whom the Software is
+furnished to do so, subject to the following conditions:
+
+The above copyright notice and this permission notice shall be included in
+all copies or substantial portions of the Software.
+
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
+IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
+FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
+AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
+LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
+OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
+THE SOFTWARE.
+
+Third party notice for W3C
+--------------------------
+
+"W3C SOFTWARE AND DOCUMENT NOTICE AND LICENSE
+Status: This license takes effect 13 May, 2015.
+This work is being provided by the copyright holders under the following license.
+License
+By obtaining and/or copying this work, you (the licensee) agree that you have read, understood, and will comply with the following terms and conditions.
+Permission to copy, modify, and distribute this work, with or without modification, for any purpose and without fee or royalty is hereby granted, provided that you include the following on ALL copies of the work or portions thereof, including modifications:
+The full text of this NOTICE in a location viewable to users of the redistributed or derivative work.
+Any pre-existing intellectual property disclaimers, notices, or terms and conditions. If none exist, the W3C Software and Document Short Notice should be included.
+Notice of any changes or modifications, through a copyright statement on the new code or document such as "This software or document includes material copied from or derived from [title and URI of the W3C document]. Copyright © [YEAR] W3C® (MIT, ERCIM, Keio, Beihang)."
+Disclaimers
+THIS WORK IS PROVIDED "AS IS," AND COPYRIGHT HOLDERS MAKE NO REPRESENTATIONS OR WARRANTIES, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO, WARRANTIES OF MERCHANTABILITY OR FITNESS FOR ANY PARTICULAR PURPOSE OR THAT THE USE OF THE SOFTWARE OR DOCUMENT WILL NOT INFRINGE ANY THIRD PARTY PATENTS, COPYRIGHTS, TRADEMARKS OR OTHER RIGHTS.
+COPYRIGHT HOLDERS WILL NOT BE LIABLE FOR ANY DIRECT, INDIRECT, SPECIAL OR CONSEQUENTIAL DAMAGES ARISING OUT OF ANY USE OF THE SOFTWARE OR DOCUMENT.
+The name and trademarks of copyright holders may NOT be used in advertising or publicity pertaining to the work without specific, written prior permission. Title to copyright in this work will at all times remain with copyright holders."
+
+License notice for Bit Twiddling Hacks
+--------------------------------------
+
+Bit Twiddling Hacks
+
+By Sean Eron Anderson
+seander@cs.stanford.edu
+
+Individually, the code snippets here are in the public domain (unless otherwise
+noted) — feel free to use them however you please. The aggregate collection and
+descriptions are © 1997-2005 Sean Eron Anderson. The code and descriptions are
+distributed in the hope that they will be useful, but WITHOUT ANY WARRANTY and
+without even the implied warranty of merchantability or fitness for a particular
+purpose.
+
+License notice for Brotli
+--------------------------------------
+
+Copyright (c) 2009, 2010, 2013-2016 by the Brotli Authors.
+
+Permission is hereby granted, free of charge, to any person obtaining a copy
+of this software and associated documentation files (the "Software"), to deal
+in the Software without restriction, including without limitation the rights
+to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
+copies of the Software, and to permit persons to whom the Software is
+furnished to do so, subject to the following conditions:
+
+The above copyright notice and this permission notice shall be included in
+all copies or substantial portions of the Software.
+
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
+IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
+FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
+AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
+LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
+OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
+THE SOFTWARE.
+
+compress_fragment.c:
+Copyright (c) 2011, Google Inc.
+All rights reserved.
+
+Redistribution and use in source and binary forms, with or without
+modification, are permitted provided that the following conditions are
+met:
+
+    * Redistributions of source code must retain the above copyright
+notice, this list of conditions and the following disclaimer.
+    * Redistributions in binary form must reproduce the above
+copyright notice, this list of conditions and the following disclaimer
+in the documentation and/or other materials provided with the
+distribution.
+    * Neither the name of Google Inc. nor the names of its
+contributors may be used to endorse or promote products derived from
+this software without specific prior written permission.
+
+THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
+""AS IS"" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
+LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
+A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
+OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
+SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
+LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
+DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
+THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
+(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
+OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
+
+decode_fuzzer.c:
+Copyright (c) 2015 The Chromium Authors. All rights reserved.
+
+Redistribution and use in source and binary forms, with or without
+modification, are permitted provided that the following conditions are
+met:
+
+   * Redistributions of source code must retain the above copyright
+notice, this list of conditions and the following disclaimer.
+   * Redistributions in binary form must reproduce the above
+copyright notice, this list of conditions and the following disclaimer
+in the documentation and/or other materials provided with the
+distribution.
+   * Neither the name of Google Inc. nor the names of its
+contributors may be used to endorse or promote products derived from
+this software without specific prior written permission.
+
+THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
+""AS IS"" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
+LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
+A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
+OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
+SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
+LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
+DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
+THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
+(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
+OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE."
+
+License notice for Json.NET
+-------------------------------
+
+https://github.com/JamesNK/Newtonsoft.Json/blob/master/LICENSE.md
+
+The MIT License (MIT)
+
+Copyright (c) 2007 James Newton-King
+
+Permission is hereby granted, free of charge, to any person obtaining a copy of
+this software and associated documentation files (the "Software"), to deal in
+the Software without restriction, including without limitation the rights to
+use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
+the Software, and to permit persons to whom the Software is furnished to do so,
+subject to the following conditions:
+
+The above copyright notice and this permission notice shall be included in all
+copies or substantial portions of the Software.
+
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
+IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
+FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
+COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
+IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
+CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
+
+License notice for vectorized base64 encoding / decoding
+--------------------------------------------------------
+
+Copyright (c) 2005-2007, Nick Galbreath
+Copyright (c) 2013-2017, Alfred Klomp
+Copyright (c) 2015-2017, Wojciech Mula
+Copyright (c) 2016-2017, Matthieu Darbois
+All rights reserved.
+
+Redistribution and use in source and binary forms, with or without
+modification, are permitted provided that the following conditions are
+met:
+
+- Redistributions of source code must retain the above copyright notice,
+  this list of conditions and the following disclaimer.
+
+- Redistributions in binary form must reproduce the above copyright
+  notice, this list of conditions and the following disclaimer in the
+  documentation and/or other materials provided with the distribution.
+
+THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS
+IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED
+TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A
+PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
+HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
+SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED
+TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
+PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
+LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
+NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
+SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
+
+License notice for vectorized hex parsing
+--------------------------------------------------------
+
+Copyright (c) 2022, Geoff Langdale
+Copyright (c) 2022, Wojciech Mula
+All rights reserved.
+
+Redistribution and use in source and binary forms, with or without
+modification, are permitted provided that the following conditions are
+met:
+
+- Redistributions of source code must retain the above copyright notice,
+  this list of conditions and the following disclaimer.
+
+- Redistributions in binary form must reproduce the above copyright
+  notice, this list of conditions and the following disclaimer in the
+  documentation and/or other materials provided with the distribution.
+
+THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS
+IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED
+TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A
+PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
+HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
+SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED
+TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
+PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
+LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
+NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
+SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
+
+License notice for RFC 3492
+---------------------------
+
+The punycode implementation is based on the sample code in RFC 3492
+
+Copyright (C) The Internet Society (2003).  All Rights Reserved.
+
+This document and translations of it may be copied and furnished to
+others, and derivative works that comment on or otherwise explain it
+or assist in its implementation may be prepared, copied, published
+and distributed, in whole or in part, without restriction of any
+kind, provided that the above copyright notice and this paragraph are
+included on all such copies and derivative works.  However, this
+document itself may not be modified in any way, such as by removing
+the copyright notice or references to the Internet Society or other
+Internet organizations, except as needed for the purpose of
+developing Internet standards in which case the procedures for
+copyrights defined in the Internet Standards process must be
+followed, or as required to translate it into languages other than
+English.
+
+The limited permissions granted above are perpetual and will not be
+revoked by the Internet Society or its successors or assigns.
+
+This document and the information contained herein is provided on an
+"AS IS" basis and THE INTERNET SOCIETY AND THE INTERNET ENGINEERING
+TASK FORCE DISCLAIMS ALL WARRANTIES, EXPRESS OR IMPLIED, INCLUDING
+BUT NOT LIMITED TO ANY WARRANTY THAT THE USE OF THE INFORMATION
+HEREIN WILL NOT INFRINGE ANY RIGHTS OR ANY IMPLIED WARRANTIES OF
+MERCHANTABILITY OR FITNESS FOR A PARTICULAR PURPOSE.
+
+Copyright(C) The Internet Society 1997. All Rights Reserved.
+
+This document and translations of it may be copied and furnished to others,
+and derivative works that comment on or otherwise explain it or assist in
+its implementation may be prepared, copied, published and distributed, in
+whole or in part, without restriction of any kind, provided that the above
+copyright notice and this paragraph are included on all such copies and
+derivative works.However, this document itself may not be modified in any
+way, such as by removing the copyright notice or references to the Internet
+Society or other Internet organizations, except as needed for the purpose of
+developing Internet standards in which case the procedures for copyrights
+defined in the Internet Standards process must be followed, or as required
+to translate it into languages other than English.
+
+The limited permissions granted above are perpetual and will not be revoked
+by the Internet Society or its successors or assigns.
+
+This document and the information contained herein is provided on an "AS IS"
+basis and THE INTERNET SOCIETY AND THE INTERNET ENGINEERING TASK FORCE
+DISCLAIMS ALL WARRANTIES, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO
+ANY WARRANTY THAT THE USE OF THE INFORMATION HEREIN WILL NOT INFRINGE ANY
+RIGHTS OR ANY IMPLIED WARRANTIES OF MERCHANTABILITY OR FITNESS FOR A
+PARTICULAR PURPOSE.
+
+License notice for Algorithm from RFC 4122 -
+A Universally Unique IDentifier (UUID) URN Namespace
+----------------------------------------------------
+
+Copyright (c) 1990- 1993, 1996 Open Software Foundation, Inc.
+Copyright (c) 1989 by Hewlett-Packard Company, Palo Alto, Ca. &
+Digital Equipment Corporation, Maynard, Mass.
+Copyright (c) 1998 Microsoft.
+To anyone who acknowledges that this file is provided "AS IS"
+without any express or implied warranty: permission to use, copy,
+modify, and distribute this file for any purpose is hereby
+granted without fee, provided that the above copyright notices and
+this notice appears in all source code copies, and that none of
+the names of Open Software Foundation, Inc., Hewlett-Packard
+Company, Microsoft, or Digital Equipment Corporation be used in
+advertising or publicity pertaining to distribution of the software
+without specific, written prior permission. Neither Open Software
+Foundation, Inc., Hewlett-Packard Company, Microsoft, nor Digital
+Equipment Corporation makes any representations about the
+suitability of this software for any purpose."
+
+License notice for The LLVM Compiler Infrastructure
+---------------------------------------------------
+
+Developed by:
+
+    LLVM Team
+
+    University of Illinois at Urbana-Champaign
+
+    http://llvm.org
+
+Permission is hereby granted, free of charge, to any person obtaining a copy of
+this software and associated documentation files (the "Software"), to deal with
+the Software without restriction, including without limitation the rights to
+use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
+of the Software, and to permit persons to whom the Software is furnished to do
+so, subject to the following conditions:
+
+    * Redistributions of source code must retain the above copyright notice,
+      this list of conditions and the following disclaimers.
+
+    * Redistributions in binary form must reproduce the above copyright notice,
+      this list of conditions and the following disclaimers in the
+      documentation and/or other materials provided with the distribution.
+
+    * Neither the names of the LLVM Team, University of Illinois at
+      Urbana-Champaign, nor the names of its contributors may be used to
+      endorse or promote products derived from this Software without specific
+      prior written permission.
+
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
+IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
+FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
+CONTRIBUTORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
+LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
+OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS WITH THE
+SOFTWARE.
+
+License notice for Bob Jenkins
+------------------------------
+
+By Bob Jenkins, 1996.  bob_jenkins@burtleburtle.net.  You may use this
+code any way you wish, private, educational, or commercial.  It's free.
+
+License notice for Greg Parker
+------------------------------
+
+Greg Parker     gparker@cs.stanford.edu     December 2000
+This code is in the public domain and may be copied or modified without
+permission.
+
+License notice for libunwind based code
+----------------------------------------
+
+Permission is hereby granted, free of charge, to any person obtaining
+a copy of this software and associated documentation files (the
+"Software"), to deal in the Software without restriction, including
+without limitation the rights to use, copy, modify, merge, publish,
+distribute, sublicense, and/or sell copies of the Software, and to
+permit persons to whom the Software is furnished to do so, subject to
+the following conditions:
+
+The above copyright notice and this permission notice shall be
+included in all copies or substantial portions of the Software.
+
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
+EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
+MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
+NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
+LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
+OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
+WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
+
+License notice for Printing Floating-Point Numbers (Dragon4)
+------------------------------------------------------------
+
+/******************************************************************************
+  Copyright (c) 2014 Ryan Juckett
+  http://www.ryanjuckett.com/
+
+  This software is provided 'as-is', without any express or implied
+  warranty. In no event will the authors be held liable for any damages
+  arising from the use of this software.
+
+  Permission is granted to anyone to use this software for any purpose,
+  including commercial applications, and to alter it and redistribute it
+  freely, subject to the following restrictions:
+
+  1. The origin of this software must not be misrepresented; you must not
+     claim that you wrote the original software. If you use this software
+     in a product, an acknowledgment in the product documentation would be
+     appreciated but is not required.
+
+  2. Altered source versions must be plainly marked as such, and must not be
+     misrepresented as being the original software.
+
+  3. This notice may not be removed or altered from any source
+     distribution.
+******************************************************************************/
+
+License notice for Printing Floating-point Numbers (Grisu3)
+-----------------------------------------------------------
+
+Copyright 2012 the V8 project authors. All rights reserved.
+Redistribution and use in source and binary forms, with or without
+modification, are permitted provided that the following conditions are
+met:
+
+    * Redistributions of source code must retain the above copyright
+      notice, this list of conditions and the following disclaimer.
+    * Redistributions in binary form must reproduce the above
+      copyright notice, this list of conditions and the following
+      disclaimer in the documentation and/or other materials provided
+      with the distribution.
+    * Neither the name of Google Inc. nor the names of its
+      contributors may be used to endorse or promote products derived
+      from this software without specific prior written permission.
+
+THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
+"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
+LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
+A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
+OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
+SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
+LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
+DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
+THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
+(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
+OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
+
+License notice for xxHash
+-------------------------
+
+xxHash - Extremely Fast Hash algorithm
+Header File
+Copyright (C) 2012-2021 Yann Collet
+
+BSD 2-Clause License (https://www.opensource.org/licenses/bsd-license.php)
+
+Redistribution and use in source and binary forms, with or without
+modification, are permitted provided that the following conditions are
+met:
+
+   * Redistributions of source code must retain the above copyright
+     notice, this list of conditions and the following disclaimer.
+   * Redistributions in binary form must reproduce the above
+     copyright notice, this list of conditions and the following disclaimer
+     in the documentation and/or other materials provided with the
+     distribution.
+
+THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
+"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
+LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
+A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
+OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
+SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
+LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
+DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
+THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
+(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
+OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
+
+You can contact the author at:
+  - xxHash homepage: https://www.xxhash.com
+  - xxHash source repository: https://github.com/Cyan4973/xxHash
+
+License notice for Berkeley SoftFloat Release 3e
+------------------------------------------------
+
+https://github.com/ucb-bar/berkeley-softfloat-3
+https://github.com/ucb-bar/berkeley-softfloat-3/blob/master/COPYING.txt
+
+License for Berkeley SoftFloat Release 3e
+
+John R. Hauser
+2018 January 20
+
+The following applies to the whole of SoftFloat Release 3e as well as to
+each source file individually.
+
+Copyright 2011, 2012, 2013, 2014, 2015, 2016, 2017, 2018 The Regents of the
+University of California.  All rights reserved.
+
+Redistribution and use in source and binary forms, with or without
+modification, are permitted provided that the following conditions are met:
+
+ 1. Redistributions of source code must retain the above copyright notice,
+    this list of conditions, and the following disclaimer.
+
+ 2. Redistributions in binary form must reproduce the above copyright
+    notice, this list of conditions, and the following disclaimer in the
+    documentation and/or other materials provided with the distribution.
+
+ 3. Neither the name of the University nor the names of its contributors
+    may be used to endorse or promote products derived from this software
+    without specific prior written permission.
+
+THIS SOFTWARE IS PROVIDED BY THE REGENTS AND CONTRIBUTORS "AS IS", AND ANY
+EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
+WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE, ARE
+DISCLAIMED.  IN NO EVENT SHALL THE REGENTS OR CONTRIBUTORS BE LIABLE FOR ANY
+DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
+(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
+LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
+ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
+(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
+THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
+
+License notice for xoshiro RNGs
+--------------------------------
+
+Written in 2018 by David Blackman and Sebastiano Vigna (vigna@acm.org)
+
+To the extent possible under law, the author has dedicated all copyright
+and related and neighboring rights to this software to the public domain
+worldwide. This software is distributed without any warranty.
+
+See <http://creativecommons.org/publicdomain/zero/1.0/>.
+
+License for fastmod (https://github.com/lemire/fastmod), ibm-fpgen (https://github.com/nigeltao/parse-number-fxx-test-data) and fastrange (https://github.com/lemire/fastrange)
+--------------------------------------
+
+   Copyright 2018 Daniel Lemire
+
+   Licensed under the Apache License, Version 2.0 (the "License");
+   you may not use this file except in compliance with the License.
+   You may obtain a copy of the License at
+
+       http://www.apache.org/licenses/LICENSE-2.0
+
+   Unless required by applicable law or agreed to in writing, software
+   distributed under the License is distributed on an "AS IS" BASIS,
+   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
+   See the License for the specific language governing permissions and
+   limitations under the License.
+
+License for sse4-strstr (https://github.com/WojciechMula/sse4-strstr)
+--------------------------------------
+
+   Copyright (c) 2008-2016, Wojciech Mula
+   All rights reserved.
+
+   Redistribution and use in source and binary forms, with or without
+   modification, are permitted provided that the following conditions are
+   met:
+
+   1. Redistributions of source code must retain the above copyright
+      notice, this list of conditions and the following disclaimer.
+
+   2. Redistributions in binary form must reproduce the above copyright
+      notice, this list of conditions and the following disclaimer in the
+      documentation and/or other materials provided with the distribution.
+
+   THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS
+   IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED
+   TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A
+   PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
+   HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
+   SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED
+   TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
+   PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
+   LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
+   NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
+   SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
+
+License notice for The C++ REST SDK
+-----------------------------------
+
+C++ REST SDK
+
+The MIT License (MIT)
+
+Copyright (c) Microsoft Corporation
+
+All rights reserved.
+
+Permission is hereby granted, free of charge, to any person obtaining a copy of
+this software and associated documentation files (the "Software"), to deal in
+the Software without restriction, including without limitation the rights to
+use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
+the Software, and to permit persons to whom the Software is furnished to do so,
+subject to the following conditions:
+
+The above copyright notice and this permission notice shall be included in all
+copies or substantial portions of the Software.
+
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
+IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
+FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
+AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
+LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
+OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
+SOFTWARE.
+
+License notice for MessagePack-CSharp
+-------------------------------------
+
+MessagePack for C#
+
+MIT License
+
+Copyright (c) 2017 Yoshifumi Kawai
+
+Permission is hereby granted, free of charge, to any person obtaining a copy
+of this software and associated documentation files (the "Software"), to deal
+in the Software without restriction, including without limitation the rights
+to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
+copies of the Software, and to permit persons to whom the Software is
+furnished to do so, subject to the following conditions:
+
+The above copyright notice and this permission notice shall be included in all
+copies or substantial portions of the Software.
+
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
+IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
+FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
+AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
+LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
+OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
+SOFTWARE.
+
+License notice for lz4net
+-------------------------------------
+
+lz4net
+
+Copyright (c) 2013-2017, Milosz Krajewski
+
+All rights reserved.
+
+Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:
+
+Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
+
+Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.
+
+THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
+
+License notice for Nerdbank.Streams
+-----------------------------------
+
+The MIT License (MIT)
+
+Copyright (c) Andrew Arnott
+
+Permission is hereby granted, free of charge, to any person obtaining a copy
+of this software and associated documentation files (the "Software"), to deal
+in the Software without restriction, including without limitation the rights
+to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
+copies of the Software, and to permit persons to whom the Software is
+furnished to do so, subject to the following conditions:
+
+The above copyright notice and this permission notice shall be included in all
+copies or substantial portions of the Software.
+
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
+IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
+FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
+AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
+LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
+OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
+SOFTWARE.
+
+License notice for RapidJSON
+----------------------------
+
+Tencent is pleased to support the open source community by making RapidJSON available.
+
+Copyright (C) 2015 THL A29 Limited, a Tencent company, and Milo Yip. All rights reserved.
+
+Licensed under the MIT License (the "License"); you may not use this file except
+in compliance with the License. You may obtain a copy of the License at
+
+http://opensource.org/licenses/MIT
+
+Unless required by applicable law or agreed to in writing, software distributed
+under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR
+CONDITIONS OF ANY KIND, either express or implied. See the License for the
+specific language governing permissions and limitations under the License.
+
+License notice for DirectX Math Library
+---------------------------------------
+
+https://github.com/microsoft/DirectXMath/blob/master/LICENSE
+
+                               The MIT License (MIT)
+
+Copyright (c) 2011-2020 Microsoft Corp
+
+Permission is hereby granted, free of charge, to any person obtaining a copy of this
+software and associated documentation files (the "Software"), to deal in the Software
+without restriction, including without limitation the rights to use, copy, modify,
+merge, publish, distribute, sublicense, and/or sell copies of the Software, and to
+permit persons to whom the Software is furnished to do so, subject to the following
+conditions:
+
+The above copyright notice and this permission notice shall be included in all copies
+or substantial portions of the Software.
+
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
+INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
+PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
+HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF
+CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE
+OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
+
+License notice for ldap4net
+---------------------------
+
+The MIT License (MIT)
+
+Copyright (c) 2018 Alexander Chermyanin
+
+Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
+
+The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
+
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
+
+License notice for vectorized sorting code
+------------------------------------------
+
+MIT License
+
+Copyright (c) 2020 Dan Shechter
+
+Permission is hereby granted, free of charge, to any person obtaining a copy
+of this software and associated documentation files (the "Software"), to deal
+in the Software without restriction, including without limitation the rights
+to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
+copies of the Software, and to permit persons to whom the Software is
+furnished to do so, subject to the following conditions:
+
+The above copyright notice and this permission notice shall be included in all
+copies or substantial portions of the Software.
+
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
+IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
+FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
+AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
+LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
+OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
+SOFTWARE.
+
+License notice for musl
+-----------------------
+
+musl as a whole is licensed under the following standard MIT license:
+
+Copyright © 2005-2020 Rich Felker, et al.
+
+Permission is hereby granted, free of charge, to any person obtaining
+a copy of this software and associated documentation files (the
+"Software"), to deal in the Software without restriction, including
+without limitation the rights to use, copy, modify, merge, publish,
+distribute, sublicense, and/or sell copies of the Software, and to
+permit persons to whom the Software is furnished to do so, subject to
+the following conditions:
+
+The above copyright notice and this permission notice shall be
+included in all copies or substantial portions of the Software.
+
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
+EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
+MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
+IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
+CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
+TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
+SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
+
+
+License notice for "Faster Unsigned Division by Constants"
+------------------------------
+
+Reference implementations of computing and using the "magic number" approach to dividing
+by constants, including codegen instructions. The unsigned division incorporates the
+"round down" optimization per ridiculous_fish.
+
+This is free and unencumbered software. Any copyright is dedicated to the Public Domain.
+
+
+License notice for mimalloc
+-----------------------------------
+
+MIT License
+
+Copyright (c) 2019 Microsoft Corporation, Daan Leijen
+
+Permission is hereby granted, free of charge, to any person obtaining a copy
+of this software and associated documentation files (the "Software"), to deal
+in the Software without restriction, including without limitation the rights
+to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
+copies of the Software, and to permit persons to whom the Software is
+furnished to do so, subject to the following conditions:
+
+The above copyright notice and this permission notice shall be included in all
+copies or substantial portions of the Software.
+
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
+IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
+FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
+AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
+LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
+OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
+SOFTWARE.
+
+License for remote stack unwind (https://github.com/llvm/llvm-project/blob/main/lldb/source/Symbol/CompactUnwindInfo.cpp)
+--------------------------------------
+
+Copyright 2019 LLVM Project
+
+Licensed under the Apache License, Version 2.0 (the "License") with LLVM Exceptions;
+you may not use this file except in compliance with the License.
+You may obtain a copy of the License at
+
+https://llvm.org/LICENSE.txt
+
+Unless required by applicable law or agreed to in writing, software
+distributed under the License is distributed on an "AS IS" BASIS,
+WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
+See the License for the specific language governing permissions and
+limitations under the License.
+
+License notice for Apple header files
+-------------------------------------
+
+Copyright (c) 1980, 1986, 1993
+   The Regents of the University of California.  All rights reserved.
+
+Redistribution and use in source and binary forms, with or without
+modification, are permitted provided that the following conditions
+are met:
+1. Redistributions of source code must retain the above copyright
+   notice, this list of conditions and the following disclaimer.
+2. Redistributions in binary form must reproduce the above copyright
+   notice, this list of conditions and the following disclaimer in the
+   documentation and/or other materials provided with the distribution.
+3. All advertising materials mentioning features or use of this software
+   must display the following acknowledgement:
+   This product includes software developed by the University of
+   California, Berkeley and its contributors.
+4. Neither the name of the University nor the names of its contributors
+   may be used to endorse or promote products derived from this software
+   without specific prior written permission.
+
+THIS SOFTWARE IS PROVIDED BY THE REGENTS AND CONTRIBUTORS ``AS IS'' AND
+ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
+IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
+ARE DISCLAIMED.  IN NO EVENT SHALL THE REGENTS OR CONTRIBUTORS BE LIABLE
+FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
+DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS
+OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
+HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
+LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY
+OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF
+SUCH DAMAGE.
+
+License notice for JavaScript queues
+-------------------------------------
+
+CREATIVE COMMONS CORPORATION IS NOT A LAW FIRM AND DOES NOT PROVIDE LEGAL SERVICES. DISTRIBUTION OF THIS DOCUMENT DOES NOT CREATE AN ATTORNEY-CLIENT RELATIONSHIP. CREATIVE COMMONS PROVIDES THIS INFORMATION ON AN "AS-IS" BASIS. CREATIVE COMMONS MAKES NO WARRANTIES REGARDING THE USE OF THIS DOCUMENT OR THE INFORMATION OR WORKS PROVIDED HEREUNDER, AND DISCLAIMS LIABILITY FOR DAMAGES RESULTING FROM THE USE OF THIS DOCUMENT OR THE INFORMATION OR WORKS PROVIDED HEREUNDER.
+
+Statement of Purpose
+The laws of most jurisdictions throughout the world automatically confer exclusive Copyright and Related Rights (defined below) upon the creator and subsequent owner(s) (each and all, an "owner") of an original work of authorship and/or a database (each, a "Work").
+Certain owners wish to permanently relinquish those rights to a Work for the purpose of contributing to a commons of creative, cultural and scientific works ("Commons") that the public can reliably and without fear of later claims of infringement build upon, modify, incorporate in other works, reuse and redistribute as freely as possible in any form whatsoever and for any purposes, including without limitation commercial purposes. These owners may contribute to the Commons to promote the ideal of a free culture and the further production of creative, cultural and scientific works, or to gain reputation or greater distribution for their Work in part through the use and efforts of others.
+For these and/or other purposes and motivations, and without any expectation of additional consideration or compensation, the person associating CC0 with a Work (the "Affirmer"), to the extent that he or she is an owner of Copyright and Related Rights in the Work, voluntarily elects to apply CC0 to the Work and publicly distribute the Work under its terms, with knowledge of his or her Copyright and Related Rights in the Work and the meaning and intended legal effect of CC0 on those rights.
+
+1. Copyright and Related Rights. A Work made available under CC0 may be protected by copyright and related or neighboring rights ("Copyright and Related Rights"). Copyright and Related Rights include, but are not limited to, the following:
+the right to reproduce, adapt, distribute, perform, display, communicate, and translate a Work;
+moral rights retained by the original author(s) and/or performer(s);
+publicity and privacy rights pertaining to a person's image or likeness depicted in a Work;
+rights protecting against unfair competition in regards to a Work, subject to the limitations in paragraph 4(a), below;
+rights protecting the extraction, dissemination, use and reuse of data in a Work;
+database rights (such as those arising under Directive 96/9/EC of the European Parliament and of the Council of 11 March 1996 on the legal protection of databases, and under any national implementation thereof, including any amended or successor version of such directive); and
+other similar, equivalent or corresponding rights throughout the world based on applicable law or treaty, and any national implementations thereof.
+2. Waiver. To the greatest extent permitted by, but not in contravention of, applicable law, Affirmer hereby overtly, fully, permanently, irrevocably and unconditionally waives, abandons, and surrenders all of Affirmer's Copyright and Related Rights and associated claims and causes of action, whether now known or unknown (including existing as well as future claims and causes of action), in the Work (i) in all territories worldwide, (ii) for the maximum duration provided by applicable law or treaty (including future time extensions), (iii) in any current or future medium and for any number of copies, and (iv) for any purpose whatsoever, including without limitation commercial, advertising or promotional purposes (the "Waiver"). Affirmer makes the Waiver for the benefit of each member of the public at large and to the detriment of Affirmer's heirs and successors, fully intending that such Waiver shall not be subject to revocation, rescission, cancellation, termination, or any other legal or equitable action to disrupt the quiet enjoyment of the Work by the public as contemplated by Affirmer's express Statement of Purpose.
+3. Public License Fallback. Should any part of the Waiver for any reason be judged legally invalid or ineffective under applicable law, then the Waiver shall be preserved to the maximum extent permitted taking into account Affirmer's express Statement of Purpose. In addition, to the extent the Waiver is so judged Affirmer hereby grants to each affected person a royalty-free, non transferable, non sublicensable, non exclusive, irrevocable and unconditional license to exercise Affirmer's Copyright and Related Rights in the Work (i) in all territories worldwide, (ii) for the maximum duration provided by applicable law or treaty (including future time extensions), (iii) in any current or future medium and for any number of copies, and (iv) for any purpose whatsoever, including without limitation commercial, advertising or promotional purposes (the "License"). The License shall be deemed effective as of the date CC0 was applied by Affirmer to the Work. Should any part of the License for any reason be judged legally invalid or ineffective under applicable law, such partial invalidity or ineffectiveness shall not invalidate the remainder of the License, and in such case Affirmer hereby affirms that he or she will not (i) exercise any of his or her remaining Copyright and Related Rights in the Work or (ii) assert any associated claims and causes of action with respect to the Work, in either case contrary to Affirmer's express Statement of Purpose.
+4. Limitations and Disclaimers.
+a. No trademark or patent rights held by Affirmer are waived, abandoned, surrendered, licensed or otherwise affected by this document.
+b. Affirmer offers the Work as-is and makes no representations or warranties of any kind concerning the Work, express, implied, statutory or otherwise, including without limitation warranties of title, merchantability, fitness for a particular purpose, non infringement, or the absence of latent or other defects, accuracy, or the present or absence of errors, whether or not discoverable, all to the greatest extent permissible under applicable law.
+c. Affirmer disclaims responsibility for clearing rights of other persons that may apply to the Work or any use thereof, including without limitation any person's Copyright and Related Rights in the Work. Further, Affirmer disclaims responsibility for obtaining any necessary consents, permissions or other rights required for any use of the Work.
+d. Affirmer understands and acknowledges that Creative Commons is not a party to this document and has no duty or obligation with respect to this CC0 or use of the Work.
+
+
+License notice for FastFloat algorithm
+-------------------------------------
+MIT License
+Copyright (c) 2021 csFastFloat authors
+Permission is hereby granted, free of charge, to any person obtaining a copy
+of this software and associated documentation files (the "Software"), to deal
+in the Software without restriction, including without limitation the rights
+to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
+copies of the Software, and to permit persons to whom the Software is
+furnished to do so, subject to the following conditions:
+The above copyright notice and this permission notice shall be included in all
+copies or substantial portions of the Software.
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
+IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
+FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
+AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
+LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
+OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
+SOFTWARE.
+
+License notice for MsQuic
+--------------------------------------
+
+Copyright (c) Microsoft Corporation.
+Licensed under the MIT License.
+
+Available at
+https://github.com/microsoft/msquic/blob/main/LICENSE
+
+License notice for m-ou-se/floatconv
+-------------------------------
+
+Copyright (c) 2020 Mara Bos <m-ou.se@m-ou.se>
+All rights reserved.
+
+Redistribution and use in source and binary forms, with or without
+modification, are permitted provided that the following conditions are met:
+
+1. Redistributions of source code must retain the above copyright notice, this
+   list of conditions and the following disclaimer.
+2. Redistributions in binary form must reproduce the above copyright notice,
+   this list of conditions and the following disclaimer in the documentation
+   and/or other materials provided with the distribution.
+
+THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
+ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
+WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
+DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
+ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
+(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
+LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
+ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
+(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
+SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
+
+License notice for code from The Practice of Programming
+-------------------------------
+
+Copyright (C) 1999 Lucent Technologies
+
+Excerpted from 'The Practice of Programming
+by Brian W. Kernighan and Rob Pike
+
+You may use this code for any purpose, as long as you leave the copyright notice and book citation attached.
+
+Notice for Euclidean Affine Functions and Applications to Calendar
+Algorithms
+-------------------------------
+
+Aspects of Date/Time processing based on algorithm described in "Euclidean Affine Functions and Applications to Calendar
+Algorithms", Cassio Neri and Lorenz Schneider. https://arxiv.org/pdf/2102.06959.pdf
+
+License notice for amd/aocl-libm-ose
+-------------------------------
+
+Copyright (C) 2008-2020 Advanced Micro Devices, Inc. All rights reserved.
+
+Redistribution and use in source and binary forms, with or without modification,
+are permitted provided that the following conditions are met:
+1. Redistributions of source code must retain the above copyright notice,
+   this list of conditions and the following disclaimer.
+2. Redistributions in binary form must reproduce the above copyright notice,
+   this list of conditions and the following disclaimer in the documentation
+   and/or other materials provided with the distribution.
+3. Neither the name of the copyright holder nor the names of its contributors
+   may be used to endorse or promote products derived from this software without
+   specific prior written permission.
+
+THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
+ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
+WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.
+IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT,
+INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING,
+BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA,
+OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY,
+WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
+ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
+POSSIBILITY OF SUCH DAMAGE.
+
+License notice for fmtlib/fmt
+-------------------------------
+
+Formatting library for C++
+
+Copyright (c) 2012 - present, Victor Zverovich
+
+Permission is hereby granted, free of charge, to any person obtaining
+a copy of this software and associated documentation files (the
+"Software"), to deal in the Software without restriction, including
+without limitation the rights to use, copy, modify, merge, publish,
+distribute, sublicense, and/or sell copies of the Software, and to
+permit persons to whom the Software is furnished to do so, subject to
+the following conditions:
+
+The above copyright notice and this permission notice shall be
+included in all copies or substantial portions of the Software.
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
+EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
+MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
+NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
+LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
+OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
+WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
+
+License for Jb Evain
+---------------------
+
+Copyright (c) 2006 Jb Evain (jbevain@gmail.com)
+
+Permission is hereby granted, free of charge, to any person obtaining a copy
+of this software and associated documentation files (the "Software"), to deal
+in the Software without restriction, including without limitation the rights
+to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
+copies of the Software, and to permit persons to whom the Software is
+furnished to do so, subject to the following conditions:
+
+The above copyright notice and this permission notice shall be included
+in all copies or substantial portions of the Software.
+
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
+IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
+FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
+AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
+LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
+OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
+THE SOFTWARE.
+
+--- Optional exception to the license ---
+
+As an exception, if, as a result of your compiling your source code, portions
+of this Software are embedded into a machine-executable object form of such
+source code, you may redistribute such embedded portions in such object form
+without including the above copyright and permission notices.
+
+
+License for MurmurHash3
+--------------------------------------
+
+https://github.com/aappleby/smhasher/blob/master/src/MurmurHash3.cpp
+
+MurmurHash3 was written by Austin Appleby, and is placed in the public
+domain. The author hereby disclaims copyright to this source
+
+License for Fast CRC Computation
+--------------------------------------
+
+https://github.com/intel/isa-l/blob/33a2d9484595c2d6516c920ce39a694c144ddf69/crc/crc32_ieee_by4.asm
+https://github.com/intel/isa-l/blob/33a2d9484595c2d6516c920ce39a694c144ddf69/crc/crc64_ecma_norm_by8.asm
+
+Copyright(c) 2011-2015 Intel Corporation All rights reserved.
+
+Redistribution and use in source and binary forms, with or without
+modification, are permitted provided that the following conditions
+are met:
+  * Redistributions of source code must retain the above copyright
+    notice, this list of conditions and the following disclaimer.
+  * Redistributions in binary form must reproduce the above copyright
+    notice, this list of conditions and the following disclaimer in
+    the documentation and/or other materials provided with the
+    distribution.
+  * Neither the name of Intel Corporation nor the names of its
+    contributors may be used to endorse or promote products derived
+    from this software without specific prior written permission.
+
+THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
+"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
+LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
+A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
+OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
+SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
+LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
+DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
+THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
+(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
+OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
+
+License for C# Implementation of Fast CRC Computation
+-----------------------------------------------------
+
+https://github.com/SixLabors/ImageSharp/blob/f4f689ce67ecbcc35cebddba5aacb603e6d1068a/src/ImageSharp/Formats/Png/Zlib/Crc32.cs
+
+Copyright (c) Six Labors.
+Licensed under the Apache License, Version 2.0.
+
+Available at
+https://github.com/SixLabors/ImageSharp/blob/f4f689ce67ecbcc35cebddba5aacb603e6d1068a/LICENSE
+
+License for the Teddy multi-substring searching implementation
+--------------------------------------
+
+https://github.com/BurntSushi/aho-corasick
+
+The MIT License (MIT)
+
+Copyright (c) 2015 Andrew Gallant
+
+Permission is hereby granted, free of charge, to any person obtaining a copy
+of this software and associated documentation files (the "Software"), to deal
+in the Software without restriction, including without limitation the rights
+to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
+copies of the Software, and to permit persons to whom the Software is
+furnished to do so, subject to the following conditions:
+
+The above copyright notice and this permission notice shall be included in
+all copies or substantial portions of the Software.
+
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
+IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
+FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
+AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
+LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
+OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
+THE SOFTWARE.
+
+License notice for Avx512Vbmi base64 encoding / decoding
+--------------------------------------------------------
+
+Copyright (c) 2015-2018, Wojciech Muła
+All rights reserved.
+
+Redistribution and use in source and binary forms, with or without
+modification, are permitted provided that the following conditions are
+met:
+
+1. Redistributions of source code must retain the above copyright
+   notice, this list of conditions and the following disclaimer.
+
+2. Redistributions in binary form must reproduce the above copyright
+   notice, this list of conditions and the following disclaimer in the
+   documentation and/or other materials provided with the distribution.
+
+THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS
+IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED
+TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A
+PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
+HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
+SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED
+TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
+PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
+LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
+NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
+SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
+
+--------------------------------------------------------
+
+Aspects of base64 encoding / decoding are based on algorithm described in "Base64 encoding and decoding at almost the speed of a memory
+copy", Wojciech Muła and Daniel Lemire. https://arxiv.org/pdf/1910.05109.pdf
+
+License for FormatJS Intl.Segmenter grapheme segmentation algorithm
+--------------------------------------------------------------------------
+Available at https://github.com/formatjs/formatjs/blob/58d6a7b398d776ca3d2726d72ae1573b65cc3bef/packages/intl-segmenter/LICENSE.md
+
+MIT License
+
+Copyright (c) 2022 FormatJS
+
+Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
+
+The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
+
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
+
+License for SharpFuzz and related samples
+--------------------------------------
+
+https://github.com/Metalnem/sharpfuzz
+https://github.com/Metalnem/dotnet-fuzzers
+https://github.com/Metalnem/libfuzzer-dotnet
+
+MIT License
+
+Copyright (c) 2018 Nemanja Mijailovic
+
+Permission is hereby granted, free of charge, to any person obtaining a copy
+of this software and associated documentation files (the "Software"), to deal
+in the Software without restriction, including without limitation the rights
+to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
+copies of the Software, and to permit persons to whom the Software is
+furnished to do so, subject to the following conditions:
+
+The above copyright notice and this permission notice shall be included in all
+copies or substantial portions of the Software.
+
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
+IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
+FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
+AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
+LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
+OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
+SOFTWARE.
+
+License for National Institute of Standards and Technology ACVP Data
+--------------------------------------------------------------------
+Available at https://github.com/usnistgov/ACVP-Server/blob/85f8742965b2691862079172982683757d8d91db/README.md#License
+
+NIST-developed software is provided by NIST as a public service. You may use, copy, and distribute copies of the software in any medium, provided that you keep intact this entire notice. You may improve, modify, and create derivative works of the software or any portion of the software, and you may copy and distribute such modifications or works. Modified works should carry a notice stating that you changed the software and should note the date and nature of any such change. Please explicitly acknowledge the National Institute of Standards and Technology as the source of the software.
+
+NIST-developed software is expressly provided "AS IS." NIST MAKES NO WARRANTY OF ANY KIND, EXPRESS, IMPLIED, IN FACT, OR ARISING BY OPERATION OF LAW, INCLUDING, WITHOUT LIMITATION, THE IMPLIED WARRANTY OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, NON-INFRINGEMENT, AND DATA ACCURACY. NIST NEITHER REPRESENTS NOR WARRANTS THAT THE OPERATION OF THE SOFTWARE WILL BE UNINTERRUPTED OR ERROR-FREE, OR THAT ANY DEFECTS WILL BE CORRECTED. NIST DOES NOT WARRANT OR MAKE ANY REPRESENTATIONS REGARDING THE USE OF THE SOFTWARE OR THE RESULTS THEREOF, INCLUDING BUT NOT LIMITED TO THE CORRECTNESS, ACCURACY, RELIABILITY, OR USEFULNESS OF THE SOFTWARE.
+
+You are solely responsible for determining the appropriateness of using and distributing the software and you assume all risks associated with its use, including but not limited to the risks and costs of program errors, compliance with applicable laws, damage to or loss of data, programs or equipment, and the unavailability or interruption of operation. This software is not intended to be used in any situation where a failure could cause risk of injury or damage to property. The software developed by NIST employees is not subject to copyright protection within the United States.
+
+
+================================================================================
+Appendix C — WPF third-party notices (dotnet/wpf, release/10.0)
+================================================================================
+
+.NET Core uses third-party libraries or other resources that may be
+distributed under licenses different than the .NET Core software.
+
+In the event that we accidentally failed to list a required notice, please
+bring it to our attention. Post an issue or email us:
+
+           dotnet@microsoft.com
+
+The attached notices are provided for information only.
+
+License notice for Zlib 
+-----------------------
+
+https://github.com/madler/zlib
+http://zlib.net/zlib_license.html
+
+/* zlib.h -- interface of the 'zlib' general purpose compression library
+  version 1.2.11, January 15th, 2017
+
+  Copyright (C) 1995-2017 Jean-loup Gailly and Mark Adler
+
+  This software is provided 'as-is', without any express or implied
+  warranty.  In no event will the authors be held liable for any damages
+  arising from the use of this software.
+
+  Permission is granted to anyone to use this software for any purpose,
+  including commercial applications, and to alter it and redistribute it
+  freely, subject to the following restrictions:
+
+  1. The origin of this software must not be misrepresented; you must not
+     claim that you wrote the original software. If you use this software
+     in a product, an acknowledgment in the product documentation would be
+     appreciated but is not required.
+  2. Altered source versions must be plainly marked as such, and must not be
+     misrepresented as being the original software.
+  3. This notice may not be removed or altered from any source distribution.
+  
+  License notice for Json.NET
+-------------------------------
+
+https://github.com/JamesNK/Newtonsoft.Json/blob/master/LICENSE.md
+
+The MIT License (MIT)
+
+Copyright (c) 2007 James Newton-King
+
+Permission is hereby granted, free of charge, to any person obtaining a copy of
+this software and associated documentation files (the "Software"), to deal in
+the Software without restriction, including without limitation the rights to
+use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
+the Software, and to permit persons to whom the Software is furnished to do so,
+subject to the following conditions:
+
+The above copyright notice and this permission notice shall be included in all
+copies or substantial portions of the Software.
+
+THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
+IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
+FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
+COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
+IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
+CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
diff --git a/docs/GUIDE.md b/docs/GUIDE.md
index 970e7ae..282d25a 100644
--- a/docs/GUIDE.md
+++ b/docs/GUIDE.md
@@ -11,7 +11,7 @@ Almost everything starts from two places:
 
 ![A desktop with NeoFences: Games, Apps, Downloads and a Desk fence with widgets](guide/desktop.jpg)
 
-New here? Start with [First start](#first-start).
+New here? Start with [Install](#install) and [First start](#first-start).
 
 ## Contents
 
@@ -31,6 +31,25 @@ New here? Start with [First start](#first-start).
 14. [Troubleshooting](#14-troubleshooting)
 15. [Cheat-sheet](#15-cheat-sheet)
 
+## Install
+
+NeoFences runs on **Windows 11** (64-bit) and is free (see the [licence](../LICENSE.txt)).
+
+1. Download **NeoFences.App-win-Setup.exe** from the
+   [latest release](https://github.com/CipherSnowden/neo-fences/releases/latest).
+2. Run it. Windows SmartScreen may say **"Windows protected your PC"**: NeoFences is not code-signed (a certificate costs
+   money every year), and Windows warns about new unsigned apps until enough people have run them. Click **More info**,
+   then **Run anyway**. NeoFences installs for your account only (no administrator rights), starts, and from then on
+   keeps itself up to date.
+3. If Windows says **Smart App Control** blocked NeoFences: that Windows 11 feature lets only signed or well-known apps
+   run, and it has no "Run anyway". Turning it off is your decision — read
+   [Microsoft's Smart App Control FAQ](https://support.microsoft.com/en-us/windows/security/threat-malware-protection/smart-app-control-frequently-asked-questions)
+   first.
+
+Want to be sure the file is the real one? See "Check your download" in the
+[README](https://github.com/CipherSnowden/neo-fences#check-your-download). To uninstall: Windows Settings → Apps →
+Installed apps → NeoFences → Uninstall (your desktop icons come back; your files are never touched).
+
 ## First start
 
 After installing, NeoFences starts with one fence: the **welcome**. It says what NeoFences is and offers three buttons:
@@ -282,7 +301,8 @@ Tray or fence menu → **Settings…**. A list of sections on the left opens one
   **Colour fences from the wallpaper**, **Title font**. A fence can have its own of each (fence menu → **Fence settings…**).
 - **Games**, **Game mode**, **Snapshots**, **Updates** — see their sections above. **Snapshots** also has **Export setup…** and
   **Import setup…**.
-- **About** — the version, **Open logs folder**, **Open data folder**, **Help (online guide)**, and **Reset settings
+- **About** — the version, **Open logs folder**, **Open data folder**, **Help (online guide)**, **Licence and notices**
+  (the licence; the third-party notices and the privacy note are next to it), and **Reset settings
   to defaults…**: every page of Settings back to how NeoFences comes (asked first, a snapshot "Before reset" first). Your
   fences, items, own presets, game folders, hidden games and chosen covers stay.
 
@@ -320,9 +340,11 @@ Tray or fence menu → **Settings…**. A list of sections on the left opens one
 - **"Changes are not saved"**: NeoFences could not read or write its files (another program locking them, or a file from
   a newer version). Your fences work, but changes are lost at exit; restart NeoFences, and see the log.
 - **Something went wrong:** **Settings → About → Open logs folder** and look at the newest file; report problems
-  on the project's [GitHub Issues page](https://github.com/CipherSnowden/neo-fences/issues) with what you did and that
-  log.
-  Logs never contain your Windows user name (the profile folder is written as `%USERPROFILE%`).
+  with the **Bug** form on the project's [GitHub Issues page](https://github.com/CipherSnowden/neo-fences/issues/new/choose):
+  what you did, what happened, and that log if you like.
+  Logs never contain your Windows user name (the profile folder is written as `%USERPROFILE%`), but they name the
+  folders and files your fences point at — look through a log before you attach it. Security problems: privately, see
+  [SECURITY.md](../SECURITY.md).
 - **CPU TEMP shows "—"**: start MSI Afterburner (with "CPU temperature" ticked in its Settings → Monitoring) or HWiNFO64 (in HWiNFO, turn on "Shared Memory Support" in its
   settings; the free version turns it off again after 12 hours).
 
diff --git a/src/NeoFences.App/FenceHost.cs b/src/NeoFences.App/FenceHost.cs
index 0d7517b..cce0fc2 100644
--- a/src/NeoFences.App/FenceHost.cs
+++ b/src/NeoFences.App/FenceHost.cs
@@ -992,6 +992,7 @@ public sealed partial class FenceHost
         window.KeyboardLayoutChanged += RefreshSettings; // M16: key caps follow the new layout
         window.OpenLogsRequested += () => OpenItem(AppPaths.LogsDirectory, ownerHandle: 0);
         window.HelpRequested += () => OpenItem(GuideUrl, ownerHandle: 0); // M29
+        window.LicenceRequested += () => OpenItem(Path.Combine(AppContext.BaseDirectory, "LICENSE.txt"), ownerHandle: 0); // M39
         window.OpenDataRequested += () => OpenItem(AppPaths.DataDirectory, ownerHandle: 0);
         window.Closed += (_, _) =>
         {
diff --git a/src/NeoFences.App/NeoFences.App.csproj b/src/NeoFences.App/NeoFences.App.csproj
index d146609..7dc30af 100644
--- a/src/NeoFences.App/NeoFences.App.csproj
+++ b/src/NeoFences.App/NeoFences.App.csproj
@@ -24,6 +24,11 @@
     <PackageReference Include="Velopack" Version="1.2.161" />
   </ItemGroup>
 
+  <ItemGroup>
+    <!-- M39 (ADR-061): the licence, the third-party notices and the privacy note next to NeoFences.exe in every install. -->
+    <None Include="..\..\LICENSE.txt;..\..\THIRD-PARTY-NOTICES.txt;..\..\PRIVACY.md" Link="%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
+  </ItemGroup>
+
   <ItemGroup>
     <ProjectReference Include="..\NeoFences.Core\NeoFences.Core.csproj" />
     <ProjectReference Include="..\NeoFences.Shell\NeoFences.Shell.csproj" />
diff --git a/src/NeoFences.App/SettingsWindow.xaml b/src/NeoFences.App/SettingsWindow.xaml
index 931f885..73463eb 100644
--- a/src/NeoFences.App/SettingsWindow.xaml
+++ b/src/NeoFences.App/SettingsWindow.xaml
@@ -369,7 +369,9 @@
                         <Button x:Name="OpenLogsButton" Content="Open logs folder" Margin="0,0,8,0" />
                         <Button x:Name="OpenDataButton" Content="Open data folder" Margin="0,0,8,0" />
                         <!-- M29: the guide on GitHub (ADR-050). -->
-                        <Button x:Name="HelpButton" Content="Help (online guide)" />
+                        <Button x:Name="HelpButton" Content="Help (online guide)" Margin="0,0,8,0" />
+                        <!-- M39 (ADR-061): LICENSE.txt next to NeoFences.exe; it names the notices and the privacy note beside it. -->
+                        <Button x:Name="LicenceButton" Content="Licence and notices" />
                     </StackPanel>
                 </StackPanel>
             </Border>
diff --git a/src/NeoFences.App/SettingsWindow.xaml.cs b/src/NeoFences.App/SettingsWindow.xaml.cs
index 1eab9df..a42ee32 100644
--- a/src/NeoFences.App/SettingsWindow.xaml.cs
+++ b/src/NeoFences.App/SettingsWindow.xaml.cs
@@ -48,6 +48,8 @@ public partial class SettingsWindow : Window
     public event Action? OpenDataRequested;
     /// <summary>"Help (online guide)" (M29): the guide on GitHub.</summary>
     public event Action? HelpRequested;
+    /// <summary>"Licence and notices" (M39): LICENSE.txt next to NeoFences.exe.</summary>
+    public event Action? LicenceRequested;
     public event Action? TakeSnapshotRequested;
     public event Action<string>? RestoreSnapshotRequested;
     public event Action<string, string>? RenameSnapshotRequested;
@@ -102,6 +104,7 @@ public partial class SettingsWindow : Window
         OpenLogsButton.Click += (_, _) => OpenLogsRequested?.Invoke();
         OpenDataButton.Click += (_, _) => OpenDataRequested?.Invoke();
         HelpButton.Click += (_, _) => HelpRequested?.Invoke();
+        LicenceButton.Click += (_, _) => LicenceRequested?.Invoke();
         TakeSnapshotButton.Click += (_, _) => TakeSnapshotRequested?.Invoke();
         OpenSnapshotsButton.Click += (_, _) => OpenSnapshotsRequested?.Invoke();
         ExportSetupButton.Click += (_, _) => ExportSetupRequested?.Invoke(); // M36
diff --git a/tools/vm/guest/guest-checks.ps1 b/tools/vm/guest/guest-checks.ps1
index 7a6fdb7..61f7de5 100644
--- a/tools/vm/guest/guest-checks.ps1
+++ b/tools/vm/guest/guest-checks.ps1
@@ -125,6 +125,11 @@ Check 'update' {
   $version = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
   "downloaded: $downloaded; version now: $version; icons hidden: $(IconsHidden)"; $version -like "$($settings.candidate)*" -and (IconsHidden)
 }
+# M39 (ADR-061): the licence, the third-party notices and the privacy note are installed next to NeoFences.exe.
+Check 'trust-files' {
+  $missing = @('LICENSE.txt', 'THIRD-PARTY-NOTICES.txt', 'PRIVACY.md' | Where-Object { -not (Test-Path -LiteralPath (Join-Path $appDir "current\$_")) })
+  "missing: $(if ($missing.Count) { $missing -join ', ' } else { 'none' })"; $missing.Count -eq 0
+}
 # After the update: the keyboard way in is new in the candidate.
 Check 'peek-keyboard' {
   $notepad = Notepad; Keys @(0x11, 0x12, 0x20); Start-Sleep -Seconds 1
```

- [ ] **Step 2: Build and test.** `dotnet build` → `0 Warning(s)`; the three files are in
  `src/NeoFences.App/bin/Debug/net10.0-windows/`; `dotnet test` → `Passed: 878`; the guest script parses in Windows
  PowerShell 5.1 (`[System.Management.Automation.Language.Parser]::ParseFile(...)` → 0 errors) and stays ASCII; the issue
  forms parse as YAML (WSL: `python3 -c "import yaml,sys; yaml.safe_load(open(sys.argv[1]))" <file>` for each).
- [ ] **Step 3: Commit.** `git add -A && git commit -m "feat: added the licence, privacy, security and third-party notices, Licence and notices in Settings, and issue forms"` (ledger the Task 2 calls).

### Task 3: CI — Core on Linux; checksums and attestations on releases

**Files:**
- Modify: `.github/workflows/ci.yml` (job `core-linux`), `.github/workflows/release.yml` (permissions `id-token: write`,
  `attestations: write`; steps "Checksums", "Build attestations", "Upload the checksums to the draft")

**Interfaces:**
- Produces: release asset `SHA256SUMS.txt` (lines `<sha256 lowercase>  <file name>`); attestations for this version's
  `.nupkg` files, Setup.exe and the portable zip.

- [ ] **Step 1: Implement.** Write this patch to `m39-3-ci.patch` and apply it:

```diff
diff --git a/.github/workflows/ci.yml b/.github/workflows/ci.yml
index 6c3067f..60963f7 100644
--- a/.github/workflows/ci.yml
+++ b/.github/workflows/ci.yml
@@ -1,4 +1,5 @@
-# Every push to main and every pull request: build and run all tests on Windows (M17, ADR-039).
+# Every push to main and every pull request: build and run all tests on Windows (M17, ADR-039), and Core's tests on
+# Linux (M39, ADR-061).
 name: CI
 on:
   push:
@@ -16,3 +17,12 @@ jobs:
           dotnet-version: '10.0.x'
       - run: dotnet build NeoFences.slnx -c Release
       - run: dotnet test NeoFences.slnx -c Release --no-build
+  # M39 (ADR-061): Core is pure C# and its tests pass on Linux too (WindowsPath reads stored paths by Windows' rules).
+  core-linux:
+    runs-on: ubuntu-latest
+    steps:
+      - uses: actions/checkout@v5
+      - uses: actions/setup-dotnet@v5
+        with:
+          dotnet-version: '10.0.x'
+      - run: dotnet test tests/NeoFences.Core.Tests -c Release
diff --git a/.github/workflows/release.yml b/.github/workflows/release.yml
index d7484c8..f5c91ab 100644
--- a/.github/workflows/release.yml
+++ b/.github/workflows/release.yml
@@ -6,6 +6,8 @@ on:
     tags: ['v[0-9]+.[0-9]+.[0-9]+'] # stable versions only: a v1.9.0-beta tag would otherwise reach every installed copy (final review M5)
 permissions:
   contents: write # the draft release; the workflow's own token, no stored secret
+  id-token: write # M39 (ADR-061): build attestations, signed by GitHub
+  attestations: write
 jobs:
   release:
     runs-on: windows-latest
@@ -38,6 +40,31 @@ jobs:
         shell: pwsh
         run: ./build/pack.ps1 -Version "${{ steps.version.outputs.version }}" -ReleaseNotes artifacts/release-notes.md
       # Stage 2 (ADR-039): code signing goes here (vpk pack --signParams / --azureTrustedSignFile) once a certificate exists.
+      # M39 (ADR-061): unsigned, so every download can be checked instead — its SHA-256 and GitHub's build attestation.
+      # Only this version's files (artifacts/releases also holds the previous release, downloaded for the delta).
+      - name: Checksums
+        shell: pwsh
+        run: |
+          $version = "${{ steps.version.outputs.version }}"
+          $names = @("NeoFences.App-$version-full.nupkg", "NeoFences.App-$version-delta.nupkg", 'NeoFences.App-win-Setup.exe', 'NeoFences.App-win-Portable.zip', 'releases.win.json', 'RELEASES')
+          $lines = foreach ($name in $names) {
+            $path = Join-Path artifacts/releases $name
+            if (Test-Path -LiteralPath $path) { "$((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant())  $name" }
+          }
+          Set-Content artifacts/SHA256SUMS.txt -Value $lines -Encoding ascii
+          Get-Content artifacts/SHA256SUMS.txt
+      - name: Build attestations
+        uses: actions/attest-build-provenance@v2
+        with:
+          subject-path: |
+            artifacts/releases/NeoFences.App-${{ steps.version.outputs.version }}-*.nupkg
+            artifacts/releases/NeoFences.App-win-Setup.exe
+            artifacts/releases/NeoFences.App-win-Portable.zip
       - name: Upload as a draft release
         shell: pwsh
         run: dotnet vpk upload github --repoUrl "https://github.com/${{ github.repository }}" --token "${{ secrets.GITHUB_TOKEN }}" --outputDir artifacts/releases --tag "${{ github.ref_name }}" --releaseName "NeoFences ${{ github.ref_name }}"
+      - name: Upload the checksums to the draft
+        shell: pwsh
+        env:
+          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
+        run: gh release upload "${{ github.ref_name }}" artifacts/SHA256SUMS.txt --repo "${{ github.repository }}"
```

- [ ] **Step 2: Check.** Both workflows parse as YAML (WSL python as in Task 2). The real proof is Task 7 (CI on push) and
  Task 8 (the 0.26.0 release).
- [ ] **Step 3: Commit.** `git add -A && git commit -m "ci: added Core tests on Linux, and checksums and build attestations to releases"`.

### Task 4: Landing page, README and the release checklist

**Files:**
- Create: `site/index.html`, `site/style.css`, `.github/workflows/pages.yml`, `docs/RELEASING.md`
- Modify: `README.md` (download line, install steps, Check your download, Licence/privacy/security, Linux note)

**Interfaces:**
- Consumes: Task 2's trust files and GUIDE `#install`; Task 3's `SHA256SUMS.txt` and attestations (named in the README).
- Produces: Pages site at `https://ciphersnowden.github.io/neo-fences/` (after Pages is turned on in Task 8); README anchor
  `#check-your-download` (the guide and the page link to it).

- [ ] **Step 1: Implement.** Write this patch to `m39-4-site.patch` and apply it:

```diff
diff --git a/.github/workflows/pages.yml b/.github/workflows/pages.yml
new file mode 100644
index 0000000..27c9c51
--- /dev/null
+++ b/.github/workflows/pages.yml
@@ -0,0 +1,32 @@
+# The landing page (M39, ADR-061): site/ plus the guide's pictures, published to GitHub Pages when either changes on main.
+name: Pages
+on:
+  push:
+    branches: [main]
+    paths: ['site/**', 'docs/guide/**', '.github/workflows/pages.yml']
+  workflow_dispatch:
+permissions:
+  contents: read
+  pages: write
+  id-token: write
+concurrency:
+  group: pages
+  cancel-in-progress: true
+jobs:
+  deploy:
+    runs-on: ubuntu-latest
+    environment:
+      name: github-pages
+      url: ${{ steps.deployment.outputs.page_url }}
+    steps:
+      - uses: actions/checkout@v5
+      - name: Assemble the site
+        run: |
+          mkdir -p _site/img
+          cp -r site/. _site/
+          cp docs/guide/*.jpg docs/guide/*.png _site/img/
+          cp src/NeoFences.App/NeoFences.ico _site/favicon.ico
+      - uses: actions/configure-pages@v5
+      - uses: actions/upload-pages-artifact@v3
+      - id: deployment
+        uses: actions/deploy-pages@v4
diff --git a/README.md b/README.md
index 3f05396..60e746e 100644
--- a/README.md
+++ b/README.md
@@ -7,19 +7,25 @@ play, and it never moves, renames or deletes anything of yours.
 
 ![NeoFences on a desktop: a Games fence with covers, an Apps fence, a Downloads folder panel and widgets](docs/guide/desktop.jpg)
 
+**[Download for Windows 11](https://github.com/CipherSnowden/neo-fences/releases/latest/download/NeoFences.App-win-Setup.exe)**
+· [Website](https://ciphersnowden.github.io/neo-fences/) · [Guide](docs/GUIDE.md) · Free ([licence](LICENSE.txt))
+
 ## Install
 
-1. Open the [Releases](../../releases) page and download `NeoFences.App-win-Setup.exe` from the newest release. If
-   your browser warns that the file "isn't commonly downloaded", keep it (Edge: **…** → **Keep** → **Show more** →
+1. Download `NeoFences.App-win-Setup.exe` from the [latest release](https://github.com/CipherSnowden/neo-fences/releases/latest).
+   If your browser warns that the file "isn't commonly downloaded", keep it (Edge: **…** → **Keep** → **Show more** →
    **Keep anyway**).
 2. Run it. NeoFences installs for your user only (no administrator rights needed) and starts. Its icon sits in the
    notification area (the tray), next to the clock. On Windows 11 it may be behind the **^** arrow there; drag it
    onto the taskbar to keep it in view.
 3. **The first time only**, Windows may show *"Windows protected your PC"*: click **More info → Run anyway**. NeoFences
-   is not code-signed yet, so Windows does not know the publisher.
+   is unsigned, not unsafe: a code-signing certificate costs money every year, so Windows does not know the publisher
+   yet. Instead, every download can be checked — see [Check your download](#check-your-download).
 
-> **Smart App Control:** on PCs where Windows' Smart App Control is on (some fresh Windows 11 installs), unsigned apps are
-> blocked without a "Run anyway" button. NeoFences cannot run while Smart App Control is on.
+> **Smart App Control:** if it is on (some fresh Windows 11 installs), Windows blocks unsigned apps such as NeoFences
+> and offers no "Run anyway". Turning it off is your decision — read
+> [Microsoft's Smart App Control FAQ](https://support.microsoft.com/en-us/windows/security/threat-malware-protection/smart-app-control-frequently-asked-questions)
+> first.
 
 NeoFences **updates itself**: when a new version is ready the tray menu shows *"Restart to update"*; if you ignore it,
 the update installs the next time NeoFences exits. You can turn this off in **Settings → Updates**.
@@ -64,10 +70,25 @@ The [guide](docs/GUIDE.md) explains everything else, with pictures. In the app:
 
 Uninstalling (Windows Settings → Apps) brings your desktop icons back and leaves that folder in place.
 
-## Status and license
+## Check your download
+
+Every release lists `SHA256SUMS.txt`, and GitHub keeps a signed record (a build attestation) that each file was built
+from this repository by its release workflow. In PowerShell, in your Downloads folder:
+
+```powershell
+(Get-FileHash .\NeoFences.App-win-Setup.exe -Algorithm SHA256).Hash   # compare with the line in SHA256SUMS.txt
+gh attestation verify .\NeoFences.App-win-Setup.exe --repo CipherSnowden/neo-fences   # needs the GitHub CLI
+```
+
+## Licence, privacy, security
 
-Free to download and use. The source is published for transparency; **all rights reserved** — it is not open source
-(no license is granted to copy, modify or redistribute it).
+- **Free** to use at home or at work and to share unchanged — see [LICENSE.txt](LICENSE.txt). The source is published so
+  anyone can see what NeoFences does; it is not open source.
+- **Privacy:** no telemetry, no accounts, no ads; the only network traffic is the update check and, if you turn them on,
+  game covers and website icons — see [PRIVACY.md](PRIVACY.md).
+- **Bugs and ideas:** the [issue forms](https://github.com/CipherSnowden/neo-fences/issues/new/choose).
+  **Security problems:** privately, see [SECURITY.md](SECURITY.md).
+- Third-party components and their licences: [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
 
 ## Building
 
@@ -79,5 +100,6 @@ dotnet test
 dotnet run --project src/NeoFences.App
 ```
 
+Core (`src/NeoFences.Core`) is plain .NET: `dotnet test tests/NeoFences.Core.Tests` also runs on Linux, as CI does.
 `build\pack.ps1 -Version x.y.z` makes an installer locally (Velopack). Releases are built by GitHub Actions from version
 tags. Design notes, decisions (ADRs) and the test checklist are in [`docs/`](docs/).
diff --git a/docs/RELEASING.md b/docs/RELEASING.md
new file mode 100644
index 0000000..5cc541f
--- /dev/null
+++ b/docs/RELEASING.md
@@ -0,0 +1,53 @@
+# Releasing NeoFences
+
+Every step that leaves this PC (push, tag, publish, submissions) is asked of the owner first. Versions are `x.y.z`; the
+Release workflow ignores pre-release tags on purpose, so installed copies never jump to a test build (ADR-039).
+
+## Every release
+
+1. **Version:** set `<Version>` in `src/NeoFences.App/NeoFences.App.csproj`; commit `chore: bumped the version to x.y.z`.
+   Add `- [ ] Release x.y.z (the user approved the release <date>)` under the milestone in `docs/ROADMAP.md`; commit
+   `docs: marked the x.y.z release as approved`.
+2. **Secret scan** of everything about to be pushed: `git diff origin/main..HEAD` must not contain the owner's e-mail
+   address or a claude.ai artifact link (the repo is public).
+3. **Push** main and wait for **CI** (Windows build and tests, Core tests on Linux): `gh run watch <id> --exit-status`.
+4. **Tag** `vx.y.z` and push the tag. The **Release** workflow builds, tests and packs (Velopack, with a delta from the
+   previous release), writes `SHA256SUMS.txt`, records **build attestations**, and uploads a **draft** release. Check the
+   draft's assets: Setup.exe, the portable zip, the full and delta packages, `releases.win.json`, `RELEASES`,
+   `SHA256SUMS.txt`.
+5. **Install check in the VM** (not on the owner's PC): download the draft's assets
+   (`gh release download vx.y.z --dir <folder>`), then
+   `pwsh -File tools\vm\Invoke-VmChecks.ps1 -Name NF-Win11 -PreviousSetup <last release's Setup.exe> -Feed <folder> -Candidate x.y.z`
+   → every check PASS (install the last release, update to this one from the draft's own files, and the checklist).
+6. **Check a download:** `gh attestation verify <folder>\NeoFences.App-win-Setup.exe --repo CipherSnowden/neo-fences`
+   passes, and `Get-FileHash` matches its line in `SHA256SUMS.txt`.
+7. **Publish:** write the notes (what changed, for users), then
+   `gh release edit vx.y.z --notes-file <notes.md> --draft=false`. Installed copies update themselves.
+8. **Record:** tick the ROADMAP line (`Released x.y.z on <date> …`), add a **Released:** paragraph to the session's
+   `docs/SESSION-LOG.md` entry, refresh the project hub, push.
+
+## At 1.0.0 (and later major releases) — getting past the warnings
+
+NeoFences is unsigned (ADR-039, ADR-061): Windows' SmartScreen warns until a download has a reputation, and antivirus
+tools sometimes flag new unsigned installers. After publishing 1.0.0:
+
+1. **VirusTotal:** look up Setup.exe and the portable zip by their SHA-256 on https://www.virustotal.com (upload them if
+   they are unknown). Note any detections in the session log.
+2. **Microsoft false-positive review** (the owner signs in and submits): https://www.microsoft.com/wdsi/filesubmission →
+   "Software developer" → upload `NeoFences.App-win-Setup.exe`, detection "Incorrectly detected as malware/malicious"
+   (or SmartScreen), and this text:
+   > NeoFences is a free desktop organizer for Windows 11 by CipherSnowden
+   > (https://github.com/CipherSnowden/neo-fences). The installer is built by GitHub Actions from the public source;
+   > its SHA-256 is listed in the release's SHA256SUMS.txt and it carries a GitHub build attestation. It is unsigned
+   > because a code-signing certificate is not affordable for this free project. Please review it for SmartScreen
+   > reputation.
+3. **winget** (after the owner's OK; the pull request comes from the owner's GitHub account):
+   - `winget install Microsoft.WingetCreate` once;
+   - `wingetcreate new https://github.com/CipherSnowden/neo-fences/releases/download/v1.0.0/NeoFences.App-win-Setup.exe`
+     — identifier `CipherSnowden.NeoFences`, installer type `exe`, silent switch `--silent`, scope `user`, licence
+     "Freeware", licence URL `https://github.com/CipherSnowden/neo-fences/blob/main/LICENSE.txt`, privacy URL
+     `.../blob/main/PRIVACY.md`, short description "Translucent fences on your desktop that hold links to your apps,
+     games, files and websites";
+   - check it in Windows Sandbox or the VM (`winget validate`, `winget install --manifest <folder>`), then let
+     wingetcreate submit (`--submit`), or open the pull request to `microsoft/winget-pkgs` with `gh`;
+   - later versions: `wingetcreate update CipherSnowden.NeoFences --version x.y.z --urls <Setup.exe url> --submit`.
diff --git a/site/index.html b/site/index.html
new file mode 100644
index 0000000..14b2ccf
--- /dev/null
+++ b/site/index.html
@@ -0,0 +1,90 @@
+<!doctype html>
+<html lang="en">
+<head>
+  <meta charset="utf-8">
+  <meta name="viewport" content="width=device-width, initial-scale=1">
+  <title>NeoFences — your desktop, in fences</title>
+  <meta name="description" content="NeoFences: translucent fences on your Windows 11 desktop that hold links to your apps, games, files, folders and websites. Free.">
+  <link rel="icon" href="favicon.ico">
+  <link rel="stylesheet" href="style.css">
+</head>
+<body>
+  <!-- M39 (ADR-061): one hand-written page; the pictures come from docs/guide (the Pages workflow copies them to img/). -->
+  <header class="bar">
+    <a class="brand" href="./"><img src="favicon.ico" alt="" width="28" height="28"> NeoFences</a>
+    <nav>
+      <a href="https://github.com/CipherSnowden/neo-fences/blob/main/docs/GUIDE.md">Guide</a>
+      <a href="https://github.com/CipherSnowden/neo-fences">GitHub</a>
+    </nav>
+  </header>
+
+  <main>
+    <section class="hero">
+      <h1>Your desktop, in fences.</h1>
+      <p class="lead">Translucent fences hold links to your apps, games, files, folders and websites — never the files
+        themselves. Widgets, live folder panels, game covers, and a desktop that stays tidy. Built for a gaming PC first.</p>
+      <div class="actions">
+        <a class="download" href="https://github.com/CipherSnowden/neo-fences/releases/latest/download/NeoFences.App-win-Setup.exe">Download for Windows 11</a>
+        <span class="meta">Free · 64-bit · <a href="https://github.com/CipherSnowden/neo-fences/releases/latest">release notes</a></span>
+      </div>
+      <img class="shot hero-shot" src="img/desktop.jpg" alt="A desktop with NeoFences: a Games fence with covers, an Apps fence, a Downloads folder panel and a Desk fence with widgets" width="1600" height="900">
+    </section>
+
+    <section class="features" aria-label="What it does">
+      <article class="card">
+        <img class="shot" src="img/games.jpg" alt="A Games fence with large cover tiles" loading="lazy">
+        <h2>Games as covers</h2>
+        <p>Steam, Epic, GOG, Ubisoft, EA, Battle.net, Xbox and your own game folders — found and shown as covers, next to
+          your apps.</p>
+      </article>
+      <article class="card">
+        <img class="shot" src="img/widgets.png" alt="Clock, date and system stats widgets" loading="lazy">
+        <h2>Widgets</h2>
+        <p>A clock, the date, and CPU, GPU, RAM and temperatures — in any fence, at any size.</p>
+      </article>
+      <article class="card">
+        <img class="shot" src="img/panel.png" alt="A Downloads folder panel in details view" loading="lazy">
+        <h2>Live folder panels</h2>
+        <p>Downloads, a game folder or a USB stick shown live inside a fence, as details, a list or icons.</p>
+      </article>
+      <article class="card">
+        <h2>Peek and the keyboard</h2>
+        <p><kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Space</kbd> brings the fences above your windows with the keyboard in one:
+          arrows, <kbd>Enter</kbd>, <kbd>Tab</kbd> to the next fence, <kbd>Esc</kbd> back to your app.</p>
+      </article>
+      <article class="card">
+        <h2>Quiet while you play</h2>
+        <p>A full-screen game puts NeoFences to sleep: no timers, no network, about 0&nbsp;% CPU until you are back.</p>
+      </article>
+      <article class="card">
+        <h2>Your files are safe</h2>
+        <p>Fences hold links: removing one never touches a file. Hidden desktop icons always come back — after a crash too.
+          Snapshots and an undo for everything else.</p>
+      </article>
+    </section>
+
+    <section class="install" id="install">
+      <h2>Install in a minute</h2>
+      <ol>
+        <li><a href="https://github.com/CipherSnowden/neo-fences/releases/latest/download/NeoFences.App-win-Setup.exe">Download the installer</a> and run it.
+          No administrator rights needed.</li>
+        <li>If Windows says <em>"Windows protected your PC"</em>, click <strong>More info → Run anyway</strong>. NeoFences
+          is unsigned, not unsafe: a signing certificate costs money every year. Every download can be
+          <a href="https://github.com/CipherSnowden/neo-fences#check-your-download">checked</a> instead.</li>
+        <li>If <strong>Smart App Control</strong> blocks it, that Windows feature allows only signed apps; turning it off is
+          your decision — read <a href="https://support.microsoft.com/en-us/windows/security/threat-malware-protection/smart-app-control-frequently-asked-questions">Microsoft's FAQ</a> first.</li>
+      </ol>
+      <p class="note">NeoFences updates itself. No telemetry, no accounts, no ads — <a href="https://github.com/CipherSnowden/neo-fences/blob/main/PRIVACY.md">privacy</a>.</p>
+    </section>
+  </main>
+
+  <footer>
+    <a href="https://github.com/CipherSnowden/neo-fences/blob/main/docs/GUIDE.md">Guide</a>
+    <a href="https://github.com/CipherSnowden/neo-fences/issues/new/choose">Report a bug</a>
+    <a href="https://github.com/CipherSnowden/neo-fences/blob/main/LICENSE.txt">Licence</a>
+    <a href="https://github.com/CipherSnowden/neo-fences/blob/main/PRIVACY.md">Privacy</a>
+    <a href="https://github.com/CipherSnowden/neo-fences/blob/main/SECURITY.md">Security</a>
+    <span>Windows 11 · free · © 2026 CipherSnowden</span>
+  </footer>
+</body>
+</html>
diff --git a/site/style.css b/site/style.css
new file mode 100644
index 0000000..b4d043e
--- /dev/null
+++ b/site/style.css
@@ -0,0 +1,77 @@
+/* M39 (ADR-061): the landing page — dark glass, like the fences. No framework, no web fonts. */
+:root {
+  --bg: #0b0d12;
+  --glass: rgba(255, 255, 255, 0.06);
+  --edge: rgba(255, 255, 255, 0.12);
+  --text: #e9ecf2;
+  --muted: #a3abba;
+  --accent: #4c8dff;
+  --accent-text: #ffffff;
+  --radius: 14px;
+  color-scheme: dark;
+}
+
+* { box-sizing: border-box; }
+
+body {
+  margin: 0;
+  background: radial-gradient(1200px 700px at 15% -10%, #1d2a4a 0%, transparent 60%),
+              radial-gradient(900px 600px at 110% 10%, #2a1d3f 0%, transparent 55%), var(--bg);
+  color: var(--text);
+  font: 16px/1.6 "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif;
+}
+
+a { color: var(--accent); }
+a:hover { color: #7aa9ff; }
+
+.bar, main, footer { max-width: 1120px; margin: 0 auto; padding: 0 16px; }
+
+.bar { display: flex; align-items: center; justify-content: space-between; padding-top: 18px; padding-bottom: 18px; }
+.brand { display: flex; align-items: center; gap: 10px; color: var(--text); text-decoration: none; font-weight: 600; font-size: 18px; }
+.bar nav { display: flex; gap: 20px; }
+.bar nav a { color: var(--muted); text-decoration: none; }
+.bar nav a:hover { color: var(--text); }
+
+.hero { text-align: center; padding: 48px 0 24px; }
+.hero h1 { font: 700 clamp(34px, 6vw, 60px)/1.1 "Segoe UI Variable Display", "Segoe UI", system-ui, sans-serif; margin: 0 0 16px; letter-spacing: -0.02em; }
+.lead { color: var(--muted); font-size: clamp(17px, 2.2vw, 20px); max-width: 720px; margin: 0 auto 28px; }
+.actions { display: flex; flex-direction: column; align-items: center; gap: 10px; margin-bottom: 40px; }
+.download {
+  display: inline-block; padding: 14px 28px; border-radius: 999px; background: var(--accent); color: var(--accent-text);
+  font-weight: 600; font-size: 18px; text-decoration: none; box-shadow: 0 8px 30px rgba(76, 141, 255, 0.35);
+}
+.download:hover { color: var(--accent-text); background: #3b7cf0; }
+.download:focus-visible, a:focus-visible { outline: 3px solid #9fc0ff; outline-offset: 3px; }
+.meta { color: var(--muted); font-size: 14px; }
+
+.shot { display: block; width: 100%; height: auto; border-radius: var(--radius); border: 1px solid var(--edge); }
+.hero-shot { box-shadow: 0 30px 80px rgba(0, 0, 0, 0.55); }
+
+.features { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 20px; padding: 56px 0; }
+.card {
+  background: var(--glass); border: 1px solid var(--edge); border-radius: var(--radius); padding: 18px;
+  backdrop-filter: blur(12px);
+}
+.card .shot { margin-bottom: 14px; aspect-ratio: 16 / 10; object-fit: cover; object-position: top; }
+.card h2 { font-size: 19px; margin: 0 0 6px; }
+.card p { color: var(--muted); margin: 0; }
+
+kbd {
+  font: 600 13px/1 "Segoe UI", system-ui, sans-serif; padding: 3px 7px; border-radius: 6px;
+  border: 1px solid var(--edge); background: rgba(255, 255, 255, 0.08); color: var(--text);
+}
+
+.install { background: var(--glass); border: 1px solid var(--edge); border-radius: var(--radius); padding: 24px 28px; margin-bottom: 56px; }
+.install h2 { margin-top: 0; }
+.install li { margin-bottom: 10px; }
+.note { color: var(--muted); margin-bottom: 0; }
+
+footer { display: flex; flex-wrap: wrap; gap: 18px; padding-top: 24px; padding-bottom: 40px; border-top: 1px solid var(--edge); color: var(--muted); font-size: 14px; }
+footer a { color: var(--muted); }
+footer span { margin-left: auto; }
+
+@media (max-width: 600px) {
+  .bar nav { gap: 14px; }
+  .install { padding: 18px; }
+  footer span { margin-left: 0; width: 100%; }
+}
```

- [ ] **Step 2: Look at the page.** Assemble it as the workflow does (`site/` + `docs/guide/*.jpg|png` → `img/` +
  `NeoFences.ico` → `favicon.ico`) in a scratch folder; screenshot with headless Edge at `--window-size=1280,2600`, and
  inside a 390 px `<iframe>` for the phone width → one column, no sideways scrolling, every picture shown. Send both
  screenshots to the owner.
- [ ] **Step 3: Commit.** `git add -A && git commit -m "docs: added the landing page, the release checklist and the README's install, download check and licence sections"`.

### Task 5: Docs

**Files:**
- Modify: `docs/DECISIONS.md` (ADR-061), `docs/ARCHITECTURE.md` (0.26.0 paragraph after 0.25.0), `docs/FEATURES.md`
  (an extras row), `docs/TEST-CHECKLIST.md` (section AY)
- Create: `docs/research/m39-release-readiness.md`

- [ ] **Step 1: ADR-061** appended to `docs/DECISIONS.md`:

```markdown
## ADR-061 — A freeware licence, trust files, checked downloads, Core on Linux, one landing page
**Date:** 2026-10-10 · **Status:** Accepted · **Amends:** ADR-039 (releases also carry checksums and attestations)

**Context.** Before 1.0.0-rc goes to friends, strangers need to know what NeoFences is, what they may do with it, what it
sends, and that a download is genuine — without a code-signing certificate (none affordable for an individual in India,
ADR-039). Core was meant to be pure C#, but nothing ran it off Windows; on Linux 45 of its tests failed.

**Decision.**
- **Licence:** a short custom freeware licence (`LICENSE.txt`, the owner's wording; not lawyer-reviewed): free at home
  and at work, the unmodified installer may be shared, no selling or modified versions, the source is published to be
  read and built for oneself. Not open source.
- **Trust files:** `PRIVACY.md` (no telemetry; only the update check and opt-in covers and website icons go over the
  network), `SECURITY.md` (private vulnerability reporting), `THIRD-PARTY-NOTICES.txt`; the licence, notices and privacy
  note ship next to `NeoFences.exe`, and Settings → About → **Licence and notices** opens the licence. Issue forms for
  bugs and ideas.
- **Checked downloads:** every release carries `SHA256SUMS.txt` and GitHub build attestations
  (`gh attestation verify`).
- **Core on Linux:** `WindowsPath` reads the Windows paths Core stores by Windows' rules on every OS; NeoFences' own
  files keep `System.IO.Path`. CI runs Core's tests on Ubuntu as well as Windows.
- **One landing page:** `site/` on GitHub Pages; the README and the guide explain SmartScreen and Smart App Control
  honestly (no push to turn Smart App Control off).
- **Outreach at 1.0:** VirusTotal, Microsoft's false-positive review and a winget entry, prepared in
  `docs/RELEASING.md`, done at 1.0.0.

**Consequences.** A later Linux or macOS port starts from a Core that already runs there; the release flow is written
down; a stranger can check a download without trusting the page that links it.
```

- [ ] **Step 2: ARCHITECTURE** — after the 0.25.0 paragraph:

```markdown
**0.26.0 (M39, release readiness)**: ADR-061 — `WindowsPath` (Core) reads stored Windows paths by Windows' rules on every
OS, so Core's tests run on Linux in CI (`core-linux`); NeoFences' own files keep `System.IO.Path`. The repo has
`LICENSE.txt`, `PRIVACY.md`, `SECURITY.md`, `THIRD-PARTY-NOTICES.txt` (the first, second and fourth ship next to the exe;
Settings → About → Licence and notices), issue forms, `docs/RELEASING.md`, and a landing page (`site/`, published by
`pages.yml` with the guide's pictures). Releases carry `SHA256SUMS.txt` and build attestations.
`research/m39-release-readiness.md`.
```

- [ ] **Step 3: FEATURES** — after the M38 extras row:
  `| Release readiness: freeware licence, privacy and security notes, third-party notices, checked downloads (checksums and attestations), issue forms, a landing page; Core tested on Linux | 0.26 (M39) | done | ADR-061 |`
- [ ] **Step 4: Checklist AY** appended to `docs/TEST-CHECKLIST.md`:

```markdown
## AY — 0.26.0 release readiness (M39)

| ID | Steps | Expected |
|---|---|---|
| AY1 | CI on main | the Windows job and `core-linux` both green |
| AY2 | The 0.26.0 draft | `SHA256SUMS.txt` among the assets; `gh attestation verify <Setup.exe> --repo CipherSnowden/neo-fences` passes; `Get-FileHash` matches its line |
| AY3 | `tools/vm/Invoke-VmChecks.ps1` for the candidate in NF-Win11 | every check PASS, `trust-files` included |
| AY4 | Settings → About → **Licence and notices** | the licence opens; `THIRD-PARTY-NOTICES.txt` and `PRIVACY.md` are beside it |
| AY5 | The landing page at desktop and phone width; every link | reads well, one column on a phone; the download fetches the latest Setup.exe; guide, issues, licence, privacy, security and Microsoft's FAQ open |
| AY6 | GitHub → Issues → New issue | the Bug and Idea forms; blank issues off; "Security problem" goes to a private report |
```

- [ ] **Step 5: Research note** `docs/research/m39-release-readiness.md`: the 45 Linux failures by cause (from "How this
  plan is written"), the fix (`WindowsPath`, which calls stayed on `System.IO.Path` and why), the WSL setup
  (`dotnet-install.sh --channel 10.0`, `libicu78` as root), the licence and notices sources (package licences, dotnet/runtime
  and dotnet/wpf release/10.0 notices, Apache 2.0), the landing page checks, the calls made while prototyping, and
  sections "VM runs", "Screenshots" and "Release 0.26.0" filled by Tasks 7–8.
- [ ] **Step 6: Check** the texts against the code; secret scan of `git diff main`.
- [ ] **Step 7: Commit.** `git add -A && git commit -m "docs: described release readiness in ADR-061, architecture, features, checklist AY and the M39 note"`

### Task 6: Final review and fix pass

- [ ] Whole-branch review on the most capable model (Review Focus above); findings re-graded by effect; Critical/Important
  fixed in one pass, each with a failing test first where Core-testable (Core fixes re-run on Linux too).

### Task 7: VM pass, screenshots (asked first), merge

- [ ] Pack the branch as `0.26.0-rc.1` (`build/pack.ps1 -Version 0.26.0-rc.1 -OutputDir <scratch>`); in NF-Win11:
  `Invoke-VmChecks.ps1 -PreviousSetup <0.25.0 Setup.exe> -Feed <scratch> -Candidate 0.26.0-rc.1` → every check PASS
  (`trust-files` included); in NF-Win11-Dev: `-Live -Data <a scratch copy of the owner's data>` → 8 of 8. Results into the
  research note.
- [ ] **Screenshots, with the owner's OK** (the owner's real setup; nothing changed; screenshots sent as taken; Terminal
  refocused after): retake `docs/guide/desktop.jpg` (the whole desktop), `fence-menu.png`, `item-menu.jpg`,
  `settings.png` (About, with Licence and notices), `panel.png` (Downloads in details view), and add `peek.jpg` (Peek with
  the keyboard ring in a fence). Use it in `docs/GUIDE.md` §8 and as the picture of the landing page's "Peek and the
  keyboard" card (`img/peek.jpg`). Or the owner takes them from this list.
- [ ] Commit `docs: refreshed the guide's screenshots and added Peek with the keyboard`; fast-forward main locally; ROADMAP,
  SESSION-LOG, hub.

### Task 8: Release 0.26.0 and turn on the public pieces (asked first)

- [ ] Follow `docs/RELEASING.md` (asked at each outward step): version 0.26.0, push, CI (AY1), tag, draft with
  `SHA256SUMS.txt` (AY2), install check in NF-Win11 (AY3), `gh attestation verify` and `Get-FileHash` (AY2), publish.
- [ ] With the owner's OK: turn on **GitHub Pages** (source: GitHub Actions — `gh api -X POST repos/CipherSnowden/neo-fences/pages -f build_type=workflow`),
  run the Pages workflow, check the page at `https://ciphersnowden.github.io/neo-fences/` (AY5); turn on **private
  vulnerability reporting** (`gh api -X PUT repos/CipherSnowden/neo-fences/private-vulnerability-reporting`); check the
  issue forms (AY6).
- [ ] Record the release (ROADMAP, SESSION-LOG, research note "Release 0.26.0"), hub, push.
