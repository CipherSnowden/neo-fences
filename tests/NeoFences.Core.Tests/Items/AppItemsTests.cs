using NeoFences.Core.Items;

namespace NeoFences.Core.Tests.Items;

/// <summary>Apps as items (M19, spec 2026-10-05 §1): <c>shell:AppsFolder\&lt;id&gt;</c> targets, their check, their place among the checked targets.</summary>
public class AppItemsTests
{
    [Theory]
    [InlineData(@"shell:AppsFolder\SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", true)]
    [InlineData(@"SHELL:appsfolder\308046B0AF4A39CB", true)] // Firefox: a desktop program's entry has no "!"
    [InlineData(@"shell:AppsFolder\", false)] // no app id
    [InlineData(@"shell:AppsFolder", false)] // the folder itself
    [InlineData(@"shell:Downloads", false)]
    [InlineData("::{645FF040-5081-101B-9F08-00AA002F954E}", false)]
    [InlineData(@"D:\Games\a.exe", false)]
    public void IsApp_KnowsAnAppEntry(string target, bool expected) => Assert.Equal(expected, ItemKinds.IsApp(target));

    [Fact]
    public void AppTarget_AndAppIdOf_RoundTrip_AndAnAppIsSpecial()
    {
        var target = ItemKinds.AppTarget("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");
        Assert.Equal(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", target);
        Assert.Equal("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", ItemKinds.AppIdOf(target));
        Assert.Equal(ItemKind.Special, ItemKinds.Of(target));
        Assert.Null(ItemKinds.AppIdOf(@"D:\Games\a.exe"));
        Assert.False(ItemKinds.TakesArguments(ItemKinds.Of(target), isFolder: false)); // Store apps take no arguments
    }

    [Fact]
    public void Classify_AnApp_IsOkWhileWindowsKnowsIt_MissingOnceUninstalled_NeverTouchesTheDisk()
    {
        Func<string, bool> noDisk = _ => throw new InvalidOperationException("an app has no path");
        const string app = @"shell:AppsFolder\Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe!App";
        Assert.Equal(TargetCheck.Ok, TargetChecks.Classify(app, noDisk, noDisk, appExists: _ => true));
        Assert.Equal(new TargetCheck(TargetState.Missing), TargetChecks.Classify(app, noDisk, noDisk, appExists: _ => false));
        Assert.Equal(TargetCheck.Ok, TargetChecks.Classify(app, noDisk, noDisk)); // nobody to ask: as before, Ok
        Assert.Equal(TargetCheck.Ok, TargetChecks.Classify("::{645FF040-5081-101B-9F08-00AA002F954E}", noDisk, noDisk,
            appExists: _ => throw new InvalidOperationException("not an app")));
    }

    [Fact]
    public void CheckedTargets_ArePathsAndApps_OnceEach_WatchedOnesArePathsOnly()
    {
        var document = new ItemsDocument()
            .With("f1", [
                new VirtualItem { Id = "1", Target = @"D:\Games\a.exe" },
                new VirtualItem { Id = "2", Target = @"shell:AppsFolder\Spotify!App" },
                new VirtualItem { Id = "3", Target = "https://github.com" },
                new VirtualItem { Id = "4", Target = "::{645FF040-5081-101B-9F08-00AA002F954E}" },
            ])
            .With("f2", [
                new VirtualItem { Id = "5", Target = @"SHELL:APPSFOLDER\spotify!app" },
                new VirtualItem { Id = "6", Target = @"d:\games\A.EXE" },
            ]);
        Assert.Equal([@"D:\Games\a.exe", @"shell:AppsFolder\Spotify!App"], ItemEdits.CheckedTargets(document));
        Assert.Equal([@"D:\Games\a.exe"], ItemEdits.PathTargets(document));
    }

    [Fact]
    public void Gone_ListsKeysNoLongerLive_IgnoringCase()
    {
        Assert.Equal([@"D:\old.txt"], StaleEntries.Gone([@"D:\a.exe", @"D:\old.txt"], [@"d:\A.EXE", @"D:\new.txt"], ItemKinds.Comparer));
        Assert.Empty(StaleEntries.Gone([], [@"D:\a.exe"], ItemKinds.Comparer));
    }
}
