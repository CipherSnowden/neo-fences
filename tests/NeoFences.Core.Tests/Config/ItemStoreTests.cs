using NeoFences.Core.Config;
using NeoFences.Core.Items;
using NeoFences.Core.Tests.TestSupport;

namespace NeoFences.Core.Tests.Config;

/// <summary>items.json (ADR-041): the same safe save and recovery as config.json, in a file of its own.</summary>
public class ItemStoreTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 10, 4, 9, 30, 0, TimeSpan.Zero));

    public void Dispose() => _directory.Dispose();

    private ItemStore NewStore() => new(_directory.Path, _clock);

    private static ItemsDocument Holding(string name) =>
        new ItemsDocument().With("fence", [new VirtualItem { Id = "a", Target = @"D:\Games\a.exe", Name = name, Icon = new ItemIcon { File = @"C:\x.dll", Index = 3 } }]);

    [Fact]
    public void Save_ThenLoad_RoundTripsEveryField_NoTempLeft()
    {
        var item = new VirtualItem
        {
            Id = "a", Target = @"D:\Games\a.exe", Name = "A", Arguments = "-windowed", RunAsAdmin = true, Note = "the note",
            Icon = new ItemIcon { Image = "a.png" },
        };
        var store = NewStore();

        Assert.True(store.Save(new ItemsDocument().With("fence", [item])));
        var loaded = NewStore().Load();

        Assert.Equal(ConfigLoadSource.Primary, loaded.Source);
        Assert.Equal(item, loaded.Document.Of("fence").Single());
        Assert.False(File.Exists(store.ItemsPath + ".tmp"));
        Assert.Equal(["items-20261004.json"], Directory.GetFiles(store.BackupsDirectory).Select(Path.GetFileName));
        Assert.Contains("\"schema\": 1", File.ReadAllText(store.ItemsPath));
        Assert.DoesNotContain("\"kind\"", File.ReadAllText(store.ItemsPath)); // derived, never stored
    }

    [Fact]
    public void Load_NothingOnDisk_IsEmpty()
    {
        var loaded = NewStore().Load();

        Assert.Equal(ConfigLoadSource.Fresh, loaded.Source);
        Assert.Empty(loaded.Document.Fences);
        Assert.False(loaded.IsReadOnly);
    }

    [Fact]
    public void Load_ADamagedFile_IsKept_AndTheBackupIsUsed()
    {
        var store = NewStore();
        store.Save(Holding("Older"));
        store.Save(Holding("Newer"));
        File.WriteAllText(store.ItemsPath, "{ broken");

        var loaded = NewStore().Load();

        Assert.Equal(ConfigLoadSource.Backup, loaded.Source);
        Assert.Equal("Older", loaded.Document.Find("a")!.Name);
        Assert.Equal(_directory.File("items.corrupt-20261004-093000.json"), loaded.CorruptCopyPath);
    }

    [Fact]
    public void Load_FromANewerVersion_IsReadOnly_AndNeverOverwritten()
    {
        var store = NewStore();
        const string newer = """{ "schema": 99, "fences": {} }""";
        File.WriteAllText(store.ItemsPath, newer);

        var loaded = store.Load();

        Assert.True(loaded.IsReadOnly);
        Assert.False(store.Save(Holding("x")));
        Assert.Equal(newer, File.ReadAllText(store.ItemsPath));
    }

    [Fact]
    public void Load_RepairsAHandEditedFile()
    {
        var store = NewStore();
        File.WriteAllText(store.ItemsPath, """{ "schema": 1, "fences": { "f": [ { "id": "x", "target": "D:\\a.exe" }, { "id": "x", "target": "D:\\b.exe" }, null ] } }""");

        var items = store.Load().Document.Of("f");

        Assert.Equal(2, items.Select(item => item.Id).Distinct().Count());
    }

    [Fact]
    public void OnlyARealConfig_MayDropItemListsOfUnknownFences()
    {
        // A fresh or read-only config is a fallback: its fence ids are not the user's, so items.json's lists must stay (ADR-041).
        var config = new ConfigStore(_directory.Path, _clock);
        Assert.False(config.Load().KnowsTheFences); // nothing on disk yet: fresh
        config.Save(NeoFences.Core.Model.NeoFencesConfig.CreateDefault());
        Assert.True(new ConfigStore(_directory.Path, _clock).Load().KnowsTheFences);
        File.WriteAllText(config.ConfigPath, """{ "schemaVersion": 99 }""");
        Assert.False(new ConfigStore(_directory.Path, _clock).Load().KnowsTheFences); // a newer NeoFences': read-only
        File.WriteAllText(config.ConfigPath, """{ "schemaVersion": 4, "fences": [] }""");
        Assert.False(new ConfigStore(_directory.Path, _clock).Load().KnowsTheFences); // from before the virtual items: fresh
    }

    [Fact]
    public void ADamagedItemsFile_NeverTouchesConfigJson()
    {
        var config = new ConfigStore(_directory.Path, _clock);
        config.Save(NeoFences.Core.Model.NeoFencesConfig.CreateDefault());
        var before = File.ReadAllText(config.ConfigPath);
        File.WriteAllText(NewStore().ItemsPath, "garbage");

        NewStore().Load();

        Assert.Equal(before, File.ReadAllText(config.ConfigPath));
        Assert.Equal(ConfigLoadSource.Primary, new ConfigStore(_directory.Path, _clock).Load().Source);
    }
}
