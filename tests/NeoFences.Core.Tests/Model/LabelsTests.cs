using NeoFences.Core.Config;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Model;

/// <summary>Icon-only fences (M8b, user choice 2026-10-03): labels per fence, a default for new fences, "apply to all".</summary>
public class LabelsTests
{
    private static (NeoFencesConfig Config, Fence Games) Sample()
    {
        var games = Fence.Create("Games");
        return (new NeoFencesConfig { Fences = [Fence.Create("Inbox"), games] }, games);
    }

    [Fact]
    public void ExistingFences_ShowLabels_ByDefault()
    {
        // Configs from v1.0 have no "labels" field: everything keeps looking the same.
        var (config, games) = Sample();
        Assert.Null(games.Labels); // M41b: follows All fences (Always)
        Assert.Equal(LabelMode.Always, config.Settings.DefaultLabels);
        Assert.False(config.Settings.ShowShortcutArrows); // shortcut arrows: off unless chosen in Settings
        // A real v1.0 file: the new fields removed from the JSON itself (final review I6: a text replace never matched).
        var labelled = config with { Fences = config.Fences.Select(fence => fence with { Labels = LabelMode.OnHover }).ToList(), Settings = config.Settings with { DefaultLabels = LabelMode.OnHover } };
        var json = System.Text.Json.Nodes.JsonNode.Parse(ConfigJson.Serialize(labelled))!;
        foreach (var fence in json["fences"]!.AsArray()) Assert.True(fence!.AsObject().Remove("labels"));
        Assert.True(json["settings"]!.AsObject().Remove("defaultLabels"));
        Assert.True(json["settings"]!.AsObject().Remove("showShortcutArrows"));
        var loaded = ConfigJson.Deserialize(json.ToJsonString());
        // M41b: no labels of its own = follows All fences, which shows them (Always).
        Assert.All(loaded.Fences, fence => Assert.Equal(LabelMode.Always, NeoFences.Core.Appearance.FenceLook.Resolve(loaded.Settings.Appearance, fence, false, null).Labels));
        Assert.Equal(LabelMode.Always, loaded.Settings.DefaultLabels);
        Assert.False(loaded.Settings.ShowShortcutArrows);
    }

    [Fact]
    public void SetLabels_ChangesOneFence()
    {
        var (config, games) = Sample();
        var changed = FenceEdits.SetLabels(config, games.Id, LabelMode.OnHover);
        Assert.Equal(LabelMode.OnHover, changed.Fences.Single(fence => fence.Id == games.Id).Labels);
        Assert.Null(changed.Fences[0].Labels); // M41b: the other fence still follows All fences
    }

    // M41b: "Apply to all fences" is All fences mode in Fence settings (AllFencesTests.MakeFollow_…).

    [Fact]
    public void NewFences_FollowAllFences()
    {
        var (config, _) = Sample();
        config = config with { Settings = config.Settings with { DefaultLabels = LabelMode.OnHover } };
        Assert.Null(FenceEdits.CreateFence(config, "Tools").Fence.Labels); // M41b: follows All fences (AllFencesTests)
    }

    [Fact]
    public void LabelMode_RoundTrips_AsCamelCase()
    {
        var (config, games) = Sample();
        var json = ConfigJson.Serialize(FenceEdits.SetLabels(config, games.Id, LabelMode.OnHover));
        Assert.Contains("\"labels\": \"onHover\"", json.Replace("\":\"", "\": \""));
        Assert.Equal(LabelMode.OnHover, ConfigJson.Deserialize(json).Fences.Single(fence => fence.Id == games.Id).Labels);
    }

    [Fact]
    public void Rename_CutAt64_NeverSplitsAnEmoji()
    {
        // 63 letters and an emoji (a surrogate pair): cutting at 64 chars would leave half of it (M2c review carry-over).
        var (config, games) = Sample();
        var title = new string('a', 63) + "🎮" + "tail";
        var renamed = FenceEdits.Rename(config, games.Id, title).Fences.Single(fence => fence.Id == games.Id).Title;
        Assert.Equal(new string('a', 63), renamed);
        Assert.False(char.IsHighSurrogate(renamed[^1]));
    }
}
