# M13a — v1.6 Carry-overs: Data Safety Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the data-safety and crash-path carry-overs from the M9–M12 reviews. Released as v1.6.0.
- Older builds must never save over new fields.
- Snapshots from a newer version must never be half-read.
- Copied rules get their own ids.
- The library index stays true after failures.
- No disk checks on the UI thread, and nothing re-arms after exit.
- USB sticks with Portals can always be removed.
- `--exit` also stops a copy that is waiting to start.

**Architecture:**
- **Core:** schema version 2, stamped by the normalizer; duplicate rule ids renewed; `SnapshotStore` refuses newer and oversized files and cleans up a failed write; `Snapshots.Restore` keeps the Desktop's order for newcomers; `LibraryFiles.Settle`.
- **Shell:**
  - per-entry failures in the Epic and Xbox scanners;
  - `LibraryWriter` settles the index;
  - `FolderWatcher.HeldFolder`.
- **App:**
  - Portals register removal notices for the folder they hold, with a short grace after a release;
  - library guards for exit and quick-hide, and no disk checks on the UI thread;
  - a manual-reset exit signal that a waiting copy also sees.

**Tech Stack:** .NET 10, WPF, CsWin32 0.3.335, Serilog, xUnit. No new dependencies.

**Spec:** none (a carry-over batch). Source: `docs/ROADMAP.md` carry-overs from the M9–M12 reviews. User choice 2026-10-04: v1.6 carry-overs in 3 batches, each released (M13a data safety → v1.6.0, M13b Game Library and rules polish → v1.6.1, M13c fence and Settings UX → v1.6.2). **Decision:** ADR-033 (new, Task 3).

**Pre-verified (2026-10-04):** every code block below was compiled together (0 warnings, 0 errors), and **373/373 tests pass** (9 new). Live check on the user's PC with consent (no input automation; config and the installed 1.5.0 restored):
- the config was saved as schema 2;
- a copy waiting to start gave up when `--exit` stopped the running one;
- a later start ran normally;
- 0 log warnings.

## Global Constraints

- **Hard rule 1:** nothing touches user files; the library index only ever names files in NeoFences' own `library\` folder.
- **Hard rule 7:** a bad snapshot, Epic manifest or Xbox package degrades that one entry, never the app or the whole source.
- **Schema:** `CurrentSchemaVersion = 2`; a newer config or snapshot is never written over or half-read.
- **Commits:** single line, Conventional Commits, past tense, no `Co-Authored-By` trailer. Test command: `dotnet test NeoFences.slnx`.
- **Smokes:** only with the user's explicit go; restore the installed copy (1.5.0) and `config.json`; print TEST RUNNING / TEST COMPLETE; no input automation is needed for this batch.

## Review Focus

1. **Upgrading and downgrading.** A v1 config with tabs, rules and a library loaded by v1.6; then v1.5 started on the v2 file; daily backups in between. Expected: nothing lost; v1.5 stays read-only; a pre-1.6 backup still loads in 1.5.
2. **Two copies and `--exit` in odd orders.** `--exit` with nothing running, two `--exit`s in a row, an `--exit` racing a fresh start, the watchdog restarting after a crash. Expected: a stale signal never stops a later start, and a crash restart still happens.
3. **Drive removal around refreshes.** A Portal whose folder is deleted then re-created on the stick; eject refused by another program; the stick back within the grace period. Expected: the eject is never vetoed by NeoFences, and the Portal comes back.
4. **Library index under failure.** A locked shortcut during a rewrite, a delete denied, a power cut between the shortcut writes and the index write. Expected: the index never names a missing file for long, and no orphan is left behind.
5. **Snapshot files from elsewhere.** A file from a newer version, a huge file, a file being written while the tray opens. Expected: listed as a problem once, never loaded, renamed or restored.

---

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/NeoFences.Core/Model/NeoFencesConfig.cs`, `Model/Snapshots.cs`, `Config/ConfigNormalizer.cs`, `Config/SnapshotStore.cs`, `Library/LibraryFiles.cs` + `tests/…/Config/DataSafetyTests.cs` (new), `tests/…/Config/ConfigJsonTests.cs` | schema, snapshots, rules, library index | 1 |
| `src/NeoFences.Shell/GameScanners.cs`, `LibraryWriter.cs`, `FolderItems.cs`, `src/NeoFences.App/PortalState.cs`, `FenceHost.Library.cs`, `FenceHost.cs`, `App.cs` | scanners, writer, watchers, Portals, library, exit | 2 |
| `docs/…` (ADR-033, checklist Z, research, ROADMAP, ARCHITECTURE, SESSION-LOG), hub | docs | 3 |

---

### Task 1: Core — schema 2, snapshots, rule ids, library index

**Files:**
- Modify: `docs/ROADMAP.md` (claim).
- Create: `tests/NeoFences.Core.Tests/Config/DataSafetyTests.cs`.
- Replace: `src/NeoFences.Core/Model/NeoFencesConfig.cs`, `Model/Snapshots.cs`, `Config/ConfigNormalizer.cs`, `Config/SnapshotStore.cs`, `Library/LibraryFiles.cs`, `tests/NeoFences.Core.Tests/Config/ConfigJsonTests.cs`.

**Interfaces:**
- Produces:
  - `NeoFencesConfig.CurrentSchemaVersion = 2`;
  - `SnapshotStore.MaxFileBytes`;
  - `LibraryFiles.Settle(previous, plan, failedWrites, failedDeletes) → LibraryState`.

- [ ] **Step 1: Branch and claim**

```powershell
git switch -c m13a-data-safety
```
In `docs/ROADMAP.md`, before the line `Tasks for M2–M8 are broken down when the previous milestone closes.`, add:
```markdown
## M13 — v1.6 carry-overs (user choice 2026-10-04: 3 batches, each released)
- [x] M13a plan (data safety → v1.6.0)
- [~] M13a — claimed by session 2026-10-04 m13a
- [ ] M13b — Game Library and rules polish (v1.6.1)
- [ ] M13c — fence and Settings UX (v1.6.2)
```
```powershell
git add docs/ROADMAP.md
git commit -m "docs: claimed M13a"
```

- [ ] **Step 2: Write the failing tests**

`tests/NeoFences.Core.Tests/Config/DataSafetyTests.cs`:
```csharp
using NeoFences.Core.Config;
using NeoFences.Core.Library;
using NeoFences.Core.Model;
using NeoFences.Core.Tests.TestSupport;

namespace NeoFences.Core.Tests.Config;

/// <summary>
/// M13a (v1.6.0) data safety: carry-overs from the M9–M12 reviews — the schema version, snapshots from newer versions,
/// duplicate rule ids, the library index after failed writes, and the M10 test gaps.
/// </summary>
public class DataSafetyTests : IDisposable
{
    private const string Desktop = @"C:\Users\cipher\Desktop\";
    private static readonly DateTimeOffset Taken = new(2026, 10, 4, 12, 0, 0, TimeSpan.FromHours(5.5));
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    // ---------- schema version (M9 carry-over: an older build would save over tabs, rules and the library) ----------

    [Fact]
    public void Schema_IsTwo_AndAnOldConfigIsUpgradedWhenNormalized()
    {
        Assert.Equal(2, NeoFencesConfig.CurrentSchemaVersion);
        var old = ConfigJson.Deserialize("""{ "schemaVersion": 1, "fences": [] }""");
        Assert.Equal(1, old.SchemaVersion);
        Assert.Equal(2, ConfigNormalizer.Normalize(old).SchemaVersion);
    }

    [Fact]
    public void Schema_AVersionOneFileLoads_AndIsSavedAsTwo_WhileANewerFileIsNeverOverwritten()
    {
        var store = new ConfigStore(_directory.Path, new FixedTimeProvider(Taken));
        File.WriteAllText(store.ConfigPath, """{ "schemaVersion": 1, "fences": [ { "id": "a", "title": "Games" } ] }""");

        var loaded = store.Load();
        Assert.False(loaded.IsReadOnly);
        Assert.True(store.Save(loaded.Config));
        Assert.Contains("\"schemaVersion\": 2", File.ReadAllText(store.ConfigPath));

        File.WriteAllText(store.ConfigPath, """{ "schemaVersion": 3, "fences": [] }""");
        var newer = new ConfigStore(_directory.Path, new FixedTimeProvider(Taken));
        Assert.True(newer.Load().IsReadOnly);
        Assert.False(newer.Save(NeoFencesConfig.CreateDefault()));
    }

    // ---------- rules (M11 carry-over) ----------

    [Fact]
    public void Rules_DuplicateIdsGetNewOnes_TheFirstKeepsItsId()
    {
        var images = Rule.Create(new RuleCondition { Kind = RuleKind.Type, Group = TypeGroup.Images }, "fence") with { Id = "same" };
        var config = ConfigNormalizer.Normalize(new NeoFencesConfig { Rules = [images, images with { FenceId = "other" }, images] });

        Assert.Equal("same", config.Rules[0].Id);
        Assert.Equal(3, config.Rules.Select(rule => rule.Id).Distinct().Count());
    }

    // ---------- snapshots (M10 carry-overs and test gaps) ----------

    private static NeoFencesConfig Sample(out Fence inbox, out Fence games)
    {
        inbox = Fence.Create("Inbox") with { IsInbox = true, Items = [Desktop + "a.txt"] };
        games = Fence.Create("Games") with { Items = [Desktop + "Game.lnk"] };
        return new NeoFencesConfig { Fences = [inbox, games] };
    }

    [Fact]
    public void Snapshots_FromANewerVersionAreRefused_NotHalfRead()
    {
        var store = new SnapshotStore(_directory.Path);
        var path = Path.Combine(_directory.Path, "snapshot-newer.json");
        File.WriteAllText(path, """{ "schemaVersion": 3, "name": "From the future", "takenAt": "2026-10-04T12:00:00+05:30", "fences": [] }""");

        Assert.Null(store.Load(path)); // a rename would rewrite it without the fields this version does not know
        Assert.NotNull(store.LastFailure);
        Assert.Empty(store.List());
        Assert.Equal([path], store.Problems.Select(problem => problem.Path));
    }

    [Fact]
    public void Snapshots_AHugeStrayFileIsSkipped_NotRead()
    {
        var store = new SnapshotStore(_directory.Path);
        var huge = Path.Combine(_directory.Path, "huge.json");
        using (var stream = File.Create(huge)) stream.SetLength(SnapshotStore.MaxFileBytes + 1);

        Assert.Empty(store.List());
        Assert.Equal([huge], store.Problems.Select(problem => problem.Path));
    }

    [Fact]
    public void Snapshots_AFailedSaveLeavesNoTempFile()
    {
        var store = new SnapshotStore(_directory.Path);
        Directory.CreateDirectory(Path.Combine(_directory.Path, "blocked.json")); // a folder where the file should go

        Assert.Null(store.Save(Snapshots.Take(Sample(out _, out _), name: "x", now: Taken), "blocked.json"));
        Assert.NotNull(store.LastFailure);
        Assert.Empty(Directory.GetFiles(_directory.Path, "*.tmp"));
    }

    [Fact]
    public void Snapshots_RestoreKeepsAPortal_MatchesRefsIgnoringCase_AndOrdersNewcomers()
    {
        var current = Sample(out var inbox, out var games);
        var portal = Fence.Create("Downloads", FenceSource.Portal(@"D:\Downloads"));
        var snapshot = Snapshots.Take(current with { Fences = [.. current.Fences, portal] }, name: "s", now: Taken);
        // Since then: Game.lnk renamed in case only, a.txt moved by hand into Games, three new unfenced items.
        current = current
            .WithFence(inbox with { Items = [] })
            .WithFence(games with { Items = [Desktop + "a.txt", Desktop + "new-in-games.txt"] });
        string[] desktopNow = [Desktop + "z.txt", Desktop + "GAME.LNK", Desktop + "y.txt", Desktop + "a.txt", Desktop + "new-in-games.txt", Desktop + "x.txt"];

        var restored = Snapshots.Restore(current, snapshot, desktopNow);

        Assert.Empty(restored.Fences.Single(fence => fence.Title == "Downloads").Items);
        Assert.Equal([Desktop + "Game.lnk", Desktop + "new-in-games.txt"], restored.Fences.Single(fence => fence.Id == games.Id).Items);
        Assert.Equal([Desktop + "a.txt", Desktop + "z.txt", Desktop + "y.txt", Desktop + "x.txt"], restored.Inbox.Items); // the Desktop's order
    }

    [Fact]
    public void Snapshots_AFileWithNullFieldsStillRestores()
    {
        var store = new SnapshotStore(_directory.Path);
        var path = Path.Combine(_directory.Path, "nulls.json");
        File.WriteAllText(path, """{ "schemaVersion": 1, "name": null, "takenAt": "2026-10-04T12:00:00+05:30", "fences": [ null, { "id": "g", "title": null, "items": null, "tabs": null } ], "layouts": null }""");

        var snapshot = store.Load(path)!;
        var restored = Snapshots.Restore(Sample(out _, out _), snapshot, [Desktop + "a.txt"]);

        Assert.Single(restored.Fences, fence => fence.IsInbox);
        Assert.Contains(restored.Fences, fence => fence.Id == "g");
        Assert.Equal([Desktop + "a.txt"], restored.Fences.SelectMany(fence => fence.Items));
    }

    // ---------- the Game Library index after failed writes (M12 carry-over) ----------

    private static GameEntry Game(string id, string name) => new(id, name, GameSource.Steam, "steam", new GameLaunch($"steam://rungameid/{id}"));

    [Fact]
    public void LibraryIndex_AFailedRewriteKeepsTheOldEntry_AFailedDeleteStaysListed_AFailedNewFileIsLeftOut()
    {
        var previous = LibraryFiles.Plan(previous: [], games: [Game("1", "Old Name"), Game("2", "Gone"), Game("3", "Same")]).Items;
        var plan = LibraryFiles.Plan(previous, games: [Game("1", "Old Name") with { Poster = @"C:\new.jpg" }, Game("3", "Same"), Game("4", "New")]);

        var state = LibraryFiles.Settle(previous, plan, failedWrites: ["Old Name.url", "New.url"], failedDeletes: ["Gone.url"]);

        Assert.Equal(["Old Name.url", "Same.url", "Gone.url"], state.Items.Select(item => item.FileName));
        Assert.Null(state.Items[0].Game.Poster); // the old file is still the old one: it is retried next time
        Assert.Equal(plan.Items, LibraryFiles.Settle(previous, plan, failedWrites: [], failedDeletes: []).Items);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/NeoFences.Core.Tests`
Expected: build FAILS: `CS0117 'LibraryFiles' does not contain a definition for 'Settle'`, `CS0117 'SnapshotStore' does not contain a definition for 'MaxFileBytes'`. (With those two added as stubs, 6 tests fail on their asserts and 3 test-gap tests already pass.)

- [ ] **Step 4: Implement**

`src/NeoFences.Core/Model/NeoFencesConfig.cs`:
```csharp
using System.Text.Json.Serialization;

namespace NeoFences.Core.Model;

/// <summary>Everything NeoFences persists, stored as <c>config.json</c> (ADR-006).</summary>
public sealed record NeoFencesConfig
{
    /// <summary>
    /// 2 since v1.6 (M13a): tabs, rules and the library are in the file; v1.5 and older read 2 as "newer" and never save
    /// over it (they would drop those fields).
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public Settings Settings { get; init; } = new();
    public IReadOnlyList<Fence> Fences { get; init; } = [];
    public IReadOnlyDictionary<string, Layout> Layouts { get; init; } = new Dictionary<string, Layout>();

    /// <summary>Rules auto-sort (M11), in order: the first matching enabled rule decides a new item's fence.</summary>
    public IReadOnlyList<Rule> Rules { get; init; } = [];

    /// <summary>Game Library settings (M12): game folders, sources, hidden games.</summary>
    public LibrarySettings Library { get; init; } = new();

    /// <summary>Fingerprint of the display configuration seen last; new configurations are derived from it.</summary>
    public string? LastLayoutFingerprint { get; init; }

    /// <summary>The single Inbox fence. Guaranteed to exist after <c>ConfigNormalizer.Normalize</c>.</summary>
    [JsonIgnore]
    public Fence Inbox => Fences.First(fence => fence.IsInbox);

    public static NeoFencesConfig CreateDefault() =>
        new() { Fences = [Fence.Create("Inbox") with { IsInbox = true }] };

    public NeoFencesConfig WithFence(Fence updated) =>
        this with { Fences = Fences.Select(fence => fence.Id == updated.Id ? updated : fence).ToList() };
}
```
`src/NeoFences.Core/Model/Snapshots.cs`:
```csharp
using NeoFences.Core.Config;

namespace NeoFences.Core.Model;

/// <summary>
/// A saved arrangement (M10, spec 2026-10-03-snapshots-design): every fence with every field (items, tabs, colours,
/// roll-up, lock) and every monitor setup's places. No Settings, no files. Stored as a file of its own.
/// </summary>
public sealed record Snapshot
{
    public int SchemaVersion { get; init; } = NeoFencesConfig.CurrentSchemaVersion;
    public string Name { get; init; } = "";
    public DateTimeOffset TakenAt { get; init; }
    public IReadOnlyList<Fence> Fences { get; init; } = [];
    public IReadOnlyDictionary<string, Layout> Layouts { get; init; } = new Dictionary<string, Layout>();
    public string? LastLayoutFingerprint { get; init; }
}

/// <summary>Taking and restoring snapshots (M10). Pure: the App supplies the desktop listing and saves the result.</summary>
public static class Snapshots
{
    public static Snapshot Take(NeoFencesConfig config, string name, DateTimeOffset now) => new()
    {
        Name = name,
        TakenAt = now,
        Fences = config.Fences,
        Layouts = config.Layouts,
        LastLayoutFingerprint = config.LastLayoutFingerprint,
    };

    /// <summary>
    /// The arrangement of <paramref name="snapshot"/> applied to <paramref name="current"/> (spec §3): the snapshot's fences
    /// and places; its icons back where they were if still on the desktop (<paramref name="desktopNow"/>); icons it does
    /// not know stay in their current fence when that fence survives, else go to the Inbox; Settings and setups the
    /// snapshot never saw stay as they are.
    /// </summary>
    public static NeoFencesConfig Restore(NeoFencesConfig current, Snapshot snapshot, IEnumerable<string> desktopNow)
    {
        var desktop = desktopNow.ToList(); // the Desktop's own order for newcomers (M13a: not a HashSet's order)
        var present = desktop.ToHashSet(ItemRef.Comparer);
        // The snapshot file may be damaged or hand-edited: the normalizer gives it one Inbox, no duplicates, sane tabs.
        var saved = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = snapshot.Fences, Layouts = snapshot.Layouts });
        var fences = saved.Fences
            .Select(fence => fence.Source.Kind == FenceSourceKind.Desktop ? fence with { Items = fence.Items.Where(present.Contains).ToList() } : fence)
            .ToList();
        var placed = fences.SelectMany(fence => fence.Items).ToHashSet(ItemRef.Comparer);
        var desktopFenceIds = fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop).Select(fence => fence.Id).ToHashSet(StringComparer.Ordinal);
        var inboxId = fences.First(fence => fence.IsInbox).Id;

        // Newer icons, in their current order: their fence if it survives, else the Inbox; unfenced ones to the Inbox too.
        var arrivals = new List<(string FenceId, string ItemRef)>();
        foreach (var fence in current.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop))
        {
            foreach (var itemRef in fence.Items.Where(itemRef => present.Contains(itemRef) && placed.Add(itemRef)))
            {
                arrivals.Add((desktopFenceIds.Contains(fence.Id) ? fence.Id : inboxId, itemRef));
            }
        }
        arrivals.AddRange(desktop.Where(placed.Add).Select(itemRef => (inboxId, itemRef)));
        fences = fences.Select(fence =>
        {
            var joining = arrivals.Where(arrival => arrival.FenceId == fence.Id).Select(arrival => arrival.ItemRef).ToList();
            return joining.Count == 0 ? fence : fence with { Items = [.. fence.Items, .. joining] };
        }).ToList();

        var layouts = new Dictionary<string, Layout>(current.Layouts);
        foreach (var (fingerprint, layout) in saved.Layouts) layouts[fingerprint] = layout;
        return ConfigNormalizer.Normalize(current with { Fences = fences, Layouts = layouts });
    }
}
```
`src/NeoFences.Core/Config/ConfigNormalizer.cs`:
```csharp
using NeoFences.Core.Model;

namespace NeoFences.Core.Config;

/// <summary>
/// Repairs a loaded (possibly hand-edited) config without losing items: exactly one Inbox, unique fence
/// ids, no nulls, supported icon sizes and enum values, each desktop item in at most one fence, no items on
/// portal fences, the Inbox always a desktop fence, and layouts free of null or non-finite entries.
/// </summary>
public static class ConfigNormalizer
{
    public static IReadOnlyList<int> IconSizes { get; } = [32, 48, 64, 96];

    public static NeoFencesConfig Normalize(NeoFencesConfig config)
    {
        var defaults = new Settings();
        var settings = config.Settings ?? defaults;
        settings = settings with
        {
            PeekHotkey = string.IsNullOrWhiteSpace(settings.PeekHotkey) ? defaults.PeekHotkey : settings.PeekHotkey,
            RollupExpand = Enum.IsDefined(settings.RollupExpand) ? settings.RollupExpand : defaults.RollupExpand,
            DefaultLabels = Enum.IsDefined(settings.DefaultLabels) ? settings.DefaultLabels : defaults.DefaultLabels, // M8b review
        };

        var seenFenceIds = new HashSet<string>(StringComparer.Ordinal);
        var seenItems = new HashSet<string>(ItemRef.Comparer);
        var inboxFound = false;
        var libraryFound = false; // M12: at most one Game Library fence
        var fences = new List<Fence>();

        foreach (var loadedFence in config.Fences ?? [])
        {
            if (loadedFence is null) continue;
            var isInbox = loadedFence.IsInbox && !inboxFound;
            var source = loadedFence.Source is { } loadedSource && Enum.IsDefined(loadedSource.Kind) ? loadedSource : FenceSource.Desktop;
            if (source.Kind == FenceSourceKind.Library && (libraryFound || loadedFence.IsInbox)) source = FenceSource.Desktop;
            libraryFound |= source.Kind == FenceSourceKind.Library;
            var fence = loadedFence with
            {
                Id = string.IsNullOrWhiteSpace(loadedFence.Id) || !seenFenceIds.Add(loadedFence.Id) ? Fence.NewId() : loadedFence.Id,
                Title = loadedFence.Title ?? "",
                Source = isInbox ? FenceSource.Desktop : source,
                IsInbox = isInbox,
                Sort = Enum.IsDefined(loadedFence.Sort) ? loadedFence.Sort : FenceSort.Manual,
                Labels = Enum.IsDefined(loadedFence.Labels) ? loadedFence.Labels : LabelMode.Always, // a hand-edited number (M8b review)
                Tabs = loadedFence.Tabs ?? [], // a hand-edited "tabs": null (M9 final review)
                IconSize = IconSizes.Contains(loadedFence.IconSize) ? loadedFence.IconSize : 48,
            };
            seenFenceIds.Add(fence.Id);
            inboxFound |= fence.IsInbox;

            var items = fence.Source.Kind == FenceSourceKind.Desktop
                ? (loadedFence.Items ?? []).Where(itemRef => !string.IsNullOrWhiteSpace(itemRef) && seenItems.Add(itemRef)).ToList()
                : [];
            fences.Add(fence with { Items = items });
        }

        if (!inboxFound)
        {
            fences.Insert(0, Fence.Create("Inbox") with { IsInbox = true });
        }

        return config with
        {
            SchemaVersion = NeoFencesConfig.CurrentSchemaVersion, // an older file is saved in today's format (M13a)
            Settings = settings,
            Fences = FenceTabs.Repair(fences), // M9: one consistent box per tab
            Rules = UniqueIds((config.Rules ?? []).Where(rule => rule is not null).Select(Rules.Repair)), // M11: a broken rule is disabled
            Layouts = NormalizeLayouts(config.Layouts),
            Library = NormalizeLibrary(config.Library),
        };
    }

    /// <summary>A rule block copied in a hand edit keeps its id: the copies get new ones, or one action would hit all (M13a).</summary>
    private static List<Rule> UniqueIds(IEnumerable<Rule> rules)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return rules.Select(rule => seen.Add(rule.Id) ? rule : rule with { Id = Guid.NewGuid().ToString("N") }).ToList();
    }

    private static LibrarySettings NormalizeLibrary(LibrarySettings? library) => new()
    {
        Folders = (library?.Folders ?? []).Where(folder => !string.IsNullOrWhiteSpace(folder)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        Sources = library?.Sources ?? new LibrarySources(),
        Hidden = (library?.Hidden ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
    };

    private static Dictionary<string, Layout> NormalizeLayouts(IReadOnlyDictionary<string, Layout>? loadedLayouts)
    {
        var layouts = new Dictionary<string, Layout>();
        foreach (var (fingerprint, layout) in loadedLayouts ?? new Dictionary<string, Layout>())
        {
            if (layout is null) continue;
            layouts[fingerprint] = new Layout
            {
                Monitors = (layout.Monitors ?? new Dictionary<string, MonitorArea>())
                    .Where(entry => entry.Value is { IsUsable: true })
                    .ToDictionary(entry => entry.Key, entry => entry.Value),
                Fences = (layout.Fences ?? new Dictionary<string, FenceRect>())
                    .Where(entry => entry.Value is { } rect && !string.IsNullOrWhiteSpace(rect.Monitor)
                                    && double.IsFinite(rect.X) && double.IsFinite(rect.Y) && double.IsFinite(rect.W) && double.IsFinite(rect.H))
                    .ToDictionary(entry => entry.Key, entry => entry.Value),
            };
        }
        return layouts;
    }
}
```
`src/NeoFences.Core/Config/SnapshotStore.cs`:
```csharp
using System.Text.Json;
using NeoFences.Core.Model;

namespace NeoFences.Core.Config;

/// <summary>A snapshot file as the list shows it.</summary>
public sealed record SnapshotEntry(string Path, string Name, DateTimeOffset TakenAt)
{
    /// <summary>The automatic one written before every restore (replaced each time).</summary>
    public bool IsBeforeRestore => string.Equals(System.IO.Path.GetFileName(Path), SnapshotStore.BeforeRestoreFileName, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Snapshot files (M10): one per snapshot in <c>%LOCALAPPDATA%\NeoFences\snapshots\</c>, written to a temp file and then
/// swapped in, so a power cut never leaves half a snapshot. A damaged file is skipped and reported, never thrown.
/// Deleting is the App's job (the Recycle Bin, hard rule 1).
/// </summary>
public sealed class SnapshotStore(string directory)
{
    public const string BeforeRestoreFileName = "before-restore.json";

    /// <summary>Bigger files are not snapshots (a stray file): skipped unread, so a tray click never loads it (M13a).</summary>
    public const long MaxFileBytes = 16 * 1024 * 1024;

    public string Directory => directory;

    /// <summary>Files the last <see cref="List"/> could not read (path and reason).</summary>
    public IReadOnlyList<(string Path, Exception Failure)> Problems { get; private set; } = [];

    /// <summary>Why the last <see cref="Save"/> or <see cref="Rename"/> failed, or null.</summary>
    public Exception? LastFailure { get; private set; }

    /// <summary>Every readable snapshot, newest first.</summary>
    public IReadOnlyList<SnapshotEntry> List()
    {
        var entries = new List<SnapshotEntry>();
        var problems = new List<(string, Exception)>();
        string[] paths;
        try
        {
            paths = System.IO.Directory.Exists(directory) ? System.IO.Directory.GetFiles(directory, "*.json") : [];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            paths = []; // the folder itself cannot be listed: no snapshots, never a crash (final review I3, hard rule 7)
            problems.Add((directory, failure));
        }
        foreach (var path in paths)
        {
            try
            {
                var snapshot = Read(path);
                entries.Add(new SnapshotEntry(path, snapshot.Name ?? "", snapshot.TakenAt));
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                problems.Add((path, failure));
            }
        }
        Problems = problems;
        return entries.OrderByDescending(entry => entry.TakenAt).ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Writes a snapshot; a new file (named by its time) unless <paramref name="fileName"/> is given.</summary>
    /// <returns>The file's path, or null when it could not be written (<see cref="LastFailure"/>).</returns>
    public string? Save(Snapshot snapshot, string? fileName = null)
    {
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, fileName ?? NewFileName(snapshot.TakenAt));
            WriteSafely(path, ConfigJson.SerializeSnapshot(snapshot));
            LastFailure = null;
            return path;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            LastFailure = failure;
            return null;
        }
    }

    /// <summary>The snapshot in a file, or null when it cannot be read.</summary>
    public Snapshot? Load(string path)
    {
        try
        {
            return Read(path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            LastFailure = failure;
            return null;
        }
    }

    /// <exception cref="InvalidDataException">Too big, or written by a newer NeoFences (M13a): restoring or renaming it
    /// here would drop what this version does not know.</exception>
    private static Snapshot Read(string path)
    {
        if (new FileInfo(path).Length > MaxFileBytes) throw new InvalidDataException($"{path} is too big for a snapshot");
        var snapshot = ConfigJson.DeserializeSnapshot(File.ReadAllText(path));
        if (snapshot.SchemaVersion > NeoFencesConfig.CurrentSchemaVersion)
            throw new InvalidDataException($"{path} comes from a newer NeoFences (schema {snapshot.SchemaVersion})");
        return snapshot;
    }

    /// <summary>
    /// A new name for a snapshot (its time stays). A renamed "Before restore" becomes an ordinary snapshot file, so the
    /// next restore does not overwrite it (final review I4); our own file is moved, nothing is deleted.
    /// </summary>
    public bool Rename(string path, string newName)
    {
        if (Load(path) is not { } snapshot) return false;
        var fileName = System.IO.Path.GetFileName(path);
        if (string.Equals(fileName, BeforeRestoreFileName, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                fileName = NewFileName(snapshot.TakenAt);
                File.Move(path, System.IO.Path.Combine(directory, fileName));
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                LastFailure = failure;
                return false;
            }
        }
        return Save(snapshot with { Name = newName.Trim() }, fileName) is not null;
    }

    private string NewFileName(DateTimeOffset takenAt)
    {
        var stem = $"snapshot-{takenAt:yyyy-MM-dd_HH-mm-ss}";
        var name = stem + ".json";
        for (var counter = 2; File.Exists(System.IO.Path.Combine(directory, name)); counter++) name = $"{stem}-{counter}.json";
        return name;
    }

    private static void WriteSafely(string path, string json)
    {
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(json);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
        try
        {
            if (File.Exists(path)) File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else File.Move(temp, path);
        }
        catch
        {
            File.Delete(temp); // no *.json.tmp left behind (M13a); the caller reports the failure
            throw;
        }
    }
}
```
`src/NeoFences.Core/Library/LibraryFiles.cs`:
```csharp
namespace NeoFences.Core.Library;

/// <summary>A game's shortcut in NeoFences' library folder (M12).</summary>
/// <param name="Signature">What the file was written from; a changed signature means the file is rewritten.</param>
public sealed record LibraryItem(GameEntry Game, string FileName, string Signature);

/// <summary>The library folder's index (<c>library\index.json</c>): which files NeoFences wrote, for which games.</summary>
public sealed record LibraryState
{
    public IReadOnlyList<LibraryItem> Items { get; init; } = [];
}

/// <summary>The shortcut files to write and remove, and the next index.</summary>
public sealed record LibraryPlan(IReadOnlyList<LibraryItem> Items, IReadOnlyList<LibraryItem> Write, IReadOnlyList<string> Delete);

/// <summary>Plans the library folder from the catalog (M12, spec §3): safe, stable, unique names; only changes written.</summary>
public static class LibraryFiles
{
    private static readonly char[] Forbidden = ['\\', '/', '*', '?', '"', '<', '>', '|'];
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static LibraryPlan Plan(IReadOnlyList<LibraryItem> previous, IReadOnlyList<GameEntry> games)
    {
        var previousById = previous.GroupBy(item => item.Game.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var takenBases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // First keep the names of files that still fit their game, so a new game never takes them over.
        foreach (var game in games)
        {
            if (previousById.TryGetValue(game.Id, out var old) && Fits(old.FileName, game) && takenBases.Add(Path.GetFileNameWithoutExtension(old.FileName)))
                fileNames[game.Id] = old.FileName;
        }
        foreach (var game in games.Where(game => !fileNames.ContainsKey(game.Id)))
        {
            var baseName = SafeName(game.Name);
            var candidate = baseName;
            for (var counter = 2; !takenBases.Add(candidate); counter++) candidate = $"{baseName} ({counter})";
            fileNames[game.Id] = candidate + Extension(game);
        }
        var items = games.Select(game => new LibraryItem(game, fileNames[game.Id], Signature(game))).ToList();
        var write = items.Where(item => !previousById.TryGetValue(item.Game.Id, out var old) || old.Signature != item.Signature || old.FileName != item.FileName).ToList();
        var kept = items.Select(item => item.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var delete = previous.Select(item => item.FileName).Where(fileName => !kept.Contains(fileName)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new LibraryPlan(items, write, delete);
    }

    /// <summary>
    /// The index that matches the folder after <see cref="Plan"/> was carried out with some failures (M13a): a file that
    /// could not be rewritten keeps its old entry (the old file is still there, retried next time), a new file that could
    /// not be written is left out, and a file that could not be deleted stays listed (deleted next time).
    /// </summary>
    public static LibraryState Settle(IReadOnlyList<LibraryItem> previous, LibraryPlan plan, IReadOnlyCollection<string> failedWrites, IReadOnlyCollection<string> failedDeletes)
    {
        var failedWrite = failedWrites.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var failedDelete = failedDeletes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previousByFile = new Dictionary<string, LibraryItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in previous) previousByFile.TryAdd(item.FileName, item);
        var items = plan.Items
            .Select(item => !failedWrite.Contains(item.FileName) ? item : previousByFile.GetValueOrDefault(item.FileName))
            .OfType<LibraryItem>()
            .Concat(previous.Where(item => failedDelete.Contains(item.FileName)))
            .ToList();
        return new LibraryState { Items = items };
    }

    /// <summary>
    /// A loaded index made safe (final review, re-graded minor 3): entries with missing fields, a file name that is a path
    /// (a delete must never leave the library folder, hard rule 1) or a second entry for the same file are dropped.
    /// </summary>
    public static LibraryState Repair(LibraryState state)
    {
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = (state.Items ?? [])
            .Where(item => item?.Game is { Id.Length: > 0, Name: not null, ScanKey: not null, Launch.Target.Length: > 0 }
                           && item.FileName is { Length: > 0 } fileName && fileName == Path.GetFileName(fileName)
                           && fileName is not ("." or "..") && fileName.IndexOfAny(['/', '\\', ':']) < 0
                           && fileNames.Add(fileName))
            .Select(item => item with { Signature = item.Signature ?? "", Game = item.Game with { OtherIds = item.Game.OtherIds ?? [] } })
            .ToList();
        return new LibraryState { Items = items };
    }

    /// <summary>A file name for a game: forbidden characters out (":" becomes " -"), no reserved device names, at most 100 characters.</summary>
    public static string SafeName(string name)
    {
        var cleaned = new string(name.Replace(":", " -").Where(character => !char.IsControl(character) && Array.IndexOf(Forbidden, character) < 0).ToArray());
        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.');
        if (cleaned.Length > 100) cleaned = cleaned[..100].TrimEnd('.', ' ');
        if (cleaned.Length == 0) cleaned = "Game";
        return Reserved.Contains(cleaned) ? cleaned + " (game)" : cleaned;
    }

    private static string Extension(GameEntry game) => game.Launch.IsLink ? ".url" : ".lnk";

    /// <summary>The old file still names this game: "Blur.lnk", or "Blur (2).url" after a clash.</summary>
    private static bool Fits(string fileName, GameEntry game)
    {
        if (!Path.GetExtension(fileName).Equals(Extension(game), StringComparison.OrdinalIgnoreCase)) return false;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var safe = SafeName(game.Name);
        return stem.Equals(safe, StringComparison.OrdinalIgnoreCase)
            || (stem.StartsWith(safe + " (", StringComparison.OrdinalIgnoreCase) && stem.EndsWith(')') && int.TryParse(stem[(safe.Length + 2)..^1], out _));
    }

    private static string Signature(GameEntry game) =>
        string.Join('|', game.Name, game.Launch.Target, game.Launch.Arguments, game.Launch.WorkingFolder, game.Poster, game.IconPath, game.InstallFolder);
}
```
`tests/NeoFences.Core.Tests/Config/ConfigJsonTests.cs` (the schema assertion now uses `NeoFencesConfig.CurrentSchemaVersion`):
```csharp
using System.Text.Json;
using NeoFences.Core.Config;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Config;

public class ConfigJsonTests
{
    private static NeoFencesConfig SampleConfig()
    {
        var inbox = Fence.Create("Inbox") with { IsInbox = true, Items = [@"C:\Users\cipher\Desktop\notes.txt"] };
        var games = Fence.Create("Games") with
        {
            Items = [@"C:\Users\cipher\Desktop\Crysis 2.lnk", "::{645FF040-5081-101B-9F08-00AA002F954E}"],
            IconSize = 64,
            RolledUp = true,
        };
        var screenshots = Fence.Create("Screenshots", FenceSource.Portal(@"D:\Pictures\Screenshots")) with { Sort = FenceSort.Date };
        var layout = new Layout
        {
            Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(2560, 1392) },
            Fences = new Dictionary<string, FenceRect> { [games.Id] = new("DELL", 40, 60, 420, 260) },
        };
        return new NeoFencesConfig
        {
            Fences = [inbox, games, screenshots],
            Layouts = new Dictionary<string, Layout> { ["1mon:DELL-3840x2160@150%"] = layout },
            LastLayoutFingerprint = "1mon:DELL-3840x2160@150%",
        };
    }

    [Fact]
    public void RoundTrip_PreservesEverything()
    {
        var original = SampleConfig();

        var restored = ConfigJson.Deserialize(ConfigJson.Serialize(original));

        Assert.Equal(original.SchemaVersion, restored.SchemaVersion);
        Assert.Equal(original.Settings, restored.Settings);
        Assert.Equal(original.LastLayoutFingerprint, restored.LastLayoutFingerprint);
        Assert.Equal(original.Fences.Count, restored.Fences.Count);
        for (var fenceIdx = 0; fenceIdx < original.Fences.Count; fenceIdx++)
        {
            var expected = original.Fences[fenceIdx];
            var actual = restored.Fences[fenceIdx];
            Assert.Equal(expected with { Items = [], Tabs = [] }, actual with { Items = [], Tabs = [] }); // lists compare by reference
            Assert.Equal(expected.Items, actual.Items);
        }
        var restoredLayout = restored.Layouts["1mon:DELL-3840x2160@150%"];
        Assert.Equal(new MonitorArea(2560, 1392), restoredLayout.Monitors["DELL"]);
        Assert.Equal(new FenceRect("DELL", 40, 60, 420, 260), restoredLayout.Fences[original.Fences[1].Id]);
    }

    [Fact]
    public void Json_UsesCamelCaseNamesAndEnumValues_AndOmitsComputedInbox()
    {
        var json = ConfigJson.Serialize(SampleConfig());
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(NeoFencesConfig.CurrentSchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.False(root.GetProperty("settings").GetProperty("takeover").GetBoolean());
        Assert.False(root.TryGetProperty("inbox", out _));
        var screenshots = root.GetProperty("fences")[2];
        Assert.Equal("portal", screenshots.GetProperty("source").GetProperty("kind").GetString());
        Assert.Equal(@"D:\Pictures\Screenshots", screenshots.GetProperty("source").GetProperty("path").GetString());
        Assert.Equal("date", screenshots.GetProperty("sort").GetString());
        var rect = root.GetProperty("layouts").GetProperty("1mon:DELL-3840x2160@150%").GetProperty("fences").EnumerateObject().Single().Value;
        Assert.Equal("DELL", rect.GetProperty("monitor").GetString());
        Assert.Equal(420, rect.GetProperty("w").GetDouble());
    }

    [Fact]
    public void Deserialize_AcceptsMinimalDocument()
    {
        var config = ConfigJson.Deserialize("""{ "schemaVersion": 1 }""");

        Assert.Empty(config.Fences);
        Assert.Equal(new Settings(), config.Settings);
    }

    [Fact]
    public void Deserialize_MissingPropertiesKeepTheirDefaults()
    {
        var config = ConfigJson.Deserialize("""
            { "schemaVersion": 1, "settings": { "takeover": true },
              "fences": [ { "id": "f1", "title": "Games" } ] }
            """);

        Assert.True(config.Settings.Takeover);
        Assert.Equal("Ctrl+Alt+Space", config.Settings.PeekHotkey);
        var fence = Assert.Single(config.Fences);
        Assert.Equal(48, fence.IconSize);
        Assert.Equal(FenceSource.Desktop, fence.Source);
        Assert.Empty(fence.Items);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("""{ "fences": [ { "title": "no id" } ] }""")]
    public void Deserialize_RejectsInvalidDocuments(string json)
    {
        Assert.ThrowsAny<JsonException>(() => ConfigJson.Deserialize(json));
    }

    [Fact]
    public void Json_IsReadableForHandEditing_NoEscapedPlusOrAmpersand()
    {
        var config = NeoFencesConfig.CreateDefault() with
        {
            LastLayoutFingerprint = @"1mon:\\?\DISPLAY#GSM5B71#5&66efef6&1&UID4353-1920x1080@100%",
        };

        var json = ConfigJson.Serialize(config);

        Assert.Contains("\"peekHotkey\": \"Ctrl+Alt+Space\"", json);
        Assert.Contains("5&66efef6&1&UID4353", json);
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test NeoFences.slnx`
Expected: `Passed! - Failed: 0, Passed: 373`

- [ ] **Step 6: Commit**

```powershell
git add src/NeoFences.Core tests/NeoFences.Core.Tests
git commit -m "fix: fixed older builds saving over new config fields, half-read newer snapshots, shared rule ids and a library index out of step after failures"
```

---

### Task 2: Shell + App — scanners, watchers, Portals, library, exit

**Files:** replace `src/NeoFences.Shell/GameScanners.cs`, `LibraryWriter.cs`, `FolderItems.cs`, `src/NeoFences.App/PortalState.cs`, `FenceHost.Library.cs`, `FenceHost.cs`, `App.cs`.

**Interfaces:**
- Consumes: Task 1 (`LibraryFiles.Settle`).
- Produces:
  - `FolderWatcher.HeldFolder`;
  - `LibraryWriter.Apply(folder, previous, plan, logFailure)`.

- [ ] **Step 1: Files**

`src/NeoFences.Shell/GameScanners.cs`:
```csharp
using System.Xml.Linq;
using Microsoft.Win32;
using NeoFences.Core.Library;
using NeoFences.Core.Model;

namespace NeoFences.Shell;

/// <summary>
/// Finds installed games for the Game Library (M12, spec §2): Steam, Epic, GOG, Ubisoft Connect, EA app, Battle.net,
/// Xbox / Microsoft Store, the user's game folders and game shortcuts on the Desktop. Read-only: nothing is changed or
/// started. Each scan is isolated — a failure makes only that scan "unreadable" (its games are kept). Call on an STA
/// thread (shortcuts are read through the shell).
/// </summary>
public static class GameScanners
{
    private static readonly EnumerationOptions ProgramSearch = new()
    {
        RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public static IReadOnlyList<SourceScan> ScanAll(LibrarySettings settings, Action<string, Exception> logFailure)
    {
        var scans = new List<SourceScan>();
        void Run(string scanKey, Func<IEnumerable<SourceScan>> scan)
        {
            try
            {
                scans.AddRange(scan());
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                logFailure(scanKey, failure);
                scans.Add(new SourceScan(scanKey, false, []));
            }
        }
        var sources = settings.Sources;
        if (sources.Steam) Run("steam", ScanSteam);
        if (sources.Epic) Run("epic", () => [ScanEpic(logFailure)]);
        if (sources.Gog) Run("gog", () => [ScanGog()]);
        if (sources.Ubisoft) Run("ubisoft", () => [ScanUbisoft()]);
        if (sources.Ea) Run("ea", () => [ScanUninstallEntries("ea", GameSource.Ea, "Electronic Arts", ["EA app", "Origin", "EA Desktop"])]);
        if (sources.BattleNet) Run("battlenet", () => [ScanUninstallEntries("battlenet", GameSource.BattleNet, "Blizzard Entertainment", ["Battle.net"])]);
        if (sources.Xbox) Run("xbox", () => [ScanXbox(logFailure)]);
        if (sources.Folders)
        {
            foreach (var folder in settings.Folders) Run(FolderKey(folder), () => [ScanGameFolder(folder)]);
        }
        if (sources.DesktopShortcuts) Run("desktop", () => [ScanDesktopShortcuts(settings.Folders, logFailure)]);
        return scans;
    }

    /// <summary>Folders whose changes mean "installed or removed a game": Steam's library folders, Epic's manifests, game folders.</summary>
    public static IReadOnlyList<string> WatchFolders(LibrarySettings settings)
    {
        var folders = new List<string>();
        try
        {
            if (settings.Sources.Steam && SteamRoot() is { } steam)
                folders.AddRange(SteamLibraries(steam).Select(library => Path.Combine(library, "steamapps")).Where(Directory.Exists));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or FormatException)
        {
            // no watching for a broken Steam install; scans still say why
        }
        if (settings.Sources.Epic && Directory.Exists(EpicManifests)) folders.Add(EpicManifests);
        if (settings.Sources.Folders) folders.AddRange(settings.Folders.Where(Directory.Exists));
        return folders;
    }

    public static string FolderKey(string folder) => "folder:" + folder.Trim().TrimEnd('\\', '/').ToLowerInvariant();

    // --- Steam -------------------------------------------------------------------------------------------------------

    private static IEnumerable<SourceScan> ScanSteam()
    {
        var steamPath = SteamPath();
        if (steamPath is null) return [new SourceScan("steam", true, [])]; // Steam not installed
        if (!Directory.Exists(steamPath)) return [new SourceScan("steam", false, [])]; // its drive is not there (yet): keep its games (final review I1)
        var steam = Path.GetFullPath(steamPath);
        var scans = new List<SourceScan>();
        foreach (var library in SteamLibraries(steam))
        {
            var key = "steam:" + library.TrimEnd('\\').ToLowerInvariant();
            var steamapps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamapps))
            {
                scans.Add(new SourceScan(key, false, [])); // a library on a drive that is not there: keep its games
                continue;
            }
            try
            {
                var games = new List<GameEntry>();
                foreach (var manifest in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
                {
                    if (SteamFiles.ParseManifest(File.ReadAllText(manifest)) is not { } app) continue;
                    var installFolder = Path.Combine(steamapps, "common", app.InstallDir);
                    games.Add(new GameEntry($"steam:{app.AppId}", app.Name, GameSource.Steam, key, new GameLaunch(SteamFiles.LaunchUri(app.AppId)),
                        installFolder, SteamPoster(steam, app.AppId), ProgramIn(installFolder)));
                }
                scans.Add(new SourceScan(key, true, games));
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // A manifest Steam is rewriting right now (the watcher fires then): this library keeps its games this time.
                scans.Add(new SourceScan(key, false, []));
            }
        }
        return scans;
    }

    /// <summary>Steam's folder as the registry names it (it may be on a drive that is not there), or null when Steam is not installed.</summary>
    private static string? SteamPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        return key?.GetValue("SteamPath") is string { Length: > 0 } path ? path : null;
    }

    private static string? SteamRoot() => SteamPath() is { } path && Directory.Exists(path) ? Path.GetFullPath(path) : null;

    /// <summary>
    /// An install folder that is gone while its drive is there: an uninstalled game left in a launcher's list (skip it).
    /// A drive that is not there: unknown (the caller keeps the source's games). Final review I3.
    /// </summary>
    private static bool? InstalledAt(string folder)
    {
        if (Directory.Exists(folder)) return true;
        return Path.GetPathRoot(folder) is { Length: > 0 } root && Directory.Exists(root) ? false : null;
    }

    private static IReadOnlyList<string> SteamLibraries(string steam)
    {
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        var libraries = File.Exists(vdf) ? SteamFiles.LibraryFolders(File.ReadAllText(vdf)).ToList() : [];
        if (!libraries.Any(library => string.Equals(library.TrimEnd('\\'), steam.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) libraries.Insert(0, steam);
        return libraries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Steam's cached 2:3 cover: <c>librarycache\&lt;appid&gt;\library_600x900.jpg</c> or, newer, <c>…\&lt;hash&gt;\library_capsule.jpg</c>.</summary>
    private static string? SteamPoster(string steam, string appId)
    {
        var cache = Path.Combine(steam, "appcache", "librarycache");
        var folder = Path.Combine(cache, appId);
        string[] direct = [Path.Combine(folder, "library_600x900.jpg"), Path.Combine(cache, $"{appId}_library_600x900.jpg")];
        if (direct.FirstOrDefault(File.Exists) is { } poster) return poster;
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "library_capsule.jpg", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 1 }).FirstOrDefault()
            : null;
    }

    // --- Epic --------------------------------------------------------------------------------------------------------

    private static string EpicManifests =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");

    private static SourceScan ScanEpic(Action<string, Exception> logFailure)
    {
        if (!Directory.Exists(EpicManifests)) return new SourceScan("epic", true, []);
        var games = new List<GameEntry>();
        var readable = true;
        foreach (var manifest in Directory.EnumerateFiles(EpicManifests, "*.item"))
        {
            EpicGame? game;
            try
            {
                game = EpicManifest.Parse(File.ReadAllText(manifest));
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                logFailure(manifest, failure); // one locked manifest: that game only, this time only (M13a)
                continue;
            }
            if (game is null) continue;
            var installed = InstalledAt(game.InstallLocation);
            readable &= installed is not null;
            if (installed != true) continue;
            var program = game.LaunchExecutable is { } executable ? Path.Combine(game.InstallLocation, executable) : ProgramIn(game.InstallLocation);
            games.Add(new GameEntry($"epic:{game.AppName}", game.DisplayName, GameSource.Epic, "epic", new GameLaunch(game.LaunchUri),
                game.InstallLocation, Poster: null, IconPath: program is not null && File.Exists(program) ? program : null));
        }
        return new SourceScan("epic", readable, games);
    }

    // --- GOG ---------------------------------------------------------------------------------------------------------

    private static SourceScan ScanGog()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var games = machine.OpenSubKey(@"SOFTWARE\GOG.com\Games");
        if (games is null) return new SourceScan("gog", true, []);
        using var galaxyPaths = machine.OpenSubKey(@"SOFTWARE\GOG.com\GalaxyClient\paths");
        var galaxy = galaxyPaths?.GetValue("client") is string clientFolder ? Path.Combine(clientFolder, "GalaxyClient.exe") : null;
        if (galaxy is not null && !File.Exists(galaxy)) galaxy = null;
        var found = new List<GameEntry>();
        var readable = true;
        foreach (var id in games.GetSubKeyNames())
        {
            using var game = games.OpenSubKey(id);
            if (game?.GetValue("gameName") is not string name || game.GetValue("path") is not string folder || game.GetValue("exe") is not string exe) continue;
            var installed = InstalledAt(folder);
            readable &= installed is not null;
            if (installed != true) continue;
            var launch = galaxy is not null
                ? new GameLaunch(galaxy, $"/command=runGame /gameId={id} /path=\"{folder}\"", Path.GetDirectoryName(galaxy))
                : new GameLaunch(exe, game.GetValue("launchParam") as string is { Length: > 0 } parameters ? parameters : null,
                    game.GetValue("workingDir") as string is { Length: > 0 } working ? working : Path.GetDirectoryName(exe));
            found.Add(new GameEntry($"gog:{id}", name, GameSource.Gog, "gog", launch, folder, Poster: null, IconPath: File.Exists(exe) ? exe : null));
        }
        return new SourceScan("gog", readable, found);
    }

    // --- Ubisoft Connect ---------------------------------------------------------------------------------------------

    private static SourceScan ScanUbisoft()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var installs = machine.OpenSubKey(@"SOFTWARE\Ubisoft\Launcher\Installs");
        if (installs is null) return new SourceScan("ubisoft", true, []);
        var found = new List<GameEntry>();
        foreach (var id in installs.GetSubKeyNames())
        {
            using var install = installs.OpenSubKey(id);
            if (install?.GetValue("InstallDir") is not string raw) continue;
            var folder = Path.GetFullPath(raw.Replace('/', '\\')).TrimEnd('\\');
            if (!Directory.Exists(folder)) continue;
            found.Add(new GameEntry($"ubisoft:{id}", Path.GetFileName(folder), GameSource.Ubisoft, "ubisoft", new GameLaunch($"uplay://launch/{id}/0"),
                folder, Poster: null, IconPath: ProgramIn(folder)));
        }
        return new SourceScan("ubisoft", true, found);
    }

    // --- EA app, Battle.net (their games' uninstall entries) ---------------------------------------------------------

    private static SourceScan ScanUninstallEntries(string scanKey, GameSource source, string publisher, string[] launcherNames)
    {
        var found = new List<GameEntry>();
        foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var entry = uninstall.OpenSubKey(name);
                if (entry?.GetValue("Publisher") is not string entryPublisher || !entryPublisher.Contains(publisher, StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.GetValue("DisplayName") is not string displayName || launcherNames.Any(launcher => displayName.StartsWith(launcher, StringComparison.OrdinalIgnoreCase))) continue;
                if (entry.GetValue("InstallLocation") is not string folder || !Directory.Exists(folder) || ProgramIn(folder) is not { } program) continue;
                found.Add(new GameEntry($"{scanKey}:{name.ToLowerInvariant()}", displayName, source, scanKey, new GameLaunch(program, WorkingFolder: Path.GetDirectoryName(program)),
                    folder.TrimEnd('\\'), Poster: null, IconPath: program));
            }
        }
        return new SourceScan(scanKey, true, found);
    }

    // --- Xbox / Microsoft Store --------------------------------------------------------------------------------------

    private static SourceScan ScanXbox(Action<string, Exception> logFailure)
    {
        using var packages = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
        if (packages is null) return new SourceScan("xbox", true, []);
        var found = new List<GameEntry>();
        foreach (var fullName in packages.GetSubKeyNames())
        {
            try
            {
                if (XboxGame(packages, fullName) is { } game) found.Add(game);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                logFailure(fullName, failure); // one broken package (a damaged manifest): that package only (M13a)
            }
        }
        return new SourceScan("xbox", true, found);
    }

    private static GameEntry? XboxGame(RegistryKey packages, string fullName)
    {
        {
            using var package = packages.OpenSubKey(fullName);
            if (package?.GetValue("PackageRootFolder") is not string folder) return null;
            var gameConfig = Path.Combine(folder, "MicrosoftGame.config");
            if (!File.Exists(gameConfig)) return null; // only game packages have one
            var visuals = XDocument.Load(gameConfig).Descendants().FirstOrDefault(element => element.Name.LocalName == "ShellVisuals");
            var appId = XDocument.Load(Path.Combine(folder, "AppxManifest.xml")).Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Application")?.Attribute("Id")?.Value;
            if (appId is null) return null;
            var name = visuals?.Attribute("DefaultDisplayName")?.Value is { Length: > 0 } shown && !shown.StartsWith("ms-resource", StringComparison.OrdinalIgnoreCase)
                ? shown
                : package.GetValue("DisplayName") as string is { Length: > 0 } listed && !listed.StartsWith("@", StringComparison.Ordinal) ? listed : fullName.Split('_')[0];
            var parts = fullName.Split('_');
            var familyName = $"{parts[0]}_{parts[^1]}";
            var logo = (visuals?.Attribute("Square480x480Logo") ?? visuals?.Attribute("Square150x150Logo") ?? visuals?.Attribute("StoreLogo"))?.Value;
            return new GameEntry($"xbox:{familyName.ToLowerInvariant()}", name, GameSource.Xbox, "xbox",
                new GameLaunch(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"shell:AppsFolder\\{familyName}!{appId}"),
                folder, Poster: null, IconPath: logo is null ? null : ScaledAsset(folder, logo));
        }
    }

    /// <summary>A package image: the file itself, or its largest ".scale-NNN" variant.</summary>
    private static string? ScaledAsset(string packageFolder, string relative)
    {
        var path = Path.Combine(packageFolder, relative.Replace('/', '\\'));
        if (File.Exists(path)) return path;
        var folder = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(folder)) return null;
        return Directory.EnumerateFiles(folder, $"{Path.GetFileNameWithoutExtension(path)}.scale-*{Path.GetExtension(path)}")
            .OrderByDescending(file => new FileInfo(file).Length).FirstOrDefault();
    }

    // --- My game folders ---------------------------------------------------------------------------------------------

    private static SourceScan ScanGameFolder(string folder)
    {
        var key = FolderKey(folder);
        if (!Directory.Exists(folder)) return new SourceScan(key, false, []); // a drive that is not there: keep its games
        var found = new List<GameEntry>();
        foreach (var game in Directory.EnumerateDirectories(folder))
        {
            if (ProgramIn(game) is not { } program) continue; // no program found: not listed
            found.Add(new GameEntry("folder:" + game.ToLowerInvariant(), Path.GetFileName(game), GameSource.Folder, key,
                new GameLaunch(program, WorkingFolder: Path.GetDirectoryName(program)), game, Poster: null, IconPath: program));
        }
        return new SourceScan(key, true, found);
    }

    /// <summary>The program that starts a game in this folder (<see cref="ProgramPicker"/>), or null.</summary>
    private static string? ProgramIn(string? folder)
    {
        if (folder is null || !Directory.Exists(folder)) return null;
        var programs = new DirectoryInfo(folder).EnumerateFiles("*.exe", ProgramSearch)
            .Select(file => (Path.GetRelativePath(folder, file.FullName), file.Length));
        return ProgramPicker.Pick(programs) is { } relative ? Path.Combine(folder, relative) : null;
    }

    // --- Game shortcuts on the Desktop -------------------------------------------------------------------------------

    private static SourceScan ScanDesktopShortcuts(IReadOnlyList<string> gameFolders, Action<string, Exception> logFailure)
    {
        var listing = DesktopItems.Enumerate();
        var found = new List<GameEntry>();
        foreach (var itemRef in listing.ItemRefs.Where(itemRef => Path.GetExtension(itemRef).ToLowerInvariant() is ".lnk" or ".url"))
        {
            GameLaunch? launch;
            try
            {
                launch = ShellLinks.Read(itemRef);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                logFailure(itemRef, failure);
                continue;
            }
            if (launch is null) continue;
            var gameFolder = gameFolders.Select(root => GameFolderOf(root, launch.Target)).FirstOrDefault(found => found is not null);
            var asText = launch.Arguments is null ? launch.Target : $"{launch.Target} {launch.Arguments}";
            if (gameFolder is null && Rules.LauncherOf(asText) is null) continue;
            var installFolder = gameFolder ?? (launch.IsLink ? null : Path.GetDirectoryName(launch.Target));
            found.Add(new GameEntry("desktop:" + itemRef.ToLowerInvariant(), Path.GetFileNameWithoutExtension(itemRef), GameSource.DesktopShortcut, "desktop",
                launch, installFolder, Poster: null, IconPath: launch.IsLink ? null : launch.Target));
        }
        // A Desktop folder that could not be listed: keep the shortcuts found before.
        return new SourceScan("desktop", listing.UnavailableFolders.Count == 0, found);
    }

    /// <summary>The game sub-folder of a game folder that holds this target, or null.</summary>
    private static string? GameFolderOf(string root, string target)
    {
        var prefix = root.TrimEnd('\\') + "\\";
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var slash = target.IndexOf('\\', prefix.Length);
        return slash < 0 ? null : target[..slash];
    }
}
```
`src/NeoFences.Shell/LibraryWriter.cs`:
```csharp
using NeoFences.Core.Config;
using NeoFences.Core.Library;

namespace NeoFences.Shell;

/// <summary>
/// Keeps NeoFences' library folder in step with a <see cref="LibraryPlan"/> (M12, spec §3): writes changed shortcuts
/// (temp file, then swapped in), removes only files listed in its own index, and saves the index. User files are never
/// touched: the folder is NeoFences' own. Call on an STA thread.
/// </summary>
public static class LibraryWriter
{
    public const string IndexFileName = "index.json";

    /// <summary>The saved index, or an empty one when missing or damaged (the next scan rebuilds it).</summary>
    public static LibraryState ReadIndex(string folder)
    {
        try
        {
            var path = Path.Combine(folder, IndexFileName);
            return File.Exists(path) ? LibraryFiles.Repair(ConfigJson.DeserializeLibrary(File.ReadAllText(path))) : new LibraryState();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return new LibraryState();
        }
    }

    /// <returns>The state actually on disk: an item whose file could not be written is left out (it is tried again next time).</returns>
    /// <param name="previous">The index before this plan: what is on disk when a write or delete fails (M13a).</param>
    public static LibraryState Apply(string folder, IReadOnlyList<LibraryItem> previous, LibraryPlan plan, Action<string, Exception> logFailure)
    {
        Directory.CreateDirectory(folder);
        var failedDeletes = new List<string>();
        foreach (var fileName in plan.Delete)
        {
            try
            {
                File.Delete(Path.Combine(folder, fileName)); // our own shortcut, listed in our index
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                logFailure(fileName, failure);
                failedDeletes.Add(fileName);
            }
        }
        var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toWrite = plan.Items.Where(item => plan.Write.Contains(item) || !File.Exists(Path.Combine(folder, item.FileName))); // also files deleted by hand
        foreach (var item in toWrite)
        {
            var path = Path.Combine(folder, item.FileName);
            var temp = Path.Combine(folder, $".{Guid.NewGuid():N}{Path.GetExtension(item.FileName)}");
            try
            {
                ShellLinks.Write(temp, item.Game.Launch, item.Game.IconPath is { } icon && IsIconSource(icon) ? icon : null);
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                logFailure(item.FileName, failure);
                failed.Add(item.FileName);
                try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        var state = LibraryFiles.Settle(previous, plan, failedWrites: failed, failedDeletes: failedDeletes);
        var index = Path.Combine(folder, IndexFileName);
        var indexTemp = index + ".tmp";
        File.WriteAllText(indexTemp, ConfigJson.SerializeLibrary(state));
        File.Move(indexTemp, index, overwrite: true);
        return state;
    }

    /// <summary>Shortcut icons come from programs and icon files, not from PNG logos (those show on the tile instead).</summary>
    public static bool IsIconSource(string path) => Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".ico" or ".dll";
}
```
`src/NeoFences.Shell/FolderItems.cs`:
```csharp
using NeoFences.Core.Model;

namespace NeoFences.Shell;

/// <summary>The contents of a Portal's folder, and the facts sorting needs about any item (M4).</summary>
public static class FolderItems
{
    /// <summary>Visible entries (not Hidden or System, like Explorer), or null when the folder cannot be read (missing, offline, denied).</summary>
    public static IReadOnlyList<ItemInfo>? TryList(string folderPath)
    {
        try
        {
            return new DirectoryInfo(folderPath).EnumerateFileSystemInfos()
                .Where(DesktopItems.IsVisibleOnDesktop)
                .Select(Describe)
                .ToList();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Sorting facts for items of a desktop fence ("Sort by", one time). Special items sort as folders by name.</summary>
    public static IReadOnlyList<ItemInfo> Describe(IEnumerable<string> itemRefs) => itemRefs.Select(itemRef =>
    {
        if (itemRef.StartsWith("::", StringComparison.Ordinal))
            return new ItemInfo(itemRef, ShellItems.TryGetDisplayName(itemRef) ?? itemRef, IsFolder: true, TypeName: "", DateTimeOffset.MinValue);
        FileSystemInfo entry = Directory.Exists(itemRef) ? new DirectoryInfo(itemRef) : new FileInfo(itemRef);
        // Keep the caller's ref: Windows path normalisation drops trailing spaces/dots ("foo "), so FullName may differ
        // and the one-time sort would then not match the fence's items (M3 review I3).
        return Describe(entry) with { ItemRef = itemRef };
    }).ToList();

    // ponytail: type = extension, not the shell's type name (SHGetFileInfo per item); upgrade if users sort by type often.
    private static ItemInfo Describe(FileSystemInfo entry)
    {
        var isFolder = entry is DirectoryInfo;
        return new ItemInfo(entry.FullName, entry.Name, isFolder, isFolder ? "" : entry.Extension.ToUpperInvariant(),
            entry.Exists ? entry.LastWriteTime : DateTimeOffset.MinValue);
    }
}

/// <summary>Says when a folder's contents changed (a Portal re-lists it). Events arrive on thread-pool threads.</summary>
public sealed class FolderWatcher : IDisposable
{
    private readonly FileSystemWatcher? _watcher;
    private readonly FileSystemWatcher? _parentWatcher; // the folder itself renamed, moved or deleted (and back)

    public event Action? Changed;
    /// <summary>The watcher lost events or stopped (a drive going away, a network hiccup): re-list and re-arm, with backoff (M8d).</summary>
    public event Action? Failed;

    /// <summary>True once the watcher failed, even before anyone subscribed (it can fail as it arms, M8d review I2).</summary>
    public bool HasFailed => _hasFailed;
    private volatile bool _hasFailed;

    /// <summary>False when the folder itself could not be watched (missing, offline): the Portal then retries.</summary>
    public bool IsWatching => _watcher is not null;

    /// <summary>
    /// The folder this watcher holds open: the folder itself, or its parent while the folder is missing (M13a). A removal
    /// notice must be registered for it, or the open handle vetoes "Safely remove".
    /// </summary>
    public string? HeldFolder { get; private set; }

    /// <param name="logFailure">Told when the folder cannot be watched; the Portal then only refreshes on navigation.</param>
    public FolderWatcher(string folderPath, Action<Exception> logFailure)
    {
        try
        {
            _watcher = new FileSystemWatcher(folderPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                InternalBufferSize = 64 * 1024,
            };
            _watcher.Created += (_, _) => Changed?.Invoke();
            _watcher.Deleted += (_, _) => Changed?.Invoke();
            _watcher.Renamed += (_, _) => Changed?.Invoke();
            _watcher.Changed += (_, _) => Changed?.Invoke();
            _watcher.Error += (_, _) =>
            {
                _hasFailed = true;
                Failed?.Invoke(); // lost events or stopped: a full re-list covers them, not too often
            };
            _watcher.EnableRaisingEvents = true;
            HeldFolder = folderPath;
        }
        catch (Exception failure) when (failure is IOException or ArgumentException or UnauthorizedAccessException)
        {
            logFailure(failure);
        }
        // A watcher follows its directory when that is renamed and reports nothing, so the Portal would keep showing
        // a folder that is gone (M4 smoke). The parent tells when the folder itself goes away or comes back.
        try
        {
            var trimmed = folderPath.TrimEnd('\\', '/');
            if (Path.GetDirectoryName(trimmed) is { } parent && Directory.Exists(parent))
            {
                _parentWatcher = new FileSystemWatcher(parent, Path.GetFileName(trimmed))
                {
                    NotifyFilter = NotifyFilters.DirectoryName,
                    IncludeSubdirectories = false,
                };
                _parentWatcher.Created += (_, _) => Changed?.Invoke();
                _parentWatcher.Deleted += (_, _) => Changed?.Invoke();
                _parentWatcher.Renamed += (_, _) => Changed?.Invoke();
                _parentWatcher.EnableRaisingEvents = true;
                HeldFolder ??= parent;
            }
        }
        catch (Exception failure) when (failure is IOException or ArgumentException or UnauthorizedAccessException)
        {
            logFailure(failure);
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _parentWatcher?.Dispose();
    }
}
```
`src/NeoFences.App/PortalState.cs`:
```csharp
using System.IO;
using System.Windows.Threading;
using NeoFences.Core.Model;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>
/// One Portal fence at runtime (M4): the folder it mirrors, the subfolder it shows now (browsing is not saved; a restart
/// shows the Portal's own folder again), and its watcher. Listing and watcher setup run off the UI thread, since a
/// network or USB folder can block for seconds (M4 review I1); bursts of changes become one re-list at most every
/// 250 ms, even while a file keeps being written (I2); an unavailable or unwatched folder is retried every few
/// seconds, so a Portal comes back when its drive does (I4).
/// </summary>
public sealed class PortalState : IDisposable
{
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(7);

    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _retryTimer;
    private readonly Action<IReadOnlyList<ItemInfo>?> _show;
    private readonly Action<Exception> _logFailure;
    private FolderWatcher? _watcher;
    private DeviceRemovalNotice? _removal;   // asks before the Portal's drive is removed (USB stick, M8d)
    private readonly nint _noticeOwner;
    private volatile bool _noticeFailureLogged;
    private readonly DispatcherTimer _backoffTimer;
    private TimeSpan _failureDelay = NeoFences.Core.Lifecycle.WatcherBackoff.First;
    private DateTime _lastArm = DateTime.MinValue;
    private HashSet<string> _listedFolders = new(StringComparer.OrdinalIgnoreCase);
    private int _generation;
    private bool _refreshing;   // a listing is running in the background (a network folder may take seconds)
    private bool _disposed;
    private bool _paused;        // game mode (M6a): changes wait
    private bool _missedChanges; // something changed while paused: re-list on resume
    private DateTime _releasedUntil = DateTime.MinValue; // just let go of a drive being removed: no re-opening for a moment (M13a)
    private static readonly TimeSpan ReleaseGrace = TimeSpan.FromSeconds(5);

    public string Root { get; }

    public string Current { get; private set; }

    public bool CanGoBack => !string.Equals(Current, Root, StringComparison.OrdinalIgnoreCase);

    /// <param name="show">Called on the UI thread with the shown folder's items, or null when it cannot be read.</param>
    /// <param name="noticeOwner">The window that receives "may this drive be removed?" (the app's message window).</param>
    public PortalState(string root, nint noticeOwner, Action<IReadOnlyList<ItemInfo>?> show, Action<Exception> logFailure)
    {
        _noticeOwner = noticeOwner;
        _backoffTimer = new DispatcherTimer();
        _backoffTimer.Tick += (_, _) =>
        {
            _backoffTimer.Stop();
            if (_paused) _missedChanges = true; // re-listed when the game ends (M8d review I1)
            else Refresh();
        };
        Root = Normalize(root);
        Current = Root;
        _show = show;
        _logFailure = logFailure;
        _refreshTimer = new DispatcherTimer { Interval = RefreshDelay };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            Refresh();
        };
        _retryTimer = new DispatcherTimer { Interval = RetryDelay };
        _retryTimer.Tick += (_, _) => { if (!_refreshing && !_paused) Refresh(); }; // never pile up blocked listings
        Refresh();
    }

    /// <summary>"Downloads › Mods › Old" while browsing below the Portal's folder.</summary>
    public string Breadcrumb(string title)
    {
        var below = Path.GetRelativePath(Root, Current);
        return below == "." ? title : title + " › " + below.Replace("\\", " › ");
    }

    /// <summary>True when the last listing showed this ref as a folder (no disk access: a network folder may block).</summary>
    public bool IsListedFolder(string itemRef) => _listedFolders.Contains(itemRef);

    /// <summary>Shows a subfolder of the Portal (double-click on a folder inside it).</summary>
    public void Browse(string folder)
    {
        Current = Normalize(folder);
        Refresh();
    }

    /// <summary>One level up, never above the Portal's own folder.</summary>
    public void Back()
    {
        if (!CanGoBack) return;
        Current = Path.GetDirectoryName(Current) is { } parent ? Normalize(parent) : Root;
        Refresh();
    }

    /// <summary>
    /// Re-arms the watcher and re-lists the shown folder in the background; only the newest request is shown. The
    /// watcher is re-created each time: a folder deleted and created again ends the old one.
    /// </summary>
    public void Refresh()
    {
        if (_disposed) return;
        if (DateTime.UtcNow < _releasedUntil)
        {
            _retryTimer.Start(); // a refresh queued just before the release would re-open the drive: the retry comes back later
            return;
        }
        _refreshing = true;
        var generation = ++_generation;
        var folder = Current;
        var dispatcher = _refreshTimer.Dispatcher;
        Task.Run(() =>
        {
            var watcher = new FolderWatcher(folder, _logFailure);
            var removal = watcher.HeldFolder is { } held ? DeviceRemovalNotice.TryRegister(_noticeOwner, held, LogNoticeFailureOnce) : null;
            var items = FolderItems.TryList(folder);
            dispatcher.BeginInvoke(() =>
            {
                if (generation == _generation) _refreshing = false;
                if (_disposed || generation != _generation)
                {
                    watcher.Dispose(); // a newer refresh (or the fence's deletion) superseded this one
                    removal?.Dispose();
                    return;
                }
                _watcher?.Dispose();
                _watcher = watcher;
                _removal?.Dispose();
                _removal = removal;
                _lastArm = DateTime.UtcNow;
                watcher.Changed += () => dispatcher.BeginInvoke(ScheduleRefresh);
                watcher.Failed += () => dispatcher.BeginInvoke(OnWatcherFailed);
                if (watcher.HasFailed) OnWatcherFailed(); // it failed while arming, before this subscription (M8d review I2)
                _listedFolders = items is null
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(items.Where(item => item.IsFolder).Select(item => item.ItemRef), StringComparer.OrdinalIgnoreCase);
                // Unreadable or unwatched (drive not there yet): try again every few seconds until it is.
                if (items is null || !watcher.IsWatching) _retryTimer.Start();
                else _retryTimer.Stop();
                _show(items);
            });
        });
    }

    /// <summary>Game mode (spec §4.7): while paused, folder changes only mark the Portal stale; resuming re-lists once.</summary>
    public void SetPaused(bool paused)
    {
        _paused = paused;
        if (paused || !_missedChanges) return;
        _missedChanges = false;
        Refresh();
    }

    /// <summary>
    /// Windows asks to remove the drive holding this handle: close everything the Portal holds there, so "Safely Remove"
    /// and Eject work while it is shown (M8d). The Portal shows the folder as unavailable and comes back by itself when
    /// the drive does (the retry timer), or right away if the removal was refused.
    /// </summary>
    public bool ReleaseForRemoval(nint handle)
    {
        if (_removal is null || _removal.Handle != handle) return false;
        ++_generation; // a listing in flight must not re-open the folder
        _refreshing = false;
        _refreshTimer.Stop();
        _backoffTimer.Stop();
        _watcher?.Dispose();
        _watcher = null;
        _removal.Dispose();
        _removal = null;
        _listedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _releasedUntil = DateTime.UtcNow + ReleaseGrace;
        _show(null);
        _retryTimer.Start(); // every 7 s, after the grace: back when the drive is, or right away if the removal was refused
        return true;
    }

    /// <summary>A drive that refuses removal notices (a virtual drive reporting "Fixed") is said once, not on every refresh (M8d review).</summary>
    private void LogNoticeFailureOnce(Exception failure)
    {
        if (_noticeFailureLogged) return;
        _noticeFailureLogged = true;
        _logFailure(failure);
    }

    /// <summary>A watcher that fails again soon after each re-arm waits longer each time, up to a minute (M8d, M4 review).</summary>
    private void OnWatcherFailed()
    {
        if (_disposed || _backoffTimer.IsEnabled) return;
        if (_paused)
        {
            _missedChanges = true; // a stopped watcher during a game: re-list (and re-arm) when it ends (M8d review I1)
            return;
        }
        _failureDelay = NeoFences.Core.Lifecycle.WatcherBackoff.Next(_failureDelay, lastRearm: _lastArm, failureAt: DateTime.UtcNow);
        _backoffTimer.Interval = _failureDelay;
        _backoffTimer.Start();
        Serilog.Log.Information("Portal folder watcher stopped ({Folder}); re-listing after {Delay}", Current, _failureDelay);
    }

    private void ScheduleRefresh()
    {
        if (_paused)
        {
            _missedChanges = true;
            return;
        }
        // Not restarted by every event: a file that keeps being written still refreshes the Portal every 250 ms.
        if (!_disposed && !_refreshTimer.IsEnabled) _refreshTimer.Start();
    }

    /// <summary>A path without a trailing separator, except a drive root ("D:\").</summary>
    private static string Normalize(string folder)
    {
        var trimmed = folder.TrimEnd('\\', '/');
        return trimmed.Length == 2 && trimmed[1] == ':' ? trimmed + "\\" : trimmed;
    }

    public void Dispose()
    {
        _disposed = true;
        _refreshTimer.Stop();
        _retryTimer.Stop();
        _backoffTimer.Stop();
        _watcher?.Dispose();
        _watcher = null;
        _removal?.Dispose();
        _removal = null;
    }
}
```
`src/NeoFences.App/FenceHost.Library.cs`:
```csharp
using System.IO;
using System.Windows.Threading;
using NeoFences.Core.Library;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// The Game Library fence (M12, spec 2026-10-03-game-library-design, ADR-032): scans the launchers, the Xbox app, the
/// user's game folders and Desktop game shortcuts on its own STA thread, merges them (Core <see cref="GameCatalog"/>),
/// keeps one shortcut per game in <see cref="AppPaths.LibraryDirectory"/> and shows that folder like a Portal, as tiles.
/// Nothing is started or changed outside NeoFences' own folder.
/// </summary>
public sealed partial class FenceHost
{
    private static readonly TimeSpan LibraryQuietTime = TimeSpan.FromSeconds(5);

    private LibraryState _library = new();
    private IReadOnlyList<HiddenGame> _hiddenGames = []; // hidden games found by the last scan, with every id they go by
    // Steam's / Epic's install lists and the game folders, each with a removal notice so "Safely remove" works (final review I2).
    private readonly List<(FolderWatcher Watcher, DeviceRemovalNotice? Notice)> _libraryWatchers = [];
    private DispatcherTimer? _libraryTimer;
    private bool _libraryActive, _libraryScanning, _libraryScanAgain, _libraryDeferred;
    private bool _libraryStopped; // NeoFences is exiting: a scan finishing now re-arms nothing (M13a)
    private string _libraryStatus = "Not scanned yet.";

    private bool HasLibraryFence => _config.Fences.Any(fence => fence.Source.Kind == FenceSourceKind.Library);

    /// <summary>Starts the library when its fence appears, stops watching when it goes (EnsurePortals calls this).</summary>
    private void UpdateLibrary()
    {
        if (HasLibraryFence == _libraryActive) return;
        _libraryActive = !_libraryActive;
        if (_libraryActive)
        {
            _library = LibraryWriter.ReadIndex(AppPaths.LibraryDirectory); // the last scan, until this one finishes
            ScanLibrary();
        }
        else
        {
            StopLibraryWatchers();
            _libraryTimer?.Stop();
        }
    }

    /// <summary>Rescans in the background; one scan at a time (a request during a scan runs once after it); waits during a game.</summary>
    private void ScanLibrary()
    {
        if (!_libraryActive || _libraryStopped) return;
        if (Current.ShellWorkDeferred)
        {
            _libraryDeferred = true; // after the game (ApplyDeferredShellWork)
            return;
        }
        if (_libraryScanning)
        {
            _libraryScanAgain = true;
            return;
        }
        _libraryScanning = true;
        var settings = _config.Library;
        var previous = _library;
        var dispatcher = Dispatcher.CurrentDispatcher;
        var noticeOwner = _messages.Handle;
        ShellWorker.RunAlone(() =>
        {
            try
            {
                void LogFailure(string what, Exception failure) => Log.Warning(failure, "game library: {What} could not be read", what);
                var scans = GameScanners.ScanAll(settings, LogFailure);
                var everything = GameCatalog.Merge(scans, previous.Items.Select(item => item.Game).ToList(), hidden: []);
                var hidden = settings.Hidden.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var games = everything.Where(game => !GameCatalog.IdsOf(game).Any(hidden.Contains)).ToList();
                var plan = LibraryFiles.Plan(previous.Items, games);
                var state = LibraryWriter.Apply(AppPaths.LibraryDirectory, previous.Items, plan,
                    logFailure: (file, failure) => Log.Warning(failure, "game library: {File} could not be written", file));
                var hiddenGames = everything.Where(game => GameCatalog.IdsOf(game).Any(hidden.Contains))
                    .Select(game => new HiddenGame(GameCatalog.IdsOf(game).First(hidden.Contains), game.Name, GameCatalog.IdsOf(game))).ToList();
                var unreadable = scans.Where(scan => !scan.Readable).Select(scan => scan.ScanKey).ToList();
                // Built here, off the UI thread: opening a watcher on a sleeping disk or a network share can take seconds (final review I2).
                var watch = BuildLibraryWatchers(GameScanners.WatchFolders(settings), noticeOwner);
                Log.Information("game library: {Count} games ({Written} written, {Removed} removed); unreadable: {Unreadable}",
                    state.Items.Count, plan.Write.Count, plan.Delete.Count, unreadable);
                dispatcher.BeginInvoke(() => OnLibraryScanned(state, hiddenGames, unreadable, watch));
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                Log.Warning(failure, "game library scan failed; the library keeps its games"); // hard rule 7
                dispatcher.BeginInvoke(() => OnLibraryScanned(null, null, ["library"], []));
            }
        }, name: "NeoFences game library");
    }

    private void OnLibraryScanned(LibraryState? state, IReadOnlyList<HiddenGame>? hiddenGames, IReadOnlyList<string> unreadable,
        IReadOnlyList<(FolderWatcher Watcher, DeviceRemovalNotice? Notice)> watch)
    {
        _libraryScanning = false;
        if (_libraryStopped)
        {
            DisposeWatchers(watch);
            return;
        }
        if (state is not null) _library = state;
        if (hiddenGames is not null) _hiddenGames = hiddenGames;
        _libraryStatus = $"Last scan {DateTime.Now:HH:mm}: {_library.Items.Count} games"
                         + (unreadable.Count > 0 ? $"; not readable right now: {string.Join(", ", unreadable)}" : ".");
        if (state is null || !_libraryActive) DisposeWatchers(watch); // a failed scan keeps the watchers it had (final review I2)
        else ReplaceLibraryWatchers(watch);
        foreach (var window in _windows.Values.Where(window => window.IsLibrary)) RefreshPortal(window); // new art, order
        RefreshSettings();
        if (!_libraryScanAgain) return;
        _libraryScanAgain = false;
        ScanLibrary();
    }

    /// <summary>A watcher per folder, with a removal notice where Windows offers one. Runs on the scan thread.</summary>
    private static List<(FolderWatcher Watcher, DeviceRemovalNotice? Notice)> BuildLibraryWatchers(IReadOnlyList<string> folders, nint noticeOwner) =>
        folders.Select(folder =>
        {
            var watcher = new FolderWatcher(folder, failure => Log.Warning(failure, "game library: cannot watch {Folder}", folder));
            var notice = watcher.HeldFolder is { } held
                ? DeviceRemovalNotice.TryRegister(noticeOwner, held, failure => Log.Debug(failure, "game library: no removal notice for {Folder}", held))
                : null;
            return (watcher, notice);
        }).ToList();

    /// <summary>A change rescans after 5 quiet seconds (downloads write a lot); a watcher that stops (a network hiccup) rescans too, which re-arms it.</summary>
    private void ReplaceLibraryWatchers(IReadOnlyList<(FolderWatcher Watcher, DeviceRemovalNotice? Notice)> watch)
    {
        StopLibraryWatchers();
        var dispatcher = Dispatcher.CurrentDispatcher;
        foreach (var (watcher, notice) in watch)
        {
            watcher.Changed += () => dispatcher.BeginInvoke(ScheduleLibraryScan);
            watcher.Failed += () => dispatcher.BeginInvoke(ScheduleLibraryScan);
            if (watcher.HasFailed) ScheduleLibraryScan();
            _libraryWatchers.Add((watcher, notice));
        }
    }

    /// <summary>Windows asks to remove a drive the library watches (a USB Steam library): let go of it now (final review I2).</summary>
    private bool ReleaseLibraryForRemoval(nint handle)
    {
        var index = _libraryWatchers.FindIndex(entry => entry.Notice?.Handle == handle);
        if (index < 0) return false;
        DisposeWatchers([_libraryWatchers[index]]);
        _libraryWatchers.RemoveAt(index);
        ScheduleLibraryScan(); // re-arms what is still there once the removal is done (or refused)
        return true;
    }

    private static void DisposeWatchers(IEnumerable<(FolderWatcher Watcher, DeviceRemovalNotice? Notice)> watch)
    {
        foreach (var (watcher, notice) in watch)
        {
            watcher.Dispose();
            notice?.Dispose();
        }
    }

    private void ScheduleLibraryScan()
    {
        if (_libraryTimer is null)
        {
            _libraryTimer = new DispatcherTimer { Interval = LibraryQuietTime };
            _libraryTimer.Tick += (_, _) =>
            {
                _libraryTimer.Stop();
                ScanLibrary();
            };
        }
        _libraryTimer.Stop();
        _libraryTimer.Start();
    }

    private void StopLibraryWatchers()
    {
        DisposeWatchers(_libraryWatchers);
        _libraryWatchers.Clear();
    }

    /// <summary>The library's shortcuts in catalog (A–Z) order; files NeoFences did not write (its index) are not shown.</summary>
    private IReadOnlyList<string> LibraryOrder(IReadOnlyList<ItemInfo> listed)
    {
        var position = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (item, index) in _library.Items.Select((item, index) => (item, index))) position.TryAdd(item.FileName, index); // never throws on the UI thread
        return listed.Where(item => position.ContainsKey(Path.GetFileName(item.ItemRef)))
            .OrderBy(item => position[Path.GetFileName(item.ItemRef)]).Select(item => item.ItemRef).ToList();
    }

    /// <summary>Tile art by item ref: a poster, or an image logo (Xbox); programs' icons come from the shell.</summary>
    private IReadOnlyDictionary<string, (string Path, bool IsPoster)> LibraryArt()
    {
        var art = new Dictionary<string, (string Path, bool IsPoster)>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _library.Items)
        {
            var itemRef = Path.Combine(AppPaths.LibraryDirectory, item.FileName);
            if (item.Game.Poster is { } poster) art[itemRef] = (poster, true);
            else if (item.Game.IconPath is { } logo && Path.GetExtension(logo).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg") art[itemRef] = (logo, false);
        }
        return art;
    }

    private LibraryItem? LibraryItemOf(string itemRef) =>
        _library.Items.FirstOrDefault(item => string.Equals(item.FileName, Path.GetFileName(itemRef), StringComparison.OrdinalIgnoreCase));

    /// <summary>Tray / fence menu → "New Game Library fence": one at most; with one already, its tab is shown.</summary>
    private void CreateLibraryFence()
    {
        if (_config.Fences.FirstOrDefault(fence => fence.Source.Kind == FenceSourceKind.Library) is { } existing)
        {
            if (FenceTabs.HostOf(_config, existing.Id) is { } host && _windows.TryGetValue(host.Id, out var window)) SwitchTab(window, existing.Id);
            return;
        }
        SetQuickHidden(false); // a new fence must show (as "New fence" does)
        var fence = Fence.Create("Games", FenceSource.Library) with { Sort = FenceSort.Name, IconSize = 64, Labels = _config.Settings.DefaultLabels };
        _config = _config with { Fences = [.. _config.Fences, fence] };
        Log.Information("Game Library fence created");
        SyncBoxes();
        SaveNow();
        if (_config.Library.Folders.Count > 0) return;
        // Spec §4: D:\GameLibrary when it exists — checked off the UI thread (a sleeping disk can take seconds, M13a).
        var dispatcher = Dispatcher.CurrentDispatcher;
        Task.Run(() => Directory.Exists(DefaultGameFolder)).ContinueWith(found =>
        {
            if (!found.Result || _config.Library.Folders.Count > 0) return;
            _config = _config with { Library = _config.Library with { Folders = [DefaultGameFolder] } };
            SaveNow();
            ScanLibrary();
            RefreshSettings();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Right-click → "Hide from library" (or Delete): the game leaves the library until "Show again" in Settings.</summary>
    private void HideGames(IReadOnlyList<string> itemRefs)
    {
        var ids = itemRefs.Select(LibraryItemOf).OfType<LibraryItem>().SelectMany(item => GameCatalog.IdsOf(item.Game)).ToList(); // every source of the game
        if (ids.Count == 0) return;
        _config = _config with { Library = _config.Library with { Hidden = [.. _config.Library.Hidden.Union(ids, StringComparer.OrdinalIgnoreCase)] } };
        Log.Information("game library: hid {Ids}", ids);
        SaveNow();
        ScanLibrary();
    }

    private const string DefaultGameFolder = @"D:\GameLibrary";

    private void OpenInstallFolder(FenceWindow window, string itemRef)
    {
        // Opened on its own thread; a missing folder is logged there (no disk check on the UI thread, M13a).
        if (LibraryItemOf(itemRef)?.Game.InstallFolder is { } folder) OpenItem(folder, ownerHandle: window.Handle);
        else Log.Information("game library: no install folder known for {ItemRef}", itemRef);
    }

    private void ShowLibraryItemMenu(FenceWindow window, IReadOnlyList<string> itemRefs, int screenX, int screenY, bool extended)
    {
        var choice = ShellItemMenu.Show(window.Handle, itemRefs, screenX, screenY, extended,
            logFailure: failure => Log.Warning(failure, "item menu or its command failed for {ItemRefs}", itemRefs),
            customCommands: ["Hide from library", "Open install folder"], canRename: false, out var custom);
        if (choice == ItemMenuChoice.Delete || (choice == ItemMenuChoice.Custom && custom == 0)) HideGames(itemRefs); // nothing goes to the Recycle Bin
        else if (choice == ItemMenuChoice.Custom && custom == 1) OpenInstallFolder(window, itemRefs[0]);
    }

    private LibraryView LibrarySettingsView() => new(
        HasFence: HasLibraryFence,
        Folders: _config.Library.Folders,
        Sources: _config.Library.Sources,
        Hidden: HiddenGamesForSettings(),
        Status: HasLibraryFence ? _libraryStatus : "No Game Library fence yet: tray → New Game Library fence.");

    private void WireLibrarySettings(SettingsWindow window)
    {
        void Change(Func<LibrarySettings, LibrarySettings> edit, string what)
        {
            _config = _config with { Library = edit(_config.Library) };
            Log.Information("game library settings: {What}", what);
            SaveNow();
            ScanLibrary();
            RefreshSettings();
        }
        window.LibraryFoldersChanged += folders => Change(library => library with { Folders = folders }, "folders");
        window.LibrarySourcesChanged += sources => Change(library => library with { Sources = sources }, "sources");
        window.ShowGameAgainRequested += id =>
        {
            var ids = (_hiddenGames.FirstOrDefault(game => game.AllIds.Contains(id, StringComparer.OrdinalIgnoreCase))?.AllIds ?? [id]).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Change(library => library with { Hidden = [.. library.Hidden.Where(hiddenId => !ids.Contains(hiddenId))] }, "show again");
        };
        window.RefreshLibraryRequested += ScanLibrary;
    }

    /// <summary>One row per hidden game (all its ids together); a hidden id no scan finds any more is listed by itself.</summary>
    private IReadOnlyList<HiddenGame> HiddenGamesForSettings()
    {
        var hidden = _config.Library.Hidden;
        var known = _hiddenGames.Where(game => game.AllIds.Any(id => hidden.Contains(id, StringComparer.OrdinalIgnoreCase))).ToList();
        var orphans = hidden.Where(id => !known.Any(game => game.AllIds.Contains(id, StringComparer.OrdinalIgnoreCase))).Select(id => new HiddenGame(id, id, [id]));
        return [.. known, .. orphans];
    }
}
```
`src/NeoFences.App/App.cs`:
```csharp
using System.IO;
using System.Windows;
using System.Windows.Threading;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>The WPF application in normal mode (see <see cref="Program"/>).</summary>
public sealed class App : Application
{
    private Mutex? _singleInstance;
    private EventWaitHandle? _exitSignal;
    private RegisteredWaitHandle? _exitWait;
    private bool _ownsSingleInstance;
    private FenceHost? _host;

    public App()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    protected override void OnStartup(StartupEventArgs startupArgs)
    {
        base.OnStartup(startupArgs);

        _singleInstance = new Mutex(initiallyOwned: true, name: @"Local\NeoFences.Main", out _ownsSingleInstance);
        // An instance still exiting (Setup closing the old version while starting the new one) gets a few seconds to go,
        // instead of this one quitting silently and leaving the desktop without fences (M8c: 1.1.0 did not start after Setup).
        if (!_ownsSingleInstance) _ownsSingleInstance = WaitForPreviousInstance(_singleInstance);
        else
        {
            using var staleExit = ExitSignal();
            staleExit.Reset(); // a stale "--exit" from before this start must not stop it
        }
        ConfigureLogging(fileName: "neofences-.log");
        if (!_ownsSingleInstance)
        {
            Log.Information("NeoFences is already running (pid {ProcessId} exits)", Environment.ProcessId);
            Log.CloseAndFlush();
            Shutdown();
            return;
        }
        Log.Information("NeoFences starting, pid {ProcessId}, OS {OsVersion}", Environment.ProcessId, Environment.OSVersion.Version);
        if (SessionState.IsShuttingDown())
        {
            // Never hide icons in a session that is ending: they would stay hidden at the next sign-in (FWF_NOICONS persists).
            Log.Warning("session is shutting down; not starting");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, unhandled) => OnFatal(unhandled.Exception, source: "UI thread");
        AppDomain.CurrentDomain.UnhandledException += (_, unhandled) => OnFatal(unhandled.ExceptionObject as Exception, source: "background thread");
        // WPF answers WM_QUERYENDSESSION itself and then shuts the app down (it cannot be bypassed without blocking
        // sign-out). We bring the icons back right here, still inside the query (ADR-013).
        SessionEnding += (_, sessionEnding) =>
        {
            Log.Information("session ending ({Reason})", sessionEnding.ReasonSessionEnding);
            _host?.OnSessionEnding();
        };

        _exitSignal = ExitSignal();
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitSignal, (_, _) => Dispatcher.BeginInvoke(Shutdown), null, Timeout.Infinite, executeOnlyOnce: true);

        _host = new FenceHost();
        _host.ExitRequested += Shutdown;
        _host.Start();
    }

    private void OnFatal(Exception? failure, string source)
    {
        // Not handled on purpose: the process dies and the watchdog restores icons and restarts us (ADR-005).
        Log.Fatal(failure, "unhandled exception on the {Source}", source);
        _host?.EmergencyRestoreIcons();
        Log.CloseAndFlush();
    }

    protected override void OnExit(ExitEventArgs exitArgs)
    {
        if (_host is not null)
        {
            _host.Shutdown();
            Log.Information("NeoFences exited");
        }
        Log.CloseAndFlush();
        _exitWait?.Unregister(null);
        _exitSignal?.Dispose();
        if (_ownsSingleInstance) _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(exitArgs);
    }

    /// <summary>Shared: a second instance (or an install hook) can add its line while the running one holds the file.</summary>
    public static void ConfigureLogging(string fileName) =>
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(AppPaths.LogsDirectory, fileName), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, shared: true)
            .CreateLogger();

    /// <summary>
    /// "--exit" sets this; manual-reset so a copy waiting to start sees it too and gives up instead of starting as the
    /// running one exits (M13a). The owner resets it when it starts.
    /// </summary>
    private static EventWaitHandle ExitSignal() => new(initialState: false, EventResetMode.ManualReset, Program.ExitSignalName);

    private static bool WaitForPreviousInstance(Mutex singleInstance)
    {
        try
        {
            using var exit = ExitSignal();
            // Whichever comes first: the previous instance leaving (start), or "--exit" meaning everyone (give up).
            return WaitHandle.WaitAny([singleInstance, exit], TimeSpan.FromSeconds(5)) == 0;
        }
        catch (AbandonedMutexException)
        {
            return true; // the previous instance was killed: the mutex is ours now
        }
    }
}
```
`src/NeoFences.App/FenceHost.cs`:
```csharp
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using NeoFences.Core.Config;
using NeoFences.Core.Input;
using NeoFences.Core.Layouts;
using NeoFences.Core.Lifecycle;
using NeoFences.Core.Membership;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Owns the fences on the desktop: loads the config, places one <see cref="FenceWindow"/> per fence on the current
/// monitors, keeps fence contents in step with the Desktop folders (reconcile at start, then watcher events),
/// saves changes (debounced 500 ms, ADR-006), and keeps everything attached through display changes,
/// Explorer restarts and sign-out (ADR-011, ADR-013). Every Win32/COM call goes through NeoFences.Shell.
/// </summary>
public sealed partial class FenceHost
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ReattachInterval = TimeSpan.FromMilliseconds(500);
    private const double SnapGapDips = 8;        // spec §6: 8 px spacing from other fences and screen edges
    private const double SnapThresholdDips = 12; // how close an edge must come before it snaps
    private const int ReattachAttempts = 10;
    private const int PeekHotkeyId = 1;
    private const int PeekEscapeHotkeyId = 2;
    private static readonly TimeSpan DrawFrame = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan TrayRetryInterval = TimeSpan.FromSeconds(2);
    private const int TrayRetryAttempts = 15;

    private readonly ConfigStore _store = new(AppPaths.DataDirectory);
    private readonly Watchdog _watchdog = new(AppPaths.DataDirectory, message => Log.Information("watchdog: {Message}", message));
    private readonly SystemMessageWindow _messages = new();
    private readonly Dictionary<string, FenceWindow> _windows = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _saveTimer;
    private readonly IconLoader _iconLoader = new(Dispatcher.CurrentDispatcher);
    private DesktopWatcher? _desktopWatcher;
    private readonly ShellWorker _shellWorker = new(); // open, recycle, rename: off the UI thread, on STA (M3a review)
    private SpecialIconNotifications? _specialIcons;
    private bool _specialIconsDeferred;
    private readonly SnapshotStore _snapshots = new(Path.Combine(AppPaths.DataDirectory, "snapshots")); // M10
    private readonly HashSet<string> _loggedSnapshotProblems = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _specialIconsTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    // Rules wait until arrivals are quiet: a shortcut or file still being written reads wrong (M11 smoke X4, review M3).
    private readonly DispatcherTimer _filingTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private readonly List<string> _pendingArrivals = [];
    // Watcher trouble arrives in bursts: one re-arm and one reconcile per burst; a watcher that fails again at once waits longer (M8a review).
    private readonly DispatcherTimer _watcherRecoveryTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _rearmWatcher;
    private DateTime _lastWatcherRearm = DateTime.MinValue;
    private TimeSpan _watcherRearmDelay = WatcherBackoff.First;
    // Safe-save memory and expected drop arrivals (FenceMembership.SafeSaveWindow).
    private IReadOnlyList<RememberedPlacement> _rememberedPlacements = [];
    private readonly Dictionary<string, IDisposable> _dropRegistrations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PortalState> _portals = new(StringComparer.Ordinal); // M4: Portal fences by id
    private NeoFencesConfig _config = NeoFencesConfig.CreateDefault();
    private IReadOnlyList<MonitorPlacement> _monitors = [];
    private bool _takeoverActive;
    private bool _lightTheme = SystemTheme.AppsUseLightTheme();
    private bool _sessionEnding;
    // M5 desktop gestures
    private DesktopMouseHook? _mouseHook;
    private bool _quickHidden;              // double-click on the desktop: fences (and icons) hidden until the next one
    private bool _iconsHiddenByUser;        // RunState.IconsHiddenByUser: set when quick-hide begins, cleared when it ends
    private DrawFenceOverlay? _drawOverlay; // right-drag on the desktop: the fence being drawn
    private DispatcherTimer? _drawTimer;
    private (int X, int Y) _drawStart;
    private GlobalHotkey? _peekHotkey;
    private GlobalHotkey? _peekEscapeHotkey; // Esc ends Peek; registered only while peeking
    private bool _peeking;
    private bool _recordingHotkey;          // Settings' hotkey box has the keyboard: the Peek hotkey is released
    private string? _peekHotkeyProblem;     // why the Peek hotkey is not registered (Settings shows it), null when it is
    private bool _cornersLogged;
    private FenceWindow? _movingWindow; // the box being moved by its title (not resized): it may merge where it is dropped (M9)
    // M6a
    private bool _paused;                    // tray: Pause NeoFences (not saved)
    private bool _gameMode;                  // a full-screen app is in front: idle (spec §4.7, ADR-021)
    private ForegroundWatcher? _foregroundWatcher;
    private TrayIcon? _trayIcon;
    private readonly List<DesktopChange> _deferredDesktopChanges = [];
    private bool _reconcileDeferred;
    private const int MaxDeferredDesktopChanges = 500;
    private const int TrayNewFence = 1, TrayQuickHide = 2, TrayPeek = 3, TrayPause = 4, TrayExit = 5, TraySettings = 6;
    // Snapshots (M10): "Restore snapshot" lists the newest few by id TrayRestoreFirst + index.
    private const int TrayTakeSnapshot = 7, TrayRestoreMenu = 8, TrayRestoreBefore = 9, TrayRestoreFirst = 100, TrayRestoreCount = 10;
    private const int TrayNewLibrary = 11; // M12
    private SettingsWindow? _settingsWindow; // M6b: one at a time

    public event Action? ExitRequested;

    public FenceHost()
    {
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => SaveNow();
        _messages.ExplorerRestarted += OnExplorerRestarted;
        _messages.DisplayChanged += OnDisplayChanged;
        _messages.ThemeChanged += OnThemeChanged;
        _messages.HotkeyPressed += OnHotkey;
        _messages.TrayMenuRequested += ShowTrayMenu;
        _messages.SessionUnlocked += OnSessionUnlocked;
        _messages.SpecialIconsChanged += ScheduleSpecialIconRefresh;
        _messages.DeviceRemovalRequested += handle =>
        {
            foreach (var (fenceId, portal) in _portals)
            {
                if (portal.ReleaseForRemoval(handle)) Log.Information("drive of Portal {FenceId} is being removed: released it", fenceId);
            }
            if (ReleaseLibraryForRemoval(handle)) Log.Information("a drive the game library watches is being removed: released it");
        };
        _specialIconsTimer.Tick += (_, _) => RefreshSpecialIcons();
        _filingTimer.Tick += (_, _) => FilePendingArrivals();
        _watcherRecoveryTimer.Tick += (_, _) => RecoverDesktopWatcher();
    }

    public void Start()
    {
        var loaded = _store.Load();
        Log.Information("config loaded from {Source} (read-only: {IsReadOnly}, corrupt copy: {CorruptCopyPath})",
            loaded.Source, loaded.IsReadOnly, loaded.CorruptCopyPath);
        _config = loaded.Config;
        _watchdog.LaunchDetached(Environment.ProcessId);
        ApplyStartup(); // after a power loss NeoFences must come back by itself (ADR-019)

        RefreshMonitors();
        foreach (var box in FenceTabs.Boxes(_config)) OpenWindow(box); // one window per box (M9)
        EnsurePortals();
        StartDesktopWatcher(); // first: an item created while the startup reconcile lists the desktop is not missed (M8a)
        ReconcileDesktop();
        StartSpecialIconNotifications();
        ApplyLayout();
        if (_config.Settings.Takeover) SetTakeover(true);
        else if (_watchdog.IsTakeoverActiveMarked)
        {
            // Icons may still be hidden from a run whose Takeover-off never got saved (both processes killed): show them.
            Log.Warning("takeover-active marker found while Takeover is off; showing desktop icons");
            SetIconsHidden(false);
        }
        StartGestures();
        if (!_messages.SessionNotificationsActive) Log.Warning("unlock notices unavailable: the mouse hook is re-installed only after an Explorer restart");
        try
        {
            _trayIcon = new TrayIcon(_messages.Handle, TrayTooltip(), log: message => Log.Warning("{Message}", message));
            ShowTrayIcon();
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Error(failure, "tray icon unavailable; fences and gestures keep working"); // hard rule 7: only the tray is lost
        }
        StartGameMode();
        ScheduleSave();
    }

    /// <summary>
    /// The clock for safe-save and drop memory: wall time at start plus a monotonic stopwatch, so a clock change (time
    /// sync, daylight saving, the user) cannot expire or extend a memory early (M8a).
    /// </summary>
    private static readonly DateTimeOffset ClockStart = DateTimeOffset.Now;
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static DateTimeOffset Now => ClockStart + Clock.Elapsed;

    /// <summary>All of NeoFences' run-time modes together (Core rules: which fences, icons and hooks they imply).</summary>
    private RunState Current => new(Takeover: _takeoverActive, QuickHidden: _quickHidden, Paused: _paused, GameMode: _gameMode,
        IconsHiddenByUser: _iconsHiddenByUser);

    /// <summary>
    /// Windows asked to end the session (WPF's SessionEnding, inside WM_QUERYENDSESSION). WPF then shuts the app
    /// down, which may be the last thing that ever runs, so the icons come back now and the watchdog is told it is
    /// a session end, not a user exit: if the user cancels the shutdown, the watchdog restarts NeoFences (ADR-013).
    /// </summary>
    public void OnSessionEnding()
    {
        _sessionEnding = true;
        ApplyDeferredShellWork(); // Desktop changes queued during a game must be saved too (M6a review M2)
        SaveNow();
        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) SetIconsHidden(false);
        TryMarker(() => _watchdog.MarkSessionEnding(Environment.ProcessId), what: "session-ending marker");
    }

    /// <summary>Orderly exit: save, bring icons back, tell the watchdog all is well.</summary>
    public void Shutdown()
    {
        ApplyDeferredShellWork(); // Desktop changes queued during a game must be saved too (M6a review M2)
        SaveNow();
        _trayIcon?.Dispose(); // also on session end: a cancelled shutdown restarts us, and the old icon would linger (M8a)
        _trayIcon = null;
        if (_sessionEnding) return; // OnSessionEnding already restored and marked; no clean marker, so a cancel restarts us
        // The marker also catches a show that failed earlier (quick-hide ending while Explorer was busy; M5 review M6).
        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) SetIconsHidden(false);
        TryMarker(() => _watchdog.MarkCleanShutdown(Environment.ProcessId), what: "clean-shutdown marker");
        _foregroundWatcher?.Dispose();
        _mouseHook?.Dispose();
        _peekHotkey?.Dispose();
        _peekEscapeHotkey?.Dispose();
        _desktopWatcher?.Dispose();
        _desktopWatcher = null;
        _specialIcons?.Dispose();
        _libraryStopped = true;
        StopLibraryWatchers();
        _libraryTimer?.Stop();
        _shellWorker.Dispose();
        _iconLoader.Dispose();
        _messages.Dispose();
    }

    /// <summary>Best effort from the crash handler; the watchdog restores too.</summary>
    public void EmergencyRestoreIcons()
    {
        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) DesktopIcons.TrySetHidden(false);
    }

    /// <summary>One window per box (M9): it shows the box's active tab; roll-up and lock are the box's.</summary>
    private void OpenWindow(Fence box)
    {
        var shown = FenceTabs.ActiveOf(_config, box.Id);
        var window = new FenceWindow(shown with { RolledUp = box.RolledUp, Locked = box.Locked }, takeoverActive: _takeoverActive,
            lightTheme: _lightTheme, iconLoader: _iconLoader, rollupExpand: _config.Settings.RollupExpand)
        {
            // Spec §6: no animations when Windows' "Animation effects" are off; spec §4.7: none while gaming.
            AnimationsAllowed = () => !_gameMode && SystemParameters.ClientAreaAnimation,
            BoxId = box.Id,
        };
        window.TabSelected += fenceId => SwitchTab(window, fenceId);
        window.TabDropped += (fenceId, screenX, screenY) => OnTabDropped(window, fenceId, screenX, screenY);
        window.TabDragMoved += (screenX, screenY) =>
        {
            foreach (var other in _windows.Values) other.SetMergeHighlight(other != window && other.TitleRowContains(screenX, screenY));
        };
        window.TabColorRequested += color =>
        {
            _config = FenceTabs.SetColor(_config, window.FenceId, color);
            RefreshTabs(window);
            ScheduleSave();
        };
        window.DetachTabRequested += () => DetachTab(window, window.FenceId, dropPoint: null);
        window.TabCycleRequested += step => CycleTab(window, step);
        window.SetTabs(FenceTabs.TabsOf(_config, box.Id), shown.Id);
        window.SnapRect = (rect, edges) => SnapFence(window, rect, edges);
        window.MovedByUser += OnFenceMoved;
        window.RenameRequested += title => RenameFence(window, title);
        window.IconSizeRequested += iconSize => SetFenceIconSize(window, iconSize);
        window.LockToggled += locked => SetFenceLocked(window, locked);
        window.DeleteRequested += () => DeleteFence(window);
        window.NewFenceRequested += CreateFence;
        window.TakeoverToggled += SetTakeover;
        window.ExitRequested += () => ExitRequested?.Invoke();
        window.OpenRequested += itemRef => OpenOrBrowse(window, itemRef);
        window.OpenManyRequested += itemRefs =>
        {
            foreach (var itemRef in itemRefs) OpenItem(itemRef, ownerHandle: window.Handle); // folders open in Explorer
            SetPeek(false);
        };
        window.ItemMenuRequested += (itemRefs, screenX, screenY, fromKeyboard) => ShowItemMenu(window, itemRefs, screenX, screenY, fromKeyboard);
        window.RecycleRequested += itemRefs => RecycleItems(window, itemRefs);
        window.ItemRenameRequested += (itemRef, newName) => RenameItem(window, itemRef, newName);
        window.BackRequested += () => BrowsePortal(window, back: true);
        window.NewPortalRequested += () => CreatePortal(window);
        window.NewLibraryRequested += CreateLibraryFence;
        window.RefreshLibraryRequested += ScanLibrary;
        window.StartupToggled += SetStartWithWindows;
        window.SettingsRequested += OpenSettings;
        window.RulesRequested += () => OpenRulesFor(window.FenceId);
        window.LabelModeRequested += labels => SetFenceLabels(window, labels);
        window.SetShortcutArrows(_config.Settings.ShowShortcutArrows);
        window.SetStartupChecked(_config.Settings.StartWithWindows);
        window.SortRequested += sort => SortFence(window, sort);
        window.OpenFolderRequested += () => { if (_portals.TryGetValue(window.FenceId, out var portal)) OpenItem(portal.Current, ownerHandle: window.Handle); };
        window.DragRequested += itemRefs =>
            ShellDragDrop.TryDrag(window.Handle, itemRefs, logFailure: failure => Log.Warning(failure, "could not start dragging {ItemRefs}", itemRefs),
                copyOnly: window.IsLibrary); // a game dragged out of the library is copied, never moved (M12)
        window.TakeoverPromptAnswered += AnswerTakeoverPrompt;
        window.RollUpToggled += () => ToggleRollUp(window);
        new WindowInteropHelper(window).EnsureHandle(); // HWND exists (styles, blur) before the first Show
        if (window.CornersUnavailable && !_cornersLogged)
        {
            _cornersLogged = true;
            Log.Information("Windows refused rounded corners (Windows 10?): fences keep square blur corners"); // M8a review
        }
        RegisterDrops(window); // needs the HWND
        if (!DesktopHost.AttachToDesktop(window.Handle)) Log.Warning("fence {FenceId}: not attached to the desktop yet (no Progman)", box.Id);
        _windows[box.Id] = window;
        // A Portal tab that gets a window of its own (detached, or its host deleted) lists its folder now (final review I1).
        if (_portals.TryGetValue(shown.Id, out var shownPortal)) shownPortal.Refresh();
    }

    /// <summary>Full pass over the Desktop folders: at start and whenever watcher events were lost.</summary>
    private void ReconcileDesktop()
    {
        var listing = DesktopItems.Enumerate();
        var (reconciled, report) = FenceMembership.Reconcile(_config, listing.ItemRefs, listing.UnavailableFolders,
            remembered: _rememberedPlacements, now: Now);
        _config = reconciled;
        _rememberedPlacements = [.. _rememberedPlacements.Except(report.UsedMemories)]; // each memory places one item once (M8a review)
        if (listing.UnavailableFolders.Count > 0) Log.Warning("desktop folders not readable: {Folders}", listing.UnavailableFolders);
        if (report.Suspicious) Log.Warning("kept fenced items from an unreadable or empty desktop listing until a later reconcile");
        Log.Information("desktop reconciled: {AddedCount} added to the Inbox, {RemovedCount} removed", report.AddedToInbox.Count, report.Removed.Count);
        RefreshWindows();
        ScheduleSave();
        FileNewItems(report.AddedToInbox);
    }

    private void StartDesktopWatcher()
    {
        // A folder that cannot be watched degrades to "its changes show after a restart" (hard rule 7).
        _desktopWatcher = new DesktopWatcher((folder, failure) => Log.Error(failure, "cannot watch {Folder}; its changes show after a restart", folder));
        var dispatcher = Dispatcher.CurrentDispatcher;
        _desktopWatcher.Changed += change => dispatcher.BeginInvoke(() => OnDesktopChanged(change));
        _desktopWatcher.Overflowed += () => dispatcher.BeginInvoke(() => OnDesktopWatcherTrouble(rearm: true));
        _desktopWatcher.ReconcileNeeded += () => dispatcher.BeginInvoke(() => OnDesktopWatcherTrouble(rearm: false));
    }

    /// <summary>
    /// Events were lost or the watcher stopped (re-arm: .NET disables it after a non-overflow error), or one event could
    /// not be read (reconcile only). Bursts are gathered into one recovery (M8a review).
    /// </summary>
    private void OnDesktopWatcherTrouble(bool rearm)
    {
        if (_desktopWatcher is null) return; // shut down meanwhile
        if (rearm && !_rearmWatcher)
        {
            _rearmWatcher = true;
            // Measured when the failure arrives: the wait itself is not quiet time (Core WatcherBackoff, M8c review I3).
            _watcherRearmDelay = WatcherBackoff.Next(_watcherRearmDelay, lastRearm: _lastWatcherRearm, failureAt: DateTime.UtcNow);
            _watcherRecoveryTimer.Stop(); // a reconcile-only recovery already waiting now waits for the re-arm delay
            _watcherRecoveryTimer.Interval = _watcherRearmDelay;
            _watcherRecoveryTimer.Start();
            return;
        }
        if (_watcherRecoveryTimer.IsEnabled) return;
        _watcherRecoveryTimer.Interval = WatcherBackoff.First;
        _watcherRecoveryTimer.Start();
    }

    private void RecoverDesktopWatcher()
    {
        _watcherRecoveryTimer.Stop();
        if (_desktopWatcher is null) return;
        if (_rearmWatcher)
        {
            _rearmWatcher = false;
            _lastWatcherRearm = DateTime.UtcNow;
            Log.Warning("desktop watcher lost events or stopped; re-armed after {Delay} and reconciling", _watcherRearmDelay);
            _desktopWatcher.Dispose();
            StartDesktopWatcher();
        }
        else
        {
            Log.Information("a desktop change could not be read; reconciling");
        }
        if (Current.ShellWorkDeferred) _reconcileDeferred = true; // after the game
        else ReconcileDesktop();
    }

    /// <summary>Also after an Explorer restart: Explorer brokers the shell's change notices and forgets them (M8c review I6).</summary>
    private void StartSpecialIconNotifications()
    {
        _specialIcons?.Dispose();
        var dispatcher = Dispatcher.CurrentDispatcher;
        _specialIcons = new SpecialIconNotifications(_messages.Handle,
            settingsChanged: () => dispatcher.BeginInvoke(ScheduleSpecialIconRefresh),
            log: (what, failure) => Log.Warning(failure, "{What} unavailable: special icons change after a restart", what));
    }

    private void ScheduleSpecialIconRefresh()
    {
        _specialIconsTimer.Stop(); // a burst (several icons, a Recycle Bin emptying) is one refresh
        _specialIconsTimer.Start();
    }

    /// <summary>"Desktop icon settings" changed (reconcile), or the Recycle Bin turned full or empty (new icon) (M8c).</summary>
    private void RefreshSpecialIcons()
    {
        _specialIconsTimer.Stop();
        if (Current.ShellWorkDeferred)
        {
            _specialIconsDeferred = true; // games get every bit of the machine: after the game (M8c review)
            return;
        }
        var shown = DesktopItems.SpecialIconRefs().ToHashSet(ItemRef.Comparer);
        var fenced = _config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop).SelectMany(fence => fence.Items)
            .Where(itemRef => itemRef.StartsWith("::", StringComparison.Ordinal)).ToHashSet(ItemRef.Comparer);
        if (!shown.SetEquals(fenced))
        {
            Log.Information("desktop icon settings changed; reconciling");
            if (Current.ShellWorkDeferred) _reconcileDeferred = true;
            else ReconcileDesktop();
        }
        foreach (var window in _windows.Values) window.ReloadSpecialIcons();
        Log.Information("special icons refreshed");
    }

    private void OnDesktopChanged(DesktopChange change)
    {
        if (Current.ShellWorkDeferred)
        {
            // Applied in order when the game is left; a flood (a big download unpacking) becomes one reconcile instead (M8a).
            if (_deferredDesktopChanges.Count < MaxDeferredDesktopChanges) _deferredDesktopChanges.Add(change);
            else _reconcileDeferred = true;
            return;
        }
        var arrival = ArrivalOf(change); // before Apply: was the item fenced already?
        (_config, _rememberedPlacements) = FenceMembership.Apply(_config, change, _rememberedPlacements, Now);
        RefreshWindows();
        ScheduleSave();
        if (arrival is not null) FileNewItems([arrival]);
    }

    /// <summary>
    /// The item a change brings that rules may file (M11): a created item no fence holds yet (an attribute change on an
    /// existing item also arrives as "created", final review I1), or a download that just got its final name (I2).
    /// </summary>
    private string? ArrivalOf(DesktopChange change) => change switch
    {
        DesktopChange.Created created when !_config.Fences.Any(fence => fence.Items.Contains(created.ItemRef, ItemRef.Comparer)) => created.ItemRef,
        DesktopChange.Renamed renamed when Rules.IsDownloadRename(renamed.OldRef) => renamed.NewRef,
        _ => null,
    };

    /// <summary>
    /// Rules auto-sort (M11, spec §3): new items that landed in the Inbox go to the fence of the first rule they match.
    /// Their facts are read on the shell worker (a shortcut to an offline share can be slow); an item moved or deleted
    /// meanwhile stays put. Membership only: no file is touched.
    /// </summary>
    private void FileNewItems(IReadOnlyList<string> itemRefs)
    {
        if (itemRefs.Count == 0 || !_config.Rules.Any(rule => rule.Enabled)) return;
        _pendingArrivals.AddRange(itemRefs);
        _filingTimer.Stop(); // a burst (an unpack, an installer) is one read, 1.5 s after the last arrival
        _filingTimer.Start();
    }

    private void FilePendingArrivals()
    {
        _filingTimer.Stop();
        var inbox = _config.Inbox.Items.ToHashSet(ItemRef.Comparer);
        var arrivals = _pendingArrivals.Distinct(ItemRef.Comparer).Where(inbox.Contains).ToList(); // a safe-save memory or a drop placed it already
        _pendingArrivals.Clear();
        if (arrivals.Count == 0) return;
        ReadFactsThen(arrivals, facts =>
        {
            var stillInInbox = _config.Inbox.Items.ToHashSet(ItemRef.Comparer);
            var filed = Rules.File(_config, [.. facts.Where(fact => stillInInbox.Contains(fact.ItemRef))], DateTimeOffset.Now);
            if (ReferenceEquals(filed, _config)) return;
            var moved = _config.Inbox.Items.Except(filed.Inbox.Items, ItemRef.Comparer).ToList();
            Log.Information("rules filed {Count} new item(s): {ItemRefs}", moved.Count, moved);
            _config = filed;
            RefreshWindows();
            ScheduleSave();
        });
    }

    /// <summary>Reads item facts on the shell worker, then continues on the UI thread.</summary>
    private void ReadFactsThen(IReadOnlyList<string> itemRefs, Action<IReadOnlyList<ItemFacts>> then)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        _shellWorker.Run(() =>
        {
            var facts = ItemFactsReader.Read(itemRefs, logFailure: (itemRef, failure) => Log.Warning(failure, "rules: {ItemRef} could not be read", itemRef));
            dispatcher.BeginInvoke(() => then(facts));
        });
    }

    /// <summary>
    /// Settings → "Apply rules now" (M11, spec §3): every desktop item, hand-placed ones too. "Before restore" is written
    /// first, so tray → "Undo the last restore" puts everything back; nothing moves without it.
    /// </summary>
    private void ApplyRulesNow()
    {
        var itemRefs = _config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop).SelectMany(fence => fence.Items).ToList();
        ReadFactsThen(itemRefs, facts =>
        {
            var now = DateTimeOffset.Now;
            var moves = Rules.CountMoves(_config, facts, now);
            if (moves == 0)
            {
                _settingsWindow?.ShowRulesResult("Nothing to move: every item is where the rules want it.");
                return;
            }
            if (_snapshots.Save(Snapshots.Take(_config, name: $"Before applying rules ({now:d MMM HH:mm})", now: now), SnapshotStore.BeforeRestoreFileName) is null)
            {
                Log.Warning(_snapshots.LastFailure, "rules not applied: 'Before restore' could not be saved");
                _settingsWindow?.ShowRulesResult("Nothing moved: NeoFences could not save 'Before restore' first (see the log).");
                return;
            }
            _config = Rules.File(_config, facts, now);
            Log.Information("rules applied: {Count} item(s) moved", moves);
            SaveNow();
            RefreshWindows();
            RefreshSettings();
            var message = $"{moves} item{(moves == 1 ? "" : "s")} moved. Tray → Restore snapshot → Undo the last restore puts them back.";
            _settingsWindow?.ShowRulesResult(message);
            _trayIcon?.ShowBalloon("Rules applied", message);
        });
    }

    private void RefreshWindows()
    {
        var showPrompt = !_config.Settings.TakeoverPromptAnswered && !_takeoverActive;
        foreach (var window in _windows.Values)
        {
            if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is not { } shown) continue;
            if (!_portals.ContainsKey(shown.Id)) window.SetItems(shown.Items); // Portals refresh on their own (M4 review I1)
            window.ShowTakeoverPrompt(showPrompt && shown.IsInbox);
        }
    }

    private void OpenItem(string itemRef, nint ownerHandle)
    {
        // Off the UI thread: ShellExecute can block on a network timeout or a UAC prompt, freezing every fence (M2b review I6);
        // on an STA thread, as shell handlers and Windows' error dialog expect (M3a review).
        // Each open on its own thread: one waiting on an offline share (and its error dialog) holds up nothing else (M8c review I4).
        ShellWorker.RunAlone(() =>
        {
            if (!ShellItems.TryOpen(itemRef, ownerHandle)) Log.Warning("could not open {ItemRef}", itemRef);
        }, name: "NeoFences open");
    }

    /// <summary>Makes the fence accept drops (M3b): fence items and Desktop files move membership; other files go to Windows.</summary>
    private void RegisterDrops(FenceWindow window)
    {
        try
        {
            _dropRegistrations[window.BoxId] = ShellDragDrop.RegisterFence(window.Handle, new FenceDropHandlers(
                HitTest: window.HitTest,
                MoveItems: (itemRefs, insertAt) =>
                {
                    _config = FenceMembership.MoveItems(_config, itemRefs, window.FenceId, insertAt);
                    RefreshWindows();
                    ScheduleSave();
                },
                ExpectArrivals: (itemRefs, insertAt) =>
                    _rememberedPlacements = FenceMembership.ExpectArrivals(_rememberedPlacements, itemRefs, window.FenceId, insertAt, Now),
                Recycle: itemRefs => RecycleItems(window, itemRefs),
                ShowFeedback: window.ShowDropFeedback,
                LogFailure: failure => Log.Warning(failure, "drop on fence {FenceId} failed", window.FenceId),
                AcceptsDrops: () => !window.IsLibrary), // the library shows NeoFences' own shortcuts only (M12)
                portalFolder: () => _portals.TryGetValue(window.FenceId, out var portal) ? portal.Current : null);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Error(failure, "fence {FenceId} cannot accept drops", window.FenceId); // degrade: everything else still works
        }
    }

    private void ShowItemMenu(FenceWindow window, IReadOnlyList<string> itemRefs, int screenX, int screenY, bool fromKeyboard)
    {
        // Shift+right-click is the extended menu; Shift+F10 is just the keyboard's normal menu (M3a review).
        var extended = !fromKeyboard && System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);
        if (window.IsLibrary)
        {
            ShowLibraryItemMenu(window, itemRefs, screenX, screenY, extended);
            return;
        }
        var choice = ShellItemMenu.Show(window.Handle, itemRefs, screenX, screenY, extended,
            logFailure: failure => Log.Warning(failure, "item menu or its command failed for {ItemRefs}", itemRefs));
        switch (choice)
        {
            case ItemMenuChoice.Rename:
                window.BeginItemRename(itemRefs[0]); // the right-clicked item (it comes first)
                break;
            case ItemMenuChoice.Delete:
                RecycleItems(window, itemRefs); // always the Recycle Bin, even with Shift held (hard rule 1)
                break;
        }
    }

    /// <summary>
    /// Windows moves them to the Recycle Bin (with its own dialogs) on the shell worker, so a long recycle never freezes
    /// the fences (M3a review); the watcher then removes them from the fence.
    /// </summary>
    private void RecycleItems(FenceWindow window, IReadOnlyList<string> itemRefs)
    {
        if (window.IsLibrary)
        {
            HideGames(itemRefs); // Delete in the library hides the game; its shortcut is NeoFences' own (M12)
            return;
        }
        Log.Information("recycling {Count} item(s)", itemRefs.Count);
        var ownerHandle = window.Handle;
        var dispatcher = Dispatcher.CurrentDispatcher;
        _shellWorker.Run(() =>
        {
            var (started, refused, missing) = ShellFileOps.TryRecycle(LiveOwner(ownerHandle), itemRefs);
            if (!started) Log.Warning("recycle did not run or was cancelled: {ItemRefs}", itemRefs);
            if (missing.Count > 0) Log.Information("skipped {Count} item(s) that no longer exist: {ItemRefs}", missing.Count, missing);
            if (refused.Count > 0) dispatcher.BeginInvoke(() => ExplainRefusedRecycle(window, refused));
        });
    }

    /// <summary>No owner when the fence was deleted while the operation waited: Windows' dialogs then stand alone (M8c review).</summary>
    private static nint LiveOwner(nint ownerHandle) => FenceWindowChrome.IsLiveWindow(ownerHandle) ? ownerHandle : 0;

    private static void ExplainRefusedRecycle(FenceWindow window, IReadOnlyList<string> refused)
    {
        Log.Information("not deleting {Count} item(s) on drives without a Recycle Bin", refused.Count);
        System.Windows.MessageBox.Show(window,
            (refused.Count == 1 ? $"\"{Path.GetFileName(refused[0])}\" is" : $"{refused.Count} items are") +
            " on a drive without a Recycle Bin (a USB stick or network drive), so NeoFences won't delete it." +
            " Deleting there would be permanent; if you really mean it, delete it in Explorer.",
            "NeoFences", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    /// <summary>Windows renames the file (on the shell worker: its conflict dialogs); the watcher keeps it in its fence and position.</summary>
    private void RenameItem(FenceWindow window, string itemRef, string newName)
    {
        var ownerHandle = window.Handle;
        _shellWorker.Run(() =>
        {
            if (!ShellFileOps.TryRename(LiveOwner(ownerHandle), itemRef, newName)) Log.Warning("rename did not run or was cancelled: {ItemRef}", itemRef);
        });
    }

    private void AnswerTakeoverPrompt(bool hideIcons)
    {
        Log.Information("first-run question answered: hide desktop icons {HideIcons}", hideIcons);
        if (hideIcons) SetTakeover(true);
        else
        {
            _config = _config with { Settings = _config.Settings with { TakeoverPromptAnswered = true } };
            RefreshWindows();
            SaveNow();
        }
    }

    private void ApplyLayout()
    {
        if (_monitors.Count == 0)
        {
            Log.Warning("no monitors reported; keeping the current layout");
            return;
        }
        try
        {
            var (resolved, layout) = LayoutEngine.Resolve(_config, _monitors.Select(monitor => monitor.ToDisplayMonitor()).ToList());
            _config = resolved;
            foreach (var (fenceId, rect) in layout.Fences)
            {
                if (!_windows.TryGetValue(fenceId, out var window)) continue;
                var monitor = _monitors.First(candidate => candidate.DeviceId == rect.Monitor);
                window.Place(FencePlacement.ToPixels(rect, monitor));
                if (!window.IsVisible && Current.FencesVisible)
                {
                    window.ShowNow();
                    if (_peeking)
                    {
                        // A fence made during Peek (its menus no longer end it, M5 review I1) joins the others on top.
                        window.Peeking = true;
                        FenceWindowChrome.SetTopmost(window.Handle, topmost: true);
                    }
                    else FenceWindowChrome.SendToBack(window.Handle); // Show puts it above every app; fences live just above the desktop
                }
            }
        }
        catch (ArgumentException unusableDisplay)
        {
            // Garbage from a monitor query mid-change (M1 review): skip this resolve, the next display event retries.
            Log.Warning(unusableDisplay, "display query unusable; keeping the current layout");
        }
    }

    private void RefreshMonitors()
    {
        _monitors = Monitors.Enumerate();
        Log.Information("monitors: {Monitors}", string.Join("; ", _monitors.Select(monitor =>
            $"{monitor.DeviceId} {monitor.PixelWidth}x{monitor.PixelHeight}@{monitor.ScalePercent}% " +
            $"work {monitor.WorkLeftPx},{monitor.WorkTopPx} {monitor.WorkWidthPx}x{monitor.WorkHeightPx}{(monitor.IsPrimary ? " primary" : "")}")));
    }

    private void OnFenceMoved(FenceWindow window, PixelRect pixels)
    {
        var wasMove = ReferenceEquals(_movingWindow, window);
        _movingWindow = null;
        foreach (var other in _windows.Values) other.SetMergeHighlight(false);
        // Dropped by its title onto another fence's title row: the whole box joins that box as tabs (M9).
        var (cursorX, cursorY) = FenceWindowChrome.GetCursorPosition();
        if (wasMove && _windows.Values.FirstOrDefault(other => other != window && other.TitleRowContains(cursorX, cursorY)) is { } target)
        {
            Log.Information("fence {FenceId} merged into the box of {TargetId}", window.BoxId, target.BoxId);
            _config = FenceTabs.Merge(_config, movingFenceId: window.BoxId, targetFenceId: target.BoxId);
            SyncBoxes();
            return;
        }
        if (_monitors.Count == 0 || _config.LastLayoutFingerprint is not { } fingerprint) return;
        var monitor = FencePlacement.ContainingMonitor(pixels, _monitors);
        _config = LayoutEngine.WithFenceRect(_config, fingerprint: fingerprint, fenceId: window.BoxId, rect: FencePlacement.FromPixels(pixels, monitor));
        ScheduleSave();
    }

    private PixelRect SnapFence(FenceWindow window, PixelRect rect, SnapEdges edges)
    {
        // While a box moves, the title row it would merge into lights up; a resize never merges (M9).
        _movingWindow = edges == SnapEdges.Move ? window : null;
        if (_movingWindow is not null)
        {
            var (cursorX, cursorY) = FenceWindowChrome.GetCursorPosition();
            foreach (var other in _windows.Values) other.SetMergeHighlight(other != window && other.TitleRowContains(cursorX, cursorY));
        }
        if (_monitors.Count == 0) return rect;
        var monitor = FencePlacement.ContainingMonitor(rect, _monitors);
        var others = _windows.Values.Where(other => other != window && other.IsVisible).Select(other => FenceWindowChrome.GetPixelRect(other.Handle)).ToList();
        return Snapping.Snap(rect, edges,
            workArea: new PixelRect(monitor.WorkLeftPx, monitor.WorkTopPx, monitor.WorkWidthPx, monitor.WorkHeightPx),
            others: others,
            gapPx: (int)Math.Round(SnapGapDips * monitor.Scale),
            thresholdPx: (int)Math.Round(SnapThresholdDips * monitor.Scale),
            // A resize snap never makes the fence smaller than its minimum (M2c review carry-over).
            minWidthPx: (int)Math.Round(LayoutEngine.MinWidth * monitor.Scale),
            minHeightPx: (int)Math.Round(LayoutEngine.MinHeight * monitor.Scale));
    }

    private void RenameFence(FenceWindow window, string title)
    {
        _config = FenceEdits.Rename(_config, window.FenceId, title);
        window.SetTitle(_config.Fences.First(fence => fence.Id == window.FenceId).Title);
        RefreshTabs(window);
        RefreshPortal(window); // a Portal browsing a subfolder shows its breadcrumb again
        ScheduleSave();
    }

    private void SetFenceIconSize(FenceWindow window, int iconSize)
    {
        _config = FenceEdits.SetIconSize(_config, window.FenceId, iconSize);
        window.SetIconSize(iconSize);
        ScheduleSave();
    }

    private void SetFenceLocked(FenceWindow window, bool locked)
    {
        _config = FenceEdits.SetLocked(_config, window.BoxId, locked); // the box's (M9)
        window.SetLocked(locked);
        ScheduleSave();
    }

    /// <summary>Removes the fence; its items go to the Inbox. Files are never touched (hard rule 1).</summary>
    private void DeleteFence(FenceWindow window)
    {
        _config = FenceMembership.DeleteFence(_config, window.FenceId); // the shown tab; the rest of its box stays (M9)
        SyncBoxes(); // closes the window when its box is gone; a deleted Portal's watcher ends (the folder is never touched)
    }

    private void OnThemeChanged()
    {
        var light = SystemTheme.AppsUseLightTheme();
        if (light == _lightTheme) return;
        _lightTheme = light;
        Log.Information("Windows app mode changed; light: {Light}", light);
        foreach (var window in _windows.Values) window.ApplyTheme(light);
    }

    private void OnDisplayChanged()
    {
        RefreshMonitors();
        ApplyLayout();
        ScheduleSave();
    }

    /// <summary>"New Portal fence…": Windows' folder dialog, then a fence that mirrors the folder (M4).</summary>
    private void CreatePortal(FenceWindow owner)
    {
        var folder = FolderPicker.TryPick(owner.Handle, "Choose the folder for the new Portal fence",
            logFailure: failure => Log.Warning(failure, "folder dialog failed"));
        if (folder is null) return;
        var title = Path.GetFileName(folder.TrimEnd('\\')) is { Length: > 0 } name ? name : folder;
        (_config, _) = FenceMembership.CreatePortal(_config, title: title, folderPath: folder);
        Log.Information("Portal fence created for {Folder}", folder);
        SyncBoxes();
    }

    /// <summary>
    /// Brings the windows in line with the boxes after any change to them (M9): a window per box, showing the box's
    /// active tab, with its tab strip, lock and roll-up. A box that changed hands (its host left) keeps its window.
    /// </summary>
    private void SyncBoxes()
    {
        var boxes = FenceTabs.Boxes(_config);
        var boxIds = boxes.Select(box => box.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var (boxId, window) in _windows.ToList())
        {
            if (boxIds.Contains(boxId)) continue;
            _windows.Remove(boxId);
            if (_config.Fences.Any(fence => fence.Id == window.FenceId) && FenceTabs.HostOf(_config, window.FenceId) is { } heir
                && boxIds.Contains(heir.Id) && !_windows.ContainsKey(heir.Id))
            {
                window.BoxId = heir.Id;
                _windows[heir.Id] = window;
                if (_dropRegistrations.Remove(boxId, out var moved)) _dropRegistrations[heir.Id] = moved;
                continue;
            }
            if (_dropRegistrations.Remove(boxId, out var registration)) registration.Dispose();
            window.Close();
        }
        foreach (var box in boxes.Where(box => !_windows.ContainsKey(box.Id))) OpenWindow(box);
        EnsurePortals();
        foreach (var box in boxes)
        {
            var window = _windows[box.Id];
            var active = FenceTabs.ActiveOf(_config, box.Id);
            if (window.FenceId != active.Id)
            {
                window.ShowTab(active);
                if (_portals.TryGetValue(active.Id, out var portal)) portal.Refresh();
            }
            window.SetTabs(FenceTabs.TabsOf(_config, box.Id), active.Id);
            window.SetLocked(box.Locked);
            window.SetRolledUp(box.RolledUp);
        }
        RefreshWindows();
        ApplyLayout();
        ScheduleSave();
    }

    /// <summary>A watcher per Portal fence, shown or not (a hidden Portal tab keeps watching, M9); gone ones end.</summary>
    private void EnsurePortals()
    {
        var portalFences = _config.Fences.Where(fence => fence.Source is { Kind: FenceSourceKind.Portal, Path: not null } or { Kind: FenceSourceKind.Library }).ToList();
        foreach (var goneId in _portals.Keys.Where(fenceId => portalFences.All(fence => fence.Id != fenceId)).ToList())
        {
            _portals[goneId].Dispose(); // the folder itself is never touched
            _portals.Remove(goneId);
        }
        foreach (var fence in portalFences.Where(fence => !_portals.ContainsKey(fence.Id)))
        {
            var fenceId = fence.Id;
            var root = fence.Source.Kind == FenceSourceKind.Library ? AppPaths.LibraryDirectory : fence.Source.Path!;
            if (fence.Source.Kind == FenceSourceKind.Library) TryCreateFolder(root);
            _portals[fenceId] = new PortalState(root, noticeOwner: _messages.Handle, show: items => ShowPortalTab(fenceId, items),
                logFailure: failure => Log.Warning(failure, "cannot watch Portal folder of {FenceId}", fenceId));
            if (_gameMode) _portals[fenceId].SetPaused(true);
        }
        UpdateLibrary(); // M12: the library scans while its fence exists
    }

    /// <summary>A Portal's listing goes to the window showing it; a hidden Portal tab re-lists when shown.</summary>
    private void ShowPortalTab(string fenceId, IReadOnlyList<ItemInfo>? items)
    {
        if (_windows.Values.FirstOrDefault(window => window.FenceId == fenceId) is { } window) ShowPortal(window, items);
    }

    private void RefreshTabs(FenceWindow window) => window.SetTabs(FenceTabs.TabsOf(_config, window.BoxId), window.FenceId);

    /// <summary>A header clicked (or hovered during a drop): that tab shows, now and after a restart.</summary>
    private void SwitchTab(FenceWindow window, string fenceId)
    {
        if (window.FenceId == fenceId || _config.Fences.FirstOrDefault(fence => fence.Id == fenceId) is not { } tab) return;
        _config = FenceTabs.SetActive(_config, fenceId);
        window.ShowTab(tab);
        RefreshTabs(window);
        if (_portals.TryGetValue(fenceId, out var portal)) portal.Refresh();
        RefreshWindows();
        ScheduleSave();
    }

    private void CycleTab(FenceWindow window, int step)
    {
        var tabs = FenceTabs.TabsOf(_config, window.BoxId);
        var index = tabs.ToList().FindIndex(tab => tab.Id == window.FenceId);
        SwitchTab(window, tabs[((index + step) % tabs.Count + tabs.Count) % tabs.Count].Id);
    }

    /// <summary>A tab header released after a drag: on its own strip = reorder; on another title row = merge; elsewhere = detach.</summary>
    private void OnTabDropped(FenceWindow window, string fenceId, int screenX, int screenY)
    {
        var target = _windows.Values.FirstOrDefault(candidate => candidate.TitleRowContains(screenX, screenY));
        var own = FenceWindowChrome.GetPixelRect(window.Handle);
        if (target is null && screenX >= own.X && screenX < own.X + own.Width && screenY >= own.Y && screenY < own.Y + own.Height)
        {
            return; // released over its own box: nothing happens (not a detach on top of itself; final review)
        }
        if (target == window)
        {
            var slot = window.TabIndexAt(screenX);
            var current = FenceTabs.TabsOf(_config, window.BoxId).ToList().FindIndex(tab => tab.Id == fenceId);
            _config = FenceTabs.Reorder(_config, fenceId, slot > current ? slot - 1 : slot);
        }
        else if (target is not null)
        {
            Log.Information("tab {FenceId} moved into the box of {TargetId}", fenceId, target.BoxId);
            // One tab moves, also when it is its box's host (final review C1).
            _config = FenceTabs.Merge(_config, movingFenceId: fenceId, targetFenceId: target.BoxId, insertAt: target.TabIndexAt(screenX), wholeBox: false);
        }
        else
        {
            DetachTab(window, fenceId, dropPoint: (screenX, screenY));
            return;
        }
        SyncBoxes();
    }

    /// <summary>A tab leaves its box: at the drop point (dragged out) or offset 40 DIP down-right of the box (menu), with the box's size.</summary>
    private void DetachTab(FenceWindow window, string fenceId, (int X, int Y)? dropPoint)
    {
        if (_monitors.Count == 0 || _config.LastLayoutFingerprint is not { } fingerprint
            || !_config.Layouts.TryGetValue(fingerprint, out var layout) || !layout.Fences.TryGetValue(window.BoxId, out var boxRect))
        {
            Log.Warning("tab {FenceId} not detached: its box has no place in the current layout", fenceId);
            return;
        }
        var boxMonitor = _monitors.FirstOrDefault(monitor => monitor.DeviceId == boxRect.Monitor) ?? _monitors[0];
        var box = FencePlacement.ToPixels(boxRect, boxMonitor);
        var offset = (int)Math.Round(40 * boxMonitor.Scale);
        var placed = dropPoint is { } point
            ? box with { X = point.X - offset, Y = point.Y - offset / 3 } // the pointer near its title
            : box with { X = box.X + offset, Y = box.Y + offset };
        var monitor = FencePlacement.ContainingMonitor(placed, _monitors);
        Log.Information("tab {FenceId} detached from the box of {BoxId}", fenceId, window.BoxId);
        _config = FenceTabs.Detach(_config, fenceId, fingerprint, FencePlacement.FromPixels(placed, monitor));
        SyncBoxes();
    }

    /// <summary>Asks a Portal to re-list its folder (in the background; ShowPortal follows).</summary>
    private void RefreshPortal(FenceWindow window)
    {
        if (_portals.TryGetValue(window.FenceId, out var portal)) portal.Refresh();
    }

    /// <summary>Shows a Portal's listing (null: the folder cannot be read) in its sort order (M4).</summary>
    private void ShowPortal(FenceWindow window, IReadOnlyList<ItemInfo>? items)
    {
        if (!_portals.TryGetValue(window.FenceId, out var portal)) return;
        if (_config.Fences.FirstOrDefault(candidate => candidate.Id == window.FenceId) is not { } fence) return;
        window.SetPortalLocation(portal.Breadcrumb(fence.Title), portal.CanGoBack);
        window.SetSortChecked(fence.Sort);
        if (fence.Source.Kind == FenceSourceKind.Library)
        {
            window.ShowPortalMessage(null);
            window.SetLibraryArt(LibraryArt());
            window.SetItems(items is null ? [] : LibraryOrder(items)); // A–Z by game, only NeoFences' own shortcuts (M12)
            return;
        }
        window.ShowPortalMessage(items is null ? $"This folder is not available right now:\n{portal.Current}" : null);
        window.SetItems(items is null ? [] : ItemSorting.Order(items, fence.Sort));
    }

    /// <summary>Double-click / Enter: inside a Portal a folder is browsed in place (Ctrl opens it in Explorer, user choice 2026-10-03).</summary>
    private void OpenOrBrowse(FenceWindow window, string itemRef)
    {
        var inExplorer = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control);
        if (_portals.TryGetValue(window.FenceId, out var portal) && !inExplorer && portal.IsListedFolder(itemRef))
        {
            portal.Browse(itemRef); // re-lists in the background
            return;
        }
        OpenItem(itemRef, ownerHandle: window.Handle);
        SetPeek(false); // like Fences: Peek ends once something is opened from it
    }

    private void BrowsePortal(FenceWindow window, bool back)
    {
        if (!back || !_portals.TryGetValue(window.FenceId, out var portal) || !portal.CanGoBack) return;
        portal.Back(); // re-lists in the background
    }

    /// <summary>"Sort by": a Portal keeps the order live; a desktop fence is sorted once (M4).</summary>
    private void SortFence(FenceWindow window, FenceSort sort)
    {
        var fence = _config.Fences.First(candidate => candidate.Id == window.FenceId);
        if (_portals.ContainsKey(fence.Id))
        {
            _config = FenceEdits.SetSort(_config, fence.Id, sort);
            RefreshPortal(window);
        }
        else
        {
            try
            {
                _config = FenceEdits.SetItemOrder(_config, fence.Id, ItemSorting.Order(FolderItems.Describe(fence.Items), sort));
            }
            catch (ArgumentException mismatch)
            {
                Log.Warning(mismatch, "sort of fence {FenceId} refused: the sorted list did not match its items", fence.Id); // never crash (M4 review I3)
                return;
            }
            RefreshWindows();
        }
        ScheduleSave();
    }

    private void ApplyStartup() =>
        StartupRegistration.Apply(_config.Settings.StartWithWindows, Environment.ProcessPath ?? "", log: message => Log.Information("{Message}", message));

    private void SetStartWithWindows(bool startWithWindows)
    {
        _config = _config with { Settings = _config.Settings with { StartWithWindows = startWithWindows } };
        ApplyStartup();
        foreach (var window in _windows.Values) window.SetStartupChecked(startWithWindows);
        SaveNow();
        RefreshSettings();
    }

    private void CreateFence()
    {
        (_config, _) = FenceMembership.CreateFence(_config, "New fence");
        SyncBoxes();
    }

    private void SetTakeover(bool active)
    {
        SetQuickHidden(false); // an explicit icons choice ends quick-hide first, so the two never disagree
        _takeoverActive = active;
        // Any explicit choice (banner or menu) answers the first-run question.
        _config = _config with { Settings = _config.Settings with { Takeover = active, TakeoverPromptAnswered = true } };
        SetIconsHidden(Current.IconsHidden);
        foreach (var window in _windows.Values) window.SetTakeoverChecked(active);
        RefreshSettings();
        RefreshWindows();
        SaveNow(); // not debounced: the saved setting must match the takeover-active marker if we are killed next
    }

    /// <returns>True when Windows confirmed the new state.</returns>
    private bool SetIconsHidden(bool hidden)
    {
        // The watchdog must know whenever icons may be hidden: mark before hiding, unmark only after a confirmed show.
        if (hidden) TryMarker(() => _watchdog.SetTakeoverActive(true), what: "takeover-active marker");
        var applied = DesktopIcons.TrySetHidden(hidden);
        if (applied && !hidden) TryMarker(() => _watchdog.SetTakeoverActive(false), what: "takeover-active marker");
        if (applied) Log.Information("desktop icons hidden: {Hidden}", hidden);
        else Log.Warning("could not set desktop icons hidden: {Hidden}", hidden);
        return applied;
    }

    /// <summary>Icons as they should be: hidden while Takeover is on, shown (and unmarked) otherwise.</summary>
    private bool EnsureIconState() =>
        Current.IconsHidden ? SetIconsHidden(true)
        : !_watchdog.IsTakeoverActiveMarked || SetIconsHidden(false);

    private static void TryMarker(Action writeMarker, string what)
    {
        try
        {
            writeMarker();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Error(failure, "could not update the {What}", what);
        }
    }

    private void OnExplorerRestarted()
    {
        Log.Information("Explorer restarted; re-attaching fences");
        SetPeek(false); // re-attaching sends fences to the bottom; Peek (and its global Esc) must end with it (M5 review M2)
        ShowTrayIcon(); // Explorer forgot every tray icon
        ReinstallMouseHook(); // a hook Windows dropped silently comes back here at the latest (M5 review)
        StartSpecialIconNotifications();
        ScheduleSpecialIconRefresh(); // the Recycle Bin may have changed meanwhile
        var attempts = 0;
        var retryTimer = new DispatcherTimer { Interval = ReattachInterval };
        retryTimer.Tick += (_, _) =>
        {
            attempts++;
            var unattachedCount = _windows.Values.Count(window => !DesktopHost.AttachToDesktop(window.Handle));
            foreach (var window in _windows.Values) FenceWindowChrome.SendToBack(window.Handle);
            var iconsOk = EnsureIconState();
            if ((unattachedCount == 0 && iconsOk) || attempts >= ReattachAttempts)
            {
                retryTimer.Stop();
                Log.Information("re-attach after Explorer restart: {Attempts} attempt(s), unattached {UnattachedCount}, icons ok {IconsOk}",
                    attempts, unattachedCount, iconsOk);
            }
        };
        retryTimer.Start();
    }

    /// <summary>The WH_MOUSE_LL desktop gestures and the Peek hotkey (M5). Either failing only turns that feature off.</summary>
    private void StartGestures()
    {
        UpdateMouseHook();
        UpdatePeekHotkey();
    }

    /// <summary>The Peek hotkey is registered exactly while wanted: released to Windows and the game while paused or gaming.</summary>
    private void UpdatePeekHotkey()
    {
        // Released while Settings records a new one, so pressing the current combination is recorded, not Peek (M6b review).
        if ((Current.PeekHotkeyWanted && !_recordingHotkey) == _peekHotkey is not null) return;
        if (_peekHotkey is not null)
        {
            _peekHotkey.Dispose();
            _peekHotkey = null;
            return;
        }
        _peekHotkey = TryRegisterPeekHotkey(_config.Settings.PeekHotkey, out var problem);
        _peekHotkeyProblem = _peekHotkey is null ? problem : null;
        if (_peekHotkey is null) Log.Warning("Peek is off: {Problem}", problem);
    }

    private GlobalHotkey? TryRegisterPeekHotkey(string text, out string problem)
    {
        if (!Hotkey.TryParse(text, out var hotkey) || !Enum.TryParse<System.Windows.Input.Key>(hotkey.Key, ignoreCase: true, out var key))
        {
            problem = $"\"{text}\" is not a hotkey: it needs Ctrl, Alt, Shift or Win plus one key.";
            return null;
        }
        var virtualKey = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            problem = $"{hotkey.DisplayText} has no key Windows can watch for. Pick another key."; // would say "Saved" and never fire (M6b review M4)
            return null;
        }
        var registration = new GlobalHotkey(_messages.Handle, PeekHotkeyId);
        if (registration.TryRegister(hotkey, (uint)virtualKey))
        {
            problem = "";
            Log.Information("Peek hotkey {Hotkey} registered", hotkey);
            return registration;
        }
        registration.Dispose();
        problem = $"{hotkey.DisplayText} is already taken by Windows or another app. Pick another combination.";
        return null;
    }

    /// <summary>Settings: a new Peek hotkey. It is kept only if Windows accepts it; otherwise the old one stays.</summary>
    private (bool Saved, string Message) SetPeekHotkey(string text)
    {
        if (!Hotkey.TryParse(text, out var parsed)) return (false, $"\"{text}\" is not a hotkey: it needs Ctrl, Alt, Shift or Win plus one key.");
        // A global hotkey swallows its keys in every app (M6b review I1): only safe combinations are recorded.
        if (!parsed.IsSafeToRecord) return (false, $"{parsed.DisplayText} would stop working everywhere else (typing, Tab, closing windows). Use Alt or Win with a key, or an F-key.");
        var normalized = parsed.ToString();
        // The same combination again is a retry when it is not registered (Settings showed it as not active, M6b review).
        if (normalized == _config.Settings.PeekHotkey && _peekHotkeyProblem is null) return (true, $"Peek: {parsed.DisplayText}");
        _peekHotkey?.Dispose();
        _peekHotkey = null;
        var registration = TryRegisterPeekHotkey(normalized, out var problem);
        if (registration is null)
        {
            UpdatePeekHotkey(); // the old one again
            return (false, problem);
        }
        registration.Dispose(); // proven free; UpdatePeekHotkey registers it whenever it is wanted
        _peekHotkeyProblem = null;
        _config = _config with { Settings = _config.Settings with { PeekHotkey = normalized } };
        UpdatePeekHotkey();
        SaveNow();
        return (true, $"Saved. Peek: {parsed.DisplayText}");
    }

    /// <summary>Tray or fence menu → Settings… (M6b). One window; a second request brings it to the front.</summary>
    private void OpenSettings()
    {
        if (_settingsWindow is { } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        var window = new SettingsWindow();
        window.StartWithWindowsChanged += SetStartWithWindows;
        window.TakeoverChanged += SetTakeover;
        window.PeekHotkeyChosen += text =>
        {
            var (saved, message) = SetPeekHotkey(text);
            RefreshSettings();
            window.ShowHotkeyResult(saved, message);
        };
        window.RollupExpandChanged += SetRollupExpand;
        window.TakeSnapshotRequested += () => TakeSnapshot();
        window.RestoreSnapshotRequested += RestoreSnapshot;
        window.RenameSnapshotRequested += (path, name) =>
        {
            if (!_snapshots.Rename(path, name)) Log.Warning(_snapshots.LastFailure, "snapshot {Path} could not be renamed", path);
            RefreshSettings();
        };
        window.DeleteSnapshotRequested += DeleteSnapshot;
        window.RulesChanged += rules =>
        {
            _config = _config with { Rules = rules };
            Log.Information("rules changed: {Count} rule(s)", rules.Count);
            SaveNow();
            RefreshSettings();
        };
        window.ApplyRulesRequested += ApplyRulesNow;
        WireLibrarySettings(window);
        window.OpenSnapshotsRequested += () =>
        {
            try
            {
                Directory.CreateDirectory(_snapshots.Directory);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                Log.Warning(failure, "snapshots folder {Folder} could not be created", _snapshots.Directory); // hard rule 7 (final review I3)
                return;
            }
            OpenItem(_snapshots.Directory, ownerHandle: 0);
        };
        window.HotkeyRecording += recording =>
        {
            _recordingHotkey = recording;
            UpdatePeekHotkey();
            // Leaving the box re-registers: Settings must show if that failed (final review I5).
            if (!recording) RefreshSettings();
        };
        window.DefaultLabelsChanged += labels =>
        {
            _config = _config with { Settings = _config.Settings with { DefaultLabels = labels } };
            SaveNow();
        };
        window.LabelsAppliedToAll += labels =>
        {
            _config = FenceEdits.SetLabelsEverywhere(_config, labels);
            foreach (var fenceWindow in _windows.Values) fenceWindow.SetLabelMode(labels);
            Log.Information("labels for every fence: {Labels}", labels);
            SaveNow();
            RefreshSettings();
        };
        window.ShortcutArrowsChanged += show =>
        {
            _config = _config with { Settings = _config.Settings with { ShowShortcutArrows = show } };
            foreach (var fenceWindow in _windows.Values) fenceWindow.SetShortcutArrows(show);
            SaveNow();
        };
        window.GameModeChanged += SetGameModeEnabled;
        window.OpenLogsRequested += () => OpenItem(AppPaths.LogsDirectory, ownerHandle: 0);
        window.OpenDataRequested += () => OpenItem(AppPaths.DataDirectory, ownerHandle: 0);
        window.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _recordingHotkey = false;
            UpdatePeekHotkey();
        };
        _settingsWindow = window;
        RefreshSettings();
        window.Show();
        window.Activate();
    }

    private void RefreshSettings() => _settingsWindow?.Show(new SettingsView(
        StartWithWindows: _config.Settings.StartWithWindows,
        Takeover: _takeoverActive,
        PeekHotkey: PeekHotkeyDisplay,
        PeekHotkeyActive: _peekHotkeyProblem is null,
        RollupExpand: _config.Settings.RollupExpand,
        GameModeEnabled: _config.Settings.GameMode,
        GameModeActive: _gameMode,
        Version: typeof(FenceHost).Assembly.GetName().Version?.ToString(3) ?? "",
        DataFolder: AppPaths.DataDirectory,
        DefaultLabels: _config.Settings.DefaultLabels,
        ShowShortcutArrows: _config.Settings.ShowShortcutArrows,
        Snapshots: ListSnapshots(),
        Rules: _config.Rules,
        RuleLines: [.. _config.Rules.Select(rule => Rules.Describe(rule, _config))],
        RuleFences: [.. _config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop).Select(fence => new RuleFence(fence.Id, fence.Title))],
        Library: LibrarySettingsView()));

    /// <summary>Fence menu → "Rules for this fence…" (M11): Settings at Rules, a new rule for that fence.</summary>
    private void OpenRulesFor(string fenceId)
    {
        OpenSettings();
        _settingsWindow?.BeginNewRule(fenceId);
    }

    /// <summary>The Peek hotkey as a person reads it ("Ctrl+Shift+=", not "Ctrl+Shift+OemPlus").</summary>
    private string PeekHotkeyDisplay =>
        Hotkey.TryParse(_config.Settings.PeekHotkey, out var hotkey) ? hotkey.DisplayText : _config.Settings.PeekHotkey;

    /// <summary>Saves the arrangement now, named by date and time (M10); renamed in Settings if wanted.</summary>
    private void TakeSnapshot()
    {
        var now = DateTimeOffset.Now;
        var snapshot = Snapshots.Take(_config, name: $"Snapshot {now:d MMM HH:mm}", now: now);
        if (_snapshots.Save(snapshot) is { } path)
        {
            Log.Information("snapshot saved: {Path}", path);
            _trayIcon?.ShowBalloon("Snapshot saved", snapshot.Name);
        }
        else
        {
            Log.Warning(_snapshots.LastFailure, "snapshot could not be saved");
            _trayIcon?.ShowBalloon("Snapshot not saved", "NeoFences could not write the snapshot file (see the log).");
        }
        RefreshSettings();
    }

    /// <summary>
    /// Puts a snapshot's arrangement back (M10, spec §3): only with a complete desktop listing, and only after "Before
    /// restore" was written, so the restore itself can be undone. One config change, saved at once.
    /// </summary>
    private void RestoreSnapshot(string path)
    {
        if (_snapshots.Load(path) is not { } snapshot)
        {
            Log.Warning(_snapshots.LastFailure, "snapshot {Path} could not be read", path);
            _trayIcon?.ShowBalloon("Snapshot not restored", "The snapshot file could not be read.");
            return;
        }
        var listing = DesktopItems.Enumerate();
        if (listing.UnavailableFolders.Count > 0)
        {
            Log.Warning("snapshot not restored: desktop folders not readable {Folders}", listing.UnavailableFolders);
            _trayIcon?.ShowBalloon("Snapshot not restored", "A Desktop folder cannot be read right now. Try again in a moment.");
            return;
        }
        var now = DateTimeOffset.Now;
        if (_snapshots.Save(Snapshots.Take(_config, name: $"Before restore ({now:d MMM HH:mm})", now: now), SnapshotStore.BeforeRestoreFileName) is null)
        {
            Log.Warning(_snapshots.LastFailure, "snapshot not restored: 'Before restore' could not be saved");
            _trayIcon?.ShowBalloon("Snapshot not restored", "NeoFences could not save 'Before restore' first (see the log).");
            return;
        }
        Log.Information("restoring snapshot {Name} from {Path}", snapshot.Name, path);
        _config = Snapshots.Restore(_config, snapshot, listing.ItemRefs);
        SaveNow();
        SyncBoxes();
        // Windows that kept their fence still show its old title, icon size and labels (final review I1).
        foreach (var window in _windows.Values)
        {
            if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is not { } shown) continue;
            window.Refresh(shown);
            RefreshPortal(window); // the Portal breadcrumb replaces the plain title again
        }
        RefreshSettings();
    }

    /// <summary>The snapshot list; a damaged file or an unreadable folder is logged once, not on every tray open (final review I2).</summary>
    private IReadOnlyList<SnapshotEntry> ListSnapshots()
    {
        var snapshots = _snapshots.List();
        foreach (var (path, failure) in _snapshots.Problems.Where(problem => _loggedSnapshotProblems.Add(problem.Path)))
            Log.Warning(failure, "snapshot {Path} skipped: it cannot be read", path);
        return snapshots;
    }

    /// <summary>A snapshot file goes to the Recycle Bin, never deleted for good (hard rule 1).</summary>
    private void DeleteSnapshot(string path)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        _shellWorker.Run(() =>
        {
            var (started, refused, missing) = ShellFileOps.TryRecycle(0, [path]);
            if (!started || refused.Count > 0) Log.Warning("snapshot {Path} could not be moved to the Recycle Bin", path);
            if (missing.Count > 0) Log.Information("snapshot {Path} was already gone", path);
            dispatcher.BeginInvoke(RefreshSettings);
        });
    }

    /// <summary>Fence menu → Labels (M8b).</summary>
    private void SetFenceLabels(FenceWindow window, LabelMode labels)
    {
        _config = FenceEdits.SetLabels(_config, window.FenceId, labels);
        window.SetLabelMode(labels);
        ScheduleSave();
    }

    private void SetRollupExpand(RollupExpand mode)
    {
        _config = _config with { Settings = _config.Settings with { RollupExpand = mode } };
        foreach (var window in _windows.Values) window.SetRollupExpand(mode);
        Log.Information("roll-up expands on: {Mode}", mode);
        SaveNow();
    }

    private void SetGameModeEnabled(bool enabled)
    {
        _config = _config with { Settings = _config.Settings with { GameMode = enabled } };
        Log.Information("game mode enabled: {Enabled}", enabled);
        SaveNow();
        CheckGameMode(); // turning it off in a game ends idle at once
        RefreshSettings();
    }

    /// <summary>The hook exists exactly while it is wanted: not while paused or gaming (hard rule 3, spec §4.7).</summary>
    private void UpdateMouseHook()
    {
        if (Current.MouseHookWanted == _mouseHook is not null) return;
        if (_mouseHook is not null)
        {
            EndDrawOverlay();
            _mouseHook.Dispose();
            _mouseHook = null;
            return;
        }
        var dispatcher = Dispatcher.CurrentDispatcher;
        _mouseHook = new DesktopMouseHook(
            onGesture: (gesture, screenX, screenY) => dispatcher.BeginInvoke(() => OnDesktopGesture(gesture, screenX, screenY)),
            onPeekClickOutside: () => dispatcher.BeginInvoke(() => SetPeek(false)),
            log: message => Log.Information("{Message}", message));
    }

    /// <summary>Windows drops a low-level hook silently (LowLevelHooksTimeout): a fresh one after Explorer restarts and on unlock.</summary>
    private void ReinstallMouseHook()
    {
        if (_mouseHook is null) return; // not wanted now; it comes back when it is
        EndDrawOverlay();
        _mouseHook.Dispose();
        _mouseHook = null;
        UpdateMouseHook();
    }

    private void OnSessionUnlocked()
    {
        Log.Information("session unlocked; re-installing the mouse hook");
        ReinstallMouseHook();
        CheckGameMode();
    }

    /// <summary>Game mode (spec §4.7, ADR-021): checked on every foreground change, and again shortly after (the signal lags).</summary>
    private void StartGameMode()
    {
        // Backstop for a game that goes full screen after the last re-check (M6a review I1).
        var poll = new DispatcherTimer { Interval = GameModePolicy.PollInterval };
        poll.Tick += (_, _) => CheckGameMode();
        poll.Start();
        _foregroundWatcher = new ForegroundWatcher(onForegroundChanged: OnForegroundChanged,
            logFailure: failure => Log.Error(failure, "game mode check failed"));
        if (!_foregroundWatcher.IsWatching) Log.Warning("game mode: foreground changes cannot be watched; game mode is off");
        CheckGameMode();
    }

    private void OnForegroundChanged()
    {
        CheckGameMode();
        foreach (var delay in GameModePolicy.RecheckDelays)
        {
            // ponytail: one short-lived timer per foreground change and delay; a coalescing timer if switching storms ever show up in a profile.
            var recheck = new DispatcherTimer { Interval = delay };
            recheck.Tick += (_, _) =>
            {
                recheck.Stop();
                CheckGameMode();
            };
            recheck.Start();
        }
    }

    private void CheckGameMode()
    {
        var gameMode = GameModePolicy.IsGameActive(enabled: _config.Settings.GameMode, foreground: GameDetection.TakeSnapshot());
        if (gameMode == _gameMode) return;
        _gameMode = gameMode;
        Log.Information("game mode: {GameMode}", gameMode);
        if (gameMode) SetPeek(false);
        UpdateMouseHook();
        foreach (var portal in _portals.Values) portal.SetPaused(gameMode);
        if (!gameMode) ApplyDeferredShellWork();
        UpdatePeekHotkey();
        _trayIcon?.SetTooltip(TrayTooltip());
        RefreshSettings();
    }

    /// <summary>The game was left: Desktop changes made meanwhile apply in order (or one reconcile if events were lost).</summary>
    private void ApplyDeferredShellWork()
    {
        if (_libraryDeferred)
        {
            _libraryDeferred = false;
            ScanLibrary(); // a game installed while playing shows up now (M12)
        }
        if (_specialIconsDeferred)
        {
            _specialIconsDeferred = false;
            ScheduleSpecialIconRefresh();
        }
        if (_reconcileDeferred)
        {
            _reconcileDeferred = false;
            _deferredDesktopChanges.Clear();
            ReconcileDesktop();
            return;
        }
        if (_deferredDesktopChanges.Count == 0) return;
        Log.Information("applying {Count} desktop change(s) from game mode", _deferredDesktopChanges.Count);
        var arrivals = new List<string>();
        foreach (var change in _deferredDesktopChanges)
        {
            if (ArrivalOf(change) is { } arrival) arrivals.Add(arrival);
            (_config, _rememberedPlacements) = FenceMembership.Apply(_config, change, _rememberedPlacements, Now);
        }
        _deferredDesktopChanges.Clear();
        RefreshWindows();
        ScheduleSave();
        FileNewItems(arrivals);
    }

    /// <summary>Pause (tray): the desktop goes back to Windows — fences hidden, icons shown, the hook gone — until resumed.</summary>
    private void SetPaused(bool paused)
    {
        if (paused == _paused) return;
        if (paused)
        {
            SetPeek(false);
            EndDrawOverlay();
        }
        _paused = paused;
        if (paused)
        {
            _quickHidden = false; // resuming shows everything
            _iconsHiddenByUser = false;
        }
        foreach (var window in _windows.Values)
        {
            if (!Current.FencesVisible) window.HideNow();
            else if (!window.IsVisible)
            {
                window.ShowNow();
                FenceWindowChrome.SendToBack(window.Handle);
            }
        }
        EnsureIconState(); // also shows icons left hidden by an earlier failed show: Pause "restores icons" (spec §6, M6a review M1)
        UpdateMouseHook();
        UpdatePeekHotkey();
        RefreshSettings(); // a hotkey that cannot be registered on resume shows in an open Settings (final review I5)
        _trayIcon?.SetTooltip(TrayTooltip());
        Log.Information("paused: {Paused}", paused);
    }

    private string TrayTooltip() =>
        _paused ? "NeoFences — paused" : _gameMode ? "NeoFences — idle while a game runs" : "NeoFences";

    /// <summary>Tray menu (spec §6; user choice 2026-10-03: left-click opens it too).</summary>
    private void ShowTrayMenu(int screenX, int screenY)
    {
        var snapshots = ListSnapshots();
        var restorable = snapshots.Where(entry => !entry.IsBeforeRestore).Take(TrayRestoreCount).ToList();
        var beforeRestore = snapshots.FirstOrDefault(entry => entry.IsBeforeRestore);
        List<TrayMenuItem> restoreItems = [.. restorable.Select((entry, index) => new TrayMenuItem(TrayRestoreFirst + index, entry.Name.Replace("&", "&&"))) /* & is a menu accelerator */];
        if (beforeRestore is not null) restoreItems.AddRange([TrayMenuItem.Separator, new TrayMenuItem(TrayRestoreBefore, "Undo the last restore")]);
        restoreItems.AddRange([TrayMenuItem.Separator, new TrayMenuItem(TraySettings, "More in Settings…")]);
        var chosen = TrayMenu.Show(_messages.Handle,
        [
            new TrayMenuItem(TrayNewFence, "New fence", Enabled: !_paused),
            new TrayMenuItem(TrayNewLibrary, "New Game Library fence", Enabled: !_paused),
            new TrayMenuItem(TrayQuickHide, "Quick-hide", Checked: _quickHidden, Enabled: !_paused),
            new TrayMenuItem(TrayPeek, $"Peek\t{PeekHotkeyDisplay}", Checked: _peeking, Enabled: !_paused),
            TrayMenuItem.Separator,
            new TrayMenuItem(TrayTakeSnapshot, "Take snapshot"),
            new TrayMenuItem(TrayRestoreMenu, "Restore snapshot", Enabled: snapshots.Count > 0) { Children = restoreItems },
            TrayMenuItem.Separator,
            new TrayMenuItem(TraySettings, "Settings…"),
            new TrayMenuItem(TrayPause, "Pause NeoFences", Checked: _paused),
            TrayMenuItem.Separator,
            new TrayMenuItem(TrayExit, "Exit NeoFences"),
        ], screenX, screenY);
        switch (chosen)
        {
            case TrayNewFence:
                SetQuickHidden(false); // a new fence must be visible (M6a review I3)
                CreateFence();
                break;
            case TrayNewLibrary: CreateLibraryFence(); break;
            case TrayQuickHide: SetQuickHidden(!_quickHidden); break;
            case TrayPeek: SetPeek(!_peeking); break;
            case TrayPause: SetPaused(!_paused); break;
            case TraySettings: OpenSettings(); break;
            case TrayExit: ExitRequested?.Invoke(); break;
            case TrayTakeSnapshot: TakeSnapshot(); break;
            case TrayRestoreBefore when beforeRestore is not null: RestoreSnapshot(beforeRestore.Path); break;
            case >= TrayRestoreFirst and < TrayRestoreFirst + TrayRestoreCount when chosen - TrayRestoreFirst < restorable.Count:
                RestoreSnapshot(restorable[chosen - TrayRestoreFirst].Path);
                break;
        }
    }

    private void OnDesktopGesture(DesktopGesture gesture, int screenX, int screenY)
    {
        switch (gesture)
        {
            // A double-click on a visible native icon opens it; only empty desktop toggles quick-hide.
            case DesktopGesture.DoubleClick when Current.IconsHidden
                || !DesktopWindows.IsOverDesktopIcon(screenX, screenY, log: message => Log.Warning("{Message}", message)):
                SetQuickHidden(!_quickHidden);
                break;
            case DesktopGesture.RightDragStarted:
                BeginDrawFence(screenX, screenY);
                break;
            case DesktopGesture.RightDragCompleted:
                EndDrawFence(screenX, screenY);
                break;
            case DesktopGesture.RightDragCancelled:
                EndDrawOverlay(); // the drag's right-up was never seen (M5 review I2)
                break;
        }
    }

    /// <summary>Quick-hide (M5, user choice): fences and the native desktop icons go away together and come back together.</summary>
    private void SetQuickHidden(bool hidden)
    {
        if (hidden == _quickHidden) return;
        if (hidden) SetPeek(false);
        // Icons the user had hidden through Explorer stay theirs: quick-hide neither hides nor later shows them (M8a).
        // Hidden now without the takeover-active marker: the user hid them in Explorer, so they stay the user's. With the marker
        // set, NeoFences hid them (an earlier show failed) and the end of this quick-hide retries the show (M8a review I1).
        if (hidden && !_takeoverActive) _iconsHiddenByUser = DesktopIcons.TryIsHidden() == true && !_watchdog.IsTakeoverActiveMarked;
        var iconsWereHidden = Current.IconsHidden;
        _quickHidden = hidden;
        foreach (var window in _windows.Values)
        {
            if (hidden) window.HideFaded(); // 150 ms fade (spec §6)
            else
            {
                window.ShowFaded();
                FenceWindowChrome.SendToBack(window.Handle);
            }
        }
        if (Current.IconsHidden != iconsWereHidden) SetIconsHidden(Current.IconsHidden); // RunState decides, user-hidden icons included
        if (!hidden) _iconsHiddenByUser = false;
        Log.Information("quick-hide: {Hidden}", hidden);
    }

    private void BeginDrawFence(int startX, int startY)
    {
        if (_mouseHook is not { } mouseHook) return;
        EndDrawOverlay();
        _drawStart = (startX, startY);
        var overlay = new DrawFenceOverlay(_lightTheme);
        overlay.Track(DrawFenceOverlay.Between(_drawStart, mouseHook.DragPoint));
        overlay.Show();
        _drawOverlay = overlay;
        _drawTimer = new DispatcherTimer { Interval = DrawFrame };
        // A lost right-up (Win+L, UAC, an elevated window) is ended by the next click (RightDragCancelled). The button
        // state cannot tell: the swallowed right-press never reaches Windows' key state (M5 review I2, smoke finding).
        _drawTimer.Tick += (_, _) => overlay.Track(DrawFenceOverlay.Between(_drawStart, mouseHook.DragPoint));
        _drawTimer.Start();
    }

    /// <summary>The right button came up: a new fence where the rectangle was, its title ready to type.</summary>
    private void EndDrawFence(int endX, int endY)
    {
        if (!EndDrawOverlay()) return; // the start was never seen
        if (_monitors.Count == 0 || _config.LastLayoutFingerprint is not { } fingerprint) return;
        SetQuickHidden(false);
        var pixels = DrawFenceOverlay.Between(_drawStart, (endX, endY));
        var monitor = FencePlacement.ContainingMonitor(pixels, _monitors);
        (_config, var fence) = FenceMembership.CreateFence(_config, "New fence");
        // Too small a drag still makes a usable fence: the layout clamps it to the minimum size.
        _config = LayoutEngine.WithFenceRect(_config, fingerprint: fingerprint, fenceId: fence.Id, rect: FencePlacement.FromPixels(pixels, monitor));
        Log.Information("fence drawn on the desktop at {Pixels}", pixels);
        OpenWindow(fence);
        RefreshWindows();
        ApplyLayout();
        ScheduleSave();
        if (_windows.TryGetValue(fence.Id, out var window)) window.BeginRename();
    }

    /// <returns>True when an overlay was showing.</returns>
    private bool EndDrawOverlay()
    {
        _drawTimer?.Stop();
        _drawTimer = null;
        if (_drawOverlay is null) return false;
        _drawOverlay.Close();
        _drawOverlay = null;
        return true;
    }

    private void OnHotkey(int hotkeyId)
    {
        CheckGameMode(); // fresh: a game may have gone full screen since the last check (M6a review I1)
        // Paused: the desktop belongs to Windows. Gaming: fences must not rise over the game, and the click-outside hook is off.
        if (_paused || _gameMode) return;
        if (hotkeyId == PeekHotkeyId) SetPeek(!_peeking);
        else if (hotkeyId == PeekEscapeHotkeyId) SetPeek(false);
    }

    /// <summary>Peek (M5): every fence above all windows until the hotkey again, Esc, a click outside, or an item opens.</summary>
    private void SetPeek(bool peeking)
    {
        if (peeking == _peeking) return;
        if (peeking) SetQuickHidden(false);
        _peeking = peeking;
        if (_mouseHook is not null) _mouseHook.PeekActive = peeking;
        // Every fence first: raising one restacks its siblings (all owned by Progman), and a sibling still keeping
        // itself at the bottom would drop back (M5 smoke: only one fence rose).
        foreach (var window in _windows.Values) window.Peeking = peeking;
        foreach (var window in _windows.Values) FenceWindowChrome.SetTopmost(window.Handle, peeking);
        _peekEscapeHotkey?.Dispose();
        _peekEscapeHotkey = null;
        if (peeking)
        {
            _peekEscapeHotkey = new GlobalHotkey(_messages.Handle, PeekEscapeHotkeyId);
            if (!_peekEscapeHotkey.TryRegister(new Hotkey(Ctrl: false, Alt: false, Shift: false, Win: false, Key: "Escape"), virtualKey: 0x1B))
                Log.Warning("Esc is taken by another app; Peek ends with its hotkey or a click outside");
        }
        Log.Information("peek: {Peeking}", peeking);
    }

    /// <summary>Title double-click (M5): rolled up to its title bar, or back. Stored, so it survives a restart.</summary>
    private void ToggleRollUp(FenceWindow window)
    {
        var rolledUp = !_config.Fences.First(fence => fence.Id == window.BoxId).RolledUp; // the box's (M9)
        _config = FenceEdits.SetRolledUp(_config, window.BoxId, rolledUp);
        window.SetRolledUp(rolledUp);
        ScheduleSave();
    }

    /// <summary>Adds the tray icon, retrying while Explorer is still busy (sign-in autostart, Explorer restart).</summary>
    private void ShowTrayIcon()
    {
        if (_trayIcon is not { } trayIcon || trayIcon.Show()) return;
        var attempts = 0;
        var retry = new DispatcherTimer { Interval = TrayRetryInterval };
        retry.Tick += (_, _) =>
        {
            if (trayIcon.Show() || ++attempts >= TrayRetryAttempts)
            {
                retry.Stop();
                Log.Information("tray icon retry finished after {Attempts} attempt(s)", attempts + 1);
            }
        };
        retry.Start();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            if (!_store.Save(_config)) Log.Warning("config not saved: config.json is read-only this session");
            else if (_store.LastBackupFailure is { } backupFailure) Log.Warning(backupFailure, "config saved, but the daily backups could not be written or pruned");
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Error(failure, "config save failed");
        }
    }

    private static void TryCreateFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Warning(failure, "cannot create {Folder}", folder); // the fence then shows "not available" (hard rule 7)
        }
    }
}
```

- [ ] **Step 2: Build and test**

Run: `dotnet build NeoFences.slnx`, then `dotnet test NeoFences.slnx`
Expected: 0 warnings, 0 errors; `Passed: 373`.

- [ ] **Step 3: Live checks (only with the user's go; print TEST RUNNING / TEST COMPLETE; restore the installed copy)**

Run `m13a-smoke.ps1 -Exe <branch exe>` with Windows PowerShell (session scratchpad; it dot-sources `m8b-helpers.ps1`). No mouse or keyboard input.
Expected: schema 2 after the first start; after `--exit` 0 copies, with the waiting copy logging "already running"; a later start runs. (The script's "a later start runs" line counts the watchdog too: read the log for the third start.)

- [ ] **Step 4: Commit**

```powershell
git add src/NeoFences.Shell src/NeoFences.App
git commit -m "fix: fixed one bad epic or xbox entry hiding a source, sticks vetoed by portal refreshes, library work after exit and a waiting copy ignoring --exit"
```

---

### Task 3: Verification and docs

- [ ] **Step 1: Append section Z to `docs/TEST-CHECKLIST.md`**

```markdown

## Z — v1.6 carry-overs (M13, ADR-033)
| ID | Steps | Expected |
|---|---|---|
| Z1 | Start v1.6 once, then look at `config.json` | `"schemaVersion": 2`; tabs, rules and library settings unchanged |
| Z2 | Start an older NeoFences (≤ 1.5) on that config | it shows a fresh, read-only state and never saves over the file |
| Z3 | Put a snapshot with `"schemaVersion": 3` (or a 20 MB file) in `snapshots\` | not listed, logged once; the others work |
| Z4 | Start NeoFences twice quickly, then run `NeoFences.exe --exit` | both copies end; a later start runs normally |
| Z5 | A Portal on a USB stick whose folder was deleted (the stick still in): Safely remove | Windows lets the stick go |
| Z6 | Safely remove a stick with a Portal while files on it are changing | Windows lets it go (no veto from a refresh queued just before) |
| Z7 | Tray → New Game Library fence while quick-hide is on | quick-hide ends; the library fence shows |
| Z8 | Two rules with the same id pasted in `config.json`; start; delete one in Settings | only that one goes |
```

- [ ] **Step 2: Append ADR-033 to `docs/DECISIONS.md`**

```markdown

## ADR-033 — Config schema version 2; data from a newer NeoFences is never rewritten (M13a, v1.6.0)
**Date:** 2026-10-04 · **Status:** Accepted

**Context.** Tabs (v1.2), rules (v1.4) and the Game Library (v1.5) added fields to `config.json` and snapshot files
while the schema version stayed 1. An older NeoFences reading such a file would drop those fields on its next save.
The M9–M12 reviews also left smaller data-safety gaps: snapshots from a newer version were renamed or restored with
fields missing, copied rule blocks shared one id, and the library index could disagree with the folder after a
failed write.

**Decision.**
- `NeoFencesConfig.CurrentSchemaVersion = 2`; the normalizer stamps every loaded config with it, so the first save of
  v1.6 writes 2. Versions ≤ 1.5 already treat a newer schema as read-only (ADR-006 store) and never save over it.
- Snapshot files carry the same version; a snapshot from a newer NeoFences, or one over 16 MB, is listed as a problem
  and never loaded, restored or renamed. A failed snapshot write leaves no temp file.
- Duplicate rule ids get new ids on load (the first keeps its id).
- The library index is settled against what really happened (`LibraryFiles.Settle`): a failed rewrite keeps the old
  entry, a failed delete stays listed, a failed new file is left out.

**Consequences.** Downgrading after v1.6 shows a fresh read-only state until the user upgrades again or restores a
pre-1.6 backup from `backups\`; nothing is lost.
```

- [ ] **Step 3: Write `docs/research/m13a-data-safety.md`**

```markdown
# M13a — v1.6 carry-overs, data safety: results

**Date:** 2026-10-04 · **Machine:** Windows 11 Pro 25H2 (26200), NeoFences 1.5.0 installed.
**Build:** M13a prototype (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section Z · **Decision:** ADR-033.

## Live check on the prototype (no mouse or keyboard; config and installed copy restored)

| Check | Result |
|---|---|
| Z1 first start of the prototype | **Pass**: `schemaVersion` 1 → 2 in the saved config. |
| Z4 a second copy waiting for the first, then `--exit` | **Pass**: the waiting copy logged "already running … exits" and gave up; the running one exited; 0 copies left. |
| A plain start after that `--exit` | **Pass**: started and ran normally (the stale signal is reset by the owner). |
| Log | 0 warnings or errors. |

Not run live: Z5/Z6 need the LOCO_DUCK stick ejected (the user would have to replug it); they are covered by the
final review and left as hand checks.

## Core (test-first)

9 new tests (373 in all): schema 2 and the upgrade of a version-1 file, a newer file never overwritten, duplicate rule
ids, snapshots from a newer version refused, a huge stray file skipped, no temp file after a failed save, a restore
with a Portal, case-only renames and newcomers in the Desktop's order, a snapshot with null fields, the library index
after failed writes and deletes.
```

- [ ] **Step 4: Update the other docs**
  - `ROADMAP.md`:
    - the claim line becomes `- [x] M13a — done <date> (ADR-033, research/m13a-data-safety.md)`, and add `- [ ] v1.6.0 release`;
    - in the carry-over lists, tick the items this batch closed: the schemaVersion bump for tabs; "Refuse a snapshot with a newer schemaVersion …"; "Unfenced newcomers in Restore …"; the M10 test gaps; "Re-id duplicate rule ids on load"; "A failed shortcut write or delete …"; "Tray New Game Library fence while quick-hidden …"; the scan after exit; "--exit while a second instance waits …"; "A release can be undone …"; "A Portal whose folder is missing …".
  - `ARCHITECTURE.md`: add an "M13a complete" line (schema 2, newer data never rewritten, `LibraryFiles.Settle`, `HeldFolder` notices).
  - `SESSION-LOG.md`: add an entry.

- [ ] **Step 5: Refresh the hub** (`node --check`, publish with `url`).

- [ ] **Step 6: Commit**

```powershell
git add docs
git commit -m "docs: added ADR-033, M13a checks and results, and recorded the data safety batch"
```
