using NeoFences.Core.Config;
using NeoFences.Core.Model;
using NeoFences.Core.Tests.TestSupport;

namespace NeoFences.Core.Tests.Config;

public class ConfigStoreTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 9, 30, 0, TimeSpan.Zero));

    public void Dispose() => _directory.Dispose();

    private ConfigStore NewStore() => new(_directory.Path, _clock);

    private static NeoFencesConfig ConfigTitled(string title) =>
        NeoFencesConfig.CreateDefault() is var config ? config.WithFence(config.Fences[0] with { Title = title }) : throw new InvalidOperationException();

    private string[] DailyBackupNames(ConfigStore store) =>
        Directory.GetFiles(store.BackupsDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    // ---------- Save ----------

    [Fact]
    public void Save_FirstTime_WritesConfigAndTodaysBackup_NoTempLeft()
    {
        var store = NewStore();

        Assert.True(store.Save(ConfigTitled("First")));

        Assert.Equal("First", ConfigJson.Deserialize(File.ReadAllText(store.ConfigPath)).Fences[0].Title);
        Assert.Equal(["config-20261002.json"], DailyBackupNames(store));
        Assert.False(File.Exists(store.ConfigPath + ".tmp"));
        Assert.False(File.Exists(store.BackupPath));
    }

    [Fact]
    public void Save_SecondTime_KeepsPreviousVersionAsBak()
    {
        var store = NewStore();
        store.Save(ConfigTitled("First"));

        store.Save(ConfigTitled("Second"));

        Assert.Equal("Second", ConfigJson.Deserialize(File.ReadAllText(store.ConfigPath)).Fences[0].Title);
        Assert.Equal("First", ConfigJson.Deserialize(File.ReadAllText(store.BackupPath)).Fences[0].Title);
    }

    [Fact]
    public void Save_SameDay_DailyBackupKeepsFirstSaveOfTheDay()
    {
        var store = NewStore();
        store.Save(ConfigTitled("Morning"));
        _clock.Now = _clock.Now.AddHours(8);

        store.Save(ConfigTitled("Evening"));

        var daily = Path.Combine(store.BackupsDirectory, "config-20261002.json");
        Assert.Equal("Morning", ConfigJson.Deserialize(File.ReadAllText(daily)).Fences[0].Title);
    }

    [Fact]
    public void Save_KeepsOnlyNewestTenDailyBackups()
    {
        var store = NewStore();
        for (var dayIdx = 0; dayIdx < 12; dayIdx++)
        {
            store.Save(ConfigTitled($"Day {dayIdx}"));
            _clock.Now = _clock.Now.AddDays(1);
        }

        var names = DailyBackupNames(store);
        Assert.Equal(ConfigStore.DailyBackupsKept, names.Length);
        Assert.Equal("config-20261004.json", names[0]); // days 0 and 1 pruned
        Assert.Equal("config-20261013.json", names[^1]);
    }

    [Fact]
    public void Save_WhenTempFileCannotBeWritten_Fails_AndExistingConfigIsUntouched()
    {
        var store = NewStore();
        store.Save(ConfigTitled("Good"));
        var before = File.ReadAllText(store.ConfigPath);
        Directory.CreateDirectory(store.ConfigPath + ".tmp"); // a directory where the temp file must go: the write fails

        var failure = Record.Exception(() => store.Save(ConfigTitled("Never written")));

        Assert.True(failure is IOException or UnauthorizedAccessException, $"unexpected {failure?.GetType().Name}");

        Assert.Equal(before, File.ReadAllText(store.ConfigPath));
    }

    // ---------- Load ----------

    [Fact]
    public void Load_NothingOnDisk_IsFreshDefault()
    {
        var result = NewStore().Load();

        Assert.Equal(ConfigLoadSource.Fresh, result.Source);
        Assert.Single(result.Config.Fences);
        Assert.Null(result.CorruptCopyPath);
        Assert.False(result.IsReadOnly);
    }

    [Fact]
    public void Load_ValidConfig_IsPrimary()
    {
        var store = NewStore();
        store.Save(ConfigTitled("Saved"));

        var result = NewStore().Load();

        Assert.Equal(ConfigLoadSource.Primary, result.Source);
        Assert.Equal("Saved", result.Config.Fences[0].Title);
    }

    [Fact]
    public void Load_IsNormalized()
    {
        var store = NewStore();
        File.WriteAllText(store.ConfigPath, """{ "schemaVersion": 5, "fences": [ { "id": "a", "title": "A", "iconSize": 7 } ] }""");

        var result = store.Load();

        Assert.Equal(ConfigLoadSource.Primary, result.Source);
        Assert.Equal(48, Assert.Single(result.Config.Fences).IconSize);
    }

    [Fact]
    public void Load_ConfigFromBeforeTheVirtualItems_StartsFresh_AndTheFirstSaveKeepsTheOldFile()
    {
        // M18 spec §1: a schema-4 config (Inbox, Desktop membership, Portals) is not migrated.
        var store = NewStore();
        const string oldJson = """{ "schemaVersion": 4, "fences": [ { "id": "inbox", "title": "Inbox", "isInbox": true, "items": [ "a.txt" ] } ] }""";
        File.WriteAllText(store.ConfigPath, oldJson);

        var result = store.Load();
        Assert.True(store.Save(result.Config));

        Assert.Equal(ConfigLoadSource.Fresh, result.Source);
        Assert.False(result.IsReadOnly);
        Assert.Equal("Fence", Assert.Single(result.Config.Fences).Title);
        Assert.Equal(oldJson, File.ReadAllText(Path.Combine(store.BackupsDirectory, "pre-schema-5-config.json")));
        Assert.Equal(5, ConfigJson.Deserialize(File.ReadAllText(store.ConfigPath)).SchemaVersion);
    }

    [Fact]
    public void Load_CorruptConfig_KeepsCorruptCopy_AndFallsBackToBak()
    {
        var store = NewStore();
        store.Save(ConfigTitled("Older"));
        store.Save(ConfigTitled("Newer"));
        File.WriteAllText(store.ConfigPath, "{ broken");

        var result = NewStore().Load();

        Assert.Equal(ConfigLoadSource.Backup, result.Source);
        Assert.Equal("Older", result.Config.Fences[0].Title);
        Assert.Equal(_directory.File("config.corrupt-20261002-093000.json"), result.CorruptCopyPath);
        Assert.Equal("{ broken", File.ReadAllText(result.CorruptCopyPath!));
    }

    [Fact]
    public void Load_CorruptConfigAndBak_FallsBackToNewestReadableDailyBackup()
    {
        var store = NewStore();
        store.Save(ConfigTitled("Day 1"));
        _clock.Now = _clock.Now.AddDays(1);
        store.Save(ConfigTitled("Day 2"));
        File.WriteAllText(Path.Combine(store.BackupsDirectory, "config-20261003.json"), "garbage");
        File.WriteAllText(store.ConfigPath, "garbage");
        File.WriteAllText(store.BackupPath, "garbage");

        var result = NewStore().Load();

        Assert.Equal(ConfigLoadSource.DailyBackup, result.Source);
        Assert.Equal("Day 1", result.Config.Fences[0].Title);
    }

    [Fact]
    public void Load_EverythingCorrupt_IsFreshButCorruptFileIsKept()
    {
        var store = NewStore();
        File.WriteAllText(store.ConfigPath, "garbage");

        var result = store.Load();

        Assert.Equal(ConfigLoadSource.Fresh, result.Source);
        Assert.NotNull(result.CorruptCopyPath);
        Assert.True(File.Exists(result.CorruptCopyPath));
    }

    [Fact]
    public void Load_ConfigFromNewerVersion_ShowsNoWelcome()
    {
        // M30 final review I1: a read-only fresh start is an existing user on an older build, not a first run.
        var store = NewStore();
        File.WriteAllText(store.ConfigPath, """{ "schemaVersion": 99 }""");

        var result = store.Load();

        Assert.True(result.IsReadOnly);
        Assert.DoesNotContain(result.Config.Fences, fence => fence.Welcome);
    }

    [Fact]
    public void Load_ConfigFromNewerVersion_IsReadOnly_AndSaveNeverOverwritesIt()
    {
        var store = NewStore();
        const string newerJson = """{ "schemaVersion": 99, "somethingNew": true }""";
        File.WriteAllText(store.ConfigPath, newerJson);

        var result = store.Load();
        var saved = store.Save(result.Config);

        Assert.True(result.IsReadOnly);
        Assert.Equal(ConfigLoadSource.Fresh, result.Source);
        Assert.False(saved);
        Assert.Equal(newerJson, File.ReadAllText(store.ConfigPath));
    }

    [Fact]
    public void Load_PrimaryLockedByAnotherProcess_IsReadOnly_UsesBackup_AndSaveDoesNotOverwrite()
    {
        var store = NewStore();
        store.Save(ConfigTitled("Precious"));
        var before = File.ReadAllText(store.ConfigPath);
        var lockedStore = NewStore();

        ConfigLoadResult result;
        using (new FileStream(store.ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = lockedStore.Load();
        }
        var saved = lockedStore.Save(result.Config);

        Assert.True(result.IsReadOnly);
        Assert.Equal("Precious", result.Config.Fences[0].Title); // from the daily backup
        Assert.False(saved);
        Assert.Equal(before, File.ReadAllText(store.ConfigPath));
    }

    [Fact]
    public void Save_FromStoreThatNeverLoaded_NeverOverwritesNewerConfig()
    {
        var store = NewStore();
        const string newerJson = """{ "schemaVersion": 99 }""";
        File.WriteAllText(store.ConfigPath, newerJson);

        Assert.False(store.Save(NeoFencesConfig.CreateDefault()));
        Assert.Equal(newerJson, File.ReadAllText(store.ConfigPath));
    }

    [Fact]
    public void Save_WhenTheDailyBackupCannotBeWritten_StillSaves_AndReportsTheBackupFailure()
    {
        // A file where the backups folder should be (or a full disk): the config itself must still save (M1 carry-over).
        var store = NewStore();
        File.WriteAllText(store.BackupsDirectory, "not a folder");

        Assert.True(store.Save(ConfigTitled("Saved")));

        Assert.Equal("Saved", ConfigJson.Deserialize(File.ReadAllText(store.ConfigPath)).Fences[0].Title);
        Assert.NotNull(store.LastBackupFailure);
    }

    [Fact]
    public void Save_WithWorkingBackups_ReportsNoBackupFailure()
    {
        var store = NewStore();
        Assert.True(store.Save(ConfigTitled("Saved")));
        Assert.Null(store.LastBackupFailure);
    }
}
