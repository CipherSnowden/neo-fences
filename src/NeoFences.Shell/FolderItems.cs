using NeoFences.Core.Model;

namespace NeoFences.Shell;

/// <summary>A folder's contents (the Game Library's folder), and the facts "Sort by" needs about any target (M4).</summary>
public static class FolderItems
{
    /// <summary>Visible entries (not Hidden or System, like Explorer), or null when the folder cannot be read (missing, offline, denied).</summary>
    public static IReadOnlyList<ItemInfo>? TryList(string folderPath)
    {
        try
        {
            return new DirectoryInfo(folderPath).EnumerateFileSystemInfos()
                .Where(DesktopItems.IsVisibleOnDesktop)
                .Select(Describe)
                .ToList();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Sorting facts for targets ("Sort by", one time). Special items sort as folders by name.</summary>
    public static IReadOnlyList<ItemInfo> Describe(IEnumerable<string> itemRefs) => itemRefs.Select(itemRef =>
    {
        if (itemRef.StartsWith("::", StringComparison.Ordinal))
            return new ItemInfo(itemRef, ShellItems.TryGetDisplayName(itemRef) ?? itemRef, IsFolder: true, TypeName: "", DateTimeOffset.MinValue);
        FileSystemInfo entry = Directory.Exists(itemRef) ? new DirectoryInfo(itemRef) : new FileInfo(itemRef);
        // Keep the caller's ref: Windows path normalisation drops trailing spaces/dots ("foo "), so FullName may differ
        // and the one-time sort would then not match the fence's items (M3 review I3).
        return Describe(entry) with { ItemRef = itemRef };
    }).ToList();

    // ponytail: type = extension, not the shell's type name (SHGetFileInfo per item); upgrade if users sort by type often.
    private static ItemInfo Describe(FileSystemInfo entry)
    {
        var isFolder = entry is DirectoryInfo;
        return new ItemInfo(entry.FullName, entry.Name, isFolder, isFolder ? "" : entry.Extension.ToUpperInvariant(),
            entry.Exists ? entry.LastWriteTime : DateTimeOffset.MinValue,
            Size: entry is FileInfo { Exists: true } file ? file.Length : null); // M26: a panel's Size column (read with the listing)
    }
}

/// <summary>Says when a folder's contents changed (the library rescans, items are re-checked). Events arrive on thread-pool threads.</summary>
public sealed class FolderWatcher : IDisposable
{
    private readonly FileSystemWatcher? _watcher;
    private readonly FileSystemWatcher? _parentWatcher; // the folder itself renamed, moved or deleted (and back)

    public event Action? Changed;
    /// <summary>An entry of the folder, or the folder itself, was renamed in place: old and new full path (items follow it, M18).</summary>
    public event Action<string, string>? Renamed;
    /// <summary>The watcher lost events or stopped (a drive going away, a network hiccup): re-list and re-arm, with backoff (M8d).</summary>
    public event Action? Failed;

    /// <summary>True once the watcher failed, even before anyone subscribed (it can fail as it arms, M8d review I2).</summary>
    public bool HasFailed => _hasFailed;
    private volatile bool _hasFailed;

    /// <summary>False when the folder itself could not be watched (missing, offline): the owner then retries.</summary>
    public bool IsWatching => _watcher is not null;

    /// <summary>
    /// The folder this watcher holds open: the folder itself, or its parent while the folder is missing (M13a). A removal
    /// notice must be registered for it, or the open handle vetoes "Safely remove".
    /// </summary>
    public string? HeldFolder { get; private set; }

    /// <param name="logFailure">Told when the folder cannot be watched; its items are then only re-checked.</param>
    /// <param name="directoriesOnly">Only folders appearing, going or renamed: a game folder whose games write files at their
    /// own root would otherwise rescan the library while they run (M13b).</param>
    /// <param name="namesOnly">Only entries appearing, going or renamed (item targets): writes to files are not reported.</param>
    public FolderWatcher(string folderPath, Action<Exception> logFailure, bool directoriesOnly = false, bool namesOnly = false)
    {
        try
        {
            _watcher = new FileSystemWatcher(folderPath)
            {
                NotifyFilter = directoriesOnly ? NotifyFilters.DirectoryName
                    : namesOnly ? NotifyFilters.FileName | NotifyFilters.DirectoryName
                    : NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                InternalBufferSize = 64 * 1024,
            };
            _watcher.Created += (_, _) => Changed?.Invoke();
            _watcher.Deleted += (_, _) => Changed?.Invoke();
            _watcher.Renamed += (_, renamed) =>
            {
                Renamed?.Invoke(renamed.OldFullPath, renamed.FullPath);
                Changed?.Invoke();
            };
            _watcher.Changed += (_, _) => Changed?.Invoke();
            _watcher.Error += (_, _) =>
            {
                _hasFailed = true;
                Failed?.Invoke(); // lost events or stopped: a full re-list covers them, not too often
            };
            _watcher.EnableRaisingEvents = true;
            HeldFolder = folderPath;
        }
        catch (Exception failure) when (failure is IOException or ArgumentException or UnauthorizedAccessException)
        {
            logFailure(failure);
        }
        // A watcher follows its directory when that is renamed and reports nothing (M4 smoke). The parent tells when the
        // folder itself goes away, comes back or is renamed.
        try
        {
            var trimmed = folderPath.TrimEnd('\\', '/');
            if (Path.GetDirectoryName(trimmed) is { } parent && Directory.Exists(parent))
            {
                _parentWatcher = new FileSystemWatcher(parent, Path.GetFileName(trimmed))
                {
                    NotifyFilter = NotifyFilters.DirectoryName,
                    IncludeSubdirectories = false,
                };
                _parentWatcher.Created += (_, _) => Changed?.Invoke();
                _parentWatcher.Deleted += (_, _) => Changed?.Invoke();
                _parentWatcher.Renamed += (_, renamed) =>
                {
                    Renamed?.Invoke(renamed.OldFullPath, renamed.FullPath);
                    Changed?.Invoke();
                };
                _parentWatcher.EnableRaisingEvents = true;
                HeldFolder ??= parent;
            }
        }
        catch (Exception failure) when (failure is IOException or ArgumentException or UnauthorizedAccessException)
        {
            logFailure(failure);
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _parentWatcher?.Dispose();
    }
}
