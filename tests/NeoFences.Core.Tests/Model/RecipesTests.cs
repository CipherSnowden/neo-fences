using NeoFences.Core.Appearance;
using NeoFences.Core.Config;
using NeoFences.Core.Items;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Model;

/// <summary>M41b (spec §4): recipes set look and behaviour, never items, places or sizes.</summary>
public class RecipesTests
{
    private static Recipe Named(string name) => Recipes.BuiltIn.Single(recipe => recipe.Name == name);

    [Fact]
    public void BuiltIns_AreTheFourFromTheBrainstorm()
    {
        Assert.Equal(["Game shelf", "Work desk", "Downloads inbox", "Minimal"], Recipes.BuiltIn.Select(recipe => recipe.Name));
        Assert.All(Recipes.BuiltIn, recipe => Assert.False(string.IsNullOrEmpty(recipe.Emoji)));
        Assert.True(Named("Downloads inbox").Inbox);
    }

    [Fact]
    public void Apply_SetsLookAndBehaviour_KeepsEverythingElse()
    {
        var fence = Fence.Create("Games") with { Emoji = "🎮", TabColor = TabColor.Red, Locked = true, Tabs = ["x"], WidgetsFloat = false };
        var config = new NeoFencesConfig { Fences = [fence], Layouts = new Dictionary<string, Layout>() };
        var after = Recipes.Apply(config, fence.Id, Named("Game shelf")).Fences[0];
        Assert.Equal(FenceTheme.Hud, after.Look!.Theme);
        Assert.Equal((96, LabelMode.OnHover, OpenWith.OneClick), (after.IconSize, after.Labels, after.OpenWith));
        Assert.Equal((fence.Id, "Games", "🎮", TabColor.Red, true), (after.Id, after.Title, after.Emoji, after.TabColor, after.Locked));
        Assert.Equal(fence.Tabs, after.Tabs);
    }

    [Fact]
    public void Apply_ARecipeWithoutAPart_KeepsTheFencesPart()
    {
        var fence = Fence.Create("Desk") with { Layout = FenceLayout.Free, IconSize = 32 };
        var after = Recipes.Apply(new NeoFencesConfig { Fences = [fence] }, fence.Id, Named("Downloads inbox")).Fences[0];
        Assert.Equal((FenceLayout.Free, 32), (after.Layout, after.IconSize));
    }

    [Fact]
    public void ApplyToAll_SetsAllFencesAndMakesEveryFenceFollow()
    {
        var own = Fence.Create("Own") with { IconSize = 32, Look = new OwnLook { Theme = FenceTheme.Neon } };
        var plain = Fence.Create("Plain");
        var config = new NeoFencesConfig { Settings = Settings.NewInstall, Fences = [own, plain] };
        var after = Recipes.ApplyToAll(config, Named("Game shelf"), onlyFenceIds: null);
        Assert.Equal(FenceTheme.Hud, after.Settings.Appearance.Theme);
        Assert.Equal((96, LabelMode.OnHover), (after.Settings.Appearance.IconSize, after.Settings.Appearance.Labels));
        Assert.Equal(OpenWith.OneClick, after.Settings.OpenItemsWith);
        Assert.All(after.Fences, fence => Assert.Equal(FenceTheme.Hud, FenceLook.Resolve(after.Settings.Appearance, fence, false, null).Theme));
        Assert.All(after.Fences, fence => Assert.Null(fence.IconSize));
    }

    [Fact]
    public void ApplyToAll_OnlySomeFences_SetsTheirOwnValues_OthersKeepFollowing()
    {
        var games = Fence.Create("Games");
        var work = Fence.Create("Work");
        var config = new NeoFencesConfig { Settings = Settings.NewInstall, Fences = [games, work] };
        var after = Recipes.ApplyToAll(config, Named("Game shelf"), onlyFenceIds: [games.Id]);
        Assert.Equal(config.Settings, after.Settings);
        Assert.Equal(FenceTheme.Hud, after.Fences[0].Look!.Theme);
        Assert.Same(work, after.Fences[1]);
    }

    [Fact]
    public void ARecipesLayout_IsNotSetByCore_TheAppPinsTheCellsFirst()
    {
        var recipe = Named("Game shelf") with { Layout = FenceLayout.Free };
        var fence = Fence.Create("A");
        var config = new NeoFencesConfig { Settings = Settings.NewInstall, Fences = [fence] };
        Assert.Equal(FenceLayout.Flow, Recipes.Apply(config, fence.Id, recipe).Fences[0].Layout);
        Assert.Equal(FenceLayout.Flow, Recipes.ApplyToAll(config, recipe, null).Fences[0].Layout);
        Assert.Equal(FenceLayout.Flow, Recipes.ApplyToAll(config, recipe, [fence.Id]).Fences[0].Layout);
    }

    [Fact]
    public void AnOldOwnPresetNamedLikeANewBuiltIn_IsKept_UnderANewName()
    {
        var config = ConfigNormalizer.Normalize(new NeoFencesConfig { Presets = [new Recipe { Name = "Game shelf", Look = new OwnLook { Strength = 12 } }] });
        var kept = Assert.Single(config.Presets);
        Assert.Equal(("Game shelf (mine)", 12), (kept.Name, kept.Look.Strength));
    }

    [Fact]
    public void ApplyToAll_NeverTouchesLayoutsOrItems()
    {
        var fence = Fence.Create("A");
        var layouts = new Dictionary<string, Layout> { ["fp"] = new Layout() };
        var config = new NeoFencesConfig { Settings = Settings.NewInstall, Fences = [fence], Layouts = layouts };
        Assert.Same(layouts, Recipes.ApplyToAll(config, Named("Minimal"), null).Layouts);
    }

    [Fact]
    public void Save_CapturesWhatTheFenceShows_IncludingWhatItFollows()
    {
        var config = new NeoFencesConfig { Settings = Settings.NewInstall with { Appearance = AppearanceSettings.NewInstall with { IconSize = 64 } }, Fences = [Fence.Create("A") with { Look = new OwnLook { TitleStyle = TitleStyle.Pill } }] };
        var saved = Recipes.Save(config, config.Fences[0].Id, "  My  look ", "⭐", windowsLight: false);
        var recipe = Assert.Single(saved.Presets);
        Assert.Equal(("My look", "⭐", 64, TitleStyle.Pill, FenceTheme.Aurora), (recipe.Name, recipe.Emoji, recipe.IconSize, recipe.Look.TitleStyle, recipe.Look.Theme));
    }

    [Fact]
    public void NameProblem_BlocksBuiltInNames_AllowsTheOldPresetNames()
    {
        Assert.NotNull(Recipes.NameProblem("game SHELF"));
        Assert.Null(Recipes.NameProblem("Glass")); // the old built-in presets are gone
        Assert.NotNull(Recipes.NameProblem("   "));
    }

    [Fact]
    public void OldPresetsLoad_AsOwnRecipes()
    {
        var json = """{"schemaVersion":5,"presets":[{"name":"Mine","look":{"strength":12},"iconSize":32,"labels":"onHover"}]}""";
        var config = ConfigNormalizer.Normalize(ConfigJson.Deserialize(json));
        var recipe = Assert.Single(config.Presets);
        Assert.Equal(("Mine", 12, 32, LabelMode.OnHover, (string?)null), (recipe.Name, recipe.Look.Strength, recipe.IconSize, recipe.Labels, recipe.Emoji));
    }

    [Fact]
    public void Changes_ListsWhatARecipeChanges_InPlainWords()
    {
        var changes = Recipes.Changes(Named("Game shelf"));
        Assert.Contains("Theme → Game HUD", changes);
        Assert.Contains("Open with → one click", changes);
        Assert.DoesNotContain(changes, line => line.Contains("item", StringComparison.OrdinalIgnoreCase) && !line.StartsWith("Icon", StringComparison.Ordinal));
    }

    // 1.0.1 (rc.4 review minor): a recipe changes only the look parts it has; the fence's other own parts stay.
    [Fact]
    public void Apply_KeepsTheFencesOwnLookParts_TheRecipeHasNot()
    {
        var font = new TitleFont("Consolas", 17, null);
        var fence = Fence.Create("Work") with { Look = new OwnLook { TitleFont = font, TitleAlign = TitleAlign.Centre, ColourStyle = ColourStyle.TitleStrip, Spacing = Spacing.Roomy, Strength = 80 } };
        var look = Recipes.Apply(new NeoFencesConfig { Fences = [fence] }, fence.Id, Named("Game shelf")).Fences[0].Look!;
        Assert.Equal((FenceTheme.Hud, TitleStyle.Caps, 60), (look.Theme, look.TitleStyle, look.Strength));
        Assert.Equal((font, TitleAlign.Centre, ColourStyle.TitleStrip, Spacing.Roomy), (look.TitleFont, look.TitleAlign, look.ColourStyle, look.Spacing));
    }

    // 1.0.1 (rc.4 review minor): a Classic fence in light mode shows the light transparency; a recipe saved from it keeps that.
    [Fact]
    public void SaveAndMatching_UseTheTransparencyTheFenceShows()
    {
        var appearance = new AppearanceSettings { Theme = FenceTheme.Classic, StrengthDark = 20, StrengthLight = 70 };
        var config = new NeoFencesConfig { Settings = Settings.NewInstall with { Appearance = appearance }, Fences = [Fence.Create("A")] };
        var fence = config.Fences[0];
        var light = Recipes.Save(config, fence.Id, "Light", emoji: null, windowsLight: true);
        Assert.Equal(70, Assert.Single(light.Presets).Look.Strength);
        Assert.Equal(20, Assert.Single(Recipes.Save(config, fence.Id, "Dark", emoji: null, windowsLight: false).Presets).Look.Strength);
        Assert.Equal("Light", Recipes.Matching(light, fence, windowsLight: true));
        Assert.Null(Recipes.Matching(light, fence, windowsLight: false));
        // A theme with its own tone ignores Windows': Light Mica is light.
        var mica = config with { Settings = config.Settings with { Appearance = appearance with { Theme = FenceTheme.LightMica } } };
        Assert.Equal(70, Assert.Single(Recipes.Save(mica, fence.Id, "Mica", emoji: null, windowsLight: false).Presets).Look.Strength);
    }

    // 1.0.1 (rc.4 review minor): the confirmations list every part an own recipe changes.
    [Fact]
    public void Changes_ListsTheTitleFontAlignmentColourStyleAndAShownTitleBar()
    {
        var recipe = new Recipe { Name = "Mine", Look = new OwnLook { TitleFont = new TitleFont("Consolas", 17, TitleWeight.Bold), TitleAlign = TitleAlign.Centre, ColourStyle = ColourStyle.TintedGlass, TitleOnHover = false } };
        var changes = Recipes.Changes(recipe);
        Assert.Contains("Title font → Consolas, Large, Bold", changes);
        Assert.Contains("Title alignment → Centre", changes);
        Assert.Contains("Colour style → Tinted glass", changes);
        Assert.Contains("Title bar → always shown", changes);
    }
}
