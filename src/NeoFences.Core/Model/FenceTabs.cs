using NeoFences.Core.Layouts;

namespace NeoFences.Core.Model;

/// <summary>A tab's accent colour (M9): a bar under its header.</summary>
public enum TabColor { Red, Orange, Yellow, Green, Teal, Blue, Purple, Pink }

/// <summary>
/// Fence tabs (M9, spec 2026-10-03-fence-tabs-design): fences combined into one box. Every tab stays a full fence (items,
/// source, sort, icon size, labels). The host — the fence whose <see cref="Fence.Tabs"/> lists two or more ids, itself
/// included — owns the box: its window, its placement in every layout, roll-up and lock. Members have no placement.
/// Every edit returns a new config; nothing changes items or files.
/// </summary>
public static class FenceTabs
{
    /// <summary>True when the fence is a tab of another fence's box.</summary>
    public static bool IsMember(NeoFencesConfig config, string fenceId) => HostListing(config, fenceId) is not null;

    /// <summary>The fence owning the box this fence shows in: its host, or itself when it is not a member.</summary>
    public static Fence HostOf(NeoFencesConfig config, string fenceId) =>
        HostListing(config, fenceId) ?? Find(config, fenceId);

    /// <summary>The fences that get a window: hosts and standalone fences, in config order.</summary>
    public static IReadOnlyList<Fence> Boxes(NeoFencesConfig config) =>
        config.Fences.Where(fence => !IsMember(config, fence.Id)).ToList();

    /// <summary>A box's tabs in order; a standalone fence is its own only tab.</summary>
    public static IReadOnlyList<Fence> TabsOf(NeoFencesConfig config, string hostId)
    {
        var host = Find(config, hostId);
        return host.Tabs.Count > 1 ? host.Tabs.Select(id => Find(config, id)).ToList() : [host];
    }

    /// <summary>The tab a box shows (the host's <see cref="Fence.ActiveTab"/>, else its first tab).</summary>
    public static Fence ActiveOf(NeoFencesConfig config, string hostId)
    {
        var tabs = TabsOf(config, hostId);
        var activeId = Find(config, hostId).ActiveTab;
        return tabs.FirstOrDefault(tab => tab.Id == activeId) ?? tabs[0];
    }

    /// <summary>
    /// Drops a fence onto another fence's box: it becomes a tab there (a box dropped as a whole brings all its tabs, in
    /// order), at <paramref name="insertAt"/> or the end, and is shown. Its own placements go (a member has none). Into
    /// its own box, or onto itself: the same config.
    /// </summary>
    /// <param name="wholeBox">True when a box was moved by its title (all its tabs come along); false for one tab's header
    /// (only that tab moves, also when it is the box's host; final review C1).</param>
    public static NeoFencesConfig Merge(NeoFencesConfig config, string movingFenceId, string targetFenceId, int? insertAt = null, bool wholeBox = true)
    {
        var targetHost = HostOf(config, targetFenceId);
        if (HostOf(config, movingFenceId).Id == targetHost.Id) return config;

        List<string> moving;
        if (IsMember(config, movingFenceId) || !wholeBox)
        {
            config = Leave(config, movingFenceId); // one tab dragged out of its box (a host hands the box to the next tab)
            moving = [movingFenceId];
        }
        else
        {
            var movingFence = Find(config, movingFenceId);
            moving = movingFence.Tabs.Count > 1 ? [.. movingFence.Tabs] : [movingFenceId];
            config = config.WithFence(movingFence with { Tabs = [], ActiveTab = null }); // a whole box: its tabs come along
        }

        // The box owns roll-up and lock: the joining fences' own go, so a tab detached later is not rolled up from before.
        foreach (var joiningId in moving) config = config.WithFence(Find(config, joiningId) with { RolledUp = false, Locked = false });
        targetHost = Find(config, targetHost.Id);
        var tabs = targetHost.Tabs.Count > 1 ? targetHost.Tabs.ToList() : [targetHost.Id];
        tabs.InsertRange(Math.Clamp(insertAt ?? tabs.Count, 0, tabs.Count), moving);
        config = config.WithFence(targetHost with { Tabs = tabs, ActiveTab = movingFenceId });
        return WithoutPlacements(config, moving);
    }

    /// <summary>
    /// A tab leaves its box and becomes an ordinary fence at <paramref name="rect"/> in the layout
    /// <paramref name="fingerprint"/> (its placements in other setups go: the layout engine places it there anew).
    /// Detaching the host hands the box (tabs, active tab, roll-up, lock, placements) to the next tab. A fence that is
    /// not in a box: the same config.
    /// </summary>
    public static NeoFencesConfig Detach(NeoFencesConfig config, string fenceId, string fingerprint, FenceRect rect)
    {
        if (TabsOf(config, HostOf(config, fenceId).Id).Count < 2) return config;
        config = WithoutPlacements(Leave(config, fenceId), [fenceId]);
        return LayoutEngine.WithFenceRect(config, fingerprint, fenceId, rect);
    }

    /// <summary>
    /// Takes a tab out of its box without placing it (detach, delete, a move to another box). The host leaving hands the
    /// box to the next tab; a box left with one tab becomes an ordinary fence.
    /// </summary>
    public static NeoFencesConfig Leave(NeoFencesConfig config, string fenceId)
    {
        var host = HostOf(config, fenceId);
        if (host.Tabs.Count < 2) return config;
        var remaining = host.Tabs.Where(id => id != fenceId).ToList();
        var active = host.ActiveTab == fenceId ? null : host.ActiveTab;
        if (host.Id != fenceId)
        {
            return config.WithFence(remaining.Count > 1 ? host with { Tabs = remaining, ActiveTab = active } : host with { Tabs = [], ActiveTab = null });
        }

        var heir = Find(config, remaining[0]);
        heir = remaining.Count > 1
            ? heir with { Tabs = remaining, ActiveTab = active ?? heir.Id, RolledUp = host.RolledUp, Locked = host.Locked }
            : heir with { Tabs = [], ActiveTab = null, RolledUp = host.RolledUp, Locked = host.Locked };
        config = config.WithFence(heir).WithFence(host with { Tabs = [], ActiveTab = null, RolledUp = false, Locked = false });
        // The heir takes the box's place in every setup.
        var layouts = config.Layouts.ToDictionary(entry => entry.Key, entry =>
        {
            if (!entry.Value.Fences.TryGetValue(host.Id, out var boxRect)) return entry.Value;
            var rects = new Dictionary<string, FenceRect>(entry.Value.Fences) { [heir.Id] = boxRect };
            return entry.Value with { Fences = rects };
        });
        return config with { Layouts = layouts };
    }

    /// <summary>Moves a tab to another position in its box (dragging its header along the strip).</summary>
    public static NeoFencesConfig Reorder(NeoFencesConfig config, string fenceId, int newIndex)
    {
        var host = HostOf(config, fenceId);
        if (host.Tabs.Count < 2) return config;
        var tabs = host.Tabs.Where(id => id != fenceId).ToList();
        tabs.Insert(Math.Clamp(newIndex, 0, tabs.Count), fenceId);
        return tabs.SequenceEqual(host.Tabs, StringComparer.Ordinal) ? config : config.WithFence(host with { Tabs = tabs }); // nothing moved: the same config (M13c)
    }

    /// <summary>Shows this tab in its box (remembered across restarts).</summary>
    public static NeoFencesConfig SetActive(NeoFencesConfig config, string fenceId)
    {
        var host = HostOf(config, fenceId);
        return host.Tabs.Count < 2 || host.ActiveTab == fenceId ? config : config.WithFence(host with { ActiveTab = fenceId });
    }

    /// <summary>A tab's accent colour, or none.</summary>
    /// <summary>A swatch (or none) replaces a custom colour too (M14): the menu shows one choice.</summary>
    public static NeoFencesConfig SetColor(NeoFencesConfig config, string fenceId, TabColor? color) =>
        Find(config, fenceId) is var fence && fence.TabColor == color && fence.CustomColor is null
            ? config // unchanged: the same config (M13c)
            : config.WithFence(fence with { TabColor = color, CustomColor = null });

    /// <summary>
    /// Load-time repair (spec §2): unknown and repeated ids go; a fence listed by two boxes stays with the first; a box
    /// listed inside another is flattened into it; a list without its host gets the host first; a box left with one tab
    /// is cleared; an unknown active tab falls back to the first; an unknown colour becomes none.
    /// </summary>
    public static IReadOnlyList<Fence> Repair(IReadOnlyList<Fence> fences)
    {
        var known = fences.Select(fence => fence.Id).ToHashSet(StringComparer.Ordinal);
        var lists = fences.Where(fence => fence.Tabs.Count > 0).ToDictionary(fence => fence.Id, fence =>
        {
            var ids = fence.Tabs.Where(known.Contains).Distinct(StringComparer.Ordinal).ToList();
            if (!ids.Contains(fence.Id)) ids.Insert(0, fence.Id);
            return ids;
        });
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);
        var final = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var fence in fences)
        {
            if (!lists.TryGetValue(fence.Id, out var ids) || owner.ContainsKey(fence.Id)) continue; // absorbed by an earlier box
            var tabs = new List<string>();
            foreach (var id in ids)
            {
                if (owner.ContainsKey(id)) continue;
                AddTab(id);
                // A box listed inside this one: its tabs come along, in order (no nesting).
                if (id != fence.Id && lists.TryGetValue(id, out var nested))
                {
                    foreach (var nestedId in nested.Where(nestedId => !owner.ContainsKey(nestedId))) AddTab(nestedId);
                }
            }
            final[fence.Id] = tabs;

            void AddTab(string id)
            {
                owner[id] = fence.Id;
                tabs.Add(id);
            }
        }

        return fences.Select(fence =>
        {
            // Untouched when there is nothing to repair: records compare lists by reference (Core contract, ARCHITECTURE).
            if (fence.Tabs.Count == 0 && fence.ActiveTab is null && (fence.TabColor is null || Enum.IsDefined(fence.TabColor.Value))) return fence;
            var tabs = final.TryGetValue(fence.Id, out var list) && list.Count > 1 ? list : [];
            return fence with
            {
                Tabs = tabs,
                ActiveTab = tabs.Contains(fence.ActiveTab ?? "") ? fence.ActiveTab : null,
                TabColor = fence.TabColor is { } color && Enum.IsDefined(color) ? color : null,
            };
        }).ToList();
    }

    private static Fence? HostListing(NeoFencesConfig config, string fenceId) =>
        config.Fences.FirstOrDefault(host => host.Id != fenceId && host.Tabs.Count > 1 && host.Tabs.Contains(fenceId));

    private static Fence Find(NeoFencesConfig config, string fenceId) =>
        config.Fences.FirstOrDefault(fence => fence.Id == fenceId) ?? throw new ArgumentException($"No fence with id {fenceId}.", nameof(fenceId));

    private static NeoFencesConfig WithoutPlacements(NeoFencesConfig config, IReadOnlyCollection<string> fenceIds) =>
        config with
        {
            Layouts = config.Layouts.ToDictionary(entry => entry.Key, entry => entry.Value with
            {
                Fences = entry.Value.Fences.Where(rect => !fenceIds.Contains(rect.Key)).ToDictionary(rect => rect.Key, rect => rect.Value),
            }),
        };
}
