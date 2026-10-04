namespace NeoFences.Core.Model;

/// <summary>Everything NeoFences persists except the items (those are <c>items.json</c>, ADR-041), stored as <c>config.json</c> (ADR-006).</summary>
public sealed record NeoFencesConfig
{
    /// <summary>
    /// 5 since v0.9 (M18): virtual items, no Inbox, Portals or rules (older files start fresh, ConfigStore). 4 since M17: the
    /// auto-update switch. 3 since M14: appearance. 2 since M13a: tabs and the library. An older NeoFences reads a newer
    /// number as read-only and never saves over it (it would drop the fields).
    /// </summary>
    public const int CurrentSchemaVersion = 5;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public Settings Settings { get; init; } = new();
    public IReadOnlyList<Fence> Fences { get; init; } = [];
    public IReadOnlyDictionary<string, Layout> Layouts { get; init; } = new Dictionary<string, Layout>();

    /// <summary>Game Library settings (M12): game folders, sources, hidden games.</summary>
    public LibrarySettings Library { get; init; } = new();

    /// <summary>Fingerprint of the display configuration seen last; new configurations are derived from it.</summary>
    public string? LastLayoutFingerprint { get; init; }

    /// <summary>First run (spec §5): one empty fence; its hint says how to fill it.</summary>
    public static NeoFencesConfig CreateDefault() => new() { Fences = [Fence.Create("Fence")] };

    public NeoFencesConfig WithFence(Fence updated) =>
        this with { Fences = Fences.Select(fence => fence.Id == updated.Id ? updated : fence).ToList() };
}
