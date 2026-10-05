using NeoFences.Core.Config;
using NeoFences.Core.Items;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Items;

/// <summary>M21 (0.11.0): folder views — a fence that shows one folder live, read-only (spec 2026-10-05-folder-views-design).</summary>
public class FolderViewsTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static ItemInfo File(string name, int minutesAgo = 0) =>
        new($@"D:\View\{name}", name, IsFolder: false, TypeName: Path.GetExtension(name).ToUpperInvariant(), Day.AddMinutes(-minutesAgo));

    private static ItemInfo Folder(string name, int minutesAgo = 0) => new($@"D:\View\{name}", name, IsFolder: true, TypeName: "", Day.AddMinutes(-minutesAgo));

    private static IReadOnlyList<string> Names(FolderViews.Selection selection) => [.. selection.Shown.Select(Path.GetFileName)!];

    private static readonly IReadOnlyList<ItemInfo> Mixed =
    [
        File("b.png", 5), File("a.jpg", 1), File("notes10.txt", 30), File("notes2.txt", 20), Folder("Saves", 2), Folder("Mods", 40),
    ];

    [Fact]
    public void Select_ByName_PutsFoldersFirst_InNaturalOrder()
    {
        var selection = FolderViews.Select(Mixed, new FolderView { Path = @"D:\View" });
        Assert.Equal(["Mods", "Saves", "a.jpg", "b.png", "notes2.txt", "notes10.txt"], Names(selection));
        Assert.Equal(0, selection.Hidden);
    }

    [Fact]
    public void Select_ByDate_IsNewestFirst_FoldersMixedIn()
    {
        var selection = FolderViews.Select(Mixed, new FolderView { Path = @"D:\View", Sort = FenceSort.Date });
        Assert.Equal(["a.jpg", "Saves", "b.png", "notes2.txt", "notes10.txt", "Mods"], Names(selection));
    }

    [Theory]
    [InlineData(ViewShow.Files, new[] { "a.jpg", "b.png", "notes2.txt", "notes10.txt" })]
    [InlineData(ViewShow.Folders, new[] { "Mods", "Saves" })]
    public void Select_ShowsOnlyTheChosenKind(ViewShow show, string[] expected) =>
        Assert.Equal(expected, Names(FolderViews.Select(Mixed, new FolderView { Path = @"D:\View", Show = show })));

    [Fact]
    public void Select_Patterns_FilterFiles_ButKeepSubfolders_WhenShowingAll()
    {
        var all = FolderViews.Select(Mixed, new FolderView { Path = @"D:\View", Patterns = "*.png; *.JPG" });
        Assert.Equal(["Mods", "Saves", "a.jpg", "b.png"], Names(all));
        var filesOnly = FolderViews.Select(Mixed, new FolderView { Path = @"D:\View", Show = ViewShow.Files, Patterns = "png" });
        Assert.Equal(["b.png"], Names(filesOnly));
    }

    [Fact]
    public void Select_Newest_TakesTheLatestByDate_ThenSorts()
    {
        var selection = FolderViews.Select(Mixed, new FolderView { Path = @"D:\View", Show = ViewShow.Files, Newest = 2 });
        Assert.Equal(["a.jpg", "b.png"], Names(selection)); // the two newest files, then by name
        Assert.Equal(0, selection.Hidden);
    }

    [Fact]
    public void Select_CapsAtMaxShown_AndCountsTheRest()
    {
        var many = Enumerable.Range(0, FolderViews.MaxShown + 34).Select(index => File($"file{index}.txt", index)).ToList();
        var selection = FolderViews.Select(many, new FolderView { Path = @"D:\View" });
        Assert.Equal(FolderViews.MaxShown, selection.Shown.Count);
        Assert.Equal(34, selection.Hidden);
        Assert.Equal("file0.txt", Names(selection)[0]);
    }

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("  ", new string[0])]
    [InlineData("*.png;*.jpg", new[] { "*.png", "*.jpg" })]
    [InlineData(" png , .JPG ;; report?.pdf ", new[] { "*.png", "*.JPG", "report?.pdf" })]
    public void ParsePatterns_AcceptsPatternsAndBareExtensions(string text, string[] expected) =>
        Assert.Equal(expected, FolderViews.ParsePatterns(text));

    [Theory]
    [InlineData(@"C:\*.png")]
    [InlineData("a/b")]
    [InlineData("*.png|*.jpg")]
    [InlineData("\"x\"")]
    [InlineData("a:b")]
    [InlineData("<x>")]
    public void ParsePatterns_RejectsPathsAndForbiddenCharacters(string text) => Assert.Null(FolderViews.ParsePatterns(text));

    [Fact]
    public void DefaultsFor_BusyFolders_AreNewestFirst_OthersByName()
    {
        string[] busy = [@"C:\Users\c\Downloads", @"C:\Users\c\Pictures\Screenshots"];
        var downloads = FolderViews.DefaultsFor(@"c:\users\c\downloads\", busy);
        Assert.Equal(new FolderView { Path = @"c:\users\c\downloads\", Sort = FenceSort.Date, Newest = FolderViews.BusyFolderNewest }, downloads);
        Assert.Equal(new FolderView { Path = @"D:\GameLibrary" }, FolderViews.DefaultsFor(@"D:\GameLibrary", busy));
    }

    [Theory]
    [InlineData(@"D:\GameLibrary", "GameLibrary")]
    [InlineData(@"D:\GameLibrary\", "GameLibrary")]
    [InlineData(@"D:\", "D:")]
    [InlineData(@"\\nas\share", "share")]
    public void NameOf_IsTheFolderName_OrTheDrive(string path, string expected) => Assert.Equal(expected, FolderViews.NameOf(path));

    [Fact]
    public void Status_SaysWhatTheFenceShowsInsteadOfItems()
    {
        var view = new FolderView { Path = @"D:\View", Patterns = "*.zip" };
        Assert.Equal((@"Folder not available: D:\View", null), FolderViews.Status(null, view));
        Assert.Equal(("This folder is empty", null), FolderViews.Status(FolderViews.Select([], view), view));
        Assert.Equal(("Nothing here matches this view", null), FolderViews.Status(FolderViews.Select([File("a.txt")], view), view));
        var many = Enumerable.Range(0, FolderViews.MaxShown + 1234).Select(index => File($"f{index}.zip")).ToList();
        Assert.Equal((null, "+ 1,234 more — Open folder"), FolderViews.Status(FolderViews.Select(many, view), view));
    }

    [Fact]
    public void Normalize_RepairsHandEditedViews()
    {
        Assert.Null(FolderViews.Normalize(null));
        Assert.Null(FolderViews.Normalize(new FolderView { Path = "  " }));
        var repaired = FolderViews.Normalize(new FolderView { Path = @"D:\X", Show = (ViewShow)9, Sort = FenceSort.Manual, Newest = 9999, Patterns = "a|b" });
        Assert.Equal(new FolderView { Path = @"D:\X", Newest = FolderViews.MaxNewest }, repaired);
        Assert.Null(FolderViews.Normalize(new FolderView { Path = @"D:\X", Newest = 0, Patterns = null! })!.Newest);
        Assert.Equal("", FolderViews.Normalize(new FolderView { Path = @"D:\X", Patterns = null! })!.Patterns);
    }

    [Fact]
    public void Kind_TellsItemsLibraryAndViewFencesApart()
    {
        Assert.Equal(FenceKind.Items, Fence.Create("a").Kind);
        Assert.Equal(FenceKind.Library, Fence.Create("Games", isLibrary: true).Kind);
        Assert.Equal(FenceKind.View, (Fence.Create("v") with { View = new FolderView { Path = @"D:\" } }).Kind);
    }

    [Fact]
    public void CreateView_TitlesTheFenceAfterItsFolder()
    {
        var (config, fence) = FenceEdits.CreateView(NeoFencesConfig.CreateDefault(), new FolderView { Path = @"C:\Users\c\Downloads", Sort = FenceSort.Date });
        Assert.Equal("Downloads", fence.Title);
        Assert.Equal(FenceKind.View, fence.Kind);
        Assert.Contains(config.Fences, candidate => candidate.Id == fence.Id && candidate.View!.Sort == FenceSort.Date);
    }

    [Fact]
    public void SetView_TitleFollowsTheFolder_OnlyWhileItStillHasTheFoldersName()
    {
        var (config, fence) = FenceEdits.CreateView(NeoFencesConfig.CreateDefault(), new FolderView { Path = @"D:\Shots" });
        var moved = FenceEdits.SetView(config, fence.Id, new FolderView { Path = @"D:\Screenshots" });
        Assert.Equal("Screenshots", moved.Fences.Single(candidate => candidate.Id == fence.Id).Title);
        var renamed = FenceEdits.Rename(moved, fence.Id, "My pictures");
        var movedAgain = FenceEdits.SetView(renamed, fence.Id, new FolderView { Path = @"E:\Pics" });
        Assert.Equal("My pictures", movedAgain.Fences.Single(candidate => candidate.Id == fence.Id).Title);
    }

    [Fact]
    public void SetView_RefusesTheLibrary()
    {
        var library = Fence.Create("Games", isLibrary: true);
        var config = NeoFencesConfig.CreateDefault() with { Fences = [library] };
        Assert.Throws<ArgumentException>(() => FenceEdits.SetView(config, library.Id, new FolderView { Path = @"D:\" }));
    }

    [Fact]
    public void Config_KeepsViews_AndTheNormalizerDropsOneOnTheLibrary()
    {
        var (config, fence) = FenceEdits.CreateView(NeoFencesConfig.CreateDefault(),
            new FolderView { Path = @"D:\GameLibrary", Show = ViewShow.Folders, Patterns = "*.lnk", Newest = 20, Sort = FenceSort.Type });
        var json = ConfigJson.Serialize(config);
        Assert.DoesNotContain("\"kind\"", json);
        var back = ConfigNormalizer.Normalize(ConfigJson.Deserialize(json));
        Assert.Equal(fence.View, back.Fences.Single(candidate => candidate.Id == fence.Id).View);

        var both = Fence.Create("Games", isLibrary: true) with { View = new FolderView { Path = @"D:\" } };
        var repaired = ConfigNormalizer.Normalize(NeoFencesConfig.CreateDefault() with { Fences = [both] });
        Assert.Equal(FenceKind.Library, repaired.Fences.Single().Kind);
        Assert.Null(repaired.Fences.Single().View);
    }
}
