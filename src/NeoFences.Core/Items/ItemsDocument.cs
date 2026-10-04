namespace NeoFences.Core.Items;

/// <summary>
/// Every fence's virtual items, stored as <c>items.json</c> next to <c>config.json</c> (ADR-041): a list per fence id.
/// Each tab is a fence of its own, so a box's tabs have a list each.
/// </summary>
public sealed record ItemsDocument
{
    /// <summary>An older NeoFences reads a newer number as read-only and never saves over it.</summary>
    public const int CurrentSchemaVersion = 1;

    public int Schema { get; init; } = CurrentSchemaVersion;

    public IReadOnlyDictionary<string, IReadOnlyList<VirtualItem>> Fences { get; init; } = new Dictionary<string, IReadOnlyList<VirtualItem>>();

    /// <summary>A fence's items in order; a fence with no list is empty.</summary>
    public IReadOnlyList<VirtualItem> Of(string fenceId) => Fences.TryGetValue(fenceId, out var items) ? items : [];

    public ItemsDocument With(string fenceId, IReadOnlyList<VirtualItem> items) =>
        this with { Fences = new Dictionary<string, IReadOnlyList<VirtualItem>>(Fences) { [fenceId] = items } };

    public VirtualItem? Find(string itemId) => Fences.Values.SelectMany(items => items).FirstOrDefault(item => item.Id == itemId);

    /// <summary>The fence holding this item, or null.</summary>
    public string? FenceOf(string itemId) => Fences.FirstOrDefault(entry => entry.Value.Any(item => item.Id == itemId)).Key;
}

/// <summary>What <see cref="ItemEdits.Add"/> did: the new items' ids, and the ids of items the fence already had for a target.</summary>
public sealed record ItemsAdded(ItemsDocument Document, IReadOnlyList<string> AddedIds, IReadOnlyList<string> AlreadyThereIds);

/// <summary>
/// Changes to the virtual items (spec §2, §4). Pure: each returns a new document (the same one when nothing changed).
/// None of them touches a file.
/// </summary>
public static class ItemEdits
{
    /// <summary>
    /// Adds items to a fence at <paramref name="insertAt"/> (null or past the end: at the end). A target the fence already
    /// holds is not added again (its existing item is reported, so it can flash); other fences may hold it too.
    /// </summary>
    public static ItemsAdded Add(ItemsDocument document, string fenceId, IReadOnlyList<VirtualItem> newItems, int? insertAt = null)
    {
        var items = document.Of(fenceId).ToList();
        var added = new List<VirtualItem>();
        var alreadyThere = new List<string>();
        foreach (var newItem in newItems.Where(candidate => !string.IsNullOrWhiteSpace(candidate.Target)))
        {
            if (items.FirstOrDefault(item => ItemKinds.Comparer.Equals(item.Target, newItem.Target)) is { } existing)
            {
                if (!alreadyThere.Contains(existing.Id)) alreadyThere.Add(existing.Id);
                continue;
            }
            if (added.Any(item => ItemKinds.Comparer.Equals(item.Target, newItem.Target))) continue; // the same target twice in one drop
            added.Add(newItem);
        }
        if (added.Count == 0) return new ItemsAdded(document, [], alreadyThere);
        items.InsertRange(Math.Clamp(insertAt ?? items.Count, 0, items.Count), added);
        return new ItemsAdded(document.With(fenceId, items), [.. added.Select(item => item.Id)], alreadyThere);
    }

    /// <summary>
    /// Drag-drop between or inside fences (spec §2): the items move, in the order they had, to <paramref name="toFenceId"/>
    /// before the item shown at <paramref name="insertAt"/> during the drag (the dragged items still included). Unknown ids
    /// are ignored. Every field stays.
    /// </summary>
    public static ItemsDocument Move(ItemsDocument document, IReadOnlyList<string> itemIds, string toFenceId, int insertAt)
    {
        var moving = itemIds.ToHashSet(StringComparer.Ordinal);
        var ordered = document.Fences.Values.SelectMany(items => items).Where(item => moving.Contains(item.Id)).ToList();
        if (ordered.Count == 0) return document;
        var target = document.Of(toFenceId);
        var displayedIndex = Math.Clamp(insertAt, 0, target.Count);
        var movingBeforeDrop = target.Take(displayedIndex).Count(item => moving.Contains(item.Id));
        var without = document with
        {
            Fences = document.Fences.ToDictionary(entry => entry.Key,
                entry => (IReadOnlyList<VirtualItem>)entry.Value.Where(item => !moving.Contains(item.Id)).ToList()),
        };
        var items = without.Of(toFenceId).ToList();
        items.InsertRange(displayedIndex - movingBeforeDrop, ordered);
        return without.With(toFenceId, items);
    }

    /// <summary>Ctrl+drag (spec §2): copies with new ids at <paramref name="insertAt"/>, any fence, the same one too.</summary>
    public static (ItemsDocument Document, IReadOnlyList<string> NewIds) Duplicate(ItemsDocument document, IReadOnlyList<string> itemIds,
        string toFenceId, int insertAt)
    {
        var copying = itemIds.ToHashSet(StringComparer.Ordinal);
        var copies = document.Fences.Values.SelectMany(items => items).Where(item => copying.Contains(item.Id))
            .Select(item => item with { Id = VirtualItem.NewId() }).ToList();
        if (copies.Count == 0) return (document, []);
        var items = document.Of(toFenceId).ToList();
        items.InsertRange(Math.Clamp(insertAt, 0, items.Count), copies);
        return (document.With(toFenceId, items), [.. copies.Select(copy => copy.Id)]);
    }

    /// <summary>Del / "Remove from fence": the items go; their targets are never touched.</summary>
    public static ItemsDocument Remove(ItemsDocument document, IReadOnlyCollection<string> itemIds)
    {
        if (!document.Fences.Values.Any(items => items.Any(item => itemIds.Contains(item.Id)))) return document;
        return document with
        {
            Fences = document.Fences.ToDictionary(entry => entry.Key,
                entry => (IReadOnlyList<VirtualItem>)entry.Value.Where(item => !itemIds.Contains(item.Id)).ToList()),
        };
    }

    /// <summary>Properties → OK, or Locate…: the item with the same id takes these fields. Unknown id: unchanged.</summary>
    public static ItemsDocument Replace(ItemsDocument document, VirtualItem updated)
    {
        if (document.FenceOf(updated.Id) is not { } fenceId) return document;
        return document.With(fenceId, [.. document.Of(fenceId).Select(item => item.Id == updated.Id ? updated : item)]);
    }

    /// <summary>
    /// A watched file or folder was renamed in place (spec §4): every item pointing at it, in every fence, follows, and so
    /// does every item pointing inside a renamed folder. Names, icons and notes stay.
    /// </summary>
    public static ItemsDocument Retarget(ItemsDocument document, string oldPath, string newPath)
    {
        var oldPrefix = oldPath.TrimEnd('\\') + "\\";
        string? Follow(string target) =>
            ItemKinds.Comparer.Equals(target, oldPath) ? newPath
            : target.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase) ? newPath.TrimEnd('\\') + "\\" + target[oldPrefix.Length..]
            : null;
        if (!document.Fences.Values.Any(items => items.Any(item => item.Kind == ItemKind.Path && Follow(item.Target) is not null))) return document;
        return document with
        {
            Fences = document.Fences.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<VirtualItem>)entry.Value
                .Select(item => item.Kind == ItemKind.Path && Follow(item.Target) is { } followed ? item with { Target = followed } : item).ToList()),
        };
    }

    /// <summary>Delete fence: its list goes (the targets are never touched).</summary>
    public static ItemsDocument RemoveFence(ItemsDocument document, string fenceId) =>
        !document.Fences.ContainsKey(fenceId) ? document
            : document with { Fences = document.Fences.Where(entry => entry.Key != fenceId).ToDictionary(entry => entry.Key, entry => entry.Value) };

    /// <summary>
    /// Only the lists of these fences (a snapshot restore keeps the items of the fences it brings back). Saves never prune
    /// (ADR-041 amendment): a list whose fence the config lacks stays; a fence without a list is simply empty.
    /// </summary>
    public static ItemsDocument Prune(ItemsDocument document, IReadOnlyCollection<string> fenceIds) =>
        document.Fences.Keys.All(fenceIds.Contains) ? document
            : document with { Fences = document.Fences.Where(entry => fenceIds.Contains(entry.Key)).ToDictionary(entry => entry.Key, entry => entry.Value) };

    /// <summary>"Sort by" (one time): the new order must hold exactly the fence's items.</summary>
    /// <exception cref="ArgumentException">Not a permutation of the fence's item ids.</exception>
    public static ItemsDocument Reorder(ItemsDocument document, string fenceId, IReadOnlyList<string> orderedIds)
    {
        var items = document.Of(fenceId).ToDictionary(item => item.Id, StringComparer.Ordinal);
        if (orderedIds.Count != items.Count || orderedIds.Distinct(StringComparer.Ordinal).Count() != items.Count || !orderedIds.All(items.ContainsKey))
            throw new ArgumentException("The new order must contain exactly the fence's items.", nameof(orderedIds));
        return document.With(fenceId, [.. orderedIds.Select(id => items[id])]);
    }

    /// <summary>Every file and folder target once, in fence order (for watching and checks).</summary>
    public static IReadOnlyList<string> PathTargets(ItemsDocument document) =>
        document.Fences.Values.SelectMany(items => items).Where(item => item.Kind == ItemKind.Path)
            .Select(item => item.Target).Distinct(ItemKinds.Comparer).ToList();

    /// <summary>Every target NeoFences checks, once, in fence order: files and folders, and apps (M19; never watched).</summary>
    public static IReadOnlyList<string> CheckedTargets(ItemsDocument document) =>
        document.Fences.Values.SelectMany(items => items).Where(item => item.Kind == ItemKind.Path || ItemKinds.IsApp(item.Target))
            .Select(item => item.Target).Distinct(ItemKinds.Comparer).ToList();

    /// <summary>
    /// The bulk fix after Locate… (M19 §2): these items (by id) take these targets; names, icons, arguments, notes and the
    /// order stay. Unknown ids are ignored; nothing to change: the same document.
    /// </summary>
    public static ItemsDocument Relocate(ItemsDocument document, IReadOnlyDictionary<string, string> newTargets)
    {
        if (!document.Fences.Values.Any(items => items.Any(item => newTargets.ContainsKey(item.Id)))) return document;
        return document with
        {
            Fences = document.Fences.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<VirtualItem>)entry.Value
                .Select(item => newTargets.TryGetValue(item.Id, out var target) ? item with { Target = target } : item).ToList()),
        };
    }

    /// <summary>Picture files in <c>icons\</c> some item still uses (the others are deleted at start).</summary>
    public static IReadOnlySet<string> ImagesInUse(ItemsDocument document) =>
        document.Fences.Values.SelectMany(items => items).Select(item => item.Icon?.Image).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Load-time repair of a (possibly hand-edited) file: no null lists or items, no blank targets, every id unique (a copied
    /// block gets new ids, or one edit would hit both), and an icon with neither a file nor an image is none.
    /// </summary>
    public static ItemsDocument Repair(ItemsDocument document)
    {
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var fences = new Dictionary<string, IReadOnlyList<VirtualItem>>();
        foreach (var (fenceId, items) in document.Fences ?? new Dictionary<string, IReadOnlyList<VirtualItem>>())
        {
            if (string.IsNullOrWhiteSpace(fenceId)) continue;
            fences[fenceId] = (items ?? []).Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Target))
                .Select(item => item with
                {
                    Id = string.IsNullOrWhiteSpace(item.Id) || !seenIds.Add(item.Id) ? VirtualItem.NewId() : item.Id,
                    Target = item.Target.Trim(),
                    Icon = item.Icon is { File: null or "", Image: null or "" } ? null : item.Icon,
                })
                .ToList();
        }
        return new ItemsDocument { Fences = fences };
    }
}
