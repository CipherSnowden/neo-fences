using NeoFences.Core.Config;
using NeoFences.Core.Layouts;
using NeoFences.Core.Model;
using NeoFences.Core.Tests.TestSupport;

namespace NeoFences.Core.Tests.Model;

/// <summary>Snapshots (M10, spec 2026-10-03-snapshots-design): save an arrangement by name, restore it later.</summary>
public class SnapshotsTests
{
    private const string Desktop = @"C:\Users\cipher\Desktop\";
    private const string Setup = "1mon:test";
    private static readonly DateTimeOffset Taken = new(2026, 10, 3, 22, 45, 10, TimeSpan.FromHours(5.5));
    private static readonly FenceRect Box = new("mon", 100, 100, 320, 220);

    private static (NeoFencesConfig Config, Fence Inbox, Fence Games, Fence Tools) Sample()
    {
        var inbox = Fence.Create("Inbox") with { IsInbox = true, Items = [Desktop + "notes.txt"] };
        var games = Fence.Create("Games") with { Items = [Desktop + "a.lnk", Desktop + "b.lnk"], IconSize = 64 };
        var tools = Fence.Create("Tools") with { Items = [Desktop + "t.lnk"], TabColor = TabColor.Blue };
        var layout = new Layout
        {
            Monitors = new Dictionary<string, MonitorArea> { ["mon"] = new(1920, 1040) },
            Fences = new Dictionary<string, FenceRect> { [inbox.Id] = Box with { X = 1000 }, [games.Id] = Box, [tools.Id] = Box with { X = 500 } },
        };
        var config = new NeoFencesConfig
        {
            Settings = new Settings { PeekHotkey = "Ctrl+Alt+P" },
            Fences = [inbox, games, tools],
            Layouts = new Dictionary<string, Layout> { [Setup] = layout },
            LastLayoutFingerprint = Setup,
        };
        return (config, inbox, games, tools);
    }

    private static Fence Get(NeoFencesConfig config, Fence fence) => config.Fences.Single(candidate => candidate.Id == fence.Id);

    private static string[] DesktopOf(NeoFencesConfig config) =>
        config.Fences.SelectMany(fence => fence.Items).ToArray();

    [Fact]
    public void TakeThenRestore_BringsBackFencesPlacesAndIcons()
    {
        var (config, inbox, games, tools) = Sample();
        var snapshot = Snapshots.Take(config, name: "Clean desk", now: Taken);

        // Shuffled: Tools merged into Games as a tab, b.lnk moved to the Inbox, Games moved and resized.
        var shuffled = FenceTabs.Merge(config, movingFenceId: tools.Id, targetFenceId: games.Id);
        shuffled = shuffled.WithFence(Get(shuffled, games) with { Items = [Desktop + "a.lnk"] })
                           .WithFence(Get(shuffled, inbox) with { Items = [Desktop + "notes.txt", Desktop + "b.lnk"] });
        shuffled = LayoutEngine.WithFenceRect(shuffled, Setup, games.Id, Box with { X = 1400, W = 500 });

        var restored = Snapshots.Restore(shuffled, snapshot, desktopNow: DesktopOf(config));

        Assert.Equal([Desktop + "a.lnk", Desktop + "b.lnk"], Get(restored, games).Items);
        Assert.Equal([Desktop + "notes.txt"], Get(restored, inbox).Items);
        Assert.Empty(Get(restored, games).Tabs); // the tab merge is undone
        Assert.Equal(TabColor.Blue, Get(restored, tools).TabColor);
        Assert.Equal(64, Get(restored, games).IconSize);
        Assert.Equal(Box, restored.Layouts[Setup].Fences[games.Id]);
        Assert.Equal(Box with { X = 500 }, restored.Layouts[Setup].Fences[tools.Id]);
    }

    [Fact]
    public void Restore_DropsDeletedIcons_AndKeepsNewOnesWhereTheyAre()
    {
        var (config, inbox, games, tools) = Sample();
        var snapshot = Snapshots.Take(config, name: "Clean desk", now: Taken);
        // Since then: b.lnk deleted; new.lnk appeared in Tools; drop.zip appeared in a fence made after the snapshot.
        var later = Fence.Create("Later") with { Items = [Desktop + "drop.zip"] };
        var now = config with { Fences = [.. config.Fences, later] };
        now = now.WithFence(Get(now, tools) with { Items = [Desktop + "t.lnk", Desktop + "new.lnk"] });
        string[] desktopNow = [Desktop + "notes.txt", Desktop + "a.lnk", Desktop + "t.lnk", Desktop + "new.lnk", Desktop + "drop.zip"];

        var restored = Snapshots.Restore(now, snapshot, desktopNow);

        Assert.Equal([Desktop + "a.lnk"], Get(restored, games).Items); // b.lnk is gone
        Assert.Equal([Desktop + "t.lnk", Desktop + "new.lnk"], Get(restored, tools).Items); // stays in its surviving fence
        Assert.DoesNotContain(restored.Fences, fence => fence.Id == later.Id); // made after the snapshot
        Assert.Equal([Desktop + "notes.txt", Desktop + "drop.zip"], Get(restored, inbox).Items); // its fence is gone: Inbox
    }

    [Fact]
    public void Restore_KeepsSettingsAndOtherSetups()
    {
        var (config, _, games, _) = Sample();
        var snapshot = Snapshots.Take(config, name: "Clean desk", now: Taken);
        var otherSetup = new Layout { Monitors = new Dictionary<string, MonitorArea> { ["dock"] = new(2560, 1400) },
            Fences = new Dictionary<string, FenceRect> { [games.Id] = new("dock", 10, 10, 300, 200) } };
        var now = config with
        {
            Settings = config.Settings with { PeekHotkey = "Alt+F9" },
            Layouts = new Dictionary<string, Layout>(config.Layouts) { ["2mon:dock"] = otherSetup },
            LastLayoutFingerprint = "2mon:dock",
        };

        var restored = Snapshots.Restore(now, snapshot, desktopNow: DesktopOf(config));

        Assert.Equal("Alt+F9", restored.Settings.PeekHotkey);
        Assert.Equal("2mon:dock", restored.LastLayoutFingerprint);
        Assert.Equal(otherSetup.Fences[games.Id], restored.Layouts["2mon:dock"].Fences[games.Id]);
        Assert.Equal(Box, restored.Layouts[Setup].Fences[games.Id]);
    }

    [Fact]
    public void Restore_ADamagedSnapshotWithoutAnInbox_StillHasOne()
    {
        var (config, _, games, _) = Sample();
        var snapshot = Snapshots.Take(config, name: "x", now: Taken) with { Fences = [Get(config, games)] };

        var restored = Snapshots.Restore(config, snapshot, desktopNow: DesktopOf(config));

        Assert.Single(restored.Fences, fence => fence.IsInbox);
        Assert.Contains(Desktop + "notes.txt", restored.Inbox.Items);
        Assert.Contains(Desktop + "t.lnk", restored.Inbox.Items); // Tools is not in this snapshot
    }

    [Fact]
    public void Store_SavesListsLoadsAndRenames_NewestFirst()
    {
        using var folder = new TempDirectory();
        var store = new SnapshotStore(folder.Path);
        var (config, _, games, _) = Sample();

        var older = store.Save(Snapshots.Take(config, name: "Older", now: Taken));
        var newer = store.Save(Snapshots.Take(config, name: "Newer", now: Taken.AddMinutes(5)));
        var sameSecond = store.Save(Snapshots.Take(config, name: "Same second", now: Taken.AddMinutes(5)));

        Assert.NotNull(older);
        Assert.NotEqual(newer, sameSecond); // two in the same second get different files
        Assert.Equal(3, store.List().Count);
        Assert.Equal("Older", store.List()[^1].Name); // newest first
        Assert.Equal(64, store.Load(older!)!.Fences.Single(fence => fence.Id == games.Id).IconSize);

        Assert.True(store.Rename(older!, "Clean desk"));
        Assert.Equal("Clean desk", store.Load(older!)!.Name);
        Assert.Equal(Taken, store.Load(older!)!.TakenAt);
    }

    [Fact]
    public void Store_SkipsDamagedFilesAndLeftoverTemps()
    {
        using var folder = new TempDirectory();
        var store = new SnapshotStore(folder.Path);
        var (config, _, _, _) = Sample();
        store.Save(Snapshots.Take(config, name: "Good", now: Taken));
        File.WriteAllText(folder.File("snapshot-broken.json"), "{ not json");
        File.WriteAllText(folder.File("snapshot-half.json.tmp"), "{ \"name\": \"Half");

        var entries = store.List();

        Assert.Equal(["Good"], entries.Select(entry => entry.Name));
        Assert.Single(store.Problems);
        Assert.Null(store.Load(folder.File("snapshot-broken.json")));
    }

    [Fact]
    public void Store_BeforeRestore_IsReplacedEachTime()
    {
        using var folder = new TempDirectory();
        var store = new SnapshotStore(folder.Path);
        var (config, _, _, _) = Sample();

        store.Save(Snapshots.Take(config, name: "Before restore 1", now: Taken), SnapshotStore.BeforeRestoreFileName);
        store.Save(Snapshots.Take(config, name: "Before restore 2", now: Taken.AddMinutes(1)), SnapshotStore.BeforeRestoreFileName);

        Assert.Equal(["Before restore 2"], store.List().Select(entry => entry.Name));
        Assert.True(store.List()[0].IsBeforeRestore);
    }

    [Fact]
    public void Store_RenamingBeforeRestore_KeepsItAsAnOrdinarySnapshot()
    {
        using var folder = new TempDirectory();
        var store = new SnapshotStore(folder.Path);
        var (config, _, _, _) = Sample();
        var path = store.Save(Snapshots.Take(config, name: "Before restore 1", now: Taken), SnapshotStore.BeforeRestoreFileName)!;

        Assert.True(store.Rename(path, "Good layout"));
        store.Save(Snapshots.Take(config, name: "Before restore 2", now: Taken.AddMinutes(1)), SnapshotStore.BeforeRestoreFileName);

        // final review I4: the renamed one is kept, not overwritten by the next restore
        Assert.Equal([("Before restore 2", true), ("Good layout", false)], store.List().Select(entry => (entry.Name, entry.IsBeforeRestore)));
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")] // folder ACLs
    public void Store_AFolderThatCannotBeListed_IsAProblemNotACrash()
    {
        using var folder = new TempDirectory();
        var denied = new System.Security.AccessControl.FileSystemAccessRule(
            System.Security.Principal.WindowsIdentity.GetCurrent().User!, System.Security.AccessControl.FileSystemRights.ListDirectory,
            System.Security.AccessControl.AccessControlType.Deny);
        var directory = new DirectoryInfo(folder.Path);
        var security = directory.GetAccessControl();
        security.AddAccessRule(denied);
        directory.SetAccessControl(security);
        try
        {
            var store = new SnapshotStore(folder.Path);
            Assert.Empty(store.List()); // final review I3: hard rule 7, never a crash on the tray click
            Assert.Single(store.Problems);
        }
        finally
        {
            security.RemoveAccessRule(denied);
            directory.SetAccessControl(security);
        }
    }
}
