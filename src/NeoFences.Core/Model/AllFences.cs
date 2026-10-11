using NeoFences.Core.Config;

namespace NeoFences.Core.Model;

/// <summary>M41b: the settings a fence can have its own value for, or follow from All fences (spec §1).</summary>
public enum LookSetting { Theme, Transparency, ColourStyle, TitleStyle, TitleFont, TitleAlign, TitleBar, IconSize, Labels, Spacing, OpenWith }

/// <summary>M41b (spec 2026-10-11-m41b-settings-design §1, ADR-067): own values and All fences. Pure; each returns a new config or fence.</summary>
public static class AllFences
{
    public static bool HasOwn(Fence fence, LookSetting setting) => setting switch
    {
        LookSetting.IconSize => fence.IconSize is not null,
        LookSetting.Labels => fence.Labels is not null,
        LookSetting.OpenWith => fence.OpenWith is not null,
        _ => fence.Look is { } look && setting switch
        {
            LookSetting.Theme => look.Theme is not null,
            LookSetting.Transparency => look.Strength is not null,
            LookSetting.ColourStyle => look.ColourStyle is not null,
            LookSetting.TitleStyle => look.TitleStyle is not null,
            LookSetting.TitleFont => look.TitleFont is not null,
            LookSetting.TitleAlign => look.TitleAlign is not null,
            LookSetting.TitleBar => look.TitleOnHover is not null,
            _ => look.Spacing is not null,
        },
    };

    public static Fence ClearOwn(Fence fence, LookSetting setting) => setting switch
    {
        LookSetting.IconSize => fence with { IconSize = null },
        LookSetting.Labels => fence with { Labels = null },
        LookSetting.OpenWith => fence with { OpenWith = null },
        _ when fence.Look is null => fence,
        _ => fence with { Look = ConfigNormalizer.NormalizeLook(ClearPart(fence.Look, setting)) },
    };

    public static int OwnCount(NeoFencesConfig config, LookSetting setting) => config.Fences.Count(fence => HasOwn(fence, setting));

    /// <summary>"Make them follow": every fence's own value for this setting goes.</summary>
    public static NeoFencesConfig MakeFollow(NeoFencesConfig config, LookSetting setting) =>
        config with { Fences = config.Fences.Select(fence => HasOwn(fence, setting) ? ClearOwn(fence, setting) : fence).ToList() };

    /// <summary>"Use all defaults": every own value goes; emoji, colour, layout, lock and the rest are only the fence's.</summary>
    public static NeoFencesConfig UseAllDefaults(NeoFencesConfig config, string fenceId) =>
        config with { Fences = config.Fences.Select(fence => fence.Id == fenceId ? Enum.GetValues<LookSetting>().Aggregate(fence, ClearOwn) : fence).ToList() };

    /// <summary>
    /// Once, for a setup from before rc.4: All fences' icon size and labels become the most common among the fences (a tie
    /// prefers 48 and Always: the usual look); a fence with exactly those values follows, others keep them as their own.
    /// </summary>
    public static NeoFencesConfig Introduce(NeoFencesConfig config)
    {
        if (config.Settings.AllFencesIntroduced) return config;
        var sizes = config.Fences.Select(fence => fence.IconSize).OfType<int>().ToList();
        var labels = config.Fences.Select(fence => fence.Labels).OfType<LabelMode>().ToList();
        var size = sizes.Count == 0 ? config.Settings.Appearance.IconSize
            : sizes.GroupBy(value => value).OrderByDescending(group => group.Count()).ThenByDescending(group => group.Key == 48).First().Key;
        var label = labels.Count == 0 ? config.Settings.DefaultLabels
            : labels.GroupBy(value => value).OrderByDescending(group => group.Count()).ThenByDescending(group => group.Key == LabelMode.Always).First().Key;
        return config with
        {
            Settings = config.Settings with { AllFencesIntroduced = true, Appearance = config.Settings.Appearance with { IconSize = size, Labels = label } },
            Fences = config.Fences.Select(fence => fence with
            {
                IconSize = fence.IconSize == size ? null : fence.IconSize,
                Labels = fence.Labels == label ? null : fence.Labels,
            }).ToList(),
        };
    }

    private static OwnLook ClearPart(OwnLook look, LookSetting setting) => setting switch
    {
        LookSetting.Theme => look with { Theme = null },
        LookSetting.Transparency => look with { Strength = null },
        LookSetting.ColourStyle => look with { ColourStyle = null },
        LookSetting.TitleStyle => look with { TitleStyle = null },
        LookSetting.TitleFont => look with { TitleFont = null },
        LookSetting.TitleAlign => look with { TitleAlign = null },
        LookSetting.TitleBar => look with { TitleOnHover = null },
        _ => look with { Spacing = null },
    };
}
