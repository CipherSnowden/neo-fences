using NeoFences.Core.Config;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Model;

/// <summary>Rules auto-sort (M11, spec 2026-10-03-rules-design): new items go to the fence a rule names.</summary>
public class RulesTests
{
    private const string Desktop = @"C:\Users\cipher\Desktop\";
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 23, 0, 0, TimeSpan.FromHours(5.5));

    private static ItemFacts File(string name, long size = 1000, double daysOld = 1, string? target = null, bool folder = false) =>
        new(Desktop + name, name, folder ? "" : Path.GetExtension(name), folder, folder ? null : size, Now.AddDays(-daysOld), target);

    private static Rule Rule(RuleCondition condition, Fence fence) => NeoFences.Core.Model.Rule.Create(condition, fence.Id);

    private static (NeoFencesConfig Config, Fence Inbox, Fence Pictures, Fence Games, Fence Bills) Sample()
    {
        var inbox = Fence.Create("Inbox") with { IsInbox = true, Items = [Desktop + "shot.png", Desktop + "notes.txt"] };
        var pictures = Fence.Create("Pictures");
        var games = Fence.Create("Games") with { Items = [Desktop + "old.png"] };
        var bills = Fence.Create("Bills");
        return (new NeoFencesConfig { Fences = [inbox, pictures, games, bills] }, inbox, pictures, games, bills);
    }

    [Theory]
    [InlineData("photo.JPG", TypeGroup.Images, true)]
    [InlineData("clip.mkv", TypeGroup.Videos, true)]
    [InlineData("tax.pdf", TypeGroup.Documents, true)]
    [InlineData("mods.7z", TypeGroup.Archives, true)]
    [InlineData("Game.lnk", TypeGroup.ShortcutsApps, true)]
    [InlineData("setup.exe", TypeGroup.ShortcutsApps, true)]
    [InlineData("photo.jpg", TypeGroup.Documents, false)]
    public void TypeGroups(string name, TypeGroup group, bool matches) =>
        Assert.Equal(matches, Rules.Matches(new RuleCondition { Kind = RuleKind.Type, Group = group }, File(name), Now));

    [Fact]
    public void TypeFolders_AndCustomExtensions()
    {
        Assert.True(Rules.Matches(new RuleCondition { Kind = RuleKind.Type, Group = TypeGroup.Folders }, File("Mods", folder: true), Now));
        Assert.False(Rules.Matches(new RuleCondition { Kind = RuleKind.Type, Group = TypeGroup.Folders }, File("a.txt"), Now));
        var custom = new RuleCondition { Kind = RuleKind.Type, Extensions = ".iso  torrent, .BIN" };
        Assert.True(Rules.Matches(custom, File("ubuntu.iso"), Now));
        Assert.True(Rules.Matches(custom, File("x.torrent"), Now)); // the dot is optional
        Assert.True(Rules.Matches(custom, File("y.bin"), Now));
        Assert.False(Rules.Matches(custom, File("z.zip"), Now));
    }

    [Theory]
    [InlineData("steam://rungameid/1091500", GameLauncher.Steam)]
    [InlineData("com.epicgames.launcher://apps/Fortnite?action=launch", GameLauncher.Epic)]
    [InlineData("uplay://launch/635/0", GameLauncher.Ubisoft)]
    [InlineData("origin2://game/launch?offerIds=1", GameLauncher.Ea)]
    [InlineData("battlenet://WoW", GameLauncher.BattleNet)]
    [InlineData("goggalaxy://openGameView/1207658924", GameLauncher.Gog)]
    [InlineData(@"D:\SteamLibrary\steamapps\common\Hades\Hades.exe", GameLauncher.Steam)]
    [InlineData(@"C:\Program Files\Epic Games\Fortnite\FortniteLauncher.exe", GameLauncher.Epic)]
    [InlineData(@"C:\Program Files (x86)\Steam\steam.exe -applaunch 570", GameLauncher.Steam)]
    [InlineData(@"C:\Program Files (x86)\Battle.net\Battle.net.exe --exec=""launch WoW""", GameLauncher.BattleNet)]
    [InlineData(@"C:\Program Files (x86)\GOG Galaxy\Games\Witcher 3\witcher3.exe", GameLauncher.Gog)]
    public void GameLaunchers(string target, GameLauncher expected) => Assert.Equal(expected, Rules.LauncherOf(target));

    [Fact]
    public void GameRules_AnyOrOne()
    {
        var any = new RuleCondition { Kind = RuleKind.Game, Launcher = GameLauncher.Any };
        var steamOnly = new RuleCondition { Kind = RuleKind.Game, Launcher = GameLauncher.Steam };
        var cyberpunk = File("Cyberpunk 2077.url", target: "steam://rungameid/1091500");
        var fortnite = File("Fortnite.url", target: "com.epicgames.launcher://apps/Fortnite");
        Assert.True(Rules.Matches(any, cyberpunk, Now));
        Assert.True(Rules.Matches(any, fortnite, Now));
        Assert.True(Rules.Matches(steamOnly, cyberpunk, Now));
        Assert.False(Rules.Matches(steamOnly, fortnite, Now));
        Assert.False(Rules.Matches(any, File("Notepad.lnk", target: @"C:\Windows\notepad.exe"), Now));
        Assert.Null(Rules.LauncherOf(null));
        // The launchers themselves are not games (probe on the user's desktop, 2026-10-03)
        Assert.Null(Rules.LauncherOf(@"C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe"));
        Assert.Null(Rules.LauncherOf(@"C:\Program Files (x86)\Steam\Steam.exe"));
        Assert.Null(Rules.LauncherOf(@"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\UbisoftConnect.exe"));
    }

    [Fact]
    public void GameRules_InAFolderOfMine()
    {
        // The user's own games live in D:\GameLibrary, outside any launcher (probe 2026-10-03; user choice: add a game folder).
        var library = new RuleCondition { Kind = RuleKind.Game, Launcher = GameLauncher.Folder, Folder = @"D:\GameLibrary" };
        Assert.True(Rules.Matches(library, File("Blur.lnk", target: @"D:\GameLibrary\Blur\Blur.exe"), Now));
        Assert.True(Rules.Matches(library, File("Blur.lnk", target: @"d:\gamelibrary\Blur\Blur.exe"), Now));
        Assert.True(Rules.Matches(library with { Folder = @"D:\GameLibrary\" }, File("Blur.lnk", target: @"D:\GameLibrary\Blur\Blur.exe"), Now));
        Assert.False(Rules.Matches(library, File("Old.lnk", target: @"D:\GameLibraryOld\Old.exe"), Now)); // a folder, not a prefix
        Assert.False(Rules.Matches(library, File("notes.txt"), Now));
        Assert.False(Rules.Matches(new RuleCondition { Kind = RuleKind.Game, Launcher = GameLauncher.Any }, File("Blur.lnk", target: @"D:\GameLibrary\Blur\Blur.exe"), Now));
        var (config, _, _, games, _) = Sample();
        Assert.Equal(@"Game shortcuts in D:\GameLibrary → Games", Rules.Describe(Rule(library, games), config));
        var repaired = ConfigNormalizer.Normalize(config with { Rules = [Rule(library with { Folder = " " }, games), Rule(library, games)] });
        Assert.Equal([false, true], repaired.Rules.Select(rule => rule.Enabled)); // no folder: disabled
        Assert.Contains("\"launcher\": \"folder\"", ConfigJson.Serialize(repaired));
    }

    [Theory]
    [InlineData("invoice*", "Invoice-1.pdf", true)]
    [InlineData("invoice*", "my invoice.pdf", false)]
    [InlineData("*screenshot*", "2026 Screenshot 1.png", true)]
    [InlineData("report", "Q3 REPORT final.docx", true)] // plain text: contains
    [InlineData("v?.zip", "v2.zip", true)]
    [InlineData("v?.zip", "v10.zip", false)]
    public void NamePatterns(string pattern, string name, bool matches) =>
        Assert.Equal(matches, Rules.Matches(new RuleCondition { Kind = RuleKind.Name, Pattern = pattern }, File(name), Now));

    [Fact]
    public void AgeAndSize()
    {
        var older30 = new RuleCondition { Kind = RuleKind.Age, Compare = RuleCompare.OlderThan, Days = 30 };
        var newer2 = new RuleCondition { Kind = RuleKind.Age, Compare = RuleCompare.NewerThan, Days = 2 };
        var bigger100 = new RuleCondition { Kind = RuleKind.Size, Compare = RuleCompare.BiggerThan, Megabytes = 100 };
        var smaller1 = new RuleCondition { Kind = RuleKind.Size, Compare = RuleCompare.SmallerThan, Megabytes = 1 };
        Assert.True(Rules.Matches(older30, File("a.txt", daysOld: 45), Now));
        Assert.False(Rules.Matches(older30, File("a.txt", daysOld: 3), Now));
        Assert.True(Rules.Matches(newer2, File("a.txt", daysOld: 1), Now));
        Assert.True(Rules.Matches(bigger100, File("movie.mkv", size: 200L * 1024 * 1024), Now));
        Assert.False(Rules.Matches(bigger100, File("Mods", folder: true), Now)); // folders never match size
        Assert.True(Rules.Matches(smaller1, File("tiny.txt", size: 10), Now));
        Assert.False(Rules.Matches(older30, new ItemFacts(Desktop + "x", "x", "", false, null, null, null), Now)); // unknown date
    }

    [Fact]
    public void Match_FirstEnabledRuleWithAnExistingDesktopFenceWins()
    {
        var (config, _, pictures, games, bills) = Sample();
        var portal = Fence.Create("Downloads", FenceSource.Portal(@"C:\Downloads"));
        config = config with
        {
            Fences = [.. config.Fences, portal],
            Rules =
            [
                Rule(new RuleCondition { Kind = RuleKind.Type, Group = TypeGroup.Images }, games) with { Enabled = false },
                Rule(new RuleCondition { Kind = RuleKind.Type, Group = TypeGroup.Images }, portal), // Portals are never targets
                NeoFences.Core.Model.Rule.Create(new RuleCondition { Kind = RuleKind.Type, Group = TypeGroup.Images }, "gone"), // fence deleted
                Rule(new RuleCondition { Kind = RuleKind.Type, Group = TypeGroup.Images }, pictures),
                Rule(new RuleCondition { Kind = RuleKind.Name, Pattern = "*" }, bills),
            ],
        };

        Assert.Equal(pictures.Id, Rules.Match(File("shot.png"), config, Now));
        Assert.Equal(bills.Id, Rules.Match(File("notes.txt"), config, Now));
    }

    [Fact]
    public void File_MovesMatchedItemsToTheEndOfTheirFence_AndLeavesTheRest()
    {
        var (config, inbox, pictures, games, _) = Sample();
        config = config with { Rules = [Rule(new RuleCondition { Kind = RuleKind.Type, Group = TypeGroup.Images }, pictures)] };
        config = config.WithFence(pictures with { Items = [Desktop + "first.png"] });

        var filed = Rules.File(config, [File("shot.png"), File("notes.txt"), File("old.png")], Now);

        // old.png sat in Games by hand; File moves every matched item it is given (Apply rules now gives all of them).
        Assert.Equal([Desktop + "first.png", Desktop + "shot.png", Desktop + "old.png"], filed.Fences.Single(fence => fence.Id == pictures.Id).Items);
        Assert.Equal([Desktop + "notes.txt"], filed.Inbox.Items);
        Assert.Empty(filed.Fences.Single(fence => fence.Id == games.Id).Items);
        Assert.Same(config, Rules.File(config, [File("notes.txt")], Now)); // nothing to file: the same config
        Assert.Equal(1, Rules.CountMoves(config, [File("shot.png"), File("first.png")], Now)); // first.png is there already
    }

    [Fact]
    public void Describe_ReadsLikeASentence()
    {
        var (config, _, pictures, games, _) = Sample();
        Assert.Equal("Images → Pictures", Rules.Describe(Rule(new RuleCondition { Kind = RuleKind.Type, Group = TypeGroup.Images }, pictures), config));
        Assert.Equal("Game shortcuts (any launcher) → Games", Rules.Describe(Rule(new RuleCondition { Kind = RuleKind.Game, Launcher = GameLauncher.Any }, games), config));
        Assert.Equal("Name like \"invoice*\" → Pictures", Rules.Describe(Rule(new RuleCondition { Kind = RuleKind.Name, Pattern = "invoice*" }, pictures), config));
        Assert.Equal("Older than 30 days → Games", Rules.Describe(Rule(new RuleCondition { Kind = RuleKind.Age, Compare = RuleCompare.OlderThan, Days = 30 }, games), config));
        Assert.Equal("Bigger than 100 MB → (fence missing)", Rules.Describe(NeoFences.Core.Model.Rule.Create(new RuleCondition { Kind = RuleKind.Size, Compare = RuleCompare.BiggerThan, Megabytes = 100 }, "gone"), config));
    }

    [Fact]
    public void Normalizer_DisablesBrokenRules_AndRulesRoundTrip()
    {
        var (config, _, pictures, _, _) = Sample();
        config = config with
        {
            Rules =
            [
                Rule(new RuleCondition { Kind = RuleKind.Name, Pattern = "  " }, pictures),                     // empty pattern
                Rule(new RuleCondition { Kind = RuleKind.Age, Compare = RuleCompare.OlderThan, Days = -3 }, pictures), // negative
                Rule(new RuleCondition { Kind = RuleKind.Type }, pictures),                                       // no group, no extensions
                Rule(new RuleCondition { Kind = (RuleKind)42 }, pictures),                                         // unknown kind
                Rule(new RuleCondition { Kind = RuleKind.Type, Group = TypeGroup.Images }, pictures),              // fine
                null!,
            ],
        };

        var normalized = ConfigNormalizer.Normalize(config);

        Assert.Equal(5, normalized.Rules.Count);
        Assert.Equal([false, false, false, false, true], normalized.Rules.Select(rule => rule.Enabled));
        var json = ConfigJson.Serialize(normalized);
        Assert.Contains("\"kind\": \"type\"", json);
        Assert.Equal(normalized.Rules[4].Condition, ConfigJson.Deserialize(json).Rules[4].Condition);
        Assert.Empty(ConfigNormalizer.Normalize(ConfigJson.Deserialize("""{ "schemaVersion": 1, "fences": [] }""")).Rules);
    }

    [Fact]
    public void AgeRule_WithHugeDays_NeverThrows()
    {
        // final review C1: "99999999 days" meant "never"; a TimeSpan overflow would crash on every new item
        var huge = new RuleCondition { Kind = RuleKind.Age, Compare = RuleCompare.OlderThan, Days = 99_999_999 };
        Assert.False(Rules.Matches(huge, File("a.txt", daysOld: 45), Now));
        Assert.True(Rules.Matches(huge with { Compare = RuleCompare.NewerThan }, File("a.txt", daysOld: 45), Now));
    }

    [Theory]
    [InlineData("photo.png.crdownload", true)]   // Chrome / Edge finishing a download
    [InlineData("Unconfirmed 123.crdownload", true)]
    [InlineData("clip.mkv.part", true)]          // Firefox
    [InlineData("report.tmp", true)]             // save-then-rename
    [InlineData("notes.txt", false)]             // the user renaming an item: it stays put
    public void DownloadRenames_AreNewItems(string oldName, bool isNew) =>
        Assert.Equal(isNew, Rules.IsDownloadRename(Desktop + oldName)); // final review I2

    [Fact]
    public void HandEditedRules_WithUnknownNamesOrMissingIds_AreDisabled_NotACorruptConfig()
    {
        // final review I3: an unknown enum name or a missing id/fenceId must not make config.json "corrupt"
        var config = ConfigNormalizer.Normalize(ConfigJson.Deserialize("""
            { "schemaVersion": 1, "fences": [],
              "rules": [
                { "id": "a", "condition": { "kind": "colour" }, "fenceId": "f" },
                { "id": "b", "condition": { "kind": "type", "group": "music" }, "fenceId": "f" },
                { "condition": { "kind": "game", "launcher": "origin" } },
                { "id": "d", "condition": { "kind": "age", "compare": "longAgo", "days": 3 }, "fenceId": "f" },
                { "id": "e", "condition": { "kind": "type", "group": "shortcutsApps" }, "fenceId": "f" }
              ] }
            """));

        Assert.Equal([false, false, false, false, true], config.Rules.Select(rule => rule.Enabled));
        Assert.False(string.IsNullOrEmpty(config.Rules[2].Id));
        Assert.Equal(TypeGroup.ShortcutsApps, config.Rules[4].Condition.Group);
    }
}
