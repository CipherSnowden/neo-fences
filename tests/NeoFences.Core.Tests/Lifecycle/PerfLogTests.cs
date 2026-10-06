using NeoFences.Core.Lifecycle;

namespace NeoFences.Core.Tests.Lifecycle;

/// <summary>M37 (spec §1): the timing marks NeoFences logs, read back for the measurement report.</summary>
public class PerfLogTests
{
    private static readonly string[] Lines =
    [
        "2026-10-06 17:56:57.193 +05:30 [INF] NeoFences starting, pid 11092, OS 10.0.26200.0",
        "2026-10-06 17:56:57.933 +05:30 [INF] desktop gestures: WH_MOUSE_LL installed",
        "2026-10-06 17:56:58.020 +05:30 [INF] timing: fences shown after 812 ms (50 windows)",
        "2026-10-06 17:56:59.900 +05:30 [INF] timing: icons settled after 2690 ms (493 requests, 410 from the cache)",
        "2026-10-06 17:57:10.000 +05:30 [INF] timing: frames 120, worst 48 ms, over 33 ms 3",
        "2026-10-06 17:57:12.000 +05:30 [INF] timing: frames 118, worst 21 ms, over 33 ms 0",
        "2026-10-06 17:58:14.656 +05:30 [INF] NeoFences starting, pid 5656, OS 10.0.26200.0",
        "2026-10-06 17:58:15.300 +05:30 [INF] timing: fences shown after 640 ms (50 windows)",
        "   at a stack trace line that is not a log line",
    ];

    [Fact]
    public void Read_GivesOneRunPerStart_WithItsMarksAndFrames()
    {
        var runs = PerfLog.Read(Lines);
        Assert.Equal(2, runs.Count);
        var first = runs[0];
        Assert.Equal(11092, first.Pid);
        Assert.Equal((812, 2690, 410, 493), (first.FencesShownMs, first.IconsSettledMs, first.FromCache, first.IconRequests));
        Assert.Equal((238, 48.0, 3), (first.Frames, first.WorstFrameMs, first.SlowFrames));
        var second = runs[1];
        Assert.Equal((640, (int?)null, 0), (second.FencesShownMs, second.IconsSettledMs, second.Frames));
    }

    [Fact]
    public void Read_IgnoresMarksBeforeAnyStart_AndEmptyInput()
    {
        Assert.Empty(PerfLog.Read([]));
        Assert.Empty(PerfLog.Read(["2026-10-06 17:56:58.020 +05:30 [INF] timing: fences shown after 812 ms (50 windows)"]));
    }

    [Fact]
    public void FramesBetween_SumsOnlyTheFrameLinesInsideTheTimes()
    {
        var from = new DateTimeOffset(2026, 10, 6, 17, 57, 9, TimeSpan.FromHours(5.5));
        var (frames, worst, slow) = PerfLog.FramesBetween(Lines, from, from.AddSeconds(4));
        Assert.Equal((238, 48.0, 3), (frames, worst, slow));
        Assert.Equal((120, 48.0, 3), PerfLog.FramesBetween(Lines, from, from.AddSeconds(1.5)));
        Assert.Equal((0, 0.0, 0), PerfLog.FramesBetween(Lines, from.AddHours(1), from.AddHours(2)));
    }

    [Fact]
    public void Marks_AreWrittenAsTheReaderExpects()
    {
        var lines = new[]
        {
            "2026-10-06 18:00:00.000 +05:30 [INF] NeoFences starting, pid 7, OS 10",
            "2026-10-06 18:00:01.000 +05:30 [INF] " + PerfLog.FencesShownText(ms: 700, windows: 4),
            "2026-10-06 18:00:02.000 +05:30 [INF] " + PerfLog.IconsSettledText(ms: 1500, requests: 30, fromCache: 28),
            "2026-10-06 18:00:03.000 +05:30 [INF] " + PerfLog.FramesText(frames: 60, worstMs: 35.4, slow: 1),
        };
        var run = PerfLog.Read(lines).Single();
        Assert.Equal((700, 1500, 28, 30, 60, 35.4, 1), (run.FencesShownMs, run.IconsSettledMs, run.FromCache, run.IconRequests, run.Frames, run.WorstFrameMs, run.SlowFrames));
    }
}
