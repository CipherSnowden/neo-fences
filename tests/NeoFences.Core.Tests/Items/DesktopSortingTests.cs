using NeoFences.Core.Items;

namespace NeoFences.Core.Tests.Items;

/// <summary>"Add from desktop…" groups (M19, spec 2026-10-05 §3).</summary>
public class DesktopSortingTests
{
    private static readonly string[] GameFolders = [@"D:\GameLibrary"];

    private static DesktopGroup Group(string itemRef, bool isFolder = false, string? linkTarget = null, string? linkArguments = null) =>
        DesktopSorting.GroupOf(new DesktopEntry(itemRef, isFolder, linkTarget, linkArguments), GameFolders);

    [Theory]
    [InlineData(@"C:\Users\c\Desktop\Dota 2.url", "steam://rungameid/570", null)] // a Steam desktop link
    [InlineData(@"C:\Users\c\Desktop\Fortnite.url", "com.epicgames.launcher://apps/Fortnite?action=launch", null)]
    [InlineData(@"C:\Users\c\Desktop\Hades.lnk", @"C:\Program Files (x86)\Steam\steamapps\common\Hades\Hades.exe", null)]
    [InlineData(@"C:\Users\c\Desktop\Dota.lnk", @"C:\Program Files (x86)\Steam\steam.exe", "-applaunch 570")]
    [InlineData(@"C:\Users\c\Desktop\Crysis 2.lnk", @"D:\GameLibrary\Crysis 2\Bin32\Crysis2.exe", null)] // under a game folder of the user's
    [InlineData(@"C:\Users\c\Desktop\Crysis 3.lnk", @"d:\gamelibrary\Crysis 3\crysis3.exe", null)]
    public void Games_AreWhatTheGameLibraryCountsAsGames(string itemRef, string target, string? arguments) =>
        Assert.Equal(DesktopGroup.Games, Group(itemRef, linkTarget: target, linkArguments: arguments));

    [Theory]
    [InlineData(@"C:\Users\c\Desktop\Discord.lnk", @"C:\Users\c\AppData\Local\Discord\Update.exe")]
    [InlineData(@"C:\Users\Public\Desktop\VLC.lnk", @"C:\Program Files\VideoLAN\VLC\vlc.exe")]
    [InlineData(@"C:\Users\c\Desktop\Spotify.lnk", @"shell:AppsFolder\SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify")]
    [InlineData(@"C:\Users\c\Desktop\WhatsApp.lnk", "")] // a Store app shortcut: no file path
    [InlineData(@"C:\Users\c\Desktop\Backup.lnk", @"C:\Tools\backup.cmd")]
    [InlineData(@"C:\Users\c\Desktop\Settings.url", "ms-settings:display")] // not a website, not a game
    public void Apps_AreOtherProgramShortcuts(string itemRef, string target) =>
        Assert.Equal(DesktopGroup.Apps, Group(itemRef, linkTarget: target));

    [Fact]
    public void AProgramRightOnTheDesktop_IsAnApp() => Assert.Equal(DesktopGroup.Apps, Group(@"C:\Users\c\Desktop\tool.exe"));

    [Theory]
    [InlineData(@"C:\Users\c\Desktop\GitHub.url", "https://github.com/")]
    [InlineData(@"C:\Users\c\Desktop\Docs.url", "http://intranet/docs")]
    public void WebLinks_AreUrlFilesForWebsites(string itemRef, string target) =>
        Assert.Equal(DesktopGroup.WebLinks, Group(itemRef, linkTarget: target));

    [Fact]
    public void FoldersFilesAndWindowsIcons_GoTogether()
    {
        Assert.Equal(DesktopGroup.FoldersAndFiles, Group(@"C:\Users\c\Desktop\Screenshots", isFolder: true));
        Assert.Equal(DesktopGroup.FoldersAndFiles, Group(@"C:\Users\c\Desktop\notes.txt"));
        Assert.Equal(DesktopGroup.FoldersAndFiles, Group("::{645FF040-5081-101B-9F08-00AA002F954E}")); // Recycle Bin
        Assert.Equal(DesktopGroup.FoldersAndFiles, Group(@"C:\Users\c\Desktop\Projects.lnk", linkTarget: @"F:\projects"));
        Assert.Equal(DesktopGroup.FoldersAndFiles, Group(@"C:\Users\c\Desktop\CV.lnk", linkTarget: @"C:\Users\c\Documents\cv.pdf"));
    }

    [Fact]
    public void AShortcutThatCouldNotBeRead_IsAFile() =>
        Assert.Equal(DesktopGroup.FoldersAndFiles, Group(@"C:\Users\c\Desktop\broken.lnk", linkTarget: null));

    [Fact]
    public void AShortcutTheGameLibraryKnows_IsAGame_EvenWithoutALauncher()
    {
        var entry = new DesktopEntry(@"C:\Users\c\Desktop\Blur.lnk", IsFolder: false, LinkTarget: @"C:\Games\Blur\Blur.exe", LinkArguments: null);
        Assert.Equal(DesktopGroup.Apps, DesktopSorting.GroupOf(entry, GameFolders));
        Assert.Equal(DesktopGroup.Games, DesktopSorting.GroupOf(entry, GameFolders, knownGames: new HashSet<string>([@"c:\users\c\desktop\blur.lnk"], StringComparer.OrdinalIgnoreCase)));
        // A game's install folder found by the library's scan counts like a game folder of the user's.
        Assert.Equal(DesktopGroup.Games, DesktopSorting.GroupOf(entry, [@"C:\Games\Blur"]));
    }

    [Fact]
    public void AGameFolderPrefix_MustEndAtASeparator() =>
        Assert.Equal(DesktopGroup.Apps, Group(@"C:\Users\c\Desktop\x.lnk", linkTarget: @"D:\GameLibraryTools\x.exe"));
}
