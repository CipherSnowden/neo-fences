using NeoFences.Core.Items;
using NeoFences.Core.Library;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Items;

/// <summary>M23 (0.12.1): the M21 and M22 review minors.</summary>
public class ReviewMinorsTests
{
    [Theory]
    [InlineData(@"D:\GameLibrary", @"D:\GameLibrary")]
    [InlineData(@"  D:\GameLibrary\  ", @"D:\GameLibrary\")]
    [InlineData(@"\\nas\share\pics", @"\\nas\share\pics")]
    public void FolderPath_AcceptsFullPaths(string text, string expected) => Assert.Equal(expected, FolderViews.FolderPath(text));

    [Fact]
    public void FolderPath_ExpandsVariables()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.Combine(profile, "Downloads"), FolderViews.FolderPath(@"%USERPROFILE%\Downloads"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Downloads")]
    [InlineData(@"..\Downloads")]
    [InlineData(@"C:Downloads")]
    [InlineData(@"\Downloads")]
    [InlineData(@"%NO_SUCH_NEOFENCES_VARIABLE%\x")]
    public void FolderPath_RefusesRelativeOrUnexpandedPaths(string text) => Assert.Null(FolderViews.FolderPath(text));

    [Fact]
    public void Migrate_TurnsEveryLibraryFenceIntoGameItems_AndTheFirstTakesNewGames()
    {
        var first = Fence.Create("Games", isLibrary: true);
        var second = Fence.Create("More games", isLibrary: true);
        var config = NeoFencesConfig.CreateDefault() with { Fences = [first, second] };
        var game = new LibraryItem(new GameEntry("steam:1", "Blur", GameSource.Steam, "steam", new GameLaunch("steam://rungameid/1")), "Blur.url", "sig");
        var migration = GameItems.Migrate(config, new ItemsDocument(), new LibraryState { Items = [game] }, @"C:\lib");
        Assert.Equal([first.Id, second.Id], migration.MigratedFenceIds);
        Assert.All(migration.Config.Fences, fence => Assert.Equal(FenceKind.Items, fence.Kind));
        Assert.All([first.Id, second.Id], id => Assert.Equal(["steam:1"], migration.Items.Of(id).Select(item => item.GameId)));
        Assert.Equal(first.Id, migration.Config.Library.NewGamesFence);
    }
}
