using NeoFences.Core.Items;

namespace NeoFences.Core.Tests.Items;

/// <summary>Virtual items (M18, spec 2026-10-04-virtual-items-design §1): kinds, typed targets, names.</summary>
public class VirtualItemTests
{
    [Theory]
    [InlineData(@"D:\Games\Hades\Hades.exe", ItemKind.Path)]
    [InlineData(@"C:\Users\cipher\Desktop\notes", ItemKind.Path)]
    [InlineData(@"\\nas\media\Movies", ItemKind.Path)]
    [InlineData("::{645FF040-5081-101B-9F08-00AA002F954E}", ItemKind.Special)]
    [InlineData("shell:AppsFolder", ItemKind.Special)]
    [InlineData("https://github.com/", ItemKind.Website)]
    [InlineData("HTTP://example.com/a?b=c", ItemKind.Website)]
    [InlineData("steam://rungameid/570", ItemKind.Path)] // a launcher link is not a website: it opens through Windows like a file
    [InlineData("ftp://example.com", ItemKind.Path)]
    public void Kind_FollowsTheTarget(string target, ItemKind expected) => Assert.Equal(expected, ItemKinds.Of(target));

    [Theory]
    [InlineData(@"  ""D:\Games\Hades\Hades.exe""  ", @"D:\Games\Hades\Hades.exe")] // Explorer's "Copy as path"
    [InlineData("www.github.com", "https://www.github.com")]
    [InlineData(" https://a.b/ ", "https://a.b/")]
    [InlineData("\"\"", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Clean_MakesATypedTargetUsable(string? typed, string? expected) => Assert.Equal(expected, ItemKinds.Clean(typed));

    [Fact]
    public void Arguments_ApplyToFilesAndApps_Only()
    {
        Assert.True(ItemKinds.TakesArguments(ItemKind.Path, isFolder: false));
        Assert.False(ItemKinds.TakesArguments(ItemKind.Path, isFolder: true));
        Assert.False(ItemKinds.TakesArguments(ItemKind.Website, isFolder: false));
        Assert.False(ItemKinds.TakesArguments(ItemKind.Special, isFolder: false));
    }

    [Theory]
    [InlineData("https://www.github.com/x", "github.com")]
    [InlineData("https://docs.microsoft.com", "docs.microsoft.com")]
    [InlineData("not a url", "not a url")]
    public void WebsiteName_IsTheHost(string url, string expected) => Assert.Equal(expected, ItemKinds.WebsiteName(url));

    [Fact]
    public void OwnName_IsNullWhenBlank_SoWindowsNameShows()
    {
        var item = VirtualItem.Create(@"D:\x.exe");
        Assert.Null(item.OwnName);
        Assert.Null((item with { Name = "  " }).OwnName);
        Assert.Equal("Mine", (item with { Name = "Mine" }).OwnName);
        Assert.NotEqual(item.Id, VirtualItem.Create(@"D:\x.exe").Id); // the same target, two items
    }
}
