using NeoFences.Core.Items;

namespace NeoFences.Core.Model;

/// <summary>
/// When the first-run welcome ends (M30, ADR-051). Pure: each returns the same config when nothing changes, so the host
/// saves only on a change; none touches a file (hard rule 1).
/// </summary>
public static class WelcomeEdits
{
    /// <summary>A welcome fence that holds items is an ordinary fence from now on.</summary>
    public static NeoFencesConfig ClearIfFilled(NeoFencesConfig config, ItemsDocument items)
    {
        if (!config.Fences.Any(fence => fence.Welcome && items.Of(fence.Id).Count > 0)) return config;
        return config with { Fences = [.. config.Fences.Select(fence => fence.Welcome && items.Of(fence.Id).Count > 0 ? fence with { Welcome = false } : fence)] };
    }

    /// <summary>
    /// Add from desktop put items into new fences: a welcome fence still empty was only the welcome, and goes (spec §3).
    /// Nothing added, or only into existing fences: it stays.
    /// </summary>
    public static NeoFencesConfig AfterDesktopFill(NeoFencesConfig config, ItemsDocument items, int added, int newFences)
    {
        if (added == 0 || newFences == 0) return config;
        foreach (var welcome in config.Fences.Where(fence => fence.Welcome && items.Of(fence.Id).Count == 0).ToList())
            config = FenceEdits.DeleteFence(config, welcome.Id);
        return config;
    }
}
