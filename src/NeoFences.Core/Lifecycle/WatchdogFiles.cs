namespace NeoFences.Core.Lifecycle;

/// <summary>
/// The watchdog's per-process files (M33 <c>watchdog-&lt;pid&gt;</c>) left by a power cut: NeoFences removes those of
/// processes that no longer run, at start (M34). Pure: the caller lists the data folder and asks Windows.
/// </summary>
public static class WatchdogFiles
{
    private const string Prefix = "watchdog-";

    public static IReadOnlyList<string> Stale(IEnumerable<string> fileNames, Func<int, bool> isRunning) =>
        [.. fileNames.Where(name => name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
                                    && int.TryParse(name.AsSpan(Prefix.Length), System.Globalization.NumberStyles.None, null, out var processId)
                                    && !isRunning(processId))];
}
