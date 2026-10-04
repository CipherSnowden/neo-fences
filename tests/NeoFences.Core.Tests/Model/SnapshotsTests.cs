using NeoFences.Core.Config;
using NeoFences.Core.Items;
using NeoFences.Core.Layouts;
using NeoFences.Core.Model;
using NeoFences.Core.Tests.TestSupport;

namespace NeoFences.Core.Tests.Model;

/// <summary>Snapshots (M10, spec 2026-10-03-snapshots-design; M18: with the virtual items): save an arrangement, restore it later.</summary>
public class SnapshotsTests
{
    private const string Desktop = @"C:\Users\cipher\Desktop\";
    private const string Setup = "1mon:test";
    private static readonly DateTimeOffset Taken = new(2026, 10, 3, 22, 45, 10, TimeSpan.FromHours(5.5));
    private static readonly FenceRect Box = new("mon", 100, 100, 320, 220);

    private static VirtualItem Item(string id, string target, string? name = null) => new() { Id = id, Target = target, Name = name };

    private static (NeoFencesConfig Config, ItemsDocument Items, Fence Notes, Fence Games, Fence Tools) Sample()
    {
        var notes = Fence.Create("Notes");
        var games = Fence.Create("Games") with { IconSize = 64 };
        var tools = Fence.Create("Tools") with { TabColor = TabColor.Blue };
        var layout = new Layout
        {
            Monitors = new Dictionary<string, MonitorArea> { ["mon"] = new(1920, 1040) },
            Fences = new Dictionary<string, FenceRect> { [notes.Id] = Box with { X = 1000 }, [games.Id] = Box, [tools.Id] = Box with { X = 500 } },
        };
        var config = new NeoFencesConfig
        {
            Settings = new Settings { PeekHotkey = "Ctrl+Alt+P" },
            Fences = [notes, games, tools],
            Layouts = new Dictionary<string, Layout> { [Setup] = layout },
            LastLayoutFingerprint = Setup,
        };
        var items = new ItemsDocument()
            .With(notes.Id, [Item("n", Desktop + "notes.txt")])
            .With(games.Id, [Item("a", Desktop + "a.lnk", name: "Alpha"), Item("b", Desktop + "b.lnk")])
            .With(tools.Id, [Item("t", Desktop + "t.lnk")]);
        return (config, items, notes, games, tools);
    }

    private static Fence Get(NeoFencesConfig config, Fence fence) => config.Fences.Single(candidate => candidate.Id == fence.Id);

    private static string[] Ids(ItemsDocument items, Fence fence) => [.. items.Of(fence.Id).Select(item => item.Id)];

    [Fact]
    public void TakeThenRestore_BringsBackFencesPlacesAndItems()
    {
        var (config, items, notes, games, tools) = Sample();
        var snapshot = Snapshots.Take(config, items, name: "Clean desk", now: Taken);

        // Shuffled: Tools merged into Games as a tab, b moved to Notes, a renamed, Games moved and resized.
        var shuffled = FenceTabs.Merge(config, movingFenceId: tools.Id, targetFenceId: games.Id);
        shuffled = LayoutEngine.WithFenceRect(shuffled, Setup, games.Id, Box with { X = 1400, W = 500 });
        var shuffledItems = ItemEdits.Move(items, ["b"], notes.Id, insertAt: 1);
        shuffledItems = ItemEdits.Replace(shuffledItems, Item("a", Desktop + "a.lnk", name: "Renamed"));

        var (restored, restoredItems) = Snapshots.Restore(shuffled, snapshot);

        Assert.Equal(["a", "b"], Ids(restoredItems, games));
        Assert.Equal(["n"], Ids(restoredItems, notes));
        Assert.Equal("Alpha", restoredItems.Find("a")!.Name);
        Assert.Empty(Get(restored, games).Tabs); // the tab merge is undone
        Assert.Equal(TabColor.Blue, Get(restored, tools).TabColor);
        Assert.Equal(64, Get(restored, games).IconSize);
        Assert.Equal(Box, restored.Layouts[Setup].Fences[games.Id]);
        Assert.Equal(Box with { X = 500 }, restored.Layouts[Setup].Fences[tools.Id]);
    }

    [Fact]
    public void Restore_FencesMadeAfterTheSnapshotGo_WithTheirItems()
    {
        var (config, items, _, games, _) = Sample();
        var snapshot = Snapshots.Take(config, items, name: "Clean desk", now: Taken);
        var later = Fence.Create("Later");
        var now = config with { Fences = [.. config.Fences, later] };
        var nowItems = items.With(later.Id, [Item("z", Desktop + "drop.zip")]);

        var (restored, restoredItems) = Snapshots.Restore(now, snapshot);

        Assert.DoesNotContain(restored.Fences, fence => fence.Id == later.Id);
        Assert.Null(restoredItems.Find("z"));
        Assert.Equal(["a", "b"], Ids(restoredItems, games));
        Assert.Single(nowItems.Of(later.Id)); // the current document itself is untouched
    }

    [Fact]
    public void Restore_KeepsSettingsAndOtherSetups()
    {
        var (config, items, _, games, _) = Sample();
        var snapshot = Snapshots.Take(config, items, name: "Clean desk", now: Taken);
        var otherSetup = new Layout { Monitors = new Dictionary<string, MonitorArea> { ["dock"] = new(2560, 1400) },
            Fences = new Dictionary<string, FenceRect> { [games.Id] = new("dock", 10, 10, 300, 200) } };
        var now = config with
        {
            Settings = config.Settings with { PeekHotkey = "Alt+F9" },
            Layouts = new Dictionary<string, Layout>(config.Layouts) { ["2mon:dock"] = otherSetup },
            LastLayoutFingerprint = "2mon:dock",
        };

        var (restored, _) = Snapshots.Restore(now, snapshot);

        Assert.Equal("Alt+F9", restored.Settings.PeekHotkey);
        Assert.Equal("2mon:dock", restored.LastLayoutFingerprint);
        Assert.Equal(otherSetup.Fences[games.Id], restored.Layouts["2mon:dock"].Fences[games.Id]);
        Assert.Equal(Box, restored.Layouts[Setup].Fences[games.Id]);
    }

    [Fact]
    public void Restore_ADamagedSnapshot_DropsItemsOfFencesItDoesNotHave_AndRepairsIds()
    {
        var (config, items, _, games, _) = Sample();
        var snapshot = Snapshots.Take(config, items, name: "x", now: Taken) with
        {
            Fences = [Get(config, games)],
            Items = new Dictionary<string, IReadOnlyList<VirtualItem>>
            {
                [games.Id] = [Item("same", Desktop + "a.lnk"), Item("same", Desktop + "b.lnk")],
                ["gone"] = [Item("g", Desktop + "g.lnk")],
            },
        };

        var (restored, restoredItems) = Snapshots.Restore(config, snapshot);

        Assert.Equal(games.Id, Assert.Single(restored.Fences).Id);
        Assert.Equal(2, restoredItems.Of(games.Id).Select(item => item.Id).Distinct().Count());
        Assert.Equal([games.Id], restoredItems.Fences.Keys);
    }

    [Fact]
    public void Store_SavesListsLoadsAndRenames_NewestFirst()
    {
        using var folder = new TempDirectory();
        var store = new SnapshotStore(folder.Path);
        var (config, items, _, games, _) = Sample();

        var older = store.Save(Snapshots.Take(config, items, name: "Older", now: Taken));
        var newer = store.Save(Snapshots.Take(config, items, name: "Newer", now: Taken.AddMinutes(5)));
        var sameSecond = store.Save(Snapshots.Take(config, items, name: "Same second", now: Taken.AddMinutes(5)));

        Assert.NotNull(older);
        Assert.NotEqual(newer, sameSecond); // two in the same second get different files
        Assert.Equal(3, store.List().Count);
        Assert.Equal("Older", store.List()[^1].Name); // newest first
        Assert.Equal(64, store.Load(older!)!.Fences.Single(fence => fence.Id == games.Id).IconSize);
        Assert.Equal("Alpha", store.Load(older!)!.Items[games.Id][0].Name);

        Assert.True(store.Rename(older!, "Clean desk"));
        Assert.Equal("Clean desk", store.Load(older!)!.Name);
        Assert.Equal(Taken, store.Load(older!)!.TakenAt);
    }

    [Fact]
    public void Store_SkipsDamagedFilesAndLeftoverTemps()
    {
        using var folder = new TempDirectory();
        var store = new SnapshotStore(folder.Path);
        var (config, items, _, _, _) = Sample();
        store.Save(Snapshots.Take(config, items, name: "Good", now: Taken));
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
        var (config, items, _, _, _) = Sample();

        store.Save(Snapshots.Take(config, items, name: "Before restore 1", now: Taken), SnapshotStore.BeforeRestoreFileName);
        store.Save(Snapshots.Take(config, items, name: "Before restore 2", now: Taken.AddMinutes(1)), SnapshotStore.BeforeRestoreFileName);

        Assert.Equal(["Before restore 2"], store.List().Select(entry => entry.Name));
        Assert.True(store.List()[0].IsBeforeRestore);
    }

    [Fact]
    public void Store_RenamingBeforeRestore_KeepsItAsAnOrdinarySnapshot()
    {
        using var folder = new TempDirectory();
        var store = new SnapshotStore(folder.Path);
        var (config, items, _, _, _) = Sample();
        var path = store.Save(Snapshots.Take(config, items, name: "Before restore 1", now: Taken), SnapshotStore.BeforeRestoreFileName)!;

        Assert.True(store.Rename(path, "Good layout"));
        store.Save(Snapshots.Take(config, items, name: "Before restore 2", now: Taken.AddMinutes(1)), SnapshotStore.BeforeRestoreFileName);

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
