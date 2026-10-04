using NeoFences.Core.Config;

namespace NeoFences.Core.Model;

/// <summary>Changes to one fence's own settings (title, icon size, lock). Pure: each returns a new config.</summary>
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

    public static NeoFencesConfig SetSort(NeoFencesConfig config, string fenceId, FenceSort sort) =>
        config.WithFence(Require(config, fenceId) with { Sort = sort });

    /// <summary>
    /// "Sort by" on a desktop fence: a one-time reorder (dragging still works afterwards). The new order must hold exactly
    /// the fence's items (compared ignoring case; the stored spelling is kept), so a sort can never drop or add an item.
    /// </summary>
    /// <exception cref="ArgumentException">The order is not a permutation of the fence's items.</exception>
    public static NeoFencesConfig SetItemOrder(NeoFencesConfig config, string fenceId, IReadOnlyList<string> orderedRefs)
    {
        var fence = Require(config, fenceId);
        var spelling = fence.Items.ToDictionary(itemRef => itemRef, ItemRef.Comparer);
        if (orderedRefs.Count != fence.Items.Count || orderedRefs.Distinct(ItemRef.Comparer).Count() != orderedRefs.Count
            || !orderedRefs.All(spelling.ContainsKey))
            throw new ArgumentException("The new order must contain exactly the fence's items.", nameof(orderedRefs));
        return config.WithFence(fence with { Items = orderedRefs.Select(itemRef => spelling[itemRef]).ToList() });
    }

    private static Fence Require(NeoFencesConfig config, string fenceId) =>
        config.Fences.FirstOrDefault(fence => fence.Id == fenceId) ?? throw new ArgumentException($"No fence with id {fenceId}.", nameof(fenceId));
}
