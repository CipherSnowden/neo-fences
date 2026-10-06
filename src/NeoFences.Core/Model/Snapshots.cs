using NeoFences.Core.Config;
using NeoFences.Core.Items;

namespace NeoFences.Core.Model;

/// <summary>
/// A saved arrangement (M10, spec 2026-10-03-snapshots-design): every fence with every field (tabs, colours, roll-up,
/// lock), every fence's virtual items (M18) and every monitor setup's places. No Settings, no files. Stored as a file of its own.
/// </summary>
public sealed record Snapshot
{
    public int SchemaVersion { get; init; } = NeoFencesConfig.CurrentSchemaVersion;
    public string Name { get; init; } = "";
    public DateTimeOffset TakenAt { get; init; }
    public IReadOnlyList<Fence> Fences { get; init; } = [];
    public IReadOnlyDictionary<string, Layout> Layouts { get; init; } = new Dictionary<string, Layout>();
    public string? LastLayoutFingerprint { get; init; }

    /// <summary>Every fence's items (M18), as in items.json.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<VirtualItem>> Items { get; init; } = new Dictionary<string, IReadOnlyList<VirtualItem>>();
}

/// <summary>Taking and restoring snapshots (M10). Pure: the App saves the result.</summary>
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
        return clean; // M35: shown by a WPF menu, which escapes its own access keys (no Win32 "&&")
    }

    public static Snapshot Take(NeoFencesConfig config, ItemsDocument items, string name, DateTimeOffset now) => new()
    {
        Name = name,
        TakenAt = now,
        Fences = config.Fences,
        Layouts = config.Layouts,
        LastLayoutFingerprint = config.LastLayoutFingerprint,
        Items = items.Fences,
    };

    /// <summary>
    /// The arrangement of <paramref name="snapshot"/> applied to <paramref name="current"/> (spec §3; M18: items as saved):
    /// the snapshot's fences, places and items; Settings and setups the snapshot never saw stay as they are. Items of
    /// fences the snapshot does not have are dropped (a damaged or hand-edited file). Auto-collect rules start looking at
    /// <paramref name="restoredAt"/> (M27 final review I4): files from the snapshot's time on are not collected again.
    /// </summary>
    public static (NeoFencesConfig Config, ItemsDocument Items) Restore(NeoFencesConfig current, Snapshot snapshot, DateTimeOffset? restoredAt = null)
    {
        // The snapshot file may be damaged or hand-edited: the normalizer gives it unique ids and sane tabs.
        var saved = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = snapshot.Fences, Layouts = snapshot.Layouts });
        var layouts = new Dictionary<string, Layout>(current.Layouts);
        foreach (var (fingerprint, layout) in saved.Layouts) layouts[fingerprint] = layout;
        var config = ConfigNormalizer.Normalize(current with { Fences = saved.Fences, Layouts = layouts });
        if (restoredAt is { } now)
            config = config with { Fences = [.. config.Fences.Select(fence => fence.Collect.Count == 0 ? fence : fence with { Collect = [.. fence.Collect.Select(rule => rule with { Watermark = now })] })] };
        var items = ItemEdits.Repair(new ItemsDocument { Fences = snapshot.Items ?? new Dictionary<string, IReadOnlyList<VirtualItem>>() });
        return (config, ItemEdits.Prune(items, config.Fences.Select(fence => fence.Id).ToHashSet(StringComparer.Ordinal)));
    }
}
