using System.Globalization;
using System.Text.RegularExpressions;

namespace NeoFences.Core.Lifecycle;

/// <summary>One NeoFences start as its log tells it (M37): the timing marks and the frame statistics after it.</summary>
public sealed record PerfRun(int Pid, int? FencesShownMs, int? IconsSettledMs, int IconRequests, int FromCache, int Frames, double WorstFrameMs, int SlowFrames);

/// <summary>
/// The timing marks NeoFences writes to its log (M37, spec §1) and the reader the measurement script uses (it loads this
/// assembly). The texts and the reader live together so they cannot drift apart.
/// </summary>
public static partial class PerfLog
{
    public static string FencesShownText(long ms, int windows) => $"timing: fences shown after {ms} ms ({windows} windows)";

    public static string IconsSettledText(long ms, int requests, int fromCache) =>
        $"timing: icons settled after {ms} ms ({requests} requests, {fromCache} from the cache)";

    public static string FramesText(int frames, double worstMs, int slow) =>
        string.Create(CultureInfo.InvariantCulture, $"timing: frames {frames}, worst {worstMs:0.#} ms, over 33 ms {slow}");

    /// <summary>One run per "NeoFences starting" line; marks before the first start are ignored.</summary>
    public static IReadOnlyList<PerfRun> Read(IEnumerable<string> lines)
    {
        var runs = new List<PerfRun>();
        PerfRun? run = null;
        foreach (var line in lines)
        {
            if (StartLine().Match(line) is { Success: true } start)
            {
                if (run is not null) runs.Add(run);
                run = new PerfRun(int.Parse(start.Groups[1].Value, CultureInfo.InvariantCulture), null, null, 0, 0, 0, 0, 0);
                continue;
            }
            if (run is null) continue;
            if (FencesLine().Match(line) is { Success: true } fences)
                run = run with { FencesShownMs = int.Parse(fences.Groups[1].Value, CultureInfo.InvariantCulture) };
            else if (IconsLine().Match(line) is { Success: true } icons)
                run = run with
                {
                    IconsSettledMs = int.Parse(icons.Groups[1].Value, CultureInfo.InvariantCulture),
                    IconRequests = int.Parse(icons.Groups[2].Value, CultureInfo.InvariantCulture),
                    FromCache = int.Parse(icons.Groups[3].Value, CultureInfo.InvariantCulture),
                };
            else if (FramesLine().Match(line) is { Success: true } frames)
                run = run with
                {
                    Frames = run.Frames + int.Parse(frames.Groups[1].Value, CultureInfo.InvariantCulture),
                    WorstFrameMs = Math.Max(run.WorstFrameMs, double.Parse(frames.Groups[2].Value, CultureInfo.InvariantCulture)),
                    SlowFrames = run.SlowFrames + int.Parse(frames.Groups[3].Value, CultureInfo.InvariantCulture),
                };
        }
        if (run is not null) runs.Add(run);
        return runs;
    }

    /// <summary>The frame statistics logged between two times (a scroll, a drag), summed; the worst frame of them.</summary>
    public static (int Frames, double WorstMs, int Slow) FramesBetween(IEnumerable<string> lines, DateTimeOffset from, DateTimeOffset to)
    {
        var (frames, worst, slow) = (0, 0.0, 0);
        foreach (var line in lines)
        {
            if (line.Length < 30 || !DateTimeOffset.TryParseExact(line[..30], "yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
                || at < from || at > to || FramesLine().Match(line) is not { Success: true } match) continue;
            frames += int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            worst = Math.Max(worst, double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
            slow += int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        }
        return (frames, worst, slow);
    }

    [GeneratedRegex(@"\[INF\] NeoFences starting, pid (\d+)")]
    private static partial Regex StartLine();

    [GeneratedRegex(@"\[INF\] timing: fences shown after (\d+) ms")]
    private static partial Regex FencesLine();

    [GeneratedRegex(@"\[INF\] timing: icons settled after (\d+) ms \((\d+) requests, (\d+) from the cache\)")]
    private static partial Regex IconsLine();

    [GeneratedRegex(@"\[INF\] timing: frames (\d+), worst ([\d.]+) ms, over 33 ms (\d+)")]
    private static partial Regex FramesLine();
}
