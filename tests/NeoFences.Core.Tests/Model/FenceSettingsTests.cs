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
        var config = ConfigWith(Red with { Look = look }) with { Presets = [new Recipe { Name = "Dark", Look = look, IconSize = 64, Labels = LabelMode.OnHover }] };
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
    public void Normalizer_KeepsOwnRecipesWithUsableNamesOnly()
    {
        var config = ConfigNormalizer.Normalize(new NeoFencesConfig
        {
            Presets =
            [
                new Recipe { Name = "  Mine  ", IconSize = 50, Labels = (LabelMode)4 },
                new Recipe { Name = "MINE" },          // the same name again
                new Recipe { Name = "game shelf" },    // a built-in's name (M41b)
                new Recipe { Name = " \t " },          // blank
                null!,
                new Recipe { Name = new string('x', 80), Look = null! },
            ],
        });
        Assert.Equal(["Mine", new string('x', Recipes.MaxNameLength)], config.Presets.Select(recipe => recipe.Name));
        Assert.Equal(new Recipe { Name = "Mine" }, config.Presets[0]); // odd icon size and labels dropped
        Assert.Equal(new OwnLook(), config.Presets[1].Look);
    }

    // ---------- §2 presets: recipes since M41b (RecipesTests) ----------

    [Fact]
    public void SetLook_StoresAnEmptyLookAsNone()
    {
        var config = FenceEdits.SetLook(ConfigWith(Red), Red.Id, new OwnLook { TitleOnHover = false });
        Assert.Null(config.Fences[0].Look);
        Assert.Equal(new OwnLook { Spacing = Spacing.Roomy }, FenceEdits.SetLook(config, Red.Id, new OwnLook { Spacing = Spacing.Roomy }).Fences[0].Look);
    }

    [Fact]
    public void Save_ReplacesAnOwnRecipeOfTheSameName()
    {
        var fence = Red with { IconSize = 64, Labels = LabelMode.OnHover, Look = new OwnLook { Strength = 70 } };
        var config = Recipes.Save(ConfigWith(fence), fence.Id, "  My dark look ", emoji: null);
        Assert.Equal("My dark look", Recipes.Matching(config, fence));
        var changed = config.WithFence(fence with { Look = new OwnLook { Strength = 10 } });
        config = Recipes.Save(changed, fence.Id, "my DARK look", emoji: null);
        Assert.Equal(10, config.Presets.Single().Look.Strength); // replaced, not added
        Assert.Equal("my DARK look", config.Presets.Single().Name);
        Assert.Same(config, Recipes.Save(config, fence.Id, "Minimal", emoji: null));
        Assert.Same(config, Recipes.Save(config, fence.Id, "", emoji: null));
    }

    [Fact]
    public void Delete_RemovesOnlyAnOwnRecipe_AndTheFencesKeepTheirLook()
    {
        var fence = Red with { Look = new OwnLook { Strength = 70 } };
        var config = Recipes.Save(ConfigWith(fence), fence.Id, "Mine", emoji: null);
        config = Recipes.Delete(config, "MINE");
        Assert.Empty(config.Presets);
        Assert.Equal(new OwnLook { Strength = 70 }, config.Fences[0].Look); // recipes are copies, not links
        Assert.Same(config, Recipes.Delete(config, "Game shelf"));
        Assert.Equal(Recipes.BuiltIn, Recipes.All(config));
    }

    // ---------- §3 reset and the whole snapshot ----------

    [Fact]
    public void ResetSettings_GivesEveryPageItsDefaults_AndKeepsFencesItemsPresetsAndTheGamesData()
    {
        var fence = Red with { Look = new OwnLook { Strength = 70 } };
        var config = ConfigWith(fence) with
        {
            Settings = new Settings { HideDesktopIcons = true, PeekHotkey = "Ctrl+Q", AutoUpdate = false, Appearance = Appearance with { WallpaperAccent = true } },
            Presets = [new Recipe { Name = "Mine" }],
            Library = new LibrarySettings
            {
                Folders = ["D:\\Games"], Hidden = ["steam:1"], CoverChoices = new Dictionary<string, string> { ["steam:2"] = "choice.jpg" },
                Sources = new LibrarySources { Steam = false }, OnlineArt = true, NewGamesFence = fence.Id,
            },
        };
        var reset = SettingsReset.Apply(config);
        Assert.Equal(Settings.NewInstall, reset.Settings); // M41a: as NeoFences comes now
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
            Settings = new Settings { PeekHotkey = "Ctrl+Q" }, Presets = [new Recipe { Name = "Mine" }], Library = new LibrarySettings { Folders = ["D:\\Games"] },
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
