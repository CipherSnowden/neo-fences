using NeoFences.Core.Appearance;
using NeoFences.Core.Config;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Model;

/// <summary>M41b (spec 2026-10-11-m41b-settings-design §1): one place for the look — own values and All fences.</summary>
public class AllFencesTests
{
    private static NeoFencesConfig Two(Fence first, Fence second) => new() { Settings = Settings.NewInstall, Fences = [first, second] };

    [Fact]
    public void ANewFence_FollowsAllFences()
    {
        var (config, fence) = FenceEdits.CreateFence(new NeoFencesConfig { Settings = Settings.NewInstall with { Appearance = AppearanceSettings.NewInstall with { IconSize = 64, Labels = LabelMode.OnHover } } }, "Apps");
        Assert.Null(fence.IconSize);
        Assert.Null(fence.Labels);
        var style = FenceLook.Resolve(config.Settings.Appearance, fence, light: false, wallpaperAccent: null);
        Assert.Equal((64, LabelMode.OnHover), (style.IconSize, style.Labels));
    }

    [Fact]
    public void AnOwnValue_WinsOverAllFences()
    {
        var appearance = new AppearanceSettings { IconSize = 48, Labels = LabelMode.Always, TitleAlign = TitleAlign.Right, Spacing = Spacing.Roomy, TitleOnHover = true };
        var fence = Fence.Create("Games") with { IconSize = 96, Labels = LabelMode.OnHover, Look = new OwnLook { TitleAlign = TitleAlign.Centre } };
        var style = FenceLook.Resolve(appearance, fence, light: false, wallpaperAccent: null);
        Assert.Equal((96, LabelMode.OnHover, TitleAlign.Centre, Spacing.Roomy, true), (style.IconSize, style.Labels, style.Align, style.Spacing, style.TitleOnHover));
    }

    [Fact]
    public void HasOwnAndClearOwn_CoverEverySetting()
    {
        var own = Fence.Create("A") with
        {
            IconSize = 32, Labels = LabelMode.OnHover, OpenWith = OpenWith.DoubleClick,
            Look = new OwnLook { Theme = FenceTheme.Neon, Strength = 40, ColourStyle = ColourStyle.TitleStrip, TitleStyle = TitleStyle.Pill,
                                 TitleFont = new TitleFont("Bahnschrift", null, null), TitleAlign = TitleAlign.Right, TitleOnHover = true, Spacing = Spacing.Compact },
        };
        foreach (var setting in Enum.GetValues<LookSetting>())
        {
            Assert.True(AllFences.HasOwn(own, setting), setting.ToString());
            Assert.False(AllFences.HasOwn(AllFences.ClearOwn(own, setting), setting), setting.ToString());
        }
        var cleared = Enum.GetValues<LookSetting>().Aggregate(own, AllFences.ClearOwn);
        Assert.Null(cleared.Look); // nothing own left: no look at all
        Assert.Equal((null, null, null), (cleared.IconSize, cleared.Labels, cleared.OpenWith));
    }

    [Fact]
    public void MakeFollow_ClearsExactlyTheCountedFences()
    {
        var neon = Fence.Create("Neon") with { Look = new OwnLook { Theme = FenceTheme.Neon, Strength = 20 } };
        var plain = Fence.Create("Plain");
        var config = Two(neon, plain);
        Assert.Equal(1, AllFences.OwnCount(config, LookSetting.Theme));
        var followed = AllFences.MakeFollow(config, LookSetting.Theme);
        Assert.Equal(0, AllFences.OwnCount(followed, LookSetting.Theme));
        Assert.Equal(20, followed.Fences[0].Look!.Strength); // only the theme follows; its own transparency stays
        Assert.Same(plain, followed.Fences[1]);
    }

    [Fact]
    public void UseAllDefaults_DropsEveryOwnValue_KeepsWhatIsOnlyTheFences()
    {
        var fence = Fence.Create("Games") with { IconSize = 96, Labels = LabelMode.OnHover, OpenWith = OpenWith.OneClick, Emoji = "🎮", TabColor = TabColor.Red,
                                                 Layout = NeoFences.Core.Items.FenceLayout.Free, Locked = true, Look = new OwnLook { Theme = FenceTheme.Hud } };
        var after = AllFences.UseAllDefaults(new NeoFencesConfig { Fences = [fence] }, fence.Id).Fences[0];
        Assert.Equal((null, null, null, (OwnLook?)null), (after.IconSize, after.Labels, after.OpenWith, after.Look));
        Assert.Equal(("🎮", TabColor.Red, NeoFences.Core.Items.FenceLayout.Free, true), (after.Emoji, after.TabColor, after.Layout, after.Locked));
    }

    [Fact]
    public void Introduce_MakesEqualValuesFollow_AndTheMostCommonBecomesAllFences()
    {
        var config = new NeoFencesConfig
        {
            Settings = new Settings { DefaultLabels = LabelMode.Always }, // a setup from before rc.4: AllFencesIntroduced false
            Fences =
            [
                Fence.Create("A") with { IconSize = 64, Labels = LabelMode.OnHover },
                Fence.Create("B") with { IconSize = 64, Labels = LabelMode.OnHover },
                Fence.Create("C") with { IconSize = 32, Labels = LabelMode.Always },
            ],
        };
        var introduced = AllFences.Introduce(config);
        Assert.True(introduced.Settings.AllFencesIntroduced);
        Assert.Equal((64, LabelMode.OnHover), (introduced.Settings.Appearance.IconSize, introduced.Settings.Appearance.Labels));
        Assert.Equal(new int?[] { null, null, 32 }, introduced.Fences.Select(fence => fence.IconSize));
        Assert.Equal(new LabelMode?[] { null, null, LabelMode.Always }, introduced.Fences.Select(fence => fence.Labels));
        foreach (var (before, after) in config.Fences.Zip(introduced.Fences)) // nothing changes on screen
        {
            var was = FenceLook.Resolve(config.Settings.Appearance, before, false, null);
            var now = FenceLook.Resolve(introduced.Settings.Appearance, after, false, null);
            Assert.Equal((was.IconSize, was.Labels), (now.IconSize, now.Labels));
        }
        Assert.Same(introduced, AllFences.Introduce(introduced)); // once
    }

    [Fact]
    public void Introduce_OnATie_PrefersTheUsualSize()
    {
        var config = new NeoFencesConfig { Fences = [Fence.Create("A") with { IconSize = 32 }, Fence.Create("B") with { IconSize = 48 }] };
        Assert.Equal(48, AllFences.Introduce(config).Settings.Appearance.IconSize);
    }

    [Fact]
    public void Normalize_RunsTheIntroduction_AndANewInstallNeedsNone()
    {
        Assert.True(NeoFencesConfig.CreateDefault().Settings.AllFencesIntroduced);
        var old = new NeoFencesConfig { Fences = [Fence.Create("A") with { IconSize = 96, Labels = LabelMode.Always }] };
        var normalized = ConfigNormalizer.Normalize(old);
        Assert.True(normalized.Settings.AllFencesIntroduced);
        Assert.Equal(96, normalized.Settings.Appearance.IconSize);
        Assert.Null(normalized.Fences[0].IconSize);
    }

    [Fact]
    public void Normalize_RepairsBadAllFencesValues()
    {
        var bad = new NeoFencesConfig { Settings = Settings.NewInstall with { Appearance = new AppearanceSettings { IconSize = 50, Labels = (LabelMode)9, TitleAlign = (TitleAlign)9, Spacing = (Spacing)9 } },
                                        Fences = [Fence.Create("A") with { IconSize = 50 }] };
        var normalized = ConfigNormalizer.Normalize(bad);
        Assert.Equal((48, LabelMode.Always, TitleAlign.Left, Spacing.Normal), (normalized.Settings.Appearance.IconSize, normalized.Settings.Appearance.Labels, normalized.Settings.Appearance.TitleAlign, normalized.Settings.Appearance.Spacing));
        Assert.Null(normalized.Fences[0].IconSize); // an unsupported own size follows
    }

    [Fact]
    public void RestoringASnapshotFromBeforeRc4_ShowsEveryFenceAsItWas()
    {
        var current = new NeoFencesConfig { Settings = Settings.NewInstall with { Appearance = AppearanceSettings.NewInstall with { IconSize = 64 } } };
        var snapshot = new Snapshot { Name = "rc.3", Fences = [Fence.Create("A") with { IconSize = 48 }, Fence.Create("B") with { IconSize = 48 }, Fence.Create("C") with { IconSize = 32 }] };
        var (restored, _) = Snapshots.Restore(current, snapshot);
        Assert.Equal([48, 48, 32], restored.Fences.Select(fence => FenceLook.Resolve(restored.Settings.Appearance, fence, false, null).IconSize));
        Assert.Equal(64, restored.Settings.Appearance.IconSize); // All fences' stays the user's
    }

    // 1.0.1 (rc.4 review minor): a fences-only snapshot from before rc.4 stored 48 / Always on every fence; where that is
    // what All fences shows anyway, the fence follows again (no "This fence" badges).
    [Fact]
    public void RestoringAFencesOnlySnapshot_ValuesEqualToAllFences_Follow()
    {
        var current = new NeoFencesConfig { Settings = Settings.NewInstall with { Appearance = AppearanceSettings.NewInstall with { IconSize = 48, Labels = LabelMode.Always } } };
        var snapshot = new Snapshot { Name = "Before deleting", Fences = [Fence.Create("A") with { IconSize = 48, Labels = LabelMode.Always }, Fence.Create("B") with { IconSize = 32, Labels = LabelMode.OnHover }] };
        var (restored, _) = Snapshots.Restore(current, snapshot);
        Assert.Equal(((int?)null, (LabelMode?)null), (restored.Fences[0].IconSize, restored.Fences[0].Labels));
        Assert.Equal(((int?)32, (LabelMode?)LabelMode.OnHover), (restored.Fences[1].IconSize, restored.Fences[1].Labels));
    }

    [Fact]
    public void WithStrength_SetsOneValueForBothModes()
    {
        var both = new AppearanceSettings().WithStrength(40);
        Assert.Equal((40, 40), (both.StrengthDark, both.StrengthLight));
        var plain = Fence.Create("A");
        Assert.Equal(FenceLook.Resolve(both, plain, light: true, null).Veil.A, FenceLook.Resolve(both, plain, light: false, null).Veil.A);
    }

    [Fact]
    public void WindowsAccent_FollowsWindows_AndBeatsNothingElse()
    {
        var accent = new Argb(0xFF, 0x12, 0x34, 0x56);
        var fence = Fence.Create("A") with { WindowsAccent = true };
        Assert.Equal(accent, FenceLook.Resolve(new AppearanceSettings(), fence, false, null, windowsAccent: accent).Colour);
        Assert.Null(FenceLook.Resolve(new AppearanceSettings(), fence, false, null, windowsAccent: null).Colour); // unknown accent: neutral
        var custom = FenceEdits.SetColour(new NeoFencesConfig { Fences = [fence] }, fence.Id, swatch: null, customHex: "#FF0000", windowsAccent: false).Fences[0];
        Assert.Equal((false, "#FF0000", (TabColor?)null), (custom.WindowsAccent, custom.CustomColor, custom.TabColor));
        var follows = FenceEdits.SetColour(new NeoFencesConfig { Fences = [custom] }, fence.Id, swatch: null, customHex: null, windowsAccent: true).Fences[0];
        Assert.Equal((true, (string?)null, (TabColor?)null), (follows.WindowsAccent, follows.CustomColor, follows.TabColor));
    }
}
