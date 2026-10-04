using NeoFences.Core.Config;

namespace NeoFences.Core.Model;

/// <summary>Changes to fences (title, icon size, lock, new, delete). Pure: each returns a new config; none touches a file.</summary>
public static class FenceEdits
{
    public const int MaxTitleLength = 64;

    /// <summary>Trims and cuts the title; a blank title keeps the old one (returns the same config).</summary>
    public static NeoFencesConfig Rename(NeoFencesConfig config, string fenceId, string title)
    {
        var fence = Require(config, fenceId);
        var trimmed = title.Trim();
        if (trimmed.Length == 0) return config;
        // Never cut between the halves of a surrogate pair (an emoji): one char less instead (M8b).
        var cut = trimmed.Length > MaxTitleLength && char.IsHighSurrogate(trimmed[MaxTitleLength - 1]) ? MaxTitleLength - 1 : MaxTitleLength;
        return config.WithFence(fence with { Title = trimmed.Length > MaxTitleLength ? trimmed[..cut] : trimmed });
    }

    /// <exception cref="ArgumentOutOfRangeException">Not one of <see cref="ConfigNormalizer.IconSizes"/>.</exception>
    public static NeoFencesConfig SetIconSize(NeoFencesConfig config, string fenceId, int iconSize)
    {
        var fence = Require(config, fenceId);
        if (!ConfigNormalizer.IconSizes.Contains(iconSize)) throw new ArgumentOutOfRangeException(nameof(iconSize), iconSize, "Unsupported icon size.");
        return config.WithFence(fence with { IconSize = iconSize });
    }

    /// <summary>Fence menu → Colour → Custom… (M14): any "#RRGGBB"; it wins over the swatch, which is cleared.</summary>
    /// <exception cref="ArgumentException">Not a "#RRGGBB" colour.</exception>
    public static NeoFencesConfig SetCustomColor(NeoFencesConfig config, string fenceId, string hex)
    {
        var colour = Appearance.Argb.FromHex(hex) ?? throw new ArgumentException($"'{hex}' is not a #RRGGBB colour.", nameof(hex));
        return config.WithFence(Require(config, fenceId) with { CustomColor = colour.ToHex(), TabColor = null });
    }

    public static NeoFencesConfig SetLocked(NeoFencesConfig config, string fenceId, bool locked) =>
        config.WithFence(Require(config, fenceId) with { Locked = locked });

    /// <summary>Roll-up (M5): the fence shows only its title bar until hovered (spec §6, Settings.RollupExpand).</summary>
    public static NeoFencesConfig SetRolledUp(NeoFencesConfig config, string fenceId, bool rolledUp) =>
        config.WithFence(Require(config, fenceId) with { RolledUp = rolledUp });

    /// <summary>Icon-only (M8b): labels always shown, or only on hover / selection.</summary>
    public static NeoFencesConfig SetLabels(NeoFencesConfig config, string fenceId, LabelMode labels) =>
        config.WithFence(Require(config, fenceId) with { Labels = labels });

    /// <summary>Settings → "Apply to all fences": every fence, and the default for new ones.</summary>
    public static NeoFencesConfig SetLabelsEverywhere(NeoFencesConfig config, LabelMode labels) =>
        config with
        {
            Fences = config.Fences.Select(fence => fence with { Labels = labels }).ToList(),
            Settings = config.Settings with { DefaultLabels = labels },
        };

    /// <summary>A new empty fence (fence menu, tray, or one drawn on the desktop), with the default labels.</summary>
    public static (NeoFencesConfig Config, Fence Fence) CreateFence(NeoFencesConfig config, string title)
    {
        var fence = Fence.Create(title) with { Labels = config.Settings.DefaultLabels };
        return (config with { Fences = [.. config.Fences, fence] }, fence);
    }

    /// <summary>
    /// Deletes a fence (a tab leaves its box first; a host hands the box to the next tab, M9). Its items go with it from
    /// items.json; their targets are never touched (hard rule 1).
    /// </summary>
    /// <exception cref="ArgumentException">No fence with that id.</exception>
    public static NeoFencesConfig DeleteFence(NeoFencesConfig config, string fenceId)
    {
        Require(config, fenceId);
        config = FenceTabs.Leave(config, fenceId);
        return config with { Fences = config.Fences.Where(fence => fence.Id != fenceId).ToList() };
    }

    private static Fence Require(NeoFencesConfig config, string fenceId) =>
        config.Fences.FirstOrDefault(fence => fence.Id == fenceId) ?? throw new ArgumentException($"No fence with id {fenceId}.", nameof(fenceId));
}
