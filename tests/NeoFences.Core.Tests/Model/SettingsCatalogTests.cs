using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Model;

/// <summary>M41b (spec §2–3): every setting has plain words, an explanation, and is found by search.</summary>
public class SettingsCatalogTests
{
    [Fact]
    public void EverySetting_HasAnIdLabelAndExplanation()
    {
        Assert.Equal(SettingsCatalog.All.Count, SettingsCatalog.All.Select(setting => setting.Id).Distinct().Count());
        Assert.All(SettingsCatalog.All, setting =>
        {
            Assert.False(string.IsNullOrWhiteSpace(setting.Label));
            Assert.False(string.IsNullOrWhiteSpace(setting.What));
            Assert.False(string.IsNullOrWhiteSpace(setting.Default));
        });
    }

    [Fact]
    public void EverySetting_IsFoundByItsOwnLabel()
    {
        Assert.All(SettingsCatalog.All, setting => Assert.Contains(setting, SettingsCatalog.Search(setting.Label)));
    }

    [Fact]
    public void Double_FindsBothDoubleClickSettings_LabelMatchesFirst()
    {
        var found = SettingsCatalog.Search("double").Select(setting => setting.Id).ToList();
        Assert.Contains("desktop.quickHide", found);
        Assert.Contains("fence.openWith", found);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyQuery_FindsNothing(string? query) => Assert.Empty(SettingsCatalog.Search(query));

    [Theory]
    [InlineData("x")]
    [InlineData("🎮")]
    [InlineData("transparéncy")]
    [InlineData("zzzz qqqq")]
    [InlineData("\ud83c")] // a lone surrogate (pasted half of an emoji)
    [InlineData("a\udc00b")]
    public void OddQueries_NeverThrow(string query) => _ = SettingsCatalog.Search(query);

    [Fact]
    public void EveryWord_MustMatch_AnyCase()
    {
        Assert.Contains(SettingsCatalog.Find("look.motion"), SettingsCatalog.Search("ANIMATIONS lively"));
        Assert.DoesNotContain(SettingsCatalog.Find("look.motion"), SettingsCatalog.Search("animations wallpaper"));
    }

    [Fact]
    public void TheAuditsRemovals_AreNotInTheCatalog()
    {
        var labels = SettingsCatalog.All.Select(setting => setting.Label).ToList();
        Assert.DoesNotContain("Hidden games", labels);
        Assert.DoesNotContain("Labels for new fences", labels);
        Assert.DoesNotContain("Use my accent colour", labels);
    }
}
