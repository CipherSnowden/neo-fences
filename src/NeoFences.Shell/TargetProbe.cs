using NeoFences.Core.Items;

namespace NeoFences.Shell;

/// <summary>Where a target is right now (M18 spec §4): the disk asked off the UI thread, a network share with a timeout.</summary>
public static class TargetProbe
{
    public static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Call off the UI thread. A share that does not answer within <see cref="NetworkTimeout"/> is Unavailable.</summary>
    public static TargetCheck Check(string target)
    {
        if (ItemKinds.Of(target) != ItemKind.Path) return TargetCheck.Ok;
        if (!TargetChecks.IsNetworkPath(target)) return Classify(target);
        // ponytail: a share that never answers keeps one pool thread blocked until Windows gives up; a probe thread per share if that piles up.
        var probe = Task.Run(() => Classify(target));
        return probe.Wait(NetworkTimeout) ? probe.Result : new TargetCheck(TargetState.Unavailable);
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
