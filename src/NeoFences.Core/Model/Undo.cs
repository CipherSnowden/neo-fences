using NeoFences.Core.Items;

namespace NeoFences.Core.Model;

/// <summary>One undoable change (M33, ADR-054): the label shown in the undo bar and the tray.</summary>
public abstract record UndoEntry(string Label);

/// <summary>An item that was removed, and where it was.</summary>
public sealed record RemovedItem(string FenceId, int Index, VirtualItem Item);

/// <summary>Items removed from fences (Del, Remove from fence).</summary>
public sealed record ItemsRemoved(string Label, IReadOnlyList<RemovedItem> Items) : UndoEntry(Label);

/// <summary>A fence deleted: the fence, its place in every display setup, and its items.</summary>
public sealed record FenceDeleted(string Label, Fence Fence, IReadOnlyDictionary<string, FenceRect> Places, IReadOnlyList<VirtualItem> Items)
    : UndoEntry(Label);

/// <summary>
/// One level of undo (M33, ADR-054, spec 2026-10-06-safety-net-design §2). Undo puts back only what the change took away:
/// later changes stay; a fence that is gone since gets nothing back; a deleted tab comes back as its own fence at its
/// box's place. Pure.
/// </summary>
public static class Undo
{
    public static ItemsRemoved ForRemoval(ItemsDocument items, IReadOnlyCollection<string> itemIds)
    {
        var removed = new List<RemovedItem>();
        foreach (var (fenceId, list) in items.Fences)
        {
            for (var index = 0; index < list.Count; index++)
            {
                if (itemIds.Contains(list[index].Id)) removed.Add(new RemovedItem(fenceId, index, list[index]));
            }
        }
        return new ItemsRemoved(removed.Count == 1 ? "Removed 1 item" : $"Removed {removed.Count} items", removed);
    }

    public static FenceDeleted ForDeletion(NeoFencesConfig config, ItemsDocument items, string fenceId)
    {
        var fence = config.Fences.First(candidate => candidate.Id == fenceId);
        var placeOf = FenceTabs.HostOf(config, fenceId).Id; // a tab sits at its box's place
        var places = config.Layouts
            .Where(layout => layout.Value.Fences.ContainsKey(placeOf))
            .ToDictionary(layout => layout.Key, layout => layout.Value.Fences[placeOf]);
        return new FenceDeleted($"Deleted fence {fence.Title}", fence, places, items.Of(fenceId));
    }

    public static (NeoFencesConfig Config, ItemsDocument Items) Apply(NeoFencesConfig config, ItemsDocument items, UndoEntry entry) => entry switch
    {
        ItemsRemoved removed => (config, PutBack(config, items, removed)),
        FenceDeleted deleted => Restore(config, items, deleted),
        _ => (config, items),
    };

    private static ItemsDocument PutBack(NeoFencesConfig config, ItemsDocument items, ItemsRemoved removed)
    {
        foreach (var group in removed.Items.GroupBy(item => item.FenceId))
        {
            if (config.Fences.All(fence => fence.Id != group.Key)) continue; // its fence is gone
            var list = items.Of(group.Key).ToList();
            foreach (var item in group.OrderBy(item => item.Index))
            {
                if (list.Any(existing => existing.Id == item.Item.Id)) continue;
                list.Insert(Math.Min(item.Index, list.Count), item.Item);
            }
            items = items.With(group.Key, list);
        }
        return items;
    }

    private static (NeoFencesConfig, ItemsDocument) Restore(NeoFencesConfig config, ItemsDocument items, FenceDeleted deleted)
    {
        if (config.Fences.Any(fence => fence.Id == deleted.Fence.Id)) return (config, items);
        var fence = deleted.Fence with { Tabs = [], ActiveTab = null };
        var layouts = config.Layouts.ToDictionary(layout => layout.Key, layout =>
            deleted.Places.TryGetValue(layout.Key, out var place) && !layout.Value.Fences.ContainsKey(fence.Id)
                ? layout.Value with { Fences = new Dictionary<string, FenceRect>(layout.Value.Fences) { [fence.Id] = place } }
                : layout.Value);
        var restored = config with { Fences = [.. config.Fences, fence], Layouts = layouts };
        return (restored, items.Of(fence.Id).Count == 0 ? items.With(fence.Id, deleted.Items) : items);
    }
}
