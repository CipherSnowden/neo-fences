using System.Collections.Concurrent;
using NeoFences.Core.Items;

namespace NeoFences.Shell;

/// <summary>
/// Where targets are right now (M18 spec §4), asked off the UI thread. Each drive or share root is asked once per batch,
/// with a timeout for every kind of drive (a disconnected mapped drive hangs as long as a share): a root that does not
/// answer makes all its targets Unavailable without asking for each (final review I6).
/// </summary>
public static class TargetProbe
{
    public static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Root probes still waiting: a root that never answers is not asked again (and no more threads block) until it does.</summary>
    private static readonly ConcurrentDictionary<string, Task<bool>> PendingRoots = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>One target (Properties, opening an item). Call off the UI thread.</summary>
    public static TargetCheck Check(string target) => CheckAll([target])[0].Check;

    /// <summary>Every target's state, in order. Call off the UI thread.</summary>
    public static IReadOnlyList<(string Target, TargetCheck Check)> CheckAll(IReadOnlyList<string> targets)
    {
        var results = new List<(string, TargetCheck)>(targets.Count);
        var rootAnswers = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            // Websites, special items, launcher links (steam://…): nothing on a disk to ask.
            if (ItemKinds.Of(target) != ItemKind.Path || TargetChecks.RootOf(target) is not { } root)
            {
                results.Add((target, TargetCheck.Ok));
                continue;
            }
            if (!rootAnswers.TryGetValue(root, out var answers)) rootAnswers[root] = answers = RootAnswers(root);
            results.Add((target, answers ? Classify(target) : new TargetCheck(TargetState.Unavailable)));
        }
        return results;
    }

    /// <summary>A file or folder is at the path now (settling renames). Call off the UI thread.</summary>
    public static bool Exists(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The drive or share is there and answers within <see cref="NetworkTimeout"/>.</summary>
    private static bool RootAnswers(string root)
    {
        // ponytail: a probe that never returns keeps one pool thread until Windows gives up; it is never started twice.
        var probe = PendingRoots.GetOrAdd(root, key => Task.Run(() => Exists(key)));
        if (!probe.Wait(NetworkTimeout)) return false; // still pending: the next batch does not start another one
        PendingRoots.TryRemove(new KeyValuePair<string, Task<bool>>(root, probe));
        return probe.Result;
    }

    private static TargetCheck Classify(string target)
    {
        try
        {
            return TargetChecks.Classify(target, File.Exists, Directory.Exists);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new TargetCheck(TargetState.Unavailable); // never a crash for one odd path (hard rule 7)
        }
    }
}
