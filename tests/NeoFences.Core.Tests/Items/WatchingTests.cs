using NeoFences.Core.Items;

namespace NeoFences.Core.Tests.Items;

/// <summary>Target states, the watch budget and the refresh throttle (M18 spec §4).</summary>
public class WatchingTests
{
    // ---------- TargetChecks.Classify ----------

    private static TargetCheck Classify(string target, string[] files, string[] folders) =>
        TargetChecks.Classify(target,
            fileExists: path => files.Contains(path, StringComparer.OrdinalIgnoreCase),
            folderExists: path => folders.Contains(path, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void Classify_AFileThatExists_IsOk_AFolderSaysSo()
    {
        Assert.Equal(TargetCheck.Ok, Classify(@"D:\Games\a.exe", files: [@"D:\Games\a.exe"], folders: [@"D:\"]));
        Assert.Equal(new TargetCheck(TargetState.Ok, IsFolder: true), Classify(@"D:\Games", files: [], folders: [@"D:\Games", @"D:\"]));
    }

    [Fact]
    public void Classify_Gone_WithItsDriveThere_IsMissing_WithoutItsDrive_IsUnavailable()
    {
        Assert.Equal(TargetState.Missing, Classify(@"D:\Games\a.exe", files: [], folders: [@"D:\"]).State);
        Assert.Equal(TargetState.Unavailable, Classify(@"G:\NeoFences-test\a.txt", files: [], folders: [@"D:\"]).State);
        Assert.Equal(TargetState.Unavailable, Classify(@"\\nas\media\Movies", files: [], folders: []).State);
        Assert.Equal(TargetState.Missing, Classify(@"\\nas\media\Movies", files: [], folders: [@"\\nas\media\"]).State);
    }

    [Fact]
    public void Classify_WebsitesAndSpecialItems_AreAlwaysOk_WithoutAsking()
    {
        Func<string, bool> never = _ => throw new InvalidOperationException("no disk access for these");
        Assert.Equal(TargetCheck.Ok, TargetChecks.Classify("https://github.com", never, never));
        Assert.Equal(TargetCheck.Ok, TargetChecks.Classify("::{645FF040-5081-101B-9F08-00AA002F954E}", never, never));
        Assert.Equal(TargetCheck.Ok, TargetChecks.Classify("steam://rungameid/570", never, never)); // a launcher link: Windows opens it
    }

    [Theory]
    [InlineData(@"D:\Games\a.exe", @"D:\")]
    [InlineData(@"d:\", @"d:\")]
    [InlineData(@"\\nas\media\Movies\x.mkv", @"\\nas\media\")]
    [InlineData(@"\\nas", null)]
    [InlineData("relative.txt", null)]
    public void RootOf(string path, string? expected) => Assert.Equal(expected, TargetChecks.RootOf(path));

    // ---------- WatchPlan ----------

    [Theory]
    [InlineData(@"D:\Games\a.exe", @"D:\Games")]
    [InlineData(@"D:\Games\Hades\", @"D:\Games")]
    [InlineData(@"D:\a.exe", @"D:\")]
    [InlineData(@"D:\", null)]
    [InlineData(@"\\nas\media\x.mkv", @"\\nas\media")]
    [InlineData(@"\\nas\media", null)]
    public void ParentOf(string path, string? expected) => Assert.Equal(expected, WatchPlan.ParentOf(path));

    [Fact]
    public void Folders_TheBusiestFirst_TiesInOrder_AtMostTheBudget()
    {
        string[] targets = [@"C:\Tools\t.exe", @"D:\Games\a.exe", @"D:\Games\b.exe", @"d:\games\c.exe", @"E:\Docs\x.pdf", @"C:\Tools\u.exe", @"F:\"];

        Assert.Equal([@"D:\Games", @"C:\Tools", @"E:\Docs"], WatchPlan.Folders(targets));
        Assert.Equal([@"D:\Games", @"C:\Tools"], WatchPlan.Folders(targets, maxFolders: 2));
    }

    [Fact]
    public void Folders_SeventyFolders_WatchSixtyFour()
    {
        var targets = Enumerable.Range(0, 70).Select(index => $@"D:\Folder{index}\file.txt");
        Assert.Equal(WatchPlan.MaxFolders, WatchPlan.Folders(targets).Count);
    }

    // ---------- RefreshThrottle ----------

    [Fact]
    public void Throttle_OnePerFenceEveryTwoSeconds()
    {
        var throttle = new RefreshThrottle();
        var start = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, throttle.DelayFor("games", start)); // never refreshed: now
        throttle.Refreshed("games", start);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), throttle.DelayFor("games", start.AddMilliseconds(500)));
        Assert.Equal(TimeSpan.Zero, throttle.DelayFor("tools", start.AddMilliseconds(500))); // per fence
        Assert.Equal(TimeSpan.Zero, throttle.DelayFor("games", start.AddSeconds(2)));
    }
}
