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
    public void Schema_IsCurrent_AndAnOldConfigIsUpgradedWhenNormalized() // 2 in v1.6, 3 since v1.7 (M14)
    {
        var old = ConfigJson.Deserialize("""{ "schemaVersion": 1, "fences": [] }""");
        Assert.Equal(1, old.SchemaVersion);
        Assert.Equal(NeoFencesConfig.CurrentSchemaVersion, ConfigNormalizer.Normalize(old).SchemaVersion);
    }

    [Fact]
    public void Schema_AVersionOneFileLoads_AndIsSavedAsCurrent_WhileANewerFileIsNeverOverwritten()
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
        File.WriteAllText(path, """{ "schemaVersion": 99, "name": "From the future", "takenAt": "2026-10-04T12:00:00+05:30", "fences": [] }""");

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

        var state = LibraryFiles.Settle(previous, plan, failedWrites: ["Old Name.url", "New.url"], failedDeletes: ["Gone.url"], lostFiles: []);

        Assert.Equal(["Old Name.url", "Same.url", "Gone.url"], state.Items.Select(item => item.FileName));
        Assert.Null(state.Items[0].Game.Poster); // the old file is still the old one: it is retried next time
        Assert.Equal(plan.Items, LibraryFiles.Settle(previous, plan, failedWrites: [], failedDeletes: [], lostFiles: []).Items);
    }
}
