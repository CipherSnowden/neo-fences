using System.Text.RegularExpressions;

namespace NeoFences.Core.Library;

/// <summary>Where a game was found. The order is the merge priority: launchers, then Xbox, then shortcuts, then folders.</summary>
public enum GameSource { Steam, Epic, Gog, Ubisoft, Ea, BattleNet, Xbox, DesktopShortcut, Folder }

/// <summary>How a game starts: a link (<c>steam://…</c>) or a program with arguments.</summary>
public sealed record GameLaunch(string Target, string? Arguments = null, string? WorkingFolder = null)
{
    public bool IsLink => Target.Contains("://", StringComparison.Ordinal);
}

/// <summary>One game in the library (M12).</summary>
/// <param name="Id">Stable across scans: "steam:1222140", "folder:d:\gamelibrary\blur" (the hidden list uses it).</param>
/// <param name="ScanKey">The scan that found it ("steam", "folder:d:\gamelibrary"): its games are kept while that scan cannot read.</param>
/// <param name="Poster">A 2:3 cover image on disk, when the launcher keeps one.</param>
/// <param name="IconPath">A file whose icon stands for the game (its program, a package logo).</param>
public sealed record GameEntry(string Id, string Name, GameSource Source, string ScanKey, GameLaunch Launch,
    string? InstallFolder = null, string? Poster = null, string? IconPath = null)
{
    /// <summary>A Desktop shortcut's own file: the library copies it, keeping its icon and "Run as administrator" (M13b).</summary>
    public string? ShortcutFile { get; init; }

    /// <summary>Ids of the same game found by weaker sources (merged into this one): hiding covers them too.</summary>
    public IReadOnlyList<string> OtherIds { get; init; } = [];
}

/// <summary>A hidden game as Settings lists it: every id it goes by, and its name (M13b).</summary>
public sealed record HiddenEntry(IReadOnlyList<string> Ids, string Name);

/// <summary>What one scan found; <paramref name="Readable"/> false = the source could not be read this time (keep its games).</summary>
public sealed record SourceScan(string ScanKey, bool Readable, IReadOnlyList<GameEntry> Games);

/// <summary>Merges what the sources found into one A–Z list, one entry per game (M12, spec §2). Pure.</summary>
public static partial class GameCatalog
{
    [GeneratedRegex(@"redistributable|steamworks common|^proton\b|steam linux runtime|\bsdk\b|dedicated server", RegexOptions.IgnoreCase)]
    private static partial Regex ToolName();

    public static IReadOnlyList<GameEntry> Merge(IReadOnlyList<SourceScan> scans, IReadOnlyList<GameEntry> previous, IReadOnlyCollection<string> hidden)
    {
        var found = new List<GameEntry>();
        foreach (var scan in scans)
        {
            // An unreadable source keeps its games, also those of its parts ("steam" keeps "steam:d:\steamlibrary"; final review I1).
            found.AddRange(scan.Readable ? scan.Games : previous.Where(game => game.ScanKey == scan.ScanKey
                || game.ScanKey.StartsWith(scan.ScanKey + ":", StringComparison.Ordinal)));
        }
        var kept = new List<GameEntry>();
        foreach (var game in found.Where(game => !IsTool(game.Name)).OrderBy(game => game.Source))
        {
            var index = kept.FindIndex(existing => SameGame(existing, game));
            if (index < 0)
            {
                kept.Add(game);
                continue;
            }
            var winner = kept[index]; // stronger source: keeps its name and launch, borrows what it lacks
            kept[index] = winner with
            {
                InstallFolder = winner.InstallFolder ?? game.InstallFolder,
                Poster = winner.Poster ?? game.Poster,
                IconPath = winner.IconPath ?? game.IconPath,
                OtherIds = [.. winner.OtherIds.Concat(IdsOf(game)).Distinct(StringComparer.OrdinalIgnoreCase).Where(id => !string.Equals(id, winner.Id, StringComparison.OrdinalIgnoreCase))],
            };
        }
        var hiddenIds = hidden.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return kept.Where(game => !IdsOf(game).Any(hiddenIds.Contains))
            .OrderBy(game => SortKey(game.Name), StringComparer.CurrentCultureIgnoreCase).ThenBy(game => game.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Every id a game is known by: its own and those of the duplicates merged into it.</summary>
    public static IReadOnlyList<string> IdsOf(GameEntry game) => [game.Id, .. game.OtherIds];

    /// <summary>Launcher tools that are not games (redistributables, Proton, SDKs, dedicated servers).</summary>
    public static bool IsTool(string name) => ToolName().IsMatch(name);

    /// <summary>A–Z ignoring a leading "The ".</summary>
    public static string SortKey(string name)
    {
        var trimmed = name.Trim();
        return trimmed.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? trimmed[4..] : trimmed;
    }

    /// <param name="existing">A game already kept (a stronger source).</param>
    /// <param name="incoming">The game being merged in (a weaker source).</param>
    private static bool SameGame(GameEntry existing, GameEntry incoming) =>
        (existing.InstallFolder is { } existingFolder && incoming.InstallFolder is { } incomingFolder && NormalizeFolder(existingFolder) == NormalizeFolder(incomingFolder))
        || LaunchKey(existing.Launch) == LaunchKey(incoming.Launch)
        // One way only: the weaker entry's program inside the stronger one's folder. Both ways let a launcher's shortcut
        // (GalaxyClient.exe /gameId=…) swallow the first game whose program is that launcher (final review I2).
        || StartsInside(incoming, existing);

    /// <summary>
    /// The program of <paramref name="game"/> lies inside <paramref name="other"/>'s install folder: a Desktop shortcut to
    /// "…\steamapps\common\Dota 2\game\bin\dota2.exe" is the Steam game (M13b). Programs only, never folders: a
    /// shortcut to steam.exe must not swallow a game installed under Steam's folder.
    /// </summary>
    private static bool StartsInside(GameEntry game, GameEntry other) =>
        !game.Launch.IsLink && other.InstallFolder is { } folder
        && NormalizeFolder(game.Launch.Target).StartsWith(NormalizeFolder(folder) + "\\", StringComparison.Ordinal);

    /// <summary>A scan key as people say it, for the status line: "Steam library d:\steamlibrary", "game folder d:\gamelibrary".</summary>
    public static string SourceName(string scanKey)
    {
        var colon = scanKey.IndexOf(':');
        var (kind, detail) = colon < 0 ? (scanKey, "") : (scanKey[..colon], scanKey[(colon + 1)..]);
        return (kind, detail) switch
        {
            ("steam", "") => "Steam",
            ("steam", _) => $"Steam library {detail}",
            ("folder", _) => $"game folder {detail}",
            ("epic", _) => "Epic",
            ("gog", _) => "GOG",
            ("ubisoft", _) => "Ubisoft Connect",
            ("ea", _) => "EA app",
            ("battlenet", _) => "Battle.net",
            ("xbox", _) => "Xbox",
            ("desktop", _) => "Desktop shortcuts",
            ("library", _) => "the library",
            _ => scanKey,
        };
    }

    /// <summary>
    /// The hidden games for Settings, one per game with every id it goes by and its name (M13b): found now, else as
    /// remembered from before (its source cannot be read right now), else by its id alone.
    /// </summary>
    public static IReadOnlyList<HiddenEntry> HiddenGames(IReadOnlyList<GameEntry> everything, IReadOnlyCollection<string> hidden, IReadOnlyList<HiddenEntry> previous)
    {
        var hiddenIds = hidden.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<HiddenEntry>();
        foreach (var game in everything.Where(game => IdsOf(game).Any(hiddenIds.Contains)))
        {
            // Found by only some of its sources (D: away): the ids remembered for it stay with it (final review I1).
            var ids = IdsOf(game).ToList();
            foreach (var entry in previous.Where(entry => entry.Ids.Any(id => ids.Contains(id, StringComparer.OrdinalIgnoreCase))))
                ids.AddRange(entry.Ids.Where(id => !ids.Contains(id, StringComparer.OrdinalIgnoreCase)));
            entries.Add(new HiddenEntry(ids, game.Name));
            covered.UnionWith(ids);
        }
        foreach (var entry in previous.Where(entry => entry.Ids.Any(hiddenIds.Contains) && !entry.Ids.Any(covered.Contains)))
        {
            entries.Add(entry);
            covered.UnionWith(entry.Ids);
        }
        entries.AddRange(hidden.Where(id => covered.Add(id)).Select(id => new HiddenEntry([id], id)));
        return entries;
    }

    private static string NormalizeFolder(string folder) => folder.Trim().TrimEnd('\\', '/').ToLowerInvariant();

    /// <summary>A link up to its query ("?action=launch"), or a program with its arguments; case-insensitive.</summary>
    private static string LaunchKey(GameLaunch launch)
    {
        var target = launch.Target.Trim();
        if (launch.IsLink)
        {
            var query = target.IndexOf('?');
            return (query >= 0 ? target[..query] : target).ToLowerInvariant();
        }
        return $"{target} {launch.Arguments?.Trim()}".Trim().ToLowerInvariant();
    }
}

/// <summary>Which program in a game folder starts the game (M12, spec §2), when no shortcut says.</summary>
public static partial class ProgramPicker
{
    [GeneratedRegex(@"^(_?redist|redist.*|installers?|__installer|directx|dotnet|vcredist|support|commonredist|easyanticheat|battleye|nodvd)$", RegexOptions.IgnoreCase)]
    private static partial Regex SkippedFolder();

    [GeneratedRegex(@"^(unins|setup|vc_|dxsetup|easyanticheat)|crash|redist|report|helper|install", RegexOptions.IgnoreCase)]
    private static partial Regex SkippedProgram();

    /// <summary>The largest program that is not an installer, uninstaller, crash reporter or redistributable; null when none.</summary>
    /// <param name="programs">Paths relative to the game folder, with their size in bytes.</param>
    public static string? Pick(IEnumerable<(string RelativePath, long Size)> programs) =>
        programs
            .Where(program =>
            {
                var parts = program.RelativePath.Split('\\', '/');
                return !parts[..^1].Any(SkippedFolder().IsMatch) && !SkippedProgram().IsMatch(Path.GetFileNameWithoutExtension(parts[^1]));
            })
            .OrderByDescending(program => program.Size).ThenBy(program => program.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(program => program.RelativePath)
            .FirstOrDefault();
}
