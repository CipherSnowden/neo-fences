using NeoFences.Core.Config;

namespace NeoFences.Core.Model;

/// <summary>
/// One change in Fence settings (M41b, spec §1–2), by its <see cref="SettingsCatalog"/> id: to this fence (a null value
/// follows All fences again) or, with no fence, to All fences. Settings only a fence has (layout, lock, widgets float) change
/// the fence; "Rolled-up fences open" is always All fences'. Pure: an unknown id or a gone fence changes nothing.
/// </summary>
public static class SettingEdits
{
    /// <summary>The setting a fence can have its own value for, or null (only the fence's, or only All fences').</summary>
    public static LookSetting? LookSettingOf(string id) => id switch
    {
        "fence.theme" => LookSetting.Theme,
        "fence.transparency" => LookSetting.Transparency,
        "fence.colourStyle" => LookSetting.ColourStyle,
        "fence.titleStyle" => LookSetting.TitleStyle,
        "fence.titleFont" => LookSetting.TitleFont,
        "fence.titleAlign" => LookSetting.TitleAlign,
        "fence.titleBar" => LookSetting.TitleBar,
        "fence.iconSize" => LookSetting.IconSize,
        "fence.labels" => LookSetting.Labels,
        "fence.spacing" => LookSetting.Spacing,
        "fence.openWith" => LookSetting.OpenWith,
        _ => null,
    };

    /// <param name="fenceId">The fence, or null: All fences.</param>
    public static NeoFencesConfig Set(NeoFencesConfig config, string? fenceId, string id, object? value)
    {
        if (id == "fence.rollUp") return value is RollupExpand mode ? config with { Settings = config.Settings with { RollupExpand = mode } } : config;
        if (fenceId is null) return value is null ? config : SetAll(config, id, value);
        if (config.Fences.FirstOrDefault(fence => fence.Id == fenceId) is not { } fence) return config;
        var own = fence.Look ?? new OwnLook();
        Fence? changed = id switch
        {
            "fence.theme" => fence with { Look = own with { Theme = (FenceTheme?)value } },
            "fence.transparency" => fence with { Look = own with { Strength = (int?)value } },
            "fence.colourStyle" => fence with { Look = own with { ColourStyle = (ColourStyle?)value } },
            "fence.titleStyle" => fence with { Look = own with { TitleStyle = (TitleStyle?)value } },
            "fence.titleFont" => fence with { Look = own with { TitleFont = (TitleFont?)value } },
            "fence.titleAlign" => fence with { Look = own with { TitleAlign = (TitleAlign?)value } },
            "fence.titleBar" => fence with { Look = own with { TitleOnHover = value is bool show ? !show : null } },
            "fence.spacing" => fence with { Look = own with { Spacing = (Spacing?)value } },
            "fence.iconSize" => fence with { IconSize = value is int size && ConfigNormalizer.IconSizes.Contains(size) ? size : null },
            "fence.labels" => fence with { Labels = (LabelMode?)value },
            "fence.openWith" => fence with { OpenWith = (OpenWith?)value },
            "fence.layout" when value is Items.FenceLayout layout => fence with { Layout = layout },
            "fence.lock" when value is bool locked => fence with { Locked = locked },
            "fence.widgetsFloat" when value is bool floating => fence with { WidgetsFloat = floating },
            _ => null,
        };
        if (changed is null) return config;
        return config.WithFence(changed with { Look = ConfigNormalizer.NormalizeLook(changed.Look) });
    }

    /// <summary>
    /// Undo in Fence settings: what Fence settings changes — All fences' look, Open items with, Rolled-up fences open, and every
    /// fence's settings (look, sizes, names, clicks, colour, emoji, name, layout, lock, widgets float) — as it was. The rest of
    /// Settings and the recipes stay as they are now (final review: Settings' switches have effects undo cannot replay);
    /// fences made or deleted since stay too, and items are never involved.
    /// </summary>
    public static NeoFencesConfig UndoTo(NeoFencesConfig current, NeoFencesConfig previous)
    {
        var before = previous.Fences.ToDictionary(fence => fence.Id, StringComparer.Ordinal);
        return current with
        {
            Settings = current.Settings with
            {
                Appearance = previous.Settings.Appearance, OpenItemsWith = previous.Settings.OpenItemsWith, RollupExpand = previous.Settings.RollupExpand,
            },
            Fences = current.Fences.Select(fence => before.TryGetValue(fence.Id, out var was)
                ? fence with
                {
                    Look = was.Look, IconSize = was.IconSize, Labels = was.Labels, OpenWith = was.OpenWith, Emoji = was.Emoji, Title = was.Title,
                    TabColor = was.TabColor, CustomColor = was.CustomColor, WindowsAccent = was.WindowsAccent, Layout = was.Layout,
                    Locked = was.Locked, WidgetsFloat = was.WidgetsFloat,
                }
                : fence).ToList(),
        };
    }

    private static NeoFencesConfig SetAll(NeoFencesConfig config, string id, object value)
    {
        var settings = config.Settings;
        var all = settings.Appearance;
        settings = id switch
        {
            "fence.theme" when value is FenceTheme theme => settings with { Appearance = all with { Theme = theme } },
            "fence.transparency" when value is int strength => settings with { Appearance = all.WithStrength(Math.Clamp(strength, 0, AppearanceSettings.MaxStrength)) },
            "fence.colourStyle" when value is ColourStyle style => settings with { Appearance = all with { ColourStyle = style } },
            "fence.titleStyle" when value is TitleStyle titleStyle => settings with { Appearance = all with { TitleStyle = titleStyle } },
            "fence.titleFont" when value is TitleFont font => settings with
            {
                Appearance = all with { TitleFont = new TitleFont(font.Family ?? all.TitleFont.Family, font.Size ?? all.TitleFont.Size, font.Weight ?? all.TitleFont.Weight) },
            },
            "fence.titleAlign" when value is TitleAlign align => settings with { Appearance = all with { TitleAlign = align } },
            "fence.titleBar" when value is bool show => settings with { Appearance = all with { TitleOnHover = !show } },
            "fence.spacing" when value is Spacing spacing => settings with { Appearance = all with { Spacing = spacing } },
            "fence.iconSize" when value is int size && ConfigNormalizer.IconSizes.Contains(size) => settings with { Appearance = all with { IconSize = size } },
            "fence.labels" when value is LabelMode labels => settings with { Appearance = all with { Labels = labels } },
            "fence.openWith" when value is OpenWith openWith => settings with { OpenItemsWith = openWith },
            _ => settings,
        };
        return settings == config.Settings ? config : config with { Settings = settings };
    }
}
