using NeoFences.Core.Config;
using NeoFences.Core.Items;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Items;

/// <summary>M26 (0.15.0): the folder panel element — a folder shown inside any fence (spec 2026-10-05-folder-panel-design).</summary>
public class FolderPanelsTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static ItemInfo File(string name, int minutesAgo = 0, long size = 0) =>
        new($@"D:\Panel\{name}", name, IsFolder: false, TypeName: Path.GetExtension(name).ToUpperInvariant(), Day.AddMinutes(-minutesAgo), size);

    private static ItemInfo Folder(string name, int minutesAgo = 0) => new($@"D:\Panel\{name}", name, IsFolder: true, TypeName: "", Day.AddMinutes(-minutesAgo));

    private static IReadOnlyList<string> Names(FolderViews.Selection selection) => [.. selection.Entries.Select(entry => entry.Name)];

    private static readonly IReadOnlyList<ItemInfo> Mixed =
    [
        File("b.png", 5, size: 300), File("a.jpg", 1, size: 2000), File("notes10.txt", 30, size: 10), File("notes2.txt", 20, size: 10),
        Folder("Saves", 2), Folder("Mods", 40),
    ];

    [Fact]
    public void Panels_AreFolderItems_WithTheirOwnDefaultSpan()
    {
        var panel = VirtualItem.Create(@"D:\Panel") with { Panel = new FolderPanel() };
        Assert.Equal(ItemKind.Path, panel.Kind); // a path item: missing state, Locate… and the bulk fix work as for any item
        Assert.True(FolderPanels.IsPanel(panel));
        Assert.Equal(new GridSpan(4, 4), FenceGrid.SpanOf(panel));
        Assert.Equal(new GridSpan(2, 3), FenceGrid.SpanOf(panel with { Size = new GridSpan(2, 3) }));
        Assert.False(FolderPanels.IsPanel(VirtualItem.Create(@"D:\Panel")));
        Assert.False(FolderPanels.IsPanel(VirtualItem.Create("https://example.com") with { Panel = new FolderPanel() })); // only a path can be a panel
    }

    [Fact]
    public void Create_BusyFolders_StartNewestFirst_OthersByName()
    {
        var downloads = FolderPanels.Create(@"C:\Users\me\Downloads", [@"C:\Users\me\Downloads\"]);
        Assert.Equal(@"C:\Users\me\Downloads", downloads.Target);
        Assert.Equal(new FolderPanel { Sort = PanelSort.Date, Descending = true, Newest = FolderViews.BusyFolderNewest }, downloads.Panel);
        Assert.Equal(new FolderPanel(), FolderPanels.Create(@"D:\GameLibrary", [@"C:\Users\me\Downloads"]).Panel);
        Assert.Equal(PanelLook.Details, new FolderPanel().Look);
    }

    [Fact]
    public void Select_ByName_PutsFoldersFirst_BothWays()
    {
        Assert.Equal(["Mods", "Saves", "a.jpg", "b.png", "notes2.txt", "notes10.txt"], Names(FolderPanels.Select(Mixed, new FolderPanel())));
        Assert.Equal(["Saves", "Mods", "notes10.txt", "notes2.txt", "b.png", "a.jpg"], Names(FolderPanels.Select(Mixed, new FolderPanel { Descending = true })));
    }

    [Fact]
    public void Select_ByDate_AndBySize_MixFolders_TiesByName()
    {
        Assert.Equal(["a.jpg", "Saves", "b.png", "notes2.txt", "notes10.txt", "Mods"],
            Names(FolderPanels.Select(Mixed, new FolderPanel { Sort = PanelSort.Date, Descending = true })));
        Assert.Equal(["Mods", "notes10.txt", "notes2.txt", "b.png", "Saves", "a.jpg"],
            Names(FolderPanels.Select(Mixed, new FolderPanel { Sort = PanelSort.Date })));
        // Biggest first; equal sizes by name; folders (no size) last.
        Assert.Equal(["a.jpg", "b.png", "notes2.txt", "notes10.txt", "Mods", "Saves"],
            Names(FolderPanels.Select(Mixed, new FolderPanel { Sort = PanelSort.Size, Descending = true })));
    }

    [Fact]
    public void Select_ByType_KeepsFoldersFirst()
    {
        Assert.Equal(["Mods", "Saves", "a.jpg", "b.png", "notes2.txt", "notes10.txt"], Names(FolderPanels.Select(Mixed, new FolderPanel { Sort = PanelSort.Type })));
        Assert.Equal(["Mods", "Saves", "notes2.txt", "notes10.txt", "b.png", "a.jpg"], // types reversed, ties still A→Z
            Names(FolderPanels.Select(Mixed, new FolderPanel { Sort = PanelSort.Type, Descending = true })));
    }

    [Fact]
    public void Select_FiltersLikeFolderViews_AndCaps()
    {
        Assert.Equal(["Mods", "Saves", "a.jpg", "b.png"], Names(FolderPanels.Select(Mixed, new FolderPanel { Patterns = "png; .JPG" })));
        Assert.Equal(["a.jpg", "b.png"], Names(FolderPanels.Select(Mixed, new FolderPanel { Show = ViewShow.Files, Newest = 2 })));
        var many = Enumerable.Range(0, FolderViews.MaxShown + 7).Select(index => File($"f{index}.txt")).ToList();
        var capped = FolderPanels.Select(many, new FolderPanel());
        Assert.Equal(FolderViews.MaxShown, capped.Entries.Count);
        Assert.Equal(7, capped.Hidden);
    }

    [Theory]
    [InlineData(PanelSort.Name, false, PanelSort.Name, PanelSort.Name, true)] // the same column again: reversed
    [InlineData(PanelSort.Name, true, PanelSort.Name, PanelSort.Name, false)]
    [InlineData(PanelSort.Name, false, PanelSort.Date, PanelSort.Date, true)] // a new column: its natural direction
    [InlineData(PanelSort.Date, true, PanelSort.Size, PanelSort.Size, true)]
    [InlineData(PanelSort.Size, true, PanelSort.Type, PanelSort.Type, false)]
    public void HeaderSort_NewColumnNatural_SameColumnReversed(PanelSort sort, bool descending, PanelSort clicked, PanelSort expectedSort, bool expectedDescending)
    {
        var sorted = FolderPanels.HeaderSort(new FolderPanel { Sort = sort, Descending = descending, Newest = 5 }, clicked);
        Assert.Equal(expectedSort, sorted.Sort);
        Assert.Equal(expectedDescending, sorted.Descending);
        Assert.Equal(5, sorted.Newest); // nothing else changes
    }

    [Theory]
    [InlineData(120.0, 2)]
    [InlineData(299.0, 2)]
    [InlineData(300.0, 4)]
    [InlineData(1200.0, 4)]
    public void Columns_NarrowPanelsShowNameAndDate_ByTheirRealWidth(double widthDips, int expected) => Assert.Equal(expected, FolderPanels.Columns(widthDips));

    [Fact]
    public void Normalize_RepairsHandEdits()
    {
        Assert.Null(FolderPanels.Normalize(null));
        var repaired = FolderPanels.Normalize(new FolderPanel { Look = (PanelLook)9, Sort = (PanelSort)9, Show = (ViewShow)9, Newest = 0, Patterns = @"C:\x" })!;
        Assert.Equal(new FolderPanel(), repaired);
        Assert.Equal(FolderViews.MaxNewest, FolderPanels.Normalize(new FolderPanel { Newest = 100_000 })!.Newest);
    }

    [Fact]
    public void Fills_OnlyALonePanelThatAsks()
    {
        var filling = VirtualItem.Create(@"D:\Panel") with { Panel = new FolderPanel(), Fill = true };
        Assert.True(FolderPanels.Fills([filling]));
        Assert.False(FolderPanels.Fills([filling, VirtualItem.Create(@"D:\other.txt")])); // not alone: cells (its own size or 4×4)
        Assert.False(FolderPanels.Fills([filling with { Panel = null }])); // a plain folder item never fills
        Assert.False(FolderPanels.Fills([filling with { Fill = false }]));
        Assert.False(FolderPanels.Fills([]));
    }

    [Theory]
    [InlineData(FenceSort.Name, PanelSort.Name, false)]
    [InlineData(FenceSort.Manual, PanelSort.Name, false)]
    [InlineData(FenceSort.Type, PanelSort.Type, false)]
    [InlineData(FenceSort.Date, PanelSort.Date, true)]
    public void SortedBy_TheFenceMenuSortsAFillingPanel_AsAViewDid(FenceSort sort, PanelSort expected, bool descending)
    {
        var sorted = FolderPanels.SortedBy(new FolderPanel { Look = PanelLook.Icons, Newest = 30, Sort = PanelSort.Size, Descending = !descending }, sort);
        Assert.Equal(new FolderPanel { Look = PanelLook.Icons, Newest = 30, Sort = expected, Descending = descending }, sorted);
    }

    [Fact]
    public void MigrateViews_SaysWhetherItAddedPanels_SoARepeatNeedsNoSecondSnapshot()
    {
        var view = Fence.Create("Downloads") with { View = new FolderView { Path = @"C:\Users\me\Downloads" } };
        var config = NeoFencesConfig.CreateDefault() with { Fences = [view] };
        var once = FolderPanels.MigrateViews(config, new ItemsDocument());
        Assert.Equal(1, once.AddedPanels);
        Assert.Equal(0, FolderPanels.MigrateViews(config, once.Items).AddedPanels); // a read-only config: the view again, its panel already there
    }

    [Fact]
    public void SetSize_PutsAFillingPanelBackOnCells()
    {
        var filling = VirtualItem.Create(@"D:\Panel") with { Panel = new FolderPanel(), Fill = true };
        var items = new ItemsDocument().With("f", [filling]);
        var sized = ItemEdits.SetSize(items, [filling.Id], new GridSpan(3, 2)).Of("f")[0];
        Assert.False(sized.Fill);
        Assert.Equal(new GridSpan(3, 2), sized.Size);
    }

    [Fact]
    public void FromView_KeepsWhatTheViewShowed()
    {
        var panel = FolderPanels.FromView(new FolderView { Path = @"D:\Shots", Show = ViewShow.Files, Patterns = "png", Newest = 30, Sort = FenceSort.Date });
        Assert.Equal(new FolderPanel { Look = PanelLook.Icons, Show = ViewShow.Files, Patterns = "png", Newest = 30, Sort = PanelSort.Date, Descending = true }, panel);
        Assert.Equal(PanelSort.Type, FolderPanels.FromView(new FolderView { Path = @"D:\Shots", Sort = FenceSort.Type }).Sort);
        Assert.Equal(PanelSort.Name, FolderPanels.FromView(new FolderView { Path = @"D:\Shots", Sort = FenceSort.Manual }).Sort);
    }

    [Fact]
    public void MigrateViews_EachViewBecomesOneFillingPanel_ThenTheViewGoes()
    {
        var view = Fence.Create("Downloads") with { View = new FolderView { Path = @"C:\Users\me\Downloads", Sort = FenceSort.Date, Newest = 30 }, IconSize = 64 };
        var plain = Fence.Create("Apps");
        var config = NeoFencesConfig.CreateDefault() with { Fences = [view, plain] };
        var items = new ItemsDocument().With(plain.Id, [VirtualItem.Create(@"C:\a.lnk")]);

        var migrated = FolderPanels.MigrateViews(config, items);

        Assert.Equal([view.Id], migrated.MigratedFenceIds);
        var fence = migrated.Config.Fences.Single(candidate => candidate.Id == view.Id);
        Assert.Null(fence.View);
        Assert.Equal(FenceKind.Items, fence.Kind);
        Assert.Equal(("Downloads", 64), (fence.Title, fence.IconSize)); // its look stays
        var panel = Assert.Single(migrated.Items.Of(view.Id));
        Assert.Equal(@"C:\Users\me\Downloads", panel.Target);
        Assert.True(panel.Fill);
        Assert.Equal(new FolderPanel { Look = PanelLook.Icons, Sort = PanelSort.Date, Descending = true, Newest = 30 }, panel.Panel);
        Assert.Equal(items.Of(plain.Id), migrated.Items.Of(plain.Id));
    }

    [Fact]
    public void MigrateViews_RunAgainAfterACutShortSave_AddsNoSecondPanel()
    {
        var view = Fence.Create("Downloads") with { View = new FolderView { Path = @"C:\Users\me\Downloads" } };
        var config = NeoFencesConfig.CreateDefault() with { Fences = [view] };
        var once = FolderPanels.MigrateViews(config, new ItemsDocument());
        // The items were saved, the config was not: the next start sees the view again with its panel already there.
        var again = FolderPanels.MigrateViews(config, once.Items);
        Assert.Single(again.Items.Of(view.Id));
        Assert.Null(again.Config.Fences.Single().View);
        Assert.Empty(FolderPanels.MigrateViews(again.Config, again.Items).MigratedFenceIds); // nothing left to do
    }

    [Fact]
    public void Json_WritesPanelAndFill_OnlyWhenSet_AndReadsThemBack()
    {
        const string fenceId = "f";
        var plain = ConfigJson.SerializeItems(new ItemsDocument().With(fenceId, [VirtualItem.Create(@"D:\x")]));
        Assert.DoesNotContain("panel", plain);
        Assert.DoesNotContain("fill", plain);
        var panel = VirtualItem.Create(@"D:\x") with { Panel = new FolderPanel { Look = PanelLook.List, Sort = PanelSort.Size, Descending = true }, Fill = true };
        var json = ConfigJson.SerializeItems(new ItemsDocument().With(fenceId, [panel]));
        Assert.Contains("\"fill\": true", json);
        Assert.Contains("\"look\": \"list\"", json);
        Assert.Equal(panel, ConfigJson.DeserializeItems(json).Of(fenceId)[0]);
    }

    [Fact]
    public void Browsing_IntoBackUpHome_NeverAboveTheHomeFolder()
    {
        const string home = @"D:\GameLibrary";
        var place = PanelPlace.At(home);
        Assert.False(place.CanGoBack);
        Assert.False(place.CanGoUp(home));

        place = place.Into(@"D:\GameLibrary\Cyberpunk").Into(@"D:\GameLibrary\Cyberpunk\bin");
        Assert.Equal(@"D:\GameLibrary\Cyberpunk\bin", place.Current);
        Assert.True(place.CanGoUp(home));

        var up = place.Up(home);
        Assert.Equal(@"D:\GameLibrary\Cyberpunk", up.Current);
        Assert.Equal(@"D:\GameLibrary\Cyberpunk\bin", up.Back().Current); // Up is a step Back undoes

        var back = place.Back();
        Assert.Equal(@"D:\GameLibrary\Cyberpunk", back.Current);
        Assert.Equal(home, back.Back().Current);
        Assert.Equal(home, back.Back().Back().Current); // nothing more to go back to: it stays

        Assert.Equal(home, place.Home(home).Current);
        Assert.Equal(place.Current, place.Home(home).Back().Current);
        var atHome = PanelPlace.At(home);
        Assert.Same(atHome, atHome.Up(home)); // at home, Up and Home do nothing
        Assert.Same(atHome, atHome.Home(home));
    }

    [Theory]
    [InlineData(@"D:\GameLibrary", @"D:\GameLibrary", null, "GameLibrary")]
    [InlineData(@"D:\GameLibrary\", @"D:\GameLibrary", null, "GameLibrary")]
    [InlineData(@"D:\GameLibrary", @"D:\GameLibrary\Cyberpunk\bin", null, "GameLibrary › Cyberpunk › bin")]
    [InlineData(@"D:\GameLibrary", @"D:\GameLibrary", "My games", "My games")]
    [InlineData(@"D:\GameLibrary", @"D:\GameLibrary\Saves", "My games", "My games › Saves")]
    [InlineData(@"D:\", @"D:\Music", null, "D: › Music")]
    public void HeaderOf_NamesTheFolderAndThePathBelowIt(string home, string shown, string? ownName, string expected) =>
        Assert.Equal(expected, FolderPanels.HeaderOf(home, shown, ownName));

    [Theory]
    [InlineData(@"D:\GameLibrary", @"D:\GameLibrary\Saves", true)]
    [InlineData(@"D:\GameLibrary", @"d:\gamelibrary\saves\x", true)]
    [InlineData(@"D:\GameLibrary", @"D:\GameLibrary", false)]
    [InlineData(@"D:\GameLibrary", @"D:\GameLibraryOld", false)]
    [InlineData(@"D:\GameLibrary", @"D:\", false)]
    [InlineData(@"D:\", @"D:\Music", true)]
    public void IsBelow_OnlyRealSubfolders(string home, string folder, bool expected) => Assert.Equal(expected, PanelPlace.IsBelow(folder, home));

    [Theory]
    [InlineData(null, "")]
    [InlineData(0L, "0 KB")]
    [InlineData(1L, "1 KB")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1025L, "2 KB")]
    [InlineData(5_000_000L, "4,883 KB")]
    public void SizeText_LikeExplorer_InWholeKilobytes(long? bytes, string expected) =>
        Assert.Equal(expected, FolderPanels.SizeText(bytes, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Repair_NormalizesAHandEditedPanel_AndATypoNeverFailsTheFile()
    {
        const string json = """{ "schema": 1, "fences": { "f": [ { "id": "a", "target": "D:\\x", "panel": { "look": "tiles", "sort": "colour", "newest": -3 } } ] } }""";
        var item = ItemEdits.Repair(ConfigJson.DeserializeItems(json)).Of("f")[0];
        Assert.Equal(new FolderPanel(), item.Panel);
    }
}
