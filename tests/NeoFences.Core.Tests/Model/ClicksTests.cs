using NeoFences.Core.Config;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Model;

/// <summary>M41a (spec 2026-10-11-m41a-feel-design §4, §5, §7): how items open, the fence menu's shape, the reveal switch.</summary>
public class ClicksTests
{
    [Fact]
    public void Defaults_OneClickOpens_GuidedMenu_RevealOff()
    {
        var settings = new Settings();
        Assert.Equal((OpenWith.OneClick, FenceMenuStyle.Guided, false), (settings.OpenItemsWith, settings.FenceMenu, settings.RevealGesture));
        Assert.Null(Fence.Create("Apps").OpenWith);
    }

    [Theory]
    [InlineData(null, OpenWith.OneClick, true)]
    [InlineData(null, OpenWith.DoubleClick, false)]
    [InlineData(OpenWith.OneClick, OpenWith.DoubleClick, true)]
    [InlineData(OpenWith.DoubleClick, OpenWith.OneClick, false)]
    public void AFencesOwnChoice_WinsOverAllFences(OpenWith? own, OpenWith all, bool oneClick) =>
        Assert.Equal(oneClick, Clicks.OneClickOpens(Fence.Create("Games") with { OpenWith = own }, new Settings { OpenItemsWith = all }));

    [Fact]
    public void AnOldConfig_GetsTheDefaults()
    {
        var config = ConfigJson.Deserialize("""{ "schemaVersion": 5, "settings": { "gameMode": true }, "fences": [ { "id": "a", "title": "Apps" } ] }""");
        Assert.Equal(OpenWith.OneClick, config.Settings.OpenItemsWith);
        Assert.Equal(FenceMenuStyle.Guided, config.Settings.FenceMenu);
        Assert.Null(config.Fences[0].OpenWith);
    }

    [Fact]
    public void HandEditedNumbers_AreRepaired()
    {
        var config = ConfigNormalizer.Normalize(new NeoFencesConfig
        {
            Settings = new Settings { OpenItemsWith = (OpenWith)7, FenceMenu = (FenceMenuStyle)9 },
            Fences = [Fence.Create("Apps") with { OpenWith = (OpenWith)5 }],
        });
        Assert.Equal((OpenWith.OneClick, FenceMenuStyle.Guided), (config.Settings.OpenItemsWith, config.Settings.FenceMenu));
        Assert.Null(config.Fences[0].OpenWith);
    }

    [Fact]
    public void TheChoices_SurviveASaveAndLoad()
    {
        var original = new NeoFencesConfig
        {
            Settings = new Settings { OpenItemsWith = OpenWith.DoubleClick, FenceMenu = FenceMenuStyle.Compact, RevealGesture = true },
            Fences = [Fence.Create("Games") with { OpenWith = OpenWith.OneClick }],
        };
        var restored = ConfigJson.Deserialize(ConfigJson.Serialize(original));
        Assert.Equal((OpenWith.DoubleClick, FenceMenuStyle.Compact, true), (restored.Settings.OpenItemsWith, restored.Settings.FenceMenu, restored.Settings.RevealGesture));
        Assert.Equal(OpenWith.OneClick, restored.Fences[0].OpenWith);
    }

    [Fact]
    public void FenceSettings_SetsAndClearsAFencesOwnChoice()
    {
        var games = Fence.Create("Games");
        var config = FenceEdits.SetOpenWith(new NeoFencesConfig { Fences = [games] }, games.Id, OpenWith.DoubleClick);
        Assert.Equal(OpenWith.DoubleClick, config.Fences[0].OpenWith);
        Assert.Null(FenceEdits.SetOpenWith(config, games.Id, null).Fences[0].OpenWith);
    }
}

/// <summary>Final review I5: "Click wallpaper to reveal" decides from the desktop at click time, not a remembered toggle.</summary>
public class RevealPolicyTests
{
    [Theory]
    [InlineData(true, false, RevealStep.Reveal)]   // windows show: move them aside
    [InlineData(true, true, RevealStep.Reveal)]    // the user brought some back (taskbar, Alt+Tab): aside again, not "undo"
    [InlineData(false, true, RevealStep.Restore)]  // nothing shows since our reveal: bring them back
    [InlineData(false, false, RevealStep.None)]    // nothing shows and we hid nothing: leave the desktop alone
    public void TheStep_FollowsWhatTheDesktopShows(bool windowsShown, bool revealedByUs, RevealStep step) =>
        Assert.Equal(step, RevealPolicy.Next(windowsShown, revealedByUs));
}
