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

    /// <summary>Layout ▸ (M24): packed in order, or at fixed positions.</summary>
    public static NeoFencesConfig SetLayout(NeoFencesConfig config, string fenceId, Items.FenceLayout layout) =>
        config.WithFence(Require(config, fenceId) with { Layout = layout });

    /// <summary>Auto-collect… (M27): the fence's rules, replaced as a whole.</summary>
    /// <exception cref="ArgumentException">No fence with that id.</exception>
    public static NeoFencesConfig SetCollect(NeoFencesConfig config, string fenceId, IReadOnlyList<Items.CollectRule> rules) =>
        config.WithFence(Require(config, fenceId) with { Collect = [.. rules] });

    /// <summary>Icon-only (M8b): labels always shown, or only on hover / selection.</summary>
    public static NeoFencesConfig SetLabels(NeoFencesConfig config, string fenceId, LabelMode labels) =>
        config.WithFence(Require(config, fenceId) with { Labels = labels });

    /// <summary>Fence settings… (M36): the fence's own look, replaced as a whole; a look with nothing set is none (like all fences).</summary>
    public static NeoFencesConfig SetLook(NeoFencesConfig config, string fenceId, OwnLook? look) =>
        config.WithFence(Require(config, fenceId) with { Look = ConfigNormalizer.NormalizeLook(look) });

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

    /// <summary>A new folder view (M21), titled with its folder's name.</summary>
    public static (NeoFencesConfig Config, Fence Fence) CreateView(NeoFencesConfig config, FolderView view)
    {
        var (created, fence) = CreateFence(config, Items.FolderViews.NameOf(view.Path));
        fence = fence with { View = view };
        return (created.WithFence(fence), fence);
    }

    /// <summary>
    /// A view's settings or folder changed (M21; its folder renamed, or chosen again): the title follows the folder while it
    /// still is the old folder's name.
    /// </summary>
    /// <exception cref="ArgumentException">No fence with that id, or it is the Game Library.</exception>
    public static NeoFencesConfig SetView(NeoFencesConfig config, string fenceId, FolderView view)
    {
        var fence = Require(config, fenceId);
        if (fence.IsLibrary) throw new ArgumentException("The Game Library cannot be a folder view.", nameof(fenceId));
        var follows = fence.View is { } old && fence.Title == Items.FolderViews.NameOf(old.Path) && !Items.FolderViews.SameFolder(old.Path, view.Path);
        return config.WithFence(fence with { View = view, Title = follows ? Items.FolderViews.NameOf(view.Path) : fence.Title });
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
        return config with
        {
            Fences = config.Fences.Where(fence => fence.Id != fenceId).ToList(),
            Library = config.Library.NewGamesFence == fenceId ? config.Library with { NewGamesFence = null } : config.Library, // M22
        };
    }

    private static Fence Require(NeoFencesConfig config, string fenceId) =>
        config.Fences.FirstOrDefault(fence => fence.Id == fenceId) ?? throw new ArgumentException($"No fence with id {fenceId}.", nameof(fenceId));
}
