# M37 — Performance at scale (0.24.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** With about 500 items in 50 fences NeoFences starts sooner, shows its icons in about 3 s instead of 11 s (at once from a cache on later starts), scrolls and resizes with fewer stalls, and does no work while nothing can be seen — measured before and after with a script kept in the repo.

**Architecture:** Core (test-first) gains the icon cache's decisions (`IconCache`: key, stamp, pruning; `IconCacheIndex`: the index file) and `PerfLog` (the timing marks' texts and their reader, loaded by the measurement script). The App's `IconLoader` answers each key once per run (a session layer), then from the disk cache (`IconDiskCache`, `cache\icons\`), then from the shell, with an urgent queue for what can be seen; Refresh and special icons go past the cache. The host logs timing marks, starts non-visual work after "fences shown", and stops the widget timer, the hover polls and the auto-collect timer while nothing can be seen. Item labels are drawn once into a bitmap (same shadow); the cover tile is built only for covers. `tools/perf/measure-scale.ps1` builds a 500 / 50 setup on a backed-up copy of the data and measures.

**Tech Stack:** .NET 10, C#, WPF, xUnit; PowerShell 7.5+ for the measurement (it loads `NeoFences.Core.dll`).

**Spec:** `docs/superpowers/specs/2026-10-06-performance-at-scale-design.md` (approved 2026-10-06). Decision: ADR-058 — added by Task 3.

## How this plan is written

Built in a scratch worktree (`m37-proto`) and measured six times on a copy of the owner's data with the script (owner's OK
each time; data, startup entry and the installed copy restored after each). The numbers that shaped it (500 items, 50
fences, the owner's PC, 100 %):

| | before (no cache) | after |
|---|---|---|
| fences shown (cold) | 3.6 s | 3.0 s |
| icons settled (cold) | 11.1 s (4,967 requests) | 3.0 s (503 requests) |
| fences and icons (warm, cache) | — | 2.75 s (272 of 503 from the cache) |
| the 200-item fence shown | 242 ms | 130–160 ms |
| scroll a 200-item fence | 12 of 268 frames over 33 ms | 4–12 of ~300–450 over 33 ms (worst 42–85 ms) |
| drag and resize a 100-item fence | worst 420 ms | worst 215–264 ms |
| CPU idle 60 s | 0.00–0.02 s | 0.00–0.03 s |

Where the start goes now: ~1.1–1.3 s creating 50 windows, ~1.6 s showing them (each lays out its items); icons are no
longer the bottleneck. Ready-to-run (two self-contained publishes) gave 3.87 s vs 4.17 s (7 %, below the spec's 20 %):
dropped. Virtualization: the 200-item fence came down to 130–160 ms (the spec's line is 150 ms) after the lighter items;
the owner chose to plan without it (after 1.0). The script's fake game (a borderless window) never engaged game mode
(Windows did not count it as full screen in the foreground): game-mode CPU is measured in the live check with a real
full-screen app. Replay-verified on a fresh worktree of `main` at `386b145`: patch 1a alone fails to build (`error CS`:
`IconCacheEntry`, `IconStamp` … do not exist — the RED), 1b makes 822 tests pass, patch 2 builds with 0 warnings and 822
pass; the tree is identical to the prototype. Tasks 1–2 are **patches to apply** (`git apply --whitespace=nowarn <file>`;
if one does not apply, stop).

**Calls made while prototyping (ledger them as rulings at the task named):**
- Task 1: the timing marks' texts and their reader live together in Core (`PerfLog`), loaded by the script (pwsh 7.6 runs
  on .NET 10); the cache key lower-cases file paths only (URLs and app ids keep their case); a newer index is an empty one.
- Task 2: each key is asked of the shell once per run (a session layer) — that, not the disk cache, halved the requests;
  names are stored with every icon (another item of the same target may want it); an unreachable target's generic icon is
  not cached; Refresh and special icons (the Recycle Bin) go past both caches; "can be seen" is the item's container inside
  the list's view (the first 48 by index before the first layout); the hover poll and the auto-collect timer stop in game
  mode and while hidden or paused (the spec's "every timer reviewed"); labels keep their shadow and are cached as bitmaps
  (`BitmapCache`) instead of a new drawn halo — the same look, the scroll stutter cut by two thirds; the cover tile became
  its own template built only for covers (the M28 pattern) — not in the spec's list, it brought the 200-item fence from 242
  to ~140 ms; ready-to-run dropped (7 %); virtualization not done (owner's choice, at the line); the index is written by
  one save at a time; "icons settled" before "fences shown" is logged with it; timing steps (windows opened, items set,
  slowest fence, layout applied) are logged at every start.

## Global Constraints

- Hard rules stand: the cache is NeoFences' own folder (`%LOCALAPPDATA%\NeoFences\cache\icons\`), never a user's file;
  no new NuGet dependency; Core pure and test-first; Win32 only in Shell; failures logged, never a crash.
- Nothing visible changes except speed: the same icons, labels, shadows and cover tiles as 0.23.0.
- The script never changes the owner's data for good: it backs up, runs, puts back the data and the startup entry and starts
  the installed copy; ASCII only; no personal paths in the repo.
- Commits: single line, Conventional Commits, past tense, **no Co-Authored-By trailer**; secret scan before each commit.
- Version stays `0.23.0` until the release step; the release is 0.24.0.

## Review Focus

1. **Stale icons**: an app updated in place (same .exe, new icon), a shortcut pointed elsewhere, an item picture edited, the
   Recycle Bin turning full or empty, a Start app updated — the cached icon may show first but is replaced; fence menu →
   Refresh always asks Windows again. (Checklist AW3.)
2. **The cache folder in trouble**: a damaged index, a PNG deleted or locked, a full disk, a cache folder that cannot be
   written — icons still load fresh, no crash, no endless retries, nothing outside the cache folder touched. (AW4.)
3. **Waking up after idle**: game mode ending, quick-hide off, unpause, a tab switch, a roll-up opening, a time change —
   widgets tick again within a second; rolled-up fences open on hover again; hidden title bars show on hover again. (AW5.)
4. **Cover tiles after the template move**: hover lift, selection ring, poster / logo / glow, the Missing badge and dimming,
   Large covers, the name overlay on labels-on-hover fences — exactly as in 0.23.0. (AW6.)
5. **Labels drawn once**: sharp at 125 / 150 %, in light mode, after a light/dark switch, after a rename, on fences with
   labels on hover. (AW7.)

---

### Task 0: Worktree and baseline

- [ ] `git worktree add -b m37-performance ..\neo_fences-m37 main` (main at `386b145` or later docs-only commits).
- [ ] `dotnet build` → 0 warnings; `dotnet test` → 810 passed.

### Task 1: Core — the icon cache's decisions and the timing marks

**Files:**
- Create: `tests/NeoFences.Core.Tests/Items/IconCacheTests.cs`, `tests/NeoFences.Core.Tests/Lifecycle/PerfLogTests.cs`,
  `src/NeoFences.Core/Items/IconCache.cs`, `src/NeoFences.Core/Lifecycle/PerfLog.cs`

**Interfaces:**
- Produces: `record IconStamp(long LastWriteTicks, long Length)`; `record IconCacheEntry { string File; string? Name;
  IconStamp? Stamp; DateTimeOffset LastUsed; long Bytes }`; `record IconCacheIndex { Entries; With(key, entry); Touch(keys,
  now); static Load(path) → (Index, Failure); Save(path) }`; `IconCache.KeyOf(target, ownIcon, sizePx) → string`,
  `.FileNameOf(key)`, `.NeedsFreshLoad(entry, current) → bool`, `.Prune(index, now, maxBytes) → (Kept, Delete)`,
  `.MaxBytes` (64 MB), `.UnusedFor` (30 days); `PerfLog.FencesShownText(ms, windows)`, `.IconsSettledText(ms, requests,
  fromCache)`, `.FramesText(frames, worstMs, slow)`, `.Read(lines) → IReadOnlyList<PerfRun>`, `.FramesBetween(lines, from,
  to) → (Frames, WorstMs, Slow)`; `record PerfRun(Pid, FencesShownMs, IconsSettledMs, IconRequests, FromCache, Frames,
  WorstFrameMs, SlowFrames)`.

- [ ] **Step 1: Write the failing tests.** Write this patch to `m37-1a-tests.patch` and apply it:

```diff
diff --git a/tests/NeoFences.Core.Tests/Items/IconCacheTests.cs b/tests/NeoFences.Core.Tests/Items/IconCacheTests.cs
new file mode 100644
index 0000000..a0bf3ef
--- /dev/null
+++ b/tests/NeoFences.Core.Tests/Items/IconCacheTests.cs
@@ -0,0 +1,134 @@
+using NeoFences.Core.Items;
+using NeoFences.Core.Tests.TestSupport;
+
+namespace NeoFences.Core.Tests.Items;
+
+/// <summary>M37 (spec 2026-10-06-performance-at-scale-design §3): the icon and name cache's decisions.</summary>
+public class IconCacheTests
+{
+    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
+
+    private static IconCacheEntry Entry(string key, DateTimeOffset lastUsed, long bytes = 1000, IconStamp? stamp = null, string? name = null) =>
+        new() { File = IconCache.FileNameOf(key), LastUsed = lastUsed, Bytes = bytes, Stamp = stamp, Name = name };
+
+    // ---------- the key ----------
+
+    [Fact]
+    public void Key_IsTheSameForTheSamePathInAnyCase_AndDiffersBySizeAndOwnIcon()
+    {
+        var key = IconCache.KeyOf(@"C:\Tools\App.exe", ownIcon: null, sizePx: 48);
+        Assert.Matches("^[0-9a-f]{32}$", key);
+        Assert.Equal(key, IconCache.KeyOf(@"c:\tools\app.EXE", ownIcon: null, sizePx: 48));
+        Assert.NotEqual(key, IconCache.KeyOf(@"C:\Tools\App.exe", ownIcon: null, sizePx: 72));
+        Assert.NotEqual(key, IconCache.KeyOf(@"C:\Tools\App.exe", new ItemIcon { File = @"C:\icons\a.ico", Index = 2 }, sizePx: 48));
+        Assert.NotEqual(IconCache.KeyOf(@"C:\Tools\App.exe", new ItemIcon { File = @"C:\icons\a.ico", Index = 2 }, 48),
+            IconCache.KeyOf(@"C:\Tools\App.exe", new ItemIcon { File = @"C:\icons\a.ico", Index = 3 }, 48));
+        Assert.NotEqual(key, IconCache.KeyOf(@"C:\Tools\App.exe", new ItemIcon { Image = "pic.png" }, sizePx: 48));
+    }
+
+    [Fact]
+    public void Key_KeepsTheCaseOfWebsitesAndAppIds()
+    {
+        // A URL's path and a Store app id are not case-insensitive file paths: two of them may differ only in case.
+        Assert.NotEqual(IconCache.KeyOf("https://example.com/A", null, 48), IconCache.KeyOf("https://example.com/a", null, 48));
+        Assert.Equal("abc.png", IconCache.FileNameOf("abc"));
+    }
+
+    // ---------- when the fresh load runs ----------
+
+    [Fact]
+    public void FreshLoad_RunsWithoutAnEntry_WithoutAStamp_AndWhenTheStampChanged()
+    {
+        var stamp = new IconStamp(LastWriteTicks: 100, Length: 5);
+        Assert.True(IconCache.NeedsFreshLoad(entry: null, current: stamp));
+        Assert.True(IconCache.NeedsFreshLoad(Entry("a", Now, stamp: null), current: null));      // a Start app, a website: always behind
+        Assert.True(IconCache.NeedsFreshLoad(Entry("a", Now, stamp: stamp), current: null));     // the file is gone now
+        Assert.True(IconCache.NeedsFreshLoad(Entry("a", Now, stamp: stamp), current: stamp with { Length = 6 }));
+        Assert.False(IconCache.NeedsFreshLoad(Entry("a", Now, stamp: stamp), current: stamp));
+    }
+
+    // ---------- pruning ----------
+
+    [Fact]
+    public void Prune_DropsEntriesUnusedFor30Days_ThenTheOldestUntilUnderTheCap()
+    {
+        var index = new IconCacheIndex
+        {
+            Entries = new Dictionary<string, IconCacheEntry>
+            {
+                ["old"] = Entry("old", Now.AddDays(-31)),
+                ["a"] = Entry("a", Now.AddDays(-3), bytes: 600),
+                ["b"] = Entry("b", Now.AddDays(-2), bytes: 600),
+                ["c"] = Entry("c", Now.AddDays(-1), bytes: 600),
+            },
+        };
+        var (kept, delete) = IconCache.Prune(index, Now, maxBytes: 1300);
+        Assert.Equal(["b", "c"], kept.Entries.Keys.Order(StringComparer.Ordinal));
+        Assert.Equal(["a.png", "old.png"], delete.Order(StringComparer.Ordinal));
+        // nothing to drop: nothing deleted
+        Assert.Empty(IconCache.Prune(new IconCacheIndex { Entries = new Dictionary<string, IconCacheEntry> { ["c"] = Entry("c", Now) } }, Now, 1300).Delete);
+    }
+
+    [Fact]
+    public void Touch_MarksEntriesUsedNow_AndWithAddsOrReplaces()
+    {
+        var index = new IconCacheIndex().With("a", Entry("a", Now.AddDays(-10)));
+        index = index.With("b", Entry("b", Now.AddDays(-10))).Touch(["a", "missing"], Now);
+        Assert.Equal(Now, index.Entries["a"].LastUsed);
+        Assert.Equal(Now.AddDays(-10), index.Entries["b"].LastUsed);
+        Assert.False(index.Entries.ContainsKey("missing"));
+        Assert.Equal("New", index.With("a", Entry("a", Now, name: "New")).Entries["a"].Name);
+    }
+
+    // ---------- the index file ----------
+
+    [Fact]
+    public void Index_SurvivesTheFile_AndADamagedOneStartsOver()
+    {
+        using var folder = new TempDirectory();
+        var path = folder.File("index.json");
+        var index = new IconCacheIndex().With("a", Entry("a", Now, stamp: new IconStamp(100, 5), name: "Tool"));
+        index.Save(path);
+        var (back, failure) = IconCacheIndex.Load(path);
+        Assert.Null(failure);
+        Assert.Equal(index.Entries["a"], back.Entries["a"]);
+
+        File.WriteAllText(path, "{ not json");
+        var (damaged, problem) = IconCacheIndex.Load(path);
+        Assert.Empty(damaged.Entries);
+        Assert.NotNull(problem);
+
+        var (missing, none) = IconCacheIndex.Load(folder.File("nothing.json"));
+        Assert.Empty(missing.Entries);
+        Assert.Null(none); // no cache yet is not a problem
+    }
+
+    [Fact]
+    public void Load_DropsEntriesThatNameAFileOutsideTheFolderOrAreOdd()
+    {
+        using var folder = new TempDirectory();
+        var path = folder.File("index.json");
+        File.WriteAllText(path, """
+            { "version": 1, "entries": {
+              "good": { "file": "good.png", "lastUsed": "2026-10-06T12:00:00+00:00", "bytes": 10 },
+              "escape": { "file": "..\\..\\evil.png", "lastUsed": "2026-10-06T12:00:00+00:00" },
+              "other": { "file": "other.exe", "lastUsed": "2026-10-06T12:00:00+00:00" },
+              "blank": { "file": "", "lastUsed": "2026-10-06T12:00:00+00:00" },
+              "nulled": null,
+              "  ": { "file": "x.png" }
+            } }
+            """);
+        var (index, failure) = IconCacheIndex.Load(path);
+        Assert.Null(failure);
+        Assert.Equal(["good"], index.Entries.Keys);
+    }
+
+    [Fact]
+    public void Load_TreatsANewerIndexAsEmpty()
+    {
+        using var folder = new TempDirectory();
+        var path = folder.File("index.json");
+        File.WriteAllText(path, """{ "version": 9, "entries": { "good": { "file": "good.png" } } }""");
+        Assert.Empty(IconCacheIndex.Load(path).Index.Entries);
+    }
+}
diff --git a/tests/NeoFences.Core.Tests/Lifecycle/PerfLogTests.cs b/tests/NeoFences.Core.Tests/Lifecycle/PerfLogTests.cs
new file mode 100644
index 0000000..01654d7
--- /dev/null
+++ b/tests/NeoFences.Core.Tests/Lifecycle/PerfLogTests.cs
@@ -0,0 +1,64 @@
+using NeoFences.Core.Lifecycle;
+
+namespace NeoFences.Core.Tests.Lifecycle;
+
+/// <summary>M37 (spec §1): the timing marks NeoFences logs, read back for the measurement report.</summary>
+public class PerfLogTests
+{
+    private static readonly string[] Lines =
+    [
+        "2026-10-06 17:56:57.193 +05:30 [INF] NeoFences starting, pid 11092, OS 10.0.26200.0",
+        "2026-10-06 17:56:57.933 +05:30 [INF] desktop gestures: WH_MOUSE_LL installed",
+        "2026-10-06 17:56:58.020 +05:30 [INF] timing: fences shown after 812 ms (50 windows)",
+        "2026-10-06 17:56:59.900 +05:30 [INF] timing: icons settled after 2690 ms (493 requests, 410 from the cache)",
+        "2026-10-06 17:57:10.000 +05:30 [INF] timing: frames 120, worst 48 ms, over 33 ms 3",
+        "2026-10-06 17:57:12.000 +05:30 [INF] timing: frames 118, worst 21 ms, over 33 ms 0",
+        "2026-10-06 17:58:14.656 +05:30 [INF] NeoFences starting, pid 5656, OS 10.0.26200.0",
+        "2026-10-06 17:58:15.300 +05:30 [INF] timing: fences shown after 640 ms (50 windows)",
+        "   at a stack trace line that is not a log line",
+    ];
+
+    [Fact]
+    public void Read_GivesOneRunPerStart_WithItsMarksAndFrames()
+    {
+        var runs = PerfLog.Read(Lines);
+        Assert.Equal(2, runs.Count);
+        var first = runs[0];
+        Assert.Equal(11092, first.Pid);
+        Assert.Equal((812, 2690, 410, 493), (first.FencesShownMs, first.IconsSettledMs, first.FromCache, first.IconRequests));
+        Assert.Equal((238, 48.0, 3), (first.Frames, first.WorstFrameMs, first.SlowFrames));
+        var second = runs[1];
+        Assert.Equal((640, (int?)null, 0), (second.FencesShownMs, second.IconsSettledMs, second.Frames));
+    }
+
+    [Fact]
+    public void Read_IgnoresMarksBeforeAnyStart_AndEmptyInput()
+    {
+        Assert.Empty(PerfLog.Read([]));
+        Assert.Empty(PerfLog.Read(["2026-10-06 17:56:58.020 +05:30 [INF] timing: fences shown after 812 ms (50 windows)"]));
+    }
+
+    [Fact]
+    public void FramesBetween_SumsOnlyTheFrameLinesInsideTheTimes()
+    {
+        var from = new DateTimeOffset(2026, 10, 6, 17, 57, 9, TimeSpan.FromHours(5.5));
+        var (frames, worst, slow) = PerfLog.FramesBetween(Lines, from, from.AddSeconds(4));
+        Assert.Equal((238, 48.0, 3), (frames, worst, slow));
+        Assert.Equal((120, 48.0, 3), PerfLog.FramesBetween(Lines, from, from.AddSeconds(1.5)));
+        Assert.Equal((0, 0.0, 0), PerfLog.FramesBetween(Lines, from.AddHours(1), from.AddHours(2)));
+    }
+
+    [Fact]
+    public void Marks_AreWrittenAsTheReaderExpects()
+    {
+        var lines = new[]
+        {
+            "2026-10-06 18:00:00.000 +05:30 [INF] NeoFences starting, pid 7, OS 10",
+            "2026-10-06 18:00:01.000 +05:30 [INF] " + PerfLog.FencesShownText(ms: 700, windows: 4),
+            "2026-10-06 18:00:02.000 +05:30 [INF] " + PerfLog.IconsSettledText(ms: 1500, requests: 30, fromCache: 28),
+            "2026-10-06 18:00:03.000 +05:30 [INF] " + PerfLog.FramesText(frames: 60, worstMs: 35.4, slow: 1),
+        };
+        var run = PerfLog.Read(lines).Single();
+        Assert.Equal((700, 1500, 28, 30, 60, 35.4, 1), (run.FencesShownMs, run.IconsSettledMs, run.FromCache, run.IconRequests, run.Frames, run.WorstFrameMs, run.SlowFrames));
+    }
+}
```

- [ ] **Step 2: Run them.** `dotnet test tests/NeoFences.Core.Tests` → Expected: build fails with `error CS` lines (`IconCacheEntry`, `IconStamp`, … do not exist).
- [ ] **Step 3: Implement.** Write this patch to `m37-1b-core.patch` and apply it:

```diff
diff --git a/src/NeoFences.Core/Items/IconCache.cs b/src/NeoFences.Core/Items/IconCache.cs
new file mode 100644
index 0000000..a484118
--- /dev/null
+++ b/src/NeoFences.Core/Items/IconCache.cs
@@ -0,0 +1,142 @@
+using System.Security.Cryptography;
+using System.Text;
+using System.Text.Json;
+using NeoFences.Core.Config;
+
+namespace NeoFences.Core.Items;
+
+/// <summary>Where a cached icon came from (M37): the file's last write and length when it was cached.</summary>
+public sealed record IconStamp(long LastWriteTicks, long Length);
+
+/// <summary>One cached icon (M37): its PNG in the cache folder, the display name, the source's stamp and when it was last used.</summary>
+public sealed record IconCacheEntry
+{
+    public string File { get; init; } = "";
+
+    /// <summary>Windows' display name for the target, or null when the item has its own name (not asked).</summary>
+    public string? Name { get; init; }
+
+    /// <summary>Null for a target without one (a Start app, a shell item, a website, a file that was missing).</summary>
+    public IconStamp? Stamp { get; init; }
+
+    public DateTimeOffset LastUsed { get; init; }
+
+    public long Bytes { get; init; }
+}
+
+/// <summary>
+/// The icon and name cache's index (M37, ADR-058): <c>cache\icons\index.json</c> next to the PNGs it names. NeoFences' own
+/// data only; a damaged, missing or newer file is an empty index (the icons load as before and the cache fills again).
+/// </summary>
+public sealed record IconCacheIndex
+{
+    public const int CurrentVersion = 1;
+
+    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };
+
+    public int Version { get; init; } = CurrentVersion;
+
+    public IReadOnlyDictionary<string, IconCacheEntry> Entries { get; init; } = new Dictionary<string, IconCacheEntry>(StringComparer.Ordinal);
+
+    public IconCacheIndex With(string key, IconCacheEntry entry) =>
+        this with { Entries = new Dictionary<string, IconCacheEntry>(Entries, StringComparer.Ordinal) { [key] = entry } };
+
+    /// <summary>The entries of these keys were used now (keys without an entry are skipped).</summary>
+    public IconCacheIndex Touch(IEnumerable<string> keys, DateTimeOffset now)
+    {
+        var entries = new Dictionary<string, IconCacheEntry>(Entries, StringComparer.Ordinal);
+        foreach (var key in keys)
+        {
+            if (entries.TryGetValue(key, out var entry)) entries[key] = entry with { LastUsed = now };
+        }
+        return this with { Entries = entries };
+    }
+
+    /// <returns>The index (empty when there is none or it cannot be used) and why it could not be read, or null.</returns>
+    public static (IconCacheIndex Index, Exception? Failure) Load(string path)
+    {
+        try
+        {
+            if (!System.IO.File.Exists(path)) return (new IconCacheIndex(), null);
+            var stored = JsonSerializer.Deserialize<Stored>(System.IO.File.ReadAllText(path), Options);
+            if (stored is null || stored.Version > CurrentVersion) return (new IconCacheIndex(), null); // a newer NeoFences': start over
+            var entries = new Dictionary<string, IconCacheEntry>(StringComparer.Ordinal);
+            foreach (var (key, entry) in stored.Entries ?? [])
+            {
+                // Only a PNG's own name in the cache folder counts: a hand-edited "..\x" never points elsewhere.
+                if (string.IsNullOrWhiteSpace(key) || entry is null || entry.File.Length == 0 || entry.File != Path.GetFileName(entry.File)
+                    || !entry.File.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
+                entries[key] = entry;
+            }
+            return (new IconCacheIndex { Entries = entries }, null);
+        }
+        catch (Exception failure) when (failure is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
+        {
+            return (new IconCacheIndex(), failure);
+        }
+    }
+
+    /// <exception cref="IOException">The file could not be written (the caller logs it; the icons show anyway).</exception>
+    public void Save(string path)
+    {
+        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
+        SafeFile.Write(path, JsonSerializer.Serialize(new Stored(Version, new Dictionary<string, IconCacheEntry?>(Entries.Select(entry =>
+            new KeyValuePair<string, IconCacheEntry?>(entry.Key, entry.Value)))), Options), backupPath: null);
+    }
+
+    private sealed record Stored(int Version, Dictionary<string, IconCacheEntry?>? Entries);
+}
+
+/// <summary>The icon cache's decisions (M37, spec §3). Pure.</summary>
+public static class IconCache
+{
+    /// <summary>The cache stays under this size, oldest entries first.</summary>
+    public const long MaxBytes = 64L * 1024 * 1024;
+
+    /// <summary>Entries not used for this long go.</summary>
+    public static readonly TimeSpan UnusedFor = TimeSpan.FromDays(30);
+
+    /// <summary>
+    /// An item's cache key (32 hex characters, also its PNG's name): its target (a file path in any case is the same), its own
+    /// icon and the pixel size its fence draws it at.
+    /// </summary>
+    public static string KeyOf(string target, ItemIcon? ownIcon, int sizePx)
+    {
+        var normalized = ItemKinds.Of(target) == ItemKind.Path ? target.ToLowerInvariant() : target;
+        var own = ownIcon is null ? "" : $"{ownIcon.File?.ToLowerInvariant()}|{ownIcon.Index}|{ownIcon.Image}";
+        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{normalized}\n{own}\n{sizePx}"));
+        return Convert.ToHexStringLower(hash)[..32];
+    }
+
+    public static string FileNameOf(string key) => key + ".png";
+
+    /// <summary>
+    /// Whether the icon is loaded fresh behind the cached one: no entry; no stamp then or now (Start apps, shell items, websites,
+    /// a missing file: always, quietly); or a source that changed since.
+    /// </summary>
+    public static bool NeedsFreshLoad(IconCacheEntry? entry, IconStamp? current) =>
+        entry is null || entry.Stamp is null || current is null || entry.Stamp != current;
+
+    /// <summary>
+    /// The cleanup at start: entries unused for <see cref="UnusedFor"/> go, then the oldest until the rest fits in
+    /// <paramref name="maxBytes"/>.
+    /// </summary>
+    /// <returns>The index kept and the PNG file names to delete (NeoFences' own files in its cache folder).</returns>
+    public static (IconCacheIndex Kept, IReadOnlyList<string> Delete) Prune(IconCacheIndex index, DateTimeOffset now, long maxBytes = MaxBytes)
+    {
+        var kept = new Dictionary<string, IconCacheEntry>(StringComparer.Ordinal);
+        var delete = new List<string>();
+        long total = 0;
+        foreach (var (key, entry) in index.Entries.OrderByDescending(entry => entry.Value.LastUsed))
+        {
+            if (now - entry.LastUsed > UnusedFor || total + entry.Bytes > maxBytes)
+            {
+                delete.Add(entry.File);
+                continue;
+            }
+            total += entry.Bytes;
+            kept[key] = entry;
+        }
+        return (delete.Count == 0 ? index : index with { Entries = kept }, delete);
+    }
+}
diff --git a/src/NeoFences.Core/Lifecycle/PerfLog.cs b/src/NeoFences.Core/Lifecycle/PerfLog.cs
new file mode 100644
index 0000000..ecbeb56
--- /dev/null
+++ b/src/NeoFences.Core/Lifecycle/PerfLog.cs
@@ -0,0 +1,84 @@
+using System.Globalization;
+using System.Text.RegularExpressions;
+
+namespace NeoFences.Core.Lifecycle;
+
+/// <summary>One NeoFences start as its log tells it (M37): the timing marks and the frame statistics after it.</summary>
+public sealed record PerfRun(int Pid, int? FencesShownMs, int? IconsSettledMs, int IconRequests, int FromCache, int Frames, double WorstFrameMs, int SlowFrames);
+
+/// <summary>
+/// The timing marks NeoFences writes to its log (M37, spec §1) and the reader the measurement script uses (it loads this
+/// assembly). The texts and the reader live together so they cannot drift apart.
+/// </summary>
+public static partial class PerfLog
+{
+    public static string FencesShownText(long ms, int windows) => $"timing: fences shown after {ms} ms ({windows} windows)";
+
+    public static string IconsSettledText(long ms, int requests, int fromCache) =>
+        $"timing: icons settled after {ms} ms ({requests} requests, {fromCache} from the cache)";
+
+    public static string FramesText(int frames, double worstMs, int slow) =>
+        string.Create(CultureInfo.InvariantCulture, $"timing: frames {frames}, worst {worstMs:0.#} ms, over 33 ms {slow}");
+
+    /// <summary>One run per "NeoFences starting" line; marks before the first start are ignored.</summary>
+    public static IReadOnlyList<PerfRun> Read(IEnumerable<string> lines)
+    {
+        var runs = new List<PerfRun>();
+        PerfRun? run = null;
+        foreach (var line in lines)
+        {
+            if (StartLine().Match(line) is { Success: true } start)
+            {
+                if (run is not null) runs.Add(run);
+                run = new PerfRun(int.Parse(start.Groups[1].Value, CultureInfo.InvariantCulture), null, null, 0, 0, 0, 0, 0);
+                continue;
+            }
+            if (run is null) continue;
+            if (FencesLine().Match(line) is { Success: true } fences)
+                run = run with { FencesShownMs = int.Parse(fences.Groups[1].Value, CultureInfo.InvariantCulture) };
+            else if (IconsLine().Match(line) is { Success: true } icons)
+                run = run with
+                {
+                    IconsSettledMs = int.Parse(icons.Groups[1].Value, CultureInfo.InvariantCulture),
+                    IconRequests = int.Parse(icons.Groups[2].Value, CultureInfo.InvariantCulture),
+                    FromCache = int.Parse(icons.Groups[3].Value, CultureInfo.InvariantCulture),
+                };
+            else if (FramesLine().Match(line) is { Success: true } frames)
+                run = run with
+                {
+                    Frames = run.Frames + int.Parse(frames.Groups[1].Value, CultureInfo.InvariantCulture),
+                    WorstFrameMs = Math.Max(run.WorstFrameMs, double.Parse(frames.Groups[2].Value, CultureInfo.InvariantCulture)),
+                    SlowFrames = run.SlowFrames + int.Parse(frames.Groups[3].Value, CultureInfo.InvariantCulture),
+                };
+        }
+        if (run is not null) runs.Add(run);
+        return runs;
+    }
+
+    /// <summary>The frame statistics logged between two times (a scroll, a drag), summed; the worst frame of them.</summary>
+    public static (int Frames, double WorstMs, int Slow) FramesBetween(IEnumerable<string> lines, DateTimeOffset from, DateTimeOffset to)
+    {
+        var (frames, worst, slow) = (0, 0.0, 0);
+        foreach (var line in lines)
+        {
+            if (line.Length < 30 || !DateTimeOffset.TryParseExact(line[..30], "yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
+                || at < from || at > to || FramesLine().Match(line) is not { Success: true } match) continue;
+            frames += int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
+            worst = Math.Max(worst, double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
+            slow += int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
+        }
+        return (frames, worst, slow);
+    }
+
+    [GeneratedRegex(@"\[INF\] NeoFences starting, pid (\d+)")]
+    private static partial Regex StartLine();
+
+    [GeneratedRegex(@"\[INF\] timing: fences shown after (\d+) ms")]
+    private static partial Regex FencesLine();
+
+    [GeneratedRegex(@"\[INF\] timing: icons settled after (\d+) ms \((\d+) requests, (\d+) from the cache\)")]
+    private static partial Regex IconsLine();
+
+    [GeneratedRegex(@"\[INF\] timing: frames (\d+), worst ([\d.]+) ms, over 33 ms (\d+)")]
+    private static partial Regex FramesLine();
+}
```

- [ ] **Step 4: Run.** `dotnet test tests/NeoFences.Core.Tests` → Expected: `Passed: 822`.
- [ ] **Step 5: Commit.** `git add -A && git commit -m "feat: added the icon cache's rules and the timing marks in Core"` (ledger the Task 1 calls).

### Task 2: App — icon cache, visible first, idle timers, lighter items, timing marks, the measurement script

**Files:**
- Create: `src/NeoFences.App/IconDiskCache.cs`, `src/NeoFences.App/FenceHost.Perf.cs`, `tools/perf/measure-scale.ps1`
- Modify: `src/NeoFences.App/IconLoader.cs` (session layer, disk cache, urgent / later queues, `fresh`, stamps, settle
  count), `src/NeoFences.App/FenceWindow.xaml(.cs)` (visible-first requests, raise on scroll / unroll, hover-poll pause,
  `ItemCount`, label `BitmapCache`, `TileTemplate`), `src/NeoFences.App/FenceItemView.cs` (`IconPending`),
  `src/NeoFences.App/AppPaths.cs` (`IconCacheDirectory`), `src/NeoFences.App/FenceHost.cs` (marks, steps, fences first,
  hover polls, the loader with its cache), `src/NeoFences.App/FenceHost.Widgets.cs` (timer stops when nothing can be seen),
  `src/NeoFences.App/FenceHost.Collect.cs` (no ticks during a game), `src/NeoFences.App/FenceHost.Watching.cs` (Refresh
  past the cache)

**Interfaces:**
- Consumes: Task 1's `IconCache`, `IconCacheIndex`, `IconStamp`, `PerfLog`.
- Produces: `IconLoader.Request(view, sizePx, urgent = true, fresh = false)`, `.Settled`, `.Pending`, `.Requested`,
  `.FromCache`; `IconDiskCache(directory)` with `LoadAndPrune()`, `TryGet(key)`, `Put(key, icon, name, stamp)`,
  `SaveIfChanged()`; `FenceWindow.ReloadIcons(fresh)`, `.SetHoverPaused(bool)`, `.ItemCount`; log marks `timing: fences
  shown`, `timing: icons settled`, `timing: frames` (with `NEOFENCES_PERF=1`), steps `timing: windows opened / items set /
  slowest fence placed and shown / layout applied`; `tools/perf/measure-scale.ps1 [-Exe] [-Out] [-StartOnly] [-SkipGame]`.

- [ ] **Step 1: Implement.** Write this patch to `m37-2-app.patch` and apply it:

```diff
diff --git a/src/NeoFences.App/AppPaths.cs b/src/NeoFences.App/AppPaths.cs
index 06bdb09..3e609ff 100644
--- a/src/NeoFences.App/AppPaths.cs
+++ b/src/NeoFences.App/AppPaths.cs
@@ -20,4 +20,7 @@ public static class AppPaths
     public static string CoversDirectory { get; } = Path.Combine(DataDirectory, "covers");
 
     public static string SiteIconsDirectory { get; } = Path.Combine(CoversDirectory, "sites");
+
+    /// <summary>The icon and name cache (M37, ADR-058): NeoFences' own PNGs and their index; safe to delete (it fills again).</summary>
+    public static string IconCacheDirectory { get; } = Path.Combine(DataDirectory, "cache", "icons");
 }
diff --git a/src/NeoFences.App/FenceHost.Collect.cs b/src/NeoFences.App/FenceHost.Collect.cs
index 15c4461..cb5d31f 100644
--- a/src/NeoFences.App/FenceHost.Collect.cs
+++ b/src/NeoFences.App/FenceHost.Collect.cs
@@ -81,6 +81,7 @@ public sealed partial class FenceHost
     private void SetCollectPaused()
     {
         foreach (var (_, lister) in _collectListers.Values) lister.SetPaused(_gameMode || _paused);
+        if (!_gameMode && !_paused && _collectPending.Count > 0) _collectTimer?.Start(); // M37: arrivals that waited for the game
     }
 
     private bool ReleaseCollectForRemoval(nint handle) => _collectListers.Values.Aggregate(false, (released, entry) => entry.Lister.ReleaseForRemoval(handle) | released);
@@ -114,7 +115,11 @@ public sealed partial class FenceHost
     /// </summary>
     private void OnCollectTick()
     {
-        if (_gameMode || _paused) return;
+        if (_gameMode || _paused)
+        {
+            _collectTimer?.Stop(); // M37 (truly idle): no 500 ms ticks during a game; SetCollectPaused starts it again
+            return;
+        }
         var collected = new List<string>();
         foreach (var (source, pending) in _collectPending.Where(entry => Clock.Elapsed - entry.Value.LastAt >= CollectSettle).ToList())
         {
diff --git a/src/NeoFences.App/FenceHost.Perf.cs b/src/NeoFences.App/FenceHost.Perf.cs
new file mode 100644
index 0000000..0469d1e
--- /dev/null
+++ b/src/NeoFences.App/FenceHost.Perf.cs
@@ -0,0 +1,84 @@
+using System.Diagnostics;
+using System.Windows.Media;
+using System.Windows.Threading;
+using NeoFences.Core.Lifecycle;
+using Serilog;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// Timing marks for the measurements (M37, spec §1, ADR-058): "fences shown" once every window is placed and drawn,
+/// "icons settled" the first time no icon request waits after that; with <c>NEOFENCES_PERF=1</c> also frame statistics
+/// every 2 s (it keeps WPF drawing every frame, so it is for measuring only). Read back by <see cref="PerfLog"/>.
+/// </summary>
+public sealed partial class FenceHost
+{
+    private static readonly DateTime ProcessStarted = Process.GetCurrentProcess().StartTime;
+    private bool _fencesShownLogged, _iconsSettledLogged;
+
+    private static long SinceStartMs => (long)(DateTime.Now - ProcessStarted).TotalMilliseconds;
+
+    /// <summary>After <see cref="Start"/>: once everything waiting to load and draw has run (the windows' Loaded included).</summary>
+    private void MarkFencesShown()
+    {
+        _iconLoader.Settled += OnIconsSettled;
+        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
+        {
+            _fencesShownLogged = true;
+            Log.Information("{Mark:l}", PerfLog.FencesShownText(SinceStartMs, _windows.Count));
+            if (_iconLoader.Pending == 0) OnIconsSettled(_iconLoader.Requested, _iconLoader.FromCache); // all from the cache before the windows were drawn
+            StartAfterFencesShown();
+        });
+        StartFrameStats();
+    }
+
+    /// <summary>
+    /// M37 (spec §5, fences first): work the fences do not need to show waits until they are on screen — the old library
+    /// fence's folder, the target checks and watching (Missing marks fill in a moment later), the wallpaper colour.
+    /// </summary>
+    private void StartAfterFencesShown()
+    {
+        EnsureLibraryLister();
+        StartWatching(); // M18: states fill in as the checks finish (spec §4)
+        if (Appearance.WallpaperAccent) UpdateAccents(); // M14: the accent is read once at start, then on wallpaper changes
+    }
+
+    /// <summary>A step of the start, for the measurement (not read back: it shows where the time goes).</summary>
+    private static void StepMark(string step) => Log.Information("timing: {Step:l} after {Ms} ms", step, SinceStartMs);
+
+    private void OnIconsSettled(int requests, int fromCache)
+    {
+        if (!_fencesShownLogged || _iconsSettledLogged) return;
+        _iconsSettledLogged = true;
+        Log.Information("{Mark:l}", PerfLog.IconsSettledText(SinceStartMs, requests, fromCache));
+    }
+
+    /// <summary>NEOFENCES_PERF=1 only: the longest gap between frames and how many took over 33 ms, every 2 s.</summary>
+    private void StartFrameStats()
+    {
+        if (Environment.GetEnvironmentVariable("NEOFENCES_PERF") != "1") return;
+        var last = TimeSpan.Zero;
+        var (frames, slow, worst) = (0, 0, 0.0);
+        CompositionTarget.Rendering += (_, args) =>
+        {
+            var at = ((RenderingEventArgs)args).RenderingTime;
+            if (at == last) return; // the same frame reported twice
+            if (last != TimeSpan.Zero)
+            {
+                var gap = (at - last).TotalMilliseconds;
+                frames++;
+                worst = Math.Max(worst, gap);
+                if (gap > 33) slow++;
+            }
+            last = at;
+        };
+        var report = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
+        report.Tick += (_, _) =>
+        {
+            if (frames > 0) Log.Information("{Mark:l}", PerfLog.FramesText(frames, worst, slow));
+            (frames, slow, worst) = (0, 0, 0.0);
+        };
+        report.Start();
+        Log.Information("perf mode: frame statistics every 2 s (NEOFENCES_PERF=1)");
+    }
+}
diff --git a/src/NeoFences.App/FenceHost.Watching.cs b/src/NeoFences.App/FenceHost.Watching.cs
index 6516ec9..992fd74 100644
--- a/src/NeoFences.App/FenceHost.Watching.cs
+++ b/src/NeoFences.App/FenceHost.Watching.cs
@@ -260,7 +260,7 @@ public sealed partial class FenceHost
             if (_panelListers.TryGetValue(panel.Id, out var panelLister)) panelLister.Refresh(); // its panels list again (M21, M26)
         }
         CheckFence(window.FenceId);
-        window.ReloadIcons();
+        window.ReloadIcons(fresh: true); // M37: Refresh asks Windows again, past the icon cache
     }
 
     private void CheckFence(string fenceId) =>
diff --git a/src/NeoFences.App/FenceHost.Widgets.cs b/src/NeoFences.App/FenceHost.Widgets.cs
index 6864185..461e695 100644
--- a/src/NeoFences.App/FenceHost.Widgets.cs
+++ b/src/NeoFences.App/FenceHost.Widgets.cs
@@ -57,7 +57,14 @@ public sealed partial class FenceHost
     {
         ScheduleWidgetTick();
         var windows = WidgetWindows();
-        if (windows.Count == 0) return; // nobody can see a widget: no work at all
+        if (windows.Count == 0)
+        {
+            // M37 (truly idle): nobody can see a widget — the timer stops until one shows again. Every way a widget comes into
+            // view calls OnWidgetTick (game mode ending, unpause, quick-hide off, a roll-up opening, a tab switch, a time change).
+            _widgetTimer?.Stop();
+            return;
+        }
+        if (_widgetTimer is { IsEnabled: false } stopped) stopped.Start();
         var now = DateTime.Now;
         foreach (var window in windows) window.UpdateWidgets(now, _lastStats);
         var elapsed = Clock.Elapsed;
diff --git a/src/NeoFences.App/FenceHost.cs b/src/NeoFences.App/FenceHost.cs
index 08a1409..b271115 100644
--- a/src/NeoFences.App/FenceHost.cs
+++ b/src/NeoFences.App/FenceHost.cs
@@ -41,7 +41,7 @@ public sealed partial class FenceHost
     private readonly SystemMessageWindow _messages = new();
     private readonly Dictionary<string, FenceWindow> _windows = new(StringComparer.Ordinal);
     private readonly DispatcherTimer _saveTimer;
-    private readonly IconLoader _iconLoader = new(Dispatcher.CurrentDispatcher);
+    private readonly IconLoader _iconLoader = new(Dispatcher.CurrentDispatcher, new IconDiskCache(AppPaths.IconCacheDirectory)); // M37: the icon cache
     private readonly ShellWorker _shellWorker = new(); // recycling snapshots: off the UI thread, on STA (M3a review)
     private SpecialIconNotifications? _specialIcons;
     private bool _specialIconsDeferred;
@@ -158,10 +158,12 @@ public sealed partial class FenceHost
 
         RefreshMonitors();
         foreach (var box in FenceTabs.Boxes(_config)) OpenWindow(box); // one window per box (M9)
-        EnsureLibraryLister();
+        StepMark("windows opened"); // M37
         RefreshWindows(); // M26: panels list their folders from here
+        StepMark("items set"); // M37
         StartSpecialIconNotifications();
         ApplyLayout();
+        StepMark("layout applied"); // M37
         if (Current.IconsHidden) SetIconsHidden(true); // not in safe mode (M33 review I1)
         else if (_watchdog.IsIconsHiddenMarked)
         {
@@ -190,10 +192,9 @@ public sealed partial class FenceHost
         // M30: a fresh start says where NeoFences lives; Windows 11 may tuck a new tray icon behind the ^ arrow.
         _firstStartNotice = loaded.Source == ConfigLoadSource.Fresh && !loaded.IsReadOnly; // not for an older build on a newer config (final review I1); held while a game runs (M32 review I1)
         ShowFirstStartNotice(); // M31: or once a retried tray icon shows
-        StartWatching(); // M18: states fill in as the checks finish (spec §4)
         StartUpdates(); // M17: the first check a minute after start
-        if (Appearance.WallpaperAccent) UpdateAccents(); // M14: the accent is read once at start, then on wallpaper changes
         ScheduleSave();
+        MarkFencesShown(); // M37: the timing marks
     }
 
     /// <summary>
@@ -301,6 +302,7 @@ public sealed partial class FenceHost
         window.TabCycleRequested += step => CycleTab(window, step);
         window.SetTabs(FenceTabs.TabsOf(_config, box.Id), shown.Id);
         ApplyStyle(window); // M14
+        window.SetHoverPaused(_gameMode || !Current.FencesVisible); // M37
         window.SnapRect = (rect, edges) => SnapFence(window, rect, edges);
         window.MovedByUser += OnFenceMoved;
         window.RenameRequested += title => RenameFence(window, title);
@@ -448,9 +450,11 @@ public sealed partial class FenceHost
         {
             var (resolved, layout) = LayoutEngine.Resolve(_config, _monitors.Select(monitor => monitor.ToDisplayMonitor()).ToList());
             _config = resolved;
+            var slowest = (Ms: 0.0, Items: 0); // M37: the start's slowest fence to place and show
             foreach (var (fenceId, rect) in layout.Fences)
             {
                 if (!_windows.TryGetValue(fenceId, out var window)) continue;
+                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                 var monitor = _monitors.First(candidate => candidate.DeviceId == rect.Monitor);
                 window.Place(FencePlacement.ToPixels(rect, monitor));
                 if (!window.IsVisible && Current.FencesVisible)
@@ -464,7 +468,10 @@ public sealed partial class FenceHost
                     }
                     else FenceWindowChrome.SendToBack(window.Handle); // Show puts it above every app; fences live just above the desktop
                 }
+                var took = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
+                if (took > slowest.Ms) slowest = (took, window.ItemCount);
             }
+            if (!_fencesShownLogged && slowest.Ms > 0) Log.Information("timing: slowest fence placed and shown in {Ms:0} ms ({Items} items)", slowest.Ms, slowest.Items); // M37
         }
         catch (ArgumentException unusableDisplay)
         {
@@ -674,6 +681,7 @@ public sealed partial class FenceHost
         if (tab.IsLibrary) _libraryLister?.Refresh();
         else CheckFence(fenceId); // a fence becoming visible is checked again (spec §4)
         RefreshWindow(window);
+        OnWidgetTick(); // M37: a tab with widgets may have come to the front (the widget timer may be stopped)
         ScheduleSave();
     }
 
@@ -1199,6 +1207,12 @@ public sealed partial class FenceHost
         }
     }
 
+    /// <summary>M37 (truly idle): the fences' hover polls (roll-up, title bar on hover) stop in a game and while hidden or paused.</summary>
+    private void UpdateHoverPolls()
+    {
+        foreach (var window in _windows.Values) window.SetHoverPaused(_gameMode || !Current.FencesVisible);
+    }
+
     private void CheckGameMode()
     {
         var gameMode = GameModePolicy.IsGameActive(enabled: _config.Settings.GameMode, foreground: GameDetection.TakeSnapshot());
@@ -1214,6 +1228,7 @@ public sealed partial class FenceHost
         _libraryLister?.SetPaused(gameMode);
         SetPanelsPaused(gameMode); // M21, M26
         SetCollectPaused(); // M27
+        UpdateHoverPolls(); // M37
         if (!gameMode)
         {
             ApplyDeferredShellWork();
@@ -1273,6 +1288,7 @@ public sealed partial class FenceHost
         UpdatePeekHotkey();
         RefreshSettings(); // a hotkey that cannot be registered on resume shows in an open Settings (final review I5)
         _trayIcon?.SetTooltip(TrayTooltip());
+        UpdateHoverPolls(); // M37
         Log.Information("paused: {Paused}", paused);
         if (!paused) OnWidgetTick(); // M28: widgets right at once
     }
@@ -1413,6 +1429,7 @@ public sealed partial class FenceHost
         }
         if (Current.IconsHidden != iconsWereHidden) SetIconsHidden(Current.IconsHidden); // RunState decides, user-hidden icons included
         if (!hidden) _iconsHiddenByUser = false;
+        UpdateHoverPolls(); // M37
         Log.Information("quick-hide: {Hidden}", hidden);
         if (!hidden) OnWidgetTick(); // M28: widgets right at once
     }
diff --git a/src/NeoFences.App/FenceItemView.cs b/src/NeoFences.App/FenceItemView.cs
index 871c714..ad95459 100644
--- a/src/NeoFences.App/FenceItemView.cs
+++ b/src/NeoFences.App/FenceItemView.cs
@@ -211,6 +211,9 @@ public sealed class FenceItemView : INotifyPropertyChanged
     /// <summary>Counts icon requests (UI thread): a slower, older load (another size or target) never wins (final review I4).</summary>
     public int IconRequest { get; set; }
 
+    /// <summary>M37: an icon request waits (raised to the front when the item scrolls into view).</summary>
+    public bool IconPending { get; set; }
+
     public ImageSource? Icon
     {
         get;
diff --git a/src/NeoFences.App/FenceWindow.xaml b/src/NeoFences.App/FenceWindow.xaml
index d46c033..dbd54e9 100644
--- a/src/NeoFences.App/FenceWindow.xaml
+++ b/src/NeoFences.App/FenceWindow.xaml
@@ -155,6 +155,81 @@
                 </DataTrigger>
             </DataTemplate.Triggers>
         </DataTemplate>
+        <!-- M37: the cover tile (M12, M34), built only for items shown as covers. -->
+        <DataTemplate x:Key="TileTemplate">
+            <Grid x:Name="TileRoot" Width="{Binding TileWidth}" Height="{Binding TileHeight}">
+                                    <Border Margin="-2,1,-2,-3" CornerRadius="11" Background="{DynamicResource TileShadowFar}" IsHitTestVisible="False" />
+                                    <Border Margin="-1,1,-1,-1" CornerRadius="9" Background="{DynamicResource TileShadowNear}" IsHitTestVisible="False" />
+                                    <Border x:Name="TileGlow" CornerRadius="8">
+                                        <Border.Background>
+                                            <ImageBrush ImageSource="{Binding Glow}" Stretch="UniformToFill" />
+                                        </Border.Background>
+                                    </Border>
+                                    <Border x:Name="TileShade" CornerRadius="8">
+                                        <Border.Background>
+                                            <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
+                                                <GradientStop Color="#33000000" Offset="0" />
+                                                <GradientStop Color="#8C000000" Offset="1" />
+                                            </LinearGradientBrush>
+                                        </Border.Background>
+                                    </Border>
+                                    <Image x:Name="TileIcon" Source="{Binding Icon}" Width="{Binding IconDips}" Height="{Binding IconDips}" Margin="0,0,0,10" />
+                                    <Image x:Name="TileLogo" Source="{Binding Art}" Margin="14" Stretch="Uniform" Visibility="Collapsed" />
+                                    <Border x:Name="TilePoster" CornerRadius="8" Visibility="Collapsed">
+                                        <Border.Background>
+                                            <ImageBrush ImageSource="{Binding Art}" Stretch="UniformToFill" />
+                                        </Border.Background>
+                                    </Border>
+                                    <Border x:Name="TileName" VerticalAlignment="Bottom" CornerRadius="0,0,8,8" Padding="6,22,6,7" Opacity="0"
+                                            Visibility="{DynamicResource TileNameVisibility}" IsHitTestVisible="False">
+                                        <Border.Background>
+                                            <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
+                                                <GradientStop Color="#00000000" Offset="0" />
+                                                <GradientStop Color="#D9000000" Offset="1" />
+                                            </LinearGradientBrush>
+                                        </Border.Background>
+                                        <TextBlock Text="{Binding Label}" Foreground="White" FontSize="12" FontWeight="SemiBold" TextAlignment="Center"
+                                                   TextWrapping="Wrap" TextTrimming="CharacterEllipsis" MaxHeight="32" />
+                                    </Border>
+                                    <Border x:Name="TileRing" Margin="-3" CornerRadius="10" BorderThickness="2" BorderBrush="{DynamicResource ItemSelectedEdge}"
+                                            Visibility="Collapsed" IsHitTestVisible="False" />
+                                    <!-- A not-installed game shown as its cover (M22, final review I1): the same badge as on icons. -->
+                                    <Border x:Name="TileBadge" Visibility="Collapsed" HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="4"
+                                            Width="16" Height="16" CornerRadius="8" Background="#FFE8A33D" BorderBrush="#FF3A2A10" BorderThickness="1"
+                                            IsHitTestVisible="False">
+                                        <TextBlock Text="!" Foreground="#FF3A2A10" FontWeight="Bold" FontSize="11"
+                                                   HorizontalAlignment="Center" VerticalAlignment="Center" />
+                                    </Border>
+            </Grid>
+            <DataTemplate.Triggers>
+                <DataTrigger Binding="{Binding ArtKind}" Value="Poster">
+                    <Setter TargetName="TilePoster" Property="Visibility" Value="Visible" />
+                    <Setter TargetName="TileIcon" Property="Visibility" Value="Collapsed" />
+                    <Setter TargetName="TileGlow" Property="Visibility" Value="Collapsed" />
+                    <Setter TargetName="TileShade" Property="Visibility" Value="Collapsed" />
+                </DataTrigger>
+                <!-- A cover lifts under the pointer and shows its name; selected, it gets the accent ring (M34 style A). -->
+                <DataTrigger Binding="{Binding IsMouseOver, RelativeSource={RelativeSource AncestorType=ListBoxItem}}" Value="True">
+                    <Setter TargetName="TileRoot" Property="Margin" Value="0,-2,0,2" />
+                    <Setter TargetName="TileName" Property="Opacity" Value="1" />
+                </DataTrigger>
+                <DataTrigger Binding="{Binding IsSelected, RelativeSource={RelativeSource AncestorType=ListBoxItem}}" Value="True">
+                    <Setter TargetName="TileRing" Property="Visibility" Value="Visible" />
+                    <Setter TargetName="TileName" Property="Opacity" Value="1" />
+                </DataTrigger>
+                <DataTrigger Binding="{Binding ArtKind}" Value="Logo">
+                    <Setter TargetName="TileLogo" Property="Visibility" Value="Visible" />
+                    <Setter TargetName="TileIcon" Property="Visibility" Value="Collapsed" />
+                </DataTrigger>
+                <DataTrigger Binding="{Binding State}" Value="Missing">
+                    <Setter TargetName="TileRoot" Property="Opacity" Value="0.45" />
+                    <Setter TargetName="TileBadge" Property="Visibility" Value="Visible" />
+                </DataTrigger>
+                <DataTrigger Binding="{Binding State}" Value="Unavailable">
+                    <Setter TargetName="TileRoot" Property="Opacity" Value="0.45" />
+                </DataTrigger>
+            </DataTemplate.Triggers>
+        </DataTemplate>
         <!-- M26: a folder panel fills its span (or, alone and set to fill, the whole fence) and takes its own clicks; built only for panels (M28). -->
         <DataTemplate x:Key="PanelTemplate">
             <Grid Width="{Binding WidgetWidth}" Height="{Binding WidgetHeight}">
@@ -365,61 +440,26 @@
                                     <!-- Game tile (M12, M34 style A): a rounded 2:3 cover with a soft shadow; without art, the glow tile (the icon's
                                          colours as a backdrop, the icon in the middle). Hover lifts it, selection rings it in the accent; on a fence
                                          with labels on hover the name fades in over the bottom (style B's overlay). -->
-                                    <Grid x:Name="Tile" Visibility="Collapsed" HorizontalAlignment="Center" Width="{Binding TileWidth}" Height="{Binding TileHeight}">
-                                        <Border Margin="-2,1,-2,-3" CornerRadius="11" Background="{DynamicResource TileShadowFar}" IsHitTestVisible="False" />
-                                        <Border Margin="-1,1,-1,-1" CornerRadius="9" Background="{DynamicResource TileShadowNear}" IsHitTestVisible="False" />
-                                        <Border x:Name="TileGlow" CornerRadius="8">
-                                            <Border.Background>
-                                                <ImageBrush ImageSource="{Binding Glow}" Stretch="UniformToFill" />
-                                            </Border.Background>
-                                        </Border>
-                                        <Border x:Name="TileShade" CornerRadius="8">
-                                            <Border.Background>
-                                                <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
-                                                    <GradientStop Color="#33000000" Offset="0" />
-                                                    <GradientStop Color="#8C000000" Offset="1" />
-                                                </LinearGradientBrush>
-                                            </Border.Background>
-                                        </Border>
-                                        <Image x:Name="TileIcon" Source="{Binding Icon}" Width="{Binding IconDips}" Height="{Binding IconDips}" Margin="0,0,0,10" />
-                                        <Image x:Name="TileLogo" Source="{Binding Art}" Margin="14" Stretch="Uniform" Visibility="Collapsed" />
-                                        <Border x:Name="TilePoster" CornerRadius="8" Visibility="Collapsed">
-                                            <Border.Background>
-                                                <ImageBrush ImageSource="{Binding Art}" Stretch="UniformToFill" />
-                                            </Border.Background>
-                                        </Border>
-                                        <Border x:Name="TileName" VerticalAlignment="Bottom" CornerRadius="0,0,8,8" Padding="6,22,6,7" Opacity="0"
-                                                Visibility="{DynamicResource TileNameVisibility}" IsHitTestVisible="False">
-                                            <Border.Background>
-                                                <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
-                                                    <GradientStop Color="#00000000" Offset="0" />
-                                                    <GradientStop Color="#D9000000" Offset="1" />
-                                                </LinearGradientBrush>
-                                            </Border.Background>
-                                            <TextBlock Text="{Binding Label}" Foreground="White" FontSize="12" FontWeight="SemiBold" TextAlignment="Center"
-                                                       TextWrapping="Wrap" TextTrimming="CharacterEllipsis" MaxHeight="32" />
-                                        </Border>
-                                        <Border x:Name="TileRing" Margin="-3" CornerRadius="10" BorderThickness="2" BorderBrush="{DynamicResource ItemSelectedEdge}"
-                                                Visibility="Collapsed" IsHitTestVisible="False" />
-                                        <!-- A not-installed game shown as its cover (M22, final review I1): the same badge as on icons. -->
-                                        <Border x:Name="TileBadge" Visibility="Collapsed" HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="4"
-                                                Width="16" Height="16" CornerRadius="8" Background="#FFE8A33D" BorderBrush="#FF3A2A10" BorderThickness="1"
-                                                IsHitTestVisible="False">
-                                            <TextBlock Text="!" Foreground="#FF3A2A10" FontWeight="Bold" FontSize="11"
-                                                       HorizontalAlignment="Center" VerticalAlignment="Center" />
-                                        </Border>
-                                    </Grid>
+                                    <!-- M37: built only for items shown as covers (the TileTemplate resource): lighter items for everything else. -->
+                                    <ContentControl x:Name="Tile" Content="{Binding}" Focusable="False" IsTabStop="False" HorizontalAlignment="Center" Visibility="Collapsed" />
                                     <!-- M25 widgets, M26 panels: their own templates, built only for those elements (M28: lighter items). -->
                                     <!-- Collapsed for plain items: without a template it would print the item's type name. -->
                                     <ContentControl x:Name="Extra" Content="{Binding}" Focusable="False" IsTabStop="False" HorizontalAlignment="Center" Visibility="Collapsed" />
                                     <TextBlock x:Name="Label" Margin="0,4,0,0" Text="{Binding Label}" Foreground="{DynamicResource FenceText}" FontSize="12"
                                                Visibility="{DynamicResource LabelVisibility}"
                                                TextAlignment="Center" TextWrapping="Wrap" TextTrimming="CharacterEllipsis"
-                                               MaxHeight="32" Effect="{DynamicResource LabelShadow}" />
+                                               MaxHeight="32" Effect="{DynamicResource LabelShadow}">
+                                        <!-- M37: the label with its shadow is drawn once into a bitmap and reused while the list scrolls
+                                             or the fence moves (an effect per label redrawn every frame stuttered at 200 items). -->
+                                        <TextBlock.CacheMode>
+                                            <BitmapCache SnapsToDevicePixels="True" />
+                                        </TextBlock.CacheMode>
+                                    </TextBlock>
                                 </StackPanel>
                                 <DataTemplate.Triggers>
                                     <DataTrigger Binding="{Binding IsTile}" Value="True">
                                         <Setter TargetName="IconGrid" Property="Visibility" Value="Collapsed" />
+                                        <Setter TargetName="Tile" Property="ContentTemplate" Value="{StaticResource TileTemplate}" />
                                         <Setter TargetName="Tile" Property="Visibility" Value="Visible" />
                                     </DataTrigger>
                                     <DataTrigger Binding="{Binding IsWidget}" Value="True">
@@ -436,38 +476,17 @@
                                         <Setter TargetName="Extra" Property="ContentTemplate" Value="{StaticResource PanelTemplate}" />
                                         <Setter TargetName="Extra" Property="Visibility" Value="Visible" />
                                     </DataTrigger>
-                                    <DataTrigger Binding="{Binding ArtKind}" Value="Poster">
-                                        <Setter TargetName="TilePoster" Property="Visibility" Value="Visible" />
-                                        <Setter TargetName="TileIcon" Property="Visibility" Value="Collapsed" />
-                                        <Setter TargetName="TileGlow" Property="Visibility" Value="Collapsed" />
-                                        <Setter TargetName="TileShade" Property="Visibility" Value="Collapsed" />
-                                    </DataTrigger>
                                     <!-- M34: hover lifts the tile a little and shows the name over it (labels on hover); selection rings it. -->
-                                    <DataTrigger Binding="{Binding IsMouseOver, RelativeSource={RelativeSource AncestorType=ListBoxItem}}" Value="True">
-                                        <Setter TargetName="Tile" Property="Margin" Value="0,-2,0,2" />
-                                        <Setter TargetName="TileName" Property="Opacity" Value="1" />
-                                    </DataTrigger>
-                                    <DataTrigger Binding="{Binding IsSelected, RelativeSource={RelativeSource AncestorType=ListBoxItem}}" Value="True">
-                                        <Setter TargetName="TileRing" Property="Visibility" Value="Visible" />
-                                        <Setter TargetName="TileName" Property="Opacity" Value="1" />
-                                    </DataTrigger>
-                                    <DataTrigger Binding="{Binding ArtKind}" Value="Logo">
-                                        <Setter TargetName="TileLogo" Property="Visibility" Value="Visible" />
-                                        <Setter TargetName="TileIcon" Property="Visibility" Value="Collapsed" />
-                                    </DataTrigger>
                                     <DataTrigger Binding="{Binding IsShortcut}" Value="True">
                                         <Setter TargetName="ShortcutArrow" Property="Visibility" Value="{DynamicResource ShortcutArrowVisibility}" />
                                     </DataTrigger>
                                     <DataTrigger Binding="{Binding State}" Value="Missing">
                                         <Setter TargetName="ItemIcon" Property="Opacity" Value="0.45" />
-                                        <Setter TargetName="Tile" Property="Opacity" Value="0.45" />
-                                        <Setter TargetName="TileBadge" Property="Visibility" Value="Visible" />
                                         <Setter TargetName="Label" Property="Opacity" Value="0.6" />
                                         <Setter TargetName="MissingBadge" Property="Visibility" Value="Visible" />
                                     </DataTrigger>
                                     <DataTrigger Binding="{Binding State}" Value="Unavailable">
                                         <Setter TargetName="ItemIcon" Property="Opacity" Value="0.45" />
-                                        <Setter TargetName="Tile" Property="Opacity" Value="0.45" />
                                         <Setter TargetName="Label" Property="Opacity" Value="0.6" />
                                     </DataTrigger>
                                 </DataTemplate.Triggers>
diff --git a/src/NeoFences.App/FenceWindow.xaml.cs b/src/NeoFences.App/FenceWindow.xaml.cs
index 3b53486..44c19ce 100644
--- a/src/NeoFences.App/FenceWindow.xaml.cs
+++ b/src/NeoFences.App/FenceWindow.xaml.cs
@@ -244,6 +244,10 @@ public partial class FenceWindow : Window
         LabelsOnHoverItem.Click += (_, _) => LabelModeRequested?.Invoke(LabelMode.OnHover);
         TitleBox.MaxLength = FenceEdits.MaxTitleLength; // the cut in FenceEdits.Rename never surprises the user (M2c review)
         ItemList.SelectionChanged += (_, _) => UpdateHoverLabel();
+        ItemList.AddHandler(System.Windows.Controls.ScrollViewer.ScrollChangedEvent, new System.Windows.Controls.ScrollChangedEventHandler((_, scrolled) =>
+        {
+            if (scrolled.VerticalChange != 0 || scrolled.ViewportHeightChange != 0) RaiseVisibleIcons(); // M37
+        }));
         ItemList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => UpdateHoverLabel()));
         HoverLabel.SizeChanged += (_, _) => PlaceHoverLabel(); // the bound name arrived or changed (final review I3)
         Loaded += (_, _) => ReloadIcons(); // placed on its monitor: one load at the right size (mixed DPI, M2b review)
@@ -271,7 +275,7 @@ public partial class FenceWindow : Window
         // Rolled up, the fence opens on hover or click (setting) and closes again shortly after the pointer leaves.
         _hoverTimer = new System.Windows.Threading.DispatcherTimer { Interval = HoverTick };
         _hoverTimer.Tick += (_, _) => OnHoverTick();
-        if (_rolledUp) _hoverTimer.Start();
+        UpdateHoverTimer();
         _heightAnimation = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
         _heightAnimation.Tick += (_, _) => StepHeight();
         Closed += (_, _) =>
@@ -335,6 +339,9 @@ public partial class FenceWindow : Window
         SetLabelMode(fence.Labels);
     }
 
+    /// <summary>Elements shown now (M37: the start's timing).</summary>
+    public int ItemCount => _items.Count;
+
     /// <summary>The shown tab is the Game Library (M12).</summary>
     public bool IsLibrary => _kind == FenceKind.Library;
 
@@ -755,9 +762,11 @@ public partial class FenceWindow : Window
     private int IconSizePx => (int)Math.Round(_iconSizeDips * VisualTreeHelper.GetDpi(this).DpiScaleX);
 
     /// <summary>An item's icon at its own size; a widget draws itself (M25), a panel shows its entries' icons (M26).</summary>
-    private void RequestIcon(FenceItemView view)
+    /// <param name="fresh">Past the icon cache (Refresh, special icons that change, M37).</param>
+    private void RequestIcon(FenceItemView view, bool fresh = false)
     {
-        if (!view.IsWidget && !view.IsPanel && ItemKinds.Of(view.Target) != ItemKind.Widget) _iconLoader.Request(view, PxOf(view)); // M28: an unknown widget kind has no icon to ask Windows for
+        // M37 (spec §4): what can be seen now goes first; the rest after, raised when it scrolls into view.
+        if (!view.IsWidget && !view.IsPanel && ItemKinds.Of(view.Target) != ItemKind.Widget) _iconLoader.Request(view, PxOf(view), urgent: InView(view), fresh: fresh); // M28: an unknown widget kind has no icon to ask Windows for
     }
 
     /// <summary>A panel's requests go to the host; its rows ask for icons as they come into view (M26).</summary>
@@ -1119,6 +1128,7 @@ public partial class FenceWindow : Window
         if (!_rolledUp || !_expansion.Click()) return false;
         AnimateHeight(_fullHeightPx);
         ItemsShownChanged?.Invoke(); // M25: widgets update at once
+        RaiseVisibleIcons(); // M37
         return true;
     }
 
@@ -1169,6 +1179,7 @@ public partial class FenceWindow : Window
         if (!_expansion.Tick(inside)) return;
         AnimateHeight(_expansion.Expanded ? _fullHeightPx : RolledUpHeightPx);
         ItemsShownChanged?.Invoke(); // M25
+        RaiseVisibleIcons(); // M37
     }
 
     /// <summary>Colours for Windows' light or dark app mode (M2c: fences follow Windows).</summary>
@@ -1286,10 +1297,20 @@ public partial class FenceWindow : Window
     /// <summary>The hover poll runs while it is needed: rolled up (M5), or a title bar shown on hover (M36).</summary>
     private void UpdateHoverTimer()
     {
-        if (_rolledUp || _titleOnHover) _hoverTimer.Start();
+        if (!_hoverPaused && (_rolledUp || _titleOnHover)) _hoverTimer.Start();
         else _hoverTimer.Stop();
     }
 
+    private bool _hoverPaused;
+
+    /// <summary>M37 (truly idle): no hover poll in game mode or while the fences are hidden or paused.</summary>
+    public void SetHoverPaused(bool paused)
+    {
+        if (paused == _hoverPaused) return;
+        _hoverPaused = paused;
+        UpdateHoverTimer();
+    }
+
     /// <summary>
     /// M36: a title bar shown on hover is there while the pointer is over the fence, and whenever the fence is rolled up, renamed,
     /// dragged or its menu is open; otherwise it fades out (its row keeps its place, so nothing moves under the pointer).
@@ -1418,13 +1439,40 @@ public partial class FenceWindow : Window
     /// <summary>The Recycle Bin turned full or empty, or another special icon changed (M8c).</summary>
     public void ReloadSpecialIcons()
     {
-        foreach (var view in _items.Where(view => view.Target.StartsWith("::", StringComparison.Ordinal))) RequestIcon(view);
+        foreach (var view in _items.Where(view => view.Target.StartsWith("::", StringComparison.Ordinal))) RequestIcon(view, fresh: true); // M37: past the icon cache
     }
 
     /// <summary>New size or DPI, or "Refresh": every icon and name is requested again in place; selection stays (M2c review carry-over).</summary>
-    public void ReloadIcons()
+    public void ReloadIcons(bool fresh = false)
     {
-        foreach (var view in _items) RequestIcon(view);
+        foreach (var view in _items) RequestIcon(view, fresh);
+    }
+
+    /// <summary>
+    /// M37: whether an item can be seen now — the fence shown and open, the item inside the list's view. Before the first layout,
+    /// the first ones count as seen.
+    /// </summary>
+    private bool InView(FenceItemView view)
+    {
+        if (!IsVisible || (_rolledUp && !_expansion.Expanded)) return false;
+        if (ItemList.ItemContainerGenerator.ContainerFromItem(view) is not FrameworkElement container || container.ActualHeight <= 0 || ItemList.ActualHeight <= 0)
+            return _items.IndexOf(view) < 48; // ponytail: not laid out yet, the first rows by index
+        var top = container.TranslatePoint(new Point(0, 0), ItemList).Y;
+        return top + container.ActualHeight >= 0 && top <= ItemList.ActualHeight;
+    }
+
+    private bool _raiseQueued;
+
+    /// <summary>M37: items that came into view (a scroll, an unroll) whose icons still wait go to the front of the queue.</summary>
+    private void RaiseVisibleIcons()
+    {
+        if (_raiseQueued) return;
+        _raiseQueued = true;
+        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
+        {
+            _raiseQueued = false;
+            foreach (var view in _items.Where(view => view.IconPending && InView(view))) _iconLoader.Request(view, PxOf(view), urgent: true);
+        });
     }
 
     /// <summary>Starts renaming the fence title (menu, or a freshly drawn fence).</summary>
diff --git a/src/NeoFences.App/IconDiskCache.cs b/src/NeoFences.App/IconDiskCache.cs
new file mode 100644
index 0000000..649fb61
--- /dev/null
+++ b/src/NeoFences.App/IconDiskCache.cs
@@ -0,0 +1,132 @@
+using System.IO;
+using System.Windows.Media.Imaging;
+using NeoFences.Core.Items;
+using Serilog;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// The icon and name cache on disk (M37, spec §3, ADR-058): PNGs and <c>index.json</c> in <c>cache\icons\</c>, NeoFences'
+/// own folder. Used by the icon loader's workers (thread-safe); a file that cannot be read or written only costs a fresh
+/// load, never a failure. The decisions (key, stamp, pruning) are <see cref="IconCache"/>'s.
+/// </summary>
+public sealed class IconDiskCache(string directory)
+{
+    private readonly Lock _lock = new();
+    private readonly Lock _saveLock = new(); // one index write at a time (two workers can settle together)
+    private IconCacheIndex _index = new();
+    private bool _dirty, _loaded;
+
+    private string IndexPath => Path.Combine(directory, "index.json");
+
+    /// <summary>Reads the index and drops what is too old or too much (start, off the UI thread).</summary>
+    public void LoadAndPrune()
+    {
+        var (index, failure) = IconCacheIndex.Load(IndexPath);
+        if (failure is not null) Log.Warning(failure, "icon cache: the index could not be read; the cache starts over");
+        var (kept, delete) = IconCache.Prune(index, DateTimeOffset.Now);
+        foreach (var file in delete)
+        {
+            try
+            {
+                File.Delete(Path.Combine(directory, Path.GetFileName(file))); // NeoFences' own cache file only
+            }
+            catch (Exception deleteFailure) when (deleteFailure is IOException or UnauthorizedAccessException)
+            {
+                Log.Warning(deleteFailure, "icon cache: {File} could not be removed", file);
+            }
+        }
+        lock (_lock)
+        {
+            _index = kept;
+            _loaded = true;
+            _dirty = delete.Count > 0 || failure is not null;
+        }
+        if (delete.Count > 0) Log.Information("icon cache: {Count} old entries removed", delete.Count);
+    }
+
+    /// <summary>The cached icon and its entry (marked used now), or null.</summary>
+    public (BitmapSource Icon, IconCacheEntry Entry)? TryGet(string key)
+    {
+        IconCacheEntry? entry;
+        lock (_lock)
+        {
+            if (!_loaded || !_index.Entries.TryGetValue(key, out entry)) return null;
+        }
+        try
+        {
+            var icon = new BitmapImage();
+            icon.BeginInit();
+            icon.CacheOption = BitmapCacheOption.OnLoad; // the file is not kept open
+            icon.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
+            icon.UriSource = new Uri(Path.Combine(directory, entry.File));
+            icon.EndInit();
+            icon.Freeze();
+            lock (_lock)
+            {
+                _index = _index.Touch([key], DateTimeOffset.Now);
+                _dirty = true;
+            }
+            return (icon, entry);
+        }
+        catch (Exception failure) when (failure is not OutOfMemoryException)
+        {
+            lock (_lock)
+            {
+                _index = _index with { Entries = _index.Entries.Where(pair => pair.Key != key).ToDictionary(StringComparer.Ordinal) };
+                _dirty = true;
+            }
+            Log.Debug(failure, "icon cache: {File} unreadable; loaded fresh", entry.File);
+            return null;
+        }
+    }
+
+    /// <summary>Stores a fresh icon (and the display name, when one was asked for) under its key.</summary>
+    public void Put(string key, BitmapSource icon, string? name, IconStamp? stamp)
+    {
+        try
+        {
+            Directory.CreateDirectory(directory);
+            var file = IconCache.FileNameOf(key);
+            var path = Path.Combine(directory, file);
+            var temp = path + ".tmp";
+            var encoder = new PngBitmapEncoder();
+            encoder.Frames.Add(BitmapFrame.Create(icon));
+            using (var stream = File.Create(temp)) encoder.Save(stream);
+            File.Move(temp, path, overwrite: true);
+            var entry = new IconCacheEntry { File = file, Name = name, Stamp = stamp, LastUsed = DateTimeOffset.Now, Bytes = new FileInfo(path).Length };
+            lock (_lock)
+            {
+                _index = _index.With(key, entry);
+                _dirty = true;
+            }
+        }
+        catch (Exception failure) when (failure is not OutOfMemoryException)
+        {
+            Log.Debug(failure, "icon cache: {Key} not stored", key); // the icon shows; the next start loads it fresh
+        }
+    }
+
+    /// <summary>Writes the index when it changed (after the icons settle, and at exit).</summary>
+    public void SaveIfChanged()
+    {
+        lock (_saveLock)
+        {
+            IconCacheIndex index;
+            lock (_lock)
+            {
+                if (!_dirty) return;
+                index = _index;
+                _dirty = false;
+            }
+            try
+            {
+                index.Save(IndexPath);
+            }
+            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
+            {
+                Log.Warning(failure, "icon cache: the index could not be written; the icons load fresh next time");
+            }
+        }
+    }
+}
diff --git a/src/NeoFences.App/IconLoader.cs b/src/NeoFences.App/IconLoader.cs
index 203bd39..9c4b13d 100644
--- a/src/NeoFences.App/IconLoader.cs
+++ b/src/NeoFences.App/IconLoader.cs
@@ -17,58 +17,205 @@ namespace NeoFences.App;
 /// </summary>
 public sealed class IconLoader : IDisposable
 {
-    private sealed record LoadRequest(FenceItemView View, int Number, int SizePx, string Target, bool WantsName, ItemIcon? OwnIcon);
+    private sealed record LoadRequest(FenceItemView View, int Number, int SizePx, string Target, bool WantsName, ItemIcon? OwnIcon, bool Fresh);
 
-    private readonly BlockingCollection<LoadRequest> _requests = new();
+    // M37 (spec §4): what can be seen first, the rest after (TakeFromAny takes from the first queue that has something).
+    private readonly BlockingCollection<LoadRequest> _urgent = new(), _later = new();
     private readonly Dispatcher _uiDispatcher;
 
-    public IconLoader(Dispatcher uiDispatcher)
+    // M37 (spec §3): what was loaded fresh this run — a key is asked of the shell once per run, until its stamp changes or a
+    // refresh asks again — and the cache on disk, read before the shell.
+    private readonly ConcurrentDictionary<string, (BitmapSource? Icon, string? Name, IconStamp? Stamp)> _session = new(StringComparer.Ordinal);
+    private readonly IconDiskCache? _disk;
+    private readonly ManualResetEventSlim _diskReady = new(false);
+
+    public IconLoader(Dispatcher uiDispatcher, IconDiskCache? disk = null)
     {
         _uiDispatcher = uiDispatcher;
+        _disk = disk;
         // Two STA workers: one slow thumbnail (a big video, a network shortcut) no longer holds up every other icon (M2b review).
         for (var workerNumber = 1; workerNumber <= 2; workerNumber++)
         {
-            var worker = new Thread(Work) { IsBackground = true, Name = $"NeoFences icon loader {workerNumber}" };
+            var first = workerNumber == 1;
+            var worker = new Thread(() =>
+            {
+                if (first)
+                {
+                    try
+                    {
+                        _disk?.LoadAndPrune(); // M37: the index first (a few ms), so the first icons can come from it
+                    }
+                    catch (Exception failure) when (failure is not OutOfMemoryException)
+                    {
+                        Log.Warning(failure, "icon cache unavailable; icons load fresh");
+                    }
+                    _diskReady.Set();
+                }
+                _diskReady.Wait();
+                Work();
+            }) { IsBackground = true, Name = $"NeoFences icon loader {workerNumber}" };
             worker.SetApartmentState(ApartmentState.STA);
             worker.Start();
         }
     }
 
+    /// <summary>
+    /// M37: raised on the UI thread each time no request is waiting any more, with the requests so far and how many the cache
+    /// answered (the "icons settled" timing mark).
+    /// </summary>
+    public event Action<int, int>? Settled;
+
+    private int _pending, _requested; // UI thread only
+
+    /// <summary>Requests still waiting (UI thread; M37: "icons settled" before the fences were shown).</summary>
+    public int Pending => _pending;
+
+    /// <summary>Requests so far (UI thread).</summary>
+    public int Requested => _requested;
+
+    /// <summary>Requests answered from the icon cache on disk so far (M37).</summary>
+    public int FromCache { get; private set; }
+
     /// <summary>Called on the UI thread. With two workers an older request can finish last: only the newest is applied.</summary>
-    public void Request(FenceItemView view, int sizePx)
+    /// <param name="urgent">The item can be seen now (M37): before the others.</param>
+    /// <param name="fresh">Ask the shell even when the icon is known (Refresh, the Recycle Bin's full / empty icon).</param>
+    public void Request(FenceItemView view, int sizePx, bool urgent = true, bool fresh = false)
     {
         view.IconRequest++;
-        _requests.TryAdd(new LoadRequest(view, view.IconRequest, sizePx, view.Target, WantsName: view.OwnName is null, view.OwnIcon));
+        view.IconPending = true;
+        _pending++;
+        _requested++;
+        (urgent ? _urgent : _later).TryAdd(new LoadRequest(view, view.IconRequest, sizePx, view.Target, WantsName: view.OwnName is null, view.OwnIcon, fresh));
     }
 
     private void Work()
     {
-        foreach (var request in _requests.GetConsumingEnumerable())
+        while (true)
         {
+            LoadRequest request;
             try
             {
-                var kind = ItemKinds.Of(request.Target);
-                // A share or mapped network drive is asked first, at most 2 s (M19 R7, review I2): a dead one would hold this
-                // worker in the shell for minutes.
-                var reachable = kind != ItemKind.Path || !TargetProbe.MayHang(request.Target)
-                                || TargetProbe.Check(request.Target).State == TargetState.Ok;
-                var label = !request.WantsName ? null
-                    : kind == ItemKind.Website ? ItemKinds.WebsiteName(request.Target)
-                    : reachable ? ShellItems.TryGetDisplayName(request.Target) : null;
-                var icon = OwnIcon(request) ?? (reachable ? TargetIcon(request, kind) : null) ?? GenericIcon(request, kind);
-                _uiDispatcher.BeginInvoke(() =>
-                {
-                    if (request.Number != request.View.IconRequest) return; // a newer request (size, target, icon) is on its way
-                    if (label is not null && request.View.OwnName is null) request.View.Label = label;
-                    if (icon is not null) request.View.Icon = icon;
-                });
+                BlockingCollection<LoadRequest>.TakeFromAny([_urgent, _later], out request!);
+            }
+            catch (Exception ended) when (ended is ArgumentException or InvalidOperationException or ObjectDisposedException)
+            {
+                return; // exit: the queues were completed
+            }
+            try
+            {
+                Load(request);
             }
             catch (Exception failure)
             {
                 // One bad item (odd bitmap, huge thumbnail) must not kill the app and crash-loop it (M2b review I5).
                 Log.Warning(failure, "could not load the icon of {Target}", request.Target);
+                _uiDispatcher.BeginInvoke(() => Finish(request));
+            }
+        }
+    }
+
+    /// <summary>
+    /// This run's icon, else the cached one (shown at once), else — or behind a cached one whose source may have changed (M37
+    /// spec §3) — the shell's. A fresh icon that looks the same as the cached one is not applied again.
+    /// </summary>
+    private void Load(LoadRequest request)
+    {
+        var key = IconCache.KeyOf(request.Target, request.OwnIcon, request.SizePx);
+        var stamp = StampOf(request);
+        if (!request.Fresh && _session.TryGetValue(key, out var known) && known.Stamp == stamp)
+        {
+            Apply(request, known.Name, known.Icon, final: true, fromDisk: false);
+            return;
+        }
+        BitmapSource? shown = null;
+        if (!request.Fresh && _disk?.TryGet(key) is var (cachedIcon, entry))
+        {
+            shown = cachedIcon;
+            var stale = IconCache.NeedsFreshLoad(entry, stamp);
+            Apply(request, entry.Name, cachedIcon, final: !stale, fromDisk: true);
+            if (!stale)
+            {
+                _session[key] = (cachedIcon, entry.Name, stamp);
+                return;
+            }
+        }
+        var kind = ItemKinds.Of(request.Target);
+        // A share or mapped network drive is asked first, at most 2 s (M19 R7, review I2): a dead one would hold this
+        // worker in the shell for minutes.
+        var reachable = kind != ItemKind.Path || !TargetProbe.MayHang(request.Target)
+                        || TargetProbe.Check(request.Target).State == TargetState.Ok;
+        // M37: the name is kept with the icon, so it is asked even for an item with its own (another item may want it).
+        var label = kind == ItemKind.Website ? ItemKinds.WebsiteName(request.Target)
+            : reachable ? ShellItems.TryGetDisplayName(request.Target) : null;
+        var icon = OwnIcon(request) ?? (reachable ? TargetIcon(request, kind) : null) ?? GenericIcon(request, kind);
+        _session[key] = (icon, label, stamp);
+        if (icon is not null && reachable) _disk?.Put(key, icon, label, stamp); // an unreachable target's generic icon is not kept
+        Apply(request, label, shown is not null && icon is not null && SamePixels(shown, icon) ? null : icon, final: true, fromDisk: false);
+    }
+
+    private void Apply(LoadRequest request, string? label, BitmapSource? icon, bool final, bool fromDisk)
+    {
+        _uiDispatcher.BeginInvoke(() =>
+        {
+            if (fromDisk) FromCache++;
+            if (request.Number == request.View.IconRequest) // a newer request (size, target, icon) is on its way otherwise
+            {
+                if (label is not null && request.WantsName && request.View.OwnName is null) request.View.Label = label;
+                if (icon is not null) request.View.Icon = icon;
             }
+            if (final) Finish(request);
+        });
+    }
+
+    /// <summary>One request finished (UI thread): when none waits any more, <see cref="Settled"/> and the cache index is saved.</summary>
+    private void Finish(LoadRequest request)
+    {
+        if (request.Number == request.View.IconRequest) request.View.IconPending = false;
+        if (--_pending != 0) return;
+        Settled?.Invoke(_requested, FromCache);
+        if (_disk is { } disk) ThreadPool.QueueUserWorkItem(_ => disk.SaveIfChanged());
+    }
+
+    /// <summary>
+    /// What the icon comes from, as a stamp (M37): the item's own icon file or picture, else the target file or folder; null
+    /// for Start apps, shell items, websites, missing files and network paths (no stamp is read where the share may hang).
+    /// </summary>
+    private static IconStamp? StampOf(LoadRequest request)
+    {
+        var file = request.OwnIcon is { File: { Length: > 0 } iconFile } ? iconFile
+            : request.OwnIcon is { Image: { Length: > 0 } image } ? Path.Combine(AppPaths.IconsDirectory, Path.GetFileName(image))
+            : ItemKinds.Of(request.Target) == ItemKind.Path ? request.Target : null;
+        if (file is null || TargetProbe.MayHang(file)) return null;
+        try
+        {
+            var info = new FileInfo(file);
+            if (info.Exists) return new IconStamp(info.LastWriteTimeUtc.Ticks, info.Length);
+            var folder = new DirectoryInfo(file);
+            return folder.Exists ? new IconStamp(folder.LastWriteTimeUtc.Ticks, 0) : null;
+        }
+        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
+        {
+            return null;
+        }
+    }
+
+    /// <summary>The same picture, give or take the rounding a PNG round trip brings to soft edges (M37).</summary>
+    private static bool SamePixels(BitmapSource first, BitmapSource second)
+    {
+        if (first.PixelWidth != second.PixelWidth || first.PixelHeight != second.PixelHeight) return false;
+        static byte[] Pixels(BitmapSource source)
+        {
+            var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
+            var bytes = new byte[converted.PixelWidth * converted.PixelHeight * 4];
+            converted.CopyPixels(bytes, converted.PixelWidth * 4, 0);
+            return bytes;
         }
+        var (a, b) = (Pixels(first), Pixels(second));
+        for (var index = 0; index < a.Length; index++)
+        {
+            if (Math.Abs(a[index] - b[index]) > 3) return false;
+        }
+        return true;
     }
 
     /// <summary>The item's own icon: one in a file, or a picture in NeoFences' icons folder. Null when it cannot be read (then the target's).</summary>
@@ -184,5 +331,10 @@ public sealed class IconLoader : IDisposable
         return icon;
     }
 
-    public void Dispose() => _requests.CompleteAdding();
+    public void Dispose()
+    {
+        _urgent.CompleteAdding();
+        _later.CompleteAdding();
+        _disk?.SaveIfChanged(); // M37: what this run learned
+    }
 }
diff --git a/tools/perf/measure-scale.ps1 b/tools/perf/measure-scale.ps1
new file mode 100644
index 0000000..b6b7950
--- /dev/null
+++ b/tools/perf/measure-scale.ps1
@@ -0,0 +1,190 @@
+# NeoFences performance at scale (M37, ADR-058): a 500-item / 50-fence setup on a backed-up copy of the data folder,
+# then a cold start (no icon cache, frame statistics on: scroll the big fence, drag and resize the medium one) and a warm
+# start (CPU over 60 s idle and 60 s in game mode). Everything is put back afterwards: the data folder, the startup entry,
+# and the installed NeoFences is started again. Needs PowerShell 7.5+ (it reads the log with NeoFences.Core.dll).
+# Moves the mouse: keep hands off while it runs. ASCII only.
+param(
+  [string]$Exe = (Join-Path $env:LOCALAPPDATA 'NeoFences.App\current\NeoFences.exe'),
+  [int]$Fences = 50,
+  [int]$Items = 500,
+  [int]$BigItems = 200,
+  [int]$MediumItems = 100,
+  [string]$Out = (Join-Path ([IO.Path]::GetTempPath()) 'neofences-perf'),
+  [switch]$SkipGame,
+  [switch]$StartOnly # only the cold and warm start times (to compare builds, e.g. ready-to-run)
+)
+$ErrorActionPreference = 'Stop'
+$data = Join-Path $env:LOCALAPPDATA 'NeoFences'
+$installed = Join-Path $env:LOCALAPPDATA 'NeoFences.App\current\NeoFences.exe'
+$backup = Join-Path $Out 'data-backup'
+$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
+New-Item -ItemType Directory -Force $Out | Out-Null
+Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
+if (-not ('NeoFences.Core.Lifecycle.PerfLog' -as [type])) { Add-Type -Path (Join-Path (Split-Path $Exe) 'NeoFences.Core.dll') } # once per session
+if (-not ('NfPerf.W' -as [type])) { Add-Type -Namespace NfPerf -Name W -MemberDefinition @'
+[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
+[DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);
+[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
+[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder text, int max);
+[DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr data);
+delegate bool EnumProc(IntPtr h, IntPtr data);
+public struct RECT { public int Left, Top, Right, Bottom; }
+public static System.Collections.Generic.List<IntPtr> Fences() {
+  var found = new System.Collections.Generic.List<IntPtr>();
+  EnumWindows((h, _) => { var text = new System.Text.StringBuilder(64); GetWindowText(h, text, 64); if (text.ToString() == "NeoFences fence") found.Add(h); return true; }, IntPtr.Zero);
+  return found;
+}
+'@ }
+
+function Pause-Ms([int]$ms) { Start-Sleep -Milliseconds $ms }
+function Running { @(Get-Process NeoFences -ErrorAction SilentlyContinue) }
+function StopNeoFences {
+  # --exit is asked again every 5 s: a copy that has only just started ignores it.
+  $deadline = (Get-Date).AddSeconds(40)
+  while ((Running).Count -gt 0 -and (Get-Date) -lt $deadline) {
+    foreach ($path in @(Running | Select-Object -ExpandProperty Path -Unique)) { & $path --exit }
+    $wait = (Get-Date).AddSeconds(5); while ((Running).Count -gt 0 -and (Get-Date) -lt $wait) { Pause-Ms 300 }
+  }
+  if ((Running).Count -gt 0) { throw 'NeoFences did not exit' }
+}
+function LogSince([datetime]$since) {
+  # The newest two daily logs (a file NeoFences holds open keeps an old LastWriteTime, so it cannot pick them); the reader
+  # takes the last start or a time window out of them.
+  Get-ChildItem (Join-Path $data 'logs') -Filter 'neofences-*.log' -ErrorAction SilentlyContinue | Sort-Object Name |
+    Select-Object -Last 2 | ForEach-Object { Get-Content -LiteralPath $_.FullName }
+}
+function StartNeoFences([switch]$Perf) {
+  $since = Get-Date
+  $settledBefore = @(LogSince $since | Select-String -SimpleMatch 'timing: icons settled').Count # earlier runs' marks are in the same logs
+  $env:NEOFENCES_PERF = if ($Perf) { '1' } else { $null }
+  Start-Process $Exe | Out-Null
+  $env:NEOFENCES_PERF = $null
+  $deadline = (Get-Date).AddSeconds(90)
+  while ((Get-Date) -lt $deadline -and @(LogSince $since | Select-String -SimpleMatch 'timing: icons settled').Count -le $settledBefore) { Pause-Ms 500 }
+  Pause-Ms 1500
+  $since
+}
+function CpuSeconds { (Running | Measure-Object -Property { $_.TotalProcessorTime.TotalSeconds } -Sum).Sum }
+function FenceRect([string]$title) {
+  # Fences are found by their window title (they may sit under the desktop layer, out of UI Automation's top level),
+  # then told apart by their first text (the title).
+  $text = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
+  foreach ($handle in [NfPerf.W]::Fences()) {
+    $first = [System.Windows.Automation.AutomationElement]::FromHandle($handle).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $text)
+    if ($first -and $first.Current.Name -eq $title) { $rect = New-Object NfPerf.W+RECT; [void][NfPerf.W]::GetWindowRect($handle, [ref]$rect); return $rect }
+  }
+  $null
+}
+function Wheel([int]$x, [int]$y, [int]$notches, [int]$direction) {
+  [void][NfPerf.W]::SetCursorPos($x, $y); Pause-Ms 200
+  foreach ($notch in 1..$notches) { [NfPerf.W]::mouse_event(0x0800, 0, 0, 120 * $direction, [UIntPtr]::Zero); Pause-Ms 40 }
+}
+function Drag([int]$x, [int]$y, [int]$dx, [int]$dy) {
+  [void][NfPerf.W]::SetCursorPos($x, $y); Pause-Ms 200
+  [NfPerf.W]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
+  foreach ($step in 1..30) { [void][NfPerf.W]::SetCursorPos([int]($x + $dx * $step / 30), [int]($y + $dy * $step / 30)); Pause-Ms 30 }
+  [NfPerf.W]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero); Pause-Ms 500
+}
+function Targets([int]$count) {
+  $pool = [System.Collections.Generic.List[string]]::new()
+  $exes = @(Get-ChildItem "$env:WINDIR\System32" -Filter '*.exe' -ErrorAction SilentlyContinue | Select-Object -First 260 -ExpandProperty FullName)
+  $pictures = @(Get-ChildItem "$env:WINDIR\Web" -Recurse -Include '*.jpg', '*.png' -ErrorAction SilentlyContinue | Select-Object -First 40 -ExpandProperty FullName)
+  $folders = @(Get-ChildItem $env:ProgramFiles -Directory -ErrorAction SilentlyContinue | Select-Object -First 40 -ExpandProperty FullName)
+  $media = @(Get-ChildItem "$env:WINDIR\Media" -Filter '*.wav' -ErrorAction SilentlyContinue | Select-Object -First 40 -ExpandProperty FullName)
+  $apps = @((New-Object -ComObject Shell.Application).NameSpace('shell:AppsFolder').Items() | Select-Object -First 30 | ForEach-Object { "shell:AppsFolder\$($_.Path)" })
+  $sites = @('https://www.wikipedia.org', 'https://github.com', 'https://www.youtube.com', 'https://store.steampowered.com', 'https://www.reddit.com',
+    'https://learn.microsoft.com', 'https://www.bbc.com', 'https://news.ycombinator.com', 'https://www.twitch.tv', 'https://www.nexusmods.com')
+  $kinds = @($exes, $pictures, $folders, $media, $apps, $sites) | Where-Object { $_.Count -gt 0 }
+  $index = 0
+  while ($pool.Count -lt $count) {
+    $kind = $kinds[$index % $kinds.Count]; $pool.Add($kind[[int][Math]::Floor($index / $kinds.Count) % $kind.Count]); $index++
+  }
+  $pool
+}
+function NewItem([string]$target) { [ordered]@{ id = [guid]::NewGuid().ToString('N'); target = $target } }
+
+"===== NeoFences performance at scale: keep hands off the mouse (about 4 min) ====="
+if (Test-Path -LiteralPath $backup) { throw "a backup is already at ${backup}: put it back or remove it first" }
+StopNeoFences
+Copy-Item -LiteralPath $data -Destination $backup -Recurse
+$runValue = (Get-ItemProperty $runKey -ErrorAction SilentlyContinue).NeoFences
+"data backed up to $backup"
+$report = [System.Collections.Generic.List[string]]::new()
+try {
+  # The setup: the existing fences and items, plus new fences up to the counts (one big, one medium, the rest small).
+  $config = Get-Content -LiteralPath (Join-Path $data 'config.json') -Raw | ConvertFrom-Json -AsHashtable -DateKind String
+  $itemsDoc = Get-Content -LiteralPath (Join-Path $data 'items.json') -Raw | ConvertFrom-Json -AsHashtable -DateKind String
+  if (-not $itemsDoc.fences) { $itemsDoc.fences = [ordered]@{} }
+  $existingItems = ($itemsDoc.fences.Values | ForEach-Object { @($_).Count } | Measure-Object -Sum).Sum
+  $newFences = [Math]::Max(2, $Fences - @($config.fences).Count)
+  $smallTotal = [Math]::Max(0, $Items - $existingItems - $BigItems - $MediumItems)
+  $targets = @(Targets ($BigItems + $MediumItems + $smallTotal))
+  $next = 0
+  $fenceList = [System.Collections.Generic.List[object]]::new(); foreach ($fence in @($config.fences)) { $fenceList.Add($fence) }
+  foreach ($number in 1..$newFences) {
+    $title = if ($number -eq 1) { 'Perf big' } elseif ($number -eq 2) { 'Perf medium' } else { "Perf $number" }
+    $count = if ($number -eq 1) { $BigItems } elseif ($number -eq 2) { $MediumItems } else { [int][Math]::Floor($smallTotal / ($newFences - 2)) + ([int](($number - 3) -lt ($smallTotal % ($newFences - 2)))) }
+    $id = [guid]::NewGuid().ToString('N')
+    $fenceList.Add([ordered]@{ id = $id; title = $title })
+    $itemsDoc.fences[$id] = @(foreach ($slot in 1..$count) { if ($next -lt $targets.Count) { NewItem $targets[$next]; $next++ } })
+  }
+  $config.fences = $fenceList
+  $config | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $data 'config.json') -Encoding utf8NoBOM
+  $itemsDoc | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $data 'items.json') -Encoding utf8NoBOM
+  $report.Add("setup: $($fenceList.Count) fences, $($existingItems + $next) items ($Exe)")
+
+  # Cold start: no icon cache; frame statistics on.
+  Remove-Item -LiteralPath (Join-Path $data 'cache') -Recurse -Force -ErrorAction SilentlyContinue
+  $coldSince = StartNeoFences -Perf
+  if ($StartOnly) { } elseif ($big = FenceRect 'Perf big') {
+    $x = [int]($big.Left + ($big.Right - $big.Left) / 2); $y = [int]($big.Top + ($big.Bottom - $big.Top) / 2)
+    $from = [DateTimeOffset](Get-Date); Wheel $x $y 25 -1; Wheel $x $y 25 1; Pause-Ms 2500; $to = [DateTimeOffset](Get-Date)
+    $scroll = [NeoFences.Core.Lifecycle.PerfLog]::FramesBetween([string[]]@(LogSince $coldSince), $from, $to)
+    $report.Add("scroll the big fence: worst frame $($scroll.Item2) ms, $($scroll.Item3) of $($scroll.Item1) over 33 ms")
+  } else { $report.Add('scroll: the big fence was not found') }
+  if ($StartOnly) { } elseif ($medium = FenceRect 'Perf medium') {
+    $from = [DateTimeOffset](Get-Date)
+    Drag ([int](($medium.Left + $medium.Right) / 2)) ([int]($medium.Top + 12)) 200 120
+    Drag ([int]($medium.Right + 200 - 3)) ([int]($medium.Bottom + 120 - 3)) 160 100
+    Pause-Ms 2500
+    $drag = [NeoFences.Core.Lifecycle.PerfLog]::FramesBetween([string[]]@(LogSince $coldSince), $from, [DateTimeOffset](Get-Date))
+    $report.Add("drag and resize the medium fence: worst frame $($drag.Item2) ms, $($drag.Item3) of $($drag.Item1) over 33 ms")
+  } else { $report.Add('drag: the medium fence was not found') }
+  StopNeoFences
+  $cold = [NeoFences.Core.Lifecycle.PerfLog]::Read([string[]]@(LogSince $coldSince)) | Select-Object -Last 1
+  $report.Add("cold start: fences shown $($cold.FencesShownMs) ms; icons settled $($cold.IconsSettledMs) ms ($($cold.IconRequests) requests, $($cold.FromCache) from the cache); frames: worst $($cold.WorstFrameMs) ms, $($cold.SlowFrames) of $($cold.Frames) over 33 ms")
+  @(LogSince $coldSince | Select-String -Pattern 'timing: (windows opened|items set|layout applied|slowest fence placed)' | Select-Object -Last 4 | ForEach-Object { '  cold ' + ($_.Line -replace '^.*timing: ', '') }) | ForEach-Object { $report.Add($_) }
+
+  # Warm start: the cache is there; CPU idle and in game mode (frame statistics off: they keep WPF drawing).
+  $warmSince = StartNeoFences
+  $warm = [NeoFences.Core.Lifecycle.PerfLog]::Read([string[]]@(LogSince $warmSince)) | Select-Object -Last 1
+  $report.Add("warm start: fences shown $($warm.FencesShownMs) ms; icons settled $($warm.IconsSettledMs) ms ($($warm.IconRequests) requests, $($warm.FromCache) from the cache)")
+  if (-not $StartOnly) {
+    [void][NfPerf.W]::SetCursorPos(10, 10)
+    Pause-Ms 5000; $before = CpuSeconds; Start-Sleep -Seconds 60; $report.Add(('idle: {0:N2} s CPU over 60 s' -f ((CpuSeconds) - $before)))
+  }
+  if (-not $SkipGame -and -not $StartOnly) {
+    # A borderless window over the whole screen (the taskbar too: a maximized one is not "full screen" to Windows) is what
+    # game mode reacts to. WinForms needs STA: a child pwsh -STA.
+    $gameScript = 'Add-Type -AssemblyName System.Windows.Forms; $form = New-Object System.Windows.Forms.Form -Property @{ FormBorderStyle = "None"; StartPosition = "Manual"; Bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds; BackColor = "Black"; TopMost = $true; Text = "NeoFences perf game" }; $timer = New-Object System.Windows.Forms.Timer -Property @{ Interval = 75000 }; $timer.Add_Tick({ $form.Close() }); $timer.Start(); [void]$form.ShowDialog()'
+    $gameFrom = Get-Date
+    $game = Start-Process pwsh -ArgumentList '-NoProfile', '-STA', '-Command', $gameScript -PassThru
+    Start-Sleep -Seconds 10
+    $engaged = @(LogSince $gameFrom | Select-String -SimpleMatch 'game mode: True' | Where-Object { [DateTimeOffset]::ParseExact($_.Line.Substring(0, 30), 'yyyy-MM-dd HH:mm:ss.fff zzz', $null) -ge [DateTimeOffset]$gameFrom }).Count -gt 0
+    $before = CpuSeconds; Start-Sleep -Seconds 60
+    $report.Add(('game mode{0}: {1:N2} s CPU over 60 s' -f $(if ($engaged) { '' } else { ' (NOT engaged: the window did not count as full screen)' }), ((CpuSeconds) - $before)))
+    $game.WaitForExit(15000) | Out-Null; if (-not $game.HasExited) { $game.Kill() }
+  }
+} finally {
+  try { StopNeoFences } catch { Running | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force; Start-Sleep -Seconds 2 } # the data goes back whatever happened
+  Copy-Item -LiteralPath (Join-Path $data 'logs') -Destination (Join-Path $Out 'logs') -Recurse -Force -ErrorAction SilentlyContinue # the run's logs, before the data goes back
+  if ($data -ne (Join-Path $env:LOCALAPPDATA 'NeoFences')) { throw 'unexpected data path' }
+  Remove-Item -LiteralPath $data -Recurse -Force
+  Copy-Item -LiteralPath $backup -Destination $data -Recurse
+  if ($runValue) { Set-ItemProperty $runKey -Name NeoFences -Value $runValue }
+  Remove-Item -LiteralPath $backup -Recurse -Force
+  if (Test-Path -LiteralPath $installed) { Start-Process $installed }
+  $report | Set-Content -LiteralPath (Join-Path $Out 'report.txt')
+  $report
+  "===== done: data and startup entry put back, NeoFences started again; the mouse is yours ====="
+}
```

- [ ] **Step 2: Build and test.** `dotnet build` → `0 Warning(s)`; `dotnet test` → `Passed: 822`; the script parses
  (`pwsh -NoProfile -Command '[System.Management.Automation.Language.Parser]::ParseFile("tools\perf\measure-scale.ps1", [ref]$null, [ref]$e); $e.Count'` → `0`).
- [ ] **Step 3: Commit.** `git add -A && git commit -m "perf: added the icon cache, visible-first icons, timers that stop when nothing shows, lighter items and the scale measurement"` (ledger the Task 2 calls).

### Task 3: Docs

**Files:**
- Modify: `docs/DECISIONS.md` (ADR-058), `docs/ARCHITECTURE.md` (0.24.0 paragraph after 0.23.0), `docs/FEATURES.md` (an
  extras row), `docs/GUIDE.md` (§14 a Troubleshooting line), `docs/TEST-CHECKLIST.md` (section AW)
- Create: `docs/research/m37-performance-at-scale.md`

- [ ] **Step 1: ADR-058** appended to `docs/DECISIONS.md`:

```markdown
## ADR-058 — Measure first; an icon and name cache; visible first; timers only while needed
**Date:** 2026-10-06 · **Status:** Accepted

**Context.** The readiness review found no icon cache, every fence item built, a shadow effect per label, a widget timer
that never stopped, and nothing tested beyond the owner's ~40 items. The owner picked smooth big setups, a faster start
and true idleness (not memory) and asked to measure first.

**Decision.**
- **Measure**: `tools/perf/measure-scale.ps1` builds a 500-item / 50-fence setup on a backed-up copy of the data and reads
  NeoFences' timing marks (`PerfLog`: fences shown, icons settled, frame statistics with `NEOFENCES_PERF=1`, start steps).
- **Icons**: each icon is asked of Windows once per run, kept in `cache\icons\` (PNG, display name, the source's stamp) and
  shown from there at the next start; a changed stamp or a target without one is loaded fresh behind it; Refresh and special
  icons go past the cache. What can be seen is loaded first.
- **Idle**: the widget timer, the hover polls and the auto-collect timer stop while nothing can be seen (game mode, hidden,
  paused, rolled up, a background tab); slower start work waits until the fences are shown.
- **Lighter items**: labels keep their shadow and are drawn once into a bitmap; a cover tile is built only for covers.
- **Not done**: ready-to-run (7 % faster start, below the 20 % asked for); virtualization (the 200-item fence came down to
  130–160 ms; the owner chose to leave it until after 1.0).

**Consequences.** Icons at 500 items: 11 s → 3 s cold, at once from the cache warm; the cache is NeoFences' own, under
64 MB, safe to delete. The start is now bound by creating and showing the fence windows (~50 ms each).
```

- [ ] **Step 2: ARCHITECTURE** — after the 0.23.0 paragraph:

```markdown
**0.24.0 (M37, performance at scale)**: measure first (ADR-058) — `PerfLog` (Core) writes and reads the timing marks;
`tools/perf/measure-scale.ps1` builds a 500 / 50 setup on a backed-up copy and measures. `IconLoader` answers each key once
per run, then from `IconDiskCache` (`cache\icons\`, `IconCache` / `IconCacheIndex` in Core: key, stamp, pruning), then
from the shell, with urgent (visible) and later queues; Refresh and special icons are fresh. The widget timer, hover polls
and auto-collect timer stop while nothing can be seen; library, watching and the wallpaper read start after "fences shown".
Labels are cached as bitmaps; the cover tile is its own template. `research/m37-performance-at-scale.md`.
```

- [ ] **Step 3: FEATURES** — after the M36 extras row:
  `| Performance at scale: icons from a cache at start, visible icons first, nothing ticking while hidden or in a game, a 500-item / 50-fence measurement | 0.24 (M37) | done | ADR-058 |`
- [ ] **Step 4: GUIDE** §14 Troubleshooting, a new bullet after "A fence is off-screen…":
  `- **An icon looks out of date** (an app updated its icon): fence menu → **Refresh** asks Windows again. NeoFences keeps
  icons in `%LOCALAPPDATA%\NeoFences\cache` to start faster; deleting that folder is safe (it fills again).`
- [ ] **Step 5: Checklist AW** appended to `docs/TEST-CHECKLIST.md`:

```markdown
## AW — 0.24.0 performance at scale (M37)

| ID | Steps | Expected |
|---|---|---|
| AW1 | `tools/perf/measure-scale.ps1` on the branch build | the report: cold icons settled ≈ 3 s, warm ≈ fences shown with icons from the cache; data, startup entry and NeoFences put back |
| AW2 | Restart NeoFences on your own setup | fences and icons appear at once (from the cache); nothing looks different from 0.23.0 |
| AW3 | Change a shortcut's icon or target; empty / fill the Recycle Bin; fence menu → Refresh | the new icon shows (after a moment, or at once with Refresh) |
| AW4 | Delete `%LOCALAPPDATA%\NeoFences\cache`, or make its index unreadable; start | icons load fresh, the cache fills again, no error shown |
| AW5 | A full-screen video or game for a minute, then back; quick-hide and back; a tab with a clock; a rolled-up fence | NeoFences ~0 % CPU meanwhile (Task Manager); clocks tick at once after; rolled-up fences open on hover; hidden title bars show on hover |
| AW6 | Cover tiles: hover, select, a missing game, Large covers, labels on hover | exactly as in 0.23.0 |
| AW7 | Labels at 125 % / 150 % scaling, light mode, a rename, a light/dark switch | sharp, the same shadow |
```

- [ ] **Step 6: Research note** `docs/research/m37-performance-at-scale.md`: the six measurement runs and the table from
  "How this plan is written", where the start goes, the ready-to-run comparison, the virtualization decision, the fake-game
  finding, the calls made while prototyping, and a "Live check" section filled by Task 5.
- [ ] **Step 7: Check** the texts against the code; secret scan of `git diff main`.
- [ ] **Step 8: Commit.** `git add -A && git commit -m "docs: described the performance work in ADR-058, architecture, features, the guide, checklist AW and the M37 note"`

### Task 4: Final review and fix pass

- [ ] Whole-branch review on the most capable model (Review Focus above); findings re-graded by effect; Critical/Important
  fixed in one pass, each with a failing test first where Core-testable.

### Task 5: Measurement and live check (asked first)

- [ ] With the owner's OK: `tools/perf/measure-scale.ps1` on the branch's Release build (AW1, numbers into the research
  note); then on a backed-up copy of the owner's data (`m37-switch.ps1`): AW2–AW7 as far as scriptable (screenshots sent
  as they are taken), game mode with a real full-screen video; restore the data and the Run value; start the installed
  copy; refocus Terminal; commit `docs: added the M37 measurement and live check results`.
