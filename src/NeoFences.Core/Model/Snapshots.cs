using NeoFences.Core.Config;

namespace NeoFences.Core.Model;

/// <summary>
/// A saved arrangement (M10, spec 2026-10-03-snapshots-design): every fence with every field (items, tabs, colours,
/// roll-up, lock) and every monitor setup's places. No Settings, no files. Stored as a file of its own.
/// </summary>
public sealed record Snapshot
{
    public int SchemaVersion { get; init; } = NeoFencesConfig.CurrentSchemaVersion;
    public string Name { get; init; } = "";
    public DateTimeOffset TakenAt { get; init; }
    public IReadOnlyList<Fence> Fences { get; init; } = [];
    public IReadOnlyDictionary<string, Layout> Layouts { get; init; } = new Dictionary<string, Layout>();
    public string? LastLayoutFingerprint { get; init; }
}

/// <summary>Taking and restoring snapshots (M10). Pure: the App supplies the desktop listing and saves the result.</summary>
public static class Snapshots
{
    private const int MenuLabelLength = 60;

    /// <summary>
    /// A snapshot as one safe tray-menu line (M13c): control characters (a hand-edited newline, a tab that would right-align
    /// the rest) become spaces, a blank name becomes its date and time, long names are cut, and "&" is not an accelerator.
    /// </summary>
    public static string MenuLabel(Config.SnapshotEntry entry, TimeZoneInfo timeZone)
    {
        var clean = string.Join(' ', new string((entry.Name ?? "").Select(character => char.IsControl(character) ? ' ' : character).ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length == 0)
            clean = "Snapshot " + TimeZoneInfo.ConvertTime(entry.TakenAt, timeZone).ToString("d MMM HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        // Never between the halves of a surrogate pair (an emoji): one character less instead (M16).
        var cut = clean.Length > MenuLabelLength && char.IsHighSurrogate(clean[MenuLabelLength - 1]) ? MenuLabelLength - 1 : MenuLabelLength;
        if (clean.Length > MenuLabelLength) clean = clean[..cut] + "…";
        return clean.Replace("&", "&&");
    }

    public static Snapshot Take(NeoFencesConfig config, string name, DateTimeOffset now) => new()
    {
        Name = name,
        TakenAt = now,
        Fences = config.Fences,
        Layouts = config.Layouts,
        LastLayoutFingerprint = config.LastLayoutFingerprint,
    };

    /// <summary>
    /// The arrangement of <paramref name="snapshot"/> applied to <paramref name="current"/> (spec §3): the snapshot's fences
    /// and places; its icons back where they were if still on the desktop (<paramref name="desktopNow"/>); icons it does
    /// not know stay in their current fence when that fence survives, else go to the Inbox; Settings and setups the
    /// snapshot never saw stay as they are.
    /// </summary>
    public static NeoFencesConfig Restore(NeoFencesConfig current, Snapshot snapshot, IEnumerable<string> desktopNow)
    {
        var desktop = desktopNow.ToList(); // the Desktop's own order for newcomers (M13a: not a HashSet's order)
        var present = desktop.ToHashSet(ItemRef.Comparer);
        // The snapshot file may be damaged or hand-edited: the normalizer gives it one Inbox, no duplicates, sane tabs.
        var saved = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = snapshot.Fences, Layouts = snapshot.Layouts });
        var fences = saved.Fences
            .Select(fence => fence.Source.Kind == FenceSourceKind.Desktop ? fence with { Items = fence.Items.Where(present.Contains).ToList() } : fence)
            .ToList();
        var placed = fences.SelectMany(fence => fence.Items).ToHashSet(ItemRef.Comparer);
        var desktopFenceIds = fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop).Select(fence => fence.Id).ToHashSet(StringComparer.Ordinal);
        var inboxId = fences.First(fence => fence.IsInbox).Id;

        // Newer icons, in their current order: their fence if it survives, else the Inbox; unfenced ones to the Inbox too.
        var arrivals = new List<(string FenceId, string ItemRef)>();
        foreach (var fence in current.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop))
        {
            foreach (var itemRef in fence.Items.Where(itemRef => present.Contains(itemRef) && placed.Add(itemRef)))
            {
                arrivals.Add((desktopFenceIds.Contains(fence.Id) ? fence.Id : inboxId, itemRef));
            }
        }
        arrivals.AddRange(desktop.Where(placed.Add).Select(itemRef => (inboxId, itemRef)));
        fences = fences.Select(fence =>
        {
            var joining = arrivals.Where(arrival => arrival.FenceId == fence.Id).Select(arrival => arrival.ItemRef).ToList();
            return joining.Count == 0 ? fence : fence with { Items = [.. fence.Items, .. joining] };
        }).ToList();

        var layouts = new Dictionary<string, Layout>(current.Layouts);
        foreach (var (fingerprint, layout) in saved.Layouts) layouts[fingerprint] = layout;
        return ConfigNormalizer.Normalize(current with { Fences = fences, Layouts = layouts });
    }
}
