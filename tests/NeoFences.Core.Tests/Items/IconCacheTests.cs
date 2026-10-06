using NeoFences.Core.Items;
using NeoFences.Core.Tests.TestSupport;

namespace NeoFences.Core.Tests.Items;

/// <summary>M37 (spec 2026-10-06-performance-at-scale-design §3): the icon and name cache's decisions.</summary>
public class IconCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static IconCacheEntry Entry(string key, DateTimeOffset lastUsed, long bytes = 1000, IconStamp? stamp = null, string? name = null, DateTimeOffset? verified = null) =>
        new() { File = IconCache.FileNameOf(key), LastUsed = lastUsed, Bytes = bytes, Stamp = stamp, Name = name, Verified = verified ?? lastUsed };

    // ---------- the key ----------

    [Fact]
    public void Key_IsTheSameForTheSamePathInAnyCase_AndDiffersBySizeAndOwnIcon()
    {
        var key = IconCache.KeyOf(@"C:\Tools\App.exe", ownIcon: null, sizePx: 48);
        Assert.Matches("^[0-9a-f]{32}$", key);
        Assert.Equal(key, IconCache.KeyOf(@"c:\tools\app.EXE", ownIcon: null, sizePx: 48));
        Assert.NotEqual(key, IconCache.KeyOf(@"C:\Tools\App.exe", ownIcon: null, sizePx: 72));
        Assert.NotEqual(key, IconCache.KeyOf(@"C:\Tools\App.exe", new ItemIcon { File = @"C:\icons\a.ico", Index = 2 }, sizePx: 48));
        Assert.NotEqual(IconCache.KeyOf(@"C:\Tools\App.exe", new ItemIcon { File = @"C:\icons\a.ico", Index = 2 }, 48),
            IconCache.KeyOf(@"C:\Tools\App.exe", new ItemIcon { File = @"C:\icons\a.ico", Index = 3 }, 48));
        Assert.NotEqual(key, IconCache.KeyOf(@"C:\Tools\App.exe", new ItemIcon { Image = "pic.png" }, sizePx: 48));
    }

    [Fact]
    public void Key_KeepsTheCaseOfWebsitesAndAppIds()
    {
        // A URL's path and a Store app id are not case-insensitive file paths: two of them may differ only in case.
        Assert.NotEqual(IconCache.KeyOf("https://example.com/A", null, 48), IconCache.KeyOf("https://example.com/a", null, 48));
        Assert.Equal("abc.png", IconCache.FileNameOf("abc"));
    }

    // ---------- when the fresh load runs ----------

    [Fact]
    public void FreshLoad_RunsWithoutAnEntry_WithoutAStamp_AndWhenTheStampChanged()
    {
        var stamp = new IconStamp(LastWriteTicks: 100, Length: 5);
        Assert.True(IconCache.NeedsFreshLoad(entry: null, current: stamp, now: Now));
        Assert.True(IconCache.NeedsFreshLoad(Entry("a", Now, stamp: null), current: null, now: Now));      // a Start app, a website: always behind
        Assert.True(IconCache.NeedsFreshLoad(Entry("a", Now, stamp: stamp), current: null, now: Now));     // the file is gone now
        Assert.True(IconCache.NeedsFreshLoad(Entry("a", Now, stamp: stamp), current: stamp with { Length = 6 }, now: Now));
        Assert.False(IconCache.NeedsFreshLoad(Entry("a", Now, stamp: stamp), current: stamp, now: Now));
    }

    // Final review I2: a shortcut's own stamp does not change when the app behind it gets a new icon: checked once a day.
    [Fact]
    public void FreshLoad_AlsoRunsOnceADay_SoAnAppUpdateBehindAShortcutIsSeen()
    {
        var stamp = new IconStamp(100, 5);
        Assert.False(IconCache.NeedsFreshLoad(Entry("a", Now, stamp: stamp, verified: Now.AddHours(-23)), stamp, Now));
        Assert.True(IconCache.NeedsFreshLoad(Entry("a", Now, stamp: stamp, verified: Now.AddHours(-25)), stamp, Now));
        Assert.True(IconCache.NeedsFreshLoad(Entry("a", Now, stamp: stamp) with { Verified = default }, stamp, Now)); // an entry from before: checked once
    }

    // Final review I1: a placeholder (a share that is down, a missing target's generic icon) or a website's badge must not stick
    // for the run, or a found site icon and a target that comes back would not show until the next start.
    [Fact]
    public void OnlyARealIconOfAReachableTarget_IsRememberedForTheRunOrKeptOnDisk()
    {
        Assert.True(IconCache.RememberForRun(ItemKind.Path, reachable: true, generic: false));
        Assert.False(IconCache.RememberForRun(ItemKind.Path, reachable: false, generic: false));
        Assert.False(IconCache.RememberForRun(ItemKind.Path, reachable: true, generic: true));
        Assert.False(IconCache.RememberForRun(ItemKind.Website, reachable: true, generic: false));
        Assert.True(IconCache.KeepOnDisk(reachable: true, generic: false));
        Assert.False(IconCache.KeepOnDisk(reachable: true, generic: true));
        Assert.False(IconCache.KeepOnDisk(reachable: false, generic: false));
    }

    // ---------- pruning ----------

    [Fact]
    public void Prune_DropsEntriesUnusedFor30Days_ThenTheOldestUntilUnderTheCap()
    {
        var index = new IconCacheIndex
        {
            Entries = new Dictionary<string, IconCacheEntry>
            {
                ["old"] = Entry("old", Now.AddDays(-31)),
                ["a"] = Entry("a", Now.AddDays(-3), bytes: 600),
                ["b"] = Entry("b", Now.AddDays(-2), bytes: 600),
                ["c"] = Entry("c", Now.AddDays(-1), bytes: 600),
            },
        };
        var (kept, delete) = IconCache.Prune(index, Now, maxBytes: 1300);
        Assert.Equal(["b", "c"], kept.Entries.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["a.png", "old.png"], delete.Order(StringComparer.Ordinal));
        // nothing to drop: nothing deleted
        Assert.Empty(IconCache.Prune(new IconCacheIndex { Entries = new Dictionary<string, IconCacheEntry> { ["c"] = Entry("c", Now) } }, Now, 1300).Delete);
    }

    [Fact]
    public void Touch_MarksEntriesUsedNow_AndWithAddsOrReplaces()
    {
        var index = new IconCacheIndex().With("a", Entry("a", Now.AddDays(-10)));
        index = index.With("b", Entry("b", Now.AddDays(-10))).Touch(["a", "missing"], Now);
        Assert.Equal(Now, index.Entries["a"].LastUsed);
        Assert.Equal(Now.AddDays(-10), index.Entries["b"].LastUsed);
        Assert.False(index.Entries.ContainsKey("missing"));
        Assert.Equal("New", index.With("a", Entry("a", Now, name: "New")).Entries["a"].Name);
    }

    // ---------- the index file ----------

    [Fact]
    public void Index_SurvivesTheFile_AndADamagedOneStartsOver()
    {
        using var folder = new TempDirectory();
        var path = folder.File("index.json");
        var index = new IconCacheIndex().With("a", Entry("a", Now, stamp: new IconStamp(100, 5), name: "Tool"));
        index.Save(path);
        var (back, failure) = IconCacheIndex.Load(path);
        Assert.Null(failure);
        Assert.Equal(index.Entries["a"], back.Entries["a"]);

        File.WriteAllText(path, "{ not json");
        var (damaged, problem) = IconCacheIndex.Load(path);
        Assert.Empty(damaged.Entries);
        Assert.NotNull(problem);

        var (missing, none) = IconCacheIndex.Load(folder.File("nothing.json"));
        Assert.Empty(missing.Entries);
        Assert.Null(none); // no cache yet is not a problem
    }

    [Fact]
    public void Load_DropsEntriesThatNameAFileOutsideTheFolderOrAreOdd()
    {
        using var folder = new TempDirectory();
        var path = folder.File("index.json");
        File.WriteAllText(path, """
            { "version": 1, "entries": {
              "good": { "file": "good.png", "lastUsed": "2026-10-06T12:00:00+00:00", "bytes": 10 },
              "escape": { "file": "..\\..\\evil.png", "lastUsed": "2026-10-06T12:00:00+00:00" },
              "other": { "file": "other.exe", "lastUsed": "2026-10-06T12:00:00+00:00" },
              "blank": { "file": "", "lastUsed": "2026-10-06T12:00:00+00:00" },
              "nulled": null,
              "  ": { "file": "x.png" }
            } }
            """);
        var (index, failure) = IconCacheIndex.Load(path);
        Assert.Null(failure);
        Assert.Equal(["good"], index.Entries.Keys);
    }

    [Fact]
    public void Load_TreatsANewerIndexAsEmpty()
    {
        using var folder = new TempDirectory();
        var path = folder.File("index.json");
        File.WriteAllText(path, """{ "version": 9, "entries": { "good": { "file": "good.png" } } }""");
        Assert.Empty(IconCacheIndex.Load(path).Index.Entries);
    }
}
