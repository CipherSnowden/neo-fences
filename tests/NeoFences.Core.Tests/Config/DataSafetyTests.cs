using NeoFences.Core.Config;
using NeoFences.Core.Items;
using NeoFences.Core.Library;
using NeoFences.Core.Model;
using NeoFences.Core.Tests.TestSupport;

namespace NeoFences.Core.Tests.Config;

/// <summary>
/// M13a (v1.6.0) data safety: carry-overs from the M9–M12 reviews — the schema version, snapshots from newer versions,
/// the library index after failed writes, and the M10 test gaps (M18: with virtual items).
/// </summary>
public class DataSafetyTests : IDisposable
{
    private const string Desktop = @"C:\Users\cipher\Desktop\";
    private static readonly DateTimeOffset Taken = new(2026, 10, 4, 12, 0, 0, TimeSpan.FromHours(5.5));
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    // ---------- schema version (M9 carry-over: an older build would save over newer fields) ----------

    [Fact]
    public void Schema_IsCurrent_AndAnOldConfigIsUpgradedWhenNormalized()
    {
        var old = ConfigJson.Deserialize("""{ "schemaVersion": 1, "fences": [] }""");
        Assert.Equal(1, old.SchemaVersion);
        Assert.Equal(NeoFencesConfig.CurrentSchemaVersion, ConfigNormalizer.Normalize(old).SchemaVersion);
    }

    [Fact]
    public void Schema_AnOlderFileStartsFresh_AndIsSavedAsCurrent_WhileANewerFileIsNeverOverwritten()
    {
        var store = new ConfigStore(_directory.Path, new FixedTimeProvider(Taken));
        File.WriteAllText(store.ConfigPath, """{ "schemaVersion": 1, "fences": [ { "id": "a", "title": "Games" } ] }""");

        var loaded = store.Load();
        Assert.False(loaded.IsReadOnly);
        Assert.True(store.Save(loaded.Config));
        Assert.Contains($"\"schemaVersion\": {NeoFencesConfig.CurrentSchemaVersion}", File.ReadAllText(store.ConfigPath));

        File.WriteAllText(store.ConfigPath, """{ "schemaVersion": 99, "fences": [] }""");
        var newer = new ConfigStore(_directory.Path, new FixedTimeProvider(Taken));
        Assert.True(newer.Load().IsReadOnly);
        Assert.False(newer.Save(NeoFencesConfig.CreateDefault()));
    }

    // ---------- snapshots (M10 carry-overs and test gaps) ----------

    private static (NeoFencesConfig Config, ItemsDocument Items) Sample()
    {
        var games = Fence.Create("Games");
        return (new NeoFencesConfig { Fences = [games] }, new ItemsDocument().With(games.Id, [VirtualItem.Create(Desktop + "Game.lnk")]));
    }

    [Fact]
    public void Snapshots_FromANewerVersionAreRefused_NotHalfRead()
    {
        var store = new SnapshotStore(_directory.Path);
        var path = Path.Combine(_directory.Path, "snapshot-newer.json");
        File.WriteAllText(path, """{ "schemaVersion": 99, "name": "From the future", "takenAt": "2026-10-04T12:00:00+05:30", "fences": [] }""");

        Assert.Null(store.Load(path)); // a rename would rewrite it without the fields this version does not know
        Assert.NotNull(store.LastFailure);
        Assert.Empty(store.List());
        Assert.Equal([path], store.Problems.Select(problem => problem.Path));
    }

    [Fact]
    public void Snapshots_FromBeforeTheVirtualItemsAreRefused()
    {
        // M18: a schema-4 snapshot's fences held Desktop files; restoring it would bring back empty Inbox and Portal fences.
        var store = new SnapshotStore(_directory.Path);
        var path = Path.Combine(_directory.Path, "snapshot-old.json");
        File.WriteAllText(path, """{ "schemaVersion": 4, "name": "Old", "takenAt": "2026-10-03T12:00:00+05:30", "fences": [ { "id": "i", "title": "Inbox", "isInbox": true } ] }""");

        Assert.Null(store.Load(path));
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
        var (config, items) = Sample();

        Assert.Null(store.Save(Snapshots.Take(config, items, name: "x", now: Taken), "blocked.json"));
        Assert.NotNull(store.LastFailure);
        Assert.Empty(Directory.GetFiles(_directory.Path, "*.tmp"));
    }

    [Fact]
    public void Snapshots_AFileWithNullFieldsStillRestores()
    {
        var store = new SnapshotStore(_directory.Path);
        var path = Path.Combine(_directory.Path, "nulls.json");
        File.WriteAllText(path, """{ "schemaVersion": 5, "name": null, "takenAt": "2026-10-04T12:00:00+05:30", "fences": [ null, { "id": "g", "title": null, "tabs": null } ], "layouts": null, "items": { "g": [ null, { "id": "x", "target": "" } ] } }""");

        var snapshot = store.Load(path)!;
        var (config, items) = Sample();
        var (restored, restoredItems) = Snapshots.Restore(config, snapshot);

        Assert.Equal("g", Assert.Single(restored.Fences).Id);
        Assert.Empty(restoredItems.Of("g"));
        Assert.Single(items.Fences); // the current document is untouched
    }

    // ---------- the Game Library index after failed writes (M12 carry-over) ----------

    private static GameEntry Game(string id, string name) => new(id, name, GameSource.Steam, "steam", new GameLaunch($"steam://rungameid/{id}"));

    [Fact]
    public void LibraryIndex_AFailedRewriteKeepsTheOldEntry_AFailedDeleteStaysListed_AFailedNewFileIsLeftOut()
    {
        var previous = LibraryFiles.Plan(previous: [], games: [Game("1", "Old Name"), Game("2", "Gone"), Game("3", "Same")]).Items;
        var plan = LibraryFiles.Plan(previous, games: [Game("1", "Old Name") with { Poster = @"C:\new.jpg" }, Game("3", "Same"), Game("4", "New")]);

        var state = LibraryFiles.Settle(previous, plan, failedWrites: ["Old Name.url", "New.url"], failedDeletes: ["Gone.url"], lostFiles: []);

        Assert.Equal(["Old Name.url", "Same.url", "Gone.url"], state.Items.Select(item => item.FileName));
        Assert.Null(state.Items[0].Game.Poster); // the old file is still the old one: it is retried next time
        Assert.Equal(plan.Items, LibraryFiles.Settle(previous, plan, failedWrites: [], failedDeletes: [], lostFiles: []).Items);
    }
}
