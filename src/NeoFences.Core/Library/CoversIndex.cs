using System.Text.Json;
using NeoFences.Core.Config;

namespace NeoFences.Core.Library;

/// <summary>A game's online lookup (M34): found (<see cref="File"/> in the covers folder) or a miss (null), and when.</summary>
/// <param name="Name">The game's name when it was looked up: a renamed game is looked up again.</param>
public sealed record CoverRecord(string Name, DateTimeOffset Checked, int? SteamAppId = null, string? File = null);

/// <summary>A website's icon (M34): found (<see cref="File"/> in the covers folder's <c>sites</c>) or a miss (null), and when.</summary>
public sealed record SiteRecord(DateTimeOffset Checked, string? File = null);

/// <summary>
/// NeoFences' record of online art (M34, ADR-055): <c>covers\index.json</c> next to the files it names. Only NeoFences'
/// own data; a damaged or missing file is an empty index (the art is looked up again).
/// </summary>
public sealed record CoversIndex(IReadOnlyDictionary<string, CoverRecord> Games, IReadOnlyDictionary<string, SiteRecord> Sites)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static CoversIndex Empty { get; } = new(new Dictionary<string, CoverRecord>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, SiteRecord>(StringComparer.OrdinalIgnoreCase));

    public CoversIndex WithGame(string gameId, CoverRecord record) =>
        this with { Games = new Dictionary<string, CoverRecord>(Games, StringComparer.OrdinalIgnoreCase) { [gameId] = record } };

    public CoversIndex WithSite(string host, SiteRecord record) =>
        this with { Sites = new Dictionary<string, SiteRecord>(Sites, StringComparer.OrdinalIgnoreCase) { [host] = record } };

    public static CoversIndex Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return Empty;
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path), Options);
            if (stored is null) return Empty;
            return new CoversIndex(new Dictionary<string, CoverRecord>(stored.Games ?? [], StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, SiteRecord>(stored.Sites ?? [], StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception failure) when (failure is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Empty; // looked up again; the image files stay
        }
    }

    /// <exception cref="IOException">The file could not be written (the caller logs it; the art shows anyway).</exception>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SafeFile.Write(path, JsonSerializer.Serialize(new Stored(new(Games), new(Sites)), Options), backupPath: null);
    }

    private sealed record Stored(Dictionary<string, CoverRecord>? Games, Dictionary<string, SiteRecord>? Sites);
}
