using NeoFences.Core.Appearance;
using NeoFences.Core.Items;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Model;

/// <summary>M41b (spec §1–2): what one change in Fence settings does — to this fence, or to All fences — and its undo.</summary>
public class SettingEditsTests
{
    private static readonly Fence Games = Fence.Create("Games");
    private static readonly Fence Work = Fence.Create("Work");
    private static NeoFencesConfig Config => new() { Settings = Settings.NewInstall, Fences = [Games, Work] };

    private static FenceStyle Shown(NeoFencesConfig config, Fence fence) =>
        FenceLook.Resolve(config.Settings.Appearance, config.Fences.Single(candidate => candidate.Id == fence.Id), false, null);

    [Theory]
    [InlineData("fence.theme", FenceTheme.Neon)]
    [InlineData("fence.titleStyle", TitleStyle.Pill)]
    [InlineData("fence.titleAlign", TitleAlign.Right)]
    [InlineData("fence.spacing", Spacing.Roomy)]
    [InlineData("fence.iconSize", 96)]
    [InlineData("fence.labels", LabelMode.OnHover)]
    [InlineData("fence.openWith", OpenWith.DoubleClick)]
    [InlineData("fence.transparency", 55)]
    [InlineData("fence.colourStyle", ColourStyle.TitleStrip)]
    [InlineData("fence.titleBar", false)]
    public void ThisFence_SetsItsOwnValue_AndNullFollowsAgain(string id, object value)
    {
        var setting = SettingEdits.LookSettingOf(id)!.Value;
        var own = SettingEdits.Set(Config, Games.Id, id, value);
        Assert.True(AllFences.HasOwn(own.Fences[0], setting));
        Assert.False(AllFences.HasOwn(own.Fences[1], setting)); // the other fence is untouched
        Assert.Equal(Config.Settings, own.Settings);
        Assert.False(AllFences.HasOwn(SettingEdits.Set(own, Games.Id, id, null).Fences[0], setting));
    }

    [Fact]
    public void AllFences_ChangesEveryFollowingFence_NotOneWithItsOwn()
    {
        var config = SettingEdits.Set(Config, Games.Id, "fence.theme", FenceTheme.Neon);
        config = SettingEdits.Set(config, fenceId: null, "fence.theme", FenceTheme.Hud);
        Assert.Equal(FenceTheme.Hud, config.Settings.Appearance.Theme);
        Assert.Equal((FenceTheme.Neon, FenceTheme.Hud), (Shown(config, Games).Theme, Shown(config, Work).Theme));
    }

    [Fact]
    public void AllFences_Transparency_IsOneValueForBothModes()
    {
        var config = SettingEdits.Set(Config, null, "fence.transparency", 40);
        Assert.Equal((40, 40), (config.Settings.Appearance.StrengthDark, config.Settings.Appearance.StrengthLight));
    }

    [Fact]
    public void AllFences_TitleFont_KeepsTheParts_ThatWereNotChosen()
    {
        var config = SettingEdits.Set(Config, null, "fence.titleFont", new TitleFont(null, 17, null));
        Assert.Equal(new TitleFont("Segoe UI", 17, TitleWeight.SemiBold), config.Settings.Appearance.TitleFont);
    }

    [Fact]
    public void AllFences_BehaviourGoesToSettings()
    {
        var config = SettingEdits.Set(Config, null, "fence.openWith", OpenWith.DoubleClick);
        config = SettingEdits.Set(config, null, "fence.rollUp", RollupExpand.Click);
        config = SettingEdits.Set(config, null, "fence.titleBar", false);
        Assert.Equal((OpenWith.DoubleClick, RollupExpand.Click, true), (config.Settings.OpenItemsWith, config.Settings.RollupExpand, config.Settings.Appearance.TitleOnHover));
    }

    [Fact]
    public void FenceOnlySettings_ChangeTheFence_EvenFromAllFences()
    {
        var config = SettingEdits.Set(Config, Games.Id, "fence.lock", true);
        config = SettingEdits.Set(config, Games.Id, "fence.widgetsFloat", true);
        config = SettingEdits.Set(config, Games.Id, "fence.rollUp", RollupExpand.Click); // always All fences'
        Assert.Equal((true, true), (config.Fences[0].Locked, config.Fences[0].WidgetsFloat));
        Assert.Equal(RollupExpand.Click, config.Settings.RollupExpand);
        Assert.Same(Config.Fences[1], config.Fences[1]);
    }

    [Fact]
    public void AnUnknownIdOrAGoneFence_ChangesNothing()
    {
        var config = Config;
        Assert.Same(config, SettingEdits.Set(config, Games.Id, "fence.nothing", 1));
        Assert.Same(config, SettingEdits.Set(config, "gone", "fence.theme", FenceTheme.Neon));
        Assert.Same(config, SettingEdits.Set(config, null, "fence.theme", null)); // All fences has no "follow"
    }

    [Fact]
    public void UndoTo_BringsBackSettingsAndEveryFencesSettings_KeepsFencesMadeOrGoneSince()
    {
        var before = SettingEdits.Set(Config, Games.Id, "fence.theme", FenceTheme.Neon);
        var changed = SettingEdits.Set(before, null, "fence.iconSize", 96);
        changed = SettingEdits.Set(changed, Games.Id, "fence.theme", FenceTheme.Hud);
        changed = changed with { Fences = [.. changed.Fences.Where(fence => fence.Id != Work.Id), Fence.Create("New")] };
        changed = changed.WithFence(changed.Fences[0] with { Title = "Renamed" });
        var undone = SettingEdits.UndoTo(changed, before);
        Assert.Equal(before.Settings, undone.Settings);
        Assert.Equal(FenceTheme.Neon, undone.Fences[0].Look!.Theme);
        Assert.Equal("Games", undone.Fences[0].Title);
        Assert.Equal(["Games", "New"], undone.Fences.Select(fence => fence.Title)); // Work stays gone, New stays
    }

    [Fact]
    public void UndoTo_LeavesSettingsOtherWindowsOwn_AndTheRecipes()
    {
        var before = SettingEdits.Set(Config, null, "fence.theme", FenceTheme.Neon);
        var changed = SettingEdits.Set(before, null, "fence.theme", FenceTheme.Hud);
        changed = changed with
        {
            Settings = changed.Settings with { HideDesktopIcons = true, StartWithWindows = false, PeekHotkey = "Ctrl+Alt+P" }, // Settings, meanwhile
            Presets = [new Recipe { Name = "Mine" }],
        };
        var undone = SettingEdits.UndoTo(changed, before);
        Assert.Equal(FenceTheme.Neon, undone.Settings.Appearance.Theme); // what Fence settings changed comes back
        Assert.Equal((true, false, "Ctrl+Alt+P"), (undone.Settings.HideDesktopIcons, undone.Settings.StartWithWindows, undone.Settings.PeekHotkey));
        Assert.Single(undone.Presets); // a recipe saved meanwhile stays
    }

    [Fact]
    public void UndoTo_BringsBackOpenWithAndRollUp()
    {
        var before = Config;
        var changed = SettingEdits.Set(SettingEdits.Set(before, null, "fence.openWith", OpenWith.DoubleClick), null, "fence.rollUp", RollupExpand.Click);
        var undone = SettingEdits.UndoTo(changed, before);
        Assert.Equal((before.Settings.OpenItemsWith, before.Settings.RollupExpand), (undone.Settings.OpenItemsWith, undone.Settings.RollupExpand));
    }

    [Fact]
    public void LayoutGoesThroughSet_AsTheFencesOwn()
    {
        var config = SettingEdits.Set(Config, Games.Id, "fence.layout", FenceLayout.Free);
        Assert.Equal(FenceLayout.Free, config.Fences[0].Layout);
    }
}
