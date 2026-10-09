namespace NeoFences.Core.Library;

/// <summary>A game's shortcut in NeoFences' library folder (M12).</summary>
/// <param name="Signature">What the file was written from; a changed signature means the file is rewritten.</param>
public sealed record LibraryItem(GameEntry Game, string FileName, string Signature);

/// <summary>The library folder's index (<c>library\index.json</c>): which files NeoFences wrote, for which games.</summary>
public sealed record LibraryState
{
    public IReadOnlyList<LibraryItem> Items { get; init; } = [];

    /// <summary>Hidden games with their names, so Settings can list them while their source cannot be read (M13b).</summary>
    public IReadOnlyList<HiddenEntry> Hidden { get; init; } = [];
}

/// <summary>The shortcut files to write and remove, and the next index.</summary>
public sealed record LibraryPlan(IReadOnlyList<LibraryItem> Items, IReadOnlyList<LibraryItem> Write, IReadOnlyList<string> Delete);

/// <summary>Plans the library folder from the catalog (M12, spec §3): safe, stable, unique names; only changes written.</summary>
public static class LibraryFiles
{
    private static readonly char[] Forbidden = ['\\', '/', '*', '?', '"', '<', '>', '|'];
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static LibraryPlan Plan(IReadOnlyList<LibraryItem> previous, IReadOnlyList<GameEntry> games)
    {
        var previousById = previous.GroupBy(item => item.Game.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var takenBases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // First keep the names of files that still fit their game, so a new game never takes them over.
        foreach (var game in games)
        {
            if (previousById.TryGetValue(game.Id, out var old) && Fits(old.FileName, game) && takenBases.Add(WindowsPath.FileNameWithoutExtension(old.FileName)))
                fileNames[game.Id] = old.FileName;
        }
        foreach (var game in games.Where(game => !fileNames.ContainsKey(game.Id)))
        {
            var baseName = SafeName(game.Name);
            var candidate = baseName;
            for (var counter = 2; !takenBases.Add(candidate); counter++) candidate = $"{baseName} ({counter})";
            fileNames[game.Id] = candidate + Extension(game);
        }
        var items = games.Select(game => new LibraryItem(game, fileNames[game.Id], Signature(game))).ToList();
        var write = items.Where(item => !previousById.TryGetValue(item.Game.Id, out var old) || old.Signature != item.Signature || old.FileName != item.FileName).ToList();
        var kept = items.Select(item => item.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var delete = previous.Select(item => item.FileName).Where(fileName => !kept.Contains(fileName)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new LibraryPlan(items, write, delete);
    }

    /// <summary>
    /// The index that matches the folder after <see cref="Plan"/> was carried out with some failures (M13a): a file that
    /// could not be rewritten keeps its old entry (the old file is still there, retried next time), a new file that could
    /// not be written is left out, and a file that could not be deleted stays listed (deleted next time).
    /// </summary>
    /// <param name="lostFiles">Failed writes whose file is not on disk at all (deleted by hand, then not re-created): dropped (M13b).</param>
    public static LibraryState Settle(IReadOnlyList<LibraryItem> previous, LibraryPlan plan, IReadOnlyCollection<string> failedWrites, IReadOnlyCollection<string> failedDeletes,
        IReadOnlyCollection<string> lostFiles)
    {
        var lost = lostFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var failedWrite = failedWrites.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var failedDelete = failedDeletes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previousByFile = new Dictionary<string, LibraryItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in previous) previousByFile.TryAdd(item.FileName, item);
        var items = plan.Items
            .Where(item => !lost.Contains(item.FileName))
            .Select(item => !failedWrite.Contains(item.FileName) ? item : previousByFile.GetValueOrDefault(item.FileName))
            .OfType<LibraryItem>()
            .Concat(previous.Where(item => failedDelete.Contains(item.FileName)))
            .ToList();
        return new LibraryState { Items = items };
    }

    /// <summary>
    /// A loaded index made safe (final review, re-graded minor 3): entries with missing fields, a file name that is a path
    /// (a delete must never leave the library folder, hard rule 1) or a second entry for the same file are dropped.
    /// </summary>
    public static LibraryState Repair(LibraryState state)
    {
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = (state.Items ?? [])
            .Where(item => item?.Game is { Id.Length: > 0, Name: not null, ScanKey: not null, Launch.Target.Length: > 0 }
                           && item.FileName is { Length: > 0 } fileName && fileName == WindowsPath.FileName(fileName)
                           && fileName is not ("." or "..") && fileName.IndexOfAny(['/', '\\', ':']) < 0
                           && fileNames.Add(fileName))
            .Select(item => item with { Signature = item.Signature ?? "", Game = item.Game with { OtherIds = item.Game.OtherIds ?? [] } })
            .ToList();
        var hidden = (state.Hidden ?? []).Where(entry => entry is { Ids: not null, Name: not null } && entry.Ids.All(id => id is not null)).ToList();
        return new LibraryState { Items = items, Hidden = hidden };
    }

    /// <summary>A file name for a game: forbidden characters out (":" becomes " -"), no reserved device names, at most 100 characters.</summary>
    public static string SafeName(string name)
    {
        var cleaned = new string(name.Replace(":", " -").Where(character => !char.IsControl(character) && Array.IndexOf(Forbidden, character) < 0).ToArray());
        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.');
        if (cleaned.Length > 100) cleaned = cleaned[..100].TrimEnd('.', ' ');
        if (cleaned.Length == 0) cleaned = "Game";
        return Reserved.Contains(cleaned) ? cleaned + " (game)" : cleaned;
    }

    private static string Extension(GameEntry game) => game.Launch.IsLink ? ".url" : ".lnk";

    /// <summary>The old file still names this game: "Blur.lnk", or "Blur (2).url" after a clash.</summary>
    private static bool Fits(string fileName, GameEntry game)
    {
        if (!WindowsPath.Extension(fileName).Equals(Extension(game), StringComparison.OrdinalIgnoreCase)) return false;
        var stem = WindowsPath.FileNameWithoutExtension(fileName);
        var safe = SafeName(game.Name);
        return stem.Equals(safe, StringComparison.OrdinalIgnoreCase)
            || (stem.StartsWith(safe + " (", StringComparison.OrdinalIgnoreCase) && stem.EndsWith(')') && int.TryParse(stem[(safe.Length + 2)..^1], out _));
    }

    private static string Signature(GameEntry game) =>
        string.Join('|', game.Name, game.Launch.Target, game.Launch.Arguments, game.Launch.WorkingFolder, game.Poster, game.IconPath, game.InstallFolder, game.ShortcutFile);
}
