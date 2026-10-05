using NeoFences.Core.Config;
using NeoFences.Core.Items;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Items;

/// <summary>M27 (0.16.0): auto-collect rules — new files in a watched folder become items (spec 2026-10-05-auto-collect-design).</summary>
public class CollectRulesTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static ItemInfo File(string name, string folder = @"C:\Users\me\Desktop", int minutesAgo = 0) =>
        new($@"{folder}\{name}", name, IsFolder: false, TypeName: Path.GetExtension(name).ToUpperInvariant(), Day.AddMinutes(-minutesAgo), Created: Day.AddMinutes(-minutesAgo));

    private static ItemInfo Folder(string name, string folder = @"C:\Users\me\Desktop") =>
        new($@"{folder}\{name}", name, IsFolder: true, TypeName: "", Day, Created: Day);

    private static CollectRule Rule(CollectKinds kinds, string patterns = "", string source = CollectRules.DesktopSource) =>
        new() { Source = source, Kinds = kinds, Patterns = patterns };

    [Theory]
    [InlineData("Steam.lnk", CollectKinds.Apps)]
    [InlineData("site.URL", CollectKinds.Apps)]
    [InlineData("game.exe", CollectKinds.Apps)]
    [InlineData("SteamSetup.exe", CollectKinds.Installers)]
    [InlineData("discord_installer.EXE", CollectKinds.Installers)]
    [InlineData("tool-Install.exe", CollectKinds.Installers)]
    [InlineData("app.msi", CollectKinds.Installers)]
    [InlineData("app.msixbundle", CollectKinds.Installers)]
    [InlineData("resume.pdf", CollectKinds.Documents)]
    [InlineData("notes.MD", CollectKinds.Documents)]
    [InlineData("shot.png", CollectKinds.Pictures)]
    [InlineData("photo.JPEG", CollectKinds.Pictures)]
    [InlineData("mods.7z", CollectKinds.Archives)]
    [InlineData("backup.tar.gz", CollectKinds.Archives)]
    [InlineData("save.dat", CollectKinds.None)]
    [InlineData("download.crdownload", CollectKinds.None)]
    public void KindOf_ByExtension_InstallersBeforeApps(string name, CollectKinds expected) =>
        Assert.Equal(expected, CollectRules.KindOf(name, isFolder: false));

    [Fact]
    public void Matches_TheRulesKinds_ItsPatterns_AndFoldersOnlyByAnythingOrAPattern()
    {
        Assert.True(CollectRules.Matches(Rule(CollectKinds.Apps), "Steam.lnk", isFolder: false));
        Assert.False(CollectRules.Matches(Rule(CollectKinds.Apps), "SteamSetup.exe", isFolder: false)); // an installer is not an app
        Assert.True(CollectRules.Matches(Rule(CollectKinds.Apps | CollectKinds.Installers), "SteamSetup.exe", isFolder: false));
        Assert.True(CollectRules.Matches(Rule(CollectKinds.None, patterns: "*.dat; iso"), "save.dat", isFolder: false));
        Assert.True(CollectRules.Matches(Rule(CollectKinds.None, patterns: "iso"), "ubuntu.ISO", isFolder: false));
        Assert.False(CollectRules.Matches(Rule(CollectKinds.None), "Steam.lnk", isFolder: false)); // nothing chosen: nothing collected
        Assert.True(CollectRules.Matches(Rule(CollectKinds.Anything), "save.dat", isFolder: false));
        Assert.True(CollectRules.Matches(Rule(CollectKinds.Anything), "Mods", isFolder: true));
        Assert.False(CollectRules.Matches(Rule(CollectKinds.Apps | CollectKinds.Documents), "Mods.lnk", isFolder: true)); // a folder named like a file
        Assert.True(CollectRules.Matches(Rule(CollectKinds.None, patterns: "Project*"), "Project X", isFolder: true));
    }

    [Fact]
    public void Plan_FirstMatchingRuleInFenceOrder_Wins_AndNothingAnyFenceHolds()
    {
        var setup = Fence.Create("Setup") with { Collect = [Rule(CollectKinds.Installers)] };
        var apps = Fence.Create("Apps") with { Collect = [Rule(CollectKinds.Apps | CollectKinds.Installers)] };
        var games = Fence.Create("Games");
        var config = NeoFencesConfig.CreateDefault() with { Fences = [setup, apps, games] };
        var held = VirtualItem.Create(@"C:\Users\me\Desktop\Old.lnk");
        var items = new ItemsDocument().With(games.Id, [held]); // moved by the user to Games: it stays there

        var plan = CollectRules.Plan(config, items, CollectRules.DesktopSource,
            [File("Steam.lnk"), File("SteamSetup.exe"), File("Old.lnk"), File("save.dat")]);

        Assert.Equal([(apps.Id, @"C:\Users\me\Desktop\Steam.lnk"), (setup.Id, @"C:\Users\me\Desktop\SteamSetup.exe")],
            plan.Select(entry => (entry.FenceId, entry.Path)));
        Assert.Equal(apps.Collect[0].Id, plan[0].RuleId);
    }

    [Fact]
    public void Plan_OnlyRulesOfThatSource_AndOnlyItemsFences()
    {
        var downloads = Fence.Create("Downloads rule") with { Collect = [Rule(CollectKinds.Pictures, source: @"D:\Downloads")] };
        var view = Fence.Create("Old view") with { View = new FolderView { Path = @"D:\x" }, Collect = [Rule(CollectKinds.Pictures)] };
        var config = NeoFencesConfig.CreateDefault() with { Fences = [view, downloads] };
        Assert.Empty(CollectRules.Plan(config, new ItemsDocument(), CollectRules.DesktopSource, [File("shot.png")]));
        var fromDownloads = CollectRules.Plan(config, new ItemsDocument(), @"d:\downloads\", [File("shot.png", folder: @"D:\Downloads")]);
        Assert.Equal(downloads.Id, Assert.Single(fromDownloads).FenceId);
    }

    [Fact]
    public void Plan_ABurst_IsCappedPerRule()
    {
        var apps = Fence.Create("Apps") with { Collect = [Rule(CollectKinds.Anything)] };
        var config = NeoFencesConfig.CreateDefault() with { Fences = [apps] };
        var burst = Enumerable.Range(0, CollectRules.MaxPerBurst + 50).Select(index => File($"f{index}.txt")).ToList();
        Assert.Equal(CollectRules.MaxPerBurst, CollectRules.Plan(config, new ItemsDocument(), CollectRules.DesktopSource, burst).Count);
    }

    [Fact]
    public void Plan_TheSameFileTwiceInOneBurst_OnlyOnce()
    {
        var apps = Fence.Create("Apps") with { Collect = [Rule(CollectKinds.Apps)] };
        var config = NeoFencesConfig.CreateDefault() with { Fences = [apps] };
        Assert.Single(CollectRules.Plan(config, new ItemsDocument(), CollectRules.DesktopSource, [File("Steam.lnk"), File("STEAM.LNK")]));
    }

    [Fact]
    public void Arrivals_AreTheNewEntries_OrByCreationTimeWhenThereWasNoEarlierListing()
    {
        IReadOnlyList<ItemInfo> before = [File("a.lnk", minutesAgo: 90), File("b.lnk", minutesAgo: 60)];
        IReadOnlyList<ItemInfo> now = [File("a.lnk", minutesAgo: 90), File("c.lnk", minutesAgo: 1), File("d.lnk", minutesAgo: 120)]; // d moved in: old date
        Assert.Equal(["c.lnk", "d.lnk"], CollectRules.Arrivals(before, now, watermark: Day.AddMinutes(-30)).Select(entry => entry.Name));
        // First listing (start, a pendrive back): only what was created after the rule last looked.
        Assert.Equal(["c.lnk"], CollectRules.Arrivals(null, now, watermark: Day.AddMinutes(-30)).Select(entry => entry.Name));
        Assert.Empty(CollectRules.Arrivals(null, now, watermark: null)); // never looked: nothing counts as new
    }

    [Fact]
    public void Normalize_RepairsHandEditedRules()
    {
        var repaired = CollectRules.Normalize([
            new CollectRule { Id = "", Source = "DESKTOP", Kinds = (CollectKinds)(1 | 128), Patterns = @"C:\bad" },
            new CollectRule { Id = "same", Source = "   ", Kinds = CollectKinds.Apps },
            new CollectRule { Id = "same", Source = "relative\\path", Kinds = CollectKinds.Apps },
            new CollectRule { Id = "same", Source = @"D:\Downloads", Kinds = CollectKinds.Pictures },
            new CollectRule { Id = "same", Source = @"D:\Other", Kinds = CollectKinds.Pictures },
        ]);
        Assert.Equal(3, repaired.Count); // blank and relative sources go
        Assert.Equal((CollectRules.DesktopSource, CollectKinds.Apps, ""), (repaired[0].Source, repaired[0].Kinds, repaired[0].Patterns));
        Assert.NotEmpty(repaired[0].Id);
        Assert.Equal(3, repaired.Select(rule => rule.Id).Distinct().Count()); // a copied id gets a new one
        Assert.Empty(CollectRules.Normalize(null));
    }

    [Fact]
    public void Config_RoundTrip_WritesCollectOnlyWhenSet_AndNormalizes()
    {
        var plain = Fence.Create("Plain");
        var apps = Fence.Create("Apps") with { Collect = [Rule(CollectKinds.Apps | CollectKinds.Installers, patterns: "*.iso") with { Watermark = Day }] };
        var json = ConfigJson.Serialize(NeoFencesConfig.CreateDefault() with { Fences = [plain, apps] });
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"collect\""));
        var loaded = ConfigNormalizer.Normalize(ConfigJson.Deserialize(json));
        Assert.Empty(loaded.Fences[0].Collect);
        Assert.Equal(apps.Collect, loaded.Fences[1].Collect);
    }

    [Fact]
    public void Restore_StartsEveryRuleLookingAtTheRestoreTime_SoOldFilesAreNotCollectedAgain()
    {
        var apps = Fence.Create("Apps") with { Collect = [Rule(CollectKinds.Apps) with { Watermark = Day.AddDays(-7) }] };
        var snapshot = Snapshots.Take(NeoFencesConfig.CreateDefault() with { Fences = [apps] }, new ItemsDocument(), name: "Last week", now: Day.AddDays(-7));
        var (config, _) = Snapshots.Restore(NeoFencesConfig.CreateDefault(), snapshot, restoredAt: Day);
        Assert.Equal(Day, Assert.Single(config.Fences[0].Collect).Watermark);
    }

    [Fact]
    public void SetCollect_ReplacesAFencesRules()
    {
        var apps = Fence.Create("Apps");
        var config = NeoFencesConfig.CreateDefault() with { Fences = [apps] };
        var rules = new[] { Rule(CollectKinds.Apps) };
        Assert.Equal(rules, FenceEdits.SetCollect(config, apps.Id, rules).Fences[0].Collect);
        Assert.Throws<ArgumentException>(() => FenceEdits.SetCollect(config, "nope", rules));
    }

    [Fact]
    public void Summary_SaysSourceAndKinds()
    {
        Assert.Equal("Desktop · Apps and shortcuts, Installers", CollectRules.Summary(Rule(CollectKinds.Apps | CollectKinds.Installers)));
        Assert.Equal("Downloads · Pictures, *.iso", CollectRules.Summary(Rule(CollectKinds.Pictures, patterns: "*.iso", source: @"D:\Downloads")));
        Assert.Equal("Desktop · nothing chosen", CollectRules.Summary(Rule(CollectKinds.None)));
    }
}
