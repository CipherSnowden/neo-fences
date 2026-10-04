using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace NeoFences.Spikes.M0;

/// <summary>Spike for ADR-005: a second copy of the exe restores desktop icons if the main process dies uncleanly.</summary>
public static class Watchdog
{
    private static string RestartLogPath => Path.Combine(Lab.DataDir, "restarts.txt");

    private static string MarkerPath(int processId) => Path.Combine(Lab.DataDir, $"clean-shutdown-{processId}");

    public static void Spawn()
    {
        File.Delete(MarkerPath(Environment.ProcessId)); // stale marker from an earlier process with the same PID
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--watchdog {Environment.ProcessId}") { UseShellExecute = false });
        Lab.Log("watchdog spawned");
    }

    public static void MarkCleanShutdown()
    {
        Directory.CreateDirectory(Lab.DataDir);
        File.WriteAllText(MarkerPath(Environment.ProcessId), DateTimeOffset.Now.ToString("O"));
    }

    /// <summary>Runs in the watchdog process (STA, no windows). Returns when the main process has exited.</summary>
    public static void Run(int mainProcessId)
    {
        Lab.Log($"watchdog: watching {mainProcessId}");
        try
        {
            using var mainProcess = Process.GetProcessById(mainProcessId);
            mainProcess.WaitForExit();
        }
        catch (ArgumentException)
        {
            Lab.Log("watchdog: main process already gone");
        }

        var marker = MarkerPath(mainProcessId);
        if (File.Exists(marker))
        {
            File.Delete(marker);
            Lab.Log("watchdog: clean shutdown, nothing to do");
            return;
        }

        Lab.Log("watchdog: UNCLEAN exit -> restoring desktop icons");
        DesktopIcons.TrySetHidden(false);

        var now = DateTimeOffset.Now;
        if (!RestartThrottle.ShouldRestart(recentRestarts: ReadRestarts(), now: now))
        {
            Lab.Log("watchdog: restart limit reached (3 per 10 min), staying down");
            return;
        }
        File.AppendAllText(RestartLogPath, now.ToString("O") + Environment.NewLine);
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false });
        Lab.Log("watchdog: main restarted");
    }

    private static List<DateTimeOffset> ReadRestarts() =>
        File.Exists(RestartLogPath)
            ? File.ReadAllLines(RestartLogPath)
                .Select(line => DateTimeOffset.TryParse(line, CultureInfo.InvariantCulture, DateTimeStyles.None, out var restartTime) ? restartTime : (DateTimeOffset?)null)
                .OfType<DateTimeOffset>()
                .ToList()
            : [];
}
