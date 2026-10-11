using NeoFences.Core.Appearance;
using NeoFences.Core.Config;
using NeoFences.Core.Items;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Appearance;

/// <summary>M41a (spec 2026-10-11-m41a-feel-design §1–4): themes, title styles, emoji, name styles, motion.</summary>
public class ThemesTests
{
    private static readonly AppearanceSettings Classic = new();
    private static readonly Fence Plain = Fence.Create("Apps");
    private static readonly Argb Red = FenceLook.Swatches[TabColor.Red];

    private static FenceStyle Resolve(AppearanceSettings appearance, Fence fence, bool light = false, Argb? wallpaper = null) =>
        FenceLook.Resolve(appearance, fence, light, wallpaper);

    // ---------- §1 themes ----------

    [Fact]
    public void Defaults_AreTodaysLook_ForExistingSetups()
    {
        Assert.Equal((FenceTheme.Classic, TitleStyle.Simple, NameStyle.Crisp, Motion.Lively),
            (Classic.Theme, Classic.TitleStyle, Classic.NameStyle, Classic.Motion));
        var style = Resolve(Classic, Plain);
        Assert.Equal((FenceTheme.Classic, TitleStyle.Simple), (style.Theme, style.TitleStyle));
    }

    [Fact]
    public void ANewInstall_StartsWithAuroraAndCleanTitles()
    {
        var config = NeoFencesConfig.CreateDefault();
        Assert.Equal((FenceTheme.Aurora, TitleStyle.Clean), (config.Settings.Appearance.Theme, config.Settings.Appearance.TitleStyle));
        Assert.True(config.Settings.Appearance.StrengthDark > 0); // Aurora's glass has a body from the start
        Assert.True(config.Settings.ThemesIntroduced); // no "New: fence themes" note on a fresh start
    }

    [Fact]
    public void ResetSettings_GivesTheNewInstallLook()
    {
        var reset = SettingsReset.Apply(new NeoFencesConfig { Settings = new Settings { Appearance = Classic with { Theme = FenceTheme.Neon } } });
        Assert.Equal(NeoFencesConfig.CreateDefault().Settings.Appearance, reset.Settings.Appearance);
    }

    [Fact]
    public void AFencesOwnTheme_WinsOverAllFences()
    {
        var fence = Plain with { Look = new OwnLook { Theme = FenceTheme.Hud } };
        Assert.Equal(FenceTheme.Hud, Resolve(Classic with { Theme = FenceTheme.Aurora }, fence).Theme);
        Assert.Equal(FenceTheme.Aurora, Resolve(Classic with { Theme = FenceTheme.Aurora }, Plain).Theme);
    }

    [Theory]
    [InlineData(FenceTheme.Aurora, false)]
    [InlineData(FenceTheme.Hud, false)]
    [InlineData(FenceTheme.Neon, false)]
    [InlineData(FenceTheme.LightMica, true)]
    public void ANewTheme_HasItsOwnTone_WhateverWindowsUses(FenceTheme theme, bool lightTone)
    {
        foreach (var windowsLight in new[] { false, true })
            Assert.Equal(lightTone, Resolve(Classic with { Theme = theme }, Plain, light: windowsLight).LightTone);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Classic_FollowsWindows(bool light) => Assert.Equal(light, Resolve(Classic, Plain, light).LightTone);

    [Theory]
    [InlineData(FenceTheme.Aurora)]
    [InlineData(FenceTheme.Hud)]
    [InlineData(FenceTheme.Neon)]
    [InlineData(FenceTheme.LightMica)]
    public void Transparency_IsTheStrengthSlider_InEveryTheme(FenceTheme theme)
    {
        var clear = Resolve(Classic with { Theme = theme }, Plain with { Look = new OwnLook { Strength = 0 } });
        var dense = Resolve(Classic with { Theme = theme }, Plain with { Look = new OwnLook { Strength = 85 } });
        Assert.True(dense.Veil.A > clear.Veil.A + 150, $"{theme}: {clear.Veil.A} → {dense.Veil.A}");
    }

    [Fact]
    public void ANewTheme_KeepsTheFencesColourAsItsAccent()
    {
        var red = Plain with { TabColor = TabColor.Red };
        foreach (var theme in new[] { FenceTheme.Aurora, FenceTheme.Hud, FenceTheme.Neon, FenceTheme.LightMica })
            Assert.Equal(Red, Resolve(Classic with { Theme = theme }, red).Accent);
    }

    [Fact]
    public void WithoutAColour_HudIsCyan_TheOthersTakeWindowsAccent()
    {
        Assert.Equal(new Argb(0xFF, 0x00, 0xE5, 0xFF), Resolve(Classic with { Theme = FenceTheme.Hud }, Plain).Accent);
        Assert.Null(Resolve(Classic with { Theme = FenceTheme.Aurora }, Plain).Accent); // null: the window uses Windows' accent
    }

    [Fact]
    public void Hud_UsesAMonoTitle_UnlessTheFenceChoseAFont()
    {
        Assert.Equal("Cascadia Mono", Resolve(Classic with { Theme = FenceTheme.Hud }, Plain).Font.Family);
        var own = Plain with { Look = new OwnLook { Theme = FenceTheme.Hud, TitleFont = new TitleFont("Bahnschrift", null, null) } };
        Assert.Equal("Bahnschrift", Resolve(Classic, own).Font.Family);
    }

    [Fact]
    public void LightMica_HasDarkInk_TheDarkThemesLightInk()
    {
        Assert.True(Resolve(Classic with { Theme = FenceTheme.LightMica }, Plain).TitleText.Luminance < 0.2);
        Assert.True(Resolve(Classic with { Theme = FenceTheme.Neon }, Plain, light: true).TitleText.Luminance > 0.8);
    }

    // ---------- §2 titles and emoji ----------

    [Fact]
    public void AFencesOwnTitleStyle_WinsOverAllFences()
    {
        Assert.Equal(TitleStyle.Caps, Resolve(Classic with { TitleStyle = TitleStyle.Clean }, Plain with { Look = new OwnLook { TitleStyle = TitleStyle.Caps } }).TitleStyle);
        Assert.Equal(TitleStyle.Clean, Resolve(Classic with { TitleStyle = TitleStyle.Clean }, Plain).TitleStyle);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("🎮", "🎮")]
    [InlineData(" 🎮 ", "🎮")]
    [InlineData("🎮🕹️", "🎮")]
    [InlineData("👨‍👩‍👧", "👨‍👩‍👧")] // one grapheme made of several code points
    [InlineData("A", "A")]
    public void Emoji_IsOneGraphemeOrNone(string? typed, string? kept) => Assert.Equal(kept, Emoji.Normalize(typed));

    [Fact]
    public void FenceSettings_SetsAndClearsTheEmoji()
    {
        var config = new NeoFencesConfig { Fences = [Plain] };
        config = FenceEdits.SetEmoji(config, Plain.Id, "🎮 games");
        Assert.Equal("🎮", config.Fences[0].Emoji);
        Assert.Null(FenceEdits.SetEmoji(config, Plain.Id, " ").Fences[0].Emoji);
    }

    [Fact]
    public void TheTitleCount_LeavesWidgetsOut_AndHidesZero()
    {
        VirtualItem[] items =
        [
            new() { Id = "a", Target = @"C:\Games" }, new() { Id = "b", Target = Widgets.Target(WidgetKind.Clock) },
            new() { Id = "c", Target = "https://example.com" },
        ];
        Assert.Equal("2", Titles.CountText(items.Select(item => item.Target), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("", Titles.CountText([items[1].Target], System.Globalization.CultureInfo.InvariantCulture));
    }

    // ---------- §3 motion ----------

    [Theory]
    [InlineData(Motion.Lively, false, true, false, Motion.Lively)]
    [InlineData(Motion.Calm, false, true, false, Motion.Calm)]
    [InlineData(Motion.Lively, true, true, false, Motion.Off)]   // game mode
    [InlineData(Motion.Lively, false, false, false, Motion.Off)] // Windows' animation effects off
    [InlineData(Motion.Calm, false, true, true, Motion.Off)]     // safe mode
    [InlineData(Motion.Off, false, true, false, Motion.Off)]
    public void Motion_IsOffWhenGamingOrWindowsSaysNo(Motion chosen, bool gameMode, bool windowsAnimations, bool safeMode, Motion effective) =>
        Assert.Equal(effective, MotionPolicy.Effective(chosen, gameMode: gameMode, windowsAnimations: windowsAnimations, safeMode: safeMode));

    // ---------- config ----------

    [Fact]
    public void HandEditedValues_AreRepaired()
    {
        var config = ConfigNormalizer.Normalize(new NeoFencesConfig
        {
            Settings = new Settings { Appearance = Classic with { Theme = (FenceTheme)9, TitleStyle = (TitleStyle)9, NameStyle = (NameStyle)9, Motion = (Motion)9 } },
            Fences = [Plain with { Look = new OwnLook { Theme = (FenceTheme)7, TitleStyle = (TitleStyle)7 }, Emoji = "🎮🎮" }],
        });
        var appearance = config.Settings.Appearance;
        Assert.Equal((FenceTheme.Classic, TitleStyle.Simple, NameStyle.Crisp, Motion.Lively), (appearance.Theme, appearance.TitleStyle, appearance.NameStyle, appearance.Motion));
        Assert.Null(config.Fences[0].Look); // nothing valid left in it
        Assert.Equal("🎮", config.Fences[0].Emoji);
    }

    [Fact]
    public void TheNewValues_SurviveASaveAndLoad()
    {
        var original = new NeoFencesConfig
        {
            Settings = new Settings { Appearance = Classic with { Theme = FenceTheme.Neon, TitleStyle = TitleStyle.Pill, NameStyle = NameStyle.Chips, Motion = Motion.Calm }, ThemesIntroduced = true },
            Fences = [Plain with { Look = new OwnLook { Theme = FenceTheme.Hud, TitleStyle = TitleStyle.Caps }, Emoji = "🕹️" }],
        };
        var restored = ConfigJson.Deserialize(ConfigJson.Serialize(original));
        Assert.Equal(original.Settings.Appearance, restored.Settings.Appearance);
        Assert.True(restored.Settings.ThemesIntroduced);
        Assert.Equal(original.Fences[0].Look, restored.Fences[0].Look);
        Assert.Equal("🕹️", restored.Fences[0].Emoji);
    }

    [Fact]
    public void AnOldConfig_HasNotSeenTheThemesNote()
    {
        var config = ConfigJson.Deserialize("""{ "schemaVersion": 5, "settings": { "gameMode": true }, "fences": [] }""");
        Assert.False(config.Settings.ThemesIntroduced);
        Assert.Equal(FenceTheme.Classic, config.Settings.Appearance.Theme);
    }
}

/// <summary>M41a: a hand-edited typo in a new setting is repaired, never a lost config.json (the M14/M36 rule).</summary>
public class FeelTyposTests
{
    [Fact]
    public void TyposInTheNewSettings_AreRepaired_NotALostFile()
    {
        var json = """
            { "schemaVersion": 5,
              "settings": { "openItemsWith": "tripleClick", "fenceMenu": "fancy",
                            "appearance": { "theme": "matrix", "titleStyle": "gothic", "nameStyle": "huge", "motion": "wild" } },
              "fences": [ { "id": "a", "title": "Apps", "openWith": "sometimes", "look": { "theme": "matrix", "titleStyle": "gothic" } } ] }
            """;
        var config = ConfigNormalizer.Normalize(ConfigJson.Deserialize(json));
        Assert.Equal((OpenWith.OneClick, FenceMenuStyle.Guided), (config.Settings.OpenItemsWith, config.Settings.FenceMenu));
        var appearance = config.Settings.Appearance;
        Assert.Equal((FenceTheme.Classic, TitleStyle.Simple, NameStyle.Crisp, Motion.Lively), (appearance.Theme, appearance.TitleStyle, appearance.NameStyle, appearance.Motion));
        Assert.Null(config.Fences[0].OpenWith);
        Assert.Null(config.Fences[0].Look);
    }
}
