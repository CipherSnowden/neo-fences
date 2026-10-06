using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NeoFences.Core.Config;

namespace NeoFences.Core.Items;

/// <summary>Where a cached icon came from (M37): the file's last write and length when it was cached.</summary>
public sealed record IconStamp(long LastWriteTicks, long Length);

/// <summary>One cached icon (M37): its PNG in the cache folder, the display name, the source's stamp and when it was last used.</summary>
public sealed record IconCacheEntry
{
    public string File { get; init; } = "";

    /// <summary>Windows' display name for the target, or null when the item has its own name (not asked).</summary>
    public string? Name { get; init; }

    /// <summary>Null for a target without one (a Start app, a shell item, a website, a file that was missing).</summary>
    public IconStamp? Stamp { get; init; }

    public DateTimeOffset LastUsed { get; init; }

    public long Bytes { get; init; }
}

/// <summary>
/// The icon and name cache's index (M37, ADR-058): <c>cache\icons\index.json</c> next to the PNGs it names. NeoFences' own
/// data only; a damaged, missing or newer file is an empty index (the icons load as before and the cache fills again).
/// </summary>
public sealed record IconCacheIndex
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };

    public int Version { get; init; } = CurrentVersion;

    public IReadOnlyDictionary<string, IconCacheEntry> Entries { get; init; } = new Dictionary<string, IconCacheEntry>(StringComparer.Ordinal);

    public IconCacheIndex With(string key, IconCacheEntry entry) =>
        this with { Entries = new Dictionary<string, IconCacheEntry>(Entries, StringComparer.Ordinal) { [key] = entry } };

    /// <summary>The entries of these keys were used now (keys without an entry are skipped).</summary>
    public IconCacheIndex Touch(IEnumerable<string> keys, DateTimeOffset now)
    {
        var entries = new Dictionary<string, IconCacheEntry>(Entries, StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (entries.TryGetValue(key, out var entry)) entries[key] = entry with { LastUsed = now };
        }
        return this with { Entries = entries };
    }

    /// <returns>The index (empty when there is none or it cannot be used) and why it could not be read, or null.</returns>
    public static (IconCacheIndex Index, Exception? Failure) Load(string path)
    {
        try
        {
            if (!System.IO.File.Exists(path)) return (new IconCacheIndex(), null);
            var stored = JsonSerializer.Deserialize<Stored>(System.IO.File.ReadAllText(path), Options);
            if (stored is null || stored.Version > CurrentVersion) return (new IconCacheIndex(), null); // a newer NeoFences': start over
            var entries = new Dictionary<string, IconCacheEntry>(StringComparer.Ordinal);
            foreach (var (key, entry) in stored.Entries ?? [])
            {
                // Only a PNG's own name in the cache folder counts: a hand-edited "..\x" never points elsewhere.
                if (string.IsNullOrWhiteSpace(key) || entry is null || entry.File.Length == 0 || entry.File != Path.GetFileName(entry.File)
                    || !entry.File.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                entries[key] = entry;
            }
            return (new IconCacheIndex { Entries = entries }, null);
        }
        catch (Exception failure) when (failure is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return (new IconCacheIndex(), failure);
        }
    }

    /// <exception cref="IOException">The file could not be written (the caller logs it; the icons show anyway).</exception>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SafeFile.Write(path, JsonSerializer.Serialize(new Stored(Version, new Dictionary<string, IconCacheEntry?>(Entries.Select(entry =>
            new KeyValuePair<string, IconCacheEntry?>(entry.Key, entry.Value)))), Options), backupPath: null);
    }

    private sealed record Stored(int Version, Dictionary<string, IconCacheEntry?>? Entries);
}

/// <summary>The icon cache's decisions (M37, spec §3). Pure.</summary>
public static class IconCache
{
    /// <summary>The cache stays under this size, oldest entries first.</summary>
    public const long MaxBytes = 64L * 1024 * 1024;

    /// <summary>Entries not used for this long go.</summary>
    public static readonly TimeSpan UnusedFor = TimeSpan.FromDays(30);

    /// <summary>
    /// An item's cache key (32 hex characters, also its PNG's name): its target (a file path in any case is the same), its own
    /// icon and the pixel size its fence draws it at.
    /// </summary>
    public static string KeyOf(string target, ItemIcon? ownIcon, int sizePx)
    {
        var normalized = ItemKinds.Of(target) == ItemKind.Path ? target.ToLowerInvariant() : target;
        var own = ownIcon is null ? "" : $"{ownIcon.File?.ToLowerInvariant()}|{ownIcon.Index}|{ownIcon.Image}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{normalized}\n{own}\n{sizePx}"));
        return Convert.ToHexStringLower(hash)[..32];
    }

    public static string FileNameOf(string key) => key + ".png";

    /// <summary>
    /// Whether the icon is loaded fresh behind the cached one: no entry; no stamp then or now (Start apps, shell items, websites,
    /// a missing file: always, quietly); or a source that changed since.
    /// </summary>
    public static bool NeedsFreshLoad(IconCacheEntry? entry, IconStamp? current) =>
        entry is null || entry.Stamp is null || current is null || entry.Stamp != current;

    /// <summary>
    /// The cleanup at start: entries unused for <see cref="UnusedFor"/> go, then the oldest until the rest fits in
    /// <paramref name="maxBytes"/>.
    /// </summary>
    /// <returns>The index kept and the PNG file names to delete (NeoFences' own files in its cache folder).</returns>
    public static (IconCacheIndex Kept, IReadOnlyList<string> Delete) Prune(IconCacheIndex index, DateTimeOffset now, long maxBytes = MaxBytes)
    {
        var kept = new Dictionary<string, IconCacheEntry>(StringComparer.Ordinal);
        var delete = new List<string>();
        long total = 0;
        foreach (var (key, entry) in index.Entries.OrderByDescending(entry => entry.Value.LastUsed))
        {
            if (now - entry.LastUsed > UnusedFor || total + entry.Bytes > maxBytes)
            {
                delete.Add(entry.File);
                continue;
            }
            total += entry.Bytes;
            kept[key] = entry;
        }
        return (delete.Count == 0 ? index : index with { Entries = kept }, delete);
    }
}
