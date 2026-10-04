namespace NeoFences.Core.Items;

/// <summary>Where an item's target is now (spec §4). Websites and special items are always <see cref="Ok"/>.</summary>
public enum TargetState { Ok, Missing, Unavailable }

/// <param name="IsFolder">The target is a folder (Arguments and "Run as administrator" do not apply).</param>
public sealed record TargetCheck(TargetState State, bool IsFolder = false)
{
    public static TargetCheck Ok { get; } = new(TargetState.Ok);
}

/// <summary>Target states from what the disk says (the probing, with its timeout, is NeoFences.Shell's).</summary>
public static class TargetChecks
{
    /// <summary>
    /// A path that exists is Ok; one whose drive or share root does not answer is Unavailable (a USB stick not plugged
    /// in, a sleeping NAS), otherwise Missing (deleted, moved away, or its folder renamed).
    /// </summary>
    /// <param name="fileExists">True when a file is at the path.</param>
    /// <param name="folderExists">True when a folder is at the path (also asked for the root).</param>
    public static TargetCheck Classify(string target, Func<string, bool> fileExists, Func<string, bool> folderExists)
    {
        if (ItemKinds.Of(target) != ItemKind.Path) return TargetCheck.Ok;
        // A launcher link (steam://rungameid/570) or a relative path: nothing on a disk to check, Windows opens it.
        if (RootOf(target) is not { } root) return TargetCheck.Ok;
        if (folderExists(target)) return new TargetCheck(TargetState.Ok, IsFolder: true);
        if (fileExists(target)) return TargetCheck.Ok;
        return folderExists(root) ? new TargetCheck(TargetState.Missing) : new TargetCheck(TargetState.Unavailable);
    }

    /// <summary>"D:\" for D:\Games\x.exe, "\\nas\share\" for a share; null for anything else (a relative path).</summary>
    public static string? RootOf(string path)
    {
        if (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\') return path[..3];
        if (!path.StartsWith(@"\\", StringComparison.Ordinal)) return null;
        var parts = path[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $@"\\{parts[0]}\{parts[1]}\" : null;
    }

    /// <summary>A share path (\\server\share\…): its checks may hang for many seconds, so they get a timeout.</summary>
    public static bool IsNetworkPath(string path) => path.StartsWith(@"\\", StringComparison.Ordinal);
}

/// <summary>Which folders NeoFences watches for its targets (spec §4): a fixed budget, the busiest folders first.</summary>
public static class WatchPlan
{
    public const int MaxFolders = 64;

    /// <summary>
    /// The parent folders of these targets, the one holding the most targets first (ties: the first seen), at most
    /// <paramref name="maxFolders"/>. A drive root has no parent and is not watched; its items are re-checked.
    /// </summary>
    public static IReadOnlyList<string> Folders(IEnumerable<string> pathTargets, int maxFolders = MaxFolders) =>
        pathTargets.Select(ParentOf).OfType<string>()
            .Select((folder, order) => (folder, order))
            .GroupBy(entry => entry.folder, ItemKinds.Comparer)
            .OrderByDescending(group => group.Count()).ThenBy(group => group.First().order)
            .Take(maxFolders)
            .Select(group => group.First().folder)
            .ToList();

    /// <summary>"D:\Games" for "D:\Games\x.exe" and for "D:\Games\x\" (a folder target); null for a root.</summary>
    public static string? ParentOf(string path)
    {
        var trimmed = path.TrimEnd('\\');
        if (TargetChecks.RootOf(trimmed + "\\") is not { } root || ItemKinds.Comparer.Equals(root.TrimEnd('\\'), trimmed)) return null;
        var parent = trimmed[..trimmed.LastIndexOf('\\')];
        return parent.Length == 2 ? parent + "\\" : parent; // a file right on a drive: "D:\"
    }
}

/// <summary>At most one refresh per fence every <see cref="Interval"/> (spec §4): bursts of changes wait and come once.</summary>
public sealed class RefreshThrottle
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly Dictionary<string, DateTimeOffset> _lastRefresh = new(StringComparer.Ordinal);

    /// <summary>How long a fence with changes waits before its refresh: zero when its last one was long enough ago.</summary>
    public TimeSpan DelayFor(string fenceId, DateTimeOffset now) =>
        _lastRefresh.TryGetValue(fenceId, out var last) && now - last < Interval ? Interval - (now - last) : TimeSpan.Zero;

    public void Refreshed(string fenceId, DateTimeOffset now) => _lastRefresh[fenceId] = now;
}
