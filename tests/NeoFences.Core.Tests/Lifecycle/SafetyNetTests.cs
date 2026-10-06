using NeoFences.Core.Config;
using NeoFences.Core.Items;
using NeoFences.Core.Lifecycle;
using NeoFences.Core.Model;
using NeoFences.Core.Tests.TestSupport;

namespace NeoFences.Core.Tests.Lifecycle;

/// <summary>M33 (0.20.0, spec 2026-10-06-safety-net-design): safe mode, the watchdog's decision, undo, damaged files, log privacy.</summary>
public class SafetyNetTests
{
    // ---------- safe mode and the gesture switches (RunState) ----------

    private static RunState Normal(bool hideIcons = true) => new(HideIcons: hideIcons, QuickHidden: false, Paused: false, GameMode: false);

    [Fact]
    public void SafeMode_KeepsFencesButTurnsTheExtrasOff()
    {
        var safe = Normal() with { SafeMode = true };

        Assert.True(safe.FencesVisible);
        Assert.False(safe.IconsHidden); // never hide icons after a crash loop
        Assert.False(safe.MouseHookWanted);
        Assert.False(safe.ExtrasWanted); // widgets, panels, auto-collect, library scan, wallpaper watching
        Assert.True(Normal().ExtrasWanted);
    }

    [Fact]
    public void BothGesturesOff_NeverInstallsTheMouseHook()
    {
        Assert.True(Normal().MouseHookWanted);
        Assert.True((Normal() with { QuickHideGesture = false }).MouseHookWanted); // right-drag still needs it
        Assert.False((Normal() with { QuickHideGesture = false, DrawGesture = false }).MouseHookWanted);
    }

    [Fact]
    public void GestureSwitches_AreSettings_OnByDefault_AndRoundTrip()
    {
        Assert.True(new Settings().QuickHideGesture);
        Assert.True(new Settings().DrawGesture);
        var config = NeoFencesConfig.CreateDefault() with { Settings = new Settings { QuickHideGesture = false } };
        Assert.False(ConfigJson.Deserialize(ConfigJson.Serialize(config)).Settings.QuickHideGesture);
        Assert.True(ConfigJson.Deserialize("""{ "schemaVersion": 5, "settings": {} }""").Settings.DrawGesture); // older files: on
    }

    // ---------- the watchdog's decision ----------

    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static IReadOnlyList<DateTimeOffset> Restarts(params int[] minutesAgo) => [.. minutesAgo.Select(minutes => Now.AddMinutes(-minutes))];

    [Fact]
    public void Crash_BelowTheLimit_RestartsNormally() =>
        Assert.Equal(CrashRestart.Normal, CrashRecovery.Decide(Restarts(2, 5), Now, lastStartWasSafe: false));

    [Fact]
    public void ThirdCrashInTenMinutes_RestartsInSafeMode() =>
        Assert.Equal(CrashRestart.SafeMode, CrashRecovery.Decide(Restarts(1, 4, 8), Now, lastStartWasSafe: false));

    [Fact]
    public void SafeModeCrashingSoon_StopsAndAsks() =>
        Assert.Equal(CrashRestart.StopAndAsk, CrashRecovery.Decide(Restarts(1, 3, 6, 9), Now, lastStartWasSafe: true));

    [Fact]
    public void SafeModeCrashingAfterALongRun_StaysInSafeMode() =>
        Assert.Equal(CrashRestart.SafeMode, CrashRecovery.Decide(Restarts(45, 50), Now, lastStartWasSafe: true));

    // ---------- undo (one level) ----------

    private static (NeoFencesConfig Config, ItemsDocument Items, Fence Games, Fence Apps) Setup()
    {
        var (config, games) = FenceEdits.CreateFence(new NeoFencesConfig(), "Games");
        (config, var apps) = FenceEdits.CreateFence(config, "Apps");
        config = config with { LastLayoutFingerprint = "one", Layouts = new Dictionary<string, Layout> { ["one"] = new() { Fences = new Dictionary<string, FenceRect>
        {
            [games.Id] = new("M1", 10, 20, 300, 200), [apps.Id] = new("M1", 400, 20, 300, 200),
        } } } };
        var items = new ItemsDocument()
            .With(games.Id, [VirtualItem.Create(@"C:\Games\a.exe"), VirtualItem.Create(@"C:\Games\b.exe"), VirtualItem.Create(@"C:\Games\c.exe")])
            .With(apps.Id, [VirtualItem.Create(@"C:\Apps\x.exe")]);
        return (config, items, games, apps);
    }

    [Fact]
    public void UndoRemoval_PutsTheItemsBackWhereTheyWere()
    {
        var (config, items, games, _) = Setup();
        var removed = new[] { items.Of(games.Id)[0].Id, items.Of(games.Id)[2].Id };
        var entry = Undo.ForRemoval(items, removed);
        var after = ItemEdits.Remove(items, removed);

        var (_, restored) = Undo.Apply(config, after, entry);

        Assert.Equal(items.Of(games.Id).Select(item => item.Id), restored.Of(games.Id).Select(item => item.Id));
        Assert.Equal("Removed 2 items", entry.Label);
    }

    [Fact]
    public void UndoRemoval_KeepsChangesMadeSince_AndSkipsAFenceThatIsGone()
    {
        var (config, items, games, apps) = Setup();
        var entry = Undo.ForRemoval(items, [items.Of(apps.Id)[0].Id, items.Of(games.Id)[1].Id]);
        var after = ItemEdits.Remove(items, [items.Of(apps.Id)[0].Id, items.Of(games.Id)[1].Id]);
        var appsGone = FenceEdits.DeleteFence(config, apps.Id);
        after = ItemEdits.RemoveFence(after, apps.Id);
        var added = VirtualItem.Create(@"C:\Games\new.exe");
        after = ItemEdits.Add(after, games.Id, [added]).Document;

        var (_, restored) = Undo.Apply(appsGone, after, entry);

        Assert.Contains(restored.Of(games.Id), item => item.Id == added.Id); // a later addition stays
        Assert.Contains(restored.Of(games.Id), item => item.Id == items.Of(games.Id)[1].Id);
        Assert.Empty(restored.Of(apps.Id)); // its fence is gone: nothing to put back into
    }

    [Fact]
    public void UndoDelete_BringsBackTheFence_ItsPlace_AndItsItems()
    {
        var (config, items, games, _) = Setup();
        var entry = Undo.ForDeletion(config, items, games.Id);
        var deleted = FenceEdits.DeleteFence(config, games.Id);
        var deletedItems = ItemEdits.RemoveFence(items, games.Id);

        var (restoredConfig, restoredItems) = Undo.Apply(deleted, deletedItems, entry);

        Assert.Contains(restoredConfig.Fences, fence => fence.Id == games.Id && fence.Title == "Games");
        Assert.Equal(new FenceRect("M1", 10, 20, 300, 200), restoredConfig.Layouts["one"].Fences[games.Id]);
        Assert.Equal(3, restoredItems.Of(games.Id).Count);
        Assert.Equal("Deleted fence Games", entry.Label);
    }

    [Fact]
    public void UndoDelete_OfATab_ComesBackAsItsOwnFence_AtTheBoxPlace()
    {
        var (config, items, games, apps) = Setup();
        var boxed = FenceTabs.Merge(config, movingFenceId: games.Id, targetFenceId: apps.Id); // Games is a tab of Apps' box
        var entry = Undo.ForDeletion(boxed, items, games.Id);
        var deleted = FenceEdits.DeleteFence(boxed, games.Id);

        var (restored, _) = Undo.Apply(deleted, ItemEdits.RemoveFence(items, games.Id), entry);

        var back = Assert.Single(restored.Fences, fence => fence.Id == games.Id);
        Assert.Empty(back.Tabs);
        Assert.True(restored.Layouts["one"].Fences.ContainsKey(games.Id));
        Assert.False(FenceTabs.IsMember(restored, games.Id)); // standalone, not half a box
    }

    // ---------- damaged files fall back; a briefly locked file is waited for ----------

    [Fact]
    public void Load_RepairThatThrows_CountsAsDamaged_AndFallsBackToTheBackup()
    {
        using var directory = new TempDirectory();
        var throwing = false;
        var store = new JsonStore<NeoFencesConfig>(directory.Path, "config.json", NeoFencesConfig.CurrentSchemaVersion, ConfigJson.Deserialize,
            config => config.SchemaVersion, ConfigJson.Serialize, TimeProvider.System,
            repair: config => throwing && config.Fences.Count == 2 ? throw new InvalidOperationException("odd data") : config);
        var (older, _) = FenceEdits.CreateFence(new NeoFencesConfig(), "Older");
        store.Save(older);
        store.Save(FenceEdits.CreateFence(older, "Newer").Config); // the .bak now holds the one-fence version
        throwing = true;

        var (value, source, corruptCopy, _) = store.Load();

        Assert.Equal(ConfigLoadSource.Backup, source);
        Assert.Equal("Older", Assert.Single(value!.Fences).Title);
        Assert.NotNull(corruptCopy);
    }

    [Fact]
    public async Task Load_PrimaryLockedForAMoment_IsWaitedFor_AndReadNormally()
    {
        using var directory = new TempDirectory();
        var store = new ConfigStore(directory.Path, readRetryDelay: TimeSpan.FromMilliseconds(200));
        store.Save(NeoFencesConfig.CreateDefault());
        var locked = new FileStream(store.ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var unlock = Task.Delay(300).ContinueWith(_ => locked.Dispose());

        var result = store.Load();
        await unlock;

        Assert.Equal(ConfigLoadSource.Primary, result.Source);
        Assert.False(result.IsReadOnly);
    }

    [Fact]
    public void Save_ProbesTheFileOnce_WithoutTheLockedFileWait_OrTheRepair() // M33 review I6: a save never freezes the UI
    {
        using var directory = new TempDirectory();
        var repairs = 0;
        var store = new JsonStore<NeoFencesConfig>(directory.Path, "config.json", NeoFencesConfig.CurrentSchemaVersion, ConfigJson.Deserialize,
            config => config.SchemaVersion, ConfigJson.Serialize, TimeProvider.System,
            repair: config => { repairs++; return config; }, readRetryDelay: TimeSpan.FromSeconds(2));
        store.Save(NeoFencesConfig.CreateDefault());
        store.Save(NeoFencesConfig.CreateDefault());
        Assert.Equal(0, repairs);

        using var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Assert.ThrowsAny<IOException>(() => store.Save(NeoFencesConfig.CreateDefault()));
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(1), $"save waited {timer.Elapsed}");
    }

    // ---------- log privacy ----------

    [Fact]
    public void LogPrivacy_MasksTheUserProfile_InAnyCase()
    {
        const string profile = @"C:\Users\Alex";
        Assert.Equal(@"item added: %USERPROFILE%\Downloads\a.zip", LogPrivacy.Mask(@"item added: C:\Users\Alex\Downloads\a.zip", profile));
        Assert.Equal(@"%USERPROFILE%\Desktop", LogPrivacy.Mask(@"c:\users\alex\Desktop", profile));
        Assert.Equal(@"C:\Users\Alexander\x", LogPrivacy.Mask(@"C:\Users\Alexander\x", profile)); // a longer name is another user
        Assert.Equal("no path", LogPrivacy.Mask("no path", profile));
    }
}
