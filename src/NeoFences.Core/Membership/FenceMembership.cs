using NeoFences.Core.Model;

namespace NeoFences.Core.Membership;

/// <param name="Suspicious">True when part of the desktop could not be listed (an unreadable folder, or an empty listing): the affected memberships were kept, not pruned.</param>
public sealed record ReconcileReport(IReadOnlyList<string> Removed, IReadOnlyList<string> AddedToInbox, bool Suspicious = false)
{
    /// <summary>Remembered placements this reconcile applied: the caller drops them, so they are used once (M8a review).</summary>
    public IReadOnlyList<RememberedPlacement> UsedMemories { get; init; } = [];
}

/// <summary>
/// Which desktop item lives in which fence (spec §5). Pure functions: each returns a new config.
/// Item refs compare case-insensitively (<see cref="ItemRef.Comparer"/>). Portal fences hold no items.
/// </summary>
public static class FenceMembership
{
    /// <summary>
    /// Startup reconcile: drops refs that no longer exist, adopts the shell's current spelling, keeps each
    /// item in its first fence only, and appends unknown items to the Inbox in the order given.
    /// Refs under <paramref name="unavailableFolders"/> (Desktop folders the shell layer could not list: offline
    /// redirect, unmounted OneDrive) are kept, and so is everything if the listing is empty; the report is then
    /// flagged. Everything else missing from a readable folder is dropped (M2b review: no ghosts), except:
    /// <list type="bullet">
    /// <item>a missing item whose file name now exists exactly once elsewhere, unfenced, takes that new path in place: a
    /// moved Desktop folder (OneDrive Known Folder Move) keeps every arrangement (M8a);</item>
    /// <item>an unknown item with an unexpired <paramref name="remembered"/> placement returns there instead of the Inbox:
    /// a safe-save whose watcher events were lost (M8a).</item>
    /// </list>
    /// </summary>
    public static (NeoFencesConfig Config, ReconcileReport Report) Reconcile(
        NeoFencesConfig config, IEnumerable<string> desktopItems, IReadOnlyCollection<string>? unavailableFolders = null,
        IReadOnlyList<RememberedPlacement>? remembered = null, DateTimeOffset? now = null)
    {
        var presentSpelling = new Dictionary<string, string>(ItemRef.Comparer);
        var presentOrder = new List<string>();
        foreach (var desktopItem in desktopItems)
        {
            if (presentSpelling.TryAdd(desktopItem, desktopItem)) presentOrder.Add(desktopItem);
        }

        var unlistedPrefixes = (unavailableFolders ?? []).Select(folder => folder.TrimEnd('\\') + "\\").ToList();
        bool MayStillExist(string itemRef) =>
            presentOrder.Count == 0 || unlistedPrefixes.Any(prefix => itemRef.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        var suspicious = false;

        // Present but not yet fenced, by file name: candidates for an item whose folder moved.
        var fencedPresent = config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop)
            .SelectMany(fence => fence.Items).Where(presentSpelling.ContainsKey).ToHashSet(ItemRef.Comparer);
        var unfencedByName = presentOrder
            .Where(itemRef => !fencedPresent.Contains(itemRef) && !itemRef.StartsWith("::", StringComparison.Ordinal))
            .GroupBy(itemRef => Path.GetFileName(itemRef), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var placed = new HashSet<string>(ItemRef.Comparer);
        var removed = new List<string>();
        var fences = config.Fences.Select(fence =>
        {
            if (fence.Source.Kind != FenceSourceKind.Desktop) return fence;
            var kept = new List<string>();
            foreach (var itemRef in fence.Items)
            {
                if (presentSpelling.TryGetValue(itemRef, out var currentSpelling))
                {
                    if (placed.Add(currentSpelling)) kept.Add(currentSpelling);
                }
                else if (MayStillExist(itemRef))
                {
                    suspicious = true;
                    if (placed.Add(itemRef)) kept.Add(itemRef);
                }
                else if (!itemRef.StartsWith("::", StringComparison.Ordinal)
                         && unfencedByName.TryGetValue(Path.GetFileName(itemRef), out var candidates)
                         && candidates.Count == 1 && placed.Add(candidates[0]))
                {
                    kept.Add(candidates[0]); // same name, new folder: the same item moved (an ambiguous name is never guessed)
                }
                else
                {
                    removed.Add(itemRef);
                }
            }
            return fence with { Items = kept };
        }).ToList();

        var reconciled = config with { Fences = fences };
        var live = (remembered ?? []).Where(placement => now is null || placement.ExpiresAt >= now).ToList();
        var added = new List<string>();
        var returning = new List<RememberedPlacement>();
        foreach (var itemRef in presentOrder.Where(itemRef => !placed.Contains(itemRef)))
        {
            var memory = live.LastOrDefault(placement => ItemRef.Comparer.Equals(placement.ItemRef, itemRef));
            if (memory is not null && reconciled.Fences.Any(fence => fence.Id == memory.FenceId && fence.Source.Kind == FenceSourceKind.Desktop))
            {
                returning.Add(memory with { ItemRef = itemRef }); // the shell's current spelling
            }
            else
            {
                added.Add(itemRef);
            }
        }
        // By index, not listing order: several arrivals for one fence land where they were dropped (M8a review).
        foreach (var memory in returning.OrderBy(memory => memory.Index))
        {
            var home = reconciled.Fences.First(fence => fence.Id == memory.FenceId);
            var items = home.Items.ToList();
            items.Insert(Math.Clamp(memory.Index, 0, items.Count), memory.ItemRef);
            reconciled = reconciled.WithFence(home with { Items = items });
        }
        var usedMemories = live.Where(memory => returning.Any(used => ItemRef.Comparer.Equals(used.ItemRef, memory.ItemRef))).ToList();
        if (added.Count > 0)
        {
            reconciled = reconciled.WithFence(reconciled.Inbox with { Items = [.. reconciled.Inbox.Items, .. added] });
        }
        return (reconciled, new ReconcileReport(removed, added, suspicious) { UsedMemories = usedMemories });
    }

    /// <summary>Applies one watcher event: created → Inbox, deleted → removed, renamed → same fence and position.</summary>
    public static NeoFencesConfig Apply(NeoFencesConfig config, DesktopChange change) => change switch
    {
        DesktopChange.Created created => AddItem(config, created.ItemRef),
        DesktopChange.Deleted deleted => RemoveItem(config, deleted.ItemRef),
        DesktopChange.Renamed renamed => RenameItem(config, renamed.OldRef, renamed.NewRef),
        _ => throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown desktop change."),
    };

    /// <summary>How long a removed item's fence and position are remembered, so a replace-save puts it back (M3a).</summary>
    public static readonly TimeSpan SafeSaveWindow = TimeSpan.FromSeconds(5);

    /// <summary>How long files dropped from Explorer are expected: Windows creates each when it starts copying it (M3b review I1).</summary>
    public static readonly TimeSpan ArrivalWindow = TimeSpan.FromMinutes(3);

    /// <summary>
    /// <see cref="Apply(NeoFencesConfig, DesktopChange)"/> that also survives "safe saves": editors replace a file by
    /// deleting (or renaming away) the original and creating (or renaming a temp file onto) the same name. A fenced item
    /// that disappears is remembered for <see cref="SafeSaveWindow"/>; if the same ref comes back in that time it returns
    /// to its fence and position instead of the Inbox. Keep the returned list and pass it to the next call.
    /// </summary>
    public static (NeoFencesConfig Config, IReadOnlyList<RememberedPlacement> Recent) Apply(
        NeoFencesConfig config, DesktopChange change, IReadOnlyList<RememberedPlacement> recent, DateTimeOffset now)
    {
        var remembered = recent.Where(placement => placement.ExpiresAt >= now).ToList();
        switch (change)
        {
            case DesktopChange.Deleted deleted when FindOwner(config, deleted.ItemRef) is { } deletedOwner:
                var index = deletedOwner.Items.ToList().FindIndex(existing => ItemRef.Comparer.Equals(existing, deleted.ItemRef));
                remembered.Add(new RememberedPlacement(deleted.ItemRef, deletedOwner.Id, index, now + SafeSaveWindow));
                return (RemoveItem(config, deleted.ItemRef), remembered);
            // A temp file renamed onto a name that just disappeared (Excel, LibreOffice, atomic writers): the temp may
            // already sit in the Inbox from its Created event; drop it and put the returning name back (M3a review I1).
            case DesktopChange.Renamed renamed
                when FindOwner(config, renamed.NewRef) is null
                     && remembered.Any(removal => ItemRef.Comparer.Equals(removal.ItemRef, renamed.NewRef)):
                return (Return(RemoveItem(config, renamed.OldRef), renamed.NewRef, remembered), remembered);
            case DesktopChange.Renamed renamed when FindOwner(config, renamed.OldRef) is { } owner:
                // Word renames the original away before renaming its temp file onto the old name: remember the old name too.
                var oldIndex = owner.Items.ToList().FindIndex(existing => ItemRef.Comparer.Equals(existing, renamed.OldRef));
                remembered.Add(new RememberedPlacement(renamed.OldRef, owner.Id, oldIndex, now + SafeSaveWindow));
                return (RenameItem(config, renamed.OldRef, renamed.NewRef), remembered);
            case DesktopChange.Created created when FindOwner(config, created.ItemRef) is null:
                return (Return(config, created.ItemRef, remembered), remembered);
            case DesktopChange.Renamed renamed when FindOwner(config, renamed.OldRef) is null && FindOwner(config, renamed.NewRef) is null:
                return (Return(config, renamed.NewRef, remembered), remembered);
            default:
                return (Apply(config, change), remembered);
        }
    }

    /// <summary>Puts an appearing item back where it was removed from moments ago, or in the Inbox. Consumes the memory.</summary>
    private static NeoFencesConfig Return(NeoFencesConfig config, string itemRef, List<RememberedPlacement> remembered)
    {
        var removal = remembered.FindLast(candidate => ItemRef.Comparer.Equals(candidate.ItemRef, itemRef));
        if (removal is null) return AddItem(config, itemRef);
        remembered.Remove(removal);
        var fence = config.Fences.FirstOrDefault(candidate => candidate.Id == removal.FenceId);
        return fence is { Source.Kind: FenceSourceKind.Desktop }
            ? MoveItem(config, itemRef, fence.Id, removal.Index)
            : AddItem(config, itemRef);
    }

    /// <summary>A new desktop item appeared: goes to <paramref name="targetFenceId"/> (a drop target) or the Inbox. No-op if already fenced.</summary>
    public static NeoFencesConfig AddItem(NeoFencesConfig config, string itemRef, string? targetFenceId = null)
    {
        if (FindOwner(config, itemRef) is not null) return config;
        var target = targetFenceId is null ? config.Inbox : RequireDesktopFence(config, targetFenceId);
        return config.WithFence(target with { Items = [.. target.Items, itemRef] });
    }

    /// <summary>A desktop item was deleted.</summary>
    public static NeoFencesConfig RemoveItem(NeoFencesConfig config, string itemRef)
    {
        var owner = FindOwner(config, itemRef);
        return owner is null
            ? config
            : config.WithFence(owner with { Items = owner.Items.Where(existing => !ItemRef.Comparer.Equals(existing, itemRef)).ToList() });
    }

    /// <summary>A desktop item was renamed (including case-only renames). Unknown old ref: treated as a new item.</summary>
    public static NeoFencesConfig RenameItem(NeoFencesConfig config, string oldRef, string newRef)
    {
        var owner = FindOwner(config, oldRef);
        if (owner is null) return AddItem(config, newRef);
        // New name already fenced (an event storm can deliver its create before the rename): keep that entry only.
        if (!ItemRef.Comparer.Equals(oldRef, newRef) && FindOwner(config, newRef) is not null) return RemoveItem(config, oldRef);
        return config.WithFence(owner with
        {
            Items = owner.Items.Select(existing => ItemRef.Comparer.Equals(existing, oldRef) ? newRef : existing).ToList(),
        });
    }

    /// <summary>
    /// Drag-drop of fence items (M3b). Moves them, in the order they had in their fences (not the selection order), to
    /// <paramref name="toFenceId"/>, before the item shown at <paramref name="insertAt"/> in the target as displayed
    /// during the drag (still including the dragged items). Past the end appends; unknown refs are ignored.
    /// </summary>
    /// <exception cref="ArgumentException">The target is unknown or a Portal fence.</exception>
    public static NeoFencesConfig MoveItems(NeoFencesConfig config, IReadOnlyList<string> itemRefs, string toFenceId, int insertAt)
    {
        var target = RequireDesktopFence(config, toFenceId);
        var moving = new HashSet<string>(itemRefs, ItemRef.Comparer);
        var ordered = config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop)
            .SelectMany(fence => fence.Items).Where(moving.Contains).ToList();
        if (ordered.Count == 0) return config;

        var displayedIndex = Math.Clamp(insertAt, 0, target.Items.Count);
        var movingBeforeDrop = target.Items.Take(displayedIndex).Count(moving.Contains);
        var withoutMoving = config with
        {
            Fences = config.Fences.Select(fence => fence.Source.Kind == FenceSourceKind.Desktop
                ? fence with { Items = fence.Items.Where(itemRef => !moving.Contains(itemRef)).ToList() }
                : fence).ToList(),
        };
        var remainingTarget = withoutMoving.Fences.First(fence => fence.Id == toFenceId);
        var items = remainingTarget.Items.ToList();
        items.InsertRange(displayedIndex - movingBeforeDrop, ordered);
        return withoutMoving.WithFence(remainingTarget with { Items = items });
    }

    /// <summary>
    /// Files were dropped onto a fence from outside (Explorer) and Windows is copying or moving them to the Desktop:
    /// when each appears within <see cref="ArrivalWindow"/> it goes to that fence at the drop position, in order.
    /// </summary>
    public static IReadOnlyList<RememberedPlacement> ExpectArrivals(
        IReadOnlyList<RememberedPlacement> recent, IReadOnlyList<string> itemRefs, string fenceId, int insertAt, DateTimeOffset now) =>
        [.. recent.Where(placement => placement.ExpiresAt >= now),
         .. itemRefs.Select((itemRef, offset) => new RememberedPlacement(itemRef, fenceId, insertAt + offset, now + ArrivalWindow))];

    /// <summary>Moves an item to another desktop fence (or reorders within one). Index is clamped; null appends.</summary>
    public static NeoFencesConfig MoveItem(NeoFencesConfig config, string itemRef, string toFenceId, int? index = null)
    {
        var target = RequireDesktopFence(config, toFenceId);
        var withoutItem = RemoveItem(config, itemRef);
        target = withoutItem.Fences.First(fence => fence.Id == toFenceId);
        var items = target.Items.ToList();
        items.Insert(Math.Clamp(index ?? items.Count, 0, items.Count), itemRef);
        return withoutItem.WithFence(target with { Items = items });
    }

    public static (NeoFencesConfig Config, Fence Fence) CreateFence(NeoFencesConfig config, string title, FenceSource? source = null)
    {
        var fence = Fence.Create(title, source) with { Labels = config.Settings.DefaultLabels };
        return (config with { Fences = [.. config.Fences, fence] }, fence);
    }

    /// <summary>A Portal: a fence that shows a folder live (M4). New Portals sort newest first (user choice 2026-10-03).</summary>
    /// <exception cref="ArgumentException">No folder given.</exception>
    public static (NeoFencesConfig Config, Fence Fence) CreatePortal(NeoFencesConfig config, string title, string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) throw new ArgumentException("A Portal needs a folder.", nameof(folderPath));
        var portal = Fence.Create(title, FenceSource.Portal(folderPath)) with { Sort = FenceSort.Date, Labels = config.Settings.DefaultLabels };
        return (config with { Fences = [.. config.Fences, portal] }, portal);
    }

    /// <summary>Deletes a fence; its items go to the Inbox. Files are never touched (hard rule 1).</summary>
    /// <exception cref="InvalidOperationException">The fence is the Inbox.</exception>
    public static NeoFencesConfig DeleteFence(NeoFencesConfig config, string fenceId)
    {
        var fence = config.Fences.FirstOrDefault(candidate => candidate.Id == fenceId)
                    ?? throw new ArgumentException($"No fence with id {fenceId}.", nameof(fenceId));
        if (fence.IsInbox) throw new InvalidOperationException("The Inbox cannot be deleted.");

        config = FenceTabs.Leave(config, fenceId); // a tab leaves its box first; a host hands the box to the next tab (M9)
        var remaining = config with { Fences = config.Fences.Where(candidate => candidate.Id != fenceId).ToList() };
        return remaining.WithFence(remaining.Inbox with { Items = [.. remaining.Inbox.Items, .. fence.Items] });
    }

    private static Fence? FindOwner(NeoFencesConfig config, string itemRef) =>
        config.Fences.FirstOrDefault(fence => fence.Items.Contains(itemRef, ItemRef.Comparer));

    private static Fence RequireDesktopFence(NeoFencesConfig config, string fenceId)
    {
        var fence = config.Fences.FirstOrDefault(candidate => candidate.Id == fenceId)
                    ?? throw new ArgumentException($"No fence with id {fenceId}.", nameof(fenceId));
        return fence.Source.Kind == FenceSourceKind.Desktop
            ? fence
            : throw new ArgumentException("Portal fences hold no items; moving into them is a file operation.", nameof(fenceId));
    }
}
