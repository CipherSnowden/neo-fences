using NeoFences.Core.Appearance;
using NeoFences.Core.Config;
using NeoFences.Core.Items;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Model;

/// <summary>M36 (spec 2026-10-06-fence-settings-and-presets-design): a fence's own look, presets, reset, whole snapshots.</summary>
public class FenceSettingsTests
{
    private static readonly AppearanceSettings Appearance = new()
    {
        StrengthDark = 30, StrengthLight = 60, ColourStyle = ColourStyle.AccentEdge, TitleFont = new("Segoe UI", 14, TitleWeight.SemiBold),
    };

    private static readonly Fence Red = Fence.Create("Apps") with { TabColor = TabColor.Red };

    private static NeoFencesConfig ConfigWith(params Fence[] fences) => new() { Fences = fences };

    // ---------- §1 the look a fence shows: its own values over Settings' ----------

    [Fact]
    public void NoOwnLook_IsLikeAllFences()
    {
        var style = FenceLook.Resolve(Appearance, Red, light: false, wallpaperAccent: null);
        Assert.Equal(FenceLook.Resolve(Appearance, Red with { Look = new OwnLook() }, light: false, wallpaperAccent: null), style);
        Assert.Equal((TitleAlign.Left, false, Spacing.Normal), (style.Align, style.TitleOnHover, style.Spacing));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnStrength_IsOneValueForBothModes(bool light)
    {
        var fence = Fence.Create("Plain") with { Look = new OwnLook { Strength = 40 } };
        Assert.Equal(0x66, FenceLook.Resolve(Appearance, fence, light, wallpaperAccent: null).Veil.A);
    }

    [Fact]
    public void OwnColourStyle_WinsOverSettings()
    {
        var strip = FenceLook.Resolve(Appearance, Red with { Look = new OwnLook { ColourStyle = ColourStyle.TitleStrip } }, light: false, wallpaperAccent: null);
        Assert.NotNull(strip.TitleStrip);
        Assert.Null(strip.Bar);
    }

    [Fact]
    public void OwnTitleFont_OverridesOnlyItsOwnParts()
    {
        var fence = Red with { Look = new OwnLook { TitleFont = new TitleFont(null, 20, null), TitleAlign = TitleAlign.Centre, TitleOnHover = true, Spacing = Spacing.Roomy } };
        var style = FenceLook.Resolve(Appearance, fence, light: false, wallpaperAccent: null);
        Assert.Equal(new ResolvedTitleFont("Segoe UI", 20, TitleWeight.SemiBold), style.Font);
        Assert.Equal(38, style.TitleHeight);
        Assert.Equal((TitleAlign.Centre, true, Spacing.Roomy), (style.Align, style.TitleOnHover, style.Spacing));
    }

    [Theory]
    [InlineData(Spacing.Compact, 0)]
    [InlineData(Spacing.Normal, 2)]
    [InlineData(Spacing.Roomy, 8)]
    public void Spacing_IsTheCellsInset(Spacing spacing, double inset) => Assert.Equal(inset, FenceLook.CellInset(spacing));

    // ---------- the normalizer and the file ----------

    [Fact]
    public void Normalizer_RepairsOddOwnValues_AndAnEmptyLookIsNone()
    {
        Assert.Null(ConfigNormalizer.NormalizeLook(new OwnLook { TitleFont = new TitleFont(" ", 15, null), TitleOnHover = false })); // nothing usable: like all fences
        var odd = new OwnLook
        {
            ColourStyle = (ColourStyle)7, Strength = 400, TitleFont = new TitleFont("  ", 13, (TitleWeight)9), TitleAlign = (TitleAlign)5,
            TitleOnHover = false, Spacing = (Spacing)(-1),
        };
        var config = ConfigNormalizer.Normalize(ConfigWith(Red with { Look = odd }, Fence.Create("Clean") with { Look = new OwnLook { Strength = -5 } }));
        Assert.Equal(new OwnLook { Strength = AppearanceSettings.MaxStrength }, config.Fences[0].Look); // only the strength was usable
        Assert.Equal(new OwnLook { Strength = 0 }, config.Fences[1].Look);
    }

    [Fact]
    public void OwnLookAndPresets_SurviveTheFile_AndTyposAreRepaired()
    {
        var look = new OwnLook { ColourStyle = ColourStyle.TintedGlass, Strength = 25, TitleFont = new TitleFont("Bahnschrift", null, TitleWeight.Bold), TitleAlign = TitleAlign.Right, TitleOnHover = true, Spacing = Spacing.Compact };
        var config = ConfigWith(Red with { Look = look }) with { Presets = [new LookPreset { Name = "Dark", Look = look, IconSize = 64, Labels = LabelMode.OnHover }] };
        var back = ConfigNormalizer.Normalize(ConfigJson.Deserialize(ConfigJson.Serialize(config)));
        Assert.Equal(look, back.Fences[0].Look);
        Assert.Equal(config.Presets[0], back.Presets[0]);

        var typo = ConfigJson.Serialize(config).Replace("\"compact\"", "\"cosy\"").Replace("\"right\"", "\"middle\"").Replace("\"onHover\"", "\"sometimes\"");
        var repaired = ConfigNormalizer.Normalize(ConfigJson.Deserialize(typo));
        Assert.Null(repaired.Fences[0].Look!.Spacing);
        Assert.Null(repaired.Fences[0].Look!.TitleAlign);
        Assert.Null(repaired.Presets[0].Labels);
    }

    [Fact]
    public void Normalizer_KeepsOwnPresetsWithUsableNamesOnly()
    {
        var config = ConfigNormalizer.Normalize(new NeoFencesConfig
        {
            Presets =
            [
                new LookPreset { Name = "  Mine  ", IconSize = 50, Labels = (LabelMode)4 },
                new LookPreset { Name = "MINE" },          // the same name again
                new LookPreset { Name = "glass" },         // a built-in's name
                new LookPreset { Name = " \t " },          // blank
                null!,
                new LookPreset { Name = new string('x', 80), Look = null! },
            ],
        });
        Assert.Equal(["Mine", new string('x', LookPresets.MaxNameLength)], config.Presets.Select(preset => preset.Name));
        Assert.Equal(new LookPreset { Name = "Mine" }, config.Presets[0]); // odd icon size and labels dropped
        Assert.Equal(new OwnLook(), config.Presets[1].Look);
    }

    // ---------- §2 presets ----------

    [Fact]
    public void BuiltIns_AreTheFiveFromTheSpec()
    {
        Assert.Equal(["Glass", "Minimal", "Title strip", "Solid", "Compact"], LookPresets.BuiltIn.Select(preset => preset.Name));
        var minimal = LookPresets.BuiltIn[1];
        Assert.True(minimal.Look.TitleOnHover);
        Assert.Equal(LabelMode.OnHover, minimal.Labels);
        var compact = LookPresets.BuiltIn[4];
        Assert.Equal((32, LabelMode.OnHover, Spacing.Compact), (compact.IconSize, compact.Labels, compact.Look.Spacing));
        // Every built-in survives the normalizer unchanged (none of them is "like all fences").
        foreach (var preset in LookPresets.BuiltIn) Assert.Equal(preset.Look, ConfigNormalizer.NormalizeLook(preset.Look));
    }

    [Fact]
    public void Apply_CopiesTheLook_KeepsTheColour_AndSetsIconSizeAndLabelsOnlyWhereThePresetHasThem()
    {
        var fence = Red with { IconSize = 64, Labels = LabelMode.Always, Look = new OwnLook { TitleAlign = TitleAlign.Right } };
        var config = LookPresets.Apply(ConfigWith(fence), fence.Id, LookPresets.BuiltIn[4]); // Compact
        var compact = config.Fences[0];
        Assert.Equal((32, LabelMode.OnHover, TabColor.Red), (compact.IconSize, compact.Labels, compact.TabColor));
        Assert.Equal(new OwnLook { Spacing = Spacing.Compact }, compact.Look); // the whole own look is the preset's

        var glass = LookPresets.Apply(config, fence.Id, LookPresets.BuiltIn[0]).Fences[0];
        Assert.Equal(32, glass.IconSize); // Glass has no icon size: the fence keeps its own
        Assert.Equal(LabelMode.Always, glass.Labels);
        Assert.Equal("Glass", LookPresets.Matching(config with { Fences = [glass] }, glass));
    }

    [Fact]
    public void LikeAllFences_DropsTheOwnLook_ButKeepsIconSizeLabelsAndLayout()
    {
        var fence = Red with { IconSize = 96, Labels = LabelMode.OnHover, Layout = FenceLayout.Free, Look = new OwnLook { Strength = 50 } };
        var cleared = LookPresets.LikeAllFences(ConfigWith(fence), fence.Id).Fences[0];
        Assert.Null(cleared.Look);
        Assert.Equal((96, LabelMode.OnHover, FenceLayout.Free), (cleared.IconSize, cleared.Labels, cleared.Layout));
        Assert.Null(LookPresets.Matching(ConfigWith(cleared), cleared));
    }

    [Fact]
    public void SetLook_StoresAnEmptyLookAsNone()
    {
        var config = FenceEdits.SetLook(ConfigWith(Red), Red.Id, new OwnLook { TitleOnHover = false });
        Assert.Null(config.Fences[0].Look);
        Assert.Equal(new OwnLook { Spacing = Spacing.Roomy }, FenceEdits.SetLook(config, Red.Id, new OwnLook { Spacing = Spacing.Roomy }).Fences[0].Look);
    }

    [Fact]
    public void Save_KeepsTheFencesLook_ReplacesAnOwnPresetOfTheSameName_AndRefusesBadNames()
    {
        var fence = Red with { IconSize = 64, Labels = LabelMode.OnHover, Look = new OwnLook { Strength = 70 } };
        var config = LookPresets.Save(ConfigWith(fence), fence.Id, "  My dark look ");
        Assert.Equal(new LookPreset { Name = "My dark look", Look = new OwnLook { Strength = 70 }, IconSize = 64, Labels = LabelMode.OnHover }, config.Presets.Single());
        Assert.Equal("My dark look", LookPresets.Matching(config, fence));

        var changed = config.WithFence(fence with { Look = new OwnLook { Strength = 10 } });
        config = LookPresets.Save(changed, fence.Id, "my DARK look");
        Assert.Equal(10, config.Presets.Single().Look.Strength); // replaced, not added
        Assert.Equal("my DARK look", config.Presets.Single().Name);

        Assert.Equal("Type a name.", LookPresets.NameProblem("   "));
        Assert.Equal("“Solid” is a built-in preset: choose another name.", LookPresets.NameProblem(" solid"));
        Assert.Null(LookPresets.NameProblem("Solid 2"));
        Assert.Same(config, LookPresets.Save(config, fence.Id, "Glass"));
        Assert.Same(config, LookPresets.Save(config, fence.Id, ""));
    }

    [Fact]
    public void Delete_RemovesOnlyAnOwnPreset_AndTheFencesKeepTheirLook()
    {
        var fence = Red with { Look = new OwnLook { Strength = 70 } };
        var config = LookPresets.Save(ConfigWith(fence), fence.Id, "Mine");
        config = LookPresets.Delete(config, "MINE");
        Assert.Empty(config.Presets);
        Assert.Equal(new OwnLook { Strength = 70 }, config.Fences[0].Look); // presets are copies, not links
        Assert.Same(config, LookPresets.Delete(config, "Glass"));
        Assert.Equal(LookPresets.BuiltIn, LookPresets.All(config));
    }

    // ---------- §3 reset and the whole snapshot ----------

    [Fact]
    public void ResetSettings_GivesEveryPageItsDefaults_AndKeepsFencesItemsPresetsAndTheGamesData()
    {
        var fence = Red with { Look = new OwnLook { Strength = 70 } };
        var config = ConfigWith(fence) with
        {
            Settings = new Settings { HideDesktopIcons = true, PeekHotkey = "Ctrl+Q", AutoUpdate = false, Appearance = Appearance with { WallpaperAccent = true } },
            Presets = [new LookPreset { Name = "Mine" }],
            Library = new LibrarySettings
            {
                Folders = ["D:\\Games"], Hidden = ["steam:1"], CoverChoices = new Dictionary<string, string> { ["steam:2"] = "choice.jpg" },
                Sources = new LibrarySources { Steam = false }, OnlineArt = true, NewGamesFence = fence.Id,
            },
        };
        var reset = SettingsReset.Apply(config);
        Assert.Equal(new Settings(), reset.Settings);
        Assert.Equal(config.Fences, reset.Fences);
        Assert.Equal(config.Presets, reset.Presets);
        Assert.Equal((config.Library.Folders, config.Library.Hidden, config.Library.CoverChoices), (reset.Library.Folders, reset.Library.Hidden, reset.Library.CoverChoices));
        Assert.Equal((new LibrarySources(), (bool?)null, (string?)null), (reset.Library.Sources, reset.Library.OnlineArt, reset.Library.NewGamesFence));
    }

    [Fact]
    public void AWholeSnapshot_PutsSettingsGamesAndPresetsBack_AnOrdinaryOneLeavesThem()
    {
        var fence = Red;
        var before = ConfigWith(fence) with
        {
            Settings = new Settings { PeekHotkey = "Ctrl+Q" }, Presets = [new LookPreset { Name = "Mine" }], Library = new LibrarySettings { Folders = ["D:\\Games"] },
        };
        var items = new ItemsDocument().With(fence.Id, [VirtualItem.Create("C:\\tools\\a.exe")]);
        var whole = Snapshots.TakeWhole(before, items, "Before import", DateTimeOffset.UnixEpoch);
        var ordinary = Snapshots.Take(before, items, "Plain", DateTimeOffset.UnixEpoch);
        Assert.Null(ordinary.Settings);

        var imported = new NeoFencesConfig { Fences = [Fence.Create("Other")], Settings = new Settings { PeekHotkey = "Ctrl+W" } };
        var (restored, restoredItems) = Snapshots.Restore(imported, ConfigJson.DeserializeSnapshot(ConfigJson.SerializeSnapshot(whole)));
        Assert.Equal("Ctrl+Q", restored.Settings.PeekHotkey);
        Assert.Equal(["Mine"], restored.Presets.Select(preset => preset.Name));
        Assert.Equal(["D:\\Games"], restored.Library.Folders);
        Assert.Equal([fence.Id], restored.Fences.Select(restoredFence => restoredFence.Id));
        Assert.Single(restoredItems.Of(fence.Id));

        Assert.Equal("Ctrl+W", Snapshots.Restore(imported, ordinary).Config.Settings.PeekHotkey);
    }
}
