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

    /// <summary>
    /// Root probes still waiting, with when they started: a root that never answers is not asked again until it does, and
    /// later callers wait only what is left of the first one's timeout (M19 review I1).
    /// </summary>
    private static readonly ConcurrentDictionary<string, (Task<bool> Probe, DateTimeOffset Started)> PendingRoots = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>One target (Properties, opening an item). Call off the UI thread.</summary>
    public static TargetCheck Check(string target) => CheckAll([target])[0].Check;

    /// <summary>Every target's state, in order. Call off the UI thread.</summary>
    public static IReadOnlyList<(string Target, TargetCheck Check)> CheckAll(IReadOnlyList<string> targets)
    {
        var results = new List<(string, TargetCheck)>(targets.Count);
        var rootAnswers = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            // An app (M19): Missing once Windows no longer knows it; no disk, no timeout needed.
            if (ItemKinds.IsApp(target))
            {
                results.Add((target, TargetChecks.Classify(target, File.Exists, Directory.Exists, appExists: AppList.Exists)));
                continue;
            }
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

    /// <summary>
    /// A path whose shell calls may hang for many seconds: a share (<c>\\server\share</c>) or a mapped network drive letter
    /// (M19 review I2). A local disk, even a sleeping one, is not. Never touches the path itself.
    /// </summary>
    public static bool MayHang(string path)
    {
        if (TargetChecks.IsNetworkPath(path)) return true;
        if (TargetChecks.RootOf(path) is not { } root) return false;
        try
        {
            return new DriveInfo(root).DriveType == DriveType.Network; // GetDriveType: answers at once, also when disconnected
        }
        catch (Exception failure) when (failure is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The drive or share is there and answers within <see cref="NetworkTimeout"/> of its probe's start.</summary>
    private static bool RootAnswers(string root)
    {
        // ponytail: a probe that never returns keeps one pool thread until Windows gives up; it is never started twice.
        var pending = PendingRoots.GetOrAdd(root, key => (Task.Run(() => Exists(key)), DateTimeOffset.UtcNow));
        // Still pending past its timeout: Unavailable at once, so twenty items on a dead share cost 2 s, not 40 (M19 review I1).
        if (!pending.Probe.Wait(TargetChecks.ProbeWait(pending.Started, DateTimeOffset.UtcNow, NetworkTimeout))) return false;
        PendingRoots.TryRemove(new KeyValuePair<string, (Task<bool>, DateTimeOffset)>(root, pending));
        return pending.Probe.Result;
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
